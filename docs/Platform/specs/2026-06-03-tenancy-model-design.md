# Tenancy Model — Stamp-Per-Tenant Decision and Design

**Date:** 2026-06-03
**Status:** Approved design, pre-implementation
**Resolves:** CLAUDE.md §7 #4 (tenancy model)

---

## 0. Context

This spec resolves the tenancy question: single tenant or multi-tenant from day one, and what shape multi-tenancy takes if it ever arrives. The question gates `Norse.Infrastructure.Persistence`'s connection-resolution strategy, the RLS posture, the entity-side tenancy contract the persistence spec deferred, and the `TenantId` slot the auth spec reserved on `NorsePrincipal`.

**The business shape that drives the decision:**

- Norse is aspirationally a platform across multiple industries (e.g., energy retail in deregulated US markets), not only insurance. That aspiration is a *guardrails-now* requirement, not a *build-now* requirement.
- The realistic client shape is **tens of B2B tenants, sales-led** — each tenant a company (an MGA, an energy retailer) landed through a sales process. Not hundreds-to-thousands of self-serve signups.
- Two deployment sizes were named up front: dedicated infrastructure carved off for large clients, and a lighter-weight, lower-cost option to spin up something compelling quickly.

**Related specs:**

- `2026-05-21-midgard-persistence-design.md` — reserved the `IConnectionResolver` slot and deferred the entity-side tenancy contract (its §12). Amended by §7 of this spec.
- `2026-05-20-auth-federation-design.md` — reserved `TenantId: Guid?` on `NorsePrincipal` (its §5). Amended by §7 of this spec.
- `2026-05-20-yggdrasil-hosting-design.md` — listed "tenancy claim handling" among deferred MGA cross-cutting middleware. Amended by §7 of this spec.
- `2026-06-03-messaging-foundation-design.md` — endpoint topology is untouched; §4 here records why no tenant headers ever appear on messages.

---

## 1. Decision Record

**Stamp-per-tenant. CLAUDE.md §7 #4 is RESOLVED.**

**Single-tenant code, multi-tenant by stamping.** A *tenant* is a deployment stamp — a complete, isolated instance of the platform. No code path in Norse Hosting, Abstractions, Infrastructure, Primitives, ReferenceData, Warehouse, or any product realm is ever aware that other tenants exist. Tenancy is a provisioning concern, not a runtime concern.

Drivers, in order of weight:

1. **Pit of success (§2.1).** Runtime tenant resolution creates a category of cross-tenant data-leak bugs — per-tenant connection caching, outbox persistence resolution, DEK cache keying, message tenant headers — every one a place a hurried developer can leak data across tenants. Stamping deletes the entire category: there is no cross-tenant code path to get wrong.
2. **Scale-to-zero economics make stamps lightweight.** Neon scales to zero; ACA with KEDA scales to zero; CloudAMQP small instances are cheap. An idle stamp costs near-nothing, so the "lightweight, fast spin-up" goal is met by provisioning economics, not by pooling. A demo tenant is a Neon branch off a seeded database — provisioned in seconds.
3. **Tens of B2B tenants never amortize pooling complexity.** Row-pooling and shared-compute pooling earn their complexity at a tenant scale (thousands, self-serve) that is explicitly not the shape.
4. **Isolation properties come free.** Blast radius, noisy-neighbor, data residency, crypto isolation, and offboarding are all solved by construction when the isolation boundary is the deployment.

Consequences:

- The persistence spec's deferred entity-side tenancy contract resolves to **neither, ever** (§3 rule 1).
- `IConnectionResolver` simplifies — the `NorsePrincipal?` parameter is dropped (§3 rule 3).
- `NorsePrincipal.TenantId` is removed, not kept reserved (§3 rule 4).
- Stamp count multiplies into NServiceBus licensing sizing (ServiceControl/ServicePulse per stamp) — feeds the already-tracked licensing business action item.

---

## 2. Approaches Considered

**A. Stamp-per-tenant (deployment-time tenancy) — chosen.** Every tenant gets a full stack instance; tenancy is an ops/automation concern. Code stays single-tenant forever. Costs: per-tenant fixed floors and fleet upgrade orchestration — a loop over a list at tens of stamps.

**B. Shared compute + database-per-tenant (runtime tenancy) — deferred, documented evolution path.** One `Norse.Hosting.Web.Server` fleet serves all tenants; `IConnectionResolver` resolves `principal.TenantId` → per-tenant Neon/Mongo connections at request/message time. Schema stays tenant-ignorant. Lowest marginal cost per tenant, but imports the runtime complexity catalog: per-tenant DbContext/connection pooling, NSB outbox persistence resolved per tenant, tenant header on every message, DEK cache keyed by tenant, OpenIddict tenant routing at login, noisy-neighbor on compute. Re-entry triggers in §6.

