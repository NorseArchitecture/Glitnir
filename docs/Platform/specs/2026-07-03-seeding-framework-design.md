# Seeding Framework — a Second Phase for the Migrations Realm

**Date:** 2026-07-03
**Status:** Approved design, ready for planning
**Owner:** Buvy
**Companion specs:**
- `2026-06-28-migrations-framework-identity-schema-design.md` — the migrations framework this spec extends. Every decision there (no `Order`/`DependsOn`, separate database per context, fail-loud runner semantics, `UseProjectReferences` generator verification gate) applies unchanged to seeding.

---

## 0. Why This Comes Next

The migrations framework proved the platform can stand up schema with zero manual steps. Schema alone doesn't make a context usable — Identity needs a default role and an admin account to exist, OpenIddict needs at least one registered client and scope, before anything above the database can do useful work. That's data bootstrap, not schema bootstrap, and it needs the same discipline: no coupling between contexts, no silent partial state, no hand-run scripts.

**Seeding is not a new realm-of-concern — it is a second phase of the same migrations realm.** Every artifact this spec introduces lands inside an already-existing `.Migrations` project. No new assemblies, no new Yggdrasil project, no new AppHost resource. The migrations service becomes a two-phase init container: migrate everything, then seed everything, then stop.

This spec delivers the chassis only — the contract, the generator support, and the runner. It deliberately does **not** include Himinbjörg's concrete Identity/OpenIddict seed data; that work is blocked on the Himinbjörg decomposition thread (opt-in Identity/OpenIddict split) and is tracked as a separate, later spec so it isn't written against a shape that's about to change. This spec is exercised by test-only stub contributors, the same way `IMigrationContributor` shipped and was tested before Identity existed to consume it.

---

## 1. Decisions in Force

### 1.1 Seeding lives inside the migrations realm — no new assemblies

Every layer gains new types inside its existing `.Migrations` project. Nothing new is created:

| Realm | Existing project | What it gains |
|---|---|---|
| Asgard | `Abstractions.Migrations` | `ISeedContributor`, `DeterministicGuid` |
| Urdarbrunnr | `EntityFramework.Migrations.PostgreSQL.Generator` | Discovery of `ISeedContributor` alongside `EfMigrationContributor<TContext>`; emits the seed-phase registration into the same generated `AddNorseMigrations()` |
| Midgard | `Infrastructure.Migrations` | `SeedRunnerService` |
| Yggdrasil | `Hosting.Migrations.Service` | Nothing — `Program.cs` stays exactly three lines |

The isolation rule that already governs `IMigrationContributor` and `EfMigrationContributor<TContext>` extends unchanged to seeding: none of this is referenced by `Norse.Abstractions.Worker`, `Norse.Abstractions.Web.Server`, or any runtime container. Reachable only from the migrations pathway.

### 1.2 Fail-loud, both phases

Migrate and seed, or blow up. Any contributor — migration or seed — that throws halts the host immediately and exits non-zero. No swallowed exceptions, no partial migration, no partial seed, no silent fallback. This is the existing `MigrationRunnerService` contract, extended verbatim to `SeedRunnerService`.

### 1.3 No `Order`, no `DependsOn` — same reasoning as migrations

Sequencing between seed contributors would imply coupling between bounded contexts, which is forbidden by platform law. A contributor that needs its own data seeded in a particular internal order (e.g. roles before role-assignments) does that ordering itself, inside its own `SeedAsync` body, against its own context. No contributor knows another context's contributor exists.

### 1.4 Idempotency is the contributor's own responsibility

There is no shared ledger table tracking "has this contributor already run." `ISeedContributor.SeedAsync` is invoked on every startup, same as `IMigrationContributor.MigrateAsync` — the contributor is expected to check before it writes (e.g. "does this role already exist?") exactly the way an idempotent upsert would. This keeps the contract as thin as `IMigrationContributor` and avoids introducing shared framework state that every seed contributor implicitly depends on.

**Recommended (not enforced) pattern: deterministic primary keys.** A seed contributor should derive its rows' `Guid` keys from a namespace `Guid` plus the row's natural business key, rather than calling `Guid.NewGuid()`, so the same seed produces the same ID in local dev, staging, and production. This makes "does this already exist?" a primary-key lookup instead of a content-comparison, and makes seeded IDs safe to reference from other seed data or from tests. `DeterministicGuid` (§2.2) is the helper for this. It is a convention, not a compiler-enforced rule — nothing stops a contributor from calling `Guid.NewGuid()` instead, the same way nothing stops a migration contributor from doing something unwise inside `MigrateAsync`. A stronger, type-enforced version of this is expected once `SequentialGuid` folds into Svartalfheim; that is explicitly out of scope here (see §5).

