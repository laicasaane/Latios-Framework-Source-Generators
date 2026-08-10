# Latios Generator Refactor Tasks

## Companion Analyzer Decision

### Decide whether a companion analyzer is required

- Create a companion analyzer only when an invalid condition can be derived independently from user source and has a stable diagnostic ID, message, severity, and repair location.
- Do not create an analyzer for an unexpected generator exception. An analyzer reads source and semantic data; it cannot observe or truthfully diagnose `GeneratorRunResult.Exception`, a writer crash, or a generator implementation defect.
- Retire `LATIOS_SG_01`, `LATIOS_SG_02`, `LATIOS_SG_03`, `LATIOS_SG_04`, `LATIOS_SG_05`, `LATIOS_SG_08`, `Unika_SG_01`, `Unika_SG_02`, `Unika_SG_03`, and `Unika_SG_04` because they only label internal generator failures.
- Create `ILatiosApiDiagnosticAnalyzer` because `LATIOS_SG_06` and `LATIOS_SG_07` describe source-observable invalid `LatiosApiInvoker.Get*` invocations and identify source locations the user can repair.
- Keep the unsupported `[Inject]` field behavior silent unless its public contract is explicitly changed to require a diagnostic.
- Do not create empty analyzers for generators with no source-observable invalid-input contract. Add a companion later only after freezing its descriptor and analyzer/generator validity boundary.

### Why generator crashes must remain generator exceptions

- Treat a diagnostic as a deterministic contract for a user-source violation that the user can locate and repair. An internal crash does not satisfy that contract.
- Treat an unexpected exception as evidence that the generator implementation failed, not that the consumer wrote invalid source. Do not misattribute the defect to the consumer through a diagnostic.
- Let the driver retain the original exception in `GeneratorRunResult.Exception` so its type, message, stack trace, and failing generator remain available for debugging and automated failure detection.
- Do not catch `Exception` and report its message through an internal-error diagnostic. That hides the original failure, creates unstable diagnostic text, and can make a failed or partial generation run appear to be an ordinary source-validation result.
- Do not allow generation to continue after an unexpected failure because later output may be missing, partial, or inconsistent with cached incremental state.
- Rethrow cancellation instead of converting it into a diagnostic; cancellation is host control flow, not invalid user source.
- Handle expected invalid input without throwing: let the companion analyzer report the exact source violation and make the generator skip that candidate or apply its frozen non-diagnostic fallback.
- Test supported inputs with `GeneratorRunResult.Exception == null`, and keep a deliberate failure test that proves an unexpected generator defect remains visible as an exception rather than a component-authored diagnostic.

## `global::LatiosFramework.SourceGen.CollectionComponentGenerator`

### Recognize violations

- Treat `StructDeclarationSyntax` reaching `GenerateOutput` and `ComponentCodeWriter.WriteComponentCode` as a final-pipeline Roslyn-object violation.
- Treat syntax-parent inspection during rendering as evidence that declaration scope has not been reduced to value data.
- Treat the file-stem plus short-identifier hint as collision-prone across namespaces, containing types, and equal file names.
- Treat capture-free non-static callbacks, string-only `AddSource`, and the broad exception catch as violations.
- Treat `LATIOS_SG_01`, `Diagnostic.Create`, and `ReportDiagnostic` in generator-owned code as invalid diagnostic ownership.

### Refactor the generator and writer

- Keep a narrow `CreateSyntaxProvider`; this generator discovers an interface relationship, not a direct attribute.
- Keep the predicate syntax-only. Perform semantic confirmation and extraction in the transform, honor cancellation, return an explicit invalid model, and filter it with a static callback.
- Replace the syntax value with one C# 8-compatible `readonly struct ComponentModel : IEquatable<ComponentModel>` shared with `global::LatiosFramework.SourceGen.ManagedStructComponentGenerator`.
- Store only semantic hint identity, namespace, ordered containing-scope declarations, target modifiers, and escaped target identifier. Keep `Collection` and Burst-enabled behavior as writer constants.
- Retain no `Compilation`, `SemanticModel`, symbol, syntax, location, path, printer, builder, or mutable collection in the model.
- Implement ordinal, field-complete equality and hashing, including element-wise equality for the scope sequence.
- Refactor `ComponentCodeWriter.WriteComponentCode` to render from the model only. Reuse the existing printer; do not add another writer or a shared model project.
- Conform every implementation detail to the current coding style and conventions demonstrated in `LatiosFramework.SourceGen/`, including naming, modifiers, namespaces, using placement, file layout, formatting, and nearby accessibility patterns.
- Implement this refactor adjacently with `global::LatiosFramework.SourceGen.ManagedStructComponentGenerator`: route both generators through one `ComponentModel` and one common transform/model factory because they share the same extraction logic and equatable value graph. Keep their filters, semantic hint identities, and component-role constants distinct, and place the shared path beside the two generators in `LatiosFramework.SourceGen/`; do not create a common project or general framework.
- Build a collision-safe hint from the fully qualified semantic target and output role. If existing hint text is a compatibility contract, obtain owner approval before changing it.
- Use static callbacks, check `SourceProductionContext.CancellationToken`, create UTF-8 `SourceText`, and call `AddSource` once.

