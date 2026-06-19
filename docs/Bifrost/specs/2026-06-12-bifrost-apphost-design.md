# Bifrost AppHost — Local Developer Environment Design

**Date:** 2026-06-12
**Status:** Approved design, pre-implementation
**Resolves:** Bifrost CLAUDE.md §7 #1 (container profile composition, dashboard-first local environment)

---

## 0. Context

This spec designs the `Norse.Orchestration.AppHost` — the Aspire AppHost that is the entire reason Bifrost exists. The goal is a single `dotnet run --project src/Orchestration.AppHost/Orchestration.AppHost.csproj` that stands up the complete local developer environment and leaves it running. Developers open DataGrip, RabbitMQ Management, and ServicePulse against long-lived containers regardless of whether the AppHost is still running.

Related specs:
- `2026-05-20-yggdrasil-hosting-design.md` — §12 (AppHost wiring sketch), §17 (container image deep dive). This spec supersedes the §17 pin decisions for local dev; see §4 below.
- `2026-06-03-messaging-foundation-design.md` — §8.2 (Particular platform containers in local dev). This spec is the implementation of that commitment.

---

## 1. Project Structure

Three files land in Bifrost; nothing in the submodule realms changes.

```
Bifrost/
├── Directory.Build.props                              ← new
├── src/
│   └── Orchestration.AppHost/
│       └── Orchestration.AppHost.csproj              ← new
└── Bifrost.slnx                                      ← updated
```

**`Directory.Build.props`** at the Bifrost root injects `<AssemblyName>Norse.$(MSBuildProjectName)</AssemblyName>` and `<RootNamespace>Norse.$(MSBuildProjectName)</RootNamespace>`, matching every other realm. MSBuild stops at the first `Directory.Build.props` walking up the tree — this file governs only the AppHost; the submodule realms each carry their own and are untouched.

**Assembly name:** `Norse.Orchestration.AppHost`
**Target framework:** `net11.0`
**`global.json`:** already correct (`11.0.100-` floor, `latestFeature` rollForward, `allowPrerelease: true`) — untouched.

**`Bifrost.slnx`** gains one solution folder `/Orchestration/` containing the AppHost project, following the existing `/Primitives/` pattern.

---

## 2. Tag Policy — Developer Machine as Canary

The Yggdrasil hosting spec §17 established full version pins for production-alignment reasons. The AppHost is **local developer infrastructure only** — it never runs in CI or production. That context reverses the trade-off:

> Float the tags. If a new image breaks something it breaks fast on the developer machine. That is cheap, loud, and exactly the signal needed to decide whether to pin something upstream.

**All containers in this AppHost use floating tags** — either the bare variant tag (`management`, `latest`) or the most specific floating tag the image offers. Pinning is a downstream decision triggered by an observed breakage, not a precaution applied here.

The Particular containers were already declared `latest` in §17 as a deliberate exception. This spec generalizes that policy to the entire AppHost: **`latest` (or equivalent floating variant) is the rule, not the exception.**

**`WithImagePullPolicy(ImagePullPolicy.Always)`** is set on every container so the AppHost checks for a newer image on every start. For persistent containers, if the pulled digest differs from the running container's image Aspire is expected to recreate the container automatically. This behavior should be verified empirically the first time a new image is pulled; if Aspire does not catch it, `docker rm <container-name>` followed by an AppHost restart is the fallback.

---

## 3. Persistent Lifetime and Named Volumes

Every container in this AppHost is persistent — it survives AppHost exit and restarts in place on the next `dotnet run`.

**Named Docker volumes** back every stateful container. Volume names follow the platform naming law: responsibility, not technology.

| Volume | Responsibility |
|---|---|
| `norse-relational` | Relational + time-series data (TimescaleDB) |
| `norse-messaging` | Message broker persistence (RabbitMQ) |
| `norse-document` | Document store (MongoDB Atlas Local) |
| `norse-monitoring` | Monitoring backing store (RavenDB for ServiceControl) |

The Particular containers (ServiceControl, ServicePulse) are stateless — their durable state lives in RavenDB under `norse-monitoring`.

---

## 4. Container Topology

### 4.1 Infrastructure layer

| Container name | Image | Tag | Volume mount | Internal port |
|---|---|---|---|---|
| `timescale` | `timescale/timescaledb-ha` | `latest` | `norse-relational` → `/home/postgres/pgdata` | 5432 |
| `rabbit` | `rabbitmq` | `management` | `norse-messaging` → default | 5672, 15672 |
| `mongo` | `mongodb/mongodb-atlas-local` | `latest` | `norse-document` → default | 27017, 27032 |

**TimescaleDB HA quirk:** `PGDATA` lives at `/home/postgres/pgdata`, not the standard Postgres path. The volume mount must target this path explicitly or data will not persist. The image also carries Patroni and pgBackRest ballast that is unused in this single-node local configuration. Standard `POSTGRES_*` environment variables apply.

**MongoDB Atlas Local** bundles `mongot` (the Atlas Search / Vector Search engine) on port 27032 alongside `mongod` on 27017. Both ports are exposed. This image was chosen over `mongo:*-noble` because Vector Search is in scope for the platform's AI layer.

### 4.2 Particular platform layer

| Container name | Image | Tag | Volume mount | Internal port |
|---|---|---|---|---|
| `ravendb` | `particular/ravendb` | `latest` | `norse-monitoring` → default | 8080 |
| `servicecontrol` | `particular/servicecontrol` | `latest` | — | 33333 |
| `servicecontrol-audit` | `particular/servicecontrol-audit` | `latest` | — | 44444 |
| `servicecontrol-monitoring` | `particular/servicecontrol-monitoring` | `latest` | — | 33633 |
| `servicepulse` | `particular/servicepulse` | `latest` | — | 9090 |

