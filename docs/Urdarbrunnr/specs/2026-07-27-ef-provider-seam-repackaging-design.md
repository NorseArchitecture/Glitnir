# `Norse.Persistence.EntityFramework` — Provider Seam and Repackaging

**Date:** 2026-07-27
**Status:** Ratified in session. Not implemented — the Himinbjörg/Mímisbrunnr adoption sweep is a
dedicated follow-on session with its own plan.
**Owner:** Buvy

## 1. Why

Himinbjörg and Mímisbrunnr both run on Postgres and SQL Server today; the provider surface has been
proven in anger and its flaws are visible. Two more providers are coming — Oracle (blocked on its EF
provider reaching .NET 11, inevitable for the conservative-enterprise audience) and SQLite (the
no-Docker evaluation path). The mandate: their arrival must be **additive**, not a reshaping. The
current shape would require reshaping, so the shape gets fixed now, while N = 2 and the blast radius
is small.

Guiding principle: **as few levers as possible.** Anything derivable from the provider choice —
naming defaults, forced floors, design-time connection strings — is derived, never configured.

## 2. Inventory correction — the generator family is part of the math

The session brief counted `2 + 2N` packages. The honest count includes `gen/`:
`Persistence.EntityFramework.Design.PostgreSQL.Generator`,
`Persistence.EntityFramework.Design.SqlServer.Generator`, and
`Persistence.EntityFramework.Design.Generator.Shared` — a `2 + 3N`-ish trajectory. The two provider
generators differ by **exactly two tokens** (the provider `using` and the
`AddNorse{P}MigrationContext` call name); every other byte is identical.

More importantly, the generator already *is* the platform's provider seam for the migrations host:
Yggdrasil's `Hosting.Migrations.Service` selects its provider today by which `Design.{Provider}`
package it references with `Generator="true"`. Provider selection is already expressed as "which
package is in the compilation." This design does not invent a second seam next to that live one — it
formalizes the existing one.

Two more grounding facts from the code sweep:

- **Zero hand-written callers** of `AddNorsePostgresContext` / `AddNorseSqlServerContext` /
  `AddNorse{P}MigrationContext` exist anywhere on the platform. The only live call sites are the
  generated `AddNorseMigrations()` in the migrations service and the design-time factories. The
  registration API can be reshaped freely.
- **A live bug is in scope to kill structurally** (found in the 2026-07-25 AppHost run): contributor
  discovery infers the migrations assembly from the contributor's own assembly, which broke when
  contributors moved to the shared `.Migrations` project while EF migrations live in
  `.Migrations.{Provider}` siblings.

## 3. Target package topology

| Assembly | Contents | Fate |
|---|---|---|
| `Persistence.EntityFramework` | `INorseDbContext`, `NorseDbContext`, tracking law, naming conventions and rewriter (grows a casing target, §7), value converters, length attributes, **the binding contracts** (§4), **runtime registration choreography** (§5) | Stays; gains the seam |
| `Persistence.EntityFramework.Migrations` *(new)* | `EfMigrationContributor<TContext>`, `MigrationConnectionStringAttribute`, migration-host choreography, **the single generator as its analyzer asset** (§6) | New home for the migrations-runtime concerns currently squatting in `.Design` |
| `Persistence.EntityFramework.Design` | `DdlEmittingMigrationsScaffolder`, `AddNorseDesignTimeServices`, **one neutral `IDesignTimeDbContextFactory` base** parameterized by the binding (§8) | Loses the squatters, gains the factory; keeps `Microsoft.EntityFrameworkCore.Design` as `PrivateAssets="all"` |
| `Persistence.EntityFramework.PostgreSQL` | One sealed binding class + provider/Aspire `PackageReference` pins | Shrinks to thin |
| `Persistence.EntityFramework.SqlServer` | One sealed binding class + provider/Aspire `PackageReference` pins | Shrinks to thin |
| `Persistence.EntityFramework.Design.PostgreSQL` | — | Deleted |
| `Persistence.EntityFramework.Design.SqlServer` | — | Deleted |
| `gen/` provider generators + `Generator.Shared` | — | Collapse into one provider-agnostic generator (§6) |

Package math at N providers: **3 + N thin + 1 generator that never changes again**, versus the
current `2 + 3N` trajectory. (`Persistence.EntityFramework.Generator` — the entity-configuration
generator — is untouched by this design.)

