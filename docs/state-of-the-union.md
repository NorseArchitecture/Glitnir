# State of the Union

**Last reconciled:** 2026-08-04
**Audience:** agents joining or resuming work on the Norse Architecture
**Status:** living orientation document; verify its links before treating a detail as current

## Why this exists

Norse is shared platform substrate for separately operated products. The first intended
product is a greenfield insurance MGA; energy retail and logistics are also anticipated,
but they do not share product-domain code. The platform exists to make the correct
implementation path cheap, explicit, and hard to misuse: strongly typed primitives,
strict assembly boundaries, generated wiring, database-enforced invariants, and local
composition that can be cloned and run.

This is an agent handoff, not a roadmap and not a promise to outside parties. It gives
the present shape of the work, the rulings that should not be casually reopened, and the
frontiers where judgment is still required. Detailed rationale belongs in the linked
Glitnir record.

The short version: the substrate is no longer a collection of aspirations. Its core
contracts, persistence chassis, migration path, local Aspire composition, reference-data
vertical, hosting runtime, wire/serialization laws, and the first PII/erasure seams have
all been exercised across repository boundaries. What is not yet underway is the first
insurance product's bounded contexts. The next work should convert this strong platform
floor into the few cross-cutting operational capabilities a product actually needs,
rather than inventing product features ahead of the product decisions.

## The posture of the platform

### What is real

- A local developer can run Bifrost's Aspire AppHost and receive real identity and
  reference-data database composition; the migrations framework was proven end-to-end
  with `norse_identity`, then reused for `norse_reference`.
- The platform's law/hammer split is tangible: Asgard declares contracts, Svartalfheim
  makes forbidden shapes fail at build time, Midgard supplies runtime implementations,
  Urdarbrunnr owns persistence mechanisms, and Yggdrasil composes deployables.
- Transport-neutral request handling is deliberately small and owned: server-only
  `ISender`/request/behavior abstractions, a hand-rolled behavior fold, generated
  registration and gRPC wiring. There is no MediatR or martinothamar/Mediator beneath it.
- PostgreSQL is the OLTP direction. Entity-local `jsonb` views are the operational
  read-model posture; concrete well DbContexts are generated and file-scoped. MongoDB is
  not the system of record or the identity store.
- PII is now a platform concern rather than a convention: typed PII primitives,
  retention-policy analysis, an erased outcome, subject-key contracts, a development key
  store, protected EF conversion, and wire masking have landed across the substrate.

### What that does *not* mean

- There is no insurance product context in active construction yet. Product,
  Distribution, Underwriting, Policy, Billing, Customer, Claims, and Reporting remain a
  deliberately decomposed map, not implemented services.
- Messaging has a ratified NServiceBus/RabbitMQ direction but Ratatoskr is still a bare
  shell; no one should imply that an operational outbox/event spine has shipped.
- Observability has a Layer 0 design and an existing Aspire dashboard, but the exporter,
  collector, dashboards, retention, and alerting jurisdiction are deferred.
- The identity erasure seam is a significant foundation, not an assertion that every
  subject-erasure workflow or retention register has been delivered for a product.

## Settled architecture: preserve these laws

These are working constraints, not suggested styles. Read the linked source before
changing a boundary they govern.

