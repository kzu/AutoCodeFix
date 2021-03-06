﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StreamJsonRpc;

namespace AutoCodeFix
{
    internal class ProjectReader : IDisposable
    {
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
                process.Kill();

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

        public async Task<dynamic> OpenProjectAsync(string projectFullPath, CancellationToken cancellation)
        {
            EnsureRunning();
            await initializer;
            return await rpc.InvokeWithCancellationAsync<dynamic>(nameof(OpenProjectAsync), new[] { projectFullPath }, cancellation);
        }

        public async Task CloseWorkspaceAsync()
        {
            if (process != null && !process.HasExited)
            {
                try
                {
                    await rpc.InvokeAsync(nameof(CloseWorkspaceAsync));
                }
                catch { }
            }
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