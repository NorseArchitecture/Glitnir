# The Crooked Path — Reversals, Wrong Turns, and What They Taught

**Purpose.** This document exists because the platform's central claim — *that being wrong cheaply and visibly is better than being wrong expensively and silently* — is worthless if we hide the wrongness. Any successful version of Glitnir/Yggdrasil that gets shown to the world (reference implementation, talk, OSS) **must** ship this file alongside the polished result. The clean architecture is the verdict; this is the trial. The trial is the part worth teaching.

**Ground rule: literal transparency.** Every entry names what was believed, why it was wrong, how the wrongness surfaced, the correction, and the lesson. Mistakes are attributed honestly — **including the AI co-designer's** (Claude proposed several of the antipatterns below; those are marked). No misstep is too embarrassing to log. The embarrassing ones are the valuable ones.

**This is a living document.** Every future reversal gets an entry. The mechanical record of amendments lives in `spec-reconciliation-2026-06-04.md`; this is the *narrative* companion — the why and the lesson, not the punch list.

---

## The reversals

### 1. The Reserved-codename tier — naming things that did not exist yet
- **Believed:** reserve codenames ahead of need so future components don't collide or land-grab. *(Origin: Claude's suggestion; Buvy adopted it because it seemed prudent.)*
- **Wrong because:** a reservation is a *prediction* about an unbuilt thing, and predictions rot as the worldview grows.
- **Surfaced when:** both reserved names had to move — one (reserved for an outbound messaging hub) was re-judged as a product-realm name; **Glitnir** (reserved for a dispute-resolution workflow) became this repository. Two rotted predictions out of a handful of reservations.
- **Correction (2026-06-07):** killed the Reserved tier. A name is assigned only in the same change that introduces the *real* component. The "bench" survives only as a no-meaning palette of available names.
- **Lesson:** don't name what doesn't exist. Naming is a verdict, rendered after the thing is real — never a forecast.

### 2. Codenames as relocatable meaning — the Heimdall sweep
- **Believed:** a codename is a stable handle you can scatter freely through the docs.
- **Wrong because:** codenames carry *meaning that can be reassigned*, and the meaning had been restated in prose across many files. So when **Heimdall** moved from observability to auth (and observability became **Gjallarhorn**), it was a **9-file manual sweep** with real miss-risk — the exact silent-incongruence the platform forbids in code, happening in prose.
- **Surfaced when:** the reassignment forced a hand-grep across the entire `docs/` tree.
- **Correction:** drove the **ethos⇒function dictionary** — function is the stable ID (like an enum integer, never renumbered), codename is a label bound in exactly one place. A rename should touch one file.
- **Lesson:** indirection that can be reassigned makes every reference a potential stale pointer. Hold the design layer to the platform's own fail-loud, single-source-of-truth standard.

### 3. Spec-perpetual — productively wrong forever
- **Believed:** design exhaustively before writing code; specs are cheap, code is expensive.
- **Wrong because:** specs are cheap for *rework* but **not for learning** — some errors (whether the reference-data realm generalizes across industries, whether the principal model survives a second vertical, the persistence sync edges, the tenancy cost model) only surface by building. And scope *grew* — from "platform for one insurance MGA" to "substrate for N separately-capitalized companies" — while shipped-code count stayed at zero.
- **Surfaced when:** Claude's unbiased review named it — *"spec-perpetual — a comfortable place to be productively wrong forever."* Buvy's reaction: that line was why he'd already started down the POC path that morning.
- **Correction (2026-06-07):** bring domain SME conduits (energy + insurance) into the spec-design loop, and code the lowest platform levels toward a thin vertical slice that reaches a real user. Treat a broken abstraction as the deliverable.
- **Lesson:** design needs a forcing function. Building is the cheapest one. A long pre-code phase with growing scope is a warning light, not a victory lap.

### 4. The tired-mind slip — caught live (2026-06-07, late session)
- **Believed (in the moment):** "Heimdall = logistics," plus structured answers to "rename *all code* to its function" and "rename products by vertical (insurance/energy/logistics)."
- **Wrong because:** Heimdall was the auth realm, not a product vertical (the logistics realm has its own governing figure); and the two structured answers **contradicted intentions stated minutes earlier** — keep the Norse codenames, and stand up real companies under the product-realm brand names. Executing them would have deleted the codename system and thrown away the product brands.
- **Surfaced when:** the fail-loud check flagged the contradictions before any edit; Buvy caught his own slip — *"tired mind already proving your point."*
- **Correction:** held all edits, surfaced the conflict, confirmed the corrected scope before cutting.
- **Lesson:** **fail-loud beats a tired operator.** The entire value of a drift-check is the 11 p.m. mistake it refuses to let through. This is the live demo of why the discipline exists — keep it in the talk verbatim.

### 5. The principal envelope inverted the dependency graph (ruling 1.2, 2026-06-04)
- **Believed:** the identity principal type lives in the auth context's contracts assembly.
- **Wrong because:** platform realms (host middleware, audit) consumed it — a platform→product reference that inverts the realm dependency graph.
- **Correction:** moved it to the platform `Asgard.Identity` tier; the auth realm still *populates* it but no longer *owns* it. Later renamed `YggdrasilPrincipal`.
- **Lesson:** where a type lives is a dependency claim. Put shared law at the tier everyone is allowed to depend on.