### Separate diagnostics

- Remove `LATIOS_SG_01`, the catch-all diagnostic conversion, and all generator diagnostic APIs.
- Do not recreate `LATIOS_SG_01` in an analyzer: it describes an unexpected generator defect, not a source-observable user violation.
- Rethrow cancellation and let unexpected defects surface as generator exceptions.
- Add no empty companion analyzer. Define one only if a concrete invalid `ICollectionComponent` source rule, stable ID, message, severity, and repair location are approved.

### Prove the refactor

- Use or create one minimal Roslyn test project; do not create generator-specific test infrastructure beyond required fixtures.
- Assert exact hint, exact source, one final newline, compiled generated output, empty driver/per-generator diagnostics, and a null generator exception for supported input.
- Test equal models/hashes, every field changed alone, separate equal scope storage, namespace/nesting collisions, and file-rename stability.
- Reuse one immutable driver for same input, unrelated edit, relevant edit, and candidate removal; use a fresh driver only for byte determinism.
- Use Roslyn CodeLens and the supported dotnet debug profile only. Do not use Unity CLI.

## `global::LatiosFramework.SourceGen.ILatiosApiGenerator`

### Recognize violations

- Treat the raw `CompilationProvider` combination, `StructDeclarationSyntax`, output-boundary semantic extraction, and writer access to syntax or `ITypeSymbol` as violations.
- Treat mutable `LatiosApiSemanticsExtractor.BodyContext`, mutable `List<FieldEntry>`, and missing content equality as violations.
- Treat insertion-order-dependent fields and underscore-only hint sanitization as unproven ordering and collision risks.
- Treat generator-owned `LATIOS_SG_05`, `LATIOS_SG_06`, `LATIOS_SG_07`, `Diagnostic.Create`, and `ReportDiagnostic` as invalid ownership.

### Refactor the generator and writer

- Keep a narrow `CreateSyntaxProvider`; perform all required semantic work in its transform and inspect all partial declarations only because current `Get` usage discovery requires them.
- Return an explicit invalid-or-valid C# 8-compatible `readonly struct ILatiosApiModel : IEquatable<ILatiosApiModel>` and never combine the final candidate with raw `Compilation`.
- Store semantic hint identity, namespace, ordered containing scopes, target modifiers, escaped target identifier, target full type name, and a content-equal field sequence.
- Give each field value only generated field name, fully qualified return-type text, optional Boolean constant, `FieldInitKind`, and optional built-in getter method name. Store no symbol. Omit `structShortName` when the writer does not read it.
- Normalize duplicate semantic requests. Freeze whether field order is semantic; otherwise sort by the exact ordinal key before assigning generated field names.
- Implement complete ordinal equality and hashing for the model and every field and sequence element.
- Refactor `LatiosApiSemanticsExtractor` to return value data without `SourceProductionContext`. Refactor `ILatiosApiCodeWriter` to accept only `in ILatiosApiModel`.
- Conform every implementation detail to the current coding style and conventions demonstrated in `LatiosFramework.SourceGen/`, including naming, modifiers, namespaces, using placement, file layout, formatting, and nearby accessibility patterns.
- Keep the top-level extraction and `ILatiosApiModel` local to `ILatiosApiGenerator`. Preserve the small diagnostic-neutral return-type classifier already shared with `InjectableGenerator`, but do not merge their top-level models because candidate discovery, field identity, Boolean-value source, ordering, and analyzer contracts differ. Share a larger path only if two or more generators are proven to have the same complete extraction logic and equatable value graph; then implement all participating generators adjacently and place that shared path beside them rather than in a new common project.
- Keep invalid invocations out of the field model; the generator must emit no diagnostic and no broken field for them.
- Build a collision-safe semantic hint; obtain owner approval first if existing hint text is a compatibility contract.
- Use static callbacks, cancellation, UTF-8 `SourceText`, deterministic rendering, and one `AddSource`.

### Separate diagnostics

- Move `LATIOS_SG_06` and `LATIOS_SG_07` unchanged to analyzer-owned code.
- Remove `LATIOS_SG_05`; do not recreate it in the analyzer because it represents an unexpected generator defect.
- Remove every descriptor, ID, diagnostic payload, `Diagnostic.Create`, and `ReportDiagnostic` path from generator-owned code. Rethrow cancellation and expose unexpected defects.

### Implement the companion analyzer

