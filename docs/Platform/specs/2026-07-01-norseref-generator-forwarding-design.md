# NorseRef Generator Forwarding — Design

**Status:** Decided and live (2026-07-01). Discovered while executing Task 9 of
`2026-06-28-migrations-framework-identity-schema.md` (Yggdrasil, wiring
`Hosting.Migrations.Service` to `Norse.EntityFramework.Migrations.PostgreSQL`'s
Roslyn generator).

**Why this doc exists:** the migrations framework is the first of what will be
many `*.Migrations`-style packages that ship a Roslyn `IIncrementalGenerator`
alongside a normal library. Every future bounded context that adds one will
hit the same two MSBuild gaps this doc fixes. Read this before wiring a new
generator-bearing NorseRef consumer — the fix is already in place platform-wide;
you should not need to re-derive it.

**Amendment (2026-07-25):** `Norse.EntityFramework.Migrations.PostgreSQL` (the worked example above and in "The Problem" below) named Urðarbrunnr's namespace as it stood on 2026-07-01. It has since widened to `Norse.Persistence.EntityFramework.*` (PR #31, tag v0.0.4). The forwarding mechanism this doc describes is namespace-agnostic and unaffected.

## The Problem

A source generator packaged inside a Norse library (e.g.
`Norse.EntityFramework.Migrations.PostgreSQL.Generator`, wired as an analyzer
inside its own wrapper project, `Norse.EntityFramework.Migrations.PostgreSQL`)
needs to run against the **final consuming project's** compilation — the one
that calls the generated extension method (e.g. `Hosting.Migrations.Service`,
which calls `builder.AddNorseMigrations()`). The generator walks
`compilation.SourceModule.ReferencedAssemblySymbols` to discover contributor
types across every referenced Norse assembly.

Two independent MSBuild forwarding gaps stood between "the generator is
referenced somewhere in the graph" and "the generator runs correctly exactly
once, in the right project":

### Gap 1 — dev mode (`UseProjectReferences=true`) doesn't forward analyzers through a ProjectReference chain

The `NorseRef` `Choose` block in `Bifrost/Directory.Build.targets` resolves a
`NorseRef` to a plain `<ProjectReference>` pointing at the **wrapper**
project, not the generator project. MSBuild does not transitively forward a
referenced project's own `OutputItemType="Analyzer"` items through a chain of
plain `ProjectReference`s — only the wrapper's own compilation sees its
analyzer; nothing downstream does.

**Fix:** `NorseRef` items can carry `<Generator>true</Generator>` metadata.
When set, the `Choose` block adds a second, analyzer-only `ProjectReference`
directly to `%(Repo)/src/%(Identity).Generator/%(Identity).Generator.csproj`
(`OutputItemType="Analyzer"`, `ReferenceOutputAssembly="false"`), by naming
convention: the generator project is always `{Package}.Generator`, sibling to
`{Package}` in the same realm's `src/`.

```xml
<NorseRef Include="EntityFramework.Migrations.PostgreSQL">
    <Repo>Urdarbrunnr</Repo>
    <Generator>true</Generator>
</NorseRef>
```

Only the **leaf** consuming project (the one that actually calls the
generated method) sets `Generator="true"`. Nothing else needs to.

### Gap 2 — a generator forwarded via NuGet also fires inside downstream projects that merely reference the leaf

In `UseProjectReferences=false` (real NuGet/PackageReference mode — what CI
and any standalone realm build actually run), NuGet's own analyzer-package
propagation is more aggressive than Gap 1's ProjectReference behavior: it
reaches **transitively** through the whole reference graph, including into
any project that has a plain `ProjectReference` to the leaf project (e.g. the
leaf's own `*.Tests` project). That downstream project's compilation also
satisfies the generator's discovery condition (it can see the same referenced
contributor assemblies), so the generator regenerates its own copy of the
extension method there too — colliding (`CS0121`, ambiguous call) with the
copy already compiled into the leaf project's `.dll`, which the downstream
project also sees via `InternalsVisibleTo`.

This is not specific to test projects — it will happen to **any** project
that references a `Generator="true"` leaf without itself being the intended
generator consumer.

**Fix:** a `Target`, `_NorseRemoveUnwantedGeneratorAnalyzers`
(`BeforeTargets="CoreCompile"`), strips any `@(Analyzer)` item matching the
`Norse.*.Generator` naming convention **unless** this project's own
`@(NorseRef)` items declare it with `Generator="true"`. Every other project —
tests, downstream consumers, whatever — gets the accidental duplicate
stripped automatically. No opt-in required; it's a no-op for projects that
never see a Norse generator analyzer in the first place.

