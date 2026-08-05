# Temporal Tables Persistence Chassis — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Pairs with superpowers:test-driven-development on every coding task, per standing law (Glitnir CLAUDE.md §2.8).

**Goal:** Make Model B system-time history a capability of `Norse.Persistence.EntityFramework.*` — mark an entity `ITemporalEntity`, scaffold a migration, and the full PostgreSQL history apparatus exists and evolves with the schema.

**Architecture:** A provider-neutral marker interface + finalizing convention stamps a `Norse:Temporal` annotation on each temporal entity's main table only. The `.PostgreSQL` package derives Npgsql's migrations SQL generator (plus an annotation provider) to emit and evolve the Norns §7.3 apparatus (system_period, history table with `WITHOUT OVERLAPS` PK, hardened SECURITY DEFINER triggers, timeline view). The `.SqlServer` package translates the same marker to EF-native `IsTemporal()` for non-split entities and parks split entities behind an explicit fluent declaration.

**Tech Stack:** .NET 11 preview, C# 15, EF Core 11 preview (`Npgsql.EntityFrameworkCore.PostgreSQL 11.*-*`), xUnit v3 on MTP v2, Shouldly, Testcontainers.PostgreSql (`postgres:19beta2`).

**Spec:** `../specs/2026-08-04-temporal-tables-persistence-chassis-design.md` (all §-references below are to this spec unless marked "Norns").

## Global Constraints

- **No automatic git commits — platform law overrides the skill template's commit steps.** Every task ends by staging the diff and stopping for human review. Never `git commit`.
- **PG19 floor** for the temporal apparatus, asserted in-migration via `server_version_num >= 190000` (spec ruling 2); container image is `postgres:19beta2` everywhere (matching `Bifrost/src/Orchestration.AppHost/AppHost.cs`).
- **The temporal clock is `clock_timestamp()`; `now()` appears nowhere in the apparatus** (spec §3.2).
- **Tabs for indentation** in all C#; BOM-free UTF-8, LF-only.
- **`sealed` by default**; `internal` is the project default — omit modifiers when adopting the language default; tests reach internals via the existing `InternalsVisibleTo` (`$(AssemblyName).Tests`).
- House style per `../../house-rules.md`: target-typed `new()`, collection expressions, primary constructors, expression bodies (arrow on declaration line), `is null`/`is not null`, fluent chains dot-leading, `ConfigureAwait(false)` in src (never tests), every async method takes `CancellationToken cancellationToken = default` last.
- **Tests:** Shouldly + NSubstitute globals already imported via `Directory.Test.props` — no per-file usings for them. Test classes `public sealed`; test methods bare `void`/`async Task` (no accessibility modifier), sentence-shaped names with underscores.
- **Urðarbrunnr branch law:** one feature fork per realm — if Urðarbrunnr has an open feature branch, land these commits on it; otherwise create `feature/temporal-chassis` from `master`. Never branch Bifröst; Glitnir work (Tasks 1–2) lands on Glitnir `master`.
- Realm paths below are workspace-relative from the Bifröst root (`Urdarbrunnr/…`, `Glitnir/…`).

## Plan boundary

