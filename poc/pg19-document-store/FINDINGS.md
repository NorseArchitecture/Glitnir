# Findings — PostgreSQL as the Operational Document Store

**PG version tested:** PostgreSQL **19beta1** (Debian `19~beta1-1.pgdg13+1`, gcc 14.2) — official `postgres:19beta1-trixie`.
**Npgsql version:** 9.0.2 (harness).
**Date:** 2026-06-16.
**Re-verify at:** RC1 — Q1/Q2 lean on SQL/JSON + jsonpath (stable since pg12/pg17), low risk; re-confirm anyway. Lag numbers (Q3) are localhost/synthetic — directional, not production figures.

## Matrix verdicts

| # | Question | Verdict | Detail |
|---|----------|---------|--------|
| 1 | Npgsql (no EF) serves the read shapes against jsonb — filter, sort, `skip`/`take`, projection, deserialized via `System.Text.Json`? | **PASS** | `@>`, jsonpath `@@`, OFFSET/LIMIT, `jsonb_build_object`, and SQL/JSON `JSON_VALUE`/`JSON_QUERY` all returned correctly (`harness`). **GIN proven at volume:** at 27k rows the planner chose a Bitmap Index Scan on `@>` (0.12 ms, 23 rows). Confirms jsonb is fully queryable; the consume mechanism the design actually adopts is row 1b. |
| 1b | **The ruled read path** (2026-06-16): does a read-only, NoTracking, jsonb-mapped **EF** context translate an engineer's `Expression<Func<TDoc,bool>>` predicate + `Expression<Func<TDoc,TProj>>` projection into **server-side** jsonb SQL — no hand-written `jsonb_build_object`? | **PASS** | `harness-ef`. `Where(p => p.Id == id).Select(p => p.Body.Premium.Amount)` → `SELECT CAST(p.doc #>> '{premium,amount}' AS numeric) … WHERE p.id = @id`; record projection → `SELECT p.id, CAST(p.doc #>> '{premium,amount}' AS numeric), p.doc ->> 'productCode'`; member predicate → `WHERE (p.doc ->> 'productCode') = 'WC'`. All server-side, all from expressions. This is what replaces the Mongo driver's LINQ provider. **AOT-cleanliness is NOT proven here and is a known EF gap — it rides into the verdict as an explicit caveat.** |
| 2 | Filtered-array-subset projection (`$elemMatch`/positional) expressible? GIN-indexable? | **PASS (functional); index nuance** | `jsonb_path_query_array` returns only the matching array elements; `JSON_TABLE` explodes-then-filters. **No raw-SQL escape hatch needed — the contract stays shape-stable.** *Index nuance:* containment/equality jsonpath rides GIN (`jsonb_path_ops`); an **inequality inside an array** (`@.amount > 8500`) does **not** — at volume the index matched all 27,321 rows (zero selectivity), recheck did the work, and it ran slower than a seq scan (11.5 ms vs 7.1 ms). The planner correctly preferred the scan. Range-inside-array filters need a targeted expression index or array normalization; equality membership filters are fine. |
| 3 | Streaming replica lag — shim INSERT → visible on replica? | **PASS → W1 ships** | `pg_stat_replication`: streaming, async, `replay_lag` ~1.15 ms. Harness shim→replica: **idle p95 1.32 ms** (max 4.12), **under 4-writer load p95 1.95 ms** (max 2.04). Sub-perception even loaded; the return-body contract covers it — no W2/W3. Localhost/synthetic; directional but decisive at this margin. |
| 4 | Model B temporal tables + jsonb document table coexist on one primary, both streaming? | **PASS** | tstzrange + GiST exclusion versioning (current row + full timeline correct) shares the database with the jsonb document table, no interference. Replica carries both (verified separately: write-on-primary read-on-replica). |
| 5 | In-DB document build viable vs. app-side? | **PASS (capability)** | `jsonb_build_object` + correlated `jsonb_agg` assembled the nested doc from relational source tables, and `INSERT … SELECT … ON CONFLICT` populated the document table (the in-DB `Replace`). Capability confirmed; the in-memory-vs-in-DB choice is now cost/clarity, not capability. |
| 6 | NSB Transactional Session — shim INSERT + `Send` atomic; failed INSERT rolls back dispatch? | **DEFERRED** | Not built this cut — needs NServiceBus + RabbitMQ + SQL persistence (its own harness). Documented to work against Postgres; confirm in a focused follow-on spike. **Tracked, not dropped.** |

## AOT-clean translator spike (2026-06-16, follow-on)

Triggered by a later requirement: `.Server` should be a **NativeAOT** target if possible (cold-start under KEDA autoscale). That collides with EF as the read translator — EF's NativeAOT path (precompiled queries) needs *statically visible* queries, but the contract passes `Expression` lambdas as **opaque parameters** into a generic repository, so EF falls to runtime `Expression.Compile` (`Reflection.Emit`), which NativeAOT forbids. The spike (`harness-aot`) tests the alternative: a bespoke expression-tree **walker**.

