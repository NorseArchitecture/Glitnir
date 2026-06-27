# UseProjectReferences — Cross-Realm Reference Switching

**Date:** 2026-06-26
**Status:** Approved in session 2026-06-26.
**Owner:** Buvy
**Companion specs:** `2026-06-19-ci-release-pipeline-design.md` (the CI gate this feature proves against; §4 item 6 explicitly gates on this design landing first); `2026-06-26-platform-config-sync-design.md` (the scatter mechanism that fans canonical targets files to all realms; §11 reserves the extension points this design fills); `2026-06-05-build-enforcement-design.md` (`Directory.Build.targets` reserved for this session in §4).

---

## 1. Context

Inter-realm references in the Bifrost working tree currently use raw `<ProjectReference>` items pointing to sibling submodule paths (e.g., `../../../Svartalfheim/src/Primitives/Primitives.csproj`). This works during local development in the full Bifrost peer tree but breaks in two situations:

1. **CI per-realm builds.** Each realm's CI gate checks out only that realm — sibling submodules are absent. Raw `<ProjectReference>` paths resolve to nothing.
2. **Local CI triage.** A developer cannot reproduce a CI failure by building in package mode; there is no lever to flip.

This design settles the full shape of the switching machinery: a single property (`UseProjectReferences`) drives a `<Choose>` block in Bifrost's `Directory.Build.targets`; realm projects declare cross-realm dependencies via typed items (`<NorseRef>`) rather than raw paths; canonical targets files propagated via `scatter-the-runes` handle the standalone fallback when Bifrost is absent from the tree.

The CI release design (§4 item 6) was an explicit prerequisite. It is now satisfied.

---

## 2. Rulings

### 2.1 The NorseRef Item Convention

Two item types replace raw cross-realm `<ProjectReference>` paths. Both live in **static `<ItemGroup>` blocks in csproj files — never inside `<Target>` blocks.** YGG301 is the standing law and holds here without exception.

**`<NorseRef>`** — a cross-realm platform dependency that participates in downstream consumers' restore graphs.

```xml
<NorseRef Include="Primitives">
  <Repo>Svartalfheim</Repo>
</NorseRef>
```

**`<NorseDesignRef>`** — a cross-realm design system dependency emitted with `<PrivateAssets>all</PrivateAssets>`, preventing it from flowing as a transitive dependency to consumers of the declaring assembly.

```xml
<NorseDesignRef Include="DesignSystem">
  <Repo>Naglfar</Repo>
</NorseDesignRef>
```

**The naming contract — four axes, one value:**

| Axis | Meaning | Example |
|---|---|---|
| `Include` | brand-free project folder name under `src/` | `Primitives` |
| `Include` | `.csproj` filename (brand-free) | `Primitives.csproj` |
| `Include` | NuGet package name suffix after `Norse.` | `Norse.Primitives` |
| `<Repo>` | repository folder name under the Bifrost root | `Svartalfheim` |

Project reference resolves to: `{BifrostRoot}/{Repo}/src/{Include}/{Include}.csproj`
Package reference resolves to: `Norse.{Include}`

**Single-project repos** (Svartalfheim today): `Include` and `Repo` differ in value but both resolve correctly — `Include="Primitives"`, `Repo="Svartalfheim"`.

**Multi-project repos** (Asgard): `Repo` is shared; `Include` identifies the specific project. Multiple `<NorseRef>` items may share the same `<Repo>`:

```xml
<NorseRef Include="Abstractions.Contracts">
  <Repo>Asgard</Repo>
</NorseRef>
<NorseRef Include="Abstractions.Backend">
  <Repo>Asgard</Repo>
</NorseRef>
```

**Scope rule.** `<NorseRef>` and `<NorseDesignRef>` are for **cross-realm** references only. References between projects within the same realm remain plain `<ProjectReference>` items permanently. A `<NorseRef>` that points at a project in the same realm is a misuse and fails code review.

### 2.2 The Property Layer

Two properties control the switching behavior. Both are set by condition so they can be overridden from the command line or environment without modifying any file.

**`UseProjectReferences`** — the mode toggle.

| Context | Value | Set by |
|---|---|---|
| Bifrost peer tree | `true` | Bifrost `Directory.Build.props` |
| Standalone realm (CI) | `false` (default) | Realm root `Directory.Build.props` |
| Local CI triage | `false` (override) | `-p:UseProjectReferences=false` on the CLI |

