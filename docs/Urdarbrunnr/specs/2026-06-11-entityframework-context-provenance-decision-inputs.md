# `Norse.EntityFramework` — Concrete DbContext Provenance (Decision Inputs)

**Date:** 2026-06-11
**Status:** Ruled — design session complete same-day (see §Repository-surface law, §Pressure-tests ruled); formal verdict gated on one PoC (design-time twin, §Remaining before verdict)
**Owner:** Buvy

**Amendment (2026-07-25):** `Norse.EntityFramework` throughout this document (title included) is the
pre-rename name. Urðarbrunnr's `Norse.EntityFramework.*` widened to `Norse.Persistence.EntityFramework.*`
(PR #31, merged 2026-07-22, shipped v0.0.4) — the ruling recorded here still stands, just under the new
namespace.

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

**Ruled in the design session (2026-06-11, same day):**

1. **Starting-point mechanics: keyset only.** The starting point is an after-key seek; offset never enters the contract — it is O(skipped rows) in Postgres and silently unstable under concurrent writes, and its one honest use (page-N jump UI) is read-model territory (`IDocumentRepository`), not worker-side SQL. Corollary ruled in the same breath: list shapes carry a **required declared ordering**, and the ordering must be **total** — a unique trailing key, generator/analyzer-enforced — or the seek position is ambiguous between ties.
2. **Limit-exceeded on sweeps: fail loudly.** The limit is a declared upper bound — an assertion about the table's known size ("this reference table holds ≤ N rows"), database self-defense in API form. Row count past it throws immediately. Page-to-completion is refused: it hides unbounded work behind a bounded-looking signature and bloats the NSB handler transaction; the genuinely-large mutation pass is bulk-op territory, ruled below.
3. **Tracking is a fixed property of each contract — no override knob.** `ICommandRepository<T>`: tracked, always (it exists to mutate). `ICachedRepository<T>` / `ITemporalRepository<T>`: no-tracking, always — read contracts with no mutation path. `IDocumentRepository<T>` is Mongo; the concept does not apply. Projections are untracked everywhere by EF semantics, so "projection optional everywhere" composes for free — a projected read off the command repository is simply an untracked read. A per-call tracking flag is a knob that exists so something "can be tuned later"; §2.5 says delete it.
4. **Return shape: materialized `IReadOnlyList<T>` only.** Every list read is bounded (required limit) and seek-positioned, so streaming has nothing to offer and one thing to break: `IAsyncEnumerable` holds the connection — and under the NSB per-handler session, the transaction — open during per-row handler work, exactly the long-transaction shape the messaging law exists to prevent. The honest streaming case is the unbounded analytical read, which is Warehouse-shaped by definition; Warehouse declares its own contracts when it stands up.
5. **Count / exists: first-class members, shape-following predicate law.** `ExistsAsync` follows the single-entity shape — predicate **required** ("does any row at all exist" is not a question a handler legitimately asks). `CountAsync` follows the list shape — predicate **optional**; the whole-table count is the same first-class citizen the whole-table sweep is, and the natural way to verify the sweep-limit assertion. Both return scalars: no limit, no ordering, no projection. The projection-trick alternative is refused — it invites materializing entities to answer a yes/no question; the cheap intent (`EXISTS (SELECT 1 …)`, `COUNT(*)`) gets the cheap SQL by construction.

## Pressure-tests ruled (2026-06-11, same session)

1. **Aggregate-graph loading: the graph is declared once, at configuration time — never per call.** An aggregate is a consistency boundary, and a boundary you can load partially isn't one; a per-call include API (the spec-pattern shape) is `IQueryable` wearing a typed costume, reintroducing "which shape did this handler load?" as a runtime question. The `IEntityTypeConfiguration<T>` that must already exist for every entity declares the aggregate's load graph via EF's native `AutoInclude`; command-repository loads return the whole declared aggregate, unconditionally. Read-side shaping never touches the navigation machinery — projections express their own joins. Nobody specifies graph shape at a call site, ever.
2. **Bulk operations: refused — command handlers don't bulk-mutate. The door's shape is recorded.** `ExecuteUpdate` / `ExecuteDelete` do not pass through `SaveChanges`, which means they bypass the interceptor-based audit stamping that is the realm law this context exists to enforce — and sidestep temporal hooks the same way. Admitting them naively punches a silent hole in the law. The bounded sweep is the sanctioned whole-table mutation precisely because it runs through tracked entities and `SaveChanges`, so every law applies. No current consumer needs set-based mutation; the realm's own precedent holds ("provider packs land when a real consumer forces them"). **The door, for when one does:** bulk members whose Midgard implementation mechanically appends the audit-stamp setters to every call (enforceable — callers never touch the context), with temporal entities likely excluded from bulk delete entirely. If forced, the door opens by design, not improvisation.
3. **Design-time authorship: the design-time twin.** The collision is real: the migration snapshot carries `[DbContext(typeof(…))]` and must *name* the context type, but a `file`-scoped type cannot be named from another assembly — the one legitimate second consumer is locked out by the very unreferenceability we ruled. Resolution: the generator runs in **both** assemblies over the same entity/configuration inputs. `.Worker` gets the runtime context, `file`-scoped, DI wiring beside it; `.Migrations` gets its own `file`-scoped twin plus the `IDesignTimeDbContextFactory` in the same generated file. The snapshot binds to the Migrations-local twin; `MigrationsAssembly` points home; `dotnet ef migrations add --project {Company}.{Context}.Migrations` is self-sufficient, and `Norse.Hosting.Migrations.Service` applies with the same twin. The two CLR types never meet — the runtime context never migrates, the migrations context never runs — and drift is structurally impossible: same generator, same inputs. Refused alternatives: single `internal` context + `InternalsVisibleTo` (demotes unreferenceable-by-construction to by-policy and opens a second internals door beyond `.Tests`); migrations inside `.Worker` (violates standing law — independent `.Migrations` NuGet, deployment-job application).

## Remaining before verdict

One honest unknown gates the formal verdict: **whether `dotnet ef` design-time discovery (reflection over assembly types) cooperates with file-local types' mangled metadata names** — generators exist at design time (§Forces #2), but *file-local* + EF tooling specifically is unverified. A Glitnir PoC (`poc/`, sibling to `pg19-temporal`) proves or refutes: a generated `file sealed` context + `file` `IDesignTimeDbContextFactory` in a Migrations-shaped assembly, `dotnet ef migrations add` run against it, findings on the record. If file-local discovery fails, the fallback inside the twin shape is the twin declared `internal` *within the generated file of the Migrations assembly only* — the runtime context's `file`-scoping is untouched either way.

## Boundary restated (tables already in sync)

Urdarbrunnr = EF *foundations*: abstract law-bearing context, entity base types, conventions, value converters, migrations chassis. Midgard = everything that *uses* a context: repository implementations, registration/pooling, the runtime. Asgard = the only contracts a service sees. The open question above decides only where the concrete context *comes from* — never who touches it (nobody downstream, by construction).
