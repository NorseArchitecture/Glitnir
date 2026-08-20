# The Runes

*Doctrine. Peer to `the-two-unions.md`, named for what `scatter-the-runes.ps1` actually does:
carve law once, distribute it to every realm, read it everywhere. Governs the platform's entire
MSBuild estate — brand injection, the `NorseRef` two-crossings machinery, realm-root targets
chaining, evaluation-order law, analyzer/generator/DacFx-rule delivery, packaging and release,
the Ginnungagap scatter, and schema projects. Each piece landed in its own session with its own
spec; this page is the first time the whole thing is designed, and documented, as one system.
Absorbs `the-two-crossings.md` in full — that page is deleted, not stubbed.*

---

## 1. Why

A single property, `UseProjectReferences`, selects which crossing resolves every cross-realm
dependency inside the Bifröst workspace. `true` gives 100% local builds — the whole stack
developed across submodules without releasing anything until the end. `false` reproduces each
repository's CI dependency-resolution policy — build the current repo from source, resolve every
sibling from NuGet the way GitHub Actions does. A genuinely standalone checkout has no
project-reference mode by construction — no siblings on disk — so its fallback is the same
package crossing CI uses, which is the point: there is no third mode hiding behind "standalone,"
just the package crossing with no workspace to compare against.

That one toggle retires the classic submodule complaints in a single move: a stale `master`
pointer in Bifröst cannot affect shipping software, because shipping never reads Bifröst's
checkout — it reads what each realm published. Releases stay transparent — each realm publishes
its own version, tag → artifacts → release, no hidden coupling (ch. 6). And any CI-only failure
becomes locally reproducible by flipping one flag instead of waiting on a fresh pipeline run.

**One asymmetry is recorded, not incidental:** workspace mode runs realm law — analyzers a realm
ships for the whole platform to obey — globally, on every workspace compilation, regardless of
whether a project opted in. The package crossing only delivers that same law through the
reference closure, i.e. only to projects that actually depend on the shipping realm. This is
deliberate (ch. 5): fail earlier, fail locally; a realm analyzer that has nothing to say about a
given compilation no-ops. It means workspace mode is not merely "faster" than package mode — it
runs *more* law than package mode can, and that difference is documented rather than papered
over.

**Two inputs triggered writing this doctrine down, both dated 2026-08-06/07:** a docket
(`msbuild-deep-dive-docket.md`, now deleted — absorbed here) that accumulated the two-crossings
doctrine, the evaluation-order lessons proven live 2026-08-03, and two analyzer-forwarding bugs
bitten in the same shape a week apart (2026-08-04 Urðarbrunnr, 2026-08-06 Midgard PR #61); and a
prior production estate that contributed four proven walls from its own DacFx work (ch. 8). A
full sweep of the working tree the day this was ruled found the estate disciplined: across twelve
realms, only four files diverged from Ginnungagap's canonical templates, and every one was
deliberate — Midgard's realm-root targets (the NORSE080 forwarding gap this design closes) and
Yggdrasil's three CPM variants. The complexity this design reduces was never drift; it was
structural duplication inside the canonical templates themselves.

---

## 2. The layer map

Four layers, each with one job, chained top-down by a one-hop `Import` that walks up the
directory tree until it finds the next layer or finds nothing:

| Layer | File(s) | Who authors it | Job |
|---|---|---|---|
| **Bifröst root** | `Directory.Build.targets` | Hand-authored, this repo only | Owns both crossings' `Choose` (workspace `ProjectReference`/`Analyzer` resolution, including workspace *package* mode); wildcard-aggregates every realm's analyzer manifest |
| **Realm root** | `Directory.Build.targets` | Ginnungagap-scattered, canonical* | "Law that binds `src`, `tests`, `gen`, and `schema` exactly once" — NORSE070 `Using Remove`, the platform-analyzer `Choose`, the standalone `NorseRef` fallback, the standalone manifest-attach, the hoisted generator-strip target |
| **Group level** | `src`/`tests`/`gen`/`schema` `Directory.Build.props`+`.targets` | Ginnungagap-scattered, canonical | Thin chain stub plus that layer's own subject only — `OutputType`, `IsPackable`, the DacFx property set |
| **Project** | `{Project}.csproj` | Hand-authored, per project | Declares the actual dependency intent — `NorseRef`, `NorseGeneratorRef`, `NorseDesignRef` items |

Two more files sit beside the realm root, deliberately **never** scattered:

| File | Job |
|---|---|
| `Directory.Analyzers.props` | The realm's own analyzer manifest (ch. 5) — realm-owned, self-anchored, wildcard-imported by Bifröst |
| `Directory.Realm.targets` | The realm seam (ch. 7) — realm-specific targets law, additive only |

**The chain-or-fallback walk.** Every group-level and realm-root file computes its own
`_ParentTargets` via `$([MSBuild]::GetPathOfFileAbove(...))` and imports it if it exists — group
level hops one level to its realm root; the realm root hops one level to Bifröst's root. Both
hops reuse the identical property name, `_ParentTargets`, because each file only ever cares about
its own next hop, never about the whole chain. That reuse is harmless under real MSBuild
evaluation — it is not harmless under a global property override, which is exactly the shape of
harness bug ch. 9 documents. When no next layer exists — a genuinely standalone checkout, no
Bifröst ancestor on disk — the realm root's own `Choose` supplies `PackageReference` fallbacks
instead of importing anything further; Bifröst's own root file, by construction, can never itself
have a next layer to chain to.

**The ownership rule that makes the rest of this page make sense:** a scattered file is canonical
by definition. Nothing realm-specific is ever hand-edited into one — realm law lives in the
manifest or the seam, both unscattered (ch. 7 states this as a hard law, not a convention).

**\*The realm-root file's one exception (2026-08-08).** Every rule above holds for a realm that
*might* someday declare a `NorseRef` — which is every realm but two. Svartálfheim and Naglfar are
permanent architectural leaves: Svartálfheim is the platform's dependency-graph root (nothing sits
below it to reference), Naglfar's C# is entirely generated from Style Dictionary output (no
hand-authored surface to reference anything from). Neither will ever declare a `NorseRef`/
`NorseDesignRef`/`NorseGeneratorRef`, by design, not by current absence — which is what
distinguishes them from a realm like Ratatoskr, empty today only because its code hasn't landed
yet. For these two, and only these two, the realm-root file is realm-owned instead of scattered
(ch. 7 has the mechanism). Svartálfheim keeps a lean hand-written copy — the chain import, the
`Using Remove`, its own standalone analyzer self-check, the seam — with the `NorseRef` fallback,
the platform-analyzer `Choose`, and the generator-strip target all dropped, since none can ever
fire and the analyzer `Choose` was actively double-delivering `Norse.Architecture.Analyzers`
alongside Svartálfheim's own manifest `ProjectReference` (the bug that surfaced this whole
question). Naglfar has no realm-root file at all — an RCL with no hand-authored surface has
nothing for even the lean version to check.

