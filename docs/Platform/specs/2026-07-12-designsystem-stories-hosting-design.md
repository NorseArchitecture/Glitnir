# DesignSystem.Stories Hosting — RCL in Naglfar, WASM Runtime in Yggdrasil, Shipped to GHCR

**Date:** 2026-07-12
**Status:** Approved design, ready for planning
**Owner:** Buvy
**Supersedes (in part):** `2026-07-11-blazor-component-architecture-design.md` §1.3, §2.4, §5, §6, and the corresponding bullet in §7 — see §5 below for the exact deltas. All other sections of that spec (FluentUI-direct, contracts-vs-rendering split, Asgard's headless-components exception, the Heimdall `AuthN` rename) are unaffected and remain in force.

---

## 0. Why This Comes Up Now

The 2026-07-11 spec landed `DesignSystem.Stories` in Naglfar as a dev-only, never-packaged BlazingStory WASM host. That was the right call to get working code on screen first — this platform had never touched BlazingStory or any Storybook-style tool before, and seeing it run end to end mattered more than getting the hosting shape right on the first pass. Now that there's something real to evaluate, the architecture correction lands deliberately early, before more gets built on top of the dev-only shape: technical and non-technical people alike need to hit a running catalog of the latest components at `stories.{company}.{tld}` without a dotnet toolchain, and — because it's driven off the same NuGet-publish machinery every other realm already uses — without anyone remembering to redeploy it by hand when a component changes. That's a hosted-service requirement, not a local-dev-tool requirement, and it changes where this thing lives.

## 1. Decisions in Force

### 1.1 Split: Naglfar owns content, Yggdrasil owns the runtime

`DesignSystem.Stories` (Naglfar) stops being a runnable WASM app and becomes a plain Razor Class Library — `.stories.razor` files, `Welcome.md` (via `MD2RazorGenerator`), and nothing else. It references `Abstractions.Components` (Asgard) directly so its stories can preview the headless primitives, and will reference `AuthN.Components.FluentUI` / `ReferenceData.Components.FluentUI` once those realms ship. It carries a `BlazingStory` package reference for the `.stories.razor` authoring API — that's a content-authoring dependency, not a hosting one.

All hosting infrastructure — the WASM bootstrap, the app shell, the production HTTP server, the container — moves to Yggdrasil as a new project pair:

- **`Hosting.Stories.Client`** (`Microsoft.NET.Sdk.BlazorWebAssembly`) — `Program.cs` (`WebAssemblyHostBuilder`), `App.razor`, `wwwroot/index.html` + `iframe.html`. This is the **only** project holding the `NorseRef` to Naglfar's `DesignSystem.Stories` — everything Naglfar references (Asgard now, Heimdall/Mimir later) rides in transitively through ordinary NuGet/MSBuild dependency resolution, no extra wiring required.
- **`Hosting.Stories.Server`** (`Microsoft.NET.Sdk.Web`) — an ordinary same-repo `ProjectReference` to `Hosting.Stories.Client`, `Microsoft.AspNetCore.Components.WebAssembly.Server` + `UseBlazorFrameworkFiles()` to serve the client's static output, `ContainerBaseImage` set to the platform's standard aspnet image. This is the deployable that gets dockerized.

**Why two projects, given this is 100% WASM with no Blazor Server render mode and no MAUI involvement:** not for cross-render-target reuse (that's the reason `Hosting.Web.Client`/`Hosting.Web.Server` exist, and it doesn't apply here). It's a hard compile-target constraint — a Blazor WASM bundle (`net10.0-browser`) and the ASP.NET Core process that serves it in production (ordinary server `TargetFramework`) cannot be the same project. `Microsoft.AspNetCore.Components.WebAssembly.DevServer`, which is what a standalone WASM project runs under `dotnet run`, is explicitly dev-only tooling and is not what gets dockerized. The `Client`/`Server` split is reused here purely because it's the mechanical shape this constraint already forces — matching the existing pair's naming avoids inventing new vocabulary for a shape this repo already has, even though the underlying motivation differs.

**Local dev:** clone Bifröst, `dotnet watch --project Yggdrasil/src/Hosting.Stories.Server` — `NorseRef` resolves to `ProjectReference` locally (Naglfar submodule present), so hot reload spans the whole graph: edit a `.stories.razor` file in Naglfar, see it reflected without restarting anything.

### 1.2 Freshness: the existing Gjallarhorn mechanism, extended to Naglfar

No new automation. Every realm's `release.yml` already "sounds Gjallarhorn" on a stable tag push: it opens (and auto-merges) a PR in Yggdrasil bumping `<{Realm}Version>` in `Directory.Packages.props`. Naglfar's `release.yml` is already wired to call it — it has simply never fired because Naglfar has never tagged a release. The only prerequisite is seeding `<NaglfarVersion>0.0.0</NaglfarVersion>` (plus the matching `<PackageVersion Include="Norse.DesignSystem.Stories" ... />` entry) into Yggdrasil's `Directory.Packages.props`, since `sound-gjallarhorn.ps1` throws if the property doesn't already exist. Once seeded, publishing a new `DesignSystem.Stories` version — or a new `AuthN.Components.FluentUI` / `ReferenceData.Components.FluentUI` version once those exist — auto-merges the bump with no manual step.

### 1.3 Shipping: extend the existing `release-container.yml`, don't build new CD

Yggdrasil's `release.yml` already calls the Ginnungagap-owned reusable workflow `release-container.yml` unconditionally. That workflow currently hardcodes three image blocks (migrations/web/worker): `dotnet publish .../t:PublishContainer` → Trivy SBOM scan → retag and push to `ghcr.io/norsearchitecture/hosting/{name}`. This spec adds a fourth block of identical shape for `Hosting.Stories.Server`, publishing `ghcr.io/norsearchitecture/hosting/stories:{version}`. This is a single-file edit in the `NorseArchitecture/.github` repo; Yggdrasil's own `release.yml` needs no change.

**Explicitly not in scope: an actual running deploy target.** The workflow's `deploy-hook` job is already a stub ("not yet implemented") for both pre-release and stable tags — it stays exactly that way. Per Buvy's call this session: package and ship the image to GHCR now; a real deploy target (Azure Container Apps environment, DNS for `stories.{company}.{tld}`, the `deploy-hook` implementation) waits until there's something worth showing and the cloud spend is justified.

## 2. Per-Realm Breakdown

### 2.1 Naglfar — `DesignSystem.Stories` (existing project, restructured)

SDK changes from `Microsoft.NET.Sdk.BlazorWebAssembly` to `Microsoft.NET.Sdk.Razor`. Removed: `Program.cs`, `App.razor`, `wwwroot/index.html`, `wwwroot/iframe.html`, `Shared/DefaultLayout.razor` (all move to `Hosting.Stories.Client`, §2.2), the `IsPackable=false` property, and the `RestoreBlazorWebAssemblyOutputType` target — none of that is needed once this is a plain RCL, which packs by the platform's existing default (the shared `src/Directory.Build.props` already assumes every project is a packable library; the removed override was the exception, not the rule). Kept: `Stories/Loader.stories.razor`, `Welcome.md` + `MD2RazorGenerator`, the `NorseRef` to `Abstractions.Components` (Asgard), and the `BlazingStory` package reference.

This is Naglfar's first tagged release — its existing `release.yml` (already calling the platform's `release-nuget.yml`) publishes `Norse.DesignSystem.Stories` to NuGet on the next `v*.*.*` tag, and that same tag push sounds Gjallarhorn per §1.2.

