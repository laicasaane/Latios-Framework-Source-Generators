using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LatiosFramework.SourceGen.Analyzers
{
    // Mirrors Unity.Entities.SourceGen.EntitiesAnalyzer.TypeAnalyzer's EA0007/EA0008 pattern: the Core
    // source generators (GeneratorFilterMethods.IsSyntaxStructInterfaceMatch / IsSyntaxInterfaceInterfaceMatch)
    // silently drop a type from candidacy if it's missing `partial`, so nothing ever reports it. This analyzer
    // catches it directly in the IDE instead.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class MissingPartialAnalyzer : DiagnosticAnalyzer
    {
        public static readonly DiagnosticDescriptor TypeMissingPartialDescriptor =
            new DiagnosticDescriptor("LATIOS_AN0001", "Type is not marked partial",
                                     "Missing partial on {0}. '{1}' uses a Latios Framework source generator that requires a partial declaration. Please add the partial keyword.",
                                     "Latios.SourceGen", DiagnosticSeverity.Error, isEnabledByDefault: true, description: "");

        public static readonly DiagnosticDescriptor ParentTypeMissingPartialDescriptor =
            new DiagnosticDescriptor("LATIOS_AN0002", "Parent type is not marked partial",
                                     "Missing partial on {0}. '{1}' uses a Latios Framework source generator that requires a partial declaration. Please add the partial keyword to '{2}'.",
                                     "Latios.SourceGen", DiagnosticSeverity.Error, isEnabledByDefault: true, description: "");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(TypeMissingPartialDescriptor, ParentTypeMissingPartialDescriptor);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeType, SyntaxKind.StructDeclaration, SyntaxKind.InterfaceDeclaration);
        }

        static void AnalyzeType(SyntaxNodeAnalysisContext context)
        {
            PartialAndInterfaceDetection.AnalyzeTypeForMissingPartial(context, CoreAnalyzerMarkers.All, CoreAnalyzerMarkers.ExcludedModulePath,
                                                                       TypeMissingPartialDescriptor, ParentTypeMissingPartialDescriptor);
        }
    }
}
