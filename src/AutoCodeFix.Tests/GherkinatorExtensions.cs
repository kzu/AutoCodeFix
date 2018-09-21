using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Gherkinator;
using Gherkinator.Sdk;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Xunit;

namespace AutoCodeFix
{
    public static class GherkinatorExtensions
    {
        public static ScenarioBuilder UseAutoCodeFix(this ScenarioBuilder builder)
            => builder.BeforeGiven(state =>
            {
                // We expand the properties, rather than setting them as global 
                // properties by setting the Dictionary<string, string> state, 
                // so that when files are preserved, we can open and build them 
                // from msbuild or VS.
                var dir = Path.Combine(state.GetTempDir(), "Directory.Build.props");
                Directory.CreateDirectory(state.GetTempDir());
                var props = new[]
                {
                    ("CurrentDirectory", Directory.GetCurrentDirectory() + "\\"),
                    ("DebugAutoCodeFix", Debugger.IsAttached.ToString()),
                    ("AutoCodeFixPath", Directory.GetCurrentDirectory() + "\\AutoCodeFix\\"),
                    ("AutoCodeFixVersion", ThisAssembly.Metadata.PackageVersion),
                    ("RestoreIgnoreFailedSources", "true"),
                    //("RestoreSources", Environment.ExpandEnvironmentVariables(
                    //    @"%TEMP%\packages;%USERPROFILE%\.nuget\packages;C:\Program Files\dotnet\sdk\NuGetFallbackFolder;https://api.nuget.org/v3/index.json"))
                };

                File.WriteAllText(dir, @"<Project>
    <PropertyGroup>
");
                File.AppendAllLines(dir, props.Select(x => $"\t\t<{x.Item1}>{x.Item2}</{x.Item1}>"));
                File.AppendAllText(dir, @"
    </PropertyGroup>
</Project>");
            });
                
            // Alternatively, we could have set them as MSBuild properties
            //=> state.GetOrSet<Dictionary<string, string>>()
            //    .Append("CurrentDirectory", Directory.GetCurrentDirectory() + "\\")
            //    .Append("AutoCodeFixPath", Directory.GetCurrentDirectory() + "\\AutoCodeFix\\")
            //    .Append("AutoCodeFixVersion", ThisAssembly.Metadata.PackageVersion)
            //    .Append("RestoreIgnoreFailedSources", "true")
            //    .Append("RestoreSources", Environment.ExpandEnvironmentVariables(
            //        @"%TEMP%\packages;%USERPROFILE%\.nuget\packages;C:\Program Files\dotnet\sdk\NuGetFallbackFolder;https://api.nuget.org/v3/index.json")));

        public static void AssertSuccess(this (BuildResult result, IEnumerable<BuildEventArgs>) build)
        {
            var project = CallContext.GetData("Build.Project", default(string));
            var target = CallContext.GetData("Build.Target", default(string));

            if (build.result.OverallResult != BuildResultCode.Success)
                CallContext.GetData<ScenarioState>().MSBuild().OpenLog(project, target);

            Assert.Equal(BuildResultCode.Success, build.result.OverallResult);
        }

        public static void AssertSuccess(this StepContext context, string project = null, string target = null)
        {
            project = project ?? CallContext.GetData("Build.Project", default(string));
            target = target ?? CallContext.GetData("Build.Target", default(string));

            var result = context.State.MSBuild().LastBuildResult;
            project = project ?? Path.GetFileName(result.ProjectStateAfterBuild.FullPath);
            target = target ?? result.ResultsByTarget.Keys.First();

            if (result.OverallResult != BuildResultCode.Success)
                context.State.MSBuild().OpenLog(project, target);

            Assert.Equal(BuildResultCode.Success, result.OverallResult);
        }

        public static TDictionary Append<TDictionary>(this TDictionary dictionary, string key, string value)
            where TDictionary : IDictionary<string, string>
        {
            dictionary.Add(key, value);
            return dictionary;
        }
    }
}
