# CLAUDE.md — Glitnir (Enterprise .NET Platform — Design Court)

## 0. Wrong Root — Halt

If you are reading this because **Glitnir itself is the Claude Code session root** — someone ran `claude` from inside this directory instead of `../Bifrost` — stop here. Do not read further, do not propose changes, do not run anything.

Tell the user: every Norse Architecture session starts from **Bifrost**. Org-wide settings (the `superpowers` plugin, permission rules) only apply when Bifrost is the actual session root — Claude Code never merges a submodule's own `.claude/settings.json` into a parent-launched session. Exit, `cd ../Bifrost`, and run `claude` there instead.

This repo's own `.claude/settings.json` carries a `SessionStart` hook that should already have blocked this session before this file was ever read. If you're reading this anyway, hooks were bypassed, disabled, or failed — halt regardless; this rule does not depend on the hook to hold.

---

Authoritative cold-start context for any Claude Code session in this repo. Read top to bottom before proposing anything. Where a rule here conflicts with a default behavior, this file wins.

---

## 1. Project Overview and Mission

**Glitnir** is the design court — specs, proofs of concept, and plans are heard and judged here; production code is the verdict, rendered only after the narrative converges. It lives at `./Glitnir` inside the **Bifrost** meta-repository.

**Norse** is a multi-product platform — a venture studio. Platform realms (`Norse.{Function}`) are the shared substrate; product realms (`{Company}.{Context}.*`) are separately-capitalized operating entities. Founding verticals: insurance (the first product, a greenfield MGA), deregulated energy retail, logistics. **Codenames are lore; code uses the function.** Full topology: `docs/Platform/specs/2026-06-07-multiproduct-platform-design.md`. Realm table and assembly catalog: `docs/decomposition.md`. Codename ⇒ namespace dictionary: `docs/codenames.md`.

### Mission (the first product — insurance)

Four constraints — not aspirations — that every feature, refactor, and trade-off answers to:

1. **Reduce operational cost.** Automate work that doesn't require judgment. Reserve human time for ambiguity, escalation, and care.
2. **Serve customers faster and more fairly.** Decisioning latency is a feature requirement. Same facts → same decision.
3. **Be less litigious than the industry norm.** Soft denials, attritional friction, and bad-faith claims handling are out of scope.
4. **Be unambiguous and aggressive with provably fraudulent actors.** "Fair to claimants" ≠ "tolerant of fraud."

The product realm operates as an MGA on a fronting carrier's paper; shared conventions are deliberate alignment, not code reuse.

---

## 2. Architecture Principles (Decision Rules)

Non-negotiable. **Rules**, not aspirations.

### 2.1 Pit-of-Success Engineering

> The easy path must be the correct path.

Before merging a new abstraction, ask: "Can a developer in a hurry use this incorrectly?" If yes, redesign until the wrong usage won't compile, won't bind, or won't run. Documentation alone is not enforcement.

### 2.2 Database Self-Defense

> The database enforces its own integrity, regardless of how data arrives.

Every invariant that matters exists as a database constraint (CHECK, NOT NULL, UNIQUE, FK, exclusion, partial index). Invariants that can't be expressed in the database must live in **two** places: application layer **and** a periodic verification job that fails loudly on violation. No exceptions for "internal" or "temporary" tables.

### 2.3 Least Accessibility Until the Door Must Be Opened

> Start closed. Open only when a concrete requirement demands it.

Default: `internal sealed`, non-`partial`, init-only, no extension points. Each escalation requires a concrete justified caller, not a hypothetical future need.

- **Defaults are expressed by omission** (`omit_if_default`): `class Foo`, not `internal class Foo`. A visible accessibility keyword is a deliberate escalation — never noise.
- **Tests reach internals via one sanctioned door:** `src/Directory.Build.props` grants `<InternalsVisibleTo Include="$(AssemblyName).Tests" />` once per repo. `public`-for-testability is never justified.
- **CA1852 is an error platform-wide.** Unsealing requires an actual derived type in the codebase.

