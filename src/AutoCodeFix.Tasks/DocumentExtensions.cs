using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;

namespace AutoCodeFix
{
    public static class DocumentExtensions
    {
        /// <summary>
        /// Applies the given named code fix to a document.
        /// </summary>
        public static async Task<Document> ApplyCodeFixAsync(this Document document, string codeFixName, ImmutableArray<DiagnosticAnalyzer> analyzers = default, CancellationToken cancellationToken = default)
        {
            // If we request and process ALL codefixes at once, we'll get one for each 
            // diagnostics, which is one per non-implemented member of the interface/abstract 
            // base class, so we'd be applying unnecessary fixes after the first one.
            // So we re-retrieve them after each Apply, which will leave only the remaining 
            // ones.
            var codeFixes = await GetCodeFixes(document, codeFixName, analyzers, cancellationToken).ConfigureAwait(false);
            while (codeFixes.Length != 0)
            {
                var operations = await codeFixes[0].Action.GetOperationsAsync(cancellationToken);
                ApplyChangesOperation operation;
                if ((operation = operations.OfType<ApplyChangesOperation>().FirstOrDefault()) != null)
                {
                    document = operation.ChangedSolution.GetDocument(document.Id);
                    // Retrieve the codefixes for the updated doc again.
                    codeFixes = await GetCodeFixes(document, codeFixName, analyzers, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // If we got no applicable code fixes, exit the loop and move on to the next codefix.
                    break;
                }
            }

            return document;
        }

        static async Task<ImmutableArray<CodeFix>> GetCodeFixes(
            Document document, string codeFixName,
            ImmutableArray<DiagnosticAnalyzer> analyzers = default, CancellationToken cancellationToken = default)
        {
            var provider = GetCodeFixProvider(document, codeFixName);
            if (provider == null)
                return ImmutableArray<CodeFix>.Empty;

            var compilation = await document.Project.GetCompilationAsync(cancellationToken);
            var analyerCompilation = compilation.WithAnalyzers(analyzers, cancellationToken: cancellationToken);
            var allDiagnostics = await analyerCompilation.GetAllDiagnosticsAsync(cancellationToken);
            var diagnostics = allDiagnostics
                .Where(x => provider.FixableDiagnosticIds.Contains(x.Id))
                // Only consider the diagnostics raised by the target document.
                .Where(d =>
                    d.Location.Kind == LocationKind.SourceFile &&
                    d.Location.GetLineSpan().Path == document.FilePath);

            var codeFixes = new List<CodeFix>();
            foreach (var diagnostic in diagnostics)
            {
                await provider.RegisterCodeFixesAsync(
                    new CodeFixContext(document, diagnostic,
                    (action, diag) => codeFixes.Add(new CodeFix(action, diag, codeFixName)),
                    cancellationToken));
            }
            
            return codeFixes.ToImmutableArray();
        }

        static CodeFixProvider GetCodeFixProvider(Document document, string codeFixName)
            => document.Project.Solution.Workspace.Services.HostServices.GetExports<CodeFixProvider, IDictionary<string, object>>()
                    .Where(x => ((string[])x.Metadata["Languages"]).Contains(document.Project.Language) && 
                                ((string)x.Metadata["Name"]) == codeFixName)
                    .Select(x => x.Value)
                    .FirstOrDefault();

        class CodeFix
        {
            public CodeFix(CodeAction action, ImmutableArray<Diagnostic> diagnostics, string providerName)
            {
                Action = action;
                Diagnostics = diagnostics;
                ProviderName = providerName;
            }

            public CodeAction Action { get; }

            public ImmutableArray<Diagnostic> Diagnostics { get; }

            public string ProviderName { get; }

            public override string ToString()
            {
                return Action.Title + " from '" + ProviderName + "'";
            }

        }
    }
}