Tasks 1–9 are unblocked and complete the chassis with its own real-Postgres proof. Tasks 10–11 are **gated** (.NET 11 preview 7 ~2026-08-11 + Himinbjorg#47's operational-noise split landing) and are deliberately coarser — preview-7 reality (the split-table FK-emission bug recorded in #47) may force revision; re-plan them at gate-open if so. Issue Urdarbrunnr#52's exit criteria span both halves.

## File Structure

```
Glitnir/
	poc/pg19-temporal/                        (Task 1: image bump + re-run; FINDINGS.md update)
	poc/ef-temporal-emission/                 (Task 2: NEW spike — seam verdict; FINDINGS.md)
	docs/Platform/specs/2026-08-04-temporal-tables-persistence-chassis-design.md  (Task 2 amends §3.0)
Urdarbrunnr/
	src/Persistence.EntityFramework/
		ITemporalEntity.cs                    (Task 3: NEW marker)
		NorseAnnotationNames.cs               (Task 3: NEW annotation name constants)
		TemporalEntityConvention.cs           (Task 3: NEW validate + stamp + name reservation)
		TemporalEntityTypeBuilderExtensions.cs (Task 3: NEW TemporalParkedOnSqlServer fluent)
		NorseModelConventions.cs              (Task 3: MODIFY — register the convention)
		INorseEfProvider.cs                   (Task 4: MODIFY — add TemporalRealizationHook seam)
		NorseDbContextOptionsExtensions.cs    (Task 4: MODIFY — plugin registration when hook non-null)
	src/Persistence.EntityFramework.SqlServer/
		NorseSqlServerEfProvider.cs           (Task 4: MODIFY — realization hook: IsTemporal + naming + split guard)
	src/Persistence.EntityFramework.PostgreSQL/
		NorsePostgresEfProvider.cs            (Task 5: MODIFY — ReplaceService registrations)
		NorseNpgsqlAnnotationProvider.cs      (Task 5: NEW — surface Norse:Temporal onto operations)
		NorseNpgsqlMigrationsSqlGenerator.cs  (Tasks 5–7: NEW — apparatus emission + transitions + evolution)
		TemporalSqlEmitter.cs                 (Tasks 5–7: NEW — DDL raw-string templates)
	tests/Persistence.EntityFramework.Tests/           (Task 3: convention tests)
	tests/Persistence.EntityFramework.SqlServer.Tests/ (Task 4: realization tests)
	tests/Persistence.EntityFramework.PostgreSQL.Tests/ (Tasks 5–7: snapshot + order tests, NEW PostgresContainerFixture + per-shape live tests in Task 7; Task 8: semantics integration suite)
Himinbjorg/ (Task 10, GATED)   Bifrost/ (Task 11, GATED)
```

---

### Task 1: POC beta2 re-run (`pg19-temporal`)

**Files:**
- Modify: `Glitnir/poc/pg19-temporal/run.ps1` (image tag), `Glitnir/poc/pg19-temporal/docker-compose.yml` (if it pins the image)
- Modify: `Glitnir/poc/pg19-temporal/FINDINGS.md`

**Interfaces:** none — standalone empirical task; its output is the updated FINDINGS.md.

- [ ] **Step 1:** In `run.ps1` (and `docker-compose.yml` if the tag appears there), replace the `postgres:19beta1-trixie` image reference with the current official 19beta2 tag (check `docker pull postgres:19beta2` resolves; use the `-trixie` suffixed tag if that is what docker-library publishes, matching the beta1 convention).
- [ ] **Step 2:** Run `./run.ps1` from `Glitnir/poc/pg19-temporal/`. All five scripts must complete; outputs land in `results/`.
- [ ] **Step 3:** Diff `results/*.out` against the beta1 run (`git diff`). Expected: timings/toolchain lines move; every error/notice/trigger/rowcount line either matches beta1 or reveals a verdict movement.
- [ ] **Step 4:** Update `FINDINGS.md`: header block (image, date, "re-verified at beta2"); if verdicts 1 or 4 moved, rewrite the affected verdict rows and the implications section; if nothing moved, add one line recording beta2 parity. The RC1 re-verify gate stays in place either way.
- [ ] **Step 5:** Stage the diff (`git add` in Glitnir) and stop for review.

---

### Task 2: Emission-seam spike (`ef-temporal-emission`) — DESIGN GATE

**Files:**
- Create: `Glitnir/poc/ef-temporal-emission/` — a minimal console/test project + `FINDINGS.md` + `README.md`
- Modify: `Glitnir/docs/Platform/specs/2026-08-04-temporal-tables-persistence-chassis-design.md` §3.0 (record the verdict)

**Interfaces:**
- Produces: the named EF seam (annotation provider, target-model consultation in `Generate`, or both) that Tasks 5–7 bind to; and the answer to whether marker-add/marker-remove annotation transitions surface as usable `AlterTableOperation`s.

The spike answers seven questions, one per scaffold shape (spec §3.0): create, add-column, rename-column, drop-column, alter-column, marker-added-to-existing-table, marker-removed. For each: does the generated operation carry (or can it reach) the temporal identity of its table?

- [ ] **Step 1:** Scaffold the project: `dotnet new console` under `Glitnir/poc/ef-temporal-emission/src/Spike/`, referencing `Npgsql.EntityFrameworkCore.PostgreSQL` `11.*-*` and `Microsoft.EntityFrameworkCore.Design` `11.*-*`. Define one entity + context pair *without* any Urðarbrunnr dependency (the spike isolates EF seams, not the chassis):

```csharp
public sealed record Widget
{
	public required Guid Id { get; init; }
	public required string Name { get; init; }
}

public sealed class SpikeContext : DbContext
{
	protected override void OnModelCreating(ModelBuilder modelBuilder) =>
		modelBuilder.Entity<Widget>(eb =>
		{
			eb.HasKey(w => w.Id);
			eb.Property(w => w.Name).HasMaxLength(64);
			eb.ToTable("widgets", tb => tb.HasAnnotation("Norse:Temporal", true));
		});
	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
		optionsBuilder
			.UseNpgsql("Host=localhost;Port=54329;Database=spike;Username=postgres;Password=spike")
			.ReplaceService<IMigrationsSqlGenerator, SpikeMigrationsSqlGenerator>();
}
```

- [ ] **Step 2:** Write `SpikeMigrationsSqlGenerator : NpgsqlMigrationsSqlGenerator` that overrides `Generate` for `CreateTableOperation`, `AddColumnOperation`, `RenameColumnOperation`, `DropColumnOperation`, `AlterColumnOperation`, `AlterTableOperation`, and `DropTableOperation`, and for each logs (to a file the run inspects): operation type, table, every annotation on the operation (`operation.GetAnnotations()`), old-annotations where the operation carries them (`AlterTableOperation.OldTable`), and whether the target `IModel` can resolve the table to a temporal entity (`model?.GetRelationalModel().FindTable(...)`).
- [ ] **Step 3:** Script the seven shapes (a `run.ps1` beside the project): for each shape, mutate the model source (the script switches between checked-in model variants — `Model.Create.cs`, `Model.AddColumn.cs`, … as compile-included files), run `dotnet ef migrations add Shape_<n>`, capture the scaffolded migration source and the logged operation report. Shapes 6 and 7 flip the `HasAnnotation("Norse:Temporal", true)` line on an otherwise-unchanged table — the critical question is whether EF's differ emits an `AlterTableOperation` for an annotation-only change and what it carries.
- [ ] **Step 4:** Apply each scaffolded migration to real PG (`docker compose up` a `postgres:19beta2` on port 54329) with a hand-written apparatus emission for the create shape only — enough to prove the override point receives control and its emitted SQL executes; full DDL fidelity is Tasks 5–7's job, not the spike's.
- [ ] **Step 5:** Also probe the `IRelationalAnnotationProvider` path: derive `NpgsqlAnnotationProvider`, override `For(ITable table, bool designTime)` to append the `Norse:Temporal` annotation, `ReplaceService` it, and record whether the annotation then arrives on scaffolded operations without the model-consultation fallback — including on the drop side (shape 7), where the entity is absent from the target model.
- [ ] **Step 6:** Write `FINDINGS.md`: a verdict table (shape × seam × works/fails), the named seam Tasks 5–7 must use, and any surprises (differ suppressing annotation-only diffs would be the catastrophic one — if so, the fallback to probe is a custom `IMigrationsModelDiffer`; record it as a verdict, do not silently implement it).
- [ ] **Step 7:** Amend spec §3.0 with the verdict (one paragraph: "Spike verdict (date): the seam is X; shapes verified: …"). Stage both repos' diffs and stop.
- [ ] **Step 8: CHECKPOINT — human gate.** Buvy reviews the verdict. If the seam differs from the working hypothesis written into Tasks 5–7 (annotation provider + model consultation), revise those tasks before dispatching them. If no seam supports all seven shapes, STOP — the emission approach returns to the court (spec §3.0).

---

### Task 3: `ITemporalEntity` marker, convention, and fluent park (provider-neutral)

**Files:**
- Create: `Urdarbrunnr/src/Persistence.EntityFramework/ITemporalEntity.cs`
- Create: `Urdarbrunnr/src/Persistence.EntityFramework/NorseAnnotationNames.cs`
- Create: `Urdarbrunnr/src/Persistence.EntityFramework/TemporalEntityConvention.cs`
- Create: `Urdarbrunnr/src/Persistence.EntityFramework/TemporalEntityTypeBuilderExtensions.cs`
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework/NorseModelConventions.cs` (register the convention beside `RequireExplicitLengthConvention`)
- Test: `Urdarbrunnr/tests/Persistence.EntityFramework.Tests/TemporalEntityConventionTests.cs`

**Interfaces:**
- Consumes: `INorseEntity<TSelf>` / `NorseEntityBase<TSelf>` (existing), `NorseModelConventions.Apply` registration seam (existing).
- Produces: `public interface ITemporalEntity;` (namespace `Norse.Persistence.EntityFramework`); `internal static class NorseAnnotationNames` with `public const string Temporal = "Norse:Temporal"` and `public const string TemporalParkedOnSqlServer = "Norse:TemporalParkedOnSqlServer"`; fluent `TemporalParkedOnSqlServer()` on `EntityTypeBuilder<TEntity>`. Tasks 4–7 consume all three.

- [ ] **Step 1: Write the failing tests.** Model-level, SQLite `:memory:` + `ApplyNorseConventions`, copying the arrange pattern from `NorseSnakeCaseNamingConventionTests.cs` in the same test project (build a context, inspect `context.Model`). Test entities live in the test file: `TemporalWidget` (marked, PK, `Name` with `[MaxLength(64)]`), `PlainWidget` (unmarked), `KeylessTemporal` (marked, `HasNoKey()`), and a collision pair (`Clash` marked with table `clash`; `ClashHistory` mapped to table `clash_history`).

```csharp
public sealed class TemporalEntityConventionTests
{
	[Fact]
	void Stamps_the_temporal_annotation_on_a_marked_entity()
	{
		using var context = TestContext.Create<TemporalWidget>();
		var entity = context.Model.FindEntityType(typeof(TemporalWidget))!;
		entity.FindAnnotation(NorseAnnotationNames.Temporal)!.Value.ShouldBe(true);
	}

	[Fact]
	void Leaves_unmarked_entities_unstamped()
	{
		using var context = TestContext.Create<PlainWidget>();
		context.Model.FindEntityType(typeof(PlainWidget))!
			.FindAnnotation(NorseAnnotationNames.Temporal).ShouldBeNull();
	}

	[Fact]
	void Throws_at_model_finalize_when_a_marked_entity_has_no_primary_key()
	{
		var act = () => TestContext.Create<KeylessTemporal>().Model;
		act.ShouldThrow<InvalidOperationException>().Message.ShouldContain("primary key");
	}

	[Fact]
	void Throws_at_model_finalize_when_a_table_claims_a_derived_history_name()
	{
		var act = () => TestContext.CreateWithClashPair().Model;
		act.ShouldThrow<InvalidOperationException>().Message.ShouldContain("clash_history");
	}

	[Fact]
	void The_park_fluent_stamps_the_park_annotation()
	{
		using var context = TestContext.CreateParked<TemporalWidget>();
		context.Model.FindEntityType(typeof(TemporalWidget))!
			.FindAnnotation(NorseAnnotationNames.TemporalParkedOnSqlServer)!.Value.ShouldBe(true);
	}
}
```

- [ ] **Step 2:** Run `dotnet test Urdarbrunnr/tests/Persistence.EntityFramework.Tests` — the new tests FAIL (types not defined).
- [ ] **Step 3: Implement.** The marker (doc comment per spec §2.1 — system-time temporality, main-table-only, split fragments deliberately non-temporal):

```csharp
namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Opts an entity into system-time temporality (Norns Model B): the entity's main table gets a
/// database-owned <c>system_period</c>, a history table, versioning triggers, and a timeline view
/// on PostgreSQL, and native system-versioning on SQL Server. Split-table fragments are
/// deliberately not temporal. The period never appears on the CLR type or any payload.
/// </summary>
public interface ITemporalEntity;
```

The convention (shape mirrors `RequireEntityConfigurationConvention` in the same project — follow its registration and throw style exactly):

```csharp
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Validates every <see cref="ITemporalEntity"/> at model finalize and stamps the
/// <see cref="NorseAnnotationNames.Temporal"/> annotation on its main table mapping.
/// Reserves the derived history/timeline names (fails loudly on collision).
/// </summary>
sealed class TemporalEntityConvention : IModelFinalizingConvention
{
	public void ProcessModelFinalizing(
		IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
	{
		var entityTypes = modelBuilder.Metadata.GetEntityTypes().ToList();
		var tableNames = entityTypes
			.Select(e => e.GetTableName())
			.Where(n => n is not null)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var entityType in entityTypes.Where(e => typeof(ITemporalEntity).IsAssignableFrom(e.ClrType)))
		{
			if (entityType.FindPrimaryKey() is null)
				throw new InvalidOperationException(
					$"Temporal entity '{entityType.DisplayName()}' has no primary key; ITemporalEntity requires one.");
			if (entityType.IsOwned() || entityType.GetContainerColumnName() is not null)
				throw new InvalidOperationException(
					$"Temporal entity '{entityType.DisplayName()}' is owned or JSON-mapped; ITemporalEntity applies to root table-mapped entities only.");
			var table = entityType.GetTableName()!;
			foreach (var derived in (string[])[$"{table}_history", $"{table}_timeline", $"{table}History"])
				if (tableNames.Contains(derived))
					throw new InvalidOperationException(
						$"Table '{derived}' collides with temporal entity '{entityType.DisplayName()}''s derived name; derived history/timeline names are reserved.");
			entityType.Builder.HasAnnotation(NorseAnnotationNames.Temporal, true);
		}
	}
}
```

The fluent park, as a C# 14 extension block:

```csharp
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework;

/// <summary>Temporal opt-outs declared per entity in its static Configure.</summary>
public static class TemporalEntityTypeBuilderExtensions
{
	extension<TEntity>(EntityTypeBuilder<TEntity> builder) where TEntity : class
	{
		/// <summary>
		/// Acknowledges that this split temporal entity is deliberately non-temporal on SQL Server
		/// until dotnet/efcore#26457 ships per-fragment temporal control. Deleted the day upstream moves.
		/// </summary>
		public EntityTypeBuilder<TEntity> TemporalParkedOnSqlServer() =>
			builder.HasAnnotation(NorseAnnotationNames.TemporalParkedOnSqlServer, true);
	}
}
```

Register `TemporalEntityConvention` in `NorseModelConventions.Apply` exactly where `RequireEntityConfigurationConvention` registers (same `conventions.ModelFinalizingConventions.Add(...)` shape — read the file and mirror it). Add the small `TestContext` helper in the test project (a builder over `DbContextOptionsBuilder` + SQLite `:memory:` + `ApplyNorseConventions`, one generic context class with a `Configure` callback — DRY across this file's tests).
- [ ] **Step 4:** Run the test project. Expected: all new tests PASS, all existing tests still PASS (the convention must not disturb unmarked models — the snake-case suite is the canary).
- [ ] **Step 5:** Stage the diff and stop for review.

---

### Task 4: SQL Server realization — `IsTemporal`, naming policy, split guard

**Files:**
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework/INorseEfProvider.cs` (new nullable hook member)
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework/NorseDbContextOptionsExtensions.cs` (register realization plugin when hook non-null)
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework.SqlServer/NorseSqlServerEfProvider.cs` (implement the hook)
- Test: `Urdarbrunnr/tests/Persistence.EntityFramework.SqlServer.Tests/SqlServerTemporalRealizationTests.cs`

**Interfaces:**
- Consumes: `NorseAnnotationNames.Temporal` / `.TemporalParkedOnSqlServer`, `ITemporalEntity` (Task 3); the existing options-extension → `IConventionSetPlugin` pattern (`NorseSnakeCaseNamingOptionsExtension` — mirror its shape).
- Produces: `INorseEfProvider.TemporalRealizationHook` — `Action<IConventionEntityType>? TemporalRealizationHook => null` default; SQL Server period columns `SystemPeriodStart`/`SystemPeriodEnd`, history table `{Table}History`.

The hook seam mirrors `EntityRenameHook`: the foundation declares it, a plugin-registered finalizing convention invokes it once per marked entity, and only the SqlServer package touches SQL-Server-only EF APIs. PostgreSQL's hook stays `null` (the annotation alone drives Tasks 5–7).

- [ ] **Step 1: Write the failing tests.** Model-level against `UseSqlServer` options (no database touched — same pattern the existing `NorseSqlServerEfProviderTests` uses for the rename hook). Entities in the test file: `TemporalOrder` (marked, no split), `SplitTemporalUser` (marked, `SplitToTable("user_lockout", …)` moving one column), `ParkedSplitTemporalUser` (same + `TemporalParkedOnSqlServer()` in its `Configure`).

```csharp
public sealed class SqlServerTemporalRealizationTests
{
	[Fact]
	void A_marked_unsplit_entity_becomes_native_temporal_with_the_norse_period_names()
	{
		using var context = SqlServerTestContext.Create<TemporalOrder>();
		var entity = context.Model.FindEntityType(typeof(TemporalOrder))!;
		entity.IsTemporal().ShouldBeTrue();
		entity.GetPeriodStartPropertyName().ShouldBe("SystemPeriodStart");
		entity.GetPeriodEndPropertyName().ShouldBe("SystemPeriodEnd");
		entity.GetHistoryTableName().ShouldBe("TemporalOrderHistory");
	}

	[Fact]
	void A_marked_split_entity_without_the_park_declaration_throws_at_model_finalize()
	{
		var act = () => SqlServerTestContext.Create<SplitTemporalUser>().Model;
		act.ShouldThrow<InvalidOperationException>().Message.ShouldContain("TemporalParkedOnSqlServer");
	}

	[Fact]
	void A_parked_split_entity_skips_temporality_on_sql_server_only()
	{
		using var context = SqlServerTestContext.Create<ParkedSplitTemporalUser>();
		context.Model.FindEntityType(typeof(ParkedSplitTemporalUser))!.IsTemporal().ShouldBeFalse();
	}
}
```

- [ ] **Step 2:** Run the SqlServer test project — new tests FAIL.
- [ ] **Step 3: Implement.** (a) Add to `INorseEfProvider`: `Action<IConventionEntityType>? TemporalRealizationHook => null;` (doc comment: invoked once per `Norse:Temporal`-stamped entity at model finalize; null when the provider realizes temporality elsewhere). (b) In `NorseDbContextOptionsExtensions.ApplyNorseProviderOptions`, when `provider.TemporalRealizationHook is not null`, register a `NorseTemporalRealizationOptionsExtension` → `IConventionSetPlugin` → finalizing convention that invokes the hook for every entity carrying `NorseAnnotationNames.Temporal` — copy the naming extension's three-class shape (`OptionsExtension` / `ConventionSetPlugin` / `Convention`) file-for-file. (c) In `NorseSqlServerEfProvider`, implement the hook:

```csharp
public Action<IConventionEntityType>? TemporalRealizationHook =>
	static entityType =>
	{
		var isSplit = entityType.GetMappingFragments().Any();
		var isParked = entityType.FindAnnotation(NorseAnnotationNames.TemporalParkedOnSqlServer) is { Value: true };
		if (isSplit && !isParked)
			throw new InvalidOperationException(
				$"Temporal entity '{entityType.DisplayName()}' uses table splitting; EF cannot scope SQL Server " +
				"temporality per fragment (dotnet/efcore#26457) and migration generation would fail (#30366). " +
				"Declare TemporalParkedOnSqlServer() in Configure to acknowledge the SQL-Server-only park, " +
				"or unsplit the entity.");
		if (isSplit)
			return;
		entityType.SetIsTemporal(true);
		entityType.SetPeriodStartPropertyName("SystemPeriodStart");
		entityType.SetPeriodEndPropertyName("SystemPeriodEnd");
		entityType.SetHistoryTableName($"{entityType.GetTableName()}History");
	};
```

(The message concatenation above is illustrative of content, not of style — build it as a single raw string literal; `+` between strings is banned.)
- [ ] **Step 4:** Run the SqlServer test project — new tests PASS; the existing rename-hook tests still PASS.
- [ ] **Step 5:** Run `dotnet test` across `Urdarbrunnr` (foundation tests must not regress — the hook defaults to null everywhere else).
- [ ] **Step 6:** Stage the diff and stop for review.

---

### Task 5: PostgreSQL create-path apparatus (annotation provider + SQL generator)

**Files:**
- Create: `Urdarbrunnr/src/Persistence.EntityFramework.PostgreSQL/NorseNpgsqlAnnotationProvider.cs`
- Create: `Urdarbrunnr/src/Persistence.EntityFramework.PostgreSQL/NorseNpgsqlMigrationsSqlGenerator.cs`
- Create: `Urdarbrunnr/src/Persistence.EntityFramework.PostgreSQL/TemporalSqlEmitter.cs`
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework.PostgreSQL/NorsePostgresEfProvider.cs` (`ReplaceService` both in `Configure`)
- Test: `Urdarbrunnr/tests/Persistence.EntityFramework.PostgreSQL.Tests/TemporalCreateTableSqlTests.cs`

**Interfaces:**
- Consumes: `NorseAnnotationNames.Temporal` (Task 3); **the Task 2 spike verdict** — the override points and annotation flow below are the working hypothesis; bind to whatever §3.0's recorded verdict names.
- Produces: `TemporalSqlEmitter` internal static methods consumed by Tasks 6–7: `FloorAssert()`, `BtreeGistGuard()`, `HistoryTable(schema, table, columns, pkColumns)`, `TriggerFunction(schema, table, columns, pkColumns)`, `Triggers(schema, table)`, `TimelineView(schema, table, columns)` — all returning complete SQL strings; column inputs are `IReadOnlyList<(string Name, string StoreType, bool IsNullable)>`.

- [ ] **Step 1: Write the failing snapshot tests.** No database: build the model (real `UseNpgsql` options + `ApplyNorseProviderOptions` against the placeholder connection string), produce create operations via `context.Database.GenerateCreateScript()`, and assert the script contains each apparatus element. One test per element keeps failures diagnosable:

```csharp
public sealed class TemporalCreateTableSqlTests
{
	static string Script<TEntity>() where TEntity : class =>
		PostgresTestContext.Create<TEntity>().Database.GenerateCreateScript();

	[Fact]
	void Emits_the_pg19_floor_assert() =>
		Script<TemporalWidget>().ShouldContain("server_version_num");

	[Fact]
	void Emits_the_btree_gist_guard_with_the_provisioning_diagnostic() =>
		Script<TemporalWidget>().ShouldContain("btree_gist");

	[Fact]
	void Adds_the_db_owned_system_period_with_the_clock_timestamp_default()
	{
		var script = Script<TemporalWidget>();
		script.ShouldContain("system_period tstzrange NOT NULL DEFAULT tstzrange(clock_timestamp(), 'infinity')");
		script.ShouldNotContain("now()");
	}

	[Fact]
	void Creates_the_history_table_with_the_without_overlaps_primary_key() =>
		Script<TemporalWidget>().ShouldContain("PRIMARY KEY (id, system_period WITHOUT OVERLAPS)");

	[Fact]
	void Creates_the_hardened_security_definer_trigger_function()
	{
		var script = Script<TemporalWidget>();
		script.ShouldContain("SECURITY DEFINER");
		script.ShouldContain("SET search_path = pg_catalog");
		script.ShouldContain("REVOKE EXECUTE");
		script.ShouldContain("greatest(pg_catalog.clock_timestamp()");
	}

	[Fact]
	void Creates_the_timeline_view() =>
		Script<TemporalWidget>().ShouldContain("_timeline");

	[Fact]
	void A_split_fragment_table_gets_no_apparatus()
	{
		var script = Script<SplitTemporalWidget>();
		script.ShouldNotContain("widget_counters_history");
		script.ShouldNotContain("ON split_temporal_widgets_counters");
	}

	[Fact]
	void An_unmarked_entity_gets_no_apparatus() =>
		Script<PlainWidget>().ShouldNotContain("system_period");
}
```

- [ ] **Step 2:** Run the PostgreSQL test project — new tests FAIL.
- [ ] **Step 3: Implement.** (a) `NorseNpgsqlAnnotationProvider : NpgsqlAnnotationProvider` — override `For(ITable table, bool designTime)`: yield base annotations, then `Norse:Temporal` when any of the table's mapped entity types carries it **and the table is the entity's root store object** (fragments excluded — compare against `StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)`). (b) `NorseNpgsqlMigrationsSqlGenerator : NpgsqlMigrationsSqlGenerator` (primary constructor forwarding both dependency parameters) — override `Generate(CreateTableOperation, IModel?, MigrationCommandListBuilder, bool)`: call base, then if the operation carries `Norse:Temporal`, append `TemporalSqlEmitter` blocks (floor assert and btree_gist guard once per migration — track emission with an instance flag; the generator is request-scoped per migration batch, verify at implementation and pin with a two-temporal-entities test). Column list and PK columns come from the operation itself (`operation.Columns`, `operation.PrimaryKey`). (c) `TemporalSqlEmitter` — raw string literal templates (`$$"""…"""`), one method per apparatus element, per spec §3.1–§3.2. The trigger function template in full:

```csharp
internal static string TriggerFunction(
	string schema, string table,
	IReadOnlyList<(string Name, string StoreType, bool IsNullable)> columns,
	IReadOnlyList<string> pkColumns)
{
	var columnList = string.Join(", ", columns.Select(c => $"\"{c.Name}\""));
	var oldColumnList = string.Join(", ", columns.Select(c => $"OLD.\"{c.Name}\""));
	var newColumnList = string.Join(", ", columns.Select(c => $"NEW.\"{c.Name}\""));
	return $$"""
		CREATE FUNCTION "{{schema}}"."{{table}}_versioning"() RETURNS trigger
		LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog AS $norse$
		DECLARE ts timestamptz;
		BEGIN
			IF TG_OP = 'UPDATE' AND ROW({{oldColumnList}}) IS NOT DISTINCT FROM ROW({{newColumnList}}) THEN
				RETURN NEW;
			END IF;
			ts := greatest(pg_catalog.clock_timestamp(), pg_catalog.lower(OLD.system_period) + interval '1 microsecond');
			INSERT INTO "{{schema}}"."{{table}}_history" ({{columnList}}, system_period)
				VALUES ({{oldColumnList}}, pg_catalog.tstzrange(pg_catalog.lower(OLD.system_period), ts));
			IF TG_OP = 'UPDATE' THEN
				NEW.system_period := pg_catalog.tstzrange(ts, 'infinity');
				RETURN NEW;
			END IF;
			RETURN OLD;
		END $norse$;
		REVOKE EXECUTE ON FUNCTION "{{schema}}"."{{table}}_versioning"() FROM PUBLIC;
		CREATE TRIGGER "{{table}}_versioning_update" BEFORE UPDATE ON "{{schema}}"."{{table}}"
			FOR EACH ROW EXECUTE FUNCTION "{{schema}}"."{{table}}_versioning"();
		CREATE TRIGGER "{{table}}_versioning_delete" BEFORE DELETE ON "{{schema}}"."{{table}}"
			FOR EACH ROW EXECUTE FUNCTION "{{schema}}"."{{table}}_versioning"();
		""";
}
```

`columns` excludes `system_period` (it is appended by the templates where needed); history-table columns follow the projection rule (spec §3.4): name + store type only, nullable except PK components. Floor assert and btree_gist guard per spec §3.1 steps 0–1 (DO blocks; the guard catches `insufficient_privilege` and re-raises the provisioning-prerequisite diagnostic naming `btree_gist` and this table). (d) Wire both `ReplaceService` calls into `NorsePostgresEfProvider.Configure` after `UseNpgsql`.
- [ ] **Step 4:** Run the PostgreSQL test project — new tests PASS; existing provider-binding tests still PASS.
- [ ] **Step 5:** Stage the diff and stop for review.

---

### Task 6: PostgreSQL enable/disable transitions

**Files:**
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework.PostgreSQL/NorseNpgsqlMigrationsSqlGenerator.cs`
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework.PostgreSQL/TemporalSqlEmitter.cs` (add `EnableTransition(...)` / `DisableTransition(...)`)
- Test: `Urdarbrunnr/tests/Persistence.EntityFramework.PostgreSQL.Tests/TemporalTransitionSqlTests.cs`

**Interfaces:**
- Consumes: Task 5's emitter methods; the spike verdict on how marker-add/marker-remove surface (working hypothesis: `AlterTableOperation` with the annotation appearing/disappearing between `OldTable` and the operation).
- Produces: `TemporalSqlEmitter.EnableTransition(schema, table, columns, pkColumns)` and `DisableTransition(schema, table)`.

- [ ] **Step 1: Write the failing tests.** Generate operations with EF's real differ between two models (unmarked vs marked variants of the same entity) — this validates the diffing *and* the generation in one arrange; helper `TransitionOperations(fromModel, toModel)` uses `IMigrationsModelDiffer` from the context's service provider, then `IMigrationsSqlGenerator.Generate(operations, toModel)`:

```csharp
public sealed class TemporalTransitionSqlTests
{
	[Fact]
	void Enabling_on_an_existing_table_backfills_with_a_single_captured_timestamp()
	{
		var sql = TransitionSql(from: Unmarked, to: Marked);
		sql.ShouldContain("ADD COLUMN system_period tstzrange");
		sql.ShouldContain("ts := clock_timestamp()");
		sql.ShouldContain("SET NOT NULL");
		sql.ShouldContain("PRIMARY KEY (id, system_period WITHOUT OVERLAPS)");
	}

