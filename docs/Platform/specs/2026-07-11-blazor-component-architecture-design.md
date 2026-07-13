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

---

## Addendum (2026-07-12): Theme Selection Machinery — `Infrastructure.Components.Theme(.FluentUI)`

**This section amends §1.2's definition of `.Components`, and resolves half of §2.1's deferred forward-pointer to Midgard's "UI composition" charter — specifically, app-wide theme bootstrapping. The other half (the mechanism that composes N registered dashboard widgets into a rendered, user-arranged layout) remains exactly as deferred in §2.1: not designed here.** Everything else in this spec — §1.1 (FluentUI direct, no Blazorise), §1.4 (the `AuthN` rename), Asgard's headless-and-no-`.FluentUI`-sibling exception (§2.1) — stands unchanged.

### Why This Comes Up Now

A dark-theme contrast bug in Asgard's headless `Loader` (its ring uses `currentColor`, deliberately) traced back to a real gap: nothing in the platform sets an ambient theme-aware `color` anywhere, in any host, for any component — headless or FluentUI-skinned. Chasing that surfaced a second, unrelated gap: Naglfar's Style Dictionary pipeline already generates a C# seed (`FluentTokenSeed`, `AccentBaseColor`/`NeutralBaseColor`) explicitly meant to feed FluentUI Blazor's `DesignTokens` (per Naglfar's own README), but nothing anywhere references it — no `.csproj` packs it, no project consumes it. Both gaps have the same root cause: no realm has ever owned "bootstrap the theme once, at the app root." This addendum names that owner.

### Decisions

**1. Amendment to §1.2 — `.Components` may hold headless markup, not just contracts.** §1.2 originally defined `{RootWord}.Components` as strictly the gRPC contract (service interface, DTOs, validators) with all rendering confined to the paired `.FluentUI` project. That's amended: `.Components` may also contain headless, zero-third-party-library Razor markup — the same kind of thing Asgard's `Abstractions.Components` already is. The dividing line is not "does this project render," it's "does this markup reference a specific component library" (FluentUI, Blazorise, MudBlazor, whichever). Only markup that does gets a sibling project named for that library (`.Components.FluentUI` today; `.Components.Blazorise` or `.Components.MudBlazor` if the platform ever adds them, per the escape hatch already named in §1.1). This applies to Heimdall's `AuthN.Components` and Mimir's `ReferenceData.Components` once they exist. Asgard's exception in §2.1 is unaffected — it already followed this shape; the amendment is bringing the other realms' `.Components` definition in line with it, not changing Asgard.

**2. Naglfar packs `FluentTokenSeed` as a NuGet package too — same version as the npm package, same release.** A new, fully generated `.csproj` in Naglfar's `dist/csharp/` output wraps the existing `FluentTokenSeed.g.cs` and packs it as `Norse.DesignSystem.Tokens`. It ships in the same Style Dictionary build/publish step as `@norsearchitecture/design-tokens`, under the same version number — both are 1:1 derivations of the same `tokens/*.json` source and only ever change together, unlike the Naglfar/Bragi split (different repos, different toolchains, different cadences — tokens rev far less often than story content, the reverse of why that split happened). This narrows, rather than breaks, "Naglfar is npm-only, no .NET": the boundary was always about not hand-authoring C# in this repo, and a 100%-generated `.csproj` packing 100%-generated source doesn't cross it. See Documentation Consequences below — every place that currently states Naglfar ships zero .NET needs this caveat.

**3. Naglfar's CSS token output switches from `[data-theme="dark"]` to `@media (prefers-color-scheme: dark)`.** The `css/theme-variables` Style Dictionary format (`style-dictionary.config.js`) currently gates dark values behind a `[data-theme="dark"]` attribute selector — but nothing in the platform has ever set that attribute (zero references, confirmed by search). It's dead weight, and it's the wrong mechanism for the actual requirement: flip the OS/browser color-scheme preference, reload, and have every host — `Hosting.Stories.Server` and `Hosting.Web.Server` alike — follow it, with no manual toggle anywhere yet. The format changes to emit dark overrides under `@media (prefers-color-scheme: dark)` instead. This doesn't foreclose a future manual toggle: a `[data-theme]` attribute override can coexist with the media query later (media query as the default, attribute as an explicit override) without touching this decision.

**4. Midgard gains `Infrastructure.Components.Theme` and `Infrastructure.Components.Theme.FluentUI` — the first concrete slice of its "UI composition" charter.**

