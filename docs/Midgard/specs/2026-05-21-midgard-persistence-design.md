# Norse.Infrastructure.Persistence — CQRS, Repository Contracts, and Temporality Design

**Date:** 2026-05-21
**Status:** Draft for review
**Owner:** Buvy
**Supersedes:** none
**Amended:** 2026-06-03 — CLAUDE.md §7 #2 resolved (NServiceBus 10.2); Wolverine hedges removed; DLQ language corrected to NServiceBus error-queue/ServicePulse terminology; entities + `IEntityTypeConfiguration<T>` relocated from `.Server` to `.Worker`; `{Company}.{Context}.Backend` introduced. See `2026-06-03-messaging-foundation-design.md` §12.
**Companion specs:**
  - `2026-05-19-architecture-analyzers-design.md` — the `IDocumentRepository<T>` rename and the new `Norse.Infrastructure.Persistence` analyzer rules referenced below land in `Norse.Primitives.Architecture`.
  - `2026-05-20-yggdrasil-hosting-design.md` — Norse.Infrastructure.Persistence owns the per-service DbContext family the hosting plan calls out as a prerequisite; the migrations orchestrator there targets the DbContexts this spec defines.
  - `2026-05-20-auth-federation-design.md` — `AuthDbContext` is owned by this spec's DbContext family. *(Amended 2026-06-03: it backs only Auth's Postgres reporting projection — Mongo is the identity system of record, and Auth Plan A's former AuthDbContext stub task is void; see the auth spec §3.)*
  - `2026-06-03-messaging-foundation-design.md` — resolves the messaging library this spec's outbox semantics presumed; defines the endpoint topology, `TransportTransactionMode.ReceiveOnly`, and NSB persistence-table deployment.

