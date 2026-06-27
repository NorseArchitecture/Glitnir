# Yggdrasil Runtime Scaffold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (default) paired with `superpowers:test-driven-development`. `superpowers:executing-plans` is the narrow fallback for a separate session with human review checkpoints — never an interchangeable alternative. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Scaffold the three deployable hosting projects (`Hosting.Web.Server`, `Hosting.Worker`, `Hosting.Migrations.Service`) with build infrastructure, placeholder tests that clear the 60% coverage floor, and a solution file — satisfying the hard prerequisite for the tag-release plan's smoke test (Task 5).

**Architecture:** Six projects (three main, three test) wired into `Yggdrasil.slnx`. `src/Directory.Build.props` injects `InternalsVisibleTo` and `IsPackable=false` for all source projects. `tests/Directory.Build.props` wires the MTP runner and floating test packages; CPM is disabled for `tests/` via `tests/Directory.Packages.props`. Each main project provides a `Placeholder.cs` with a ternary that gives the coverage tool a measurable branch pair; each test project covers both branches — 100% branch coverage on all user code. No Norse package dependencies in this scaffold.

**Tech Stack:** .NET 11 preview, `Microsoft.NET.Sdk.Web` (web server), `Microsoft.NET.Sdk.Worker` (worker and migrations service), xUnit v3 (`xunit.v3.mtp-v2`) + Shouldly on Microsoft.Testing.Platform, `dotnet publish /t:PublishContainer`.

## Global Constraints

- `net11.0` target, `LangVersion=preview`, `TreatWarningsAsErrors=true`, `WarningLevel=9999` — inherited from the realm root `Yggdrasil/Directory.Build.props`; do not re-declare in any project file.
- No `OutputType` in `src/Directory.Build.props` or any source `.csproj` — each SDK sets the correct value; `src/Directory.Build.targets` already lacks it (confirmed pre-condition, Step 1 of Task 2 verifies).
- `IsPackable=false` on all projects — nothing in this scaffold is a packable NuGet library.
- `IsAotCompatible` omitted throughout — that is a packable-library concern; these are deployable executables.
- No `NorseRef` items — no Norse package dependencies in this scaffold.
- `DotNetVersion=11.0.0-preview.5.26302.115` — added to root `Directory.Packages.props`; the only runtime version pin in the repo; updated manually each month when Microsoft ships the next preview.
- `ContainerDotNetVersion=11.0.0-preview.5` — also added to root `Directory.Packages.props`; the full SDK build suffix is stripped for MCR tag matching. .NET 11 preview images live in `mcr.microsoft.com/dotnet/nightly/` (not the stable registry); drop this override once .NET 11 goes stable.
- `Microsoft.Extensions.Hosting Version="10.0.9"` — explicit CPM entry required for `Microsoft.NET.Sdk.Worker` projects; the Worker SDK in .NET 11 preview does not include `Host` implicitly. Reference with `<PackageReference Include="Microsoft.Extensions.Hosting" />` (no version — CPM supplies it).
- **Container publish arch:** `--arch arm64` for all local commands (Snapdragon X1 Elite development host); `--arch x64` in `release-container.yml` (CI runs on x64 Ubuntu). Never use `--arch x64` for local `dotnet publish /t:PublishContainer` steps.
- CPM ON for `src/`; CPM OFF for `tests/` via `tests/Directory.Packages.props`. Test packages float with `Version="*"`.
- `InternalsVisibleTo` hoisted in `src/Directory.Build.props` — never per-csproj.
- US English in all code, identifiers, comments, docs, and commit copy.
- **No automatic commits** — `git add` only; the human reviews and commits. Every task ends with a stage step, a proposed commit message, and stops.

---

## File Map

