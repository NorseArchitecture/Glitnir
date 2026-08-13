# Session Transition Seam Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task, paired with superpowers:test-driven-development on every coding task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Name the principal-transition concept (`ISessionTransition` in Heimdall), neuter the story catalog's nested-doll ignition (suppress-and-record in Bragi), delete two cargo-cult forced reloads (Register, RedirectToLogin), rework Logout to click-to-confirm on a new Asgard `OutcomeComponentBase`, and enforce the whole shape with NORSE074/NORSE075 in Svartálfheim.

**Architecture:** Five ship gates in strict dependency order — Asgard (outcome bases) → Heimdall (seam + adoption) → Yggdrasil (registration + RedirectToLogin) → Bragi (recorder + stories) → Svartálfheim (analyzers last, zero violations remaining). Spec: `../specs/2026-08-11-session-transition-seam-design.md` — read it before starting any task.

**Tech Stack:** .NET 11 preview (SDK pinned by each realm's `global.json`), Blazor, bUnit 2.x, xUnit v3 on Microsoft.Testing.Platform v2, Shouldly, NSubstitute, Roslyn analyzers (`Microsoft.CodeAnalysis.CSharp`, floating `*`).

## Global Constraints

- **Never commit.** Stage (`git add`) and stop at every point this plan says "stage" — the human commits. This overrides any skill's commit step. No force-push, no hook-skipping.
- **Each realm is its own git repository** (submodules under `/home/buvy/…/Bifrost` — but never write absolute paths into any file). Run git commands with `-C <Realm>`.
- **Warnings are errors platform-wide** — a single warning fails the build, including IDE0055 formatting. IDE0005 (unused using) is never suppressed: delete the line.
- **Test invocation:** VSTest `--filter` does NOT work. Use `dotnet test <realm>/tests/<Project> -- --filter-class "*.<ClassName>"`. Never `dotnet test` a test project with zero tests (xUnit v3 fails the run).
- **Test style:** classes `public sealed`; methods omit the accessibility modifier (bare `void` / `async Task`); names sentence-shaped with underscores; Shouldly assertions; NSubstitute mocks; global usings for `NSubstitute`/`Shouldly`/`Xunit` already flow from each realm's `tests/Directory.Build.props` — do not re-add.
- **Code style:** tabs in `.cs` (existing `.razor`/`.stories.razor` markup uses 4 spaces — match the file you're in); target-typed `new()` for construction, `var` for return assignments; collection expressions; C# `extension(...)` blocks for new extension members; `sealed` by default; omit default accessibility modifiers; XML doc comments on every publicly visible member in `src` projects (CS1591 is only NoWarn'd in tests/gen); US English.
- **`ConfigureAwait(false)` never in component code** — CA2007 is pragma-suppressed with a comment in the two base classes (renderer sync context), and never used in tests (xUnit1030).
- **`NavigationResult` is a record** — value equality drives NSubstitute assertions: `transition.Received(1).Begin(new() { NextUrl = "/" })` works as-is.
- **house-rules.md** (`../../house-rules.md`) is settled law; each implementing subagent follows it. Where this plan shows code, the code already conforms — copy it faithfully.

## File Structure (what this plan creates/modifies, by realm)

| Realm | Creates | Modifies |
|---|---|---|
| Asgard | `src/Abstractions.Components/OutcomeComponentBase.cs`, `tests/Abstractions.Components.Tests/OutcomeComponentBaseTests.cs` | `src/Abstractions.Components/OutcomeFormComponentBase.cs`, `tests/Abstractions.Components.Tests/OutcomeFormComponentBaseTests.cs`, `CLAUDE.md` |
| Heimdall | `src/AuthN.Components/ISessionTransition.cs`, `src/AuthN.Components/ForceLoadSessionTransition.cs`, `src/AuthN.Components/ServiceCollectionExtensions.cs`, `tests/AuthN.Components.Tests/SessionTransitionTests.cs` | `src/AuthN.Components.FluentUI/Login.razor`, `Register.razor`, `src/AuthN.Components/Logout.razor`, `tests/AuthN.Components.FluentUI.Tests/LoginTests.cs`, `RegisterTests.cs`, `tests/AuthN.Components.Tests/LogoutTests.cs`, `CLAUDE.md` |
| Yggdrasil | `tests/Hosting.Web.Components.Tests/RedirectToLoginTests.cs` | `src/Hosting.Web.Server/Program.cs`, `src/Hosting.Web.Client/Program.cs`, `src/Hosting.Web.Components/RedirectToLogin.razor`, `Directory.Packages.props` (version bumps at gate) |
| Bragi | `src/DesignSystem.Stories/RecordingSessionTransition.cs`, `src/DesignSystem.Stories/Authentication/Logout.stories.razor`, `tests/DesignSystem.Stories.Tests/RecordingSessionTransitionTests.cs` | `src/DesignSystem.Stories/ServiceCollectionExtensions.cs`, `Authentication/FakeAuthenticationService.cs`, `Scenarios/StoryDriverMode.cs`, `Scenarios/StoryDriver.razor`, `wwwroot/storyDriver.js`, `tests/…/DrivenStoryNavigationTests.cs`, `FakeAuthenticationServiceTests.cs`, `ServiceCollectionExtensionsTests.cs`, `Scenarios/StoryDriverTests.cs`, `Scenarios/storyDriver.test.mjs`, `CLAUDE.md` |
| Svartálfheim | `gen/Architecture.Analyzers/ForcedLoadAnalyzer.cs`, `SeamBoundFormAnalyzer.cs`, `tests/Architecture.Analyzers.Tests/ForcedLoadAnalyzerTests.cs`, `SeamBoundFormAnalyzerTests.cs`, `RazorGeneratedFixtures.cs`, `SeamBoundFormBuildProofTests.cs`, `BuildFixtures/` | `gen/Architecture.Analyzers/Diagnostics.cs`, `tests/Architecture.Analyzers.Tests/DiagnosticsTests.cs`, `Architecture.Analyzers.Tests.csproj` |

---

## GATE 1 — Asgard

### Task 1: `SubmitAsync` post-await cancellation check

**Files:**
- Modify: `Asgard/src/Abstractions.Components/OutcomeFormComponentBase.cs` (after the `var outcome = await call(cancellationToken);` line)
- Test: `Asgard/tests/Abstractions.Components.Tests/OutcomeFormComponentBaseTests.cs`

**Interfaces:**
- Consumes: existing `Harness` nested class in the test file (exposes `ContextFor`, `Submit`), `AsyncComponentBase.Dispose()`.
- Produces: no signature change — `SubmitAsync` now returns `false` without running `onSuccess` when the component is disposed during the service call.

- [ ] **Step 1: Write the failing test** — append to the existing `OutcomeFormComponentBaseTests` class, using its existing `Harness` and `FakeResult`:

```csharp
	[Fact]
	async Task Disposal_during_the_call_skips_the_continuation()
	{
		var invoked = false;
		using Harness harness = new();
		var context = harness.ContextFor(new object());

		var submitted = await harness.Submit(context, _ =>
		{
			// Dispose mid-call: the token SubmitAsync captured before the await is now canceled,
			// but the call itself still completes successfully.
			harness.Dispose();
			return Task.FromResult<Outcome<FakeResult>>(new Success<FakeResult>(new()));
		}, _ => invoked = true);

		submitted.ShouldBeFalse();
		invoked.ShouldBeFalse();
	}
```

- [ ] **Step 2: Run it, verify it fails** — `dotnet test Asgard/tests/Abstractions.Components.Tests -- --filter-class "*.OutcomeFormComponentBaseTests"`. Expected: the new test FAILS (`submitted` is `true` — today nothing checks cancellation after the await), all pre-existing tests PASS.

- [ ] **Step 3: Implement** — in `OutcomeFormComponentBase.SubmitAsync`, immediately after `var outcome = await call(cancellationToken);` and before the `switch`:

```csharp
				// Checked again after the await, mirroring the pre-dispatch check: disposal during the
				// service call means there is no form left to render onto and no continuation worth
				// running — the same rule OutcomeComponentBase states as law.
				if (cancellationToken.IsCancellationRequested)
					return false;
```

- [ ] **Step 4: Run tests, verify green** — same command. Expected: all PASS.
- [ ] **Step 5: Stage** — `git -C Asgard add src/Abstractions.Components/OutcomeFormComponentBase.cs tests/Abstractions.Components.Tests/OutcomeFormComponentBaseTests.cs`

### Task 2: `OutcomeComponentBase`

**Files:**
- Create: `Asgard/src/Abstractions.Components/OutcomeComponentBase.cs`
- Create: `Asgard/tests/Abstractions.Components.Tests/OutcomeComponentBaseTests.cs`
- Modify: `Asgard/CLAUDE.md` (one row edit, step 5)

**Interfaces:**
- Consumes: `AsyncComponentBase.CancellationToken` (protected), `Outcome<T>`/`Success<T>`/`Failed`/`Problem`/`ErrorCategory` from `Norse.Abstractions.Contracts`.
- Produces: `abstract class OutcomeComponentBase : AsyncComponentBase` with `protected Problem? Problem { get; }`, `protected bool IsDispatching { get; }`, `protected Task DispatchAsync<T>(Func<CancellationToken, Task<Outcome<T>>> call, Action<T> onSuccess) where T : notnull` and a `Func<T, Task>` overload. Heimdall's Logout (Task 7) inherits this.

- [ ] **Step 1: Write the failing tests** — new file, plain xUnit (no bUnit; mirrors `OutcomeFormComponentBaseTests`'s nested-harness pattern):

```csharp
using Norse.Abstractions.Contracts;

namespace Norse.Abstractions.Components.Tests;

public sealed class OutcomeComponentBaseTests
{
	sealed record FakeResult;

	sealed class Harness : OutcomeComponentBase
	{
		internal Problem? CapturedProblem =>
			Problem;

		internal bool Dispatching =>
			IsDispatching;

		internal Task Dispatch<T>(Func<CancellationToken, Task<Outcome<T>>> call, Action<T> onSuccess)
			where T : notnull =>
			DispatchAsync(call, onSuccess);
	}

	static Outcome<FakeResult> Success() =>
		Outcome<FakeResult>.Ok(new FakeResult());

	static Outcome<FakeResult> Failure() =>
		Outcome<FakeResult>.Err(ErrorCategory.Validation,
			new Dictionary<string, string[]> { [string.Empty] = ["nope"] });

	[Fact]
	async Task Success_invokes_the_continuation_and_leaves_no_problem()
	{
		var invoked = false;
		using Harness harness = new();

		await harness.Dispatch(_ => Task.FromResult(Success()), _ => invoked = true);

		invoked.ShouldBeTrue();
		harness.CapturedProblem.ShouldBeNull();
	}

	[Fact]
	async Task Failure_captures_the_problem_and_skips_the_continuation()
	{
		var invoked = false;
		using Harness harness = new();

		await harness.Dispatch(_ => Task.FromResult(Failure()), _ => invoked = true);

		invoked.ShouldBeFalse();
		harness.CapturedProblem.ShouldNotBeNull();
		harness.CapturedProblem.Category.ShouldBe(ErrorCategory.Validation);
	}

	[Fact]
	async Task A_new_dispatch_clears_the_prior_problem()
	{
		using Harness harness = new();
		await harness.Dispatch(_ => Task.FromResult(Failure()), _ => { });

		await harness.Dispatch(_ => Task.FromResult(Success()), _ => { });

		harness.CapturedProblem.ShouldBeNull();
	}

	[Fact]
	async Task An_overlapping_dispatch_returns_without_dispatching()
	{
		var calls = 0;
		TaskCompletionSource<Outcome<FakeResult>> pending = new();
		using Harness harness = new();
		var first = harness.Dispatch(_ =>
		{
			calls++;
			return pending.Task;
		}, _ => { });

		await harness.Dispatch(_ =>
		{
			calls++;
			return Task.FromResult(Success());
		}, _ => { });
		pending.SetResult(Success());
		await first;

		calls.ShouldBe(1);
	}

	[Fact]
	async Task Disposal_during_the_call_runs_no_continuation_and_writes_no_state()
	{
		var invoked = false;
		using Harness harness = new();

		await harness.Dispatch(_ =>
		{
			harness.Dispose();
			return Task.FromResult(Failure());
		}, _ => invoked = true);

		invoked.ShouldBeFalse();
		harness.CapturedProblem.ShouldBeNull();
	}

	[Fact]
	async Task A_throwing_continuation_propagates_and_releases_the_guard()
	{
		using Harness harness = new();

		await Should.ThrowAsync<InvalidOperationException>(() =>
			harness.Dispatch(_ => Task.FromResult(Success()),
				_ => throw new InvalidOperationException("boom")));

		harness.Dispatching.ShouldBeFalse();
	}
}
```

- [ ] **Step 2: Run, verify failure to compile** — `dotnet test Asgard/tests/Abstractions.Components.Tests -- --filter-class "*.OutcomeComponentBaseTests"`. Expected: compile error, `OutcomeComponentBase` not defined.

- [ ] **Step 3: Implement** — new file `src/Abstractions.Components/OutcomeComponentBase.cs`:

```csharp
using Norse.Abstractions.Contracts;

namespace Norse.Abstractions.Components;

/// <summary>
///     The load-time sibling of <see cref="OutcomeFormComponentBase" />: a page whose
///     outcome-consuming operation has no form declares only the success continuation, and the
///     <see cref="Outcome{T}" /> failure story is handled where it cannot be forgotten —
///     <c>Failed</c> lands in <see cref="Problem" /> for the page's markup to render. Total over the
///     <see cref="Outcome{T}" /> domain only: exceptions (a throwing transport, a throwing
///     continuation) propagate to the circuit's error boundary deliberately — swallowing them here
///     would be a silent fallback.
/// </summary>
public abstract class OutcomeComponentBase : AsyncComponentBase
{
	/// <summary>The failure of the last dispatch, rendered by the page's markup. Null until a dispatch fails.</summary>
	protected Problem? Problem { get; private set; }

	/// <summary>True while a dispatch is in flight — bind to the trigger's disabled state.</summary>
	protected bool IsDispatching { get; private set; }

	/// <summary>Synchronous-continuation convenience over the <see cref="Func{T, Task}" /> overload.</summary>
	protected Task DispatchAsync<T>(Func<CancellationToken, Task<Outcome<T>>> call, Action<T> onSuccess)
		where T : notnull =>
		DispatchAsync(call, value =>
		{
			onSuccess(value);
			return Task.CompletedTask;
		});

	/// <summary>
	///     Dispatches <paramref name="call" /> and routes its <see cref="Outcome{T}" />: failure into
	///     <see cref="Problem" />, success into <paramref name="onSuccess" />. The state rules are law:
	///     a dispatch clears the prior <see cref="Problem" /> when it starts; an overlapping dispatch
	///     returns without dispatching; and disposal during the call writes no result state — no
	///     <see cref="Problem" />, no continuation; the component is gone, so there is nothing to
	///     render onto. The in-flight guard still releases in <c>finally</c> — re-entrancy
	///     bookkeeping, deliberately exempt from the no-result-state rule.
	/// </summary>
	protected async Task DispatchAsync<T>(Func<CancellationToken, Task<Outcome<T>>> call, Func<T, Task> onSuccess)
		where T : notnull
	{
		ArgumentNullException.ThrowIfNull(call);
		ArgumentNullException.ThrowIfNull(onSuccess);
		if (IsDispatching)
			return;

		IsDispatching = true;
		try
		{
			Problem = null;
			// Read once, before the first await, so the token the dispatch runs under is the same one
			// disposal cancels.
			var cancellationToken = CancellationToken;
			// CA2007 deliberately suppressed, not worked around: component code must resume on the
			// renderer's sync context, so ConfigureAwait(false) here would be a correctness bug, not
			// a style nit.
#pragma warning disable CA2007
			var outcome = await call(cancellationToken);
			// Checked again after the await: disposal during the call runs no continuation and
			// writes no result state (the guard below still releases — bookkeeping, not results).
			if (cancellationToken.IsCancellationRequested)
				return;
			switch (outcome)
			{
				case Success<T>(var value):
					await onSuccess(value);
					break;
				case Failed(var problem):
					Problem = problem;
					break;
			}
#pragma warning restore CA2007
		}
		finally
		{
			IsDispatching = false;
		}
	}
}
```

If `Outcome<FakeResult>.Ok(new FakeResult())` or the `Err(ErrorCategory, Dictionary<string, string[]>)` overload signatures differ from what compiles, mirror the exact construction used in `OutcomeFormComponentBaseTests.cs` in the same directory — that file is the authority for `Outcome` test construction.

- [ ] **Step 4: Run tests, verify green** — same command, then the full project: `dotnet test Asgard/tests/Abstractions.Components.Tests`. Expected: all PASS.
- [ ] **Step 5: Boy-scout docs** — in `Asgard/CLAUDE.md`, the `Abstractions.Components` row of the assembly table: append `OutcomeComponentBase` (the non-form outcome dispatch sibling, 2026-08-11) to its "Carries" cell. If `Asgard/README.md` enumerates the same surface, make the matching edit.
- [ ] **Step 6: Stage** — `git -C Asgard add src/Abstractions.Components/OutcomeComponentBase.cs tests/Abstractions.Components.Tests/OutcomeComponentBaseTests.cs CLAUDE.md` (plus README.md if edited).

### Task 3: GATE — Asgard ships

- [ ] Run the realm suite: `dotnet build Asgard/Asgard.slnx && dotnet test Asgard/Asgard.slnx`. Expected: zero warnings, all tests pass.
- [ ] Show the staged diff (`git -C Asgard diff --staged --stat`) and **STOP**. The human commits, PRs, waits for CI, tags (MinVer — version comes from the tag), and publishes. **Do not proceed to Gate 2 until told the Asgard gate cleared.** (In the Bifröst workspace, later tasks build against local project references, so implementation may be *authorized* to continue pre-tag — but only by explicit human say-so.)

---

## GATE 2 — Heimdall

### Task 4: `ISessionTransition`, `ForceLoadSessionTransition`, `AddNorseSessionTransition()`

**Files:**
- Create: `Heimdall/src/AuthN.Components/ISessionTransition.cs`, `ForceLoadSessionTransition.cs`, `ServiceCollectionExtensions.cs`
- Test: `Heimdall/tests/AuthN.Components.Tests/SessionTransitionTests.cs`

**Interfaces:**
- Consumes: `NavigationResult` (`Norse.Abstractions.Contracts`), `NavigationManager` (arrives transitively via Asgard's `Abstractions.Components` → `Microsoft.AspNetCore.Components.Web` — add no package references).
- Produces: `public interface ISessionTransition { void Begin(NavigationResult result); }` in `Norse.AuthN.Components`; internal `ForceLoadSessionTransition`; `public IServiceCollection AddNorseSessionTransition()` extension. Tasks 5, 7, 9, 11 consume these.

- [ ] **Step 1: Write the failing tests** — new file `tests/AuthN.Components.Tests/SessionTransitionTests.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Contracts;

namespace Norse.AuthN.Components.Tests;

public sealed class SessionTransitionTests
{
	sealed class RecordingNavigationManager : NavigationManager
	{
		internal string? RequestedUri { get; private set; }
		internal NavigationOptions RequestedOptions { get; private set; }

		public RecordingNavigationManager() =>
			Initialize("http://localhost/", "http://localhost/");

		protected override void NavigateToCore(string uri, NavigationOptions options)
		{
			RequestedUri = uri;
			RequestedOptions = options;
		}
	}

	[Fact]
	void Begin_performs_a_forced_document_load_at_the_server_resolved_hop()
	{
		RecordingNavigationManager navigation = new();
		ForceLoadSessionTransition transition = new(navigation);

		transition.Begin(new() { NextUrl = "/Account/LoginWith2fa" });

		navigation.RequestedUri.ShouldBe("/Account/LoginWith2fa");
		navigation.RequestedOptions.ForceLoad.ShouldBeTrue();
	}

	[Fact]
	void AddNorseSessionTransition_registers_the_scoped_seam()
	{
		ServiceCollection services = new();

		services.AddNorseSessionTransition();

		services.ShouldContain(d => d.ServiceType == typeof(ISessionTransition)
			&& d.ImplementationType == typeof(ForceLoadSessionTransition)
			&& d.Lifetime == ServiceLifetime.Scoped);
	}
}
```

- [ ] **Step 2: Run, verify compile failure** — `dotnet test Heimdall/tests/AuthN.Components.Tests -- --filter-class "*.SessionTransitionTests"`. Expected: compile error (types not defined).

- [ ] **Step 3: Implement three files** —

`src/AuthN.Components/ISessionTransition.cs`:

```csharp
using Norse.Abstractions.Contracts;

namespace Norse.AuthN.Components;

/// <summary>
///     The principal changed — re-establish this interactive session at the server-resolved next hop.
///     Components performing a principal transition (sign-in, sign-out) request the transition here
///     instead of touching <see cref="Microsoft.AspNetCore.Components.NavigationManager" />; the host
///     decides what stands behind it. Realm law, not platform law: only the gate changes who the user
///     is, so only the gate declares — and implements — the seam. The contract has no domain failure
///     arm; exceptional failures propagate to the circuit's error boundary.
/// </summary>
public interface ISessionTransition
{
	/// <summary>Begins the transition. Completion, if any, is the next document load's concern.</summary>
	void Begin(NavigationResult result);
}
```

`src/AuthN.Components/ForceLoadSessionTransition.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using Norse.Abstractions.Contracts;

namespace Norse.AuthN.Components;

/// <summary>
///     The production <see cref="ISessionTransition" />: a real document load, so the circuit (or WASM
///     runtime) that holds the stale principal is torn down and re-established under the new identity.
///     Named for its mechanism — contracts name the role, implementations name what distinguishes them.
///     The one call site NORSE074 absolves — matched by this exact type name AND this assembly, so the
///     exemption is unforgeable; even the gate's own pages are convicted if they force a load directly.
/// </summary>
sealed class ForceLoadSessionTransition(NavigationManager navigation) : ISessionTransition
{
	public void Begin(NavigationResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		navigation.NavigateTo(result.NextUrl, forceLoad: true);
	}
}
```

`src/AuthN.Components/ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Norse.AuthN.Components;

/// <summary>Registration entry point for the gate's session-transition seam.</summary>
public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>
		///     Registers the production <see cref="ISessionTransition" /> — a forced document load at
		///     the server-resolved next hop. Scoped, matching
		///     <see cref="Microsoft.AspNetCore.Components.NavigationManager" />'s lifetime. Hosts that
		///     render the gate's components call this; the story catalog registers its recorder instead.
		/// </summary>
		/// <returns>The same service collection instance.</returns>
		public IServiceCollection AddNorseSessionTransition() =>
			services.AddScoped<ISessionTransition, ForceLoadSessionTransition>();
	}
}
```

- [ ] **Step 4: Run tests, verify green** — same command. Expected: PASS.
- [ ] **Step 5: Stage** — `git -C Heimdall add src/AuthN.Components/ISessionTransition.cs src/AuthN.Components/ForceLoadSessionTransition.cs src/AuthN.Components/ServiceCollectionExtensions.cs tests/AuthN.Components.Tests/SessionTransitionTests.cs`

### Task 5: Login adopts the seam

**Files:**
- Modify: `Heimdall/src/AuthN.Components.FluentUI/Login.razor`
- Test: `Heimdall/tests/AuthN.Components.FluentUI.Tests/LoginTests.cs`

**Interfaces:**
- Consumes: `ISessionTransition.Begin(NavigationResult)` (Task 4). `Norse.AuthN.Components` is already in the FluentUI project's `_Imports.razor`.

- [ ] **Step 1: Make the tests demand the seam** — in `LoginTests.cs`: add a class field and register it in the constructor (every test renders `Login`, which will inject the seam):

```csharp
	readonly ISessionTransition _sessionTransition = Substitute.For<ISessionTransition>();
```

and in the constructor body: `Services.AddSingleton(_sessionTransition);` (add `using Norse.AuthN.Components;` to the usings — hoisted, alphabetical). Then rewrite the navigation assertions in `NextUrl_RoutesToTheTwoFactorChallenge_NotToACompletedLogin` (currently `navigation.Uri.ShouldBe(...)` / `ForceLoad.ShouldBeTrue()`):

```csharp
		_sessionTransition.Received(1).Begin(new() { NextUrl = "Account/LoginWith2fa?RememberMe=false" });
		Services.GetRequiredService<BunitNavigationManager>().History.ShouldBeEmpty();
```

- [ ] **Step 2: Run, verify red** — `dotnet test Heimdall/tests/AuthN.Components.FluentUI.Tests -- --filter-class "*.LoginTests"`. Expected: the 2FA test FAILS (`Begin` never received; history non-empty).

- [ ] **Step 3: Implement** — in `Login.razor`: replace `@inject NavigationManager Navigation` with `@inject ISessionTransition SessionTransition`, and change the continuation:

```razor
    // NavigationResult.NextUrl is always a concrete, server-resolved value -- 2FA challenge, deferred
    // sign-in completion, or a plain "/" -- so the component has no flag to branch on, no route to
    // build, and no default of its own to apply. The seam owns the mechanics; the page states the
    // domain fact: the principal changed, re-establish the session there.
    Task HandleLoginAsync(EditContext editContext)
    {
        return SubmitAsync(editContext, ct => AuthenticationService.Login(_request, ct),
            result => SessionTransition.Begin(result));
    }
```

- [ ] **Step 4: Run all Login tests, verify green.** Expected: PASS, including the untouched credential-failure tests.
- [ ] **Step 5: Stage** — `git -C Heimdall add src/AuthN.Components.FluentUI/Login.razor tests/AuthN.Components.FluentUI.Tests/LoginTests.cs`

### Task 6: Register drops `forceLoad`

**Files:**
- Modify: `Heimdall/src/AuthN.Components.FluentUI/Register.razor`
- Test: `Heimdall/tests/AuthN.Components.FluentUI.Tests/RegisterTests.cs`

- [ ] **Step 1: Write the failing test** — append to `RegisterTests` (add `using Bunit.TestDoubles;` to the hoisted usings):

```csharp
	[Fact]
	async Task A_successful_registration_soft_navigates_to_the_server_resolved_hop()
	{
		var service = Substitute.For<IAuthenticationService>();
		service.Register(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>())
			.Returns(Outcome<NavigationResult>.Ok(new NavigationResult { NextUrl = "/Account/Login" }));
		service.EmailExists(Arg.Any<EmailExistsRequest>(), Arg.Any<CancellationToken>())
			.Returns(Outcome<BoolResponse>.Ok(new BoolResponse { Value = false }));
		Services.AddSingleton(service);
		Services.AddScoped<IValidator<RegisterRequest>>(_ =>
			new RegisterRequestValidator(service, NullLogger<RegisterRequestValidator>.Instance));
		var component = Render<Register>();
		var inputs = component.FindAll("fluent-text-input");
		inputs[0].Change("designer@example.com");
		inputs[1].Change("aaaaaaaa");

		await component.Find("form").SubmitAsync();

		var navigation = Services.GetRequiredService<BunitNavigationManager>();
		var entry = navigation.History.ShouldHaveSingleItem();
		entry.Options.ForceLoad.ShouldBeFalse();
		navigation.Uri.ShouldBe(navigation.ToAbsoluteUri("/Account/Login").ToString());
	}
```

(Mirror the exact `Returns(...)` construction style the file's existing tests use if `Outcome<T>.Ok` reads differently there — the existing three tests are the authority.)

- [ ] **Step 2: Run, verify red** — `dotnet test Heimdall/tests/AuthN.Components.FluentUI.Tests -- --filter-class "*.RegisterTests"`. Expected: new test FAILS (`ForceLoad` is `true`).

- [ ] **Step 3: Implement** — in `Register.razor`, keep `@inject NavigationManager Navigation` and change only the continuation:

```razor
    // Registration signs nobody in -- the handler creates the user and answers with the login page
    // (the email-confirmation page the day that flow lands). No principal changed, so no session
    // transition: an ordinary soft navigation to the server-resolved hop is the honest move.
    Task HandleRegisterAsync(EditContext editContext) =>
        SubmitAsync(editContext, ct => AuthenticationService.Register(_request, ct),
            result => Navigation.NavigateTo(result.NextUrl));
```

- [ ] **Step 4: Run all Register tests, verify green.**
- [ ] **Step 5: Stage** — `git -C Heimdall add src/AuthN.Components.FluentUI/Register.razor tests/AuthN.Components.FluentUI.Tests/RegisterTests.cs`

### Task 7: Logout becomes click-to-confirm

**Files:**
- Modify: `Heimdall/src/AuthN.Components/Logout.razor` (full rewrite below)
- Modify: `Heimdall/tests/AuthN.Components.Tests/LogoutTests.cs` (full rewrite below)
- Modify: `Heimdall/CLAUDE.md` (step 5)

**Interfaces:**
- Consumes: `OutcomeComponentBase.DispatchAsync` + `Problem` + `IsDispatching` (Task 2), `ISessionTransition` (Task 4), `IAuthenticationService.Logout(CancellationToken)`.

- [ ] **Step 1: Rewrite the tests** — replace the entire body of `LogoutTests.cs` (its two navigation-asserting tests describe the dead GET behavior):

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Contracts;
using Norse.AuthN.Services;

namespace Norse.AuthN.Components.Tests;

public sealed class LogoutTests : BunitContext
{
	readonly IAuthenticationService _service = Substitute.For<IAuthenticationService>();
	readonly ISessionTransition _sessionTransition = Substitute.For<ISessionTransition>();

	public LogoutTests()
	{
		Services.AddSingleton(_service);
		Services.AddSingleton(_sessionTransition);
	}

	[Fact]
	void A_bare_render_performs_no_sign_out()
	{
		Render<Logout>();

		_service.DidNotReceiveWithAnyArgs().Logout();
		_sessionTransition.DidNotReceiveWithAnyArgs().Begin(null!);
	}

	[Fact]
	void The_confirm_click_dispatches_and_begins_the_session_transition()
	{
		_service.Logout(Arg.Any<CancellationToken>())
			.Returns(Outcome<NavigationResult>.Ok(new NavigationResult { NextUrl = "/" }));
		var page = Render<Logout>();

		page.Find("button").Click();

		_sessionTransition.Received(1).Begin(new() { NextUrl = "/" });
	}

	[Fact]
	void A_deferred_completion_url_rides_the_same_transition()
	{
		_service.Logout(Arg.Any<CancellationToken>())
			.Returns(Outcome<NavigationResult>.Ok(new NavigationResult { NextUrl = "/_auth/complete?key=abc&returnUrl=%2F" }));
		var page = Render<Logout>();

		page.Find("button").Click();

		_sessionTransition.Received(1).Begin(new() { NextUrl = "/_auth/complete?key=abc&returnUrl=%2F" });
	}

	[Fact]
	void A_failed_sign_out_renders_the_problem_and_never_transitions()
	{
		_service.Logout(Arg.Any<CancellationToken>())
			.Returns(Outcome<NavigationResult>.Err(ErrorCategory.Fault, correlationId: Guid.Empty));
		var page = Render<Logout>();

		page.Find("button").Click();

		page.Find(".alert-danger").TextContent.ShouldContain("still signed in");
		_sessionTransition.DidNotReceiveWithAnyArgs().Begin(null!);
	}
}
```

(If the `Err(ErrorCategory, correlationId:)` overload doesn't exist in this shape, use any `Failed`-producing construction the realm's existing tests use — the assertion is category-agnostic.)

- [ ] **Step 2: Run, verify red** — `dotnet test Heimdall/tests/AuthN.Components.Tests -- --filter-class "*.LogoutTests"`. Expected: FAIL (`A_bare_render_performs_no_sign_out` — the current page signs out in `OnInitializedAsync`; no button exists).

- [ ] **Step 3: Rewrite `Logout.razor`** —

```razor
@page "/Account/Logout"
@inherits OutcomeComponentBase
@inject IAuthenticationService AuthenticationService
@inject ISessionTransition SessionTransition

<PageTitle>Log out</PageTitle>

@if (Problem is not null)
{
    <div class="alert alert-danger" role="alert">Sign-out failed — you are still signed in.</div>
}

<button class="btn btn-primary" disabled="@IsDispatching" @onclick="HandleLogoutAsync">Log out</button>

@code {

    // Click-to-confirm, never a state-changing GET: a bare render performs nothing. Declare-success-
    // only: Failed renders through the base's Problem; only Success -- a genuine principal change --
    // begins the session transition. NextUrl is always server-resolved (a plain "/" or the deferred
    // sign-in completion endpoint); the client navigates it unconditionally.
    Task HandleLogoutAsync() =>
        DispatchAsync(ct => AuthenticationService.Logout(ct),
            result => SessionTransition.Begin(result));

}
```

- [ ] **Step 4: Run tests, verify green** — then the whole realm: `dotnet test Heimdall/Heimdall.slnx`. Expected: all PASS.
- [ ] **Step 5: Boy-scout docs** — `Heimdall/CLAUDE.md` Architecture Facts gains one bullet: **"The session-transition seam is realm law (2026-08-11):** `ISessionTransition` / internal `ForceLoadSessionTransition` / `AddNorseSessionTransition()` live in `AuthN.Components` — only the gate performs a forced document load (NORSE074 enforces). Login begins the transition on success; Register soft-navigates (its handler signs nobody in); Logout is click-to-confirm on Asgard's `OutcomeComponentBase` — a bare GET renders a button and performs nothing. Spec: `../Glitnir/docs/Asgard/specs/2026-08-11-session-transition-seam-design.md`." Update the `AuthN.Components` row in the project table to mention the seam. Mirror in `README.md` if it describes the same surface.
- [ ] **Step 6: Stage** — `git -C Heimdall add src/AuthN.Components/Logout.razor tests/AuthN.Components.Tests/LogoutTests.cs CLAUDE.md` (plus README.md if edited).

### Task 8: GATE — Heimdall ships

- [ ] `dotnet build Heimdall/Heimdall.slnx && dotnet test Heimdall/Heimdall.slnx` — zero warnings, all green.
- [ ] Show staged diff stat and **STOP** for the human gate (commit → PR → CI → tag → publish). Note for the human: Himinbjörg's `Identity.Web.Server` hosts these pages and floats on `Version="*"` — its next CI run picks the new Heimdall up; no Himinbjörg code change is expected (the seam registration is host-side, Task 9).

---

## GATE 3 — Yggdrasil

### Task 9: Host registrations + RedirectToLogin soft nav

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Server/Program.cs`, `Yggdrasil/src/Hosting.Web.Client/Program.cs`, `Yggdrasil/src/Hosting.Web.Components/RedirectToLogin.razor`
- Create: `Yggdrasil/tests/Hosting.Web.Components.Tests/RedirectToLoginTests.cs`

**Interfaces:**
- Consumes: `AddNorseSessionTransition()` (Task 4; namespace `Norse.AuthN.Components`, arriving through the hosts' existing Heimdall references — verify with `grep -rn "AuthN" Yggdrasil/src/Hosting.Web.Client/Hosting.Web.Client.csproj Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj` before assuming; if the client csproj lacks the reference, the components arrive transitively and the `using` still resolves).

- [ ] **Step 1: Write the failing RedirectToLogin test** — new file (mirror `NavMenuTests.cs`'s harness setup in the same directory if `Render` errors on missing services; RedirectToLogin itself injects only `NavigationManager`):

```csharp
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Hosting.Web.Components.Tests;

public sealed class RedirectToLoginTests : BunitContext
{
	// No setup navigation, deliberately: bUnit's History is stack-ordered (latest first), and a
	// second entry invites asserting on the wrong one. Rendering at the base URI leaves exactly one
	// entry — the redirect itself — so ShouldHaveSingleItem is both the selector and the proof that
	// nothing else navigated.
	[Fact]
	void Redirects_softly_to_the_gate_preserving_the_return_url()
	{
		var navigation = Services.GetRequiredService<BunitNavigationManager>();

		Render<RedirectToLogin>();

		var entry = navigation.History.ShouldHaveSingleItem();
		entry.Options.ForceLoad.ShouldBeFalse();
		navigation.Uri.ShouldBe(navigation.ToAbsoluteUri("Account/Login?returnUrl=%2F").ToString());
	}
}
```

- [ ] **Step 2: Run, verify red** — `dotnet test Yggdrasil/tests/Hosting.Web.Components.Tests -- --filter-class "*.RedirectToLoginTests"`. Expected: FAIL (`ForceLoad` is `true` today).

- [ ] **Step 3: Implement** — in `RedirectToLogin.razor`, drop the `forceLoad` argument and say why:

```razor
@inject NavigationManager NavigationManager

@code {

    /// <inheritdoc />
    // NavigationManager.Uri is always absolute; relativizing keeps the redirect host-agnostic
    // (works the same behind a reverse proxy or a different external hostname than what issued it).
    // Soft navigation deliberately: no principal changed when this fires -- an unauthenticated
    // visitor is being pointed at the gate, and Login renders interactively on every host. The
    // scaffold's forced reload was cargo cult (see the session-transition seam spec).
    protected override void OnInitialized()
    {
        NavigationManager.NavigateTo(
            $"Account/Login?returnUrl={Uri.EscapeDataString($"/{NavigationManager.ToBaseRelativePath(NavigationManager.Uri)}")}");
    }

}
```

- [ ] **Step 4: Register the seam in both hosts** — `Hosting.Web.Server/Program.cs`: in the `builder.Services` chain that opens with `.AddNorseClientComponents()`, add `.AddNorseSessionTransition()` immediately after `.AddNorseClientComponents()`; add `using Norse.AuthN.Components;` (hoisted, alphabetical). `Hosting.Web.Client/Program.cs`: add `.AddNorseSessionTransition()` after `.AddNorseClientComponents()` in its chain; same `using`.

- [ ] **Step 5: Verify** — `dotnet build Yggdrasil/Yggdrasil.slnx && dotnet test Yggdrasil/Yggdrasil.slnx`. Expected: zero warnings; all green — `CompositionTests` boot the real `Program.cs`, so a missing registration fails here, not at runtime.
- [ ] **Step 6: Stage** — `git -C Yggdrasil add src/Hosting.Web.Server/Program.cs src/Hosting.Web.Client/Program.cs src/Hosting.Web.Components/RedirectToLogin.razor tests/Hosting.Web.Components.Tests/RedirectToLoginTests.cs`

### Task 10: GATE — Yggdrasil ships, and the browser verifies the soft navigations

- [ ] **Browser verification rides THIS gate exit (spec §9), not the end of the train** — the two soft navigations are design assumptions that must hold before Bragi and the enforcement package ship on top of them. Run the real host (`dotnet run --project src/Orchestration.AppHost` from Bifröst, or `Hosting.Web.Server` directly) and confirm in a browser (Playwright or manual):
  1. Register with a fresh email → lands on `/Account/Login` via soft navigation (no document reload; the SPA stays alive), form state and login page both render correctly.
  2. Visit an `[Authorize]`-guarded route unauthenticated → `RedirectToLogin` soft-redirects to `Account/Login?returnUrl=…` with the query preserved, and the Login page renders correctly there. **Do not assert the return URL is honored after login** — it never was: `LoginRequest` carries no return URL and `LoginHandler` resolves success to the app root regardless. That pre-existing gap is a carried-forward spec item (§9), not this train's verification target.
  If either shows a hidden dependency on the old forced reload, **STOP — this is a spec-level finding**; report it before any further task.
- [ ] Show staged diff stat and **STOP** for the human gate. Note for the human: standalone CI rides the package crossing, so this gate's PR also carries the `Directory.Packages.props` bumps (`AsgardVersion`, `HeimdallVersion`) once those tags exist — the workspace build needed no pin edits.

---

## GATE 4 — Bragi

### Task 11: Recorder, fake Logout arm, registration

**Files:**
- Create: `Bragi/src/DesignSystem.Stories/RecordingSessionTransition.cs`
- Create: `Bragi/tests/DesignSystem.Stories.Tests/RecordingSessionTransitionTests.cs`
- Modify: `Bragi/src/DesignSystem.Stories/ServiceCollectionExtensions.cs`, `Authentication/FakeAuthenticationService.cs`, `tests/…/Authentication/FakeAuthenticationServiceTests.cs`, `tests/…/ServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: `ISessionTransition` (`Norse.AuthN.Components`, already flowing via the `AuthN.Components.FluentUI` NorseRef).
- Produces: internal `RecordingSessionTransition` with `internal IReadOnlyList<NavigationResult> Transitions`; `AddNorseStoryFakes()` registers it as both itself and `ISessionTransition` (same singleton). Task 12/13 assert against it.

- [ ] **Step 1: Write the failing tests** —

New `RecordingSessionTransitionTests.cs`:

```csharp
using Norse.Abstractions.Contracts;

namespace Norse.DesignSystem.Stories.Tests;

public sealed class RecordingSessionTransitionTests
{
	[Fact]
	void Records_every_begun_transition_in_order()
	{
		RecordingSessionTransition transition = new();

		transition.Begin(new() { NextUrl = "/" });
		transition.Begin(new() { NextUrl = "/Account/LoginWith2fa" });

		transition.Transitions.Select(t => t.NextUrl).ShouldBe(["/", "/Account/LoginWith2fa"]);
	}
}
```

Append to `ServiceCollectionExtensionsTests.cs` (add `using Norse.AuthN.Components;` hoisted):

```csharp
	[Fact]
	void Registers_the_recorder_as_the_catalogs_session_transition()
	{
		using var provider = Build();
		provider.GetRequiredService<ISessionTransition>()
			.ShouldBeSameAs(provider.GetRequiredService<RecordingSessionTransition>());
	}
```

Append to `FakeAuthenticationServiceTests.cs` (mirroring its `CreateFake` helper):

```csharp
	[Fact]
	async Task Logout_under_Success_returns_the_root_next_url()
	{
		var outcome = await CreateFake(AuthenticationScenario.Success).Logout(TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Success<NavigationResult> success).ShouldBeTrue();
		success.Value.NextUrl.ShouldBe("/");
	}

	[Fact]
	async Task Logout_under_Fault_pins_the_catalog_correlation_id()
	{
		var outcome = await CreateFake(AuthenticationScenario.Fault).Logout(TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Fault);
	}

	[Fact]
	async Task Logout_under_an_inapplicable_scenario_throws_the_authoring_error()
	{
		await Should.ThrowAsync<InvalidOperationException>(() =>
			CreateFake(AuthenticationScenario.LockedOut).Logout(TestContext.Current.CancellationToken));
	}
```

- [ ] **Step 2: Run, verify red** — `dotnet test Bragi/tests/DesignSystem.Stories.Tests`. Expected: compile failure (`RecordingSessionTransition` missing) and, once compiling, the Logout facts fail against the current `NotImplementedException` arm.

- [ ] **Step 3: Implement** —

New `src/DesignSystem.Stories/RecordingSessionTransition.cs`:

```csharp
using Norse.Abstractions.Contracts;
using Norse.AuthN.Components;

namespace Norse.DesignSystem.Stories;

/// <summary>
///     The catalog's <see cref="ISessionTransition" />: suppress and record. A transition that begins
///     here never completes — the canvas stays put — and the recording is the assertable trace that a
///     story reached a principal transition. What the tests read; deliberately not surfaced in the
///     canvas. Singleton, same as every catalog fake (WASM makes scoped effectively singleton anyway).
/// </summary>
sealed class RecordingSessionTransition : ISessionTransition
{
	readonly List<NavigationResult> _transitions = [];

	/// <summary>Every transition begun, in order.</summary>
	internal IReadOnlyList<NavigationResult> Transitions =>
		_transitions;

	public void Begin(NavigationResult result) =>
		_transitions.Add(result);
}
```

In `ServiceCollectionExtensions.AddNorseStoryFakes()`, extend the chain after `.AddSingleton<IAuthenticationService, FakeAuthenticationService>()`:

```csharp
				.AddSingleton<RecordingSessionTransition>()
				.AddSingleton<ISessionTransition>(static provider => provider.GetRequiredService<RecordingSessionTransition>())
```

(add `using Norse.AuthN.Components;` hoisted; update the method's `<summary>` to mention the recorder in one clause).

In `FakeAuthenticationService`, replace the throwing `Logout` with:

```csharp
	public Task<Outcome<NavigationResult>> Logout(CancellationToken cancellationToken = default) =>
		Task.FromResult(scenario.Value switch
		{
			AuthenticationScenario.Success =>
				Outcome<NavigationResult>.Ok(new NavigationResult { NextUrl = "/" }),
			AuthenticationScenario.Fault =>
				Outcome<NavigationResult>.Err(ErrorCategory.Fault, correlationId: CatalogCorrelationId),
			_ => throw new InvalidOperationException($"Scenario {scenario.Value} does not apply to Logout."),
		});
```

- [ ] **Step 4: Run the whole Bragi suite, verify green** — `dotnet test Bragi/Bragi.slnx`.
- [ ] **Step 5: Stage** — `git -C Bragi add src/DesignSystem.Stories/RecordingSessionTransition.cs src/DesignSystem.Stories/ServiceCollectionExtensions.cs src/DesignSystem.Stories/Authentication/FakeAuthenticationService.cs tests/DesignSystem.Stories.Tests/RecordingSessionTransitionTests.cs tests/DesignSystem.Stories.Tests/ServiceCollectionExtensionsTests.cs tests/DesignSystem.Stories.Tests/Authentication/FakeAuthenticationServiceTests.cs`

### Task 12: `StoryDriver` gains `ClickOnly`

**Files:**
- Modify: `Bragi/src/DesignSystem.Stories/Scenarios/StoryDriverMode.cs`, `Scenarios/StoryDriver.razor`, `wwwroot/storyDriver.js`, `tests/…/Scenarios/StoryDriverTests.cs`, `tests/…/Scenarios/storyDriver.test.mjs`

**Interfaces:**
- Produces: `StoryDriverMode.ClickOnly = 3`; JS module export `driveClick(root)`. Task 13's Logout story consumes.

- [ ] **Step 1: Write the failing C# tests** — append to `StoryDriverTests` (its existing tests are the pattern source; these use the same `SetupModule`/`Setup<bool>`/`SetResult` machinery):

```csharp
	[Fact]
	void Click_only_invokes_the_click_driver_and_reports_complete()
	{
		var module = JSInterop.SetupModule("./_content/Norse.DesignSystem.Stories/storyDriver.js");
		var driveClick = module.Setup<bool>("driveClick", _ => true);
		var component = Render<StoryDriver>(parameters =>
			parameters
				.Add(p => p.Mode, StoryDriverMode.ClickOnly)
				.AddChildContent("<button>Log out</button>"));

		var marker = component.Find("[data-norse-story-driver-state]");
		marker.GetAttribute("data-norse-story-driver-state").ShouldBe("pending");
		var invocation = module.Invocations["driveClick"].Single();
		invocation.Arguments.Count.ShouldBe(1);
		invocation.Arguments[0].ShouldBeElementReferenceTo(marker);

		driveClick.SetResult(true);
		component.WaitForAssertion(() =>
			component.Find("[data-norse-story-driver-state]")
				.GetAttribute("data-norse-story-driver-state").ShouldBe("complete"));
	}

	[Fact]
	async Task A_click_driver_that_finds_no_button_throws()
	{
		var module = JSInterop.SetupModule("./_content/Norse.DesignSystem.Stories/storyDriver.js");
		var driveClick = module.Setup<bool>("driveClick", _ => true);
		Render<StoryDriver>(parameters =>
			parameters.Add(p => p.Mode, StoryDriverMode.ClickOnly));

		driveClick.SetResult(false);
		(await Renderer.UnhandledException)
			.ShouldBeOfType<InvalidOperationException>()
			.Message.ShouldBe("StoryDriver found no button to drive.");
	}
```

- [ ] **Step 2: Write the failing JS tests** — in `storyDriver.test.mjs`, extend the import to `import { drive, driveClick } from "../../../src/DesignSystem.Stories/wwwroot/storyDriver.js";` and append (the file's `TestMutationObserver` global wires `root.observer`; the settle machinery is real, so the first test proves the observer fires *after* the click — if `driveClick` never observed the root, `completion` would never resolve and node:test would time the case out):

```js
test("driveClick clicks the story's button and resolves only after the observer settles", async () => {
	let clicks = 0;
	const root = {
		observer: undefined,
		querySelector(selector) {
			assert.equal(selector, "button");
			return button;
		}
	};
	const button = {
		click() {
			clicks++;
			// The DOM change the click causes lands on the observer AFTER the click — the settle
			// promise must not resolve before this callback runs.
			setTimeout(() => root.observer.callback(), 0);
		}
	};

	const driven = await driveClick(root);

	assert.equal(driven, true);
	assert.equal(clicks, 1);
});

test("driveClick resolves false when no button ever appears", async () => {
	const root = {
		observer: undefined,
		querySelector() {
			return null;
		}
	};

	const driven = await driveClick(root);

	assert.equal(driven, false);
});
```

- [ ] **Step 3: Run, verify red** — C#: `dotnet test Bragi/tests/DesignSystem.Stories.Tests -- --filter-class "*.StoryDriverTests"` (compile failure: `ClickOnly` missing). JS: `node --test Bragi/tests/DesignSystem.Stories.Tests/Scenarios/storyDriver.test.mjs` (import failure: `driveClick` not exported). The no-button JS case takes ~2s by design (the 40×50ms retry budget runs dry); the settle case ~1s (real 500ms minimum-settle + 250ms quiet timers, same as the existing `drive` test).
- [ ] **Step 4: Implement** —

`StoryDriverMode.cs` — add a member (explicit value, XML doc):

```csharp
	/// <summary>Click the story's first button on load — for driving confirm-style pages (no form).</summary>
	ClickOnly = 3,
```

`storyDriver.js` — append a second export reusing the settle machinery:

```js
export async function driveClick(root) {
	for (let attempt = 0; attempt < maxTries; attempt++) {
		const button = root.querySelector("button");
		if (button) {
			const settled = waitForPostSubmitSettle(root, true);
			button.click();
			settled.markSubmitted();
			await settled.completion;
			return true;
		}
		await new Promise(resolve => setTimeout(resolve, delayMs));
	}
	return false;
}
```

`StoryDriver.razor` — in `OnAfterRenderAsync`, branch the module invocation on the mode (preserve everything else — the `Unspecified` throw, the module import, the `_state` flip):

```csharp
		var driven = Mode is StoryDriverMode.ClickOnly ?
			await module.InvokeAsync<bool>("driveClick", _root) :
			await module.InvokeAsync<bool>("drive", _root, Mode is StoryDriverMode.FillAndSubmit, Email, Password);
		if (!driven)
			throw new InvalidOperationException(Mode is StoryDriverMode.ClickOnly ?
				"StoryDriver found no button to drive." :
				"StoryDriver found no form to drive.");
```

- [ ] **Step 5: Run both test surfaces, verify green.**
- [ ] **Step 6: Stage** — `git -C Bragi add src/DesignSystem.Stories/Scenarios/StoryDriverMode.cs src/DesignSystem.Stories/Scenarios/StoryDriver.razor src/DesignSystem.Stories/wwwroot/storyDriver.js tests/DesignSystem.Stories.Tests/Scenarios/StoryDriverTests.cs tests/DesignSystem.Stories.Tests/Scenarios/storyDriver.test.mjs`

### Task 13: Test inversion, Logout story, CLAUDE.md

**Files:**
- Modify: `Bragi/tests/DesignSystem.Stories.Tests/DrivenStoryNavigationTests.cs`
- Create: `Bragi/src/DesignSystem.Stories/Authentication/Logout.stories.razor`
- Modify: `Bragi/CLAUDE.md`

**Interfaces:**
- Consumes: `RecordingSessionTransition.Transitions` (Task 11), `StoryDriverMode.ClickOnly` (Task 12), Heimdall's reworked `Logout` (Task 7).

- [ ] **Step 1: Rewrite `DrivenStoryNavigationTests`** — the class keeps its constructor and `Fill` helper; changes:

1. Add a property beside `Navigation`:

```csharp
	RecordingSessionTransition SessionTransitions =>
		Services.GetRequiredService<RecordingSessionTransition>();
```

2. Every existing non-navigating test (`Login_validation_errors…`, `Register_validation_errors…`, both pinned theories, all three CountryLookup facts) gains one assertion after `Navigation.History.ShouldBeEmpty();`:

```csharp
		SessionTransitions.Transitions.ShouldBeEmpty();
```

3. Replace `An_unpinned_driven_story_force_navigates_which_is_what_boots_the_catalog_nested` (and its characterization comment) with:

```csharp
	// Inverted 2026-08-11 -- the ignition is neutered, exactly as the old characterization test's own
	// comment demanded. An unpinned driven Login story that reaches the fake's Success arm now begins
	// a session transition the catalog suppresses and records: pin loss stays a loud CI failure HERE,
	// and the canvas stops paying for it with a nested catalog.
	[Fact]
	async Task An_unpinned_driven_login_story_begins_a_session_transition_the_catalog_suppresses()
	{
		var story = Render<Login>();
		Fill(story, "designer@example.com", "aaaaaaaa");

		await story.Find("form").SubmitAsync();

		Navigation.History.ShouldBeEmpty();
		SessionTransitions.Transitions.ShouldHaveSingleItem().NextUrl.ShouldBe("/");
	}

	// Register never transitions: its handler signs nobody in, so Success is an ordinary soft
	// navigation to the server-resolved hop -- a boring wrong-render in the canvas, never a
	// document load, never a nested doll.
	[Fact]
	async Task An_unpinned_driven_register_story_soft_navigates_and_never_transitions()
	{
		var story = Render<Register>();
		Fill(story, "designer@example.com", "aaaaaaaa");

		await story.Find("form").SubmitAsync();

		SessionTransitions.Transitions.ShouldBeEmpty();
		var navigation = Navigation.History.ShouldHaveSingleItem();
		navigation.Options.ForceLoad.ShouldBeFalse();
		navigation.Uri.ShouldBe("/");
	}

	// The suppressed-success state renders identically to the confirm state, so the catalog stages
	// no story for it -- this fact is where that state lives, loudly.
	[Fact]
	void A_confirmed_logout_begins_a_suppressed_session_transition()
	{
		var page = Render<Logout>();

		page.Find("button").Click();

		Navigation.History.ShouldBeEmpty();
		SessionTransitions.Transitions.ShouldHaveSingleItem().NextUrl.ShouldBe("/");
	}
```

4. Update the class's `<summary>` (it still narrates the live hazard): the lock's story is now "a driven story that reaches `Success` records a suppressed transition instead of navigating; these tests are where pin loss stays loud."

- [ ] **Step 2: Run, verify** — `dotnet test Bragi/tests/DesignSystem.Stories.Tests -- --filter-class "*.DrivenStoryNavigationTests"`. Expected: all green (this is post-adoption; red-first was consumed by the upstream realm swaps — the deleted characterization test was the red).

- [ ] **Step 3: Add the Logout story** — new `Authentication/Logout.stories.razor` (4-space indentation, matching sibling story files):

```razor
@attribute [Stories("Authentication/Logout")]
<Stories TComponent="Logout">
    @* Confirm state: the page renders a click-to-confirm button and performs nothing on load. The
       suppressed-transition success state renders identically, so it is asserted in
       DrivenStoryNavigationTests rather than staged as a story that would show nothing. *@
    <Story Name="Default">
        <Template>
            <Logout />
        </Template>
    </Story>
    <Story Name="Sign-out Failed">
        <Template>
            <ScenarioScope Value="AuthenticationScenario.Fault">
                <StoryDriver @key="@("logout-failed")" Mode="StoryDriverMode.ClickOnly">
                    <Logout />
                </StoryDriver>
            </ScenarioScope>
        </Template>
    </Story>
</Stories>
```

- [ ] **Step 4: Update `Bragi/CLAUDE.md`** — the sentence citing Logout as the canonical no-story example becomes: "Non-visual components get no story — headless `Logout.razor` was the canonical example until it became click-to-confirm and earned its story (2026-08-11); the rule stands unchanged for the next non-visual component." Add one sentence to the story-fake paragraph noting `RecordingSessionTransition` (suppress-and-record, registered by `AddNorseStoryFakes()`) and the fake's Logout arm. Mirror in `README.md` if it tells the same story.
- [ ] **Step 5: Full Bragi suite green** — `dotnet test Bragi/Bragi.slnx`.
- [ ] **Step 6: Stage** — `git -C Bragi add tests/DesignSystem.Stories.Tests/DrivenStoryNavigationTests.cs src/DesignSystem.Stories/Authentication/Logout.stories.razor CLAUDE.md` (plus README.md if edited).

### Task 14: GATE — Bragi ships, the catalog verifies in the browser, KNOWN-ISSUES closes

- [ ] **Catalog browser sweep (spec §9 — this gate, not the end of the train):** run the stories pair (`Hosting.Stories.Server` serving `Hosting.Stories.Client`) and execute the KNOWN-ISSUES "Investigation trail" Playwright method over the driven stories: cold loads, sibling-story re-entry, and the full sweep. Expected: zero `beforeunload`/`pagehide` events, zero nested catalog bootstraps, the Logout "Sign-out Failed" story renders its alert, and Register's Success path is a soft wrong-render, never a document load.
- [ ] **Amend `Bragi/KNOWN-ISSUES.md`** — the "hazard that survives both fixes" section closes: the catalog's destructive navigation is neutered (suppress-and-record via `RecordingSessionTransition`); the characterization test is inverted as its comment promised (name `An_unpinned_driven_login_story_begins_a_session_transition_the_catalog_suppresses`); record the sweep's findings and the spec reference (`../Glitnir/docs/Asgard/specs/2026-08-11-session-transition-seam-design.md`). Stage the amendment with the rest of the Bragi diff.
- [ ] Show staged diff stat and **STOP** for the human gate. Note: Bragi's gate PR carries the Heimdall version adoption and the recorder together — its package-mode CI is only green once both are in the same train.

---

## GATE 5 — Svartálfheim

### Task 15: NORSE074 — forced document load outside the gate

**Files:**
- Modify: `Svartalfheim/gen/Architecture.Analyzers/Diagnostics.cs`
- Create: `Svartalfheim/gen/Architecture.Analyzers/ForcedLoadAnalyzer.cs`
- Test: `Svartalfheim/tests/Architecture.Analyzers.Tests/ForcedLoadAnalyzerTests.cs`

**Interfaces:**
- Consumes: `AnalyzerTestHarness` / `ReferenceAssemblies.Bcl` (existing test infra), `RealmIdentity.IsExempt` (read `RealmIdentity.cs` before starting — mirror `SuppressionLawAnalyzer`'s compilation-start jurisdiction shape).
- Produces: `Diagnostics.ForcedLoadOutsideTheGate` (`NORSE074`), used by Task 16's sibling and the gate check.

- [ ] **Step 1: Write the failing tests** — new file. Fixture references need a `NavigationManager` stub compiled from source (the real package is unreferencable here — Svartálfheim sits below everything):

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Norse.Architecture.Analyzers.Tests;

public sealed class ForcedLoadAnalyzerTests
{
	const string ComponentSource = """
		using Microsoft.AspNetCore.Components;
		namespace Norse.Hosting.Web.Components;
		public class RedirectToLogin
		{
			public void Go(NavigationManager navigation) =>
				navigation.NavigateTo("/", forceLoad: true);
		}
		""";

	static MetadataReference NavigationStub() =>
		CSharpCompilation.Create("Microsoft.AspNetCore.Components",
			[CSharpSyntaxTree.ParseText("""
				namespace Microsoft.AspNetCore.Components;
				public readonly struct NavigationOptions
				{
					public bool ForceLoad { get; init; }
					public bool ReplaceHistoryEntry { get; init; }
				}
				public abstract class NavigationManager
				{
					public void NavigateTo(string uri, bool forceLoad = false, bool replace = false) { }
					public void NavigateTo(string uri, NavigationOptions options) { }
				}
				""", AnalyzerTestHarness.ParseOptions)],
			ReferenceAssemblies.Bcl,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
			.ToMetadataReference();

	[Fact]
	async Task A_component_assembly_forcing_a_load_is_convicted()
	{
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new ForcedLoadAnalyzer(), "Norse.Hosting.Web.Components", [NavigationStub()], ComponentSource);
		diagnostics.ShouldContain(d => d.Id == "NORSE074");
	}

	[Fact]
	async Task The_production_implementation_itself_is_absolved()
	{
		const string source = """
			using Microsoft.AspNetCore.Components;
			namespace Norse.AuthN.Components;
			sealed class ForceLoadSessionTransition(NavigationManager navigation)
			{
				public void Begin(string nextUrl) =>
					navigation.NavigateTo(nextUrl, forceLoad: true);
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new ForcedLoadAnalyzer(), "Norse.AuthN.Components", [NavigationStub()], source);
		diagnostics.ShouldBeEmpty();
	}

	// The rejected opt-out, proven closed at both widths: the gate ASSEMBLY is not exempt (its own
	// pages live there), and the implementation's TYPE NAME minted in another assembly is not exempt.
	[Fact]
	async Task Another_type_inside_the_gate_assembly_is_convicted()
	{
		const string source = """
			using Microsoft.AspNetCore.Components;
			namespace Norse.AuthN.Components;
			public class Logout
			{
				public void Go(NavigationManager navigation) =>
					navigation.NavigateTo("/", forceLoad: true);
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new ForcedLoadAnalyzer(), "Norse.AuthN.Components", [NavigationStub()], source);
		diagnostics.ShouldContain(d => d.Id == "NORSE074");
	}

	[Fact]
	async Task The_implementation_type_name_minted_elsewhere_is_convicted()
	{
		const string source = """
			using Microsoft.AspNetCore.Components;
			namespace Norse.AuthN.Components;
			sealed class ForceLoadSessionTransition(NavigationManager navigation)
			{
				public void Begin(string nextUrl) =>
					navigation.NavigateTo(nextUrl, forceLoad: true);
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new ForcedLoadAnalyzer(), "Norse.Sneaky.Components", [NavigationStub()], source);
		diagnostics.ShouldContain(d => d.Id == "NORSE074");
	}

	// Fail-loud: anything not provably soft convicts — the evasions the constant-only draft missed.
	[Fact]
	async Task A_variable_forceLoad_argument_is_convicted()
	{
		const string source = """
			using Microsoft.AspNetCore.Components;
			namespace Norse.Hosting.Web.Components;
			public class Page
			{
				public void Go(NavigationManager navigation)
				{
					bool forced = true;
					navigation.NavigateTo("/", forced);
				}
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new ForcedLoadAnalyzer(), "Norse.Hosting.Web.Components", [NavigationStub()], source);
		diagnostics.ShouldContain(d => d.Id == "NORSE074");
	}

	[Fact]
	async Task A_prebuilt_options_value_is_convicted()
	{
		const string source = """
			using Microsoft.AspNetCore.Components;
			namespace Norse.Hosting.Web.Components;
			public class Page
			{
				public void Go(NavigationManager navigation)
				{
					NavigationOptions options = new() { ForceLoad = true };
					navigation.NavigateTo("/", options);
				}
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new ForcedLoadAnalyzer(), "Norse.Hosting.Web.Components", [NavigationStub()], source);
		diagnostics.ShouldContain(d => d.Id == "NORSE074");
	}

	[Fact]
	async Task An_inline_options_without_ForceLoad_is_clean()
	{
		const string source = """
			using Microsoft.AspNetCore.Components;
			namespace Norse.Hosting.Web.Components;
			public class Page
			{
				public void Go(NavigationManager navigation) =>
					navigation.NavigateTo("/", new NavigationOptions { ReplaceHistoryEntry = true });
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new ForcedLoadAnalyzer(), "Norse.Hosting.Web.Components", [NavigationStub()], source);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task An_explicit_constant_false_is_clean()
	{
		const string source = """
			using Microsoft.AspNetCore.Components;
			namespace Norse.Hosting.Web.Components;
			public class Page
			{
				public void Go(NavigationManager navigation) =>
					navigation.NavigateTo("/", forceLoad: false);
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new ForcedLoadAnalyzer(), "Norse.Hosting.Web.Components", [NavigationStub()], source);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task A_positional_true_is_convicted()
	{
		var source = ComponentSource.Replace("forceLoad: true", "true", StringComparison.Ordinal);
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new ForcedLoadAnalyzer(), "Norse.Reference.Components", [NavigationStub()], source);
		diagnostics.ShouldContain(d => d.Id == "NORSE074");
	}

	[Fact]
	async Task NavigationOptions_with_ForceLoad_true_is_convicted()
	{
		const string source = """
			using Microsoft.AspNetCore.Components;
			namespace Norse.Hosting.Web.Components;
			public class Page
			{
				public void Go(NavigationManager navigation) =>
					navigation.NavigateTo("/", new NavigationOptions { ForceLoad = true });
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new ForcedLoadAnalyzer(), "Norse.Hosting.Web.Components", [NavigationStub()], source);
		diagnostics.ShouldContain(d => d.Id == "NORSE074");
	}

	[Fact]
	async Task A_soft_navigation_is_clean()
	{
		const string source = """
			using Microsoft.AspNetCore.Components;
			namespace Norse.Hosting.Web.Components;
			public class Page
			{
				public void Go(NavigationManager navigation) =>
					navigation.NavigateTo("/Account/Login");
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new ForcedLoadAnalyzer(), "Norse.Hosting.Web.Components", [NavigationStub()], source);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task A_test_assembly_is_exempt()
	{
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new ForcedLoadAnalyzer(), "Norse.Hosting.Web.Components.Tests", [NavigationStub()], ComponentSource);
		diagnostics.ShouldBeEmpty();
	}
}
```

- [ ] **Step 2: Run, verify compile failure** — `dotnet test Svartalfheim/tests/Architecture.Analyzers.Tests -- --filter-class "*.ForcedLoadAnalyzerTests"`.

- [ ] **Step 3: Implement** —

`Diagnostics.cs`: add after `ComponentImpurity`, and update the class `<summary>`'s "All five strikes" sentence to "All seven strikes":

```csharp
	public static readonly DiagnosticDescriptor ForcedLoadOutsideTheGate = new(
		"NORSE074", "Forced document load outside the seam",
		"'{0}' forces (or cannot be proven not to force) a document load — the only absolved call site is ForceLoadSessionTransition itself; a principal transition requests ISessionTransition.Begin, and anything else is not a forced reload's job", Category,
		DiagnosticSeverity.Error, isEnabledByDefault: true, customTags: WellKnownDiagnosticTags.NotConfigurable);
```

New `ForcedLoadAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Norse.Architecture.Analyzers;

/// <summary>
/// NORSE074 — forced document load outside the seam's implementation. A forced reload exists to
/// re-establish a session under a changed principal, and only the gate changes the principal, so the
/// only absolved call site is ForceLoadSessionTransition itself — matched by BOTH its full type name
/// and its assembly, so no other assembly can mint the name and the gate's own pages are convicted
/// like everyone else (an assembly-wide exemption would be the rejected interface opt-out at assembly
/// blast radius). Enforcement is fail-loud: anything not provably soft convicts — a non-constant
/// forceLoad argument, or an options value the analyzer cannot read inline. Runs over generated code
/// deliberately: .razor components compile to auto-generated C#, and the default None would blind
/// the rule to every Razor call site.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ForcedLoadAnalyzer : DiagnosticAnalyzer
{
	const string
		GateAssembly = "Norse.AuthN.Components",
		ImplementationType = "Norse.AuthN.Components.ForceLoadSessionTransition";

	static readonly ImmutableArray<DiagnosticDescriptor> _supportedDiagnostics =
		[Diagnostics.ForcedLoadOutsideTheGate];

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		_supportedDiagnostics;

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static start =>
		{
			if (RealmIdentity.IsExempt(start.Compilation.AssemblyName ?? ""))
				return;
			start.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
		});
	}

	static void AnalyzeInvocation(OperationAnalysisContext context)
	{
		var invocation = (IInvocationOperation)context.Operation;
		if (invocation.TargetMethod.Name != "NavigateTo"
			|| invocation.TargetMethod.ContainingType.ToDisplayString() != "Microsoft.AspNetCore.Components.NavigationManager")
			return;

		// The one absolved call site: the seam's own implementation — type name AND assembly, both.
		// A second type of this name cannot compile in the gate assembly; the name minted anywhere
		// else fails the assembly key.
		if (context.Compilation.AssemblyName == GateAssembly
			&& context.ContainingSymbol.ContainingType?.ToDisplayString() == ImplementationType)
			return;

		if (!invocation.Arguments.Any(IsForced))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Diagnostics.ForcedLoadOutsideTheGate,
			invocation.Syntax.GetLocation(), context.ContainingSymbol.ToDisplayString()));
	}

	static bool IsForced(IArgumentOperation argument) =>
		argument.Parameter?.Name switch
		{
			// Anything not provably the constant false convicts — variables, negations, method
			// results. The omitted-argument default arrives as a constant false and stays clean.
			"forceLoad" =>
				argument.Value.ConstantValue is not { HasValue: true, Value: false },
			// The options overload demands an inline initializer the analyzer can read: a prebuilt
			// options value convicts outright; an inline initializer convicts unless ForceLoad is
			// absent or provably the constant false.
			"options" =>
				argument.Value is not IObjectCreationOperation creation ||
				creation.Initializer?.Initializers
					.OfType<ISimpleAssignmentOperation>()
					.FirstOrDefault(static assignment =>
						assignment.Target is IPropertyReferenceOperation { Property.Name: "ForceLoad" })
					is { } forceLoad
					&& forceLoad.Value.ConstantValue is not { HasValue: true, Value: false },
			_ => false
		};
}
```

If `RealmIdentity.IsExempt` turns out to have a different name or shape, mirror exactly what `SuppressionLawAnalyzer.Initialize` does — that file is the authority for the jurisdiction check.

- [ ] **Step 4: Extend the diagnostic meta-test** — `tests/Architecture.Analyzers.Tests/DiagnosticsTests.cs` enumerates every strike that must be a non-configurable error. Add `[InlineData("NORSE074")]` to `Every_strike_is_a_non_configurable_error` and append `Diagnostics.ForcedLoadOutsideTheGate` to the `All()` collection. Boy-scout while in the file: the test predates NORSE079 and omits it — add `[InlineData("NORSE079")]` and `Diagnostics.SuppressingTheLaw` to `All()` in the same edit.
- [ ] **Step 5: Run tests, verify green** — including `DiagnosticsTests`.
- [ ] **Step 6: Stage** — `git -C Svartalfheim add gen/Architecture.Analyzers/Diagnostics.cs gen/Architecture.Analyzers/ForcedLoadAnalyzer.cs tests/Architecture.Analyzers.Tests/ForcedLoadAnalyzerTests.cs tests/Architecture.Analyzers.Tests/DiagnosticsTests.cs`

### Task 16: NORSE075 — analyzer + real-generated-output unit fixtures

**Files:**
- Modify: `Svartalfheim/gen/Architecture.Analyzers/Diagnostics.cs`
- Create: `Svartalfheim/gen/Architecture.Analyzers/SeamBoundFormAnalyzer.cs`
- Create: `Svartalfheim/tests/Architecture.Analyzers.Tests/RazorGeneratedFixtures.cs`, `SeamBoundFormAnalyzerTests.cs`
- Modify: `Svartalfheim/tests/Architecture.Analyzers.Tests/Architecture.Analyzers.Tests.csproj` (one `PackageReference`)

**Interfaces:**
- Produces: `Diagnostics.ValidSubmitOnSeamBoundForm` (`NORSE075`).
- **Proof obligation (spec §7):** the unit fixtures are REAL Razor-compiler output, captured and committed with provenance — never hand-authored approximations. Task 17 adds the end-to-end build proof.

- [ ] **Step 1: Capture real generated output.** In the session scratchpad (never the repo), create a throwaway Razor class library:
  - `fixture.csproj`: `<Project Sdk="Microsoft.NET.Sdk.Razor"><PropertyGroup><TargetFramework>net11.0</TargetFramework><Nullable>enable</Nullable><EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.AspNetCore.Components.Web" Version="11.*-*" /></ItemGroup></Project>`
  - `StubForm.cs` — the seam-shape stub (the analyzer matches `EditContextFor` by name; Svartálfheim can never reference Asgard):

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Fixture;

public abstract class StubForm : ComponentBase
{
	protected EditContext EditContextFor(object request) =>
		new(request);
}
```

  - `ViolatingForm.razor`: `@inherits Fixture.StubForm` + `<EditForm EditContext="EditContextFor(_request)" OnValidSubmit="Handle"></EditForm>` + `@code { readonly object _request = new(); void Handle(EditContext context) { } }`
  - `CleanForm.razor`: identical but `OnSubmit="Handle"`.
  - `ModelBoundForm.razor`: `<EditForm Model="_request" OnValidSubmit="Handle"></EditForm>` + same `@code` (the scaffold shape NORSE075 must NOT convict).
  - `dotnet build` it; copy the three `obj/Debug/net11.0/generated/**/ViolatingForm_razor.g.cs` (etc.) files' contents into `RazorGeneratedFixtures.cs` as raw-string-literal consts (`ViolatingForm`, `CleanForm`, `ModelBoundForm`), with a header comment recording the SDK version that produced them (`dotnet --version`) and the regeneration recipe (this step). Delete the scratch project.

- [ ] **Step 2: Add the reference the fixtures compile against** — in the test csproj `ItemGroup` (alphabetical): `<PackageReference Include="Microsoft.AspNetCore.Components.Web" Version="11.*-*" />`. (nuget.org package — no Norse dependency inversion.)

- [ ] **Step 3: Write the failing tests** — `SeamBoundFormAnalyzerTests.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace Norse.Architecture.Analyzers.Tests;

public sealed class SeamBoundFormAnalyzerTests
{
	// The generated fixtures reference the real component assemblies — resolved from the test's own
	// runtime, the same way ReferenceAssemblies.Bcl resolves the BCL.
	static readonly MetadataReference[] ComponentReferences =
	[
		MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Components.ComponentBase).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Components.Forms.EditContext).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Components.Forms.EditForm).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Components.Web.HeadContent).Assembly.Location),
	];

	static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> Analyze(string generatedSource) =>
		AnalyzerTestHarness.GetDiagnosticsAsync(new SeamBoundFormAnalyzer(),
			"Norse.AuthN.Components.FluentUI", ComponentReferences,
			RazorGeneratedFixtures.StubFormSource, generatedSource);

	[Fact]
	async Task The_generated_violating_form_is_convicted()
	{
		var diagnostics = await Analyze(RazorGeneratedFixtures.ViolatingForm);
		diagnostics.ShouldContain(d => d.Id == "NORSE075");
	}

	[Fact]
	async Task The_generated_clean_form_passes()
	{
		var diagnostics = await Analyze(RazorGeneratedFixtures.CleanForm);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task A_model_bound_scaffold_form_with_OnValidSubmit_is_not_convicted()
	{
		var diagnostics = await Analyze(RazorGeneratedFixtures.ModelBoundForm);
		diagnostics.ShouldBeEmpty();
	}
}
```

(`RazorGeneratedFixtures.StubFormSource` is a fourth const carrying `StubForm.cs`'s source verbatim, so the generated fixtures compile. If the harness's `Fixture failed to compile` guard trips, add the missing assembly to `ComponentReferences` — the compile error names it. The fully-qualified inline type names here are sanctioned: they exist to resolve assemblies, and hoisting them creates using collisions between `Components` and `Components.Forms` — if IDE0005/style disagrees, hoist per the compiler's demand.)

- [ ] **Step 4: Run, verify compile failure**, then **Step 5: Implement** —

`Diagnostics.cs` addition:

```csharp
	public static readonly DiagnosticDescriptor ValidSubmitOnSeamBoundForm = new(
		"NORSE075", "OnValidSubmit on a seam-bound form",
		"This EditForm binds EditContextFor(...) but handles OnValidSubmit — EditForm's synchronous validation pass runs ahead of SubmitAsync's gate and skips async rules entirely; handle OnSubmit and let <FormValidator/> gate the dispatch", Category,
		DiagnosticSeverity.Error, isEnabledByDefault: true, customTags: WellKnownDiagnosticTags.NotConfigurable);
```

(and the `<summary>` strike count already says seven from Task 15 — leave it.)

New `SeamBoundFormAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Norse.Architecture.Analyzers;

/// <summary>
/// NORSE075 — OnValidSubmit on a seam-bound form. Runs over the Razor compiler's generated C#
/// (GeneratedCodeAnalysisFlags.Analyze is the point, not an accident: *_razor.g.cs is auto-generated,
/// and the default None would blind the rule to every form on the platform). Within a generated
/// render-tree body, an EditForm whose EditContext parameter is produced by EditContextFor(...) and
/// which also carries an OnValidSubmit parameter is convicted at the OnValidSubmit call — EditForm's
/// own synchronous validation pass would run ahead of SubmitAsync's async-aware gate. Model-bound
/// scaffold forms (no EditContextFor) are deliberately outside the law until they adopt the seam.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SeamBoundFormAnalyzer : DiagnosticAnalyzer
{
	static readonly ImmutableArray<DiagnosticDescriptor> _supportedDiagnostics =
		[Diagnostics.ValidSubmitOnSeamBoundForm];

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		_supportedDiagnostics;

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static start =>
		{
			if (RealmIdentity.IsExempt(start.Compilation.AssemblyName ?? ""))
				return;
			var editForm = start.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.Forms.EditForm");
			if (editForm is null)
				return;
			start.RegisterOperationBlockAction(block => AnalyzeBlock(block, editForm));
		});
	}

	static void AnalyzeBlock(OperationBlockAnalysisContext context, INamedTypeSymbol editForm)
	{
		Stack<Frame> frames = new();
		foreach (var block in context.OperationBlocks)
			foreach (var operation in block.Descendants().OfType<IInvocationOperation>())
				switch (operation.TargetMethod.Name)
				{
					case "OpenComponent":
						frames.Push(new Frame(operation.TargetMethod.TypeArguments is [var component]
							&& SymbolEqualityComparer.Default.Equals(component, editForm)));
						break;
					case "CloseComponent" when frames.Count > 0:
						Convict(context, frames.Pop());
						break;
					case "AddComponentParameter" or "AddAttribute" when frames.Count > 0:
						Record(frames.Peek(), operation);
						break;
				}
	}

	static void Convict(OperationBlockAnalysisContext context, Frame frame)
	{
		if (frame is { IsEditForm: true, SeamBound: true, ValidSubmit: not null })
			context.ReportDiagnostic(Diagnostic.Create(
				Diagnostics.ValidSubmitOnSeamBoundForm, frame.ValidSubmit.GetLocation()));
	}

	static void Record(Frame frame, IInvocationOperation operation)
	{
		if (!frame.IsEditForm)
			return;
		if (Argument(operation, 1)?.Value.ConstantValue is not { HasValue: true, Value: string parameterName })
			return;
		switch (parameterName)
		{
			case "EditContext":
				frame.SeamBound = Argument(operation, 2)?.Value.Syntax.DescendantNodesAndSelf()
					.OfType<InvocationExpressionSyntax>()
					.Any(static i => i.Expression.ToString().EndsWith("EditContextFor", StringComparison.Ordinal)) == true;
				break;
			case "OnValidSubmit":
				frame.ValidSubmit = operation.Syntax;
				break;
		}
	}

	static IArgumentOperation? Argument(IInvocationOperation operation, int ordinal) =>
		operation.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == ordinal);

	sealed class Frame(bool isEditForm)
	{
		internal bool IsEditForm { get; } = isEditForm;
		internal bool SeamBound { get; set; }
		internal SyntaxNode? ValidSubmit { get; set; }
	}
}
```

If the captured fixture reveals a different generated call shape (e.g. the parameter-name argument at a different ordinal, or `AddComponentParameter` replaced by another builder method in this SDK), adjust `Record`/`Argument` to match the fixture — the fixture is the truth, which is exactly why it must be real generator output.

- [ ] **Step 6: Extend the diagnostic meta-test** — add `[InlineData("NORSE075")]` to `DiagnosticsTests.Every_strike_is_a_non_configurable_error` and append `Diagnostics.ValidSubmitOnSeamBoundForm` to its `All()` collection.
- [ ] **Step 7: Run tests, verify green** — including `DiagnosticsTests`.
- [ ] **Step 8: Stage** — `git -C Svartalfheim add gen/Architecture.Analyzers/Diagnostics.cs gen/Architecture.Analyzers/SeamBoundFormAnalyzer.cs tests/Architecture.Analyzers.Tests/RazorGeneratedFixtures.cs tests/Architecture.Analyzers.Tests/SeamBoundFormAnalyzerTests.cs tests/Architecture.Analyzers.Tests/DiagnosticsTests.cs tests/Architecture.Analyzers.Tests/Architecture.Analyzers.Tests.csproj`

### Task 17: NORSE075 end-to-end build proof

**Files:**
- Create: `Svartalfheim/tests/Architecture.Analyzers.Tests/BuildFixtures/` (four template files), `SeamBoundFormBuildProofTests.cs`
- Modify: `Svartalfheim/tests/Architecture.Analyzers.Tests/Architecture.Analyzers.Tests.csproj` (copy fixtures to output)

This is the spec's proof obligation made executable end to end: a real `dotnet build` of a real Razor project, with the analyzer attached, must fail on `OnValidSubmit` + `EditContextFor` and pass on `OnSubmit` — proving detection against whatever the *current* SDK's Razor generator actually emits, so an SDK bump that changes the generated shape breaks THIS test, not production silently.

- [ ] **Step 1: Create `BuildFixtures/`** — `StubForm.cs`, `ViolatingForm.razor`, `CleanForm.razor` exactly as captured in Task 16 Step 1, plus `fixture.csproj.template`:

The proof must be **SDK-hermetic**: the scratch directory sits outside the repository, so without its own `global.json` the fixture would build with whatever ambient SDK the machine defaults to — a different Razor generator than the realm actually builds with, or an outright failure before the analyzer runs. The test copies the realm's own `global.json` into the scratch directory (runtime copy, not a committed duplicate, so it can never drift from the realm baseline).

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<PropertyGroup>
		<TargetFramework>net11.0</TargetFramework>
		<Nullable>enable</Nullable>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.AspNetCore.Components.Web" Version="11.*-*" />
		<ProjectReference Include="$(NorseAnalyzerProject)" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
	</ItemGroup>
</Project>
```

