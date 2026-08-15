// This file was originally written with Claude.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LatiosFramework.SourceGen
{
    internal static class LatiosApiSemanticsExtractor
    {
        // How a cached field is constructed. See PrintFieldConstructionExpression for the emitted form.
        public enum FieldInitKind
        {
            Gettable,
            GettableBool,
            // A built-in Unity Entities handle/lookup taking a readOnly bool.
            BuiltinWithBool,
            // A built-in Unity Entities handle/lookup taking nothing.
            BuiltinNoBool,
            // A built-in Unity Entities handle/lookup taking only the component type.
            BuiltinNoBoolGeneric,
            Injectable,
        }

        // Types are strings rather than ITypeSymbols because some entries name a type no generator has
        // emitted yet, most notably an IJobEach's nested handle types, which have no symbol to compare.
        public struct FieldEntry
        {
            public string        typeFullName;
            public string        simpleName;
            public bool?         boolValue;
            public string        fieldName;
            public FieldInitKind initKind;
            // Only populated for BuiltinWithBool / BuiltinNoBool / BuiltinNoBoolGeneric.
            public string builtinGetterMethodName;
            // Only populated for BuiltinWithBool / BuiltinNoBoolGeneric, whose getters take the component type.
            public string soloTypeArgumentFullName;
        }

        // Backs ILatiosApi.__GetJobDefaultQuery<T>(). Keyed by job type, since every entry is an EntityQuery.
        public struct JobQueryEntry
        {
            public string jobFullName;
            public string fieldName;
        }

        public struct BodyContext
        {
            public string              structShortName;
            public string              structFullName;
            public List<FieldEntry>    fields;
            public List<JobQueryEntry> jobQueries;
        }

        public static void ExtractApiSemantics(StructDeclarationSyntax structDeclarationSyntax,
                                               Compilation compilation,
                                               SourceProductionContext context,
                                               out BodyContext bodyContext)
        {
            bodyContext.structShortName = structDeclarationSyntax.Identifier.ToString();
            bodyContext.structFullName  = null;
            bodyContext.fields          = new List<FieldEntry>();
            bodyContext.jobQueries      = new List<JobQueryEntry>();

            var declaringModel = compilation.GetSemanticModel(structDeclarationSyntax.SyntaxTree);
            var structSymbol   = declaringModel.GetDeclaredSymbol(structDeclarationSyntax, context.CancellationToken) as INamedTypeSymbol;
            if (structSymbol == null)
                return;
            bodyContext.structFullName = structSymbol.ToFullName();

            var stringBuilder = new StringBuilder();

            // Get*() usages may live in any partial declaration, not just the one carrying the base list.
            foreach (var syntaxRef in structSymbol.DeclaringSyntaxReferences)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var node          = syntaxRef.GetSyntax(context.CancellationToken);
                var semanticModel = compilation.GetSemanticModel(node.SyntaxTree);

                foreach (var invocation in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    TryProcessInvocation(invocation, semanticModel, context, ref bodyContext, stringBuilder);
                }
            }
        }

        static void TryProcessInvocation(InvocationExpressionSyntax invocation,
                                         SemanticModel semanticModel,
                                         SourceProductionContext context,
                                         ref BodyContext bodyContext,
                                         StringBuilder stringBuilder)
        {
            if (!(semanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol methodSymbol))
                return;

            var fields = bodyContext.fields;

            if (methodSymbol.Name == "Inject" || methodSymbol.Name == "InjectByRef")
            {
                TryProcessInjectInvocation(methodSymbol, fields, stringBuilder);
                return;
            }

            if (JobEachSchedulingRecognizer.TryProcess(methodSymbol, ref bodyContext, stringBuilder))
                return;

            if (!methodSymbol.Name.StartsWith("Get", StringComparison.Ordinal))
                return;

            var thisOriginal = methodSymbol.ContainingType?.OriginalDefinition;
            if (thisOriginal == null || thisOriginal.Name != "LatiosApiInvoker" ||
                thisOriginal.ContainingNamespace?.ToDisplayString() != "Latios")
                return;

            var returnType    = methodSymbol.ReturnType;
            var boolParameter = methodSymbol.Parameters.FirstOrDefault(p => p.Type.SpecialType == SpecialType.System_Boolean);

            bool? boolValue = null;
            if (boolParameter != null)
            {
                var argumentSyntax = FindArgumentForParameter(invocation, boolParameter);
                var constantValue  = argumentSyntax != null?
                                     semanticModel.GetConstantValue(argumentSyntax.Expression, context.CancellationToken) :
                                         default;
                if (argumentSyntax == null || !constantValue.HasValue || !(constantValue.Value is bool b))
                {
                    context.ReportDiagnostic(Diagnostic.Create(ILatiosApiGenerator.NonConstantBoolArgumentDescriptor,
                                                               (argumentSyntax as SyntaxNode ?? invocation).GetLocation()));
                    return;
                }
                boolValue = b;
            }

            if (TryFindExisting(fields, returnType.ToFullName(), boolValue))
                return;

            var initKind = ClassifyReturnType(returnType, out var builtinGetterMethodName);
            if (initKind == null)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(ILatiosApiGenerator.UnsupportedReturnTypeDescriptor, invocation.GetLocation(), returnType.ToFullName()));
                return;
            }

            AddField(fields, returnType.ToFullName(), returnType.ToSimpleName(), boolValue, initKind.Value,
                     builtinGetterMethodName, GetSoloTypeArgumentFullName(returnType), stringBuilder);
        }

        internal static void AddField(List<FieldEntry> fields,
                                      string typeFullName,
                                      string simpleName,
                                      bool?  boolValue,
                                      FieldInitKind initKind,
                                      string builtinGetterMethodName,
                                      string soloTypeArgumentFullName,
                                      StringBuilder stringBuilder)
        {
            fields.Add(new FieldEntry
            {
                typeFullName             = typeFullName,
                simpleName               = simpleName,
                boolValue                = boolValue,
                fieldName                = MakeFieldName(fields, simpleName, boolValue, stringBuilder),
                initKind                 = initKind,
                builtinGetterMethodName  = builtinGetterMethodName,
                soloTypeArgumentFullName = soloTypeArgumentFullName,
            });
        }

        internal static string GetSoloTypeArgumentFullName(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol named && named.TypeArguments.Length == 1)
                return named.TypeArguments[0].ToFullName();
            return type.ToFullName();
        }

        // injectable.Inject(api) / injectable.InjectByRef(api) caches TInject like a Gettable field, so that
        // ILatiosApi.__Get<TInject>() can later resolve it.
        static void TryProcessInjectInvocation(IMethodSymbol methodSymbol, List<FieldEntry> fields, StringBuilder stringBuilder)
        {
            var thisOriginal = methodSymbol.ContainingType?.OriginalDefinition;
            if (thisOriginal == null || thisOriginal.Name != "LatiosApiCreateExtensions" ||
                thisOriginal.ContainingNamespace?.ToDisplayString() != "Latios")
                return;

            if (methodSymbol.TypeArguments.Length < 1)
                return;
            var injectableType = methodSymbol.TypeArguments[0];

            if (TryFindExisting(fields, injectableType.ToFullName(), null))
                return;

            AddField(fields, injectableType.ToFullName(), injectableType.ToSimpleName(), null, FieldInitKind.Injectable, null, null, stringBuilder);
        }

        static ArgumentSyntax FindArgumentForParameter(InvocationExpressionSyntax invocation, IParameterSymbol parameter)
        {
            var args = invocation.ArgumentList.Arguments;
            foreach (var arg in args)
            {
                if (arg.NameColon != null && arg.NameColon.Name.Identifier.ValueText == parameter.Name)
                    return arg;
            }
            if (parameter.Ordinal < args.Count && args[parameter.Ordinal].NameColon == null)
                return args[parameter.Ordinal];
            return null;
        }

        internal static bool TryFindExisting(List<FieldEntry> fields, string typeFullName, bool? boolValue)
        {
            foreach (var f in fields)
            {
                if (f.boolValue == boolValue && f.typeFullName == typeFullName)
                    return true;
            }
            return false;
        }

        // Also classifies [Inject] field types for InjectableSemanticsExtractor and JobEachSemanticsExtractor.
        internal static FieldInitKind? ClassifyReturnType(ITypeSymbol returnType, out string builtinGetterMethodName)
        {
            builtinGetterMethodName = null;

            if (returnType.InheritsFromInterface("Latios.ILatiosApiGettable"))
                return FieldInitKind.Gettable;
            if (returnType.InheritsFromInterface("Latios.ILatiosApiGettableBool"))
                return FieldInitKind.GettableBool;

            var original = returnType.OriginalDefinition;
            if (original.ContainingNamespace?.ToDisplayString() == "Unity.Entities")
            {
                switch (original.Name)
                {
                    case "ComponentTypeHandle":
                        builtinGetterMethodName = "GetComponentTypeHandle";
                        return FieldInitKind.BuiltinWithBool;
                    case "ComponentLookup":
                        builtinGetterMethodName = "GetComponentLookup";
                        return FieldInitKind.BuiltinWithBool;
                    case "BufferTypeHandle":
                        builtinGetterMethodName = "GetBufferTypeHandle";
                        return FieldInitKind.BuiltinWithBool;
                    case "BufferLookup":
                        builtinGetterMethodName = "GetBufferLookup";
                        return FieldInitKind.BuiltinWithBool;
                    case "SharedComponentTypeHandle":
                        builtinGetterMethodName = "GetSharedComponentTypeHandle";
                        return FieldInitKind.BuiltinNoBoolGeneric;
                    case "EntityTypeHandle":
                        builtinGetterMethodName = "GetEntityTypeHandle";
                        return FieldInitKind.BuiltinNoBool;
                    case "EntityStorageInfoLookup":
                        builtinGetterMethodName = "GetEntityStorageInfoLookup";
                        return FieldInitKind.BuiltinNoBool;
                }
            }
            return null;
        }

        static string MakeFieldName(List<FieldEntry> existingFields, string simpleName, bool? boolValue, StringBuilder stringBuilder)
        {
            var baseName = "m_" + SanitizeIdentifier(simpleName, stringBuilder);
            if (boolValue.HasValue)
                baseName += boolValue.Value ? "_true" : "_false";

            var candidate = baseName;
            var suffix    = 2;
            while (existingFields.Exists(f => f.fieldName == candidate))
                candidate = $"{baseName}_{suffix++}";
            return candidate;
        }

        internal static string SanitizeIdentifier(string s, StringBuilder stringBuilder)
        {
            stringBuilder.Clear();
            stringBuilder.EnsureCapacity(s.Length);
            foreach (var c in s)
                stringBuilder.Append(char.IsLetterOrDigit(c) ? c : '_');
            return stringBuilder.ToString();
        }
    }
}

