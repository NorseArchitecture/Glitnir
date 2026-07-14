# Heimdall/Himinbjörg AuthN Bootstrap Slice — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (default) or `superpowers:executing-plans` (separate-session fallback, never interchangeable). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove the whole pipe — gRPC contract, Mediator handler pattern, real Himinbjörg persistence, gRPC client wiring in both Blazor Server and WASM, and Razor UI — end to end, using only the three issuance operations (`Login`, `Register`, `Logout`). Nothing else is in scope; the full `IAccountService` lifecycle surface is deliberately deferred to a follow-on plan once this pipe is proven.

**Architecture:** Six realms, in strict dependency order. Asgard gets a minimal hand-written mediator core (`Outcome`/`Outcome<T>`, `ICommandRequest<T>`, `IRequestHandler<T,T>`) — no source generator yet, that's future work once this pattern proves out. Heimdall's `AuthN.Components` declares `IAuthenticationService` as a protobuf-net.Grpc code-first contract (per the platform-wide reinstatement, `Platform/specs/2026-07-13-protobuf-net-grpc-reinstated-design.md`). Himinbjörg's new `Identity.Web.Server` implements it as a thin forwarder over three `IRequestHandler<,>` implementations that call directly into `UserManager<NorseUser>`/`SignInManager<NorseUser>`. Yggdrasil wires the server side (gRPC hosting + the real `norse_identity` `DbContext`, for the first time) and the client side (a protobuf-net.Grpc client proxy over gRPC-Web, for the first time). Bifrost's AppHost gets `Hosting.Web.Server` wired into the Aspire composition for the first time, referencing the already-provisioned `norse-identity` Postgres database. Heimdall's `AuthN.Components.FluentUI` supplies the three Razor components (FluentUI Blazor v5), rendered `@rendermode InteractiveAuto` so both the Blazor Server and WASM paths are genuinely exercised.

**Tech Stack:** .NET 11, C#, protobuf-net.Grpc + protobuf-net.Grpc.AspNetCore (code-first, per the platform reinstatement), Grpc.Net.Client.Web (gRPC-Web transport), ASP.NET Core Identity v3 (`UserManager<NorseUser>`/`SignInManager<NorseUser>`), Npgsql.EntityFrameworkCore.PostgreSQL, FluentValidation, FluentUI Blazor v5 (RC4), Blazored.FluentValidation, xUnit v3 + Shouldly + NSubstitute, .NET Aspire 13.x

## Global Constraints

