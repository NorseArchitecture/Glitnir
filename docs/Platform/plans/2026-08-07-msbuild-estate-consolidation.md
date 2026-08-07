# MSBuild Estate Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (default) or superpowers:executing-plans (narrow separate-session fallback) to implement this plan task-by-task, paired with superpowers:test-driven-development on every task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute `../specs/2026-08-07-msbuild-estate-consolidation-design.md` — hoist the NorseRef fallback and strip target into the realm-root targets, replace hand-authored analyzer blocks with realm manifests, add the scatter divergence guard, canonicalize `schema/` (dacpac) templates with a live fixture, and land `the-runes.md` as the one doctrine page.

**Architecture:** All build-law edits are proven locally first — every realm is checked out under Bifröst, so canonical template changes are copied into realm working trees by hand (exactly what scatter will later do) and verified by an evaluated-state harness (`scripts/Verify-Runes.ps1`) before anything ships. The spec's §4 transition-safe ordering governs the *ship runbook* (Task 13), not the local edit order.

**Tech Stack:** MSBuild (.NET 11 preview 6 SDK), PowerShell 7 (`pwsh`), Microsoft.Build.Sql latest stable on the DacFx `170.*` train (fixture only — resolve the exact version at implementation time), git.

## Global Constraints

- **No automatic git commits — ever.** Every task ends by staging (`git add`) in the affected repo and stopping. The human commits. This overrides the commit steps the planning skill would normally emit.
- **Bifröst and Glitnir stay on `master`.** Realm repos (Svartalfheim, Midgard, Yggdrasil, `.github`): if a feature fork is already open, add commits there (never stack a branch on a branch); otherwise the human creates `feature/msbuild-estate` — the implementer only stages.
- **Tabs in all `.props`/`.targets`/`.csproj`/`.ps1` files; markdown 2-space.** BOM-less UTF-8, LF-only.
- **US English everywhere.**
- **Relative paths only inside documents** (workspace-relative like `../Glitnir/docs/...`); machine-absolute paths never enter any file.
- **Warnings are errors** platform-wide; a change that introduces any MSBuild warning (including MSB4011 double-import) is a failed task.
- **Workspace root for all commands:** the Bifröst checkout (all `dotnet`/`pwsh` invocations below run from there unless a `git -C` path says otherwise).
- **Harness JSON parsing:** `dotnet msbuild -getProperty:`/`-getItem:` output is extracted by regex (`'(?s)\{.*\}'`) before `ConvertFrom-Json` — SDK banners pollute stdout (proven: build-enforcement FINDINGS #11).
- **Standalone simulation knob:** passing `-p:_ParentTargets=__standalone__` (a nonexistent path) as a global property overrides the realm-root computed value, making `Exists('$(_ParentTargets)')` false — standalone behavior evaluated from inside the workspace without a second checkout. (Global properties win over file-defined properties.) Pre-hoist group files key on `_BifrostTargets`; the same trick applies with that name in Task 1's baseline.

---

### Task 1: `Verify-Runes.ps1` — the evaluated-state harness, with the NORSE080 gap as its first failing test

**Files:**
- Create: `scripts/Verify-Runes.ps1` (Bifröst repo — sibling of the existing `scripts/` content)

**Interfaces:**
- Produces: `pwsh scripts/Verify-Runes.ps1 [-Phase baseline|post-aggregation|post-hoist|final]` — exit 0 = all assertions for that phase green. Later tasks extend the phase sets; `Assert-Items` / `Assert-Property` helper signatures below are what they extend.

- [ ] **Step 1: Write the harness with baseline (passing) and gap (failing) assertions**

```powershell
#!/usr/bin/env pwsh
#
# Verify-Runes.ps1 — evaluated-state assertions for the MSBuild estate.
# Green builds are necessary, not sufficient: this harness asserts what the
# evaluation actually produced, per the verification matrix in
# ../Glitnir/docs/Platform/specs/2026-08-07-msbuild-estate-consolidation-design.md §4.
param(
	[ValidateSet('baseline', 'post-aggregation', 'post-hoist', 'final')]
	[string]$Phase = 'baseline'
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Failures = [System.Collections.Generic.List[string]]::new()

function Get-Evaluated {
	param([string]$Project, [string[]]$Items = @(), [string[]]$Properties = @(), [string[]]$Props = @())
	$MsbuildArgs = @($Project)
	$Items      | ForEach-Object { $MsbuildArgs += "-getItem:$_" }
	$Properties | ForEach-Object { $MsbuildArgs += "-getProperty:$_" }
	$Props      | ForEach-Object { $MsbuildArgs += "-p:$_" }
	$Raw = dotnet msbuild @MsbuildArgs 2>&1 | Out-String
	if ($Raw -notmatch '(?s)(\{.*\})') { throw "No JSON in msbuild output for ${Project}: $Raw" }
	$Matches[1] | ConvertFrom-Json
}

function Assert-Items {
	param([string]$Label, [object]$Evaluated, [string]$ItemType, [string]$PathSuffix, [switch]$Absent, [string]$MetadataName, [string]$MetadataValue)
	$Hits = @($Evaluated.Items.$ItemType | Where-Object {
		$Match = ($_.Identity -replace '\\', '/').EndsWith($PathSuffix)
		if ($Match -and $MetadataName) { $Match = $_.$MetadataName -eq $MetadataValue }
		$Match
	})
	$Ok = $Absent ?
		($Hits.Count -eq 0) :
		($Hits.Count -eq 1)
	if (-not $Ok) { $Failures.Add("$Label — expected $($Absent ? '0' : '1') of '$PathSuffix' in @($ItemType), found $($Hits.Count)") }
}

function Assert-Property {
	param([string]$Label, [object]$Evaluated, [string]$Name, [string]$Expected)
	$Actual = $Evaluated.Properties.$Name
	if ($Actual -ne $Expected) { $Failures.Add("$Label — $Name expected '$Expected', got '$Actual'") }
}

# ---- Anchor projects -------------------------------------------------------
$MidgardBackend = "$Root/Midgard/src/Infrastructure.Backend/Infrastructure.Backend.csproj"
$MidgardServer  = "$Root/Midgard/src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj"
$YggWebServer   = "$Root/Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj"

# ---- BASELINE: true today, true forever ------------------------------------
# Workspace mode: NorseRef -> ProjectReference; platform analyzers attach.
$E = Get-Evaluated $MidgardBackend -Items ProjectReference, PackageReference
Assert-Items 'workspace: platform law attaches'   $E ProjectReference 'Svartalfheim/gen/Architecture.Analyzers/Architecture.Analyzers.csproj' -MetadataName OutputItemType -MetadataValue Analyzer
Assert-Items 'workspace: primitives law attaches' $E ProjectReference 'Svartalfheim/gen/Primitives.Analyzers/Primitives.Analyzers.csproj' -MetadataName OutputItemType -MetadataValue Analyzer
# Workspace package mode: same graph, package lens, no ProjectReference emission for NorseRef.
$E = Get-Evaluated $MidgardBackend -Items PackageReference -Props 'UseProjectReferences=false'
$NorsePkgs = @($E.Items.PackageReference | Where-Object Identity -like 'Norse.*')
if ($NorsePkgs.Count -eq 0) { $Failures.Add('package mode: no Norse.* PackageReference emitted for a NorseRef consumer') }
$NorsePkgs | Where-Object { $_.Version -ne '*-*' } | ForEach-Object { $Failures.Add("package mode: $($_.Identity) version '$($_.Version)' != '*-*'") }
# CPM realm, package mode: Version metadata must be absent (NU1008 law).
$E = Get-Evaluated $YggWebServer -Items PackageReference -Props 'UseProjectReferences=false'
@($E.Items.PackageReference | Where-Object Identity -like 'Norse.*') | Where-Object { $_.Version } | ForEach-Object {
	$Failures.Add("CPM package mode: $($_.Identity) carries Version '$($_.Version)' — NU1008") }
# Packaging identity: src evaluates packable, Yggdrasil hosts do not.
$E = Get-Evaluated $MidgardBackend -Properties IsPackable, OutputType
Assert-Property 'src identity' $E IsPackable 'true'
Assert-Property 'src identity' $E OutputType 'Library'
$E = Get-Evaluated $YggWebServer -Properties IsPackable
Assert-Property 'ygg host identity' $E IsPackable 'false'

# ---- GAP (fails on baseline; passes from post-aggregation on) --------------
# NORSE080: Yggdrasil consumes Midgard by NorseRef but never sees the realm analyzer.
if ($Phase -ne 'baseline') {
	$E = Get-Evaluated $YggWebServer -Items ProjectReference
	Assert-Items 'workspace: realm law crosses realms' $E ProjectReference 'Midgard/gen/Infrastructure.Web.Grpc.Analyzers/Infrastructure.Web.Grpc.Analyzers.csproj' -MetadataName OutputItemType -MetadataValue Analyzer
	# No double-attach anywhere: every analyzer-shaped ProjectReference identity is unique.
	$E = Get-Evaluated $MidgardServer -Items ProjectReference
	$AnalyzerRefs = @($E.Items.ProjectReference | Where-Object OutputItemType -eq 'Analyzer' | ForEach-Object { ($_.FullPath ?? $_.Identity) -replace '\\', '/' })
	$Dupes = $AnalyzerRefs | Group-Object | Where-Object Count -gt 1
	$Dupes | ForEach-Object { $Failures.Add("double-attach: $($_.Name) appears $($_.Count)x") }
}

# ---- POST-HOIST (Task 4 onward) --------------------------------------------
if ($Phase -in 'post-hoist', 'final') {
	# Standalone simulation: fallback emits stable-wildcard packages, not project refs.
	$E = Get-Evaluated $MidgardBackend -Items ProjectReference, PackageReference -Props 'UseProjectReferences=false', '_ParentTargets=__standalone__'
	@($E.Items.PackageReference | Where-Object Identity -like 'Norse.*') | Where-Object { $_.Version -ne '*' -and $_.Identity -ne 'Norse.Architecture.Analyzers' } | ForEach-Object {
		$Failures.Add("standalone fallback: $($_.Identity) version '$($_.Version)' != '*'") }
	# Standalone realm-internal attach: Midgard's own manifest still delivers NORSE080.
	Assert-Items 'standalone: realm law attaches realm-internally' $E ProjectReference 'Midgard/gen/Infrastructure.Web.Grpc.Analyzers/Infrastructure.Web.Grpc.Analyzers.csproj' -MetadataName OutputItemType -MetadataValue Analyzer
}

if ($Failures.Count) {
	$Failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
	exit 1
}
Write-Host "Verify-Runes [$Phase]: all assertions green." -ForegroundColor Green
```

- [ ] **Step 2: Run baseline phase — must PASS (proves the harness models today's estate correctly)**

Run: `pwsh scripts/Verify-Runes.ps1 -Phase baseline`
Expected: exit 0, "all assertions green". If any baseline assertion fails, the harness's model of the current estate is wrong — fix the harness, not the estate.

- [ ] **Step 3: Run post-aggregation phase — must FAIL on the NORSE080 gap**

Run: `pwsh scripts/Verify-Runes.ps1 -Phase post-aggregation`
Expected: exit 1, exactly one failure: `workspace: realm law crosses realms`. This is the plan's driving failing test.

- [ ] **Step 4: Stage in Bifröst**

```bash
git -C . add scripts/Verify-Runes.ps1
```

---

### Task 2: Realm analyzer manifests — Svartálfheim and Midgard

**Files:**
- Create: `Svartalfheim/Directory.Analyzers.props`
- Create: `Midgard/Directory.Analyzers.props`

**Interfaces:**
- Produces: `@(NorseRealmAnalyzer)` items with absolute (self-anchored) `.csproj` identities — consumed by Task 3 (Bifröst aggregation) and Task 4 (realm-root standalone attach). Item type name is exactly `NorseRealmAnalyzer`.

- [ ] **Step 1: Write `Svartalfheim/Directory.Analyzers.props`**

```xml
<Project>
	<!--
		The realm's analyzer manifest — the single declaration of every Roslyn diagnostics
		analyzer this realm ships. Realm-owned, never scattered. Consumed twice: Bifrost's
		root Directory.Build.targets wildcard-imports every realm's manifest and attaches the
		declared analyzers to all workspace compilations; the canonical realm-root
		Directory.Build.targets imports this file in standalone mode and attaches them
		realm-internally. Diagnostics-only by law — a generator never rides a manifest
		(generators are consumer-declared via NorseRef Generator="true"). Doctrine:
		Glitnir/docs/the-runes.md.
	-->
	<ItemGroup>
		<NorseRealmAnalyzer Include="$(MSBuildThisFileDirectory)gen/Architecture.Analyzers/Architecture.Analyzers.csproj" />
		<NorseRealmAnalyzer Include="$(MSBuildThisFileDirectory)gen/Primitives.Analyzers/Primitives.Analyzers.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Write `Midgard/Directory.Analyzers.props`** — same header comment verbatim, one item:

```xml
	<ItemGroup>
		<NorseRealmAnalyzer Include="$(MSBuildThisFileDirectory)gen/Infrastructure.Web.Grpc.Analyzers/Infrastructure.Web.Grpc.Analyzers.csproj" />
	</ItemGroup>
```

- [ ] **Step 3: Verify inertness — nothing consumes the manifests yet**

Run: `pwsh scripts/Verify-Runes.ps1 -Phase baseline`
Expected: exit 0, unchanged. (`dotnet build Bifrost.slnx` also stays green if run.)

- [ ] **Step 4: Stage in each realm**

```bash
git -C Svartalfheim add Directory.Analyzers.props
git -C Midgard add Directory.Analyzers.props
```

---

### Task 3: Bifröst root targets — wildcard aggregation, generic attach, `Rules="true"` resolution

**Files:**
- Modify: `Directory.Build.targets` (Bifröst root)

**Interfaces:**
- Consumes: `@(NorseRealmAnalyzer)` from Task 2 manifests.
- Produces: workspace-mode analyzer attachment for all manifest-declared analyzers; `Rules="true"` NorseRef resolution (analyzer-shaped `ProjectReference` into `%(Repo)/src/`); plain-`src` NorseRef line now filtered by `WithMetadataValue('Rules', '')`.

- [ ] **Step 1: Add the wildcard manifest import** as the first child element of `<Project>` (before the `Choose`):

```xml
	<!--
		Aggregates every realm's analyzer manifest (Directory.Analyzers.props, realm-owned,
		never scattered). A realm landing its first analyzer edits only its own repo; this
		file never needs touching again. A wildcard that matches nothing imports nothing —
		this is path expansion, not item expansion, so the Import-resolves-at-properties-pass
		law (the-runes.md ch. 4) is not violated.
	-->
	<Import Project="$(MSBuildThisFileDirectory)*/Directory.Analyzers.props" />
```

- [ ] **Step 2: Inside the `UseProjectReferences=true` `<When>`, replace the two hand-authored analyzer blocks** (the `Architecture.Analyzers` ItemGroup and the `Primitives.Analyzers` ItemGroup, including their long comments) **with the generic block:**

```xml
				<!--
					Realm law attaches to every workspace compilation — no NorseRef opt-in, no realm
					opt-out — sourced from the aggregated realm manifests (wildcard import at the top
					of this file). Deliberately broader than the package crossing's closure-scoped
					delivery: fail earlier, fail locally; analyzers no-op where their subject is
					absent. Exclusions: any project that is itself a manifest-declared analyzer
					(covers self-reference and analyzer-on-analyzer, matched on normalized full
					path, never bare name), and any Aspire AppHost ($(IsAspireHost) — see
					the-runes.md ch. 5 for why an AppHost is orchestration wiring, not shipped code).
					Standalone builds get the same analyzers realm-internally from the canonical
					realm-root Directory.Build.targets reading the realm's own manifest — the two
					attach sites are condition-disjoint by construction, so nothing double-attaches.
				-->
				<ItemGroup Condition="'$(IsAspireHost)' != 'true'">
					<_NorseRealmAnalyzerSelf Include="@(NorseRealmAnalyzer)" Condition="'%(FullPath)' == '$(MSBuildProjectFullPath)'" />
					<ProjectReference Include="@(NorseRealmAnalyzer)" OutputItemType="Analyzer" ReferenceOutputAssembly="false" Condition="'@(_NorseRealmAnalyzerSelf)' == ''" />
				</ItemGroup>
```

- [ ] **Step 3: Split the plain `src` NorseRef line and add `Rules` resolution.** The existing first `ProjectReference` line in the `NorseRef` ItemGroup becomes two lines:

```xml
				<ItemGroup Condition="'@(NorseRef)' != ''">
					<ProjectReference Include="@(NorseRef->WithMetadataValue('Rules', '')->'$(MSBuildThisFileDirectory)%(Repo)/src/%(Identity)/%(Identity).csproj')" />
					<!--
						Rules="true": a DacFx schema-diagnostics library (project suffix .Rules, an
						ordinary src/ library) consumed analyzer-shaped — never compiled against. The
						three-suffix taxonomy (.Generator / .Rules / .Analyzers) and why Analyzer=
						metadata never exists on this platform: the-runes.md ch. 5. Custom metadata
						(e.g. DatabaseSqlCmdVariable) passes through transforms untouched, so database
						NorseRefs carry their SqlCmd wiring into both crossings for free.
					-->
					<ProjectReference Include="@(NorseRef->WithMetadataValue('Rules', 'true')->'$(MSBuildThisFileDirectory)%(Repo)/src/%(Identity)/%(Identity).csproj')" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
					<ProjectReference Include="@(NorseRef->WithMetadataValue('Generator', 'true')->'$(MSBuildThisFileDirectory)%(Repo)/gen/%(Identity).Generator/%(Identity).Generator.csproj')" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
				</ItemGroup>
```

(The `Generator` line is unchanged — shown for placement. `WithMetadataValue('Rules', '')` matches items where the metadata is unset, the same production-proven pattern as the ancestor estate's plain branch.)

- [ ] **Step 4: Update the closing comment block** — the pointer to `Glitnir/docs/the-two-crossings.md` becomes `Glitnir/docs/the-runes.md`. Package-mode branches (`CPM`/`Otherwise`) are untouched: a `Rules=` NorseRef correctly becomes a plain `PackageReference` there (the packed rules package's own `build/*.targets` is the delivery mechanism in the package crossing).

- [ ] **Step 5: Run the driving test — the gap must close**

Run: `pwsh scripts/Verify-Runes.ps1 -Phase post-aggregation`
Expected: exit 0. `workspace: realm law crosses realms` now passes (NORSE080 reaches `Hosting.Web.Server`); `double-attach` stays clean — Midgard's realm-root hand block still exists and declares the same `.csproj` path, which MSBuild/Roslyn dedup by identity; the assertion tolerates it because identities are equal, and the hand block is deleted in Task 6.

- [ ] **Step 6: Full workspace build both modes**

Run: `dotnet build Bifrost.slnx` then `dotnet build Bifrost.slnx -p:UseProjectReferences=false`
Expected: both green, zero warnings.

- [ ] **Step 7: Stage in Bifröst**

```bash
git -C . add Directory.Build.targets
```

---

### Task 4: Canonical realm-root `Directory.Build.targets` — the hoist (Ginnungagap) + local fan-out

**Files:**
- Modify: `../.github/config/Directory.Build.targets`
- Modify (local fan-out, scatter-equivalent): every realm's root `Directory.Build.targets` **except Midgard's** (Midgard is Task 6): `Svartalfheim Asgard Urdarbrunnr Ratatoskr Heimdall Himinbjorg Mimisbrunnr Mimir Naglfar Bragi Yggdrasil`

**Interfaces:**
- Consumes: `@(NorseRealmAnalyzer)` (Task 2), `@(NorseRef)`/`@(NorseDesignRef)`/`@(NorseGeneratorRef)` from csprojs.
- Produces: the realm-root file as the single home of: standalone NorseRef fallback (CPM-aware), standalone manifest attach, hoisted strip target `_NorseRemoveUnwantedGeneratorAnalyzers`, and the `Directory.Realm.targets` seam. Group-level files (Task 5) reduce to chain stubs on top of this.

- [ ] **Step 1: Rewrite `../.github/config/Directory.Build.targets`** — full replacement content (existing `Using Remove` and Architecture.Analyzers `Choose` retained verbatim where marked):

```xml
<Project>
	<PropertyGroup>
		<_ParentTargets>$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../'))</_ParentTargets>
	</PropertyGroup>
	<Import Project="$(_ParentTargets)" Condition="Exists('$(_ParentTargets)')" />
	<!--
		The realm-root targets — law that binds src, tests, gen, and schema exactly once
		(minted 2026-08-03; widened 2026-08-07 when the NorseRef fallback and the
		generator-strip target hoisted up from the group-level files). Group-level
		Directory.Build.targets are chain stubs plus their layer's own subject; everything
		shared lives here. A scattered file is canonical by definition — realm-owned law
		never edits this file; it lives in Directory.Analyzers.props or Directory.Realm.targets
		(both realm-owned, both unscattered — see the seam import at the bottom).
		Doctrine: Glitnir/docs/the-runes.md.
	-->
	[UNCHANGED: the <ItemGroup> with <Using Remove="System.Net.Http.Json" /> and its comment]
	[UNCHANGED: the Architecture.Analyzers three-way <Choose> and its comments]
	<!--
		The standalone NorseRef fallback, hoisted from the src/tests/gen group files
		(2026-08-07). Keys on Bifrost's ABSENCE, never on UseProjectReferences: in the
		workspace, Bifrost's root Choose owns both crossings (including workspace package
		mode, where NorseRefVersion=*-* floats prerelease) and emitting here too would
		double-emit. Standalone splits on CPM at targets-evaluation time — the same
		NU1008 evaluation-order law as the analyzer Choose above.
	-->
	<Choose>
		<When Condition="Exists('$(_ParentTargets)')">
			<PropertyGroup />
		</When>
		<When Condition="'$(ManagePackageVersionsCentrally)' == 'true'">
			<ItemGroup>
				<PackageReference Include="@(NorseRef->'Norse.%(Identity)')" />
				<PackageReference Include="@(NorseDesignRef->'Norse.%(Identity)')">
					<PrivateAssets>all</PrivateAssets>
				</PackageReference>
			</ItemGroup>
		</When>
		<Otherwise>
			<ItemGroup>
				<!--
					"*" = latest released, never prerelease — CI consumes shipped stable only.
					Library realms float deliberately: they do not own the deployed dependency
					closure; the composition root (CPM) owns and pins it. the-runes.md ch. 6.
				-->
				<PackageReference Include="@(NorseRef->'Norse.%(Identity)')" Version="*" />
				<PackageReference Include="@(NorseDesignRef->'Norse.%(Identity)')" Version="*">
					<PrivateAssets>all</PrivateAssets>
				</PackageReference>
			</ItemGroup>
		</Otherwise>
	</Choose>
	<!--
		Standalone-mode realm law: the realm's own analyzer manifest, attached realm-internally.
		Guarded to standalone twice over — the Import condition (so the workspace never imports
		the manifest here AND via Bifrost's wildcard: MSB4011) and the ItemGroup condition (the
		coordination law: in the workspace, Bifrost's manifest-driven block is the sole attacher).
	-->
	<Import Project="$(MSBuildThisFileDirectory)Directory.Analyzers.props" Condition="!Exists('$(_ParentTargets)') AND Exists('$(MSBuildThisFileDirectory)Directory.Analyzers.props')" />
	<ItemGroup Condition="!Exists('$(_ParentTargets)') AND '$(IsAspireHost)' != 'true'">
		<_NorseRealmAnalyzerSelf Include="@(NorseRealmAnalyzer)" Condition="'%(FullPath)' == '$(MSBuildProjectFullPath)'" />
		<ProjectReference Include="@(NorseRealmAnalyzer)" OutputItemType="Analyzer" ReferenceOutputAssembly="false" Condition="'@(_NorseRealmAnalyzerSelf)' == ''" />
	</ItemGroup>
	<!--
		The generator-strip target, hoisted from the src/tests group files (2026-08-07) so gen/
		is covered too — the dormant-gap shape the Task 7 postmortem warns about, closed.
		Applies whether or not Bifrost is present, and only to analyzers that arrived through
		the NuGet package chain (NuGetPackageId metadata set) — the one mechanism that pushes a
		generator into a compilation that didn't ask for it. An analyzer wired via
		ProjectReference (a project's own sibling gen/ generator, Bifrost's dev-mode forwarding,
		or the manifest attach above) is deliberate by construction and never a strip candidate.
		Full design: Glitnir/docs/Platform/specs/2026-07-01-norseref-generator-forwarding-design.md
		Provenance scoping: Glitnir/docs/Platform/specs/2026-07-31-norseref-strip-provenance-scoping-design.md
	-->
	<Target Name="_NorseRemoveUnwantedGeneratorAnalyzers" BeforeTargets="CoreCompile" Condition="'@(Analyzer)' != ''">
		<ItemGroup>
			<_NorseWantedGeneratorAnalyzer Include="@(NorseRef->WithMetadataValue('Generator', 'true')->'Norse.%(Identity).Generator')" />
			<_NorseWantedGeneratorAnalyzer Include="@(NorseGeneratorRef->'Norse.%(Identity)')" />
		</ItemGroup>
		<PropertyGroup>
			<_NorseWantedGeneratorAnalyzerNames>;@(_NorseWantedGeneratorAnalyzer);</_NorseWantedGeneratorAnalyzerNames>
		</PropertyGroup>
		<ItemGroup>
			<Analyzer Remove="@(Analyzer)" Condition="'%(Analyzer.NuGetPackageId)' != '' and $([System.Text.RegularExpressions.Regex]::IsMatch('%(Analyzer.Filename)', '^Norse\..+\.Generator$')) and !$(_NorseWantedGeneratorAnalyzerNames.Contains(';%(Analyzer.Filename);'))" />
		</ItemGroup>
	</Target>
	<!--
		The realm seam: realm-owned, never scattered, ADDITIVE ONLY — new items and new
		realm targets; redefining a canonical property or target here violates the
		canonicity law. Imported last deliberately so realm additions can react to
		canonical state — reaction is not mutation. the-runes.md ch. 7.
	-->
	<Import Project="$(MSBuildThisFileDirectory)Directory.Realm.targets" Condition="Exists('$(MSBuildThisFileDirectory)Directory.Realm.targets')" />
</Project>
```

(The two `[UNCHANGED: ...]` markers mean: carry those blocks over from the current file byte-for-byte — they are not placeholders for new content.)

- [ ] **Step 2: Fan out locally (scatter-equivalent) to every realm except Midgard**

```bash
for r in Svartalfheim Asgard Urdarbrunnr Ratatoskr Heimdall Himinbjorg Mimisbrunnr Mimir Naglfar Bragi Yggdrasil; do
	cp ../.github/config/Directory.Build.targets "$r/Directory.Build.targets"
done
```

Midgard keeps its divergent file until Task 6 — its NORSE080 hand block must not go dark before the harness proves the replacement paths.

- [ ] **Step 3: Verify — workspace unchanged, standalone fallback now realm-root-sourced**

Run: `pwsh scripts/Verify-Runes.ps1 -Phase post-aggregation` (must stay green)
Then evaluate the standalone simulation manually for one non-CPM realm:
Run: `dotnet msbuild Mimisbrunnr/src/Reference.Data/Reference.Data.csproj -getItem:PackageReference -p:UseProjectReferences=false -p:_ParentTargets=__standalone__`
Expected: `Norse.*` PackageReference items with `Version="*"` (fallback fired from the realm root; note the group-level fallback also still exists until Task 5 — same items, deduplicated by NuGet, transitional only).

- [ ] **Step 4: Build both modes**

Run: `dotnet build Bifrost.slnx && dotnet build Bifrost.slnx -p:UseProjectReferences=false`
Expected: green, zero warnings (MSB4011 would surface here if the manifest double-import guard were wrong).

- [ ] **Step 5: Stage — Ginnungagap and every fanned-out realm**

```bash
git -C ../.github add config/Directory.Build.targets
for r in Svartalfheim Asgard Urdarbrunnr Ratatoskr Heimdall Himinbjorg Mimisbrunnr Mimir Naglfar Bragi Yggdrasil; do git -C "$r" add Directory.Build.targets; done
```

---

### Task 5: Group-level shrink + `IsPackable` migration (Ginnungagap) + local fan-out

**Files:**
- Modify: `../.github/config/src/Directory.Build.props`, `../.github/config/src/Directory.Build.targets`, `../.github/config/tests/Directory.Build.targets`, `../.github/config/gen/Directory.Build.targets`
- Modify (local fan-out): the corresponding files in every `nuget`-group realm (`Svartalfheim Asgard Midgard Urdarbrunnr Ratatoskr Heimdall Himinbjorg Mimisbrunnr Mimir Naglfar Bragi`)
- Modify: `Yggdrasil/src/Directory.Build.targets`, `Yggdrasil/tests/Directory.Build.targets` (collapse to stubs — Yggdrasil-owned, not scattered)

**Interfaces:**
- Consumes: the hoisted realm-root law (Task 4).
- Produces: group files as chain stubs + layer subject; `IsPackable=true` in `src/Directory.Build.props`.

- [ ] **Step 1: `config/src/Directory.Build.props`** — insert into the existing PropertyGroup, alphabetical position (after `IsAotCompatible`):

```xml
		<!-- Every src project ships as a NuGet package regardless of Sdk. Lives at props level
		     deliberately: the Web/Worker SDKs' false default is Condition-on-empty, so this value
		     survives them — unlike OutputType, whose SDK default is unconditional and can only be
		     overridden at targets time (src/Directory.Build.targets). the-runes.md ch. 4. -->
		<IsPackable>true</IsPackable>
```

- [ ] **Step 2: `config/src/Directory.Build.targets`** — full replacement:

```xml
<Project>
	<PropertyGroup>
		<!-- Targets-time deliberately: the Web/Worker SDKs set OutputType=Exe UNCONDITIONALLY in
		     their Sdk props, so a props-level Library is stomped; only targets placement wins.
		     (IsPackable, whose SDK default is Condition-on-empty, lives in src/Directory.Build.props
		     for exactly the inverse reason.) No realm src project is an executable — hosts live in
		     the runtime realm only. -->
		<OutputType>Library</OutputType>
		<_ParentTargets>$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../'))</_ParentTargets>
	</PropertyGroup>
	<Import Project="$(_ParentTargets)" Condition="Exists('$(_ParentTargets)')" />
</Project>
```

- [ ] **Step 3: `config/tests/Directory.Build.targets`** — full replacement: identical shape with `<OutputType>Exe</OutputType>` (comment: `<!-- MTP requires an executable test host. -->`) and no other content.

- [ ] **Step 4: `config/gen/Directory.Build.targets`** — full replacement: the chain stub only (PropertyGroup with `_ParentTargets`, the Import, nothing else).

- [ ] **Step 5: Fan out locally to the `nuget`-group realms; collapse Yggdrasil's own pair**

```bash
for r in Svartalfheim Asgard Midgard Urdarbrunnr Ratatoskr Heimdall Himinbjorg Mimisbrunnr Mimir Naglfar Bragi; do
	cp ../.github/config/src/Directory.Build.props   "$r/src/Directory.Build.props"
	cp ../.github/config/src/Directory.Build.targets "$r/src/Directory.Build.targets"
	cp ../.github/config/tests/Directory.Build.targets "$r/tests/Directory.Build.targets"
	[ -d "$r/gen" ] && cp ../.github/config/gen/Directory.Build.targets "$r/gen/Directory.Build.targets"
done
cp ../.github/config/tests/Directory.Build.targets Yggdrasil/tests/Directory.Build.targets
```

For `Yggdrasil/src/Directory.Build.targets`, write the chain stub by hand (it must NOT contain `OutputType=Library` — Yggdrasil hosts are executables and web apps whose SDKs set their own):

```xml
<Project>
	<PropertyGroup>
		<_ParentTargets>$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../'))</_ParentTargets>
	</PropertyGroup>
	<Import Project="$(_ParentTargets)" Condition="Exists('$(_ParentTargets)')" />
</Project>
```

(`Yggdrasil/tests/Directory.Build.targets` receiving the canonical copy is correct — post-shrink it is CPM-safe: no `Version` attributes exist in it anymore. Yggdrasil's `src/Directory.Build.props` is untouched.)

- [ ] **Step 6: Run the full harness including standalone + packaging identity**

Run: `pwsh scripts/Verify-Runes.ps1 -Phase post-hoist`
Expected: exit 0 — including `src identity` (IsPackable true via props now), `ygg host identity` (still false), and the standalone assertions. Then `dotnet build Bifrost.slnx && dotnet build Bifrost.slnx -p:UseProjectReferences=false` — green.

- [ ] **Step 7: Stage everywhere touched**

```bash
git -C ../.github add config/src config/tests config/gen
for r in Svartalfheim Asgard Midgard Urdarbrunnr Ratatoskr Heimdall Himinbjorg Mimisbrunnr Mimir Naglfar Bragi; do git -C "$r" add src tests gen 2>/dev/null || git -C "$r" add src tests; done
git -C Yggdrasil add src/Directory.Build.targets tests/Directory.Build.targets
```

---

### Task 6: Midgard reversion — the hand-authored NORSE080 block dies, canonically

**Files:**
- Modify: `Midgard/Directory.Build.targets` (replace with the Task 4 canonical copy)

**Interfaces:**
- Consumes: Midgard's manifest (Task 2), Bifröst aggregation (Task 3), canonical standalone attach (Task 4).

- [ ] **Step 1: Verify NORSE080's replacement paths are live BEFORE the reversion**

Run: `pwsh scripts/Verify-Runes.ps1 -Phase post-hoist`
Expected: green — `workspace: realm law crosses realms` (Bifröst path) and `standalone: realm law attaches realm-internally` (manifest path) both pass while the hand block still exists.

- [ ] **Step 2: Revert to canonical**

```bash
cp ../.github/config/Directory.Build.targets Midgard/Directory.Build.targets
```

- [ ] **Step 3: Verify NORSE080 after — both paths, plus no-double-attach**

Run: `pwsh scripts/Verify-Runes.ps1 -Phase post-hoist`
Expected: green. The `double-attach` assertion now proves the coordination law for real (single attach site per mode). Then `dotnet build Bifrost.slnx` — green.

- [ ] **Step 4: Stage**

```bash
git -C Midgard add Directory.Build.targets
```

---

### Task 7: Scatter divergence guard + audit — lineage library first, TDD

**Files:**
- Create: `../.github/scripts/lib/rune-lineage.ps1`
- Create: `../.github/scripts/tests/verify-rune-lineage.ps1`
- Modify: `../.github/scripts/scatter-the-runes.ps1`

**Interfaces:**
- Produces: `Get-RuneClassification -ConfigRepo <path> -CanonicalRelPath <config/...> -DestFile <path>` returning one of `'Current' | 'Stale' | 'Divergent' | 'LineageUnavailable'`; scatter params `-Audit` and `-AcceptDivergence <string[]>` (entries `Realm/relative/path`).

- [ ] **Step 1: Write the failing test** `../.github/scripts/tests/verify-rune-lineage.ps1`:

```powershell
#!/usr/bin/env pwsh
# Fixture-driven proof of the lineage classifier. Builds a throwaway canonical repo
# with two versions of a file, then classifies four destination states.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../lib/rune-lineage.ps1"
$Failures = [System.Collections.Generic.List[string]]::new()
$Fixture = Join-Path ([System.IO.Path]::GetTempPath()) "rune-lineage-$(New-Guid)"
try {
	$Config = Join-Path $Fixture 'config-repo'
	New-Item -ItemType Directory -Path (Join-Path $Config 'config') -Force | Out-Null
	git -C $Config init --quiet
	Set-Content (Join-Path $Config 'config/probe.targets') "<Project>v1</Project>" -NoNewline
	git -C $Config add . ; git -C $Config -c user.email=t@t -c user.name=t commit -m v1 --quiet
	Set-Content (Join-Path $Config 'config/probe.targets') "<Project>v2</Project>" -NoNewline
	git -C $Config add . ; git -C $Config -c user.email=t@t -c user.name=t commit -m v2 --quiet

	$Dest = Join-Path $Fixture 'dest'
	New-Item -ItemType Directory -Path $Dest -Force | Out-Null
	Set-Content (Join-Path $Dest 'current.targets')   "<Project>v2</Project>" -NoNewline
	Set-Content (Join-Path $Dest 'stale.targets')     "<Project>v1</Project>" -NoNewline
	Set-Content (Join-Path $Dest 'divergent.targets') "<Project>realm edit</Project>" -NoNewline

	$Cases = @(
		@{ File = 'current.targets';   Expected = 'Current' }
		@{ File = 'stale.targets';     Expected = 'Stale' }
		@{ File = 'divergent.targets'; Expected = 'Divergent' }
	)
	foreach ($Case in $Cases) {
		$Actual = Get-RuneClassification -ConfigRepo $Config -CanonicalRelPath 'config/probe.targets' -DestFile (Join-Path $Dest $Case.File)
		if ($Actual -ne $Case.Expected) { $Failures.Add("$($Case.File): expected $($Case.Expected), got $Actual") }
	}
	# Lineage unavailable: a canonical path with no history at all.
	$Actual = Get-RuneClassification -ConfigRepo $Config -CanonicalRelPath 'config/never-existed.targets' -DestFile (Join-Path $Dest 'current.targets')
	if ($Actual -ne 'LineageUnavailable') { $Failures.Add("no-history path: expected LineageUnavailable, got $Actual") }
	# Shallow repo: classification must refuse, not guess.
	$Shallow = Join-Path $Fixture 'shallow'
	git clone --depth 1 "file://$Config" $Shallow --quiet 2>$null
	$Actual = Get-RuneClassification -ConfigRepo $Shallow -CanonicalRelPath 'config/probe.targets' -DestFile (Join-Path $Dest 'stale.targets')
	if ($Actual -ne 'LineageUnavailable') { $Failures.Add("shallow repo: expected LineageUnavailable, got $Actual") }
} finally {
	Remove-Item $Fixture -Recurse -Force -ErrorAction SilentlyContinue
}
if ($Failures.Count) { $Failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }; exit 1 }
Write-Host 'verify-rune-lineage: all assertions green.' -ForegroundColor Green
```

- [ ] **Step 2: Run it — must FAIL** (`rune-lineage.ps1` doesn't exist)

Run: `pwsh ../.github/scripts/tests/verify-rune-lineage.ps1`
Expected: hard error loading the dot-sourced library.

- [ ] **Step 3: Write `../.github/scripts/lib/rune-lineage.ps1`**

```powershell
#!/usr/bin/env pwsh
#
# rune-lineage.ps1 — classifies a scattered destination file against the canonical
# file's git lineage. A destination whose content matches ANY historical blob of the
# canonical path is merely Current/Stale; content outside the lineage is a realm
# Divergence (hard fail in scatter, never silently overwritten). A repo that cannot
# answer (shallow clone, no history for the path) is LineageUnavailable — its own
# hard-fail outcome, never conflated with divergence, never silently passed.
# Doctrine: Bifrost/Glitnir/docs/the-runes.md ch. 7.

function Get-RuneClassification {
	param(
		[Parameter(Mandatory)] [string]$ConfigRepo,
		[Parameter(Mandatory)] [string]$CanonicalRelPath,
		[Parameter(Mandatory)] [string]$DestFile
	)
	$IsShallow = git -C $ConfigRepo rev-parse --is-shallow-repository 2>$null
	if ($LASTEXITCODE -ne 0 -or $IsShallow -eq 'true') { return 'LineageUnavailable' }

	$Commits = @(git -C $ConfigRepo log --all --format=%H -- $CanonicalRelPath 2>$null)
	if ($LASTEXITCODE -ne 0 -or $Commits.Count -eq 0) { return 'LineageUnavailable' }

	$Blobs = [System.Collections.Generic.HashSet[string]]::new()
	foreach ($Commit in $Commits) {
		$Blob = git -C $ConfigRepo rev-parse --verify --quiet "${Commit}:${CanonicalRelPath}" 2>$null
		if ($LASTEXITCODE -eq 0 -and $Blob) { [void]$Blobs.Add($Blob.Trim()) }
	}
	if ($Blobs.Count -eq 0) { return 'LineageUnavailable' }

	$DestBlob = (git -C $ConfigRepo hash-object $DestFile).Trim()
	if (-not $Blobs.Contains($DestBlob)) { return 'Divergent' }

	$HeadBlob = git -C $ConfigRepo rev-parse --verify --quiet "HEAD:${CanonicalRelPath}" 2>$null
	return ($HeadBlob -and $DestBlob -eq $HeadBlob.Trim()) ?
		'Current' :
		'Stale'
}
```

- [ ] **Step 4: Run the test — must PASS**

Run: `pwsh ../.github/scripts/tests/verify-rune-lineage.ps1`
Expected: exit 0, all five cases green.

- [ ] **Step 5: Wire the guard + audit into `scatter-the-runes.ps1`**

At the top: `. "$PSScriptRoot/lib/rune-lineage.ps1"`, add params `[switch]$Audit` and `[string[]]$AcceptDivergence = @()`. In the per-realm loop, immediately after the clone/checkout succeeds and **before** the copy loop, classify every file; in the copy loop, refuse divergence:

```powershell
		$Report = foreach ($File in $Files) {
			$Dest = Join-Path $TempDir $File
			$Classification = (Test-Path $Dest) ?
				(Get-RuneClassification -ConfigRepo $PSScriptRoot/.. -CanonicalRelPath "config/$File" -DestFile $Dest) :
				'Stale'   # a file the realm never had is just not-yet-scattered
			[pscustomobject]@{ Realm = $Realm; File = $File; State = $Classification }
		}
		$Report | ForEach-Object { Write-Host "    [$($_.State)] $($_.File)" }
		if ($Audit) { continue }   # audit mode reports and touches nothing

		$Unavailable = @($Report | Where-Object State -eq 'LineageUnavailable')
		if ($Unavailable) { throw "Lineage unavailable for $($Unavailable.Count) file(s) in $Realm — full history of the config repo is required (no shallow checkout). Files: $($Unavailable.File -join ', ')" }

		$Divergent = @($Report | Where-Object { $_.State -eq 'Divergent' -and "$Realm/$($_.File)" -notin $AcceptDivergence })
		if ($Divergent) { throw "Refusing to overwrite realm-divergent file(s) in $Realm — a scattered file is canonical by definition; realm law lives in Directory.Analyzers.props or Directory.Realm.targets. Re-run with -AcceptDivergence for a deliberate reversion. Files: $($Divergent.File -join ', ')" }
```

The `scatter-the-runes.yml` workflow's checkout step gains `fetch-depth: 0` (full history — the lineage precondition).

- [ ] **Step 6: Prove the guard end-to-end with a planted divergence (dry, local)**

Run the test file again plus a manual smoke: temporarily edit any scattered file in a realm working tree (e.g. append a comment to `Bragi/Directory.Build.targets`), run `pwsh ../.github/scripts/scatter-the-runes.ps1 -DryRun -Audit` if the script supports local-path mode, or verify by direct function call:
`pwsh -c '. ../.github/scripts/lib/rune-lineage.ps1; Get-RuneClassification -ConfigRepo ../.github -CanonicalRelPath config/Directory.Build.targets -DestFile Bragi/Directory.Build.targets'`
Expected: `Divergent` while the edit is present; `Current` after reverting it. Revert the edit.

- [ ] **Step 7: Stage in Ginnungagap**

```bash
git -C ../.github add scripts/lib/rune-lineage.ps1 scripts/tests/verify-rune-lineage.ps1 scripts/scatter-the-runes.ps1 .github/workflows/scatter-the-runes.yml
```

---

### Task 8: Canonical `schema/` templates + the scratch-rules fixture harness

**Files:**
- Create: `../.github/config/schema/Directory.Build.props`
- Create: `../.github/config/schema/Directory.Build.targets`
- Create: `../.github/scripts/verify-schema-templates.ps1`
- Create: `../.github/scripts/verify-schema-fixtures/` (scratch realm: `RealmRoot/` with copies of canonical root+schema files, `RealmRoot/src/Scratch.Rules/` a minimal DacFx rule, `RealmRoot/schema/Scratch.Database/` a sqlproj + one table)
- Modify: `../.github/config/manifest.psd1` (add the `schema` group, assigned to no realm)

**Interfaces:**
- Consumes: `Rules="true"` resolution (Task 3), hoisted realm-root law (Task 4).
- Produces: the canonical schema pair; `verify-schema-templates.ps1` exit 0 proves both crossings, `RunSqlCodeAnalysis` enablement, promotion idempotence, `DatabaseSqlCmdVariable` passthrough, and the stale-transitive strip.

- [ ] **Step 1: Write `config/schema/Directory.Build.props`**

```xml
<Project>
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
	<PropertyGroup>
		<!-- Native SQL Server by default (ruled 2026-08-07) — the template assumes nothing about
		     hosting. An Azure SQL consumer overrides with SqlAzureV12DatabaseSchemaProvider in
		     the consuming sqlproj (which carries its own DSP line anyway: the VS Code SQL
		     projects extension cannot read it from Directory.Build.props). -->
		<DSP>Microsoft.Data.Tools.Schema.Sql.Sql170DatabaseSchemaProvider</DSP>
		<ModelCollation>1033, CI</ModelCollation>
		<!-- Brand injection, the same one-edit rebrand law as assemblies. -->
		<PackageId>Norse.$(MSBuildProjectName)</PackageId>
		<!-- SR0016 (EXECUTE permissions), SR0011 (special chars in names), SR0009 (varchar over
		     char) — Microsoft rules whose guidance conflicts with platform law; suppressed at
		     the source so TreatTSqlWarningsAsErrors below doesn't weaponize them. -->
		<SqlCodeAnalysisRules>-Microsoft.Rules.Data.SR0016;-Microsoft.Rules.Data.SR0011;-Microsoft.Rules.Data.SR0009</SqlCodeAnalysisRules>
		<SqlTargetName>Norse.$(MSBuildProjectName)</SqlTargetName>
		<TargetDatabaseSet>True</TargetDatabaseSet>
		<!-- Escalates T-SQL compiler warnings ONLY. DacFx static-code-analysis warnings are a
		     different tool family this property never touches — rule PROMOTION (+!RuleId) is the
		     only error path for those. the-runes.md ch. 8. -->
		<TreatTSqlWarningsAsErrors>True</TreatTSqlWarningsAsErrors>
	</PropertyGroup>
</Project>
```

- [ ] **Step 2: Write `config/schema/Directory.Build.targets`**

```xml
<Project>
	<PropertyGroup>
		<_ParentTargets>$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../'))</_ParentTargets>
	</PropertyGroup>
	<Import Project="$(_ParentTargets)" Condition="Exists('$(_ParentTargets)')" />
	<!--
		The package crossing delivers a rules package's packed build/*.targets, which is what
		enables analysis and promotes its rule IDs to errors; a ProjectReference NEVER imports
		packed targets, so without this default a workspace-mode sqlproj's custom rules
		silently never run even with the rules DLL correctly present in @(Analyzer). Proven
		in the ancestor estate; doctrine: the-runes.md ch. 8.
	-->
	<PropertyGroup Condition="'$(UseProjectReferences)' == 'true'">
		<RunSqlCodeAnalysis Condition="'$(RunSqlCodeAnalysis)' == ''">true</RunSqlCodeAnalysis>
		<!--
			Rule promotion extension point. A consumer platform shipping a rules package mirrors
			its packed promotion tokens here, one Contains-guarded line per rule — the guard makes
			re-appending idempotent (DacFx throws SQL72039 on a duplicated RuleId) regardless of
			whether the packed targets also ran. Item-derived Import paths are structurally
			impossible (imports resolve at the properties pass), so the tokens are hardcoded by
			design, not laziness. Exemplar shape, commented until a rules package exists:
			<SqlCodeAnalysisRules Condition="!$(SqlCodeAnalysisRules.Contains('+!Example.Rules.XR0001'))">$(SqlCodeAnalysisRules);+!Example.Rules.XR0001</SqlCodeAnalysisRules>
		-->
	</PropertyGroup>
	<!--
		Stale-transitive strip, mirror polarity of the Roslyn strip in the realm-root targets:
		in workspace mode a rules package can also arrive transitively through another database
		package's pinned dependency, shadowing the live local source with a stale packed copy.
		Only the packed copy (no MSBuildSourceProjectFile metadata) of a Rules= NorseRef this
		project declared is stripped — the live ProjectReference copy is the one that runs.
	-->
	<Target Name="_NorseRemoveStaleTransitiveRules" BeforeTargets="Build" Condition="'$(UseProjectReferences)' == 'true' and '@(Analyzer)' != ''">
		<ItemGroup>
			<_NorseLiveRule Include="@(NorseRef->WithMetadataValue('Rules', 'true')->'Norse.%(Identity)')" />
		</ItemGroup>
		<PropertyGroup>
			<_NorseLiveRuleNames>;@(_NorseLiveRule);</_NorseLiveRuleNames>
		</PropertyGroup>
		<ItemGroup>
			<Analyzer Remove="@(Analyzer)" Condition="'%(Analyzer.MSBuildSourceProjectFile)' == '' and $(_NorseLiveRuleNames.Contains(';%(Analyzer.Filename);'))" />
		</ItemGroup>
	</Target>
</Project>
```

- [ ] **Step 3: Build the fixture realm** under `scripts/verify-schema-fixtures/RealmRoot/`:
	- `Directory.Build.props` / `Directory.Build.targets`: copies of the canonical realm-root pair (the harness refreshes them from `config/` on every run — never hand-maintained).
	- `schema/Directory.Build.props` / `schema/Directory.Build.targets`: refreshed copies of the canonical schema pair.
	- `src/Scratch.Rules/Scratch.Rules.csproj`: `netstandard2.0`, `<PackageReference Include="Microsoft.SqlServer.DacFx" Version="170.*" PrivateAssets="all" />`, `IsPackable=true`, and a packed `build/Norse.Scratch.Rules.targets` (sets `RunSqlCodeAnalysis=true` + one Contains-guarded promotion of `Scratch.Rules.NR0001`).
	- `src/Scratch.Rules/BanTableNamedForbidden.cs` — the minimal rule:

```csharp
using Microsoft.SqlServer.Dac.CodeAnalysis;
using Microsoft.SqlServer.Dac.Model;

namespace Norse.Scratch.Rules;

/// <summary>Fixture rule NR0001: convicts any table literally named <c>forbidden</c>.</summary>
[ExportCodeAnalysisRule(RuleId, "Fixture rule", Description = "Table name 'forbidden' is banned.", Category = "Fixture", RuleScope = SqlRuleScope.Element)]
public sealed class BanTableNamedForbidden : SqlCodeAnalysisRule
{
	public const string RuleId = "Scratch.Rules.NR0001";

	public BanTableNamedForbidden() =>
		SupportedElementTypes = [ModelSchema.Table];

	public override IList<SqlRuleProblem> Analyze(SqlRuleExecutionContext ruleExecutionContext) =>
		ruleExecutionContext.ModelElement?.Name?.Parts is [.., "forbidden"] ?
			[new SqlRuleProblem("Table name 'forbidden' is banned.", ruleExecutionContext.ModelElement)] :
			[];
}
```

	- `schema/Scratch.Database/Scratch.Database.sqlproj`: `<Sdk Name="Microsoft.Build.Sql" Version="..." />` — the `Sdk` element requires a literal version; resolve the latest stable carrying the DacFx `170.*` train at implementation time (`dotnet package search Microsoft.Build.Sql --exact-match`) and pin that. Then a `NorseRef` to `Scratch.Rules` with `<Rules>true</Rules>` and a second `NorseRef` carrying `<DatabaseSqlCmdVariable>ref_db</DatabaseSqlCmdVariable>` (passthrough probe), plus `dbo/tables/good.sql` and a toggleable `dbo/tables/forbidden.sql`.

- [ ] **Step 4: Write `scripts/verify-schema-templates.ps1`** asserting, in order (each a named assertion, `exit 1` on any failure):
	1. Refresh fixture copies from `config/`; evaluate the sqlproj with `-p:UseProjectReferences=true` (simulated workspace: the fixture realm root IS the top — pass `-p:_ParentTargets=__standalone__` is NOT used here; instead pass `-p:UseProjectReferences=true` explicitly): `RunSqlCodeAnalysis` evaluates `true`; `ProjectReference` to `src/Scratch.Rules/Scratch.Rules.csproj` present with `OutputItemType=Analyzer`; NO plain compile `ProjectReference` to it; `DatabaseSqlCmdVariable` metadata present on the second ref's emission.
	2. Build with `forbidden.sql` excluded → green. Include it → build FAILS with `NR0001` as an **error** (promotion proven), not a warning.
	3. Pack `Scratch.Rules` to a throwaway feed (`dotnet pack` + `dotnet nuget add source <temp>`), flip the sqlproj to package mode (`-p:UseProjectReferences=false` — fixture realm standalone → fallback `PackageReference`), restore, rebuild: NR0001 still errors (package crossing: packed `build/*.targets` did the enabling — parity proven).
	4. Stale-strip probe: with `UseProjectReferences=true` AND the package feed still registered plus a direct `PackageReference` to the packed `Norse.Scratch.Rules` added via `-p:` injection of an extra import — assert the evaluated `@(Analyzer)` after target execution (`dotnet build -bl` + `Norse.Scratch.Rules` filename count in the binlog, or `-getTargetResult:_NorseRemoveStaleTransitiveRules`) retains exactly one copy, the `MSBuildSourceProjectFile`-bearing one.

- [ ] **Step 5: Run — red first, then green**

Run with the templates' `RunSqlCodeAnalysis` block temporarily commented out: assertion 2 must FAIL (rules silently absent — reproducing the wall). Restore the block: `pwsh ../.github/scripts/verify-schema-templates.ps1` → exit 0, all four groups green.

- [ ] **Step 6: `config/manifest.psd1`** — add to `Groups`:

```powershell
		# Microsoft.Build.Sql schema projects — assigned to NO realm by default; a repo opts in
		# via its Exceptions entry the day it grows a {Realm}/schema/{Name}.Database project.
		# The platform's own persistence remains EF Core + Postgres constraints (Key Rejections);
		# this group exists for consumer bridges that chose SQL Server. the-runes.md ch. 8.
		schema      = @(
			'schema/Directory.Build.props'
			'schema/Directory.Build.targets'
		)
```

(`DefaultGroups` unchanged — `schema` is opt-in only.)

- [ ] **Step 7: Stage in Ginnungagap**

```bash
git -C ../.github add config/schema config/manifest.psd1 scripts/verify-schema-templates.ps1 scripts/verify-schema-fixtures
```

---

### Task 9: Ginnungagap README/CLAUDE.md pair

**Files:**
- Modify: `../.github/README.md`, `../.github/CLAUDE.md`

- [ ] **Step 1:** Update both files in the same change (boy-scout law): the realm-root targets' widened role (fallback + strip + manifest attach + seam), the two realm-owned unscattered files (`Directory.Analyzers.props`, `Directory.Realm.targets`) and the canonicity law, the `schema` group, the scatter guard/audit semantics (`Current`/`Stale`/`Divergent`/`LineageUnavailable`, `-AcceptDivergence`, full-history requirement), and the `verify-schema-templates` harness. Same story at two altitudes; every group listed must match `manifest.psd1` exactly.

- [ ] **Step 2:** Stage: `git -C ../.github add README.md CLAUDE.md`

---

### Task 10: `the-runes.md` — the doctrine page, absorption, and the referrer sweep

**Files:**
- Create: `Glitnir/docs/the-runes.md`
- Delete: `Glitnir/docs/the-two-crossings.md`, `Glitnir/docs/msbuild-deep-dive-docket.md`
- Modify: every referrer of the deleted paths (discovered in Step 3)

**Interfaces:**
- Consumes: spec §2.1 chapter structure; the absorbed two-crossings content; every mechanism landed in Tasks 2–8.

- [ ] **Step 1: Write `Glitnir/docs/the-runes.md`** with the spec's nine chapters (§2.1). Non-negotiable content per chapter: **(1) Why** — the `UseProjectReferences` thesis with the workspace-scoped phrasing and the recorded asymmetry; **(2) Layer map** — the four layers with an ownership table (hand-authored vs scattered vs realm-owned-unscattered); **(3) The two crossings** — the absorbed doctrine page nearly verbatim (mechanism, polarity table, "two lenses, one graph"), the polarity table gaining two rows: `build/*.targets auto-import` (package crossing only — general NuGet behavior) and `DacFx rule enablement` (rides the packed targets); **(4) Evaluation-order law** — the four lessons with their proofs (CPM/NU1008; SDK implicit usings; Import-at-properties-pass, wildcard carve-out; conditioned vs unconditional SDK defaults with the `IsPackable`/`OutputType` adjacent-lines example); **(5) Delivery matrix** — this table plus the provenance-strip doctrine (both polarities), the manifest mechanism, identity law, diagnostics-only law, and the three-suffix taxonomy:

| Delivered thing | Project-reference crossing | Package crossing |
|---|---|---|
| Compile reference (`NorseRef`) | `ProjectReference` into sibling `src/` | `PackageReference Norse.*` |
| Roslyn generator (`Generator="true"`) | explicit analyzer-shaped `ProjectReference` — never transitive | packed `analyzers/dotnet/cs`, propagates the whole closure (strip target reins it in) |
| Roslyn diagnostics (realm law, `.Analyzers`) | manifest-attached everywhere (workspace) / realm-internal (standalone) | packed into the owning library, propagates the closure |
| DacFx rules (`Rules="true"`) | analyzer-shaped `ProjectReference` + canonical `RunSqlCodeAnalysis` default + hardcoded promotion | packed `build/*.targets` self-enables and promotes |
| Packed `build/*.targets` logic | **never imported** | auto-imported |

**(6) Packaging and release** — MinVer, generator bundling, README/LICENSE, tag → artifacts → release, and the version-ownership asymmetry (floating libraries vs CPM-pinned composition root, the Sev1-from-the-root story); **(7) The scatter** — groups, canonicity law, the seam (additive-only), guard/audit outcomes; **(8) Schema projects** — the three walls and their canonical fixes, the platform-rejection boundary, the fixture; **(9) Postmortems and probes** — the Task 7 strip incident (carried from the absorbed page), `-p:DirectoryBuildTargetsPath` and `-p:_ParentTargets=__standalone__` probing, the throwaway-feed harness pattern, `Verify-Runes.ps1`.

- [ ] **Step 2: Delete the absorbed pages**

```bash
git -C Glitnir rm docs/the-two-crossings.md docs/msbuild-deep-dive-docket.md
```

- [ ] **Step 3: Referrer sweep** — find and update every pointer:

Run: `grep -rn "the-two-crossings\|msbuild-deep-dive-docket" --include="*.md" --include="*.targets" --include="*.props" . ../.github`
Update each hit to point at `the-runes.md` (the Bifröst root `Directory.Build.targets` comment was already updated in Task 3; Ginnungagap template comments in Tasks 4–5 — this step catches CLAUDE.md files, Glitnir docs, and anything missed). Re-run the grep; expected: zero hits outside `the-runes.md` itself and this plan/spec pair (historical documents cite historically — the spec and plan keep their references with "(deleted, absorbed into the-runes.md)" annotations added).

- [ ] **Step 4: Stage in Glitnir**

```bash
git -C Glitnir add -A docs
```

---

### Task 11: Bifröst, Midgard, Yggdrasil documentation pairs

**Files:**
- Modify: `README.md` + `CLAUDE.md` (Bifröst), `Midgard/CLAUDE.md`, `Yggdrasil/CLAUDE.md`

- [ ] **Step 1: Bifröst pair.** CLAUDE.md: new state-of-the-union entry (2026-08-07 — the estate consolidation, one paragraph in the established style); §5 conventions gains the pointer to `../Glitnir/docs/the-runes.md`; §8/§9 trimmed where the runes now carry the story. README: the toggle narrative told as the differentiator at public altitude — flip one property, develop the whole stack locally or behave like CI; stale submodule pointers can't hurt shipping software; tag → artifacts → release per realm. Both files' realm tables must still match `.gitmodules` exactly.

- [ ] **Step 2: Midgard CLAUDE.md** — the NORSE080 forwarding sentence (currently describing the realm-root hand block) now describes the manifest (`Directory.Analyzers.props`) + the two condition-disjoint attach sites. **Yggdrasil CLAUDE.md** — note the collapsed chain stubs (its variant files no longer carry logic; the CPM-aware law arrives via the scattered realm-root targets).

- [ ] **Step 3: Stage**

```bash
git -C . add README.md CLAUDE.md
git -C Midgard add CLAUDE.md
git -C Yggdrasil add CLAUDE.md
```

---

### Task 12: Final full-matrix verification

**Files:** none (verification only)

- [ ] **Step 1:** `pwsh scripts/Verify-Runes.ps1 -Phase final` — exit 0.
- [ ] **Step 2:** `dotnet build Bifrost.slnx` and `dotnet build Bifrost.slnx -p:UseProjectReferences=false` — both green, zero warnings.
- [ ] **Step 3:** `pwsh ../.github/scripts/tests/verify-rune-lineage.ps1` and `pwsh ../.github/scripts/verify-schema-templates.ps1` — both exit 0.
- [ ] **Step 4:** Lineage audit of the whole tree: for every scattered file in every realm working tree, `Get-RuneClassification` returns `Current` (zero `Divergent` anywhere — Midgard included).
- [ ] **Step 5:** Duplicate-identity sweep: across all aggregated manifests, assert no duplicate `NorseRealmAnalyzer` full paths and no duplicate assembly file names (a ten-line pwsh loop over `*/Directory.Analyzers.props`).

---

### Task 13: Ship runbook (human-gated; the implementer prepares, the human fires)

No file edits — this task converts the staged work into the spec §4 transition-safe ship order. Each numbered gate is: human commits the staged diff → PR on the realm's fork → CI green → merge (→ tag/publish where the realm ships):

1. **Svartálfheim + Midgard manifests** (Task 2 diffs). Inert; land first.
2. **Bifröst root targets + harness** (Tasks 1, 3 diffs — Bifröst commits on `master`, no branch, per repo law). Post-merge canary: `Verify-Runes -Phase post-aggregation` green from a fresh pull.
3. **Ginnungagap** (Tasks 4, 5, 7, 8, 9 diffs — one PR on its fork). Merge triggers scatter.
4. **Scatter run with `-AcceptDivergence 'Midgard/Directory.Build.targets'`** — the one deliberate reversion the guard must be told about. Merge the per-realm sync PRs (Midgard's contains the NORSE080 hand-block deletion). Post-merge canary: `Verify-Runes -Phase final` green from a fresh pull; scatter `-Audit` reports zero `Divergent`.
5. **Yggdrasil stub collapse** (Task 5's Yggdrasil diff) — lands via its fork (or rides its scatter sync PR where content is identical).
6. **Glitnir + doc pairs** (Tasks 10, 11 diffs).

The local working tree already proved every step's end state (Task 12); the runbook's per-gate canaries prove no transition window regressed it.

---

## Self-Review (performed at write time)

- **Spec coverage:** §2.1→Task 10; §2.2→Tasks 4, 5; §2.3→Task 5; §2.4→doc chapter (Task 10) with proofs already banked; §2.5→Tasks 2, 3, 4, 6, 12.5; §2.6→Tasks 4 (seam), 7 (guard/audit/lineage); §2.7→Tasks 3 (Rules=), 8 (templates+fixture); §2.8→Tasks 9, 10, 11; §4 ordering→Task 13; §4 matrix→Tasks 1, 12.
- **Known law conflict, surfaced per house-rules:** the planning skill's per-task commit steps are replaced by stage-only steps — the no-automatic-commits law wins.
- **Type consistency:** item type `NorseRealmAnalyzer`, helper item `_NorseRealmAnalyzerSelf`, targets `_NorseRemoveUnwantedGeneratorAnalyzers` / `_NorseRemoveStaleTransitiveRules`, classifier `Get-RuneClassification` with the four-state return — names match across all tasks.
- **Fixture rule caveat:** `Scratch.Rules` uses DacFx's `ExportCodeAnalysisRule` on the `170.*` train (ruled 2026-08-07 — the ancestor estate's `162.*` pin was a boat anchor from a legacy SQL Azure target, not a choice; this platform starts on the current train). Companion ruling (same day): the schema template's `DSP` defaults to `Sql170DatabaseSchemaProvider` — native SQL Server, assuming and forcing nothing about hosting; the ancestor's `SqlAzureV12` value was its own hosting choice, not template law. Azure consumers override in the consuming sqlproj. If the API surface differs at implementation time (`SupportedElementTypes` setter shape), adjust the fixture to the installed package's contract — the assertion surface (NR0001 errors when promoted, silent when not) is the contract, not the rule's internals. The fixture sqlproj's `Microsoft.Build.Sql` Sdk pin is deliberately unresolved in this plan — pin the current stable carrying the same DacFx `170.*` train at implementation time (Task 8 step 3 has the lookup command).
