# Build Enforcement POC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove the MSBuild enforcement law (spec: `docs/Platform/specs/2026-06-05-build-enforcement-design.md`) end-to-end as a self-contained replica under `poc/build/`, producing a verdict (`FINDINGS.md`) that later seeds the real Glitnir root.

**Architecture:** `poc/build/` replicates the full tree taxonomy — replica root law, governed layers (`src/`, `tests/`, `benchmarks/`) chained via `GetPathOfFileAbove`, severed floors (`poc/`, `tests/smoke/`) — plus probe projects with `#if CANARY` violations and a PowerShell harness asserting properties land and diagnostics fire **as errors**. Follows the `poc/pg19-temporal/` house pattern (self-contained folder, own script, `FINDINGS.md`).

**Tech Stack:** .NET 11 preview SDK (repo `global.json`), MSBuild `Directory.Build.props` layering, PowerShell 7.

---

## House Rules That Override This Plan's Defaults

1. **No automatic git commits** (CLAUDE.md §8). Every "commit" step below is a **staging checkpoint**: `git add` the listed files, show `git status --short`, and halt for the human to commit. Never run `git commit`.
2. **Tabs** for indentation in all XML, C#, and PowerShell content (2-space width is display-only). Markdown files use the content shown verbatim.
3. **US English** everywhere.
4. The real Glitnir root gets **no files** from this plan. Everything lands under `poc/build/`, plus two doc updates (Task 9).

## The Empirical-Pin Protocol (referenced by Tasks 3–5)

Preview SDK analyzer sets shift. If an expected diagnostic does not fire, or an *unexpected* diagnostic fires on lawful code:

1. Confirm the rule still exists and its default tier (`microsoft_docs_search` for "CAxxxx" — the rule's "Enabled by default" line).
2. If a canary rule moved tiers, substitute another rule meeting the same criterion (for category canaries: **disabled in `latest-Recommended`, enabled by `latest-All`** in that category), update the canary source, its doc comment, and the harness's expected-ID list.
3. If an unexpected rule fires on lawful code, adjust the lawful code if trivial; otherwise record the rule as a NoWarn-candidate finding.
4. Record every deviation in `poc/build/FINDINGS.md` — surprises are the POC's product, not noise.

---

### Task 1: POC scaffold + replica root law

**Files:**
- Create: `poc/build/.gitignore`
- Create: `poc/build/README.md`
- Create: `poc/build/Directory.Build.props`
- Create: `poc/build/Directory.Build.targets`

- [ ] **Step 1: Create `poc/build/.gitignore`**

```gitignore
artifacts/
```

(The artifacts layout consolidates all build litter; severed floors re-declare it, so `artifacts/` appears at three depths under `poc/build/` — the unanchored pattern covers all of them. Repo root currently has no `.gitignore`; per the pg19 pattern, POCs carry their own dotfiles.)

- [ ] **Step 2: Create `poc/build/README.md`**

```markdown
# Build Enforcement POC

Self-contained replica proving the MSBuild enforcement law before it seeds the
real Glitnir root. Spec: `docs/Platform/specs/2026-06-05-build-enforcement-design.md`.

## Layout

- `Directory.Build.props` — replica of the future Glitnir-root law
- `src/`, `tests/`, `benchmarks/` — governed layers, chained to the law
- `poc/`, `tests/smoke/` — severed floors (standalone props, no chain)
- `src/Glitnir.Probe/` etc. — probe projects; violations live behind `#if CANARY`
- `Verify-Enforcement.ps1` — the harness; exit 0 = law verified

## Run

    pwsh ./Verify-Enforcement.ps1

## Verdict

See `FINDINGS.md`.
```

- [ ] **Step 3: Create `poc/build/Directory.Build.props`** (the replica root law — spec §3 verbatim)

```xml
<Project>
	<PropertyGroup Label="Platform">
		<TargetFramework>net11.0</TargetFramework>
		<LangVersion>preview</LangVersion>
		<Nullable>enable</Nullable>
		<ImplicitUsings>enable</ImplicitUsings>
	</PropertyGroup>

	<PropertyGroup Label="Output">
		<UseArtifactsOutput>true</UseArtifactsOutput>
		<ArtifactsPath>$(MSBuildThisFileDirectory)artifacts</ArtifactsPath>
	</PropertyGroup>

	<PropertyGroup Label="Enforcement">
		<AnalysisLevel>latest-Recommended</AnalysisLevel>
		<AnalysisLevelSecurity>latest-All</AnalysisLevelSecurity>
		<AnalysisLevelPerformance>latest-All</AnalysisLevelPerformance>
		<AnalysisLevelReliability>latest-All</AnalysisLevelReliability>
		<AnalysisLevelUsage>latest-All</AnalysisLevelUsage>
		<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
		<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
		<MSBuildTreatWarningsAsErrors>true</MSBuildTreatWarningsAsErrors>
		<GenerateDocumentationFile>true</GenerateDocumentationFile>
	</PropertyGroup>

	<PropertyGroup Label="Restore">
		<NuGetAudit>true</NuGetAudit>
		<NuGetAuditMode>all</NuGetAuditMode>
		<NuGetAuditLevel>low</NuGetAuditLevel>
	</PropertyGroup>

	<PropertyGroup Label="CI" Condition="('$(GITHUB_ACTIONS)' == 'true') or ('$(TF_BUILD)' == 'true')">
		<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
	</PropertyGroup>
</Project>
```

Note: `Directory.Build.props` imports **before** the SDK's own `.props`, so SDK defaults are not visible during its evaluation; our values survive because the SDK sets its defaults conditionally (`Condition="'$(Prop)' == ''"`).

- [ ] **Step 4: Create `poc/build/Directory.Build.targets`**

```xml
<Project>
	<!--
		Replica of the future Glitnir-root Directory.Build.targets: deliberately empty.
		The real-tree file is reserved for the UseProjectReferences cross-repo switching
		session. This file also isolates the replica from any future real-root targets
		file — MSBuild auto-imports the nearest Directory.Build.targets only.
	-->
</Project>
```

- [ ] **Step 5: Staging checkpoint**

```powershell
git add poc/build/.gitignore poc/build/README.md poc/build/Directory.Build.props poc/build/Directory.Build.targets
git status --short
```

Halt for human commit.

---

### Task 2: `src/` governed layer + lawful probe

**Files:**
- Create: `poc/build/src/Directory.Build.props`
- Create: `poc/build/src/Glitnir.Probe/Glitnir.Probe.csproj`
- Create: `poc/build/src/Glitnir.Probe/LawfulCitizen.cs`

- [ ] **Step 1: Create `poc/build/src/Directory.Build.props`** (chain + the sanctioned internals door)

```xml
<Project>
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)..'))" />

	<ItemGroup>
		<InternalsVisibleTo Include="$(AssemblyName).Tests" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Create `poc/build/src/Glitnir.Probe/Glitnir.Probe.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<DefineConstants Condition="'$(EnableCanaries)' == 'true'">$(DefineConstants);CANARY</DefineConstants>
	</PropertyGroup>

</Project>
```