### 2.4 Strict Assembly Boundary Enforcement

> Domain layers do not reference infrastructure. Infrastructure does not leak into domain.

`.Components` never references server-side types (it compiles into WASM/MAUI). `.Server` and `.Worker` are mutually invisible — they meet only at the queue. Cross-context references are forbidden; contexts integrate via published events and gRPC contracts. Violations are build errors (`YGG003`, `YGG004`).

### 2.5 Convention Over Configuration; Simplicity Over Ceremony

> Implicit magic is a liability. Ceremony for its own sake is also a liability.

Conventions worth having are enforced automatically (analyzer, source generator, build target) — "everybody knows" is tribal knowledge waiting to be lost. Configuration must justify itself; no extension points that exist solely for hypothetical future needs.

### 2.6 Hard-Fail on Ambiguity

> Reject ambiguous input. Do not guess.

Dates, money, currency, identifiers, and rates have exactly one accepted representation per ingress path, declared up front. Ambiguity is an immediate parse failure. Internal handoffs use strongly typed values (`Money`, `PolicyNumber`, `EffectiveDate`).

### 2.7 Push Errors Upstream — Fail Fast, Fail Loud, Fail Hard

> The closer to the source of a mistake we surface failure, the cheaper the fix.

Preference order, strictly: **compile time** → **build time** → **application startup** → **request/message boundary** → **production runtime** (last resort; reaching here means an earlier layer failed). Silent fallback is never acceptable.

### 2.8 Subagent-Orchestrated, Test-Driven Implementation

> Orchestration sequences tasks. TDD governs how each one is coded. Neither substitutes for the other.

`superpowers:subagent-driven-development` is the **default** orchestration skill for every implementation plan — not a recommendation among equals. `superpowers:executing-plans` is the narrow exception (work that specifically needs a separate session with human review checkpoints); it is never an interchangeable alternative. Every plan names both skills on the REQUIRED SUB-SKILL line. Both, every time, platform-wide.

---

## 3. Bounded Context Map

Full concern table and ownership boundaries: `docs/decomposition.md`. Consult it before placing work.

**Core domain contexts (insurance):** Product · Distribution · Underwriting · Policy · Billing · Customer · Claims · Reporting.

**Cross-cutting platform services** (not bounded contexts): `Norse.Identity` · `Norse.Access` · `Norse.Observability` · `Norse.AI`. Fraud (Tyr) and triage (Valkyrie) are unplaced — platform-vs-product unsettled.

**Integration rules:** contexts publish on their own logical endpoint; subscribers bind queues; publishers don't know who's listening. Events are versioned from day one; breaking changes ship a new type. No synchronous cross-context RPC for writes. Fraud and AI are advisors, not authorities — the originating context retains the decision. Reporting is the only context permitted to maintain denormalized cross-context state. Full design: `docs/Platform/specs/2026-06-03-messaging-foundation-design.md`.

One rule worth restating outside the spec: **claims adjudicate against a snapshot of policy facts at the date of loss — live policy edits never retroactively change a claim.**

---

## 4. Technology Decisions

Key decisions only — full rationale in linked specs.

### Runtime and Language

- **.NET 10, C# (latest).** AOT-clean where feasible.
- **`var` for return assignments only.** Construction uses target-typed `new()` with explicit type on the left.
- **Tabs, 4-space width.** Ecosystem exceptions (YAML/Markdown/JSON 2-space, Razor 4-space) declared in `.editorconfig` with reasons. Full detail: `docs/Platform/specs/2026-06-05-editorconfig-curation-design.md`.

### Persistence

PostgreSQL (Neon) — primary OLTP. TimescaleDB — time series. pgvector — embeddings. MongoDB — operational read store (`.Server` tier); also system of record for identity and UI layout (deliberate inversions). Full design: `docs/Midgard/specs/2026-05-21-midgard-persistence-design.md`.

