# Norse.Infrastructure.Persistence Foundation Implementation Plan (Plan A of 4)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **This plan halts at the plan stage during the spec-first phase; do not execute without explicit user greenlight.**

**Goal:** Stand up the `norse-abstractions-infrastructure` submodule with two .NET 10 class library projects — `Norse.Abstractions.Contracts` (Mongo-side wire-shape markers) and `Norse.Abstractions.Infrastructure` (repository contracts, EF-entity marker hierarchy, `TstzRange` value type). Pure compile-time types; concrete implementations come in Plan B (Postgres), Plan C (Mongo), and Plan D (reference data + worked example).

**Architecture:** One submodule, two class libraries, two test projects. `Norse.Abstractions.Infrastructure` references `Norse.Abstractions.Contracts` (the repository contracts mention `IDocument`). Tests use xUnit + Shouldly + reflection-based assertions to verify type-shape invariants (marker inheritance, generic constraint shapes, enum value stability). No external runtime dependencies beyond the BCL.

**Tech Stack:** .NET 10 / C# 13, xUnit, Shouldly, NSubstitute (for any mock-based shape testing).

**Companion spec:** `docs/Midgard/specs/2026-05-21-midgard-persistence-design.md`. Read §4 (repository contracts), §5 (marker hierarchy), §10.1 (TstzRange value type), and §14 (realm placement) before starting. Every design decision is justified in the spec.

---

## Plan Sequence

This is **Plan A of 4** for the Norse.Infrastructure.Persistence spec. Sibling plans (not yet written):

- **Plan B — Postgres + EF Core implementations:** `Norse.Infrastructure.Persistence` submodule, `InfrastructureDbContext` base with snake_case + MaxLength conventions, `CommandRepository<T>`, `TemporalRepository<T>` with `FromSqlInterpolated` against history tables, `UnitOfWork`, the `TemporalEntityConvention` that auto-configures system_period column + GIST exclusion + history triggers, integration tests against testcontainers Postgres.
- **Plan C — Mongo + BSON implementations:** `Norse.Infrastructure.Persistence`'s Mongo half — `DocumentRepository<T>` with `Expression<Func<T, TProjection>>` projection support, `CachedRepository<T>` with optional per-entity LRU, BSON conventions (pinned GUID/decimal/char/DateTimeOffset settings), `MongoIndexAttribute`, idempotency collection, BSON round-trip integration test.
- **Plan D — Reference-data pipeline + worked example:** seed-tool stub, reference-projection worker, `ReferenceDataReloadedEvent`, end-to-end integration test exercising the §15 worked example (Policy bind end-to-end across `.Server` shim → command chain → worker enrichment).

This plan halts after the contracts. Plans B–D will be written when the user signals readiness (or when explicitly requested earlier).

---

## Prerequisites

The plan's tasks are tagged by readiness:

- **🟢 Ready now:** dependencies in place. Tasks here are execution-ready today.

All tasks in Plan A are 🟢. No upstream specs or plans need to land first; the contracts are at the bottom of the dependency graph.

Two downstream effects on existing plans, noted but not blocking:

| Effect | Where | When |
|---|---|---|
| Auth Plan A's `🟡` DbContext task unblocked | `{company}-auth/src/Norse.Auth.Server/AuthEntityConfigurations.cs` task | Once this plan ships, Auth Plan A can complete its `AuthEntityConfigurations` work because the `IEntity`/`ITemporalEntity` markers + repository contract types exist. |
| Norse Hosting plan's reference to `InfrastructureDbContext` | Hosting plan §10 (`EfCoreMigrationContributor<TContext>`) | Stays 🟡 until Plan B (Postgres) ships `InfrastructureDbContext`. This plan delivers the contracts only. |

**Per CLAUDE.md §8:** *No automatic git commits.* Every "Stage" step ends with `git add` only. The human runs `git commit` after reviewing the diff. Each task includes a proposed commit message for that review.

---

## File Structure

All paths relative to the meta-repo root.

```
norse-abstractions-infrastructure/                                # NEW: subdirectory (future submodule)
├── .editorconfig                                     # NEW: tabs, 2-space width
├── .gitignore                                        # NEW: dotnet defaults
├── Directory.Build.props                             # NEW: submodule-local overrides
├── LICENSE                                           # NEW: MIT
├── README.md                                         # NEW: usage + sibling-plan pointers
├── Norse.Abstractions.Infrastructure.slnx                        # NEW: solution (XML .slnx)
├── src/
│   ├── Norse.Abstractions.Contracts/
│   │   ├── Norse.Abstractions.Contracts.csproj                   # NEW
│   │   ├── IDocument.cs                              # NEW: root Mongo-doc marker
│   │   ├── IWireShape.cs                             # NEW: shim-able wire shape marker
│   │   ├── IReferenceDocument.cs                     # NEW: reference-data wire shape marker
│   │   └── ProcessingStatus.cs                       # NEW: enum (Pending|Active|Rejected)
│   └── Norse.Abstractions.Infrastructure/
│       ├── Norse.Abstractions.Infrastructure.csproj              # NEW
│       ├── IEntity.cs                                # NEW: root EF entity marker
│       ├── IBridgeEntity.cs                          # NEW: composite-uniqueness marker
│       ├── IInsertOnlyEntity.cs                      # NEW: write-once mutability marker
│       ├── IReadOnlyEntity.cs                        # NEW: seed-only mutability marker
│       ├── ITemporalEntity.cs                        # NEW: tstzrange-versioned marker
│       ├── RangeBoundType.cs                         # NEW: enum (Inclusive|Exclusive)
│       ├── TstzRange.cs                              # NEW: value type for pg tstzrange
│       ├── CacheLocallyAttribute.cs                  # NEW: opt-in LRU for ICachedRepository
│       ├── IDocumentRepository.cs                    # NEW: Mongo read/shim/replace contract
│       ├── ICommandRepository.cs                     # NEW: Postgres write contract
│       ├── ICachedRepository.cs                      # NEW: reference-data read contract
│       └── ITemporalRepository.cs                    # NEW: tstzrange query contract
└── tests/
    ├── Norse.Abstractions.Contracts.Tests/
    │   ├── Norse.Abstractions.Contracts.Tests.csproj             # NEW
    │   ├── IDocumentTests.cs                         # NEW
    │   ├── WireShapeHierarchyTests.cs                # NEW
    │   └── ProcessingStatusTests.cs                  # NEW
    └── Norse.Abstractions.Infrastructure.Tests/
        ├── Norse.Abstractions.Infrastructure.Tests.csproj        # NEW
        ├── MarkerHierarchyTests.cs                   # NEW
        ├── RepositoryContractShapeTests.cs           # NEW
        ├── TstzRangeTests.cs                         # NEW
        └── RangeBoundTypeTests.cs                    # NEW
```

---

## Phase 1 — Submodule scaffolding 🟢

### Task 1: Verify .NET 10 SDK and create the norse-abstractions-infrastructure subdirectory

**Files:**
- Create: `norse-abstractions-infrastructure/` (directory)

- [ ] **Step 1: Verify SDK presence**

Run: `dotnet --list-sdks`

Expected: at least one entry starting with `10.0.` (e.g., `10.0.100`). If absent, install the latest .NET 10 SDK before proceeding.

- [ ] **Step 2: Create the subdirectory**

Run: `mkdir norse-abstractions-infrastructure`

Expected: directory `norse-abstractions-infrastructure\` exists.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure
```

Proposed commit message: `feat(persistence-foundation): create norse-abstractions-infrastructure subdirectory`

---

### Task 2: Add `.gitignore`, `.editorconfig`, `Directory.Build.props`

**Files:**
- Create: `norse-abstractions-infrastructure/.gitignore`
- Create: `norse-abstractions-infrastructure/.editorconfig`
- Create: `norse-abstractions-infrastructure/Directory.Build.props`

- [ ] **Step 1: Write `norse-abstractions-infrastructure/.gitignore`**

```
bin/
obj/
*.user
.vs/
.idea/
*.suo
TestResults/
.test/
```

- [ ] **Step 2: Write `norse-abstractions-infrastructure/.editorconfig`**

```
root = false

[*]
indent_style = tab
indent_size = 2
end_of_line = crlf
insert_final_newline = true
charset = utf-8-bom

[*.{md,yml,yaml}]
indent_style = space
indent_size = 2

[*.{cs,csproj,props,targets,json,slnx}]
indent_style = tab
indent_size = 2

[*.cs]
dotnet_style_qualification_for_field = false:warning
dotnet_style_qualification_for_property = false:warning
dotnet_style_qualification_for_method = false:warning
dotnet_style_qualification_for_event = false:warning
csharp_style_var_for_built_in_types = false:warning
csharp_style_var_when_type_is_apparent = true:warning
csharp_style_var_elsewhere = false:warning
dotnet_diagnostic.CS8618.severity = error
dotnet_diagnostic.CS8625.severity = error
dotnet_diagnostic.CS8602.severity = error
dotnet_diagnostic.CS8604.severity = error
```

- [ ] **Step 3: Write `norse-abstractions-infrastructure/Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <RootNamespace>$(MSBuildProjectName)</RootNamespace>
    <NeutralLanguage>en-US</NeutralLanguage>
    <Authors>{Company} Insurance</Authors>
    <Company>{Company}</Company>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Stage**

```
git add norse-abstractions-infrastructure/.gitignore norse-abstractions-infrastructure/.editorconfig norse-abstractions-infrastructure/Directory.Build.props
```

Proposed commit message: `feat(persistence-foundation): scaffold .gitignore, .editorconfig, Directory.Build.props`

---

### Task 3: Add `LICENSE` and `README.md`

**Files:**
- Create: `norse-abstractions-infrastructure/LICENSE`
- Create: `norse-abstractions-infrastructure/README.md`

- [ ] **Step 1: Write `norse-abstractions-infrastructure/LICENSE`** (MIT, standard text)

```
MIT License

