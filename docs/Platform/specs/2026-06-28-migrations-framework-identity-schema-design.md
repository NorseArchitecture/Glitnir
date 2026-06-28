# Migrations Framework & Identity Schema Foundation

**Date:** 2026-06-28
**Status:** Approved design, ready for planning
**Owner:** Buvy
**Supersedes (on implementation):**
- `2026-06-07-auth-design.md` §10 — Mongo identity SoR retired; Postgres/EF is the identity store
- CLAUDE.md §4 → Auth "Mongo as identity SoR" — the 2026-06-16 pivot verdict is now formally in force
**Companion specs:**
- `2026-06-16-postgres-document-store-decision-inputs.md` — the Mongo cull and Postgres pivot that gates this work; its verdict is treated as landed here
- `2026-06-16-apphost-postgres-replica-design.md` — the primary+replica AppHost topology this spec extends
- `2026-06-11-entityframework-context-provenance-decision-inputs.md` — EF context generation machinery; this spec adds the Auth exception to its server-tier wall
- `2026-06-27-yggdrasil-runtime-scaffold-design.md` — the Yggdrasil scaffold whose migrations service stub this spec replaces

---

## 0. Why This Comes First

Before any application feature can run — web server, worker, domain logic — the database schema must exist. The migrations service is the init container: it runs to completion before Yggdrasil is permitted to start anything else.

This spec establishes the migrations framework and its first concrete consumer: ASP.NET Core Identity v3 + OpenIddict running on Postgres via Himinbjörg (`Norse.Identity`). Identity is the proving vehicle by design — its entities are well-known (no synthesis required), inheriting from framework base classes forces the EF foundation to handle the brownfield onramp case from day one, and the end state is a verifiable, live Postgres database with a real production-grade schema.

The blast radius is large and deliberate: Bifrost, Asgard, Urdarbrunnr, Himinbjörg, Midgard, and Yggdrasil all move. Each realm delivers exactly its slice of the law; nothing bleeds across the boundary.

---

## 1. Decisions in Force

### 1.1 Mongo fully out — Postgres exclusive

The formal verdict of `2026-06-16-postgres-document-store-decision-inputs.md` is now in force. MongoDB is culled entirely. Postgres serves three roles:

| Role | Tier | Mechanism |
|---|---|---|
| Source of truth | Worker only | EF Core, generated `file`-scoped context |
| Operational read store | Server read path | Source-gen expression walker over raw Npgsql, against the replica |
| Identity store | Server auth path | ASP.NET Identity + OpenIddict EF stores, against the primary |

The "Mongo as identity SoR" ruling of `2026-06-07-auth-design.md` §10 is retired. All auth entities live in Postgres under the `norse_identity` database (§1.2).

### 1.2 Separate databases per context — no schema-per-context sharing

Each bounded context gets its own **Postgres database** on the shared primary server. Schema-per-context within a shared database is explicitly rejected: a database boundary is a wall; a schema boundary is a convention. A developer can trivially cross schemas with a JOIN; crossing databases requires `dblink` or `postgres_fdw` — explicit, detectable, and violating.

**Convention:** `norse_{context}` — `norse_identity` for Himinbjörg, `norse_policy` for a future Policy context, etc. Each contributor connects to its own database via its own connection string, injected via DI. No contributor knows another database exists. Cross-context reporting belongs to the Snowflake data warehouse (future), not cross-database queries.

### 1.3 Auth is the one server-tier EF exception

`Norse.Auth.Server` is the **single** `.Server`-tier project that references EF Core and owns a `DbContext` directly. Credential verification is synchronous; a queue cannot sit in the login path. The server-tier wall is redefined but not abandoned: **no EF and no source-of-truth access in `.Server`** — with Auth as the single named, documented exception. Every other `.Server` project remains governed by the original wall.

---

## 2. The Migrations Framework

### 2.1 `IMigrationContributor` — Asgard (`Norse.Abstractions.Migrations`)

```csharp
interface IMigrationContributor
{
    string Name { get; }
    Task MigrateAsync(CancellationToken cancellationToken);
}
```

**No `Order`. No `DependsOn`.** Ordering between contributors implies coupling between contexts; coupling between contexts is forbidden by platform law (§1.2). Contributors run in any order — or in parallel — because they are physically incapable of seeing each other's data. Each contributor is fully self-contained: connection string, `DbContext`, and all dependencies are resolved via DI. The interface carries no infrastructure concern.

Failure is the exception path, not a return value. A contributor that cannot migrate throws; the runner halts immediately.

### 2.2 `EfMigrationContributor<TContext>` — Urdarbrunnr (`Norse.EntityFramework.Migrations`)

