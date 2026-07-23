# `Norse.EntityFramework` — In-House Snake_Case Naming Convention

**Date:** 2026-07-22
**Status:** Shipped. Hardened 2026-07-23 — see the addendum at the end of this document; the `NorseSnakeCaseNamingConvention` pseudocode in §2 below now undersells the real implementation on two points that were bugs, not design gaps.
**Owner:** Buvy

## Finding

Urðarbrunnr is moving the whole EF stack onto the 11.x train, and `EFCore.NamingConventions` does not
yet support EF Core 11. The dependency has already been pulled (uncommitted working-tree change on
`sync/platform-config`): `EntityFramework.csproj` no longer references it, and
`NorseDbContextOptionsExtensions.ApplyNorseConventions` is a stubbed no-op with a `TODO` in its place
of the single line it used to be (`optionsBuilder.UseSnakeCaseNamingConvention();`). This leaves
`NorseDbContextOptionsExtensionsTests.ApplyNorseConventions_applies_snake_case_naming` red.

Nothing about the *decision* API changes. `../2026-07-03-provider-aware-length-and-naming-conventions.md`
already shipped (v0.0.4) the provider-registration-level opt-in/opt-out shape —
`AddNorsePostgresContext<TContext>(..., useSnakeCaseNaming = true)` and
`AddNorseSqlServerContext<TContext>(..., useSnakeCaseNaming = false)`, both overridable — and that
design holds exactly as-is. This spec only replaces *how* `ApplyNorseConventions` achieves snake_case
renaming, now that the package it rode on is gone.

Prior art exists in two places:
- The pasted `EFCore.NamingConventions.Internal.SnakeCaseNameRewriter` — the battle-tested,
  Unicode-category-aware rewrite algorithm (acronym runs, digits, pre-existing underscores all handled
  correctly), meaningfully more correct than a naive regex.
- Prior art: an `IConventionModelBuilder.ToSnakeCase()` model-walking loop that renames every EF
  metadata object (table, PK, columns, keys, FKs, indexes, default-constraint names, temporal history
  tables, JSON container columns). Its own name-conversion helper is a simple regex
  (`([a-z0-9])([A-Z])` → `$1_$2`) — adequate but the weaker half of that code.

The plan: take the walking loop, swap its naive regex for the ported rewriter, and drop it in as
Urðarbrunnr's own code instead of waiting on an upstream release.

**Addendum (found while verifying the design against the real EF Core 11 API, before planning):**
temporal-table renaming (`IsTemporal()`, `GetHistoryTableName()`, `SetHistoryTableName()`) is not a
provider-neutral relational concept — confirmed by reflecting the real EF Core 11 assemblies, those
three live in `Microsoft.EntityFrameworkCore.SqlServerEntityTypeExtensions`, part of the
`Microsoft.EntityFrameworkCore.SqlServer` package, not `Microsoft.EntityFrameworkCore.Relational`.
Porting the walking loop's temporal-history branch verbatim into `Norse.EntityFramework` would force a
SqlServer package reference onto the provider-neutral base project — exactly the kind of dependency-graph
leak this platform doesn't accept. JSON container-column naming has no such problem (`RelationalTypeBaseExtensions`
is genuinely provider-neutral) and stays in the base convention unchanged. §2–§4 below reflect the fix:
an injected-action seam on `ApplyNorseConventions` that `Norse.EntityFramework.SqlServer` supplies,
`Norse.EntityFramework` never sees SQL-Server-specific EF APIs.

## Scope

**In scope:**
- Port `SnakeCaseNameRewriter`'s algorithm into `Norse.EntityFramework`, collapsed to a static,
  `CultureInfo.InvariantCulture`-only method (no ctor, no field — nothing on this platform plumbs a
  culture through today, and adding one later is a small, easy change; not carried forward
  speculatively).