**This target does NOT live in `Bifrost/Directory.Build.targets`.** It has to
fire in a genuinely standalone checkout too (no Bifrost ancestor at all —
exactly what CI runs), so it lives in the Ginnungagap-scattered
`src/Directory.Build.targets` / `tests/Directory.Build.targets` templates
themselves, unconditionally (outside the `Exists($(_BifrostTargets))` gate),
not behind the Bifrost import. `Bifrost/Directory.Build.targets` only carries
Gap 1's fix (the analyzer `ProjectReference` in the `Choose` block), since
Gap 1 is Bifrost-exclusive by construction — `UseProjectReferences=true` only
exists when Bifrost is present.

Two `Exclude`/matching approaches were tried and rejected before landing on
this one — worth knowing if you're re-deriving this:
- `Condition="'%(Analyzer.Filename)'.StartsWith(...)"` on a quoted-metadata
  string is not valid MSBuild condition grammar (`MSB4092`) — string instance
  methods need a real property-function expression, not string-literal
  concatenation.
- Matching the *wanted* set via `Exclude="@(_NorseWantedGeneratorAnalyzer)"`
  silently excludes nothing: `Exclude` compares full `ItemSpec` values, and
  `@(Analyzer)` items carry a full file path (`.../bin/Debug/.../Norse.X.Generator.dll`)
  while the wanted-set transform produces a bare name (`Norse.X.Generator`) —
  they never match, so the "wanted" analyzer gets stripped too, breaking the
  leaf project itself (`CS1061`: `AddNorseMigrations` no longer found).

```xml
<Target Name="_NorseRemoveUnwantedGeneratorAnalyzers" BeforeTargets="CoreCompile" Condition="'@(Analyzer)' != ''">
    <ItemGroup>
        <_NorseWantedGeneratorAnalyzer Include="@(NorseRef->WithMetadataValue('Generator', 'true')->'Norse.%(Identity).Generator')" />
    </ItemGroup>
    <PropertyGroup>
        <!-- Semicolon-joined so a per-Analyzer-item Condition can do a plain substring Contains check below. -->
        <_NorseWantedGeneratorAnalyzerNames>;@(_NorseWantedGeneratorAnalyzer);</_NorseWantedGeneratorAnalyzerNames>
    </PropertyGroup>
    <ItemGroup>
        <Analyzer Remove="@(Analyzer)" Condition="$([System.Text.RegularExpressions.Regex]::IsMatch('%(Analyzer.Filename)', '^Norse\..+\.Generator$')) and !$(_NorseWantedGeneratorAnalyzerNames.Contains(';%(Analyzer.Filename);'))" />
    </ItemGroup>
</Target>
```

