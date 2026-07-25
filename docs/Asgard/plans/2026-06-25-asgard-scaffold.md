# Asgard Scaffold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (default) paired with `superpowers:test-driven-development`. `superpowers:executing-plans` is the narrow fallback for a separate session with human review checkpoints — never an interchangeable alternative. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Scaffold the full six-assembly `Norse.Abstractions.*` structure — project files, build-props ladder, solution file, and dotfiles — leaving each project's type surface empty so subsequent plans (starting with the egress contracts plan) populate them.

**Architecture:** Six source projects and six test projects wired into one `Asgard.slnx` solution. A single `src/Directory.Build.props` injects `InternalsVisibleTo` for every project automatically; a single `tests/Directory.Build.props` supplies all test-runner dependencies. No types are authored here — the scaffold is purely structural. The egress contracts plan (`../plans/2026-06-19-asgard-egress-contracts.md`, Tasks 2–6 with the amendment substitutions applied) runs against `Norse.Abstractions.Backend` immediately after this plan.

**Tech Stack:** .NET 11 preview, C# `LangVersion=preview`, xUnit v3 + Shouldly + NSubstitute on Microsoft.Testing.Platform, MinVer for versioning. Build props and dotfiles copied from Svartalfheim, which is the only other fully scaffolded realm and is the proven baseline.

## Global Constraints

- `net11.0` target, `LangVersion=preview`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `WarningLevel=9999`.
- `global.json` SDK floor: `11.0.100-`, `rollForward: latestFeature`, `allowPrerelease: true` — must match Svartalfheim so both realms build under the same local SDK.
- Tabs for indentation (`.editorconfig` copied from Svartalfheim). `var` for return assignments only; explicit type + `new()` for construction. Accessibility by omission (`omit_if_default`).
- One public type per file; filename matches the type name exactly.
- Test assertions: Shouldly only. Test doubles: NSubstitute only (not Moq). Test classes: `public sealed`. Test methods omit access modifiers. Test naming: `Should_{behavior}_when_{condition}`.
- US English spelling in all code, identifiers, comments, docs, and commit copy.
- **No `dotnet test` on a project with zero tests** — xUnit v3/MTP fails a zero-test run. Build-only verification (`dotnet build`) is the gate for this scaffold; `dotnet test` gates land in the egress contracts plan once tests exist.
- **No automatic commits.** `git -C Asgard add <files>` + `git -C Asgard status`; human reviews and commits.

---

## File Structure

```
Asgard/
  .editorconfig                                     (copied from Svartalfheim)
  .gitattributes                                    (copied from Svartalfheim)
  .gitignore                                        (copied from Svartalfheim)
  Directory.Build.props                             (new)
  global.json                                       (new)
  Asgard.slnx                                       (new)
  src/
    Directory.Build.props                           (new — InternalsVisibleTo seam)
    Abstractions.Contracts/
      Abstractions.Contracts.csproj
    Abstractions.Components/
      Abstractions.Components.csproj
    Abstractions.Backend/
      Abstractions.Backend.csproj
      Egress/                                       (empty dir — populated by egress contracts plan)
    Abstractions.Worker/
      Abstractions.Worker.csproj
    Abstractions.Web.Server/
      Abstractions.Web.Server.csproj
    Abstractions.Migrations/
      Abstractions.Migrations.csproj
  tests/
    Directory.Build.props                           (new)
    Abstractions.Contracts.Tests/
      Abstractions.Contracts.Tests.csproj
    Abstractions.Components.Tests/
      Abstractions.Components.Tests.csproj
    Abstractions.Backend.Tests/
      Abstractions.Backend.Tests.csproj
    Abstractions.Worker.Tests/
      Abstractions.Worker.Tests.csproj
    Abstractions.Web.Server.Tests/
      Abstractions.Web.Server.Tests.csproj
    Abstractions.Migrations.Tests/
      Abstractions.Migrations.Tests.csproj
```

---

### Task 1: Scaffold the full Asgard repo

