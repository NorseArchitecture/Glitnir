# Norse.Primitives.Architecture (Analyzers) + Norse.Abstractions.Architecture (Attributes) — Design

**Date:** 2026-05-19
**Status:** Draft for review
**Owner:** Buvy
**Supersedes:** none
**Amended:** 2026-06-03 — `YGG101` reshaped to its strict form following the CLAUDE.md §7 #11 resolution: bare `string` on message types is always an error; the `[NonSensitive]` opt-out attribute is deleted; `PlainText` (Primitives wrapper) is the typed declaration for non-sensitive free text.

---

## 1. Motivation

`CLAUDE.md` declares a number of architectural rules — strict assembly boundaries (§2.4), no silent fallbacks (§2.7), no `ProjectReference` inside `<Target>` blocks (§8), bare-string PII forbidden on event types (§8), implicit enum values forbidden (§5), naked money and dates forbidden (§8). Today these rules are enforced by:

1. The author remembering them.
2. Code review catching the lapse.
3. Possibly a runtime failure long after the offending change merged.

That is the exact reversed cost gradient §2.7 ("fail upstream") exists to prevent. A rule that lives only in prose imposes a constant tax on every reviewer and every cold-start session; a rule that lives in the compiler is paid for once.

The architecture-enforcement suite promotes the load-bearing rules to build errors. Under the seven-realm taxonomy (CLAUDE.md §5), the law and the hammer split cleanly: **`Norse.Abstractions.Architecture`** declares the architectural-attribute model (the contract every project conforms to); **`Norse.Primitives.Architecture`** forges the Roslyn analyzers and BuildCheck rules that strike when the law is broken. Both ship from the same submodule (`norse-primitives-architecture`) so the contracts and their enforcement travel in lockstep. The diagnostic prefix stays `YGG` — the rules are Norse-wide laws; only their implementation lives in Primitives.

The goal is not "every rule becomes an analyzer." The goal is: any rule whose violation produces silent, expensive failures (cross-context coupling, persisted bad data, untyped boundary crossings) is enforced at compile time. Rules whose violations produce noisy, cheap failures (a missing log line, a typo in a config key) stay in prose.

## 2. Architecture

Three cooperating components:

```
┌────────────────────────────────────────────────────────────────────┐
│          norse-primitives-architecture (submodule, two NuGet pkgs) │
│                                                                    │
│  Norse.Abstractions.Architecture (NuGet, normal reference)                     │
│  ┌────────────────────────────────────────────────────────────┐    │
│  │  ArchitecturalContextAttribute                             │    │
│  │  ArchitecturalLayerAttribute                               │    │
│  │  ArchitecturalTestTargetAttribute                          │    │
│  │  EnumSentinelNotRequiredAttribute                          │    │
│  │  Layer enum                                                │    │
│  │  Declared law — every project conforms; round-trips via    │    │
│  │  assembly metadata so analyzers can read it.               │    │
│  └────────────────────────────────────────────────────────────┘    │
│                                                                    │
│  Norse.Primitives.Architecture (NuGet, analyzer + dev dependency)  │
│  ┌────────────────────────┐  ┌────────────────────────────────┐    │
│  │  Attribute generator   │  │  Diagnostic analyzers          │    │
│  │  (source generator)    │  │  (Roslyn DiagnosticAnalyzer)   │    │
│  │                        │  │                                │    │
│  │  Emits AssemblyInfo.g  │  │  YGG001..YGG0xx              │    │
│  │  with [Layer/Context]  │  │  read attrs, walk symbols      │    │
│  └────────────────────────┘  └────────────────────────────────┘    │
│                                                                    │
│  ┌────────────────────────────────────────────────────────────┐    │
│  │  MSBuild BuildCheck rules — inspect project XML at         │    │
│  │  evaluation time (YGG3xx)                                  │    │
│  └────────────────────────────────────────────────────────────┘    │
└────────────────────────────────────────────────────────────────────┘
                                  │
                                  │ referenced from
                                  ▼
                       Directory.Build.props
                       (meta-repo root)
                                  │
                                  │ inherited by
                                  ▼
                       Every project in the solution
```

The package ships as a development dependency, referenced once from a root `Directory.Build.props`. Every project inherits the analyzers without per-project ceremony. Adding a new project gets enforcement for free; opting out is deliberate and visible.

## 3. Attribute Model

Two assembly-level attributes describe each project's role in the architecture. They live in `Norse.Abstractions.Architecture` (the declared law) so they can be referenced from anywhere without dragging the analyzer engine itself in.

```csharp
namespace Norse.Abstractions.Architecture;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ArchitecturalContextAttribute(string context) : Attribute
{
	public string Context { get; } = context;
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ArchitecturalLayerAttribute(Layer layer) : Attribute
{
	public Layer Layer { get; } = layer;
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ArchitecturalTestTargetAttribute(string targetContext) : Attribute
{
	public string TargetContext { get; } = targetContext;
}

public enum Layer
{
	Unspecified    = 0,
	Abstractions   = 1,  // declared law — Norse.Abstractions.* only (Contracts, Components, Infrastructure, Hosting, Architecture, Mediator). No product-tier abstractions layer exists.
	Contracts      = 2,  // per-context public face: events, I{Context}Api, request/response shapes
	Infrastructure = 3,  // concrete server-side / runtime code. Covers both per-context {Company}.{Context}.Server (entities, business logic, gRPC service impl, {Context}Plugin : IWebHostPlugin) and {Company}.{Context}.Worker (BackgroundService impls, {Context}WorkerPlugin : IWorkerHostPlugin) — they play the same DAG role; the .Server vs .Worker split is about which host loads them, not their layer. Also the home of the platform Norse.Infrastructure.* assemblies (Norse.Infrastructure.Persistence, Norse.Infrastructure.Api, Norse.Infrastructure.Mediator).
	Components     = 4,  // per-context Blazor surface: components, widgets, routed pages. Compiles into WASM / MAUI BlazorWebView; cannot reference Infrastructure (i.e., neither .Server nor .Worker)
	Host           = 5,  // deployable entry points (Norse-tier connective tissue): Norse.Hosting.Web.Server, Norse.Hosting.Worker, Norse.Hosting.Web.Client, Norse.Hosting.App, Yggdrasil.DevServer
	Test           = 6,  // dead end: depends on target; nothing depends on Test
	Primitives     = 7,  // the fulcrum: only the Primitives package and Norse.Primitives.Architecture live here; depend on nothing, everyone depends on them
}
```

`Layer.Unspecified = 0` is intentional. Per §5 of `CLAUDE.md`, `0` is reserved for "unspecified / sentinel only" — a project tagged `Unspecified` produces an immediate `YGG001` (it has not declared its role). There is no silent default.

`ArchitecturalTestTarget` is the controlled escape hatch: a test assembly may reference any layer **of the context it tests**. Without an explicit target, a test assembly is treated as if it were the layer it declares.

