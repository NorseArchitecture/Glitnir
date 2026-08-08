# Story-Fake Scenario Pattern Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (the platform default per `../../../CLAUDE.md` §2.8; superpowers:executing-plans is the narrow separate-session fallback) paired with superpowers:test-driven-development on every coding task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every reachable state of Bragi's cataloged components a pinned, bookmarkable BlazingStory story, backed by a stateless scenario-keyed fake — the platform's story-fake pattern, first instance.

**Architecture:** A `Scenario<TScenario>` ambient singleton (constructed with its family's initial value) is set declaratively by a `ScenarioScope` wrapper in story markup and read by a stateless `FakeAuthenticationService` switch. Post-submit form states are pinned by a `StoryDriver` component that fills and submits the form via JS interop after first render (Storybook play-function idiom, zero shipping-code changes). The catalog reorganizes by surface (flow pages as states under their surface title) vs widget (own story file). Spec: `../specs/2026-08-08-story-fake-scenario-pattern-design.md`.

**Tech Stack:** .NET 11 preview / C# 15, Blazor WASM RCL (`Microsoft.NET.Sdk.Razor`), BlazingStory `1.*-*`, xUnit v3 on MTP v2 + Shouldly, bUnit (new to Bragi), JS interop module as RCL static web asset.

## Global Constraints

- **No git commits, ever — stage (`git add`) and stop; the human commits.** This overrides every commit step any skill flow includes (`Bragi/CLAUDE.md` header law). Each task's final step stages the diff.
- **Never branch Bifröst.** Bragi realm work happens on a Bragi feature branch: `git -C Bragi checkout -b feature/story-fake-scenario-pattern` before Task 1 — unless a Bragi feature fork is already in flight, in which case commit onto it (one-fork-per-realm rule, 2026-08-03). The Asgard task (Task 9) follows the same rule in Asgard.
- **Tabs; warnings are errors** — a single warning fails `dotnet build Bragi.slnx`.
- **House rules bind every line** (`../../house-rules.md`): target-typed `new()`, collection expressions, expression-bodied members (arrow on declaration line, body indented on next), `sealed` by default with accessibility omitted when default, primary constructors, no string concatenation, `is null`/`is not null`, C# 14 extension blocks, XML docs on src members in ReSharper layout.
- **Tests:** Shouldly assertions; test classes `public sealed`; test methods bare `void`/`async Task` (no modifier), sentence-shaped names with underscores. Runner: `dotnet test Bragi.slnx` (MTP — VSTest `--filter` does NOT work; use `-- --filter-class "*.ClassName"`).
- **No `ConfigureAwait(false)` in `.razor` component code** — component lifecycle code must resume on the renderer's sync context (the `OutcomeFormComponentBase` precedent); Razor-generated source is analyzer-exempt, so no pragma is needed. No `ConfigureAwait` in tests (xUnit1030 is authoritative).
- **Canonical strings are law:** every error message, dictionary key, and the fault correlation ID in this plan are copied verbatim from the spec's §1.3 table (which was verified against realm source). Do not paraphrase them.
- **Exact platform copy:** US English everywhere.

## File Structure

| File | Responsibility |
|---|---|
| `Bragi/src/DesignSystem.Stories/Scenarios/Scenario.cs` | Generic ambient scenario holder (initial value, `Value`, `Reset`) |
| `Bragi/src/DesignSystem.Stories/Scenarios/ScenarioScope.razor` | Story-markup wrapper: sets ambient on render, resets on dispose |
| `Bragi/src/DesignSystem.Stories/Scenarios/StoryDriverMode.cs` | `Unspecified`/`SubmitOnly`/`FillAndSubmit` |
| `Bragi/src/DesignSystem.Stories/Scenarios/StoryDriver.razor` | Post-render form fill+submit via JS module |
| `Bragi/src/DesignSystem.Stories/wwwroot/storyDriver.js` | The JS module (`drive`): retry-find form, fill inputs (shadow-DOM aware), `requestSubmit` |
| `Bragi/src/DesignSystem.Stories/Authentication/AuthenticationScenario.cs` | The AuthN scenario enum |
| `Bragi/src/DesignSystem.Stories/Authentication/FakeAuthenticationService.cs` | Stateless scenario switch (rebuilt) |
| `Bragi/src/DesignSystem.Stories/ServiceCollectionExtensions.cs` | Singleton registrations (modified) |
| `Bragi/src/DesignSystem.Stories/Authentication/*.stories.razor` | Surface-organized stories (modified/renamed/merged) |
| `Bragi/src/DesignSystem.Stories/Primitives/*.stories.razor` | Widget stories (StatusMessage moves here; ModelValidationSummary new) |
| `Bragi/src/DesignSystem.Stories/Primitives/ValidationSummaryHarness.razor` | Story-only EditForm harness seeding a `ValidationMessageStore` |
| `Bragi/src/DesignSystem.Stories/Scenarios.md` | Scenario catalog page (beside `Welcome.md`) |
| `Bragi/tests/DesignSystem.Stories.Tests/Scenarios/ScenarioTests.cs` | Holder + enum-law tests |
| `Bragi/tests/DesignSystem.Stories.Tests/Scenarios/ScenarioScopeTests.cs` | bUnit lifecycle tests |
| `Bragi/tests/DesignSystem.Stories.Tests/Scenarios/StoryDriverTests.cs` | bUnit JS-invocation tests |
| `Bragi/tests/DesignSystem.Stories.Tests/Primitives/ValidationSummaryHarnessTests.cs` | bUnit harness lifecycle tests |
| `Bragi/tests/DesignSystem.Stories.Tests/Authentication/FakeAuthenticationServiceTests.cs` | Parity tests (rewritten) |
| `Bragi/tests/DesignSystem.Stories.Tests/DesignSystem.Stories.Tests.csproj` | SDK → `Microsoft.NET.Sdk.Razor`; + AngleSharp/bunit/M.E.DI refs (Heimdall's proven shape) |
| `Asgard/src/Abstractions.Contracts/ErrorCategory.cs` | `InvalidCredentials` doc-comment correction |
| `Bragi/CLAUDE.md`, `Bragi/README.md` | Pair sync (modified) |

Notes for every task: Bragi's `src/Directory.Build.props` already grants `<InternalsVisibleTo Include="$(AssemblyName).Tests" />` — internal types are test-visible; never escalate to `public`. Bragi's `_Imports.razor` gains `@using Norse.DesignSystem.Stories.Scenarios` in Task 3.

---

### Task 1: `AuthenticationScenario` enum + `Scenario<TScenario>` holder

**Files:**
- Create: `Bragi/src/DesignSystem.Stories/Authentication/AuthenticationScenario.cs`
- Create: `Bragi/src/DesignSystem.Stories/Scenarios/Scenario.cs`
- Test: `Bragi/tests/DesignSystem.Stories.Tests/Scenarios/ScenarioTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `enum AuthenticationScenario` (`Unspecified = 0 … Fault = 7`); `sealed class Scenario<TScenario>(TScenario initialValue) where TScenario : struct, Enum` with `TScenario Value { get; set; }` and `void Reset()`. Namespace `Norse.DesignSystem.Stories.Scenarios` for the holder, `Norse.DesignSystem.Stories.Authentication` for the enum. Both internal (default by omission).

- [ ] **Step 1: Write the failing tests**

`Bragi/tests/DesignSystem.Stories.Tests/Scenarios/ScenarioTests.cs`:

```csharp
using Norse.DesignSystem.Stories.Authentication;
using Norse.DesignSystem.Stories.Scenarios;

namespace Norse.DesignSystem.Stories.Tests.Scenarios;

public sealed class ScenarioTests
{
	[Fact]
	void Starts_at_the_initial_value_supplied_at_construction()
	{
		Scenario<AuthenticationScenario> scenario = new(AuthenticationScenario.Success);
		scenario.Value.ShouldBe(AuthenticationScenario.Success);
	}

	// Object-initializer form is REQUIRED, not stylistic: IDE0017 fires as a build error here (the
	// SDK's latest-Recommended analysis mode promotes it to warning; TreatWarningsAsErrors does the
	// rest — no .editorconfig line involved, verified empirically 2026-08-08).
	[Fact]
	void A_story_can_change_the_ambient_value()
	{
		Scenario<AuthenticationScenario> scenario = new(AuthenticationScenario.Success) { Value = AuthenticationScenario.LockedOut };
		scenario.Value.ShouldBe(AuthenticationScenario.LockedOut);
	}

	[Fact]
	void Reset_restores_the_initial_value()
	{
		Scenario<AuthenticationScenario> scenario = new(AuthenticationScenario.Success) { Value = AuthenticationScenario.Fault };
		scenario.Reset();
		scenario.Value.ShouldBe(AuthenticationScenario.Success);
	}

	// Platform enum law: 0 is the unspecified sentinel, real states start at 1, every value explicit.
	// Pinned so a reordering can never silently renumber a scenario.
	[Theory]
	[InlineData(AuthenticationScenario.Unspecified, 0)]
	[InlineData(AuthenticationScenario.Success, 1)]
	[InlineData(AuthenticationScenario.InvalidCredentials, 2)]
	[InlineData(AuthenticationScenario.LockedOut, 3)]
	[InlineData(AuthenticationScenario.NotAllowed, 4)]
	[InlineData(AuthenticationScenario.RegistrationConflict, 5)]
	[InlineData(AuthenticationScenario.RegistrationValidation, 6)]
	[InlineData(AuthenticationScenario.Fault, 7)]
	void Every_scenario_carries_its_ruled_explicit_value(AuthenticationScenario scenario, int value) =>
		((int)scenario).ShouldBe(value);
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Bragi.slnx -- --filter-class "*.ScenarioTests"`
Expected: does not compile (`AuthenticationScenario`/`Scenario<>` unknown) — compile failure is the red state here.

- [ ] **Step 3: Implement**

`Bragi/src/DesignSystem.Stories/Authentication/AuthenticationScenario.cs`:

```csharp
namespace Norse.DesignSystem.Stories.Authentication;

/// <summary>
///     The states a story can pin <see cref="FakeAuthenticationService" /> into — mirrors what the real
///     flow actually emits (spec §1.3), not the full <c>ErrorCategory</c> enum.
/// </summary>
enum AuthenticationScenario
{
	/// <summary>Sentinel CLR default — never a valid scenario; the fake throws on it.</summary>
	Unspecified = 0,

	/// <summary>Happy path — the holder's initial value, so an unwrapped story renders success.</summary>
	Success = 1,

	/// <summary>Login rejected with the generic anti-enumeration message.</summary>
	InvalidCredentials = 2,

	/// <summary>Login rejected because the account is locked out.</summary>
	LockedOut = 3,

	/// <summary>Login rejected as a precondition failure (sign-in not allowed for the account).</summary>
	NotAllowed = 4,

	/// <summary>Registration rejected because the email is already taken.</summary>
	RegistrationConflict = 5,

	/// <summary>Registration rejected by password policy.</summary>
	RegistrationValidation = 6,

	/// <summary>An unmapped failure with the fixed catalog correlation id.</summary>
	Fault = 7
}
```

`Bragi/src/DesignSystem.Stories/Scenarios/Scenario.cs`:

```csharp
namespace Norse.DesignSystem.Stories.Scenarios;

/// <summary>
///     The ambient scenario a story pins its fake family into. Registered as a singleton per fake
///     family, constructed with that family's initial (happy-path) value — the constructor argument,
///     not the enum's CLR default, is why an unwrapped story renders success while <c>0</c> stays the
///     platform-law sentinel. <see cref="ScenarioScope{TScenario}" /> is the only writer.
/// </summary>
/// <param name="initialValue">The family's happy-path value, restored by <see cref="Reset" />.</param>
sealed class Scenario<TScenario>(TScenario initialValue)
	where TScenario : struct, Enum
{
	// Explicit capture field: using the parameter both in the Value initializer and inside Reset()
	// would trigger CS9124 (double storage) — fatal under warnings-as-errors.
	readonly TScenario _initialValue = initialValue;

	/// <summary>The currently pinned scenario.</summary>
	public TScenario Value { get; set; } = initialValue;

	/// <summary>Restores the initial value — called when a <see cref="ScenarioScope{TScenario}" /> leaves the canvas.</summary>
	public void Reset() =>
		Value = _initialValue;
}
```

(The `ScenarioScope{TScenario}` crefs go live in Task 3; if the doc build complains before then, use `<c>ScenarioScope</c>` and upgrade to `<see cref>` in Task 3.)

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Bragi.slnx -- --filter-class "*.ScenarioTests"`
Expected: PASS (11 tests: 3 facts + 8 theory cases).

- [ ] **Step 5: Stage and stop**

```bash
git -C Bragi add src/DesignSystem.Stories/Authentication/AuthenticationScenario.cs src/DesignSystem.Stories/Scenarios/Scenario.cs tests/DesignSystem.Stories.Tests/Scenarios/ScenarioTests.cs
```

Show the diff. Do not commit.

---

### Task 2: Rebuild `FakeAuthenticationService` as the scenario switch; singleton registrations

**Files:**
- Modify: `Bragi/src/DesignSystem.Stories/Authentication/FakeAuthenticationService.cs` (full rewrite below)
- Modify: `Bragi/src/DesignSystem.Stories/ServiceCollectionExtensions.cs`
- Test: `Bragi/tests/DesignSystem.Stories.Tests/Authentication/FakeAuthenticationServiceTests.cs` (full rewrite below)

**Interfaces:**
- Consumes: `Scenario<AuthenticationScenario>` (Task 1); `IAuthenticationService`, wire records, `Outcome<T>.Ok`/`.Err(category, errors, correlationId)`, `Failed`, `Problem.ModelError` (all existing).
- Produces: `FakeAuthenticationService(Scenario<AuthenticationScenario> scenario)` — primary constructor; `internal const string InvalidCredentialsSentinelEmail = "fail@example.com"`; `internal static readonly Guid CatalogCorrelationId = new("0badc0de-0bad-c0de-0bad-c0de0badc0de")`. `AddNorseStoryFakes()` keeps its exact public signature.

- [ ] **Step 1: Rewrite the test class (failing first)**

Replace `FakeAuthenticationServiceTests.cs` entirely:

```csharp
using Norse.Abstractions.Contracts;
using Norse.AuthN.Services;
using Norse.DesignSystem.Stories.Authentication;
using Norse.DesignSystem.Stories.Scenarios;
using Norse.Primitives;

namespace Norse.DesignSystem.Stories.Tests.Authentication;

public sealed class FakeAuthenticationServiceTests
{
	// One holder + fake per test, pinned to the scenario under test — the fake itself is stateless;
	// all behavior selection lives in the ambient value.
	static FakeAuthenticationService CreateFake(AuthenticationScenario scenario) =>
		new(new Scenario<AuthenticationScenario>(scenario));

	static LoginRequest AnyLogin(string email = "designer@example.com") =>
		new() { Email = email, Password = "aaaaaaaa" };

	static RegisterRequest AnyRegister() =>
		new() { Email = "designer@example.com", Password = "aaaaaaaa" };

	[Fact]
	async Task Login_under_Success_returns_the_root_next_url()
	{
		var outcome = await CreateFake(AuthenticationScenario.Success).Login(AnyLogin(), TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Success<LoginResult> success).ShouldBeTrue();
		success.Value.NextUrl.ShouldBe("/");
	}

	[Fact]
	async Task Login_under_Success_still_honors_the_playground_sentinel_email()
	{
		// Upper-cased deliberately — the comparison is OrdinalIgnoreCase.
		var outcome = await CreateFake(AuthenticationScenario.Success).Login(AnyLogin("FAIL@example.com"), TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.InvalidCredentials);
	}

	[Fact]
	async Task Login_under_InvalidCredentials_pins_the_generic_anti_enumeration_message()
	{
		var outcome = await CreateFake(AuthenticationScenario.InvalidCredentials).Login(AnyLogin(), TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.InvalidCredentials);
		failed.Problem.Errors[string.Empty].ShouldBe(["Invalid email or password."]);
	}

	[Fact]
	async Task Login_under_LockedOut_pins_the_handlers_exact_model_message()
	{
		var outcome = await CreateFake(AuthenticationScenario.LockedOut).Login(AnyLogin(), TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.LockedOut);
		failed.Problem.Errors[string.Empty].ShouldBe(["This account is locked out. Try again later or reset your password."]);
	}

	[Fact]
	async Task Login_under_NotAllowed_pins_the_handlers_exact_model_message()
	{
		var outcome = await CreateFake(AuthenticationScenario.NotAllowed).Login(AnyLogin(), TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.NotAllowed);
		failed.Problem.Errors[string.Empty].ShouldBe(["Sign-in is not allowed for this account."]);
	}

	[Fact]
	async Task Login_under_Fault_carries_the_fixed_catalog_correlation_id()
	{
		var outcome = await CreateFake(AuthenticationScenario.Fault).Login(AnyLogin(), TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Fault);
		failed.Problem.CorrelationId.ShouldBe(FakeAuthenticationService.CatalogCorrelationId);
	}

	[Theory]
	[InlineData(AuthenticationScenario.Unspecified)]
	[InlineData(AuthenticationScenario.RegistrationConflict)]
	[InlineData(AuthenticationScenario.RegistrationValidation)]
	async Task Login_throws_loudly_on_scenarios_that_do_not_apply_to_it(AuthenticationScenario scenario) =>
		await Should.ThrowAsync<InvalidOperationException>(() => CreateFake(scenario).Login(AnyLogin(), TestContext.Current.CancellationToken));

	[Fact]
	async Task Register_under_Success_reports_created()
	{
		var outcome = await CreateFake(AuthenticationScenario.Success).Register(AnyRegister(), TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Success<RegisterResult> success).ShouldBeTrue();
		success.Value.Succeeded.ShouldBeTrue();
	}

	[Fact]
	async Task Register_under_RegistrationConflict_pins_the_exact_email_keyed_dictionary()
	{
		var outcome = await CreateFake(AuthenticationScenario.RegistrationConflict).Register(AnyRegister(), TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Conflict);
		failed.Problem.Errors.Keys.ShouldBe([nameof(RegisterRequest.Email)]);
		failed.Problem.Errors[nameof(RegisterRequest.Email)].ShouldBe(["Email 'taken@example.com' is already taken."]);
	}

	[Fact]
	async Task Register_under_RegistrationValidation_pins_the_exact_three_password_policy_messages()
	{
		// Exactly what the proven "aaaaaaaa" fixture yields (RegisterHandlerTests) — no PasswordTooShort,
		// which Heimdall's client-side MinimumLength(8) makes unreachable through the composed flow.
		var outcome = await CreateFake(AuthenticationScenario.RegistrationValidation).Register(AnyRegister(), TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Validation);
		failed.Problem.Errors.Keys.ShouldBe([nameof(RegisterRequest.Password)]);
		failed.Problem.Errors[nameof(RegisterRequest.Password)].ShouldBe([
			"Passwords must have at least one non alphanumeric character.",
			"Passwords must have at least one digit ('0'-'9').",
			"Passwords must have at least one uppercase ('A'-'Z').",
		]);
	}

	[Fact]
	async Task Register_under_Fault_carries_the_fixed_catalog_correlation_id()
	{
		var outcome = await CreateFake(AuthenticationScenario.Fault).Register(AnyRegister(), TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.CorrelationId.ShouldBe(FakeAuthenticationService.CatalogCorrelationId);
	}

	[Theory]
	[InlineData(AuthenticationScenario.Unspecified)]
	[InlineData(AuthenticationScenario.InvalidCredentials)]
	[InlineData(AuthenticationScenario.LockedOut)]
	[InlineData(AuthenticationScenario.NotAllowed)]
	async Task Register_throws_loudly_on_scenarios_that_do_not_apply_to_it(AuthenticationScenario scenario) =>
		await Should.ThrowAsync<InvalidOperationException>(() => CreateFake(scenario).Register(AnyRegister(), TestContext.Current.CancellationToken));

	[Theory]
	[InlineData(AuthenticationScenario.Success)]
	[InlineData(AuthenticationScenario.RegistrationConflict)]
	[InlineData(AuthenticationScenario.RegistrationValidation)]
	[InlineData(AuthenticationScenario.Fault)]
	async Task EmailExists_reports_not_taken_under_every_scenario_so_driven_registers_reach_the_fake(AuthenticationScenario scenario)
	{
		// Blazilla's async EmailExists rule runs during validation BEFORE submit — if this ever failed
		// or reported taken, the driven Register stories could never reach their pinned server state.
		var outcome = await CreateFake(scenario).EmailExists(new EmailExistsRequest { Email = "anyone@example.com" }, TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Success<BoolResponse> success).ShouldBeTrue();
		success.Value.Value.ShouldBeFalse();
	}

	[Fact]
	async Task Logout_throws_because_a_non_visual_component_never_earns_a_story()
	{
		await Should.ThrowAsync<NotImplementedException>(() => CreateFake(AuthenticationScenario.Success).Logout(TestContext.Current.CancellationToken));
	}
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Bragi.slnx -- --filter-class "*.FakeAuthenticationServiceTests"`
Expected: compile failure (`FakeAuthenticationService` has no scenario constructor yet).

- [ ] **Step 3: Rewrite the fake**

Replace `FakeAuthenticationService.cs` entirely:

```csharp
using Norse.Abstractions.Contracts;
using Norse.AuthN.Services;
using Norse.DesignSystem.Stories.Scenarios;

namespace Norse.DesignSystem.Stories.Authentication;

/// <summary>
///     Catalog-only stand-in for <see cref="IAuthenticationService" /> — never calls Himinbjörg, never
///     touches gRPC. A stateless switch over the ambient <see cref="AuthenticationScenario" />:
///     behavior is selected by the story (via <c>ScenarioScope</c>), never accumulated — the fake holds
///     no mutable state of its own. Canonical outcomes mirror the real producers verbatim
///     (spec §1.3: <c>LoginHandler</c>, <c>RegisterHandler</c>, <c>ExceptionTranslationBehavior</c>);
///     parity tests pin every shape. Scenarios that do not apply to a method throw — a story arming
///     the wrong scenario is an authoring error, and silence would mask it.
/// </summary>
sealed class FakeAuthenticationService(Scenario<AuthenticationScenario> scenario) : IAuthenticationService
{
	/// <summary>
	///     Typed into the Default (playground) story's own Email field to preview the
	///     invalid-credentials state interactively — a garnish beside the pinned stories, never the
	///     pinning mechanism.
	/// </summary>
	internal const string InvalidCredentialsSentinelEmail = "fail@example.com";

	/// <summary>
	///     The fixed catalog correlation id for <see cref="AuthenticationScenario.Fault" />. The real id
	///     is minted per fault by Midgard's <c>ExceptionTranslationBehavior</c> via
	///     <see cref="Guid.NewGuid" />, which would break the identical-render bar pinned stories exist
	///     for; this value is obviously synthetic and never mistakable for a real incident reference.
	/// </summary>
	internal static readonly Guid CatalogCorrelationId = new("0badc0de-0bad-c0de-0bad-c0de0badc0de");

	static readonly Failed _invalidCredentials =
		new(Problem.ModelError(ErrorCategory.InvalidCredentials, "Invalid email or password."));

	// Unconditionally "not taken" under every scenario: Blazilla's async EmailExists rule runs during
	// validation before submit, so any other answer would stop driven Register stories from ever
	// reaching their pinned server state.
	public Task<Outcome<BoolResponse>> EmailExists(EmailExistsRequest request, CancellationToken cancellationToken = default) =>
		Task.FromResult(Outcome<BoolResponse>.Ok(new BoolResponse { Value = false }));

	public Task<Outcome<LoginResult>> Login(LoginRequest request, CancellationToken cancellationToken = default) =>
		Task.FromResult(scenario.Value switch
		{
			AuthenticationScenario.Success when request.Email.Equals(InvalidCredentialsSentinelEmail, StringComparison.OrdinalIgnoreCase) =>
				new Outcome<LoginResult>(_invalidCredentials),
			AuthenticationScenario.Success =>
				Outcome<LoginResult>.Ok(new LoginResult { NextUrl = "/" }),
			AuthenticationScenario.InvalidCredentials =>
				new Outcome<LoginResult>(_invalidCredentials),
			AuthenticationScenario.LockedOut =>
				Outcome<LoginResult>.Err(ErrorCategory.LockedOut,
					new Dictionary<string, string[]> { [string.Empty] = ["This account is locked out. Try again later or reset your password."] }),
			AuthenticationScenario.NotAllowed =>
				Outcome<LoginResult>.Err(ErrorCategory.NotAllowed,
					new Dictionary<string, string[]> { [string.Empty] = ["Sign-in is not allowed for this account."] }),
			AuthenticationScenario.Fault =>
				Outcome<LoginResult>.Err(ErrorCategory.Fault, correlationId: CatalogCorrelationId),
			_ => throw new InvalidOperationException($"Scenario {scenario.Value} does not apply to Login."),
		});

	public Task<Outcome<LogoutResult>> Logout(CancellationToken cancellationToken = default) =>
		throw new NotImplementedException("Logout is a non-visual component and should never be in a story");

	public Task<Outcome<RegisterResult>> Register(RegisterRequest request, CancellationToken cancellationToken = default) =>
		Task.FromResult(scenario.Value switch
		{
			AuthenticationScenario.Success =>
				Outcome<RegisterResult>.Ok(new RegisterResult { Succeeded = true }),
			AuthenticationScenario.RegistrationConflict =>
				Outcome<RegisterResult>.Err(ErrorCategory.Conflict,
					new Dictionary<string, string[]> { [nameof(RegisterRequest.Email)] = ["Email 'taken@example.com' is already taken."] }),
			AuthenticationScenario.RegistrationValidation =>
				Outcome<RegisterResult>.Err(ErrorCategory.Validation,
					new Dictionary<string, string[]>
					{
						[nameof(RegisterRequest.Password)] =
						[
							"Passwords must have at least one non alphanumeric character.",
							"Passwords must have at least one digit ('0'-'9').",
							"Passwords must have at least one uppercase ('A'-'Z').",
						],
					}),
			AuthenticationScenario.Fault =>
				Outcome<RegisterResult>.Err(ErrorCategory.Fault, correlationId: CatalogCorrelationId),
			_ => throw new InvalidOperationException($"Scenario {scenario.Value} does not apply to Register."),
		});
}
```

Update `ServiceCollectionExtensions.AddNorseStoryFakes` (same file, same public signature — only the body and its doc summary change; add `using Norse.DesignSystem.Stories.Scenarios;`):

```csharp
		/// <summary>
		///     Registers the catalog's fake <see cref="IAuthenticationService" /> and its ambient
		///     <see cref="Scenario{TScenario}" /> (initialized to
		///     <see cref="AuthenticationScenario.Success" />) so the authentication stories render and
		///     pin their states with no server context. Singletons deliberately: WASM makes scoped
		///     effectively singleton anyway — say what you mean.
		/// </summary>
		/// <returns>The same service collection instance.</returns>
		public IServiceCollection AddNorseStoryFakes() =>
			services
				.AddSingleton(new Scenario<AuthenticationScenario>(AuthenticationScenario.Success))
				.AddSingleton<IAuthenticationService, FakeAuthenticationService>();
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Bragi.slnx`
Expected: PASS — all `FakeAuthenticationServiceTests`, `ScenarioTests`, and the pre-existing `AssemblyMarkerTests`.

- [ ] **Step 5: Stage and stop**

```bash
git -C Bragi add src/DesignSystem.Stories/Authentication/FakeAuthenticationService.cs src/DesignSystem.Stories/ServiceCollectionExtensions.cs tests/DesignSystem.Stories.Tests/Authentication/FakeAuthenticationServiceTests.cs
```

---

### Task 3: `ScenarioScope` component + bUnit enters the test stack

**Files:**
- Create: `Bragi/src/DesignSystem.Stories/Scenarios/ScenarioScope.razor`
- Modify: `Bragi/src/DesignSystem.Stories/_Imports.razor` (add `@using Norse.DesignSystem.Stories.Scenarios`)
- Modify: `Bragi/tests/DesignSystem.Stories.Tests/DesignSystem.Stories.Tests.csproj` (SDK + bUnit references)
- Test: `Bragi/tests/DesignSystem.Stories.Tests/Scenarios/ScenarioScopeTests.cs`

**Interfaces:**
- Consumes: `Scenario<TScenario>` (Task 1), registered singleton (Task 2).
- Produces: `ScenarioScope<TScenario>` generic component — `[Parameter, EditorRequired] TScenario Value`, `[Parameter] RenderFragment? ChildContent`; sets ambient in `OnParametersSet`, `Reset()`s on `Dispose`. In story markup the type argument is inferred from `Value`.

- [ ] **Step 1: Add bUnit to the test project, mirroring the platform's proven setup**

bUnit v2 is already live under this exact platform (Heimdall's `AuthN.Components.Tests`/`AuthN.Components.FluentUI.Tests`, also Asgard and Yggdrasil) — follow that shape, not a new one. In `Bragi/tests/DesignSystem.Stories.Tests/DesignSystem.Stories.Tests.csproj`: change the SDK to `Microsoft.NET.Sdk.Razor` and mirror Heimdall's reference block (alphabetical, per-project — bUnit is deliberately not hoisted into `tests/Directory.Build.props` anywhere on the platform):

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<ItemGroup>
		<!-- Fix for: https://github.com/bUnit-dev/bUnit/issues/1872 -->
		<PackageReference Include="AngleSharp" Version="*" />
		<PackageReference Include="bunit" Version="*" />
		<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="11.*-*" />
		<ProjectReference Include="..\..\src\DesignSystem.Stories\DesignSystem.Stories.csproj" />
	</ItemGroup>
</Project>
```

`using Bunit;` goes per test file (Heimdall's precedent — it is not a global using). `BunitContext`/`Render<T>` are the proven v2 API names in live platform tests.

- [ ] **Step 2: Write the failing tests**

`Bragi/tests/DesignSystem.Stories.Tests/Scenarios/ScenarioScopeTests.cs`:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Norse.DesignSystem.Stories.Authentication;
using Norse.DesignSystem.Stories.Scenarios;

namespace Norse.DesignSystem.Stories.Tests.Scenarios;

public sealed class ScenarioScopeTests : BunitContext
{
	readonly Scenario<AuthenticationScenario> _scenario = new(AuthenticationScenario.Success);

	public ScenarioScopeTests() =>
		Services.AddSingleton(_scenario);

	[Fact]
	void Rendering_pins_the_ambient_scenario_to_the_declared_value()
	{
		Render<ScenarioScope<AuthenticationScenario>>(parameters =>
			parameters.Add(p => p.Value, AuthenticationScenario.LockedOut));
		_scenario.Value.ShouldBe(AuthenticationScenario.LockedOut);
	}

	[Fact]
	void Re_rendering_with_a_new_value_re_pins_every_time_so_a_persistent_canvas_cannot_leak()
	{
		var cut = Render<ScenarioScope<AuthenticationScenario>>(parameters =>
			parameters.Add(p => p.Value, AuthenticationScenario.LockedOut));
		cut.Render(parameters =>
			parameters.Add(p => p.Value, AuthenticationScenario.Fault));
		_scenario.Value.ShouldBe(AuthenticationScenario.Fault);
	}

	[Fact]
	void Disposal_resets_the_ambient_scenario_to_its_initial_value()
	{
		Render<ScenarioScope<AuthenticationScenario>>(parameters =>
			parameters.Add(p => p.Value, AuthenticationScenario.LockedOut));
		DisposeComponents();
		_scenario.Value.ShouldBe(AuthenticationScenario.Success);
	}

	[Fact]
	void Child_content_renders_inside_the_scope()
	{
		var cut = Render<ScenarioScope<AuthenticationScenario>>(parameters =>
			parameters
				.Add(p => p.Value, AuthenticationScenario.Success)
				.AddChildContent("<p>canvas</p>"));
		cut.Markup.ShouldContain("<p>canvas</p>");
	}
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test Bragi.slnx -- --filter-class "*.ScenarioScopeTests"`
Expected: compile failure (`ScenarioScope<>` unknown).

- [ ] **Step 4: Implement the component**

`Bragi/src/DesignSystem.Stories/Scenarios/ScenarioScope.razor`:

```razor
@typeparam TScenario where TScenario : struct, Enum
@implements IDisposable
@inject Scenario<TScenario> Scenario
@ChildContent

@code {
	/// <summary>The scenario this story pins. Re-applied on every render; reset on dispose.</summary>
	[Parameter, EditorRequired]
	public TScenario Value { get; set; }

	/// <summary>The story canvas this scope wraps.</summary>
	[Parameter]
	public RenderFragment? ChildContent { get; set; }

	// Every render, not just init — BlazingStory's canvas iframe persists across story navigation, so
	// determinism comes from this lifecycle, never from hoping the previous story cleaned up.
	protected override void OnParametersSet() =>
		Scenario.Value = Value;

	public void Dispose() =>
		Scenario.Reset();
}
```

Add `@using Norse.DesignSystem.Stories.Scenarios` to `Bragi/src/DesignSystem.Stories/_Imports.razor` (alphabetical within the `Norse.*` block). Also upgrade the two `<c>ScenarioScope</c>` doc mentions from Task 1 to `<see cref="ScenarioScope{TScenario}" />` if they were downgraded.

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test Bragi.slnx`
Expected: PASS, all classes.

- [ ] **Step 6: Stage and stop**

```bash
git -C Bragi add src/DesignSystem.Stories/Scenarios/ScenarioScope.razor src/DesignSystem.Stories/_Imports.razor tests/DesignSystem.Stories.Tests/DesignSystem.Stories.Tests.csproj tests/DesignSystem.Stories.Tests/Scenarios/ScenarioScopeTests.cs
```

---

### Task 4 (SPIKE — gate for everything after it): `StoryDriver` + JS module + the two proving stories

**Files:**
- Create: `Bragi/src/DesignSystem.Stories/Scenarios/StoryDriverMode.cs`
- Create: `Bragi/src/DesignSystem.Stories/Scenarios/StoryDriver.razor`
- Create: `Bragi/src/DesignSystem.Stories/wwwroot/storyDriver.js`
- Modify: `Bragi/src/DesignSystem.Stories/Authentication/Login.stories.razor` (add `Locked Out`)
- Modify: `Bragi/src/DesignSystem.Stories/Authentication/Register.stories.razor` (add `Invalid Password`)
- Test: `Bragi/tests/DesignSystem.Stories.Tests/Scenarios/StoryDriverTests.cs`

**Interfaces:**
- Consumes: `ScenarioScope` (Task 3), scenario-keyed fake (Task 2), `GateLayout`/`Login`/`Register` (Heimdall, existing), BlazingStory `<Story>`/`<Template>` (existing).
- Produces: `enum StoryDriverMode { Unspecified = 0, SubmitOnly = 1, FillAndSubmit = 2 }`; `StoryDriver` component — `[Parameter, EditorRequired] StoryDriverMode Mode`, `[Parameter] string Email = "designer@example.com"`, `[Parameter] string Password = "aaaaaaaa"`, `[Parameter] RenderFragment? ChildContent`. JS module at `./_content/Norse.DesignSystem.Stories/storyDriver.js` exporting `drive(fill, email, password) -> bool`.

**Spike charter (spec §3):** prove `Login / Locked Out` (synchronous validation) **and** `Register / Invalid Password` (the async `EmailExists` validation path) render their pinned states on load, bookmarkable, before any further story builds on the driver. The JS below anticipates FluentUI web components projecting their native `<input>` into shadow roots and retries while the WASM canvas settles — if reality differs, **adapt the JS inside this task** and record what was learned in the task's handoff; if no adaptation works, **halt and ask**.

- [ ] **Step 1: Write the failing bUnit tests**

`Bragi/tests/DesignSystem.Stories.Tests/Scenarios/StoryDriverTests.cs`:

```csharp
using Bunit;
using Norse.DesignSystem.Stories.Scenarios;

namespace Norse.DesignSystem.Stories.Tests.Scenarios;

public sealed class StoryDriverTests : BunitContext
{
	[Fact]
	void Fill_and_submit_invokes_the_module_with_fill_true_and_the_fixtures()
	{
		var module = JSInterop.SetupModule("./_content/Norse.DesignSystem.Stories/storyDriver.js");
		module.Setup<bool>("drive", true, "taken@example.com", "aaaaaaaa").SetResult(true);
		Render<StoryDriver>(parameters =>
			parameters
				.Add(p => p.Mode, StoryDriverMode.FillAndSubmit)
				.Add(p => p.Email, "taken@example.com")
				.Add(p => p.Password, "aaaaaaaa"));
		module.VerifyInvoke("drive");
	}

	[Fact]
	void Submit_only_invokes_the_module_with_fill_false()
	{
		var module = JSInterop.SetupModule("./_content/Norse.DesignSystem.Stories/storyDriver.js");
		module.Setup<bool>("drive", false, "designer@example.com", "aaaaaaaa").SetResult(true);
		Render<StoryDriver>(parameters =>
			parameters.Add(p => p.Mode, StoryDriverMode.SubmitOnly));
		module.VerifyInvoke("drive");
	}

	[Fact]
	void Unspecified_mode_throws_instead_of_silently_rendering_an_undriven_story()
	{
		Should.Throw<InvalidOperationException>(() =>
			Render<StoryDriver>(parameters =>
				parameters.Add(p => p.Mode, StoryDriverMode.Unspecified)));
	}
}
```

(bUnit surfaces `OnAfterRenderAsync` exceptions from `Render`; if the resolved bUnit version surfaces them on the renderer's unhandled-exception channel instead, assert via that channel — the contract is: Unspecified fails loudly, it never no-ops.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Bragi.slnx -- --filter-class "*.StoryDriverTests"`
Expected: compile failure (`StoryDriver`/`StoryDriverMode` unknown).

- [ ] **Step 3: Implement mode enum, component, and JS module**

`Bragi/src/DesignSystem.Stories/Scenarios/StoryDriverMode.cs`:

```csharp
namespace Norse.DesignSystem.Stories.Scenarios;

/// <summary>How <see cref="StoryDriver" /> drives the form it wraps after first render.</summary>
enum StoryDriverMode
{
	/// <summary>Sentinel CLR default — never valid; the driver throws on it.</summary>
	Unspecified = 0,

	/// <summary>Submit the untouched form — client-side validation is the pinned state.</summary>
	SubmitOnly = 1,

	/// <summary>Fill valid-shaped values, then submit — the armed scenario's server state is the pinned state.</summary>
	FillAndSubmit = 2
}
```

`Bragi/src/DesignSystem.Stories/wwwroot/storyDriver.js`:

```js
// Story-side play function: find the story's form, optionally fill its text/password inputs, submit.
// FluentUI Blazor components may project their native <input> into a shadow root, so the search
// descends one shadow level; the retry loop absorbs WASM canvas settling and web-component upgrade.
const maxTries = 40, delayMs = 50;

function inputsOf(root) {
	const inputs = [...root.querySelectorAll("input")];
	for (const el of root.querySelectorAll("*"))
		if (el.shadowRoot)
			inputs.push(...el.shadowRoot.querySelectorAll("input"));
	return inputs;
}

export async function drive(fill, email, password) {
	for (let attempt = 0; attempt < maxTries; attempt++) {
		const form = document.querySelector("form");
		if (form) {
			if (fill)
				for (const input of inputsOf(form)) {
					const type = (input.getAttribute("type") ?? "text").toLowerCase();
					if (type !== "text" && type !== "email" && type !== "password")
						continue;
					input.value = type === "password" ? password : email;
					input.dispatchEvent(new Event("input", { bubbles: true, composed: true }));
					input.dispatchEvent(new Event("change", { bubbles: true, composed: true }));
				}
			form.requestSubmit();
			return true;
		}
		await new Promise(resolve => setTimeout(resolve, delayMs));
	}
	return false;
}
```

`Bragi/src/DesignSystem.Stories/Scenarios/StoryDriver.razor`:

```razor
@implements IAsyncDisposable
@inject IJSRuntime JS
@ChildContent

@code {
	IJSObjectReference? _module;

	/// <summary>How to drive the wrapped form. Required — <see cref="StoryDriverMode.Unspecified" /> throws.</summary>
	[Parameter, EditorRequired]
	public StoryDriverMode Mode { get; set; }

	/// <summary>The email fill value (FillAndSubmit only).</summary>
	[Parameter]
	public string Email { get; set; } = "designer@example.com";

	/// <summary>
	///     The password fill value (FillAndSubmit only). Defaults to the spec-prescribed "aaaaaaaa"
	///     fixture — passes client-side MinimumLength(8) so the submit reaches the fake, and matches
	///     the RegistrationValidation canonical dictionary by construction.
	/// </summary>
	[Parameter]
	public string Password { get; set; } = "aaaaaaaa";

	/// <summary>The story canvas this driver wraps.</summary>
	[Parameter]
	public RenderFragment? ChildContent { get; set; }

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
	}

	public async ValueTask DisposeAsync()
	{
		if (_module is null)
			return;
		try
		{
			await _module.DisposeAsync();
		}
		catch (JSDisconnectedException)
		{
			// Canvas iframe already tore the circuit down — nothing left to dispose.
		}
	}
}
```

(`JSDisconnectedException` lives in `Microsoft.JSInterop`; add `@using Microsoft.JSInterop` to `_Imports.razor` if not already resolving.)

- [ ] **Step 4: Run to verify tests pass**

Run: `dotnet test Bragi.slnx`
Expected: PASS.

- [ ] **Step 5: Add the two proving stories**

**Every driven story carries a stable, unique `@key` on its `StoryDriver`.** BlazingStory's canvas persists across story navigation and swaps the story fragment at the same render-tree position — two driven stories are structurally identical trees, so without a key Blazor *reuses* the `StoryDriver` instance, `firstRender` never fires again, and the previous form state stays on screen. The key forces recreation, so every navigation re-drives. Key format: kebab-cased `{surface}-{story}`.

`Login.stories.razor` — add after the `Default` story, inside `<Stories>`:

```razor
    <Story Name="Locked Out">
        <Template>
            <ScenarioScope Value="AuthenticationScenario.LockedOut">
                <StoryDriver @key="@("login-locked-out")" Mode="StoryDriverMode.FillAndSubmit">
                    <LayoutView Layout="typeof(GateLayout)">
                        <Login />
                    </LayoutView>
                </StoryDriver>
            </ScenarioScope>
        </Template>
    </Story>
```

`Register.stories.razor` — add inside `<Stories>` (keep the existing `Default` story untouched):

```razor
    <Story Name="Invalid Password">
        <Template>
            <ScenarioScope Value="AuthenticationScenario.RegistrationValidation">
                <StoryDriver @key="@("register-invalid-password")" Mode="StoryDriverMode.FillAndSubmit">
                    <LayoutView Layout="typeof(GateLayout)">
                        <Register />
                    </LayoutView>
                </StoryDriver>
            </ScenarioScope>
        </Template>
    </Story>
```

Add `@using Norse.DesignSystem.Stories.Authentication` to `_Imports.razor` if the enum doesn't already resolve in story files.

- [ ] **Step 6: SPIKE VERIFICATION — human browser gate**

Run: `dotnet watch --project Yggdrasil/src/Hosting.Stories.Server` (from the Bifröst root).
The human verifies, and this task does not pass review until all five hold:

1. `Login / Locked Out` renders "This account is locked out. Try again later or reset your password." in the validation summary **on load**, no interaction.
2. `Register / Invalid Password` renders the three password-policy messages on load — proving the async `EmailExists` validation path completed and submitted.
3. **Driven → driven navigation without reload:** `Login / Locked Out` → `Register / Invalid Password` → back to `Login / Locked Out`, all in one canvas session — each shows its own pinned state (this is the `@key` recreation working; a stale previous form here is a failure).
4. Reloading each story's direct URL reproduces the identical render (bookmarkable).
5. Navigating to `Login / Default` afterward shows a pristine happy-path form (no scenario leak).

If the fill doesn't take (shadow-DOM reality differs from the JS above): adapt `storyDriver.js` within this task and note the finding. If no adaptation works: **halt and ask**.

- [ ] **Step 7: Stage and stop**

```bash
git -C Bragi add src/DesignSystem.Stories/Scenarios/StoryDriverMode.cs src/DesignSystem.Stories/Scenarios/StoryDriver.razor src/DesignSystem.Stories/wwwroot/storyDriver.js src/DesignSystem.Stories/Authentication/Login.stories.razor src/DesignSystem.Stories/Authentication/Register.stories.razor src/DesignSystem.Stories/_Imports.razor tests/DesignSystem.Stories.Tests/Scenarios/StoryDriverTests.cs
```

---

### Task 5: Complete the Login and Register state inventories

**Files:**
- Modify: `Bragi/src/DesignSystem.Stories/Authentication/Login.stories.razor`
- Modify: `Bragi/src/DesignSystem.Stories/Authentication/Register.stories.razor`

**Interfaces:**
- Consumes: everything from Tasks 3–4. No new surface produced.

- [ ] **Step 1: Finalize `Login.stories.razor`**

Full target content (preserve the existing `Default` template's `LayoutView` comment):

```razor
@attribute [Stories("Authentication/Login")]
@using Microsoft.AspNetCore.Components
<Stories TComponent="Login">
    @* Default stays a live playground: type fail@example.com into the Email field to preview the
       invalid-credentials state interactively. The pinned stories below are the catalog's real
       state inventory — see the Scenarios page. *@
    <Story Name="Default">
        <Template>
            @* Login only renders inside GateLayout's 280px column in production -- @layout is a routing-time
               concern the Router applies via RouteView, never triggered by direct instantiation like this, so
               the story wraps it in LayoutView by hand to match what actually ships. *@
            <LayoutView Layout="typeof(GateLayout)">
                <Login @attributes="context.Args" />
            </LayoutView>
        </Template>
    </Story>
    <Story Name="Validation Errors">
        <Template>
            <StoryDriver @key="@("login-validation-errors")" Mode="StoryDriverMode.SubmitOnly">
                <LayoutView Layout="typeof(GateLayout)">
                    <Login />
                </LayoutView>
            </StoryDriver>
        </Template>
    </Story>
    <Story Name="Invalid Credentials">
        <Template>
            <ScenarioScope Value="AuthenticationScenario.InvalidCredentials">
                <StoryDriver @key="@("login-invalid-credentials")" Mode="StoryDriverMode.FillAndSubmit">
                    <LayoutView Layout="typeof(GateLayout)">
                        <Login />
                    </LayoutView>
                </StoryDriver>
            </ScenarioScope>
        </Template>
    </Story>
    <Story Name="Locked Out">
        <Template>
            <ScenarioScope Value="AuthenticationScenario.LockedOut">
                <StoryDriver @key="@("login-locked-out")" Mode="StoryDriverMode.FillAndSubmit">
                    <LayoutView Layout="typeof(GateLayout)">
                        <Login />
                    </LayoutView>
                </StoryDriver>
            </ScenarioScope>
        </Template>
    </Story>
    <Story Name="Not Allowed">
        <Template>
            <ScenarioScope Value="AuthenticationScenario.NotAllowed">
                <StoryDriver @key="@("login-not-allowed")" Mode="StoryDriverMode.FillAndSubmit">
                    <LayoutView Layout="typeof(GateLayout)">
                        <Login />
                    </LayoutView>
                </StoryDriver>
            </ScenarioScope>
        </Template>
    </Story>
</Stories>
```

- [ ] **Step 2: Finalize `Register.stories.razor`**

Same shape: keep `Default` as the existing playground template, then `Validation Errors` (`SubmitOnly`), `Email Taken`, and the Task 4 `Invalid Password` story:

```razor
    <Story Name="Validation Errors">
        <Template>
            <StoryDriver @key="@("register-validation-errors")" Mode="StoryDriverMode.SubmitOnly">
                <LayoutView Layout="typeof(GateLayout)">
                    <Register />
                </LayoutView>
            </StoryDriver>
        </Template>
    </Story>
    <Story Name="Email Taken">
        <Template>
            <ScenarioScope Value="AuthenticationScenario.RegistrationConflict">
                @* Email fill matches the canonical conflict message ("Email 'taken@example.com' is
                   already taken.") so the pinned render is internally coherent. *@
                <StoryDriver @key="@("register-email-taken")" Mode="StoryDriverMode.FillAndSubmit" Email="taken@example.com">
                    <LayoutView Layout="typeof(GateLayout)">
                        <Register />
                    </LayoutView>
                </StoryDriver>
            </ScenarioScope>
        </Template>
    </Story>
```

If the existing `Register.stories.razor` `Default` template's body differs from `Login`'s shape, preserve it as-is — only add stories, never rewrite the playground.

- [ ] **Step 3: Build, then browser-verify every new story**

Run: `dotnet build Bragi.slnx` — green, zero warnings.
Run: `dotnet watch --project Yggdrasil/src/Hosting.Stories.Server` — each new story renders its pinned state on load; `Validation Errors` on both surfaces shows the client-side required/min-length messages with no scenario armed. Then walk the driven stories **in sequence without reloading** (Login: Validation Errors → Invalid Credentials → Locked Out → Not Allowed; then Register: Validation Errors → Email Taken → Invalid Password; then Login / Default) — every hop shows its own pinned state, and Default ends pristine.

- [ ] **Step 4: Stage and stop**

```bash
git -C Bragi add src/DesignSystem.Stories/Authentication/Login.stories.razor src/DesignSystem.Stories/Authentication/Register.stories.razor
```

---

### Task 6: Catalog taxonomy — surfaces absorb their confirmation states

**Files:**
- Rename+modify: `Authentication/Lockout.stories.razor` → `Authentication/TwoFactor.stories.razor`
- Modify: `Authentication/AccessDenied.stories.razor` (title/story name only)
- Rename+modify: `Authentication/ForgotPasswordConfirmation.stories.razor` → `Authentication/ForgotPassword.stories.razor`
- Merge: `Authentication/ResetPasswordConfirmation.stories.razor` + `Authentication/InvalidPasswordReset.stories.razor` → `Authentication/ResetPassword.stories.razor` (delete both sources)
- Rename+modify: `Authentication/ShowRecoveryCodes.stories.razor` → `Authentication/RecoveryCodes.stories.razor`
- Move+modify: `Authentication/StatusMessage.stories.razor` → `Primitives/StatusMessage.stories.razor`

**Interfaces:** none produced; `[Stories("...")]` titles are the deliverable. All under `git mv` so history follows.

**Rule for every file:** change only the `[Stories]` title, the `<Story Name>`, and the filename — **preserve each existing `<Template>` body byte-for-byte** (some may carry `LayoutView` wrappers or comments; the tree is the truth, read before writing).

- [ ] **Step 1: Apply the renames and retitles**

| File (after) | `[Stories]` title (after) | Story name(s) (after) | Renders |
|---|---|---|---|
| `TwoFactor.stories.razor` | `Authentication/Two-Factor` | `Locked Out` | `Lockout` (the routed page the 2FA flow redirects to; the 2FA form itself is not yet portable) |
| `AccessDenied.stories.razor` | `Authentication/Access Denied` | `Default` | `AccessDenied` (authorization page — its own surface, never a Login state) |
| `ForgotPassword.stories.razor` | `Authentication/Forgot Password` | `Email Sent` | `ForgotPasswordConfirmation` (surface exists with only its portable state; the form's `Default` slots in when it ports from Himinbjörg) |
| `ResetPassword.stories.razor` | `Authentication/Reset Password` | `Invalid Link`, `Password Reset` | `InvalidPasswordReset`, `ResetPasswordConfirmation` (one `<Stories TComponent="ResetPasswordConfirmation">` block; the title string drives grouping, `TComponent` is a placeholder until the form ports) |
| `RecoveryCodes.stories.razor` | `Authentication/Recovery Codes` | `Default` | `ShowRecoveryCodes` |
| `Primitives/StatusMessage.stories.razor` | `Primitives/StatusMessage` | (existing story names unchanged) | `StatusMessage` |

Example — the merged `ResetPassword.stories.razor`:

```razor
@attribute [Stories("Authentication/Reset Password")]
<Stories TComponent="ResetPasswordConfirmation">
    <Story Name="Invalid Link">
        <Template>
            <InvalidPasswordReset @attributes="context.Args" />
        </Template>
    </Story>
    <Story Name="Password Reset">
        <Template>
            <ResetPasswordConfirmation @attributes="context.Args" />
        </Template>
    </Story>
</Stories>
```

(Both templates keep their source files' `@attributes="context.Args"` — the byte-preservation rule applies to the merged bodies too.)

(If either source file's template carried a `LayoutView` wrapper, carry it into the merged story unchanged.)

- [ ] **Step 2: Build and browser-verify the sidebar**

Run: `dotnet build Bragi.slnx`, then `dotnet watch --project Yggdrasil/src/Hosting.Stories.Server`.
Expected sidebar exactly per spec §1.2: Login · Register · Two-Factor · Forgot Password · Reset Password · Access Denied · Recovery Codes under Authentication; Loader · StatusMessage under Primitives. No `ForgotPasswordConfirmation`/`ResetPasswordConfirmation`/`InvalidPasswordReset`/`ShowRecoveryCodes`/`Lockout` top-level entries remain.

- [ ] **Step 3: Run tests (unchanged surface, still green)**

Run: `dotnet test Bragi.slnx`
Expected: PASS.

- [ ] **Step 4: Stage and stop**

```bash
git -C Bragi add -A src/DesignSystem.Stories/Authentication src/DesignSystem.Stories/Primitives
```

---

### Task 7: `ModelValidationSummary` harnessed story

**Files:**
- Create: `Bragi/src/DesignSystem.Stories/Primitives/ValidationSummaryHarness.razor`
- Create: `Bragi/src/DesignSystem.Stories/Primitives/ModelValidationSummary.stories.razor`

**Interfaces:**
- Consumes: `ModelValidationSummary` (Heimdall FluentUI — throws without a cascaded `EditContext`, by design).
- Produces: `ValidationSummaryHarness` — `[Parameter] IReadOnlyList<string> Messages`; story-only, internal. Behavioral lifecycle code, so TDD like everything else.

- [ ] **Step 1: Write the failing bUnit tests**

`Bragi/tests/DesignSystem.Stories.Tests/Primitives/ValidationSummaryHarnessTests.cs`:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Norse.DesignSystem.Stories.Primitives;

namespace Norse.DesignSystem.Stories.Tests.Primitives;

public sealed class ValidationSummaryHarnessTests : BunitContext
{
	public ValidationSummaryHarnessTests()
	{
		Services.AddFluentUIComponents();
		// FluentUI components make JS interop calls bunit has no way to know about in advance —
		// loose mode is bunit's own documented answer (Heimdall's ModelValidationSummaryTests precedent).
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Fact]
	void Renders_the_seeded_model_level_messages()
	{
		var cut = Render<ValidationSummaryHarness>(parameters =>
			parameters.Add(p => p.Messages, ["first message", "second message"]));
		cut.Markup.ShouldContain("first message");
		cut.Markup.ShouldContain("second message");
	}

	[Fact]
	void Parameter_changes_replace_the_messages_instead_of_accumulating()
	{
		var cut = Render<ValidationSummaryHarness>(parameters =>
			parameters.Add(p => p.Messages, ["first message"]));
		cut.Render(parameters =>
			parameters.Add(p => p.Messages, ["replacement message"]));
		cut.Markup.ShouldContain("replacement message");
		cut.Markup.ShouldNotContain("first message");
	}
}
```

(If `AddFluentUIComponents` isn't resolvable from Bragi's test project because the FluentUI package only flows transitively without its DI extensions, mirror whatever Heimdall's `AuthN.Components.FluentUI.Tests` csproj does to get it — transitive-first, add a reference only if Heimdall needed one.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Bragi.slnx -- --filter-class "*.ValidationSummaryHarnessTests"`
Expected: compile failure (`ValidationSummaryHarness` unknown).

- [ ] **Step 3: Implement the harness**

`ValidationSummaryHarness.razor`:

```razor
@using Microsoft.AspNetCore.Components.Forms

<EditForm EditContext="_editContext">
    <ModelValidationSummary />
</EditForm>

@code {
	// ModelValidationSummary requires a cascaded EditContext from an EditForm ancestor (it throws
	// without one, deliberately) — this harness is that ancestor, seeding model-level messages the
	// way ApplyServerErrors does: keyed on the empty field name. Fields are initializer/constructor-
	// backed — the null-forgiving `= default!` idiom is reserved for EF navigations (house rules).
	sealed record HarnessModel;

	static readonly HarnessModel _model = new();
	readonly EditContext _editContext = new(_model);
	readonly ValidationMessageStore _store;

	public ValidationSummaryHarness() =>
		_store = new(_editContext);

	/// <summary>The model-level messages the story pins.</summary>
	[Parameter]
	public IReadOnlyList<string> Messages { get; set; } = [];

	protected override void OnParametersSet()
	{
		_store.Clear();
		_store.Add(new FieldIdentifier(_model, string.Empty), Messages);
		_editContext.NotifyValidationStateChanged();
	}
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Bragi.slnx -- --filter-class "*.ValidationSummaryHarnessTests"`
Expected: PASS.

- [ ] **Step 5: Add the story**

`ModelValidationSummary.stories.razor` (messages live in a `@code` field so the markup stays clean and the collection-expression law holds):

```razor
@attribute [Stories("Primitives/ModelValidationSummary")]
<Stories TComponent="ModelValidationSummary">
    <Story Name="Model Errors">
        <Template>
            <ValidationSummaryHarness Messages="_messages" />
        </Template>
    </Story>
</Stories>

@code {
	static readonly IReadOnlyList<string> _messages =
	[
		"Invalid email or password.",
		"This account is locked out. Try again later or reset your password.",
	];
}
```

- [ ] **Step 6: Build and browser-verify**

Run: `dotnet build Bragi.slnx`; then the stories host — `Primitives/ModelValidationSummary → Model Errors` shows both messages in the `FluentMessageBar` on load.

- [ ] **Step 7: Stage and stop**

```bash
git -C Bragi add src/DesignSystem.Stories/Primitives/ValidationSummaryHarness.razor src/DesignSystem.Stories/Primitives/ModelValidationSummary.stories.razor tests/DesignSystem.Stories.Tests/Primitives/ValidationSummaryHarnessTests.cs
```

---

### Task 8: The Scenarios catalog page

**Files:**
- Create: `Bragi/src/DesignSystem.Stories/Scenarios.md` (beside `Welcome.md`, same MD2RazorGenerator mechanism)

- [ ] **Step 1: Write the page**

The front matter is what surfaces the page — `Welcome.md` is discovered because its front matter carries a `CustomPage` attribute; without it, the file is just an orphaned component. Same mechanism, no host-side wiring needed:

```markdown
---
$attribute: CustomPage("Scenarios")
---

# Scenarios

Every pinned story in this catalog selects its state through a **scenario** — an ambient value a
story declares with `ScenarioScope` and the backing fake obeys. Nothing here talks to a server;
nothing here accumulates state. A story renders the same way every time you load it.

## Authentication scenarios

| Scenario | What renders | Canonical shape |
|---|---|---|
| `Success` | Happy path (unwrapped stories) | `NextUrl = "/"` / `Succeeded = true` |
| `InvalidCredentials` | Login's generic rejection | "Invalid email or password." — deliberately never says which credential failed |
| `LockedOut` | Login lockout feedback | "This account is locked out. Try again later or reset your password." |
| `NotAllowed` | Login precondition failure | "Sign-in is not allowed for this account." |
| `RegistrationConflict` | Register email-taken | `Email`: "Email 'taken@example.com' is already taken." |
| `RegistrationValidation` | Register password-policy rejection | `Password`: the three complexity messages the `"aaaaaaaa"` fixture provably yields |
| `Fault` | Unmapped failure | Correlation reference `0badc0de-0bad-c0de-0bad-c0de0badc0de` — fixed and obviously synthetic |

## The playground sentinel

The `Login → Default` story stays interactive: type **`fail@example.com`** into its Email field
(any password) and submit to see the invalid-credentials state live. The sentinel is a garnish —
pinned stories, not magic inputs, are how states are reached in this catalog.

## Driven stories

States that only exist after a submit (`Locked Out`, `Invalid Password`, …) are pinned by
`StoryDriver`, which fills valid-shaped values (password fixture `"aaaaaaaa"`) and submits the form
after first render — the Storybook play-function idiom, with zero changes to shipping components.
```

- [ ] **Step 2: Verify the page surfaces in the catalog**

Run the stories host and confirm `Scenarios` appears in the sidebar exactly the way `Welcome` does (both ride the same `CustomPage` front-matter mechanism through MD2RazorGenerator — this is Bragi-only, no host change).

- [ ] **Step 3: Stage and stop**

```bash
git -C Bragi add src/DesignSystem.Stories/Scenarios.md
```

---

### Task 9: Asgard — correct the `InvalidCredentials` doc comment (ruling 2026-08-08)

**Files:**
- Modify: `Asgard/src/Abstractions.Contracts/ErrorCategory.cs` (one doc comment)

Branch rule: if an Asgard feature fork is in flight, this rides it; otherwise stage on a new `feature/error-category-doc-correction` branch in Asgard. No behavioral change anywhere.

- [ ] **Step 1: Replace the stale comment**

Old:

```csharp
	/// <summary>Invalid credentials provided. Vestigial — not actively produced, per the anti-enumeration ruling.</summary>
	InvalidCredentials = 5,
```

New:

```csharp
	/// <summary>
	///     Invalid credentials provided. Deliberately generic — the anti-enumeration stance means a
	///     login rejection never discloses which credential failed; Himinbjörg's <c>LoginHandler</c>
	///     produces exactly this category with one shared message ("Invalid email or password.").
	///     (Ruled 2026-08-08: a prior claim here that this member was vestigial was the stale side of
	///     a docs-vs-code drift — the working code stands.)
	/// </summary>
	InvalidCredentials = 5,
```

- [ ] **Step 2: Build and test Asgard**

Run: `dotnet build Asgard.slnx` then `dotnet test Asgard.slnx`
Expected: green — doc-only change.

- [ ] **Step 3: Stage and stop**

```bash
git -C Asgard add src/Abstractions.Contracts/ErrorCategory.cs
```

---

### Task 10: Bragi documentation pair sync + CI gate trigger

**Files:**
- Modify: `Bragi/CLAUDE.md`
- Modify: `Bragi/README.md`

- [ ] **Step 1: Rewrite the fake paragraph in `Bragi/CLAUDE.md`**

Replace the entire paragraph beginning `**\`FakeAuthenticationService\` lives here as of 2026-08-07**` with:

```markdown
**The story-fake scenario pattern is live (2026-08-08)** — the deferred design session on `FakeAuthenticationService` ran and shipped: the fake is a stateless switch over an ambient `AuthenticationScenario` (`Scenario<T>` singleton, initialized to `Success`; `0` stays the enum-law sentinel and throws), pinned per story by `ScenarioScope` in story markup, with post-submit states driven on load by `StoryDriver` (`SubmitOnly`/`FillAndSubmit`, JS module `wwwroot/storyDriver.js`, password fixture `"aaaaaaaa"`). Canonical outcomes mirror the real producers verbatim and are pinned by `FakeAuthenticationServiceTests`; the `fail@example.com` sentinel survives only as the `Default` playground garnish, documented on the `Scenarios` catalog page. Doctrine (stateless scenario responders; immutable seed fixtures for data-serving fakes; fakes non-public beside their stories; declarative scenario selection) and the full design: `../Glitnir/docs/Bragi/specs/2026-08-08-story-fake-scenario-pattern-design.md`. Mímir follows this pattern when `Reference.Components.FluentUI` ships. The former parked note about "flow-level login/logout stories" is retired, not deferred — it was a misstatement of the catalog taxonomy below.
```

- [ ] **Step 2: Update the inclusion rule in `Bragi/CLAUDE.md` §1**

Replace the sentence `A component earns a story when it is WASM-clean (ported to the gRPC service seam, no server types in its dependency closure) **and** visually renders.` with:

```markdown
Every WASM-clean, visually-rendering component appears in the catalog — reusable widgets as their own story files under `Primitives/`, flow-surface components as **states under their surface's title** (a confirmation page is the succeeded state of its surface, not a sidebar entry — "Forgot Password → Email Sent", never "ForgotPasswordConfirmation").
```

(Keep the following sentence about non-visual components and `Logout.razor` unchanged.)

- [ ] **Step 3: Update the test-surface and CI paragraphs in `Bragi/CLAUDE.md`**

- In **Build & test**: replace `Today's test surface is a single `AssemblyMarkerTests`; the real surface arrives with the fake's design session (above).` with `The test surface is real: `FakeAuthenticationServiceTests` (scenario parity, pinned verbatim to the spec's §1.3 table), `ScenarioTests`, and bUnit-rendered `ScenarioScopeTests`/`StoryDriverTests`/`ValidationSummaryHarnessTests`.`
- **HUMAN COMPLETION GATE — branch protection first, documentation second.** The CLAUDE.md paragraph below states a fact about branch protection; writing it before the setting is flipped would make authoritative documentation false. Sequence, strictly: (1) Buvy flips the required-check setting on Bragi in GitHub (agents never touch branch protection); (2) Buvy confirms in-session; (3) only then write and stage the replacement of the **Ungated CI** paragraph: `**Gated CI** (flipped 2026-08-08, the fake's design session having fired as the standing trigger) — with a real test surface in place, the `gate / build` check is required by branch protection.` **Task 10 — and the plan as a whole — stays incomplete until the flip is confirmed.** If Buvy defers the flip, this step and the paragraph wait with it; do not write "gated" into the doc on a promise.

- [ ] **Step 4: Mirror at public altitude in `Bragi/README.md`**

Read `Bragi/README.md` first (boy-scout law: the pair tells one story at two altitudes). Weave in, matching its existing tone and structure: (1) stories are pinned states organized by surface, with widgets under Primitives; (2) a scenario-keyed fake backs the catalog — stateless, deterministic, bookmarkable; (3) the `Scenarios` page documents every state and the playground sentinel. Do not paste CLAUDE.md prose verbatim — README is narrative, CLAUDE.md is law.

- [ ] **Step 5: Verify the pair tells one story**

Re-read both changed files end to end; the realm tables and story-inclusion rule must not contradict `Bifrost/CLAUDE.md` §2 or the spec.

- [ ] **Step 6: Stage and stop**

```bash
git -C Bragi add CLAUDE.md README.md
```

---

### Task 11: Final integrated verification — the deliverable is the composed catalog

No new files. Later tasks touched Razor, static assets, documentation, and a second realm after the last full test run — this gate proves the composition, not just the RCL.

- [ ] **Step 1: Full Bragi build and test**

Run: `dotnet build Bragi.slnx` then `dotnet test Bragi.slnx`
Expected: green, zero warnings, every test class passing (`AssemblyMarkerTests`, `ScenarioTests`, `ScenarioScopeTests`, `StoryDriverTests`, `FakeAuthenticationServiceTests`, `ValidationSummaryHarnessTests`).

- [ ] **Step 2: Asgard build and test**

Run: `dotnet build Asgard.slnx` then `dotnet test Asgard.slnx`
Expected: green (Task 9 was doc-only; this proves it).

- [ ] **Step 3: Stories host builds and serves**

Run: `dotnet build Yggdrasil/src/Hosting.Stories.Server` then `dotnet watch --project Yggdrasil/src/Hosting.Stories.Server`
Expected: clean build; catalog serves.

- [ ] **Step 4: Full browser navigation matrix (human)**

In one canvas session, no reloads except where stated:

1. Sidebar matches spec §1.2 exactly (Authentication: Login · Register · Two-Factor · Forgot Password · Reset Password · Access Denied · Recovery Codes; Primitives: Loader · StatusMessage · ModelValidationSummary; plus the `Scenarios` page).
2. Driven chain: Login Validation Errors → Invalid Credentials → Locked Out → Not Allowed → Register Validation Errors → Email Taken → Invalid Password — each hop renders its own pinned state.
3. Cheap states: Two-Factor / Locked Out, Access Denied, Forgot Password / Email Sent, Reset Password both stories, Recovery Codes, ModelValidationSummary / Model Errors — all render on load.
4. Bookmark check: hard-reload the direct URLs of `Login / Locked Out` and `Register / Invalid Password` — identical renders.
5. `Login / Default` last: pristine form; typing `fail@example.com` + any password and submitting shows the sentinel state (playground intact).
6. The `Scenarios` page renders with its table and the fixed correlation id visible.

- [ ] **Step 5: Staged-diff review across repos**

Run: `git -C Bragi status --short`, `git -C Asgard status --short`, `git -C Glitnir status --short`
Expected: every intended file staged, nothing unintended; show the human the summary. **Plan completion additionally requires the Task 10 branch-protection confirmation** — if it's still pending, say so explicitly rather than declaring done.

---

## Self-Review

**Spec coverage:** §1.1 seam → Tasks 1–3; §1.2 taxonomy (sidebar, three non-conflations, harness) → Tasks 5–7; §1.3 fake + canonical shapes → Task 2; §1.4 ruling → Task 9; §3 driver, two modes, two-story spike → Task 4; §4 doctrine → recorded in spec, restated in Task 10's CLAUDE.md text; §5 tests + bUnit + CI gate → Tasks 1–4, 7, 10 (branch protection is an explicit human completion gate, not a side note); §6 doc consequences → Tasks 8–10 (`docs/codenames.md`: no change, per spec). §7 out-of-scope respected: no Mímir code, no shipping-component change anywhere. Task 11 closes the loop on the composed catalog.

**Placeholder scan:** No TBDs. The one adapt-or-halt gate left (shadow-DOM fill in Task 4) states the exact fallback decision path and who decides — spike charter, not placeholder. bUnit follows the platform's proven Heimdall setup, no speculative fallback. Task 6 prescribes exact titles/names per file and orders template bodies preserved byte-for-byte, `@attributes="context.Args"` included in the merged example.

**Type consistency:** `Scenario<TScenario>(initialValue)`/`Value`/`Reset()` consistent across Tasks 1–4; `AuthenticationScenario` members and values identical in enum, tests, stories; `StoryDriverMode.FillAndSubmit`/`SubmitOnly` consistent between component, JS boolean (`fill`), tests, and stories; every driven story carries a unique kebab-cased `@key` (Tasks 4–5) and the spike/Task 5/Task 11 navigation matrices exercise the recreation those keys exist for; `CatalogCorrelationId` GUID string identical in Task 2 code, tests, and Task 8 page; canonical strings appear identically in Task 2 implementation and tests (copied from the spec's verified table).

**Remand round (same day):** all seven reviewer findings verified against source and folded in — `@key` + driven→driven navigation, `CustomPage` front matter (Yggdrasil fallback deleted), branch-protection human completion gate, Task 7 TDD + house-rule field initialization + collection expression, `context.Args` preserved in the merge, bUnit premise corrected to the Heimdall precedent, Task 11 integrated verification added.
