# Browser Runtime Smoke Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development`
> (recommended) or `superpowers:executing-plans` to execute this plan task by task. Use
> `superpowers:test-driven-development` for every behavior change and
> `superpowers:verification-before-completion` before claiming any train complete.

**Goal:** Add a required Chromium canary that boots Yggdrasil's real web and story hosts, proves an
InteractiveAuto WebAssembly gRPC-Web round trip, and renders every released Bragi story state without
recursive catalog startup.

**Architecture:** Ginnungagap supplies an opt-in reusable-workflow capability; Bragi releases the
guarded story-driver lifecycle against an exact BlazingStory version; Yggdrasil pins that release and
owns two Playwright tests in its existing host test assemblies. The tests share linked-source browser
infrastructure, launch real Kestrel listeners through `WebApplicationFactory`, serialize Chromium
with an OS file lease, and retain evidence only on failure.

**Tech Stack:** .NET 11 preview 6, C# 15, xUnit v3/Microsoft Testing Platform v2, bUnit,
NSubstitute, Shouldly, `Microsoft.Playwright` 1.61.0, ASP.NET Core
`WebApplicationFactory<TEntryPoint>.UseKestrel(0)`, GitHub Actions, PowerShell.

**Approved design:** `../specs/2026-08-08-browser-runtime-smoke-gate-design.md`

## Global Constraints

- Execute from the Bifrost root. Do not commit or push; stage each realm's completed, verified train
  for the human owner.
- Preserve unrelated dirty work. In particular, Bragi's current capture-phase submit guard is user
  work already in progress; build on it and never replace the file wholesale.
- Keep browser code in the two existing server test assemblies. Do not create a combined test
  project, a Node test runner, a Bragi host, or a database-backed browser fixture.
- Chromium is the only installed/launched engine. Do not add retries, HTTPS certificate bypasses,
  broad console allowlists, or browser caching.
- This estate runs xUnit v3 on MTP. Every focused command passes xUnit arguments after `--` and uses
  `--filter-class`/`--filter-not-class`; VSTest `dotnet test --filter` syntax is forbidden.
- The successful-country seam replaces only `IReadRepository<CountryOrAreaView>` after normal host
  composition. It must not replace a DbContext, factory, handler, gRPC client, service, pipeline, or
  interceptor.
- The story invariant is structural: `/iframe.html`, `data-bs-parent-frame`, no nested catalog, and
  at most six live canvases. Cumulative WASM bootstrap count is diagnostics only. The sweep must
  observe at least seven driver markers across the catalog before it can pass.
- The 90-second startup and 15-second per-state budgets are nested beneath—not summed into—the
  five-minute test ceiling. If the outer ceiling fires without an inner timeout, evidence must say
  exactly `Aggregate browser-test ceiling expired; no per-state timeout was exceeded.`
- Every Playwright web-first assertion supplies the relevant 90-second startup or 15-second
  operation/state timeout explicitly; none may inherit Playwright's five-second assertion default.
- `-m:1` under MTP is a hypothesis until the first Chromium CI run proves it. The gate is not trusted
  until project start/end output demonstrates non-overlapping test modules. If it does not, replace
  the Chromium branch with explicit per-project sequential `dotnet test` invocations; do not rely on
  the cross-process lease to hide CI scheduling.
- Follow `Glitnir/docs/house-rules.md`: US English, tabs in C#/Razor, xUnit underscore names,
  cancellation for async work, no mocked-database claims, no warning suppressions without a written
  explanation, and no generated-file edits.

---

## Train 1 — Ginnungagap: reusable Chromium capability

### Task 1: Lock the workflow contract with a failing verification script

**Files:**

- Create: `../.github/scripts/tests/verify-ci-build-test-playwright.ps1`
- Test: `../.github/.github/workflows/ci-build-test.yml`

- [ ] Write a repository-owned contract test that reads the workflow as raw text and fails unless it
  contains the boolean input/default, gated Release build, literal Chromium install, two test
  branches, timeouts, and compound artifact condition. It must also reject browser input
  interpolation inside any `run:` body.

```powershell
$ErrorActionPreference = 'Stop'
$workflowPath = Join-Path $PSScriptRoot '../../.github/workflows/ci-build-test.yml'
$workflow = Get-Content $workflowPath -Raw

$required = @(
	'playwright_chromium:',
	'type: boolean',
	'default: false',
	'if: inputs.playwright_chromium',
	'dotnet build -c Release',
	'install --with-deps chromium',
	'--no-build -m:1',
	'timeout-minutes: 20',
	'timeout-minutes: 30',
	'actions/upload-artifact@v7',
	"if: failure() && inputs.playwright_chromium"
)

foreach ($fragment in $required) {
	if (-not $workflow.Contains($fragment, [StringComparison]::Ordinal)) {
		throw "ci-build-test.yml is missing required Playwright contract: $fragment"
	}
}

$runBlocks = [regex]::Matches($workflow, '(?ms)^\s+run:\s*(?:\|\s*\r?\n(?:(?:\s{8,}.*)?\r?\n)+|[^\r\n]+)')
foreach ($runBlock in $runBlocks) {
	if ($runBlock.Value.Contains('${{ inputs.playwright_chromium }}', [StringComparison]::Ordinal)) {
		throw 'playwright_chromium may appear in workflow if-expressions, never in run scripts.'
	}
}
```

- [ ] Run the test and confirm the expected RED lists the missing `playwright_chromium` contract.

```bash
cd ../.github
pwsh -NoProfile -File ./scripts/tests/verify-ci-build-test-playwright.ps1
```

Expected: non-zero exit, first missing-fragment message names `playwright_chromium:`.

### Task 2: Implement the opt-in reusable-workflow branch

**Files:**

- Modify: `../.github/.github/workflows/ci-build-test.yml`
- Test: `../.github/scripts/tests/verify-ci-build-test-playwright.ps1`

- [ ] Add the workflow-call input and job ceiling.

```yaml
      playwright_chromium:
        description: 'Build first, install Playwright Chromium, and run test modules sequentially'
        type: boolean
        default: false

jobs:
  build:
    timeout-minutes: 30
```

- [ ] Insert a gated Release build and literal Playwright installation after coverage settings. The
  script path is discovered from build output; the browser command itself remains literal.

```yaml
      - name: Build for Playwright
        if: inputs.playwright_chromium
        env:
          NUGET_AUTH_TOKEN: ${{ secrets.PACKAGES_READ_TOKEN }}
        run: dotnet build -c Release

      - name: Install Playwright Chromium
        if: inputs.playwright_chromium
        shell: pwsh
        run: |
          $script = Get-ChildItem -Path . -Filter playwright.ps1 -Recurse |
            Where-Object FullName -Match '[\\/]bin[\\/]Release[\\/]' |
            Select-Object -First 1
          if ($null -eq $script) { throw 'Release build produced no playwright.ps1.' }
          & $script.FullName install --with-deps chromium
          if ($LASTEXITCODE -ne 0) { throw "Playwright install exited $LASTEXITCODE." }
```

- [ ] Split Test into mutually exclusive branches. Preserve the existing coverage command byte for
  byte in the non-browser branch; add only `--no-build -m:1` to the Chromium branch. Give both a
  20-minute step ceiling and the existing NuGet token.

```yaml
      - name: Test
        if: ${{ !inputs.playwright_chromium }}
        timeout-minutes: 20
        env:
          NUGET_AUTH_TOKEN: ${{ secrets.PACKAGES_READ_TOKEN }}
        run: dotnet test -c Release --coverage --coverage-output-format cobertura --coverage-settings coverage-settings.xml

      - name: Test with Chromium
        if: inputs.playwright_chromium
        timeout-minutes: 20
        env:
          NUGET_AUTH_TOKEN: ${{ secrets.PACKAGES_READ_TOKEN }}
        run: dotnet test -c Release --no-build -m:1 --coverage --coverage-output-format cobertura --coverage-settings coverage-settings.xml --tl:off --verbosity normal
```

