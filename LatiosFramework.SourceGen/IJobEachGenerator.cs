// This file was originally written with Claude.
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LatiosFramework.SourceGen
{
    [Generator]
    public class IJobEachGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var candidateProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: (node, token) => GeneratorFilterMethods.IsSyntaxStructInterfaceMatch(node, token, "IJobEach"),
                transform: (node, token) => GeneratorFilterMethods.GetSemanticStructInterfaceMatch(node, token, "global::Latios.IJobEach", "IJobEach")
                ).Where(t => t is { });

            var combinedProviders = candidateProvider.Combine(context.CompilationProvider);

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
                var structSyntax = candidate.declaration;
                JobEachSemanticsExtractor.Extract(structSyntax, compilation, context, out var job);
                if (job.jobFullName == null)
                    return;

                if (!job.isValid)
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnsupportedJobDescriptor,
                                                               job.unsupportedLocation ?? structSyntax.Identifier.GetLocation(),
                                                               job.unsupportedReason));
                }

                if (job.rejectedAttributeMessages != null)
                {
                    foreach (var message in job.rejectedAttributeMessages)
                        context.ReportDiagnostic(Diagnostic.Create(IgnoredAttributeDescriptor, structSyntax.Identifier.GetLocation(), message));
                }

                var code = JobEachCodeWriter.WriteJobCode(structSyntax, ref job);
                context.AddSource(candidate.HintName("_IJobEach.gen.cs"), code);
            }
            catch (Exception e)
            {
                if (e is OperationCanceledException)
                    throw;
                context.ReportDiagnostic(Diagnostic.Create(InternalErrorDescriptor, candidate.declaration.GetLocation(), e.ToUnityPrintableString()));
            }
        }

        // The job still gets its handle types and a throwing dispatch so that scheduling systems produce
        // this one error rather than a cascade of missing-type errors pointing at generated code.
        public static readonly DiagnosticDescriptor UnsupportedJobDescriptor =
            new DiagnosticDescriptor("LATIOS_SG_09", "Unsupported IJobEach",
                                     "This IJobEach {0}.",
                                     "Latios.IJobEach", DiagnosticSeverity.Error, isEnabledByDefault: true, description: "");

        public static readonly DiagnosticDescriptor IgnoredAttributeDescriptor =
            new DiagnosticDescriptor("LATIOS_SG_11", "Attribute ignored by IJobEach", "{0}",
                                     "Latios.IJobEach", DiagnosticSeverity.Warning, isEnabledByDefault: true, description: "");

        public static readonly DiagnosticDescriptor InternalErrorDescriptor =
            new DiagnosticDescriptor("LATIOS_SG_10", "IJobEach Generator Error",
                                     "This error indicates a bug in the Latios Framework source generators. We'd appreciate a bug report. Thanks! Error message: '{0}'.",
                                     "Latios.IJobEach", DiagnosticSeverity.Error, isEnabledByDefault: true, description: "");
    }
}