### Why thin packages beat rune-scattered bindings (Option B, rejected)

`3 + 0` counts authoring sites, not consuming sites. A binding rune gets copied into every project
that registers a context — every `.Migrations.{Provider}` project and every runtime composition
root: M realms × N providers copies of *compiled law*, including the compatibility-level floor.
Ginnungagap's scatter is proven for config; scattered law has a merge-lag half-life — the floor
stops being unforgettable the day one realm's scatter PR sits unmerged. The thin package is authored
once, referenced M × N times, and is also the natural carrier for the provider + Aspire integration
`PackageReference` pins; under runes every realm pins those itself and drifts.

### Why the generator does not emit the binding (Option E, rejected)

Generator-as-seam (`3 + 0 + 1`) is maximally compile-time-flavored, but it moves thirty lines of
F12-able library code into generated output — the overreach failure mode the platform buried with
the `GatewayGenerator` on this very date
(`../../Platform/specs/2026-07-27-mediator-pipeline-retires-gateway-design.md`). The generator stays
scoped to what it has proven: discovery and wiring, not law.

### Why the seam cannot be a bare delegate (Option C, rejected)

Aspire enrichment (`Enrich{P}DbContext<TContext>`) is a *generic* call, and open-generic delegates
do not exist in C#. A delegate seam cannot carry it, and it would also make forced floors the
caller's responsibility — a lever. The seam must be a contract with a generic method.

## 4. The binding contract

Two interfaces, split exactly at the provider-tier line. No enum exists anywhere — the binding *is*
the well-known instance ("the binding is the enum"), and an enum plus a `switch` would imply an
assembly that knows all providers, which is the anti-goal.

```csharp
// Persistence.EntityFramework — provider-neutral. Shape, not final code.
public interface INorseEfProvider
{
	// The Use{Provider}(...) call with forced floors chained inside — SQL Server chains
	// .UseCompatibilityLevel(170) unconditionally here; the floor is structurally unforgettable.
	// migrationsAssemblyName is null on the pooled runtime path, supplied on migration paths.
	void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString,
		string? migrationsAssemblyName);

	// Aspire enrichment (retry, health check, telemetry) — generic, hence a contract member.
	void Enrich<TContext>(IHostApplicationBuilder builder)
		where TContext : DbContext, INorseDbContext;

	// Engine-native identifier rewrite, or null to keep EF's raw names (§7).
	// Carries the provider-specific per-entity rename hook (temporal history tables) internally,
	// so ApplyNorseConventions is fed identically on every path.
	INorseNameRewriter? NameRewriter { get; }

	// Syntactically valid, semantically inert — sufficient for offline model building only (§8).
	string DesignTimePlaceholderConnectionString(string databaseName);
}

// The migration-host half. Postgres, SQL Server, and eventually Oracle implement it;
// SQLite implements only the base — a SQLite migrations host is a compile error.
public interface INorseEfMigrationProvider : INorseEfProvider;
```

Notes:

- The concrete bindings are `sealed`, stateless, and exposed as a well-known singleton per provider
  package (~30 lines each). They essentially never change after shipping.
- Exact member shapes (e.g. whether `NameRewriter` folds the temporal hook or the hook stays a
  separate optional member) are the implementation plan's to refine; the contract's *boundaries* —
  what is provider knowledge and what is neutral choreography — are settled here.
- **`useSnakeCaseNaming` is deleted.** Naming is binding data, engine-native per provider, no
  per-realm override — per this design's own success criteria, the only levers left on a realm
  registration are the connection-string name and the context type. A realm that someday genuinely
  needs non-native naming is a new binding, not a flag.

## 5. Registration choreography — one copy, all consumers

