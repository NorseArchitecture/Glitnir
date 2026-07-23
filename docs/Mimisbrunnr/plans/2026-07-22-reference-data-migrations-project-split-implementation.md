# Reference.Data.Migrations Project Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split Mímisbrunnr's `src/Reference.Data.Migrations` project into three — a provider-agnostic project (contributor + seed contributor + TSVs) and two new provider projects (`Reference.Data.Migrations.PostgreSQL`, `Reference.Data.Migrations.SqlServer`), each holding its own `IDesignTimeDbContextFactory`.

**Architecture:** The provider projects each `ProjectReference` the agnostic project directly (not as independent siblings) — mirroring how Urðarbrunnr's own `Persistence.EntityFramework.Design.PostgreSQL` references `Persistence.EntityFramework.Design`. A downstream consumer takes exactly one provider package and gets the contributor and seed contributor transitively.

**Tech Stack:** .NET 11 preview, EF Core 11 preview, MSBuild `NorseRef` custom item (resolves to `ProjectReference` in this Bifröst dev-mode tree), `.slnx` solution format.

**Spec:** `docs/Mimisbrunnr/specs/2026-07-22-reference-data-migrations-project-split-design.md` (this same Glitnir repo).

## Global Constraints

- Repo root for all work: `Mimisbrunnr/` (currently on branch `feature/pickup_updated_urdarbrunnr`).
- No automatic git commits without explicit human review of the diff at each step — this plan's commit steps stage and commit locally on the existing feature branch only; never push, never touch `master`.
- `sealed` by default; `internal`/omitted accessibility unless a type must be public for cross-assembly use (both factory classes are `public sealed`, matching the existing PostgreSQL factory — required because `dotnet ef` discovers `IDesignTimeDbContextFactory<T>` via reflection across the assembly).
- Tabs for indentation; brand prefix (`Norse.`) is injected by `Directory.Build.props` — never hand-write it into a `.csproj`'s `PackageId`/`AssemblyName`/`RootNamespace`.
- `IsAotCompatible=false` on every project touching EF Core (`src/Directory.Build.props` defaults it to `true` platform-wide).
- **Out of scope, explicitly** (per spec §4): the scaffold-emission defect (no checked-in `Migrations/` folder exists for either provider — that stays true after this plan), standing up a SQL Server container in Bifröst's AppHost, and any Yggdrasil-side wiring (nothing references this project today).
- **Known baseline:** `dotnet test tests/Reference.Data.Tests/Reference.Data.Tests.csproj` currently reports **8 failed / 3 succeeded** — every failure is `MigrationsNotFound` (the deferred scaffold-emission bug), unrelated to this reorg. That exact count must be unchanged after this plan; do not attempt to fix it here.

---

## File Structure

```
src/Reference.Data.Migrations/                  (existing project, csproj + README rewritten)
  NorseReferenceDataMigrationContributor.cs      (unchanged)
  ReferenceDataSeedContributor.cs                (unchanged)
  Reference.Data.Migrations.csproj               (modified — drops PostgreSQL-specific refs)
  README.md                                      (rewritten)

src/Reference.Data.Migrations.PostgreSQL/        (new)
  ReferenceDataDbContextFactory.cs               (moved from the agnostic project, namespace updated)
  Reference.Data.Migrations.PostgreSQL.csproj    (new)
  README.md                                      (new)

src/Reference.Data.Migrations.SqlServer/         (new)
  ReferenceDataDbContextFactory.cs               (new, mirrors the PostgreSQL factory)
  Reference.Data.Migrations.SqlServer.csproj     (new)
  README.md                                      (new)

Mimisbrunnr.slnx                                 (modified — two new <Project> entries)
```

---

### Task 1: Scaffold Reference.Data.Migrations.PostgreSQL and relocate the factory

**Files:**
- Create: `src/Reference.Data.Migrations.PostgreSQL/Reference.Data.Migrations.PostgreSQL.csproj`
- Create: `src/Reference.Data.Migrations.PostgreSQL/README.md`
- Move: `src/Reference.Data.Migrations/ReferenceDataDbContextFactory.cs` → `src/Reference.Data.Migrations.PostgreSQL/ReferenceDataDbContextFactory.cs`
- Modify: `Mimisbrunnr.slnx`

