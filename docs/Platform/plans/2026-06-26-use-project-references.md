# UseProjectReferences Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (default) or `superpowers:executing-plans` for a separate session with human review checkpoints. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire an MSBuild lever — `UseProjectReferences` — that swaps all inter-realm `<NorseRef>` items into `<ProjectReference>` (Bifrost local dev, default) or `<PackageReference>` (standalone CI / local CI triage) without touching individual project files.

**Architecture:** A static `<Choose>` block in `Bifrost/Directory.Build.targets` evaluates at MSBuild's item/property phase and transforms `<NorseRef>` / `<NorseDesignRef>` custom items. Each realm's `src/Directory.Build.targets` chains to Bifrost via `GetPathOfFileAbove` (two hops up: `{Realm}/src/` → `{Realm}/` → Bifrost root) and falls back to materialized `<PackageReference>` items when Bifrost is absent. `tests/Directory.Build.targets` is a separate, simpler file that only enforces `<OutputType>Exe</OutputType>` — test projects never carry cross-realm NorseRef items. CI workflows wire a composite action that registers the public GitHub Packages feed before any restore runs.

**Tech Stack:** MSBuild XML, GitHub Actions YAML (composite action + reusable workflow edits), PowerShell manifest (`manifest.psd1`).

## Global Constraints

- Tabs for indentation in all XML, YAML, and PowerShell files (YAML and Markdown use 2-space per `.editorconfig`).
- US English spelling everywhere — code, comments, file content.
- **No automatic git commits.** Every task ends with `git add <files>` inside the relevant submodule and a diff check — the human commits.
- `GetPathOfFileAbove` in `src/Directory.Build.targets` uses `$(MSBuildThisFileDirectory)../../` (two hops), never one.
- The composite action registers the GitHub Packages feed anonymously — public packages from public repos need no credentials.
- Yggdrasil is excluded from the `nuget` scatter group; it gets hand-authored CPM-variant targets files (no `Version` attribute on `<PackageReference>` items).
- `NorseRefVersion` = `*-*` in Bifrost props (latest prerelease); `*` hardcoded in the standalone realm fallback (stable only).
- Per-realm override on a feature branch: `-p:NorseRefVersion=*-my-feature` (developer-run, not committed).
- Spec: `../specs/2026-06-26-use-project-references-design.md`

---

## File Map

| Action | Path |
|---|---|
| Modify | `Bifrost/Directory.Build.props` |
| **Create** | `Bifrost/Directory.Build.targets` |
| **Create** | `Bifrost/nuget.config` |
| Modify | `.github/config/Directory.Build.props` (adds `UseProjectReferences=false`; scatter propagates to all dotnet-group realms) |
| **Create** | `.github/.github/actions/add-norse-nuget-source/action.yml` |
| Modify | `.github/.github/workflows/ci-build-test.yml` |
| Modify | `.github/.github/workflows/release-nuget.yml` |
| **Create** | `.github/config/src/Directory.Build.targets` |
| **Create** | `.github/config/tests/Directory.Build.targets` |
| **Create** | `Yggdrasil/src/Directory.Build.targets` (CPM variant — excluded from nuget scatter group) |
| **Create** | `Yggdrasil/tests/Directory.Build.targets` (excluded from nuget scatter group) |
| Modify | `.github/config/manifest.psd1` |
| Modify | `Asgard/src/Abstractions.Backend/Abstractions.Backend.csproj` |

---

## Task 1: Bifrost Property Defaults

**Files:**
- Modify: `Bifrost/Directory.Build.props`

**Interfaces:**
- Produces: `$(UseProjectReferences)` = `true` for all projects under Bifrost; `$(NorseRefVersion)` = `*-*` for package-reference mode under Bifrost.

