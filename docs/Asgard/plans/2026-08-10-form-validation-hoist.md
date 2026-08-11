# Asgard Form Validation Hoist — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move form validation into `OutcomeFormComponentBase` so no downstream realm ever decides whether a validator has async rules.

**Architecture:** A new `FormValidator` component stamps a Norse-owned marker into `EditContext.Properties` and renders `<FluentValidator AsyncMode="true"/>` with no pass-through parameter. `SubmitAsync` gains two guards ahead of dispatch — the marker check, then validation inside the `IsSubmitting` window. The Blazilla call site exists exactly once on the platform, in Asgard.

**Tech Stack:** .NET 11 preview 6 / C# 15, Blazor components, Blazilla 2.x over FluentValidation, bUnit + xUnit v3 + Shouldly.

## Global Constraints

- .NET 11 preview 6, C# 15. Preview language features permitted.
- Every class is `sealed`, `abstract`, or `static`. Omit accessibility modifiers when adopting the default.
- Target-typed `new()` everywhere the language allows; collection expressions; `var` elsewhere.
- `is null` / `is not null` — never `== null`.
- Expression-bodied members where possible; arrow on the declaration line, body indented on the next.
- Usings hoisted to the top of the file. Inline fully-qualified names banned in hand-written source.
- XML docs on every publicly visible member in `src`. ReSharper doc layout, 120-column wrap.
- `ConfigureAwait(false)` in `src` — **except** inside `SubmitAsync`, where the existing `#pragma warning disable CA2007` stands: component code must resume on the renderer's synchronization context.
- Tests: sentence-shaped method names with underscores. No `ConfigureAwait` in tests (xUnit1030).
- Internals reached via the existing `InternalsVisibleTo Include="$(AssemblyName).Tests"`. Never escalate to `public` for testability.

## Dispute for the Judge — Raise Before Task 1

`house-rules.md` § Extension members states: *"At the callsite, invoke extension methods in extension style 100% of the time. Never static-invocation style."*

This change **requires** static-invocation style and cannot comply. Extension style is precisely the defect: .NET 11 added an instance method `EditContext.ValidateAsync(CancellationToken = default)`, and C# binds instance methods before extension methods, so `editContext.ValidateAsync()` silently retargets away from Blazilla's FluentValidation pass and reports valid unconditionally. That is the bug this plan exists to remove.

Per the document's own instruction — *"If a rule seems wrong for a specific situation, raise it in the plan and let the judge rule; do not silently deviate"* — this plan does not deviate on its own authority. **Requested ruling:** carve an exception for the case where an instance method shadows an extension method of the same name, requiring static-invocation style with the type name hoisted via `using` (never an inline fully-qualified name, which § Usings separately bans).

The form this plan assumes, pending ruling:

```csharp
using Blazilla;
// ...
if (!await EditContextExtensions.ValidateAsync(editContext))
```

If the judge rules otherwise, Task 3 is blocked — there is no extension-style form that reaches Blazilla on .NET 11.

## Evidence Behind the Design

Proven in `scratchpad/spike` (throwaway; assertions transfer to Task 1–3 tests):

| Finding | Consequence |
|---|---|
| No validator attached → `ValidateAsync` returns `true`, zero messages — identical to a genuinely valid form | Marker must be checked **before** dispatch; there is no post-hoc signal |
| `AsyncMode=false` + async rule → returns `true` in 2ms, then `AsyncValidatorInvokedSynchronouslyException` on a ThreadPool thread kills the process | `AsyncMode` must not be settable from markup |
| `AsyncMode=true` + sync-only validator → correctly returns `false` with messages | One shape is safe for every validator |
| Blazilla stashes the pending task in a single `EditContext.Properties` slot; overlapping calls race it | `IsSubmitting` must be set **before** validation, not before dispatch |

Both load-bearing guards were mutation-tested: removing the marker check let a validator-less form dispatch an empty model; moving validation outside the guard let two concurrent submits both dispatch.

## File Structure

