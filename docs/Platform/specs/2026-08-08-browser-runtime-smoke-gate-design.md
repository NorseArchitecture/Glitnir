# Browser Runtime Smoke Gate

**Date:** 2026-08-08
**Status:** Approved
**Realms:** Yggdrasil (gate owner), Bragi (released input), Ginnungagap (`.github`, CI capability)

## 1. Purpose

Yggdrasil's existing tests prove that its ASP.NET Core hosts serve application shells and that its mediator, gRPC, and persistence pieces work in process or through test-server transports. They do not prove that a real browser can boot either WebAssembly application and operate it without browser-side failures.

The missing proof is already named in live source. `Hosting.Web.Client/Program.cs` carries a dated exit condition requiring a Playwright smoke before `GrpcWebCallInvoker` can be retired, while no Playwright test exists. This design supplies that infrastructure canary for Yggdrasil's two browser hosts:

1. `Hosting.Web.Server` must transition an InteractiveAuto component into WebAssembly and complete a successful browser-originated gRPC-Web request through the real mediator and outcome-interceptor stack.
2. `Hosting.Stories.Server` must boot the released Bragi catalog, render every named story state through BlazingStory's canvas pool, and never reproduce the recursive fallback race that loads the full catalog inside a canvas.

This is not the platform's full end-to-end suite. Its deliberately narrow answer is: can both released browser compositions start, boot WebAssembly, dispatch their representative interactions, and remain free of browser/runtime failures?

## 2. Ownership and protection boundary

The browser tests live in Yggdrasil and gate Yggdrasil pull requests against the exact Bragi and BlazingStory versions resolved by Yggdrasil's central package graph. They do **not** gate a Bragi pull request before Bragi releases. A future Bragi regression is detected after that release is pinned into Yggdrasil, not while the Bragi change is still under review.

That delayed-detection window is accepted for this tier. Bragi ships content and an RCL, not a runnable host; duplicating Yggdrasil's WASM/server host inside Bragi solely to move this smoke across a repository boundary would create a second composition whose fidelity must itself be maintained. A future cross-repository or black-box gate may close the window without inventing a duplicate host.

The first ship train is nevertheless ordered because the current race mitigation is a pending Bragi working-tree change, not part of released `Norse.DesignSystem.Stories` 0.0.6:

1. Ginnungagap ships the opt-in Chromium workflow capability.
2. Bragi pins the same BlazingStory version as Yggdrasil, lands the capture-phase submit guard plus the driver-readiness marker specified in §6.3, passes its unit/component tests, and releases.
3. Yggdrasil pins that Bragi release.
4. Yggdrasil lands both browser smokes and enables Chromium in its reusable-workflow call.

The Yggdrasil gate is not considered implemented against the old, unmitigated Bragi package.

## 3. Scope

### 3.1 In scope

- Chromium only, headless in the required pull-request gate.
- The real `Hosting.Web.Server` and `Hosting.Stories.Server` entry points.
- Real Kestrel listeners on ephemeral localhost ports.
- Yggdrasil's existing `/reference/country-lookup` page as the temporary full-stack browser probe.
- A successful reference lookup backed by a deterministic test repository, with no database involvement.
- The real browser gRPC-Web client, server mediator pipeline, server outcome interceptor, and client outcome interceptor.
- Dynamic discovery and sequential rendering of every named state exposed by Bragi's rendered catalog.
- BlazingStory preview.91's real iframe-pool behavior, rather than a fictional single persistent canvas.
- Strict browser-error, fallback-shell, and recursive-runtime detection.
- Failure-only traces, screenshots, browser logs, server logs, and runtime/frame inventories.
- An opt-in Playwright Chromium capability in Ginnungagap's reusable CI workflow.

### 3.2 Out of scope

- Moving `CountryLookup` from Yggdrasil to Mimir. The smoke uses its current location without blessing that ownership permanently.
- Testing Mimir's repository implementation, EF mapping, migrations, provider behavior, seed contributors, or database compatibility. Those remain service and persistence test responsibilities.
- Postgres, Testcontainers, migrations, Aspire, Bifrost orchestration, or deployed containers.
- Full authentication, identity, or product workflows.
- Visual regression, pixel comparison, responsive-layout review, or accessibility auditing.
- Firefox, WebKit, branded Chrome/Edge, and Safari.
- Test retries or quarantining a race as an acceptable intermittent failure.
- Reworking BlazingStory to use a single WebAssembly runtime or fixing its upstream pool lifecycle.
- A duplicate runnable story host in Bragi.

