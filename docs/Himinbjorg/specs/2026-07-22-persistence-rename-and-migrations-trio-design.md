# Himinbjörg: Persistence Rename Uptake + Migrations Trio Breakout

## Context

Himinbjörg has drifted out of sync with the rest of the platform while `feature/identity-web-server` sat unpushed. Two things moved underneath it:

- Urðarbrunnr's `Norse.EntityFramework.*` → `Norse.Persistence.EntityFramework.*` widening merged to `master` (PR #31, 2026-07-22), renaming the projects Himinbjörg's `NorseRef`s point at.
- Mímisbrunnr shipped a clean single-provider → dual-provider migrations split (`Reference.Data.Migrations` + `.PostgreSQL` + `.SqlServer`), the first realm to prove that shape out. Himinbjörg's `Identity.Migrations` is still one Postgres-only project.

Confirmed by `dotnet build`: Himinbjörg's `Identity.csproj` fails today with 26 errors — `Urdarbrunnr/src/EntityFramework/EntityFramework.csproj` no longer exists. Nothing in this repo builds until the rename lands.

This spec covers exactly three items, in dependency order, all on `feature/identity-web-server`:

1. Urðarbrunnr rename uptake (unblocks the build)
2. Migrations trio breakout, using Mímisbrunnr as prior art
3. `IDeferredSignIn` — already correctly implemented; carried along for verification, not new work

**Completion criterion:** repo builds clean (`Identity`, `Identity.Migrations` + `.PostgreSQL` + `.SqlServer`, `Identity.Web.Server`), all test projects pass, migrations are regenerated and reviewed on both providers, and all Himinbjörg packages ship to NuGet. Standard ship gate (PR merged, CI green, tagged, published) applies — same discipline as the original migrations framework rollout.

## Out of Scope (explicitly deferred)

- **Yggdrasil's stale `NorseRef`s** — `Hosting.Migrations.Service.csproj` references `EntityFramework.Migrations.PostgreSQL` (no longer exists) and its `Directory.Packages.props` pins `Norse.EntityFramework.Migrations.PostgreSQL` / `Norse.Identity.Migrations` / `Norse.ReferenceData.Data.Migrations` — all stale against current Urðarbrunnr/Mímisbrunnr package names. Separate follow-up, tracked but not touched here.
- **Yggdrasil's direct ASP.NET Identity references** — excising these is the next spec, after this one ships to NuGet.
- **gRPC service / Blazor components / Mediator / validation hardening end-to-end** — comes after Yggdrasil is clean.
- **New features** — after all of the above.

## Task 1: Urðarbrunnr Rename Uptake

