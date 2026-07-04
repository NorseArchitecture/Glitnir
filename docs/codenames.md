# The Ethos⇒Function Dictionary

The single source of truth binding each Norse **codename** (lore) to the **function** it names in code. Referenced from `CLAUDE.md` §6.

**The model (2026-06-07 capstone — see `docs/norse-architecture.md`):**

- **Code and spec prose use the FUNCTION.** The shared platform substrate is **`Norse.{Function}`** — branded *"the Norse Architecture."* Function names are compiler-enforced and cannot drift.
- **Codenames are LORE.** Heimdall, Mimir, the Æsir, the realms live in this dictionary, the README, and — since 2026-06-11 — as the **repository names** (the marketing front door: open the org and tour the cosmos; open the solution and every project says its function). They never appear inside code as operational identifiers: namespaces, projects, and specs use the function. *Mythology markets, functions operate, docs explain.*
- **Products are sovereign.** Each separately-capitalized company rides the substrate under its own root — `{Company}.{Context}.*` — and owns its internals once it conforms to `Norse.Abstractions` and rides the rails.
- **`Norse.` is a brand/vendor root, not a tier.** The realms stay peers within it (CLAUDE.md §5), reading like `Microsoft.Extensions.*`.

Norse mythology only. **Do not mix pantheons.**

---

## Platform substrate — `Norse.{Function}`

The shared, product-agnostic cosmos. Codename = the lore; namespace = the operational truth.

| Codename (lore) | Namespace (code) | Rationale (myth → function) |
|---|---|---|
| **Asgard** | `Norse.Abstractions` | Realm of the law-giving Æsir — *declares* the law (contracts, attribute model, mediator law) that Infrastructure embodies. |
| **Midgard** | `Norse.Infrastructure` | Realm of mortals where law is *lived* — concrete persistence, mediator runtime, API, UI Composition framework. |
| **Urdarbrunnr** | `Norse.EntityFramework` | The Well of Urd at Yggdrasil's roots, where the Norns draw water to sustain the tree and carve fate into its trunk as runes — the EF Core foundation layer: entity base types, DbContext foundations, conventions, value converters, and the migrations chassis. The record of all that has become, governed by Asgard's declared law. Named 2026-06-11 — the *well* leaves the lore pool, not a Norn (see the bench note). |
| **Svartalfheim** | `Norse.Primitives` | Dwarven forge of Mjölnir — primitives + analyzers, forged in a hotter fire *below* the domain, and they *compose* the domain you later define (hence Primitives, not "Domain"). |
| **Yggdrasil** | `Norse.Hosting` | The world tree the cosmos hangs on — hosting runtimes + deployables (`Norse.Hosting.{Web.Server\|Web.Client\|App\|Worker\|Migrations.Service}`). Aspire-fixed names are kept — don't fight the ecosystem — but the `AppHost` itself lives in Bifrost as `Norse.Orchestration.AppHost`, and `ServiceDefaults` goes to Midgard if possible, here only if it carries shared runtime context across the composition runtimes, never Bifrost (ruled 2026-06-11). |
| **Bifrost** | `Norse.Orchestration` | The rainbow bridge between the realms, watched over by Heimdall — the developer's way into the cosmos. The .NET Aspire AppHost meta-repository composing every resource (services, databases, queues, configuration): clone once, cross the bridge, and every realm is running. Named 2026-06-11, taking the function originally created under the Norns repo name — see the bench note and `the-crooked-path.md`. |
| **Himinbjorg** | `Norse.Identity` | Heimdall's hall at the head of Bifrost, where the watchman keeps the record of all who may cross — EF persistence for ASP.NET Identity and OpenIddict: entities, conventions, and migrations; sealed server-side, never referenced from WASM or MAUI. |
| **Heimdall** | `Norse.Access` | The ever-watchful guardian who alone decides who may cross — auth services riding on Himinbjorg's identity record: one access ruleset across Blazor Server, WASM, and MAUI, with admin components and the backing gRPC service. |
| **Mimisbrunnr** | `Norse.ReferenceData.Data` | The well of wisdom at Yggdrasil's roots, guarded by Mimir, where Odin traded an eye for a single drink of it — entities, view models, TSV seeders (nietras Sep), and migrations for canonical reference data: ISO country/currency codes, IANA time zones. A deliberate pair with Urdarbrunnr's Well of Urd — both foundational wells; Urdarbrunnr holds the record of what has happened, Mimisbrunnr holds what is known. Named 2026-07-03. |
| **Mimir** | `Norse.ReferenceData.Components` / `.Web.Server` / `.Worker` | Beheaded in the Æsir-Vanir war, yet still carried and consulted by Odin for counsel wherever his head was taken — the serving layer on Mimisbrunnr: Blazor components, gRPC service host, and the worker that keeps reference data current. Nobody needs the well itself to get an answer; they need Mimir's head. Named 2026-07-03, reclaimed from a premature `Norse.AI` binding that violated rule #4 below (the component was never real) — see `the-crooked-path.md` #9. |
| **Hlidskjalf** | `{Company}.Shell` *(per-product)* | Odin's high seat overlooking all realms — the stitched app shell composing auth + every context's UI. Instantiated **per product** as `{Company}.Shell`, not a shared `Norse.*` assembly. |
| **Ratatoskr** | `Norse.NServiceBus` | The squirrel racing up and down Yggdrasil's trunk, carrying slander and secrets between the eagle at the crown and Níðhöggr at the roots — NServiceBus endpoint configuration, saga infrastructure, message conventions, and transport wiring. Asgard declares the messaging surface; Ratatoskr carries it. Named 2026-06-26 — carved from Midgard for the same reason Urdarbrunnr was: strong opinions, independent versioning/licensing lifecycle, and not every realm will use the same courier. |
| **Naglfar** | `Norse.DesignSystem` | The ship built from dead men's nails, captained by giants, to ferry the end of the world. Assembled from the unglamorous remnants — tokens, radii, deprecated variants — into something seaworthy enough to carry every product UI into battle. Every design system eventually gets replaced; that's a feature, not a flaw — Naglfar doesn't survive Ragnarök, but it delivers everything else there first. Standalone (no declared consumers yet). |

