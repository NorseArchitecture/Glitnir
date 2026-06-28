# Multi-Product Platform — Yggdrasil as a Company-of-Companies (Foundational Topology)

**Date:** 2026-06-07
**Status:** Draft for review — framing approved 2026-06-07; foundational topology only (deep decomposition, repo mechanics, and per-product domain specs are scoped follow-ons, §11).
**Owner:** Buvy
**Reframes:** CLAUDE.md §1 (Norse's purpose), §5 (`{InsuranceCo}.*` → `{Product}.*`), the realm table (platform-realm vs product-realm distinction) — amendments spawned in §10.
**Companion specs:** `2026-06-07-auth-design.md` (Auth — already written against this multi-product reality; per-product identity ruled there and here); `2026-06-03-tenancy-model-design.md` (stamp-per-tenant — the per-entity isolation mechanism); `2026-05-20-yggdrasil-hosting-design.md` (plugin model — the conformance contract); `2026-05-26-mediator-design.md` / `2026-05-21-midgard-persistence-design.md` (the rails a product rides). Build-substrate session (reconciliation tracker 4.2) owns the deferred repo/package mechanics.

---

## 1. Motivation — The Substrate *Is* the Demonstration

Yggdrasil is not "the technology platform for the first product." **Yggdrasil is a company-of-companies — a venture studio whose product is companies.** The **founding product triad** — insurance MGA, deregulated energy retail, and logistics / wholesale distribution — maps directly onto the founder's three domain pillars, the verticals he can credibly build; and the many more to follow are each, like these three, a **separately-capitalized operating entity**: its own cap table, its own investors, its own terms, its own balance sheet, its own compliance perimeter. What they share is one rigorously-built technology substrate — the platform realms, the Æsir's declared contracts, and Infrastructure's rails.

**Each product realm is named for its governing figure** — the Norse figure whose own myth *rules that vertical's domain*, not a description of a service (contrast: platform services are named for function). The founder wears one hat per vertical, and each hat answers to its governing figure. The founding verticals are insurance (the first product), deregulated energy retail, and logistics / wholesale distribution; the actual assignments are each venture's own record, kept in its court until brand launch (see `docs/codenames.md`). This is codified as `docs/codenames.md` rule #6 / CLAUDE.md §6 rule #6. (Assigning a benched name to a product realm moved its prior Reserved slot — outbound messaging / partner hub — to **Ratatoskr**, a more exact fit for a message courier; see `docs/codenames.md`.)

The thesis the platform exists to prove — *the world has been building software wrong* — **is the substrate itself.** The proof is empirical and economic: standing up the energy retail vertical as a fundable energy company is mostly `{EnergyCo}.{Context}.*` domain code dropped onto rails that already exist (auth, persistence, hosting, observability, reference data, messaging), instead of yet another company reinventing all of it from scratch while accruing the usual tech debt and build-error tax. The reference-implementation goal and the business model are the *same artifact*: spec-first, pit-of-success, and compile-time enforcement are not merely how you build the platform well — they are what make a **new entity cheap and safe enough to capitalize on its own terms.**

Capitalization is the forcing function. A foundation that multiple independently-funded, independently-audited, independently-saleable companies stake their existence on cannot have silent fallbacks, ambiguous boundaries, or "we'll fix it later" debt. The discipline the platform already mandates is exactly the discipline that makes each entity bankable.

---

## 2. Scope and Non-Goals

### In scope (this spec)
- The four-level topology: platform realms · product realms · bounded contexts · tenant stamps.
- The governing principle: substitutability through contract-conformance (the platform is polymorphic over products).
- The sharing model: platform-only sharing; bounded-context purity across products.
- Cross-product identity direction: per-product (per-stamp).
- The constraints separate capitalization imposes: entity-grade isolation; divestiture/M&A optionality.
- Naming and assembly conventions for product realms (`{Product}.{Context}.*`).
- The CLAUDE.md §1/§5 reframe this spawns.

### Out of scope (scoped follow-ons, §11)
- **Repo / package / IP strategy** for N entities — and the platform-substrate **IP-ownership boundary** (platform-as-its-own-entity vs. shared IP vs. OSS'd-as-the-demonstration). Deferred to the build-substrate session (tracker 4.2) + the OSS thread; this spec records it as the headline open question, not a decision.
- **Per-product Shell** (each entity's stitched dashboard).
- **Energy domain decomposition** (energy contexts: enrollment, metering/usage, rates, supply, settlement, regulatory) — its own domain spec when the energy product realm is built.
- **ReferenceData product-specific reference sets** (energy LDC/ISO/RTO/NERC vs. insurance LOB/bureau codes).
- **Cross-product staff identity nuance** (one corporate Google Workspace potentially spanning entities) — an Auth follow-on.
- **Legal/corporate structuring** — cap tables, entity formation, IP licensing agreements are business/legal work; this spec only ensures the *architecture supports* them.

---

## 3. The Four-Level Topology

Each level has exactly one job. "Where does this belong?" has one right answer.

| Level | What it is | Naming | Shared? |
|---|---|---|---|
| **Platform realms** | The cosmos / shared substrate: Abstractions (declared law — contracts), Infrastructure (the rails — persistence, mediator, API, UI composition), Primitives (primitives + analyzers), ReferenceData (reference data + temporal), Warehouse, Norse Hosting (hosting, deployables, AppHost), **Auth**, **Observability**, AI, fraud, triage | top-level, codenamed, **product-agnostic** | **Shared** (code/IP) |
| **Product realms** | Separately-capitalized operating entities: the insurance MGA, the energy retailer, the logistics company, … | top-level, **codenamed for the governing figure of the vertical** (peers to each other and to platform realms) | own everything below them |
| **Bounded contexts** | A product's domain, split by ownership: `{Product}.{Context}.*` — `{InsuranceCo}.Billing.*`, `{EnergyCo}.Billing.*` | **descriptive** context names (CLAUDE.md §6 rule #5) | per-product, autonomous |
| **Tenant stamps** | Deployments. A stamp = (product × tenant) | configuration, not code | isolated per stamp |

Platform realms are product-agnostic **by construction** — nothing in `Norse.Abstractions.*`, `Norse.Infrastructure.*`, the Auth realm, etc. names or knows an MGA, an energy retailer, or any specific entity. A product realm is codenamed exactly as the first product is (it is a peer); its *internal* bounded contexts take descriptive names. Tenancy never enters the code: a tenant is a deployment stamp (stamp-per-tenant), so a product running for a given customer organization is just configuration over the product's assemblies.

---

## 4. The Governing Principle — Substitutability Through Contract-Conformance

> **`{EnergyCo}.Billing` and `{InsuranceCo}.Billing` are completely different animals. The platform neither knows nor cares.**

A product bounded context earns its place in Norse by, and *only* by:

1. **Living in Norse** — hosted as a plugin in the shared runtime (`{Context}Plugin : IWebHostPlugin` / `{Context}WorkerPlugin : IWorkerHostPlugin`).
2. **Abiding by the Æsir** — implementing Abstractions' declared contracts: the plugin interfaces, `I{Context}Api`, the repository contract family, the event/command shapes, the principal contract (`Norse.Abstractions.Identity`).
3. **Riding the Infrastructure rails** — consuming Infrastructure's persistence (per-service DbContext family, repository implementations), the source-generated mediator, the hosting runtime — rather than rolling its own.

Conform to the law and ride the rails, and a context's domain internals are nobody's business. The platform is **polymorphic over products**: `Norse.Hosting.Web.Server` loads `{InsuranceCo}.Billing`'s plugin and `{EnergyCo}.Billing`'s plugin identically, with no knowledge that one computes insurance premium and the other meters kWh. This substitutability is what lets a new entity inherit a battle-tested, audit-grade foundation on day one — which is precisely what makes it bankable (§7).

This is the same pit-of-success move the platform makes everywhere, raised to the entity level: the easy path (implement the contracts, use the rails) is the only path that compiles and runs in the host.

---

## 5. Sharing Model — Platform-Only

Products share the **platform realms and cross-cutting services only**. Each product owns its **complete** set of bounded contexts, even where names superficially repeat. `{InsuranceCo}.Customer` (an insured) and `{EnergyCo}.Customer` (a ratepayer) are different aggregates with different schemas, different invariants, different lifecycles; forcing a shared "Customer" abstraction over genuinely different domains would couple two independent companies and violate bounded-context purity. **It is not done.**

The only "business-shaped" things that cross products are the ones that are genuinely universal *and already platform realms*:
- **Auth** — identity *mechanism* (not identity data; §6).
- **ReferenceData** — universal classifications (geography, currency, units). Product-specific reference sets (energy ISO/RTO codes, insurance bureau codes) are namespaced per product (follow-on §11).

There is **no shared cross-product business-context layer.** Duplication of a *concept* (Billing exists in both) is not duplication of *code* — each is its own animal, and that autonomy is the point.

---

## 6. Cross-Product Identity — Per-Product (Per-Stamp)

**Auth is shared code, not shared identity data.** This is the stamp-per-tenant decision asked across products: single identity *mechanism* (the Auth realm — OpenIddict server, Identity stores, `IAccountApi`, the gate), isolated identity *data* per stamp, where a stamp = (product × tenant). Each stamp runs its own OpenIddict/IdP (per the auth spec). A human is a **separate principal in the insurance product and in the energy product**; nothing crosses the covers. This is consistent with the auth spec's per-stamp model and required by §7 (separate entities, separate customer data, separate compliance).

**Re-entry trigger:** portfolio SSO ("one Norse account across all entities") as a *business* requirement re-opens this — via stamp federation or a shared identity plane — and would re-introduce a product discriminator on the principal. Not built until that requirement is real.

**Open follow-on (Auth):** internal staff may live in one corporate Google Workspace that naturally spans entities, even while customer identity is strictly per-product. Resolved in an Auth follow-on, not here.

---

## 7. The Capitalization Constraints — Entity-Grade Isolation and Divestiture Optionality

Separate capitalization turns the architecture's isolation properties from preferences into **hard, fiduciary requirements**.

### 7.1 Entity-grade isolation
Distinct cap tables, distinct investors, distinct compliance and audit perimeters, distinct customer data → product boundaries are **legal boundaries**, not module boundaries. Per-product/per-stamp isolation (§5, §6) is mandatory: no cross-product data path, no cross-product identity, no cross-product entity references (the existing `YGG004` cross-context rule generalizes to cross-*product*). The Warehouse is the **only** realm permitted to read across boundaries, and any cross-product analytics it performs is a deliberate, governed exception that must respect each entity's data perimeter.

### 7.2 Divestiture / M&A optionality (new first-class constraint)
A separately-capitalized entity can be **sold, spun out, or take outside money.** The architecture must allow a product realm to be **cleanly extracted** — its `{Product}.*` code and its stamps' data — without untangling it from sibling entities. Consequences this spec asserts and the deferred repo thread must honor:
- A product's code lives in **independently-ownable repositories** (no product's source is entangled in another's).
- A product depends on the platform substrate only through **versioned, separately-owned packages/contracts** — never by reaching into another product.
- A stamp's data is self-contained (already true under stamp-per-tenant), so an entity's customer data travels with the entity.
- The platform substrate is a **distinct, separately-ownable IP layer** beneath all entities (the IP-ownership model itself — own-entity / shared / OSS — is the headline open question, §11).

These constraints **point the deferred repo-strategy decision hard toward independently-ownable per-product repositories on a shared, separately-versioned platform** — but the mechanics (submodule vs. package, CI reference switching, CPM authority) remain the build-substrate session's hands-dirty call, not an ivory-tower ruling here.

---

## 8. Naming and Assembly Conventions

- **Product realms are top-level codenamed namespaces**, peers to each other and to the platform realms. Every product realm is codenamed the same way — it is a fundable entity, the highest unit of the portfolio. (`docs/codenames.md` carries the founding ventures as **product realms**; the registry distinguishes them from platform-service codenames.)
- **Bounded contexts inside a product** follow the existing per-context project shape under the product namespace: `{Product}.{Context}.{Contracts | Components | Backend | Server | Worker | Migrations}` — `{EnergyCo}.Billing.Contracts`, `{EnergyCo}.Billing.Server`, etc. The project-shape law (`.Backend` iff `.Server` + `.Worker` share server-side state; the hard walls; the deployable rules) applies identically — it is platform law, product-agnostic.
- **Platform realms remain product-agnostic** and are never namespaced under a product.
- **The Shell is per-product** (each entity has its own stitched dashboard): `{InsuranceCo}.Shell.Components`, and a future `{EnergyCo}.Shell.Components`. (Exact treatment is a follow-on, §11 — whether the Shell is a per-product codename or a platform UI-composition pattern instantiated per product.)
- **Deployables** (`Norse.Hosting.Web.Server`, `Norse.Hosting.Worker`, `Norse.Hosting.Migrations.Service`, the clients) are platform-realm and host whichever product's plugins a given stamp is built from. A stamp is "Norse.Hosting.Web.Server + the product's plugins + that tenant's configuration."

---

## 9. Worked Illustration — `{InsuranceCo}` vs. `{EnergyCo}`

| | **`{InsuranceCo}`** (insurance MGA) | **`{EnergyCo}`** (deregulated energy retail) |
|---|---|---|
| Entity | Separately capitalized | Separately capitalized |
| Example contexts | Product, Distribution, Underwriting, Policy, Billing, Customer, Claims, Reporting | (illustrative) Enrollment, Metering/Usage, Rates, Supply, Settlement, Billing, Customer, Regulatory |
| `Billing` means | Premium recognition, agency-bill vs. direct-bill, receivables | kWh/therm usage billing, rate-ready vs. bill-ready, dual vs. consolidated bill |
| Channel partner | Producer (agency/broker) | Energy broker / aggregator — both are the generic `Producer` population in Auth |
| Rides the same | Abstractions contracts, Infrastructure rails, Auth, Observability, ReferenceData geography/currency, Norse Hosting | identical |
| Shares with the other | **Nothing below the platform line.** `{InsuranceCo}.Billing` ≠ `{EnergyCo}.Billing` — different animals | — |

The platform table is identical for both; the domain tables share not one line of code. That gap *is* the design.

---

## 10. CLAUDE.md Amendments This Spawns (you apply)

- **§1 (Project Overview)** — reframe: "Yggdrasil is the technology platform for the first product" → "Yggdrasil is a multi-product platform — a company-of-companies — whose product realms (the insurance MGA; deregulated energy retail; and more) are each separately-capitalized operating entities sharing one substrate. The insurance MGA is the first product, not the platform's reason." Add the four-level topology and the substitutability principle.
- **§5 (Naming)** — generalize `{InsuranceCo}.*` → `{Product}.*`; the realm table gains the **platform-realm vs. product-realm** distinction; `{Product}.{Context}.*` stated as the per-context pattern under any product realm.
- **§6 / `docs/codenames.md`** — updated 2026-06-07: the insurance, energy, **and logistics** ventures marked as **product realms**; product-realm-vs-platform-service distinction noted; the **governing-figure naming principle** added as rule #6; the benched name assigned to the logistics realm **vacated its Reserved messaging slot** (→ **Ratatoskr**). CLAUDE.md §1 (multi-product reframe + realm table + governing-figure framing), §3 (Heimdall=auth/Gjallarhorn=observability fix), §5 (`{Product}.*` generalization + platform-vs-product distinction), §6 (registry + rule #6) **applied 2026-06-07**.
- **Carried debt (from the Heimdall auth session) — DISCHARGED 2026-06-07.** The full `docs/` sweep ran: `decomposition.md` (cross-cutting table → Heimdall=auth + Gjallarhorn=observability rows), CLAUDE.md §3, messaging-foundation §8.3, tenancy-model §fleet-observability, performance-posture (3 refs) all moved Heimdall→Gjallarhorn for observability. The notifications-hub refs (auth spec ×3, reconciliation tracker 4.1) moved to Ratatoskr in the same sweep (the name that previously held the slot now names the logistics product realm). No `Heimdall = observability` or stale messaging-hub references remain.

---

## 11. Follow-Ons (deferred, in rough order)

1. **Repo / package / IP strategy + the platform-IP-ownership boundary** (headline open question). Coordinate with the build-substrate session (tracker 4.2, `UseProjectReferences` cross-repo switching) and the OSS thread. Decide: is the substrate its own entity that licenses to the product companies, shared IP co-owned, or OSS'd-as-the-demonstration? Constrained by §7.2 (independently-ownable per-product repos, separately-versioned platform).
2. **Per-product Shell** treatment.
3. **Cross-product staff identity** (one corporate Workspace spanning entities) — Auth follow-on.
4. **ReferenceData product-specific reference sets** namespacing.
5. **Energy domain decomposition** — the energy product's bounded-context map, when the energy product realm is built (its own domain spec).
6. **Generalize `YGG004`** (cross-context reference ban) to cross-*product* in the analyzers catalog (tracker 2.10).

---

## 12. References

- CLAUDE.md §1 (reframed here), §2 (decision rules — the discipline that makes entities bankable), §3 (contexts), §5 (naming, generalized), §6 (codenames).
- `2026-06-07-auth-design.md` — Auth; per-product identity; written against this multi-product reality.
- `2026-06-03-tenancy-model-design.md` — stamp-per-tenant; the per-entity isolation mechanism.
- `2026-05-20-yggdrasil-hosting-design.md` — the plugin conformance contract; `2026-05-26-mediator-design.md`, `2026-05-21-midgard-persistence-design.md` — the rails.
- `spec-reconciliation-2026-06-04.md` §4.2 — build-substrate session (owns deferred repo mechanics).
- `docs/codenames.md` — the product realms; platform-service codenames.