In the test csproj, include them in output: `<Content Include="BuildFixtures/**" CopyToOutputDirectory="PreserveNewest" />`.

- [ ] **Step 2: Write the test** —

```csharp
using System.Diagnostics;

namespace Norse.Architecture.Analyzers.Tests;

/// <summary>
///     The NORSE075 proof obligation, end to end: a real dotnet build of a real Razor project with
///     this analyzer attached. Slow by unit standards (seconds, plus a first-run restore) and worth
///     it — an SDK bump that changes the Razor generator's emitted shape fails here, loudly, instead
///     of silently blinding the rule in production.
/// </summary>
public sealed class SeamBoundFormBuildProofTests
{
	static async Task<(int ExitCode, string Output)> BuildFixture(string razorFile)
	{
		var fixtures = Path.Combine(AppContext.BaseDirectory, "BuildFixtures");
		// The analyzer project sits at a fixed offset from the test assembly inside the repo.
		var analyzerProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
			"../../../../../gen/Architecture.Analyzers/Architecture.Analyzers.csproj"));
		File.Exists(analyzerProject).ShouldBeTrue($"analyzer project not found at {analyzerProject}");

		var scratch = Directory.CreateTempSubdirectory("norse075-").FullName;
		// SDK hermeticity: the scratch dir is outside the repo, so it would otherwise build with the
		// machine's ambient SDK — a different Razor generator than the realm's. Copy the realm's own
		// global.json (three levels above the analyzer project) so the fixture builds on the exact
		// pinned baseline; a runtime copy can never drift from it.
		var globalJson = Path.GetFullPath(Path.Combine(analyzerProject, "../../../global.json"));
		File.Exists(globalJson).ShouldBeTrue($"realm global.json not found at {globalJson}");
		File.Copy(globalJson, Path.Combine(scratch, "global.json"));
		File.Copy(Path.Combine(fixtures, "StubForm.cs"), Path.Combine(scratch, "StubForm.cs"));
		File.Copy(Path.Combine(fixtures, razorFile), Path.Combine(scratch, razorFile));
		var csproj = await File.ReadAllTextAsync(Path.Combine(fixtures, "fixture.csproj.template"), TestContext.Current.CancellationToken);
		await File.WriteAllTextAsync(Path.Combine(scratch, "fixture.csproj"),
			csproj.Replace("$(NorseAnalyzerProject)", analyzerProject, StringComparison.Ordinal), TestContext.Current.CancellationToken);

		using Process process = new();
		process.StartInfo = new()
		{
			FileName = "dotnet",
			Arguments = "build fixture.csproj -nologo -v:m",
			WorkingDirectory = scratch,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		process.Start();
		var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken)
			+ await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
		await process.WaitForExitAsync(TestContext.Current.CancellationToken);
		Directory.Delete(scratch, recursive: true);
		return (process.ExitCode, output);
	}

	[Fact]
	async Task A_seam_bound_OnValidSubmit_form_fails_the_real_build_with_NORSE075()
	{
		var (exitCode, output) = await BuildFixture("ViolatingForm.razor");

		exitCode.ShouldNotBe(0, output);
		output.ShouldContain("NORSE075");
	}

	[Fact]
	async Task An_OnSubmit_form_builds_clean()
	{
		var (exitCode, output) = await BuildFixture("CleanForm.razor");

		exitCode.ShouldBe(0, output);
	}
}
```

