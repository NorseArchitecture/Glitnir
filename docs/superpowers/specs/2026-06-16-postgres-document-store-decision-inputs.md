# PostgreSQL as the Operational Document Store — Culling Mongo (Decision Inputs)

**Date:** 2026-06-16
**Status:** Direction ruled 2026-06-16; **PoC + AOT spike run 2026-06-16** (`poc/pg19-document-store`) — contract, write-tier, coexistence, and an **AOT-clean source-gen expression-walker** read path all PASS (§7). The read translator is the **walker, not EF** (§4.2) — **native-published and verified running** 2026-06-16. **Formal verdict gated on:** `.Server` host-stack (Blazor/gRPC) AOT capability (likely partial) and the Q6 Transactional Session atomicity spike. Until the verdict lands, the standing law of `2026-05-21-midgard-persistence-design.md` (Mongo as operational read store) and `2026-06-07-auth-design.md` (Mongo as identity system of record) remains in force.
**Owner:** Buvy
**Supersedes on verdict:** the MongoDB commitments in CLAUDE.md §4 → Persistence and §4 → Auth; the "operational read store (MongoDB)" architecture of `2026-05-21-midgard-persistence-design.md`; the "Mongo is the identity system of record" ruling of `2026-06-07-auth-design.md`; the UI-layout Mongo persistence of the UI Composition spec (`2026-06-05-ui-composition-design.md`); and the `IDocumentRepository<T>` "is Mongo; the concept does not apply" aside in `2026-06-11-entityframework-context-provenance-decision-inputs.md` §Repository-surface law #3.
**Companion specs:** `2026-05-21-midgard-persistence-design.md` (the three-tier CQRS model this amends); `2026-06-11-entityframework-context-provenance-decision-inputs.md` (the worker-side repository surface, unchanged); `2026-06-07-auth-design.md` (the identity inversion this reverses); `2026-06-04-norns-design.md` + `poc/pg19-temporal` (the system-time temporal model that must coexist with the jsonb store).
**Deferred to its own thread:** the interactive-vs-batch worker endpoint split (`{ctx}` + `{ctx}.bulk`). It is a messaging-foundation amendment (`2026-06-03-messaging-foundation-design.md`), has its own decision tree, and has **no bearing on this PoC**.

---

## 1. Context

The standing persistence law (`2026-05-21`) runs three storage tiers: MongoDB as the operational read store served by `.Server`, PostgreSQL as the worker-only source of truth, Snowflake as the analytical tier. Mongo earns its place on three stated grounds (that spec §1): a read-to-write ratio favoring reads by orders of magnitude; a **failure-domain commitment** — "the source of truth must outlast a compromised read path," so the read tier is deliberately sacrificeable and physically isolated from the source of truth; and CQRS purity — wire-shaped reads with no per-item .NET mapping.

The question heard 2026-06-16: **can PostgreSQL serve the operational read store directly — jsonb documents the HTTP tier fetches — and let us cull MongoDB as a runtime dependency entirely**, including its two sanctioned inversions (Auth identity, UI layout persistence)? The motivation is consolidation: one storage engine to operate, one connection story, fewer moving parts — provided the three grounds above survive the move.

They do, with one structural change (a read replica) and one bounded exception (Auth). This document records the direction; the PoC gates the verdict.

## 2. The deciding constraint, preserved

The failure-domain commitment is the load-bearing reason Mongo exists — not ergonomics. Collapsing the read store into the *same* Postgres instance as the source of truth would retire it. The ruling keeps it: **the operational read store is a physically separate Postgres instance — a streaming read replica — fed from the primary.** At least two instances, always. A flooded or compromised read path takes down a replica, not the source of truth; the blast-radius isolation of `2026-05-21` §3.2 holds, with the read engine swapped, not the guarantee abandoned. A logical read model on a fully separate instance is the documented re-entry path if a streaming replica is ever outgrown.

## 3. Forces (why this is not free-form preference)

