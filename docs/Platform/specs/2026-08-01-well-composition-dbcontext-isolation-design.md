# Well Composition — DbContext Isolation and Construction Unification

**Status:** Design approved (Buvy, 2026-08-01) — not yet planned or implemented.

**REQUIRED SUB-SKILL:** `superpowers:subagent-driven-development` (the platform default — `superpowers:executing-plans` is the narrow fallback for a separate-session review checkpoint, never an interchangeable alternative), paired with `superpowers:test-driven-development` on every task.

## 1. Problem

Discovered live, 2026-08-01, while fixing a CI failure in Yggdrasil's `feature/well-and-wire` PR (#115): `Hosting.Web.Server.Tests`' new E2E fixture (well-and-wire Task 13) directly referenced `Norse.Persistence.EntityFramework.PostgreSQL` (Urðarbrunnr) and `Norse.Reference.Data.Migrations` (Mimisbrunnr) — both two layers below what Yggdrasil is allowed to touch directly. Chasing the fix surfaced two real, pre-existing architectural gaps this spec closes:

### 1.1 Yggdrasil's composition law was never written down

**Yggdrasil composes exactly three realms directly: Midgard, Himinbjörg, Mimir.** Everything below them (Asgard, Urðarbrunnr, Svartálfheim, Mimisbrunnr) is reached only through what those three choose to expose. This was implicit in how the realm chain is described elsewhere (`../../CLAUDE.md` §2's realm table) but never stated as a hard boundary a test or a build could check. `Hosting.Web.Server.Tests` reaching for Urðarbrunnr's and Mimisbrunnr's packages directly is exactly the violation this rule exists to name.

A second, sharper point falls out of the same investigation: **migrations are not `Hosting.Web.Server`'s concern at all.** By platform law, migrations are a deployment-job concern (`Norse.Hosting.Migrations.Service`), never run at application startup. A test under `Hosting.Web.Server.Tests` that directly drives `NorseReferenceMigrationContributor.MigrateAsync`/`ReferenceDataSeedContributor.SeedAsync` is testing something that suite has no business knowing exists.

### 1.2 `DbContext` leaks into Yggdrasil's runtime realms — real vendor lock-in, not a style nit

`Mimir/src/Reference.Web.Server/ServiceCollectionExtensions.cs`'s `AddNorseReferenceService(connectionString)` calls EF Core directly:

```csharp
services.AddDbContextFactory<ReferenceDbContext>(o =>
{
	o.UseNpgsql(connectionString);
	o.ApplyNorseConventions(NorseNameRewriters.LowerSnakeCase);
	o.ApplyNorseTrackingBehavior();
});
```

`Himinbjörg`'s `AddNorseAuthenticationService` is understood to follow the identical shape (same author, same era — confirm during planning). This means `Microsoft.EntityFrameworkCore` is visible in a project that sits inside Yggdrasil's runtime composition graph. That is precisely the class of problem the platform has spent this whole design session naming: `DbSet<T>`, ambient change tracking, and lazy loading are all EF surface leaking into places that shouldn't have to know EF exists — this is the same failure mode one layer up, at the level of "does a bounded context's web server even reference the ORM at all." A platform whose runtime hosting layer requires EF Core to compile is a platform that has quietly decided every consumer is locked to EF Core, forever, whether they know it or not.

### 1.3 The shared seam already exists — `Reference.Web.Server` just doesn't call it

Corrected during design self-review, 2026-08-01: Urðarbrunnr already ships the unification this spec first assumed needed inventing. `NorseDbContextOptionsExtensions.ApplyNorseProviderOptions(INorseEfProvider provider, string connectionString, string? migrationsAssemblyName)` is documented, verbatim, as "the neutral choreography... the only consumer [of `INorseEfProvider`]... one copy, three consumers [runtime, migration-host, design-time], so runtime/design-time drift is unrepresentable." `AddNorseMigrationContext<TContext>()` (migration-time) already calls it. Urðarbrunnr also already ships `AddNorseContext<TContext>(INorseEfProvider provider, string connectionStringName)` — a pooled *runtime* registration built on `AddDbContextPool<TContext>` + `ApplyNorseProviderOptions`.

The actual gap is narrower than originally framed: `Reference.Web.Server`'s `AddNorseReferenceService` never calls either of these — it hand-rolls `UseNpgsql`/`ApplyNorseConventions`/`ApplyNorseTrackingBehavior` itself, bypassing the seam that already exists specifically to make this drift unrepresentable. And the reason it couldn't just call the existing `AddNorseContext<TContext>()` directly: that extension registers via `AddDbContextPool<TContext>` (direct pooled `TContext` injection), but Midgard's `Repository<TContext,TEntity,TView>` needs `IDbContextFactory<TContext>` (create-execute-dispose per operation, per the well-and-wire spec's own repository design) — a different DI shape `AddNorseContext<TContext>()` doesn't provide today.

