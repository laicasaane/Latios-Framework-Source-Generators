using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LatiosFramework.SourceGen.Analyzers.CodeFixes
{
    // Shared between the Core and Unika CodeFixes projects (linked in via <Compile Include>, same reason as
    // PartialAndInterfaceDetection.cs). Mirrors Unity's EntitiesCodeFixProvider.AddPartial verbatim.
    internal static class AddPartialFixHelper
    {
        public static async Task<Document> AddPartial(Document document, TypeDeclarationSyntax typeDeclarationSyntax, CancellationToken cancellationToken)
        {
            var partialModifier = SyntaxFactory.Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(SyntaxFactory.Space);
            var modifiedSyntax = typeDeclarationSyntax.WithoutLeadingTrivia().AddModifiers(partialModifier).WithTriviaFrom(typeDeclarationSyntax);
            var oldRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var newRoot = oldRoot.ReplaceNode(typeDeclarationSyntax, modifiedSyntax);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