- [ ] **Step 1: Add properties to Bifrost/Directory.Build.props**

  Current file (two properties, no blank line between):

  ```xml
  <Project>
  	<PropertyGroup>
  		<AssemblyName>Norse.$(MSBuildProjectName)</AssemblyName>
  		<RootNamespace>Norse.$(MSBuildProjectName)</RootNamespace>
  	</PropertyGroup>
  </Project>
  ```

  Replace with:

  ```xml
  <Project>
  	<PropertyGroup>
  		<AssemblyName>Norse.$(MSBuildProjectName)</AssemblyName>
  		<NorseRefVersion Condition="'$(NorseRefVersion)' == ''">*-*</NorseRefVersion>
  		<RootNamespace>Norse.$(MSBuildProjectName)</RootNamespace>
  		<UseProjectReferences Condition="'$(UseProjectReferences)' == ''">true</UseProjectReferences>
  	</PropertyGroup>
  </Project>
  ```

  Properties are in alphabetical order inside the group — `NorseRefVersion` before `RootNamespace`, `UseProjectReferences` last.

- [ ] **Step 2: Verify XML is valid**

  ```bash
  dotnet msbuild Bifrost/Orchestration.AppHost/Orchestration.AppHost.csproj -t:WriteLinesToFile -p:Lines="" -p:File=/dev/null 2>&1 | head -5
  ```

  Expected: no `error` lines (warnings about missing submodule content are OK at this stage).

- [ ] **Step 3: Stage**

  ```bash
  git -C /home/buvy/code/NorseArchitecture/Bifrost add Directory.Build.props
  git -C /home/buvy/code/NorseArchitecture/Bifrost diff --cached Directory.Build.props
  ```

---

## Task 2: Bifrost Choose Block

**Files:**
- Create: `Bifrost/Directory.Build.targets`

**Interfaces:**
- Consumes: `$(UseProjectReferences)`, `$(NorseRefVersion)`, `$(MSBuildThisFileDirectory)` (Bifrost root), `@(NorseRef)` items (Identity, Repo metadata), `@(NorseDesignRef)` items (Identity, Repo metadata).
- Produces: Either `<ProjectReference>` items or `<PackageReference>` items for every `NorseRef`/`NorseDesignRef` item in any project under Bifrost.

- [ ] **Step 1: Create Bifrost/Directory.Build.targets**

  ```xml
  <Project>
  	<Choose>
  		<When Condition="'$(UseProjectReferences)' == 'true'">
  			<ItemGroup>
  				<ProjectReference
  					Include="$(MSBuildThisFileDirectory)%(NorseRef.Repo)/src/%(NorseRef.Identity)/%(NorseRef.Identity).csproj" />
  				<ProjectReference
  					Include="$(MSBuildThisFileDirectory)%(NorseDesignRef.Repo)/src/%(NorseDesignRef.Identity)/%(NorseDesignRef.Identity).csproj">
  					<PrivateAssets>all</PrivateAssets>
  				</ProjectReference>
  			</ItemGroup>
  		</When>
  		<Otherwise>
  			<ItemGroup>
  				<PackageReference Include="@(NorseRef->'Norse.%(Identity)')" Version="$(NorseRefVersion)" />
  				<PackageReference Include="@(NorseDesignRef->'Norse.%(Identity)')" Version="$(NorseRefVersion)">
  					<PrivateAssets>all</PrivateAssets>
  				</PackageReference>
  			</ItemGroup>
  		</Otherwise>
  	</Choose>
  </Project>
  ```

  Key points:
  - This is a static `<Choose>` block, not a `<Target>`. It evaluates during the item/property phase so ProjectReference items are visible to the dependency graph (YGG301 compliant).
  - `$(MSBuildThisFileDirectory)` here resolves to the Bifrost root — the directory containing this file.
  - `NorseDesignRef` items carry `<PrivateAssets>all</PrivateAssets>` in both branches — they are design-time-only dependencies (analyzers, source generators) that must not flow transitively.