No TFM, no LangVersion, no Nullable — everything inherits. The `DefineConstants` append is conditional on the `EnableCanaries` global property; deliberately **not** `-p:DefineConstants=CANARY` from the CLI, which would clobber SDK-computed constants (`NET11_0`, `TRACE`, …).

- [ ] **Step 3: Create `poc/build/src/Glitnir.Probe/LawfulCitizen.cs`**

```csharp
namespace Glitnir.Probe;

/// <summary>Proves the law tolerates lawful code: documented public surface, internal detail.</summary>
public sealed class LawfulCitizen
{
	/// <summary>Gets the realm this citizen answers to.</summary>
	public string Realm { get; } = InternalRealm;

	internal static string InternalRealm => "src";
}
```

(Auto-property with initializer, not an expression-bodied instance member — dodges CA1822 "mark members as static", which the Performance `latest-All` escalation turns into an error. `InternalRealm` exists for Task 4's `InternalsVisibleTo` proof.)

- [ ] **Step 4: Verify landing (property introspection, no build)**

```powershell
dotnet msbuild poc/build/src/Glitnir.Probe/Glitnir.Probe.csproj -getProperty:AnalysisLevel,AnalysisLevelSecurity,AnalysisLevelPerformance,AnalysisLevelReliability,AnalysisLevelUsage,TreatWarningsAsErrors,GenerateDocumentationFile,UseArtifactsOutput
```

Expected: JSON with `AnalysisLevel = latest-Recommended`, all four category levels = `latest-All`, `TreatWarningsAsErrors = true`, `GenerateDocumentationFile = true`, `UseArtifactsOutput = true`. Any mismatch means the chain import is broken — stop and fix before proceeding.

- [ ] **Step 5: Verify clean build passes**

```powershell
dotnet build poc/build/src/Glitnir.Probe/Glitnir.Probe.csproj -tl:off --nologo
```

Expected: exit 0, `Build succeeded`. If any diagnostic fires on this lawful code, apply the Empirical-Pin Protocol.

- [ ] **Step 6: Verify artifacts landed in the consolidated layout**

```powershell
Test-Path poc/build/artifacts/bin/Glitnir.Probe
```

Expected: `True`. Also confirm no `poc/build/src/Glitnir.Probe/bin` exists.

- [ ] **Step 7: Staging checkpoint**

```powershell
git add poc/build/src
git status --short
```

Halt for human commit.

---

### Task 3: `src/` canaries — the law fires as errors

**Files:**
- Create: `poc/build/src/Glitnir.Probe/Canaries.cs`

- [ ] **Step 1: Create `poc/build/src/Glitnir.Probe/Canaries.cs`**

```csharp
#if CANARY
namespace Glitnir.Probe;

/// <summary>One member per rule the law must catch. Compiled only when EnableCanaries=true.</summary>
public static class CanaryNest
{
	/// <summary>CA5394 — insecure randomness (Security, latest-All-only).</summary>
	public static int InsecureRandom() => new Random().Next();

	/// <summary>CA2007 — await without ConfigureAwait (Reliability, latest-All-only).</summary>
	public static async Task<int> MissingConfigureAwait()
	{
		await Task.Delay(1);
		return 1;
	}

	/// <summary>CA2201 — reserved exception type (Usage, latest-All-only).</summary>
	public static void ReservedException() => throw new Exception("canary");

	/// <summary>CA2200 — rethrow destroys the stack (Usage, latest-Recommended baseline).</summary>
	public static void RethrowWrong()
	{
		try
		{
			ReservedException();
		}
		catch (InvalidOperationException ex)
		{
			throw ex;
		}
	}

	/// <summary>CS0219 — assigned, never used (compiler warning, errors via the ratchet).</summary>
	public static void UnusedLocal()
	{
		int unused = 42;
	}
}

/// <summary>CA1810 — static constructor instead of inline init (Performance, latest-All-only).</summary>
public sealed class StaticCtorCanary
{
	static StaticCtorCanary()
	{
		Seed = 7;
	}

	internal static int Seed { get; }
}

public sealed class UndocumentedPublic;
#endif
```

The last class is the CS1591 canary — deliberately undocumented. All `CanaryNest` members are static so CA1822 cannot add noise to the expected-ID set.

- [ ] **Step 2: Verify the clean build is still green** (canaries must be invisible by default)

```powershell
dotnet build poc/build/src/Glitnir.Probe/Glitnir.Probe.csproj -tl:off --nologo
```

Expected: exit 0.

- [ ] **Step 3: Verify the canaried build fails with every expected ID as `error`**

```powershell
dotnet build poc/build/src/Glitnir.Probe/Glitnir.Probe.csproj -tl:off --nologo -p:EnableCanaries=true 2>&1 | Select-String -Pattern '(error|warning) (CA5394|CA1810|CA2007|CA2201|CA2200|CS0219|CS1591)'
```

Expected: exit code non-zero; each of the seven IDs appears at least once **as `error`**. Any ID appearing as `warning` means the ratchet is broken — stop. Any ID absent: apply the Empirical-Pin Protocol (likely candidates if substitution is needed — Security: CA5395/CA5399; Performance: CA1813/CA1849; Reliability: CA2008/CA2016 tier-check first; Usage: CA2215/CA2234).

- [ ] **Step 4: Staging checkpoint**

```powershell
git add poc/build/src/Glitnir.Probe/Canaries.cs
git status --short
```

Halt for human commit.

---

### Task 4: `tests/` governed layer + probe (chain hop, NoWarn delta, IVT door)

**Files:**
- Create: `poc/build/tests/Directory.Build.props`
- Create: `poc/build/tests/Glitnir.Probe.Tests/Glitnir.Probe.Tests.csproj`
- Create: `poc/build/tests/Glitnir.Probe.Tests/UndocumentedDoorOpener.cs`
- Create: `poc/build/tests/Glitnir.Probe.Tests/Canaries.cs`

- [ ] **Step 1: Create `poc/build/tests/Directory.Build.props`** (chain + the noise ledger)

```xml
<Project>
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)..'))" />

	<PropertyGroup>
		<NoWarn>$(NoWarn);CS1591</NoWarn>
		<IsPackable>false</IsPackable>
	</PropertyGroup>
</Project>
```

- [ ] **Step 2: Create `poc/build/tests/Glitnir.Probe.Tests/Glitnir.Probe.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<DefineConstants Condition="'$(EnableCanaries)' == 'true'">$(DefineConstants);CANARY</DefineConstants>
	</PropertyGroup>

	<ItemGroup>
		<ProjectReference Include="..\..\src\Glitnir.Probe\Glitnir.Probe.csproj" UndefineProperties="EnableCanaries;EnableDocCanaries" />
	</ItemGroup>

</Project>
```

- [ ] **Step 3: Create `poc/build/tests/Glitnir.Probe.Tests/UndocumentedDoorOpener.cs`**

```csharp
using Glitnir.Probe;

namespace Glitnir.Probe.Tests;

public sealed class UndocumentedDoorOpener
{
	public string OpenedRealm { get; } = LawfulCitizen.InternalRealm;
}
```

Two proofs in one file: the undocumented `public` class compiles clean only if the `CS1591` NoWarn delta works, and `LawfulCitizen.InternalRealm` (an `internal` member) resolves only if the `InternalsVisibleTo "$(AssemblyName).Tests"` door opens for `Glitnir.Probe.Tests`.

- [ ] **Step 4: Create `poc/build/tests/Glitnir.Probe.Tests/Canaries.cs`**

```csharp
#if CANARY
namespace Glitnir.Probe.Tests;

/// <summary>Proves the law still fires after the second chain hop.</summary>
public static class ChainCanary
{
	/// <summary>CA2200 — rethrow destroys the stack (latest-Recommended baseline).</summary>
	public static void RethrowWrong()
	{
		try
		{
			throw new InvalidOperationException("canary");
		}
		catch (InvalidOperationException ex)
		{
			throw ex;
		}
	}
}
#endif
```

(`InvalidOperationException`, not `Exception` — keeps CA2201 out of this probe's expected set so CA2200 is isolated.)

- [ ] **Step 5: Verify landing — two-hop chain plus delta**

```powershell
dotnet msbuild poc/build/tests/Glitnir.Probe.Tests/Glitnir.Probe.Tests.csproj -getProperty:AnalysisLevelSecurity,TreatWarningsAsErrors,NoWarn,IsPackable
```

Expected: `AnalysisLevelSecurity = latest-All` (root law survived two hops), `TreatWarningsAsErrors = true`, `NoWarn` **contains** `CS1591`, `IsPackable = false`.

- [ ] **Step 6: Verify clean build passes** (this is the NoWarn-delta and IVT proof in one)

```powershell
dotnet build poc/build/tests/Glitnir.Probe.Tests/Glitnir.Probe.Tests.csproj -tl:off --nologo
```

Expected: exit 0. A CS1591 error here means the NoWarn delta failed; a CS0122 (inaccessible member) means the IVT door failed.

- [ ] **Step 7: Verify the canaried build fails with CA2200 as `error`**

```powershell
dotnet build poc/build/tests/Glitnir.Probe.Tests/Glitnir.Probe.Tests.csproj -tl:off --nologo -p:EnableCanaries=true 2>&1 | Select-String -Pattern '(error|warning) CA2200'
```

Expected: non-zero exit; `error CA2200` present; no `warning CA2200`. Note: `-p:EnableCanaries=true` is a global property and would flow into the referenced `Glitnir.Probe`, whose canaries would kill the dependency build before this probe ever compiles — the `UndefineProperties="EnableCanaries;EnableDocCanaries"` metadata on the ProjectReference strips it so only this probe's canaries fire (execution finding, 2026-06-05).

- [ ] **Step 8: Staging checkpoint**

```powershell
git add poc/build/tests/Directory.Build.props poc/build/tests/Glitnir.Probe.Tests
git status --short
```

Halt for human commit.

---

### Task 5: `benchmarks/` governed layer + probe

**Files:**
- Create: `poc/build/benchmarks/Directory.Build.props`
- Create: `poc/build/benchmarks/Glitnir.Probe.Benchmarks/Glitnir.Probe.Benchmarks.csproj`
- Create: `poc/build/benchmarks/Glitnir.Probe.Benchmarks/UndocumentedSurface.cs`
- Create: `poc/build/benchmarks/Glitnir.Probe.Benchmarks/Canaries.cs`

- [ ] **Step 1: Create `poc/build/benchmarks/Directory.Build.props`**

```xml
<Project>
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)..'))" />

	<PropertyGroup>
		<NoWarn>$(NoWarn);CS1591</NoWarn>
	</PropertyGroup>
</Project>
```

- [ ] **Step 2: Create `poc/build/benchmarks/Glitnir.Probe.Benchmarks/Glitnir.Probe.Benchmarks.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<DefineConstants Condition="'$(EnableCanaries)' == 'true'">$(DefineConstants);CANARY</DefineConstants>
	</PropertyGroup>

</Project>
```

- [ ] **Step 3: Create `poc/build/benchmarks/Glitnir.Probe.Benchmarks/UndocumentedSurface.cs`**

```csharp
namespace Glitnir.Probe.Benchmarks;

public sealed class UndocumentedSurface
{
	public int Iterations { get; } = 1;
}
```

- [ ] **Step 4: Create `poc/build/benchmarks/Glitnir.Probe.Benchmarks/Canaries.cs`**

```csharp
#if CANARY
namespace Glitnir.Probe.Benchmarks;

/// <summary>Proves the law still fires in the benchmarks layer.</summary>
public static class ChainCanary
{
	/// <summary>CA2200 — rethrow destroys the stack (latest-Recommended baseline).</summary>
	public static void RethrowWrong()
	{
		try
		{
			throw new InvalidOperationException("canary");
		}
		catch (InvalidOperationException ex)
		{
			throw ex;
		}
	}
}
#endif
```

- [ ] **Step 5: Verify the trio**

```powershell
dotnet msbuild poc/build/benchmarks/Glitnir.Probe.Benchmarks/Glitnir.Probe.Benchmarks.csproj -getProperty:AnalysisLevelSecurity,TreatWarningsAsErrors,NoWarn
dotnet build poc/build/benchmarks/Glitnir.Probe.Benchmarks/Glitnir.Probe.Benchmarks.csproj -tl:off --nologo
dotnet build poc/build/benchmarks/Glitnir.Probe.Benchmarks/Glitnir.Probe.Benchmarks.csproj -tl:off --nologo -p:EnableCanaries=true 2>&1 | Select-String -Pattern '(error|warning) CA2200'
```

Expected: landing (`latest-All` / `true` / contains `CS1591`); clean build exit 0; canaried build non-zero with `error CA2200`.

- [ ] **Step 6: Staging checkpoint**

```powershell
git add poc/build/benchmarks
git status --short
```

Halt for human commit.

---

### Task 6: `poc/` severed floor + severance witness

**Files:**
- Create: `poc/build/poc/Directory.Build.props`
- Create: `poc/build/poc/Glitnir.Probe.Severed/Glitnir.Probe.Severed.csproj`
- Create: `poc/build/poc/Glitnir.Probe.Severed/Outlaw.cs`

- [ ] **Step 1: Create `poc/build/poc/Directory.Build.props`** (standalone — **no** chain import; the absence is the mechanism)

```xml
<Project>
	<PropertyGroup>
		<TargetFramework>net11.0</TargetFramework>
		<LangVersion>preview</LangVersion>
		<Nullable>enable</Nullable>
		<ImplicitUsings>enable</ImplicitUsings>
		<UseArtifactsOutput>true</UseArtifactsOutput>
	</PropertyGroup>
</Project>
```

- [ ] **Step 2: Create `poc/build/poc/Glitnir.Probe.Severed/Glitnir.Probe.Severed.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
</Project>
```

- [ ] **Step 3: Create `poc/build/poc/Glitnir.Probe.Severed/Outlaw.cs`**

```csharp
namespace Glitnir.Probe.Severed;

public sealed class Outlaw
{
	public static int Roll()
	{
		int unused = 42;
		return new Random().Next();
	}
}
```

Under the law this file is multiple build errors (CS1591 ×2 implicitly via doc generation, CS0219 via the ratchet, CA5394 via the Security escalation). Severed, it must build with exit 0 — CS0219 may surface as a plain warning; that is correct severed behavior.

- [ ] **Step 4: Verify severance — landing + clean build**

```powershell
dotnet msbuild poc/build/poc/Glitnir.Probe.Severed/Glitnir.Probe.Severed.csproj -getProperty:AnalysisLevelSecurity,UseArtifactsOutput,TargetFramework
dotnet build poc/build/poc/Glitnir.Probe.Severed/Glitnir.Probe.Severed.csproj -tl:off --nologo
```

Expected: `AnalysisLevelSecurity` **empty** (the escalation never reached here), `UseArtifactsOutput = true`, `TargetFramework = net11.0`; build exit 0 (warnings acceptable). Artifacts land at `poc/build/poc/artifacts/` (relative to the severed props), not `poc/build/artifacts/` — confirm with `Test-Path poc/build/poc/artifacts/bin/Glitnir.Probe.Severed`.

- [ ] **Step 5: Staging checkpoint**

```powershell
git add poc/build/poc
git status --short
```

Halt for human commit.

---

### Task 7: `tests/smoke/` severed floor + smoke witness

**Files:**
- Create: `poc/build/tests/smoke/Directory.Build.props`
- Create: `poc/build/tests/smoke/Glitnir.Probe.Smoke/Glitnir.Probe.Smoke.csproj`
- Create: `poc/build/tests/smoke/Glitnir.Probe.Smoke/Program.cs`

(The spec ships the real `tests/smoke/` with no project — an empty proving ground proves nothing about *AOT*. But the **replica** needs a witness to prove the *props* work: nesting under `tests/` must sever from the `tests/` layer, and `PublishAot` must land. Build-only tonight; actual AOT publish arrives with the first real smoke subject.)

- [ ] **Step 1: Create `poc/build/tests/smoke/Directory.Build.props`**

```xml
<Project>
	<PropertyGroup>
		<TargetFramework>net11.0</TargetFramework>
		<LangVersion>preview</LangVersion>
		<Nullable>enable</Nullable>
		<ImplicitUsings>enable</ImplicitUsings>
		<UseArtifactsOutput>true</UseArtifactsOutput>
		<PublishAot>true</PublishAot>
	</PropertyGroup>
</Project>
```

- [ ] **Step 2: Create `poc/build/tests/smoke/Glitnir.Probe.Smoke/Glitnir.Probe.Smoke.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<OutputType>Exe</OutputType>
	</PropertyGroup>

</Project>
```

- [ ] **Step 3: Create `poc/build/tests/smoke/Glitnir.Probe.Smoke/Program.cs`**

```csharp
return 0;
```

(The exit-0/1 smoke shape in its smallest possible form.)

- [ ] **Step 4: Verify smoke severance — the critical nesting proof**

```powershell
dotnet msbuild poc/build/tests/smoke/Glitnir.Probe.Smoke/Glitnir.Probe.Smoke.csproj -getProperty:PublishAot,AnalysisLevelSecurity,NoWarn,IsPackable
dotnet build poc/build/tests/smoke/Glitnir.Probe.Smoke/Glitnir.Probe.Smoke.csproj -tl:off --nologo
```

Expected: `PublishAot = true`; `AnalysisLevelSecurity` **empty**; `NoWarn` does **not** contain `CS1591`; `IsPackable` **empty** — all four prove a project nested two levels under `tests/` sees only the smoke floor, not the `tests/` layer or the root law. Build exit 0.

- [ ] **Step 5: Staging checkpoint**

```powershell
git add poc/build/tests/smoke
git status --short
```

Halt for human commit.

---

### Task 8: The harness — `Verify-Enforcement.ps1`

**Files:**
- Create: `poc/build/Verify-Enforcement.ps1`

- [ ] **Step 1: Create `poc/build/Verify-Enforcement.ps1`**

```powershell
#Requires -Version 7
<#
.SYNOPSIS
	Verifies the MSBuild enforcement law lands and behaves.
	Spec: docs/Platform/specs/2026-06-05-build-enforcement-design.md
	Exit 0 = law verified. Exit 1 = at least one assertion failed.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$pocRoot = $PSScriptRoot
$script:failures = [System.Collections.Generic.List[string]]::new()

function Write-Check([string] $message) {
	Write-Host "CHECK  $message"
}

function Add-Failure([string] $message) {
	$script:failures.Add($message)
	Write-Host "FAIL   $message" -ForegroundColor Red
}

# Expected-value prefixes: '~' = contains, '!' = must differ, '' (empty string) = must be empty.
# $expected must carry at least two keys: a single -getProperty returns plain text, not JSON.
function Assert-Properties([string] $project, [hashtable] $expected) {
	$names = ($expected.Keys | Sort-Object) -join ','
	$raw = dotnet msbuild (Join-Path $pocRoot $project) "-getProperty:$names" 2>&1 | Out-String
	if ($LASTEXITCODE -ne 0) {
		Add-Failure "$project property evaluation failed:`n$raw"
		return
	}
	# SDK banners (NETSDK1057, workload nags) can precede the JSON in CI environments — extract the object before parsing.
	$jsonText = [regex]::Match($raw, '\{.*\}', [System.Text.RegularExpressions.RegexOptions]::Singleline).Value
	if (-not $jsonText) {
		Add-Failure "$project -getProperty returned no JSON:`n$raw"
		return
	}
	$json = $jsonText | ConvertFrom-Json
	foreach ($name in $expected.Keys) {
		$want = [string]$expected[$name]
		$actual = [string]$json.Properties.$name
		$ok = switch -Wildcard ($want) {
			'~*' { $actual -like "*$($want.Substring(1))*" }
			'!*' { $actual -ne $want.Substring(1) }
			default { $actual -eq $want }
		}
		if ($ok) { Write-Check "$project  $name = '$actual'" }
		else { Add-Failure "$project  $name expected '$want', got '$actual'" }
	}
}