Copyright (c) 2026 {Company} Insurance

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- [ ] **Step 2: Write `norse-abstractions-infrastructure/README.md`**

```markdown
# Norse.Abstractions.Infrastructure

Declared-law contracts for persistence in the Norse platform. Implements the
contract surface described in
`docs/Midgard/specs/2026-05-21-midgard-persistence-design.md`.

## Assemblies

- **Norse.Abstractions.Contracts** — Mongo-side wire-shape markers (`IDocument`,
  `IWireShape`, `IReferenceDocument`, `ProcessingStatus`). Referenced by every
  `{Company}.{Context}.Contracts` assembly.
- **Norse.Abstractions.Infrastructure** — Postgres-side entity markers (`IEntity`,
  `IBridgeEntity`, `IInsertOnlyEntity`, `IReadOnlyEntity`, `ITemporalEntity`),
  repository contracts (`IDocumentRepository<T>`, `ICommandRepository<T>`,
  `ICachedRepository<T>`, `ITemporalRepository<T>`), `TstzRange`,
  `RangeBoundType`, `CacheLocallyAttribute`. Referenced by every
  `{Company}.{Context}.Server` and `.Worker` assembly. No `IUnitOfWork` —
  the messaging library's per-handler session owns the transaction; see
  spec §4.2.

## What lives where

- **Concrete implementations** of the repository contracts live in
  `Norse.Infrastructure.Persistence` (separate submodule, Plan B+).
- **MongoIndexAttribute** lives in `Norse.Infrastructure.Persistence` (consumed at host
  startup, impl-side concern).
- **BSON conventions** are configured by `Norse.Infrastructure.Persistence` at startup.

## Status

Plan A of 4 (foundation). Plans B–D (Postgres impls, Mongo impls,
reference-data pipeline + worked example) follow.
```

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/LICENSE norse-abstractions-infrastructure/README.md
```

Proposed commit message: `docs(persistence-foundation): add LICENSE and README for norse-abstractions-infrastructure`

---

### Task 4: Create the solution file

**Files:**
- Create: `norse-abstractions-infrastructure/Norse.Abstractions.Infrastructure.slnx`

- [ ] **Step 1: Create the solution**

Run from `norse-abstractions-infrastructure/`:

```
dotnet new sln --name Norse.Abstractions.Infrastructure --format slnx
```

Expected: `Norse.Abstractions.Infrastructure.slnx` exists in `norse-abstractions-infrastructure/`.

- [ ] **Step 2: Verify it's XML `.slnx`, not legacy `.sln`**

Run: `Get-Content norse-abstractions-infrastructure/Norse.Abstractions.Infrastructure.slnx -TotalCount 3`

Expected: first line starts with `<Solution>` (or an XML declaration), confirming the `.slnx` format. Per `[[feedback_solution_file_format]]`, only `.slnx` is supported across the ecosystem.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/Norse.Abstractions.Infrastructure.slnx
```

Proposed commit message: `feat(persistence-foundation): create Norse.Abstractions.Infrastructure.slnx`

---

## Phase 2 — Norse.Abstractions.Contracts 🟢

### Task 5: Create the Norse.Abstractions.Contracts project

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Contracts/Norse.Abstractions.Contracts.csproj`

- [ ] **Step 1: Create the project**

Run from `norse-abstractions-infrastructure/`:

```
dotnet new classlib --name Norse.Abstractions.Contracts --output src/Norse.Abstractions.Contracts --framework net10.0
```

- [ ] **Step 2: Remove the auto-generated `Class1.cs`**

Run: `Remove-Item norse-abstractions-infrastructure/src/Norse.Abstractions.Contracts/Class1.cs`

- [ ] **Step 3: Replace the csproj contents**

Replace `norse-abstractions-infrastructure/src/Norse.Abstractions.Contracts/Norse.Abstractions.Contracts.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>Wire-shape markers for Mongo-backed documents in the Norse platform: IDocument, IWireShape, IReferenceDocument, ProcessingStatus. Pure value types; no infrastructure dependencies.</Description>
    <IsPackable>true</IsPackable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Add to solution**

Run from `norse-abstractions-infrastructure/`:

```
dotnet sln Norse.Abstractions.Infrastructure.slnx add src/Norse.Abstractions.Contracts/Norse.Abstractions.Contracts.csproj
```

- [ ] **Step 5: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Contracts/Norse.Abstractions.Contracts.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 6: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Contracts norse-abstractions-infrastructure/Norse.Abstractions.Infrastructure.slnx
```

Proposed commit message: `feat(norse-abstractions-contracts): scaffold Norse.Abstractions.Contracts project`

---

### Task 6: `IDocument` root marker

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Contracts/IDocument.cs`

- [ ] **Step 1: Write `IDocument.cs`**

```csharp
namespace Norse.Abstractions.Contracts;

/// <summary>
/// Root marker for Mongo-backed documents in the Norse platform. Every
/// document persisted in the operational read store carries a stable
/// <see cref="Id"/>. Two specializations follow:
/// <list type="bullet">
///   <item><see cref="IWireShape"/> for shim-able wire shapes that participate in
///         the request → enrichment lifecycle (status block included).</item>
///   <item><see cref="IReferenceDocument"/> for reference-data projections
///         loaded via the seed pipeline (no status block; always "Active").</item>
/// </list>
/// </summary>
public interface IDocument
{
	Guid Id { get; }
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Contracts/Norse.Abstractions.Contracts.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Contracts/IDocument.cs
```

Proposed commit message: `feat(norse-abstractions-contracts): add IDocument root marker`

---

### Task 7: `ProcessingStatus` enum

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Contracts/ProcessingStatus.cs`

Per CLAUDE.md §5: explicit integer values on every member; `0` reserved for sentinel only; real states start at `1`.

- [ ] **Step 1: Write `ProcessingStatus.cs`**

```csharp
namespace Norse.Abstractions.Contracts;

/// <summary>
/// Lifecycle state of a shim-able wire shape (an <see cref="IWireShape"/>) in the
/// CQRS pipeline. <see cref="Pending"/> is set by <c>.Server</c> when it plants
/// the shim and dispatches the command. <see cref="Active"/> is set by
/// <c>.Worker</c> after a successful Postgres commit and Mongo enrichment.
/// <see cref="Rejected"/> is set by <c>.Worker</c> when business validation
/// fails; the StatusReason on the wire shape carries the human-readable cause.
/// </summary>
public enum ProcessingStatus
{
	/// <summary>Reserved sentinel; never assigned in practice.</summary>
	Unspecified = 0,

	/// <summary>.Server has planted the shim; .Worker has not yet enriched.</summary>
	Pending = 1,

	/// <summary>.Worker has enriched; this is the current view.</summary>
	Active = 2,

