# Reference.Data.Migrations — Split into Provider-Agnostic + PostgreSQL + SqlServer

**Date:** 2026-07-22
**Status:** Approved design, ready for planning
**Owner:** Buvy

---

## 0. Why This Comes Next

Mímisbrunnr's mechanical port onto the new Urðarbrunnr (the `Persistence.EntityFramework.*` namespace widening, PR #31, and the Migrations→Design chassis rename) is done and builds clean. `src/Reference.Data.Migrations` is currently one project holding the migration contributor, the seed contributor, and a PostgreSQL-only `IDesignTimeDbContextFactory`. Urðarbrunnr itself just went through the same shape change at the chassis level — `Persistence.EntityFramework.Design` split into an agnostic base plus `.Design.PostgreSQL` and `.Design.SqlServer` siblings, each shipping its own `IDesignTimeDbContextFactory<T>` base class and Roslyn generator. Mímisbrunnr's own `.Migrations` project needs to follow the same split so it can offer a SQL Server-targeted migration path alongside the PostgreSQL one it has today, instead of being hard-wired to one provider.

Separately, when migrations were last run, Urðarbrunnr's `DdlEmittingMigrationsScaffolder` did not emit a scaffold into this project — no `Migrations/` folder exists on disk for either provider today. That is a real defect but a distinct one from this reorg; it is investigated after this split lands, not before. This design does not attempt to fix it, and the resulting provider projects are expected to start with no checked-in migrations, matching the current (broken) state.

Mímisbrunnr is the platform's reference-data template repo — "to give anyone building their own bounded context's reference-data seeding a working pattern to point at." This split is that pattern's next increment: proving the 3-project shape at the consuming-realm level, not just the chassis level.

---

## 1. Current State

`src/Reference.Data.Migrations/`:

| File | Provider-specific? |
|---|---|
| `NorseReferenceDataMigrationContributor.cs` | No — extends `EfMigrationContributor<ReferenceDataDbContext>`, calls `context.Database.MigrateAsync()` |
| `ReferenceDataSeedContributor.cs` | No — reads TSVs via `Norse.Primitives.Ingestion`, writes through EF's generic `DbSet<T>` API |
| `ReferenceDataDbContextFactory.cs` | Yes — extends `NorsePostgreSqlDesignTimeDbContextFactory<ReferenceDataDbContext>` |
| `seeds/*.tsv` (linked from `../../seeds/`) | No |
| `README.md` | — |

No test project exists for this assembly today, and nothing downstream (Yggdrasil's migrations service) references it yet — the split has no consumers to update.

---

## 2. Target Layout

Three projects, mirroring Urðarbrunnr's own `Design` / `.Design.PostgreSQL` / `.Design.SqlServer` split — including its reference shape: the provider projects each `ProjectReference` the agnostic project directly (not as independent siblings), so a downstream consumer takes exactly one provider package and gets the contributor and seed contributor transitively.

```
Reference.Data.Migrations/              (agnostic — keeps the existing project name)
  NorseReferenceDataMigrationContributor.cs
  ReferenceDataSeedContributor.cs
  seeds/*.tsv (linked, unchanged)
  README.md
  Reference.Data.Migrations.csproj
    -> ProjectReference ../Reference.Data/Reference.Data.csproj
    -> NorseRef Persistence.EntityFramework.Design (Urdarbrunnr)
    -> NorseRef Primitives, Primitives.Ingestion (Svartalfheim)
    -> IsAotCompatible=false

Reference.Data.Migrations.PostgreSQL/   (new)
  ReferenceDataDbContextFactory.cs  (: NorsePostgreSqlDesignTimeDbContextFactory<ReferenceDataDbContext>)
  README.md
  Reference.Data.Migrations.PostgreSQL.csproj
    -> ProjectReference ../Reference.Data.Migrations/Reference.Data.Migrations.csproj
    -> NorseRef Persistence.EntityFramework.Design.PostgreSQL (Urdarbrunnr)
    -> PackageReference Microsoft.EntityFrameworkCore.Design
    -> IsAotCompatible=false

Reference.Data.Migrations.SqlServer/    (new)
  ReferenceDataDbContextFactory.cs  (: NorseSqlServerDesignTimeDbContextFactory<ReferenceDataDbContext>)
  README.md
  Reference.Data.Migrations.SqlServer.csproj
    -> ProjectReference ../Reference.Data.Migrations/Reference.Data.Migrations.csproj
    -> NorseRef Persistence.EntityFramework.Design.SqlServer (Urdarbrunnr)
    -> PackageReference Microsoft.EntityFrameworkCore.Design
    -> IsAotCompatible=false
```

`Microsoft.EntityFrameworkCore.Design` moves out of the agnostic project — it's only needed for `dotnet ef` design-time tooling, which only the factories use — and into each provider project. Both new projects are brand-free (`ReferenceDataDbContextFactory.cs` in each has an identical type name; they never collide because each lives in its own assembly/namespace, `Norse.Reference.Data.Migrations.PostgreSQL` / `.SqlServer`, injected the normal way via `src/Directory.Build.props`).

---

## 3. Mechanics

- `NorseReferenceDataMigrationContributor.cs`, `ReferenceDataSeedContributor.cs`, and the `seeds/*.tsv` linkage stay in place (`git mv` not required — they don't move directories).
- `ReferenceDataDbContextFactory.cs` moves (`git mv`) from `Reference.Data.Migrations/` into the new `Reference.Data.Migrations.PostgreSQL/` folder, unchanged in content.
- `Reference.Data.Migrations.SqlServer/ReferenceDataDbContextFactory.cs` is authored fresh, mirroring the PostgreSQL factory against `NorseSqlServerDesignTimeDbContextFactory<ReferenceDataDbContext>`.
- Both new projects are added to `Mimisbrunnr.slnx` under `/src/`, alongside the existing two.
- `Reference.Data.Migrations/README.md` is rewritten to describe only the contributor + seed contributor it now holds; each new project gets its own `README.md`, matching Urðarbrunnr's per-project README convention.
- No changes to `Reference.Data.csproj`, `Reference.Data.Tests`, `SeedTool`, or `SeedTool.Tests` — none of them reference the Migrations project.

---

## 4. Explicitly Out of Scope

- **The scaffold-emission defect.** Neither provider project gets a checked-in `Migrations/` folder as part of this work — that requires first understanding why `DdlEmittingMigrationsScaffolder` didn't fire, which is a separate investigation after this split lands.
- **Standing up a SQL Server container in Bifröst's AppHost.** Not needed for this split — `dotnet ef migrations add` only requires a design-time factory, not a live database — and stands as its own already-tracked open decision (Bifröst CLAUDE.md §8).
- **Any downstream wiring** (Yggdrasil migrations service referencing either provider project, `MigrationsAssembly` configuration, runtime provider selection). Nothing references `Reference.Data.Migrations` today, so there is nothing to update.
- **A test project for the split assemblies.** None exists today; this is a structural reorg, not new test surface.

---

## 5. Testing

No test changes. This is a mechanical reorganization of existing, already-compiling code — the acceptance bar is `dotnet build` clean across all three new/changed projects and the existing `Reference.Data.Tests` suite still green.