- [ ] Upload failure evidence before coverage report generation. Missing files must not replace the
  original test failure.

```yaml
      - name: Upload Playwright failure evidence
        if: failure() && inputs.playwright_chromium
        uses: actions/upload-artifact@v7
        with:
          name: playwright-failure-evidence
          path: '**/TestResults/playwright/**'
          if-no-files-found: ignore
          retention-days: 7
```

- [ ] Run the contract test and the repository's existing rune-lineage test.

```bash
cd ../.github
pwsh -NoProfile -File ./scripts/tests/verify-ci-build-test-playwright.ps1
pwsh -NoProfile -File ./scripts/tests/verify-rune-lineage.ps1
git diff --check
git add .github/workflows/ci-build-test.yml scripts/tests/verify-ci-build-test-playwright.ps1
```

Expected: both scripts exit zero; staged diff contains no caller-specific Yggdrasil logic.

### Human gate G1

Ginnungagap must be reviewed and merged before Yggdrasil passes the new input to `@master`. Record the
merged workflow revision in the execution notes. Do not start Yggdrasil CI wiring against an input the
published reusable workflow does not yet accept.

The 30-minute job and 20-minute Test-step ceilings deliberately apply to all reusable-workflow
callers, including the 11 callers that leave `playwright_chromium: false`. G1 review records recent
duration evidence for those callers and confirms each remains below both ceilings; the timeouts are
an estate-wide hung-job safety bound, not a Yggdrasil-only side effect.

---

## Train 2 — Bragi: guarded driver lifecycle and exact canvas dependency

### Task 3: Specify the driver readiness lifecycle in bUnit

**Files:**

- Modify: `Bragi/tests/DesignSystem.Stories.Tests/Scenarios/StoryDriverTests.cs`
- Modify later: `Bragi/src/DesignSystem.Stories/Scenarios/StoryDriver.razor`

- [ ] Add a test that asserts the layout-neutral wrapper and completion transition after the JS
  module reports a settled submit.

```csharp
[Fact]
void A_successfully_driven_story_reports_complete_on_a_layout_neutral_wrapper()
{
	var module = JSInterop.SetupModule("./_content/Norse.DesignSystem.Stories/storyDriver.js");
	module.Setup<bool>("drive", true, "taken@example.com", "aaaaaaaa").SetResult(true);

	var component = Render<StoryDriver>(parameters =>
		parameters
			.Add(p => p.Mode, StoryDriverMode.FillAndSubmit)
			.Add(p => p.Email, "taken@example.com")
			.AddChildContent("scenario"));

	var marker = component.Find("[data-norse-story-driver-state]");
	marker.GetAttribute("style").ShouldBe("display: contents;");
	marker.GetAttribute("data-norse-story-driver-state").ShouldBe("complete");
	marker.TextContent.ShouldContain("scenario");
}
```

- [ ] Add the negative lifecycle test. Keep the existing invocation and unspecified-mode tests.

```csharp
[Fact]
void A_driver_that_finds_no_form_throws()
{
	var module = JSInterop.SetupModule("./_content/Norse.DesignSystem.Stories/storyDriver.js");
	module.Setup<bool>("drive", false, "designer@example.com", "aaaaaaaa").SetResult(false);

	Should.Throw<InvalidOperationException>(() =>
		Render<StoryDriver>(parameters => parameters.Add(p => p.Mode, StoryDriverMode.SubmitOnly)))
		.Message.ShouldBe("StoryDriver found no form to drive.");
}
```

- [ ] Add a distinct test for JavaScript settlement failure so it cannot be conflated with the
  `drive` method's `false` no-form result.

```csharp
[Fact]
void A_driver_settlement_failure_surfaces_the_javascript_exception()
{
	var module = JSInterop.SetupModule("./_content/Norse.DesignSystem.Stories/storyDriver.js");
	module
		.Setup<bool>("drive", true, "designer@example.com", "aaaaaaaa")
		.SetException(new JSException("StoryDriver observed no settled post-submit DOM activity."));

	Should.Throw<JSException>(() =>
		Render<StoryDriver>(parameters => parameters.Add(p => p.Mode, StoryDriverMode.FillAndSubmit)))
		.Message.ShouldContain("no settled post-submit DOM activity");
}
```

- [ ] Run only these tests and confirm RED because the wrapper selector is missing. The no-form and
  JS-settlement tests must already distinguish their two failure paths; neither is manufactured as
  the RED for the readiness-marker change.

```bash
dotnet test Bragi/tests/DesignSystem.Stories.Tests/DesignSystem.Stories.Tests.csproj -- --filter-class "*.StoryDriverTests"
```

### Task 4: Complete the guarded submit and readiness marker

**Files:**

- Modify: `Bragi/src/DesignSystem.Stories/wwwroot/storyDriver.js`
- Modify: `Bragi/src/DesignSystem.Stories/Scenarios/StoryDriver.razor`
- Modify: `Bragi/src/DesignSystem.Stories/DesignSystem.Stories.csproj`
- Test: `Bragi/tests/DesignSystem.Stories.Tests/Scenarios/StoryDriverTests.cs`

- [ ] Preserve the user's existing capture-phase guard and add bounded post-submit observation.
  Start the observer before `requestSubmit`. SubmitOnly may settle from synchronous validation DOM
  activity; FillAndSubmit must observe a mutation after the submit stack's microtask barrier because
  those states traverse an asynchronous service method. In both modes require at least 500 ms since
  submit and 250 ms since the last qualifying mutation; restart the quiet window after every
  qualifying mutation and reject after five seconds.

```javascript
const maxTries = 40;
const delayMs = 50;
const minimumSettleMs = 500;
const quietMs = 250;
const settleTimeoutMs = 5000;

function waitForPostSubmitSettle(root, requirePostSubmitTurn) {
    let barrierPassed = !requirePostSubmitTurn;
    let observed = false;
    let quietTimer;
    let submittedAt = performance.now();
    let lastMutationAt = submittedAt;
    let resolveCompletion;
    let rejectCompletion;

    const completion = new Promise((resolve, reject) => {
        resolveCompletion = resolve;
        rejectCompletion = reject;
    });

    const finish = () => {
        const now = performance.now();
        const minimumRemaining = minimumSettleMs - (now - submittedAt);
        const quietRemaining = quietMs - (now - lastMutationAt);
        if (!observed || minimumRemaining > 0 || quietRemaining > 0) {
            quietTimer = setTimeout(finish, Math.max(minimumRemaining, quietRemaining, quietMs));
            return;
        }

        clearTimeout(timeout);
        observer.disconnect();
        resolveCompletion();
    };

    const observer = new MutationObserver(() => {
        if (!barrierPassed)
            return;

        observed = true;
        lastMutationAt = performance.now();
        clearTimeout(quietTimer);
        quietTimer = setTimeout(finish, quietMs);
    });

    const timeout = setTimeout(() => {
        clearTimeout(quietTimer);
        observer.disconnect();
        rejectCompletion(new Error('StoryDriver observed no settled post-submit DOM activity.'));
    }, settleTimeoutMs);

    observer.observe(root, { attributes: true, characterData: true, childList: true, subtree: true });

    return {
        completion,
        markSubmitted() {
            submittedAt = performance.now();
            lastMutationAt = submittedAt;
            if (requirePostSubmitTurn)
                queueMicrotask(() => barrierPassed = true);
        }
    };
}
```

Use it at the existing submit site:

```javascript
const settled = waitForPostSubmitSettle(document.body, fill);
form.addEventListener('submit', event => event.preventDefault(), { capture: true, once: true });
form.requestSubmit();
settled.markSubmitted();
await settled.completion;
return true;
```

- [ ] Render the lifecycle marker and transition it only after the JavaScript promise has settled.

