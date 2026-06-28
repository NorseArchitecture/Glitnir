# Norns — Temporal Backbone, Reference Fabric, and Audit Substrate Design

> **AMENDED 2026-06-11** — the ReferenceData realm dissolved: temporal contracts (`ITemporalRepository<T>`) moved to `Norse.Abstractions`, implementations to `Norse.Infrastructure`, vertical reference content is sovereign (`{Company}.ReferenceData.*`). See `docs/codenames.md` and the 2026-06-11 repository topology spec. Retained as the historical record of the temporal design, which survives.

**Date:** 2026-06-04
**Status:** Draft for review
**Owner:** Buvy
**Supersedes:** none (amends `2026-05-21-midgard-persistence-design.md` §5, §10, §14 — see §13)
**Companion specs:**
  - `2026-05-21-midgard-persistence-design.md` — declared the repository contract family, the marker hierarchy, and the V1 temporal mechanics this spec relocates and upgrades. The amendments in §13 are authoritative where the two disagree.
  - `2026-06-03-messaging-foundation-design.md` — the NSB pipeline behaviors in §8 ride the endpoint topology defined there; audit headers flow through `TransportTransactionMode.ReceiveOnly` command chains unchanged.
  - `2026-05-19-architecture-analyzers-design.md` — the new and retired YGG rules in §13.3 land in `Norse.Primitives.Architecture`.
  - `2026-06-03-tenancy-model-design.md` — stamps hold per-stamp seed state; nothing in ReferenceData is tenant-aware.