## 4. Convention-Driven Attribute Generation

Manually writing `[assembly: ArchitecturalLayer(Layer.Application)]` on every project is tribal-knowledge enforcement — easy to forget, easy to get wrong, easy to drift. Instead, the attributes are **generated** from project naming.

A source generator (or, equivalently, an MSBuild target writing `AssemblyAttributes.g.cs`) reads two MSBuild properties:

| Property | Default value | Override |
|---|---|---|
| `ArchitecturalContext` | Inferred from project name segment 2: `{Realm\|{Company}}.{Context}.{Suffix}` → `Context` | Set explicitly in `.csproj` |
| `ArchitecturalLayer` | Inferred from project name suffix (see table below) | Set explicitly in `.csproj` |

### Project-name → Layer mapping

Seven top-level namespaces govern roots — peers of each other, none nested (see CLAUDE.md §5 for the realm cosmology):

- `Norse.Hosting.*` — connective tissue: hosting runtimes (`Norse.Hosting.Web.Server` / `Worker` / `Migrations.Service`), `Norse.Hosting.ServiceDefaults`, `Norse.Hosting.AppHost`, **every deployable project** (`Norse.Hosting.Web.Server`, `Norse.Hosting.Worker`, `Norse.Hosting.Migrations.Service`, `Norse.Hosting.Web.Client`, `Norse.Hosting.App`, `Yggdrasil.DevServer`). Process composition only.
- `Norse.Abstractions.*` — declared law: every shape contract any realm conforms to (`Norse.Abstractions.Contracts`, `Norse.Abstractions.Components`, `Norse.Abstractions.Infrastructure` — the repository contract family, `Norse.Abstractions.Hosting`, `Norse.Abstractions.Architecture`, `Norse.Abstractions.Mediator`).
- `Norse.Infrastructure.*` — embodied law: concrete platform infrastructure (`Norse.Infrastructure.Persistence` — per-service DbContext family + concrete repository implementations, `Norse.Infrastructure.Api`, `Norse.Infrastructure.Mediator`, `Norse.Infrastructure.UI.Composition.*`).
- `Norse.Primitives.*` — the forge: load-bearing primitives (`Norse.Primitives` — `Result<T>`, `Money`, parsing, UUID v5 registry, `[MustConsume]`) plus the forged enforcement tools (`Norse.Primitives.Architecture` — Roslyn analyzers + BuildCheck rules).
- `Norse.ReferenceData.*` — woven memory across time (codename; CLAUDE.md §6): taxonomy, classification, audit, time-travel, event-sourced history; implementation of `Norse.Abstractions.Infrastructure.ITemporalRepository<T>`. **Amendment (2026-07-25):** this realm (Norns) dissolved 2026-06-11 — most reference data proved company-specific, so the pieces went home instead (temporal contracts → Asgard, implementations → Midgard, universal content → a thin library). See `docs/codenames.md` and `docs/the-crooked-path.md` #8. Every `Norse.ReferenceData.*` mention in this document (including the namespace table below and §15) describes this dissolved design, not current topology.
- `Norse.Warehouse.*` — the data warehouse (codename; CLAUDE.md §6): cross-context OLAP / batch ETL. The only realm permitted to read across bounded-context boundaries; services cannot.
- `{Company}.*` — **the MGA bounded contexts and only the bounded contexts.** Each service's per-context assemblies (`.Contracts`, `.Components`, `.Server`, `.Worker`, `.Migrations`, optionally `.JsonApi`) for every business context — Billing, Claims, Policy, Customer, Auth, etc. Deployables, frameworks, hosting abstractions do *not* live here. Context plugins implement Abstractions contracts directly.