- **`Infrastructure.Components.Theme`** — no third-party UI-library dependency. Ships Naglfar's plain semantic CSS custom properties (text/background/border, from `color.semantic.*` in `tokens/color.json`) as a static asset, switched purely by the media query from Decision 3. This is what every headless component — Asgard's `Loader`, and any headless markup living in Heimdall's/Mimir's `.Components` per Decision 1 — implicitly depends on via `currentColor`, without ever referencing this project directly or knowing it exists.
- **`Infrastructure.Components.Theme.FluentUI`** — references `Infrastructure.Components.Theme` and the new `Norse.DesignSystem.Tokens` package (Decision 2). Wraps `builder.Services.AddFluentUIComponents()` behind `AddNorseFluentUiTheme()`, and provides `<NorseFluentDesignTheme>`, a thin wrapper around `<FluentDesignTheme Mode="DesignThemeModes.System" CustomColor="@FluentTokenSeed.AccentBaseColor" NeutralBaseColor="@FluentTokenSeed.NeutralBaseColor" StorageName="norse-fluent-theme" />`. `Mode="System"` means FluentUI's own light/dark ramp already follows OS preference with zero JS bridging required — consistent with Decision 3's media-query approach on the plain-CSS side.

Naming rationale: `.Components.FluentUI` was deliberately not reused for the FluentUI-bootstrapping project — that suffix (per Decision 1) means "renders domain markup using this library," and theme bootstrapping is a different kind of thing wearing similar clothes. `Theme` / `Theme.FluentUI` mirrors the existing `.Components` / `.Components.FluentUI` split at one layer up: a base project any consumer can use standalone, and a library-specific sibling that opts in.

**5. Both Yggdrasil hosts wire it once, at their root.** `Hosting.Stories.Client` and `Hosting.Web.Client` each take a `PackageReference` on `Infrastructure.Components.Theme.FluentUI`, call `AddNorseFluentUiTheme()` in `Program.cs`, and wrap their root component's content in `<NorseFluentDesignTheme>`. For `Hosting.Stories.Client` specifically, this is `App.razor` wrapping `<BlazingStoryApp>` — the single root both `index.html` and `iframe.html` bootstrap, so one wiring point covers both documents.

### Data Flow

`tokens/*.json` → Style Dictionary → (`@norsearchitecture/design-tokens` npm + `Norse.DesignSystem.Tokens` NuGet, one shared version) → `Infrastructure.Components.Theme` ships the plain CSS half (media-query-driven) → `Infrastructure.Components.Theme.FluentUI` reads `FluentTokenSeed` and bootstraps `<FluentDesignTheme Mode="System">` → FluentUI computes its full light/dark ramp client-side and sets ambient `color` on the page → headless components' `currentColor` resolves correctly in both themes, whether or not FluentUI is even present (the plain-CSS half works standalone).

### Explicitly Still Deferred (not decided here)

- **The dashboard-widget composition/layout mechanism** — the other half of §2.1's original forward-pointer. Still Midgard's, still not designed.
- **Bridging BlazingStory's own manual dark/light toggle to `FluentDesignTheme`'s `Mode`.** Shipping `Mode="System"` only, per Buvy's explicit call this session (fewer moving parts; OS-level toggle is sufficient for now). Not rejected — queued as the next increment once System-mode is proven, not a closed door.
- **`Abstractions.Components.FluentUI` in Asgard.** Unlike the above two, this one *is* a closed door, restated explicitly this session: Asgard stays pure BCL Razor over standard HTML, permanently, regardless of what Midgard or Yggdrasil do with FluentUI.

### Documentation Consequences (required follow-up)

| Document | What changes |
|---|---|
| `Bifrost/CLAUDE.md` (§2 naming table, Naglfar row) | "**npm-only, no .NET**" needs the Decision 2 caveat — one fully generated `.csproj` packing fully generated C#, nothing hand-authored. |
| `Naglfar/README.md` | "**Naglfar is now JS-only.**" needs the same caveat. |
| `Bragi/CLAUDE.md` | "Naglfar keeps the npm/Style Dictionary token pipeline only — no .NET at all" needs the same caveat — this line predates this addendum by hours, from the same day's Bragi split. |
| `Midgard/CLAUDE.md` §1 | Currently: "the mediator runtime, UI composition — is still a bare shell; no other specs have converged here yet." Update once `Infrastructure.Components.Theme`/`.Theme.FluentUI` land — this addendum is the converged spec for the theming slice specifically; the rest of "UI composition" (dashboard-widget composition) remains unconverged. |
| `Yggdrasil/CLAUDE.md` §1 | Add `Infrastructure.Components.Theme.FluentUI` as a dependency once `Hosting.Stories.Client`/`Hosting.Web.Client` reference it. |

