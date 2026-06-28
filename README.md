# Glitnir

> *Glitnir is the tenth; its pillars are gold, and its roof is set with silver.*
> — Grímnismál 15. The shining hall of the Edda, where every suit is settled.

![Glitnir — the shining hall, its pillars of gold and roof of silver](https://github.com/user-attachments/assets/c3658c7f-db98-4814-ae44-966c038a7536 "Glitnir — the shining hall of the design court")

*Image credit: [@norsemythologyclips](https://www.instagram.com/norsemythologyclips/) — go follow them.*

**The software industry builds the same foundation over and over, badly, once per company.** Every new venture re-implements auth, persistence, hosting, observability, and messaging from scratch — accruing tech debt and build-error tax before it writes a line of the thing that actually makes money. Glitnir is the court where we prove the alternative, and the alternative is audacious: **one rigorously-built substrate that spins up whole companies.**

Glitnir is the **design court** — where the Norse Architecture is argued to convergence before a line of production code is rendered. Code is the verdict, not the deliberation, and verdicts come last. What's on trial is a platform thesis worth betting companies on.

## The Thesis: The Namespace Is the Business Model

**The Norse Architecture is not a product. It is the substrate of a venture studio whose product is companies.**

The code splits into two identities that mirror the business model exactly:

- **`Norse.*` — the substrate.** The shared, demonstrable, separately-ownable platform. The *function* carries the meaning (`Norse.Abstractions`, `Norse.Infrastructure`, `Norse.Hosting`, …); the `Norse.` root is the brand and the tip of the cap to the mythological genesis. Drift-proof by construction — function names are compiler-enforced, so nothing load-bearing can rot.
- **`{Company}.*` — the products.** Each **product realm** is a *separately-capitalized operating entity*: its own cap table, its own investors, its own compliance perimeter. The founding verticals — a greenfield **insurance MGA** (the first product), **deregulated energy retail**, **logistics / wholesale distribution** — share nothing below the platform line. Conform to the declared contracts, ride the rails, and your domain is your own business — including what you name it.

The demonstration that *the industry has been building software wrong* **is the substrate itself.** Standing up the next fundable company is mostly its own domain code dropped onto rails that already exist. The reference implementation and the business model are the same artifact: **the discipline that makes the platform excellent is exactly what makes a new entity cheap and safe enough to capitalize on its own.**

### The proof: three Billings, zero shared code

Same context name, three different animals. The platform loads all three plugins identically and neither knows nor cares which computes premium and which meters kWh:

| Namespace | Domain | What "Billing" actually *is* |
|---|---|---|
| `{InsuranceCo}.Billing.*` | insurance | on risk day after day — **accrete earned premium** as it earns |
| `{EnergyCo}.Billing.*` | energy retail | **four utility-coordination models** — rate-ready, bill-ready, dual-bill, supplier-consolidated |
| `{LogisticsCo}.Billing.*` | logistics | bill a customer's products **ordered from all across the globe** |

Nothing below `Norse.*` is shared between the three. That gap *is* the design.

## How We Build Right

Three commitments separate this from how software usually gets made — and each is a selling point, not a slogan:

1. **Spec-first, plan-second, code-last.** A deliberate inversion of vibe coding. Contradictions are surfaced and resolved in markdown, while they cost nothing — not discovered in production. (The case law lives in `docs/{realm}/specs/` — one folder per realm, plus `docs/Platform/specs/` for decisions with no single-realm owner.)
2. **Pit of success.** The easy path is the only path that compiles. Wrong usage doesn't bind, doesn't build, doesn't run. Compile-time enforcement over runtime hope.
3. **Fail loud, fail fast, fail upstream.** No silent fallbacks. A missing rate factor is a hard error, not a quiet `1.0`. Insurance — or energy billing — silently coerced toward "no effect" is software that mispriced something real.

And one commitment about honesty: [`docs/the-crooked-path.md`](docs/the-crooked-path.md) is the public ledger of every reversal and wrong turn, including the AI co-designer's. The clean architecture is the verdict; the crooked path is the trial. Anyone shown the first without the second is being sold a myth — and selling the myth is the one failure this platform exists to refuse.

## The Cosmos: Repositories Are Lore, Namespaces Are Function

One repository per platform realm, named for the myth; every project inside named for its function. Open the org and tour the cosmos; open the `.slnx` and every project says what it does. Live at [`github.com/NorseArchitecture`](https://github.com/NorseArchitecture):

| Repository | Namespace root | Purpose |
|---|---|---|
| **Bifrost** | `Norse.Orchestration.*` | The bridge in — the .NET Aspire AppHost meta-repository; clone once, cross the bridge, and every realm is running |
| **Asgard** | `Norse.Abstractions.*` | Declared law — contracts, attribute model, plugin interfaces, mediator law |
| **Midgard** | `Norse.Infrastructure.*` | Embodied law — persistence, API, the source-generated mediator runtime, UI composition |
| **Svartalfheim** | `Norse.Primitives.*` | The forge — primitives (`Result<T>`, `Money`) and the analyzers that strike when law is broken |
| **Urdarbrunnr** | `Norse.EntityFramework.*` | The Well of Urd — entity base types, DbContext foundations, conventions, value converters, and the migrations chassis: the record of all that has become |
| **Ratatoskr** | `Norse.NServiceBus.*` | The squirrel — NServiceBus endpoint configuration, saga infrastructure, message conventions, and transport wiring; Asgard declares the messaging surface, Ratatoskr carries it |
| **Yggdrasil** | `Norse.Hosting.*` | The world tree — hosting runtimes and deployables the cosmos hangs on |
| **Himinbjorg** | `Norse.Identity.*` | Heimdall's hall — EF persistence for ASP.NET Identity and OpenIddict: entities, conventions, and migrations; sealed server-side, never referenced from WASM or MAUI |
| **Heimdall** | `Norse.Access.*` | The watchman — auth services on Himinbjorg: one access ruleset across Blazor Server, WASM, and MAUI, with admin components and the backing gRPC service |
| **Naglfar** | `Norse.DesignSystem.*` | The ship of the dead — design tokens, radii, and component primitives assembled into something seaworthy enough to carry every product UI |
| **Glitnir** | *(docs only)* | This repo — the design court and court of record |

Further realms (Observability, Warehouse, AI) each get their own lore-named repository when they land; [`docs/codenames.md`](docs/codenames.md) is the dictionary that binds every name.

## The Court

Glitnir rides inside Bifrost at `./Glitnir`, so the entire record is on disk in every development workspace: when an appeal surfaces, the verdict is reached without scouring repositories. Glitnir records **the platform** — each product venture, when born, stands up its own design court in its own perimeter, and launches its brand on its own terms. That is why no operating entity is named in this corpus: the architecture is bigger than any of its parts.

## Finding Your Way

- [`CLAUDE.md`](CLAUDE.md) — the session law: architecture principles, decision rules, anti-patterns, open questions
- `docs/{realm}/specs/` and `docs/{realm}/plans/` — the case law itself, one pair of folders per realm (`docs/Svartalfheim/`, `docs/Midgard/`, …); [`docs/Platform/specs/`](docs/Platform/specs/) holds decisions with no single-realm owner (start with the multi-product platform design and the repository topology design)
- [`docs/codenames.md`](docs/codenames.md) — the ethos⇒function dictionary (Norse only; do not mix pantheons)
- [`docs/the-crooked-path.md`](docs/the-crooked-path.md) — the reversal ledger; the part worth teaching
- [`docs/decomposition.md`](docs/decomposition.md) — bounded-context map and repository map
- [`docs/conventions.md`](docs/conventions.md) — enum and database-object law
- [`docs/project-structure.md`](docs/project-structure.md) — per-context project shapes and deployables
