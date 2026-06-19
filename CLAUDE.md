# CLAUDE.md — Glitnir (Enterprise .NET Platform — Design Court)

## 0. Wrong Root — Halt

If you are reading this because **Glitnir itself is the Claude Code session root** — someone ran `claude` from inside this directory instead of `../Bifrost` — stop here. Do not read further, do not propose changes, do not run anything.

Tell the user: every Norse Architecture session starts from **Bifrost**. Org-wide settings (the `superpowers` plugin, permission rules) only apply when Bifrost is the actual session root — Claude Code never merges a submodule's own `.claude/settings.json` into a parent-launched session. Exit, `cd ../Bifrost`, and run `claude` there instead.

This repo's own `.claude/settings.json` carries a `SessionStart` hook that should already have blocked this session before this file was ever read. If you're reading this anyway, hooks were bypassed, disabled, or failed — halt regardless; this rule does not depend on the hook to hold.

---

Authoritative cold-start context for any Claude Code session in this repo. Read top to bottom before proposing anything. Where a rule here conflicts with a default behavior, this file wins.

---

## 1. Project Overview and Mission

**This repository is Glitnir** — the shining hall of judgment, the design court. The Norse Architecture is designed here: specs, proofs of concept, and plans are heard and judged; production code is the verdict, rendered only after the narrative converges. The cosmos is live at `github.com/NorseArchitecture` — one repository per platform realm, with **Bifrost** (`Norse.Orchestration`) as the meta-repository that composes them and carries Glitnir at `./Glitnir` as the court of record (§5 → Repositories; topology spec 2026-06-11).

**Norse** is a **multi-product platform — a company-of-companies, a venture studio whose product is companies.** Its **product realms** are separately-capitalized operating entities (own cap table, investors, compliance perimeter) — the founding verticals are **insurance** (a greenfield MGA, the first product), **deregulated energy retail**, and **logistics / wholesale distribution** — sharing one rigorously-built substrate: the platform realms, Abstractions' declared contracts, and Infrastructure's rails. The first product is not the platform's reason. The substrate *is* the demonstration (the world builds software wrong); standing up a new fundable entity is mostly `{Company}.{Context}.*` domain code dropped onto rails that already exist. Full topology: `docs/Platform/specs/2026-06-07-multiproduct-platform-design.md`.

Four levels, each with exactly one job — **platform realms** (the shared substrate, branded *"the Norse Architecture"* and namespaced **`Norse.{Function}`**) · **product realms** (separately-capitalized companies, namespaced **`{Company}.{Context}.*`**) · **bounded contexts** (descriptively named) · **tenant stamps** (deployments; a stamp = product × tenant). **Codenames are lore** — the genesis/inspiration story, kept in the dictionary (`docs/codenames.md`), the README, and the repository names; **code and specs use the function.** The operating model answers "where does this code belong?" with one right answer:

| Realm (lore) | Namespace (code) | Kind | Purpose |
|---|---|---|---|
| **Svartalfheim** | `Norse.Primitives` | platform | Forged primitives + analyzers (`Result<T>`, `Money`, parsing, UUID v5, `[MustConsume]`) — forged below the domain, compose it |
| **Asgard** | `Norse.Abstractions` | platform | Declared law — contracts, plugin interfaces (`IWebHostPlugin`/`IWorkerHostPlugin`), repository contract family, attribute model |
| **Midgard** | `Norse.Infrastructure` | platform | Embodied law — DbContext family, EF conventions, repository impls (incl. temporal), `JsonControllerBase<TService>`, mediator runtime, UI Composition |
| **Urdarbrunnr** | `Norse.EntityFramework` | platform | The well's record — entity base types, DbContext foundations, conventions, value converters, and the migrations chassis; the EF foundation Midgard's concrete family rides on, governed by Asgard's declared law |
| **Yggdrasil** | `Norse.Hosting` | platform | Connective tissue — hosting runtimes (`Norse.Hosting.{Web.Server\|Web.Client\|App\|Worker\|Migrations.Service}`) and deployables (§5) |
| **Bifrost** | `Norse.Orchestration` | platform | The bridge in — the Aspire AppHost meta-repository composing every resource; carries the realm repos (and Glitnir) as submodules |
| **Muninn** | `Norse.Warehouse` | platform | Data warehouse; the **only** realm permitted to read across service (and product) boundaries |
| **Heimdall** | `Norse.Auth` | platform | Cross-cutting auth — the gate every product passes through (OpenIddict, identity stores, `IAccountApi`) |
| **Gjallarhorn** | `Norse.Observability` | platform | Cross-cutting telemetry, alerting; the system-wide signal |
| *(product realms)* | `{Company}.{Context}.*` | **product** | The operating entities (e.g. insurance / energy / logistics verticals) — per-context assemblies (`.Contracts`/`.Components`/`.Backend`/`.Server`/`.Worker`/`.Migrations`) |

> The ReferenceData realm dissolved 2026-06-11 — temporal contracts went to Abstractions, implementations to Infrastructure, vertical reference content is sovereign (`{Company}.ReferenceData.*`). See `docs/codenames.md`.