	[Fact]
	void Enabling_emits_the_floor_assert_and_extension_guard() { /* same arrange; assert both blocks present */ }

	[Fact]
	void Disabling_drops_apparatus_then_column_as_explicit_statements()
	{
		var sql = TransitionSql(from: Marked, to: Unmarked);
		sql.ShouldContain("DROP TRIGGER");
		sql.ShouldContain("DROP FUNCTION");
		sql.ShouldContain("DROP VIEW");
		sql.ShouldContain("DROP TABLE");
		sql.ShouldContain("DROP COLUMN system_period");
	}
}
```

- [ ] **Step 2:** Run — FAIL.
- [ ] **Step 3: Implement.** Override the operation the spike named for annotation transitions. Enable emission per spec §3.3: floor assert + extension guard; `ALTER TABLE … ADD COLUMN system_period tstzrange` (nullable); one `DO` block capturing `ts := clock_timestamp()` once and issuing a single `UPDATE … SET system_period = tstzrange(ts, 'infinity') WHERE system_period IS NULL`; `SET NOT NULL` + `SET DEFAULT tstzrange(clock_timestamp(), 'infinity')`; then history table, trigger function, triggers, and view via the Task 5 emitter methods. Disable emission: `DROP TRIGGER` ×2, `DROP FUNCTION`, `DROP VIEW`, `DROP TABLE {table}_history`, `ALTER TABLE … DROP COLUMN system_period` — explicit statements in that order.
- [ ] **Step 4:** Run — PASS, no regressions in the project.
- [ ] **Step 5:** Stage the diff and stop for review.

---

### Task 7: PostgreSQL evolution operations and rejected-operation diagnostics

**Files:**
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework.PostgreSQL/NorseNpgsqlMigrationsSqlGenerator.cs`
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework.PostgreSQL/TemporalSqlEmitter.cs`
- Create: `Urdarbrunnr/tests/Persistence.EntityFramework.PostgreSQL.Tests/PostgresContainerFixture.cs` (+ `PostgresCollection.cs`) — pattern from `Himinbjorg/tests/Identity.Migrations.Tests/PostgresContainerFixture.cs`, image `postgres:19beta2`; add `Testcontainers.PostgreSql` `Version="*"` to the test csproj only if not flowing transitively
- Test: `Urdarbrunnr/tests/Persistence.EntityFramework.PostgreSQL.Tests/TemporalEvolutionSqlTests.cs` (snapshot + order) and `TemporalEvolutionLiveTests.cs` (per-shape application against real PG)

**Interfaces:**
- Consumes: Tasks 5–6 emitter methods; spike verdict for operation→temporal-table identification on each shape.
- Produces: the complete evolution matrix of spec §3.4 (fixed drop-view-first order, ruling 16) — consumed semantically by Task 8; the container fixture Task 8 reuses.

**The DDL order is law (spec ruling 16), identical for every shape:** (1) `DROP VIEW {table}_timeline`; (2) main-table operation; (3) history-table mirror; (4) `CREATE OR REPLACE FUNCTION` from the target column list; (5) `CREATE VIEW` afresh from the target column list. PostgreSQL blocks `DROP COLUMN`/`ALTER … TYPE` under a dependent view, and `CREATE OR REPLACE VIEW` cannot change the output column set — the view is dropped first and recreated last, never replaced in place.

**`RenameTable` has its own six-step choreography** — PostgreSQL keeps a renamed table's triggers, with their old names, bound to the old function; creating a newly named function rebinds nothing: (1) drop the old timeline view; (2) rename the main table; (3) rename the history table; (4) drop the old update/delete triggers, then drop the old function; (5) create the newly named function and newly named triggers bound to it; (6) create the newly named timeline view. Only the tables rename in place (history data mapping preserved); every other apparatus object is retired and recreated — a rename must never leave the table versioning against stale apparatus.

- [ ] **Step 1: Write the failing snapshot tests.** Same differ-driven arrange as Task 6, model variants per shape (column added / renamed / dropped / type-altered; PK-changed; schema-moved). The ordering assertion is a first-class test, not an afterthought:

```csharp
public sealed class TemporalEvolutionSqlTests
{
	[Fact]
	void Every_evolution_batch_drops_the_view_first_and_recreates_it_last()
	{
		var sql = TransitionSql(from: Marked, to: MarkedWithExtraColumn);
		var dropView = sql.IndexOf("""DROP VIEW "public"."temporal_widgets_timeline""");
		var mainAlter = sql.IndexOf("""ALTER TABLE "public"."temporal_widgets" ADD""");
		var historyAlter = sql.IndexOf("""ALTER TABLE "public"."temporal_widgets_history" ADD""");
		var function = sql.IndexOf("CREATE OR REPLACE FUNCTION");
		var createView = sql.IndexOf("""CREATE VIEW "public"."temporal_widgets_timeline""");
		dropView.ShouldBeGreaterThanOrEqualTo(0);
		dropView.ShouldBeLessThan(mainAlter);
		mainAlter.ShouldBeLessThan(historyAlter);
		historyAlter.ShouldBeLessThan(function);
		function.ShouldBeLessThan(createView);
	}