One `NorseRef` edit (the other stale reference, `Identity.Migrations.csproj`'s, is folded into Task 2 since that file is being restructured anyway):

| File | `NorseRef` change |
|---|---|
| `src/Identity/Identity.csproj` | `EntityFramework` → `Persistence.EntityFramework` |

## Task 2: Migrations Trio Breakout

Mirrors Mímisbrunnr's `Reference.Data.Migrations*` shape exactly.

### Project shape

| Project | Contents | `NorseRef` | Packages |
|---|---|---|---|
| `Identity.Migrations` (base) | `NorseIdentityMigrationContributor`, provider-agnostic | `Persistence.EntityFramework.Design` (Urðarbrunnr) | none EF-provider-specific |
| `Identity.Migrations.PostgreSQL` | `DesignTimeServices`, `NorseIdentityDbContextFactory`, `Migrations/`, embedded `schema/*.sql` → `CreateScript.sql` logical name | `Persistence.EntityFramework.Design.PostgreSQL` | `Microsoft.EntityFrameworkCore.Design` `11.*-*` |
| `Identity.Migrations.SqlServer` | same shape, SQL Server-targeted | `Persistence.EntityFramework.Design.SqlServer` | `Microsoft.EntityFrameworkCore.Design` `11.*-*` |

Both provider projects `ProjectReference` the base `Identity.Migrations`. The `Npgsql.EntityFrameworkCore.PostgreSQL` package reference in today's single `Identity.Migrations.csproj` is removed — the provider comes entirely through the `NorseRef`'d `Persistence.EntityFramework.Design.{Provider}` generator, matching Mímisbrunnr, not a direct package pull.

### Package versioning convention (confirmed, platform-wide, not new)

Realm repos (Himinbjörg included) float 3rd-party package versions to the major version only (`11.*-*` for EF-family packages — plain `*` is wrong, doesn't reliably resolve preview builds). Exact-version pinning happens exclusively at Yggdrasil's `Directory.Packages.props` (CPM), which composes the deployable. This is already how Yggdrasil pins `Grpc.*` and `Microsoft.AspNetCore.*` today — no new convention, just applying it consistently here.

### Migrations

The existing single Postgres migration (`20260703060347_InitialCreate`) is deleted, not carried forward — consistent with the platform's "exactly one EF migration per realm until provider-defaults settle" convention. Fresh `InitialCreate` migrations get regenerated under `Identity.Migrations.PostgreSQL` and `Identity.Migrations.SqlServer` once the project shape is in place. Buvy reviews the generated SQL on both sides (Postgres and SQL Server) before this ships.

### Addendum (2026-07-23, decided live during implementation): table/index naming

Not part of the original design — added to the implementation plan as its own task (Task 3) once execution surfaced the underlying problem. Every core ASP.NET Identity entity either inherited ASP.NET's own `AspNet`-prefixed default table name or had a hardcoded lowercase snake_case name baked directly into the shared, provider-agnostic entity configuration — both wrong once a second provider with different casing conventions enters the picture. Decided: strip the `AspNet` prefix entirely and use `AspNet`-free PascalCase (`Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `UserTokens`, `RoleClaims`, `UserPasskeys`) at the canonical layer, with no hardcoded casing baked in. Each provider's own naming convention then does the casing — Postgres's existing `NorseSnakeCaseNamingConvention` rewrites to `snake_case` (`users`, `user_passkeys`, ...), SQL Server's default (no conversion) leaves it PascalCase. Same rule for explicit index `.HasDatabaseName(...)` overrides — dropped in favor of EF's own default `IX_{Table}_{Property}` naming, which the same per-provider convention then handles identically. This also resolves a genuine pre-existing defect Task 2 found live (the passkey table's checked-in migration DDL disagreed with what the current model actually produced) rather than papering over it.

### Test reorg

One test project per shipped NuGet package, no exceptions — a failure names the exact library to jump into, with no guessing whether it's a transitive dependency. This is also mechanical, not just preference: `src/Directory.Build.props` hoists `<InternalsVisibleTo Include="$(AssemblyName).Tests" />`, which only grants internals access to a test assembly literally named `{AssemblyName}.Tests` — a test project spanning multiple src projects can only ever match one of them correctly.

(Mímisbrunnr's `Reference.Data.Tests` doesn't follow this — it's a single test project wired via `ProjectReference` to all three `Reference.Data.Migrations*` src projects, with no dedicated factory test at all. Not mirrored here; this convention wins over that prior art, and is worth revisiting on Mímisbrunnr too once the planned platform-wide coverage spike reaches it.)

- `NorseIdentityMigrationContributorTests.cs` stays in `Identity.Migrations.Tests`, now testing the provider-agnostic base contributor, `ProjectReference` updated to point at the (now-slimmer) base `Identity.Migrations.csproj`.
- `NorseIdentityDbContextFactoryTests.cs` moves into a new `Identity.Migrations.PostgreSQL.Tests` project, `ProjectReference`d to `Identity.Migrations.PostgreSQL`.
- A new `Identity.Migrations.SqlServer.Tests` project is added for symmetry, with its own `NorseIdentityDbContextFactoryTests.cs` against `Identity.Migrations.SqlServer`.

```
tests/
  Identity.Migrations.Tests/                 <- base contributor test stays
    NorseIdentityMigrationContributorTests.cs
  Identity.Migrations.PostgreSQL.Tests/       <- new
    NorseIdentityDbContextFactoryTests.cs
  Identity.Migrations.SqlServer.Tests/        <- new
    NorseIdentityDbContextFactoryTests.cs
```

## Task 3: IDeferredSignIn (verification only)

`NorseSignInManager.cs` already correctly consumes `Norse.Abstractions.Web.Server.DeferredSignIn.IDeferredSignIn` from Asgard — no code changes needed. Once Tasks 1–2 land and the build is green again, confirm `Identity.Web.Server` still compiles and its tests still pass. This item is a checkpoint, not a task.

## Dependency Graph (confirmed current state, for the record)

- `Identity.csproj`, `Identity.Migrations.csproj` (+ new `.PostgreSQL`/`.SqlServer`) → Urðarbrunnr only.
- `Identity.Web.Server.csproj` → Asgard (`Abstractions.Web.Server`, `Abstractions.Components`) + Heimdall (`AuthN.Components`, `AuthN.Components.FluentUI`) + Midgard (`Infrastructure.Web.Server`, for `Outcome<T>` failure decomposition in the gRPC forwarder — deliberate, per the project's own doc comment, not something to excise).

## Branch

All of the above lands on `feature/identity-web-server` (already unpushed, already carries the Phase 2 web-server work and the correct `IDeferredSignIn` usage). One PR takes the whole thing — rename, trio, verification — to `master` together.