- Create one independent `LatiosFramework.SourceGen.Analyzers` project or assembly compatible with the existing .NET Standard 2.0, C# 8, and Microsoft.CodeAnalysis 4.0.1 floor. Mark it as a Roslyn component, keep Roslyn package assets private, and do not reference `LatiosFramework.SourceGen`.
- Add `[DiagnosticAnalyzer(LanguageNames.CSharp)] public sealed class ILatiosApiDiagnosticAnalyzer : DiagnosticAnalyzer` and keep its diagnostic IDs, descriptors, semantic checks, locations, creation, and reporting inside the analyzer assembly.
- Preserve both descriptor contracts exactly: `LATIOS_SG_06` and `LATIOS_SG_07`, category `Latios.ILatiosApi`, Error severity, enabled by default, existing titles, messages, descriptions, and message-argument order.
- Return exactly both descriptors from `SupportedDiagnostics`.
- In `Initialize`, explicitly ignore generated code, enable concurrent execution with callback-local state, and directly call `RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression)` on the `AnalysisContext` parameter.
- In `AnalyzeInvocation`, honor cancellation and reject unrelated invocation syntax before requesting semantic data.
- Resolve the invoked `IMethodSymbol`; continue only when its name starts with `Get` using ordinal comparison and its containing original type is `Latios.LatiosApiInvoker`.
- Resolve `Latios.ILatiosApi`, `Latios.ILatiosApiGettable`, and `Latios.ILatiosApiGettableBool` from the callback compilation after the cheap invocation checks; return without diagnostics when required metadata is absent.
- Resolve the invocation's enclosing type; continue only when it is a struct whose direct `Interfaces` contain the resolved `Latios.ILatiosApi`, including invocations in any partial declaration.
- Find the Boolean parameter and its argument by named-argument identity first and positional ordinal second. When the argument is absent or is not a compile-time Boolean constant, report `LATIOS_SG_06` at the argument syntax with the invocation as fallback, then stop analyzing that invocation.
- Classify the return type locally using the exact accepted taxonomy: types implementing `Latios.ILatiosApiGettable` or `Latios.ILatiosApiGettableBool`, plus the supported original definitions in `Unity.Entities` named `ComponentTypeHandle`, `ComponentLookup`, `BufferTypeHandle`, `BufferLookup`, `SharedComponentTypeHandle`, `EntityTypeHandle`, and `EntityStorageInfoLookup`.
- Define the current fully qualified `SymbolDisplayFormat` inside the analyzer and use it for identity text and `LATIOS_SG_07` message argument zero; do not reference or copy Roslyn helpers from the generator assembly.
- When the return type is unsupported, report `LATIOS_SG_07` at the invocation and pass that fully qualified return-type text as message argument zero.
- Preserve diagnostic precedence and multiplicity: report at most one of these diagnostics per invocation, and evaluate `LATIOS_SG_06` before `LATIOS_SG_07`.
- Keep compilations, semantic models, symbols, syntax, and locations callback-local. Add no compilation-start action, nested registration, cache, stored symbol state, common helper project, suppressor, or code fix.
- Add the analyzer project to the solution and ship its assembly beside the generator through the same analyzer delivery path so consumers receive both components.

### Prove the refactor

- Assert exact output/hints and compiled consumer calls for valid, partial, nested, generic, alias-qualified, and global-qualified cases.
- Add focused `ILatiosApiDiagnosticAnalyzer` tests for descriptor shape, direct registration structure, generated-code policy, valid calls, named and positional Boolean arguments, non-constant or absent Boolean arguments, unsupported return types, precedence, multiple invocations, partial declarations, aliases, similar calls outside an `ILatiosApi` struct, and missing metadata.
- Assert exact analyzer origin, ID, severity, file, span, message arguments, precedence, and multiplicity for `LATIOS_SG_06` and `LATIOS_SG_07`.
- Pair each invalid analyzer case with generator proof that the invalid invocation produces no broken field, no generator diagnostic, and no generator exception.
- Assert the generator produces empty diagnostic collections for valid and expected-invalid input.
- Test model equality/hash field by field, equal-content field sequences, normalized order, hint collisions, and partial-declaration equivalence.
- Reuse one driver for unrelated, relevant, order-only, and removal edits; use a fresh driver only for byte-identical determinism.
- Use Roslyn CodeLens and the supported dotnet debug profile only. Do not use Unity CLI.

## `global::LatiosFramework.SourceGen.InjectableGenerator`

### Recognize violations

- Treat raw `Compilation`, `StructDeclarationSyntax`, output-boundary semantic extraction, and writer access to `IFieldSymbol` or `ITypeSymbol` as violations.
- Treat mutable `BodyContext`, mutable `List<FieldEntry>`, missing content equality, and collision-prone underscore hint sanitization as violations.
- Preserve the existing intentional silent skip for unsupported `[Inject]` field types unless the owner changes that contract.
- Treat `LATIOS_SG_08` and the broad exception-to-diagnostic path as invalid generator diagnostic ownership.

### Refactor the generator and writer