function Assert-CleanBuild([string] $project) {
	$out = dotnet build (Join-Path $pocRoot $project) -tl:off --nologo 2>&1 | Out-String
	if ($LASTEXITCODE -eq 0) { Write-Check "$project  clean build passed" }
	else { Add-Failure "$project clean build failed:`n$out" }
}

function Assert-CanaryBuild([string] $project, [string[]] $expectedIds, [string] $toggle = 'EnableCanaries') {
	$out = dotnet build (Join-Path $pocRoot $project) -tl:off --nologo "-p:$toggle=true" 2>&1 | Out-String
	if ($LASTEXITCODE -eq 0) {
		Add-Failure "$project canaried build unexpectedly succeeded"
		return
	}
	foreach ($id in $expectedIds) {
		if ($out -match "error $id") { Write-Check "$project  canary $id fired as error" }
		elseif ($out -match "warning $id") { Add-Failure "$project canary $id fired as WARNING - ratchet broken" }
		else { Add-Failure "$project canary $id did not fire" }
	}
}

$law = @{
	AnalysisLevel = 'latest-Recommended'
	AnalysisLevelSecurity = 'latest-All'
	AnalysisLevelPerformance = 'latest-All'
	AnalysisLevelReliability = 'latest-All'
	AnalysisLevelUsage = 'latest-All'
	TreatWarningsAsErrors = 'true'
	GenerateDocumentationFile = 'true'
	UseArtifactsOutput = 'true'
}

