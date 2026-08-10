using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatiosFramework.SourceGen.Tests
{
    [TestClass]
    public sealed class ComponentModelTests
    {
        [TestMethod]
        public void ComponentModel_SeparateEqualStorageAndEveryField_HasFieldCompleteEquality()
        {
            var assembly  = typeof(CollectionComponentGenerator).Assembly;
            var modelType = assembly.GetType("LatiosFramework.SourceGen.ComponentModel");
            var scopeType = assembly.GetType("LatiosFramework.SourceGen.ComponentScope");

            Assert.IsNotNull(modelType);
            Assert.IsNotNull(scopeType);
            Assert.IsTrue(modelType.GetInterfaces().Any(
                type => type.IsGenericType
                    && type.GetGenericTypeDefinition() == typeof(IEquatable<>)
                    && type.GenericTypeArguments[0] == modelType
            ));

            var first = CreateComponentModel(
                modelType,
                scopeType,
                true,
                "Hint",
                "Demo",
                "public partial class Outer<T> where T : class",
                false,
                "public partial",
                "@event"
            );
            var equal = CreateComponentModel(
                modelType,
                scopeType,
                true,
                "Hint",
                "Demo",
                "public partial class Outer<T> where T : class",
                false,
                "public partial",
                "@event"
            );

            Assert.AreEqual(first, equal);
            Assert.AreEqual(first.GetHashCode(), equal.GetHashCode());

            var changedModels = new[]
            {
                CreateComponentModel(modelType, scopeType, false, "Hint", "Demo", "public partial class Outer<T> where T : class", false, "public partial", "@event"),
                CreateComponentModel(modelType, scopeType, true, "Other", "Demo", "public partial class Outer<T> where T : class", false, "public partial", "@event"),
                CreateComponentModel(modelType, scopeType, true, "Hint", "Other", "public partial class Outer<T> where T : class", false, "public partial", "@event"),
                CreateComponentModel(modelType, scopeType, true, "Hint", "Demo", "internal partial class Outer<T> where T : class", false, "public partial", "@event"),
                CreateComponentModel(modelType, scopeType, true, "Hint", "Demo", "public partial class Outer<T> where T : class", true, "public partial", "@event"),
                CreateComponentModel(modelType, scopeType, true, "Hint", "Demo", "public partial class Outer<T> where T : class", false, "internal partial", "@event"),
                CreateComponentModel(modelType, scopeType, true, "Hint", "Demo", "public partial class Outer<T> where T : class", false, "public partial", "other"),
                CreateComponentModel(modelType, scopeType, true, "Hint", "Demo", null, false, "public partial", "@event"),
            };

            foreach (var changed in changedModels)
                Assert.AreNotEqual(first, changed);
        }

        [TestMethod]
        public void ComponentPipeline_FinalDataAndOutputBoundary_ContainNoRoslynObjectsOrDiagnosticApis()
        {
            var assembly  = typeof(CollectionComponentGenerator).Assembly;
            var modelType = assembly.GetType("LatiosFramework.SourceGen.ComponentModel");

            Assert.IsNotNull(modelType);
            foreach (var field in modelType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.IsFalse(typeof(SyntaxNode).IsAssignableFrom(field.FieldType), field.Name);
                Assert.IsFalse(typeof(ISymbol).IsAssignableFrom(field.FieldType), field.Name);
                Assert.IsFalse(typeof(Compilation).IsAssignableFrom(field.FieldType), field.Name);
                Assert.IsFalse(typeof(SemanticModel).IsAssignableFrom(field.FieldType), field.Name);
                Assert.IsFalse(field.FieldType.IsArray, field.Name);
                Assert.IsFalse(field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(List<>), field.Name);
            }

            foreach (var generatorType in new[] { typeof(CollectionComponentGenerator), typeof(ManagedStructComponentGenerator) })
            {
                Assert.IsEmpty(generatorType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                    .Where(static field => field.FieldType == typeof(DiagnosticDescriptor)));
            }

            var writer = assembly.GetType("LatiosFramework.SourceGen.ComponentCodeWriter");
            Assert.IsNotNull(writer);
            var writeMethod = Assert.ContainsSingle(writer.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(static method => method.Name == "WriteComponentCode"));
            Assert.IsFalse(writeMethod.GetParameters().Any(static parameter => typeof(SyntaxNode).IsAssignableFrom(parameter.ParameterType)));
        }

        private static object CreateComponentModel(
            Type modelType,
            Type scopeType,
            bool isValid,
            string hintName,
            string namespaceName,
            string? scopeDeclaration,
            bool scopeHasBurstCompile,
            string targetModifiers,
            string targetIdentifier
        )
        {
            var scopeArray = Array.CreateInstance(scopeType, scopeDeclaration == null ? 0 : 1);
            if (scopeDeclaration != null)
            {
                var scope = Activator.CreateInstance(
                    scopeType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    args: new object[] { scopeDeclaration, scopeHasBurstCompile },
                    culture: null
                );
                Assert.IsNotNull(scope);
                scopeArray.SetValue(scope, 0);
            }
            var createRange = typeof(ImmutableArray).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method =>
                    method.Name == "CreateRange"
                    && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType.IsGenericType
                    && method.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                );
            var scopes = createRange.MakeGenericMethod(scopeType).Invoke(null, new object[] { scopeArray });
            Assert.IsNotNull(scopes);

            var model = Activator.CreateInstance(
                modelType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new[] { (object)isValid, hintName, namespaceName, scopes, targetModifiers, targetIdentifier },
                culture: null
            );
            Assert.IsNotNull(model);
            return model;
        }
    }
}
