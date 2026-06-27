# Yggdrasil Runtime Scaffold Design

**Date:** 2026-06-27
**Status:** Approved in session 2026-06-27.
**Owner:** Buvy
**Companion specs:**
- `2026-05-20-yggdrasil-hosting-design.md` — full plugin runtime design; this spec delivers the bare deployable shells that design requires.
- `../Platform/specs/2026-06-27-yggdrasil-tag-release-design.md` — the container release ceremony; Task 5 of that plan requires these projects to exist and `dotnet publish /t:PublishContainer` to succeed before the smoke test can run.

---

## 1. Motivation

The tag-and-release plan's smoke test (Task 5) is gated on three container-publishable projects existing in Yggdrasil. CI (`ci-build-test.yml`) enforces a 60% branch coverage floor for every realm, so test projects must land alongside the main projects — not later. This spec describes the minimal scaffold: compilable, container-publishable, covered by tests sufficient to clear the org floor. No business logic, no Norse package references, no hosting abstractions yet; those land when Asgard and Midgard have shipped their foundations.

**Note on the tag-release plan:** `2026-06-27-yggdrasil-tag-release.md` lists project paths without the `src/` prefix. Those paths were written before this design session settled the layout. The correct paths (with `src/`) are authoritative here; the release-container workflow steps will use these paths.

## 2. Project Layout

All six projects follow the universal platform convention: `src/{ProjectName}/{ProjectName}.csproj`. Test projects live under `tests/` as always.

```
Yggdrasil/
  src/
    Directory.Build.props          ← new; InternalsVisibleTo + IsPackable, no NuGet ceremony
    Directory.Build.targets        ← existing; OutputType removed (see §3.1)
    Hosting.Migrations.Service/
      Hosting.Migrations.Service.csproj
      Placeholder.cs
      Program.cs
    Hosting.Web.Server/
      Hosting.Web.Server.csproj
      Placeholder.cs
      Program.cs
    Hosting.Worker/
      Hosting.Worker.csproj
      Placeholder.cs
      Program.cs
  tests/
    Directory.Build.props          ← new; floating test packages, MTP runner config
    Directory.Build.targets        ← existing (OutputType Exe)
    Directory.Packages.props       ← new; CPM off
    Hosting.Migrations.Service.Tests/
      Hosting.Migrations.Service.Tests.csproj
      PlaceholderTests.cs
    Hosting.Web.Server.Tests/
      Hosting.Web.Server.Tests.csproj
      PlaceholderTests.cs
    Hosting.Worker.Tests/
      Hosting.Worker.Tests.csproj
      PlaceholderTests.cs
  Yggdrasil.slnx                   ← new; all six projects, no solution folders
```

## 3. Source Build Infrastructure

### 3.1 `src/Directory.Build.targets` — remove `OutputType`

The existing file contained `<OutputType>Library</OutputType>` — a misfire from the initial MSBuild setup, already removed. The SDK (`Microsoft.NET.Sdk.Web`, `Microsoft.NET.Sdk.Worker`, `Microsoft.NET.Sdk`) sets the correct `OutputType` for each project type automatically.

### 3.2 `src/Directory.Build.props` — new file

Mirrors Svartalfheim's shape without the NuGet ceremony (no `MinVer`, no `PackageId`, no `Authors`, no `Deterministic` for NuGet). Nothing in Yggdrasil's `src/` is packable.

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

`InternalsVisibleTo` is hoisted here (not repeated per-csproj) — same sanctioned pattern as Svartalfheim's `src/Directory.Build.props`. `IsAotCompatible` is deliberately absent: that is a library-packaging concern and the reason Yggdrasil is excluded from the `.github/config` scatter. Nothing in this `src/` tree is a packable library.

## 4. Main Project Specification

### 4.1 SDK and Container Base Image

| Project folder | SDK | `ContainerBaseImage` |
|---|---|---|
| `src/Hosting.Web.Server` | `Microsoft.NET.Sdk.Web` | `mcr.microsoft.com/dotnet/aspnet:$(DotNetVersion)` |
| `src/Hosting.Worker` | `Microsoft.NET.Sdk.Worker` | `mcr.microsoft.com/dotnet/runtime:$(DotNetVersion)` |
| `src/Hosting.Migrations.Service` | `Microsoft.NET.Sdk.Worker` | `mcr.microsoft.com/dotnet/runtime:$(DotNetVersion)` |

`Microsoft.NET.Sdk.Web` and `Microsoft.NET.Sdk.Worker` both default to `OutputType=Exe`. With `<OutputType>Library</OutputType>` removed from `src/Directory.Build.targets`, those SDK defaults are respected. `$(DotNetVersion)` is defined in `Directory.Packages.props` (§5.2) and updated monthly.

### 4.2 `.csproj` Shape

No `InternalsVisibleTo` per-csproj — hoisted to `src/Directory.Build.props`. No explicit `OutputType` needed — the SDK sets it and the conditional targets file no longer overrides it.

```xml
<!-- Hosting.Web.Server.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
	<PropertyGroup>
		<ContainerBaseImage>mcr.microsoft.com/dotnet/aspnet:$(DotNetVersion)</ContainerBaseImage>
	</PropertyGroup>
</Project>
```

```xml
<!-- Hosting.Worker.csproj and Hosting.Migrations.Service.csproj — identical except project name -->
<Project Sdk="Microsoft.NET.Sdk.Worker">
	<PropertyGroup>
		<ContainerBaseImage>mcr.microsoft.com/dotnet/runtime:$(DotNetVersion)</ContainerBaseImage>
	</PropertyGroup>
</Project>
```

