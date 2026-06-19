# UI Composition — Design (Norse.Infrastructure.UI.Composition + {Company}.Shell)

**Date:** 2026-06-05
**Status:** Draft for review
**Owner:** Buvy
**Supersedes:** `2026-05-19-ui-composition-design.md` (full supersession — every section rewritten; the old file is retained as history)
**Companion specs:** `2026-05-26-mediator-design.md` (doors, `I{Context}Api` law), `2026-05-21-midgard-persistence-design.md` (repository law), `2026-05-20-yggdrasil-hosting-design.md` (plugin runtime; its §15.1 amendment lands here), `2026-05-19-architecture-analyzers-design.md` (rule catalog)

> **Amended 2026-06-07 (error-vocabulary reconciliation, punch-list §1.6):** handler/API returns are the mediator's **`Outcome<T>`**, not Primitives' conversion `Result<T>` (§2.2, §5.2). The gRPC door maps the three trimmed `Outcome` categories (Validation/NotFound/Conflict); 401/403 are service-entry `[Authorize]`, 503/500 host-synthesized — none are `Outcome` values (§8.2). §8.3's client-side adapter is named the **Norse half of the render-table realm split** (it rebuilds the wire status into `Outcome<T>` so components stay channel-dumb). The catalog authz test asserts service-entry denial, not an `Err(Forbidden)` outcome (§10).

---

## 1. Motivation and Position

The insurance product needs a single composable UI surface that runs identically in three render targets:

1. **Blazor WASM** — the portal in a browser.
2. **Blazor Hybrid (MAUI)** — the native desktop/mobile shell, same Razor components inside a `BlazorWebView`.
3. **Blazor interactive server** — server-side circuits, used both as a production render mode and as the fast-iteration development surface.

The non-negotiable constraint: **one source of truth per component.** A billing widget is written once, in one project, and consumed by every render target. No parallel maintenance.

### The success criterion, stated as a persona

This spec succeeds when the front-end engineer — the pixel-perfect specialist who owns layout, CSS, and usability, and who has no interest in transports, persistence stances, or message buses — can build a complete feature by writing a request record, a response record, a validator, and a Razor component, injecting one interface, and never once asking "where does this run?" Every other concern belongs to somebody else's assembly, enforced by the compiler, not by review.

### What this spec owns, and what it consumes

| Concern | Owner |
|---|---|
| The component model (render modes, `I{Context}Api` binding, the per-context UI vertical) | **This spec** |
| Cross-cutting widget law (`Norse.Abstractions.Components`) | **This spec** |
| The composition framework (`Norse.Infrastructure.UI.Composition`) | **This spec** |
| The stitched app (`{Company}.Shell`) | **This spec** |
| Client hosting runtimes and deployable topology for UI | **This spec** (amends the hosting spec; debt queued in §13) |
| gRPC transport binding — proto authoring, adapters, generated client registration | **This spec** (fulfills the mediator §12 handoff and the hosting §15.1 amendment) |
| The three doors and `ErrorCategory` translation | Mediator spec §7 — consumed, never redefined |
| Repository contracts, Mongo/Postgres split, shim semantics | Persistence spec — consumed; this spec adds documented inversion #2 (§6) |
| Queue topology, endpoint flavors | Messaging spec — not touched; UI Composition has no queue presence |
| Plugin runtime (`IWebHostPlugin`), migrations orchestration | Hosting spec — consumed |

### What died with the old spec

The 2026-05-19 spec's organizing concept was a **three-stage component lifecycle** (in-process → gRPC → JSON) with graduation events. It conflated two orthogonal axes: *contract maturity* (a development-workflow concern) and *transport topology* (a deployment concern). Both axes now have real owners — the mediator's doors exist simultaneously from day one, and Blazor render modes make transport a per-component runtime fact. The stage vocabulary is retired. What survives of it is one workflow truth, recorded in §2.4: a contract iterates in-process until it stops churning; authoring the `.proto` is the crystallization act.

