# Sökkvabekkr Local Dev Loop — Brainstorm

**Status:** BRAINSTORM — nothing here is law; every section ends in a human gate
**Goal:** ClickHouse + CDC + dbt running under the Bifrost Aspire AppHost with the same
local-first dev loop as every other realm, so the sovereign cell of the matrix is the cell
we dogfood daily.
**Companion doc:** `analytics-stack-selection-matrix.md`

---

## 0. The shape of the thing

Élivágar (the CDC rivers) flows out of Urdarbrunnr's Postgres into Sökkvabekkr (ClickHouse),
where dbt carves bronze → silver → gold. Hliðskjálf (BI/semantic) reads gold. Locally, all
of it is containers orchestrated by the existing AppHost, observable through Huginn, and
testable through Vafþrúðnir.

```
Urdarbrunnr (Postgres, wal_level=logical)
      │  logical replication slot
      ▼
Élivágar (CDC: PeerDB │ Debezium Server │ Airbyte)   ← decision point D1
      │  append-only row versions
      ▼
Sökkvabekkr (ClickHouse ≥ 25.3)
      bronze: raw CDC landing (ReplacingMergeTree, version = LSN)
      silver: deduped, typed, conformed        ┐
      gold:   marts, aggregates, semantic      ┘ dbt (one-shot runner)
      │
      ▼
Hliðskjálf (Power BI / Cube — out of scope for local loop v1)
```

## 1. Prerequisite: Urdarbrunnr must speak logical replication

CDC-from-WAL requires the source Postgres to run `wal_level=logical` and grant a
replication role. This is an **Urdarbrunnr/Ginnungagap concern, not an analytics concern**:

- Local dev: the Aspire Postgres resource gets `-c wal_level=logical` (and sensible
  `max_replication_slots` / `max_wal_senders`) as container args. Zero cost locally.
- The publication (`CREATE PUBLICATION`) should be owned by the source realm and created by
  its Migrations Service, same as any other schema artifact — publications are schema, and
  schema changes ride the existing exit-code-gated migration path.
- Doctrine candidate: **every service realm's Postgres is born replication-ready**; whether
  anything subscribes is a deployment decision, not a schema decision. This keeps the
  analytics realm from ever needing write access to a source database.
- Note for the matrix's other rows: SQL Server needs CDC/Change Tracking enabled per
  database; Oracle needs supplemental logging. Same doctrine shape: the source realm owns
  its log-readiness.

**Gate G1 — RULED:** "born replication-ready" is Urdarbrunnr law, not a per-deployment
choice. `pg-primary` runs `wal_level=logical` under Bifrost's AppHost as of 2026-08-02
(`src/Orchestration.AppHost/AppHost.cs`) — `max_wal_senders`/`max_replication_slots` were
already sensible from the primary+replica design. This closes only the Postgres cell's
local-dev prerequisite; the publication itself is still unbuilt (owned by Urdarbrunnr's
Migrations Service, per the note above, whenever a source table actually needs to publish).

**Verification scope, this pass — Postgres only.** SQL Server and Oracle stay unverified
today, blocked on real constraints, not deferred out of laziness:
- **SQL Server:** locked to SQL Server 2025 (native JSON compat-level floor, platform-wide —
  see house rules), which has no arm64 image. The dev machine is Snapdragon/WSL2 arm64, the
  same wall the Midgard SQL Server Testcontainers fixture already hit. Verifying CDC/Change
  Tracking readiness here waits on amd64 CI or a Testcontainers path with an arm64-aware skip.
- **Oracle:** blocked on EF provider maturity on .NET 10 first: no realm has proven an Oracle
  DbContext yet, so supplemental-logging readiness has nothing to attach to. Sequenced after
  an Oracle EF provider lands, not before.

Doctrine shape carries forward unchanged for both rows (source realm owns its log-readiness);
only the "we ran it and it works" proof is Postgres-only for now.

## 2. Decision D1 — which river? (the EL container)

Three candidates for the local sovereign Postgres→ClickHouse path, in descending order of
how strongly I'd argue for them:

**PeerDB (proposed default).** ClickHouse acquired PeerDB specifically because it is the
purpose-built Postgres→ClickHouse CDC engine — open source, self-hostable, docker-compose
deployable (a small constellation: peerdb server, flow workers, catalog Postgres, Temporal).
It speaks logical replication natively, lands row versions in exactly the
ReplacingMergeTree-friendly shape §4 wants, and its roadmap is literally owned by the
warehouse vendor. For the *Postgres-source sovereign cell* — which is our dogfood cell — it
is hard to beat. Cost: it is Postgres-source-only, so it cannot be the whole EL story.

**Airbyte (certified generalist).** Multi-source (covers the SQL Server and Oracle rows of
the matrix with one tool), self-hostable, but heavy: the modern install path (`abctl`)
stands up a kind/k8s cluster, which fights Aspire rather than composing with it. Running
Airbyte *under* Aspire means either the legacy docker-compose topology or treating Airbyte
as an out-of-band sibling that Aspire merely health-checks. Workable, inelegant.

**Debezium Server (the fully-manual sovereign option).** Single container, no Kafka
required, reads the WAL and pushes to a sink. The catch: no native ClickHouse sink, so it
needs an HTTP sink into ClickHouse's HTTP interface or a small relay — more moving parts we
own. Keep it documented as the "client's security team vetoed everything else" escape hatch.

**Proposal:** PeerDB for the local loop and the Postgres sovereign cell; Airbyte certified
for multi-source sovereign engagements; Debezium Server documented, not defaulted.
**Validation item V1 (before anything is law):** confirm PeerDB and its Temporal dependency
publish linux/arm64 images and run on the Snapdragon/WSL2 machine. ClickHouse itself has
first-class arm64 images; PeerDB's constellation needs proving. If arm64 fails, the local
loop falls back to Debezium Server (arm64-clean) while amd64 CI covers PeerDB.

**Gate G2:** ratify D1 after V1 completes.

## 3. Aspire composition (Bifrost additions)

Sketch of the AppHost additions — names shown in narrative form; actual resource names
follow namespace law (`analytics-warehouse`, `analytics-cdc`, `analytics-transform`):

```csharp
// Bifrost AppHost — analytics realm (sketch, not law)
var warehouse = builder.AddContainer("analytics-warehouse", "clickhouse/clickhouse-server", "25.6")
    .WithHttpEndpoint(port: 8123, targetPort: 8123, name: "http")   // HTTP interface — Bruno-able
    .WithEndpoint(port: 9000, targetPort: 9000, name: "native")
    .WithVolume("sokkvabekkr-data", "/var/lib/clickhouse")
    .WithEnvironment("CLICKHOUSE_DB", "bronze");

// D1-dependent: PeerDB constellation or Debezium Server single container
var cdc = builder.AddContainer("analytics-cdc", /* per D1 */)
    .WithReference(urdarbrunnrPostgres)
    .WaitFor(urdarbrunnrPostgres)
    .WaitFor(warehouse);

// dbt as a ONE-SHOT runner, not a service — see §5
var transform = builder.AddContainer("analytics-transform", "ghcr.io/dbt-labs/dbt-core", tag)
    .WithBindMount("../sokkvabekkr/dbt", "/usr/app")
    .WithReference(warehouse)
    .WaitFor(cdc)
    .WithArgs("build", "--profiles-dir", "/usr/app");
```

Notes and open questions:

- **ClickHouse version pin:** ≥ 25.3 is the dbt adapter floor; pin an exact tag in
  Ginnungagap and distribute like every other rune. Version drift between local and client
  metal is a bug class we can delete on day one.