	/// <summary>.Worker validated the command and rejected it; see StatusReason on the wire shape.</summary>
	Rejected = 3,
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Contracts/Norse.Abstractions.Contracts.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Contracts/ProcessingStatus.cs
```

Proposed commit message: `feat(norse-abstractions-contracts): add ProcessingStatus enum with explicit values`

---

### Task 8: `IWireShape` shim-able marker

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Contracts/IWireShape.cs`

- [ ] **Step 1: Write `IWireShape.cs`**

```csharp
namespace Norse.Abstractions.Contracts;

/// <summary>
/// Marker for wire shapes that participate in the shim → enrichment lifecycle.
/// Every type returned from a write gRPC method, and every persisted
/// business-aggregate view, implements this.
///
/// <para>.Server plants a shim (<see cref="ProcessingStatus.Pending"/>) when the
/// write arrives; .Worker enriches the document and flips status to
/// <see cref="ProcessingStatus.Active"/> after a successful Postgres commit,
/// or <see cref="ProcessingStatus.Rejected"/> on business-validation failure
/// (with <see cref="StatusReason"/> populated).</para>
///
/// <para>The status block is contributed at the platform level so every wire
/// shape is uniform; clients always see a deterministic Pending|Active|Rejected
/// state regardless of which context the resource belongs to.</para>
/// </summary>
public interface IWireShape : IDocument
{
	ProcessingStatus Status { get; init; }
	string? StatusReason { get; init; }
	DateTimeOffset? ProcessedAt { get; init; }
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Contracts/Norse.Abstractions.Contracts.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Contracts/IWireShape.cs
```

Proposed commit message: `feat(norse-abstractions-contracts): add IWireShape marker with ProcessingStatus block`

---

### Task 9: `IReferenceDocument` marker

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Contracts/IReferenceDocument.cs`

- [ ] **Step 1: Write `IReferenceDocument.cs`**

```csharp
namespace Norse.Abstractions.Contracts;

/// <summary>
/// Marker for the Mongo projection of an <c>IReadOnlyEntity</c> reference-data
/// row (ZIP, ISO country code, NCCI class factor, etc.). Reference data is
/// always conceptually "Active" — it ships through the seed pipeline,
/// not the request → enrichment lifecycle — so it carries no
/// <c>ProcessingStatus</c> block. The <see cref="LoadedAt"/> timestamp lets
/// consumers reason about staleness.
/// </summary>
public interface IReferenceDocument : IDocument
{
	DateTimeOffset LoadedAt { get; }
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Contracts/Norse.Abstractions.Contracts.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Contracts/IReferenceDocument.cs
```

Proposed commit message: `feat(norse-abstractions-contracts): add IReferenceDocument marker for reference-data projections`

---

## Phase 3 — Norse.Abstractions.Contracts.Tests 🟢

### Task 10: Create the Norse.Abstractions.Contracts test project

**Files:**
- Create: `norse-abstractions-infrastructure/tests/Norse.Abstractions.Contracts.Tests/Norse.Abstractions.Contracts.Tests.csproj`

- [ ] **Step 1: Create the test project**

Run from `norse-abstractions-infrastructure/`:

```
dotnet new xunit --name Norse.Abstractions.Contracts.Tests --output tests/Norse.Abstractions.Contracts.Tests --framework net10.0
```

- [ ] **Step 2: Remove the auto-generated `UnitTest1.cs`**

Run: `Remove-Item norse-abstractions-infrastructure/tests/Norse.Abstractions.Contracts.Tests/UnitTest1.cs`

- [ ] **Step 3: Replace the csproj contents**

Replace `norse-abstractions-infrastructure/tests/Norse.Abstractions.Contracts.Tests/Norse.Abstractions.Contracts.Tests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Shouldly" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Norse.Abstractions.Contracts\Norse.Abstractions.Contracts.csproj" />
  </ItemGroup>
</Project>
```

Note: package versions are governed centrally via NuGet restore (latest stable) until a meta-repo `Directory.Packages.props` is added (out of scope here).

- [ ] **Step 4: Add to the solution**

Run from `norse-abstractions-infrastructure/`:

```
dotnet sln Norse.Abstractions.Infrastructure.slnx add tests/Norse.Abstractions.Contracts.Tests/Norse.Abstractions.Contracts.Tests.csproj
```

- [ ] **Step 5: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build tests/Norse.Abstractions.Contracts.Tests/Norse.Abstractions.Contracts.Tests.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 6: Stage**

```
git add norse-abstractions-infrastructure/tests/Norse.Abstractions.Contracts.Tests norse-abstractions-infrastructure/Norse.Abstractions.Infrastructure.slnx
```

Proposed commit message: `test(norse-abstractions-contracts): scaffold Norse.Abstractions.Contracts.Tests xUnit project`

---

### Task 11: `IDocument` shape tests

**Files:**
- Create: `norse-abstractions-infrastructure/tests/Norse.Abstractions.Contracts.Tests/IDocumentTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Norse.Abstractions.Contracts;
using Shouldly;
using Xunit;

namespace Norse.Abstractions.Contracts.Tests;

public sealed class IDocumentTests
{
	[Fact]
	public void IDocument_has_Guid_Id_getter()
	{
		var idProperty = typeof(IDocument).GetProperty(nameof(IDocument.Id));
		idProperty.ShouldNotBeNull();
		idProperty.PropertyType.ShouldBe(typeof(Guid));
		idProperty.CanRead.ShouldBeTrue();
		idProperty.CanWrite.ShouldBeFalse();  // no setter on the root marker
	}

	[Fact]
	public void IDocument_has_no_other_members()
	{
		// Root marker should be intentionally minimal. If a member is added,
		// this test fails and the addition gets a deliberate code-review touchpoint.
		var members = typeof(IDocument).GetMembers();
		members.Length.ShouldBe(2, "Expected get_Id + Id property; any other members are unexpected.");
	}
}
```

- [ ] **Step 2: Run the test**

Run from `norse-abstractions-infrastructure/`:

```
dotnet test tests/Norse.Abstractions.Contracts.Tests/Norse.Abstractions.Contracts.Tests.csproj --filter "FullyQualifiedName~IDocumentTests"
```

Expected: 2 tests pass.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/tests/Norse.Abstractions.Contracts.Tests/IDocumentTests.cs
```

Proposed commit message: `test(norse-abstractions-contracts): cover IDocument shape invariants`

---

### Task 12: `IWireShape` and `IReferenceDocument` hierarchy tests

**Files:**
- Create: `norse-abstractions-infrastructure/tests/Norse.Abstractions.Contracts.Tests/WireShapeHierarchyTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Norse.Abstractions.Contracts;
using Shouldly;
using Xunit;

namespace Norse.Abstractions.Contracts.Tests;

public sealed class WireShapeHierarchyTests
{
	[Fact]
	public void IWireShape_extends_IDocument()
	{
		typeof(IWireShape).IsAssignableTo(typeof(IDocument)).ShouldBeTrue();
	}

	[Fact]
	public void IReferenceDocument_extends_IDocument()
	{
		typeof(IReferenceDocument).IsAssignableTo(typeof(IDocument)).ShouldBeTrue();
	}

	[Fact]
	public void IWireShape_carries_Status_StatusReason_ProcessedAt_with_init_setters()
	{
		var status = typeof(IWireShape).GetProperty(nameof(IWireShape.Status));
		status.ShouldNotBeNull();
		status.PropertyType.ShouldBe(typeof(ProcessingStatus));
		status.SetMethod.ShouldNotBeNull();  // init-only setters DO appear via reflection

		var statusReason = typeof(IWireShape).GetProperty(nameof(IWireShape.StatusReason));
		statusReason.ShouldNotBeNull();
		statusReason.PropertyType.ShouldBe(typeof(string));

		var processedAt = typeof(IWireShape).GetProperty(nameof(IWireShape.ProcessedAt));
		processedAt.ShouldNotBeNull();
		processedAt.PropertyType.ShouldBe(typeof(DateTimeOffset?));
	}

	[Fact]
	public void IReferenceDocument_carries_LoadedAt_with_no_setter()
	{
		var loadedAt = typeof(IReferenceDocument).GetProperty(nameof(IReferenceDocument.LoadedAt));
		loadedAt.ShouldNotBeNull();
		loadedAt.PropertyType.ShouldBe(typeof(DateTimeOffset));
		loadedAt.CanRead.ShouldBeTrue();
		loadedAt.CanWrite.ShouldBeFalse();
	}

	[Fact]
	public void IReferenceDocument_does_not_carry_ProcessingStatus_block()
	{
		// IReferenceDocument is intentionally unconcerned with ProcessingStatus.
		typeof(IReferenceDocument).GetProperty("Status").ShouldBeNull();
		typeof(IReferenceDocument).GetProperty("StatusReason").ShouldBeNull();
		typeof(IReferenceDocument).GetProperty("ProcessedAt").ShouldBeNull();
	}

	[Fact]
	public void A_type_cannot_implement_both_specialized_markers_(by_convention)()
	{
		// This is a documentation test — the platform forbids a type
		// implementing both IWireShape and IReferenceDocument. The runtime
		// won't stop you; the analyzer (slotted in norse-primitives-architecture)
		// will. We verify here only that the two markers are distinct
		// (neither extends the other), so an analyzer can mechanically detect
		// the violation.
		typeof(IWireShape).IsAssignableTo(typeof(IReferenceDocument)).ShouldBeFalse();
		typeof(IReferenceDocument).IsAssignableTo(typeof(IWireShape)).ShouldBeFalse();
	}
}
```

- [ ] **Step 2: Run the tests**

Run from `norse-abstractions-infrastructure/`:

```
dotnet test tests/Norse.Abstractions.Contracts.Tests/Norse.Abstractions.Contracts.Tests.csproj --filter "FullyQualifiedName~WireShapeHierarchyTests"
```

Expected: all tests pass, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/tests/Norse.Abstractions.Contracts.Tests/WireShapeHierarchyTests.cs
```

Proposed commit message: `test(norse-abstractions-contracts): cover IWireShape and IReferenceDocument hierarchy invariants`

---

### Task 13: `ProcessingStatus` enum tests

**Files:**
- Create: `norse-abstractions-infrastructure/tests/Norse.Abstractions.Contracts.Tests/ProcessingStatusTests.cs`

Per CLAUDE.md §5, every enum member must have an explicit integer value. This test guards reordering.

- [ ] **Step 1: Write the failing tests**

```csharp
using Norse.Abstractions.Contracts;
using Shouldly;
using Xunit;

namespace Norse.Abstractions.Contracts.Tests;

public sealed class ProcessingStatusTests
{
	[Theory]
	[InlineData(ProcessingStatus.Unspecified, 0)]
	[InlineData(ProcessingStatus.Pending, 1)]
	[InlineData(ProcessingStatus.Active, 2)]
	[InlineData(ProcessingStatus.Rejected, 3)]
	public void Each_member_has_the_expected_integer_value(ProcessingStatus member, int expected)
	{
		((int)member).ShouldBe(expected);
	}

	[Fact]
	public void Member_count_is_four_so_a_silent_addition_breaks_this_test()
	{
		Enum.GetValues<ProcessingStatus>().Length.ShouldBe(4);
	}

