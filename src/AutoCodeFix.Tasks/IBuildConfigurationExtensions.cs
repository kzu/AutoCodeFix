using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AutoCodeFix.Properties;

namespace AutoCodeFix
{
    static class IBuildConfigurationExtensions
    {
        static readonly Version MinRoslynVersion = new Version(1, 2);

        public static ProjectReader GetProjectReader(this IBuildConfiguration configuration)
            => ProjectReader.GetProjectReader(configuration);

        public static BuildWorkspace GetWorkspace(this IBuildConfiguration configuration)
            => BuildWorkspace.GetWorkspace(configuration);

        public static IEnumerable<Assembly> LoadAnalyzers(this IBuildConfiguration configuration, bool warn = true)
        {
            var analyzers = new List<Assembly>(configuration.Analyzers.Length);
            foreach (var item in configuration.Analyzers)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(item.GetMetadata("FullPath"));
                    var roslyn = assembly.GetReferencedAssemblies().FirstOrDefault(x => x.Name == "Microsoft.CodeAnalysis");
                    if (roslyn != null && roslyn.Version < MinRoslynVersion)
                    {
                        if (warn)
                        {
                            configuration.Log.LogWarning(
                            //configuration.BuildEngine4.LogWarningEvent(new BuildWarningEventArgs(
                                nameof(AutoCodeFix), 
                                nameof(Resources.ACF001), 
                                null, null, 0, 0, 0, 0,
                                Resources.ACF001,
                                item.ItemSpec, nameof(AutoCodeFix), MinRoslynVersion);
                        }
                    }
                    else
                    {
                        analyzers.Add(assembly);
                    }
                }
                catch (Exception e)
                {
                    configuration.Log.LogWarning(
                        nameof(AutoCodeFix), 
                        nameof(Resources.ACF002), 
                        null, null, 0, 0, 0, 0,
                        Resources.ACF002,
                        item.ItemSpec, e);
                }
            }

            return analyzers;
        }

    }
}
