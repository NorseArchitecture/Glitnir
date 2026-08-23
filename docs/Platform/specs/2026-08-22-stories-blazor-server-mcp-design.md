# Stories Host: Blazor Server + MCP

**Date:** 2026-08-22
**Status:** Approved
**Realms:** Bragi (DI lifetime fix, ships first), Yggdrasil (hosting port, gated on `feature/principal-at-the-door` landing), Bifröst (AppHost reference update)

## 1. Purpose

Yggdrasil's stories catalog (`Hosting.Stories.Client` + `Hosting.Stories.Server`) hosts Bragi's `DesignSystem.Stories` as a standalone Blazor WebAssembly application — a second WASM runtime on the platform alongside `Hosting.Web.Client`, with its own bootstrap cost, its own client-bundle-naming workaround, and no MCP surface. `PriorArt/BlazorApp.Stories` demonstrates that the already-pinned `BlazingStory` `1.0.0-preview.91` supports a genuine Blazor Server hosting mode (`AddInteractiveServerComponents()` + `BlazingStoryServerComponent<IndexPage, IFramePage>` + `.AddInteractiveServerRenderMode()`) and ships a companion `BlazingStory.McpServer` package (`AddBlazingStoryMcpServer()` / `MapBlazingStoryMcp()`) exposing the catalog to MCP tooling. No `BlazingStoryVersion` bump is required — Yggdrasil already centrally pins the version that carries this support.

This design ports the stories host to Interactive Server render mode, collapsing the client/server split into one project, and wires in the MCP endpoint. It also resolves a correctness defect the port would otherwise introduce: Bragi's `AddNorseStoryFakes()` registers its fakes as `Singleton`, deliberately, on the stated assumption that "WASM makes scoped effectively singleton anyway." Under Blazor Server, a singleton is shared across every connected circuit on the process — that assumption inverts from a mild simplification into a real cross-visitor state leak.

## 2. Ownership and sequencing

Realm changes ship in strict dependency order, each behind its own gate (PR merged, CI green, tagged, published to NuGet) — the same discipline the platform used for the migrations framework and the mediator pipeline retirement:

1. **Bragi** flips the fake-service lifetimes (§4), ships on its own PR/CI/tag/NuGet cycle.
2. **Yggdrasil** bumps `BragiVersion` to the new tag and performs the hosting port (§5) plus the smoke-gate redesign (§6). **Gated: do not start implementation until `feature/principal-at-the-door` lands on Yggdrasil's `master`.** That branch carries unrelated in-flight, uncommitted work; this port is genuinely independent and gets its own fork (`feature/stories-blazor-server`) created via an isolated worktree once the gate clears, per `superpowers:using-git-worktrees` — never a branch switch in a working tree that still holds another feature's uncommitted changes.
3. **Bifröst** updates `src/Orchestration.AppHost/AppHost.cs` (`Projects.Hosting_Stories_Server` → `Projects.Hosting_Stories`) directly on `master` once Yggdrasil's submodule pointer moves to the shipped tag — no branch, per Bifröst's own process law.

## 3. Scope

### 3.1 In scope

- Bragi: `AddNorseStoryFakes()` lifetime change, doctrine comment rewrite, audit of existing tests for Singleton-identity assumptions.
- Yggdrasil: collapsing `Hosting.Stories.Client` + `Hosting.Stories.Server` into one `Hosting.Stories` project; Interactive Server render mode; `BlazingStory.McpServer` wiring; CPM entry; NorseRef carryover; FluentUI theme wiring (verified against the shipped `BlazingStoryServerComponent` API, not assumed); re-verification of the client-bundle-naming workaround under the new project name.
- Yggdrasil: redesigning §6 of `2026-08-08-browser-runtime-smoke-gate-design.md` (the Bragi catalog Playwright smoke) for Interactive Server semantics, including the CI workflow's explicit test-project reference.
- Bifröst: `AppHost.cs` project reference update.
- Boy-scout doc updates: Yggdrasil `CLAUDE.md`/`README.md` project table, Bragi `CLAUDE.md` (currently names `Hosting.Stories.Client` specifically as the sole `NorseRef` holder).

### 3.2 Out of scope

