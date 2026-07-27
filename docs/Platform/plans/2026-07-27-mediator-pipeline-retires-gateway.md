# Mediator Pipeline Retires the Gateway — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (default) or `superpowers:executing-plans` (separate-session fallback, never interchangeable). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the generated three-mode gateway machinery with a hand-rolled, Bogard-classic mediator pipeline composed once in DI, per `../specs/2026-07-27-mediator-pipeline-retires-gateway-design.md` — and make the gRPC wire path actually work (interceptors registered, surrogates registered, gRPC-Web enabled) for the first time.

**Architecture:** Five realms in strict ship-gate order. Asgard declares the request markers, envelope-native `IRequestHandler`, `ISender`/`ISenderDispatch`, `IPrincipalAccessor`, deletes the gateway machinery, and ships the handler-registration generator. Midgard re-plumbs the four behaviors for DI, ships the hand-rolled `Sender` + `AddNorsePipeline()`, the principal-seeding/outcome/unhandled interceptor stack, the client decoder interceptor, and the two gRPC wiring generators. Heimdall moves policy onto the request records and re-points components at `IAuthenticationService`. Himinbjörg's service becomes three `Send` calls. Yggdrasil adopts the generated wiring, adds the `ErrorBoundary`/`CircuitHandler` net, and proves parity end to end.

**Tech Stack:** .NET 11 preview / C# 15, protobuf-net.Grpc + protobuf-net.Grpc.AspNetCore 1.2.x, FluentValidation 12, Roslyn `IIncrementalGenerator`, xUnit v3 + Shouldly + NSubstitute on MTP v2.

## Global Constraints

- House rules read and binding (`../../house-rules.md`): tabs; target-typed `new()`; `var` for return assignments; collection expressions; primary constructors; expression bodies arrow-on-declaration-line; `is null`/`is not null`; fluent chains; `sealed`/`abstract`/`static` classes; omit default accessibility; `LoggerMessage` delegates only; `ConfigureAwait(false)` in src/gen, never tests; every async method takes `CancellationToken cancellationToken = default` last and propagates it; XML docs on src public surface; one `PropertyGroup`/`ItemGroup` per csproj, alphabetized.
- Generators: netstandard2.0, emit via `CSharpEmit.AppendCSharp` raw-string templates, write with `Utf8NoBom.Encoding`, fully-qualified (`global::`) type names in emitted code, `#pragma warning disable CS1591` in generated headers, compiled-symbol walks (never syntax) for anything crossing assembly boundaries.
- Tests: Shouldly + NSubstitute, `public sealed` test classes, bare `void`/`async Task` methods, sentence-shaped_underscore names.
- No automatic pushes; subagents commit on the realm's local `feature/mediator-pipeline` branch only (Buvy merges/ships). No force-push, no `--no-verify`, no secrets.
- `NorseRef` for cross-realm references, plain `<ProjectReference>` same-realm. Package versions `Version="11.*-*"` for framework-tracking, `Version="N.*"` otherwise.
- **Breaking-change reality:** pre-launch, no external consumers. After Asgard's phase, the Bifröst-wide dev-mode (`UseProjectReferences=true`) build is **expected red** until Yggdrasil's phase completes. Each realm's own `.slnx` must be green at its own ship gate — that is the gate, not the cross-realm build.
- Ship gate = PR merged, CI green, tag pushed, NuGet package(s) live, before the next phase starts.
- **Wired, not just designed (spec acceptance policy, binding):** every registration this plan mandates — interceptors, surrogates, pipeline, dispatch map, generated wiring — gets a test that **fails when the registration is removed**. `OutcomeServerInterceptor` sat implemented, unit-tested, documented, and dead for three days because nothing asserted its presence in composition. Never again is a test away.
- `Outcome<T>` inspection is always `TryGetValue`/`Match`/pattern-match on `Success<T>`/`Failed` — never a boolean flag; `Success<T>` comes from `Norse.Primitives`.

---

## File Map

### Asgard
| Action | Path |
|---|---|
| Create | `Asgard/src/Abstractions.Contracts/IRequest.cs` |
| Create | `Asgard/src/Abstractions.Contracts/ICommandRequest.cs` |
| Create | `Asgard/src/Abstractions.Contracts/IQueryRequest.cs` |
| Delete | `Asgard/src/Abstractions.Web.Server/Mediator/ICommandRequest.cs` |
| Modify | `Asgard/src/Abstractions.Web.Server/Mediator/IRequestHandler.cs` (envelope-native realignment) |
| Create | `Asgard/src/Abstractions.Web.Server/Mediator/ISender.cs` |
| Create | `Asgard/src/Abstractions.Web.Server/Mediator/ISenderDispatch.cs` |
| Create | `Asgard/src/Abstractions.Web.Server/Mediator/SenderDispatch.cs` |
| Create | `Asgard/src/Abstractions.Web.Server/Mediator/IPrincipalAccessor.cs` |
| Delete | `Asgard/src/Abstractions.Web.Server/Mediator/BehaviorAttribute.cs` |
| Delete | `Asgard/src/Abstractions.Contracts/GenerateGatewayAttribute.cs` |
| Delete | `Asgard/gen/Abstractions.Contracts.Generator/` (entire project) |
| Delete | `Asgard/tests/Abstractions.Contracts.Generator.Tests/` (entire project) |
| Modify | `Asgard/src/Abstractions.Contracts/Abstractions.Contracts.csproj` (drop analyzer ref + packaging target) |
| Create | `Asgard/gen/Abstractions.Web.Server.Generator/Abstractions.Web.Server.Generator.csproj` |
| Create | `Asgard/gen/Abstractions.Web.Server.Generator/HandlerRegistrationGenerator.cs` |
| Create | `Asgard/gen/Abstractions.Web.Server.Generator/HandlerModel.cs` |
| Create | `Asgard/gen/Abstractions.Web.Server.Generator/RegistrationEmitter.cs` |
| Modify | `Asgard/src/Abstractions.Web.Server/Abstractions.Web.Server.csproj` (analyzer ref + packaging target) |
| Create | `Asgard/tests/Abstractions.Web.Server.Generator.Tests/` (project + tests) |
| Tests | `Asgard/tests/Abstractions.Contracts.Tests/RequestMarkerTests.cs`, `Asgard/tests/Abstractions.Web.Server.Tests/SenderDispatchTests.cs` |

### Midgard
| Action | Path |
|---|---|
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/PrincipalAccessor.cs` |
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/PolicyCache.cs` |
| Modify | `Midgard/src/Infrastructure.Web.Server/Mediator/AuthorizationBehavior.cs` |
| Modify | `Midgard/src/Infrastructure.Web.Server/Mediator/ValidationBehavior.cs` |
| Modify | `Midgard/src/Infrastructure.Web.Server/Mediator/TelemetryBehavior.cs` + `ExceptionTranslationBehavior.cs` (doc comments only) |
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/Sender.cs` |
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/SenderDispatchMap.cs` |
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/ServiceCollectionExtensions.cs` (`AddNorsePipeline`) |
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/PrincipalSeedingInterceptor.cs` |
| Modify | `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ServiceCollectionExtensions.cs` (three interceptors) |
| Modify | `Midgard/src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj` (delete IVT grant) |
| Create | `Midgard/src/Infrastructure.Web.Client/Grpc/OutcomeClientInterceptor.cs` |
| Create | `Midgard/src/Infrastructure.Web.Client/Grpc/OutcomeFactory.cs` |
| Create | `Midgard/gen/Infrastructure.Web.Server.Generator/` (project, `GrpcServerRegistrationGenerator.cs`, `ServerRegistrationEmitter.cs`, `ContractDiscovery.cs`) |
| Create | `Midgard/gen/Infrastructure.Web.Client.Generator/` (project, `GrpcClientRegistrationGenerator.cs`, `ClientRegistrationEmitter.cs`) |
| Modify | both `Infrastructure.Web.*.csproj` (analyzer refs + packaging targets) |
| Tests | mirrors under `Midgard/tests/` per project |

### Heimdall
| Action | Path |
|---|---|
| Modify | `Heimdall/src/AuthN.Services/IAuthenticationService.cs` (drop `[GenerateGateway]`, add CT params) |
| Modify | `Heimdall/src/AuthN.Services/{LoginRequest,RegisterRequest,LogoutRequest}.cs` (markers + `[Authorize]`) |
| Modify | `Heimdall/src/AuthN.Services/AuthN.Services.csproj` (drop `NorseGatewayEmissionMode`) |
| Modify | `Heimdall/src/AuthN.Components.FluentUI/{Login,Register}.razor`, `Heimdall/src/AuthN.Components/Logout.razor` |
| Modify | `Heimdall/tests/AuthN.Components.Tests/` (substitute `IAuthenticationService`) |

### Himinbjörg
| Action | Path |
|---|---|
| Modify | `Himinbjorg/src/Identity.Web.Server/{Login,Register,Logout}Handler.cs` |
| Modify | `Himinbjorg/src/Identity.Web.Server/AuthenticationService.cs` |
| Modify | `Himinbjorg/src/Identity.Web.Server/ServiceCollectionExtensions.cs` |
| Modify | `Himinbjorg/src/Identity.Web.Server/Identity.Web.Server.csproj` (generator NorseRef) |

### Yggdrasil
| Action | Path |
|---|---|
| Modify | `Yggdrasil/src/Hosting.Web.Server/Program.cs` + `Hosting.Web.Server.csproj` |
| Modify | `Yggdrasil/src/Hosting.Web.Client/Program.cs` + `Hosting.Web.Client.csproj` |
| Delete | `Yggdrasil/src/Hosting.Web.Server/Generated/`, `Yggdrasil/src/Hosting.Web.Client/Generated/` |
| Delete | `Yggdrasil/src/Hosting.Web.Server/EnvelopeHydrationState.cs` + its tests |
| Modify | `Yggdrasil/src/Hosting.Stories.Client/FakeAuthenticationGateway.cs` → `FakeAuthenticationService.cs` |
| Modify | `Yggdrasil/src/Hosting.Web.Components/Layout/MainLayout.razor` (ErrorBoundary) |
| Create | `Yggdrasil/src/Hosting.Web.Server/LoggingCircuitHandler.cs` |
| Create | `Yggdrasil/tests/Hosting.Web.Server.Tests/MediatorParityTests.cs` |

---

## Task 1: Asgard — Request marker family in `Abstractions.Contracts`

**Files:**
- Create: `Asgard/src/Abstractions.Contracts/IRequest.cs`, `ICommandRequest.cs`, `IQueryRequest.cs`
- Delete: `Asgard/src/Abstractions.Web.Server/Mediator/ICommandRequest.cs`
- Test: `Asgard/tests/Abstractions.Contracts.Tests/RequestMarkerTests.cs`

**Interfaces:**
- Produces: `Norse.Abstractions.Contracts.IRequest<TResponse> where TResponse : notnull` (neutral base `Send` accepts), `ICommandRequest<TResponse> : IRequest<TResponse>`, `IQueryRequest<TResponse> : IRequest<TResponse>`. `TResponse` is the **handler payload** type, never `Outcome<T>` — the pipeline owns the envelope. WASM-safe: `Abstractions.Contracts` already ships to the browser.
- The old `Norse.Abstractions.Web.Server.Mediator.ICommandRequest<TResponse>` (server-only assembly, unused) is deleted, not moved — the namespace changes, and Task 10 attaches the new markers to Heimdall's wire records, which the old placement structurally forbade.

- [ ] **Step 1: Write the failing test**

```csharp
// Asgard/tests/Abstractions.Contracts.Tests/RequestMarkerTests.cs
using Norse.Abstractions.Contracts;

namespace Norse.Abstractions.Contracts.Tests;

public sealed class RequestMarkerTests
{
	sealed record FakePayload;
	sealed record FakeCommand : ICommandRequest<FakePayload>;
	sealed record FakeQuery : IQueryRequest<FakePayload>;

	[Fact]
	void Command_and_query_markers_both_derive_from_the_neutral_request_marker()
	{
		typeof(IRequest<FakePayload>).IsAssignableFrom(typeof(FakeCommand)).ShouldBeTrue();
		typeof(IRequest<FakePayload>).IsAssignableFrom(typeof(FakeQuery)).ShouldBeTrue();
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Tests --filter RequestMarkerTests`
Expected: FAIL — `IRequest<>` does not exist (build error).

- [ ] **Step 3: Implement the three markers, delete the old one**

```csharp
// Asgard/src/Abstractions.Contracts/IRequest.cs
namespace Norse.Abstractions.Contracts;

/// <summary>
/// The neutral marker every mediator-dispatched request implements, via one of its two derived
/// markers — <see cref="ICommandRequest{TResponse}"/> or <see cref="IQueryRequest{TResponse}"/>.
/// <typeparamref name="TResponse"/> is the handler's <b>payload</b> type; the pipeline wraps it in
/// <see cref="Outcome{T}"/> — request types never name the envelope.
/// </summary>
/// <typeparam name="TResponse">The success payload the request's handler produces.</typeparam>
public interface IRequest<TResponse> where TResponse : notnull;
```

```csharp
// Asgard/src/Abstractions.Contracts/ICommandRequest.cs
namespace Norse.Abstractions.Contracts;

/// <summary>
/// A state-changing request. The command/query split carries no behavioral difference in v1 — it
/// exists so a future behavior (a transaction behavior being the obvious tenant) can bind to one
/// side only without re-marking every request on the platform.
/// </summary>
/// <typeparam name="TResponse">The success payload the request's handler produces.</typeparam>
public interface ICommandRequest<TResponse> : IRequest<TResponse> where TResponse : notnull;
```

```csharp
// Asgard/src/Abstractions.Contracts/IQueryRequest.cs
namespace Norse.Abstractions.Contracts;

/// <summary>A side-effect-free read request. See <see cref="ICommandRequest{TResponse}"/> for why the split exists.</summary>
/// <typeparam name="TResponse">The success payload the request's handler produces.</typeparam>
public interface IQueryRequest<TResponse> : IRequest<TResponse> where TResponse : notnull;
```

```bash
git rm Asgard/src/Abstractions.Web.Server/Mediator/ICommandRequest.cs
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Tests --filter RequestMarkerTests`
Expected: PASS.

- [ ] **Step 5: Build the full Asgard solution — expect the `IRequestHandler` doc-comment reference to the deleted `ICommandRequest` to fail; fix only that reference (the interface body changes in Task 2)**

Run: `dotnet build Asgard/Asgard.slnx`
Fix: in `IRequestHandler.cs`'s `<summary>`, drop the sentence referencing `ICommandRequest{TResponse}` (the whole comment is rewritten in Task 2 anyway).

