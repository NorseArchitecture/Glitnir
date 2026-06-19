# Bifrost AppHost — Postgres Primary+Replica Swap (Drop Atlas + Timescale)

**Date:** 2026-06-16
**Status:** Approved design, pre-implementation
**Amends:** `2026-06-12-bifrost-apphost-design.md` §3 (volumes), §4 (topology), §5 (ports), §7 (commit plan) — the infrastructure layer is re-cut around a PostgreSQL streaming pair; the Particular platform layer is untouched.
**Companion:** `2026-06-16-postgres-document-store-decision-inputs.md` (the persistence pivot this composition validates by building) and `poc/pg19-document-store` (the proven primary+replica this faithfully ports).

---

## 0. Context

The persistence pivot (`2026-06-16-postgres-document-store-decision-inputs.md`) culls MongoDB and makes PostgreSQL the operational read store via a streaming replica, PoC-validated. This spec brings the **local dev composition** in line: the AppHost drops the Atlas (MongoDB) and TimescaleDB containers and stands up the PG19 primary+replica from the PoC.

This is a **Bifrost-local composition change** — "validate by building." It does **not** ratify the persistence verdict (still gated on host-stack AOT + Q6) and does **not** touch the Glitnir persistence §4 law. It updates only the AppHost, this design record, and Bifrost's own README/CLAUDE.md.

The AppHost is the platform's proving ground — the place where developer and enterprise productivity speed is clocked. That ethos drives the one non-obvious decision below (real scram auth, not the PoC's dev-only trust).

---

## 1. Topology Delta

| Container | Action | Note |
|---|---|---|
| `timescale` | **remove** | Time-series dropped "for the time being"; relational role moves to `pg-primary`. |
| `mongo` (atlas-local) | **remove** | Mongo culled; read store is the Postgres replica. Removes `mongot` / Atlas Vector Search from dev (see §5). |
| `pg-primary` | **add** | PG19 source of truth + WAL sender. |
| `pg-replica` | **add** | Streaming hot standby — the operational read store. |
| `rabbit` | keep | unchanged |
| `ravendb`, 3× `servicecontrol`, `servicepulse` | keep | unchanged (NSB platform; its magic-string retrofit is a separate work item) |

Credentials (`postgres-password`, `rabbitmq-user`, `rabbitmq-password`) are unchanged `AddParameter` values from `appsettings.json`.

---

## 2. Tag Policy — a Deliberate Exception

`2026-06-12` §2 floats tags (developer machine as canary). This pair **pins `postgres:19beta1-trixie`** — the exact bits the PoC proved. Floating `postgres` would resolve to PG18-stable and silently abandon the PG19 behavior under test. The pin is the deliberate exception; it lifts to a floating PG19 tag (or stable) when PG19 reaches GA. `WithImagePullPolicy(ImagePullPolicy.Always)` still applies.

---

## 3. Primary — `pg-primary`

