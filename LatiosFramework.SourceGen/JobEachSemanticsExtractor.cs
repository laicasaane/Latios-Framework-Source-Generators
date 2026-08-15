// This file was originally written with Claude.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LatiosFramework.SourceGen
{
    internal static class JobEachSemanticsExtractor
    {
        public enum ParameterKind
        {
            // A component passed by `in` or `ref`, sourced from a chunk data pointer.
            ComponentByRef,
            // RefRO<T> / RefRW<T>, sourced from a chunk NativeArray.
            ComponentRefWrapper,
            // DynamicBuffer<T>, sourced from a buffer accessor.
            Buffer,
            // The entity itself.
            Entity,
            // IJobEach.JobContext, which needs no handle.
            JobContext,
            // EnableableRefRO<T> / EnableableRefRW<T>, which pair a value ref with the enabled bit.
            EnableableRefWrapper,
            // Unity's EnabledRefRO<T> / EnabledRefRW<T>, which are the enabled bit alone.
            EnabledRefWrapper,
            // A type sourced through an IJobEach.IParameterHandle rather than directly from the chunk.
            ParameterHandle,
        }

        public struct ParameterEntry
        {
            public ParameterKind kind;
            public string        componentFullName;
            public bool          isReadOnly;
            // The index into HandleEntry list backing this parameter, or -1 for JobContext.
            public int handleIndex;
            // [NotRequired]: the component is left out of the query and may be absent at runtime.
            public bool isNotRequired;
            // The `ref`/`in` modifier the user wrote, which the call site has to match.
            public RefKind refKind;
        }

        public enum HandleKind
        {
            ComponentTypeHandle,
            BufferTypeHandle,
            EntityTypeHandle,
            // A user [Inject] field, replayed onto the job before scheduling.
            InjectedField,
            // An IJobEach.IParameterHandle instance, which sources its parameter itself.
            ParameterHandle,
        }

        public struct HandleEntry
        {
            public HandleKind kind;
            public string     componentFullName;
            public bool       isReadOnly;
            public string     fieldName;
            // Only for ParameterHandle: the closed handle type sourcing the parameter.
            public string handleTypeFullName;
            // Only for ParameterHandle: the IParameter it sources. Needed to name the handle's interface
            // when its members are reached through StaticAPI rather than called on the concrete type.
            public string parameterTypeFullName;
            // Only for ParameterHandle: the handle also implements IGroupedParameterHandle, so it needs the
            // capture/union/defer pipeline and the group callbacks.
            public bool isGrouped;
            // Only for InjectedField.
            public string                                     injectedFieldName;
            public string                                     injectedTypeFullName;
            public LatiosApiSemanticsExtractor.FieldInitKind  injectedInitKind;
            public string                                     injectedBuiltinGetterMethodName;
            public string                                     injectedSoloTypeArgumentFullName;
            public bool?                                      injectedBoolValue;
        }

        public struct QueryTerm
        {
            // The FluentQuery method to call, e.g. "With" or "WithoutEnabled".
            public string method;
            public string componentFullName;
            public bool   readOnly;
            public bool   hasReadOnlyArgument;
        }

        // Unity IJobEntity attributes with no IJobEach equivalent. Silently ignoring one would quietly
        // change what a migrated job matches, so they are reported instead.
        static readonly Dictionary<string, string> s_rejectedUnityAttributes = new Dictionary<string, string>
        {
            { "Unity.Entities.WithChangeFilterAttribute", "apply SetChangedVersionFilter to api.GetDefaultQuery<TJob>() instead" },
            { "Unity.Entities.WithAllAttribute", "use [WithEnabled] or [With]" },
            { "Unity.Entities.WithNoneAttribute", "use [WithoutEnabled] or [Without]" },
            { "Unity.Entities.WithAnyAttribute", "use [WithAnyEnabled]" },
            { "Unity.Entities.WithPresentAttribute", "use [With]" },
            { "Unity.Entities.WithAbsentAttribute", "use [Without]" },
            { "Unity.Entities.WithOptionsAttribute", "use the matching Latios option attribute" },
            { "Unity.Entities.WithEntityQueryOptionsAttribute", "use the matching Latios option attribute" },
        };

        public struct JobContext
        {
            public string             jobFullName;
            public bool               isValid;
            public List<string>       rejectedAttributeMessages;
            // The intersection of every selected parameter handle's ScheduleModeMask.
            public int  supportedModes;
            public bool hasGroupedParameter;
            public bool requiresEntityIndexInQuery;
            public bool hasBurstCompile;
            public string             unsupportedReason;
            public Location           unsupportedLocation;
            public List<ParameterEntry> parameters;
            public List<HandleEntry>    handles;
            public List<QueryTerm>      queryTerms;
            public List<string>         queryOptionMethods;
            public bool                 hasStaticAppendToQuery;
        }

        const string k_jobContextFullName = "global::Latios.IJobEach.JobContext";
        const string k_entityFullName     = "global::Unity.Entities.Entity";

        // Job-level attributes which map 1:1 onto a FluentQuery method taking component types.
        static readonly Dictionary<string, string> s_queryAttributeToMethod = new Dictionary<string, string>
        {
            { "Latios.WithAttribute", "With" },
            { "Latios.WithEnabledAttribute", "WithEnabled" },
            { "Latios.WithAnyEnabledAttribute", "WithAnyEnabled" },
            { "Latios.WithoutAttribute", "Without" },
            { "Latios.WithoutEnabledAttribute", "WithoutEnabled" },
            { "Unity.Entities.WithDisabledAttribute", "WithDisabled" },
        };

        // Job-level attributes which map onto a no-argument FluentQuery option method.
        static readonly Dictionary<string, string> s_optionAttributeToMethod = new Dictionary<string, string>
        {
            { "Latios.IgnoreEnableableBitsAttribute", "IgnoreEnableableBits" },
            { "Latios.IncludeDisabledEntitiesAttribute", "IncludeDisabledEntities" },
            { "Latios.IncludePrefabsAttribute", "IncludePrefabs" },
            { "Latios.IncludeSystemEntitiesAttribute", "IncludeSystemEntities" },
            { "Latios.IncludeMetaEntitiesAttribute", "IncludeMetaEntities" },
            { "Latios.UseWriteGroupsAttribute", "UseWriteGroups" },
        };

        public static void Extract(StructDeclarationSyntax structDeclarationSyntax,
                                   Compilation compilation,
                                   SourceProductionContext context,
                                   out JobContext job)
        {
            job                        = default;
            job.parameters             = new List<ParameterEntry>();
            job.handles                = new List<HandleEntry>();
            job.queryTerms             = new List<QueryTerm>();
            job.queryOptionMethods     = new List<string>();
            job.rejectedAttributeMessages = new List<string>();
            job.supportedModes            = ScheduleModeMaskAll;
            job.isValid                = true;

            var model       = compilation.GetSemanticModel(structDeclarationSyntax.SyntaxTree);
            var jobSymbol   = model.GetDeclaredSymbol(structDeclarationSyntax, context.CancellationToken) as INamedTypeSymbol;
            if (jobSymbol == null)
                return;
            job.jobFullName = jobSymbol.ToFullName();

            var executeMethods = jobSymbol.GetMembers().OfType<IMethodSymbol>().Where(m => m.Name == "Execute" && !m.IsStatic).ToList();
            if (executeMethods.Count != 1)
            {
                job.isValid             = false;
                job.unsupportedReason   = executeMethods.Count == 0 ? "has no Execute() method" : "has more than one Execute() method";
                job.unsupportedLocation = structDeclarationSyntax.Identifier.GetLocation();
                return;
            }

            job.hasStaticAppendToQuery = jobSymbol.GetMembers().OfType<IMethodSymbol>().Any(
                m => m.IsStatic && m.Name == "AppendToQuery" && m.Parameters.Length == 1);
            job.requiresEntityIndexInQuery = jobSymbol.HasAttribute("Latios.RequireEntityIndexInQueryAttribute");
            job.hasBurstCompile            = jobSymbol.HasAttribute("Unity.Burst.BurstCompileAttribute");

            // [AllowScheduleModes] narrows the job further; parameter handles narrow it again below.
            foreach (var attribute in jobSymbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != "Latios.AllowScheduleModesAttribute")
                    continue;
                if (attribute.ConstructorArguments.Length >= 1 && attribute.ConstructorArguments[0].Value is int allowed)
                    job.supportedModes &= allowed;
            }

            ExtractQueryAttributes(jobSymbol, ref job);
            ExtractInjectedFields(jobSymbol, ref job);

            var stringBuilder = new StringBuilder();
            foreach (var parameter in executeMethods[0].Parameters)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (!TryClassifyParameter(parameter, ref job, stringBuilder))
                {
                    job.isValid = false;
                    if (job.unsupportedReason == null)
                        job.unsupportedReason = $"has an Execute() parameter of unsupported type '{parameter.Type.ToSimpleName()}'";
                    job.unsupportedLocation = parameter.Locations.FirstOrDefault() ?? structDeclarationSyntax.Identifier.GetLocation();
                    return;
                }
            }
        }

        // The wrapper generics carry the enableable component as their sole type argument.
        static bool IsEnableableWrapper(ITypeSymbol type)
        {
            return type is INamedTypeSymbol named && named.TypeArguments.Length == 1 && named.TypeArguments[0].IsEnableableComponent();
        }

        static void ExtractQueryAttributes(INamedTypeSymbol jobSymbol, ref JobContext job)
        {
            foreach (var attribute in jobSymbol.GetAttributes())
            {
                var attributeName = attribute.AttributeClass?.ToDisplayString();
                if (attributeName == null)
                    continue;

                if (s_queryAttributeToMethod.TryGetValue(attributeName, out var method))
                {
                    foreach (var argument in attribute.ConstructorArguments)
                    {
                        foreach (var typeArgument in EnumerateTypeArguments(argument))
                        {
                            // Types named only by attribute are always requested read-only, because the job
                            // has no handle with which to access them.
                            job.queryTerms.Add(new QueryTerm
                            {
                                method              = method,
                                componentFullName   = typeArgument.ToFullName(),
                                readOnly            = true,
                                hasReadOnlyArgument = method == "With" || method == "WithEnabled" || method == "WithAnyEnabled" || method == "WithDisabled",
                            });
                        }
                    }
                }
                else if (s_optionAttributeToMethod.TryGetValue(attributeName, out var optionMethod))
                {
                    job.queryOptionMethods.Add(optionMethod);
                }
                else if (s_rejectedUnityAttributes.TryGetValue(attributeName, out var advice))
                {
                    job.rejectedAttributeMessages.Add($"[{attribute.AttributeClass.Name}] has no effect on an IJobEach; {advice}");
                }
            }
        }

        static IEnumerable<ITypeSymbol> EnumerateTypeArguments(TypedConstant argument)
        {
            if (argument.Kind == TypedConstantKind.Array)
            {
                foreach (var element in argument.Values)
                {
                    if (element.Value is ITypeSymbol elementType)
                        yield return elementType;
                }
            }
            else if (argument.Value is ITypeSymbol type)
            {
                yield return type;
            }
        }

        static void ExtractInjectedFields(INamedTypeSymbol jobSymbol, ref JobContext job)
        {
            foreach (var field in jobSymbol.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.IsStatic || field.IsConst || !field.HasAttribute("Latios.InjectAttribute"))
                    continue;

                var initKind = LatiosApiSemanticsExtractor.ClassifyReturnType(field.Type, out var builtinGetterMethodName);
                if (initKind == null)
                    continue;

                bool? boolValue = null;
                if (initKind == LatiosApiSemanticsExtractor.FieldInitKind.GettableBool ||
                    initKind == LatiosApiSemanticsExtractor.FieldInitKind.BuiltinWithBool)
                    boolValue = field.HasAttribute("Unity.Collections.ReadOnlyAttribute");

                job.handles.Add(new HandleEntry
                {
                    kind                             = HandleKind.InjectedField,
                    fieldName                        = "__inject_" + field.Name,
                    injectedFieldName                = field.Name,
                    injectedTypeFullName             = field.Type.ToFullName(),
                    injectedInitKind                 = initKind.Value,
                    injectedBuiltinGetterMethodName  = builtinGetterMethodName,
                    injectedSoloTypeArgumentFullName = LatiosApiSemanticsExtractor.GetSoloTypeArgumentFullName(field.Type),
                    injectedBoolValue                = boolValue,
                });
            }
        }

        static bool TryClassifyParameter(IParameterSymbol parameter, ref JobContext job, StringBuilder stringBuilder)
        {
            var type          = parameter.Type;
            var fullName      = type.ToFullName();
            var isNotRequired = parameter.HasAttribute("Latios.NotRequiredAttribute");
            var wantsEnabled  = parameter.HasAttribute("Latios.WithEnabledAttribute");

            // FluentQuery.WithEnabled<T> constrains T to IEnableableComponent, so catch this here rather
            // than letting it surface as a constraint violation inside generated code.
            if (wantsEnabled && !type.IsEnableableComponent() && !IsEnableableWrapper(type))
            {
                job.unsupportedReason =
                    $"has a [WithEnabled] Execute() parameter of type '{parameter.Type.ToSimpleName()}', which is not an IEnableableComponent";
                return false;
            }

            if (fullName == k_jobContextFullName)
            {
                job.parameters.Add(new ParameterEntry { kind = ParameterKind.JobContext, handleIndex = -1 , refKind = parameter.RefKind });
                return true;
            }

            if (fullName == k_entityFullName)
            {
                var handleIndex = GetOrAddHandle(ref job, HandleKind.EntityTypeHandle, null, true, stringBuilder);
                job.parameters.Add(new ParameterEntry { kind = ParameterKind.Entity, handleIndex = handleIndex , refKind = parameter.RefKind });
                return true;
            }

            var original = type.OriginalDefinition;
            if (original is INamedTypeSymbol named && named.TypeArguments.Length == 1)
            {
                if (type is INamedTypeSymbol constructed && constructed.TypeArguments.Length == 1)
                {
                    var componentFullName = constructed.TypeArguments[0].ToFullName();
                    switch (original.Name)
                    {
                        case "RefRO":
                        case "RefRW":
                        case "EnableableRefRO":
                        case "EnableableRefRW":
                        case "EnabledRefRO":
                        case "EnabledRefRW":
                        {
                            bool readOnly = original.Name.EndsWith("RO");
                            var  kind     = original.Name.StartsWith("EnableableRef") ? ParameterKind.EnableableRefWrapper :
                                            original.Name.StartsWith("EnabledRef")    ? ParameterKind.EnabledRefWrapper :
                                            ParameterKind.ComponentRefWrapper;
                            // The RW forms write either the value or the enabled bit, and both go through the
                            // same handle, so read-only tracks the wrapper's own RO/RW in every case.
                            var handleIndex = GetOrAddHandle(ref job, HandleKind.ComponentTypeHandle, componentFullName, readOnly, stringBuilder);
                            job.parameters.Add(new ParameterEntry
                            {
                                kind              = kind,
                                componentFullName = componentFullName,
                                isReadOnly        = readOnly,
                                handleIndex       = handleIndex,
                                isNotRequired     = isNotRequired,
                                refKind           = parameter.RefKind,
                                });
                            AddQueryTerm(ref job, componentFullName, readOnly, isNotRequired, wantsEnabled);
                            return true;
                        }
                        case "DynamicBuffer":
                        {
                            bool readOnly    = parameter.RefKind == RefKind.In;
                            var  handleIndex = GetOrAddHandle(ref job, HandleKind.BufferTypeHandle, componentFullName, readOnly, stringBuilder);
                            job.parameters.Add(new ParameterEntry
                            {
                                kind              = ParameterKind.Buffer,
                                componentFullName = componentFullName,
                                isReadOnly        = readOnly,
                                handleIndex       = handleIndex,
                                isNotRequired     = isNotRequired,
                                refKind           = parameter.RefKind,
                                });
                            AddQueryTerm(ref job, componentFullName, readOnly, isNotRequired, wantsEnabled);
                            return true;
                        }
                    }
                }
            }

            if (type.InheritsFromInterface("Latios.IJobEach.IParameter"))
                return TryClassifyCustomParameter(parameter, type, ref job, stringBuilder);

            if (type.IsValueType && type.IsComponent())
            {
                // Neither a zero-sized component nor an absent [NotRequired] one has a chunk data pointer to
                // hand out a ref into. Both want Has<T> instead.
                if (type.IsZeroSizedComponent() || isNotRequired)
                    return false;

                bool readOnly    = parameter.RefKind != RefKind.Ref;
                var  handleIndex = GetOrAddHandle(ref job, HandleKind.ComponentTypeHandle, fullName, readOnly, stringBuilder);
                job.parameters.Add(new ParameterEntry
                {
                    kind              = ParameterKind.ComponentByRef,
                    componentFullName = fullName,
                    isReadOnly        = readOnly,
                    handleIndex       = handleIndex,
                    isNotRequired     = isNotRequired,
                    refKind           = parameter.RefKind,
                    });
                AddQueryTerm(ref job, fullName, readOnly, isNotRequired, wantsEnabled);
                return true;
            }

            return false;
        }

        static void AddQueryTerm(ref JobContext job, string componentFullName, bool readOnly, bool isNotRequired, bool wantsEnabled)
        {
            // [NotRequired] means the entity may lack the component entirely, so it contributes nothing.
            if (isNotRequired)
                return;

            // FluentQuery.Build() dedups and promotes to read-write on conflicting access modes, so a type
            // appearing under several parameters can simply be appended more than once.
            job.queryTerms.Add(new QueryTerm
            {
                method              = wantsEnabled ? "WithEnabled" : "With",
                componentFullName   = componentFullName,
                readOnly            = readOnly,
                hasReadOnlyArgument = true,
            });
        }

        // Resolves which [ParameterHandle] sources a custom Execute() parameter. Selection is by decorating
        // attributes alone: a source is a candidate when all of its declared attributes are on the parameter,
        // and the one requiring the most wins. The mode mask takes no part in selection; it is intersected
        // into the job's supported modes, because a job has exactly one set of handles.
        static bool TryClassifyCustomParameter(IParameterSymbol parameter, ITypeSymbol type, ref JobContext job, StringBuilder stringBuilder)
        {
            var parameterAttributes = new HashSet<string>();
            foreach (var attribute in parameter.GetAttributes())
            {
                var name = attribute.AttributeClass?.ToDisplayString();
                if (name != null)
                    parameterAttributes.Add(name);
            }

            ITypeSymbol bestHandle       = null;
            int         bestModes        = (int)ScheduleModeMaskAll;
            int         bestDecoratorCount = -1;
            bool        ambiguous        = false;

            foreach (var attribute in type.OriginalDefinition.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != "Latios.IJobEach.ParameterHandleAttribute")
                    continue;
                if (attribute.ConstructorArguments.Length < 2)
                    continue;
                if (!(attribute.ConstructorArguments[0].Value is ITypeSymbol handleType))
                    continue;
                var modes = attribute.ConstructorArguments[1].Value is int m ? m : (int)ScheduleModeMaskAll;

                int  decoratorCount = 0;
                bool allPresent     = true;
                if (attribute.ConstructorArguments.Length >= 3)
                {
                    foreach (var decorator in attribute.ConstructorArguments[2].Values)
                    {
                        decoratorCount++;
                        if (!(decorator.Value is ITypeSymbol decoratorType) || !parameterAttributes.Contains(decoratorType.ToDisplayString()))
                            allPresent = false;
                    }
                }
                if (!allPresent)
                    continue;

                if (decoratorCount > bestDecoratorCount)
                {
                    bestHandle         = handleType;
                    bestModes          = modes;
                    bestDecoratorCount = decoratorCount;
                    ambiguous          = false;
                }
                else if (decoratorCount == bestDecoratorCount)
                {
                    ambiguous = true;
                }
            }

            if (bestHandle == null)
            {
                job.unsupportedReason =
                    $"has an Execute() parameter of type '{parameter.Type.ToSimpleName()}' with no [ParameterHandle] matching its decorating attributes";
                return false;
            }
            if (ambiguous)
            {
                job.unsupportedReason = $"has an Execute() parameter of type '{parameter.Type.ToSimpleName()}' whose [ParameterHandle] match is ambiguous";
                return false;
            }

            // An open generic handle such as typeof(HasHandle<>) is closed with the parameter's own arguments.
            var handleFullName = bestHandle.ToFullName();
            if (bestHandle is INamedTypeSymbol namedHandle && namedHandle.IsUnboundGenericType && type is INamedTypeSymbol namedParameter)
            {
                handleFullName = namedHandle.OriginalDefinition.ToFullName();
                var trimIndex  = handleFullName.IndexOf('<');
                if (trimIndex >= 0)
                    handleFullName = handleFullName.Substring(0, trimIndex);
                handleFullName += "<" + string.Join(", ", namedParameter.TypeArguments.Select(a => a.ToFullName())) + ">";
            }

            job.supportedModes &= bestModes;

            // Grouping keeps chunks that share state on one thread, which a single-threaded dispatch gets for
            // free, so a grouped handle never restricts the job's supported modes.
            // IGroupedParameterHandle is generic, so ToFullName() carries the closed type argument and never
            // equals the bare interface name. Match the original definition instead.
            var isGrouped = false;
            if (bestHandle is INamedTypeSymbol namedHandleSymbol)
            {
                foreach (var implemented in namedHandleSymbol.AllInterfaces)
                {
                    if (implemented.OriginalDefinition.ToFullName().StartsWith("global::Latios.IJobEach.IGroupedParameterHandle", System.StringComparison.Ordinal))
                    {
                        isGrouped = true;
                        break;
                    }
                }
            }
            if (isGrouped)
                job.hasGroupedParameter = true;

            var handleIndex = GetOrAddParameterHandle(ref job, handleFullName, type.ToFullName(), stringBuilder, isGrouped);
            job.parameters.Add(new ParameterEntry { kind = ParameterKind.ParameterHandle, handleIndex = handleIndex , refKind = parameter.RefKind });
            return true;
        }

        const int ScheduleModeMaskAll              = 15;
        const int ScheduleModeMaskScheduleParallel = 4;

        // Keyed on the handle and the parameter together, because one handle type may implement
        // IParameterHandle for several parameter types, and each closes the invokers differently.
        static int GetOrAddParameterHandle(ref JobContext job, string handleTypeFullName, string parameterTypeFullName, StringBuilder stringBuilder, bool isGrouped)
        {
            var fieldName = "__param_" + LatiosApiSemanticsExtractor.SanitizeIdentifier(handleTypeFullName, stringBuilder);
            var taken     = false;
            for (int i = 0; i < job.handles.Count; i++)
            {
                if (job.handles[i].kind != HandleKind.ParameterHandle || job.handles[i].handleTypeFullName != handleTypeFullName)
                    continue;
                if (job.handles[i].parameterTypeFullName == parameterTypeFullName)
                    return i;
                taken = true;
            }
            if (taken)
                fieldName += "_" + job.handles.Count.ToString();

            job.handles.Add(new HandleEntry
            {
                kind                  = HandleKind.ParameterHandle,
                isGrouped             = isGrouped,
                handleTypeFullName    = handleTypeFullName,
                parameterTypeFullName = parameterTypeFullName,
                isReadOnly            = false,
                fieldName             = fieldName,
            });
            return job.handles.Count - 1;
        }

        // Handles are shared across parameters of the same type, promoted to read-write if any use is.
        static int GetOrAddHandle(ref JobContext job, HandleKind kind, string componentFullName, bool readOnly, StringBuilder stringBuilder)
        {
            for (int i = 0; i < job.handles.Count; i++)
            {
                var existing = job.handles[i];
                if (existing.kind != kind || existing.componentFullName != componentFullName)
                    continue;
                if (!readOnly && existing.isReadOnly)
                {
                    existing.isReadOnly = false;
                    job.handles[i]      = existing;
                }
                return i;
            }

            string fieldName;
            switch (kind)
            {
                case HandleKind.EntityTypeHandle:
                    fieldName = "__entityHandle";
                    break;
                case HandleKind.BufferTypeHandle:
                    fieldName = "__buffer_" + LatiosApiSemanticsExtractor.SanitizeIdentifier(componentFullName, stringBuilder);
                    break;
                default:
                    fieldName = "__component_" + LatiosApiSemanticsExtractor.SanitizeIdentifier(componentFullName, stringBuilder);
                    break;
            }

            job.handles.Add(new HandleEntry
            {
                kind              = kind,
                componentFullName = componentFullName,
                isReadOnly        = readOnly,
                fieldName         = fieldName,
            });
            return job.handles.Count - 1;
        }
    }
}