## 2. Decision

**Add the one missing piece to Urðarbrunnr's existing provider seam — a factory-shaped sibling of `AddNorseContext<TContext>()` — then have a thin Midgard entry point call it plus the existing well-discovery logic.** No new mechanism is invented at the seam layer; `ApplyNorseProviderOptions` already does the unification work. The gap is that nothing today calls it with the DI shape `Repository<T>` actually needs.

### 2.1 Approaches considered

**A — Add `AddNorseContextFactory<TContext>()` beside the existing `AddNorseContext<TContext>()` in Urðarbrunnr; Midgard's well-composition wraps it (chosen).** `NorseContextExtensions.cs` (Urðarbrunnr, `Persistence.EntityFramework`) already has `AddNorseContext<TContext>(INorseEfProvider provider, string connectionStringName)`, built on `AddDbContextPool<TContext>` + `ApplyNorseProviderOptions` + `provider.Enrich<TContext>`. It registers a pooled, directly-injectable `TContext` — the wrong DI shape for `Repository<T>`, which needs `IDbContextFactory<TContext>`. The fix is a sibling extension in the same file, same pattern, swapping `AddDbContextPool<TContext>` for `AddPooledDbContextFactory<TContext>` — same provider seam, same enrichment call, same signature shape, five lines different. Midgard's well-composition (a thin `AddNorseWell<TContext>(INorseEfProvider provider, string connectionStringName)` extension, or a new overload of the existing `AddWell<TContext>()`) calls this new Urðarbrunnr extension, then performs the well/repository discovery it already does. Provider passed explicitly, mirroring `AddNorseMigrationContext`'s existing signature exactly — no connection-string sniffing, no guessing.

**B — Analyzer banning EF references from `Hosting.Web.Server`/`Hosting.Worker` (rejected).** Only catches the symptom in Yggdrasil's own two projects. Someone could still hand-roll independently-drifting EF options one layer up (`Reference.Web.Server`, `Identity.Web.Server`) and the analyzer would never see it. Doesn't close the gap that `ApplyNorseProviderOptions` exists specifically to close — it just polices people for not using it.

**C — A dedicated new "composition" project per realm for provider wiring (rejected).** Solves both problems but invents a third project per realm to do something Urðarbrunnr's existing seam plus Midgard's existing well-discovery can already absorb between them with one small addition. More ceremony for the same outcome.

### 2.2 Why this is the same fix, not two fixes

The drift risk (§1.3) and the lock-in risk (§1.2) collapse into one mechanism once every realm's runtime composition calls the same factory-shaped Urðarbrunnr extension `AddNorseMigrationContext` already calls the pool-shaped version of: no second hand-maintained copy is left to drift, and no EF-aware code is left in any project between Midgard/Urðarbrunnr and the application host.

## 3. Design

### 3.1 `AddNorseContextFactory<TContext>(INorseEfProvider provider, string connectionStringName)` — Urðarbrunnr, `Persistence.EntityFramework`

New sibling of the existing `AddNorseContext<TContext>()` in `NorseContextExtensions.cs`, same file, same pattern:

```csharp
public IHostApplicationBuilder AddNorseContextFactory<TContext>(INorseEfProvider provider,
	string connectionStringName)
	where TContext : DbContext, INorseDbContext
{
	var connectionString = builder.Configuration.GetConnectionString(connectionStringName) ??
		throw new InvalidOperationException(
			$"Connection string '{connectionStringName}' was not found.");

	builder.Services.AddPooledDbContextFactory<TContext>(opts =>
		opts.ApplyNorseProviderOptions(provider, connectionString, migrationsAssemblyName: null));
	provider.Enrich<TContext>(builder);

	return builder;
}
```

Only the registration call changes (`AddDbContextPool` → `AddPooledDbContextFactory`) — same provider seam, same enrichment, same failure-on-missing-connection-string behavior as its existing sibling.

### 3.2 `AddNorseWell<TContext>(INorseEfProvider provider, string connectionStringName)` — Midgard, `Infrastructure.Persistence.EntityFramework`

Thin: calls `AddNorseContextFactory<TContext>()` (above), then performs the well/repository discovery `AddWell<TContext>()` already does (reflecting `DbSet<TEntity>` properties, registering `IReadRepository<TView>` per discovered well) — one call instead of two a caller has to remember to chain in the right order. `AddWell<TContext>()` itself is unchanged; this is a new entry point that composes it with context registration, not a rewrite.