Key decisions:
- Abstract base context (audit stamping, conventions) → `Norse.EntityFramework` (Urdarbrunnr).
- Concrete per-service DbContexts are **source-generated, `file`-scoped** — unreferenceable by construction.
- Four repository contracts in `Norse.Abstractions.Infrastructure`: `IDocumentRepository<T>`, `ICommandRepository<T>`, `ICachedRepository<T>`, `ITemporalRepository<T>`. **No `IUnitOfWork`** — the messaging library's per-handler session owns the transaction.
- Services never inject a DbContext or call `SaveChangesAsync`.
- Migrations in `{Company}.{Context}.Migrations` — deployment job only; never silent at app startup above local dev.

Full EF design: `docs/Urdarbrunnr/specs/2026-06-11-entityframework-context-provenance-decision-inputs.md`.

### Messaging

**NServiceBus over RabbitMQ (CloudAMQP), version floor 10.2.** `TransportTransactionMode.ReceiveOnly`, globally, non-overridable — one mutation per handler. Messages are `POCO sealed record` types; no NServiceBus reference in message-bearing assemblies. Assembly scanning disabled; source-generated registration. Full design: `docs/Platform/specs/2026-06-03-messaging-foundation-design.md`.

Message placement: events → `.Contracts`; server→worker commands → `.Backend`; worker-internal chain commands → `.Worker, internal`.

### Hosting

Single server-side deployable: `Norse.Hosting.Web.Server` (one ASP.NET Core process; per-context plugin classes). Azure Container Apps with KEDA. Azure App Configuration with sentinel keys. Pulumi for cloud infrastructure. Full design: `docs/Yggdrasil/specs/2026-05-20-yggdrasil-hosting-design.md`.

### Tenancy

**Stamp-per-tenant — DECIDED (2026-06-03).** Single-tenant code, multi-tenant by stamping. No code path is tenant-aware; stamp identity is an OTel resource attribute, never an auth claim. No `TenantId` anywhere in the schema. Full design: `docs/Platform/specs/2026-06-03-tenancy-model-design.md`.

### Auth

**OpenIddict implementing OAuth 2.1 — DECIDED.** Staff: OIDC to Google Workspace (`hd`-restricted). Producers/customers: local OpenIddict accounts. Identity storage: Mongo (system of record). Full design: `docs/Platform/specs/2026-06-07-auth-design.md`.

### PII and Encryption

**AES-256-GCM — DECIDED (2026-06-03).** `EncryptedString` (`Norse.Primitives`) carries ciphertext on wire and at rest. Blind-index (HMAC) companion column for lookups. Per-customer DEKs under Azure Key Vault envelope encryption — crypto-shredding for right-to-erasure.

**`YGG101`:** No bare `string` on message types. Every string-shaped property on `*Event` / `*Command` / `*Notification` is a domain type, `EncryptedString` (PII), or `PlainText` (deliberately non-sensitive). No `[NonSensitive]` opt-out attribute.

### Testing

**Shouldly** (not FluentAssertions — commercial license). **NSubstitute** (not Moq). xUnit v3 on Microsoft.Testing.Platform. No mocked-DB tests for behavior that depends on database semantics — integration tests hit real Postgres.

### Key Rejections

| Rejected | Use instead | Spec |
|---|---|---|
| MediatR | `martinothamar/Mediator` 3.0+ | `docs/Platform/specs/2026-05-26-mediator-design.md` |
| FluentAssertions | Shouldly | (commercial license) |
| Moq | NSubstitute | — |
| MassTransit / Wolverine | NServiceBus | `docs/Platform/specs/2026-06-03-messaging-foundation-design.md` |
| DacFx / `.sqlproj` | EF Core migrations + Postgres constraints | — |
| Dapper | EF Core; `FromSql` for hot paths | — |
| Keycloak | OpenIddict direct | `docs/Platform/specs/2026-06-07-auth-design.md` |

---

## 5. Naming Conventions

Full detail in `docs/conventions.md` and `docs/project-structure.md`.

### Language