	[Fact]
	public void Zero_is_Unspecified_so_a_default_value_is_a_visible_sentinel()
	{
		default(ProcessingStatus).ShouldBe(ProcessingStatus.Unspecified);
	}
}
```

- [ ] **Step 2: Run the tests**

Run from `norse-abstractions-infrastructure/`:

```
dotnet test tests/Norse.Abstractions.Contracts.Tests/Norse.Abstractions.Contracts.Tests.csproj --filter "FullyQualifiedName~ProcessingStatusTests"
```

Expected: 6 tests pass.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/tests/Norse.Abstractions.Contracts.Tests/ProcessingStatusTests.cs
```

Proposed commit message: `test(norse-abstractions-contracts): cover ProcessingStatus enum value stability`

---

## Phase 4 — Norse.Abstractions.Infrastructure 🟢

### Task 14: Create the Norse.Abstractions.Infrastructure project

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj`

- [ ] **Step 1: Create the project**

Run from `norse-abstractions-infrastructure/`:

```
dotnet new classlib --name Norse.Abstractions.Infrastructure --output src/Norse.Abstractions.Infrastructure --framework net10.0
```

- [ ] **Step 2: Remove the auto-generated `Class1.cs`**

Run: `Remove-Item norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/Class1.cs`

- [ ] **Step 3: Replace the csproj contents**

Replace `norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>Repository contracts, EF-entity markers, and TstzRange value type for the Norse platform. Pure value types and interfaces; no infrastructure dependencies. Implementations live in Norse.Infrastructure.Persistence.</Description>
    <IsPackable>true</IsPackable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Norse.Abstractions.Contracts\Norse.Abstractions.Contracts.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add to solution**

Run from `norse-abstractions-infrastructure/`:

```
dotnet sln Norse.Abstractions.Infrastructure.slnx add src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj
```

- [ ] **Step 5: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 6: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure norse-abstractions-infrastructure/Norse.Abstractions.Infrastructure.slnx
```

Proposed commit message: `feat(norse-abstractions-infrastructure): scaffold Norse.Abstractions.Infrastructure project`

---

### Task 15: `IEntity` root entity marker

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/IEntity.cs`

- [ ] **Step 1: Write `IEntity.cs`**

```csharp
namespace Norse.Abstractions.Infrastructure;

/// <summary>
/// Root marker for relational entities persisted in PostgreSQL by the
/// Norse platform. Every entity carries a surrogate <see cref="Id"/>;
/// composite-uniqueness columns (for <see cref="IBridgeEntity"/>) are
/// declared per-entity via <c>IEntityTypeConfiguration&lt;T&gt;</c>.
///
/// <para>Documents persisted in MongoDB are NOT <see cref="IEntity"/> —
/// they implement <see cref="Norse.Abstractions.Contracts.IDocument"/> instead.</para>
///
/// <para>The mutability mode (plain mutable, insert-only, read-only,
/// temporal) is declared via one of the specialized markers
/// (<see cref="IInsertOnlyEntity"/>, <see cref="IReadOnlyEntity"/>,
/// <see cref="ITemporalEntity"/>). Plain <c>IEntity</c> without any of
/// those is the default mutable CRUD case.</para>
/// </summary>
public interface IEntity
{
	Guid Id { get; }
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/IEntity.cs
```

Proposed commit message: `feat(norse-abstractions-infrastructure): add IEntity root marker for PostgreSQL entities`

---

### Task 16: `IBridgeEntity` composite-uniqueness marker

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/IBridgeEntity.cs`

- [ ] **Step 1: Write `IBridgeEntity.cs`**

```csharp
namespace Norse.Abstractions.Infrastructure;

/// <summary>
/// Orthogonal-axis marker: this entity has additional uniqueness constraints
/// beyond <see cref="IEntity.Id"/>. The composite columns and their
/// uniqueness index are declared per-entity via
/// <c>IEntityTypeConfiguration&lt;T&gt;</c>; the surrogate
/// <see cref="IEntity.Id"/> stays the PK so repository contracts (which
/// operate on <c>Guid id</c>) work uniformly.
///
/// <para>The conventional surrogate is a UUID v5 derivation over the
/// composite columns within a per-entity-type namespace, so the same
/// logical row gets the same Id across every environment.</para>
///
/// <para>Composes freely with any one mutability mode marker
/// (<see cref="IInsertOnlyEntity"/>, <see cref="IReadOnlyEntity"/>,
/// <see cref="ITemporalEntity"/>) or with none of them.</para>
/// </summary>
public interface IBridgeEntity : IEntity
{
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/IBridgeEntity.cs
```

Proposed commit message: `feat(norse-abstractions-infrastructure): add IBridgeEntity orthogonal-axis marker`

---

### Task 17: `IInsertOnlyEntity` write-once marker

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/IInsertOnlyEntity.cs`

- [ ] **Step 1: Write `IInsertOnlyEntity.cs`**

```csharp
namespace Norse.Abstractions.Infrastructure;

/// <summary>
/// Mutability mode marker: this entity is write-once. Typical use is for
/// third-party data we persist as received (BDX rows from the fronting carrier, partner
/// feed records, immutable audit captures).
///
/// <para>The analyzer (slotted in norse-primitives-architecture) forbids
/// <c>ICommandRepository&lt;T&gt;.UpdateAsync</c> and <c>.RemoveAsync</c>
/// when <c>T : IInsertOnlyEntity</c>. <c>AddAsync</c> remains available.</para>
///
/// <para>Forbidden compositions (analyzer-enforced):</para>
/// <list type="bullet">
///   <item><c>IInsertOnlyEntity + ITemporalEntity</c> (mutability contradiction)</item>
///   <item><c>IInsertOnlyEntity + IReadOnlyEntity</c> (mutability contradiction)</item>
/// </list>
/// </summary>
public interface IInsertOnlyEntity : IEntity
{
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/IInsertOnlyEntity.cs
```

Proposed commit message: `feat(norse-abstractions-infrastructure): add IInsertOnlyEntity mutability marker`

---

### Task 18: `IReadOnlyEntity` seed-only marker

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/IReadOnlyEntity.cs`

- [ ] **Step 1: Write `IReadOnlyEntity.cs`**

```csharp
namespace Norse.Abstractions.Infrastructure;

/// <summary>
/// Mutability mode marker: this entity is reference data, loaded via the
/// seed pipeline (typically a Spectre.Console.Cli tool consuming a canonical
/// source feed: ISO 4217 currencies, USPS ZIP files, NCCI rate filings, etc.).
///
/// <para>The analyzer (slotted in norse-primitives-architecture) forbids
/// <c>ICommandRepository&lt;T&gt;</c> entirely when
/// <c>T : IReadOnlyEntity</c>. Application code cannot accidentally mutate
/// reference data; only the seed pipeline can.</para>
///
/// <para>Reads happen via <c>ICachedRepository&lt;T&gt;</c> on the worker
/// (with optional per-entity LRU via <see cref="CacheLocallyAttribute"/>) or
/// via <c>IDocumentRepository&lt;T&gt;</c> on the HTTP tier (no in-process
/// caching; Mongo lookup is the read path).</para>
///
/// <para>Composes freely with <see cref="IBridgeEntity"/> and with
/// <see cref="ITemporalEntity"/> (the latter gives an audit history of
/// reference-data changes — useful for "what did our system think this
/// currency was on date X" undo scenarios).</para>
///
/// <para>Forbidden compositions: <c>IReadOnlyEntity + IInsertOnlyEntity</c>
/// (mutability contradiction).</para>
/// </summary>
public interface IReadOnlyEntity : IEntity
{
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/IReadOnlyEntity.cs
```

Proposed commit message: `feat(norse-abstractions-infrastructure): add IReadOnlyEntity mutability marker`

---

### Task 19: `RangeBoundType` enum

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/RangeBoundType.cs`

- [ ] **Step 1: Write `RangeBoundType.cs`**

```csharp
namespace Norse.Abstractions.Infrastructure;

/// <summary>
/// Bound semantics for a <see cref="TstzRange"/> endpoint. Postgres'
/// <c>tstzrange</c> stores bound types explicitly; the conventional
/// system-versioned-table representation is <c>[lower, upper)</c>
/// (lower-inclusive, upper-exclusive).
/// </summary>
public enum RangeBoundType
{
	/// <summary>Reserved sentinel; never assigned in practice.</summary>
	Unspecified = 0,

	/// <summary>The boundary value is part of the range.</summary>
	Inclusive = 1,

	/// <summary>The boundary value is not part of the range.</summary>
	Exclusive = 2,
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/RangeBoundType.cs
```

Proposed commit message: `feat(norse-abstractions-infrastructure): add RangeBoundType enum`

---

### Task 20: `TstzRange` value type

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/TstzRange.cs`

- [ ] **Step 1: Write `TstzRange.cs`**

```csharp
namespace Norse.Abstractions.Infrastructure;

/// <summary>
/// Maps to PostgreSQL <c>tstzrange</c>, the canonical type for
/// system-versioned periods in pg-18. The value type carries explicit
/// bound semantics so it round-trips correctly through pg's range
/// serialization.
///
/// <para>The conventional system-versioned-table representation is
/// <c>[lower, upper)</c> (lower-inclusive, upper-exclusive). The factory
/// <see cref="CurrentFrom"/> produces an unbounded-upper range representing
/// the "current row" semantics.</para>
///
/// <para>On the .NET side we expose <see cref="DateTimeOffset"/> for API
/// clarity (every consumer sees explicit UTC); on the wire to Postgres the
/// value converter normalizes to UTC and writes a <c>timestamptz</c>-typed
/// range (timestamptz is always UTC internally).</para>
/// </summary>
public readonly record struct TstzRange
{
	public required DateTimeOffset Lower { get; init; }
	public DateTimeOffset? Upper { get; init; }
	public required RangeBoundType LowerBound { get; init; }
	public required RangeBoundType UpperBound { get; init; }

	/// <summary>
	/// Factory for an open-upper range starting at <paramref name="since"/>:
	/// <c>[since, +infinity)</c>. The conventional "current row" shape.
	/// </summary>
	public static TstzRange CurrentFrom(DateTimeOffset since) => new()
	{
		Lower = since,
		Upper = null,
		LowerBound = RangeBoundType.Inclusive,
		UpperBound = RangeBoundType.Exclusive,
	};

	/// <summary>
	/// True if the range contains the given timestamp under the declared
	/// bound semantics. Open-upper ranges (Upper is null) treat upper as
	/// +infinity.
	/// </summary>
	public bool Contains(DateTimeOffset at)
	{
		bool lowerOk = LowerBound == RangeBoundType.Inclusive ? at >= Lower : at > Lower;
		bool upperOk = Upper is null
			|| (UpperBound == RangeBoundType.Inclusive ? at <= Upper.Value : at < Upper.Value);
		return lowerOk && upperOk;
	}
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/TstzRange.cs
```

Proposed commit message: `feat(norse-abstractions-infrastructure): add TstzRange value type with CurrentFrom and Contains`

---

### Task 21: `ITemporalEntity` system-versioned marker

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/ITemporalEntity.cs`

- [ ] **Step 1: Write `ITemporalEntity.cs`**

```csharp
namespace Norse.Abstractions.Infrastructure;

/// <summary>
/// Mutability mode marker: this entity is system-versioned via a
/// <see cref="TstzRange"/> column. Full CRUD via
/// <c>ICommandRepository&lt;T&gt;</c>; additionally queryable across time
/// via <c>ITemporalRepository&lt;T&gt;</c>.
///
/// <para>The <see cref="SystemPeriod"/> column is auto-configured by
/// Norse.Infrastructure.Persistence's <c>TemporalEntityConvention</c> (Plan B): GIST
/// exclusion constraint preventing overlapping current rows for the same
/// id, history table named <c>{schema}.{table}_history</c>, and triggers
/// on insert/update/delete that copy prior versions to the history table.</para>
///
/// <para>Composes freely with <see cref="IBridgeEntity"/> (a
/// system-versioned bridge entity has composite uniqueness AND tracks
/// changes over time) and with <see cref="IReadOnlyEntity"/> (an audit
/// trail of reference-data changes).</para>
///
/// <para>Forbidden composition: <c>ITemporalEntity + IInsertOnlyEntity</c>
/// (mutability contradiction).</para>
/// </summary>
public interface ITemporalEntity : IEntity
{
	TstzRange SystemPeriod { get; init; }
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/ITemporalEntity.cs
```

Proposed commit message: `feat(norse-abstractions-infrastructure): add ITemporalEntity marker carrying SystemPeriod`

---

### Task 22: `CacheLocallyAttribute` opt-in LRU declaration

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/CacheLocallyAttribute.cs`

- [ ] **Step 1: Write `CacheLocallyAttribute.cs`**

```csharp
namespace Norse.Abstractions.Infrastructure;

/// <summary>
/// Opt-in marker attribute on <see cref="IReadOnlyEntity"/> types,
/// requesting that <c>ICachedRepository&lt;T&gt;</c>'s implementation in
/// Norse.Infrastructure.Persistence wrap its Mongo reads in a worker-local LRU of the
/// configured size.
///
/// <para>The LRU is invalidated by a
/// <c>ReferenceDataReloadedEvent { EntityType, EffectiveAt }</c> published
/// by the seed pipeline (Plan D); subscribing workers drop their cache
/// entry for the affected entity type.</para>
///
/// <para>Recommendation: opt in only for entity types with bounded
/// cardinality (a few thousand rows) and very-frequent lookups in worker
/// hot paths. ISO currencies (200 rows, every monetary calculation) →
/// yes. ZIP codes (40,000 rows, hit on every customer address) →
/// measure first.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false)]
public sealed class CacheLocallyAttribute : Attribute
{
	public required int MaxEntries { get; init; }
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/CacheLocallyAttribute.cs
```

Proposed commit message: `feat(norse-abstractions-infrastructure): add CacheLocallyAttribute for opt-in worker LRU`

---

### Task 23: `IDocumentRepository<T>` Mongo contract

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/IDocumentRepository.cs`

- [ ] **Step 1: Write `IDocumentRepository.cs`**

```csharp
using System.Linq.Expressions;
using Norse.Abstractions.Contracts;

namespace Norse.Abstractions.Infrastructure;

/// <summary>
/// MongoDB-backed read + shim + replace contract. Accepts both
/// <see cref="IWireShape"/> (shim-able wire shapes) and
/// <see cref="IReferenceDocument"/> (reference-data projections) via the
/// common <see cref="IDocument"/> constraint. In practice, only consumers
/// of <see cref="IWireShape"/> types call <see cref="ShimAsync"/> /
/// <see cref="ReplaceAsync"/>; reference-data projection happens via the
/// projection worker (Plan D), not through repository methods.
///
/// <para>Available on both <c>.Server</c> (reads + shim writes) and
/// <c>.Worker</c> (view enrichment writes). The single point of contact
/// with the operational read store.</para>
/// </summary>
public interface IDocumentRepository<TDocument>
	where TDocument : class, IDocument
{
	// ── Reads ────────────────────────────────────────────────────────────────

	Task<TDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

	/// <summary>
	/// Server-side projection. The Mongo driver translates the projection
	/// expression into a <c>$project</c> stage; result documents are
	/// smaller (fewer fields cross the wire from Mongo), and BSON
	/// deserialization materializes directly into <typeparamref name="TProjection"/>.
	/// No in-memory mapping in the .NET process.
	///
	/// <para>Cross-collection joins (<c>$lookup</c>) are NOT permitted;
	/// CQRS purity is enforced by the absence of the API.</para>
	/// </summary>
	Task<TProjection?> GetByIdAsync<TProjection>(
		Guid id,
		Expression<Func<TDocument, TProjection>> projection,
		CancellationToken cancellationToken);

	Task<IReadOnlyList<TDocument>> QueryAsync(
		Expression<Func<TDocument, bool>> filter,
		Expression<Func<TDocument, object>>? sort,
		int skip,
		int take,
		CancellationToken cancellationToken);

	Task<IReadOnlyList<TProjection>> QueryAsync<TProjection>(
		Expression<Func<TDocument, bool>> filter,
		Expression<Func<TDocument, TProjection>> projection,
		Expression<Func<TDocument, object>>? sort,
		int skip,
		int take,
		CancellationToken cancellationToken);

	// ── Writes ───────────────────────────────────────────────────────────────

	/// <summary>
	/// .Server uses this. Plants the request portion of the wire shape with
	/// <see cref="ProcessingStatus.Pending"/>. Idempotent: re-shim with the
	/// same <paramref name="id"/> upserts.
	/// </summary>
	Task ShimAsync(Guid id, TDocument requestShape, CancellationToken cancellationToken);

	/// <summary>
	/// .Worker uses this after Postgres commit. Idempotent upsert with the
	/// enriched document; sets <see cref="ProcessingStatus.Active"/> on
	/// success or <see cref="ProcessingStatus.Rejected"/> on business
	/// validation failure (with <c>StatusReason</c> populated).
	/// </summary>
	Task ReplaceAsync(Guid id, TDocument enriched, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/IDocumentRepository.cs
```

Proposed commit message: `feat(norse-abstractions-infrastructure): add IDocumentRepository<T> Mongo contract with projection`

---

### Task 24: `ICommandRepository<T>` Postgres write contract

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/ICommandRepository.cs`

- [ ] **Step 1: Write `ICommandRepository.cs`**

```csharp
namespace Norse.Abstractions.Infrastructure;

/// <summary>
/// PostgreSQL write contract — the single mutation path for the
/// source of truth. Available only to <c>.Worker</c> (analyzer-forbidden
/// in <c>.Server</c> via a norse-primitives-architecture rule slotted later).
///
/// <para>There is no SaveChangesAsync on this surface and no separate
/// IUnitOfWork contract. The messaging library's per-handler session owns
/// the transaction: the DbContext is constructed with the session's
/// connection + transaction, and a registered OnSaveChanges callback
/// flushes EF Core's pending changes right before the framework commits.
/// Handlers add/update/remove via this contract, dispatch follow-on
/// commands via <c>context.Send</c>, and return; the framework's commit
/// pipeline does the rest atomically. See spec §4.2.</para>
///
/// <para>Analyzer-forbidden when <c>TEntity : IReadOnlyEntity</c>:
/// reference-data writes happen via the seed pipeline, never through this
/// contract.</para>
///
/// <para>Analyzer-forbidden when <c>TEntity : IInsertOnlyEntity</c>:
/// <see cref="UpdateAsync"/> and <see cref="RemoveAsync"/> are not legal
/// for write-once entities; only <see cref="AddAsync"/>.</para>
/// </summary>
public interface ICommandRepository<TEntity>
	where TEntity : IEntity
{
	Task AddAsync(TEntity entity, CancellationToken cancellationToken);

	// Analyzer-forbidden when TEntity : IInsertOnlyEntity.
	Task UpdateAsync(TEntity entity, CancellationToken cancellationToken);

	// Analyzer-forbidden when TEntity : IInsertOnlyEntity.
	Task RemoveAsync(TEntity entity, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/ICommandRepository.cs
```

Proposed commit message: `feat(norse-abstractions-infrastructure): add ICommandRepository<T> Postgres write contract`

---

### Task 25: `ICachedRepository<T>` reference-data read contract

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/ICachedRepository.cs`

- [ ] **Step 1: Write `ICachedRepository.cs`**

```csharp
using System.Linq.Expressions;

namespace Norse.Abstractions.Infrastructure;

/// <summary>
/// Reference-data read contract. Worker-only (analyzer-forbidden in
/// <c>.Server</c>). Backing storage is MongoDB — reference data is
/// projected from Postgres (source of truth, FK target) to Mongo by the
/// seed pipeline, and this contract reads it from Mongo.
///
/// <para>Norse.Infrastructure.Persistence's implementation MAY layer a worker-local
/// LRU on top of the Mongo backing for entity types that opt in via
/// <see cref="CacheLocallyAttribute"/>; the LRU is invalidated by a
/// <c>ReferenceDataReloadedEvent</c> published by the seed pipeline.
/// Opt-in caching is an impl detail, not part of the contract.</para>
///
/// <para>No write methods. Reference-data writes are the seed pipeline's
/// job; <c>ICommandRepository&lt;T&gt;</c> is analyzer-forbidden for
/// <c>T : IReadOnlyEntity</c>.</para>
/// </summary>
public interface ICachedRepository<TEntity>
	where TEntity : IReadOnlyEntity
{
	Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

	Task<IReadOnlyList<TEntity>> QueryAsync(
		Expression<Func<TEntity, bool>> filter,
		CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/ICachedRepository.cs
```

Proposed commit message: `feat(norse-abstractions-infrastructure): add ICachedRepository<T> reference-data contract`

---

### Task 26: `ITemporalRepository<T>` tstzrange query contract

**Files:**
- Create: `norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/ITemporalRepository.cs`

- [ ] **Step 1: Write `ITemporalRepository.cs`**

```csharp
using System.Linq.Expressions;

namespace Norse.Abstractions.Infrastructure;

/// <summary>
/// Postgres <c>tstzrange</c>-backed query contract for system-versioned
/// entities. Available only to <c>.Worker</c> and to admin/Warehouse-side
/// tooling (analyzer-forbidden in <c>.Server</c>).
///
/// <para>The implementation in Norse.Infrastructure.Persistence queries the
/// history table via <c>FromSqlInterpolated</c>; the
/// <c>TemporalEntityConvention</c> creates the history table + triggers
/// at migration time. When pg-18 native <c>WITH SYSTEM VERSIONING</c>
/// becomes reachable from Npgsql, the impl swaps the trigger approach for
/// native support transparently — the contract surface here does
/// not change.</para>
/// </summary>
public interface ITemporalRepository<TEntity>
	where TEntity : ITemporalEntity
{
	/// <summary>
	/// The state of this entity as our system knew it at
	/// <paramref name="at"/>. Returns null if no row's SystemPeriod
	/// contains the timestamp.
	/// </summary>
	Task<TEntity?> AsOfAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken);

	/// <summary>
	/// Full history of this entity, ordered by SystemPeriod lower bound
	/// ascending. Includes the current row at the tail.
	/// </summary>
	Task<IReadOnlyList<TEntity>> HistoryAsync(Guid id, CancellationToken cancellationToken);

	/// <summary>
	/// Every version of any entity matching <paramref name="filter"/>
	/// whose SystemPeriod overlaps [<paramref name="from"/>,
	/// <paramref name="to"/>]. Cross-time analytical query; intended for
	/// admin and Warehouse-side tooling.
	/// </summary>
	Task<IReadOnlyList<TEntity>> AsOfRangeAsync(
		Expression<Func<TEntity, bool>> filter,
		DateTimeOffset from,
		DateTimeOffset to,
		CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build src/Norse.Abstractions.Infrastructure/Norse.Abstractions.Infrastructure.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/src/Norse.Abstractions.Infrastructure/ITemporalRepository.cs
```

Proposed commit message: `feat(norse-abstractions-infrastructure): add ITemporalRepository<T> tstzrange query contract`

---

## Phase 5 — Norse.Abstractions.Infrastructure.Tests 🟢

### Task 27: Create the Norse.Abstractions.Infrastructure test project

**Files:**
- Create: `norse-abstractions-infrastructure/tests/Norse.Abstractions.Infrastructure.Tests/Norse.Abstractions.Infrastructure.Tests.csproj`

- [ ] **Step 1: Create the test project**

Run from `norse-abstractions-infrastructure/`:

```
dotnet new xunit --name Norse.Abstractions.Infrastructure.Tests --output tests/Norse.Abstractions.Infrastructure.Tests --framework net10.0
```

- [ ] **Step 2: Remove the auto-generated test class**

Run: `Remove-Item norse-abstractions-infrastructure/tests/Norse.Abstractions.Infrastructure.Tests/UnitTest1.cs`

- [ ] **Step 3: Replace the csproj contents**

Replace `norse-abstractions-infrastructure/tests/Norse.Abstractions.Infrastructure.Tests/Norse.Abstractions.Infrastructure.Tests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="NSubstitute" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Norse.Abstractions.Infrastructure\Norse.Abstractions.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add to the solution**

Run from `norse-abstractions-infrastructure/`:

```
dotnet sln Norse.Abstractions.Infrastructure.slnx add tests/Norse.Abstractions.Infrastructure.Tests/Norse.Abstractions.Infrastructure.Tests.csproj
```

- [ ] **Step 5: Verify it builds**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build tests/Norse.Abstractions.Infrastructure.Tests/Norse.Abstractions.Infrastructure.Tests.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 6: Stage**

```
git add norse-abstractions-infrastructure/tests/Norse.Abstractions.Infrastructure.Tests norse-abstractions-infrastructure/Norse.Abstractions.Infrastructure.slnx
```

Proposed commit message: `test(norse-abstractions-infrastructure): scaffold Norse.Abstractions.Infrastructure.Tests xUnit project`

---

### Task 28: Marker hierarchy tests

**Files:**
- Create: `norse-abstractions-infrastructure/tests/Norse.Abstractions.Infrastructure.Tests/MarkerHierarchyTests.cs`

These tests guard the structural invariants the spec commits to: every marker extends `IEntity`; the two specialized markers don't accidentally extend each other; `ITemporalEntity` is the only marker carrying `SystemPeriod`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Norse.Abstractions.Infrastructure;
using Shouldly;
using Xunit;

namespace Norse.Abstractions.Infrastructure.Tests;

public sealed class MarkerHierarchyTests
{
	[Fact]
	public void IBridgeEntity_extends_IEntity()
	{
		typeof(IBridgeEntity).IsAssignableTo(typeof(IEntity)).ShouldBeTrue();
	}

	[Fact]
	public void IInsertOnlyEntity_extends_IEntity()
	{
		typeof(IInsertOnlyEntity).IsAssignableTo(typeof(IEntity)).ShouldBeTrue();
	}

	[Fact]
	public void IReadOnlyEntity_extends_IEntity()
	{
		typeof(IReadOnlyEntity).IsAssignableTo(typeof(IEntity)).ShouldBeTrue();
	}

	[Fact]
	public void ITemporalEntity_extends_IEntity()
	{
		typeof(ITemporalEntity).IsAssignableTo(typeof(IEntity)).ShouldBeTrue();
	}

	[Theory]
	[InlineData(typeof(IInsertOnlyEntity), typeof(IReadOnlyEntity))]
	[InlineData(typeof(IInsertOnlyEntity), typeof(ITemporalEntity))]
	[InlineData(typeof(IReadOnlyEntity), typeof(ITemporalEntity))]
	[InlineData(typeof(IBridgeEntity), typeof(IInsertOnlyEntity))]
	[InlineData(typeof(IBridgeEntity), typeof(IReadOnlyEntity))]
	[InlineData(typeof(IBridgeEntity), typeof(ITemporalEntity))]
	public void Markers_do_not_extend_each_other(Type a, Type b)
	{
		// The markers compose orthogonally on an entity type; none of them
		// should inherit from another. The analyzer (slotted in
		// norse-primitives-architecture) keys off the entity's interface set;
		// if a marker silently inherited another, the allow/forbid matrix
		// would be ambiguous.
		a.IsAssignableTo(b).ShouldBeFalse($"{a.Name} unexpectedly inherits from {b.Name}");
		b.IsAssignableTo(a).ShouldBeFalse($"{b.Name} unexpectedly inherits from {a.Name}");
	}

	[Fact]
	public void Only_ITemporalEntity_declares_SystemPeriod()
	{
		typeof(ITemporalEntity).GetProperty(nameof(ITemporalEntity.SystemPeriod)).ShouldNotBeNull();
		typeof(IEntity).GetProperty("SystemPeriod").ShouldBeNull();
		typeof(IBridgeEntity).GetProperty("SystemPeriod").ShouldBeNull();
		typeof(IInsertOnlyEntity).GetProperty("SystemPeriod").ShouldBeNull();
		typeof(IReadOnlyEntity).GetProperty("SystemPeriod").ShouldBeNull();
	}

	[Fact]
	public void ITemporalEntity_SystemPeriod_is_TstzRange()
	{
		var property = typeof(ITemporalEntity).GetProperty(nameof(ITemporalEntity.SystemPeriod));
		property.ShouldNotBeNull();
		property.PropertyType.ShouldBe(typeof(TstzRange));
	}

	[Fact]
	public void IEntity_has_Guid_Id_getter_only()
	{
		var property = typeof(IEntity).GetProperty(nameof(IEntity.Id));
		property.ShouldNotBeNull();
		property.PropertyType.ShouldBe(typeof(Guid));
		property.CanRead.ShouldBeTrue();
		property.CanWrite.ShouldBeFalse();
	}
}
```

- [ ] **Step 2: Run the tests**

Run from `norse-abstractions-infrastructure/`:

```
dotnet test tests/Norse.Abstractions.Infrastructure.Tests/Norse.Abstractions.Infrastructure.Tests.csproj --filter "FullyQualifiedName~MarkerHierarchyTests"
```

Expected: all tests pass; zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/tests/Norse.Abstractions.Infrastructure.Tests/MarkerHierarchyTests.cs
```

Proposed commit message: `test(norse-abstractions-infrastructure): cover entity marker hierarchy invariants`

---

### Task 29: Repository contract shape tests

**Files:**
- Create: `norse-abstractions-infrastructure/tests/Norse.Abstractions.Infrastructure.Tests/RepositoryContractShapeTests.cs`

These tests verify the generic constraints on each repository contract match the spec. If somebody later removes `where T : IReadOnlyEntity` from `ICachedRepository<T>`, the test fails and the change becomes deliberate.

- [ ] **Step 1: Write the failing tests**

```csharp
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Infrastructure;
using Shouldly;
using Xunit;

namespace Norse.Abstractions.Infrastructure.Tests;

public sealed class RepositoryContractShapeTests
{
	[Fact]
	public void IDocumentRepository_is_generic_with_TDocument_constrained_to_class_and_IDocument()
	{
		var t = typeof(IDocumentRepository<>);
		t.IsGenericTypeDefinition.ShouldBeTrue();

		var parameter = t.GetGenericArguments()[0];
		parameter.GenericParameterAttributes.HasFlag(System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint).ShouldBeTrue();
		parameter.GetGenericParameterConstraints().ShouldContain(typeof(IDocument));
	}

	[Fact]
	public void ICommandRepository_is_generic_with_TEntity_constrained_to_IEntity()
	{
		var t = typeof(ICommandRepository<>);
		t.IsGenericTypeDefinition.ShouldBeTrue();

		var parameter = t.GetGenericArguments()[0];
		parameter.GetGenericParameterConstraints().ShouldContain(typeof(IEntity));
	}

	[Fact]
	public void ICachedRepository_is_generic_with_TEntity_constrained_to_IReadOnlyEntity()
	{
		var t = typeof(ICachedRepository<>);
		t.IsGenericTypeDefinition.ShouldBeTrue();

		var parameter = t.GetGenericArguments()[0];
		parameter.GetGenericParameterConstraints().ShouldContain(typeof(IReadOnlyEntity));
	}

	[Fact]
	public void ITemporalRepository_is_generic_with_TEntity_constrained_to_ITemporalEntity()
	{
		var t = typeof(ITemporalRepository<>);
		t.IsGenericTypeDefinition.ShouldBeTrue();

		var parameter = t.GetGenericArguments()[0];
		parameter.GetGenericParameterConstraints().ShouldContain(typeof(ITemporalEntity));
	}

	[Fact]
	public void IDocumentRepository_has_GetByIdAsync_QueryAsync_ShimAsync_ReplaceAsync()
	{
		var t = typeof(IDocumentRepository<>);
		var methodNames = t.GetMethods().Select(m => m.Name).ToHashSet();

		methodNames.ShouldContain("GetByIdAsync");
		methodNames.ShouldContain("QueryAsync");
		methodNames.ShouldContain("ShimAsync");
		methodNames.ShouldContain("ReplaceAsync");
	}

	[Fact]
	public void ICommandRepository_has_AddAsync_UpdateAsync_RemoveAsync_and_no_SaveChangesAsync()
	{
		var t = typeof(ICommandRepository<>);
		var methodNames = t.GetMethods().Select(m => m.Name).ToHashSet();

		methodNames.ShouldContain("AddAsync");
		methodNames.ShouldContain("UpdateAsync");
		methodNames.ShouldContain("RemoveAsync");
		methodNames.ShouldNotContain("SaveChangesAsync",
			"The contract surface deliberately omits SaveChangesAsync (and IUnitOfWork) — the messaging library's per-handler session owns the transaction. See spec §4.2.");
	}

	[Fact]
	public void ICachedRepository_has_no_write_methods()
	{
		var t = typeof(ICachedRepository<>);
		var methodNames = t.GetMethods().Select(m => m.Name).ToHashSet();

		methodNames.ShouldContain("GetByIdAsync");
		methodNames.ShouldContain("QueryAsync");
		methodNames.ShouldNotContain("AddAsync");
		methodNames.ShouldNotContain("UpdateAsync");
		methodNames.ShouldNotContain("RemoveAsync");
		methodNames.ShouldNotContain("ShimAsync");
		methodNames.ShouldNotContain("ReplaceAsync");
	}

	[Fact]
	public void ITemporalRepository_has_AsOfAsync_HistoryAsync_AsOfRangeAsync()
	{
		var t = typeof(ITemporalRepository<>);
		var methodNames = t.GetMethods().Select(m => m.Name).ToHashSet();

		methodNames.ShouldContain("AsOfAsync");
		methodNames.ShouldContain("HistoryAsync");
		methodNames.ShouldContain("AsOfRangeAsync");
	}

	[Fact]
	public void Asgard_Infrastructure_has_no_IUnitOfWork_type()
	{
		// The contract surface deliberately omits IUnitOfWork — the messaging
		// library's per-handler session owns the transaction (see spec §4.2).
		// If a future change reintroduces an IUnitOfWork interface, this test
		// fails and the reintroduction becomes a deliberate touchpoint.
		var assembly = typeof(IDocumentRepository<>).Assembly;
		assembly.GetType("Norse.Abstractions.Infrastructure.IUnitOfWork").ShouldBeNull();
	}
}
```

- [ ] **Step 2: Run the tests**

Run from `norse-abstractions-infrastructure/`:

```
dotnet test tests/Norse.Abstractions.Infrastructure.Tests/Norse.Abstractions.Infrastructure.Tests.csproj --filter "FullyQualifiedName~RepositoryContractShapeTests"
```

Expected: all tests pass; zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-abstractions-infrastructure/tests/Norse.Abstractions.Infrastructure.Tests/RepositoryContractShapeTests.cs
```

Proposed commit message: `test(norse-abstractions-infrastructure): cover repository contract generic constraints and method shapes`

---

### Task 30: `RangeBoundType` and `TstzRange` behavior tests

**Files:**
- Create: `norse-abstractions-infrastructure/tests/Norse.Abstractions.Infrastructure.Tests/RangeBoundTypeTests.cs`
- Create: `norse-abstractions-infrastructure/tests/Norse.Abstractions.Infrastructure.Tests/TstzRangeTests.cs`

- [ ] **Step 1: Write `RangeBoundTypeTests.cs`**

```csharp
using Norse.Abstractions.Infrastructure;
using Shouldly;
using Xunit;

namespace Norse.Abstractions.Infrastructure.Tests;

public sealed class RangeBoundTypeTests
{
	[Theory]
	[InlineData(RangeBoundType.Unspecified, 0)]
	[InlineData(RangeBoundType.Inclusive, 1)]
	[InlineData(RangeBoundType.Exclusive, 2)]
	public void Each_member_has_the_expected_integer_value(RangeBoundType member, int expected)
	{
		((int)member).ShouldBe(expected);
	}

	[Fact]
	public void Member_count_is_three()
	{
		Enum.GetValues<RangeBoundType>().Length.ShouldBe(3);
	}

	[Fact]
	public void Zero_is_Unspecified()
	{
		default(RangeBoundType).ShouldBe(RangeBoundType.Unspecified);
	}
}
```

- [ ] **Step 2: Write `TstzRangeTests.cs`**

```csharp
using Norse.Abstractions.Infrastructure;
using Shouldly;
using Xunit;

namespace Norse.Abstractions.Infrastructure.Tests;

public sealed class TstzRangeTests
{
	private static readonly DateTimeOffset T0 = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset T1 = T0.AddDays(1);
	private static readonly DateTimeOffset T2 = T0.AddDays(2);

	[Fact]
	public void CurrentFrom_produces_lower_inclusive_upper_exclusive_unbounded()
	{
		var range = TstzRange.CurrentFrom(T0);

		range.Lower.ShouldBe(T0);
		range.Upper.ShouldBeNull();
		range.LowerBound.ShouldBe(RangeBoundType.Inclusive);
		range.UpperBound.ShouldBe(RangeBoundType.Exclusive);
	}

	[Fact]
	public void Contains_returns_true_when_at_equals_lower_inclusive()
	{
		var range = TstzRange.CurrentFrom(T0);
		range.Contains(T0).ShouldBeTrue();
	}

	[Fact]
	public void Contains_returns_true_when_at_is_after_lower_and_upper_is_unbounded()
	{
		var range = TstzRange.CurrentFrom(T0);
		range.Contains(T1).ShouldBeTrue();
		range.Contains(DateTimeOffset.MaxValue).ShouldBeTrue();
	}

	[Fact]
	public void Contains_returns_false_when_at_is_before_lower_inclusive()
	{
		var range = TstzRange.CurrentFrom(T1);
		range.Contains(T0).ShouldBeFalse();
	}

	[Fact]
	public void Contains_treats_upper_exclusive_correctly()
	{
		var range = new TstzRange
		{
			Lower = T0,
			Upper = T2,
			LowerBound = RangeBoundType.Inclusive,
			UpperBound = RangeBoundType.Exclusive,
		};
		range.Contains(T0).ShouldBeTrue();   // lower inclusive
		range.Contains(T1).ShouldBeTrue();
		range.Contains(T2).ShouldBeFalse();  // upper exclusive
	}

	[Fact]
	public void Contains_treats_upper_inclusive_correctly()
	{
		var range = new TstzRange
		{
			Lower = T0,
			Upper = T2,
			LowerBound = RangeBoundType.Inclusive,
			UpperBound = RangeBoundType.Inclusive,
		};
		range.Contains(T2).ShouldBeTrue();   // upper inclusive
	}

	[Fact]
	public void Contains_treats_lower_exclusive_correctly()
	{
		var range = new TstzRange
		{
			Lower = T0,
			Upper = T2,
			LowerBound = RangeBoundType.Exclusive,
			UpperBound = RangeBoundType.Exclusive,
		};
		range.Contains(T0).ShouldBeFalse();  // lower exclusive
		range.Contains(T1).ShouldBeTrue();
	}

	[Fact]
	public void TstzRange_is_a_value_type()
	{
		typeof(TstzRange).IsValueType.ShouldBeTrue();
	}

	[Fact]
	public void TstzRange_has_structural_equality()
	{
		var a = TstzRange.CurrentFrom(T0);
		var b = TstzRange.CurrentFrom(T0);
		a.ShouldBe(b);
		(a == b).ShouldBeTrue();
	}

	[Fact]
	public void TstzRange_distinguishes_bound_types_in_equality()
	{
		var a = new TstzRange
		{
			Lower = T0,
			Upper = T2,
			LowerBound = RangeBoundType.Inclusive,
			UpperBound = RangeBoundType.Exclusive,
		};
		var b = a with { UpperBound = RangeBoundType.Inclusive };
		a.ShouldNotBe(b);
	}
}
```

- [ ] **Step 3: Run all tests in the project**

Run from `norse-abstractions-infrastructure/`:

```
dotnet test tests/Norse.Abstractions.Infrastructure.Tests/Norse.Abstractions.Infrastructure.Tests.csproj
```

Expected: all tests pass (the marker hierarchy + repository shape + range bound type + TstzRange tests); zero warnings.

- [ ] **Step 4: Stage**

```
git add norse-abstractions-infrastructure/tests/Norse.Abstractions.Infrastructure.Tests/RangeBoundTypeTests.cs norse-abstractions-infrastructure/tests/Norse.Abstractions.Infrastructure.Tests/TstzRangeTests.cs
```

Proposed commit message: `test(norse-abstractions-infrastructure): cover RangeBoundType and TstzRange behavior`

---

## Phase 6 — Full-solution verification 🟢

### Task 31: Build the entire solution and run all tests

**Files:** (none modified)

- [ ] **Step 1: Build the entire solution**

Run from `norse-abstractions-infrastructure/`:

```
dotnet build Norse.Abstractions.Infrastructure.slnx
```

Expected: build succeeds for all 4 projects (Norse.Abstractions.Contracts, Norse.Abstractions.Infrastructure, Norse.Abstractions.Contracts.Tests, Norse.Abstractions.Infrastructure.Tests); zero warnings.

- [ ] **Step 2: Run all tests**

Run from `norse-abstractions-infrastructure/`:

```
dotnet test Norse.Abstractions.Infrastructure.slnx
```

Expected output structure:
```
Test summary: total: <N>, failed: 0, succeeded: <N>, skipped: 0
```

`<N>` should be at least 25 tests (across the four test classes in Norse.Abstractions.Contracts.Tests and the four test classes in Norse.Abstractions.Infrastructure.Tests).

- [ ] **Step 3: If anything fails, fix the underlying issue and re-run**

The plan is structured so each task's tests pass before the next one starts. A failure here indicates either a copy-paste error or a divergence from one of the earlier tasks. Do not skip tests; fix the cause.

- [ ] **Step 4: No commit needed**

This is a verification step; no files were changed.

---

### Task 32: Final repo-state check and pointer-update memo

**Files:** (none modified — this is a documentation update)

- [ ] **Step 1: Verify the staged state is correct**

Run from the meta-repo root:

```
git status
```

Expected: working tree clean OR every change is already committed via the per-task commits the human has run. No untracked files left in `norse-abstractions-infrastructure/`.

- [ ] **Step 2: Confirm the foundation is in place for downstream plans**

The following plans / tasks are now unblocked:

- **Auth Plan B (forthcoming)** — its `AuthDbContext` will be defined in `Norse.Infrastructure.Persistence` (Plan B of this spec) and backs Auth's Postgres reporting projection; the entity markers and repository contracts are ready for `Norse.Auth.Worker`'s projected entities. *(Amended 2026-06-03: Auth Plan A's AuthEntityConfigurations task is void — Mongo is the identity system of record, and Auth's EF artifacts live in `.Worker`; see the auth spec §3.)*
- **Every product-context plan that needs repository-contract-family references** — those plans can now reference `Norse.Abstractions.Infrastructure` directly.
- **Norse Hosting plan's `EfCoreMigrationContributor<TContext>`** — its `TContext : DbContext` constraint is already satisfied because `InfrastructureDbContext` will derive from `DbContext` (in Plan B of this spec); no change needed in the hosting plan, but the foundation contracts exist for Norse.Infrastructure.Persistence to build on.

- [ ] **Step 3: No commit needed**

This is a verification step; no files were changed.

---

## Self-Review

**Spec coverage check** — each load-bearing section of the spec mapped to a task in this plan:

| Spec section | Task(s) | Notes |
|---|---|---|
| §4.1 IDocumentRepository<T> | 23 | Full contract surface including projection overloads |
| §4.2 ICommandRepository<T> | 24 | Including the IInsertOnlyEntity / IReadOnlyEntity analyzer-forbidden notes |
| §4.3 ICachedRepository<T> | 25 | IReadOnlyEntity constraint; no writes |
| §4.4 ITemporalRepository<T> | 26 | AsOf / History / AsOfRange |
| §4 "no IUnitOfWork" decision | 29 | Negative test asserts the type does not exist in Norse.Abstractions.Infrastructure |
| §5.1 Allow/forbid matrix | 17, 18, 21 | XML docs on each mutability marker enumerate the forbid pairs; analyzer enforcement is slotted in norse-primitives-architecture (out of scope for this plan) |
| §5.2 Two temporality flavors | 16, 21 | IBridgeEntity for business-effective; ITemporalEntity for system-versioned |
| §5.3 IDocument / IWireShape / IReferenceDocument | 6, 8, 9 | With ProcessingStatus on the shim-able marker only |
| §10.1 TstzRange value type | 20 | Including CurrentFrom factory and Contains predicate |
| §10.1 RangeBoundType enum | 19 | Explicit values per CLAUDE.md §5 |
| §14 Realm placement | 14, 15, 5, 3 | Norse.Abstractions.Contracts + Norse.Abstractions.Infrastructure projects under norse-abstractions-infrastructure submodule |
| §16 Resolved decisions chronicle | (n/a) | Captured in the spec, not the plan |

Out-of-scope per spec §2 (deferred to later plans):
- All concrete implementations (Plan B: Postgres; Plan C: Mongo)
- BSON conventions (Plan C)
- TemporalEntityConvention and tstzrange Npgsql interop (Plan B)
- IConnectionResolver (Plan B / C)
- MongoIndexAttribute (Plan C, lives in Norse.Infrastructure.Persistence)
- Reference-projection worker (Plan D)
- Worked example end-to-end test (Plan D)
- Analyzer rule numbers (assigned in norse-primitives-architecture)
- CLAUDE.md update (coordinated change; staged when Plan B / C land)

**Placeholder scan:** no TBDs, TODOs, "fill in later", or vague requirements. Every task contains the exact code, exact commands, and expected output.

**Type consistency check:**
- `IDocument` extended by `IWireShape` and `IReferenceDocument` — verified in Tasks 6, 8, 9, 11, 12.
- `IEntity` extended by `IBridgeEntity` / `IInsertOnlyEntity` / `IReadOnlyEntity` / `ITemporalEntity` — verified in Tasks 15–18, 21, 29.
- `TstzRange.SystemPeriod` on `ITemporalEntity` — Task 21 declares it as `TstzRange`; Task 28 verifies via reflection.
- `IDocumentRepository<TDocument>` constraint `where TDocument : class, IDocument` — Task 23 declares; Task 29 verifies via reflection.
- `ICommandRepository<TEntity>` constraint `where TEntity : IEntity` — Task 24 declares; Task 29 verifies.
- `ICachedRepository<TEntity>` constraint `where TEntity : IReadOnlyEntity` — Task 25 declares; Task 29 verifies.
- `ITemporalRepository<TEntity>` constraint `where TEntity : ITemporalEntity` — Task 26 declares; Task 29 verifies.
- No `IUnitOfWork` type — Task 29's negative test asserts the type is absent from the assembly.

All type references match between declaration and use.

---

## Execution Handoff

**Plan complete and staged at `docs/Midgard/plans/2026-05-21-midgard-persistence-foundation.md`.**

Per the spec-first-phase memory: this plan halts at the plan stage. Do not execute without explicit greenlight from the user.

When the user signals readiness to execute, the two execution options are:

1. **Subagent-Driven (recommended for larger plans)** — fresh subagent per task, review between tasks, fast iteration with `superpowers:subagent-driven-development`.
2. **Inline Execution** — execute tasks in-session using `superpowers:executing-plans`, batch execution with checkpoints.

For this plan (32 tasks, all 🟢, mechanically straightforward), inline execution is likely the right pick — there's no exploration cost, each task's output is well-defined, and the review cadence per-task would be heavier than the work.