- Port `ToSnakeCase()`'s model-walking loop into a Norse `IModelFinalizingConvention`, inlined in the
  same file/shape as the existing `RequireExplicitLengthConvention`, calling the ported rewriter. Covers
  every provider-neutral rename: table, PK, columns, default-constraint names, keys, FKs, indexes, and
  JSON container columns. `RegisterResult()`/`GuardMaxLength()` from that prior art are **not** ported —
  unrelated concerns; max-length enforcement is already `RequireExplicitLengthConvention`'s job here.
- Add an injected-action seam so a provider-specific project can extend the rename walk without the
  base project referencing that provider's package — see the Addendum above and Design §2–§4. Wire
  `Norse.EntityFramework.SqlServer`'s `AddNorseSqlServerContext`/`AddNorseSqlServerMigrationContext` to
  supply the temporal-history-table rename through it.
- Re-wire `NorseDbContextOptionsExtensions.ApplyNorseConventions` to register the new convention via a
  proper additive `IDbContextOptionsExtension`, replacing the removed one-liner.
- Turn the existing red test green; add rewriter-algorithm unit tests and one integration-style test
  covering a renamed object other than the table name (the current single test only asserts the table).

**Out of scope:**
- Any abstraction for naming styles other than snake_case (`INameRewriter`-equivalent). Buvy's own
  framing: PascalCase-for-SQL-Server / snake_case-for-Postgres is "perfect for now"; a second case
  notation is a real-consumer-forces-it extension point for later, not speculative now.
- Any change to the provider-registration decision API (defaults, override shape) — that's settled and
  shipped per the 2026-07-03 spec.
- Restoring `EFCore.NamingConventions` once it ships EF 11 support. Not a stated goal — this is a
  permanent in-house replacement, not a stopgap pending upstream (consistent with "auto-resolving over
  pinned" / "no waiting on a feed" instincts elsewhere on this platform, applied here to a dependency
  rather than a version).
- The rest of the uncommitted EF-11 package-pinning work already in the working tree (unrelated
  `Version="11.*-*"` / Aspire package bumps across several `.csproj` files) — this spec covers only the
  naming-convention slice of that larger in-flight change.

## Design

### 1. `SnakeCaseNameRewriter`

```csharp
namespace Norse.EntityFramework;

internal static class SnakeCaseNameRewriter
{
    internal static string RewriteName(string name)
    {
        // ...ported algorithm, unchanged logic, CultureInfo.InvariantCulture hardcoded
        // at the two ToLower(char, CultureInfo) call sites instead of threaded through a ctor.
    }
}
```

`internal` — nothing outside this assembly needs it directly; consumers reach snake-casing exclusively
through `ApplyNorseConventions`.

### 2. `NorseSnakeCaseNamingConvention`

```csharp
namespace Norse.EntityFramework;

sealed class NorseSnakeCaseNamingConvention(
    Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames) : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder builder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entity in builder.Metadata.GetEntityTypes())
        {
            // JSON-mapped entities: rewrite the container column name only, then continue —
            // ported from ToSnakeCase()'s early-exit branch (EF migrations fail if a JSON-mapped
            // entity's table/column identity is touched the normal way).
            // Otherwise: table name, primary key name, every property's column name and default
            // constraint name, every key name, every FK constraint name, every index name — each via
            // SnakeCaseNameRewriter.RewriteName.

            // Provider-specific extension point (e.g. SQL Server temporal history tables). The base
            // convention has no idea what this does — it only hands the entity and its own rewrite
            // function to whatever the registering provider supplied, or nothing at all.
            applyProviderSpecificRenames?.Invoke(entity, SnakeCaseNameRewriter.RewriteName);
        }
    }
}
```

Same file-and-class shape as `RequireExplicitLengthConvention` — no separate extension-method file the
way that prior art split it; Urðarbrunnr's existing pattern inlines the walk directly in
`ProcessModelFinalizing`, and this stays consistent with that. `applyProviderSpecificRenames` receives
`SnakeCaseNameRewriter.RewriteName` itself, not the rewriter type — so a provider-specific caller (e.g.
`Norse.EntityFramework.SqlServer`) never needs visibility into `SnakeCaseNameRewriter`; it stays
`internal` to this project.

