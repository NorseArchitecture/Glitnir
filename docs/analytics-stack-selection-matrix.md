# Sökkvabekkr — Analytics Stack Selection Matrix

> *Sökkvabekkr: the sunken hall where Sága and Odin drink each day while stories of all that
> has happened are recounted. The hall of recorded history. The data warehouse.*

**Status:** DRAFT — pending Fenrir ratification
**Realm:** Sökkvabekkr (warehouse), Élivágar (CDC ingestion flow), Hliðskjálf (BI observation seat)
**Namespace law:** Norse names dissipate at the package boundary. Consumers see
`Analytics.Warehouse`, `Analytics.Ingestion`, `Analytics.Semantic` — never the hall names.

---

## 1. Scope and stance

This record selects the analytics stack for the Norse Architecture platform and defines the
recommendation matrix used in client engagements. The platform position is:

**The pattern is prescribed; the vendors are certified per client posture.**

The prescribed pattern is invariant across all cells of the matrix:

1. **Ingestion is CDC from the WAL/transaction log.** The database's own log is the only
   event stream the warehouse trusts. Application-level integration events are contracts for
   services, not feeds for analytics.
2. **Raw data is immutable.** Land bronze untouched; every cleansing bug is recoverable by
   re-running transforms. ELT, never ETL.
3. **Medallion layering** (bronze → silver → gold) with transforms in version-controlled,
   tested SQL (dbt or SQLMesh).
4. **The semantic layer lives outside the BI tool.** Metrics are defined in the transform
   layer; the BI tool is a swappable presentation skin.
5. **Nothing re-enters Midgard except through the law.** Cleansed flat-file data returns to
   OLTP via the application's `IRequestHandler<,>` pipeline or domain-aware staging tables —
   never by direct writes to transactional tables.

## 2. The matrix

Axes: **source RDBMS** (rows) × **hosting posture** (columns). Each cell names the certified
warehouse + EL path. Transform layer is uniform (see §3). BI is chosen per client (see §4).

| Source ↓ / Posture → | **Sovereign / on-metal** | **Azure** | **AWS** | **GCP** |
|---|---|---|---|---|
| **PostgreSQL** | ✅ **ClickHouse** + PeerDB (or Airbyte self-hosted); Debezium Server as the fully self-managed CDC option | ✅ **Fabric** (OneLake) via Fabric-native Postgres CDC, or Snowflake-on-Azure + Fivetran/Estuary | ✅ **Snowflake** + Fivetran or Estuary Flow (native Postgres CDC) | ⚠️ **BigQuery** + Datastream — acceptable only if client is already GCP-committed |
| **SQL Server** | ✅ **ClickHouse** + Airbyte (SQL Server CDC connector) or Debezium; requires SQL Server CDC/Change Tracking enabled | ✅ **Fabric** — the path of least resistance; mirroring/CDC into OneLake, Power BI included in the SKU | ✅ **Snowflake** + Fivetran (best-in-class SQL Server CDC connector) | ⚠️ **BigQuery** + Datastream for SQL Server; rare combination, certify per engagement |
| **Oracle** | ⚠️ **ClickHouse** + Debezium (LogMiner) — works, but see Oracle tax below | ⚠️ **Fabric or Snowflake** + Fivetran (LogMiner-based) or GoldenGate if the client already licenses it | ⚠️ **Snowflake** + Fivetran, or GoldenGate → object storage → Snowpipe | ⚠️ **BigQuery** + Datastream for Oracle |

Legend: ✅ recommended · ⚠️ acceptable with conditions (conditions must be written into the
engagement SOW).

### Cell conditions

**The Oracle tax (all Oracle cells).** Oracle CDC is a licensing and operational minefield.
GoldenGate is priced separately and per-core; LogMiner-based routes (Debezium, Fivetran) are
free of GoldenGate licensing but sensitive to supplemental logging configuration, archive log
retention, and RAC topology. Every Oracle engagement gets a discovery line item to establish:
supplemental logging enablement, archive log retention window ≥ pipeline recovery time, and
whether the client's DBA team will bless LogMiner access. No Oracle cell is quoted without
this discovery completing.

**GCP cells.** Certified only when the client is already GCP-resident. We do not lead a bank
or insurer onto GCP for analytics; the segment is Azure-dominant and the recommendation must
survive an RFP defended by the client's incumbent Microsoft relationship.

**Fabric cells.** Fabric is recommended where the client's identity, licensing (E5), and
analyst population are already Microsoft-shaped — which is most of the target segment.
Condition: capacity-unit pricing is modelled during discovery; Fabric's consumption model
surprises clients who size it like a Power BI Premium renewal.

**Sovereign cells.** ClickHouse ≥ **25.3** (floor imposed by the dbt adapter — see §3).
Deployment on client metal or client-controlled cloud accounts only; we do not operate a
multi-tenant warehouse on the client's behalf.

## 3. Transform layer (uniform across all cells)

**dbt is the default; SQLMesh is the certified alternative.** Both cover every engine in the
matrix (ClickHouse, Snowflake, Fabric, BigQuery, MSSQL, Postgres), which is what makes a
uniform transform doctrine possible.

- **dbt on ClickHouse:** use the ClickHouse-maintained `dbt-clickhouse` adapter (classic
  Python engine) today. It requires ClickHouse ≥ 25.3 and supports dbt-core 1.10 features.
  **Fusion migration posture:** the Rust-based dbt Fusion engine's ClickHouse adapter is in
  private preview via a new ADBC driver; MergeTree configuration, materialized views, static
  analysis, and model contracts are post-v1 backlog. Adopt Fusion once MergeTree-specific
  support lands; models and profiles carry over. Structure the project as if Fusion were
  already the engine (no exotic Jinja, no Python models on the hot path).