### 2.2 Yggdrasil — `Hosting.Stories.Client` (new) / `Hosting.Stories.Server` (new)

Both new projects under `src/`, with matching test project stubs (`Hosting.Stories.Client.Tests`, `Hosting.Stories.Server.Tests`) wired into `Yggdrasil.slnx`, per this repo's existing one-test-project-per-source-project convention. `Directory.Packages.props` gains `<NaglfarVersion>0.0.0</NaglfarVersion>` and `<PackageVersion Include="Norse.DesignSystem.Stories" Version="$(NaglfarVersion)" />`, in the same shape as every other realm already listed there.

### 2.3 Ginnungagap (`.github`) — `release-container.yml` (existing reusable workflow, extended)

One new block, mirroring the existing migrations/web/worker blocks exactly: publish `Hosting.Stories.Server` via `dotnet publish --os linux --arch x64 -c Release /t:PublishContainer`, Trivy SBOM scan (`HIGH,CRITICAL`, fails the build on findings), retag and push to `ghcr.io/norsearchitecture/hosting/stories:{version}`.

## 3. Explicitly Out of Scope

- **The actual deploy target for the running container** — registry choice is settled (GHCR), but the hosting environment, DNS for `stories.{company}.{tld}`, and the `deploy-hook` job's implementation are not. Deferred per §1.3.
- **Blazor Server render mode and any MAUI involvement for Stories.** Explicitly rejected this session — this is 100% WASM, full stop. Nothing about this spec puts BlazingStory content anywhere near the MAUI app.
- **Full behavioral content of `AuthN.Components.FluentUI` / `ReferenceData.Components.FluentUI`.** Unchanged from the 2026-07-11 spec — both realms still owe their own converged specs before implementation.
- **Splitting BlazingStory's scaffolded template mechanically.** See §6 (Risks) — this is a stated risk with a named first task, not a solved problem.