Write-Host "`n=== src/ - full law ==="
Assert-Properties 'src/Glitnir.Probe/Glitnir.Probe.csproj' $law
Assert-CleanBuild 'src/Glitnir.Probe/Glitnir.Probe.csproj'
Assert-CanaryBuild 'src/Glitnir.Probe/Glitnir.Probe.csproj' @('CA5394', 'CA1810', 'CA2007', 'CA2201', 'CA2200', 'CS0219', 'CS8618')
Assert-CanaryBuild 'src/Glitnir.Probe/Glitnir.Probe.csproj' @('CS1591') 'EnableDocCanaries'

Write-Host "`n=== tests/ - law + NoWarn delta + IVT ==="
Assert-Properties 'tests/Glitnir.Probe.Tests/Glitnir.Probe.Tests.csproj' ($law + @{ NoWarn = '~CS1591'; IsPackable = 'false' })
Assert-CleanBuild 'tests/Glitnir.Probe.Tests/Glitnir.Probe.Tests.csproj'
Assert-CanaryBuild 'tests/Glitnir.Probe.Tests/Glitnir.Probe.Tests.csproj' @('CA2200')

Write-Host "`n=== benchmarks/ - law + NoWarn delta ==="
Assert-Properties 'benchmarks/Glitnir.Probe.Benchmarks/Glitnir.Probe.Benchmarks.csproj' ($law + @{ NoWarn = '~CS1591' })
Assert-CleanBuild 'benchmarks/Glitnir.Probe.Benchmarks/Glitnir.Probe.Benchmarks.csproj'
Assert-CanaryBuild 'benchmarks/Glitnir.Probe.Benchmarks/Glitnir.Probe.Benchmarks.csproj' @('CA2200')