**Files:**
- Copy: `Asgard/.editorconfig`, `Asgard/.gitattributes`, `Asgard/.gitignore` (from `Svartalfheim/`)
- Create: `Asgard/global.json`
- Create: `Asgard/Directory.Build.props`
- Create: `Asgard/src/Directory.Build.props`
- Create: `Asgard/tests/Directory.Build.props`
- Create: `Asgard/src/Abstractions.Contracts/Abstractions.Contracts.csproj`
- Create: `Asgard/src/Abstractions.Components/Abstractions.Components.csproj`
- Create: `Asgard/src/Abstractions.Backend/Abstractions.Backend.csproj`
- Create: `Asgard/src/Abstractions.Worker/Abstractions.Worker.csproj`
- Create: `Asgard/src/Abstractions.Web.Server/Abstractions.Web.Server.csproj`
- Create: `Asgard/src/Abstractions.Migrations/Abstractions.Migrations.csproj`
- Create: `Asgard/tests/Abstractions.Contracts.Tests/Abstractions.Contracts.Tests.csproj`
- Create: `Asgard/tests/Abstractions.Components.Tests/Abstractions.Components.Tests.csproj`
- Create: `Asgard/tests/Abstractions.Backend.Tests/Abstractions.Backend.Tests.csproj`
- Create: `Asgard/tests/Abstractions.Worker.Tests/Abstractions.Worker.Tests.csproj`
- Create: `Asgard/tests/Abstractions.Web.Server.Tests/Abstractions.Web.Server.Tests.csproj`
- Create: `Asgard/tests/Abstractions.Migrations.Tests/Abstractions.Migrations.Tests.csproj`
- Create: `Asgard/Asgard.slnx`

**Interfaces:**
- Produces: twelve empty, buildable assemblies — `Norse.Abstractions.Contracts`, `Norse.Abstractions.Components`, `Norse.Abstractions.Backend`, `Norse.Abstractions.Worker`, `Norse.Abstractions.Web.Server`, `Norse.Abstractions.Migrations` and their `.Tests` counterparts — wired into `Asgard.slnx` with the correct project-reference graph from the spec. The egress contracts plan (Tasks 2–6, amended) depends on `Norse.Abstractions.Backend` and `Norse.Abstractions.Backend.Tests` existing at the right paths.

- [ ] **Step 1: Copy the repo-wide dotfiles from Svartalfheim**

```bash
cp Svartalfheim/.editorconfig Asgard/.editorconfig
cp Svartalfheim/.gitattributes Asgard/.gitattributes
cp Svartalfheim/.gitignore Asgard/.gitignore
```

Verify:

```bash
ls Asgard/.editorconfig Asgard/.gitattributes Asgard/.gitignore
```

Expected: all three files present, no error.

- [ ] **Step 2: Write `Asgard/global.json`**

```json
{
  "sdk": {
    "version": "11.0.100-",
    "rollForward": "latestFeature",
    "allowPrerelease": true
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

- [ ] **Step 3: Write `Asgard/Directory.Build.props`**

Mirrors Svartalfheim exactly. The comment about CA1034 applies here too: `Norse.Abstractions.Backend`'s `HttpResult<T>` union uses the same nested-case-type shape as `Norse.Primitives`'s `Result<T>`, and CA1034 would reject it.

```xml
<Project>
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
		<Authors>Norse Architecture</Authors>
		<Deterministic>true</Deterministic>
		<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
		<GenerateDocumentationFile>true</GenerateDocumentationFile>
		<ImplicitUsings>enable</ImplicitUsings>
		<LangVersion>preview</LangVersion>
		<MinVerTagPrefix>v</MinVerTagPrefix>
		<Nullable>enable</Nullable>
		<PackageId>Norse.$(MSBuildProjectName)</PackageId>
		<RootNamespace>Norse.$(MSBuildProjectName)</RootNamespace>
		<TargetFramework>net11.0</TargetFramework>
		<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
		<WarningLevel>9999</WarningLevel>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="MinVer" Version="6.*">
			<PrivateAssets>all</PrivateAssets>
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
		</PackageReference>
	</ItemGroup>