## 4. Documentation Consequences (required follow-up)

| Document | What changes |
|---|---|
| `Bifrost/CLAUDE.md` (§2 naming table, Naglfar/Yggdrasil rows) | Naglfar's row still reads "first token pipeline live" — add that `DesignSystem.Stories` (RCL) is its first .NET project. Yggdrasil's row/description should mention `Hosting.Stories.Client`/`.Server` alongside the existing deployable list. |
| `Yggdrasil/CLAUDE.md` §1 | Currently enumerates `Norse.Hosting.Web.Server`/`.Web.Client`/`.App`/`.Worker`/`.Migrations.Service` — add `.Stories.Client`/`.Stories.Server` to that list once live. |
| `Naglfar/README.md` | Currently silent on `DesignSystem.Stories` entirely (only describes the token pipeline). Add a line once the RCL restructure lands, noting Yggdrasil hosts the runnable catalog. |

## 5. Deltas to the 2026-07-11 Spec

That spec's §1.3 ("Naglfar hosts BlazingStory only — no components of its own... Story tooling never seeps into what ships... confined entirely to Naglfar's `DesignSystem.Stories` project... never ships downstream") is **superseded**: Naglfar now hosts only the story *content* as a plain RCL; the runnable BlazingStory app is Yggdrasil's, and it explicitly does ship downstream — to GHCR. §2.4 and the Naglfar row of §6's Realm Responsibility Summary table are superseded to match (Naglfar gains a contracts-shaped content project, no longer "no contracts project, no rendering project"). The risk framing in §5 ("BlazingStory is preview-only... blast radius confined entirely to Naglfar's own DX") is superseded — the blast radius now extends to Yggdrasil and GHCR, an explicitly accepted trade Buvy made this session in exchange for a zero-toolchain, always-current catalog.

**Unaffected and still true:** the §7 success-criterion bullet "No `.Components.FluentUI` project in any realm carries a BlazingStory package reference or a `.stories.razor` file" — that constraint is about `AuthN.Components.FluentUI` / `ReferenceData.Components.FluentUI` staying story-tooling-free, which this spec does not touch. Everything else in the 2026-07-11 spec (§1.1 FluentUI-direct, §1.2 contracts-vs-rendering split, §1.4 the `AuthN` rename, Asgard's headless-components exception in §2.1) stands as written.

## 6. Risks

**BlazingStory's `blazingstorywasm` scaffold was not designed to be split this way.** The template generates app shell (routing, story discovery, the sidebar/canvas UI) and content (`.stories.razor` files) as one project. This spec asserts the split is mechanically sound — story discovery should work the same whether `.stories.razor` files live in the same assembly or a referenced one, since it's ordinary reflection/routing over loaded assemblies — but that is unproven anywhere in this platform. **The first implementation task must be a spike:** move `Loader.stories.razor` into a bare RCL, reference it from a throwaway WASM host, and confirm BlazingStory's app shell discovers and renders it correctly before the rest of the restructure proceeds.

## 7. Success Criterion

This spec converges shape and CI plumbing, not deploy infrastructure. It is realized when:

- `DesignSystem.Stories` in Naglfar is `Microsoft.NET.Sdk.Razor`, contains only story/content files, and packs on tag push via the existing `release-nuget.yml` path.
- Yggdrasil has `Hosting.Stories.Client` and `Hosting.Stories.Server` projects (plus test stubs) in `Yggdrasil.slnx`; `Hosting.Stories.Server` builds a container image tagged `ghcr.io/norsearchitecture/hosting/stories:{version}` via the extended `release-container.yml`.
- `Directory.Packages.props` carries `<NaglfarVersion>` and the platform's Gjallarhorn mechanism successfully auto-merges a version bump PR the first time Naglfar tags a release — no manual step required.
- `dotnet watch --project Yggdrasil/src/Hosting.Stories.Server` from a fresh Bifröst clone produces a working, hot-reloading story browser with no additional setup.
- No deploy target, DNS, or `deploy-hook` implementation exists yet — and that absence is expected, not a gap in this spec.

---

## Self-Review

**Placeholder scan:** No TBDs. §3 and §6 name deferrals and risks explicitly rather than leaving gaps; §6 in particular states the exact spike task rather than a vague "verify it works."

**Internal consistency:** §1.1's stated reason for the `Client`/`Server` split (compile-target constraint, not cross-render-target reuse) is consistent with §3's explicit rejection of Blazor Server/MAUI involvement — the spec doesn't accidentally imply a reuse motive it then disclaims. §5's delta list matches §1.1/§1.3's decisions exactly, naming the specific superseded sub-sections rather than declaring the whole prior spec void.

**Scope:** Deliberately shape-and-CI-only. §3 and §7 both state that no deploy target exists yet and that this is intentional, consistent with Buvy's explicit call this session (package and ship to GHCR; no cloud spend until something's worth showing).

