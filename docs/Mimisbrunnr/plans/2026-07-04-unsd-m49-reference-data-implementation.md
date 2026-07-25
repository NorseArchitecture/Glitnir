# UN M49 Reference Data — Implementation Plan

**Amendment (2026-07-25):** Every occurrence below of namespace `Norse.ReferenceData.Data` (and its `.Migrations`/`.Tests` variants), the class `ReferenceDataDbContext`, and the class `NorseReferenceDataMigrationContributor` is written in its 2026-07-04 working-title form. Shipped source has since renamed the realm's namespace to `Norse.Reference.Data`, the DbContext to `ReferenceDbContext`, and the migration contributor to `NorseReferenceMigrationContributor`. None of these renames are reflected below.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mímisbrunnr seeds the UN M49 TSVs into real Postgres tables (`Region`, `CountryOrArea`) through the shipped `ISeedContributor` chassis, and exposes a JSONB "dossier" query view — giving the platform its first end-to-end proof of both the relational surface and the query-viewmodel surface for reference data, wired all the way through Yggdrasil's migrations service and Bifröst's AppHost.

**Architecture:** Two new Mímisbrunnr projects — `ReferenceData.Data` (Tier-1 entities, `NorseDbContext` subclass, keyless dossier read-model, query extension) and `ReferenceData.Data.Migrations` (design-time factory, migration contributor, checked-in EF migrations including a raw-SQL view, and the `ISeedContributor` implementation that reads the committed TSVs). Yggdrasil's `Hosting.Migrations.Service` gets a new `NorseRef` so the generator discovers both contributors; Bifröst's AppHost provisions a second database (`norse_referencedata`) on the existing Postgres primary.

**Tech Stack:** .NET 10 (`net11.0` per repo TFM), EF Core (Npgsql), nietras Sep via `Norse.Primitives.Ingestion.TabularReader`, `Norse.Primitives.Identifiers.DeterministicGuid`, xUnit v3 (Microsoft.Testing.Platform) + Shouldly, Testcontainers.PostgreSql (new to the platform), System.Text.Json.

## Global Constraints