```razor
<div style="display: contents;" data-norse-story-driver-state="@_state">
	@ChildContent
</div>

@code {
	IJSObjectReference? _module;
	string _state = "pending";

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (!firstRender)
			return;
		if (Mode is StoryDriverMode.Unspecified)
			throw new InvalidOperationException("StoryDriver requires an explicit mode.");

		_module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Norse.DesignSystem.Stories/storyDriver.js");
		var driven = await _module.InvokeAsync<bool>("drive", Mode is StoryDriverMode.FillAndSubmit, Email, Password);
		if (!driven)
			throw new InvalidOperationException("StoryDriver found no form to drive.");

		_state = "complete";
		StateHasChanged();
	}
}
```

Retain the existing `Mode`, `Email`, `Password`, and `ChildContent` parameters and the existing
`DisposeAsync` block verbatim around this changed render fragment and lifecycle method.

- [ ] Replace the floating BlazingStory reference with the exact audited preview.

```xml
<PackageReference Include="BlazingStory" Version="1.0.0-preview.91" />
```

- [ ] Run focused and full Bragi verification.

```bash
dotnet test Bragi/tests/DesignSystem.Stories.Tests/DesignSystem.Stories.Tests.csproj -- --filter-class "*.StoryDriverTests"
dotnet test Bragi/Bragi.slnx
git -C Bragi diff --check
git -C Bragi add src/DesignSystem.Stories/DesignSystem.Stories.csproj src/DesignSystem.Stories/Scenarios/StoryDriver.razor src/DesignSystem.Stories/wwwroot/storyDriver.js tests/DesignSystem.Stories.Tests/Scenarios/StoryDriverTests.cs
```

Expected: all tests pass; staged JS diff still contains the one-shot capture listener; package version
is exact, not `1.*-*`.

### Human gate B1

Review, merge, tag, and publish Bragi. Record the released `Norse.DesignSystem.Stories` version. Do not
enable the Yggdrasil story smoke against 0.0.6. Update Yggdrasil's `<BragiVersion>` through the normal
Gjallarhorn/release process or a reviewed CPM edit, then restore in standalone/package mode and prove
the released package contains both the marker and guarded JavaScript asset.

---

## Train 3 — Yggdrasil: shared browser chassis

### Task 5: Add deterministic pre-boot environment setup

**Files:**

- Create: `Yggdrasil/tests/Hosting.Web.Server.Tests/TestHostEnvironment.cs`
- Modify: `Yggdrasil/tests/Hosting.Web.Server.Tests/CompositionTests.cs`

- [ ] Move the two process-global connection strings out of `CompositionTests`' static constructor
  and into one module initializer.

```csharp
using System.Runtime.CompilerServices;

namespace Norse.Hosting.Web.Server.Tests;

static class TestHostEnvironment
{
	[ModuleInitializer]
	internal static void Initialize()
	{
		Environment.SetEnvironmentVariable(
			"ConnectionStrings__norse_identity",
			"Host=localhost;Database=norse_identity_composition_tests;Username=test;Password=test");
		Environment.SetEnvironmentVariable(
			"ConnectionStrings__norse_reference",
			"Host=localhost;Database=norse_reference_composition_tests;Username=test;Password=test");
	}
}
```

- [ ] Add a composition assertion for the identical assembly-wide values, then remove the old static
  constructor and update its comments to name `TestHostEnvironment`.

```csharp
[Fact]
void Test_host_connection_strings_exist_before_factory_boot()
{
	Environment.GetEnvironmentVariable("ConnectionStrings__norse_identity").ShouldNotBeNullOrWhiteSpace();
	Environment.GetEnvironmentVariable("ConnectionStrings__norse_reference").ShouldNotBeNullOrWhiteSpace();
}
```

- [ ] Run the composition class before proceeding. If CA2255 fires, put one justified
  `SuppressMessage` on this test-only initializer; do not add a realm-wide `NoWarn`.

```bash
dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj -- --filter-class "*.CompositionTests"
```

### Task 6: Build and test the cross-process browser lease

**Files:**

- Create: `Yggdrasil/tests/BrowserTesting/BrowserTimeouts.cs`
- Create: `Yggdrasil/tests/BrowserTesting/BrowserProcessLease.cs`
- Create: `Yggdrasil/tests/Hosting.Stories.Server.Tests/BrowserProcessLeaseTests.cs`
- Modify: `Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj`
- Modify: `Yggdrasil/tests/Hosting.Stories.Server.Tests/Hosting.Stories.Server.Tests.csproj`

- [ ] Pin the library package in both test projects and link shared sources rather than inventing a
  third assembly.

```xml
<PackageReference Include="Microsoft.Playwright" Version="1.61.0" />
<Compile Include="../BrowserTesting/*.cs" Link="BrowserTesting/%(Filename)%(Extension)" />
```

- [ ] Define one source of time budgets.

```csharp
namespace Norse.Hosting.BrowserTesting;

static class BrowserTimeouts
{
	internal static readonly TimeSpan HostStartup = TimeSpan.FromSeconds(90);
	internal static readonly TimeSpan BrowserOperation = TimeSpan.FromSeconds(15);
	internal static readonly TimeSpan StoryState = TimeSpan.FromSeconds(15);
	internal static readonly TimeSpan Test = TimeSpan.FromMinutes(5);
	internal const float PlaywrightHostStartupMilliseconds = 90_000;
	internal const float PlaywrightOperationMilliseconds = 15_000;
	internal const float PlaywrightStoryStateMilliseconds = 15_000;
}
```

- [ ] First write a lease test that holds one lease, proves a second waiter cannot enter and reports
  the owning PID/lease-wait phase, then releases the first and proves the waiter succeeds. Confirm
  RED because the types do not exist.

```csharp
[Fact]
async Task A_second_browser_process_waits_until_the_first_lease_releases()
{
	var first = await BrowserProcessLease.AcquireAsync(TestContext.Current.CancellationToken);
	try
	{
		using var blocked = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
		var exception = await Should.ThrowAsync<BrowserLeaseWaitException>(() =>
			BrowserProcessLease.AcquireAsync(blocked.Token).AsTask());
		exception.Message.ShouldContain("Browser lease");
		exception.Message.ShouldContain($"pid={Environment.ProcessId}");
	}
	finally
	{
		await first.DisposeAsync();
	}

	await using var second = await BrowserProcessLease.AcquireAsync(TestContext.Current.CancellationToken);
	second.ShouldNotBeNull();
}
```

- [ ] Implement a bounded exclusive file handle at
  `Path.Combine(Path.GetTempPath(), "norse-yggdrasil-playwright.lock")`. Use `FileShare.None`, retry
  only `IOException`, and write the current owner to the companion
  `norse-yggdrasil-playwright.lock.owner` file while the lock is held. On cancellation during the
  retry loop, translate to `BrowserLeaseWaitException` with elapsed time and the last owner record;
  cancellation before the first contention remains ordinary cancellation. Emit acquired/released
  UTC timestamps plus process ID to test output for the first CI serialization audit.

