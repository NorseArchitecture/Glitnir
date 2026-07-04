# Platform Decomposition — Bounded-Context Map and Submodule Map

Reference detail relocated from `CLAUDE.md` (§3 and §5 carry the rules and point here for the full tables and rationale).

## Bounded-Context Ownership

The contexts below are the **insurance product realm's** (the greenfield MGA — the first product realm). Each product realm (energy retail, logistics, … — multi-product spec, 2026-06-07) defines its own complete set of bounded contexts under `{Company}.{Context}.*`; names that superficially repeat (two verticals' `{Company}.Billing`) are different animals sharing no code. The other verticals' context maps land with their own domain specs, in their own courts.

Each context owns its own schema, plugin/service boundary, and ubiquitous language.

| Context | Owns | Does Not Own |
|---|---|---|
| **Product** | Coverage forms, rate manuals, eligibility rules, state filings, factor tables, product versions | Quoting, policy state |
| **Distribution** | Producer (agent/broker) registry, appointment status, commission schedules, API consumer credentials | Customer identity, policy ownership |
| **Underwriting** | Quote, risk scoring, automated decision, referral routing, declination | Policy lifecycle, rate manuals |
| **Policy** | Policy lifecycle: bind, issue, endorse, renew, cancel, reinstate; versions and effective-dated changes | Premium calculation, claims, billing |
| **Billing** | Invoicing, payment plans, premium recognition, receivables, agency-bill vs. direct-bill | Bordereaux production, claims payments |
| **Customer** | Customer identity, contact info, communication preferences, portal sessions, consent records | Producer identity, policy ownership |
| **Claims** | FNOL, claim file, adjuster assignment, reserves, claim payments, claim closure | Policy facts (consumes via published snapshot), fraud determination |
| **Reporting** | Bordereaux production for the fronting carrier, regulatory reports, internal reporting, warehouse feeds | Operational data |

## Cross-Cutting Platform Services

Platform-wide; not bounded contexts. Namespace (code); codename (lore) maps in `docs/codenames.md`.

