# Heimdall/Himinbjörg AuthN Bootstrap Slice — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (default) or `superpowers:executing-plans` (separate-session fallback, never interchangeable). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove the whole pipe — gRPC contract, Mediator handler pattern, real Himinbjörg persistence, gRPC client wiring in both Blazor Server and WASM, and Razor UI — end to end, using only the three issuance operations (`Login`, `Register`, `Logout`). Nothing else is in scope; the full `IAccountService` lifecycle surface is deliberately deferred to a follow-on plan once this pipe is proven.

**Architecture:** Seven realms, in strict dependency order. Asgard gets a minimal hand-written mediator core (`Outcome`/`Outcome<T>`, `ICommandRequest<T>`, `IRequestHandler<T,T>`) — no source generator yet, that's future work once this pattern proves out. Heimdall's `AuthN.Components` declares `IAuthenticationService` as a protobuf-net.Grpc code-first contract (per the platform-wide reinstatement, `Platform/specs/2026-07-13-protobuf-net-grpc-reinstated-design.md`). Himinbjörg's new `Identity.Web.Server` implements it as a thin forwarder over three `IRequestHandler<,>` implementations that call directly into `UserManager<NorseUser>`/`SignInManager<NorseUser>`. Yggdrasil wires the server side (gRPC hosting + the real `norse_identity` `DbContext`, for the first time) and the client side (a protobuf-net.Grpc client proxy over gRPC-Web, for the first time). Bifrost's AppHost gets `Hosting.Web.Server` wired into the Aspire composition for the first time, referencing the already-provisioned `norse-identity` Postgres database. Heimdall's `AuthN.Components.FluentUI` supplies the three Razor components (FluentUI Blazor v5), rendered `@rendermode InteractiveAuto` so both the Blazor Server and WASM paths are genuinely exercised. Bragi picks up a matching story for each of those three components, per the platform-wide rule that every Razor component drop gets a paired Bragi story in the same slice (`[[feedback_every-component-needs-a-bragi-story]]`).

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
- `LoginRequest`/`RegisterRequest` deliberately use mutable (`get; set;`) `[DataMember]` properties, not `init` — they are direct `EditForm` binding targets in Task 7, and introducing a parallel mutable form-model type purely to preserve `init`-only wire records would duplicate the validator for no benefit at this scale. Every other record in this plan (`LoginResponse`, `RegisterResponse`, `LogoutRequest`, `Outcome`, `Outcome<T>`, `Problem`) stays `init`-only as usual.
- **Every Razor component this plan ships gets a matching Bragi story in the same slice** — a platform-wide rule as of 2026-07-13 (`[[feedback_every-component-needs-a-bragi-story]]`), not specific to AuthN. Two decisions travel together whenever a `.razor` file is created anywhere on the platform: (1) headless-vs-skinned first, per `Platform/specs/2026-07-11-blazor-component-architecture-design.md` Decision 1 — does the markup reference a specific design-system package, or does it stay in `.Components` unstyled; (2) regardless of that answer, a `.stories.razor` catalog page lands in Bragi (`Norse.DesignSystem.Stories`) content-only, no exceptions. See Task 9 below, added specifically to apply this to `Login.razor`/`Register.razor`/`Logout.razor`. Bragi ships no runtime of its own — the story *content* lives there; the app that renders it (`Hosting.Stories.Client`/`Hosting.Stories.Server`) is Yggdrasil's.

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

**Amendment (2026-07-25):** `Himinbjorg/src/Identity/Identity.csproj` no longer exists — deleted 2026-07-23, folded into `src/Identity.Web.Server`. Current shape is four live projects: `Identity.Web.Server`, `Identity.Migrations`, `Identity.Migrations.PostgreSQL`, `Identity.Migrations.SqlServer`.

### Yggdrasil
| Action | Path |
|---|---|
| Modify | `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj` |
| Modify | `Yggdrasil/src/Hosting.Web.Server/Program.cs` |
| Modify | `Yggdrasil/src/Hosting.Web.Client/Hosting.Web.Client.csproj` |
| Modify | `Yggdrasil/src/Hosting.Web.Client/Program.cs` |
| Create | `Yggdrasil/src/Hosting.Web.Client/BrowserCredentialsHandler.cs` |
| Create | `Yggdrasil/src/Hosting.Stories.Client/FakeAuthenticationService.cs` |
| Modify | `Yggdrasil/src/Hosting.Stories.Client/Program.cs` |

### Bifrost
| Action | Path |
|---|---|
| Modify | `src/Orchestration.AppHost/AppHost.cs` |

### Bragi
| Action | Path |
|---|---|
| Modify | `Bragi/src/DesignSystem.Stories/DesignSystem.Stories.csproj` |
| Create | `Bragi/src/DesignSystem.Stories/Authentication/Login.stories.razor` |
| Create | `Bragi/src/DesignSystem.Stories/Authentication/Register.stories.razor` |
| Create | `Bragi/src/DesignSystem.Stories/Authentication/Logout.stories.razor` |

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