- Keep a narrow `CreateSyntaxProvider`; keep its predicate syntax-only and project semantic candidates directly to an invalid-or-valid model.
- Replace syntax plus compilation with one C# 8-compatible `readonly struct InjectableModel : IEquatable<InjectableModel>`.
- Store semantic hint identity, namespace, ordered containing scopes, target modifiers, escaped target identifier, target full type name, and a content-equal injection-field sequence.
- Give each field value only escaped field name, fully qualified type text, optional Boolean value, `FieldInitKind`, and optional built-in getter method name.
- Store no compilation, semantic model, syntax, symbol, location, file path, printer, builder, or mutable collection.
- Freeze whether injection-field declaration order affects generated behavior; preserve and compare it when semantic, otherwise normalize it before model construction.
- Implement complete ordinal equality and hashing for every scalar and every sequence element.
- Refactor the extractor to return the model without `SourceProductionContext`; refactor `InjectableCodeWriter` to accept only `in InjectableModel`.
- Conform every implementation detail to the current coding style and conventions demonstrated in `LatiosFramework.SourceGen/`, including naming, modifiers, namespaces, using placement, file layout, formatting, and nearby accessibility patterns.
- Keep the top-level extraction and `InjectableModel` local to `InjectableGenerator`. Preserve the small diagnostic-neutral return-type classifier already shared with `ILatiosApiGenerator`, but do not merge their top-level models because candidate discovery, field identity, Boolean-value source, ordering, and analyzer contracts differ. Share a larger path only if two or more generators are proven to have the same complete extraction logic and equatable value graph; then implement all participating generators adjacently and place that shared path beside them rather than in a new common project.
- Build a collision-safe semantic hint, use static callbacks, check cancellation, emit UTF-8 `SourceText`, and call `AddSource` once.

### Separate diagnostics

- Remove `LATIOS_SG_08`, `Diagnostic.Create`, `ReportDiagnostic`, and the catch-all exception translation.
- Do not move `LATIOS_SG_08` to an analyzer; an analyzer cannot truthfully detect an internal generator failure.
- Keep unsupported `[Inject]` field types as silent skips unless a new user-facing analyzer contract is explicitly approved.
- Add no empty analyzer. Rethrow cancellation and expose unexpected generator exceptions.

### Prove the refactor

- Assert exact generated source/hint and output compilation for every supported injection kind and the intentional unsupported-field skip.
- Test nested and partial targets, collision-safe hints, equal models/hashes, every model field, and equal-content field storage.
- Reuse one driver for same input, unrelated edit, field changes, and candidate removal; use a fresh driver only for byte determinism.
- Structurally reject Roslyn objects and diagnostic APIs in generator-owned final models and output code.
- Use Roslyn CodeLens and the supported dotnet debug profile only. Do not use Unity CLI.

## `global::LatiosFramework.SourceGen.ManagedStructComponentGenerator`

### Recognize violations

- Treat `StructDeclarationSyntax` reaching `GenerateOutput` and `ComponentCodeWriter` as a final-pipeline Roslyn-object violation.
- Treat syntax-parent scope rendering and the file-stem plus short-name hint as unreduced data and a collision risk.
- Treat capture-free non-static callbacks, string-only `AddSource`, and the broad catch as violations.
- Treat `LATIOS_SG_02`, `Diagnostic.Create`, and `ReportDiagnostic` in generator-owned code as invalid ownership.

### Refactor the generator and writer

- Keep a narrow `CreateSyntaxProvider`; keep the predicate syntax-only and perform semantic confirmation in the transform.
- Return an explicit invalid-or-valid C# 8-compatible `readonly struct ComponentModel : IEquatable<ComponentModel>` shared with `global::LatiosFramework.SourceGen.CollectionComponentGenerator`.
- Store only semantic hint identity, namespace, ordered containing-scope declarations, target modifiers, and escaped target identifier. Keep `ManagedStruct` and Burst-disabled behavior as writer constants.
- Store no Roslyn object, path, printer, builder, service, or mutable collection. Give the scope sequence explicit element-wise equality and matching hashing.
- Refactor `ComponentCodeWriter.WriteComponentCode` to render from this model only; reuse the existing printer and add no second writer or shared model project.
- Conform every implementation detail to the current coding style and conventions demonstrated in `LatiosFramework.SourceGen/`, including naming, modifiers, namespaces, using placement, file layout, formatting, and nearby accessibility patterns.
- Implement this refactor adjacently with `global::LatiosFramework.SourceGen.CollectionComponentGenerator`: route both generators through one `ComponentModel` and one common transform/model factory because they share the same extraction logic and equatable value graph. Keep their filters, semantic hint identities, and component-role constants distinct, and place the shared path beside the two generators in `LatiosFramework.SourceGen/`; do not create a common project or general framework.
- Build a collision-safe semantic hint, subject to owner approval if existing hint text is a compatibility contract.
- Use static callbacks, cancellation, UTF-8 `SourceText`, deterministic newlines, and one `AddSource`.

### Separate diagnostics

- Remove `LATIOS_SG_02` and the catch/report path; do not recreate an internal-failure diagnostic in an analyzer.
- Add no empty companion analyzer. A new analyzer requires an approved source-observable invalid-input rule and exact descriptor contract.
- Rethrow cancellation and let unexpected defects remain generator exceptions.

### Prove the refactor