- [ ] **Step 2: Verify XML is well-formed**

  ```bash
  dotnet msbuild Bifrost/Orchestration.AppHost/Orchestration.AppHost.csproj -t:WriteLinesToFile -p:Lines="" -p:File=/dev/null 2>&1 | grep -i error | head -10
  ```

  Expected: no MSBuild errors. The Choose block with empty NorseRef collections is a legal no-op.

- [ ] **Step 3: Stage**

  ```bash
  git -C /home/buvy/code/NorseArchitecture/Bifrost add Directory.Build.targets
  git -C /home/buvy/code/NorseArchitecture/Bifrost diff --cached Directory.Build.targets
  ```

---

## Task 3: Bifrost nuget.config

**Files:**
- Create: `Bifrost/nuget.config`

**Interfaces:**
- Produces: NuGet source resolution for `Norse.*` packages during `dotnet restore` when `UseProjectReferences=false` inside Bifrost. NuGet walks parent directories for `nuget.config`; this file is found for all submodule builds under Bifrost.

- [ ] **Step 1: Create Bifrost/nuget.config**

  ```xml
  <?xml version="1.0" encoding="utf-8"?>
  <configuration>
  	<packageSources>
  		<add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  		<add key="github" value="https://nuget.pkg.github.com/NorseArchitecture/index.json" />
  	</packageSources>
  </configuration>
  ```

  This file is Bifrost-only. It is never scattered to realm repos. The GitHub Packages feed is anonymous-read for packages from public repos — no credentials required.

- [ ] **Step 2: Verify restore can see both sources**

  ```bash
  dotnet nuget list source --configfile Bifrost/nuget.config
  ```

  Expected output includes both `nuget.org` and `github` sources, both `[Enabled]`.

- [ ] **Step 3: Stage**

  ```bash
  git -C /home/buvy/code/NorseArchitecture/Bifrost add nuget.config
  git -C /home/buvy/code/NorseArchitecture/Bifrost diff --cached nuget.config
  ```

---

## Task 4: Realm Root Property Default — Canonical Source

**Files:**
- Modify: `.github/config/Directory.Build.props`

**Interfaces:**
- Consumes: the canonical root props file that the `dotnet` scatter group propagates to all dotnet-group realms (Svartalfheim, Asgard, Midgard, Urdarbrunnr, Ratatoskr, Himinbjorg, Heimdall, Yggdrasil).
- Produces: `$(UseProjectReferences)` defaults to `false` in standalone builds; Bifrost's `Directory.Build.props` overrides it to `true` via the new parent-chain import. Propagation to realm repos is handled by `scatter-the-runes` — no direct realm submodule edits.

