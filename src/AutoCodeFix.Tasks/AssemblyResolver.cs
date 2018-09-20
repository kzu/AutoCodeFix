using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Build.Framework;

namespace AutoCodeFix
{
    /// <summary>
    /// Performs initial assembly resolving from the local execution path, as well 
    /// as per-task resolving when constructed.
    /// </summary>
    public class AssemblyResolver : IDisposable
    {
        readonly Action<MessageImportance, string> logMessage;

        /// <summary>
        /// Initial resolve hookup to allow initial load to succeed.
        /// </summary>
        static AssemblyResolver()
            => AppDomain.CurrentDomain.AssemblyResolve += OnInitialAssemblyResolve;

        /// <summary>
        /// Forces static initialization/hookup to <see cref="AppDomain.AssemblyResolve"/>.
        /// </summary>
        public static void Init() { }

        /// <summary>
        /// Initial probing is naive, just looking for assemblies alongside the 
        /// currently executing assembly.
        /// </summary>
        static Assembly OnInitialAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var directory = Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath);
            var assemblyFile = Path.Combine(directory, new AssemblyName(args.Name).Name + ".dll");
            if (File.Exists(assemblyFile))
                return Assembly.LoadFrom(assemblyFile);

            return null;
        }

        /// <summary>
        /// Gets or sets the paths to directories to search for dependencies.
        /// </summary>
        public ITaskItem[] AssemblySearchPath { get; }

        public AssemblyResolver(ITaskItem[] assemblySearchPath, Action<MessageImportance, string> logMessage)
        {
            AssemblySearchPath = assemblySearchPath ?? new ITaskItem[0];
            this.logMessage = logMessage;

            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            // Once we're successfully loaded, don't resolve copy-local naively anymore.
            AppDomain.CurrentDomain.AssemblyResolve -= OnInitialAssemblyResolve;
        }

        private Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            logMessage(MessageImportance.Low, $"Resolving {args.Name}");

            var name = new AssemblyName(args.Name);
            var assembly = LoadAssembly(name);

            if (assembly != null)
                logMessage(MessageImportance.Low, $"Resolved to {assembly.FullName}");
            // We don't care about localized resource assembly loading failures
            else if (!name.Name.EndsWith(".resources"))
                logMessage(MessageImportance.Low, $"Assembly not found: {args.Name}");

            return assembly;
        }

        private Assembly LoadAssembly(AssemblyName assemblyName)
        {
            var searchedAssemblies = from pathItem in AssemblySearchPath
                                     let path = pathItem.GetMetadata("FullPath")
                                     where Directory.Exists(path)
                                     from file in Directory.EnumerateFiles(path, $"{assemblyName.Name}.dll", SearchOption.TopDirectoryOnly)
                                     select AssemblyName.GetAssemblyName(file);

            return searchedAssemblies
                // Be more strict and just allow same-major?
                .Where(name =>
                    (name.Version.Major >= assemblyName.Version.Major &&
                     name.Version.Minor >= assemblyName.Version.Minor))
                .Select(name =>
                {
                    logMessage(MessageImportance.Low, $"Loading {name.Name} from {new Uri(name.CodeBase).LocalPath}");
                    // LoadFrom should be compatible with how MSBuild itself loads custom tasks assemblies.
                    try
                    {
                        return Assembly.LoadFrom(new Uri(name.CodeBase).LocalPath);
                    }
                    catch (Exception e)
                    {
                        logMessage(MessageImportance.Normal, $"Failed to load {name.Name}: {e.Message}");
                        return null;
                    }
                })
                .FirstOrDefault();
        }

        /// <summary>
        /// Stop doing custom assembly resolution.
        /// </summary>
        public void Dispose()
            => AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssembly;
    }
}
