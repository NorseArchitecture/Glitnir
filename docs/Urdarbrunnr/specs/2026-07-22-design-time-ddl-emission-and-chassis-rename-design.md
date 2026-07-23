# EF Design-Time Chassis Rename + DDL Emission — Design

**Status:** Approved, ready for planning (2026-07-22).

## 1. Motivation

Urðarbrunnr's `Persistence.EntityFramework.Migrations` / `.Migrations.PostgreSQL` /
`.Migrations.SqlServer` today provide only the runtime side of the migrations
framework: `EfMigrationContributor<TContext>`, `MigrationConnectionStringAttribute`,
and the Roslyn generator that emits `AddNorseMigrations()` for whichever project
hosts the migration service. None of that is design-time tooling — it's the
chassis a realm's checked-in migrations ride on at application startup.

What's genuinely missing is a way to see the *current-state* schema as plain
DDL — a single `CREATE TABLE`/`CREATE INDEX` script reflecting the latest
migration, reviewable by someone fluent in SQL DDL but not in C#/EF's fluent
API. Today the only way to know what a context's schema actually looks like is
to read migration `Designer.cs` files or stand up the database and inspect it
by hand.

This design does two things in the same pass:

1. **Renames** the existing chassis from `Migrations*` to `Design*`, since what
   it actually is — EF design-time tooling — was always a slight misnomer under
   the old name. The realm-level projects that consume it (Mímisbrunnr,
   Himinbjörg) keep the `.Migrations` name, because from their side of the
   fence the design work is done and what's left really is a live runnable
   migration.
2. **Adds DDL-emission** to the renamed chassis: a decorated
   `IMigrationsScaffolder` that, on every `dotnet ef migrations add`/`remove`,
   also calls `Database.GenerateCreateScript()` and writes the result to a
   checked-in file — plus the provider-specific design-time plumbing
   (`IDesignTimeDbContextFactory<T>` bases) that downstream realms build on.

## 2. Scope

**In scope — this pass touches Urðarbrunnr only:**

- Rename `Persistence.EntityFramework.Migrations` (+ `.PostgreSQL`, `.SqlServer`,
  their `.Generator` siblings, and all matching test projects) to
  `Persistence.EntityFramework.Design` (+ `.PostgreSQL`, `.SqlServer`).
  Existing responsibilities (`EfMigrationContributor<T>`,
  `MigrationConnectionStringAttribute`, the `AddNorseMigrations()` generator)
  move under the new name unchanged — same behavior, same consumers.
- New capability, folded into the same three assemblies:
  - `Persistence.EntityFramework.Design`: `DdlEmittingMigrationsScaffolder`
    (decorates `IMigrationsScaffolder`) and `AddNorseDesignTimeServices()` (the
    `IServiceCollection` extension that swaps EF's registered scaffolder for
    the decorated one). Provider-agnostic — no regex/string cleanup of the
    generated script; it's a raw passthrough of `GenerateCreateScript()`,
    since nothing compiles this file yet (§4).
  - `Persistence.EntityFramework.Design.PostgreSQL` /
    `.Design.SqlServer`: each adds a thin abstract
    `IDesignTimeDbContextFactory<TContext>` base
    (`NorsePostgreSqlDesignTimeDbContextFactory<T>` /
    `NorseSqlServerDesignTimeDbContextFactory<T>`) wiring the right provider
    and Norse's naming conventions. A downstream realm supplies the database
    name, `new TContext(options)`, and — only if it needs to — an override of
    a second, narrower hook that customizes the `DbContextOptionsBuilder`
    itself before `.Options` is built (§3). The base intentionally does not
    force every consumer through a single `CreateContext(options)` seam.
- Consumption wiring: downstream `.Migrations.{Provider}` projects reference
  `Design`/`.Design.{Provider}` via `NorseDesignRef`
  (`PrivateAssets="all"` — already defined in
  `Bifrost/Directory.Build.targets`, never yet exercised by a real consumer).
  This is a deliberate departure from the `#if DEBUG`-guard pattern seen in
  older prior art: `NorseDesignRef` keeps the dependency out of the
  referencing project's own published NuGet dependency graph without any
  compiler-directive gymnastics.
- The generator-forwarding strip target
  (`_NorseRemoveUnwantedGeneratorAnalyzers`, `../Glitnir/docs/Platform/specs/2026-07-01-norseref-generator-forwarding-design.md`)
  only treats an analyzer as "wanted" via `NorseRef Generator="true"` — it
  never inspects `NorseDesignRef`. A project pulling `Design.PostgreSQL`/
  `.SqlServer` in via `NorseDesignRef` therefore gets the packed generator
  auto-stripped from its own compilation, with no changes to that target
  required. This is correct: a realm's `.Migrations.{Provider}` project is
  never the one calling `AddNorseMigrations()` — only the migration service
  host (Yggdrasil) is, and it keeps consuming `Design.PostgreSQL`/`.SqlServer`
  via ordinary `NorseRef Generator="true"` (renamed from `Migrations.PostgreSQL`/
  `.SqlServer`, otherwise unchanged).

