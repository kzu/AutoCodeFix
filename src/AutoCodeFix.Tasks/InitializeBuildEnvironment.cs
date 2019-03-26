using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace AutoCodeFix
{
    public class InitializeBuildEnvironment : Task, IBuildConfiguration
    {
        /// <summary>
        /// Gets or sets the paths to directories to search for dependencies.
        /// </summary>
        [Required]
        public ITaskItem[] AssemblySearchPath { get; set; }

        public bool BuildingInsideVisualStudio { get; set; }

        /// <summary>
        /// Whether to cause the project reader console program to launch a debugger on run 
        /// for troubleshooting purposes.
        /// </summary>
        public bool DebugProjectReader { get; set; }

        [Required]
        public string MSBuildBinPath { get; set; }

        [Required]
        public string ToolsPath { get; set; }

        /// <summary>
        /// Logging verbosity for reporting.
        /// </summary>
        public string Verbosity { get; set; }

        IDictionary<string, string> IBuildConfiguration.GlobalProperties => BuildEngine.GetGlobalProperties();

        LoggerVerbosity? verbosity = null;

        public void LogMessage(string message, MessageImportance importance) => Log.LogMessage(importance.ForVerbosity(verbosity), message);

        public override bool Execute()
        {
            if (Verbosity != null && Verbosity.Length > 0)
            {
                if (!Enum.TryParse<LoggerVerbosity>(Verbosity, out var value))
                {
                    Log.LogError($"Invalid {nameof(Verbosity)} value '{Verbosity}'. Expected values: {string.Join(", ", Enum.GetNames(typeof(LoggerVerbosity)))}");
                    return false;
                }
                verbosity = value;
            }

            using (var resolver = new AssemblyResolver(AssemblySearchPath, (i, m) => Log.LogMessage(i, m)))
            {
                InitializeBuildWorkspace();
                InitializeProjectReader();
            }

            return true;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void InitializeBuildWorkspace()
        {
            var key = typeof(BuildWorkspace).FullName;
            var lifetime = BuildingInsideVisualStudio ?
                RegisteredTaskObjectLifetime.AppDomain :
                RegisteredTaskObjectLifetime.Build;

            if (!(BuildEngine4.GetRegisteredTaskObject(key, lifetime) is BuildWorkspace workspace))
            {
                var watch = Stopwatch.StartNew();
                workspace = new BuildWorkspace(this);
                BuildEngine4.RegisterTaskObject(key, workspace, lifetime, false);
                watch.Stop();
                LogMessage($"Initialized workspace in {watch.Elapsed.Milliseconds} milliseconds", MessageImportance.Low.ForVerbosity(verbosity));
            }

            // Register a per-build cleaner so we can cleanup the in-memory solution information.
            if (BuildEngine4.GetRegisteredTaskObject(key + ".Cleanup", RegisteredTaskObjectLifetime.Build) == null)
            {
                BuildEngine4.RegisterTaskObject(
                    key + ".Cleanup",
                    new DisposableAction(() => workspace.Cleanup()),
                    RegisteredTaskObjectLifetime.Build,
                    false);
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void InitializeProjectReader()
        {
            var key = typeof(ProjectReader).FullName;
            var lifetime = BuildingInsideVisualStudio ?
                RegisteredTaskObjectLifetime.AppDomain :
                RegisteredTaskObjectLifetime.Build;

            if (!(BuildEngine4.GetRegisteredTaskObject(key, lifetime) is ProjectReader reader))
            {
                LogMessage($"Initializing project reader...", MessageImportance.Low.ForVerbosity(verbosity));
                reader = new ProjectReader(MSBuildBinPath, ToolsPath, DebugProjectReader, BuildEngine.GetGlobalProperties());

                BuildEngine4.RegisterTaskObject(key, reader, lifetime, false);
            }

            // Register a per-build cleaner so we can cleanup the in-memory solution information.
            if (BuildEngine4.GetRegisteredTaskObject(key + ".Cleanup", RegisteredTaskObjectLifetime.Build) == null)
            {
                BuildEngine4.RegisterTaskObject(
                    key + ".Cleanup",
                    new DisposableAction(async () => await reader.CloseWorkspaceAsync()),
                    RegisteredTaskObjectLifetime.Build,
                    false);
            }
        }
    }
}
