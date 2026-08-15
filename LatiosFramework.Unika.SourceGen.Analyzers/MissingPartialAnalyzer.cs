using System.Collections.Immutable;
using LatiosFramework.SourceGen.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LatiosFramework.Unika.SourceGen.Analyzers
{
    // Mirrors LatiosFramework.SourceGen.Analyzers.MissingPartialAnalyzer, scoped to the marker interfaces
    // owned by SourceGenerators~/LatiosFramework.Unika.SourceGen/. Distinct diagnostic ID prefix ("Unika_AN")
    // matches this repo's existing convention of Unika_SG_NN being distinct from LATIOS_SG_NN.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class MissingPartialAnalyzer : DiagnosticAnalyzer
    {
        public static readonly DiagnosticDescriptor TypeMissingPartialDescriptor =
            new DiagnosticDescriptor("Unika_AN0001", "Type is not marked partial",
                                     "Missing partial on {0}. '{1}' uses a Latios Framework source generator that requires a partial declaration. Please add the partial keyword.",
                                     "Latios.Unika.SourceGen", DiagnosticSeverity.Error, isEnabledByDefault: true, description: "");

        public static readonly DiagnosticDescriptor ParentTypeMissingPartialDescriptor =
            new DiagnosticDescriptor("Unika_AN0002", "Parent type is not marked partial",
                                     "Missing partial on {0}. '{1}' uses a Latios Framework source generator that requires a partial declaration. Please add the partial keyword to '{2}'.",
                                     "Latios.Unika.SourceGen", DiagnosticSeverity.Error, isEnabledByDefault: true, description: "");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(TypeMissingPartialDescriptor, ParentTypeMissingPartialDescriptor);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeType, SyntaxKind.StructDeclaration, SyntaxKind.InterfaceDeclaration, SyntaxKind.ClassDeclaration);
        }

        static void AnalyzeType(SyntaxNodeAnalysisContext context)
        {
            PartialAndInterfaceDetection.AnalyzeTypeForMissingPartial(context, UnikaAnalyzerMarkers.All, UnikaAnalyzerMarkers.ExcludedModulePath,
                                                                       TypeMissingPartialDescriptor, ParentTypeMissingPartialDescriptor);
        }
    }
}
