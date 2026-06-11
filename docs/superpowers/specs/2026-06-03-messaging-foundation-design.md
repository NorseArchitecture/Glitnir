# Messaging Foundation — NServiceBus Decision and Design

**Date:** 2026-06-03
**Status:** Approved design, pre-implementation
**Resolves:** CLAUDE.md §7 #2 (NServiceBus vs. Wolverine)

---

## 0. Context

This spec resolves the messaging-library question and designs the messaging foundation it unlocks: endpoint topology, registration model, message placement, transaction architecture, recoverability, and the Particular platform deployment posture.

The trigger is **NServiceBus 10.2** (Particular, 2026), which shipped the two capabilities that were previously the strongest arguments against NServiceBus for this platform:

- **`AddNServiceBusEndpoint`** — first-class `Microsoft.Extensions.Hosting` support for hosting multiple logical endpoints in a single process, with per-endpoint dependency isolation. The prior one-endpoint-per-process model would have fought the single `Norse.Hosting.Web.Server` deployable; 10.2's model *is* our hosting model.
- **Assembly scanning can be disabled entirely**, replaced by explicit registration paired with source-generated lookup for handlers, sagas, and features. This eliminates the CLAUDE.md §8 "no DI registration by convention scanning" conflict.

**Related specs:**

- `2026-05-20-yggdrasil-hosting-design.md` — plugin model, webhook controller pattern. Amended by §12 of this spec (`IWebhookDispatcher` deleted; Worker↔Server relationship corrected).
- `2026-05-21-midgard-persistence-design.md` — CQRS write pipeline, chained commands, outbox semantics. This spec supplies the messaging-library specifics that spec deferred. Amended by §12 (entity placement).
- `2026-05-26-mediator-design.md` — HTTP-server-only mediator. Unchanged; its NServiceBus assumptions are now ratified.

---

## 1. Decision Record

**NServiceBus, version floor 10.2. CLAUDE.md §7 #2 is RESOLVED.**

Drivers, in order of weight:

1. **Multi-endpoint hosting matches the deployable.** `AddNServiceBusEndpoint` hosts every context's endpoints inside the single `Norse.Hosting.Web.Server` process with per-endpoint DI isolation — a structural match for the one-plugin-per-context model.
2. **Source-generated explicit registration.** Scanning disabled globally; handlers, sagas, and features register via 10.2's source-generated lookup. The strongest technical argument for Wolverine is gone.
3. **ServicePulse / ServiceControl.** Paid recoverability, audit, and retry tooling, available today, with deep operational experience on this team. Chosen over waiting for the Critter Stack equivalent to mature. Licensing cost is an accepted, deliberate trade.
4. **AOT is a trajectory bet, recorded honestly.** 10.2's registration changes are a *foundation* for AOT/trimming, not compliance. `Norse.Hosting.Web.Server` is not Native-AOT today and will not be until at least NServiceBus v11. "AOT-clean where feasible" continues to apply to Primitives's primitives regardless.

Consequences:

- 10.2 deprecates the self-hosting APIs, so this platform never writes legacy `EndpointConfiguration` self-hosting code. The `AddNServiceBusEndpoint` shape is the only shape.
- **Wolverine and MassTransit are both "not in use."** The reversibility motive behind library-agnostic contracts is gone; the POCO-contract rule survives for a different reason (§4).
- **Licensing sizing/procurement is a business action item**, not an architecture question. Tracked outside this spec.

---

## 2. Endpoint Topology

**Two endpoint flavors per bounded context, both riding in `Norse.Hosting.Web.Server`:**

| | Worker endpoint | Server endpoint |
|---|---|---|
| Name | `{company}.{context}` | `{company}.{context}.web` |
| Purpose | Command handlers and sagas — mutates the system of record | Sends out-of-band work to the worker (shim → command); subscribes to worker events to push UI notifications (gRPC stream / SignalR) |
| Outbox | **On** | **Off** |
| SQL persistence | Outbox + saga tables | **None** |
| Queue | Durable | **Ephemeral, per-replica** (instance-discriminated, auto-delete) |
| Loss tolerance | Never loses a message | Misses during downtime are acceptable — clients re-fetch current state from Mongo on reconnect |

