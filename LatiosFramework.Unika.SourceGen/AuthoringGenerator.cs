using System;
using System.Diagnostics;
using LatiosFramework.SourceGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LatiosFramework.Unika.SourceGen
{
    [Generator]
    public class AuthoringGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            //Debugger.Launch();

            var candidateProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: (node, token) => GeneratorFilterMethods.IsSyntaxClassGenericMatch(node, token, "UnikaScriptAuthoring"),
                transform: (node, token) => GeneratorFilterMethods.GetSemanticClassGenericMatch(node, token, "global::Latios.Unika.Authoring.UnikaScriptAuthoringBase",
                                                                                                "UnikaScriptAuthoring")
                ).Where(t => t is { });

            var compilationProvider = context.CompilationProvider;
            var combinedProviders   = candidateProvider.Combine(compilationProvider);

            context.RegisterSourceOutput(combinedProviders, (sourceProductionContext, sourceProviderTuple) =>
            {
                var (candidate, compilation) = sourceProviderTuple;
                GenerateOutput(sourceProductionContext, candidate, compilation);
            });
        }

        static void GenerateOutput(SourceProductionContext context, GeneratorCandidate<ClassDeclarationSyntax> candidate, Compilation compilation)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                var unikaAuthoringSyntax = candidate.declaration;
                var semanticModel        = compilation.GetSemanticModel(unikaAuthoringSyntax.SyntaxTree);
                UnikaSemanticsExtractor.ExtractAuthoringSemantics(unikaAuthoringSyntax, semanticModel, out var bodyContext);
                var code = AuthoringCodeWriter.WriteAuthoringCode(unikaAuthoringSyntax, ref bodyContext);

                context.AddSource(candidate.HintName("_IUnikaAuthoring.gen.cs"), code);
            }
            catch (Exception e)
            {
                if (e is OperationCanceledException)
                    throw;
                context.ReportDiagnostic(
                    Diagnostic.Create(CollectionComponentErrorDescriptor, candidate.declaration.GetLocation(), e.ToUnityPrintableString()));
            }
        }

        public static readonly DiagnosticDescriptor CollectionComponentErrorDescriptor =
            new DiagnosticDescriptor("Unika_SG_03", "UnikaScriptAuthoring Generator Error",
                                     "This error indicates a bug in the Latios Framework source generators. We'd appreciate a bug report. Thanks! Error message: '{0}'.",
                                     "Latios.Unika.Authoring.UnikaScriptAuthoring<>", DiagnosticSeverity.Error, isEnabledByDefault: true, description: "");
    }
}

