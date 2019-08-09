using System;
using System.Collections.Concurrent;
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
using StyleCopTester;
using Xamarin.Build;
using Humanizer;

namespace AutoCodeFix
{
    public class ApplyCodeFixes : AsyncTask
    {
        static readonly Version MinRoslynVersion = new Version(1, 2);

        LoggerVerbosity? verbosity = null;

        [Required]
        public string ProjectFullPath { get; set; }

        /// <summary>
        /// Gets or sets the paths to directories to search for dependencies.
        /// </summary>
        [Required]
        public ITaskItem[] AssemblySearchPath { get; set; }

        [Required]
        public ITaskItem[] AutoCodeFixIds { get; set; }

        [Required]
        public string Language { get; set; }

        //[Required]
        public ITaskItem[] Analyzers { get; set; } = new ITaskItem[0];

        public ITaskItem[] SkipAnalyzers { get; set; } = new ITaskItem[0];

        public ITaskItem[] AdditionalFiles { get; set; } = new ITaskItem[0];

        public ITaskItem[] NoWarn { get; set; } = new ITaskItem[0];

        public ITaskItem[] WarningsAsErrors { get; set; } = new ITaskItem[0];

        public string PreprocessorSymbols { get; set; } = "";

        public string CodeAnalysisRuleSet { get; set; }

        public bool BuildingInsideVisualStudio { get; set; }

        /// <summary>
        /// Logging verbosity for reporting.
        /// </summary>
        public string Verbosity { get; set; } = LoggerVerbosity.Normal.ToString();

        [Output]
        public ITaskItem[] FailedAnalyzers { get; set; } = new ITaskItem[0];

        public override bool Execute()
        {
            if (AutoCodeFixIds?.Length == 0)
                return true;

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
                Task.Run(ExecuteAsync).ConfigureAwait(false);
                return base.Execute();
            }
        }

