# Norse.Infrastructure.UI.Composition — Design

**Date:** 2026-05-19
**Status:** SUPERSEDED — retained as history only
**Superseded by:** `2026-06-05-ui-composition-design.md` (full supersession, 2026-06-05: render modes replaced the three-stage lifecycle; layout persistence inverted to Mongo-as-system-of-record; {Company}.Shell introduced; native gRPC stack applied; DevServer deleted)
**Owner:** Buvy
**Supersedes:** none
**Companion spec:** `2026-05-19-architecture-analyzers-design.md` (the analyzer rules referenced below are defined there)

---

## 1. Motivation

The insurance product needs a single composable UI surface that runs identically in three places:

1. **Blazor WASM** — the customer/producer portal in a browser.
2. **Blazor Hybrid (MAUI)** — the native desktop/mobile app, with the same Razor components rendered inside a `BlazorWebView`.
3. **Blazor Server** — initial development mode for components whose data contracts are still in flux.

The non-negotiable constraint: **one source of truth per component.** A billing widget, a claims widget, a policy summary widget — each one is written once, in one project, and consumed by all three runtimes. No parallel maintenance. No "the MAUI version is six weeks behind the WASM version."

A second constraint is layering: services must not be able to depend on UI by accident, and widgets must not be able to depend on a service's internals. UI is not "above" the business contexts and is not "below" them — it is **peer** to them, structurally identical to any other bounded context, with its own Abstractions/Contracts/Application/Infrastructure layout.

A third constraint, deliberately chosen: components should be able to ship initially in Blazor Server mode against an in-process service adapter, then graduate to gRPC over the wire once their data contract settles, then optionally graduate to a JSON adapter for partners who cannot consume protobuf. The component code does not change across these three stages. Only the transport binding does.

This spec defines the architecture that satisfies all three constraints.

## 2. The Three-Stage Component Lifecycle

The defining feature of the insurance product's UI layer is that the **same component source code** runs through three progressively-decoupled transports.

### Stage 1 — Contract Inception (Blazor Server, in-process)

Purpose: iterate fast while the data contract is still settling.

- The component lives in `{Company}.Billing.Components` and renders server-side under Blazor Server.
- The component injects `IBillingApi` — a contract interface declared in `{Company}.Billing.Contracts`.
- The Blazor Server host (`Yggdrasil.DevServer`, or `Norse.Hosting.Web.Server` in monolith deployment) registers `IBillingApi` against an **in-process adapter** that calls the service implementation in `{Company}.Billing.Server` directly. The adapter lives in the host project, not in the UI project.
- Round-trip is a method call. No serialization. No network. No `.proto` files to update. The contract changes, the adapter recompiles, hot reload picks it up.

This is the development mode. It is also a perfectly valid production deployment for any context that ships as a monolith — the in-process adapter is not a hack, it is the simplest possible implementation of `IBillingApi`.

### Stage 2 — Contract Crystallization (gRPC via protobuf-net.Grpc)

Purpose: lock the wire contract once it stops churning.

- `IBillingApi` is decorated with `[ServiceContract]` (from `protobuf-net.Grpc`) and its members with `[OperationContract]`. The C# interface **is** the protobuf contract — no separate `.proto` file, no parallel type hierarchy from a `protoc` codegen pass, no schema language to keep in sync with C#. The single-source-of-truth principle that justifies one component code path across runtimes also justifies one contract definition across transports.
- The server hosts a gRPC service that implements `IBillingApi` and delegates to the Application layer.
- WASM clients connect via **gRPC-Web** (the binary protobuf transport with HTTP/1.1 framing, supported in browsers).
- MAUI clients can use gRPC-Web (same code path as WASM) or native HTTP/2 gRPC if the platform supports it.
- The component code is unchanged. Only the DI registration in the shell changes: instead of an in-process adapter, `IBillingApi` resolves to a `protobuf-net.Grpc` client proxy targeting the right channel.

### Stage 3 — Partner Compatibility (JSON over HTTP, additive)

Purpose: expose the same contract to partners who cannot consume protobuf.

Stage 3 **does not transcode** gRPC. It adds a parallel JSON front door to the same in-process service. No third-party transcoding library, no gRPC-frame-to-JSON translation, no double-marshalling. We own the lifecycle.

The pattern:

- The service implementing `IBillingApi` is a POCO C# class registered in DI. The gRPC host routes incoming gRPC calls to it via `protobuf-net.Grpc`. This is unchanged from Stage 2.
- A controllers project (`{Company}.Billing.JsonApi`) defines ASP.NET Core controllers that derive from `JsonControllerBase<IBillingApi>`. Each controller injects the **same** `IBillingApi` instance via DI — the gRPC host and the JSON controllers are two front doors to one service object.
- The base class is a higher-order-function wrapper that concentrates the cross-cutting concerns (error mapping, cancellation, telemetry, response envelope) in one place. Each controller method is a route attribute plus a one-line `InvokeAsync(svc => svc.Method(...))` call.

A worked controller:

```csharp
[ApiController]
[Route("api/v1/billing")]
public sealed class BillingController(IBillingApi billing)
	: JsonControllerBase<IBillingApi>(billing)
{
	[HttpPost("summary")]
	public Task<IActionResult> GetSummary(GetBillingSummaryRequest request, CancellationToken ct)
		=> InvokeAsync(svc => svc.GetSummaryAsync(request, ct), ct);

	[HttpGet("invoices/{customerId}")]
	public Task<IActionResult> ListInvoices(string customerId, CancellationToken ct)
		=> InvokeAsync(svc => svc.ListInvoicesAsync(new ListInvoicesRequest { CustomerId = customerId }, ct), ct);
}
```

The base class:

```csharp
public abstract class JsonControllerBase<TService>(TService service) : ControllerBase
	where TService : class
{
	protected TService Service { get; } = service;

	protected async Task<IActionResult> InvokeAsync<TResult>(
		Func<TService, Task<TResult>> action,
		CancellationToken cancellationToken)
	{
		try
		{
			var result = await action(Service);
			return Ok(result);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return StatusCode(StatusCodes.Status499ClientClosedRequest);
		}
		catch (RpcException rpc)
		{
			return MapRpcStatusToHttp(rpc.Status);
		}
		// Unhandled exceptions intentionally propagate to ASP.NET's exception
		// pipeline. A catch-all swallow here would violate §2.7 (fail loudly).
	}

	protected Task<IActionResult> InvokeAsync(
		Func<TService, Task> action,
		CancellationToken cancellationToken)
		=> InvokeAsync<Unit>(async svc => { await action(svc); return default; }, cancellationToken);

	private static IActionResult MapRpcStatusToHttp(Status status) => status.StatusCode switch
	{
		StatusCode.NotFound           => new NotFoundObjectResult(status.Detail),
		StatusCode.InvalidArgument    => new BadRequestObjectResult(status.Detail),
		StatusCode.PermissionDenied   => new ForbidResult(),
		StatusCode.Unauthenticated    => new UnauthorizedResult(),
		StatusCode.AlreadyExists      => new ConflictObjectResult(status.Detail),
		StatusCode.FailedPrecondition => new UnprocessableEntityObjectResult(status.Detail),
		_                             => new ObjectResult(status.Detail) { StatusCode = StatusCodes.Status500InternalServerError },
	};
}
```

Why this design rather than transcoding:

1. **One marshalling pass per door.** Wire bytes → POCO (protobuf decode at the gRPC door, `System.Text.Json` deserialize at the HTTP door) → service method → POCO → wire bytes. The two protocols never touch each other. Transcoding libraries fail in exactly the scenario where they do touch — double-allocation, double-validation, double-error-shape, and the boundary owns nothing.
2. **We own the lifecycle.** Error mapping, cancellation semantics, response envelope, observability, auth integration — all explicit, all in this repo, all changeable without waiting for an upstream library release or worrying about an upstream that stops shipping.
3. **No additional dependency surface.** ASP.NET Core controllers are already in the platform. `JsonControllerBase` is ~50 lines. There is no library version to track, no transcoding behavior to learn, no edge cases where the library disagrees with our intent.
4. **AOT-friendly and trim-clean.** No reflection in the hot path, no dynamic dispatch. The HOF closure resolves at compile time. `System.Text.Json` source generators handle serialization.
5. **Both front doors share authn/authz.** Same ASP.NET Core process, same auth pipeline. A claims-principal authenticated for a JSON request is the same principal the service method sees as if the call had come over gRPC.
6. **One-way out, by design.** Partners consume JSON. Internal clients (WASM, MAUI, internal services) always use gRPC. We do not bring partner-shaped JSON requests *back* into the system through the gRPC path — partners hit the JSON door, the in-process service runs, JSON goes out.

`JsonControllerBase` is reusable across every context — every `*.Contracts` interface that is published externally gets a JSON-API project of the same shape. The pattern is a candidate for its own focused spec when it becomes a workload of its own; for now it lives here because Stage 3 of the UI composition lifecycle depends on it.

### Why this lifecycle works

Three observations make this pattern viable rather than a fairy tale:

1. **`protobuf-net.Grpc` lets the C# interface be the contract.** There is no schema language to learn, no `.proto` file to keep in sync, no codegen step that produces a parallel type hierarchy. The `IBillingApi` you injected in Stage 1 is the same `IBillingApi` you connect to in Stage 2.
2. **Blazor Server, WASM, and Hybrid share the same component runtime model.** A Razor component that compiles for one compiles for the others, as long as it injects against interfaces (not concrete services) and avoids runtime-specific APIs (no `IJSRuntime.InvokeAsync` for WASM-only browser APIs in a way that breaks MAUI, etc.).
3. **Graduation is a DI-registration change, not a code change.** The component asks for `IBillingApi`. Stage 1 gives it an in-process adapter. Stage 2 gives it a gRPC client. Stage 3 doesn't change anything for our own clients — it adds an outbound JSON face for partners.

## 3. Architecture: UI as a Peer Bounded Context

The composition framework is its own bounded context — call it `UI.Composition` — structurally identical to Billing, Claims, Policy. It has the same three per-context assemblies every product bounded context has: `Contracts`, `Components`, `Infrastructure`. It is **not** above the business contexts; it sits beside them. The cross-cutting widget-shape contracts (`IWidget`, `WidgetAttribute`, `IWidgetCatalog`, `IWidgetEventBus`) live in the platform-tier `Norse.Abstractions.Components` library (declared law) — they're consumed by every `*.Components` assembly across the platform, including this framework's own.

