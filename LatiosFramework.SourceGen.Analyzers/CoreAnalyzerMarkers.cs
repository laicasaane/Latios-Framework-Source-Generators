namespace LatiosFramework.SourceGen.Analyzers
{
    // Every marker owned by SourceGenerators~/LatiosFramework.SourceGen/, kept in sync with that project's
    // GeneratorFilterMethods usage. Add a row here whenever a new Core generator entry point is added.
    internal static class CoreAnalyzerMarkers
    {
        // Scopes the framework-internal-source exclusion (see PartialAndInterfaceDetection.IsExcludedFromAnalysis)
        // to just this module, not the whole framework, so this analyzer still flags real mistakes elsewhere
        // (e.g. in Unika or a third-party addon).
        public const string ExcludedModulePath = "com.latios.latiosframework/Core";

        public static readonly MarkerInfo[] All =
        {
            new MarkerInfo("IJobEach", "global::Latios.IJobEach", allowIndirect: false, requiresExplicitListing: false, targetKind: MarkerTargetKind.Struct),
            new MarkerInfo("IVInterface", "global::Latios.Unsafe.IVInterface", allowIndirect: true, requiresExplicitListing: true, targetKind: MarkerTargetKind.Struct),
            new MarkerInfo("IVInterface", "global::Latios.Unsafe.IVInterface", allowIndirect: true, requiresExplicitListing: true, targetKind: MarkerTargetKind.Interface),
            new MarkerInfo("ILatiosApi", "global::Latios.ILatiosApi", allowIndirect: false, requiresExplicitListing: false, targetKind: MarkerTargetKind.Struct),
            new MarkerInfo("IInjectable", "global::Latios.IInjectable", allowIndirect: false, requiresExplicitListing: false, targetKind: MarkerTargetKind.Struct),
            new MarkerInfo("ICollectionComponent", "global::Latios.ICollectionComponent", allowIndirect: false, requiresExplicitListing: false, targetKind: MarkerTargetKind.Struct),
            new MarkerInfo("IManagedStructComponent", "global::Latios.IManagedStructComponent", allowIndirect: false, requiresExplicitListing: false, targetKind: MarkerTargetKind.Struct),
        };
    }
}