## 4. Test architecture

Use `Microsoft.Playwright` from Yggdrasil's existing xUnit v3 test assemblies:

- `tests/Hosting.Web.Server.Tests`
- `tests/Hosting.Stories.Server.Tests`

Each matching server assembly grants its own test assembly access to its internal top-level `Program` through `src/Directory.Build.props`. That explicit `InternalsVisibleTo` relationship, rather than the transitive reference topology, keeps each fixture's entry point unambiguous. A combined browser project would need extra entry-point disambiguation without buying a useful shared boundary.

Each fixture wraps `WebApplicationFactory<Program>` and calls `UseKestrel(0)` before factory initialization. `UseKestrel` is a .NET 10 `Microsoft.AspNetCore.Mvc.Testing` API, consumed here through the platform's pinned .NET 11 preview package; the design does not depend on the API being preview-only. After startup, the fixture calls `CreateClient()` and uses that client's dynamically corrected `BaseAddress` as Playwright's origin. It never accesses `WebApplicationFactory.Server`, which is unsupported in Kestrel mode.

The factory binds HTTP only and leaves `HttpsRedirectionOptions.HttpsPort` unset. Because the test server exposes no HTTPS address, `UseHttpsRedirection` has no redirect target and the browser remains on the Kestrel HTTP origin. The expected one-time server warning is diagnostic output, not a browser-console allowance. If a future framework version redirects despite that configuration, the fixture must explicitly reconfigure the test listener; it must not paper over the failure with a browser-wide certificate-error flag.

`Hosting.Web.Server` reads both connection strings before `WebApplicationFactory`'s deferred configuration hooks run. A test-module initializer sets the same inert, syntactically valid `ConnectionStrings__norse_identity` and `ConnectionStrings__norse_reference` values before any factory can boot. The existing per-class static setup is consolidated into that one initializer. The values are process-global but identical for every test in the assembly; no browser fixture claims a competing value, and no smoke path opens either connection.

### 4.1 Serialization

Within each assembly, the browser class belongs to an xUnit collection declared with `DisableParallelization = true`, following Midgard's `GrpcWebRoundTripTests` precedent. Across the two concurrently discoverable test modules, the Chromium fixtures also acquire the same bounded cross-process lease under the operating system's temporary directory before launching a browser. This keeps ordinary local `dotnet test` runs serialized even when the outer test runner schedules projects concurrently.

In CI, Yggdrasil additionally runs test modules with MSBuild node parallelism disabled (`-m:1`) when Chromium is enabled. The cross-process lease remains the correctness mechanism; CI serialization keeps the second browser test from spending its timeout merely waiting for the first.

One Chromium browser and one browser context are used per host test. No Playwright or test-runner retry is permitted. Hosts, contexts, browsers, traces, and the cross-process lease are disposed on every exit path.

## 5. Yggdrasil Web.Server smoke

### 5.1 Data seam

The browser smoke does not replace `ReferenceDbContext` and does not exercise EF. `AddNorseReferenceService` fuses the Npgsql factory, generated handlers, and `IReferenceService`, while `AddWell<ReferenceDbContext>` registers its repository as a deferred singleton that validates the live EF model on first resolution. Surgery at the factory boundary would be an unproven second persistence composition and would test database behavior in the browser tier.

Instead, the fixture removes the production `IReadRepository<CountryOrAreaView>` descriptor after normal host registration and installs one deterministic test repository. The replacement accepts only the baked United States identifier and evaluates the requested projection over a real `CountryOrAreaView` fixture. It returns NotFound for every other identifier. This preserves the real `CountryQueryHandler`, parser, mediator pipeline, gRPC service, both outcome interceptors, and browser client while cleanly stopping at the repository interface.

