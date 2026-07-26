# Design: Generator Authoring Toolkit (`Norse.Abstractions.Generator`) + Raw-String Emitter House Style

**Date:** 2026-07-25
**Status:** Approved, ready for planning
**Realm:** Asgard (new project), Bifröst (CLAUDE.md convention)

**Addendum (final review, 2026-07-25):** two corrections from the whole-branch review, recorded here
rather than rewritten into the body — the body below is the historical record of what was designed.

1. **The project shipped as `Norse.Abstractions.Emit`, not `Norse.Abstractions.Generator`.** The
   original name collided with the platform's duplicate-generator strip target
   (`_NorseRemoveUnwantedGeneratorAnalyzers` in each realm's `src/Directory.Build.targets`), which
   drops any `@(Analyzer)` item matching `^Norse\..+\.Generator$` that isn't the consumer's own
   wanted generator — it stripped the toolkit right back out of every consumer's analyzer set. The
   name was semantically wrong anyway: on this platform `Norse.X.Generator` means "the Roslyn
   generator serving `Norse.X`", and this assembly is a generator-*authoring toolkit* serving every
   realm's generator projects, not a generator and not specific to `Norse.Abstractions`.
2. **Rollout step 2's "plain in-repo `ProjectReference`" is not sufficient on its own.** MSBuild's
   `OutputItemType="Analyzer"` wiring pulls only the referenced project's own `GetTargetPath` output
   into `@(Analyzer)` — never that project's transitive `ProjectReference`s. A Roslyn generator
   project with a real assembly dependency must explicitly forward it by hooking
   `GetTargetPathDependsOn` with a target that appends the dependency to
   `@(TargetPathWithTargetPlatformMoniker)`; without it the generator loads and then dies with
   `FileNotFoundException`, surfaced to the consumer as `CS8785`. Rollout step 3's `dotnet pack` +
   `unzip` check proves the *package's* contents only — it says nothing about whether a
   ProjectReference-mode consumer actually loads the dependency. The acceptance test is building a
   real downstream consumer.

## Problem

Source generators across the platform emit code via repeated `sb.AppendLine(...)` calls, one
line at a time. This is hard to read at the call site — the generated shape has to be
reconstructed mentally from a sequence of separate statements instead of being visible as a
block. Prior art exists: Buvy's own `TaskTupleAwaiter.Generator` collapses these into single
`AppendCSharp(raw-string-literal)` calls, and the helper enabling it (`CSharpEmit.AppendCSharp`)
has already been ported once, into Urðarbrunnr's `Persistence.EntityFramework.Design.Generator.Shared`.

What's missing: a house style rule making this the platform default, and a real (not vendored-per-project)
home for the small polyfills every netstandard2.0 generator project needs.

## House Style Rule

`sb.AppendLine(...)` is never called directly in generator emitter code. Always `sb.AppendCSharp(...)`,
including for single-line appends (`sb.AppendCSharp("}")`) — one emission verb, no judgment call about
when to switch. Where a block of code would previously have been multiple sequential `AppendLine` calls,
it becomes one `AppendCSharp` call with a raw string literal (`"""..."""`), preserving the generated
code's shape and indentation as written.

This rule lands in **Bifröst's own `CLAUDE.md`** (§5 Conventions → Bifröst-specific additions), not the
global `~/.claude/CLAUDE.md` — Bifröst is the clone-once-and-run root every session and every new
contributor starts from, so it's where the rule needs to be found to produce compliant code, not buried
in a personal dotfile only one person reads.

## `Norse.Abstractions.Generator`

A new project, `Asgard/src/Abstractions.Generator/` (assembly `Norse.Abstractions.Generator`), living
under `src/` alongside Asgard's other declared-law assemblies — not under `gen/`, even though its only
consumers are generator projects. `gen/Directory.Build.props` only imports the realm root, not
`src/Directory.Build.props`, so a `gen/`-housed packable project would need `PackageId`, README/LICENSE
packing, and `InternalsVisibleTo` hand-added on top of flipping `IsPackable`/`IsRoslynComponent`. Under
`src/`, all of that is inherited for free; the only overrides needed are:

```xml
<TargetFramework>netstandard2.0</TargetFramework>
<IsAotCompatible>false</IsAotCompatible>
```

Starts with exactly three files, all relocated or ported rather than newly invented:

- `CSharpEmit.cs` — the `AppendCSharp` extension, `[StringSyntax("C#")]`-annotated, identical to
  `AppendLine` at runtime. Ported from `TaskTupleAwaiter.Generator`.
- `StringSyntaxAttribute.cs` — polyfill making `[StringSyntax]` available on netstandard2.0. Also
  ported from `TaskTupleAwaiter.Generator`.
- `IsExternalInit.cs` — **relocated**, not copied, out of `Abstractions.Contracts.Generator`. It's a
  generic netstandard2.0 polyfill with nothing specific to the gateway generator; it belongs in the
  shared toolkit, not vendored in one generator's project.

Namespace/assembly purpose: the platform's netstandard2.0 generator-authoring toolkit — the one place
behavioral polyfills for Roslyn generator projects live, since every such project is stuck on
netstandard2.0 regardless of what its consumer targets.

## Rollout

**Phase 1 — prove it in Asgard (this plan):**

1. Create `Abstractions.Generator` with the three files above.
2. `Abstractions.Contracts.Generator` takes a plain in-repo `ProjectReference` to it (default
   `ReferenceOutputAssembly=true` — a real compile-time dependency, distinct from the
   `OutputItemType="Analyzer"` / `ReferenceOutputAssembly="false"` pattern `Abstractions.Contracts`
   itself uses to reference its generator).
3. `Abstractions.Contracts.csproj`'s `IncludeGeneratorInPackage` target gets a second `None` item so
   `Norse.Abstractions.Generator.dll` also lands in `analyzers/dotnet/cs/` — a Roslyn analyzer package
   must be fully self-contained; the compiler's isolated load context doesn't resolve ordinary NuGet
   dependencies. Exact MSBuild shape is implementation-plan detail, not decided here.
4. Retrofit all four `Abstractions.Contracts.Generator` emitters (`ContractEmitter`,
   `OutcomeSurrogatesEmitter`, `WireHostEmitter`, `InProcessHostEmitter`) to the house style.

**Phase 2 — ship gate:** Buvy reviews. If good, `Norse.Abstractions.Generator` ships to NuGet as its
own package, its own version line — same ship-gate discipline as every other Norse package (PR merged,
CI green, tagged, published).

**Explicitly out of scope:** Urðarbrunnr's `EntityConfigurationApplicationGenerator.cs` and its existing
`Design.Generator.Shared/CSharpEmit.cs` migrating from a local copy to a `PackageReference` on the
published `Norse.Abstractions.Generator` package. Real follow-on work, not started, not planned here —
it happens after Phase 2's ship gate clears, in its own brainstorm → spec → plan cycle per Urðarbrunnr's
own CLAUDE.md.

## Non-Goals

- No change to `Abstractions.Contracts.Generator`'s own domain-specific model types
  (`GatewayMethodModel.cs`, `GatewayInterfaceModel.cs`, `Index.cs`) — only genuinely generic
  cross-generator polyfills move to the new project.
- No retrofit of Urðarbrunnr's generators in this pass (see Rollout, Phase 2 boundary).
- No change to `Svartálfheim` — it authors analyzers/BuildCheck rules, not source generators, and stays
  out of the generator-authoring toolkit's consumer set by design.
