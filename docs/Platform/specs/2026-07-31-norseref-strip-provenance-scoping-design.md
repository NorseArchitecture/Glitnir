# NorseRef Strip Provenance Scoping — Design

**Status:** Decided 2026-07-31, verified by experiment before adoption. Resolves `2026-07-31-norseref-self-consuming-generator-gap.md` (the gap that halted Task 7 of `../plans/2026-07-31-well-and-wire-reference-data-slice.md`), including its previously unexplained "point 2" anomaly.

**Lineage:** third document in the NorseRef generator family. Read `2026-07-01-norseref-generator-forwarding-design.md` (Gaps 1 and 2) and the gap doc above first — this one assumes both.

---

## The Decision

`_NorseRemoveUnwantedGeneratorAnalyzers` gains one leading conjunct: **only analyzers that arrived through the NuGet package chain are strip candidates.**

```xml
<Analyzer Remove="@(Analyzer)" Condition="'%(Analyzer.NuGetPackageId)' != '' and $([System.Text.RegularExpressions.Regex]::IsMatch('%(Analyzer.Filename)', '^Norse\..+\.Generator$')) and !$(_NorseWantedGeneratorAnalyzerNames.Contains(';%(Analyzer.Filename);'))" />
```

The doctrine the condition now encodes, and which the target's comment is rewritten to state: **the strip exists solely to undo NuGet's transitive analyzer propagation (Gap 2). An analyzer that arrived via `ProjectReference` — a realm's own sibling `gen/` generator, or the Bifröst `Choose` block's dev-mode forwarding — is deliberate by construction and is never a strip candidate.** The `NorseRef Generator="true"` allow-list is untouched; it still governs the NuGet-delivered case, which is the only case it ever actually applied to.

## Why Provenance Is the Right Key

Gap 1's founding fact does the heavy lifting: **MSBuild does not forward `OutputItemType="Analyzer"` items transitively through plain `ProjectReference` chains.** That is why the `Choose` block has to add the generator reference explicitly in dev mode — and it equally means dev mode has no accidental analyzer flow to clean up. Every `Norse.*.Generator` analyzer present in a `UseProjectReferences=true` compilation got there because *this project* asked for it: either its own raw sibling-generator wiring or its own `NorseRef Generator="true"`. The only mechanism on the platform that pushes a generator into a compilation that didn't ask for it is NuGet's transitive analyzer propagation — the exact thing Gap 2's strip was built against, and the exact set `NuGetPackageId` metadata identifies.

The metadata split is clean and was confirmed empirically on the live toolchain (SDK `11.0.100-preview.6`):

| Provenance | `NuGetPackageId` | `MSBuildSourceProjectFile` |
|---|---|---|
| NuGet package (SDK `ResolvePackageAssets`) | set | empty |
| `ProjectReference` (`OutputItemType="Analyzer"`) | empty | set (the generator `.csproj`) |
| SDK-built-in analyzers | empty | empty |

SDK-built-in analyzers never match the `^Norse\..+\.Generator$` regex, so the conjunct's only behavioral delta is exempting `ProjectReference`-sourced Norse generators — precisely the self-consumption case, in both build modes. `Repo` metadata on `NorseRef` items plays no role here: cross-repository vs. intra-repository is fully captured by *how the DLL arrived*, not by where its source lives.

## What Verification Proved

All experiments ran against Mimir's blocked Task 7 projects (`Reference.Contracts` + its tests, `feature/well-and-wire`), with zero repository files modified — injection via MSBuild global properties only.

1. **The strip target is the sole cause, and the gap doc's "point 2" anomaly is dissolved.** The anomaly (re-adding the analyzer after the strip did not restore generation) was an import-order trap, not multi-pass compiler behavior: the scattered `Directory.Build.targets` imports *after* `CustomAfterMicrosoftCommonTargets`, so an externally injected redefinition of the target silently loses to the scattered definition, and an `AfterTargets` hook on a target that does not exist yet at evaluation time is silently ignored (MSBuild logs "listed in an AfterTargets attribute … does not exist in the project, and will be ignored"). In the diagnostic log the re-add target (TargetId 445) demonstrably ran *before* the strip (TargetId 446), which then re-removed the item. The original implementer's failed re-add experiment died the same way.
2. **The `ProjectReference`→`Analyzer` channel works end to end.** `Norse.Abstractions.Emit.dll` — swept into the analyzer channel by the same gen-project reference, but failing the `.Generator$` regex — survived to the final `csc` command line untouched. Nothing between the strip target and `Csc` mutates the item list; exempting an item is sufficient, no re-add machinery is needed.
3. **The fix works.** With the scattered template replicated via `-p:DirectoryBuildTargetsPath` injection and only the new conjunct added, a full rebuild ran the generator for real: `IsoCountryCode.g.cs` (1,281+ lines) was emitted and compiled into `Norse.Reference.Contracts.dll`. The build then failed on a *different, real* defect in the generated code (see Watch Items) — the strongest possible evidence the generator is executing against its own project's compilation.

