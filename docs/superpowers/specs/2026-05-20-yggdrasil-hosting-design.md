# Yggdrasil Hosting — Plugin Runtime Design

**Date:** 2026-05-20
**Status:** Draft for review
**Owner:** Buvy
**Supersedes:** none
**Amended:** 2026-06-03 — CLAUDE.md §7 #2 resolved (NServiceBus). `IWebhookDispatcher` deleted in favor of direct `IMessageSession`; `.Server`/`.Worker` corrected to mutually invisible hard walls with shared types in `.Backend`; entities + EF configurations relocated to `.Worker`; webhook commands renamed with the `Command` suffix to satisfy the unobtrusive message conventions. See `2026-06-03-messaging-foundation-design.md` §12.
**Amended:** 2026-06-07 — the Egress spec (`2026-06-07-egress-http-resilience-parsing-design.md`) supersedes this spec's global-HttpClient-resilience model (§8, §9, rule #13). That model predates the 2026-06-07 egress POC and is stale: external/third-party HTTP now routes through the egress layer (`IHttpEgress` / `AddExternalApi`) with a *required* named resilience profile + per-partner classifier; the global `AddStandardResilienceHandler` default survives for **infrastructure** HttpClients only. Reconciliation tracker §5.1.
**Amended:** 2026-06-07 — webhook design (§4, §7.1, §11.2, rules #15/#16): (a) **verification is authentication, not per-command validation** — deleted `IWebhookValidator<TCommand>` in favor of three `WebhookSchemes` (client-credentials JWT, HMAC `Signature`, IP `Whitelist`), each a data-driven authentication handler that resolves the partner's OpenIddict `client_id` via `IWebhookClientResolver` (implemented by `Norse.Auth.Server`) and surfaces it as a claim; controllers declare their tier with one `[Authorize(AuthenticationSchemes = …)]` and the base reads the namespace uniformly (no frozen-claims mutation). (b) The partner's **`client_id` IS the UUID v5 namespace** for the synthesized idempotency key (discharges ruling 1.4's §7.1/§11.2 edit; minimal command — no header/URL/IP on the wire). (c) Added the base-class `TryHandleVerificationAsync` hook for provider subscription handshakes (Monday `challenge`, Slack `url_verification`, Meta `hub.challenge`) — the sole non-202 success path. (d) Noted the webhook→egress bridge (a handler calls a partner's API via the egress layer, in the worker, never the controller). Reconciliation tracker §5.7; auth-spec absorption §5.8.
**Companion specs:**
  - `2026-05-19-architecture-analyzers-design.md` — the plugin model is what §15.7 there assumed.
  - `2026-05-19-ui-composition-design.md` — this spec amends §2.2, §5, §9, §11.3 of that one (see §15 below).
  - `2026-05-20-auth-federation-design.md` — `AuthPlugin` is one consumer of this runtime; the auth spec's prereq table for Plan A is satisfied by what is specified here.
  - `2026-06-03-messaging-foundation-design.md` — resolves the messaging library (NServiceBus 10.2) and amends §4, §5, §7.1, §8, §11, and §13 of this spec.
  - `2026-06-07-egress-http-resilience-parsing-design.md` — supersedes this spec's global-HttpClient-resilience model; amends §8 (typed-client example + resilience note), §9 (HttpClient-defaults rows), and rule #13.

**Realm placement of the artifacts this spec introduces** (per CLAUDE.md §5's seven-realm split):
  - `Norse.Abstractions.Hosting` (declared law) — `IHostPlugin`, `IWebHostPlugin`, `IWorkerHostPlugin`, `IMigrationContributor`. The plugin contracts every host runtime consumes; context plugins implement these directly with no product-tier wrapper.
  - `Norse.Hosting.{Web|Worker|Migrations}` (connective tissue) — the concrete host runtimes that load plugins, plus `Norse.Hosting.ServiceDefaults` (Aspire defaults) and `Norse.Hosting.AppHost` (local-dev orchestrator). MGA-specific cross-cutting (audit, `NorsePrincipal` flow) is platform middleware configured here, not interface extensions. *(Tenancy was originally listed here; removed 2026-06-03 — there is no runtime tenancy under stamp-per-tenant, see `2026-06-03-tenancy-model-design.md`.)*

**Downstream consumers (specs not yet written, will reference this one):**
  - `norse-infrastructure-persistence` — defines the per-service DbContext family and snake_case/MaxLength conventions. *(Superseded in detail by the persistence spec: plugins do not call `AddDbContext` — Infrastructure owns DbContext registration and scans `.Worker` assemblies for `IEntityTypeConfiguration<T>` impls; see §6 "No DbContext registration in plugins.")*
  - `norse-infrastructure-api` — defines `JsonControllerBase<TService>`; the visibility semantics of inheriting it are settled here.

---

## 1. Motivation

Norse is composed at runtime from a fixed catalog of bounded-context plugins inside two long-lived deployables (`Norse.Hosting.Web.Server` for web/gRPC, `Norse.Hosting.Worker` for background work — the second is optional and only used when a workload's resource profile justifies splitting from the web process). CLAUDE.md §4 and the architecture-analyzers spec §15.7 commit to this single-deployable model; this spec specifies the plugin contract that makes it work.

A platform that does plugin composition badly accumulates ceremony fast: per-plugin assembly-discovery, per-plugin lifecycle ordering quirks, per-plugin "remember to call this method" footguns. Norse's hosting layer is designed against the opposite pole — **two methods, total, on the plugin interface; explicit `AddPlugin<T>` registration in `Program.cs`; no reflection, no convention scanning, no startup-time discovery magic.** A new context's plugin is mechanical to write and mechanical to register. There is one right place for everything, and the XML docs on the interface methods name the alternatives for the common "I think I need…" instincts.

The hosting layer is also the home of the migrations service — a third deployable distinct from web and worker, sharing the same explicit-registration aesthetic but with a different runtime contract (one-shot work, long-lived process, health-signal-gated downstream). All three deployables sit on top of `Norse.Hosting.ServiceDefaults` (the Aspire-derived cross-cutting shared by every host) and reference the hosting libraries specified here.

## 2. Scope and Non-Goals

### In scope (this spec)

- The plugin interface family: `IHostPlugin`, `IWebHostPlugin`, `IWorkerHostPlugin`, `IMigrationContributor` — declared in `Norse.Abstractions.Hosting` (Abstractions-tier law).
- Plugin registration model: explicit `AddPlugin<T>` / `AddMigration<T>`, builder semantics, ordering rules.
- Runtime libraries (Norse-tier connective tissue): `Norse.Hosting.Web`, `Norse.Hosting.Worker`, `Norse.Hosting.Migrations.Service`. The plugin-contract package is `Norse.Abstractions.Hosting`, shipped from the `norse-abstractions-hosting` submodule and consumed by every concrete host.
- The codified Stage 1 → Stage 2 → Stage 3 lifecycle (Blazor Server → gRPC → optional JSON publication).
- The visibility model for the partner-facing OpenAPI document (one document; `JsonControllerBase<TService>` inheritance is the inclusion signal).
- Cross-cutting platform setup that `AddNorseWebHost` / `AddNorseWorkerHost` / `AddNorseMigrationsHost` provide.
- The migrations service runtime contract (long-running process, never exits non-zero, health-signal-driven readiness gating).
- Aspire wiring conventions for the local-dev orchestrator (`Norse.Hosting.AppHost`).

### Out of scope (separate specs)

- **`JsonControllerBase<TService>` implementation** — defined in the future `norse-infrastructure-api` spec (Infrastructure-tier concrete infrastructure). The semantics ("inheriting this base class includes the controller in the partner-facing OpenAPI document") are settled here; the HOF wrapper internals (error mapping, cancellation, ProblemDetails shape) are the api spec's territory.
- **`InfrastructureDbContext` base and EF Core conventions** — defined in the `norse-infrastructure-persistence` spec (Infrastructure-tier concrete infrastructure). Plugins do not register DbContexts (§6 "No DbContext registration in plugins"); the persistence spec owns the DbContext family and conventions.
- **MGA-specific cross-cutting middleware** — `NorsePrincipal` flow, audit publication. *(Tenancy claim handling was listed here originally; removed 2026-06-03 — no tenancy claim exists under stamp-per-tenant, see `2026-06-03-tenancy-model-design.md`.)* These are platform middleware configured at the Norse host-runtime level (in `Norse.Hosting.Web`'s standard pipeline), not interface extensions context plugins implement. Their detailed shape belongs to a follow-on spec under the product umbrella; context plugins implement `Norse.Abstractions.Hosting.IWebHostPlugin` / `IWorkerHostPlugin` directly.
- **Client-side hosting (`Norse.Hosting.Web.Client`)** — shared cross-cutting for `Norse.Hosting.Web.Client` / `Norse.Hosting.App` (auth state, gRPC-Web channel options, telemetry). Mentioned here for completeness; specified when the client deployables get their own spec. There are deliberately no client-side plugin interfaces (see §4 → "No client-side plugin variants").
- **Production Kubernetes deployment manifests** — the migrations binary's behavior is settled here; the choice of init container vs. sidecar vs. separate deployment is operations territory.
- **Source-generated mediator, Roslyn analyzers, etc.** — referenced by the architecture-analyzers and other specs; not concerns of this one.

## 3. Architecture Overview

Four packages ship from this spec, plus two more that are referenced and defined elsewhere:

```
┌──────────────────────────────────────────────────────────────────────────┐
│                          Norse.Abstractions.Hosting (NuGet)                          │
│                       — declared law (Abstractions realm)                      │
│                                                                          │
│  IHostPlugin              ← base contract: ConfigureServices             │
│  IWebHostPlugin           ← + MapEndpoints (gRPC services)               │
│  IWorkerHostPlugin        ← marker for worker-host inclusion             │
│  IMigrationContributor    ← the migrations contract (separate lifecycle) │
│  EfCoreMigrationContributor<TContext>  ← convenience base                │
└──────────────────────────────────────────────────────────────────────────┘
            ▲                          ▲                           ▲
            │ implemented by           │                           │
            │                          │                           │
┌───────────┴──────────┐  ┌────────────┴────────────┐  ┌───────────┴─────────┐
│  Norse.Hosting.Web │  │  Norse.Hosting.Worker │  │ Norse.Hosting.        │
│  — connective tissue │  │  — connective tissue    │  │ Migrations          │
│                      │  │                         │  │  — connective tissue│
│  AddNorseWebHost   │  │  AddNorseWorkerHost   │  │                     │
│  AddPlugin<T>        │  │  AddPlugin<T>           │  │ AddNorseMigra-    │
│  UseNorseWebHost   │  │  UseNorseWorkerHost   │  │ tionsHost           │
│                      │  │                         │  │ AddMigration<T>     │
│  Cross-cutting:      │  │  Cross-cutting:         │  │                     │
│  - gRPC server       │  │  - BackgroundService    │  │ Long-running        │
│  - Controllers       │  │    runtime              │  │ orchestrator;       │
│  - One OpenAPI doc   │  │  - IOptions validation  │  │ /health-driven;     │
│  - Auth/Authz hooks  │  │                         │  │ never exits 1.      │
│  - ProblemDetails    │  │                         │  │                     │
│  - IOptions validate │  │                         │  │                     │
└──────────────────────┘  └─────────────────────────┘  └─────────────────────┘
            ▲                          ▲                           ▲
            │ consumed by              │ consumed by               │ consumed by
            │                          │                           │
┌───────────┴──────────┐  ┌────────────┴────────────┐  ┌───────────┴─────────┐
│  Norse.Hosting.Web.Server        │  │  Norse.Hosting.Worker     │  │  Norse.Hosting.Migrations.Service │
│  (the deployable)    │  │  (optional deployable)  │  │  (the deployable)   │
│  ~10 lines Program   │  │  ~10 lines Program      │  │  ~10 lines Program  │
└──────────────────────┘  └─────────────────────────┘  └─────────────────────┘

            All three deployables also reference Norse.Hosting.ServiceDefaults
            (Aspire defaults: OTEL, service discovery, HttpClient resilience,
            health checks /health and /alive, default logging).
```

**Layer / Context attributes** (per architecture-analyzers spec §3–§4):

| Project | Realm | Layer | Context |
|---|---|---|---|
| `Norse.Abstractions.Hosting` | Abstractions (declared law) | `Abstractions` | `Hosting` |
| `Norse.Hosting.Web` | Norse (connective tissue) | `Infrastructure` | `Hosting` |
| `Norse.Hosting.Worker` | Norse | `Infrastructure` | `Hosting` |
| `Norse.Hosting.Migrations.Service` | Norse | `Infrastructure` | `Hosting` |
| `Norse.Hosting.ServiceDefaults` | Norse | `Infrastructure` | `Hosting` |

No product-tier hosting-abstractions row — context plugins (`{Company}.{Context}.Server` / `{Company}.{Context}.Worker`) implement the Abstractions contracts directly.

## 4. The Codified Lifecycle Rule

The plugin model exists to support a specific three-stage component lifecycle. Stating it up front so the rest of this spec reads as enforcement of one coherent rule:

> **Stage 1 — Inception.** Components in `{Company}.{Context}.Components` inject `I{Context}Api`. The host (`Norse.Hosting.DevServer` in local dev) registers an in-process adapter that implements the interface and calls into the Application layer directly. Blazor Server, single process, no serialization. Iterate the contract until it stops churning.
>
> **Stage 2 — Crystallization.** Pair the C# interface with a `.proto` file (LLM-assisted authoring). `Norse.Hosting.Web.Server` exposes the service via `Grpc.AspNetCore` on the server. WASM and MAUI clients consume the service via gRPC-Web using `Grpc.Net.Client.Web` + `Google.Protobuf` — native Microsoft stack, AOT-clean by codegen. Cross-context callers and operators (Postman + reflection in dev/staging; generated clients in scripts) consume gRPC directly. gRPC reflection is on in `Development` and `Staging`, off in `Production`. This is the production posture for the vast majority of services.
>
> **Stage 3 — Publication (additive, optional).** Only when a 3rd party needs the service and cannot speak gRPC: add a controller in `{Company}.{Context}.JsonApi` that inherits `JsonControllerBase<I{Context}Api>`. The same service POCO is wrapped by both the gRPC adapter and the JSON controller — two front doors, one service. Inheriting `JsonControllerBase<T>` **IS** the act of declaring partner-facing intent; the controller appears in the partner-facing OpenAPI document automatically. No attribute, no second registration step, no route group to pick. Most services never reach Stage 3.

**No client-side plugin variants.** WASM and MAUI clients are composed via source-generated extension methods (`Add{Company}{Context}Client(apiBase)` from the UI Composition spec's generator, extended per §15 here to register native gRPC-Web clients). Per-context client contributions are exactly two lines (UI registration + gRPC-Web client), and source generation is the right tool at that scale. The Hosting interface family has no `IWasmHostPlugin` or `IMauiHostPlugin`.

**`protobuf-net.Grpc` is rejected.** Its "C# interface IS the proto" ergonomics are nice, but its AOT roadmap is uncertain and Microsoft is investing in the native stack. The .proto-authoring friction is a tooling problem (LLM assistance solves it), not an architectural one.

**No JSON on WASM/MAUI clients.** Those platforms were built for binary transports; sending JSON over the wire is a category error there. The only JSON paths in the platform are server-side, all driven by external constraints we do not control:

| Path | Why JSON | Pattern |
|---|---|---|
| Partner integrations that can't speak gRPC | Partner technology choice | `JsonControllerBase<TService>` controllers, attribute-routed |
| Webhooks (Stripe, Monday, fronting-carrier callbacks, …) | Third party hits us; they own the request shape | `[ApiController] : ControllerBase`, attribute-routed |
| OAuth/OIDC discovery + token endpoints | RFC 8414 / OIDC Discovery 1.0 mandate JSON | OpenIddict middleware registered by `AuthPlugin.ConfigureServices` |

**Everything else is gRPC.**

**Webhook handling — auth-then-dispatch.** Inbound webhook controllers follow a uniform three-step pattern that is non-negotiable; deviations require a documented reason.

1. **Authenticate — and establish the partner's identity.** *(Amended 2026-06-07.)* One of three authentication schemes runs per the partner's capability tier (§7.1.1): client-credentials JWT (preferred), HMAC signature, or source-IP allowlist. The scheme verifies the caller *and* surfaces the partner's OpenIddict `client_id` as a claim — the non-JWT handlers resolve it from the `{partnerCode}` route segment via the OpenIddict client store (`IWebhookClientResolver`). Authentication failure returns bare `401` / `403` immediately; no redirect, no other work.
2. **Capture the raw request body and dispatch to the message bus.** The controller does NOT deserialize the payload. It captures the request body as a `byte[]`, wraps it in a `{Source}WebhookReceivedCommand` — minimal by ruling 1.4: raw bytes + a **synthesized `Guid` idempotency key** (UUID v5 over the partner's OpenIddict `client_id` as namespace + SHA-256 of the body, via the SequentialGuid v5 generator — never a raw header string, YGG101-clean) + received timestamp; the `Command` suffix is mandatory (the unobtrusive message conventions classify by it) — and dispatches to the messaging infrastructure. No headers, URL, or IP ride the wire. Webhook commands are server→worker commands and live in `{Company}.{Context}.Backend`.
3. **Return `202 Accepted` immediately.** The webhook sender sees successful delivery. Median webhook timeouts are short (Stripe's is 30 seconds before retry); this pattern keeps the controller's work well under that bound regardless of downstream complexity. *(Amended 2026-06-07.)* The **sole** non-202 success path is a provider subscription-verification handshake (Monday `challenge`, Slack `url_verification`, Meta `hub.challenge`), answered synchronously via the base class's `TryHandleVerificationAsync` hook — it runs after `[Authorize]` authz but before the signature validator (a setup handshake carries no event signature yet) and parses the already-captured bytes (no stream re-read). **Egress bridge:** if processing a webhook requires calling the partner's API back (e.g. fetching the full Monday item), that outbound call happens in the *worker handler* via the egress layer (`IHttpEgress`, `2026-06-07-egress-http-resilience-parsing-design.md`) — at the queue, never in the controller. The controller is 202-and-done; ingress and egress meet only in the worker.

The handler — on the context's durable NServiceBus worker endpoint (`{company}.{context}`), riding in `Norse.Hosting.Web.Server` in monolith mode or in `Norse.Hosting.Worker` (split deployment) — picks up the command, deserializes the bytes into the expected payload shape, runs business logic, enforces idempotency via the captured key. Deserialization failures, schema mismatches, business-logic exceptions all route through the platform recoverability policy (immediate + delayed retries) to the `error` queue. **The webhook sender is unaware of any of it** — they delivered successfully. Operations sees error-queue accumulation in ServicePulse and reacts there (inspect, edit, retry).

This shape buys four things:

- **Sender-perceived delivery is decoupled from successful processing.** A bug in our payload-handling code does NOT trigger webhook retries; retries on our bug would multiply the bug's blast radius via duplicated processing.
- **Idempotency lives in one place** — the worker side, downstream of the queue. The base controller synthesizes the deterministic `Guid` key (UUID v5 over the partner's OpenIddict `client_id` + SHA-256(body), ruling 1.4) and stamps it on the command; the handler enforces it once. Because the key derives from what the partner actually *sent*, the same key also drives the messaging spec's deterministic `MessageId`, so broker-level dedup of replayed deliveries falls out for free. No duplicate enforcement logic in the HTTP layer.
- **The HTTP layer is schema-agnostic.** Stripe changes their webhook payload shape next quarter — we don't redeploy the HTTP layer; only the worker-side deserializer changes.
- **Replayable audit trail.** The webhook command on the queue (with raw bytes) is durable, and every processed message flows to the `audit` queue. If a worker-side bug ate a message, we retry it from ServicePulse.

*(Amended 2026-06-03.)* CLAUDE.md §7 #2 is resolved: NServiceBus (see `2026-06-03-messaging-foundation-design.md`). Webhook controllers dispatch via NServiceBus's **`IMessageSession`** directly. The interim library-agnostic `IWebhookDispatcher` abstraction is deleted — its only motive was reversibility, and §2.5 says an abstraction whose motive is gone goes with it. The test seam is `NServiceBus.Testing`'s `TestableMessageSession`. The worked example in §11 reflects this.

## 5. The Plugin Interface Family

Three interfaces in `Norse.Abstractions.Hosting`, plus a separate migrations contract.

```csharp
namespace Norse.Abstractions.Hosting;

/// <summary>
/// Base plugin contract. Plugins contribute everything they need to the application's
/// service graph during the builder phase: DI registrations, HttpClient registrations,
/// authentication schemes, authorization policies, IOptions bindings,
/// BackgroundService registrations, controller application parts — anything
/// that goes on IServiceCollection. (NOT DbContext registration — Norse.Infrastructure.Persistence
/// owns the DbContext family; see "No DbContext registration in plugins" in §6.)
///
/// <para>Plugins are parameterless POCOs. The platform instantiates them via
/// <c>new TPlugin()</c>; they have no own DI dependencies. Their behavior is the
/// methods on this interface, not the constructor.</para>
///
/// <para>Configuration convention: bind options classes with
/// <c>services.AddOptions&lt;TOptions&gt;().BindConfiguration("…").ValidateDataAnnotations().ValidateOnStart()</c>.
/// The host's standard startup-validator infrastructure runs every registered
/// validator during initialization; misconfiguration fails the host immediately,
/// before any request is served.</para>
/// </summary>
public interface IHostPlugin
{
  void ConfigureServices(
    IServiceCollection services,
    IHostEnvironment environment,
    IConfiguration configuration);
}

/// <summary>
/// Plugin variant for web hosts. Adds the gRPC service mapping phase.
/// </summary>
public interface IWebHostPlugin : IHostPlugin
{
  /// <summary>
  /// Map this plugin's gRPC service implementations.
  /// <c>endpoints.MapGrpcService&lt;BillingService&gt;().RequireAuthorization(policy)</c>
  /// is the canonical line. Map one gRPC service per published <c>I{Context}Api</c>
  /// interface; chain <c>RequireAuthorization</c> to attach the service's authorization
  /// policy. gRPC reflection is enabled in Development and Staging so operators can
  /// reach the service from Postman; off in Production.
  ///
  /// <para>This method is for gRPC services only. Other endpoint shapes have other
  /// homes:</para>
  /// <list type="bullet">
  ///   <item>Webhook controllers (<see cref="ControllerBase"/> / <c>[ApiController]</c>):
  ///   opt the controller assembly in via
  ///   <c>services.AddControllers().AddApplicationPart(…)</c> in
  ///   <see cref="IHostPlugin.ConfigureServices"/>. They are attribute-routed by
  ///   the host's global <c>app.MapControllers()</c> call.</item>
  ///   <item>Partner JSON controllers (<see cref="Norse.Infrastructure.Api.JsonControllerBase{TService}"/>):
  ///   same opt-in path as webhooks. Inheriting <c>JsonControllerBase&lt;T&gt;</c> IS
  ///   the act of declaring partner-facing intent — the controller appears in the
  ///   partner OpenAPI document automatically.</item>
  ///   <item>OAuth/OIDC endpoints: registered by the OpenIddict middleware that
  ///   <c>AuthPlugin.ConfigureServices</c> wires up. Never mapped here.</item>
  ///   <item>Health checks: handled by <c>Norse.Hosting.ServiceDefaults</c>. Never mapped here.</item>
  /// </list>
  ///
  /// <para>If you find yourself reaching for <c>endpoints.MapPost(…)</c> or
  /// <c>endpoints.MapGet(…)</c> here, stop. Either it's a gRPC service method that
  /// belongs on an <c>I{Context}Api</c>, or it's a controller that belongs in a
  /// <c>JsonApi</c> assembly, or it's a platform concern that should be raised with
  /// the platform team. Plugin minimal API is not a supported pattern.</para>
  /// </summary>
  void MapEndpoints(IEndpointRouteBuilder endpoints);
}

/// <summary>
/// Plugin variant for worker hosts. Has no method of its own — <see cref="BackgroundService"/>
/// implementations are registered via <see cref="IHostPlugin.ConfigureServices"/> like any
/// other service. The interface is a discriminator so the worker host can load only the
/// plugins relevant to it (a plugin that implements both <see cref="IWebHostPlugin"/> and
/// <see cref="IWorkerHostPlugin"/> on a single class is normal and expected).
/// </summary>
public interface IWorkerHostPlugin : IHostPlugin
{
}
```

**Plugins split by deployable destination.** A context with server-side concerns ships `internal sealed class BillingPlugin : IWebHostPlugin` in `{Company}.Billing.Server`. A context with background work also ships `internal sealed class BillingWorkerPlugin : IWorkerHostPlugin` in `{Company}.Billing.Worker`. *(Amended 2026-06-03 — this paragraph previously said `.Worker` references `.Server`; that was wrong.)* **`.Server` and `.Worker` are mutually invisible — hard walls.** Neither references the other; both reference `{Company}.Billing.Backend` (server→worker commands, Mongo document records, shared server-side types) and `.Contracts`. The worker never references ASP.NET Core; the server never references EF Core or entity types. `Norse.Hosting.Web.Server` loads both kinds in monolith mode; `Norse.Hosting.Worker` (when used) loads only the `WorkerPlugin` classes. The two classes are fully independent; the two halves meet only at the queue.

**Plugins are `internal sealed`.** CLAUDE.md §2.3 default. They are referenced explicitly by type in `Norse.Hosting.Web.Server`'s `Program.cs`, which has visibility into the plugin types through ordinary `<ProjectReference>` items in `Norse.Hosting.Web.Server.csproj`.

## 6. Plugin Registration and Discovery

Explicit, compile-time, no scanning. CLAUDE.md §8 forbids convention scanning for handlers; the same rule applies here.

```csharp
namespace Norse.Hosting.Web;

public static class NorseWebHostBuilderExtensions
{
  /// <summary>
  /// Adds the Norse web-host runtime to the builder. After this call, plugins can be
  /// registered via <c>AddPlugin&lt;T&gt;()</c>. Call once, early in Program.cs.
  /// </summary>
  public static INorseWebHostBuilder AddNorseWebHost(this WebApplicationBuilder builder);
}

public interface INorseWebHostBuilder
{
  WebApplicationBuilder Builder { get; }

  /// <summary>
  /// Registers a plugin with the host. The plugin is instantiated via <c>new TPlugin()</c>
  /// and its <c>ConfigureServices</c> method is called immediately.
  ///
  /// <para>Plugin order matters in two ways: (1) cross-cutting plugins (Auth, telemetry)
  /// should register first so their authentication schemes and global policies are in
  /// place when business plugins register their resources; (2) <c>MapEndpoints</c> is
  /// invoked in registration order, which determines route registration order — routes
  /// declared earlier win conflicts.</para>
  /// </summary>
  INorseWebHostBuilder AddPlugin<TPlugin>() where TPlugin : IWebHostPlugin, new();
}

public static class NorseWebApplicationExtensions
{
  /// <summary>
  /// Finalizes the host pipeline: applies default middleware (routing, exception handler,
  /// authentication, authorization, OpenAPI doc endpoint, telemetry), iterates registered
  /// plugins to call <c>MapEndpoints</c>, calls <c>app.MapControllers()</c> once globally,
  /// returns the configured app.
  /// </summary>
  public static WebApplication UseNorseWebHost(this WebApplication app);
}
```

`Norse.Hosting.Worker` mirrors this shape — `AddNorseWorkerHost(HostApplicationBuilder)`, `AddPlugin<TPlugin>() where TPlugin : IWorkerHostPlugin, new()`, `UseNorseWorkerHost(IHost)`. No `MapEndpoints` phase; worker hosts have no HTTP surface beyond the standard health endpoints inherited from `Norse.Hosting.ServiceDefaults`.

## 7. `MapEndpoints` — gRPC-Only Surface and Visibility Model

**One method, one job.** Plugins call `endpoints.MapGrpcService<TService>()` once per published gRPC service, optionally chaining `.RequireAuthorization("policy-name")`. Nothing else belongs here.

**The visibility model for the partner OpenAPI document is trivial:**

| Surface | In the partner OpenAPI doc? | Why |
|---|---|---|
| Controllers inheriting `Norse.Infrastructure.Api.JsonControllerBase<TService>` | **Yes** | Inheriting the base class IS the partner-facing declaration |
| Controllers inheriting `ControllerBase` (webhooks) | No | Filtered out by document transformer; webhooks aren't partner API surface |
| gRPC services | No | gRPC has its own discovery (reflection in dev/staging; .proto in Contracts package for partners) |
| OAuth/OIDC endpoints registered by OpenIddict | No | Partners discover via the well-known paths (`/.well-known/openid-configuration`), not OpenAPI |
| Health endpoints from ServiceDefaults | No | Operational, not partner-facing |

There is **one** OpenAPI document. There is no "internal OpenAPI document" because there are no internal JSON surfaces to document (operational endpoints are gRPC; webhooks are inbound contracts owned by third parties).

**The act of writing a `JsonControllerBase<T>` is the explicit declaration of partner intent.** Adding a partner endpoint requires creating a new file in `{Company}.{Context}.JsonApi`. A developer cannot accidentally publish an endpoint by forgetting an attribute or misnaming a route group — the controller's existence in `JsonApi` is the affirmative declaration.

### 7.1 Webhook Controller Base Class

The auth-then-dispatch convention from §4 is implemented once in a shared abstract base class. The partner's identity is established by an **authentication scheme** — one per capability tier (§7.1.1) — so by the time the action runs, the validated principal already carries the partner's OpenIddict `client_id`. The contracts live in `Norse.Abstractions.Hosting` (declared law); the `WebhookControllerBase<TCommand>` abstract class — concrete infrastructure binding the contracts to ASP.NET Core MVC — lives in `Norse.Hosting.Web` (connective tissue, server-side host runtime). Every webhook controller inherits from it; the boilerplate (raw-bytes capture, handshake answer, key synthesis, dispatch, `202` return) is centralized. Concrete webhook controllers are minimal — one `[Authorize(AuthenticationSchemes = …)]` attribute declaring the tier, the `[Route]`, the generic-parameter command type, and a one-line `BuildCommand`.

**The contracts** (live in `Norse.Abstractions.Hosting`):

```csharp
namespace Norse.Abstractions.Hosting.Webhooks;

/// <summary>
/// Marker interface for webhook commands dispatched to the message bus.
/// Implementations are concrete record types per webhook source; all carry
/// the raw payload bytes, a deterministic idempotency key, and the time the
/// platform received the request. Minimal by design (ruling 1.4): no headers,
/// URL, or IP ride the wire — those are consumed at ingress (auth) or captured
/// by Norse.ReferenceData.Audit, never shipped into the domain message.
/// </summary>
public interface IWebhookCommand
{
  byte[] Bytes { get; }
  // Synthesized, never carried: UUID v5 over (partner namespace, SHA-256(Bytes))
  // via the SequentialGuid v5 generator (ruling 1.4). YGG101-clean — a Guid is legal
  // on a message type; a raw header string is not.
  Guid IdempotencyKey { get; }
  DateTimeOffset ReceivedAt { get; }
}

/// <summary>
/// The webhook authentication schemes — one per partner capability tier (§7.1.1). A controller
/// declares its tier with a single [Authorize(AuthenticationSchemes = ...)] attribute. Each
/// scheme's handler establishes the caller's identity and surfaces the partner's OpenIddict
/// client_id as the <see cref="ClientIdClaim"/> — so the controller reads the idempotency
/// namespace uniformly across tiers, with no post-auth claim mutation (claims freeze once
/// authentication completes; resolving inside the handler is the one place the principal is
/// still being built).
/// </summary>
public static class WebhookSchemes
{
  public const string ClientCredentials = "Webhook.ClientCredentials"; // JWT bearer (OpenIddict) — preferred
  public const string Signature         = "Webhook.Signature";         // HMAC over the raw body
  public const string Whitelist         = "Webhook.Whitelist";         // source-IP allowlist

  // The claim every scheme emits, matching OpenIddict's client_id claim so the JWT tier needs
  // no special handling. Its value is the partner's Guid client_id — the UUID v5 namespace.
  public const string ClientIdClaim = "client_id";
}

/// <summary>
/// Resolves a webhook partner from its route partner-code to the OpenIddict client identity the
/// non-JWT authentication handlers need. Implemented in Norse.Auth over the OpenIddict
/// application store (the identity system of record); consumed by the Norse webhook auth
/// handlers. Returns null when no client matches — the handler then fails authentication (401).
/// </summary>
public interface IWebhookClientResolver
{
  Task<WebhookClient?> FindByPartnerCodeAsync(string partnerCode, CancellationToken cancellationToken);
}

/// <summary>
/// A resolved webhook partner. <see cref="ClientId"/> is a Guid (partner-client registration
/// convention) and doubles as the UUID v5 namespace for idempotency-key synthesis. Verification
/// material is stored as OpenIddict application properties — NOT the hashed client_secret, which
/// is unrecoverable and serves only the client-credentials token flow. The signing secret is an
/// EncryptedString (it is a secret); the allowlist is empty for non-whitelist tiers.
/// </summary>
public readonly record struct WebhookClient(
  Guid ClientId,
  EncryptedString? SigningSecret,
  IReadOnlyList<IPNetwork> IpAllowlist);

// (Amended 2026-06-03: the IWebhookDispatcher abstraction that previously sat here
// is deleted. Controllers dispatch via NServiceBus's IMessageSession directly —
// the per-context session resolved from the endpoint's keyed registration, so
// Billing code can only see Billing's session. Test seam: TestableMessageSession.)

/// <summary>
/// Abstract base class for webhook controllers. The partner's identity is established by the
/// authentication scheme (one per capability tier — see <see cref="WebhookSchemes"/>); by the
/// time the action runs the validated principal carries the partner's OpenIddict client_id. The
/// base captures the raw body once, answers any provider verification handshake, synthesizes the
/// deterministic idempotency key from the client_id namespace, sends the command to the context's
/// durable worker endpoint via <see cref="IMessageSession"/>, and returns 202 Accepted.
///
/// <para>Subclasses declare their tier with [Authorize(AuthenticationSchemes = ...)], provide a
/// one-line <see cref="BuildCommand"/>, and — if the provider requires it — override
/// <see cref="TryHandleVerificationAsync"/>.</para>
///
/// <para>Authentication failure is handled by the scheme (bare 401/403, never a redirect) before
/// the action runs — there is no validator branch here. The ONLY non-202 success path is a
/// verification handshake (below).</para>
/// </summary>
public abstract class WebhookControllerBase<TCommand>(IMessageSession session)
  : ControllerBase
  where TCommand : IWebhookCommand
{
  [HttpPost]
  public async Task<IActionResult> Receive(CancellationToken ct)
  {
    // Raw bytes captured ONCE. The signature scheme already read the buffered body to verify the
    // HMAC, so reset before re-reading. Subclasses parse these bytes — never the stream.
    if (Request.Body.CanSeek) Request.Body.Position = 0;
    using var ms = new MemoryStream();
    await Request.Body.CopyToAsync(ms, ct);
    var bytes = ms.ToArray();

    // Provider subscription-verification handshake (Monday challenge, Slack url_verification,
    // Meta hub.challenge). Parses the captured bytes; runs before dispatch. Default: no handshake.
    if (await TryHandleVerificationAsync(bytes, Request, ct) is { } handshake)
      return handshake;

    // Namespace = the partner's OpenIddict client_id, surfaced as a claim by the auth scheme.
    var clientId = Guid.Parse(User.FindFirstValue(WebhookSchemes.ClientIdClaim)!);
    var key = WebhookKey.Synthesize(clientId, bytes);   // UUID v5(client_id, SHA-256(body)) — ruling 1.4
    var command = BuildCommand(bytes, key, DateTimeOffset.UtcNow);
    await session.Send(command, ct);
    return Accepted();
  }

  /// <summary>Answer a provider's subscription-verification handshake synchronously. Return a
  /// result to short-circuit (e.g. <c>Ok(new { challenge })</c>); return null to proceed to
  /// dispatch + 202. Default: null. Parse the already-captured <paramref name="body"/> — never
  /// re-read the request stream.</summary>
  protected virtual ValueTask<IActionResult?> TryHandleVerificationAsync(
    byte[] body, HttpRequest request, CancellationToken ct) =>
    ValueTask.FromResult<IActionResult?>(null);

  /// <summary>Map the captured body + synthesized key + receipt time to the typed command.
  /// Minimal by ruling 1.4 — nothing else from the request rides the wire. Usually one line.</summary>
  protected abstract TCommand BuildCommand(byte[] body, Guid idempotencyKey, DateTimeOffset receivedAt);
}
```

**Concrete controller — signature tier** (Stripe; ~3 lines beyond declaration):

```csharp
[ApiController]
[Route("webhooks/{partnerCode}/stripe")]
[Authorize(AuthenticationSchemes = WebhookSchemes.Signature)]   // HMAC; handler resolves the client by {partnerCode}
internal sealed class StripeWebhookController(IMessageSession session)
  : WebhookControllerBase<StripeWebhookReceivedCommand>(session)
{
  protected override StripeWebhookReceivedCommand BuildCommand(byte[] body, Guid key, DateTimeOffset at) =>
    new(body, key, at);
}
```

**Concrete controller — whitelist tier with a verification handshake** (Monday):

```csharp
[ApiController]
[Route("webhooks/{partnerCode}/monday/events")]
[Authorize(AuthenticationSchemes = WebhookSchemes.Whitelist)]   // source-IP; handler resolves the client by {partnerCode}
internal sealed partial class MondayWebhookController(IMessageSession session)
  : WebhookControllerBase<MondayWebhookReceivedCommand>(session)
{
  // Monday's subscription setup POSTs {"challenge":"..."} and expects it echoed at 200.
  // Parses the bytes the base already captured — no EnableBuffering, no stream re-read.
  protected override ValueTask<IActionResult?> TryHandleVerificationAsync(
    byte[] body, HttpRequest req, CancellationToken ct)
  {
    using var doc = JsonDocument.Parse(body);
    if (doc.RootElement.TryGetProperty("challenge", out var challenge))
    {
      MondayChallengeRequested(challenge.GetString()!);
      return ValueTask.FromResult<IActionResult?>(Ok(new { challenge = challenge.GetString() }));
    }
    return ValueTask.FromResult<IActionResult?>(null);  // not a handshake → validate + dispatch + 202
  }

  protected override MondayWebhookReceivedCommand BuildCommand(byte[] body, Guid key, DateTimeOffset at) =>
    new(body, key, at);

  [LoggerMessage(EventId = 300, Level = LogLevel.Information,
    Message = "Monday.com webhook challenge requested {Challenge}")]
  partial void MondayChallengeRequested(string challenge);
}
```

Versus a full `PostAsync` override: the challenge no longer needs `EnableBuffering()` or a second read — it parses the bytes the base already captured, and the dispatch/202 path is inherited untouched. The `[LoggerMessage]` co-location is the sanctioned `partial` exception (performance-posture spec §4.2 / CLAUDE.md §2.3).

#### 7.1.1 The authentication schemes

Verification lives in authentication, not authorization — resolving a partner from an HMAC or a source IP *is* establishing who is calling, and only an authentication handler can put the resolved `client_id` onto the principal before claims freeze. The three schemes are registered once by `AddNorseWebHost()`:

| Scheme | Resolves client by | Verifies | On success |
|---|---|---|---|
| `ClientCredentials` | the JWT itself (OpenIddict) | token signature | `client_id` claim already present |
| `Signature` | `{partnerCode}` route value → `IWebhookClientResolver` | `HMAC(body, client.SigningSecret)` | emits `client_id` claim |
| `Whitelist` | `{partnerCode}` route value → `IWebhookClientResolver` | `RemoteIp ∈ client.IpAllowlist` | emits `client_id` claim |

- The custom `Signature` / `Whitelist` handlers live in `Norse.Hosting.Web` and depend only on the Abstractions `IWebhookClientResolver` contract; **`Norse.Auth.Server`** implements it over the OpenIddict application store (registered in `AuthPlugin.ConfigureServices`). No realm-DAG inversion — the handler binds to declared law, not to the product.
- **The route partner-code is untrusted** until the looked-up client's signature/IP check passes. It only *locates* the verification material; the verification is what authenticates. The `client_id` namespace is therefore never attacker-controllable.
- The `Signature` handler reads the buffered request body to compute the HMAC; webhook endpoints enable buffering so the controller base can re-read it. The handler is generic and data-driven — one handler serves every signature-tier partner, the secret looked up per request. No per-command validator type.
- All three schemes return bare `401` / `403` — webhook senders are machines; there is no interactive login to redirect to.

**Registration** in the plugin's `ConfigureServices` is just the application part — the schemes are platform-wide:

```csharp
services.AddControllers().AddApplicationPart(typeof(StripeWebhookController).Assembly);
```

**What this buys:**
- No copy-paste of "capture body, verify, dispatch, return 202" across controllers — and no per-command validator type to write.
- Verification is data-driven from the OpenIddict client store: onboarding a new signature/whitelist partner is a client-store registration, not a code change or redeploy.
- One uniform namespace source (`client_id`) across all three tiers — idempotency-key synthesis is identical regardless of how the partner authenticated.

**What stays per-controller:**
- The `[Authorize(AuthenticationSchemes = …)]` attribute — declares the partner's tier.
- The `[Route]` (with `{partnerCode}` for the non-JWT tiers).
- The one-line `BuildCommand`, and `TryHandleVerificationAsync` only when the provider demands a handshake.

**Why an abstract class, not just an interface:** webhook controllers need the inherited `[HttpPost] Receive(...)` action to participate in MVC routing. An interface would force each controller to re-declare the action, defeating the purpose.

## 8. `ConfigureServices` — Cross-Cutting Conventions

Plugins do all their registration work in `ConfigureServices`. The conventions below are uniform across plugins; deviations need a documented reason.

**Options binding (universally):**
```csharp
services.AddOptions<BillingOptions>()
  .BindConfiguration("Billing")
  .ValidateDataAnnotations()
  .ValidateOnStart();
```
Each plugin's `.ValidateOnStart()` call registers a startup validator. The host runs all registered startup validators during initialization; missing required config values fail the host before traffic.

**No DbContext registration in plugins.** The per-service DbContext family (`BillingDbContext`, `ClaimsDbContext`, etc.) is owned by `Norse.Infrastructure.Persistence`, not by per-context plugins. Entity classes plus `IEntityTypeConfiguration<T>` implementations ship in `{Company}.{Context}.Worker` — the system-of-record tier *(amended 2026-06-03; previously `.Server`)* — and Infrastructure's startup scans the `.Worker` assemblies for those configurations and applies them to the right context's DbContext. Plugins inject repository contracts from `Norse.Abstractions.Infrastructure` for actual data access (`IDocumentRepository<T>` on the web side; `ICommandRepository<T>` / `ICachedRepository<T>` / `ITemporalRepository<T>` on the worker side). EF Core migrations are NEVER applied at host startup (CLAUDE.md §8). The migrations service (§10) owns that — and orchestrates each `{Company}.{Context}.Migrations` package against the matching `Norse.Infrastructure.Persistence` DbContext in dependency order.

**HttpClient registration** *(amended 2026-06-07 — egress spec):* split by destination.

- **External / third-party APIs** (payment processors, rating services, partner systems) register through the **egress layer**, never raw `AddHttpClient`:
```csharp
services.AddExternalApi(
	name:     "payments",
	profile:  ResilienceProfile.Standard,	// required — no silent default
	baseAddress: new(configuration["Payments:BaseUri"]!),
	auth:     EgressAuth.Bearer(configuration["Payments:Token"]!),
	parser:   ResponseParser.Json<PaymentResult>());
```
  The egress registration removes the global standard handler for that client and applies its named resilience profile + per-partner `EgressClassifier` (egress spec §4.4 / §6). The plugin author still never hand-wires Polly — they pick a profile name. See `2026-06-07-egress-http-resilience-parsing-design.md`.

- **Infrastructure HttpClients** (OIDC / metadata discovery, OTel export, internal plumbing — *not* calls to a bounded context, which are forbidden) keep the global default: `AddNorseWebHost()` applies `AddStandardResilienceHandler` via `ConfigureHttpClientDefaults`, and these clients inherit it with no per-client wiring.

**The service POCO** (the IService implementation that backs gRPC and any future JSON controller):
```csharp
services.AddScoped<IBillingApi, BillingService>();
```

**Authorization policies:**
```csharp
services.AddAuthorizationBuilder()
  .AddPolicy(BillingPolicies.CustomerSelfService, p => p.RequireClaim("population", "Customer"))
  .AddPolicy(BillingPolicies.StaffAdmin, p => p.RequireClaim("population", "Staff").RequireRole("billing-admin"));
```
Policy names live in a per-context static class (`BillingPolicies.CustomerSelfService`), referenced from `.RequireAuthorization(BillingPolicies.CustomerSelfService)` calls in `MapEndpoints`.

**Controller application parts** (webhooks and partner JSON):
```csharp
services.AddControllers()
  .AddApplicationPart(typeof(StripeWebhookController).Assembly);     // webhooks
// If a Stage 3 partner JSON assembly exists, add it too:
// .AddApplicationPart(typeof(BillingPartnerController).Assembly);
```

**BackgroundService registration** (worker plugins, or web plugins doing dual duty):
```csharp
services.AddHostedService<NightlyBordereauxWorker>();
```
Loaded by whichever host (`Norse.Hosting.Web.Server` for web, `Norse.Hosting.Worker` for dedicated worker) the plugin is registered against.

## 9. What `AddNorseWebHost()` / `AddNorseWorkerHost()` Bring

The platform cross-cutting that plugins inherit automatically and never reconfigure. Both hosts share most of this; differences are noted.

### `AddNorseWebHost()`

| Concern | What's registered |
|---|---|
| gRPC server | `services.AddGrpc()`. Reflection enabled in `Development` and `Staging`, disabled in `Production`. |
| Controllers | `services.AddControllers()` with platform `JsonOptions` (snake_case property names via `JsonNamingPolicy.SnakeCaseLower`, ISO-8601 dates, enums as strings). Plugins add application parts. |
| Partner OpenAPI document | One document. A custom `IOpenApiDocumentTransformer` includes only controllers inheriting `Norse.Infrastructure.Api.JsonControllerBase<TService>`. |
| HttpClient defaults | `ConfigureHttpClientDefaults(b => b.AddStandardResilienceHandler())` — the default for **infrastructure** HttpClients only. *(Amended 2026-06-07 — egress spec.)* External/third-party calls register via `AddExternalApi` (egress layer), which replaces this handler with a required named profile + classifier; they do **not** inherit the global default. |
| Authentication / Authorization | `services.AddAuthentication()` + `services.AddAuthorizationBuilder()`. Plugins add schemes and policies inside their `ConfigureServices`. |
| Exception handling | Platform-wide `ProblemDetails` mapping for unhandled exceptions, `RpcException` translation, validation failures. |
| Telemetry hooks | OTEL request-scope enrichment with route, gRPC service, plugin name. Underlying OTEL pipeline comes from `ServiceDefaults`. |
| IOptions validation | Plugins call `.ValidateOnStart()` on their options bindings (convention). The host's standard startup-validator infrastructure runs all registered validators during initialization; misconfiguration fails the host before traffic. |

### `AddNorseWorkerHost()`

| Concern | What's registered |
|---|---|
| HttpClient defaults | Same infrastructure default + egress-layer carve-out as the web host. *(Amended 2026-06-07 — egress spec.)* The worker is the primary egress consumer — ingestion handlers calling partner APIs. |
| BackgroundService runtime | `Microsoft.Extensions.Hosting`'s `BackgroundService` infrastructure — already there by virtue of `HostApplicationBuilder`, but the worker host adds observable health hooks (per-`BackgroundService` health checks reflecting last-execution status). |
| IOptions validation | Same global `ValidateOnStart()` pass. |
| Telemetry hooks | Background-service-scoped OTEL enrichment. |
| No HTTP surface | Worker host has only the standard `/health` and `/alive` endpoints from `ServiceDefaults`. No gRPC, no controllers, no OpenAPI. |

### Critical: `BackgroundServiceExceptionBehavior` is **default** in web and worker hosts

`Microsoft.Extensions.Hosting`'s default behavior is `StopHost` — a `BackgroundService` that throws shuts the entire process down. Norse keeps this default for `Norse.Hosting.Web.Server` and `Norse.Hosting.Worker`: a background service exception there should fail loud (the orchestrator/operator wants the pod to crash and restart). The migrations host (§10) is the **only** deployable that overrides this to `Ignore`.

## 10. Migrations Service

A separate runtime library and a separate deployable. Different lifecycle from web/worker hosts: orchestrator runs once, then idles; never exits non-zero; readiness is signaled via the standard `/health` endpoint.

### 10.1 The Contract

```csharp
// Contract: lives in Norse.Abstractions.Hosting (declared law).
namespace Norse.Abstractions.Hosting;

/// <summary>
/// Contract for a migrations contributor. One implementation per
/// {Company}.{Context}.Migrations assembly. The runtime instantiates
/// contributors via new TContributor(), topologically sorts them by DependsOn, then
/// invokes RunAsync on each in dependency order.
/// </summary>
public interface IMigrationContributor
{
  string ContextName { get; }
  IReadOnlyCollection<string> DependsOn { get; }

  /// <summary>
  /// Apply this contributor's pending migrations. Returns the count applied (0 if no-op).
  /// Throwing flips the host's /health to Unhealthy and aborts remaining contributors;
  /// the host stays alive so the failure can be diagnosed via standard health/logs surface.
  /// </summary>
  Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>
/// Convenience base for EF-Core-backed contributors. Handles pending-migration detection
/// and the MigrateAsync call. Derived classes declare ContextName, DependsOn, and the
/// DbContext registration in their own ConfigureServices step (invoked by the runtime
/// before RunAsync).
/// </summary>
public abstract class EfCoreMigrationContributor<TContext> : IMigrationContributor
  where TContext : DbContext
{
  public abstract string ContextName { get; }
  public virtual IReadOnlyCollection<string> DependsOn => [];

  public async Task<int> RunAsync(IServiceProvider services, CancellationToken ct)
  {
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<TContext>();
    var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
    if (pending.Count == 0) return 0;
    await db.Database.MigrateAsync(ct);
    return pending.Count;
  }
}
```

### 10.2 Per-context Contributor (lives in `{Company}.{Context}.Migrations`)

```csharp
internal sealed class BillingMigrationContributor : EfCoreMigrationContributor<BillingDbContext>
{
  public override string ContextName => "Billing";
  public override IReadOnlyCollection<string> DependsOn => ["Customer", "Auth"];
}
```

The DbContext registration (and its connection-string binding) lives in the per-context `Migrations` assembly's own setup, invoked by the runtime before `RunAsync`. The spec-level guarantee is that a contributor declares its DbContext type and the runtime arranges for it to be resolvable from the `IServiceProvider` passed to `RunAsync`; the exact mechanism is the implementation plan's choice.

### 10.3 Runtime Orchestration

The orchestrator is a `BackgroundService`. It uses the framework's `BackgroundServiceExceptionBehavior.Ignore` primitive so an exception leaves the host running but flips `/health` to `Unhealthy`.

```csharp
// Concrete runtime: lives in Norse.Hosting.Migrations.Service (connective tissue).
namespace Norse.Hosting.Migrations.Service;

public static class NorseMigrationsHostBuilderExtensions
{
  public static INorseMigrationsHostBuilder AddNorseMigrationsHost(this IHostApplicationBuilder builder)
  {
    // Keep the host alive even if the orchestrator's ExecuteAsync throws. This is what
    // forces the operator to stare at the failure: process stays up, /health is Unhealthy,
    // downstream Aspire/K8s gates never release, the only way forward is to fix the migration.
    builder.Services.Configure<HostOptions>(o =>
      o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

    builder.Services.AddSingleton<MigrationsHealthStatus>();
    builder.Services.AddHealthChecks().AddCheck<MigrationsHealthCheck>("migrations");
    builder.Services.AddHostedService<MigrationsOrchestrator>();
    return new NorseMigrationsHostBuilder(builder);
  }
}

public interface INorseMigrationsHostBuilder
{
  INorseMigrationsHostBuilder AddMigration<TContributor>()
    where TContributor : IMigrationContributor, new();
}

internal sealed class MigrationsOrchestrator(
  IEnumerable<IMigrationContributor> contributors,
  IServiceProvider services,
  MigrationsHealthStatus health,
  IHostApplicationLifetime lifetime,
  IOptions<NorseMigrationsOptions> options,
  ILogger<MigrationsOrchestrator> log) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var ordered = TopologicalSort.Order(contributors);   // throws on cycle

    for (var i = 0; i < ordered.Count; i++)
    {
      var c = ordered[i];
      health.ReportInProgress(c.ContextName, i + 1, ordered.Count);
      try
      {
        var applied = await c.RunAsync(services, stoppingToken);
        log.LogInformation("Migration {Context} applied {Count} migration(s)", c.ContextName, applied);
      }
      catch (Exception ex)
      {
        health.ReportFailure(c.ContextName, ex);
        log.LogError(ex, "Migration contributor {Context} failed; process will stay alive", c.ContextName);
        _ = RecurringFailureLog.Start(log, c.ContextName, ex, stoppingToken);
        throw;     // BackgroundServiceExceptionBehavior.Ignore keeps the host running.
      }
    }

    // Success path: flip /health to Healthy, give the readiness-check infrastructure
    // a brief grace period to observe Healthy, then exit 0.
    health.ReportHealthy();
    log.LogInformation("All migrations applied successfully; releasing readiness gate in {Grace} and shutting down",
      options.Value.HealthyShutdownGracePeriod);
    await Task.Delay(options.Value.HealthyShutdownGracePeriod, stoppingToken);
    lifetime.StopApplication();   // Graceful shutdown; process exits 0.
  }
}
```

### 10.4 The Deployable — `Norse.Hosting.Migrations.Service`

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.AddNorseMigrationsHost()
  .AddMigration<AuthMigrationContributor>()
  .AddMigration<CustomerMigrationContributor>()
  .AddMigration<BillingMigrationContributor>()
  .AddMigration<ClaimsMigrationContributor>()
  .AddMigration<PolicyMigrationContributor>()
  .AddMigration<LayoutMigrationContributor>();

await builder.Build().RunAsync();
```

### 10.5 Behavior Guarantees

- **Exits 0 on successful completion; never exits non-zero.** Once every contributor returns, the orchestrator flips `/health` to `Healthy`, waits for a configurable grace period (`HealthyShutdownGracePeriod`, default 5 seconds — long enough for Aspire's health-check infrastructure to observe Healthy before the host shuts down), then calls `IHostApplicationLifetime.StopApplication()`. The process exits 0 and stops consuming resources.
- **Stays alive on contributor failure.** `BackgroundServiceExceptionBehavior.Ignore` swallows the orchestrator's exception at the framework level; the host keeps running with `/health` reporting `Unhealthy` until shutdown signal. Aspire's `WaitFor` never releases, downstream services never start, and the developer is forced to look at the failure rather than have it papered over by an exit code.
- **Recurring failure log.** On contributor failure, a logger writes the exception every 60 seconds for the rest of the process lifetime so the developer sees it in stdout / Aspire dashboard / pod logs regardless of when they look.
- **Idempotent.** `MigrateAsync` against `__EFMigrationsHistory` makes second runs a no-op; the orchestrator reports `Healthy` with `applied=0` for each contributor on a re-run.
- **Cycle detection.** A cycle in `DependsOn` is detected during `TopologicalSort.Order(...)`; the throw propagates through `BackgroundServiceExceptionBehavior.Ignore`, flips health to `Unhealthy`, and the recurring log identifies the cycle members.
- **Same binary, local and prod.** Aspire `WaitFor(migrations)` and K8s readiness gates both consume the same `/health` signal. On success the binary exits 0 either way; the readiness gate has already released. The Hosting spec mandates the binary's behavior; the K8s deployment pattern (Job + readiness gate vs. sidecar vs. init-container-with-wrapper-script) is operations territory.

## 11. Worked Example — `BillingPlugin` + `BillingWorkerPlugin` End-to-End

A representative pair exercising every facet of the design. *(Amended 2026-06-03.)* Billing splits across three server-side assemblies: `.Backend` (server→worker commands like `StripeWebhookReceivedCommand`, Mongo document records, shared server-side types), `.Server` (gRPC service impl, web plugin, webhook controller, Mongo reads + shim writes), and `.Worker` (entities, EF configurations, business logic, NServiceBus handlers, nightly bordereaux background service, worker plugin). `.Server` and `.Worker` are mutually invisible; both reference `.Backend` and `.Contracts`.

### 11.1 `{Company}.Billing.Server/BillingPlugin.cs`

```csharp
namespace {Company}.Billing.Server;

using Norse.Abstractions.Hosting;
using Norse.Abstractions.Infrastructure;  // IDocumentRepository<T>, ICommandRepository<T>, etc.

internal sealed class BillingPlugin : IWebHostPlugin
{
  public void ConfigureServices(
    IServiceCollection services,
    IHostEnvironment environment,
    IConfiguration configuration)
  {
    services.AddOptions<BillingOptions>()
      .BindConfiguration("Billing")
      .ValidateDataAnnotations()
      .ValidateOnStart();

    // NOTE: No DbContext registration — and no entities in this assembly at all
    // (amended 2026-06-03; they live in {Company}.Billing.Worker). BillingService
    // below is the web tier: it reads Mongo documents via IDocumentRepository<T>,
    // shims request portions on writes, and sends commands from
    // {Company}.Billing.Backend via this context's IMessageSession. It cannot
    // reach Postgres — the entity types are not referenceable from here.
    services.AddScoped<IBillingApi, BillingService>();

    services.AddHttpClient<IPaymentsClient, PaymentsClient>(client =>
    {
      client.BaseAddress = new(configuration["Payments:BaseUri"]!);
    });

    services.AddControllers()
      .AddApplicationPart(typeof(StripeWebhookController).Assembly);

    services.AddAuthorizationBuilder()
      .AddPolicy(BillingPolicies.CustomerSelfService, p =>
        p.RequireClaim("population", "Customer"))
      .AddPolicy(BillingPolicies.StaffAdmin, p =>
        p.RequireClaim("population", "Staff").RequireRole("billing-admin"));
  }

  public void MapEndpoints(IEndpointRouteBuilder endpoints)
  {
    endpoints.MapGrpcService<BillingService>()
      .RequireAuthorization(BillingPolicies.CustomerSelfService);

    endpoints.MapGrpcService<BillingAdminService>()
      .RequireAuthorization(BillingPolicies.StaffAdmin);
  }
}
```

*(Amended 2026-06-03.)* Entity classes (`Invoice`, `Payment`, etc.) and `IEntityTypeConfiguration<Invoice>` / `IEntityTypeConfiguration<Payment>` / ... implementations (relationships, indexes, check constraints, `builder.ToTable("invoices", schema: "billing")`) ship in `{Company}.Billing.Worker`, not here. Norse.Infrastructure.Persistence picks them up from the `.Worker` assembly at startup. Worker-side handlers inject the worker-only repository contracts (`ICommandRepository<Invoice>`, `ICachedRepository<RateTable>`, etc.) — never a DbContext, never `SaveChangesAsync`; the commit is atomic with the NServiceBus outbox per the messaging spec §5.2. `BillingService` on the web side injects `IDocumentRepository<T>` (Mongo) and `IMessageSession` only.

### 11.1b `{Company}.Billing.Worker/BillingWorkerPlugin.cs`

*(Amended 2026-06-03.)* `.Worker` does **not** reference `{Company}.Billing.Server` — hard wall. It owns the system of record: entity classes, EF configurations, NServiceBus command/saga handlers, and background services. Shared types (commands, Mongo document records, options) come from `{Company}.Billing.Backend`. The worker plugin registers its own services in full; nothing is borrowed from the web plugin. DbContext registration is Infrastructure's job; handler/saga registration is contributed explicitly per the messaging spec §3.

```csharp
namespace {Company}.Billing.Worker;

using Norse.Abstractions.Hosting;
using {Company}.Billing.Backend;  // commands, Mongo document records, shared options

internal sealed class BillingWorkerPlugin : IWorkerHostPlugin
{
  public void ConfigureServices(
    IServiceCollection services,
    IHostEnvironment environment,
    IConfiguration configuration)
  {
    // No reference to .Server — hard wall; the halves meet only at the queue.
    // Entities and IEntityTypeConfiguration<T> impls ship in THIS assembly;
    // Norse.Infrastructure.Persistence scans them at startup and wires repositories.
    // NServiceBus handlers/sagas register via the explicit source-generated
    // contribution per the messaging spec — never assembly scanning.
    services.AddHostedService<NightlyBordereauxWorker>();
  }
}
```

`NightlyBordereauxWorker` and the command handlers consume the Abstractions repository contracts (`ICommandRepository<T>` and friends); they never see a DbContext, and their Postgres commits are atomic with the NServiceBus outbox.

### 11.2 `{Company}.Billing.Server/StripeWebhookController.cs` (webhook surface)

Inherits `WebhookControllerBase<TCommand>` from §7.1. The auth-then-dispatch flow lives in the base class; the concrete controller is the route attribute, the generic-parameter declaration of the command type, the partner namespace, and a one-line `BuildCommand`. The validator and the context's `IMessageSession` are forwarded to the base constructor.

```csharp
[ApiController]
[Route("webhooks/stripe")]
internal sealed class StripeWebhookController(
  IWebhookValidator<StripeWebhookReceivedCommand> validator,
  IMessageSession session,
  ILogger<WebhookControllerBase<StripeWebhookReceivedCommand>> log)
  : WebhookControllerBase<StripeWebhookReceivedCommand>(validator, session, log)
{
  protected override Guid PartnerNamespace => WebhookNamespaces.Stripe;

  protected override StripeWebhookReceivedCommand BuildCommand(byte[] body, Guid key, DateTimeOffset at) =>
    new(body, key, at);
}
```

The Stripe-specific validator (`StripeSignatureValidator : IWebhookValidator<StripeWebhookReceivedCommand>`) sits alongside in `{Company}.Billing.Server` and is registered in `BillingPlugin.ConfigureServices`:

```csharp
services.AddScoped<IWebhookValidator<StripeWebhookReceivedCommand>, StripeSignatureValidator>();
```

*(Amended 2026-06-03.)* The controller sends via the context's `IMessageSession`, resolved from the Billing endpoint's per-context registration — no dispatch abstraction in between.

### 11.3 `{Company}.Billing.JsonApi/BillingPartnerController.cs` (Stage 3, optional)

Created only if a non-gRPC partner needs the service. Lives in a separate assembly. Inheriting `Norse.Infrastructure.Api.JsonControllerBase<TService>` IS the partner-facing declaration.

```csharp
using Norse.Infrastructure.Api;

[ApiController]
[Route("api/v1/billing")]
internal sealed class BillingPartnerController(IBillingApi billing)
  : JsonControllerBase<IBillingApi>(billing)
{
  [HttpPost("summary")]
  public Task<IActionResult> GetSummary(GetBillingSummaryRequest req, CancellationToken ct)
    => InvokeAsync(svc => svc.GetSummaryAsync(req, ct), ct);
}
```

### 11.4 `Norse.Hosting.Web.Server/Program.cs` (the entire server-side composition)

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.AddNorseWebHost()
  // IWebHostPlugin implementations from each *.Server assembly
  .AddPlugin<AuthPlugin>()          // cross-cutting first
  .AddPlugin<BillingPlugin>()
  .AddPlugin<ClaimsPlugin>()
  .AddPlugin<CustomerPlugin>()
  .AddPlugin<LayoutPlugin>()
  .AddPlugin<PolicyPlugin>()
  // IWorkerHostPlugin implementations from each *.Worker assembly (monolith mode)
  .AddPlugin<BillingWorkerPlugin>()
  .AddPlugin<ClaimsWorkerPlugin>();

var app = builder.Build();
app.UseNorseWebHost();
app.Run();
```

That's the entire production server-side posture in monolith mode. Worker plugins ride along in-process. Scale horizontally by running more replicas of the same process. If a specific worker's resource profile justifies splitting, drop the matching `.AddPlugin<…WorkerPlugin>()` line from `Norse.Hosting.Web.Server` and pick it up in `Norse.Hosting.Worker`'s Program.cs instead.

## 12. Aspire Wiring — `Norse.Hosting.AppHost`

The local-dev orchestrator composes the three deployables. `WaitFor(migrations)` is the gate that enforces "migrations succeed before web/worker start":

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres  = builder.AddPostgres("postgres").AddDatabase("{company}");
// Container images are pinned explicitly (WithImage/WithImageTag) — never Aspire
// defaults. Decided pins and rationale: §13 #17.
var rabbit    = builder.AddRabbitMQ("rabbit");           // messaging spec landed 2026-06-03
// Plus the Particular platform containers (ServiceControl error/audit/monitoring +
// ServicePulse) per messaging spec §8.2 — full retry/audit fidelity in local dev.
var openiddict = ...;                                    // local OpenIddict resource if needed

var migrations = builder.AddProject<Projects.Norse_Hosting_Migrations_Service>("migrations")
  .WithReference(postgres)
  .WaitFor(postgres);

var host = builder.AddProject<Projects.Norse_Hosting_Web_Server>("host")
  .WithReference(postgres)
  .WithReference(rabbit)
  .WaitFor(migrations);                                  // gates on /health = Healthy

var worker = builder.AddProject<Projects.Norse_Hosting_Worker>("worker")
  .WithReference(postgres)
  .WithReference(rabbit)
  .WaitFor(migrations);                                  // same gate

builder.Build().Run();
```

If migrations stalls in `Unhealthy`, `host` and `worker` stay in `Waiting`. The Aspire dashboard shows the failed contributor and the exception. Developer fixes the migration, restarts the AppHost. They never see a partially-migrated platform.

`WaitForCompletion` is **not** used. It has historically been unreliable; `WaitFor` on the health signal is the path that actually works.

## 13. Resolved Decisions

Captured here so the rationale survives the move from draft to implementation.

1. **Two methods on the plugin interface, total.** `ConfigureServices` (builder phase) and `MapEndpoints` (app phase, web-only). The natural ASP.NET Core lifecycle separation prevents wrong-place wiring; XML docs name the alternative for every common "I think I need…" instinct. Pit of success via minimum surface, not via documentation of many surfaces.

2. **No CRTP base classes.** Plugins are plain `internal sealed class BillingPlugin : IWebHostPlugin` (in `.Server`) and `internal sealed class BillingWorkerPlugin : IWorkerHostPlugin` (in `.Worker`). Naming/context derivation is handled by the `[ArchitecturalContext]` attribute (architecture-analyzers spec §3), not by CRTP threading.

3. **Explicit `AddPlugin<T>()` registration; no scanning, no source generator.** CLAUDE.md §8's "no convention scanning for handlers" applies here. Adding a context = one line per plugin (Web, Worker) in `Norse.Hosting.Web.Server`'s `Program.cs`. Each `Program.cs` is a readable manifest of the platform's composition.

4. **Plugins split by deployable destination; `.Server` and `.Worker` are hard walls.** *(Amended 2026-06-03 — originally ".Worker references .Server"; that was wrong.)* A context that exposes gRPC services and runs background services has TWO plugin classes in two mutually invisible assemblies — `BillingPlugin : IWebHostPlugin` in `{Company}.Billing.Server` and `BillingWorkerPlugin : IWorkerHostPlugin` in `{Company}.Billing.Worker`. Shared server-side types (commands, Mongo document records) live in `{Company}.Billing.Backend`, referenced by both. The worker never references ASP.NET Core; the server never references EF Core or entities. `Norse.Hosting.Web.Server` registers both kinds of plugin in monolith mode; `Norse.Hosting.Worker` registers only the worker plugins when run as a split deployable.

5. **No client-side plugin variants.** WASM and MAUI clients use source-generated `Add{Company}{Context}Client(apiBase)` extension methods (from the UI Composition spec's generator). The growth pressure that justifies server-side plugins doesn't exist on the client (UI registration + gRPC-Web client registration is two lines per context).

6. **`MapEndpoints` is gRPC-only.** Plugin minimal API is not a supported pattern. Operational endpoints, diagnostics, internal staff actions — all gRPC services. Postman with gRPC reflection (enabled in Development/Staging) handles ad-hoc operator interaction.

7. **Native `Grpc.AspNetCore` + `Grpc.Net.Client.Web` + `Google.Protobuf`.** `protobuf-net.Grpc` rejected for uncertain AOT roadmap. .proto files are LLM-authored alongside the C# interface; the friction protobuf-net.Grpc avoided becomes a tooling problem instead of an architectural one. (See §15 for the UI Composition spec amendment.)

8. **gRPC reflection on in `Development` and `Staging`, off in `Production`.** Operators (Postman, internal scripts) hit gRPC services directly without distribution of a .proto file in pre-prod. Production keeps the attack surface minimal.

9. **One OpenAPI document.** The partner-facing one. Filter is "controllers inheriting `JsonControllerBase<TService>`." Webhooks (`ControllerBase`) and gRPC services do not appear. The two-document model considered earlier is gone with the internal-JSON use case (which gRPC absorbed).

10. **`Norse.Infrastructure.Api.JsonControllerBase<TService>` is the single JSON controller base.** Inheriting it IS the partner-facing declaration. No "internal-default JsonControllerBase variant"; that's `ControllerBase` (used for webhooks, automatically excluded from the OpenAPI doc). The base class lives in Infrastructure (concrete platform infrastructure); the inclusion rule it triggers lives in `Norse.Hosting.Web`'s OpenAPI document transformer (connective tissue).

11. **Migrations service exits 0 on success; stays alive only on failure.** Once every contributor completes, the orchestrator flips `/health` to `Healthy`, waits a brief grace period (`HealthyShutdownGracePeriod`, default 5 seconds — long enough for the readiness-check infrastructure to observe Healthy), then calls `IHostApplicationLifetime.StopApplication()` and exits 0. On contributor failure, `BackgroundServiceExceptionBehavior.Ignore` keeps the host alive in `Unhealthy` state with a recurring failure log; the developer is forced to look at the problem rather than have it papered over by an exit code. The exit-on-success path stops consuming resources once the readiness gate has released; the never-exit-on-failure path forces the operator to actually fix the failure. Aspire's `WaitFor` never depends on `WaitForCompletion`.

12. **`AddNorseWebHost()` calls `services.AddControllers()` to install the OpenAPI document transformer.** Plugins also call `services.AddControllers().AddApplicationPart(...)` in their `ConfigureServices` to opt in their controller assemblies (webhooks, partner JSON). `AddControllers()` is idempotent — both calls coexist cleanly. Plugins that have no controllers omit the `AddApplicationPart` call.

13. **HttpClient resilience: infrastructure default + egress carve-out.** *(Amended 2026-06-07 — egress spec supersedes the original "resilience defaults are global.")* The global `ConfigureHttpClientDefaults(b => b.AddStandardResilienceHandler())` in `AddNorseWebHost` is the default for **infrastructure** HttpClients only. **External/third-party HTTP routes through the egress layer** (`IHttpEgress` / `AddExternalApi`, egress spec) with a *required* named resilience profile (`Standard` / `RetryAfterTolerant`) + per-partner `EgressClassifier`, which removes the global handler for that client. Plugins never hand-wire Polly either way — infrastructure clients inherit the default, egress clients pick a profile name. Cross-context HTTP remains forbidden (events + gRPC only).

14. **No `BackgroundServiceExceptionBehavior` override in web/worker hosts.** Default `StopHost` behavior is correct there — a failing background service should fail the pod loudly. The migrations host is the only deployable that overrides to `Ignore`.

15. **Webhook controllers follow the auth-then-dispatch convention (§4).** Verify authenticity, capture raw bytes, dispatch a command to the message bus, return `202 Accepted`. No deserialization in the controller; no business logic in the controller. Failure modes downstream of the queue land in the DLQ where operations can react — they never propagate back to the webhook sender as retries. This is a Hosting-layer convention, not a per-context one; every webhook controller in the platform obeys it. *(Amended 2026-06-07.)* The sole non-202 success path is a provider subscription-verification handshake, answered via the base class's `TryHandleVerificationAsync` hook (after authz, before the validator). The dispatched command is minimal (ruling 1.4): raw bytes, a synthesized `Guid` idempotency key, receipt time — no headers/URL/IP on the wire. A webhook handler that must call the partner's API back does so in the worker via the egress layer, never in the controller.

16. **`WebhookControllerBase<TCommand>` is a Norse-tier concrete (§7.1); the contracts it depends on live in Abstractions.** *(Amended 2026-06-03; auth model superseded 2026-06-07.)* The webhook contracts (`IWebhookCommand`, `WebhookSchemes`, `IWebhookClientResolver`, `WebhookClient`) ship from `Norse.Abstractions.Hosting` — they're declared law. The abstract `WebhookControllerBase<TCommand>` MVC base class ships from `Norse.Hosting.Web` — connective tissue binding the contracts to ASP.NET Core MVC. The auth-then-dispatch convention is implemented once in the shared abstract base; dispatch is a direct `IMessageSession.Send` to the context's durable worker endpoint. Concrete webhook controllers are reduced to one `[Authorize(AuthenticationSchemes = …)]` attribute (declaring the partner's capability tier), a `[Route]`, a generic-parameter declaration, and a one-line `BuildCommand`. **Verification is authentication, not per-command validation** *(2026-06-07)*: the former `IWebhookValidator<TCommand>` is deleted in favor of three `WebhookSchemes` (client-credentials JWT, HMAC `Signature`, IP `Whitelist`), each a generic data-driven authentication handler that resolves the partner's OpenIddict `client_id` (via `IWebhookClientResolver`, implemented by `Norse.Auth.Server` over the OpenIddict store) and surfaces it as a claim — so the base reads the idempotency namespace uniformly and no code mutates the frozen principal. The former `IWebhookDispatcher` abstraction is deleted — it existed solely to keep controllers library-agnostic while CLAUDE.md §7 #2 was open; the decision is made (NServiceBus, see `2026-06-03-messaging-foundation-design.md`), and `NServiceBus.Testing`'s `TestableMessageSession` provides the test seam the wrapper would have.

17. **Container images are pinned, fully qualified, and glibc-based.** *(Added 2026-06-07.)* `Norse.Hosting.AppHost` never accepts Aspire's default image tags — every container resource declares image and tag explicitly (`WithImage`/`WithImageTag`). Image movement is a deliberate act, same spirit as package-version pinning. Two pins are already decided:
    - **PostgreSQL: official `postgres`, Debian variant, explicit codename tag** — today's shape is `postgres:19beta1-trixie`; at adoption, the then-current GA (`postgres:19.x-trixie` or successor codename). glibc over musl, decided once and never switched: Postgres is allocation- and `memcpy`-heavy, where glibc's ptmalloc and SIMD string routines beat musl's portable implementations — and the glibc-vs-ICU collation split between libc families is index-corruption risk on any future data move, so the libc family is a one-way door. The bare `postgres:19*` tag aliases the trixie variant today (digest-verified 2026-06-07); the explicit suffix exists so a docker-library default-base flip can't silently move us. Evidence: the PG19 temporal POC (`poc/pg19-temporal/`) runs this image; its 2026-06-07 official-image re-run matched the release-day PGDG-apt run verdict-for-verdict.
    - **TimescaleDB: `timescale/timescaledb-ha`, fully pinned (today `pg18.4-ts2.27.1`-shaped), never `-oss`, never `-all`.** Carries unchanged to `pg19.x-ts2.yy.z` when the PG19-based image ships — expect weeks-to-months after PG19 RTM; Timescale trails PG majors. Why `-ha` over the Alpine `timescale/timescaledb`: (a) the glibc rule made structural — `timescaledb-toolkit` is Rust requiring glibc 2.33+, so the Alpine image cannot carry it at all; (b) the payload is §4's persistence row in one container — timescaledb + toolkit + pgvector/pgvectorscale (+ PostGIS); (c) `-oss` strips the TSL code, i.e. columnstore compression — the reason to run Timescale — and TSL community is free for self-hosted workloads (the restriction targets managed-DBaaS resale, not us); (d) `-all` bundles multiple PG majors into one ~2.7 GB image — dead weight when one major is pinned. Accepted cost: ~1.45 GB vs the Alpine image's ~244 MB — a one-time local pull. Quirks to verify at wiring time: `PGDATA` lives under `/home/postgres/pgdata` (not the official image's path), and the image carries Patroni/pgBackRest ballast we won't use; standard `POSTGRES_*` env vars work.
    - **RabbitMQ: official `rabbitmq`, `-management` variant, full version pin (today `rabbitmq:4.3.1-management`), no `-alpine`.** *(Added 2026-06-07.)* The musl/glibc perf argument is largely a database concern and does **not** carry here — the BEAM VM's `erts_alloc` framework self-manages arenas and bypasses libc malloc, so musl's allocator gap never engages; RabbitMQ bounds on scheduler/I/O/network, not libc. The Ubuntu image (24.04 base, Erlang/OTP built from source — verified 2026-06-07) wins anyway on cheaper grounds: the one-libc-family posture costs 29 MB to keep consistent, it's the canonical image docker-library/Team RabbitMQ treat as primary, and this container is local-dev/CI only (production is CloudAMQP, per §4 Messaging) so there is no perf stake at all. `-management` is deliberate: the management plugin is the HTTP API (queue browsing, definitions export/import, health tooling), Aspire's `WithManagementPlugin()` assumes it, it's the operator backstop if ServiceControl/ServicePulse misbehave, and it mirrors the CloudAMQP management UI the team gets in production.
    - **MongoDB: official `mongo`, full version pin with codename suffix (today `mongo:8.3.2-noble`), current rapid-release train.** *(Added 2026-06-07.)* The musl question is moot here — no alpine variant exists; `mongod` is glibc-only, so every candidate satisfies the libc posture for free. `library/mongo` over the `mongodb/` org images: canonical, sane tag taxonomy, Ubuntu 24.04, standard `MONGO_INITDB_*` env vars + `docker-entrypoint-initdb.d`, and it's what Aspire's `AddMongoDB` resolves. The `mongodb/mongodb-community-server` grid ({ubi8|ubi9|ubuntu2204} × slim × timestamped rebuilds) serves their Kubernetes operator and OpenShift compliance lineage — problems we don't have; FIPS/STIG variants are `mongodb-enterprise-server` compliance posture, owned by a future ops/deployment spec, never by local-dev image choice. Rapid train over the 8.0 major (human decision, 2026-06-07): this container is local-dev/CI only, Atlas runs the rapid train in production, and a pre-GA greenfield gains nothing anchoring to a major that will be superseded before launch. Accepted cost, eyes open: rapids EOL at the next rapid, so this pin moves quarterly rather than yearly. `mongodb-atlas-local` enters only if Atlas Search/Vector Search enter the spec — an open question as of 2026-06-07: vector-store ownership is under review for the AI spec (see `2026-06-07-vector-embeddings-decision-inputs.md`); if Mongo vector search wins there, this pin gains a `mongot`/atlas-local companion decision.
    - **Particular platform containers: `latest`, deliberately — a declared exception to the pin law.** *(Added 2026-06-07.)* The five images (`particular/servicecontrol`, `-audit`, `-monitoring`, `-ravendb`, `particular/servicepulse`) ride `latest` to absorb Particular's patches without revisiting; they ship in lockstep from one vendor, are dev-time observability tooling rather than data-bearing platform infrastructure, and have no variant axis to referee. Do not "fix" this into a pin — the exception is the decision.

## 14. Open Questions / Future Work

Deferred to specific follow-on specs or revisited when the platform's posture changes.

1. **MGA-specific cross-cutting middleware** (`NorsePrincipal` flow, audit publication — tenancy claim handling was originally listed and is resolved-N/A as of 2026-06-03: no tenancy claim exists under stamp-per-tenant, see `2026-06-03-tenancy-model-design.md`). Earlier drafts proposed product-tier plugin extensions (`I{Company}WebPlugin` / `I{Company}WorkerPlugin`) for this; the cleaner answer is shared middleware configured at the Norse host runtime, not interface extensions every context implements. Owns its own follow-on spec under the product track.

2. **Source-generated `Add{Company}{Context}Client(apiBase)` for WASM/MAUI shells.** The UI Composition spec's existing source generator (`Add{Company}{Context}Ui()`) is extended to also emit the gRPC-Web client registration. Detail belongs to the UI Composition spec amendment (§15) and the client-side `norse-infrastructure-api` spec.

3. **Production K8s pattern for the migrations service.** Init-container-with-wrapper-script, sidecar with readiness gate, separate `Job` with completion dependency — all viable. The Hosting spec settles the binary's behavior; the choice belongs to a future operations/deployment spec.

4. **OpenIddict integration patterns inside `AuthPlugin`.** Mentioned here but the detail (federation handlers, signing-key rotation hooks, MCP pre-registered client seed) is auth-federation spec territory (Plan A → Plan E sibling plans).

5. **`Norse.Hosting.Web.Client`** — the shared client-side cross-cutting library mentioned in §3. Specified when WASM/MAUI deployable specs are written.

6. **Health check granularity inside `Norse.Hosting.Web.Server`.** ServiceDefaults provides `/health` and `/alive`; should individual plugins contribute per-context health checks (DB connectivity, downstream HTTP reachability) to a more granular `/health/detail` document? Deferred until operational pain forces the question.

## 15. Spec Amendments Triggered by This Work

Two amendments to written specs follow from settling this one. They are listed here so they don't fall out of memory; the amendments themselves should be applied as their own commits to the existing spec files.

### 15.1 `2026-05-19-ui-composition-design.md` — gRPC transport switch

- **§2.2 (Stage 2 — Contract Crystallization):** replace "gRPC via `protobuf-net.Grpc`" with "Native `Grpc.AspNetCore` on server + `Grpc.Net.Client.Web` + `Google.Protobuf` on clients". Replace `[ServiceContract]` / `[OperationContract]` / `[DataContract]` mentions with ".proto files paired with the C# interface (LLM-assisted authoring); the C# interface remains the consumer-facing abstraction and is preserved across stage transitions via a thin adapter wrapping the generated `BillingClient`."
- **§5 (The Three Shells):** WASM/MAUI shell registration changes from `services.AddGrpcWebClient<IBillingApi>(...)` (protobuf-net.Grpc) to a source-generated `services.Add{Company}BillingClient(apiBase)` that internally registers `Grpc.Net.Client.Web` + Google.Protobuf-generated client + the adapter that implements `IBillingApi`.
- **§9 (Stage transitions in practice):** Day-N graduation no longer adds `[ServiceContract]` to `IBillingApi`. Instead it adds a `.proto` file (or LLM-generates one from the interface) and stands up the gRPC service via `Grpc.AspNetCore`'s generated `BillingApiBase` base class wrapped by an adapter delegating to `IBillingApi`.
- **§11.3 (Resolved Decisions):** keep the conclusion "gRPC-Web for both WASM and MAUI clients" but make the stack explicit: native Microsoft `Grpc.Net.Client.Web` + `Google.Protobuf`. Add a new resolved decision: **"protobuf-net.Grpc rejected; AOT roadmap risk does not warrant the lower friction."**

### 15.2 `2026-05-19-architecture-analyzers-design.md` — minor reference updates

- **§3 (Attribute Model) / §4 (Project-name → Layer mapping):** confirm that the four `Norse.Hosting.*` projects map to the layers/contexts in §3 of this spec. Likely already implicit; add an explicit row if convention scanning would not infer correctly.
- **§15.7 (Host plugin pattern):** add a forward reference to this spec as the authoritative definition of the plugin contract.

## 16. Done Criteria

This spec is "done enough to implement" when:

- [x] §1–§13 reviewed; the design is self-consistent.
- [x] Plugin interfaces in §5 read clearly enough for a new context author to implement a plugin from the XML docs alone, without referring back to the spec body.
- [x] The codified lifecycle rule in §4 is the single answer for "where does this endpoint go?" — the worked example (§11) demonstrates it end-to-end.
- [x] Migrations service guarantees (§10.5) align with what Aspire `WaitFor` and K8s readiness gates can consume.
- [x] Amendments to UI Composition spec (§15.1) and analyzers spec (§15.2) are scoped and queued.
- [ ] An implementation plan is drafted for the `Norse.Abstractions.Hosting` contract package plus the three concrete runtime packages (`Norse.Hosting.Web`, `Norse.Hosting.Worker`, `Norse.Hosting.Migrations.Service`), the three deployable hosts (`Norse.Hosting.Web.Server`, `Norse.Hosting.Worker`, `Norse.Hosting.Migrations.Service`), and the `Norse.Hosting.AppHost` Aspire wiring. (Out of scope for this spec; written next.)