### 3. `NorseSnakeCaseNamingOptionsExtension` + `NorseSnakeCaseConventionSetPlugin`

EF Core resolves `IConventionSetPlugin` as `IEnumerable<IConventionSetPlugin>` when building the
convention set — designed for multiple plugins to compose, the same mechanism
`EFCore.NamingConventions` itself used. Two ways to register one from a `DbContextOptionsBuilder`:
`ReplaceService<IConventionSetPlugin, T>()` (one line, but swaps the DI registration outright — fine
today since nothing else registers a plugin, but silently clobbers any future second plugin with no
compile-time signal) or a proper additive `IDbContextOptionsExtension` whose `ApplyServices` calls
`services.AddSingleton<IConventionSetPlugin, T>()` (composes safely, matches how
`EFCore.NamingConventions` itself did it). **Decided: the additive route** — more boilerplate, but
this platform prefers a landmine-free default over a shortcut, especially for package-internal wiring
that's cheap to get right once.

Both types now carry `applyProviderSpecificRenames` through to the convention:

```csharp
namespace Norse.EntityFramework;

sealed class NorseSnakeCaseNamingOptionsExtension(
    Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames) : IDbContextOptionsExtension
{
    internal Action<IConventionEntityType, Func<string, string>>? ApplyProviderSpecificRenames { get; }
        = applyProviderSpecificRenames;

    public void ApplyServices(IServiceCollection services)
        => services.AddSingleton<IConventionSetPlugin>(
            new NorseSnakeCaseConventionSetPlugin(ApplyProviderSpecificRenames));

    public IDbContextOptionsExtension ApplyDefaults(IDbContextOptions options) => this;

    public void Validate(IDbContextOptions options) { }

    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

    sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "using Norse snake_case naming";

        public override int GetServiceProviderHashCode()
            => ((NorseSnakeCaseNamingOptionsExtension)Extension).ApplyProviderSpecificRenames?.GetHashCode() ?? 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo otherInfo
               && Equals(
                   ((NorseSnakeCaseNamingOptionsExtension)Extension).ApplyProviderSpecificRenames,
                   ((NorseSnakeCaseNamingOptionsExtension)otherInfo.Extension).ApplyProviderSpecificRenames);

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) { }
    }
}
```

**Addendum (found during implementation, Task 3):** the code above originally read the primary
constructor's captured `applyProviderSpecificRenames` parameter directly from `ExtensionInfo` via a cast
(`((NorseSnakeCaseNamingOptionsExtension)Extension).applyProviderSpecificRenames`). That does not
compile — a primary-constructor-captured parameter is only resolvable as a bare identifier inside the
declaring class's own instance members; it is not a real member reachable via `instance.parameterName`
from anywhere else, including a nested class after a cast to the outer type. Fixed by adding a genuine
named `internal` property (`ApplyProviderSpecificRenames`) that the primary constructor initializes —
shown above. A C#-mechanics correction, not a design change; the implementer who hit this verified it
with an isolated repro before escalating rather than improvising a workaround.

```csharp

sealed class NorseSnakeCaseConventionSetPlugin(
    Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames) : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.ModelFinalizingConventions.Add(new NorseSnakeCaseNamingConvention(applyProviderSpecificRenames));
        return conventionSet;
    }
}
```

`ShouldUseSameServiceProvider`/`GetServiceProviderHashCode` compare the delegate itself (reference
equality) rather than a constant — two options builders sharing the exact same injected action can share
EF's cached internal service provider; a Postgres context (`null`) and a SQL Server context (the temporal
lambda) never will, avoiding a latent cross-context caching bug. Mirrors the real
`EFCore.NamingConventions` package's own `NamingConventionsOptionsExtension`, which compares its
`INameRewriter` instance the same way.