**Interfaces:**
- Consumes: `Norse.Reference.Data.Migrations.csproj` (existing, `ProjectReference`), `NorsePostgreSqlDesignTimeDbContextFactory<TContext>` (Urðarbrunnr, `Norse.Persistence.EntityFramework.Design.PostgreSQL` — abstract members `DatabaseName` and `CreateContext(DbContextOptions<TContext>)`).
- Produces: `Norse.Reference.Data.Migrations.PostgreSQL.ReferenceDataDbContextFactory`, a `public sealed class` — Task 2 does not consume this directly, but the slnx entry and project must exist before Task 2's build-verification step references it.

- [ ] **Step 1: Create the project directory and csproj**

```bash
mkdir -p src/Reference.Data.Migrations.PostgreSQL
```

Write `src/Reference.Data.Migrations.PostgreSQL/Reference.Data.Migrations.PostgreSQL.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Reference.Data.Migrations.PostgreSQL: the PostgreSQL-targeted IDesignTimeDbContextFactory and checked-in EF migrations for ReferenceDataDbContext. Migration tooling only — never referenced from a runtime container.</Description>
		<!-- EF Core is not AOT/trim-compatible; override the src-level default. -->
		<IsAotCompatible>false</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../Reference.Data.Migrations/Reference.Data.Migrations.csproj" />
		<NorseRef Include="Persistence.EntityFramework.Design.PostgreSQL">
			<Repo>Urdarbrunnr</Repo>
		</NorseRef>
		<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="11.*-*">
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
		</PackageReference>
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Write the project README**

Write `src/Reference.Data.Migrations.PostgreSQL/README.md`:

```markdown
# Norse.Reference.Data.Migrations.PostgreSQL

The PostgreSQL-targeted `IDesignTimeDbContextFactory` and checked-in EF migrations for `ReferenceDataDbContext`. Migration tooling only — never referenced from a runtime container.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
```

- [ ] **Step 3: Move the factory file**

```bash
git mv src/Reference.Data.Migrations/ReferenceDataDbContextFactory.cs src/Reference.Data.Migrations.PostgreSQL/ReferenceDataDbContextFactory.cs
```

- [ ] **Step 4: Update the moved file's namespace**

The file's `namespace` declaration is still `Norse.Reference.Data.Migrations;` (the old project's namespace). It must become `Norse.Reference.Data.Migrations.PostgreSQL;` to match this project's `RootNamespace` (injected by `Directory.Build.props` as `Norse.$(MSBuildProjectName)`). `ReferenceDataDbContext` (declared in `Norse.Reference.Data`) still resolves unqualified — C#'s namespace lookup walks outward through every enclosing namespace, and `Norse.Reference.Data.Migrations.PostgreSQL` is nested under `Norse.Reference.Data` the same way the old namespace was.

Edit `src/Reference.Data.Migrations.PostgreSQL/ReferenceDataDbContextFactory.cs` — change line 4 from:

```csharp
namespace Norse.Reference.Data.Migrations;
```

to:

```csharp
namespace Norse.Reference.Data.Migrations.PostgreSQL;
```

The rest of the file (usings, class body) is unchanged:

```csharp
using Microsoft.EntityFrameworkCore;
using Norse.Persistence.EntityFramework.Design.PostgreSQL;

namespace Norse.Reference.Data.Migrations.PostgreSQL;

/// <summary>
/// Design-time factory for <see cref="ReferenceDataDbContext"/>, used only by <c>dotnet ef</c> tooling
/// (e.g. <c>dotnet ef migrations add</c>) to construct a context instance outside of DI at design time.
/// </summary>
public sealed class ReferenceDataDbContextFactory : NorsePostgreSqlDesignTimeDbContextFactory<ReferenceDataDbContext>
{
	/// <inheritdoc />
	protected override string DatabaseName => "norse_referencedata";

	/// <inheritdoc />
	protected override ReferenceDataDbContext CreateContext(DbContextOptions<ReferenceDataDbContext> options) =>
		new(options);
}
```

- [ ] **Step 5: Add the project to the solution**

Edit `Mimisbrunnr.slnx` — in the `/src/` folder, immediately after the existing `Reference.Data.Migrations` entry, add:

```xml
		<Project Path="src/Reference.Data.Migrations.PostgreSQL/Reference.Data.Migrations.PostgreSQL.csproj" />