- Assert exact source, hint, output compilation, empty generator diagnostics, and null exceptions for supported input.
- Test namespace/nesting collisions, equal file stems, alias/global-qualified candidates, model equality/hash, and every scope/name field.
- Reuse one driver for unrelated edit, relevant declaration edit, and removal; use a fresh driver only for deterministic bytes.
- Structurally reject diagnostic APIs and Roslyn objects in final generator data.
- Use Roslyn CodeLens and the supported dotnet debug profile only. Do not use Unity CLI.

## `global::LatiosFramework.SourceGen.VInterfaceGenerator`

### Recognize violations

- Treat raw `Compilation`, `InterfaceDeclarationSyntax`, output-boundary `SemanticModel` creation, and syntax-driven writing as violations.
- Treat mutable `BodyContext` and mutable method, property, indexer, argument, and base-interface lists without content equality as violations.
- Treat unnormalized base-interface ordering, path-derived hints, capture-free non-static callbacks, and string-only `AddSource` as violations.
- Treat `LATIOS_SG_03` and catch-all diagnostic reporting as invalid generator diagnostic ownership.

### Refactor the generator and writer

- Keep a narrow `CreateSyntaxProvider`; keep the predicate syntax-only and return a fully reduced invalid-or-valid model from the semantic transform.
- Create one C# 8-compatible `readonly struct InterfaceModel : IEquatable<InterfaceModel>` containing semantic hint identity, namespace, ordered containing scopes, interface modifiers/name, base-interface names, methods, properties, and indexers.
- Give each method value name, optional explicit-interface qualifier, accessibility, return type text, return ref kind, and content-equal argument values containing type text, name, and ref kind.
- Give each property value name, optional explicit-interface qualifier, accessibility, type text, ref kind, getter flag, and setter flag.
- Give each indexer value optional explicit-interface qualifier, accessibility, type text, ref kind, getter/setter flags, and content-equal argument values containing type text and name.
- Derive property and indexer operation counts from the value collections; do not store duplicate counts.
- Preserve the current comparer-defined member order. Normalize duplicate and order-insensitive base-interface data before constructing the model.
- Implement complete ordinal equality and element-wise hashing for the entire nested model graph.
- Refactor `VptrSemanticsExtractor` to return the model and release all symbols before return. Refactor `VInterfaceCodeWriter` to accept only `in InterfaceModel`.
- Conform every implementation detail to the current coding style and conventions demonstrated in `LatiosFramework.SourceGen/`, including naming, modifiers, namespaces, using placement, file layout, formatting, and nearby accessibility patterns.
- Implement this refactor adjacently with `global::LatiosFramework.Unika.SourceGen.InterfaceGenerator`: route both generators through one `InterfaceModel` and one parameterized common interface extractor because they share the same extraction logic and equatable value graph. Keep their filters, writers, hint identities, and output roles distinct. Place the shared source beside the generators in `LatiosFramework.SourceGen/` and link it into the Unika project through the existing linked-source convention; do not create a shared project or framework.
- Build a collision-safe semantic hint; use static callbacks, cancellation, UTF-8 `SourceText`, deterministic rendering, and one `AddSource`.

### Separate diagnostics

- Remove `LATIOS_SG_03` and the exception-to-diagnostic path; do not recreate an internal generator failure as an analyzer diagnostic.
- Add no empty analyzer. Add one only after a source-observable invalid `IVInterface` rule and exact descriptor contract are approved.
- Rethrow cancellation and expose unexpected generator exceptions.

### Prove the refactor

- Assert exact generated source/hint and compiled consumers for inheritance, overload qualification, methods, properties, indexers, ref forms, nesting, and generics.
- Test every nested model field, separate equal collection storage, normalized interface order, and collision-safe hints.
- Reuse one driver for unrelated edit, member edit, order-only edit, and removal; use a fresh driver only for byte determinism.
- Assert empty generator diagnostics, null supported-input exceptions, and structural absence of Roslyn objects and diagnostic APIs.
- Use Roslyn CodeLens and the supported dotnet debug profile only. Do not use Unity CLI.

## `global::LatiosFramework.SourceGen.VStructGenerator`

### Recognize violations

- Treat raw `Compilation`, `StructDeclarationSyntax`, output-boundary semantic extraction, and syntax-driven rendering as violations.
- Treat mutable `BodyContext`, reference-equal interface lists, unnormalized interface order, and path-derived hints as violations.
- Treat `LATIOS_SG_04`, the broad catch, and generator diagnostic reporting as invalid ownership.
- Treat the current `Latios.Unika.IVInterface` diagnostic category as inconsistent with the `global::Latios.Unsafe.IVInterface` candidate contract; do not preserve it by accident.

### Refactor the generator and writer