| Project name pattern | Inferred Layer | Inferred Context |
|---|---|---|
| `Norse.Primitives` | `Primitives` | `Primitives` |
| `Norse.Primitives.Architecture` | `Primitives` (forged enforcement tools) | `Architecture` |
| `Norse.Abstractions.Contracts` | `Abstractions` | `Abstractions` |
| `Norse.Abstractions.Components` | `Abstractions` | `Abstractions` |
| `Norse.Abstractions.Infrastructure` | `Abstractions` (repository contract family — `IDocumentRepository<T>`, `ICommandRepository<T>`, etc.) | `Abstractions` |
| `Norse.Abstractions.Hosting` | `Abstractions` (host plugin interfaces — `IWebHostPlugin`, `IWorkerHostPlugin`, etc.) | `Hosting` |
| `Norse.Abstractions.Architecture` | `Abstractions` (architectural-attribute model) | `Architecture` |
| `Norse.Abstractions.Mediator` | `Abstractions` (mediator contracts) | `Mediator` |
| `Norse.Infrastructure.Persistence` | `Infrastructure` (per-service DbContext family + concrete repository implementations) | `Persistence` |
| `Norse.Infrastructure.Api` | `Infrastructure` (`JsonControllerBase<TService>` and the JSON face) | `Api` |
| `Norse.Infrastructure.Mediator` | `Infrastructure` (source generator + runtime) | `Mediator` |
| `Norse.Infrastructure.UI.Composition.Contracts` | `Contracts` (`IWidgetLayoutApi`, `LayoutModel`) | `UI.Composition` |
| `Norse.Infrastructure.UI.Composition.Components` | `Components` (DashboardHost, drag/drop, runtime `IWidgetHost` impls) | `UI.Composition` |
| `Norse.Infrastructure.UI.Composition.Server` | `Infrastructure` (layout service, EF entity configs, `LayoutPlugin : IWebHostPlugin`) | `UI.Composition` |
| `Norse.ReferenceData.*` | `Infrastructure` (concrete temporal / audit / event-sourced implementations) | `ReferenceData` |
| `Norse.Warehouse.*` | `Infrastructure` (cross-context warehouse — the one realm with read access across services) | `Warehouse` |
| `Norse.Hosting.{Web.Server\|Worker\|Migrations.Service}` | `Infrastructure` (concrete host runtimes that implement `Norse.Abstractions.Hosting` contracts) | `Hosting` |
| `Norse.Hosting.ServiceDefaults` | `Infrastructure` (Aspire defaults: OTEL, service discovery, health checks) | `Hosting` |
| `Norse.Hosting.AppHost` | `Host` (local-dev Aspire orchestrator) | `Host` |
| `Norse.Hosting.Web.Server` | `Host` (single ASP.NET Core deployable; loads all context plugins) | `Host` |
| `Norse.Hosting.Worker` *(optional)* | `Host` (sibling worker deployable) | `Host` |
| `Norse.Hosting.Migrations.Service` | `Host` (migrations deployable; orchestrates per-context migrations) | `Host` |
| `Norse.Hosting.Web.Client` / `Norse.Hosting.App` / `Yggdrasil.DevServer` | `Host` | `Client` |
| `{Company}.{Context}.Contracts` | `Contracts` | `{Context}` |
| `{Company}.{Context}.Components` | `Components` | `{Context}` |
| `{Company}.{Context}.Server` | `Infrastructure` (server-side per-context: entity classes, `IEntityTypeConfiguration<T>` impls, business logic, gRPC impl, JSON controllers, `{Context}Plugin : IWebHostPlugin`; no DbContext, no `SaveChangesAsync`) | `{Context}` |
| `{Company}.{Context}.Worker` | `Infrastructure` (worker-side per-context: `BackgroundService` impls, `{Context}WorkerPlugin : IWorkerHostPlugin`; references `.Server`) | `{Context}` |
| `{Company}.{Context}.Migrations` | `Infrastructure` (data migration assembly, independently versioned; targets the `Norse.Infrastructure.Persistence` DbContext for its context) | `{Context}` |
| `{Company}.{Context}.JsonApi` *(rare — folded into `.Server` by default)* | `Infrastructure` | `{Context}` |
| `{Company}.{Context}.{Anything}.Tests` | `Test`, with `TestTarget = "{Context}"` | `{Context}` |
| `Norse.Infrastructure.UI.Composition.Contracts` | `Contracts` | `UI.Composition` |
| `Norse.Infrastructure.UI.Composition.Components` | `Components` | `UI.Composition` |
| `Norse.Infrastructure.UI.Composition.Server` | `Infrastructure` | `UI.Composition` |
| `Norse.Hosting.Web.Server` | `Host` (single ASP.NET Core deployable; loads all context plugins) | `Host` |
| `Norse.Hosting.Worker` *(optional)* | `Host` (sibling worker deployable, only if a workload's profile justifies splitting from `Norse.Hosting.Web.Server`) | `Host` |
| `Norse.Hosting.Web.Client` / `Norse.Hosting.App` / `Yggdrasil.DevServer` | `Host` | `Client` |
| `Norse.Hosting.{Web.Server\|Worker\|Migrations.Service}` | `Infrastructure` (concrete host runtimes that implement `Norse.Abstractions.Hosting` contracts; consumed by product deployables) | `Hosting` |
| `Norse.Hosting.ServiceDefaults` | `Infrastructure` (Aspire defaults: OTEL, service discovery, health checks) | `Hosting` |
| `Norse.Hosting.AppHost` | `Host` (local-dev Aspire orchestrator) | `Host` |

### What lives where, by concern

Per bounded context (e.g., Billing), assemblies split by deployable destination — no per-context Host deployables (see §15.7):

- **`{Company}.{Context}.Contracts`** — published events, `I{Context}Api` interfaces, request/response shapes. The single project other contexts may reference.
- **`{Company}.{Context}.Components`** — Blazor components, routed pages, widgets. Compiles into WASM/MAUI bundles; cannot reference `.Server`, `.Worker`, or any server-only types.
- **`{Company}.{Context}.Server`** — server-side internals: entities (which double as EF Core entities), business logic, command/query handlers, EF Core mappings, gRPC service implementations, JSON controllers (if any), query repositories, **and** an `internal sealed class {Context}Plugin : Norse.Abstractions.Hosting.IWebHostPlugin` that declares the context's DI registrations and route bindings. Domain and Application are folder-level organization here, not separate assemblies. Most contexts have a `.Server` assembly; pure worker-only contexts skip it.
- **`{Company}.{Context}.Worker`** *(optional — only when the context has background work)* — `BackgroundService` implementations, scheduled jobs, queue handlers, **and** an `internal sealed class {Context}WorkerPlugin : Norse.Abstractions.Hosting.IWorkerHostPlugin` that registers them. References `.Server` for shared internals; `.Server` is the canonical home of business logic, `.Worker` is the canonical home of background execution.

Optional per-context add-ons: `{Company}.{Context}.Migrations` when migrations warrant separate versioning; `{Company}.{Context}.JsonApi` only when the partner-JSON surface justifies a separate assembly (default: fold partner controllers into `.Server`).

The shared `Norse.Abstractions.{Concern}` libraries are referenced by the per-context implementations of that concern: `{Company}.Billing.Contracts` references `Norse.Abstractions.Contracts`, `{Company}.Billing.Components` references `Norse.Abstractions.Components`, `{Company}.Billing.Server` references `Norse.Abstractions.Hosting` (for `IWebHostPlugin`), `{Company}.Billing.Worker` references `Norse.Abstractions.Hosting` (for `IWorkerHostPlugin`). No product-tier wrapper interfaces.

### Server-side deployables are not per-context

A single `Norse.Hosting.Web.Server` ASP.NET Core deployable loads every context's plugin. Scaling is horizontal across replicas of `Norse.Hosting.Web.Server`, not by fragmenting into per-context API servers. See §15.7 for the rationale and §5 → "Host references the minimum" for the dependency convention.

### Why no per-context Abstractions assembly

A `{Company}.{Context}.Abstractions` project would be redundant. The per-context **public** abstractions (interfaces other contexts call) live in `{Company}.{Context}.Contracts` because they're the wire-shaped published API. The per-context **internal** abstractions (interfaces only the context's own code uses) live in `{Company}.{Context}.Server` (or `.Worker`) as internal types. Splitting them across a third assembly adds friction without giving the analyzer more enforcement power: the dependency-graph rules already prevent cross-context internal references.

