using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LatiosFramework.SourceGen.Analyzers.CodeFixes
{
    [ExportCodeFixProvider(Microsoft.CodeAnalysis.LanguageNames.CSharp, Name = nameof(AddPartialCodeFixProvider)), Shared]
    public class AddPartialCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(MissingPartialAnalyzer.TypeMissingPartialDescriptor.Id, MissingPartialAnalyzer.ParentTypeMissingPartialDescriptor.Id);

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            foreach (var diagnostic in context.Diagnostics)
            {
                if (root?.FindNode(diagnostic.Location.SourceSpan) is TypeDeclarationSyntax typeDeclarationSyntax)
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(title: "Add partial keyword",
                                          createChangedDocument: c => AddPartialFixHelper.AddPartial(context.Document, typeDeclarationSyntax, c),
                                          equivalenceKey: "AddPartial"),
                        diagnostic);
                }
            }
        }
    }
}
