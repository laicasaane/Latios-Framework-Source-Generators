# Roslyn Generator and Analyzer Rules

## Apply These Instructions

- Read the target repository rules, project files, package versions, tests, and closest valid implementations before
  planning changes.
- Verify the oldest compiler host, Roslyn version, target framework, component language version, generated-language
  floor, nullable mode, and packaging route.
- Treat existing code as evidence, not automatic authority. Reject local patterns that violate these instructions.
- Preserve public generated APIs, diagnostic contracts, hint names, package identity, and unrelated work unless the
  requested contract explicitly changes them.
- Prefer the smallest complete design. Do not add shared infrastructure, abstraction layers, caches, configuration,
  models, or projects without a demonstrated requirement.
- Change generator inputs, models, writers, or analyzers. Do not repair generated output by hand.

## Generator

### Recognize Violations

Reject a generator that:

- Uses `ISourceGenerator` for normal new work instead of `IIncrementalGenerator`.
- Performs extraction, semantic analysis, rendering, or source creation inside `Initialize`.
- Keeps mutable instance state, mutable static state, or a custom cache.
- Performs semantic work inside a syntax predicate.
- Scans the whole compilation when a direct attribute or narrow syntax candidate can identify the input.
- Carries `Compilation`, `SemanticModel`, `ISymbol`, `SyntaxNode`, `SyntaxTree`, `Location`, `AdditionalText`,
  `SourceText`, a service, a builder, a delegate, or mutable storage into a final pipeline model.
- Uses a mutable or oversized catch-all model.
- Gives a writer data that the writer does not use.
- Omits any output-, hint-, order-, grouping-, acceptance-, or fallback-affecting field from value equality and hashing.
- Stores arrays, immutable arrays, lists, dictionaries, or sets without explicit content equality.
- Uses `Collect()` when each candidate can produce independent output.
- Passes deeply nested tuples instead of a named value when three or more inputs meet.
- Depends on enumeration order, current culture, time, randomness, environment values, user names, or machine paths.
- Uses unstable or colliding hint names, including `string.GetHashCode()`.
- Declares or references diagnostic identifiers, descriptors, messages, categories, severities, locations, arguments,
  or diagnostic-specific models.
- Creates or reports diagnostics.
- Swallows cancellation, converts cancellation into normal failure, hides unexpected defects, or silently catches all
  exceptions.
- Renders from live Roslyn objects or writes generated source to disk as its correctness path.

### Build the Pipeline

- Implement `IIncrementalGenerator`.
- Limit `Initialize` to provider construction, filtering, projection, combination, and output registration.
- Prefer `ForAttributeWithMetadataName` when a direct marker identifies candidates.
- Use `CreateSyntaxProvider` only when no direct marker can express the input. Record the reason.
- Keep the syntax predicate cheap, local, allocation-light, and free of semantic work.
- Perform semantic extraction in the transform callback and pass its cancellation token through nested work.
- Return a defined invalid value for expected rejected input, then remove it with `Where`.
- Use `static` callbacks when they capture nothing.
- Reduce compilation, configuration, and additional-file inputs to small immutable values before combining them with
  candidates.
- Use a named immutable carrier when three or more independent facts meet.
- Split independent output roles into independent providers and `RegisterSourceOutput` calls.
- Use `Collect()` only when one output genuinely depends on the complete candidate set. Normalize duplicates, sort
  explicitly, and use content equality before rendering that aggregate.
- Never depend on another normal generator running first.

### Build One Minimal Equatable Model per Writer

- Start from one code writer and list every fact it reads.
- Add only facts that affect that writer's source, hint name, ordering, grouping, acceptance, or frozen fallback.
- Create a separate model for each writer when output roles need different facts.
- Project a larger candidate model into the smaller writer model before the output boundary.
- Remove unrelated collections and configuration from the writer model.
- Prefer a `readonly struct` or `readonly record struct` with primitives, strings, enums, small equatable values, and
  content-equal immutable collections.
- Normalize spelling, duplicates, and non-semantic ordering before constructing the model.
- Recompute derived text from equality-covered fields, or store it and include it in equality and hashing.
- Implement complete `Equals` and `GetHashCode` behavior using the same comparison rules.
- Use ordinal comparison for identity strings unless the domain explicitly requires another rule.
- Test separate equal instances, equal hashes, every field changed alone, separate collections with equal content,
  different collection content, and normalized ordering.

### Reduce Compilation Input

- Extract only facts that change output: required metadata availability, assembly identity, language facts, nullable
  facts, or feature flags.
- Do not combine a candidate directly with a raw `CompilationProvider` at the final boundary.
- Do not retain the compilation or resolved symbols in the reduced value.
- Check cancellation during metadata resolution and long loops.

### Render and Emit

