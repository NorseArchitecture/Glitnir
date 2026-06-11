# `Norse.EntityFramework` — Concrete DbContext Provenance (Decision Inputs)

**Date:** 2026-06-11
**Status:** Converging — direction recorded same-day (see §Convergence); formal verdict pending a design session
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

## Convergence (recorded 2026-06-11, same session)

The direction landing — not yet a verdict, but the shape the verdict is expected to take:

1. **The entity interface forces the persistence declaration.** Declaring an entity (Asgard's entity markers) creates a build-time obligation to declare its persistence configuration — entity without configuration is a build error, configuration without entity likewise (`YGG` rule). Conformance is not reviewable behavior; it is compilability.
2. **The concrete DbContext is source-generated** (shape B) from the declared entity/configuration pairs — all configuration applied, AOT-clean. Nothing enters the database's universe that didn't conform its way in: the analyzer gates declaration, the generator only admits conforming pairs into the model, and the base context's model-finalize validation backstops both.
3. **The generated context is unreferenceable — `file`-scoped.** Emit `file sealed class {Context}DbContext : NorseDbContext`: a file-local type no other file can name, even in the same assembly. The same generated file emits the DI wiring that closes Midgard's open-generic repository implementations over it, so the only consumer of the context is code generated beside it. Not policy — unreachability by construction.
4. **A feature-complete, deliberately bounded repository surface** so the end developer never needs (and never gets) the things they shouldn't: per call — a **filter predicate**, a **projection expression**, and for list shapes a **limit** and a **starting point**.

## Repository-surface law

**Ruled 2026-06-11:**

- **Single-entity shape:** filter predicate **required**; projection **optional**.
- **List shape:** filter predicate **optional** — the whole-table sweep is a legitimate first-class case (e.g. a reference table whose values change across the board); **limit and starting point required**.
- **Projection is optional everywhere — encouraged, never forbidden.** The odds are high you really don't want `SELECT *`, and the docs say so loudly, but the opportunity to materialize full entities stays open (the sweep case requires it). Guidance, not law.

**Still open for the design session:**

1. **Starting-point mechanics** — keyset (after-key seek) vs offset; corollary settled with it: list shapes need **declared ordering** or limit/starting-point are non-deterministic (same filter, different pages per call).
2. **Limit-exceeded semantics on sweeps.** A required limit that silently truncates an "update the whole table" pass turns a full-table update into a partial one with no signal — a silent fallback wearing a seatbelt. Rule it: page-to-completion vs fail-loudly-when-exceeded. Silent truncation is not a candidate.
3. **Tracking defaults split by contract.** The sweep mutates, so it wants tracked entities — command-repository territory; read contracts default no-tracking. Draw the line per Asgard contract (`ICommandRepository` vs `ICachedRepository` / `ITemporalRepository`), not globally.
4. **Return shapes** — materialized `IReadOnlyList<T>` with required limit vs `IAsyncEnumerable` streaming (streaming likely deferred to Warehouse-shaped work).
5. **Count / exists** — first-class members or projection tricks; decide once.

## To pressure-test before ruling "Asgard interfaces suffice"

1. **Aggregate-graph loading** — the repository contract must express aggregate-root loading policy honestly; leaking `IQueryable` is refused (it reintroduces runtime guessing as API).
2. **Bulk operations** — `ExecuteUpdate` / `ExecuteDelete` shapes, or an explicit decision that command handlers don't bulk-mutate.
3. **Design-time authorship** — who runs `dotnet ef migrations add` against what, when the context is generated, file-local, and the entities live in `.Worker`.

## Boundary restated (tables already in sync)

Urdarbrunnr = EF *foundations*: abstract law-bearing context, entity base types, conventions, value converters, migrations chassis. Midgard = everything that *uses* a context: repository implementations, registration/pooling, the runtime. Asgard = the only contracts a service sees. The open question above decides only where the concrete context *comes from* — never who touches it (nobody downstream, by construction).