(If either relative path offset is wrong, fix the number of `../` segments — the `File.Exists` guards name the resolved paths.)

- [ ] **Step 3: Run, verify** — the violating fact must be RED before `SeamBoundFormAnalyzer` exists in the built analyzer assembly and GREEN after Task 16 — since Task 16 already landed, both should pass immediately; verify the violating fixture genuinely fails the build by reading the assertion message on any failure.
- [ ] **Step 4: Full realm suite** — `dotnet build Svartalfheim/Svartalfheim.slnx && dotnet test Svartalfheim/Svartalfheim.slnx`. Zero warnings, all green.
- [ ] **Step 5: Stage** — `git -C Svartalfheim add tests/Architecture.Analyzers.Tests/BuildFixtures tests/Architecture.Analyzers.Tests/SeamBoundFormBuildProofTests.cs tests/Architecture.Analyzers.Tests/Architecture.Analyzers.Tests.csproj`

### Task 18: GATE — Svartálfheim ships; the train closes

- [ ] **Pre-flight sweep — zero violations before the law turns on:** from the workspace root, `rg -n "forceLoad|ForceLoad\s*=" --glob "*/src/**/*.razor" --glob "*/src/**/*.cs"` — the two shapes a forced load is *written* in (the `forceLoad` argument name; a `ForceLoad =` property assignment), deliberately not the bare `ForceLoad` token, which would false-hit every legitimate `ForceLoadSessionTransition` type reference such as the registration in `ServiceCollectionExtensions.cs`. Expected hits: **only** `Heimdall/src/AuthN.Components/ForceLoadSessionTransition.cs`. Any other hit is unconverted work — stop and report it. This textual sweep is advisory triage; the analyzer and the full realm builds are the authoritative enforcement.
- [ ] Show staged diff stat and **STOP** for the human gate. `Norse.Architecture.Analyzers` is delivered by the Ginnungagap scatter with no opt-out — every realm's next CI run enforces NORSE074/075.
- [ ] **Train-closing checks:** the browser verifications already ran at their gates (soft navigations at Task 10, catalog sweep + KNOWN-ISSUES amendment at Task 14) — confirm both actually happened before this gate closes; if either was skipped, it is a blocker here, not a footnote. The spec's §9 carried-forward items (registration/email-verification flow discussion; theme relocation) remain open by design — no action.