	[Fact]
	void Add_column_mirrors_nullable_onto_history()
	{
		var sql = TransitionSql(from: Marked, to: MarkedWithExtraColumn);
		sql.ShouldContain("""ALTER TABLE "public"."temporal_widgets_history" ADD""");
		sql.ShouldNotContain("_history\" ADD COLUMN extra text NOT NULL"); // history add is nullable
	}

	[Fact]
	void Drop_column_mirrors_the_drop_onto_history() { /* DROP COLUMN on both tables, view-first order */ }

	[Fact]
	void Rename_column_renames_on_history_never_drop_and_add() { /* RENAME COLUMN on both; no DROP COLUMN */ }

	[Fact]
	void Alter_column_type_mirrors_onto_history() { /* ALTER COLUMN TYPE on both, view-first order */ }

	[Fact]
	void Rename_table_retires_the_old_apparatus_and_creates_the_new_in_order()
	{
		var sql = TransitionSql(from: Marked, to: MarkedRenamed);
		var dropView = sql.IndexOf("""DROP VIEW "public"."temporal_widgets_timeline""");
		var renameMain = sql.IndexOf("""RENAME TO "renamed_widgets""");
		var dropTrigger = sql.IndexOf("""DROP TRIGGER "temporal_widgets_versioning_update""");
		var dropFunction = sql.IndexOf("""DROP FUNCTION "public"."temporal_widgets_versioning""");
		var newFunction = sql.IndexOf("""CREATE FUNCTION "public"."renamed_widgets_versioning""");
		var newTrigger = sql.IndexOf("""CREATE TRIGGER "renamed_widgets_versioning_update""");
		var newView = sql.IndexOf("""CREATE VIEW "public"."renamed_widgets_timeline""");
		dropView.ShouldBeGreaterThanOrEqualTo(0);
		dropView.ShouldBeLessThan(renameMain);
		renameMain.ShouldBeLessThan(dropTrigger);
		dropTrigger.ShouldBeLessThan(dropFunction);
		dropFunction.ShouldBeLessThan(newFunction);
		newFunction.ShouldBeLessThan(newTrigger);
		newTrigger.ShouldBeLessThan(newView);
	}