**Verified, not sketched:** the full member set above — `IDbContextOptionsExtension.ApplyServices` /
`ApplyDefaults` / `Validate` / `Info`, and `DbContextOptionsExtensionInfo`'s ctor plus its five overridable
members — was confirmed by reflecting the real `Microsoft.EntityFrameworkCore` 11.x assembly (not
decompiled source, not recalled from memory) before this spec was finalized. `IConventionSetPlugin.
ModifyConventions(ConventionSet) : ConventionSet` and `ConventionSet.ModelFinalizingConventions` (a plain
`List<IModelFinalizingConvention>`) were confirmed the same way. No first-task verification spike needed
for this shape — it's already load-bearing here.

### 4. `NorseDbContextOptionsExtensions.ApplyNorseConventions`

```csharp
public static DbContextOptionsBuilder ApplyNorseConventions(
    DbContextOptionsBuilder optionsBuilder,
    Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames = null)
{
    ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
        .AddOrUpdateExtension(new NorseSnakeCaseNamingOptionsExtension(applyProviderSpecificRenames));
    return optionsBuilder;
}
```

The new parameter is optional and defaults to `null` — every existing call site
(`AddNorsePostgresContext`, its `*MigrationContext` sibling) compiles and behaves identically with no
change required. Only `Norse.EntityFramework.SqlServer`'s two registration extensions
(`AddNorseSqlServerContext`, `AddNorseSqlServerMigrationContext`) change, to supply the temporal-history
rename when `useSnakeCaseNaming` is true:

```csharp
// Norse.EntityFramework.SqlServer — the only project allowed to reference
// Microsoft.EntityFrameworkCore.SqlServer's IsTemporal()/GetHistoryTableName()/SetHistoryTableName().
if (useSnakeCaseNaming)
{
    NorseDbContextOptionsExtensions.ApplyNorseConventions(opts, static (entity, rewrite) =>
    {
        if (!entity.IsTemporal())
            return;
        var name = entity.GetHistoryTableName();
        if (!string.IsNullOrWhiteSpace(name))
            entity.SetHistoryTableName(rewrite(name));
    });
}
```

### 5. Testing approach

- `SnakeCaseNameRewriterTests` (new) — direct algorithm coverage: PascalCase, camelCase, acronym runs
  (`ID`, `HTTPClient`-shaped input), embedded digits, names with pre-existing underscores, empty
  string.
- `NorseDbContextOptionsExtensionsTests.ApplyNorseConventions_applies_snake_case_naming` (existing,
  currently red) — goes green unmodified; it's already the acceptance test for the table-name case.
- One new integration-style test in the same file/pattern, extending the SQLite-model-build approach
  to assert a second renamed object — a foreign key constraint name or index name, not just the table
  — since the current test only exercises the table-name path and the convention touches several other
  metadata kinds.
- A new test in `EntityFramework.SqlServer.Tests` proving the injected-action seam: build a model with a
  temporal entity against a real `UseSqlServer(...)` provider (no live connection needed — same
  metadata-only pattern already used for `Database.ProviderName` detection), assert
  `AddNorseSqlServerContext`'s injected action renames the history table. `Norse.EntityFramework`'s own
  tests cannot cover this branch — they have no SqlServer package reference by design.

## Documentation updates required

- `NorseDbContextOptionsExtensions.ApplyNorseConventions` — XML doc currently says "via
  `EFCore.NamingConventions`"; update to describe the in-house convention and the new optional
  injected-action parameter.
- `NorseSqlServerContextExtensions.AddNorseSqlServerContext` / `AddNorseSqlServerMigrationContext` — note
  that when `useSnakeCaseNaming` is true, temporal history table names are renamed too, via the
  injected-action seam, and why that logic lives here rather than in `Norse.EntityFramework`.
- Urðarbrunnr `CLAUDE.md` — state-of-the-union note once shipped: `EFCore.NamingConventions` dependency
  removed, snake_case naming now Norse's own code, no functional change to the provider-registration
  API.

## Open items carried into planning, not blocking design

1. Whether the new integration test (item 5 in §5 above) should target a foreign key or an index name —
   plan-time call, not a design fork; either exercises a metadata path the current single test misses.

## Addendum (2026-07-23, found downstream in Mímisbrunnr): two real bugs in the shipped convention