---

## 2. Asgard — `Norse.Abstractions.Migrations` (existing assembly)

### 2.1 `ISeedContributor`

```csharp
namespace Norse.Abstractions.Migrations.Seeding;

public interface ISeedContributor
{
    string Name { get; }

    Task SeedAsync(CancellationToken cancellationToken);

    static abstract void ConfigureServices(IServiceCollection services);
}
```

- **`Name`** — same purpose as `IMigrationContributor.Name`: identifies the contributor in logs.
- **`SeedAsync`** — the contributor's own domain logic. No base class provides an implementation, because — unlike migration (`context.Database.MigrateAsync()`) — there is no single operation every seed contributor shares. A contributor constructor-injects whatever `DbContext` the migration phase already registered for its bounded context, plus anything it declared in `ConfigureServices`.
- **`static abstract ConfigureServices(IServiceCollection services)`** — how a contributor pulls in the services its own `SeedAsync` needs (e.g. `UserManager<NorseUser>`, `RoleManager<NorseRole>`, an OpenIddict application/scope manager). Declared on the interface, colocated with the contributor's own class, so the generator can call it for every discovered contributor without any separate attribute or registrar indirection, and without `Program.cs` ever needing to know a contributor type exists. A contributor that needs nothing beyond its own `DbContext` implements this as a no-op body.

No `EfSeedContributor<TContext>` base class exists in Urdarbrunnr. There is nothing generic to hoist — a contributor's constructor takes the `TContext` type it needs directly, the same `TContext` the migration phase's `EfMigrationContributor<TContext>` already caused to be registered in DI.

### 2.2 `DeterministicGuid`

```csharp
namespace Norse.Abstractions.Migrations.Seeding;

public static class DeterministicGuid
{
    public static Guid Create(Guid @namespace, string key);
    public static Guid Create(Guid @namespace, params ReadOnlySpan<string> keyParts); // joins with '|' before hashing
}
```

A real RFC 4122 §4.3 version-5 UUID: namespace bytes + name bytes, SHA-1 hash, version and variant bits forced per the RFC. The BCL has no built-in equivalent — `Guid.CreateVersion7()` exists (time-ordered, .NET 9+) but there is no namespace-based `CreateVersion5`. This is EF-free and belongs next to `ISeedContributor`, not in Urdarbrunnr, since non-EF seed contributors (raw SQL, Dapper) benefit from it equally.

Composite business keys are pipe-joined (`"role|admin"`) before hashing — callers never hand-roll their own delimiter.

This is a stopgap. Once `SequentialGuid` folds into Svartalfheim, the plan is a type that structurally forces deterministic derivation rather than a static helper someone has to remember to call. Not attempted here — see §5.

---

## 3. Urdarbrunnr — generator extension

The existing `MigrationContributorGenerator` (in `EntityFramework.Migrations.PostgreSQL.Generator`) extends its compiled-symbol walk to also discover types implementing `ISeedContributor`, using the same `AllTypes(compilation)` traversal it already uses for `IMigrationContributor` — this is what makes the `UseProjectReferences` verification gate (ProjectReference vs PackageReference produce identical generated output) apply to seed contributors automatically, with no new gate to invent.

For each discovered `ISeedContributor` type, the generator emits, inside the same `AddNorseMigrations()` method it already builds:

```csharp
GeneratedType.ConfigureServices(builder.Services);
builder.Services.AddTransient<ISeedContributor, GeneratedType>();
```

`ConfigureServices` is called as a direct static call on the concrete generated type name — the generator knows the concrete type at compile time, so no generic constraint or reflection is needed to reach a static abstract interface member.

After registering every discovered migration and seed contributor, the generated method calls both runners:

```csharp
builder.AddNorseMigrationsRunner();
builder.AddNorseSeedingRunner();
```

`AddNorseMigrations()` keeps its existing name — seeding is part of the migrations realm, not a peer concern the method name needs to announce.

If a project has zero seed contributors, `AddNorseSeedingRunner()` is still called; `SeedRunnerService` runs with an empty contributor list and completes immediately. Symmetric with today's behavior when a project has migration contributors but nothing else.

---

## 4. Midgard — `SeedRunnerService`

Added to the existing `Infrastructure.Migrations` project, alongside `MigrationRunnerService`:

```csharp
sealed partial class SeedRunnerService(
    IEnumerable<ISeedContributor> contributors,
    IHostApplicationLifetime lifetime,
    ILogger<SeedRunnerService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(contributors.Select(c => RunAsync(c, cancellationToken))).ConfigureAwait(false);
        lifetime.StopApplication();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    // RunAsync + logging mirror MigrationRunnerService exactly
}
```