- Render only from the final writer model.
- Use fixed indentation, one fixed newline sequence, invariant formatting, and explicit ordinal ordering.
- End each generated file with exactly one newline.
- Emit syntax accepted by the oldest supported consumer language version.
- Build every hint name from stable semantic identity plus output role.
- When shortening is required, use a documented stable hash or encoding.
- Create UTF-8 `SourceText` and call `AddSource` once per unique hint.
- At each output callback, check `SourceProductionContext.CancellationToken`, render, then call `AddSource`.
- Pass cancellation into long renderers and loops.

### Handle Invalid Input and Failure

- Silently skip expected invalid input or apply the frozen non-diagnostic fallback.
- Emit no broken or partial feature output for rejected input.
- Rethrow `OperationCanceledException` and preserve normal cancellation.
- Leave unexpected defects visible as generator exceptions.
- Do not translate invalid input, cancellation, or unexpected defects into generator diagnostics.

### Prove the Generator

- Assert exact output count, generator owner, hint-name set, and generated text.
- Compile generated output and compile representative consumer calls.
- Assert empty driver-level and per-generator diagnostic collections.
- Assert every generator exception is null for supported and expected-invalid inputs.
- Reuse the same immutable driver for same-input, unrelated-edit, relevant-edit, order-only, and candidate-removal runs.
- Prove unrelated edits keep independent output cached or unchanged.
- Prove relevant edits change only dependent output.
- Prove candidate removal removes stale output.
- Use a fresh driver to prove byte-identical text and hints.
- Prove different semantic targets and output roles cannot collide.
- Add structural checks that reject diagnostic APIs and filesystem output in generator-owned code.

## Analyzer

### Recognize Violations

Reject an analyzer that:

- Registers a compilation-start action, compilation-end action, nested action, operation action, code-block action, or
  another bootstrap shape outside the two direct forms allowed below.
- Uses `KnownSymbols`, `AnalysisState`, an equivalent compilation cache, mutable static state, or symbols stored across
  compilations.
- Scans the whole compilation for a local declaration or syntax rule.
- Resolves metadata before cheaply rejecting unrelated candidates.
- Copies another component's checks merely to make both components look symmetrical.
- Contains an empty analysis hook kept only for symmetry.
- Introduces a model, equality, cache, service, or abstraction without data that requires it.
- Declares a diagnostic that is absent from `SupportedDiagnostics`.
- Changes an existing diagnostic identifier, severity, message, argument order, location, precedence, or multiplicity
  without explicit contract authority.
- Reports a broad declaration span or `Location.None` when an exact source token can identify the repair.
- Selects the first partial declaration without proving that it owns the invalid input.
- Throws for expected user source, ignores cancellation, or enables concurrency over unsafe state.

### Keep Project Ownership Separate

- Put diagnostic identifiers, descriptors, detection rules, location selection, `Diagnostic.Create`, and reporting in
  a dedicated analyzer project or assembly.
- Keep the analyzer project independent. Do not reference a companion component project.
- Use an existing diagnostic-neutral common project only when the target already owns one and reuse is justified.
- Do not create a shared project merely to avoid repeating a small feature-owned rule.
- Keep descriptor declarations in analyzer-owned files and include every descriptor in `SupportedDiagnostics`.

### Bootstrap Directly in `Initialize`

- Call `ConfigureGeneratedCodeAnalysis` with an explicit policy.
- Call `EnableConcurrentExecution` only when all analyzer state is immutable or callback-local.
- Register every analysis callback directly on the `AnalysisContext` parameter inside `Initialize`.
- Use only `RegisterSymbolAction` or `RegisterSyntaxNodeAction`.
- Do not wrap either call in `RegisterCompilationStartAction` or another callback.

Use direct symbol registration for declaration rules:

```csharp
public override void Initialize(AnalysisContext context)
{
    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    context.EnableConcurrentExecution();
    context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.NamedType);
}
```

Use direct syntax registration for one exact syntax form:

```csharp
public override void Initialize(AnalysisContext context)
{
    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    context.EnableConcurrentExecution();
    context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.InvocationExpression);
}
```

### Analyze Only Diagnostic Requirements

- Check cancellation first and inside loops.
- Reject unrelated candidates before resolving metadata or requesting semantic facts.
- Use a symbol action for declared type or member shape.
- Use a syntax-node action for one exact syntax form, then request semantic facts only after syntax filtering.
- Resolve required metadata from the callback compilation after candidate rejection; return safely when it is absent.
- Implement only the frozen user-facing diagnostic rules.
- Do not require diagnostic analysis to be identical to source-production analysis.
- Allow diagnostic analysis to inspect extra candidates, exact syntax, source locations, precedence, and multiplicity
  when its contract requires them.
- Align only overlapping validity boundaries. Do not force extraction mechanics, intermediate values, or traversal
  order to match another component.