| Action | File | Repo | Notes |
|---|---|---|---|
| Modify | `Glitnir/docs/Platform/plans/2026-06-27-yggdrasil-tag-release.md` | Bifrost | Add `src/` prefix to all project paths; fix smoke-test arch flag |
| Modify | `Directory.Packages.props` | Yggdrasil | Add `DotNetVersion` + `ContainerDotNetVersion` properties; add `Microsoft.Extensions.Hosting 10.0.9` |
| Verify | `src/Directory.Build.targets` | Yggdrasil | Confirm no `OutputType` — pre-condition, no edit |
| Create | `src/Directory.Build.props` | Yggdrasil | `IsPackable=false`, `InternalsVisibleTo` |
| Create | `tests/Directory.Packages.props` | Yggdrasil | CPM off |
| Create | `tests/Directory.Build.props` | Yggdrasil | Floating test packages, MTP runner config |
| Create | `src/Hosting.Web.Server/Hosting.Web.Server.csproj` | Yggdrasil | SDK Web, `ContainerBaseImage aspnet` |
| Create | `src/Hosting.Web.Server/Placeholder.cs` | Yggdrasil | `Norse.Hosting.Web.Server` namespace |
| Create | `src/Hosting.Web.Server/Program.cs` | Yggdrasil | `WebApplication` stub |
| Create | `tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj` | Yggdrasil | `ProjectReference` to source |
| Create | `tests/Hosting.Web.Server.Tests/PlaceholderTests.cs` | Yggdrasil | Two-branch tests |
| Create | `src/Hosting.Worker/Hosting.Worker.csproj` | Yggdrasil | SDK Worker, `ContainerBaseImage runtime` |
| Create | `src/Hosting.Worker/Placeholder.cs` | Yggdrasil | `Norse.Hosting.Worker` namespace |
| Create | `src/Hosting.Worker/Program.cs` | Yggdrasil | `Host` stub |
| Create | `tests/Hosting.Worker.Tests/Hosting.Worker.Tests.csproj` | Yggdrasil | `ProjectReference` to source |
| Create | `tests/Hosting.Worker.Tests/PlaceholderTests.cs` | Yggdrasil | Two-branch tests |
| Create | `src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj` | Yggdrasil | SDK Worker, `ContainerBaseImage runtime` |
| Create | `src/Hosting.Migrations.Service/Placeholder.cs` | Yggdrasil | `Norse.Hosting.Migrations.Service` namespace |
| Create | `src/Hosting.Migrations.Service/Program.cs` | Yggdrasil | `Host` stub |
| Create | `tests/Hosting.Migrations.Service.Tests/Hosting.Migrations.Service.Tests.csproj` | Yggdrasil | `ProjectReference` to source |
| Create | `tests/Hosting.Migrations.Service.Tests/PlaceholderTests.cs` | Yggdrasil | Two-branch tests |
| Create | `Yggdrasil.slnx` | Yggdrasil | All six projects, no solution folders |
| Modify | `CLAUDE.md` | Yggdrasil | Remove "bare shell" language; reflect scaffolded state |
| Modify | `README.md` | Yggdrasil | Same update — boy-scout law requires both in one change |

---

## Task 1: Correct companion plan paths

The tag-release plan (`2026-06-27-yggdrasil-tag-release.md`) was written before this design session settled the `src/` project layout. Three places need correction: the Global Constraints project path lines, Task 2's `release-container.yml` `dotnet publish` commands, and Task 5's smoke-test commands. Task 5 also uses `--arch x64` for local execution — fix to `--arch arm64` (Snapdragon host). Note: `release-container.yml` does not exist yet; the correction lands only in the plan document so the workflow is written with correct paths when Task 2 of the tag-release plan executes.

**Files:**
- Modify: `Glitnir/docs/Platform/plans/2026-06-27-yggdrasil-tag-release.md`

**Interfaces:**
- Produces: corrected plan document that Tasks 2–5 of the tag-release plan can execute against without path errors.

- [ ] **Step 1: Fix Global Constraints — three project path lines**

In `Glitnir/docs/Platform/plans/2026-06-27-yggdrasil-tag-release.md`, locate the bullet block under **Global Constraints**:

```
- **Project paths** (brand-free per Bifrost CLAUDE.md §2, at Yggdrasil repo root):
  - `Hosting.Migrations.Service/Hosting.Migrations.Service.csproj`
  - `Hosting.Web.Server/Hosting.Web.Server.csproj`
  - `Hosting.Worker/Hosting.Worker.csproj`
```