1. **PostgreSQL jsonb is a first-class queryable type, not a blob.** Operators (`->`, `->>`, `@>`), the SQL/JSON path language (`@?`, `@@`, `jsonb_path_query`), and SQL:2023 functions (`JSON_TABLE`, `JSON_QUERY`, `JSON_VALUE` — pg17+) cover filtering and projection inside documents and arrays; GIN indexes keep containment and path predicates index-backed. The open question is not *whether* Postgres can do it but whether the **Npgsql driver, with no EF**, translates the `IDocumentRepository<T>` filter and projection expressions the way the Mongo driver does — and where the filtered-array-subset projection (`$elemMatch`/positional) lands. That is the PoC's first job (§7 Q1/Q2).
2. **The `.Server` hard wall must hold, redefined.** Standing law forbids any Postgres reach from `.Server` (`2026-05-21` §8.3). The wall's *intent* is "the web tier cannot reach the source of truth and carries no EF." Reading jsonb documents from a **replica** does not reach the source of truth, and a raw-Npgsql read client carries no EF. So the wall is redefined, not breached: **no EF and no source-of-truth access in `.Server`** — not "no Postgres."
3. **The shim must be readable the instant the client can GET.** The 404-on-immediate-GET race is real and observed in production. The shim (a `Pending` document the worker later flips to `Active`) survives the engine swap unchanged in concept. Because a streaming replica is read-only and Mongo is gone, the synchronous shim write has only one honest home: in-process to the **primary**, jsonb only (§4 W1). Anything asynchronous returns `201` before the shim exists and reinstates the race.
4. **Replica lag is the new variable.** A synchronous shim write to the primary does not guarantee an immediate GET against the *replica* sees it. Write-side and read-side are separate levers; the gap is a measurement, not a guess (§7 Q3).
5. **House law unchanged.** Compile-time over runtime; fail loudly; database self-defense; least accessibility; no silent fallbacks. The idempotency and wall decisions below are shaped by these, not around them.

## 4. Ruled now (2026-06-16) — direction

### 4.1 Storage topology
MongoDB is culled entirely. The operational read store is a PostgreSQL **streaming read replica** carrying jsonb documents; the primary remains the source of truth. ≥2 instances, always (§2).

### 4.2 The `.Server` wall — "no EF," reads via the source-gen walker
Ruled 2026-06-16, after the AOT spike (FINDINGS, "AOT-clean translator spike"): the read path uses a **source-generated expression-walker translator over raw Npgsql** against the replica — **no EF in `.Server` at all.** The wall holds its original strict form, **"no EF in `.Server`,"** now with a read mechanism that honors it: engineers pass `Expression` predicates/projections (§4.10); the walker turns them into jsonb SQL with **no `Expression.Compile` and no `Reflection.Emit`**; `System.Text.Json` source-gen materializes the result. `.Server` gets:
- a **read-only raw-Npgsql client against the replica**, driven by the walker translator (§4.5), and
- a **narrow write** to the **primary**: the document/view jsonb table only, via the Transactional Session's ADO command (§4.3) — never source-of-truth entities, never EF.

Two connection strings (replica read, primary shim-write) at the `IConnectionResolver` choke point (`Norse.Infrastructure.Persistence`). The analyzer rule keeps its original intent: **no EF reference and no source-of-truth reach in `.Server`.** EF lives **solely in the worker** (the generated SoR context, mutation, temporal), in the migrations service, and in Auth (§4.6) — never in `.Server`.

> Net of the session's two reversals: a read-only EF context was the *provisional* answer; the AOT spike overturned it. The walker satisfies **both** the symmetric lambda surface (§4.10) and a NativeAOT `.Server` — which EF cannot, because its precompiled-query AOT path needs statically-visible queries and the contract passes expressions as **opaque parameters** into a generic repository (→ runtime `Expression.Compile` → no AOT). So "EF lives solely in the worker" — the session's original instinct — **stands, vindicated by evidence rather than preference.** Building the bounded walker is the accepted cost (§8). **Persistence is one front of a platform-wide AOT posture** (gRPC/protobuf; Blazor/validation/UI; messaging — Particular is moving the same way); the cross-cutting posture is flagged for the performance-posture spec, not ruled here.

