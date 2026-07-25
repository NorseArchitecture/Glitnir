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
- **Wrong because:** two failures at once. The repo name silently reassigned a codename the dictionary had bound elsewhere — the exact stale-pointer drift rule #6 ("update the dictionary in the same change") exists to prevent — and it spent the fates' actual essence, *time* (Urð/Verdandi/Skuld, what-was/is/shall-be), on a component with no temporal semantics, using only the weaker "weaving" half of the myth.
- **Surfaced when:** drafting the public org profile forced reading the dictionary next to the live repositories; the conflicting binding was caught before any code, submodule, or doc referenced the name.
- **Correction:** renamed the repo **Bifröst** — the bridge between the realms, the developer's way into the cosmos, watched by Heimdall — chosen on its own merits, not to keep Norns warm (that would be a reservation, the tier killed in #1). The rename cost a repo name and one dictionary entry, zero namespaces, because the namespace was `Norse.Orchestration.*` all along. The same session dissolved the ReferenceData realm on the company-specificity argument (loss costs are insurance's business, transit zones are logistics'), so Norns returned to the bench *unassigned* — available, not reserved.
- **Lesson:** the two-identity model paid out exactly as designed — lore can be re-judged for the price of a rename because no operational identifier ever depended on it. And essence matters in naming: a name whose strongest facet is wasted on the component is the wrong name, even when a weaker facet fits.

### 9. Speculative naming rotted again — Muninn, Gjallarhorn, Mímir, Tyr, and Valkyrie all bound to nothing real (2026-07-03)
- **Believed:** the Reserved-codename tier was killed 2026-06-07 (#1) and rule #4 ("name only when the component is real") has held since. In practice, `codenames.md` still carried five bindings made before that rule existed and never revisited: **Muninn** → `Norse.Warehouse`, **Gjallarhorn** → `Norse.Observability`, **Mímir** → `Norse.AI` in the "Platform substrate" table, plus **Tyr** → fraud detection and **Valkyrie** → claims triage in a separate "In the ether" table — none of the five had (or have) a repository.
- **Wrong because:** all five are exactly the reservations rule #4 forbids. The first pass on this entry believed "In the ether" was the discipline working correctly — Tyr and Valkyrie looked different from Muninn/Gjallarhorn/Mímir because they were honestly labeled "unsettled" and "provisional" instead of sitting in a table that implied they were real. Buvy called that out immediately: a name with a *provisional* meaning attached is still a name bound ahead of the thing it names — rule #4 draws no exception for honest labeling. Two failure modes, same root cause, one caught only because the other was pointed out first.
- **Surfaced when:** a real reference-data realm landed today and independently earned the name **Mímir** — beheaded, carried, consulted for counsel — for the serving layer on **Mímisbrunnr** (the well of wisdom). The repository was created and submoduled before anyone checked the dictionary and found `Mimir` already sitting against `Norse.AI`, a component that has never existed. Auditing the rest of the dictionary for the same mistake surfaced Muninn and Gjallarhorn immediately; Tyr and Valkyrie only after Buvy pushed back on the first correction as incomplete.
- **Correction:** `Mimir` reassigned to the real component (`Norse.ReferenceData.Components`/`.Web.Server`/`.Worker`) — it earned the name honestly and the repo already existed, so the realm keeps it. `Norse.AI`, `Norse.Observability`, and `Norse.Warehouse` lose their premature bindings entirely, no replacement reserved. The "In the ether" table is deleted outright, not just relabeled — a provisional function is still a function. `Muninn`, `Gjallarhorn`, `Tyr`, and `Valkyrie` all return to the bench, unreserved, same treatment Norns got in #8. If the fraud-detection/claims-triage placement questions need tracking, they live in `decomposition.md` as unnamed open questions.
- **Lesson:** a rule adopted going forward doesn't retroactively clean up what came before it, and an audit pass can itself stop short — the first sweep fixed the blatant version of the bug and mistook the dressed-up version for compliance. When auditing against a rule, check every table for the exact violation, not just the ones that look wrong at a glance.

### 10. Keeping a README/CLAUDE.md pair in sync doesn't keep the platform in sync (2026-07-25)
- **Believed:** the boy-scout law — "the same change that alters what either describes updates both" — was enough discipline to keep documentation current. Every realm's own README/CLAUDE.md pair genuinely was kept in sync, change by change, exactly as required.
- **Wrong because:** the rule only has jurisdiction over one repo's own pair. A rename or retirement in one realm — Urðarbrunnr's `Norse.EntityFramework.*` → `Norse.Persistence.EntityFramework.*` widening, Mímisbrunnr/Mímir's `Norse.ReferenceData.*` → `Norse.Reference.*`, Heimdall's `IAuthenticationGateway`/`AuthenticationResult` retirement in favor of Asgard's generated gateway, Himinbjörg's `Identity.csproj` folding into `Identity.Web.Server` — has zero mechanism to propagate to every *other* file that names the old thing: sibling realms' own CLAUDE.md files, the org-wide `codenames.md`/`decomposition.md` dictionaries, the `.csproj` `Description` fields that ship straight into NuGet's package listing, and every Glitnir spec that was accurate the day it was written and never revisited. Two of Asgard's own package READMEs had drifted so far they described types (`NorsePrincipal`, `Population`, `IAccountApi`) that never existed in shipped code at all — not stale, never true.
- **Surfaced when:** a request to cascade one new doctrine doc (`the-two-unions.md`) into the obvious cross-references kept finding one more thing wrong every time it was checked against source instead of against other docs — a coverage floor documented as `60` when the CI workflow said `0.1`, a namespace rename logged as "staged" three weeks after it merged and published. What started as "there isn't much to learn from this session" became a 20-agent sweep across every living doc in the platform plus dated amendments to 49 historical specs.
- **Correction:** verify claims against source and git state, never against other documentation — other docs are exactly the thing that's stale. For point-in-time records (Glitnir's specs/plans) that can't be rewritten without corrupting the historical account, attach an additive, dated amendment note instead of editing the original claim.
- **Lesson:** a sync rule scoped to one repo's own pair doesn't scale past that repo's boundary. The moment a rename crosses a realm line, it needs either an active push (a lint/CI check that fails when a stale name is grepped platform-wide) or a deliberate periodic pull (an audit like this one) — passive per-repo discipline lets the gap grow invisibly, file by file, until it's forty-nine specs and a handful of NuGet descriptions wide.