```csharp
using System.Diagnostics;

namespace Norse.Hosting.BrowserTesting;

sealed class BrowserLeaseWaitException(TimeSpan elapsed, string owner, Exception innerException) :
	TimeoutException($"Browser lease phase ended after waiting {elapsed.TotalSeconds:F1}s; holder {owner}.", innerException);

sealed class BrowserProcessLease(FileStream stream, string ownerPath) : IAsyncDisposable
{
	const string FileName = "norse-yggdrasil-playwright.lock";
	const string UnknownOwner = "unknown";

	internal static async ValueTask<BrowserProcessLease> AcquireAsync(CancellationToken cancellationToken)
	{
		var path = Path.Combine(Path.GetTempPath(), FileName);
		var ownerPath = $"{path}.owner";
		var wait = Stopwatch.StartNew();
		var contended = false;
		var owner = UnknownOwner;
		while (true)
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
			}
			catch (OperationCanceledException exception) when (contended)
			{
				throw new BrowserLeaseWaitException(wait.Elapsed, owner, exception);
			}

			FileStream stream;
			try
			{
				stream = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			}
			catch (IOException)
			{
				contended = true;
				owner = ReadOwner(ownerPath);
				try
				{
					await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
				}
				catch (OperationCanceledException exception)
				{
					throw new BrowserLeaseWaitException(wait.Elapsed, owner, exception);
				}
				continue;
			}

			try
			{
				owner = $"pid={Environment.ProcessId}, acquiredUtc={DateTimeOffset.UtcNow:O}";
				await File.WriteAllTextAsync(ownerPath, owner, cancellationToken);
				Console.WriteLine($"Browser lease acquired: {owner}");
				return new(stream, ownerPath);
			}
			catch
			{
				await stream.DisposeAsync();
				throw;
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		try
		{
			File.Delete(ownerPath);
		}
		finally
		{
			await stream.DisposeAsync();
			Console.WriteLine($"Browser lease released: pid={Environment.ProcessId}, utc={DateTimeOffset.UtcNow:O}");
		}
	}

	static string ReadOwner(string path)
	{
		try
		{
			return File.ReadAllText(path);
		}
		catch (IOException)
		{
			return UnknownOwner;
		}
	}
}
```

- [ ] Run RED, implement, then run GREEN.

```bash
dotnet test Yggdrasil/tests/Hosting.Stories.Server.Tests/Hosting.Stories.Server.Tests.csproj -- --filter-class "*.BrowserProcessLeaseTests"
```

### Task 7: Add the Kestrel/Chromium fixture and failure evidence collector

**Files:**

- Create: `Yggdrasil/tests/BrowserTesting/BrowserHostFixture.cs`
- Create: `Yggdrasil/tests/BrowserTesting/BrowserEvidence.cs`
- Create: `Yggdrasil/tests/BrowserTesting/BrowserFailure.cs`
- Create: `Yggdrasil/tests/BrowserTesting/BrowserPhaseRunner.cs`
- Create: `Yggdrasil/tests/BrowserTesting/FrameworkRequestQuiescence.cs`
- Create: `Yggdrasil/tests/BrowserTesting/BrowserServerLogProvider.cs`
- Create: `Yggdrasil/tests/Hosting.Stories.Server.Tests/BrowserHostFixtureTests.cs`
- Create: `Yggdrasil/tests/Hosting.Stories.Server.Tests/BrowserTimeoutClassificationTests.cs`

- [ ] Write a fixture contract test using `Hosting.Stories.Server.Program`: initialize, assert
  `Origin.Scheme == "http"`, assert a non-default port, assert `.Server` is never touched, open a
  page, and get `/` successfully. Confirm RED before adding the shared types. Build once and install
  the package-matched local Chromium before the first fixture run:

```bash
dotnet build Yggdrasil/tests/Hosting.Stories.Server.Tests/Hosting.Stories.Server.Tests.csproj
pwsh Yggdrasil/tests/Hosting.Stories.Server.Tests/bin/Debug/net11.0/playwright.ps1 install chromium
```

- [ ] Implement `BrowserHostFixture<TEntryPoint>` as an xUnit `IAsyncLifetime`. The order is
  load-bearing: start the five-minute aggregate token, acquire the lease, configure factory, call
  `UseKestrel(0)`, create a client, capture `BaseAddress`, then create Chromium. Do not derive from a
  Playwright test base class.

```csharp
abstract class BrowserHostFixture<TEntryPoint> : IAsyncLifetime where TEntryPoint : class
{
	readonly CancellationTokenSource _aggregate = new(BrowserTimeouts.Test);
	BrowserProcessLease? _lease;
	WebApplicationFactory<TEntryPoint>? _factory;
	IPlaywright? _playwright;
	IBrowser? _browser;

	internal Uri Origin { get; private set; } = null!;

	protected virtual void ConfigureWebHost(IWebHostBuilder builder) { }

	public async ValueTask InitializeAsync()
	{
		try
		{
			_lease = await BrowserProcessLease.AcquireAsync(_aggregate.Token);
		}
		catch (BrowserLeaseWaitException exception)
		{
			var host = typeof(TEntryPoint).Assembly.GetName().Name ??
				throw new InvalidOperationException("Host entry-point assembly has no name.");
			throw BrowserFailure.WriteStartupFailure(host, "browser lease", exception);
		}
		_factory = new ConfigurableFactory<TEntryPoint>(ConfigureWebHost);
		_factory.UseKestrel(0);

		using var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
		Origin = client.BaseAddress ?? throw new InvalidOperationException("Kestrel exposed no origin.");

		_playwright = await Playwright.CreateAsync();
		_browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
	}

	internal async Task<BrowserEvidence> OpenEvidenceAsync(string testName)
	{
		var context = await _browser!.NewContextAsync(new()
		{
			BaseURL = Origin.AbsoluteUri,
			IgnoreHTTPSErrors = false,
		});
		return await BrowserEvidence.StartAsync(context, testName, Origin, _aggregate.Token);
	}

	public async ValueTask DisposeAsync()
	{
		if (_browser is not null)
			await _browser.DisposeAsync();
		_playwright?.Dispose();
		if (_factory is not null)
			await _factory.DisposeAsync();
		if (_lease is not null)
			await _lease.DisposeAsync();
		_aggregate.Dispose();
	}
}
```

`ConfigurableFactory<TEntryPoint>` is sealed and overrides `ConfigureWebHost` only to call the
supplied delegate. It also registers one `BrowserServerLogProvider` backed by a thread-safe queue;
`BrowserEvidence` receives that queue and writes the ordered entries to `server.log` on failure.
`WebApplicationFactory.Server` does not appear anywhere in this folder.

`BrowserFailure.WriteStartupFailure` creates
`<AppContext.BaseDirectory>/TestResults/playwright/<host-assembly>/startup.log` before Chromium
exists. `typeof(TEntryPoint).Assembly.GetName().Name` yields `Norse.Hosting.Web.Server` or
`Norse.Hosting.Stories.Server`; neither suite writes to an ambiguous `Program` directory. For a
lease failure the file records phase `browser lease`, elapsed wait, and owner PID/acquisition UTC
from `BrowserLeaseWaitException`; it must not emit the aggregate/no-state-overrun sentence because a
named inner phase is known.

```csharp
sealed class BrowserFailure(string message, Exception? innerException = null) :
	Exception(message, innerException)
{
	internal static string ArtifactRoot { get; } =
		Path.Combine(AppContext.BaseDirectory, "TestResults", "playwright");

	internal static BrowserFailure AggregateTimeout() =>
		new("Aggregate browser-test ceiling expired; no per-state timeout was exceeded.");

	internal static BrowserFailure AggregateTimeoutDuringPhase(
		string phase,
		TimeSpan elapsed,
		TimeSpan phaseBudget,
		bool phaseBudgetExpired,
		Exception exception)
	{
		var phaseVerdict = phaseBudgetExpired
			? $"phase budget {phaseBudget.TotalSeconds:F1}s also expired before cancellation was observed"
			: $"phase budget {phaseBudget.TotalSeconds:F1}s was not exceeded";
		return new(
			$"Aggregate browser-test ceiling expired during phase '{phase}' after {elapsed.TotalSeconds:F1}s; {phaseVerdict}.",
			exception);
	}

	internal static BrowserFailure PhaseTimeout(
		string phase,
		TimeSpan elapsed,
		TimeSpan budget,
		Exception exception) =>
		new(
			$"Browser phase '{phase}' timed out after {elapsed.TotalSeconds:F1}s (budget {budget.TotalSeconds:F1}s).",
			exception);

	internal static BrowserFailure WriteStartupFailure(
		string host,
		string phase,
		Exception exception)
	{
		var directory = Path.Combine(ArtifactRoot, host);
		Directory.CreateDirectory(directory);
		var message = $"Browser startup phase '{phase}' failed: {exception.Message}";
		File.WriteAllText(Path.Combine(directory, "startup.log"), message);
		return new(message, exception);
	}
}
```