Discovered via `superpowers:systematic-debugging` while root-causing 8 failing
`Mimisbrunnr/tests/Reference.Data.Tests` — both bugs live in `NorseSnakeCaseNamingConvention` as actually
implemented (`src/Persistence.EntityFramework/NorseSnakeCaseNamingConvention.cs`), not in this spec's
design intent; §2's pseudocode above was never updated for either. Root-caused by decompiling the exact
pinned `Microsoft.EntityFrameworkCore.Relational`/`Npgsql.EntityFrameworkCore.PostgreSQL`
`11.0.0-preview.6.26359.118` assemblies with `ilspycmd`, and by `gh search issues`/`gh api graphql`
against `dotnet/efcore`. Full narrative: session memory
`project_ef11-preview-json-shaper-and-history-table-bugs-fixed.md`.

**Bug A — migrations-history table desync.** §2's walking loop renamed `HistoryRepository.EnsureModel()`'s
synthetic `HistoryRow` entity's table too, same as everything else in the model. But
`HistoryRepository.TableName` — used verbatim for raw SQL such as Npgsql's `LOCK TABLE` in
`AcquireDatabaseLockAsync` — is sourced from `RelationalOptionsExtension.MigrationsHistoryTableName`,
never from this convention. First `MigrateAsync` against a fresh database: the model-driven
`CREATE TABLE IF NOT EXISTS __ef_migrations_history` succeeds, then the raw-SQL `LOCK TABLE
"__EFMigrationsHistory"` immediately 42P01s against a table that was never created under that literal
name. **Fix:** `HistoryRow` (`Microsoft.EntityFrameworkCore.Migrations`) is now excluded entirely from
the rename walk — the history table stays PascalCase forever, next to snake_case domain tables.

**Bug B — JSON container-column rename crashes EF Core 11 preview6's query shaper.** §2's JSON early-exit
branch ("JSON-mapped entities: rewrite the container column name only, then continue") renames the
container column on **every** JSON-mapped entity in the walk, including entities nested inside an
already-JSON-mapped parent (e.g. a `SubregionNode` owned by a JSON-root `RegionNode`). Only the JSON
**root** entity actually owns a container column — a nested entity shares it. Renaming a nested entity's
container column a second time crashes
`RelationalShapedQueryCompilingExpressionVisitor.ShaperProcessingExpressionVisitor.CreateJsonShapers`
with `ArgumentNullException: Value cannot be null. (Parameter 'key')` compiling the shaper for any query
that materializes the owning entity — a pure model-shape defect at shaper-compile time, not
data-dependent (reproduces even when the JSON payload is null), and not Npgsql-specific (reproduces
identically on SQLite). Confirmed as a real, already-triaged upstream defect, not a Norse-specific
misuse: `dotnet/efcore#37417` is the identical crash (same stack trace), closed as a duplicate of
`efcore/EFCore.NamingConventions#346`, whose merged fix (`efcore/EFCore.NamingConventions#347`)
introduces the exact root-vs-nested distinction this platform's in-house convention was missing — this
platform doesn't consume that package (§0 above: it's the very dependency this spec replaced), so the
equivalent fix has to live here too. **Fix:** `entity.FindOwnership()?.PrincipalEntityType.IsMappedToJson()`
detects nesting; only the root JSON entity's container column is renamed, nested entities are skipped
entirely. Full snake_case is preserved at the root (`CountryOrArea.View` → `view`); this is the real fix
matching upstream's own resolution, not a workaround pending one.

Both fixes are unit-tested in `Persistence.EntityFramework.Tests/NorseSnakeCaseNamingConventionTests.cs`
(`Migrations_history_table_name_is_not_rewritten`, `Nested_json_entity_shares_the_root_entitys_container_column_unrewritten_again`,
`Nested_json_mapped_entity_round_trips_through_an_actual_query`) — the last two specifically because a
model-metadata-only assertion would not have caught Bug B; it only surfaces compiling an actual query's
shaper.
