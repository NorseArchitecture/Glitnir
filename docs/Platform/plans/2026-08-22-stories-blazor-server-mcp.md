# Stories Host: Blazor Server + MCP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development`
> (recommended) or `superpowers:executing-plans` to execute this plan task by task. Use
> `superpowers:test-driven-development` for every behavior change and
> `superpowers:verification-before-completion` before claiming any train complete.

**Goal:** Move Bragi's story-fake DI lifetime from Singleton to Scoped, then port Yggdrasil's
BlazingStory catalog host from a standalone WASM app to Blazor Interactive Server with an MCP
endpoint, collapsing two projects into one and retiring a duplicate WASM runtime; finally update
Bifröst's AppHost reference once the new host ships.

**Architecture:** Three realms ship in strict dependency order behind their own gates (PR, CI,
tag, NuGet publish where applicable) — Bragi first, Yggdrasil second, Bifröst last. Each realm's
train is independently reviewable and independently testable.

**Tech Stack:** .NET 11 preview, C# 15, xUnit v3 on Microsoft Testing Platform v2, Shouldly,
NSubstitute, bUnit, `BlazingStory` / `BlazingStory.McpServer` 1.0.0-preview.91, ASP.NET Core
Blazor (Interactive Server render mode), Playwright/Chromium (Yggdrasil's browser smoke gate),
Aspire (Bifröst AppHost).

**Spec:** `../specs/2026-08-22-stories-blazor-server-mcp-design.md`

## Global Constraints

- Execute from the Bifröst root (`Bifrost/`). Each realm's tasks run inside that realm's own
  submodule directory (`Bragi/`, `Yggdrasil/`) — do not run realm-specific `dotnet` commands from
  the Bifröst root itself.
- **Train 1 (Bragi) executes now. Train 2 (Yggdrasil) and Train 3 (Bifröst) are HELD** — do not
  create the Yggdrasil fork, do not touch any Yggdrasil file, do not touch `AppHost.cs`, until the
  human owner confirms `feature/principal-at-the-door` has landed on Yggdrasil `master`. Nothing
  in this plan authorizes starting Train 2 or Train 3 automatically.
- No automatic git commits, no branch pushes, no tag creation, no NuGet publish, no PR creation —
  every train's terminal state is a staged, verified diff on its own feature branch, handed to the
  human owner. This applies even though "ships behind its own PR/CI/tag/NuGet gate" describes the
  realm's overall process; this plan performs the code-and-test portion only.
- xUnit v3 on Microsoft Testing Platform: VSTest `dotnet test --filter` syntax does not work.
  Every focused run uses `dotnet test <project-path> -- --filter-class "<FullyQualifiedClassName>"`.
- House style (`Glitnir/docs/house-rules.md`) governs every line written: target-typed `new()`,
  `static` lambdas with no captures, sentence-shaped underscore test names, Shouldly assertions,
  no `else` after a returning `if`, hoisted usings, one `<PropertyGroup>`/`<ItemGroup>` per csproj.
- Never escalate a class or member to `public` to make it testable — internals are already visible
  via each project's `InternalsVisibleTo Include="$(AssemblyName).Tests"`.
- Bragi's design-system content is exempt from the brainstorm→spec→plan→TDD cycle for *story
  content and authoring*, but this change touches real DI behavior (`AddNorseStoryFakes()`), which
  Bragi's own CLAUDE.md already carves out as riding the full TDD discipline — this plan follows
  that discipline throughout.

---

## Train 1 — Bragi: Scoped story-fake lifetime (ACTIVE)

### Task 1: Flip `AddNorseStoryFakes()` from Singleton to Scoped, with scope-isolation tests

**Files:**
- Modify: `src/DesignSystem.Stories/ServiceCollectionExtensions.cs`
- Modify: `tests/DesignSystem.Stories.Tests/ServiceCollectionExtensionsTests.cs`
- Modify: `CLAUDE.md`
- Modify: `../Glitnir/docs/Bragi/specs/2026-08-08-story-fake-scenario-pattern-design.md`

**Interfaces:**
- Consumes: nothing new — `Norse.AuthN.Services.FakeAuthenticationService`,
  `Norse.DesignSystem.Stories.Reference.FakeReferenceService`,
  `Norse.DesignSystem.Stories.Scenarios.Scenario<TScenario>`,
  `Norse.DesignSystem.Stories.RecordingSessionTransition` all exist unchanged.
- Produces: `IServiceCollection.AddNorseStoryFakes()` — **same signature**, every registration now
  `AddScoped` instead of `AddSingleton`. Yggdrasil's Train 2 consumes this exact extension method
  once it repins `BragiVersion` to the tag this train produces; no call-site change is required
  there beyond the version bump.

- [ ] **Step 0: Create the feature branch**

Bragi is currently clean on `master`. Create an isolated branch for this independent change:

```bash
git -C Bragi checkout -b feature/story-fake-scoped-lifetime
```

- [ ] **Step 1: Write the failing scope-isolation tests**

Replace the two singleton-identity tests in
`tests/DesignSystem.Stories.Tests/ServiceCollectionExtensionsTests.cs` with four tests: the two
renamed (still true, still passing — same-instance-within-one-scope is a weaker, honest claim for
a Scoped registration) plus two new tests that only pass once the lifetime is actually Scoped.

Replace this block:

```csharp
	[Fact]
	void Registers_the_fake_and_its_scenario_as_the_same_singletons()
	{
		using var provider = Build();
		provider.GetRequiredService<IAuthenticationService>().ShouldBeSameAs(provider.GetRequiredService<IAuthenticationService>());
	}

	[Fact]
	void Registers_the_country_request_validator_blazilla_resolves()
	{
		using var provider = Build();
		provider.GetRequiredService<IValidator<CountryRequest>>().ShouldBeOfType<CountryRequestValidator>();
	}

	[Fact]
	void Registers_the_reference_fake_and_its_scenario_as_the_same_singletons()
	{
		using var provider = Build();
		provider.GetRequiredService<IReferenceService>().ShouldBeSameAs(provider.GetRequiredService<IReferenceService>());
	}
```

with:

```csharp
	[Fact]
	void Registers_the_fake_as_the_same_instance_within_one_scope()
	{
		using var provider = Build();
		using var scope = provider.CreateScope();
		scope.ServiceProvider.GetRequiredService<IAuthenticationService>()
			.ShouldBeSameAs(scope.ServiceProvider.GetRequiredService<IAuthenticationService>());
	}

	[Fact]
	void A_new_scope_gets_its_own_authentication_fake_instance()
	{
		using var provider = Build();
		using var scopeA = provider.CreateScope();
		using var scopeB = provider.CreateScope();
		scopeA.ServiceProvider.GetRequiredService<IAuthenticationService>()
			.ShouldNotBeSameAs(scopeB.ServiceProvider.GetRequiredService<IAuthenticationService>());
	}

	[Fact]
	void Registers_the_country_request_validator_blazilla_resolves()
	{
		using var provider = Build();
		provider.GetRequiredService<IValidator<CountryRequest>>().ShouldBeOfType<CountryRequestValidator>();
	}

	[Fact]
	void Registers_the_reference_fake_as_the_same_instance_within_one_scope()
	{
		using var provider = Build();
		using var scope = provider.CreateScope();
		scope.ServiceProvider.GetRequiredService<IReferenceService>()
			.ShouldBeSameAs(scope.ServiceProvider.GetRequiredService<IReferenceService>());
	}

	[Fact]
	void A_new_scope_gets_its_own_reference_fake_instance()
	{
		using var provider = Build();
		using var scopeA = provider.CreateScope();
		using var scopeB = provider.CreateScope();
		scopeA.ServiceProvider.GetRequiredService<IReferenceService>()
			.ShouldNotBeSameAs(scopeB.ServiceProvider.GetRequiredService<IReferenceService>());
	}
```

- [ ] **Step 2: Run to verify the two new tests fail**

```bash
cd Bragi && dotnet test tests/DesignSystem.Stories.Tests -- --filter-class "Norse.DesignSystem.Stories.Tests.ServiceCollectionExtensionsTests"
```

Expected: `A_new_scope_gets_its_own_authentication_fake_instance` and
`A_new_scope_gets_its_own_reference_fake_instance` FAIL with a Shouldly `ShouldNotBeSameAs`
failure (both scopes resolved the identical object) — the current `AddSingleton` registration
returns one instance platform-wide regardless of scope. The two renamed tests PASS unchanged: a
singleton trivially satisfies "same instance within one scope" too, so this red state is exactly
the two tests that assert the *new* behavior.

- [ ] **Step 3: Flip the registrations to Scoped**

In `src/DesignSystem.Stories/ServiceCollectionExtensions.cs`, replace the `AddNorseStoryFakes()`
method (including its doc comment) with:

```csharp
	/// <summary>
	///     Registers the catalog's fake <see cref="IAuthenticationService" /> and
	///     <see cref="IReferenceService" />, each with its own ambient <see cref="Scenario{TScenario}" />
	///     (initialized to the family's <c>Success</c> member) so their stories render and pin their
	///     states with no server context, plus the <see cref="RecordingSessionTransition" /> that stands
	///     in for <see cref="ISessionTransition" />. Also registers the real client-side validators
	///     (Asgard's <c>FormValidator</c> resolves them from DI) — the async email-availability rule
	///     rides the fake, so driven Register stories validate against catalog truth. Scoped
	///     deliberately: the story host is a Blazor Server composition, and DI scope is the framework's
	///     own per-circuit boundary — each visitor's session gets its own fake, scenario, and
	///     session-transition recorder, with no state bleeding across circuits.
	/// </summary>
	/// <returns>The same service collection instance.</returns>
	public IServiceCollection AddNorseStoryFakes() =>
		services
			.AddScoped(static _ => new Scenario<AuthenticationScenario>(AuthenticationScenario.Success))
			.AddScoped<IAuthenticationService, FakeAuthenticationService>()
			.AddScoped<RecordingSessionTransition>()
			.AddScoped<ISessionTransition>(static provider => provider.GetRequiredService<RecordingSessionTransition>())
			.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>()
			.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>()
			.AddScoped(static _ => new Scenario<ReferenceScenario>(ReferenceScenario.Success))
			.AddScoped<IReferenceService, FakeReferenceService>()
			.AddScoped<IValidator<CountryRequest>, CountryRequestValidator>();
```

The class-level `<summary>` above the `extension(IServiceCollection services)` block is unchanged
— it does not mention lifetime.

- [ ] **Step 4: Run the focused test again to verify all six tests pass**

```bash
cd Bragi && dotnet test tests/DesignSystem.Stories.Tests -- --filter-class "Norse.DesignSystem.Stories.Tests.ServiceCollectionExtensionsTests"
```

Expected: PASS, all 6 tests (2 unrelated validator tests + 4 lifetime tests).

- [ ] **Step 5: Update Bragi's `CLAUDE.md`**

In `CLAUDE.md`, find the sentence describing the story-fake scenario pattern (under "The
story-fake scenario pattern is live (2026-08-08)"). It currently reads:

```
...the fake is a stateless switch over an ambient `AuthenticationScenario` (`Scenario<T>` singleton, initialized to `Success`; `0` stays the enum-law sentinel and throws)...
```

Change `singleton` to `scoped`:

```
...the fake is a stateless switch over an ambient `AuthenticationScenario` (`Scenario<T>` scoped, initialized to `Success`; `0` stays the enum-law sentinel and throws)...
```

- [ ] **Step 6: Append a dated correction to the 2026-08-08 design spec**

Append this new section to the end of
`../Glitnir/docs/Bragi/specs/2026-08-08-story-fake-scenario-pattern-design.md`, after the existing
"Ambiguity check" line (do not edit the original §1.1 text — the platform's convention is a dated
correction appended, not a silent rewrite of settled law):

```markdown

## Correction (2026-08-22): the lifetime reverts to Scoped

§1.1's "Lifetime correction" ruled Singleton for the fake and `Scenario<T>`, reasoning "WASM makes
scoped effectively singleton anyway — say what you mean." That reasoning assumed the story host
would always be a standalone WASM app, where each browser tab is its own runtime instance. It does
not hold once the host is Blazor Interactive Server (`../Platform/specs/2026-08-22-stories-blazor-server-mcp-design.md`):
under Interactive Server a Singleton is shared across every connected circuit on the process, so
one visitor pinning a scenario would leak into every other visitor's tab.

`AddNorseStoryFakes()` now registers every fake, its `Scenario<T>`, and
`RecordingSessionTransition`/`ISessionTransition` as Scoped — Blazor Server's DI scope is already
the framework's own per-circuit boundary, so this is the direct DI-native equivalent of what the
Singleton-as-scoped assumption approximated, not a new concept. Doctrine restated: match the
registration lifetime to the real isolation boundary of the host actually consuming it — never to
whichever host happens to be consuming it today.
```

- [ ] **Step 7: Run Bragi's full test suite**

```bash
cd Bragi && dotnet test Bragi.slnx
```

Expected: PASS, no regressions. (bUnit-based tests in `DrivenStoryNavigationTests.cs`,
`ScenarioScopeTests.cs`, `StoryDriverTests.cs`, `FakeAuthenticationServiceTests.cs`,
`FakeReferenceServiceTests.cs`, `RecordingSessionTransitionTests.cs` each build their own
service collection or construct fakes directly — none resolve a fake or scenario across two
separate DI scopes, so none are sensitive to this lifetime change.)

- [ ] **Step 8: Stage the diff**

```bash
git -C Bragi add src/DesignSystem.Stories/ServiceCollectionExtensions.cs tests/DesignSystem.Stories.Tests/ServiceCollectionExtensionsTests.cs CLAUDE.md
git -C Glitnir add docs/Bragi/specs/2026-08-08-story-fake-scenario-pattern-design.md
git -C Bragi status --short
git -C Glitnir status --short
```

Do not commit. Report the staged diff and stop — Train 1 is complete and ready for human review,
commit, PR, CI, tag, and NuGet publish. **Do not proceed to Train 2** without the human owner's
explicit go-ahead confirming `feature/principal-at-the-door` has landed on Yggdrasil `master`.

---

## Train 2 — Yggdrasil: hosting port + MCP (HELD — do not start)

**Entry gate:** confirm with the human owner that `feature/principal-at-the-door` has landed on
Yggdrasil `master`, and that Bragi's Train 1 has shipped a tagged, published release. Before
writing any code, re-verify this train's task list against Yggdrasil's actual `master` at that
time — files may have moved since this plan was written (2026-08-22).

### Task 2: Isolate the work and repin Bragi

**Files:**
- Modify: `Directory.Packages.props` (`BragiVersion`)

**Interfaces:**
- Consumes: the Bragi tag Train 1 produced (its exact version number is not known at plan-writing
  time — read it from Bragi's published release/tag list at execution time, do not guess).
- Produces: Yggdrasil's CPM graph resolving `Norse.DesignSystem.Stories` at the new Scoped-lifetime
  version, ready for the composition work in Task 3 onward.

- [ ] Confirm `feature/principal-at-the-door` is merged: `git -C Yggdrasil log master -1 --oneline`
  should show it merged or its content present; confirm with the human owner if ambiguous.
- [ ] `git -C Yggdrasil fetch origin && git -C Yggdrasil checkout master && git -C Yggdrasil pull`
- [ ] Create an isolated worktree via `superpowers:using-git-worktrees` for
  `feature/stories-blazor-server` off the fresh `master` — a worktree, not an in-place branch
  switch, so this train's work never shares a working directory with any other in-flight Yggdrasil
  branch.
- [ ] In `Directory.Packages.props`, bump `<BragiVersion>` to the tag Bragi's Train 1 published.
- [ ] `dotnet restore Yggdrasil.slnx` to confirm the new version resolves.

### Task 3: Collapse the two projects into `Hosting.Stories`

**Files:**
- Delete: `src/Hosting.Stories.Client/` (all contents)
- Delete: `src/Hosting.Stories.Server/` (all contents)
- Create: `src/Hosting.Stories/Hosting.Stories.csproj`
- Create: `src/Hosting.Stories/Program.cs`
- Create: `src/Hosting.Stories/wwwroot/css/blazor-ui.css` (carried forward verbatim from the old
  client's `wwwroot/css/blazor-ui.css`)
- Create: `src/Hosting.Stories/wwwroot/favicon.ico` (carried forward verbatim from the old client's
  `wwwroot/favicon.ico`)
- Modify: `Directory.Packages.props` (add `BlazingStory.McpServer`)

**Interfaces:**
- Consumes: `Norse.DesignSystem.Stories.ServiceCollectionExtensions.AddNorseStoryFakes()` (Bragi,
  unchanged signature per Train 1), `Norse.Infrastructure.Components.Theme.FluentUI` (Midgard,
  `AddNorseFluentUiTheme()`), `Norse.Infrastructure.ServiceDefaults.AspNet` (Midgard,
  `AddAssetHostServiceDefaults()` / `MapDefaultEndpoints()`), `Norse.AuthN.Services` (Heimdall,
  transitively required by Bragi's fakes).
- Produces: an `Aspire.Hosting.ApplicationModel` project resource discoverable by Bifröst's AppHost
  as `Projects.Hosting_Stories` (Train 3 consumes this).

- [ ] Do not delete before reading: confirm `src/Hosting.Stories.Client/wwwroot/css/blazor-ui.css`
  and `favicon.ico` still exist and are unchanged from the 2026-08-22 read (`index.html` and
  `iframe.html` in the same directory are WASM-boot-specific and are NOT carried forward — Blazor
  Server serves through razor components, not a static WASM shell).
- [ ] `git -C Yggdrasil rm -r src/Hosting.Stories.Client src/Hosting.Stories.Server`
- [ ] Create `src/Hosting.Stories/Hosting.Stories.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

	<ItemGroup>
		<NorseRef Include="AuthN.Services">
			<Repo>Heimdall</Repo>
		</NorseRef>
		<NorseRef Include="DesignSystem.Stories">
			<Repo>Bragi</Repo>
		</NorseRef>
		<NorseRef Include="Infrastructure.ServiceDefaults.AspNet">
			<Repo>Midgard</Repo>
		</NorseRef>
		<NorseRef Include="Infrastructure.Components.Theme.FluentUI">
			<Repo>Midgard</Repo>
		</NorseRef>
		<PackageReference Include="BlazingStory" />
		<PackageReference Include="BlazingStory.McpServer" />
	</ItemGroup>

</Project>
```

  (Keep the comment from the old client csproj about the direct `AuthN.Services` ref pinning
  Bragi's transitive floor, if it still applies once restored — verify against the actual NU1608
  behavior at restore time rather than assuming; the comment travels with the `NorseRef` it
  explains, not blindly copy-pasted.)

- [ ] In `Directory.Packages.props`, add, alphabetically with the other third-party entries:

```xml
		<PackageVersion Include="BlazingStory.McpServer" Version="$(BlazingStoryVersion)" />
```

- [ ] Create `src/Hosting.Stories/Program.cs` with the known-certain composition (FluentUI theme
  attachment is deliberately NOT included here — that is Task 4):

```csharp
using Norse.DesignSystem.Stories;
using Norse.Infrastructure.Components.Theme.FluentUI;

Console.Title = "Norse Stories";
var builder = WebApplication.CreateBuilder(args);
builder.AddAssetHostServiceDefaults();

builder.Services
	.AddRazorComponents()
	.AddInteractiveServerComponents();
builder.Services
	.AddNorseFluentUiTheme()
	.AddNorseStoryFakes()
	.AddBlazingStoryMcpServer();

var app = builder.Build();

app.MapStaticAssets().DisableHttpMetrics();
app.MapDefaultEndpoints();
app.MapBlazingStoryMcp();

app.UseAntiforgery();

// TODO(Task 4): map the BlazingStoryServerComponent root once the FluentUI theme attachment point
// is confirmed against the shipped package API — see spec §5.3.
```

  This file is intentionally incomplete at the end of Task 3 — Task 4 is a spike that determines
  the remaining wiring before this plan can specify it without guessing. Do not remove the `TODO`
  comment until Task 4 replaces it with real code; it is a marker for this plan's own sequencing,
  not a shipped placeholder.

- [ ] Move `wwwroot/css/blazor-ui.css` and `wwwroot/favicon.ico` from the deleted client project
  into `src/Hosting.Stories/wwwroot/` unchanged.
- [ ] `dotnet build Yggdrasil.slnx` — expect it to fail only on the unfinished `Program.cs` (missing
  `MapRazorComponents` call means no root route is mapped; this is expected at this checkpoint, not
  a task failure) — confirm there are no *other* build errors (missing package, bad `NorseRef`,
  etc.) before moving to Task 4.

### Task 4: Spike — determine the FluentUI attachment point and canvas structure

**Files:**
- Modify: `src/Hosting.Stories/Program.cs` (only after findings are in — see Step 4 below)
- No test file — this is an investigatory task; its deliverable is a findings note, not a
  behavior change.

**Interfaces:**
- Consumes: the actual shipped `BlazingStoryServerComponent<TIndex, TFrame>` API from the
  `BlazingStory` package restored in Task 3.
- Produces: a concrete, verified answer to two open questions from spec §5.3/§6, consumed by
  Task 5 (FluentUI wiring) and Task 6 (browser smoke redesign).

- [ ] Finish `Program.cs` enough to boot: add
  `app.MapRazorComponents<BlazingStoryServerComponent<IndexPage, IFramePage>>().AddInteractiveServerRenderMode();`
  (types from `BlazingStory.Components`/wherever the prior-art reference imports them from — check
  `PriorArt/BlazorApp.Stories/BlazorApp.Stories/Program.cs`'s `using` list) with no FluentUI markup
  yet.
- [ ] `dotnet run --project src/Hosting.Stories` and open the root page in a browser.
- [ ] Inspect the rendered DOM (browser dev tools) for: (a) whether `BlazingStoryServerComponent`
  exposes a slot, parameter, or documented extension point for wrapping content (FluentUI theme
  provider needs to wrap the whole app, not just the catalog canvas) — read the component's actual
  source (decompile if the NuGet package ships no source) rather than guessing from its public
  surface; (b) whether individual story canvases render as `<iframe src="/iframe.html">` documents
  (as in WASM mode) or as inline Interactive Server components with no iframe boundary; (c) if
  iframe-isolated, whether a `data-bs-parent-frame`-style discriminator attribute still exists on
  the canvas body.
- [ ] **Scenario-sharing check (added post-Train-1-review, 2026-08-22):** Train 1's Scoped fix
  assumes DI scope == visitor isolation boundary, which is only exactly right if each visitor's
  circuit hosts at most one live story canvas at a time. If Interactive Server renders multiple
  canvases inline within one circuit (no iframe boundary — see (b) above) rather than one canvas
  per circuit, every canvas mounted in that circuit shares the same `Scenario<T>` scope instance.
  `Scenario<T>`'s single-pin-slot semantics (`Repin` throws `InvalidOperationException` once a pin
  is superseded — `src/DesignSystem.Stories/Scenarios/Scenario.cs`) make that a live authoring
  hazard, not just a leftover WASM assumption: two canvases pinned to different scenarios in the
  same circuit would collide. Confirm whether the shipped host ever mounts more than one canvas
  per circuit before treating Train 1's fix as closing the full leak — if it does, this needs its
  own follow-up (most likely: BlazingStory already isolates one canvas per circuit by construction,
  in which case this is a confirmation, not a new problem; if not, escalate rather than silently
  shipping Train 2 on top of an incomplete fix).
- [ ] Write the findings as a short dated note appended to
  `../Glitnir/docs/Platform/specs/2026-08-22-stories-blazor-server-mcp-design.md` under a new
  "Findings (implementation-time)" section — this is the record Task 5 and Task 6 build on, and it
  replaces the spec's own "unresolved at spec time" language once findings land.

### Task 5: Wire FluentUI theming; verify the asset-naming workaround

**Files:**
- Modify: `src/Hosting.Stories/Program.cs`
- Test: manual browser verification (no automated coverage for theme rendering exists elsewhere on
  the platform for this component — do not invent one here; this is a visual smoke check)

**Interfaces:**
- Consumes: Task 4's findings note.
- Produces: a fully bootable `Hosting.Stories` host with FluentUI theming rendering correctly.

- [ ] Using Task 4's findings, add `<NorseFluentDesignTheme/>`/`<FluentProviders/>` (or whatever
  the findings determined) at the correct attachment point.
- [ ] `dotnet run --project src/Hosting.Stories`, confirm in a browser that FluentUI components in
  the catalog (buttons, inputs) render themed, not unstyled.
- [ ] Check the served stylesheet path in the browser network tab. If BlazingStory requests a
  bundle name derived from `Hosting.Stories.csproj` that does not match the real branded output
  (`Norse.Hosting.Stories.styles.css`), add the redirect (spec §5.4) with the real observed paths.
  If the paths already match, do not add a redirect that solves a problem that does not exist.
- [ ] Remove the `TODO(Task 4)` comment from `Program.cs` now that this wiring is real.

### Task 6: Redesign the browser-runtime smoke gate

**Files:**
- Rename: `tests/Hosting.Stories.Server.Tests/` → `tests/Hosting.Stories.Tests/`
- Rename: `tests/Hosting.Stories.Tests/Hosting.Stories.Server.Tests.csproj` →
  `Hosting.Stories.Tests.csproj`
- Modify: `tests/Hosting.Stories.Tests/StoriesServerTests.cs` (the `_framework/blazor.webassembly`
  body assertions in `Root_serves_the_blazor_app_shell` and `Deep_client_route_falls_back_to_the_app_shell`
  no longer hold under Interactive Server — replace with an assertion appropriate to what the real
  Interactive Server shell actually serves, confirmed by running the host, not guessed)
- Modify: `tests/Hosting.Stories.Tests/BrowserRuntime/StoriesBrowserFixture.cs`
- Modify: `tests/Hosting.Stories.Tests/BrowserRuntime/StoriesBrowserRuntimeSmokeTests.cs`
- Modify: `src/Directory.Build.props` (`InternalsVisibleTo` — verify it already targets
  `$(AssemblyName).Tests` generically and needs no change, since the assembly rename carries the
  convention automatically; confirm rather than assume)
- Modify: `.github/workflows/browser-runtime.yml` (the explicit `Hosting.Stories.Server.Tests`
  invocation)

**Interfaces:**
- Consumes: Task 4's findings note (canvas/iframe structure), spec §6's retained invariants.
- Produces: a passing, renamed browser smoke test wired into CI, replacing the WASM-pool-specific
  version.

- [ ] Read `tests/Hosting.Stories.Tests/BrowserRuntime/StoriesBrowserFixture.cs` and
  `StoriesBrowserRuntimeSmokeTests.cs` in full before changing anything — they are substantial
  (17KB/33KB as of 2026-08-22) and encode real, working Chromium orchestration (Kestrel-on-port-zero
  boot, cross-process browser lease, failure-only diagnostics) that must be preserved; only the
  WASM-canvas-specific assertions from spec §6 are being replaced.
- [ ] Using Task 4's findings, rewrite the readiness wait (replace the WASM-interactive marker wait
  with whatever signals a connected circuit + rendered canvas, per spec §6) and the canvas
  structural assertions (replace the five-pooled/one-active/seven-total WASM bound with whatever
  Task 4 found — no iframe pool at all, a different bound, or no iframe boundary whatsoever).
- [ ] Keep unchanged: no-retry policy, failure-only diagnostics collection, the ≥20-discovered-state
  floor, per-state non-empty-render/no-console-error assertions, the driven-state completion-marker
  wait (confirm it still applies once Scoped fakes are live under a real circuit — the marker is
  JS-interop-driven and not WASM-specific, but must be re-verified in this exact host).
- [ ] Update `.github/workflows/browser-runtime.yml`'s explicit test invocation from
  `Hosting.Stories.Server.Tests` to `Hosting.Stories.Tests`.
- [ ] Run the renamed browser smoke locally:
  `dotnet test tests/Hosting.Stories.Tests -- --explicit only --filter-class "Norse.Hosting.Stories.Tests.BrowserRuntime.StoriesBrowserRuntimeSmokeTests"`
- [ ] Run `dotnet test tests/Hosting.Stories.Tests -- --filter-class "Norse.Hosting.Stories.Tests.StoriesServerTests"`
  for the non-explicit suite.

### Task 7: Docs sync

**Files:**
- Modify: `CLAUDE.md` (project table row for the stories host)
- Modify: `README.md` (matching entry)
- Modify: `../Bragi/CLAUDE.md` (the sentence naming `Hosting.Stories.Client` as the sole `NorseRef`
  holder)

**Interfaces:** none — documentation only.

- [ ] In Yggdrasil's `CLAUDE.md` project table, replace the `Hosting.Stories.Client` / `.Stories.Server`
  row with a single `Hosting.Stories` row describing the collapsed project and its MCP surface.
- [ ] Mirror the same change in `README.md` (boy-scout law — same change, same PR).
- [ ] In `../Bragi/CLAUDE.md`, replace "`Hosting.Stories.Client` (`Microsoft.NET.Sdk.BlazorWebAssembly`)
  is the **only** project holding the `NorseRef` to this repo's `DesignSystem.Stories`" with the
  equivalent sentence naming `Hosting.Stories`.
- [ ] `dotnet build Yggdrasil.slnx && dotnet test Yggdrasil.slnx` (excluding explicit browser tests,
  which the default run already skips) — full regression check before staging.
- [ ] Stage the diff across Yggdrasil and Bragi; do not commit. Report and stop — Train 2 complete,
  ready for human review, commit, PR, CI, tag.

---

## Train 3 — Bifröst: AppHost reference (HELD — do not start)

**Entry gate:** Yggdrasil's Train 2 has shipped, tagged, and its submodule pointer inside Bifröst
has been bumped to that tag.

### Task 8: Update the Aspire project reference

**Files:**
- Modify: `src/Orchestration.AppHost/AppHost.cs`

**Interfaces:**
- Consumes: `Projects.Hosting_Stories` (Aspire's generated reference for the renamed project,
  available once Yggdrasil's submodule pointer moves).
- Produces: the `stories` Aspire resource, referencing the new project.

- [ ] In `src/Orchestration.AppHost/AppHost.cs`, replace:

```csharp
builder.AddProject<Projects.Hosting_Stories_Server>("stories");
```

  with:

```csharp
builder.AddProject<Projects.Hosting_Stories>("stories");
```

- [ ] `dotnet run --project src/Orchestration.AppHost`, confirm in the Aspire dashboard that the
  `stories` resource starts and its endpoint serves the catalog.
- [ ] This edit lands directly on Bifröst `master` per Bifröst's own process law (no branch) — stage
  the diff and stop; the human owner commits.

---

## Self-Review

**Spec coverage:** §4 (Bragi) → Train 1 Task 1, including the test audit and doctrine rewrite.
§5.1–5.2 (project collapse, carried-forward composition) → Train 2 Task 3. §5.3 (FluentUI
attachment, explicitly unresolved at spec time) → Train 2 Task 4 (spike) + Task 5. §5.4 (asset
redirect) → Train 2 Task 5. §5.5/docs → Train 2 Task 7 + Train 1 Task 1 Steps 5–6. §6 (smoke gate
redesign) → Train 2 Task 6. §2/§4 (Bifröst AppHost) → Train 3 Task 8. §8 acceptance criterion 9
(sequencing gate) → the Global Constraints entry-gate note plus each held train's own entry gate.

**Placeholder scan:** Train 2's `Program.cs` intermediate state (Task 3) carries a `TODO` comment,
but it is an explicit, load-bearing sequencing marker this plan itself defines and Task 5 removes
— not an unresolved plan gap. Every other step names its exact file, exact code, or exact
verification command. No task says "handle appropriately" or defers a decision without naming what
resolves it.

**Type consistency:** `AddNorseStoryFakes()` signature is identical across Task 1 (Bragi) and Task
3 (Yggdrasil consumption) — `IServiceCollection` in, `IServiceCollection` out, no new parameters.
`Hosting_Stories` (Aspire-generated) is used consistently in Task 3's stated produced interface and
Task 8's consumption.

**Scope check:** three realms, three trains, each independently shippable and independently
testable — matches the spec's own realm boundaries. Not decomposed further; Trains 2 and 3 are
sequentially dependent on Train 1 and on each other, not independent subsystems that could ship in
parallel.

**Ambiguity check:** the two genuine unknowns (FluentUI attachment point, canvas/iframe structure
under Interactive Server) are resolved by a named spike task (Task 4) with a concrete deliverable
(a findings note), not left ambiguous in the tasks that depend on them.
