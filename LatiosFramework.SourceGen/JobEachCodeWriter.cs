// This file was originally written with Claude.
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LatiosFramework.SourceGen
{
    internal static class JobEachCodeWriter
    {
        public static string WriteJobCode(StructDeclarationSyntax structDeclaration, ref JobEachSemanticsExtractor.JobContext job)
        {
            var scopePrinter = new SyntaxNodeScopePrinter(Printer.DefaultLarge, structDeclaration.Parent);
            scopePrinter.PrintOpen(false);
            var printer = scopePrinter.Printer;

            printer.PrintLine("[global::System.Runtime.CompilerServices.CompilerGenerated]");
            printer.PrintBeginLine();
            foreach (var m in structDeclaration.Modifiers)
                printer.Print(m.ToString()).Print(" ");
            printer.Print("struct ").Print(structDeclaration.Identifier.Text);
            // An unsupported job still gets its handle types and a throwing dispatch, so its one error is
            // LATIOS_SG_09 rather than a cascade of missing-type errors in generated code.
            if (job.isValid)
                printer.Print(" : global::Unity.Entities.IJobChunk");
            printer.PrintEndLine();
            printer.OpenScope();

            PrintHandlesStruct(ref printer, ref job, "__HandlesForParameters", false);
            printer.PrintBeginLine().PrintEndLine();
            PrintHandlesStruct(ref printer, ref job, "__HandlesToGet", true);
            printer.PrintBeginLine().PrintEndLine();

            printer.PrintLine("private __HandlesForParameters __handles;");
            printer.PrintLine("private global::Latios.IJobEach.ScheduleMode __mode;");
            printer.PrintLine("private uint __lastSystemVersion;");
            // The index of the next matching entity within the query, or -1 when unavailable. Single-threaded
            // dispatches carry it across chunks; the deferred path resets it per chunk from the capture pass.
            printer.PrintLine("internal int __entityIndexInQuery;");
            printer.PrintBeginLine("internal const global::Latios.IJobEach.ScheduleModeMask __supportedModes = (global::Latios.IJobEach.ScheduleModeMask)").Print(
                job.supportedModes.ToString()).PrintEndLine(";");
            printer.PrintBeginLine().PrintEndLine();

            if (job.isValid)
            {
                PrintExecute(ref printer, ref job);
                printer.PrintBeginLine().PrintEndLine();
                if (UsesDeferredDispatch(ref job))
                {
                    PrintGroupedChunksJob(ref printer, ref job, structDeclaration.Identifier.Text);
                    printer.PrintBeginLine().PrintEndLine();
                }
            }

            PrintSchedule(ref printer, ref job);
            printer.PrintBeginLine().PrintEndLine();
            PrintScheduleWithDeps(ref printer, ref job);
            printer.PrintBeginLine().PrintEndLine();
            PrintAppendToQuery(ref printer, ref job);

            printer.CloseScope();
            scopePrinter.PrintClose();
            return printer.Result;
        }

        // Injected handles are copied onto the job's own fields before scheduling, so the cache they came
        // from must not travel with the job: the safety system would see one read-write container reachable
        // twice from the same job struct and reject the dispatch as aliasing. They therefore live in a
        // separate type the dispatch reads into a local and drops, rather than in the job's handle field.
        static bool BelongsToHandlesStruct(JobEachSemanticsExtractor.HandleEntry handle, bool injectedOnly)
        {
            return injectedOnly == (handle.kind == JobEachSemanticsExtractor.HandleKind.InjectedField);
        }

        static bool HasInjectedFields(ref JobEachSemanticsExtractor.JobContext job)
        {
            foreach (var handle in job.handles)
            {
                if (handle.kind == JobEachSemanticsExtractor.HandleKind.InjectedField)
                    return true;
            }
            return false;
        }

        // Both types are emitted even when empty. The scheduling system registers them by name from the
        // dispatch call site, where it cannot see whether the job has any [Inject] fields, and the job may
        // not even be in the same assembly.
        static void PrintHandlesStruct(ref Printer printer, ref JobEachSemanticsExtractor.JobContext job, string structName, bool injectedOnly)
        {
            // Public because the scheduling system names this type in its cache, and it may live in a
            // different assembly than the system.
            printer.PrintBeginLine("public struct ").Print(structName).PrintEndLine(" : global::Latios.ILatiosApiGettable");
            printer.OpenScope();

            foreach (var handle in job.handles)
            {
                if (!BelongsToHandlesStruct(handle, injectedOnly))
                    continue;
                // Unity's job safety system walks nested struct fields, so a read-only handle must carry
                // [ReadOnly] here or scheduling throws about the container being read only.
                if (IsReadOnlyHandle(handle))
                    printer.PrintLine("[global::Unity.Collections.ReadOnly]");
                printer.PrintBeginLine("public ");
                PrintHandleType(ref printer, handle);
                printer.Print(" ").Print(handle.fieldName).PrintEndLine(";");
            }
            printer.PrintBeginLine().PrintEndLine();

            printer.PrintLine("void global::Latios.ILatiosApiGettable.CreateForApi(ref global::Unity.Entities.SystemState state)");
            printer.OpenScope();
            foreach (var handle in job.handles)
            {
                if (!BelongsToHandlesStruct(handle, injectedOnly))
                    continue;
                printer.PrintBeginLine(handle.fieldName).Print(" = ");
                switch (handle.kind)
                {
                    case JobEachSemanticsExtractor.HandleKind.ComponentTypeHandle:
                        printer.Print("state.GetComponentTypeHandle<").Print(handle.componentFullName).Print(">(").Print(handle.isReadOnly ? "true" : "false").Print(")");
                        break;
                    case JobEachSemanticsExtractor.HandleKind.BufferTypeHandle:
                        printer.Print("state.GetBufferTypeHandle<").Print(handle.componentFullName).Print(">(").Print(handle.isReadOnly ? "true" : "false").Print(")");
                        break;
                    case JobEachSemanticsExtractor.HandleKind.EntityTypeHandle:
                        printer.Print("state.GetEntityTypeHandle()");
                        break;
                    case JobEachSemanticsExtractor.HandleKind.ParameterHandle:
                        printer.Print("global::Latios.InternalSourceGen.StaticAPI.Create<").Print(handle.handleTypeFullName).Print(">(ref state)");
                        break;
                    default:
                        ILatiosApiCodeWriter.PrintFieldConstructionExpression(ref printer, handle.injectedInitKind, handle.injectedTypeFullName,
                                                                              handle.injectedSoloTypeArgumentFullName, handle.injectedBoolValue,
                                                                              handle.injectedBuiltinGetterMethodName);
                        break;
                }
                printer.PrintEndLine(";");
            }
            printer.CloseScope();
            printer.PrintBeginLine().PrintEndLine();

            printer.PrintLine("void global::Latios.ILatiosApiGettable.UpdateForApi(ref global::Unity.Entities.SystemState state)");
            printer.OpenScope();
            foreach (var handle in job.handles)
            {
                if (!BelongsToHandlesStruct(handle, injectedOnly))
                    continue;
                if (handle.kind == JobEachSemanticsExtractor.HandleKind.ParameterHandle)
                {
                    printer.PrintBeginLine("global::Latios.InternalSourceGen.StaticAPI.UpdateGettable(ref ").Print(handle.fieldName).PrintEndLine(", ref state);");
                    continue;
                }
                if (handle.kind == JobEachSemanticsExtractor.HandleKind.InjectedField)
                {
                    // A handle type may implement ILatiosApiGettable explicitly, so the update has to be
                    // routed through StaticAPI rather than called directly on the field.
                    switch (handle.injectedInitKind)
                    {
                        case LatiosApiSemanticsExtractor.FieldInitKind.Gettable:
                            printer.PrintBeginLine("global::Latios.InternalSourceGen.StaticAPI.UpdateGettable(ref ").Print(handle.fieldName).PrintEndLine(", ref state);");
                            continue;
                        case LatiosApiSemanticsExtractor.FieldInitKind.GettableBool:
                            printer.PrintBeginLine("global::Latios.InternalSourceGen.StaticAPI.UpdateGettableBool(ref ").Print(handle.fieldName).PrintEndLine(
                                ", ref state);");
                            continue;
                    }
                }
                printer.PrintBeginLine(handle.fieldName).PrintEndLine(".Update(ref state);");
            }
            printer.CloseScope();

            printer.CloseScope();
        }

        // Grouping and entityIndexInQuery both need the chunks captured up front, which turns the dispatch
        // into an IJobParallelForDefer over the resulting groups.
        static bool UsesDeferredDispatch(ref JobEachSemanticsExtractor.JobContext job)
        {
            return job.hasGroupedParameter || job.requiresEntityIndexInQuery;
        }

        static void PrintGroupedChunksJob(ref Printer printer, ref JobEachSemanticsExtractor.JobContext job, string jobName)
        {
            // Burst is the user's choice on their own job; mirror it rather than imposing it, or a job
            // deliberately left unburstable would fail to compile through this generated type.
            if (job.hasBurstCompile)
                printer.PrintLine("[global::Unity.Burst.BurstCompile]");
            // IJob as well as IJobParallelForDefer, so the single-threaded modes can still get the group
            // callbacks by running the whole query as one group.
            printer.PrintLine("public struct __GroupedChunks : global::Unity.Jobs.IJobParallelForDefer, global::Unity.Jobs.IJob");
            printer.OpenScope();
            printer.PrintBeginLine("public ").Print(jobName).PrintEndLine(" job;");
            printer.PrintLine("public global::Latios.IJobEach.ChunkGroups groups;");
            printer.PrintBeginLine().PrintEndLine();

            // A query matching no chunks captures no groups, so the single-group dispatch has nothing to run.
            printer.PrintLine("void global::Unity.Jobs.IJob.Execute()");
            printer.OpenScope();
            printer.PrintLine("if (groups.groupRanges.Length > 0)");
            printer.PrintLine("    Execute(0);");
            printer.CloseScope();
            printer.PrintBeginLine().PrintEndLine();

            printer.PrintLine("public void Execute(int groupIndex)");
            printer.OpenScope();
            printer.PrintLine("var __range = groups.groupRanges[groupIndex];");
            printer.PrintLine(
                "var __groupContext = global::Latios.InternalSourceGen.StaticAPI.CreateJobEachGroupContext(groupIndex, __range.count, job.__lastSystemVersion, job.__mode);");

            foreach (var handle in job.handles)
            {
                if (!handle.isGrouped)
                    continue;
                printer.PrintBeginLine();
                PrintHandleInvoke(ref printer, handle, "CallHandleOnGroupBegin");
                printer.Print("(ref job.__handles.").Print(handle.fieldName).PrintEndLine(", in __groupContext);");
            }

            printer.PrintLine("for (int __c = 0; __c < __range.count; __c++)");
            printer.OpenScope();
            printer.PrintLine("var __captured = groups.chunks[__range.start + __c];");
            // Query-order index, captured before grouping reordered the chunks.
            printer.PrintLine("job.__entityIndexInQuery = __captured.firstEntityIndexInQuery;");
            printer.PrintLine(
                "job.__ExecuteChunk(in __captured.chunk, __captured.unfilteredChunkIndex, __captured.useEnabledMask, in __captured.chunkEnabledMask);");
            printer.CloseScope();

            // Reverse order, mirroring the chunk callbacks.
            for (int i = job.handles.Count - 1; i >= 0; i--)
            {
                if (!job.handles[i].isGrouped)
                    continue;
                printer.PrintBeginLine();
                PrintHandleInvoke(ref printer, job.handles[i], "CallHandleOnGroupEnd");
                printer.Print("(ref job.__handles.").Print(job.handles[i].fieldName).PrintEndLine(", in __groupContext);");
            }
            printer.CloseScope();
            printer.CloseScope();
        }

        // A handle may implement IParameterHandle explicitly, so every one of its members is reached through
        // a StaticAPI invoker closed over the handle and its parameter rather than called on the field.
        static void PrintHandleInvoke(ref Printer printer, JobEachSemanticsExtractor.HandleEntry handle, string invoker)
        {
            printer.Print("global::Latios.InternalSourceGen.StaticAPI.").Print(invoker).Print("<").Print(handle.handleTypeFullName).Print(", ").Print(
                handle.parameterTypeFullName).Print(">");
        }

        static bool UsesHandle(ref JobEachSemanticsExtractor.JobContext job, int handleIndex, JobEachSemanticsExtractor.ParameterKind kind)
        {
            foreach (var parameter in job.parameters)
            {
                if (parameter.handleIndex == handleIndex && parameter.kind == kind)
                    return true;
            }
            return false;
        }

        static bool IsReadOnlyHandle(JobEachSemanticsExtractor.HandleEntry handle)
        {
            if (handle.kind == JobEachSemanticsExtractor.HandleKind.InjectedField)
                return handle.injectedBoolValue == true;
            if (handle.kind == JobEachSemanticsExtractor.HandleKind.EntityTypeHandle)
                return true;
            return handle.isReadOnly;
        }

        static void PrintHandleType(ref Printer printer, JobEachSemanticsExtractor.HandleEntry handle)
        {
            switch (handle.kind)
            {
                case JobEachSemanticsExtractor.HandleKind.ComponentTypeHandle:
                    printer.Print("global::Unity.Entities.ComponentTypeHandle<").Print(handle.componentFullName).Print(">");
                    break;
                case JobEachSemanticsExtractor.HandleKind.BufferTypeHandle:
                    printer.Print("global::Unity.Entities.BufferTypeHandle<").Print(handle.componentFullName).Print(">");
                    break;
                case JobEachSemanticsExtractor.HandleKind.EntityTypeHandle:
                    printer.Print("global::Unity.Entities.EntityTypeHandle");
                    break;
                case JobEachSemanticsExtractor.HandleKind.ParameterHandle:
                    printer.Print(handle.handleTypeFullName);
                    break;
                default:
                    printer.Print(handle.injectedTypeFullName);
                    break;
            }
        }

        static void PrintExecute(ref Printer printer, ref JobEachSemanticsExtractor.JobContext job)
        {
            // Explicit interface implementation so it can never collide with the user's variadic Execute(),
            // forwarding to a public method the generated defer job can also call without boxing.
            printer.PrintLine(
                "void global::Unity.Entities.IJobChunk.Execute(in global::Unity.Entities.ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in global::Unity.Burst.Intrinsics.v128 chunkEnabledMask) => __ExecuteChunk(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);");
            printer.PrintBeginLine().PrintEndLine();

            printer.PrintLine(
                "internal void __ExecuteChunk(in global::Unity.Entities.ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in global::Unity.Burst.Intrinsics.v128 chunkEnabledMask)");
            printer.OpenScope();

            printer.PrintLine(
                "var __context = global::Latios.InternalSourceGen.StaticAPI.CreateJobContext(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask, __lastSystemVersion, __mode, __entityIndexInQuery);");

            printer.PrintLine("var __chunkWasExecuted = global::Latios.InternalSourceGen.StaticAPI.CallOnChunkBegin(ref this, in __context);");

            printer.PrintLine("if (__chunkWasExecuted)");
            printer.OpenScope();

            for (int i = 0; i < job.handles.Count; i++)
            {
                var handle = job.handles[i];
                switch (handle.kind)
                {
                    case JobEachSemanticsExtractor.HandleKind.ComponentTypeHandle:
                        // Only the accessors actually consumed are hoisted. Where a [NotRequired] component is
                        // absent the pointer would be null but the NativeArray is merely empty, which is what
                        // RefRO/RefRW.Optional() expects.
                        if (UsesHandle(ref job, i, JobEachSemanticsExtractor.ParameterKind.ComponentByRef))
                            printer.PrintBeginLine("var __ptr").Print(i.ToString()).Print(
                                " = global::Latios.InternalSourceGen.StaticAPI.GetComponentPtr").Print(handle.isReadOnly ? "RO" : "RW").Print("(in chunk, ref __handles.")
                            .Print(handle.fieldName).PrintEndLine(");");
                        if (UsesHandle(ref job, i, JobEachSemanticsExtractor.ParameterKind.ComponentRefWrapper) ||
                            UsesHandle(ref job, i, JobEachSemanticsExtractor.ParameterKind.EnableableRefWrapper))
                            printer.PrintBeginLine("var __arr").Print(i.ToString()).Print(" = chunk.GetNativeArray(ref __handles.").Print(handle.fieldName).PrintEndLine(
                                ");");
                        break;
                    case JobEachSemanticsExtractor.HandleKind.BufferTypeHandle:
                        printer.PrintBeginLine("var __acc").Print(i.ToString()).Print(" = chunk.GetBufferAccessor(ref __handles.").Print(handle.fieldName).PrintEndLine(
                            ");");
                        break;
                    case JobEachSemanticsExtractor.HandleKind.EntityTypeHandle:
                        printer.PrintBeginLine("var __entities = global::Latios.InternalSourceGen.StaticAPI.GetEntityPtr(in chunk, __handles.").Print(
                            handle.fieldName).PrintEndLine(");");
                        break;
                    case JobEachSemanticsExtractor.HandleKind.ParameterHandle:
                        printer.PrintBeginLine();
                        PrintHandleInvoke(ref printer, handle, "CallHandleOnChunkBegin");
                        printer.Print("(ref __handles.").Print(handle.fieldName).PrintEndLine(", in __context);");
                        break;
                }
            }

            // Enabled masks, only for components whose parameters read or write bits. Iterating handles rather
            // than parameters keeps the mask declared once when several parameters share a component type.
            for (int i = 0; i < job.handles.Count; i++)
            {
                if (!UsesHandle(ref job, i, JobEachSemanticsExtractor.ParameterKind.EnableableRefWrapper) &&
                    !UsesHandle(ref job, i, JobEachSemanticsExtractor.ParameterKind.EnabledRefWrapper))
                    continue;
                printer.PrintBeginLine("var __mask").Print(i.ToString()).Print(" = chunk.GetEnabledMask(ref __handles.").Print(
                    job.handles[i].fieldName).PrintEndLine(");");
            }

            printer.PrintLine("var __entityIndex = __entityIndexInQuery;");
            // Iterating enabled entities as contiguous runs rather than one at a time keeps the inner loop
            // free of per-entity mask work.
            printer.PrintLine("var __enumerator = new global::Latios.ChunkEntityBatchEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);");
            printer.PrintLine("while (__enumerator.NextRange(out var __rangeStart, out var __rangeCount))");
            printer.OpenScope();
            printer.PrintLine("for (int __i = __rangeStart, __rangeEnd = __rangeStart + __rangeCount; __i < __rangeEnd; __i++)");
            printer.OpenScope();
            printer.PrintLine("global::Latios.InternalSourceGen.StaticAPI.SetJobContextEntity(ref __context, __i, __entityIndex);");

            // Hoist the arguments which are not already lvalues, since `ref`/`in` requires one.
            for (int i = 0; i < job.parameters.Count; i++)
            {
                if (!NeedsLocal(job.parameters[i]))
                    continue;
                printer.PrintBeginLine("var __arg").Print(i.ToString()).Print(" = ");
                PrintArgumentValue(ref printer, ref job, job.parameters[i]);
                printer.PrintEndLine(";");
            }

            printer.PrintBeginLine("Execute(");
            for (int i = 0; i < job.parameters.Count; i++)
            {
                if (i > 0)
                    printer.Print(", ");
                var parameter = job.parameters[i];
                printer.Print(RefKeyword(parameter.refKind));
                if (NeedsLocal(parameter))
                    printer.Print("__arg").Print(i.ToString());
                else
                    PrintArgumentValue(ref printer, ref job, parameter);
            }
            printer.PrintEndLine(");");
            // The per-entity index stays pinned at the -1 sentinel when the dispatch cannot provide one.
            printer.PrintLine("if (__entityIndex >= 0) __entityIndex++;");
            printer.CloseScope();
            printer.CloseScope();

            printer.CloseScope();

            // Outside the executed check: a skipped chunk still holds matching entities, so the running index
            // has to advance past them or a single-threaded dispatch would report different indices than the
            // deferred one, which seeds each chunk from the capture pass.
            printer.PrintLine(
                "__entityIndexInQuery += __entityIndexInQuery >= 0 ? global::Latios.InternalSourceGen.StaticAPI.CountEntitiesInChunk(in chunk, useEnabledMask, in chunkEnabledMask) : 0;");

            foreach (var handle in job.handles)
            {
                if (handle.kind != JobEachSemanticsExtractor.HandleKind.ParameterHandle)
                    continue;
                printer.PrintBeginLine();
                PrintHandleInvoke(ref printer, handle, "CallHandleOnChunkEnd");
                printer.Print("(ref __handles.").Print(handle.fieldName).PrintEndLine(", in __context, __chunkWasExecuted);");
            }
            printer.PrintLine("global::Latios.InternalSourceGen.StaticAPI.CallOnChunkEnd(ref this, in __context, __chunkWasExecuted);");

            printer.CloseScope();
        }

        // RunImmediate never touches the job system, so there is no capture pass to hang the group callbacks
        // off. The whole query is one group and the callbacks bracket the inline run.
        static void PrintRunImmediateBody(ref Printer printer, ref JobEachSemanticsExtractor.JobContext job)
        {
            if (!job.hasGroupedParameter)
            {
                printer.PrintLine("    global::Latios.InternalSourceGen.StaticAPI.RunWithoutJobs(ref this, query);");
                return;
            }

            printer.PrintLine("    {");
            printer.PrintLine(
                "        var __groupContext = global::Latios.InternalSourceGen.StaticAPI.CreateJobEachGroupContext(0, query.CalculateChunkCount(), __lastSystemVersion, mode);");
            foreach (var handle in job.handles)
            {
                if (!handle.isGrouped)
                    continue;
                printer.PrintBeginLine("        ");
                PrintHandleInvoke(ref printer, handle, "CallHandleOnGroupBegin");
                printer.Print("(ref __handles.").Print(handle.fieldName).PrintEndLine(", in __groupContext);");
            }
            printer.PrintLine("        global::Latios.InternalSourceGen.StaticAPI.RunWithoutJobs(ref this, query);");
            for (int i = job.handles.Count - 1; i >= 0; i--)
            {
                if (!job.handles[i].isGrouped)
                    continue;
                printer.PrintBeginLine("        ");
                PrintHandleInvoke(ref printer, job.handles[i], "CallHandleOnGroupEnd");
                printer.Print("(ref __handles.").Print(job.handles[i].fieldName).PrintEndLine(", in __groupContext);");
            }
            printer.PrintLine("    }");
        }

        static string RefKeyword(Microsoft.CodeAnalysis.RefKind refKind)
        {
            switch (refKind)
            {
                case Microsoft.CodeAnalysis.RefKind.Ref: return "ref ";
                case Microsoft.CodeAnalysis.RefKind.In:  return "in ";
                default:                                 return string.Empty;
            }
        }

        // ComponentByRef indexes a pointer and JobContext is already a local, so both are lvalues already.
        // Every other kind is a call or a by-value indexer and must be hoisted to be passed by ref or in.
        static bool NeedsLocal(JobEachSemanticsExtractor.ParameterEntry parameter)
        {
            if (parameter.refKind == Microsoft.CodeAnalysis.RefKind.None)
                return false;
            return parameter.kind != JobEachSemanticsExtractor.ParameterKind.ComponentByRef &&
                   parameter.kind != JobEachSemanticsExtractor.ParameterKind.JobContext;
        }

        static void PrintArgumentValue(ref Printer printer, ref JobEachSemanticsExtractor.JobContext job, JobEachSemanticsExtractor.ParameterEntry parameter)
        {
            switch (parameter.kind)
            {
                case JobEachSemanticsExtractor.ParameterKind.JobContext:
                    printer.Print("__context");
                    break;
                case JobEachSemanticsExtractor.ParameterKind.Entity:
                    printer.Print("__entities[__i]");
                    break;
                case JobEachSemanticsExtractor.ParameterKind.ComponentByRef:
                    printer.Print("__ptr").Print(parameter.handleIndex.ToString()).Print("[__i]");
                    break;
                case JobEachSemanticsExtractor.ParameterKind.ComponentRefWrapper:
                {
                    var index    = parameter.handleIndex.ToString();
                    var typeText = "global::Unity.Entities.Ref" + (parameter.isReadOnly ? "RO<" : "RW<") + parameter.componentFullName + ">";
                    // Optional() yields a null reference when the component is absent, which is what makes
                    // IsValid the documented way to test a [NotRequired] parameter.
                    if (parameter.isNotRequired)
                        printer.Print(typeText).Print(".Optional(__arr").Print(index).Print(", __i)");
                    else
                        printer.Print("new ").Print(typeText).Print("(__arr").Print(index).Print(", __i)");
                    break;
                }
                case JobEachSemanticsExtractor.ParameterKind.EnableableRefWrapper:
                {
                    var index = parameter.handleIndex.ToString();
                    var roRw  = parameter.isReadOnly ? "RO<" : "RW<";
                    // An absent component yields an empty array and an invalid mask, so both halves must come
                    // from the Optional forms. Optional() nulls only the value, leaving IsValid as the test.
                    var maskGet  = parameter.isNotRequired ? ".GetOptionalEnabledRef" : ".GetEnabledRef";
                    var typeText = "global::Latios.EnableableRef" + roRw + parameter.componentFullName + ">";
                    if (parameter.isNotRequired)
                        printer.Print(typeText).Print(".Optional(__arr").Print(index);
                    else
                        printer.Print("new ").Print(typeText).Print("(__arr").Print(index);
                    printer.Print(", __i, __mask").Print(index).Print(maskGet).Print(roRw).Print(parameter.componentFullName).Print(">(__i))");
                    break;
                }
                case JobEachSemanticsExtractor.ParameterKind.EnabledRefWrapper:
                {
                    var index = parameter.handleIndex.ToString();
                    // GetOptionalEnabledRef*() yields EnabledRef*.Null for a chunk lacking the component,
                    // which is what makes IsValid the way to test a [NotRequired] parameter.
                    printer.Print("__mask").Print(index).Print(parameter.isNotRequired ? ".GetOptionalEnabledRef" : ".GetEnabledRef").Print(
                        parameter.isReadOnly ? "RO<" : "RW<").Print(parameter.componentFullName).Print(">(__i)");
                    break;
                }
                case JobEachSemanticsExtractor.ParameterKind.ParameterHandle:
                {
                    var handle = job.handles[parameter.handleIndex];
                    PrintHandleInvoke(ref printer, handle, "CallHandleGetParameter");
                    printer.Print("(ref __handles.").Print(handle.fieldName).Print(", in __context)");
                    break;
                }
                case JobEachSemanticsExtractor.ParameterKind.Buffer:
                {
                    var index = parameter.handleIndex.ToString();
                    // An absent buffer yields a zero-length accessor, so indexing it would throw. The default
                    // DynamicBuffer reports IsCreated false, which is how a [NotRequired] buffer is tested.
                    if (parameter.isNotRequired)
                        printer.Print("__acc").Print(index).Print(".Length > 0 ? __acc").Print(index).Print("[__i] : default");
                    else
                        printer.Print("__acc").Print(index).Print("[__i]");
                    break;
                }
            }
        }

        static void PrintSchedule(ref Printer printer, ref JobEachSemanticsExtractor.JobContext job)
        {
            printer.PrintLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            printer.PrintLine(
                "void global::Latios.IJobEach.__Schedule<TSystem>(global::Latios.LatiosApiInvoker<TSystem> api, global::Unity.Entities.EntityQuery query, global::Latios.IJobEach.ScheduleMode mode)");
            printer.OpenScope();
            if (!job.isValid)
            {
                PrintUnsupportedThrow(ref printer, ref job);
                printer.CloseScope();
                return;
            }
            PrintDispatchPrologue(ref printer, ref job);
            printer.PrintLine("ref var state = ref global::Latios.InternalSourceGen.StaticAPI.GetState(api);");
            printer.PrintLine("switch (mode)");
            printer.OpenScope();
            // A single-threaded dispatch walks the chunks in query order, so the running index seeds at zero.
            // A parallel one has no such order without the capture pass, so it seeds at the -1 sentinel that
            // makes entityIndexInQuery throw.
            printer.PrintLine("case global::Latios.IJobEach.ScheduleMode.Run:");
            printer.PrintLine("    __entityIndexInQuery = 0;");
            printer.PrintLine("    state.CompleteDependency();");
            if (job.hasGroupedParameter)
                printer.PrintLine("    __ScheduleGroupedChunks(ref state, query, default, true, true);");
            else
                printer.PrintLine("    global::Unity.Entities.JobChunkExtensions.RunByRef(ref this, query);");
            printer.PrintLine("    break;");
            printer.PrintLine("case global::Latios.IJobEach.ScheduleMode.RunImmediate:");
            printer.PrintLine("    __entityIndexInQuery = 0;");
            printer.PrintLine("    state.CompleteDependency();");
            PrintRunImmediateBody(ref printer, ref job);
            printer.PrintLine("    break;");
            printer.PrintLine("case global::Latios.IJobEach.ScheduleMode.Schedule:");
            printer.PrintLine("    __entityIndexInQuery = 0;");
            if (job.hasGroupedParameter)
                printer.PrintLine("    state.Dependency = __ScheduleGroupedChunks(ref state, query, state.Dependency, true, false);");
            else
                printer.PrintLine("    state.Dependency = global::Unity.Entities.JobChunkExtensions.ScheduleByRef(ref this, query, state.Dependency);");
            printer.PrintLine("    break;");
            printer.PrintLine("default:");
            printer.PrintLine("    __entityIndexInQuery = -1;");
            if (UsesDeferredDispatch(ref job))
            {
                printer.PrintLine("    state.Dependency = __ScheduleGroupedChunks(ref state, query, state.Dependency, false, false);");
            }
            else
            {
                printer.PrintLine("    state.Dependency = global::Unity.Entities.JobChunkExtensions.ScheduleParallelByRef(ref this, query, state.Dependency);");
            }
            printer.PrintLine("    break;");
            printer.CloseScope();
            printer.CloseScope();

            if (UsesDeferredDispatch(ref job))
            {
                printer.PrintBeginLine().PrintEndLine();
                PrintScheduleDeferred(ref printer, ref job);
            }
        }

        // Captures the matching chunks, lets each grouped handle declare its constraints, then dispatches one
        // work item per resulting group. The union jobs chain serially, because each mutates the shared lists.
        static void PrintScheduleDeferred(ref Printer printer, ref JobEachSemanticsExtractor.JobContext job)
        {
            printer.PrintLine(
                "global::Unity.Jobs.JobHandle __ScheduleGroupedChunks(ref global::Unity.Entities.SystemState state, global::Unity.Entities.EntityQuery query, global::Unity.Jobs.JobHandle inputDeps, bool singleGroup, bool runNow)");
            printer.OpenScope();
            printer.PrintLine("var __allocator = state.WorldUpdateAllocator;");
            // CaptureChunksJob uses AddNoResize on both lists, so the capacity must cover every chunk.
            printer.PrintLine("var __chunkCapacity = query.CalculateChunkCountWithoutFiltering();");
            printer.PrintLine("var __chunks = new global::Unity.Collections.NativeList<global::Latios.IJobEach.CapturedChunk>(__chunkCapacity, __allocator);");
            printer.PrintLine("var __ranges = new global::Unity.Collections.NativeList<global::Latios.IJobEach.ChunkGroupRange>(__chunkCapacity, __allocator);");
            printer.PrintLine(
                "var __capture = new global::Latios.IJobEach.CaptureChunksJob { capturedChunks = __chunks, chunkGroupRanges = __ranges, forceSingleGroup = singleGroup };");
            printer.PrintLine("var __builder = new global::Latios.IJobEach.ChunkGroupBuilder { chunks = __chunks, groupRanges = __ranges };");
            printer.PrintLine("var __deps = inputDeps;");
            printer.PrintLine("if (runNow)");
            printer.PrintLine("    global::Unity.Entities.JobChunkExtensions.RunByRef(ref __capture, query);");
            printer.PrintLine("else");
            printer.PrintLine("    __deps = global::Unity.Entities.JobChunkExtensions.ScheduleByRef(ref __capture, query, inputDeps);");

            bool anyGrouped = false;
            foreach (var handle in job.handles)
            {
                if (handle.isGrouped)
                    anyGrouped = true;
            }
            if (anyGrouped)
            {
                // A single group already contains every chunk, so there is nothing left for a handle to
                // merge and the union pass would only cost a job.
                printer.PrintLine("if (!singleGroup)");
                printer.OpenScope();
                foreach (var handle in job.handles)
                {
                    if (!handle.isGrouped)
                        continue;
                    printer.PrintBeginLine("__deps = ");
                    PrintHandleInvoke(ref printer, handle, "CallHandleScheduleGroupUnions");
                    printer.Print("(ref __handles.").Print(handle.fieldName).PrintEndLine(", __builder, __deps);");
                }
                printer.CloseScope();
            }
            else
            {
                printer.PrintLine("// No grouped parameters: every chunk stays in the one-chunk group the capture pass created.");
            }

            // Running now means the lists are already populated, so the groups can alias them directly.
            printer.PrintLine("var __groupedChunks = new __GroupedChunks");
            printer.PrintLine("{");
            printer.PrintLine("    job    = this,");
            printer.PrintLine("    groups = runNow ? __builder.AsMainThreadChunkGroups() : __builder.AsDeferredChunkGroups()");
            printer.PrintLine("};");
            // A single group means every chunk must be visited by one thread, so it dispatches as an IJob
            // rather than one work item per group.
            printer.PrintLine("if (singleGroup)");
            printer.OpenScope();
            printer.PrintLine("if (runNow)");
            printer.OpenScope();
            printer.PrintLine("global::Unity.Jobs.IJobExtensions.RunByRef(ref __groupedChunks);");
            printer.PrintLine("return default;");
            printer.CloseScope();
            printer.PrintLine("return global::Unity.Jobs.IJobExtensions.ScheduleByRef(ref __groupedChunks, __deps);");
            printer.CloseScope();
            printer.PrintLine("return global::Unity.Jobs.IJobParallelForDeferExtensions.ScheduleByRef(ref __groupedChunks, __ranges, 1, __deps);");
            printer.CloseScope();
        }

        static void PrintScheduleWithDeps(ref Printer printer, ref JobEachSemanticsExtractor.JobContext job)
        {
            printer.PrintLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            printer.PrintLine(
                "global::Unity.Jobs.JobHandle global::Latios.IJobEach.__ScheduleWithDeps<TSystem>(global::Latios.LatiosApiInvoker<TSystem> api, global::Unity.Entities.EntityQuery query, global::Latios.IJobEach.ScheduleMode mode, global::Unity.Jobs.JobHandle inputDeps)");
            printer.OpenScope();
            if (!job.isValid)
            {
                PrintUnsupportedThrow(ref printer, ref job);
                printer.CloseScope();
                return;
            }
            PrintDispatchPrologue(ref printer, ref job);
            printer.PrintLine("switch (mode)");
            printer.OpenScope();
            printer.PrintLine("case global::Latios.IJobEach.ScheduleMode.Run:");
            printer.PrintLine("    __entityIndexInQuery = 0;");
            printer.PrintLine("    inputDeps.Complete();");
            if (job.hasGroupedParameter)
                printer.PrintLine("    __ScheduleGroupedChunks(ref global::Latios.InternalSourceGen.StaticAPI.GetState(api), query, default, true, true);");
            else
                printer.PrintLine("    global::Unity.Entities.JobChunkExtensions.RunByRef(ref this, query);");
            printer.PrintLine("    return default;");
            printer.PrintLine("case global::Latios.IJobEach.ScheduleMode.RunImmediate:");
            printer.PrintLine("    __entityIndexInQuery = 0;");
            printer.PrintLine("    inputDeps.Complete();");
            PrintRunImmediateBody(ref printer, ref job);
            printer.PrintLine("    return default;");
            printer.PrintLine("case global::Latios.IJobEach.ScheduleMode.Schedule:");
            printer.PrintLine("    __entityIndexInQuery = 0;");
            if (job.hasGroupedParameter)
                printer.PrintLine(
                    "    return __ScheduleGroupedChunks(ref global::Latios.InternalSourceGen.StaticAPI.GetState(api), query, inputDeps, true, false);");
            else
                printer.PrintLine("    return global::Unity.Entities.JobChunkExtensions.ScheduleByRef(ref this, query, inputDeps);");
            printer.PrintLine("default:");
            printer.PrintLine("    __entityIndexInQuery = -1;");
            if (UsesDeferredDispatch(ref job))
                printer.PrintLine("    return __ScheduleGroupedChunks(ref global::Latios.InternalSourceGen.StaticAPI.GetState(api), query, inputDeps, false, false);");
            else
                printer.PrintLine("    return global::Unity.Entities.JobChunkExtensions.ScheduleParallelByRef(ref this, query, inputDeps);");
            printer.CloseScope();
            printer.CloseScope();
        }

        static void PrintDispatchPrologue(ref Printer printer, ref JobEachSemanticsExtractor.JobContext job)
        {
            printer.PrintLine("global::Latios.InternalSourceGen.StaticAPI.CheckScheduleModeSupported(mode, __supportedModes);");
            printer.PrintLine("__handles = api.Get<__HandlesForParameters>();");
            printer.PrintLine("__mode = mode;");
            printer.PrintLine("__lastSystemVersion = api.lastSystemVersion;");

            if (!HasInjectedFields(ref job))
                return;

            // [Inject] fields are validated by the job safety system at schedule time, so they have to be
            // populated here rather than on entry to Execute. The cache stays in this local so the job
            // carries exactly one reference to each injected container.
            printer.PrintLine("var __handlesToGet = api.Get<__HandlesToGet>();");
            foreach (var handle in job.handles)
            {
                if (handle.kind == JobEachSemanticsExtractor.HandleKind.InjectedField)
                    printer.PrintBeginLine("this.").Print(handle.injectedFieldName).Print(" = __handlesToGet.").Print(handle.fieldName).PrintEndLine(";");
            }
        }

        static void PrintUnsupportedThrow(ref Printer printer, ref JobEachSemanticsExtractor.JobContext job)
        {
            printer.PrintBeginLine("throw new global::System.NotImplementedException(\"This IJobEach ").Print(job.unsupportedReason).Print(
                ", which the source generator does not support yet.\");").PrintEndLine();
        }

        static void PrintAppendToQuery(ref Printer printer, ref JobEachSemanticsExtractor.JobContext job)
        {
            printer.PrintLine("global::Latios.FluentQuery global::Latios.IJobEach.__AppendToQuery(global::Latios.FluentQuery query)");
            printer.OpenScope();
            foreach (var term in job.queryTerms)
            {
                printer.PrintBeginLine("query = query.").Print(term.method).Print("<").Print(term.componentFullName).Print(">(");
                if (term.hasReadOnlyArgument)
                    printer.Print(term.readOnly ? "true" : "false");
                printer.PrintEndLine(");");
            }
            // A parameter handle contributes whatever types it accesses, called on a default instance.
            foreach (var handle in job.handles)
            {
                if (handle.kind != JobEachSemanticsExtractor.HandleKind.ParameterHandle)
                    continue;
                printer.PrintBeginLine("query = ");
                PrintHandleInvoke(ref printer, handle, "CallHandleAppendToQuery");
                printer.PrintEndLine("(query);");
            }
            foreach (var option in job.queryOptionMethods)
                printer.PrintBeginLine("query = query.").Print(option).PrintEndLine("();");
            if (job.hasStaticAppendToQuery)
                printer.PrintLine("query = AppendToQuery(query);");
            printer.PrintLine("return query;");
            printer.CloseScope();
        }
    }
}