Replace with:

```
- **Project paths** (brand-free per Bifrost CLAUDE.md §2, under `src/` at Yggdrasil repo root):
  - `src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj`
  - `src/Hosting.Web.Server/Hosting.Web.Server.csproj`
  - `src/Hosting.Worker/Hosting.Worker.csproj`
```

- [ ] **Step 2: Fix Task 2 — three `dotnet publish` commands in the release-container.yml block**

Still in the same file, inside Task 2's YAML content block (under `# ── migrations ──`, `# ── web ──`, `# ── worker ──`), change each `dotnet publish` line:

```
          dotnet publish Hosting.Migrations.Service/Hosting.Migrations.Service.csproj \
```
→
```
          dotnet publish src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj \
```

```
          dotnet publish Hosting.Web.Server/Hosting.Web.Server.csproj \
```
→
```
          dotnet publish src/Hosting.Web.Server/Hosting.Web.Server.csproj \
```

```
          dotnet publish Hosting.Worker/Hosting.Worker.csproj \
```
→
```
          dotnet publish src/Hosting.Worker/Hosting.Worker.csproj \
```

The `--arch x64` in these CI workflow steps is correct — GitHub Actions ubuntu runners are x64; do not change it.

- [ ] **Step 3: Fix Task 5 — prerequisite list, project paths, and arch flag**

In Task 5's prerequisite block, change:

```
- `Hosting.Migrations.Service/Hosting.Migrations.Service.csproj`
- `Hosting.Web.Server/Hosting.Web.Server.csproj`
- `Hosting.Worker/Hosting.Worker.csproj`
```

to:

```
- `src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj`
- `src/Hosting.Web.Server/Hosting.Web.Server.csproj`
- `src/Hosting.Worker/Hosting.Worker.csproj`
```

Then in Step 1's `dotnet publish` commands (local smoke test), change all three from `--os linux --arch x64` to `--os linux --arch arm64` AND add the `src/` prefix to all three project paths. Example — before:

```bash
dotnet publish Hosting.Migrations.Service/Hosting.Migrations.Service.csproj \
  --os linux --arch x64 -c Release /t:PublishContainer \
  /p:ContainerRepository=norsearchitecture/hosting/migrations \
  /p:ContainerImageTag=smoke-test
```

After:

```bash
dotnet publish src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj \
  --os linux --arch arm64 -c Release /t:PublishContainer \
  /p:ContainerRepository=norsearchitecture/hosting/migrations \
  /p:ContainerImageTag=smoke-test
```

Apply the same `src/` and `--arch arm64` change to the `Hosting.Web.Server` and `Hosting.Worker` blocks in that same step. The `docker image ls` and `docker image rm` lines reference the local image tag, not the project path — leave them unchanged.

- [ ] **Step 4: Stage and stop**

```bash
git -C Glitnir add docs/Platform/plans/2026-06-27-yggdrasil-tag-release.md
git -C Glitnir status
```

Proposed commit message (Glitnir): `docs: correct yggdrasil tag-release plan project paths and local arch flag`

---

## Task 2: Build infrastructure + Hosting.Web.Server (TDD)

Adds the build props ladder and the first project pair. The props files are prerequisites for any project to compile; folded into the first project task.

**Files:**
- Verify: `src/Directory.Build.targets` (no edit — confirm pre-condition)
- Modify: `Directory.Packages.props`
- Create: `src/Directory.Build.props`
- Create: `tests/Directory.Packages.props`
- Create: `tests/Directory.Build.props`
- Create: `src/Hosting.Web.Server/Hosting.Web.Server.csproj`
- Create: `tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj`
- Create: `tests/Hosting.Web.Server.Tests/PlaceholderTests.cs`
- Create: `src/Hosting.Web.Server/Placeholder.cs`
- Create: `src/Hosting.Web.Server/Program.cs`

**Interfaces:**
- Produces: `Norse.Hosting.Web.Server` (compilable ASP.NET Core stub with `internal static Placeholder.ServiceName(bool)`) and `Norse.Hosting.Web.Server.Tests` with 2/2 passing tests.