- Any change to `Hosting.Web.Server` / `Hosting.Web.Client` (the main app's WASM/InteractiveAuto composition is unaffected — §5 of the browser-runtime smoke gate design stands unchanged).
- Any change to Bragi's story content, authoring conventions, or the story-fake scenario pattern's *behavior* — only its DI lifetime.
- A general audit of other Singleton registrations on the platform.
- MCP client tooling itself (skills, agent wiring) — this design lands the server-side endpoint only.

## 4. Bragi change

`ServiceCollectionExtensions.AddNorseStoryFakes()` (`src/DesignSystem.Stories/ServiceCollectionExtensions.cs`) changes every `AddSingleton` to `AddScoped`: `Scenario<AuthenticationScenario>`, `IAuthenticationService`/`FakeAuthenticationService`, `RecordingSessionTransition`, `ISessionTransition`, `IValidator<LoginRequest>`, `IValidator<RegisterRequest>`, `Scenario<ReferenceScenario>`, `IReferenceService`/`FakeReferenceService`, `IValidator<CountryRequest>`. Each Blazor Server circuit gets its own instance of every fake and its own ambient scenario — the direct DI-native equivalent of what the WASM singleton-as-scoped assumption approximated, with no new concepts introduced.

The method's XML doc comment ("Singletons deliberately: WASM makes scoped effectively singleton anyway — say what you mean") is rewritten to state the Scoped rationale instead; it is no longer accurate once the sole consumer is a Blazor Server host.

**First task, before flipping the lifetime:** audit `FakeAuthenticationServiceTests`, `ScenarioTests`, `ScenarioScopeTests`, `StoryDriverTests`, and `ValidationSummaryHarnessTests` for any assumption that two resolutions of a fake or its scenario return the same instance outside a single test's own explicit scope. bUnit test setup typically registers its own service collection per test and would be unaffected, but this must be verified, not assumed — a Scoped registration that a test's harness resolves twice from two different scopes is a silent behavior change, not a compile error.

Ships via its own PR → CI green → tag → NuGet publish, bumping the Bragi-consumed `BragiVersion` in Yggdrasil's CPM once released.

## 5. Yggdrasil change

### 5.1 Project collapse

Delete `Hosting.Stories.Client` (`Sdk.BlazorWebAssembly`) and `Hosting.Stories.Server` (`Sdk.Web`, static host). Create one new project, `Hosting.Stories` (`Sdk.Web`, assembly `Norse.Hosting.Stories`), modeled on `PriorArt/BlazorApp.Stories`'s `Program.cs`:

```
AddRazorComponents().AddInteractiveServerComponents();
AddBlazingStoryMcpServer();
...
MapBlazingStoryMcp();
MapRazorComponents<BlazingStoryServerComponent<IndexPage, IFramePage>>()
    .AddInteractiveServerRenderMode();
```

MCP is always mapped — no `IsDevelopment()` gate. The stories host carries no production traffic or auth story of its own; gating MCP specifically while leaving the rest of the catalog open would be an inconsistent, unmotivated exception.

### 5.2 Carried-forward composition

The new project's `ItemGroup` combines what both old projects held:

- `NorseRef` to Heimdall's `AuthN.Services`, Bragi's `DesignSystem.Stories`, and Midgard's `Infrastructure.Components.Theme.FluentUI` (from the old client).
- `NorseRef` to Midgard's `Infrastructure.ServiceDefaults.AspNet` (from the old server — `AddAssetHostServiceDefaults()`, `MapDefaultEndpoints()`, `DisableHttpMetrics()`).
- `AddNorseStoryFakes()` and `AddNorseFluentUiTheme()` remain called from `builder.Services`.
- A new `PackageVersion Include="BlazingStory.McpServer" Version="$(BlazingStoryVersion)"` entry in `Directory.Packages.props` (same version family as `BlazingStory`, confirmed by the prior-art reference pinning both at `1.0.0-preview.91`), plus the corresponding `PackageReference`.
- Dropped entirely: `Microsoft.AspNetCore.Components.WebAssembly`, `.DevServer`, `.Server` package references; `OverrideHtmlAssetPlaceholders`; `SupportedPlatform Include="browser"`.

### 5.3 Unresolved at spec time — first implementation task

Where the FluentUI theme markup (`<NorseFluentDesignTheme/>`, `<FluentProviders/>`) attaches relative to `BlazingStoryServerComponent<IndexPage, IFramePage>` is not knowable without reading the shipped component's actual render-tree API — prior art's demo app carries no FluentUI dependency and shows no equivalent wiring. The implementer's first task is reading that API (decompiled source or package docs) to determine the correct attachment point before writing any host markup; this design does not prescribe a shape it cannot verify.

### 5.4 Asset-naming workaround

The old `Hosting.Stories.Server/Program.cs` redirects `/Hosting.Stories.Client.styles.css` → `/Norse.Hosting.Stories.Client.styles.css`, because BlazingStory derives the host's scoped-CSS bundle name from the `.csproj` filename rather than the Norse-branded `AssemblyName`. Collapsing to a single project named `Hosting.Stories` does not make this workaround moot by construction — BlazingStory may still request a bundle name derived from `Hosting.Stories.csproj` against an actual output named `Norse.Hosting.Stories.styles.css`. This must be verified against the real build output and the redirect updated (new literal paths) or removed, based on what is actually observed — not assumed either way.

### 5.5 Docs

Yggdrasil's `CLAUDE.md` project table row and `README.md` entry for `Hosting.Stories.Client` / `.Stories.Server` collapse to a single `Hosting.Stories` row. Bragi's `CLAUDE.md`, which currently states "`Hosting.Stories.Client` ... is the **only** project holding the `NorseRef` to this repo's `DesignSystem.Stories`," updates that sentence to name `Hosting.Stories`. Both updates land in the same change as the code that makes them true, per each repo's own boy-scout-law rule.

## 6. Browser-runtime smoke gate redesign (supersedes §6 of `2026-08-08-browser-runtime-smoke-gate-design.md`)

§6 of the existing design ("Bragi catalog smoke") is built entirely around BlazingStory's *WASM* canvas mechanics: a pooled-iframe bound (five released + one active, up to seven live documents), a WASM-interactive readiness marker, and the `/Hosting.Stories.Client.styles.css` redirect as "the only expected first-party redirect." Interactive Server removes the premise underneath all of it — there is no per-canvas WASM bootstrap, and whether canvases remain iframe-isolated at all under server render mode is unknown (§5.3 applies here too: verify against the real shipped component before designing assertions around it).

**First task:** boot the ported host locally and inspect the rendered DOM for the story catalog. Determine empirically:

- Whether story canvases are still `iframe.html`-document-isolated (in which case a `data-bs-parent-frame`-style discriminator likely still exists, just driven by a SignalR circuit instead of a WASM runtime) or whether Interactive Server renders canvases inline without iframe isolation.
- What, if anything, replaces the WASM-interactive readiness marker — the redesigned smoke needs a signal that a circuit is connected and a canvas's real component (not a loading shell) has rendered, whatever markup carries that signal in the shipped package.
- Whether the asset-naming redirect (§5.4) still exists, and under what literal path.

**What must still hold, regardless of the above** (the invariant the original design was protecting, restated mechanism-agnostically):

- The real `Hosting.Stories` entry point boots over Kestrel and the catalog tree is discoverable at runtime — the test still consumes the rendered tree as its target list, not a maintained inventory, and still fails if the discovered state count drops below the current baseline (20 at time of writing; re-baseline if the catalog has grown by implementation time).
- Every discovered story state is visited; each visit asserts a non-empty component render, no browser console error, no unhandled page/frame exception, and no visible circuit-failure or reconnect UI.
- Every driven state (`StoryDriver`-backed Login/Register flows) still reaches its completion marker before assertion — this marker is JS-interop-driven and is not WASM-specific, so it should carry over unchanged, but must be confirmed once Scoped fakes (§4) are live under a real circuit.
- No Playwright retries; failure artifacts (trace, screenshot, console/network log, server log) collect only on failure, matching §7 of the original design.

**What is explicitly retired, not adapted:** the five-pooled/one-active/seven-total canvas bound (a WASM-pool-specific invariant with no server-mode analogue until the DOM inspection above says otherwise).

**CI wiring:** `browser-runtime.yml`'s explicit `dotnet test` invocation of `Hosting.Stories.Server.Tests` becomes `Hosting.Stories.Tests` (or whatever test project accompanies the renamed `Hosting.Stories` project), and the `InternalsVisibleTo` grant in `src/Directory.Build.props` follows the assembly rename. The existing `Hosting.Web.Server` smoke (§5 of the original design) is untouched — that host keeps its WASM/InteractiveAuto composition.

## 7. Testing and verification

- Bragi: existing xUnit v3 suite re-run after the lifetime flip, with the audit from §4 resolved first.
- Yggdrasil: manual verification that two independent browser sessions against the ported host hold independent scenario state (proves the Scoped fix actually isolates circuits); MCP endpoint reachability (an MCP client or direct request against the mapped route); FluentUI theme renders; full catalog navigation (Welcome, `Primitives/`, `Authentication/` states) works end to end.
- Yggdrasil: the redesigned browser-runtime smoke (§6) is a hard requirement of this port's completion, not deferred follow-up — the port is not done until the safety net that would catch a stories-host regression works again.
- Bifröst: `dotnet run --project src/Orchestration.AppHost` boots the `stories` resource under its new project reference.

## 8. Acceptance criteria

1. Bragi's `AddNorseStoryFakes()` registers every fake as Scoped; the doctrine comment reflects why; the Singleton-assumption audit is resolved (tests fixed or confirmed unaffected) before the lifetime change merges; Bragi ships tagged and published.
2. Yggdrasil's `Hosting.Stories.Client` and `Hosting.Stories.Server` no longer exist; `Hosting.Stories` is the sole project, `Sdk.Web`, Interactive Server render mode, no WebAssembly package references.
3. `BlazingStory.McpServer` is wired (`AddBlazingStoryMcpServer()` + `MapBlazingStoryMcp()`), always mapped, and independently verified reachable.
4. FluentUI theming renders correctly under the verified attachment point from §5.3.
5. The asset-naming workaround from §5.4 is either confirmed unnecessary or updated to the real observed mismatch — not left stale, not silently assumed gone.
6. The Bragi catalog browser smoke is redesigned per §6, wired into `browser-runtime.yml` under the renamed test project, and passing — the five-pool WASM canvas bound is gone, not merely renamed.
7. Bifröst's `AppHost.cs` references `Projects.Hosting_Stories`; the dashboard boots the stories resource.
8. Yggdrasil `CLAUDE.md`/`README.md` and Bragi `CLAUDE.md` reflect the collapsed project name in the same change as the code.
9. Implementation began only after `feature/principal-at-the-door` landed on Yggdrasil `master`, on an isolated fork/worktree that never touched that branch's uncommitted state.

## 9. Findings (implementation-time, 2026-08-22 — Task 4 spike)

§5.3 and §6 are no longer "unresolved at spec time" — Task 4 decompiled the shipped `BlazingStory`
1.0.0-preview.91 package (`ilspycmd` against `BlazingStory.dll`) and drove the real ported host
with Playwright. Full evidence (decompiled snippets, live DOM captures, exact exception traces):
`../Bifrost/.superpowers/sdd/2026-08-22-stories-blazor-server-mcp/task-4-report.md`.

**§5.3 — the FluentUI attachment point.** `BlazingStoryServerComponent<TIndexPage, TIFramePage>`
exposes no slot or parameter to wrap — it is a bare route shim (`[Route("/{*urlPath}")]`) that
picks one of its two type arguments as the root document component based on whether the request
path is `/iframe.html`. **The attachment point is the type arguments themselves**: `TIndexPage`
and `TIFramePage` must each be a full, consumer-authored HTML-document component (mirroring
`PriorArt/BlazorApp.Stories`'s `IndexPage.razor`/`IFramePage.razor`, not BlazingStory's
internal `Index`/`IFrame` types of the same name). Inside each doc shell, `@rendermode` must land
on a **parameterless wrapper component**, not directly on `<BlazingStoryApp Assemblies="...">` —
putting `@rendermode` straight on `BlazingStoryApp` makes ASP.NET Core try to JSON-serialize its
`Assemblies` parameter (`IEnumerable<Assembly>`) across the SSR→interactive boundary marker, and
`System.Reflection.Assembly`/`TypeInfo` is not JSON-serializable (`NotSupportedException`, 500 on
every request — reproduced live before the fix). The wrapper (mirroring the platform's own
`Hosting.Web.Server/Components/App.razor` pattern — `<NorseFluentDesignTheme @rendermode="..."/>`
beside `<Routes @rendermode="..."/>`) is where FluentUI theme markup belongs, as a sibling of
`<BlazingStoryApp>`:

```razor
@* Components/App.razor *@
<NorseFluentDesignTheme/>
<BlazingStoryApp Assemblies="[typeof(AssemblyMarker).Assembly]"/>
```

**And it must be wired into both `IndexPage.razor` and `IFramePage.razor`** — see §6 below for
why: `/iframe.html` is a separate top-level document/circuit, not a component nested inside the
catalog shell's circuit, so a theme provider wired only into the index shell leaves every canvas
iframe unthemed.

**§6 — canvas structure, confirmed empirically:**
- **Still iframe-isolated**, identically to WASM mode: live DOM inspection of the running host
  shows every story canvas as a real `<iframe src="/iframe.html?viewMode=story&id=...">`. This is
  baked into `BlazingStory.dll` itself (`PreviewFrame` → `PooledIFrame`, JS-managed), not
  render-mode-specific.
- **The `data-bs-parent-frame` discriminator survives** (`data-bs-parent-frame="docs"` observed
  live), unchanged from WASM.
- **Correction to this design:** the five-pooled/one-active canvas bound §6 called "explicitly
  retired... a WASM-pool-specific invariant with no server-mode analogue" is **not** WASM-specific
  — `PooledIFrame.razor.js`'s `maxIframesInPool = 5` pool is the same client-side mechanism
  regardless of render mode. §6's redesign should carry the pool bound forward (re-verified
  against the live DOM, not assumed retired) rather than treating it as WASM-only baggage.