**Required environment variables:**

*servicecontrol:*
| Variable | Value |
|---|---|
| `TRANSPORTTYPE` | `RabbitMQ.QuorumConventionalRouting` |
| `CONNECTIONSTRING` | `host=rabbit` |
| `RAVENDB_CONNECTIONSTRING` | `http://ravendb:8080` |
| `REMOTEINSTANCES` | `[{"api_uri":"http://servicecontrol-audit:44444/api"}]` |
| `ENABLEINTEGRATEDSERVICEPULSE` | `false` |
| `PARTICULARSOFTWARE_LICENSE` | from user secrets (see §6) |

*servicecontrol-audit:*
| Variable | Value |
|---|---|
| `TRANSPORTTYPE` | `RabbitMQ.QuorumConventionalRouting` |
| `CONNECTIONSTRING` | `host=rabbit` |
| `RAVENDB_CONNECTIONSTRING` | `http://ravendb:8080` |
| `PARTICULARSOFTWARE_LICENSE` | from user secrets (see §6) |

*servicecontrol-monitoring:*
| Variable | Value |
|---|---|
| `TRANSPORTTYPE` | `RabbitMQ.QuorumConventionalRouting` |
| `CONNECTIONSTRING` | `host=rabbit` |
| `PARTICULARSOFTWARE_LICENSE` | from user secrets (see §6) |

*servicepulse:*
| Variable | Value |
|---|---|
| `SERVICECONTROL_URL` | `http://servicecontrol:33333` |
| `MONITORING_URL` | `http://servicecontrol-monitoring:33633` |

**Startup args:** ServiceControl, ServiceControl Audit, and ServiceControl Monitoring all require a one-time setup pass before normal operation. Pass `--setup-and-run` as the container command. This is idempotent — if setup has already run it is skipped; running it on every start is safe and eliminates the need for a separate init container or two-step startup.

ServicePulse and RavenDB require no special startup args.

**Dependency order:** `ravendb` must be healthy before `servicecontrol` and `servicecontrol-audit` start. `rabbit` must be healthy before all three ServiceControl instances start. `servicecontrol` and `servicecontrol-monitoring` must be healthy before `servicepulse` starts.

---

## 5. Port Binding — No Proxy

All containers bind to fixed host ports with `isProxied: false`. This means:

- Connection strings for dependent services point directly to the container port, not through the Aspire DCP proxy.
- Tools such as DataGrip, RabbitMQ Management UI, and ServicePulse are reachable whether or not the AppHost is running.
- The Aspire dashboard may occasionally show stale health state for persistent containers when the DCP proxy is not running. This is a known, accepted trade-off.

| Container | Host port(s) |
|---|---|
| timescale | 5432 |
| rabbit (AMQP) | 5672 |
| rabbit (management UI) | 15672 |
| mongo (mongod) | 27017 |
| mongo (mongot / Atlas Search) | 27032 |
| ravendb | 8080 |
| servicecontrol | 33333 |
| servicecontrol-audit | 44444 |
| servicecontrol-monitoring | 33633 |
| servicepulse | 9090 |

---

## 6. Particular License

`PARTICULARSOFTWARE_LICENSE` must never be committed to the repository. It is supplied via .NET user secrets at the Bifrost root. The implementation plan will include the `dotnet user-secrets set` command that seeds it. Developers without a license key cannot start the Particular stack; the three ServiceControl containers will fail loudly and immediately — correct behavior.

---

## 7. Staged Commit Plan

Implementation lands in two commits. The human reviews the running dashboard before each commit is made.

**Commit 1 — Infrastructure layer**
Files: `Directory.Build.props`, `src/Orchestration.AppHost/Orchestration.AppHost.csproj`, `Bifrost.slnx`
Containers wired: `timescale`, `rabbit`, `mongo`

Review gate: Aspire dashboard shows all three containers healthy and persistent. DataGrip connects to `localhost:5432` with the AppHost stopped.

**Commit 2 — Particular platform layer**
Files: AppHost updated
Containers wired: `ravendb`, `servicecontrol`, `servicecontrol-audit`, `servicecontrol-monitoring`, `servicepulse`

Review gate: ServicePulse at `localhost:9090` shows all three ServiceControl instances connected.

---

## 8. Open Items

1. **`WithImagePullPolicy(Always)` + persistent container recreation.** The expected behavior is that Aspire recreates a persistent container when a newly pulled image digest differs from the running container's. This must be verified empirically on the first image update after the AppHost is live. If Aspire does not handle it automatically, document the `docker rm <name>` + restart fallback.

2. **MongoDB Atlas Local auth model.** The `mongodb/mongodb-atlas-local` image may differ from the `mongo` library image in its `MONGO_INITDB_*` environment variable support and default auth posture. Confirm during implementation.

3. **RavenDB shared instance for both ServiceControl instances.** Both `servicecontrol` and `servicecontrol-audit` point to the same `http://ravendb:8080`. Confirm that a single `particular/ravendb` container cleanly hosts two separate ServiceControl databases without collision.

---

## Self-Review Checklist

- [x] No TBDs or placeholders — open items are tracked in §8 with conditions.
- [x] Internally consistent — tag policy (§2), topology (§4), and port table (§5) describe the same system.
- [x] Scoped to one implementation plan — AppHost only; no realm code, no product code.
- [x] No absolute paths — all paths are workspace-relative.
- [x] No committed secrets — Particular license via user secrets, §6.
- [x] Naming follows responsibility not technology — volume names (§3), consistent with platform law.
