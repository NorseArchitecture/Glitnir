# Spec Reconciliation Punch List — 2026-06-04

Findings from a full congruence sweep of all ten specs plus `codenames.md`, `decomposition.md`, and `project-structure.md`, conducted after the Norns spec landed. Each item carries enough context to be actioned in a fresh session without re-running the sweep.

**Verified clean during the sweep (no action needed):** the tenancy amendment wave (`TenantId` removal, `IConnectionResolver` simplification, middleware-list corrections) and the bulk of the messaging amendment wave (`IWebhookDispatcher` deletion, `.Backend` introduction, `.Server`/`.Worker` hard-wall corrections in hosting §5/§11 and persistence §14/§15.2-placement) propagated correctly everywhere they were claimed — except the specific misses listed below.

Legend: ☐ open · ☑ done. Update statuses in place; this file is the cross-session tracker.

---

## §1 — Design rulings required (live contradictions, no queued amendment)

These change project graphs or contract shapes. Each needs a human decision before the mechanical edits in §2 that depend on it.

### ☑ 1.1 UI Composition violates the persistence walls — RESOLVED 2026-06-05

Was: `Midgard.UI.Composition.Server` carried `EfCoreWidgetLayoutStore`, EF entity configs, and a Postgres `ui_composition.layouts` table read/written synchronously from the web tier.