**Explicitly deferred — designed here, not implemented this pass:**

- Splitting Mímisbrunnr's `ReferenceData.Data.Migrations` and Himinbjörg's
  `Identity.Migrations` into real dual-provider siblings (§5). Both realms
  stay on their current single Postgres-only migrations project until their
  own implementation pass.
- Mímisbrunnr's `Norse.ReferenceData.Data` → `Norse.Reference.Data` rename
  (the current name is needlessly repetitive — "ReferenceData.Data"). Applied
  when Mímisbrunnr's downstream split actually happens, touching Bifröst's and
  Mímisbrunnr's own CLAUDE.md/README naming tables in that same change.
  Recorded here so it isn't re-litigated later.
- Removing Himinbjörg's live `.ToTable()`/`.HasDatabaseName()` violations in
  `NorseRole`/`NorseUser`/`NorseUserRole` — the author is handling this by hand.
  **Clarification for that cleanup, not a carve-out:** the ban targets the
  *naming* overloads — `.ToTable("literal_name")`, `.ToTable("name", schema)`.
  It does not reach `.ToTable(t => t.IsTemporal())`, the parameterless overload
  used purely to turn on temporal versioning with no name involved. No Norse
  realm uses temporal tables today, but a future one that wants them isn't in
  violation by doing so.
- Removing Himinbjörg's `HasDefaultSchema("identity")` (redundant nesting inside
  an already-dedicated `norse_identity` database — contradicts the
  separate-databases-per-context convention). Folds into whichever pass
  regenerates Himinbjörg's initial migration for the provider split, not this
  one.
- Scaffolding an actual `Microsoft.Build.Sql` project against the emitted DDL.
  Raw `.sql` file only, for now — no compiled-views story until a realm
  actually needs one.
- Wiring a real SQL Server container into Bifröst's `AppHost.cs`.

## 3. DDL Emission Mechanism

`DdlEmittingMigrationsScaffolder` decorates EF's concrete `IMigrationsScaffolder`
registration (reconstructed from the pre-existing service descriptor, the same
technique used to layer a decorator over any EF-registered design-time
service):

```
ScaffoldMigration(...) → delegate to inner → emit DDL → return result
RemoveMigration(...)   → delegate to inner → emit DDL → return result
Save(...)              → delegate to inner only (no re-emit needed)
```

"Emit DDL" means: call `ICurrentDbContext.Context.Database.GenerateCreateScript()`
and write the raw output to a checked-in file, headed with an auto-generated
banner. No schema-guard regex cleanup (prior art needed this only to satisfy a
compiling `Microsoft.Build.Sql` project — a constraint that doesn't apply here;
revisit if/when a sqlproj is scaffolded, per §2).

`AddNorseDesignTimeServices()` is the `IServiceCollection` extension a
downstream realm's own `IDesignTimeServices` implementation calls to install
the decorator — this is the one piece of boilerplate every provider-specific
`.Migrations.{Provider}` project must author itself, because EF's tooling
discovers `IDesignTimeServices` by reflecting over whichever assembly
`dotnet ef` is actually pointed at.

**Factory base shape — deliberately not a single rigid seam.** Prior art's
equivalent base only exposed `CreateContext(DbContextOptions<TContext> options)`
as an override point, building the `DbContextOptionsBuilder` itself with no
extension hook. That shape breaks down for ASP.NET Core Identity-style
contexts: Identity's base `OnModelCreating` reads schema version
(`IdentitySchemaVersions.Version1` vs `Version3` — whether the passkey table
exists) off the context's `ApplicationServiceProvider`, not off
`DbContextOptions`, so a consumer needs to call `UseApplicationServiceProvider(...)`
on the *builder* before `.Options` is materialized — something
`CreateContext(options)` alone cannot express. `NorsePostgreSqlDesignTimeDbContextFactory<T>` /
`NorseSqlServerDesignTimeDbContextFactory<T>` therefore expose a second virtual
hook, `ConfigureOptions(DbContextOptionsBuilder<TContext> builder, string connectionString)`
— the base implementation wires the provider, connection string, and Norse
naming conventions; a subclass overrides it, calls
`base.ConfigureOptions(builder, connectionString)`, and layers in whatever
else it needs (Identity's `ApplicationServiceProvider`, or nothing at all for
the common case). The `connectionString` parameter travels alongside the
builder because the base wiring needs the raw string to call
`.UseNpgsql`/`.UseSqlServer` itself. `CreateContext(options)` remains the
only *required* override. This matters even though Himinbjörg's own
consumption is deferred (§5) — designing the base around the narrower shape
now would make it useless for the realm that most needs it.