The per-replica queue on the server endpoint is required, not an optimization: a gRPC-stream/SignalR client can be connected to *any* host replica, so every replica needs its own copy of each event. Exactly-once delivery would be actively wrong there.

Endpoint names are lowercase (`{company}.billing`, `{company}.billing.web`). RabbitMQ queue names follow endpoint names. Events route via RabbitMQ native pub/sub (exchanges); **no subscription persistence is needed**.

The `Norse.Hosting.Worker` split remains deferred exactly as CLAUDE.md §4 states. When it activates, worker endpoints move with their plugins; nothing in this design changes.

### 2.1 Canonical write lifecycle

1. HTTP POST arrives at `.Server`. It validates authority, shims the request portion of the wire document into Mongo with `ProcessingStatus = Pending` — an immediate GET shows data even if the back end goes sideways.
2. `.Server` sends the command via its context's `IMessageSession` → the durable worker queue. (Shim is best-effort; dispatch must succeed — per the persistence spec §6.3.)
3. The `.Worker` handler chain executes: business logic → system-of-record write (Postgres, atomic with the outbox) → follow-on command enriches the Mongo document with the result → **the event publishes last**.
4. Every replica's `{company}.{context}.web` endpoint receives the event and notifies connected, authorized users via gRPC stream / SignalR.

---

## 3. Registration Model

**The hosting runtime owns the endpoint; the plugin contributes only its handler set.**

`Norse.Hosting.{Web.Server|Worker}` calls `AddNServiceBusEndpoint` once per registered context plugin, deriving the endpoint name from the context by convention. Platform-shaped configuration is set in exactly one place and has **no per-plugin override surface** (§2.3 of CLAUDE.md: no extension point until a concrete need exists):

- RabbitMQ transport (CloudAMQP)
- `TransportTransactionMode.ReceiveOnly` (§5)
- System.Text.Json serializer
- Unobtrusive message conventions (§4)
- Outbox on (worker endpoints) / off + ephemeral queue (server endpoints)
- SQL persistence against the Infrastructure-resolved connection (worker endpoints only)
- Recoverability policy, error/audit queue wiring (§7)
- Outgoing `MessageId`-stamping behavior *(added 2026-06-03, mediator spec §5)*: for commands implementing the Abstractions `ResourceId` marker, an outgoing pipeline behavior stamps `MessageId = UUIDv5(command type, ResourceId)` — frontier idempotency extends into broker-level dedup; senders never touch `SendOptions`

