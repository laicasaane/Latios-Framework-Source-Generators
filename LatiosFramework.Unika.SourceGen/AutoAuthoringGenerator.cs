using System;
using System.Threading;
using LatiosFramework.SourceGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Notice: I threw the requirements for this generator at Claude. The output this generates seems to meet the common use case.
// However, there are a couple of potential gotchas in this generator's implementation that might present as bugs in the future.
// First, Claude embedded the interface generation implementation of AuthoringGenerator within this generator, rather than make
// the other generator support Auto-Authoring.
// Second, this generator changes internal fields to private fields when replicating to authoring.
// Third, this generator copies usings so that field attributes can be copied as strings directly. Unity's ISystem generator
// does this too. However, I don't know if Claude's implementation handles the weird edge cases such as putting usings inside of
// namespaces.

namespace LatiosFramework.Unika.SourceGen
{
    [Generator]
    public class AutoAuthoringGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            //Debugger.Launch();

            var candidateProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: (node, token) => GeneratorFilterMethods.IsSyntaxClassGenericMatch(node, token, "UnikaScriptAutoAuthoring"),
                transform: (node, token) => GeneratorFilterMethods.GetSemanticDirectGenericMatch(node, token,
                                                                                                 "global::Latios.Unika.Authoring.UnikaScriptAutoAuthoring<T>",
                                                                                                 "UnikaScriptAutoAuthoring")
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
                var unikaAutoAuthoringSyntax = candidate.declaration;
                var semanticModel            = compilation.GetSemanticModel(unikaAutoAuthoringSyntax.SyntaxTree);
                UnikaSemanticsExtractor.ExtractAutoAuthoringSemantics(unikaAutoAuthoringSyntax, semanticModel, out var bodyContext);
                var code = AutoAuthoringCodeWriter.WriteAutoAuthoringCode(unikaAutoAuthoringSyntax, ref bodyContext);

                context.AddSource(candidate.HintName("_IUnikaAutoAuthoring.gen.cs"), code);
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
            new DiagnosticDescriptor("Unika_SG_04", "UnikaScriptAutoAuthoring Generator Error",
                                     "This error indicates a bug in the Latios Framework source generators. We'd appreciate a bug report. Thanks! Error message: '{0}'.",
                                     "Latios.Unika.Authoring.UnikaScriptAutoAuthoring<>", DiagnosticSeverity.Error, isEnabledByDefault: true, description: "");
    }
}