        private async Task ExecuteAsync()
        {
            try
            {
                LogMessage("Applying code fixes...", MessageImportance.Low.ForVerbosity(verbosity));
                var overallTime = Stopwatch.StartNew();
                var appliedFixes = new ConcurrentDictionary<string, int>();

                LogMessage("Getting Workspace...", MessageImportance.Low.ForVerbosity(verbosity));
                var workspace = BuildEngine4.GetRegisteredTaskObject<BuildWorkspace>(BuildingInsideVisualStudio);

                var watch = Stopwatch.StartNew();
                LogMessage("Getting Project...", MessageImportance.Low.ForVerbosity(verbosity));
                var project = await workspace.GetOrAddProjectAsync(
                    BuildEngine4.GetRegisteredTaskObject<ProjectReader>(BuildingInsideVisualStudio),
                    ProjectFullPath,
                    PreprocessorSymbols.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                    (i, m) => LogMessage(m, i), Token);

                // Locate our settings ini file
                var settings = AdditionalFiles.Select(x => x.GetMetadata("FullPath")).FirstOrDefault(x => x.EndsWith("AutoCodeFix.ini"));
                if (settings != null && !project.AdditionalDocuments.Any(d => d.FilePath != null && d.FilePath.Equals(settings)))
                {
                    Debug.Assert(workspace
                        .TryApplyChanges(project
                        .AddAdditionalDocument(Path.GetFileName(settings), File.ReadAllText(settings), filePath: settings)
                        .Project.Solution), 
                        "Failed to apply changes to workspace");

                    project = workspace.CurrentSolution.GetProject(project.Id);
                    Debug.Assert(project != null, "Failed to retrieve project from current workspace solution.");
                }

                watch.Stop();

                LogMessage($"Loaded {project.Name} in {TimeSpan.FromMilliseconds(watch.ElapsedMilliseconds).Humanize()}", MessageImportance.Low.ForVerbosity(verbosity));

                var fixableIds = new HashSet<string>(AutoCodeFixIds.Select(x => x.ItemSpec));

                watch.Restart();

                var analyzerAssemblies = LoadAnalyzers().Concat(MefHostServices.DefaultAssemblies).ToList();
                var failedAssemblies = new List<Assembly>();

                Dictionary<string, CodeFixProvider[]> allProviders = default;

                while (allProviders == null)
                {
                    // We filter all available codefix providers to only those that support the project language and can 
                    // fix any of the generator diagnostic codes we received. 

                    try
                    {
                        allProviders = MefHostServices.Create(analyzerAssemblies)
                            .GetExports<CodeFixProvider, IDictionary<string, object>>()
                            .Where(x => ((string[])x.Metadata["Languages"]).Contains(project.Language))
                            .SelectMany(x => x.Value.FixableDiagnosticIds.Select(id => new { Id = id, Provider = x.Value }))
                            .Where(x => fixableIds.Contains(x.Id))
                            .GroupBy(x => x.Id)
                            .ToDictionary(x => x.Key, x => x.Select(y => y.Provider).ToArray());
                        break;
                    }
                    catch (ReflectionTypeLoadException rle)
                    {
                        foreach (var assembly in rle.Types
                            .Where(t => t != null)
                            .GroupBy(t => t.Assembly)
                            .Select(g => g.Key))
                        {
                            Log.LogWarning(
                                nameof(AutoCodeFix),
                                nameof(Resources.ACF002),
                                null, null, 0, 0, 0, 0,
                                Resources.ACF002,
                                assembly.Location, "Skipping analyer for AutoCodeFix");

                            failedAssemblies.Add(assembly);
                            analyzerAssemblies.Remove(assembly);
                        }
                    }
                }

                FailedAnalyzers = failedAssemblies
                    .Select(asm => Analyzers.FirstOrDefault(i => i.GetMetadata("FullPath") == asm.Location))
                    .Where(i => i != null)
                    .Concat(SkipAnalyzers)
                    .ToArray();

                watch.Stop();
                LogMessage($"Loaded {allProviders.SelectMany(x => x.Value).Distinct().Count()} applicable code fix providers in {TimeSpan.FromMilliseconds(watch.ElapsedMilliseconds).Humanize()}", MessageImportance.Low.ForVerbosity(verbosity));

                Token.ThrowIfCancellationRequested();

                watch.Restart();

                var analyzers = analyzerAssemblies
                    .SelectMany(GetTypes)
                    .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                    .Where(t =>
                    {
                        try
                        {
                            return t.GetConstructor(Type.EmptyTypes) != null;
                        }
                        catch (TargetInvocationException tie)
                        {
                            Log.LogWarning(
                                nameof(AutoCodeFix),
                                nameof(Resources.ACF004),
                                null, null, 0, 0, 0, 0,
                                Resources.ACF004,
                                t.FullName, tie.InnerException);
                            return false;
                        }
                    })
                    .Select(CreateAnalyzer)
                    // Only keep the analyzers that can fix the diagnostics we were given.
                    .Where(d => d != null && d.SupportedDiagnostics.Any(s => fixableIds.Contains(s.Id)))
                    .ToImmutableArray();

                watch.Stop();
                LogMessage($"Loaded {analyzers.Length} applicable analyzers in {TimeSpan.FromMilliseconds(watch.ElapsedMilliseconds).Humanize()}", MessageImportance.Low.ForVerbosity(verbosity));

                Token.ThrowIfCancellationRequested();

                // Report errors for the fixable ids that don't have a corresponding codefix provider.
                var unfixable = fixableIds.Where(id => !allProviders.ContainsKey(id)).ToArray();
                if (unfixable.Any())
                {
                    LogCodedError(nameof(Resources.ACF005), Resources.ACF005, string.Join(", ", unfixable));
                    Cancel();
                    return;
                }

                // Report errors for the fixable ids that don't have a corresponding analyzer.
                unfixable = fixableIds.Where(id => !analyzers.Any(x => x.SupportedDiagnostics.Any(d => d.Id == id))).ToArray();
                if (unfixable.Any())
                {
                    LogCodedError(nameof(Resources.ACF010), Resources.ACF010, string.Join(", ", unfixable));
                    Cancel();
                    return;
                }

                fixableIds = new HashSet<string>(fixableIds.Where(id => allProviders.ContainsKey(id)));

                var compilationOptions = CreateCompilationOptions(project);
                if (!workspace.TryApplyChanges(project.Solution.WithProjectCompilationOptions(project.Id, compilationOptions)))
                {
                    throw new NotSupportedException("Workspace does not support supplying project compilation options.");
                }

                project = workspace.CurrentSolution.GetProject(project.Id);

                watch.Restart();

                var additionalFiles = ImmutableArray.Create(AdditionalFiles == null ? Array.Empty<AdditionalText>() :
                    AdditionalFiles
                        .Select(x => x.GetMetadata("FullPath"))
                        .Distinct()
                        .Select(x => AdditionalTextFile.Create(x)).ToArray());

                // TODO: upcoming editorconfig-powered options available to Analyzers would 
                // not work if we invoke them like this. See how the editor options can be 
                // retrieved and passed properly here. See https://github.com/dotnet/roslyn/projects/18
                var options = new AnalyzerOptions(additionalFiles);

                async Task<(ImmutableArray<Diagnostic>, Diagnostic, CodeFixProvider[])> GetNextFixableDiagnostic()
                {
                    var getNextWatch = Stopwatch.StartNew();
                    var compilation = await project.GetCompilationAsync(Token);

                    var analyzed = compilation.WithAnalyzers(analyzers, options);
                    var allDiagnostics = await analyzed.GetAnalyzerDiagnosticsAsync(analyzers, Token);
                    var nextDiagnostic = allDiagnostics.FirstOrDefault(d => fixableIds.Contains(d.Id));
                    var nextProviders = nextDiagnostic == null ? null : allProviders[nextDiagnostic.Id];

                    getNextWatch.Stop();
                    if (nextDiagnostic == null)
                        LogMessage($"Did not find more fixable diagnostics in {TimeSpan.FromMilliseconds(getNextWatch.ElapsedMilliseconds).Humanize()}", MessageImportance.Low.ForVerbosity(verbosity));
                    else
                        LogMessage($"Found fixable diagnostic {nextDiagnostic.Id} in {TimeSpan.FromMilliseconds(getNextWatch.ElapsedMilliseconds).Humanize()}", MessageImportance.Low.ForVerbosity(verbosity));

                    return (allDiagnostics, nextDiagnostic, nextProviders);
                }

                var (diagnostics, diagnostic, providers) = await GetNextFixableDiagnostic();

                while (diagnostic != null && providers?.Length != 0)
                {
                    var document = project.GetDocument(diagnostic.Location.SourceTree);
                    Debug.Assert(document != null, "Failed to locate document from diagnostic.");

                    CodeAction codeAction = null;
                    
                    var fixApplied = false;

                    foreach (var provider in providers)
                    {
                        // TODO: add support for using the provider.GetFixAllProvider() if one is returned, 
                        // which should boost performance when the FixAllProvider is tunned for performance.
                        // See https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/StyleCop.Analyzers/StyleCopTester/Program.cs#L314 
                        // for inspiration and in-depth knowledge of how it should be applied/supported.

                        await provider.RegisterCodeFixesAsync(
                            new CodeFixContext(document, diagnostic,
                            (action, diag) => codeAction = action,
                            Token));

                        if (codeAction == null)
                            continue;

                        // See if we can get a FixAll provider for the diagnostic we are trying to fix.
                        if (provider.GetFixAllProvider() is FixAllProvider fixAll &&
                            fixAll != null &&
                            fixAll.GetSupportedFixAllDiagnosticIds(provider).Contains(diagnostic.Id) && 
                            fixAll.GetSupportedFixAllScopes().Contains(FixAllScope.Project))
                        {
                            var group = await CodeFixEquivalenceGroup.CreateAsync(provider, ImmutableDictionary.CreateRange(new []
                            {
                                new KeyValuePair<ProjectId, ImmutableArray<Diagnostic>>(project.Id, diagnostics)
                            }), project.Solution, Token);

                            // TODO: should we only apply one equivalence group at a time? See https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/StyleCop.Analyzers/StyleCopTester/Program.cs#L330
                            if (group.Length > 0)
                            {
                                LogMessage($"Applying batch code fix for {diagnostic.Id}: {diagnostic.Descriptor.Title}", MessageImportance.Normal.ForVerbosity(verbosity));
                                var fixAllWatch = Stopwatch.StartNew();
                                foreach (var fix in group)
                                {
                                    try
                                    {
                                        LogMessage($"Calculating fix for {fix.NumberOfDiagnostics} instances.", MessageImportance.Low.ForVerbosity(verbosity));
                                        var operations = await fix.GetOperationsAsync(Token);
                                        var fixAllChanges = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
                                        if (fixAllChanges != null)
                                        {
                                            fixAllChanges.Apply(workspace, Token);
                                            fixApplied = true;
                                            appliedFixes[diagnostic.Id] = appliedFixes.GetOrAdd(diagnostic.Id, 0) + fix.NumberOfDiagnostics;
                                            project = workspace.CurrentSolution.GetProject(project.Id);
                                            watch.Stop();
                                        }

                                        LogMessage($"Applied batch changes in {TimeSpan.FromMilliseconds(fixAllWatch.ElapsedMilliseconds).Humanize()}. This is {fix.NumberOfDiagnostics / fixAllWatch.Elapsed.TotalSeconds:0.000} instances/second.", MessageImportance.Low.ForVerbosity(verbosity));
                                    }
                                    catch (Exception ex)
                                    {
                                        // Report thrown exceptions
                                        LogMessage($"The fix '{fix.CodeFixEquivalenceKey}' failed after {TimeSpan.FromMilliseconds(fixAllWatch.ElapsedMilliseconds).Humanize()}: {ex.ToString()}", MessageImportance.High.ForVerbosity(verbosity));
                                    }
                                }
                            }
                        }

                        // Try applying the individual fix in a specific document
                        if (!fixApplied)
                        {
                            try
                            {
                                LogMessage($"Applying code fix for {diagnostic}", MessageImportance.Normal.ForVerbosity(verbosity));
                                var operations = await codeAction.GetOperationsAsync(Token);
                                var applyChanges = operations.OfType<ApplyChangesOperation>().FirstOrDefault();

                                if (applyChanges != null)
                                {
                                    applyChanges.Apply(workspace, Token);

                                    // According to https://github.com/DotNetAnalyzers/StyleCopAnalyzers/pull/935 and 
                                    // https://github.com/dotnet/roslyn-sdk/issues/140, Sam Harwell mentioned that we should 
                                    // be forcing a re-parse of the document syntax tree at this point. 
                                    var newDoc = await workspace.CurrentSolution.GetDocument(document.Id).RecreateDocumentAsync(Token);

                                    // TODO: what happens if we can't apply?
                                    Debug.Assert(workspace.TryApplyChanges(newDoc.Project.Solution), "Failed to apply changes to workspace.");

                                    fixApplied = true;
                                    appliedFixes[diagnostic.Id] = appliedFixes.GetOrAdd(diagnostic.Id, 0) + 1;
                                    watch.Stop();

                                    project = await workspace.GetOrAddProjectAsync(
                                        BuildEngine4.GetRegisteredTaskObject<ProjectReader>(BuildingInsideVisualStudio),
                                        ProjectFullPath,
                                        PreprocessorSymbols.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                                        (i, m) => LogMessage(m, i), Token);

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
                                    return;
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
                                return;
                            }
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
                        return;
                    }
                    else
                    {
                        LogMessage($"Fixed {diagnostic.Id} in {TimeSpan.FromMilliseconds(watch.ElapsedMilliseconds).Humanize()}", MessageImportance.Low.ForVerbosity(verbosity));
                    }

                    watch.Restart();

                    (diagnostics, diagnostic, providers) = await GetNextFixableDiagnostic();
                }

                overallTime.Stop();
                LogMessage($"Overall processing for {appliedFixes.Values.Sum()} fixes took {TimeSpan.FromMilliseconds(overallTime.ElapsedMilliseconds).Humanize()}", MessageImportance.Low.ForVerbosity(verbosity));
                if (appliedFixes.Count == 1)
                {
                    var appliedFix = appliedFixes.First();
                    LogMessage($"Fixed {appliedFix.Key} ({appliedFix.Value} {(appliedFix.Value > 1 ? "instances" : "instance")})", MessageImportance.High.ForVerbosity(verbosity));
                }
                else
                {
                    LogMessage($"Fixed {string.Join(", ", appliedFixes.Select(x => $"{x.Key} ({x.Value} {(x.Value > 1 ? "instances" : "instance")})"))}", MessageImportance.High.ForVerbosity(verbosity));
                }
            }
            catch (Exception e)
            {
                LogErrorFromException(e);
                Cancel();
            }
            finally
            {
                Complete();
            }
        }

        CompilationOptions CreateCompilationOptions(Project project)
        {
            // Process diagnostic options and rule set
            var diagnosticOptions = new Dictionary<string, ReportDiagnostic>();
            if (!string.IsNullOrEmpty(CodeAnalysisRuleSet))
            {
                _ = RuleSet.GetDiagnosticOptionsFromRulesetFile(CodeAnalysisRuleSet, out diagnosticOptions);
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

            return compilationOptions;
        }

        IEnumerable<Assembly> LoadAnalyzers(bool warn = true)
        {
            var analyzers = new List<Assembly>(Analyzers.Length);
            foreach (var item in Analyzers.Where(x => !SkipAnalyzers.Any(s => s.ItemSpec == x.ItemSpec)))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(item.GetMetadata("FullPath"));
                    var roslyn = assembly.GetReferencedAssemblies().FirstOrDefault(x => x.Name == "Microsoft.CodeAnalysis");
                    if (roslyn != null && roslyn.Version < MinRoslynVersion)
                    {
                        if (warn)
                        {
                            Log.LogWarning(
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
                    Log.LogWarning(
                        nameof(AutoCodeFix),
                        nameof(Resources.ACF002),
                        null, null, 0, 0, 0, 0,
                        Resources.ACF002,
                        item.ItemSpec, e);
                }
            }

            return analyzers;
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