| Claim | Verdict | Evidence |
|---|---|---|
| (a) An expression **walker** (no `Expression.Compile`, no `Reflection.Emit`) translates predicate + projection lambdas to jsonb SQL | **PASS** | `Where(p => p.Id == id).Select(p => p.Premium)` → `SELECT (doc->>'premium')::numeric … WHERE (doc->>'id')::uuid = $1`; record projection → `jsonb_build_object('Id', …, 'Premium', …, 'ProductCode', …)` materialized by **STJ source-gen** into `PocViewModel`. Bounded surface (comparisons, member/ctor projection), not a general LINQ provider. |
| (b) That path is **NativeAOT-clean** | **PASS — native binary runs** | `dotnet publish -p:PublishAot=true` (from a VS Developer shell): **zero IL2xxx/IL3xxx trim/AOT warnings** (translator + Npgsql 9 + STJ source-gen), clean native link → `Norse.harness-aot.exe`. The native binary's output is **identical to the JIT run** — predicate + single-member + record projection (STJ source-gen materialization) all work under NativeAOT. (The earlier link failure was only the MSVC environment missing from a bare shell; `results/harness-aot-native.out`.) |

**Consequence:** the symmetric lambda surface (§4.10) and a NativeAOT `.Server` are **jointly achievable via the walker, and not via EF**. This reopens the EF-read-only ruling (§4.2/§4.11) in favor of a source-generated/expression-walker translator; EF retained only where AOT doesn't bind (worker SoR writes, migrations, Auth). **Separate, still-open gate:** whether the whole `.Server` host (Blazor-Server circuits + gRPC) AOTs — the translator clears only the data layer.

## Implication for the decision record

The §4 direction is **supported, not refuted** — and on two points strengthened:

1. **`IDocumentRepository<T>` stays shape-stable (§4.5 holds), and the translator is settled.** Every read shape, including the feared filtered-array-subset projection, expresses in jsonb/jsonpath. The expression→SQL translation Mongo's driver used to provide is supplied by a **read-only, NoTracking, jsonb-mapped EF context in `.Server`** (ruled 2026-06-16; proven in row 1b). This **reverses "EF lives solely in the worker"** — the wall re-redefines to "no *source-of-truth* EF in `.Server`: no entities, no migrations, no `SaveChanges`; a read-only projection context against the replica is allowed." Carries one caveat: **EF's query pipeline is not AOT-clean** — confirm the AOT/startup-translation cost is acceptable before the verdict commits this path.
2. **W1 is the floor and the ceiling (§4.3).** Replica lag is sub-2 ms p95 under load; the return-body contract suffices. W2/W3 unneeded until the lag profile changes (re-entry: a real-data, cross-AZ replica whose p95 crosses perception).
3. **Index shape follows filter shape.** Hot read-store filters should be containment/equality-shaped to ride GIN. Range predicates inside jsonb arrays are scan-bound; where one is a hot path it earns a targeted expression index or a normalized projection column. Persistence amendment's index section, not a blocker.
4. **Document key casing must be one declared, fail-loud convention — NOT per-property annotations.** The EF spike initially returned `premium = null` (not an error) because the owned navigation defaulted to the CLR name `Premium` while the document key is `premium`. A casing mismatch between the worker's `System.Text.Json` serialization and the read context's EF element-name mapping silently yields `null` — a silent fallback the platform forbids (§2.6/§2.7). The amendment must pin jsonb document key casing as a single convention shared by both sides (or generate both from one source), with a startup guard that fails loud on mismatch. This is the pit-of-success obligation the spike surfaced.

**No surprise refutes the direction.** The formal verdict turns on Q1/Q1b/Q2 (PASS), Q3 (W1), the EF AOT caveat (open), and Q6 (deferred).

## Script notes

- `01`/`02` `EXPLAIN` lines show `Seq Scan` — a 3-row small-table artifact, not an index failure; `06-gin-at-volume.sql` supersedes them for the index question (27k rows, ANALYZE, planner-choice + `enable_seqscan=off` capability proof).
- Two cosmetic `\echo` bugs fixed (no effect on SQL results): `02` had an apostrophe (`(b')` → `(b2)`); `03` had backticks psql ran as a shell command (removed).
- `harness-ef` (the ruled read path) is a SECOND .NET project that DOES use EF — deliberately, to prove the expression→jsonb translation. The original `harness` stays EF-free on purpose (it proved jsonb deserializes without EF). Both run from `./run.ps1`; `results/harness-ef.out` holds the generated SQL.

## Reproduction

`./run.ps1` (official `postgres:19beta1-trixie`, primary + streaming replica; harness needs the .NET 10 SDK). `./run.ps1 -Script 06` for the index-at-volume probe alone. Raw outputs in `results/*.out` and `results/harness.out`. The replica clones from the primary via `pg_basebackup` on first start; the `host replication all all trust` line is added by `primary-init/01-replication-hba.sh` (trust-mode omits it).