### Success Criterion (addendum-specific)

- `Norse.DesignSystem.Tokens` exists as a NuGet package, versioned identically to `@norsearchitecture/design-tokens`, publishing from the same Naglfar CI step.
- Naglfar's `css/theme-variables` format emits dark overrides under `@media (prefers-color-scheme: dark)`; `[data-theme="dark"]` is no longer the only mechanism (or is removed entirely if no override hook is needed yet — an implementation-time call, not a spec-time one).
- `Infrastructure.Components.Theme` has zero `PackageReference` to any third-party component library. `Infrastructure.Components.Theme.FluentUI` is the only project in Midgard that does.
- Flipping the OS/browser color-scheme preference and reloading changes the rendered theme in both `Hosting.Stories.Server` and `Hosting.Web.Server`, with no manual toggle involved anywhere in the platform yet.
- Asgard's `Loader.razor` and `Loader.razor.css` are byte-for-byte unchanged by this work.

### Self-Review (addendum)

**Placeholder scan:** No TBDs. The `[data-theme]` removal-vs-retain question is explicitly flagged as implementation-time, not left ambiguous as a spec gap.

**Internal consistency:** Decision 1's amendment to §1.2 is stated as an amendment, not a silent contradiction — it explains exactly what changes (the dividing line moves from "does it render" to "does it reference a specific library") and confirms Asgard's existing exception is unaffected. Decision 4's naming rationale explicitly addresses why `.Components.FluentUI` wasn't reused, preventing the two different `.FluentUI`-suffixed concerns (domain rendering vs. theme bootstrapping) from being conflated.

**Scope:** Deliberately the theming slice only. "Explicitly Still Deferred" separates a permanently closed door (Asgard) from two open ones (dashboard-widget composition, toggle bridging), rather than flattening all three into one "future work" bucket.

**Ambiguity:** Decision 2 states the exact mechanism (shared version, same CI step) rather than "version them somehow." Decision 5 names the exact wiring point (`App.razor`, wrapping `<BlazingStoryApp>`) rather than "wire it into the client."

---

## Addendum 2 (2026-07-13): FluentUI Blazor v5 Post-Mortem — `FluentDesignTheme` Removed Upstream

**This is a post-mortem, not a redesign.** Buvy converted Yggdrasil's consuming components to FluentUI Blazor v5 (RC4) component-by-component ahead of this record; the fix below is the minimum change to make Addendum 1's theming mechanism compile and run again against what shipped. It does not revisit whether Addendum 1's shape is still the right one — that's explicitly deferred below, pending v5 RTM.

### Why This Comes Up Now

FluentUI Blazor v5 removed `FluentDesignTheme` and `FluentDesignSystemProvider` entirely — confirmed directly against `microsoft/fluentui-blazor`'s `dev-v5` branch at the exact commit NuGet built `5.0.0-rc.4-26180.1` from (`a6ec02a5d26b2c64c68180d8a662736b4cb18e4a`). That component was the literal mechanism Addendum 1's Decision 4 named (`<FluentDesignTheme Mode="DesignThemeModes.System" CustomColor="..." NeutralBaseColor="..." StorageName="..." />`). Theming in v5 is JS-interop/CSS-variable-based, not component-based — the platform's chosen mechanism didn't survive the library's own major-version rewrite (a from-scratch rebuild on an orphaned `dev-v5` branch history, not an incremental refactor of v4).

### What Changed

