using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LatiosFramework.SourceGen.Analyzers
{
    // Mirrors Unity.Entities.SourceGen.SystemGenerator.PartialStructWarningSuppressor. CS0282 fires because
    // JobEachCodeWriter emits a second partial declaration of the user's IJobEach struct carrying its own
    // fields (__handles, __mode, etc.), which the compiler otherwise correctly flags as having no defined
    // field ordering across partial declarations.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class PartialStructWarningSuppressor : DiagnosticSuppressor
    {
        static readonly SuppressionDescriptor PartialStructWarningRule = new SuppressionDescriptor("LATIOS_SPDC0282", "CS0282",
            "Some Latios Framework types utilize codegen requiring the type to be partial.");

        static readonly string[] _allowedPartialStructInterfaces = { "IJobEach" };

        public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => ImmutableArray.Create(PartialStructWarningRule);

        public override void ReportSuppressions(SuppressionAnalysisContext context)
        {
            foreach (var diagnostic in context.ReportedDiagnostics.Where(diagnostic => diagnostic.Id == PartialStructWarningRule.SuppressedDiagnosticId))
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var node = diagnostic.Location.SourceTree?.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan);
                if (node is StructDeclarationSyntax {BaseList: { } baseList} structSyntax)
                    if (baseList.Types.Any(type => _allowedPartialStructInterfaces.Contains(type.ToString())))
                        context.ReportSuppression(Suppression.Create(PartialStructWarningRule, diagnostic));
                    else
                    {
                        var semanticModel = context.GetSemanticModel(structSyntax.SyntaxTree);
                        var typeSymbol = semanticModel.GetDeclaredSymbol(structSyntax) as INamedTypeSymbol;

                        if (typeSymbol != null && typeSymbol.AllInterfaces.Any(i => i.ToDisplayString() == "Latios.IJobEach"))
                            context.ReportSuppression(Suppression.Create(PartialStructWarningRule, diagnostic));
                    }
            }
        }
    }
}