	[Fact]
	void A_primary_key_change_on_a_temporal_table_is_rejected_with_a_named_diagnostic()
	{
		var act = () => TransitionSql(from: Marked, to: MarkedWithDifferentKey);
		act.ShouldThrow<InvalidOperationException>().Message.ShouldContain("drop temporality");
	}

	[Fact]
	void A_schema_move_on_a_temporal_table_is_rejected_with_a_named_diagnostic() { /* same shape */ }
}
```

- [ ] **Step 2:** Run — FAIL.
- [ ] **Step 3: Implement.** Override `Generate` for `AddColumnOperation` / `DropColumnOperation` / `RenameColumnOperation` / `AlterColumnOperation` / `RenameTableOperation` targeting temporal tables (identification per spike verdict). Column-shaped overrides emit the five statements in the fixed order above — `TemporalSqlEmitter` gains `DropTimelineView(schema, table)` and the existing `TimelineView(...)` is reused for step 5; the main-table statement is base's output, sequenced between drop-view and the history mirror (restructure the override to compose the batch rather than call base first). History mirror per the projection rule (spec §3.4 — nullable regardless of main nullability; store type only). The `RenameTableOperation` override follows its six-step choreography above — `TemporalSqlEmitter` gains `DropTriggersAndFunction(schema, oldTable)`; the newly named function/triggers/view come from the existing Task 5 emitters with the new table name; never rely on PG renaming apparatus objects implicitly. For `AddPrimaryKeyOperation`/`DropPrimaryKeyOperation` and schema-differing table operations on temporal tables: throw `InvalidOperationException` whose message names the operation, the table, and the sanctioned path ("drop temporality first (remove ITemporalEntity — visible destruction), perform the change, re-mark; or author the migration by hand").
- [ ] **Step 4:** Run — snapshot tests PASS.
- [ ] **Step 5: Write and run the live-application tests.** Port the container fixture trio from Himinbjörg now (it was Task 8's; it moves here). `TemporalEvolutionLiveTests`: for each supported shape — add, drop, rename column, alter type, rename table — create the marked schema on real PG (`EnsureCreatedAsync` through the custom generator), seed one row and one update (so a live history row and view exist), then execute the shape's differ-generated SQL via `ExecuteSqlRawAsync` and assert it **applies without error** and the view SELECTs afterward. This is the test Task 8 cannot be left to discover: snapshot-green-but-unappliable DDL dies here.

```csharp
[Collection(PostgresCollection.Name)]
public sealed class TemporalEvolutionLiveTests(PostgresContainerFixture fixture)
{
	[Fact]
	async Task Drop_column_applies_against_a_live_history_table_and_view()
	{
		await using var context = await fixture.CreateWidgetContextAsync();
		var id = await context.SeedWidgetAsync("v1");
		await context.UpdateWidgetNameAsync(id, "v2"); // live history row + dependent view
		await context.Database.ExecuteSqlRawAsync(TransitionSql(from: MarkedWithExtraColumn, to: Marked));
		(await context.TimelineRowCountAsync()).ShouldBe(2); // view recreated and queryable
	}