- Target framework: `net11.0` for every project (matches every existing realm's `Directory.Build.props`).
- `internal sealed` is the default accessibility; omit accessibility keywords when they are the default (`omit_if_default`). A type needs a justified cross-assembly caller to be `public`.
- `var` for return-value assignments only; explicit type + `new()` for construction.
- Tabs for indentation everywhere except YAML/JSON (2-space) and Razor (4-space, per this platform's `.editorconfig`).
- US English spelling in all identifiers, comments, docs.
- No automatic git commits — stage only (`git add`); the human commits.
- Shouldly for all assertions; NSubstitute for all mocks.
- No force-push to `master`. No `--no-verify`.
- `NorseRef` for cross-realm references; plain `<ProjectReference>` for same-realm references. No `NorseRef` inside a `<Target>` block (YGG301).
- `[DataContract]`/`[DataMember(Order = N)]` on every wire-crossing type (`System.Runtime.Serialization`, BCL — no extra package needed for these two attributes specifically).
- `[ServiceContract]`/`[OperationContract]` come from the `protobuf-net.Grpc` package (brings in `System.ServiceModel.Primitives` transitively).
- Every `[OperationContract]` method has a matching `IRequestHandler<TRequest, TResponse>` — no business logic in the gRPC service class itself, per `Heimdall/specs/2026-07-13-authn-identity-split-design.md` §0/§3.
- `LoginRequest`/`RegisterRequest` deliberately use mutable (`get; set;`) `[DataMember]` properties, not `init` — they are direct `EditForm` binding targets in Task 6, and introducing a parallel mutable form-model type purely to preserve `init`-only wire records would duplicate the validator for no benefit at this scale. Every other record in this plan (`LoginResponse`, `RegisterResponse`, `LogoutRequest`, `Outcome`, `Outcome<T>`, `Problem`) stays `init`-only as usual.

---

## File Map

### Asgard
| Action | Path |
|---|---|
| Create | `Asgard/src/Abstractions.Mediator/Abstractions.Mediator.csproj` |
| Create | `Asgard/src/Abstractions.Mediator/ErrorCategory.cs` |
| Create | `Asgard/src/Abstractions.Mediator/Problem.cs` |
| Create | `Asgard/src/Abstractions.Mediator/Outcome.cs` |
| Create | `Asgard/src/Abstractions.Mediator/ICommandRequest.cs` |
| Create | `Asgard/src/Abstractions.Mediator/IRequestHandler.cs` |
| Create | `Asgard/tests/Abstractions.Mediator.Tests/Abstractions.Mediator.Tests.csproj` |
| Create | `Asgard/tests/Abstractions.Mediator.Tests/OutcomeTests.cs` |
| Modify | `Asgard/Asgard.slnx` |
| Modify | `Bifrost.slnx` |

### Heimdall
| Action | Path |
|---|---|
| Create | `Heimdall/Heimdall.slnx` |
| Create | `Heimdall/src/AuthN.Components/AuthN.Components.csproj` |
| Create | `Heimdall/src/AuthN.Components/IAuthenticationService.cs` |
| Create | `Heimdall/src/AuthN.Components/LoginRequest.cs` |
| Create | `Heimdall/src/AuthN.Components/LoginResponse.cs` |
| Create | `Heimdall/src/AuthN.Components/RegisterRequest.cs` |
| Create | `Heimdall/src/AuthN.Components/RegisterResponse.cs` |
| Create | `Heimdall/src/AuthN.Components/LogoutRequest.cs` |
| Create | `Heimdall/src/AuthN.Components/LoginRequestValidator.cs` |
| Create | `Heimdall/src/AuthN.Components/RegisterRequestValidator.cs` |
| Create | `Heimdall/tests/AuthN.Components.Tests/AuthN.Components.Tests.csproj` |
| Create | `Heimdall/tests/AuthN.Components.Tests/LoginRequestValidatorTests.cs` |
| Create | `Heimdall/tests/AuthN.Components.Tests/RegisterRequestValidatorTests.cs` |
| Create | `Heimdall/src/AuthN.Components.FluentUI/AuthN.Components.FluentUI.csproj` |
| Create | `Heimdall/src/AuthN.Components.FluentUI/_Imports.razor` |
| Create | `Heimdall/src/AuthN.Components.FluentUI/Login.razor` |
| Create | `Heimdall/src/AuthN.Components.FluentUI/Register.razor` |
| Create | `Heimdall/src/AuthN.Components.FluentUI/Logout.razor` |
| Modify | `Bifrost.slnx` |

### Himinbjörg
| Action | Path |
|---|---|
| Create | `Himinbjorg/src/Identity.Web.Server/Identity.Web.Server.csproj` |
| Create | `Himinbjorg/src/Identity.Web.Server/LoginHandler.cs` |
| Create | `Himinbjorg/src/Identity.Web.Server/RegisterHandler.cs` |
| Create | `Himinbjorg/src/Identity.Web.Server/LogoutHandler.cs` |
| Create | `Himinbjorg/src/Identity.Web.Server/AuthenticationService.cs` |
| Create | `Himinbjorg/src/Identity.Web.Server/ServiceCollectionExtensions.cs` |
| Create | `Himinbjorg/src/Identity.Web.Server/WebApplicationExtensions.cs` |
| Create | `Himinbjorg/tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj` |
| Create | `Himinbjorg/tests/Identity.Web.Server.Tests/LoginHandlerTests.cs` |
| Create | `Himinbjorg/tests/Identity.Web.Server.Tests/RegisterHandlerTests.cs` |
| Create | `Himinbjorg/tests/Identity.Web.Server.Tests/LogoutHandlerTests.cs` |
| Modify | `Himinbjorg/Himinbjorg.slnx` |
| Modify | `Himinbjorg/src/Identity/Identity.csproj` (stale `Norse.Auth.Server` comment → `Norse.Identity.Web.Server`) |

### Yggdrasil
| Action | Path |
|---|---|
| Modify | `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj` |
| Modify | `Yggdrasil/src/Hosting.Web.Server/Program.cs` |
| Modify | `Yggdrasil/src/Hosting.Web.Client/Hosting.Web.Client.csproj` |
| Modify | `Yggdrasil/src/Hosting.Web.Client/Program.cs` |
| Create | `Yggdrasil/src/Hosting.Web.Client/BrowserCredentialsHandler.cs` |

### Bifrost
| Action | Path |
|---|---|
| Modify | `src/Orchestration.AppHost/AppHost.cs` |

---

## Task 1: Asgard — `Norse.Abstractions.Mediator`

**Files:**
- Create: `Asgard/src/Abstractions.Mediator/Abstractions.Mediator.csproj`
- Create: `Asgard/src/Abstractions.Mediator/ErrorCategory.cs`
- Create: `Asgard/src/Abstractions.Mediator/Problem.cs`
- Create: `Asgard/src/Abstractions.Mediator/Outcome.cs`
- Create: `Asgard/src/Abstractions.Mediator/ICommandRequest.cs`
- Create: `Asgard/src/Abstractions.Mediator/IRequestHandler.cs`
- Test: `Asgard/tests/Abstractions.Mediator.Tests/OutcomeTests.cs`
- Modify: `Asgard/Asgard.slnx`, `Bifrost.slnx`

**Interfaces:**
- Produces:
  - `Norse.Abstractions.Mediator.ErrorCategory` — enum: `Validation = 1`, `NotFound = 2`, `Conflict = 3`, `LockedOut = 4`, `InvalidCredentials = 5`, `NotAllowed = 6`.
  - `Norse.Abstractions.Mediator.Problem` — `sealed record` with `ErrorCategory Category { get; init; }` and `IReadOnlyDictionary<string, string[]> Errors { get; init; }`.
  - `Norse.Abstractions.Mediator.Outcome` — `sealed record` with `bool IsSuccess { get; init; }`, `Problem? Problem { get; init; }`, static `Ok()` / `Err(ErrorCategory, IReadOnlyDictionary<string,string[]>?)`.
  - `Norse.Abstractions.Mediator.Outcome<T>` — same shape plus `T? Value { get; init; }`, static `Ok(T)` / `Err(ErrorCategory, IReadOnlyDictionary<string,string[]>?)`.
  - `Norse.Abstractions.Mediator.ICommandRequest<TResponse>` — empty marker interface.
  - `Norse.Abstractions.Mediator.IRequestHandler<TRequest, TResponse>` — `ValueTask<TResponse> Handle(TRequest request, CancellationToken cancellationToken)`.

- [ ] **Step 1: Create the project file**

`Asgard/src/Abstractions.Mediator/Abstractions.Mediator.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Abstractions.Mediator: the platform's Outcome/Outcome&lt;T&gt; application-result vocabulary, and the ICommandRequest/IRequestHandler mediator-dispatch shapes. Declared law — no dispatch implementation lives here.</Description>
	</PropertyGroup>
</Project>
```

- [ ] **Step 2: Write the failing test**

`Asgard/tests/Abstractions.Mediator.Tests/Abstractions.Mediator.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Abstractions.Mediator/Abstractions.Mediator.csproj" />
	</ItemGroup>
</Project>
```

`Asgard/tests/Abstractions.Mediator.Tests/OutcomeTests.cs`:
```csharp
using Norse.Abstractions.Mediator;

namespace Norse.Abstractions.Mediator.Tests;

public class OutcomeTests
{
	[Fact]
	public void Ok_sets_IsSuccess_true_and_no_problem()
	{
		var outcome = Outcome.Ok();

		outcome.IsSuccess.ShouldBeTrue();
		outcome.Problem.ShouldBeNull();
	}

	[Fact]
	public void Err_sets_IsSuccess_false_and_carries_the_category()
	{
		var outcome = Outcome.Err(ErrorCategory.Conflict);

		outcome.IsSuccess.ShouldBeFalse();
		outcome.Problem.ShouldNotBeNull();
		outcome.Problem.Category.ShouldBe(ErrorCategory.Conflict);
	}

	[Fact]
	public void Err_carries_field_keyed_errors_when_provided()
	{
		var errors = new Dictionary<string, string[]> { ["Email"] = ["'Email' must not be empty."] };

		var outcome = Outcome.Err(ErrorCategory.Validation, errors);

		outcome.Problem!.Errors["Email"].ShouldBe(["'Email' must not be empty."]);
	}

	[Fact]
	public void Generic_Ok_carries_the_value()
	{
		var outcome = Outcome<int>.Ok(42);

		outcome.IsSuccess.ShouldBeTrue();
		outcome.Value.ShouldBe(42);
	}

	[Fact]
	public void Generic_Err_carries_no_value()
	{
		var outcome = Outcome<int>.Err(ErrorCategory.NotFound);

		outcome.IsSuccess.ShouldBeFalse();
		outcome.Value.ShouldBe(0);
		outcome.Problem!.Category.ShouldBe(ErrorCategory.NotFound);
	}
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test Asgard/tests/Abstractions.Mediator.Tests/Abstractions.Mediator.Tests.csproj`
Expected: FAIL to compile — `Outcome`, `ErrorCategory`, `Outcome<T>` don't exist yet.

- [ ] **Step 4: Implement `ErrorCategory`**

`Asgard/src/Abstractions.Mediator/ErrorCategory.cs`:
```csharp
namespace Norse.Abstractions.Mediator;

/// <summary>
/// Trimmed application-level error vocabulary an <see cref="Outcome"/>/<see cref="Outcome{T}"/> carries
/// on failure. <see cref="LockedOut"/>/<see cref="InvalidCredentials"/>/<see cref="NotAllowed"/> are an
/// AuthN-specific extension over the platform's base Validation/NotFound/Conflict trio — the first real
/// consumer of this type, per <c>Heimdall/specs/2026-07-13-authn-identity-split-design.md</c> §3.1.
/// </summary>
public enum ErrorCategory
{
	Validation = 1,
	NotFound = 2,
	Conflict = 3,
	LockedOut = 4,
	InvalidCredentials = 5,
	NotAllowed = 6,
}
```

- [ ] **Step 5: Implement `Problem`**

`Asgard/src/Abstractions.Mediator/Problem.cs`:
```csharp
using System.Runtime.Serialization;

namespace Norse.Abstractions.Mediator;

/// <summary>
/// The structured detail an <see cref="Outcome"/>/<see cref="Outcome{T}"/> carries on failure.
/// </summary>
[DataContract]
public sealed record Problem
{
	[DataMember(Order = 1)]
	public required ErrorCategory Category { get; init; }

	[DataMember(Order = 2)]
	public IReadOnlyDictionary<string, string[]> Errors { get; init; } = new Dictionary<string, string[]>();
}
```

- [ ] **Step 6: Implement `Outcome` and `Outcome<T>`**

`Asgard/src/Abstractions.Mediator/Outcome.cs`:
```csharp
using System.Runtime.Serialization;

namespace Norse.Abstractions.Mediator;

/// <summary>
/// The platform's application-level result vehicle for operations with no success payload.
/// </summary>
[DataContract]
public sealed record Outcome
{
	[DataMember(Order = 1)]
	public required bool IsSuccess { get; init; }

	[DataMember(Order = 2)]
	public Problem? Problem { get; init; }

	public static Outcome Ok() => new() { IsSuccess = true };

	public static Outcome Err(ErrorCategory category, IReadOnlyDictionary<string, string[]>? errors = null) =>
		new() { IsSuccess = false, Problem = new Problem { Category = category, Errors = errors ?? new Dictionary<string, string[]>() } };
}

/// <summary>
/// The platform's application-level result vehicle for operations with a success payload of type
/// <typeparamref name="T"/>.
/// </summary>
[DataContract]
public sealed record Outcome<T>
{
	[DataMember(Order = 1)]
	public required bool IsSuccess { get; init; }

	[DataMember(Order = 2)]
	public T? Value { get; init; }

	[DataMember(Order = 3)]
	public Problem? Problem { get; init; }

	public static Outcome<T> Ok(T value) => new() { IsSuccess = true, Value = value };

	public static Outcome<T> Err(ErrorCategory category, IReadOnlyDictionary<string, string[]>? errors = null) =>
		new() { IsSuccess = false, Problem = new Problem { Category = category, Errors = errors ?? new Dictionary<string, string[]>() } };
}
```

- [ ] **Step 7: Implement the mediator-dispatch shapes**

`Asgard/src/Abstractions.Mediator/ICommandRequest.cs`:
```csharp
namespace Norse.Abstractions.Mediator;

/// <summary>
/// Marker for a mediator-dispatched request whose handler produces a <typeparamref name="TResponse"/>.
/// </summary>
public interface ICommandRequest<TResponse>;
```

`Asgard/src/Abstractions.Mediator/IRequestHandler.cs`:
```csharp
namespace Norse.Abstractions.Mediator;

/// <summary>
/// Handles a single <see cref="ICommandRequest{TResponse}"/>. Every gRPC <c>[OperationContract]</c>
/// method forwards to exactly one of these — no business logic lives in the gRPC service class
/// (<c>Heimdall/specs/2026-07-13-authn-identity-split-design.md</c> §0/§3).
/// </summary>
public interface IRequestHandler<in TRequest, TResponse>
	where TRequest : ICommandRequest<TResponse>
{
	ValueTask<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}
```

- [ ] **Step 8: Run the test to verify it passes**

Run: `dotnet test Asgard/tests/Abstractions.Mediator.Tests/Abstractions.Mediator.Tests.csproj`
Expected: PASS — 5 tests green.

- [ ] **Step 9: Wire the new projects into `Asgard.slnx` and `Bifrost.slnx`**

In `Asgard/Asgard.slnx`, add under `/src/`:
```xml
		<Project Path="src/Abstractions.Mediator/Abstractions.Mediator.csproj" />
```
and under `/tests/`:
```xml
		<Project Path="tests/Abstractions.Mediator.Tests/Abstractions.Mediator.Tests.csproj" />
```

In `Bifrost.slnx`, under the existing `/Abstractions/src/` folder, add:
```xml
		<Project Path="Asgard/src/Abstractions.Mediator/Abstractions.Mediator.csproj" />
```
and under `/Abstractions/tests/`:
```xml
		<Project Path="Asgard/tests/Abstractions.Mediator.Tests/Abstractions.Mediator.Tests.csproj" />
```

- [ ] **Step 10: Commit**

```bash
cd Asgard
git add src/Abstractions.Mediator tests/Abstractions.Mediator.Tests Asgard.slnx
git commit -m "feat: add Outcome/Outcome<T> and the mediator dispatch shapes"
cd ..
git add Bifrost.slnx
git commit -m "chore: wire Asgard's Abstractions.Mediator into Bifrost.slnx"
```

---

## SHIP GATE — Asgard

**STOP. Do not start Task 2 until this gate is cleared.**

1. Push the Asgard commit; open a PR against `master`; confirm CI is green.
2. Merge the PR; push a version tag; confirm `Norse.Abstractions.Mediator` is live on the NuGet feed.
3. Push the Bifrost commit (submodule pointer + `Bifrost.slnx` update).

Only after the package is live does Task 2 begin.

---

## Task 2: Heimdall — `AuthN.Components` (the contract)

**Files:**
- Create: `Heimdall/Heimdall.slnx`
- Create: `Heimdall/src/AuthN.Components/AuthN.Components.csproj`
- Create: `Heimdall/src/AuthN.Components/IAuthenticationService.cs`
- Create: `Heimdall/src/AuthN.Components/LoginRequest.cs`
- Create: `Heimdall/src/AuthN.Components/LoginResponse.cs`
- Create: `Heimdall/src/AuthN.Components/RegisterRequest.cs`
- Create: `Heimdall/src/AuthN.Components/RegisterResponse.cs`
- Create: `Heimdall/src/AuthN.Components/LogoutRequest.cs`
- Create: `Heimdall/src/AuthN.Components/LoginRequestValidator.cs`
- Create: `Heimdall/src/AuthN.Components/RegisterRequestValidator.cs`
- Test: `Heimdall/tests/AuthN.Components.Tests/LoginRequestValidatorTests.cs`, `RegisterRequestValidatorTests.cs`
- Modify: `Bifrost.slnx`

**Interfaces:**
- Consumes: `Outcome`, `Outcome<T>`, `ICommandRequest<TResponse>` from Task 1 (`Norse.Abstractions.Mediator`).
- Produces:
  - `Norse.AuthN.Components.IAuthenticationService` — `[ServiceContract]`; `Login`, `Register`, `Logout`.
  - `Norse.AuthN.Components.LoginRequest` — implements `ICommandRequest<Outcome<LoginResponse>>`; `string Email { get; set; }`, `string Password { get; set; }`, `bool RememberMe { get; set; }`.
  - `Norse.AuthN.Components.LoginResponse` — `LoginStatus Status { get; init; }` (`Succeeded = 1`, `RequiresTwoFactor = 2`, `RequiresConfirmedEmail = 3`).
  - `Norse.AuthN.Components.RegisterRequest` — implements `ICommandRequest<Outcome<RegisterResponse>>`; `string Email { get; set; }`, `string Password { get; set; }`.
  - `Norse.AuthN.Components.RegisterResponse` — `Guid UserId { get; init; }`.
  - `Norse.AuthN.Components.LogoutRequest` — implements `ICommandRequest<Outcome>`; empty.
  - `Norse.AuthN.Components.LoginRequestValidator : AbstractValidator<LoginRequest>`, `RegisterRequestValidator : AbstractValidator<RegisterRequest>`.

- [ ] **Step 1: Create `Heimdall.slnx`**

`Heimdall/Heimdall.slnx`:
```xml
<Solution>
	<Folder Name="/Solution Items/">
		<File Path=".editorconfig" />
		<File Path=".gitattributes" />
		<File Path=".gitignore" />
		<File Path="CLAUDE.md" />
		<File Path="Directory.Build.props" />
		<File Path="global.json" />
		<File Path="LICENSE" />
		<File Path="nuget.config" />
		<File Path="README.md" />
	</Folder>
	<Folder Name="/src/">
		<File Path="src/Directory.Build.props" />
		<File Path="src/Directory.Build.targets" />
		<Project Path="src/AuthN.Components/AuthN.Components.csproj" />
	</Folder>
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<File Path="tests/Directory.Build.targets" />
		<Project Path="tests/AuthN.Components.Tests/AuthN.Components.Tests.csproj" />
	</Folder>
</Solution>
```

- [ ] **Step 2: Create the project file**

`Heimdall/src/AuthN.Components/AuthN.Components.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.AuthN.Components: the IAuthenticationService gRPC contract (protobuf-net.Grpc code-first), its request/response records, and FluentValidation validators. No implementation.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="protobuf-net.Grpc" Version="*" />
		<PackageReference Include="FluentValidation" Version="*" />
	</ItemGroup>
	<ItemGroup>
		<NorseRef Include="Abstractions.Mediator">
			<Repo>Asgard</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

- [ ] **Step 3: Write the failing validator tests**

`Heimdall/tests/AuthN.Components.Tests/AuthN.Components.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/AuthN.Components/AuthN.Components.csproj" />
	</ItemGroup>
</Project>
```

`Heimdall/tests/AuthN.Components.Tests/LoginRequestValidatorTests.cs`:
```csharp
using Norse.AuthN.Components;

namespace Norse.AuthN.Components.Tests;

public class LoginRequestValidatorTests
{
	private readonly LoginRequestValidator _validator = new();

	[Fact]
	public void Rejects_empty_email()
	{
		var request = new LoginRequest { Email = "", Password = "correct-horse" };

		var result = _validator.Validate(request);

		result.IsValid.ShouldBeFalse();
	}

	[Fact]
	public void Rejects_malformed_email()
	{
		var request = new LoginRequest { Email = "not-an-email", Password = "correct-horse" };

		var result = _validator.Validate(request);

		result.IsValid.ShouldBeFalse();
	}

	[Fact]
	public void Rejects_empty_password()
	{
		var request = new LoginRequest { Email = "user@example.com", Password = "" };

		var result = _validator.Validate(request);

		result.IsValid.ShouldBeFalse();
	}

	[Fact]
	public void Accepts_a_well_formed_request()
	{
		var request = new LoginRequest { Email = "user@example.com", Password = "correct-horse" };

		var result = _validator.Validate(request);

		result.IsValid.ShouldBeTrue();
	}
}
```

`Heimdall/tests/AuthN.Components.Tests/RegisterRequestValidatorTests.cs`:
```csharp
using Norse.AuthN.Components;

namespace Norse.AuthN.Components.Tests;

public class RegisterRequestValidatorTests
{
	private readonly RegisterRequestValidator _validator = new();

	[Fact]
	public void Rejects_malformed_email()
	{
		var request = new RegisterRequest { Email = "not-an-email", Password = "correct-horse-battery" };

		var result = _validator.Validate(request);

		result.IsValid.ShouldBeFalse();
	}

	[Fact]
	public void Rejects_password_shorter_than_eight_characters()
	{
		var request = new RegisterRequest { Email = "user@example.com", Password = "short" };

		var result = _validator.Validate(request);

		result.IsValid.ShouldBeFalse();
	}

	[Fact]
	public void Accepts_a_well_formed_request()
	{
		var request = new RegisterRequest { Email = "user@example.com", Password = "correct-horse-battery" };

		var result = _validator.Validate(request);

		result.IsValid.ShouldBeTrue();
	}
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test Heimdall/tests/AuthN.Components.Tests/AuthN.Components.Tests.csproj`
Expected: FAIL to compile — none of the contract types exist yet.

- [ ] **Step 5: Implement the request/response records**

`Heimdall/src/AuthN.Components/LoginRequest.cs`:
```csharp
using System.Runtime.Serialization;
using Norse.Abstractions.Mediator;

namespace Norse.AuthN.Components;

/// <summary>
/// Deliberately mutable (not <c>init</c>) — this is the direct two-way <c>EditForm</c> binding target
/// for <c>AuthN.Components.FluentUI</c>'s <c>Login.razor</c>; every other record in this contract stays
/// <c>init</c>-only.
/// </summary>
[DataContract]
public sealed record LoginRequest : ICommandRequest<Outcome<LoginResponse>>
{
	[DataMember(Order = 1)]
	public required string Email { get; set; }

	[DataMember(Order = 2)]
	public required string Password { get; set; }

	[DataMember(Order = 3)]
	public bool RememberMe { get; set; }
}
```

`Heimdall/src/AuthN.Components/LoginResponse.cs`:
```csharp
using System.Runtime.Serialization;

namespace Norse.AuthN.Components;

public enum LoginStatus
{
	Succeeded = 1,
	RequiresTwoFactor = 2,
	RequiresConfirmedEmail = 3,
}

[DataContract]
public sealed record LoginResponse
{
	[DataMember(Order = 1)]
	public required LoginStatus Status { get; init; }
}
```

`Heimdall/src/AuthN.Components/RegisterRequest.cs`:
```csharp
using System.Runtime.Serialization;
using Norse.Abstractions.Mediator;

namespace Norse.AuthN.Components;

/// <summary>Deliberately mutable — see <see cref="LoginRequest"/>'s remark.</summary>
[DataContract]
public sealed record RegisterRequest : ICommandRequest<Outcome<RegisterResponse>>
{
	[DataMember(Order = 1)]
	public required string Email { get; set; }

	[DataMember(Order = 2)]
	public required string Password { get; set; }
}
```

`Heimdall/src/AuthN.Components/RegisterResponse.cs`:
```csharp
using System.Runtime.Serialization;

namespace Norse.AuthN.Components;

[DataContract]
public sealed record RegisterResponse
{
	[DataMember(Order = 1)]
	public required Guid UserId { get; init; }
}
```

`Heimdall/src/AuthN.Components/LogoutRequest.cs`:
```csharp
using System.Runtime.Serialization;
using Norse.Abstractions.Mediator;

namespace Norse.AuthN.Components;

/// <summary>
/// Deliberately empty — the caller's authenticated cookie identifies who's logging out. A wire type
/// still exists per operation because protobuf-net.Grpc requires one.
/// </summary>
[DataContract]
public sealed record LogoutRequest : ICommandRequest<Outcome>;
```

- [ ] **Step 6: Implement the validators**

`Heimdall/src/AuthN.Components/LoginRequestValidator.cs`:
```csharp
using FluentValidation;

namespace Norse.AuthN.Components;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
	public LoginRequestValidator()
	{
		RuleFor(x => x.Email).NotEmpty().EmailAddress();
		RuleFor(x => x.Password).NotEmpty();
	}
}
```

`Heimdall/src/AuthN.Components/RegisterRequestValidator.cs`:
```csharp
using FluentValidation;

namespace Norse.AuthN.Components;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
	public RegisterRequestValidator()
	{
		RuleFor(x => x.Email).NotEmpty().EmailAddress();
		// Password *policy* specifics (breach lists, lockout backoff) are out of scope
		// (Heimdall/specs/2026-07-13-authn-identity-split-design.md carries this forward from
		// 2026-06-07-auth-design.md §2); NIST SP 800-63B's length-over-complexity floor is the only
		// rule enforced client/server-side here.
		RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
	}
}
```

- [ ] **Step 7: Implement the service contract**

`Heimdall/src/AuthN.Components/IAuthenticationService.cs`:
```csharp
using Norse.Abstractions.Mediator;
using ProtoBuf.Grpc;

namespace Norse.AuthN.Components;

/// <summary>
/// Issuance surface — real, network-callable gRPC methods that mint or clear the authenticated
/// cookie. Allowed <c>HttpContext</c> coupling in the implementation because minting the credential
/// is the entire job (<c>Heimdall/specs/2026-07-13-authn-identity-split-design.md</c> §2).
/// </summary>
[ServiceContract]
public interface IAuthenticationService
{
	[OperationContract]
	Task<Outcome<LoginResponse>> Login(LoginRequest request, CallContext context = default);

	[OperationContract]
	Task<Outcome<RegisterResponse>> Register(RegisterRequest request, CallContext context = default);

	[OperationContract]
	Task<Outcome> Logout(LogoutRequest request, CallContext context = default);
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test Heimdall/tests/AuthN.Components.Tests/AuthN.Components.Tests.csproj`
Expected: PASS — 7 tests green.

- [ ] **Step 9: Wire `Heimdall.slnx` into `Bifrost.slnx`**

In `Bifrost.slnx`, add a new top-level folder (mirroring the existing `/Abstractions/` pattern):
```xml
	<Folder Name="/AuthN/">
		<File Path="Heimdall/.editorconfig" />
		<File Path="Heimdall/.gitattributes" />
		<File Path="Heimdall/.gitignore" />
		<File Path="Heimdall/CLAUDE.md" />
		<File Path="Heimdall/Directory.Build.props" />
		<File Path="Heimdall/global.json" />
		<File Path="Heimdall/Heimdall.slnx" />
		<File Path="Heimdall/LICENSE" />
		<File Path="Heimdall/nuget.config" />
		<File Path="Heimdall/README.md" />
	</Folder>
	<Folder Name="/AuthN/src/">
		<File Path="Heimdall/src/Directory.Build.props" />
		<File Path="Heimdall/src/Directory.Build.targets" />
		<Project Path="Heimdall/src/AuthN.Components/AuthN.Components.csproj" />
	</Folder>
	<Folder Name="/AuthN/tests/">
		<File Path="Heimdall/tests/Directory.Build.props" />
		<File Path="Heimdall/tests/Directory.Build.targets" />
		<Project Path="Heimdall/tests/AuthN.Components.Tests/AuthN.Components.Tests.csproj" />
	</Folder>
```

- [ ] **Step 10: Commit**

```bash
cd Heimdall
git add Heimdall.slnx src/AuthN.Components tests/AuthN.Components.Tests
git commit -m "feat: add AuthN.Components — IAuthenticationService contract, DTOs, validators"
cd ..
git add Bifrost.slnx
git commit -m "chore: wire Heimdall's AuthN.Components into Bifrost.slnx"
```

---

## SHIP GATE — Heimdall (`AuthN.Components`)

**STOP. Do not start Task 3 until this gate is cleared.**

1. Push the Heimdall commit; open a PR against `master`; confirm CI is green.
2. Merge the PR; push a version tag; confirm `Norse.AuthN.Components` is live on the NuGet feed.
3. Push the Bifrost commit.

---

## Task 3: Himinbjörg — `Identity.Web.Server`

**Files:**
- Create: `Himinbjorg/src/Identity.Web.Server/Identity.Web.Server.csproj`
- Create: `Himinbjorg/src/Identity.Web.Server/LoginHandler.cs`
- Create: `Himinbjorg/src/Identity.Web.Server/RegisterHandler.cs`
- Create: `Himinbjorg/src/Identity.Web.Server/LogoutHandler.cs`
- Create: `Himinbjorg/src/Identity.Web.Server/AuthenticationService.cs`
- Create: `Himinbjorg/src/Identity.Web.Server/ServiceCollectionExtensions.cs`
- Create: `Himinbjorg/src/Identity.Web.Server/WebApplicationExtensions.cs`
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/*`
- Modify: `Himinbjorg/Himinbjorg.slnx`, `Himinbjorg/src/Identity/Identity.csproj` (stale comment fix)

**Interfaces:**
- Consumes: `IAuthenticationService`, `LoginRequest`, `LoginResponse`, `RegisterRequest`, `RegisterResponse`, `LogoutRequest` (Task 2, `Norse.AuthN.Components`); `Outcome`, `Outcome<T>`, `ErrorCategory`, `IRequestHandler<,>` (Task 1); `NorseUser`, `NorseIdentityDbContext`, `AddNorseIdentity()` (existing `Norse.Identity`).
- Produces:
  - `Norse.Identity.Web.Server.LoginHandler : IRequestHandler<LoginRequest, Outcome<LoginResponse>>`
  - `Norse.Identity.Web.Server.RegisterHandler : IRequestHandler<RegisterRequest, Outcome<RegisterResponse>>`
  - `Norse.Identity.Web.Server.LogoutHandler : IRequestHandler<LogoutRequest, Outcome>`
  - `Norse.Identity.Web.Server.AuthenticationService : IAuthenticationService` (internal, thin forwarder)
  - `IServiceCollectionExtensions.AddNorseAuthenticationService(this IServiceCollection, string connectionString)`
  - `IApplicationBuilderExtensions.MapNorseAuthenticationService(this WebApplication)`

- [ ] **Step 1: Fix the stale comment in `Identity.csproj` first**

`Himinbjorg/src/Identity/Identity.csproj` currently reads (in `<Description>`): `"...Runtime library — referenced by Norse.Auth.Server; never by migration tooling."` — `Norse.Auth.Server` predates the 07-11 rename and this realm's own gRPC-implementation project (`Identity.Web.Server`, created in this task). Update to:
```xml
<Description>Norse.Identity: ASP.NET Core Identity v3 entity types, NorseIdentityDbContext (Identity + OpenIddict), NorseUserStore with projection overrides, and DI extension. Runtime library — referenced by Norse.Identity.Web.Server; never by migration tooling.</Description>
```

- [ ] **Step 2: Create the project file**

`Himinbjorg/src/Identity.Web.Server/Identity.Web.Server.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Identity.Web.Server: IAuthenticationService's gRPC implementation over NorseUserStore. Always runs inside an HTTP context, bound into Yggdrasil's Hosting.Web.Server process.</Description>
	</PropertyGroup>
	<ItemGroup>
		<FrameworkReference Include="Microsoft.AspNetCore.App" />
	</ItemGroup>
	<ItemGroup>
		<PackageReference Include="protobuf-net.Grpc.AspNetCore" Version="*" />
		<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="*" />
	</ItemGroup>
	<ItemGroup>
		<ProjectReference Include="../Identity/Identity.csproj" />
	</ItemGroup>
	<ItemGroup>
		<NorseRef Include="AuthN.Components">
			<Repo>Heimdall</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

- [ ] **Step 3: Write the failing handler tests**

`Himinbjorg/tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="*" />
		<PackageReference Include="NSubstitute" Version="*" />
		<ProjectReference Include="../../src/Identity.Web.Server/Identity.Web.Server.csproj" />
	</ItemGroup>
</Project>
```

`Himinbjorg/tests/Identity.Web.Server.Tests/RegisterHandlerTests.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Norse.AuthN.Components;
using Norse.Identity;

namespace Norse.Identity.Web.Server.Tests;

public class RegisterHandlerTests
{
	private static NorseIdentityDbContext CreateContext()
	{
		var options = new DbContextOptionsBuilder<NorseIdentityDbContext>()
			.UseSqlite("DataSource=:memory:")
			.Options;
		var context = new NorseIdentityDbContext(options);
		context.Database.OpenConnection();
		context.Database.EnsureCreated();
		return context;
	}

	private static UserManager<NorseUser> CreateUserManager(NorseIdentityDbContext context)
	{
		var store = new NorseUserStore(context, new IdentityErrorDescriber());
		return new UserManager<NorseUser>(
			store, null, new PasswordHasher<NorseUser>(), [], [],
			new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null, NullLogger<UserManager<NorseUser>>.Instance);
	}

	[Fact]
	public async Task Rejects_an_invalid_request_without_touching_the_store()
	{
		using var context = CreateContext();
		var handler = new RegisterHandler(CreateUserManager(context), new RegisterRequestValidator());
		var request = new RegisterRequest { Email = "not-an-email", Password = "short" };

		var outcome = await handler.Handle(request, CancellationToken.None);

		outcome.IsSuccess.ShouldBeFalse();
		outcome.Problem!.Category.ShouldBe(ErrorCategory.Validation);
		(await context.Users.CountAsync()).ShouldBe(0);
	}

	[Fact]
	public async Task Creates_a_NorseUser_for_a_valid_request()
	{
		using var context = CreateContext();
		var handler = new RegisterHandler(CreateUserManager(context), new RegisterRequestValidator());
		var request = new RegisterRequest { Email = "user@example.com", Password = "correct-horse-battery" };

		var outcome = await handler.Handle(request, CancellationToken.None);

		outcome.IsSuccess.ShouldBeTrue();
		outcome.Value!.UserId.ShouldNotBe(Guid.Empty);
		(await context.Users.SingleAsync()).Email.ShouldBe("user@example.com");
	}

	[Fact]
	public async Task Rejects_a_duplicate_email()
	{
		using var context = CreateContext();
		var userManager = CreateUserManager(context);
		var handler = new RegisterHandler(userManager, new RegisterRequestValidator());
		var request = new RegisterRequest { Email = "user@example.com", Password = "correct-horse-battery" };
		await handler.Handle(request, CancellationToken.None);

		var outcome = await handler.Handle(request, CancellationToken.None);

		outcome.IsSuccess.ShouldBeFalse();
		outcome.Problem!.Category.ShouldBe(ErrorCategory.Conflict);
	}
}
```

`Himinbjorg/tests/Identity.Web.Server.Tests/LogoutHandlerTests.cs`:
```csharp
using Norse.Abstractions.Mediator;
using Norse.AuthN.Components;
using NSubstitute;

namespace Norse.Identity.Web.Server.Tests;

public class LogoutHandlerTests
{
	[Fact]
	public async Task Always_returns_a_successful_outcome()
	{
		var signInManager = MockSignInManager.Create();
		var handler = new LogoutHandler(signInManager);

		var outcome = await handler.Handle(new LogoutRequest(), CancellationToken.None);

		outcome.IsSuccess.ShouldBeTrue();
		await signInManager.Received(1).SignOutAsync();
	}
}
```

Note the `MockSignInManager` helper referenced above and by `LoginHandlerTests` below — `SignInManager<TUser>` has no public parameterless constructor path that NSubstitute can proxy directly without its full dependency graph, so this plan uses a small test-only factory instead of substituting the sealed framework type's constructor by hand in every test.

`Himinbjorg/tests/Identity.Web.Server.Tests/MockSignInManager.cs`:
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Norse.Identity;
using NSubstitute;

namespace Norse.Identity.Web.Server.Tests;

internal static class MockSignInManager
{
	public static SignInManager<NorseUser> Create()
	{
		var userManager = Substitute.For<UserManager<NorseUser>>(
			Substitute.For<IUserStore<NorseUser>>(), null, new PasswordHasher<NorseUser>(),
			Array.Empty<IUserValidator<NorseUser>>(), Array.Empty<IPasswordValidator<NorseUser>>(),
			new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null,
			NullLogger<UserManager<NorseUser>>.Instance);

		return Substitute.For<SignInManager<NorseUser>>(
			userManager, new HttpContextAccessor(),
			Substitute.For<IUserClaimsPrincipalFactory<NorseUser>>(),
			Options.Create(new IdentityOptions()), NullLogger<SignInManager<NorseUser>>.Instance,
			Substitute.For<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>(),
			Substitute.For<IUserConfirmation<NorseUser>>());
	}
}
```

`Himinbjorg/tests/Identity.Web.Server.Tests/LoginHandlerTests.cs`:
```csharp
using Norse.Abstractions.Mediator;
using Norse.AuthN.Components;
using Norse.Identity;
using NSubstitute;

namespace Norse.Identity.Web.Server.Tests;

public class LoginHandlerTests
{
	[Fact]
	public async Task Rejects_an_invalid_request_without_attempting_sign_in()
	{
		var signInManager = MockSignInManager.Create();
		var handler = new LoginHandler(signInManager, new LoginRequestValidator());
		var request = new LoginRequest { Email = "", Password = "" };

		var outcome = await handler.Handle(request, CancellationToken.None);

		outcome.IsSuccess.ShouldBeFalse();
		outcome.Problem!.Category.ShouldBe(ErrorCategory.Validation);
		await signInManager.DidNotReceive().PasswordSignInAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
	}

	[Fact]
	public async Task Returns_LockedOut_when_the_store_reports_lockout()
	{
		var signInManager = MockSignInManager.Create();
		signInManager.PasswordSignInAsync("user@example.com", "wrong", false, true)
			.Returns(Microsoft.AspNetCore.Identity.SignInResult.LockedOut);
		var handler = new LoginHandler(signInManager, new LoginRequestValidator());
		var request = new LoginRequest { Email = "user@example.com", Password = "wrong" };

		var outcome = await handler.Handle(request, CancellationToken.None);

		outcome.IsSuccess.ShouldBeFalse();
		outcome.Problem!.Category.ShouldBe(ErrorCategory.LockedOut);
	}

	[Fact]
	public async Task Returns_Succeeded_when_the_store_signs_in()
	{
		var signInManager = MockSignInManager.Create();
		signInManager.PasswordSignInAsync("user@example.com", "correct-horse", false, true)
			.Returns(Microsoft.AspNetCore.Identity.SignInResult.Success);
		var handler = new LoginHandler(signInManager, new LoginRequestValidator());
		var request = new LoginRequest { Email = "user@example.com", Password = "correct-horse" };

		var outcome = await handler.Handle(request, CancellationToken.None);

		outcome.IsSuccess.ShouldBeTrue();
		outcome.Value!.Status.ShouldBe(LoginStatus.Succeeded);
	}
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test Himinbjorg/tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj`
Expected: FAIL to compile — `LoginHandler`, `RegisterHandler`, `LogoutHandler` don't exist yet.

- [ ] **Step 5: Implement the handlers**

`Himinbjorg/src/Identity.Web.Server/LoginHandler.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Norse.Abstractions.Mediator;
using Norse.AuthN.Components;
using Norse.Identity;

namespace Norse.Identity.Web.Server;

public sealed class LoginHandler(SignInManager<NorseUser> signInManager, LoginRequestValidator validator)
	: IRequestHandler<LoginRequest, Outcome<LoginResponse>>
{
	public async ValueTask<Outcome<LoginResponse>> Handle(LoginRequest request, CancellationToken cancellationToken)
	{
		var validation = await validator.ValidateAsync(request, cancellationToken);
		if (!validation.IsValid)
		{
			return Outcome<LoginResponse>.Err(ErrorCategory.Validation, validation.ToDictionary());
		}

		// SignInManager mints/clears the cookie itself via its own IHttpContextAccessor dependency —
		// no manual HttpContext.SignInAsync call needed here (must register AddHttpContextAccessor()).
		var result = await signInManager.PasswordSignInAsync(
			request.Email, request.Password, request.RememberMe, lockoutOnFailure: true);

		if (result.IsLockedOut)
		{
			return Outcome<LoginResponse>.Err(ErrorCategory.LockedOut);
		}

		if (result.IsNotAllowed)
		{
			return Outcome<LoginResponse>.Err(ErrorCategory.NotAllowed);
		}

		if (result.RequiresTwoFactor)
		{
			return Outcome<LoginResponse>.Ok(new LoginResponse { Status = LoginStatus.RequiresTwoFactor });
		}

		if (!result.Succeeded)
		{
			return Outcome<LoginResponse>.Err(ErrorCategory.InvalidCredentials);
		}

		return Outcome<LoginResponse>.Ok(new LoginResponse { Status = LoginStatus.Succeeded });
	}
}
```

`Himinbjorg/src/Identity.Web.Server/RegisterHandler.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Norse.Abstractions.Mediator;
using Norse.AuthN.Components;
using Norse.Identity;

namespace Norse.Identity.Web.Server;

public sealed class RegisterHandler(UserManager<NorseUser> userManager, RegisterRequestValidator validator)
	: IRequestHandler<RegisterRequest, Outcome<RegisterResponse>>
{
	public async ValueTask<Outcome<RegisterResponse>> Handle(RegisterRequest request, CancellationToken cancellationToken)
	{
		var validation = await validator.ValidateAsync(request, cancellationToken);
		if (!validation.IsValid)
		{
			return Outcome<RegisterResponse>.Err(ErrorCategory.Validation, validation.ToDictionary());
		}

		var user = new NorseUser { UserName = request.Email, Email = request.Email };
		var result = await userManager.CreateAsync(user, request.Password);

		if (!result.Succeeded)
		{
			var errors = result.Errors
				.GroupBy(e => e.Code)
				.ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
			return Outcome<RegisterResponse>.Err(ErrorCategory.Conflict, errors);
		}

		return Outcome<RegisterResponse>.Ok(new RegisterResponse { UserId = user.Id });
	}
}
```

`Himinbjorg/src/Identity.Web.Server/LogoutHandler.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Norse.Abstractions.Mediator;
using Norse.AuthN.Components;
using Norse.Identity;

namespace Norse.Identity.Web.Server;

public sealed class LogoutHandler(SignInManager<NorseUser> signInManager)
	: IRequestHandler<LogoutRequest, Outcome>
{
	public async ValueTask<Outcome> Handle(LogoutRequest request, CancellationToken cancellationToken)
	{
		await signInManager.SignOutAsync();
		return Outcome.Ok();
	}
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test Himinbjorg/tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj`
Expected: PASS — 8 tests green.

- [ ] **Step 7: Implement the gRPC forwarder (no new test — pure delegation, covered by the handler tests above plus Task 7's end-to-end check)**

`Himinbjorg/src/Identity.Web.Server/AuthenticationService.cs`:
```csharp
using Norse.Abstractions.Mediator;
using Norse.AuthN.Components;
using ProtoBuf.Grpc;

namespace Norse.Identity.Web.Server;

/// <summary>
/// Thin forwarder — every method delegates to its matching <see cref="IRequestHandler{TRequest,TResponse}"/>.
/// No business logic lives here (<c>Heimdall/specs/2026-07-13-authn-identity-split-design.md</c> §0/§3).
/// </summary>
internal sealed class AuthenticationService(
	IRequestHandler<LoginRequest, Outcome<LoginResponse>> loginHandler,
	IRequestHandler<RegisterRequest, Outcome<RegisterResponse>> registerHandler,
	IRequestHandler<LogoutRequest, Outcome> logoutHandler)
	: IAuthenticationService
{
	public async Task<Outcome<LoginResponse>> Login(LoginRequest request, CallContext context = default) =>
		await loginHandler.Handle(request, context.CancellationToken);

	public async Task<Outcome<RegisterResponse>> Register(RegisterRequest request, CallContext context = default) =>
		await registerHandler.Handle(request, context.CancellationToken);

	public async Task<Outcome> Logout(LogoutRequest request, CallContext context = default) =>
		await logoutHandler.Handle(request, context.CancellationToken);
}
```

- [ ] **Step 8: Implement the DI/hosting wiring**

`Himinbjorg/src/Identity.Web.Server/ServiceCollectionExtensions.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Mediator;
using Norse.AuthN.Components;
using Norse.Identity;

namespace Norse.Identity.Web.Server;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddNorseAuthenticationService(this IServiceCollection services, string connectionString)
	{
		services.AddDbContext<NorseIdentityDbContext>(o => o.UseNpgsql(connectionString));
		services.AddNorseIdentity();
		services.AddHttpContextAccessor();
		services.AddCodeFirstGrpc();

		services.AddScoped<LoginRequestValidator>();
		services.AddScoped<RegisterRequestValidator>();

		services.AddScoped<IRequestHandler<LoginRequest, Outcome<LoginResponse>>, LoginHandler>();
		services.AddScoped<IRequestHandler<RegisterRequest, Outcome<RegisterResponse>>, RegisterHandler>();
		services.AddScoped<IRequestHandler<LogoutRequest, Outcome>, LogoutHandler>();

		services.AddScoped<IAuthenticationService, AuthenticationService>();

		return services;
	}
}
```

`Himinbjorg/src/Identity.Web.Server/WebApplicationExtensions.cs`:
```csharp
using Microsoft.AspNetCore.Builder;

namespace Norse.Identity.Web.Server;

public static class WebApplicationExtensions
{
	public static WebApplication MapNorseAuthenticationService(this WebApplication app)
	{
		app.MapGrpcService<AuthenticationService>();
		return app;
	}
}
```

- [ ] **Step 9: Wire the new projects into `Himinbjorg.slnx`**

Add under `/src/`:
```xml
		<Project Path="src/Identity.Web.Server/Identity.Web.Server.csproj" />
```
and under `/tests/`:
```xml
		<Project Path="tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj" />
```

- [ ] **Step 10: Commit**

```bash
cd Himinbjorg
git add src/Identity.Web.Server tests/Identity.Web.Server.Tests Himinbjorg.slnx src/Identity/Identity.csproj
git commit -m "feat: add Identity.Web.Server — LoginHandler/RegisterHandler/LogoutHandler and the gRPC forwarder"
cd ..
git add Bifrost.slnx
git commit -m "chore: bump Himinbjorg submodule pointer"
```

---

## SHIP GATE — Himinbjörg

**STOP. Do not start Task 4 until this gate is cleared.**

1. Push the Himinbjörg commit; open a PR against `master`; confirm CI is green.
2. Merge the PR; push a version tag; confirm `Norse.Identity.Web.Server` is live on the NuGet feed.
3. Push the Bifrost submodule-pointer commit.

---

## Task 4: Yggdrasil — `Hosting.Web.Server` (host the gRPC service)

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`
- Modify: `Yggdrasil/src/Hosting.Web.Server/Program.cs`

**Interfaces:**
- Consumes: `AddNorseAuthenticationService(this IServiceCollection, string)`, `MapNorseAuthenticationService(this WebApplication)` (Task 3, `Norse.Identity.Web.Server`).

- [ ] **Step 1: Add the `NorseRef`s and the reflection package**

gRPC Server Reflection lets a client (Postman, `grpcurl`) discover `IAuthenticationService`'s methods and message shapes without needing a hand-authored `.proto` — this is the direct tool for testing the wire lifecycle independent of the Blazor UI, per protobuf-net.Grpc.AspNetCore riding on the same underlying `Grpc.AspNetCore.Server` primitives the native reflection package hooks into.

In `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`, add to the existing `NorseRef` `ItemGroup`:
```xml
		<NorseRef Include="Identity.Web.Server">
			<Repo>Himinbjorg</Repo>
		</NorseRef>
		<NorseRef Include="AuthN.Components.FluentUI">
			<Repo>Heimdall</Repo>
		</NorseRef>
```
and add a new `ItemGroup`:
```xml
	<ItemGroup>
		<PackageReference Include="Grpc.AspNetCore.Server.Reflection" Version="*" />
	</ItemGroup>
```

- [ ] **Step 2: Wire `Program.cs`**

In `Yggdrasil/src/Hosting.Web.Server/Program.cs`, add `using Norse.Identity.Web.Server;` to the top, and register the new service (this coexists with the existing `ApplicationUser`/`PlaceholderUserStore` wiring — that scaffold is untouched until the Task-7-and-later cutover plan):
```csharp
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Norse.Hosting.Web.Components;
using Norse.Hosting.Web.Server.Components;
using Norse.Hosting.Web.Server.Components.Account;
using Norse.Hosting.Web.Server.Identity;
using Norse.Identity.Web.Server;
using Norse.Infrastructure.Components.Theme.FluentUI;
```

Immediately after the existing `builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();` line, add:
```csharp
var norseIdentityConnectionString = builder.Configuration.GetConnectionString("norse_identity")
	?? throw new InvalidOperationException("Connection string 'norse_identity' is not configured.");
builder.Services.AddNorseAuthenticationService(norseIdentityConnectionString);

// Dev-only: lets Postman/grpcurl discover IAuthenticationService and call it directly, proving the
// protobuf-net.Grpc wire lifecycle independent of the Blazor UI. Never mapped outside Development —
// reflection hands out the full service/message catalog to anyone who can reach the endpoint.
builder.Services.AddGrpcReflection();
```

After `app.MapAdditionalIdentityEndpoints();`, add:
```csharp
app.MapNorseAuthenticationService();

if (app.Environment.IsDevelopment())
{
	app.MapGrpcReflectionService();
}
```

- [ ] **Step 3: Manually verify the project still builds**

Run: `dotnet build Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`
Expected: builds clean (0 errors) — there is no automated test for Program.cs wiring; Task 8's end-to-end check (extended below) is the real verification.

- [ ] **Step 4: Commit**

```bash
cd Yggdrasil
git add src/Hosting.Web.Server/Hosting.Web.Server.csproj src/Hosting.Web.Server/Program.cs
git commit -m "feat: host Norse.Identity.Web.Server's gRPC endpoints (with dev-only reflection) in Hosting.Web.Server"
```

---

## Task 5: Yggdrasil — `Hosting.Web.Client` (gRPC-Web client wiring)

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Client/Hosting.Web.Client.csproj`
- Modify: `Yggdrasil/src/Hosting.Web.Client/Program.cs`
- Create: `Yggdrasil/src/Hosting.Web.Client/BrowserCredentialsHandler.cs`

**Interfaces:**
- Consumes: `IAuthenticationService` (Task 2, `Norse.AuthN.Components`).
- Produces: `IAuthenticationService` registered in the WASM container as a real gRPC-Web client proxy.

- [ ] **Step 1: Add package references and `NorseRef`s**

In `Yggdrasil/src/Hosting.Web.Client/Hosting.Web.Client.csproj`, add:
```xml
	<ItemGroup>
		<PackageReference Include="protobuf-net.Grpc" Version="*" />
		<PackageReference Include="Grpc.Net.Client.Web" Version="*" />
	</ItemGroup>
```
and add to the existing `NorseRef` `ItemGroup`:
```xml
		<NorseRef Include="AuthN.Components">
			<Repo>Heimdall</Repo>
		</NorseRef>
		<NorseRef Include="AuthN.Components.FluentUI">
			<Repo>Heimdall</Repo>
		</NorseRef>
```

- [ ] **Step 2: Implement the browser-credentials handler**

WASM's `fetch`-backed `HttpClient` does not send cookies cross-request by default; the gRPC-Web request needs `credentials: 'include'` set explicitly per request.

`Yggdrasil/src/Hosting.Web.Client/BrowserCredentialsHandler.cs`:
```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Norse.Hosting.Web.Client;

/// <summary>
/// Sets <c>credentials: 'include'</c> on every outgoing gRPC-Web request so the browser sends and
/// stores the cookie <c>IAuthenticationService.Login</c>/<c>.Logout</c> mint/clear server-side.
/// </summary>
internal sealed class BrowserCredentialsHandler : DelegatingHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
		return base.SendAsync(request, cancellationToken);
	}
}
```

- [ ] **Step 3: Wire the client proxy in `Program.cs`**

In `Yggdrasil/src/Hosting.Web.Client/Program.cs`, add:
```csharp
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Norse.AuthN.Components;
using ProtoBuf.Grpc.Client;
```

After the existing `builder.Services.AddSingleton(new RoutesAdditionalAssemblies([]));` line, add:
```csharp
// gRPC-Web rides ordinary HTTP/1.1 — no HTTP/2-specific channel configuration needed in the browser.
var authNChannel = GrpcChannel.ForAddress(builder.HostEnvironment.BaseAddress, new GrpcChannelOptions
{
	HttpHandler = new GrpcWebHandler(new BrowserCredentialsHandler { InnerHandler = new HttpClientHandler() }),
});
builder.Services.AddSingleton(authNChannel.CreateGrpcService<IAuthenticationService>());
```

- [ ] **Step 4: Manually verify the project still builds**

Run: `dotnet build Yggdrasil/src/Hosting.Web.Client/Hosting.Web.Client.csproj`
Expected: builds clean (0 errors). The actual cookie round-trip through the browser can only be confirmed by running the app (Task 7).

- [ ] **Step 5: Commit**

```bash
git add src/Hosting.Web.Client/Hosting.Web.Client.csproj src/Hosting.Web.Client/Program.cs src/Hosting.Web.Client/BrowserCredentialsHandler.cs
git commit -m "feat: wire a gRPC-Web IAuthenticationService client proxy in Hosting.Web.Client"
cd ..
git add Bifrost.slnx
```

---

## SHIP GATE — Yggdrasil (backend + client wiring)

**STOP. Do not start Task 6 until this gate is cleared.**

1. Push the Yggdrasil commits (Tasks 4 and 5); open a PR against `master`; confirm CI is green.
2. Merge the PR. `Hosting.Web.Server`/`Hosting.Web.Client` are deployables, not NuGet-published libraries — no version tag/publish step applies here, unlike the library-producing realms above.
3. Push the Bifrost submodule-pointer commit.

---

## Task 6: Bifrost — wire `Hosting.Web.Server` into the Aspire AppHost

**Files:**
- Modify: `src/Orchestration.AppHost/AppHost.cs`

**Interfaces:**
- Consumes: the already-provisioned `norseIdentity` Postgres database resource, the already-wired `migrationsService` resource (both already exist in `AppHost.cs`).

- [ ] **Step 1: Add the project to the composition**

`Hosting.Web.Server` is already `ProjectReference`d by `Orchestration.AppHost.csproj` but was never added to `builder` in `AppHost.cs`. Add, after the existing `migrationsService` block and before the final `await builder.Build().RunAsync()...` line:
```csharp
builder
	.AddProject<Projects.Hosting_Web_Server>("web")
	.WithReference(norseIdentity, connectionName: "norse_identity")
	.WaitFor(norseIdentity)
	.WaitForCompletion(migrationsService);
```

`WaitForCompletion` (not `WaitFor`) — the web server must not start until the migrations service has exited 0, matching Bifrost's own "migrations run to completion before Yggdrasil is permitted to start anything else" rule (`Bifrost/CLAUDE.md` §1).

- [ ] **Step 2: Manually verify the AppHost builds and the dashboard shows the new resource**

Run: `dotnet run --project src/Orchestration.AppHost`
Expected: the Aspire dashboard shows `web` alongside `pg-primary`, `pg-replica`, and `migrations`; `web` starts only after `migrations` exits 0.

- [ ] **Step 3: Commit**

```bash
git add src/Orchestration.AppHost/AppHost.cs
git commit -m "feat: wire Hosting.Web.Server into the Aspire composition against norse_identity"
```

---

## Task 7: Heimdall — `AuthN.Components.FluentUI` (the Razor components)

**Files:**
- Create: `Heimdall/src/AuthN.Components.FluentUI/AuthN.Components.FluentUI.csproj`
- Create: `Heimdall/src/AuthN.Components.FluentUI/_Imports.razor`
- Create: `Heimdall/src/AuthN.Components.FluentUI/Login.razor`
- Create: `Heimdall/src/AuthN.Components.FluentUI/Register.razor`
- Create: `Heimdall/src/AuthN.Components.FluentUI/Logout.razor`
- Modify: `Bifrost.slnx`

**Interfaces:**
- Consumes: `IAuthenticationService`, `LoginRequest`, `LoginResponse`, `LoginStatus`, `RegisterRequest`, `LogoutRequest` (Task 2); `ErrorCategory` (Task 1).

No new automated tests in this task — Razor component behavior is verified by the manual end-to-end check in Task 8; this task is TDD-exempt only in the narrow sense that bUnit component tests are deferred to the `IAccountService` follow-on plan once the pattern's proven here, consistent with keeping this bootstrap slim.

- [ ] **Step 1: Create the project file**

`Heimdall/src/AuthN.Components.FluentUI/AuthN.Components.FluentUI.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<PropertyGroup>
		<Description>Norse.AuthN.Components.FluentUI: Login/Register/Logout Razor components, FluentUI Blazor v5, wired against AuthN.Components' gRPC contract.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.FluentUI.AspNetCore.Components" Version="5.*" />
		<PackageReference Include="Blazored.FluentValidation" Version="*" />
	</ItemGroup>
	<ItemGroup>
		<ProjectReference Include="../AuthN.Components/AuthN.Components.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: `_Imports.razor`**

`Heimdall/src/AuthN.Components.FluentUI/_Imports.razor`:
```razor
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.FluentUI.AspNetCore.Components
@using Blazored.FluentValidation
@using Norse.Abstractions.Mediator
@using Norse.AuthN.Components
```

- [ ] **Step 3: `Login.razor`**

`Heimdall/src/AuthN.Components.FluentUI/Login.razor`:
```razor
@page "/authn/login"
@rendermode InteractiveAuto
@inject IAuthenticationService AuthenticationService
@inject NavigationManager Navigation

<PageTitle>Log in</PageTitle>

<h1>Log in</h1>

<EditForm Model="_request" OnValidSubmit="HandleLoginAsync" FormName="authn-login">
	<FluentValidationValidator />
	<FluentTextField @bind-Value="_request.Email" Label="Email" Required="true" />
	<FluentTextField @bind-Value="_request.Password" TextFieldType="TextFieldType.Password" Label="Password" Required="true" />
	<FluentCheckbox @bind-Value="_request.RememberMe" Label="Remember me" />
	<FluentButton Type="ButtonType.Submit" Appearance="Appearance.Accent">Log in</FluentButton>
</EditForm>

@if (_errorMessage is not null)
{
	<FluentMessageBar Intent="MessageIntent.Error">@_errorMessage</FluentMessageBar>
}

@code {
	private LoginRequest _request = new() { Email = "", Password = "" };
	private string? _errorMessage;

	private async Task HandleLoginAsync()
	{
		_errorMessage = null;
		var outcome = await AuthenticationService.Login(_request);

		if (!outcome.IsSuccess)
		{
			_errorMessage = outcome.Problem!.Category switch
			{
				ErrorCategory.LockedOut => "This account is locked out. Try again later.",
				ErrorCategory.InvalidCredentials => "Invalid email or password.",
				ErrorCategory.NotAllowed => "Sign-in is not allowed for this account.",
				_ => "Something went wrong. Please try again.",
			};
			return;
		}

		if (outcome.Value!.Status == LoginStatus.RequiresTwoFactor)
		{
			_errorMessage = "Two-factor authentication is required but not yet supported here.";
			return;
		}

		Navigation.NavigateTo("/", forceLoad: true);
	}
}
```

- [ ] **Step 4: `Register.razor`**

`Heimdall/src/AuthN.Components.FluentUI/Register.razor`:
```razor
@page "/authn/register"
@rendermode InteractiveAuto
@inject IAuthenticationService AuthenticationService
@inject NavigationManager Navigation

<PageTitle>Register</PageTitle>

<h1>Register</h1>

<EditForm Model="_request" OnValidSubmit="HandleRegisterAsync" FormName="authn-register">
	<FluentValidationValidator />
	<FluentTextField @bind-Value="_request.Email" Label="Email" Required="true" />
	<FluentTextField @bind-Value="_request.Password" TextFieldType="TextFieldType.Password" Label="Password" Required="true" />
	<FluentButton Type="ButtonType.Submit" Appearance="Appearance.Accent">Register</FluentButton>
</EditForm>

@if (_errorMessage is not null)
{
	<FluentMessageBar Intent="MessageIntent.Error">@_errorMessage</FluentMessageBar>
}

@code {
	private RegisterRequest _request = new() { Email = "", Password = "" };
	private string? _errorMessage;

	private async Task HandleRegisterAsync()
	{
		_errorMessage = null;
		var outcome = await AuthenticationService.Register(_request);

		if (!outcome.IsSuccess)
		{
			_errorMessage = outcome.Problem!.Category switch
			{
				ErrorCategory.Conflict => "That email address is already registered.",
				_ => "Something went wrong. Please try again.",
			};
			return;
		}

		Navigation.NavigateTo("/authn/login");
	}
}
```

- [ ] **Step 5: `Logout.razor`**

`Heimdall/src/AuthN.Components.FluentUI/Logout.razor`:
```razor
@page "/authn/logout"
@rendermode InteractiveAuto
@inject IAuthenticationService AuthenticationService
@inject NavigationManager Navigation

@code {
	protected override async Task OnInitializedAsync()
	{
		await AuthenticationService.Logout(new LogoutRequest());
		Navigation.NavigateTo("/", forceLoad: true);
	}
}
```

- [ ] **Step 6: Manually verify the project builds**

Run: `dotnet build Heimdall/src/AuthN.Components.FluentUI/AuthN.Components.FluentUI.csproj`
Expected: builds clean (0 errors).

- [ ] **Step 7: Wire into `Heimdall.slnx` and `Bifrost.slnx`**

In `Heimdall/Heimdall.slnx`, add under `/src/`:
```xml
		<Project Path="src/AuthN.Components.FluentUI/AuthN.Components.FluentUI.csproj" />
```

In `Bifrost.slnx`, add to the `/AuthN/src/` folder created in Task 2:
```xml
		<Project Path="Heimdall/src/AuthN.Components.FluentUI/AuthN.Components.FluentUI.csproj" />
```

- [ ] **Step 8: Commit**

```bash
cd Heimdall
git add src/AuthN.Components.FluentUI Heimdall.slnx
git commit -m "feat: add AuthN.Components.FluentUI — Login/Register/Logout Razor components"
cd ..
git add Bifrost.slnx
git commit -m "chore: wire Heimdall's AuthN.Components.FluentUI into Bifrost.slnx"
```

---

## SHIP GATE — Heimdall (`AuthN.Components.FluentUI`)

**STOP. Do not start Task 8 until this gate is cleared.**

1. Push the Heimdall commit; open a PR against `master`; confirm CI is green.
2. Merge the PR; push a version tag; confirm `Norse.AuthN.Components.FluentUI` is live on the NuGet feed.
3. Push the Bifrost commit.

---

## Task 8: End-to-end manual verification

**Files:** none — this task runs the composed system and exercises it by hand. No shortcuts: this is the step that actually proves the bootstrap, not a formality.

- [ ] **Step 1: Start the composed system**

Run: `dotnet run --project src/Orchestration.AppHost`
Expected: Aspire dashboard shows `pg-primary`, `pg-replica`, `migrations` (exits 0), then `web` starts.

- [ ] **Step 2: Verify Blazor Server registration + login (in-process path)**

Open the `web` resource's browser endpoint before any client-side interactivity has taken over (first paint). Navigate to `/authn/register`, submit a new account. Expected: redirected to `/authn/login`. Log in with the same credentials. Expected: redirected to `/`, and the browser's dev tools show a `Set-Cookie` for the ASP.NET Core Identity application cookie.

- [ ] **Step 3: Verify the WASM path takes over and a second login round-trips over gRPC-Web**

With the same browser tab still open (WASM has now hydrated per `InteractiveAuto`), log out via `/authn/logout`, then log back in at `/authn/login` again. Expected: same successful redirect; in the browser's Network tab, confirm the `Login` call is now a `POST` to a gRPC-Web content-type endpoint (`application/grpc-web` or `application/grpc-web-text`) rather than a normal form post, and that the response carries a `Set-Cookie` header the browser accepted (confirmed by the subsequent authenticated navigation succeeding).

- [ ] **Step 4: Verify a locked-out / invalid-credentials path surfaces correctly**

At `/authn/login`, submit the wrong password five times for the account created in Step 2. Expected: the fifth attempt (or the first attempt past Identity's configured lockout threshold) shows the "This account is locked out" message from `Login.razor`'s `ErrorCategory.LockedOut` branch, not an unhandled exception.

- [ ] **Step 5: Verify the raw protobuf lifecycle in Postman via gRPC reflection**

This exercises `IAuthenticationService` directly over the wire — no Blazor UI, no cookie jar, nothing but the gRPC contract itself. It's the cleanest proof that `Identity.Web.Server`'s forwarder and handlers are correct independent of any client concern.

1. In Postman, create a new gRPC request against the `web` resource's endpoint (check the Aspire dashboard for the exact `https://localhost:{port}` address).
2. Use "Server reflection" as the import method (not "Import a .proto file") — Postman calls the reflection service Task 4 wired and lists `Norse.AuthN.Components.IAuthenticationService` with its three methods.
3. Call `Register` with a JSON body `{ "email": "postman-test@example.com", "password": "correct-horse-battery" }`. Expected: a response shaped like `{ "isSuccess": true, "value": { "userId": "<a real guid>" } }`.
4. Call `Register` again with the **same** body. Expected: `{ "isSuccess": false, "problem": { "category": 3, "errors": { ... } } }` — `3` is `ErrorCategory.Conflict` (§1 of Task 1's `ErrorCategory` enum), proving `RegisterHandler`'s duplicate-email path is reachable over the real wire, not just in the unit test from Task 3.
5. Call `Login` with the same credentials. Expected: `{ "isSuccess": true, "value": { "status": 1 } }` — `1` is `LoginStatus.Succeeded`.
6. Call `Login` with the wrong password five times in a row. Expected: the response's `problem.category` becomes `4` (`ErrorCategory.LockedOut`) once Identity's lockout threshold is hit — the same behavior confirmed through the browser in Step 4, now confirmed at the protocol level with nothing else in the way.
7. Call `Logout` with an empty body (`{}`). Expected: `{ "isSuccess": true }`. Note that a bare Postman gRPC call carries no cookie jar by default — this call proves the RPC completes cleanly, not that it cleared a specific browser session; Steps 2–4 above are what prove the cookie side of the lifecycle.

If reflection doesn't list the service, don't guess — confirm `AddGrpcReflection()`/`MapGrpcReflectionService()` actually ran (`app.Environment.IsDevelopment()` must be true) before assuming Postman or the contract is at fault.

- [ ] **Step 6: Record the result**

If every check in Steps 2–5 passes, this bootstrap slice is proven end to end. If anything fails, treat it as a bug against the specific task above (per `superpowers:systematic-debugging`) — do not patch around it inline without understanding which task's assumption broke.

---

## What's Deliberately Deferred (not part of this plan)

- `IAccountService`'s full lifecycle surface (`ChangePassword` through `DeletePersonalData`) — `Heimdall/specs/2026-07-13-authn-identity-split-design.md` §7, step 3.
- Yggdrasil's old `Components/Account/*` scaffold — untouched, still serves its own routes; the cutover is its own future plan (design doc §7, step 5).
- The mediator's generic `ISender`/pipeline-behavior/source-generator machinery (`2026-05-26-mediator-design.md`) — this plan hand-wires each handler directly into the gRPC forwarder's constructor instead. Building the generic dispatcher is real future work once this pattern's proven across more than three operations.
- OpenIddict's authorization-server endpoints for MAUI's Auth Code + PKCE flow — separate future spec, per the design doc §6.