- **Image:** `postgres:19beta1-trixie`, persistent lifetime.
- **Env:** `POSTGRES_USER=postgres`, `POSTGRES_PASSWORD` ← `postgres-password` parameter, `POSTGRES_DB=norse`.
- **Command args:** `postgres -c wal_level=replica -c max_wal_senders=10 -c max_replication_slots=10 -c hot_standby=on`. (`synchronous_commit` stays default/async — the PoC measured sub-2 ms replica lag, W1.)
- **Replication hba:** the official image writes `host all all all scram-sha-256` but **no replication line**. A bind-mounted `initdb.d` script appends `host replication all all scram-sha-256` (the scram form of the PoC's trust line — this is the auth change). Runs once during init; the final server reads it on start.
- **Volume:** `norse-pg-primary` → `/var/lib/postgresql/data` (stock path).
- **Endpoint:** host **5432** → target 5432, `isProxied: false` (DataGrip muscle memory; replaces Timescale's 5432).

## 4. Replica — `pg-replica`

- **Image:** `postgres:19beta1-trixie`, persistent lifetime.
- **Entrypoint override:** `WithEntrypoint("bash")` + a bind-mounted clone script. On first start (no `standby.signal`): `PGPASSWORD=$POSTGRES_PASSWORD pg_basebackup -h <primary> -p 5432 -U postgres -D "$PGDATA" -Fp -Xs -R -w -P` **in a retry-until-success loop**, then `chown -R postgres:postgres "$PGDATA"`, then `exec gosu postgres postgres`. The retry loop is the real readiness guard; `.WaitFor(pgPrimary)` is best-effort ordering on top. `POSTGRES_PASSWORD` is supplied to the replica solely for the script's `PGPASSWORD` (the replica is cloned, never `initdb`'d).
- **Volume:** `norse-pg-replica` → `/var/lib/postgresql/data`. The persistent volume + the `standby.signal` check means the clone runs only on first start; restarts resume streaming.
- **Endpoint:** host **5433** → target 5432, `isProxied: false`.
- No named replication slot (PoC parity); slots are a future hardening if WAL retention on the primary becomes an issue.

## 5. Scripts Live in the AppHost, Not Glitnir

The two scripts are copied (scram-adapted) into the AppHost project and bind-mounted:

```
src/Orchestration.AppHost/postgres/
├── replication-hba.sh        → /docker-entrypoint-initdb.d/ on pg-primary
└── replica-entrypoint.sh     → bind-mounted on pg-replica, run via bash
```

The AppHost must **not** reference `Glitnir/poc/...` — Glitnir is the design court, never a runtime dependency. Aspire resolves `WithBindMount` source paths relative to the AppHost project directory; the scripts ship with the project. Both files are `eol=lf` (a stray CR breaks them under the Linux container's bash/psql) — `.gitattributes` enforces it.

---

## 6. Ports

| Container | Host port(s) | Was |
|---|---|---|
| `pg-primary` | 5432 | (timescale 5432) |
| `pg-replica` | 5433 | new |
| `rabbit` | 5672, 15672 | unchanged |
| ravendb / servicecontrol×3 / servicepulse | 8080 / 33333 / 44444 / 33633 / 9090 | unchanged |

`27017` / `27032` (mongo) retired.

---

## 7. The One Open Item — Replica → Primary Addressing

docker-compose gave the PoC service-name DNS (`-h primary`). Aspire's cross-container raw-TCP name resolution for `AddContainer` resources is not guaranteed for a raw libpq connection. **Proposed default:** the replica clones via `host.docker.internal:5432` — the primary publishes 5432 unproxied, and Docker Desktop (Windows) resolves `host.docker.internal` to the host. **At implementation:** verify whether the container name `pg-primary` resolves on Aspire's network; if it does, prefer it (cleaner). This is the piece most likely to need a first-run tweak — fail loudly there, don't paper over it.

---

## 8. Consequence — Vector Search Leaves Dev

atlas-local bundled `mongot` (Atlas Vector Search). Dropping it removes vector search from the local environment. This is consistent with the pgvector-via-Semantic-Kernel direction, but `postgres:19beta1-trixie` does not ship pgvector — so **for now there is no vector store in dev.** Tracked as a future item (a pgvector-enabled image, or an init that builds the extension); not a blocker for this swap.

---

## 9. Implementation — One Increment

Per the proving-ground intent, the full primary+replica lands together (the PoC already proved the pair). Files touched:

- `src/Orchestration.AppHost/Program.cs` — remove `timescale` + `mongo`; add `pg-primary` + `pg-replica`.
- `src/Orchestration.AppHost/postgres/replication-hba.sh`, `replica-entrypoint.sh` — new, scram-adapted.
- `src/Orchestration.AppHost/.gitattributes` (or the Bifrost root's) — `*.sh text eol=lf`.
- README.md + CLAUDE.md (Bifrost) — §7 #1 container profile now partially resolved (PG primary+replica, RabbitMQ, Particular stack); Mongo/Timescale removed from the narrative.

**Review gate (the dashboard):** Aspire shows `pg-primary` and `pg-replica` healthy and persistent; DataGrip connects to `localhost:5432` (primary) and `localhost:5433` (replica) with the AppHost stopped; a write on 5432 appears on 5433 (streaming confirmed, as in the PoC).

The Glitnir persistence §4 verdict sweep is **out of scope** and untouched.

---

## Self-Review Checklist

- [x] No TBDs — the single open item (§7) has a proposed default and a verify-step.
- [x] Internally consistent — topology (§1), primary/replica (§3/§4), ports (§6) describe one system; the Particular layer is explicitly unchanged.
- [x] Scoped to one implementation plan — AppHost composition only; no realm code, no product code, no NSB-resilience retrofit (separate item).
- [x] Amends, not contradicts — §3/§4/§5/§7 of `2026-06-12` are superseded for the infra layer; the Particular layer and tag/lifetime/no-proxy principles carry forward.
- [x] No absolute paths — workspace-relative throughout.
- [x] No committed secrets — auth via the existing `postgres-password` parameter.
- [x] Naming follows responsibility — volume names `norse-pg-primary`/`norse-pg-replica` (the relational/document split collapses to one engine).