- [ ] **Step 1: Verify `src/Directory.Build.targets` has no `OutputType`**

```bash
cat /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Directory.Build.targets
```

Expected: the file contains the conditional Bifrost parent-targets import and the `NorseRef`/`NorseDesignRef` ItemGroup. No `<OutputType>` property anywhere. If `OutputType` is present, remove it before proceeding — that is a misfire this plan does not expect.

- [ ] **Step 2: Add `DotNetVersion` to `Directory.Packages.props`**

Rewrite `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/Directory.Packages.props` to add `<DotNetVersion>` after `<ManagePackageVersionsCentrally>`, preserving existing realm version order:

```xml
<Project>
	<PropertyGroup>
		<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
		<DotNetVersion>11.0.0-preview.5.26302.115</DotNetVersion>
		<ContainerDotNetVersion>11.0.0-preview.5</ContainerDotNetVersion>
		<SvartalfheimVersion>0.0.4</SvartalfheimVersion>
		<AsgardVersion>0.0.0</AsgardVersion>
		<MidgardVersion>0.0.0</MidgardVersion>
		<UrdarbrunnrVersion>0.0.0</UrdarbrunnrVersion>
		<RatatoskrVersion>0.0.0</RatatoskrVersion>
		<HiminbjorgVersion>0.0.0</HiminbjorgVersion>
		<HeimdallVersion>0.0.0</HeimdallVersion>
	</PropertyGroup>
	<ItemGroup>
		<PackageVersion Include="Norse.Primitives" Version="$(SvartalfheimVersion)" />

		<PackageVersion Include="Norse.Abstractions.Backend"    Version="$(AsgardVersion)" />
		<PackageVersion Include="Norse.Abstractions.Components" Version="$(AsgardVersion)" />
		<PackageVersion Include="Norse.Abstractions.Contracts"  Version="$(AsgardVersion)" />
		<PackageVersion Include="Norse.Abstractions.Migrations" Version="$(AsgardVersion)" />
		<PackageVersion Include="Norse.Abstractions.Web.Server" Version="$(AsgardVersion)" />
		<PackageVersion Include="Norse.Abstractions.Worker"     Version="$(AsgardVersion)" />
	</ItemGroup>
</Project>
```

- [ ] **Step 3: Create `src/Directory.Build.props`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Directory.Build.props`:

```xml
<Project>
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
	<PropertyGroup>
		<IsPackable>false</IsPackable>
	</PropertyGroup>
	<ItemGroup>
		<InternalsVisibleTo Include="$(AssemblyName).Tests" />
	</ItemGroup>
</Project>
```

- [ ] **Step 4: Create `tests/Directory.Packages.props`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/tests/Directory.Packages.props`:

```xml
<Project>
	<PropertyGroup>
		<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
	</PropertyGroup>
</Project>
```

- [ ] **Step 5: Create `tests/Directory.Build.props`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/tests/Directory.Build.props`. `OutputType=Exe` is already supplied by the existing `tests/Directory.Build.targets` — do not repeat it here.

```xml
<Project>
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
	<PropertyGroup>
		<IsPackable>false</IsPackable>
		<IsTestProject>true</IsTestProject>
		<NoWarn>$(NoWarn);CA1812;CA1859;CS1591;IDE0051</NoWarn>
		<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" Version="*" />
		<PackageReference Include="Microsoft.Testing.Platform.MSBuild" Version="*" />
		<PackageReference Include="Shouldly" Version="*" />
		<PackageReference Include="xunit.v3.mtp-v2" Version="*" />
		<Using Include="Shouldly" />
		<Using Include="Xunit" />
	</ItemGroup>
</Project>
```

- [ ] **Step 6: Create `src/Hosting.Web.Server/Hosting.Web.Server.csproj`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
	<PropertyGroup>
		<ContainerBaseImage>mcr.microsoft.com/dotnet/nightly/aspnet:$(ContainerDotNetVersion)</ContainerBaseImage>
	</PropertyGroup>
</Project>
```

