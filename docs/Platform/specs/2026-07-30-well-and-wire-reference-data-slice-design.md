# The Well and the Wire: Reference Data Vertical Slice — Design

**Status:** Spec — ratified in design session 2026-07-30; supersedes the brainstorm of the same name
**Realms touched:** Asgard, Midgard, Mimir, Mimisbrunnr (Svartalfheim and Yggdrasil consume/host only; Urdarbrunnr is untouched — see §2)
**Purpose:** Land the smallest end-to-end slice — ISO 3166-1 country lookup over gRPC into WASM — that proves the patterns, locations, and laws for the entire Mimir/Mimisbrunnr reference data tree and, more importantly, for every future well (Billing, Claims, Policy). Reference data is the seam, not the point. The point is the repository doctrine.

---

## 1. Rulings

Every open question from the brainstorm, resolved. These are the session's verdicts; the rationale is recorded inline so the next reader does not re-litigate from scratch.

| # | Question | Ruling |
|---|---|---|
| 1 | Promoted-member discovery | **Convention with total validation** (§4.2). No per-member attribute; an exclusion attribute exists only for declared exceptions. |
| 2 | Contract placement | **Asgard `Abstractions.Backend`** hosts both `IReadRepository<TView>` and `IViewBearer<TView>` (§3). Urdarbrunnr gains nothing; consumers look where they already look — at declared law. |
| 3 | Generator ergonomics | Raw UNSD CSV moves to Mimir; generator emits enum + parse surface + dataset + **precomputed** v5 GUIDs from four columns (§6). |
| 4 | `Outcome<T>` home | **Stays in Asgard.** The Urdarbrunnr→Asgard edge is recorded as legal doctrine — it already exists de facto (`Persistence.EntityFramework.Migrations` references `Abstractions.Migrations`) — but this slice no longer needs it: the contract moved to Asgard, beside its envelope. |
| 5 | Write side | Named, not designed: command/write contracts land in `Abstractions.Backend` beside the read contract when their day comes. Midgard implements; wells stay dumb. |
| 6 | View `Id` exposure | Yes — `TView` exposes `Id` (`CountryOrAreaView` already does). The contract shape makes it impossible to use as the hot filter: the identity path takes no predicate. |

**Combinators are dead.** The brainstorm ratified `.OrDefault()`/`.Ignore(NotFound)` envelope combinators; the author overrode that ratification in session. Acceptability of absence is a call-site judgment expressed in the `Match`, not a property marshalled into the envelope. The old world's `if (result == null) { /* not-found thing */ }` becomes the `NotFound` arm of the match — same ergonomics, reduced API footprint, and the compiler forces the arm to exist where the null test never did. Consequence: `Outcome<T>`'s starved API (`Ok`/`Err`/`Match`) is untouched and `the-two-unions.md` needs **no amendment** — which is itself evidence the design sits where it should.

**Amended from the brainstorm's decision 1.** "Urdarbrunnr defines the data-access abstractions, laws, interfaces, and behaviors for wells" is corrected to match platform precedent: **the law lives in Asgard, like all law; Urdarbrunnr embodies persistence mechanics; wells contribute entities conforming to the law.** This is the migrations framework's own shape — Asgard declared `IMigrationContributor`, Midgard ran it, Urdarbrunnr shipped the EF foundation. Asgard's charter is literally "declared law: contracts, attribute model, plugin interfaces."

Carried unchanged from the brainstorm (not restated here in full): deterministic GUID namespace chaining from a single hand-minted root; the v5 name form for ISO 3166-1 is the zero-padded numeric code (`"840"`) — a forever decision, and every future dataset must declare its frozen name form in its spec; byte order is settled and appears only as an E2E assertion; slice one ships on the RDBMS with the JSON view column; identity lives on the row, never the JSON path.

## 2. Topology

New projects and edges, in dependency order:

| Realm | Change | Contents |
|---|---|---|
| Asgard | `Abstractions.Backend` gains its first content | `IViewBearer<TView>`, `IReadRepository<TView>`, the exclusion attribute. References `Abstractions.Contracts` for `Outcome<T>`. Server-side shared law — `.Backend`'s exact charter (consumed by `.Server` and `.Worker`, never Components/WASM). |
| Midgard | New `src/Infrastructure.Persistence.EntityFramework` | `Repository<TContext, TEntity, TView>`, the predicate rewriter, `AddWell<TContext>()`. References Asgard (`Abstractions.Backend`, `Abstractions.Contracts`) and Urdarbrunnr (`Persistence.EntityFramework`). Vendor-family named from day one — a future MongoDB implementation lands as a sibling project, no extraction surgery. |
| Mimir | First projects: `src/Reference.Contracts`, `src/Reference.Seeds`, `src/Reference.Web.Server` (the slice's handler + gRPC service home, §7.2), `gen/Reference.Contracts.Generator` | The raw `UNSD — Methodology.csv` moves here (`seeds/raw/`); the generator (netstandard2.0, `gen/` per the Urdarbrunnr repackaging pattern, emitting via `AppendCSharp`) parses it as `AdditionalFiles` in `Reference.Contracts`. `Reference.Seeds` is the **server-only raw-dataset home** — embeds the same physical file via link + stream accessor (see raw data ownership below). `.Contracts` is the only cross-context-referenceable project — exactly its charter. Both join the shared `Norse.Reference` namespace family. |
| Mimisbrunnr | Gains references to `Reference.Contracts` and `Abstractions.Backend`; keeps `SeedTool` + relational TSVs; `SeedTool` references `Reference.Seeds` for the raw bytes | Entity retrofit (§7.1); seed resolves country `Id`s through the generated GUID surface — an unknown code fails the seed loudly. |

**Edges recorded as doctrine:**

- **Urdarbrunnr→Asgard is legal.** Both are abstraction realms; the edge exists de facto today. Recorded so the next design does not treat it as a question.
- **Mimir `.Web.Server`→Mimisbrunnr `Reference.Data` for view types.** Already sanctioned — Mimisbrunnr's own CLAUDE.md declares its entities and view models the interop boundary. View types are realm-owned; services see views, never entities.

**Raw data ownership.** `seeds/raw/UNSD — Methodology.csv` lives in Mimir — one physical file, single owner, two build-time consumers, zero checkout-topology assumptions, zero bytes to the browser:

1. **`Reference.Contracts`** includes it as `AdditionalFiles`; the generator emits the identity/parse surface at compile time.
2. **`Reference.Seeds`** (server-only) includes the same physical file as an `EmbeddedResource` via link, plus a one-method accessor returning the stream. `SeedTool` in Mimisbrunnr references `Reference.Seeds` and flattens the raw bytes into the relational TSVs (`region.tsv`, `country-or-area.tsv`) — entity modeling and referential integrity are the well's concern, not Mímir's; the region/ancestry columns ride the raw bytes untouched. Nothing in the WASM dependency graph ever touches `Reference.Seeds`, enforced by the same charter language that keeps `Abstractions.Backend` off the client.

A whole project for one CSV is admittedly heavy — but `Reference.Seeds` is the natural home for every future dataset's raw file (ISO 4217, ISO 639, IANA tzdata), so it amortizes across the reference tree rather than being ceremony for one country list.

Drift between the derivations is structurally loud, not silently possible: the seed types codes through the generated enum and resolves `Id`s through the generated GUID lookup, so a country present in the TSVs but unknown to the enum fails the seed immediately.

**Superseded records, fixed in this same change (boy-scout law):**

- The **Urdarbrunnr : Mimisbrunnr :: Asgard : Midgard analogy** from the design conversation → **retired**, superseded by ruling 2 and the amended decision 1: law lives in Asgard, Urdarbrunnr embodies persistence mechanics, wells contribute conforming entities. There is no two-abstraction-realms model; recorded dead so no future session resurrects it.

- Glitnir `CLAUDE.md` §4's four-contract family (`IDocumentRepository<T>`, `ICommandRepository<T>`, `ICachedRepository<T>`, `ITemporalRepository<T>` in `Norse.Abstractions.Infrastructure`) → superseded by `IReadRepository<TView>` in `Norse.Abstractions.Backend`, write-side contract to follow at the same address. The §8 anti-pattern bullet referencing the old family follows.
- Mongo-as-operational-read-store → superseded by the jsonb `View` column doctrine (this spec). This discharges the long-pending Mongo-purge reconciliation: the POC verdict (Postgres jsonb replica wins) is now promoted into the record. Mongo remains a sanctioned *future* Midgard sibling implementation behind the same contract, adopted per-well on evidence (§8).

## 3. The Laws (Asgard)

### 3.1 IViewBearer — the well law

```csharp
public interface IViewBearer<TView>
{
	TView View { get; }   // the JSON-mapped document column
}
```

One property of pure generic law — no persistence vocabulary. An entity exposing a read view implements this beside its Id-bearing base; that is the entire per-well contract for read access. A well that models its entities correctly gets its repositories by existing.

### 3.2 The read contract

Everything on the query side returns `Outcome<T>`. Four shapes × 2 (with/without projection); no nulls in the envelope, ever. **Invariant: `Outcome<T>.Succeeded ⇒ value is present.`** Absence is a Problem, not a null.

```csharp
public interface IReadRepository<TView>
{
	// Identity path. Filters on the root PK internally; the caller cannot
	// express a predicate, so the scan cannot be written by accident.
	// Absence → NotFound problem.
	Task<Outcome<TView>> GetAsync(Guid id, CancellationToken ct);
	Task<Outcome<TProjection>> GetAsync<TProjection>(
		Guid id, Expression<Func<TView, TProjection>> projection, CancellationToken ct);

	// Asserts exactly one match. 0 → NotFound; >1 → MultipleMatches.
	Task<Outcome<TView>> SingleAsync(
		Expression<Func<TView, bool>> predicate, CancellationToken ct);
	Task<Outcome<TProjection>> SingleAsync<TProjection>(
		Expression<Func<TView, bool>> predicate,
		Expression<Func<TView, TProjection>> projection, CancellationToken ct);

	// Asserts at least one match. 0 → NotFound.
	Task<Outcome<TView>> FirstAsync(
		Expression<Func<TView, bool>> predicate, CancellationToken ct);
	Task<Outcome<TProjection>> FirstAsync<TProjection>(
		Expression<Func<TView, bool>> predicate,
		Expression<Func<TView, TProjection>> projection, CancellationToken ct);

	// Set query: asserts nothing, so emptiness is data. Always succeeds
	// with a list; empty [] is a value, never a Problem.
	Task<Outcome<IReadOnlyList<TView>>> ListAsync(
		Expression<Func<TView, bool>> predicate, CancellationToken ct);
	Task<Outcome<IReadOnlyList<TProjection>>> ListAsync<TProjection>(
		Expression<Func<TView, bool>> predicate,
		Expression<Func<TView, TProjection>> projection, CancellationToken ct);
}
```

`TView` is deliberately unconstrained — Asgard's contract never references `IViewBearer`; the entity-side constraint belongs to Midgard's implementation, where the entity exists.

**Consumption is `Match`.** No `OrDefault`, no `Ignore` — see §1. A caller for whom absence is acceptable writes the `NotFound` arm; a caller for whom it is not lets the problem flow to the transport edge, where the existing `OutcomeServerInterceptor` machinery maps it (`NotFound` → `StatusCode.NotFound`) — no proto3 `optional`/wrapper ceremony for "succeeded with nothing," because that state cannot exist.

**Problem taxonomy.** `NotFound` and `MultipleMatches` are not peers in severity. `NotFound` is an expected domain state. `MultipleMatches` on a `Single` call is a data-integrity smell — the caller asserted an invariant the data violated — and warrants logging/telemetry even when handled. Both join the existing `Problem`/`ErrorCategory` model in `Abstractions.Contracts` and stay distinguishable for observability.

**Paging** — a paging-aware sibling of `ListAsync` (ordering + skip/take or keyset cursor) is a named future contract member for unanchored book-wide queries. Not slice-one work; the shape is planned, not built.

### 3.3 The no-joins law

**The law binds callers, not machinery.** Callers write one predicate against one `TView` and cannot reach beyond the view's information boundary. Midgard's rewriter is permitted to compile that predicate into seeks and EXISTS probes against promoted columns and child tables, because promoted structures are by definition projections of the view's own data. Intent preserved; only the physical plan changes.

## 4. The Promotion Law

### 4.1 Definitions

- A **promoted scalar** is a view property duplicated as a real relational column on the entity. Same value, same transaction, same source — drift impossible by construction. Promoted columns are where indexes land when a workload earns them (§4.2's indexing act); this is Mongo's path-index shred made explicit and transactional.
- A **promoted collection** is a view collection duplicated as an indexed child table. This is the multikey index made explicit. In this doctrine a child table *exists only* to serve as the promoted index of a view collection — the well never queries it directly.

### 4.2 Discovery: convention with total validation

The view is a **total mirror**: it carries the entity's whole current state at the top level, plus projected ancestry, by doctrine — `CountryOrAreaView` is the built precedent (every declared entity scalar mirrored name-and-type identical; the FK/nav pair replaced by the projected `Region` ancestry object). Because the mirror is total, convention is not opportunistic matching — it is an exhaustively validatable law:

- **Scalar law:** every *declared* CLR scalar on the entity, minus FK columns, must pair name-and-type (value-converter-aware) with a top-level view property.
- **Collection law:** every collection navigation on the entity must pair with a view collection.
- **Excluded structurally:** shadow audit/concurrency stamps (not CLR members — there for the audit trail, cannot be counted on in the view), temporal period columns (the view is the current state as of now), FK scalars, and navigations. EF model metadata at `AddWell` time knows all four categories; the exclusions are mechanical, not judgment calls.
- **Enforcement:** `AddWell` throws on any missing or mismatched pair. A Ginnungagap analyzer later moves the check to build time. A rename cannot silently demote a promoted member to a residual JSON scan — the law demands the pair exist at all, so the mismatch fails loudly.
- **The exclusion attribute — `[NotProjected]`** (in `Abstractions.Backend`) exists only for declared exceptions — an entity scalar that deliberately does not ride in the document. Configuration marks deviations; convention is the rule.
- **View-extra members are legal residual:** ancestry objects, computed/denormalized fields with no entity column route through the JSON path. Ancestor reference navigations (`ParentRegion` → projected `Region`) are seed-time hydration, invisible to the rewriter.

**Doctrine — promoted no longer implies indexed.** Under the total mirror, every declared entity scalar is column-backed by law, so "promoted" and "indexed" are two distinct acts:

- **Materialization** — a view-extra member becomes a declared entity scalar. Changes the rewriter's routing for that path from JSON extraction to a relational column. This is the act that moves a member from residual to promoted.
- **Indexing** — an index lands on an already-mirrored column. Changes the plan from scan to seek. This is the per-well, per-workload **schema decision**; the Policy trio exercises only this act.

Fast arbitrary predicates on residual (JSON-only) paths are not promised. A new hot filter is materialization and/or indexing, knowingly taken — or a knowingly accepted scan cost. Same governance Mongo demands (create the index or eat the collscan), visible in the entity and its migrations instead of an ops runbook.

## 5. The Machinery (Midgard `Infrastructure.Persistence.EntityFramework`)

### 5.1 The generic repository

```
Repository<TContext, TEntity, TView> : IReadRepository<TView>
	where TContext : DbContext
	where TEntity : class, IViewBearer<TView>
```

- Takes `IDbContextFactory<TContext>`; create-execute-dispose per operation. Context never escapes.
- Core query shape: `Set<TEntity>().Where(rewrittenPredicate).Select(viewSelector).Select(projection)` — filter on relational surface first, then surgical JSON extraction (EF 8+ projects individual JSON properties; the full document is not materialized).
- The identity path takes `Guid` at the contract; the machinery builds the PK filter from the EF model's key metadata, adapting to `INorseGuid`-typed keys (`DeterministicGuid` on the slice entity) through their converters — never a hardcoded `e.Id` of assumed type.
- **Known trap:** the view selector must NOT be written as the literal lambda `e => e.View` against the interface — interface member access does not translate. Build `Expression.Property` against the concrete `TEntity` via reflection once per closed generic, cache in a static. Protected by comment and translation test so it does not get "simplified" away.

### 5.2 The predicate rewriter

One `ExpressionVisitor`, built and cached per closed generic:

- Member access on the `TView` parameter for a **promoted scalar** → retargets to the entity's relational column.
- `Any(...)` over a **promoted collection** → retargets to the relational navigation (EF emits EXISTS against the indexed child table).
- Everything else → routes through the JSON path (`e.View.X.Y`), translating to `JSON_VALUE`/`OPENJSON` (SQL Server) or `->>`/`jsonb_array_elements` laterals (Postgres). Legal, cost-bearing, residual.

Roughly 150 lines. Fully unit-testable without a database: assert rewritten tree shapes. This is the highest-leverage component in the slice — telemetry on translated SQL is the evidence it works.

### 5.3 Materialization law

Public names are `FirstAsync`/`SingleAsync`/`ListAsync`; the EF OrDefault methods are internal plumbing, never vocabulary.

- **`FirstAsync`** → EF `FirstOrDefaultAsync()`; null → `NotFound` problem. One null check, no exception path, `TOP(1)` SQL.
- **`SingleAsync`** → `Take(2).ToListAsync()` + count inspection: 0 → `NotFound`, 1 → success, 2 → `MultipleMatches`. Same SQL as EF's `SingleOrDefaultAsync` (`TOP(2)`/`LIMIT 2`) with the exception replaced by a count check.
- **FORBIDDEN:** implementing `SingleAsync` via EF `SingleOrDefaultAsync` + catch. The >1 signal arrives as `InvalidOperationException` — EF's junk drawer, shared with untranslatable-predicate and context-lifetime failures. Catching it would materialize rewriter translation bugs as polite `MultipleMatches` domain problems and send them up the wire as phantom duplicate data. Problems travel as values, not throws; converting EF's throw back into a value is the ProblemException veto in reverse. Real exceptions (translation failure, connection loss) stay exceptions and propagate loudly.
- Never `Count()` to detect cardinality — never pay a full count for a violation `Take(2)` detects.

### 5.4 Wiring

`services.AddWell<TContext>()`: instantiate the EF model at startup, scan for `IViewBearer<TView>` implementors, register `IReadRepository<TView> → Repository<TContext, TEntity, TView>` for each, run the §4.2 total-mirror validation, build and cache selectors/rewriters. Collision law: two entities claiming the same `TView` is a startup exception. View types are realm-owned.

## 6. The Generator (Mimir)

**Input:** `seeds/raw/UNSD — Methodology.csv` as `AdditionalFiles` in `Reference.Contracts`. The generator reads exactly four columns — `Country or Area`, `M49 Code`, `ISO-alpha2 Code`, `ISO-alpha3 Code` — with a hand-rolled minimal CSV read (netstandard2.0; no Sep dependency inside a generator). Region/ancestry columns are none of Mímir's business. Raw rows lacking ISO alpha codes (pure M49 areas) are skipped: this is an ISO 3166-1 surface. The current export contains zero such rows — 249 lines = 1 header + 248 data rows, all ISO-bearing — so today the enum's universe and the seed's universe are identical. The two derivations diverge deliberately on a future ISO-less reissue: the generator skips the row (ISO surface by definition), while `SeedTool`'s `ValidateIsoAlpha` throws — forcing a human modeling decision before the well ever seeds it. Either way the §9.11 drift guard holds: no country reaches the database without resolving through the generated surface.

**Output** (all emission via `AppendCSharp` raw-string house style):

- `enum IsoCountryCode : ushort` — M49 numeric values as the enum values.
- **Parse surface:** `Parse(ReadOnlySpan<char>) → Result<IsoCountryCode>` accepting all three forms (numeric including unpadded, alpha-2, alpha-3; case-insensitive; trimmed), returning a Problem for invalid input per the Svartalfheim parser template; `TryParse` retained for boolean-flow callers. `FrozenDictionary` code cache with `GetAlternateLookup<ReadOnlySpan<char>>` — zero-allocation parse at the gRPC edge; separate `FrozenDictionary<ushort, IsoCountryCode>` for the numeric path (no zero-pad-through-string laundering).
- **Dataset:** `Iso3166.All` — one generated record per row (code, alpha-2, alpha-3, English name, **precomputed v5 `Guid`**) plus `FrozenDictionary<IsoCountryCode, Guid>` for the identity hot path. Zero runtime hashing anywhere, WASM included.
- **Namespaces:** `MimirNamespaces.Root` (hand-minted constant, the one manual act) and generated chained `Iso3166 = v5(Root, "iso3166-1")`. Future datasets chain the same way (`"iso4217"`, …). The tree is reproducible from one constant plus documented names.

**Verification:** the package ships a self-verification test that recomputes every namespace and every row GUID via Svartalfheim's `DeterministicGuid` and asserts equality against the shipped constants — generator-vs-mechanism drift cannot ship.

## 7. The Slice, End to End

### 7.1 Mimisbrunnr retrofit

- `CountryOrArea` implements `IViewBearer<CountryOrAreaView>` — one interface declaration; the entity/view pair already conforms to the total-mirror law.
- `Code` columns re-typed from `ushort` to `IsoCountryCode` via a ushort↔enum value converter (possible because Mimisbrunnr now references `Reference.Contracts`).
- Seed resolves each country's `Id` through the generated GUID lookup; row `Id` and document `Id` are written from the same value in the same operation. Deterministic snapshot; unknown code fails the seed loudly.
- Migrations continue to ride the multi-provider package (SQL Server + Postgres); schema and persistence version independently of Mimir service components.

### 7.2 Request path

```
string code (any of numeric / alpha-2 / alpha-3)
  → IsoCountryCode.Parse (Result, span-based)              [Mimir gRPC edge]
  → generated FrozenDictionary lookup → Guid               [zero DB involvement, zero hashing]
  → IReadRepository<CountryOrAreaView>.GetAsync(id, proj)  [Asgard contract]
  → PK seek on root row, JSON projection in SQL            [Midgard machinery]
  → response envelope, Uuid bytes                          [transport edge]
  → WASM component recomputes v5, asserts equality         [E2E acceptance]
```

Wire records are `[DataContract]` types in `Reference.Contracts`; the handler in Mimir's `Reference.Web.Server` rides the existing mediator pipeline (`IQueryRequest<T>`/`ISender`, generated gRPC server/client wiring, `OutcomeServerInterceptor`). Parse failure maps through the interceptor per existing doctrine (`InvalidArgument`, offending code in problem detail). The handler consumes the view via projection to the wire record — the projection happens in SQL, and `CountryOrAreaView` itself never crosses the wire (its home, `Reference.Data`, references EF and is forever server-side).

## 8. Non-Goals and Deferred Positions

- **No Mongo in slice one.** The contract is storage-agnostic by construction; a future `MongoDB` sibling in Midgard is a simpler implementation (null rewriter — every path is promoted in a document store, modulo index creation). Adopted per-well when a workload demands arbitrary document predicates at scale AND tolerates projection lag / dual-write machinery. Reference data never meets that bar; Claims might. Evidence first.
- **No provider-specific optimization passes yet.** Research note: Npgsql `EF.Functions.JsonContains` → `@>` is GIN-accelerated on Postgres and could serve unanchored containment without a child table; provider-specific, caller-invisible, strictly a later layer. The child table remains the provider-neutral law. (GIN does NOT accelerate the `->>` extraction SQL EF emits for ordinary predicates — do not assume "Postgres indexes anywhere.")
- **No paging in slice one** (shape named in §3.2, built later).
- **No byte-order design work.** Settled. The E2E test asserts it; nothing else references it.
- **No write-side design.** Named in §1 ruling 5 only.

## 9. Acceptance Tests

1. **E2E identity round-trip:** WASM computes v5 client-side → calls Mimir with each of the three code forms (`"US"`, `"USA"`, `"840"`, plus unpadded `"40"` for Austria) → asserts returned Uuid equals local computation, Guid and canonical string both.
2. **Parse surface:** all three forms, case-insensitivity, whitespace, unpadded numerics, garbage → Problem; span overload allocates nothing (allocation test).
3. **Namespace and GUID self-verification:** regenerate every dataset namespace and every row GUID from Root + documented names via `DeterministicGuid`; assert against shipped constants.
4. **Rewriter unit suite:** promoted scalar retarget, promoted collection `Any` → navigation, unpromoted residual → JSON path, mixed predicates — assert tree shapes, no database.
5. **Translation canaries (both providers):** `Any` into a JSON collection translates server-side (no client eval) — version-sensitive EF territory (collections of complex types inside `ToJson` had gaps through EF 8/9). Capture the generated SQL in telemetry.
6. **Seek verification:** anchored composite query (promoted trio) produces an index seek + residual, not a scan — assert via query plan or logged SQL on both providers. **Precondition:** the composite index exists — the §4.2 indexing act is part of the test fixture's schema. Absence of the index producing a scan is honest schema, not rewriter failure; this test verifies the rewriter's output *given* the index, never that promotion alone bought a seek.
7. **Startup laws:** duplicate `TView` claim throws at `AddWell`; a deliberately broken mirror (entity scalar with no view pair) throws with a message naming the member; the interface-member selector trap covered by a translation test.
8. **Contract semantics suite:** `SingleAsync` over 0/1/2-row fixtures → `NotFound`/success/`MultipleMatches`; `FirstAsync` over 0/1/many; `ListAsync` empty → succeeded `[]`, never a Problem; no code path yields a succeeded Outcome with a null value (invariant test).
9. **Failure-channel purity:** an untranslatable predicate through `SingleAsync` must surface as a thrown exception, NOT as a `MultipleMatches` problem — the exact regression a future "simplification" to EF's `SingleOrDefaultAsync` + catch would introduce. Pinned by test.
10. **SQL parity check:** `SingleAsync`'s `Take(2)` emits `TOP(2)`/`LIMIT 2` identical to EF's native `SingleOrDefaultAsync` on both providers (capture in telemetry).
11. **Seed drift guard:** a TSV country row whose code is absent from the generated enum fails the seed with a loud, named error.

## 10. Telemetry to Bring Back

- Generated SQL for: identity Get, anchored Find with residual JSON predicate, promoted-collection `Any`, unpromoted `Any` (both providers).
- Query plans / timing for the anchored composite on a synthetic million-row Policy-shaped table (the evidence that promoted columns carry the real workload).
- Allocation profile of the parse path under load.
- Generator build-time cost in `Reference.Contracts`.