The **platform-tier** shared abstractions (the shape every context's `Contracts`/`Components`/`Server`/`Worker` must conform to) live in `Norse.Abstractions.{Concern}`. There is **no product-tier abstractions layer** — context plugins implement `Norse.Abstractions.Hosting.IWebHostPlugin` / `IWorkerHostPlugin` directly. MGA-specific cross-cutting (audit, `NorsePrincipal` flow) is platform middleware configured at the Norse host runtime, not an interface extension. *(Tenancy was originally listed here; removed 2026-06-03 — no runtime tenancy under stamp-per-tenant, see `2026-06-03-tenancy-model-design.md`.)* **Amendment (2026-07-25):** `NorsePrincipal` itself is superseded — Asgard's shipped `Abstractions.Contracts` carries `Outcome<T>`/`Problem`/`ErrorCategory`/`GenerateGatewayAttribute` instead of the `NorsePrincipal`/`Population`/`IAccountApi` design this reference assumed. No `NorsePrincipal` type exists in current source.

Projects that don't fit these patterns must declare both properties explicitly. If neither inference nor explicit declaration produces a value, the generator emits `[ArchitecturalLayer(Layer.Unspecified)]` — which trips `YGG001` on the first build, making the omission impossible to miss.

### Why generate instead of decorate

- Adding a new project gets the right attributes by virtue of being named correctly.
- The project name is already the source of truth for the layout convention — duplicating it as an attribute is a synchronization bug waiting to happen.
- Renaming a project automatically updates its role.
- The same naming convention drives the analyzer rules *and* the file/folder layout convention, so they cannot drift apart.

## 5. The Rule Matrix

The matrix is the rule of record, but the matrix exists for one reason: **the assembly dependency graph must be a one-way DAG.** No cycles, ever. The "layers" are vocabulary for stating that DAG concisely — they are not goals in themselves.

A reference from project **A** to project **B** is permitted if the cell `(A.Layer, B.Layer)` is satisfied, taking context equality into account.

Legend:
- `✔` allowed within the same context
- `✔c` allowed cross-context (and same-context)
- `—` forbidden

| **From ↓ / To →** | Primitives | Abstractions | Contracts | Infrastructure | Components | Host | Test |
|---|---|---|---|---|---|---|---|
| **Primitives** | ✔ | — | — | — | — | — | — |
| **Abstractions** | ✔c | ✔c | — | — | — | — | — |
| **Contracts** | ✔c | ✔c | — | — | — | — | — |
| **Infrastructure** | ✔c | ✔c | ✔c | ✔ | — | — | — |
| **Components** | ✔c | ✔c | ✔c | — | ✔c | — | — |
| **Host** | ✔c | ✔c | ✔c | ✔ | ✔c | ✔ | — |
| **Test** | ✔c | references the layer it targets (any layer within target context) | | | | | — |

### Reading the matrix — three principles

1. **Primitives is the fulcrum.** It depends on nothing but itself; every other layer may reference it cross-context. The forged primitives (`Result<T>`, `Money`, parsing stack, UUID v5 registry, `[MustConsume]`) anchor the entire dependency graph and rule out cycles by construction. Adding code at Layer.Primitives means adding it to the Primitives package — no other assembly lives at this layer.
2. **Components must not see Infrastructure.** Components compile into the WASM bundle and run inside MAUI's BlazorWebView. Infrastructure carries EF Core entities, database drivers, gRPC server bindings, and other server-only types that cannot exist in those environments. A widget talks to `IBillingApi` (in `{Company}.Billing.Contracts`), never to a Billing service implementation directly. YGG003 catches violations at build time.
3. **Cross-context coupling goes through Contracts only.** Infrastructure, Components, and Host may reference *other contexts'* Contracts — never their Infrastructure. The Contracts assembly is the published API of a context.

### Host references the minimum; transitivity does the rest

A Host project directly references only what it actually composes:

- **`Norse.Hosting.Web.Server`** (single server-side deployable): every `{Company}.{Context}.Server` project whose plugin it loads, every `{Company}.{Context}.Worker` (when running monolith-mode), plus `Norse.Hosting.Web.Server`. That's it. Contracts, the Abstractions layer, the Infrastructure layer, and Primitives flow transitively through `.Server`/`.Worker`.
- **Client hosts** (`Norse.Hosting.Web.Client`, `Norse.Hosting.App`): the `Components` project(s) bundled, plus `Norse.Infrastructure.UI.Composition.Components`, plus client transport bindings. **No** Infrastructure — server-only types would break the WASM bundle.
- **Dev host** (`Yggdrasil.DevServer`): both — Infrastructure for in-process adapters, Components for in-process rendering. Blazor Server dev-mode in one process.

Contracts, Abstractions, and Primitives flow transitively through Infrastructure (for `Norse.Hosting.Web.Server`) or Components (for client hosts). The matrix permits direct references to all of those layers, but the convention is "name only what you actually instantiate; let the project graph carry the rest." A `Norse.Hosting.Web.Server` `.csproj` enumerates the Infrastructure projects whose plugins it loads and a couple of platform references — that's the whole composition.

### Tests are dead ends

A test project depends on the assembly it tests; nothing depends on tests. Cycles through test assemblies are impossible by construction. The `[ArchitecturalTestTarget("{Context}")]` attribute (§8) widens the test project's allowed referees to "any layer within the target context" — but the *outbound* direction stays empty.

### Intra-service concrete example (Billing)

*(Diagram amended 2026-06-03 to the post-messaging-foundation shape: `{Company}.{Context}.Backend` added; entity classes + EF configurations relocated from `.Server` to `.Worker`; `.Server` and `.Worker` mutually invisible; repository contract family per the persistence spec.)*

The matrix produces this graph for one context:

```
Primitives                                      ← fulcrum, depends on nothing
    ↑ (referenced by everything below)

Norse.Abstractions.Contracts                                ← declared law: shape every *.Contracts conforms to
Norse.Abstractions.Components                               ← declared law: shape every *.Components conforms to (IWidget, etc.)
Norse.Abstractions.Infrastructure                           ← declared law: shape every *.Server / *.Worker conforms to (IDocumentRepository<T>, ICommandRepository<T>, etc.)
Norse.Abstractions.Hosting                                  ← declared law: IHostPlugin / IWebHostPlugin / IWorkerHostPlugin / IMigrationContributor
Norse.Abstractions.Architecture                             ← declared law: ArchitecturalLayer/Context attributes, Layer enum
Norse.Abstractions.Mediator                                 ← declared law: IRequest, IRequestHandler
    ↑ (referenced directly by per-context implementations — no product-tier wrapper)

{Company}.Billing.Contracts                       ← IBillingApi, published events, wire shapes
    ↑ (consumed cross-context by other product contexts and transitively by {Company}.Billing.Components / Server / Worker)

{Company}.Billing.Backend                         ← server-side shared assembly: server→worker commands,
    ↑ (referenced by .Server and .Worker;          Mongo document records, shared server-side
    │  never by Components)                        options/constants.
    │
{Company}.Billing.Server                          ← web tier: authority validation, Mongo reads + shim
    │                                              writes (IDocumentRepository<T>), command dispatch via
    │                                              IMessageSession, gRPC services, JSON controllers,
    │                                              BillingPlugin : Norse.Abstractions.Hosting.IWebHostPlugin.
    │                                              NO SQL entities, NO EF, NO DbContext — the system of
    │                                              record is unreachable from this assembly.
    │
{Company}.Billing.Worker                          ← system of record: entity classes,
    │ [Layer.Infrastructure]                       IEntityTypeConfiguration<T> impls (relationships,
    │                                              indexes, check constraints, schema/table mapping),
    │                                              business logic, NServiceBus handlers/sagas,
    │                                              BackgroundService impls, BillingWorkerPlugin :
    │                                              Norse.Abstractions.Hosting.IWorkerHostPlugin. Consumes the
    │                                              worker-only repository contracts (ICommandRepository<T>,
    │                                              ICachedRepository<T>, ITemporalRepository<T>). NEVER
    │                                              injects a DbContext, NEVER calls SaveChangesAsync —
    │                                              that's all Infrastructure's. Mutually invisible with .Server;
    │                                              they meet only at the queue.

Norse.Infrastructure.Persistence                              ← owns BillingDbContext (per-service DbContext family),
   [Layer.Infrastructure]                          scans {Company}.Billing.Worker's IEntityTypeConfiguration<T>
                                                   impls at startup and applies them, provides concrete
                                                   repository implementations against the DbContext.
                                                   Infrastructure decides at deployment time whether BillingDbContext
                                                   and ClaimsDbContext resolve to the same connection
                                                   (schema isolation) or different ones (database isolation).

ReferenceData                                    ← implements Norse.Abstractions.Infrastructure.ITemporalRepository<T>
   [Layer.Infrastructure]                          (the "across time" repository) on top of its
                                                   event-sourced / projection backing store.

    │  (.Server consumed by Norse.Hosting.Web.Server; .Worker consumed by Norse.Hosting.Web.Server monolith mode
    │   OR Norse.Hosting.Worker split mode — same plugins, different deployable.)
    │
    ▼
Norse.Hosting.Web.Server                                   ← single ASP.NET Core deployable (platform composition,
   [Layer.Host]                                    not per-tenant identity). Loads BillingPlugin,
                                                   ClaimsPlugin, PolicyPlugin, CustomerPlugin, LayoutPlugin
                                                   (from Norse.Infrastructure.UI.Composition.Server), AuthPlugin (from
                                                   .Server), plus BillingWorkerPlugin etc. (from .Worker)
                                                   in monolith mode. All gRPC services and JSON controllers
                                                   on one HTTP endpoint.
                                                   Direct refs: every {Company}.{Context}.Server (+ .Worker in
                                                                monolith mode) + Norse.Hosting.Web.Server.


{Company}.Billing.Components ───────► {Company}.Billing.Contracts             (wire shapes)
   [Layer.Components]      ───────► Norse.Infrastructure.UI.Composition.Components     (layout primitives)
                           ───────► Norse.Abstractions.Components                     (IWidget, WidgetAttribute)
                           ───────► Primitives                            (Result<T>, Money in wire shapes)
                           (no .Server or .Worker — server-only types would break the WASM bundle)
```

Nothing in this graph closes a cycle. Every arrow points toward Primitives. The Components branch is parallel to the server branch and shares only the Contracts assembly — exactly what makes the same component code run in WASM, MAUI, and Blazor Server without leaking server-only types. The single `Norse.Hosting.Web.Server` consumes every context's Infrastructure on the server side; scaling out means more replicas of that one process, not 25 separate API servers.

## 6. Diagnostic Catalog

The diagnostic prefix is `YGG`. Severities listed are the **final** severities. Rollout (§14) ships at lower severities first.

### Boundary rules (YGG0xx)

| ID | Title | Severity |
|---|---|---|
| YGG001 | Assembly has not declared `[ArchitecturalLayer]` (resolves to `Layer.Unspecified`) | Error |
| YGG002 | Assembly has not declared `[ArchitecturalContext]` | Error |
| YGG003 | Disallowed layer reference (rule matrix violation) | Error |
| YGG004 | Disallowed cross-context reference (only Contracts allowed) | Error |
| YGG005 | `InternalsVisibleTo` targets non-test assembly | Error |
| YGG006 | `InternalsVisibleTo` targets test assembly of a different context | Error |
| YGG007 | Test assembly missing `[ArchitecturalTestTarget]` | Warning |
| YGG008 | Project name does not match any layer convention and no override declared | Error |

### Domain type-shape rules (YGG1xx)

| ID | Title | Severity |
|---|---|---|
| YGG101 | Bare `string` property on `*Event` / `*Command` / `*Notification` type — no exemptions *(Amended 2026-06-03)* | Error |
| YGG102 | Enum member declared without explicit integer value | Error |
| YGG103 | Enum member `0` named anything other than `None`/`Unspecified` | Error |
| YGG104 | Enum has zero members assigned `0` (reserved sentinel missing) | Warning |
| YGG105 | `decimal` property/parameter on a published type without companion currency | Error |
| YGG106 | `DateTime` (not `DateTimeOffset`/`DateOnly`/domain type) on a published type | Error |
| YGG107 | Public/internal field on a `*Event`/`*Command`/`*Dto` type (must be init-only property) | Warning |
| YGG108 | `[Obsolete]` enum member removed from source (history check via baseline file) | Error |

### Control-flow rules (YGG2xx)

| ID | Title | Severity |
|---|---|---|
| YGG201 | `Result<T>` returned and discarded at call site | Error |
| YGG202 | `catch (Exception)` without rethrow, logging, or explicit `// reason:` comment justifying swallow | Warning |
| YGG203 | `async void` method outside of event handler context | Error |
| YGG204 | `Task` returned from method whose name does not end in `Async` or which is a Razor handler | Info |

### MSBuild / project-shape rules (YGG3xx — implemented as BuildCheck rules)

| ID | Title | Severity |
|---|---|---|
| YGG301 | `ProjectReference` declared inside `<Target>` element | Error |
| YGG302 | `Directory.Packages.props` modified outside designated tier rules (CPM strategy violation) | Warning |
| YGG303 | Project uses both `<PackageReference>` and `<NorseRef>` for the same package | Error |
| YGG304 | Migration assembly missing `<IsAotCompatible>` declaration | Info |

The numbering leaves room for future rules without renumbering. New rules slot in at the next available number in their category.

## 7. Symbol-Level Analyzer Notes

The boundary rules (YGG001–008) are compilation-level — they read assembly attributes and reference graphs. The rules below have implementation subtleties worth pinning down.

### YGG101 — bare string on message types *(Amended 2026-06-03)*

The rule fires on any `string` (or `string?`) property declared on a type whose name ends with `Event`, `Command`, or `Notification`, regardless of namespace — **with no exemptions.** Insurance PII (SSN, DOB, addresses, claim narratives) leaking onto wire payloads is the kind of failure that ends in a regulator letter — it earns build-error severity.

The original draft allowed a `[NonSensitive]` opt-out attribute. The §7 #11 resolution deleted it: an attribute is a decoration that doesn't travel — it gets lost the moment the value is copied into a projection, a log line, or another message. A type travels with the value everywhere it goes. Declaring data non-sensitive is therefore a deliberate typed act, not a per-property annotation a hurried author forgets or cargo-cults.

Allowed shapes for string-shaped data on a message:
- Domain types like `EmailAddress`, `UsZipCode`, `PolicyNumber` that carry their own type identity
- `EncryptedString` — PII; a wrapper struct from the PII encryption infrastructure (`EncryptedString` spec — AES-256-GCM, Key Vault envelope encryption, per-customer DEKs, per CLAUDE.md §4 → PII and Encryption)
- `PlainText` — deliberately non-sensitive free text; a Primitives wrapper whose construction is the grep-able declaration that a human judged the content non-sensitive

Bare `int` / `bool` / `Guid` / `DateOnly` remain legal on message types where genuinely primitive; YGG105/YGG106 continue to govern money and temporals independently.

### YGG102 / YGG103 / YGG104 — enum hygiene

`CLAUDE.md` §5 makes the rule unambiguous: every member explicit, `0` reserved for sentinel only, real states start at `1`. The analyzer enforces all three. YGG104 is a Warning rather than Error because adding an `Unspecified` member to every enum is sometimes overkill (e.g., flag enums where `0` legitimately means "no flags"). YGG104 can be silenced with `[EnumSentinelNotRequired]` on the enum.

### YGG105 / YGG106 — naked money and dates

"Published type" means: a type declared in `Contracts`, in a `Domain` aggregate root, on a `*Api` interface, or on any `*Event`/`*Command`/`*Dto`. Internal helpers in `Application` may use raw `decimal` / `DateTime` freely — the analyzer only fires at boundary crossings.

Allowed shapes:
- `Money` — required currency, declared in `Norse.Primitives` (the forged-primitives package)
- `DateOnly` — for calendar dates with no time component
- `DateTimeOffset` — for instants
- Domain types: `EffectiveDate`, `LossDate`, `BindDate`, etc. — each a wrapper with its own meaning

### YGG201 — Result<T> discarded

`Result<T>` is a `readonly record struct` (decision recorded: matches the existing implementation, plays correctly with EF entity mapping, surfaces nullability through `Result<T>?` in OpenAPI docs, and serializes correctly in `System.Text.Json`). Enforcement is attribute-driven:

- `[MustConsume]` attribute lives in `Norse.Primitives` (it's a forged primitive that travels with `Result<T>`, not an architectural attribute) and is applied to `Result<T>`. Any type, method, or property the platform wants to enforce against may also wear it.
- YGG201 uses `IOperation` analysis to fire on three concrete patterns:
  - Direct discard: `_ = MethodReturningResult();`
  - Expression statement: `MethodReturningResult();` where the return value is dropped on the floor.
  - Unused local: `var r = MethodReturningResult(); /* r is never read in any subsequent operation */`
- Out of scope for the analyzer: results stored in a field that is never read, results passed through multiple layers and ultimately dropped, results read but their failure case unhandled. Those failure modes are real but caught by IDE unused-symbol warnings or by integration tests, not by this analyzer. Trying to enforce them produces unsustainable false-positive rates.

The source-generator alternative (consumers must call `.Match` / `.IfFail` to extract the value) was considered and rejected: it adds friction for a primitive that consumers should be able to construct and pattern-match naturally, and `readonly record struct` already gives positional deconstruction for free.

### YGG005 / YGG006 — InternalsVisibleTo

`InternalsVisibleTo` is a side channel that defeats layer enforcement. The rule:
- Target must be an assembly with `Layer = Test` AND matching context.
- Cross-context `InternalsVisibleTo` is forbidden outright. If you need to share internals across contexts, promote them to public on the Contracts boundary.

## 8. Test Project Exemption

Test projects are special: they must be able to reach into the layer they exercise. The exemption is two-step:

1. The test project declares `[ArchitecturalLayer(Layer.Test)]` (auto-generated from the `.Tests` suffix).
2. The test project declares `[ArchitecturalTestTarget("Billing")]` (auto-generated from the project name — `{Company}.Billing.Server.Tests` → target context `Billing`).

Given those two attributes, the analyzer permits references to **any layer of the target context**. It does **not** permit references to *other* contexts' internals — even tests must go through Contracts cross-context. This is deliberate: a test that needs Billing internals AND Claims internals is a test that's testing too much.

A test project with no `ArchitecturalTestTarget` (YGG007 Warning) is permitted same-layer references only, which is rarely what you want — the warning is meant to catch misnaming, not block legitimate test scenarios.

## 9. BuildCheck Integration (YGG3xx)

.NET 10's BuildCheck infrastructure runs at MSBuild evaluation time and can analyze the project XML graph itself. This is the right tool for project-shape rules — they are not C# code, they are build inputs.

The three rules implemented as BuildCheck:

- **YGG301** — `ProjectReference` inside `<Target>`: scan the evaluated project for items added under target elements; emit a build error pinpointing the offending target name. Project references declared inside an MSBuild target are invisible to the solution build manager's dependency ordering; the build "works" but link order is non-deterministic and the first cold rebuild on a clean machine fails for reasons that take hours to diagnose. Non-negotiable.
- **YGG302** — Central Package Management tier mismatch: Norse's CPM strategy (declared in a future companion spec) classifies projects into tiers with declared CPM posture per tier. If a project's declared layer doesn't match the CPM configuration of its `Directory.Packages.props`, fire. The rule is scaffolded here as a placeholder; activation waits on the CPM spec.
- **YGG303** — Conflicting reference types: a project using both `<PackageReference>` and the platform's project/package ref item (`<PlatformRef>` or equivalent — to be specified in the meta-build spec) for the same logical package is ambiguous about which wins. Fire and require the consumer to pick one.

BuildCheck rules ship as the same NuGet package, registered through the BuildCheck extension points.

## 10. Packaging and Distribution

```
Norse.Abstractions.Architecture          (NuGet, normal reference)
├── ArchitecturalContextAttribute
├── ArchitecturalLayerAttribute
├── ArchitecturalTestTargetAttribute
├── EnumSentinelNotRequiredAttribute
└── Layer enum

Norse.Primitives.Architecture (NuGet, analyzer + development dependency)
├── Source generator (AssemblyAttributes.g.cs from MSBuild properties)
├── DiagnosticAnalyzers (YGG001..YGG2xx)
├── BuildCheck rules (YGG3xx)
└── Targets file that wires the source generator to MSBuild properties

Directory.Build.props (at meta-repo root)
└── <PackageReference Include="Norse.Primitives.Architecture" PrivateAssets="all" />
└── <PackageReference Include="Norse.Abstractions.Architecture" />
```

`PrivateAssets="all"` on the analyzer package means consumers of any project in this repo don't transitively pull the analyzer engine — they get enforced output without inheriting the enforcement machinery.

`Norse.Abstractions.Architecture` is a real runtime reference because the attributes need to be readable from referenced assemblies at analysis time. It is tiny (no dependencies, no behavior, just metadata types) and AOT-trivial. The `[MustConsume]` attribute itself ships from `Norse.Primitives` (the forged-primitives package), not from `Norse.Abstractions.Architecture` — it travels with `Result<T>`.

## 11. Analyzer Testing

The analyzer project itself is testable via `Microsoft.CodeAnalysis.Testing`. The test project structure:

```
Norse.Primitives.Architecture.Tests/
├── BoundaryRuleTests/
│   ├── Contracts_Cannot_Reference_Infrastructure_Tests.cs
│   ├── Infrastructure_Cannot_Reference_Other_Context_Infrastructure_Tests.cs
│   └── Components_Cannot_Reference_Infrastructure_Tests.cs
├── TypeShapeRuleTests/
│   ├── BareStringPiiTests.cs
│   ├── ImplicitEnumValueTests.cs
│   └── NakedMoneyTests.cs
├── BuildCheckTests/
│   └── ProjectReferenceInTargetTests.cs
└── Fixtures/
    └── (synthetic assemblies / project XML for the analyzer to chew on)
```

Each rule has at least:
- A "should fire" case demonstrating the violation
- A "should not fire" case demonstrating the legitimate adjacent pattern
- A "fixer" test if a code fix provider is offered

## 12. Code Fix Providers (Selective)

Code fixes are written **only** where the fix is mechanical and unambiguous:

| Rule | Fix offered? | Fix description |
|---|---|---|
| YGG102 | Yes | Number members sequentially starting at `1` (with prompt) |
| YGG104 | Yes | Insert `Unspecified = 0` as first member |
| YGG106 | No | Choice between `DateOnly`/`DateTimeOffset`/domain type is contextual |
| YGG101 | No | Choosing among `EncryptedString` / `PlainText` / a domain type requires human judgment *(Amended 2026-06-03)* |
| YGG003 | No | Boundary violations almost always indicate a missing Contracts type, not a fixable line |

The principle: a code fix that gets it wrong is worse than no code fix. Bias to "no fix, clear diagnostic" unless the right answer is obvious.

## 13. Severity Defaults and Editor Config

Severities ship at the levels in §6 by default. Consumers may override via `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.YGG101.severity = error
dotnet_diagnostic.YGG102.severity = error
```

The default `.editorconfig` shipped in the repo locks all `YGG0xx` and `YGG1xx` to `error`. `YGG2xx` (control flow) ships at `warning` initially because of false-positive risk during initial rollout; it is ratcheted to `error` once the existing codebase clears.

### .NET version policy

The analyzer is **single-target** against the latest stable .NET (currently .NET 10). Multi-targeting is explicitly not supported. The platform upgrades aggressively: as soon as the next .NET version reaches RC1 with go-live, the analyzer (and every other Norse/{Company} project) migrates to it. Roslyn versions are pinned to the SDK in use; cross-version compatibility is not a goal.

## 14. Rollout Strategy

Adding strict enforcement to a non-empty codebase causes a flood of pre-existing violations. The rollout is staged:

1. **Phase 1 — Ship as warning.** All new rules ship as `warning` severity. The codebase compiles. The full inventory of violations is visible in IDE and CI output.
2. **Phase 2 — Baseline.** Existing violations are captured in a `glit-baseline.xml` file (or per-rule suppression file). New violations are warnings; baselined violations are silent. CI fails if `glit-baseline.xml` *grows*.
3. **Phase 3 — Burn down.** Existing violations are fixed at convenience. Each baseline entry removed is a small permanent ratchet.
4. **Phase 4 — Promote to error.** When baseline is empty, rule severity flips to `error` in the shipped `.editorconfig`.

Norse is greenfield, so Phase 1–4 collapse: every rule ships at error severity from the first commit. The baseline mechanism is still useful for future-added rules.

## 15. Resolved Decisions

Captured here so the rationale survives the move from draft. Each decision is reflected in the spec body above; this section is the chronicle.

1. **`Result<T>` shape and enforcement.** `readonly record struct Result<T>` decorated with `[MustConsume]`; YGG201 fires on direct discard, expression-statement, and unused-local patterns. Source-generated consumer alternative considered and rejected (friction without proportionate value). See §7 → YGG201.
2. **Layer.Service renamed to Layer.Host.** "Service" overloaded "service layer" (Application) and "host" (composition root). See §3, §4, §5. (Note: superseded in part by decision 7 — per-context Host projects do exist; the original framing of "platform-provided host wires everything up" is realized via the plugin pattern, not via a monolithic catalog.)
3. **Five-realm top-level namespace split.** Peer top-level namespaces: `Norse.Hosting.*` (connective tissue — hosting runtimes, AppHost, ServiceDefaults), `Norse.Abstractions.*` (declared law — abstractions, contracts, shapes), `Norse.Infrastructure.*` (embodied law — concrete platform infrastructure: `InfrastructureDbContext`, `JsonControllerBase`, mediator runtime), `Norse.Primitives.*` (the forge — forged primitives plus the architecture analyzer suite), `Norse.ReferenceData.*` (woven memory across time — taxonomy / audit / event-sourcing), `{Company}.*` (the MGA codebase — one of the realms running on Norse). The boundary between platform realms and the product is "knows about MGA semantics" — anything that doesn't, stays in whichever platform realm matches its role. UI Composition is MGA-shaped despite being structurally reusable. See §4; see CLAUDE.md §5 for the cosmology rationale.
4. **Cross-context UI references resolved through context ownership.** A widget rendering cross-context data lives in the owning context's UI assembly (e.g., a Customer 360 widget lives in `{Company}.Customer.UI` and consumes `{Company}.Billing.Contracts` + `{Company}.Claims.Contracts` via gRPC). Widgets stay dumb about how they're invoked; each `*.UI` assembly also contributes routed Blazor pages for direct URL access. Validated in the UI Composition spec.
5. **.NET version policy.** Single-target, latest stable, migrate at RC1+go-live for the next version. Multi-targeting explicitly not supported. See §13.
6. **Primitives — platform primitives + architecture-enforcement package family.** Top-level namespace `Norse.Primitives.*`, peer to `Norse.Hosting.*`, `Norse.Abstractions.*`, `Norse.Infrastructure.*`, `Norse.ReferenceData.*`, and `{Company}.*`. Primitives is the forge — it produces the load-bearing primitives every other layer rests on AND the forged enforcement tools that strike when the Abstractions layer's laws are broken. Two NuGet packages from this realm: **`Norse.Primitives`** (v1 contents: `Result<T>`, `Money`, parsing stack, UUID v5 namespace registry, `[MustConsume]` attribute) and **`Norse.Primitives.Architecture`** (Roslyn analyzers + BuildCheck rules implementing `YGG001`..`YGG3xx`). The architectural-attribute model itself — the contract the analyzers read — lives in `Norse.Abstractions.Architecture` (declared law, not forged tool). Split-per-concern within the primitives package was rejected: the surface is small enough that a single package isn't a packaging hazard, and consumers benefit from a single dependency anchor for the forged primitives every other layer rests on.
7. **Host plugin pattern, single deployable.** The platform exposes a plugin interface family in `Norse.Abstractions.Hosting` (declared law); every bounded context contributes a plugin; **one** product deployable web host loads every plugin. Scaling is horizontal — more replicas of the same process — not by fragmenting into per-context API servers. (Twenty-five API servers is "ick very quick," in the operator's words. The Norse host runtime is service-agnostic by design; the deployable is the product's composition of plugins, not a fleet.)

   Plugin contracts (in `Norse.Abstractions.Hosting` — the Abstractions layer's declared law):
   - **`IHostPlugin`** (base) — HttpClient configuration, database configuration, DI/IOC configuration.
   - **`IWebHostPlugin : IHostPlugin`** — base + authorization configuration + route configuration (gRPC `[ServiceContract]` endpoints + JSON controllers).
   - **`IWorkerHostPlugin : IHostPlugin`** — base + `BackgroundService` registrations and queue/scheduler bindings.

   The concrete host runtimes that load these plugins live in `Norse.Hosting.{Web.Server|Worker|Migrations.Service}` — Norse-tier connective tissue.

   **No product-tier wrapper interfaces.** Context plugins implement the Abstractions contracts directly. MGA-specific cross-cutting (audit, `NorsePrincipal` flow) is shared middleware configured at the Norse host runtime, not an interface extension. *(Tenancy removed from this list 2026-06-03 — stamp-per-tenant, see `2026-06-03-tenancy-model-design.md`.)*

   Per-context plugin classes split by deployable destination:
   - `BillingPlugin : Norse.Abstractions.Hosting.IWebHostPlugin` lives `internal sealed` in `{Company}.Billing.Server`.
   - `BillingWorkerPlugin : Norse.Abstractions.Hosting.IWorkerHostPlugin` (when Billing has background work) lives `internal sealed` in `{Company}.Billing.Worker`, which references `.Server` for shared internals.
   - Same pattern for every other context: `ClaimsPlugin` / `ClaimsWorkerPlugin`, `PolicyPlugin` / `PolicyWorkerPlugin`, `LayoutPlugin`, `AuthPlugin`, etc.

   No separate `Plugin` project; a single class doesn't earn its own assembly.

   Deployable host projects (the **only** runtime deployables for server-side workloads):
   - **`Norse.Hosting.Web.Server`** — single ASP.NET Core deployable. Thin Program.cs that creates the Norse-tier web host runtime (`Norse.Hosting.Web.Server`) and registers every product context's `{Context}Plugin` from its `.Server` assembly plus every `{Context}WorkerPlugin` from its `.Worker` assembly. All gRPC services, all JSON controllers, all auth endpoints share one HTTP endpoint; background workers ride along as `BackgroundService` instances in the same process unless workload isolation later demands splitting.
   - **`Norse.Hosting.Worker`** *(optional, future)* — a sibling deployable that creates `Norse.Hosting.Worker` and loads **only** the `{Context}WorkerPlugin` classes from each `.Worker` assembly, used only if a specific workload's resource profile (memory, runtime, throughput) justifies splitting from the web process. When `WorkerHost` is deployed, `Norse.Hosting.Web.Server` is configured to stop loading the `.Worker` plugins so the two deployables don't duplicate background-job execution. Default: don't.

   Operational consequence: bumping a single context's behavior triggers one deployment, not 25. CPU/memory profiles are shared until a real signal forces a split. Service-discovery wiring is trivial because in-process service references are direct method calls — gRPC channels matter only for clients reaching IN to the host.

   This decision supersedes both the §15.2 "per-context Host projects do not exist" framing and the prior version of this decision (§15.7) that assumed `{Company}.{Context}.Web.Server` / `{Company}.{Context}.Worker` per-context deployables. Per-context server-side deployables do not exist; a single composed host does.

8. **Persistence inversion: per-service DbContexts owned by Infrastructure.** *(Amended 2026-06-03 to match the persistence and messaging specs.)* `Norse.Abstractions.Infrastructure` declares the repository contract family (`IDocumentRepository<T>`, `ICommandRepository<T>`, `ICachedRepository<T>`, `ITemporalRepository<T>` — no `IUnitOfWork`; the messaging library's per-handler session owns the transaction). `Norse.Infrastructure.Persistence` declares the per-service DbContext family (`BillingDbContext`, `ClaimsDbContext`, etc.) as platform-internal types, scans each `{Company}.{Context}.Worker` for `IEntityTypeConfiguration<T>` impls at startup and applies them to that service's DbContext, and provides the concrete repository implementations. Workers contribute entity classes + EF configurations + business logic; they never see a DbContext, never call `SaveChangesAsync`, never know whether `BillingDbContext` and `ClaimsDbContext` resolve to the same connection (schema isolation) or different ones (full database isolation). That choice is Infrastructure's at deployment time. Cross-context queries are impossible at the type system: a Billing handler cannot construct `ICommandRepository<Claim>` because `Claim` is not visible from `{Company}.Billing.Worker` (YGG004), and the repositories scoped to `BillingDbContext` don't surface other contexts' entities even if the type were somehow visible. `ITemporalRepository<T>` resolves to ReferenceData (the across-time realm), not Infrastructure.

9. **Product namespace sequestered to bounded-context services only.** `{Company}.*` holds **only** the per-context assemblies (`.Contracts` / `.Components` / `.Backend` / `.Server` / `.Worker` / `.Migrations` / `.JsonApi`) for each bounded context. Every other artifact — deployables (`Norse.Hosting.Web.Server`, `Norse.Hosting.Worker`, `Norse.Hosting.Migrations.Service`, `Norse.Hosting.Web.Client`, `Norse.Hosting.App`, `Yggdrasil.DevServer`), the UI Composition framework (`Norse.Infrastructure.UI.Composition.*`), the platform infrastructure (`Norse.Infrastructure.Persistence`, `Norse.Infrastructure.Api`, `Norse.Infrastructure.Mediator`), the hosting runtimes (`Norse.Hosting.*`) — moves to whichever realm root matches its role. Deployables are Norse because their identity is platform composition; what they compose (the product's plugins) is configuration, not project name. Practical payoff: if the consumer-facing brand changes later, only the per-context assemblies migrate; all of platform stays.

## 16. Open Questions

(No remaining open questions at this layer. Future questions arising from implementation will be appended here.)

## 17. Done Criteria

This spec is "done enough to implement." Each criterion is resolved or accepted:

- [x] Open questions §16 — none remain at this layer.
- [x] Rule matrix (§5) reviewed and rewritten around the DAG principle. Primitives is the fulcrum; the matrix's job is to enforce a one-way DAG, not to enumerate layer ceremony. Intra-service concrete example added showing the actual dependency graph for a single context.
- [x] Diagnostic catalog (§6) accepted as the first-draft surface. Future rules will be added as patterns earn enforcement; this is not a one-shot definition.
- [x] Packaging story (§10) resolved: Norse is the meta-repository. It holds a .NET Aspire AppHost (`Norse.Hosting.AppHost`) that orchestrates the full local-dev environment (Postgres, RabbitMQ, OpenIddict, every per-context plugin) without requiring a VPN or cloud connection. Submodules under the meta-repo include the platform components — `norse-primitives`, `norse-primitives-architecture`, `norse-abstractions-hosting`, `norse-hosting`, `norse-infrastructure-persistence`, `norse-infrastructure-api`, `norse-abstractions-mediator`, `norse-infrastructure-mediator` — and the product business-context repos (`{company}-billing`, `{company}-claims`, etc.). `Directory.Build.props` lives at the meta-repo root and is inherited by every submodule via MSBuild's implicit-import behavior.