- [ ] **Step 7: Create `tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="..\..\src\Hosting.Web.Server\Hosting.Web.Server.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 8: Write `PlaceholderTests.cs` — failing test (red)**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/tests/Hosting.Web.Server.Tests/PlaceholderTests.cs`:

```csharp
namespace Norse.Hosting.Web.Server.Tests;

public class PlaceholderTests
{
	[Fact]
	public void ServiceName_returns_short_form() =>
		Placeholder.ServiceName().ShouldBe("Hosting.Web.Server");

	[Fact]
	public void ServiceName_returns_qualified_form() =>
		Placeholder.ServiceName(qualified: true).ShouldBe("Norse.Hosting.Web.Server");
}
```

- [ ] **Step 9: Run — confirm compile failure (red)**

```bash
dotnet build /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj
```

Expected: build failure with `CS0103 The name 'Placeholder' does not exist in the current context` (and possibly `CS5001` — no Program entry point until `Program.cs` is added). Any compile error is the correct red signal; proceed to Step 10.

- [ ] **Step 10: Write `Placeholder.cs`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Hosting.Web.Server/Placeholder.cs`:

```csharp
namespace Norse.Hosting.Web.Server;

static class Placeholder
{
	internal static string ServiceName(bool qualified = false) =>
		qualified ? "Norse.Hosting.Web.Server" : "Hosting.Web.Server";
}
```

- [ ] **Step 11: Write `Program.cs`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Hosting.Web.Server/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();
```

- [ ] **Step 12: Run tests — confirm 2/2 pass (green)**

```bash
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj -c Release
```

Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`

- [ ] **Step 13: Build source project — confirm 0 warnings**

```bash
dotnet build /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj -c Release
```

Expected: `Build succeeded. 0 Warning(s)  0 Error(s)`

- [ ] **Step 14: Stage and stop**

```bash
git -C Yggdrasil add \
  Directory.Packages.props \
  src/Directory.Build.props \
  tests/Directory.Packages.props \
  tests/Directory.Build.props \
  src/Hosting.Web.Server \
  tests/Hosting.Web.Server.Tests
git -C Yggdrasil status
```

Proposed commit message (Yggdrasil): `feat: scaffold build infrastructure and Hosting.Web.Server with placeholder coverage`

---

## Task 3: Hosting.Worker (TDD)

The props ladder from Task 2 is in place. This task adds only the `Hosting.Worker` project pair.

**Files:**
- Create: `src/Hosting.Worker/Hosting.Worker.csproj`
- Create: `tests/Hosting.Worker.Tests/Hosting.Worker.Tests.csproj`
- Create: `tests/Hosting.Worker.Tests/PlaceholderTests.cs`
- Create: `src/Hosting.Worker/Placeholder.cs`
- Create: `src/Hosting.Worker/Program.cs`

**Interfaces:**
- Consumes: `src/Directory.Build.props`, `tests/Directory.Build.props`, `tests/Directory.Packages.props` from Task 2.
- Produces: `Norse.Hosting.Worker` (compilable Worker-SDK skeleton with `internal static Placeholder.ServiceName(bool)`) and `Norse.Hosting.Worker.Tests` with 2/2 passing tests.

- [ ] **Step 1: Create `src/Hosting.Worker/Hosting.Worker.csproj`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Hosting.Worker/Hosting.Worker.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
	<PropertyGroup>
		<ContainerBaseImage>mcr.microsoft.com/dotnet/nightly/runtime:$(ContainerDotNetVersion)</ContainerBaseImage>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.Extensions.Hosting" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Create `tests/Hosting.Worker.Tests/Hosting.Worker.Tests.csproj`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/tests/Hosting.Worker.Tests/Hosting.Worker.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="..\..\src\Hosting.Worker\Hosting.Worker.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 3: Write `PlaceholderTests.cs` — failing test (red)**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/tests/Hosting.Worker.Tests/PlaceholderTests.cs`:

```csharp
namespace Norse.Hosting.Worker.Tests;

public class PlaceholderTests
{
	[Fact]
	public void ServiceName_returns_short_form() =>
		Placeholder.ServiceName().ShouldBe("Hosting.Worker");

	[Fact]
	public void ServiceName_returns_qualified_form() =>
		Placeholder.ServiceName(qualified: true).ShouldBe("Norse.Hosting.Worker");
}
```