- Keep a narrow `CreateSyntaxProvider`; keep the predicate syntax-only and return a reduced invalid-or-valid value from its semantic transform.
- Create a C# 8-compatible `readonly struct VStructModel : IEquatable<VStructModel>`.
- Store semantic hint identity, namespace, ordered containing scopes, struct modifiers/name, and normalized fully qualified V-interface names.
- Derive dispatcher names from interface names and target name; do not store duplicate rendered strings.
- Store no syntax, compilation, semantic model, symbol, location, path, printer, builder, or mutable collection.
- Implement ordinal field equality and element-wise sequence equality/hashing.
- Refactor `VptrSemanticsExtractor.ExtractObjSemantics` to return the model and `VStructCodeWriter.WriteObjCode` to consume only the model.
- Conform every implementation detail to the current coding style and conventions demonstrated in `LatiosFramework.SourceGen/`, including naming, modifiers, namespaces, using placement, file layout, formatting, and nearby accessibility patterns.
- Keep this extraction and model local to `VStructGenerator`; do not merge it with `ScriptGenerator`, whose extension-output and accessibility value graph is different. Share a path only if two or more generators are proven to have the same extraction logic and equatable model; then implement all participants adjacently and place the shared path beside them rather than in a common project.
- Build a collision-safe semantic hint, use static callbacks, check cancellation, emit UTF-8 `SourceText`, and call `AddSource` once.

### Separate diagnostics

- Remove `LATIOS_SG_04` and the catch/report path. Do not move its internal-failure meaning or inconsistent category into an analyzer.
- Add no empty analyzer. A new analyzer requires a separate approved user-source rule and a new or explicitly preserved diagnostic contract.
- Rethrow cancellation and leave unexpected defects visible.

### Prove the refactor

- Assert exact output and compilation for one and multiple V-interfaces, nested targets, qualified forms, and removal.
- Test semantic hint collisions, normalized interface ordering, model equality/hash, and every model field.
- Reuse one driver for unrelated, relevant, order-only, and removal edits; use a fresh driver only for deterministic bytes.
- Assert empty generator diagnostics and structurally reject Roslyn objects and diagnostic APIs.
- Use Roslyn CodeLens and the supported dotnet debug profile only. Do not use Unity CLI.

## `global::LatiosFramework.Unika.SourceGen.AuthoringGenerator`

### Recognize violations

- Treat raw `Compilation`, `ClassDeclarationSyntax`, output-boundary `SemanticModel` creation, and syntax-driven writer scope as violations.
- Treat mutable `AuthoringCodeWriter.Context`, mutable interface lists, file-path-derived hints, non-static callbacks, and string-only `AddSource` as violations.
- Treat `Unika_SG_03`, the broad catch, and generator diagnostic reporting as invalid ownership.
- Treat adding empty generated source when no base Unika interfaces exist as an unnecessary output boundary; filter it before output.

### Refactor the generator and writer

- Keep a narrow `CreateSyntaxProvider`; keep the predicate syntax-only and perform semantic extraction in its transform.
- Return an explicit invalid-or-valid C# 8-compatible `readonly struct AuthoringModel : IEquatable<AuthoringModel>`.
- Store semantic hint identity, namespace, ordered containing scopes, authoring-class modifiers/name, fully qualified script type name, and normalized content-equal base Unika-interface names.
- Store no syntax, compilation, semantic model, symbol, location, path, printer, builder, or mutable collection.
- Filter models with no base interface before output instead of rendering and adding an empty string.
- Implement complete ordinal equality and element-wise sequence hashing.
- Refactor `UnikaSemanticsExtractor.ExtractAuthoringSemantics` to return the model and `AuthoringCodeWriter.WriteAuthoringCode` to accept only the model.
- Conform every implementation detail to the current coding style and conventions demonstrated in `LatiosFramework.SourceGen/`, while preserving more-specific adjacent Unika patterns for naming, modifiers, namespaces, using placement, file layout, formatting, and accessibility.
- Keep this extraction and model local to `AuthoringGenerator`; do not merge it with `AutoAuthoringGenerator`, whose script scope, field graph, ordering, and output contract are different. Share a path only if two or more generators are proven to have the same extraction logic and equatable model; then implement all participants adjacently and place the shared path beside them rather than in a common project.
- Build a collision-safe semantic hint, subject to owner approval if hint text is contractual.
- Use static callbacks, cancellation, UTF-8 `SourceText`, deterministic newlines, and one `AddSource`.

### Separate diagnostics

- Remove `Unika_SG_03`, `Diagnostic.Create`, `ReportDiagnostic`, and the exception translation.
- Do not recreate this internal-error diagnostic in an analyzer. Add no empty analyzer without a real invalid-authoring rule.
- Rethrow cancellation and expose unexpected generator defects.

### Prove the refactor

- Assert exact output/hint and compilation for direct, qualified, alias-qualified, nested, and no-output candidates.
- Test equal models/hashes, every field, equal-content interface storage, normalized order, and semantic hint collisions.
- Reuse one driver for unrelated edit, interface change, and candidate removal; use a fresh driver only for deterministic bytes.
- Assert no empty output, empty generator diagnostics, null supported-input exceptions, and no Roslyn objects in final models.
- Use Roslyn CodeLens and the supported dotnet debug profile only. Do not use Unity CLI.

## `global::LatiosFramework.Unika.SourceGen.AutoAuthoringGenerator`

### Recognize violations