---

## Self-Review (performed at authoring)

- **Spec coverage:** §2 contract → Task 4; §3 base + `SubmitAsync` fix → Tasks 1–2; §4 implementation/registration → Tasks 4, 9; §5 adoption (Login/Register/Logout/RedirectToLogin) → Tasks 5–7, 9; §6 recorder + test doctrine + Logout story + CLAUDE.md → Tasks 11–13; §7 NORSE074/075 + proof obligation → Tasks 15–17; §8 gates/order → Tasks 3, 8, 10, 14, 18; §9 soft-nav browser check → Task 10 (Yggdrasil gate exit, per spec); catalog sweep + KNOWN-ISSUES amendment → Task 14 (Bragi gate, per spec).
- **Known judgment calls encoded above:** no "Signed Out" story (renders identically to confirm — asserted in `DrivenStoryNavigationTests` instead, spec §6 as revised); `StoryDriver` gains `ClickOnly` because the spec's driven failed-logout story requires clicking a button, not submitting a form; Svartálfheim fixtures stub `EditContextFor`/`NavigationManager` by name because the forge may never reference Asgard or the gate.
- **Type consistency:** `ISessionTransition.Begin(NavigationResult)` is the only cross-task signature; `RecordingSessionTransition.Transitions` is `IReadOnlyList<NavigationResult>`; `AddNorseSessionTransition()` returns `IServiceCollection`. Checked against every consuming task.