US English (en-US) everywhere — code, comments, docs, commits. No `organisation` / `colour` / `behaviour`.

### Namespaces

- Platform substrate: `Norse.{Function}`. Product realms: `{Company}.{Context}.*`. Full mapping: `docs/codenames.md`. Assembly catalog: `docs/decomposition.md`.
- Namespace mirrors assembly name exactly. Folder structure mirrors namespace.
- No per-context `{Company}.{Context}.Abstractions` — public contracts → `.Contracts`; shared server-side → `.Backend`; internal → that half, `internal`.

### Repositories and Solutions

- Repository names = lore. Project/namespace names = function. One repo per platform realm.
- Solution per repo, named for the repo (lore name for platform realms, `{Company}.{Context}` for products). **`.slnx` format mandatory** — `dotnet new sln --format slnx`.
- Project layout: `src/{ProjectName}/{ProjectName}.csproj`. **Brand-free project names** — brand injected via root `Directory.Build.props` as `Norse.$(MSBuildProjectName)`. One props edit rebrands; no project renames, no slnx surgery.

### Per-Context Projects

`.Contracts` — only cross-context-referenceable project · `.Components` — client bundle; no server refs · `.Backend` — server-side shared; exists iff both `.Server` and `.Worker` exist · `.Server` — no EF, no entities, no DbContext · `.Worker` — no DbContext injection. `.Server` and `.Worker` are mutually invisible. Full responsibilities: `docs/project-structure.md`.

### Types

- `sealed record` default for events, commands, value objects, and wire shapes.
- `sealed class` for entities and services where reference semantics matter.
- `internal` is the default accessibility; `public` only for cross-assembly types.
- Event types carry the `Event` suffix (`PolicyBoundEvent`, `ClaimReportedEvent`).

### Enums

- Explicit integer values on every member — implicit ordinals are silent data corruption across persisted rows and in-flight messages.
- `0` = unspecified/sentinel; real states start at `1`.
- Never remove a persisted value — `[Obsolete]` and keep the integer.
- String-mapping (`HasConversion<string>()`) at the database boundary where the enum appears in queries or bordereaux.

### Database Objects

Schema per context (`policy`, `claims`, …); snake_case; plural tables (`policy.policies`); FK `{referenced_table_singular}_id`; constraint prefixes `pk_` / `fk_` / `uq_` / `ck_` / `ix_`; migration names read as a changelog (`AddPolicyCancellationReason`, never `Update3`).

### Files

One public type per file. Filename matches type name exactly, including case. Test files: `{TypeUnderTest}Tests.cs`.

---

## 6. Codename Rules

Full mapping: `docs/codenames.md`. Norse mythology only — no other pantheons.

1. **Must be Norse.**
2. **Code uses the function; codename is lore.** Never as an operational identifier — lives in `docs/codenames.md`, the README, and repository names.
3. **Platform services named for function; product realms for the governing figure of the vertical.** Assignments are each venture's own record.
4. **Name only when the component is real.** No speculative reservation — a name leaves the bench in the same change that introduces the thing it narrates.
5. **Do not codename a bounded context.**
6. **Update `docs/codenames.md`** in the same PR that introduces or renames a component.
7. **Product internals are sovereign.** Conform to `Norse.Abstractions` and ride the rails; naming beyond that is the company's own business.

---

## 7. Open Decisions

Resolved decisions (repository strategy, messaging, auth, tenancy, PII/encryption) are in §4. The following are **genuinely open — raise before writing code that touches them:**

1. **Bordereaux contract with the fronting carrier.** Own the format, follow the carrier's exactly, or negotiated shared spec? Affects Reporting directly.
2. **Line-of-business scope at launch.** Workers' Comp? Commercial property? Personal auto? Drives rate-engine complexity and in-scope states.
3. **State strategy.** First filing states? Each state's regulatory regime (rate filings, forms, surplus lines) is non-trivial.
4. **Portal / front-end scope.** Customer/producer portal: this platform (`Norse.Hosting.Web.Client` / `Norse.Hosting.App`), separate repo, or third-party vendor?
5. **Product brand timing.** Legal entity name vs. brand only at venture launch?
6. **Geographic and currency scope.** US-only? USD-only? Multi-currency / multi-country has architectural implications that must be declared, not discovered.