- [ ] **Step 4: Run — confirm compile failure (red)**

```bash
dotnet build /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/tests/Hosting.Worker.Tests/Hosting.Worker.Tests.csproj
```

Expected: build failure with `CS0103 The name 'Placeholder' does not exist in the current context`. Any compile error is the correct red signal.

- [ ] **Step 5: Write `Placeholder.cs`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Hosting.Worker/Placeholder.cs`:

```csharp
namespace Norse.Hosting.Worker;

static class Placeholder
{
	internal static string ServiceName(bool qualified = false) =>
		qualified ? "Norse.Hosting.Worker" : "Hosting.Worker";
}
```

- [ ] **Step 6: Write `Program.cs`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Hosting.Worker/Program.cs`:

```csharp
var builder = Host.CreateApplicationBuilder(args);
await builder.Build().RunAsync();
```

- [ ] **Step 7: Run tests — confirm 2/2 pass (green)**

```bash
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/tests/Hosting.Worker.Tests/Hosting.Worker.Tests.csproj -c Release
```

Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`

- [ ] **Step 8: Build source project — confirm 0 warnings**

```bash
dotnet build /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Hosting.Worker/Hosting.Worker.csproj -c Release
```

Expected: `Build succeeded. 0 Warning(s)  0 Error(s)`

- [ ] **Step 9: Stage and stop**

```bash
git -C Yggdrasil add src/Hosting.Worker tests/Hosting.Worker.Tests
git -C Yggdrasil status
```

Proposed commit message (Yggdrasil): `feat: add Hosting.Worker with placeholder coverage`

---

## Task 4: Hosting.Migrations.Service (TDD)

**Files:**
- Create: `src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj`
- Create: `tests/Hosting.Migrations.Service.Tests/Hosting.Migrations.Service.Tests.csproj`
- Create: `tests/Hosting.Migrations.Service.Tests/PlaceholderTests.cs`
- Create: `src/Hosting.Migrations.Service/Placeholder.cs`
- Create: `src/Hosting.Migrations.Service/Program.cs`

**Interfaces:**
- Consumes: `src/Directory.Build.props`, `tests/Directory.Build.props`, `tests/Directory.Packages.props` from Task 2.
- Produces: `Norse.Hosting.Migrations.Service` (compilable Worker-SDK skeleton with `internal static Placeholder.ServiceName(bool)`) and `Norse.Hosting.Migrations.Service.Tests` with 2/2 passing tests.

- [ ] **Step 1: Create `src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
	<PropertyGroup>
		<ContainerBaseImage>mcr.microsoft.com/dotnet/nightly/runtime:$(ContainerDotNetVersion)</ContainerBaseImage>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.Extensions.Hosting" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Create `tests/Hosting.Migrations.Service.Tests/Hosting.Migrations.Service.Tests.csproj`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/tests/Hosting.Migrations.Service.Tests/Hosting.Migrations.Service.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="..\..\src\Hosting.Migrations.Service\Hosting.Migrations.Service.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 3: Write `PlaceholderTests.cs` — failing test (red)**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/tests/Hosting.Migrations.Service.Tests/PlaceholderTests.cs`:

```csharp
namespace Norse.Hosting.Migrations.Service.Tests;

public class PlaceholderTests
{
	[Fact]
	public void ServiceName_returns_short_form() =>
		Placeholder.ServiceName().ShouldBe("Hosting.Migrations.Service");

	[Fact]
	public void ServiceName_returns_qualified_form() =>
		Placeholder.ServiceName(qualified: true).ShouldBe("Norse.Hosting.Migrations.Service");
}
```

- [ ] **Step 4: Run — confirm compile failure (red)**

```bash
dotnet build /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/tests/Hosting.Migrations.Service.Tests/Hosting.Migrations.Service.Tests.csproj
```

Expected: build failure with `CS0103 The name 'Placeholder' does not exist in the current context`. Any compile error is the correct red signal.

- [ ] **Step 5: Write `Placeholder.cs`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Hosting.Migrations.Service/Placeholder.cs`:

```csharp
namespace Norse.Hosting.Migrations.Service;

static class Placeholder
{
	internal static string ServiceName(bool qualified = false) =>
		qualified ? "Norse.Hosting.Migrations.Service" : "Hosting.Migrations.Service";
}
```

- [ ] **Step 6: Write `Program.cs`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Hosting.Migrations.Service/Program.cs`:

```csharp
var builder = Host.CreateApplicationBuilder(args);
await builder.Build().RunAsync();
```

- [ ] **Step 7: Run tests — confirm 2/2 pass (green)**

```bash
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/tests/Hosting.Migrations.Service.Tests/Hosting.Migrations.Service.Tests.csproj -c Release
```

Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`

- [ ] **Step 8: Build source project — confirm 0 warnings**

```bash
dotnet build /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj -c Release
```

Expected: `Build succeeded. 0 Warning(s)  0 Error(s)`

- [ ] **Step 9: Stage and stop**

```bash
git -C Yggdrasil add src/Hosting.Migrations.Service tests/Hosting.Migrations.Service.Tests
git -C Yggdrasil status
```

Proposed commit message (Yggdrasil): `feat: add Hosting.Migrations.Service with placeholder coverage`

---

## Task 5: Solution file + integration verification

**Files:**
- Create: `Yggdrasil.slnx`

**Interfaces:**
- Consumes: all six projects from Tasks 2–4.
- Produces: a solution tying all six projects together for IDE and CI; full green suite confirmed under `--coverage`; local container publish verified for each main project.

- [ ] **Step 1: Create `Yggdrasil.slnx`**

Create `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/Yggdrasil.slnx`. No solution folders — six projects do not warrant nesting. Projects listed alphabetically within each group.

```xml
<Solution>
	<Project Path="src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj" />
	<Project Path="src/Hosting.Web.Server/Hosting.Web.Server.csproj" />
	<Project Path="src/Hosting.Worker/Hosting.Worker.csproj" />
	<Project Path="tests/Hosting.Migrations.Service.Tests/Hosting.Migrations.Service.Tests.csproj" />
	<Project Path="tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj" />
	<Project Path="tests/Hosting.Worker.Tests/Hosting.Worker.Tests.csproj" />
</Solution>
```

- [ ] **Step 2: Build solution — confirm 0 warnings across all six projects**

```bash
dotnet build /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/Yggdrasil.slnx -c Release
```

Expected: `Build succeeded. 0 Warning(s)  0 Error(s)` for all six projects. Any warning is a build error under `TreatWarningsAsErrors=true` — fix before continuing.

- [ ] **Step 3: Run all tests with coverage — confirm 6/6 pass**

```bash
dotnet test /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/Yggdrasil.slnx -c Release --coverage
```

Expected: `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`

- [ ] **Step 4: Local container publish smoke — verify all three images build**

These commands use `--arch arm64` for the Snapdragon X1 Elite host. Note: `release-container.yml` (CI) uses `--arch x64` — do not change the CI workflow.

```bash
dotnet publish /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj \
  --os linux --arch arm64 -c Release /t:PublishContainer \
  /p:ContainerRepository=norsearchitecture/hosting/migrations \
  /p:ContainerImageTag=scaffold-smoke
docker image ls norsearchitecture/hosting/migrations:scaffold-smoke
docker image rm norsearchitecture/hosting/migrations:scaffold-smoke

dotnet publish /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj \
  --os linux --arch arm64 -c Release /t:PublishContainer \
  /p:ContainerRepository=norsearchitecture/hosting/web \
  /p:ContainerImageTag=scaffold-smoke
docker image ls norsearchitecture/hosting/web:scaffold-smoke
docker image rm norsearchitecture/hosting/web:scaffold-smoke

dotnet publish /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/src/Hosting.Worker/Hosting.Worker.csproj \
  --os linux --arch arm64 -c Release /t:PublishContainer \
  /p:ContainerRepository=norsearchitecture/hosting/worker \
  /p:ContainerImageTag=scaffold-smoke
docker image ls norsearchitecture/hosting/worker:scaffold-smoke
docker image rm norsearchitecture/hosting/worker:scaffold-smoke
```