```

So the `/src/` folder reads:

```xml
	<Folder Name="/src/">
		<File Path="src/Directory.Build.props" />
		<File Path="src/Directory.Build.targets" />
		<Project Path="src/Reference.Data/Reference.Data.csproj" />
		<Project Path="src/Reference.Data.Migrations/Reference.Data.Migrations.csproj" />
		<Project Path="src/Reference.Data.Migrations.PostgreSQL/Reference.Data.Migrations.PostgreSQL.csproj" />
	</Folder>
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build src/Reference.Data.Migrations.PostgreSQL/Reference.Data.Migrations.PostgreSQL.csproj`
Expected: `Build succeeded.` with `0 Error(s)`. (The old `Reference.Data.Migrations.csproj` still declares its now-unused PostgreSQL `NorseRef`/package at this point — that's expected and harmless; Task 2 removes it.)

- [ ] **Step 7: Commit**

```bash
git add src/Reference.Data.Migrations.PostgreSQL src/Reference.Data.Migrations/ReferenceDataDbContextFactory.cs Mimisbrunnr.slnx
git status --short
```

Confirm the status shows the factory as a rename (`R`) into the new project, plus the two new project files and the slnx change — then stop and hand off to the human for review before committing (per this repo's "stage, show diff, human commits" rule).

---

### Task 2: Strip PostgreSQL-specifics from the agnostic project

**Files:**
- Modify: `src/Reference.Data.Migrations/Reference.Data.Migrations.csproj`
- Modify: `src/Reference.Data.Migrations/README.md`

**Interfaces:**
- Consumes: nothing new — `NorseReferenceDataMigrationContributor.cs` and `ReferenceDataSeedContributor.cs` are untouched; they only ever needed `EfMigrationContributor<TContext>`/`MigrationConnectionStringAttribute` (from `Persistence.EntityFramework.Design`, agnostic) and `ISeedContributor`/`DeterministicGuid`/`TabularReader` (Asgard/Svartálfheim), never anything PostgreSQL-specific.
- Produces: the final, provider-agnostic `Reference.Data.Migrations.csproj` that `Reference.Data.Migrations.PostgreSQL` (Task 1) and `Reference.Data.Migrations.SqlServer` (Task 3) both `ProjectReference`.

- [ ] **Step 1: Rewrite the csproj**

Replace the full contents of `src/Reference.Data.Migrations/Reference.Data.Migrations.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Reference.Data.Migrations: the migration contributor and ISeedContributor that loads the UN M49 TSVs, provider-agnostic. Migration tooling only — never referenced from a runtime container.</Description>
		<!-- EF Core is not AOT/trim-compatible; override the src-level default. -->
		<IsAotCompatible>false</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<None Include="../../seeds/*.tsv" CopyToOutputDirectory="PreserveNewest" LinkBase="seeds" />
		<NorseRef Include="Persistence.EntityFramework.Design">
			<Repo>Urdarbrunnr</Repo>
		</NorseRef>
		<NorseRef Include="Primitives">
			<Repo>Svartalfheim</Repo>
		</NorseRef>
		<NorseRef Include="Primitives.Ingestion">
			<Repo>Svartalfheim</Repo>
		</NorseRef>
		<ProjectReference Include="../Reference.Data/Reference.Data.csproj" />
	</ItemGroup>
</Project>
```

This drops the `Persistence.EntityFramework.Design.PostgreSQL` `NorseRef` (replaced with the agnostic `Persistence.EntityFramework.Design`) and the `Microsoft.EntityFrameworkCore.Design` package reference (only needed by the factory, which now lives in the PostgreSQL/SqlServer projects).

- [ ] **Step 2: Rewrite the README**

Replace the full contents of `src/Reference.Data.Migrations/README.md`:

```markdown
# Norse.Reference.Data.Migrations

Migration contributor and `ISeedContributor` that loads the UN M49 TSVs, provider-agnostic. Migration tooling only — never referenced from a runtime container.