- [ ] Implement `BrowserEvidence` around a fresh context/page. Start tracing with screenshots,
  snapshots, and sources before creating/navigating the page. Subscribe to `Console`, `PageError`,
  `RequestFailed`, `Response`, `FrameAttached`, and `FrameNavigated`. Record only; assert after the
  operation so the callback never throws on Playwright's event thread.

```csharp
await context.Tracing.StartAsync(new()
{
	Screenshots = true,
	Snapshots = true,
	Sources = true,
});
```

`CompleteAsync()` checks collected page errors, error-severity console entries, failed first-party
requests, unexpected first-party 4xx/5xx, and redirects. It accepts a host-specific exact redirect
predicate. On success it calls `Tracing.StopAsync()` with no path and disposes the context. On failure
it creates `Path.Combine(BrowserFailure.ArtifactRoot, testName)`, writes `trace.zip`,
`page.png`, `browser.log`, `network.log`, and `frames.log`, then throws one `BrowserFailure`
containing that directory. Its `DisposeAsync` treats a missing successful `CompleteAsync()` as a
failure path, so an assertion thrown mid-test still flushes evidence before closing the context.
No evidence path is derived from `Environment.CurrentDirectory`; the workflow's recursive
`**/TestResults/playwright/**` glob therefore finds artifacts beneath each test assembly output
regardless of the runner's working directory.

- [ ] Implement `BrowserPhaseRunner` and have `BrowserEvidence.RunPhaseAsync(string phase,
  TimeSpan budget, Func<CancellationToken, Task> action)` delegate to it. Keep the aggregate token and
  the phase-budget source separate, then link only the token passed to the action. On cancellation,
  test the aggregate token first: aggregate expiry during a phase is not a phase-budget overrun. The
  standalone aggregate sentence remains reserved for expiry while no phase is active.

```csharp
sealed class BrowserPhaseRunner(CancellationToken aggregateToken)
{
	internal string? CurrentPhase { get; private set; }
	internal string? TimedOutPhase { get; private set; }

	internal async Task RunAsync(
		string phase,
		TimeSpan budget,
		Func<CancellationToken, Task> action)
	{
		using var phaseBudget = new CancellationTokenSource(budget);
		using var actionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
			aggregateToken,
			phaseBudget.Token);
		var elapsed = Stopwatch.StartNew();
		CurrentPhase = phase;
		try
		{
			await action(actionCancellation.Token);
		}
		catch (OperationCanceledException exception) when (actionCancellation.IsCancellationRequested)
		{
			if (aggregateToken.IsCancellationRequested)
				throw BrowserFailure.AggregateTimeoutDuringPhase(
					phase,
					elapsed.Elapsed,
					budget,
					phaseBudget.IsCancellationRequested,
					exception);

			TimedOutPhase = phase;
			throw BrowserFailure.PhaseTimeout(phase, elapsed.Elapsed, budget, exception);
		}
		finally
		{
			CurrentPhase = null;
		}
	}

	internal void ThrowIfAggregateExpired()
	{
		if (aggregateToken.IsCancellationRequested)
			throw BrowserFailure.AggregateTimeout();
	}
}
```

`BrowserEvidence.StartAsync` constructs one `BrowserPhaseRunner` from the aggregate token and stores
it in `_phaseRunner`. The test-facing method checks for aggregate expiry between phases before
delegating; `CompleteAsync` performs the same check before declaring success. No classification
branch is duplicated in `BrowserEvidence`:

```csharp
internal async Task RunPhaseAsync(
	string phase,
	TimeSpan budget,
	Func<CancellationToken, Task> action)
{
	_phaseRunner.ThrowIfAggregateExpired();
	await _phaseRunner.RunAsync(phase, budget, action);
}
```

- [ ] Implement `FrameworkRequestQuiescence.WaitAsync(IPage page, Uri origin,
  CancellationToken cancellationToken)`. Subscribe before navigation to `Request`,
  `RequestFinished`, and `RequestFailed`; track only same-origin paths beneath `/_framework/`; require
  at least one successful `.wasm` response, zero in-flight framework requests, and 500 ms with no new
  framework activity. Always unsubscribe in `finally`. This is the InteractiveAuto warm-cache
  precondition; Playwright's generic network-idle state is not used because the prerendered host may
  maintain non-framework connections.

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Playwright;

static class FrameworkRequestQuiescence
{
	internal static async Task WaitAsync(IPage page, Uri origin, CancellationToken cancellationToken)
	{
		ConcurrentDictionary<IRequest, byte> pending = new();
		var sawSuccessfulWasm = 0;
		var lastActivity = Stopwatch.GetTimestamp();

		bool IsFramework(string url)
		{
			var uri = new Uri(url);
			return uri.GetLeftPart(UriPartial.Authority) == origin.GetLeftPart(UriPartial.Authority) &&
				uri.AbsolutePath.StartsWith("/_framework/", StringComparison.Ordinal);
		}

		void Started(object? _, IRequest request)
		{
			if (!IsFramework(request.Url))
				return;
			pending.TryAdd(request, 0);
			Volatile.Write(ref lastActivity, Stopwatch.GetTimestamp());
		}

		void Finished(object? _, IRequest request)
		{
			if (!pending.TryRemove(request, out _))
				return;
			Volatile.Write(ref lastActivity, Stopwatch.GetTimestamp());
		}

		void Responded(object? _, IResponse response)
		{
			if (!response.Ok || !IsFramework(response.Url) ||
				!new Uri(response.Url).AbsolutePath.EndsWith(".wasm", StringComparison.Ordinal))
				return;
			Interlocked.Exchange(ref sawSuccessfulWasm, 1);
			Volatile.Write(ref lastActivity, Stopwatch.GetTimestamp());
		}

		page.Request += Started;
		page.RequestFinished += Finished;
		page.RequestFailed += Finished;
		page.Response += Responded;
		try
		{
			await page.GotoAsync("/", new()
			{
				WaitUntil = WaitUntilState.DOMContentLoaded,
				Timeout = BrowserTimeouts.PlaywrightHostStartupMilliseconds,
			});
			while (Volatile.Read(ref sawSuccessfulWasm) == 0 || pending.Count != 0 ||
				Stopwatch.GetElapsedTime(Volatile.Read(ref lastActivity)) < TimeSpan.FromMilliseconds(500))
				await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
		}
		finally
		{
			page.Request -= Started;
			page.RequestFinished -= Finished;
			page.RequestFailed -= Finished;
			page.Response -= Responded;
		}
	}
}
```

- [ ] Unit-test all three timeout classifications without Chromium. A phase budget expiring first
  names the phase; the aggregate expiring first reports that it expired during the active phase and
  explicitly says the larger phase budget was not exceeded; aggregate expiry outside a phase uses
  the global no-per-state-overrun sentence. These tests exercise `BrowserPhaseRunner`, not just
  `BrowserFailure` string factories.

```csharp
[Fact]
async Task Phase_budget_expiry_is_classified_as_the_named_phase()
{
	BrowserPhaseRunner runner = new(CancellationToken.None);

	var exception = await Should.ThrowAsync<BrowserFailure>(() => runner.RunAsync(
		"framework warm-up",
		TimeSpan.FromMilliseconds(50),
		cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)));

	exception.Message.ShouldContain("Browser phase 'framework warm-up' timed out");
	exception.Message.ShouldNotContain("Aggregate browser-test ceiling expired");
	runner.TimedOutPhase.ShouldBe("framework warm-up");
}

[Fact]
async Task Aggregate_expiry_during_a_phase_does_not_accuse_the_phase_budget()
{
	using var aggregate = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
	BrowserPhaseRunner runner = new(aggregate.Token);

	var exception = await Should.ThrowAsync<BrowserFailure>(() => runner.RunAsync(
		"framework warm-up",
		TimeSpan.FromSeconds(5),
		cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)));

	exception.Message.ShouldContain("Aggregate browser-test ceiling expired during phase 'framework warm-up'");
	exception.Message.ShouldContain("phase budget 5.0s was not exceeded");
	runner.TimedOutPhase.ShouldBeNull();
}

