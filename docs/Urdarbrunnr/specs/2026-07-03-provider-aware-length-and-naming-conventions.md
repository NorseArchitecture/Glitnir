# `Norse.EntityFramework` — Provider-Aware Fixed-Length and Naming Conventions

**Date:** 2026-07-03
**Status:** Design converged, ready for planning
**Owner:** Buvy

## Finding

PostgreSQL's own documentation states `character(n)` has no storage or performance advantage over
`character varying(n)` on that engine — unlike SQL Server, where fixed-length storage avoids a
per-row length-prefix — and is usually the *slower* of the two. Himinbjörg already discovered and
hand-worked-around this for `NorseUser.SecurityStamp`
(`Himinbjorg/src/Identity/NorseUser.cs:41-44`): `.HasMaxLength(32)` deliberately without
`.IsFixedLength()`, with a matching test (`NorseUserConfigureTests.cs:50-53`) and a comment
explaining why. This spec pushes that discovery down into the framework so every realm gets the
provider-correct behavior automatically from `[FixedLength(n)]`, instead of re-deriving and
re-commenting it per property, per realm.

## Scope

**In scope (Urdarbrunnr):**
- `[FixedLength(n)]` → `.IsFixedLength()` translation becomes provider-conditional.
- New `Norse.EntityFramework.SqlServer`, `Norse.EntityFramework.Migrations.SqlServer`, and
  `Norse.EntityFramework.Migrations.SqlServer.Generator` packages, mirroring the existing
  PostgreSQL trio exactly, plus their `.Tests` projects.
- Fixes the pre-existing snake_case-naming bug (`ApplyNorseConventions()` called unconditionally
  regardless of provider) as part of this work — the SQL Server packages landing is what turns a
  dormant defect into an active one, and the fix is a known, bounded blast radius. Boy-scout rule:
  a known-bad thing found while working nearby gets fixed here, not filed for later.

**In scope (Himinbjörg, minimal companion edit only):**
- `NorseIdentityDbContext.cs` — remove its unconditional `ApplyNorseConventions()` call from
  `OnConfiguring`, and update its `NorseModelConventions.Apply(...)` call site for the new required
  parameter. Nothing else in Himinbjörg changes.

**Out of scope:**
- Actually proving the bifurcation against a running SQL Server instance ("test the mettle in
  Himinbjörg") — that's a separate future session once these packages exist.
- `UnboundedLengthAttribute` — needs no change. Postgres's own docs say there's no performance
  difference between `text` and a bounded `varchar(n)` either, and both providers already map the
  `-1` sentinel to their native unbounded type (`text`/`nvarchar(max)`) correctly today.

## Design

### 1. FixedLength bifurcation

`RequireExplicitLengthConvention` gains a constructor parameter, `bool applyFixedLength`. Only when
`true` does it translate `[FixedLength(n)]` into `.IsFixedLength()`
(`RequireExplicitLengthConvention.cs:29-30` today). When `false`, `[FixedLength(n)]` still satisfies
"explicit length declared" — it derives from the real
`System.ComponentModel.DataAnnotations.MaxLengthAttribute`, so EF Core's own built-in attribute
convention already sets `HasMaxLength` regardless of this flag. The only thing the flag changes is
whether `.IsFixedLength()` gets called; no property ever goes unbounded because of this change.

`NorseModelConventions.Apply(ModelConfigurationBuilder configurationBuilder, bool applyFixedLength)`
threads it through to the convention registration. The parameter is required, with no default —
every call site must state its provider explicitly rather than silently inheriting a guess.

`NorseDbContext.ConfigureConventions` computes the flag:

```csharp
const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";

protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    base.ConfigureConventions(configurationBuilder);
    NorseModelConventions.Apply(configurationBuilder, applyFixedLength: Database.ProviderName == SqlServerProviderName);
}
```

The constant lives in `Norse.EntityFramework` itself — comparing `Database.ProviderName` (a plain
string on the base `DbContext` API) to a literal needs no assembly reference to the SqlServer
package, so the provider-agnostic base package stays provider-agnostic in its dependencies even
though it now contains one provider's name as a string.

**Verify before relying on it:** `Database.ProviderName` is documented as safe inside
`OnModelCreating`. `ConfigureConventions` runs slightly earlier in the context lifecycle
(`OnConfiguring` → `ConfigureConventions` → `OnModelCreating`), but strictly after `OnConfiguring`
completes and options are frozen, so it should already be populated. The first TDD task for this
work is a red test confirming this empirically, before any other code depends on it.

Himinbjörg's `NorseIdentityDbContext.ConfigureConventions` gets the same treatment (it cannot
inherit `NorseDbContext`, so it duplicates the pattern independently, same as it already does today
for `NorseModelConventions.Apply`).