- [ ] **Step 6: Commit**

```bash
git add -A Asgard/src Asgard/tests
git commit -m "feat: declare IRequest/ICommandRequest/IQueryRequest markers in Abstractions.Contracts"
```

---

## Task 2: Asgard — Envelope-native `IRequestHandler`, `ISender`, `SenderDispatch`, `IPrincipalAccessor`

**Files:**
- Modify: `Asgard/src/Abstractions.Web.Server/Mediator/IRequestHandler.cs`
- Create: `Asgard/src/Abstractions.Web.Server/Mediator/ISender.cs`, `ISenderDispatch.cs`, `SenderDispatch.cs`, `IPrincipalAccessor.cs`
- Delete: `Asgard/src/Abstractions.Web.Server/Mediator/BehaviorAttribute.cs` + `Asgard/tests/Abstractions.Web.Server.Tests/BehaviorAttributeTests.cs`
- Test: `Asgard/tests/Abstractions.Web.Server.Tests/SenderDispatchTests.cs`

**Interfaces:**
- Consumes: `IRequest<TResponse>` (Task 1), `IBehavior<,>`/`BehaviorDelegate<>` (unchanged, already shipped), `Outcome<T>`.
- Produces (exact signatures later tasks rely on):
  - `IRequestHandler<in TRequest, TResponse>.Handle(TRequest, CancellationToken = default) : ValueTask<Outcome<TResponse>>` with `where TRequest : IRequest<TResponse> where TResponse : notnull` — **breaking realignment**: `TResponse` is now the payload; handlers stop closing over `Outcome<...>` themselves.
  - `ISender.Send<TResponse>(IRequest<TResponse>, CancellationToken = default) : ValueTask<Outcome<TResponse>>`.
  - `ISenderDispatch { Type RequestType { get; } }` and `ISenderDispatch<TResponse>.Dispatch(IServiceProvider, IRequest<TResponse>, CancellationToken = default) : ValueTask<Outcome<TResponse>>`.
  - `SenderDispatch<TRequest, TResponse>` — the closed-generic fold: resolves the handler and `IEnumerable<IBehavior<TRequest, TResponse>>` from the scoped provider and folds **first-registered = outermost**. `public` (generated registrations in service realms construct it via DI).
  - `IPrincipalAccessor.GetPrincipalAsync(CancellationToken = default) : ValueTask<ClaimsPrincipal>`.
- `BehaviorAttribute` deleted — the extension seam is now behavior registration order (spec §2.2).

- [ ] **Step 1: Write the failing tests — fold order, empty chain, missing handler**

```csharp
// Asgard/tests/Abstractions.Web.Server.Tests/SenderDispatchTests.cs
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.Primitives;

namespace Norse.Abstractions.Web.Server.Tests;

public sealed class SenderDispatchTests
{
	sealed record Ping : IQueryRequest<string>;

	sealed class PingHandler : IRequestHandler<Ping, string>
	{
		public ValueTask<Outcome<string>> Handle(Ping request, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(Outcome<string>.Ok("pong"));
	}

	sealed class TaggingBehavior(string tag, List<string> log) : IBehavior<Ping, string>
	{
		public async ValueTask<Outcome<string>> Handle(Ping request, BehaviorDelegate<string> next, CancellationToken cancellationToken = default)
		{
			log.Add($"{tag}:in");
			var outcome = await next();
			log.Add($"{tag}:out");
			return outcome;
		}
	}

	[Fact]
	async Task Folds_behaviors_first_registered_outermost_around_the_handler()
	{
		List<string> log = [];
		var services = new ServiceCollection()
			.AddScoped<IRequestHandler<Ping, string>, PingHandler>()
			.AddScoped<IBehavior<Ping, string>>(_ => new TaggingBehavior("first", log))
			.AddScoped<IBehavior<Ping, string>>(_ => new TaggingBehavior("second", log))
			.BuildServiceProvider();

		SenderDispatch<Ping, string> dispatch = new();
		var outcome = await dispatch.Dispatch(services, new Ping(), CancellationToken.None);

		outcome.TryGetValue(out Success<string> success).ShouldBeTrue();
		success.Value.ShouldBe("pong");
		log.ShouldBe(["first:in", "second:in", "second:out", "first:out"]);
	}

	[Fact]
	async Task Dispatches_straight_to_the_handler_when_no_behaviors_are_registered()
	{
		var services = new ServiceCollection()
			.AddScoped<IRequestHandler<Ping, string>, PingHandler>()
			.BuildServiceProvider();

		SenderDispatch<Ping, string> dispatch = new();
		var outcome = await dispatch.Dispatch(services, new Ping(), CancellationToken.None);

		outcome.TryGetValue(out Success<string> success).ShouldBeTrue();
		success.Value.ShouldBe("pong");
	}

	[Fact]
	async Task Fails_loudly_when_the_handler_registration_is_missing()
	{
		var services = new ServiceCollection().BuildServiceProvider();
		SenderDispatch<Ping, string> dispatch = new();
		await Should.ThrowAsync<InvalidOperationException>(async () =>
			await dispatch.Dispatch(services, new Ping(), CancellationToken.None));
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Asgard/tests/Abstractions.Web.Server.Tests --filter SenderDispatchTests`
Expected: FAIL — `SenderDispatch` does not exist.

- [ ] **Step 3: Implement**

`IRequestHandler.cs` — full replacement:

```csharp
using Norse.Abstractions.Contracts;

namespace Norse.Abstractions.Web.Server.Mediator;

/// <summary>
/// Handles a single request, producing the payload wrapped in the platform envelope. The whole
/// chain — <see cref="ISender"/>, <see cref="IBehavior{TRequest,TResponse}"/>, this interface —
/// speaks one type algebra: <typeparamref name="TResponse"/> is the <b>payload</b>, the pipeline
/// owns the <see cref="Outcome{T}"/>. Handlers never validate, authorize, or catch-to-translate —
/// the behaviors composed around them by <c>AddNorsePipeline()</c> (Midgard) do.
/// </summary>
public interface IRequestHandler<in TRequest, TResponse>
	where TRequest : IRequest<TResponse>
	where TResponse : notnull
{
	/// <summary>Handles the given request.</summary>
	ValueTask<Outcome<TResponse>> Handle(TRequest request, CancellationToken cancellationToken = default);
}
```

```csharp
// Asgard/src/Abstractions.Web.Server/Mediator/ISender.cs
using Norse.Abstractions.Contracts;

namespace Norse.Abstractions.Web.Server.Mediator;

/// <summary>
/// Dispatches a request through the composed behavior chain to its handler — the platform's
/// hand-rolled, MediatR-familiar seam (spec §2.2). One implementation lives in Midgard; callers
/// (service implementations, never components) constructor-inject this and stay channel-dumb.
/// </summary>
public interface ISender
{
	/// <summary>Sends the request through the pipeline and returns the enveloped payload.</summary>
	ValueTask<Outcome<TResponse>> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
		where TResponse : notnull;
}
```

```csharp
// Asgard/src/Abstractions.Web.Server/Mediator/ISenderDispatch.cs
using Norse.Abstractions.Contracts;

namespace Norse.Abstractions.Web.Server.Mediator;

/// <summary>
/// One request type's entry in the sender's dispatch map. Registered (as a singleton) by each
/// realm's generated <c>AddNorse*Handlers()</c> — compile-time dispatch, no reflection, no
/// assembly scanning (spec §2.7).
/// </summary>
public interface ISenderDispatch
{
	/// <summary>The concrete request type this entry dispatches.</summary>
	Type RequestType { get; }
}

/// <summary>The response-typed half of <see cref="ISenderDispatch"/>, invoked by the sender.</summary>
public interface ISenderDispatch<TResponse> : ISenderDispatch where TResponse : notnull
{
	/// <summary>Resolves the handler and behaviors from <paramref name="services"/> (the caller's scope) and runs the fold.</summary>
	ValueTask<Outcome<TResponse>> Dispatch(IServiceProvider services, IRequest<TResponse> request, CancellationToken cancellationToken = default);
}
```

```csharp
// Asgard/src/Abstractions.Web.Server/Mediator/SenderDispatch.cs
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Contracts;

namespace Norse.Abstractions.Web.Server.Mediator;

/// <summary>
/// The closed-generic dispatch entry: resolves <see cref="IRequestHandler{TRequest,TResponse}"/>
/// and every <see cref="IBehavior{TRequest,TResponse}"/> from the scoped provider and folds them
/// around the handler, <b>first-registered outermost</b> — registration order in
/// <c>AddNorsePipeline()</c> is the chain order, and it is law (spec §2.2). A missing handler
/// registration fails loudly here (<see cref="ServiceProviderServiceExtensions.GetRequiredService{T}(IServiceProvider)"/>),
/// never silently no-ops. Stateless; registered as a singleton by generated code.
/// </summary>
public sealed class SenderDispatch<TRequest, TResponse> : ISenderDispatch<TResponse>
	where TRequest : IRequest<TResponse>
	where TResponse : notnull
{
	/// <inheritdoc />
	public Type RequestType =>
		typeof(TRequest);

	/// <inheritdoc />
	public ValueTask<Outcome<TResponse>> Dispatch(IServiceProvider services, IRequest<TResponse> request, CancellationToken cancellationToken = default)
	{
		var handler = services.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
		var typed = (TRequest)request;
		BehaviorDelegate<TResponse> next = () => handler.Handle(typed, cancellationToken);
		foreach (var behavior in services.GetServices<IBehavior<TRequest, TResponse>>().Reverse())
		{
			var current = next;
			next = () => behavior.Handle(typed, current, cancellationToken);
		}

		return next();
	}
}
```

```csharp
// Asgard/src/Abstractions.Web.Server/Mediator/IPrincipalAccessor.cs
using System.Security.Claims;

namespace Norse.Abstractions.Web.Server.Mediator;

/// <summary>
/// The pipeline's ambient principal source — the Bogard scoped-context pattern, typed (spec §2.4).
/// Each channel adapter supplies it at entry: Midgard's gRPC seeding interceptor stamps the request
/// principal; inside a circuit the implementation defers to <c>AuthenticationStateProvider</c> live
/// (a circuit outlives login/logout, so an eagerly seeded value would go stale). Resolving a
/// principal in a scope no channel adapter prepared fails loudly — never a silent anonymous.
/// </summary>
public interface IPrincipalAccessor
{
	/// <summary>Gets the current caller's principal.</summary>
	ValueTask<ClaimsPrincipal> GetPrincipalAsync(CancellationToken cancellationToken = default);
}
```

Delete the attribute and its test:

```bash
git rm Asgard/src/Abstractions.Web.Server/Mediator/BehaviorAttribute.cs Asgard/tests/Abstractions.Web.Server.Tests/BehaviorAttributeTests.cs
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Asgard/tests/Abstractions.Web.Server.Tests --filter SenderDispatchTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Run the full Asgard suite**

Run: `dotnet test Asgard/Asgard.slnx`
Expected: FAIL is acceptable **only** in `Abstractions.Contracts.Generator.Tests` (the generator still references `GenerateGatewayAttribute` and is deleted next task). Everything else PASS.

- [ ] **Step 6: Commit**

```bash
git add -A Asgard/src Asgard/tests
git commit -m "feat: envelope-native IRequestHandler, ISender/SenderDispatch fold, IPrincipalAccessor; retire BehaviorAttribute"
```

---

## Task 3: Asgard — Delete the gateway machinery

**Files:**
- Delete: `Asgard/src/Abstractions.Contracts/GenerateGatewayAttribute.cs`
- Delete: `Asgard/gen/Abstractions.Contracts.Generator/` (entire project)
- Delete: `Asgard/tests/Abstractions.Contracts.Generator.Tests/` (entire project)
- Modify: `Asgard/src/Abstractions.Contracts/Abstractions.Contracts.csproj`, `Asgard/Asgard.slnx`, `Asgard/CLAUDE.md`, `Asgard/README.md`

**Interfaces:**
- Consumes: nothing. Produces: absence. `Abstractions.Emit` and `gen/Directory.Build.props` **stay** — Task 4 and Midgard's Task 9 generators build on them.

- [ ] **Step 1: Delete the generator, its tests, and the trigger attribute**

```bash
git rm -r Asgard/gen/Abstractions.Contracts.Generator Asgard/tests/Abstractions.Contracts.Generator.Tests
git rm Asgard/src/Abstractions.Contracts/GenerateGatewayAttribute.cs
```

- [ ] **Step 2: Strip the packaging from `Abstractions.Contracts.csproj`**

Remove the `<ProjectReference ... OutputItemType="Analyzer" ...>` item and the entire `IncludeGeneratorInPackage` target. Update `<Description>` to: `Norse declared law: Outcome<T> (the platform's interior discriminated union — Success<T>/Failed(Problem); doctrine at Glitnir's the-two-unions.md), Problem/ErrorCategory/BoolResponse/Unit, and the IRequest/ICommandRequest/IQueryRequest marker family. The single assembly other product contexts reference from Norse.Abstractions.`

- [ ] **Step 3: Remove both deleted projects from `Asgard.slnx`; build and test**

Run: `dotnet build Asgard/Asgard.slnx && dotnet test Asgard/Asgard.slnx`
Expected: green — nothing left references the gateway.

- [ ] **Step 4: Update `Asgard/CLAUDE.md` + `Asgard/README.md`** — remove every `GatewayGenerator`/`GenerateGateway`/emission-mode/`NorseGatewayEmissionMode` claim (including the "known gap, not yet started" `CompilerVisibleProperty` item — the gap dissolves with the property); describe the mediator law surface as: markers (Contracts), `IRequestHandler`/`ISender`/`ISenderDispatch`/`IBehavior`/`IPrincipalAccessor` (Web.Server), and the Task 4 registration generator. Reference the design: `../Glitnir/docs/Platform/specs/2026-07-27-mediator-pipeline-retires-gateway-design.md`.

- [ ] **Step 5: Commit**

```bash
git add -A Asgard
git commit -m "feat!: delete GatewayGenerator, GenerateGatewayAttribute, and all three emission modes"
```

---

## Task 4: Asgard — Handler-registration generator (`Abstractions.Web.Server.Generator`)

**Files:**
- Create: `Asgard/gen/Abstractions.Web.Server.Generator/Abstractions.Web.Server.Generator.csproj`, `HandlerRegistrationGenerator.cs`, `HandlerModel.cs`, `RegistrationEmitter.cs`
- Modify: `Asgard/src/Abstractions.Web.Server/Abstractions.Web.Server.csproj`
- Create: `Asgard/tests/Abstractions.Web.Server.Generator.Tests/Abstractions.Web.Server.Generator.Tests.csproj`, `HandlerRegistrationGeneratorTests.cs` (+ copy `ReferenceAssemblies.cs` helper pattern from the deleted generator tests' git history: `git show HEAD~1:tests/Abstractions.Contracts.Generator.Tests/ReferenceAssemblies.cs`)

**Interfaces:**
- Consumes: `IRequestHandler<,>` (Task 2), `SenderDispatch<,>`/`ISenderDispatch` (Task 2), `FluentValidation.IValidator<T>`, `Microsoft.AspNetCore.Authorization.AuthorizeAttribute`.
- Produces: one generated file per consuming assembly, `NorseHandlerRegistration.g.cs`, declaring `public static IServiceCollection AddNorse{AssemblyNameWithoutDots}Handlers(this IServiceCollection services)` — e.g. assembly `Norse.Identity.Web.Server` → `AddNorseIdentityWebServerHandlers()`. Per discovered handler: a scoped `IRequestHandler<TReq,TRes>` registration, a singleton `ISenderDispatch` → `SenderDispatch<TReq,TRes>` registration; per request type, a scoped `IValidator<TReq>` registration for every implementation found in the compiling assembly **or any referenced assembly** (Heimdall's validators serve Himinbjörg's handlers).
- Diagnostics (`Norse.Mediator` category, all errors): **NORSE010** — two handlers for the same request type; **NORSE011** — a handled request type carries no `[Authorize(Policy = ...)]` with a non-empty policy (compile-time arm of spec §2.5; Midgard's `PolicyCache` is the runtime backstop).
- **NORSE010 is per-assembly by design** (2026-07-27 review, priced not accidental): a *cross-realm* duplicate handler escapes the compiler — two realms each legally register a `SenderDispatch` for the same request type — and is caught at startup by `SenderDispatchMap`'s `ToFrozenDictionary` throwing on the duplicate key (Task 6). That is the chosen loud backstop; do not add cross-assembly discovery here to close it.
- Discovery: handlers from the **compiling assembly only** (registration is a realm-local act; the generated code references `internal` handler types legally). Validators via compiled-symbol walk of `[compilation.Assembly, .. compilation.SourceModule.ReferencedAssemblySymbols]`.

- [ ] **Step 1: Create the generator project**

```xml
<!-- Asgard/gen/Abstractions.Web.Server.Generator/Abstractions.Web.Server.Generator.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Abstractions.Web.Server.Generator: discovers a realm's IRequestHandler and IValidator implementations at compile time and emits the AddNorse{Realm}Handlers() registration extension — handler, dispatch-map, and validator registrations, replacing assembly scanning with compile-time wiring. Bundled into Abstractions.Web.Server's package (analyzers/dotnet/cs/), never referenced or packed standalone.</Description>
		<GetTargetPathDependsOn>$(GetTargetPathDependsOn);_NorseIncludeEmitDependencyTargetPath</GetTargetPathDependsOn>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../../src/Abstractions.Emit/Abstractions.Emit.csproj" />
	</ItemGroup>