`Reference.Web.Server`'s `AddNorseReferenceService` (and Himinbjörg's `AddNorseAuthenticationService`, pending confirmation during planning) become thin: receive a provider instance + connection-string name from their own caller, pass both straight to `AddNorseWell<TContext>()`, register the handler/service wiring that's actually specific to that realm. Neither project needs `Microsoft.EntityFrameworkCore`/`Npgsql.EntityFrameworkCore.PostgreSQL` as a package reference afterward.

Host `Program.cs` (Yggdrasil) is the one place that decides which `INorseEfProvider` to use per environment — it already effectively makes this decision via connection-string configuration today; this makes the decision explicit and typed instead of implicit in which `Use*` call happened to get hand-written.

### 3.3 Migration-time path — unchanged, now provably identical

`NorseReferenceMigrationContributor` → `EfMigrationContributor<TContext>` → `AddNorseMigrationContext<TContext>()` → `ApplyNorseProviderOptions` continues exactly as it is. No change here — the point is that runtime now converges on this same call (§3.1/§3.2), not that migration-time needs to change to match runtime.

### 3.4 Seeding — unchanged, already correct

`ISeedContributor` (Asgard) already gets its typed `DbContext` via constructor DI, already discovered at compile time by Urðarbrunnr's generator, already run by `SeedRunnerService` after `MigrationRunnerService` completes within `Hosting.Migrations.Service`. This spec does not touch seeding — it was already the right shape (confirmed during design research against real prior art in a separate codebase, which — despite motivating this investigation — turned out to have a *weaker* version of what this platform already ships: ad-hoc per-context static seeders with a `TODO` wishing for the interface abstraction Norse already has).

### 3.5 The E2E fixture fix (the original CI failure)

`Hosting.Web.Server.Tests`' fixture stops directly invoking `NorseReferenceMigrationContributor`/`ReferenceDataSeedContributor`. It instead stands up the real `Hosting.Migrations.Service` composition (`AddNorseMigrations()`) against the Testcontainers Postgres instance — the actual production mechanism, migrate-then-seed, run for real — then points the `TestServer`-hosted `Hosting.Web.Server` composition at the now-populated database. Once `Reference.Web.Server` no longer references EF directly (§3.1/§3.2), the test project structurally has nothing left to reach for in Mimisbrunnr or Urðarbrunnr even if someone wanted to — the only path left is composing the real migrations service, which is exactly the three-realm law working as intended.

## 4. Testing

1. **Construction parity test:** assert migration-time-constructed and `AddNorseWell`-constructed contexts produce the identical model (same resolved table names, same conventions) — the actual proof the drift is closed, not just relocated.
2. **The assembly-boundary regression test (the adversarial case):** assert `Hosting.Web.Server`'s and `Hosting.Worker`'s compiled output carries no `Microsoft.EntityFrameworkCore` reference, transitively. This is the wall: if someone reintroduces a direct EF reference anywhere in that graph, this test fails loudly rather than relying on anyone remembering the rule in review.
3. **Corrected E2E fixture** (§3.5): real `Hosting.Migrations.Service` composition seeds the container; `Hosting.Web.Server` test host connects to an already-populated database. No direct Mimisbrunnr/Urðarbrunnr references anywhere in `Hosting.Web.Server.Tests`.
4. Existing `ReferenceDbContextFactoryTests`-style design-time tests (both providers) continue to prove table-name/model-shape correctness independent of a live database — unaffected by this change, still green.

## 5. Out of scope

- The `YGG004` analyzer (no `DbContext` injection in services / no cross-context entity references) — documented platform law, currently unenforced by any compiler diagnostic (`Glitnir/docs/Platform/specs/2026-05-19-architecture-analyzers-design.md`, status "Draft for review"). This spec's assembly-boundary test (§4.2) is a narrow, hand-written proof for this one boundary — it is not a substitute for that broader analyzer effort, which remains its own future work.
- Whether Himinbjörg's `AddNorseAuthenticationService` follows the identical hand-rolled-EF shape as Mimir's is assumed, not yet confirmed against source — first step of the implementation plan.
- SqlServer provider parity for `AddNorseWell<TContext>()` — the design covers both providers symmetrically via `INorseEfProvider`, but only the Postgres path has a live consumer today; SqlServer verification rides along whenever a realm actually needs it, consistent with the platform's existing "prove it against a real consumer" discipline.

## 6. Why this matters beyond tonight's CI failure

This is the same law the platform has applied at every other altitude this session, applied one level further down: two things that rhyme (migration-time construction, runtime construction) must never be allowed to drift into "basically the same," and the fewer legal ways there are to do something, the fewer ways there are to do it wrong. A platform whose own hosting layer cannot construct a database connection without knowing which ORM it's using is not a platform anyone can safely build a second persistence strategy on top of — this closes that gap the same way `IReadRepository<TView>` already closed it for reads.