	[Fact]
	async Task Rename_table_rebinds_triggers_to_the_new_function_with_new_names()
	{
		await using var context = await fixture.CreateWidgetContextAsync();
		var id = await context.SeedWidgetAsync("v1");
		await context.UpdateWidgetNameAsync(id, "v2");
		await context.Database.ExecuteSqlRawAsync(TransitionSql(from: Marked, to: MarkedRenamed));
		// TriggerBindingsAsync: pg_trigger joined to pg_proc for the table, tgisinternal = false,
		// returning (trigger name, bound function name) pairs
		var bindings = await context.TriggerBindingsAsync("renamed_widgets");
		bindings.ShouldBe(
			[
				("renamed_widgets_versioning_delete", "renamed_widgets_versioning"),
				("renamed_widgets_versioning_update", "renamed_widgets_versioning"),
			],
			ignoreOrder: true);
		await context.UpdateWidgetNameAsync(id, "v3", table: "renamed_widgets"); // versioning survives the rename
		(await context.HistoryRowCountAsync("renamed_widgets_history")).ShouldBe(2);
	}
	// … one equivalent application test per remaining shape (add, rename column, alter type)
}
```

- [ ] **Step 6:** Run the full PostgreSQL test project (Docker running) — all green.
- [ ] **Step 7:** Stage the diff and stop for review.

---

### Task 8: Real-Postgres integration suite (Testcontainers)

**Files:**
- Create: `Urdarbrunnr/tests/Persistence.EntityFramework.PostgreSQL.Tests/TemporalApparatusIntegrationTests.cs`

**Interfaces:**
- Consumes: everything Tasks 3–7 produced, exercised end-to-end; the `PostgresContainerFixture` created in Task 7.

TDD note: these tests are the spec's acceptance criteria (§6 item 3) — they are written first *as the task's failing suite* and pass only when the chassis is genuinely correct against PG 19beta2; expect them to catch trigger-semantics defects (clock, clamp, suppression) the snapshots and per-shape application tests cannot.

- [ ] **Step 1:** Context under test: the Task 5 `SplitTemporalWidget` model on the Task 7 fixture, schema created via `context.Database.EnsureCreatedAsync()` (which runs through the same SQL generator — assert that holds; if `EnsureCreated` bypasses the custom generator, fall back to executing `GenerateCreateScript()` output directly and record the finding in the task report).
- [ ] **Step 2: Write the failing suite** (representative bodies; every test async, container-scoped database per test via unique database name):

```csharp
[Collection(PostgresCollection.Name)]
public sealed class TemporalApparatusIntegrationTests(PostgresContainerFixture fixture)
{
	[Fact]
	async Task An_update_writes_a_closed_history_row()
	{
		await using var context = await fixture.CreateWidgetContextAsync();
		var id = await context.SeedWidgetAsync("before");
		await context.UpdateWidgetNameAsync(id, "after");
		var history = await context.HistoryRowsAsync(id);
		history.Count.ShouldBe(1);
		history[0].Name.ShouldBe("before");
		history[0].PeriodIsClosed.ShouldBeTrue();
	}

