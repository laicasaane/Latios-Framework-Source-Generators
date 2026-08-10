using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static LatiosFramework.SourceGen.Tests.GeneratorTestHarness;

namespace LatiosFramework.SourceGen.Tests
{
    [TestClass]
    public sealed class ComponentGeneratorOutputTests
    {
        private const string CollectionExpectedSource =
"""
namespace Demo
{
	[global::System.Runtime.CompilerServices.CompilerGenerated]
	[global::Unity.Burst.BurstCompile]
	public partial struct Inventory : global::Latios.InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
	{
		public struct ExistComponent : global::Unity.Entities.IComponentData { }
		public struct CleanupComponent : global::Unity.Entities.ICleanupComponentData, global::Latios.InternalSourceGen.StaticAPI.ICollectionComponentCleanup
		{
			[global::UnityEngine.Scripting.Preserve]
			public static global::Unity.Burst.FunctionPointer<global::Latios.InternalSourceGen.StaticAPI.BurstDispatchCollectionComponentDelegate> GetBurstDispatchFunctionPtr()
			{
				return global::Unity.Burst.BurstCompiler.CompileFunctionPointer<global::Latios.InternalSourceGen.StaticAPI.BurstDispatchCollectionComponentDelegate>(BurstDispatch);
			}

			[global::UnityEngine.Scripting.Preserve]
			public static global::System.Type GetCollectionComponentType() => typeof(Inventory);
		}

		public global::Unity.Entities.ComponentType componentType => global::Unity.Entities.ComponentType.ReadOnly<ExistComponent>();
		public global::Unity.Entities.ComponentType cleanupType => global::Unity.Entities.ComponentType.ReadOnly<CleanupComponent>();

		[global::AOT.MonoPInvokeCallback(typeof(global::Latios.InternalSourceGen.StaticAPI.BurstDispatchCollectionComponentDelegate))]
		[global::UnityEngine.Scripting.Preserve]
		[global::Unity.Burst.BurstCompile]
		public static void BurstDispatch(global::Latios.InternalSourceGen.StaticAPI.ContextPtr context, int operation)
		{
			global::Latios.InternalSourceGen.StaticAPI.BurstDispatchCollectionComponent<Inventory>(context, operation);
		}
	}
}

""";

