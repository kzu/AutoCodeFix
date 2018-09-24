using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace AutoCodeFix
{
    public static class DocumentExtensions
    {
        public static async Task<Document> RecreateDocumentAsync(this Document document, CancellationToken cancellationToken)
        {
            var newText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            newText = newText.WithChanges(new TextChange(new TextSpan(0, 0), " "));
            newText = newText.WithChanges(new TextChange(new TextSpan(0, 1), string.Empty));
            return document.WithText(newText);
        }
    }
}