	[Fact]
	async Task A_delete_writes_the_final_closed_version() { /* delete → one closed row, main row gone */ }

	[Fact]
	async Task Repeated_updates_in_one_transaction_yield_positive_length_contiguous_versions()
	{
		// open one transaction, update twice, commit; assert two history rows,
		// each period non-empty, first.upper == second.lower, second.upper == current.lower
	}

	[Fact]
	async Task A_lock_waiting_concurrent_update_closes_monotonically()
	{
		// txn A begins (reads clock), txn B updates + commits, then A updates the same row:
		// no exception, and A's history row's period is strictly positive
	}

	[Fact]
	async Task A_no_op_update_writes_no_history_and_leaves_the_period_untouched() { }

	[Fact]
	async Task A_fragment_only_update_writes_no_history_row() { /* update only the split column */ }

	[Fact]
	async Task Enabling_on_a_table_with_existing_rows_backfills_one_enable_timestamp()
	{
		// EnsureCreated from the UNMARKED model, insert 3 rows, apply the differ-generated
		// enable transition SQL (Task 6 arrange, executed via ExecuteSqlRawAsync), then:
		// all 3 rows share one system_period lower bound; a subsequent update versions normally
	}

	[Fact]
	async Task Disabling_tears_the_apparatus_down() { /* apply disable SQL; triggers/function/view/history gone */ }