## The Change

Two files in Ginnungagap's scatter source, one conjunct each, plus the comment rewrite described above:

- `config/src/Directory.Build.targets`
- `config/tests/Directory.Build.targets`

The `gen/` template carries no strip target and is untouched. `Bifrost/Directory.Build.targets` (the `Choose` block, Gap 1's fix) is untouched — this design changes no Bifröst-tracked file, so the §7 branch law question never arises.

## Rollout

1. **Ginnungagap:** edit the two templates in the scatter source; Buvy merges and scatters the runes on his own schedule (the scatter fans out as a PR per realm).
2. **Mimir, immediately:** hand-sync the same one-line edit into Mimir's checked-out `src/Directory.Build.targets` and `tests/Directory.Build.targets` on `feature/well-and-wire`, ahead of the scatter — the same precedent as Yggdrasil's hand-sync in the Gap 1/2 rollout. The eventual scatter PR lands as a no-op diff there.
3. **Every other realm:** via the scatter. No urgency — the change is behaviorally inert for realms without self-consuming generators.

**Retroactive effect:** Asgard's `Abstractions.Web.Server` and Urðarbrunnr's `Persistence.EntityFramework` self-references stop being silently stripped and become functional. No observable change today (their generators discover nothing declared in their own wrapper projects), but the platform-wide exposure the gap doc flagged is closed structurally — the next generator that *does* emit against its own project's declarations just works.

## Rejected Alternatives

- **Own-`gen/` path exemption** (the gap doc's candidate direction): works for self-consumption, but with weaker semantics — it special-cases one legitimate `ProjectReference` shape while still stripping others, of which no illegitimate ones can exist (Gap 1's founding fact again). It also rests on path-prefix string comparison, and the diagnostic logs showed `//wsl.localhost/…` UNC aliasing leaking into item paths on a WSL toolchain — exactly the kind of silent mismatch path matching invites. Provenance metadata is representation-independent.
- **Teaching `NorseRef` to express "self":** already disproven in the gap doc — the item's dual wrapper-plus-generator nature resolves a self-`NorseRef` into a self-`ProjectReference` (`MSB4006`, circular), and any metadata split to avoid that adds per-realm ceremony to buy nothing the raw sibling reference doesn't already provide. The raw reference is needed regardless for the NuGet package's `analyzers/` payload.

## Done-Bar

`dotnet build -t:Rebuild` on `Mimir/src/Reference.Contracts` with the real (hand-synced) templates emits `IsoCountryCode.g.cs` into the compilation. The expected next failure is the generated code's missing `using System.Collections.Frozen;` (below) — that failure *is* the pass signal for this design. NuGet-mode verification rides the branch's CI run, which is the only place that mode genuinely executes.

## Watch Items

- **Next domino, owned by the well-and-wire plan (Task 7's first move on resumption):** the generator's emitted `IsoCountryCode.g.cs` calls `ToFrozenDictionary` without a `System.Collections.Frozen` using in scope — `CS1061` ×3 in the wrapper project, whose net11.0 `ImplicitUsings` don't include that namespace. The generator's own 2/2 tests didn't catch it because `GeneratorTestHarness.CreateCompilation` hand-supplies `global using System.Collections.Frozen;` — the harness's compilation is more permissive than the real consumer's, masking exactly this class of mismatch. Fix the emission (self-contained usings or fully-qualified calls) and align the harness's global usings with what a real consumer actually has.
- **Benign but noisy:** the sibling-gen `ProjectReference` also sweeps `Norse.Abstractions.Emit.dll` into the analyzer channel (it rides the generator project's target outputs), and it now reaches `csc` as an `/analyzer:` in every self-consuming project. Roslyn loads it, finds no analyzers, moves on. Recorded here so nobody rediscovers it as a mystery; fixing the sweep (if ever) is its own small look at the gen-project reference shape, not this design's business.
- `NuGetPackageId` is SDK-assigned metadata (`ResolvePackageAssets`), stable across SDK generations but re-verify if the platform ever moves off the standard SDK resolution path.
- **Future hardening (Buvy, 2026-07-31, deferred until well-and-wire ships):** roll out rules analyzers / BuildCheck coverage (Svartálfheim's beat) that verify the MSBuild contours are in the right spots — e.g. a sibling-gen reference carries `OutputItemType="Analyzer"` + `ReferenceOutputAssembly="false"`, `NorseRef Generator="true"` appears only on genuine leaf consumers, and no raw `PackageReference` to a `Norse.*` package bypasses the `NorseRef` chain. The strip target's contract is now doctrine; analyzers are how doctrine stops regressing.