**Ruling (Buvy):** the likely ruling, taken further — **Mongo as system of record** (documented inversion #2; rationale differs from Auth's: not a sync-hot-path argument but "a dashboard layout is not an insurance fact" — no audit/reporting/bordereaux value, no Postgres projection at all). `IDocumentRepository<LayoutDocument>` from `.Server`; authoritative idempotent upsert (UUID v5 `_id` over subject + layout name); no `ProcessingStatus`, no worker, no `.Migrations`. EF surface deleted. Executed in the superseding spec (`2026-06-05-ui-composition-design.md` §6).

### ☑ 1.2 `NorsePrincipal` inverts the realm DAG — RESOLVED 2026-06-04

Was: declared in `{Company}.Auth.Contracts` (auth spec §5.2) but consumed by platform realms (Yggdrasil host middleware; `Norns.Audit` edge behavior, norns §8.3) — platform → product reference, inverting the realm DAG.

**Ruling:** the principal envelope (`NorsePrincipal`, `Population`, `Audience`, claim-name constants) moves to a new **`Asgard.Identity`** assembly — one-assembly-per-concern matches the Asgard pattern. `{Company}.Auth` remains the authority that *populates* it; Yggdrasil middleware and `Norns.Audit` consume Asgard-tier law. `Population` values stand as-is (generic B2B2C shapes; "Producer" = channel partner — survives the multi-vertical aspiration).

**Sub-point RULED 2026-06-05 (UI Composition session):** the type's new name is **`YggdrasilPrincipal`** per the `YggdrasilTier` precedent (svartalfheim spec §9.3: the Yggdrasil prefix marks platform-wide concepts). Amends: auth spec §3/§5, hosting spec realm notes, norns §8.3, CLAUDE.md §4 — execute in the mechanical passes (`ui-composition 2026-06-05` §3 already uses the ruled name).

### ☑ 1.3 `EfCoreMigrationContributor<TContext>` in `Asgard.Hosting` drags EF Core into every `.Server` compile — RESOLVED 2026-06-04

Was: hosting spec §10.1 places the convenience base (`where TContext : DbContext`) in the same assembly as `IWebHostPlugin`, which every `.Server` references — the exact transitive-EF leak the Norns spec split `Asgard.Persistence` out to kill (norns §4.1).

**Ruling:** `EfCoreMigrationContributor<TContext>` moves to **`Asgard.Persistence`**; `IMigrationContributor` (EF-free) stays in `Asgard.Hosting`. Not `Yggdrasil.Hosting.Migrations` — matrix check: `{Company}.{Context}.Migrations` assemblies (Infrastructure, context `{Context}`) derive from the base; a home in `Yggdrasil.Hosting.Migrations` (Infrastructure, context Hosting) would be a cross-context Infrastructure reference (YGG004). `Asgard.Persistence` is Abstractions-tier (✔c reachable from any Migrations assembly), already the sanctioned EF-referencing law assembly, and unreachable from `.Server` — the wall holds by construction. Amends: hosting spec §10.1, norns §11 listing (`Asgard.Persistence` gains the base), analyzers §4 if a mapping row is added.

### ☑ 1.4 `IWebhookCommand.IdempotencyKey` violates YGG101 strict — RESOLVED 2026-06-04

Was: `string? IdempotencyKey` on `IWebhookCommand` / `StripeWebhookReceivedCommand` (hosting spec §7.1, §11.2) — bare `string?` on a `*Command`, forbidden by amended YGG101.

**Ruling (Buvy):** the raw header string disappears entirely — the key is **synthesized**, not carried. Partner code (as the UUID v5 namespace) + SHA-256 of the raw request body → SequentialGuid's v5 generator → **`Guid IdempotencyKey`** on the command. Consequences:

- YGG101 satisfied structurally — `Guid` is legal on message types; no new domain type, no `PlainText`.
- Source-agnostic: the per-source idempotency-header variance in `BuildCommand` collapses — the key derives from what the partner actually *sent*, not what they labeled it. (`BuildCommand` may shrink to near-nothing; re-examine whether it's still per-controller during the amendment.)
- The synthesized Guid is a natural `ResourceId` for the messaging spec's deterministic-MessageId outgoing behavior (`MessageId = UUIDv5(command type, ResourceId)`, messaging §3) — broker-level dedup of replayed webhook deliveries falls out for free.
- Partner codes want namespace entries in the Svartálfheim UUID v5 registry (one per partner, or one webhook namespace keyed by partner code) — coordinate with the registry when it lands.

**Follow-on:** persistence §7.2's M2M request-hash dedup (hex SHA-256 string as Mongo `_id`) is the same shape — consider unifying it onto the same UUID v5 synthesis (Guid-keyed, same generator). Fold into the 2.6 pass.

Amends: hosting spec §7.1, §11.2 (`IWebhookCommand`, `WebhookControllerBase`, worked example). **§7.1/§11.2 portion EXECUTED 2026-06-07 — see §5.7** (`string? IdempotencyKey` → synthesized `Guid`; minimal envelope). The Svartálfheim UUID v5 registry namespace entries remain the open follow-on.

### ☑ 1.5 .NET 11 / C# 15 Svartálfheim vs .NET 10 platform — RESOLVED 2026-06-04

**Ruling: .NET 11 is the platform target, hard and fast.** Option (b) — no v0 non-union shape, no dual-targeting. Rationale (Buvy, 2026-06-04): RC1 with go-live license is ~3 months out (≈Sept 2026), RTM ~5 months (≈Nov 2026), and implementation is at minimum 6 months out — the gate costs nothing on the real timeline. Runtime-async and native discriminated unions are worth far more than insulation against preview-band API churn; the primitives spec's §4.1 caveat already contains any union-syntax drift inside Svartálfheim.

Consequences: the Svartálfheim spec stands as written (`net11.0`, C# 15 unions, runtime-async tier policy); the analyzers spec §13 migrate-at-RC1+go-live policy resolves to .NET 11 with no change in principle; every ".NET 10" hard-pin elsewhere is now mechanical debt → **item 2.12**. The analyzers §7 `Result<T>` "readonly record struct" text resolves toward the union representation (folded into 2.1).

### ☑ 1.6 Error vocabulary — RESOLVED + EXECUTED 2026-06-07

The **"crossing the streams"** ruling (Buvy): each layer is smart about one thing and carries no other layer's error vocabulary.

- **Svartálfheim** = scalar→domain *conversion* only. The six-case `Error` union collapsed to `Result<T> = Success<T> | Failure(ParseFailure reason + bounded diagnostics)`; `ParseFailure` enum = `Unspecified / Empty / Malformed`. `Collect` / `AggregateError` deleted (accumulation relocated). **EXECUTED** in primitives spec §1–§7, §10, §12, §13 (+ top amendment note).
- **Mediator** = application outcomes. Owns its own **`Outcome<T>`** (failure case named `Problem`, so it never collides with Svartálfheim's conversion `Failure`), distinct from `Result<T>`. `ErrorCategory` trimmed to **`Validation` / `NotFound` / `Conflict`**. Non-generic `Outcome` for validators; `Ok` / `Err` factories defined here. Field-failure aggregation (former `Collect`) lives in the validate step. **EXECUTED** in mediator spec §0–§11, §13 (+ top amendment note).
- **Yggdrasil host pipeline** = authn/authz (401/403, service-entry `[Authorize]` before the mediator — confirmed against Buvy's prior-platform `ServerGrpcServiceBase`) + transport conditions (503 broker-down, 500 uncaught) — never `Outcome` values. **Authorization left the mediator entirely** (`IRequestAuthorizer` deleted; no `Forbidden` category); the mediator runs *inside* an already-authorized service.

**Three result families now coexist, each its own concern:** `Result<T>` (Svartálfheim, conversion) · `Outcome<T>` (mediator, application) · `HttpResult<T>` (egress, transport). Names deliberately distinct.

**Spec debt spawned by this ruling:**
- **UI Composition spec — EXECUTED 2026-06-07.** Handler/API returns → `Outcome<T>` (§2.2, §5.2); gRPC door trimmed to the three categories with 401/403 as service-entry and 503/500 host-synthesized (§8.2); §8.3 client-side adapter named the **Yggdrasil half of the render-table realm split** (rebuilds wire status → `Outcome<T>`, components channel-dumb); catalog authz test reframed to service-entry denial, not `Err(Forbidden)` (§10); top amendment note added. The client-rebuild mechanism already existed in §8.3 — this made it congruent and explicit.
- **CLAUDE.md — enrichment, not a fault.** CLAUDE.md does not *contradict* the ruling (§8's "No `Result<T>` that nobody checks" applies to `Outcome<T>` equally; "Why Not MediatR" untouched); it is merely *silent* on the three-result-families vocabulary. Optional §4/§8 line; batch with the other pending CLAUDE.md passes (2.7, 2.12, 2.13, 2.18, 5.3), not in isolation.
- **§5.5** below — resolved by this ruling.
- **Auth follow-on** — the parked "auth uses `Result<T>`?" flag became its own design thread (2026-06-07). Seed captured in `2026-06-07-auth-result-shape-decision-inputs.md`; dedicated auth session pending (batch with auth items 2.11 / 2.17 / 5.8). Thrust: account-lifecycle ops → gRPC-able `IAccountApi` returning `Outcome<T>`; credential issuance → OpenIddict OAuth 2.1 (Auth Code + PKCE for MAUI); retire `AuthResponse`/`AuthResult` and the `SignInAsync`-in-handler coupling.

### ☑ 1.7 Norns broke `ICachedRepository<T>`'s Guid surface — RESOLVED 2026-06-04

Was: stance markers re-rooted on `IPersisted` (no `Id`), read-only links legal, but `ICachedRepository<T>` declared "unchanged" with `GetByIdAsync(Guid id, …)` — incoherent for a seeded link with no surrogate Id.

**Ruling:** rubber-stamped — same move Norns made for `ITemporalRepository<T>` (§7.4): the queryable surface stays stance-constrained; `GetByIdAsync` becomes an extension method constrained `T : IEntity`. Compile-time unavailable for links, no analyzer. Amends norns spec §5.6.

### ☑ 1.8 Auth spec contradicts its own 2026-06-03 Mongo inversion — RESOLVED 2026-06-04

Was: §8.3 magic-link tokens and §8.6 cookie sessions still in Postgres, both consulted in the auth hot path; §11 #6 still scopes `{Company}.Auth.Migrations` to identity tables.

**Ruling (Buvy) — split by durability, not by table.** Postgres' `auth` projection holds **durable, audit-grade history**; Mongo owns **TTL-churning operational state**. TTL workloads in Postgres are rejected outright — no expiry-cleanup jobs, no dead-row/vacuum churn on the reporting store.

| Concern | Store | Shape |
|---|---|---|
| Magic-link tokens (issue/redeem hot path) | Mongo, TTL index | Operational; single-use enforcement; system of record |
| Live cookie-session records (revocation check per request) | Mongo, TTL index | Operational; system of record |
| Session lifecycle history (sign-in, sign-out, revocation, global logout) | Postgres `auth` projection | Event-fed, insert-only stance, no TTL columns |
| Auth failures + lockouts | Postgres `auth` projection | Event-fed, insert-only stance — durable security audit |

Implies new published event types from Auth (`PrincipalSignInFailedEvent`, `PrincipalLockedOutEvent`, session lifecycle events alongside the existing `PrincipalSignedInEvent` family) feeding `{Company}.Auth.Worker`'s projection handlers. This stays **within the existing Auth inversion design** — it is not the Norns-deferred append-only audit-event store (norns §8.4's re-entry triggers are unaffected). Insert-only stance markers fit these projections exactly.

Amends: auth spec §8.3, §8.6, §3 (Contracts event list), §11 #6 → executes as item 2.11 (`{Company}.Auth.Migrations` = projection tables only).

---

## §2 — Queued-but-unapplied amendment debt (mechanical once §1 rulings land)

### ☐ 2.1 Analyzers spec §3 + §4 missed the 2026-06-03 amendment pass

- §3 `Layer` enum comment (`Infrastructure = 3`) still describes `.Server` as holding "entities, business logic" — entities live in `.Worker`.
- §4 project-mapping table: `{Company}.{Context}.Server` row still lists "entity classes, `IEntityTypeConfiguration<T>` impls"; `.Worker` row still says "references `.Server`" (hard wall now); **no `.Backend` row exists** (needs a layer/context mapping).
- §4 "What lives where, by concern" prose: `.Server` described with "entities (which double as EF Core entities) … EF Core mappings"; `.Worker` "references `.Server` for shared internals"; `.Backend` absent.
- §4 table contains duplicated row blocks (UI.Composition and Yggdrasil rows appear twice).
- §15.3 says "Five-realm top-level namespace split" — it's seven.
- §7 YGG201 notes say `Result<T>` is a `readonly record struct` — primitives spec made it a C# 15 union; ruling 1.5 (RESOLVED: .NET 11 locked) settles this toward the union representation. Update the YGG201 implementation notes (the three consumption patterns are unchanged; only the type-shape description and the §13 ".NET 10 currently" parenthetical move).
- The amendment hit §5's intra-service diagram and §15.7/15.8 but skipped all of the above.

### ☐ 2.2 Hosting spec §5 never gained the handler-contribution method

Messaging spec §12 claims the hosting amendments were applied 2026-06-03 including "plugin interfaces gain the handler-contribution method" — they weren't. `IWorkerHostPlugin` is still an empty marker; neither interface carries the explicit NSB handler/saga registration contribution (messaging §3). Apply the missing amendment; correct messaging §12's applied-status claim if needed.

### ☐ 2.3 Hosting spec §8 / §11.1 superseded by the mediator

`services.AddScoped<IBillingApi, BillingService>()` and the hand-written `BillingService` are superseded by the generated `{Context}Service` + `Add{Context}Mediator()` (mediator spec §8). The mediator spec never queued a hosting amendment; queue and apply it.

### ☐ 2.4 Hosting spec §12 Aspire snippet uses stale deployable names

`Projects.{Company}_Migrations` / `Projects.{Company}_Host` / `Projects.{Company}_WorkerHost` → deployables are `Yggdrasil.*` (analyzers §15.9 sequestration).

### ☑ 2.5 UI Composition spec rewrite pass (the big one) — EXECUTED 2026-06-05 via full supersession

`2026-06-05-ui-composition-design.md` supersedes the 2026-05-19 spec entirely (old file marked SUPERSEDED, retained as history). Every queued amendment landed: hosting §15.1 (native gRPC stack, §8 there); mediator §12 handoff (proto ↔ record mapping + `ErrorCategory` door contract, §8.2/§8.3); ruling 1.1 (Mongo inversion #2, §6); five-project vocabulary (and the new project-shape law, §5.1); `IWidgetEventBus` impl → `.Components` with a scoped-lifetime rule (§3); `BillingSummaryDto` → `BillingSummary` (§2.2).

Beyond the queued items, the session produced nine new rulings (spec §12): render modes replace the three-stage lifecycle; JSON door exits UI scope; single authorization-filtered app; **{Company}.Hlidskjalf** (codenames.md updated same change set); durable-resume-state law; `.Backend` never-client law; `Yggdrasil.Host` hosts the Blazor Web App + **DevServer deleted**; `Yggdrasil.Hosting.Web.Server`/`Web.Client`/`Maui` runtime family. New amendment debt spawned → items 2.13–2.17 below.

### ☐ 2.6 Persistence spec ← Norns §13.1

Already queued; listed for tracking: §4.2/§4.4 contract relocation + `ITemporalRepository` redesign (`AsOfRangeAsync` dead); §5 marker hierarchy superseded (forced stance kills "plain `IEntity` ✓ allow"); §10 `TstzRange` shape (`RangeBoundType` dead, non-null `Upper`), GIST exclusion → `WITHOUT OVERLAPS`, convention/converter relocation to `Norns.Temporal`; §14 listing; §15.2 worked example → `record` deriving `TemporalEntityBase<Policy>`. Also: §15.1/§15.3 worked examples still show `PolicyView : IWireShape` in `Contracts` and a hand-written `PolicyService` throwing `RpcException` — superseded by mediator §3.4/§7 (partially tracked in persistence §17 #6; finish the job in the same pass).

### ☐ 2.7 CLAUDE.md ← Norns §13.2

Already queued; listed for tracking: §4 Persistence ("`Asgard.Infrastructure` declares the four repository contracts" → split with `Asgard.Persistence`); §5 "`sealed class` for entities" → records (with documented edges); §5 Norns realm assembly list; §8 forced-stance + skip-navigation anti-patterns.

### ☐ 2.8 decomposition.md + project-structure.md post-split staleness

- `decomposition.md` `asgard-infrastructure` row: "Repository contract family + shared entity bases" → reflect the `Asgard.Infrastructure` / `Asgard.Persistence` split (both live in the asgard-infrastructure submodule per norns §4.3).
- `project-structure.md` `.Worker` bullet: "repository contract family from `Asgard.Infrastructure`" → `Asgard.Persistence` for the worker-only contracts.

### ☐ 2.9 Hosting spec §10 never queued for the Norns seed phase

Norns §11 asserts "`Yggdrasil.Migrations` gains the seed phase: schema migrations → Norns seed engine → `ReferenceDataReloadedEvent`," but norns §13 queues no hosting-spec amendment. **Tracked nowhere until this list.** Amend hosting §10 (orchestrator gains the post-migration seed phase, sentinel-key mechanics) — interacts with ruling 1.3.

### ☐ 2.10 YGG catalog absorption

Analyzers spec §6 has absorbed none of the rules other specs have queued, while §16 claims "no remaining open questions" (false). One consolidation pass:

| Source | Rules |
|---|---|
| Auth §11 #1 | YGG110 — `[AllowAnonymous]` forbidden |
| Mediator §9 | YGG401–408 ratification |
| Messaging §11 | Message placement, endpoint flavor, worker/server purity (numbers TBD) |
| Persistence §17 #5 | Marker composition matrix (sketched as YGG109–112) |
| Norns §13.3 | New: forced stance, skip-navigation ban, `UpdateAsync` forbidden on `ILinkEntity`. **Retired:** worker-only repository checks (structurally dead per the `Asgard.Persistence` split) |

Also adopt Norns' governing principle into the analyzers spec body: "never write a YGG rule where a type constraint or assembly boundary will do."

### ☐ 2.11 Auth spec §11 #6 migrations scope

Mechanical tail of ruling 1.8 — rewrite the `{Company}.Auth.Migrations` inbox item to projection-tables-only.

### ☐ 2.12 Platform-wide .NET 11 retarget (mechanical tail of ruling 1.5)

Every ".NET 10" hard-pin updates to .NET 11 / EF Core 11 / C# 15:

- **CLAUDE.md §4** — ".NET 10, C# (latest)" → ".NET 11, C# 15 (native discriminated unions and runtime-async are load-bearing platform features, not nice-to-haves)". Key-libraries table "Target runtime" row.
- **Analyzers spec §13** — "(currently .NET 10)" parenthetical; the migrate-at-RC1+go-live policy itself is unchanged and is what this ruling exercised.
- **Norns spec §15** — acceptance ".NET 10 + EF Core 10" → ".NET 11 + EF Core 11".
- **Persistence spec §18** — acceptance ".NET 10" pin.
- **Mediator spec** — no version pin to fix, but note the lib floor (martinothamar/Mediator 3.0) needs a .NET 11 compatibility check when RC1 lands (open item §13 #2 there already covers re-evaluation).
- **Svartálfheim spec** — stands as written; it was the spec that had it right. Its §4.1 preview-syntax caveat gains a "re-pin at RC1" note alongside the existing PG19-at-RC1 re-verification (norns §14 #2) — the two RC1-era re-checks should ride the same calendar trigger.
- Sweep for any remaining "net10.0" / ".NET 10" strings across `docs/` when executing this item.

### ☐ 2.13 CLAUDE.md ← UI Composition supersession (2026-06-05)

§4 Hosting: DevServer deleted; `Yggdrasil.Host` gains the Blazor Web App surface (SSR + circuits + WASM bundle). §5: deployables list (`DevServer` removed), `Yggdrasil.Hosting.*` list (`Web` → `Web.Server`, add `Web.Client`, add `Maui`), product realm list + submodule list gain Hlidskjalf, `.Backend` row gains "never client-reachable (analyzer-enforced)", §4 Persistence Mongo bullet notes inversion #2 (layouts).

### ☐ 2.14 Hosting spec ← UI Composition supersession

`Yggdrasil.Host` definition gains the Blazor Web App surface; `Yggdrasil.Hosting.Web` → `Yggdrasil.Hosting.Web.Server` ripples through §3/§5/§9/§12; mark §15.1 **applied** (landed as ui-composition 2026-06-05 §8); DevServer references removed if any.

### ☐ 2.15 project-structure.md + decomposition.md ← UI Composition supersession

Deployables catalog (DevServer removed; Host's Blazor surface); submodule table (`{company}-hlidskjalf` added; `yggdrasil-clients` contents updated); project-shape law (`.Backend` exists iff `.Server` + `.Worker` both exist; a context ships only the projects its persistence stance demands).

### ☐ 2.16 Analyzers spec ← `.Backend` never-client rule

Fold into the 2.10 catalog absorption: any `*.Components` assembly or client deployable referencing a `*.Backend` assembly is a build error. (Source: ui-composition 2026-06-05 §9.)

### ☐ 2.17 Auth spec ← `{Company}.Auth.Components` declaration check

Hlidskjalf references `{Company}.Auth.Components` (login/profile surface). Confirm the auth spec's assembly list declares it; add the line if missing.

### ☐ 2.18 CLAUDE.md ← Performance posture spec (2026-06-05)

§4 gains the logging law (`[LoggerMessage]`-only, CA1848/CA2254/CA1727 as errors) and JSON law (source-gen only, `JsonSerializerIsReflectionEnabledByDefault=false`); §4 "AOT-clean where feasible" strengthens to AOT-as-end-state with named blocker register; §5 gains the `{Submodule}.Benchmarks` naming convention; §8 gains three anti-patterns (no string-interpolated logging, no reflection-based JSON serialization, no new AOT blockers without documented exception). Source: `2026-06-05-performance-posture-design.md` §8.

### ☐ 2.19 Hosting spec ← NSB serializer resolver-chain wiring

The hosting runtime wires NSB's System.Text.Json serializer to a combined `JsonTypeInfoResolver` over loaded plugins' message contexts; missing message type = startup failure. Lands where unobtrusive-mode/serializer config is specified; ride the 2.2/2.3 hosting pass. Source: performance posture spec §5.2.

### ☐ 2.20 Svartálfheim primitives spec ← platform-law cross-reference

§12.4 gains a note that its benchmark machinery is now an instance of platform law (performance posture spec §2); §9 tier policy gains the end-state framing — server-tier JIT is a temporary concession to the §7.2 blocker register, not a stance. Batch with the 2.12 touch of the same file.

### ☐ 2.21 Function-first namespace sweep (the big one — 2026-06-07 capstone)

**Decision (CAPSTONE, see `docs/norse-architecture.md` + `docs/codenames.md`):** the platform substrate's code/spec namespaces become **`Norse.{Function}`** (Asgard→`Norse.Abstractions`, Midgard→`Norse.Infrastructure`, Svartálfheim→`Norse.Primitives`, Yggdrasil→`Norse.Hosting`, Norns→`Norse.ReferenceData`, Muninn→`Norse.Warehouse`, Heimdall→`Norse.Auth`, Gjallarhorn→`Norse.Observability`, Mímir→`Norse.AI`, Hlidskjalf→`{Company}.Shell`, Ratatoskr→`Norse.Notifications`); products stay `{Company}.{Context}.*`; codenames retreat to lore (README/dictionary). Tyr/Valkyrie unplaced (in the ether).

**Done (continuity-gap close, 2026-06-07):** the model-defining sources — CLAUDE.md §1 realm table, §3 cross-cutting list, §5 namespaces, §6 registry, and `docs/codenames.md` (now the ethos⇒function dictionary, Reserved tier killed) — all reflect the capstone. A cold-start reader is no longer behind.

**Dated spec/plan corpus — SWEPT 2026-06-07** (6 parallel agent groups, ~25 files): every **dotted/backtick operational** codename namespace → `Norse.{Function}` / `{Company}.*` across all specs and plans (hosting, primitives, persistence, norns, tenancy, mediator, messaging, analyzers, performance, ui-composition ×2, auth ×3, multiproduct, egress, vector, aichatweb, editorconfig, build-enforcement). Lore/realm-actor prose, H1 titles, dated filenames, lowercase DB schema literals (`heimdall`/`auth`), third-party packages, and PascalCase API/type symbols were preserved by rule. Notable correct judgment: the rhetorical `"not {Company}.Auth.*"` in the Heimdall spec was preserved (renaming inverts its meaning); the affirmative `{Company}.Auth.*` in the superseded federation spec was renamed.

**Remaining (fresh-mind living-docs pass + decisions):**
- **Living docs not yet swept** (reserved for a careful hand-pass): CLAUDE.md **inline** §2/§4/§7/§8 usages; `decomposition.md` submodule map + realm refs; `project-structure.md`.
- **Four residue decisions, all riding the Yggdrasil-umbrella-vs-Norse-brand flag** (`docs/codenames.md`): (a) **bare realm-actor prose** ("Midgard owns", "Asgard tier", "Heimdall judges") — convert to function-prose, or keep codenames as acceptable lore-shorthand? (b) **PascalCase API/type symbols** — `AddYggdrasilWebHost`, `YggdrasilTier`, `HeimdallService`, `HeimdallDbContext`, `MidgardDbContext` → function form, or keep? (`YggdrasilPrincipal` stays per ruling 1.2.) (c) **lowercase repo/submodule slugs** — `asgard-infrastructure`, `yggdrasil-hosting`, `{company}-auth` → `norse-*` form? (d) **lowercase DB schema names** — `heimdall` schema → `auth`?
- **Ghost references (content cleanup, not naming):** `Yggdrasil.DevServer`, `Yggdrasil.Hosting.Wasm`, `Yggdrasil.Hosting.Composition` reference *deleted* projects (DevServer deleted 2026-06-05; Wasm/Composition erased) — remove/update in a content pass, separate from the rename.
- The **Æsir-README** (lore home) still to author.

---

## §3 — Hygiene (low stakes, batch anytime)

- ☐ **"Dto" occurrences** (banned vocabulary): analyzers YGG105/YGG107 rule text (`*Dto`); persistence §3.3 (`enrichedDto`). ~~ui-composition §4 `BillingSummaryDto`~~ — cleared by the 2.5 supersession (2026-06-05).
- ☐ **Mediator §10 references `ICommandSender`** — ghost of the deleted dispatch abstraction; §5 settled on raw `IMessageSession`.
- ☐ **CLAUDE.md §4 sells TimescaleDB for "audit logs"** — Norns §8.4 deferred the audit event store (TimescaleDB hypertables named in the deferral). Soften to match.
- ☑ **Skuld collision-adjacent** — RULED 2026-06-04: pulled from the bench (Urð/Verdandi/Skuld are load-bearing Norns facets). Executed in `codenames.md` same day.
- ☐ **Auth §3 "standard three-assembly bounded-context layout"** — then lists four; platform shape is five. Wording fix.
- ☑ **YGG101 family mentions `*Notification`** — RULED 2026-06-04: **trim** (no-speculative-surface; a `*Notification` type appearing today would be a naming violation before a PII one). Execute in the 2.1 (analyzers) and 2.7 (CLAUDE.md) passes. The future notifications spec (item 4.1) owns reintroducing a message kind and its YGG101 coverage if/when it earns one.

---

## §4 — New spec demand surfaced by this sweep

### ☐ 4.1 Notifications spec (full-scale)

Ruled needed (Buvy, 2026-06-04). NServiceBus-driven outbound notifications are a platform concern with no owning spec:

- **Multiple channels out** — UI push (gRPC streams / SignalR), email, SMS, future channels.
- **The backplane question** — the messaging spec's per-replica ephemeral `{company}.{context}.web` endpoints (§2 there) already act as the event backplane for UI push, but nothing owns the push surface itself: connection registry, authorized fan-out, delivery semantics across replicas, SignalR-backplane-vs-NSB-fan-out trade.
- **Channel routing** — Customer context owns communication preferences (CLAUDE.md §3); the notifications layer consumes them but must not absorb them.
- **Codename** — likely activates the **Ratatoskr** reservation (outbound messaging / notification relay; took the slot vacated 2026-06-07 when its prior holder became the logistics product realm); `codenames.md` updates when the spec lands.
- **Message kind** — decides whether a `*Notification` message kind (and its YGG101 coverage, trimmed in §3 above) is reintroduced. Trimmed stays trimmed until this spec re-earns it.

### ☐ 4.2 Build-enforcement stack session (planned ≈2026-06-06/07 weekend)

Ruled needed (Buvy, 2026-06-05). The root `.editorconfig` is template-default ("pure as the driven snow") — a dedicated session bangs out the optimal enforcement mix across the three levels before the YGG analyzer work lands: **MSBuild `AnalysisLevel`/`AnalysisMode` + root `Directory.Build.props` + `.editorconfig`** — which rule enforces at which level, ratcheted to error per platform philosophy. Absorbs the queued mechanics:

- `dotnet_style_require_accessibility_modifiers` → `omit_if_default` (currently `for_non_interface_members:silent` — contradicts CLAUDE.md §2.3).
- CA1852 (seal internal types) → error (CLAUDE.md §2.3).
- CA1848 / CA2254 / CA1727 logging ratchets → error (performance posture spec §4.4).
- `JsonSerializerIsReflectionEnabledByDefault=false` placement (performance posture spec §5.1).
- `src/Directory.Build.props` per repo: `<InternalsVisibleTo Include="$(AssemblyName).Tests" />` (CLAUDE.md §2.3).
- The untracked root config files (`.editorconfig`, `.gitattributes`, `.gitignore`, `dotnet-tools.json`, `global.json`) get reviewed and committed as part of this session.

**Phase 2 executed (2026-06-06):** `.editorconfig` curated per `docs/Platform/specs/2026-06-05-editorconfig-curation-design.md` — `omit_if_default` ✓, CA1848/CA2254 ✓ (category-knob reach, canary-proven), CA1727 ✓ (targeted severity), CA1852 ✓ (canary-proven; requires `ignore_internalsvisibleto` — POC deviation #12), `JsonSerializerIsReflectionEnabledByDefault` ✓ (relocated to `src/`-only delta by ruling), root config files ✓ (committed 2026-06-05). Remaining from 4.2: real-tree seeding, `UseProjectReferences` session.

**Scope extension (Buvy, 2026-06-05): cross-repo reference switching.** The session also stands up the `UseProjectReferences` machinery across the entire submodule landscape: cross-repo references resolve to `ProjectReference` locally and `PackageReference` in CI, `$(CI)` forces package mode, single toggle. The payoff being bought: **debugging CI issues on your own machine** — flip to package mode locally instead of opening a PR and hoping. Validation is end-to-end, not on-paper: publish dummy packages to the GitHub Packages NuGet feed if that's what it takes to prove the package-resolution path resolves correctly across every submodule. Get it right now, not later. (Standing law already applies: reference items in plain `ItemGroup` elements, never inside `<Target>` blocks — YGG301.)

Overlaps the meta-repo build-infrastructure work (Svartálfheim plan task 2) — this session is that work's enforcement-configuration venue. With the reference-switching scope added, the session may produce spec-worthy output (meta-repo build-graph mechanics); promote to a spec during the session if the mechanics warrant a citable home rather than forcing it now.

**Status (2026-06-05):** MSBuild-law phase designed (`docs/Platform/specs/2026-06-05-build-enforcement-design.md`, planned via `docs/Platform/plans/2026-06-05-build-enforcement.md`) and **proven in the `poc/build/` replica** — harness `Verify-Enforcement.ps1` green, canary ledger + eleven deviations in its `FINDINGS.md` (headline: `EnforceCodeStyleInBuild` makes a root `.editorconfig` seed a build prerequisite; `ArtifactsPath` must be pinned at the law; CS1591 needs an isolated canary toggle). Remaining: `.editorconfig` curation (Phase 2), real-tree seeding from the replica, and the `UseProjectReferences` switching session (owns `Directory.Build.targets`).

**CPM stance (Buvy, 2026-06-05): decide-by-doing, deliberately.** Apply what's known to work where it obviously fits (the fronting carrier's tier strategy as the reference point); leave the unknowns — Midgard's tier, `{company}-{context}` floating, submodule-pin vs package-range authority — to trial and error during build-out. No ivory-tower rulings without hands-dirty understanding of the repercussions. The session should *not* attempt to close CPM; it stands up the switching machinery and records what was tried.

### ☐ 4.3 EncryptedString spec (tracking adoption — demand predates this sweep)

The surviving work item from the PII/encryption ruling (CLAUDE.md §4 → PII and Encryption, §7 #11) was tracked only in prose until now; listed here so this file is the single complete to-do surface. Owns: `EncryptedString` wrapper mechanics, blind-index (HMAC) companion-column design (designed once, never ad hoc per table), AES-256-GCM nonce bounds, Key Vault envelope/rotation mechanics, per-customer DEK lifecycle, local-dev keys. Platform-tier (Svartálfheim wrapper + Asgard/Midgard integration) — fits the platform-first roadmap alongside 4.1.

---

### ☐ 4.4 pgvector-line-under-review flag (vector/embeddings decision-inputs, 2026-06-07)

Bookkeeping, surfaced by the 2026-06-07 sweep: `2026-06-07-vector-embeddings-decision-inputs.md` flags CLAUDE.md §4 ("pgvector for embeddings feeding Mímir") and the two hosting-spec §13 #17 parentheticals as **under review, not reversed** — superseded only if/when the Mímir spec rules (sync vector serving signaled real; Mongo Atlas Vector Search is the presumptive store). Logged here so this file stays the complete cross-spec surface. **No action until the Mímir spec convenes** (platform-first sequencing); that spec owns the resolution and the matching CLAUDE.md/hosting cleanup. The quasi-PII embedding-eligibility question is separately parked for its own session (decision-inputs §4) and feeds the EncryptedString spec (4.3).

---

## §5 — Egress spec (2026-06-07) cross-spec impact

Surfaced when the Egress spec (`2026-06-07-egress-http-resilience-parsing-design.md`) landed. The spec is congruent with Svartálfheim (`Result<T>`, `[MustConsume]`, non-boxing union pattern reused, not re-invented) and `codenames.md` (Egress is descriptive-within-a-realm per the `Asgard.Infrastructure` / `Midgard.Persistence` precedent — **not** a codename, no registry change). The items below are the deltas it does create.

### ☑ 5.1 Hosting spec resilience model contradicts egress named profiles — RULED + EXECUTED 2026-06-07

Hosting spec §8, §9 (HttpClient-defaults rows), and rule #13 stated resilience is applied **globally** via `ConfigureHttpClientDefaults(b => b.AddStandardResilienceHandler())` and "**plugins do not call resilience extensions per-client**." The worked example was `AddHttpClient<IPaymentsClient, PaymentsClient>` — but a payment processor is **external egress**, which the egress spec routes through `AddExternalApi` with a *required* named profile + classifier.

**Ruling (Buvy, 2026-06-07):** the hosting spec's global-resilience model predates today's egress POC and is **stale and wrong** — amend it. The global `AddStandardResilienceHandler` default **stays for infrastructure HttpClients** (OIDC/metadata, OTel export, internal plumbing); **external/third-party egress goes through the egress layer**, which removes the global handler and applies its named profile + classifier-driven retry. The plugin author never hand-wires Polly (picks a profile name), so the principle "plugins don't hand-wire resilience" survives — only the "global standard for *all* clients" letter changed.

**Executed 2026-06-07:** hosting spec amended — top `**Amended:**` line + egress companion-spec entry added; §8 HttpClient-registration reframed (the `IPaymentsClient` example is now an `AddExternalApi("payments", ...)` egress registration; infrastructure-client carve-out spelled out); §9 web + worker HttpClient-defaults rows amended; rule #13 rewritten. Inbound `StripeWebhookController` left untouched (it is *not* egress — egress is outbound only).

### ☐ 5.2 decomposition.md submodule map — add the egress pair

Submodule map (§Submodule Map) gains an `asgard-egress` (Asgard.Egress — `HttpResult<T>`, `EgressError`, `IResponseParser<T>`, `EgressClassifier`, `IHttpEgress`) + `midgard-egress` (Midgard.Egress — JSON/XML parsers, `sealed HttpEgress`, `AddExternalApi`) lockstep pair, matching the existing Asgard-contracts/Midgard-impl convention. The `DelegatingHandler` pipeline + named resilience profile catalog ride `Yggdrasil.ServiceDefaults` (`yggdrasil-hosting`); note it where the resilience default is described.

### ☐ 5.3 CLAUDE.md — egress as the sanctioned outbound-HTTP path

§4 (Technology Decisions) gains a short Egress entry: external/third-party HTTP goes through `IHttpEgress` / `AddExternalApi` (named resilience profile + per-partner `EgressClassifier`); typed `HttpResult<T>` return; four-shape parser seam. Ties to the existing §8 anti-pattern "No synchronous cross-context RPC for writes" / "no cross-service HTTP" — egress is the *outbound-to-the-outside-world* counterpart, equally load-bearing. Batch with the other queued CLAUDE.md amendments (2.7, 2.12, 2.13, 2.18).

### ☐ 5.4 Performance posture spec — AOT blocker register gains the F# type-provider egress entry

The egress shape-3 `Func<string, T>` path backed by an F# type provider is a named, isolated AOT blocker (egress spec §8). Add it to the perf-posture blocker register (§7.2) so the "no new blockers without documented exception" rule (item 2.18 / perf §8) has the documented exception on file. Confined to the single egress client that opts in; default JSON path stays source-gen/AOT-clean.

### ☑ 5.5 Error-vocabulary cross-reference — RESOLVED 2026-06-07 (via 1.6)

The egress `HttpResult<T>` / `EgressError.FailureKind` stays **deliberately separate**: transport ≠ application ≠ conversion. The 1.6 ruling formalized three distinct result families (`Result<T>` conversion / `Outcome<T>` application / `HttpResult<T>` transport). `HttpResult.NotFound` (a 404 from a third party) and `ErrorCategory.NotFound` (our record doesn't exist) are genuinely different facts that correctly share a word across different vocabularies. No alignment; no further action.

### ☐ 5.6 project-structure.md — egress client registration (minor)

Note that external API clients register via `AddExternalApi` in the owning `.Worker` (or `.Server`) plugin's `ConfigureServices` — the egress facade is infrastructure consumed by a context, not a new per-context project. Low stakes; batch with 2.15 (the other project-structure.md touch).

### ☑ 5.7 Webhook design: auth-as-verification + handshake hook + minimal command — EXECUTED 2026-06-07

Surfaced when Buvy supplied a production `WebhookControllerBase<T>` + Monday.com implementation. Three rulings, all executed in the hosting spec (the design iterated within the session — an initial per-controller `PartnerNamespace` + Svartálfheim namespace-registry shape was superseded by the auth-handler shape below):

- **Verification is authentication, not per-command validation.** `IWebhookValidator<TCommand>` is **deleted**. Three `WebhookSchemes` replace it — `ClientCredentials` (JWT bearer, OpenIddict, preferred), `Signature` (HMAC over body), `Whitelist` (source-IP) — one per partner capability tier (none → whitelist, signature → HMAC, client-credentials → JWT). Each is a generic, data-driven ASP.NET **authentication handler** (not authz policy — only authentication can enrich the principal before claims freeze, which was Buvy's blocker). The handler resolves the partner's OpenIddict `client_id` and surfaces it as the `client_id` claim. Controllers declare their tier with one `[Authorize(AuthenticationSchemes = …)]`; the base reads the namespace uniformly.
- **`client_id` IS the UUID v5 namespace** (Buvy's unification — no separate per-partner namespace registry; OpenIddict's client store *is* the partner registry). Partner clients get **Guid `client_id`s** by registration convention. JWT tier: `client_id` from the validated token. Non-JWT tiers: `{partnerCode}` route segment → `IWebhookClientResolver` (Asgard contract, implemented by `{Company}.Auth.Server` over the OpenIddict application store) → `client_id` + verification material. The signing secret is an `EncryptedString` application property, **NOT** the hashed `client_secret` (unrecoverable; serves only the token flow). Route partner-code is untrusted until the looked-up client's signature/IP check passes. **Discharges ruling 1.4's §7.1/§11.2 edit** (minimal `IWebhookCommand`: `byte[] Bytes` + synthesized `Guid IdempotencyKey` + `DateTimeOffset ReceivedAt`; no headers/URL/IP on the wire).
- **Verification/challenge handshake = base-class hook.** Monday `{"challenge"}` / Slack `url_verification` / Meta `hub.challenge` is the *sole* non-202 success path. `protected virtual ValueTask<IActionResult?> TryHandleVerificationAsync(byte[] body, HttpRequest, ct)`; parses the already-captured bytes (no `EnableBuffering`, no stream re-read); default null → dispatch + 202.

**Executed 2026-06-07:** hosting spec §4 (steps 1/2/3 + idempotency bullet), §7.1 (contracts rewrite: `WebhookSchemes` + `IWebhookClientResolver` + `WebhookClient` replace the validator; base class; new §7.1.1 schemes section; Stripe/Monday examples retiered), §11.2, rules #15 and #16, two 2026-06-07 top `**Amended:**` lines. Open follow-on: `WebhookKey.Synthesize` wraps the SequentialGuid v5 generator (lands with Svartálfheim; no per-partner registry needed — namespace is data). Auth-spec absorption → **§5.8**.

### ☐ 5.8 Auth spec ← webhook client modeling (from §5.7)

The auth spec (`2026-05-20-auth-federation-design.md`) owns the OpenIddict client modeling the webhook auth handlers depend on; absorb:

- **Guid `client_id`s for partner/producer clients** (registration convention) — the `client_id` doubles as the webhook UUID v5 namespace. State it where partner/producer client registration is described.
- **Webhook config as OpenIddict application properties**: a queryable `partner_code` (route lookup key; Mongo-indexed on the application store), the capability tier, the signing secret as `EncryptedString` (signature tier), the IP allowlist (whitelist tier). Explicitly **not** the hashed `client_secret`.
- **`{Company}.Auth.Server` implements `IWebhookClientResolver`** (`partnerCode` → `WebhookClient`) over the OpenIddict application store; declare it in the auth spec's assembly/responsibility list.
- **The three `WebhookSchemes` register** in the web host (`AddYggdrasilWebHost`) with the OpenIddict bearer scheme for the client-credentials tier; note the cross-reference so auth and hosting agree on scheme names. Interacts with the EncryptedString spec (signing-secret storage) — coordinate when that lands.

---

## Suggested order

**Status 2026-06-05 (end of UI Composition session):** 1.1 + 2.5 executed via full supersession (`2026-06-05-ui-composition-design.md`); nine new rulings recorded there (§12); new mechanical debt 2.13–2.17 queued. Only **1.6** (error vocabulary) remains open in §1 — parked deliberately.

**Status 2026-06-05 (performance posture session, same day):** full punch-list verification sweep run — every ☑ confirmed landed in the files, every spot-checked ☐ confirmed genuinely stale, no new untracked incongruences from the 06-05 UI spec. New spec `2026-06-05-performance-posture-design.md` codifies benchmark convention, logging law, JSON law, allocation posture, AOT end state; mechanical debt 2.18–2.20 queued from it. Remaining work:

**Status 2026-06-07 (error-vocabulary session):** **1.6 RESOLVED + EXECUTED** — the "crossing the streams" ruling, amended into the primitives spec (`Result<T>` → `Success`/`Failure(ParseFailure)`) and the mediator spec (own `Outcome<T>`, `ErrorCategory` trimmed to 3, authorization removed to service-entry). §5.5 resolved (three result families stay separate). New mechanical debt: UI Composition client-side `Outcome<T>` rebuild (tracked under 1.6). New tracked item 4.4 (pgvector-line flag). **§1 is now fully closed.** Remaining work below is mechanical passes + new specs only.

1. ~~**1.6** — error vocabulary reconciliation.~~ **DONE 2026-06-07** (see §1.6).
2. **2.1–2.4, 2.6–2.9, 2.11–2.20** — mechanical amendment passes; every ruling they depend on is now made.
3. **2.10** — YGG catalog consolidation last (it absorbs outputs of everything above, including 2.16).
4. **§3 leftovers** — batch with whichever pass touches the same file.
5. **4.1** — notifications spec; sequence per the platform-first roadmap (it's platform-tier — fits before the insurance-product deep dive). The UI Composition session sharpened its framing: the component-facing shape is "subscribe to an authorized server event stream, transport-blind" (circuit re-render vs gRPC server-stream is the container's problem — ui-composition 2026-06-05 §3).
6. **4.2** — build-substrate session (enforcement stack + `UseProjectReferences` cross-repo switching), planned for the 2026-06-06/07 weekend; independent of the mechanical passes (build-level, no spec text it depends on) and should land before any implementation plan executes.
