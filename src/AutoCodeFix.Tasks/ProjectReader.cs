﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using StreamJsonRpc;

namespace AutoCodeFix
{
    internal class ProjectReader : IDisposable
    {
        public static ProjectReader GetProjectReader(IBuildConfiguration configuration)
        {
            var lifetime = configuration.BuildingInsideVisualStudio ?
                RegisteredTaskObjectLifetime.AppDomain :
                RegisteredTaskObjectLifetime.Build;
            var key = typeof(ProjectReader).FullName;
            if (!(configuration.BuildEngine4.GetRegisteredTaskObject(key, lifetime) is ProjectReader reader))
            {
                configuration.LogMessage($"Initializing project reader...", MessageImportance.Low);
                reader = new ProjectReader(configuration.MSBuildBinPath, configuration.ToolsPath, configuration.DebugProjectReader, configuration.GlobalProperties);
                
                configuration.BuildEngine4.RegisterTaskObject(key, reader, lifetime, false);
            }

            // Register a per-build cleaner so we can cleanup the in-memory solution information.
            if (configuration.BuildEngine4.GetRegisteredTaskObject(key + ".Cleanup", RegisteredTaskObjectLifetime.Build) == null)
            {
                configuration.BuildEngine4.RegisterTaskObject(
                    key + ".Cleanup",
                    new DisposableAction(async () => await reader.CloseWorkspaceAsync()),
                    RegisteredTaskObjectLifetime.Build,
                    false);
            }

            return reader;
        }

        private readonly string msBuildBinPath;
        private readonly bool debugConsole;
        private readonly string readerExe;
        private Process process;
        private JsonRpc rpc;
        private Task initializer;

        public ProjectReader(string msBuildBinPath, string toolsPath, bool debugConsole, IDictionary<string, string> globalProperties)
        {
            this.msBuildBinPath = msBuildBinPath;
            this.debugConsole = debugConsole;

            readerExe = new FileInfo(Path.Combine(toolsPath, "ProjectReader.exe")).FullName;

            if (!File.Exists(readerExe))
                throw new FileNotFoundException($"Did not find project reader tool at '{readerExe}'.", readerExe);

            EnsureRunning();
            initializer = Task.Run(async () => await CreateWorkspaceAsync(globalProperties));
        }

        private void EnsureRunning()
        {
            if (process == null || process.HasExited)
            {
                var args = "-parent=" + Process.GetCurrentProcess().Id +
                    // We pass down the -d flag so that the external process can also launch a debugger 
                    // for easy troubleshooting.
                    (debugConsole ? " -d " : " ") +
                    "-m=\"" + msBuildBinPath + "\"";

                process = Process.Start(new ProcessStartInfo(readerExe, args)
                {
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                });

                rpc = new JsonRpc(process.StandardInput.BaseStream, process.StandardOutput.BaseStream);
                rpc.StartListening();
            }
        }

        public void Dispose()
        {
            if (process != null && !process.HasExited)
                Task.Run(async () => await rpc.InvokeAsync("Exit"));

            process.WaitForExit();
            process = null;
        }

        public async Task Debug()
        {
            EnsureRunning();
            await rpc.InvokeAsync(nameof(Debug));
        }

        public async Task Exit()
        {
            if (process != null && !process.HasExited)
            {
                await rpc.InvokeAsync(nameof(Exit));
                process.WaitForExit();
            }

            process = null;
        }

        public async Task<bool> Ping()
        {
            EnsureRunning();
            return await rpc.InvokeAsync<bool>(nameof(Ping));
        }

        public async Task<dynamic> OpenProjectAsync(string projectFullPath)
        {
            EnsureRunning();
            await initializer;
            return await rpc.InvokeAsync<dynamic>(nameof(OpenProjectAsync), projectFullPath);
        }

        public async Task CloseWorkspaceAsync()
        {
            EnsureRunning();
            await rpc.InvokeAsync(nameof(CloseWorkspaceAsync));
        }

        private async Task CreateWorkspaceAsync(IDictionary<string, string> globalProperties)
        {
            EnsureRunning();
            // We never do codegen in the remote workspace
            await rpc.InvokeAsync(nameof(CreateWorkspaceAsync), new Dictionary<string, string>(globalProperties)
            {
                ["NoCodeGen"] = "true"
            });
        }
    }
}