### 4.3 `Placeholder.cs`

A trivial internal static class with one ternary — gives the coverage tool a branch to measure so the threshold calculation is well-defined (not `null`). Each project gets a copy in its own namespace.

```csharp
namespace Norse.Hosting.Web.Server;

static class Placeholder
{
	internal static string ServiceName(bool qualified = false) =>
		qualified ? "Norse.Hosting.Web.Server" : "Hosting.Web.Server";
}
```

Adapt the namespace and string literals for `Norse.Hosting.Worker` and `Norse.Hosting.Migrations.Service` respectively.

### 4.4 `Program.cs` Stubs

```csharp
// Hosting.Web.Server/Program.cs
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();
```

```csharp
// Hosting.Worker/Program.cs and Hosting.Migrations.Service/Program.cs — identical
var builder = Host.CreateApplicationBuilder(args);
await builder.Build().RunAsync();
```

These stubs compile clean and produce runnable binaries. They are replaced wholesale when the hosting abstractions from Asgard and Midgard are ready.

## 5. Test and CPM Infrastructure

### 5.1 `tests/Directory.Packages.props`

Turns CPM off for the entire `tests/` subtree so test packages always float to latest without requiring edits to the root props file.

```xml
<Project>
	<PropertyGroup>
		<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
	</PropertyGroup>
</Project>
```

### 5.2 `Directory.Packages.props` — `DotNetVersion` addition

Add `<DotNetVersion>` to the existing `<PropertyGroup>` alongside the Norse realm version properties. Updated manually each month when Microsoft ships the next .NET release.

```xml
<DotNetVersion>11.0.0-preview.5.26302.115</DotNetVersion>
```

This is the only .NET runtime version pin in Yggdrasil. A runtime update is a single-line edit here; no project files change, no realm republish needed.

### 5.3 `tests/Directory.Build.props`

Mirrors Svartalfheim's shape exactly. With CPM disabled in `tests/`, `Version="*"` on each `PackageReference` is legal and floats to latest-stable at restore time.

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

### 5.4 Test `.csproj` Files

```xml
<!-- Hosting.Web.Server.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="..\..\src\Hosting.Web.Server\Hosting.Web.Server.csproj" />
	</ItemGroup>
</Project>
```

Identical shape for `Hosting.Worker.Tests` and `Hosting.Migrations.Service.Tests` — only the `ProjectReference` path differs. `OutputType>Exe`, `IsPackable>false`, `IsTestProject>true`, and all test packages flow from `tests/Directory.Build.props`.

### 5.5 `PlaceholderTests.cs`

Two tests covering both branches of `Placeholder.ServiceName`:

```csharp
// Hosting.Web.Server.Tests/PlaceholderTests.cs
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

Adapt namespace and expected strings for each project. Both branches of the ternary are hit → 100% branch coverage on all user code.

## 6. Solution File

`Yggdrasil.slnx` at repo root, `.slnx` format, referencing all six projects. No solution folders — six projects do not warrant nesting. Named for the repo (lore name), matching the per-realm convention.

## 7. Container Publish Command

Corrected project paths for `release-container.yml` (Task 2 of the tag-release plan — the plan's paths predate this design and must be updated):

```bash
dotnet publish src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj \
  --os linux --arch x64 -c Release /t:PublishContainer \
  /p:ContainerRepository=norsearchitecture/hosting/migrations \
  /p:ContainerImageTag={version}

dotnet publish src/Hosting.Web.Server/Hosting.Web.Server.csproj \
  --os linux --arch x64 -c Release /t:PublishContainer \
  /p:ContainerRepository=norsearchitecture/hosting/web \
  /p:ContainerImageTag={version}

dotnet publish src/Hosting.Worker/Hosting.Worker.csproj \
  --os linux --arch x64 -c Release /t:PublishContainer \
  /p:ContainerRepository=norsearchitecture/hosting/worker \
  /p:ContainerImageTag={version}
```

## 8. What This Spec Does Not Include

- **Norse hosting abstractions** (`IHostPlugin`, `AddNorseWebHost`, etc.) — Asgard + Midgard work; stubs are replaced wholesale when they land.
- **`ServiceDefaults`** — will be a library project under `src/` when the Midgard/Yggdrasil service-defaults decision is implemented; it will follow `src/Directory.Build.props` but may warrant its own `IsAotCompatible` consideration at that time.
- **`minimum_coverage` in `ci.yml`** — the org floor (60%) applies automatically; a per-realm override is set when the real hosting tests land and a higher threshold is warranted.
- **NorseRef items** — no Norse package dependencies in this scaffold.

## 9. Done Criteria

- [ ] `src/Directory.Build.props` created (no NuGet ceremony, `InternalsVisibleTo`, `IsPackable`).
- [ ] `src/Directory.Build.targets` updated — `OutputType>Library` removed.
- [ ] Three main projects exist under `src/` with `Placeholder.cs` and `Program.cs`.
- [ ] `<DotNetVersion>` property added to root `Directory.Packages.props`.
- [ ] `tests/Directory.Packages.props` created (CPM off).
- [ ] `tests/Directory.Build.props` created (floating test packages, MTP runner config).
- [ ] Three test projects exist under `tests/`, each referencing the corresponding main project.
- [ ] `Yggdrasil.slnx` created at repo root referencing all six projects.
- [ ] `dotnet build -c Release` succeeds from Yggdrasil repo root with no warnings.
- [ ] `dotnet test -c Release --coverage` succeeds; all six tests pass.
- [ ] `dotnet publish /t:PublishContainer` succeeds for each main project (local daemon, smoke tag).