| File | Responsibility |
|---|---|
| `src/Abstractions.Components/Abstractions.Components.csproj` | Modify: add Blazilla `PackageReference` |
| `src/Abstractions.Components/FormProperties.cs` | Create: the marker key constant |
| `src/Abstractions.Components/FormValidator.razor` | Create: stamps the marker, renders `FluentValidator` with `AsyncMode` fixed |
| `src/Abstractions.Components/OutcomeFormComponentBase.cs` | Modify: marker guard + validation inside the submit guard |
| `tests/Abstractions.Components.Tests/FormValidatorTests.cs` | Create: marker stamped, no `AsyncMode` parameter, loud outside an `EditForm` |
| `tests/Abstractions.Components.Tests/OutcomeFormComponentBaseTests.cs` | Modify: existing `Harness` must stamp the marker; new guard tests |
| `tests/Abstractions.Components.Tests/FormValidationGateTests.cs` | Create: end-to-end gate over a real validator |

**Naming note:** the component is `FormValidator`, not `NorseValidator`. §2 of Bifröst's CLAUDE.md holds the brand build-injected and never file-encoded; a type named `Norse*` bakes into source what every project folder and `.csproj` deliberately keeps out, and a fork would have to rename it.

**Breaking-change note for Task 2:** the five existing tests in `OutcomeFormComponentBaseTests.cs` call `harness.ContextFor(...)` without rendering a form, so no marker is ever stamped. All five throw once the guard lands. Task 2 updates the `Harness` in the same commit — this is expected, not a regression.

---

### Task 1: FormValidator and its marker

**Files:**
- Modify: `src/Abstractions.Components/Abstractions.Components.csproj`
- Create: `src/Abstractions.Components/FormProperties.cs`
- Create: `src/Abstractions.Components/FormValidator.razor`
- Test: `tests/Abstractions.Components.Tests/FormValidatorTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `Norse.Abstractions.Components.FormProperties.ValidatorAttached` (`const string`), consumed by Task 2's guard. `FormValidator` — a parameterless component, consumed by Task 3's tests and by Heimdall/Mimir later.

- [ ] **Step 1: Add the Blazilla package reference**

In `src/Abstractions.Components/Abstractions.Components.csproj`, add to the existing `ItemGroup`, keeping alphabetical order ahead of `Microsoft.AspNetCore.Components.Web`:

```xml
<PackageReference Include="Blazilla" Version="2.*" />
```

Also extend the `<Description>` — it currently promises the assembly compiles into a client bundle and enumerates what it holds. Append: `Form validation is owned here: FormValidator attaches Blazilla's FluentValidation pass and stamps the marker OutcomeFormComponentBase requires before dispatch.`

- [ ] **Step 2: Write the failing tests**

Create `tests/Abstractions.Components.Tests/FormValidatorTests.cs`:

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Norse.Abstractions.Components.Tests;

public sealed class FormValidatorTests : BunitContext
{
	[Fact]
	void Rendering_inside_a_form_stamps_the_marker()
	{
		EditContext context = new(new object());

		Render<CascadingValue<EditContext>>(parameters => parameters
			.Add(p => p.Value, context)
			.Add(p => p.IsFixed, true)
			.Add(p => p.ChildContent, (RenderFragment)(builder =>
			{
				builder.OpenComponent<FormValidator>(0);
				builder.CloseComponent();
			})));

		context.Properties.TryGetValue(FormProperties.ValidatorAttached, out var attached).ShouldBeTrue();
		attached.ShouldBe(true);
	}

	[Fact]
	void AsyncMode_is_not_reachable_from_markup()
	{
		// AsyncMode=false against an async rule reports valid, then throws on a ThreadPool thread
		// out of Blazilla's async void handler. The trap is deleting the knob, not documenting it.
		var parameters = typeof(FormValidator)
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.GetCustomAttribute<ParameterAttribute>() is not null)
			.Select(property => property.Name);