```
       ┌──────────────────────┐    ┌──────────────────────┐
       │ Norse.Hosting.Web.Client   │    │ Norse.Hosting.App   │   ← thin Program.cs entry points
       │ (Blazor WASM)        │    │ (Blazor Hybrid)      │     atop Yggdrasil.Hosting.{Wasm|Maui}
       └────────┬─────────────┘    └────────────┬─────────┘
                │                                │
                └─────────────┬──────────────────┘
                              │ (also: Yggdrasil.DevServer — Blazor Server dev host, atop Norse.Hosting.Web.Server)
                              │
              ┌───────────────▼────────────────┐
              │  Norse.Infrastructure.UI.Composition.*      │   ← peer bounded context (3 assemblies)
              │                                │
              │  Contracts: IWidgetLayoutApi,  │   ← gRPC-able layout API,
              │    LayoutModel, LayoutSlot,    │     wire-shape layout records,
              │    IWidgetHost                 │     host abstraction
              │                                │
              │  Components: DashboardHost,    │   ← Razor: dashboard root,
              │    WidgetSlot, drag/drop       │     widget slot, drag/drop
              │    primitives, layout grid     │     primitives
              │                                │
              │  Infrastructure: layout        │   ← load/save/validate layouts;
              │    service, EfCoreWidget-      │     EF Core user-scoped store;
              │    LayoutStore, catalog scan   │     [Widget]-scanning catalog
              └────────────────────────────────┘
                              ▲
                              │  (also depends on Norse.Abstractions.Components
                              │   for IWidget / WidgetAttribute / IWidgetCatalog /
                              │   IWidgetEventBus — the cross-cutting widget shape)
              ┌───────────────┼────────────────┬───────────────┐
              │               │                │               │
      ┌───────┴───────┐ ┌─────┴───────┐ ┌──────┴───────┐ ┌────┴─────────┐
      │ {Company}.    │ │ {Company}.  │ │ {Company}.   │ │ {Company}.   │
      │ Billing.      │ │ Claims.     │ │ Policy.      │ │ Customer.    │
      │ Components    │ │ Components  │ │ Components   │ │ Components   │
      │ widgets +     │ │ widgets +   │ │ widgets +    │ │ widgets +    │
      │ routed pages  │ │ routed pages│ │ routed pages │ │ routed pages │
      └───────┬───────┘ └─────┬───────┘ └──────┬───────┘ └────┬─────────┘
              │               │                │              │
              ▼               ▼                ▼              ▼
       ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌────────────┐
       │ {Company}. │  │ {Company}. │  │ {Company}. │  │ {Company}. │
       │  Billing.  │  │  Claims.   │  │  Policy.   │  │  Customer. │
       │  Contracts │  │  Contracts │  │  Contracts │  │  Contracts │
       │  (IApi +   │  │  (IApi +   │  │  (IApi +   │  │  (IApi +   │
       │   shapes)  │  │   shapes)  │  │   shapes)  │  │   shapes)  │
       └────────────┘  └────────────┘  └────────────┘  └────────────┘
```

### What each layer holds

#### `Norse.Abstractions.Components` (cross-cutting; not framework-internal)

The cross-cutting widget-shape contracts every `*.Components` assembly in the platform consumes:

- `IWidget` — descriptor for a widget kind (id, title, default size, capabilities).
- `WidgetAttribute` — decorates Razor components that participate in dashboard composition; the source generator scans for it.
- `IWidgetCatalog` — runtime registry of available widget kinds.
- `IWidgetEventBus` — in-process pub/sub for cross-widget notifications (see §7).

These types live in the shared platform-level abstractions library, not in this framework. Every business context's Components assembly (`{Company}.Billing.Components`, `{Company}.Claims.Components`, …) references `Norse.Abstractions.Components` to consume them. The UI Composition framework references it for the same reason.

#### `Norse.Infrastructure.UI.Composition.Contracts`

The framework's public face — types other contexts can see and consume:

- `IWidgetLayoutApi` — `[ServiceContract]`, decorated for `protobuf-net.Grpc`; methods to load and save a user's layout. Hosted on `Norse.Hosting.Web.Server` alongside every other context's gRPC service (see §12.7).
- `LayoutModel` — serializable model of "this user's dashboard": which widgets, where, what size, what config. Used as both the in-memory and wire shape.
- `LayoutSlot` — a single positioned widget instance.
- `GridPosition` — column/row/width/height for a slot.
- `IWidgetHost` — host abstraction a runtime implements to render widgets in a layout. (One implementation per runtime: Blazor Server, WASM, MAUI Hybrid — all in `Norse.Infrastructure.UI.Composition.Components`.)

#### `Norse.Infrastructure.UI.Composition.Components`

Razor components that render the dashboard itself:

- `DashboardHost.razor` — the root component. Takes a `LayoutModel`, renders the grid, handles drag/drop, raises events when the layout changes.
- `WidgetSlot.razor` — renders a single widget given its `LayoutSlot` and looks up the component type from `IWidgetCatalog`.
- `WidgetCatalogProvider.razor` — cascading-value provider that makes the catalog available to descendants.
- Drag/drop primitives — pointer event handling, grid snap math, accessibility (keyboard navigation, ARIA).
- Runtime implementations of `IWidgetHost` for the Blazor Server / WASM / MAUI Hybrid surface.

