using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AutoCodeFix.Tests
{
    // Example analyzer + code fix that makes every method virtual

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class VirtualizerAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "TEST001";
        public const string Category = "Build";

        readonly DiagnosticDescriptor descriptor = new DiagnosticDescriptor(
            DiagnosticId,
            "All instance methods should be virtual",
            nameof(VirtualizerAnalyzer),
            Category,
            DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public VirtualizerAnalyzer()
        {
            // NOTE: the environment variable tells us we're being run with AutoCodeFix enabled and 
            // active, meaning we should issue warnings so that AutoCodeFix can apply the code fixes. 
            // If AutoCodeFix is not active, we can alternatively do so if we want to allow users to 
            // also apply the code fixes themselves, by reporting either Info or Warning.
            // Reporting Error is not recommended since it defeats the purpose of AutoCodeFix, since 
            // in that case the compilation phase will completely fail and therefore not give AutoCodeFix 
            // a chance to detect that after build and apply the fixes before attempting a second build.
            var autoCodeFixEnabled = bool.TryParse(Environment.GetEnvironmentVariable("AutoCodeFix"), out var value) && value;

            descriptor = new DiagnosticDescriptor(
                        DiagnosticId,
                        "All methods should be virtual",
                        "Make '{0}' virtual",
                        Category,
                        autoCodeFixEnabled ? DiagnosticSeverity.Warning : DiagnosticSeverity.Hidden,
                        isEnabledByDefault: true);
        }

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(descriptor);

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSymbolAction(AnalyzeSymbolNode, SymbolKind.Method);
        }

        void AnalyzeSymbolNode(SymbolAnalysisContext context)
        {
            if (context.Symbol is IMethodSymbol method &&
                !method.IsStatic &&
                // .ctor cannot be referenced by name, yet it's reported as an IMethodSymbol
                method.CanBeReferencedByName &&
                !method.IsVirtual &&
                // We need a declaring reference where we'll add the `virtual` keyword.
                context.Symbol.DeclaringSyntaxReferences.FirstOrDefault() is SyntaxReference reference && 
                reference != null)
            {
                var syntax = context.Symbol.DeclaringSyntaxReferences.First();
                var diagnostic = Diagnostic.Create(descriptor, Location.Create(reference.SyntaxTree, reference.Span), method.Name);
                context.ReportDiagnostic(diagnostic);

                // Optionally, code fixes can optimize build times by flagging to AutoCodeFix that 
                // there are code fixes to apply before compilation happens. This way, a single build 
                // will happen, after code fixes are applied. Otherwise, two compilation passes need 
                // to happen: first to record the fixable warnings, then to apply them and finally to 
                // compile again so the final code contains the modified versions.
                var metadataFile = context.Options.AdditionalFiles.FirstOrDefault(x => x.Path.EndsWith("AutoCodeFix.ini", StringComparison.OrdinalIgnoreCase));
                if (metadataFile != null)
                {
                    // We can pass ourselves arbitrary settings by adding <AutoFixSetting Include="" Value="" /> items.
                    // If items are calculated, you can create a target and run BeforeTargets="SaveAutoFixSettings".
                    var settings = File.ReadAllLines(metadataFile.Path)
                            .Where(line => !string.IsNullOrEmpty(line))
                            .Select(line => line.Split('='))
                            .Where(pair => pair.Length == 2)
                            .ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim());

                    // The location of the flag file must be the intermediate output path.
                    if (settings.TryGetValue("IntermediateOutputPath", out var intermediatePath))
                    {
                        File.WriteAllText(Path.Combine(intermediatePath, "AutoCodeFixBeforeCompile.flag"), "");
                    }
                }

            }
        }
    }

    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(VirtualizerCodeFixProvider))]
    public class VirtualizerCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(VirtualizerAnalyzer.DiagnosticId);

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var document = context.Document;
            var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var span = context.Span;
            var node = root.FindNode(span);
            var token = root.FindToken(span.Start);
            if (!token.Span.IntersectsWith(span) || node.Kind() != SyntaxKind.MethodDeclaration)
                return;

            var syntax = (MethodDeclarationSyntax)node;

            foreach (var diagnostic in context.Diagnostics.Where(d => FixableDiagnosticIds.Contains(d.Id)))
            {
                context.RegisterCodeFix(
                    CodeAction.Create("Make method virtual",
                        (cancellation) =>
                        {
                            var updated = syntax.AddModifiers(SyntaxFactory.Token(SyntaxKind.VirtualKeyword));
                            var newRoot = syntax.SyntaxTree.GetCompilationUnitRoot().ReplaceNode(syntax, updated);

                            return Task.FromResult(document.WithSyntaxRoot(newRoot));
                        },
                        diagnostic.Id + diagnostic.Location.GetLineSpan().ToString()),
                    diagnostic);
            }
        }
    }
}
