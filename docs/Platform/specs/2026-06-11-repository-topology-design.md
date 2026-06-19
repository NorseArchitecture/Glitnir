# Repository Topology and the Public Record — Bifrost, Glitnir, and the Fractal Courts

**Date:** 2026-06-11
**Status:** Approved in session 2026-06-11.
**Owner:** Buvy
**Amends:** CLAUDE.md §1 (Glitnir's charter), §5 → Repositories (the meta-repository is named and live; repo-naming ruling; clone instructions), §6 (dictionary pointers). Ratifies the `decomposition.md` Repository Map amendment of the same date. Supersedes the "Norse meta-repository is born when the specs settle" phrasing wherever it appears — the cosmos was lifted early, deliberately (validate-by-building).
**Companion specs:** `2026-06-07-multiproduct-platform-design.md` (the company-of-companies topology this repo records); `docs/codenames.md` (the dictionary; Bifrost's entry and the repo-naming model); `docs/the-crooked-path.md` #8 (the repo-name misstep that preceded Bifrost's naming).

---

## 1. Context

The GitHub organization **NorseArchitecture** is live, with one repository per platform realm — **Bifrost** (`Norse.Orchestration`, the Aspire AppHost meta-repository), **Asgard** (`Norse.Abstractions`), **Midgard** (`Norse.Infrastructure`), **Svartalfheim** (`Norse.Primitives`), **Yggdrasil** (`Norse.Hosting`) — and **Glitnir**, the design court, freshly moved into the org. All private today; the intent from this date forward is full open source.

The question heard: does Glitnir become a submodule of Bifrost so all specs from all realms live in one hall, or do specs migrate into the repositories they govern? The deciding criterion was declared up front: what works best for AI-driven development sessions, since this platform is built spec-first with an AI co-designer.

## 2. Rulings

### 2.1 Glitnir is the platform court — the full record, one hall

Glitnir records **the Norse Architecture**: the platform realms and the *generic product shape* (`{Company}.{Context}.*` law, exemplar slices, conformance contracts). Specs and plans for every platform realm live here, permanently. The decisive property: **when an appeal surfaces, the entire record is already in the courtroom** — a verdict can be reached without scouring multiple repositories and wiring up context. No discovery phase, ever.

### 2.2 Glitnir rides in Bifrost at `./Glitnir`

Bifrost carries Glitnir as a submodule (relative URL, `branch = master`), exactly as it carries the realm repos. The submodule SHA pin is embraced as the **verdict ledger**: every Bifrost bump commit records which version of the law the code answered to. Spec commits land in Glitnir; the pin advances in Bifrost. Two commits, two repos, deliberate.

### 2.3 The session rule

Development and AI sessions start at the **Bifrost workspace root** (`git clone --recurse-submodules` → everything present: code and law in one tree). All platform specs and plans land in `./Glitnir`. Standalone single-repo clones are not a supported authoring workflow (see §5.2 for the re-entry trigger).

### 2.4 Context tiering — constitution, procedure, pointers

- **Bifrost root `CLAUDE.md` is the constitution** — the cold-start law for any session in the workspace. The bulk of Glitnir's current CLAUDE.md (§1–§8: principles, decisions, naming, anti-patterns) graduates there when Bifrost's root context is authored.
- **Glitnir's `CLAUDE.md` narrows to court procedure** — how to file a spec, directory layout, the reconciliation tracker, the crooked path.
- **Each realm repo's `CLAUDE.md`** carries that realm's distilled binding rules plus relative pointers (`../Glitnir/docs/{realm}/specs/...`) valid from the workspace by construction. The contract travels with the code; the case law stays in the hall.

The rationale is mechanical, not aesthetic: session context is **push** (CLAUDE.md, loaded automatically) plus **pull** (specs, read on demand). Proximity of spec files to code buys nothing; correctness of each repo's CLAUDE.md buys everything. And the court's highest-value workflows — the spec-reconciliation ledger, supersession sweeps, codename sweeps — depend on grepping the entire body of law in one namespace. Distributing the specs would fork the ledger into N ledgers across N checkouts.

### 2.5 Fractal courts — ventures birth their own halls

Glitnir is **not** the record of the operating entities. Each venture, when born, stands up its **own private design court** — same pattern, own perimeter: its specs, its domain decomposition, its brand, its cap-table-adjacent record. Venture-specific record never lands in Glitnir; the platform court stays publishable. The vertical *as a category* (insurance, energy retail, logistics) is admissible in the platform record; the entity is not.

### 2.6 The public record is brand-clean (the obfuscation law)

The platform corpus speaks in **vertical descriptors and placeholders**, never operating-entity brands:

| Concern | Public form |
|---|---|
| Product realms in prose | "the insurance / energy / logistics product realm" |
| Example assemblies, namespaces | `{Company}.Billing.Worker`, `{Company}.Shell` |
| Queue names, endpoints | `{company}.{context}`, `{company}.{context}.web` |
| Stamp telemetry attribute | `norse.stamp` |
| The fronting carrier (insurance vertical) | "the fronting carrier" — never named |
| Carrier-internal platform vocabulary | removed entirely |
| Governing-figure naming rule | survives **abstractly** ("a product realm is named for the Norse figure whose myth rules the vertical"); the actual assignments live in each venture's own court until launch |

Brand mappings exist outside the public record until each venture launches on its own terms.

### 2.7 Fresh root — the record begins curated

Existing commit history is abandoned: the curated, brand-clean tree becomes the initial commit of the public record. The supersession trail *inside* the corpus (superseded specs with banners, the reconciliation ledger, the crooked path) preserves the intellectual history that matters; stray commit messages do not make the cut. **From the fresh root forward, the crooked path is public record** — reversals are logged in daylight as they happen. Sibling repos receive the same residue check before any of them flips public.

### 2.8 Repository = lore, namespace = function

Ratified (amended earlier today in `decomposition.md`, recorded in the dictionary): the `norse-{function}` repo-naming model is superseded. One repository per platform realm, **named for the lore**; every project and namespace inside is **named for the function**. Open the org and tour the cosmos; open the `.slnx` and every project says what it does. Repos ship no namespaces of their own, so lore names at the repo tier cannot drift into code — the two-identity model holds.

## 3. Alternatives Rejected

- **Distribute specs to governing repos; Glitnir hears only inter-realm matters.** Rejected: most of the corpus is cross-cutting (tenancy, messaging, persistence, the topology itself) and has no single home — placement would be arbitrary; cross-cutting amendments would fan out as N pull requests; the one-grep-namespace ledger workflows die; and the wins are mooted by §2.3 — in the blessed workspace, the specs are always on disk anyway.
- **Glitnir private forever, curated public mirror.** Rejected: two artifacts, permanent drift risk between record and mirror — a silent-incongruence machine, the exact failure mode the platform forbids.
- **Per-repo spec excerpts generated from Glitnir.** Rejected today as two-copies-of-the-truth. **Re-entry trigger:** the first outside contributor whose workflow genuinely requires a standalone single-realm clone.

## 4. Consequences

1. **This session:** the brand-obfuscation sweep across the full corpus (per §2.6), the README rebuilt from `docs/norse-architecture.md`, CLAUDE.md amendments (§1 charter, §5 repositories, §6 registry), dictionary updates, fresh-root preparation.
2. **At Bifrost:** add the `./Glitnir` submodule; author the root constitution CLAUDE.md (graduation per §2.4); per-realm CLAUDE.md pointer discipline as realms gain code.
3. **When the first venture is born:** instantiate its court from the Glitnir pattern; record the governing-figure assignment there, not here.