**C. Row-pooled with RLS (`tenant_id` on every table) — rejected permanently.** Classic SaaS pooling. Pollutes every table, index, query, and unique constraint; forces the RLS posture now; worst cross-tenant blast radius; serves a scale (thousands of tiny self-serve tenants) that is not the shape. If that scale ever arrives, Approach B reaches it without row pooling.

**The structural insight:** A and B share the same data model — tenant-ignorant schema, isolation at the database boundary, `IConnectionResolver` as the single choke point. The choice between them is deployment topology and is reversible (§6). Only C had to die today, and killing C is precisely what satisfies §7 #4's reversibility test: the data model never carries tenancy, so single-now-multi-later requires zero schema rework.

---

## 3. The Law

In the CLAUDE.md §8 anti-pattern register — build errors, PR rejections, refusal-to-write-the-code:

1. **The schema is tenant-ignorant, permanently.** No `TenantId` on `IEntity`, no `ITenantScoped` marker, no global query filter, no `tenant_id` column anywhere — OLTP, Mongo, Warehouse, audit. The persistence spec §12's deferred entity-side contract resolves to **neither, ever**.
2. **RLS is not part of the tenancy story.** Isolation is the database boundary, not a row predicate. Postgres RLS remains available for unrelated future concerns; it never spells "tenant."
3. **`IConnectionResolver` stays the single choke point** where a connection string is born — and simplifies:

   ```csharp
   namespace Norse.Infrastructure.Persistence;

   internal interface IConnectionResolver
   {
     string ResolvePostgres(string contextName);
     string ResolveMongoDatabase(string contextName);
   }
   ```

   The `NorsePrincipal?` parameter from the persistence spec's original sketch is dropped: under stamping, connection resolution is principal-independent by definition, and §2.5 forbids speculative parameters. The interface is `internal` to `Norse.Infrastructure.Persistence`, so re-adding the parameter under a future Approach B is a mechanical, contained change.
4. **`NorsePrincipal.TenantId` is removed**, not kept reserved. Each stamp has its own OpenIddict — principals never cross stamps, so a tenant claim is dead weight in every token. What "which tenant?" actually serves is fleet observability, and that is deployment metadata, not an auth claim: stamp identity ships as an OTel resource attribute (`norse.stamp=<slug>`) set once in `Norse.Hosting.ServiceDefaults`, flowing automatically through every log, metric, and trace the observability platform (`Norse.Observability`) aggregates across the fleet.
5. **Nothing outside `Norse.Infrastructure.Persistence` may assume "there is exactly one database."** Already true via the repository inversion; restated here because it is the load-bearing guardrail that keeps Approach B reachable.
6. **Verticals are not tenants.** An energy-retail platform is a sibling realm to the insurance product — its own bounded contexts on the same Norse/Abstractions/Infrastructure/Primitives substrate. The realm architecture already paid for verticalization; tenancy machinery contributes nothing to it. Platform realms stay brand- and industry-ignorant (already §5 law).
7. **Dedicated carve-off and lightweight spin-up are the same mechanism at different sizes.** A large client's dedicated infrastructure is a stamp with raised resource floors; a demo is a stamp with everything scaled to zero and a Neon branch off a seeded database. One model, one provisioning path, sized per deal.

---

## 4. Stamp Anatomy

One stamp = one tenant = one isolated set of:

| Resource | Per-stamp shape | Notes |
|---|---|---|
| **Compute** | `Norse.Hosting.Web.Server` (+ optional `WorkerHost`) replicas in ACA, KEDA scale-to-zero | Demo/idle stamps cost ~nothing; dedicated clients raise the floors |
| **Postgres** | One Neon project per stamp | Per-context databases/schemas *inside* the project remain Infrastructure's deployment-time isolation choice, unchanged. Demo stamps branch off a seeded parent project |
| **Mongo** | Per-stamp database set on a shared cluster, or per-stamp cluster | Same deployment-time sizing call as Postgres; `IConnectionResolver` resolves per context per stamp, exactly as today |
| **RabbitMQ** | Per-stamp CloudAMQP instance (preferred) or vhost | Endpoint names (`{company}.{context}`) are stamp-relative — vhost/instance isolation means **no tenant headers on messages, ever**. The messaging spec's logical topology is untouched |
| **Auth** | Per-stamp OpenIddict | Each stamp is its own IdP. Staff federation (Google Workspace) is per-stamp config — a white-label client federates to *their* Workspace by configuration, not code |
| **Keys** | Per-stamp KEK in Key Vault; per-customer DEKs under it | Crypto isolation between tenants for free; tenant offboarding destroys the stamp KEK — crypto-shredding scales up a level. Per-customer DEKs (PII spec) stand unchanged within each stamp |
| **Config** | Per-stamp App Configuration store (or label) | The config root is the stamp boundary — already how everything binds |
| **ServiceControl / ServicePulse** | Per-stamp | Stamp count multiplies into NSB licensing sizing — tracked business action item |