**Both leaves keep the props chain import (2026-08-20 amendment).** The same 2026-08-08 ruling
also dropped the `_ParentProps` import from both leaves' realm-root `Directory.Build.props`, on
the reasoning that a permanent leaf never needs the workspace-vs-standalone crossing decision
`UseProjectReferences` carries. That was right about `UseProjectReferences` and wrong about the
file. Bifröst's root `Directory.Build.props` carries a **second, unrelated payload**: a
`PropertyGroup` conditioned on `NORSE_BUILD_ARTIFACTS_DIR` that redirects `BaseIntermediateOutputPath`
and `BaseOutputPath` out of the tree. It exists because this checkout is shared between build
environments — a devcontainer (`remoteEnv`) and a bare-metal Windows/Visual Studio build against
the same ReFS mount — and whichever side builds in-tree last leaves its own RID and absolute paths
baked into `obj/project.assets.json` for the other side to fail on (`Unable to find fallback
package folder 'C:\Program Files (x86)\...'`, seen from Linux reading a Windows-restored tree).
That payload applies to **every realm that compiles anything, leaf or not**, and the import is the
only thing carrying it down — so from 2026-08-08 until this amendment both leaves wrote `bin`/`obj`
in-tree regardless of the environment variable. It cannot be relocated to a targets file to dodge
the chain: `BaseIntermediateOutputPath` must be set before `Microsoft.Common.props` reads it, which
is props-time only. The import is restored in both realms; inheriting `UseProjectReferences` and
`NorseRefVersion` alongside it is inert by construction, since neither declares a `NorseRef`.

The general rule the miss cost us, worth stating plainly: **the realm-owned exception is about the
`NorseRef` machinery, never about the chain itself.** A leaf opts out of what the canonical file
*declares*; it does not opt out of being a realm. Before dropping any import on leaf-status grounds,
enumerate everything the parent file carries — not just the payload that motivated the question.

---

## 3. The two crossings

Bifröst's root `Directory.Build.targets` carries one `Choose` block. Every realm's `src/`/`tests/`
`Directory.Build.props` declares its cross-realm dependencies as `NorseRef` items —
`<NorseRef Include="Reference.Data.Primitives"><Repo>Mimisbrunnr</Repo></NorseRef>` and the
like — and the `Choose` decides, from a single property, what that item actually becomes at build
time:

```xml
<When Condition="'$(UseProjectReferences)' == 'true'">
	<ProjectReference Include="@(NorseRef->'$(MSBuildThisFileDirectory)%(Repo)/src/%(Identity)/%(Identity).csproj')" />
	...
</When>
<When Condition="'$(ManagePackageVersionsCentrally)' == 'true'">
	<PackageReference Include="@(NorseRef->'Norse.%(Identity)')" />
	...
</When>
<Otherwise>
	<PackageReference Include="@(NorseRef->'Norse.%(Identity)')" Version="$(NorseRefVersion)" />
	...
</Otherwise>
```

