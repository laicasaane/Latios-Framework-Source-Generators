using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatiosFramework.SourceGen.Tests
{
    internal static class GeneratorTestHarness
    {
        private const string RuntimeStubs =
"""
namespace Unity.Burst
{
    public sealed class BurstCompileAttribute : global::System.Attribute { }
    public readonly struct FunctionPointer<T> { }
    public static class BurstCompiler
    {
        public static FunctionPointer<T> CompileFunctionPointer<T>(T function) => default;
    }
}

namespace UnityEngine.Scripting
{
    public sealed class PreserveAttribute : global::System.Attribute { }
}

namespace AOT
{
    public sealed class MonoPInvokeCallbackAttribute : global::System.Attribute
    {
        public MonoPInvokeCallbackAttribute(global::System.Type type) { }
    }
}

namespace Unity.Entities
{
    public interface IComponentData { }
    public interface ICleanupComponentData { }
    public readonly struct ComponentType
    {
        public static ComponentType ReadOnly<T>() => default;
    }
}

namespace Latios
{
    public interface ICollectionComponent { }
    public interface IManagedStructComponent { }
}

namespace Latios.InternalSourceGen
{
    public static class StaticAPI
    {
        public interface ICollectionComponentSourceGenerated { }
        public interface ICollectionComponentCleanup { }
        public interface IManagedStructComponentSourceGenerated { }
        public interface IManagedStructComponentCleanup { }
        public readonly struct ContextPtr { }
        public delegate void BurstDispatchCollectionComponentDelegate(ContextPtr context, int operation);
        public static void BurstDispatchCollectionComponent<T>(ContextPtr context, int operation) { }
    }
}
""";

        internal static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp10);

        private static readonly ImmutableArray<MetadataReference> s_references =
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") !)
            .Split(Path.PathSeparator)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();

        internal static void AssertSuccessfulRun(GeneratorRun run, int expectedSourceCount)
        {
            Assert.IsEmpty(run.DriverDiagnostics);
            var generatorResult = Assert.ContainsSingle(run.Result.Results);
            Assert.IsNull(generatorResult.Exception);
            Assert.IsEmpty(generatorResult.Diagnostics);
            Assert.HasCount(expectedSourceCount, generatorResult.GeneratedSources);
        }

        internal static ImmutableArray<Diagnostic> GetErrors(Compilation compilation)
            => compilation.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();

        internal static IIncrementalGenerator CreateGenerator(bool collection)
            => collection ? new CollectionComponentGenerator() : new ManagedStructComponentGenerator();

        internal static SyntaxTree Parse(string source, string path)
            => CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), ParseOptions, path);

        internal static CSharpCompilation CreateCompilation(params SyntaxTree[] featureTrees)
            => CSharpCompilation.Create(
                assemblyName: "ComponentGeneratorTests",
                syntaxTrees: new[] { Parse(RuntimeStubs, "RuntimeStubs.cs") }.Concat(featureTrees),
                references: s_references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

        internal static GeneratorRun RunGenerator(
            IIncrementalGenerator generator,
            CSharpCompilation compilation,
            GeneratorDriver? driver = null
        )
        {
            driver ??= CSharpGeneratorDriver.Create(
                generators: new[] { generator.AsSourceGenerator() },
                parseOptions: ParseOptions
            );
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var outputCompilation,
                out var diagnostics
            );
            return new GeneratorRun(driver, driver.GetRunResult(), outputCompilation, diagnostics);
        }
    }

    internal readonly struct GeneratorRun
    {
        public readonly GeneratorDriver Driver;
        public readonly GeneratorDriverRunResult Result;
        public readonly Compilation OutputCompilation;
        public readonly ImmutableArray<Diagnostic> DriverDiagnostics;

        public GeneratorRun(
            GeneratorDriver driver,
            GeneratorDriverRunResult result,
            Compilation outputCompilation,
            ImmutableArray<Diagnostic> driverDiagnostics
        )
        {
            Driver            = driver;
            Result            = result;
            OutputCompilation = outputCompilation;
            DriverDiagnostics = driverDiagnostics;
        }
    }
}