**`NorseRefVersion`** — the version wildcard for package mode inside the Bifrost peer tree.

| Context | Value | Set by |
|---|---|---|
| Bifrost peer tree, package mode | `*-*` | Bifrost `Directory.Build.props` |
| Standalone realm canonical fallback | `*` (hardcoded in the targets file) | — |

`NorseRefVersion` is only consulted by Bifrost's `Directory.Build.targets`. The standalone fallback in the realm targets hardcodes `Version="*"` and never reads this property. `*-*` includes prerelease packages; `*` matches stable releases only — correct for each context.

**Bifrost `Directory.Build.props` additions:**

```xml
<PropertyGroup>
  <!-- existing: AssemblyName, RootNamespace -->
  <UseProjectReferences Condition="'$(UseProjectReferences)' == ''">true</UseProjectReferences>
  <NorseRefVersion Condition="'$(NorseRefVersion)' == ''">*-*</NorseRefVersion>
</PropertyGroup>
```

**Canonical realm root `Directory.Build.props` — two additions:**

The canonical root props (`.github/config/Directory.Build.props`, scattered to all `dotnet`-group realms) requires a parent-chain import in addition to the `UseProjectReferences` property. MSBuild's auto-import mechanism finds only the **closest** `Directory.Build.props` walking up from each project file — for a project under `{Realm}/src/`, that is `{Realm}/src/Directory.Build.props`. That file chains to the realm root, but the realm root does not automatically continue to Bifrost. Without an explicit import, Bifrost's `Directory.Build.props` is never read during a realm build, and `UseProjectReferences=true` never reaches the realm's MSBuild evaluation.

The fix is the same `_ParentProps` intermediate-property pattern used by `src/Directory.Build.targets` for `_BifrostTargets`: walk one directory above the realm root at evaluation time. In Bifrost context that resolves to `Bifrost/Directory.Build.props`; in a standalone CI checkout the parent directory has no `Directory.Build.props` and the import is skipped.

```xml
<Project>
  <PropertyGroup>
    <_ParentProps>$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))</_ParentProps>
  </PropertyGroup>
  <Import Project="$(_ParentProps)" Condition="Exists('$(_ParentProps)')" />
  <PropertyGroup>
    <!-- existing analyzer config, AssemblyName, etc. -->
    <!-- Default to false unless the directory above overrides i.e. bifrost is loaded -->
    <UseProjectReferences Condition="'$(UseProjectReferences)' == ''">false</UseProjectReferences>
    <!-- WarningLevel -->
  </PropertyGroup>
</Project>
```

How this resolves at runtime:

- **Bifrost context** — `GetPathOfFileAbove` from `{Realm}/` with `../` reaches the Bifrost root and finds `Bifrost/Directory.Build.props`. Import fires. Bifrost sets `UseProjectReferences=true`. The realm's Condition is `false` — no-op.
- **Standalone CI** — `GetPathOfFileAbove` walks above the checkout root, finds nothing, returns empty. Import skipped. Realm sets `UseProjectReferences=false`.

### 2.3 Bifrost `Directory.Build.targets` — The Choose Block

Bifrost's root `Directory.Build.targets` (reserved since the build-enforcement session, currently empty) receives the `<Choose>` block. This is the **only** location the Choose block lives — it is never duplicated in realm targets files.

The static `<Choose>` construct is NOT a `<Target>` block. It is evaluated during MSBuild's property and item phase, before any target execution. `<ProjectReference>` items declared within a static `<Choose>` are visible to the dependency graph resolver. YGG301 is not violated.

