# `Norse.EntityFramework` — Concrete DbContext Provenance (Decision Inputs)

**Date:** 2026-06-11
**Status:** Open — inputs for a future design session, not a verdict
**Owner:** Buvy

## Ruled now (2026-06-11)

**The abstract DbContext — the one that does the stamping and enforces the laws — belongs to `Norse.EntityFramework` (Urdarbrunnr).** Audit stamping, convention enforcement at model finalize, and the invariants every context inherits are realm law there, not Infrastructure code. CLAUDE.md §4 → Persistence carries the pointer.

## The open question

Does a consuming service ever declare a concrete `DbContext` that inherits the base — or can Midgard (`Norse.Infrastructure`) materialize one on the service's behalf, with Asgard's repository contracts (`ICommandRepository<T>` / `ICachedRepository<T>` / `ITemporalRepository<T>` / `IDocumentRepository<T>`) as the only surface a service touches? In the ideal world the downstream service declares **no context at all**; if a concrete one must exist, perhaps it is generated.

## Forces (why this is not free-form preference)

1. **EF's per-type machinery.** The model cache is keyed by context CLR type, `DbContextPool` is per-type, and migration snapshots bind to a context type. "No declared context" therefore has exactly two honest mechanisms — distinct types nobody hand-writes, or one shared type with a custom `IModelCacheKeyFactory` and per-service model building.
2. **Migrations law.** Migrations live in `{Company}.{Context}.Migrations`, applied by a deployment job. Whatever produces the concrete context must cooperate with `dotnet ef` design time (`IDesignTimeDbContextFactory`, `MigrationsAssembly`). Source-generated types exist at design time — the design-time build runs the generator — so generation does not break the chassis.
3. **Provider variance.** A fork may run SqlServer where the products run Postgres. The abstract base carries only provider-agnostic law (stamping via `SaveChanges` interceptors — not virtual overrides an inheritor could skip — enum explicit-value enforcement, MaxLength law, temporal hooks); naming and type-mapping conventions (snake_case, `jsonb`) belong to provider packs (`Norse.EntityFramework.Postgres` first; others when a real consumer forces them).
4. **House law.** Compile-time over runtime; no reflection in hot paths; least accessibility; the wrong path must not compile. A `YGG` rule refusing hand-authored `DbContext` subclasses outside generated code is the natural enforcement once provenance is ruled.
5. **Existing platform law (unchanged by this question):** services never inject a DbContext or call `SaveChangesAsync`; SQL entities live solely in `.Worker`; `IEntityTypeConfiguration<T>` impls live in `.Worker`; the NSB per-handler session owns the transaction (no `IUnitOfWork`).

## Candidate shapes

- **A. Hand-declared marker subclass** *(status quo shape)* — `sealed class BillingDbContext : NorseDbContext` with an empty body. Cheap, boring; still ceremony, still a thing a hurried developer can pollute.
- **B. Source-generated concrete context** — the `.Worker` declares one assembly-level marker (e.g. `[NorseDbContext]`) plus its `IEntityTypeConfiguration<T>` set; a generator in Urdarbrunnr emits the sealed context. Zero authored context code; EF's per-type machinery fully satisfied; analyzer-enforceable. *(Leading candidate.)*
- **C. Runtime-generic context** — Midgard registers `NorseDbContext` instances per service with `IModelCacheKeyFactory` discrimination. No types anywhere, but migrations and pooling need bespoke care, and enforcement moves from compile time to runtime — against the grain of §2.7.

## To pressure-test before ruling "Asgard interfaces suffice"

1. **Aggregate-graph loading** — the repository contract must express aggregate-root loading policy honestly; leaking `IQueryable` is refused (it reintroduces runtime guessing as API).
2. **Bulk operations** — `ExecuteUpdate` / `ExecuteDelete` shapes, or an explicit decision that command handlers don't bulk-mutate.
3. **Design-time authorship** — who runs `dotnet ef migrations add` against what, when the context is generated and the entities live in `.Worker`.

## Boundary restated (tables already in sync)

Urdarbrunnr = EF *foundations*: abstract law-bearing context, entity base types, conventions, value converters, migrations chassis. Midgard = everything that *uses* a context: repository implementations, registration/pooling, the runtime. Asgard = the only contracts a service sees. The open question above decides only where the concrete context *comes from* — never who touches it (nobody downstream, by construction).
