using System;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LatiosFramework.SourceGen
{
    [Generator]
    public class ManagedStructComponentGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            //Debugger.Launch();

            var candidateProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: (node, token) => GeneratorFilterMethods.IsSyntaxStructInterfaceMatch(node, token, "IManagedStructComponent"),
                transform: (node, token) => GeneratorFilterMethods.GetSemanticStructInterfaceMatch(node, token, "global::Latios.IManagedStructComponent",
                                                                                                  "IManagedStructComponent")
                ).Where(t => t is { });

            context.RegisterSourceOutput(candidateProvider, (sourceProductionContext, candidate) =>
            {
                GenerateOutput(sourceProductionContext, candidate);
            });
        }

        static void GenerateOutput(SourceProductionContext context, GeneratorCandidate<StructDeclarationSyntax> candidate)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                context.AddSource(candidate.HintName("_IManagedStructComponent.gen.cs"),
                                  ComponentCodeWriter.WriteComponentCode(candidate.declaration, "ManagedStruct", false));
            }
            catch (Exception e)
            {
                if (e is OperationCanceledException)
                    throw;
                context.ReportDiagnostic(
                    Diagnostic.Create(ManagedStructComponentErrorDescriptor, candidate.declaration.GetLocation(), e.ToUnityPrintableString()));
            }
        }

        public static readonly DiagnosticDescriptor ManagedStructComponentErrorDescriptor =
            new DiagnosticDescriptor("LATIOS_SG_02", "IManagedStructComponent Generator Error",
                                     "This error indicates a bug in the Latios Framework source generators. We'd appreciate a bug report. Thanks! Error message: '{0}'.",
                                     "Latios.IManagedStructComponent", DiagnosticSeverity.Error, isEnabledByDefault: true, description: "");
    }
}