### Keep Analyzer Data Minimal

- Keep symbols, syntax nodes, locations, and compilation values callback-local.
- Introduce a small `readonly` carrier only when several checks need the same few values.
- Store only values needed to decide, locate, or format a diagnostic.
- Prefer local variables and short pure helpers over persistent state.
- Do not add equality to callback-local diagnostic data unless it is genuinely compared or keyed.

### Report Exact Diagnostics

- Freeze each identifier, title, message, category, severity, enablement, description, help link, location, argument
  order, precedence, and multiplicity.
- Report the narrowest useful token or expression the user must change.
- For a wrong argument, report that argument expression.
- For a missing modifier, report the declaration identifier or the target repository's exact insertion location.
- For an invalid member use, report the relevant member name, type syntax, or argument.
- For a partial type, select the declaration that carries the invalid source.
- Preserve deliberate multiple reports when each occurrence needs repair.
- Prevent cascading diagnostics when an earlier invalid value makes later analysis meaningless.

### Prove the Analyzer

- Add a syntax-aware structural test that proves each registration is a direct call on the `Initialize` parameter.
- Add structural tests that reject `RegisterCompilationStartAction`, nested registration, compilation caches, and
  equivalent state-holder types.
- Assert exact descriptor membership and every public descriptor field.
- Assert exact diagnostic identifier, severity, file, span, arguments, precedence, and multiplicity.
- Cover valid input, each invalid boundary, similar valid input, missing metadata, aliases, partial declarations,
  nested and generic declarations, generated-code policy, multiple violations, and cancellation behavior.
- Verify expected diagnostics come from the analyzer type and that no extra diagnostics appear.

## Printer

### Find an Existing Equivalent First

- Search the target repository for names such as `Printer`, `SourceWriter`, `CodeWriter`, `IndentedTextWriter`, and
  wrappers around `StringBuilder`.
- Search for behavior as well as names: fragment append, conditional append, indented line start, line termination,
  complete line output, indentation changes, scope opening and closing, fixed newline, and result access.
- Read current users and exact-output tests for each candidate.
- Verify target framework support, ownership, output behavior, and license before selecting a candidate.
- Reuse a target-owned equivalent when it provides the required behavior.
- Record the exact missing capabilities before adding a new type.
- Do not add a second writer merely because existing method names differ.

### Add a Fresh Minimal Fallback Only When Needed

- Do not copy a `Printer` implementation from another project.
- Implement a fresh target-owned type from required behavior only.
- Add it to the smallest existing component or feature project that owns the writers.
- Use one private `StringBuilder`, fixed indentation, and a fixed `\n` newline.
- Create one instance per generated output.
- Implement only these core capabilities when current writers need all of them:

```csharp
internal sealed class Printer
{
    public string Result { get; }

    public Printer Print(string text);
    public Printer PrintIf(bool condition, string whenTrue, string whenFalse = "");
    public Printer PrintBeginLine(string text = "");
    public Printer PrintEndLine(string text = "");
    public Printer PrintLine(string text = "");
    public Printer PrintLineIf(bool condition, string whenTrue, string whenFalse = "");
    public Printer OpenScope(string opening = "{");
    public Printer CloseScope(string closing = "}");
    public Printer WithIncreasedIndent();
}
```

- Let `PrintBeginLine` write indentation once, `Print` append fragments, and `PrintEndLine` terminate the line once.
- Let `PrintLine` write indentation, text, and newline.
- Let `PrintIf` select one inline fragment without building temporary fragment lists.
- Let `OpenScope` write the opening token and increase persistent indentation.
- Let `CloseScope` decrease persistent indentation and write the closing token.
- Let `WithIncreasedIndent` share output storage while leaving caller indentation unchanged.
- Add `PrintBeginLineIf` or `PrintEndLineIf` only when conditional partial-line output requires it.
- Add relative indentation, indent-depth access, or width information only when verified line-width logic requires it.
- If the type is a mutable value type, pass section writers by `ref` when their mutations must affect caller state.
- Do not require `ref` for a reference type.
- Do not add clearing, pooling, numeric overloads, debug helpers, callback helpers, alternate scope APIs, or capacity
  controls without a measured or current-source requirement.
- Format culture-sensitive values before passing text to `Print`.
- Do not retain Roslyn objects, inspect compiler state, or write files through `Printer`.
- Do not assemble ordinary output with fragment lists, fragment arrays, `string.Join`, or a second builder.

### Prove the Printer

- Assert exact bytes, fixed newline behavior, exactly one final newline, nested scopes, blank lines, fragment-built
  lines, conditional inclusion and exclusion, and temporary indentation.
- Compile representative generated output.
- Keep writer tests focused on observable source; do not expose implementation details without a contract need.
