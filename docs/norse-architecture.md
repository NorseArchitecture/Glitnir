# Norse Architecture — Positioning Synthesis

*The most important takeaway from the 2026-06-07 session. This is the seed for the public README / talk narrative — the brand story that sits on top of the rigor.*

## The thesis, in one line

**The namespace is the business model.** The code splits into two identities that mirror the company-of-companies exactly: one shared, branded substrate (`Norse.*`) and many sovereign product companies (`{Company}.*`) that ride it.

## Two namespace identities

### `Norse.*` — the substrate. "The Norse Architecture."
The shared, demonstrable, separately-ownable reference platform. The **function** carries the meaning; the `Norse.` root is the brand and the tip of the cap to the mythological genesis. Drift-proof by construction — function names are compiler-enforced, so nothing load-bearing can rot.

`Norse.Abstractions` · `Norse.Primitives` · `Norse.Infrastructure` · `Norse.Hosting` · `Norse.Orchestration` · `Norse.Auth` · `Norse.Observability` · `Norse.Warehouse` · `Norse.AI` · `Norse.Notifications`

The codenames — Heimdall, Mimir, Asgard, the Aesir, the nine realms — live **entirely in the docs** as the origin story: hype at the front door, the how/why for anyone who reaches in. *Mythology markets; functions operate; docs explain.* Three jobs, never crossed.

(`Norse.` is a brand/vendor root, not a tier — the realms stay peers within it, exactly as CLAUDE.md §5 requires. It reads like `Microsoft.Extensions.*`: one identity, sibling concerns beneath.)

### `{Company}.*` — the products. Sovereign companies riding the substrate.
Each separately-capitalized entity owns its own root and its own internals. The platform **suggests** `{Company}.{Context}.*` but cannot and does not dictate — conform to the `Norse.Abstractions` contracts and ride the rails, and your domain code is nobody's business, including what you name it.

## The proof: three Billings, zero shared code

Same context name. Three completely different animals. The platform loads all three plugins identically and neither knows nor cares which computes premium and which meters kWh:

| Namespace | Domain | What "Billing" actually *is* |
|---|---|---|
| `{InsuranceCo}.Billing.*` | insurance | on risk day after day — **accrete earned premium** as it earns |
| `{EnergyCo}.Billing.*` | energy retail | **four utility-coordination models** — rate-ready, bill-ready, dual-bill, supplier-consolidated |
| `{LogisticsCo}.Billing.*` | logistics | bill a customer's products **ordered from all across the globe** |

Billing is the chosen proof precisely because it is **the most radically divergent domain across verticals** — the place where the "different animals" claim is least deniable. The four energy models alone have no analog in insurance or logistics:

- **Rate-ready** — the supplier sends the utility its rate; the utility bills it.
- **Bill-ready** — the supplier sends the utility its invoice lines; the utility bills them.
- **Dual bill** — supplier and utility each bill the customer independently.
- **Supplier consolidated** — the utility sends the supplier its line items, and the supplier bills for both.

Nothing below `Norse.*` is shared between the three. That gap *is* the design.

## Why this is the takeaway

1. **The business model is now literal in the code.** The substrate (`Norse.*`) is a separable, brandable, ownable thing — distinct from the companies (`{Company}.*`) that ride it. That directly serves the IP-ownership / divestiture thesis: OSS the substrate, license it, or spin out a company, without untangling one from another.
2. **It gives the reference implementation a name to sell:** *Norse Architecture.* The talk, the OSS release, the "the world builds software wrong — here's the right way" pitch finally has a brand.
3. **It ends the drift war permanently.** Function = operational truth (can't drift). `Norse.` = brand (can't rot — it's a prefix and a story). `{Company}.` = sovereignty (the platform doesn't care). Every layer is visible in the namespace, and not one carries meaning that can go stale — the exact failure mode that cost a 9-file sweep earlier the same day (`the-crooked-path.md`).
4. **The marketing funnel is built in.** The mythology hooks you; the function names mean something the instant you read them; the docs reward the curious. Hype at the surface, rigor underneath — which is the whole pitch.

> `Norse.*` is the cathedral anyone can tour. `{Company}.*` is what each tenant builds inside it. The mythology is why they walk in; the functions are why they stay.