The fixture obtains the identifier from `Iso3166.Ids[IsoCountryCode.UnitedStatesOfAmerica]`. Alpha-2, alpha-3, and name come from the United States entry in `Iso3166.All`; they are not attributes of `Iso3166.Ids`. Both the returned `CountryResponse.Id` and the view fixture's `Id` use that same baked GUID, so the page's independent client lookup renders `Match`.

No test in this work uses the existing `CountryLookupE2ETests` name. The browser class is named for browser-runtime smoke so its role cannot be confused with the existing Postgres transport suite.

### 5.2 WebAssembly readiness

Downloading the WebAssembly bundle does not prove that a prerendered component is interactive. `CountryLookup` therefore exposes semantically inert `data-norse-renderer` and `data-norse-interactive` attributes derived from `ComponentBase.RendererInfo`. The values are the renderer name and an explicitly lower-case boolean. The browser waits for `data-norse-renderer="WebAssembly"` and `data-norse-interactive="true"`. This is ordinary runtime observability on the temporary probe page, not a test environment branch.

The browser must observe the marker transition to interactive WebAssembly before clicking. The button's prerendered presence is not a readiness signal, and the test never clicks during the window in which the DOM exists but no client handler is attached.

### 5.3 Browser flow

One browser context performs this sequence:

1. Navigate to the real host and wait for its InteractiveAuto WebAssembly resources to download successfully.
2. Within the same browser context, navigate afresh to `/reference/country-lookup`, giving InteractiveAuto the warm cache from which to choose WebAssembly.
3. Wait until the page's `RendererInfo` marker reports an interactive WebAssembly renderer.
4. Enter `US` and activate the `Look up` button through its accessible role/name.
5. Observe a browser-originated request whose content type identifies the real gRPC-Web call. Rendered output alone is insufficient because an InteractiveServer circuit could produce identical markup.
6. Wait for the gRPC-Web response and rendered completion.
7. Assert `US`, `USA`, `United States of America`, and the `Match` badge.

The observed browser request proves that the event handler executed in the WebAssembly client. The successful rendered payload proves request serialization, gRPC-Web transport, mediator dispatch, server outcome encoding, client outcome decoding, and component rendering together.

### 5.4 Failure conditions

The test fails for any of the following during its browser context's lifetime:

- uncaught page or frame exception;
- console message at error severity;
- unhandled promise rejection;
- failed first-party framework, static-asset, or gRPC-Web request;
- first-party HTTP response with an unexpected status;
- redirect away from the selected HTTP Kestrel origin;
- missing interactive-WebAssembly readiness marker;
- missing browser-originated gRPC-Web request;
- missing or mismatched successful lookup output;
- timeout waiting for boot, readiness, dispatch, or render completion.

There is no broad console or network allowlist. Any unavoidable message must be documented and matched narrowly at its assertion site.

## 6. Bragi catalog smoke

### 6.1 Pinned upstream behavior

Bragi currently floats `BlazingStory` as `1.*-*`, while Yggdrasil centrally pins `1.0.0-preview.91`. Bragi must pin `1.0.0-preview.91` before the driver change releases so both standalone Bragi CI and Yggdrasil exercise the same canvas machinery. A future BlazingStory upgrade is deliberate and must re-audit this section before changing both pins together.

Preview.91 does not maintain exactly one canvas. Its `PooledIFrame.razor.js` keeps up to five released iframes in a hidden body-level pool for 60 seconds while another canvas may be active. A correct page can therefore hold the outer catalog runtime plus as many as six live canvas runtimes. Cumulative bootstrap count is not a correctness invariant and is not asserted.

The recursive defect has a different shape: a canvas form performs an unintended native navigation, `MapFallbackToFile("index.html")` answers with the outer catalog shell, and that shell creates another canvas inside the first. The host fallback amplifies the submit race; neither cause is omitted from the verdict.

### 6.2 Runtime and document law

Across the catalog sweep:

- every iframe document has the `/iframe.html` document path;
- an active story canvas reports `body[data-bs-parent-frame="story"]`, the discriminator stamped by BlazingStory itself;
- a documentation canvas reports `body[data-bs-parent-frame="docs"]` and is excluded from the story-state count;
- no iframe document renders the outer catalog navigation shell;
- no iframe contains a descendant iframe;
- preview.91 never exceeds five pooled canvases plus one active canvas, or seven live documents/runtimes including the outer catalog;
- movement between the active container and pool is allowed and is not mistaken for recursive startup.