	[Fact]
	async Task The_schema_dump_contains_the_apparatus() { /* GenerateCreateScript ShouldContain the blocks */ }
}
```

- [ ] **Step 3:** Run the suite (`dotnet test`, Docker running). Iterate on Tasks 5–7 SQL until green — any fix loops back through that task's snapshot tests first (fix the snapshot expectation only if the snapshot was wrong, never to make a defect pass).
- [ ] **Step 4:** Run the **entire Urðarbrunnr solution** test set — all green.
- [ ] **Step 5:** Stage the diff and stop for review.

---

### Task 9: Realm docs + ship gate (Urðarbrunnr)

**Files:**
- Modify: `Urdarbrunnr/README.md` + `Urdarbrunnr/CLAUDE.md` (boy-scout law — the pair must describe the temporal capability: marker, provider posture, PG19 floor)
- Modify: `Glitnir/docs/Platform/specs/2026-08-04-temporal-tables-persistence-chassis-design.md` (mark chassis-side exit criteria met; record any deviations discovered in Tasks 3–8)

**Interfaces:** none new — this is the record catching up to the code.

- [ ] **Step 1:** Update the Urðarbrunnr README/CLAUDE.md pair in one change: the `ITemporalEntity` opt-in, the two-act adoption workflow, the SQL Server park with its re-entry trigger, and the PG19 floor. Keep both files telling one story at two altitudes.
- [ ] **Step 2:** Spec check: walk spec §2–§6 against the shipped code; annotate the spec only where reality forced a deviation (each deviation is a dispute — if any is more than mechanical, stop and raise it rather than annotating around it).
- [ ] **Step 3:** Full `dotnet build` + `dotnet test` at the Urðarbrunnr solution root — green, zero warnings (warnings are errors platform-wide).
- [ ] **Step 4:** Stage everything and stop. **Ship ceremony is human-driven** per standing law: PR, CI green, tag, NuGet publish — the same gate every realm slice has passed. Do not open the PR unprompted.

---

### Task 10 (GATED): Himinbjörg enablement

**Gate:** .NET 11 preview 7 installed (~2026-08-11) AND Himinbjorg#47's operational-noise split verified on it (split tables create with dependent FKs bound to `users` — #47 sequencing step 1) AND Task 9 shipped (Urðarbrunnr package published carrying the chassis). **Re-plan this task at gate-open if preview 7 changed EF's split-table emission** — the #47 FK bug's fate decides the exact model configuration.

**Files (expected shape):**
- Modify: `Himinbjorg/src/Identity.EntityFramework/NorseIdentityDbContext.cs` + identity entity configurations — `ITemporalEntity` on the post-split PII-bearing tables; `TemporalParkedOnSqlServer()` on the split user entity; `user_lockout` unmarked
- Create: new scaffolded migrations in `Himinbjorg/src/Identity.Migrations.PostgreSQL/` and `.SqlServer/` (`dotnet ef migrations add EnableTemporalIdentity` per provider)
- Test: `Himinbjorg/tests/Identity.Migrations.Tests/` — container tests asserting: identity flows through `UserManager`/`SignInManager` write history on `users`; lockout churn (failed sign-ins) writes none; which side tables (claims/logins/tokens) take the marker is **Himinbjorg#47 open question 4's ruling — read it from that issue's brainstorm output at gate-open, do not decide here**

- [ ] Steps written at gate-open against preview-7 reality; the chassis contract they consume is fixed by Tasks 3–7 (`ITemporalEntity`, `TemporalParkedOnSqlServer()`, two-act adoption).

### Task 11 (GATED): Bifröst proving-ground assertion

**Gate:** Task 10 merged and shipped (Himinbjörg tag consumed by Yggdrasil's migrations service; Bifröst submodules tracking master pick it up).

**Files (expected shape):**
- Bifröst verification test (home per the migrations-framework live-gate pattern — `aspire.config.json` gate): write through a temporal identity entity against the AppHost-composed primary, then assert the history row on **both primary and replica** under the LSN contract (spec §6 item 5): capture `pg_current_wal_flush_lsn()` post-commit, poll replica `pg_last_wal_replay_lsn() >= lsn` with bounded timeout, timeout diagnostic names replication lag distinctly from a missing row.
- Modify: `Bifrost/README.md` + `Bifrost/CLAUDE.md` pair (state of the union: temporal apparatus live in `norse_identity`).

- [ ] Steps written at gate-open; the replica-assertion contract above is fixed now and is not renegotiable at execution time.

---

## Self-review (performed at write time)

- **Spec coverage:** §1 rulings 1→Task 1, 2→Task 5 (floor assert), 3→Task 3, 4→Task 11, 5→Task 4, 6→Task 5 (no grants emitted; SECURITY DEFINER shape), 7→Tasks 5–7, 8→Tasks 5+8 (clock/clamp/no-op), 9→Task 2, 10→Task 5 (guard), 11→Task 5 (hardening), 12→Task 7 (projection+rejections), 13→Task 6, 14→Tasks 3–4 (residence+naming), 15→Task 3 (reservation), 16→Task 7 (drop-view-first order + per-shape live application). §6 test matrix: items 1→Task 1, 2→Tasks 3–7, 3→Tasks 7–8, 4→Task 10, 5→Task 11.
- **Known open dependency:** Tasks 5–7 bind to the Task 2 spike verdict by design — the checkpoint at Task 2 Step 8 owns revising them. This is the one deliberate forward reference.
- **Type consistency:** `NorseAnnotationNames.Temporal`/`.TemporalParkedOnSqlServer` (Task 3) are the only annotation names used in Tasks 4–7; `TemporalSqlEmitter` signatures declared in Task 5 are the ones Tasks 6–7 extend.