**CLEARED 2026-07-14.** `Norse.Abstractions.Web.Server` v0.0.4 live on GitHub Packages (https://github.com/NorseArchitecture/Asgard/releases/tag/v0.0.4, PR #25) — the mediator vocabulary shipped inside the existing `Abstractions.Web.Server` project, not a standalone `Norse.Abstractions.Mediator` package (that project was deleted mid-session; see `Glitnir/docs/Heimdall/specs/2026-07-13-authn-identity-split-design.md` §9.1 for why).

**CLEARED 2026-07-14 05:27 UTC.** `Norse.Abstractions.Web.Server` v0.0.5 live on GitHub Packages (PR #26) — `IRequestHandler<TRequest,TResponse>`'s `where TRequest : ICommandRequest<TResponse>` constraint dropped (see spec §9.1) and `Abstractions.Web.Server/Mediator/BoolResponse.cs` added (spec §9.4). Note: an intermediate draft of this v0.0.5 also added `MediatorFailureException`/`OutcomeExtensions.ThrowIfFailed()` directly to Asgard — that was reverted before shipping, per spec §9.1's "second follow-up... explicitly reverted" note. The `ThrowIfFailed()`/interceptor pair lives in Midgard instead (Task 3), not Asgard.

Bonus verification: Yggdrasil's CPM auto-bump fan-in fired for real too — `Directory.Packages.props`' `AsgardVersion` bumped to `0.0.5` via auto-merged PR #49, confirming the release-ceremony automation still works end to end.

---

## Task 2: Heimdall — `AuthN.Components` (the contract)

Rewritten in full 2026-07-14 to match spec addendum `Glitnir/docs/Heimdall/specs/2026-07-13-authn-identity-split-design.md` §9.2/§9.3/§9.6 — the pre-addendum version (`Outcome<T>` on the wire, `LoginRequest : ICommandRequest<T>`, `CallContext`, a `protobuf-net.Grpc` package reference) is gone, not just superseded-in-place. This project takes **no `NorseRef` at all** — no cross-realm dependency, plain DataContract DTOs + FluentValidation only.

**Files:**
- Create: `Heimdall/Heimdall.slnx`
- Create: `Heimdall/src/AuthN.Components/AuthN.Components.csproj`
- Create: `Heimdall/src/AuthN.Components/IAuthenticationService.cs`
- Create: `Heimdall/src/AuthN.Components/LoginRequest.cs`
- Create: `Heimdall/src/AuthN.Components/LoginResult.cs`
- Create: `Heimdall/src/AuthN.Components/RegisterRequest.cs`
- Create: `Heimdall/src/AuthN.Components/LogoutRequest.cs`
- Create: `Heimdall/src/AuthN.Components/AuthenticationResult.cs`
- Create: `Heimdall/src/AuthN.Components/LoginRequestValidator.cs`
- Create: `Heimdall/src/AuthN.Components/RegisterRequestValidator.cs`
- Test: `Heimdall/tests/AuthN.Components.Tests/LoginRequestValidatorTests.cs`, `RegisterRequestValidatorTests.cs`
- Modify: `Bifrost.slnx`

**Interfaces:**
- Consumes: nothing cross-realm.
- Produces:
  - `Norse.AuthN.Components.IAuthenticationService` — `[ServiceContract]`; `Login(LoginRequest) -> Task<LoginResult>`, `Register(RegisterRequest) -> Task`, `Logout(LogoutRequest) -> Task`. No `CallContext` parameter.
  - `Norse.AuthN.Components.LoginRequest` — plain `[DataContract]`; `string Email { get; set; }`, `string Password { get; set; }`, `bool RememberMe { get; set; }`.
  - `Norse.AuthN.Components.LoginResult` — `bool Succeeded { get; init; }`.
  - `Norse.AuthN.Components.RegisterRequest` — plain `[DataContract]`; `string Email { get; set; }`, `string Password { get; set; }`.
  - `Norse.AuthN.Components.LogoutRequest` — plain `[DataContract]`; empty.
  - `Norse.AuthN.Components.AuthenticationResult` — `bool Succeeded { get; init; }`, `IReadOnlyDictionary<string,string[]> Errors { get; init; }`.
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
		<Description>Norse.AuthN.Components: the IAuthenticationService gRPC contract, its request/response records, and FluentValidation validators. No implementation. Deliberately references only System.ServiceModel.Primitives (attributes) rather than the full protobuf-net.Grpc package, and takes no NorseRef on Asgard's server-only mediator — Outcome&lt;T&gt; never appears on this wire (see the Heimdall spec §9.1/§9.2). This keeps the widely-shared contract library WASM-thin.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="System.ServiceModel.Primitives" Version="*" />
		<PackageReference Include="FluentValidation" Version="*" />
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

public sealed class LoginRequestValidatorTests
{
	private readonly LoginRequestValidator _validator = new();

	[Fact]
	void Rejects_empty_email()
	{
		var request = new LoginRequest { Email = "", Password = "correct-horse" };

		var result = _validator.Validate(request);

		result.IsValid.ShouldBeFalse();
	}

	[Fact]
	void Rejects_malformed_email()
	{
		var request = new LoginRequest { Email = "not-an-email", Password = "correct-horse" };

		var result = _validator.Validate(request);

		result.IsValid.ShouldBeFalse();
	}

	[Fact]
	void Rejects_empty_password()
	{
		var request = new LoginRequest { Email = "user@example.com", Password = "" };

		var result = _validator.Validate(request);

		result.IsValid.ShouldBeFalse();
	}

	[Fact]
	void Accepts_a_well_formed_request()
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

public sealed class RegisterRequestValidatorTests
{
	private readonly RegisterRequestValidator _validator = new();

	[Fact]
	void Rejects_malformed_email()
	{
		var request = new RegisterRequest { Email = "not-an-email", Password = "correct-horse-battery" };

		var result = _validator.Validate(request);

		result.IsValid.ShouldBeFalse();
	}

	[Fact]
	void Rejects_password_shorter_than_eight_characters()
	{
		var request = new RegisterRequest { Email = "user@example.com", Password = "short" };

		var result = _validator.Validate(request);

		result.IsValid.ShouldBeFalse();
	}

	[Fact]
	void Accepts_a_well_formed_request()
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

namespace Norse.AuthN.Components;

/// <summary>
/// Deliberately mutable (not <c>init</c>) — this is the direct two-way <c>EditForm</c> binding target
/// for <c>AuthN.Components.FluentUI</c>'s <c>Login.razor</c>; every other record in this contract stays
/// <c>init</c>-only. Plain wire DTO — no mediator-law coupling of any kind (spec §9.1/§9.2).
/// </summary>
[DataContract]
public sealed record LoginRequest
{
	[DataMember(Order = 1)]
	public required string Email { get; set; }

	[DataMember(Order = 2)]
	public required string Password { get; set; }

	[DataMember(Order = 3)]
	public bool RememberMe { get; set; }
}
```

`Heimdall/src/AuthN.Components/LoginResult.cs`:
```csharp
using System.Runtime.Serialization;

namespace Norse.AuthN.Components;

/// <summary>
/// The wire response for <see cref="IAuthenticationService.Login"/>. <c>Succeeded=false</c> is a
/// legitimate successful credential check (wrong username or password), not a failure — the two are
/// deliberately never distinguished, see spec §9.3/§9.4.
/// </summary>
[DataContract]
public sealed record LoginResult
{
	[DataMember(Order = 1)]
	public required bool Succeeded { get; init; }
}
```

`Heimdall/src/AuthN.Components/RegisterRequest.cs`:
```csharp
using System.Runtime.Serialization;

namespace Norse.AuthN.Components;

/// <summary>Deliberately mutable — see <see cref="LoginRequest"/>'s remark.</summary>
[DataContract]
public sealed record RegisterRequest
{
	[DataMember(Order = 1)]
	public required string Email { get; set; }

	[DataMember(Order = 2)]
	public required string Password { get; set; }
}
```

`Heimdall/src/AuthN.Components/LogoutRequest.cs`:
```csharp
using System.Runtime.Serialization;

namespace Norse.AuthN.Components;

/// <summary>
/// Deliberately empty — the caller's authenticated cookie identifies who's logging out. A wire type
/// still exists per operation because protobuf-net.Grpc requires one.
/// </summary>
[DataContract]
public sealed record LogoutRequest;
```

`Heimdall/src/AuthN.Components/AuthenticationResult.cs`:
```csharp
using System.Runtime.Serialization;

namespace Norse.AuthN.Components;

/// <summary>
/// The only thing any Razor component reads — never <see cref="IAuthenticationService"/> directly,
/// never a caught exception. Produced by a host-specific gateway (Blazor Server or WASM), per spec
/// §9.6. <c>Errors</c> convention: field name key -&gt; field-level messages; empty string key ("") -&gt;
/// general/model-level messages (e.g. account locked out) — matches FluentValidation/Blazor's own
/// convention for a message not tied to a specific property, so both flow into the same
/// ValidationSummary/ValidationMessageStore with no special-casing in the UI.
/// </summary>
[DataContract]
public sealed record AuthenticationResult
{
	[DataMember(Order = 1)]
	public required bool Succeeded { get; init; }

	[DataMember(Order = 2)]
	public IReadOnlyDictionary<string, string[]> Errors { get; init; } = new Dictionary<string, string[]>();
}
```

**Amendment (2026-07-25):** `AuthenticationResult` was retired 2026-07-25 by Heimdall's `feature/transport-neutral-gateway` slice (merged, tag v0.0.3). `IAuthenticationService` now carries Asgard's `[GenerateGateway]` attribute and Asgard's `GatewayGenerator` emits the generated `IAuthenticationGateway` directly off it; Login/Register/Logout components consume `ValueTask<Outcome<T>>` straight from the generated gateway. No hand-written result-wrapper type exists anywhere on this path anymore.

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
using System.ServiceModel;

namespace Norse.AuthN.Components;

/// <summary>
/// Issuance surface — real, network-callable gRPC methods that mint or clear the authenticated
/// cookie. No <c>CallContext</c> parameter, deliberately — see spec §9.2 for why this contract stays
/// off the full protobuf-net.Grpc package. Where the implementation needs a cancellation token or
/// <c>HttpContext</c>, it comes from a directly-injected <c>IHttpContextAccessor</c> instead.
/// </summary>
[ServiceContract]
public interface IAuthenticationService
{
	[OperationContract]
	Task<LoginResult> Login(LoginRequest request);

	[OperationContract]
	Task Register(RegisterRequest request);

	[OperationContract]
	Task Logout(LogoutRequest request);
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

**Do NOT `cd` into Bifrost's own root or run any `git checkout`/`git branch` command scoped there — edit `Bifrost.slnx` in place and stage only that one file from the Bifrost repo. Bifrost itself never gets a feature branch for a pointer/solution-wiring change (see `[[feedback_bifrost-stays-on-master]]`); it stays on `master`.**

- [ ] **Step 10: Stage (session policy: stage only, never commit — the human commits everything, on `master` for Bifrost, on the task branch for Heimdall)**

```bash
cd Heimdall
git add Heimdall.slnx src/AuthN.Components tests/AuthN.Components.Tests
cd ..
git add Bifrost.slnx
```

---

## SHIP GATE — Heimdall (`AuthN.Components`)

**STOP. Do not start Task 3 until this gate is cleared.**

1. Push the Heimdall commit; open a PR against `master`; confirm CI is green.
2. Merge the PR; push a version tag; confirm `Norse.AuthN.Components` is live on the NuGet feed.
3. Push the Bifrost commit.

---

## Task 3: Midgard — channel adapters (`Infrastructure.Web.Server`, `Infrastructure.Web.Client`)

**Added this session, not in the original bootstrap slice — see spec addendum `Glitnir/docs/Heimdall/specs/2026-07-13-authn-identity-split-design.md` §9.5/§9.6 for the full design and rationale, read it before implementing.** Two new Midgard projects, both realm-first (Midgard's first `Mediator`-anything). Neither is auth-specific — they translate `Outcome<T>` into and out of gRPC's failure idiom generically, so Himinbjörg's forwarder (Task 4) never branches on failure at all, and neither does any future gRPC-hosted mediator handler (Mímir's, when it exists).

**Files:**
| Action | Path |
|---|---|
| Create | `Midgard/src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj` |
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/OutcomeFailedException.cs` |
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/OutcomeExtensions.cs` |
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/OutcomeServerInterceptor.cs` |
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ProblemExtensions.cs` |
| Create | `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/OutcomeExtensionsTests.cs` |
| Create | `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/ProblemExtensionsTests.cs` |
| Create | `Midgard/src/Infrastructure.Web.Client/Infrastructure.Web.Client.csproj` |
| Create | `Midgard/src/Infrastructure.Web.Client/Grpc/RpcExceptionExtensions.cs` |
| Create | `Midgard/tests/Infrastructure.Web.Client.Tests/Grpc/RpcExceptionExtensionsTests.cs` |
| Modify | `Midgard/Midgard.slnx` |
| Modify | `Bifrost.slnx` |

**Interfaces:**
- Consumes: `Outcome`, `Outcome<T>`, `Problem`, `ErrorCategory` (Task 1, `Norse.Abstractions.Web.Server`, live at v0.0.5).
- Produces:
  - `Norse.Infrastructure.Web.Server.Mediator.Grpc.OutcomeFailedException` — thrown only by `ThrowIfFailed`, caught only by `OutcomeServerInterceptor`.
  - `Norse.Infrastructure.Web.Server.Mediator.Grpc.OutcomeExtensions.ThrowIfFailed<T>(this Outcome<T>)` / `ThrowIfFailed(this Outcome)`.
  - `Norse.Infrastructure.Web.Server.Mediator.Grpc.OutcomeServerInterceptor : Interceptor` — catches `OutcomeFailedException`, throws the `RpcException` `ProblemExtensions.ToRpcException` produces.
  - `Norse.Infrastructure.Web.Server.Mediator.Grpc.ProblemExtensions.ToRpcException(this Problem)`.
  - `Norse.Infrastructure.Web.Client.Grpc.RpcExceptionExtensions.DecodeProblem(this RpcException) : IReadOnlyDictionary<string,string[]>`.

- [ ] **Step 1: Create the project files**

`Midgard/src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Infrastructure.Web.Server: the embodied half of Asgard's server-only mediator law. Mediator/Grpc translates Outcome&lt;T&gt; into gRPC's native failure idiom (RpcException + trailers) and back out of a thrown OutcomeFailedException — zero domain knowledge, reused verbatim by every gRPC-hosted mediator handler on the platform.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="protobuf-net.Grpc.AspNetCore" Version="*" />
	</ItemGroup>
	<ItemGroup>
		<NorseRef Include="Abstractions.Web.Server">
			<Repo>Asgard</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

`Midgard/src/Infrastructure.Web.Client/Infrastructure.Web.Client.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Infrastructure.Web.Client: WASM-friendly gRPC client-side failure decoding. Grpc/ decodes an RpcException's problem-bin trailer directly into a plain dictionary — never references Asgard's server-only Outcome/Problem/ErrorCategory, because this project is meant to compile into a WASM client bundle.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="protobuf-net.Grpc" Version="*" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing tests**

`Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/OutcomeExtensionsTests.cs`:
```csharp
using Norse.Abstractions.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class OutcomeExtensionsTests
{
	[Fact]
	void ThrowIfFailed_returns_the_value_on_success()
	{
		var outcome = Outcome<bool>.Ok(true);

		outcome.ThrowIfFailed().ShouldBeTrue();
	}

	[Fact]
	void ThrowIfFailed_throws_OutcomeFailedException_carrying_the_Problem_on_failure()
	{
		var outcome = Outcome<bool>.Err(ErrorCategory.LockedOut);

		var exception = Should.Throw<OutcomeFailedException>(() => outcome.ThrowIfFailed());

		exception.Problem.Category.ShouldBe(ErrorCategory.LockedOut);
	}

	[Fact]
	void Non_generic_ThrowIfFailed_does_not_throw_on_success()
	{
		Should.NotThrow(() => Outcome.Ok().ThrowIfFailed());
	}

	[Fact]
	void Non_generic_ThrowIfFailed_throws_OutcomeFailedException_on_failure()
	{
		var outcome = Outcome.Err(ErrorCategory.Conflict);

		var exception = Should.Throw<OutcomeFailedException>(() => outcome.ThrowIfFailed());

		exception.Problem.Category.ShouldBe(ErrorCategory.Conflict);
	}
}
```

`Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/ProblemExtensionsTests.cs`:
```csharp
using Grpc.Core;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class ProblemExtensionsTests
{
	[Theory]
	[InlineData(ErrorCategory.Validation, StatusCode.InvalidArgument)]
	[InlineData(ErrorCategory.Conflict, StatusCode.AlreadyExists)]
	[InlineData(ErrorCategory.LockedOut, StatusCode.PermissionDenied)]
	[InlineData(ErrorCategory.NotAllowed, StatusCode.PermissionDenied)]
	[InlineData(ErrorCategory.NotFound, StatusCode.Unknown)]
	void ToRpcException_maps_the_category_to_the_expected_status_code(ErrorCategory category, StatusCode expected)
	{
		var problem = new Problem { Category = category };

		var exception = problem.ToRpcException();

		exception.StatusCode.ShouldBe(expected);
	}

	[Fact]
	void ToRpcException_carries_the_errors_dictionary_in_the_problem_bin_trailer()
	{
		var problem = new Problem { Category = ErrorCategory.Validation, Errors = new Dictionary<string, string[]> { ["Email"] = ["required"] } };

		var exception = problem.ToRpcException();
		var trailer = exception.Trailers.Get("problem-bin");
		var decoded = JsonSerializer.Deserialize<Dictionary<string, string[]>>(trailer!.ValueBytes);

		decoded!["Email"].ShouldBe(["required"]);
	}
}
```

`Midgard/tests/Infrastructure.Web.Client.Tests/Grpc/RpcExceptionExtensionsTests.cs`:
```csharp
using Grpc.Core;
using Norse.Infrastructure.Web.Client.Grpc;

namespace Norse.Infrastructure.Web.Client.Tests.Grpc;

public sealed class RpcExceptionExtensionsTests
{
	[Fact]
	void DecodeProblem_returns_the_errors_from_the_problem_bin_trailer()
	{
		var errors = new Dictionary<string, string[]> { ["Email"] = ["required"] };
		var trailers = new Metadata { { "problem-bin", JsonSerializer.SerializeToUtf8Bytes(errors) } };
		var exception = new RpcException(new Status(StatusCode.InvalidArgument, "Validation"), trailers);

		var decoded = exception.DecodeProblem();

		decoded["Email"].ShouldBe(["required"]);
	}

	[Fact]
	void DecodeProblem_returns_empty_when_no_trailer_present()
	{
		var exception = new RpcException(new Status(StatusCode.Unknown, ""));

		exception.DecodeProblem().ShouldBeEmpty();
	}
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests/Infrastructure.Web.Server.Tests.csproj` and `dotnet test Midgard/tests/Infrastructure.Web.Client.Tests/Infrastructure.Web.Client.Tests.csproj`
Expected: FAIL to compile — none of the types exist yet.

- [ ] **Step 4: Implement `Infrastructure.Web.Server/Mediator/Grpc/`**

`OutcomeFailedException.cs`:
```csharp
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
/// Thrown only by <see cref="OutcomeExtensions.ThrowIfFailed{T}"/>, caught only by <see cref="OutcomeServerInterceptor"/> —
/// scoped to this project so it's never visible to code that isn't already building a gRPC-hosted mediator handler.
/// </summary>
sealed class OutcomeFailedException(Problem problem) : Exception
{
	public Problem Problem { get; } = problem;
}
```

`OutcomeExtensions.cs`:
```csharp
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

public static class OutcomeExtensions
{
	public static T ThrowIfFailed<T>(this Outcome<T> outcome) =>
		outcome.IsSuccess ? outcome.Value! : throw new OutcomeFailedException(outcome.Problem!);

	public static void ThrowIfFailed(this Outcome outcome)
	{
		if (!outcome.IsSuccess)
			throw new OutcomeFailedException(outcome.Problem!);
	}
}
```

`ProblemExtensions.cs`:
```csharp
using System.Text.Json;
using Grpc.Core;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

public static class ProblemExtensions
{
	public static RpcException ToRpcException(this Problem problem)
	{
		var status = problem.Category switch
		{
			ErrorCategory.Validation => StatusCode.InvalidArgument,
			ErrorCategory.Conflict => StatusCode.AlreadyExists,
			ErrorCategory.LockedOut or ErrorCategory.NotAllowed => StatusCode.PermissionDenied,
			_ => StatusCode.Unknown,
		};
		var trailers = new Metadata { { "problem-bin", JsonSerializer.SerializeToUtf8Bytes(problem.Errors) } };
		return new RpcException(new Status(status, problem.Category.ToString()), trailers);
	}
}
```

`OutcomeServerInterceptor.cs`:
```csharp
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>Zero domain knowledge — registered once per gRPC-hosting realm, reused verbatim by every future gRPC-hosted mediator handler.</summary>
sealed class OutcomeServerInterceptor : Interceptor
{
	public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
		TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
	{
		try { return await continuation(request, context); }
		catch (OutcomeFailedException ex) { throw ex.Problem.ToRpcException(); }
	}
}
```

- [ ] **Step 5: Implement `Infrastructure.Web.Client/Grpc/`**

`RpcExceptionExtensions.cs`:
```csharp
using System.Text.Json;
using Grpc.Core;

namespace Norse.Infrastructure.Web.Client.Grpc;

/// <summary>Client-side companion to Infrastructure.Web.Server's OutcomeServerInterceptor — decodes an
/// RpcException's problem-bin trailer directly into a plain dictionary. Never references Asgard's
/// Problem/ErrorCategory (server-only) — this project compiles into a WASM client bundle.</summary>
public static class RpcExceptionExtensions
{
	public static IReadOnlyDictionary<string, string[]> DecodeProblem(this RpcException exception)
	{
		var trailer = exception.Trailers.Get("problem-bin");
		return trailer is null
			? new Dictionary<string, string[]>()
			: JsonSerializer.Deserialize<Dictionary<string, string[]>>(trailer.ValueBytes) ?? new();
	}
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run both test projects again. Expected: PASS — 4 tests in `OutcomeExtensionsTests`, 6 in `ProblemExtensionsTests` (5 theory cases + 1 fact), 2 in `RpcExceptionExtensionsTests`.

- [ ] **Step 7: Wire `Midgard.slnx` and `Bifrost.slnx`**

In `Midgard/Midgard.slnx`, add under `/src/`:
```xml
		<Project Path="src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj" />
		<Project Path="src/Infrastructure.Web.Client/Infrastructure.Web.Client.csproj" />
```
and under `/tests/`:
```xml
		<Project Path="tests/Infrastructure.Web.Server.Tests/Infrastructure.Web.Server.Tests.csproj" />
		<Project Path="tests/Infrastructure.Web.Client.Tests/Infrastructure.Web.Client.Tests.csproj" />
```

In `Bifrost.slnx`, under the existing Midgard `/src/` and `/tests/` folders, add the same four entries with the `Midgard/` path prefix.

- [ ] **Step 8: Commit**

```bash
cd Midgard
git add src/Infrastructure.Web.Server src/Infrastructure.Web.Client tests/Infrastructure.Web.Server.Tests tests/Infrastructure.Web.Client.Tests Midgard.slnx
git commit -m "feat: add Infrastructure.Web.Server/Mediator/Grpc and Infrastructure.Web.Client/Grpc — gRPC channel adapters for the platform mediator"
cd ..
git add Bifrost.slnx
git commit -m "chore: wire Midgard's new channel-adapter projects into Bifrost.slnx"
```

---

## SHIP GATE — Midgard (`Infrastructure.Web.Server`, `Infrastructure.Web.Client`)

**STOP. Do not start Task 4 until this gate is cleared.**

1. Push the Midgard commit; open a PR against `master`; confirm CI is green.
2. Merge the PR; push a version tag; confirm `Norse.Infrastructure.Web.Server` and `Norse.Infrastructure.Web.Client` are both live on the NuGet feed.
3. Push the Bifrost commit.

Task 4 (Himinbjörg's forwarder, `ThrowIfFailed()`) and Task 6 (Yggdrasil's `Hosting.Web.Client`, `DecodeProblem()`) both need these packages live.

---

## Task 4: Himinbjörg — `Identity.Web.Server`

Rewritten in full 2026-07-14 to match spec addendum `Glitnir/docs/Heimdall/specs/2026-07-13-authn-identity-split-design.md` §9.3/§9.4/§9.5 — handlers return `Outcome<BoolResponse>` (not `Outcome<LoginResponse>`/`Outcome<RegisterResponse>`, both retired), `RegisterHandler`'s `IdentityResult` → `ErrorCategory` mapping only treats genuine duplicates as `Conflict` (password-policy failures are `Validation`), and the `AuthenticationService` forwarder becomes one line per method via `ThrowIfFailed()` — no branching, no `CallContext`, cancellation comes from a directly-injected `IHttpContextAccessor`. `LoginHandler`'s shape was confirmed verbatim by Buvy against his own independent expectation before this rewrite — implement it exactly as given, no further negotiation needed on that one file.

**This project takes explicit NorseRefs on all three of its actual dependencies** — Heimdall's `AuthN.Components` (the wire contract), Asgard's `Abstractions.Web.Server` (the mediator vocabulary the handlers work in directly), and Midgard's `Infrastructure.Web.Server` (`ThrowIfFailed()`/`OutcomeServerInterceptor`) — rather than relying on any transitive exposure through one of the others.

**Autonomous-run note: `Norse.AuthN.Components` (Task 2) is not live on NuGet yet.** Build/test this task with `-p:UseProjectReferences=true` so its `NorseRef` resolves to a local `ProjectReference` at `Heimdall/src/AuthN.Components/AuthN.Components.csproj` instead of failing to restore. Midgard's and Asgard's NorseRefs resolve normally either way — both are genuinely live.

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
- Modify: `Bifrost.slnx` (wire the two new projects into the existing Himinbjörg folders — edit the file directly, do **not** `cd` into Bifrost's own root or run any `git checkout`/`git branch` there; Bifrost stays on `master`, no feature branch)

**Interfaces:**
- Consumes: `IAuthenticationService`, `LoginRequest`, `LoginResult`, `RegisterRequest`, `LogoutRequest` (Task 2, `Norse.AuthN.Components`); `Outcome`, `Outcome<T>`, `ErrorCategory`, `BoolResponse`, `IRequestHandler<,>` (Task 1, `Norse.Abstractions.Web.Server`); `OutcomeExtensions.ThrowIfFailed`, `OutcomeServerInterceptor` (Task 3, `Norse.Infrastructure.Web.Server`); `NorseUser`, `NorseIdentityDbContext`, `AddNorseIdentity()` (existing `Norse.Identity`).
- Produces:
  - `Norse.Identity.Web.Server.LoginHandler : IRequestHandler<LoginRequest, Outcome<BoolResponse>>`
  - `Norse.Identity.Web.Server.RegisterHandler : IRequestHandler<RegisterRequest, Outcome<BoolResponse>>`
  - `Norse.Identity.Web.Server.LogoutHandler : IRequestHandler<LogoutRequest, Outcome>`
  - `Norse.Identity.Web.Server.AuthenticationService : IAuthenticationService` (internal, thin forwarder)
  - `IServiceCollectionExtensions.AddNorseAuthenticationService(this IServiceCollection, string connectionString)` — also registers `OutcomeServerInterceptor`.
  - `IApplicationBuilderExtensions.MapNorseAuthenticationService(this WebApplication)`

- [ ] **Step 1: Fix the stale comment in `Identity.csproj` first**

`Himinbjorg/src/Identity/Identity.csproj` currently reads (in `<Description>`): `"...Runtime library — referenced by Norse.Auth.Server; never by migration tooling."` — `Norse.Auth.Server` predates the 07-11 rename and this realm's own gRPC-implementation project (`Identity.Web.Server`, created in this task). Update to:
```xml
<Description>Norse.Identity: ASP.NET Core Identity v3 entity types, NorseIdentityDbContext (Identity + OpenIddict), NorseUserStore with projection overrides, and DI extension. Runtime library — referenced by Norse.Identity.Web.Server; never by migration tooling.</Description>
```

**Amendment (2026-07-25):** the base `Identity` project this step edits was deleted 2026-07-23 and folded into `Identity.Web.Server` — this comment-fix step, and the `ProjectReference` to it in Step 2 below, describe a project shape that no longer exists. See `Identity.Web.Server`'s current `.csproj` for the live description.

- [ ] **Step 2: Create the project file**

`Himinbjorg/src/Identity.Web.Server/Identity.Web.Server.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Identity.Web.Server: IAuthenticationService's gRPC implementation over NorseUserStore. Handlers work entirely in Outcome&lt;T&gt; (Asgard's mediator law); the forwarder decomposes failure via Midgard's Infrastructure.Web.Server, never by hand. Always runs inside an HTTP context, bound into Yggdrasil's Hosting.Web.Server process.</Description>
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
		<NorseRef Include="Abstractions.Web.Server">
			<Repo>Asgard</Repo>
		</NorseRef>
		<NorseRef Include="Infrastructure.Web.Server">
			<Repo>Midgard</Repo>
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
using Norse.Abstractions.Web.Server.Mediator;
using Norse.AuthN.Components;
using Norse.Identity;

namespace Norse.Identity.Web.Server.Tests;

public sealed class RegisterHandlerTests
{
	static NorseIdentityDbContext CreateContext()
	{
		var options = new DbContextOptionsBuilder<NorseIdentityDbContext>()
			.UseSqlite("DataSource=:memory:")
			.Options;
		var context = new NorseIdentityDbContext(options);
		context.Database.OpenConnection();
		context.Database.EnsureCreated();
		return context;
	}

	// Real PasswordValidator<NorseUser> wired in (not an empty array) so a weak-but-non-duplicate
	// password actually produces IdentityResult errors — needed to test the Validation-vs-Conflict
	// categorization below meaningfully, not just narrate it in a comment.
	static UserManager<NorseUser> CreateUserManager(NorseIdentityDbContext context)
	{
		var store = new NorseUserStore(context, new IdentityErrorDescriber());
		return new UserManager<NorseUser>(
			store, null, new PasswordHasher<NorseUser>(), [], [new PasswordValidator<NorseUser>()],
			new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null, NullLogger<UserManager<NorseUser>>.Instance);
	}

	[Fact]
	async Task Rejects_an_invalid_request_without_touching_the_store()
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
	async Task Creates_a_NorseUser_for_a_valid_request()
	{
		using var context = CreateContext();
		var handler = new RegisterHandler(CreateUserManager(context), new RegisterRequestValidator());
		var request = new RegisterRequest { Email = "user@example.com", Password = "correct-horse-battery-1A!" };

		var outcome = await handler.Handle(request, CancellationToken.None);

		outcome.IsSuccess.ShouldBeTrue();
		outcome.Value!.Value.ShouldBeTrue();
		(await context.Users.SingleAsync()).Email.ShouldBe("user@example.com");
	}

	[Fact]
	async Task Rejects_a_duplicate_email_as_Conflict()
	{
		using var context = CreateContext();
		var userManager = CreateUserManager(context);
		var handler = new RegisterHandler(userManager, new RegisterRequestValidator());
		var request = new RegisterRequest { Email = "user@example.com", Password = "correct-horse-battery-1A!" };
		await handler.Handle(request, CancellationToken.None);

		var outcome = await handler.Handle(request, CancellationToken.None);

		outcome.IsSuccess.ShouldBeFalse();
		outcome.Problem!.Category.ShouldBe(ErrorCategory.Conflict);
	}

	[Fact]
	async Task Rejects_a_weak_but_non_duplicate_password_as_Validation_not_Conflict()
	{
		using var context = CreateContext();
		var handler = new RegisterHandler(CreateUserManager(context), new RegisterRequestValidator());
		// Passes FluentValidation's client-side MinimumLength(8) but fails ASP.NET Identity's default
		// password-complexity rules (needs a digit, an uppercase letter, a non-alphanumeric char) —
		// exercises the corrected mapping: this must be Validation, never Conflict.
		var request = new RegisterRequest { Email = "user2@example.com", Password = "aaaaaaaa" };

		var outcome = await handler.Handle(request, CancellationToken.None);

		outcome.IsSuccess.ShouldBeFalse();
		outcome.Problem!.Category.ShouldBe(ErrorCategory.Validation);
	}
}
```

`Himinbjorg/tests/Identity.Web.Server.Tests/LogoutHandlerTests.cs`:
```csharp
using Norse.Abstractions.Web.Server.Mediator;
using Norse.AuthN.Components;
using NSubstitute;

namespace Norse.Identity.Web.Server.Tests;

public sealed class LogoutHandlerTests
{
	[Fact]
	async Task Always_returns_a_successful_outcome()
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

static class MockSignInManager
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
using Norse.Abstractions.Web.Server.Mediator;
using Norse.AuthN.Components;
using Norse.Identity;
using NSubstitute;

namespace Norse.Identity.Web.Server.Tests;

public sealed class LoginHandlerTests
{
	[Fact]
	async Task Rejects_an_invalid_request_without_attempting_sign_in()
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
	async Task Returns_LockedOut_when_the_store_reports_lockout()
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
	async Task Returns_Succeeded_true_when_the_store_signs_in()
	{
		var signInManager = MockSignInManager.Create();
		signInManager.PasswordSignInAsync("user@example.com", "correct-horse", false, true)
			.Returns(Microsoft.AspNetCore.Identity.SignInResult.Success);
		var handler = new LoginHandler(signInManager, new LoginRequestValidator());
		var request = new LoginRequest { Email = "user@example.com", Password = "correct-horse" };

		var outcome = await handler.Handle(request, CancellationToken.None);

		outcome.IsSuccess.ShouldBeTrue();
		outcome.Value!.Value.ShouldBeTrue();
	}

	[Fact]
	async Task Returns_Succeeded_false_never_an_error_when_credentials_are_wrong()
	{
		// The whole point of §9.3's anti-enumeration collapse: wrong username and wrong password both
		// land here, as a successful check that returned false — never Outcome.Err(InvalidCredentials).
		var signInManager = MockSignInManager.Create();
		signInManager.PasswordSignInAsync("user@example.com", "wrong", false, true)
			.Returns(Microsoft.AspNetCore.Identity.SignInResult.Failed);
		var handler = new LoginHandler(signInManager, new LoginRequestValidator());
		var request = new LoginRequest { Email = "user@example.com", Password = "wrong" };

		var outcome = await handler.Handle(request, CancellationToken.None);

		outcome.IsSuccess.ShouldBeTrue();
		outcome.Value!.Value.ShouldBeFalse();
	}
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test Himinbjorg/tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj` (add `-p:UseProjectReferences=true`, per the autonomous-run note above)
Expected: FAIL to compile — `LoginHandler`, `RegisterHandler`, `LogoutHandler` don't exist yet.

- [ ] **Step 5: Implement the handlers**

`Himinbjorg/src/Identity.Web.Server/LoginHandler.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.AuthN.Components;
using Norse.Identity;

namespace Norse.Identity.Web.Server;

public sealed class LoginHandler(SignInManager<NorseUser> signInManager, LoginRequestValidator validator)
	: IRequestHandler<LoginRequest, Outcome<BoolResponse>>
{
	public async ValueTask<Outcome<BoolResponse>> Handle(LoginRequest request, CancellationToken cancellationToken)
	{
		var validation = await validator.ValidateAsync(request, cancellationToken);
		if (!validation.IsValid)
			return Outcome<BoolResponse>.Err(ErrorCategory.Validation, validation.ToDictionary());

		// SignInManager mints/clears the cookie itself via its own IHttpContextAccessor dependency —
		// no manual HttpContext.SignInAsync call needed here (must register AddHttpContextAccessor()).
		var result = await signInManager.PasswordSignInAsync(
			request.Email, request.Password, request.RememberMe, lockoutOnFailure: true);

		if (result.IsLockedOut) return Outcome<BoolResponse>.Err(ErrorCategory.LockedOut);
		if (result.IsNotAllowed) return Outcome<BoolResponse>.Err(ErrorCategory.NotAllowed);

		// Succeeded=false covers "no such user" and "wrong password" identically — deliberate,
		// anti-enumeration, see spec §9.3. Never Outcome.Err(InvalidCredentials).
		return Outcome<BoolResponse>.Ok(new BoolResponse { Value = result.Succeeded });
	}
}
```

`Himinbjorg/src/Identity.Web.Server/RegisterHandler.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.AuthN.Components;
using Norse.Identity;

namespace Norse.Identity.Web.Server;

public sealed class RegisterHandler(UserManager<NorseUser> userManager, RegisterRequestValidator validator)
	: IRequestHandler<RegisterRequest, Outcome<BoolResponse>>
{
	public async ValueTask<Outcome<BoolResponse>> Handle(RegisterRequest request, CancellationToken cancellationToken)
	{
		var validation = await validator.ValidateAsync(request, cancellationToken);
		if (!validation.IsValid)
			return Outcome<BoolResponse>.Err(ErrorCategory.Validation, validation.ToDictionary());

		var user = new NorseUser { UserName = request.Email, Email = request.Email };
		var result = await userManager.CreateAsync(user, request.Password);

		if (!result.Succeeded)
		{
			// Only a genuine duplicate is Conflict — Buvy's explicit call, so a legitimate user sees
			// "that email's taken" and doesn't retry a doomed registration 10,000 times (spec §9.3).
			// Everything else (password-policy codes) is Validation — a rejected password isn't a conflict.
			var isDuplicate = result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
			var category = isDuplicate ? ErrorCategory.Conflict : ErrorCategory.Validation;
			var errors = result.Errors
				.GroupBy(e => e.Code)
				.ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
			return Outcome<BoolResponse>.Err(category, errors);
		}

		return Outcome<BoolResponse>.Ok(new BoolResponse { Value = true });
	}
}
```

`Himinbjorg/src/Identity.Web.Server/LogoutHandler.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Norse.Abstractions.Web.Server.Mediator;
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

Run: `dotnet test Himinbjorg/tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj -p:UseProjectReferences=true`
Expected: PASS — 9 tests green (4 `RegisterHandlerTests` + 1 `LogoutHandlerTests` + 4 `LoginHandlerTests` — note the two new tests beyond the original brief: the weak-password-is-Validation case and the explicit "Succeeded=false is not an error" case).

- [ ] **Step 7: Implement the gRPC forwarder — one line per method, no branching**

`Himinbjorg/src/Identity.Web.Server/AuthenticationService.cs`:
```csharp
using Microsoft.AspNetCore.Http;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.AuthN.Components;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

namespace Norse.Identity.Web.Server;

/// <summary>
/// Thin forwarder — every method delegates to its matching <see cref="IRequestHandler{TRequest,TResponse}"/>
/// and calls <see cref="OutcomeExtensions.ThrowIfFailed{T}"/>; <see cref="OutcomeServerInterceptor"/>
/// (registered in <see cref="ServiceCollectionExtensions.AddNorseAuthenticationService"/>) does the actual
/// failure translation. No branching, no business logic lives here — spec §9.4/§9.5.
/// </summary>
internal sealed class AuthenticationService(
	IRequestHandler<LoginRequest, Outcome<BoolResponse>> loginHandler,
	IRequestHandler<RegisterRequest, Outcome<BoolResponse>> registerHandler,
	IRequestHandler<LogoutRequest, Outcome> logoutHandler,
	IHttpContextAccessor httpContextAccessor)
	: IAuthenticationService
{
	public async Task<LoginResult> Login(LoginRequest request) =>
		new() { Succeeded = (await loginHandler.Handle(request, httpContextAccessor.HttpContext!.RequestAborted)).ThrowIfFailed().Value };

	public async Task Register(RegisterRequest request) =>
		(await registerHandler.Handle(request, httpContextAccessor.HttpContext!.RequestAborted)).ThrowIfFailed();

	public async Task Logout(LogoutRequest request) =>
		(await logoutHandler.Handle(request, httpContextAccessor.HttpContext!.RequestAborted)).ThrowIfFailed();
}
```

- [ ] **Step 8: Implement the DI/hosting wiring — registers the Midgard interceptor**

`Himinbjorg/src/Identity.Web.Server/ServiceCollectionExtensions.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.AuthN.Components;
using Norse.Identity;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

namespace Norse.Identity.Web.Server;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddNorseAuthenticationService(this IServiceCollection services, string connectionString)
	{
		services.AddDbContext<NorseIdentityDbContext>(o => o.UseNpgsql(connectionString));
		services.AddNorseIdentity();
		services.AddHttpContextAccessor();
		services.AddCodeFirstGrpc(o => o.Interceptors.Add<OutcomeServerInterceptor>());

		services.AddScoped<LoginRequestValidator>();
		services.AddScoped<RegisterRequestValidator>();

		services.AddScoped<IRequestHandler<LoginRequest, Outcome<BoolResponse>>, LoginHandler>();
		services.AddScoped<IRequestHandler<RegisterRequest, Outcome<BoolResponse>>, RegisterHandler>();
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

- [ ] **Step 9: Wire the new projects into `Himinbjorg.slnx` and `Bifrost.slnx`**

In `Himinbjorg/Himinbjorg.slnx`, add under `/src/`:
```xml
		<Project Path="src/Identity.Web.Server/Identity.Web.Server.csproj" />
```
and under `/tests/`:
```xml
		<Project Path="tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj" />
```

In `Bifrost.slnx`, under the existing Himinbjörg `/src/` and `/tests/` solution folders (search for `Himinbjorg/src/Identity/Identity.csproj` to find them), add the same two entries with the `Himinbjorg/` path prefix. **Edit the file directly — do not `cd` into Bifrost's root, do not run any `git checkout`/`git branch` scoped to Bifrost.**

- [ ] **Step 10: Stage (session policy: stage only, never commit)**

```bash
cd Himinbjorg
git add src/Identity.Web.Server tests/Identity.Web.Server.Tests Himinbjorg.slnx src/Identity/Identity.csproj
cd ..
git add Bifrost.slnx
```

---

## SHIP GATE — Himinbjörg

**STOP. Do not start Task 5 until this gate is cleared.**

1. Push the Himinbjörg commit; open a PR against `master`; confirm CI is green.
2. Merge the PR; push a version tag; confirm `Norse.Identity.Web.Server` is live on the NuGet feed.
3. Push the Bifrost submodule-pointer commit.

---

## Task 5: Yggdrasil — `Hosting.Web.Server` (host the gRPC service)

Rewritten in full 2026-07-14 — see spec addendum §9.8 for a correction discovered while writing this task's actual code: the plan previously described a "Blazor Server gateway using Midgard's transform" that was never given a concrete shape, and wrongly implied Midgard would provide the `Outcome<T>`-to-`AuthenticationResult` mapping. It can't — that mapping requires knowing what `AuthenticationResult` is, a Heimdall type, and Midgard staying domain-agnostic is the whole point of building it there. §9.8 introduces the actual shared interface (`IAuthenticationGateway`, new addition to Heimdall's `AuthN.Components`) and this task's own concrete implementation of it — read §9.8 in full before implementing.

**Autonomous-run note:** Himinbjörg's `Identity.Web.Server` (Task 4) is not live on NuGet — build/test with `-p:UseProjectReferences=true`.

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`
- Modify: `Yggdrasil/src/Hosting.Web.Server/Program.cs`
- Create: `Yggdrasil/src/Hosting.Web.Server/BlazorServerAuthenticationGateway.cs`
- Modify: `Heimdall/src/AuthN.Components/AuthN.Components.csproj` *(no new package — just a new file)*
- Create: `Heimdall/src/AuthN.Components/IAuthenticationGateway.cs`

**Interfaces:**
- Consumes: `AddNorseAuthenticationService(this IServiceCollection, string)`, `MapNorseAuthenticationService(this WebApplication)` (Task 4, `Norse.Identity.Web.Server`); `IRequestHandler<,>`, `Outcome`, `Outcome<T>`, `BoolResponse` (Task 1, `Norse.Abstractions.Web.Server`); `LoginRequest`, `RegisterRequest`, `LogoutRequest`, `AuthenticationResult` (Task 2, `Norse.AuthN.Components`).
- Produces: `Norse.AuthN.Components.IAuthenticationGateway` (new shared interface — first implementation is this task's); `Norse.Hosting.Web.Server.BlazorServerAuthenticationGateway : IAuthenticationGateway`, registered in DI.

- [ ] **Step 0: Add `IAuthenticationGateway` to Heimdall's `AuthN.Components`**

This is the interface both this task and Task 6 implement — lands in the shared contract project since Task 8's Razor components need to inject it uniformly regardless of host. No new package reference needed.

`Heimdall/src/AuthN.Components/IAuthenticationGateway.cs`:
```csharp
namespace Norse.AuthN.Components;

/// <summary>
/// The only thing any Razor component injects — never <see cref="IAuthenticationService"/> directly.
/// Two implementations exist, one per host: Yggdrasil's Hosting.Web.Server (Blazor Server, wraps the
/// mediator handlers directly, no wire) and Hosting.Web.Client (WASM, wraps the real gRPC-Web client
/// proxy). Both produce the same <see cref="AuthenticationResult"/> shape.
/// </summary>
public interface IAuthenticationGateway
{
	Task<AuthenticationResult> Login(LoginRequest request);
	Task<AuthenticationResult> Register(RegisterRequest request);
	Task<AuthenticationResult> Logout(LogoutRequest request);
}
```

**Amendment (2026-07-25):** this hand-written `IAuthenticationGateway` was retired 2026-07-25 (Heimdall `feature/transport-neutral-gateway`, merged, tag v0.0.3). `IAuthenticationService` (in `AuthN.Services`) now carries Asgard's `[GenerateGateway]` attribute, and `AuthN.Services` (`NorseGatewayEmissionMode=Contract`) is where Asgard's `GatewayGenerator` emits the equivalent interface at compile time instead — no hand-authored file, no `BlazorServerAuthenticationGateway`/`WasmAuthenticationGateway` split as such. See `../specs/2026-07-13-authn-identity-split-design.md` §9.8 for where this shape originated.

Build/test `Heimdall/tests/AuthN.Components.Tests` to confirm this compiles cleanly alongside the existing Task 2 work (no test needed for a plain interface declaration).

- [ ] **Step 1: Add the `NorseRef`s and the reflection package**

gRPC Server Reflection lets a client (Postman, `grpcurl`) discover `IAuthenticationService`'s methods and message shapes without needing a hand-authored `.proto` — this is the direct tool for testing the wire lifecycle independent of the Blazor UI, per protobuf-net.Grpc.AspNetCore riding on the same underlying `Grpc.AspNetCore.Server` primitives the native reflection package hooks into.

In `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`, add to the existing `NorseRef` `ItemGroup`:
```xml
		<NorseRef Include="Identity.Web.Server">
			<Repo>Himinbjorg</Repo>
		</NorseRef>
		<NorseRef Include="AuthN.Components">
			<Repo>Heimdall</Repo>
		</NorseRef>
		<NorseRef Include="Abstractions.Web.Server">
			<Repo>Asgard</Repo>
		</NorseRef>
```
(`AuthN.Components.FluentUI` is **not** referenced here — that's Task 8's Razor markup, not needed to host the gRPC service or the Blazor Server gateway.) Add a new `ItemGroup`:
```xml
	<ItemGroup>
		<PackageReference Include="Grpc.AspNetCore.Server.Reflection" Version="*" />
	</ItemGroup>
```

- [ ] **Step 2: Implement the Blazor Server gateway**

`Yggdrasil/src/Hosting.Web.Server/BlazorServerAuthenticationGateway.cs`:
```csharp
using Microsoft.AspNetCore.Http;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.AuthN.Components;

namespace Norse.Hosting.Web.Server;

/// <summary>
/// Blazor Server's own <see cref="IAuthenticationGateway"/> — calls the mediator handlers directly,
/// in-process, no gRPC involved at all (per §2's transport matrix). Maps <c>Outcome&lt;T&gt;</c> to
/// <see cref="AuthenticationResult"/> inline — this glue is realm-specific, not generic Midgard
/// infrastructure (spec §9.8).
/// </summary>
internal sealed class BlazorServerAuthenticationGateway(
	IRequestHandler<LoginRequest, Outcome<BoolResponse>> loginHandler,
	IRequestHandler<RegisterRequest, Outcome<BoolResponse>> registerHandler,
	IRequestHandler<LogoutRequest, Outcome> logoutHandler,
	IHttpContextAccessor httpContextAccessor)
	: IAuthenticationGateway
{
	public async Task<AuthenticationResult> Login(LoginRequest request)
	{
		var outcome = await loginHandler.Handle(request, httpContextAccessor.HttpContext!.RequestAborted);
		return outcome.IsSuccess
			? new AuthenticationResult { Succeeded = outcome.Value!.Value }
			: new AuthenticationResult { Succeeded = false, Errors = outcome.Problem!.Errors };
	}

	public async Task<AuthenticationResult> Register(RegisterRequest request)
	{
		var outcome = await registerHandler.Handle(request, httpContextAccessor.HttpContext!.RequestAborted);
		return new AuthenticationResult { Succeeded = outcome.IsSuccess, Errors = outcome.Problem?.Errors ?? new Dictionary<string, string[]>() };
	}

	public async Task<AuthenticationResult> Logout(LogoutRequest request)
	{
		var outcome = await logoutHandler.Handle(request, httpContextAccessor.HttpContext!.RequestAborted);
		return new AuthenticationResult { Succeeded = outcome.IsSuccess, Errors = outcome.Problem?.Errors ?? new Dictionary<string, string[]>() };
	}
}
```

- [ ] **Step 3: Wire `Program.cs`**

In `Yggdrasil/src/Hosting.Web.Server/Program.cs`, add `using Norse.Identity.Web.Server;` and `using Norse.AuthN.Components;` to the top, and register the new service (this coexists with the existing `ApplicationUser`/`PlaceholderUserStore` wiring — that scaffold is untouched until the Task-8-and-later cutover plan):
```csharp
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Norse.AuthN.Components;
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
builder.Services.AddScoped<IAuthenticationGateway, BlazorServerAuthenticationGateway>();

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

- [ ] **Step 4: Manually verify the project still builds**

Run: `dotnet build Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj -p:UseProjectReferences=true`
Expected: builds clean (0 errors) — there is no automated test for `Program.cs` wiring; Task 10's end-to-end check is the real verification. `BlazorServerAuthenticationGateway`'s mapping logic is straightforward enough that a unit test isn't required here either — it's exercised for real by Task 10.

- [ ] **Step 5: Stage (session policy: stage only, never commit)**

```bash
cd Heimdall
git add src/AuthN.Components/IAuthenticationGateway.cs
cd ../Yggdrasil
git add src/Hosting.Web.Server/Hosting.Web.Server.csproj src/Hosting.Web.Server/Program.cs src/Hosting.Web.Server/BlazorServerAuthenticationGateway.cs
```

---

## Task 6: Yggdrasil — `Hosting.Web.Client` (gRPC-Web client wiring)

Rewritten in full 2026-07-14 alongside Task 5 — see spec addendum §9.8. This is `IAuthenticationGateway`'s second implementation, wrapping the real gRPC-Web client proxy. `RpcException` here is unavoidable (it's what the underlying client library itself throws) — this is the one place in this whole design a `try`/`catch` is genuine infrastructure, not an authored shortcut.

**Autonomous-run note:** `Norse.AuthN.Components` (Task 2/5) is not live on NuGet — build with `-p:UseProjectReferences=true`. `Norse.Infrastructure.Web.Client` (Task 3) genuinely is live.

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Client/Hosting.Web.Client.csproj`
- Modify: `Yggdrasil/src/Hosting.Web.Client/Program.cs`
- Create: `Yggdrasil/src/Hosting.Web.Client/BrowserCredentialsHandler.cs`
- Create: `Yggdrasil/src/Hosting.Web.Client/WasmAuthenticationGateway.cs`

**Interfaces:**
- Consumes: `IAuthenticationService`, `IAuthenticationGateway`, `AuthenticationResult` (Task 2/5, `Norse.AuthN.Components`); `RpcExceptionExtensions.DecodeProblem` (Task 3, `Norse.Infrastructure.Web.Client`).
- Produces: `Norse.Hosting.Web.Client.WasmAuthenticationGateway : IAuthenticationGateway`, registered in the WASM container; `IAuthenticationService` registered as a real gRPC-Web client proxy (internal to the gateway, not injected directly by any component).

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
		<NorseRef Include="Infrastructure.Web.Client">
			<Repo>Midgard</Repo>
		</NorseRef>
```
(`AuthN.Components.FluentUI` is **not** referenced here either — Task 8's concern, not the client wiring's.)

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

- [ ] **Step 3: Implement the WASM gateway**

`Yggdrasil/src/Hosting.Web.Client/WasmAuthenticationGateway.cs`:
```csharp
using Grpc.Core;
using Norse.AuthN.Components;
using Norse.Infrastructure.Web.Client.Grpc;

namespace Norse.Hosting.Web.Client;

/// <summary>
/// WASM's <see cref="IAuthenticationGateway"/> — wraps the real gRPC-Web client proxy. Catches
/// <see cref="RpcException"/> (the underlying client library's own failure signal, not this platform's
/// choice) and decodes it via Midgard's <see cref="RpcExceptionExtensions.DecodeProblem"/> — the one
/// piece of this that's genuine shared infrastructure, since it only ever touches the wire trailer,
/// never <see cref="AuthenticationResult"/> itself (spec §9.8).
/// </summary>
internal sealed class WasmAuthenticationGateway(IAuthenticationService authenticationService) : IAuthenticationGateway
{
	public async Task<AuthenticationResult> Login(LoginRequest request)
	{
		try
		{
			var result = await authenticationService.Login(request);
			return new AuthenticationResult { Succeeded = result.Succeeded };
		}
		catch (RpcException ex)
		{
			return new AuthenticationResult { Succeeded = false, Errors = ex.DecodeProblem() };
		}
	}

	public async Task<AuthenticationResult> Register(RegisterRequest request)
	{
		try
		{
			await authenticationService.Register(request);
			return new AuthenticationResult { Succeeded = true };
		}
		catch (RpcException ex)
		{
			return new AuthenticationResult { Succeeded = false, Errors = ex.DecodeProblem() };
		}
	}

	public async Task<AuthenticationResult> Logout(LogoutRequest request)
	{
		try
		{
			await authenticationService.Logout(request);
			return new AuthenticationResult { Succeeded = true };
		}
		catch (RpcException ex)
		{
			return new AuthenticationResult { Succeeded = false, Errors = ex.DecodeProblem() };
		}
	}
}
```

- [ ] **Step 4: Wire the client proxy and gateway in `Program.cs`**

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
builder.Services.AddScoped<IAuthenticationGateway, WasmAuthenticationGateway>();
```

- [ ] **Step 5: Manually verify the project still builds**

Run: `dotnet build Yggdrasil/src/Hosting.Web.Client/Hosting.Web.Client.csproj -p:UseProjectReferences=true`
Expected: builds clean (0 errors). The actual cookie round-trip through the browser can only be confirmed by running the app (Task 10).

- [ ] **Step 6: Stage (session policy: stage only, never commit)**

```bash
git add src/Hosting.Web.Client/Hosting.Web.Client.csproj src/Hosting.Web.Client/Program.cs src/Hosting.Web.Client/BrowserCredentialsHandler.cs src/Hosting.Web.Client/WasmAuthenticationGateway.cs
```

---

## SHIP GATE — Yggdrasil (backend + client wiring)

**STOP. Do not start Task 7 until this gate is cleared.**

1. Push the Yggdrasil commits (Tasks 5 and 6, same branch — `Hosting.Web.Server` + `Hosting.Web.Client`); open a PR against `master`; confirm CI is green.
2. Merge the PR. `Hosting.Web.Server`/`Hosting.Web.Client` are deployables, not NuGet-published libraries — no version tag/publish step applies here, unlike the library-producing realms above.
3. Push the Bifrost submodule-pointer commit (`master`, no branch — pointer bumps only, per `[[feedback_bifrost-stays-on-master]]`).

---

## Task 7: Bifrost — wire `Hosting.Web.Server` into the Aspire AppHost

**This is the one task in this whole plan where a Bifrost feature branch is actually warranted** — `AppHost.cs` is Bifrost's own code (`Orchestration.AppHost`), not a submodule-pointer bump or a `.slnx` wiring change. Per `[[feedback_bifrost-stays-on-master]]`'s own stated carve-out ("unless the feature being built genuinely lives in Bifrost"), branch here.

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

Run: `dotnet run --project src/Orchestration.AppHost -p:UseProjectReferences=true` (autonomous-run note: `Hosting.Web.Server` transitively needs Heimdall's/Himinbjörg's not-yet-published packages)
Expected: the Aspire dashboard shows `web` alongside `pg-primary`, `pg-replica`, and `migrations`; `web` starts only after `migrations` exits 0.

- [ ] **Step 3: Stage (session policy: stage only, never commit)**

```bash
git add src/Orchestration.AppHost/AppHost.cs
```

---

## Task 8: Heimdall — `AuthN.Components.FluentUI` (the Razor components)

Rewritten in full 2026-07-14 — the previous version predates `IAuthenticationGateway`/`AuthenticationResult` entirely and was never reconciled with spec §9.6/§9.8. This is also the first place the `Errors[""]` model-level convention actually gets consumed — working out the concrete `ValidationMessageStore` wiring is new design, not just a rename, since nothing before this task ever needed to populate one from a plain dictionary. Read spec §9.6/§9.8 in full before implementing.

**One non-obvious asymmetry, worth understanding before writing `Login.razor`:** `Login`'s anti-enumeration collapse (spec §9.3) means a failed login can have `Succeeded = false` **and an empty `Errors` dictionary** — there's deliberately nothing more specific to say for a wrong username/password. `Register`/`Logout` never produce that shape — every one of their failures has a populated `Errors` dictionary (`Conflict`/`Validation`). `Login.razor`'s handler needs an explicit fallback for the empty-`Errors` case; `Register.razor` doesn't.

**Files:**
- Create: `Heimdall/src/AuthN.Components.FluentUI/AuthN.Components.FluentUI.csproj`
- Create: `Heimdall/src/AuthN.Components.FluentUI/_Imports.razor`
- Create: `Heimdall/src/AuthN.Components.FluentUI/Login.razor`
- Create: `Heimdall/src/AuthN.Components.FluentUI/Register.razor`
- Create: `Heimdall/src/AuthN.Components.FluentUI/Logout.razor`
- Modify: `Bifrost.slnx`

**Interfaces:**
- Consumes: `IAuthenticationGateway`, `LoginRequest`, `RegisterRequest`, `LogoutRequest`, `AuthenticationResult`, `LoginRequestValidator`, `RegisterRequestValidator` (Task 2/5, `Norse.AuthN.Components`). **Never** `IAuthenticationService`, `Outcome<T>`, or `ErrorCategory` directly — all boxed below the gateway, per §9.8.

No new automated tests in this task — Razor component behavior is verified by the manual end-to-end check in Task 10; this task is TDD-exempt only in the narrow sense that bUnit component tests are deferred to the `IAccountService` follow-on plan once the pattern's proven here, consistent with keeping this bootstrap slim.

- [ ] **Step 1: Create the project file**

`Heimdall/src/AuthN.Components.FluentUI/AuthN.Components.FluentUI.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
	<PropertyGroup>
		<Description>Norse.AuthN.Components.FluentUI: Login/Register/Logout Razor components, FluentUI Blazor v5, wired against AuthN.Components' IAuthenticationGateway — never IAuthenticationService directly, never Outcome&lt;T&gt;, never a caught exception (spec §9.6/§9.8).</Description>
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
@using Norse.AuthN.Components
```

- [ ] **Step 3: `Login.razor`**

Uses a manually-constructed `EditContext`/`ValidationMessageStore` (not the simpler `Model="..."` form) specifically so `AuthenticationResult.Errors` can populate it after the async call returns — `<ValidationSummary />` (the standard framework component, not a FluentUI-specific one; it reads any `EditContext`'s message store regardless of which component library styled the form around it) then renders every message, field-keyed or model-level, in one place.

`Heimdall/src/AuthN.Components.FluentUI/Login.razor`:
```razor
@page "/authn/login"
@rendermode InteractiveAuto
@inject IAuthenticationGateway AuthenticationGateway
@inject NavigationManager Navigation

<PageTitle>Log in</PageTitle>

<h1>Log in</h1>

<EditForm EditContext="_editContext" OnValidSubmit="HandleLoginAsync" FormName="authn-login">
	<FluentValidationValidator />
	<ValidationSummary />
	<FluentTextField @bind-Value="_request.Email" Label="Email" Required="true" />
	<FluentTextField @bind-Value="_request.Password" TextFieldType="TextFieldType.Password" Label="Password" Required="true" />
	<FluentCheckbox @bind-Value="_request.RememberMe" Label="Remember me" />
	<FluentButton Type="ButtonType.Submit" Appearance="Appearance.Accent">Log in</FluentButton>
</EditForm>

@code {
	readonly LoginRequest _request = new() { Email = "", Password = "" };
	EditContext _editContext = default!;
	ValidationMessageStore _messageStore = default!;

	protected override void OnInitialized()
	{
		_editContext = new EditContext(_request);
		_messageStore = new ValidationMessageStore(_editContext);
	}

	async Task HandleLoginAsync()
	{
		_messageStore.Clear();
		var result = await AuthenticationGateway.Login(_request);

		if (!result.Succeeded)
		{
			// Succeeded=false with an EMPTY Errors dictionary is the deliberate anti-enumeration
			// collapse (spec §9.3) — wrong username and wrong password both land here with nothing
			// more specific the server is willing to say. Synthesize one generic message for that
			// case; LockedOut/NotAllowed/Validation always arrive with Errors already populated.
			var errors = result.Errors.Count > 0
				? result.Errors
				: new Dictionary<string, string[]> { [""] = ["Invalid email or password."] };

			foreach (var (field, messages) in errors)
			{
				var identifier = new FieldIdentifier(_request, field);
				foreach (var message in messages)
					_messageStore.Add(identifier, message);
			}

			_editContext.NotifyValidationStateChanged();
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
@inject IAuthenticationGateway AuthenticationGateway
@inject NavigationManager Navigation

<PageTitle>Register</PageTitle>

<h1>Register</h1>

<EditForm EditContext="_editContext" OnValidSubmit="HandleRegisterAsync" FormName="authn-register">
	<FluentValidationValidator />
	<ValidationSummary />
	<FluentTextField @bind-Value="_request.Email" Label="Email" Required="true" />
	<FluentTextField @bind-Value="_request.Password" TextFieldType="TextFieldType.Password" Label="Password" Required="true" />
	<FluentButton Type="ButtonType.Submit" Appearance="Appearance.Accent">Register</FluentButton>
</EditForm>

@code {
	readonly RegisterRequest _request = new() { Email = "", Password = "" };
	EditContext _editContext = default!;
	ValidationMessageStore _messageStore = default!;

	protected override void OnInitialized()
	{
		_editContext = new EditContext(_request);
		_messageStore = new ValidationMessageStore(_editContext);
	}

	async Task HandleRegisterAsync()
	{
		_messageStore.Clear();
		var result = await AuthenticationGateway.Register(_request);

		if (!result.Succeeded)
		{
			// Register never produces an empty Errors dictionary on failure — Conflict/Validation are
			// always populated (spec §9.3), unlike Login's anti-enumeration collapse. No fallback needed.
			foreach (var (field, messages) in result.Errors)
			{
				var identifier = new FieldIdentifier(_request, field);
				foreach (var message in messages)
					_messageStore.Add(identifier, message);
			}

			_editContext.NotifyValidationStateChanged();
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
@inject IAuthenticationGateway AuthenticationGateway
@inject NavigationManager Navigation

@code {
	protected override async Task OnInitializedAsync()
	{
		await AuthenticationGateway.Logout(new LogoutRequest());
		Navigation.NavigateTo("/", forceLoad: true);
	}
}
```

- [ ] **Step 6: Manually verify the project builds**

Run: `dotnet build Heimdall/src/AuthN.Components.FluentUI/AuthN.Components.FluentUI.csproj -p:UseProjectReferences=true`
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

- [ ] **Step 8: Stage (session policy: stage only, never commit)**

```bash
cd Heimdall
git add src/AuthN.Components.FluentUI Heimdall.slnx
cd ..
git add Bifrost.slnx
```

---

## SHIP GATE — Heimdall (`AuthN.Components.FluentUI`)

**STOP. Do not start Task 9 until this gate is cleared.**

1. Push the Heimdall commit; open a PR against `master`; confirm CI is green.
2. Merge the PR; push a version tag; confirm `Norse.AuthN.Components.FluentUI` is live on the NuGet feed.
3. Push the Bifrost commit.

Task 9 needs the published `Norse.AuthN.Components.FluentUI` package — that's why it waits for this gate rather than running alongside Task 8.

---

## Task 9: Bragi — stories for `Login`/`Register`/`Logout`

**Files:**
- Modify: `Bragi/src/DesignSystem.Stories/DesignSystem.Stories.csproj`
- Create: `Bragi/src/DesignSystem.Stories/Authentication/Login.stories.razor`
- Create: `Bragi/src/DesignSystem.Stories/Authentication/Register.stories.razor`
- Create: `Bragi/src/DesignSystem.Stories/Authentication/Logout.stories.razor`
- Create: `Yggdrasil/src/Hosting.Stories.Client/FakeAuthenticationGateway.cs`
- Modify: `Yggdrasil/src/Hosting.Stories.Client/Program.cs`

**Interfaces:**
- Consumes: `IAuthenticationGateway`, `LoginRequest`, `RegisterRequest`, `LogoutRequest`, `AuthenticationResult` (Task 5/Task 2, `Norse.AuthN.Components`).
- Consumes: `Login`, `Register`, `Logout` (Task 8, `Norse.AuthN.Components.FluentUI`).

This is content, not behavior — Bragi is exempt from the brainstorm→spec→plan→TDD cycle (`Bragi/CLAUDE.md` §1), so there's no failing-test step here. Bragi is its own composition layer purely for Razor components that render with **no server context** — no real backend call, no `HttpContext`, nothing but the component and its inputs. Story files live directly under `DesignSystem.Stories/`, one subfolder per realm-category matching each story's `[Stories("Category/Name")]` attribute — no intermediate `Stories/` folder, that would just be redundant with the project's own name. As of this plan, that convention is: Asgard's headless primitives (`Abstractions.Components`) live under `DesignSystem.Stories/Primitives/` — the existing `Loader.stories.razor` moved there (from a flat `DesignSystem.Stories/Stories/` layout that predates this convention) as part of adopting it — and Heimdall's `AuthN.Components.FluentUI` components live under `DesignSystem.Stories/Authentication/` (the folder name is the domain word, not the realm/namespace abbreviation — "Authentication," not "AuthN"). Every future realm's stories get their own subfolder the same way; there is no ship gate for this task — nothing later in this plan consumes `Norse.DesignSystem.Stories` — but it still goes through the normal PR/merge Buvy runs by hand, same as any other repo change.

**`Login`/`Register`/`Logout` (Task 8) each inject `IAuthenticationGateway`, never `IAuthenticationService` directly (spec §9.8) — the gateway is what needs a fake here, not the mediator/gRPC service underneath it.** `Yggdrasil/src/Hosting.Stories.Client` (the DI composition root — Bragi itself stays markup/story-wiring only, per its charter) registers a `FakeAuthenticationGateway : IAuthenticationGateway` returning canned `AuthenticationResult` values directly — no `Outcome<T>`, no gRPC, no `CallContext` anywhere in this fake, since none of that vocabulary reaches the component layer even in the real implementations. This is simpler than the pre-`IAuthenticationGateway` design assumed: one interface, one client-safe result type, no channel to simulate.

- [ ] **Step 1: Add the `NorseRef` to `AuthN.Components.FluentUI`**

`Bragi/src/DesignSystem.Stories/DesignSystem.Stories.csproj` — add alongside the existing `NorseRef` to Asgard's `Abstractions.Components`:
```xml
<NorseRef Include="AuthN.Components.FluentUI">
	<Repo>Heimdall</Repo>
</NorseRef>
```

- [ ] **Step 2: Yggdrasil — add `FakeAuthenticationGateway` to the story host**

`Yggdrasil/src/Hosting.Stories.Client/FakeAuthenticationGateway.cs`:
```csharp
using Norse.AuthN.Components;

namespace Norse.Hosting.Stories.Client;

/// <summary>
/// Story-host-only stand-in for <see cref="IAuthenticationGateway"/> — never calls Himinbjörg, never
/// touches gRPC. Exists so Bragi's Login/Register/Logout stories render and are interactive with no
/// server context, per Bragi's charter (content/markup only, no real backend calls from the catalog).
/// </summary>
sealed class FakeAuthenticationGateway : IAuthenticationGateway
{
	static readonly AuthenticationResult Success = new() { Succeeded = true };

	public Task<AuthenticationResult> Login(LoginRequest request) => Task.FromResult(Success);

	public Task<AuthenticationResult> Register(RegisterRequest request) => Task.FromResult(Success);

	public Task<AuthenticationResult> Logout(LogoutRequest request) => Task.FromResult(Success);
}
```

In `Yggdrasil/src/Hosting.Stories.Client/Program.cs`, register it ahead of `builder.Build()`:
```csharp
builder.Services.AddScoped<IAuthenticationGateway, FakeAuthenticationGateway>();
```

- [ ] **Step 3: Write the stories**

`Bragi/src/DesignSystem.Stories/Authentication/Login.stories.razor`:
```razor
@using Norse.AuthN.Components

@attribute [Stories("Authentication/Login")]

<Stories TComponent="Login">
	<Story Name="Default">
		<Template>
			<Login @attributes="context.Args" />
		</Template>
	</Story>
</Stories>
```

`Bragi/src/DesignSystem.Stories/Authentication/Register.stories.razor` and `Logout.stories.razor` follow the same shape, `@attribute [Stories("Authentication/Register")]` / `[Stories("Authentication/Logout")]` respectively.

- [ ] **Step 4: Manually verify the story host builds and renders**

Run: `dotnet build Bragi/src/DesignSystem.Stories/DesignSystem.Stories.csproj -p:UseProjectReferences=true`
Expected: builds clean (0 errors).

Run: `dotnet build Yggdrasil/src/Hosting.Stories.Client -p:UseProjectReferences=true`, then `dotnet run --project Yggdrasil/src/Hosting.Stories.Client --no-build` (or via the Aspire AppHost once `Hosting.Stories.Client` is composed there), open the catalog. Expected: `Authentication/Login`, `Authentication/Register`, `Authentication/Logout` all appear (alongside `Primitives/Loader`) and render without a DI fault. Submitting `Login`'s form should show the success path (`Succeeded = true` navigates away) since the fake always succeeds — that's expected; the fake proves the component renders and wires up, not failure-path behavior (Task 10 proves that against the real backend).

- [ ] **Step 5: Commit**

```bash
cd Bragi
git add src/DesignSystem.Stories/DesignSystem.Stories.csproj src/DesignSystem.Stories/Authentication
git commit -m "feat: add Login/Register/Logout stories for Heimdall's AuthN.Components.FluentUI"
cd ../Yggdrasil
git add src/Hosting.Stories.Client/FakeAuthenticationGateway.cs src/Hosting.Stories.Client/Program.cs
git commit -m "feat: register FakeAuthenticationGateway in the story host"
```

---

## Task 10: End-to-end manual verification

**Files:** none — this task runs the composed system and exercises it by hand. No shortcuts: this is the step that actually proves the bootstrap, not a formality.

- [ ] **Step 1: Start the composed system**

Run: `dotnet run --project src/Orchestration.AppHost`
Expected: Aspire dashboard shows `pg-primary`, `pg-replica`, `migrations` (exits 0), then `web` starts.

- [ ] **Step 2: Verify Blazor Server registration + login (in-process path)**

Open the `web` resource's browser endpoint before any client-side interactivity has taken over (first paint). Navigate to `/authn/register`, submit a new account. Expected: redirected to `/authn/login`. Log in with the same credentials. Expected: redirected to `/`, and the browser's dev tools show a `Set-Cookie` for the ASP.NET Core Identity application cookie.

- [ ] **Step 3: Verify the WASM path takes over and a second login round-trips over gRPC-Web**

With the same browser tab still open (WASM has now hydrated per `InteractiveAuto`), log out via `/authn/logout`, then log back in at `/authn/login` again. Expected: same successful redirect; in the browser's Network tab, confirm the `Login` call is now a `POST` to a gRPC-Web content-type endpoint (`application/grpc-web` or `application/grpc-web-text`) rather than a normal form post, and that the response carries a `Set-Cookie` header the browser accepted (confirmed by the subsequent authenticated navigation succeeding).

- [ ] **Step 4: Verify a locked-out / not-allowed path surfaces its own distinct message**

At `/authn/login`, submit the wrong password enough times to trip Identity's configured lockout threshold for the account created in Step 2. Expected: the triggering attempt shows a distinct **"This account is locked out. Try again later or reset your password."** message (`LoginHandler`'s `ErrorCategory.LockedOut` branch, populated in `Problem.Errors[""]` — see the ledger's post-Task-8 fix), not an unhandled exception and not the generic "Invalid email or password." message. Then, separately, submit a wrong password once (before tripping lockout) against a *different* still-good account. Expected: the generic **"Invalid email or password."** message (`Login.razor`'s own synthesized fallback for the anti-enumeration empty-`Errors` case) — confirming the two paths are now visibly different in the UI, which is the entire point of keeping `LockedOut` distinguishable.

- [ ] **Step 5: Verify the raw protobuf lifecycle in Postman via gRPC reflection**

This exercises `IAuthenticationService` directly over the wire — no Blazor UI, no cookie jar, nothing but the gRPC contract itself. It's the cleanest proof that `Identity.Web.Server`'s forwarder and handlers are correct independent of any client concern. Unlike the pre-`IAuthenticationGateway` design this plan originally assumed, there is no `isSuccess`/`value`/`problem` envelope on the wire — `Register`/`Logout` return nothing on success and an `RpcException` (with a `problem-bin` trailer) on failure; only `Login` has a real success payload (`LoginResult { succeeded: bool }`), and even `Login` throws an `RpcException` for `Validation`/`LockedOut`/`NotAllowed` rather than returning a JSON error body — only the raw wrong-credentials case returns `{ "succeeded": false }` as a normal 200-shaped response (spec §9.3's anti-enumeration collapse; see `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ProblemExtensions.cs` for the exact `ErrorCategory` → `StatusCode` mapping: `Validation` → `InvalidArgument`, `Conflict` → `AlreadyExists`, `LockedOut`/`NotAllowed` → `PermissionDenied`).

1. In Postman, create a new gRPC request against the `web` resource's endpoint (check the Aspire dashboard for the exact `https://localhost:{port}` address).
2. Use "Server reflection" as the import method (not "Import a .proto file") — Postman calls the reflection service Task 5 wired and lists `Norse.AuthN.Components.IAuthenticationService` with its three methods.
3. Call `Register` with a JSON body `{ "email": "postman-test@example.com", "password": "correct-horse-battery" }`. Expected: the call completes with an **empty response body** (`Register` returns `Task`, not `Task<T>`) and gRPC status `OK`.
4. Call `Register` again with the **same** body. Expected: the call **fails** with gRPC status `AlreadyExists` (`StatusCode.AlreadyExists`) and a status detail of `"Conflict"` (the `ErrorCategory` name, per `ToRpcException`'s `Status(status, problem.Category.ToString())`) — check Postman's trailers/metadata panel for a `problem-bin` binary trailer containing the serialized errors dictionary. This proves `RegisterHandler`'s duplicate-email path is reachable over the real wire, not just in the unit test from Task 4.
5. Call `Login` with the same credentials. Expected: a normal `OK` response with body `{ "succeeded": true }`.
6. Call `Login` with the wrong password once. Expected: still `OK` status, but body `{ "succeeded": false }` — this is the deliberate anti-enumeration case, not an error at the protocol level.
7. Call `Login` with the wrong password repeatedly until Identity's lockout threshold trips. Expected: the triggering call **fails** with gRPC status `PermissionDenied` and status detail `"LockedOut"`, with a `problem-bin` trailer whose decoded JSON is `{"": ["This account is locked out. Try again later or reset your password."]}` — the same message confirmed through the browser in Step 4, now confirmed at the protocol level with nothing else in the way.
8. Call `Logout` with an empty body (`{}`). Expected: empty response body, gRPC status `OK`. Note that a bare Postman gRPC call carries no cookie jar by default — this call proves the RPC completes cleanly, not that it cleared a specific browser session; Steps 2–4 of the browser walkthrough above are what prove the cookie side of the lifecycle.

If reflection doesn't list the service, don't guess — confirm `AddGrpcReflection()`/`MapGrpcReflectionService()` actually ran (`app.Environment.IsDevelopment()` must be true; both are already wired in `Hosting.Web.Server/Program.cs`) before assuming Postman or the contract is at fault.

- [ ] **Step 6: Record the result**

If every check in Steps 2–5 passes, this bootstrap slice is proven end to end. If anything fails, treat it as a bug against the specific task above (per `superpowers:systematic-debugging`) — do not patch around it inline without understanding which task's assumption broke.

---

## What's Deliberately Deferred (not part of this plan)

- `IAccountService`'s full lifecycle surface (`ChangePassword` through `DeletePersonalData`) — `Heimdall/specs/2026-07-13-authn-identity-split-design.md` §7, step 3.
- Yggdrasil's old `Components/Account/*` scaffold — untouched, still serves its own routes; the cutover is its own future plan (design doc §7, step 5).
- The mediator's generic `ISender`/pipeline-behavior/source-generator machinery (`2026-05-26-mediator-design.md`) — this plan hand-wires each handler directly into the gRPC forwarder's constructor instead. Building the generic dispatcher is real future work once this pattern's proven across more than three operations.
- OpenIddict's authorization-server endpoints for MAUI's Auth Code + PKCE flow — separate future spec, per the design doc §6.