The JSON door is likewise out of scope entirely: it serves `client_credentials` third parties only, is owned by `Norse.Infrastructure.Api` + mediator §7, and **never surfaces into component code**. No component, widget, or client deployable ever sees JSON.

---

## 2. The Component Model

The spec's spine, replacing the three-stage lifecycle:

> A component declares **where it runs** (`@rendermode` — `InteractiveAuto`, `InteractiveWebAssembly`, `InteractiveServer`); the platform decides **how its data arrives**. The component injects `I{Context}Api` and nothing else. In a WASM or MAUI process, the container resolves the source-generated gRPC-Web client adapter; in a server circuit, it resolves the generated `{Context}Service` directly — same interface, zero serialization. The component cannot tell the difference, **by construction**. There is no graduation event, no stage, no lifecycle: the doors all exist on day one, and render mode is the only knob a component owns.

### 2.1 The per-context UI vertical

Restates mediator §3.4 from the UI's vantage. For every bounded context:

| Artifact | Assembly | Note |
|---|---|---|
| Request records, response records, **validators**, `I{Context}Api` | `{Company}.{Context}.Contracts` | Validators ride the client bundle: the same rules run in WASM/MAUI forms, server circuits, and the server pipeline. Client-side validation is courtesy; the pipeline run is law. |
| Widgets, routed pages, context-private building blocks | `{Company}.{Context}.Components` | Client-safe by construction (YGG003). |
| Handlers, authorizers, projections, `{Context}Plugin` | `{Company}.{Context}.Server` | Components never reference it; the compiler enforces the wall. |

One request model, one response model, one validator — written once, running everywhere the component runs.

### 2.2 The worked widget

```csharp
namespace {Company}.Billing.Components.Widgets;

[Widget(
	Id = "billing.summary",
	Title = "Billing Summary",
	Description = "Outstanding balance, last payment, next due.",
	Audience = new[] { Population.Customer },
	DefaultWidth = 4,
	DefaultHeight = 3)]
public partial class BillingSummaryWidget  // partial: Razor compiler demand (documented generator demand per CLAUDE.md §8)
{
	[Inject] private IBillingApi Billing { get; set; } = default!;
	[CascadingParameter] public WidgetContext Context { get; set; } = default!;

	private Outcome<BillingSummary>? summary;  // Err renders as a value — no throwing across the in-process door

	protected override async Task OnInitializedAsync()
		=> summary = await Billing.GetSummary(new GetBillingSummaryRequest(), CancellationToken.None);
}
```

Three rules baked into the example:

1. **Role-named shapes.** `BillingSummary` is a response record. "Dto" is banned vocabulary.
2. **`Outcome<T>` is consumed as a value.** An error category is a render state (`Err(NotFound)` draws an honest empty state), not an exception. Per the mediator §7 in-process door column, failure never throws across the boundary. (`Outcome<T>` is the mediator's application-result type — distinct from Primitives' conversion `Result<T>`; 2026-06-07 ruling.)
3. **No caller identity on the request.** `GetBillingSummaryRequest` carries no customer id — the handler resolves the subject from the principal server-side. A client-supplied identity field is an IDOR vulnerability with extra steps; the field's absence deletes the error class instead of validating it.

### 2.3 Catalog registration — source generation only

The `[Widget]` attribute and `@page` directive are scanned at **build time** by a source generator. Each `*.Components` assembly gets a generated `Add{Company}{Context}Ui()` extension registering its widgets and routed pages. There is no runtime assembly scan anywhere — the old spec's `WidgetCatalogScanner` (a startup reflection scan that contradicted the platform's own no-convention-scanning law, and contradicted the old spec's own §4) does not exist.

### 2.4 Contract iteration workflow (what survives of "stages")

A new contract iterates fastest in-process: run `Norse.Hosting.Web.Server` under Aspire, set the component `InteractiveServer`, and every contract change is a recompile + hot reload — no `.proto`, no client regen. When the contract stops churning, author the `.proto` (LLM-assisted, beside the interface — §8) and the WASM/MAUI doors light up. The component does not change at any point in this workflow; only artifacts beside it appear.

---

## 3. `Norse.Abstractions.Components` — Cross-Cutting Widget Law