**Realm placement of the artifacts this spec introduces** (per CLAUDE.md §5's seven-realm split):
  - `Norse.Abstractions.Persistence` (declared law, **new assembly**) — the entity marker family, `IPersisted<TSelf>` self-configuration law, `TstzRange`, the worker-only repository contracts relocated from `Norse.Abstractions.Infrastructure`. References EF Core (see §4 for why this is safe).
  - `Norse.ReferenceData.Temporal`, `Norse.ReferenceData.Audit`, `Norse.ReferenceData.Reference`, `Norse.ReferenceData.Contracts`, `Norse.ReferenceData.Data.{Dataset}`, `Norse.ReferenceData.Cli` (ReferenceData realm) — the forged machinery. See §11.
  - `Norse.Infrastructure.Persistence` (Infrastructure realm) — **composes** ReferenceData's conventions, interceptors, and repositories into the per-service DbContext family. Infrastructure plays the cards it is dealt; it no longer implements temporal machinery.

---

## 1. Motivation

The Norns — Urd, Verdandi, Skuld; what was, what is, what shall be — weave fate at Yggdrasil's roots. Even the Æsir are bound by their weave. In platform terms:

- **Urd (what was)** — the audit substrate. Every row version, who wrote it, from where, through which message chain. When a dispute needs an unwind eighteen months from now, the answer exists because it was woven at write time. History that was not captured cannot be recovered; this spec makes *not capturing it* a deliberate, typed, reviewed act rather than an omission.
- **Verdandi (what is)** — the classification fabric. SIC, NAICS, geography, currencies, units of measure, and the licensed datasets (NCCI, ISO forms) every bounded context FKs against. Canonical, versioned, deterministic identity, seeded through one pipeline.
- **Skuld (what shall be)** — time traversal. "What did the system know on date X?" and "which rate applied on date Y?" answered by query, not archaeology — including across parent/child aggregates versioned on independent cadences.

The Norse.Infrastructure.Persistence spec declared the contract surface (`ITemporalRepository<T>`, the entity markers, the seed lifecycle) and sketched V1 mechanics. This spec is where those mechanics get a permanent home, a PG18-native upgrade, and the two siblings they were always going to need: the audit substrate and the reference-data fabric.

The mission framing (CLAUDE.md §1) applies directly: an MGA that resolves disputes rather than wearing claimants down needs to *prove what it knew and when it knew it* — cheaply, mechanically, and for every entity that matters. That is what Norns is for.

---

## 2. Scope and Non-Goals

### In scope

- The dependency ruling between ReferenceData and Norse.Infrastructure.Persistence, and the resulting `Norse.Abstractions.Persistence` law split.
- The entity law: record entities, CRTP self-configuration, the forced-stance rule, `ILinkEntity`, `ICurrentOnlyEntity`, the skip-navigation ban.
- The temporal machinery: `tstzrange` with the `'infinity'` sentinel, PG18 `WITHOUT OVERLAPS` temporal PKs, the storage-model split by temporality flavor, timeline views, the composable `AsOf` contract.
- The Urd audit substrate V1: audit shadow columns, principal propagation through NSB headers, the stamping interceptor.
- The Verdandi reference fabric: dataset packaging, the seed engine, deploy-time and CLI application, deterministic UUID v5 identity.
- Database self-defense for the entity stances: role grants per stance.
- Amendments to the Norse.Infrastructure.Persistence spec, CLAUDE.md, and the YGG rule catalog.

### Out of scope

- **A separate append-only audit event log** (read auditing, PII-access logging, auth event streams, TimescaleDB hypertables). Deferred with re-entry triggers (§8.4). Row history + who-columns is V1.
- **Warehouse / Snowflake mechanics.** Warehouse inherits temporality from the timeline views; its ETL design is its own spec.
- **The Mongo projection of reference data.** Owned by the Norse.Infrastructure.Persistence spec §9.3 (`ReferenceProjectionWorker`); unchanged here except the event type's new home (`Norse.ReferenceData.Contracts`).
- **Business-effective rate-engine semantics.** ReferenceData provides the storage shape (single-table `WITHOUT OVERLAPS`); which factors exist and how they compose is Product-context work.
- **PG native system versioning.** PG18 ships temporal PKs/FKs, not `WITH SYSTEM VERSIONING`. If a later PG release lands it and Npgsql exposes it, the trigger emission inside `TemporalEntityConvention` swaps out; the contract surface does not move (§14 open question).

---

## 3. The Dependency Ruling — Infrastructure Composes, ReferenceData Forges

The Norse.Infrastructure.Persistence spec placed `TemporalEntityConvention` and `TemporalRepository<T>` in `Norse.Infrastructure.Persistence` (§14 there). CLAUDE.md and `docs/decomposition.md` say ReferenceData implements `ITemporalRepository<T>`. The contradiction is resolved in ReferenceData's favor, with one strict rule:

> **Infrastructure → ReferenceData → Abstractions. Never ReferenceData → Infrastructure. Nothing in ReferenceData names an Infrastructure type.**

- **ReferenceData forges** the temporal convention, the audit convention and interceptor, the migration SQL helpers, and the `TemporalRepository<T>` implementation. The repository's constructor takes the **EF base `DbContext`** — temporal queries are `FromSql` against timeline views plus EF materialization; nothing requires `BillingDbContext` or any per-service type.
- **Infrastructure composes.** `Norse.Infrastructure.Persistence` plugs ReferenceData's conventions into the `InfrastructureDbContext` convention set, registers ReferenceData's interceptor on every per-service context, and registers `TemporalRepository<T>` instances closed over the correct per-context `DbContext`. Infrastructure deals with the cards it is dealt: Abstractions deals the contracts, ReferenceData deals the fate hooks, Infrastructure wires them.

Mythological alignment is exact: the embodied realm is bound by the fates, not the other way around.

**Cost acknowledged:** the norse-abstractions-infrastructure / norse-infrastructure-persistence lockstep pair becomes a tripod with ReferenceData. A change to a stance marker ripples through three submodules. Accepted — the alternative (temporality inside Infrastructure) guts ReferenceData to a reference-data library wearing a profound name.

---

## 4. The `Norse.Abstractions.Persistence` Law Split

### 4.1 Why the split is forced

The self-configuration law (§5.3) puts EF Core types (`EntityTypeBuilder<T>`, `IEntityTypeConfiguration<T>`) on the entity marker interfaces. Today's marker home — `Norse.Abstractions.Infrastructure` — is referenced by `.Server` for `IDocumentRepository<T>`. Entities never bleed into `.Server`, but the *marker declarations* would, dragging EF Core transitively through the hard wall YGG003 defends.

The fix splits the law along the same wall the deployables already honor:

| Assembly | References EF? | Referenced by | Contents |
|---|---|---|---|
| `Norse.Abstractions.Infrastructure` | **No** | `.Server`, `.Worker`, Infrastructure, ReferenceData | `IDocumentRepository<T>` and any other server-visible contracts. Loses the entity markers, `TstzRange`, and the three worker-only repository contracts. |
| `Norse.Abstractions.Persistence` (new) | **Yes** | `.Worker`, Norse.Infrastructure.Persistence, ReferenceData | The marker family (§5), `TstzRange` (§6.2), `ICommandRepository<T>`, `ICachedRepository<T>`, `ITemporalRepository<T>`, `CacheLocallyAttribute`. |

### 4.2 A wall that holds by construction

`.Server` cannot reference `Norse.Abstractions.Persistence` (build-graph rule, same family as YGG003). Consequence: **the "worker-only repository contracts" analyzer rules die of natural causes.** `.Server` cannot see `ICommandRepository<T>` because the assembly declaring it is unreachable — compile-time enforcement by structure, not by analyzer. This honors the governing principle this spec adopts platform-wide:

> **Analyzers exist for what the compiler cannot natively enforce. Never write a YGG rule where a type constraint or an assembly boundary will do.**

### 4.3 Submodule home

`Norse.Abstractions.Persistence` lives in the **norse-abstractions-infrastructure submodule** (the law home), which already travels in lockstep with norse-infrastructure-persistence. No new submodule.

---

## 5. Entity Law

### 5.1 Records, not classes — CLAUDE.md amendment

Entities are **`record` types** (abstract base records, sealed concrete records). This amends CLAUDE.md §5's "`sealed class` for entities" line. Rationale: with init-only properties, `QueryTrackingBehavior.NoTracking` as the default, and updates flowing as fresh instances through `ICommandRepository<T>.UpdateAsync`, entities on this platform are immutable data — exactly what records are for.

Documented edges (in the spec so nobody rediscovers them in production):

- Record value equality compares navigation properties by reference inside generated equality. Do not rely on `==` between entity instances for identity decisions; identity is `Id` (or the composite key).
- `with` expressions clone the identity columns silently. Constructing the next version of an entity must be a deliberate act, not a casual `with`.

### 5.2 The marker family

```csharp
namespace Norse.Abstractions.Persistence;

// Root: "this type is persisted as rows in PostgreSQL."
public interface IPersisted { }

// Self-configuration law (CRTP). Inheriting IEntityTypeConfiguration<TSelf>
// makes the IDE demand Configure(EntityTypeBuilder<TSelf>) the moment a type
// declares itself persisted — compiler-prompted, not analyzer-policed.
public interface IPersisted<TSelf> : IPersisted, IEntityTypeConfiguration<TSelf>
  where TSelf : class, IPersisted<TSelf>
{ }

// ── Identity axis ──────────────────────────────────────────────────────────

// Scalar surrogate identity.
public interface IEntity : IPersisted
{
  Guid Id { get; }
}

// Composite business uniqueness ON TOP of a surrogate Guid Id (UUID v5 over
// the business key — same logical row, same Id, every environment). Use when
// the row carries payload or anything FKs to it.
public interface IBridgeEntity : IEntity { }

// Pure key-to-key association. Composite PK only — NO surrogate Id. Use when
// the row carries no payload and nothing FKs to it. A "changed" pair is a
// different row; UpdateAsync is analyzer-forbidden (YGG-rule TBD).
public interface ILinkEntity : IPersisted { }

// ── Stance axis (exactly one required — the forced-stance rule, §5.4) ──────

// System-versioned: history table, triggers, timeline view, AsOf queries.
public interface ITemporalEntity : IPersisted
{
  TstzRange SystemPeriod { get; init; }
}

// Write-once: third-party feeds, BDX rows. The rows ARE the history.
public interface IInsertOnlyEntity : IPersisted { }

// Seed-only reference data: no ICommandRepository<T>; the seed pipeline writes.
public interface IReadOnlyEntity : IPersisted { }

// Deliberately ahistorical: "I keep only current state, and I am saying so
// out loud." Idempotency records, ephemeral operational state. The typed act
// of declining history — the PlainText pattern applied to temporality.
public interface ICurrentOnlyEntity : IPersisted { }
```

**Decision rule for the identity axis:** payload or inbound FK → `IBridgeEntity`; neither → `ILinkEntity`.

**Why `ILinkEntity` drops the surrogate.** Nothing FKs to a payload-free link and nobody queries one by Guid. Currying a 16-byte column plus its index into every link row to support an API nobody calls is ceremony (§2.5 of CLAUDE.md). The divorce/remarry saga survives without it: delete ejects the row to history with its closed span; re-inserting the same pair opens a fresh `[now, 'infinity')`; the full saga of the pair is a timeline-view query on the two key columns.

### 5.3 Self-configuration — one file, whole story

Adding an entity to a `.Worker` assembly must force, via the type system, the declaration of keys, indexes, relationships, and constraints **in the same file as the properties**. Open one file, read everything; no codebase search.

The law is the interface (`IPersisted<TSelf>` inherits `IEntityTypeConfiguration<TSelf>`, so the IDE demands `Configure` immediately). The convenience is a family of thin CRTP base records carrying only the template method:

```csharp
namespace Norse.Abstractions.Persistence;

public abstract record TemporalEntityBase<TSelf> : IEntity, ITemporalEntity, IPersisted<TSelf>
  where TSelf : TemporalEntityBase<TSelf>
{
  public required Guid Id { get; init; }
  public required TstzRange SystemPeriod { get; init; }

  public void Configure(EntityTypeBuilder<TSelf> builder) => ConfigureEntity(builder);
  protected abstract void ConfigureEntity(EntityTypeBuilder<TSelf> builder);
}
```

One base per valid cell of the stance × identity matrix (`CurrentOnlyEntityBase<TSelf>`, `InsertOnlyEntityBase<TSelf>`, `ReadOnlyEntityBase<TSelf>`, `TemporalEntityBase<TSelf>`, plus `*BridgeBase<TSelf>` and `*LinkBase<TSelf>` variants). Single inheritance means compositions get pre-built bases rather than mix-ins; the matrix is small and ReferenceData ships them once. Exotic compositions may implement the interfaces directly — the `IPersisted<TSelf>` law still forces `Configure`.

**The bases carry no platform configuration.** Audit shadow columns, temporal storage apparatus, snake_case naming — all conventions (§6, §8), applied model-wide by Infrastructure's composition. `ConfigureEntity` is domain-only: keys, indexes, navigations, `MaxLength`, check constraints, table/schema mapping.

A worked entity:

```csharp
namespace {Company}.Policy.Worker;

internal sealed record Policy : TemporalEntityBase<Policy>
{
  public required Guid CustomerId { get; init; }
  public required string PolicyNumber { get; init; }
  public required DateOnly EffectiveDate { get; init; }
  public required decimal BoundPremium { get; init; }

  protected override void ConfigureEntity(EntityTypeBuilder<Policy> b)
  {
    b.ToTable("policies", schema: "policy");
    b.HasKey(p => p.Id);
    b.HasIndex(p => new { p.CustomerId, p.EffectiveDate });
    b.Property(p => p.PolicyNumber).HasMaxLength(40);
    b.Property(p => p.BoundPremium).HasPrecision(18, 2);
    // SystemPeriod, history table, timeline view, audit columns: ReferenceData conventions.
  }
}
```

**Documented wart:** `IEntityTypeConfiguration<TSelf>.Configure` is an instance method, and `required` members kill `new()`. Infrastructure's startup conjures one uninitialized instance per entity type via `RuntimeHelpers.GetUninitializedObject` to invoke `Configure`. One-time startup wiring — explicitly blessed by the platform's reflection rule — but it is a wart, recorded here, not a surprise.

**Navigations are not optional decoration.** Where a projection or business path needs a relationship, the navigation must be declared. The convention layer fails model finalization loudly when an FK column exists without its navigation (startup = enforcement layer 3 — still well upstream of production).

### 5.4 The forced-stance rule

> **Every persisted type declares exactly one stance.** Bare `IPersisted`/`IEntity` with no stance marker is a build error.

The danger was never the opt-in; it was the **silent omission** — a Claims entity shipped as plain `IEntity`, discovered eighteen months later when a dispute needs history that was never captured. Missing history is unrecoverable; therefore choosing "no history" must be a typed act (`ICurrentOnlyEntity`), not an absence. This is the YGG101 `PlainText` move applied to temporality: there is no default, only declared stances.

Enforcement: a `Norse.Primitives.Architecture` analyzer rule (number TBD — the compiler cannot natively force "implement exactly one of these four"), backed by a model-finalization check that fails startup if an undeclared type reaches the model. Two layers, both upstream of production.

The persistence spec's allow/forbid matrix extends:

| Composition | Status | Notes |
|---|---|---|
| Exactly one stance marker | required | `ITemporalEntity` / `IInsertOnlyEntity` / `IReadOnlyEntity` / `ICurrentOnlyEntity` |
| `ITemporalEntity` + `IInsertOnlyEntity` | forbid | Mutability contradiction (unchanged) |
| `IReadOnlyEntity` + `IInsertOnlyEntity` | forbid | Mutability contradiction (unchanged) |
| `ICurrentOnlyEntity` + any other stance | forbid | Stance is single-valued |
| `ILinkEntity` + `IEntity` (or `IBridgeEntity`) | forbid | Identity is single-valued |
| `ILinkEntity` + any stance | allow | Temporal links (appointment spans), insert-only links, read-only seeded links all legal |

### 5.5 The skip-navigation ban

EF's implicit many-to-many join entities are **banned**. A conjured shadow join type carries no stance marker, no deterministic identity, no audit columns — a stowaway dodging every law in this spec. Every many-to-many is an explicit `ILinkEntity` or `IBridgeEntity` class.

Enforcement: the convention layer detects skip navigations at model finalization and throws; a Roslyn rule (number TBD) catches collection-navigation pairs without an explicit join entity at compile time.

### 5.6 Repository contract adjustments

`ICommandRepository<T>` loosens its constraint from `IEntity` to `IPersisted` so links are admissible (its methods take entity instances, never a Guid):

```csharp
public interface ICommandRepository<TEntity> where TEntity : class, IPersisted
{
  Task AddAsync(TEntity entity, CancellationToken ct);
  Task UpdateAsync(TEntity entity, CancellationToken ct);   // analyzer-forbidden: IInsertOnlyEntity, ILinkEntity
  Task RemoveAsync(TEntity entity, CancellationToken ct);   // analyzer-forbidden: IInsertOnlyEntity
}
```

`ICommandRepository<T>` remains entirely unavailable for `T : IReadOnlyEntity` (analyzer, unchanged from the persistence spec). `ICachedRepository<T>` is unchanged. `ITemporalRepository<T>` is redesigned in §7.4.

---

## 6. Temporal Storage — `tstzrange`, `'infinity'`, and PG18

### 6.1 The period type ruling

**`tstzrange`, definitively.** Postgres' `timestamptz` stores no timezone — it stores 8 bytes of UTC microseconds, identical storage to `timestamp`; the "tz" suffix is a 25-year-old misnomer. `timestamptz` is the *absolute instant* type: input is normalized to UTC at the gate, and Npgsql refuses non-UTC `DateTime` kinds against it. `timestamp` ("without time zone") is the ambiguous type — a wall-clock reading with no declared relationship to UTC, where one misconfigured session timezone silently corrupts every `now()`-derived value. Choosing `tsrange` to "keep everything UTC" would have deleted the enforcement and kept only a convention — a §2.6 violation wearing a UTC costume.

Conversion to local time is, correctly, an edge concern: Npgsql pins the session and always materializes UTC; nothing converts until a UI or report renderer deliberately converts it.

### 6.2 The `'infinity'` sentinel and the amended `TstzRange`

Current rows carry `[lower, 'infinity')` — the true `'infinity'` special value (a reserved bit pattern, not a real timestamp; not `294276-12-31`, the storage ceiling; not `9999-12-31`, the .NET ceiling). Closing a period:

```sql
UPDATE {table}
   SET system_period = tstzrange(lower(system_period), now())
 WHERE id = @id AND upper(system_period) = 'infinity';
```

Npgsql (6+) performs infinity conversions by default: `'infinity'::timestamptz` materializes as `DateTimeOffset.MaxValue`, and `MaxValue` written back becomes `'infinity'` on disk. The .NET `MaxValue` is purely the in-process projection of the sentinel; it never touches disk as a timestamp. The `AppContext` switch `Npgsql.DisableDateTimeInfinityConversions` would break this silently — **a ReferenceData integration test pins the round-trip** (same pattern as the persistence spec's BSON-conventions pin test).

The value type makes the sentinel *named* — nobody reasons about `MaxValue` meaning anything, in 2026 or 2051:

```csharp
namespace Norse.Abstractions.Persistence;

public readonly record struct TstzRange
{
  // The only spelling of "open-ended" anyone reads or writes.
  public static readonly DateTimeOffset Infinity = DateTimeOffset.MaxValue;

  public required DateTimeOffset Lower { get; init; }
  public required DateTimeOffset Upper { get; init; }   // non-nullable; Infinity = current row

  public bool IsCurrent => Upper == Infinity;

  public static TstzRange CurrentFrom(DateTimeOffset since) => new()
  {
    Lower = since,
    Upper = Infinity,
  };

  public bool Contains(DateTimeOffset at) => at >= Lower && at < Upper;   // [lower, upper)
}
```

Amendments vs. the persistence spec's sketch: `Upper` is non-nullable (`DateTimeOffset?` null-means-unbounded was the ambiguous spelling — dead); the `RangeBoundType` pair is dropped — the platform pins `[lower, upper)` (lower-inclusive, upper-exclusive) as the only convention, and the value converter enforces it. F# consumers never see a `Nullable`. SQL predicates stay honest: `upper(system_period)` never returns NULL.

Documented trade: a genuine year-9999+ instant is unrepresentable. In an insurance platform, accepted with a clean conscience.

### 6.3 PG18 `WITHOUT OVERLAPS` temporal primary keys

PG18 (confirmed available on Neon) supports range types as the final member of a PK/UNIQUE constraint:

```sql
PRIMARY KEY (id, system_period WITHOUT OVERLAPS)
```

Same GIST index underneath as the persistence spec's hand-rolled exclusion constraint — but declared as the table's actual identity rather than bolted on. The exclusion constraint is retired. `FOREIGN KEY ... PERIOD` temporal FKs become available where both sides carry periods (used selectively; see §7.2 for why plain FKs are preserved where it matters).

EF has no native emission for this; ReferenceData's migration helpers emit it as raw SQL (§7.6).

### 6.4 The two temporality flavors map to two storage models

The flavors from the persistence spec (§5.2 there) are not just semantically distinct — they demand different physical layouts:

| Flavor | Question | Storage model | Temporal PK location |
|---|---|---|---|
| **System time** (`ITemporalEntity`) | "What did our system know on date X?" | **Current table + history table.** Main table holds only current rows: plain `PK (id)`, plain FKs from everywhere, lean indexes. Triggers eject prior versions to `{table}_history` on UPDATE/DELETE. | On the **history** table: `PRIMARY KEY (id, system_period WITHOUT OVERLAPS)` — replaces the persistence spec's `__history_id` surrogate; version-overlap corruption is structurally impossible where versions accumulate. |
| **Business-effective** (period is domain data: rate effective spans, appointment terms) | "Which rate applies on date Y?" | **Single table, all rows first-class.** The period is part of the row's identity; 2026 rates and 2027 rates are both *current domain data*. | On the **main** table: `PRIMARY KEY ({business key}, validity_period WITHOUT OVERLAPS)` — the domain invariant ("no overlapping effective rows") declared as law. |

Rationale for the split: single-table system versioning breaks plain FKs (`id` is no longer unique alone — every referencing table would need viral temporal FKs, or no FKs at all, violating CLAUDE.md §2.2) and accretes dead versions inline on hot tables. The current/history split is the same conclusion SQL Server's temporal tables reached, for the same reasons. Business-effective rows, conversely, would fight a current/history split immediately — all effective spans are live domain data.

Both flavors compose on one entity (the canonical `ClassFactor`: business-effective `(class_code, effective_date)` uniqueness + system-versioned corrections), unchanged from the persistence spec's §5.2/§10.5.

For pure links under system time, the divorce/remarry saga is free: same composite pair re-inserted after a period-closed delete reopens the association; the history table holds both disjoint spans under the temporal PK; the timeline view (§7.1) returns the full saga of the pair.

---

## 7. Time Traversal — Timeline Views and the Composable `AsOf`

### 7.1 Timeline views

For every `ITemporalEntity`, `TemporalEntityConvention` emits a view:

```sql
CREATE VIEW policy.policies_timeline AS
  SELECT * FROM policy.policies          -- current rows, [x, 'infinity')
  UNION ALL
  SELECT * FROM policy.policies_history; -- closed versions
```

The timeline view is the **uniform as-of relation**. `UNION ALL`, no dedup pass — the temporal PK already guarantees disjoint periods. Current-row queries through the non-temporal repositories still hit only the lean main table; as-of queries pay only for what they ask.

### 7.2 Why this kills the SQL Server scar

EF's SQL Server `TemporalAsOf` is single-entity because every temporal table is *two* relations, and a cross-entity as-of join means hand-stitching N unions. With timeline views, every participant speaks the same predicate, and reconstructing a parent/child aggregate across independently-versioned entities is an ordinary join:

```sql
SELECT p.*, c.*
FROM policy.policies_timeline p
JOIN policy.coverages_timeline c ON c.policy_id = p.id
                                AND c.system_period @> @at
WHERE p.id = @id AND p.system_period @> @at
```

One clock (`@at`), N entities, zero unions in user code — the views swallowed them.

### 7.3 The history-table apparatus

Per `ITemporalEntity`, the convention emits (via the migration helpers, §7.6):

0. `CREATE EXTENSION IF NOT EXISTS btree_gist` — a hard prerequisite: `WITHOUT OVERLAPS` builds a GiST index mixing scalar key columns with the range column, and the scalar opclasses come from `btree_gist`. Emitted once per database, before any temporal apparatus. (Neon supports it.)
1. `system_period tstzrange NOT NULL` on the main table, defaulted to `tstzrange(now(), 'infinity')`.
2. `{schema}.{table}_history` — same columns; `PRIMARY KEY (id, system_period WITHOUT OVERLAPS)` (for links: the composite key columns + period).
3. UPDATE/DELETE triggers on the main table that close the prior version's period and insert it into history. Trigger functions are owned by the migration role (`SECURITY DEFINER`) so the runtime role needs no direct write grant on history (§10).
4. The `{table}_timeline` view.

### 7.4 The redesigned `ITemporalRepository<T>`

```csharp
namespace Norse.Abstractions.Persistence;

public interface ITemporalRepository<TEntity>
  where TEntity : class, ITemporalEntity
{
  // Composable LINQ root over the timeline view, pre-filtered to
  // system_period @> at. Joins, Where, projections translate server-side.
  IQueryable<TEntity> AsOf(DateTimeOffset at);

  // Unfiltered timeline root: every version of every row. For history
  // queries by arbitrary predicate (e.g., a link pair's full saga).
  IQueryable<TEntity> Timeline();
}
```

`AsOf(at)` is EF `FromSql` against the timeline view — `FromSql` roots compose, so `repo.AsOf(at).Where(...)` and joins between two timeline roots translate to the SQL in §7.2. The Guid conveniences become **extension methods constrained to `T : IEntity`** — compile-time unavailable for links, no analyzer involved:

```csharp
public static class TemporalRepositoryExtensions
{
  public static Task<TEntity?> AsOfAsync<TEntity>(
    this ITemporalRepository<TEntity> repo, Guid id, DateTimeOffset at, CancellationToken ct)
    where TEntity : class, ITemporalEntity, IEntity
    => repo.AsOf(at).SingleOrDefaultAsync(e => e.Id == id, ct);

  public static async Task<IReadOnlyList<TEntity>> HistoryAsync<TEntity>(
    this ITemporalRepository<TEntity> repo, Guid id, CancellationToken ct)
    where TEntity : class, ITemporalEntity, IEntity
  {
    var versions = await repo.Timeline().Where(e => e.Id == id).ToListAsync(ct);
    return versions.OrderBy(e => e.SystemPeriod.Lower).ToList();   // in-memory: histories are small;
  }                                                                // SystemPeriod.Lower doesn't translate
}
```

(`AsOfRangeAsync` from the persistence spec's contract is subsumed by `Timeline()` + LINQ; it is dropped.)

`Norse.ReferenceData.Temporal`'s `TemporalRepository<TEntity>` implements the contract; its constructor takes the EF base `DbContext` (§3). Infrastructure registers one per temporal entity type against the owning per-context DbContext.

### 7.5 Worker-only by construction

`ITemporalRepository<T>` lives in `Norse.Abstractions.Persistence`, unreachable from `.Server` (§4.2). Operational HTTP reads remain point-in-time wire shapes from Mongo; time traversal is worker, admin, and Warehouse territory — unchanged posture, stronger enforcement.

### 7.6 Migration helpers

`Norse.ReferenceData.Temporal` ships `MigrationBuilder` extensions so per-context migration packages never hand-roll the apparatus:

```csharp
migrationBuilder.AddSystemVersioning<Policy>();    // history table + temporal PK + triggers + timeline view
migrationBuilder.AddStanceGrants<Policy>();        // §10 role grants
```

Emitted SQL is deterministic and idempotent-guarded, so re-running a migration in a recovery scenario is safe.

---

## 8. The Urd Audit Substrate (V1)

### 8.1 Scope ruling

**Row history + who is the whole of V1.** The history tables are the audit trail of *what*; three shadow columns answer *who, from where, through which chain* — on **every** stance, not just temporal (the insert-only BDX row and the current-only operational row want attribution just as much).

### 8.2 The audit shadow columns

`AuditConvention` (in `Norse.ReferenceData.Audit`, composed by Infrastructure) adds shadow properties to every `IPersisted` entity type:

| Column | Type | Content |
|---|---|---|
| `audit_user_id` | `uuid` | The originating principal (staff, producer, customer, or M2M client) |
| `audit_user_ip` | `varchar(45)` | Client IP at the edge (45 = IPv6 max) |
| `audit_conversation_id` | `varchar(36)` | NSB conversation id — links the row version to the exact message chain that produced it |

Shadow properties: present in the model and the database, absent from the C# entity records — domain code cannot read, fake, or forget them. History rows carry the columns too (the history table mirrors the main table), so every *version* knows its author.

The conversation id is the forensic keystone: ServicePulse shows the chain, the history table shows the data, and "unwind something" becomes a join instead of an investigation.

### 8.3 Principal propagation — the plumbing handlers never see

The mutation happens in an NSB handler with no HTTP context. The audit facts travel in message headers:

1. **At the edge (`.Server`):** a ReferenceData-shipped outgoing NSB pipeline behavior stamps `norse.audit.user_id`, `norse.audit.user_ip` (from `NorsePrincipal` + connection info) onto every `Send`.
2. **Hop to hop (`.Worker`):** the behavior pair copies audit headers from the incoming message onto every outgoing message a handler sends — the *originating* user rides the entire command chain regardless of depth. (NSB propagates `ConversationId` natively; ReferenceData only adds the two custom headers.)
3. **At the flush:** an incoming behavior surfaces the headers as an ambient audit context (`AsyncLocal`); `AuditSaveChangesInterceptor` stamps the shadow columns on every added/modified entry during the pre-commit EF flush (the `OnSaveChanges` callback inside NSB's `ISqlStorageSession` commit — see the persistence spec §4.2).

No handler sees, touches, or can forget the plumbing. Seed-pipeline writes stamp a well-known seed principal id so reference rows are attributed too.

**Hard-fail posture:** a worker-side flush with no ambient audit context is an error, not a NULL — a mutation with unattributable provenance means a code path skipped the pipeline, and that fails loudly at the boundary (layer 4), not silently into the audit record.

### 8.4 What is deferred, and what brings it back

A separate append-only audit *event* store — security-relevant reads, PII access logging, auth event streams, export tracking, TimescaleDB hypertables — is **deferred** (dragon-sizing: no current consumer). Re-entry triggers, any one sufficient:

1. A regulatory or fronting-carrier requirement to log *reads* of PII.
2. SOC 2 (or equivalent) audit scope landing on the platform.
3. The first real request for a "who accessed this customer's data" report.

Row-version capture needs no retrofit when that lands — the event store is additive.

---

## 9. The Verdandi Reference Fabric

### 9.1 Packaging — hybrid

| Dataset class | Examples | Packaging |
|---|---|---|
| Public domain | SIC, NAICS, ZIP/FIPS geography, ISO 3166 / 4217, units of measure, state codes | **`Norse.ReferenceData.Data.{Dataset}` NuGet packages** — embedded TSV resources + a manifest (the pattern proven at the fronting carrier). Independently versioned: a NAICS revision is a package version bump. |
| Licensed | NCCI class codes & rate data, ISO (insurance) forms, purchased datasets | **Operator file-drops** at seed time. Never committed, never packaged, never on a feed. The manifest schema is identical; only the source differs. |

Each dataset manifest declares: dataset id, version, target entity type, column mappings, the declared culture/format for every parsed column (ambiguity is a parse failure — §2.6; the BDX lessons apply verbatim), and the UUID v5 derivation columns.

### 9.2 Deterministic identity

Reference rows derive `Id` via UUID v5 over the Primitives namespace registry from their business key columns — same logical row, same Guid, every environment, every reseed. Re-running a seed is an upsert keyed on deterministic identity, never a duplicate.

### 9.3 Application — deploy-time job + CLI

**Primary path — `Norse.Hosting.Migrations.Service`:** after schema migrations, the orchestrator runs the ReferenceData seed engine over every referenced `Norse.ReferenceData.Data.*` package. Azure App Configuration sentinel keys (`ReferenceData:Seeds:{dataset}` = applied version) skip datasets whose package version hasn't changed — the same sentinel mechanics the migration job already uses for DACPAC-era skipping. On any applied change, the engine publishes `ReferenceDataReloadedEvent { EntityType, DatasetVersion, EffectiveAt }` (now living in `Norse.ReferenceData.Contracts`); the Infrastructure `ReferenceProjectionWorker` and worker-local LRU invalidation consume it unchanged.

**Secondary path — `Norse.ReferenceData.Cli`** (Spectre.Console.Cli): `norns seed apply --dataset ncci --file ./drop/ncci-2026.tsv` for licensed file-drops and ad-hoc corrections; `norns seed verify` re-derives and compares (drift detection); `norns seed status` reads the sentinel keys. The CLI drives the same engine — one code path, two invokers.

Seeds write Postgres through the engine (platform tooling, not application code — the `IReadOnlyEntity` analyzer rules don't apply to it), under the seed role (§10), inside a transaction per dataset.

### 9.4 Temporal reference data

A reference dataset that is also `ITemporalEntity` (NCCI factor corrections over time) gets the full §6–§7 apparatus automatically — the seed engine's upserts close and open system periods via the same triggers as any other write. "What did we think the 2027 rates were on 2026-11-15?" is `ITemporalRepository<ClassFactor>.AsOf(...)`, unchanged from the persistence spec's worked example.

---

## 10. Database Self-Defense — Role Grants Per Stance

The analyzer forbids misuse at compile time; CLAUDE.md §2.2 demands the database defend itself *regardless of how data arrives*. Stance markers project onto Postgres role grants, emitted by `AddStanceGrants<T>()` in migrations:

| Stance | Runtime role (`{company}_app`) | Seed role (`norns_seed`) |
|---|---|---|
| `ICurrentOnlyEntity` | SELECT, INSERT, UPDATE, DELETE | — |
| `ITemporalEntity` (main table) | SELECT, INSERT, UPDATE, DELETE | — |
| `ITemporalEntity` (history table) | **SELECT only** — versions arrive via `SECURITY DEFINER` triggers | — |
| `ITemporalEntity` (timeline view) | SELECT | — |
| `IInsertOnlyEntity` | **SELECT, INSERT only** | — |
| `IReadOnlyEntity` | **SELECT only** | SELECT, INSERT, UPDATE, DELETE |

A compromised handler, a raw-SQL hot path, and a fat-fingered psql session all hit the same wall: the runtime role simply does not hold the grant. Compile-time analyzer + database grant = two independent walls per stance; neither trusts the other.

Roles are per-stamp provisioning artifacts (tenancy spec); Neon role management supports this topology. The migrations role owns the schema, the trigger functions, and the grants.

Verification tests (§15) attempt every forbidden operation as the runtime role and assert failure.

---

## 11. Realm Placement and Project Structure

```
Norse.Abstractions.Persistence                   (Abstractions realm, declared law — NEW; lives in norse-abstractions-infrastructure submodule)
  ├── IPersisted / IPersisted<TSelf>          root marker + self-configuration law
  ├── IEntity / IBridgeEntity / ILinkEntity   identity axis
  ├── ITemporalEntity / IInsertOnlyEntity /   stance axis
  │   IReadOnlyEntity / ICurrentOnlyEntity
  ├── TstzRange                               amended: non-null Upper, named Infinity, IsCurrent
  ├── ICommandRepository<T>                   relocated; constraint loosened to IPersisted
  ├── ICachedRepository<T>                    relocated
  ├── ITemporalRepository<T>                  relocated; redesigned (AsOf/Timeline composable roots)
  ├── TemporalRepositoryExtensions            Guid conveniences, T : IEntity-constrained
  ├── CacheLocallyAttribute                   relocated
  └── {Stance}{Identity}EntityBase<TSelf>     thin CRTP template-method base records

Norse.Abstractions.Infrastructure                (Abstractions realm, declared law — EF-free; .Server-visible)
  └── IDocumentRepository<T> + server-visible contracts (entity markers et al. removed)

Norse.ReferenceData.Contracts                      (ReferenceData realm — POCO messages + manifest shapes, no NSB/EF refs)
  ├── ReferenceDataReloadedEvent              relocated from persistence spec §9
  └── DatasetManifest shapes

Norse.ReferenceData.Temporal                       (ReferenceData realm — Skuld)
  ├── TemporalEntityConvention                system_period column, history table, triggers,
  │                                           WITHOUT OVERLAPS temporal PKs, timeline views
  ├── TemporalRepository<T>                   ctor takes EF base DbContext; FromSql vs timeline views
  ├── TstzRangeValueConverter                 relocated from Infrastructure; pins [lower, upper) + infinity
  └── Migration helpers                       AddSystemVersioning<T>(), AddStanceGrants<T>()

Norse.ReferenceData.Audit                          (ReferenceData realm — Urd)
  ├── AuditConvention                         audit_user_id / audit_user_ip / audit_conversation_id
  │                                           shadow properties on every IPersisted type
  ├── AuditSaveChangesInterceptor             stamps shadow columns at the pre-commit flush
  ├── Outgoing/incoming NSB pipeline behaviors header stamping + hop-to-hop forwarding
  └── Ambient audit context                   AsyncLocal surface the interceptor reads

Norse.ReferenceData.Reference                      (ReferenceData realm — Verdandi)
  ├── Seed engine                             Sep-based TSV reader, manifest validation,
  │                                           UUID v5 derivation, transactional upsert, sentinel keys
  └── ReferenceDataReloadedEvent publication

Norse.ReferenceData.Data.{Dataset}                 (ReferenceData realm — versioned data packages)
  └── Norse.ReferenceData.Data.Naics / .Sic / .Geography / .Iso4217 / .UnitsOfMeasure / …
      embedded TSV + manifest; licensed datasets are NOT packaged (file-drop only)

Norse.ReferenceData.Cli                            (ReferenceData realm — Spectre.Console.Cli tool)
  └── norns seed apply / verify / status

Norse.Infrastructure.Persistence                  (Infrastructure realm — composes)
  ├── plugs TemporalEntityConvention + AuditConvention into InfrastructureDbContext
  ├── registers AuditSaveChangesInterceptor on every per-context DbContext
  ├── registers TemporalRepository<T> per temporal entity, closed over the owning DbContext
  └── invokes each entity's Configure via GetUninitializedObject at startup (documented wart)

Norse.Hosting.Migrations.Service                 (Hosting realm)
  └── gains the seed phase: schema migrations → ReferenceData seed engine → ReferenceDataReloadedEvent
```

Solution: `Norse.ReferenceData.slnx` at the ReferenceData submodule root, standard `src/{ProjectName}/{ProjectName}.csproj` layout. The fates (Urd/Verdandi/Skuld) remain documentation flavor; assembly names stay descriptive per CLAUDE.md naming law.

---

## 12. Resolved Decisions

1. **Dependency direction: Infrastructure composes, ReferenceData forges.** Norse.Infrastructure.Persistence → ReferenceData → Abstractions, one direction; nothing in ReferenceData names an Infrastructure type. `TemporalRepository<T>` takes the EF base `DbContext`. Resolves the CLAUDE.md vs. persistence-spec contradiction in ReferenceData's favor.
2. **`Norse.Abstractions.Persistence` law split.** Entity markers, `TstzRange`, and the worker-only repository contracts move to a new EF-referencing law assembly unreachable from `.Server`. The worker-only analyzer rules retire — the wall holds by construction.
3. **Governing principle adopted:** analyzers exist for what the compiler cannot natively enforce; never write a YGG rule where a type constraint or assembly boundary will do.
4. **`tstzrange` stands; `tsrange` rejected.** `timestamptz` is the absolute-instant (UTC) type; `timestamp` is ambiguous-by-construction. Local-time conversion stays at the edges, which `tstzrange` already provides.
5. **`'infinity'` sentinel for current rows.** `[lower, 'infinity')`; `TstzRange.Upper` non-nullable with named `Infinity`; `RangeBoundType` dropped — `[lower, upper)` is the only convention. Npgsql infinity-conversion behavior pinned by integration test.
6. **PG18 `WITHOUT OVERLAPS` temporal PKs** replace the hand-rolled GIST exclusion constraint. Neon-verified.
7. **Storage model split by temporality flavor.** System time → current + history table (plain FKs preserved; temporal PK guards history). Business-effective → single table with `WITHOUT OVERLAPS` as the domain invariant. Both compose on one entity.
8. **Timeline views** (`{table}_timeline` = current `UNION ALL` history) as the uniform as-of relation — kills the SQL Server single-entity-AsOf scar; cross-entity time-slice joins are ordinary joins.
9. **Composable temporal contract.** `ITemporalRepository<T>` = `AsOf(at)` + `Timeline()` `IQueryable` roots; Guid conveniences are `T : IEntity`-constrained extensions; `AsOfRangeAsync` dropped as subsumed.
10. **Forced explicit stance.** Every persisted type declares exactly one of temporal / insert-only / read-only / current-only; bare markers are a build error. `ICurrentOnlyEntity` is the typed act of declining history (the `PlainText` pattern).
11. **Record entities** — CLAUDE.md §5 amended from `sealed class`; edges documented (navigation equality, `with` clones identity).
12. **CRTP self-configuration.** `IPersisted<TSelf> : IEntityTypeConfiguration<TSelf>` makes configuration compiler-prompted; thin per-stance base records carry the `Configure → ConfigureEntity` template method; platform concerns live in conventions, never bases. `GetUninitializedObject` startup wart documented.
13. **`ILinkEntity`: pure key-to-key links, composite PK, no surrogate.** Decision rule: payload or inbound FK → `IBridgeEntity`; neither → `ILinkEntity`. `UpdateAsync` analyzer-forbidden on links. Divorce/remarry saga preserved via timeline view on the pair.
14. **Skip-navigation join entities banned** — every M2M is an explicit class; convention throws at model finalization, analyzer catches at compile time.
15. **Urd V1 = row history + who.** Three audit shadow columns (`audit_user_id`, `audit_user_ip`, `audit_conversation_id`) on every stance, stamped by interceptor from an ambient context fed by NSB header propagation; missing context at flush is a hard failure. Separate audit-event store deferred with re-entry triggers (§8.4).
16. **Hybrid reference-data packaging.** Public datasets as versioned `Norse.ReferenceData.Data.*` NuGet with embedded TSV; licensed datasets are operator file-drops, never committed or packaged.
17. **Dual seed application.** `Norse.Hosting.Migrations.Service` applies packaged datasets at deploy (sentinel-key skipped); `Norse.ReferenceData.Cli` covers licensed drops and ad-hoc loads; one engine, two invokers.
18. **Role grants per stance** emitted by migration helpers: runtime role lacks UPDATE/DELETE where the stance forbids it; history tables writable only via `SECURITY DEFINER` triggers; seed role owns reference writes. Two independent walls (compiler + database) per stance.
19. **CLIs are named and placed by their scope realm — `Norse.ReferenceData.Cli` lives in ReferenceData.** A single platform-wide CLI would belong to Hosting; a fleet of small, focused CLIs belongs with the domains they serve (precedent: the fronting carrier's data CLI lives with its domain). Resolves the placement flag.

---

## 13. Amendments Carried to Existing Documents

### 13.1 `2026-05-21-midgard-persistence-design.md`

- §4.2/§4.4: `ICommandRepository<T>` constraint loosens to `IPersisted`; `ITemporalRepository<T>` redesigned per §7.4 here; contracts relocate to `Norse.Abstractions.Persistence`.
- §5: marker hierarchy superseded by §5.2 here (`IPersisted` root, `ILinkEntity`, `ICurrentOnlyEntity`, forced stance, composition matrix extension).
- §10: `TstzRange` shape change (§6.2 here); GIST exclusion replaced by `WITHOUT OVERLAPS` (§6.3); two-table fall-through in `AsOfAsync` replaced by timeline views (§7.1); `TstzRangeValueConverter` and `TemporalEntityConvention` relocate to `Norse.ReferenceData.Temporal`.
- §14: `TemporalEntityConvention` and `TemporalRepository<T>` move out of the `Norse.Infrastructure.Persistence` listing; `ReferenceDataReloadedEvent` moves to `Norse.ReferenceData.Contracts`.
- §15.2 worked example: entity becomes a `record` deriving `TemporalEntityBase<Policy>`.

### 13.2 CLAUDE.md

- §4 Persistence: repository contract family location (`Norse.Abstractions.Infrastructure` → split with `Norse.Abstractions.Persistence`); "implemented by ReferenceData" line now accurate as written.
- §5 Classes and Records: entities become records (with the documented edges).
- §5 Namespaces: ReferenceData realm entry gains the assembly list (`Norse.ReferenceData.Temporal`, `Norse.ReferenceData.Audit`, `Norse.ReferenceData.Reference`, `Norse.ReferenceData.Contracts`, `Norse.ReferenceData.Data.*`, `Norse.ReferenceData.Cli`).
- §8 Anti-patterns: add the skip-navigation ban and the forced-stance rule.

### 13.3 YGG rule catalog (`Norse.Primitives.Architecture`)

**New rules (numbers slotted at next-available):** forced stance (exactly one per persisted type); skip-navigation ban; `UpdateAsync`/`RemoveAsync` forbidden for `IInsertOnlyEntity` (existing) extended with `UpdateAsync` forbidden for `ILinkEntity`; `ICommandRepository<T>` forbidden for `IReadOnlyEntity` (existing, unchanged).

**Retired rules:** worker-only repository contract checks (`ICommandRepository`/`ICachedRepository`/`ITemporalRepository` in `.Server`) — now structurally impossible (§4.2).

---

## 14. Open Questions

1. **PG native system versioning.** If a future Postgres lands `WITH SYSTEM VERSIONING` and Npgsql exposes it, `TemporalEntityConvention` swaps trigger emission for the native clause; `ITemporalRepository<T>` does not change. Tracked, not designed. (PG19 does **not** ship it — it completes the *application-time* half instead; see #2.)
2. **PG19 `FOR PORTION OF` adoption (business-effective DML).** PG19 adds `UPDATE/DELETE ... FOR PORTION OF`, completing SQL:2011 application time: mid-span corrections become engine-performed row splits — flanks preserved automatically under the `WITHOUT OVERLAPS` PK, promoting "slice edits cannot create gaps or overlaps" from application arithmetic to declared engine behavior (§2.2 alignment). This spec's Model A shape (range column in a temporal PK) is exactly the substrate `FOR PORTION OF` consumes, so adoption is purely additive DML — no schema migration, no contract change. Adopt inside implementations (seed-engine corrections first) when Neon ships PG19. Trigger semantics are documented (Neon PG19 docs): the portion row fires an UPDATE trigger (Urd's history captures the full original span as the prior version) and leftover flanks fire genuine INSERT triggers (fresh `[now, 'infinity')` system periods) — composing correctly with the §7.3 apparatus; a confirm-the-doc integration test pins it. Implementation caveats: `RETURNING` and affected-row counts exclude the auto-inserted leftovers (rowcount assertions must not expect flanks); only UPDATE/DELETE privilege is required (leftover inserts need no INSERT grant — §10 matrix unaffected); `daterange` and multiranges are supported (business-effective insurance periods are mostly date-grained — Product-context detail); `NULL` bounds express open-ended portions. **Reconnaissance executed against beta1 on release day (2026-06-04) — full verdicts in `poc/pg19-temporal/FINDINGS.md`.** Headlines: the §6.4 storage split stands unchanged (plain-FK impossibility against single-table temporal parents empirically confirmed); temporal-FK **aggregated coverage CONFIRMED** (a child span across contiguous parent versions inserts cleanly — the Neon doc's single-row-containment claim is wrong; version splits don't strand children), though PERIOD FKs remain viral and `ON DELETE CASCADE` is unsupported in beta1; `FOR PORTION OF` trigger/RETURNING/rowcount semantics match this section's text exactly, de-risking Model A adoption; a "single-table temporal" variant for non-referenced leaf entities proved viable in beta1 (single-statement system versioning, RLS-protected history, modest read penalty) but is **parked** — its immutability story rests on leftover inserts bypassing RLS `WITH CHECK`, which is the likely subject of a beta open item and may reverse before GA. Re-verify at RC1. No new contract surface until a context demands a portion edit.
3. **Dataset roster for V1 packages.** Which `Norse.ReferenceData.Data.*` packages ship first (driven by §7 #6/#7 — line-of-business and state scope, still open in CLAUDE.md). The engine and packaging design do not depend on the answer.
4. **Analyzer rule numbers.** Slotted at next-available in the `Norse.Primitives.Architecture` catalog when this spec lands, coordinated with the architecture-analyzers spec.
5. **Seed principal identity.** The well-known Guid the seed pipeline stamps into `audit_user_id` — registry entry in the Primitives UUID namespace registry; exact namespace path decided when the registry lands.

---

## 15. Acceptance

This spec is "done enough to implement" when:

- `Norse.Abstractions.Persistence` compiles with the full marker family, amended `TstzRange`, relocated/redesigned repository contracts, extension conveniences, and the CRTP base-record set, against .NET 10 + EF Core 10.
- `Norse.ReferenceData.Temporal` emits, for a sample `ITemporalEntity` in a real PG18 database (testcontainers): the `system_period` column, the history table with `WITHOUT OVERLAPS` temporal PK, working close-and-eject triggers, and the timeline view — and `TemporalRepository<T>.AsOf(at)` joins across two timeline roots translate to a single server-side query.
- The Npgsql infinity round-trip pin test passes (write `TstzRange.Infinity`, read back `'infinity'` on disk via raw introspection, materialize `Infinity` in .NET).
- `Norse.ReferenceData.Audit`'s behaviors propagate `audit_*` headers through a two-hop NSB command chain and the interceptor stamps all three shadow columns on a worker-side write; a flush with no ambient context fails loudly.
- The seed engine applies a sample `Norse.ReferenceData.Data.*` package idempotently (second run = no-op, identical UUID v5 ids), honors sentinel keys, and publishes `ReferenceDataReloadedEvent` on change.
- Grant-enforcement tests: the runtime role's forbidden operations (UPDATE on read-only, UPDATE/DELETE on insert-only, direct write to a history table) each fail at the database.
- The forced-stance and skip-navigation checks fail model finalization for violating sample models.
- The Norse.Infrastructure.Persistence spec amendments (§13.1) and CLAUDE.md updates (§13.2) are staged in the same PR.