### 4.3 The shim — synchronous, in-process (W1), atomic with dispatch
`.Server` writes the `Pending` shim in-process to the primary's jsonb document table, then dispatches the command, then returns `201` + the shim body (the client holds `Pending` without GETting; status-polls hit the replica). The `Pending → Active`/`Rejected` lifecycle of `2026-05-21` §6 is unchanged, on jsonb.

Because the shim now lives in Postgres, the shim INSERT and the command `Send` are wrapped in an **NServiceBus Transactional Session** against the primary: they commit atomically, and a failed INSERT rolls back the dispatch. This **closes the one non-atomic seam** the Mongo design left open and handled by operational discipline (`2026-05-21` §6.2/§6.3, "dispatch fails after shim succeeded"). Cost: `.Server` gains NSB SQL-persistence tables on the primary. Taken deliberately — the seam closure is worth the footprint.

Whether W1 alone suffices, or needs read-side help, is gated on the lag measurement:
- **W1** — return-body only; status-polls tolerate brief replica lag. The floor.
- **W2** — route reads for freshly-written ids to the primary for a lag window (read-your-writes routing). More state.
- **W3** — `synchronous_commit = remote_apply` on the shim write so the replica is guaranteed current before `201`. Deterministic; costs shim-path write latency.

PoC Q3 produces the deciding number. W1 ships unless the lag tail is bad.

### 4.4 Idempotency — DB constraint, sequential PK, transparent replay
- **Resource id is a SequentialGuid (PK)** — index-friendly insert locality preserved.
- **The dedup key is a separate UNIQUE column** = UUIDv5(namespace = the OpenIddict client id for M2M / the interactive user id for cookie traffic, name = body hash). The two idempotency paths of `2026-05-21` §7 unify into one deterministic computation.
- **The unique constraint is the dedup mechanism**, replacing the racy check-then-write of §7.1 (which had a TOCTOU window under concurrent identical POSTs). Database self-defense (CLAUDE.md §2.2): the *insert* trips the constraint.
- **Collision → transparent replay, query-only.** On the unique violation, `.Server` does a pure `SELECT` of the existing row and returns it as-is (`Pending`/`Active`/`Rejected`); it **does not dispatch the command**. The Transactional Session enforces this atomically — no insert, no command on the bus. Same contract as today's Mongo replay; the constraint just makes it race-safe. A `409` was considered and rejected: a collision always denotes the same logical request (M2M callers wanting N identical creates already must carry a discriminator, §7.3), so surfacing an error to an honest retry is the wrong default.

### 4.5 `IDocumentRepository<T>` — shape-stable (proven)
The contract surface (`GetById`, `Query(filter, sort, skip, take)`, the projection variants, `Shim`/`Replace`) survives unchanged; the Mongo impl swaps for one backed by the **source-gen walker translator** (§4.2), and `Shim`/`Replace` become jsonb upserts on the primary. Offset paging (`skip`/`take`) stays — explicitly the document repository's sanctioned home (keyset-only was a *worker-SQL* rule, `2026-06-11` §Repository-surface). **PoC-proven (2026-06-16):** the walker translates an engineer's `Expression<Func<TDoc,bool>>` predicate and `Expression<Func<TDoc,TProj>>` projection — single-member and record-constructor — into server-side jsonb SQL (`#>>`, `->>`, generated `jsonb_build_object`), materialized by STJ source-gen, with **no `Expression.Compile`/`Reflection.Emit`** (AOT-clean) and nothing hand-written by engineers. The filtered-array-subset projection (the feared `$elemMatch` gap) also expresses natively (`jsonb_path_query_array`/`JSON_TABLE`). No contract churn, no escape hatch. *(A read-only EF context was the provisional path and translated cleanly too, but was overturned on AOT grounds — §4.2.)*