</Project>
```

- [ ] **Step 4: Write `Asgard/src/Directory.Build.props`**

Injects `InternalsVisibleTo` for every source assembly in `src/` automatically — the sanctioned door for test access to internals, declared once, never per-csproj. `$(AssemblyName)` expands per-project (e.g. `Norse.Abstractions.Backend` → `Norse.Abstractions.Backend.Tests`).

```xml
<Project>
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
	<ItemGroup>
		<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
			<_Parameter1>$(AssemblyName).Tests</_Parameter1>
		</AssemblyAttribute>
	</ItemGroup>
</Project>
```

- [ ] **Step 5: Write `Asgard/tests/Directory.Build.props`**

NSubstitute is included at the shared level because multiple test assemblies across Asgard will need it (starting with `Abstractions.Backend.Tests` in the egress contracts plan).

```xml
<Project>
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
	<PropertyGroup>
		<IsPackable>false</IsPackable>
		<IsTestProject>true</IsTestProject>
		<NoWarn>$(NoWarn);CA1812;CA1859;CS1591;IDE0051</NoWarn>
		<OutputType>Exe</OutputType>
		<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.Testing.Platform.MSBuild" Version="2.*" />
		<PackageReference Include="NSubstitute" Version="5.*" />
		<PackageReference Include="Shouldly" Version="4.*" />
		<PackageReference Include="xunit.v3.mtp-v2" Version="3.*" />
		<Using Include="Shouldly" />
		<Using Include="Xunit" />
	</ItemGroup>
</Project>
```

- [ ] **Step 6: Write `Asgard/src/Abstractions.Contracts/Abstractions.Contracts.csproj`**

No upstream dependencies — the project carries no `ProjectReference` items.

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse declared law: the NorsePrincipal identity surface, Population value type, published domain event interfaces, and the IAccountApi cross-realm read contract. The single assembly other product contexts reference from Norse.Abstractions.</Description>
		<IsAotCompatible>true</IsAotCompatible>
	</PropertyGroup>
</Project>
```

> **Amendment (2026-07-25):** this description was never shipped as written. `Abstractions.Contracts` carries `Outcome<T>`/`Problem`/`ErrorCategory`/`BoolResponse`/`Unit` and `GenerateGatewayAttribute` instead — no `NorsePrincipal`, `Population`, or `IAccountApi` in current source. See `docs/Asgard/specs/2026-06-25-asgard-project-structure-design.md`.

- [ ] **Step 7: Write `Asgard/src/Abstractions.Components/Abstractions.Components.csproj`**

No upstream dependencies; must never pull in ASP.NET Core or any server-side infrastructure — MAUI and WASM bundles reference this directly.

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse component abstractions: Razor component base types shared across Blazor WASM, Blazor Server, and MAUI consumers. No ASP.NET Core, EF Core, or server-side infrastructure references — this assembly must compile into a client bundle.</Description>
		<IsAotCompatible>true</IsAotCompatible>
	</PropertyGroup>