**1. `NorseFluentDesignTheme`'s internals, not its contract.** The component still exists, is still named `NorseFluentDesignTheme`, and every existing call site (`Hosting.Web.Server/Components/App.razor`, `Hosting.Stories.Client/App.razor`) still just drops `<NorseFluentDesignTheme />` — zero consumer-facing change. What changed is what's inside it: no more markup. It injects `IThemeService` (v5's JS-interop-backed theme API, auto-registered by `AddFluentUIComponents()`) and calls `SetThemeAsync(new ThemeSettings(FluentTokenSeed.AccentBaseColor, Mode: ThemeMode.System))` from `OnAfterRenderAsync(firstRender)` — `IThemeService` cannot be called before the circuit has rendered once, so this can't happen in `OnInitialized`/constructor the way the old declarative markup implicitly could.

**2. `<FluentProviders />` is now required, once per host root.** v5 composes `FluentDialogProvider`/`FluentToastProvider`/`FluentTooltipProvider`/`FluentKeyCodeProvider` behind a single new `FluentProviders` component that didn't exist in v4. Neither Yggdrasil host had it wired. Added as a sibling to the closing `</FluentLayout>` in `Hosting.Web.Components/Layout/MainLayout.razor` (matching the placement convention observed directly in `microsoft/fluentui-blazor`'s own reference layouts) and as a sibling to `<NorseFluentDesignTheme />` in `Hosting.Stories.Client/App.razor` (which has no `FluentLayout` shell of its own).

**3. `FluentTokenSeed.NeutralBaseColor` is now orphaned — not deleted, not silently dropped.** v5's `ThemeSettings` record takes a single `Color` and derives the entire ramp (including neutrals) algorithmically; there is no neutral-color input parameter anywhere in the v5 theme API. Naglfar still generates the constant (nothing here asked Naglfar to stop), and nothing in Midgard consumes it anymore. This is a known, named gap — see Explicitly Deferred below — not an oversight.

### Documentation Consequences (required follow-up, done in this change)

| Document | What changed |
|---|---|
| `Naglfar/src/DesignSystem.Tokens/README.md` | "Consumed by" line corrected — only `AccentBaseColor` is consumed post-v5; `NeutralBaseColor` is generated but currently unconsumed. |
| `Glitnir/docs/Naglfar/specs/2026-07-09-style-dictionary-tokens-design.md` | Own addendum added (§11) — its §5 confirmed v4-specific `FluentDesignTheme`/`DesignThemeModes` source facts that no longer hold for v5; addended rather than rewritten, per this platform's practice of recording supersession rather than erasing what was true when written. |

### Explicitly Still Deferred (not decided here)

- **A considered v5 theme integration, once FluentUI Blazor v5 reaches RTM.** This fix is mechanical — swap the guts, keep the contract, get it compiling and running again. It does not evaluate v5's fuller theme API surface (`CreateCustomThemeAsync`/`GetColorRampAsync` for exposing the computed ramp elsewhere, raw CSS-variable overrides as an alternative to the service call, per-element scoped theming via `SetThemeToElementAsync`) for whether Naglfar's token pipeline should feed more of it through — including giving `NeutralBaseColor` a real home again, or deliberately retiring it from Naglfar if v5's ramp generation makes it permanently redundant. RC-era API surface is a poor foundation for that decision; revisit against the RTM release, not another RC. Tracked as a standing project memory so it isn't lost between now and RTM.
- **Bridging BlazingStory's own manual dark/light toggle to the new `IThemeService`.** Same deferral as Addendum 1, now against a different underlying API — still not designed here.

### Success Criterion (addendum-specific)

- `Infrastructure.Components.Theme.FluentUI`, `Hosting.Web.Server`, and `Hosting.Stories.Client` all build with `0 Warning(s)`, `0 Error(s)` against `Microsoft.FluentUI.AspNetCore.Components 5.0.0-rc.4-26180.1` — verified directly, not assumed.
- No remaining reference to `FluentDesignTheme`, `FluentDesignSystemProvider`, or `DesignThemeModes` anywhere in Midgard or Yggdrasil source.
- `<FluentProviders />` present at both Yggdrasil host roots (`Hosting.Web.Components/Layout/MainLayout.razor`, `Hosting.Stories.Client/App.razor`).

### Self-Review (Addendum 2)

**Placeholder scan:** No TBDs. The RTM re-evaluation is named as a deferred decision with a concrete trigger (v5 RTM ships), not left as an open-ended "revisit later."

**Internal consistency:** This addendum amends Addendum 1's Decision 4 code sample only — Addendum 1's Decisions 1–3 and 5 (naming rationale, data flow shape, wiring-once-at-root) are unaffected; the wiring point named in Decision 5 is exactly where `<FluentProviders />` was added, not a new location.

**Scope:** Deliberately a compile-fix post-mortem, not a re-litigation of Addendum 1's mechanism choice. The orphaned `NeutralBaseColor` is named rather than quietly left to rot, and the RTM re-review is queued as the explicit place that question gets answered.