**Ambiguity:** §1.1 states which project holds the `NorseRef` (only `Hosting.Stories.Client`) rather than leaving it ambiguous. §1.2's freshness mechanism names the exact missing prerequisite (`sound-gjallarhorn.ps1` throwing without the property) rather than asserting it "should just work." §6's risk is stated as a concrete first task with a concrete verification method, not a hand-wave.

---

## Addendum (2026-07-12, same day): `DesignSystem.Stories` split out of Naglfar into Bragi

**This section supersedes §1.1's "Naglfar owns content" decision, §2.1, and the `Naglfar/README.md` row of §4.** Everything else in this spec — the `Hosting.Stories.Client`/`.Server` split and its compile-target rationale (§1.1), the Gjallarhorn freshness mechanism (§1.2), the GHCR shipping path (§1.3), and the deltas to the 2026-07-11 spec (§5) — stands unchanged. Only *which repo* holds `DesignSystem.Stories` changes.

**Decision:** `DesignSystem.Stories` moves out of Naglfar into a new repo, **Bragi** (`Norse.DesignSystem.Stories`), the same day it landed as a plain RCL. Naglfar keeps the npm/Style Dictionary token pipeline only — no .NET at all. Reasoning: the token pipeline and the story catalog have different publish cadences and toolchains (npm vs. NuGet) and don't need to share a repo just because both narrate "design system." Splitting immediately, before any tag or NuGet publish of `Norse.DesignSystem.Stories` from Naglfar, avoids ever having to migrate a released package's identity.

**What changed mechanically (clean-cut copy, no git history preservation — the moved history was ~10 commits old and never tagged):**

- Naglfar → Bragi: `Naglfar.slnx` → `Bragi.slnx`, `src/DesignSystem.Stories/`, `src/Directory.Build.props`/`.targets`, `tests/Directory.Build.props`/`.targets`, root `Directory.Build.props` (fully .NET-specific, moves wholesale).
- Naglfar sheds `global.json` and `nuget.config` entirely — no .NET SDK pin, no NuGet restore, needed anymore.
- Naglfar's `release.yml` now calls `release-npm.yml` (it incorrectly called `release-nuget.yml` before this addendum — a pre-existing gap, since Naglfar had never tagged a release and the npm package's publish path had never actually been wired).
- Yggdrasil's `Hosting.Stories.Client.csproj` `NorseRef` now points `Repo>Bragi</Repo>`; `Directory.Packages.props`' `NaglfarVersion` property (and the `Norse.DesignSystem.Stories` `PackageVersion` keyed off it) renamed to `BragiVersion`.
- Ginnungagap's `manifest.psd1`: Naglfar's exception shrinks to `universal, ci, workflows, claude` (npm-only baseline); a new Bragi exception takes Naglfar's former .NET profile verbatim (`universal, sdk, dotnet, nuget, tests, ci, workflows, claude`, ungated).
- `docs/codenames.md`: Bragi leaves the bench for `Norse.DesignSystem.Stories`.
- Doc pairs updated in the same change: `Bifrost/CLAUDE.md` §2 and `README.md` realm tables, `Naglfar/README.md`, `Bragi/README.md` + new `Bragi/CLAUDE.md`, Ginnungagap's `CLAUDE.md` (dependency-order list + the now-corrected gated/ungated classification prose — `carve-the-laws.ps1` derives gate status from `manifest.psd1` dynamically, it was never a hardcoded list to begin with as of this pass) and `profile/README.md`, and `docs/decomposition.md`.

**Not revisited:** whether design-system realm-wiring goes through the platform's brainstorm → spec → plan → TDD cycle. It doesn't, per standing precedent — this addendum documents a mechanical split, not a new design.