| Law | Current ruling | Record |
| --- | --- | --- |
| Realm placement | Lore names repositories; functional names name projects and namespaces. Bifrost composes; it does not provide product/runtime features. | [decomposition](decomposition.md), [codenames](codenames.md) |
| Client/server boundary | `.Components` is client-safe. `.Server` and `.Worker` never reference one another; both meet through declared contracts/queues. A product context has only the projects its persistence stance requires. | [project structure](project-structure.md) |
| Enforced correctness | Prefer compile/build/startup enforcement to runtime convention. Svartalfheim analyzers and generated seams are part of the architecture, not optional polish. | [house rules](house-rules.md) |
| Persistence | PostgreSQL; constraints carry invariants; no service injects `DbContext` or calls `SaveChangesAsync`; generated concrete contexts remain unreferenceable. | [well and wire design](Platform/specs/2026-07-30-well-and-wire-reference-data-slice-design.md) |
| Wire and mediation | Wire DTOs are lean and data-contract-shaped; mediator law is server-only. Midgard composes validation then authorization around handlers. | [mediator design](Platform/specs/2026-07-27-mediator-pipeline-retires-gateway-design.md) |
| Identity and PII | Direct identity is isolated in Himinbjorg; crypto-shredding uses keys outside temporal storage; PII must be visible to the type system and carry an explicit retention basis. | [PII and erasure plan](Platform/plans/2026-08-03-pii-primitives-identity-erasure-seam.md) |
| Serialization | XML/JSON are boundaries with explicit laws; sensitive values must not leak through a serializer's convenient default path. | [serialization seam](Platform/specs/2026-08-03-serialization-seam-design.md) |

Two common traps deserve explicit mention. `Result<T>` and `Outcome<T>` are intentionally
different unions with different jurisdictions; do not unify them for superficial
consistency. And “a document says so” is not enforcement: when the platform can turn a
law into an analyzer, generator, type, or database constraint, that is the preferred
delivery form. See [the two unions](the-two-unions.md) and [the wolf and the judge](the-wolf-and-the-judge.md).

## Realm register

The labels below are intentionally conservative. **Shipped** means a real, merged
platform increment; it does not mean every possible consumer has adopted it.

| Realm | Status | Current role and recent meaningful state | Start here |
| --- | --- | --- | --- |
| **Bifrost** | Shipped | Local Aspire reference composition. Owns the AppHost, realm checkout, and developer resource topology; it is not a product host. | [root README](../../README.md) |
| **Glitnir** | Shipped | The design court. Specs, plans, rulings, and PoCs live here before code; this document is its current handoff layer. | [court instructions](../CLAUDE.md) |
| **Svartalfheim** | Shipped | Primitives and architecture enforcement. The 2026-08-04 PII hardening closes cyclic/inherited composition and raw-PII-on-failure gaps. | [PII design](Platform/specs/2026-08-03-pii-primitives-identity-erasure-seam-design.md) |
| **Asgard** | Shipped | Abstractions and declared law. The key-seam contracts and erased outcome crossed the ship gate on 2026-08-04. | [PII plan](Platform/plans/2026-08-03-pii-primitives-identity-erasure-seam.md) |
| **Midgard** | Shipped | Infrastructure implementations: persistence/runtime composition, mediator pipeline, UI composition, and now dev-grade subject keys plus wire masking. | [serialization seam](Platform/specs/2026-08-03-serialization-seam-design.md) |
| **Urdarbrunnr** | Shipped | Persistence mechanisms, EF foundation, migrations chassis, and the protected PII converter. It is broader than EF: `Norse.Persistence.*` is the vendor-family model. | [persistence seam](Platform/specs/2026-08-01-well-composition-dbcontext-isolation-design.md) |
| **Yggdrasil** | Shipped | Hosting deployables and runtime composition. It is consuming the latest PII-seam packages; a separate worker deployment remains deferred. | [hosting design](Yggdrasil/specs/2026-05-20-yggdrasil-hosting-design.md) |
| **Himinbjorg** | Shipped | Identity persistence for ASP.NET Identity/OpenIddict. It has the migrations proof and has been remediated for the wire/PII disclosure law. | [identity erasure plan](Platform/plans/2026-08-03-pii-primitives-identity-erasure-seam.md) |
| **Heimdall** | Designed / early shipped surface | Authentication UX and gRPC story over Himinbjorg. Its foundational slice exists, but it is not the next architectural frontier. | [auth design](Platform/specs/2026-06-07-auth-design.md) |
| **Mimisbrunnr** | Shipped | Canonical reference data, generated browser-safe primitives/namespaces, seed tooling, and migrations. | [reference-data design](Platform/specs/2026-08-01-reference-data-dependency-inversion-design.md) |
| **Mimir** | Shipped | Reference-data serving layer. Its web server was recently cleaned of the forbidden Midgard dependency; composition owns the well registration. | [reference-data design](Platform/specs/2026-08-01-reference-data-dependency-inversion-design.md) |
| **Naglfar** | Shipped | npm-first design-token pipeline plus a wholly generated .NET package. Stories do not live here. | [token design](Naglfar/specs/2026-07-09-style-dictionary-tokens-design.md) |
| **Bragi** | Early shipped surface | Content-only component-story RCL, hosted by Yggdrasil. Its useful growth follows real component surface, not speculative gallery work. | [stories design](Platform/specs/2026-07-12-designsystem-stories-hosting-design.md) |
| **Ratatoskr** | Designed / shell | Reserved messaging home. NServiceBus/RabbitMQ direction is decided, but the runtime endpoint, outbox, and event spine are not shipped. | [messaging foundation](Platform/specs/2026-06-03-messaging-foundation-design.md) |
| **Vafthrudnir** | Early exploration | Small reference-data retrieval experiment; do not treat it as a core platform dependency without a court record. | repository history |