- **SQLMesh caveat on ClickHouse:** SQLMesh treats ClickHouse as a first-class engine but
  works around the absence of upserts (delete+insert on ClickHouse is unusably slow) with
  engine-specific materialization strategies, and has no first-class model kind for
  ClickHouse materialized views yet. Acceptable, but the dbt adapter is the more battle-worn
  path on ClickHouse specifically.

### ClickHouse idiom law (settled, non-negotiable in sovereign cells)

ClickHouse is **not** Snowflake with the invoice removed. It is append-oriented MergeTree
storage, and mutation-shaped modelling is illegal:

- ❌ FORBIDDEN: merge/upsert incremental strategies; SCD2 maintained via `UPDATE`; any
  transform whose correctness depends on in-place row mutation; `ALTER TABLE ... UPDATE`
  ("mutations") on any scheduled path.
- ✅ REQUIRED: insert-only increments; `ReplacingMergeTree` with a version column for
  dedup-on-read (the CDC stream from Postgres is naturally an append-only log of row
  versions — this mapping is the design, not a workaround); `AggregatingMergeTree` /
  refreshable materialized views for rollups; `FINAL`-free query patterns in gold-layer
  models (dedup resolved at silver).

This is the analytics-realm sibling of the `SingleOrDefaultAsync` ban: the pattern that
feels natural to a warehouse-brained modeller is the one that destroys the engine.

## 4. BI layer

The warehouse team's obligation ends at governed, tested, documented gold-layer models and a
semantic layer. The BI tool is the client's choice of skin:

- **Power BI** — default recommendation for the target segment (already licensed, analysts
  already fluent). Fabric cells get it in the SKU.
- **Cube** — certified for *embedded* analytics inside Blazor applications (headless
  semantic layer with REST/GraphQL/SQL APIs). This is the platform differentiator cell.
- **Tableau** — acceptable where incumbent.
- Sigma / Omni / Looker — not certified; niche fit for the segment.

**Doctrine:** metric definitions never live inside the BI tool. A metric defined in Power BI
DAX that does not exist in the semantic layer is a defect.

## 5. The forbidden list (with failure modes)

This section exists to win arguments. Each entry names the pattern, the failure mode, and
the verdict.

| # | Forbidden pattern | Failure mode | Verdict |
|---|---|---|---|
| F1 | **Scheduled batch pulls against OLTP** (`SELECT * WHERE modified_date > @last`) | Hammers the transactional store; silently misses hard deletes; trusts application code to maintain timestamps; drifts unrecoverably | Banned. CDC from the WAL or nothing. |
| F2 | **Application events as the warehouse feed** | Events are integration contracts, not full state; schema versioned for services, not analytics; dual-write divergence between DB and stream; backfill requires history that may not exist | Banned as primary feed. The WAL is the only event stream the warehouse trusts. |
| F3 | **Ingesting JSONB view-model projections** | Projections are derived state owned by the Worker; warehouse-on-projection stacks derivations on derivations and inherits every projection bug | Banned. Ingest source-of-truth tables. |
| F4 | **Reverse-ETL direct writes into OLTP tables** | Bypasses every domain invariant the platform enforces; the data-layer equivalent of reflection past a sealed class | Banned. Cleansed data re-enters through `IRequestHandler<,>` or domain-owned staging tables. |
| F5 | **ETL (transform-before-land)** | Raw data destroyed at the door; cleansing bugs unrecoverable; lineage begins at a lie | Banned. Bronze is immutable; ELT only. |
| F6 | **Semantic logic inside the BI tool** | Metrics fork per dashboard; tool lock-in; untestable business logic | Banned. Semantic layer in dbt/Cube. |
| F7 | **Mutation-shaped modelling on ClickHouse** | See §3 idiom law | Banned in sovereign cells. |
| F8 | **Matillion** | Judged and found wanting. GUI-defined pipelines resist code review, diffing, and testing; the antithesis of transforms-as-code | Banned. The banshee may rest. |
| F9 | **Redshift** (new engagements) | Stagnant mindshare and roadmap relative to peers; no cell in which it beats the certified option | Not certified. Existing-Redshift clients get a migration conversation, not an expansion. |
| F10 | **Skyvia** | Neither the reliability leader nor the sovereignty play; no cell to occupy | Not certified. |

## 6. One-sentence doctrine (for Glitnir front matter)

> *The WAL is the only event stream the warehouse trusts; raw data is immutable; nothing
> re-enters Midgard except through the law.*

## 7. Open items for ratification

1. Confirm PeerDB vs. Airbyte as the default sovereign Postgres→ClickHouse EL (see
   companion brainstorm — PeerDB is ClickHouse-owned and purpose-built for exactly this
   pair; Airbyte is the multi-source generalist). Proposed: PeerDB for Postgres sources,
   Airbyte for everything else in sovereign cells.
2. Decide whether SQLMesh certification is worth carrying at all, or whether dbt-only
   reduces doctrine surface. Proposed: carry SQLMesh as "certified alternative" one release
   cycle, then review.
3. Fabric capacity-pricing discovery template (finance artifact, not architecture).
4. Oracle discovery checklist as a standalone Glitnir document.