</Project>
```

- [ ] **Step 8: Write `Asgard/src/Abstractions.Backend/Abstractions.Backend.csproj`**

The raw relative path to Svartalfheim is the Mjolnir precedent: three `../` steps from `Asgard/src/Abstractions.Backend/` reach the Bifrost root, where `Svartalfheim/` is a sibling of `Asgard/`. Wiring this via a more standardized mechanism is the separate "`UseProjectReferences` infrastructure" task flagged for a future Bifrost session; a raw `ProjectReference` functions today.

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse server-side shared contracts, visible to both Worker and Web.Server. Egress contracts live under the Norse.Abstractions.Backend.Egress namespace: HttpResult&lt;T&gt;, EgressError, FailureKind, ResponseDisposition, EgressClassifier, IResponseParser&lt;T&gt;, IHttpEgress. Additional server-side shared concerns land here as they emerge; a concern graduates to its own assembly only if a hard wall requires it.</Description>
		<IsAotCompatible>true</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../Abstractions.Contracts/Abstractions.Contracts.csproj" />
		<ProjectReference Include="../../../Svartalfheim/src/Primitives/Primitives.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 9: Write `Asgard/src/Abstractions.Worker/Abstractions.Worker.csproj`**

`Abstractions.Contracts` and `Norse.Primitives` are transitive via `Abstractions.Backend`. Mutually invisible with `Abstractions.Web.Server` — enforced by the absence of a project reference between them.

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse worker abstractions: IWorkerHostPlugin, command and cached repository surfaces (ICommandRepository&lt;T&gt;, ICachedRepository&lt;T&gt;), and NServiceBus handler contract seams — the server-side law for the system-of-record tier. Mutually invisible with Norse.Abstractions.Web.Server.</Description>
		<IsAotCompatible>true</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../Abstractions.Backend/Abstractions.Backend.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 10: Write `Asgard/src/Abstractions.Web.Server/Abstractions.Web.Server.csproj`**

`Abstractions.Contracts` and `Norse.Primitives` are transitive via `Abstractions.Backend`. Mutually invisible with `Abstractions.Worker`.

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse web-server abstractions: IWebHostPlugin, the document repository surface (IDocumentRepository&lt;T&gt;), and mediator law (ICommandRequest&lt;T&gt;, validator and authorizer contracts) — the server-side law for the web tier. Mutually invisible with Norse.Abstractions.Worker.</Description>
		<IsAotCompatible>true</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../Abstractions.Backend/Abstractions.Backend.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 11: Write `Asgard/src/Abstractions.Migrations/Abstractions.Migrations.csproj`**

No upstream dependencies; not referenced by Worker or Web.Server — isolation enforced by the absence of a project reference from either.

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse migration contract: the IMigrationContributor interface (EF-free) — the single law governing migration contribution across all contexts. Not referenced by Worker or Web.Server; isolation enforced by the absence of a project reference.</Description>
		<IsAotCompatible>true</IsAotCompatible>
	</PropertyGroup>
</Project>
```

- [ ] **Step 12: Write `Asgard/tests/Abstractions.Contracts.Tests/Abstractions.Contracts.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Abstractions.Contracts/Abstractions.Contracts.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 13: Write `Asgard/tests/Abstractions.Components.Tests/Abstractions.Components.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Abstractions.Components/Abstractions.Components.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 14: Write `Asgard/tests/Abstractions.Backend.Tests/Abstractions.Backend.Tests.csproj`**

`Norse.Abstractions.Contracts` and `Norse.Primitives` are transitive via `Abstractions.Backend`. NSubstitute arrives via `tests/Directory.Build.props`.

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Abstractions.Backend/Abstractions.Backend.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 15: Write `Asgard/tests/Abstractions.Worker.Tests/Abstractions.Worker.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Abstractions.Worker/Abstractions.Worker.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 16: Write `Asgard/tests/Abstractions.Web.Server.Tests/Abstractions.Web.Server.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Abstractions.Web.Server/Abstractions.Web.Server.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 17: Write `Asgard/tests/Abstractions.Migrations.Tests/Abstractions.Migrations.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Abstractions.Migrations/Abstractions.Migrations.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 18: Write `Asgard/Asgard.slnx`**

All twelve projects in `/src/` and `/tests/` solution folders. Build props files are listed under their respective folders so they appear in the IDE.

```xml
<Solution>
	<Folder Name="/Solution Items/">
		<File Path=".editorconfig" />
		<File Path=".gitattributes" />
		<File Path=".gitignore" />
		<File Path="Directory.Build.props" />
		<File Path="global.json" />
		<File Path="LICENSE" />
	</Folder>
	<Folder Name="/src/">
		<File Path="src/Directory.Build.props" />
		<Project Path="src/Abstractions.Contracts/Abstractions.Contracts.csproj" />
		<Project Path="src/Abstractions.Components/Abstractions.Components.csproj" />
		<Project Path="src/Abstractions.Backend/Abstractions.Backend.csproj" />
		<Project Path="src/Abstractions.Worker/Abstractions.Worker.csproj" />
		<Project Path="src/Abstractions.Web.Server/Abstractions.Web.Server.csproj" />
		<Project Path="src/Abstractions.Migrations/Abstractions.Migrations.csproj" />
	</Folder>
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<Project Path="tests/Abstractions.Contracts.Tests/Abstractions.Contracts.Tests.csproj" />
		<Project Path="tests/Abstractions.Components.Tests/Abstractions.Components.Tests.csproj" />
		<Project Path="tests/Abstractions.Backend.Tests/Abstractions.Backend.Tests.csproj" />
		<Project Path="tests/Abstractions.Worker.Tests/Abstractions.Worker.Tests.csproj" />
		<Project Path="tests/Abstractions.Web.Server.Tests/Abstractions.Web.Server.Tests.csproj" />
		<Project Path="tests/Abstractions.Migrations.Tests/Abstractions.Migrations.Tests.csproj" />
	</Folder>