</Project>
```

(The `_NorseIncludeEmitDependencyTargetPath` target itself is already hoisted in `Asgard/gen/Directory.Build.props` — the property line above is all the csproj needs.)

- [ ] **Step 2: Write the failing generator tests**

```csharp
// Asgard/tests/Abstractions.Web.Server.Generator.Tests/HandlerRegistrationGeneratorTests.cs
// Test harness pattern: CSharpGeneratorDriver over a compilation assembled from source text +
// ReferenceAssemblies (recover the helper from git history, step header above). Assembly name of
// the test compilation: "Norse.Identity.Web.Server" so the emitted method name is deterministic.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Norse.Abstractions.Web.Server.Generator;

namespace Norse.Abstractions.Web.Server.Generator.Tests;

public sealed class HandlerRegistrationGeneratorTests
{
	const string Contract = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Contracts;
		using Norse.Abstractions.Web.Server.Mediator;
		using FluentValidation;

		namespace Norse.Identity.Web.Server;

		[Authorize(Policy = "AuthN.Public")]
		public sealed record LoginRequest : ICommandRequest<BoolResponse>;

		sealed class LoginHandler : IRequestHandler<LoginRequest, BoolResponse>
		{
			public ValueTask<Outcome<BoolResponse>> Handle(LoginRequest request, CancellationToken cancellationToken = default) =>
				ValueTask.FromResult(Outcome<BoolResponse>.Ok(new BoolResponse { Value = true }));
		}

		public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>;
		""";

	[Fact]
	void Emits_handler_dispatch_and_validator_registrations_named_for_the_assembly()
	{
		var generated = Generate(Contract);
		generated.ShouldContain("AddNorseIdentityWebServerHandlers");
		generated.ShouldContain("AddScoped<global::Norse.Abstractions.Web.Server.Mediator.IRequestHandler<global::Norse.Identity.Web.Server.LoginRequest, global::Norse.Abstractions.Contracts.BoolResponse>, global::Norse.Identity.Web.Server.LoginHandler>");
		generated.ShouldContain("AddSingleton<global::Norse.Abstractions.Web.Server.Mediator.ISenderDispatch, global::Norse.Abstractions.Web.Server.Mediator.SenderDispatch<global::Norse.Identity.Web.Server.LoginRequest, global::Norse.Abstractions.Contracts.BoolResponse>>");
		generated.ShouldContain("AddScoped<global::FluentValidation.IValidator<global::Norse.Identity.Web.Server.LoginRequest>, global::Norse.Identity.Web.Server.LoginRequestValidator>");
	}

	[Fact]
	void NORSE011_fires_when_a_handled_request_carries_no_authorize_policy()
	{
		var withoutAuthorize = Contract.Replace("[Authorize(Policy = \"AuthN.Public\")]", "");
		var diagnostics = GenerateDiagnostics(withoutAuthorize);
		diagnostics.ShouldContain(d => d.Id == "NORSE011" && d.Severity == DiagnosticSeverity.Error);
	}

	[Fact]
	void NORSE010_fires_when_two_handlers_claim_the_same_request()
	{
		var duplicated = $$"""
			{{Contract}}

			namespace Norse.Identity.Web.Server
			{
				sealed class SecondLoginHandler : Norse.Abstractions.Web.Server.Mediator.IRequestHandler<LoginRequest, Norse.Abstractions.Contracts.BoolResponse>
				{
					public ValueTask<Norse.Abstractions.Contracts.Outcome<Norse.Abstractions.Contracts.BoolResponse>> Handle(LoginRequest request, CancellationToken cancellationToken = default) =>
						ValueTask.FromResult(Norse.Abstractions.Contracts.Outcome<Norse.Abstractions.Contracts.BoolResponse>.Ok(new Norse.Abstractions.Contracts.BoolResponse { Value = true }));
				}
			}
			""";
		var diagnostics = GenerateDiagnostics(duplicated);
		diagnostics.ShouldContain(d => d.Id == "NORSE010" && d.Severity == DiagnosticSeverity.Error);
	}

	// Generate / GenerateDiagnostics: build CSharpCompilation (assembly name "Norse.Identity.Web.Server",
	// references: recovered ReferenceAssemblies + Norse.Abstractions.Contracts + Norse.Abstractions.Web.Server
	// + FluentValidation + Microsoft.AspNetCore.Authorization), run HandlerRegistrationGenerator via
	// CSharpGeneratorDriver, return the single generated tree's text / the driver diagnostics.
}
```

- [ ] **Step 3: Run tests to verify they fail** (`dotnet test Asgard/tests/Abstractions.Web.Server.Generator.Tests` — build error, generator doesn't exist)

- [ ] **Step 4: Implement the generator**

```csharp
// Asgard/gen/Abstractions.Web.Server.Generator/HandlerModel.cs
namespace Norse.Abstractions.Web.Server.Generator;

sealed record HandlerModel(
	string HandlerTypeName,      // global::-qualified
	string RequestTypeName,      // global::-qualified
	string ResponseTypeName,     // global::-qualified payload
	string[] ValidatorTypeNames); // global::-qualified, may be empty
```

```csharp
// Asgard/gen/Abstractions.Web.Server.Generator/HandlerRegistrationGenerator.cs
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Norse.Abstractions.Emit;

namespace Norse.Abstractions.Web.Server.Generator;

#pragma warning disable RS2008 // No analyzer-release ledger, matching the platform's other generators.

[Generator(LanguageNames.CSharp)]
public sealed class HandlerRegistrationGenerator : IIncrementalGenerator
{
	static readonly DiagnosticDescriptor DuplicateHandler = new(
		"NORSE010", "Duplicate request handler",
		"Request type '{0}' has more than one IRequestHandler implementation in this assembly", "Norse.Mediator",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	static readonly DiagnosticDescriptor MissingAuthorizePolicy = new(
		"NORSE011", "Request missing authorization policy",
		"Request type '{0}' carries no [Authorize(Policy = ...)] — every request names its policy, AuthNPolicies.Public included", "Norse.Mediator",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var models = context.CompilationProvider.Select(Discover);
		context.RegisterSourceOutput(models, static (productionContext, result) =>
		{
			foreach (var diagnostic in result.Diagnostics)
				productionContext.ReportDiagnostic(diagnostic);
			if (result.Handlers.Length > 0 && !result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
				productionContext.AddSource("NorseHandlerRegistration.g.cs",
					SourceText.From(RegistrationEmitter.Emit(result.AssemblyName, result.RootNamespace, result.Handlers), Utf8NoBom.Encoding));
		});
	}

	static DiscoveryResult Discover(Compilation compilation, CancellationToken cancellationToken)
	{
		var handlerInterface = compilation.GetTypeByMetadataName("Norse.Abstractions.Web.Server.Mediator.IRequestHandler`2");
		var validatorInterface = compilation.GetTypeByMetadataName("FluentValidation.IValidator`1");
		var authorizeAttribute = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Authorization.AuthorizeAttribute");
		if (handlerInterface is null)
			return DiscoveryResult.Empty(compilation);

		// Handlers: compiling assembly only — registration is a realm-local act and the emitted code
		// legally references internal handler types from inside their own assembly.
		var handlers = AllTypes(compilation.Assembly.GlobalNamespace)
			.Where(t => t is { IsAbstract: false, TypeKind: TypeKind.Class })
			.SelectMany(t => t.AllInterfaces
				.Where(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, handlerInterface))
				.Select(i => (Handler: t, Request: i.TypeArguments[0], Response: i.TypeArguments[1])))
			.ToImmutableArray();

		var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

		foreach (var duplicate in handlers.GroupBy(h => h.Request, SymbolEqualityComparer.Default).Where(g => g.Count() > 1))
			diagnostics.Add(Diagnostic.Create(DuplicateHandler, Location.None, duplicate.Key!.ToDisplayString()));

		foreach (var handler in handlers)
			if (authorizeAttribute is not null && !handler.Request.GetAttributes().Any(a =>
					SymbolEqualityComparer.Default.Equals(a.AttributeClass, authorizeAttribute) &&
					a.NamedArguments.Any(n => n.Key == "Policy" && n.Value.Value is string { Length: > 0 })))
				diagnostics.Add(Diagnostic.Create(MissingAuthorizePolicy, Location.None, handler.Request.ToDisplayString()));

		// Validators: compiled-symbol walk across own + referenced assemblies (PackageReference-mode
		// parity) — Heimdall's validators serve Himinbjorg's handlers.
		IAssemblySymbol[] assemblies = [compilation.Assembly, .. compilation.SourceModule.ReferencedAssemblySymbols];
		ImmutableArray<(INamedTypeSymbol Validator, ITypeSymbol Request)> validators = validatorInterface is null ?
			[] :
			assemblies
				.SelectMany(a => AllTypes(a.GlobalNamespace))
				.Where(t => t is { IsAbstract: false, TypeKind: TypeKind.Class })
				.SelectMany(t => t.AllInterfaces
					.Where(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, validatorInterface))
					.Select(i => (Validator: t, Request: i.TypeArguments[0])))
				.ToImmutableArray();

		var format = SymbolDisplayFormat.FullyQualifiedFormat; // const is illegal on a reference type — local it is
		var models = handlers
			.Select(h => new HandlerModel(
				h.Handler.ToDisplayString(format),
				h.Request.ToDisplayString(format),
				h.Response.ToDisplayString(format),
				[.. validators
					.Where(v => SymbolEqualityComparer.Default.Equals(v.Request, h.Request))
					.Select(v => v.Validator.ToDisplayString(format))
					.Distinct()]))
			.OrderBy(m => m.RequestTypeName, StringComparer.Ordinal)
			.ToImmutableArray();

		return new(compilation.AssemblyName ?? "Unknown", RootNamespace(compilation), models, diagnostics.ToImmutable());
	}

	static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol root)
	{
		foreach (var member in root.GetMembers())
			switch (member)
			{
				case INamespaceSymbol ns:
					foreach (var nested in AllTypes(ns)) yield return nested;
					break;
				case INamedTypeSymbol type:
					yield return type;
					break;
			}
	}

	static string RootNamespace(Compilation compilation) =>
		compilation.AssemblyName ?? "Norse.Generated";

	sealed record DiscoveryResult(string AssemblyName, string RootNamespace, ImmutableArray<HandlerModel> Handlers, ImmutableArray<Diagnostic> Diagnostics)
	{
		public static DiscoveryResult Empty(Compilation compilation) =>
			new(compilation.AssemblyName ?? "Unknown", compilation.AssemblyName ?? "Norse.Generated", [], []);
	}
}
```

```csharp
// Asgard/gen/Abstractions.Web.Server.Generator/RegistrationEmitter.cs
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Norse.Abstractions.Emit;

namespace Norse.Abstractions.Web.Server.Generator;

static class RegistrationEmitter
{
	internal static string Emit(string assemblyName, string rootNamespace, ImmutableArray<HandlerModel> handlers)
	{
		var methodName = $"Add{assemblyName.Replace(".", "")}Handlers";
		StringBuilder builder = new();
		builder.AppendCSharp(
			$$"""
			// <auto-generated/>
			namespace {{rootNamespace}};