An iframe whose navigation is answered by `index.html` fails immediately through the wrong path/marker, catalog-shell, or descendant-frame checks. Structural classification is authoritative; cumulative `_framework/blazor.webassembly` request count is retained only as diagnostics.

### 6.3 Driver prerequisite and readiness

The pending Bragi capture-phase guard becomes required product behavior: before `requestSubmit`, `StoryDriver` registers a one-shot capture listener that prevents the browser's native submit default without stopping propagation to Blazor. That closes the race in which no Blazor/enhanced-navigation listener is ready and the form escapes the iframe.

Seven current states are `StoryDriver`-driven: four Login states and three Register states. The driver wraps its child in a layout-neutral `display: contents` element carrying `data-norse-story-driver-state="pending"`; the element exists only for a driven state and disappears with that component. It transitions to `complete` only after the guarded submit produces post-submit DOM activity and the result reaches bounded DOM quiescence. Failure to find a form, observe submit activity, or settle within the driver budget throws. The marker reports lifecycle only; it does not change fake outcomes or add a host testing mode.

The Playwright sweep waits for this marker on driven states. It does not rely on BlazingStory's `canvasFrameInitialized` flag or `_blazing_story_ready_for_visible` timer: those prove canvas boot and a fixed 300 ms delay, not fake-backed scenario completion.

### 6.4 Catalog discovery and flow

The test launches the real `Hosting.Stories.Server` entry point and keeps one Chromium page/context alive for the entire sweep. It discovers navigation targets from the rendered catalog tree rather than maintaining a second source inventory.

Discovery cannot pass vacuously. The rendered tree must yield at least the current baseline of 20 named story states, including non-zero state sets under both `Authentication/` and `Primitives/`. Documentation targets such as Welcome and Scenarios are classified through `data-bs-parent-frame="docs"` and do not count toward the floor.

Every discovered story state is visited sequentially. For each state, completion requires:

- `body[data-bs-parent-frame="story"]` on the active canvas;
- a non-empty component root;
- the driver readiness marker at complete when that state is driver-backed;
- no visible loading shell or Blazor error/reload UI;
- no browser or frame error;
- the runtime/document law in §6.2 still holding across active and pooled frames.

The assertion does not pin exact wording, pixels, or layout. The gate cares that the real component and fake-backed scenario render without failure and without recursive catalog startup.

### 6.5 Network and failure conditions

`Hosting.Stories.Server` deliberately answers `/Hosting.Stories.Client.styles.css` with one 302 redirect to `/Norse.Hosting.Stories.Client.styles.css`. The test asserts that exact redirect and the final successful stylesheet response. It is the only expected first-party redirect.

The state currently being visited is recorded before navigation. The sweep fails immediately for:

- uncaught exception in the main page or any active/pooled frame;
- console error or unhandled promise rejection;
- failed WebAssembly, framework, or static-asset request;
- unexpected first-party redirect or HTTP failure status;
- fewer than 20 discovered states or absence of either required catalog root;
- empty canvas or visible Blazor failure UI;
- missing driver-completion marker on a driven state;
- any iframe document not served from `/iframe.html`;
- a canvas with the wrong `data-bs-parent-frame` classification;
- a catalog shell or descendant iframe inside any canvas;
- more live frames/runtimes than preview.91's outer-plus-pool bound;
- timeout waiting for a state to render and settle.

Playwright retries remain disabled. A scheduling race is a product failure, not a flaky-test classification.

## 7. Diagnostics and time budgets

Both fixtures collect diagnostics before the first navigation. On failure they write a self-contained directory beneath the test-results tree containing:

- Playwright trace;
- full-page screenshot;
- ordered console and page-error log with frame URLs;
- failed and redirected request/response log;
- server log captured from the real host;
- current URL and current catalog state, where applicable;
- active and pooled frame tree with document paths and `data-bs-parent-frame` values;
- observed WebAssembly bootstrap requests and their owning frames.