The EF-specific implementation base lives in **`Norse.EntityFramework.Migrations`**, a separate class library within Urdarbrunnr from the runtime `Norse.EntityFramework` base. This split is load-bearing: `Norse.EntityFramework` is what the Worker and any runtime project references; `Norse.EntityFramework.Migrations` is what the migrations service and realm `.Migrations` projects reference. The wrong path does not compile — `context.Database.MigrateAsync` is unreachable from a runtime container because the assembly that exposes it is not in scope.

```csharp
abstract class EfMigrationContributor<TContext>(TContext context) : IMigrationContributor
    where TContext : DbContext
{
    public abstract string Name { get; }

    public Task MigrateAsync(CancellationToken cancellationToken) =>
        context.Database.MigrateAsync(cancellationToken);
}
```

Non-EF contributors (Dapper, raw SQL) implement `IMigrationContributor` from Asgard directly — no dependency on either Urdarbrunnr assembly.

### 2.3 Source-generated contributor registration

A Roslyn source generator in Urdarbrunnr discovers every `IMigrationContributor` implementation visible in the compilation of the consuming migrations service project and emits a `AddNorseMigrations()` extension method into that project. The generated method registers all discovered contributors in DI and calls `AddNorseMigrationsRunner()` from Midgard.

**Verification gate — `UseProjectReferences` toggle:** The generator must produce identical output whether contributor packages arrive as `ProjectReference` (Bifrost dev mode, submodule) or `PackageReference` (NuGet / CI mode). The toggle must be exercised in both states as an explicit done criterion. A generator that walks source trees rather than compiled symbols breaks the NuGet path silently; the verification gate catches this before CI surfaces it.

### 2.4 `MigrationRunnerService` — Midgard (`Norse.Infrastructure`)

An `IHostedService` that resolves all registered `IMigrationContributor` implementations, calls `MigrateAsync` on each, and calls `IHostApplicationLifetime.StopApplication()` on completion. Any failure stops execution immediately and exits non-zero — no swallowed exceptions, no partial migration, no silent fallback.

Midgard exposes `AddNorseMigrationsRunner()` as the infrastructure extension the generated method calls internally.

### 2.5 Migrations service `Program.cs` — Yggdrasil (`Norse.Hosting.Migrations.Service`)

The existing stub is replaced:

```csharp
Console.Title = "Norse Migrations Service";
var builder = Host.CreateApplicationBuilder(args);
builder.AddNorseMigrations();
await builder.Build().RunAsync();
```

`AddNorseMigrations()` is the source-generated extension. It registers all discovered contributors and wires the runner. `Program.cs` never changes regardless of how many contexts join the platform — it has no knowledge of Identity, OpenIddict, or any contributor type.

---

## 3. Himinbjörg — Identity Schema (`Norse.Identity`)

### 3.1 Entity types

Identity's fully-generic store requires all supporting types to be declared explicitly. Each is a sealed class inheriting its framework base with `Guid` as the key — no added properties.

```csharp
sealed class NorseUser       : IdentityUser<Guid>;
sealed class NorseRole       : IdentityRole<Guid>;
sealed class NorseUserClaim  : IdentityUserClaim<Guid>;
sealed class NorseUserRole   : IdentityUserRole<Guid>;
sealed class NorseUserLogin  : IdentityUserLogin<Guid>;
sealed class NorseUserToken  : IdentityUserToken<Guid>;
sealed class NorseRoleClaim  : IdentityRoleClaim<Guid>;
sealed class NorseUserPasskey : IdentityUserPasskey<Guid>;
```

**No additional properties on any type at this stage.** The platform's principle: do not pollute reference entities with speculative Norse-specific fields that adopters must sift through. If platform-level properties are required on `NorseUser` in the future, the upgrade path is:

```csharp
abstract class NorseUser<T> : IdentityUser<T>;
sealed class NorseUser : NorseUser<Guid>;
```

Brownfield adopters who already have `ApplicationUser : IdentityUser<Guid>` extend the abstract base when it exists; they never depend on the concrete reference type. The abstract intermediary is introduced only when there is a real property to put on it — not now.

The full machinery is generic over `TUser where TUser : IdentityUser<TKey>`. A brownfield team plugs in their own entity types, their own `DbContext` subclass, and their own contributor — the framework accepts all of it.

### 3.2 `NorseUserStore`

A custom `UserStore` inheriting the fully-generic base, registered in place of the default Identity store. Its authoring discipline: **override methods to project only the fields required for the operation** — never load a full entity when the call site needs a subset of columns.