Each product realm is named for its **governing figure** — the Norse figure whose myth *rules that vertical* (§6 rule #3). Platform services, by contrast, are named for function (Heimdall guards, Gjallarhorn sounds the alarm). The actual governing-figure assignments are each venture's own record, kept out of the platform corpus until the venture launches its brand (topology spec §2.6). The mission below is written for the insurance product realm (the first product); each product realm inherits the same discipline and asserts its own domain mission.

### Mission (the first product — insurance)

The insurance product realm automates underwriting, customer service, and claims processing to:

1. **Reduce operational cost.** Automate work that doesn't require judgment. Reserve human time for ambiguity, escalation, and care.
2. **Serve customers faster and more fairly.** Decisioning latency is a feature requirement. "Fair" means same facts → same decision.
3. **Be less litigious than the industry norm.** Resolve disputes; don't grind people down. Soft denials, attritional friction, and bad-faith claims handling are out of scope. Prefer resolution-friendly design even when slightly more expensive short-term.
4. **Be unambiguous and aggressive with provably fraudulent actors.** "Fair to claimants" ≠ "tolerant of fraud." Where fraud is *established* (not suspected), act decisively.

These are constraints, not aspirations. Every feature, refactor, and trade-off answers to them.

### Relationship to the Fronting Carrier

The insurance product realm operates as an MGA on a fronting carrier's paper and produces the bordereaux the carrier consumes. The carrier's own platform is **separate infrastructure**; shared conventions are deliberate alignment, not code reuse, unless explicitly versioned and published as a package by the carrier.

---

## 2. Architecture Principles (Decision Rules)

Non-negotiable. **Rules**, not aspirations.

### 2.1 Pit-of-Success Engineering

> The easy path must be the correct path.

Before merging a new abstraction, ask: "Can a developer in a hurry use this incorrectly?" If yes, redesign until the wrong usage won't compile, won't bind, or won't run. Compile-time enforcement preferred; runtime fallback; documentation alone is not enforcement.

### 2.2 Database Self-Defense

> The database enforces its own integrity, regardless of how data arrives.

Every invariant that matters exists as a database constraint (CHECK, NOT NULL, UNIQUE, FK, exclusion, partial index). EF Core emits these via migrations; we don't rely on EF to enforce them application-side alone. Invariants that cannot be expressed in the database must exist in **two** places: application layer **and** a periodic verification job that fails loudly on violation. No exceptions for "internal" or "temporary" tables.

### 2.3 Least Accessibility Until the Door Must Be Opened

> Start closed. Open only when a concrete requirement demands it.

Default to `internal sealed`. Default to non-`partial`. Default to no `set` accessor (init-only). Default to no extension points. Each escalation (`internal` → `public`, `sealed` → unsealed, `private set` → `set`) requires a concrete justified caller, not a hypothetical future need. Source-generated types are non-partial by default (one sanctioned exception: co-located `[LoggerMessage]` methods — performance posture spec §4.2).

- **Defaults are expressed by omission** (`omit_if_default`): `class Foo`, not `internal class Foo`; bare members, not `private` members. A visible accessibility keyword is a deliberate escalation someone chose — never noise.
- **Tests reach internals through one sanctioned door:** every repo's `src/Directory.Build.props` grants `<InternalsVisibleTo Include="$(AssemblyName).Tests" />` — declared once, never per-csproj. `public`-for-testability is never justified.
- **`sealed` is enforced, not aspirational:** CA1852 (seal internal types with no subtypes) is an error platform-wide. Unsealing requires an actual derived type in the codebase — inheritance takes effort and intent, by design.

### 2.4 Strict Assembly Boundary Enforcement

> Domain layers do not reference infrastructure. Infrastructure does not leak into domain.

`{Company}.{Context}.Components` may not reference EF Core, ASP.NET Core server types, messaging libraries, file system, or any server-side concrete infrastructure (Components compile into WASM/MAUI bundles; server types break the bundle). Cross-context references between `{Company}.{Context}.Server` (or `.Worker`, or `.Backend`) projects are also forbidden; contexts integrate via published events and the gRPC API surface in `{Company}.{Context}.Contracts`. Within a context, `.Server` and `.Worker` are mutually invisible (§5) — the worker never references ASP.NET Core, the server never references EF Core or entities. Internal organization inside `.Worker` (Domain / Application / EF mappings) is folder-level, not assembly-level. Violations are **build errors** (`YGG003`, `YGG004`).

### 2.5 Convention Over Configuration; Simplicity Over Ceremony

> Implicit magic is a liability. Ceremony for its own sake is also a liability.

Every convention appears in this file or a referenced doc — "everybody knows" is tribal knowledge waiting to be lost. Conventions worth having are worth enforcing automatically (analyzer, source generator, build target, test).

But configuration must justify itself. If a default is correct in the overwhelming majority of cases, don't require every consumer to specify it. Builders, options classes, and extension points that exist solely so something "can be extended later" are friction without payoff — delete them. Prefer the shortest path that fails loudly when wrong over the longer path that accommodates a hypothetical future need.

### 2.6 Hard-Fail on Ambiguity

> Reject ambiguous input. Do not guess.

Dates, monetary amounts, currency codes, identifiers, percentages, and rates have **exactly one** accepted representation per ingress path, declared up front. Ambiguity (`01/02/2026` with no declared culture) is an immediate parse failure, not a probabilistic interpretation. Silent fallbacks (defaulting to current year, assuming USD, inferring "P" means policy) are forbidden. Internal handoffs use strongly typed values (`Money`, `PolicyNumber`, `EffectiveDate`) that cannot be constructed ambiguously.

### 2.7 Push Errors Upstream — Fail Fast, Fail Loud, Fail Hard

> The closer to the source of a mistake we surface failure, the cheaper the fix.

Preference order, strictly:

1. **Compile time** — static typing, source generators, Roslyn analyzers, build-time SDK checks
2. **Build time** — analyzer warnings ratcheted to errors, MSBuild targets, test execution
3. **Application startup** — DI validation, configuration binding, schema verification, migration assertions
4. **Request / message boundary** — input validation, contract enforcement, version checks
5. **Production runtime** — last resort; reaching here means an earlier layer failed

**Silent fallback is never acceptable.** A missing rate factor is a hard fail, not `1.0` — insurance silently coerced toward "no effect" is insurance that mispriced something.

Where two designs are otherwise equally simple, prefer the one that fails earlier. Where a design *lets* an error slip past a layer, that is a design problem to fix — not a runtime concern to handle.

### 2.8 Subagent-Orchestrated, Test-Driven Implementation

> Orchestration sequences tasks. TDD governs how each one is coded. Neither substitutes for the other.

`superpowers:subagent-driven-development` is the **default** orchestration skill for every implementation plan — not a recommendation among equals. `superpowers:executing-plans` is the narrow exception, reached for only when the work specifically needs a separate session with human review checkpoints; it is never an interchangeable alternative chosen by preference. Every plan's REQUIRED SUB-SKILL line names the orchestration default **and** `superpowers:test-driven-development` together — never one without the other. Subagent orchestration without TDD ships code nobody watched fail first; TDD without orchestration loses the plan-as-source-of-truth discipline that keeps a multi-task implementation coherent across context windows. Both, every time, platform-wide — realm CLAUDE.md files state this locally; this is the rule they all point back to.

This is retroactive only in spirit, not in the record: a plan already executed and preserved verbatim (§5 → Repositories) is not rewritten to add this line after the fact — the lesson lands on the next plan, not a rewritten history.

---

## 3. Bounded Context Map

Each context owns its own schema, plugin/service boundary, and ubiquitous language. **Do not collapse them prematurely.** The map below is the insurance product realm's (the first product, and the exemplar); every product realm defines its own complete context map in its own court.

### Core Domain Contexts

Product · Distribution · Underwriting · Policy · Billing · Customer · Claims · Reporting. Each context's ownership boundary — what it owns and what it explicitly does **not** — is the table in `docs/decomposition.md`; consult it before placing work.

### Cross-Cutting Platform Services

Platform-wide; not bounded contexts (function name in code; codename in parens is lore — `docs/codenames.md`): **`Norse.Auth`** (Heimdall) · **`Norse.Observability`** (Gjallarhorn) · **`Norse.AI`** (Mimir — decision support) · **`Norse.Notifications`** (Ratatoskr, when it lands). Shared across every product realm, never specific to one. **Fraud (Tyr) and triage (Valkyrie) are *unplaced*** — insurance-lifeblood but minor elsewhere, so platform-vs-product is unsettled (in the ether — §5, `docs/codenames.md`). Full concern table: `docs/decomposition.md`.

### Integration Rules

Contexts integrate via **published domain events** over RabbitMQ (CloudAMQP):

- Each context publishes on its own logical endpoint. Subscribers bind queues; publishers don't know who's listening.
- Events are **versioned** from day one. Breaking changes ship a new event type; old types are deprecated, not mutated.
- No synchronous cross-context RPC for write paths. Cross-context reads go through a published read model or explicit ACL.
- Fraud and AI consume from every context but publish back narrowly (fraud signals, decision recommendations) — they are **advisors**, not authorities. The originating context retains the decision.
- Reporting consumes from every context and is the only context permitted to maintain denormalized cross-context state.

### Why This Decomposition

Rationale per split (Product ≠ Underwriting, Customer ≠ Distribution, Claims ≠ Policy, Billing ≠ Policy, Reporting ≠ everything): `docs/decomposition.md`. The one rule worth restating: claims adjudicate against a **snapshot of policy facts at the date of loss** — live policy edits never retroactively change a claim.

---

## 4. Technology Decisions

### Runtime and Language

- **.NET 10, C# (latest).** Single runtime for service, CLI, tooling. AOT-clean where feasible.
- **Tabs, 4-space width.** Whitespace-aware/ecosystem exceptions declared in the root `.editorconfig` with reasons: YAML/Markdown/JSON 2-space, Python/F#/Razor 4-space (Razor because the VS formatter is editorconfig-blind upstream — dotnet/razor #4406).
- **Don't fight the ecosystem.** Where an ecosystem's standard tooling has a fixed convention (Black's 4-space Python, dotnet CLI's 2-space JSON), the ecosystem wins and the exception is declared inline with its reason.
- **`var` for return assignments only.** Construction uses target-typed `new()` with explicit type on the left. Reading top-down should never require chasing a method signature to learn a type.

### Persistence

- **PostgreSQL (Neon)** as the primary OLTP store — rich constraint expressiveness, exclusion constraints, partial indexes, `jsonb`, range types, `INTERVAL`, RLS, operator-class flexibility.
- **TimescaleDB** for time-series-heavy data (claim event timelines, audit logs, metric persistence).
- **pgvector** for embeddings feeding AI.
- **MongoDB as the operational read store.** `.Server` serves reads and idempotent shim writes from per-context Mongo databases (wire shapes marked `IWireShape`); `.Worker` enriches them after the system of record commits. Postgres remains the system of record; analytical reads bypass Mongo entirely (Warehouse feeds Snowflake from Postgres). Sanctioned inversions where Mongo **is** the system of record: identity (§4 → Auth) and UI layout persistence (UI Composition spec, 2026-06-05). Full design: `docs/Midgard/specs/2026-05-21-midgard-persistence-design.md`.
- **Entity Framework Core** with snake_case and `MaxLength` conventions. **The abstract, law-enforcing base context — audit stamping, convention enforcement — lives in `Norse.EntityFramework` (Urdarbrunnr; ruled 2026-06-11).** The concrete per-service DbContext family (`BillingDbContext`, `ClaimsDbContext`, …) is **source-generated and `file`-scoped — unreferenceable by construction (ruled 2026-06-11)**: each `.Worker` declares entities and `IEntityTypeConfiguration<T>` pairs (entity ⇄ configuration forced at build time), the generator emits the sealed context plus the DI wiring that closes `Norse.Infrastructure.Persistence`'s open-generic repository implementations over it, and a design-time twin generated into `.Migrations` carries `dotnet ef`. The bounded repository surface (keyset-only paging with required total ordering, fail-loud sweep limits, tracking fixed per contract, materialized `IReadOnlyList<T>`, first-class count/exists, declared aggregate graphs, bulk ops refused) is ruled in `docs/Urdarbrunnr/specs/2026-06-11-entityframework-context-provenance-decision-inputs.md`; the formal verdict is gated on one PoC (EF design-time discovery of file-local types). Services never inject a DbContext or call `SaveChangesAsync`; SQL entities live solely in `.Worker` — the web tier cannot reach the system of record by construction.
- **Infrastructure chooses isolation** at deployment time: shared connection string (schema isolation) vs. distinct (full DB isolation). Tenancy never enters this choice — a tenant is a whole deployment stamp (§4 → Tenancy), so per-stamp values are just configuration.
- **Repository inversion.** `Norse.Abstractions.Infrastructure` declares the four repository contracts: `IDocumentRepository<T>` (Mongo wire shapes) plus the worker-only `ICommandRepository<T>`, `ICachedRepository<T>`, `ITemporalRepository<T>` (declared in Abstractions, implemented in `Norse.Infrastructure` — ReferenceData realm dissolved 2026-06-11). **No `IUnitOfWork`** — the messaging library's per-handler session owns the transaction. `Norse.Infrastructure.Persistence` implements against the per-service DbContexts. Cross-context queries are type-system impossible (`YGG004`); cross-context aggregation is Warehouse's job.
- **Migrations live in `{Company}.{Context}.Migrations`** — independently versioned NuGet, applied by a deployment job, never silently at app startup above local dev.
- **DacFx / Microsoft.Build.Sql is not in scope.** Postgres-only. Schema owned by EF migrations + Postgres-native constraints.

### Messaging

- **NServiceBus over RabbitMQ (CloudAMQP). DECIDED — version floor 10.2.** Source-generated handler/saga registration (assembly scanning disabled). Two endpoint flavors per context, both in `Norse.Hosting.Web.Server`: `{company}.{context}` (worker — durable, outbox on) and `{company}.{context}.web` (server — ephemeral, no outbox). **Wolverine and MassTransit are not in use.** Full design: `docs/Platform/specs/2026-06-03-messaging-foundation-design.md`.
- **`TransportTransactionMode.ReceiveOnly`, globally, non-overridable.** Corollary golden rule: one mutation per handler — multi-step work is a command chain.
- **Message placement:** events → `{Company}.{Context}.Contracts` (the only cross-context message surface); server→worker commands → `{Company}.{Context}.Backend`; worker-private chain commands → `.Worker`, `internal`.
- **Messages are POCO `sealed record` types — no NServiceBus reference in message-bearing assemblies.** Unobtrusive-mode conventions declared once by the hosting runtime; System.Text.Json platform-wide. NSB persistence tables deploy via `Norse.Hosting.Migrations.Service`; `EnableInstallers()` is local-dev only.

### Hosting

- **Azure Container Apps with KEDA scaling** in production. Event-driven autoscale matches the first product's MGA workload shape (spiky underwriting, nightly bordereaux).
- **Azure App Configuration with sentinel keys** for config and feature flags. Sentinel keys let the migration job skip work where schema hasn't changed.
- **.NET Aspire owns container orchestration, local and cloud.** The AppHost (`Norse.Orchestration.AppHost`, in Bifrost) composes Postgres, RabbitMQ, OpenIddict, and the web/worker/migrations containers locally (no VPN/cloud needed: `git clone --recurse-submodules` → `dotnet run --project Norse.Orchestration.AppHost`) and drives cloud deployment. **Pulumi codifies the cloud infrastructure.** Orchestration is connective tissue — no product semantics.
- **Single server-side deployable: `Norse.Hosting.Web.Server`.** One ASP.NET Core process loads every context's plugin. All gRPC, gRPC-Web, JSON controllers, and `BackgroundService` ride in one process by default — plus the Blazor Web App surface (SSR + interactive-server circuits for Shell, serves the WASM bundle; UI Composition spec §7.1). Scale by replicas, not by fragmenting. `Norse.Hosting.Worker` is admissible only when a workload's resource profile justifies splitting; default is don't.
- **Per-context Plugin classes by deployable** — `{Context}Plugin : IWebHostPlugin` in `.Server`, `{Context}WorkerPlugin : IWorkerHostPlugin` in `.Worker` (§5). **No** product-tier hosting-abstractions layer — cross-cutting (audit, `NorsePrincipal`) is platform middleware on the host runtime. Plugins declare DI, HttpClient, authorization, routes, `BackgroundService` registrations, and their explicit NServiceBus handler/saga set — never DbContext or endpoint configuration (Midgard and the hosting runtime own those).

### Tenancy

- **Stamp-per-tenant. DECIDED — 2026-06-03 (§7 #4).** Single-tenant code, multi-tenant by stamping: a tenant is a complete, isolated deployment. No code path anywhere is tenant-aware; stamp identity is an OTel resource attribute (`norse.stamp`), never an auth claim — `NorsePrincipal` carries no `TenantId`. Full design: `docs/Platform/specs/2026-06-03-tenancy-model-design.md`.
- **The schema is tenant-ignorant, permanently.** No `TenantId` on entities, no `ITenantScoped` marker, no global query filters, no `tenant_id` columns, and RLS never spells "tenant." Isolation is the database boundary, provisioned per stamp. `IConnectionResolver` (`Norse.Infrastructure.Persistence`) stays the single choke point where connection strings are born.

### Auth

- **OpenIddict implementing OAuth 2.1.** Two flows: Authorization Code + PKCE (portal, internal tooling), Client Credentials (producer APIs, partner integrations).
- **Federation. DECIDED (§7 #3).** Staff federate via direct OIDC to Google Workspace (`hd`-restricted); producers and customers are local accounts in OpenIddict (customers may additively link Google/Apple social); M2M via client credentials; no Keycloak broker. Per-stamp OpenIddict under stamp-per-tenant (§4 → Tenancy). Full design: `docs/Platform/specs/2026-06-07-auth-design.md` (supersedes the 2026-05-20 federation spec; a partner-federation slot is documented for V2 in the spec).
- **Identity storage: Mongo is the system of record. DECIDED — 2026-06-03.** OpenIddict Mongo stores + custom Identity stores in `Norse.Auth.Server`; the Postgres `auth` schema is an event-fed, read-only reporting projection. Credentials never leave Mongo. Deliberate inversion of the platform default — credential verification is synchronous web-tier work; a queue cannot sit in the login path. The `.Server` hard wall holds with no exemption.

### PII and Encryption

- **AES-256-GCM at the application level. DECIDED — 2026-06-03 (§7 #11).** One mechanism, wire and at rest: `EncryptedString` (`Norse.Primitives` wrapper) carries ciphertext through NServiceBus payloads **and** round-trips to ciphertext columns/fields in Postgres and Mongo. PII is plaintext only in-process. Encrypted columns are not queryable — lookups use a blind-index (HMAC) companion column, designed once in the forthcoming `EncryptedString` spec, never ad hoc per table.
- **Keys: Azure Key Vault + envelope encryption, per-customer DEKs — crypto-shredding.** Right-to-erasure is destroying the customer's DEK, not a row-deletion sweep; each stamp's KEK sits above the DEKs, so tenant offboarding is crypto-shredding one level up. Rotation mechanics, nonce bounds, local-dev keys: `EncryptedString` spec.
- **`YGG101` strict form: no bare `string` on message types, period.** Every string-shaped property on a `*Event` / `*Command` / `*Notification` is a domain type (`EmailAddress`, `PolicyNumber`, …), `EncryptedString` (PII), or `PlainText` (deliberately non-sensitive free text). There is **no** `[NonSensitive]` opt-out attribute — declaring something non-sensitive is a typed act that travels with the value. Bare `int` / `bool` / `Guid` / `DateOnly` remain legal where genuinely primitive.

### CLI and Tooling

- **Spectre.Console.Cli** for every CLI. No bespoke `args[]` parsing.

### File and Data Parsing

- **Sep (`nietras.SeparatedValues`)** for delimited file parsing — zero-allocation, `ReadOnlySpan<char>`-friendly.
- **Sylvan.Data.Excel** for reading `.xlsx`.
- **DocumentFormat.OpenXml** if/when we need to *write* `.xlsx` (not yet a requirement).

### Testing

- **Shouldly** for assertions. **Not FluentAssertions** (commercial license incompatible).
- **NSubstitute** for test doubles. **Not Moq.**
- Mixing assertion libraries fragments error reporting and muscle memory: one library, repo-wide.

### Why Not …

- **MediatR** — source-generated [martinothamar/Mediator](https://github.com/martinothamar/Mediator) (3.0 floor) as the dispatch core instead: compile-time dispatch, no reflection, Native AOT. `Norse.Abstractions.Mediator` owns the law on top (YGG401–408). See `docs/Platform/specs/2026-05-26-mediator-design.md`.
- **Dapper** — EF Core with the right conventions covers our needs. Drop to raw SQL via `FromSql` or ADO for hot paths, not as default.
- **Minimal APIs only** — minimal APIs for thin HTTP surfaces; command/query handlers live behind the source-generated mediator. HTTP is one transport.

---

## 5. Naming Conventions

### Language

- **All prose, identifiers, comments, docs, and commit messages use US English (en-US) spelling.** No `organisation` / `colour` / `behaviour` — `organization` / `color` / `behavior`. Mixed spelling fragments search, code review, and analyzer-rule naming.

### Top-Level Namespaces

**Codenames are lore; code uses the function.** The **platform substrate** lives under one brand root, **`Norse.*`** (*"the Norse Architecture"*); its realms are peers *within* that root (like `Microsoft.Extensions.*`), never nested under each other. **Product realms** are their own peer roots, `{Company}.*`. Realm purposes are §1's table; the full ethos⇒function dictionary (codename → namespace) is `docs/codenames.md`; the assembly catalog is `docs/decomposition.md`. Rules worth restating:

- **Platform substrate — `Norse.{Function}`** (product-agnostic by construction; never names or knows a specific entity): `Norse.Primitives` forges (primitives, analyzers `YGG001`..`YGG3xx`); `Norse.Abstractions` declares (contracts, attribute model, mediator law, temporal contracts); `Norse.Infrastructure` embodies (persistence, API, mediator runtime, UI Composition); `Norse.Hosting` hosts (runtimes `Norse.Hosting.{Web.Server|Web.Client|App|Worker|Migrations.Service}`, deployables; `ServiceDefaults` placement ruled 2026-06-11 — Infrastructure if possible, Hosting only if it carries shared runtime context, never Orchestration); `Norse.Orchestration` composes (the Aspire AppHost, in Bifrost); `Norse.Warehouse` remembers; `Norse.Auth` gates; `Norse.Observability` signals; `Norse.AI` advises.
- **Product realms — `{Company}.{Context}.*`** (sovereign companies; founding verticals: insurance, energy retail, logistics; more to follow). Each owns per-context assemblies (`.Contracts`, `.Components`, `.Backend`, `.Server`, `.Worker`, `.Migrations`, optionally `.JsonApi`) for every bounded context it defines — e.g. the insurance product's Billing, Claims, Policy, Customer, Underwriting, Distribution, Product, Reporting — plus a per-product stitched app shell (`{Company}.Shell`). A product shares **nothing** below the platform line with another: one vertical's `{Company}.Billing` and another's are different animals, no shared code. `{Company}.{Context}.*` is the **house suggestion, not a mandate** — internals are sovereign once the product conforms to `Norse.Abstractions` and rides the rails.
  - The product boundary is a **legal** boundary (separate capitalization) — `YGG004` (cross-context reference ban) generalizes to cross-*product* (analyzers catalog, deferred).

**Future services:** load-bearing, platform-wide, product-agnostic → its own `Norse.{Function}` namespace; product-domain-specific → `{Company}.{Function}`. Auth / observability / AI are platform (`Norse.Auth` / `Norse.Observability` / `Norse.AI`); **fraud and triage (lore: Tyr, Valkyrie) are unplaced** — lifeblood in insurance, minor elsewhere, so platform-vs-product is unsettled (in the ether — `docs/codenames.md`). The dictionary records placement case-by-case.

### Repositories

**Repository = lore, namespace = function** (ruled 2026-06-11; topology spec). One repository per platform realm, named for the lore; every project and namespace inside is named for the function. Open the org and tour the cosmos; open the `.slnx` and every project says what it does. Live at `github.com/NorseArchitecture`: **Bifrost** (`Norse.Orchestration` — the Aspire AppHost meta-repository; carries the realm repos and Glitnir as submodules, relative URLs, tracking `master`), **Asgard**, **Midgard**, **Svartalfheim**, **Urdarbrunnr**, **Yggdrasil**, and **Glitnir** (this repo — the design court: `docs/` specs, plans, registries; `poc/`; `benchmarks/`). Full repository map: `docs/decomposition.md`.

- **Sessions start at the Bifrost workspace root**; all platform specs and plans land in `./Glitnir`. Spec commits land in Glitnir; the submodule pin advances in Bifrost (the verdict ledger).
- **Glitnir is the platform court only.** It records the Norse Architecture and the generic product shape. Each venture, when born, stands up its own private design court — venture/brand-specific record never lands here (fractal courts; topology spec §2.5).
- **The public record is brand-clean.** Operating-entity brands, carrier names, and brand-revealing lore never enter this corpus; vertical descriptors and `{Company}` placeholders speak instead (topology spec §2.6).
- **The record is CI-clean: relative paths only — hard law (2026-06-11).** Every path in every document is relative — to its repo root, or to the Bifrost workspace root for cross-realm references (`../Glitnir/docs/...`). Absolute machine-local paths (`C:\...`, `/home/...`) never enter the corpus: a path that names a workstation is a record that cannot be replayed on a clone, a CI runner, or the next machine. Where a machine location is unavoidable, an environment variable names it (`$env:TEMP`, `$env:ProgramFiles`). Historical plans were normalized to this law 2026-06-11; their "all paths relative to the meta-repo root" headers carry the context.

Clone: `git clone --recurse-submodules <bifrost-repo-url>`

### Solutions and Projects

- **Solution per repo, named for the repo:** platform realms use the lore name (`Svartalfheim.slnx`, `Bifrost.slnx` — ruled 2026-06-11, superseding `Norse.{Function}.slnx`); product contexts use `{Company}.{Context}.slnx`. At repo root either way.
- **`.slnx` (XML) format is mandatory.** Legacy `.sln` is not supported. Create with `dotnet new sln --format slnx`.
- **Project layout:** `src/{ProjectName}/{ProjectName}.csproj`, and **project names are brand-free** (ruled 2026-06-11): each realm's root `Directory.Build.props` injects `<AssemblyName>` and `<RootNamespace>` as `Norse.$(MSBuildProjectName)` — `src/Primitives/Primitives.csproj` produces `Norse.Primitives.dll`. The brand prefix exists in exactly one file per realm, so a fork rebrands by changing `Norse` once — no project renames, no slnx surgery. The namespace-mirrors-assembly rule below is unchanged; it is the *project file* that drops the prefix, never the code. The props edit rebrands everything the build *derives* — assembly, package, `InternalsVisibleTo` (`$(AssemblyName).Tests` follows automatically) — but the `namespace Norse.*` declarations in code deliberately do **not** follow. Culling them is the fork's own conscious act: the design shows them the direction they are choosing, and neither step ever forces a filesystem change.

**Platform-tier shared abstractions live in Abstractions (`Norse.Abstractions`)**, one per concern: `Norse.Abstractions.Contracts` / `Norse.Abstractions.Components` (shape contracts for the matching `*.Contracts` / `*.Components` assemblies), `Norse.Abstractions.Infrastructure` (repository contract family, entity markers, audit/timestamp interfaces), `Norse.Abstractions.Hosting` (plugin contracts; context plugins implement directly).

**No** per-context `{Company}.{Context}.Abstractions` project. Public contracts → `.Contracts`. Server-side types shared by both halves → `.Backend`. Internal abstractions used by one half only → that half, as `internal` types.

**Per-bounded-context projects** (split by deployable; no per-context Host deployables): `.Contracts` — the **single** project other contexts may reference · `.Components` — client-bundle Blazor UI; never references `Server`/`Worker`/`Backend` · `.Backend` — server-side shared (server→worker commands, Mongo documents); never client-reachable (analyzer-enforced); exists iff `.Server` + `.Worker` both exist · `.Server` — web tier; **no SQL entities, no EF, no DbContext** · `.Worker` — system-of-record tier; **no DbContext declaration or injection**. Full responsibilities, deployables catalog, and add-on rules (`.Migrations`, `.JsonApi`): `docs/project-structure.md`.

**`.Server` and `.Worker` are mutually invisible — hard walls.** Neither references the other; both reference `.Backend` and `.Contracts`. The worker never references ASP.NET Core; the server never references EF Core or entity types. They meet only at the queue.

**Deployables** (all under `Norse.Hosting.*`): one server-side process, `Norse.Hosting.Web.Server` (§4 → Hosting); `Norse.Hosting.Worker` optional and deferred; `Norse.Hosting.Migrations.Service` (deployment-job orchestrator); clients `Norse.Hosting.{Web.Client|App}` (Components-only — never `Server` or `Worker`; `DevServer` deleted 2026-06-05, superseded by `InteractiveServer` render mode on `Norse.Hosting.Web.Server`). Details: `docs/project-structure.md`.

**Test projects** mirror with `.Tests` suffix. Tests depend on their target; nothing depends on tests.

### Namespaces

- Namespace mirrors assembly name exactly. No re-rooting, no shortcuts.
- Folder structure mirrors namespace structure.

### Classes and Records

- **Records by default for immutable data.** `sealed record` is the default for events, commands, value objects, and wire shapes.
- **`sealed class`** for entities and services where reference semantics matter.
- **`internal`** is the default accessibility. Promote to `public` only for types that cross assembly boundaries with intent.
- Event types use the meaningful `Event` suffix (`PolicyBoundEvent`, `ClaimReportedEvent`) — analyzers and humans key on it.

### Enums

- **Explicit integer values on every member, always** — implicit ordinals turn a reorder into silent data corruption across persisted rows, in-flight messages, and audit logs, with no compile-time signal.
- **`0` is reserved for "unspecified"/sentinel**; real states start at `1`.
- **Never remove a persisted value** — `[Obsolete]` and keep the integer.
- **String-mapping (`HasConversion<string>()`) at the database boundary** where the enum appears in queries, dashboards, or bordereaux; explicit integers still rule the C# side.

Full rationale: `docs/conventions.md`.

### Database Objects

Schema per bounded context (`policy`, `claims`, …); snake_case throughout; plural tables (`policy.policies`); FKs `{referenced_table_singular}_id`; constraint prefixes `pk_` / `fk_` / `uq_` / `ck_` / `ix_`; migration names read as a changelog (`AddPolicyCancellationReason`, never `Update3`). Full detail: `docs/conventions.md`.

### Files

- One public type per file. Filename matches the type name exactly, including case.
- Test files: `{TypeUnderTest}Tests.cs`. Test methods: `Should_{behavior}_when_{condition}` or `{Method}_{condition}_{expectedResult}` — pick one shape per project and hold the line.

---

## 6. Codename Registry

Norse mythology only. **Do not mix pantheons.** **Codenames are lore** — the genesis/inspiration story; code and specs use the **function** (`Norse.{Function}` substrate / `{Company}.{Context}.*` products — §5). The full **ethos⇒function dictionary** (codename → namespace + rationale) is `docs/codenames.md`; the brand synthesis is `docs/norse-architecture.md`. **Platform realms** (named for function; lore in parens): Abstractions (Asgard) · Infrastructure (Midgard) · Primitives (Svartalfheim) · EntityFramework (Urdarbrunnr) · Hosting (Yggdrasil) · Orchestration (Bifrost) · Warehouse (Muninn) · Auth (Heimdall) · Observability (Gjallarhorn) · AI (Mimir) · Notifications (Ratatoskr, when it lands) · Shell (Hlidskjalf, per-product). **Product realms** are named for the governing figure of their vertical (rule #3); the assignments are each venture's own record, not this corpus's. **In the ether** (unplaced): fraud (Tyr) and triage (Valkyrie) — insurance-lifeblood, platform-vs-product unsettled. The **Reserved-with-an-intended-use tier was killed 2026-06-07** (predictions rot — see `docs/the-crooked-path.md`); the **bench** is a no-meaning palette (Norns, Huginn, Saga, Bragi, Var, Idunn, Vidar) — a name leaves it only when a real component takes it. Glitnir is this repository (the design court).

### Rules for New Codename Assignments

1. **Must be Norse.** No Greek, Roman, Egyptian, or generic mythology mixing.
2. **Code uses the function; the codename is lore.** `Norse.{Function}` (platform) or `{Company}.{Context}.*` (product) in code and specs; the codename lives in `docs/codenames.md`, the README, and the repository names — never as the operational identifier.
3. **Platform services are named for function; product realms for the governing figure of the vertical** — sovereignty over a domain, not a function. Assignments are recorded in each venture's own court (topology spec §2.5/§2.6).
4. **Name only when the component is real.** No speculative reservation — a name leaves the bench in the same change that introduces the thing it narrates (the killed-reservations lesson).
5. **Do not codename a bounded context.** Contexts inside a product take descriptive names.
6. **Update `docs/codenames.md`** (the dictionary) in the same PR that introduces or renames a component.
7. **Product internals are sovereign.** `{Company}.{Context}.*` is the house suggestion, not a mandate; conform to `Norse.Abstractions` and ride the rails, and the naming is the company's own business.

---

## 7. What Requires a Human Decision Before Proceeding

Genuine unknowns, not casual preferences. Each must be answered before the work it gates begins.

1. **Repository strategy. RESOLVED — re-amended 2026-06-11.** The cosmos is live: lore-named realm repos under `github.com/NorseArchitecture`, Bifrost as meta-repository, Glitnir as the platform court riding at `./Glitnir`. See §5 → Repositories and the topology spec (2026-06-11).
2. **Messaging library. RESOLVED.** NServiceBus, version floor 10.2. See §4 → Messaging. AOT remains a v11 trajectory bet; licensing sizing/procurement is a tracked business action item.
3. **Auth federation. RESOLVED.** See §4 → Auth (a partner-federation slot is documented for V2 in the spec).
4. **Tenancy model. RESOLVED — 2026-06-03.** Stamp-per-tenant. See §4 → Tenancy; re-entry triggers toward shared-compute pooling are in the spec.
5. **Bordereaux contract with the fronting carrier.** Own the format, follow the carrier's exactly, or negotiated shared spec? Affects Reporting directly.
6. **Line-of-business scope at launch.** Workers' Comp? Commercial property? Personal auto? Drives rate-engine complexity, filing scope, and in-scope states.
7. **State strategy.** First filing states? Each state's regulatory regime (rate filings, forms, surplus lines) is non-trivial.
8. **Portal / front-end scope.** The insurance product's customer/producer portal: this platform (via `Norse.Hosting.Web.Client`/`Norse.Hosting.App`), separate repo, or out of scope (third-party vendor portal)?
9. **Product brand timing.** Working assumption: each venture's codename is its intended public brand, launched on the venture's own terms; until then the platform corpus stays brand-clean (topology spec §2.6). Per venture, at launch: legal entity name vs. brand only?
10. **Geographic and currency scope.** US-only? USD-only? Multi-currency / multi-country has architectural implications that must be declared, not discovered.
11. **PII / encryption posture. RESOLVED — 2026-06-03.** See §4 → PII and Encryption. The `EncryptedString` spec is the surviving work item.

**Do not silently proceed on any of these without a documented decision.** Defaults in insurance MGA contexts cause expensive rework.

---

## 8. What This Codebase Refuses to Do (Anti-Patterns)

Build errors, PR rejections, or refusal-to-write-the-code situations. **Not style preferences.**

### Type Safety and Domain Modeling

- **No stringly-typed money.** `Money(Amount, Currency)` or it isn't money.
- **No stringly-typed identifiers.** Domain identifiers are strong types (`PolicyNumber`, `ClaimNumber`, `CustomerId`) that cannot be constructed from arbitrary strings.
- **No stringly-typed dates.** Use `DateOnly`, `DateTimeOffset`, or domain types (`EffectiveDate`, `LossDate`). Never naked `DateTime` at boundaries.
- **No bare `string` on message types — PII or otherwise.** `YGG101` enforces. See §4 → PII and Encryption.
- **No implicit enum values.** See §5 → Enums.
- **No null collections.** Absence of items is `[]`, never null; `?` on an enumerable-shaped type is a design error. Null is reserved for genuinely optional references (NRT-declared) and `Nullable<T>` structs. Compiler-enforced for non-nullable returns (NRT + ratchet); declaration ban is YGG analyzer bench until the rule lands.

### Ambiguity

- **No silent date-culture inference.** Ingress paths declare their date format; if a file says `01/02/2026`, the loader knows whether that's Jan 2 or Feb 1 because the format was declared up front.
- **No silent currency assumption.** No defaulting to USD. If the source doesn't declare currency, ingest fails.
- **No silent rounding.** Monetary rounding rule is declared at the operation (banker's, half-up, half-away-from-zero), not inherited from `decimal` defaults.

### Architecture

- **No `ProjectReference` items inside `<Target>` blocks.** Invisible to dependency ordering; works until the first cold rebuild fails. `YGG301` enforces.
- **No domain → infrastructure references.** Build-time enforcement.
- **No synchronous cross-context RPC for writes.** Contexts integrate via published events. Cross-context reads go through a published read model or anti-corruption layer.
- **No `partial` on generated classes** unless the generator demands it and the demand is documented.

### Persistence

- **No EF-Fluent-only invariants.** Anything that matters at the data layer exists as a database constraint.
- **No silent migrations at app startup.** Deployment job only above local dev.
- **No "just for now" hardcoded credentials or connection strings.** Azure App Configuration or env vars. Local dev uses user secrets.
- **No `DbContext` injection in services.** Repository contract family from `Norse.Abstractions.Infrastructure` only; DbContexts are `Norse.Infrastructure.Persistence`-owned. Analyzer-enforced. See §4 → Persistence.
- **No cross-context entity references.** `YGG004`. Cross-context flows: (1) published events, (2) gRPC via `I{Context}Api`, (3) Warehouse.
- **No tenancy in the schema.** A tenant is a deployment stamp; isolation is the database boundary. See §4 → Tenancy.

### Error Handling

- **No catch-all `catch (Exception)` that swallows.** Catch only what you can handle meaningfully; otherwise let it propagate.
- **No `Result<T>` that nobody checks.** Either consumers are compile-time-forced to handle the failure case, or we throw. Half-measures combine the cost of both.
- **No silent fallbacks in business logic.** Missing rate factor is a hard fail, not `1.0`. Insurance silently coerced toward "no effect" is insurance that mispriced something.

### Testing and Tooling

- **No code without a failing test first, no plan without subagent orchestration — both, every time (§2.8).**
- **Shouldly only** (not FluentAssertions). **NSubstitute only** (not Moq).
- **No mocking what we don't own** beyond a thin port. Wrap and mock the wrapper.
- **No mocked-DB tests for behavior that depends on database semantics.** Integration tests hit real Postgres (testcontainers or shared dev).

### Reflection and Magic

- **No reflection in hot paths.** Source generators preferred. One-time startup wiring is acceptable; per-request is not.
- **No DI registration by convention scanning** for handlers. The source-generated mediator registers at compile time; NServiceBus assembly scanning is disabled in favor of source-generated explicit registration.
- **No "framework magic" that requires reading framework source to understand.** If a future maintainer can't figure out why a thing happens from the code in front of them, the thing should not happen.

### Process

- **No automatic git commits.** Stage and show the diff; the human runs `git commit`. Applies even when a skill's flow includes a commit step (brainstorming, writing-plans, finishing-a-development-branch). When in doubt, stop and wait.
- **No skipping git hooks** (`--no-verify`, `--no-gpg-sign`). If a hook fails, fix the underlying issue.
- **No force-pushing to `master`** under any circumstance.
- **No committing secrets**, even temporarily.
- **No machine-local absolute paths in documents** (hard law, 2026-06-11). Specs, plans, and findings use repo- or workspace-relative paths, with environment variables for unavoidable machine locations — the record must replay anywhere.
- **No stale README/CLAUDE.md pairs — boy-scout law (2026-06-11).** Every repo that carries a CLAUDE.md carries a README.md; they tell one story at two altitudes — README the public narrative, CLAUDE.md the session law. The same change that alters what either describes updates both. Touching one while leaving the other stale is leaving the campsite dirty.

---

## Appendix A — Quick Reference for New Sessions

1. **Read this file fully** before proposing anything. §2, §7, §8 are where you get yourself in trouble fastest.
2. **Confirm the open questions in §7** that apply to your work. Unresolved item touched? Raise it before writing code.
3. **Use the bounded context map (§3)** to decide where work belongs. Change cuts across two contexts? That's a design conversation.
4. **Naming is a deliberate act.** `Policy` ≠ `Policies` ≠ `PolicyAdministration`. Pick the right name once.
5. **When in doubt, fail loudly.** Silent fallbacks in insurance cause real financial harm. The whole point of the first product is to *not* be the carrier that does that.