The plugin's single messaging duty is its **explicit handler/saga registration list**, expressed through NServiceBus 10.2's source-generated lookup. Assembly scanning is disabled globally. `IWorkerHostPlugin` (and `IWebHostPlugin`, for the server endpoint's event subscriptions) each grow one narrow contribution method — the messaging mirror of how plugins contribute `IEntityTypeConfiguration<T>` while Infrastructure owns the DbContext.

A plugin author cannot misconfigure the transport, forget the outbox, invent a queue-naming scheme, or register a state-mutating handler on a web endpoint without it being a build error candidate (§11). The wrong thing has no API surface.

---

## 4. Message Placement, Conventions, Serialization

### 4.1 The `.Backend` assembly

Each context gains **`{Company}.{Context}.Backend`** — the server-side shared assembly. It holds:

- Server→worker commands
- Mongo document C# records (the server serves them; the worker writes them)
- Shared server-side options/constants

`.Backend` runs only in server-context processes; it is never referenced by `Components` and never lands in a WASM/MAUI bundle.

### 4.2 Hard walls (project-layout correction)

**`.Server` and `.Worker` are mutually invisible.** Neither references the other; both reference `.Backend` and `.Contracts`. The worker never references ASP.NET Core; the server never references EF Core or entity types. Prior documentation stating `.Worker` references `.Server` "for shared internals" was wrong and is corrected by this spec (§12).

**SQL entities and `IEntityTypeConfiguration<T>` implementations live solely in `.Worker`.** This turns "public HTTP cannot reach Postgres under any code path" from an analyzer promise into a reference-graph fact: the server tier cannot dig into the system of record because the types are not there to dig with. Infrastructure's `IEntityTypeConfiguration<T>` scan retargets from `.Server` to `.Worker`.

### 4.3 Message placement

| Message kind | Lives in | Visibility |
|---|---|---|
| Events | `{Company}.{Context}.Contracts` | Public — the cross-context surface |
| Server→worker commands | `{Company}.{Context}.Backend` | Context-internal |
| Worker-private chain commands (segmenting third-party calls into replay-safe steps) | `{Company}.{Context}.Worker`, `internal` | Worker-only |

Cross-context command sends have no compile path: another context's commands are internal types in assemblies it does not reference. Events are the only cross-context message surface, per CLAUDE.md §3.

### 4.4 Unobtrusive conventions

Message-bearing assemblies (`Contracts`, `Backend`, `Worker` message types) reference **no NServiceBus package**. Messages are POCO `sealed record` types. The hosting runtime declares unobtrusive-mode conventions once:

- `*Event` suffix → event
- `*Command` suffix → command

The pre-existing Event-suffix naming rule is now bus-load-bearing. The original motive for POCO contracts (reversibility) is gone; the surviving motives are WASM-bundle hygiene for `Contracts` and a single point of convention truth.

### 4.5 Serialization, versioning, PII

- **System.Text.Json**, platform-wide, set by the hosting runtime.
- **Events are versioned from day one.** A breaking change ships a new event type; old types are deprecated, never mutated. (CLAUDE.md §3, unchanged.)
- **PII:** `YGG101` bars bare `string` properties on event/command/notification types outright — domain types, `EncryptedString` (PII), or `PlainText` (non-sensitive) only. §7 #11 was resolved 2026-06-03: AES-256-GCM at application level, one mechanism wire + at rest, Key Vault envelope encryption, per-customer DEKs. `EncryptedString` carries ciphertext inside the System.Text.Json payload — the worker decrypts in-process after receive. See CLAUDE.md §4 → PII and Encryption. *(Amended 2026-06-03)*

---

## 5. Transaction Architecture and Replay Safety

### 5.1 `TransportTransactionMode.ReceiveOnly`, globally, non-overridable

ReceiveOnly is the one transaction mode every NServiceBus transport supports. Pinning it gives **transport-ubiquitous behavior**: if RabbitMQ ever gives way to Azure Service Bus or SQS, handler semantics do not change. The hosting runtime sets it; no endpoint can override it.

### 5.2 Outbox on worker endpoints

Every worker endpoint runs the NServiceBus Outbox: receive-side deduplication plus outgoing messages persisted atomically with the handler's Postgres transaction. The net effect is effectively-once *processing* for Postgres-mutating handlers.

**`Norse.Infrastructure.Persistence` owns the wiring** that enlists the per-context DbContext in NServiceBus's storage session (same connection, same transaction). Handler authors never see a transaction, a connection, or `SaveChangesAsync` — exactly as the persistence spec demands. Neither outbox-shaped boundary lives in handler code: the server-side shim+dispatch is policy ("shim is best-effort, dispatch must succeed"); the worker-side commit+send is the library's outbox.

Server endpoints run **no outbox and no SQL persistence**: they are ephemeral notification fan-out, and their event handlers must tolerate both loss and duplication.

### 5.3 The golden rule

> **A handler that has mutated state must not mutate again — it sends the next command.** One mutation per handler; multi-step work is a command chain.

ReceiveOnly means any handler can run at least twice. A handler that performs mutation A then mutation B can crash between them and replay A. The outbox shields Postgres mutations; everything it cannot shield — third-party API calls, Mongo enrichment — must be segmented into its own replay-safe handler, using worker-private chain commands (§4.3). This is the persistence spec's "6 little lines of fail" discipline, stated as law with its enforcement rationale.

Static enforcement of "one mutation per handler" is not tractable; this rule is review discipline plus chain/saga design. §11 records it as a documentation-level rule, not an analyzer.

### 5.4 Sagas

Sagas are the sanctioned shape for long-running, stateful coordination (policy bind orchestration, claims workflows). Saga data records are explicitly mapped, registered via source-generated lookup, and stored in the owning context's schema via SQL persistence.

---

## 6. NServiceBus Persistence Table Deployment

Outbox and saga tables use **NServiceBus SQL Persistence on PostgreSQL**, living in each context's schema (`billing.outbox_data`, `billing.saga_*`).

1. SQL Persistence emits its DDL scripts at build time. **`Norse.Hosting.Migrations.Service` applies them** per context alongside the EF migrations, in dependency order, behind the same `/health` readiness gate.
2. **`EnableInstallers()` is local-dev (Aspire) only.** Production table creation at endpoint startup is the silent-startup-DDL that CLAUDE.md §8 forbids, and table-creation races across replicas are real.
3. **Nobody hand-edits NServiceBus's table shapes.** They are library-owned, version-promoted artifacts; upgrades re-emit and re-promote.

---

## 7. Recoverability

Set once by the hosting runtime, applied to every worker endpoint:

- **Immediate retries: 3** — transient noise (deadlock victim, brief connection blip).
- **Delayed retries: 3 with increasing backoff** — infrastructure-scale blips (NServiceBus stock defaults; tune only with operational evidence).
- Exhaustion → the **`error` queue**, where the ServicePulse inspect/edit/retry workflow takes over.
- **`audit` queue on for every endpoint** — every processed message flows to ServiceControl Audit. The audit trail and the retry console are, together, the operational justification for the Particular license.

Server endpoints get minimal recoverability (no delayed retries, no audit) — their messages are ephemeral notifications with no replay value.

---

## 8. Particular Platform Deployment

### 8.1 Production (Azure Container Apps)

Four official Particular containers:

| Container | Role | Storage |
|---|---|---|
| ServiceControl (error) | Error-queue ingestion, retry orchestration | Azure Files volume (embedded RavenDB) |
| ServiceControl Audit | Audit-queue ingestion, message history | Azure Files volume (embedded RavenDB) |
| ServiceControl Monitoring | Endpoint queue/processing metrics | — |
| ServicePulse | Operations web console | — |

They ingest from the `error`/`audit` queues on CloudAMQP. **ServicePulse gets internal ingress only plus staff auth** — it is an operations console, never public.

### 8.2 Local development

`Norse.Hosting.AppHost` orchestrates the same four containers alongside RabbitMQ, Postgres, and Mongo. Every developer gets the full failed-message → inspect → retry workflow and audit trails at production fidelity from `dotnet run --project Norse.Hosting.AppHost`. This is deliberate: the audit platform is the reason NServiceBus won; developers live in it from day one.

### 8.3 Observability boundary

ServicePulse owns **message-level** operations: failures, retries, audit, saga visualization. Observability is the platform observability layer: logs, metrics, traces, SLOs (the observability realm — renamed from Heimdall 2026-06-07; Heimdall is now auth). The Monitoring instance's queue metrics are an input Observability may scrape; neither replaces the other.

---

## 9. Send Surface

`.Server` resolves its own context's **`IMessageSession` directly** from the endpoint's per-context registration; plugin DI isolation ensures Billing code sees only Billing's session. No wrapper, no dispatch port.

**`IWebhookDispatcher` is deleted** from the hosting spec. It existed solely as a library-agnostic placeholder pending §7 #2; with the decision final, §2.5 (simplicity over ceremony) says the abstraction goes. The webhook controller pattern re-lands directly on `IMessageSession`. The "wrap what you don't own" rule's intent — a test seam — is satisfied by `NServiceBus.Testing` (§10).

---

## 10. Testing

- **Unit:** `NServiceBus.Testing` — `TestableMessageSession`, `TestableMessageHandlerContext` — with Shouldly assertions and NSubstitute for genuine collaborators.
- **Integration:** chain behavior, outbox atomicity, and recoverability semantics are database/broker semantics — per the no-mocked-DB rule, they get integration tests against real RabbitMQ + Postgres (testcontainers).

---

## 11. Enforcement (Analyzer Candidates)

To be numbered and built in the Norse.Primitives.Architecture work; recorded here so they are not lost:

| Candidate | Rule |
|---|---|
| Message placement | A `*Event` type outside `Contracts`, or a `*Command` type in `Contracts`, is a build error |
| Endpoint flavor | A state-mutating (worker) handler registered on a `.web` endpoint is a build error |
| Worker purity | `{Company}.{Context}.Worker` referencing ASP.NET Core assemblies is a build error |
| Server purity | `{Company}.{Context}.Server` referencing EF Core or `.Worker` is a build error |
| YGG405 (carried) | Worker assemblies do not reference `Norse.Abstractions.Mediator` / `Norse.Infrastructure.Mediator` |

The golden rule (§5.3) is documentation + review discipline, not an analyzer.

---

## 12. Amendments to CLAUDE.md and Prior Specs

**CLAUDE.md:**

- §4 Messaging — rewritten: NServiceBus 10.2 floor, ReceiveOnly, worker-endpoint outbox, two endpoint flavors per context, unobtrusive conventions; Wolverine moves to "not in use" alongside MassTransit.
- §7 #2 — marked RESOLVED, pointing at this spec.
- §5 project layout — `{Company}.{Context}.Backend` added; SQL entities + `IEntityTypeConfiguration<T>` move to `.Worker`; Worker↔Server corrected to mutually invisible hard walls; Infrastructure scan retargets `.Worker`.

**Hosting spec (`2026-05-20`):** `IWebhookDispatcher` deleted in favor of direct `IMessageSession`; plugin interfaces gain the handler-contribution method; "references `.Server` for shared internals" language corrected.

**Persistence spec (`2026-05-21`):** entity/EF-configuration placement follows the wall (`.Server` → `.Worker`); "messaging-library pick" out-of-scope note now resolved by this spec.

The CLAUDE.md edits landed with this spec. The hosting/persistence spec amendment edits were applied 2026-06-03 (same day, follow-up changeset), each marked with an **Amended** header note and inline *(Amended 2026-06-03)* annotations. One placement question surfaced during amendment and is recorded as persistence spec §17 #6: wire-shape documents (`PolicyView : IWireShape`) sit in `Contracts` per the persistence spec but "Mongo document records" belong to `.Backend` per this spec — the same type cannot satisfy both. Expected to resolve with the mediator spec.

---

## 13. Open Items

1. **Particular licensing sizing/procurement** — business action item; architecture does not block on it.
2. **CLAUDE.md §7 #11 (PII/encryption posture)** — ~~explicit confirmation still outstanding~~ **RESOLVED 2026-06-03**: AES-256-GCM confirmed; `YGG101` strict form (no bare `string` on message types, `[NonSensitive]` deleted, `PlainText` wrapper added); Key Vault envelope encryption; per-customer DEKs for crypto-shredding. The `EncryptedString` implementation spec is the surviving work item.
3. **Retry-count tuning** — stock 3/3 until operational evidence says otherwise.

---

## Self-Review Checklist

- [x] No placeholders or TBDs — open items are explicitly tracked in §13 with owners/conditions.
- [x] Internally consistent — endpoint flavor table (§2), lifecycle (§2.1), and transaction architecture (§5) describe the same system.
- [x] Scoped to one implementation plan — messaging foundation only; analyzer construction and prior-spec amendment text are queued work, not designed here.
- [x] No two-way ambiguity — outbox applies to worker endpoints only; commands have exactly one home per kind; ReceiveOnly is non-overridable.