**Do not silently proceed on any of these without a documented decision.**

---

## 8. What This Codebase Refuses to Do (Anti-Patterns)

Build errors, PR rejections, or refusal-to-write-the-code. **Not style preferences.**

### Type Safety and Domain Modeling

- **No stringly-typed money.** `Money(Amount, Currency)` or it isn't money.
- **No stringly-typed identifiers.** Domain identifiers are strong types (`PolicyNumber`, `ClaimNumber`, `CustomerId`).
- **No stringly-typed dates.** `DateOnly`, `DateTimeOffset`, or domain types. Never naked `DateTime` at boundaries.
- **No bare `string` on message types** — `YGG101` enforces.
- **No implicit enum values.**
- **No null collections.** Absence is `[]`, never null. `?` on an enumerable-shaped type is a design error.

### Ambiguity

- **No silent date-culture inference.** Declare the format up front; if a file says `01/02/2026`, the loader knows which interpretation because the format was declared.
- **No silent currency assumption.** No defaulting to USD. If the source doesn't declare currency, ingest fails.
- **No silent rounding.** Monetary rounding rule is declared at the operation (banker's, half-up, half-away-from-zero).

### Architecture

- **No `ProjectReference` inside `<Target>` blocks** — invisible to dependency ordering (`YGG301`).
- **No domain → infrastructure references** — build-time enforcement.
- **No synchronous cross-context RPC for writes** — contexts integrate via published events.
- **No `partial` on generated classes** unless the generator demands it and the demand is documented.

### Persistence

- **No EF-Fluent-only invariants** — everything that matters at the data layer exists as a database constraint.
- **No silent migrations at app startup** — deployment job only above local dev.
- **No hardcoded credentials or connection strings** — Azure App Configuration or env vars; local dev uses user secrets.
- **No `DbContext` injection in services** — repository contract family from `Norse.Abstractions.Infrastructure` only (`YGG004`).
- **No cross-context entity references** (`YGG004`) — cross-context flows: (1) published events, (2) gRPC via `I{Context}Api`, (3) Warehouse.
- **No tenancy in the schema** — isolation is the database boundary.

### Error Handling

- **No catch-all `catch (Exception)` that swallows** — catch only what can be handled meaningfully.
- **No `Result<T>` nobody checks** — compile-time-force the failure case or throw; half-measures combine the cost of both.
- **No silent fallbacks in business logic** — a missing rate factor is a hard fail, not `1.0`.

### Testing and Tooling

- **No code without a failing test first; no plan without subagent orchestration — both, every time (§2.8).**
- **Shouldly only** (not FluentAssertions). **NSubstitute only** (not Moq).
- **No mocking what we don't own** beyond a thin port — wrap and mock the wrapper.
- **No mocked-DB tests** for behavior that depends on database semantics — integration tests hit real Postgres.

### Reflection and Magic

- **No reflection in hot paths** — source generators preferred; one-time startup wiring is acceptable.
- **No DI registration by convention scanning** — source-generated mediator registers at compile time; NServiceBus assembly scanning is disabled.
- **No framework magic that requires reading framework source to understand.**

### Process

- **No automatic git commits** — stage and show the diff; the human commits. Applies even when a skill's flow includes a commit step.
- **No skipping git hooks** (`--no-verify`, `--no-gpg-sign`) — fix the underlying issue.
- **No force-pushing to `master`.**
- **No committing secrets**, even temporarily.
- **No machine-local absolute paths in documents** — repo-relative or workspace-relative paths only; environment variables for unavoidable machine locations.
- **No stale README/CLAUDE.md pairs** — boy-scout law. The same change that alters what either describes updates both.