The neutral choreography lives once and is consumed by the runtime path, the migration-host path,
and the design-time factory. The temporal-history-rename drift documented in the current SQL Server
design factory (runtime passed the rename hook, design-time didn't) becomes *unrepresentable*, not
merely fixed — there are no longer two hand-synced copies to disagree.

- **Runtime path** (`AddNorseContext<TContext>(binding, connectionStringName)`): resolve the
  connection string from configuration, failing loudly if absent; `AddDbContextPool<TContext>` with
  `binding.Configure(...)`, unconditional `ApplyNorseTrackingBehavior()` (platform law, no lever),
  and binding-derived naming via `ApplyNorseConventions`; then `binding.Enrich<TContext>(builder)`.
- **Migration-host path** (`AddNorseMigrationContext<TContext>(binding, connectionStringName,
  migrationsAssemblyName)`, constrained on `INorseEfMigrationProvider`): identical minus pooling —
  a migrations service constructs its context once and exits, and EF Core forbids `OnConfiguring`
  mutating frozen pooled options, so pooling is pure risk there.
- **Aspire's `Add{P}DbContext` sugar is dropped.** It is connection-string resolution +
  `AddDbContextPool` + the same instrumentation `Enrich{P}DbContext` applies; Aspire documents
  register-your-own-context-then-`Enrich` as a first-class path, and both read the same `Aspire:*`
  settings sections (`DisableHealthChecks`, `DisableTracing`, …), so consumer-visible behavior is
  preserved. This is the single Aspire-facing assumption of the design; the follow-on plan's first
  verification gate is proving pooled + enriched equivalence against the live AppHost.

## 6. The generator — one package, provider-agnostic, forever

The three `gen/` migration-generator projects collapse into one `IIncrementalGenerator`, shipped as
the analyzer asset of `Persistence.EntityFramework.Migrations` — a migrations host references one
package and gets discovery, wiring, and choreography. Yggdrasil's `Generator="true"` reference moves
from `Design.PostgreSQL` to `.Migrations`.

Provider selection extends the discovery pattern the generator already proved (walking compiled
assembly symbols, never syntax trees): scan the compilation's reference closure for implementations
of `INorseEfMigrationProvider`.

- **Exactly one found** → emit `AddNorseMigrations()` calling the neutral choreography with that
  binding's singleton, plus the contributor/seeder registrations exactly as today.
- **Zero or two-plus found** → compile-time diagnostic naming what was (or wasn't) found. No silent
  fallback, no default.

The provider is thereby *derived from the dependency graph* — the migrations project already had to
reference exactly one provider binding to compile its migrations; the generator reads that fact
instead of asking for it. No `CompilerVisibleProperty`, no MSBuild handshake — the mechanism whose
workarounds the platform deleted in the mediator retirement never comes back.

**Structural fix for the 2026-07-25 assembly-resolution bug:** the emitted
`migrationsAssemblyName` is derived from the assembly where the context's `ModelSnapshot` actually
compiles — never from the contributor's assembly. The contributor lives in the shared `.Migrations`
realm project; the snapshot lives in `.Migrations.{Provider}`; only the latter is ever correct.

`Persistence.EntityFramework.Generator` (entity configuration discovery) is out of scope and
unchanged.

## 7. Naming — derived from engine identifier folding

| Provider | Unquoted folding | Engine-native style | Binding's rewriter |
|---|---|---|---|
| PostgreSQL | lowercase | `snake_case` | lower snake (current behavior, kept) |
| SQL Server | none; case-insensitive collation | `PascalCase` | none (current behavior, kept) |
| Oracle | **UPPERCASE** | `UPPER_SNAKE_CASE` | upper snake |
| SQLite | stored as written | anything | none (mirrors SQL Server) |

Oracle verification (the 2002-currency check): unquoted identifiers still fold to uppercase, and
quoted mixed-case identifiers must be quoted forever in every hand-written query and DBA session —
quoted PascalCase optimizes for the C# reader who never sees physical names and punishes the DBA who
lives in them, backwards for a platform that emits DDL as the DBA's lingua franca. Rejected. The one
material change since 2002: the 30-byte identifier limit rose to 128 in 12.2, so UPPER_SNAKE length
is no longer a truncation hazard.

Mechanically: `SnakeCaseNameRewriter` gains an upper/lower target case — a small, contained change —
and each binding supplies its configured rewriter instance (or null). The neutral package holds the
algorithm; the binding holds the choice; no casing enum leaks into the neutral surface. All the
2026-07-23 hardening (migrations-history table exclusion, JSON root-container-only renames) rides
along untouched inside the rewriter/convention.

## 8. Design-time — never connects, fully derived

- `dotnet ef migrations add/remove` builds the model offline; no connection is ever opened. The
  factory needs only a syntactically valid placeholder, supplied by
  `binding.DesignTimePlaceholderConnectionString(databaseName)` — inert by construction (e.g.
  `Host=design;Database={databaseName};…`), pointing at nothing.
- **`DOTNET_EFTOOLS_CONNECTIONSTRING` is deleted.** Its only surviving use was `database update`
  against live infrastructure — the migration host's job by platform law, and "show me the SQL" is
  already covered database-free by `DdlEmittingMigrationsScaffolder`. It was a lever; it dies.
- Both `Design.{Provider}` factory bases collapse into one neutral base in `.Design`, parameterized
  by the binding (abstract `Binding` property beside the existing `DatabaseName`). It consumes the
  same choreography as §5, which is what retires the temporal-rename drift permanently.
- `DdlEmittingMigrationsScaffolder`, `AddNorseDesignTimeServices`, and the realm-side
  `DesignTimeServices` wiring are unchanged. The leaf-project `Microsoft.EntityFrameworkCore.Design`
  reference pattern (chassis marks it `PrivateAssets="all"`, tooling entry-point re-adds it) is
  confirmed working in Mímisbrunnr today and is kept as-is.

## 9. Provider tiers — SQLite is a different animal, and the seam says so

SQLite is not a fourth production provider; it exists solely for clone-and-run without Docker.
The seam encodes that as a *declared capability*, not a pile of `NotSupportedException`s:

1. **Compile-time:** its binding implements `INorseEfProvider` only. The migration-host
   choreography and the generator both constrain on `INorseEfMigrationProvider`, so a SQLite
   migrations host cannot build. No tier flag, no runtime check standing in for the type system.
2. **Startup guard:** the SQLite binding's `Configure`/registration path refuses to run outside a
   Development environment — thrown loudly, so the escape hatch cannot quietly become someone's
   production database.
3. Its migrate-on-boot convenience (there is no production deployment to protect, so no init
   container applies) is that package's local concern when it arrives — it never touches the
   migration-host choreography or this contract.

## 10. Acceptance test

- **Oracle arrives:** one thin package (sealed binding implementing both interfaces + provider/Aspire
  pins) and a row in §7's table. Zero edits to the three neutral assemblies, zero generator edits,
  zero edits to realms that don't opt in.
- **SQLite arrives:** one thin package implementing the runtime half only. Same zeroes.

If either arrival requires more, this design has failed and gets amended — not worked around.

## 11. Follow-on consumer diff (sized for honesty, executed in its own session)

- **Himinbjörg / Mímisbrunnr:** re-base each `.Migrations.{Provider}` factory onto the neutral
  `.Design` base naming its binding; swap `NorseDesignRef` from `Design.{Provider}` to `.Design`
  plus the thin provider package; `DesignTimeServices` files unchanged. Delete nothing realm-side
  beyond the reference swap.
- **Yggdrasil:** `Generator="true"` reference moves from `Design.PostgreSQL` to `.Migrations`;
  `Program.cs` stays three lines.
- **Bifröst:** no composition change; the AppHost run is the live verification gate.
- Zero hand-written registration calls exist anywhere to migrate (§2).

## 12. Testing

- **Binding contract tests**, one suite parameterized per provider package: forced floors present in
  built options (compat level 170 on SQL Server), placeholder string parses but points nowhere,
  rewriter output matches §7, tracking behavior is `NoTracking` on every path.
- **Generator tests:** compilations with zero, one, and two `INorseEfMigrationProvider`
  implementations in the reference closure (diagnostic / emission / diagnostic); migrations-assembly
  derivation from the snapshot's assembly, including the split-contributor arrangement that broke on
  2026-07-25.
- **Live gate:** `dotnet run --project src/Orchestration.AppHost` from Bifröst — the migrations
  service stands up `norse_identity` and `norse_reference` to completion, as today.

## 13. Non-goals

- Migrating Himinbjörg or Mímisbrunnr onto the new shape (follow-on session, §11 is its outline).
- Implementing Oracle or SQLite — only guaranteeing their arrival is additive.
- The no-tracking law, the Two Unions doctrine, the DDL scaffolder's behavior, and
  `Persistence.EntityFramework.Generator` — all untouched.
- Multi-provider routing within a single deployment.
- Versioning ceremony: settled by mechanics, not preference — a tag packs every packable project in
  the repo together, so the trio and the thin bindings ship lockstep within Urðarbrunnr by
  construction.