Write-Host "`n=== poc/ - severed ==="
Assert-Properties 'poc/Glitnir.Probe.Severed/Glitnir.Probe.Severed.csproj' @{
	AnalysisLevelSecurity = ''
	TreatWarningsAsErrors = '!true'
	UseArtifactsOutput = 'true'
	TargetFramework = 'net11.0'
}
Assert-CleanBuild 'poc/Glitnir.Probe.Severed/Glitnir.Probe.Severed.csproj'

Write-Host "`n=== tests/smoke/ - severed + AOT floor ==="
Assert-Properties 'tests/smoke/Glitnir.Probe.Smoke/Glitnir.Probe.Smoke.csproj' @{
	PublishAot = 'true'
	AnalysisLevelSecurity = ''
	NoWarn = '!~CS1591'
	IsPackable = '!false'
}
Assert-CleanBuild 'tests/smoke/Glitnir.Probe.Smoke/Glitnir.Probe.Smoke.csproj'

if ($script:failures.Count -gt 0) {
	Write-Host "`n$($script:failures.Count) assertion(s) failed." -ForegroundColor Red
	exit 1
}
Write-Host "`nAll enforcement assertions passed." -ForegroundColor Green
exit 0
```

Note the one wrinkle: `NoWarn = '!~CS1591'` — the `!` branch matches first in the `switch -Wildcard`, so this asserts "not equal to the literal `~CS1591`", which is weaker than intended ("does not contain"). Acceptable for the smoke floor because the manual Task 7 check already proved the stronger claim; if you want it airtight, add a `'!~*'` case above `'!*'` that negates `-like` — do so only if the harness run shows it matters.

- [ ] **Step 2: Run the harness**

```powershell
pwsh poc/build/Verify-Enforcement.ps1
```

Expected: every line `CHECK`, final line `All enforcement assertions passed.`, exit code 0 (`$LASTEXITCODE`). Any `FAIL` line: fix via the Empirical-Pin Protocol before proceeding — the harness is now the single source of verification truth, superseding the manual per-task commands.

- [ ] **Step 3: Staging checkpoint**

```powershell
git add poc/build/Verify-Enforcement.ps1
git status --short
```

Halt for human commit.

---

### Task 9: Verdict + documentation reconciliation

**Files:**
- Create: `poc/build/FINDINGS.md`
- Modify: `docs/Platform/specs/2026-06-05-build-enforcement-design.md` (§8 execution note)
- Modify: `docs/spec-reconciliation-2026-06-04.md` (§4.2 status)

- [ ] **Step 1: Write `poc/build/FINDINGS.md`** — the verdict, from observed reality

Skeleton below; every `(observed)` slot is filled from the actual harness run and SDK in use (`dotnet --version`). Surprises recorded under Deviations — substituted canary IDs, rules that fired on lawful code, property values that differed from expectations.

```markdown
# Build Enforcement POC — Findings

