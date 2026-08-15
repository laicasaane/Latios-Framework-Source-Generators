using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LatiosFramework.SourceGen
{
    [Generator]
    public class VInterfaceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            //Debugger.Launch();

            var candidateProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: (node, token) => GeneratorFilterMethods.IsSyntaxInterfaceInterfaceMatch(node, token, "IVInterface"),
                transform: (node, token) => GeneratorFilterMethods.GetSemanticInterfaceInterfaceMatch(node, token, "global::Latios.Unsafe.IVInterface",
                                                                                                     "IVInterface")
                ).Where(t => t is { });

            var compilationProvider = context.CompilationProvider;
            var combinedProviders   = candidateProvider.Combine(compilationProvider);

            context.RegisterSourceOutput(combinedProviders, (sourceProductionContext, sourceProviderTuple) =>
            {
                var (candidate, compilation) = sourceProviderTuple;
                GenerateOutput(sourceProductionContext, candidate, compilation);
            });
        }

        static void GenerateOutput(SourceProductionContext context, GeneratorCandidate<InterfaceDeclarationSyntax> candidate, Compilation compilation)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                var interfaceSyntax = candidate.declaration;
                var semanticModel   = compilation.GetSemanticModel(interfaceSyntax.SyntaxTree);
                VptrSemanticsExtractor.ExtractInterfaceSemantics(interfaceSyntax, semanticModel, out var bodyContext);
                var code = VInterfaceCodeWriter.WriteInterfaceCode(interfaceSyntax, ref bodyContext);

                context.AddSource(candidate.HintName("_IVInterface.gen.cs"), code);
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
            new DiagnosticDescriptor("LATIOS_SG_03", "IVInterface Generator Error",
                                     "This error indicates a bug in the Latios Framework source generators. We'd appreciate a bug report. Thanks! Error message: '{0}'.",
                                     "Latios.Unsafe.IVInterface", DiagnosticSeverity.Error, isEnabledByDefault: true, description: "");
    }
}