| Namespace | Concern | Codename (lore) |
|---|---|---|
| `Norse.Identity` | EF persistence for ASP.NET Identity and OpenIddict: entities, conventions, and migrations; sealed server-side, never referenced from WASM or MAUI | Himinbjorg |
| `Norse.Access` | Auth services on Himinbjorg's identity record: one access ruleset across Blazor Server, WASM, and MAUI, with admin components and the backing gRPC service | Heimdall |
| `Norse.ReferenceData` | Canonical external-standard reference data — ISO country/currency codes, IANA time zones. `.Data` (entities, view models, TSV seeders, migrations) is one repo; `.Components`/`.Web.Server`/`.Worker` (serving layer) is a second, split for independent release cadence | Mimisbrunnr (data) / Mimir (serving) |
| `Norse.Observability` | Logs, metrics, traces, alerting, SLO tracking | *(unnamed — no repository yet; name only when real, per `codenames.md` rule #4)* |
| `Norse.AI` | Model serving, embeddings, RAG over policy/claim docs, decision support | *(unnamed — no repository yet; `Mimir` reassigned to reference data 2026-07-03, see `the-crooked-path.md` #9)* |
| *(unplaced)* | Fraud detection / legal enforcement: signals, case management, SIU referral, recovery — **platform-vs-product placement unsettled** | *(unnamed — placement isn't settled and the component isn't real; a codename attached to either half of that uncertainty is the same rule-#4 violation, just dressed as "provisional." `Tyr` and `Valkyrie` returned to the bench 2026-07-03, see `codenames.md`.)* |
| *(unplaced)* | Claims triage: routing by severity, complexity, fraud signals — **placement unsettled** | *(same as above)* |

## Repository Map

**Amended 2026-06-11:** the `norse-{function}` one-repo-per-concern model is superseded. **One repository per platform realm, named for the lore; the projects and namespaces inside are named for function** (`Norse.{Function}.*`). Open the org and you tour the cosmos; open the `.slnx` and every project says what it does. Live at `github.com/NorseArchitecture`:

| Repository (lore) | Namespace root | Contents |
|---|---|---|
| **Svartalfheim** | `Norse.Primitives.*` | Forged primitives (`Result<T>`, `Money`, parsing stack, UUID v5 registry, `[MustConsume]`) + the hammer: analyzers and BuildCheck rules (`Norse.Primitives.Architecture`, `YGG001`..`YGG3xx`). |
| **Asgard** | `Norse.Abstractions.*` | Declared law: attribute model (`Norse.Abstractions.Architecture`), host plugin contracts, repository contract family + shared entity bases + audit/timestamp interfaces, mediator law (`[MediatorService]`, `ICommandRequest<T>`, validator/authorizer contracts, forwarder + projection source generator — dispatch core is martinothamar/Mediator). |
| **Midgard** | `Norse.Infrastructure.*` | Embodied law: repository implementations (riding on Urdarbrunnr's EF foundation); `JsonControllerBase<TService>` + JSON-face primitives; mediator runtime (fixed validate → authorize pipeline, strict-single helper, paging clamps, `ErrorCategory` door table); UI Composition framework. |
| **Urdarbrunnr** | `Norse.EntityFramework.*` | The EF Core foundation layer: entity base types, DbContext foundations, conventions, value converters, and the migrations chassis. The EF foundation Midgard's concrete family rides on, governed by Asgard's declared law. |
| **Ratatoskr** | `Norse.NServiceBus.*` | NServiceBus endpoint configuration, saga infrastructure, message conventions, and transport wiring. Asgard declares the messaging surface; Ratatoskr carries it. |
| **Yggdrasil** | `Norse.Hosting.*` | Hosting chassis: server deployables (`Norse.Hosting.Web.Server`, `Norse.Hosting.Worker`, `Norse.Hosting.Migrations.Service`) and client deployables (`Norse.Hosting.Web.Client`, `Norse.Hosting.App`). `Norse.Hosting.DevServer` deleted 2026-06-05 — superseded by `InteractiveServer` render mode on `Norse.Hosting.Web.Server` (UI Composition spec §7.1). |
| **Himinbjorg** | `Norse.Identity.*` | EF persistence for ASP.NET Identity and OpenIddict: entities, conventions, and migrations; sealed server-side, never referenced from WASM or MAUI. |
| **Heimdall** | `Norse.Access.*` | Auth services riding on Himinbjorg: one access ruleset across Blazor Server, WASM, and MAUI, with admin components and the backing gRPC service. |
| **Mimisbrunnr** | `Norse.ReferenceData.Data` | Entities, view models, TSV seeders (nietras Sep), and migrations for canonical reference data: ISO country/currency codes, IANA time zones. |
| **Mimir** | `Norse.ReferenceData.Components` / `.Web.Server` / `.Worker` | Serving layer on Mimisbrunnr: Blazor components, gRPC service host, and the background worker that keeps reference data current. Split from Mimisbrunnr for independent release cadence, not a distinct bounded context. |
| **Naglfar** | `Norse.DesignSystem.*` | Design tokens, radii, and component primitives — standalone for now, no declared consumers. |
| **Bifrost** | `Norse.Orchestration.*` | Local developer meta-repository: the .NET Aspire AppHost composing services, databases, queues, and configuration; carries the realm repos as submodules (relative URLs, tracking `master`). A reference composition — consumers are expected to build their own bridge from the constituent realms. |

Consequences and rulings of the amendment:

- **Law-and-hammer pairs now version across repo boundaries.** The Abstractions + implementation pairs that previously traveled in one submodule (architecture, mediator, persistence, hosting) split as law → Asgard, hammer → Svartalfheim/Midgard. Lockstep mechanics across repos fold into the build-substrate session (reconciliation tracker 4.2).
- **`ServiceDefaults` (ruled 2026-06-11):** Midgard if possible; Yggdrasil only if it carries shared runtime context that touches all the composition runtimes; never Bifrost. The `AppHost` keeps its Aspire-conventional name as `Norse.Orchestration.AppHost` in Bifrost.
- **`norse-referencedata` is dissolved (2026-06-11):** temporal contracts (`ITemporalRepository<T>`) → Asgard; implementations → Midgard; universal geographic/world content → a thin library, named when real; vertical reference content is sovereign (`{Company}.ReferenceData.*` — loss costs are insurance's business, transit zones are logistics'). Norns returned to the bench (`docs/codenames.md`). **The deferred half landed 2026-07-03:** the universal geographic/world-content library is real — Mimisbrunnr (`Norse.ReferenceData.Data`) and Mimir (`Norse.ReferenceData.Components`/`.Web.Server`/`.Worker`), split across two repos for release-cadence reasons, not two bounded contexts.
- **Future platform realms** (`Norse.Observability`, `Norse.AI`, `Norse.Warehouse`) each get their own lore-named repository when they land, chosen fresh at that time — `Muninn`, `Gjallarhorn`, and `Mimir` were bound to these prematurely and walked back 2026-07-03 (`the-crooked-path.md` #9); none currently reserve a name.

Product realms follow the same pattern under their own roots:

| Repository | Realm | Contents |
|---|---|---|
| `{company}-{context}` | per product | Per-context business code. One repo per bounded context. |
| `{company}-shell` | per product | The stitched app: `{Company}.Shell.Components` — single client-safe assembly composing auth + every context's UI into the dashboard (UI Composition spec, 2026-06-05). |
| `{company}-{function}` | per product | Cross-cutting services that encode the product's domain semantics — descriptively named within the product. |

Each product realm owns its repos (and its own Shell) independently, so a product can be cleanly divested (multi-product spec §7.2). The **exact repo/package mechanics across N products are deferred** to the build-substrate session (reconciliation tracker 4.2) and the platform-IP-ownership decision (multi-product spec §11 #1) — not settled here. (Product realms keep their codename as the namespace root because the codename is the company brand; the platform substrate carries the lore on its repositories and the function in its namespaces.)

## Why the Bounded Contexts Decompose This Way

- **Product separate from Underwriting** — rate manuals and forms have their own lifecycle (state filings, effective dates, regulatory approvals) independent of any quote or policy.
- **Customer separate from Distribution** — a customer is not a producer; conflating identity systems causes audit and authorization problems.
- **Claims separate from Policy** — claims operate on a snapshot of policy facts at the date of loss; live policy edits must not retroactively change claim adjudication.
- **Billing separate from Policy** — premium accounting outlives policy state (collections continue after cancellation; refunds after expiration).
- **Reporting separate from everything** — bordereaux is a contract with an external party; schema and cadence must be decoupled from any single source system.
