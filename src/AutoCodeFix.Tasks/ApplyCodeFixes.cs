using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AutoCodeFix.Properties;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Xamarin.Build;

namespace AutoCodeFix
{
    public class ApplyCodeFixes : AsyncTask, IBuildConfiguration
    {
        static readonly Version MinRoslynVersion = new Version(1, 2);

        [Required]
        public string ProjectFullPath { get; set; }

        /// <summary>
        /// Gets or sets the paths to directories to search for dependencies.
        /// </summary>
        [Required]
        public ITaskItem[] AssemblySearchPath { get; set; }

        [Required]
        public string MSBuildBinPath { get; set; }

        [Required]
        public string ToolsPath { get; set; }

        [Required]
        public ITaskItem[] AutoFixIds { get; set; }

        //[Required]
        public ITaskItem[] Analyzers { get; set; }

        public ITaskItem[] AdditionalFiles { get; set; }

        public ITaskItem[] NoWarn { get; set; }

        public ITaskItem[] WarningsAsErrors { get; set; }

        public string CodeAnalysisRuleSet { get; set; }

        public bool BuildingInsideVisualStudio { get; set; }

        /// <summary>
        /// Whether to debug debug the task for troubleshooting purposes.
        /// </summary>
        public bool DebugAutoCodeFix { get; set; }

        /// <summary>
        /// Whether to cause the project reader console program to launch a debugger on run 
        /// for troubleshooting purposes.
        /// </summary>
        public bool DebugProjectReader { get; set; }

        [Output]
        public ITaskItem[] GeneratedFiles { get; set; }

        IDictionary<string, string> IBuildConfiguration.GlobalProperties => BuildEngine.GetGlobalProperties();

        public override bool Execute()
        {
            if (DebugAutoCodeFix)
                Debugger.Launch();

            if (AutoFixIds?.Length == 0)
                return true;

            using (var resolver = new AssemblyResolver(AssemblySearchPath, (i, m) => Log.LogMessage(i, m)))
            {
                Task.Run(ExecuteAsync).ConfigureAwait(false);
                return base.Execute();
            }
        }