```csharp
sealed class NorseUserStore(NorseIdentityDbContext context, IdentityErrorDescriber describer)
    : UserStore<NorseUser, NorseRole, NorseIdentityDbContext, Guid,
                NorseUserClaim, NorseUserRole, NorseUserLogin,
                NorseUserToken, NorseRoleClaim, NorseUserPasskey>(context, describer)
{
    public override Task<NorseUser?> FindByIdAsync(string userId, CancellationToken ct = default)
    {
        var id = Guid.Parse(userId);
        return Users
            .Where(u => u.Id == id)
            .Select(u => new NorseUser
            {
                Id              = u.Id,
                UserName        = u.UserName,
                Email           = u.Email,
                SecurityStamp   = u.SecurityStamp,
                ConcurrencyStamp = u.ConcurrencyStamp
            })
            .SingleOrDefaultAsync(ct);
    }
}
```

`Guid.Parse(userId)` replaces any need for a type converter — Identity's `ConvertIdFromString` handles `Guid` natively, but the override exists to project rather than to fix a type problem. Each override that loads a user or role should apply the same projection discipline: emit `SELECT` only the columns the caller actually needs. No `SELECT *` via navigation property load.

`NorseUserStore` is registered alongside the rest of Identity DI: `AddIdentity<NorseUser, NorseRole>().AddUserStore<NorseUserStore>().AddEntityFrameworkStores<NorseIdentityDbContext>()`.

### 3.3 `NorseIdentityDbContext`

Extends `IdentityDbContext` using the fully-generic form so all entity types are explicit:

```csharp
sealed class NorseIdentityDbContext
    : IdentityDbContext<NorseUser, NorseRole, Guid,
                        NorseUserClaim, NorseUserRole, NorseUserLogin,
                        NorseUserToken, NorseRoleClaim, NorseUserPasskey>
```

OpenIddict stores are registered on the same context. One database (`norse_identity`), one migration history, one contributor. Auth owns it end to end.

**OpenIddict key type: `Guid` — non-negotiable.** OpenIddict's application (client) entity uses `Guid` as its primary key. This is not a cosmetic preference: in the Midgard web server, the client's database ID (`Guid`) is used as a UUIDv5 namespace and the request payload is hashed against it to produce a deterministic, idempotent request ID — the same mechanism used with ASP.NET Identity user IDs. Every partner gets built-in idempotency for free without any API surface change on their side. Letting OpenIddict default to `string` would break this namespace strategy at the schema level with no clean migration path.

OpenIddict DI registration with Guid key: `AddOpenIddict().AddCore(o => o.UseEntityFrameworkCore().UseDbContext<NorseIdentityDbContext>().ReplaceDefaultEntities<Guid>())`. In `OnModelCreating`: `builder.UseOpenIddict<Guid>()`. The OpenIddict entity tables land in `norse_identity` alongside the Identity tables.

`NorseIdentityDbContext` applies Urdarbrunnr's snake_case convention machinery to all table and column names — the framework defaults (`AspNetUsers`, `OpenIddictApplications`, etc.) are overridden to follow Norse platform naming law. The convention is applied once in `OnModelCreating`; no per-entity `ToTable()` calls.

### 3.5 The Himinbjörg split — `Norse.Identity` and `Norse.Identity.Migrations`

Himinbjörg follows the same isolation pattern as Urdarbrunnr. Two class libraries:

**`Norse.Identity`** — the runtime library. Contains all entity types, `NorseUserStore`, `NorseIdentityDbContext`, and the DI extensions wiring Identity + OpenIddict for `Norse.Auth.Server`. Referenced by `Norse.Auth.Server` at runtime. Has no knowledge of migrations or `EfMigrationContributor<TContext>`.

**`Norse.Identity.Migrations`** — the migrations-only library. Contains:
- `NorseIdentityMigrationContributor` (references `EfMigrationContributor<TContext>` from `Norse.EntityFramework.Migrations`)
- `IDesignTimeDbContextFactory<NorseIdentityDbContext>` — required by `dotnet ef` tooling to discover the context at design time without a running host
- The EF migration files scaffolded by `dotnet ef migrations add` — `Migrations/` folder with `InitialCreate.cs`, snapshot, and all future migration files

Referenced only by the migrations service. Never referenced from `Norse.Auth.Server` or any runtime container.

```csharp
sealed class NorseIdentityMigrationContributor(NorseIdentityDbContext context)
    : EfMigrationContributor<NorseIdentityDbContext>(context)
{
    public override string Name => "Norse.Identity";
}
```

Registered via the source-generated `AddNorseMigrations()` call in the migrations service. No explicit registration in Himinbjörg itself.

### 3.6 .NET 11 Identity additions

**TimeProvider:** ASP.NET Core Identity in .NET 11 uses `TimeProvider` for all time-sensitive operations — token expiration, lockout durations, security stamp validation. `TimeProvider` is registered in DI as part of the auth wiring: `SystemTimeProvider.Instance` in production, `FakeTimeProvider` in tests. Tests for time-sensitive auth behaviour (lockout, token expiry, stamp validation) are deterministic without `Thread.Sleep` or clock-mocking.