Provider-specific `IDesignTimeDbContextFactory` implementations and checked-in EF migrations live in the sibling `Reference.Data.Migrations.PostgreSQL` and `Reference.Data.Migrations.SqlServer` projects, each of which references this one.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/Reference.Data.Migrations.PostgreSQL/Reference.Data.Migrations.PostgreSQL.csproj`
Expected: `Build succeeded.` with `0 Error(s)` — this rebuilds the agnostic project as a dependency, proving the trimmed-down csproj still compiles and the PostgreSQL project still resolves `ReferenceDataDbContext` and `NorsePostgreSqlDesignTimeDbContextFactory<T>` correctly.

- [ ] **Step 4: Commit**

```bash
git add src/Reference.Data.Migrations/Reference.Data.Migrations.csproj src/Reference.Data.Migrations/README.md
git status --short
```

Stop and hand off to the human for review before committing.

---

### Task 3: Scaffold Reference.Data.Migrations.SqlServer and run full-solution verification

**Files:**
- Create: `src/Reference.Data.Migrations.SqlServer/Reference.Data.Migrations.SqlServer.csproj`
- Create: `src/Reference.Data.Migrations.SqlServer/README.md`
- Create: `src/Reference.Data.Migrations.SqlServer/ReferenceDataDbContextFactory.cs`
- Modify: `Mimisbrunnr.slnx`

**Interfaces:**
- Consumes: `Norse.Reference.Data.Migrations.csproj` (Task 2's final form, `ProjectReference`), `NorseSqlServerDesignTimeDbContextFactory<TContext>` (Urðarbrunnr, `Norse.Persistence.EntityFramework.Design.SqlServer` — abstract members `DatabaseName` and `CreateContext(DbContextOptions<TContext>)`, same shape as the PostgreSQL base class).
- Produces: `Norse.Reference.Data.Migrations.SqlServer.ReferenceDataDbContextFactory`, a `public sealed class`.

- [ ] **Step 1: Create the project directory and csproj**

```bash
mkdir -p src/Reference.Data.Migrations.SqlServer
```

Write `src/Reference.Data.Migrations.SqlServer/Reference.Data.Migrations.SqlServer.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Reference.Data.Migrations.SqlServer: the SQL Server-targeted IDesignTimeDbContextFactory and checked-in EF migrations for ReferenceDataDbContext. Migration tooling only — never referenced from a runtime container.</Description>
		<!-- EF Core is not AOT/trim-compatible; override the src-level default. -->
		<IsAotCompatible>false</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../Reference.Data.Migrations/Reference.Data.Migrations.csproj" />
		<NorseRef Include="Persistence.EntityFramework.Design.SqlServer">
			<Repo>Urdarbrunnr</Repo>
		</NorseRef>
		<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="11.*-*">
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
		</PackageReference>
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Write the factory class**

Write `src/Reference.Data.Migrations.SqlServer/ReferenceDataDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Norse.Persistence.EntityFramework.Design.SqlServer;

namespace Norse.Reference.Data.Migrations.SqlServer;

/// <summary>
/// Design-time factory for <see cref="ReferenceDataDbContext"/>, used only by <c>dotnet ef</c> tooling
/// (e.g. <c>dotnet ef migrations add</c>) to construct a context instance outside of DI at design time.
/// </summary>
public sealed class ReferenceDataDbContextFactory : NorseSqlServerDesignTimeDbContextFactory<ReferenceDataDbContext>
{
	/// <inheritdoc />
	protected override string DatabaseName => "norse_referencedata";

	/// <inheritdoc />
	protected override ReferenceDataDbContext CreateContext(DbContextOptions<ReferenceDataDbContext> options) =>
		new(options);
}
```

`ReferenceDataDbContext` resolves unqualified for the same reason it does in the PostgreSQL factory: `Norse.Reference.Data.Migrations.SqlServer` is nested under `Norse.Reference.Data`, so C#'s outward namespace lookup finds it without a `using`.

- [ ] **Step 3: Write the project README**

Write `src/Reference.Data.Migrations.SqlServer/README.md`:

```markdown
# Norse.Reference.Data.Migrations.SqlServer

The SQL Server-targeted `IDesignTimeDbContextFactory` and checked-in EF migrations for `ReferenceDataDbContext`. Migration tooling only — never referenced from a runtime container.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
```

- [ ] **Step 4: Add the project to the solution**

Edit `Mimisbrunnr.slnx` — in the `/src/` folder, immediately after the `Reference.Data.Migrations.PostgreSQL` entry added in Task 1, add:

```xml
		<Project Path="src/Reference.Data.Migrations.SqlServer/Reference.Data.Migrations.SqlServer.csproj" />
```

So the `/src/` folder reads:

```xml
	<Folder Name="/src/">
		<File Path="src/Directory.Build.props" />
		<File Path="src/Directory.Build.targets" />
		<Project Path="src/Reference.Data/Reference.Data.csproj" />
		<Project Path="src/Reference.Data.Migrations/Reference.Data.Migrations.csproj" />
		<Project Path="src/Reference.Data.Migrations.PostgreSQL/Reference.Data.Migrations.PostgreSQL.csproj" />
		<Project Path="src/Reference.Data.Migrations.SqlServer/Reference.Data.Migrations.SqlServer.csproj" />
	</Folder>
```

- [ ] **Step 5: Build the new project to verify**

Run: `dotnet build src/Reference.Data.Migrations.SqlServer/Reference.Data.Migrations.SqlServer.csproj`
Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 6: Build the whole solution**

Run: `dotnet build Mimisbrunnr.slnx`
Expected: `Build succeeded.` with `0 Error(s)` across all projects, including `Reference.Data`, `Reference.Data.Migrations`, `Reference.Data.Migrations.PostgreSQL`, `Reference.Data.Migrations.SqlServer`, `SeedTool`, and all four test projects.

- [ ] **Step 7: Run the existing test suite to confirm no regression**

Run: `dotnet test tests/Reference.Data.Tests/Reference.Data.Tests.csproj`
Expected: `Test run summary: Failed!` with `total: 11`, `failed: 8`, `succeeded: 3` — the same counts as the pre-reorg baseline. All 8 failures are still `MigrationsNotFound` against the `Norse.Reference.Data.Migrations` assembly (the tests derive `MigrationsAssembly` from `typeof(NorseReferenceDataMigrationContributor).Assembly`, and that type never moved). If the failure count or the failing test names differ from this baseline, stop — something in this reorg broke a code path the deferred bug wasn't already covering, and that's a regression, not the known issue.

**Note for whoever picks up the scaffold-emission investigation next:** once real migrations exist, they'll be generated by running `dotnet ef migrations add` from `Reference.Data.Migrations.PostgreSQL` (where the factory now lives), landing in that project's own assembly (`Norse.Reference.Data.Migrations.PostgreSQL`). The four test files under `tests/Reference.Data.Tests/` currently derive `MigrationsAssembly` from `typeof(NorseReferenceDataMigrationContributor).Assembly` — that now points at the *agnostic* project, not the one migrations will actually live in. Left as-is here deliberately (it doesn't change today's failure mode, and fixing test wiring for migrations that don't exist yet is premature) — but it will need to change to `typeof(ReferenceDataDbContextFactory).Assembly` (or equivalent) as part of that follow-up work.

- [ ] **Step 8: Commit**

```bash
git add src/Reference.Data.Migrations.SqlServer Mimisbrunnr.slnx
git status --short
```

Stop and hand off to the human for review before committing.

---

## Self-Review

**Spec coverage:** §2 (target layout) → Tasks 1–3 create exactly the three projects with the reference shape specified. §3 (mechanics) → `git mv` for the factory (Task 1), fresh authoring for the SqlServer factory (Task 3), slnx updates (Tasks 1 and 3), README rewrites (Tasks 1, 2, 3). §4 (out of scope) → no task touches the scaffold-emission defect, no SQL Server container is provisioned, no Yggdrasil changes. §5 (testing) → Task 3 Step 7 is the acceptance gate, calibrated to the actual current baseline (8 failed / 3 succeeded), not an idealized "all green" that doesn't reflect reality.

**Placeholder scan:** no TBD/TODO; every step shows complete file contents or exact commands with expected output.

**Type consistency:** `ReferenceDataDbContextFactory` (PostgreSQL, Task 1) and `ReferenceDataDbContextFactory` (SqlServer, Task 3) are same-named but different types in different namespaces/assemblies — verified no collision since neither project references the other. `DatabaseName => "norse_referencedata"` and `CreateContext(DbContextOptions<ReferenceDataDbContext> options) => new(options);` are identical across both factories, matching the base classes' identical abstract member shapes confirmed by reading both `NorsePostgreSqlDesignTimeDbContextFactory` and `NorseSqlServerDesignTimeDbContextFactory` directly.