```xml
<Project>
  <!--
    Resolves NorseRef and NorseDesignRef items into the appropriate MSBuild reference
    type based on UseProjectReferences.

    Project mode  (UseProjectReferences=true, Bifrost peer tree):
      Paths are resolved from $(MSBuildThisFileDirectory), which is the Bifrost root.
      No separate $(Bifrost) property is needed — the targets file knows where it lives.

    Package mode  (UseProjectReferences=false, local CI triage from Bifrost):
      Uses $(NorseRefVersion), which defaults to *-* (latest including prerelease).
      To pin a specific feature prerelease suffix: -p:NorseRefVersion=*-my-feature.

    Standalone realm (CI):
      This file is not found. The realm's src/Directory.Build.targets handles resolution
      via its standalone fallback.
  -->
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

### 2.4 Canonical `src/Directory.Build.targets` — Chain-or-Fallback

This file is authored in the `.github` repo at `config/src/Directory.Build.targets` and propagated to all `nuget`-group realms by `scatter-the-runes`. It fills the reserved slot from §11 of the sync design.

```xml
<Project>
  <PropertyGroup>
    <OutputType>Library</OutputType>
  </PropertyGroup>

  <!--
    Walk up two directories from the realm's src/ folder to find Bifrost/Directory.Build.targets
    when cloned as a Bifrost submodule. One hop reaches {Repo}/; a second reaches Bifrost/.
    No realm carries a root-level Directory.Build.targets, so Bifrost's is the first hit.
    Returns an empty string when the realm is cloned standalone.
  -->
  <PropertyGroup>
    <_BifrostTargets>
      $([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../../'))
    </_BifrostTargets>
  </PropertyGroup>

  <!-- Full peer tree: Bifrost handles all resolution via its Choose block. -->
  <Import Project="$(_BifrostTargets)" Condition="Exists('$(_BifrostTargets)')" />

  <!--
    Standalone fallback: materialize package refs directly from NorseRef declarations.
    UseProjectReferences=false by default on standalone clones, so the project ref
    branch of the Bifrost Choose block is never needed here.
    Version="*" — stable releases only, which is all CI should consume.
    If @(NorseRef) or @(NorseDesignRef) are empty (no items declared), the item
    transforms produce nothing. This is the correct behavior for Svartalfheim.
  -->
  <ItemGroup Condition="!Exists('$(_BifrostTargets)')">
    <PackageReference Include="@(NorseRef->'Norse.%(Identity)')" Version="*" />
    <PackageReference Include="@(NorseDesignRef->'Norse.%(Identity)')" Version="*">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

**Why no Choose block in this file.** The standalone fallback only ever runs with `UseProjectReferences=false` (the realm's props default). There is no scenario where a standalone clone needs to emit `<ProjectReference>` items — the sibling realms simply do not exist. The `Condition="!Exists(...)"` form is correct and sufficient.

**Why `OutputType=Library` here and not in `src/Directory.Build.props`.** Targets files evaluate after the project file; props files evaluate before. Setting `OutputType` in a targets file gives it highest-static precedence. Projects that require a different output type — worker host, web application — do so via their SDK choice or an explicit property in their csproj. The `src/` default is library. Individual project files override as needed.

**Svartalfheim note.** Svartalfheim receives this file as part of the `nuget` group. It is harmless: when `@(NorseRef)` and `@(NorseDesignRef)` are both empty (no items declared in any Svartalfheim csproj), the item transforms produce nothing. The `OutputType=Library` default is still beneficial. Svartalfheim csproj files never declare either item type.

### 2.5 Canonical `tests/Directory.Build.targets`

```xml
<Project>
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
</Project>
```

Sets the MTP-required executable output type for all test projects. Eliminates the need for every test `.csproj` to declare it individually.

MSBuild finds the nearest `Directory.Build.targets` by walking up from the project file. Test projects under `tests/` find `tests/Directory.Build.targets` before they reach `src/Directory.Build.targets` — the two trees are isolated from each other by directory position. The `OutputType=Library` default in `src/` never applies to test projects.

### 2.6 Platform-Config-Sync Activation

Two edits to the `.github` repo fill the slots reserved in the sync design (§11):

1. Add `config/src/Directory.Build.targets` — the canonical chain-or-fallback file from §2.4.
2. Add `config/tests/Directory.Build.targets` — the `OutputType=Exe` file from §2.5.
3. Uncomment the two reserved lines in `config/manifest.psd1` to include both files in the `nuget` group.

The next push to `config/**` on master triggers `scatter-the-runes.yml`, which fans both files to all `nuget`-group realms: Svartalfheim, Asgard, Midgard, Urdarbrunnr, Ratatoskr, Heimdall, Himinbjorg.

Yggdrasil is **excluded** from the `nuget` group, unchanged from the sync design. It maintains its own CPM-compatible variants — see §2.8.

### 2.7 Bifrost `nuget.config`

A `nuget.config` at the Bifrost root declares the GitHub Packages NuGet source for the NorseArchitecture org. No credentials section — packages are public, anonymous read works.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="github"
         value="https://nuget.pkg.github.com/NorseArchitecture/index.json"
         protocolVersion="3" />
  </packageSources>
</configuration>
```

This is the **only** checked-in `nuget.config` in the platform. It covers local developer experience in the Bifrost working tree. The session rule (Bifrost CLAUDE.md §6 / Glitnir CLAUDE.md §2.3) requires all development to start from Bifrost, so the source declaration is always in scope for the supported workflow.

Per-realm `nuget.config` files are not needed: CI configures the source programmatically via the composite action (§2.8), and standalone developer clones of individual realms are not a supported authoring workflow.

### 2.8 Composite Action — `add-norse-nuget-source`

A new composite action in the `.github` repo registers the GitHub Packages NuGet source before any `dotnet restore` invocation. Encapsulating it in a composite action avoids repeating the step across the three caller workflows.

```
.github/
  actions/
    add-norse-nuget-source/
      action.yml
```

```yaml
name: Add Norse NuGet Source
description: >
  Registers the NorseArchitecture GitHub Packages feed for anonymous NuGet restore.
  Packages are public — no token required.

runs:
  using: composite
  steps:
    - name: Register Norse NuGet feed
      shell: bash
      run: |
        dotnet nuget add source "https://nuget.pkg.github.com/NorseArchitecture/index.json" \
          --name "github"
```

### 2.9 Workflow Changes — Two Callers

Each workflow that invokes `dotnet restore` or `dotnet build` calls the composite action **before** the first such step. All existing gate structure, triggers, CodeQL steps, and release ceremony are unchanged.

```yaml
# Added to each job before restore/build:
- name: Add Norse NuGet source
  uses: NorseArchitecture/.github/.github/actions/add-norse-nuget-source@master
```

| Workflow | Jobs that need it |
|---|---|
| `ci-build-test.yml` | `build` (before Restore) |
| `release-nuget.yml` | `codeql` (before Restore and build); `pack-and-publish` (before Restore) |

Each job in GitHub Actions runs on a fresh runner. The source registered in one job does not carry into another — the call is required per job, not once across the workflow.

`update-bifrost.yml` and `scatter-the-runes.yml` are unchanged — they do not invoke `dotnet`.

### 2.10 Yggdrasil CPM Variants

Yggdrasil uses Central Package Management (CPM). Its `src/Directory.Build.targets` and `tests/Directory.Build.targets` mirror the canonical files in structure but omit the `Version` attribute on generated `<PackageReference>` items, relying on `Directory.Packages.props` to supply versions.

Yggdrasil manages these files itself — they are not received from the `nuget` group via `scatter-the-runes` (unchanged from the sync design). The CPM standalone fallback:

```xml
<ItemGroup Condition="!Exists('$(_BifrostTargets)')">
  <PackageReference Include="@(NorseRef->'Norse.%(Identity)')" />
  <PackageReference Include="@(NorseDesignRef->'Norse.%(Identity)')">
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
</ItemGroup>
```

When Yggdrasil is built in package mode from within the Bifrost peer tree, Bifrost's Choose block applies and `$(NorseRefVersion)` provides the version. CPM entries for Norse packages in Yggdrasil's `Directory.Packages.props` are required for standalone CI — the `Version` attribute on an explicitly-supplied version takes precedence over CPM in the Bifrost case, so the two contexts do not conflict.

### 2.11 The Toggle — Local CI Triage

To simulate CI builds locally from the Bifrost working tree:

```powershell
dotnet build Bifrost.slnx -p:UseProjectReferences=false
```

Forces package mode across all projects. `$(NorseRefVersion)` defaults to `*-*` — the latest published prerelease is resolved from GitHub Packages.

To pin a specific feature prerelease suffix:

```powershell
dotnet build Bifrost.slnx -p:UseProjectReferences=false -p:NorseRefVersion=*-my-feature
```

No file changes required. The toggle is pure command-line.

### 2.12 Adding a New Cross-Realm Dependency

When a project in realm B needs to reference a project in realm A:

1. In the consuming csproj:
   ```xml
   <ItemGroup>
     <NorseRef Include="{ProjectName}">
       <Repo>{RealmRepo}</Repo>
     </NorseRef>
   </ItemGroup>
   ```
2. Confirm `Include` matches the brand-free project folder name exactly — it is simultaneously the package name suffix and the csproj filename.
3. Build from Bifrost (`dotnet build`) — ProjectReference resolves automatically via the Choose block.
4. Build in package mode (`dotnet build -p:UseProjectReferences=false`) — PackageReference resolves. Confirm the published package exists on the feed before merging.
5. No changes to `Directory.Build.props`, either `Directory.Build.targets`, or `nuget.config`.

### 2.13 Foundation Realms — No NorseRef Participation

**Svartalfheim** (`Norse.Primitives`) is the bottom of the dependency graph. It has no cross-realm Norse dependencies and never will. No `<NorseRef>` items appear in any Svartalfheim csproj. The canonical `src/Directory.Build.targets` arrives via sync and is harmless — empty item transforms produce nothing.

**Naglfar** (`Norse.DesignSystem`) is a design-artifact repository — Figma exports, brand tokens, visual reference. It does not produce NuGet packages and does not participate in the MSBuild build graph. It receives `git`-group files only from `scatter-the-runes`, the same posture as Glitnir. No `<NorseDesignRef>` item currently points at Naglfar. The `<NorseDesignRef>` item type is defined and available for any design package a consuming realm chooses to reference regardless of source; whether Naglfar ever ships one is a separate design decision.

---

## 3. Alternatives Rejected

- **Dual conditional `<ItemGroup>` in every csproj.** Both a `<ProjectReference>` block and a `<PackageReference>` block per cross-realm dep, each conditioned on `$(UseProjectReferences)`. Correct and YGG301-clean, but verbose: two declarations per dependency, both requiring updates on rename or addition. The typed item approach gives a single declaration; the switching machinery is entirely infrastructure.

- **Choose block duplicated in every realm's `src/Directory.Build.targets`.** Self-contained per realm; no chaining. The canonical file is small and sync keeps it honest, but a logic change in the Choose block requires a full scatter cycle to propagate. Keeping the Choose block in Bifrost alone maintains a true single source of truth.

- **Import-only realm targets, no standalone fallback.** Chains up to Bifrost but has no local fallback. Works in the Bifrost peer tree, fails in standalone CI with missing-type errors because no reference resolution occurs. Rejected.

- **`$(CI)` environment variable as the sole mode trigger.** GitHub Actions sets `CI=true` automatically. Using it as the trigger is tempting but conflates two concerns — "running on a CI runner" and "I want package mode" — and has side effects on other CI-detection logic in the ecosystem. The named property `$(UseProjectReferences)` is explicit, scoped, and overridable without environment side effects.

---

## 4. Consequences

1. Migrate the existing raw cross-realm `<ProjectReference>` in Asgard's `Abstractions.Backend.csproj` to `<NorseRef Include="Primitives"><Repo>Svartalfheim</Repo></NorseRef>`.
2. Add `UseProjectReferences` and `NorseRefVersion` to Bifrost's root `Directory.Build.props`.
3. Add parent-chain import and `UseProjectReferences` default to `.github/config/Directory.Build.props` (§2.2); scatter propagates to all `dotnet`-group realms.
4. Author `Bifrost/Directory.Build.targets` with the Choose block (§2.3).
5. Author `config/src/Directory.Build.targets` in `.github` (§2.4).
6. Author `config/tests/Directory.Build.targets` in `.github` (§2.5).
7. Uncomment the two reserved lines in `.github/config/manifest.psd1`.
8. Add `nuget.config` to Bifrost root (§2.7).
9. Author `.github/actions/add-norse-nuget-source/action.yml` (§2.8).
10. Add the composite action call to `ci-build-test.yml` and `release-nuget.yml` (§2.9).
11. Author Yggdrasil's CPM-variant `src/Directory.Build.targets` and `tests/Directory.Build.targets` (§2.10).
12. Run `scatter-the-runes` — canonical targets fan out to all `nuget`-group realms.
13. Prove end-to-end: Bifrost full tree builds clean (`UseProjectReferences=true`); package mode builds clean (`UseProjectReferences=false`); CI gate passes on at least one realm with a `<NorseRef>` dependency (Asgard, which references Svartalfheim's `Primitives`).