This is a metadata `Condition` on an item element (`<Analyzer Remove=... Condition=...>`), which is only legal **inside a `Target`** — the same expression on a static top-level `ItemGroup` throws `MSB4191`/`MSB4190` (custom and built-in metadata references aren't allowed there; batching context doesn't exist outside a target's execution). This is also *why* the fix can't be a plain `ItemGroup` in the first place: `@(Analyzer)` items from `PackageReference`/`ProjectReference` resolution don't exist yet at static evaluation time either way — both problems point to the same `BeforeTargets="CoreCompile"` answer.

## A Third Gap, Prerequisite to the Fix Reaching `tests/` At All: No Import Up From `tests/`

Gap 1's fix lives in `Bifrost/Directory.Build.targets`; Gap 2's fix lives in
the scatter templates directly (previous section). Both still needed a way
to actually reach a test project's compilation, and none did. MSBuild only
auto-imports the **nearest** `Directory.Build.targets` walking up from a
project — for anything under `{Realm}/src/`, that's
`{Realm}/src/Directory.Build.targets`, which already imported Bifrost's root
file via `GetPathOfFileAbove` (this predates this doc — it's how `src/`
projects have always resolved `NorseRef` under Bifrost). `{Realm}/tests/`
had its own `Directory.Build.targets`, but it stopped at
`<OutputType>Exe</OutputType>` — it never imported anything further up, so
none of the `Choose` block or either fix above ever reached a test project.

**Fixed by mirroring `src/`'s import pattern into `tests/`:**

```xml
<Project>
	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<_BifrostTargets>
			$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../../'))
		</_BifrostTargets>
	</PropertyGroup>
	<Import Project="$(_BifrostTargets)" Condition="Exists('$(_BifrostTargets)')" />
	<ItemGroup Condition="!Exists('$(_BifrostTargets)')">
		<PackageReference Include="@(NorseRef->'Norse.%(Identity)')" Version="*" />
		<PackageReference Include="@(NorseDesignRef->'Norse.%(Identity)')" Version="*">
			<PrivateAssets>all</PrivateAssets>
		</PackageReference>
	</ItemGroup>
</Project>
```

Both `src/Directory.Build.targets` and `tests/Directory.Build.targets` are
**scatter-managed** — sourced from Ginnungagap
(`.github/config/src/Directory.Build.targets`,
`.github/config/tests/Directory.Build.targets`) and distributed by
`scatter-the-runes.ps1`. Both templates were updated in Ginnungagap on
2026-07-01: `tests/` gained the `_BifrostTargets` import (it never had one),
and both gained the `_NorseRemoveUnwantedGeneratorAnalyzers` target,
unconditionally, so it applies whether or not Bifrost is present.

**As of this doc, only Yggdrasil's checked-out copies have been hand-synced**
(to unblock Task 9) — the template change has not yet been scattered to the
other seven realms. Running the actual scatter across every realm is a
separate, larger, multi-repo action (creates a PR per repo) and needs its own
explicit go-ahead; don't assume it happened just because the source template
did.

### CPM realms are not byte-identical to the template — don't blind-copy

Yggdrasil is (as of this doc) the platform's one realm with
`ManagePackageVersionsCentrally=true` (`Directory.Packages.props`). Its
deployed `src/` and `tests/` `Directory.Build.targets` had **already**
diverged from the generic template in two ways the template doesn't account
for, both discovered by naively overwriting Yggdrasil's copies wholesale
during this fix and immediately regressing them:

1. **No `Version="*"` on the fallback `PackageReference` items.** Under CPM,
   a `PackageReference` must never carry a `Version` — only a matching
   `PackageVersion` item may (`NU1008` otherwise). The template's
   `Version="*"` is only correct for non-CPM realms (confirmed identical on
   Asgard and Midgard, which don't use CPM).
2. **No `<OutputType>Library</OutputType>` in `src/Directory.Build.targets`.**
   Yggdrasil's `src/` mixes library projects (`Hosting.Web.Components`) with
   executables (`Hosting.Worker`, `Hosting.Web.Client`,
   `Hosting.Migrations.Service`) — a blanket `OutputType` in `targets`
   (which evaluates *after* each project's own `PropertyGroup`) stomps every
   executable project's own setting (`CS8805`). The template's
   `OutputType=Library` is only correct for realms whose entire `src/` tree
   is libraries.

**If you ever need to re-sync a realm's checked-out copy against the
Ginnungagap template by hand (instead of running the real scatter script),
diff first — don't `cat template > realm/file`.** The real scatter tool
presumably already handles per-realm divergence correctly (or this is itself
a latent scatter-tooling gap worth a separate look); this doc only documents
what was hand-verified for Yggdrasil.

## How To Adopt This For a Future Generator-Bearing Package

1. Name the generator project `{Package}.Generator`, sibling to `{Package}`
   in the same realm's `src/`, matching the existing
   `EntityFramework.Migrations.PostgreSQL` / `EntityFramework.Migrations.PostgreSQL.Generator`
   pair.
2. Wire the generator as an analyzer inside `{Package}`'s own `.csproj`
   (`OutputItemType="Analyzer"`, `ReferenceOutputAssembly="false"`) — this
   makes the wrapper's own compilation and the NuGet package's `analyzers/`
   payload both correct, independent of anything in this doc.
3. In the **one** leaf project that actually calls the generated method, add
   `<Generator>true</Generator>` metadata to its `NorseRef Include="{Package}"`
   item. Nothing else changes on the consumer side — the `Choose` block and
   the strip `Target` handle both directions automatically.
4. If a realm's `tests/Directory.Build.targets` hasn't been re-scattered from
   the 2026-07-01 template yet, its test projects won't see the strip target
   and may hit `CS0121` if a test project references the leaf. Check the
   realm's `tests/Directory.Build.targets` for the `_BifrostTargets` import
   before assuming the fix is live there.

## Verification Performed

- `UseProjectReferences=true`, nested under Bifrost (dev loop):
  `dotnet build Yggdrasil/src/Hosting.Migrations.Service/...` — clean.
- `UseProjectReferences=false`, genuinely standalone checkout (rsync'd outside
  the Bifrost tree, packages restored from the live GitHub Packages feed —
  the same conditions as GitHub Actions' `runner` checkout):
  `dotnet test Yggdrasil.slnx -c Release --coverage ...` — `total: 6, failed: 0`.
- Both runs confirm the generator produces exactly one definition of
  `AddNorseMigrations()`, and it actually executes (registers the
  `norse_identity` Postgres context and all three migration contributors)
  without throwing.

## Still Open

- The scatter template changes (both `src/` and `tests/`) have not been
  propagated beyond Yggdrasil. Any other realm adding a generator-bearing
  `NorseRef` (or tests that reference a `Generator="true"` leaf project)
  before the real scatter run will need the same hand-sync Yggdrasil got
  here, or will hit `CS0121`.
- Whether `scatter-the-runes.ps1` already accounts for CPM-realm divergence
  (no `Version="*"`, no blanket `OutputType`) when it actually runs, or
  whether it would blindly overwrite a CPM realm's copy the same way this
  session's first attempt did, has not been checked. Worth confirming before
  the real scatter run touches Yggdrasil again, or before a second CPM realm
  exists.
