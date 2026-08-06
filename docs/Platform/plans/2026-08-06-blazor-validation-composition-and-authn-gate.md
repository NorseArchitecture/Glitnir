# Blazor Validation Composition + AuthN Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task, paired with superpowers:test-driven-development on every coding task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the four-leg validation composition story (Heimdall #39) — coordinator-based server-rejection rendering, generated client/server discovery, submit-seam base component, `Result<T>`-hydrating setter, async email-exists — then the gate visual redesign on top of it.

**Architecture:** Cross-realm train in ship-gate order (Asgard → Midgard → Heimdall → Himinbjörg → Yggdrasil), with the independent Naglfar token task and the visual retrofit (Heimdall + Yggdrasil shell) at the tail. Specs: `../../Heimdall/specs/2026-08-06-blazor-validation-composition-design.md` (mechanism) and `../specs/2026-08-06-authn-gate-and-shell-visual-design.md` (visual).

**Tech Stack:** .NET 11 preview / C# 15, Blazor (Server + WASM), Blazilla + FluentValidation, FluentUI Blazor, protobuf-net.Grpc, Roslyn incremental generators, xUnit v3 on MTP v2 + Shouldly + NSubstitute + bUnit, Style Dictionary (Naglfar).

## Global Constraints

- **No automatic git commits — ever.** Where this plan's steps say "stage," run `git add <files>` in the realm submodule and stop; the human commits. This deliberately overrides the plan-template's commit steps, per every realm CLAUDE.md.
- **Ship gates are human acts.** Steps marked `HUMAN GATE` (PR, CI, tag, NuGet/npm publish) block the next realm's tasks; halt and wait.
- Tabs for indentation; US English; `sealed` by default (every class `sealed`, `abstract`, or `static`); omit default accessibility modifiers; smallest API footprint; `InternalsVisibleTo` already grants each `*.Tests` project internals access — never escalate to `public` for tests.
- Target-typed `new()` for construction; `var` for return assignments; collection expressions (`[]`, `[.. x]`); expression-bodied members with arrow on the declaration line, body indented on the next; pattern matching `is null`/`is not null`; no string concatenation with `+`.
- Every async method: `CancellationToken cancellationToken = default` last parameter, propagated. `ConfigureAwait(false)` in library/src code — **except Blazor component code**, where the renderer's sync context is load-bearing (matches every existing Heimdall component).
- XML docs on every publicly visible member in src projects; none required in tests/generators.
- Test classes `public sealed`; test methods bare `void`/`async Task` (no accessibility modifier); sentence-shaped underscore names (`Applies_field_errors_to_the_second_store`). Shouldly assertions; NSubstitute mocks; usings for those are global — do not re-add per file.
- Generators: emit via `sb.AppendCSharp($$"""…""")` raw string literals only (never `AppendLine`), fully-qualified `global::` names in emitted code, `Utf8NoBom.Encoding` on write, `#pragma warning disable CS1591` in the generated header.
- Package references: leverage transitive flow; tag to major (`Version="3.*"`); framework-tracking `11.*-*`. Yggdrasil is CPM and pins explicitly.
- New wire records: `[DataContract]` `sealed record`, `[DataMember(Order = n)]`, no mediator markers, no `[Authorize]` (purity locks assert this).
- Copy is en-US, sentence case, active voice; exact strings come from the specs.

---

## Task Overview

| # | Realm | Deliverable |
|---|---|---|
| 1 | Asgard | `Problem.ModelError` factory |
| 2 | Asgard | `TryAddEnumerable` validator registration in `RegistrationEmitter` |
| G1 | — | HUMAN GATE: Asgard PR → CI → tag → publish |
| 3 | Midgard | `ComponentDiscovery` in `Generator.Shared` |
| 4 | Midgard | Client generator head — `AddNorseClientComponents()` |
| 5 | Midgard | Server generator head — routes singleton + `AddNorseComponentAssemblies()` |
| G2 | — | HUMAN GATE: Midgard PR → CI → tag → publish |
| 6 | Heimdall | `ApplyServerErrors`/`ClearServerErrors` + `ServerErrorCoordinator` + `CategoryDisplay` |
| 7 | Heimdall | `<ModelValidationSummary />` |
| 8 | Heimdall | `OutcomeFormComponentBase.SubmitAsync` |
| 9 | Heimdall | `EmailExistsRequest` + `IAuthenticationService.EmailExists` + purity locks |
| 10 | Heimdall | Blur-gated async email-exists rule in `RegisterRequestValidator` (+ `CustomAsync` test-level proof) |
| 11 | Heimdall | `Result<EmailAddress>`-hydrating setter on `RegisterRequest` |
| 12 | Heimdall | `LoginResult` reshape + Login/Register mechanism retrofit |
| G3 | — | HUMAN GATE: Heimdall PR → CI → tag → publish |
| 13 | Himinbjörg | `LoginHandler` reshape + `EmailExistsCommand`/handler + nested-send integration test |
| G4 | — | HUMAN GATE: Himinbjörg PR → CI → tag → publish |
| 14 | Yggdrasil | Hosts adopt generated discovery (all four hand lists die) + Playwright smoke |
| 15 | Naglfar | Violet ramp + `--norse-bifrost-seam` tokens (independent — may run any time) |
| 16 | Heimdall | Gate visual retrofit (layout, panel, seam, form styles) |
| 17 | Yggdrasil | Shell tidy (Template nav group, footer line) |

Every Heimdall/Himinbjörg/Yggdrasil task lands on that realm's existing in-flight feature branch if one is open — one fork per realm, never branch-on-branch (Bifröst CLAUDE.md §7).

---

### Task 1: `Problem.ModelError` factory (Asgard)

**Files:**
- Modify: `Asgard/src/Abstractions.Contracts/Problem.cs`
- Test: `Asgard/tests/Abstractions.Contracts.Tests/ProblemTests.cs` (create if absent)

**Interfaces:**
- Consumes: existing `Problem` record (`Category` required, `Errors` dictionary), `ErrorCategory`.
- Produces: `public static Problem ModelError(ErrorCategory category, string message)` — used by Tasks 6, 12, 13.

- [ ] **Step 1: Confirm no equivalent exists** — grep `Abstractions.Contracts` for existing factory members on `Problem` (`grep -n "static Problem" Asgard/src/Abstractions.Contracts/Problem.cs`). Expected: none (verified at planning). If one exists, adopt it and skip this task, updating later tasks' call sites.
- [ ] **Step 2: Write the failing test**

```csharp
public sealed class ProblemTests
{
	[Fact]
	void Model_error_carries_the_single_message_under_the_empty_key()
	{
		var problem = Problem.ModelError(ErrorCategory.InvalidCredentials, "Invalid email or password.");

		problem.Category.ShouldBe(ErrorCategory.InvalidCredentials);
		problem.Errors.ShouldHaveSingleItem();
		problem.Errors[string.Empty].ShouldBe(["Invalid email or password."]);
	}
}
```

- [ ] **Step 3: Run to verify failure** — `dotnet test Asgard/tests/Abstractions.Contracts.Tests` → FAIL: `Problem` contains no definition for `ModelError`.
- [ ] **Step 4: Implement**

```csharp
	/// <summary>
	/// Creates a <see cref="Problem"/> carrying one model-level message — the empty-string key both
	/// Blazor and FluentValidation reserve for errors not tied to any field — so call sites never
	/// hand-build the single-entry dictionary literal.
	/// </summary>
	/// <param name="category">The error category the message belongs to.</param>
	/// <param name="message">The model-level message.</param>
	public static Problem ModelError(ErrorCategory category, string message) =>
		new()
		{
			Category = category,
			Errors = new Dictionary<string, string[]> { [string.Empty] = [message] },
		};
```

- [ ] **Step 5: Run to verify pass**, then run the full `Abstractions.Contracts.Tests` suite for regressions.
- [ ] **Step 6: Stage** — `git -C Asgard add src/Abstractions.Contracts/Problem.cs tests/Abstractions.Contracts.Tests/ProblemTests.cs`. Stop; no commit.

---

### Task 2: `TryAddEnumerable` validator registration (Asgard)

**Files:**
- Modify: `Asgard/gen/Abstractions.Web.Server.Generator/RegistrationEmitter.cs` (validator emission lines, currently `AddScoped`)
- Test: the existing generator snapshot test project under `Asgard/tests/` (locate by `$(AssemblyName).Tests` parity with the generator; update affected snapshots)

**Interfaces:**
- Produces: emitted registrations of shape `TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IValidator<TRequest>), typeof(TValidator)))` — the idempotency half that pairs with Task 4's client-side emission so a validator registered by both generators resolves exactly once.

- [ ] **Step 1: Update the snapshot expectation first (the failing test)** — in the generator test asserting validator registration output, change the expected emission from the `AddScoped` line to:

```csharp
		global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(
			services, global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped(
				typeof(global::FluentValidation.IValidator<{RequestTypeName}>), typeof({ValidatorTypeName})));
```

  Apply the same shape to the wire-validator and `CommandRequestValidator` adapter registrations in the same emitter (three sites total, per `RegistrationEmitter.cs:42-50`).
