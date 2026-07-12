# Blazor Component Architecture — FluentUI Direct, Contracts-vs-Rendering Split, Naglfar as Story Host

**Date:** 2026-07-11
**Status:** Approved design, ready for planning
**Owner:** Buvy

---

## 0. Why This Comes Up Now

Naglfar's Style Dictionary token pipeline is live (`../Naglfar/specs/2026-07-09-style-dictionary-tokens-design.md`) — it already publishes a generated C# seed (`FluentTokenSeed`) targeting FluentUI Blazor's `DesignTokens`. That's the backdrop: a Storybook-equivalent tool for Blazor (BlazingStory) surfaced as the missing DX piece, and Blazorise came up as an alternative because it lets an app swap the underlying design system (Bootstrap/Material/FluentUI/Ant) without rewriting component markup — at the cost of a paid license for non-OSS use.

This spec is not a Naglfar design-system content decision (palette, tokens, visual taste) — those remain deferred pending real design expertise per the standing note in Naglfar's own README and the `[[naglfar-design-authorship-deferred]]` memory. This is an **architecture and naming decision**: what a Blazor component library looks like as a first-class citizen of this platform, and where it lives, per realm.

---

## 1. Decisions in Force

### 1.1 FluentUI Blazor, direct — no Blazorise

The platform standardizes on FluentUI Blazor as the only component library target. Blazorise's core value — swapping the underlying design system without touching component markup — is explicitly not needed today: there is no plan to move off FluentUI, and Naglfar's token pipeline already regenerates per target if that ever changes at the styling layer.

Blazorise remains a **named, not-yet-built escape hatch** for a hypothetical future platform consumer who wants a different design system (Ant, Material, Bootstrap). Because contracts and rendering are split per realm (§1.2), adding that later is a new sibling project (e.g. `AuthN.Components.Blazorise`) — never a rename or a rewrite.

### 1.2 Contracts vs. rendering, split per realm — with one exception

Every realm that ships **domain-specific** Blazor UI (a real backing service, real behavior) follows the platform's existing `{RootWord}.{Feature}` project-naming convention (already observed in Asgard's `Abstractions.Migrations`, Midgard's `Infrastructure.Migrations`, Urdarbrunnr's `EntityFramework.*`, Himinbjörg's `Identity.*`) and gets exactly two projects:

- **`{RootWord}.Components`** — the contract: the gRPC service interface (protobuf-net.Grpc) plus request/response DTOs plus FluentValidation validators.
- **`{RootWord}.Components.FluentUI`** — the only project that renders. Razor components implementing/consuming the sibling `.Components` contracts, styled through Naglfar's tokens (`FluentTokenSeed`).

**Asgard is the one exception, and it doesn't get a `.FluentUI` sibling at all.** Asgard never has a concrete backing service — per its own already-approved structure (`../Asgard/specs/2026-06-25-asgard-project-structure-design.md`), `Abstractions.Components` is fixed at six assemblies with "no implementations live here, by design." Rather than carve a new exemption into that rule, or reach across the hard wall that keeps Midgard's concrete implementations Yggdrasil-only-wireable, Asgard's contribution here is **headless, unstyled Razor components** — pure BCL/Razor, zero third-party design-system package reference (no FluentUI, no Blazorise, nothing), zero domain logic, zero gRPC. A headless component (behavior/markup only, no skin) is the same *kind* of thing as an interface — a base every design-system-specific realm skins, not a finished visual product. This lands inside the existing `Abstractions.Components` project as new types, not a new project — see §2.1.

### 1.3 Naglfar hosts BlazingStory only — no components of its own

Naglfar gains its first-ever .NET project, `DesignSystem.Stories` (via `dotnet new blazingstorywasm`), which references `Abstractions.Components` (to preview the headless primitives directly), `AuthN.Components.FluentUI`, and `ReferenceData.Components.FluentUI` purely to catalog and preview them. Naglfar implements zero components itself.

This requires Naglfar's first `Naglfar.slnx` and `Directory.Build.props` — bootstrapping a .NET side onto what is currently a pure JS/Style Dictionary repo. The existing token/npm tooling is unaffected.

**Story tooling never seeps into what ships.** BlazingStory (and any `.stories.razor` files) live exclusively in `DesignSystem.Stories`. No `.Components.FluentUI` project in any realm takes a BlazingStory dependency or carries story files — Naglfar references the published component libraries; the reverse never happens. This is designer experience, not developer experience, and it stays out of every packaged, published artifact.

### 1.4 Heimdall renames `Norse.Access` → `Norse.AuthN`

Heimdall's charter is the authn story specifically — login, register, forgot-password, 2FA setup, recovery, reset — not authorization ("can this principal do X"), which "Access" reads as. Two candidate root words were rejected first:

- **"Access"** (the currently-documented name in `Glitnir/docs/codenames.md` and `decomposition.md`) — rejected for reading authz-first when the actual scope is authn.
- **"Principal"** — rejected because `NorsePrincipal` is already a real, existing operational type (the Yggdrasil→Norse brand-flush rename recorded in `codenames.md`); reusing it as a namespace root would be a fresh collision, not a fix to one.

