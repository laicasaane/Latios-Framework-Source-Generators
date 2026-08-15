namespace LatiosFramework.SourceGen.Analyzers
{
    // How a marker's presence on a type declaration is confirmed.
    internal enum MarkerTargetKind
    {
        // The marker interface must be in AllInterfaces. A direct marker (AllowIndirect = false) must also
        // appear literally in the declaration's own BaseList, matching the generator's candidacy rule.
        Struct,
        Interface,
        // class Foo : Bar<T> where Bar<T>'s own BaseType equals FullName (e.g. UnikaScriptAuthoring<T>,
        // matched by GeneratorFilterMethods.GetSemanticClassGenericMatch).
        ClassBaseTypesBaseType,
        // class Foo : Bar<T> where Bar<T>.OriginalDefinition equals FullName (e.g. UnikaScriptAutoAuthoring<T>,
        // matched by AutoAuthoringGenerator.GetSemanticDirectGenericMatch, not GeneratorFilterMethods).
        ClassDirectOriginalDefinition,
    }

    internal readonly struct MarkerInfo
    {
        // Identifier text as written in a base list, e.g. "IJobEach" or "UnikaScriptAuthoring" (without generic args).
        public readonly string ShortName;
        // Fully-qualified name to confirm semantically, e.g. "global::Latios.IJobEach".
        public readonly string FullName;
        // True only for markers designed to be implemented transitively through a derived interface
        // (IVInterface, IUnikaInterface) — missing-partial detection uses AllInterfaces for these instead
        // of requiring the marker to appear literally in the declaration's own BaseList.
        public readonly bool AllowIndirect;
        // True only for markers that must also be explicitly re-declared even when implemented indirectly
        // (checked by the separate missing-explicit-interface analyzer, not this one).
        public readonly bool RequiresExplicitListing;
        public readonly MarkerTargetKind TargetKind;

        public MarkerInfo(string shortName, string fullName, bool allowIndirect, bool requiresExplicitListing, MarkerTargetKind targetKind)
        {
            ShortName               = shortName;
            FullName                = fullName;
            AllowIndirect           = allowIndirect;
            RequiresExplicitListing = requiresExplicitListing;
            TargetKind              = targetKind;
        }
    }
}