		parameters.ShouldNotContain("AsyncMode");
	}

	[Fact]
	void Rendering_outside_a_form_is_rejected_loudly()
	{
		Should.Throw<InvalidOperationException>(() => Render<FormValidator>())
			.Message.ShouldContain("EditForm");
	}
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet run --project tests/Abstractions.Components.Tests`
Expected: FAIL — `FormValidator` and `FormProperties` do not exist (compile error).

- [ ] **Step 4: Create the marker constant**

Create `src/Abstractions.Components/FormProperties.cs`:

```csharp
namespace Norse.Abstractions.Components;

/// <summary>
///     Keys Norse stamps into <see cref="Microsoft.AspNetCore.Components.Forms.EditContext.Properties" />.
/// </summary>
static class FormProperties
{
	/// <summary>
	///     Set by <see cref="FormValidator" /> on initialization and required by
	///     <see cref="OutcomeFormComponentBase" /> before dispatch. Norse-owned because Blazilla exposes no
	///     durable "a validator is attached" signal — its own context key exists only mid-flight and is
	///     removed on completion, and a form with no validator validates to <c>true</c> with zero messages,
	///     indistinguishable from a genuinely valid one.
	/// </summary>
	internal const string ValidatorAttached = "__Norse_FormValidatorAttached";
}
```

- [ ] **Step 5: Create the component**

Create `src/Abstractions.Components/FormValidator.razor`:

```razor
@using Blazilla
<FluentValidator AsyncMode="true"/>

