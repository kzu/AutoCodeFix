using System.Collections.Generic;
using Microsoft.Build.Framework;

namespace AutoCodeFix
{
    internal interface IBuildConfiguration
    {
        IBuildEngine4 BuildEngine4 { get; }

        bool BuildingInsideVisualStudio { get; }

        IDictionary<string, string> GlobalProperties { get; }

        string MSBuildBinPath { get; }

        string ToolsPath { get; }

        bool DebugProjectReader { get; }

        void LogMessage(string message, MessageImportance importance);

        //TaskLoggingHelper Log { get; }
    }
}