Same item. Same declared intent — "this project depends on that one." Two genuinely different
mechanisms answer it, and which one answers is a single boolean the entire build reasons from.
(The real file today also resolves `Rules="true"` and `Generator="true"` metadata into
analyzer-shaped references, and carries `NorseGeneratorRef`/`NorseDesignRef` — ch. 5 and ch. 8
cover those branches; this excerpt is the doctrine shape, not a transcription.)

### The polarity

| | `ProjectReference` — local dev | `PackageReference` — CI / package mode |
|---|---|---|
| **Faces** | The working tree. Every sibling realm's `src/` sitting on disk right now. | The published record. Whatever NuGet actually shipped, pinned by version. |
| **Resolves to** | The sibling's real source, compiled fresh, every build. | A specific artifact, frozen at pack time, identified by SemVer. |
| **Cost of a change** | Zero. Edit Svartálfheim, rebuild Mímisbrunnr, the new code is just *there* — no publish, no version bump, no wait. | A real ship gate: PR merged, CI green, tagged, `dotnet pack`, published. The dependency only moves when someone pays that price. |
| **What it guarantees** | Nothing about isolation. A realm can accidentally lean on a sibling's unshipped, half-finished internals and nobody will know until release. | Realm isolation, for real. You cannot depend on code that hasn't shipped — the package doesn't exist yet, so the reference fails loudly at restore. |
| **What it's for** | Fast iteration across a realm boundary — feature work and debugging that legitimately spans two repos, without ceremony standing in the way. | Proving the platform's actual claim: that realms are independently releasable, independently versioned, and safe to depend on without reading their source. |
| **Analyzer/generator forwarding** | MSBuild never forwards analyzers transitively through `ProjectReference` — a sibling's `gen/` generator only runs where it's explicitly wired. No accidental flow to clean up. | NuGet's package-analyzer propagation reaches transitively through the whole reference graph — a generator can arrive somewhere it was never meant to run, and something has to strip it back out (ch. 9). |
| **`build/*.targets` auto-import** | Never — packed build logic never travels through a project reference, at all. General MSBuild/NuGet behavior, not Norse-specific. | Auto-imports automatically — ordinary NuGet packaging convention, first exercised on this platform by DacFx rules (ch. 8). |
| **DacFx rule enablement** | Off by default. The packed `build/*.targets` that flips `RunSqlCodeAnalysis` and promotes rule IDs to errors never arrives through a `ProjectReference`; the canonical `schema/Directory.Build.targets` restores parity with its own `UseProjectReferences=true` default (ch. 8). | On by construction — rides the packed `build/*.targets` from the row above. |

Both branches answer the same question — "does this compile against that" — with opposite
honesty. `ProjectReference` is honest about *velocity*: nothing stands between an edit and its
consumer seeing it, which is exactly what makes cross-realm feature work and live debugging
tractable instead of a publish-and-pray loop. `PackageReference` is honest about *boundary*:
nothing crosses it that wasn't actually shipped, which is exactly what makes CI's realm isolation
a claim you can trust instead of one you have to hope holds.

### Why this is a platform doctrine and not a convenience

The value isn't either branch alone — plenty of monorepos give you fast local iteration, plenty
of polyrepos give you real package boundaries. The value is that **the same declaration means
both, and which one is live is one flag**, so the two modes never drift into two different
dependency graphs that happen to usually agree. There's exactly one `NorseRef` graph. It just has
two lenses.

That's what makes the triage story real, not aspirational: a CI failure that only reproduces
under real package resolution — a version actually pinned lower than what local `master`
currently has, a package that forwards something a `ProjectReference` never would — gets
reproduced locally by flipping `UseProjectReferences` to `false` and rebuilding. No waiting on a
fresh pipeline run to iterate on a hypothesis. No maintaining a second, hand-synced local repro
harness. The same graph, the other lens, on the same machine, in the time it takes to rebuild.

And the inverse holds too: a whole feature that spans Svartálfheim, Mímisbrunnr, and Yggdrasil can
be built and debugged as one continuous edit-compile-run loop across three repositories, with the
CI-facing guarantee — that none of it can silently depend on unshipped internals — enforced the
moment the same code runs in package mode, without anyone having to remember to check by hand.

### Consequences for designers

- **Never reason about a `NorseRef` consumer as if it only has one shape.** Anything that
  inspects, filters, or forwards an item derived from `NorseRef` — an analyzer, a build target, a
  future source generator reading project metadata — has to ask which crossing it's looking at,
  not just what the item is named.