Expected: each `docker image ls` shows the image with tag `scaffold-smoke` before removal.

- [ ] **Step 5: Stage and stop**

```bash
git -C Yggdrasil add Yggdrasil.slnx
git -C Yggdrasil status
```

Proposed commit message (Yggdrasil): `feat: add Yggdrasil.slnx wiring all six projects`

---

## Task 6: Documentation sync

Boy-scout law (Bifrost CLAUDE.md §6): `CLAUDE.md` and `README.md` both describe Yggdrasil as "a bare shell." After this plan executes, that is false. Update both in the same change.

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: scaffolded state from Tasks 2–5.
- Produces: accurate paired docs; no later task depends on this one.

- [ ] **Step 1: Update `CLAUDE.md` §1 — replace "bare shell" paragraph**

In `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/CLAUDE.md`, locate and replace the paragraph starting with "This repo is currently a bare shell (LICENSE only)":

```
This repo is currently a bare shell (LICENSE only) — no specs have converged here yet. Before writing any code: brainstorm → spec → plan, recorded in `../Glitnir/docs/Yggdrasil/`, per the org's spec-first discipline. Do not scaffold a project structure ahead of a converged spec. A hosting plan is already filed (`../Glitnir/docs/Yggdrasil/plans/2026-05-20-yggdrasil-hosting.md`, halted at the plan stage awaiting greenlight) — when it (or any later plan) executes, its REQUIRED SUB-SKILL line names `superpowers:subagent-driven-development` as the default (not a recommendation among equals — `executing-plans` is the narrow fallback for separate-session review checkpoints) paired with `superpowers:test-driven-development` — implementation here is subagent-orchestrated and test-driven, never one without the other (`../Glitnir/CLAUDE.md` §2.8).
```

Replace with:

```
This repo is scaffolded — three source projects (`src/Hosting.Web.Server`, `src/Hosting.Worker`, `src/Hosting.Migrations.Service`) and three matching test projects wired into `Yggdrasil.slnx`. Each source project contains a `Placeholder.cs` covering both branches of a ternary (so CI coverage is well-defined from day one) and a `Program.cs` stub that compiles and runs clean with no hosted services. These stubs are replaced wholesale when the hosting abstractions from Asgard and Midgard land. Every subsequent implementation plan for this realm follows the same discipline: brainstorm → spec → plan in `../Glitnir/docs/Yggdrasil/`, greenlit by the human, then code. Each plan's REQUIRED SUB-SKILL line names `superpowers:subagent-driven-development` as the default (not a recommendation among equals — `executing-plans` is the narrow fallback for separate-session review checkpoints) paired with `superpowers:test-driven-development` — implementation here is subagent-orchestrated and test-driven, never one without the other (`../Glitnir/CLAUDE.md` §2.8).
```

- [ ] **Step 2: Update `README.md` §Status — replace "bare shell"**

In `/home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil/README.md`, locate the `## Status` section:

```
This realm is currently a bare shell — no code, no specs converged yet. Design happens first: brainstorm → spec → plan, recorded in Glitnir's `docs/Yggdrasil/`, before any project is scaffolded here.
```

Replace with:

```
Scaffolded — `Hosting.Web.Server`, `Hosting.Worker`, and `Hosting.Migrations.Service` exist as minimal deployable stubs: placeholder code, passing tests, and container-publishable via `dotnet publish /t:PublishContainer`. Real hosting abstractions land once Asgard and Midgard have shipped their foundations. Each subsequent type surface follows the spec-first discipline: brainstorm → spec → plan in [Glitnir](https://github.com/NorseArchitecture/Glitnir)'s `docs/Yggdrasil/`, greenlit by the human, then code.
```

- [ ] **Step 3: Stage and stop**

```bash
git -C Yggdrasil add CLAUDE.md README.md
git -C Yggdrasil status
```

Proposed commit message (Yggdrasil): `docs: update CLAUDE.md and README.md to reflect scaffolded state`