### 2. Naming-convention fix

Today `UseSnakeCaseNamingConvention()` is applied inconsistently, which is exactly why the
provider-blindness went unnoticed:

| Call site | Today | Problem |
|---|---|---|
| `AddNorsePostgresContext` (pooled runtime) | Calls it directly inline via `configureDbContextOptions` | None — already correct shape |
| `AddNorsePostgresMigrationContext` (non-pooled/migrations) | Doesn't call it at all | Relies entirely on the context's own `OnConfiguring` |
| `NorseDbContext.OnConfiguring` | Calls `ApplyNorseConventions()` unconditionally | Runs for every provider, not just Postgres |
| Himinbjörg `NorseIdentityDbContext.OnConfiguring` | Calls `ApplyNorseConventions()` unconditionally (both pooled-frozen-skip and non-pooled branches) | Same problem, independently |

Fix: naming becomes a decision made once, explicitly, at the provider-registration call site —
never inferred at runtime, never living on a shared base context. Both `AddNorsePostgresContext`
and the new `AddNorseSqlServerContext` (and their `*MigrationContext` siblings) take the same
parameter shape:

```csharp
// Norse.EntityFramework.PostgreSQL — default true: Postgres folds unquoted identifiers to
// lowercase, so snake_case is this engine's own escape-free native style, not an opinionated
// override being imposed on it.
public static IHostApplicationBuilder AddNorsePostgresContext<TContext>(
    this IHostApplicationBuilder builder, string connectionStringName, bool useSnakeCaseNaming = true)
    where TContext : DbContext, INorseDbContext
{
    builder.AddNpgsqlDbContext<TContext>(connectionStringName,
        configureDbContextOptions: opts => { if (useSnakeCaseNaming) NorseDbContextOptionsExtensions.ApplyNorseConventions(opts); });
    return builder;
}

// Norse.EntityFramework.SqlServer — default false: SQL Server's default collation is
// case-insensitive, so raw PascalCase already round-trips without quoting/escaping.
public static IHostApplicationBuilder AddNorseSqlServerContext<TContext>(
    this IHostApplicationBuilder builder, string connectionStringName, bool useSnakeCaseNaming = false)
    where TContext : DbContext, INorseDbContext
{
    builder.AddSqlServerDbContext<TContext>(connectionStringName,
        configureDbContextOptions: opts => { if (useSnakeCaseNaming) NorseDbContextOptionsExtensions.ApplyNorseConventions(opts); });
    return builder;
}
```

Same shape, same per-provider default, on `AddNorsePostgresMigrationContext` /
`AddNorseSqlServerMigrationContext`. Both directions stay fully explicit and overridable — pass
`true` to opt SQL Server into snake_case, `false` to opt Postgres out — but silence resolves to
whatever that storage engine actually wants, never a cross-provider opinion smuggled in as a
default. Whichever choice a deployment makes, it is made once, explicitly, at the registration call
site — not re-derived per query by whoever has to hand-write SQL against it later.

`NorseDbContextOptionsExtensions.ApplyNorseConventions` survives unchanged in shape (still one line:
`optionsBuilder.UseSnakeCaseNamingConvention()`), just changes callers: from "every context calls it
unconditionally in `OnConfiguring`" to "every provider registration extension calls it
conditionally." Both `NorseDbContext.OnConfiguring` and Himinbjörg's
`NorseIdentityDbContext.OnConfiguring` lose their call entirely.

**Migration generator note:** the Roslyn-generated `AddNorseMigrations()` (emitted from
`MigrationConnectionStringAttribute`) currently calls `AddNorsePostgresMigrationContext<T>(name,
assemblyName)` with no naming argument. It keeps emitting the plain call — defaults apply
(`true` for Postgres, `false` for SqlServer once that generator variant exists) — rather than
plumbing a new attribute property through this round. A realm needing to override naming
specifically for its migrations context is a real-consumer-forces-it extension point for later, not
speculative now.

### 3. New SQL Server packages (full parity)

Mirrors the existing PostgreSQL trio exactly:

- **`Norse.EntityFramework.SqlServer`** — `AddNorseSqlServerContext<TContext>()` (pooled runtime) +
  `AddNorseSqlServerMigrationContext<TContext>()` (non-pooled migrations), via
  `Aspire.Microsoft.EntityFrameworkCore.SqlServer` (`Version="*"`, matching the floating-version
  convention the Postgres package already uses).
