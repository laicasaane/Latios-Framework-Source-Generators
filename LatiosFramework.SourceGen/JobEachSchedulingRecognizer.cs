using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace LatiosFramework.SourceGen
{
    // Recognizes, inside an ILatiosApi system, the calls which oblige that system to cache something on
    // behalf of an IJobEach: the scheduling extension methods, and api.GetDefaultQuery<TJob>() calls.
    //
    // Registration is by name, because IJobEachGenerator's output is not observable from here and may not
    // even be in this assembly. Both sides agree on jobSymbol.ToFullName() plus the suffixes below.
    //
    // Both are registered unconditionally: whether a job has any [Inject] fields is not knowable from a
    // dispatch call site, so IJobEachGenerator emits both types even when one of them is empty.
    internal static class JobEachSchedulingRecognizer
    {
        public const string ParameterHandlesSuffix = ".__HandlesForParameters";
        public const string GettableHandlesSuffix  = ".__HandlesToGet";

        static readonly HashSet<string> s_schedulingMethodNames = new HashSet<string>
        {
            "Run", "RunByRef",
            "RunImmediate", "RunImmediateByRef",
            "Schedule", "ScheduleByRef",
            "ScheduleParallel", "ScheduleParallelByRef",
        };

        public static bool TryProcess(IMethodSymbol methodSymbol, ref LatiosApiSemanticsExtractor.BodyContext bodyContext, StringBuilder stringBuilder)
        {
            if (TryProcessSchedulingCall(methodSymbol, ref bodyContext, stringBuilder))
                return true;
            return TryProcessDefaultQueryCall(methodSymbol, ref bodyContext, stringBuilder);
        }

        static bool TryProcessSchedulingCall(IMethodSymbol methodSymbol, ref LatiosApiSemanticsExtractor.BodyContext bodyContext, StringBuilder stringBuilder)
        {
            if (!s_schedulingMethodNames.Contains(methodSymbol.Name))
                return false;

            var containing = methodSymbol.ContainingType?.OriginalDefinition;
            if (containing == null || containing.Name != "JobEachExtensions" || containing.ContainingNamespace?.ToDisplayString() != "Latios")
                return false;

            if (methodSymbol.TypeArguments.Length < 1)
                return true;
            var jobType = methodSymbol.TypeArguments[0];

            RegisterHandles(jobType, bodyContext.fields, stringBuilder);

            // Only the overloads without an EntityQuery fall back to the cached default query, and their
            // api.GetDefaultQuery<T>() call lives in JobEachExtensions, never in the system, so the overload
            // shape is the only signal available here.
            bool takesQuery = methodSymbol.Parameters.Any(p => p.Type.ToFullName() == "global::Unity.Entities.EntityQuery");
            if (!takesQuery)
                RegisterDefaultQuery(jobType, bodyContext.jobQueries, stringBuilder);

            return true;
        }

        static bool TryProcessDefaultQueryCall(IMethodSymbol methodSymbol, ref LatiosApiSemanticsExtractor.BodyContext bodyContext, StringBuilder stringBuilder)
        {
            if (methodSymbol.Name != "GetDefaultQuery")
                return false;

            var containing = methodSymbol.ContainingType?.OriginalDefinition;
            if (containing == null || containing.Name != "LatiosApiInvoker" || containing.ContainingNamespace?.ToDisplayString() != "Latios")
                return false;

            if (methodSymbol.TypeArguments.Length < 1)
                return true;

            // The same cached field a no-query dispatch uses, so a count read here and the query scheduled
            // against can never diverge.
            RegisterDefaultQuery(methodSymbol.TypeArguments[0], bodyContext.jobQueries, stringBuilder);
            return true;
        }

        static void RegisterHandles(ITypeSymbol jobType, List<LatiosApiSemanticsExtractor.FieldEntry> fields, StringBuilder stringBuilder)
        {
            RegisterHandlesType(jobType, ParameterHandlesSuffix, "_HandlesForParameters", fields, stringBuilder);
            RegisterHandlesType(jobType, GettableHandlesSuffix, "_HandlesToGet", fields, stringBuilder);
        }

        static void RegisterHandlesType(ITypeSymbol jobType,
                                        string suffix,
                                        string nameSuffix,
                                        List<LatiosApiSemanticsExtractor.FieldEntry> fields,
                                        StringBuilder stringBuilder)
        {
            var handlesFullName = jobType.ToFullName() + suffix;
            if (LatiosApiSemanticsExtractor.TryFindExisting(fields, handlesFullName, null))
                return;

            LatiosApiSemanticsExtractor.AddField(fields,
                                                 handlesFullName,
                                                 jobType.ToSimpleName() + nameSuffix,
                                                 null,
                                                 LatiosApiSemanticsExtractor.FieldInitKind.Gettable,
                                                 null,
                                                 null,
                                                 stringBuilder);
        }

        static void RegisterDefaultQuery(ITypeSymbol jobType, List<LatiosApiSemanticsExtractor.JobQueryEntry> jobQueries, StringBuilder stringBuilder)
        {
            var jobFullName = jobType.ToFullName();
            foreach (var q in jobQueries)
            {
                if (q.jobFullName == jobFullName)
                    return;
            }

            jobQueries.Add(new LatiosApiSemanticsExtractor.JobQueryEntry
            {
                jobFullName = jobFullName,
                fieldName   = "m_query_" + LatiosApiSemanticsExtractor.SanitizeIdentifier(jobType.ToSimpleName(), stringBuilder),
            });
        }
    }
}