        private async Task ExecuteAsync()
        {
            try
            {
                var watch = Stopwatch.StartNew();
                var workspace = this.GetWorkspace();
                var project = await workspace.GetOrAddProjectAsync(ProjectFullPath, (i, m) => LogMessage(m, i), Token);
                watch.Stop();

                LogMessage($"Loaded {project.Name} in {watch.Elapsed.TotalSeconds} seconds", MessageImportance.Low);

                var fixableIds = new HashSet<string>(AutoFixIds.Select(x => x.ItemSpec));

                watch = Stopwatch.StartNew();

                // We don't warn again, since loading the BuildWorkspace will already have done that.
                var analyzers = this.LoadAnalyzers(warn: false)
                    .Concat(MefHostServices.DefaultAssemblies)
                    .SelectMany(GetTypes)
                    .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                    .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
                    .Select(CreateAnalyzer)
                    // Only keep the analyzers that can fix the diagnostics we were given.
                    .Where(d => d != null && d.SupportedDiagnostics.Any(s => fixableIds.Contains(s.Id)))
                    .ToImmutableArray();

                watch.Stop();
                LogMessage($"Loaded applicable analyzers in {watch.Elapsed.TotalSeconds} seconds", MessageImportance.Low);

                Token.ThrowIfCancellationRequested();

                watch = Stopwatch.StartNew();

                // We filter all available codefix providers to only those that support the project language and can 
                // fix any of the generator diagnostic codes we received. 
                var allProviders = project.Solution.Workspace.Services.HostServices
                    .GetExports<CodeFixProvider, IDictionary<string, object>>()
                    .Where(x => ((string[])x.Metadata["Languages"]).Contains(project.Language))
                    .SelectMany(x => x.Value.FixableDiagnosticIds.Select(id => new { Id = id, Provider = x.Value }))
                    .Where(x => fixableIds.Contains(x.Id))
                    .GroupBy(x => x.Id)
                    //.Select(x => new { Id = x.Key, Provider = x.Select(p => p.Provider).First() })
                    .ToDictionary(x => x.Key, x => x.Select(y => y.Provider).ToArray());
                
                watch.Stop();
                LogMessage($"Loaded applicable code fix providers in {watch.Elapsed.TotalSeconds} seconds", MessageImportance.Low);

                Token.ThrowIfCancellationRequested();

                // Report errors for the fixable ids that don't have a corresponding codefix provider.
                var unfixable = fixableIds.Where(id => !allProviders.ContainsKey(id)).ToArray();
                if (unfixable.Any())
                {
                    LogCodedError(nameof(Resources.ACF005), Resources.ACF005, string.Join(", ", unfixable));
                    Cancel();
                    return;
                }

                fixableIds = new HashSet<string>(fixableIds.Where(id => allProviders.ContainsKey(id)));
                var additionalFiles = ImmutableArray.Create(AdditionalFiles == null ? Array.Empty<AdditionalText>() :
                    AdditionalFiles.Select(x => AdditionalTextFile.Create(x.GetMetadata("FullPath"))).ToArray());

                // Process diagnostic options and rule set
                var diagnosticOptions = new Dictionary<string, ReportDiagnostic>();
                var reportDiagnostic = ReportDiagnostic.Default;
                if (!string.IsNullOrEmpty(CodeAnalysisRuleSet))
                {
                    reportDiagnostic = RuleSet.GetDiagnosticOptionsFromRulesetFile(CodeAnalysisRuleSet, out diagnosticOptions);
                }

                // Explicitly supress the diagnostics in NoWarn
                foreach (var noWarn in NoWarn)
                {
                    diagnosticOptions[noWarn.ItemSpec] = ReportDiagnostic.Suppress;
                }
                // Explicitly report as errors the specificied WarningsAsErrors
                foreach (var warnAsError in WarningsAsErrors)
                {
                    diagnosticOptions[warnAsError.ItemSpec] = ReportDiagnostic.Error;
                }

                // Merge with existing compilation options
                var immutableOptions = diagnosticOptions.ToImmutableDictionary().SetItems(project.CompilationOptions.SpecificDiagnosticOptions);
                var compilationOptions = project.CompilationOptions.WithSpecificDiagnosticOptions(immutableOptions);
                // Apply to solution and update project reference.

                if (!workspace.TryApplyChanges(project.Solution.WithProjectCompilationOptions(project.Id, compilationOptions)))
                {
                    throw new NotSupportedException();
                }

                project = workspace.CurrentSolution.GetProject(project.Id);

                watch = Stopwatch.StartNew();

                var options = new AnalyzerOptions(additionalFiles);

                async Task<(Diagnostic, CodeFixProvider[])> GetNextFixableDiagnostic()
                {
                    var compilation = await project.GetCompilationAsync(Token);
                    
                    var analyzed = compilation.WithAnalyzers(analyzers, options);
                    var diagnostics = await analyzed.GetAnalyzerDiagnosticsAsync(analyzers, Token);
                    var nextDiagnostic = diagnostics.FirstOrDefault(d => fixableIds.Contains(d.Id));
                    var nextProviders = nextDiagnostic == null ? null : allProviders[nextDiagnostic.Id];

                    return (nextDiagnostic, nextProviders);
                }

                var (diagnostic, providers) = await GetNextFixableDiagnostic();

                while (diagnostic != null && providers?.Length != 0)
                {
                    var document = project.GetDocument(diagnostic.Location.SourceTree);
                    CodeAction codeAction = null;

                    var fixApplied = false;

                    foreach (var provider in providers)
                    {
                        // TODO: add support for using the provider.GetFixAllProvider() if one is returned, 
                        // which should boost performance when the FixAllProvider is tunned for performance.

                        await provider.RegisterCodeFixesAsync(
                            new CodeFixContext(document, diagnostic,
                            (action, diag) => codeAction = action,
                            Token));

                        if (codeAction == null)
                            continue;

                        try
                        {
                            var operations = await codeAction.GetOperationsAsync(Token);
                            var applyChanges = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
                            if (applyChanges != null)
                            {
                                applyChanges.Apply(workspace, Token);
                                fixApplied = true;

                                watch.Stop();
                                LogMessage($"Fixed {diagnostic.Id} in {watch.Elapsed.Milliseconds} milliseconds", MessageImportance.Low);

                                project = await workspace.GetOrAddProjectAsync(ProjectFullPath, (i, m) => LogMessage(m, i), Token);

                                // We successfully applied one code action for the given diagnostics, 
                                // consider it fixed even if there are other providers.
                                break;
                            }
                            else
                            {
                                Log.LogError(
                                    nameof(AutoCodeFix),
                                    nameof(Resources.ACF008),
                                    diagnostic.Location.GetLineSpan().Path,
                                    diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1,
                                    diagnostic.Location.GetLineSpan().StartLinePosition.Character + 1,
                                    diagnostic.Location.GetLineSpan().EndLinePosition.Line + 1,
                                    diagnostic.Location.GetLineSpan().EndLinePosition.Character + 1,
                                    Resources.ACF008,
                                    codeAction.Title, diagnostic.Id, diagnostic.GetMessage());
                                Cancel();
                            }
                        }
                        catch (Exception e)
                        {
                            Log.LogError(
                                nameof(AutoCodeFix),
                                nameof(Resources.ACF006),
                                diagnostic.Location.GetLineSpan().Path,
                                diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1,
                                diagnostic.Location.GetLineSpan().StartLinePosition.Character + 1,
                                diagnostic.Location.GetLineSpan().EndLinePosition.Line + 1,
                                diagnostic.Location.GetLineSpan().EndLinePosition.Character + 1,
                                Resources.ACF006,
                                codeAction.Title, diagnostic.Id, diagnostic.GetMessage(), e);
                            Cancel();
                        }
                    }

                    if (!fixApplied)
                    {
                        Log.LogError(
                            nameof(AutoCodeFix),
                            nameof(Resources.ACF007),
                            null,
                            diagnostic.Location.GetLineSpan().Path,
                            diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1,
                            diagnostic.Location.GetLineSpan().StartLinePosition.Character + 1,
                            diagnostic.Location.GetLineSpan().EndLinePosition.Line + 1,
                            diagnostic.Location.GetLineSpan().EndLinePosition.Character + 1,
                            Resources.ACF007,
                            diagnostic.Id, diagnostic.GetMessage());
                        Cancel();
                    }

                    watch = Stopwatch.StartNew();

                    (diagnostic, providers) = await GetNextFixableDiagnostic();
                }
            }
            catch (Exception e)
            {
                LogErrorFromException(e);
            }
            finally
            {
                Complete();
            }
        }

        IEnumerable<Type> GetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (Exception e)
            {
                Log.LogWarning(
                    nameof(AutoCodeFix),
                    nameof(Resources.ACF003),
                    null, null, 0, 0, 0, 0,
                    Resources.ACF003,
                    assembly.FullName, e);
                return Enumerable.Empty<Type>();
            }
        }

        DiagnosticAnalyzer CreateAnalyzer(Type type)
        {
            try
            {
                return (DiagnosticAnalyzer)Activator.CreateInstance(type);
            }
            catch (TargetInvocationException tie)
            {
                Log.LogWarning(
                    nameof(AutoCodeFix),
                    nameof(Resources.ACF004),
                    null, null, 0, 0, 0, 0,
                    Resources.ACF004,
                    type.FullName, tie.InnerException);

                return null;
            }
        }

        class AdditionalTextFile : AdditionalText
        {
            public AdditionalTextFile(string path) => Path = path;

            public override string Path { get; }

            public static AdditionalText Create(string path) => new AdditionalTextFile(path);

            public override SourceText GetText(CancellationToken cancellationToken = default)
                => SourceText.From(File.ReadAllText(Path));
        }
    }
}