### 4.9 Document key casing — one declared, fail-loud convention
The worker serializes documents with `System.Text.Json`; the `.Server` read context maps jsonb members by element name. **These two must agree on key casing, by a single declared convention — not per-property annotations.** The PoC proved why: an unmapped casing mismatch returned `null`, *not* an error — a silent fallback the platform forbids (§2.6/§2.7). The amendment pins jsonb document key casing once (shared by serializer and EF element-name mapping, or generated from one source) and adds a startup guard that fails loud on mismatch. Pit-of-success, surfaced by the PoC.

### 4.10 The symmetric strongly-typed surface (the success criterion)
The experience the entire direction exists to deliver, stated as the bar to clear: **application code passes only `Expression` lambdas against strongly-typed view models and receives strongly-typed objects back — identically on `.Server` (query) and `.Worker` (read/enrich).** No SQL, no jsonb operators, no `jsonb_build_object`, no driver query objects ever reach application code, on either side. This is the bar the Mongo C# driver set; the PoC (row 1b) proved EF clears it for predicate and projection expressions. Any later implementation detail that forces raw SQL or jsonb syntax into application code is a **regression against this criterion** — escalated, not absorbed.

### 4.11 Document mapping is source-generated (ruled)
The `.Server` read mapping (the walker's member→jsonb-path map) and the worker's write contract are **source-generated from a single view-model declaration** — the same machinery that generates the source-of-truth DbContext (`2026-06-11` EF-context ruling). The shape is declared once; both sides consume the generated artifact. Hand-maintaining the read-side map and the write-side serialization contract as two independent representations is **forbidden** — that is precisely the drift that produced the §4.9 silent `null` in the PoC. Generation turns Mongo's *runtime* inference into Norse's *compile-time* equivalent (compile-time over runtime, §2.7), and is the same investment that carries the AOT trajectory — the walker plus STJ source-gen is AOT-clean (§8). The fail-loud guard (§4.9) backstops generation; it does not replace it.

### 4.6 Auth — the bounded exception persists, its engine flips
Auth was already the carve-out (Mongo as identity system of record in `.Server`, because credential verification is synchronous and a queue cannot sit in the login path). The exception persists; its engine changes: **ASP.NET Identity + OpenIddict ride their own EF Core relational stores, in-process, in `Norse.Auth.Server`** — those stores are not built for jsonb and identity must be governed synchronously in-process.
- `Norse.Auth.Server` becomes the **one** server-tier project that references EF and owns a DbContext — a narrow, documented exception to §4.2.
- The former `auth` Postgres schema (a read-only reporting projection fed from Mongo) **inverts to the source of truth**; reporting/Warehouse reads come off it (or its replica) directly. One fewer projection.
- The framework schemas govern their own secret handling (Identity's password hasher, OpenIddict's secret storage); `EncryptedString` + blind-index (CLAUDE.md §4 → PII) continues to govern *our* domain tables, not the framework identity schema.
- Auth does **not** ride the document/replica CQRS model at all — it is a normal in-process EF application against its own Postgres.

### 4.7 UI layout persistence → W1 jsonb
Moves to the same W1 model: in-process jsonb writes to the primary, reads from the replica. Inherits the §4.3 replica read-your-writes window; no special handling.

### 4.8 Reference data (`ICachedRepository<T>`) — worker simplifies, HTTP retargets
Stated precisely, not over-claimed:
- **Worker-side reads simplify.** Reference data is already the source of truth in Postgres (FK target). The worker has EF and the tables; `ICachedRepository<T>` reads `IReadOnlyEntity` straight from relational Postgres (no-tracking, ± the opt-in worker-local LRU). The Postgres→Mongo projection step **for worker consumption is deleted**.
- **HTTP-side reference reads retarget, not vanish.** `.Server` still needs reference data; it is projected into the read store as `IReferenceDocument` jsonb on the replica, by the same worker mechanism — instead of Mongo collections.

## 5. Alternatives rejected

- **Collapse into a single Postgres instance** (documents as jsonb tables beside the source of truth). Rejected: retires the §2 failure-domain commitment with nothing but role/RLS separation to defend it — a weaker isolation than the platform's stated security driver requires.
- **Keep Mongo only for the read store, cull it from Auth/UI.** Rejected: leaves the operational dependency in place for the smallest of reasons (read ergonomics Postgres now matches) while doing the harder inversions anyway — the worst of both columns.
- **Asynchronous shim (a `.Server`-queue handler writes it).** Rejected: returns `201` before the shim exists; reinstates the 404 race the shim exists to kill (§3.3). The shim must be synchronous.
- **`409` on idempotency collision.** Rejected (§4.4): a collision denotes the same logical request; an error to an honest retry is the wrong default.
- **Deterministic dedup key as the PK.** Rejected (§4.4): a hash-distributed PK reintroduces the B-tree page-split / WAL write-amplification that SequentialGuid was chosen to avoid.

## 6. Consequences (on verdict — not this session)

When the PoC returns a passing verdict, in one boy-scout-law change (CLAUDE.md §6):
1. CLAUDE.md §4 → Persistence rewritten (Postgres jsonb read store on a replica; the three-tier table; the wall redefinition); §4 → Auth rewritten (EF relational stores, in-process); §8 anti-patterns "No `DbContext` injection" gains the Auth carve-out and the wall's new wording.
2. `2026-05-21-midgard-persistence-design.md` amended (Mongo sections → Postgres jsonb; the read pipeline; idempotency §7; reference §9).
3. `2026-06-07-auth-design.md` amended (identity SoR inversion).
4. `2026-06-05-ui-composition-design.md` amended (layout persistence → W1 jsonb).
5. README.md realm/runtime narrative re-synced (Mongo removed from the local composition; Aspire AppHost in Bifrost drops the Mongo resource).

None of these are touched in this session. This document is the pre-verdict record; the PoC runs next.

## 7. Remaining before verdict — the PoC

`poc/pg19-document-store` (sibling to `pg19-temporal`), against `postgres:19beta1-trixie`, primary + streaming replica:

| # | Question | Gates |
|---|---|---|
| 1 | Raw Npgsql (no EF) serving the `IDocumentRepository<T>` shapes against a `jsonb` column — `GetById`, filtered `Query` (`@>`/jsonpath), sort, `skip`/`take`, **and projection to a narrower `TProjection`** (`jsonb_build_object`/`JSON_QUERY`), deserialized via `System.Text.Json`. | Contract stability (§4.5) and the wall redefinition (§4.2). |
| 2 | The filtered-array-subset projection (`$elemMatch`/positional analog) — `jsonpath`/`JSON_TABLE` expressible, or raw-SQL-only? GIN-indexed either way? | Whether the contract grows an escape hatch. |
| 3 | Primary + physical streaming replica; measure **shim INSERT → visible on replica** latency, idle and under batch-fan-out load. | W1 vs. W2/W3 (§4.3). |
| 4 | System-time temporal tables (Model B, from `pg19-temporal`) + the jsonb document table coexisting on the primary, both streaming to the replica. | The two storage models compose. |
| 5 | Document build: worker materializes jsonb in app code (the `Replace` analog) vs. building it Postgres-side from source-of-truth tables (`JSON_TABLE`/aggregation). | "In memory vs. in DB" — the open shaping question. |
| 6 | NSB **Transactional Session** on the primary: shim INSERT + command `Send` atomic; a failed INSERT rolls back the dispatch. | §4.3's atomicity claim and §4.4's no-command-on-replay. |

Findings land in `poc/pg19-document-store/FINDINGS.md`, dated against PG19 beta1, re-verify at RC1 (same discipline as `pg19-temporal`).

**PoC outcome (2026-06-16):** Q1/Q1b/Q2 **PASS** — expression predicate + projection (incl. the array-subset projection) translate to server-side jsonb SQL; the contract is shape-stable. Q3 **→ W1** (sub-2 ms p95 replica lag under load). Q4/Q5 **PASS** (temporal+jsonb coexist; in-DB build viable). GIN proven at volume for containment/equality; range-inside-array is scan-bound (§index guidance, FINDINGS). **AOT spike PASS — and native-published:** a source-gen expression-walker + STJ source-gen is AOT-clean (no IL2xxx/IL3xxx warnings) and the **NativeAOT binary runs with output identical to JIT** (`Norse.harness-aot.exe`, 2026-06-16, from a VS Developer shell). This overturned the provisional EF-read path (§4.2; EF reads would be JIT-only). **Remaining before the formal verdict:** (a) **`.Server` host-stack AOT** — whether Blazor-Server circuits + gRPC themselves AOT (separate, bigger gate; accepted as likely *partial* — AOT-preferred still pays even if the host isn't fully native); (b) **Q6** — the NSB Transactional Session atomicity spike (§4.3), slated for its own session. Nothing surfaced refutes the direction; the casing obligation (§4.9) is a build-it requirement, not a blocker.

---

## 8. Tradeoff ledger — MongoDB vs PostgreSQL for the operational read store

Recorded behind the verdict so the *why* survives. The frame: Postgres becomes the do-everything store **except message queues** (RabbitMQ stays; embeddings stay on Postgres via the Semantic Kernel pgvector package — no separate vector store, and the AI-chat-web PoC need not be revisited).

| Dimension | MongoDB (prior) | PostgreSQL (ruled) | Net |
|---|---|---|---|
| Shape declaration | Driver infers the class at **runtime** — "free," reflection-based | Mapping declared, but **source-generated** from the view-model (§4.11) | Compile-time equivalent of Mongo's convenience — *more* on-philosophy (§2.7) |
| Query / projection ergonomics | LINQ provider → BSON `$project` | Source-gen expression-**walker** → server-side jsonb (`#>>`/`->>`, generated `jsonb_build_object`); array-subset native | **Parity, proven** (§4.5; FINDINGS spike) |
| Symmetric typed surface (§4.10) | Lambdas in, typed out, both sides | Lambdas in, typed out, both sides | **Parity** — the success criterion is met either way |
| AOT trajectory | Runtime inference **fights** trimming/AOT | Walker + STJ source-gen is **AOT-clean, proven** (no IL warnings, ILC codegen). *EF's* AOT path needs static queries → defeated by the dynamic-expression repository, so EF reads would be JIT-only | Postgres — **but only via the walker, not EF**; that's why the EF read path was overturned (§4.2) |
| SoR → view build | In-process serialize, ship across the wire to a separate store | Buildable **in-DB** (`jsonb_build_object`), can share the SoR transaction | Postgres — closes the dual-write seam Mongo structurally can't |
| Transactional integrity | Read store is a separate engine, always eventually consistent | One engine; shim/view write can be atomic with dispatch (Transactional Session) | Postgres |
| Operational footprint | Two engines, two backup/PITR stories, extra local container | One engine, one consistency/backup story, one fewer container | Postgres |
| Failure surface | One driver, one shape representation | Risk of **two** representations drifting (PoC silent `null`) | Mongo simpler here — mitigated only by §4.11 generation + §4.9 fail-loud guard |

**Synthesis.** The trade is Mongo's *runtime* convenience for a *compile-time-generated* equivalent the platform must produce (§4.11). In exchange: one environment, a real AOT path, transactional in-DB view builds, and the dual-write seam closed. For a platform whose thesis is compile-time-over-runtime and pit-of-success, the trade leans **Postgres** — conditioned on the mapping being generated, never hand-maintained on two sides. That condition is the load-bearing one; without it the failure-surface row dominates and the case weakens.