[Fact]
void Aggregate_expiry_outside_a_phase_names_no_per_state_overrun()
{
	using var aggregate = new CancellationTokenSource();
	aggregate.Cancel();
	BrowserPhaseRunner runner = new(aggregate.Token);

	var exception = Should.Throw<BrowserFailure>(runner.ThrowIfAggregateExpired);

	exception.Message.ShouldBe("Aggregate browser-test ceiling expired; no per-state timeout was exceeded.");
}
```

- [ ] Run fixture and lease tests, then build both assemblies.

```bash
dotnet test Yggdrasil/tests/Hosting.Stories.Server.Tests/Hosting.Stories.Server.Tests.csproj -- --filter-class "*.BrowserProcessLeaseTests" "*.BrowserHostFixtureTests" "*.BrowserTimeoutClassificationTests"
dotnet build Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj
dotnet build Yggdrasil/tests/Hosting.Stories.Server.Tests/Hosting.Stories.Server.Tests.csproj
```

The generated target-framework directory is authoritative; if the SDK emits a more specific TFM,
use that emitted directory rather than changing project configuration.

---

## Train 4 — Yggdrasil Web.Server: InteractiveAuto gRPC-Web proof

### Task 8: Expose and component-test the renderer readiness marker

**Files:**

- Create: `Yggdrasil/tests/Hosting.Web.Components.Tests/CountryLookupTests.cs`
- Modify: `Yggdrasil/src/Hosting.Web.Components/Pages/CountryLookup.razor`

- [ ] Add a bUnit test that registers Fluent UI plus a substitute `IReferenceService`, supplies the
  renderer metadata bUnit requires before any component reads `ComponentBase.RendererInfo`, renders
  `CountryLookup`, and asserts the exact deterministic values.

```csharp
using Microsoft.AspNetCore.Components;

[Fact]
void Probe_exposes_renderer_name_and_an_explicit_lowercase_interactivity_value()
{
	Services.AddFluentUIComponents();
	Services.AddSingleton(Substitute.For<IReferenceService>());
	JSInterop.Mode = JSRuntimeMode.Loose;
	SetRendererInfo(new RendererInfo("WebAssembly", true));

	var component = Render<CountryLookup>();
	var marker = component.Find("[data-norse-renderer][data-norse-interactive]");

	marker.GetAttribute("data-norse-renderer").ShouldBe("WebAssembly");
	marker.GetAttribute("data-norse-interactive").ShouldBe("true");
}
```

- [ ] Run and confirm RED because the attributes do not exist. A
  `MissingRendererInfoException` is not an acceptable RED; it means the test omitted the required
  `SetRendererInfo` setup.

```bash
dotnet test Yggdrasil/tests/Hosting.Web.Components.Tests/Hosting.Web.Components.Tests.csproj -- --filter-class "*.CountryLookupTests"
```

- [ ] Wrap the existing page body in a semantically inert element. Insert the opening tag immediately
  after `PageTitle` and the closing tag immediately before `@code`; keep every existing element in
  between unchanged. Do not condition on environment.

```razor
<div data-norse-renderer="@RendererInfo.Name"
	 data-norse-interactive="@RendererInfo.IsInteractive.ToString().ToLowerInvariant()">
```

```razor
</div>

