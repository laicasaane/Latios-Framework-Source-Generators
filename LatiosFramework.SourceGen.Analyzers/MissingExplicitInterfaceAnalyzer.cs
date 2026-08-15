using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LatiosFramework.SourceGen.Analyzers
{
    // IVInterface is meant to be extended by user interfaces, but GeneratorFilterMethods' candidacy gate only
    // looks at the declaration's own BaseList, so a type implementing it transitively is silently dropped.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class MissingExplicitInterfaceAnalyzer : DiagnosticAnalyzer
    {
        public static readonly DiagnosticDescriptor MarkerInterfaceNotExplicitDescriptor =
            new DiagnosticDescriptor("LATIOS_AN0003", "Marker interface must be listed explicitly",
                                     "'{1}' implements '{0}' indirectly through another interface, but the Latios Framework source generator requires '{0}' to be listed explicitly in '{1}''s own declaration.",
                                     "Latios.SourceGen", DiagnosticSeverity.Error, isEnabledByDefault: true, description: "");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(MarkerInterfaceNotExplicitDescriptor);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeType, SyntaxKind.StructDeclaration, SyntaxKind.InterfaceDeclaration);
        }

        static void AnalyzeType(SyntaxNodeAnalysisContext context)
        {
            PartialAndInterfaceDetection.AnalyzeTypeForMissingExplicitInterface(context, CoreAnalyzerMarkers.All, CoreAnalyzerMarkers.ExcludedModulePath,
                                                                                 MarkerInterfaceNotExplicitDescriptor);
        }
    }
}