---

## 5. Fleet Concerns (Deferred)

Acknowledged and deliberately deferred to an operations spec when tenant #2 is real:

- **Provisioning automation** ("the stamper") — Bicep/Terraform plus a seeding script. Earns its own spec and possibly a codename when it exists; not assigned now per CLAUDE.md §6.
- **Upgrade orchestration** — `Norse.Hosting.Migrations.Service` runs per stamp; rolling fleet upgrades are a loop over a list at tens of stamps.
- **Fleet observability** — the `norse.stamp` OTel resource attribute (§3 rule 4) is the aggregation key Observability uses across stamps. Cross-stamp dashboards are Observability territory.

---

## 6. Re-Entry Triggers and Reversibility

Re-entry triggers — any of these reopens the deployment-topology choice toward Approach B:

1. Tenant count where per-stamp cost floors dominate total spend (~low hundreds).
2. Demand for self-serve / instant signup where provisioning latency matters.
3. Fleet upgrade burden exceeding operational tolerance.

Reversibility verification: Approach B shares Approach A's data model (tenant-ignorant schema, isolation at the database boundary). Re-entry cost is contained to:

- `IConnectionResolver` — re-add the principal parameter (`internal` interface, mechanical).
- `NorsePrincipal` — re-add `TenantId` (additive record member).
- OpenIddict — tenant routing at login (new work, contained to Auth).

No schema rework under any path — which is exactly the test CLAUDE.md §7 #4 set: "single-now-multi-later is acceptable only if data model and RLS posture allow it without a rewrite." They do, because the data model never carries tenancy and the RLS posture is "never for tenancy."

---

## 7. Amendments to Existing Documents

All applied in the same change set as this spec:

- **CLAUDE.md** — §7 #4 → RESOLVED, pointing at this spec. §4 gains a Tenancy subsection. §4 → Persistence loses the "per-tenant isolation is a connection-string-resolution strategy" line in favor of the stamp model. §4 → Hosting and §4 → PII updated (no tenancy in MGA cross-cutting; per-stamp KEK above per-customer DEKs). §8 → Persistence gains the "No tenancy in the schema" anti-pattern.
- **Persistence spec §12** — rewritten: entity-side contract resolves to "neither, ever"; the global-query-filter plan is deleted; `IConnectionResolver` loses the `NorsePrincipal?` parameter; `SingleTenantConnectionResolver` renamed `ConfigurationConnectionResolver`; realm-placement note, out-of-scope list, §11.2, decision-list items 16/19, and open-question #1 updated to point here.
- **Auth spec** — `TenantId` removed from `NorsePrincipal` (§5); out-of-scope, re-entry, and §10 notes updated: shared-compute re-entry re-adds the claim slot per §6 of this spec.
- **Hosting spec** — "tenancy claim handling" mentions in the realm-placement notes, out-of-scope list, and §14 #1 marked resolved-N/A: there is no tenancy claim under stamping.
- **Architecture-analyzers spec** — the two "(tenancy, audit, `NorsePrincipal` flow)" middleware mentions drop tenancy.
- **Auth-foundation plan** (unexecuted) — `ClaimNames.TenantId` (`nrs:tenant`), the `NorsePrincipal.TenantId` property/constructor parameter, factory mapping, and the corresponding tests removed from the code listings; amendment note added at top.
- **Norse-hosting plan** (unexecuted) — same middleware-list correction as the hosting spec.

---

## 8. Open Questions / Future Work

1. **The provisioning stamp tooling** — owns its own operations spec when tenant #2 (or the first demo stamp) is real. Codename assignment happens then, per §6 rules.
2. **Per-stamp Azure topology** — one ACA environment per stamp vs. shared environment with per-stamp apps; one App Configuration store vs. labels. Deployment-time sizing calls; no architectural impact (the config root is the stamp boundary either way).
3. **Cross-stamp aggregation for the platform operator** — Observability dashboards keyed on `norse.stamp` cover observability; whether any *business* cross-stamp reporting (e.g., platform-wide book analytics) ever exists is a business question. If it does, it is an explicit export per stamp, never a shared database.