Two changes are needed: (1) a parent-chain import so Bifrost's props can override the realm's default; (2) the `UseProjectReferences=false` default with its explanatory comment. Without the import, Bifrost's `Directory.Build.props` is never seen by realm project builds — MSBuild's auto-import only picks up the closest `Directory.Build.props` walking up from each project file, which is `{Realm}/src/Directory.Build.props`. The realm root (the outermost level of the realm's chain) must explicitly walk one more level to reach Bifrost.

The import uses the same `_ParentProps` intermediate-property pattern as `src/Directory.Build.targets` uses for `_BifrostTargets`. It goes at the top of the file (before the PropertyGroup) so Bifrost's values are set first; the realm's Condition-guarded defaults then safely no-op.

- [ ] **Step 1: Add parent-chain import and UseProjectReferences to .github/config/Directory.Build.props**

  Replace the existing file content with:

  ```xml
  <Project>
  	<PropertyGroup>
  		<_ParentProps>$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))</_ParentProps>
  	</PropertyGroup>
  	<Import Project="$(_ParentProps)" Condition="Exists('$(_ParentProps)')" />
  	<PropertyGroup>
  		<!--
  			Analyzer tiers follow the platform baseline: Security/Performance/Reliability/Usage
  			at latest-All; Design stays at the global baseline because latest-All enables rules
  			(e.g. CA1034) that conflict with discriminated-union-style type shapes.
  		-->
  		<AnalysisLevel>latest-Recommended</AnalysisLevel>
  		<AnalysisLevelSecurity>latest-All</AnalysisLevelSecurity>
  		<AnalysisLevelPerformance>latest-All</AnalysisLevelPerformance>
  		<AnalysisLevelReliability>latest-All</AnalysisLevelReliability>
  		<AnalysisLevelUsage>latest-All</AnalysisLevelUsage>
  		<!--
  			The brand prefix lives here and nowhere else: project folders and .csproj files are
  			brand-free, so a fork rebrands by changing "Norse" once per realm — no file renames.
  		-->
  		<AssemblyName>Norse.$(MSBuildProjectName)</AssemblyName>
  		<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  		<GenerateDocumentationFile>true</GenerateDocumentationFile>
  		<ImplicitUsings>enable</ImplicitUsings>
  		<LangVersion>preview</LangVersion>
  		<Nullable>enable</Nullable>
  		<RootNamespace>Norse.$(MSBuildProjectName)</RootNamespace>
  		<TargetFramework>net11.0</TargetFramework>
  		<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  		<!-- Default to false unless the directory above overrides i.e. bifrost is loaded -->
  		<UseProjectReferences Condition="'$(UseProjectReferences)' == ''">false</UseProjectReferences>
  		<WarningLevel>9999</WarningLevel>
  	</PropertyGroup>
  </Project>
  ```

  How this works at runtime:
  - **Bifrost context** (`{Realm}/` sits one level below Bifrost root): `GetPathOfFileAbove` from `{Realm}/` with `../` finds `Bifrost/Directory.Build.props` → import fires → Bifrost sets `UseProjectReferences=true` → realm's Condition is false → no-op.
  - **Standalone CI** (realm is the checkout root): `GetPathOfFileAbove` walks above the checkout root, finds nothing, returns empty → import skipped → `UseProjectReferences=false` condition is true → property set.

- [ ] **Step 2: Stage**

  ```bash
  git -C /home/buvy/code/NorseArchitecture/.github add config/Directory.Build.props
  git -C /home/buvy/code/NorseArchitecture/.github diff --cached config/Directory.Build.props
  ```

  Scatter propagates this to all dotnet-group realms on the next push to `.github` master — no manual realm submodule edits needed.

---

## Task 5: Composite Action

**Files:**
- Create: `.github/.github/actions/add-norse-nuget-source/action.yml`

**Interfaces:**
- Produces: a reusable composite action callable as `uses: NorseArchitecture/.github/.github/actions/add-norse-nuget-source@master` from any reusable workflow or realm CI workflow.

- [ ] **Step 1: Create the actions directory and action.yml**

  ```bash
  mkdir -p /home/buvy/code/NorseArchitecture/.github/.github/actions/add-norse-nuget-source
  ```

  Create `.github/.github/actions/add-norse-nuget-source/action.yml`:

  ```yaml
  name: Add Norse NuGet Source
  description: Registers the NorseArchitecture GitHub Packages feed — anonymous read, no credentials required.

  runs:
    using: composite
    steps:
      - name: Add Norse NuGet source
        shell: bash
        run: dotnet nuget add source "https://nuget.pkg.github.com/NorseArchitecture/index.json" --name github
  ```

- [ ] **Step 2: Stage**

  ```bash
  git -C /home/buvy/code/NorseArchitecture/.github add .github/actions/add-norse-nuget-source/action.yml
  git -C /home/buvy/code/NorseArchitecture/.github diff --cached
  ```

---

## Task 6: Workflow Changes

**Files:**
- Modify: `.github/.github/workflows/ci-build-test.yml`
- Modify: `.github/.github/workflows/release-nuget.yml`

**Interfaces:**
- Consumes: composite action from Task 5 at `NorseArchitecture/.github/.github/actions/add-norse-nuget-source@master`.
- Produces: NuGet source registered before every `dotnet restore` across CI and release workflows. Realms that use `PackageReference` for cross-realm deps (standalone CI) can resolve `Norse.*` packages from GitHub Packages.

### ci-build-test.yml

The current `build` job has this step sequence: Checkout → Setup .NET → **Restore** → Build → Test → ...

Add the composite action step **between** Setup .NET and Restore:

- [ ] **Step 1: Add source step to ci-build-test.yml**

  After:
  ```yaml
        - name: Setup .NET
          uses: actions/setup-dotnet@v5
          with:
            dotnet-version: "11.0.x"
            dotnet-quality: "preview"
  ```

  Insert:
  ```yaml
        - name: Add Norse NuGet source
          uses: NorseArchitecture/.github/.github/actions/add-norse-nuget-source@master
  ```

  So the sequence becomes: Checkout → Setup .NET → **Add Norse NuGet source** → Restore → Build → Test → ...

### release-nuget.yml

The `codeql` job runs `dotnet restore` inline. The `pack-and-publish` job also runs `dotnet restore`. Both run on fresh runners. Both need the source registered.

- [ ] **Step 2: Add source step to codeql job in release-nuget.yml**

  In the `codeql` job, after the Setup .NET step and before the "Restore and build" step, insert:

  ```yaml
        - name: Add Norse NuGet source
          uses: NorseArchitecture/.github/.github/actions/add-norse-nuget-source@master
  ```

  The step sequence in `codeql` becomes: Checkout → Setup .NET → Initialize CodeQL → **Add Norse NuGet source** → Restore and build → Analyze.

  Note: the composite action step goes AFTER Initialize CodeQL because CodeQL initialization must precede the build it instruments.

- [ ] **Step 3: Add source step to pack-and-publish job in release-nuget.yml**

  In the `pack-and-publish` job, after the Setup .NET step and before the Restore step, insert the same step:

  ```yaml
        - name: Add Norse NuGet source
          uses: NorseArchitecture/.github/.github/actions/add-norse-nuget-source@master
  ```

  The step sequence in `pack-and-publish` becomes: Checkout → Setup .NET → **Add Norse NuGet source** → Restore → Pack → Generate SBOM → Push → Release.

- [ ] **Step 4: Stage both workflow files**

  ```bash
  git -C /home/buvy/code/NorseArchitecture/.github add .github/workflows/ci-build-test.yml
  git -C /home/buvy/code/NorseArchitecture/.github add .github/workflows/release-nuget.yml
  git -C /home/buvy/code/NorseArchitecture/.github diff --cached
  ```

---

## Task 7: Canonical Targets Files in .github

**Files:**
- Create: `.github/config/src/Directory.Build.targets`
- Create: `.github/config/tests/Directory.Build.targets`

**Interfaces:**
- Produces: canonical files that `scatter-the-runes.ps1` will propagate to all nuget-group realms when Task 9 (manifest uncomment) takes effect.

### config/src/Directory.Build.targets

This file is placed in every nuget-group realm's `src/` directory. It:
1. Forces `<OutputType>Library</OutputType>` (targets file > project file — guarantees all `src/` projects are class libraries).
2. Chains up to Bifrost's `Directory.Build.targets` when present (local dev, project-reference mode).
3. Falls back to materialized `<PackageReference>` items when Bifrost is absent (standalone CI, package-reference mode). The fallback version is `*` (stable only — no prereleases in isolated realm builds).

- [ ] **Step 1: Create config/src/Directory.Build.targets**

  ```xml
  <Project>
  	<PropertyGroup>
  		<OutputType>Library</OutputType>
  	</PropertyGroup>
  	<PropertyGroup>
  		<_BifrostTargets>
  			$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../../'))
  		</_BifrostTargets>
  	</PropertyGroup>
  	<Import Project="$(_BifrostTargets)" Condition="Exists('$(_BifrostTargets)')" />
  	<ItemGroup Condition="!Exists('$(_BifrostTargets)')">
  		<PackageReference Include="@(NorseRef->'Norse.%(Identity)')" Version="*" />
  		<PackageReference Include="@(NorseDesignRef->'Norse.%(Identity)')">
  			<PrivateAssets>all</PrivateAssets>
  		</PackageReference>
  	</ItemGroup>
  </Project>
  ```

  The `../../` in `GetPathOfFileAbove` walks up two directories: from `{Realm}/src/` to `{Realm}/` to the Bifrost root. One `../` would stop at `{Realm}/`, which has no `Directory.Build.targets`.

### config/tests/Directory.Build.targets

Test projects never carry `NorseRef` items — they reference source projects within their own realm only. This file just reinforces `OutputType=Exe` at the targets layer (stronger guarantee than props, since targets evaluate after the project file).

- [ ] **Step 2: Create config/tests/Directory.Build.targets**

  ```xml
  <Project>
  	<PropertyGroup>
  		<OutputType>Exe</OutputType>
  	</PropertyGroup>
  </Project>
  ```

- [ ] **Step 3: Stage both files**

  ```bash
  git -C /home/buvy/code/NorseArchitecture/.github add config/src/Directory.Build.targets
  git -C /home/buvy/code/NorseArchitecture/.github add config/tests/Directory.Build.targets
  git -C /home/buvy/code/NorseArchitecture/.github diff --cached config/
  ```

---

## Task 8: Yggdrasil CPM Targets Files

**Files:**
- Create: `Yggdrasil/src/Directory.Build.targets` (CPM variant)
- Create: `Yggdrasil/tests/Directory.Build.targets`

Yggdrasil uses Central Package Management (CPM) — `<PackageReference>` items carry no `Version` attribute; versions are supplied by `Directory.Packages.props`. The standalone fallback in Yggdrasil's `src/Directory.Build.targets` therefore omits `Version` entirely from the generated `<PackageReference>` items.

The `tests/Directory.Build.targets` is structurally identical to the canonical version (just `OutputType=Exe`).

- [ ] **Step 1: Create Yggdrasil/src/Directory.Build.targets (CPM variant)**

  ```xml
  <Project>
  	<PropertyGroup>
  		<OutputType>Library</OutputType>
  	</PropertyGroup>
  	<PropertyGroup>
  		<_BifrostTargets>
  			$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../../'))
  		</_BifrostTargets>
  	</PropertyGroup>
  	<Import Project="$(_BifrostTargets)" Condition="Exists('$(_BifrostTargets)')" />
  	<ItemGroup Condition="!Exists('$(_BifrostTargets)')">
  		<PackageReference Include="@(NorseRef->'Norse.%(Identity)')" />
  		<PackageReference Include="@(NorseDesignRef->'Norse.%(Identity)')">
  			<PrivateAssets>all</PrivateAssets>
  		</PackageReference>
  	</ItemGroup>
  </Project>
  ```

  Difference from the canonical: no `Version="*"` on the `PackageReference` items in the fallback block — CPM's `Directory.Packages.props` supplies versions.

- [ ] **Step 2: Create Yggdrasil/tests/Directory.Build.targets**

  ```xml
  <Project>
  	<PropertyGroup>
  		<OutputType>Exe</OutputType>
  	</PropertyGroup>
  </Project>
  ```

- [ ] **Step 3: Stage**

  ```bash
  git -C /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil add src/Directory.Build.targets tests/Directory.Build.targets
  git -C /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil diff --cached
  ```

---

## Task 9: manifest.psd1 — Activate Scatter

**Files:**
- Modify: `.github/config/manifest.psd1`

**Interfaces:**
- Produces: the `nuget` group now includes `src/Directory.Build.targets` and `tests/Directory.Build.targets`; the next push to `.github`'s `config/**` fans both files out to all nuget-group realms. Also re-fans `Directory.Build.props` (root, via the `dotnet` group) with the new `UseProjectReferences` property from Task 4.

- [ ] **Step 1: Uncomment the two reserved lines**

  In `.github/config/manifest.psd1`, replace:

  ```powershell
  		# 'src/Directory.Build.targets'   # UseProjectReferences — pending
  		# 'tests/Directory.Build.targets' # UseProjectReferences — pending
  ```

  With:

  ```powershell
  		'src/Directory.Build.targets'
  		'tests/Directory.Build.targets'
  ```

- [ ] **Step 2: Stage**

  ```bash
  git -C /home/buvy/code/NorseArchitecture/.github add config/manifest.psd1
  git -C /home/buvy/code/NorseArchitecture/.github diff --cached config/manifest.psd1
  ```

---

## Task 10: Migrate Asgard Cross-Realm Reference to NorseRef

**Files:**
- Modify: `Asgard/src/Abstractions.Backend/Abstractions.Backend.csproj`

**Interfaces:**
- Consumes: `NorseRef` custom item type resolved by `Bifrost/Directory.Build.targets` (Task 2) and the realm `src/Directory.Build.targets` chain-import (deployed by scatter after Task 9). `Include` = project name = package suffix = `.csproj` filename. `<Repo>` = submodule folder under Bifrost.
- Produces: in Bifrost (project-reference mode): `<ProjectReference Include="{BifrostRoot}/Svartalfheim/src/Primitives/Primitives.csproj" />`; in standalone CI: `<PackageReference Include="Norse.Primitives" Version="*" />`.

This is the only cross-realm reference in the current codebase. All future cross-realm references must use `NorseRef` — the raw `<ProjectReference>` to a sibling repo path is now forbidden.

- [ ] **Step 1: Replace the raw cross-realm ProjectReference with NorseRef**

  Current `Abstractions.Backend.csproj`:

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
  	<PropertyGroup>
  		<Description>Norse server-side shared contracts, visible to both Worker and Web.Server. ...</Description>
  	</PropertyGroup>
  	<ItemGroup>
  		<ProjectReference Include="../Abstractions.Contracts/Abstractions.Contracts.csproj" />
  		<ProjectReference Include="../../../Svartalfheim/src/Primitives/Primitives.csproj" />
  	</ItemGroup>
  </Project>
  ```

  Replace the `../../../Svartalfheim/...` line:

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
  	<PropertyGroup>
  		<Description>Norse server-side shared contracts, visible to both Worker and Web.Server. ...</Description>
  	</PropertyGroup>
  	<ItemGroup>
  		<NorseRef Include="Primitives">
  			<Repo>Svartalfheim</Repo>
  		</NorseRef>
  		<ProjectReference Include="../Abstractions.Contracts/Abstractions.Contracts.csproj" />
  	</ItemGroup>
  </Project>
  ```

  Note: `NorseRef` is placed before `ProjectReference` — cross-realm deps before within-realm deps, alphabetically by element name.

- [ ] **Step 2: Stage**

  ```bash
  git -C /home/buvy/code/NorseArchitecture/Bifrost/Asgard add src/Abstractions.Backend/Abstractions.Backend.csproj
  git -C /home/buvy/code/NorseArchitecture/Bifrost/Asgard diff --cached
  ```

---

## Task 11: Verification

All prior tasks must be staged before this task runs. Note: realm `src/Directory.Build.targets` files land in nuget-group realms via scatter after the `.github` changes are pushed and merged — steps below account for this.

- [ ] **Step 1: Build Asgard in project-reference mode (Bifrost default)**

  In Bifrost mode, MSBuild walks up from `Asgard/src/Abstractions.Backend/` and finds `Bifrost/Directory.Build.targets` directly (there is no `Asgard/src/Directory.Build.targets` yet — scatter hasn't run). The Choose block fires, `UseProjectReferences=true`, and `NorseRef` resolves to a `ProjectReference`.

  ```bash
  dotnet build /home/buvy/code/NorseArchitecture/Bifrost/Asgard/Asgard.slnx -c Release
  ```

  Expected: zero errors. `NorseRef Include="Primitives"` resolves to `ProjectReference` at `{BifrostRoot}/Svartalfheim/src/Primitives/Primitives.csproj`. All warnings-as-errors must pass.

- [ ] **Step 2: Verify project-reference mode MSBuild evaluation**

  ```bash
  dotnet msbuild /home/buvy/code/NorseArchitecture/Bifrost/Asgard/src/Abstractions.Backend/Abstractions.Backend.csproj \
    -t:WriteLinesToFile -p:Lines="" -p:File=/dev/null \
    -verbosity:diag 2>&1 | grep -E "NorseRef|ProjectReference|PackageReference" | head -30
  ```

  Expected: `ProjectReference` resolved to the Svartalfheim path; no `PackageReference Norse.Primitives`.

- [ ] **Step 3: Verify package-reference mode MSBuild evaluation**

  ```bash
  dotnet msbuild /home/buvy/code/NorseArchitecture/Bifrost/Asgard/src/Abstractions.Backend/Abstractions.Backend.csproj \
    -p:UseProjectReferences=false \
    -t:WriteLinesToFile -p:Lines="" -p:File=/dev/null \
    -verbosity:diag 2>&1 | grep -E "NorseRef|ProjectReference|PackageReference" | head -30
  ```

  Expected: `PackageReference Norse.Primitives` with version `*-*`; no `ProjectReference` to the Svartalfheim path. The realm root props default (`UseProjectReferences=false`) is overridden by Bifrost's props (`true`) in normal Bifrost mode, so passing `-p:UseProjectReferences=false` simulates a standalone CI run.

- [ ] **Step 4: Build Asgard in package-reference mode (CI triage)**

  Requires `Norse.Primitives` to be published on GitHub Packages (i.e., Svartalfheim has a tagged release).

  ```bash
  dotnet build /home/buvy/code/NorseArchitecture/Bifrost/Asgard/Asgard.slnx \
    -c Release -p:UseProjectReferences=false
  ```

  Expected: zero errors. NuGet restore resolves `Norse.Primitives *-*` from the GitHub Packages feed registered in `Bifrost/nuget.config`.

  If no package exists yet: skip and note in the PR. Step 3 is sufficient proof the toggle wires correctly.

- [ ] **Step 5: Run Asgard tests in project-reference mode**

  ```bash
  dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Asgard/Asgard.slnx --no-build -c Release
  ```

  Expected: all tests pass (scaffolded test projects with zero tests are acceptable — runner must not error).

- [ ] **Step 6: Inspect git status across all touched repos**

  ```bash
  for repo in Bifrost Asgard Yggdrasil; do
    echo "=== $repo ===" && git -C /home/buvy/code/NorseArchitecture/Bifrost/$repo status --short
  done
  echo "=== .github ===" && git -C /home/buvy/code/NorseArchitecture/.github status --short
  ```

  Expected: only staged files (`A` or `M` in the index column) in Bifrost, Asgard, Yggdrasil, and `.github`. The nuget-group realms (Svartalfheim, Midgard, Urdarbrunnr, Ratatoskr, Himinbjorg, Heimdall) will be untouched locally — scatter opens PRs in those repos after `.github` is pushed.

---

## Adding Cross-Realm Dependencies Going Forward

When a project in realm X needs a type from realm Y:

1. Add to the project's `.csproj`:
   ```xml
   <NorseRef Include="{ProjectName}">
     <Repo>{RealmFolder}</Repo>
   </NorseRef>
   ```
   - `{ProjectName}` = the brand-free project folder/csproj name (e.g., `Primitives`, `Abstractions.Contracts`).
   - `{RealmFolder}` = the submodule folder under Bifrost (e.g., `Svartalfheim`, `Asgard`).
   - `NorseDesignRef` instead of `NorseRef` if the dependency must not flow transitively (analyzers, source generators, design-time tools).

2. No other file changes needed — the Choose block resolves the item automatically in both modes.

3. On a feature branch with unpublished packages: override the version with `-p:NorseRefVersion=*-my-feature` at the command line. Do not commit this override.
