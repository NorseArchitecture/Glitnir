# ServiceDefaults Layer 0 — The Shared Observability Root

**Date:** 2026-07-28
**Status:** Brainstorm-ratified 2026-07-28; awaiting written-spec review before planning
**Executes:** the 2026-06-11 ruling placing `ServiceDefaults` in Midgard (`codenames.md`, Yggdrasil row) — this is the first slice of that composition
**Scope guard:** Layer 0 only. Layers 1–3 and the terminal layer are enumerated so their boundaries can be respected, not so they can be built.

---

## 1. Context

### 1.1 The motivating incident

The Migrations Service audit (2026-07-28 morning) initially read the init container as "observably silent." **Corrected the same day, during spec review: the container is not mute** — `Host.CreateApplicationBuilder(args)` registers the console provider by default and its lifetime/migration logs flow — **it is observably impoverished.** No OTel pipeline at all: no resource attributes, no OTLP export, no dashboard presence, no participation in tracing or metrics — and, by design, no participation in health checks either (§2.7: exit code is its contract, forever). The requirement the incident actually motivates: the init container exports telemetry exactly as if it were Web.Server or the Worker — runtime items now (this layer), database items when Layer 1 lands (its consumer set includes the Migrations Service, §4).

*Case file, per the discipline that got the Gjallarhorn precedent written down:* the original "zero console logging" claim was a misdiagnosis, caught before implementation chased it. The authoring side needs zero work — `MigrationRunnerService` already logs through `LoggerMessage` source-gen against `ILogger<T>`; `Hosting.Migrations.Service` ships no `appsettings.json` and trusts host-builder defaults, which do include console. The *mechanism* of the misread is pinned by plan Task 0 (working hypothesis: the audit read the OTLP-fed Structured-logs pane — empty before any OTLP export existed — while console output sat live in the per-resource console pane); the finding lands here as the §1.1 addendum.

If the init container is telemetry-poor, the others are only accidentally rich. The fix is a shared root every container composes, not a one-off per host.

### 1.2 The verdict on observability

OpenTelemetry won the war. Tracing, metrics, and logging flow through OTel primitives; `ILogger<T>` remains the authoring API; OTel is the pipeline and the wire. No Serilog sinks, no App Insights SDK, no bespoke logging abstractions — and no vendor exporter package ever enters a container (§2.2).

### 1.3 The four consumers

| Container | Persistence (EF) | ASP.NET serving | Messaging + HttpClient egress | Identity/OpenIddict |
|---|---|---|---|---|
| Stories.Server | ✗ | ✓ | ✗ | ✗ |
| Migrations Service | ✓ | ✗ | ✗ | ✗ |
| Web.Server | ✓ | ✓ | ✓ | ✓ (sole owner) |
| Worker | ✓ | ✗ | ✓ | ✗ |

The only universally shared column is the one not in the table: base OpenTelemetry. That is Layer 0.

---

## 2. Decisions

### 2.1 Naming — the lore dissipates at the file path

