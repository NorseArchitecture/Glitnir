# Project Structure — Per-Context Projects and Deployables

Extracted from CLAUDE.md §5. This is the full catalog of project responsibilities and reference rules. The compressed table and the hard-wall rule remain in CLAUDE.md; this file is the authority on the details.

---

## Per-Bounded-Context Projects

Split by deployable; no per-context Host deployables.

- `{Company}.{Context}.Contracts` — published events, `I{Context}Api`, request/response shapes. The single project other contexts may reference.
- `{Company}.{Context}.Components` — Blazor components, widgets (`[Widget(...)]`), routed pages (`@page`). Compiles into WASM/MAUI; must not reference `Server`, `Worker`, or `Backend`.
- `{Company}.{Context}.Backend` — the server-side shared assembly: server→worker commands, Mongo document C# records (server serves them; worker writes them), shared server-side options/constants. Referenced by `.Server` and `.Worker`; never by `Components`; never client-reachable (analyzer-enforced). Exists iff `.Server` and `.Worker` both exist — a context ships only the projects its persistence stance demands.
- `{Company}.{Context}.Server` — the web tier: authority validation, Mongo reads + shim writes, command dispatch via `IMessageSession`, gRPC services, JSON controllers, UI-push event subscriptions, **and** an `internal sealed class {Context}Plugin : Norse.Abstractions.Hosting.IWebHostPlugin`. **No SQL entities, no EF, no DbContext** — the system of record is unreachable from this assembly by construction. Pure worker-only contexts skip `.Server`.
- `{Company}.{Context}.Worker` — the system-of-record tier: SQL entity classes, `IEntityTypeConfiguration<T>` impls (relationships, indexes, check constraints, schema/table mapping), business logic, NServiceBus message handlers, sagas, `BackgroundService` impls, **and** `internal sealed class {Context}WorkerPlugin : Norse.Abstractions.Hosting.IWorkerHostPlugin`. Domain and Application are folder-level. **No DbContext declaration or injection** — handlers consume the repository contract family from `Norse.Abstractions.Infrastructure`.

**`.Server` and `.Worker` are mutually invisible — hard walls.** Neither references the other; both reference `.Backend` and `.Contracts`. The worker never references ASP.NET Core; the server never references EF Core or entity types. They meet only at the queue.

### Optional Add-Ons

- `{Company}.{Context}.Migrations` — independently versioned NuGet. Only when a separately-versioned artifact is warranted.
- `{Company}.{Context}.JsonApi` — folded into `.Server` by default. Stays separate only when partner-JSON surface or lifecycle warrants it.

---

## Server-Side Deployables

All under `Norse.Hosting.*`:

- `Norse.Hosting.Web.Server` — **the** server-side ASP.NET Core deployable. Thin Program.cs registers every `{Context}Plugin` and every `{Context}WorkerPlugin`. All gRPC, gRPC-Web, JSON controllers, and `BackgroundService` ride in one process — plus the Blazor Web App surface: SSR + interactive-server circuits for Shell and serving the WASM bundle (UI Composition spec §7.1). Scale by replicas. Layout and Auth participate as plugins.
- `Norse.Hosting.Worker` *(optional, deferred)* — sibling, registers only `.Worker` plugins. When active, `Norse.Hosting.Web.Server` stops loading `.Worker` assemblies to avoid duplicate execution.
- `Norse.Hosting.Migrations.Service` — long-running orchestrator. Applies each `{Company}.{Context}.Migrations` against its `Norse.Infrastructure.Persistence` DbContext in dependency order; never exits non-zero; `/health` is the readiness gate.

---

## Client Deployables

All under `Norse.Hosting.*`:

- `Norse.Hosting.Web.Client` *(WASM deployable)* — the `.Client` half of the Blazor Web App. Thin `Program.cs`: `AddShell()` + `AddShellClients(apiBase)`. References `{Company}.Shell.Components` + the client hosting runtime.
- `Norse.Hosting.App` *(MAUI deployable)* — native shell, `BlazorWebView`; same two calls in `MauiProgram.cs`. References `{Company}.Shell.Components` + the MAUI hosting runtime.

Client deployables are Components-only — never `.Server`, `.Worker`, or `.Backend`. (`Norse.Hosting.DevServer` was deleted 2026-06-05: superseded by the `InteractiveServer` render mode on `Norse.Hosting.Web.Server` under Aspire — UI Composition spec §7.1.)

> **Open structural call (surfaced by the flush):** the deployable and its hosting-runtime library both want `Norse.Hosting.Web.Client` — under codenames they were distinct (`Yggdrasil.WasmClient` deployable vs. `Yggdrasil.Hosting.Web.Client` runtime). The five-name family collapses cleanly *unless* the runtime lib must stay a separate project; if it must, the lib needs a distinguishing segment (e.g. `Norse.Hosting.Web.Client.Runtime`). Buvy's call — left descriptive ("the client hosting runtime") above to avoid a self-reference until then.

---

## Test Projects

Mirror their target with the `.Tests` suffix. Tests depend on their target; nothing depends on tests.