- Treat raw `Compilation`, candidate `ClassDeclarationSyntax`, retained script `StructDeclarationSyntax`, and output-boundary semantic extraction as violations.
- Treat mutable context/lists, render-time construction of a non-public field list, missing cancellation in long extraction loops, and syntax-driven dual-scope rendering as violations.
- Treat field, attribute, using, and interface ordering without a frozen equality/ordering contract as violations.
- Treat path-derived hints and `Unika_SG_04` catch-all generator diagnostics as violations.

### Refactor the generator and writer

- Keep a narrow `CreateSyntaxProvider`; keep its predicate syntax-only and return a fully reduced invalid-or-valid model from the semantic transform.
- Create one C# 8-compatible `readonly struct AutoAuthoringModel : IEquatable<AutoAuthoringModel>` because one writer emits the authoring class and optional script helper into one source.
- Store semantic hint identity; value-owned authoring declaration scope/modifiers/name; value-owned script declaration scope/modifiers/name; script full type name; using directives; base Unika-interface names; and field values.
- Give each field value only field name, public flag, authoring type text, script type text, `FieldKind`, and content-equal copied attribute texts.
- Precompute or cheaply derive the non-public field subset from model values without allocating a second persistent model collection.
- Preserve script field declaration order when it controls authoring/inspector order. Preserve first-seen using order unless an exact semantic-safe normalization is proven. Include both sequences in equality.
- Normalize order-insensitive interface data and implement complete ordinal, element-wise equality and hashing across the model graph.
- Pass cancellation through every syntax-reference read and loop. Return no syntax, symbol, semantic model, compilation, location, or mutable storage.
- Refactor `UnikaSemanticsExtractor.ExtractAutoAuthoringSemantics` to return the model and `AutoAuthoringCodeWriter.WriteAutoAuthoringCode` to accept only `in AutoAuthoringModel`.
- Conform every implementation detail to the current coding style and conventions demonstrated in `LatiosFramework.SourceGen/`, while preserving more-specific adjacent Unika patterns for naming, modifiers, namespaces, using placement, file layout, formatting, and accessibility.
- Keep this extraction and model local to `AutoAuthoringGenerator`; do not merge it with `AuthoringGenerator`, whose model does not have the script scope, field graph, ordering, or dual-output contract. Share a path only if two or more generators are proven to have the same extraction logic and equatable model; then implement all participants adjacently and place the shared path beside them rather than in a common project.
- Reuse the existing printer; do not add another writer, a common project, or separate models unless outputs are split into distinct providers.
- Build a collision-safe semantic hint, use a static output callback, UTF-8 `SourceText`, fixed newlines, and one `AddSource`.

### Separate diagnostics

- Remove `Unika_SG_04` and the catch/report path; do not move an internal writer failure into an analyzer.
- Add no empty analyzer. Define a companion only after a concrete invalid auto-authoring input rule and exact diagnostic contract exist.
- Rethrow cancellation and expose unexpected generator exceptions.

### Prove the refactor

- Assert exact source/hint and compiled output for public, serialized private, ignored, readonly/static/const, blob, script-ref, interface-ref, entity, and entity-wrapper fields.
- Cover copied attributes/usings, no-private-field output, private-field helper output, nesting, accessibility, and hint collisions.
- Test every model and field member, equal-content attribute/using/interface storage, preserved semantic ordering, and equal hashes.
- Reuse one driver for unrelated edit, field/interface change, order change, and removal; use a fresh driver only for deterministic bytes.
- Assert empty generator diagnostics and structural absence of Roslyn objects and diagnostic APIs.
- Use Roslyn CodeLens and the supported dotnet debug profile only. Do not use Unity CLI.

## `global::LatiosFramework.Unika.SourceGen.InterfaceGenerator`

### Recognize violations

- Treat raw `Compilation`, `InterfaceDeclarationSyntax`, output-boundary semantic extraction, and syntax-driven rendering as violations.
- Treat mutable `BodyContext` and mutable nested method, property, indexer, argument, and base-interface collections without content equality as violations.
- Treat unnormalized base-interface order, path-derived hints, non-static callbacks, string-only `AddSource`, and `Unika_SG_01` catch-all diagnostics as violations.

### Refactor the generator and writer

- Keep a narrow `CreateSyntaxProvider`; keep the predicate syntax-only and return a reduced invalid-or-valid model from the semantic transform.
- Create one C# 8-compatible `readonly struct InterfaceModel : IEquatable<InterfaceModel>` containing semantic hint identity, namespace, ordered containing scopes, interface modifiers/name, base-interface names, methods, properties, and indexers.
- Give each method value name, optional explicit-interface qualifier, accessibility, return type text, return ref kind, and content-equal arguments containing type text, name, and ref kind.
- Give each property value name, optional explicit-interface qualifier, accessibility, type text, ref kind, getter flag, and setter flag.
- Give each indexer value optional explicit-interface qualifier, accessibility, type text, ref kind, getter/setter flags, and content-equal arguments containing type text and name.
- Derive operation counts from property/indexer collections. Preserve the current comparer-defined member order and normalize only proven order-insensitive base-interface data.
- Implement complete ordinal equality and element-wise hashing for the entire nested model graph.
- Refactor `UnikaSemanticsExtractor.ExtractInterfaceSemantics` to return the model and `InterfaceCodeWriter.WriteInterfaceCode` to accept only the model.
- Conform every implementation detail to the current coding style and conventions demonstrated in `LatiosFramework.SourceGen/`, while preserving more-specific adjacent Unika patterns for naming, modifiers, namespaces, using placement, file layout, formatting, and accessibility.
- Implement this refactor adjacently with `global::LatiosFramework.SourceGen.VInterfaceGenerator`: route both generators through one `InterfaceModel` and one parameterized common interface extractor because they share the same extraction logic and equatable value graph. Keep their filters, writers, hint identities, and output roles distinct. Place the shared source beside the generators in `LatiosFramework.SourceGen/` and link it into the Unika project through the existing linked-source convention; do not create a shared project or framework.
- Build a collision-safe semantic hint, use static callbacks, cancellation, UTF-8 `SourceText`, fixed newlines, and one `AddSource`.