**Passkeys:** .NET 11 Identity includes passkey (FIDO2/WebAuthn) support with AAGUID-based display name inference and built-in mappings for Google Password Manager, iCloud Keychain, Windows Hello, 1Password, and Bitwarden. The passkey credential schema additions are included in the `NorseIdentityMigrationContributor` baseline — the .NET 11 Identity migration rolls them in as part of the standard schema. Passkey auth flow, enrollment UI, and `PasskeyAuthenticators` configuration are deferred to `Norse.Auth.Components` and `Norse.Auth.Server` and are out of scope for this spec.

---

## 4. Bifrost AppHost Addition

The migrations service is added to the AppHost as an Aspire project resource:

```csharp
var migrations = builder.AddProject<Projects.Hosting_Migrations_Service>("migrations")
    .WaitFor(pgPrimary);
```

The postgres primary+replica topology is unchanged from `2026-06-16-apphost-postgres-replica-design.md`. This spec adds only the migrations service project reference and its `WaitFor` dependency on the primary. Future web server and worker resources add `.WaitFor(migrations)`. The `norse_identity` database is created by EF's `MigrateAsync` when it connects to the primary — no manual `CREATE DATABASE` step.

---

## 5. Realm Responsibility Summary

| Realm | Namespace | What lands |
|---|---|---|
| Bifrost | `Norse.Orchestration` | Migrations service added to AppHost; `WaitFor(pgPrimary)` |
| Asgard | `Norse.Abstractions.Migrations` | `IMigrationContributor` interface |
| Urdarbrunnr | `Norse.EntityFramework` | Base context, conventions, value converters, entity base types — runtime projects reference this |
| Urdarbrunnr | `Norse.EntityFramework.Migrations` | `EfMigrationContributor<TContext>` base; source generator (discovery + registration emission) — migrations service and realm `.Migrations` projects only; never referenced from a runtime container |
| Himinbjörg | `Norse.Identity` | All eight entity types, `NorseUserStore` with projection overrides, `NorseIdentityDbContext` (Identity + OpenIddict) — runtime; referenced by `Norse.Auth.Server` |
| Himinbjörg | `Norse.Identity.Migrations` | `NorseIdentityMigrationContributor`, `IDesignTimeDbContextFactory<NorseIdentityDbContext>`, EF migration files (`dotnet ef migrations add` target) — migrations service only; never referenced from a runtime container |
| Midgard | `Norse.Infrastructure` | `MigrationRunnerService` (`IHostedService`); `AddNorseMigrationsRunner()` extension |
| Yggdrasil | `Norse.Hosting.Migrations.Service` | `Program.cs` stub replaced with the three-line production form |

---

## 6. Success Criterion

- Aspire dashboard shows the migrations service resource completed (exit 0) before any other resource starts.
- DataGrip connects to `localhost:5432` (primary). The `norse_identity` database exists and carries the full ASP.NET Core Identity v3 + OpenIddict + .NET 11 passkey schema with Norse snake_case table names.
- The replica at `localhost:5433` carries the same schema (streaming confirmed).
- `Norse.Hosting.Migrations.Service/Program.cs` is three lines and contains no `using` reference to `Norse.Identity`, `Microsoft.AspNetCore.Identity`, or any contributor type.
- The `UseProjectReferences` toggle is flipped to false (NuGet mode), packages restored, and the generated `AddNorseMigrations()` output is identical to the project-reference build.

---

## 7. Open Decisions

None. All load-bearing questions were resolved in the design session (2026-06-28).

---

## Self-Review

**Placeholder scan:** No TBDs, no "similar to," no incomplete sections. The snake_case convention machinery is forward-referenced as "Urdarbrunnr's convention machinery" — this is part of what Urdarbrunnr delivers in this effort, not a dependency on prior art.

**Internal consistency:** §1.2 (separate databases) aligns with §3.2 (`norse_identity`). §1.3 (Auth EF exception) aligns with §3.2 (`NorseIdentityDbContext` in `.Server`). §2.1 (no ordering) aligns with §1.2 (no cross-context coupling possible). §2.3 (source-gen) and §2.5 (three-line Program.cs) are mutually consistent. §4 (AppHost) extends `2026-06-16-apphost-postgres-replica-design.md` without contradicting it.

**Scope:** Six realms, one coherent effort. The realms are all dependencies of each other in one delivery — splitting the spec would obscure the narrative without reducing the implementation scope.

**Ambiguity:** `NorseUser : IdentityUser<Guid>` vs the abstract path is explicitly documented with the trigger condition. The `UseProjectReferences` gate is a named verification criterion, not a vague note.