**Date executed:** 2026-06-05/06
**SDK:** (observed — `dotnet --version`)
**Harness result:** (observed — pass/fail + assertion count)

## Verdicts

| Claim | Verdict |
|---|---|
| Chain import propagates root law through two hops | (observed) |
| `AnalysisLevel<Category>` knobs escalate per category | (observed) |
| `TreatWarningsAsErrors` ratchets analyzer + compiler diagnostics | (observed) |
| `NoWarn` accumulation suppresses CS1591 without weakening the rest | (observed) |
| `InternalsVisibleTo "$(AssemblyName).Tests"` opens for the tests probe | (observed) |
| Severed floors inherit nothing (poc/, tests/smoke/ nesting) | (observed) |
| Artifacts layout consolidates per declaring props file | (observed) |

## Canary Ledger

| ID | Category | Tier | Fired as error? |
|---|---|---|---|
| CA5394 | Security | latest-All-only | (observed) |
| CA1810 | Performance | latest-All-only | (observed) |
| CA2007 | Reliability | latest-All-only | (observed) |
| CA2201 | Usage | latest-All-only | (observed) |
| CA2200 | Usage | latest-Recommended baseline | (observed) |
| CS0219 | Compiler | ratchet | (observed) |
| CS1591 | Docs | ratchet | (observed) |