### Separate diagnostics

- Remove `Unika_SG_01` and the exception-to-diagnostic path; do not recreate it in an analyzer.
- Add no empty analyzer. A companion requires a source-observable invalid-interface rule and frozen descriptor contract.
- Rethrow cancellation and expose unexpected generator defects.

### Prove the refactor

- Assert exact source/hint and compiled output for inherited interfaces, overload qualification, ref forms, properties, indexers, nesting, generics, aliases, and partial declarations.
- Test every nested model field, separate equal collection storage, normalized ordering, and hint collisions.
- Reuse one driver for unrelated edit, member edit, order-only edit, and removal; use a fresh driver only for deterministic bytes.
- Assert empty generator diagnostics and structurally reject diagnostic APIs and Roslyn objects.
- Use Roslyn CodeLens and the supported dotnet debug profile only. Do not use Unity CLI.

## `global::LatiosFramework.Unika.SourceGen.ScriptGenerator`

### Recognize violations

- Treat raw `Compilation`, `StructDeclarationSyntax`, output-boundary semantic extraction, and syntax-driven access-scope rendering as violations.
- Treat mutable `BodyContext`/`ExtensionClassContext`, reference-equal interface lists, and writer mutation of the extension modifier as violations.
- Treat unnormalized interface order, path-derived hints, non-static callbacks, string-only `AddSource`, and `Unika_SG_02` catch-all diagnostics as violations.

### Refactor the generator and writer

- Keep a narrow `CreateSyntaxProvider`; keep the predicate syntax-only and return a reduced invalid-or-valid model from the semantic transform.
- Create one C# 8-compatible `readonly struct ScriptModel : IEquatable<ScriptModel>` because the partial script and extension class remain one generated source.
- Store semantic hint identity, namespace, ordered containing scopes with reduced accessibility, script modifiers/name, script full type name, normalized content-equal Unika-interface names, reduced extension accessibility, and whether extension output exists.
- Compute restrictive accessibility before rendering. Do not mutate the model or store a syntax-based scope printer input.
- Derive dispatcher and extension names from equality-covered model fields instead of storing duplicate rendered strings.
- Implement complete ordinal equality and element-wise sequence hashing. Normalize interface order only after confirming order has no public meaning.
- Refactor `UnikaSemanticsExtractor.ExtractScriptSemantics` to return the model and `ScriptCodeWriter.WriteScriptCode` to accept only `in ScriptModel`.
- Conform every implementation detail to the current coding style and conventions demonstrated in `LatiosFramework.SourceGen/`, while preserving more-specific adjacent Unika patterns for naming, modifiers, namespaces, using placement, file layout, formatting, and accessibility.
- Keep this extraction and model local to `ScriptGenerator`; do not merge it with `VStructGenerator`, whose value graph has no extension-output or reduced-accessibility contract. Share a path only if two or more generators are proven to have the same extraction logic and equatable model; then implement all participants adjacently and place the shared path beside them rather than in a common project.
- Store no syntax, compilation, semantic model, symbol, location, path, printer, builder, or mutable collection.
- Build a collision-safe semantic hint, use static callbacks, cancellation, UTF-8 `SourceText`, deterministic newlines, and one `AddSource`.

### Separate diagnostics

- Remove `Unika_SG_02` and the exception-to-diagnostic path; do not recreate an internal generator failure in an analyzer.
- Add no empty analyzer. Define one only after a concrete invalid-script rule and exact descriptor contract are approved.
- Rethrow cancellation and expose unexpected generator exceptions.

### Prove the refactor

- Assert exact source/hint and compiled dispatch/downcast/extension calls for zero, one, and multiple interfaces plus nested/accessibility cases.
- Test every model field, equal-content interface storage, extension visibility, normalized order, and semantic hint collisions.
- Reuse one driver for unrelated edit, interface add/remove, and candidate removal; use a fresh driver only for byte determinism.
- Assert empty generator diagnostics and structurally reject Roslyn objects, model mutation, and diagnostic APIs.
- Use Roslyn CodeLens and the supported dotnet debug profile only. Do not use Unity CLI.