- **Config distribution:** ClickHouse server config + users.xml are Ginnungagap-scattered
  artifacts. No hand-edited XML on any machine, ever. (Yes, ClickHouse config is XML.
  No, Futhark does not get involved — Futhark is for boundary DTOs, not vendor config.
  Stating this now so future-us doesn't get clever.)
- **WSL2 reality check:** this stack is container-pure, so the dual-clone constraint
  (VS debugging) doesn't bite — there's no .NET debug target in the analytics loop v1.
  The mirrored-vs-NAT networking question from the GSA/VPN saga applies to PeerDB's
  outbound connection to Postgres inside the same Docker network — should be a non-issue
  since it's all one compose network, but V1 validates on the real machine.
- **Aspire's role is honest here (RULED):** Aspire adds no analytics-specific value and we
  don't pretend otherwise. Its entire job in this realm is that
  `git clone --recurse-submodules` + `dotnet run` brings the whole constellation up —
  warehouse, river, transforms — with zero further ceremony. Huginn visibility is a free
  side effect, not the justification. The analytics containers do not get, and do not
  want, ServiceDefaults; see §6.

**Gate G3:** approve the AppHost shape before any implementation handoff to CC.

## 4. Landing shape (bronze) — the ReplacingMergeTree covenant

The CDC stream is an append-only log of row versions. Bronze tables honor that:

- One bronze table per source table, schema mirroring source plus CDC metadata columns:
  `_lsn` (version), `_op` (insert/update/delete), `_ingested_at`.
- Engine: `ReplacingMergeTree(_lsn)` ordered by source PK. Deletes are **rows with
  `_op = 'd'`**, never actual deletions — bronze is immutable; a delete is an event that
  happened, and Sökkvabekkr records history, it does not revise it.
- Silver resolves current-state: dedup by PK on max `_lsn`, filter tombstones — in dbt
  models, tested, never via `FINAL` in downstream queries.
- Temporal-table synergy: Urdarbrunnr's system-versioned history remains the *authoritative*
  audit record; Sökkvabekkr's bronze is the *analytical* history. They should agree, and
  "do they agree" is itself a cheap dbt test worth writing (row-count reconciliation per
  table per day). When an auditor asks, Urdarbrunnr answers; when an analyst asks,
  Sökkvabekkr answers.
- **F3 enforcement:** the publication enumerates source-of-truth tables explicitly. JSONB
  projection tables are never added to a publication. This makes the ban structural rather
  than reviewed-for.

**Gate G4:** ratify the bronze covenant (metadata columns, engine choice, tombstone law).

## 5. dbt in the Norse operating model

dbt is a *task*, not a *service* — which maps beautifully onto existing law:

- **Exit code as health contract**, exactly like the Migrations Service. `dbt build`
  (run + test) exits nonzero on any model or test failure; Aspire surfaces it; CI gates on
  it. No health endpoints, no daemon, no scheduler in v1 — locally it runs on demand and
  after CDC settles; in production a scheduler (cron/Temporal/client's orchestrator) is a
  deployment detail, not architecture.
- **Repo home:** proposal — `Sokkvabekkr` as its own repository (dbt project, ClickHouse
  config templates, bronze DDL bootstrap), submoduled into Bifrost like the other realms.
  Transforms are code; they get the same PR/review/CI treatment as C#.
- **house-rules extension needed:** SQL style law does not exist yet. Minimum viable
  doctrine: every silver/gold model has a schema.yml contract with tests (unique PK,
  not-null, accepted-values, reconciliation); warnings-as-errors equivalent =
  `dbt build --warn-error`; no model without a description; snake_case throughout
  (pleasingly, the convention already matches Urdarbrunnr's provider-derived naming).
- **Fusion posture:** write models Fusion-forward now (vanilla SQL + standard Jinja, no
  Python models, no exotic macros) so the classic→Fusion engine swap later is a toolchain
  change, not a rewrite.
- **ClickHouse idiom law** from the matrix (§3 there) applies to every model: insert-only
  increments, ReplacingMergeTree dedup at silver, no mutations anywhere on a scheduled path.

**Gate G5:** ratify repo placement + the SQL house-rules addendum before first model merges.

## 6. Observability (Huginn/Muninn story)

- ClickHouse exports Prometheus metrics natively and can be configured to emit/receive
  OpenTelemetry; wire its metrics endpoint into the local collector so Huginn shows
  warehouse health beside everything else. It will not use `Midgard.ServiceDefaults`
  (not a .NET process) and no vendor exporter enters the container — fan-out stays
  collector-side, per existing law. The mandate "OTel emission is mandatory" translates
  here as: the *collector scrapes it*; the obligation is satisfied at the edge.
- dbt run artifacts (`run_results.json`, `manifest.json`) are the transform layer's
  telemetry. v1: persist them as CI artifacts. Later brainstorm: a tiny relay that emits
  them as OTel spans so a dbt run appears as a trace in Huginn — genuinely differentiating
  demo material, genuinely not v1.
- CDC lag is the metric that matters most: source LSN vs. last-applied LSN. PeerDB exposes
  this; whatever D1 resolves to, "replication lag visible in Huginn" is an acceptance
  criterion, not a nice-to-have.

## 7. Vafþrúðnir reach

ClickHouse's HTTP interface (port 8123) is plain HTTP — Bruno can query it directly. Add a
fourth folder to the harness: smoke queries asserting bronze tables exist, silver dedup
holds (no PK appears twice), and a reconciliation count matches a source-side query via the
existing Postgres connection. The wisdom-contest naming writes itself: Vafþrúðnir asking
the warehouse questions it must answer correctly or forfeit its head.

## 8. What "bring it online sooner" means — a carving order

Smallest honest increments, each independently landable, human gate between every one:

1. **Slice 1 — the hall stands:** ClickHouse under Aspire, version-pinned, volume-backed,
   visible in Huginn. One `SELECT 1` Bruno request. (Half a day of CC work.)
2. **Slice 2 — the river flows:** V1 arm64 validation → D1 decision → CDC container wired
   Postgres→ClickHouse for *one* Mimisbrunnr reference table (small, stable, deterministic
   GUIDs make reconciliation trivial). Bronze covenant applied.
3. **Slice 3 — the carving begins:** dbt project skeleton, one silver model deduping that
   table, schema tests, exit-code wiring, `--warn-error`. CI green.
4. **Slice 4 — a real vein:** first genuine OLTP source table end-to-end, reconciliation
   test against Urdarbrunnr temporal history.
5. **Slice 5 — the return path:** flat-file bronze landing + the staged re-entry into
   Midgard through `IRequestHandler<,>` (this is the F4 doctrine made concrete, and the
   piece most likely to spawn its own brainstorm).

Slices 1–3 are a weekend-scale spike that makes the sovereign cell *demonstrable* — which,
for a consulting platform, is the difference between a matrix on paper and a matrix you can
screen-share.

## 9. Open questions (rolling)

- Q1 **RULED:** Élivágar stays on the bench — no repo. CDC config is deployment artifacts
  inside Sökkvabekkr's repo. The name survives only in the narrative layer, as law demands.
- Q2 **RULED (Forseti's call, delegated):** Cube enters at **v2**, gated on gold-layer
  models existing — a headless semantic layer with nothing to serve is a demo of nothing.
  Once slice 4 lands a real vein, Cube-over-gold embedded in a Blazor component is the
  screen-share that sells the platform; that's worth the v2 slot over v3.
- Q3 **DEFERRED:** multi-source local story shelved for now. An x64 Dell exists if the
  Airbyte path ever needs local exercise; no urgency, revisit when a SQL Server-source
  engagement is real.
- Q4 **OPEN — needs its own session:** backfill/resnapshot when a publication adds a table.
  Known hard parts to bring to that session: initial snapshot vs. streaming handoff without
  a gap or overlap at the LSN boundary; snapshot load on the source; whether bronze
  distinguishes snapshot rows from stream rows (`_op = 'r'`); idempotent re-runs. Runbook
  must be settled before any client engagement.
- Q5 **ANSWERED:** the one sentence is §6 of the matrix doc — *"The WAL is the only event
  stream the warehouse trusts; raw data is immutable; nothing re-enters Midgard except
  through the law."* Proposed home: Glitnir front matter for the analytics realm, with the
  matrix doc citing it rather than owning it.
- Q6 **OPEN — needs its own session:** should Himinbjörg's `norse_identity` ever publish?
  Gate G1 only proves the WAL is readable; it says nothing about whether Identity is a wise
  CDC source. Known hard parts to bring to that session: reconciling `EncryptedString`
  crypto-shredding (key deletion makes ciphertext permanently unrecoverable, by design) with
  bronze's immutability covenant (F5 — nothing lands is ever revised) — a shredded row's
  bronze copy either shreds too (breaking "raw data is immutable") or doesn't (breaking
  right-to-erasure); whether the publication can exclude ciphertext columns outright and
  still be useful; and Himinbjörg's temporal history tables as a second, competing candidate
  for "the" identity history record — does bronze duplicate what temporal already answers,
  or does temporal stay the audit-grade source and bronze stay analytical-grade, per §4's
  "when an auditor asks, Urdarbrunnr answers; when an analyst asks, Sökkvabekkr answers"
  split. No urgency — no publication exists yet for any realm, Identity included.

---

*Nothing above is settled. Forseti proposes; Fenrir disposes.*