@code {
	[CascadingParameter]
	EditContext? CascadedEditContext { get; set; }

	/// <summary>
	///     Stamps the marker <see cref="OutcomeFormComponentBase" /> checks before dispatch. <c>AsyncMode</c>
	///     is fixed on and deliberately not a parameter: set false against an async rule, Blazilla reports the
	///     form valid and then throws from an <c>async void</c> handler on a ThreadPool thread.
	/// </summary>
	protected override void OnInitialized()
	{
		if (CascadedEditContext is null)
			throw new InvalidOperationException("<FormValidator/> must be placed inside an EditForm.");
		CascadedEditContext.Properties[FormProperties.ValidatorAttached] = true;
	}
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet run --project tests/Abstractions.Components.Tests`
Expected: PASS — 3 new tests green, all pre-existing tests still green (the guard does not land until Task 2).

- [ ] **Step 7: Commit**

```bash
git add src/Abstractions.Components tests/Abstractions.Components.Tests/FormValidatorTests.cs
git commit -m "Add FormValidator with the attachment marker"
```

---

### Task 2: The marker guard in SubmitAsync

**Files:**
- Modify: `src/Abstractions.Components/OutcomeFormComponentBase.cs`
- Test: `tests/Abstractions.Components.Tests/OutcomeFormComponentBaseTests.cs`

**Interfaces:**
- Consumes: `FormProperties.ValidatorAttached` from Task 1.
- Produces: `SubmitAsync` throwing `InvalidOperationException` when the marker is absent. Task 3 adds validation behind this guard.

- [ ] **Step 1: Write the failing test**

Add to `OutcomeFormComponentBaseTests.cs`:

```csharp
	[Fact]
	async Task A_form_with_no_validator_is_rejected_loudly()
	{
		// A validator-less form and a valid form both validate to true with zero messages, so this
		// must be caught before dispatch — after the fact the two are indistinguishable.
		using Harness harness = new();
		var context = harness.ContextFor(new object());
		var calls = 0;

		await Should.ThrowAsync<InvalidOperationException>(
			harness.Submit(context, _ =>
			{
				calls++;
				return Task.FromResult<Outcome<FakeResult>>(new Success<FakeResult>(new()));
			}, _ => { }));

		calls.ShouldBe(0);
	}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet run --project tests/Abstractions.Components.Tests`
Expected: FAIL — no exception thrown, `calls` is 1.

- [ ] **Step 3: Add the guard**

In `OutcomeFormComponentBase.SubmitAsync`, immediately after the existing foreign-context check and **before** the `IsSubmitting` check:

```csharp
		// Checked before dispatch, not inferred from the result: a form with no validator attached
		// validates to true with zero messages, identical to a genuinely valid one.
		if (!editContext.Properties.TryGetValue(FormProperties.ValidatorAttached, out var attached)
			|| attached is not true)
			throw new InvalidOperationException(
				"No <FormValidator/> in this form — validation would pass vacuously.");
```

- [ ] **Step 4: Update the existing Harness so the other five tests keep passing**

The five pre-existing tests build their `EditContext` through `ContextFor` without rendering a form, so nothing stamps the marker and all five now throw. Give the harness the same stamp `FormValidator` applies:

```csharp
		internal EditContext ContextFor(object request)
		{
			var context = EditContextFor(request);
			context.Properties[FormProperties.ValidatorAttached] = true;
			return context;
		}
```

Leave `A_form_with_no_validator_is_rejected_loudly` using a bare `EditContextFor` instead — add a second accessor so it can reach the unstamped form:

```csharp
		internal EditContext UnstampedContextFor(object request) =>
			EditContextFor(request);
```

Then switch that one test to `harness.UnstampedContextFor(new object())`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet run --project tests/Abstractions.Components.Tests`
Expected: PASS — all six `OutcomeFormComponentBase` tests plus Task 1's three.

- [ ] **Step 6: Commit**

```bash
git add src/Abstractions.Components/OutcomeFormComponentBase.cs tests/Abstractions.Components.Tests/OutcomeFormComponentBaseTests.cs
git commit -m "Reject a form with no FormValidator before dispatch"
```

---

### Task 3: Validation inside the submit guard

**Files:**
- Modify: `src/Abstractions.Components/OutcomeFormComponentBase.cs`
- Test: `tests/Abstractions.Components.Tests/FormValidationGateTests.cs`

**Interfaces:**
- Consumes: `FormProperties.ValidatorAttached` and `FormValidator` from Task 1; the marker guard from Task 2.
- Produces: `SubmitAsync` returning without dispatching when validation fails. Heimdall and Mimir rely on this to delete their own validate calls.

**Blocked on the judge's ruling in the Dispute section above.**

- [ ] **Step 1: Write the failing tests**

Create `tests/Abstractions.Components.Tests/FormValidationGateTests.cs`:

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Contracts;

namespace Norse.Abstractions.Components.Tests;

public sealed class FormValidationGateTests : BunitContext
{
	[Fact]
	async Task An_invalid_model_never_reaches_the_service()
	{
		var probe = Arrange(new Model(), TimeSpan.Zero);

		await probe.Instance.Submit();

		probe.Instance.Calls.ShouldBe(0);
		probe.Instance.Context.GetValidationMessages().ShouldNotBeEmpty();
	}

	[Fact]
	async Task A_valid_model_reaches_the_service_once()
	{
		var probe = Arrange(new Model { Password = "aaaaaaaa" }, TimeSpan.Zero);

		await probe.Instance.Submit();

		probe.Instance.Calls.ShouldBe(1);
	}

	[Fact]
	async Task A_second_submit_during_async_validation_is_rejected()
	{
		// Blazilla stashes the pending validation task in one EditContext.Properties slot, so
		// overlapping calls race it. IsSubmitting opens before validation, not before dispatch.
		var probe = Arrange(new Model { Password = "aaaaaaaa" }, TimeSpan.FromMilliseconds(300));

		var first = probe.Instance.Submit();
		var second = probe.Instance.Submit();
		await Task.WhenAll(first, second);

		probe.Instance.Calls.ShouldBe(1);
	}

	[Fact]
	async Task A_sync_only_validator_still_gates_under_the_fixed_async_mode()
	{
		// The claim the one-shape design rests on: Login and CountryLookup carry no async rules, and
		// must gate identically without their authors choosing anything.
		Services.AddScoped<IValidator<Model>>(_ => new SyncModelValidator());
		var probe = Render<Probe>(parameters => parameters.Add(p => p.Request, new Model()));

		await probe.Instance.Submit();

		probe.Instance.Calls.ShouldBe(0);
		probe.Instance.Context.GetValidationMessages().ShouldNotBeEmpty();
	}

	IRenderedComponent<Probe> Arrange(Model model, TimeSpan roundTrip)
	{
		Services.AddScoped<IValidator<Model>>(_ => new ModelValidator(roundTrip));
		return Render<Probe>(parameters => parameters.Add(p => p.Request, model));
	}

	sealed record Model
	{
		public string Password { get; init; } = "";
	}

	sealed class ModelValidator : AbstractValidator<Model>
	{
		public ModelValidator(TimeSpan roundTrip) =>
			RuleFor(model => model.Password)
				.MinimumLength(8)
				.MustAsync(async (_, cancellationToken) =>
				{
					await Task.Delay(roundTrip, cancellationToken);
					return true;
				});
	}

	sealed class SyncModelValidator : AbstractValidator<Model>
	{
		public SyncModelValidator() =>
			RuleFor(model => model.Password)
				.NotEmpty()
				.MinimumLength(8);
	}

	sealed class Probe : OutcomeFormComponentBase
	{
		[Parameter]
		public Model Request { get; set; } = new();

		internal int Calls { get; private set; }

		internal EditContext Context =>
			EditContextFor(Request);

		internal Task Submit() =>
			SubmitAsync(Context, _ =>
			{
				Calls++;
				return Task.FromResult<Outcome<Ack>>(new Success<Ack>(new()));
			}, _ => { });

		protected override void BuildRenderTree(RenderTreeBuilder builder)
		{
			builder.OpenComponent<EditForm>(0);
			builder.AddAttribute(1, nameof(EditForm.EditContext), Context);
			builder.AddAttribute(2, nameof(EditForm.ChildContent), (RenderFragment<EditContext>)(_ => child =>
			{
				child.OpenComponent<FormValidator>(0);
				child.CloseComponent();
			}));
			builder.CloseComponent();
		}

		internal sealed record Ack;
	}
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet run --project tests/Abstractions.Components.Tests`
Expected: FAIL — `An_invalid_model_never_reaches_the_service` reports `Calls` of 1 (nothing gates dispatch yet); the concurrency test reports 2.

- [ ] **Step 3: Add the validation call inside the guard**

Hoist `using Blazilla;` to the top of `OutcomeFormComponentBase.cs`. Inside the existing `#pragma warning disable CA2007` region, ahead of `var outcome = await call(CancellationToken);`:

```csharp
				// Static-invocation style is required, not preferred: .NET 11's instance
				// EditContext.ValidateAsync(CancellationToken) shadows Blazilla's extension, and
				// extension-style binding silently reports every form valid. See the plan's dispute.
				if (!await EditContextExtensions.ValidateAsync(editContext))
					return;
```

`IsSubmitting` is already set above this point, so the async round trip sits inside the guard — that placement is the fix, not incidental.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project tests/Abstractions.Components.Tests`
Expected: PASS — all three new tests plus Task 1's and Task 2's.

- [ ] **Step 5: Verify the whole realm is green with no warnings**

Run: `dotnet build`
Expected: `0 Warning(s)`, `0 Error(s)`. Warnings are ratcheted to errors on this platform; a warning here is a failure.

- [ ] **Step 6: Commit**

```bash
git add src/Abstractions.Components/OutcomeFormComponentBase.cs tests/Abstractions.Components.Tests/FormValidationGateTests.cs
git commit -m "Validate inside the submit guard so no form dispatches unvalidated"
```

---

## Out of Scope

- **`OnValidSubmit`.** `SubmitAsync` cannot see `EditForm`'s parameters, so a form wired that way skips async rules and never reaches any guard here. That is the Svartálfheim analyzer's job, in a later plan — the rule being `OnValidSubmit` is illegal when the `EditContext` came from `EditContextFor`, which excludes Himinbjörg's `Model=`-bound Identity pages by construction.
- **Heimdall and Mimir retrofits.** They ship after Asgard tags and publishes, and they close the effort — Register drops its manual validate and `AsyncMode`; Login and CountryLookup move `OnValidSubmit` → `OnSubmit`; all three swap `<FluentValidator/>` for `<FormValidator/>`. Bragi is rebuilt as the integration check, not changed: its stories drive Login and Register through `StoryDriver`, so Login newly waits through a validation round trip against `minimumSettleMs=500` / `settleTimeoutMs=5000`.