## Deviations and Surprises

(observed — or "none")

## Seeding Recommendation

(observed — what transfers verbatim to the real Glitnir root, what changed and why)
```

- [ ] **Step 2: Amend the spec with the execution note**

In `docs/Platform/specs/2026-06-05-build-enforcement-design.md`, insert immediately after the `## 8. File Inventory` heading:

```markdown
> **Execution note (2026-06-05):** Per directive, this design is proven first as a
> self-contained replica under `poc/build/` (see its `FINDINGS.md` for the verdict).
> The real-tree files below are seeded from the replica in a follow-up pass.
```

- [ ] **Step 3: Update the punch list**

In `docs/spec-reconciliation-2026-06-04.md`, locate section `### ☐ 4.2 Build-enforcement stack session` and append this paragraph at the end of that section (before the next `###` heading):

```markdown
**Status (2026-06-05):** MSBuild-law phase designed
(`docs/Platform/specs/2026-06-05-build-enforcement-design.md`) and proven in the
`poc/build/` replica (see its `FINDINGS.md`). Remaining: `.editorconfig` phase
(Phase 2), real-tree seeding, and the `UseProjectReferences` switching session.
```

- [ ] **Step 4: Final staging checkpoint**

```powershell
git add poc/build/FINDINGS.md docs/Platform/specs/2026-06-05-build-enforcement-design.md docs/spec-reconciliation-2026-06-04.md
git status --short
```

Halt for human commit. The POC is complete when the harness exits 0 and `FINDINGS.md` carries the verdict.

---

## Self-Review Record

- **Spec coverage:** §3 law → Task 1; §5 governed deltas → Tasks 2, 4, 5; §6 severed floors → Tasks 6, 7; §7 probes/canaries/harness → Tasks 2–8; §8 inventory → re-targeted to `poc/build/` per directive (spec amended in Task 9); §9 deferrals untouched. Real-tree seeding is deliberately out of scope (follow-up pass driven by FINDINGS.md).
- **Placeholders:** `(observed)` slots in FINDINGS.md are execution-time data capture, not plan gaps; all code and commands are complete.
- **Type consistency:** `LawfulCitizen.InternalRealm` (Task 2) matches its use in Task 4; probe/csproj names consistent across Tasks 2–8 and the harness's project paths.