			#pragma warning disable CS1591 // Generated registration: no XML doc comments.
			/// <summary>Generated by Norse.Abstractions.Web.Server.Generator — compile-time handler/validator/dispatch registration.</summary>
			public static class NorseHandlerRegistration
			{
				public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection {{methodName}}(
					this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
				{
			{{Registrations(handlers)}}
					return services;
				}
			}
			#pragma warning restore CS1591
			""");
		return builder.ToString();
	}

	static string Registrations(ImmutableArray<HandlerModel> handlers) =>
		string.Join("\n", handlers.Select(h =>
		{
			var lines = new List<string>
			{
				$"\t\tglobal::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<global::Norse.Abstractions.Web.Server.Mediator.IRequestHandler<{h.RequestTypeName}, {h.ResponseTypeName}>, {h.HandlerTypeName}>(services);",
				$"\t\tglobal::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<global::Norse.Abstractions.Web.Server.Mediator.ISenderDispatch, global::Norse.Abstractions.Web.Server.Mediator.SenderDispatch<{h.RequestTypeName}, {h.ResponseTypeName}>>(services);",
			};
			lines.AddRange(h.ValidatorTypeNames.Select(v =>
				$"\t\tglobal::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<global::FluentValidation.IValidator<{h.RequestTypeName}>, {v}>(services);"));
			return string.Join("\n", lines);
		}));
}
```

- [ ] **Step 5: Wire the generator into `Abstractions.Web.Server.csproj`** (analyzer ProjectReference + package bundling, mirroring what `Abstractions.Contracts.csproj` had before Task 3 — same `IncludeGeneratorInPackage` shape, DLL names `Norse.Abstractions.Web.Server.Generator.dll` + `Norse.Abstractions.Emit.dll`):

```xml
<ProjectReference
	Include="../../gen/Abstractions.Web.Server.Generator/Abstractions.Web.Server.Generator.csproj"
	OutputItemType="Analyzer"
	ReferenceOutputAssembly="false" />
```

- [ ] **Step 6: Run tests to verify they pass** (`dotnet test Asgard/tests/Abstractions.Web.Server.Generator.Tests`)

- [ ] **Step 7: Full solution green** (`dotnet test Asgard/Asgard.slnx`), add the new projects to `Asgard.slnx` first.

- [ ] **Step 8: Commit**

```bash
git add -A Asgard
git commit -m "feat: handler-registration generator — compile-time AddNorse*Handlers with NORSE010/NORSE011"
```

---

## SHIP GATE — Asgard

PR merged, CI green, tag pushed, packages live (`Norse.Abstractions.Contracts`, `Norse.Abstractions.Web.Server` with bundled generator). Downstream phases consume the published version in package mode; dev mode resolves ProjectReferences regardless. **Bifröst-wide dev build is red from here until Yggdrasil's gate — expected, per Global Constraints.**

---

## Task 5: Midgard — `PrincipalAccessor`, `PolicyCache`, behavior re-plumb

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/Mediator/PrincipalAccessor.cs`, `PolicyCache.cs`
- Modify: `Midgard/src/Infrastructure.Web.Server/Mediator/AuthorizationBehavior.cs`, `ValidationBehavior.cs`
- Modify: `Midgard/src/Infrastructure.Web.Server/Mediator/TelemetryBehavior.cs`, `ExceptionTranslationBehavior.cs` — doc comments only: delete the "Stays internal (2026-07-25) … InternalsVisibleTo grant" paragraph from all four behaviors (they stay `internal`, but now because Midgard's own `AddNorsePipeline()` is the only constructor — no cross-assembly story left to explain)
- Modify: `Midgard/src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj` — delete the `<InternalsVisibleTo Include="Norse.Hosting.Web.Server" />` item and its comment block
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/AuthorizationBehaviorTests.cs` (rewrite), `ValidationBehaviorTests.cs` (rewrite), `PrincipalAccessorTests.cs` (new)

**Interfaces:**
- Consumes: `IPrincipalAccessor`, `IBehavior<,>`, `Outcome<T>` (Asgard, new version).
- Produces (Task 6/7 rely on these):
  - `PrincipalAccessor` — `internal sealed`, scoped; `internal void Seed(ClaimsPrincipal principal)`; implements `IPrincipalAccessor.GetPrincipalAsync`: seeded value wins; else resolves `AuthenticationStateProvider` from the scope and fetches **live** (circuits outlive login/logout — an eager seed would go stale); else throws `InvalidOperationException` naming the missing channel adapter.
  - `static class PolicyCache<TRequest>` — `internal`; `public static string Policy { get; }` read once per closed type from `[Authorize(Policy = ...)]` on `TRequest`; missing/empty → throw (runtime backstop behind NORSE011).
  - `AuthorizationBehavior<TRequest, TResponse>(IAuthorizationService authorizationService, IPrincipalAccessor principalAccessor)` — policy from `PolicyCache<TRequest>`.
  - `ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)` — empty collection ⇒ straight to `next()`; multiple validators aggregate failures.

- [ ] **Step 1: Write the failing tests**

```csharp
// Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/PrincipalAccessorTests.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Norse.Infrastructure.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class PrincipalAccessorTests
{
	static ClaimsPrincipal Authenticated() =>
		new(new ClaimsIdentity(authenticationType: "test"));

	[Fact]
	async Task Seeded_principal_wins_and_never_touches_the_authentication_state_provider()
	{
		var services = new ServiceCollection().BuildServiceProvider();
		PrincipalAccessor accessor = new(services);
		var seeded = Authenticated();
		accessor.Seed(seeded);

		(await accessor.GetPrincipalAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(seeded);
	}

	[Fact]
	async Task Unseeded_scope_with_an_authentication_state_provider_fetches_live()
	{
		var user = Authenticated();
		var provider = Substitute.For<AuthenticationStateProvider>();
		provider.GetAuthenticationStateAsync().Returns(new AuthenticationState(user));
		var services = new ServiceCollection().AddSingleton(provider).BuildServiceProvider();

		PrincipalAccessor accessor = new(services);

		(await accessor.GetPrincipalAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(user);
	}

	[Fact]
	async Task Unseeded_scope_with_no_provider_fails_loudly()
	{
		var services = new ServiceCollection().BuildServiceProvider();
		PrincipalAccessor accessor = new(services);

		await Should.ThrowAsync<InvalidOperationException>(async () =>
			await accessor.GetPrincipalAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	async Task Circuit_principal_is_live_not_cached_across_mid_circuit_revalidation()
	{
		// Spec §2.4 remand (2026-07-27, security-relevant): a RevalidatingAuthenticationStateProvider
		// can log the user out mid-circuit; the accessor must reflect that on the very next access,
		// never keep authorizing the old identity for the life of the circuit scope.
		var before = Authenticated();
		var after = new ClaimsPrincipal(new ClaimsIdentity()); // revalidation failed → anonymous
		var provider = Substitute.For<AuthenticationStateProvider>();
		provider.GetAuthenticationStateAsync().Returns(new AuthenticationState(before), new AuthenticationState(after));
		var services = new ServiceCollection().AddSingleton(provider).BuildServiceProvider();

		PrincipalAccessor accessor = new(services);

		(await accessor.GetPrincipalAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(before);
		(await accessor.GetPrincipalAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(after);
	}
}
```

```csharp
// Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/AuthorizationBehaviorTests.cs — full rewrite
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class AuthorizationBehaviorTests
{
	[Authorize(Policy = "Test.Policy")]
	public sealed record PolicedRequest : IQueryRequest<bool>;

	public sealed record UnpolicedRequest : IQueryRequest<bool>;

	sealed class FixedPrincipal(ClaimsPrincipal principal) : IPrincipalAccessor
	{
		public ValueTask<ClaimsPrincipal> GetPrincipalAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(principal);
	}

	static AuthorizationBehavior<PolicedRequest, bool> Behavior(ClaimsPrincipal user, bool authorized)
	{
		var authorizationService = Substitute.For<IAuthorizationService>();
		authorizationService.AuthorizeAsync(user, "Test.Policy")
			.Returns(authorized ? AuthorizationResult.Success() : AuthorizationResult.Failed());
		return new(authorizationService, new FixedPrincipal(user));
	}

	[Fact]
	async Task Not_authenticated_returns_Unauthorized()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity());
		var outcome = await Behavior(user, authorized: false)
			.Handle(new PolicedRequest(), () => throw new InvalidOperationException("must not reach handler"));
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Unauthorized);
	}

	[Fact]
	async Task Authenticated_but_policy_fails_returns_Forbidden()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "cookie"));
		var outcome = await Behavior(user, authorized: false)
			.Handle(new PolicedRequest(), () => throw new InvalidOperationException("must not reach handler"));
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Forbidden);
	}

	[Fact]
	async Task Authorized_calls_next()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "cookie"));
		var outcome = await Behavior(user, authorized: true)
			.Handle(new PolicedRequest(), () => ValueTask.FromResult(Outcome<bool>.Ok(true)));
		outcome.TryGetValue(out Norse.Primitives.Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeTrue();
	}

	[Fact]
	void A_request_type_with_no_policy_is_a_hard_failure_at_first_touch()
	{
		Should.Throw<TypeInitializationException>(() => _ = PolicyCache<UnpolicedRequest>.Policy)
			.InnerException.ShouldBeOfType<InvalidOperationException>();
	}
}
```

```csharp
// Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/ValidationBehaviorTests.cs — full rewrite
using FluentValidation;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class ValidationBehaviorTests
{
	public sealed record Sample(string Name) : ICommandRequest<bool>;

	[Fact]
	async Task No_registered_validators_means_a_valid_request()
	{
		ValidationBehavior<Sample, bool> behavior = new([]);
		var outcome = await behavior.Handle(new("anything"), () => ValueTask.FromResult(Outcome<bool>.Ok(true)));
		outcome.TryGetValue(out Norse.Primitives.Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeTrue();
	}

	[Fact]
	async Task Multiple_validators_aggregate_failures_by_property()
	{
		InlineValidator<Sample> first = new();
		first.RuleFor(s => s.Name).NotEmpty();
		InlineValidator<Sample> second = new();
		second.RuleFor(s => s.Name).MinimumLength(3).WithMessage("too short");

		ValidationBehavior<Sample, bool> behavior = new([first, second]);
		var outcome = await behavior.Handle(new(""), () => throw new InvalidOperationException("must not reach handler"));

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Validation);
		failed.Problem.Errors["Name"].Length.ShouldBe(2);
	}
}
```

- [ ] **Step 2: Run tests to verify they fail** (`dotnet test Midgard/tests/Infrastructure.Web.Server.Tests --filter "PrincipalAccessorTests|AuthorizationBehaviorTests|ValidationBehaviorTests"`)

- [ ] **Step 3: Implement**

```csharp
// Midgard/src/Infrastructure.Web.Server/Mediator/PrincipalAccessor.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// The scoped principal context (spec §2.4, the Bogard scoped-context pattern). An explicit
/// <see cref="Seed"/> from a channel adapter (the gRPC seeding interceptor) always wins and is
/// deterministic for request-scoped channels. In a circuit scope — never seeded, because a circuit
/// outlives login/logout — the accessor defers to <see cref="AuthenticationStateProvider"/> live on
/// every access. A scope neither seeded nor circuit-shaped fails loudly: no silent anonymous.
/// </summary>
sealed class PrincipalAccessor(IServiceProvider services) : IPrincipalAccessor
{
	ClaimsPrincipal? _seeded;

	internal void Seed(ClaimsPrincipal principal) =>
		_seeded = principal;

	public async ValueTask<ClaimsPrincipal> GetPrincipalAsync(CancellationToken cancellationToken = default) =>
		_seeded ??
			(services.GetService<AuthenticationStateProvider>() is { } provider ?
				(await provider.GetAuthenticationStateAsync().ConfigureAwait(false)).User :
				throw new InvalidOperationException(
					"No principal is available in this scope. A gRPC channel must register Midgard's PrincipalSeedingInterceptor; a circuit scope must have an AuthenticationStateProvider. Neither was found."));
}
```

```csharp
// Midgard/src/Infrastructure.Web.Server/Mediator/PolicyCache.cs
using System.Reflection;
using Microsoft.AspNetCore.Authorization;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// Reads <c>[Authorize(Policy = ...)]</c> off <typeparamref name="TRequest"/> exactly once per
/// closed type — zero per-call reflection (spec §2.5). The runtime backstop behind the registration
/// generator's NORSE011 compile-time check: a request with no policy is a hard failure at first
/// dispatch, never an open door.
/// </summary>
static class PolicyCache<TRequest>
{
	/// <summary>The policy name <typeparamref name="TRequest"/> declares.</summary>
	public static string Policy { get; } =
		typeof(TRequest).GetCustomAttribute<AuthorizeAttribute>() is { Policy.Length: > 0 } authorize ?
			authorize.Policy :
			throw new InvalidOperationException(
				$"{typeof(TRequest).Name} carries no [Authorize(Policy = ...)] — every request names its policy, AuthNPolicies.Public included.");
}
```

`AuthorizationBehavior.cs` — full replacement of the class (keep file-level doc summary tone, drop the "generator baked in" and "Stays internal (2026-07-25)" text):

```csharp
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// Evaluates the policy the request type declares via <c>[Authorize(Policy = ...)]</c> (read once
/// per closed type by <see cref="PolicyCache{TRequest}"/>) against the principal
/// <see cref="IPrincipalAccessor"/> supplies. Not authenticated at all →
/// <see cref="ErrorCategory.Unauthorized"/>; authenticated but the policy fails →
/// <see cref="ErrorCategory.Forbidden"/>. On the wire path this runs behind ASP.NET Core's endpoint
/// [Authorize] wall — defense in depth, same policy, same decision; this behavior is the single
/// source of Unauthorized/Forbidden as data.
/// </summary>
sealed class AuthorizationBehavior<TRequest, TResponse>(
	IAuthorizationService authorizationService, IPrincipalAccessor principalAccessor) :
	IBehavior<TRequest, TResponse>
	where TResponse : notnull
{
	public async ValueTask<Outcome<TResponse>> Handle(TRequest request, BehaviorDelegate<TResponse> next, CancellationToken cancellationToken = default)
	{
		var user = await principalAccessor.GetPrincipalAsync(cancellationToken).ConfigureAwait(false);
		var result = await authorizationService.AuthorizeAsync(user, PolicyCache<TRequest>.Policy).ConfigureAwait(false);
		return !result.Succeeded ?
			Outcome<TResponse>.Err(user.Identity is { IsAuthenticated: true } ? ErrorCategory.Forbidden : ErrorCategory.Unauthorized) :
			await next().ConfigureAwait(false);
	}
}
```

`ValidationBehavior.cs` — full replacement of the class:

```csharp
using FluentValidation;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// Runs every registered <see cref="IValidator{T}"/> for the request and collapses failures into
/// field-grouped <see cref="ErrorCategory.Validation"/>. An empty validator collection is a valid
/// request by definition (spec §2.6) — queries and commands both flow through this chain, and most
/// queries never declare a validator. Absence is <c>[]</c>, not an error.
/// </summary>
sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators) :
	IBehavior<TRequest, TResponse>
	where TResponse : notnull
{
	public async ValueTask<Outcome<TResponse>> Handle(TRequest request, BehaviorDelegate<TResponse> next, CancellationToken cancellationToken = default)
	{
		Dictionary<string, List<string>> failures = [];
		foreach (var validator in validators)
		{
			var result = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
			foreach (var failure in result.Errors)
			{
				if (!failures.TryGetValue(failure.PropertyName, out var messages))
					failures[failure.PropertyName] = messages = [];
				messages.Add(failure.ErrorMessage);
			}
		}

		return failures.Count > 0 ?
			Outcome<TResponse>.Err(ErrorCategory.Validation, failures.ToDictionary(f => f.Key, f => f.Value.ToArray())) :
			await next().ConfigureAwait(false);
	}
}
```

Also: delete the IVT item + comment from `Infrastructure.Web.Server.csproj`; scrub the "Stays internal (2026-07-25)" paragraphs from `TelemetryBehavior.cs` and `ExceptionTranslationBehavior.cs` doc comments.

- [ ] **Step 4: Run tests to verify they pass**, then the full project suite (`dotnet test Midgard/tests/Infrastructure.Web.Server.Tests`) — `TelemetryBehaviorTests` must still pass untouched.

- [ ] **Step 5: Commit**

```bash
git add -A Midgard/src Midgard/tests
git commit -m "feat: DI-native behaviors — PrincipalAccessor, PolicyCache, IEnumerable validators; drop the IVT grant"
```

---

## Task 6: Midgard — `Sender`, `SenderDispatchMap`, `AddNorsePipeline()`

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/Mediator/Sender.cs`, `SenderDispatchMap.cs`, `ServiceCollectionExtensions.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/SenderTests.cs`

**Interfaces:**
- Consumes: `ISender`, `ISenderDispatch`/`ISenderDispatch<T>`, `SenderDispatch<,>`, `IPrincipalAccessor` (Asgard); the four behaviors (Task 5).
- Produces:
  - `Norse.Infrastructure.Web.Server.Mediator.ServiceCollectionExtensions` — extension block on `IServiceCollection`: `public IServiceCollection AddNorsePipeline()` registering, in exactly this order (order is chain order, spec §2.2): open-generic scoped `IBehavior<,>` → `TelemetryBehavior<,>`, `ExceptionTranslationBehavior<,>`, `AuthorizationBehavior<,>`, `ValidationBehavior<,>`; then `AddScoped<PrincipalAccessor>()` + `AddScoped<IPrincipalAccessor>(sp => sp.GetRequiredService<PrincipalAccessor>())` (one instance, two faces — the seeding interceptor needs the concrete); `AddSingleton<SenderDispatchMap>()`; `AddScoped<ISender, Sender>()`. All idempotent via `TryAdd`-style guards is NOT required — the composition root calls it once; a duplicate call duplicating open generics would double-run behaviors, so guard with `services.Any(d => d.ServiceType == typeof(ISender))` early-return.
  - `SenderDispatchMap(IEnumerable<ISenderDispatch> entries)` — singleton; `FrozenDictionary<Type, ISenderDispatch>`; `Get(Type)` throws `InvalidOperationException` naming the missing generated registration call on a miss.
  - `Sender(IServiceProvider services, SenderDispatchMap map)` — scoped; `Send` casts the entry to `ISenderDispatch<TResponse>` and dispatches with the scoped provider.

- [ ] **Step 1: Write the failing integration test — the full chain through a real container**

```csharp
// Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/SenderTests.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class SenderTests
{
	[Authorize(Policy = "Test.Open")]
	public sealed record Echo(string Text) : IQueryRequest<string>;

	sealed class EchoHandler : IRequestHandler<Echo, string>
	{
		public ValueTask<Outcome<string>> Handle(Echo request, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(Outcome<string>.Ok(request.Text));
	}

	sealed class ThrowingHandler : IRequestHandler<Echo, string>
	{
		public ValueTask<Outcome<string>> Handle(Echo request, CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("boom");
	}

	static ServiceProvider Host<THandler>() where THandler : class, IRequestHandler<Echo, string> =>
		new ServiceCollection()
			.AddLogging()
			.AddAuthorizationBuilder().AddPolicy("Test.Open", p => p.RequireAssertion(_ => true)).Services
			.AddNorsePipeline()
			.AddScoped<IRequestHandler<Echo, string>, THandler>()
			.AddSingleton<ISenderDispatch, SenderDispatch<Echo, string>>()
			.BuildServiceProvider();

	[Fact]
	async Task Sends_through_the_full_standard_chain_to_the_handler()
	{
		await using var host = Host<EchoHandler>();
		await using var scope = host.CreateAsyncScope();
		scope.ServiceProvider.GetRequiredService<PrincipalAccessor>()
			.Seed(new(new System.Security.Claims.ClaimsIdentity(authenticationType: "test")));

		var outcome = await scope.ServiceProvider.GetRequiredService<ISender>()
			.Send(new Echo("hello"), TestContext.Current.CancellationToken);

		outcome.TryGetValue(out Norse.Primitives.Success<string> success).ShouldBeTrue();
		success.Value.ShouldBe("hello");
	}

	[Fact]
	async Task A_throwing_handler_degrades_to_a_Fault_outcome_with_a_correlation_id()
	{
		await using var host = Host<ThrowingHandler>();
		await using var scope = host.CreateAsyncScope();
		scope.ServiceProvider.GetRequiredService<PrincipalAccessor>()
			.Seed(new(new System.Security.Claims.ClaimsIdentity(authenticationType: "test")));

		var outcome = await scope.ServiceProvider.GetRequiredService<ISender>()
			.Send(new Echo("hello"), TestContext.Current.CancellationToken);

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Fault);
		failed.Problem.CorrelationId.ShouldNotBeNull();
	}

	[Fact]
	async Task An_unmapped_request_type_fails_loudly_naming_the_generated_registration()
	{
		await using var host = new ServiceCollection().AddLogging().AddNorsePipeline().BuildServiceProvider();
		await using var scope = host.CreateAsyncScope();
		var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
			await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Echo("x"), TestContext.Current.CancellationToken));
		exception.Message.ShouldContain("AddNorse");
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement**

```csharp
// Midgard/src/Infrastructure.Web.Server/Mediator/SenderDispatchMap.cs
using System.Collections.Frozen;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// The sender's frozen request-type → dispatch-entry map, built once from every
/// <see cref="ISenderDispatch"/> the realms' generated <c>AddNorse*Handlers()</c> calls registered.
/// A cross-realm duplicate handler — invisible to NORSE010, which is per-assembly — lands here as
/// <c>ToFrozenDictionary</c> throwing <see cref="ArgumentException"/> on the duplicate key at first
/// resolution: the chosen loud startup backstop (2026-07-27 review), priced, not accidental.
/// </summary>
sealed class SenderDispatchMap(IEnumerable<ISenderDispatch> entries)
{
	readonly FrozenDictionary<Type, ISenderDispatch> _map =
		entries.ToFrozenDictionary(entry => entry.RequestType);

	public ISenderDispatch Get(Type requestType) =>
		_map.TryGetValue(requestType, out var entry) ?
			entry :
			throw new InvalidOperationException(
				$"No handler is registered for request type {requestType.Name}. Is the owning realm's generated AddNorse*Handlers() call missing from the composition root?");
}
```

```csharp
// Midgard/src/Infrastructure.Web.Server/Mediator/Sender.cs
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// The hand-rolled sender (spec §2.2): a frozen-dictionary lookup plus the closed-generic fold in
/// <see cref="SenderDispatch{TRequest,TResponse}"/>. Scoped so behaviors and handlers resolve from
/// the caller's own scope. No reflection, no assembly scanning — the dispatch map is populated by
/// generated compile-time registrations.
/// </summary>
sealed class Sender(IServiceProvider services, SenderDispatchMap map) : ISender
{
	public ValueTask<Outcome<TResponse>> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
		where TResponse : notnull =>
		((ISenderDispatch<TResponse>)map.Get(request.GetType())).Dispatch(services, request, cancellationToken);
}
```

```csharp
// Midgard/src/Infrastructure.Web.Server/Mediator/ServiceCollectionExtensions.cs
using FluentValidation;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>Composition of the platform's standard mediator pipeline — the one composition site.</summary>
public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>
		/// Registers the standard behavior chain — registration order <b>is</b> chain order, and it is
		/// law (spec §2.2): Telemetry → ExceptionTranslation → Authorization → Validation → handler —
		/// plus the scoped <see cref="PrincipalAccessor"/>, the dispatch map, and the
		/// <see cref="ISender"/>. A product realm appends its own <c>IBehavior&lt;,&gt;</c> registration
		/// after this call; it lands between Validation and the handler. Idempotent: a second call
		/// no-ops rather than double-running the chain.
		/// </summary>
		public IServiceCollection AddNorsePipeline()
		{
			if (services.Any(descriptor => descriptor.ServiceType == typeof(ISender)))
				return services;

			services.AddScoped(typeof(IBehavior<,>), typeof(TelemetryBehavior<,>));
			services.AddScoped(typeof(IBehavior<,>), typeof(ExceptionTranslationBehavior<,>));
			services.AddScoped(typeof(IBehavior<,>), typeof(AuthorizationBehavior<,>));
			services.AddScoped(typeof(IBehavior<,>), typeof(ValidationBehavior<,>));
			services
				.AddScoped<PrincipalAccessor>()
				.AddScoped<IPrincipalAccessor>(provider => provider.GetRequiredService<PrincipalAccessor>())
				.AddSingleton<SenderDispatchMap>()
				.AddScoped<ISender, Sender>();
			return services;
		}
	}
}
```

Note: open-generic registrations cannot ride the fluent chain (`AddScoped(typeof(...), typeof(...))` returns `IServiceCollection` — they can; keep all four on the chain if the overload resolution cooperates, otherwise the shape above). `AddLogging()` is the test's job; `AddNorsePipeline` does not take dependencies it doesn't own.

- [ ] **Step 4: Run tests to verify they pass** (all three SenderTests + the Task 5 suites stay green)

- [ ] **Step 5: Commit**

```bash
git add -A Midgard/src Midgard/tests
git commit -m "feat: hand-rolled Sender + AddNorsePipeline — registration order is chain order"
```

---

## Task 7: Midgard — gRPC server interceptor stack (seeding + outcome + unhandled)

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/PrincipalSeedingInterceptor.cs`
- Modify: `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ServiceCollectionExtensions.cs`
- Modify: `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/OutcomeServerInterceptor.cs` + `UnhandledExceptionInterceptor.cs` (doc comments: reflect that registration now actually happens here)
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/PrincipalSeedingInterceptorTests.cs`, extend `.../Grpc/ServiceCollectionExtensionsTests.cs` (create if absent)

**Interfaces:**
- Consumes: `PrincipalAccessor.Seed` (Task 5), `OutcomeServerInterceptor` + `UnhandledExceptionInterceptor` (already shipped, unchanged code).
- Produces: `AddNorseCodeFirstGrpc()` now registers, in this order (first = outermost): `UnhandledExceptionInterceptor` (safety net), `PrincipalSeedingInterceptor` (stamps `context.GetHttpContext().User` into the scoped `PrincipalAccessor`), `OutcomeServerInterceptor` (innermost, the wire-boundary throw point — spec §9 v5 order preserved).

- [ ] **Step 1: Write the failing tests**

```csharp
// Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/PrincipalSeedingInterceptorTests.cs
// PrincipalSeedingInterceptor.UnaryServerHandler: build a DefaultHttpContext with a known User, a
// TestServerCallContext (Grpc.Core.Testing or the repo's existing test double for ServerCallContext —
// mirror OutcomeServerInterceptorTests' arrangement), run the interceptor with a continuation that
// captures accessor state, assert Seed happened before the continuation ran and the continuation's
// response passes through unchanged.
```

Also: `AddNorseCodeFirstGrpc_registers_all_three_interceptors_in_net_order` — call `AddNorseCodeFirstGrpc()` on a `ServiceCollection`, resolve `IConfigureOptions<GrpcServiceOptions>`, apply to a fresh `GrpcServiceOptions`, assert `options.Interceptors` types equal `[typeof(UnhandledExceptionInterceptor), typeof(PrincipalSeedingInterceptor), typeof(OutcomeServerInterceptor)]` in order.

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement**

```csharp
// Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/PrincipalSeedingInterceptor.cs
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
/// The gRPC channel adapter's half of the principal contract (spec §2.4): stamps the request
/// principal into the scoped <see cref="PrincipalAccessor"/> at entry, before any pipeline code can
/// ask for it. Grpc.AspNetCore activates interceptors from the request's DI scope, so the accessor
/// this constructor receives is the same instance the behaviors resolve.
/// </summary>
sealed class PrincipalSeedingInterceptor(PrincipalAccessor accessor) : Interceptor
{
	public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
		TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
	{
		accessor.Seed(context.GetHttpContext().User);
		return continuation(request, context);
	}
}
```

`ServiceCollectionExtensions.cs` (Grpc) — registration order is nesting order, first registered = outermost:

```csharp
/// <summary>
/// Wires protobuf-net.Grpc code-first hosting with the platform interceptor stack (spec §2.1):
/// UnhandledExceptionInterceptor outermost (the net), PrincipalSeedingInterceptor (channel adapter),
/// OutcomeServerInterceptor innermost (the DU's idiom translator — Failed → throw + ErrorInfo).
/// </summary>
public IServiceCollection AddNorseCodeFirstGrpc()
{
	services.AddCodeFirstGrpc(options =>
	{
		options.Interceptors.Add<UnhandledExceptionInterceptor>();
		options.Interceptors.Add<PrincipalSeedingInterceptor>();
		options.Interceptors.Add<OutcomeServerInterceptor>();
	});
	return services;
}
```

Update `OutcomeServerInterceptor`'s doc comment: replace the surrogate-registration reference with "registered by <c>AddNorseCodeFirstGrpc()</c>; surrogates are registered by the generated gRPC wiring (Task 9)". Update `UnhandledExceptionInterceptor`'s stale "a service implementation throws Problem.ToRpcException() directly (Task 12)" sentence — services never throw; the Outcome interceptor is the sole business-failure throw point.

- [ ] **Step 4: Run tests to verify they pass** (including the existing `OutcomeServerInterceptorTests`, untouched)

- [ ] **Step 5: Commit**

```bash
git add -A Midgard/src Midgard/tests
git commit -m "feat: register the full gRPC interceptor stack — seeding, outcome throw point, unhandled net"
```

---

## Task 8: Midgard — client decoder (`OutcomeClientInterceptor`, the sole decoder in the land)

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Client/Grpc/OutcomeClientInterceptor.cs`, `OutcomeFactory.cs`
- Test: `Midgard/tests/Infrastructure.Web.Client.Tests/Grpc/OutcomeClientInterceptorTests.cs`, `OutcomeFactoryTests.cs`

**Interfaces:**
- Consumes: `RpcExceptionExtensions.DecodeProblem()` (shipped, unchanged), `Outcome<T>`/`Failed`/`Problem`.
- Produces:
  - `public sealed class OutcomeClientInterceptor : Interceptor` — overrides `AsyncUnaryCall` (and `BlockingUnaryCall` for completeness): when `TResponse` closes `Outcome<>`, wraps the response task; a caught `RpcException` decodes to `Problem` and re-envelopes via `OutcomeFactory<TResponse>`; non-`Outcome` responses pass through untouched; non-`RpcException` failures propagate.
  - `static class OutcomeFactory<TResponse>` — `public static bool CanCreate`; `public static TResponse CreateErr(Problem problem)`; one compiled delegate per closed type, built in the static initializer via `Expression.New(Outcome<T>(Failed(problem)))` — one-time wiring (sanctioned), never on the success path. On WASM the expression interpreter serves; fine — this is the failure path.

- [ ] **Step 1: Write the failing tests**

```csharp
// Midgard/tests/Infrastructure.Web.Client.Tests/Grpc/OutcomeFactoryTests.cs
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Client.Grpc;

namespace Norse.Infrastructure.Web.Client.Tests.Grpc;

public sealed class OutcomeFactoryTests
{
	[Fact]
	void Creates_a_Failed_outcome_for_a_closed_outcome_type()
	{
		OutcomeFactory<Outcome<BoolResponse>>.CanCreate.ShouldBeTrue();
		Problem problem = new() { Category = ErrorCategory.LockedOut };
		var outcome = OutcomeFactory<Outcome<BoolResponse>>.CreateErr(problem);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.ShouldBeSameAs(problem);
	}

	[Fact]
	void Declines_non_outcome_response_types()
	{
		OutcomeFactory<string>.CanCreate.ShouldBeFalse();
	}
}
```

```csharp
// OutcomeClientInterceptorTests.cs — arrange a ClientInterceptorContext + a continuation returning an
// AsyncUnaryCall whose ResponseAsync faults with an RpcException carrying a grpc-status-details-bin
// trailer encoding ErrorInfo{Reason="LockedOut"} (reuse the encoding helper from the existing
// RpcExceptionExtensionsTests to build the trailer). Assert:
//   Decodes_a_thrown_RpcException_into_a_Failed_outcome — result is Failed(LockedOut).
//   Passes_success_responses_through_untouched — Ok payload arrives unchanged.
//   Propagates_non_rpc_exceptions — an InvalidOperationException from ResponseAsync surfaces as-is.
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement**

```csharp
// Midgard/src/Infrastructure.Web.Client/Grpc/OutcomeFactory.cs
using System.Linq.Expressions;
using Norse.Abstractions.Contracts;

namespace Norse.Infrastructure.Web.Client.Grpc;

/// <summary>
/// Type-erased <c>Failed</c>-envelope factory for the client decoder (spec §2.1): one compiled
/// delegate per closed <see cref="Outcome{T}"/>, built once in the static initializer — one-time
/// wiring, never touched on the success path. <see cref="CanCreate"/> is <see langword="false"/>
/// for every non-<c>Outcome</c> response type, which is how the interceptor passes those through.
/// Internal — only the interceptor (same assembly) and tests (IVT) touch it.
/// </summary>
static class OutcomeFactory<TResponse>
{
	static readonly Func<Problem, TResponse>? Factory = Build();

	/// <summary>Whether <typeparamref name="TResponse"/> is a closed <see cref="Outcome{T}"/>.</summary>
	public static bool CanCreate =>
		Factory is not null;

	/// <summary>Envelopes the decoded problem as the failure case of <typeparamref name="TResponse"/>.</summary>
	public static TResponse CreateErr(Problem problem) =>
		Factory is not null ?
			Factory(problem) :
			throw new InvalidOperationException($"{typeof(TResponse).Name} is not an Outcome<T>.");

	static Func<Problem, TResponse>? Build()
	{
		if (typeof(TResponse) is not { IsGenericType: true } type || type.GetGenericTypeDefinition() != typeof(Outcome<>))
			return null;

		var problem = Expression.Parameter(typeof(Problem), "problem");
		var failed = Expression.New(typeof(Failed).GetConstructor([typeof(Problem)])!, problem);
		var outcome = Expression.New(type.GetConstructor([typeof(Failed)])!, failed);
		return Expression.Lambda<Func<Problem, TResponse>>(outcome, problem).Compile();
	}
}
```

```csharp
// Midgard/src/Infrastructure.Web.Client/Grpc/OutcomeClientInterceptor.cs
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Norse.Infrastructure.Web.Client.Grpc;

/// <summary>
/// The sole inbound decoder in the land (spec §2.1): Norse clients receive failure on exactly one
/// wire — gRPC — and this interceptor translates it back into the DU. A faulted call whose response
/// type is a closed <c>Outcome&lt;T&gt;</c> has its <see cref="RpcException"/> decoded
/// (<c>ErrorInfo.Reason</c>-authoritative) and re-enveloped as <c>Failed(Problem)</c>; everything
/// else — non-Outcome responses, non-RpcException faults — passes through untouched.
/// </summary>
public sealed class OutcomeClientInterceptor : Interceptor
{
	public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
		TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
	{
		var call = continuation(request, context);
		return OutcomeFactory<TResponse>.CanCreate ?
			new(Decode(call.ResponseAsync), call.ResponseHeadersAsync, call.GetStatus, call.GetTrailers, call.Dispose) :
			call;
	}

	public override TResponse BlockingUnaryCall<TRequest, TResponse>(
		TRequest request, ClientInterceptorContext<TRequest, TResponse> context, BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
	{
		if (!OutcomeFactory<TResponse>.CanCreate)
			return continuation(request, context);

		try
		{
			return continuation(request, context);
		}
		catch (RpcException exception)
		{
			return OutcomeFactory<TResponse>.CreateErr(exception.DecodeProblem());
		}
	}

	static async Task<TResponse> Decode<TResponse>(Task<TResponse> response)
	{
		try
		{
			return await response.ConfigureAwait(false);
		}
		catch (RpcException exception)
		{
			return OutcomeFactory<TResponse>.CreateErr(exception.DecodeProblem());
		}
	}
}
```

- [ ] **Step 4: Run tests to verify they pass** (`dotnet test Midgard/tests/Infrastructure.Web.Client.Tests`)

- [ ] **Step 5: Commit**

```bash
git add -A Midgard/src Midgard/tests
git commit -m "feat: OutcomeClientInterceptor — RpcException + ErrorInfo decode back into the DU"
```

---

## Task 9: Midgard — gRPC wiring generators (server + client)

**Files:**
- Create: `Midgard/gen/Infrastructure.Web.Server.Generator/Infrastructure.Web.Server.Generator.csproj`, `GrpcServerRegistrationGenerator.cs`, `ServerRegistrationEmitter.cs`, `ContractDiscovery.cs`
- Create: `Midgard/gen/Infrastructure.Web.Client.Generator/Infrastructure.Web.Client.Generator.csproj`, `GrpcClientRegistrationGenerator.cs`, `ClientRegistrationEmitter.cs`
- Modify: `Midgard/src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj`, `Midgard/src/Infrastructure.Web.Client/Infrastructure.Web.Client.csproj` (analyzer refs + `IncludeGeneratorInPackage` targets, mirroring Asgard Task 4/Step 5)
- Modify: `Midgard/gen/Directory.Build.props` already exists pre-positioned — verify it matches Asgard's (it does; no change expected)
- Create: `Midgard/tests/Infrastructure.Web.Server.Generator.Tests/`, `Midgard/tests/Infrastructure.Web.Client.Generator.Tests/`

**Interfaces:**
- Consumes: `OutcomeClientInterceptor` (Task 8), the interceptor stack (Task 7), protobuf-net.Grpc (`AddCodeFirstGrpc`/`MapGrpcService`/`CreateGrpcService`), `RuntimeTypeModel`.
- Shared discovery (`ContractDiscovery`, compiled to both generator projects via `<Compile Include>` link or duplicated — link it, mirroring Urðarbrunnr's `Design.Generator.Shared` pattern): a **Norse contract** is an interface that (a) carries `System.ServiceModel.ServiceContractAttribute`, (b) is named `I{Context}Service`, and (c) has ≥1 method whose return type is `Task<Outcome<T>>`/`ValueTask<Outcome<T>>`. Walk `[compilation.Assembly, .. compilation.SourceModule.ReferencedAssemblySymbols]`. Collect per contract: interface name (global-qualified), payload type names (global-qualified, distinct, ordinal-sorted, for surrogates).
- Server generator additionally discovers the **implementation**: a non-abstract class in the walk implementing a discovered contract. Diagnostics (`Norse.Grpc` category, errors): **NORSE020** — a discovered contract visible to this compilation has no implementation in the walk; **NORSE021** — a contract method's `Outcome<T>` payload type collides by short name with a different payload namespace (the old unqualified-`.Name` collision, now caught instead of silently mis-emitted; emitters use fully-qualified names so this is belt-and-braces on surrogate dedup only).
- Produces, server (`NorseGrpcServerRegistration.g.cs`, emitted into the composition root that installs the analyzer):

```csharp
// <auto-generated/> (shape — real emitter uses AppendCSharp raw-string template)
namespace Norse.Hosting.Web.Server;

#pragma warning disable CS1591
public static class NorseGrpcServerRegistration
{
	static int _surrogatesRegistered;

	/// <summary>Registers the Outcome&lt;T&gt; passthrough surrogates, idempotent per type.</summary>
	public static void RegisterNorseOutcomeSurrogates()
	{
		if (global::System.Threading.Interlocked.Exchange(ref _surrogatesRegistered, 1) == 1)
			return;
		var model = global::ProtoBuf.Meta.RuntimeTypeModel.Default;
		if (!model.IsDefined(typeof(global::Norse.Abstractions.Contracts.Outcome<global::Norse.Abstractions.Contracts.BoolResponse>)))
			model.Add(typeof(global::Norse.Abstractions.Contracts.Outcome<global::Norse.Abstractions.Contracts.BoolResponse>), applyDefaultBehaviour: false).SetSurrogate(typeof(global::Norse.Abstractions.Contracts.BoolResponse));
		if (!model.IsDefined(typeof(global::Norse.Abstractions.Contracts.Outcome<global::Norse.Abstractions.Contracts.Unit>)))
			model.Add(typeof(global::Norse.Abstractions.Contracts.Outcome<global::Norse.Abstractions.Contracts.Unit>), applyDefaultBehaviour: false).SetSurrogate(typeof(global::Norse.Abstractions.Contracts.Unit));
		// ... one guarded pair per distinct payload across all discovered contracts (LoginResult, LogoutResult, ...)
		// Per-type IsDefined guards, not just the per-class Interlocked fast path (2026-07-27 review cure):
		// the server- and client-generated registrations write the SAME types to the SAME shared model
		// when both run in one process — exactly what Task 16's parity test does. The second Add against
		// an already-defined type would throw on protobuf-net's shared model; the guard makes both
		// registrations idempotent per type regardless of which ran first.
	}

	/// <summary>Maps every discovered Norse gRPC service with gRPC-Web enabled, registering surrogates first.</summary>
	public static global::Microsoft.AspNetCore.Builder.WebApplication MapNorseGrpcServices(
		this global::Microsoft.AspNetCore.Builder.WebApplication app)
	{
		RegisterNorseOutcomeSurrogates();
		global::Microsoft.AspNetCore.Builder.GrpcWebApplicationBuilderExtensions.UseGrpcWeb(app,
			new global::Microsoft.AspNetCore.Builder.GrpcWebOptions { DefaultEnabled = false });
		global::Microsoft.AspNetCore.Builder.GrpcEndpointRouteBuilderExtensions
			.MapGrpcService<global::Norse.Identity.Web.Server.AuthenticationService>(app)
			.EnableGrpcWeb();
		// ... one MapGrpcService per discovered implementation
		return app;
	}
}
```

- Produces, client (`NorseGrpcClientRegistration.g.cs`):

```csharp
// <auto-generated/> (shape)
namespace Norse.Hosting.Web.Client;

#pragma warning disable CS1591
public static class NorseGrpcClientRegistration
{
	static int _surrogatesRegistered;

	public static void RegisterNorseOutcomeSurrogates() { /* identical per-type-IsDefined-guarded body, client-side payload set */ }

	/// <summary>
	/// Registers every discovered Norse contract's proxy over <paramref name="channel"/>, decoded
	/// through OutcomeClientInterceptor — the host builds the channel (base address, credentials,
	/// gRPC-Web handler are host policy), this method does the rest.
	/// </summary>
	public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddNorseGrpcClients(
		this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,
		global::Grpc.Net.Client.GrpcChannel channel)
	{
		RegisterNorseOutcomeSurrogates();
		var invoker = global::Grpc.Core.Interceptors.CallInvokerExtensions.Intercept(
			channel, new global::Norse.Infrastructure.Web.Client.Grpc.OutcomeClientInterceptor());
		global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(
			services, global::ProtoBuf.Grpc.Client.GrpcClientFactory.CreateGrpcService<global::Norse.AuthN.Services.IAuthenticationService>(invoker));
		// ... one AddSingleton per discovered contract
		return services;
	}
}
```

- Packaging: `Norse.Infrastructure.Web.Server` and `Norse.Infrastructure.Web.Client` packages each bundle their generator DLL + `Norse.Abstractions.Emit.dll` under `analyzers/dotnet/cs/`, exactly the Asgard Task 4 shape. Composition roots get the generators by referencing the packages they already reference — no extra install step, no emission-mode property, nothing to forget.
- **`RuntimeTypeModel.Default` is the sanctioned home, on the record (2026-07-27 review):** this deviates from the desktop b2 directive's "dedicated `RuntimeTypeModel`, never `Default`" instruction, deliberately — the generated wiring now guarantees per-type idempotent registration (the `IsDefined` guards above), and `Default` means no `BinderConfiguration` threading on either end. This sentence supersedes the dedicated-model instruction; when the b2 brief lands in Glitnir, its ledger gets the same annotation (Task 17).

- [ ] **Step 1: Write the failing server-generator tests** — same `CSharpGeneratorDriver` harness as Task 4 (assembly name `Norse.Hosting.Web.Server`); source under test declares Heimdall-shaped `IAuthenticationService` + a Himinbjörg-shaped implementing class. Assert: `MapGrpcService<global::...AuthenticationService>` emitted; `EnableGrpcWeb` emitted; one `SetSurrogate` line per distinct payload including `Unit`; **every `SetSurrogate` line sits behind an `if (!model.IsDefined(...))` guard for its exact closed type** (the per-type idempotence cure — count of `IsDefined` occurrences equals count of `SetSurrogate` occurrences); `RegisterNorseOutcomeSurrogates()` called first inside `MapNorseGrpcServices`; NORSE020 fires when the implementation is absent.

- [ ] **Step 2: Write the failing client-generator tests** — assert `CreateGrpcService<global::...IAuthenticationService>` over an `Intercept(...OutcomeClientInterceptor())` invoker; surrogate lines present **with the same per-type `IsDefined` guard assertion as Step 1**; nothing emitted when no contract is visible.

- [ ] **Step 3: Run both test projects to verify failure**

- [ ] **Step 4: Implement `ContractDiscovery` + both generators + both emitters.** Structure mirrors Task 4's generator exactly: `CompilationProvider.Select(Discover)` → `RegisterSourceOutput` with diagnostics-then-emit. All type names via `SymbolDisplayFormat.FullyQualifiedFormat`. Payload extraction: unwrap `Task<>`/`ValueTask<>` then match the `Outcome<T>` **symbol** via `compilation.GetTypeByMetadataName("Norse.Abstractions.Contracts.Outcome`1")` + `SymbolEqualityComparer` on the original definition — never by unqualified name (the audit's finding #5 does not get re-implemented).

- [ ] **Step 5: Wire analyzer refs + packaging into both src csprojs; add all four new projects to `Midgard.slnx`**

- [ ] **Step 6: Run the full Midgard suite** (`dotnet test Midgard/Midgard.slnx`) — green.

- [ ] **Step 7: Commit**

```bash
git add -A Midgard
git commit -m "feat: gRPC wiring generators — MapNorseGrpcServices/AddNorseGrpcClients with surrogates and gRPC-Web"
```

---

## SHIP GATE — Midgard

PR merged, CI green, tag pushed, packages live (`Norse.Infrastructure.Web.Server`, `Norse.Infrastructure.Web.Client`, each with bundled generator). The IVT grant to `Norse.Hosting.Web.Server` is gone from the published package.

---

## Task 10: Heimdall — contract carries the policy, the markers, and the token

**Files:**
- Modify: `Heimdall/src/AuthN.Services/IAuthenticationService.cs`, `LoginRequest.cs`, `RegisterRequest.cs`, `LogoutRequest.cs`, `AuthN.Services.csproj`
- Test: `Heimdall/tests/AuthN.Services.Tests/` — extend the existing contract-shape tests (or create `RequestContractTests.cs`)

**Interfaces:**
- Consumes: `ICommandRequest<>` (Asgard new version), `AuthNPolicies` (same assembly).
- Produces (Himinbjörg Task 12 and Yggdrasil rely on these exact shapes):
  - `LoginRequest : ICommandRequest<BoolResponse>`, `RegisterRequest : ICommandRequest<BoolResponse>`, `LogoutRequest : ICommandRequest<Unit>` — payloads are the **handler** payloads (`BoolResponse`/`Unit`); the service maps them onto wire results exactly as today. All three are commands: login mutates lockout counters and mints a cookie.
  - Each request record gains `[Authorize(Policy = AuthNPolicies.Public)]` — the request names its policy (spec §2.5).
  - `IAuthenticationService` methods gain the token: `Task<Outcome<LoginResult>> Login(LoginRequest request, CancellationToken cancellationToken = default);` (same for `Register`/`Logout`) — protobuf-net.Grpc binds a trailing `CancellationToken` natively; `[GenerateGateway]` is removed; the interface doc-comment's "no CallContext parameter, deliberately" sentence is replaced with a note that the token rides the contract so components cancel without a gateway wrapper.
  - `LoginRequest`'s "no mediator-law coupling of any kind" doc sentence is updated: the marker couples it to `Abstractions.Contracts` (WASM-safe, already referenced for `Outcome<T>`) — the 07-24 objection was to the server-only assembly, which remains untouched.
- csproj: delete `<NorseGatewayEmissionMode>Contract</NorseGatewayEmissionMode>` (the property no longer exists anywhere).

- [ ] **Step 1: Write the failing contract test**

```csharp
// Heimdall/tests/AuthN.Services.Tests/RequestContractTests.cs
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Contracts;
using Norse.AuthN.Services;
using System.Reflection;

namespace Norse.AuthN.Services.Tests;

public sealed class RequestContractTests
{
	[Fact]
	void Every_request_is_a_marked_command_with_a_declared_policy()
	{
		typeof(ICommandRequest<BoolResponse>).IsAssignableFrom(typeof(LoginRequest)).ShouldBeTrue();
		typeof(ICommandRequest<BoolResponse>).IsAssignableFrom(typeof(RegisterRequest)).ShouldBeTrue();
		typeof(ICommandRequest<Unit>).IsAssignableFrom(typeof(LogoutRequest)).ShouldBeTrue();

		foreach (var request in (Type[])[typeof(LoginRequest), typeof(RegisterRequest), typeof(LogoutRequest)])
			request.GetCustomAttribute<AuthorizeAttribute>()!.Policy.ShouldBe(AuthNPolicies.Public);
	}

	[Fact]
	void Every_service_method_takes_a_trailing_cancellation_token()
	{
		foreach (var method in typeof(IAuthenticationService).GetMethods())
			method.GetParameters()[^1].ParameterType.ShouldBe(typeof(CancellationToken));
	}
}
```

- [ ] **Step 2: Run to verify failure; implement per the Produces block above; run to verify pass**

- [ ] **Step 3: Build the Heimdall solution** — `dotnet build Heimdall/Heimdall.slnx`. Components still reference `IAuthenticationGateway` (now nonexistent) and fail: that is Task 11's scope; gate this task on `AuthN.Services` + its tests only (`dotnet test Heimdall/tests/AuthN.Services.Tests`).

- [ ] **Step 4: Commit**

```bash
git add -A Heimdall/src/AuthN.Services Heimdall/tests/AuthN.Services.Tests
git commit -m "feat!: requests carry markers + policy, service contract carries the token, gateway trigger removed"
```

---

## Task 11: Heimdall — components inject the service, not a gateway

**Files:**
- Modify: `Heimdall/src/AuthN.Components.FluentUI/Login.razor`, `Register.razor`; `Heimdall/src/AuthN.Components/Logout.razor`
- Modify: `Heimdall/tests/AuthN.Components.Tests/*` (every `Substitute.For<IAuthenticationGateway>()` becomes `Substitute.For<IAuthenticationService>()`)

**Interfaces:**
- Consumes: `IAuthenticationService` with CT params (Task 10). Components already pattern-match `Outcome<T>` — the switch bodies do not change.

- [ ] **Step 1: Update the failing tests first** — swap the substitute target and the stubbed method signatures (`service.Login(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>())` etc.). Run: FAIL (components still inject the gateway).

- [ ] **Step 2: Re-point the three components.** Login.razor and Logout.razor: `@inject IAuthenticationService AuthenticationService` and the call becomes `await AuthenticationService.Login(_request, CancellationToken)` / `.Logout(new LogoutRequest(), CancellationToken)` — switch bodies untouched. Register.razor additionally picks up the pieces it always should have had (audit finding): `@inherits AsyncComponentBase` and `await AuthenticationService.Register(_request, CancellationToken)` — no more token-less call.

- [ ] **Step 3: Full Heimdall suite green** (`dotnet test Heimdall/Heimdall.slnx`), update `Heimdall/CLAUDE.md` + `README.md` (gateway story → direct service injection, DI substitution per host).

- [ ] **Step 4: Commit**

```bash
git add -A Heimdall
git commit -m "feat!: components inject IAuthenticationService directly — the gateway interface is gone"
```

---

## SHIP GATE — Heimdall

PR merged, CI green, tag pushed, `Norse.AuthN.Services` / `Norse.AuthN.Components` / `Norse.AuthN.Components.FluentUI` packages live.

---

## Task 12: Himinbjörg — handlers realign, the service becomes three `Send` calls

**Files:**
- Modify: `Himinbjorg/src/Identity.Web.Server/LoginHandler.cs`, `RegisterHandler.cs`, `LogoutHandler.cs`, `AuthenticationService.cs`, `ServiceCollectionExtensions.cs`, `Identity.Web.Server.csproj`
- Modify: `Himinbjorg/tests/Identity.Web.Server.Tests/*` (handler tests: drop validator-ctor arrangement; service tests: substitute `ISender`)

**Interfaces:**
- Consumes: `IRequestHandler<,>` (payload-typed), `ISender`, the generated `AddNorseIdentityWebServerHandlers()` (Asgard's registration generator, which arrives via the `Abstractions.Web.Server` package/NorseRef), Heimdall's marked requests (Task 10).
- Produces: `AddNorseAuthenticationService(connectionString)` keeps its public signature — Yggdrasil's Program.cs line does not change. Internally it now chains the generated registration instead of six hand-written lines.

- [ ] **Step 1: Realign the three handlers (tests first).** Handler declarations become payload-typed and **drop inline validation** — `ValidationBehavior` owns it now (a handler that validates is validating twice):

```csharp
// LoginHandler.cs — new declaration + validation block deleted; SignInManager logic byte-identical
sealed class LoginHandler(SignInManager<NorseUser> signInManager) : IRequestHandler<LoginRequest, BoolResponse>
{
	public async ValueTask<Outcome<BoolResponse>> Handle(LoginRequest request, CancellationToken cancellationToken = default)
	{
		// (validator ctor parameter and the ValidateAsync/Err(Validation) block are deleted —
		//  ValidationBehavior runs LoginRequestValidator before this handler is ever reached)
		var result = await signInManager.PasswordSignInAsync(
			request.Email, request.Password, request.RememberMe, lockoutOnFailure: true).ConfigureAwait(false);
		// LockedOut / NotAllowed / anti-enumeration collapse: unchanged from the current file.
		...
	}
}
```

`RegisterHandler` likewise: `IRequestHandler<RegisterRequest, BoolResponse>`, validator dep + inline block deleted, `UserManager` logic unchanged. `LogoutHandler`: `IRequestHandler<LogoutRequest, Unit>`, body unchanged. Update handler tests: no validator to arrange; invalid-input tests move conceptually to `ValidationBehavior` (already covered in Midgard Task 5) — handler tests keep only domain behavior (lockout, duplicate email, sign-out).

- [ ] **Step 2: `AuthenticationService` → `ISender`** (mapping switches unchanged, CT now flows from the contract):

```csharp
public sealed class AuthenticationService(
	ISender sender, IDeferredSignIn deferredSignIn, IHttpContextAccessor httpContextAccessor) : IAuthenticationService
{
	[Authorize(Policy = AuthNPolicies.Public)]
	public async Task<Outcome<LoginResult>> Login(LoginRequest request, CancellationToken cancellationToken = default)
	{
		var outcome = await sender.Send(request, cancellationToken).ConfigureAwait(false);
		return outcome switch
		{
			Success<BoolResponse>(var value) => Outcome<LoginResult>.Ok(new LoginResult { Succeeded = value.Value, DeferredCompletionUrl = TryGetDeferredCompletionUrl() }),
			Failed(var problem) => new Outcome<LoginResult>(new Failed(problem)),
		};
	}
	// Register/Logout: same shape; TryGetDeferredCompletionUrl() unchanged.
}
```

Update the class doc comment: the handlers run behind Midgard's pipeline now (validation, authorization, telemetry, exception translation), reached through Asgard's `ISender` — Himinbjörg remains Midgard-blind.

- [ ] **Step 3: Registration goes generated.** In `ServiceCollectionExtensions.AddNorseAuthenticationService`: delete the three validator lines and three handler lines; add `services.AddNorseIdentityWebServerHandlers();` (generated). Keep `AddScoped<IAuthenticationService, AuthenticationService>()`, Identity/DbContext wiring untouched. csproj: the registration generator arrives with `Abstractions.Web.Server` — dev mode needs the analyzer leg: `<NorseRef Include="Abstractions.Web.Server" Generator="true"><Repo>Asgard</Repo></NorseRef>` (add `Generator="true"` to the existing NorseRef).

- [ ] **Step 4: Composition assertion test (wired-not-designed policy):**

```csharp
// Himinbjorg/tests/Identity.Web.Server.Tests/RegistrationCompositionTests.cs
[Fact]
void AddNorseAuthenticationService_registers_handlers_dispatch_entries_and_validators()
{
	var services = new ServiceCollection();
	services.AddNorseAuthenticationService("Host=localhost;Database=test");

	services.ShouldContain(d => d.ServiceType == typeof(IRequestHandler<LoginRequest, BoolResponse>));
	services.ShouldContain(d => d.ServiceType == typeof(IRequestHandler<RegisterRequest, BoolResponse>));
	services.ShouldContain(d => d.ServiceType == typeof(IRequestHandler<LogoutRequest, Unit>));
	services.Count(d => d.ServiceType == typeof(ISenderDispatch)).ShouldBe(3);
	services.ShouldContain(d => d.ServiceType == typeof(IValidator<LoginRequest>));
	services.ShouldContain(d => d.ServiceType == typeof(IValidator<RegisterRequest>));
}
```

(No `IValidator<LogoutRequest>` assertion — `LogoutRequest` has no validator class, and that absence is the point: the old `InlineValidator<LogoutRequest>` hack dies with the required-validator constraint.)

- [ ] **Step 5: Full Himinbjörg suite green; update `Himinbjorg/CLAUDE.md` + `README.md`; commit**

```bash
git add -A Himinbjorg
git commit -m "feat!: handlers payload-typed behind the pipeline; AuthenticationService sends; registration generated"
```

---

## SHIP GATE — Himinbjörg

PR merged, CI green, tag pushed, `Norse.Identity.Web.Server` package live.

---

## Task 13: Yggdrasil — server host adopts the pipeline and generated wiring

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Server/Program.cs`, `Hosting.Web.Server.csproj`
- Delete: `Yggdrasil/src/Hosting.Web.Server/Generated/` (tree), `EnvelopeHydrationState.cs`, `Yggdrasil/tests/Hosting.Web.Server.Tests/EnvelopeHydrationStateTests.cs`
- Modify: `Yggdrasil/Directory.Packages.props` (CPM pins: new Asgard/Midgard/Heimdall/Himinbjörg versions; add `Grpc.AspNetCore.Web`)

**Interfaces:**
- Consumes: `AddNorsePipeline()` (Midgard), generated `MapNorseGrpcServices()` (Midgard server wiring generator — arrives with the `Norse.Infrastructure.Web.Server` package/NorseRef `Generator="true"`), `AddNorseAuthenticationService` (Himinbjörg, unchanged signature).

- [ ] **Step 1: csproj surgery.** Delete from `Hosting.Web.Server.csproj`: `<NorseGatewayEmissionMode>`, `<EmitCompilerGeneratedFiles>`, `<CompilerGeneratedFilesOutputPath>`, the `<Compile Remove="Generated/**">` item, the `<CompilerVisibleProperty Include="NorseGatewayEmissionMode" />` item and both explanatory comment blocks. Add `Generator="true"` to the `Infrastructure.Web.Server` NorseRef. `git rm -r` the `Generated/` tree and `EnvelopeHydrationState.cs` + its tests.

- [ ] **Step 2: Program.cs.** Replace the gateway/hydration lines:

```csharp
builder.Services
	.AddSingleton<IEmailSender<NorseUser>, IdentityNoOpEmailSender>()
	.AddNorsePipeline()          // Midgard: behaviors in law order, PrincipalAccessor, Sender
	.AddNorseCodeFirstGrpc()     // Midgard: Unhandled → Seeding → Outcome interceptor stack
	.AddNorseAuthenticationService(norseIdentityConnectionString)
	.AddDeferredSignIn()
	.AddGrpcReflection();
```

(`.AddScoped<IAuthenticationGateway, AuthenticationInProcessGateway>()` and `.AddScoped<EnvelopeHydrationState>()` are deleted.) And the endpoint block: `app.MapGrpcService<AuthenticationService>();` becomes `app.MapNorseGrpcServices();` (generated: surrogates + `UseGrpcWeb` + every discovered service with `.EnableGrpcWeb()`).

- [ ] **Step 3: Composition assertion tests (wired-not-designed policy)** — in `Yggdrasil/tests/Hosting.Web.Server.Tests/CompositionTests.cs`: build the real `WebApplicationBuilder` service collection (extract Program.cs's registration into a testable static or use `WebApplicationFactory`), assert: `ISender` resolvable in a scope; `IConfigureOptions<GrpcServiceOptions>` yields the three interceptors in `[Unhandled, Seeding, Outcome]` order; `RuntimeTypeModel.Default[typeof(Outcome<LoginResult>)]` has a surrogate after `MapNorseGrpcServices()` runs (each assertion fails if its registration is removed).

- [ ] **Step 4: Solution builds + tests green; commit**

```bash
git add -A Yggdrasil
git commit -m "feat!: server host on AddNorsePipeline + generated gRPC wiring; gateway artifacts deleted"
```

---

## Task 14: Yggdrasil — WASM client host adopts generated wiring

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Client/Program.cs`, `Hosting.Web.Client.csproj`
- Delete: `Yggdrasil/src/Hosting.Web.Client/Generated/` (tree)
- Modify: `Yggdrasil/src/Hosting.Stories.Client/FakeAuthenticationGateway.cs` → rename `FakeAuthenticationService.cs`, implement `IAuthenticationService` (same canned outcomes, new interface + CT params)

**Interfaces:**
- Consumes: generated `AddNorseGrpcClients(channel)` (Midgard client wiring generator).

- [ ] **Step 1: csproj surgery** — same deletions as Task 13 (`NorseGatewayEmissionMode`, `CompilerVisibleProperty`, `EmitCompilerGeneratedFiles` block, `Generated/**` remove item); add `Generator="true"` to the `Infrastructure.Web.Client` NorseRef; `git rm -r Generated/`.

- [ ] **Step 2: Program.cs** — channel construction stays hand-written (host policy: base address, `GrpcWebHandler`, `BrowserCredentialsHandler`); the two registration lines become one:

```csharp
builder.Services.AddNorseGrpcClients(authNChannel);
```

(`AddSingleton(authNChannel.CreateGrpcService<IAuthenticationService>())` and the wire-gateway line are deleted — the generated method registers the proxy through `OutcomeClientInterceptor`.)

- [ ] **Step 3: Stories fake re-pointed; both client projects and Stories build; commit**

```bash
git add -A Yggdrasil
git commit -m "feat!: WASM host on generated AddNorseGrpcClients; wire gateway deleted"
```

---

## Task 15: Yggdrasil — the circuit net (`ErrorBoundary` + `CircuitHandler`)

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Components/Layout/MainLayout.razor` (verify exact layout file first: `ls Yggdrasil/src/Hosting.Web.Components/Layout/`; wrap whatever renders `@Body`)
- Create: `Yggdrasil/src/Hosting.Web.Server/LoggingCircuitHandler.cs`
- Modify: `Yggdrasil/src/Hosting.Web.Server/Program.cs` (register the handler)

**Interfaces:**
- Consumes: nothing new. Covers what the pipeline cannot (spec §2.9): lifecycle exceptions outside a `Send`.

- [ ] **Step 1: ErrorBoundary around the body**

```razor
<ErrorBoundary @ref="_errorBoundary">
	<ChildContent>@Body</ChildContent>
	<ErrorContent>
		<div class="norse-fault">
			<p>An unexpected error occurred. The details have been logged.</p>
			<button @onclick="() => _errorBoundary?.Recover()">Try again</button>
		</div>
	</ErrorContent>
</ErrorBoundary>

@code {
	ErrorBoundary? _errorBoundary;
}
```

(Blazor's `ErrorBoundary` logs the exception server-side itself with the circuit's logger; the platform's correlation-id vocabulary for these is the CircuitHandler's job below. Styling rides the existing theme tokens — no new design work.)

- [ ] **Step 2: `LoggingCircuitHandler`**

```csharp
// Yggdrasil/src/Hosting.Web.Server/LoggingCircuitHandler.cs
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Norse.Hosting.Web.Server;

/// <summary>
/// The circuit's lifecycle net (spec §2.9): logs open/close/connection-down with a correlation id in
/// the platform's vocabulary, so a torn circuit is a traceable event, not a silent reconnect modal.
/// </summary>
sealed partial class LoggingCircuitHandler(ILogger<LoggingCircuitHandler> logger) : CircuitHandler
{
	public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
	{
		LogOpened(logger, circuit.Id);
		return Task.CompletedTask;
	}

	public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
	{
		LogConnectionDown(logger, circuit.Id);
		return Task.CompletedTask;
	}

	public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
	{
		LogClosed(logger, circuit.Id);
		return Task.CompletedTask;
	}

	[LoggerMessage(Level = LogLevel.Debug, Message = "Circuit {CircuitId} opened")]
	static partial void LogOpened(ILogger logger, string circuitId);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Circuit {CircuitId} connection down")]
	static partial void LogConnectionDown(ILogger logger, string circuitId);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Circuit {CircuitId} closed")]
	static partial void LogClosed(ILogger logger, string circuitId);
}
```

Register: `.AddScoped<CircuitHandler, LoggingCircuitHandler>()` in Program.cs (scoped = per circuit).

- [ ] **Step 3: Build + existing tests green; commit**

```bash
git add -A Yggdrasil
git commit -m "feat: circuit net — layout ErrorBoundary + LoggingCircuitHandler"
```

---

## Task 16: Yggdrasil — acceptance proof: parity for real, wire validation regression

**Files:**
- Create: `Yggdrasil/tests/Hosting.Web.Server.Tests/MediatorParityTests.cs`
- Delete: `Yggdrasil/tests/Hosting.Web.Server.Tests/AuthenticationHydrationParityTests.cs` (its fake-both-sides shape is what this replaces)

**Interfaces:**
- Consumes: everything. This is spec §5 — the test the old design could never write.

- [ ] **Step 1: Write the tests.** One self-contained TestServer host reused by all three:

```csharp
// Yggdrasil/tests/Hosting.Web.Server.Tests/MediatorParityTests.cs
// Host arrangement (shared fixture): WebApplicationBuilder with TestServer, services:
//   AddLogging + AddAuthorizationBuilder().AddPolicy(AuthNPolicies.Public, p => p.RequireAssertion(_ => true))
//   + AddNorsePipeline() + AddNorseCodeFirstGrpc() + AddRouting()
//   + a stub IRequestHandler<LoginRequest, BoolResponse> returning Outcome.Err(ErrorCategory.LockedOut,
//     errors: { [""]: ["locked"] }) + its SenderDispatch registration
//   + a TestAuthenticationService : IAuthenticationService that Sends (the Himinbjorg shape, minus Identity)
// Pipeline: UseRouting + MapNorseGrpcServices(). Client channel: GrpcChannel.ForAddress(server.BaseAddress,
//   new() { HttpHandler = server.CreateHandler() }) — then AddNorseGrpcClients-equivalent wiring
//   (Intercept(OutcomeClientInterceptor) + CreateGrpcService<IAuthenticationService>).

[Fact]
async Task LockedOut_renders_identically_through_the_circuit_path_and_the_wire_path()
{
	// Circuit path: resolve ISender from a server-side scope (seed PrincipalAccessor), Send(LoginRequest).
	// Wire path: call the gRPC client proxy over the TestServer channel.
	// Assert both: TryGetValue(out Failed failed) && failed.Problem.Category == ErrorCategory.LockedOut
	// && failed.Problem.Errors[""] single "locked" — same category, same errors, side by side.
}

[Fact]
async Task Wire_path_requests_are_validated_server_side()
{
	// Register the real LoginRequestValidator (Heimdall) in the host. Send an empty-email LoginRequest
	// over the WIRE path. Assert Failed(Validation) with a populated Errors dictionary — impossible
	// before this design: the old wire path had no validator in it (audit finding, spec §5.4).
}

[Fact]
async Task A_handler_throw_reaches_the_wire_client_as_Fault_with_a_correlation_id()
{
	// Swap the stub handler for one that throws. Wire call → ExceptionTranslationBehavior converts in-
	// process → Failed(Fault) → OutcomeServerInterceptor throws → trailer → client decoder → Failed(Fault)
	// with CorrelationId — the full round trip of spec §2.1's matrix, every registration load-bearing.
}
```

Write them with the full host arrangement inline (no placeholders in the actual test file) — the comment blocks above are the specification of each body.

- [ ] **Step 2: Run — all three must fail meaningfully first** (e.g. against a host missing `AddNorsePipeline` to prove the assertions bite), then pass against the real composition.

- [ ] **Step 3: Full Yggdrasil suite green. Package-mode proof:** `dotnet build Yggdrasil/Yggdrasil.slnx -p:UseProjectReferences=false` — the true package-mode build the 07-24 plan's final gate deferred; it must succeed against the published Asgard/Midgard/Heimdall/Himinbjörg packages.

- [ ] **Step 4: Commit**

```bash
git add -A Yggdrasil
git commit -m "test: real parity + wire-validation acceptance — every registration has a test that fails without it"
```

---

## SHIP GATE — Yggdrasil

PR merged, CI green, tag pushed. This gate includes the package-mode build (Task 16 Step 3) — the deferred obligation from the 2026-07-24 plan's final gate, now dischargeable because the IVT/CompilerVisibleProperty blockers no longer exist.

---

## Task 17: Platform docs reconciliation

**Files:**
- Modify: `Bifrost/CLAUDE.md`, `Bifrost/README.md`, `Glitnir/CLAUDE.md`, `Glitnir/docs/decomposition.md`, `Glitnir/docs/Platform/specs/2026-05-26-mediator-design.md`, `Glitnir/docs/Platform/specs/2026-07-24-transport-neutral-invocation-pipeline-design.md`, `Glitnir/docs/Heimdall/specs/2026-07-15-blazor-validation-poc.md`

**Steps (one commit per repo, no code):**

- [ ] **Step 1: Bifröst CLAUDE.md + README.md.** Remove Open Decision #2 (dissolved — the property no longer exists). Rewrite the 2026-07-25 state-of-the-union paragraph: the transport-neutral pipeline now runs through the hand-rolled mediator (`AddNorsePipeline` + `ISender`), the gateway generator and emission modes are deleted, and the martinothamar/Mediator claim is corrected (it was never a dependency — the pipeline is and was hand-rolled). Fix the stale "fixed and staged in Midgard, not yet shipped" IVT sentence (the grant is deleted entirely).
- [ ] **Step 2: Glitnir CLAUDE.md §4 key rejections row:** `MediatR` → becomes `MediatR, martinothamar/Mediator` / use instead: `hand-rolled Norse pipeline (ISender + IBehavior fold)` / spec: `docs/Platform/specs/2026-07-27-mediator-pipeline-retires-gateway-design.md`. Same correction in `docs/decomposition.md`'s Midgard row.
- [ ] **Step 3: Supersession notices.** Top of `2026-05-26-mediator-design.md` and `2026-07-24-transport-neutral-invocation-pipeline-design.md`: a dated one-paragraph banner naming what the 2026-07-27 design supersedes (mediator selection; gateway surface/generator packaging/chain composition/hydration decided law) and what survives (envelope, wire encoding, behavior semantics). One-line note in the 07-15 blazor-validation POC where it names martinothamar. **When the desktop b2 brief lands in Glitnir, annotate its ledger:** `RuntimeTypeModel.Default` is the sanctioned surrogate home (generated wiring guarantees per-type idempotent registration), superseding the brief's dedicated-model instruction — see Task 9.
- [ ] **Step 4: Stage everything, run nothing — docs only. The human commits Glitnir.**

---

## Execution notes

- Realm branches: `feature/mediator-pipeline` in Asgard, Midgard, Heimdall, Himinbjörg; Yggdrasil continues on `feature/hosting-web-server-authn` or a successor — Buvy's call at execution time. Bifröst itself stays on `master` throughout; submodule pointer bumps ride the normal bot flow.
- Between the Asgard gate and the Yggdrasil gate the Bifröst-wide dev build is red (Global Constraints). Realm-local `.slnx` green is the bar at every gate.
- Task 16's package-mode build is the plan's final acceptance — it retroactively discharges the 2026-07-24 plan's deferred ship-gate obligation.