> **Naglfar's nomenclature is settled, its design rules are not (2026-06-19).** This hall is Forseti's — fitting, since what's recorded here is the verdict on the *name*, not a verdict on taste. The codename and namespace land in this dictionary now so the repository exists and is wired correctly; the actual design-system content (palette, type scale, component states, the rules a real design system needs) is explicitly deferred to domain experts not yet in the room. Don't read this entry as design authority — it's registry, not taste.

> **The ReferenceData realm dissolved (2026-06-11).** Most reference data is company-specific — loss costs are insurance's business, transit zones are logistics' — so only the mechanism and the world itself are platform. The pieces went home: temporal contracts (`ITemporalRepository<T>`) → Asgard (`Norse.Abstractions`); implementations → Midgard (`Norse.Infrastructure`); universal geographic/world content → a thin library, named when real; vertical reference content → sovereign (`{Company}.ReferenceData.*`). Norns returned to the bench. A realm can dissolve as well as land — the dictionary records both.

## Product realms — `{Company}.{Context}.*`

Separately-capitalized operating entities. Codenamed for the **governing figure who rules the vertical** (rule #3); the codename is the intended company brand, kept as the namespace root. Internal contexts take **descriptive** names (`{Company}.Billing`, …).

**The assignments are not recorded here.** Each venture's governing figure *is* its future brand, launched on the venture's own terms — until then it lives in the venture's own design court, never in this public corpus (topology spec 2026-06-11 §2.5/§2.6). This corpus speaks of the founding verticals descriptively: **insurance** (a greenfield MGA, the first product), **deregulated energy retail**, and **logistics / wholesale distribution**.

## This repository

| Codename | What |
|---|---|
| **Glitnir** | This repo — the design court: specs, proofs of concept, plans, heard and judged before code is the verdict. The shining hall of the Edda — gold pillars, silver roof — where every suit is settled. |

## The bench — available palette, **no committed meaning**

The Reserved-with-an-intended-use tier was **killed 2026-06-07**: reserving a name for an unbuilt thing is a prediction, and predictions rot (it cost us two reassignments — Glitnir and a name re-judged as a product-realm brand; see `docs/the-crooked-path.md` #1). These names are simply *available*; a name leaves the bench only in the same change that introduces the real component it will narrate:

**Norns** · **Huginn** · **Saga** · **Bragi** · **Var** · **Idunn** · **Vidar** · **Muninn** · **Gjallarhorn** · **Tyr** · **Valkyrie**

(Bifrost left the bench 2026-06-11 for `Norse.Orchestration`. Norns returned the same day when the ReferenceData realm dissolved; Urd / Verdandi / Skuld are not individually on the bench — the three fates travel with Norns as a unit. Their what-was/is/shall-be essence makes them a natural fit for a genuinely temporal component someday, but that is an observation, not a reservation. **Urdarbrunnr** — the well, not a Norn — was named for `Norse.EntityFramework` later the same day: the well of what-was holds the record the fates read from, so the trio remains intact, benched as a unit, for whenever a genuinely temporal component makes the what-was/is/shall-be essence operational.)

(**Muninn** and **Gjallarhorn** joined the bench 2026-07-03: both had been bound to `Norse.Warehouse` and `Norse.Observability` respectively despite neither component being real — a straight violation of rule #4 caught when the same mistake, repeated for **Mimir**/`Norse.AI`, collided with a name legitimately earned by a real component. All three premature bindings are undone; Muninn and Gjallarhorn are available again, unreserved, and Mimir is spent for real — see `the-crooked-path.md` #9.)

(**Tyr** and **Valkyrie** joined the bench the same day: the former "In the ether" section bound both to a "provisional function" — fraud detection, claims triage — despite neither being real either, the same rule-#4 violation just dressed in honest "unsettled" language instead of a confident-looking table row. A name with no committed meaning and a name with a *provisional* meaning are both still names bound ahead of the thing. The section is gone; if fraud/claims-triage placement questions need tracking, they live in `decomposition.md` on their own merits, with no name attached until a real component earns one.)

> **Umbrella resolved (2026-06-07): Norse wins, flushed through the system.** "Yggdrasil" yields entirely to "Norse Architecture" for every *operational* purpose — the hosting realm is `Norse.Hosting.*`, the meta-repo is **Bifrost** (`Norse.Orchestration.*` — named 2026-06-11, after the cosmos was lifted into lore-named repositories), and the API/brand symbols functionize (`AddYggdrasilWebHost`→`AddNorseWebHost`, `YggdrasilTier`→`NorseTier`, `YggdrasilPrincipal`→`NorsePrincipal` [ruling 1.2's token updates to the new brand], `IYggdrasilWebHostBuilder`→`INorseWebHostBuilder`). **Yggdrasil survives only as pure lore** — the world tree on which the cosmos hangs, told in the README. No codename remains in any operational or realm-actor position anywhere outside this dictionary, the README, and `the-crooked-path.md`; only pure mythological narrative may remain as lore color. (The `YGG` analyzer-diagnostic prefix is a stable ID scheme like `CA`/`IDE` — its rename is a separate decision, not part of this flush.)

## Rules

1. **Norse only.** No Greek, Roman, Egyptian, or generic mythology mixing.
2. **Code uses the function; the codename is lore.** `Norse.{Function}` (platform) or `{Company}.{Context}.*` (product) in code and specs; the codename lives here, in the README, and as the **repository name** (2026-06-11) — never inside code as an operational identifier.
3. **Platform services are named for function; product realms for the governing figure of the vertical.**
4. **Name only when the component is real.** No speculative reservation — a name leaves the bench in the same change that introduces the thing it narrates.
5. **Do not codename a bounded context.** Contexts inside a product take descriptive names.
6. **Update this dictionary in the same PR** that introduces or renames a component.
7. **Product internals are sovereign.** `{Company}.{Context}.*` is the house suggestion, not a mandate; conform to `Norse.Abstractions` and ride the rails, and the naming is the company's own business.
