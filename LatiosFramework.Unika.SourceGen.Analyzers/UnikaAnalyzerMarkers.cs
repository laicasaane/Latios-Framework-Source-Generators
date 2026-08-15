using LatiosFramework.SourceGen.Analyzers;

namespace LatiosFramework.Unika.SourceGen.Analyzers
{
    // Every marker owned by SourceGenerators~/LatiosFramework.Unika.SourceGen/, kept in sync with that
    // project's GeneratorFilterMethods usage (plus AutoAuthoringGenerator's own bespoke semantic check).
    // Add a row here whenever a new Unika generator entry point is added.
    internal static class UnikaAnalyzerMarkers
    {
        // Scopes the framework-internal-source exclusion (see PartialAndInterfaceDetection.IsExcludedFromAnalysis)
        // to just this module, not the whole framework, so this analyzer still flags real mistakes elsewhere
        // (e.g. in Core or a third-party addon).
        public const string ExcludedModulePath = "com.latios.latiosframework/Unika";

        public static readonly MarkerInfo[] All =
        {
            new MarkerInfo("IUnikaInterface", "global::Latios.Unika.IUnikaInterface", allowIndirect: true, requiresExplicitListing: true, targetKind: MarkerTargetKind.Interface),
            new MarkerInfo("IUnikaScript", "global::Latios.Unika.IUnikaScript", allowIndirect: false, requiresExplicitListing: false, targetKind: MarkerTargetKind.Struct),
            // AuthoringGenerator.cs: class Foo : UnikaScriptAuthoring<T>, where UnikaScriptAuthoring<T>'s
            // own BaseType is UnikaScriptAuthoringBase (GeneratorFilterMethods.GetSemanticClassGenericMatch).
            new MarkerInfo("UnikaScriptAuthoring", "global::Latios.Unika.Authoring.UnikaScriptAuthoringBase", allowIndirect: false, requiresExplicitListing: false, targetKind: MarkerTargetKind.ClassBaseTypesBaseType),
            // AutoAuthoringGenerator.cs: class Foo : UnikaScriptAutoAuthoring<T> directly
            // (AutoAuthoringGenerator.GetSemanticDirectGenericMatch, not GeneratorFilterMethods).
            new MarkerInfo("UnikaScriptAutoAuthoring", "global::Latios.Unika.Authoring.UnikaScriptAutoAuthoring<T>", allowIndirect: false, requiresExplicitListing: false, targetKind: MarkerTargetKind.ClassDirectOriginalDefinition),
        };
    }
}