- **Spec of record:** `../specs/2026-07-04-unsd-m49-reference-data-design.md` ("Approved design, ready for planning"). Companion: `../../Platform/specs/2026-07-03-seeding-framework-design.md`. Where this plan's code differs from a spec's illustrative snippet, the plan follows what's actually shipped in Asgard/Urdarbrunnr/Midgard (verified against source, not the spec's draft samples) — see the deviation note immediately below.
- **Deviation from the design spec's literal text, deliberately corrected:** §2's `Region? ParentRegion` and `CountryOrArea.ParentRegion` are written nullable in the spec. Per platform convention (nav properties are always `Type Nav { get; init; } = null!;`, never `Type?` — FK scalar nullability alone governs required-ness), this plan declares `ParentRegion` as non-nullable `Region ParentRegion { get; init; } = null!;` on both entities. `ParentRegionId` stays `Guid?` and is what actually makes the relationship optional (`.IsRequired(false)` in `Configure`).
- **`ISeedContributor` (Asgard, shipped):** `string Name { get; }`, `Task SeedAsync(CancellationToken)`, `static virtual void ConfigureServices(IServiceCollection services) { }` (no-op default — omit the override, do not write an empty one).
- **`DeterministicGuid` (Svartalfheim, shipped):** `readonly record struct`, nested `DeterministicGuid.Namespaces` static class (`.Dns`, `.Url`, `.Oid`, `.X500`). Always fully qualify as `DeterministicGuid.Namespaces.Dns` — a bare `Namespaces.Dns` only compiles under a `using static` that this plan does not introduce.
- **Tier-1 entity convention (`NorseEntityBase<TSelf>` + `INorseEntity<TSelf>`):** `sealed class Foo : NorseEntityBase<Foo>, INorseEntity<Foo>` with `public static void Configure(EntityTypeBuilder<Foo> builder)`. The platform's first Tier-1 entities — no existing example to copy beyond the bare interface/base declarations themselves.
- **DbContext must be `partial`** so `EntityConfigurationApplicationGenerator` (Urdarbrunnr) can inject the `ConfigureNorseEntities` override. Do not call `ApplyNorseConfigurations()` manually — that's Tier-2-only (Identity's pattern); a Tier-1 partial context gets it for free.
- **Naming (Glitnir §5):** snake_case, plural tables, FK `{referenced_table_singular}_id`, constraint prefixes `pk_`/`fk_`/`uq_`/`ck_`/`ix_`. `UseSnakeCaseNamingConvention()` (already wired via `AddNorsePostgresContext`/`AddNorsePostgresMigrationContext`) auto-lowercases and auto-prefixes `fk_`/`ix_` for FKs and non-unique indexes — confirmed against Himinbjörg's checked-in migration. Unique indexes do **not** get an automatic `uq_` prefix (EF's default naming doesn't distinguish unique from non-unique), so this plan sets `.HasDatabaseName("uq_...")` explicitly wherever `.IsUnique()` is used.
- **First-of-a-kind patterns this plan establishes (no existing precedent to copy — decisions are made explicitly below, not guessed):** a Tier-1 `NorseEntityBase<TSelf>` entity; a `migrationBuilder.Sql(...)`-created database view; a `HasNoKey()`/`ToView()` keyless read-model; `Testcontainers.PostgreSql` for a real-Postgres integration test (per Glitnir's "no mocked-DB tests" rule).
- **Hands-off files — do not edit:** `Mimisbrunnr/src/Directory.Build.props`, `Mimisbrunnr/src/Directory.Build.targets`, `Mimisbrunnr/tests/Directory.Build.props`, `Mimisbrunnr/tests/Directory.Build.targets`. These are scatter-managed and immutable; if something seems to require editing one of them, stop and ask rather than editing.
- **`NorseRef` mechanics:** `<NorseRef Include="X"><Repo>Y</Repo></NorseRef>` resolves to a `ProjectReference` (Bifröst dev mode, `UseProjectReferences=true`) or `PackageReference` (CI/NuGet mode). Add `<Generator>true</Generator>` only on the project that itself consumes that generator's output (never on a project that merely depends on something downstream of it) — `src/Directory.Build.targets`'s `_NorseRemoveUnwantedGeneratorAnalyzers` target strips it everywhere else automatically.
- **Out of scope for this plan (explicitly deferred, do not build):** SQL Server / dual-provider wiring (Buvy: "will probably want to wire it all the way into SQL Server 2025 & 19beta1 on postgres but we will get there" — Postgres-only for now, the dossier view is Postgres-specific `jsonb`); Mímir's consumption of this data (Blazor/gRPC/worker — spec §6); environment-gated seeding; a per-Region rollup view.
- **Test conventions:** xUnit v3 + Shouldly only (already hoisted in `tests/Directory.Build.props`). No mocked-DB tests for anything touching database semantics — the seed contributor and the dossier view get real-Postgres integration tests via Testcontainers, not in-memory substitutes.

---

### Task 1: Testcontainers Postgres fixture (shared test infrastructure)

**Files:**
- Create: `Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceData.Data.Tests.csproj`
- Create: `Mimisbrunnr/tests/ReferenceData.Data.Tests/PostgresContainerFixture.cs`
- Create: `Mimisbrunnr/tests/ReferenceData.Data.Tests/PostgresCollection.cs`
- Create: `Mimisbrunnr/tests/ReferenceData.Data.Tests/PostgresContainerFixtureTests.cs`
- Modify: `Mimisbrunnr/Mimisbrunnr.slnx`

**Interfaces:**
- Produces: `PostgresContainerFixture.ConnectionString` (`string`, non-null only after `InitializeAsync` completes) — every later integration test task depends on this.
- Produces: `[Collection("Postgres")]` — later test classes use this attribute to share one container per test run instead of spinning up a new one per class.

This is the platform's first use of Testcontainers — no existing fixture to copy, so this task establishes the pattern the rest of the plan reuses.

- [ ] **Step 1: Create the test project**

```xml
<!-- Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceData.Data.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Testcontainers.PostgreSql" Version="*" />
	</ItemGroup>
	<ItemGroup>
		<ProjectReference Include="..\..\src\ReferenceData.Data\ReferenceData.Data.csproj" />
		<ProjectReference Include="..\..\src\ReferenceData.Data.Migrations\ReferenceData.Data.Migrations.csproj" />
	</ItemGroup>
</Project>
```

(The `ReferenceData.Data` and `ReferenceData.Data.Migrations` projects don't exist yet — this project won't build until Task 2/3 land. That's fine; Step 2 below only needs the fixture file itself to exist, and Step 4's build check is what actually proves it out, run again after Task 3.)

- [ ] **Step 2: Write the fixture**

```csharp
// Mimisbrunnr/tests/ReferenceData.Data.Tests/PostgresContainerFixture.cs
using Testcontainers.PostgreSql;

namespace Norse.ReferenceData.Data.Tests;

public sealed class PostgresContainerFixture : IAsyncLifetime
{
	readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
		.WithImage("postgres:19beta1-trixie")
		.WithDatabase("norse_referencedata")
		.Build();

	public string ConnectionString { get; private set; } = null!;

	public async ValueTask InitializeAsync()
	{
		await _container.StartAsync().ConfigureAwait(false);
		ConnectionString = _container.GetConnectionString();
	}

	public async ValueTask DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
```

```csharp
// Mimisbrunnr/tests/ReferenceData.Data.Tests/PostgresCollection.cs
namespace Norse.ReferenceData.Data.Tests;

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>;
```

- [ ] **Step 3: Write a smoke test proving the fixture works**

```csharp
// Mimisbrunnr/tests/ReferenceData.Data.Tests/PostgresContainerFixtureTests.cs
using Npgsql;

namespace Norse.ReferenceData.Data.Tests;

[Collection("Postgres")]
public class PostgresContainerFixtureTests(PostgresContainerFixture fixture)
{
	[Fact]
	public async Task Container_accepts_a_real_connection()
	{
		await using var connection = new NpgsqlConnection(fixture.ConnectionString);
		await connection.OpenAsync();

		connection.State.ShouldBe(System.Data.ConnectionState.Open);
	}
}
```

This test needs `Npgsql` directly — add it:

```xml
<!-- append to the first ItemGroup in ReferenceData.Data.Tests.csproj -->
<PackageReference Include="Npgsql" Version="*" />
```

- [ ] **Step 4: Add both new test/src projects to the solution now (paths only — projects land in later tasks)**

```xml
<!-- Mimisbrunnr.slnx — add alongside the existing /tests/ folder -->
<Folder Name="/src/">
	<File Path="src/Directory.Build.props" />
	<File Path="src/Directory.Build.targets" />
	<Project Path="src/ReferenceData.Data/ReferenceData.Data.csproj" />
	<Project Path="src/ReferenceData.Data.Migrations/ReferenceData.Data.Migrations.csproj" />
</Folder>
```

Add `<Project Path="tests/ReferenceData.Data.Tests/ReferenceData.Data.Tests.csproj" />` inside the existing `/tests/` folder, next to `SeedTool.Tests`.

- [ ] **Step 5: Confirm this task's own test can at least be discovered**

This will not compile yet (Task 2/3 projects don't exist). Skip running it now — Task 2's Step 4 is the first point this project actually builds. Note that dependency in the commit message.

- [ ] **Step 6: Commit**

```bash
git -C Mimisbrunnr add tests/ReferenceData.Data.Tests Mimisbrunnr.slnx
git -C Mimisbrunnr commit -m "test: add Testcontainers Postgres fixture for ReferenceData.Data (depends on Task 2/3 projects to build)"
```

---

### Task 2: `Region`/`CountryOrArea` entities and the Tier-1 `ReferenceDataDbContext`

**Files:**
- Create: `Mimisbrunnr/src/ReferenceData.Data/ReferenceData.Data.csproj`
- Create: `Mimisbrunnr/src/ReferenceData.Data/RegionLevel.cs`
- Create: `Mimisbrunnr/src/ReferenceData.Data/Region.cs`
- Create: `Mimisbrunnr/src/ReferenceData.Data/CountryOrArea.cs`
- Create: `Mimisbrunnr/src/ReferenceData.Data/ReferenceDataDbContext.cs`
- Create: `Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceDataDbContextModelTests.cs`

**Interfaces:**
- Produces: `Region { Guid Id; string M49Code; string Name; RegionLevel Level; Guid? ParentRegionId; Region ParentRegion; }`
- Produces: `CountryOrArea { Guid Id; string M49Code; string IsoAlpha2Code; string IsoAlpha3Code; string Name; Guid? ParentRegionId; Region ParentRegion; bool IsLeastDevelopedCountry; bool IsLandLockedDevelopingCountry; bool IsSmallIslandDevelopingState; }`
- Produces: `ReferenceDataDbContext(DbContextOptions<ReferenceDataDbContext>)` — Task 3's design-time factory and Task 5's seed contributor both take this as a constructor parameter.

- [ ] **Step 1: Create the runtime project**

```xml
<!-- Mimisbrunnr/src/ReferenceData.Data/ReferenceData.Data.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.ReferenceData.Data: canonical reference-data entities (Region, CountryOrArea), the ReferenceDataDbContext, and the country-or-area dossier read model. Runtime library — referenced by the migrations service and, later, Mímir's serving layer.</Description>
		<!-- EF Core is not AOT/trim-compatible; override the src-level default. -->
		<IsAotCompatible>false</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<NorseRef Include="EntityFramework">
			<Repo>Urdarbrunnr</Repo>
			<Generator>true</Generator>
		</NorseRef>
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing model test**

```csharp
// Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceDataDbContextModelTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Norse.ReferenceData.Data.Tests;

public class ReferenceDataDbContextModelTests
{
	static ReferenceDataDbContext CreateContext()
	{
		var options = new DbContextOptionsBuilder<ReferenceDataDbContext>()
			.UseNpgsql("Host=localhost;Database=model-build-only")
			.Options;
		return new ReferenceDataDbContext(options);
	}

	[Fact]
	public void Model_configures_Region_with_unique_M49Code_index_and_self_referencing_FK()
	{
		using var context = CreateContext();
		IEntityType entityType = context.Model.FindEntityType(typeof(Region))!;

		entityType.ShouldNotBeNull();
		entityType.GetIndexes().Any(i => i.IsUnique && i.Properties.Single().Name == nameof(Region.M49Code)).ShouldBeTrue();
		entityType.GetForeignKeys().Single().PrincipalEntityType.ClrType.ShouldBe(typeof(Region));
	}

	[Fact]
	public void Model_configures_CountryOrArea_with_three_unique_indexes_and_FK_to_Region()
	{
		using var context = CreateContext();
		IEntityType entityType = context.Model.FindEntityType(typeof(CountryOrArea))!;

		entityType.ShouldNotBeNull();
		entityType.GetIndexes().Count(i => i.IsUnique).ShouldBe(3);
		entityType.GetForeignKeys().Single().PrincipalEntityType.ClrType.ShouldBe(typeof(Region));
	}
}
```

- [ ] **Step 2b: Run it to confirm it fails**

```bash
dotnet test Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceData.Data.Tests.csproj --filter ReferenceDataDbContextModelTests
```
Expected: build failure — `Region`, `CountryOrArea`, `ReferenceDataDbContext` don't exist yet.

- [ ] **Step 3: Write the entities**

```csharp
// Mimisbrunnr/src/ReferenceData.Data/RegionLevel.cs
namespace Norse.ReferenceData.Data;

public enum RegionLevel
{
	Region = 1,
	Subregion = 2,
	IntermediateRegion = 3,
}
```

```csharp
// Mimisbrunnr/src/ReferenceData.Data/Region.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.EntityFramework;

namespace Norse.ReferenceData.Data;

public sealed class Region : NorseEntityBase<Region>, INorseEntity<Region>
{
	public Guid Id { get; init; }
	public string M49Code { get; init; } = null!;
	public string Name { get; init; } = null!;
	public RegionLevel Level { get; init; }
	public Guid? ParentRegionId { get; init; }
	public Region ParentRegion { get; init; } = null!;

	public static void Configure(EntityTypeBuilder<Region> builder)
	{
		builder.ToTable("regions");
		builder.HasKey(r => r.Id);
		builder.Property(r => r.M49Code).HasMaxLength(3).IsRequired();
		builder.Property(r => r.Name).IsRequired();
		builder.HasIndex(r => r.M49Code).IsUnique().HasDatabaseName("uq_regions_m49_code");
		builder.HasOne(r => r.ParentRegion).WithMany().HasForeignKey(r => r.ParentRegionId).IsRequired(false);
	}
}
```

```csharp
// Mimisbrunnr/src/ReferenceData.Data/CountryOrArea.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.EntityFramework;

namespace Norse.ReferenceData.Data;

public sealed class CountryOrArea : NorseEntityBase<CountryOrArea>, INorseEntity<CountryOrArea>
{
	public Guid Id { get; init; }
	public string M49Code { get; init; } = null!;
	public string IsoAlpha2Code { get; init; } = null!;
	public string IsoAlpha3Code { get; init; } = null!;
	public string Name { get; init; } = null!;
	public Guid? ParentRegionId { get; init; }
	public Region ParentRegion { get; init; } = null!;
	public bool IsLeastDevelopedCountry { get; init; }
	public bool IsLandLockedDevelopingCountry { get; init; }
	public bool IsSmallIslandDevelopingState { get; init; }

	public static void Configure(EntityTypeBuilder<CountryOrArea> builder)
	{
		builder.ToTable("country_or_areas");
		builder.HasKey(c => c.Id);
		builder.Property(c => c.M49Code).HasMaxLength(3).IsRequired();
		builder.Property(c => c.IsoAlpha2Code).HasMaxLength(2).IsRequired();
		builder.Property(c => c.IsoAlpha3Code).HasMaxLength(3).IsRequired();
		builder.Property(c => c.Name).IsRequired();
		builder.HasIndex(c => c.M49Code).IsUnique().HasDatabaseName("uq_country_or_areas_m49_code");
		builder.HasIndex(c => c.IsoAlpha2Code).IsUnique().HasDatabaseName("uq_country_or_areas_iso_alpha2_code");
		builder.HasIndex(c => c.IsoAlpha3Code).IsUnique().HasDatabaseName("uq_country_or_areas_iso_alpha3_code");
		builder.HasOne(c => c.ParentRegion).WithMany().HasForeignKey(c => c.ParentRegionId).IsRequired(false);
	}
}
```

```csharp
// Mimisbrunnr/src/ReferenceData.Data/ReferenceDataDbContext.cs
using Microsoft.EntityFrameworkCore;
using Norse.EntityFramework;

namespace Norse.ReferenceData.Data;

public sealed partial class ReferenceDataDbContext(DbContextOptions<ReferenceDataDbContext> options)
	: NorseDbContext(options);
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceData.Data.Tests.csproj --filter ReferenceDataDbContextModelTests
```
Expected: PASS (2 tests). Note: the project still won't fully build as part of the whole solution until Task 3 exists, but `dotnet test` on this project alone works once its `ProjectReference` to `ReferenceData.Data.Migrations` is a no-op stub — if the build fails purely because that sibling project doesn't exist yet, temporarily comment out its `ProjectReference` line in the `.csproj`, run the test, then restore the line before committing (Task 3 will make it real).

- [ ] **Step 5: Commit**

```bash
git -C Mimisbrunnr add src/ReferenceData.Data tests/ReferenceData.Data.Tests
git -C Mimisbrunnr commit -m "feat: add Region/CountryOrArea entities and Tier-1 ReferenceDataDbContext"
```

---

### Task 3: `ReferenceData.Data.Migrations` project, `InitialCreate` migration, and the migration contributor

**Files:**
- Create: `Mimisbrunnr/src/ReferenceData.Data.Migrations/ReferenceData.Data.Migrations.csproj`
- Create: `Mimisbrunnr/src/ReferenceData.Data.Migrations/ReferenceDataDbContextFactory.cs`
- Create: `Mimisbrunnr/src/ReferenceData.Data.Migrations/NorseReferenceDataMigrationContributor.cs`
- Create (EF-generated, checked in): `Mimisbrunnr/src/ReferenceData.Data.Migrations/Migrations/{timestamp}_InitialCreate.cs` + `.Designer.cs` + `ReferenceDataDbContextModelSnapshot.cs`
- Create: `Mimisbrunnr/tests/ReferenceData.Data.Tests/NorseReferenceDataMigrationContributorTests.cs`

**Interfaces:**
- Consumes: `ReferenceDataDbContext(DbContextOptions<ReferenceDataDbContext>)` (Task 2).
- Produces: `NorseReferenceDataMigrationContributor(ReferenceDataDbContext context) : IMigrationContributor`, connection-string name `"norse_referencedata"` — Task 6's Yggdrasil/AppHost wiring and Task 7's end-to-end run both depend on this exact name.

- [ ] **Step 1: Create the migrations project**

```xml
<!-- Mimisbrunnr/src/ReferenceData.Data.Migrations/ReferenceData.Data.Migrations.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.ReferenceData.Data.Migrations: migration contributor, IDesignTimeDbContextFactory, checked-in EF migrations, and the ISeedContributor that loads the UN M49 TSVs. Migration tooling only — never referenced from a runtime container.</Description>
		<!-- EF Core is not AOT/trim-compatible; override the src-level default. -->
		<IsAotCompatible>false</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="*">
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
		</PackageReference>
		<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="*" />
	</ItemGroup>
	<ItemGroup>
		<ProjectReference Include="../ReferenceData.Data/ReferenceData.Data.csproj" />
	</ItemGroup>
	<ItemGroup>
		<NorseRef Include="EntityFramework.Migrations">
			<Repo>Urdarbrunnr</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Write the design-time factory**

```csharp
// Mimisbrunnr/src/ReferenceData.Data.Migrations/ReferenceDataDbContextFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Norse.EntityFramework;

namespace Norse.ReferenceData.Data.Migrations;

public sealed class ReferenceDataDbContextFactory : IDesignTimeDbContextFactory<ReferenceDataDbContext>
{
	public ReferenceDataDbContext CreateDbContext(string[] args)
	{
		var connectionString =
			Environment.GetEnvironmentVariable("DOTNET_EFTOOLS_CONNECTIONSTRING")
			?? "Host=localhost;Port=5432;Database=norse_referencedata;Username=postgres;Password=devpassword";

		var optionsBuilder = new DbContextOptionsBuilder<ReferenceDataDbContext>()
			.UseNpgsql(connectionString,
				o => o.MigrationsAssembly(typeof(ReferenceDataDbContextFactory).Assembly.GetName().Name));

		NorseDbContextOptionsExtensions.ApplyNorseConventions(optionsBuilder);

		return new ReferenceDataDbContext(optionsBuilder.Options);
	}
}
```

- [ ] **Step 3: Write the migration contributor**

```csharp
// Mimisbrunnr/src/ReferenceData.Data.Migrations/NorseReferenceDataMigrationContributor.cs
using Norse.EntityFramework.Migrations;

namespace Norse.ReferenceData.Data.Migrations;

[MigrationConnectionString("norse_referencedata")]
public sealed class NorseReferenceDataMigrationContributor(ReferenceDataDbContext context)
	: EfMigrationContributor<ReferenceDataDbContext>(context)
{
	public override string Name => "Norse.ReferenceData";
}
```

- [ ] **Step 4: Generate the initial migration**

```bash
cd Mimisbrunnr/src/ReferenceData.Data.Migrations
dotnet ef migrations add InitialCreate --project . --startup-project .
```
Expected: creates `Migrations/{timestamp}_InitialCreate.cs`, `.Designer.cs`, and `ReferenceDataDbContextModelSnapshot.cs`, containing `CreateTable` calls for `regions` and `country_or_areas` with the FK/unique-index names from Task 2's `Configure` methods. Inspect the generated `Up()` — confirm table names are `regions`/`country_or_areas`, the self-referencing FK on `regions` is named `fk_regions_regions_parent_region_id`, and the three `country_or_areas` unique indexes carry the exact `uq_...` names from Task 2 (they will, since `.HasDatabaseName(...)` was explicit).

- [ ] **Step 5: Write the failing integration test**

```csharp
// Mimisbrunnr/tests/ReferenceData.Data.Tests/NorseReferenceDataMigrationContributorTests.cs
using Microsoft.EntityFrameworkCore;
using Norse.EntityFramework;
using Norse.ReferenceData.Data.Migrations;

namespace Norse.ReferenceData.Data.Tests;

[Collection("Postgres")]
public class NorseReferenceDataMigrationContributorTests(PostgresContainerFixture fixture)
{
	[Fact]
	public async Task MigrateAsync_creates_regions_and_country_or_areas_tables()
	{
		var options = new DbContextOptionsBuilder<ReferenceDataDbContext>()
			.UseNpgsql(fixture.ConnectionString)
			.Options;
		using var context = new ReferenceDataDbContext(options);
		var contributor = new NorseReferenceDataMigrationContributor(context);

		await contributor.MigrateAsync(TestContext.Current.CancellationToken);

		(await context.Database.GetAppliedMigrationsAsync()).ShouldContain(m => m.Contains("InitialCreate", StringComparison.Ordinal));
	}
}
```

- [ ] **Step 6: Run it to verify it fails first (before Step 4 ran, or on a clean checkout)**

```bash
dotnet test Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceData.Data.Tests.csproj --filter NorseReferenceDataMigrationContributorTests
```
Expected: FAIL if run against a checkout without the generated migration; since Step 4 already ran above, instead confirm the test currently passes and treat this step as the checkpoint — if it fails, the migration wasn't generated correctly in Step 4.

- [ ] **Step 7: Run it to verify it passes**

```bash
dotnet test Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceData.Data.Tests.csproj --filter NorseReferenceDataMigrationContributorTests
```
Expected: PASS.

- [ ] **Step 8: Restore Task 2's commented-out `ProjectReference` if it was disabled**

Confirm `ReferenceData.Data.Tests.csproj`'s `ProjectReference` to `ReferenceData.Data.Migrations` is active (not commented), then run the full test project once to confirm everything still builds together:

```bash
dotnet test Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceData.Data.Tests.csproj
```
Expected: all tests so far PASS.

- [ ] **Step 9: Commit**

```bash
git -C Mimisbrunnr add src/ReferenceData.Data.Migrations tests/ReferenceData.Data.Tests Mimisbrunnr.slnx
git -C Mimisbrunnr commit -m "feat: add ReferenceData.Data.Migrations project, InitialCreate migration, and migration contributor"
```

---

### Task 4: `country_or_area_dossier` JSONB view, keyless read model, and query extension

**Files:**
- Create: `Mimisbrunnr/src/ReferenceData.Data.Migrations/Migrations/{timestamp}_AddCountryOrAreaDossierView.cs`
- Create: `Mimisbrunnr/src/ReferenceData.Data/CountryOrAreaDossierRow.cs`
- Create: `Mimisbrunnr/src/ReferenceData.Data/CountryOrAreaDossier.cs`
- Modify: `Mimisbrunnr/src/ReferenceData.Data/ReferenceDataDbContext.cs`
- Create: `Mimisbrunnr/src/ReferenceData.Data/ReferenceDataDbContextExtensions.cs`
- Create: `Mimisbrunnr/tests/ReferenceData.Data.Tests/CountryOrAreaDossierTests.cs`

**Interfaces:**
- Consumes: `regions`/`country_or_areas` tables (Task 3).
- Produces: `ReferenceDataDbContextExtensions.GetCountryOrAreaDossierAsync(this ReferenceDataDbContext, string code, CancellationToken) : Task<CountryOrAreaDossier?>` — this is the "query viewmodel" surface the definition of done calls for; Task 7's manual verification exercises it directly.

This is the plan's first-of-a-kind: a raw-SQL view migration plus a `HasNoKey()` keyless read model. No existing platform code to mirror — the design below is this plan's own, documented so it's not a silent guess.

**Design decision — column layout:** the view carries `code`, `alpha2`, `alpha3` (plain `text`, for indexed direct lookup) plus a single `dossier jsonb` column holding the full nested document. A non-recursive `WITH` CTE first normalizes each country's three possible ancestor rows (its direct region parent may be a Subregion or an IntermediateRegion — the spec's "ragged-depth tree") into three explicit id columns (`region_id`, `subregion_id`, `intermediate_region_id`) keyed off each ancestor's own `Level`, then three plain `LEFT JOIN`s (not a recursive CTE — depth is fixed at three) resolve those ids to names for `jsonb_build_object`.

- [ ] **Step 1: Write the view migration**

```bash
cd Mimisbrunnr/src/ReferenceData.Data.Migrations
dotnet ef migrations add AddCountryOrAreaDossierView --project . --startup-project .
```
This creates an empty `Up`/`Down` pair (no model changes — the view isn't part of the EF model yet). Edit the generated file:

```csharp
// Mimisbrunnr/src/ReferenceData.Data.Migrations/Migrations/{timestamp}_AddCountryOrAreaDossierView.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norse.ReferenceData.Data.Migrations.Migrations;

public partial class AddCountryOrAreaDossierView : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.Sql(
			"""
			CREATE VIEW country_or_area_dossier AS
			WITH ancestry AS (
			    SELECT
			        coa.id AS country_id,
			        coa.m49_code, coa.iso_alpha2_code, coa.iso_alpha3_code, coa.name,
			        coa.is_least_developed_country, coa.is_land_locked_developing_country, coa.is_small_island_developing_state,
			        CASE WHEN r1.level = 3 THEN r1.id END AS intermediate_region_id,
			        CASE WHEN r1.level = 2 THEN r1.id WHEN r2.level = 2 THEN r2.id END AS subregion_id,
			        CASE WHEN r2.level = 1 THEN r2.id WHEN r3.level = 1 THEN r3.id END AS region_id
			    FROM country_or_areas coa
			    LEFT JOIN regions r1 ON r1.id = coa.parent_region_id
			    LEFT JOIN regions r2 ON r2.id = r1.parent_region_id
			    LEFT JOIN regions r3 ON r3.id = r2.parent_region_id
			)
			SELECT
			    a.m49_code AS code,
			    a.iso_alpha2_code AS alpha2,
			    a.iso_alpha3_code AS alpha3,
			    jsonb_build_object(
			        'code', a.m49_code,
			        'alpha2', a.iso_alpha2_code,
			        'alpha3', a.iso_alpha3_code,
			        'name', a.name,
			        'isLeastDevelopedCountry', a.is_least_developed_country,
			        'isLandLockedDevelopingCountry', a.is_land_locked_developing_country,
			        'isSmallIslandDevelopingState', a.is_small_island_developing_state,
			        'region', CASE WHEN region.id IS NULL THEN NULL ELSE jsonb_build_object(
			            'code', region.m49_code,
			            'name', region.name,
			            'subregion', CASE WHEN subregion.id IS NULL THEN NULL ELSE jsonb_build_object(
			                'code', subregion.m49_code,
			                'name', subregion.name,
			                'intermediateRegion', CASE WHEN intermediate_region.id IS NULL THEN NULL ELSE jsonb_build_object(
			                    'code', intermediate_region.m49_code,
			                    'name', intermediate_region.name
			                ) END
			            ) END
			        ) END
			    ) AS dossier
			FROM ancestry a
			LEFT JOIN regions region ON region.id = a.region_id
			LEFT JOIN regions subregion ON subregion.id = a.subregion_id
			LEFT JOIN regions intermediate_region ON intermediate_region.id = a.intermediate_region_id;

			CREATE UNIQUE INDEX uq_country_or_area_dossier_code ON country_or_areas (m49_code);
			""");
	}

	protected override void Down(MigrationBuilder migrationBuilder) =>
		migrationBuilder.Sql("DROP VIEW country_or_area_dossier;");
}
```

Note: the `CREATE UNIQUE INDEX` line duplicates Task 2's `uq_country_or_areas_m49_code` — remove it; the base table already carries that index, so drop that statement from the migration entirely (the view doesn't need its own index; Postgres views can't be indexed directly anyway — this plan's earlier "indexed for lookup" language refers to the *base table* index already in place from Task 2, not a new one here). Edit `Up()` to end at the `dossier` `SELECT` statement's closing `;`.

- [ ] **Step 2: Write the keyless read model**

```csharp
// Mimisbrunnr/src/ReferenceData.Data/CountryOrAreaDossierRow.cs
namespace Norse.ReferenceData.Data;

sealed class CountryOrAreaDossierRow
{
	public string Code { get; init; } = null!;
	public string Alpha2 { get; init; } = null!;
	public string Alpha3 { get; init; } = null!;
	public string Dossier { get; init; } = null!;
}
```

- [ ] **Step 3: Register it in the DbContext (hand-written override, not the generated one)**

```csharp
// Mimisbrunnr/src/ReferenceData.Data/ReferenceDataDbContext.cs — replace the whole file
using Microsoft.EntityFrameworkCore;
using Norse.EntityFramework;

namespace Norse.ReferenceData.Data;

public sealed partial class ReferenceDataDbContext(DbContextOptions<ReferenceDataDbContext> options)
	: NorseDbContext(options)
{
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		modelBuilder.Entity<CountryOrAreaDossierRow>(eb =>
		{
			eb.HasNoKey();
			eb.ToView("country_or_area_dossier");
			eb.Property(r => r.Dossier).HasColumnName("dossier").HasColumnType("jsonb");
		});
	}
}
```

This is safe alongside the generator's own partial declaration of `ConfigureNorseEntities` — `OnModelCreating` here is a plain override of `NorseDbContext.OnModelCreating`, a different method than the generated `ConfigureNorseEntities` override, and this file's `base.OnModelCreating(modelBuilder)` call still reaches the generated entity-configuration wiring.

- [ ] **Step 4: Write the dossier record types**

```csharp
// Mimisbrunnr/src/ReferenceData.Data/CountryOrAreaDossier.cs
namespace Norse.ReferenceData.Data;

public sealed record CountryOrAreaDossier(
	string Code,
	string Alpha2,
	string Alpha3,
	string Name,
	bool IsLeastDevelopedCountry,
	bool IsLandLockedDevelopingCountry,
	bool IsSmallIslandDevelopingState,
	RegionDossier? Region);

public sealed record RegionDossier(string Code, string Name, SubregionDossier? Subregion);

public sealed record SubregionDossier(string Code, string Name, IntermediateRegionDossier? IntermediateRegion);

public sealed record IntermediateRegionDossier(string Code, string Name);
```

- [ ] **Step 5: Write the failing integration test**

```csharp
// Mimisbrunnr/tests/ReferenceData.Data.Tests/CountryOrAreaDossierTests.cs
using Microsoft.EntityFrameworkCore;
using Norse.ReferenceData.Data.Migrations;

namespace Norse.ReferenceData.Data.Tests;

[Collection("Postgres")]
public class CountryOrAreaDossierTests(PostgresContainerFixture fixture)
{
	static async Task<ReferenceDataDbContext> MigratedContextAsync(string connectionString, CancellationToken cancellationToken)
	{
		var options = new DbContextOptionsBuilder<ReferenceDataDbContext>().UseNpgsql(connectionString).Options;
		var context = new ReferenceDataDbContext(options);
		await new NorseReferenceDataMigrationContributor(context).MigrateAsync(cancellationToken);
		return context;
	}

	[Fact]
	public async Task Dossier_nests_region_subregion_and_intermediate_region_for_Nigeria()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		using var context = await MigratedContextAsync(fixture.ConnectionString, cancellationToken);

		var africa = new Region { Id = Guid.NewGuid(), M49Code = "002", Name = "Africa", Level = RegionLevel.Region };
		var subSaharan = new Region { Id = Guid.NewGuid(), M49Code = "202", Name = "Sub-Saharan Africa", Level = RegionLevel.Subregion, ParentRegionId = africa.Id };
		var westernAfrica = new Region { Id = Guid.NewGuid(), M49Code = "011", Name = "Western Africa", Level = RegionLevel.IntermediateRegion, ParentRegionId = subSaharan.Id };
		context.Set<Region>().AddRange(africa, subSaharan, westernAfrica);
		context.Set<CountryOrArea>().Add(new CountryOrArea
		{
			Id = Guid.NewGuid(), M49Code = "566", IsoAlpha2Code = "NG", IsoAlpha3Code = "NGA", Name = "Nigeria",
			ParentRegionId = westernAfrica.Id,
		});
		await context.SaveChangesAsync(cancellationToken);

		var dossier = await context.GetCountryOrAreaDossierAsync("566", cancellationToken);

		dossier.ShouldNotBeNull();
		dossier.Region.ShouldNotBeNull();
		dossier.Region.Subregion.ShouldNotBeNull();
		dossier.Region.Subregion.IntermediateRegion.ShouldNotBeNull();
		dossier.Region.Subregion.IntermediateRegion.Code.ShouldBe("011");
	}

	[Fact]
	public async Task Dossier_has_null_intermediate_region_for_Algeria()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		using var context = await MigratedContextAsync(fixture.ConnectionString, cancellationToken);

		var africa = new Region { Id = Guid.NewGuid(), M49Code = "002", Name = "Africa", Level = RegionLevel.Region };
		var northernAfrica = new Region { Id = Guid.NewGuid(), M49Code = "015", Name = "Northern Africa", Level = RegionLevel.Subregion, ParentRegionId = africa.Id };
		context.Set<Region>().AddRange(africa, northernAfrica);
		context.Set<CountryOrArea>().Add(new CountryOrArea
		{
			Id = Guid.NewGuid(), M49Code = "012", IsoAlpha2Code = "DZ", IsoAlpha3Code = "DZA", Name = "Algeria",
			ParentRegionId = northernAfrica.Id,
		});
		await context.SaveChangesAsync(cancellationToken);

		var dossier = await context.GetCountryOrAreaDossierAsync("012", cancellationToken);

		dossier.ShouldNotBeNull();
		dossier.Region.ShouldNotBeNull();
		dossier.Region.Subregion.ShouldNotBeNull();
		dossier.Region.Subregion.IntermediateRegion.ShouldBeNull();
	}

	[Fact]
	public async Task Dossier_has_null_region_for_Antarctica()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		using var context = await MigratedContextAsync(fixture.ConnectionString, cancellationToken);

		context.Set<CountryOrArea>().Add(new CountryOrArea
		{
			Id = Guid.NewGuid(), M49Code = "010", IsoAlpha2Code = "AQ", IsoAlpha3Code = "ATA", Name = "Antarctica",
			ParentRegionId = null,
		});
		await context.SaveChangesAsync(cancellationToken);

		var dossier = await context.GetCountryOrAreaDossierAsync("010", cancellationToken);

		dossier.ShouldNotBeNull();
		dossier.Region.ShouldBeNull();
	}
}
```

- [ ] **Step 6: Write the query extension method (making the test compile, still expected to fail on assertions until the view migration is correct)**

```csharp
// Mimisbrunnr/src/ReferenceData.Data/ReferenceDataDbContextExtensions.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Norse.ReferenceData.Data;

public static class ReferenceDataDbContextExtensions
{
	static readonly JsonSerializerOptions DossierJsonOptions = new() { PropertyNameCaseInsensitive = true };

	public static async Task<CountryOrAreaDossier?> GetCountryOrAreaDossierAsync(
		this ReferenceDataDbContext context, string code, CancellationToken cancellationToken)
	{
		var row = await context.Set<CountryOrAreaDossierRow>()
			.Where(r => r.Code == code)
			.SingleOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);

		return row is null ? null : JsonSerializer.Deserialize<CountryOrAreaDossier>(row.Dossier, DossierJsonOptions);
	}
}
```

- [ ] **Step 7: Run the tests, fix the migration if the shape doesn't match**

```bash
dotnet test Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceData.Data.Tests.csproj --filter CountryOrAreaDossierTests
```
Expected: PASS on all three (Nigeria/Algeria/Antarctica). If a case fails, the most likely culprit is the `ancestry` CTE's `CASE` conditions on `r1.level`/`r2.level` — re-verify against the worked example in this task's design-decision note above before changing anything else.

- [ ] **Step 8: Commit**

```bash
git -C Mimisbrunnr add src/ReferenceData.Data src/ReferenceData.Data.Migrations tests/ReferenceData.Data.Tests
git -C Mimisbrunnr commit -m "feat: add country_or_area_dossier JSONB view and query extension"
```

---

### Task 5: `ReferenceDataSeedContributor` — TSV-to-table seeding, idempotent

**Files:**
- Modify: `Mimisbrunnr/src/ReferenceData.Data.Migrations/ReferenceData.Data.Migrations.csproj`
- Create: `Mimisbrunnr/src/ReferenceData.Data.Migrations/ReferenceDataSeedContributor.cs`
- Create: `Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceDataSeedContributorTests.cs`

**Interfaces:**
- Consumes: `regions`/`country_or_areas` tables (Task 3), `TabularReader.OpenDelimited(string path, char separator) : ITabularReader` (Svartalfheim, shipped).
- Produces: `ReferenceDataSeedContributor(ReferenceDataDbContext context) : ISeedContributor`, `Name => "Norse.ReferenceData"` — Task 6's generator discovery picks this up by type, no further interface surface needed.

- [ ] **Step 1: Wire the TSVs and the Svartalfheim ingestion package into the migrations project**

```xml
<!-- Mimisbrunnr/src/ReferenceData.Data.Migrations/ReferenceData.Data.Migrations.csproj — add: -->
<ItemGroup>
	<NorseRef Include="Primitives.Ingestion">
		<Repo>Svartalfheim</Repo>
	</NorseRef>
</ItemGroup>
<ItemGroup>
	<None Include="../../../seeds/*.tsv" CopyToOutputDirectory="PreserveNewest" LinkBase="seeds" />
</ItemGroup>
```

(Path is relative to `Mimisbrunnr/src/ReferenceData.Data.Migrations/` up to `Mimisbrunnr/seeds/*.tsv`.)

- [ ] **Step 2: Write the failing integration test**

```csharp
// Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceDataSeedContributorTests.cs
using Microsoft.EntityFrameworkCore;
using Norse.ReferenceData.Data.Migrations;

namespace Norse.ReferenceData.Data.Tests;

[Collection("Postgres")]
public class ReferenceDataSeedContributorTests(PostgresContainerFixture fixture)
{
	static async Task<ReferenceDataDbContext> MigratedContextAsync(string connectionString, CancellationToken cancellationToken)
	{
		var options = new DbContextOptionsBuilder<ReferenceDataDbContext>().UseNpgsql(connectionString).Options;
		var context = new ReferenceDataDbContext(options);
		await new NorseReferenceDataMigrationContributor(context).MigrateAsync(cancellationToken);
		return context;
	}

	[Fact]
	public async Task SeedAsync_loads_248_countries_and_their_region_ancestors()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		using var context = await MigratedContextAsync(fixture.ConnectionString, cancellationToken);

		await new ReferenceDataSeedContributor(context).SeedAsync(cancellationToken);

		(await context.Set<CountryOrArea>().CountAsync(cancellationToken)).ShouldBe(248);
		(await context.Set<Region>().CountAsync(cancellationToken)).ShouldBeGreaterThan(0);
	}

	[Fact]
	public async Task SeedAsync_is_idempotent_on_a_second_run()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		using var context = await MigratedContextAsync(fixture.ConnectionString, cancellationToken);
		var contributor = new ReferenceDataSeedContributor(context);

		await contributor.SeedAsync(cancellationToken);
		var firstRunCount = await context.Set<CountryOrArea>().CountAsync(cancellationToken);

		await contributor.SeedAsync(cancellationToken);
		var secondRunCount = await context.Set<CountryOrArea>().CountAsync(cancellationToken);

		secondRunCount.ShouldBe(firstRunCount);
	}

	[Fact]
	public async Task Reseeding_from_scratch_produces_byte_identical_ids()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		using var contextA = await MigratedContextAsync(fixture.ConnectionString, cancellationToken);
		await new ReferenceDataSeedContributor(contextA).SeedAsync(cancellationToken);
		var nigeriaIdFirstRun = await contextA.Set<CountryOrArea>().Where(c => c.M49Code == "566").Select(c => c.Id).SingleAsync(cancellationToken);

		nigeriaIdFirstRun.ShouldBe(new DeterministicGuid(
			new DeterministicGuid(DeterministicGuid.Namespaces.Dns, "country-or-area.m49.referencedata.norse"), "566"));
	}
}
```

- [ ] **Step 3: Run it to verify it fails**

```bash
dotnet test Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceData.Data.Tests.csproj --filter ReferenceDataSeedContributorTests
```
Expected: FAIL — `ReferenceDataSeedContributor` doesn't exist.

- [ ] **Step 4: Write the seed contributor**

```csharp
// Mimisbrunnr/src/ReferenceData.Data.Migrations/ReferenceDataSeedContributor.cs
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Norse.Abstractions.Migrations.Seeding;
using Norse.Primitives.Identifiers;
using Norse.Primitives.Ingestion;

namespace Norse.ReferenceData.Data.Migrations;

public sealed class ReferenceDataSeedContributor(ReferenceDataDbContext context) : ISeedContributor
{
	static readonly Guid NamespaceRegion =
		new DeterministicGuid(DeterministicGuid.Namespaces.Dns, "region.m49.referencedata.norse");

	static readonly Guid NamespaceCountryOrArea =
		new DeterministicGuid(DeterministicGuid.Namespaces.Dns, "country-or-area.m49.referencedata.norse");

	public string Name => "Norse.ReferenceData";

	public async Task SeedAsync(CancellationToken cancellationToken)
	{
		var regionIdByCode = await SeedRegionsAsync(cancellationToken).ConfigureAwait(false);
		await SeedCountriesAsync(regionIdByCode, cancellationToken).ConfigureAwait(false);
	}

	async Task<Dictionary<string, Guid>> SeedRegionsAsync(CancellationToken cancellationToken)
	{
		var regionIdByCode = new Dictionary<string, Guid>();
		List<(Guid Id, string M49Code, string Name, RegionLevel Level, string? ParentM49Code)> rows = [];

		using ITabularReader reader = TabularReader.OpenDelimited(
			Path.Combine(AppContext.BaseDirectory, "seeds", "region.tsv"), '\t');
		var m49Ordinal = reader.Ordinal("M49Code");
		var nameOrdinal = reader.Ordinal("Name");
		var levelOrdinal = reader.Ordinal("Level");
		var parentOrdinal = reader.Ordinal("ParentM49Code");

		while (reader.Read())
		{
			var m49Code = reader[m49Ordinal].ToString();
			var level = (RegionLevel)int.Parse(reader[levelOrdinal], CultureInfo.InvariantCulture);
			var parentCode = reader[parentOrdinal].ToString();
			var id = new DeterministicGuid(NamespaceRegion, m49Code);

			regionIdByCode[m49Code] = id;
			rows.Add((id, m49Code, reader[nameOrdinal].ToString(), level, parentCode.Length == 0 ? null : parentCode));
		}

		foreach (var row in rows)
		{
			if (await context.Set<Region>().AnyAsync(r => r.Id == row.Id, cancellationToken).ConfigureAwait(false))
				continue;

			context.Set<Region>().Add(new Region
			{
				Id = row.Id,
				M49Code = row.M49Code,
				Name = row.Name,
				Level = row.Level,
				ParentRegionId = row.ParentM49Code is null ? null : regionIdByCode[row.ParentM49Code],
			});
		}

		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return regionIdByCode;
	}

	async Task SeedCountriesAsync(Dictionary<string, Guid> regionIdByCode, CancellationToken cancellationToken)
	{
		using ITabularReader reader = TabularReader.OpenDelimited(
			Path.Combine(AppContext.BaseDirectory, "seeds", "country-or-area.tsv"), '\t');
		var m49Ordinal = reader.Ordinal("M49Code");
		var alpha2Ordinal = reader.Ordinal("IsoAlpha2Code");
		var alpha3Ordinal = reader.Ordinal("IsoAlpha3Code");
		var nameOrdinal = reader.Ordinal("Name");
		var parentOrdinal = reader.Ordinal("ParentM49Code");
		var ldcOrdinal = reader.Ordinal("IsLeastDevelopedCountry");
		var lldcOrdinal = reader.Ordinal("IsLandLockedDevelopingCountry");
		var sidsOrdinal = reader.Ordinal("IsSmallIslandDevelopingState");

		while (reader.Read())
		{
			var m49Code = reader[m49Ordinal].ToString();
			var id = new DeterministicGuid(NamespaceCountryOrArea, m49Code);

			if (await context.Set<CountryOrArea>().AnyAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false))
				continue;

			var parentCode = reader[parentOrdinal].ToString();

			context.Set<CountryOrArea>().Add(new CountryOrArea
			{
				Id = id,
				M49Code = m49Code,
				IsoAlpha2Code = reader[alpha2Ordinal].ToString(),
				IsoAlpha3Code = reader[alpha3Ordinal].ToString(),
				Name = reader[nameOrdinal].ToString(),
				ParentRegionId = parentCode.Length == 0 ? null : regionIdByCode[parentCode],
				IsLeastDevelopedCountry = reader[ldcOrdinal].SequenceEqual("x"),
				IsLandLockedDevelopingCountry = reader[lldcOrdinal].SequenceEqual("x"),
				IsSmallIslandDevelopingState = reader[sidsOrdinal].SequenceEqual("x"),
			});
		}

		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}
}
```

Note: `DeterministicGuid`'s implicit conversion to `Guid` (`public static implicit operator Guid(DeterministicGuid value)`) makes `Id = new DeterministicGuid(...)` assignable directly to the `Guid Id` property — no explicit `.Value` needed.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceData.Data.Tests.csproj --filter ReferenceDataSeedContributorTests
```
Expected: PASS on all three.

- [ ] **Step 6: Commit**

```bash
git -C Mimisbrunnr add src/ReferenceData.Data.Migrations tests/ReferenceData.Data.Tests
git -C Mimisbrunnr commit -m "feat: add ReferenceDataSeedContributor loading UN M49 TSVs into Region/CountryOrArea"
```

---

### Task 6: Wire Yggdrasil's migrations service and Bifröst's AppHost

**Files:**
- Modify: `Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj`
- Modify: `Bifrost/src/Orchestration.AppHost/AppHost.cs`

**Interfaces:**
- Consumes: `NorseReferenceDataMigrationContributor` (Task 3), `ReferenceDataSeedContributor` (Task 5), connection-string name `"norse_referencedata"` (fixed by Task 3's `[MigrationConnectionString]` attribute).

- [ ] **Step 1: Add the NorseRef to Yggdrasil's migrations service**

```xml
<!-- Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj — add alongside the existing Urdarbrunnr/Midgard/Himinbjorg NorseRef entries -->
<NorseRef Include="ReferenceData.Data.Migrations">
	<Repo>Mimisbrunnr</Repo>
</NorseRef>
```

- [ ] **Step 2: Rebuild and confirm the generator picks up both new types**

```bash
dotnet build Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj
```
Then inspect the regenerated source:

```bash
find Yggdrasil/src/Hosting.Migrations.Service/obj -name "NorseMigrationsExtensions.g.cs" -newer Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj
cat $(find Yggdrasil/src/Hosting.Migrations.Service/obj -name "NorseMigrationsExtensions.g.cs")
```
Expected: the generated `AddNorseMigrations()` now includes a call for `NorseReferenceDataMigrationContributor` alongside the existing `NorseIdentityMigrationContributor`, plus a `ConfigureSeedContributor<ReferenceDataSeedContributor>(builder.Services);` line and its `AddTransient<ISeedContributor, ReferenceDataSeedContributor>()` registration.

- [ ] **Step 3: Provision the second database in Bifröst's AppHost**

```csharp
// Bifrost/src/Orchestration.AppHost/AppHost.cs — add after norseIdentity's declaration, before migrationsService
var norseReferenceData = pgPrimary.AddDatabase("norse-referencedata", databaseName: "norse_referencedata");
```

Then update `migrationsService`'s registration to add the second `WithReference`:

```csharp
var migrationsService = builder
	.AddProject<Projects.Hosting_Migrations_Service>("migrations")
	.WithReference(norseIdentity, connectionName: "norse_identity")
	.WithReference(norseReferenceData, connectionName: "norse_referencedata")
	.WaitFor(norseIdentity)
	.WaitFor(norseReferenceData);
```

- [ ] **Step 4: Build the whole AppHost to confirm no wiring errors**

```bash
dotnet build Bifrost/src/Orchestration.AppHost/Orchestration.AppHost.csproj
```
Expected: builds clean.

- [ ] **Step 5: Commit (three separate repos — Yggdrasil, Bifröst; Mimisbrunnr already committed in earlier tasks)**

```bash
git -C Yggdrasil add src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj
git -C Yggdrasil commit -m "feat: add Mimisbrunnr ReferenceData.Data.Migrations to the migrations service"

git -C Bifrost add src/Orchestration.AppHost/AppHost.cs
git -C Bifrost commit -m "feat: provision norse_referencedata database and wire it to the migrations service"
```

---

### Task 7: End-to-end verification against the spec's success criteria

**Files:** none (verification only — no code changes).

- [ ] **Step 1: Run the full composition**

```bash
cd Bifrost
dotnet run --project src/Orchestration.AppHost
```

- [ ] **Step 2: Confirm both databases stand up and the migrations service exits cleanly**

Check the Aspire dashboard (or container logs) for the `migrations` resource — expect it to run to completion and stop (per `SeedRunnerService`'s `StopApplication()` call), with no unhandled exceptions.

- [ ] **Step 3: Confirm row counts and idempotency directly against Postgres**

```bash
psql "host=localhost port=5432 dbname=norse_referencedata user=postgres" -c "SELECT count(*) FROM country_or_areas;"
psql "host=localhost port=5432 dbname=norse_referencedata user=postgres" -c "SELECT count(*) FROM regions;"
```
Expected: `country_or_areas` = 248, `regions` > 0, matching spec §7's first bullet. Re-run `dotnet run --project src/Orchestration.AppHost` a second time and re-check both counts are unchanged — proves spec §7's idempotency bullet.

- [ ] **Step 4: Confirm the three dossier cases from spec §7 directly against the view**

```bash
psql "host=localhost port=5432 dbname=norse_referencedata user=postgres" -c "SELECT dossier FROM country_or_area_dossier WHERE code = '566';"   -- Nigeria: full 3-level nesting
psql "host=localhost port=5432 dbname=norse_referencedata user=postgres" -c "SELECT dossier FROM country_or_area_dossier WHERE code = '012';"   -- Algeria: intermediateRegion null
psql "host=localhost port=5432 dbname=norse_referencedata user=postgres" -c "SELECT dossier FROM country_or_area_dossier WHERE code = '010';"   -- Antarctica: region null
```

- [ ] **Step 5: Report back**

Summarize actual row counts and paste the three dossier JSON payloads for confirmation. If any of Task 7's checks fail, that's a signal to go back to the specific task above rather than patching ad hoc at this layer — this task makes no code changes itself.

---

## Self-Review

**Spec coverage:** §1 (source of record, TSVs never parsed directly by the seed contributor) → Task 5. §2 (entities) → Task 2. §3 (GUID scheme) → Task 5. §4 (seed contributor ordering, Region before CountryOrArea) → Task 5's `SeedAsync` sequencing. §5 (dossier view) → Task 4. §7 (success criteria) → Task 7, checked one-for-one. §6 (out of scope) → Global Constraints' deferred list.

**Placeholder scan:** none found — every step has concrete code, exact paths, and runnable commands.

**Type consistency:** `ReferenceDataDbContext` (Task 2) is the constructor parameter for `NorseReferenceDataMigrationContributor` (Task 3), `ReferenceDataSeedContributor` (Task 5), and the extension method receiver (Task 4) — consistent throughout. `RegionLevel` enum values (Task 2) match the `int.Parse` cast in Task 5's seed contributor. Connection-string name `"norse_referencedata"` is identical across Task 3's attribute, Task 6's AppHost `WithReference`, and Task 7's `psql` target database.