### 6. UI Composition broke the persistence wall (ruling 1.1, 2026-06-05)
- **Believed:** the dashboard-layout store could be EF/Postgres, read and written straight from the web tier.
- **Wrong because:** it violated the hard rule that the web tier cannot touch the system-of-record database.
- **Correction:** layouts became Mongo-as-system-of-record (a deliberate, documented inversion) — "a dashboard layout is not an insurance fact."
- **Lesson:** the wall has no exceptions for "internal" or "convenient" tables. If a rule earns an exception, the exception is designed and named, not slipped in.

### 7. "Decided" is not "permanent" — .NET target and the vector store
- **.NET 10 → .NET 11 (ruling 1.5):** reversed the runtime target to depend cleanly on near-future language features (discriminated unions, runtime-async) once the timeline made the gate free. A reversal *toward* a dependency, not away from churn.
- **pgvector → reopened (2026-06-07):** a previously-settled "pgvector for embeddings" line was reopened on threat-model grounds (minimize hands in the system-of-record database). Still under review, not yet re-ruled.
- **Lesson:** reopening a closed decision on genuinely new reasoning is health, not failure. The sin is reopening on *no* new information — or refusing to reopen in the face of it.

### 8. Norns named the bridge — essence spent on the wrong realm (2026-06-11)
- **Believed (in the moment):** the local-dev meta-repository (the Aspire AppHost) was created on GitHub as **Norns** — "the fates weave the threads" read as composition — while this dictionary still bound Norns to `Norse.ReferenceData`.
- **Wrong because:** two failures at once. The repo name silently reassigned a codename the dictionary had bound elsewhere — the exact stale-pointer drift rule #6 ("update the dictionary in the same change") exists to prevent — and it spent the fates' actual essence, *time* (Urd/Verdandi/Skuld, what-was/is/shall-be), on a component with no temporal semantics, using only the weaker "weaving" half of the myth.
- **Surfaced when:** drafting the public org profile forced reading the dictionary next to the live repositories; the conflicting binding was caught before any code, submodule, or doc referenced the name.
- **Correction:** renamed the repo **Bifrost** — the bridge between the realms, the developer's way into the cosmos, watched by Heimdall — chosen on its own merits, not to keep Norns warm (that would be a reservation, the tier killed in #1). The rename cost a repo name and one dictionary entry, zero namespaces, because the namespace was `Norse.Orchestration.*` all along. The same session dissolved the ReferenceData realm on the company-specificity argument (loss costs are insurance's business, transit zones are logistics'), so Norns returned to the bench *unassigned* — available, not reserved.
- **Lesson:** the two-identity model paid out exactly as designed — lore can be re-judged for the price of a rename because no operational identifier ever depended on it. And essence matters in naming: a name whose strongest facet is wasted on the component is the wrong name, even when a weaker facet fits.

### 9. Speculative naming rotted again — Muninn, Gjallarhorn, and Mimir bound to nothing real (2026-07-03)
- **Believed:** the Reserved-codename tier was killed 2026-06-07 (#1) and rule #4 ("name only when the component is real") has held since. In practice, the "Platform substrate" table in `codenames.md` still carried three bindings made before that rule existed and never revisited: **Muninn** → `Norse.Warehouse`, **Gjallarhorn** → `Norse.Observability`, **Mimir** → `Norse.AI` — none of which had (or have) a repository.
- **Wrong because:** these are exactly the reservations rule #4 forbids, just grandfathered in by never being audited against the rule that superseded them. The dictionary's own "In the ether" and "bench" sections modeled the discipline correctly (Tyr, Valkyrie, and the bench palette all carry no committed meaning); the "Platform substrate" table quietly didn't.
- **Surfaced when:** a real reference-data realm landed today and independently earned the name **Mimir** — beheaded, carried, consulted for counsel — for the serving layer on **Mimisbrunnr** (the well of wisdom). The repository was created and submoduled before anyone checked the dictionary and found `Mimir` already sitting against `Norse.AI`, a component that has never existed.
- **Correction:** `Mimir` reassigned to the real component (`Norse.ReferenceData.Components`/`.Web.Server`/`.Worker`) — it earned the name honestly and the repo already existed, so the realm keeps it. `Norse.AI` loses its premature binding entirely, no replacement reserved. `Muninn` and `Gjallarhorn` return to the bench, unreserved, same treatment Norns got in #8. `Norse.Observability` and `Norse.Warehouse` stay unnamed until they're real.
- **Lesson:** a rule adopted going forward doesn't retroactively clean up what came before it. Rule #4 killed new speculative reservations in 2026-06-07; it took a real collision on 2026-07-03 to notice three old ones had survived the rule anyway. A discipline needs an audit pass against its own back catalog, not just enforcement on new entries.

---

## Meta-lessons (the patterns under the entries)

1. **Speculative naming rots.** Name the real, never the forecast.
2. **Reassignable meaning is a stale-pointer factory.** Bind meaning once; reference the stable thing.
3. **Spec-first without a forcing function decays into spec-perpetual.** Ship a slice.
4. **Fail-loud is most valuable exactly when the human is least reliable** (tired, rushed, certain).
5. **Where a thing lives is a claim about who may depend on it.** Tier mistakes are dependency mistakes.
6. **Walls with quiet exceptions aren't walls.** Design the exception or don't have one.
7. **The AI got things wrong too.** A co-designer that never appears in the error log is a co-designer the error log is lying about.

> The architecture is what we got right. This file is how we got there. Anyone shown the first without the second is being sold a myth — and selling the myth is the one failure this whole platform exists to refuse.