**Realm placement of the artifacts this spec introduces** (per CLAUDE.md §5's seven-realm split):
  - `Norse.Abstractions.Contracts` (declared law) — `IWireShape`, `ProcessingStatus`, the wire-side marker contract every Mongo-backed wire shape implements.
  - `Norse.Abstractions.Infrastructure` (declared law) — the four repository contracts (`IDocumentRepository<T>`, `ICommandRepository<T>`, `ICachedRepository<T>`, `ITemporalRepository<T>`), the EF-entity marker hierarchy (`IEntity`, `IBridgeEntity`, `IInsertOnlyEntity`, `IReadOnlyEntity`, `ITemporalEntity`), and the `TstzRange` value type. No `IUnitOfWork` contract — transaction lifecycle is owned by the messaging library's per-handler session (see §4.2).
  - `Norse.Infrastructure.Persistence` (embodied law, Infrastructure realm) — every concrete implementation: per-service DbContext family, Mongo client wiring + BSON conventions, the tstzrange Npgsql interop layer, the connection-resolution strategy (tenancy resolved 2026-06-03 — stamp-per-tenant, see `2026-06-03-tenancy-model-design.md`; the resolver is per-stamp configuration).

**CLAUDE.md update accompanies this spec:** the Persistence row in §4 gains MongoDB as the operational read store; the repository inversion line replaces `IQueryRepository<T>` with `IDocumentRepository<T>` and adds the worker-only constraint to `ICommandRepository<T>` / `ICachedRepository<T>` / `ITemporalRepository<T>`.

---

## 1. Motivation

Norse writes a lot. Norse reads more. Every bound policy is bound once; the resulting policy document is read by the customer on the portal, by the producer in the agency UI, by the underwriter in QA, by the billing worker calculating premium, by the claims worker validating coverage at FNOL, by the bordereaux producer feeding the fronting carrier, and by every regulatory dashboard whose authors haven't been hired yet. The read-to-write ratio is multiple orders of magnitude in favor of reads.

That asymmetry is reason enough to take CQRS seriously. But there's a second reason that's specific to insurance: **the source of truth must outlast a compromised read path**. If an attacker pulls down the operational read store with a layer-7 flood, regulators do not care; if the source of truth goes with it, regulators care a lot. Operational reads and authoritative writes must be physically isolated, with separate access boundaries, separate scaling regimes, and separate failure domains.

A third reason is downstream: every analytical / executive / regulatory consumer of the platform is eventually fed by a data warehouse (Warehouse → Snowflake, behind VPN per §3.3). If temporality is built into the source of truth from the start, the warehouse pipeline doesn't have to recreate it; if it isn't, every analytical question that crosses a time boundary becomes a research project.

Norse.Infrastructure.Persistence implements all three commitments:

1. **CQRS at its peak.** Reads come from a document store (MongoDB) in the same wire format the gRPC services ship. No per-item .NET mapping on the read path. Writes go through the worker tier and land in Postgres; the worker projects the enriched document back to Mongo via a follow-on command. Public HTTP cannot reach Postgres directly under any code path.
2. **Replay-safe command chains.** Multi-step workflows (Postgres write → Mongo enrichment → third-party API call → …) are expressed as **chained commands** under NServiceBus, each handler doing exactly one thing. The messaging library owns the outbox semantics; we do not hand-roll Postgres outboxes. Jimmy Bogard's "6 little lines of fail" framing applies: every distributed step must be replay-safe in isolation.
3. **Temporality is dimensional, not bolted on.** System-versioned time (Postgres `tstzrange`) handles "what did our system know on date X?". Business-effective time (composite keys with a business date) handles "which rate applies on date Y?". The two compose; entities can use one, the other, both, or neither. The data warehouse inherits temporality from the source; nothing downstream recreates it.

Norse.Infrastructure.Persistence is opinionated about this on purpose. Three storage tiers, four repository contracts, five entity markers, one source of truth. Developers do not pick which database their feature touches — the contract surface tells them.

---

## 2. Scope and Non-Goals

### In scope (this spec)

- The four repository contracts and what each backs.
- The five entity-marker hierarchy and the allow/forbid composition matrix.
- The `IWireShape` marker, the `ProcessingStatus` block on every shim-able wire shape, and the shim → enrichment lifecycle.
- The CQRS write pipeline: `.Server` shim, NServiceBus command chains, worker-side enrichment.
- Idempotency: SequentialGuid-on-the-frontier for cookie traffic; request-hash dedup for M2M.
- Mongo physical layout (per-context database, shared cluster, connection-resolution strategy).
- BSON serialization conventions (GUID Standard, char/decimal/DateTimeOffset normalizations).
- The `TstzRange` value type and the EF Core conventions that integrate `tstzrange` into Npgsql for `ITemporalEntity`.
- Reference-data flow: seed → Postgres → Mongo projection → worker `ICachedRepository<T>`.
- Realm placement and project structure.
- The migrations integration with the existing `Norse.Hosting.Migrations.Service` orchestrator.
- The CLAUDE.md update.

### Out of scope (each has its own spec or is explicitly deferred)

- **Snowflake / Warehouse data warehouse pipeline.** The fact that Postgres feeds Snowflake (not Mongo) is settled here; the ETL mechanics, batch cadence, schema mapping, and CDC strategy are the Warehouse spec.
- **Reporting / bordereaux production.** The Reporting context (CLAUDE.md §3) consumes Postgres via Warehouse-style cross-context reads; its operational model is a separate spec.
- **The messaging library's own design.** *(Amended 2026-06-03: CLAUDE.md §7 #2 is RESOLVED — NServiceBus 10.2, see `2026-06-03-messaging-foundation-design.md`.)* This spec describes the *semantics* the messaging layer provides to persistence (outbox atomicity, per-handler session); endpoint topology, registration, recoverability, and the Particular platform are the messaging spec's territory.
- **Tenancy.** *(Amended 2026-06-03: CLAUDE.md §7 #4 is RESOLVED — stamp-per-tenant, see `2026-06-03-tenancy-model-design.md`. §12 below is rewritten accordingly.)*
- **Auth principal claim handling.** Covered by the auth-federation spec.
- **UI Composition.** Covered by the UI Composition spec; this spec is the persistence layer underneath.
- **The bake-in mechanism for `pg-18 WITH SYSTEM VERSIONING` if/when Npgsql gets first-class support.** V1 uses history-table triggers; the migration path to native SYSTEM VERSIONING is noted in §10 but not designed in detail here.

---

## 3. Architecture Overview

### 3.1 Three storage tiers, three reader populations

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  Operational tier — public HTTP/gRPC surface                                │
│                                                                             │
│   ┌──────────────────────────────────────────────────────────────────────┐  │
│   │  .Server (Norse.Hosting.Web.Server)                                            │  │
│   │                                                                      │  │
│   │  IDocumentRepository<T>     →  MongoDB  (reads business + reference) │  │
│   │  IDocumentRepository<T>.Shim →  MongoDB  (request-portion writes)    │  │
│   │  context.Send(Cmd)          →  RabbitMQ via NServiceBus              │  │
│   │                                                                      │  │
│   │  ❌ ICommandRepository<T>, ICachedRepository<T>, ITemporalRepository │  │
│   │     are analyzer-forbidden in .Server (worker-only)                  │  │
│   └──────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
                                       │
                                       ▼  (commands via NServiceBus)
┌─────────────────────────────────────────────────────────────────────────────┐
│  Source-of-truth tier — worker handlers only                                │
│                                                                             │
│   ┌──────────────────────────────────────────────────────────────────────┐  │
│   │  .Worker (Norse.Hosting.Web.Server monolith OR Norse.Hosting.Worker)           │  │
│   │                                                                      │  │
│   │  ICommandRepository<T>      →  PostgreSQL  ← source of truth         │  │
│   │  ITemporalRepository<T>     →  PostgreSQL tstzrange                  │  │
│   │  ICachedRepository<T>       →  MongoDB    (reference, opt-in LRU)    │  │
│   │  IDocumentRepository<T>.Replace → MongoDB (view enrichment)          │  │
│   │  context.Send(NextCmd)      →  NServiceBus outbox: atomic with the   │  │
│   │                                Postgres commit                       │  │
│   └──────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
                                       │
                                       ▼  (CDC or batch, Warehouse-owned)
┌─────────────────────────────────────────────────────────────────────────────┐
│  Analytical tier — separate spec, VPN-gated                                 │
│                                                                             │
│   Warehouse ETL  →  Snowflake  →  Executive dashboards, regulatory reports  │
│                                                                             │
│   No path Mongo → Snowflake. Snowflake is fed from Postgres only, where     │
│   temporality is already baked in via ITemporalEntity / tstzrange.          │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.2 The three-tier rationale

| Tier | Backing | Reachable from | Why isolated |
|---|---|---|---|
| Operational reads | MongoDB | Public HTTP/gRPC | Wire-shaped, low-latency, sacrificeable under L7 attack |
| Source of truth | PostgreSQL | Worker handlers only (via messaging) | Authoritative, transactional, never serves public HTTP |
| Analytical | Snowflake | VPN-gated tooling, exec dashboards | Cross-context, batch-cadence, immune to operational outages |

A public-tier compromise that takes Mongo down does not take Postgres or Snowflake with it. A Postgres outage stalls writes but leaves reads working (Mongo keeps serving whatever it last had). A Snowflake outage affects only analytical consumers. Three failure domains; three blast radii.

### 3.3 The CQRS write pipeline at a glance

```
1.  Client POSTs to .Server gRPC (BindPolicyAsync(req))

2.  .Server:
    a. Validate authority (population, scopes)
    b. IDocumentRepository<PolicyView>.ShimAsync(req.id, request_portion)
       → Mongo collection "{company}_policy.policies"
       → status = Pending
    c. context.Send(new ExecutePolicyBindCommand(req.id, req.payload))
    d. Return 201 + Location: /policies/{id} + the shim body

3.  .Worker Handler1 receives ExecutePolicyBindCommand:
    a. Business logic, validation, rule evaluation
    b. ICommandRepository<Policy>.AddAsync(entity)
    c. context.Send(new ProjectPolicyViewCommand(req.id, enrichedDto))
    d. Handler returns — framework's transaction commits atomically:
       EF Core flushes (via OnSaveChanges callback) + outbox row for the
       follow-on command inserts, both in the same ADO.NET transaction.

4.  .Worker Handler2 receives ProjectPolicyViewCommand:
    a. IDocumentRepository<PolicyView>.ReplaceAsync(id, enrichedDto)
       → status = Active, ProcessingError = null, ProcessedAt = now
    b. Handler returns

5.  (Optional Handler3 for downstream side effects: third-party APIs, event
    fan-out, etc. Each one its own command, idempotent, retry-safe.)
```

If any handler fails, NServiceBus retries that handler in isolation. Handler1 retry rolls back the Postgres transaction (it never committed). Handler2 retry idempotently re-upserts Mongo. No handler ever has to coordinate two stores in a single atomic step.

The two "outbox-shaped" boundaries are:
- `.Server`'s shim write + dispatch (resolved by "shim is best-effort, dispatch must succeed"; §6.3)
- `.Worker` Handler1's Postgres commit + Send(NextCmd) (resolved by NServiceBus's outbox primitive)

Neither outbox lives in Norse.Infrastructure.Persistence code. The first is policy; the second is library.

---

## 4. The Repository Contract Surface

Four contracts in `Norse.Abstractions.Infrastructure`. Two markers (`IDocument`, `IWireShape` / `IReferenceDocument`) in `Norse.Abstractions.Contracts`. The contracts are wire-shape-typed or entity-typed depending on which store they back; the constraint is part of the contract surface so the compiler refuses misuse.

**There is no `IUnitOfWork` contract.** The transaction boundary is owned by NServiceBus's per-handler session (`ISqlStorageSession`), which stitches the EF Core DbContext and the outbox into a single ADO.NET transaction committed atomically when the handler returns. Handlers do not call `SaveChangesAsync` explicitly; the framework's commit pipeline flushes EF via a registered callback right before the transaction commits. The contract surface in `Norse.Abstractions.Infrastructure` stays free of any messaging-library coupling.

### 4.1 `IDocumentRepository<T>` — MongoDB

Wire-shape-typed. Used by `.Server` for reads and shim writes, by `.Worker` for view enrichment. The same contract serves both tiers; what differs is which methods each tier calls.

```csharp
namespace Norse.Abstractions.Infrastructure;

public interface IDocumentRepository<TDocument>
  where TDocument : class, IDocument
{
  // ── Reads ────────────────────────────────────────────────────────────────

  Task<TDocument?> GetByIdAsync(Guid id, CancellationToken ct);

  // Server-side projection — Mongo $project, no in-memory mapping.
  // Driver translates the expression into BSON; result deserializes
  // directly into TProjection. Cross-collection joins are NOT permitted;
  // CQRS purity is enforced by the absence of the API.
  Task<TProjection?> GetByIdAsync<TProjection>(
    Guid id,
    Expression<Func<TDocument, TProjection>> projection,
    CancellationToken ct);

  Task<IReadOnlyList<TDocument>> QueryAsync(
    Expression<Func<TDocument, bool>> filter,
    Expression<Func<TDocument, object>>? sort,
    int skip,
    int take,
    CancellationToken ct);

  Task<IReadOnlyList<TProjection>> QueryAsync<TProjection>(
    Expression<Func<TDocument, bool>> filter,
    Expression<Func<TDocument, TProjection>> projection,
    Expression<Func<TDocument, object>>? sort,
    int skip,
    int take,
    CancellationToken ct);

  // ── Writes ───────────────────────────────────────────────────────────────

  // .Server uses this. Plants the request portion of the wire shape; status = Pending.
  // Re-shim with the same id is idempotent (upsert).
  Task ShimAsync(Guid id, TDocument requestShape, CancellationToken ct);

  // .Worker uses this after Postgres commit. Idempotent upsert with the
  // enriched document. status = Active (or Rejected on validation failure).
  Task ReplaceAsync(Guid id, TDocument enriched, CancellationToken ct);
}
```

**Design notes:**

- `TProjection` is unconstrained. The typical case is "PolicyListItem (10 fields) projected from PolicyDocument (60 fields)," but the contract doesn't dictate.
- Sort expressions are `Expression<Func<TDocument, object>>`, not `TProjection`. Mongo's pipeline runs `$match → $sort → $skip → $limit → $project`; the sort field doesn't need to survive into `TProjection`.
- No `$lookup`. No cross-collection joins. If a wire shape needs data from another aggregate, the worker projects it into the source aggregate at write time. If two views genuinely cannot be expressed within one aggregate, the UI composes (§3.1 of the UI Composition spec).
- `ShimAsync` and `ReplaceAsync` are both upserts. Repeated calls with the same `id` are idempotent — required for the command-chain replay semantics.

### 4.2 `ICommandRepository<T>` — PostgreSQL, worker-only

Entity-typed. Available only to `.Worker` (analyzer-forbidden in `.Server` via `YGG-rule TBD`). The single mutation path for the source of truth.

```csharp
public interface ICommandRepository<TEntity>
  where TEntity : IEntity
{
  Task AddAsync(TEntity entity, CancellationToken ct);

  // Analyzer-forbidden when TEntity : IInsertOnlyEntity.
  Task UpdateAsync(TEntity entity, CancellationToken ct);

  // Analyzer-forbidden when TEntity : IInsertOnlyEntity.
  Task RemoveAsync(TEntity entity, CancellationToken ct);
}
```

`ICommandRepository<T>` does not surface `SaveChangesAsync`, and there is no separate `IUnitOfWork` contract for handlers to inject. NServiceBus's per-handler session — `ISqlStorageSession` — owns one ADO.NET connection and one transaction for the duration of one message's handling. The DbContext is wired to use that connection (`UseNpgsql(session.Connection)`) and `UseTransaction(session.Transaction)` enrolls EF in the same transaction; a `session.OnSaveChanges(...)` callback registered at DbContext construction flushes EF Core's pending changes right before the framework commits.

The handler then does:

```csharp
public async Task Handle(ExecutePolicyBindCommand cmd, IMessageHandlerContext context)
{
  var policy = new Policy(cmd.Id, ...);
  await _policies.AddAsync(policy, context.CancellationToken);
  // ... business logic, possibly more Add/Update calls ...
  await context.Send(new ProjectPolicyViewCommand(cmd.Id, ToWireShape(policy)));
  // Handler returns; framework commits:
  //   1. OnSaveChanges callback fires → EF Core flushes pending changes
  //   2. Outbox row for ProjectPolicyViewCommand is inserted in the same tx
  //   3. Single COMMIT — business data + outbox row land atomically
}
```

No explicit `SaveChangesAsync` call, no transaction-boundary marker. The transaction is implicit in the handler scope; the framework owns the commit lifecycle.

This is the only place where the messaging library leaks into the `Norse.Abstractions.Infrastructure` contract by **behavior** (the library provides this stitching primitive) — but it does NOT leak by API. The contracts stay library-agnostic; the concrete impl in `Norse.Infrastructure.Persistence` constructs the DbContext per-context against NServiceBus's `ISqlStorageSession`.

**`QueryTrackingBehavior.NoTracking` is the default** in this model — explicit writes via `ICommandRepository<T>` don't need EF's change-tracking machinery, and turning it off is a free per-query allocation reduction.

`ICommandRepository<T>` is forbidden for `T : IReadOnlyEntity` — reference data writes go through the seed pipeline (§9), never through handler code. The analyzer enforces this as a build error.

### 4.3 `ICachedRepository<T>` — MongoDB, worker-only

Entity-typed (specifically `IReadOnlyEntity`). Backed by MongoDB — the same physical store as `IDocumentRepository<T>`. Worker-only.

```csharp
public interface ICachedRepository<TEntity>
  where TEntity : IReadOnlyEntity
{
  Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct);
  Task<IReadOnlyList<TEntity>> QueryAsync(
    Expression<Func<TEntity, bool>> filter,
    CancellationToken ct);
  // No writes. Reference data lifecycle is the seed pipeline.
}
```

**The "cached" framing:** Mongo IS the cache from Postgres' perspective. Reference data is the source of truth in Postgres (FK target for business tables) and is projected into Mongo by a deploy-time or seed-time step (§9). Workers read it from Mongo via `ICachedRepository<T>`.

`Norse.Infrastructure.Persistence`'s impl MAY layer a worker-local LRU on top of the Mongo backing for hot entity types — this is an opt-in optimization per entity (`[CacheLocally(maxEntries: 5000)]` attribute or DI configuration), not part of the contract. The HTTP server gets neither the contract nor the LRU: it reads reference data via `IDocumentRepository<T>` like any other Mongo doc, because HTTP-tier memory budget is constrained and Mongo lookups are already fast enough.

Invalidation of a worker-local LRU is event-driven: a `ReferenceDataReloadedEvent { EntityType, EffectiveAt }` published by the seed pipeline is consumed by every worker, which drops the cache entry for the affected `EntityType`.

### 4.4 `ITemporalRepository<T>` — PostgreSQL tstzrange, worker-only

Entity-typed (`ITemporalEntity`). Available only to `.Worker` (and to admin / Warehouse-side tooling that runs in worker-equivalent contexts).

```csharp
public interface ITemporalRepository<TEntity>
  where TEntity : ITemporalEntity
{
  // Point-in-time read: the state of this entity as our system knew it at `at`.
  Task<TEntity?> AsOfAsync(Guid id, DateTimeOffset at, CancellationToken ct);

  // Full history of this entity, ordered by system-period lower bound ascending.
  Task<IReadOnlyList<TEntity>> HistoryAsync(Guid id, CancellationToken ct);

  // Query across the timeline: every version of any entity matching the filter,
  // whose system-period overlaps [from, to].
  Task<IReadOnlyList<TEntity>> AsOfRangeAsync(
    Expression<Func<TEntity, bool>> filter,
    DateTimeOffset from,
    DateTimeOffset to,
    CancellationToken ct);
}
```

The Postgres-side mechanics (history table, triggers, query SQL) are in §10. The contract surface here is deliberately small; complex cross-time queries are admin/Warehouse territory and use raw SQL.

---

## 5. The Marker Interface Hierarchy

`Norse.Abstractions.Infrastructure` declares five entity markers. `IEntity` is the root; the others compose along two orthogonal axes (identity shape, mutability mode).

```csharp
namespace Norse.Abstractions.Infrastructure;

// Root. Indicates "this type is an EF entity persisted in PostgreSQL."
// All other markers extend this one.
public interface IEntity
{
  Guid Id { get; }
}

// Orthogonal axis: identity shape. Composite uniqueness on top of the
// surrogate Guid Id inherited from IEntity. The marker tells the analyzer
// and EF conventions "this entity has additional uniqueness columns beyond
// Id." Composite-uniqueness column structure is per-entity via
// IEntityTypeConfiguration<T>; the surrogate Guid Id is conventionally
// derived from those columns via UUID v5 over a per-entity namespace,
// so the same logical row always gets the same Id across environments.
//
// Repository contracts continue to operate on Guid Id (no separate
// generic parameter for composite keys); the composite uniqueness is
// a database-level constraint added by the EF configuration.
public interface IBridgeEntity : IEntity { }

// Mutability mode: write-once. Third-party feeds, BDX rows, etc.
// ICommandRepository<T>.UpdateAsync and .RemoveAsync are forbidden.
public interface IInsertOnlyEntity : IEntity { }

// Mutability mode: seeded only. ICommandRepository<T> is NOT available
// at all; writes go through the seed pipeline (§9). Reads via
// ICachedRepository<T> in the worker, IDocumentRepository<T> on the
// HTTP tier.
public interface IReadOnlyEntity : IEntity { }

// Mutability mode: tstzrange-versioned. Full CRUD via ICommandRepository<T>,
// plus AsOf queries via ITemporalRepository<T>. The SystemPeriod column
// is auto-configured by Norse.Infrastructure.Persistence's EF conventions.
public interface ITemporalEntity : IEntity
{
  TstzRange SystemPeriod { get; init; }
}
```

### 5.1 Allow / forbid composition matrix

| Composition | Status | Notes |
|---|---|---|
| Plain `IEntity` (no extra markers) | ✓ allow | Default mutable CRUD |
| `IBridgeEntity` + (any one mutability mode, or none) | ✓ allow | Composite PK with whatever mutability fits |
| `ITemporalEntity` + `IBridgeEntity` | ✓ allow | e.g., effective-dated producer↔agency relationship |
| `IInsertOnlyEntity` + `IBridgeEntity` | ✓ allow | BDX row keyed by (bdx_id, row_number) |
| `IReadOnlyEntity` + `IBridgeEntity` | ✓ allow | state × ZIP reference table |
| `ITemporalEntity` + `IReadOnlyEntity` | ✓ allow | Reference data with audit history (e.g., NCCI class-factor changes over time) |
| `ITemporalEntity` + `IInsertOnlyEntity` | ✗ forbid | Mutability contradiction (temporal implies mutation history) |
| `IReadOnlyEntity` + `IInsertOnlyEntity` | ✗ forbid | Mutability contradiction (both restrict writes, differently) |
| Two mutability markers (any other combination) | ✗ forbid | Mutability mode is single-valued |

Enforcement is via a `Norse.Primitives.Architecture` analyzer rule (`YGG-rule TBD`, slotted in the diagnostic catalog at next-available number). Violations are build errors.

### 5.2 The two temporality flavors

System-versioned and business-effective temporality are **distinct concerns** that can compose freely:

| Flavor | Marker | Question it answers | Mechanism |
|---|---|---|---|
| System-versioned | `ITemporalEntity` | "What did our system know about this row on date X?" | `tstzrange` column, history table, triggers |
| Business-effective | `IBridgeEntity` with a business date in the composite PK | "Which rate applies on date Y?" | Composite PK `(id, effective_date)`, no triggers |

A canonical rate-manual factor row uses both: composite uniqueness on `(class_code, effective_date)` for business semantics, plus the `SystemPeriod` tstzrange for "we loaded the 2027 rates in November 2026; the version we shipped on day 1 had a typo we fixed on day 3." The surrogate `Guid Id` is derived deterministically (UUID v5 over `(class_code, effective_date)`), so the same logical row gets the same Id across every environment.

There is no separate marker for "I have a business date in my uniqueness columns" — `IBridgeEntity` is sufficient. If the `(id, business_date)` pattern becomes pervasive across many entities, we'll specialize at that point.

### 5.3 The wire-shape markers (`IDocument` family)

Live in `Norse.Abstractions.Contracts` (declared law for `*.Contracts` assemblies). All Mongo-backed documents implement `IDocument`; the two specializations split based on whether the document participates in the shim lifecycle.

```csharp
namespace Norse.Abstractions.Contracts;

// Root marker. Every Mongo document carries an Id.
public interface IDocument
{
  Guid Id { get; }
}

// Shim-able wire shape. Every type returned from a write gRPC method
// AND every persisted business-aggregate view implements this.
public interface IWireShape : IDocument
{
  ProcessingStatus Status { get; init; }
  string? StatusReason { get; init; }
  DateTimeOffset? ProcessedAt { get; init; }
}

// Reference-data wire shape (the Mongo projection of an IReadOnlyEntity record).
// Reference data is always "Active" by definition; the ProcessingStatus block
// would be noise. The LoadedAt timestamp lets clients reason about staleness.
public interface IReferenceDocument : IDocument
{
  DateTimeOffset LoadedAt { get; }
}

public enum ProcessingStatus
{
  Unspecified = 0,
  Pending     = 1,  // .Server has planted a shim; .Worker has not yet enriched
  Active      = 2,  // .Worker has enriched; this is the current view
  Rejected    = 3,  // .Worker validated the command and rejected it; see StatusReason
}
```

`IDocumentRepository<TDocument> where TDocument : class, IDocument` accepts both `IWireShape` and `IReferenceDocument` types — same Mongo backing, same query surface, same projection support. `ShimAsync` / `ReplaceAsync` are still part of the contract surface for both kinds, but in practice only consumers of `IWireShape` types call them; reference-data projection happens via the projection worker (§9.3), not through the repository's shim/replace methods.

The two specialized markers are mutually exclusive at the wire-shape level (a type implements one or the other, never both). The analyzer enforces this when the rule slot lands.

---

## 6. The CQRS Write Pipeline

### 6.1 The canonical shape

Every write workflow has at minimum three steps:

```
.Server                          .Worker Handler1                .Worker Handler2
 (HTTP tier)                      (business + Postgres)           (view projection)
─────────────                    ──────────────────────           ─────────────────
1. Validate authority
2. Shim → Mongo                  ▸ Receive ExecuteXCommand        ▸ Receive ProjectXCommand
   (status = Pending)            ▸ Business logic                 ▸ IDocumentRepository<T>
3. Send ExecuteXCommand          ▸ ICommandRepository<T>.Add        .ReplaceAsync(id, dto)
4. Return 201 + Location +       ▸ Send ProjectXCommand              (status = Active or
   shim body                     ▸ Handler returns                   Rejected)
                                   ↑                               ▸ Handler done
                                   Framework commits atomically:
                                   EF flush (via OnSaveChanges
                                   callback) + outbox row, in
                                   one ADO.NET transaction
```

Multi-step workflows extend by chaining more commands: `ProjectXCommand` → `NotifyPartnerCommand` → `SendConfirmationEmailCommand` → … Each command's handler does exactly one external interaction; failures retry that handler in isolation.

### 6.2 What "atomic" means at each boundary

- **`.Server` shim + dispatch.** The shim write to Mongo and the `context.Send` are **not** atomic with each other. We design around this with operational discipline (§6.3) rather than a distributed transaction.
- **`.Worker` Postgres commit + Send.** NServiceBus's per-handler session (`ISqlStorageSession`) makes the EF Core flush (triggered by an `OnSaveChanges` callback the session fires immediately before commit) and the subsequent `context.Send(nextCommand)` outbox-row insert atomic at the Postgres-transaction level. The outbox table lives in the same Postgres database as the business data, owned by the messaging library. We do not interact with it directly, and handler code never calls `SaveChangesAsync` explicitly.
- **`.Worker` Mongo enrichment.** A single Mongo upsert. Idempotent by construction (same id, same payload → same outcome). No transactional partner.

### 6.3 What happens on each failure boundary

| Failure point | What's left behind | Operator action |
|---|---|---|
| `.Server` validation (before shim) | Nothing | Client retries or surfaces error to user |
| `.Server` shim write fails | Nothing in Mongo, no message dispatched | Return 5xx to client; client retries |
| `.Server` dispatch fails after shim succeeded | Shim in Mongo (Pending), no command on bus | Return 5xx to client; client retries with same id; shim is upsert-idempotent; second attempt's dispatch will fire |
| Handler1 fails before Postgres commit | Nothing in Postgres, NServiceBus retries Handler1 | None unless retry-cap exhausted; then the error queue → operator inspects in ServicePulse |
| Handler1 fails after Postgres commit but before `Send` | Atomic via NServiceBus outbox — cannot happen | n/a |
| Handler2 fails | Postgres has the entity, Mongo still has Pending shim | NServiceBus retries Handler2; idempotent ReplaceAsync |
| Handler2 retry-cap exhausted | Postgres entity exists, Mongo shim stuck at Pending, message in the error queue | Operator inspects in ServicePulse, addresses cause, retries the message |
| Any handler later in chain fails | Earlier steps' state is committed; later steps haven't run | NServiceBus retries that handler in isolation; idempotent step is replayable |

The "stuck at Pending" case is observable through the wire shape itself — the client's GET shows `Status = Pending` indefinitely, and ServicePulse shows the failed message in the error queue. The two signals correspond.

### 6.4 The Rejected status

A handler that determines the command is invalid (business rule violation, out-of-bounds input, validation failure that wasn't caught at `.Server`) does NOT throw to retry. It writes a Rejected shim and exits successfully:

```csharp
public async Task Handle(ExecutePolicyBindCommand cmd, IMessageHandlerContext context)
{
  var validation = await _underwriting.ValidateAsync(cmd);
  if (!validation.Ok)
  {
    var rejected = new PolicyView
    {
      Id = cmd.Id,
      Status = ProcessingStatus.Rejected,
      StatusReason = validation.Reason,
      ProcessedAt = DateTimeOffset.UtcNow,
      // ... copy request fields from cmd ...
    };
    await _documents.ReplaceAsync(cmd.Id, rejected, context.CancellationToken);
    return;
  }
  // ... happy path ...
}
```

Rejected is a terminal state. The client polls, sees Rejected, surfaces the reason to the user, and the resource's lifecycle ends there. A re-POST with the same idempotency mechanism returns the Rejected shim (idempotency is about not double-processing; it does not retry failed business logic).

A Rejected shim does NOT trigger Handler2 (no `ProjectViewCommand` was sent — the rejection write was inline). The handler chain just stops.

### 6.5 The "6 little lines of fail" principle, applied

Each handler in a chain follows three rules:

1. **Do one external interaction.** Postgres write, Mongo write, third-party POST — pick one. Never two.
2. **Be idempotent.** Same input, same outcome, regardless of how many times the message is delivered.
3. **Dispatch the next step only after the local interaction has succeeded.** And rely on the messaging library's outbox to make that dispatch atomic with the local store.

If a handler violates rule 1 (e.g., "write Postgres AND call Stripe in the same handler"), then a retry after a partial success duplicates side effects. Stripe gets called twice; the customer is charged twice. This is exactly the failure mode the chain pattern exists to prevent.

---

## 7. Idempotency

Two paths, one principle: the second arrival of the same logical request returns the first arrival's answer.

### 7.1 Cookie path — SequentialGuid at the frontier

`PrincipalSource = Cookie` traffic (WASM, MAUI, internal Blazor Server). Clients use the **SequentialGuid library** (Buvy's, dependency-free / AOT / WASM-compatible) to generate the resource ID at the edge:

```csharp
// WASM client:
var policyId = SequentialGuid.NewGuid();   // sortable, time-ordered
await billingApi.BindPolicyAsync(new BindPolicyRequest { Id = policyId, ... });
```

`.Server`'s gRPC handler:

```csharp
public async Task<BindPolicyResponse> BindPolicyAsync(BindPolicyRequest req, ...)
{
  var existing = await _documents.GetByIdAsync(req.Id, ct);
  if (existing != null)
  {
    // Re-POST with the same id. Return the existing shim/document.
    return new BindPolicyResponse { Id = req.Id, View = existing };
  }

  var shim = BuildShimFromRequest(req);
  await _documents.ShimAsync(req.Id, shim, ct);
  await _bus.Send(new ExecutePolicyBindCommand(req.Id, req.Payload));
  return new BindPolicyResponse { Id = req.Id, View = shim };
}
```

No separate idempotency table. The Mongo collection's `_id` index IS the dedup mechanism. Re-POST with same id hits the same shim. Different id = different request.

### 7.2 M2M path — request-hash dedup

`PrincipalSource = Jwt` with `Population = Machine` (third-party APIs via client_credentials). Server generates the SequentialGuid on the caller's behalf and dedups by request hash:

```csharp
public async Task<BindPolicyResponse> BindPolicyAsync(BindPolicyRequest req, ...)
{
  var hash = ComputeIdempotencyHash(callerPrincipalId, "BindPolicy", req);
  var existing = await _idempotency.LookupAsync(hash, ct);
  if (existing != null)
  {
    var doc = await _documents.GetByIdAsync(existing.ResourceId, ct);
    return new BindPolicyResponse { Id = existing.ResourceId, View = doc! };
  }

  var newId = SequentialGuid.NewGuid();
  await _idempotency.RecordAsync(hash, newId, ct);
  var shim = BuildShimFromRequest(req, newId);
  await _documents.ShimAsync(newId, shim, ct);
  await _bus.Send(new ExecutePolicyBindCommand(newId, req.Payload));
  return new BindPolicyResponse { Id = newId, View = shim };
}

internal static string ComputeIdempotencyHash(Guid caller, string method, object payload)
{
  using var sha = SHA256.Create();
  var bytes = sha.ComputeHash(MessagePackSerializer.Serialize(new { caller, method, payload }));
  return Convert.ToHexString(bytes);
}
```

The idempotency record lives in a per-context `_idempotency` Mongo collection:

```
{company}_billing._idempotency:
  { _id: "<hex sha256>", caller_principal_id: Guid, resource_id: Guid, recorded_at: ISO-8601 }

  Indexes: unique on (_id)
           TTL on recorded_at  → 90 days (configurable per context)
```

90-day default TTL keeps the dedup window long enough to cover any realistic client retry pattern, short enough that the collection doesn't grow unboundedly. M2M clients wanting N truly-distinct creates from identical inputs must vary the payload (nonce, timestamp, sequence number) — contract responsibility, not platform.

### 7.3 The idempotency contract responsibility

A wire shape whose request payload is genuinely identical between two distinct intended creations must be designed to carry a discriminator. The platform does not interpret what discriminator is meaningful; it hashes the payload as given. M2M API documentation explicitly states: "If you want to create N resources with otherwise identical input, include a unique field (nonce, client_reference_id, timestamp)."

---

## 8. The Read Pipeline

### 8.1 Reads from .Server

`.Server`'s gRPC read methods inject `IDocumentRepository<TWireShape>` and serve directly from Mongo:

```csharp
public async Task<GetPolicyResponse> GetPolicyAsync(GetPolicyRequest req, ...)
{
  var view = await _documents.GetByIdAsync(req.Id, ct);
  return view == null
    ? throw new RpcException(new Status(StatusCode.NotFound, $"policy {req.Id}"))
    : new GetPolicyResponse { View = view };
}

public async Task<ListPoliciesResponse> ListPoliciesAsync(ListPoliciesRequest req, ...)
{
  var views = await _documents.QueryAsync(
    filter: v => v.CustomerId == req.CustomerId && v.Status != ProcessingStatus.Rejected,
    sort:   v => v.CreatedAt,
    skip:   req.PageToken.Skip,
    take:   req.PageSize,
    ct: ct);
  return new ListPoliciesResponse { Items = views, NextPageToken = ... };
}
```

No mapping. The wire shape returned from Mongo IS the wire shape the gRPC method returns. The .NET process spends CPU on BSON deserialization (which the Mongo driver handles natively) and gRPC serialization (which Grpc.AspNetCore handles natively). No per-item LINQ projection in process.

### 8.2 Projected reads

For lighter-weight list endpoints (a dashboard tile showing 50 policies × 5 fields shouldn't pull 50 × 60-field documents):

```csharp
public async Task<ListPolicySummariesResponse> ListPolicySummariesAsync(...)
{
  var summaries = await _documents.QueryAsync(
    filter: v => v.CustomerId == req.CustomerId,
    projection: v => new PolicySummary
    {
      Id = v.Id,
      Status = v.Status,
      EffectiveDate = v.EffectiveDate,
      Premium = v.Premium,
      ProductCode = v.ProductCode,
    },
    sort: v => v.EffectiveDate,
    skip: req.PageToken.Skip,
    take: req.PageSize,
    ct: ct);
  return new ListPolicySummariesResponse { Items = summaries };
}
```

The Mongo driver translates the projection expression to `$project`, only the projected fields cross the wire from Mongo, and the BSON deserializer materializes `PolicySummary` directly (no intermediate `PolicyView` allocation).

### 8.3 What .Server cannot do

- **Touch Postgres in any direction.** `ICommandRepository<T>`, `ICachedRepository<T>`, and `ITemporalRepository<T>` are all worker-only — none are registered in the .Server DI scope. There is no Postgres DbContext bound to the `.Server` request scope (and no transaction-scope contract exists for `.Server` to construct one). The HTTP tier has no Postgres reads, no Postgres writes, no Postgres transactions. Every operational read goes through `IDocumentRepository<T>` to MongoDB; every write becomes a command dispatched to RabbitMQ for the worker to process.
- Construct a cross-context view by calling another context's gRPC API at read time. Per the UI Composition spec, cross-context composition happens in the UI layer (each widget makes its own gRPC call; the dashboard host arranges them). `.Server` services are single-context.
- Cache reference data in-process. Reference-data reads on the .Server tier go through `IDocumentRepository<T>` to Mongo. No in-process FrozenDictionary, no static lookup table.

---

## 9. Reference Data

### 9.1 The lifecycle

```
1. Source (e.g., NCCI rate filing, ISO 4217 currency list, USPS ZIP file)
   ↓
2. Seed tool (Spectre.Console.Cli command in a dedicated data CLI or a similar tool)
   ↓
3. Postgres (source of truth, FK target, ITemporalEntity if audit-versioned)
   ↓
4. Reference-projection worker (or seed-tool step)
   ↓
5. Mongo (per-context reference collections)
   ↓
6a. .Server reads → IDocumentRepository<T> (treats reference docs like any other)
6b. .Worker reads → ICachedRepository<T> (with opt-in per-entity LRU)
```

### 9.2 Postgres as the source of truth

Reference data lives in Postgres because business tables FK to it. A policy's `state_code` references a real `reference.us_states` row; a claim's `cause_code` references `reference.ncci_loss_codes`; an invoice line item's `currency_code` references `reference.iso_currencies`. Without an FK target in Postgres, integrity is enforced only by application code — exactly the silent-failure mode the platform fights against.

A reference-data entity is declared `IReadOnlyEntity` (no writes from handler code, no `ICommandRepository<T>` available). It may additionally be `ITemporalEntity` if we want audit history (e.g., NCCI class-factor changes over years).

### 9.3 The seed tool

Reference data is loaded via a Spectre.Console.Cli command. The seed reads from a canonical source file (state filings, ISO refreshes, USPS feeds), writes to Postgres via raw EF operations (bypassing the repository contracts — the seed tool is platform tooling, not application code), and publishes a `ReferenceDataReloadedEvent` to RabbitMQ on completion.

A separate **reference-projection worker** subscribes to `ReferenceDataReloadedEvent` and replays the Postgres reference table into Mongo. The projection is idempotent (upsert by id) and re-runnable on demand.

For V1, the seed tool and the projection worker may be co-located in a single utility (the data CLI's `ref load --type ncci`). When projection lag becomes operationally interesting, they split.

### 9.4 Worker-local LRU

`Norse.Infrastructure.Persistence`'s impl of `ICachedRepository<T>` defaults to "Mongo read every time." Opt-in per entity via an attribute on the entity type:

```csharp
[CacheLocally(MaxEntries = 50_000)]
public sealed class IsoCurrency : IReadOnlyEntity { ... }
```

The impl checks for the attribute at startup. If present, it wraps the Mongo client with an LRU of the configured size. LRU entries are invalidated by `ReferenceDataReloadedEvent` for the matching entity type.

The attribute lives in `Norse.Abstractions.Infrastructure` (with the other markers). Recommendation: apply only to entity types with bounded cardinality (a few thousand rows) and very-frequent lookups in worker hot paths. ISO currencies (200 rows, hit on every monetary calculation) → yes. ZIP codes (40,000 rows, hit on every customer address) → measure first.

---

## 10. Temporality

### 10.1 The `TstzRange` value type

Lives in `Norse.Abstractions.Infrastructure`. Maps to Postgres' `tstzrange` (timestamp-with-timezone range) — the canonical type for system-versioned periods in pg-18.

```csharp
namespace Norse.Abstractions.Infrastructure;

public readonly record struct TstzRange
{
  public required DateTimeOffset Lower { get; init; }
  public DateTimeOffset? Upper { get; init; }                  // null = unbounded (current row)
  public required RangeBoundType LowerBound { get; init; }     // typically Inclusive
  public required RangeBoundType UpperBound { get; init; }     // typically Exclusive

  public static TstzRange CurrentFrom(DateTimeOffset since) => new()
  {
    Lower = since,
    Upper = null,
    LowerBound = RangeBoundType.Inclusive,
    UpperBound = RangeBoundType.Exclusive,
  };

  public bool Contains(DateTimeOffset at)
  {
    var lowerOk = LowerBound == RangeBoundType.Inclusive ? at >= Lower : at > Lower;
    var upperOk = Upper is null
      || (UpperBound == RangeBoundType.Inclusive ? at <= Upper : at < Upper);
    return lowerOk && upperOk;
  }
}

public enum RangeBoundType
{
  Unspecified = 0,
  Inclusive   = 1,
  Exclusive   = 2,
}
```

The standard convention for system-versioned tables is `[lower, upper)` (lower-inclusive, upper-exclusive). The value type carries the bound types explicitly so it round-trips correctly through pg's range serialization.

### 10.2 Npgsql interop

Npgsql's EF Core provider supports `NpgsqlRange<DateTime>` natively, but:
- It uses `DateTime`, not `DateTimeOffset` — round-tripping loses timezone information.
- It does not auto-configure history tables / triggers for system versioning.
- pg-18 native `WITH SYSTEM VERSIONING` is not yet wired into the Npgsql provider as of writing.

`Norse.Infrastructure.Persistence` ships a small interop layer:

1. **A value converter `TstzRangeValueConverter`** that maps `TstzRange` ↔ `NpgsqlRange<DateTime>` (the canonical Npgsql range type; the converter normalizes to UTC on the way in and reconstructs `DateTimeOffset.UtcNow.Offset == TimeSpan.Zero` on the way out). The `DateTimeOffset` on the .NET side is for API clarity (every consumer sees explicit UTC); the on-disk representation is timestamptz, which is always UTC anyway.
2. **An EF Core convention `TemporalEntityConvention`** that auto-configures any `T : ITemporalEntity` with:
   - The `SystemPeriod` property as a `tstzrange` column.
   - A GIST exclusion constraint preventing overlapping rows for the same logical id (only one "current" row at a time).
   - A history table named `{schema}.{table}_history` with the same columns plus `__history_id` PK.
   - INSERT/UPDATE/DELETE triggers on the main table that copy the prior row into the history table with the bounded `tstzrange`.
3. **A `Norse.Infrastructure.Persistence.Migrations` helper** that emits the trigger SQL in EF migrations (raw SQL `migrationBuilder.Sql(...)` calls), so per-context migration packages don't have to hand-roll them.

The trigger approach is V1. When Npgsql gets first-class `WITH SYSTEM VERSIONING` support and pg-18's history machinery is reachable from EF, `TemporalEntityConvention` swaps the trigger emission for the native `WITH SYSTEM VERSIONING` clause. The change is transparent to consumers — `ITemporalRepository<T>` doesn't know which path is active underneath.

### 10.3 `ITemporalRepository<T>` implementation

Queries the history table via `FromSqlInterpolated`. The contract is small; the impl is correspondingly small.

```csharp
// Sketch of the .AsOfAsync impl in Norse.Infrastructure.Persistence:
public async Task<TEntity?> AsOfAsync(Guid id, DateTimeOffset at, CancellationToken ct)
{
  // Current row first
  var current = await _dbContext.Set<TEntity>()
    .FromSqlInterpolated($@"
      SELECT * FROM {_table}
       WHERE id = {id}
         AND system_period @> {at}::timestamptz
      LIMIT 1
    ")
    .SingleOrDefaultAsync(ct);
  if (current != null) return current;

  // Fall through to history
  return await _dbContext.Set<TEntity>()
    .FromSqlInterpolated($@"
      SELECT * FROM {_historyTable}
       WHERE id = {id}
         AND system_period @> {at}::timestamptz
      LIMIT 1
    ")
    .SingleOrDefaultAsync(ct);
}
```

The Postgres `@>` operator is "range contains element" — checks whether the row's system_period contains the requested timestamp. The query is index-friendly when the system_period column has a GIST index (which the convention adds).

### 10.4 What temporality does NOT do

- **Project to Mongo as history.** Mongo holds the current view, not the temporal history. If a use case genuinely needs "show me how this policy looked on every day for the last year," it's an analytical question (Warehouse / Snowflake), not an operational one.
- **Apply to the wire shapes.** `IWireShape` does NOT carry `SystemPeriod`. Wire shapes are point-in-time projections of the current entity state.
- **Provide a generic LINQ-native temporal predicate.** The Postgres operator support hasn't landed in EF Core's LINQ translation. We use `FromSqlInterpolated` until it does.

### 10.5 The two-temporality composition in practice

A rate-manual class-factor row:

```csharp
public sealed class ClassFactor : IBridgeEntity, ITemporalEntity, IReadOnlyEntity
{
  // Surrogate PK — IEntity contract. Derived deterministically from (ClassCode, EffectiveDate)
  // via UUID v5 over a per-entity-type namespace; the seed pipeline computes it once on insert.
  public required Guid Id { get; init; }

  // Business-uniqueness columns. IEntityTypeConfiguration<ClassFactor> declares a
  // UNIQUE INDEX over these so the database enforces "one row per (class_code, effective_date)."
  public required string ClassCode { get; init; }
  public required DateOnly EffectiveDate { get; init; }

  // System-versioned column — auto-configured by TemporalEntityConvention.
  public required TstzRange SystemPeriod { get; init; }

  // Business columns
  public required decimal LossCostFactor { get; init; }
  public required decimal ExpenseLoad { get; init; }
  // ... etc
}
```

This row is:
- `IBridgeEntity` — composite PK `(class_code, effective_date)`, EF configuration sets it up.
- `IReadOnlyEntity` — `ICommandRepository<T>` unavailable; only the seed pipeline writes.
- `ITemporalEntity` — system-versioned with `SystemPeriod`; the convention adds history table + triggers.

The repository surface:
- `ICachedRepository<ClassFactor>` — worker reads "current" rows (system_period @> now()).
- `ITemporalRepository<ClassFactor>` — "what did we think the 2027 rates were on 2026-11-15?" — admin-only query.
- `IDocumentRepository<ClassFactorView>` — HTTP-tier reads of the wire-shape projection.

The seed tool inserts; the system_period auto-updates via triggers; Mongo gets re-projected by the reference-projection worker.

---

## 11. Mongo Physical Layout and BSON Conventions

### 11.1 Database and collection layout

**Per-context Mongo database** in a shared Mongo cluster:

```
{company}_billing
  ├── invoices              (PolicyInvoiceView documents)
  ├── payment_methods       (PaymentMethodView documents)
  ├── _idempotency          (M2M idempotency records, TTL-indexed)
  └── _reference            (or one collection per reference-data type, see §11.2)

{company}_claims
  ├── claims
  ├── claim_payments
  └── _idempotency

{company}_policy
  ├── policies
  ├── endorsements
  ├── _idempotency
  └── _reference

{company}_customer
  ├── customers
  ├── consents
  └── _idempotency

{company}_auth
  ├── sessions               (cookie/jwt session view shapes)
  └── _idempotency
```

Per-context isolation in Mongo mirrors per-service isolation in Postgres. Norse.Infrastructure.Persistence resolves the connection string per context at deployment time — same approach as the Postgres DbContext family. Tenancy never enters this resolution: a tenant is a deployment stamp (`2026-06-03-tenancy-model-design.md`), so per-stamp values are just configuration.

### 11.2 Reference-data collection layout

Two acceptable patterns:

**Per-context `_reference` collection set** (default for V1):
```
{company}_billing._reference
  ├── (filter by document_type field: iso_currency, payment_terms, …)
```

**One collection per reference type** (for high-cardinality reference data):
```
{company}_billing.reference_iso_currencies
{company}_billing.reference_payment_terms
```

Cardinality of the reference type drives the choice: a few hundred rows total → single `_reference` collection with a `document_type` discriminator; tens of thousands → per-type collection. Norse.Infrastructure.Persistence's reference-projection worker generates the layout from the seeded entity metadata.

### 11.3 BSON serialization conventions

`Norse.Infrastructure.Persistence`'s startup registers a platform-wide set of BSON serializers. The settings come directly from production-tested patterns; deviations require coordinated migration of every collection.

```csharp
// In Norse.Infrastructure.Persistence — invoked once from AddPersistence(...) at host startup.
internal static class BsonConventions
{
  public static void Register()
  {
    // GUIDs in BSON binary subtype 0x04 (Standard / UUID).
    // Other languages and drivers can read these; SequentialGuid output round-trips correctly.
    var guidSerializer = new GuidSerializer(GuidRepresentation.Standard);
    BsonSerializer.TryRegisterSerializer(guidSerializer);
    BsonSerializer.TryRegisterSerializer(new NullableSerializer<Guid>(guidSerializer));

    // char as BSON string — the default (int32) is unreadable.
    var charSerializer = new CharSerializer(BsonType.String);
    BsonSerializer.TryRegisterSerializer(charSerializer);
    BsonSerializer.TryRegisterSerializer(new NullableSerializer<char>(charSerializer));

    // decimal as BSON Decimal128 — money MUST NOT round-trip through binary float.
    var decimalSerializer = new DecimalSerializer(BsonType.Decimal128);
    BsonSerializer.TryRegisterSerializer(decimalSerializer);
    BsonSerializer.TryRegisterSerializer(new NullableSerializer<decimal>(decimalSerializer));

    // DateTimeOffset as ISO-8601 string — the default sub-document representation is hostile to humans.
    var dtoSerializer = new DateTimeOffsetSerializer(BsonType.String);
    BsonSerializer.TryRegisterSerializer(dtoSerializer);
    BsonSerializer.TryRegisterSerializer(new NullableSerializer<DateTimeOffset>(dtoSerializer));
  }
}
```

(`TryRegisterSerializer` instead of `RegisterSerializer` so duplicate registration during test bootstrap doesn't throw.)

**`Norse.Infrastructure.Persistence` pins these as integration-tested.** A test in `Norse.Infrastructure.Persistence.Tests` round-trips a known SequentialGuid + decimal + char + DateTimeOffset through Mongo, reads back the raw BSON via the driver's introspection API, and asserts the binary subtype (0x04 for the Guid), the BSON type (`Decimal128` for the decimal, `String` for the char and the DateTimeOffset), and the bit-perfect round-trip. If a future driver upgrade silently changes a default, the test fails before production sees it.

### 11.4 Mongo driver version

V1 targets the **C# Mongo driver 3.x** line. The 2.x → 3.x transition retired `GuidRepresentationMode` (V3 became the only mode); the equivalent settings above no longer need the `BsonDefaults.GuidRepresentationMode = GuidRepresentationMode.V3` line that 2.x required.

### 11.5 Indexing

`Norse.Infrastructure.Persistence` provides a `[MongoIndex(...)]` attribute consumed at startup. Per-context plugins declare indexes on their wire shapes:

```csharp
[MongoIndex(nameof(CustomerId))]
[MongoIndex(nameof(EffectiveDate), Descending = true)]
[MongoIndex(nameof(Status), nameof(EffectiveDate))]
public sealed record PolicyView : IWireShape { ... }
```

Index creation is idempotent (Mongo's `createIndex` is); Norse.Infrastructure.Persistence runs it at host startup or via a separate index-management command. Migrating an index (renaming, changing key direction) is operational tooling, not a startup-time decision.

---

## 12. Tenancy

*(Rewritten 2026-06-03: CLAUDE.md §7 #4 is RESOLVED — stamp-per-tenant. Full design: `2026-06-03-tenancy-model-design.md`.)*

A tenant is a deployment stamp — a complete, isolated platform instance. No persistence code path is tenant-aware.

Each repository contract's impl in `Norse.Infrastructure.Persistence` resolves its store connection (Postgres connection string, Mongo URL) through an `IConnectionResolver` abstraction — the single choke point where a connection string is born:

```csharp
namespace Norse.Infrastructure.Persistence;

internal interface IConnectionResolver
{
  string ResolvePostgres(string contextName);
  string ResolveMongoDatabase(string contextName);
}

internal sealed class ConfigurationConnectionResolver : IConnectionResolver
{
  public string ResolvePostgres(string contextName)
    => _config[$"Persistence:Postgres:{contextName}"];
  public string ResolveMongoDatabase(string contextName)
    => _config[$"Persistence:Mongo:{contextName}"];
}
```

The earlier sketch's `NorsePrincipal?` parameter is dropped: under stamping, connection resolution is principal-independent by definition, and CLAUDE.md §2.5 forbids speculative parameters. The interface is `internal`, so a future shared-compute model (tenancy spec §6 re-entry triggers) re-adds the parameter as a mechanical, contained change. `SingleTenantConnectionResolver` is renamed `ConfigurationConnectionResolver` — the impl was never about tenancy; it resolves per-stamp configuration.

**The entity-side tenancy contract is resolved: neither, ever.** No `TenantId` on `IEntity`, no `ITenantScoped` marker, no global query filter, no `tenant_id` column in any store. RLS never spells "tenant." Isolation is the database boundary, provisioned per stamp.

---

## 13. Migrations

### 13.1 Recap of the hosting-spec mechanics

- `{Company}.{Context}.Migrations` packages target their context's `Norse.Infrastructure.Persistence`-owned DbContext.
- `Norse.Infrastructure.Persistence` supplies the base `InfrastructureDbContext` with snake_case naming, `MaxLength` conventions, and the `TemporalEntityConvention` for `ITemporalEntity`.
- The `Norse.Hosting.Migrations.Service` orchestrator applies migrations in dependency order per the existing hosting spec.
- Mongo doesn't have schema migrations in the traditional sense. Wire-shape evolution rules (additive fields, never breaking, source-generator-friendly) are an API-versioning concern, not a persistence one. The reference-projection worker rebuilds reference collections from Postgres at any time.

Per-context Mongo index creation happens at host startup (idempotent), separate from migrations.

### 13.2 The migrations service gates BOTH deployables, not just the worker

A first instinct is that `Norse.Hosting.Migrations.Service` only needs to gate `Norse.Hosting.Worker` (or the worker-side plugins inside `Norse.Hosting.Web.Server` monolith mode). After all, `.Server` is forbidden from `ICommandRepository<T>` and never touches Postgres directly — what does it care if migrations haven't applied?

It cares because **`.Server` dispatches commands that workers consume**. If `.Server` starts before migrations land, the race is:

1. v2 migration begins; fails partway through.
2. Workers can't start (their `/health` is Unhealthy via the migrations dependency).
3. `.Server` starts anyway (no migration dependency) and begins accepting v2 traffic.
4. v2 writes plant Mongo shims and dispatch v2 commands to RabbitMQ.
5. No worker is running to consume them. Commands queue up. After NServiceBus's delivery retries exhaust, they land in the error queue. Customers see shims stuck Pending forever.

The avoidable failure mode is "v2 web leading v1-or-broken worker schema." The fix: **the migrations service's `/health = Healthy` signal gates BOTH `Norse.Hosting.Web.Server` AND `Norse.Hosting.Worker`**. In Aspire-orchestrated local dev, both deployables `WaitFor(migrations)`. In Kubernetes prod, both have a readiness gate keyed on the migrations Job's completion (or equivalent operator-controlled signal).

Cost: `.Server` startup latency includes the migration's runtime. Amortized into deployment time, not per-request. Worth it.

Benefit: a failed migration is a deployment-stuck failure, not a half-deployed mismatch. Blue-green or rolling deployments keep the prior version serving until the migration succeeds. The operator gets paged before users see anything change.

### 13.3 Init container vs. Job + readiness gate

The hosting spec leaves the K8s shape (init container, sidecar, separate Job) as operations territory; the binary's behavior is what's contractual:

- The Migrations binary exits 0 on success after flipping `/health` to `Healthy` (with a configurable grace period for readiness probes to observe).
- On failure, the binary stays alive with `/health = Unhealthy` until the operator drains it.

K8s-side options that satisfy this contract:

| Pattern | Suitability | Notes |
|---|---|---|
| Init container per Pod (Host + WorkerHost) | Works for monolith Pod, awkward when split | Migrations run twice in split mode; harmless but wasteful |
| Single migrations Job + readiness gate on Host + WorkerHost Deployments | Recommended | Migrations run once per deploy; both Deployments wait on the same signal |
| Migrations as a sidecar in each Pod | Works | Same harmlessness wart as init containers; sidecars are also fine in Aspire-mapped-to-K8s scenarios |

The simplest pattern that matches the binary's contract is the single Job + readiness gate. The choice is documented at the operations layer.

---

## 14. Realm Placement and Project Structure

```
Norse.Abstractions.Contracts                     (Abstractions realm, declared law)
  ├── IDocument                      root marker for Mongo-backed documents (Guid Id only)
  ├── IWireShape : IDocument         shim-able wire shapes (carries ProcessingStatus block)
  ├── IReferenceDocument : IDocument reference-data wire shapes (carries LoadedAt)
  ├── ProcessingStatus               enum (Unspecified | Pending | Active | Rejected)

Norse.Abstractions.Infrastructure                (Abstractions realm, declared law)
  ├── IEntity                        root marker
  ├── IBridgeEntity                  composite-PK marker
  ├── IInsertOnlyEntity              write-once marker
  ├── IReadOnlyEntity                seed-only marker
  ├── ITemporalEntity                tstzrange-versioned marker (carries SystemPeriod)
  ├── TstzRange                      value type (lower/upper/bound types)
  ├── RangeBoundType                 enum
  ├── IDocumentRepository<T>         contract
  ├── ICommandRepository<T>          contract
  ├── ICachedRepository<T>           contract
  ├── ITemporalRepository<T>         contract
  └── CacheLocallyAttribute          opt-in per-entity LRU declaration

  (No IUnitOfWork — messaging library's per-handler session owns the tx)

Norse.Infrastructure.Persistence                  (Infrastructure realm, embodied law)
  ├── InfrastructureDbContext        base DbContext with snake_case + MaxLength + temporal conventions
  ├── TstzRangeValueConverter        EF Core value converter
  ├── TemporalEntityConvention       auto-configures ITemporalEntity tables
  ├── BsonConventions                static class — Register() runs at startup
  ├── MongoIndexAttribute            consumed at startup
  ├── IConnectionResolver            per-stamp connection resolution (tenancy spec 2026-06-03)
  ├── ConfigurationConnectionResolver impl (renamed from SingleTenantConnectionResolver)
  ├── DocumentRepository<T>          concrete impl
  ├── CommandRepository<T>           concrete impl
  ├── CachedRepository<T>            concrete impl (Mongo backing + optional LRU)
  ├── TemporalRepository<T>          concrete impl (FromSqlInterpolated against history table)
  ├── PersistenceServiceCollectionExtensions
  │   └── AddPersistence(...) registers everything for a given list of contexts
  └── ReferenceProjectionWorker      BackgroundService that consumes ReferenceDataReloadedEvent

{Company}.{Context}.Backend          (product realm, server-side shared — added 2026-06-03)
  ├── server→worker commands (*Command records)
  └── Mongo document records (server serves; worker writes — see §17 #6 placement question)

{Company}.{Context}.Server           (product realm, embodied per-context — amended 2026-06-03)
  ├── gRPC services (IDocumentRepository<T> reads + shim writes, IMessageSession sends)
  └── {Context}Plugin : IWebHostPlugin
      (no entities, no EF — hard wall; cannot reference .Worker)

{Company}.{Context}.Worker           (product realm, embodied per-context — amended 2026-06-03)
  ├── entity classes (implementing IEntity + appropriate markers; relocated from .Server)
  ├── IEntityTypeConfiguration<T> impls (relationships, indexes, CHECK constraints, schema mapping)
  ├── message handlers (consume commands, use ICommandRepository<T> + IDocumentRepository<T>)
  ├── ToWireShape mappers (per-entity, hand-written or source-generated)
  └── {Context}WorkerPlugin : IWorkerHostPlugin
      (no ASP.NET Core — hard wall; cannot reference .Server)
```

---

## 15. Worked Example — Policy bind, end to end

A representative flow exercising every facet of the design.

### 15.1 The wire shapes

In `{Company}.Policy.Contracts`:

```csharp
namespace {Company}.Policy.Contracts;

public sealed record BindPolicyRequest(
  Guid Id,                           // client-generated SequentialGuid (cookie path)
  Guid CustomerId,
  string ProductCode,
  DateOnly EffectiveDate,
  decimal RequestedPremium,
  IReadOnlyList<CoverageLine> Coverages);

public sealed record PolicyView : IWireShape
{
  public required Guid Id { get; init; }
  public required ProcessingStatus Status { get; init; }
  public string? StatusReason { get; init; }
  public DateTimeOffset? ProcessedAt { get; init; }

  // Request fields — planted by .Server in the shim
  public required Guid CustomerId { get; init; }
  public required string ProductCode { get; init; }
  public required DateOnly EffectiveDate { get; init; }
  public required decimal RequestedPremium { get; init; }
  public required IReadOnlyList<CoverageLine> Coverages { get; init; }

  // Enriched fields — populated by .Worker after Postgres commit
  public string? PolicyNumber { get; init; }                 // generated post-validation
  public decimal? BoundPremium { get; init; }                // may differ from RequestedPremium
  public DateOnly? ExpirationDate { get; init; }
  public string? UnderwriterId { get; init; }
  public IReadOnlyList<EndorsementSummary>? Endorsements { get; init; }
}
```

### 15.2 The entity

In `{Company}.Policy.Worker` *(amended 2026-06-03; previously `.Server` — entities live with the handlers that mutate them, invisible to the web tier)*:

```csharp
namespace {Company}.Policy.Worker;

internal sealed class Policy : ITemporalEntity, IEntity
{
  public required Guid Id { get; init; }
  public required Guid CustomerId { get; init; }
  public required string ProductCode { get; init; }
  public required DateOnly EffectiveDate { get; init; }
  public required DateOnly ExpirationDate { get; init; }
  public required decimal BoundPremium { get; init; }
  public required string PolicyNumber { get; init; }
  public required Guid UnderwriterId { get; init; }
  public required TstzRange SystemPeriod { get; init; }
  // ... navigation properties for coverages, endorsements ...
}

internal sealed class PolicyEntityConfiguration : IEntityTypeConfiguration<Policy>
{
  public void Configure(EntityTypeBuilder<Policy> b)
  {
    b.ToTable("policies", schema: "policy");
    b.HasKey(p => p.Id);
    b.HasIndex(p => new { p.CustomerId, p.EffectiveDate });
    b.Property(p => p.PolicyNumber).HasMaxLength(40);
    b.Property(p => p.BoundPremium).HasPrecision(18, 2);
    // SystemPeriod is auto-configured by TemporalEntityConvention; we don't write it here.
  }
}
```

### 15.3 The `.Server` gRPC handler

In `{Company}.Policy.Server`:

```csharp
internal sealed class PolicyService(
  IDocumentRepository<PolicyView> documents,
  IMessageSession bus)
  : IPolicyApi
{
  public async Task<BindPolicyResponse> BindPolicyAsync(BindPolicyRequest req, CallContext ctx)
  {
    var ct = ctx.CancellationToken;

    var existing = await documents.GetByIdAsync(req.Id, ct);
    if (existing != null)
      return new BindPolicyResponse { Id = req.Id, View = existing };

    var shim = new PolicyView
    {
      Id = req.Id,
      Status = ProcessingStatus.Pending,
      ProcessedAt = null,
      CustomerId = req.CustomerId,
      ProductCode = req.ProductCode,
      EffectiveDate = req.EffectiveDate,
      RequestedPremium = req.RequestedPremium,
      Coverages = req.Coverages,
    };
    await documents.ShimAsync(req.Id, shim, ct);
    await bus.Send(new ExecutePolicyBindCommand(req.Id, req));

    return new BindPolicyResponse { Id = req.Id, View = shim };
  }

  public async Task<GetPolicyResponse> GetPolicyAsync(GetPolicyRequest req, CallContext ctx)
  {
    var view = await documents.GetByIdAsync(req.Id, ctx.CancellationToken);
    return view == null
      ? throw new RpcException(new Status(StatusCode.NotFound, "policy not found"))
      : new GetPolicyResponse { View = view };
  }
}
```

### 15.4 The `.Worker` handlers

In `{Company}.Policy.Worker`:

```csharp
internal sealed class ExecutePolicyBindHandler(
  ICommandRepository<Policy> policies,
  ICachedRepository<ClassFactor> rates,
  IUnderwritingEngine underwriting,
  IDocumentRepository<PolicyView> documents)
  : IHandleMessages<ExecutePolicyBindCommand>
{
  public async Task Handle(ExecutePolicyBindCommand cmd, IMessageHandlerContext context)
  {
    var ct = context.CancellationToken;

    var decision = await underwriting.EvaluateAsync(cmd.Request, ct);
    if (!decision.Approved)
    {
      var rejected = BuildRejectedShim(cmd, decision.Reason);
      await documents.ReplaceAsync(cmd.Id, rejected, ct);
      return;
    }

    var rate = await rates.GetByIdAsync(/* derived id */, ct);
    var policy = BuildPolicy(cmd.Request, decision, rate!);
    await policies.AddAsync(policy, ct);

    var enriched = BuildEnrichedView(cmd.Request, policy);
    await context.Send(new ProjectPolicyViewCommand(cmd.Id, enriched));
    // Handler returns; framework commits: EF Core flushes the policy via the
    // OnSaveChanges callback, and the ProjectPolicyViewCommand outbox row
    // commits in the same ADO.NET transaction. No explicit SaveChangesAsync.
  }
}

internal sealed class ProjectPolicyViewHandler(IDocumentRepository<PolicyView> documents)
  : IHandleMessages<ProjectPolicyViewCommand>
{
  public Task Handle(ProjectPolicyViewCommand cmd, IMessageHandlerContext context)
    => documents.ReplaceAsync(cmd.Id, cmd.View, context.CancellationToken);
}
```

### 15.5 The behavior under failure

| Failure | Observable state | Recovery |
|---|---|---|
| Client retries the BindPolicy POST | `.Server` sees shim already exists, returns it | None — idempotent by design |
| Underwriting validation fails | Mongo doc → status = Rejected, StatusReason populated, Postgres untouched | Client polls, sees rejection, surfaces reason to user |
| Postgres write fails inside ExecutePolicyBindHandler | NServiceBus retries the message; Postgres rollback; shim stays Pending | None unless retries exhaust; then the error queue |
| ProjectPolicyViewHandler fails | NServiceBus retries; Postgres has the policy; Mongo stays Pending | None unless retries exhaust; then the error queue; operator fixes Mongo connectivity and retries from ServicePulse |
| Both Postgres and Mongo work but the client never polls | Mongo shim is Active; ready for whoever asks | n/a |

---

## 16. Resolved Decisions

Captured here so the rationale survives.

1. **CQRS read store = MongoDB.** Wire-shape-typed per item; no .NET mapping on the read path; physical isolation from Postgres. New element relative to CLAUDE.md's Persistence list; the spec ships with a coordinated CLAUDE.md update.
2. **Per-item Mongo doc shape, not per-response.** Lists fan out as Mongo queries with $project/$sort/$skip/$limit; server wraps the items in a trivial envelope (`items[], next_page_token`).
3. **`IQueryRepository<T>` renamed to `IDocumentRepository<T>`.** The contract handles read + shim + enrichment writes; "Query" was a misnomer. The architecture-analyzers spec is updated accordingly.
4. **`ICachedRepository<T>` is worker-only, backed by Mongo.** Mongo IS the cache from Postgres' perspective. HTTP-tier reference reads go through `IDocumentRepository<T>`. Worker may opt entity types into a local LRU via `[CacheLocally]`.
5. **The shim → enrichment lifecycle.** `.Server` writes the request portion of the wire shape to Mongo, dispatches the command, returns 201 + Location + shim body — no synchronous wait on the worker. Read-your-own-writes is satisfied by the shim.
6. **`ProcessingStatus` field on every shim-able wire shape.** Pending → Active or Rejected. The platform contributes this block via `IWireShape` so it's uniform.
7. **Reference data uses a sister marker `IReferenceDocument`** without ProcessingStatus.
8. **Command chains, not Postgres outboxes.** NServiceBus owns outbox semantics for the Postgres-commit + dispatch atomicity. Each handler does one external interaction. Multi-step workflows are chains of single-purpose commands.
9. **No cross-context Mongo projections; no cross-context reads at the .Server tier.** A "Customer 360"-style view is composed in the UI by independent widgets calling their respective contexts' gRPC APIs.
10. **Analytical reads do not touch Mongo.** Warehouse feeds Snowflake from Postgres directly. Snowflake is VPN-gated and behind a separate access boundary, so an operational outage cannot take down executive dashboards or regulatory reporting.
11. **Two distinct temporality flavors, freely composable.** System-versioned (`ITemporalEntity` + tstzrange) for "what did the system know?" Business-effective (`IBridgeEntity` with a business date in the PK) for "which rate applies?" Either, both, or neither per entity. No separate marker for "composite key with a date column"; `IBridgeEntity` is sufficient.
12. **Marker allow/forbid matrix enforced by analyzer.** `ITemporalEntity` + `IInsertOnlyEntity` is a build error; so is `IReadOnlyEntity` + `IInsertOnlyEntity`. Other combinations allowed.
13. **`ICommandRepository<T>` forbidden for `T : IReadOnlyEntity`** — reference-data writes go through the seed pipeline.
14. **SequentialGuid-on-the-frontier for cookie idempotency.** WASM, MAUI, internal Blazor clients generate the resource ID at the edge. `.Server` upserts shim by ID; re-POST is naturally idempotent.
15. **Request-hash dedup for M2M idempotency.** `(caller_principal_id || method || payload)` hashed; `.Server` looks up in per-context `_idempotency` Mongo collection; same hash returns the prior resource ID with `201 Created + Location`. TTL 90 days (configurable).
16. **Per-context Mongo database, shared cluster.** Connection resolution is `IConnectionResolver` — per-stamp configuration; tenancy never enters the runtime (stamp-per-tenant, `2026-06-03-tenancy-model-design.md`).
17. **BSON conventions pinned in a single startup function with an integration test that asserts on-disk representation.** GUID = Standard (binary subtype 0x04); char = String; decimal = Decimal128; DateTimeOffset = ISO-8601 String. C# driver 3.x; the 2.x `BsonDefaults.GuidRepresentationMode` line is dropped.
18. **`TstzRange` value type with explicit RangeBoundType.** Maps to Postgres `tstzrange`. The EF convention auto-configures the column, the GIST exclusion constraint, the history table, and the triggers for any `T : ITemporalEntity`. Native pg-18 `WITH SYSTEM VERSIONING` is a future upgrade path; the contract surface (`ITemporalRepository<T>`) doesn't change.
19. **Tenancy resolved — stamp-per-tenant (amended 2026-06-03).** The entity model never carries tenancy: no `TenantId` field, no `ITenantScoped` marker, no global query filter, no `tenant_id` column. `IConnectionResolver` is per-stamp configuration and drops its principal parameter. See `2026-06-03-tenancy-model-design.md`.
20. **Migrations gate BOTH `Norse.Hosting.Web.Server` and `Norse.Hosting.Worker`.** Even though `.Server` is forbidden from Postgres, it dispatches commands that workers consume. Letting `.Server` start before migrations land races into a "v2 web leads v1 worker schema" deadlock where dispatched commands accumulate in the error queue. Gating both deployables on the migrations Job's `/health = Healthy` signal turns a half-deployed mismatch into a clean deployment-stuck failure that pages the operator before users see anything.
21. **No `IUnitOfWork` contract.** NServiceBus's per-handler session (`ISqlStorageSession`) owns the transaction. The DbContext is wired to use the session's connection + transaction; a registered `OnSaveChanges` callback flushes EF Core's pending changes immediately before the framework commits. Handlers never call `SaveChangesAsync` explicitly; the commit is implicit in handler return. This removes the only messaging-library-coupled contract from `Norse.Abstractions.Infrastructure` (the contracts stay library-agnostic; the stitching lives in `Norse.Infrastructure.Persistence`'s impl). `QueryTrackingBehavior.NoTracking` is the default since explicit `ICommandRepository<T>` writes don't need change tracking.

---

## 17. Open Questions

1. **CLAUDE.md §7 #4 (tenancy)** — *resolved 2026-06-03: stamp-per-tenant (`2026-06-03-tenancy-model-design.md`). `IConnectionResolver` simplifies to per-stamp configuration; §12 rewritten.*
2. **NServiceBus vs Wolverine (CLAUDE.md §7 #2) — RESOLVED 2026-06-03: NServiceBus, version floor 10.2.** The original table's "AOT: unlikely ever" row was overtaken by events — 10.2 shipped multi-endpoint hosting (`AddNServiceBusEndpoint`) and source-generated handler/saga registration with scanning disabled, putting it on an AOT trajectory for v11. ServicePulse/ServiceControl operational maturity decided it over waiting for Critter Stack parity. See `2026-06-03-messaging-foundation-design.md`. `Norse.Infrastructure.Persistence`'s DbContext construction targets `ISqlStorageSession` (§4.2).
3. **pg-18 `WITH SYSTEM VERSIONING` reachability.** V1 uses history-table triggers. Migrating to native pg-18 system versioning is an `Npgsql` capability question; until the Npgsql EF provider lands first-class support, triggers are how we get there.
4. **`MongoIndexAttribute` vs. fluent declaration.** V1 ships the attribute. If per-context plugins want richer index expressions (partial, sparse, text), the fluent surface earns a follow-on spec.
5. **The architecture-analyzer rule numbers for the marker enforcement and the worker-only repository checks.** Slotted in the `Norse.Primitives.Architecture` rule catalog at next-available numbers (likely YGG109–YGG112 by analogy to YGG108); coordinated when this spec lands.
6. **Wire-shape placement: `Contracts` vs `Backend` — RESOLVED 2026-06-03** by the mediator spec (`2026-05-26-mediator-design.md` §3.4). Mongo document records (+ `ProcessingStatus`, `IWireShape`) live in `.Backend`; requests, responses (projection targets), and validators live in `Contracts`. **Every read projects** — the handler feeds a server-side projection expression (document → response) to the Mongo driver, so the document shape never ships to a client even when the response mirrors a tight document 1:1. Projection declarations live in `.Server`, the only compilation that sees both types. The §15.1 worked example's `PolicyView : IWireShape` in `Contracts` is therefore superseded: the document record (e.g., `PolicyDocument`) sits in `.Backend`, and `Contracts` carries the response record it projects to.

---

## 18. Acceptance

This spec is "done enough to implement" when:

- The contract types (`IDocumentRepository<T>`, `ICommandRepository<T>`, `ICachedRepository<T>`, `ITemporalRepository<T>`, the marker hierarchy (`IEntity`, `IBridgeEntity`, `IInsertOnlyEntity`, `IReadOnlyEntity`, `ITemporalEntity`), `IDocument`, `IWireShape`, `IReferenceDocument`, `ProcessingStatus`, `TstzRange`, `RangeBoundType`, `CacheLocallyAttribute`, `MongoIndexAttribute`) compile in `Norse.Abstractions.Contracts` + `Norse.Abstractions.Infrastructure` against .NET 10.
- `Norse.Infrastructure.Persistence` ships concrete implementations of every contract.
- The BSON-convention integration test in `Norse.Infrastructure.Persistence.Tests` round-trips a SequentialGuid + char + decimal + DateTimeOffset and asserts on-disk BSON representation.
- The `TemporalEntityConvention` emits the system_period column + GIST exclusion + history table + triggers for any `ITemporalEntity` in a `InfrastructureDbContext`-derived context.
- The `Norse.Primitives.Architecture` analyzer rules for marker composition + worker-only repository constraints are slotted (numbers TBD) in the rule catalog.
- The CLAUDE.md update (Persistence section + repository inversion section) is staged in the same PR.

---

## 19. CLAUDE.md Update (preview)

Two changes accompany this spec:

### 19.1 Persistence section (§4 Technology Decisions → Persistence)

Replace the current bullet:

> - **Entity Framework Core** with snake_case and `MaxLength` conventions. DbContext family is owned by `Norse.Infrastructure.Persistence`: …

with:

> - **Entity Framework Core** + **MongoDB** for the operational two-tier CQRS model. Postgres is the source of truth (worker-only); Mongo is the operational read store (HTTP-tier reads + worker view projection). Snowflake fed by Warehouse from Postgres provides VPN-gated analytical reads; Mongo is never the source for downstream warehousing. DbContext family is owned by `Norse.Infrastructure.Persistence` (per-service DbContexts, snake_case + `MaxLength` conventions, `TemporalEntityConvention` for system versioning).

And add after the repository-inversion bullet:

> - **`IDocumentRepository<T>`** (formerly `IQueryRepository<T>`) is wire-shape-typed and backs onto MongoDB. Used by `.Server` for reads + shim writes and by `.Worker` for view enrichment.
> - **`ICommandRepository<T>`, `ICachedRepository<T>`, `ITemporalRepository<T>` are worker-only.** Analyzer-forbidden in `.Server` (rule numbers TBD in norse-primitives-architecture).

### 19.2 Anti-patterns section (§8)

Add:

> - **No HTTP-tier access to Postgres — in either direction.** `.Server` is forbidden from `ICommandRepository<T>`, `ICachedRepository<T>`, and `ITemporalRepository<T>` (all worker-only). No Postgres DbContext is bound to the HTTP request scope; there is no `IUnitOfWork` contract for `.Server` to misuse. The HTTP tier reads only from MongoDB via `IDocumentRepository<T>`; writes dispatch to RabbitMQ for the worker to handle. Source-of-truth mutations go exclusively through worker handlers via the messaging library's command chain. Analyzer-enforced.
> - **No worker-internal multi-store transactions.** A handler that writes Postgres AND Mongo (or Postgres AND a third-party API) in the same handler must be split into chained commands — one external interaction per handler, each idempotent. The Jimmy Bogard "6 little lines of fail" rule applies.