## Work in front of us

### The immediate platform frontier

1. **Turn the PII/erasure seam into product-usable infrastructure.** The substrate has
   contracts, analysis, conversion, masking, key behavior, and error shape. The remaining
   work is careful adoption at identity and future product boundaries, accompanied by
   retention registers and a real custody-provider decision—not more abstractions for
   their own sake.
2. **Finish the reference-data vertical as a dependable first consumer.** It is the
   proving ground for generated data primitives, seeding, migrations, well composition,
   server exposure, and client-safe consumption.
3. **Choose and build only the first operational spine required by the first product
   decision.** Messaging/outbox, configuration, flags, audit, key custody, and telemetry
   are interdependent, but should be admitted in slices with a real consumer and a clear
   ownership boundary.

### Deliberately deferred or open

| Area | Status | Re-entry condition |
| --- | --- | --- |
| Product scope | Open | Decide line of business, launch states, geography/currency, portal scope, and product brand before product-context implementation. |
| Messaging/outbox | Designed | A first product flow needs durable cross-context work; do not pre-build it because “platforms have buses.” |
| Worker split | Deferred | A workload requires independent worker scaling or process isolation beyond the current single server-side deployable. |
| Observability exporter/operations | Deferred | A real deployment/operational requirement needs collector pipeline, retention, dashboards, alerting, and SLOs. |
| Analytics | Ratified direction, deferred implementation | Syn/consent and an actual product event need it. The target is server-side, cookieless, provider-seamed analytics. |
| AI / fraud / claims triage | Unplaced | Establish product-vs-platform ownership and a concrete authority model before naming or building anything. |
| Carrier bordereaux | Open | A fronting-carrier contract exists; Reporting cannot be responsibly finalized without it. |

## How to join the work safely

1. Begin at Bifrost, then read the root and relevant realm instructions. Bifrost is the
   working root; Glitnir is adjacent as the record.
2. Find the most recent linked spec/plan *and* inspect the target realm's current source
   and recent history. Plans are evidence of intent; merged code is evidence of delivery.
3. Before crossing a realm boundary, consult [decomposition](decomposition.md),
   [project structure](project-structure.md), and the applicable architecture analyzer
   rules. Do not solve a boundary violation with a new reference.
4. Treat an apparently missing abstraction as a question, not an invitation. First ask
   whether the platform intentionally keeps that concern product-sovereign, deferred, or
   unplaced.
5. For a feature, converge in Glitnir before implementation. The platform process is
   design → reviewed plan → test-driven, bounded implementation; the record is part of
   the deliverable.

## Keeping this document alive

Update this document after a cross-realm shipment, a material architectural verdict, or a
change in the active frontier. Keep the update small: change the reconciliation date,
adjust the affected realm/frontier entry, add or replace links, and remove claims that are
no longer true. Do not copy detailed design rationale here—link to its court record.

If this document is older than the surrounding realm history, use it as a map of questions
to verify, not as authority. The source of truth remains current code plus the newest
applicable Glitnir verdict.