Successful runs remove or never materialize these artifacts. The anonymous flows contain no credentials or tokens.

The initial budgets are explicit:

- 90 seconds for first host/WebAssembly startup;
- 15 seconds for each story-state navigation and settle;
- 5 minutes for either browser test;
- 20 minutes for the complete CI test step;
- 30 minutes for the CI job, including build and browser installation.

Timeout diagnostics name the phase and route/state. Growing the catalog does not silently grow the gate without limit; the five-minute browser-test ceiling forces an intentional revisit if the dynamic sweep outgrows its canary budget.

## 8. CI integration

Yggdrasil calls Ginnungagap's `ci-build-test.yml` reusable workflow and cannot inject setup steps into that job. Browser installation becomes an explicit reusable-workflow capability with no string interpolation:

1. Add a boolean `playwright_chromium` input, default `false`.
2. Yggdrasil passes `playwright_chromium: true`; every other caller inherits `false`.
3. Under `if: inputs.playwright_chromium`, build the caller in Release with `NUGET_AUTH_TOKEN` present, locate the generated .NET Playwright script, and invoke its literal `install --with-deps chromium` command.
4. The Test step uses the existing coverage arguments plus `--no-build -m:1` when Chromium is enabled. This consumes the Release build rather than compiling the solution twice and serializes test projects on the runner. The non-Chromium branch retains the current command.
5. Set the Test step timeout to 20 minutes and the job timeout to 30 minutes.
6. Immediately after Test, upload diagnostics only under `if: failure() && inputs.playwright_chromium`, before coverage steps are skipped. Missing diagnostic files do not mask the original test failure.

The boolean is used only in workflow `if:` expressions; no caller-controlled string enters `run:`. Future engines require a separate, deliberate workflow edit. Browser binaries are not cached: the Playwright package/browser version pair is the reproducible unit, and cache restoration cost is comparable to downloading the browser.

Browser tests remain ordinary xUnit tests under the normal `dotnet test` entry point. There is no Node toolchain or second test runner.

## 9. Test tiers and future expansion

This work establishes three distinct tiers:

1. **Unit/component/service tests:** behavior, validation, repository/EF mapping, migrations, and provider-specific correctness.
2. **Required Chromium runtime smoke:** Yggdrasil's two released browser compositions and representative infrastructure paths specified here.
3. **Deferred on-demand black-box suite:** containerized production-like dependencies, full user workflows, and a separately triggered browser matrix.

Firefox or WebKit does not join the pull-request gate merely because Playwright supports it. An observed compatibility need may add an engine to the later on-demand workflow without multiplying normal pull-request feedback time.

The black-box suite is intentionally not designed here. Its trigger, environment ownership, data lifecycle, browser matrix, and workflow inventory require their own design when full-flow automation becomes a platform priority.

## 10. Acceptance criteria

Implementation is complete only when all of the following are true:

1. Bragi pins BlazingStory preview.91, lands the guarded submit/readiness behavior, releases, and Yggdrasil pins that release before its smoke is enabled.
2. A clean CI runner installs only Chromium through the boolean reusable-workflow capability.
3. Both browser tests launch their real host entry point over Kestrel on port zero and obtain the browser origin from `CreateClient().BaseAddress`.
4. Browser execution is serialized within assemblies, across local test processes, and across CI test modules.
5. Yggdrasil waits for an interactive WebAssembly renderer before clicking and observes the resulting browser-originated gRPC-Web request.
6. The successful `US` lookup traverses the real handler/pipeline/interceptors over the deterministic repository and renders the expected response plus client-baked identifier match.
7. Bragi discovers at least 20 story states across both required roots and visits every discovered state in one browser page.
8. Every state renders a non-empty component; every driven state reaches the driver-complete marker.
9. Every live canvas remains an `iframe.html` story/docs document with no nested catalog, while preview.91's legitimate pool stays within its six-canvas bound.
10. Browser, frame, console, network, classification, and timeout failures fail without retry.
11. The known stories stylesheet redirect is asserted narrowly; no other first-party redirect is accepted.
12. A failed run uploads sufficient trace/log/frame evidence to identify the host, route or state, and runtime topology.
13. A successful run produces no Playwright artifact upload.
