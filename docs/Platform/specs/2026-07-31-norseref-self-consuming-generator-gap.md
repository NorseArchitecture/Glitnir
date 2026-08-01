# NorseRef Self-Consuming Generator Gap

**Status:** RESOLVED by design, 2026-07-31 (same day) — see `2026-07-31-norseref-strip-provenance-scoping-design.md`. The strip target is provenance-scoped: only NuGet-delivered analyzers are strip candidates, so a project's own `ProjectReference`-wired sibling generator is never stripped. The "point 2" anomaly below is also dissolved there: re-adding the analyzer failed because the scattered `Directory.Build.targets` imports after `CustomAfterMicrosoftCommonTargets`, so the externally injected re-add ran *before* the strip target (whose `AfterTargets` hook was silently ignored — the target didn't exist yet at evaluation time), not because of any multi-pass compiler behavior. Original diagnosis preserved below unchanged. Originally blocked Task 7 onward of `2026-07-31-well-and-wire-reference-data-slice.md` (Mimir's `Reference.Contracts` generator).

**Lineage:** direct third gap in the same family as `2026-07-01-norseref-generator-forwarding-design.md` ("Gap 1" and "Gap 2" below refer to that doc's own numbering). Read that doc first — this one assumes it.

---

## Why This Doc Exists

Discovered live while a subagent executed Task 7 of the well-and-wire plan: Mimir's `Reference.Contracts` project needs to consume its own sibling `Reference.Contracts.Generator` (a Roslyn incremental generator that parses the UNSD CSV at compile time and emits the `IsoCountryCode` enum + parse surface into the *same* project that references the generator). This is the platform's first case of a project's own compilation depending on its own generator's output — every prior generator-bearing package (`Persistence.EntityFramework`, `Abstractions.Web.Server`) only needs its generator to run against *downstream* consumers, never against itself.

The implementer correctly treated `Directory.Build.targets` as immutable/halt-and-ask per the plan's Global Constraints, fully diagnosed the gap (~15 isolated, reproducible experiments — not speculation), staged everything within its authority, and reported BLOCKED rather than hacking around it. That diagnosis is captured in full below because the ephemeral SDD workspace it was written into (`.superpowers/sdd/2026-07-31-well-and-wire-reference-data-slice/`) is deleted once the plan's branch finishes — this doc is the durable record.

**Buvy's framing, worth preserving verbatim as the design constraint:** the entire point of `UseProjectReferences=true` / the `NorseRef Generator="true"` forwarding mechanism (Gap 1 in the prior doc) is to avoid "consistently publish an analyzer to NuGet to then turn around and run it locally." Any fix to this gap must preserve that property — a workaround that falls back to "just pack and consume via NuGet even in the local dev loop" defeats the reason the mechanism exists at all.

---

## The Problem: Self-Consumption Is Architecturally Different From Forwarding

Gap 1 and Gap 2 (prior doc) are both about a generator **forwarding through** other projects — from the wrapper project, through a `ProjectReference` chain, into a downstream consumer's compilation (Gap 1), and then being stripped back out of consumers that shouldn't see it (Gap 2). Both fixes are keyed on `@(NorseRef)` items: a `NorseRef` represents "I depend on someone else's package," and `Generator="true"` says "...and I want their generator to run against me too."

**Self-consumption has no `NorseRef` to hang a fix on.** `Reference.Contracts.csproj` doesn't depend on itself via `NorseRef` — it wires its own generator directly:

```xml
<ProjectReference Include="../../gen/Reference.Contracts.Generator/Reference.Contracts.Generator.csproj"
	OutputItemType="Analyzer"
	ReferenceOutputAssembly="false" />
```

This is the exact pattern the prior doc's own "How To Adopt This" step 2 prescribes ("Wire the generator as an analyzer inside `{Package}`'s own `.csproj`") — correct, and necessary regardless of anything else, so that the NuGet package's `analyzers/` payload is right. But `_NorseRemoveUnwantedGeneratorAnalyzers` doesn't know the difference between "a generator analyzer that leaked in from a forwarded `NorseRef` chain and shouldn't be here" and "a generator analyzer this exact project directly and deliberately wired via its own raw `ProjectReference`." It strips both, because its allow-list is built entirely from `@(NorseRef->WithMetadataValue('Generator','true'))`, and there is no `NorseRef` item — by construction — representing "myself."

**Trying to fake one in doesn't work.** Adding `<NorseRef Include="Reference.Contracts" Generator="true"><Repo>Mimir</Repo></NorseRef>` to `Reference.Contracts.csproj` to get onto the allow-list also triggers the `Choose` block's *other* effect (Gap 1's fix): it unconditionally adds a second, regular `ProjectReference` to `%(Repo)/src/%(Identity)/%(Identity).csproj` — which resolves to `Mimir/src/Reference.Contracts/Reference.Contracts.csproj` referencing itself. Confirmed via direct test: fails immediately at restore with `MSB4006: circular dependency in the target dependency graph`. The `NorseRef` mechanism's dual purpose (wrapper reference + generator reference, always both) makes it structurally incapable of expressing "I already have a direct reference to my own generator, I just need the strip target to leave it alone."

---

## Confirmed Symptom

`dotnet build src/Reference.Contracts/Reference.Contracts.csproj` succeeds with 0 warnings/0 errors, but the resulting `Norse.Reference.Contracts.dll` has **zero types** — the generator never actually runs. `tests/Reference.Contracts.Tests` then fails with `CS0103`/`CS0246` (`IsoCountryCode`/`IsoCountryCodes` don't exist) — 10 real compile errors, not a flake.

## Confirmed Platform-Wide Exposure — Not Mimir-Specific

Two already-shipped realms carry the **identical** self-referencing pattern today, dormant only because neither generator currently has anything to emit against its own project's own declarations:

- **Asgard**, `src/Abstractions.Web.Server/Abstractions.Web.Server.csproj` — references `../../gen/Abstractions.Web.Server.Generator/Abstractions.Web.Server.Generator.csproj` the same raw way. Its handler-registration generator discovers `IRequestHandler`/`IValidator` implementations; `Abstractions.Web.Server` itself declares none of its own, so the bug never surfaces.
- **Urðarbrunnr**, `src/Persistence.EntityFramework/Persistence.EntityFramework.csproj` — same pattern. Its entity-discovery generator finds `INorseEntity<TSelf>` implementations; `Persistence.EntityFramework` itself declares none, same reason it's never surfaced.

Both were independently re-verified (not just taken on the implementer's word) by reading the live `.csproj` files directly on 2026-07-31 — confirmed identical `<ProjectReference ... OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` shape in both.

**Task 7 is the platform's first case where this actually matters**, because `Reference.Contracts` needs its *own* enum/parser to exist for its *own* test project to compile against it. Any future package with the same shape (a generator that discovers something declared inside its own wrapper project) will hit this identically.

## Root Cause — Confirmed via ~15 Isolated Reproductions

1. **The strip target itself is *a* confirmed cause.** A from-scratch minimal project living *inside* `Mimir/src/` (inheriting the real props/targets chain, `Norse.Abstractions.Emit.dll` present alongside the generator DLL) still failed to produce generated output; the identical pair living *outside* the chain (a bare `/tmp/analyzertest` project with zero Bifröst machinery) worked correctly, loading and running the generator via a plain `<Analyzer Include="...">` item. Zero other variables between the two — isolates the cause to the `Directory.Build.targets` chain specifically, not TFM, not analyzer settings (`TreatWarningsAsErrors`/`WarningLevel`/`EnforceCodeStyleInBuild`/`AnalysisLevel*` were all copied into the isolated repro and it still worked there), not a missing-dependency issue.
2. **There may be a second, not-fully-isolated contributing factor.** `@(Analyzer)` was confirmed present (via temporary `<Message>` diagnostics and a `CoreCompileDependsOn` append) at multiple checkpoints right up to `CoreCompile` — but manually restoring the item *after* the strip target ran did **not** restore the generated output in one experiment. This points at some deeper multi-pass build behavior in this SDK/preview-channel build (`11.0.100-preview.6.26359.118`) that wasn't fully unraveled. **Any proposed fix needs to be verified end-to-end against a real build, not assumed correct from the strip-target analysis alone** — this is the one piece of the diagnosis that isn't fully closed.
3. All experimental/scratch artifacts (`/tmp/analyzertest`, `/tmp/reflect`, a temporary `Mimir/src/ScratchTest/`, debug `<Message>`/marker-generator code) were removed; nothing exploratory was left in any working tree.

## Candidate Fix Direction (Not Yet Designed — Buvy's Call)

The implementer's suggestion, offered as a starting point for the real design pass, not a final answer given point 2 above: give `_NorseRemoveUnwantedGeneratorAnalyzers` a second allow-list path that doesn't route through `NorseRef` at all — exempt any `@(Analyzer)` item whose resolved path lives under the *current* project's own `../gen/` sibling directory (`$(MSBuildProjectDirectory)/../../gen/`). This is exactly the shape every `src/{Package}` + `gen/{Package}.Generator` pairing already follows by the prior doc's own naming convention (step 1: "sibling to `{Package}` in the same realm's `src/`"), so it's a structural, pathname-based recognition of "this analyzer was never forwarded from anywhere — it's mine" rather than another entry in the `NorseRef`-keyed allow-list. Doesn't touch the cross-repo `Generator="true"` path at all. Would retroactively make Asgard's and Urðarbrunnr's dormant self-references correct too, closing the platform-wide exposure, not just Mimir's instance.

**Before this (or any) fix is adopted:** it needs to actually be built and verified against a real `dotnet build` producing real generated output in `Reference.Contracts.dll` — the unresolved point-2 anomaly above means "strip target excludes the self-path" might not be sufficient by itself.

## What Would Unblock the well-and-wire Plan

Task 7's own generator logic, CSV parser, and name-sanitizer are fully implemented and independently verified correct in isolation (2/2 generator tests pass against both a synthetic 5-row excerpt and the real 248-row UNSD dataset — zero identifier collisions). The work is staged, not committed, on `feature/well-and-wire` in Mimir. Once this gap has a real fix, Task 7 needs only: confirm `Reference.Contracts.csproj` actually produces `IsoCountryCode`/`IsoCountryCodes` on a normal build, then `Reference.Contracts.Tests` should compile and its own test suite (already written) should just run.

## Still Open

- The actual fix design (candidate direction above is a starting point, not a decision).
- The unresolved secondary anomaly (point 2 above) — needs isolation before any fix is trusted.
- Whether the fix belongs in `Bifrost/Directory.Build.targets` (dev-loop-only, matching where Gap 1 lives) or needs a scatter-template change too (matching where Gap 2's fix lives, since Gap 2's problem — and possibly this one — needs to hold even in a genuinely standalone, non-Bifröst checkout, i.e. real CI). Worth checking whether the self-consumption case can even arise outside `UseProjectReferences=true` — in real `PackageReference` mode, `Reference.Contracts`'s own generator is already correctly wired into its own package via the direct `OutputItemType="Analyzer"` reference (that part was never in question); the question is only whether NuGet-mode's *own* analyzer propagation has an equivalent self-stripping gap or whether it's Gap-1-shaped (dev-loop-only).
- Per Bifröst's own process law (§7): `Directory.Build.targets` is Bifröst's own tracked file, but a direct-to-master fix there requires every other realm submodule to also be on `master` with nothing else in flight — not true right now (five realms carry active `feature/well-and-wire` branches). The fix needs to land after this slice's realm branches are merged or reconciled, not mid-flight.