The layer family lands in Midgard as function-named projects, brand-injected per platform law (`codenames.md` rule #2):

```
src/Infrastructure.ServiceDefaults              → Norse.Infrastructure.ServiceDefaults   (Layer 0 — this spec)
src/Infrastructure.ServiceDefaults.Persistence  (Layer 1 — fast follow)
src/Infrastructure.ServiceDefaults.AspNet       (Layer 2 — fast follow)
src/Infrastructure.ServiceDefaults.Messaging    (Layer 3 — fast follow)
```

`.Messaging`, not `.Ratatoskr`; `.Persistence`, not `.Urdarbrunnr`. A consumer who forks the library gets names that say what they do, not an obligation to know who the squirrel is.

One project per layer, not one project with feature flags. The reason is the Worker guarantee: the stock Aspire ServiceDefaults template carries `<FrameworkReference Include="Microsoft.AspNetCore.App" />` in the single shared project, which drags the entire ASP.NET shared framework into every consumer — including workers and init containers that must never serve HTTP. That FrameworkReference is the root cause of the template's worst property, and separate assemblies are its cure: Layer 0 carries no FrameworkReference, ever; only `.AspNet` does. The matrix becomes compiler-enforced and auditable by Ginnungagap's banned-symbol analyzers.

### 2.2 The observability taxonomy — two tiers, one of them deliberately unnamed

- **Midgard proper — mandatory emission, no opt-out.** `Infrastructure.ServiceDefaults` lives directly in Midgard; every container calls `AddServiceDefaults()`; there is no opt-out flag, no conditional registration, no "lightweight" variant. Emission is a reflex, not a jurisdiction, and it earns no mythological name for the same reason breathing doesn't.
- **The exporter jurisdiction — where telemetry becomes visible.** Collector deployment, OTLP pipeline configuration, routing, dashboards, retention, alert rules. All vendor heft (Seq / Prometheus / Jaeger / Grafana) lives here, and most of it is configuration, not C#: containers export OTLP to one endpoint and fan-out happens collector-side. One structural proof this holds: pull-based Prometheus scraping requires an in-process HTTP endpoint the Worker can never carry — push OTLP outward and let the collector expose the scrape endpoint, and the law holds with zero exceptions. **Standing rule: no vendor exporter package ever enters a container.** If an exception is ever forced, the consumer matrix grows a column — treat that as the fire alarm it is.

**The exporter jurisdiction is not named in this spec, and Huginn & Muninn stay on the bench.** Rule #4 of `codenames.md`: a name leaves the bench only in the same change that introduces the real component. Layer 0 builds zero collector-side artifacts, and observability has been burned by exactly this once already — Gjallarhorn was stripped of a premature `Norse.Observability` binding in the crooked-path #9 cleanup. The ravens' essence — Huginn (Thought) as the ephemeral live view that dies with the session, Muninn (Memory) as the durable pipeline whose loss Odin trembles more for — maps onto the ephemeral-dashboard / durable-backend split so well it reads as operational priority ordering written down a millennium early. That is an observation in the bench tradition, not a reservation. The names leave the bench when the collector pipeline and compose fragments actually land.

### 2.3 Shape — Aspire-template-faithful, Norse-hardened

`AddServiceDefaults()` mirrors the canonical Aspire ServiceDefaults structure (any Aspire-literate developer reads it instantly; the standalone dashboard and collector world are tested against it) with the Layer 2/3 concerns subtracted: no ASP.NET instrumentation, no service discovery, no HTTP resilience, no HttpClient defaults. Ecosystem names are kept, not fought — the method is `AddServiceDefaults()`, not a novel Norse verb.

### 2.4 Console policy — always-on, no environment detection

The console channel is unconditional in every container, every environment. Stdout is the container-native channel (`docker compose logs`, `kubectl logs`); a container that only exports to a collector that may not exist is silent, which is the exact failure this library exists to kill. Conditional console emission is a silent fallback wearing a config flag. There is zero environment branching in Layer 0. The named trigger for revisiting is real production log-volume/cost pressure — and that is solved collector-side, never in-process.

**The mechanism is the BCL console provider, not OTel's console exporter.** OTel's console exporter is a debug-grade tool whose output is meant for neither humans nor scrapers. `AddServiceDefaults()` asserts the `Microsoft.Extensions.Logging.Console` provider explicitly — defensive posture, not diagnosis: host-builder defaults do include console (§1.1), but the shared root guarantees it rather than trusting whichever builder a future host reaches for — and the formatter stays selectable via standard `Logging:Console:FormatterName` config (SimpleConsole for humans locally, JsonConsole when a scraper wants structure). No bespoke knobs.

### 2.5 ActivitySource / Meter naming — per-assembly, wildcard subscription

Source and meter names are the emitting assembly's name, exactly (`Norse.Reference.Data`, `Norse.Infrastructure.Web.Server`) — the OTel ecosystem convention (source = instrumenting library). ServiceDefaults subscribes `AddSource("Norse.*")` / `AddMeter("Norse.*")` and never maintains a registry of realm names: each realm owns its source (smart about one thing), ServiceDefaults stays deliberately dumb. The assembly name is already brand-injected, so the name is derivable from `$(AssemblyName)` with zero hand-typed strings; a source-generator assist is a deferred observation, not a Layer 0 deliverable. No shared helper type ships in Layer 0 — realms construct their own `ActivitySource`/`Meter`.

### 2.6 Health — checks are registrations; reporters are per-container-shape

The seam is `Microsoft.Extensions.Diagnostics.HealthChecks` — pure `Microsoft.Extensions.*`, no ASP.NET anywhere in its dependency graph (the stock template's ASP.NET stench comes from the FrameworkReference of §2.1, not from `AddHealthChecks()`). Checks are registered host-neutrally by whichever layer owns the dependency; each container shape bolts on the reporter appropriate to it:

| Container | Checks (registered) | Reporter (consumes) | Arrives |
|---|---|---|---|
| Web.Server | self + DB/transport (Layers 1/3) | `MapDefaultEndpoints` (`/healthz` readiness + `/alive` liveness, Aspire-native outside polling) **+ gRPC health service (`Grpc.HealthCheck`, standard `grpc.health.v1.Health`)** — both legal here and only here among current containers: Web.Server already carries Kestrel and the gRPC transport; the attack-surface law bans listeners in headless containers, not health protocols in containers that already listen | Layer 2 |
| Stories.Server | self, **only, forever** — it touches no database and no transport (§1.3) | `MapDefaultEndpoints`, same endpoints — REST exposure only: no gRPC health (Stories carries no gRPC transport), no infrastructure checks (serves the WASM catalog; no database, no messaging) | Layer 2 |
| Worker | transport + DB checks | `IHealthCheckPublisher` → heartbeat file → exec-form AOT probe | Layer 3 (checks land with the transport) |
| Migrations Service | none | exit code | never — already done |

**Layer 0 ships the rail, not a reporter — and ships it uncomposed:** `AddDefaultHealthChecks()` = `AddHealthChecks()` plus the trivial `self` liveness check (tagged `live`, the tag Layer 2's `/alive` endpoint filters on). Layer 0 provides the method; later layers decide who rides. Emission (`AddServiceDefaults()`) never composes it — inert registration is still participation, and the Migrations Service's contract is *none*.

**Attack-surface law (hard):** no listener of any protocol enters a headless container for health reporting. HTTP is banned outright, and the gRPC health protocol is rejected for the same reason in a trench coat — there is no non-ASP.NET gRPC server in .NET (`Grpc.AspNetCore` and protobuf-net.Grpc's server host on Kestrel inside ASP.NET Core), so serving gRPC health from the Worker would put the exact FrameworkReference in the container the law bans. Health reporting in headless containers is **passive**: `IHealthCheckPublisher` (the standard in-box headless consumer) touches a heartbeat file on its timer; Docker `HEALTHCHECK`/K8s probes in **exec form** exec a tiny self-contained AOT binary that stats the file's freshness and exits — no shell required, which is what makes it chiseled-compatible. Binary heft is acceptable; listening surface is not. The probe binary opens no socket, loads no runtime, and runs only when the orchestrator execs it.

*Layer 3 note, recorded not solved:* if the probe binary offends when the messaging layer is designed, the named fallback is K8s process-liveness only, with transport/DB health flowing through OTel metrics — observability answering "is it healthy," the orchestrator answering only "is it alive." That choice belongs to the messaging layer's design. Also recorded for that design: the heartbeat file requires a writable path, which chiseled + `readOnlyRootFilesystem` does not provide — a tmpfs mount becomes part of the Worker's container contract, and the probe binary must be designed against that mount, not against a writable rootfs that won't exist.

*Layer 2 note, recorded not solved:* the stock template maps health endpoints in Development only, for good security reasons; the exposure policy for production-shaped environments is a Layer 2 decision.

### 2.7 The init container idiom — exit code is the contract

Run-to-completion with exit code is the standard init-container contract everywhere: Aspire's `WaitForCompletion` gates on exit code 0, compose's `service_completed_successfully` is literally that condition, K8s init containers/Jobs consume exit codes with backoff semantics. A failed migration exits nonzero → dependents never start → local and cloud both halt loudly until a human solves it — which is exactly what Bifröst's `AppHost.cs` already does. The considered alternative (stay alive and report healthy/unhealthy) is rejected: it makes a batch job impersonate a service, and every orchestrator fights that shape. No health machinery for the Migrations Service, ever.

### 2.8 Definition-of-done re-grounding — the AppHost dashboard, not compose

The brief's `docker compose up` acceptance gate does not match how Bifröst runs: no compose file exists anywhere in the tree, and the Aspire AppHost injects `OTEL_EXPORTER_OTLP_ENDPOINT` into project resources automatically — the dashboard comes free with `dotnet run`. Local verification is the Bifröst AppHost dashboard. The standalone-dashboard-as-compose-service story is real but belongs to the exporter jurisdiction (§2.2), deferred and unnamed with the rest of it.

Only two of the four containers are composed in `AppHost.cs` today (migrations, web). Layer 0's verification requires all four visible in one dashboard, so the Worker and Stories hosts are added to the AppHost — a Bifröst composition change on `master` per Bifröst CLAUDE.md §7 (its own tracked files, no branch).

---

## 3. Layer 0 specification

### 3.1 Project

`Midgard/src/Infrastructure.ServiceDefaults/` → assembly `Norse.Infrastructure.ServiceDefaults`. `internal sealed` by default; the public surface is the extension entry points only. One test project per package: `tests/Infrastructure.ServiceDefaults.Tests`.

Packages (direct `PackageReference`s in the csproj — CPM is Yggdrasil-only per house rules; stable OTel packages tag the major as `1.*`, framework-tracking `Microsoft.Extensions.*` packages tag `11.*-*`):

- `OpenTelemetry.Extensions.Hosting`
- `OpenTelemetry.Exporter.OpenTelemetryProtocol`
- `OpenTelemetry.Instrumentation.Runtime`
- `Microsoft.Extensions.Diagnostics.HealthChecks`

No FrameworkReference. Nothing else.

**`OpenTelemetry.Instrumentation.Process` is deliberately excluded.** It has lived in beta (0.5.x) for years, and what it measures — process CPU, memory, thread count — is precisely what the orchestrator and collector already observe from outside the process (cgroups, `docker stats`, kubelet/cAdvisor), which is where the platform's push-outward philosophy says that observation belongs. Runtime instrumentation (stable) covers the in-process signals only the process itself can see: GC, JIT, thread pool, exceptions. Named trigger to revisit: the package reaches stable **and** the dashboard shows a gap runtime instrumentation cannot fill. Pinning a beta to duplicate externally-visible signals fails the house prerelease test — it buys no capability.

### 3.2 API surface

All extensions target `IHostApplicationBuilder`, never `WebApplicationBuilder` — the only thing that lets the Worker and the Migrations Service call the exact same line as the web hosts.

- `AddServiceDefaults()` — the one line every container calls. Composes resource attributes, logging, tracing, metrics, and OTLP wiring — pure emission. Health registration is deliberately not composed here — see §2.6: registration is participation, and participation arrives with the layer that guarantees a consumer.
- `AddDefaultHealthChecks()` — `AddHealthChecks()` + the `self` liveness check tagged `live`. Public so later layers and product hosts can reach the rail directly; no Layer 0 container calls it — Layer 2 composes it for the web hosts, Layer 3 for the Worker, and the Migrations Service never does.

### 3.3 Resource attributes

Injected once, identical shape across all four containers:

| Attribute | Source |
|---|---|
| `service.name` | entry assembly name; `OTEL_SERVICE_NAME` overrides natively (no bespoke key) |
| `service.version` | entry assembly informational version |
| `service.instance.id` | per-process unique id |
| deployment-environment attribute | `IHostEnvironment.EnvironmentName` — **key per the pinned semconv revision**: semconv renamed `deployment.environment` → `deployment.environment.name`; which key the pinned resource builder emits is verified at implementation, not hard-coded here (the Aspire dashboard tolerates both) |

### 3.4 Logging

- BCL console provider asserted explicitly (§2.4), all environments.
- OTel `ILogger` provider with `IncludeFormattedMessage`, `IncludeScopes`, `ParseStateValues` enabled; OTLP is its export path.

### 3.5 Tracing

- `AddSource("Norse.*")` — realm code emits against per-assembly sources (§2.5).
- No EF, no ASP.NET, no HTTP, no messaging instrumentation — those are Layers 1–3.

### 3.6 Metrics

- `AddMeter("Norse.*")`.
- .NET runtime instrumentation (process-level metrics are observed from outside the process — §3.1).

### 3.7 OTLP export

`UseOtlpExporter()` — one call covering all three signals — gated on the presence of `OTEL_EXPORTER_OTLP_ENDPOINT`. **The guard is ours, not the SDK's:** `UseOtlpExporter()` called with no endpoint configured does not no-op — it defaults to `localhost:4317` and fails on export attempts, which is exactly why the stock template wraps it in an endpoint-presence `if`. The implementation carries that explicit `if`; only behind it does absence become a silent no-op. Standard OTel env vars only; console works either way. A container in a fresh environment with no collector must not crash.

---

## 4. Explicitly out of scope — the fast-follow layers

Enumerated so the dependency graph can respect them; not designed here:

- **Layer 1 — `.Persistence`.** EF Core tracing/metrics instrumentation, connection diagnostics, DB health checks. Consumers: Migrations Service, Web.Server, Worker. Never Stories.Server.
- **Layer 2 — `.AspNet`.** ASP.NET Core instrumentation, health-rail composition (`AddDefaultHealthChecks()` — Layer 0 ships it uncomposed, §2.6), `MapDefaultEndpoints` (`/healthz` + `/alive` per §2.6), the gRPC health service for Web.Server (`Grpc.HealthCheck` against the registered checks), request logging enrichment, production exposure policy. Consumers: Stories.Server, Web.Server. Carries the only FrameworkReference in the family; the Worker's inability to reference it is compiler-enforced.
- **Layer 3 — `.Messaging`.** Messaging instrumentation, trace-context propagation, transport health checks composed onto the rail alongside `AddDefaultHealthChecks()`, the Worker's heartbeat publisher + probe binary (§2.6), and all outbound HttpClient egress hygiene (`OpenTelemetry.Instrumentation.Http`, resilience, `ConfigureHttpClientDefaults`) — egress rides with messaging because the consumer set is identical (Web.Server, Worker).
- **Terminal — the gatekeeper.** All Identity/OpenIddict telemetry lives in Web.Server and only Web.Server. When Layer 2 is designed, resist any temptation to generalize auth instrumentation upward.
- **The exporter jurisdiction.** Collector pipeline, compose fragments, dashboards, retention, alerting — deferred and unnamed per §2.2.

---

## 5. Testing

TDD per house rules. The assertions that matter:

- Resource attributes carry the §3.3 shape for a host with no configuration.
- A span emitted on a fresh `ActivitySource("Norse.Test")` is captured by the wildcard subscription; a non-`Norse.` source is not.
- A meter named under `Norse.*` is captured by the wildcard subscription.
- A host with no `OTEL_EXPORTER_OTLP_ENDPOINT` builds and runs cleanly — OTLP absence is a no-op, not a crash.
- The console logging provider is registered after `AddServiceDefaults()`.
- `AddDefaultHealthChecks()` registers the `self` check tagged `live`.
- A host composed with only `AddServiceDefaults()` registers zero health-check registrations — emission does not imply participation.
- The package graph of `Infrastructure.ServiceDefaults` contains no `Microsoft.AspNetCore.*` — the Worker guarantee, asserted.

## 6. Definition of done

- `Infrastructure.ServiceDefaults` exists in Midgard, targets `IHostApplicationBuilder`, ships `AddServiceDefaults()` and `AddDefaultHealthChecks()`, tests green.
- All four Yggdrasil containers call `builder.AddServiceDefaults();` — one line each; Yggdrasil hosts but never implements.
- `AppHost.cs` composes all four containers; `dotnet run --project src/Orchestration.AppHost` shows structured console logs from all four, including the Migrations Service — first-class in the dashboard, not just an exit code.
- Traces, metrics, and logs from all four containers are visible in the Aspire dashboard; removing the OTLP endpoint breaks nothing.
- No container's dependency graph contains a package outside its column in the §1.3 matrix.

---

## Addendum (2026-07-28) — §1.1 case file: the mechanism of the misread

**Hypothesis: CONFIRMED — wrong-pane audit, not a mute pipeline.** Task 0 of the plan ran both the bare Migrations Service and the full AppHost dashboard to pin the mechanism; the evidence below matches §1.1's leading hypothesis exactly, with one strengthening finding the hypothesis didn't anticipate.

**Step 1 — bare run.** `dotnet run --project Yggdrasil/src/Hosting.Migrations.Service` (no AppHost, no injected connection string) crashed immediately:

```
Unhandled exception. System.InvalidOperationException: Connection string 'norse_identity' was not found.
   at Norse.Persistence.EntityFramework.Migrations.NorseMigrationContextExtensions.AddNorseMigrationContext[TContext](...)
   at NorseMigrationsGeneratedExtensions.AddNorseMigrations(IHostApplicationBuilder builder) in .../NorseMigrationsExtensions.g.cs:line 16
   at Program.<Main>$(String[] args) in .../Program.cs:line 3
```

`Program.cs` is three lines: `Host.CreateApplicationBuilder(args)` → `builder.AddNorseMigrations()` → `builder.Build().RunAsync()`. The throw happens inside `AddNorseMigrations()`, before `Build()` is ever called — so neither `Microsoft.Hosting.Lifetime` (fires on host start) nor any `MigrationRunnerService` `LoggerMessage` output (fires once a contributor runs) had a chance to emit. What *did* print was the BCL's default unhandled-exception handler writing the stack trace straight to stderr — console output was not absent, it just wasn't application-authored logging. This is a distinct, narrower failure mode than the bare-run case the spec's working hypothesis was written against (that hypothesis concerns a *successful* run reaching the OTLP-gated Structured-logs pane); it doesn't disconfirm anything, it just isn't the case Step 2 needed. Bare stdout was not empty — the halt condition in Task 0 Step 3 does not trigger.

**Step 2 — under the AppHost, both panes audited.** `dotnet run --project src/Orchestration.AppHost` (Postgres primary + replica up via Docker) ran the `migrations` resource to completion (dashboard state: **Finished**). The two panes:

- **Console logs pane (`/consolelogs/resource/migrations`) — fully populated, 70 lines.** In order: Aspire's own `WaitFor` orchestration lines, then `[sys] Starting process...`, then real application output — `info: Norse.Infrastructure.Migrations.MigrationRunnerService[...]` / "Starting migration contributor Norse.Identity", the same for `Norse.Reference`, full EF Core `Microsoft.EntityFrameworkCore.Database.Command` SQL text (`SELECT ... FROM "__EFMigrationsHistory"`, `CREATE TABLE IF NOT EXISTS ...`), `MigrationRunnerService` "... completed" lines for both contributors, `SeedRunnerService` start/complete lines for `Norse.Reference`, and finally `Microsoft.Hosting.Lifetime` — "Application started. Press Ctrl+C to shut down.", "Hosting environment: Production", "Content root path: ...", "Application is shutting down...". Every category Task 0 Step 1 asked about (`Microsoft.Hosting.Lifetime`, `MigrationRunnerService` `LoggerMessage` output) is present and legible.
- **Structured logs pane (`/structuredlogs/resource/migrations`) — empty.** "No structured logs found," 0 rows.

**Strengthening finding beyond the working hypothesis:** the Structured-logs emptiness is not scoped to `migrations`. `/structuredlogs` with no resource filter — i.e. "(All)" across every resource in the dashboard, including `web` (Hosting.Web.Server, which was also running) — likewise reports **0 structured logs, platform-wide**. This is exactly what §1.1/§2.7 already establish: Layer 0 (the thing this spec designs) hasn't shipped yet, so *no* container anywhere in the composition calls `UseOtlpExporter()` or otherwise exports OTLP — the Structured-logs pane has nothing to show for anyone, not just the Migrations Service. The original audit reading that pane empty and concluding the Migrations Service was silent generalizes from a platform-wide pre-Layer-0 fact to a per-container diagnosis; the per-container fact (console output was live and rich) sat one tab over the whole time.

**Verdict:** the gremlin was a wrong-pane audit. Changes nothing about the Layer 0 fix (always-on console + OTLP export, §2.4/§3.7, remain exactly correct) and confirms the case file: `MigrationRunnerService`'s `LoggerMessage`-authored logs were never missing, they were on the Console tab; the Structured tab was correctly empty for a platform that has no OTLP pipeline yet, not just for this one container.

Evidence captured 2026-07-28.