- **A bug that's dormant in one crossing isn't fixed.** "No project has hit this" is not evidence
  a gap is closed — it's evidence nobody's found the first exerciser (the Task 7 postmortem, ch.
  9, is the platform's own lesson in this).
- **The `ProjectReference` branch is not the sandbox and the `PackageReference` branch is not the
  real one.** Both are real. Local dev optimizes for velocity across a boundary that CI optimizes
  for proving; neither lens is more "true" than the other, and a fix that only makes sense under
  one of them is incomplete.
- **When something behaves differently in CI than it does locally, ask which crossing changed
  first** — before suspecting the code. The flag exists precisely so that question has a fast,
  local answer.

> Two lenses, one graph, and the platform's own build law had to learn to ask which lens it was
> holding before it could tell a real leak from an honest self-reference.

---

## 4. Evaluation-order law

MSBuild evaluates in passes — properties, then items, then targets — and imports resolve at a
specific point in that sequence. Getting this wrong doesn't warn; it silently misfires. Four
lessons, each proven live, not theorized:

1. **CPM is invisible at props time.** `ManagePackageVersionsCentrally` is defined by
   `Directory.Packages.props`, which imports *after* `Directory.Build.props` — a props-level
   `Choose` misfires the versioned branch under CPM (`NU1008`; proven live 2026-08-03). Every
   delivery `Choose` on this platform lives at targets evaluation time for exactly this reason.
2. **SDK implicit usings exist only at targets time.** A props-level `<Using Remove>` precedes
   the SDK's `Include` and removes nothing — the banned `System.Net.Http.Json` global using
   (NORSE070) has to be removed from the realm-root `Directory.Build.targets`, not props, or the
   SDK re-adds it after the removal already ran (proven live 2026-08-03).
3. **`<Import>` resolves during the properties pass — before items exist.** An import path can
   never be derived from item lists, no matter the syntax; every item-to-property conversion
   inside an `Import` evaluates to literal unexpanded text (proven exhaustively in the prior
   production estate, which burned a session confirming it). File-glob **wildcards** in `Import`
   are fine — they are path expansion, not item expansion — which is exactly what makes the
   analyzer manifest's aggregation legal:
   ```xml
   <Import Project="$(MSBuildThisFileDirectory)*/Directory.Analyzers.props" />
   ```
   A wildcard that matches nothing imports nothing; a realm landing its first analyzer edits only
   its own repo.
4. **Conditioned vs. unconditional SDK defaults are not interchangeable.** Verified against the
   installed SDKs (net11 preview, confirmed identical in SDK source): both the Web and Worker
   SDKs set `IsPackable`'s `false` default **conditioned on empty**
   (`<IsPackable Condition="'$(IsPackable)' == ''">false</IsPackable>`) — a props-level `true`
   survives, because `Directory.Build.props` evaluates before the SDK's own props and the SDK's
   condition sees non-empty and skips. Both SDKs set `OutputType` to `Exe` **unconditionally** —
   a props-level `Library` is stomped regardless; only targets-time placement wins. The two
   properties sit in adjacent lines of the same SDK file, one conditioned, one not — the platform's
   own placement follows the asymmetry rather than fighting it: `IsPackable=true` lives in
   `src/Directory.Build.props`, `OutputType=Library` lives in `src/Directory.Build.targets` with a
   comment stating the real reason it can't move. **The law:** a conditioned SDK default yields to
   props; an unconditional one yields only to targets.

---

## 5. Delivery matrix

Three kinds of thing cross a realm boundary through `NorseRef` — a compile reference, a Roslyn
generator, or a DacFx rule library — and each crosses differently depending on which lens is live:

| Delivered thing | Project-reference crossing | Package crossing |
|---|---|---|
| Compile reference (`NorseRef`) | `ProjectReference` into sibling `src/` | `PackageReference Norse.*` |
| Roslyn generator (`Generator="true"`) | explicit analyzer-shaped `ProjectReference` — never transitive | packed `analyzers/dotnet/cs`, propagates the whole closure (strip target reins it in) |
| Roslyn diagnostics (realm law, `.Analyzers`) | manifest-attached everywhere (workspace) / realm-internal (standalone) | packed into the owning library, propagates the closure |
| DacFx rules (`Rules="true"`) | analyzer-shaped `ProjectReference` + canonical `RunSqlCodeAnalysis` default + hardcoded promotion | packed `build/*.targets` self-enables and promotes |
| Packed `build/*.targets` logic | **never imported** | auto-imported |

### The provenance-strip doctrine — both polarities

The package crossing's transitive propagation is a feature for ordinary libraries and a liability
for analyzers and generators: something can arrive somewhere it was never meant to run.
Provenance metadata on `@(Analyzer)` items makes the two crossings distinguishable at the item
level — NuGet-delivered analyzers carry `NuGetPackageId`; `ProjectReference`-delivered analyzers
carry `MSBuildSourceProjectFile`; SDK built-ins carry neither. Two strip targets key off this,
in opposite directions:

- **The Roslyn strip** (`_NorseRemoveUnwantedGeneratorAnalyzers`, hoisted to the realm root so it
  now covers `gen/` as well as `src/`/`tests/`) removes an *unwanted package copy* — an item with
  `NuGetPackageId` set, a filename matching `^Norse\..+\.Generator$`, and no matching entry in the
  consumer's own `NorseRef Generator="true"` / `NorseGeneratorRef` allow-list.
- **The DacFx strip** (`_NorseRemoveStaleTransitiveRules`, ch. 8) removes the opposite thing: a
  *stale packed copy* that arrived transitively through another database package's pinned
  dependency, shadowing the live `ProjectReference` copy. Keyed on the item having **no**
  `MSBuildSourceProjectFile` and a filename among the project's own `Rules="true"`-declared names.

Same provenance metadata, mirror-image conditions — one strip clears an unwanted package arrival
in favor of nothing (the analyzer shouldn't run here at all); the other clears a stale package
arrival in favor of the live local source.

### The manifest mechanism

A realm that ships public analyzers declares them once, at its own root, in
`Directory.Analyzers.props` — brand-free, riding the `Directory.*` family idiom, self-anchored so
a realm whose analyzers live outside `gen/` is equally expressible:

```xml
<Project>
	<ItemGroup>
		<NorseRealmAnalyzer Include="$(MSBuildThisFileDirectory)gen/Infrastructure.Web.Grpc.Analyzers/Infrastructure.Web.Grpc.Analyzers.csproj" />
	</ItemGroup>
</Project>
```

Bifröst's root `Directory.Build.targets` wildcard-imports every realm's manifest (ch. 4's wildcard
lesson). In workspace mode, one generic block attaches every declared analyzer to every
compilation, excluding three things: the analyzer project itself, any other manifest-declared
analyzer (analyzers don't analyze each other), and any Aspire AppHost (orchestration wiring, not
shipped code). In standalone mode, the realm root imports its *own* sibling manifest and attaches
realm-internally, gated on Bifröst's absence — the two attach sites are condition-disjoint by
construction, so nothing double-attaches by luck of deduplication.

**Identity and exclusion are design, not mechanics.** Nothing enforces project-name uniqueness
across realms, so a bare-name comparison could exclude an innocent stranger, and Roslyn resolves
duplicate analyzer *assembly names* by loader luck. Analyzer identity is **normalized
`MSBuildProjectFullPath`**, matched via the `WithMetadataValue` item-function transform — not a
raw `Condition` on the `Include`, which is illegal MSBuild outside a `<Target>` (ch. 9's
MSB4190/4191 story). Exclusions compare full paths, never bare names; duplicate analyzer project
paths and duplicate analyzer assembly filenames are convicted as build-verification failures, not
review hopes.

**Manifest analyzers are diagnostics-only — never source-emitting.** A generator arriving by
manifest would emit into every workspace compilation unconditionally, so generators stay
consumer-declared (`Generator="true"`) by structural enforcement, not convention. Scope metadata
(`Global`/`Realm`/`ReferenceClosure`) is deliberately absent — every analyzer on the platform today
is diagnostics-only and no-ops off-subject. The revisit trigger is named, not vague: the first
analyzer that genuinely cannot attach globally.

### The three-suffix taxonomy

`Rules="true"` metadata resolves to an analyzer-shaped `ProjectReference` into `src/` — DacFx
rules are ordinary `src/` libraries, unlike Roslyn generators' `gen/` convention. The ancestor
estate's `Analyzer="true"` spelling is deliberately **not** carried forward: in an estate that
already has Roslyn analyzers, analyzer manifests, and `Generator="true"`, `Analyzer=` no longer
names a role.

| Project suffix | Metadata | What it is | How it arrives |
|---|---|---|---|
| `.Generator` | `Generator="true"` | Roslyn source emission, `gen/` | Consumer-declared `NorseRef` |
| `.Rules` | `Rules="true"` | DacFx schema diagnostics, `src/` | Consumer-declared `NorseRef` |
| `.Analyzers` | — (never on a `NorseRef`) | Roslyn diagnostics — realm law | Manifest-attached, no opt-in |

Metadata, filename, and package name rhyme by design, and the taxonomy is machine-checkable: a
`Rules="true"` ref resolving to a non-`.Rules` project, a `Generator="true"` ref to a
non-`.Generator` project, or `Analyzer=` metadata appearing anywhere in the estate is a
convictable violation. `.Analyzers` deliberately has no metadata spelling — realm law arrives by
manifest or package closure, never consumer opt-in; the absence is the enforcement.

---

## 6. Packaging and release

Every realm publishes as a versioned NuGet package: MinVer derives the version from git tags (no
hand-maintained version file to drift from the tag that actually shipped); a library that bundles
a Roslyn generator packs it into `analyzers/dotnet/cs` via `IncludeGeneratorInPackage`; README and
LICENSE ride into the package alongside the assembly. The release path is uniform across every
realm: PR merged, CI green, tag pushed, `dotnet pack`, GitHub release published — the same ship
gate the two-crossings polarity table's "cost of a change" row describes for the package crossing.

**The version-ownership asymmetry is deliberate, and now recorded** (previously tribal): library
realms float their own `NorseRef` fallbacks (`Version="*"` / `*-*`) because they do not own the
deployed dependency closure — their packages are ingredients, not deployments. Yggdrasil, the
composition root, owns the closure through Central Package Management and pins explicit versions
in `Directory.Packages.props`. That's what lets a Sev1 dependency update be made and shipped
**from the composition root** without republishing a single intermediate realm — the fix lands in
one file, one repo, one release, and every consumer picks it up on their next restore. A future
reviewer reading a floating library fallback is looking at the ownership boundary, not an omitted
reproducibility mechanism.

---

## 7. The scatter

Ginnungagap's `scatter-the-runes.ps1` fans canonical config files out to every realm as
auto-merging PRs, grouped by `manifest.psd1`'s `Groups`: `git`, `universal`, `sdk`, `dotnet`,
`msbuild`, `nuget`, `tests`, `schema`, `ci`, `release`, `workflows`, `claude`. `DefaultGroups`
excludes `git` (the reduced set for git-hygiene-only realms) and `schema` (opt-in only — a repo
joins the day it grows its first `{Realm}/schema/{Name}.Database` project, ch. 8).

**The canonicity law.** *A scattered file is canonical by definition — realm-owned law never
edits one.* Realm-specific declarations live in the analyzer manifest (`Directory.Analyzers.props`,
ch. 5) or the realm seam (below) — both unscattered, both realm-owned, neither ever touched by
`scatter-the-runes.ps1`.

**The realm-root exception, and why it's a group split, not a special case in the engine
(2026-08-08).** `Directory.Build.targets` used to ride in `dotnet` alongside `Directory.Build.props`
and `{Realm}.sln.DotSettings` — two files every realm genuinely needs regardless of leaf-status.
Splitting it into its own group, `msbuild`, meant Svartálfheim and Naglfar (ch. 2) could opt out of
*just* the one file via the existing per-realm `Exceptions.Groups` mechanism — the same mechanism
Yggdrasil already uses to opt out of `nuget` — with zero new plumbing in `scatter-the-runes.ps1` or
`Get-RuneClassification`. `DefaultGroups` carries `msbuild`; Svartálfheim's `Exceptions` entry is
`DefaultGroups` minus `msbuild` (its first-ever entry — previously a fully default realm); Naglfar's
existing entry simply never listed `msbuild` to begin with. Once a file isn't in a realm's group
set, neither the scatter's copy loop nor its divergence guard ever look at it for that realm — its
total absence (Naglfar) or realm-owned content (Svartálfheim) is the expected, correct state, not
something the audit flags.

**The realm seam.** The canonical realm-root `Directory.Build.targets` ends with a conditional
import of `Directory.Realm.targets` — realm-owned, unscattered, **additive only**: it may declare
new items and new realm-owned targets; redefining a canonical property or target is a violation of
the canonicity law, not a supported extension point. End-of-file placement is deliberate — realm
additions may *react to* canonical state, which is not the same as mutating it. The seam exists
because of a concrete near-miss, not a hypothetical: `scatter-the-runes.ps1` used to
`Copy-Item -Force` every file unconditionally, and scatter PRs auto-merge, so Midgard's genuine
NORSE080-forwarding divergence in its realm-root file (PR #61, 2026-08-06) would have been
silently reverted by the next scatter run — the fix that closed a real bug would have vanished
without anyone noticing until the bug came back.

**The scatter guard + audit.** `scatter-the-runes.ps1` now classifies every destination file
against the canonical file's full git lineage (`Get-RuneClassification`, in
`scripts/lib/rune-lineage.ps1`) before overwriting it, into four states:

- **`Current`** — matches the canonical file's `HEAD` blob exactly. Nothing to do.
- **`Stale`** — matches some historical blob of the canonical path, just not the latest.
  Overwritten safely — this is the ordinary "hasn't synced yet" case.
- **`Divergent`** — does not match *any* historical blob of the canonical path's lineage. Hard
  fail, per realm, listing the files — overridable only via an explicit `-AcceptDivergence` token
  (`{Realm}/{File}`), for a deliberate, named reversion.
- **`LineageUnavailable`** — the check cannot answer at all: a shallow clone, or no history for
  the path. Its **own** hard-fail outcome, never conflated with `Divergent` and never silently
  passed. This is why the scatter workflow's own checkout runs `fetch-depth: 0` — a shallow
  Ginnungagap checkout would blind the lineage check entirely.

`-Audit` runs the same classification and reports it, then stops before either hard-fail check —
read-only, touches nothing. A renamed canonical path starts a new lineage from scratch. Silent
reversion of realm law is now structurally impossible — the guard sits in the copy loop itself,
not in a report nobody reads in time.

---

## 8. Schema projects

**New canonical templates, new opt-in group.** `schema/Directory.Build.props` and
`schema/Directory.Build.targets` land in Ginnungagap as a new `schema` scatter group, assigned to
no realm by default (ch. 7). A repo opts in the day it grows its first
`{Realm}/schema/{Name}.Database` `Microsoft.Build.Sql` project.

`schema/Directory.Build.props` carries the chain import (the same one-hop walk as `src/`), `DSP`
and `ModelCollation`, `TreatTSqlWarningsAsErrors=True`, the Microsoft rule suppressions that
conflict with platform law (`SR0016`/`SR0011`/`SR0009`), and the same one-edit brand injection as
every other project — `Norse.$(MSBuildProjectName)` as `SqlTargetName`/`PackageId`.

`NorseRef` extends with almost no new machinery: MSBuild transforms preserve custom metadata for
free, so a `<NorseRef DatabaseSqlCmdVariable="ref_db">` carries its SqlCmd wiring into both
crossings — workspace `ProjectReference` and package-mode dacpac `PackageReference` alike — with
zero new resolution logic. `Rules="true"` slots into the same taxonomy as ch. 5.

### Three walls, three canonical fixes

Each field-proven in the prior production estate, now canonical law in `schema/Directory.Build.targets`:

1. **`RunSqlCodeAnalysis=true` under `UseProjectReferences=true`.** A rules package's packed
   `build/*.targets` — the thing that actually enables analysis and promotes rule IDs to errors —
   only auto-imports through the package crossing, never through a `ProjectReference`. Without
   this default, a workspace-mode sqlproj's custom rules silently never run, even with the rules
   DLL sitting correctly in `@(Analyzer)`.
2. **The Contains-guarded rule-promotion pattern, documented as an extension point.** Norse ships
   no DacFx rules package itself, so the canonical file carries the pattern commented, with an
   exemplar promotion line, for the reasons the pattern exists at all: `TreatTSqlWarningsAsErrors`
   does **not** escalate DacFx static-analysis warnings — rule *promotion* (`+!RuleId`) is the
   only error path — and an un-guarded re-append trips DacFx's `SQL72039` duplicate-key failure.
3. **The stale-transitive strip, mirror polarity of the Roslyn strip** (ch. 5). In workspace mode,
   a rules package can arrive transitively through another database package's pinned dependency,
   shadowing the live local source with a stale packed copy. Keyed on the same provenance
   doctrine, opposite direction: the Roslyn strip removes an unwanted *package* copy; this one
   removes the package copy *in favor of* the live `ProjectReference`.

**The platform-rejection boundary stands unchanged.** DacFx remains rejected for the platform's
own persistence — EF Core migrations plus Postgres constraints is still the ruling (Glitnir's
`CLAUDE.md` Key Rejections table). This chapter is the consumer-facing story for a bridge that
chose SQL Server, not a reversal.

**The templates get a live exerciser on day one.** Canonicalizing subtle DacFx behavior with no
consumer would repeat the exact dormant-gap shape this design closed in `gen/` (ch. 5) — "no
project has hit this" is not evidence a gap is closed. A disposable verification fixture
(`scripts/verify-schema-templates.ps1` in Ginnungagap, a throwaway `Scratch.Rules`/
`Scratch.Database` pair) proves the canonical templates end to end: both crossings, the
`RunSqlCodeAnalysis` default, rule promotion, `DatabaseSqlCmdVariable` passthrough, and the
stale-transitive strip — all four as one harness, packing the fixture's own rules library into a
throwaway local NuGet feed to prove the package crossing for real, not just evaluated-state. This
is a test harness, not a consumer-facing worked example; a real reference realm stays deferred
until a first SQL Server consumer exists.

---

## 9. Postmortems and probes

*A doctrine that only shows its good side isn't earning the "peer to the two unions" claim. Two
real incidents, and the probing techniques that came out of chasing them.*

### The Task 7 strip incident (2026-07-31 → 2026-08-01)

- **Believed:** the generator-forwarding strip target
  (`_NorseRemoveUnwantedGeneratorAnalyzers`) fully accounted for every shape a `NorseRef`-wired
  generator could arrive in. Its allow-list — a project may only run a generator it explicitly
  declared via `NorseRef Generator="true"` — was believed complete.
- **Wrong because:** no allowance existed for a project consuming its *own* sibling `gen/`
  generator — the `src/X` + `gen/X.Generator` pairing, wired with a raw
  `<ProjectReference OutputItemType="Analyzer" ReferenceOutputAssembly="false">` rather than a
  cross-repo `NorseRef`. `NorseRef Generator="true"`'s allow-list has no way to represent
  *self*-reference — adding one would resolve to the project referencing itself, `MSB4006`, a
  circular target graph. The strip's original condition matched on filename alone
  (`^Norse\..+\.Generator$`), with no regard for *how* the analyzer item arrived —
  package-forwarded and project-forwarded looked identical to it, and only one of those two
  crossings was ever the actual problem.
- **Surfaced when:** Mímir's `Reference.Contracts` became the platform's first case where a
  project's own compilation genuinely depended on its own generator's output. Two earlier
  projects — Asgard's `Abstractions.Web.Server` and Urðarbrunnr's `Persistence.EntityFramework` —
  carried the identical dormant shape the whole time; neither ever tripped the bug because
  neither had anything to generate against its *own* compilation yet. The gap was real and
  platform-wide from the moment the strip target shipped — it just had no live exerciser.
- **Correction:** the strip condition gained one conjunct —
  `'%(Analyzer.NuGetPackageId)' != ''` — scoping it to NuGet-delivered analyzers only. MSBuild
  never forwards analyzers transitively through `ProjectReference` (ch. 3's polarity table), so a
  `ProjectReference`-wired analyzer was never a strip candidate in the first place; only the
  package crossing can leak a generator somewhere it wasn't meant to run. Provenance metadata
  (ch. 5) makes the two crossings distinguishable at the item level, which is the fix's whole
  argument.
- **A second, unrelated-looking symptom dissolved under the same fix, not worked around:**
  re-adding the stripped analyzer as a standalone experiment failed for a reason that had
  nothing to do with the strip logic — the scattered `Directory.Build.targets` imports *after*
  `CustomAfterMicrosoftCommonTargets`, so an injected target redefinition silently loses, and an
  `AfterTargets` hook on a not-yet-defined target is silently ignored. No multi-pass compiler
  behavior was involved, despite early diagnostics suggesting otherwise. Verified with **zero
  repo edits** via `-p:DirectoryBuildTargetsPath` global-property injection — pointing MSBuild's
  own built-in property at an alternate file directly, without mutating anything on disk. Worth
  keeping on hand for the next time a fix needs verifying without committing a hypothesis first.
- **Lesson:** the strip target's entire job is to reason correctly about which of the two
  crossings produced a given item. A filename-only match was never enough, because filename
  can't tell you which lens built the graph. Full record:
  `Platform/specs/2026-07-31-norseref-strip-provenance-scoping-design.md`.

### The MSB4190/4191 illegal-Condition-on-Item defect class (this consolidation, hit three times)

`Condition="'%(FullPath)' == '$(MSBuildProjectFullPath)'"` applied directly to an item's `Include`
outside a `<Target>` is not legal MSBuild, regardless of what the item list contains — built-in
metadata in that position raises `MSB4190`, custom metadata raises `MSB4191`. This is a hard
parser rejection, not a project-specific fluke, and it surfaced identically in two separate
canonical files during this consolidation (Bifröst's own root `Directory.Build.targets`, then the
Ginnungagap realm-root template) because both needed the same "exclude self from this item list"
shape for the analyzer manifest attach (ch. 5). The fix is the same idiom this file already used
one line above for `NorseRef->WithMetadataValue('Rules', '')`: a metadata **transform** evaluated
as part of the `Include` expression, not a raw `Condition` batching over it —
`@(NorseRealmAnalyzer->WithMetadataValue('FullPath', '$(MSBuildProjectFullPath)'))` instead of
`@(NorseRealmAnalyzer)` with `Condition="'%(FullPath)' == '...'"`. Once vetted the first time, the
identical substitution applied the second time without re-litigating — a defect class, not a
one-off, and the same fix closes it everywhere it appears.

### The standalone-simulation probe, two generations

Verifying "what does a genuinely standalone checkout see" without an actual standalone checkout
on hand needs a way to make `Exists('$(_ParentTargets)')` false for real:

1. **First attempt: `-p:_ParentTargets=__standalone__` on the command line.** This looked right —
   it makes the realm-root file's own existence check fail — but a command-line `-p:` value
   becomes a **global** MSBuild property for the whole evaluation, and global properties cannot
   be reassigned by any in-project `<PropertyGroup>` in the import chain. The group-level file's
   own, identically-named `_ParentTargets` hop (ch. 2's chain-or-fallback walk) got overridden
   too — so the group-level `Import` that would otherwise pull in the realm-root file never
   fired, the realm-root file's fallback `Choose` never even got a chance to run, and the
   resulting empty item list made assertions built on it **pass vacuously** rather than genuinely
   verify anything.
2. **Replacement: physical move-aside.** `Verify-Runes.ps1`'s `Invoke-WithBifrostRootHidden`
   `Move-Item`s Bifröst's own root `Directory.Build.targets` aside for the duration of a
   scriptblock, in a `try`/`finally`, and restores it after — "no Bifröst ancestor" becomes
   literally true on disk instead of simulated via a property that collides with real chain
   plumbing. Slower than a `-p:` flag, but it exercises the actual `Exists()` check the shipped
   mechanism relies on, not a stand-in for it.

The lesson generalizes past this one script: a probing technique that overrides a property shared
by more than one file in an import chain can silently break a hop it wasn't aimed at. When the
thing under test *is* the chain itself, prefer a physical condition over a global-property
shortcut.

### The throwaway-local-feed harness pattern

Proving the package crossing's packaging behavior — that a packed `build/*.targets` actually
auto-imports and actually enables `RunSqlCodeAnalysis` — cannot be done by inspecting evaluated
items alone; packed targets only exist after a real NuGet package acquisition. Both the schema
fixture (ch. 8) and the DacFx work it generalizes from prove this by packing a throwaway rules
library into a scratch local NuGet feed, adding a scoped `nuget.config` source for the duration of
the run, running a real restore and build against it, and tearing the feed down in a `finally`
block. A canonical-file regression this way shows up as a genuine restore or build failure, not a
false green from an evaluation-only check that never actually asked NuGet to do anything.

### `Verify-Runes.ps1` as the doctrine's own executable ledger

The mechanism this page describes is not a narrative running parallel to the code — it is exactly
what `scripts/Verify-Runes.ps1`'s four phases (`baseline` → `post-aggregation` → `post-hoist` →
`final`) assert against real evaluated MSBuild state: platform law attaching, realm law crossing
realms, the standalone fallback emitting the right package shape, no analyzer double-attaching. A
future change to any mechanism this page documents should turn one of those assertions red before
it turns this page's prose wrong.
