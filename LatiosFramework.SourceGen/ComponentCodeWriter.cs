namespace LatiosFramework.SourceGen
{
    internal static class ComponentCodeWriter
    {
        public static string WriteComponentCode(in ComponentModel model, string componentTypeString, bool writeBurst = true)
        {
            var printer = Printer.DefaultLarge;
            if (!string.IsNullOrEmpty(model.Namespace))
            {
                printer.PrintBeginLine("namespace ").PrintEndLine(model.Namespace);
                printer.OpenScope();
            }

            foreach (var scope in model.ContainingScopes)
            {
                if (writeBurst && !scope.HasBurstCompile)
                    printer.PrintLine("[global::Unity.Burst.BurstCompile]");
                printer.PrintLine(scope.Declaration);
                printer.OpenScope();
            }

            printer.PrintLine("[global::System.Runtime.CompilerServices.CompilerGenerated]");
            if (writeBurst)
                printer.PrintLine("[global::Unity.Burst.BurstCompile]");
            printer.PrintBeginLine();
            if (!string.IsNullOrEmpty(model.TargetModifiers))
                printer.Print(model.TargetModifiers).Print(" ");
            printer.Print("struct ").Print(model.TargetIdentifier).Print(" : global::Latios.InternalSourceGen.StaticAPI.I").Print(componentTypeString).PrintEndLine(
                "ComponentSourceGenerated");
            {
                printer.OpenScope();
                printer.PrintLine("public struct ExistComponent : global::Unity.Entities.IComponentData { }");
                printer.PrintBeginLine("public struct CleanupComponent : global::Unity.Entities.ICleanupComponentData, global::Latios.InternalSourceGen.StaticAPI.I").Print(
                    componentTypeString).
                PrintEndLine("ComponentCleanup");
                {
                    printer.OpenScope();
                    if (writeBurst)
                    {
                        printer.PrintLine("[global::UnityEngine.Scripting.Preserve]");
                        printer.PrintBeginLine("public static global::Unity.Burst.FunctionPointer<global::Latios.InternalSourceGen.StaticAPI.BurstDispatch").Print(
                            componentTypeString).
                        PrintEndLine("ComponentDelegate> GetBurstDispatchFunctionPtr()");
                        {
                            printer.OpenScope();
                            printer.PrintBeginLine("return global::Unity.Burst.BurstCompiler.CompileFunctionPointer<").Print(
                                "global::Latios.InternalSourceGen.StaticAPI.BurstDispatch")
                            .Print(componentTypeString).PrintEndLine("ComponentDelegate>(BurstDispatch);");
                            printer.CloseScope();
                        }
                        printer.PrintEndLine();
                    }
                    printer.PrintLine("[global::UnityEngine.Scripting.Preserve]");
                    printer.PrintBeginLine("public static global::System.Type Get").Print(componentTypeString).Print("ComponentType() => typeof(").Print(
                        model.TargetIdentifier).PrintEndLine(");");
                    printer.CloseScope();
                }
                printer.PrintEndLine();
                printer.PrintLine("public global::Unity.Entities.ComponentType componentType => global::Unity.Entities.ComponentType.ReadOnly<ExistComponent>();");
                printer.PrintLine("public global::Unity.Entities.ComponentType cleanupType => global::Unity.Entities.ComponentType.ReadOnly<CleanupComponent>();");
                printer.PrintEndLine();
                if (writeBurst)
                {
                    printer.PrintBeginLine("[global::AOT.MonoPInvokeCallback(typeof(global::Latios.InternalSourceGen.StaticAPI.BurstDispatch")
                    .Print(componentTypeString).PrintEndLine("ComponentDelegate))]");
                    printer.PrintLine("[global::UnityEngine.Scripting.Preserve]");
                    printer.PrintLine("[global::Unity.Burst.BurstCompile]");
                    printer.PrintLine("public static void BurstDispatch(global::Latios.InternalSourceGen.StaticAPI.ContextPtr context, int operation)");
                    {
                        printer.OpenScope();
                        printer.PrintBeginLine("global::Latios.InternalSourceGen.StaticAPI.BurstDispatch").Print(componentTypeString).Print("Component<").Print(
                            model.TargetIdentifier).PrintEndLine(">(context, operation);");
                        printer.CloseScope();
                    }
                }
                printer.CloseScope();
            }

            for (var i = model.ContainingScopes.Length - 1; i >= 0; i--)
                printer.CloseScope();
            if (!string.IsNullOrEmpty(model.Namespace))
                printer.CloseScope();

            return printer.Result.Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