- **Readiness marker for the redesigned smoke:** the `_blazing_story_ready_for_visible` CSS class
  BlazingStory adds to each iframe's `<html>` once fonts/styles/frame-size settle is unchanged
  under Interactive Server and is not WASM-boot-specific — it's the marker §6's redesigned smoke
  should watch for.

**Scenario-sharing check — confirmed a non-issue, not a new leak.** A pooled iframe/circuit *can*
be reused across different story canvases over its lifetime (`PooledIFrame.razor.js` prefers
`blazor.navigateTo` client-side navigation over a full reload when reusing a pooled iframe on the
same origin) — the concern the brief raised is real in principle. Empirically, navigating the
live host between `Authentication/Login` and `Authentication/Register` (each a Docs page
rendering two canvases apiece, six canvas-visits total, iframes confirmed reused via persisted
Playwright element references) produced zero exceptions and zero cross-canvas state bleed —
verified against the server log (no `Exception`, no `fail:` lines) across the whole session.
Mechanistically: `ScenarioScope.Repin` only fires when the *same* `ScenarioScope` component
instance survives a re-render; navigating between two different story canvases is a genuine route
change, so Blazor's router disposes the outgoing `ScenarioScope` (clean, reference-checked
no-op `Release`) before the incoming one mounts and calls `Pin` (unconditional supersede, by
design). BlazingStory does isolate one *live, mounted* canvas's scenario pin per circuit at a
time, even though the circuit/iframe itself is recycled across canvases — Train 1's
Scoped-per-circuit fix holds. This depends on Blazor's default router behavior (full disposal of
the outgoing route's tree before the incoming one mounts), not on anything Bragi or Yggdrasil
control or test today — worth re-confirming if a future BlazingStory version changes how pooled
iframes recycle across navigations.

**§5.4 (asset-naming workaround) — not yet resolved, needs Task 5.** The boot-minimal shells built
for this spike don't reference a `Hosting.Stories`/`Norse.Hosting.Stories` scoped-CSS bundle at
all (deliberately out of scope — presentation, not boot-plumbing), so no request for any
`*.styles.css` bundle was observed. Task 5 must add that `<link>` and check the real generated
bundle filename against `Norse.Hosting.Stories.styles.css` once it exists, per this design's own
§5.4 instruction — this spike neither confirms nor refutes the mismatch.