**Sequencing is structural, not coordinated.** `IHost` awaits each registered `IHostedService.StartAsync` to completion before starting the next, in registration order. `AddNorseMigrationsRunner()` registers `MigrationRunnerService` before `AddNorseSeedingRunner()` registers `SeedRunnerService`, so seeding physically cannot begin until every migration contributor has finished — no explicit phase barrier, coordinator, or shared state is needed.

**Breaking change to already-shipped code:** `MigrationRunnerService.StartAsync` currently calls `lifetime.StopApplication()` itself after its contributors finish (Task 2, shipped and tagged). That call is removed. Stopping the host becomes `SeedRunnerService`'s responsibility exclusively, since it is now always the last phase. A migrations service with no seed contributors still stops correctly — `SeedRunnerService` runs, finds nothing to do, and stops the host immediately after.

`AddNorseMigrationsRunner()` and the new `AddNorseSeedingRunner()` remain the two infrastructure extensions the generated code calls; nothing in Midgard's public surface is renamed.

---

## 5. Explicitly Out of Scope

- **Himinbjörg's concrete Identity/OpenIddict seed data** (default roles, admin account, default OpenIddict client/scopes). Depends on the Himinbjörg decomposition (opt-in Identity/OpenIddict split) landing first, so the seed contributor is written once against the final shape, not the current combined one. Tracked as a separate, later spec.
- **A type-enforced deterministic-ID struct.** `DeterministicGuid` is a static helper by convention today. The stronger version — a type that makes `Guid.NewGuid()` uncallable where a deterministic key is required — waits for `SequentialGuid` to fold into Svartalfheim.
- **Environment-gated seeding** (e.g. rich demo fixtures in dev, minimal required rows in production). Not raised as a requirement; every seed contributor's data is assumed required in every environment until a concrete need for environment-conditional seeding shows up.

---

## 6. Realm Responsibility Summary

| Realm | Namespace / Project | What lands |
|---|---|---|
| Asgard | `Norse.Abstractions.Migrations` (existing) | `ISeedContributor`, `DeterministicGuid` — new `Norse.Abstractions.Migrations.Seeding` namespace within the existing project |
| Urdarbrunnr | `Norse.EntityFramework.Migrations.PostgreSQL.Generator` (existing) | Discovery of `ISeedContributor`; emits `ConfigureServices` calls, seed-contributor DI registration, and `AddNorseSeedingRunner()` call into the existing generated `AddNorseMigrations()` |
| Midgard | `Norse.Infrastructure.Migrations` (existing) | `SeedRunnerService`; `AddNorseSeedingRunner()` extension; `MigrationRunnerService.StartAsync` no longer calls `StopApplication()` |
| Yggdrasil | `Norse.Hosting.Migrations.Service` | No change — `Program.cs` stays three lines |

---

## 7. Success Criterion

- A test-only stub `ISeedContributor` (mirroring `IMigrationContributorTests.StubContributor`) proves the contract in Asgard without any real consumer.
- The `UseProjectReferences` toggle produces identical generated output for a compilation containing both migration and seed contributors, in both `ProjectReference` and `PackageReference` modes.
- A migrations service with migration contributors but zero seed contributors still runs to completion and stops (regression check against the existing shipped behavior).
- A migrations service with both kinds of contributors runs every migration to completion before any seed contributor's `SeedAsync` is invoked, provable by a test that fails if a seed contributor observes a pending migration.
- A seed contributor that throws halts the host non-zero, and no seed contributor after it in the (unordered) set is guaranteed to have run — same as migrations today.

---

## Self-Review

**Placeholder scan:** No TBDs. §5 explicitly enumerates what's deferred and why, rather than leaving gaps.

**Internal consistency:** §1.1 (no new assemblies) is reflected exactly in §2–4 (every new type named to an existing project) and §6 (the summary table has no new project column). §1.3 (no ordering) is consistent with §1.1 (no cross-context knowledge) and with the migrations framework's identical §2.1 reasoning. §4's breaking change to `MigrationRunnerService` is called out explicitly rather than silently implied by the `SeedRunnerService` code sample.

**Scope:** Chassis only, by deliberate exclusion of Himinbjörg seed data (§5) — the same "prove the chassis with a stub before proving it with a hard consumer" order the migrations framework itself did not follow (it used Identity as day-one proving vehicle) but which is correct here because the hard consumer's shape is mid-flight in a separate, sequenced thread.

**Ambiguity:** `ConfigureServices` calling convention (direct static call on the concrete generated type, no generic constraint) is spelled out in §3 rather than left to be discovered at implementation time. The `SeedRunnerService` stop-ownership handoff (§4) states which existing method loses a line of code, not just which method gains one.
