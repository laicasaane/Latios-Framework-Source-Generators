// This file was originally written with Claude.
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LatiosFramework.SourceGen
{
    [Generator]
    public class InjectableGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            //Debugger.Launch();

            var candidateProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: (node, token) => GeneratorFilterMethods.IsSyntaxStructInterfaceMatch(node, token, "IInjectable"),
                transform: (node, token) => GeneratorFilterMethods.GetSemanticStructInterfaceMatch(node, token, "global::Latios.IInjectable", "IInjectable")
                ).Where(t => t is { });

            var compilationProvider = context.CompilationProvider;
            var combinedProviders   = candidateProvider.Combine(compilationProvider);

            context.RegisterSourceOutput(combinedProviders, (sourceProductionContext, sourceProviderTuple) =>
            {
                var (candidate, compilation) = sourceProviderTuple;
                GenerateOutput(sourceProductionContext, candidate, compilation);
            });
        }

        static void GenerateOutput(SourceProductionContext context, GeneratorCandidate<StructDeclarationSyntax> candidate, Compilation compilation)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                InjectableSemanticsExtractor.ExtractInjectableSemantics(candidate.declaration, compilation, context, out var bodyContext);
                var code = InjectableCodeWriter.WriteInjectableCode(candidate.declaration, ref bodyContext);

                context.AddSource(candidate.HintName("_IInjectable.gen.cs"), code);
            }
            catch (Exception e)
            {
                if (e is OperationCanceledException)
                    throw;
                context.ReportDiagnostic(
                    Diagnostic.Create(InternalErrorDescriptor, candidate.declaration.GetLocation(), e.ToUnityPrintableString()));
            }
        }

        public static readonly DiagnosticDescriptor InternalErrorDescriptor =
            new DiagnosticDescriptor("LATIOS_SG_08", "IInjectable Generator Error",
                                     "This error indicates a bug in the Latios Framework source generators. We'd appreciate a bug report. Thanks! Error message: '{0}'.",
                                     "Latios.IInjectable", DiagnosticSeverity.Error, isEnabledByDefault: true, description: "");
    }
}