**`AuthN` is the new root word.** `Norse.AuthN.*`, projects `AuthN.Components` and `AuthN.Components.FluentUI`. Because Heimdall is currently a bare shell (LICENSE only, no code), this rename costs nothing at the code level — but see §4 for the documentation updates it requires.

---

## 2. Per-Realm Breakdown

### 2.1 Asgard — `Abstractions.Components` (exists, empty) gains two new things, no new project

No new project is added to Asgard. The existing `Abstractions.Components` project (already part of its approved six-assembly structure) gains two kinds of content, kept in different places on purpose:

- **`Norse.Abstractions.Components.Primitives`** (folder `Primitives/`) — declared-law, `IMigrationContributor`-style plugin interfaces for cross-realm UI composition, starting with something in the shape of `IDashboardWidget`, so a component from any realm can register as a dashboard widget an end user arranges. No gRPC, no concrete service: Asgard only ever declares.
- **`Norse.Abstractions.Components`** (project root, flat — not nested under `Primitives/`) — headless, unstyled Razor components (e.g. a `Loader.razor`), per §1.2's exception. Deliberately flat rather than split further: anything consuming `IDashboardWidget` will also want direct access to these without extra namespace friction.

Each realm's own `.Components.FluentUI` project (Heimdall's, Mimir's) skins/wraps these same headless bases with FluentUI Blazor styling — one behavior implementation, no duplication across realms, and no duplication later if Blazorise's escape hatch (§1.1) is ever actually built.

**Explicitly not resolved here:** the mechanism that actually composes N registered widgets into a rendered, user-arranged dashboard layout. That reads as Midgard's already-declared "UI composition" charter (`../Midgard/CLAUDE.md`), not something that falls out of Asgard's interface existing — flagged as a forward-pointer for a future, separate spec, not decided in this one. Also explicitly not resolved: the service that persists a user's widget layout/preferences. That must be Yggdrasil-hosted and run exclusively — never Asgard, never any downstream realm's concern, and never Midgard either (Midgard's concrete implementations are wireable only from Yggdrasil's composition root — a hard wall this spec does not cross in either direction).

### 2.2 Heimdall — `AuthN.Components` (new) / `AuthN.Components.FluentUI` (new)

`AuthN.Components` holds the login/logout gRPC service interface, request/response DTOs, and FluentValidation validators. `AuthN.Components.FluentUI` is a lift-and-shift-and-clean of the ASP.NET Identity UI template currently sitting in Yggdrasil (login, register, forgot password, 2FA setup, recovery, reset).

Himinbjörg (`Norse.Identity.*`, EF/OpenIddict persistence, sealed server-side) is **one implementation choice behind the gRPC contract, not a hard dependency of Heimdall**. A consumer who doesn't want ASP.NET Identity at all can implement their own gRPC server against `AuthN.Components`'s contract and skip Himinbjörg entirely.

### 2.3 Mimir — `ReferenceData.Components` (new) / `ReferenceData.Components.FluentUI` (new)

Already named in Bifröst's naming table (`Norse.ReferenceData.Components` / `.Web.Server` / `.Worker`). `ReferenceData.Components` holds the gRPC service interface, request/response DTOs, and validators for querying and updating reference data (ISO country/currency codes, IANA time zones, per Mimisbrunnr's schema). `ReferenceData.Components.FluentUI` implements the actual components against that contract.

### 2.4 Naglfar — `DesignSystem.Stories` (new)

See §1.3. Token pipeline unchanged; this is purely the addition of a BlazingStory host referencing the three `.FluentUI` projects above.

---

## 3. Explicitly Out of Scope

- **Blazorise itself.** No code, no package reference, anywhere. The contracts-vs-rendering split is the only preparation being made for it.
- **The dashboard-widget user-preference persistence service.** Yggdrasil-hosted and run, per §2.1 — not designed here.
- **The UI composition / dashboard-rendering mechanism.** Flagged as likely Midgard's charter (§2.1) — not designed here.
- **Full gRPC contract shapes, request/response DTOs, validators, and component behavior for Heimdall and Mimir.** Both realms remain bare shells with their own CLAUDE.md gate ("no code before a converged spec"). This document converges the *project shape* (§1.2, §2.2, §2.3) for both; it does not converge their behavioral specs. Each still needs its own follow-on spec in `Glitnir/docs/Heimdall/` and `Glitnir/docs/Mimir/` before implementation begins.
- **Naglfar's Style Dictionary token pipeline.** Unchanged by this spec.

---

## 4. Documentation Consequences (required follow-up)

The `Norse.Access` → `Norse.AuthN` rename (§1.4) touches four documents that currently record `Norse.Access` for Heimdall, consistently:

| Document | What changes |
|---|---|
| `Glitnir/docs/codenames.md` (line 29) | `Norse.Access` → `Norse.AuthN`, description reframed around authn (login/register/forgot-password/2FA/recovery/reset) rather than "one access ruleset" |
| `Glitnir/docs/decomposition.md` (lines 29, 49) | Same rename, both table rows |
| `Bifrost/CLAUDE.md` (§2 naming table) | `Norse.Access.*` → `Norse.AuthN.*` for Heimdall |
| `Heimdall/CLAUDE.md` (header, §1) | `Norse.Access` → `Norse.AuthN` throughout |

Separately, and independent of this rename: **Bifröst's CLAUDE.md platform-vocabulary table lists a "Chamber" UI/UX frontend layer that does not exist and was never real** — confirmed directly in this session. That row should be removed from `Bifrost/CLAUDE.md` in a follow-up change; it is not a product of this design and this spec does not attempt to fix it, only records that it needs fixing.

---

## 5. Risks

**BlazingStory is preview-only** (`1.0.0-preview.70` on NuGet as of this writing, no GA release). Accepted risk: it is designer-tooling confined entirely to Naglfar's `DesignSystem.Stories` project (§1.3) and never ships downstream, so an API break or abandonment upstream has no blast radius beyond Naglfar's own DX and would not require touching any published component library.

---

## 6. Realm Responsibility Summary

| Realm | Root word | Contracts project | Rendering project | Contains gRPC? |
|---|---|---|---|---|
| Asgard | Abstractions | `Abstractions.Components` *(exists, empty — gains `Primitives/` plugin interfaces + flat headless Razor components)* | — *(no `.FluentUI` sibling; see §1.2, §2.1)* | No — plugin interfaces + headless components only |
| Heimdall | **AuthN** *(renamed from Access)* | `AuthN.Components` *(new)* | `AuthN.Components.FluentUI` *(new)* | Yes — login/register/reset/2FA |
| Mimir | ReferenceData | `ReferenceData.Components` *(new)* | `ReferenceData.Components.FluentUI` *(new)* | Yes — query/update reference data |
| Naglfar | DesignSystem | — (no contracts project) | — (no components; hosts `DesignSystem.Stories` instead) | No |

---

## 7. Success Criterion

This spec converges the **shape**, not the behavior. It is realized when:

- Bifröst's naming table, `codenames.md`, `decomposition.md`, and Heimdall's own `CLAUDE.md` all read `Norse.AuthN` consistently, with no remaining `Norse.Access` reference to Heimdall anywhere in the record.
- Naglfar has a `Naglfar.slnx`, a root `Directory.Build.props`, and a `DesignSystem.Stories` project that builds and runs, referencing (once they exist) `Abstractions.Components`, `AuthN.Components.FluentUI`, and `ReferenceData.Components.FluentUI`.
- No `.Components.FluentUI` project in any realm carries a BlazingStory package reference or a `.stories.razor` file.
- Asgard gains no new project. `Abstractions.Components` contains at least one plugin interface (`IDashboardWidget` or its eventual name) under `Norse.Abstractions.Components.Primitives`, and at least one headless Razor component at the project's root namespace — neither with any gRPC/service/third-party-design-system reference.
- Heimdall's and Mimir's own converged specs (still to be written) place their gRPC contracts in `AuthN.Components` / `ReferenceData.Components` respectively, and their component implementations in the paired `.FluentUI` project, skinning Asgard's headless components rather than reimplementing equivalent behavior — i.e., neither realm's eventual spec contradicts the project shape fixed here.

---

## Self-Review

**Placeholder scan:** No TBDs. §3 enumerates deferrals explicitly rather than leaving gaps; §4 lists exact line numbers for the doc-sync follow-up rather than a vague "update references."

**Internal consistency:** §1.2's contracts-vs-rendering split applies to Heimdall and Mimir identically (§2.2, §2.3); Asgard (§2.1) is the one explicit, named exception — no new project, headless components only — and Naglfar (§2.4) is the other named exception (no contracts project, no rendering project of its own). Both exceptions are called out rather than silently different. §1.4's naming rationale (Access rejected, Principal rejected, AuthN chosen) matches the reasoning given inline in §2.2 for what Heimdall's components actually do. Asgard's exception was deliberately resolved to avoid two worse alternatives: carving a fresh "no implementations, except this" hole in Asgard's own already-approved six-assembly design, or crossing Midgard's Yggdrasil-only hard wall for the first time — both rejected in favor of a change that needs no amendment to any other repo's already-approved spec at all.

**Scope:** Deliberately shape-only, not behavior. §3 and §7 both make this explicit: Heimdall and Mimir still owe their own converged specs before any code gets written, consistent with their existing CLAUDE.md gates. This document does not attempt to pre-empt those.

**Ambiguity:** The Heimdall rename's blast radius is spelled out as four specific documents with line numbers (§4), not left as "update the docs." The Blazorise escape hatch (§1.1) states exactly what would be added later (a sibling `.Components.Blazorise` project) rather than leaving "swap it out later" vague. Asgard's headless-vs-plugin-interface split within `Abstractions.Components` states exactly which is flat and which is nested (§2.1), and why, rather than leaving the internal folder structure to be improvised at implementation time.