Declared law; every `*.Components` assembly in the platform consumes it.

- **`IWidget`** — descriptor for a widget kind (id, title, default size, audience, capabilities).
- **`WidgetAttribute`** — decorates Razor components that participate in dashboard composition; the source generator scans for it. Gains **`Audience`**, expressed in the `Norse.Abstractions.Identity` population model (an array of `Population` values — the type relocated there by reconciliation ruling 1.2).
- **`IWidgetCatalog`** — the **authorization-aware** registry. It exposes only the widget kinds the current principal's population permits; `DashboardHost` can neither offer nor render outside it. The courtesy/law split is explicit: **catalog filtering is UX courtesy; the law is the door.** Every `I{Context}Api` call behind a widget is independently guarded by service-entry `[Authorize]` / `RequireAuthorization` — a hostile client that hand-crafts a hidden widget's request is denied (403 / `PermissionDenied`) **at the service boundary**, before dispatch, regardless of what the catalog showed it. Authorization is service-entry, never an `Outcome` value (mediator §3.3).
- **`IWidgetEventBus`** — **intra-UI pub/sub only.** Widget A publishes "user selected policy X"; widget B re-queries. It never leaves the rendering process, never crosses the gRPC boundary, and is not a substitute for the domain bus. **Explicit non-goal: server push.** Updates streaming *in* (connection registry, authorized fan-out, delivery semantics) belong to the future notifications spec (reconciliation tracker §4.1, Ratatoskr candidate). The component-facing shape that spec must design is "subscribe to an authorized server event stream, transport-blind" — in a server circuit the already-open SignalR circuit carries re-renders for free; in WASM/MAUI a gRPC server-stream drives the same state mutation. Nothing about that is this spec's surface; the boundary is recorded so nobody wedges push into the event bus.
- **`WidgetContext`** — cascading parameter carrying **`NorsePrincipal`** (the `Norse.Abstractions.Identity` envelope; name ruled 2026-06-05, closing the reconciliation 1.2 sub-point — the `Norse` prefix marks platform-wide concepts per the `NorseTier` precedent), slot identity, and the slot's opaque config. No bare identity strings.

**Registration lifetime rule (load-bearing):** `IWidgetEventBus` registers **scoped, never singleton**. In WASM a scope is effectively the app, so behavior is unchanged; in server circuits a singleton bus would broadcast one user's selections into other users' dashboards — a cross-user data leak killed by one DI lifetime word. The framework's registration extension owns this; component authors never register the bus.

---

## 4. `{Company}.Shell` — The Stitched App

*Hlidskjalf: Odin's high seat, from which all realms are visible at once. The composed dashboard overlooking every bounded context.*

The platform ships **one** stitched app — a single client-safe assembly composing cross-cutting auth components and every context's UI, shipped to WASM and MAUI, inherited by server circuits via project reference.

### 4.1 Contents

**`{Company}.Shell.Components`** — the only assembly. App root (`App.razor`, router, `MainLayout`), the dashboard pages hosting `DashboardHost`, and the stitching. References `Norse.Infrastructure.UI.Composition.Components`, `Norse.Auth.Components`, and every `{Company}.{Context}.Components`. Components-tier law (YGG003) applies to it like any other Components assembly: it is client-safe by construction and can never reach a `.Server`, `.Worker`, or `.Backend` assembly.