- **`Norse.EntityFramework.Migrations.SqlServer`** + **`.Generator`** — same meta-package bundling
  shape as `EntityFramework.Migrations.PostgreSQL` (+ its generator), packaging
  `EntityFramework.Migrations` + `EntityFramework.SqlServer` + the generator as one NuGet reference.
- Corresponding `.Tests` projects mirroring `EntityFramework.PostgreSQL.Tests` and
  `EntityFramework.Migrations.PostgreSQL.Generator.Tests`.

**Generator duplication avoided.** `MigrationContributorGenerator.cs` (Postgres) is ~140 lines of
which only two emitted lines are provider-specific (the `using Norse.EntityFramework.PostgreSQL;`
namespace and the `AddNorsePostgresMigrationContext<T>` call) — the rest (walking compiled symbols
for `EfMigrationContributor<TContext>`, matching `MigrationConnectionStringAttribute`, building the
contributor list) is entirely provider-agnostic. That logic moves into a new
`EntityFramework.Migrations.Generator.Shared` source file, linked into both
`EntityFramework.Migrations.PostgreSQL.Generator` and the new
`EntityFramework.Migrations.SqlServer.Generator` via `<Compile Include>` — plain compiled-in source
sharing (no analyzer-referencing-analyzer problem), each generator project supplying only its own
provider namespace and method name to the shared entry point.

### 4. Testing approach

The `applyFixedLength` and `useSnakeCaseNaming` flags are explicit parameters at every level down to
`RequireExplicitLengthConvention`'s constructor and `NorseModelConventions.Apply` — so unit tests can
exercise both branches directly (`NorseModelConventions.Apply(configurationBuilder,
applyFixedLength: true)` vs `false`) without needing a real Postgres or SQL Server instance. The
existing `EntityFramework.Tests` suite builds models against SQLite
(`UseSqlite("Data Source=:memory:")`) purely as a cheap in-memory relational stand-in for model
metadata assertions — that stays unchanged; SQLite's own `Database.ProviderName` is irrelevant to
these tests since the flag is passed explicitly rather than inferred, decoupling "which convention
branch does this test exercise" from "which ADO provider builds the model."

`FixedLength_attribute_sets_IsFixedLength_and_satisfies_the_convention`
(`RequireExplicitLengthConventionTests.cs:60`) currently asserts `IsFixedLength().ShouldBe(true)`
unconditionally — it becomes two tests, one per flag value, both still riding on SQLite.

Only `NorseDbContext.ConfigureConventions`'s own `Database.ProviderName == SqlServerProviderName`
computation needs an integration-level check (or a targeted unit test against a `DbContextOptions`
built with `UseSqlServer(...)` / `UseNpgsql(...)` purely for metadata, no live connection) to prove
the detection itself is correct — this is the "verify before relying on it" item from §1.

## Documentation updates required

- `FixedLengthAttribute.cs` — XML doc currently says the attribute unconditionally "translates...
  into `IsFixedLength()`." Needs the provider caveat and a pointer to why (Postgres docs finding).
- `RequireExplicitLengthConvention.cs` — explain the new `applyFixedLength` parameter and its
  provenance.
- `NorseModelConventions.cs` — explain the threaded parameter.
- `NorseDbContext.cs` — explain the provider-name constant and the `Database.ProviderName` check,
  and why the naming-convention call was removed from `OnConfiguring` (moved to registration sites).
- `NorseDbContextOptionsExtensions.ApplyNorseConventions` — update doc to reflect it's now called
  conditionally by provider registration extensions, not unconditionally by every context.
- Urdarbrunnr `CLAUDE.md` — state of the union update once shipped, same as prior task waves.

## Open items carried into planning, not blocking design

1. Exact EF Core API confirmation for `Database.ProviderName` timing inside `ConfigureConventions`
   — first TDD red test, not a design risk (fallback if it's ever unpopulated: compute the flag in
   `OnModelCreating` instead, one level later in the pipeline, and pass it down from there).
2. Whether `Norse.EntityFramework.Migrations.SqlServer.Generator`'s shared-source project needs its
   own `.Tests` project mirroring `EntityFramework.Migrations.PostgreSQL.Generator.Tests`, or
   whether the existing Postgres generator tests already cover the shared logic sufficiently and the
   SqlServer generator only needs a thin smoke test for its two provider-specific emitted lines —
   plan-time call, not a design fork.