@code {
```

- [ ] Re-run focused tests and all component tests.

```bash
dotnet test Yggdrasil/tests/Hosting.Web.Components.Tests/Hosting.Web.Components.Tests.csproj -- --filter-class "*.CountryLookupTests"
dotnet test Yggdrasil/tests/Hosting.Web.Components.Tests/Hosting.Web.Components.Tests.csproj
```

### Task 9: Write the Web.Server smoke against the still-real persistence descriptor

**Files:**

- Create: `Yggdrasil/tests/Hosting.Web.Server.Tests/BrowserRuntime/WebServerBrowserFixture.cs`
- Create: `Yggdrasil/tests/Hosting.Web.Server.Tests/BrowserRuntime/WebServerBrowserCollection.cs`
- Create: `Yggdrasil/tests/Hosting.Web.Server.Tests/BrowserRuntime/WebServerBrowserRuntimeSmokeTests.cs`

- [ ] Declare a collection fixture with `DisableParallelization = true`.

```csharp
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WebServerBrowserCollection : ICollectionFixture<WebServerBrowserFixture>
{
	public const string Name = "WebServerBrowser";
}
```

- [ ] Create the fixture without a service override first, then write the full test. It must:

  1. warm `/` until at least one first-party `_framework/*.wasm` response succeeds and every
     in-flight first-party `/_framework/` request has finished, followed by 500 ms of framework
     request quiescence;
  2. navigate the same page/context to `/reference/country-lookup`;
  3. wait for `data-norse-renderer="WebAssembly"` and `data-norse-interactive="true"`;
  4. fill label `Code` with `US` and click role/name `Look up`;
  5. await POST `/grpc.reference.v1.ReferenceService/GetCountry` with request content type
     `application/grpc-web+proto`;
  6. assert successful response plus `US`, `USA`, `United States of America`, and `Match`;
  7. call evidence completion so browser/network failures fail the test.

```csharp
[Collection(WebServerBrowserCollection.Name)]
public sealed class WebServerBrowserRuntimeSmokeTests(WebServerBrowserFixture fixture)
{
	[Fact(Timeout = 300_000)]
	async Task Interactive_auto_executes_a_successful_country_lookup_in_webassembly()
	{
		await using var evidence = await fixture.OpenEvidenceAsync(nameof(Interactive_auto_executes_a_successful_country_lookup_in_webassembly));
		var page = evidence.Page;

		await evidence.RunPhaseAsync(
			"InteractiveAuto framework warm-up",
			BrowserTimeouts.HostStartup,
			cancellationToken => FrameworkRequestQuiescence.WaitAsync(page, fixture.Origin, cancellationToken));
		await page.GotoAsync("/reference/country-lookup", new()
		{
			Timeout = BrowserTimeouts.PlaywrightHostStartupMilliseconds,
		});

		var marker = page.Locator("[data-norse-renderer]");
		await Assertions.Expect(marker).ToHaveAttributeAsync(
			"data-norse-renderer",
			"WebAssembly",
			new() { Timeout = BrowserTimeouts.PlaywrightHostStartupMilliseconds });
		await Assertions.Expect(marker).ToHaveAttributeAsync(
			"data-norse-interactive",
			"true",
			new() { Timeout = BrowserTimeouts.PlaywrightHostStartupMilliseconds });

		var grpc = page.WaitForRequestAsync(request =>
			new Uri(request.Url).AbsolutePath == "/grpc.reference.v1.ReferenceService/GetCountry" &&
			request.Method == "POST",
			new() { Timeout = BrowserTimeouts.PlaywrightOperationMilliseconds });
		await page.GetByLabel("Code").FillAsync("US");
		await page.GetByRole(AriaRole.Button, new() { Name = "Look up" }).ClickAsync();

		var request = await grpc;
		(await request.AllHeadersAsync())["content-type"].ShouldStartWith("application/grpc-web+proto");
		await Assertions.Expect(page.GetByText("United States of America")).ToBeVisibleAsync(
			new() { Timeout = BrowserTimeouts.PlaywrightOperationMilliseconds });
		await Assertions.Expect(page.GetByText("Match", new() { Exact = true })).ToBeVisibleAsync(
			new() { Timeout = BrowserTimeouts.PlaywrightOperationMilliseconds });
		var result = await page.Locator("dl").TextContentAsync();
		result.ShouldContain("US");
		result.ShouldContain("USA");

		await evidence.CompleteAsync();
	}
}
```

- [ ] Run and confirm intentional RED at the persistence boundary: the page reaches WebAssembly and
  dispatches gRPC-Web, but the unresolved production repository cannot return the deterministic
  success. Capture the evidence directory and verify the failure names the gRPC/render phase rather
  than browser installation or host startup.

```bash
dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj -- --filter-class "*.WebServerBrowserRuntimeSmokeTests"
```

### Task 10: Replace only the read-repository descriptor and turn the browser smoke green

**Files:**

- Modify: `Yggdrasil/tests/Hosting.Web.Server.Tests/BrowserRuntime/WebServerBrowserFixture.cs`
- Test: `Yggdrasil/tests/Hosting.Web.Server.Tests/BrowserRuntime/WebServerBrowserRuntimeSmokeTests.cs`

- [ ] In `ConfigureWebHost`, use `ConfigureTestServices` to remove all
  `IReadRepository<CountryOrAreaView>` descriptors and add one NSubstitute singleton. Build the view
  from the platform datasets, use the baked identifier for both IDs, and compile the handler's real
  projection expression.

```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder) =>
	builder.ConfigureTestServices(services =>
	{
		var row = Iso3166.All.Single(static row => row.Code is IsoCountryCode.UnitedStatesOfAmerica);
		DeterministicGuid id = new(Iso3166.Ids[IsoCountryCode.UnitedStatesOfAmerica]);
		CountryOrAreaView view = new()
		{
			Id = id,
			Code = row.Code,
			Alpha2 = row.Alpha2,
			Alpha3 = row.Alpha3,
			Name = row.Name,
			Classification = Classification.None,
		};

		var repository = Substitute.For<IReadRepository<CountryOrAreaView>>();
		repository
			.GetAsync(
				Arg.Any<Guid>(),
				Arg.Any<Expression<Func<CountryOrAreaView, CountryResponse>>>(),
				Arg.Any<CancellationToken>())
			.Returns(Outcome<CountryResponse>.Err(ErrorCategory.NotFound));
		repository
			.GetAsync(
				(Guid)id,
				Arg.Any<Expression<Func<CountryOrAreaView, CountryResponse>>>(),
				Arg.Any<CancellationToken>())
			.Returns(call => Task.FromResult(Outcome<CountryResponse>.Ok(
				call.Arg<Expression<Func<CountryOrAreaView, CountryResponse>>>().Compile()(view))));

		services.RemoveAll<IReadRepository<CountryOrAreaView>>();
		services.AddSingleton(repository);
	});
```

- [ ] Re-run the browser smoke twice without retries. Both runs must pass independently, and no
  success artifact directory may remain.

```bash
dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj -- --filter-class "*.WebServerBrowserRuntimeSmokeTests"
dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj -- --filter-class "*.WebServerBrowserRuntimeSmokeTests"
```

- [ ] Add one fixture-level assertion that an unexpected ID receives NotFound if the substitute API
  defaults do not already preserve that contract. Do not implement or exercise EF behavior.

---

## Train 5 — Yggdrasil Stories.Server: catalog and runtime topology proof

### Task 11: Pin the released Bragi package and write the catalog smoke

**Prerequisite:** Human gate B1 is complete and the released version is known.

**Files:**

- Modify: `Yggdrasil/Directory.Packages.props`
- Create: `Yggdrasil/tests/Hosting.Stories.Server.Tests/BrowserRuntime/StoriesBrowserFixture.cs`
- Create: `Yggdrasil/tests/Hosting.Stories.Server.Tests/BrowserRuntime/StoriesBrowserCollection.cs`
- Create: `Yggdrasil/tests/Hosting.Stories.Server.Tests/BrowserRuntime/StoriesBrowserRuntimeSmokeTests.cs`

- [ ] Replace `<BragiVersion>0.0.6</BragiVersion>` with the exact published version and restore
  Yggdrasil standalone so this test exercises the released input rather than only Bifrost project
  references.

```bash
dotnet restore Yggdrasil/Yggdrasil.slnx
```

- [ ] Declare the second `DisableParallelization = true` collection and a fixture derived from the
  shared host fixture. Its evidence redirect predicate permits exactly:

```text
/Hosting.Stories.Client.styles.css (302) -> /Norse.Hosting.Stories.Client.styles.css (200)
```

- [ ] Discover links only from rendered story nodes:

```csharp
var links = await page.Locator(".navigation-tree-item.type-story > .caption a.action")
	.EvaluateAllAsync<string[]>("anchors => anchors.map(anchor => anchor.getAttribute('href'))");

links.Length.ShouldBeGreaterThanOrEqualTo(20);
links.ShouldContain(static path => path.Contains("/story/authentication-", StringComparison.Ordinal));
links.ShouldContain(static path => path.Contains("/story/primitives-", StringComparison.Ordinal));
```

The selector and root predicates are pinned to the audited preview.91 DOM. If the first RED discovers
zero states, capture the rendered navigation subtree in failure evidence and correct the discovery
selector to that DOM before continuing; if discovery is nonzero but a required root is absent,
inspect the rendered `href` values and correct only the two root predicates. Record the observed DOM
shape in the implementation notes. Neither correction may switch to source-file inventory or weaken
the 20-state/two-root floors.

- [ ] Visit every link by clicking the existing catalog anchor in the same page/context. Do not use
  `GotoAsync` per story because that destroys the pool and stops testing the real lifecycle.

```csharp
var drivenStateCount = 0;
foreach (var link in links)
{
	fixture.CurrentState = link;
	var target = page.Locator($"a[href='{link}']");
	await Assertions.Expect(target).ToBeVisibleAsync(
		new() { Timeout = BrowserTimeouts.PlaywrightStoryStateMilliseconds });
	await target.ClickAsync(new() { Timeout = BrowserTimeouts.PlaywrightStoryStateMilliseconds });

	var active = page.Locator(".canvas-container iframe");
	await Assertions.Expect(active).ToHaveCountAsync(
		1,
		new() { Timeout = BrowserTimeouts.PlaywrightStoryStateMilliseconds });
	var handle = await active.ElementHandleAsync() ?? throw new BrowserFailure($"No active iframe for {link}.");
	var frame = await handle.ContentFrameAsync() ?? throw new BrowserFailure($"No active frame for {link}.");
	await Assertions.Expect(frame.Locator("body")).ToHaveAttributeAsync(
		"data-bs-parent-frame",
		"story",
		new() { Timeout = BrowserTimeouts.PlaywrightStoryStateMilliseconds });
	await Assertions.Expect(frame.Locator("#app > *")).Not.ToHaveCountAsync(
		0,
		new() { Timeout = BrowserTimeouts.PlaywrightStoryStateMilliseconds });

	var driver = frame.Locator("[data-norse-story-driver-state]");
	var driverCount = await driver.CountAsync();
	if (driverCount > 0)
	{
		driverCount.ShouldBe(1);
		drivenStateCount++;
		await Assertions.Expect(driver).ToHaveAttributeAsync(
			"data-norse-story-driver-state",
			"complete",
			new() { Timeout = BrowserTimeouts.PlaywrightStoryStateMilliseconds });
	}

	await AssertFrameLawAsync(page, link);
}

drivenStateCount.ShouldBeGreaterThanOrEqualTo(7);
```

The aggregate floor is load-bearing: released 0.0.6 exposes no marker, so all seven driven states
would otherwise take the conditional's zero-marker path and falsely pass. The floor rejects that
package without maintaining a route inventory; future driver-backed states only increase the count.

Normal actionability is part of the smoke: expand the catalog's visible disclosure controls if a
target is collapsed, then use ordinary `ClickAsync`. `Force = true` is not the default and may be
introduced only as a narrowly documented last resort after preview.91 evidence proves that a
legitimate rendered story link cannot become actionable through its disclosure controls.

- [ ] `AssertFrameLawAsync` enumerates `page.Frames` excluding `MainFrame` and asserts for every live
  canvas: URL absolute path equals `/iframe.html`; body marker is `story` or `docs`; no descendant
  frames; no catalog selectors; no visible `#blazor-error-ui`, reload prompt, or loading shell. It
  asserts `page.Frames.Count <= 7`. It records every `_framework/blazor.webassembly` request but never
  fails on their cumulative count. Every locator assertion in this helper receives
  `BrowserTimeouts.PlaywrightStoryStateMilliseconds`; it must not inherit Playwright's five-second
  default.

- [ ] Verify the known redirect exactly once-or-more as pool boots demand, require every matching
  redirect to have the exact target and final 200, and reject every other first-party redirect.

- [ ] Run standalone/package-mode RED before accepting the train. If B1 or the Yggdrasil bump is
  missing, the aggregate driven-marker floor must report 0 observed against the required 7; that is
  the intended ship-order proof. After the published bump, run twice GREEN with no retries.

```bash
dotnet test Yggdrasil/tests/Hosting.Stories.Server.Tests/Hosting.Stories.Server.Tests.csproj -- --filter-class "*.StoriesBrowserRuntimeSmokeTests"
dotnet test Yggdrasil/tests/Hosting.Stories.Server.Tests/Hosting.Stories.Server.Tests.csproj -- --filter-class "*.StoriesBrowserRuntimeSmokeTests"
```

- [ ] On any failure, inspect `frames.log` and confirm it contains state, every frame URL/path, body
  marker, parent relationship, pool/active classification, and bootstrap requests. Do not accept a
  bare timeout as sufficient evidence.

---

## Train 6 — Yggdrasil CI activation and proof

### Task 12: Enable Chromium only for Yggdrasil and document local execution

**Files:**

- Modify: `Yggdrasil/.github/workflows/ci.yml`
- Create: `Yggdrasil/tests/BrowserTesting/README.md`

- [ ] Enable the published boolean capability.

```yaml
    with:
      minimum_coverage: 0
      playwright_chromium: true
```

- [ ] Document the local build/install/run sequence and the failure evidence location. Include the
  package/browser coupling and explicitly say that Firefox/WebKit are deferred.

```text
dotnet build Yggdrasil.slnx -c Release
pwsh tests/Hosting.Web.Server.Tests/bin/Release/net11.0/playwright.ps1 install chromium
dotnet test Yggdrasil.slnx -c Release --no-build -m:1
```

- [ ] Run the exact local CI-shaped sequence. Then run all non-browser tests once normally to prove
  the shared harness did not impose a browser prerequisite on discovery/build-only callers.

```bash
cd Yggdrasil
dotnet build Yggdrasil.slnx -c Release
pwsh tests/Hosting.Web.Server.Tests/bin/Release/net11.0/playwright.ps1 install chromium
dotnet test Yggdrasil.slnx -c Release --no-build -m:1 --tl:off --verbosity normal
dotnet test Yggdrasil.slnx -- --filter-not-class "*BrowserRuntimeSmokeTests" "*BrowserHostFixtureTests"
git diff --check
```

- [ ] Confirm the full run produced exactly two browser lease intervals with no overlap and no
  `TestResults/playwright` success payloads.

### Task 13: First-CI serialization and budget checkpoint

**Files:**

- Modify only if evidence disproves the assumption:
  `../.github/.github/workflows/ci-build-test.yml`
- Record evidence in the implementation handoff; do not add permanent noisy artifacts solely for
  this checkpoint.

- [ ] Run the Yggdrasil pull-request workflow on a clean runner. Verify from `--tl:off --verbosity
  normal` output that MTP starts and finishes one test module before the next begins. Correlate the two
  browser lease PID/UTC intervals and GUID-named coverage file creation times with module output.

- [ ] If module execution overlaps despite `-m:1`, stop and revise the Chromium branch to enumerate
  solution test projects in a deterministic order and invoke each project with the same Release,
  no-build, and coverage arguments one at a time. Re-run the workflow until logs prove non-overlap.
  The cross-process lease is retained either way.

- [ ] Run the permanent timeout-classification tests and inspect the deliberate failure-artifact
  exercise below. Confirm phase budget, aggregate-during-phase, and aggregate-outside-phase produce
  three distinct verdicts; the exact aggregate/no-state sentence appears only in the third branch.

```bash
dotnet test Yggdrasil/tests/Hosting.Stories.Server.Tests/Hosting.Stories.Server.Tests.csproj -- --filter-class "*.BrowserTimeoutClassificationTests"
```

- [ ] Check uploaded diagnostics by creating one temporary failing assertion in each smoke, running
  locally, and confirming trace, screenshot, browser log, network log, server log, and frame inventory
  are present. Revert only the temporary assertions, rerun GREEN, and confirm success creates no
  uploadable directory.

Insert this line immediately before each smoke's successful `CompleteAsync()` call, run that class
with its MTP `--filter-class` command, inspect the artifacts, then remove only this line:

```csharp
throw new InvalidOperationException("Playwright failure-evidence probe.");
```

### Task 14: Final verification, spec traceability, and staging

**Files:**

- Verify all files above.
- Verify: `Glitnir/docs/Platform/specs/2026-08-08-browser-runtime-smoke-gate-design.md`
- Verify: `Glitnir/docs/Platform/plans/2026-08-08-browser-runtime-smoke-gate.md`

- [ ] Run all changed-realm tests from clean standalone checkouts/package resolution where release
  boundaries matter.

```bash
dotnet test Bragi/Bragi.slnx
dotnet test Yggdrasil/Yggdrasil.slnx -c Release --no-build -m:1 --tl:off --verbosity normal
pwsh -NoProfile -File ../.github/scripts/tests/verify-ci-build-test-playwright.ps1
git -C Bragi diff --check
git -C Yggdrasil diff --check
git -C ../.github diff --check
git -C Glitnir diff --check
```

- [ ] Trace all 13 design acceptance criteria to a passing assertion or workflow check. In
  particular, record: released Bragi version, Playwright/Chromium version, discovered state count,
  Authentication/Primitives counts, maximum live-frame count observed, exact stylesheet redirect,
  gRPC-Web request path/content type, two lease intervals, CI module ordering, and artifact behavior.

- [ ] Search for forbidden shortcuts.

```bash
rg -n "IgnoreHTTPSErrors = true|Retry|Firefox|Webkit|WebKit|Testcontainers|UseInMemoryDatabase|ReferenceDbContext" Yggdrasil/tests/BrowserTesting Yggdrasil/tests/Hosting.Web.Server.Tests/BrowserRuntime Yggdrasil/tests/Hosting.Stories.Server.Tests/BrowserRuntime
rg -n "1\.\*-\*" Bragi/src/DesignSystem.Stories/DesignSystem.Stories.csproj
rg -n "WebApplicationFactory<.*>\.Server|\.Server\b" Yggdrasil/tests/BrowserTesting Yggdrasil/tests/Hosting.Web.Server.Tests/BrowserRuntime Yggdrasil/tests/Hosting.Stories.Server.Tests/BrowserRuntime
```

Expected: no forbidden browser/database bypass; the only `Retry` text may be explanatory test output,
not behavior; exact BlazingStory pin; no TestServer access.

- [ ] Stage only the files belonging to each realm. Leave commits, tags, package publication, pushes,
  and pull requests to the human gates.

## Plan self-review checklist

- [ ] Every approved design section (§§2–8) maps to at least one task and executable assertion.
- [ ] No task depends on Bragi source when it claims to test the released package boundary.
- [ ] All code snippets use existing namespaces/types; there are no `TODO`, ellipsis, fake project
  names, or invented host entry points.
- [ ] All behavior tasks have an intentional RED before implementation and a focused GREEN before the
  broader run.
- [ ] The aggregate timeout and MTP serialization observations are acceptance checkpoints, not buried
  notes.
- [ ] The plan changes neither EF semantics nor Mimir ownership and creates no second end-to-end tier.