It is MGA-semantic cross-cutting (it knows every context's UI exists), so it lives in the product realm per CLAUDE.md §5 — it is not a bounded context (no data, no server, no worker; pure composition). Repo: `{company}-shell` submodule. `codenames.md` gains the entry in the same PR as this spec.

### 4.2 The two extensions

| Extension | Job | Called by |
|---|---|---|
| `AddShell()` | Composition: every context's generated `Add{Company}{Context}Ui()` + framework registration (catalog, event bus, dashboard primitives) | All three hosts — `Norse.Hosting.Web.Server`, `Norse.Hosting.Web.Client`, `Norse.Hosting.App` |
| `AddShellClients(apiBase)` | The client door: aggregates the source-generated `Add{Company}{Context}Client(apiBase)` adapters (§8) | **WASM/MAUI `Program.cs` only.** `Norse.Hosting.Web.Server` never calls it — its container already holds the real `{Context}Service` instances via plugins. The in-process door is the *absence* of client registration, not a third registration flavor |

### 4.3 The single-app trade-off, and its re-entry trigger

**Decided 2026-06-05: single app, authorization-filtered.** One assembly serves every population (staff, producer, customer); the authorization-aware catalog (§3) and standard route authorization decide what each principal sees. The accepted cost: the full bundle ships to every audience — customer browsers download staff widget code, which is both payload weight and a quiet disclosure of internal operations.

**Re-entry trigger:** a compliance finding on bundle disclosure, or customer-portal load weight materially hurt by back-office payload, reopens this as per-population composition assemblies (`{Company}.Shell.{Population}.Components` or successors). Because Shell is pure stitching, that split is mechanical — N thin assemblies replacing one, zero framework change — and the framework design keeps it that way deliberately: nothing in `Norse.Infrastructure.UI.Composition` may assume a single app assembly.

---

## 5. `Norse.Infrastructure.UI.Composition` — The Framework

### 5.1 The project-shape law

> **A context ships only the projects its persistence stance demands.** `.Backend` exists iff `.Server` and `.Worker` both exist — it is their shared surface, and it is **never client-reachable** (analyzer rule queued to the YGG catalog; §9). No worker → no `.Backend`, no `.Worker`, no `.Migrations`.

UI Composition's stance is Mongo-as-system-of-record (§6): no worker exists, so the framework ships **three projects**:

| Project | Contents |
|---|---|
| `Norse.Infrastructure.UI.Composition.Contracts` | `ILayoutApi` (`[MediatorService]`, YGG401 shape), request/response records (`GetLayoutRequest`, `SaveLayoutRequest`, `LayoutModel`, `LayoutSlot`, `GridPosition`, `LayoutSaved`), validators (ride the client bundle) |
| `Norse.Infrastructure.UI.Composition.Components` | `DashboardHost.razor`, `WidgetSlot.razor`, `WidgetCatalogProvider`, drag/drop + grid-snap primitives (pointer events, keyboard navigation, ARIA), `MissingWidgetSlot`, **the `IWidgetEventBus` default impl** (`Channel<T>`-backed, scoped — §3) |
| `Norse.Infrastructure.UI.Composition.Server` | `GetLayoutHandler` / `SaveLayoutHandler`, **`LayoutDocument` (`internal` — no `.Backend` exists to put it in)**, projections, `UiCompositionPlugin : IWebHostPlugin` registering the layout gRPC surface and DI on `Norse.Hosting.Web.Server` like any context plugin |

The framework is Infrastructure-tier (embodied law, platform infrastructure, MGA-agnostic) — it knows *that* widgets exist, never *what* they show. No Norse codename: codenames are for opaque cross-cutting services; this is concrete enough to name plainly.

### 5.2 The layout API on mediator law

`ILayoutApi` follows YGG401 exactly — `ValueTask<Outcome<T>>`, one request parameter, `CancellationToken` required, `[MediatorService]` only:

```csharp
namespace Norse.Infrastructure.UI.Composition.Contracts;

[MediatorService]
public interface ILayoutApi
{
	ValueTask<Outcome<LayoutModel>> GetLayout(GetLayoutRequest request, CancellationToken cancellationToken);
	ValueTask<Outcome<LayoutSaved>> SaveLayout(SaveLayoutRequest request, CancellationToken cancellationToken);
}
```

`SaveLayoutRequest` is an `ICommandRequest<LayoutSaved>` — its validator (slot-grid integrity, schema version, config size bounds) is mandatory per YGG406 and rides the client bundle. **Neither request carries an owner field** (§6.2).

---

## 6. Layout Persistence — Documented Inversion #2

### 6.1 The inversion and its rationale

**Decided 2026-06-05: Mongo is the system of record for user layouts. No Postgres surface exists.**

This is the platform's second documented inversion of the Postgres-is-system-of-record default, and it argues from a different premise than the first:

> Auth inverted because a queue cannot sit in the login path. UI Composition inverts because **there is no system of record to protect — a dashboard layout is not an insurance fact.** No audit trail, no reporting value, no bordereaux relevance, nothing for Warehouse, no regulatory retention. The Mongo document *is* the record.

Mechanics: `IDocumentRepository<LayoutDocument>` from `.Server`. The save is an **authoritative durable write** — explicitly *not* a shim awaiting worker enrichment. `LayoutDocument` carries no `ProcessingStatus` block; there is no command, no queue hop, no worker, no Postgres projection, no migrations assembly.

### 6.2 Identity from the principal, period

Requests carry no owner field. Handlers resolve the subject from the platform principal server-side. The old spec's `LayoutModel.OwnerId : string` is doubly dead: client-supplied (IDOR class) and stringly-typed (CLAUDE.md §8).

### 6.3 Document shape

```csharp
namespace Norse.Infrastructure.UI.Composition.Server;

internal sealed record LayoutDocument
{
	public required Guid Id { get; init; }              // UUID v5 over (principal subject, layout name)
	public required Guid Subject { get; init; }          // principal subject — resolved server-side, never wire-supplied
	public required string LayoutName { get; init; }
	public required int SchemaVersion { get; init; }
	public required IReadOnlyList<LayoutSlot> Slots { get; init; }
	public required DateTimeOffset UpdatedAt { get; init; }
}
```

- **Deterministic `_id`:** UUID v5 over (principal subject, layout name) — the platform's existing identity discipline. Idempotent upsert falls out of the key derivation; a retried save is structurally harmless. The namespace registers in the Primitives UUID v5 registry when it lands.
- **`SchemaVersion` is mandatory.** Loaders upgrade older versions to current; saves always write current.
- **Widget removal handling (kept from the old spec — still correct):** a stored `LayoutSlot.WidgetId` no longer in the catalog logs at `Warning` and renders as `MissingWidgetSlot` — a polite "this widget is no longer available — remove it?" panel. No silent drop (silent data loss), no crash (hostile to the user).
- **Role-template fallback (kept):** load resolves `(subject, name)` → population template → empty default. Template authoring tooling remains deferred to a follow-on spec.
- **No TTL.** Layouts persist until the user changes them; this is not the TTL-churn class the Auth split sent to Mongo for expiry semantics.

### 6.4 The durable-resume-state law

The layout store is the first instance of a platform law, not a special case:

> **Durable resume state — anything a user expects to survive the session and follow them across devices — is a context-owned Mongo document behind that context's API.** A dashboard layout is UI Composition's instance. A half-finished quote is Underwriting's instance. There is no generic "UI state bag," no platform user-state service, and no client-side-only storage for anything that must survive the device.

Ephemeral in-session state (selected tab, expanded panel, cross-widget coordination) is component state + `IWidgetEventBus`. **The state-management-library decision (Fluxor et al.) is deferred** with a documented re-entry trigger: the first screen that demonstrably outgrows event-bus coordination — most plausibly a multi-widget quote wizard — opens the decision. Fluxor's reflection-based feature/reducer discovery is pre-flagged as against the platform's source-generation grain.

---

## 7. Deployables and Hosting Topology

### 7.1 Deployables

| Deployable | Role | References |
|---|---|---|
| `Norse.Hosting.Web.Server` | **Gains the Blazor Web App surface:** SSR + interactive-server circuits for Shell, serves the WASM bundle, gRPC, gRPC-Web, partner JSON door — one process, scale by replicas. The single-deployable doctrine extends to the web UI | `{Company}.Shell.Components`, `Norse.Hosting.Web.Server`, every context plugin |
| `Norse.Hosting.Web.Client` | The `.Client` half of the web app. Thin `Program.cs`: `AddShell()` + `AddShellClients(apiBase)` | `{Company}.Shell.Components`, `Norse.Hosting.Web.Client` |
| `Norse.Hosting.App` | Native shell, `BlazorWebView`. Same two calls in `MauiProgram.cs` | `{Company}.Shell.Components`, `Norse.Hosting.App` |
| `Yggdrasil.DevServer` | **Deleted.** Superseded by `InteractiveServer` render mode on `Norse.Hosting.Web.Server` under Aspire — its entire reason to exist (in-process iteration with hot reload) is now a render-mode keyword | — |

Client deployables remain Components-only — never `.Server`, `.Worker`, or `.Backend` (§9). The server side of every circuit lives in `Norse.Hosting.Web.Server`, which is precisely why the in-process door is zero-serialization: the circuit's DI container holds the real generated `{Context}Service`s.

### 7.2 The hosting-runtime family

| Runtime assembly | Status | Contents |
|---|---|---|
| `Norse.Hosting.Web.Server` | **Renamed from `Norse.Hosting.Web.Server`** | The existing web-host runtime (plugin loading, endpoint conventions, middleware) — name now carries which half of the pair it is |
| `Norse.Hosting.Web.Client` | **New** | Client-side platform plumbing: gRPC-Web channel factory, OIDC token attach, client OTel (`norse.stamp` resource attribute), generated-client registration conventions |
| `Norse.Hosting.App` | **New** | `BlazorWebView` bootstrap specifics. Unadorned name: the `.Server`/`.Client` suffixes disambiguate halves of a *pair*, and MAUI is unpaired (single-process, client-only by construction). References `Norse.Hosting.Web.Client` for channel/auth plumbing — MAUI deliberately runs gRPC-Web for parity (§8), so "Web" in that dependency reads as *talks to the web host* |
| `Norse.Hosting.Worker` / `Norse.Hosting.Migrations.Service` | Unchanged | — |

The old spec's `Yggdrasil.Hosting.Wasm`, `Norse.Hosting.App` (as previously sketched), and `Yggdrasil.Hosting.Composition` are erased from the topology — the first two are replaced by the family above; the third's one job (aggregating widget registration) is Shell's reason to exist.

### 7.3 Deployment note — circuits and scale

Interactive-server circuits require **session affinity** at the ingress. Azure Container Apps supports sticky sessions; the interplay between sticky circuits and KEDA scale rules (scale-in disconnecting live circuits) is a deployment-time verification item, recorded in §11 — not hand-waved here.

---

## 8. Transport — Native gRPC Stack

This section lands the hosting spec's §15.1 amendment (queued 2026-05-20) and fulfills the mediator §12 handoff ("proto-message ↔ request/response mapping is specified in the UI Composition amendment").

### 8.1 The stack

**protobuf-net.Grpc is rejected** (resolved decision, restated from the hosting spec: AOT roadmap risk does not warrant the lower friction). The platform uses the native stack: **`Grpc.AspNetCore`** server-side; **`Grpc.Net.Client.Web` + `Google.Protobuf`** on clients. gRPC-Web is the client transport for **both WASM and MAUI** — single transport path, eliminating the "works in MAUI, broken in WASM" bug class (decision carried forward from the old spec).

### 8.2 Server side

The `.proto` file is authored beside `I{Context}Api` (LLM-assisted authoring from the interface; the C# interface remains the consumer-facing abstraction). `Grpc.AspNetCore` generates `{Service}Base`; a thin adapter in `.Server` wraps it and delegates to the generated `{Context}Service`:

- `Ok` → proto response message.
- `Err` → `RpcException` per the mediator §7 door table — the **three** `Outcome` categories only: `Validation` → `InvalidArgument` + `google.rpc.BadRequest` field violations; `NotFound` → `NotFound`; `Conflict` → `Aborted`. (401/403 are produced by the service-entry `[Authorize]` as `Unauthenticated` / `PermissionDenied`; 503/500 by the host pipeline as `Unavailable` / `Internal` — none are `Outcome` values, mediator §7.)
- Proto-message ↔ request/response record mapping is the adapter's job, source-generated where reductionary (member-name + type match, same discipline as projections), hand-written where shapes genuinely diverge.

### 8.3 Client side

The source generator emits **`Add{Company}{Context}Client(apiBase)`** per context: registers the `Google.Protobuf`-generated client over a gRPC-Web channel (from `Norse.Hosting.Web.Client`'s channel factory) plus the adapter implementing `I{Context}Api`. The component injects `I{Context}Api`; the adapter translates records ↔ proto messages and **rebuilds the gRPC status back into an `Outcome<T>`** (inverse of §8.2 — the three `Outcome` categories round-trip). This client-side rebuild is the **Norse half of the mediator §7 render-table realm split**: the component consumes `Outcome<T>` whether it ran in-process (Blazor Server) or over the wire (WASM/MAUI), and stays dumb about transport. Auth/transport statuses (401/403/503/500) are *not* `Outcome` values — the client surfaces them as auth or connectivity states, never render states.

`AddShellClients(apiBase)` aggregates these per-context calls for the client deployables (§4.2).

### 8.4 JSON — non-applicability

The JSON door (`JsonControllerBase<TService>` in `Norse.Infrastructure.Api`, RFC 9457 mapping) serves `client_credentials` third parties only. It is owned by the mediator and hosting specs and **never surfaces into component code, client deployables, or this framework**. The old spec's inline `JsonControllerBase` (which caught `RpcException` and mapped gRPC status codes — a transcoding shape the door table killed) is fully superseded.

---

## 9. Enforcement

Rules this architecture consumes (defined in the analyzers spec):

- **YGG003** — `*.Components` cannot reference Infrastructure-tier assemblies, in either direction: `{Company}.Billing.Components` ↛ `{Company}.Billing.Server`, and `{Company}.Billing.Server` ↛ any `*.Components`. The bundle-safety rule.
- **YGG004** — no cross-context internal references. Components may reference any context's `Contracts`; never its `Server`/`Worker`/`Backend`.
- **YGG005/YGG006** — no `InternalsVisibleTo` to non-test assemblies.
- **YGG101** — wire shapes obey the strict string law; widget-facing response records included.

Rule this spec spawns (queued to the reconciliation tracker's 2.10 catalog absorption pass):

- **`.Backend` is never client-reachable** — any `*.Components` assembly or client deployable (`Norse.Hosting.Web.Client`, `Norse.Hosting.App`) referencing a `*.Backend` assembly is a build error. Codifies the law that `.Backend` is exclusively the `.Server`/`.Worker` shared surface.

Structural (no analyzer needed — the compiler is the analyzer): the project-shape law (§5.1) is enforced by the projects simply not existing; cross-context queries from layout handlers are type-system impossible per the persistence spec.

---

## 10. Testing

- **bUnit** for component rendering (`DashboardHost`, `WidgetSlot`, drag/drop, `MissingWidgetSlot`); xUnit for everything non-rendering.
- **Shouldly** assertions, **NSubstitute** doubles, per platform law.
- Layout handlers unit-test with substituted `IDocumentRepository<LayoutDocument>`.
- The repository path itself integration-tests against **real Mongo** (testcontainers) — no mocked-DB tests for behavior that depends on database semantics (upsert idempotency, the deterministic-`_id` derivation).
- Catalog filtering tests assert both directions: permitted widgets present, non-permitted absent — and a door-level test asserts the **service-entry `[Authorize]` denies** (403 / `PermissionDenied`) a hand-crafted request against a non-permitted widget's API (the courtesy/law split, verified at both layers — authorization is service-entry, never an `Outcome` value; mediator §3.3).

---

## 11. Open Questions

1. **Component library: Blazorise, FluentUI, or hand-rolled** (carried from the old spec, still open, still gates code rather than spec). Coupled to the design-system question; owner needs design expertise the project doesn't currently have — flagged for hiring/consulting.
2. **MAUI handling of server-only components.** The MAUI shell can only host `Auto`/`Client` render modes natively; a component that is `InteractiveServer`-only would need fetching via route. Parked by explicit decision 2026-06-05 — needs its own debate when MAUI work begins.
3. **Circuit affinity × KEDA.** Verify sticky-session behavior under scale-in on Container Apps before production traffic rides interactive-server circuits (§7.3).

---

## 12. Resolved Decisions (2026-06-05 session)

| # | Decision |
|---|---|
| 1 | **Layouts: Mongo as system of record** — documented inversion #2; no Postgres surface, no worker, no migrations (§6) |
| 2 | **`.Backend` never-client law codified** → analyzer rule queued; corollary: no worker → no `.Backend`. UI Composition ships `Contracts / Components / Server` (§5.1) |
| 3 | **Single stitched app, authorization-filtered**; re-entry trigger to per-population split documented (§4.3) |
| 4 | **`{Company}.Shell`** is the stitched app's home — codename assigned, registry updated same PR (§4) |
| 5 | **Durable-resume-state law** codified; state-library decision deferred with re-entry trigger (§6.4) |
| 6 | **JSON door exits UI scope** — `client_credentials` third parties only; never component-facing (§1, §8.4) |
| 7 | **Render modes replace stages** — `I{Context}Api` constant, DI container variable; zero serialization on the server door (§2) |
| 8 | **`Norse.Hosting.Web.Server` hosts the Blazor Web App; `DevServer` deleted** (§7.1) |
| 9 | **`Norse.Hosting.Web.Server` / `Web.Client` / `Maui`** runtime family; `Hosting.Web` renamed for pair clarity (§7.2) |
| 10 | **`NorsePrincipal`** is the platform principal's name — closes the reconciliation 1.2 open sub-point; `WidgetContext` carries it (§3) |

Carried forward intact from the superseded spec: gRPC-Web for both WASM and MAUI; MAUI uses Blazor Hybrid (`BlazorWebView`), not native XAML; layout-as-a-unit document storage (was jsonb rationale, now Mongo document rationale — same argument); role-template hybrid layout defaults; `MissingWidgetSlot` semantics; schema versioning; hot reload as a first-class requirement (now trivially satisfied by `InteractiveServer` under Aspire, but the spike stays on Done Criteria).

---

## 13. Amendment Debt Spawned by This Spec

Queued to `docs/spec-reconciliation-2026-06-04.md` as new §2 items — not silently absorbed:

1. **CLAUDE.md** — §4 Hosting (DevServer deleted; Host gains Blazor Web App surface); §5 deployables list and `Norse.Hosting.*` assembly list (`Web.Server` rename, `Web.Client`, `Maui`); product realm list + submodule list gain Shell; `.Backend` row wording gains "never client-reachable (analyzer-enforced)"; Mongo bullet notes inversion #2.
2. **Hosting spec** — `Norse.Hosting.Web.Server` definition gains the Blazor Web App surface; `Norse.Hosting.Web.Server` rename ripples through §3/§5/§9/§12; §15.1 marked **applied here**.
3. **`codenames.md`** — Hlidskjalf entry (done in the same change set as this spec, per registry rule 4).
4. **`project-structure.md` / `decomposition.md`** — deployables catalog (DevServer removed), submodule table (`{company}-shell` added; `norse-hosting-clients` contents updated), project-shape law (`.Backend` optionality).
5. **Analyzers spec** — the 2.10 catalog-absorption pass gains the `.Backend` never-client rule.
6. **Auth spec** — confirm `Norse.Auth.Components` is declared in its assembly list (Shell references it; if the auth spec never named it, it gains a line).

---

## 14. Done Criteria

This spec is "done enough to implement" when:

- [ ] Open question §11.1 (component library / design system) is resolved — one decision, gates code.
- [ ] The analyzers spec's 2.10 pass has absorbed the `.Backend` never-client rule, making §9 fully concrete.
- [ ] A spike runs one widget through `InteractiveAuto` end-to-end: server-circuit render via the in-process `{Context}Service`, then WASM takeover via the generated gRPC-Web client adapter — confirming the container-resolves-the-door model works as designed, not just as drawn.
- [ ] A spike validates hot reload across widget + handler code under `Norse.Hosting.Web.Server` + Aspire with `InteractiveServer` (the DevServer-replacement claim, verified).
- [ ] The circuit-affinity × KEDA verification (§11.3) has a recorded answer.
