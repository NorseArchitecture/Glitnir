# CountryOrArea RegionAncestry Revision Implementation Plan

**Amendment (2026-07-25):** Every renamed identifier below is written in its 2026-07-04/05 working-title form throughout this plan (including the file's own title, which still reads "RegionAncestry"). Shipped source has since renamed: `RegionAncestry` (the column/property on `CountryOrArea`) → `View` (see `../specs/2026-07-04-unsd-m49-reference-data-design.md` §5, Revision 2); namespace `Norse.ReferenceData.Data` → `Norse.Reference.Data`; `ReferenceDataDbContext` → `ReferenceDbContext`; `NorseReferenceDataMigrationContributor` → `NorseReferenceMigrationContributor`. None of these renames are reflected below — read every occurrence accordingly.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hand-written `country_or_area_dossier` Postgres view with an EF Core native owned-JSON column (`CountryOrArea.RegionAncestry`), hydrated in C# by the seed contributor at seed time instead of derived by the database at read time.

**Architecture:** Delete the view, the keyless `CountryOrAreaDossierRow` entity, and the bespoke query extension. Add three plain owned-JSON types (`RegionNode`/`SubregionNode`/`IntermediateRegionNode`) mapped onto `CountryOrArea` via `.OwnsOne(...).ToJson()`. The seed contributor builds the ancestor-chain graph in memory (from the same region rows it already reads) and sets it on each `CountryOrArea` before insert. Querying becomes plain LINQ (`.Select(c => c.RegionAncestry)`), translated to native JSON path expressions server-side by EF.

**Tech Stack:** EF Core 10.0.9 native owned-JSON mapping (`.ToJson()`), Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2 (confirmed jsonb/JSON-owned support), xUnit v3 + Shouldly, Testcontainers.PostgreSql.

## Global Constraints

- **Spec of record:** `../specs/2026-07-04-unsd-m49-reference-data-design.md` §5 (revised same day this plan was written — read §5 in full before starting; the code samples below match it exactly).
- **No database view, no raw SQL at all in the regenerated migration.** This revision removes `migrationBuilder.Sql(...)` entirely — the new migration is pure EF-scaffolded DDL (tables + one jsonb column).
- **`RegionAncestry` holds the ancestor chain only** — `CountryOrArea`'s own `M49Code`/`IsoAlpha2Code`/`IsoAlpha3Code`/`Name`/three booleans are NOT duplicated into the JSON; they stay plain relational columns on the same row.
- **The "many side" heuristic:** the JSON column lives on `CountryOrArea` (the many side relative to `Region`), never on `Region` (the one side, which would need an unbounded descendant collection).
- **`RegionNode`/`SubregionNode`/`IntermediateRegionNode` are plain classes — NOT Tier-1 entities.** They must NOT implement `INorseEntity<TSelf>` and must NOT extend `NorseEntityBase<TSelf>`. Both `RequireEntityConfigurationConvention` and `RequireExplicitLengthConvention` (`Urdarbrunnr/src/EntityFramework/`) explicitly skip any entity type where `IsMappedToJson()` is true — confirmed by reading both convention's source directly. Adding `INorseEntity<TSelf>`/`NorseEntityBase<TSelf>` to these types is unnecessary and wrong; it will build, but it's not what the convention requires and adds ceremony these types don't need.
- **`.ToJson()` is called exactly once, on the outermost owned navigation** (`RegionAncestry` on `CountryOrArea`). Nested `.OwnsOne(...)` calls for `Subregion`/`IntermediateRegion` do NOT get their own `.ToJson()` — they inherit the JSON-column mapping from the outermost call. Calling it more than once throws at model-finalization time ("Only call 'ToJson()' on the outermost owned entity type").
- **Migrations stay squashed to one.** Per the platform's standing convention while the EF chassis is unsettled — see `feedback_squash-migrations-until-chassis-ships` — delete all existing migration files and regenerate a single fresh `InitialCreate` after this model change, exactly as was already done once for this repo.
- **Hands-off files, do not edit:** `Mimisbrunnr/src/Directory.Build.props`, `Mimisbrunnr/src/Directory.Build.targets`, `Mimisbrunnr/tests/Directory.Build.props`, `Mimisbrunnr/tests/Directory.Build.targets`.
- **Test conventions:** xUnit v3 + Shouldly (already hoisted). No mocked-DB tests for anything touching database semantics (a JSON column round-trip is database semantics) — use the existing `[Collection("Postgres")]` + `PostgresContainerFixture` (Task 1 of the original plan, already shipped) for every test that touches the real column. Every test that inserts rows must clean them up unconditionally in a `finally` — the shared Testcontainers container is never auto-reset between tests (established the hard way in the original plan's Task 4/5 reviews).
- **Every real-connection `DbContextOptionsBuilder<ReferenceDataDbContext>` needs both `MigrationsAssembly(...)` and `NorseDbContextOptionsExtensions.ApplyNorseConventions(...)`** — the `DbContext` and its migrations live in different assemblies, and the checked-in snapshot assumes snake_case naming. Copy the exact shape from `tests/ReferenceData.Data.Tests/ReferenceDataSeedContributorTests.cs`'s `MigratedContextAsync` helper (already correct) rather than re-deriving it.
- **Out of scope:** the CQRS command-side JSONB idea this revision is a proving ground for (per spec §6) — this plan touches only the reference-data query side.

---

### Task 1: Replace the view with `CountryOrArea.RegionAncestry` (owned-JSON column, migration, round-trip test)

**Files:**
- Delete: `Mimisbrunnr/src/ReferenceData.Data/CountryOrAreaDossierRow.cs`
- Delete: `Mimisbrunnr/src/ReferenceData.Data/CountryOrAreaDossier.cs`
- Delete: `Mimisbrunnr/src/ReferenceData.Data/ReferenceDataDbContextExtensions.cs`
- Delete: `Mimisbrunnr/tests/ReferenceData.Data.Tests/CountryOrAreaDossierTests.cs`
- Delete: `Mimisbrunnr/src/ReferenceData.Data.Migrations/Migrations/*` (all files — regenerated in Step 6)
- Create: `Mimisbrunnr/src/ReferenceData.Data/RegionNode.cs`
- Create: `Mimisbrunnr/src/ReferenceData.Data/SubregionNode.cs`
- Create: `Mimisbrunnr/src/ReferenceData.Data/IntermediateRegionNode.cs`
- Modify: `Mimisbrunnr/src/ReferenceData.Data/CountryOrArea.cs`
- Create: `Mimisbrunnr/tests/ReferenceData.Data.Tests/CountryOrAreaRegionAncestryTests.cs`

**Interfaces:**
- Produces: `CountryOrArea.RegionAncestry` (`RegionNode?`) — Task 2's seed contributor sets this property; no other task reads it directly (querying is plain LINQ against `CountryOrArea`, not a named helper).
- Produces: `RegionNode { string Code; string Name; SubregionNode? Subregion; }`, `SubregionNode { string Code; string Name; IntermediateRegionNode? IntermediateRegion; }`, `IntermediateRegionNode { string Code; string Name; }` — exact shape Task 2 constructs.

- [ ] **Step 1: Delete the view-era files**

```bash
cd Mimisbrunnr
rm src/ReferenceData.Data/CountryOrAreaDossierRow.cs
rm src/ReferenceData.Data/CountryOrAreaDossier.cs
rm src/ReferenceData.Data/ReferenceDataDbContextExtensions.cs
rm tests/ReferenceData.Data.Tests/CountryOrAreaDossierTests.cs
rm src/ReferenceData.Data.Migrations/Migrations/*
```

- [ ] **Step 2: Write the failing round-trip test**

```csharp
// Mimisbrunnr/tests/ReferenceData.Data.Tests/CountryOrAreaRegionAncestryTests.cs
using Microsoft.EntityFrameworkCore;
using Norse.EntityFramework;
using Norse.ReferenceData.Data.Migrations;

namespace Norse.ReferenceData.Data.Tests;

[Collection("Postgres")]
public class CountryOrAreaRegionAncestryTests(PostgresContainerFixture fixture)
{
	static async Task<ReferenceDataDbContext> MigratedContextAsync(string connectionString, CancellationToken cancellationToken)
	{
		var optionsBuilder = new DbContextOptionsBuilder<ReferenceDataDbContext>()
			.UseNpgsql(connectionString,
				o => o.MigrationsAssembly(typeof(NorseReferenceDataMigrationContributor).Assembly.GetName().Name));
		NorseDbContextOptionsExtensions.ApplyNorseConventions(optionsBuilder);
		var context = new ReferenceDataDbContext(optionsBuilder.Options);
		await new NorseReferenceDataMigrationContributor(context).MigrateAsync(cancellationToken).ConfigureAwait(false);
		return context;
	}

	[Fact]
	public async Task RegionAncestry_round_trips_all_three_levels_for_Nigeria_shape()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		using var context = await MigratedContextAsync(fixture.ConnectionString, cancellationToken);
		var countryId = Guid.NewGuid();

		try
		{
			context.Set<CountryOrArea>().Add(new CountryOrArea
			{
				Id = countryId, M49Code = "566", IsoAlpha2Code = "NG", IsoAlpha3Code = "NGA", Name = "Nigeria",
				RegionAncestry = new RegionNode
				{
					Code = "002",
					Name = "Africa",
					Subregion = new SubregionNode
					{
						Code = "202",
						Name = "Sub-Saharan Africa",
						IntermediateRegion = new IntermediateRegionNode { Code = "011", Name = "Western Africa" },
					},
				},
			});
			await context.SaveChangesAsync(cancellationToken);
			context.ChangeTracker.Clear();

			var reread = await context.Set<CountryOrArea>().SingleAsync(c => c.Id == countryId, cancellationToken);

			reread.RegionAncestry.ShouldNotBeNull();
			reread.RegionAncestry.Code.ShouldBe("002");
			reread.RegionAncestry.Subregion.ShouldNotBeNull();
			reread.RegionAncestry.Subregion.IntermediateRegion.ShouldNotBeNull();
			reread.RegionAncestry.Subregion.IntermediateRegion.Code.ShouldBe("011");
		}
		finally
		{
			await context.Set<CountryOrArea>().Where(c => c.Id == countryId).ExecuteDeleteAsync(cancellationToken);
		}
	}

	[Fact]
	public async Task RegionAncestry_has_null_intermediate_region_for_Algeria_shape()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		using var context = await MigratedContextAsync(fixture.ConnectionString, cancellationToken);
		var countryId = Guid.NewGuid();

		try
		{
			context.Set<CountryOrArea>().Add(new CountryOrArea
			{
				Id = countryId, M49Code = "012", IsoAlpha2Code = "DZ", IsoAlpha3Code = "DZA", Name = "Algeria",
				RegionAncestry = new RegionNode
				{
					Code = "002",
					Name = "Africa",
					Subregion = new SubregionNode { Code = "015", Name = "Northern Africa", IntermediateRegion = null },
				},
			});
			await context.SaveChangesAsync(cancellationToken);
			context.ChangeTracker.Clear();

			var reread = await context.Set<CountryOrArea>().SingleAsync(c => c.Id == countryId, cancellationToken);

			reread.RegionAncestry.ShouldNotBeNull();
			reread.RegionAncestry.Subregion.ShouldNotBeNull();
			reread.RegionAncestry.Subregion.IntermediateRegion.ShouldBeNull();
		}
		finally
		{
			await context.Set<CountryOrArea>().Where(c => c.Id == countryId).ExecuteDeleteAsync(cancellationToken);
		}
	}

	[Fact]
	public async Task RegionAncestry_is_null_for_Antarctica_shape()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		using var context = await MigratedContextAsync(fixture.ConnectionString, cancellationToken);
		var countryId = Guid.NewGuid();

		try
		{
			context.Set<CountryOrArea>().Add(new CountryOrArea
			{
				Id = countryId, M49Code = "010", IsoAlpha2Code = "AQ", IsoAlpha3Code = "ATA", Name = "Antarctica",
				RegionAncestry = null,
			});
			await context.SaveChangesAsync(cancellationToken);
			context.ChangeTracker.Clear();

			var reread = await context.Set<CountryOrArea>().SingleAsync(c => c.Id == countryId, cancellationToken);

			reread.RegionAncestry.ShouldBeNull();
		}
		finally
		{
			await context.Set<CountryOrArea>().Where(c => c.Id == countryId).ExecuteDeleteAsync(cancellationToken);
		}
	}
}
```

Note these tests never insert a real `Region` row — `RegionAncestry` is a self-contained JSON graph with no FK relationship to the `regions` table, so the round-trip is testable in complete isolation from the relational hierarchy.

- [ ] **Step 3: Run it to verify it fails**

```bash
dotnet test Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceData.Data.Tests.csproj
```
Expected: build failure — `RegionNode`/`SubregionNode`/`IntermediateRegionNode` don't exist yet, and `CountryOrArea.RegionAncestry` doesn't exist yet. (The project also won't build at all right now since Step 1 deleted the migration files — that's expected; this step is about confirming the *new* test's own compile errors are the ones you expect once the rest catches up.)

- [ ] **Step 4: Write the three owned-JSON types**

```csharp
// Mimisbrunnr/src/ReferenceData.Data/RegionNode.cs
namespace Norse.ReferenceData.Data;

/// <summary>
/// The Region ancestor of a <see cref="CountryOrArea.RegionAncestry"/> graph — an owned JSON document,
/// never a separately-queried table or view. Hydrated by the seed contributor at seed time.
/// </summary>
public sealed class RegionNode
{
	/// <summary>The Region's UN M49 code.</summary>
	public string Code { get; init; } = null!;
	/// <summary>The Region's name.</summary>
	public string Name { get; init; } = null!;
	/// <summary>The Subregion beneath this Region, if the leaf country resolved through one.</summary>
	public SubregionNode? Subregion { get; init; }
}
```

```csharp
// Mimisbrunnr/src/ReferenceData.Data/SubregionNode.cs
namespace Norse.ReferenceData.Data;

/// <summary>The Subregion ancestor nested within a <see cref="RegionNode"/>.</summary>
public sealed class SubregionNode
{
	/// <summary>The Subregion's UN M49 code.</summary>
	public string Code { get; init; } = null!;
	/// <summary>The Subregion's name.</summary>
	public string Name { get; init; } = null!;
	/// <summary>The Intermediate Region beneath this Subregion, if one exists.</summary>
	public IntermediateRegionNode? IntermediateRegion { get; init; }
}
```

```csharp
// Mimisbrunnr/src/ReferenceData.Data/IntermediateRegionNode.cs
namespace Norse.ReferenceData.Data;

/// <summary>The Intermediate Region ancestor nested within a <see cref="SubregionNode"/>.</summary>
public sealed class IntermediateRegionNode
{
	/// <summary>The Intermediate Region's UN M49 code.</summary>
	public string Code { get; init; } = null!;
	/// <summary>The Intermediate Region's name.</summary>
	public string Name { get; init; } = null!;
}
```

- [ ] **Step 5: Add `RegionAncestry` to `CountryOrArea` and configure the owned-JSON mapping**

Replace the whole file:

```csharp
// Mimisbrunnr/src/ReferenceData.Data/CountryOrArea.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.EntityFramework;

namespace Norse.ReferenceData.Data;

/// <summary>
/// A country or area per UN M49 with ISO and LDC classifications.
/// </summary>
public sealed class CountryOrArea : NorseEntityBase<CountryOrArea>, INorseEntity<CountryOrArea>
{
	/// <summary>The country-or-area identifier.</summary>
	public Guid Id { get; init; }
	/// <summary>The UN M49 code (3 digits).</summary>
	public string M49Code { get; init; } = null!;
	/// <summary>The ISO 3166-1 alpha-2 code (2 letters).</summary>
	public string IsoAlpha2Code { get; init; } = null!;
	/// <summary>The ISO 3166-1 alpha-3 code (3 letters).</summary>
	public string IsoAlpha3Code { get; init; } = null!;
	/// <summary>The country or area name in English.</summary>
	public string Name { get; init; } = null!;
	/// <summary>The parent region identifier, if applicable.</summary>
	public Guid? ParentRegionId { get; init; }
	/// <summary>The parent region, if applicable.</summary>
	public Region ParentRegion { get; init; } = null!;
	/// <summary>True if this is a Least Developed Country per UN classification.</summary>
	public bool IsLeastDevelopedCountry { get; init; }
	/// <summary>True if this is a Land Locked Developing Country per UN classification.</summary>
	public bool IsLandLockedDevelopingCountry { get; init; }
	/// <summary>True if this is a Small Island Developing State per UN classification.</summary>
	public bool IsSmallIslandDevelopingState { get; init; }
	/// <summary>
	/// The ancestor Region/Subregion/IntermediateRegion chain, hydrated by the seed contributor and
	/// stored as an owned JSON document — <see langword="null"/> only for Antarctica.
	/// </summary>
	public RegionNode? RegionAncestry { get; init; }

	/// <summary>Configures the EF entity mapping.</summary>
	public static void Configure(EntityTypeBuilder<CountryOrArea> builder)
	{
		builder.ToTable("country_or_areas");
		builder.HasKey(c => c.Id);
		builder.Property(c => c.M49Code).HasMaxLength(3).IsRequired();
		builder.Property(c => c.IsoAlpha2Code).HasMaxLength(2).IsRequired();
		builder.Property(c => c.IsoAlpha3Code).HasMaxLength(3).IsRequired();
		builder.Property(c => c.Name).HasMaxLength(256).IsRequired();
		builder.HasIndex(c => c.M49Code).IsUnique().HasDatabaseName("uq_country_or_areas_m49_code");
		builder.HasIndex(c => c.IsoAlpha2Code).IsUnique().HasDatabaseName("uq_country_or_areas_iso_alpha2_code");
		builder.HasIndex(c => c.IsoAlpha3Code).IsUnique().HasDatabaseName("uq_country_or_areas_iso_alpha3_code");
		builder.HasOne(c => c.ParentRegion).WithMany().HasForeignKey(c => c.ParentRegionId).IsRequired(false);
		builder.OwnsOne(c => c.RegionAncestry, region =>
		{
			region.ToJson();
			region.OwnsOne(r => r.Subregion, sub => sub.OwnsOne(s => s.IntermediateRegion));
		});
	}
}
```

- [ ] **Step 6: Regenerate the squashed migration**

```bash
cd Mimisbrunnr/src/ReferenceData.Data.Migrations
dotnet ef migrations add InitialCreate --project . --startup-project .
```
Expected: creates `Migrations/{timestamp}_InitialCreate.cs`, `.Designer.cs`, and `ReferenceDataDbContextModelSnapshot.cs`. Inspect the generated `Up()` — confirm it's pure `CreateTable`/`CreateIndex` calls with **no `migrationBuilder.Sql(...)` anywhere**, and that `country_or_areas`'s `CreateTable` columns include a JSON-typed column for the region ancestry (Npgsql maps `.ToJson()`-configured owned types to a `jsonb` column — confirm the column exists; the exact generated name doesn't need to match anything specific, just note it for Task 2).

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet build Mimisbrunnr/src/ReferenceData.Data.Migrations/ReferenceData.Data.Migrations.csproj
dotnet test Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceData.Data.Tests.csproj
```
Expected: build succeeds with **zero** `migrationBuilder.Sql` in the diff, and all tests pass, including the three new `CountryOrAreaRegionAncestryTests`. Run the test command a second time to confirm no state leaked (matches this repo's established verification habit).

- [ ] **Step 8: Commit**

```bash
git -C Mimisbrunnr add -A src/ReferenceData.Data src/ReferenceData.Data.Migrations/Migrations tests/ReferenceData.Data.Tests
git -C Mimisbrunnr commit -m "feat: replace country_or_area_dossier view with CountryOrArea.RegionAncestry owned-JSON column"
```

---

### Task 2: Hydrate `RegionAncestry` during seeding

**Files:**
- Modify: `Mimisbrunnr/src/ReferenceData.Data.Migrations/ReferenceDataSeedContributor.cs`
- Modify: `Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceDataSeedContributorTests.cs`

**Interfaces:**
- Consumes: `RegionNode`/`SubregionNode`/`IntermediateRegionNode` (Task 1), `CountryOrArea.RegionAncestry` (Task 1).
- Produces: no new public surface — `ReferenceDataSeedContributor.SeedAsync` now also populates `RegionAncestry` on every seeded `CountryOrArea`; Task 3's live verification reads this via `psql`.

- [ ] **Step 1: Write the failing test**

Add this test to the existing file (after `Reseeding_from_scratch_produces_byte_identical_ids`):

```csharp
	[Fact]
	public async Task SeedAsync_hydrates_RegionAncestry_for_all_three_verified_shapes()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		using var context = await MigratedContextAsync(fixture.ConnectionString, cancellationToken);

		try
		{
			await new ReferenceDataSeedContributor(context).SeedAsync(cancellationToken);
			context.ChangeTracker.Clear();

			var nigeria = await context.Set<CountryOrArea>().SingleAsync(c => c.M49Code == "566", cancellationToken);
			nigeria.RegionAncestry.ShouldNotBeNull();
			nigeria.RegionAncestry.Code.ShouldBe("002");
			nigeria.RegionAncestry.Subregion.ShouldNotBeNull();
			nigeria.RegionAncestry.Subregion.IntermediateRegion.ShouldNotBeNull();
			nigeria.RegionAncestry.Subregion.IntermediateRegion.Code.ShouldBe("011");

			var algeria = await context.Set<CountryOrArea>().SingleAsync(c => c.M49Code == "012", cancellationToken);
			algeria.RegionAncestry.ShouldNotBeNull();
			algeria.RegionAncestry.Subregion.ShouldNotBeNull();
			algeria.RegionAncestry.Subregion.IntermediateRegion.ShouldBeNull();

			var antarctica = await context.Set<CountryOrArea>().SingleAsync(c => c.M49Code == "010", cancellationToken);
			antarctica.RegionAncestry.ShouldBeNull();
		}
		finally
		{
			await context.Set<CountryOrArea>().ExecuteDeleteAsync(cancellationToken);
			await context.Set<Region>().ExecuteDeleteAsync(cancellationToken);
		}
	}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceData.Data.Tests.csproj
```
Expected: FAIL — `nigeria.RegionAncestry` is `null` because the seed contributor doesn't hydrate it yet.

- [ ] **Step 3: Rewrite the seed contributor to build and set `RegionAncestry`**

Replace the whole file:

```csharp
// Mimisbrunnr/src/ReferenceData.Data.Migrations/ReferenceDataSeedContributor.cs
using Microsoft.EntityFrameworkCore;
using Norse.Abstractions.Migrations.Seeding;
using Norse.Primitives.Identifiers;
using Norse.Primitives.Ingestion;

namespace Norse.ReferenceData.Data.Migrations;

/// <summary>
/// Seeds <see cref="Region"/> and <see cref="CountryOrArea"/> rows from the committed UN M49 TSVs
/// (<c>seeds/region.tsv</c>, <c>seeds/country-or-area.tsv</c>), idempotently, and hydrates each
/// <see cref="CountryOrArea.RegionAncestry"/> from the same region rows.
/// </summary>
/// <param name="context">The reference-data context instance resolved from DI.</param>
public sealed class ReferenceDataSeedContributor(ReferenceDataDbContext context) : ISeedContributor
{
	static readonly Guid _namespaceRegion =
		new DeterministicGuid(DeterministicGuid.Namespaces.Dns, "region.m49.referencedata.norse");

	static readonly Guid _namespaceCountryOrArea =
		new DeterministicGuid(DeterministicGuid.Namespaces.Dns, "country-or-area.m49.referencedata.norse");

	/// <inheritdoc />
	public string Name => "Norse.ReferenceData";

	/// <inheritdoc />
	public async Task SeedAsync(CancellationToken cancellationToken)
	{
		var regionsByCode = await SeedRegionsAsync(cancellationToken).ConfigureAwait(false);
		await SeedCountriesAsync(regionsByCode, cancellationToken).ConfigureAwait(false);
	}

	async Task<Dictionary<string, RegionRow>> SeedRegionsAsync(CancellationToken cancellationToken)
	{
		Dictionary<string, RegionRow> regionsByCode = [];

		using ITabularReader reader = TabularReader.OpenDelimited(
			Path.Combine(AppContext.BaseDirectory, "seeds", "region.tsv"), '\t');
		var m49Ordinal = reader.Ordinal("M49Code");
		var nameOrdinal = reader.Ordinal("Name");
		var levelOrdinal = reader.Ordinal("Level");
		var parentOrdinal = reader.Ordinal("ParentM49Code");

		while (reader.Read())
		{
			var m49Code = reader[m49Ordinal].ToString();
			// The TSV's Level column holds the enum member name (Region/Subregion/IntermediateRegion),
			// not a numeric value — written that way by tools/SeedTool's UnsdM49Writer.
			var level = Enum.Parse<RegionLevel>(reader[levelOrdinal]);
			var parentCode = reader[parentOrdinal].ToString();
			var id = new DeterministicGuid(_namespaceRegion, m49Code);

			regionsByCode[m49Code] = new RegionRow(id, m49Code, reader[nameOrdinal].ToString(), level, parentCode.Length == 0 ? null : parentCode);
		}

		var existingIds = (await context.Set<Region>().Select(r => r.Id).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();

		foreach (var row in regionsByCode.Values)
		{
			if (existingIds.Contains(row.Id))
				continue;

			context.Set<Region>().Add(new Region
			{
				Id = row.Id,
				M49Code = row.M49Code,
				Name = row.Name,
				Level = row.Level,
				ParentRegionId = row.ParentM49Code is null ? null : regionsByCode[row.ParentM49Code].Id,
			});
		}

		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return regionsByCode;
	}

	async Task SeedCountriesAsync(Dictionary<string, RegionRow> regionsByCode, CancellationToken cancellationToken)
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

		List<(Guid Id, string M49Code, string Alpha2Code, string Alpha3Code, string Name, string? ParentM49Code, bool IsLeastDevelopedCountry, bool IsLandLockedDevelopingCountry, bool IsSmallIslandDevelopingState)> rows = [];

		while (reader.Read())
		{
			var m49Code = reader[m49Ordinal].ToString();
			var id = new DeterministicGuid(_namespaceCountryOrArea, m49Code);
			var parentCode = reader[parentOrdinal].ToString();

			rows.Add((
				id,
				m49Code,
				reader[alpha2Ordinal].ToString(),
				reader[alpha3Ordinal].ToString(),
				reader[nameOrdinal].ToString(),
				parentCode.Length == 0 ? null : parentCode,
				// The TSV's flag columns hold literal "true"/"false" (written by UnsdM49Writer's
				// FormatFlag), not the "x"/blank convention of the raw UNSD source CSV.
				bool.Parse(reader[ldcOrdinal]),
				bool.Parse(reader[lldcOrdinal]),
				bool.Parse(reader[sidsOrdinal])));
		}

		var existingIds = (await context.Set<CountryOrArea>().Select(c => c.Id).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();

		foreach (var row in rows)
		{
			if (existingIds.Contains(row.Id))
				continue;

			context.Set<CountryOrArea>().Add(new CountryOrArea
			{
				Id = row.Id,
				M49Code = row.M49Code,
				IsoAlpha2Code = row.Alpha2Code,
				IsoAlpha3Code = row.Alpha3Code,
				Name = row.Name,
				ParentRegionId = row.ParentM49Code is null ? null : regionsByCode[row.ParentM49Code].Id,
				RegionAncestry = BuildRegionAncestry(row.ParentM49Code, regionsByCode),
				IsLeastDevelopedCountry = row.IsLeastDevelopedCountry,
				IsLandLockedDevelopingCountry = row.IsLandLockedDevelopingCountry,
				IsSmallIslandDevelopingState = row.IsSmallIslandDevelopingState,
			});
		}

		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Walks <paramref name="leafCode"/> up through <paramref name="regionsByCode"/> via each row's own
	/// <c>ParentM49Code</c>, then re-nests the chain from the root down (Region contains Subregion
	/// contains IntermediateRegion), classifying each ancestor by its own <see cref="RegionLevel"/>
	/// rather than assuming a fixed position — a country's direct parent may be a Subregion or an
	/// IntermediateRegion, never a bare positional offset.
	/// </summary>
	static RegionNode? BuildRegionAncestry(string? leafCode, Dictionary<string, RegionRow> regionsByCode)
	{
		if (leafCode is null)
			return null;

		List<RegionRow> chain = [];
		for (var code = leafCode; code is not null; code = regionsByCode[code].ParentM49Code)
			chain.Add(regionsByCode[code]);

		var intermediateRow = chain.SingleOrDefault(r => r.Level == RegionLevel.IntermediateRegion);
		var subregionRow = chain.SingleOrDefault(r => r.Level == RegionLevel.Subregion);
		var regionRow = chain.Single(r => r.Level == RegionLevel.Region);

		var intermediate = intermediateRow is null
			? null
			: new IntermediateRegionNode { Code = intermediateRow.M49Code, Name = intermediateRow.Name };

		var subregion = subregionRow is null
			? null
			: new SubregionNode { Code = subregionRow.M49Code, Name = subregionRow.Name, IntermediateRegion = intermediate };

		return new RegionNode { Code = regionRow.M49Code, Name = regionRow.Name, Subregion = subregion };
	}

	sealed record RegionRow(Guid Id, string M49Code, string Name, RegionLevel Level, string? ParentM49Code);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test Mimisbrunnr/tests/ReferenceData.Data.Tests/ReferenceData.Data.Tests.csproj
```
Expected: PASS, including `SeedAsync_hydrates_RegionAncestry_for_all_three_verified_shapes`. Run it a second time to confirm identical results (isolation still holds).

- [ ] **Step 5: Commit**

```bash
git -C Mimisbrunnr add src/ReferenceData.Data.Migrations/ReferenceDataSeedContributor.cs tests/ReferenceData.Data.Tests/ReferenceDataSeedContributorTests.cs
git -C Mimisbrunnr commit -m "feat: hydrate CountryOrArea.RegionAncestry during seeding"
```

---

### Task 3: Live end-to-end re-verification

**Files:** none (verification only).

- [ ] **Step 1: Drop the existing database so it rebuilds from scratch**

```bash
docker exec -e PGPASSWORD=devpassword pg-primary-69160315 psql -U postgres -c "DROP DATABASE norse_referencedata;"
```
(Container name may differ if Docker was restarted since the last session — check with `docker ps --format '{{.Names}}'` and substitute.)

- [ ] **Step 2: Run the AppHost**

```bash
cd Bifrost
dotnet run --project src/Orchestration.AppHost
```
Wait for the migrations service to run to completion (it self-terminates; the AppHost dashboard keeps running).

- [ ] **Step 3: Confirm row counts and no leftover `country_or_area_dossier` view**

```bash
docker exec -e PGPASSWORD=devpassword pg-primary-69160315 psql -U postgres -d norse_referencedata -c "\dv"
docker exec -e PGPASSWORD=devpassword pg-primary-69160315 psql -U postgres -d norse_referencedata -c "SELECT count(*) FROM regions;"
docker exec -e PGPASSWORD=devpassword pg-primary-69160315 psql -U postgres -d norse_referencedata -c "SELECT count(*) FROM country_or_areas;"
```
Expected: `\dv` (list views) returns **no rows** — confirms the view is genuinely gone, not just unused. Region/country counts match the original plan's Task 7 results (29 regions, 248 countries).

- [ ] **Step 4: Confirm the three `RegionAncestry` shapes directly against the jsonb column**

```bash
docker exec -e PGPASSWORD=devpassword pg-primary-69160315 psql -U postgres -d norse_referencedata -c "SELECT m49code, region_ancestry FROM country_or_areas WHERE m49code = '566';"
docker exec -e PGPASSWORD=devpassword pg-primary-69160315 psql -U postgres -d norse_referencedata -c "SELECT m49code, region_ancestry FROM country_or_areas WHERE m49code = '012';"
docker exec -e PGPASSWORD=devpassword pg-primary-69160315 psql -U postgres -d norse_referencedata -c "SELECT m49code, region_ancestry FROM country_or_areas WHERE m49code = '010';"
```
(Column name may differ slightly from `region_ancestry` depending on what Npgsql's naming convention actually produced in Task 1 Step 6 — use whatever `\d country_or_areas` shows if this exact name 404s.) Expected: Nigeria shows the full 3-level nested JSON, Algeria shows `"intermediateRegion": null`, Antarctica shows `region_ancestry` as SQL `NULL` (the whole column, not a nested key).

- [ ] **Step 5: Confirm idempotency on a second run**

```bash
cd Bifrost
dotnet run --project src/Orchestration.AppHost
```
(Against the already-seeded database from Step 2 — do not drop it again.) Re-run the same count queries from Step 3 and confirm identical numbers.

- [ ] **Step 6: Report back**

Summarize the view's absence, the row counts, and the three `region_ancestry` JSON payloads for confirmation.

---

## Self-Review

**Spec coverage:** §5's owned-JSON design → Task 1. §5's "hydrated in C# by the seed contributor" → Task 2. §7's success criteria (row counts, idempotency, three verified shapes, reproducible GUIDs) → Task 3 plus the tests in Tasks 1–2. §6's exclusion of the CQRS command-side idea → not touched anywhere in this plan.

**Placeholder scan:** none — every step has concrete code, exact paths, and runnable commands.

**Type consistency:** `RegionNode`/`SubregionNode`/`IntermediateRegionNode` (Task 1) are constructed with the exact same property names in Task 2's `BuildRegionAncestry`. `RegionRow` (Task 2, private to the seed contributor) is distinct from the public `RegionNode` (Task 1) — the former is seeding-time scratch data, the latter is the persisted JSON shape; they are never confused in the code above.