## 4. Landing Spot and Embedding Convention (for downstream implementation)

Even though no downstream realm changes land this pass, the convention is
fixed here so it doesn't get re-decided per realm:

- Checked-in file: `schema/<database-name>.sql` inside the provider-specific
  Migrations project (e.g. `ReferenceData.Data.Migrations.PostgreSQL/schema/norse_referencedata.sql`),
  named for the actual database rather than derived from the `DbContext` type
  name — readable by a DBA who knows the database, not the C# class.
- The same file is also marked
  `<EmbeddedResource Include="schema/<database-name>.sql" LogicalName="CreateScript.sql" />`
  in the realm's `.csproj`. This embeds the current-state DDL into the compiled
  assembly under a fixed, predictable manifest resource name (`CreateScript.sql`)
  — any tool can `Assembly.GetManifestResourceStream(...)` for it uniformly
  across every realm's Migrations assembly, without knowing the database name
  in advance, while the on-disk file keeps its DBA-readable name for git/review
  purposes.

## 5. Downstream Target Shape (design only, not this pass)

When Mímisbrunnr and Himinbjörg do their own implementation passes, each
realm's single `.Migrations` project splits into three:

- `{Realm}.Migrations` (shared, provider-agnostic): migration contributor,
  seed contributor. No provider knowledge — confirmed nothing else belongs
  here.
- `{Realm}.Migrations.PostgreSQL`: checked-in Postgres EF migrations,
  `{Realm}DbContextFactory : NorsePostgreSqlDesignTimeDbContextFactory<T>`,
  `DesignTimeServices : IDesignTimeServices`, `schema/<database>.sql`
  (+ embedded per §4). References `Persistence.EntityFramework.PostgreSQL`
  (runtime provider wiring) via `NorseRef`, and
  `Persistence.EntityFramework.Design.PostgreSQL` via `NorseDesignRef`.
- `{Realm}.Migrations.SqlServer`: same shape, SQL Server provider.

Per-provider-project surface is deliberately small: the Migrations folder,
the `DbContextFactory`, the `DesignTimeServices` entry point, and the schema
file. Everything else genuinely is shared.

Yggdrasil's migration-service host updates its existing `NorseRef` identity
names (`Migrations.PostgreSQL`/`.SqlServer` → `Design.PostgreSQL`/`.SqlServer`,
`Generator="true"` unchanged) and, once each realm's split lands, references
both of that realm's new `.Migrations.SqlServer` projects alongside the
existing `.Migrations.PostgreSQL` ones.

## 6. Testing Strategy (this pass, Urðarbrunnr only)

- `DdlEmittingMigrationsScaffolder`: unit-test the delegation behavior
  (`ScaffoldMigration`/`RemoveMigration`/`Save` all call through to the inner
  scaffolder) and the emission step, against a minimal in-repo test
  `DbContext` — no real downstream realm involved.
- `AddNorseDesignTimeServices()`: unit-test that it correctly reconstructs and
  substitutes the `IMigrationsScaffolder` registration.
- `NorsePostgreSqlDesignTimeDbContextFactory<T>` /
  `NorseSqlServerDesignTimeDbContextFactory<T>`: unit-test that `CreateDbContext`
  wires the expected provider and applies Norse's naming conventions, using a
  minimal concrete test-double context — building `DbContextOptions` doesn't
  require a live database connection. Also test-double a subclass that
  overrides `ConfigureOptions` to prove the extension hook actually composes
  with the base's own wiring rather than replacing it.
- `SnakeCaseNameRewriter`: add an explicit regression case for ASP.NET Core
  Identity's own built-in index names — `RewriteName("UserNameIndex")` →
  `"user_name_index"`, plus `EmailIndex`/`RoleNameIndex`. This is a proven
  historical failure mode (a prior rewrite of this same algorithm mangled
  these into `usernameindex`, undelimited, and needed a dedicated follow-up
  migration to fix), not a hypothetical edge case — cheap insurance against
  reintroducing it, and directly relevant once Himinbjörg's hand-cleanup
  removes the hardcoded index names currently masking this path.
- End-to-end proof (an actual `dotnet ef migrations add` producing a real
  `schema/*.sql` file) is exercised for real once Mímisbrunnr or Himinbjörg
  implement §5 — that's the genuine integration test for this mechanism, and
  it can't be faked meaningfully from inside Urðarbrunnr alone.