        private const string ManagedExpectedSource =
"""
namespace Demo
{
	[global::System.Runtime.CompilerServices.CompilerGenerated]
	public partial struct Inventory : global::Latios.InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
	{
		public struct ExistComponent : global::Unity.Entities.IComponentData { }
		public struct CleanupComponent : global::Unity.Entities.ICleanupComponentData, global::Latios.InternalSourceGen.StaticAPI.IManagedStructComponentCleanup
		{
			[global::UnityEngine.Scripting.Preserve]
			public static global::System.Type GetManagedStructComponentType() => typeof(Inventory);
		}

		public global::Unity.Entities.ComponentType componentType => global::Unity.Entities.ComponentType.ReadOnly<ExistComponent>();
		public global::Unity.Entities.ComponentType cleanupType => global::Unity.Entities.ComponentType.ReadOnly<CleanupComponent>();

	}
}

""";

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void Generate_ValidComponent_ProducesExactUtf8Source(bool collection)
        {
            var interfaceName = collection ? "ICollectionComponent" : "IManagedStructComponent";
            var source        = $$"""

            namespace Demo
            {
                public partial struct Inventory : global::Latios.{{interfaceName}} { }
            }

            """;
            var compilation = CreateCompilation(Parse(source, "Feature.cs"));

            var run = RunGenerator(CreateGenerator(collection), compilation);

            Assert.IsEmpty(run.DriverDiagnostics);
            var generatorResult = Assert.ContainsSingle(run.Result.Results);
            Assert.IsNull(generatorResult.Exception);
            Assert.IsEmpty(generatorResult.Diagnostics);
            var generated = Assert.ContainsSingle(generatorResult.GeneratedSources);
            Assert.AreEqual(
                collection
                    ? "global_003A__003A_Demo_002E_Inventory.CollectionComponent.g.cs"
                    : "global_003A__003A_Demo_002E_Inventory.ManagedStructComponent.g.cs",
                generated.HintName
            );
            Assert.AreEqual(collection ? CollectionExpectedSource : ManagedExpectedSource, generated.SourceText.ToString());
            Assert.AreEqual("utf-8", generated.SourceText.Encoding?.WebName);
            Assert.AreEqual('\n', generated.SourceText[generated.SourceText.Length - 1]);
            Assert.AreNotEqual('\n', generated.SourceText[generated.SourceText.Length - 2]);
            Assert.IsEmpty(GetErrors(run.OutputCompilation));
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void Generate_AliasAndGlobalQualifiedInterfaces_ProducesBothComponents(bool collection)
        {
            var interfaceName = collection ? "ICollectionComponent" : "IManagedStructComponent";
            var source        = $$"""

            using Alias = Latios;

            namespace Demo
            {
                public partial struct Aliased : Alias.{{interfaceName}} { }
                public partial struct Global : global::Latios.{{interfaceName}} { }
            }

            """;

            var run = RunGenerator(CreateGenerator(collection), CreateCompilation(Parse(source, "Qualified.cs")));

            AssertSuccessfulRun(run, expectedSourceCount: 2);
            Assert.IsEmpty(GetErrors(run.OutputCompilation));
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void Generate_CollidingFileStemsNamespacesAndNesting_UsesUniqueSemanticHints(bool collection)
        {
            var interfaceName = collection ? "ICollectionComponent" : "IManagedStructComponent";
            var first         = Parse($$"""

            namespace Alpha
            {
                public partial struct Same : global::Latios.{{interfaceName}} { }

                public partial class Outer
                {
                    public partial struct Same : global::Latios.{{interfaceName}} { }
                }
            }

            """, "A/Same.cs");
            var second = Parse($$"""

            namespace Beta
            {
                public partial struct Same : global::Latios.{{interfaceName}} { }
            }

            """, "B/Same.cs");
            var role = collection ? "CollectionComponent" : "ManagedStructComponent";

            var run = RunGenerator(CreateGenerator(collection), CreateCompilation(first, second));

            AssertSuccessfulRun(run, expectedSourceCount: 3);
            var hints = run.Result.Results[0].GeneratedSources
                .Select(static source => source.HintName)
                .OrderBy(static hint => hint, StringComparer.Ordinal)
                .ToArray();
            CollectionAssert.AreEqual(
                new[]
                {
                    $"global_003A__003A_Alpha_002E_Outer_002E_Same.{role}.g.cs",
                    $"global_003A__003A_Alpha_002E_Same.{role}.g.cs",
                    $"global_003A__003A_Beta_002E_Same.{role}.g.cs",
                },
                hints
            );
            Assert.IsEmpty(GetErrors(run.OutputCompilation));
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void Generate_RenamedFile_PreservesHintAndSource(bool collection)
        {
            var interfaceName = collection ? "ICollectionComponent" : "IManagedStructComponent";
            var source        = $"namespace Demo {{ public partial struct Stable : global::Latios.{interfaceName} {{ }} }}";

            var first  = RunGenerator(CreateGenerator(collection), CreateCompilation(Parse(source, "Before.cs")));
            var second = RunGenerator(CreateGenerator(collection), CreateCompilation(Parse(source, "After.cs")));

            AssertSuccessfulRun(first, expectedSourceCount: 1);
            AssertSuccessfulRun(second, expectedSourceCount: 1);
            Assert.AreEqual(first.Result.Results[0].GeneratedSources[0].HintName, second.Result.Results[0].GeneratedSources[0].HintName);
            Assert.AreEqual(first.Result.Results[0].GeneratedSources[0].SourceText.ToString(), second.Result.Results[0].GeneratedSources[0].SourceText.ToString());
        }
    }
}