This assembly references `Norse.Infrastructure.UI.Composition.Contracts`, `Norse.Abstractions.Components`, and Blazor types (`Microsoft.AspNetCore.Components`). It does **not** reference any business context's internals.

#### `Norse.Infrastructure.UI.Composition.Server`

Server-side internals (Domain and Application live here as folder organization):

- Layout service — load, save, and validate layouts (a stored layout referencing a widget kind that no longer exists must be handled, not crashed; see §6).
- `EfCoreWidgetLayoutStore` — EF Core implementation of the internal `IWidgetLayoutStore` port; schema in its own EF context (probably co-located with Customer's user store, but layout records are scoped per-user).
- `WidgetCatalogScanner` — scans loaded assemblies for `[Widget]`-decorated components at startup. The scan is one-time; the catalog is immutable after construction.
- `LayoutPlugin : Norse.Abstractions.Hosting.IWebHostPlugin` — registers the gRPC `IWidgetLayoutApi` endpoint and the layout-related DI on `Norse.Hosting.Web.Server` alongside every other context's plugin.

(No `Norse.Infrastructure.UI.Composition.Host` project. Layout API is hosted by `Norse.Hosting.Web.Server` like every other context's gRPC service — see §12.7 and analyzer spec §15.7.)

## 4. Per-Context UI Conventions

A `*.Components` assembly belongs to **exactly one** bounded context. Naming: `{Company}.{Context}.Components`. It contributes two kinds of UI surface:

1. **Widgets** — components decorated with `[Widget(...)]` that participate in dashboard composition (drag/drop, layout persistence).
2. **Routed pages** — components decorated with `@page "/route"` that participate in Blazor routing for direct URL access (e.g., `/billing/2026-04` for the full Billing detail view).

Both consume the same context's gRPC API. Both live in the same assembly and own the same vertical (component → gRPC service → validations → database). Whether a given component is rendered as a dashboard widget or a full-page route is the host's decision; the component stays dumb about how it's invoked.

### What's inside a `*.Components` assembly

```
{Company}.Billing.Components/
├── Widgets/
│   ├── BillingSummaryWidget.razor          ← [Widget("billing.summary", ...)]
│   ├── BillingSummaryWidget.razor.cs
│   ├── OutstandingInvoicesWidget.razor
│   └── PaymentMethodWidget.razor
├── Pages/
│   ├── BillingDetail.razor                 ← @page "/billing/{Period}"
│   └── BillingHistory.razor                ← @page "/billing/history"
├── Components/
│   └── (shared building blocks used only by Billing UI surface)
└── {Company}BillingUiExtensions.cs           ← generated by source generator
```

### What a widget references

Allowed (per the analyzer rule matrix — see companion spec §5):

- `Norse.Abstractions.Components` (for `IWidget`, `WidgetAttribute`, `IWidgetCatalog`, `IWidgetEventBus`)
- `Norse.Infrastructure.UI.Composition.Contracts` (for `LayoutModel`, `LayoutSlot`, `IWidgetHost` if the widget needs them; usually not — widgets render their own content and the host handles positioning)
- `{Company}.Billing.Contracts` (for `IBillingApi` and Billing wire shapes)
- Other contexts' `*.Contracts` if the widget legitimately needs cross-context data (e.g., a Billing widget that links out to Policy details might reference `{Company}.Policy.Contracts`)

Forbidden:

- `{Company}.Billing.Server` (server-only types — entities, EF, gRPC server impl)
- Other contexts' `*.Infrastructure` assemblies

The widget speaks to its context through the same gRPC API any external caller uses. The analyzer enforces this (YGG003 — Components cannot reference Infrastructure).

### How a widget declares itself

```csharp
namespace {Company}.Billing.Components.Widgets;

[Widget(
	Id = "billing.summary",
	Title = "Billing Summary",
	Description = "Outstanding balance, last payment, next due.",
	DefaultWidth = 4,
	DefaultHeight = 3,
	Capabilities = WidgetCapabilities.Refreshable)]
public partial class BillingSummaryWidget
{
	[Inject] private IBillingApi Billing { get; set; } = default!;
	[Parameter] public WidgetContext Context { get; set; } = default!;

	private BillingSummaryDto? summary;

	protected override async Task OnInitializedAsync()
	{
		summary = await Billing.GetSummaryForCustomerAsync(Context.CustomerId);
	}
}
```

The `[Widget]` attribute and `@page` directive are both scanned at build time by a source generator (sibling to `Norse.Abstractions.Components`). The generator emits an extension method per `*.Components` assembly that registers both widgets and routed pages:

```csharp
// Generated
namespace {Company}.Billing.Components;

public static class {Company}BillingUiExtensions
{
	public static IServiceCollection Add{Company}BillingUi(this IServiceCollection services)
	{
		// Widgets — participate in dashboard composition
		services.AddWidget<BillingSummaryWidget>();
		services.AddWidget<OutstandingInvoicesWidget>();
		services.AddWidget<PaymentMethodWidget>();

		// Routed pages — participate in Blazor routing
		services.AddRoutedComponent<BillingDetail>();
		services.AddRoutedComponent<BillingHistory>();

		return services;
	}
}
```

No runtime reflection scans. The widget catalog and the routed-component catalog are both populated at compile time. This matches CLAUDE.md's "no reflection in hot paths, no convention scanning for handlers" rule (§8).

### Cross-context widgets and the Customer 360 pattern

A widget that renders data from multiple contexts (e.g., a Customer 360 view that pulls billing balance, claim status, and policy details) lives in the **owning** context's Components assembly — the context that conceptually owns the cross-cutting view. A Customer 360 widget lives in `{Company}.Customer.Components` because Customer is the aggregating identity; it consumes `{Company}.Billing.Contracts`, `{Company}.Claims.Contracts`, and `{Company}.Policy.Contracts` via their respective gRPC APIs. This satisfies the analyzer rule matrix (Components may reference Contracts of any context) and keeps the widget's vertical clean: `{Company}.Customer.Components` owns the rendering and the routed page; each other context still owns its data, validations, and database.

The widget does not orchestrate a cross-context transaction. If a piece of cross-cutting business logic needs to span multiple contexts (e.g., "show me the consolidated risk picture for this customer"), that's an Infrastructure-layer concern in the owning context, not a Components concern. The widget just renders.

## 5. The Three Shells

Each runtime is a thin shell — its job is to compose widgets, configure transport bindings, and host the `<DashboardHost />` component.

### `Yggdrasil.DevServer` / `Norse.Hosting.Web.Server` — Blazor Server (development / server deployable)

```csharp
// Program.cs (sketch)
builder.Services
	.AddServerSideBlazor()
	.Add{Company}UiComposition()      // registers IWidgetCatalog, layout host, dashboard primitives
	.Add{Company}BillingUi()             // generated
	.Add{Company}ClaimsUi()              // generated
	.Add{Company}PolicyUi()              // generated
	.Add{Company}CustomerUi();           // generated

// Stage 1 bindings — in-process adapters
builder.Services.AddScoped<IBillingApi, InProcessBillingApiAdapter>();
builder.Services.AddScoped<IClaimsApi, InProcessClaimsApiAdapter>();
// ...
builder.Services.AddScoped<IWidgetLayoutApi, InProcessWidgetLayoutApiAdapter>();
```

The in-process adapters live in the host project (or in sibling `*.Adapters` projects co-located with the host). They reference the relevant Application + Infrastructure assemblies — that is permitted because the host is `Layer.Host` which has full access. The widget never sees them.

### `Norse.Hosting.Web.Client` — Blazor WebAssembly (browser)

```csharp
// Program.cs (sketch)
builder.Services
	.Add{Company}UiComposition()
	.Add{Company}BillingUi()
	.Add{Company}ClaimsUi()
	.Add{Company}PolicyUi()
	.Add{Company}CustomerUi();

// Stage 2 bindings — gRPC-Web clients via protobuf-net.Grpc
builder.Services.AddGrpcWebClient<IBillingApi>(opts => opts.Address = ApiBaseUri);
builder.Services.AddGrpcWebClient<IClaimsApi>(opts => opts.Address = ApiBaseUri);
builder.Services.AddGrpcWebClient<IPolicyApi>(opts => opts.Address = ApiBaseUri);
builder.Services.AddGrpcWebClient<ICustomerApi>(opts => opts.Address = ApiBaseUri);
builder.Services.AddGrpcWebClient<IWidgetLayoutApi>(opts => opts.Address = ApiBaseUri);
```

`AddGrpcWebClient<T>` is a thin extension (in `Norse.Infrastructure.GrpcClient` or similar) that registers a `protobuf-net.Grpc` client over a gRPC-Web channel. Same interface, different transport.

### `Norse.Hosting.App` — Blazor Hybrid (desktop/mobile)

```csharp
// MauiProgram.cs (sketch)
builder.Services
	.AddMauiBlazorWebView()
	.Add{Company}UiComposition()
	.Add{Company}BillingUi()
	.Add{Company}ClaimsUi()
	.Add{Company}PolicyUi()
	.Add{Company}CustomerUi();

// Stage 2 bindings — gRPC-Web for parity with WASM
// (Native HTTP/2 gRPC is available where the platform supports it,
//  but defaulting to gRPC-Web keeps a single code path for both shells.)
builder.Services.AddGrpcWebClient<IBillingApi>(opts => opts.Address = ApiBaseUri);
// ... same as WASM shell
```

**Decision recorded:** MAUI defaults to gRPC-Web, not native gRPC, so that WASM and MAUI exercise the same transport stack. Eliminates an entire class of "works in MAUI, broken in WASM" bugs. Revisit only if native gRPC delivers measurable performance benefit that justifies the dual-transport maintenance cost.

### Shared shell pieces

To avoid copy-paste between the three shells, a `Yggdrasil.Hosting.Composition` library packages the common DI extensions (`Add{Company}AllWidgets()` that calls every context's `Add{X}Widgets()` once). Each shell then has roughly five lines of widget setup plus its transport bindings.

## 6. Layout Model and Persistence

```csharp
namespace Norse.Infrastructure.UI.Composition.Contracts;

public sealed record LayoutModel
{
	public required string OwnerId { get; init; }
	public required string LayoutName { get; init; }
	public required int SchemaVersion { get; init; }
	public required IReadOnlyList<LayoutSlot> Slots { get; init; }
}

public sealed record LayoutSlot
{
	public required Guid SlotId { get; init; }
	public required string WidgetId { get; init; }       // matches WidgetAttribute.Id
	public required GridPosition Position { get; init; }
	public required IReadOnlyDictionary<string, string> Config { get; init; }  // per-widget opaque config
}

public sealed record GridPosition(int Column, int Row, int Width, int Height);
```

### Schema versioning

A `LayoutModel.SchemaVersion` field is mandatory. When the layout schema changes (new fields, renamed fields, removed widget capabilities), the version bumps. The `Application` layer's `LayoutValidator` upgrades older versions to current on load. Saving always writes the current version.

### Widget removal handling

If a stored `LayoutSlot.WidgetId` references a widget that no longer exists in the catalog (the assembly was removed, or the widget was renamed), the loader:

1. Logs the orphaned slot at `Warning`.
2. Replaces the slot with a `MissingWidgetSlot` placeholder that renders a polite "This widget is no longer available — remove it?" panel.

It does **not** silently drop the slot (that's silent data loss), and it does not crash (that's hostile to the user). This matches the CLAUDE.md §2.7 principle — fail loudly, but in a way the user can act on.

### Persistence schema

User layout records live in `Norse.Infrastructure.UI.Composition.Server`'s EF Core context. Schema:

- `ui_composition.layouts` — one row per (owner, layout_name). Columns: id, owner_id, layout_name, schema_version, slots_jsonb, updated_at.
- `slots_jsonb` is `jsonb` (Postgres) — opaque to the DB, validated by the Application layer.

The decision to store layout as `jsonb` rather than a normalized `layout_slots` table is deliberate: layouts are read and written as a single unit, the per-slot configuration is opaque to the layout service (it's interpreted by the widget), and the cardinality is small (a layout has dozens of slots, not thousands). Normalizing buys nothing.

## 7. Cross-Widget Concerns

### Auth and user identity

Widgets receive a `WidgetContext` cascading parameter containing the authenticated user/customer/producer identity. The shell populates this from the auth subsystem (OpenIddict — separate spec). Widgets never re-authenticate; they consume the identity the shell hands them.

### Cross-widget messaging

A widget that wants to broadcast an event ("user selected policy X — interested widgets should update") publishes on an in-process `IWidgetEventBus` registered as a singleton in the shell. Other widgets subscribe. This is a UI concern only — it does **not** cross the gRPC boundary and is not a substitute for the domain message bus.

`IWidgetEventBus` is declared in `Norse.Abstractions.Components` (cross-cutting; widgets from any context publish/subscribe); the default implementation in `Norse.Infrastructure.UI.Composition.Server` is in-process (a simple `Channel<T>`-backed pub/sub). For v1 the event shapes are open (no schema for cross-widget events). Widgets that care about a specific event type subscribe to that type.

### Per-widget configuration

The `LayoutSlot.Config` dictionary is opaque from the layout service's perspective. The owning widget interprets it. A widget that needs typed config declares a config record and serializes to/from the dictionary itself. A source-generator-driven config binding is out of scope for v1.

### PII inside widgets

Widgets render PII. The encryption boundary (CLAUDE.md §8, analyzer YGG101) protects PII *in transit and at rest*. Once the widget receives a decrypted wire shape from `IBillingApi`, the data is plaintext in the user's session. The widget is expected to honor display-masking conventions (e.g., showing only the last four digits of a SSN by default) but the analyzer cannot enforce that. Display-masking is a code-review concern.

## 8. Analyzer Rules That Support This Architecture

The companion analyzer spec defines the rules; this section enumerates which rules backstop the UI architecture specifically. All rules referenced here are defined in `2026-05-19-architecture-analyzers-design.md` §6.

- **YGG003 (Components cannot reference Infrastructure)** — `{Company}.Billing.Components` cannot reference `{Company}.Billing.Server`. Build error. This is the most important safeguard: Infrastructure carries server-only types (EF entities, database drivers, gRPC server bindings) that cannot exist in a WASM bundle or a MAUI BlazorWebView; leakage breaks the bundle at runtime.
- **YGG004 (cross-context internal reference)** — `{Company}.Billing.Components` cannot reference `{Company}.Claims.Server` (or `.Worker`). It can reference `{Company}.Claims.Contracts`. Build error.
- **YGG003 reverse direction** — A `{Company}.Billing.Server` project that takes a dependency on any `*.Components` assembly is also a build error. Server-side code does not pull UI in, even accidentally.
- **YGG101 (bare string PII on events)** — Applies to widget wire shapes equally. A widget wire shape with a bare `string SocialSecurityNumber` is a build error.
- **YGG005/YGG006 (InternalsVisibleTo)** — A widget assembly cannot grant `InternalsVisibleTo` to any non-test assembly. The boundary stays clean.

If these rules pass, the architecture described here is self-policing.

## 9. Stage Transitions in Practice

A worked example for a single widget shows how Stage 1 → Stage 2 → Stage 3 plays out without source changes to the widget itself.

### Day 1 — Stage 1

`BillingSummaryWidget` is written. `IBillingApi.GetSummaryForCustomerAsync(CustomerId)` is added to `{Company}.Billing.Contracts`. An in-process adapter is written in `Yggdrasil.DevServer` (or `Norse.Hosting.Web.Server` for monolith deployment) that resolves an `IBillingService` from `{Company}.Billing.Application` and forwards the call.

The team iterates: maybe `GetSummaryForCustomerAsync` needs a date range parameter; maybe the return type grows a `LastPaymentAt` field; maybe the whole method splits into two. Each change touches `IBillingApi` + the adapter + the Application layer + the widget. No `.proto` files, no client regen, no gRPC restart.

### Day N — Stage 2 graduation

The contract has stopped churning. `IBillingApi` and its wire shapes get `[ServiceContract]` / `[OperationContract]` / `[DataContract]` attributes (from `protobuf-net.Grpc`). The Billing service stands up a gRPC endpoint that implements `IBillingApi` and delegates to `IBillingService`. The Blazor WASM and MAUI shells switch their DI registration from in-process adapter to `protobuf-net.Grpc` gRPC-Web client. The widget code does not change.

The Blazor Server shell, used for internal dev/monolith deployment, **may** keep the in-process adapter or switch to the gRPC client — that's a deployment decision per environment.

### Day N+k — Stage 3 (only if needed)

A partner integration needs JSON. We add `{Company}.Billing.JsonApi`, an ASP.NET Core controllers project containing `BillingController : JsonControllerBase<IBillingApi>`. Each controller method is one route attribute + one `InvokeAsync(svc => svc.SomeMethodAsync(...))` line. The controller injects the **same** `IBillingApi` instance the gRPC host already serves — two front doors, one in-process service, zero transcoding. The partner consumes JSON; our internal clients keep using gRPC. The widget is untouched.

If we never need partner JSON, Stage 3 never happens. The Stage 1 → Stage 2 progression alone delivers the architecture's value.

## 10. Project Topology Summary

```
src/
├── Norse.Abstractions.Components/                      (Layer.Abstractions, Context "Components")    ← cross-cutting widget shape (declared law)
│                                             IWidget, WidgetAttribute, IWidgetCatalog, IWidgetEventBus
│
├── Norse.Infrastructure.UI.Composition.Contracts/       (Layer.Contracts,      Context "UI.Composition")  ← IWidgetLayoutApi, LayoutModel, IWidgetHost
├── Norse.Infrastructure.UI.Composition.Components/      (Layer.Components,     Context "UI.Composition")  ← DashboardHost, drag/drop, runtime IWidgetHost impls
├── Norse.Infrastructure.UI.Composition.Server/  (Layer.Infrastructure, Context "UI.Composition")  ← layout service, EF store, catalog scanner, LayoutPlugin
│
├── {Company}.{Context}.Components/           (Layer.Components, Context "{Context}")  ← per business context: widgets + routed pages
│
├── Norse.Hosting.Web.Server/                  (Layer.Infrastructure, Context "Hosting", Yggdrasil realm — connective tissue)  generic web host runtime
├── Yggdrasil.Hosting.Wasm/                 (Layer.Infrastructure, Context "Hosting")  generic Wasm host runtime
├── Norse.Hosting.App/                 (Layer.Infrastructure, Context "Hosting")  generic Maui host runtime
├── Yggdrasil.Hosting.Composition/          (Layer.Infrastructure, Context "Hosting")  host-side widget composition helper
│
├── Norse.Hosting.Web.Server/                           (Layer.Host)  ← server deployable; loads every context's plugin (Billing, Claims, Layout, Auth, ...)
├── Norse.Hosting.Web.Client/                     (Layer.Host)  ← client deployable; bundles every *.Components consumed
├── Norse.Hosting.App/                     (Layer.Host)  ← client deployable; Blazor Hybrid in BlazorWebView
└── Yggdrasil.DevServer/                      (Layer.Host)  ← Blazor Server dev bundle for local in-process Stage 1

test/
├── Norse.Infrastructure.UI.Composition.Components.Tests/        ← bUnit tests for DashboardHost, drag/drop, WidgetSlot
├── Norse.Infrastructure.UI.Composition.Server.Tests/    ← layout service, EF store, catalog scanner
└── {Company}.{Context}.Components.Tests/             ← bUnit-driven widget tests
```

Test layer for UI: bUnit (Razor component testing) plus standard xUnit for the non-rendering pieces (catalog, layout validator, in-process adapters). Shouldly for asserts, NSubstitute for doubles, per CLAUDE.md §4. *(Corrected 2026-06-03: an earlier draft said FluentAssertions; the platform standardized on Shouldly — FluentAssertions' commercial license is incompatible.)*

## 11. Resolved Decisions

1. **Naming.** `Norse.Infrastructure.UI.Composition` (not codenamed). *(Corrected 2026-06-03: an earlier draft placed the framework under `{Company}.*`; the analyzers spec §15.9 sequestered `{Company}.*` to bounded-context assemblies only, and this framework is platform infrastructure — embodied law — so it lives under `Norse.Infrastructure.*` per the seven-realm split.)* No Norse codename: codenames are reserved for opaque cross-cutting platform services, and this framework is concrete enough to name plainly. The cross-cutting widget-shape contracts it depends on (`IWidget`, `WidgetAttribute`, etc.) live in `Norse.Abstractions.Components` (declared law — every realm's `*.Components` assembly conforms to this shape).
2. **Cross-context UI references.** A widget that aggregates cross-cutting data lives in the owning context's Components assembly (Customer 360 → `{Company}.Customer.Components`) and consumes other contexts' published Contracts via gRPC. Routed pages live alongside widgets in the same assembly; each `*.Components` owns its own vertical from component to database. See §4.
3. **gRPC-Web for both WASM and MAUI clients.** Single transport path eliminates a class of "works in MAUI, broken in WASM" bugs. Connect protocol explicitly out of scope until `protobuf-net.Grpc` supports it upstream.
4. **MAUI uses Blazor Hybrid (BlazorWebView), not native XAML.** Razor components render to HTML inside WebView2/WKWebView/Android WebView. Single source of truth for components. If a future requirement demands native MAUI widgets, the abstraction migrates: the cross-cutting contracts already in `Norse.Abstractions.Components` stay; parallel `*.Blazor` and `*.Maui` implementations replace today's single Blazor implementation in `Norse.Infrastructure.UI.Composition.Components` (and the equivalent per business-context Components assemblies). Not on the v1 path.

## 12. Resolved Decisions (Additional)

These resolve open questions from the earlier draft:

5. **Layout-per-role and per-user (hybrid).** Role-specific defaults ship with the platform; users override at their discretion. `IWidgetLayoutStore` carries a `TemplateId` alongside `OwnerId`; loading a layout falls back from `(OwnerId, TemplateId)` → `TemplateId` → empty default. The role-template authoring tooling and the override-vs-reset semantics belong to a follow-on layout-persistence spec.
6. **Hot reload is a first-class requirement.** Stage 1 (Blazor Server in-process, all in `Yggdrasil.DevServer`) must give hot reload across widget code, in-process adapters, and `{Company}.{Context}.Server` business logic — every layer that's compiled C# in the dev process. The spike validating this is on the Done Criteria.
7. **Layout API lives on the shared host as a gRPC service.** `LayoutPlugin` (in `Norse.Infrastructure.UI.Composition.Server`) registers `IWidgetLayoutApi` on `Norse.Hosting.Web.Server` alongside every other context's plugin. Same for auth (`AuthPlugin`). There is no separate layout-API deployable; cross-cutting services are plugins like any business context. Scaling = replicas of `Norse.Hosting.Web.Server`. See analyzer spec §15.7 for the single-host model.

## 13. Open Questions

These remain genuinely open and need calls before code lands:

1. **Component library: Blazorise, FluentUI, or hand-rolled.** Narrowed to three candidates. Blazorise (commercial; designer-selectable CSS framework, fixed component ecosystem). FluentUI (Microsoft, non-commercial, opinionated styling). Hand-rolled (zero external surface, full control, more authoring cost). Decision deferred pending design-system spec; Q2 below depends on this. Recommendation when ready to pick: validate Blazorise's component coverage against expected forms (quote, claim FNOL, policy detail) and its behavior inside `BlazorWebView` for MAUI; if either is shaky, fall to FluentUI; if FluentUI doesn't fit the visual language, hand-roll.
2. **Component design system.** Coupled to Q1. If a library is picked (Blazorise/FluentUI), `{Company}.UI.DesignSystem` is a thin theming/wrapping layer over it. If hand-rolled, `{Company}.UI.DesignSystem` carries primitives (Button, Input, Form, Table) directly. Either way the design system is permissive (components *should* use it; analyzer doesn't enforce). Owner needs design expertise the project doesn't currently have — flag for hiring/consulting.

## 14. Done Criteria

This spec is "done enough to implement" when:

- [ ] Open questions §13 items 1 and 2 are resolved (one decision, since 2 depends on 1).
- [ ] The companion analyzer spec is approved and its boundary rules are concrete enough to enforce this layering at build time.
- [ ] A spike of the Stage 1 → Stage 2 transition has been executed (one widget, one context, full graduation path) to confirm the lifecycle works as designed and not just as drawn.
- [ ] A spike validates that Blazor Server hot reload covers widget code + `{Company}.{Context}.Server` in-process changes in one F5/save cycle (see §12.6).

## 15. Reconciliation History

The first peer-review draft of this spec lagged the analyzer spec and CLAUDE.md on six mechanical points. They are listed here as a closed punch-list so future readers can verify the spec is now internally consistent.

1. **`Norse.Infrastructure.UI.Composition.UI` → `Norse.Infrastructure.UI.Composition.Components`** — closed. The framework's Blazor-rendering project uses the `Components` concern name throughout.
2. **`Norse.Infrastructure.UI.Composition.Application` folded into `Norse.Infrastructure.UI.Composition.Server`** — closed. The layout service (load/save/validate) lives in Infrastructure; the Application project is gone from §3 and §10.
3. **`IWidget` / `WidgetAttribute` / `IWidgetCatalog` / `IWidgetEventBus` moved to `Norse.Abstractions.Components`** — closed. They're cross-cutting shape contracts every `*.Components` assembly consumes (declared law under the seven-realm split); the framework references them like any other consumer.
4. **§3 architecture diagram reconciled with the three-assembly model** — closed. The framework's box shows Contracts, Components, Infrastructure (not the legacy five-assembly layout).
5. **§3 diagram labels: per-context boxes use `.Components`** — closed.
6. **YGG003 reference text in §8 reflects the Components ↔ Infrastructure rule** — closed. The two stale Application-layer references introduced during the layer collapse (Components → Application examples) were corrected to Components → Infrastructure.

Open: the component-library decision (§13.1, §13.2). That gates code, not spec consistency.
