# MSBuild Estate Consolidation — The Runes

**Date:** 2026-08-07
**Status:** Approved in session 2026-08-07; amended same session after external review (11 items triaged: 7 folded as-is, 3 narrowed, 1 naming ruling — see §2.7's taxonomy and §3 for the rejected remainders).
**Owner:** Buvy
**Companion specs:** `2026-06-26-use-project-references-design.md` (the original switching machinery this design consolidates); `2026-07-01-norseref-generator-forwarding-design.md` and `2026-07-31-norseref-strip-provenance-scoping-design.md` (the strip target this design hoists); `2026-08-03-realm-dependency-law-compiler-enforcement-design.md` (the realm-root targets and analyzer delivery Choose this design extends); `2026-06-26-platform-config-sync-design.md` (the scatter mechanism that fans every canonical file below).
**Supersedes:** `../../msbuild-deep-dive-docket.md` (the docket this session executes — deleted by the plan); `../../the-two-crossings.md` as a standalone page (absorbed into `../../the-runes.md` — deleted by the plan, referrers updated).

---

## 1. Context

The MSBuild layer is one of the things that genuinely sets this stack apart from its .NET peers, and it has quietly accreted a real architecture: brand injection, the `NorseRef` two-crossings machinery, realm-root targets chaining, evaluation-order law, analyzer/generator forwarding with provenance-scoped stripping, packaging targets, and the Ginnungagap scatter that keeps thirteen repos honest. Each piece landed in its own session with its own spec; the whole has never been designed — or documented — as one thing.

The thesis the whole estate serves, stated once and carried into every document this design produces: **a single property, `UseProjectReferences`, selects which crossing resolves every cross-realm dependency inside the Bifröst workspace.** `true` gives 100% local builds — the whole stack developed across submodules without releasing anything until the end. `false` reproduces each repository's CI dependency-resolution policy — build the current repo from source, resolve every sibling from NuGet the way GitHub Actions does. A genuinely standalone checkout has no project-reference mode by construction (no siblings on disk); its fallback is the same package crossing CI uses, which is the point. That one toggle takes the classic submodule complaints off the table (a stale `master` pointer in Bifröst cannot affect shipping software), keeps releases transparent (each realm publishes its own version: tag → artifacts → release, no hidden coupling), and makes any CI-only failure locally reproducible by flipping one flag — with one recorded, deliberate asymmetry: workspace mode runs globally-attached realm law that a closure-scoped package build may not deliver (§2.5).

Two inputs triggered this consolidation now:

1. **The docket** (`msbuild-deep-dive-docket.md`, filed 2026-08-06; deleted, absorbed into `the-runes.md`) accumulated the full inventory: the two-crossings doctrine, the evaluation-order lessons proven live 2026-08-03, the analyzer-forwarding gaps bitten twice (2026-08-04 Urðarbrunnr, 2026-08-06 Midgard PR #61), and one parked open item — cross-realm workspace-mode forwarding of realm-owned analyzers.
2. **A prior production estate** (the direct ancestor of the `NorseRef` machinery) contributed four proven walls from its Microsoft.Build.Sql/DacFx work that generalize lessons this platform's doctrine already half-owns (§2.7).

A full sweep of the working tree (2026-08-07) confirmed the estate is disciplined: across twelve realms, only four files diverge from Ginnungagap's canonical templates, every one deliberate — Midgard's realm-root targets (NORSE080 forwarding) and Yggdrasil's three CPM variants. The complexity to reduce is *structural duplication inside the canonical templates themselves*, not drift.

---

## 2. Rulings

### 2.1 One doctrine page: `the-runes.md`

A single top-level doctrine page, `Glitnir/docs/the-runes.md`, peer to `the-two-unions.md` — named for the metaphor the platform already uses (`scatter-the-runes.ps1` scatters exactly these files: carved law, distributed to every realm, read everywhere).

It **absorbs `the-two-crossings.md` entirely.** The old file is deleted, not stubbed; every referrer is updated in the same change — including the comment pointers baked into Bifröst's root `Directory.Build.targets` and the Ginnungagap-scattered templates (which fan out via scatter), Bifröst's CLAUDE.md, and every Glitnir document citing the old path.

Chapter structure — thesis first, mechanism second, ledger last; every chapter opens with the why in prose, and file excerpts stay minimal because the files themselves keep their load-bearing comments:

1. **Why** — the `UseProjectReferences` thesis (§1 above, at full altitude).
2. **The layer map** — the four layers (Bifröst root / realm root / group level / project), who authors each file (hand-authored vs Ginnungagap-scattered), and the chain-or-fallback walk.
3. **The two crossings** — the absorbed doctrine, polarity table extended with the DacFx rows (§2.7).
4. **Evaluation-order law** — the four proven lessons as one chapter (§2.4).
5. **Analyzer and generator delivery** — the full delivery matrix: Roslyn analyzer / Roslyn generator / DacFx rules × the two crossings; provenance-strip doctrine including the DacFx mirror twin; the forwarding blocks and the manifest mechanism (§2.5).
6. **Packaging and release** — MinVer, generator bundling (`IncludeGeneratorInPackage`), README/LICENSE packing, tag → artifacts → release.
7. **The scatter** — manifest groups, the ownership story, the canonicity law (a scattered file is canonical by definition; realm law lives in the manifest or the realm seam, never in a scattered file), and the divergence guard.
8. **Schema projects** — the dacpac chapter (§2.7), including the explicit boundary: DacFx remains rejected for the platform's own persistence (EF Core migrations + Postgres constraints, Key Rejections intact); this is the consumer-facing story for bridges that chose SQL Server.
9. **Postmortems and probes** — the Task 7 strip incident (carried over from the absorbed page), the `-p:DirectoryBuildTargetsPath` global-property probing technique, and the verify-rule pack-into-throwaway-feed harness pattern.

### 2.2 The hoist: realm-root targets becomes the single home of shared resolution law

The standalone `NorseRef` fallback and the `_NorseRemoveUnwantedGeneratorAnalyzers` strip target currently exist as three copies each (canonical `src/`, `tests/`, `gen/` `Directory.Build.targets` — with `gen/` missing the strip entirely, and Yggdrasil hand-maintaining CPM variants of two of them). Both hoist into the scattered realm-root `Directory.Build.targets` — the file minted 2026-08-03 as "law that binds src, tests, and gen exactly once" — beside the Architecture.Analyzers delivery Choose already living there.

**The hoisted fallback** is a second `Choose`, preserving the load-bearing condition the group files encode today — the fallback keys on *Bifröst's absence*, never on `UseProjectReferences`, so workspace-package-mode cannot double-emit:

- **Workspace** (`Exists('$(_ParentTargets)')` — the realm root already computes this, and in the peer tree it *is* Bifröst's root targets) → emit nothing. Bifröst's Choose owns both crossings, including workspace-package-mode with `NorseRefVersion=*-*`.
- **Standalone + CPM** (`ManagePackageVersionsCentrally=true`) → `PackageReference` with no `Version` attribute — CPM supplies it (NU1008 forbids the attribute).
- **Standalone otherwise** → `PackageReference Version="*"` — stable releases only, the unchanged CI posture.

`NorseDesignRef` rides both standalone branches with `PrivateAssets=all`, exactly as today; a realm with no items in a list contributes nothing.

**The version-ownership asymmetry is deliberate and now recorded** (previously tribal): library realms float (`Version="*"` / `*-*`) because they do not own the deployed dependency closure — their packages are ingredients, not deployments. Yggdrasil owns the closure through CPM and pins explicit versions in `Directory.Packages.props` — which is what lets a Sev1 dependency update be made and shipped from the composition root without republishing a single intermediate realm. A future reviewer reading a floating library fallback is looking at the ownership boundary, not an omitted reproducibility mechanism. Carried into `the-runes.md` chapter 6.

**The hoisted strip target** applies uniformly to src, tests, and gen — closing the dormant `gen/` gap this session's sweep found. That gap is exactly the "no live exerciser yet" shape the Task 7 postmortem warns about: a `gen/` project consuming a `NorseRef`'d library that itself forwards a generator would regenerate and collide, and nothing strips it today.

**The group-level targets shrink to their actual subject:** `src/` = `OutputType` + chain import; `tests/` = `OutputType=Exe` + chain import; `gen/` = chain import (its Roslyn scaffold stays in `gen/Directory.Build.props`). The `_BifrostTargets` two-hop probe disappears from all three, along with its "existence stopped meaning workspace" caveat comment.

### 2.3 `IsPackable` moves to props; `OutputType` stays in targets — and the asymmetry is doctrine

Verified against the installed SDKs (net11 preview, confirmed identical shape in the SDK source):

- **`IsPackable`** — both the Web and Worker SDKs set their `false` default **conditioned on empty** (`Microsoft.NET.Sdk.Web.ProjectSystem.props`: `<IsPackable Condition="'$(IsPackable)' == ''">false</IsPackable>`; same in `Microsoft.NET.Sdk.Worker.props`). `Directory.Build.props` evaluates before SDK props, so a props-level `true` survives: the SDK's condition sees non-empty and skips.
- **`OutputType`** — both SDKs set `Exe` **unconditionally**. A props-level `Library` is stomped; only targets-time placement wins.

Therefore: `IsPackable=true` moves from the canonical `src/Directory.Build.targets` to the canonical `src/Directory.Build.props`, where it belongs three ways at once — it survives (the SDK default is conditioned), it sits with the rest of the packaging identity (`PackageId`, MinVer, README/LICENSE packing), and the `nuget`-group scatter gives the clean bifurcation line: the property structurally never reaches Yggdrasil. `OutputType=Library` stays in `src/Directory.Build.targets` with a corrected comment stating the *real* reason it cannot follow: the SDK counterpart is unconditional.

The asymmetry itself enters `the-runes.md`'s evaluation-order chapter: **a conditioned SDK default yields to props; an unconditional one yields only to targets** — with both polarities sitting in adjacent lines of the same SDK file as the teaching example.

### 2.4 The evaluation-order chapter — four proven lessons, one law

Consolidated in `the-runes.md` chapter 4, each with its live proof:

1. **CPM is invisible at props time.** `ManagePackageVersionsCentrally` is defined by `Directory.Packages.props`, which imports *after* `Directory.Build.props` — a props-level `Choose` misfires the versioned branch under CPM (NU1008; proven live 2026-08-03). Delivery Chooses live at targets evaluation time.
2. **SDK implicit usings exist only at targets time.** A props-level `<Using Remove>` precedes the SDK's `Include` and removes nothing (the `System.Net.Http.Json` ban, proven live 2026-08-03).
3. **`<Import>` resolves during the properties pass — before items exist.** An import path can never be derived from item lists, no matter the syntax; every item-to-property conversion inside an `Import` evaluates to literal unexpanded text (proven exhaustively in the prior estate, which burned a session confirming it). File-glob wildcards in `Import` are fine — they are path expansion, not item expansion (this is what makes §2.5's manifest aggregation legal).
4. **Conditioned vs unconditional SDK defaults** (§2.3).

### 2.5 Realm analyzer manifests — the parked open item, answered

**The manifest.** A realm that ships public analyzers declares them once, at its own root, in `Directory.Analyzers.props` (brand-free, riding the `Directory.*` family idiom). Paths are self-anchored — no `Repo` metadata, no path convention to honor, and a realm whose analyzers live outside `gen/` is equally expressible:

```xml
<Project>
	<ItemGroup>
		<NorseRealmAnalyzer Include="$(MSBuildThisFileDirectory)gen/Infrastructure.Web.Grpc.Analyzers/Infrastructure.Web.Grpc.Analyzers.csproj" />
	</ItemGroup>
</Project>
```

**The aggregation.** Bifröst's root `Directory.Build.targets` wildcard-imports `$(MSBuildThisFileDirectory)*/Directory.Analyzers.props`. Realms without the file contribute nothing; a realm landing its first analyzer edits only its own repo; Bifröst never needs touching again. Inside the `UseProjectReferences=true` branch, one generic block attaches every declared analyzer to every workspace compilation (`OutputItemType="Analyzer"`, `ReferenceOutputAssembly="false"`) with three generalized exclusions:

1. the analyzer project itself (self-reference);
2. any other manifest-declared analyzer project (analyzers do not analyze each other, matching the existing mutual exclusions);
3. any Aspire AppHost (`$(IsAspireHost)`), for the reasons already recorded on the existing blocks.

The two hand-authored platform blocks (Architecture.Analyzers, Primitives.Analyzers) become entries in **Svartálfheim's manifest**, and Bifröst's root targets shrinks to the one generic mechanism.

**Identity and exclusion semantics are design, not mechanics.** Nothing enforces project-name uniqueness across realms, so name-only comparison can exclude an innocent stranger; and Roslyn resolves duplicate analyzer *assembly names* by loader luck. Rulings: analyzer identity is **normalized `MSBuildProjectFullPath`**; exclusions compare full paths, never bare names; aggregation rejects duplicate analyzer project paths and duplicate analyzer assembly file names as errors, asserted in the §4 verification matrix.

**The manifest contract.** `Directory.Analyzers.props` is technically executable MSBuild; the contract keeps it data-shaped without inventing a data format (these are first-party, PR-reviewed, org-owned files — the trust boundary is the org itself): exactly one `ItemGroup`; `NorseRealmAnalyzer` items only; no properties, no imports, no targets; every path inside the declaring realm, existing, and pointing at a `.csproj`. The §4 matrix asserts all of it; a violating manifest is a build-verification failure, not a code-review hope.

**Manifest analyzers are diagnostics-only — never source-emitting.** A generator arriving by manifest would emit into every workspace compilation unconditionally; generators remain consumer-declared (`Generator="true"`), which the design enforces structurally, and the law makes the misuse reviewable. Scope metadata (`Global`/`Realm`/`ReferenceClosure`) is deliberately not added — three current analyzers, all diagnostics-only, all no-op off-subject. Revisit trigger, by name: the first analyzer that genuinely cannot attach globally.

**The manifest drives both modes — no hand-authored realm blocks anywhere.** The canonical realm-root `Directory.Build.targets` gains one generic block: import the realm's own sibling `Directory.Analyzers.props` (static path, `Exists`-conditioned — legal at the properties pass) and attach its declared analyzers realm-internally, conditioned to standalone mode (`!Exists('$(_ParentTargets)')`), since in workspace mode Bifröst aggregates that same manifest and is the sole attacher. This answers the docket's open coordination ruling *and* deletes Midgard's hand-authored NORSE080 block outright — Midgard's realm-root targets returns to byte-identical canonical (see §2.6 for why that matters more than tidiness). One declaration per realm, consumed by both modes' machinery; nothing double-attaches by construction rather than by dedup luck. NuGet-mode delivery is untouched: packaging already bundles the analyzers, and the package crossing propagates them through the reference closure.

**The recorded asymmetry.** Workspace mode now runs *more* law than the package crossing delivers — every realm analyzer, everywhere, not just through the reference closure. This is the correct polarity (fail earlier, fail locally; analyzers no-op where their subject is absent) and is recorded in `the-runes.md`'s crossing table as deliberate. The compile cost of loading all realm analyzers in every workspace compilation is the accepted price; no scoping metadata is added until real pain justifies it.

### 2.6 Scattered-file canonicity, the realm seam, and the scatter guard

The self-review of this design surfaced a latent live bug: `scatter-the-runes.ps1` copies every manifest-listed file with `Copy-Item -Force`, and scatter PRs ride `auto-approve.yml` — so Midgard's realm-root targets divergence (the NORSE080 block, PR #61, 2026-08-06) would have been silently reverted by the next scatter run, and NORSE080 forwarding silently lost. The docket's "Bifröst-root block coordinated with realm-root blocks" fix shape was sitting on ground the scatter mechanism does not support: there is no such thing as a realm-specific addition to a scattered file. Three rulings close the class:

1. **The canonicity law.** *A scattered file is canonical by definition — realm-owned law never edits one.* Realm-specific declarations live in the analyzer manifest (§2.5) or the realm seam (below), both unscattered. Recorded in `the-runes.md` chapter 7.
2. **The realm seam.** The canonical realm-root `Directory.Build.targets` ends with a conditional import of an unscattered, realm-owned sibling: `<Import Project="$(MSBuildThisFileDirectory)Directory.Realm.targets" Condition="Exists(...)" />`. Any future realm-specific targets law lands there, never in the scattered file. Justified by the concrete near-miss, not hypothesis; no props twin is minted until a props-shaped case is real. **The seam is additive only**: it may declare new items and new realm-owned targets; redefining a canonical property or target is a violation of the canonicity law, not a supported extension point. End-of-file placement is deliberate — realm additions may *react to* canonical state — and is not a license to mutate it.
3. **The scatter guard + audit.** `scatter-the-runes.ps1` gains divergence awareness: before overwriting, a destination file whose content does not belong to the canonical file's git lineage (any historical blob of `config/<file>` in Ginnungagap) is a **divergence — hard fail, per realm, listing the files**, overridable only by an explicit switch for deliberate reversion. Content matching a historical blob is merely stale and overwrites safely. `-DryRun` grows into a real audit mode reporting each file as current / stale / divergent. The lineage contract: the check **requires full history of the canonical repo** (CI must fetch Ginnungagap unshallowed — `actions/checkout` defaults to depth 1 and would blind it); a renamed canonical path starts a new lineage; and **"lineage unavailable" is its own hard-fail outcome, distinct from "divergent"** — never conflated with realm divergence, never silently passed. Silent reversion becomes impossible; remaining mechanics are plan-level detail.

### 2.7 Schema projects — dacpac support as canonical, opt-in law

**New canonical templates in Ginnungagap:** `schema/Directory.Build.props` and `schema/Directory.Build.targets`, in a new `schema` scatter group **assigned to no realm by default**. A repo opts in via the manifest the day it grows a `{Realm}/schema/{Name}.Database` Microsoft.Build.Sql project. The platform's own persistence ruling is unchanged and the doctrine page says so explicitly.

**`schema/Directory.Build.props`:** chain import (one hop to realm root, the same walk as `src/`), `DSP`, `ModelCollation`, `TreatTSqlWarningsAsErrors=True`, the Microsoft rule suppressions, and brand injection — `Norse.$(MSBuildProjectName)` as `SqlTargetName`/`PackageId`, the same one-edit rebrand law as assemblies.

**`NorseRef` extends with almost no new machinery:**

1. **Metadata passthrough is free.** MSBuild transforms preserve custom metadata, so a `<NorseRef>` carrying `DatabaseSqlCmdVariable` flows it into both crossings automatically — cross-realm database references (workspace sqlproj `ProjectReference` ↔ package-mode dacpac `PackageReference`) need zero new mechanism.
2. **`Rules="true"` metadata** resolves to an analyzer-shaped `ProjectReference` into `src/` — DacFx rules are ordinary `src/` libraries, unlike Roslyn generators' `gen/` convention. The ancestor estate's `Analyzer="true"` spelling is deliberately **not** carried forward: with Roslyn analyzers, analyzer manifests, and `Generator="true"` already in the estate, `Analyzer=` no longer names a role. The ruling is a three-suffix taxonomy — metadata carries role, the project name carries the rest (SQL-ness included):

| Project suffix | Metadata | What it is | How it arrives |
|---|---|---|---|
| `.Generator` | `Generator="true"` | Roslyn source emission, `gen/` | Consumer-declared `NorseRef` |
| `.Rules` | `Rules="true"` | DacFx schema diagnostics, `src/` | Consumer-declared `NorseRef` |
| `.Analyzers` | — (never on a `NorseRef`) | Roslyn diagnostics — realm law | Manifest-attached, no opt-in |

	DacFx rule libraries carry the `.Rules` project suffix — the name the role naturally takes — so metadata, filename, and package name rhyme — and the taxonomy is machine-checkable: a `Rules="true"` ref resolving to a non-`.Rules` project, a `Generator="true"` ref to a non-`.Generator` project, or `Analyzer=` metadata appearing anywhere in the Norse estate is convicted by the §4 assertions. `.Analyzers` deliberately has no metadata spelling — realm law arrives by manifest or package closure, never consumer opt-in; the absence is the enforcement. The strip targets separate for free: the Roslyn strip's `^Norse\..+\.Generator$` match cannot touch a `Norse.*.Rules` DLL, and the DacFx stale-strip keys on `Rules=`-declared names.

**`schema/Directory.Build.targets` carries the three wall-fixes as canonical law**, each field-proven in the prior production estate:

1. **`RunSqlCodeAnalysis=true` default under `UseProjectReferences=true`.** A rules package's packed `build/*.targets` — the thing that enables analysis and promotes rule IDs to errors — only auto-imports through the package crossing, never through a `ProjectReference`. Without this default, a workspace-mode sqlproj's custom rules silently never run even though the rules DLL sits correctly in `@(Analyzer)`.
2. **The Contains-guarded rule-promotion pattern as a documented extension point.** Norse ships no DacFx rules package; the canonical file documents the pattern with commented exemplar promotion lines, and the two reasons it exists: `TreatTSqlWarningsAsErrors` does *not* escalate DacFx static-analysis warnings (promotion tokens are the only error path), and un-guarded re-appends trip DacFx's SQL72039 duplicate-key failure.
3. **The stale-transitive strip, mirror polarity.** In workspace mode, a rules package can also arrive transitively through another database package's pinned dependency, shadowing the live local source with a stale packed copy. The strip is keyed on missing `MSBuildSourceProjectFile` — the same provenance doctrine as the Roslyn strip (§2.2), opposite polarity: the Roslyn strip removes unwanted *package* copies; this one removes the package copy *in favor of* the live `ProjectReference`. Generalized from the ancestor's hardcoded filename to the `Rules=`-declared names.

**The templates get a live exerciser on day one.** Canonicalizing subtle DacFx behavior with no consumer would repeat the exact dormant-gap shape this design closes in `gen/` — "no project has hit this" is not evidence a gap is closed. A **disposable verification fixture** (the field-proven verify-rule pattern: throwaway local feed, scratch `.sqlproj` fixtures) proves the canonical `schema/` templates end to end — both crossings, `RunSqlCodeAnalysis` enablement, rule promotion, `DatabaseSqlCmdVariable` passthrough, stale-transitive stripping — and runs as part of the §4 matrix. This is a test harness, not a consumer-facing worked example; the deferred reference realm from §3 stays deferred.

**The two-crossings polarity table gains the DacFx rows:** packed `build/*.targets` auto-import (package crossing only — general NuGet behavior, first exercised here by DacFx) and DacFx rule enablement (follows the packed targets, hence package-crossing-only until the canonical default above restores parity).

### 2.8 The sweep

1. `Glitnir/docs/the-runes.md` — the doctrine page (§2.1).
2. `the-two-crossings.md` deleted; referrers updated (Bifröst root targets comments, scattered templates via Ginnungagap, Bifröst CLAUDE.md, citing Glitnir docs).
3. `msbuild-deep-dive-docket.md` deleted — superseded by this executed project.
4. Boy-scout pairs updated in the same change as the machinery they describe: Bifröst README + CLAUDE.md (state of the union, §5 conventions pointer, §8/§9 trims), Ginnungagap pair (`schema` group, `Directory.Analyzers.props` convention), Midgard CLAUDE.md (NORSE080 block condition), Yggdrasil CLAUDE.md (collapsed stubs).
5. The public README narrative — the toggle story told as a differentiator at README altitude.

---

## 3. Alternatives Rejected

- **Patch-in-place (no hoist).** Adding the CPM-aware `Choose` to the three canonical group files where the fallback already lives, and the strip to `gen/`. Cheaper diff, but keeps three copies of the logic forever — each now three-way instead of one-way — and keeps Yggdrasil's variant files alive. The realm-root targets already exists as the single home for exactly this kind of law.
- **Scattering canonical group targets to Yggdrasil via a new group, with a `NorsePackable` opt-out seam.** Proposed and retracted in session: it invented an extension point in canonical law to serve one realm that deliberately inverts the model. The hoist makes it unnecessary — Yggdrasil already receives the realm-root targets (`dotnet` group), the CPM branch rides along, and its hand-owned variants collapse to trivial chain stubs with nothing left in them to drift. The exception in `manifest.psd1` stands; its surface area shrinks from ~50 duplicated lines to a few.
- **Consumer-declared analyzer metadata** (`<NorseRef Analyzers="...">` naming the producer's analyzers). Highest fidelity to the package crossing's closure-scoped delivery, but every consumer must know the producer's analyzer inventory — exactly the drift that let NORSE080 go invisible to Yggdrasil in the first place.
- **Hand-curated central analyzer list in Bifröst's root targets.** Simplest mechanically; but Bifröst becomes the file someone forgets to edit when a realm lands an analyzer — the same failure mode, one file over. The wildcard-imported manifest keeps the realm the authority on its own analyzer inventory.
- **Keeping `the-two-crossings.md` (deleted, absorbed into `the-runes.md`) standalone under an umbrella page.** Rejected for one-door navigation: the crossings are a chapter of the estate story, not a separate estate.
- **Analyzer scope metadata (`Global`/`Realm`/`ReferenceClosure`).** Proposed in external review; rejected as machinery for a problem with zero instances — all three current analyzers are diagnostics-only and no-op off-subject. The diagnostics-only law (§2.5) captures the kernel; the revisit trigger is named there.
- **A data-only manifest format with a generation layer.** Proposed in external review against the manifest-is-executable-MSBuild observation; rejected as a new machine serving a trust boundary the platform already stands inside (first-party, PR-reviewed, org-owned files). The §2.5 contract plus §4 assertions deliver the same guarantee without a DSL.
- **Carrying the ancestor estate's `Analyzer="true"` spelling forward.** It wasn't wrong there — it was the first reach when the first generators rolled out and it carried. In an estate that now has Roslyn analyzers, analyzer manifests, and `Generator="true"`, it no longer names a role; `Rules="true"` and the three-suffix taxonomy (§2.7) replace it. The ancestor keeps its own history.
- **Deferring the realm seam (YAGNI-strict).** The manifest amendment alone returns Midgard to canonical, and no second realm-specific targets law exists today — but the hazard class (realm law edited into a scattered file, silently reverted by an auto-approved scatter PR) is proven by a live near-miss, and the seam is one conditional-import line. Concrete near-miss beats hypothetical-future-need; the seam lands now.
- **Audit-only scatter (keep `Copy-Item -Force` semantics).** "Scattered = canonical, period" is philosophically clean, but an audit that runs beside an auto-approving overwrite is a warning nobody reads in time. The guard belongs in the copy loop, where the loss would happen.
- **A worked sqlproj example (Glitnir PoC or reference realm).** Deferred, not rejected — the mechanism is designed and canonicalized now; a live exerciser lands when a first SQL Server consumer is real.
- **Document-only (change nothing).** The docket's deliverable expectation was always consolidation *plus* closed gaps; leaving the `gen/` strip gap and the Yggdrasil drift surface open while documenting them would record debt instead of retiring it.

---

## 4. Consequences

Ordered change list — each step behind the usual ship gates, sequenced so no enforcement goes dark mid-transition (NORSE080 must have an active delivery path at every step):

1. **Manifests first — Svartálfheim and Midgard** land their `Directory.Analyzers.props` (Architecture.Analyzers + Primitives.Analyzers; Infrastructure.Web.Grpc.Analyzers). Realm-owned, never scattered, inert until something consumes them — safe to land ahead of everything.
2. **Bifröst root:** the wildcard aggregation import and generic attach block replace the two hand-authored platform blocks; `NorseRef` gains `Rules="true"` resolution; comment pointers move to `the-runes.md`. From this step, NORSE080 reaches *every* workspace compilation via Bifröst. Midgard's hand-authored block still exists and double-declares the same analyzer project — same path, so the compiler dedups; benign and bounded by step 4. **Verify NORSE080 fires in a Yggdrasil workspace compilation before proceeding** — the original gap becomes the transition canary at its most fragile point.
3. **Ginnungagap:** hoist fallback + strip into the canonical realm-root `Directory.Build.targets`, which also gains the own-manifest import + standalone attach block (§2.5) and the `Directory.Realm.targets` seam import (§2.6); shrink `src/`/`tests/`/`gen/` targets; move `IsPackable=true` to `src/Directory.Build.props` with the §2.3 comment pair; author `schema/Directory.Build.props` + `schema/Directory.Build.targets` and their verification fixture (§2.7); add the `schema` group (assigned to none) to `manifest.psd1`; add the divergence guard + audit mode to `scatter-the-runes.ps1` (§2.6); update the Ginnungagap README/CLAUDE.md pair.
4. **Scatter run — with the explicit Midgard override.** Midgard's realm-root targets is genuinely divergent; the new guard must refuse it by default and be overridden for exactly this one deliberate reversion, which deletes the hand-authored NORSE080 block and returns the file to byte-identical canonical. Its replacement paths are both already live: workspace via step 2, standalone via the canonical manifest-reading block + step 1's manifest. **Verify NORSE080 again immediately after** — workspace and standalone simulation both.
5. **Yggdrasil:** delete the three variant files' logic — `src/Directory.Build.targets` and `tests/Directory.Build.targets` collapse to chain stubs; `src/Directory.Build.props` untouched.
6. **Glitnir:** `the-runes.md` lands; `the-two-crossings.md` and the docket are deleted; citing docs updated.
7. **Bifröst docs:** README + CLAUDE.md pair updated.

Verification matrix, applied per step in the plan. Green builds are necessary, not sufficient — the platform's own harness culture (`Verify-Enforcement.ps1`) asserts evaluated state, and this matrix does the same via binlog or evaluated-project inspection:

- **Build modes:** workspace build (`dotnet build` from Bifröst), workspace package-mode build (`-p:UseProjectReferences=false`), standalone simulation (`-p:DirectoryBuildTargetsPath` probing where a real standalone checkout is impractical), CI green on at least one realm per changed template — and **every group-level template (`src`/`tests`/`gen`/`schema`) has a real exerciser**, the `schema/` fixture (§2.7) included.
- **Evaluated-state assertions:** exactly one intended `ProjectReference`/`PackageReference` emitted per `NorseRef` per mode; CPM-mode references carry no `Version` metadata; non-CPM standalone fallbacks carry the intentional stable wildcard; wanted generators remain while unwanted packaged copies are stripped; each analyzer attaches exactly once (no duplicates in `@(Analyzer)`); `IsPackable` and `OutputType` land at their intended final values under both plain and Web/Worker SDKs; manifest contract holds (§2.5 — item shape, in-realm existing `.csproj` paths, no duplicate paths or assembly names); taxonomy holds (§2.7 — `Rules=`/`Generator=` suffix agreement, no `Analyzer=` metadata anywhere).
- **Transition canaries:** NORSE080 fires in a Yggdrasil workspace compilation referencing Midgard by `NorseRef` at §4 steps 2 and 4 (the original gap, now the regression canary); no analyzer double-attaches in a Midgard-internal compilation after step 4.
- **Scatter:** the audit mode reports zero divergent build files across all twelve realms post-rollout; the guard hard-fails on a deliberately planted divergence before the override switch is honored; lineage-unavailable reports as its own outcome, not as divergence.