</Solution>
```

- [ ] **Step 19: Build the solution**

Run: `dotnet build Asgard/Asgard.slnx`

Expected: 0 warnings, 0 errors across all twelve projects. If the `[MustConsume]`/`[Union]`/`IUnion` symbols from `Norse.Primitives` produce build errors (they are emitted by Svartalfheim's source generators and require the ref pack's runtime support), confirm the local SDK matches `global.json`'s `11.0.100-` floor before debugging further.

- [ ] **Step 20: Stage and stop**

```bash
git -C Asgard add .editorconfig .gitattributes .gitignore Directory.Build.props global.json Asgard.slnx src tests
git -C Asgard status
```

Expected: all new files staged, nothing unstaged. Show the output; the human reviews and commits.

---

### Task 2: Documentation sync

**Files:**
- Modify: `Asgard/CLAUDE.md`
- Modify: `Asgard/README.md`
- Modify: `Glitnir/docs/Asgard/plans/2026-06-19-asgard-egress-contracts.md` (status line on the amendment note)

**Interfaces:**
- Consumes: the scaffolded structure from Task 1.
- Produces: accurate paired docs and an egress-plan amendment update; no later task depends on this one.

- [ ] **Step 1: Update `Asgard/CLAUDE.md` §1 — fix "bare shell" and "rides on nothing"**

In `Asgard/CLAUDE.md`, §1 currently reads:

> Asgard is **declared law** — `Norse.Abstractions`: contracts and the rules every realm must honor. No implementations live here, by design — plugin interfaces (`IWebHostPlugin`/`IWorkerHostPlugin`), the repository contract family, and the attribute model. It is the topmost layer of the Norse Architecture dependency chain: every other realm rides on Asgard; Asgard rides on nothing.
>
> This repo is currently a bare shell (LICENSE only) — no specs have converged here yet. Before writing any code: brainstorm → spec → plan, recorded in `../Glitnir/docs/Asgard/`, per the org's spec-first discipline. Do not scaffold a project structure ahead of a converged spec. When that plan is written, its REQUIRED SUB-SKILL line names `superpowers:subagent-driven-development` as the default (not a recommendation among equals — `executing-plans` is the narrow fallback for separate-session review checkpoints) paired with `superpowers:test-driven-development` — implementation here is subagent-orchestrated and test-driven, never one without the other (`../Glitnir/CLAUDE.md` §2.8).

Replace that paragraph block with:

> Asgard is **declared law** — `Norse.Abstractions`: contracts and the rules every realm must honor. No implementations live here, by design. Six assemblies, split by dependency wall and consumer context — see `../Glitnir/docs/Asgard/specs/2026-06-25-asgard-project-structure-design.md` for the full assembly set, dependency graph, and rationale.
>
> The dependency graph is peer-flat except for one assembly: `Norse.Abstractions.Backend` depends on `Norse.Abstractions.Contracts` and `Norse.Primitives` (Svartalfheim — forged below the domain, per the platform convention). The five remaining assemblies carry no upstream dependencies. "Asgard rides on nothing" was the claim before specs converged; the settled design shows `Norse.Abstractions.Backend` is the exception.
>
> This repo is scaffolded — six source projects and six test projects wired into `Asgard.slnx`. The first implementation is the egress contracts slice (plan: `../Glitnir/docs/Asgard/plans/2026-06-19-asgard-egress-contracts.md`, Tasks 2–6 with the amendment applied — egress types land in `Norse.Abstractions.Backend.Egress`). Every subsequent plan for this realm follows the same discipline: brainstorm → spec → plan in `../Glitnir/docs/Asgard/`, greenlit by the human, then code. Each plan's REQUIRED SUB-SKILL line names `superpowers:subagent-driven-development` as the default (not a recommendation among equals — `executing-plans` is the narrow fallback for separate-session review checkpoints) paired with `superpowers:test-driven-development` — implementation here is subagent-orchestrated and test-driven, never one without the other (`../Glitnir/CLAUDE.md` §2.8).

- [ ] **Step 2: Update `Asgard/README.md` — fix "bare shell" and "rides on nothing"**

In `Asgard/README.md`, the current text reads:

> Declared law for the Norse Architecture — **`Norse.Abstractions`**: the contracts and rules every realm must honor. No implementations live here, by design — plugin interfaces (`IWebHostPlugin`/`IWorkerHostPlugin`), the repository contract family, and the attribute model. It is the topmost layer of the dependency chain: every other realm rides on Asgard; Asgard rides on nothing.
>
> ## Status
>
> This realm is currently a bare shell — no code, no specs converged yet. Design happens first: brainstorm → spec → plan, recorded in Glitnir's `docs/Asgard/`, before any project is scaffolded here.

Replace with:

> Declared law for the Norse Architecture — **`Norse.Abstractions`**: the contracts and rules every realm must honor. No implementations live here, by design. Six assemblies, split by dependency wall and consumer context:
>
> | Assembly | Upstream Dependencies | Purpose |
> |---|---|---|
> | `Norse.Abstractions.Contracts` | none | `NorsePrincipal`, `Population`, published event interfaces, `IAccountApi` |
>
> **Amendment (2026-07-25):** superseded — the shipped contents are `Outcome<T>`/`Problem`/`ErrorCategory`/`BoolResponse`/`Unit` and `GenerateGatewayAttribute`, not `NorsePrincipal`/`Population`/`IAccountApi`. See `docs/Asgard/specs/2026-06-25-asgard-project-structure-design.md`.
> | `Norse.Abstractions.Components` | none | Razor component base abstractions (MAUI/WASM-safe — no server-side infrastructure) |
> | `Norse.Abstractions.Backend` | `Norse.Primitives`, `Norse.Abstractions.Contracts` | Shared server-side contracts (egress contracts under `.Egress` namespace) |
> | `Norse.Abstractions.Worker` | `Norse.Abstractions.Backend` (transitive) | `IWorkerHostPlugin`, `ICommandRepository<T>`, `ICachedRepository<T>`, NServiceBus seams |
> | `Norse.Abstractions.Web.Server` | `Norse.Abstractions.Backend` (transitive) | `IWebHostPlugin`, `IDocumentRepository<T>`, mediator law |
> | `Norse.Abstractions.Migrations` | none | `IMigrationContributor` (EF-free) |
>
> Worker and Web.Server are mutually invisible — neither references the other.
>
> ## Status
>
> Scaffolded — six source projects and six test projects, wired into `Asgard.slnx`. First implementation in progress: the egress contracts slice (`Norse.Abstractions.Backend.Egress`). Design for each subsequent type surface follows the spec-first discipline: brainstorm → spec → plan in [Glitnir](https://github.com/NorseArchitecture/Glitnir)'s `docs/Asgard/`, greenlit by the human, then code.

- [ ] **Step 3: Update the egress contracts plan amendment note**

In `Glitnir/docs/Asgard/plans/2026-06-19-asgard-egress-contracts.md`, the amendment note at line 3 currently ends with: "...See the amendment note at the top of that plan." The amendment note is accurate; no content change is needed — but add one sentence to the end of the amendment paragraph to record that the scaffold is complete:

Append to the end of the amendment paragraph (after the sentence ending "…needs none of that to function today."):

> The scaffold this plan's Task 1 was superseded by is complete (`2026-06-25-asgard-scaffold.md`); Tasks 2–6 below are ready to execute.

- [ ] **Step 4: Stage and stop**

```bash
git -C Asgard add CLAUDE.md README.md
git -C Asgard status
git -C Glitnir add docs/Asgard/plans/2026-06-19-asgard-egress-contracts.md docs/Asgard/plans/2026-06-25-asgard-scaffold.md
git -C Glitnir status
```

Show both diffs; the human reviews and commits each repo independently.