- [ ] **Step 2: Run to verify failure** — snapshot mismatch on the old `AddScoped` output.
- [ ] **Step 3: Update `RegistrationEmitter`** — replace the three `AddScoped<IValidator<...>, ...>` emission templates with the `TryAddEnumerable` shape above. Handler registrations (`IRequestHandler`) are untouched.
- [ ] **Step 4: Run generator tests to green**, then build a downstream consumer (`dotnet build Himinbjorg/src/Identity.Web.Server`) to confirm the regenerated registration compiles.
- [ ] **Step 5: Add the resolution lock test** in the same test project: build a `ServiceCollection`, invoke the generated registration twice, assert `provider.GetServices<IValidator<TRequest>>().Count() == 1` for a request with one validator (use the test project's existing compilation-harness fixtures).

```csharp
	[Fact]
	void Registering_twice_resolves_each_validator_exactly_once()
	{
		ServiceCollection services = new();
		InvokeGeneratedRegistration(services);
		InvokeGeneratedRegistration(services);

		using var provider = services.BuildServiceProvider();
		provider.GetServices<IValidator<FakeRequest>>().ShouldHaveSingleItem();
	}
```

  (`InvokeGeneratedRegistration`/`FakeRequest` ride the harness the project already uses for emission tests — reuse its fixture, do not build a new one.)
- [ ] **Step 6: Add the adapter async-rule test** — in the existing `CommandRequestValidator` test file (`Abstractions.Web.Server.Tests`), prove the adapter executes async rules declared on the wrapped wire type through its plain `ValidateAsync` (the guarantee Task 10's default-set rule rides on — "single source of validation truth, run twice" is only true because of this):

```csharp
	[Fact]
	async Task The_adapter_runs_async_rules_declared_on_the_wire_type()
	{
		var called = false;
		InlineValidator<FakeRequest> wireValidator = new();
		wireValidator.RuleFor(r => r.Value).CustomAsync(async (_, _, _) =>
		{
			called = true;
			await Task.Yield();
		});
		CommandRequestValidator<FakeCommand, FakeRequest, FakeResponse> adapter = new([wireValidator]);

		await adapter.ValidateAsync(new ValidationContext<FakeCommand>(NewFakeCommand()), CancellationToken.None);

		called.ShouldBeTrue();
	}
```

  (Reuse the file's existing fake command/request/response fixtures and construction helper; `InlineValidator` is FluentValidation's built-in test validator.)
- [ ] **Step 7: Run to green; stage** the emitter + snapshots + both new tests. Stop.

---

### HUMAN GATE G1 — Asgard ships

- [ ] PR the Asgard branch; CI green; merge; tag; publish to NuGet. Downstream tasks consume the new package version (floating `*` references pick it up on restore).

---

### Task 3: `ComponentDiscovery` (Midgard, Generator.Shared)

**Files:**
- Create: `Midgard/gen/Infrastructure.Web.Grpc.Generator.Shared/ComponentDiscovery.cs`
- Test: Midgard's existing generator test project for the shared library (parity-named; add `ComponentDiscoveryTests.cs`)

**Interfaces:**
- Consumes: `Compilation` (Roslyn).
- Produces (both used by Tasks 4 and 5):

```csharp
internal static class ComponentDiscovery
{
	internal static ComponentDiscoveryResult Discover(Compilation compilation);
}

internal sealed record ComponentDiscoveryResult(
	ImmutableArray<ValidatorModel> Validators,          // ordered by ValidatorTypeName, ordinal
	ImmutableArray<string> RoutableAssemblyMarkers,     // one global::-qualified routable type per assembly, ordered
	string? RoutesHolderMarker,                          // marker type in the assembly declaring Norse.Hosting.Web.Components.Routes, null if unreferenced
	bool RoutesAdditionalAssembliesTypeExists);          // Norse.Hosting.Web.Components.RoutesAdditionalAssemblies resolvable

internal sealed record ValidatorModel(string ValidatorTypeName, string RequestTypeName); // both global::-qualified
```

- [ ] **Step 1: Write the failing tests** — using the shared test harness's compilation builder (same fixture `ContractDiscovery`'s tests use):

```csharp
	[Fact]
	void Discovers_concrete_validators_in_own_and_referenced_assemblies()
	{
		var compilation = HarnessCompilation(sources: [ValidatorSource], references: [ReferencedValidatorAssembly]);

		var result = ComponentDiscovery.Discover(compilation);

		result.Validators.Select(v => v.ValidatorTypeName)
			.ShouldBe(["global::Referenced.OtherValidator", "global::Own.FakeValidator"]);
	}

	[Fact]
	void Records_one_routable_marker_per_assembly_and_skips_assemblies_without_routes()
	{
		var compilation = HarnessCompilation(references: [RoutableAssembly, PlainAssembly]);

		var result = ComponentDiscovery.Discover(compilation);

		result.RoutableAssemblyMarkers.ShouldHaveSingleItem();
	}

	[Fact]
	void Identifies_the_routes_holder_assembly_separately()
	{
		var compilation = HarnessCompilation(references: [RoutesHolderAssembly]);

		var result = ComponentDiscovery.Discover(compilation);

		result.RoutesHolderMarker.ShouldNotBeNull();
		result.RoutableAssemblyMarkers.ShouldNotContain(result.RoutesHolderMarker);
	}
```

- [ ] **Step 2: Run to verify failure** (type does not exist).
- [ ] **Step 3: Implement** — mirror `ContractDiscovery`'s walk: `compilation.Assembly` plus `compilation.References` resolved via `GetAssemblyOrModuleSymbol`, visiting each assembly's global namespace. Rules:
	- A validator is a non-abstract named type implementing `FluentValidation.IValidator<T>` (resolve via `compilation.GetTypeByMetadataName("FluentValidation.IValidator\`1")`; skip everything if unresolvable). Record validator and `T` display strings (`global::` format), sorted ordinal.
	- A routable assembly is one containing at least one type bearing `Microsoft.AspNetCore.Components.RouteAttribute`; record the first such type (ordinal ordering) as the marker.
	- The assembly declaring `Norse.Hosting.Web.Components.Routes` (metadata lookup) is reported via `RoutesHolderMarker` and **excluded** from `RoutableAssemblyMarkers` — the Router's `AppAssembly` already covers it, and Blazor throws on duplicate route discovery if it appears in `AdditionalAssemblies` too.
	- `RoutesAdditionalAssembliesTypeExists` = `GetTypeByMetadataName("Norse.Hosting.Web.Components.RoutesAdditionalAssemblies") is not null` — Tasks 4/5 emit route registrations only when true, so non-Yggdrasil consumers get validators-only output.
- [ ] **Step 4: Run to green.**
- [ ] **Step 5: Stage.** Stop.

---

### Task 4: Client generator head (Midgard)

**Files:**
- Create: `Midgard/gen/Infrastructure.Web.Client.Generator/ClientComponentRegistrationGenerator.cs`
- Create: `Midgard/gen/Infrastructure.Web.Client.Generator/ClientComponentRegistrationEmitter.cs`
- Test: the existing `Infrastructure.Web.Client.Generator` test project — `ClientComponentRegistrationEmitterTests.cs`

**Interfaces:**
- Consumes: `ComponentDiscovery.Discover` (Task 3).
- Produces: generated `AddNorseClientComponents(this IServiceCollection services)` in the consuming compilation's root namespace — Task 14 calls it from `Hosting.Web.Client/Program.cs`.

- [ ] **Step 1: Write the failing snapshot test** — feed the harness a compilation containing one validator and one routable referenced assembly plus the `RoutesAdditionalAssemblies` type; assert the emitted source contains, in order:

```csharp
	// validator (idempotent, pairs with the server-side generator's identical shape)
	global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(
		services, global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped(
			typeof(global::FluentValidation.IValidator<global::Fake.LoginRequest>), typeof(global::Fake.LoginRequestValidator)));
	// router discovery
	global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(
		services, new global::Norse.Hosting.Web.Components.RoutesAdditionalAssemblies([
			typeof(global::Fake.RoutablePage).Assembly,
		]));
```

  and a second case: compilation **without** the `RoutesAdditionalAssemblies` type → emitted source contains the validator block and no route registration.
- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement** — generator mirrors `GrpcClientRegistrationGenerator`'s shape (`CompilationProvider.Select` → `RegisterSourceOutput`, `Utf8NoBom.Encoding`, emit nothing when discovery is empty). Emitter is one `AppendCSharp($$"""…""")` template producing:

```csharp
// <auto-generated/>
namespace {{rootNamespace}};

#pragma warning disable CS1591 // Generated registration: no XML doc comments.
/// <summary>Generated by Norse.Infrastructure.Web.Client.Generator — compile-time validator and routable-assembly registration.</summary>
public static class NorseClientComponentRegistration
{
	/// <summary>Registers every discovered FluentValidation validator (idempotently) and the discovered routable component assemblies.</summary>
	public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddNorseClientComponents(
		this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
	{
{{ValidatorRegistrations(result)}}
{{RouteRegistration(result)}}
		return services;
	}
}
#pragma warning restore CS1591
```

  with `ValidatorRegistrations`/`RouteRegistration` as interpolated helper methods (no `AppendLine`, no `+` concatenation — `string.Join("\n", …)` over interpolated strings).
- [ ] **Step 4: Run to green.**
- [ ] **Step 5: Stage.** Stop.

---

### Task 5: Server generator head (Midgard)

**Files:**
- Create: `Midgard/gen/Infrastructure.Web.Server.Generator/ServerComponentRegistrationGenerator.cs`
- Create: `Midgard/gen/Infrastructure.Web.Server.Generator/ServerComponentRegistrationEmitter.cs`
- Test: the existing `Infrastructure.Web.Server.Generator` test project — `ServerComponentRegistrationEmitterTests.cs`

**Interfaces:**
- Consumes: `ComponentDiscovery.Discover` (Task 3).
- Produces: generated `AddNorseClientComponents(this IServiceCollection)` (same name/shape as Task 4 — the host calls one or the other, never both packages) **plus** `AddNorseComponentAssemblies(this RazorComponentsEndpointConventionBuilder)` — Task 14 calls both from `Hosting.Web.Server/Program.cs`.

- [ ] **Step 1: Write the failing snapshot test** — same harness; assert the server emission additionally contains:

```csharp
	/// <summary>Feeds every discovered routable component assembly to Razor endpoint discovery — the render-mode half of discovery, distinct from the Router's.</summary>
	public static global::Microsoft.AspNetCore.Builder.RazorComponentsEndpointConventionBuilder AddNorseComponentAssemblies(
		this global::Microsoft.AspNetCore.Builder.RazorComponentsEndpointConventionBuilder builder) =>
		global::Microsoft.AspNetCore.Builder.RazorComponentsEndpointConventionBuilderExtensions.AddAdditionalAssemblies(builder,
			typeof(global::Norse.Hosting.Web.Components.Routes).Assembly,
			typeof(global::Fake.RoutablePage).Assembly);
```

  Assertions encode the two exclusion rules: the **endpoint** list includes the `Routes`-holder assembly and every routable assembly but never the compilation's own assembly (the host's `App` assembly is `MapRazorComponents<App>`'s implicit root); the **router** singleton excludes the `Routes`-holder assembly but may include the compilation's own assembly when it carries routable pages.
- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement** — same generator/emitter pattern as Task 4; the emitter template adds the endpoint extension member and applies the exclusion rules above from `ComponentDiscoveryResult`. Verify the exact extension-holder type name for `AddAdditionalAssemblies` against the referenced ASP.NET Core assemblies in the harness (it must compile in the snapshot compile-check, which is the test).
- [ ] **Step 4: Run to green.**
- [ ] **Step 5: Stage.** Stop.

---

### HUMAN GATE G2 — Midgard ships

- [ ] PR → CI → merge → tag → publish `Norse.Infrastructure.Web.Client` / `.Web.Server` (generators pack inside).

---

### Task 6: `ApplyServerErrors` + coordinator + category display (Heimdall)

**Files:**
- Create: `Heimdall/src/AuthN.Components/ServerValidation/EditContextServerErrorsExtensions.cs`
- Create: `Heimdall/src/AuthN.Components/ServerValidation/ServerErrorCoordinator.cs`
- Create: `Heimdall/src/AuthN.Components/ServerValidation/CategoryDisplay.cs`
- Test: `Heimdall/tests/AuthN.Components.Tests/ServerValidation/EditContextServerErrorsTests.cs`

**Interfaces:**
- Consumes: `Problem`/`ErrorCategory` (Asgard), `EditContext`/`ValidationMessageStore`/`FieldIdentifier` (Blazor).
- Produces: `ApplyServerErrors(this EditContext, Problem)`, `ClearServerErrors(this EditContext)` — used by Tasks 8 and 12. `CategoryDisplay.For(Problem)` stays `internal`.

- [ ] **Step 1: Write the failing tests** (plain xUnit, no bUnit — `EditContext` is a POCO):

```csharp
public sealed class EditContextServerErrorsTests
{
	sealed record FakeModel
	{
		public string Email { get; set; } = "";
	}

	static (EditContext Context, FakeModel Model) NewContext()
	{
		FakeModel model = new();
		return (new(model), model);
	}

	[Fact]
	void Applies_field_errors_against_the_named_field()
	{
		var (context, model) = NewContext();

		context.ApplyServerErrors(new()
		{
			Category = ErrorCategory.Validation,
			Errors = new Dictionary<string, string[]> { [nameof(FakeModel.Email)] = ["Taken."] },
		});

		context.GetValidationMessages(new FieldIdentifier(model, nameof(FakeModel.Email))).ShouldBe(["Taken."]);
	}

	[Fact]
	void Applies_empty_key_errors_at_model_level()
	{
		var (context, model) = NewContext();

		context.ApplyServerErrors(Problem.ModelError(ErrorCategory.InvalidCredentials, "Invalid email or password."));

		context.GetValidationMessages(new FieldIdentifier(model, string.Empty)).ShouldBe(["Invalid email or password."]);
	}

	[Fact]
	void Renders_category_display_when_the_dictionary_is_empty()
	{
		var (context, model) = NewContext();

		context.ApplyServerErrors(new() { Category = ErrorCategory.Forbidden });

		context.GetValidationMessages(new FieldIdentifier(model, string.Empty)).ShouldHaveSingleItem();
	}

	[Fact]
	void Fault_display_carries_the_correlation_id()
	{
		var (context, model) = NewContext();
		Guid correlationId = new("11111111-2222-3333-4444-555555555555");

		context.ApplyServerErrors(new() { Category = ErrorCategory.Fault, CorrelationId = correlationId });

		context.GetValidationMessages(new FieldIdentifier(model, string.Empty))
			.ShouldHaveSingleItem()
			.ShouldContain(correlationId.ToString());
	}

	[Fact]
	void Editing_a_field_clears_only_that_fields_server_messages()
	{
		var (context, model) = NewContext();
		context.ApplyServerErrors(new()
		{
			Category = ErrorCategory.Validation,
			Errors = new Dictionary<string, string[]>
			{
				[nameof(FakeModel.Email)] = ["Taken."],
				[string.Empty] = ["Also broken."],
			},
		});

		context.NotifyFieldChanged(new FieldIdentifier(model, nameof(FakeModel.Email)));

		context.GetValidationMessages(new FieldIdentifier(model, nameof(FakeModel.Email))).ShouldBeEmpty();
		context.GetValidationMessages(new FieldIdentifier(model, string.Empty)).ShouldBe(["Also broken."]);
	}

	[Fact]
	void A_fresh_validation_pass_clears_all_server_messages()
	{
		var (context, _) = NewContext();
		context.ApplyServerErrors(Problem.ModelError(ErrorCategory.InvalidCredentials, "Invalid email or password."));

		context.Validate().ShouldBeTrue(); // raises OnValidationRequested → coordinator clears → no store blocks validity
	}

	[Fact]
	void The_validation_request_clear_raises_its_own_state_change_notification()
	{
		var (context, _) = NewContext();
		context.ApplyServerErrors(Problem.ModelError(ErrorCategory.InvalidCredentials, "Invalid email or password."));
		var notified = false;
		context.OnValidationStateChanged += (_, _) => notified = true;

		context.Validate();

		notified.ShouldBeTrue(); // no other validator exists in this test — the coordinator itself must notify
	}

	[Fact]
	void Reapply_replaces_rather_than_accumulates()
	{
		var (context, model) = NewContext();
		context.ApplyServerErrors(Problem.ModelError(ErrorCategory.InvalidCredentials, "First."));

		context.ApplyServerErrors(Problem.ModelError(ErrorCategory.InvalidCredentials, "Second."));

		context.GetValidationMessages(new FieldIdentifier(model, string.Empty)).ShouldBe(["Second."]);
	}

	[Fact]
	void Clear_removes_every_server_message()
	{
		var (context, model) = NewContext();
		context.ApplyServerErrors(Problem.ModelError(ErrorCategory.InvalidCredentials, "Invalid email or password."));

		context.ClearServerErrors();

		context.GetValidationMessages(new FieldIdentifier(model, string.Empty)).ShouldBeEmpty();
	}

	[Fact]
	void The_coordinator_is_created_once_and_cached()
	{
		var (context, _) = NewContext();

		context.ApplyServerErrors(Problem.ModelError(ErrorCategory.InvalidCredentials, "First."));
		context.ApplyServerErrors(Problem.ModelError(ErrorCategory.InvalidCredentials, "Second."));

		context.Properties.TryGetValue(EditContextServerErrorsExtensions.CoordinatorKey, out var coordinator).ShouldBeTrue();
		coordinator.ShouldBeOfType<ServerErrorCoordinator>();
	}
}
```

- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement.** `CategoryDisplay` (internal static): a `switch` expression over `ErrorCategory` returning spec §3.4's constants — safe generic sentences per category, `Fault`/`Unspecified`/`Erased` producing the generic sentence plus `Reference: {correlationId}` when present (string interpolation, never `+`). `ServerErrorCoordinator`:

```csharp
using Microsoft.AspNetCore.Components.Forms;
using Norse.Abstractions.Contracts;

namespace Norse.AuthN.Components;

/// <summary>
/// Owns the server-produced <see cref="ValidationMessageStore"/> for one <see cref="EditContext"/>,
/// plus the two subscriptions that make resubmission possible: field edits clear that field's server
/// messages, and any fresh validation pass clears them all. Without the second subscription a stale
/// server message keeps <see cref="EditContext.Validate"/> false forever and the valid-submit
/// handler can never run again — the live defect the hand-rolled components carried.
/// </summary>
sealed class ServerErrorCoordinator
{
	readonly EditContext _editContext;
	readonly ValidationMessageStore _messages;

	internal ServerErrorCoordinator(EditContext editContext)
	{
		_editContext = editContext;
		_messages = new(editContext);
		editContext.OnFieldChanged += (_, e) =>
		{
			_messages.Clear(e.FieldIdentifier);
			editContext.NotifyValidationStateChanged();
		};
		editContext.OnValidationRequested += (_, _) =>
		{
			// Notify after clearing: without it, UI cleanup would silently depend on some OTHER
			// store (Blazilla's) raising the notification afterward — a correctness dependency on
			// another library's implementation. A form with no other validator must still update.
			_messages.Clear();
			editContext.NotifyValidationStateChanged();
		};
	}

	internal void Apply(Problem problem)
	{
		_messages.Clear();
		if (problem.Errors.Count == 0)
			_messages.Add(new FieldIdentifier(_editContext.Model, string.Empty), CategoryDisplay.For(problem));
		else
			foreach (var (field, messages) in problem.Errors)
			{
				FieldIdentifier identifier = new(_editContext.Model, field);
				foreach (var message in messages)
					_messages.Add(identifier, message);
			}

		_editContext.NotifyValidationStateChanged();
	}

	internal void Clear()
	{
		_messages.Clear();
		_editContext.NotifyValidationStateChanged();
	}
}
```

  `EditContextServerErrorsExtensions` (public static): `ApplyServerErrors`/`ClearServerErrors` with `ArgumentNullException.ThrowIfNull` guards, coordinator cached in `EditContext.Properties` under `internal static readonly object CoordinatorKey = new();` (internal so the caching test can see it). Prefer a C# 14 extension block per house rules if the compiler accepts it against `EditContext`; fall back to classic static-class extensions if not, noting which landed.
- [ ] **Step 4: Run to green** (all nine tests).
- [ ] **Step 5: Stage.** Stop.

---

### Task 7: `<ModelValidationSummary />` (Heimdall)

**Files:**
- Create: `Heimdall/src/AuthN.Components.FluentUI/ModelValidationSummary.razor`
- Test: `Heimdall/tests/AuthN.Components.FluentUI.Tests/ModelValidationSummaryTests.cs` (bUnit — already referenced by this test project)

**Interfaces:**
- Consumes: cascaded `EditContext`; renders empty-key messages only.
- Produces: the component tag Tasks 12 and 16 place inside each `EditForm`.

- [ ] **Step 1: Write the failing bUnit tests**

```csharp
public sealed class ModelValidationSummaryTests : TestContext
{
	sealed record FakeModel;

	IRenderedComponent<ModelValidationSummary> Render(EditContext editContext) =>
		RenderComponent<ModelValidationSummary>(parameters =>
			parameters.AddCascadingValue(editContext));

	[Fact]
	void Renders_nothing_when_no_model_level_messages_exist()
	{
		EditContext context = new(new FakeModel());

		var component = Render(context);

		component.Markup.ShouldBeEmpty();
	}

	[Fact]
	void Renders_model_level_messages_and_ignores_field_messages()
	{
		FakeModel model = new();
		EditContext context = new(model);
		ValidationMessageStore store = new(context);
		store.Add(new FieldIdentifier(model, "Email"), "Field-scoped.");
		store.Add(new FieldIdentifier(model, string.Empty), "Model-scoped.");

		var component = Render(context);
		context.NotifyValidationStateChanged();

		component.Markup.ShouldContain("Model-scoped.");
		component.Markup.ShouldNotContain("Field-scoped.");
	}

	[Fact]
	void Rerenders_when_validation_state_changes()
	{
		FakeModel model = new();
		EditContext context = new(model);
		var component = Render(context);
		ValidationMessageStore store = new(context);

		store.Add(new FieldIdentifier(model, string.Empty), "Appeared.");
		context.NotifyValidationStateChanged();

		component.Markup.ShouldContain("Appeared.");
	}

	[Fact]
	void A_rendered_model_message_disappears_on_a_fresh_validation_request_with_no_other_validator_present()
	{
		FakeModel model = new();
		EditContext context = new(model);
		var component = Render(context);
		context.ApplyServerErrors(Problem.ModelError(ErrorCategory.InvalidCredentials, "Invalid email or password."));
		component.Markup.ShouldContain("Invalid email or password.");

		context.Validate(); // no Blazilla in this form — the coordinator's own notification must drive the re-render

		component.Markup.ShouldNotContain("Invalid email or password.");
	}

	[Fact]
	void Unsubscribes_on_dispose()
	{
		FakeModel model = new();
		EditContext context = new(model);
		var component = Render(context);

		component.Instance.Dispose();
		ValidationMessageStore store = new(context);
		store.Add(new FieldIdentifier(model, string.Empty), "After dispose.");
		context.NotifyValidationStateChanged();

		component.Markup.ShouldNotContain("After dispose.");
	}
}
```

- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement**

```razor
@implements IDisposable

@if (_messages.Count > 0)
{
	<FluentMessageBar Intent="MessageIntent.Error" AllowDismiss="false" Class="norse-model-errors">
		@foreach (var message in _messages)
		{
			<div>@message</div>
		}
	</FluentMessageBar>
}

@code {
	IReadOnlyList<string> _messages = [];

	[CascadingParameter]
	EditContext EditContext { get; set; } = null!;

	/// <inheritdoc />
	protected override void OnInitialized()
	{
		if (EditContext is null)
			throw new InvalidOperationException($"{nameof(ModelValidationSummary)} requires a cascading EditContext — place it inside an EditForm.");
		EditContext.OnValidationStateChanged += HandleValidationStateChanged;
		CollectMessages();
	}

	void HandleValidationStateChanged(object? sender, ValidationStateChangedEventArgs e)
	{
		CollectMessages();
		StateHasChanged();
	}

	void CollectMessages() =>
		_messages = [.. EditContext.GetValidationMessages(new FieldIdentifier(EditContext.Model, string.Empty))];

	/// <inheritdoc />
	public void Dispose() =>
		EditContext.OnValidationStateChanged -= HandleValidationStateChanged;
}
```

  Verify `FluentMessageBar`'s `Intent` parameter/enum name against the FluentUI Blazor version this project references (v5-family API — check `~/.nuget/packages/microsoft.fluentui.aspnetcore.components/<version>` if the build disagrees); the failing compile is the verification.
- [ ] **Step 4: Run to green.**
- [ ] **Step 5: Stage.** Stop.

---

### Task 8: `OutcomeFormComponentBase` (Heimdall)

**Files:**
- Create: `Heimdall/src/AuthN.Components/OutcomeFormComponentBase.cs`
- Test: `Heimdall/tests/AuthN.Components.Tests/OutcomeFormComponentBaseTests.cs`

**Interfaces:**
- Consumes: `AsyncComponentBase` (Asgard `Abstractions.Components` — exposes the component `CancellationToken`), `Outcome<T>`/`Success<T>`/`Failed`, Task 6's extensions.
- Produces (Task 12/16 pages inherit this):

```csharp
protected bool IsSubmitting { get; }
protected Task SubmitAsync<T>(EditContext editContext, Func<CancellationToken, Task<Outcome<T>>> call, Action<T> onSuccess) where T : notnull;
protected Task SubmitAsync<T>(EditContext editContext, Func<CancellationToken, Task<Outcome<T>>> call, Func<T, Task> onSuccess) where T : notnull;
```

- [ ] **Step 1: Write the failing tests** (bUnit-free — drive the protected members through a tiny test subclass):

```csharp
public sealed class OutcomeFormComponentBaseTests
{
	sealed record FakeResult;

	sealed class Harness : OutcomeFormComponentBase
	{
		internal Task Submit<T>(EditContext editContext, Func<CancellationToken, Task<Outcome<T>>> call, Action<T> onSuccess) where T : notnull =>
			SubmitAsync(editContext, call, onSuccess);

		internal bool Submitting =>
			IsSubmitting;
	}

	[Fact]
	async Task Success_invokes_the_continuation_and_clears_server_errors()
	{
		Harness harness = new();
		EditContext context = new(new object());
		context.ApplyServerErrors(Problem.ModelError(ErrorCategory.InvalidCredentials, "Stale."));
		var invoked = false;

		await harness.Submit(context, _ => Task.FromResult<Outcome<FakeResult>>(new Success<FakeResult>(new())), _ => invoked = true);

		invoked.ShouldBeTrue();
		context.GetValidationMessages().ShouldBeEmpty();
	}

	[Fact]
	async Task Failure_applies_the_problem_and_skips_the_continuation()
	{
		Harness harness = new();
		var model = new object();
		EditContext context = new(model);
		var invoked = false;

		await harness.Submit(context,
			_ => Task.FromResult<Outcome<FakeResult>>(new Failed(Problem.ModelError(ErrorCategory.LockedOut, "Locked."))),
			_ => invoked = true);

		invoked.ShouldBeFalse();
		context.GetValidationMessages(new FieldIdentifier(model, string.Empty)).ShouldBe(["Locked."]);
	}

	[Fact]
	async Task An_overlapping_call_returns_without_dispatching()
	{
		Harness harness = new();
		EditContext context = new(new object());
		TaskCompletionSource<Outcome<FakeResult>> pending = new();
		var calls = 0;

		var first = harness.Submit(context, _ => { calls++; return pending.Task; }, _ => { });
		await harness.Submit(context, _ => { calls++; return pending.Task; }, _ => { });

		calls.ShouldBe(1);
		harness.Submitting.ShouldBeTrue();
		pending.SetResult(new Success<FakeResult>(new()));
		await first;
		harness.Submitting.ShouldBeFalse();
	}

	[Fact]
	async Task A_throwing_continuation_propagates_and_releases_the_guard()
	{
		Harness harness = new();
		EditContext context = new(new object());

		await Should.ThrowAsync<InvalidOperationException>(
			harness.Submit(context, _ => Task.FromResult<Outcome<FakeResult>>(new Success<FakeResult>(new())),
				_ => throw new InvalidOperationException("continuation bug")));

		harness.Submitting.ShouldBeFalse();
	}
}
```

  Match `Success<T>`/`Failed` construction to their actual constructors (pattern shapes are proven by `Login.razor`'s existing switch; adjust `new Success<FakeResult>(new())` if the union uses factory methods — the compile is the check).
- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement**

```csharp
using Microsoft.AspNetCore.Components.Forms;
using Norse.Abstractions.Components;
using Norse.Abstractions.Contracts;

namespace Norse.AuthN.Components;

/// <summary>
/// The pit-of-success submit seam: pages hand <see cref="SubmitAsync{T}(EditContext, Func{CancellationToken, Task{Outcome{T}}}, Action{T})"/>
/// the call and the success continuation, and the <see cref="Outcome{T}"/> error story is handled where it
/// cannot be forgotten — <c>Failed</c> renders through <see cref="EditContextServerErrorsExtensions.ApplyServerErrors"/>,
/// success clears prior server errors before the continuation runs. Total over the <see cref="Outcome{T}"/> domain
/// only: exceptions (a throwing transport, a throwing continuation) propagate to the circuit's error boundary
/// deliberately — swallowing them here would be a silent fallback.
/// </summary>
public abstract class OutcomeFormComponentBase : AsyncComponentBase
{
	/// <summary>True while a submit is in flight — bind to the submit button's <c>Disabled</c> state.</summary>
	protected bool IsSubmitting { get; private set; }

	/// <summary>Synchronous-continuation convenience over the <see cref="Func{T, Task}"/> overload.</summary>
	protected Task SubmitAsync<T>(EditContext editContext, Func<CancellationToken, Task<Outcome<T>>> call, Action<T> onSuccess)
		where T : notnull =>
		SubmitAsync(editContext, call, value =>
		{
			onSuccess(value);
			return Task.CompletedTask;
		});

	/// <summary>Dispatches <paramref name="call"/> and routes its <see cref="Outcome{T}"/>: failure into the form, success into <paramref name="onSuccess"/>.</summary>
	protected async Task SubmitAsync<T>(EditContext editContext, Func<CancellationToken, Task<Outcome<T>>> call, Func<T, Task> onSuccess)
		where T : notnull
	{
		ArgumentNullException.ThrowIfNull(editContext);
		if (IsSubmitting)
			return;

		IsSubmitting = true;
		try
		{
			// No ConfigureAwait(false): component code must resume on the renderer's sync context.
			var outcome = await call(CancellationToken);
			switch (outcome)
			{
				case Success<T>(var value):
					editContext.ClearServerErrors();
					await onSuccess(value);
					break;
				case Failed(var problem):
					editContext.ApplyServerErrors(problem);
					break;
			}
		}
		finally
		{
			IsSubmitting = false;
		}
	}
}
```

- [ ] **Step 4: Run to green.**
- [ ] **Step 5: Stage.** Stop.

---

### Task 9: `EmailExists` wire contract (Heimdall)

**Files:**
- Create: `Heimdall/src/AuthN.Services/EmailExistsRequest.cs`
- Modify: `Heimdall/src/AuthN.Services/IAuthenticationService.cs` (add the operation)
- Test: `Heimdall/tests/AuthN.Services.Tests/RequestContractTests.cs` (extend the purity locks)

**Interfaces:**
- Produces: `EmailExistsRequest { string Email }`; `Task<Outcome<BoolResponse>> EmailExists(EmailExistsRequest request, CancellationToken cancellationToken = default)` on `IAuthenticationService` — consumed by Tasks 10 and 13. `BoolResponse` is Asgard's existing wire bool (`Abstractions.Contracts`).

- [ ] **Step 1: Extend the failing purity-lock tests** — `RequestContractTests` already walks wire records and service methods; add `EmailExistsRequest` to the covered set for `Wire_records_carry_no_Authorize_attribute` (follow the file's existing enumeration mechanism — if it reflects over the assembly, the new record is picked up automatically and this step is verifying that; if it lists types, add the type). `Every_service_method_ends_with_a_trailing_cancellation_token` must fail until the method exists only if the test enumerates a hardcoded expectation — otherwise write one focused new test:

```csharp
	[Fact]
	void Email_exists_is_declared_on_the_authentication_contract()
	{
		var method = typeof(IAuthenticationService).GetMethod("EmailExists");

		method.ShouldNotBeNull();
		method.ReturnType.ShouldBe(typeof(Task<Outcome<BoolResponse>>));
	}

	[Fact]
	void Every_service_operation_carries_the_operation_contract_attribute()
	{
		// A code-first gRPC method without [OperationContract] is a silently dead endpoint —
		// this lock covers every current and future operation on both contracts.
		Type[] contracts = [typeof(IAuthenticationService), typeof(IIdentityService)];

		foreach (var method in contracts.SelectMany(c => c.GetMethods()))
			method.GetCustomAttribute<OperationContractAttribute>()
				.ShouldNotBeNull($"{method.DeclaringType!.Name}.{method.Name} is missing [OperationContract]");
	}
```

- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement** — mirror `LoginRequest`'s exact attribute shape:

```csharp
using System.Runtime.Serialization;

namespace Norse.AuthN.Services;

/// <summary>
/// The wire request for the pre-submit email-existence check — UX sugar over an inherently racy
/// lookup; the atomic user-creation conflict in the register handler remains the authority.
/// </summary>
[DataContract]
public sealed record EmailExistsRequest
{
	/// <summary>The email address to check.</summary>
	[DataMember(Order = 1)]
	public required string Email { get; init; }
}
```

  On `IAuthenticationService`, add beside `Register` (mirroring its `[OperationContract]` style exactly as declared in that file):

```csharp
	/// <summary>Reports whether an account already exists for <paramref name="request"/>'s email.</summary>
	[OperationContract]
	Task<Outcome<BoolResponse>> EmailExists(EmailExistsRequest request, CancellationToken cancellationToken = default);
```

  (`[OperationContract]` is load-bearing, not cosmetic — code-first gRPC never surfaces a method without it; the interface's three existing operations all carry it, and the reflection lock above keeps it that way.)

- [ ] **Step 4: Run `AuthN.Services.Tests` to green.** Note: `Himinbjorg`'s `AuthenticationService` implements this interface and now fails to compile **in Himinbjörg's repo** — expected and correct; Task 13 completes the pair. Heimdall's own solution must build clean.
- [ ] **Step 5: Stage.** Stop.

---

### Task 10: Submit-gated async rules (Heimdall)

**Files:**
- Modify: `Heimdall/src/AuthN.Components/RegisterRequestValidator.cs`
- Test: `Heimdall/tests/AuthN.Components.Tests/RegisterRequestValidatorTests.cs` (extend), plus new `Heimdall/tests/AuthN.Components.Tests/CustomAsyncWriteBackTests.cs`

**Interfaces:**
- Consumes: Task 9's `EmailExists`; FluentValidation `CustomAsync`/`Cascade`.
- Produces: `RegisterRequestValidator(IAuthenticationService authenticationService, ILogger<RegisterRequestValidator> logger)` — primary constructor; DI resolves it on both hosts (generated registrations construct via DI, so the new dependencies are satisfied wherever `IAuthenticationService` is registered).

**No rule sets — ruled 2026-08-06 (spec §6.1 amendment, decompilation-backed):** Blazilla's field-change pass builds a bare `MemberNameValidatorSelector` (early-returned before its `Selector`/`RuleSets` parameters apply) and FV's member-name selector carries no rule-set guard — so rule-set gating of field-change validation is unbuildable. The async rule lives in the **default** set, chained with `Cascade(CascadeMode.Stop)` behind the sync shape rules so malformed emails never reach the service. Client-side it fires on the email field's change event — blur, not keystroke (`FluentTextInput` binds on change). Server-side the unmodified `CommandRequestValidator` runs it (Task 2 step 6's adapter test is the guarantee).

- [ ] **Step 1: Write the failing tests**

```csharp
public sealed class RegisterRequestValidatorAsyncTests
{
	static RegisterRequestValidator NewValidator(Outcome<BoolResponse> emailExistsOutcome)
	{
		var service = Substitute.For<IAuthenticationService>();
		service.EmailExists(Arg.Any<EmailExistsRequest>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(emailExistsOutcome));
		return new(service, NullLogger<RegisterRequestValidator>.Instance);
	}

	static RegisterRequest ValidRequest() =>
		new() { Email = "gyal@example.com", Password = "correct horse battery" };

	[Fact]
	async Task An_existing_email_fails_the_email_field()
	{
		var validator = NewValidator(new Success<BoolResponse>(new() { Value = true }));

		var result = await validator.ValidateAsync(ValidRequest());

		result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterRequest.Email));
	}

	[Fact]
	async Task A_free_email_passes()
	{
		var validator = NewValidator(new Success<BoolResponse>(new() { Value = false }));

		var result = await validator.ValidateAsync(ValidRequest());

		result.IsValid.ShouldBeTrue();
	}

	[Fact]
	async Task A_failed_lookup_blocks_with_a_could_not_verify_error()
	{
		var validator = NewValidator(new Failed(new() { Category = ErrorCategory.Fault, CorrelationId = Guid.NewGuid() }));

		var result = await validator.ValidateAsync(ValidRequest());

		result.IsValid.ShouldBeFalse();
	}

	[Fact]
	async Task A_malformed_email_short_circuits_before_the_service_is_called()
	{
		var service = Substitute.For<IAuthenticationService>();
		RegisterRequestValidator validator = new(service, NullLogger<RegisterRequestValidator>.Instance);

		await validator.ValidateAsync(new() { Email = "not-an-email", Password = "correct horse battery" });

		await service.DidNotReceiveWithAnyArgs().EmailExists(default!, default);
	}

	[Fact]
	async Task The_cancellation_token_propagates_into_the_service_call()
	{
		var service = Substitute.For<IAuthenticationService>();
		service.EmailExists(Arg.Any<EmailExistsRequest>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<Outcome<BoolResponse>>(new Success<BoolResponse>(new() { Value = false })));
		RegisterRequestValidator validator = new(service, NullLogger<RegisterRequestValidator>.Instance);
		using CancellationTokenSource source = new();

		await validator.ValidateAsync(ValidRequest(), source.Token);

		await service.Received(1).EmailExists(Arg.Any<EmailExistsRequest>(), source.Token);
	}
}
```

- [ ] **Step 2: Run to verify failure** (constructor doesn't exist yet).
- [ ] **Step 3: Implement** — convert the validator to a primary constructor. The email rule chain becomes one cascaded chain (this is also where Task 11's parse-state rule lives, so shape truth and the exists-check share the stop-on-first-failure cascade):

```csharp
		RuleFor(r => r.Email)
			.Cascade(CascadeMode.Stop)
			.NotEmpty()
			.Must((request, _) => request.EmailParsed.TryGetValue(out _))
				.WithMessage("Enter a valid email address (local@domain.tld).")
			.CustomAsync(async (email, context, cancellationToken) =>
			{
				var outcome = await authenticationService.EmailExists(new() { Email = email }, cancellationToken).ConfigureAwait(false);
				switch (outcome)
				{
					case Success<BoolResponse>({ Value: true }):
						context.AddFailure("This email is already registered.");
						break;
					case Success<BoolResponse>:
						break;
					case Failed(var problem):
						Log.EmailExistsLookupFailed(logger, problem.Category, problem.CorrelationId);
						context.AddFailure("Could not verify this email right now — try again.");
						break;
				}
			});
```

  (If Task 11 executes after this task, the `Must` line lands there instead — whichever task touches the chain second leaves it in exactly this final shape.)

  with a `static partial class Log` `LoggerMessage` delegate (`EventId` new to this file, message `"Email-exists lookup failed: {Category} (correlation {CorrelationId})"`) per the logging-delegates law — the `Fault` correlation is logged before the message collapses to a field error.
- [ ] **Step 4: Run to green.**
- [ ] **Step 5: Add the §6.2 `CustomAsync` write-back proof** (`CustomAsyncWriteBackTests.cs`) — a test-local model/validator (never shipped):

```csharp
public sealed class CustomAsyncWriteBackTests
{
	sealed class Address
	{
		public string PostalCode { get; set; } = "";
		public string City { get; set; } = "";
	}

	sealed class AddressValidator : AbstractValidator<Address>
	{
		public AddressValidator(Func<string, CancellationToken, Task<string?>> lookupCity)
		{
			RuleFor(a => a.PostalCode).NotEmpty();
			RuleFor(a => a.PostalCode).CustomAsync(async (zip, context, cancellationToken) =>
			{
				var city = await lookupCity(zip, cancellationToken);
				if (city is null)
				{
					context.AddFailure("Zip code not found.");
					return;
				}
				context.InstanceToValidate.City = city;
			});
		}
	}

	[Fact]
	async Task A_successful_lookup_writes_back_onto_the_model_and_composes_with_sync_rules()
	{
		AddressValidator validator = new((_, _) => Task.FromResult<string?>("Thibodaux"));
		Address address = new() { PostalCode = "70301" };

		var result = await validator.ValidateAsync(address);

		result.IsValid.ShouldBeTrue();
		address.City.ShouldBe("Thibodaux");
	}

	[Fact]
	async Task A_missing_lookup_fails_the_field_without_writing_back()
	{
		AddressValidator validator = new((_, _) => Task.FromResult<string?>(null));
		Address address = new() { PostalCode = "00000" };

		var result = await validator.ValidateAsync(address);

		result.IsValid.ShouldBeFalse();
		address.City.ShouldBeEmpty();
	}
}
```

- [ ] **Step 6: Run to green; stage.** Stop.

---

### Task 11: `Result<EmailAddress>`-hydrating setter (Heimdall)

**Files:**
- Modify: `Heimdall/src/AuthN.Services/RegisterRequest.cs`
- Modify: `Heimdall/src/AuthN.Services/AuthN.Services.csproj` (add `<PackageReference Include="Norse.Primitives" Version="*" />` — sanctioned by the two-unions law: request objects carry `Result<T>` for scalars)
- Modify: `Heimdall/src/AuthN.Components/RegisterRequestValidator.cs` (email-shape rule reads the parse state)
- Test: `Heimdall/tests/AuthN.Services.Tests/RegisterRequestTests.cs`

**Interfaces:**
- Consumes: `EmailAddress.Parse(string?) → Result<EmailAddress>` (Svartálfheim `Norse.Primitives`, `Pii/EmailAddress.cs` — exists, verified at planning).
- Produces: `RegisterRequest.EmailParsed` (`Result<EmailAddress>`, get-only, never `[DataMember]`) — the validator and the exit-criterion test consume it.

- [ ] **Step 1: Write the failing test matrix**

```csharp
public sealed class RegisterRequestTests
{
	[Fact]
	void Object_initializer_assignment_hydrates_the_parse_state()
	{
		RegisterRequest request = new() { Email = "baw@example.com", Password = "p" };

		request.EmailParsed.TryGetValue(out var email).ShouldBeTrue();
		email.ToString().ShouldBe("baw@example.com");
	}

	[Fact]
	void A_malformed_email_hydrates_a_failed_parse()
	{
		RegisterRequest request = new() { Email = "not-an-email", Password = "p" };

		request.EmailParsed.TryGetValue(out _).ShouldBeFalse();
	}

	[Fact]
	void Repeated_assignment_replaces_the_parse_state()
	{
		RegisterRequest request = new() { Email = "not-an-email", Password = "p" };

		request.Email = "fixed@example.com";

		request.EmailParsed.TryGetValue(out _).ShouldBeTrue();
	}

	[Fact]
	void Protobuf_round_trip_rehydrates_through_the_setter()
	{
		RegisterRequest original = new() { Email = "wire@example.com", Password = "p" };

		using MemoryStream stream = new();
		ProtoBuf.Serializer.Serialize(stream, original);
		stream.Position = 0;
		var roundTripped = ProtoBuf.Serializer.Deserialize<RegisterRequest>(stream);

		roundTripped.EmailParsed.TryGetValue(out var email).ShouldBeTrue();
		email.ToString().ShouldBe("wire@example.com");
	}

	[Fact]
	void The_cached_state_always_equals_a_fresh_parse()
	{
		RegisterRequest request = new() { Email = "probe@example.com", Password = "p" };

		request.EmailParsed.ShouldBe(EmailAddress.Parse(request.Email));
	}
}
```

  Adjust `TryGetValue`/equality calls to `Result<T>`'s actual member names on first compile (the union is an existing platform type; its `Parse` factory shape is confirmed, and `Register.razor`'s `Outcome` usage shows the platform's `TryGetValue` idiom). The mutable `Email` set in the repeated-assignment test requires the property to be `get/set` — it already is effectively mutable for form binding (`@bind-Value`), so this changes nothing about the record's contract.
- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement** — on `RegisterRequest`, replace the auto-property:

```csharp
	string _email = "";

	/// <summary>The email address as entered. Assignment hydrates <see cref="EmailParsed"/> — there is no code path that sets the string without refreshing the parse state.</summary>
	[DataMember(Order = 1)]
	public required string Email
	{
		get => _email;
		set
		{
			_email = value;
			EmailParsed = EmailAddress.Parse(value);
		}
	}

	/// <summary>The cached parse of <see cref="Email"/> — never serialized; deserialization hydrates it by construction because protobuf-net assigns through the same setter.</summary>
	public Result<EmailAddress> EmailParsed { get; private set; } = EmailAddress.Parse("");
```

  In `RegisterRequestValidator`, replace the existing email-shape rule (whatever `EmailAddress()`/regex form it uses today) with the parse-state assertion so shape truth has one source:

```csharp
		RuleFor(r => r.Email)
			.NotEmpty()
			.Must((request, _) => request.EmailParsed.TryGetValue(out _))
			.WithMessage("Enter a valid email address (local@domain.tld).");
```

- [ ] **Step 4: Run `AuthN.Services.Tests` + `AuthN.Components.Tests` to green** (validator tests from Task 10 must still pass).
- [ ] **Step 5: Stage.** Stop.

---

### Task 12: `LoginResult` reshape + mechanism retrofit (Heimdall)

**Files:**
- Modify: `Heimdall/src/AuthN.Services/LoginResult.cs` (delete `Succeeded`)
- Modify: `Heimdall/src/AuthN.Components.FluentUI/Login.razor`
- Modify: `Heimdall/src/AuthN.Components.FluentUI/Register.razor`
- Test: `Heimdall/tests/AuthN.Components.FluentUI.Tests/LoginTests.cs`, `RegisterTests.cs` (extend/replace as below)

**Interfaces:**
- Consumes: Tasks 6–10. Produces: the retrofitted pages — the diff against the hand-rolled originals is issue #39's exit-criterion evidence.

- [ ] **Step 1: Write the failing edit-and-resubmit bUnit test (the deadlock lock, spec §10)** — for Login, with `IAuthenticationService` substituted:

```csharp
	[Fact]
	async Task A_rejected_login_can_be_corrected_and_resubmitted()
	{
		var service = Substitute.For<IAuthenticationService>();
		service.Login(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>())
			.Returns(
				Task.FromResult<Outcome<LoginResult>>(new Failed(Problem.ModelError(ErrorCategory.InvalidCredentials, "Invalid email or password."))),
				Task.FromResult<Outcome<LoginResult>>(new Success<LoginResult>(new() { DeferredCompletionUrl = "/" })));
		Services.AddSingleton(service);
		Services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
		var page = RenderComponent<Login>();

		// (1) server rejection applied
		page.Find("input[type=text]").Change("baw@example.com");
		page.Find("input[type=password]").Change("wrong-password-1");
		await page.Find("form").SubmitAsync();
		page.Markup.ShouldContain("Invalid email or password.");

		// (2) user edits a field — (3) client validation passes — (4) second server call dispatches
		page.Find("input[type=password]").Change("right-password-11");
		await page.Find("form").SubmitAsync();

		await service.Received(2).Login(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>());
	}
```

  Mirror the same four-step test on Register (model-level rejection variant uses a `Failed` with `Conflict`). Selector details bend to FluentUI's rendered markup — use `page.FindAll("fluent-text-input")`-appropriate queries as the existing component tests in this project already do; follow their idiom.
- [ ] **Step 2: Run to verify failure** — fails against the current hand-rolled pages (the second `Login` call never happens: the live deadlock, now caught by a test).
- [ ] **Step 3: Reshape `LoginResult`** — delete the `Succeeded` member and its `[DataMember]`; `DeferredCompletionUrl` remains. Lineage, for the commit message and the record: `Succeeded == true` predates the two-unions design — it is the pre-`Outcome<T>`/`Problem` era's way of carrying failure on a success envelope, so deleting it *completes* the union migration rather than breaking a contract (ruling reaffirmed 2026-08-06 against a second review pass; topology rationale in spec §4). Fix any Heimdall-side compile fallout (`Login.razor` is rewritten next step; test fakes updated).
- [ ] **Step 4: Rewrite the pages.** `Login.razor` becomes:

```razor
@page "/Account/Login"
@inherits OutcomeFormComponentBase
@inject IAuthenticationService AuthenticationService
@inject NavigationManager Navigation

<PageTitle>Log in</PageTitle>

<h1>Log in</h1>

<EditForm Model="_request" OnValidSubmit="HandleLoginAsync" FormName="authn-login">
	<FluentValidator />
	<ModelValidationSummary />
	<FluentTextInput @bind-Value="_request.Email" Label="Email" Required="true" />
	<FluentTextInput @bind-Value="_request.Password" TextInputType="TextInputType.Password" Label="Password" Required="true" />
	<FluentCheckbox @bind-Value="_request.RememberMe" Label="Remember me" />
	<FluentButton Type="ButtonType.Submit" Appearance="ButtonAppearance.Primary" Disabled="IsSubmitting">Log in</FluentButton>
</EditForm>

@code {
	readonly LoginRequest _request = new() { Email = "", Password = "" };

	Task HandleLoginAsync(EditContext editContext) =>
		SubmitAsync(editContext, ct => AuthenticationService.Login(_request, ct),
			result => Navigation.NavigateTo(result.DeferredCompletionUrl ?? "/", forceLoad: true));
}
```

  `Register.razor` (async rule set → `OnSubmit` + explicit async validation per Blazilla's documented pattern):

```razor
@page "/Account/Register"
@inherits OutcomeFormComponentBase
@inject IAuthenticationService AuthenticationService
@inject NavigationManager Navigation

<PageTitle>Register</PageTitle>

<h1>Register</h1>

<EditForm Model="_request" OnSubmit="HandleRegisterAsync" FormName="authn-register">
	<FluentValidator AsyncMode="true" />
	<ModelValidationSummary />
	<FluentTextInput @bind-Value="_request.Email" Label="Email" Required="true" />
	<FluentTextInput @bind-Value="_request.Password" TextInputType="TextInputType.Password" Label="Password" Required="true" />
	<FluentButton Type="ButtonType.Submit" Appearance="ButtonAppearance.Primary" Disabled="IsSubmitting">Register</FluentButton>
</EditForm>

@code {
	readonly RegisterRequest _request = new() { Email = "", Password = "" };

	async Task HandleRegisterAsync(EditContext editContext)
	{
		if (!await editContext.ValidateAsync())
			return;

		await SubmitAsync(editContext, ct => AuthenticationService.Register(_request, ct),
			_ => Navigation.NavigateTo("/Account/Login"));
	}
}
```

  The Blazilla call shape is **verified by decompilation (2026-08-06), not delegated**: parameterless `editContext.ValidateAsync()` (Blazilla's `EditContextExtensions`) raises `EditContext.Validate()`, Blazilla's `OnValidationRequested` handler builds a `DefaultValidatorSelector` (no `Selector`, no `AllRules`, no rule sets in play) and awaits the async pass via the `__FluentValidation_Task` Properties stash — running every default-set rule including the async email chain. Field-change passes build a member-name selector scoped to the changed field, so password edits never trigger the email rule, and email edits trigger it only on the change event (blur). No `RuleSets` parameter appears anywhere. Delete the second `ValidationMessageStore`, `_editContext`, and `OnInitialized` from both pages — their absence is the point.
- [ ] **Step 4a: Write the blur-semantics component test** (the spec §6.1 behavior lock, four parts):

```csharp
	[Fact]
	async Task The_email_exists_check_fires_on_blur_and_submit_but_never_per_keystroke()
	{
		var service = Substitute.For<IAuthenticationService>();
		service.EmailExists(Arg.Any<EmailExistsRequest>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<Outcome<BoolResponse>>(new Success<BoolResponse>(new() { Value = false })));
		service.Register(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<Outcome<RegisterResult>>(new Success<RegisterResult>(new() { Succeeded = true })));
		Services.AddSingleton(service);
		Services.AddScoped<IValidator<RegisterRequest>>(_ =>
			new RegisterRequestValidator(service, NullLogger<RegisterRequestValidator>.Instance));
		var page = RenderComponent<Register>();

		// (1) keystroke input without a change event: no call
		page.Find("input[type=text]").Input("baw@example.com");
		await service.DidNotReceiveWithAnyArgs().EmailExists(default!, default);

		// (2) change (blur): one call is permitted and expected
		page.Find("input[type=text]").Change("baw@example.com");
		await service.Received(1).EmailExists(Arg.Any<EmailExistsRequest>(), Arg.Any<CancellationToken>());

		// (3) sync-invalid submit (malformed email): cascade stops before the service
		service.ClearReceivedCalls();
		page.Find("input[type=text]").Change("not-an-email");
		await page.Find("form").SubmitAsync();
		await service.DidNotReceiveWithAnyArgs().EmailExists(default!, default);

		// (4) a second valid submit calls again
		service.ClearReceivedCalls();
		page.Find("input[type=text]").Change("baw@example.com");
		page.Find("input[type=password]").Change("correct horse battery");
		await page.Find("form").SubmitAsync();
		service.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAuthenticationService.EmailExists))
			.ShouldBeGreaterThanOrEqualTo(1);
	}
```

  (Part 2's exact count and part 4's `>= 1` acknowledge the blur-then-submit design: the change event and the submit pass may each run the rule — the invariant is *never on keystroke input, always by submit*. Selector idiom bends to FluentUI's rendered markup as in the existing tests. Adjust `RegisterResult` construction if Task 12's reshape scope has touched it — see step 3.)
- [ ] **Step 5: Run the full Heimdall test suite to green** — the four-step tests pass against the retrofit; `RequestContractTests` still green.
- [ ] **Step 6: Stage.** Stop.

---

### HUMAN GATE G3 — Heimdall ships

- [ ] PR → CI → merge → tag → publish `Norse.AuthN.Services` / `.Components` / `.Components.FluentUI`. Himinbjörg floats on `Version="*"` and needs the new package before Task 13 compiles.

---

### Task 13: Himinbjörg — handler reshape + `EmailExists` + nested-send proof

**Files:**
- Modify: `Himinbjorg/src/Identity.Web.Server/LoginHandler.cs`
- Modify: `Himinbjorg/src/Identity.Web.Server/AuthenticationService.cs` (implement `EmailExists`)
- Create: `Himinbjorg/src/Identity.Web.Server/EmailExistsCommand.cs`
- Create: `Himinbjorg/src/Identity.Web.Server/EmailExistsHandler.cs`
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/LoginHandlerTests.cs` (extend), `EmailExistsHandlerTests.cs`, `NestedSendIntegrationTests.cs`

**Interfaces:**
- Consumes: Heimdall's published contract (Task 9), `Problem.ModelError` (Task 1), `CommandRequest<TRequest,TResponse>` (Asgard), `NorseUserManager`.
- Produces: `EmailExistsCommand(EmailExistsRequest Request) : CommandRequest<EmailExistsRequest, BoolResponse>` with `[Authorize(Policy = AuthNPolicies.Public)]`, mirroring `LoginCommand`'s exact shape.

- [ ] **Step 1: Write the failing handler tests**

```csharp
	[Fact]
	async Task Wrong_credentials_produce_an_invalid_credentials_model_error()
	{
		var handler = NewHandlerWithFailingSignIn(); // reuse LoginHandlerTests' existing fixture for a SignInManager that returns SignInResult.Failed
		var outcome = await handler.Handle(new(new() { Email = "who@example.com", Password = "nope" }), CancellationToken.None);

		var failed = outcome.ShouldBeOfType<Failed>();
		failed.Problem.Category.ShouldBe(ErrorCategory.InvalidCredentials);
		failed.Problem.Errors[string.Empty].ShouldBe(["Invalid email or password."]);
	}

	[Fact]
	async Task Wrong_user_and_wrong_password_produce_the_same_problem_instance()
	{
		// Record equality would lie here: Problem.Errors is a dictionary, which records compare by
		// reference — two separately built identical Problems are UNEQUAL. The implementation
		// therefore holds ONE static instance and every credential-failure path returns it, making
		// anti-enumeration a reference-identity guarantee rather than a structural coincidence.
		var unknownUser = await NewHandlerWithUnknownUser().Handle(new(new() { Email = "ghost@example.com", Password = "x" }), CancellationToken.None);
		var wrongPassword = await NewHandlerWithFailingSignIn().Handle(new(new() { Email = "real@example.com", Password = "x" }), CancellationToken.None);

		var first = unknownUser.ShouldBeOfType<Failed>().Problem;
		first.ShouldBeSameAs(wrongPassword.ShouldBeOfType<Failed>().Problem);

		// Structural belt over the identity suspenders: the one instance carries exactly the collapse.
		first.Category.ShouldBe(ErrorCategory.InvalidCredentials);
		first.Errors.Keys.ShouldBe([string.Empty]);
		first.Errors[string.Empty].ShouldBe(["Invalid email or password."]);
		first.CorrelationId.ShouldBeNull();
	}
```

  and for `EmailExistsHandler`: found → `Success(BoolResponse { Value = true })`; not found → `Value = false` (fixture idiom: whatever `LoginHandlerTests` already uses to stub `NorseUserManager.FindByEmailAsync`).
- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement.** `LoginHandler` holds one shared instance:

```csharp
	static readonly Failed _invalidCredentials =
		new(Problem.ModelError(ErrorCategory.InvalidCredentials, "Invalid email or password."));
```

  and every credential-failure path — unknown user, wrong password, whichever branches the handler's current shape actually has (note: `PasswordSignInAsync` may already collapse the two into one `SignInResult.Failed`; verify against the real handler and keep whatever single path exists) — returns `_invalidCredentials`. One instance, one code path, reference-provable. `EmailExistsCommand`/`EmailExistsHandler` clone `LoginCommand`/`LoginHandler`'s structure: handler resolves the user via `UserManager.FindByEmailAsync`, returns `new Success<BoolResponse>(new() { Value = user is not null })`. `AuthenticationService.EmailExists` forwards through `ISender` exactly as `Login` does.
- [ ] **Step 4: Write and run the nested-send integration test** — real DI container (the test project's existing pipeline fixture): register the pipeline, both handlers, and a `RegisterRequestValidator` whose `IAuthenticationService` is the real `AuthenticationService` (in-process, backed by stubbed identity services); dispatch `RegisterCommand` through `ISender` and assert (a) it completes without lifetime/scope errors, and (b) **`EmailExistsHandler` actually executed during `RegisterCommand` validation** — the server-side half of "run twice", proven, not claimed (observe via the stubbed `UserManager.FindByEmailAsync` receiving the register email during the dispatch). Cancellation propagation is **not** asserted here with a pre-canceled token — nothing in the chain promises to pre-check one (`Sender` doesn't, `FindByEmailAsync` takes no token), so that test would assert a promise the code doesn't make; token *propagation* is locked at the validator seam instead (Task 10's token-identity test).
- [ ] **Step 5: Full suite green; stage.** Stop.

---

### HUMAN GATE G4 — Himinbjörg ships

- [ ] PR → CI → merge → tag → publish.

---

### Task 14: Yggdrasil hosts adopt generated discovery

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Client/Program.cs`
- Modify: `Yggdrasil/src/Hosting.Web.Server/Program.cs`
- Modify: `Yggdrasil/Directory.Packages.props` (bump Midgard/Heimdall/Himinbjörg pins to the tagged versions — CPM pins explicitly here)
- Test: existing host test projects + the Playwright smoke (`Hosting.Web.Client`'s established smoke pattern)

**Interfaces:**
- Consumes: generated `AddNorseClientComponents()` (both hosts) and `AddNorseComponentAssemblies()` (server), Tasks 4–5.

- [ ] **Step 1: Client** — in `Hosting.Web.Client/Program.cs`, delete the three `AddScoped<IValidator<…>>` lines and the `AddSingleton(new RoutesAdditionalAssemblies([typeof(Login).Assembly]))` line; add `builder.Services.AddNorseClientComponents()` chained into the existing fluent registration block (fluent-chain law). Delete the now-dead `using FluentValidation;` (IDE0005 is never suppressed).
- [ ] **Step 2: Server** — delete the `RoutesAdditionalAssemblies` singleton (line 42) and the `.AddAdditionalAssemblies(typeof(Routes).Assembly, …)` call (line 125); add `builder.Services.AddNorseClientComponents()` beside the existing registrations and chain `.AddNorseComponentAssemblies()` onto `MapRazorComponents<App>()` after the render-mode calls.
- [ ] **Step 3: Build both hosts; run existing host tests.** A route resolution failure here means a discovery rule (Task 3's exclusions) is wrong — fix the generator, not the host.
- [ ] **Step 4: Playwright smoke** — run the established WASM smoke; additionally navigate `/Account/Login` and `/Account/Register` on both render paths, assert the pages resolve (router + endpoint discovery both proven), submit a wrong-credential login and assert "Invalid email or password." renders once (no summary duplicate), correct it and assert a second dispatch occurs (the live edit-and-resubmit proof).
- [ ] **Step 5: Proof-by-adding-one (spec exit criterion) — as a permanent regression lock, not a one-time demo.** Create `Yggdrasil/tests/DiscoveryFixture` — a minimal RCL carrying exactly one `IValidator<T>` implementation (over a fixture-local record) and one `@page`-routed component — referenced by the **client host test project** (never by the hosts themselves). Add a test asserting the generated `AddNorseClientComponents()` output registers the fixture validator (`GetServices<IValidator<FixtureRequest>>()` resolves it) and includes the fixture assembly in `RoutesAdditionalAssemblies`. This keeps referenced-RCL discovery covered against future generator changes — exactly the cross-compilation behavior snapshot tests can miss — and *is* the "adding an assembly requires zero host registration edits" proof, permanently re-run by CI.
- [ ] **Step 6: Stage both `Program.cs` files + props.** Stop.

---

### Task 15: Naglfar — violet ramp + seam tokens (independent)

**Files:**
- Modify: `Naglfar/tokens/color.json`
- Create: `Naglfar/tokens/components/bifrost.json` (following the existing `components/*.json` shape)

No TDD — token authoring is content, exempt per Naglfar's charter. Verification is the build.

- [ ] **Step 1: Add the ramp** to `color.json` after `blue`:

```json
		"violet": {
			"400": { "$type": "color", "$value": "#8b7ae0" },
			"600": { "$type": "color", "$value": "#6d5bd0" }
		},
```

  and to `semantic`: `"bifrost"` entries per stop are **not** added there — the seam is a component token:
- [ ] **Step 2: Add `components/bifrost.json`** mirroring the existing component-token file structure, defining the six stops per theme by reference (`{color.red.600}`, `{color.amber.700}`, `{color.gold.600}`, `{color.green.600}`, `{color.blue.600}`, `{color.violet.600}` for light; the 400/500-weight counterparts for dark, matching spec §3.1's exact hex outcomes) and a composed `--norse-bifrost-seam` `linear-gradient(180deg, …)` custom property — study how `components/*.json` currently emit into `norse-design-tokens.css` and follow that mechanism exactly; if the pipeline has no composed-value precedent, emit the six stops as custom properties and compose the `linear-gradient` at the consumer (Task 16), noting which shape landed.
- [ ] **Step 3: `npm run build`** — confirm `norse-design-tokens.css` and `FluentTokenSeed` regenerate cleanly and the css contains the new custom properties in both theme blocks.
- [ ] **Step 4: Stage `tokens/` + generated outputs** per Naglfar's established generated-file staging practice. Stop.
- [ ] **HUMAN GATE:** npm + NuGet publish (same release step, per Naglfar's charter).

---

### Task 16: Gate visual retrofit (Heimdall)

**Files:**
- Create: `Heimdall/src/AuthN.Components.FluentUI/GateLayout.razor`
- Create: `Heimdall/src/AuthN.Components.FluentUI/GateLayout.razor.css` (scoped)
- Modify: `Heimdall/src/AuthN.Components.FluentUI/Login.razor`, `Register.razor` (add `@layout GateLayout`, headings/subtitles/cross-links per spec copy)
- Test: `Heimdall/tests/AuthN.Components.FluentUI.Tests/GateLayoutTests.cs` (bUnit structural assertions)

**Interfaces:**
- Consumes: `--norse-bifrost-seam` (or the six stop properties — whichever Task 15 landed) from `norse-design-tokens.css`; `LayoutComponentBase`.
- Produces: the `@layout` both auth pages declare — a no-shell full-viewport split.

- [ ] **Step 1: Write the failing structural tests** — bUnit render of `GateLayout` with a body fragment: asserts the identity panel text (`Heimdall keeps the gate.`, `NORSE ARCHITECTURE`, `norse_identity · OpenIddict · OAuth 2.1`), the seam element (`.gate-seam`), and the body content rendering inside `.gate-form`; plus a Login render test asserting the `Forgot password?` and `Create an account` links exist with `href="Account/ForgotPassword"` / `href="Account/Register"`, and Register's `Already have an account? Log in` link.
- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement `GateLayout.razor`**

```razor
@inherits LayoutComponentBase

<div class="gate">
	<aside class="gate-panel">
		<div class="gate-eyebrow">NORSE ARCHITECTURE</div>
		<div class="gate-voice">
			<h2>Heimdall keeps the gate.</h2>
			<p>One identity for every realm — Server, WASM, and MAUI cross the same bridge.</p>
		</div>
		<div class="gate-receipts">norse_identity · OpenIddict · OAuth 2.1</div>
	</aside>
	<div class="gate-seam" aria-hidden="true"></div>
	<main class="gate-form">
		<div class="gate-form-column">
			@Body
		</div>
	</main>
</div>
```

  Scoped CSS (`GateLayout.razor.css`) implements spec §3 exactly: `.gate` full-viewport grid `52% 3px 1fr`; `.gate-panel` constant `var(--norse-color-neutral-900, #1c1a17)` both themes, flex column `space-between`, eyebrow `letter-spacing: .28em`; `.gate-seam` `background: var(--norse-bifrost-seam)`; `.gate-form` theme-following surface via the existing semantic surface custom property; `.gate-form-column` `max-width: 280px; margin: auto;`. The input-width law rides here too, targeting the pages' fields (`::deep fluent-field { width: 100%; }` and the message slot excluded from width — `::deep fluent-field [slot="message"] { max-width: 100%; overflow-wrap: anywhere; }`, adjusting selectors to FluentUI's rendered structure; the ballooning repro from the live run is the manual check). Narrow-viewport `@media (max-width: 768px)`: grid rows `auto 3px 1fr`, panel keeps eyebrow + headline only (`.gate-voice p, .gate-receipts { display: none; }`), seam runs horizontal. Visible `:focus-visible` outline on links; no motion introduced.
- [ ] **Step 4: Add page copy** — Login: subtitle `Welcome back to the bridge.` under the heading; `Forgot password?` beside the Password label; footer link line. Register: subtitle `One identity for every realm.`, `Already have an account? Log in`. Headings per spec (`Log in` / `Create your account`); button copy `Log in` / `Create account`.
- [ ] **Step 5: Run tests to green; manual Playwright screenshot pass** against the running Bifröst (light + dark + narrow) compared to the approved comp; fix drift.
- [ ] **Step 6: Stage.** Stop. (Rides the same open Heimdall fork; ships in the next Heimdall train — HUMAN GATE.)

---

### Task 17: Shell tidy (Yggdrasil)

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Components/Layout/NavMenu.razor` (or the nav's actual file — locate the `Counter`/`Weather` links)
- Modify: the layout file rendering the footer (locate by the `Documentation and demos` string)
- Test: existing component test project — nav/footer structural assertions

- [ ] **Step 1: Failing tests** — bUnit: nav renders a `Template` group label containing Counter/Weather/Auth Required; no `Account/Register` or `Account/Login` nav items; footer contains no `fluentui-blazor.net` or `learn.microsoft.com` links and does contain the platform name + informational version.
- [ ] **Step 2: Implement** — group the three demo links under FluentUI's nav-group affordance labeled `Template`; delete the Register/Login nav items; replace the footer's two promo links with one line: `Norse Architecture · {InformationalVersion}`. The version shown is **the deployed host's**, not this RCL's — `Assembly.GetEntryAssembly()` (falling back to the layout's own assembly only when the entry assembly is null, e.g. under bUnit), reading `AssemblyInformationalVersionAttribute` trimmed at `+` (build metadata is noise). `GetExecutingAssembly()` here would report `Hosting.Web.Components`' version, which can drift from the host's once packages float. Styled `font.size.xs` / `color.neutral.500` via the token custom properties.
- [ ] **Step 3: Green; stage.** Stop. (Rides Yggdrasil's train with Task 14 — same HUMAN GATE.)

---

## Plan Self-Review (performed at write time)

- **Spec coverage:** mechanism spec §3 → Tasks 6–7; §4 → Tasks 12–13; §5 → Task 8; §6 → Tasks 9–10, 13; §7 → Tasks 2–5, 14; §8 invariants → tests in 6, 7, 10, 12; §9 ordering → the gate sequence; §10 → distributed per task; §11 exit criteria → Tasks 12 (retrofit), 14 (zero-edit proof, step 5), 11 (parse-failure seam), 10+13 (email-exists live); §12 → Task 11. Visual spec §3 → Task 16; §4 → Task 17; §5 → Task 15; §6 sequencing → task order. The §11 "next component lands on the mechanism" criterion is deliberately **not** a task here — it's the lift-and-shift's first move, gated behind this plan by the issue's own definition.
- **Known verify-at-compile points, called out in their tasks:** `Success<T>`/`Failed` construction (Task 8), `Result<T>` member names (Task 11), `FluentMessageBar` intent enum (Task 7), `AddAdditionalAssemblies` extension-holder type (Task 5), Style Dictionary composed-value support (Task 15). Each has its check built into the failing-compile or failing-test step — none is a placeholder; all are pinned to a named alternative. The Blazilla validation call shape is **not** on this list: it is verified by decompilation (2026-08-06 review pass — selector chain, `__FluentValidation_RuleSet`/`__FluentValidation_Task` Properties stashes, member-name field-change selector) and locked by Task 12's blur-semantics component test.
- **Type consistency:** `AddNorseClientComponents` (Tasks 4, 5, 14), `EmailExistsRequest`/`BoolResponse` (Tasks 9, 10, 12, 13), `EmailParsed` (Tasks 10, 11), `CoordinatorKey` (Task 6), `DiscoveryFixture` (Task 14) — names match across tasks. (Second review pass, 2026-08-06: rule sets deleted from the design entirely — `SubmitRuleSet` no longer exists anywhere in this plan; `[OperationContract]` restored to Task 9; anti-enumeration is reference-identity via a shared `Failed` instance in Task 13; the coordinator self-notifies on validation-request clears in Task 6.)
