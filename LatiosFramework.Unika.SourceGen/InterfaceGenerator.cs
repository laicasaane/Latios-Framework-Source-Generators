using System;
using LatiosFramework.SourceGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LatiosFramework.Unika.SourceGen
{
    [Generator]
    public class InterfaceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            //Debugger.Launch();

            var candidateProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: (node, token) => GeneratorFilterMethods.IsSyntaxInterfaceInterfaceMatch(node, token, "IUnikaInterface"),
                transform: (node, token) => GeneratorFilterMethods.GetSemanticInterfaceInterfaceMatch(node, token, "global::Latios.Unika.IUnikaInterface",
                                                                                                     "IUnikaInterface")
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
                var unikaInterfaceSyntax = candidate.declaration;
                var semanticModel        = compilation.GetSemanticModel(unikaInterfaceSyntax.SyntaxTree);
                UnikaSemanticsExtractor.ExtractInterfaceSemantics(unikaInterfaceSyntax, semanticModel, out var bodyContext);
                var code = InterfaceCodeWriter.WriteInterfaceCode(unikaInterfaceSyntax, ref bodyContext);

                context.AddSource(candidate.HintName("_IUnikaInterface.gen.cs"), code);
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
            new DiagnosticDescriptor("Unika_SG_01", "IUnikaInterface Generator Error",
                                     "This error indicates a bug in the Latios Framework source generators. We'd appreciate a bug report. Thanks! Error message: '{0}'.",
                                     "Latios.Unika.IUnikaInterface", DiagnosticSeverity.Error, isEnabledByDefault: true, description: "");
    }
}

