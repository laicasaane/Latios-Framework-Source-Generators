using System;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static LatiosFramework.SourceGen.Tests.GeneratorTestHarness;

namespace LatiosFramework.SourceGen.Tests
{
    [TestClass]
    public sealed class ComponentGeneratorIncrementalTests
    {
        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void Generate_ReusedDriver_TracksUnrelatedRelevantAndRemovalEdits(bool collection)
        {
            var interfaceName = collection ? "ICollectionComponent" : "IManagedStructComponent";
            var originalTree  = Parse(
                $"namespace Demo {{ public partial struct Tracked : global::Latios.{interfaceName} {{ }} }}",
                "Tracked.cs"
            );
            var compilation = CreateCompilation(originalTree);
            var first       = RunGenerator(CreateGenerator(collection), compilation);
            var same        = RunGenerator(CreateGenerator(collection), compilation, first.Driver);
            var unrelatedCompilation = compilation.AddSyntaxTrees(Parse("namespace Other { public struct Value { } }", "Other.cs"));
            var unrelated            = RunGenerator(
                CreateGenerator(collection),
                unrelatedCompilation,
                same.Driver
            );
            var relevantTree = Parse(
                $"namespace Demo {{ internal partial struct Tracked : global::Latios.{interfaceName} {{ }} }}",
                "Tracked.cs"
            );
            var relevantCompilation = unrelatedCompilation.ReplaceSyntaxTree(originalTree, relevantTree);
            var relevant            = RunGenerator(CreateGenerator(collection), relevantCompilation, unrelated.Driver);
            var removedTree         = Parse("namespace Demo { internal partial struct Tracked { } }", "Tracked.cs");
            var removed             = RunGenerator(
                CreateGenerator(collection),
                relevantCompilation.ReplaceSyntaxTree(relevantTree, removedTree),
                relevant.Driver
            );

            AssertSuccessfulRun(first, expectedSourceCount: 1);
            AssertSuccessfulRun(same, expectedSourceCount: 1);
            AssertSuccessfulRun(unrelated, expectedSourceCount: 1);
            AssertSuccessfulRun(relevant, expectedSourceCount: 1);
            var removedGeneratedTrees = removed.OutputCompilation.SyntaxTrees
                .Where(static tree => tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
                .ToArray();
            Assert.AreEqual(
                0,
                removedGeneratedTrees.Length,
                string.Join(", ", removedGeneratedTrees.Select(static tree => tree.FilePath))
            );
            AssertSuccessfulRun(removed, expectedSourceCount: 0);
            Assert.AreEqual(
                first.Result.Results[0].GeneratedSources[0].SourceText.ToString(),
                unrelated.Result.Results[0].GeneratedSources[0].SourceText.ToString()
            );
            Assert.AreNotEqual(
                unrelated.Result.Results[0].GeneratedSources[0].SourceText.ToString(),
                relevant.Result.Results[0].GeneratedSources[0].SourceText.ToString()
            );
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void Generate_UnexpectedWriterFailure_RemainsGeneratorException(bool collection)
        {
            var interfaceName = collection ? "ICollectionComponent" : "IManagedStructComponent";
            var source        = new StringBuilder("namespace Demo {");
            for (var i = 0; i < 40; i++)
                source.Append(" public partial class Outer").Append(i).Append(" {");
            source.Append(" public partial struct Broken : global::Latios.").Append(interfaceName).Append(" { }");
            for (var i = 0; i < 40; i++)
                source.Append(" }");
            source.Append(" }");

            var run             = RunGenerator(CreateGenerator(collection), CreateCompilation(Parse(source.ToString(), "Failure.cs")));
            var generatorResult = Assert.ContainsSingle(run.Result.Results);

            Assert.IsNotNull(generatorResult.Exception);
            Assert.IsFalse(generatorResult.Diagnostics.Any(static diagnostic => diagnostic.Id.StartsWith("LATIOS_SG_", StringComparison.Ordinal)));
        }
    }
}