### 11. The AI held the docs-only line past the point it made sense (2026-07-25, same session — Claude's own miss)
- **Believed:** absent explicit instruction otherwise, the cautious default — stay inside the narrowest reading of "sweep the docs," defer anything that looked like a bigger change (a `.csproj` edit, a line in Glitnir's own CLAUDE.md, widening a realm's namespace) — was the responsible choice.
- **Wrong because:** this platform has shipped to zero real consumers. There was no backwards-compatibility cost any of those deferrals were actually protecting against — the caution was guarding against a risk that didn't exist, while documentation actively describing deleted types as current was a live, compounding cost the whole time. Buvy had to override the same instinct four separate times in one session before the sweep reached the size the problem actually called for.
- **Surfaced when:** his own words, escalating each time caution showed up again — *"No fix the left alone on purpose segment we haven't gone live this is our sweep also,"* then *"breaking changes are wanted I want accuracy above all,"* then *"I dont want to argue past points that have been rendered invalid by the new state of the union,"* then finally *"fix everything but .superpowers/*."*
- **Correction:** widened scope each time it was asked for — eventually 43 living docs, 8 NuGet-facing `Description` fields, and amendment notes across 49 historical specs — none of which needed permission in hindsight, all of which had been held back pending it.
- **Lesson:** on a platform with no real consumers yet, "ask before fixing something verifiably wrong" is a tax with no safety it's actually buying. The default should be fix-and-disclose until a real consumer exists to make the caution pay for itself — being wrong quietly and cautiously is still being wrong, which is the whole reason this file exists.

---

## Meta-lessons (the patterns under the entries)

1. **Speculative naming rots.** Name the real, never the forecast.
2. **Reassignable meaning is a stale-pointer factory.** Bind meaning once; reference the stable thing.
3. **Spec-first without a forcing function decays into spec-perpetual.** Ship a slice.
4. **Fail-loud is most valuable exactly when the human is least reliable** (tired, rushed, certain).
5. **Where a thing lives is a claim about who may depend on it.** Tier mistakes are dependency mistakes.
6. **Walls with quiet exceptions aren't walls.** Design the exception or don't have one.
7. **The AI got things wrong too.** A co-designer that never appears in the error log is a co-designer the error log is lying about.
8. **A per-repo sync rule has no jurisdiction past its own repo.** Cross-realm drift needs an active check or a periodic audit — passive discipline alone lets it compound invisibly.
9. **Caution has to be earned by an actual risk, not assumed by default.** Before there's a real consumer to protect, "ask first" on a verifiable fix is friction wearing safety's clothes.

> The architecture is what we got right. This file is how we got there. Anyone shown the first without the second is being sold a myth — and selling the myth is the one failure this whole platform exists to refuse.
