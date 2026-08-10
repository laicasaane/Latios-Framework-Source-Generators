using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace LatiosFramework.SourceGen
{
    [Generator]
    public class ManagedStructComponentGenerator : IIncrementalGenerator
    {
        private const string InterfaceMetadataName = "global::Latios.IManagedStructComponent";
        private const string OutputRole            = "ManagedStructComponent";
        private const string ComponentType         = "ManagedStruct";
        private const bool   WriteBurst            = false;

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var candidateProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: IsCandidate,
                transform: CreateModel
                ).Where(IsValid);

            context.RegisterSourceOutput(candidateProvider, GenerateOutput);
        }

        private static bool IsCandidate(SyntaxNode syntaxNode, CancellationToken cancellationToken)
            => ComponentModel.IsCandidate(syntaxNode, cancellationToken);

        private static ComponentModel CreateModel(GeneratorSyntaxContext context, CancellationToken cancellationToken)
            => ComponentModel.Create(
                context,
                cancellationToken,
                InterfaceMetadataName,
                OutputRole
            );

        private static bool IsValid(ComponentModel model)
            => model.IsValid;

        private static void GenerateOutput(SourceProductionContext context, ComponentModel model)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var code = ComponentCodeWriter.WriteComponentCode(in model, ComponentType, WriteBurst);
            context.AddSource(model.HintName, SourceText.From(code, Encoding.UTF8));
        }
    }
}
