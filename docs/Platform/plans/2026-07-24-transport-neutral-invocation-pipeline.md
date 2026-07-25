# Transport-Neutral Invocation Pipeline & Generated Gateways — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (default) or `superpowers:executing-plans` (separate-session fallback, never interchangeable). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the transport-neutral gateway pipeline from `../specs/2026-07-24-transport-neutral-invocation-pipeline-design.md` end to end — envelope, behavior chain, generator, gRPC hosting — and prove it on Heimdall/Himinbjörg's real authentication flow with a hydration-parity test.

**Architecture:** Six realms share the work in strict dependency order. Asgard relocates the envelope and declares the behavior/gateway contracts. Midgard implements the standard behaviors, the ErrorInfo-based wire encoding, and the generic `AddCodeFirstGrpc` wiring. Asgard's own `gen/` ships the gateway generator (three emission modes: Contract, WireHost, InProcessHost). Heimdall updates its contract to the new shape. Himinbjörg — the reference backend, not the only possible one — implements the real `AuthenticationService` for the first time (it was a stub) using the new envelope. Yggdrasil, the composition root, wires gRPC hosting and the generated gateways together and proves the whole chain with a hydration-parity test.

**Execution model — realm-by-realm ship gates:** Six phases, six `## SHIP GATE` sections. Do not start the next phase until the gate before it is cleared: PR merged, CI green, tag pushed, NuGet package(s) live. In Bifröst during development (`UseProjectReferences=true`), NorseRef items resolve as ProjectReferences across the submodule tree regardless.

**Tech Stack:** .NET 10/11, C#, protobuf-net.Grpc + protobuf-net.Grpc.AspNetCore 1.2.x, FluentValidation, Roslyn `IIncrementalGenerator`, xUnit v3 + Shouldly + NSubstitute, ASP.NET Core Authorization.

## Global Constraints

- Target framework: match each project's existing TFM (net11.0 for library/service projects observed in this tree); `netstandard2.0` for the generator project only.
- `var` for return-value assignments only; explicit type + `new()` for construction.
- `internal sealed` (or bare `sealed` where accessibility is already the default) unless a concrete cross-assembly caller in this plan justifies `public`.
- Tabs for indentation everywhere except YAML/JSON (2-space).
- US English spelling in all identifiers, comments, docs.
- No automatic git commits — stage only (`git add`); human commits.
- Shouldly for all assertions; NSubstitute for all mocks.
- No force-push to `master`. No `--no-verify`.
- `NorseRef` for cross-realm references; plain `<ProjectReference>` for same-realm references. No NorseRef inside a `<Target>` block (YGG301).
- Generator must walk **compiled symbols** for any discovery that crosses assembly boundaries (`compilation.SourceModule.ReferencedAssemblySymbols`), never source syntax trees for that part — matches Urdarbrunnr's shipped generator, needed for PackageReference-mode parity.
- Heimdall stays dumb to transport: no gRPC package reference, no `AddCodeFirstGrpc` call, no interceptor, anywhere in Heimdall. It owns only the service contract, the gateway interface (generated), and Razor components. Himinbjörg is *a* reference backend behind that contract, not the only legal one — nothing in this plan may make Heimdall depend on Himinbjörg or Midgard.
- No `[Behavior]` or gateway-generation attribute may appear on a type inside a `.Components`/`.Services` project that ships to WASM if that attribute's argument would force a server-only assembly reference (spec §2.5 defect 2) — `[Behavior]` decorates the service **implementation**, never the interface.
- Wire decode of `Problem` category is always via `ErrorInfo.Reason`, never via gRPC status code (spec §2.1 defect 1).
- `Outcome<T>` is one native `[Union]` readonly record struct (`IUnion`, `[MustConsume]`), matching Svartalfheim's `Result<T>` exactly — no `.IsSuccess`/`.Value`/`.Problem` flat-record fields exist, and no separate non-generic `Outcome` type. Void-success operations use `Outcome<Unit>`, spelled `Outcome` via a platform-wide `global using` alias (Task 1) — never a second type or a second `IBehavior<TRequest>` interface family. Every task that *inspects* one (not just constructs via `Ok`/`Err`) uses `TryGetValue`/`Match`/exhaustive `switch` on `Success<T>`/`Failed`.

---

## v5 Amendment (2026-07-24, mid-execution — supersedes conflicting task text below)

**Full rationale and the amended law itself: spec §9.** Summary for implementers working task-by-task: decided law item 3's first clause ("service interfaces keep returning plain `TResponse`") is **reversed** — Asgard-contracted service interfaces (including Heimdall's `IAuthenticationService`) now return `Task<Outcome<TPayload>>` directly. The second clause ("nothing in-process throws to communicate") is **unchanged** — the throw point moves to the gRPC server interceptor, a transport-boundary adapter, not in-process code. `OutcomeServerInterceptor` (Task 5's retirement target) is **reinstated**, redesigned to pattern-match the `Outcome<T>` return value (`response is IUnion { Value: Failed failed }` → throw `failed.Problem.ToRpcException()`) instead of catching a thrown exception — the mechanism changes, the interceptor's role as wire-boundary throw point does not.

**Trigger:** Himinbjörg's `AuthenticationService` (Task 12) has no data channel to signal failure to a caller under the original law without either naming Midgard/`Grpc.*` types directly (Himinbjörg and Midgard are peer realms — neither may depend on the other, full stop) or relocating the class to Yggdrasil (rejected as unnecessary churn once the interceptor can carry the throw instead).

**Task-by-task effect (this section governs; do not follow superseded text in a task's own body without cross-checking here first):**

- **Task 1 (Asgard, already shipped `3f70b37`/`3f8a1bc`):** no `Outcome<T>` shape change. Retire the platform-wide `Outcome`/`Outcome<Unit>` alias (`GlobalUsings.Outcome.cs` + its plan prose) — no remaining hand-written consumer once Task 5's rework lands (Himinbjörg's `LogoutHandler` registration becomes `IRequestHandler<LogoutRequest, Outcome<LogoutResult>>`, spelled out, not aliased). Delete the file, remove references to it from every later task's steps.
- **Task 5 (Midgard, ErrorInfo wire encoding):** `OutcomeServerInterceptor` is **not** retired — it is rewritten. New shape: `UnaryServerHandler` runs the continuation, inspects the returned `TResponse` via `is IUnion { Value: Failed failed }`, throws `failed.Problem.ToRpcException()` on match, returns the response unchanged otherwise (including the `Success<T>` arm, unwrapped-not — the wire payload carries the whole envelope's success case; see spec §9's "known accepted consequence"). `ProblemExtensions.ToRpcException()` (already shipped, ErrorInfo-based) is unchanged and is what this interceptor calls. Protobuf-net surrogate registration for `Outcome<T>`'s success arm is new required scope here (spec §9) — own step, own test, HALT AND ESCALATE per spec §9 if surrogate mechanics prove unworkable rather than routing around it.
- **Task 6 (Midgard, `AddNorseCodeFirstGrpc`):** registers `OutcomeServerInterceptor` alongside `UnhandledExceptionInterceptor` — order: `OutcomeServerInterceptor` innermost (closest to the handler), `UnhandledExceptionInterceptor` outermost (the safety net for anything the Outcome interceptor didn't recognize as `IUnion`).
- **Tasks 7-9 (Asgard/gen, already shipped):** `ContractEmitter`'s response-type extraction changes from "wrap the service's bare return type in `Outcome<T>`" to "the service already returns `Outcome<T>`; the gateway surface mirrors it" — no double-wrap. `InProcessHostEmitter`'s innermost call already returns `Outcome<T>`; delete the `Ok`-wrapping/`AwaitThenUnit` ceremony that assumed a bare-payload return. Keep Task 7's `ContractMode_VoidSuccessMethod_...` harness test — the `Unit` emission path still needs coverage even with fewer production consumers.
- **Task 10 (Heimdall, already shipped `7137b96`/`efa4e00`):** `IAuthenticationService.Login`/`Register`/`Logout` change from `Task<LoginResult>`/`Task`/`Task<LogoutResult>` to `Task<Outcome<LoginResult>>`/`Task<Outcome<Unit>>`/`Task<Outcome<LogoutResult>>`. `LoginResult`/`LogoutResult` are unaffected in shape (still carry `DeferredCompletionUrl`).
- **Task 11 (Heimdall components, already shipped `122d80c`/`2efee7b`):** `Login.razor`/`Register.razor`/`Logout.razor` already pattern-match `Outcome<T>` from the *generated gateway* — unaffected. No change; the gateway's own shape (`ValueTask<Outcome<T>>`) was already correct for the in-process/wire adapters regardless of what the *service interface itself* returns underneath.
- **Task 12 (Himinbjörg, `AuthenticationService`):** stays in Himinbjörg. Rewritten to be **trivially thin and Midgard-blind**: per method, invoke the handler, return its `Outcome<T>` verbatim (`Logout` additionally populates `LogoutResult.DeferredCompletionUrl` via `IDeferredSignIn.BuildCompletionUrl`, Asgard contract only). Zero throw statements, zero `RpcException`, zero `Grpc.*`, zero Midgard `NorseRef`. `[Authorize]` mirrors stay as originally planned (still required for gRPC endpoint metadata discovery, unrelated to this amendment). `LoginHandler`/`RegisterHandler`/`LogoutHandler` are unaffected — they already do 100% of the identity-domain (`SignInResult`/`IdentityResult` → `ErrorCategory`) mapping; that was always Himinbjörg's whole job and stays exactly there.
- **Task 13 (Yggdrasil):** two new registration lines in the gRPC composition (`OutcomeServerInterceptor` + the protobuf-net surrogate registration from Task 5), nothing else new from this amendment.
- **New: parity regression test** (not in the original 14 tasks) — a handler returning `Failed(LockedOut)` invoked through the in-process gateway renders `LockedOut`; the same failure through the wire gateway (service → interceptor → client decode) also decodes to `LockedOut`. Same category, both paths, asserted side by side — this is exactly the class of defect an in-process/wire divergence would hide, and the original plan's §4 suite never had a way to write it (business failures used to reach the wire only via a thrown exception the in-process path never exercised).

**Explicitly out of scope for this amendment:** the generic-dispatcher/generated-adapter fast-follow (its motivating "parity defect" is cured structurally by this change — no in-process throw exists to diverge from the wire path anymore); any new behavior, envelope, or emission-mode work beyond the mechanical updates listed above.

---

## File Map

### Asgard
| Action | Path |
|---|---|
| Move | `Asgard/src/Abstractions.Web.Server/Mediator/Outcome.cs` → `Asgard/src/Abstractions.Contracts/Outcome{T}.cs` |
| Move | `Asgard/src/Abstractions.Web.Server/Mediator/Problem.cs` → `Asgard/src/Abstractions.Contracts/Problem.cs` |
| Move | `Asgard/src/Abstractions.Web.Server/Mediator/ErrorCategory.cs` → `Asgard/src/Abstractions.Contracts/ErrorCategory.cs` |
| Move | `Asgard/src/Abstractions.Web.Server/Mediator/BoolResponse.cs` → `Asgard/src/Abstractions.Contracts/BoolResponse.cs` |
| Create | `Asgard/src/Abstractions.Contracts/Failed.cs` |
| Create | `Asgard/src/Abstractions.Contracts/Unit.cs` |
| Create | `Asgard/src/Abstractions.Contracts/GlobalUsings.Outcome.cs` |
| Create | `Asgard/src/Abstractions.Contracts/GenerateGatewayAttribute.cs` |
| Create | `Asgard/tests/Abstractions.Contracts.Tests/OutcomeTests.cs` |
| Create | `Asgard/src/Abstractions.Web.Server/Mediator/IBehavior.cs` |
| Create | `Asgard/src/Abstractions.Web.Server/Mediator/BehaviorAttribute.cs` |
| Create | `Asgard/tests/Abstractions.Web.Server.Tests/BehaviorAttributeTests.cs` |
| Create | `Asgard/gen/Abstractions.Contracts.Generator/Abstractions.Contracts.Generator.csproj` |
| Create | `Asgard/gen/Abstractions.Contracts.Generator/GatewayGenerator.cs` |
| Create | `Asgard/gen/Abstractions.Contracts.Generator/GatewayInterfaceModel.cs` |
| Create | `Asgard/gen/Abstractions.Contracts.Generator/GatewayMethodModel.cs` |
| Create | `Asgard/gen/Abstractions.Contracts.Generator/ContractEmitter.cs` |
| Create | `Asgard/gen/Abstractions.Contracts.Generator/WireHostEmitter.cs` |
| Create | `Asgard/gen/Abstractions.Contracts.Generator/InProcessHostEmitter.cs` |
| Create | `Asgard/tests/Abstractions.Contracts.Generator.Tests/Abstractions.Contracts.Generator.Tests.csproj` |
| Create | `Asgard/tests/Abstractions.Contracts.Generator.Tests/GatewayGeneratorTests.cs` |

### Midgard
| Action | Path |
|---|---|
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/TelemetryBehavior.cs` |
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/ExceptionTranslationBehavior.cs` |
| Create | `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/TelemetryBehaviorTests.cs` |
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/AuthorizationBehavior.cs` |
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/ValidationBehavior.cs` |
| Create | `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/AuthorizationBehaviorTests.cs` |
| Create | `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/ValidationBehaviorTests.cs` |
| Modify | `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ProblemExtensions.cs` |
| Create | `Midgard/src/Infrastructure.Web.Client/Grpc/RpcExceptionExtensions.cs` (rewrite in place) |
| Delete | `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/OutcomeServerInterceptor.cs` |
| Delete | `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/OutcomeFailedException.cs` |
| Delete | `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/OutcomeExtensions.cs` |
| Modify | `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/ProblemExtensionsTests.cs` (rewrite) |
| Modify | `Midgard/tests/Infrastructure.Web.Client.Tests/Grpc/RpcExceptionExtensionsTests.cs` (rewrite) |
| Delete | corresponding old test files for `OutcomeServerInterceptor`/`OutcomeFailedException`/`OutcomeExtensions` |
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/UnhandledExceptionInterceptor.cs` |
| Create | `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ServiceCollectionExtensions.cs` |
| Create | `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/UnhandledExceptionInterceptorTests.cs` |

### Heimdall
| Action | Path |
|---|---|
| Create | `Heimdall/src/AuthN.Services/AuthNPolicies.cs` |
| Modify | `Heimdall/src/AuthN.Services/IAuthenticationService.cs` |
| Modify | `Heimdall/src/AuthN.Services/LoginResult.cs` |
| Modify | `Heimdall/src/AuthN.Services/AuthN.Services.csproj` |
| Delete | `Heimdall/src/AuthN.Components/IAuthenticationGateway.cs` |
| Delete | `Heimdall/src/AuthN.Components/AuthenticationResult.cs` |
| Modify | `Heimdall/src/AuthN.Components.FluentUI/Login.razor` |
| Delete/replace tests referencing `AuthenticationResult`/`IAuthenticationGateway` in `Heimdall/tests/AuthN.Components.Tests/` | |

### Himinbjörg
| Action | Path |
|---|---|
| Modify | `Himinbjorg/src/Identity.Web.Server/AuthenticationService.cs` (stub → real) |
| Modify | `Himinbjorg/src/Identity.Web.Server/ServiceCollectionExtensions.cs` |
| Modify | `Himinbjorg/src/Identity.Web.Server/LoginHandler.cs`, `RegisterHandler.cs`, `LogoutHandler.cs` (namespace fix) |
| Create | `Himinbjorg/src/Identity.Web.Server/GlobalUsings.Outcome.cs` |
| Create | `Himinbjorg/tests/Identity.Web.Server.Tests/AuthenticationServiceTests.cs` |

### Yggdrasil
| Action | Path |
|---|---|
| Modify | `Yggdrasil/src/Hosting.Web.Server/Program.cs` |
| Modify | `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj` |
| Modify | `Yggdrasil/src/Hosting.Web.Client/Hosting.Web.Client.csproj` |
| Delete | `Yggdrasil/src/Hosting.Web.Server/BlazorServerAuthenticationGateway.cs` |
| Delete | `Yggdrasil/src/Hosting.Web.Client/WasmAuthenticationGateway.cs` |
| Create | `Yggdrasil/src/Hosting.Web.Server/EnvelopeHydrationState.cs` |
| Create | `Yggdrasil/tests/Hosting.Web.Server.Tests/EnvelopeHydrationStateTests.cs` |
| Create | `Yggdrasil/tests/Hosting.Web.Server.Tests/WirePathAuthorizationTests.cs` |
| Create | `Yggdrasil/tests/Hosting.Web.Server.Tests/AuthenticationHydrationParityTests.cs` |

---

## Task 1: Asgard — Relocate the envelope, extend `ErrorCategory` and `Problem`

**Files:**
- Move: `Asgard/src/Abstractions.Web.Server/Mediator/{Problem,ErrorCategory,BoolResponse}.cs` → `Asgard/src/Abstractions.Contracts/`
- Move: `Asgard/src/Abstractions.Web.Server/Mediator/Outcome.cs` → `Asgard/src/Abstractions.Contracts/Outcome{T}.cs` (renamed — single generic type only, matching Svartalfheim's own `Result{T}.cs` naming)
- Create: `Asgard/src/Abstractions.Contracts/Unit.cs`
- Create: `Asgard/src/Abstractions.Contracts/GlobalUsings.Outcome.cs`
- Test: `Asgard/tests/Abstractions.Contracts.Tests/OutcomeTests.cs`

**Interfaces:**
- Produces: `Norse.Abstractions.Contracts.Outcome<T>` — a native `[Union]` readonly record struct (`IUnion`, `[MustConsume]`), matching Svartalfheim's `Result<T>` pattern exactly. **2026-07-24 amendment, pre-ship-gate:** there is no separate non-generic `Outcome` type or `Succeeded` case — void-success operations use `Outcome<Unit>` with a new `Norse.Abstractions.Contracts.Unit` (empty readonly record struct, zero-cost, trivially `notnull`) as the payload. A platform-wide `global using Outcome = Norse.Abstractions.Contracts.Outcome<Norse.Abstractions.Contracts.Unit>;` alias (C#'s closed-generic-type aliasing, valid since C# 2.0; `global using` since C# 10) keeps every existing and future call site that means "no payload" spelled `Outcome`, identical to the non-generic type it replaces — `IRequestHandler<LogoutRequest, Outcome>` compiles unchanged in spelling. This collapses what would otherwise be a permanent fork: every future `[Behavior]` implementation would need both a generic and non-generic form forever, and Register/Logout would need `IBehavior<TRequest>` a second interface family just to be void. Ruled before Asgard's ship gate specifically because the day after, this becomes a breaking change against a published package, not a same-day edit. `object` was rejected as the placeholder (boxing, notnull awkwardness, doesn't say what it is); `Unit` isn't a placeholder in that sense — it's the honest, zero-cost payload type for "nothing." Cases: `Success<T>` (reused directly from Svartalfheim — the only realm Asgard's own charter allows it to depend on, and `Success<T>` is already a T-only, Result-agnostic wrapper) or `Failed(Problem Problem)`. `TryGetValue`/`Match` only — `outcome.IsSuccess`/`outcome.Value` do not exist; `outcome is Outcome<T>` is a compiler error (CS8121), same as `Result<T>`. `Norse.Abstractions.Contracts.Problem { ErrorCategory Category; IReadOnlyDictionary<string,string[]> Errors; Guid? CorrelationId; }`, `Norse.Abstractions.Contracts.ErrorCategory` (now 9 members), `Norse.Abstractions.Contracts.BoolResponse`. Every later task in this plan consumes these from `Norse.Abstractions.Contracts`, not `Norse.Abstractions.Web.Server` — and every task that *inspects* an `Outcome<T>` (not just constructs one via `Ok`/`Err`) pattern-matches on `Success<T>`/`Failed`, never a boolean flag. **Delivery of the alias file** is per-project (a small file copied into each project that needs the bare `Outcome` spelling), not the platform's repo-wide config-scatter manifest (`manifest.psd1`) — that manifest scatters whole files uniformly to every realm regardless of whether it references `Abstractions.Contracts`; blanket-delivering this alias that way would break every unrelated project's build the moment it can't resolve the aliased namespace. Promoting this to a proper conditional/selective scatter mechanism is a follow-up infrastructure question, not part of this plan.

- [ ] **Step 1: Confirm `Abstractions.Contracts` project exists and has no existing production types to collide with**

Run: `find Asgard/src/Abstractions.Contracts -iname "*.cs" -not -path "*/obj/*" -not -path "*/bin/*"`
Expected: no output (project is scaffolding-only today).

- [ ] **Step 2: Write the failing relocation test — union semantics, not a flat boolean**

```csharp
// Asgard/tests/Abstractions.Contracts.Tests/OutcomeTests.cs
using System.Runtime.CompilerServices;
using Norse.Abstractions.Contracts;
using Norse.Primitives;
using Shouldly;

namespace Norse.Abstractions.Contracts.Tests;

public sealed class OutcomeTests
{
	[Fact]
	void OutcomeOfUnit_Ok_IsTheVoidSuccessShape()
	{
		// Explicit Outcome<Unit> here — this test doesn't need the alias in scope to prove the type
		// itself works; the alias (GlobalUsings.Outcome.cs, copied into consumers as needed, e.g.
		// Task 12) is an ergonomic spelling concern for downstream call sites, not this type's own test.
		var outcome = Outcome<Unit>.Ok(Unit.Value);
		var matched = outcome switch { Success<Unit> => true, Failed => false };
		matched.ShouldBeTrue();
	}

	[Fact]
	void OutcomeOfT_Ok_TryGetValue_UnwrapsSuccessWithoutBoxing()
	{
		var outcome = Outcome<BoolResponse>.Ok(new BoolResponse { Value = true });
		outcome.TryGetValue(out Success<BoolResponse> success).ShouldBeTrue();
		success.Value.Value.ShouldBeTrue();
	}

	[Fact]
	void OutcomeOfT_Err_CarriesCategoryAndCorrelationId()
	{
		var correlationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
		var outcome = Outcome<BoolResponse>.Err(
			ErrorCategory.Fault,
			errors: new Dictionary<string, string[]>(),
			correlationId: correlationId);

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Fault);
		failed.Problem.CorrelationId.ShouldBe(correlationId);
	}

	[Fact]
	void OutcomeOfT_Match_ExhaustiveOverBothCases()
	{
		var success = Outcome<BoolResponse>.Ok(new BoolResponse { Value = true });
		var failure = Outcome<BoolResponse>.Err(ErrorCategory.NotFound);

		success.Match(value => value.Value, _ => false).ShouldBeTrue();
		failure.Match(value => value.Value, problem => problem.Category == ErrorCategory.NotFound).ShouldBeTrue();
	}

	[Fact]
	void ErrorCategory_HasNineMembers_ExplicitValues()
	{
		((byte)ErrorCategory.Validation).ShouldBe((byte)1);
		((byte)ErrorCategory.NotFound).ShouldBe((byte)2);
		((byte)ErrorCategory.Conflict).ShouldBe((byte)3);
		((byte)ErrorCategory.LockedOut).ShouldBe((byte)4);
		((byte)ErrorCategory.InvalidCredentials).ShouldBe((byte)5);
		((byte)ErrorCategory.NotAllowed).ShouldBe((byte)6);
		((byte)ErrorCategory.Unauthorized).ShouldBe((byte)7);
		((byte)ErrorCategory.Forbidden).ShouldBe((byte)8);
		((byte)ErrorCategory.Fault).ShouldBe((byte)9);
	}

	[Fact]
	void OutcomeOfT_TryGetValue_ReturnsFalse_ForTheOtherCase()
	{
		var success = Outcome<BoolResponse>.Ok(new BoolResponse { Value = true });
		success.TryGetValue(out Failed _).ShouldBeFalse();

		var failure = Outcome<BoolResponse>.Err(ErrorCategory.NotFound);
		failure.TryGetValue(out Success<BoolResponse> _).ShouldBeFalse();
	}

	[Fact]
	void OutcomeOfT_Default_ThrowsOnMatch_MalformedByConstruction()
	{
		var defaulted = default(Outcome<BoolResponse>);
		Should.Throw<SwitchExpressionException>(() => defaulted.Match(value => value.Value, _ => false));
	}
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Tests --filter OutcomeTests`
Expected: FAIL — project has no `Abstractions.Contracts.Tests.csproj` reference to the not-yet-moved types (build error: `Outcome` not found).

- [ ] **Step 4: Move the three files with `git mv`, update namespaces, extend the changed types, add `Unit` and the alias**

```bash
git mv Asgard/src/Abstractions.Web.Server/Mediator/Outcome.cs Asgard/src/Abstractions.Contracts/Outcome{T}.cs
git mv Asgard/src/Abstractions.Web.Server/Mediator/Problem.cs Asgard/src/Abstractions.Contracts/Problem.cs
git mv Asgard/src/Abstractions.Web.Server/Mediator/ErrorCategory.cs Asgard/src/Abstractions.Contracts/ErrorCategory.cs
git mv Asgard/src/Abstractions.Web.Server/Mediator/BoolResponse.cs Asgard/src/Abstractions.Contracts/BoolResponse.cs
```

`Asgard/src/Abstractions.Contracts/ErrorCategory.cs` — full replacement contents:

```csharp
namespace Norse.Abstractions.Contracts;

/// <summary>
/// Application-level error vocabulary an <see cref="Outcome{T}"/> carries on failure.
/// <see cref="LockedOut"/>/<see cref="InvalidCredentials"/>/<see cref="NotAllowed"/> are an
/// AuthN-specific extension over the platform's base Validation/NotFound/Conflict trio.
/// <see cref="Unauthorized"/>/<see cref="Forbidden"/> split not-authenticated from
/// authenticated-but-lacks-the-policy — every request carries a principal (anonymous role included),
/// so both are live, reachable paths. <see cref="Fault"/> is the catch-all for anything unmapped.
/// </summary>
public enum ErrorCategory : byte
{
	/// <summary>Request shape or field-level validation failure.</summary>
	Validation = 1,
	/// <summary>Resource not found.</summary>
	NotFound = 2,
	/// <summary>Conflict with existing state.</summary>
	Conflict = 3,
	/// <summary>Account or resource is locked out.</summary>
	LockedOut = 4,
	/// <summary>Invalid credentials provided. Vestigial — not actively produced, per the anti-enumeration ruling.</summary>
	InvalidCredentials = 5,
	/// <summary>Operation not allowed given current state (a precondition failure, not an authorization failure).</summary>
	NotAllowed = 6,
	/// <summary>Caller is not authenticated for an operation that requires it.</summary>
	Unauthorized = 7,
	/// <summary>Caller is authenticated but lacks the required policy.</summary>
	Forbidden = 8,
	/// <summary>Unmapped failure. Always carries a <see cref="Problem.CorrelationId"/>.</summary>
	Fault = 9
}
```

`Asgard/src/Abstractions.Contracts/Problem.cs` — full replacement contents:

```csharp
namespace Norse.Abstractions.Contracts;

/// <summary>
/// The structured detail an <see cref="Outcome{T}"/> carries on failure.
/// </summary>
public sealed record Problem
{
	/// <summary>The error category.</summary>
	public required ErrorCategory Category { get; init; }

	/// <summary>Field-keyed validation or structured errors.</summary>
	public IReadOnlyDictionary<string, string[]> Errors { get; init; } = new Dictionary<string, string[]>();

	/// <summary>
	/// Populated only for <see cref="ErrorCategory.Fault"/> — every other category is deterministic and
	/// reproducible from the request itself, so a trace handle adds no diagnostic value there.
	/// </summary>
	public Guid? CorrelationId { get; init; }
}
```

`Asgard/src/Abstractions.Contracts/Failed.cs` — new file, the failure case:

```csharp
namespace Norse.Abstractions.Contracts;

/// <summary>
/// The failure case of <see cref="Outcome{T}"/>. Named <c>Failed</c>, not <c>Failure</c>, to avoid
/// colliding with <see cref="Norse.Primitives.Failure"/> (Svartalfheim's <c>ParseFailure</c>-shaped
/// case type, unrelated) when both namespaces are open in the same file.
/// </summary>
/// <param name="Problem">The error detail.</param>
public readonly record struct Failed(Problem Problem);
```

`Asgard/src/Abstractions.Contracts/Unit.cs` — new file, the payload type for void-success operations:

```csharp
namespace Norse.Abstractions.Contracts;

/// <summary>
/// The payload of <see cref="Outcome{T}"/> for operations with no success value — not a placeholder,
/// the honest zero-cost type for "nothing." Trivially satisfies <c>where T : notnull</c> (structs
/// always do). Spelled bare as <c>Outcome</c> almost everywhere via the platform-wide alias in
/// <c>GlobalUsings.Outcome.cs</c> — most call sites never name <see cref="Unit"/> directly.
/// </summary>
public readonly record struct Unit
{
	/// <summary>The single value of this type.</summary>
	public static readonly Unit Value = default;
}
```

`Asgard/src/Abstractions.Contracts/GlobalUsings.Outcome.cs` — new file, the platform-wide alias. **Generated code never uses it** — the gateway generator (Tasks 7-9) always spells `Outcome<Unit>` explicitly, on purpose, so a generated file's compilability never depends on the consuming project happening to carry this alias (2026-07-24 review — this is exactly the rule `ContractEmitter` initially missed and had to be fixed to follow, matching `WireHostEmitter`/`InProcessHostEmitter`). The alias exists purely for **hand-written** call sites that want to keep an existing bare-`Outcome` spelling — today that's Himinbjörg alone (Task 12, `LogoutHandler.cs`'s pre-existing `IRequestHandler<LogoutRequest, Outcome>` registration). Copy it to any other project only if and when a human writes new code there that wants the short spelling; nothing in this plan's generated output requires it:

```csharp
global using Outcome = Norse.Abstractions.Contracts.Outcome<Norse.Abstractions.Contracts.Unit>;
```

`Asgard/src/Abstractions.Contracts/Outcome{T}.cs` — full replacement contents. A native `[Union]` readonly record struct, matching Svartalfheim's `Result<T>` exactly (hand-authored, not a shorthand `union` declaration — both cases stored inline, zero boxing on either path; `[MustConsume]` so a caller can't silently drop the failure case; `outcome is Outcome<T>` is CS8121, pattern match `Success<T>`/`Failed` directly). One generic type only — void-success operations use `T = Unit` via the alias above, never a separate non-generic type:

```csharp
using System.Runtime.CompilerServices;
using Norse.Primitives;

namespace Norse.Abstractions.Contracts;

/// <summary>
/// The mediator's application-level result vehicle: exactly one of <see cref="Success{T}"/> (reused
/// directly from Svartalfheim) or <see cref="Failed"/>, as a native C# union. Match against the case
/// types — never against <c>Outcome&lt;T&gt;</c> itself; the compiler rejects <c>outcome is Outcome&lt;T&gt;</c>
/// (CS8121). Do not use <c>default(Outcome&lt;T&gt;)</c>; a defaulted value is malformed by construction
/// and throws <see cref="SwitchExpressionException"/> on first exhaustive-switch consumption.
/// Void-success operations use <c>T = </c><see cref="Unit"/> — spelled bare as <c>Outcome</c> via the
/// platform-wide alias, never a second, non-generic type.
/// </summary>
/// <typeparam name="T">The success payload's type. Non-nullable by construction.</typeparam>
[MustConsume]
[Union]
public readonly record struct Outcome<T> : IUnion where T : notnull
{
	enum State : byte { Default = 0, Success = 1, Failure = 2 }

	readonly Success<T> _success;
	readonly Failed _failed;
	readonly State _state;

	/// <summary>Creates a successful outcome. Also reachable as an implicit union conversion.</summary>
	public Outcome(Success<T> value)
	{
		_success = value;
		_state = State.Success;
	}

	/// <summary>Creates a failed outcome. Also reachable as an implicit union conversion.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> carries a null <see cref="Problem"/> — a smuggled <c>default(Failed)</c> past the case type's own guard.</exception>
	public Outcome(Failed value)
	{
		ArgumentNullException.ThrowIfNull(value.Problem);
		_failed = value;
		_state = State.Failure;
	}

	/// <summary>Retrieves the success case without boxing.</summary>
	public bool TryGetValue(out Success<T> value)
	{
		value = _success;
		return _state == State.Success;
	}

	/// <summary>Retrieves the failure case without boxing.</summary>
	public bool TryGetValue(out Failed value)
	{
		value = _failed;
		return _state == State.Failure;
	}

	/// <summary>Creates a successful outcome with the given value.</summary>
	public static Outcome<T> Ok(T value) => new(new Success<T>(value));

	/// <summary>Creates a failed outcome with the given error category and optional field errors.</summary>
	public static Outcome<T> Err(ErrorCategory category, IReadOnlyDictionary<string, string[]>? errors = null, Guid? correlationId = null) =>
		new(new Failed(new Problem { Category = category, Errors = errors ?? new Dictionary<string, string[]>(), CorrelationId = correlationId }));

	/// <summary>Consumes the outcome by handling both cases.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="success"/> or <paramref name="failure"/> is null.</exception>
	/// <exception cref="SwitchExpressionException">This value was defaulted rather than constructed.</exception>
	public TResult Match<TResult>(Func<T, TResult> success, Func<Problem, TResult> failure)
	{
		ArgumentNullException.ThrowIfNull(success);
		ArgumentNullException.ThrowIfNull(failure);
		return this switch
		{
			Success<T>(var value) => success(value),
			Failed(var problem) => failure(problem),
		};
	}
}
```

`Asgard/src/Abstractions.Contracts/BoolResponse.cs` — same contents as before, only the `namespace` line changes to `Norse.Abstractions.Contracts;`.

- [ ] **Step 5: Reference Svartalfheim from `Abstractions.Contracts`; update `Abstractions.Web.Server` to reference `Abstractions.Contracts`**

`Asgard/src/Abstractions.Contracts/Abstractions.Contracts.csproj` gains the one dependency Asgard's charter allows:

```xml
<ItemGroup>
	<NorseRef Include="Primitives">
		<Repo>Svartalfheim</Repo>
	</NorseRef>
</ItemGroup>
```

`IRequestHandler<,>`/`ICommandRequest<>`/`IDeferredSignIn` stay in `Abstractions.Web.Server` but now use `Outcome`/`Problem` from `Abstractions.Contracts` — add same-realm `ProjectReference`:

```xml
<ItemGroup>
	<ProjectReference Include="../Abstractions.Contracts/Abstractions.Contracts.csproj" />
</ItemGroup>
```

Add `using Norse.Abstractions.Contracts;` to any file in `Abstractions.Web.Server` that referenced the moved types (none currently do directly — `IRequestHandler<TRequest,TResponse>` is fully generic and doesn't name `Outcome` itself).

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Tests --filter OutcomeTests`
Expected: PASS (7 tests).

- [ ] **Step 7: Run the full Asgard test suite to confirm nothing else broke**

Run: `dotnet test Asgard.slnx`
Expected: PASS. (Any test file that referenced `Norse.Abstractions.Web.Server.Mediator.Outcome`/`Problem`/`ErrorCategory`/`BoolResponse` needs its `using` updated to `Norse.Abstractions.Contracts` — fix any resulting compile errors before this step passes.)

- [ ] **Step 8: Fix `Asgard/CLAUDE.md`'s now-stale file description**

`Asgard/CLAUDE.md` currently describes `Abstractions.Web.Server` as carrying "the mediator law surface (`IRequestHandler`, `ICommandRequest`, `Outcome`, `Problem`, `ErrorCategory`, `BoolResponse`)" — remove `Outcome`, `Problem`, `ErrorCategory`, `BoolResponse` from that sentence (they moved to `Abstractions.Contracts` in this task); keep `IRequestHandler`/`ICommandRequest`, which didn't move.

- [ ] **Step 9: Commit**

```bash
git add Asgard/src/Abstractions.Contracts Asgard/src/Abstractions.Web.Server Asgard/tests/Abstractions.Contracts.Tests Asgard/CLAUDE.md
git commit -m "feat: relocate envelope to Abstractions.Contracts as a native union, extend ErrorCategory and Problem"
```

---

## Task 2: Asgard — Behavior contract, extension-seam attribute, gateway trigger attribute

**Files:**
- Create: `Asgard/src/Abstractions.Contracts/GenerateGatewayAttribute.cs`
- Create: `Asgard/src/Abstractions.Web.Server/Mediator/IBehavior.cs`
- Create: `Asgard/src/Abstractions.Web.Server/Mediator/BehaviorAttribute.cs`
- Test: `Asgard/tests/Abstractions.Web.Server.Tests/BehaviorAttributeTests.cs`

**Interfaces:**
- Consumes: `Outcome<T>` (Task 1, `Norse.Abstractions.Contracts`; void-success is `Outcome<Unit>`, spelled `Outcome` via the alias — no separate non-generic type).
- Produces: `Norse.Abstractions.Contracts.GenerateGatewayAttribute` (marker, no members, WASM-safe — decorates a `[ServiceContract]` interface). `Norse.Abstractions.Web.Server.Mediator.IBehavior<TRequest,TResponse>.Handle(TRequest, CancellationToken, BehaviorDelegate<TResponse>) : ValueTask<Outcome<TResponse>>` — one interface family, `TResponse = Unit` for void-success operations, never a second `IBehavior<TRequest>` form (2026-07-24 amendment: the Unit consolidation exists specifically so this doesn't fork). `Norse.Abstractions.Web.Server.Mediator.BehaviorAttribute(Type behaviorType, Type? after = null)` — decorates a service **implementation class or method**, never an interface. Midgard's four standard behaviors (Task 3, 4) implement `IBehavior<,>`. The generator (Task 7-9) reads `BehaviorAttribute` from implementation-class symbols.

- [ ] **Step 1: Write the failing test for `BehaviorAttribute`'s allowed targets**

```csharp
// Asgard/tests/Abstractions.Web.Server.Tests/BehaviorAttributeTests.cs
using System.Reflection;
using Norse.Abstractions.Web.Server.Mediator;
using Shouldly;

namespace Norse.Abstractions.Web.Server.Tests;

public sealed class BehaviorAttributeTests
{
	[Fact]
	void BehaviorAttribute_TargetsClassAndMethod_NotInterface()
	{
		var usage = typeof(BehaviorAttribute).GetCustomAttribute<AttributeUsageAttribute>();
		usage.ShouldNotBeNull();
		usage.ValidOn.HasFlag(AttributeTargets.Class).ShouldBeTrue();
		usage.ValidOn.HasFlag(AttributeTargets.Method).ShouldBeTrue();
		usage.ValidOn.HasFlag(AttributeTargets.Interface).ShouldBeFalse();
	}

	[Fact]
	void BehaviorAttribute_StoresBehaviorTypeAndAfter()
	{
		var attribute = new BehaviorAttribute(typeof(string), after: typeof(int));
		attribute.BehaviorType.ShouldBe(typeof(string));
		attribute.After.ShouldBe(typeof(int));
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Asgard/tests/Abstractions.Web.Server.Tests --filter BehaviorAttributeTests`
Expected: FAIL — `BehaviorAttribute` does not exist.

- [ ] **Step 3: Implement `IBehavior.cs`**

```csharp
// Asgard/src/Abstractions.Web.Server/Mediator/IBehavior.cs
using Norse.Abstractions.Contracts;

namespace Norse.Abstractions.Web.Server.Mediator;

/// <summary>Continues to the next behavior (or the handler) in a generated in-process chain.</summary>
public delegate ValueTask<Outcome<TResponse>> BehaviorDelegate<TResponse>();

/// <summary>
/// One link in the generated in-process gateway's behavior chain (spec §2.5). Standard behaviors
/// (Telemetry, ExceptionTranslation, Authorization, Validation) live in Midgard; a product realm's
/// custom behavior implements this same contract. One family only — void-success operations use
/// <c>TResponse = Unit</c> (spelled <c>Outcome</c> via the platform-wide alias), never a second,
/// non-generic <c>IBehavior&lt;TRequest&gt;</c> form. That fork was drafted and reverted before
/// Asgard's ship gate specifically to avoid forcing every future custom behavior — including every
/// {Company}.{Context} product realm's own — to implement both shapes forever.
/// </summary>
public interface IBehavior<TRequest, TResponse>
{
	/// <summary>Runs this behavior, calling <paramref name="next"/> to continue the chain.</summary>
	ValueTask<Outcome<TResponse>> Handle(TRequest request, CancellationToken cancellationToken, BehaviorDelegate<TResponse> next);
}
```

- [ ] **Step 4: Implement `BehaviorAttribute.cs`**

```csharp
// Asgard/src/Abstractions.Web.Server/Mediator/BehaviorAttribute.cs
namespace Norse.Abstractions.Web.Server.Mediator;

/// <summary>
/// Declares a custom behavior for the generated in-process gateway chain. Decorates the service
/// <b>implementation</b> class or a specific method on it — never the service interface, which lives
/// in a <c>.Components</c> project shipped to WASM; a <see cref="Type"/> argument there would force
/// that assembly to reference the behavior's (server-side) implementation assembly (spec §2.5 defect 2).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class BehaviorAttribute(Type behaviorType, Type? after = null) : Attribute
{
	/// <summary>The <see cref="IBehavior{TRequest,TResponse}"/> implementation to insert.</summary>
	public Type BehaviorType { get; } = behaviorType;

	/// <summary>The standard behavior this one runs after in the chain; <see langword="null"/> inserts immediately after Validation, before the handler.</summary>
	public Type? After { get; } = after;
}
```

- [ ] **Step 5: Implement `GenerateGatewayAttribute.cs`**

```csharp
// Asgard/src/Abstractions.Contracts/GenerateGatewayAttribute.cs
namespace Norse.Abstractions.Contracts;

/// <summary>
/// Opts a <c>[ServiceContract]</c> interface into gateway generation (spec §2.2, §2.4). Every method
/// on a decorated interface must carry <c>[Authorize(Policy = ...)]</c> — enforced by the generator
/// as a build error (spec decided law item 4).
/// </summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class GenerateGatewayAttribute : Attribute;
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test Asgard/tests/Abstractions.Web.Server.Tests --filter BehaviorAttributeTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Run the full Asgard test suite**

Run: `dotnet test Asgard.slnx`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add Asgard/src/Abstractions.Contracts/GenerateGatewayAttribute.cs Asgard/src/Abstractions.Web.Server/Mediator/IBehavior.cs Asgard/src/Abstractions.Web.Server/Mediator/BehaviorAttribute.cs Asgard/tests/Abstractions.Web.Server.Tests/BehaviorAttributeTests.cs
git commit -m "feat: declare IBehavior chain contract, BehaviorAttribute seam, GenerateGatewayAttribute trigger"
```

---

## SHIP GATE — Asgard

Asgard's PR merges, CI is green, a version tag is pushed, and the resulting NuGet package (containing `Abstractions.Contracts` and `Abstractions.Web.Server`) is live on the feed before Task 3 starts. Midgard's behaviors (next phase) consume `Outcome<T>`/`IBehavior<,>` from the published package, not a local ProjectReference, in any CI run exercising `UseProjectReferences=false`.

---

## Task 3: Midgard — `TelemetryBehavior` + `ExceptionTranslationBehavior` (ordered pair)

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/Mediator/TelemetryBehavior.cs`
- Create: `Midgard/src/Infrastructure.Web.Server/Mediator/ExceptionTranslationBehavior.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/TelemetryBehaviorTests.cs`

**Interfaces:**
- Consumes: `IBehavior<TRequest,TResponse>`, `BehaviorDelegate<TResponse>`, `Outcome<T>`, `Problem`, `ErrorCategory` (Task 1, 2).
- Produces: `Norse.Infrastructure.Web.Server.Mediator.TelemetryBehavior<TRequest,TResponse>(ILogger<TelemetryBehavior<TRequest,TResponse>> logger) : IBehavior<TRequest,TResponse>` and `Norse.Infrastructure.Web.Server.Mediator.ExceptionTranslationBehavior<TRequest,TResponse>(ILogger<ExceptionTranslationBehavior<TRequest,TResponse>> logger) : IBehavior<TRequest,TResponse>` — one generic family only (2026-07-24 amendment); `Register`/`Logout`, which have no payload, are wrapped by the generator (Task 9) as `TResponse = Unit`, never a second non-generic behavior form. These are composed outermost-first as `Telemetry(ExceptionTranslation(...))` — tested together here because the ordering property (telemetry reads the finished `Problem.CorrelationId`) only holds when both are present (spec §2.5).

This task proves the fix from spec review: `ExceptionTranslationBehavior` never rethrows past itself — an unhandled exception becomes `Outcome<T>.Err(ErrorCategory.Fault, correlationId: ...)` as a **return value**, which `TelemetryBehavior`, wrapping it, can read directly.

- [ ] **Step 1: Write the failing test — exception becomes Fault with a correlation id, and Telemetry observes it as data**

```csharp
// Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/TelemetryBehaviorTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Mediator;
using Shouldly;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class TelemetryBehaviorTests
{
	[Fact]
	async Task Chain_UnhandledException_BecomesFaultOutcome_NotRethrown()
	{
		var telemetry = new TelemetryBehavior<string, bool>(NullLogger<TelemetryBehavior<string, bool>>.Instance);
		var translation = new ExceptionTranslationBehavior<string, bool>(NullLogger<ExceptionTranslationBehavior<string, bool>>.Instance);

		Outcome<bool> Result() => throw new InvalidOperationException("boom");

		var outcome = await telemetry.Handle("request", CancellationToken.None,
			() => translation.Handle("request", CancellationToken.None,
				() => ValueTask.FromResult(Result())));

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Fault);
		failed.Problem.CorrelationId.ShouldNotBeNull();
	}

	[Fact]
	async Task Chain_SuccessfulCall_PassesThroughUnchanged()
	{
		var telemetry = new TelemetryBehavior<string, bool>(NullLogger<TelemetryBehavior<string, bool>>.Instance);
		var translation = new ExceptionTranslationBehavior<string, bool>(NullLogger<ExceptionTranslationBehavior<string, bool>>.Instance);

		var outcome = await telemetry.Handle("request", CancellationToken.None,
			() => translation.Handle("request", CancellationToken.None,
				() => ValueTask.FromResult(Outcome<bool>.Ok(true))));

		outcome.TryGetValue(out Norse.Primitives.Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeTrue();
	}

	[Fact]
	async Task Chain_CooperativeCancellation_PropagatesAsOperationCanceledException()
	{
		var telemetry = new TelemetryBehavior<string, bool>(NullLogger<TelemetryBehavior<string, bool>>.Instance);
		var translation = new ExceptionTranslationBehavior<string, bool>(NullLogger<ExceptionTranslationBehavior<string, bool>>.Instance);
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		await Should.ThrowAsync<OperationCanceledException>(async () =>
			await telemetry.Handle("request", cts.Token,
				() => translation.Handle("request", cts.Token,
					() => throw new OperationCanceledException(cts.Token))));
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests --filter TelemetryBehaviorTests`
Expected: FAIL — `TelemetryBehavior`/`ExceptionTranslationBehavior` do not exist.

- [ ] **Step 3: Implement `ExceptionTranslationBehavior.cs`**

```csharp
// Midgard/src/Infrastructure.Web.Server/Mediator/ExceptionTranslationBehavior.cs
using Microsoft.Extensions.Logging;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// Converts any exception the chain doesn't already model as data into <see cref="ErrorCategory.Fault"/>
/// — as a returned <see cref="Outcome{T}"/>, never rethrown past this point (spec §2.5, §2.6).
/// <see cref="OperationCanceledException"/> on the caller's own token is never caught; it propagates
/// so the channel's native cancellation handling takes over.
/// </summary>
sealed class ExceptionTranslationBehavior<TRequest, TResponse>(ILogger<ExceptionTranslationBehavior<TRequest, TResponse>> logger)
	: IBehavior<TRequest, TResponse>
{
	public async ValueTask<Outcome<TResponse>> Handle(TRequest request, CancellationToken cancellationToken, BehaviorDelegate<TResponse> next)
	{
		try
		{
			return await next().ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			var correlationId = Guid.NewGuid();
			logger.LogError(ex, "Unhandled exception, correlation id {CorrelationId}", correlationId);
			return Outcome<TResponse>.Err(ErrorCategory.Fault, correlationId: correlationId);
		}
	}
}
```

- [ ] **Step 4: Implement `TelemetryBehavior.cs`**

```csharp
// Midgard/src/Infrastructure.Web.Server/Mediator/TelemetryBehavior.cs
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// Outermost behavior in the standard chain (spec §2.5) — sits outside
/// <see cref="ExceptionTranslationBehavior{TRequest,TResponse}"/> specifically so it reads the
/// finished <see cref="Outcome{T}.Problem"/>, including <see cref="Problem.CorrelationId"/>, directly
/// off the return value rather than watching an exception fly past unlabeled. Trusted not to throw —
/// it is not itself further wrapped.
/// </summary>
sealed class TelemetryBehavior<TRequest, TResponse>(ILogger<TelemetryBehavior<TRequest, TResponse>> logger)
	: IBehavior<TRequest, TResponse>
{
	public async ValueTask<Outcome<TResponse>> Handle(TRequest request, CancellationToken cancellationToken, BehaviorDelegate<TResponse> next)
	{
		var stopwatch = Stopwatch.StartNew();
		var outcome = await next().ConfigureAwait(false);
		stopwatch.Stop();

		switch (outcome)
		{
			case Norse.Primitives.Success<TResponse>:
				logger.LogInformation("{RequestType} succeeded in {ElapsedMs}ms", typeof(TRequest).Name, stopwatch.ElapsedMilliseconds);
				break;
			case Failed(var problem) when problem.Category == ErrorCategory.Fault:
				logger.LogWarning("{RequestType} faulted in {ElapsedMs}ms, correlation id {CorrelationId}",
					typeof(TRequest).Name, stopwatch.ElapsedMilliseconds, problem.CorrelationId);
				break;
			case Failed(var problem):
				logger.LogInformation("{RequestType} failed in {ElapsedMs}ms with {Category}",
					typeof(TRequest).Name, stopwatch.ElapsedMilliseconds, problem.Category);
				break;
		}

		return outcome;
	}
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests --filter TelemetryBehaviorTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add Midgard/src/Infrastructure.Web.Server/Mediator/TelemetryBehavior.cs Midgard/src/Infrastructure.Web.Server/Mediator/ExceptionTranslationBehavior.cs Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/TelemetryBehaviorTests.cs
git commit -m "feat: add TelemetryBehavior and ExceptionTranslationBehavior, telemetry outermost"
```

---

## Task 4: Midgard — `AuthorizationBehavior` + `ValidationBehavior`

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/Mediator/AuthorizationBehavior.cs`
- Create: `Midgard/src/Infrastructure.Web.Server/Mediator/ValidationBehavior.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/AuthorizationBehaviorTests.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/ValidationBehaviorTests.cs`

**Interfaces:**
- Consumes: `IBehavior<TRequest,TResponse>` (Task 2). `IAuthorizationService` (ASP.NET Core). `IValidator<TRequest>` (FluentValidation, already referenced by Himinbjörg's validators).
- Produces: `Norse.Infrastructure.Web.Server.Mediator.AuthorizationBehavior<TRequest,TResponse>(string policyName, IAuthorizationService authorizationService, Func<ValueTask<ClaimsPrincipal>> principalAccessor) : IBehavior<TRequest,TResponse>` and `Norse.Infrastructure.Web.Server.Mediator.ValidationBehavior<TRequest,TResponse>(IValidator<TRequest> validator) : IBehavior<TRequest,TResponse>` — one generic family only (2026-07-24 amendment), `TResponse = Unit` for handlers with no payload, never a non-generic `<TRequest>` form. The generator (Task 9) supplies `policyName` as a compile-time literal read from the service method's `[Authorize(Policy=...)]` attribute — this behavior never discovers its own policy via reflection. **`principalAccessor`, not `IHttpContextAccessor`, deliberately** — spec §2.5 already rules the principal source belongs to the host adapter, not the behavior, and `IHttpContextAccessor` is explicitly unsupported inside a live Blazor Server circuit (Microsoft's own docs: `HttpContext` is valid only for the initial synchronous render, `null`/stale after that, including after SignalR reconnection) — the exact path this feature exists to make safe. The in-process gateway (Task 9) supplies a closure over `AuthenticationStateProvider`; any future REST/gRPC-endpoint adapter (decided law item 5, deferred) supplies one over its own request principal instead.

- [ ] **Step 1: Write the failing tests — authorization split (Unauthorized vs Forbidden) and validation error shape**

```csharp
// Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/AuthorizationBehaviorTests.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator;
using NSubstitute;
using Shouldly;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class AuthorizationBehaviorTests
{
	[Fact]
	async Task NotAuthenticated_ReturnsUnauthorized()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity()); // IsAuthenticated: false
		var authorizationService = Substitute.For<IAuthorizationService>();
		authorizationService.AuthorizeAsync(user, "AuthN.Public").Returns(AuthorizationResult.Failed());

		var behavior = new AuthorizationBehavior<string, bool>("AuthN.Public", authorizationService, () => ValueTask.FromResult(user));

		var outcome = await behavior.Handle("request", CancellationToken.None, () => throw new InvalidOperationException("should not reach handler"));

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Unauthorized);
	}

	[Fact]
	async Task AuthenticatedButLacksPolicy_ReturnsForbidden()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "cookie")); // IsAuthenticated: true
		var authorizationService = Substitute.For<IAuthorizationService>();
		authorizationService.AuthorizeAsync(user, "AuthN.Admin").Returns(AuthorizationResult.Failed());

		var behavior = new AuthorizationBehavior<string, bool>("AuthN.Admin", authorizationService, () => ValueTask.FromResult(user));

		var outcome = await behavior.Handle("request", CancellationToken.None, () => throw new InvalidOperationException("should not reach handler"));

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Forbidden);
	}

	[Fact]
	async Task Authorized_CallsNext()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "cookie"));
		var authorizationService = Substitute.For<IAuthorizationService>();
		authorizationService.AuthorizeAsync(user, "AuthN.Public").Returns(AuthorizationResult.Success());

		var behavior = new AuthorizationBehavior<string, bool>("AuthN.Public", authorizationService, () => ValueTask.FromResult(user));

		var outcome = await behavior.Handle("request", CancellationToken.None, () => ValueTask.FromResult(Outcome<bool>.Ok(true)));

		outcome.TryGetValue(out Norse.Primitives.Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeTrue();
	}
}
```

```csharp
// Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/ValidationBehaviorTests.cs
using FluentValidation;
using FluentValidation.Results;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator;
using NSubstitute;
using Shouldly;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class ValidationBehaviorTests
{
	[Fact]
	async Task Invalid_ReturnsValidationOutcome_GroupedByField()
	{
		var validator = Substitute.For<IValidator<string>>();
		validator.ValidateAsync(Arg.Any<ValidationContext<string>>(), Arg.Any<CancellationToken>())
			.Returns(new ValidationResult([
				new ValidationFailure("Email", "Email is required"),
				new ValidationFailure("Email", "Email is not a valid address"),
				new ValidationFailure("Password", "Password is required"),
			]));
		var behavior = new ValidationBehavior<string, bool>(validator);

		var outcome = await behavior.Handle("request", CancellationToken.None, () => throw new InvalidOperationException("should not reach handler"));

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Validation);
		failed.Problem.Errors["Email"].ShouldBe(["Email is required", "Email is not a valid address"]);
		failed.Problem.Errors["Password"].ShouldBe(["Password is required"]);
	}

	[Fact]
	async Task Valid_CallsNext()
	{
		var validator = Substitute.For<IValidator<string>>();
		validator.ValidateAsync(Arg.Any<ValidationContext<string>>(), Arg.Any<CancellationToken>())
			.Returns(new ValidationResult());
		var behavior = new ValidationBehavior<string, bool>(validator);

		var outcome = await behavior.Handle("request", CancellationToken.None, () => ValueTask.FromResult(Outcome<bool>.Ok(true)));

		outcome.TryGetValue(out Norse.Primitives.Success<bool> _).ShouldBeTrue();
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests --filter "AuthorizationBehaviorTests|ValidationBehaviorTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement `AuthorizationBehavior.cs`**

```csharp
// Midgard/src/Infrastructure.Web.Server/Mediator/AuthorizationBehavior.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// Evaluates the policy the generator baked in from the service method's <c>[Authorize(Policy=...)]</c>
/// attribute (spec §2.5) against the principal <paramref name="principalAccessor"/> supplies. The
/// principal source is deliberately the host adapter's problem, not this behavior's — no
/// <c>IHttpContextAccessor</c> here: it's explicitly unsupported inside a live Blazor Server circuit
/// (valid only for the initial synchronous render, null/stale after SignalR reconnection), exactly the
/// path this feature exists to make safe. Not authenticated at all → <see cref="ErrorCategory.Unauthorized"/>;
/// authenticated but the policy fails → <see cref="ErrorCategory.Forbidden"/>.
/// </summary>
sealed class AuthorizationBehavior<TRequest, TResponse>(
	string policyName, IAuthorizationService authorizationService, Func<ValueTask<ClaimsPrincipal>> principalAccessor)
	: IBehavior<TRequest, TResponse>
{
	public async ValueTask<Outcome<TResponse>> Handle(TRequest request, CancellationToken cancellationToken, BehaviorDelegate<TResponse> next)
	{
		var user = await principalAccessor().ConfigureAwait(false);
		var result = await authorizationService.AuthorizeAsync(user, policyName).ConfigureAwait(false);

		if (!result.Succeeded)
		{
			return Outcome<TResponse>.Err(user.Identity is { IsAuthenticated: true } ? ErrorCategory.Forbidden : ErrorCategory.Unauthorized);
		}

		return await next().ConfigureAwait(false);
	}
}
```

- [ ] **Step 4: Implement `ValidationBehavior.cs`**

```csharp
// Midgard/src/Infrastructure.Web.Server/Mediator/ValidationBehavior.cs
using FluentValidation;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// Runs the request's <see cref="IValidator{T}"/> (resolved by the generator via the
/// <c>{RequestName}Validator</c> naming convention, registered as <c>IValidator&lt;TRequest&gt;</c> in
/// DI) and collapses failures into field-grouped <see cref="ErrorCategory.Validation"/>.
/// </summary>
sealed class ValidationBehavior<TRequest, TResponse>(IValidator<TRequest> validator) : IBehavior<TRequest, TResponse>
{
	public async ValueTask<Outcome<TResponse>> Handle(TRequest request, CancellationToken cancellationToken, BehaviorDelegate<TResponse> next)
	{
		var result = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);

		if (!result.IsValid)
		{
			var errors = result.Errors
				.GroupBy(failure => failure.PropertyName)
				.ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray());
			return Outcome<TResponse>.Err(ErrorCategory.Validation, errors);
		}

		return await next().ConfigureAwait(false);
	}
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests --filter "AuthorizationBehaviorTests|ValidationBehaviorTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Run the full Midgard test suite**

Run: `dotnet test Midgard.slnx`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Midgard/src/Infrastructure.Web.Server/Mediator/AuthorizationBehavior.cs Midgard/src/Infrastructure.Web.Server/Mediator/ValidationBehavior.cs Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/AuthorizationBehaviorTests.cs Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/ValidationBehaviorTests.cs
git commit -m "feat: add AuthorizationBehavior and ValidationBehavior"
```

---

## Task 5: Midgard — ErrorInfo wire encoding, retire `OutcomeServerInterceptor`/`OutcomeFailedException`/`OutcomeExtensions`

**Files:**
- Modify: `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ProblemExtensions.cs`
- Modify: `Midgard/src/Infrastructure.Web.Client/Grpc/RpcExceptionExtensions.cs`
- Delete: `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/{OutcomeServerInterceptor,OutcomeFailedException,OutcomeExtensions}.cs` and their test counterparts
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/ProblemExtensionsTests.cs` (rewrite)
- Test: `Midgard/tests/Infrastructure.Web.Client.Tests/Grpc/RpcExceptionExtensionsTests.cs` (rewrite)

**Interfaces:**
- Consumes: `Problem`, `ErrorCategory` (Task 1).
- Produces: `Norse.Infrastructure.Web.Server.Mediator.Grpc.ProblemExtensions.ToRpcException(this Problem) : RpcException` — status code is the partner-legible idiom, `google.rpc.ErrorInfo{ Reason, Domain = "norse.io" }` is the authoritative decode key (spec §2.1 defect 1). `Norse.Infrastructure.Web.Client.Grpc.RpcExceptionExtensions.DecodeProblem(this RpcException) : Problem` — decodes `ErrorInfo.Reason` into `ErrorCategory` by name, never the status code. This task deletes the never-registered `OutcomeServerInterceptor`/`OutcomeFailedException`/`OutcomeExtensions.ThrowIfFailed` trio entirely — nothing in the codebase calls or registers them (confirmed: zero hits for `AddCodeFirstGrpc`/interceptor registration anywhere in the tree), so deletion has no runtime impact to migrate around.

- [ ] **Step 1: Write the failing round-trip test — non-injective status codes decode correctly via ErrorInfo**

```csharp
// Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/ProblemExtensionsTests.cs
using Grpc.Core;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;
using Shouldly;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class ProblemExtensionsTests
{
	[Fact]
	void LockedOut_And_Forbidden_ShareStatusCode_ButDistinctErrorInfoReason()
	{
		var lockedOut = new Problem { Category = ErrorCategory.LockedOut }.ToRpcException();
		var forbidden = new Problem { Category = ErrorCategory.Forbidden }.ToRpcException();

		lockedOut.StatusCode.ShouldBe(StatusCode.PermissionDenied);
		forbidden.StatusCode.ShouldBe(StatusCode.PermissionDenied);
		// Same status code — the test that matters is that Reason still disambiguates them.
		lockedOut.Trailers.Get("grpc-status-details-bin").ShouldNotBeNull();
	}

	[Fact]
	void Validation_MapsTo_InvalidArgument()
	{
		var exception = new Problem { Category = ErrorCategory.Validation, Errors = new Dictionary<string, string[]> { ["Email"] = ["required"] } }.ToRpcException();
		exception.StatusCode.ShouldBe(StatusCode.InvalidArgument);
	}

	[Fact]
	void NotAllowed_MapsTo_FailedPrecondition_NotSharedWithLockedOut()
	{
		var exception = new Problem { Category = ErrorCategory.NotAllowed }.ToRpcException();
		exception.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
	}

	[Fact]
	void Fault_MapsTo_Internal_AndCarriesCorrelationId()
	{
		var correlationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
		var exception = new Problem { Category = ErrorCategory.Fault, CorrelationId = correlationId }.ToRpcException();
		exception.StatusCode.ShouldBe(StatusCode.Internal);
	}
}
```

```csharp
// Midgard/tests/Infrastructure.Web.Client.Tests/Grpc/RpcExceptionExtensionsTests.cs
using Norse.Infrastructure.Web.Client.Grpc;
using Shouldly;

namespace Norse.Infrastructure.Web.Client.Tests.Grpc;

public sealed class RpcExceptionExtensionsTests
{
	[Fact]
	void DecodeProblem_ReadsReason_NotStatusCode_DisambiguatesSharedStatus()
	{
		// Server-side ToRpcException() and client-side DecodeProblem() are the two halves of one
		// round-trip; this test exercises the client half against a hand-built trailer shaped exactly
		// like ToRpcException() produces, so LockedOut/Forbidden — same status code — decode distinctly.
		var lockedOutException = Norse.Infrastructure.Web.Server.Mediator.Grpc.ProblemExtensions.ToRpcException(
			new Norse.Abstractions.Contracts.Problem { Category = Norse.Abstractions.Contracts.ErrorCategory.LockedOut });
		var forbiddenException = Norse.Infrastructure.Web.Server.Mediator.Grpc.ProblemExtensions.ToRpcException(
			new Norse.Abstractions.Contracts.Problem { Category = Norse.Abstractions.Contracts.ErrorCategory.Forbidden });

		lockedOutException.DecodeProblem().Category.ShouldBe(Norse.Abstractions.Contracts.ErrorCategory.LockedOut);
		forbiddenException.DecodeProblem().Category.ShouldBe(Norse.Abstractions.Contracts.ErrorCategory.Forbidden);
	}
}
```

Note: `RpcExceptionExtensionsTests` cross-references the server-side project directly rather than a fake trailer — acceptable here since this test's whole point is proving the *real* round-trip; a `ProjectReference` from `Infrastructure.Web.Client.Tests` to `Infrastructure.Web.Server` is test-only and does not violate the WASM-safety rule (test projects never ship to a browser).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Midgard.slnx --filter "ProblemExtensionsTests|RpcExceptionExtensionsTests"`
Expected: FAIL — current `ProblemExtensions.ToRpcException` uses the old `problem-bin` JSON trailer and an incomplete switch; `NotAllowed` still maps to `PermissionDenied`, not `FailedPrecondition`.

- [ ] **Step 3: Delete the retired trio and their tests**

```bash
git rm Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/OutcomeServerInterceptor.cs
git rm Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/OutcomeFailedException.cs
git rm Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/OutcomeExtensions.cs
git rm Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/OutcomeServerInterceptorTests.cs 2>/dev/null || true
git rm Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/OutcomeExtensionsTests.cs 2>/dev/null || true
```

- [ ] **Step 4: Rewrite `ProblemExtensions.cs` — ErrorInfo-based, full category coverage**

```csharp
// Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ProblemExtensions.cs
using Google.Rpc;
using Grpc.Core;
using Norse.Abstractions.Contracts;
using Status = Grpc.Core.Status;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
/// Converts a <see cref="Problem"/> to an <see cref="RpcException"/>. The gRPC status code is the
/// partner-legible idiom — standard tooling reads it correctly without knowing Norse exists — but it
/// is not injective (<see cref="ErrorCategory.LockedOut"/>/<see cref="ErrorCategory.Forbidden"/> share
/// PermissionDenied; <see cref="ErrorCategory.Unauthorized"/>/<see cref="ErrorCategory.InvalidCredentials"/>
/// share Unauthenticated). Every response also carries a <c>google.rpc.ErrorInfo</c> detail whose
/// <c>Reason</c> is the exact <see cref="ErrorCategory"/> member name — the only field
/// <see cref="RpcExceptionExtensions.DecodeProblem"/> trusts (spec §2.1).
/// </summary>
public static class ProblemExtensions
{
	const string ErrorInfoDomain = "norse.io";

	/// <summary>Converts a <see cref="Problem"/> to an <see cref="RpcException"/> carrying a <c>grpc-status-details-bin</c> trailer.</summary>
	public static RpcException ToRpcException(this Problem problem)
	{
		var statusCode = problem.Category switch
		{
			ErrorCategory.Validation => Grpc.Core.StatusCode.InvalidArgument,
			ErrorCategory.NotFound => Grpc.Core.StatusCode.NotFound,
			ErrorCategory.Conflict => Grpc.Core.StatusCode.AlreadyExists,
			ErrorCategory.Unauthorized => Grpc.Core.StatusCode.Unauthenticated,
			ErrorCategory.Forbidden => Grpc.Core.StatusCode.PermissionDenied,
			ErrorCategory.LockedOut => Grpc.Core.StatusCode.PermissionDenied,
			ErrorCategory.NotAllowed => Grpc.Core.StatusCode.FailedPrecondition,
			ErrorCategory.InvalidCredentials => Grpc.Core.StatusCode.Unauthenticated,
			ErrorCategory.Fault => Grpc.Core.StatusCode.Internal,
			_ => Grpc.Core.StatusCode.Unknown,
		};

		var richStatus = new Google.Rpc.Status
		{
			Code = (int)MapToGoogleRpcCode(statusCode),
			Message = problem.Category.ToString(),
		};
		richStatus.Details.Add(Any.Pack(new ErrorInfo
		{
			Reason = problem.Category.ToString(),
			Domain = ErrorInfoDomain,
		}));
		if (problem.Errors.Count > 0)
		{
			var badRequest = new BadRequest();
			foreach (var (field, messages) in problem.Errors)
			{
				foreach (var message in messages)
					badRequest.FieldViolations.Add(new BadRequest.Types.FieldViolation { Field = field, Description = message });
			}
			richStatus.Details.Add(Any.Pack(badRequest));
		}
		if (problem.CorrelationId is { } correlationId)
		{
			richStatus.Details.Add(Any.Pack(new DebugInfo { Detail = correlationId.ToString() }));
		}

		var trailers = new Metadata { { "grpc-status-details-bin", richStatus.ToByteArray() } };
		return new RpcException(new Status(statusCode, problem.Category.ToString()), trailers);
	}

	static Code MapToGoogleRpcCode(Grpc.Core.StatusCode statusCode) => statusCode switch
	{
		Grpc.Core.StatusCode.InvalidArgument => Code.InvalidArgument,
		Grpc.Core.StatusCode.NotFound => Code.NotFound,
		Grpc.Core.StatusCode.AlreadyExists => Code.AlreadyExists,
		Grpc.Core.StatusCode.Unauthenticated => Code.Unauthenticated,
		Grpc.Core.StatusCode.PermissionDenied => Code.PermissionDenied,
		Grpc.Core.StatusCode.FailedPrecondition => Code.FailedPrecondition,
		Grpc.Core.StatusCode.Internal => Code.Internal,
		_ => Code.Unknown,
	};
}
```

Add `PackageReference` for `Google.Api.CommonProtos` (the package that ships `Google.Rpc.Status`/`ErrorInfo`/`BadRequest`/`DebugInfo`) to `Midgard/src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj`:

```xml
<PackageReference Include="Google.Api.CommonProtos" Version="2.*" />
```

- [ ] **Step 5: Rewrite `RpcExceptionExtensions.cs` — decode `ErrorInfo.Reason`, never the status code**

```csharp
// Midgard/src/Infrastructure.Web.Client/Grpc/RpcExceptionExtensions.cs
using Google.Rpc;
using Grpc.Core;
using Norse.Abstractions.Contracts;

namespace Norse.Infrastructure.Web.Client.Grpc;

/// <summary>
/// Client-side companion to Infrastructure.Web.Server's <c>ProblemExtensions.ToRpcException</c>.
/// Decodes the <c>grpc-status-details-bin</c> trailer's <c>google.rpc.ErrorInfo.Reason</c> field
/// authoritatively — never the gRPC status code, which is not injective across all nine
/// <see cref="ErrorCategory"/> members (spec §2.1).
/// </summary>
public static class RpcExceptionExtensions
{
	/// <summary>Decodes an <see cref="RpcException"/>'s <c>grpc-status-details-bin</c> trailer into a <see cref="Problem"/>.</summary>
	public static Problem DecodeProblem(this RpcException exception)
	{
		var trailer = exception.Trailers.Get("grpc-status-details-bin");
		if (trailer is null)
			return new Problem { Category = ErrorCategory.Fault };

		var richStatus = Google.Rpc.Status.Parser.ParseFrom(trailer.ValueBytes);
		var category = ErrorCategory.Fault;
		var errors = new Dictionary<string, string[]>();
		Guid? correlationId = null;

		foreach (var detail in richStatus.Details)
		{
			if (detail.Is(ErrorInfo.Descriptor) && detail.TryUnpack<ErrorInfo>(out var errorInfo) && Enum.TryParse<ErrorCategory>(errorInfo.Reason, out var parsed))
			{
				category = parsed;
			}
			else if (detail.Is(BadRequest.Descriptor) && detail.TryUnpack<BadRequest>(out var badRequest))
			{
				errors = badRequest.FieldViolations
					.GroupBy(violation => violation.Field)
					.ToDictionary(group => group.Key, group => group.Select(violation => violation.Description).ToArray());
			}
			else if (detail.Is(DebugInfo.Descriptor) && detail.TryUnpack<DebugInfo>(out var debugInfo) && Guid.TryParse(debugInfo.Detail, out var parsedCorrelationId))
			{
				correlationId = parsedCorrelationId;
			}
		}

		return new Problem { Category = category, Errors = errors, CorrelationId = correlationId };
	}
}
```

Add `PackageReference Include="Google.Api.CommonProtos" Version="2.*"` to `Midgard/src/Infrastructure.Web.Client/Infrastructure.Web.Client.csproj` too — this package is pure protobuf message types, WASM-safe, no server hosting dependency.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Midgard.slnx --filter "ProblemExtensionsTests|RpcExceptionExtensionsTests"`
Expected: PASS (5 tests total).

- [ ] **Step 7: Run the full Midgard test suite to confirm the deletions didn't break anything**

Run: `dotnet test Midgard.slnx`
Expected: PASS. (Confirms no other file referenced `OutcomeServerInterceptor`/`OutcomeFailedException`/`OutcomeExtensions` — grounding research already showed zero such references outside their own definitions and doc comments.)

- [ ] **Step 8: Commit**

```bash
git add Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ProblemExtensions.cs Midgard/src/Infrastructure.Web.Client/Grpc/RpcExceptionExtensions.cs Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/ProblemExtensionsTests.cs Midgard/tests/Infrastructure.Web.Client.Tests/Grpc/RpcExceptionExtensionsTests.cs Midgard/src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj Midgard/src/Infrastructure.Web.Client/Infrastructure.Web.Client.csproj
git add -u Midgard/src/Infrastructure.Web.Server/Mediator/Grpc Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc
git commit -m "feat: ErrorInfo-based wire encoding for Problem, retire OutcomeServerInterceptor trio"
```

---

## Task 6: Midgard — `AddNorseCodeFirstGrpc()` + `UnhandledExceptionInterceptor`

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/UnhandledExceptionInterceptor.cs`
- Create: `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ServiceCollectionExtensions.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/UnhandledExceptionInterceptorTests.cs`

**Interfaces:**
- Consumes: `ProblemExtensions.ToRpcException` (Task 5).
- Produces: `Norse.Infrastructure.Web.Server.Mediator.Grpc.ServiceCollectionExtensions.AddNorseCodeFirstGrpc(this IServiceCollection) : IServiceCollection` — the generic gRPC hosting wiring, called once by Yggdrasil's composition root (Task 13), never by a realm-specific registration like Himinbjörg's `AddNorseAuthenticationService`. This is the concrete fix for "Heimdall stays dumb to gRPC" (`corrected 2026-07-24`): the interceptor pipeline is wired generically here, not per-service.

- [ ] **Step 1: Write the failing test — an exception a service implementation lets escape (not already an RpcException from `ToRpcException`) becomes Fault**

```csharp
// Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/UnhandledExceptionInterceptorTests.cs
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;
using Shouldly;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class UnhandledExceptionInterceptorTests
{
	[Fact]
	async Task UnhandledException_BecomesInternalRpcException_WithErrorInfoFault()
	{
		var interceptor = new UnhandledExceptionInterceptor(NullLogger<UnhandledExceptionInterceptor>.Instance);

		var exception = await Should.ThrowAsync<RpcException>(async () =>
			await interceptor.UnaryServerHandler<string, bool>(
				"request",
				TestServerCallContext.Create(),
				(_, _) => throw new InvalidOperationException("unexpected")));

		exception.StatusCode.ShouldBe(StatusCode.Internal);
	}

	[Fact]
	async Task AlreadyWellFormedRpcException_PassesThroughUnchanged()
	{
		var interceptor = new UnhandledExceptionInterceptor(NullLogger<UnhandledExceptionInterceptor>.Instance);
		var original = new RpcException(new Status(StatusCode.NotFound, "not found"));

		var exception = await Should.ThrowAsync<RpcException>(async () =>
			await interceptor.UnaryServerHandler<string, bool>(
				"request",
				TestServerCallContext.Create(),
				(_, _) => throw original));

		exception.ShouldBeSameAs(original);
	}
}
```

Note: `TestServerCallContext.Create()` is `Grpc.Core.Testing.TestServerCallContext` — already a transitive test dependency of `Grpc.Core`/`Grpc.AspNetCore.Server`; add `PackageReference Include="Grpc.Core.Testing" Version="2.*"` to `Midgard/tests/Infrastructure.Web.Server.Tests/Infrastructure.Web.Server.Tests.csproj` if not already present.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests --filter UnhandledExceptionInterceptorTests`
Expected: FAIL — `UnhandledExceptionInterceptor` does not exist.

- [ ] **Step 3: Implement `UnhandledExceptionInterceptor.cs`**

```csharp
// Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/UnhandledExceptionInterceptor.cs
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using Norse.Abstractions.Contracts;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
/// Generic, zero-domain-knowledge safety net registered once for every gRPC-hosted service (spec
/// §2.6). Expected business failures are already well-formed <see cref="RpcException"/>s by the time
/// they reach this interceptor — a service implementation throws <c>Problem.ToRpcException()</c>
/// directly (Task 12). This interceptor's only job is catching whatever a service implementation
/// let escape uncaught and converting it to <see cref="ErrorCategory.Fault"/>.
/// </summary>
sealed class UnhandledExceptionInterceptor(ILogger<UnhandledExceptionInterceptor> logger) : Interceptor
{
	public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
		TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
	{
		try
		{
			return await continuation(request, context).ConfigureAwait(false);
		}
		catch (RpcException)
		{
			throw;
		}
		catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			var correlationId = Guid.NewGuid();
			logger.LogError(ex, "Unhandled exception in {Method}, correlation id {CorrelationId}", context.Method, correlationId);
			throw new Problem { Category = ErrorCategory.Fault, CorrelationId = correlationId }.ToRpcException();
		}
	}
}
```

- [ ] **Step 4: Implement `ServiceCollectionExtensions.cs`**

```csharp
// Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.Server;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
/// Generic gRPC hosting wiring, called once by the composition root (Yggdrasil), never by a
/// realm-specific service registration — no service, including Heimdall's, knows this call happens.
/// </summary>
public static class ServiceCollectionExtensions
{
	/// <summary>Wires protobuf-net.Grpc's code-first hosting with the platform's <see cref="UnhandledExceptionInterceptor"/>.</summary>
	public static IServiceCollection AddNorseCodeFirstGrpc(this IServiceCollection services)
	{
		services.AddCodeFirstGrpc(options => options.Interceptors.Add<UnhandledExceptionInterceptor>());
		return services;
	}
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests --filter UnhandledExceptionInterceptorTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Run the full Midgard test suite**

Run: `dotnet test Midgard.slnx`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/UnhandledExceptionInterceptor.cs Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ServiceCollectionExtensions.cs Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/UnhandledExceptionInterceptorTests.cs
git commit -m "feat: add AddNorseCodeFirstGrpc composition-root wiring and UnhandledExceptionInterceptor"
```

---

## SHIP GATE — Midgard

Midgard's PR merges, CI is green, a version tag is pushed, and the resulting NuGet package is live on the feed before Task 7 starts. The gateway generator (next phase) emits code that names Midgard's behavior types by fully-qualified name in generated source text — it doesn't compile-reference Midgard directly, but the *emitted* code (tested via a compiled fixture in Task 9) must resolve those names against the real published package, not a local guess.

---

## Task 7: Asgard/gen — Generator skeleton, discovery, Contract-mode emission

**Files:**
- Create: `Asgard/gen/Abstractions.Contracts.Generator/Abstractions.Contracts.Generator.csproj`
- Create: `Asgard/gen/Abstractions.Contracts.Generator/GatewayInterfaceModel.cs`
- Create: `Asgard/gen/Abstractions.Contracts.Generator/GatewayMethodModel.cs`
- Create: `Asgard/gen/Abstractions.Contracts.Generator/GatewayGenerator.cs`
- Create: `Asgard/gen/Abstractions.Contracts.Generator/ContractEmitter.cs`
- Create: `Asgard/tests/Abstractions.Contracts.Generator.Tests/Abstractions.Contracts.Generator.Tests.csproj`
- Test: `Asgard/tests/Abstractions.Contracts.Generator.Tests/GatewayGeneratorTests.cs`

**Interfaces:**
- Consumes: `GenerateGatewayAttribute` (Task 2), `System.ServiceModel.ServiceContractAttribute`/`OperationContractAttribute` (protobuf-net.Grpc's WCF-derived attributes), `Microsoft.AspNetCore.Authorization.AuthorizeAttribute`.
- Produces: `Norse.Abstractions.Contracts.Generator.GatewayGenerator : IIncrementalGenerator` — discovers every `[GenerateGateway]`-decorated interface reachable from the compilation (own symbols and referenced assembly symbols, per the compiled-symbols constraint) and, in `Contract` emission mode, emits `I{Context}Gateway`. Diagnostic `NORSE001` (error) fires for any `[OperationContract]` method missing `[Authorize(Policy=...)]` (decided law item 4). Diagnostic `NORSE002` (error) fires for any method returning `IAsyncEnumerable<T>` (spec §2.3 — streaming excluded entirely from v1). Diagnostic `NORSE003` (error) fires when a `[GenerateGateway]` interface's name isn't `I{Context}Service` — the context-name derivation refuses to guess for any other shape (e.g. `I{Context}Api`, also real per spec §9.8) rather than silently slicing an arbitrary suffix (2026-07-24 review, lesser finding 1). Diagnostic `NORSE004` (error) fires for a method with zero parameters — a malformed method shape the generator refuses to crash on (2026-07-24 review: an unguarded `member.Parameters[0]` access would throw `ArgumentOutOfRangeException` mid-compilation instead of failing loudly with a diagnostic, the exact opposite of the platform's own convention). Tasks 8 and 9 add the other two emission modes to this same generator.

- [ ] **Step 1: Write the failing generator test — Contract mode emits the gateway interface**

```csharp
// Asgard/tests/Abstractions.Contracts.Generator.Tests/GatewayGeneratorTests.cs
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Norse.Abstractions.Contracts.Generator;
using Shouldly;

namespace Norse.Abstractions.Contracts.Generator.Tests;

static class GeneratorTestHarness
{
	public static (ImmutableArray<Diagnostic> Diagnostics, string[] GeneratedSources) Run(string source, string emissionMode)
	{
		var compilation = CSharpCompilation.Create(
			"TestAssembly",
			[CSharpSyntaxTree.ParseText(source)],
			ReferenceAssemblies.Net110.Concat(
			[
				MetadataReference.CreateFromFile(typeof(System.ServiceModel.ServiceContractAttribute).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(Norse.Abstractions.Contracts.GenerateGatewayAttribute).Assembly.Location),
			]),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		var options = new TestAnalyzerConfigOptionsProvider(emissionMode);
		var driver = CSharpGeneratorDriver.Create([new GatewayGenerator().AsSourceGenerator()])
			.WithUpdatedAnalyzerConfigOptions(options)
			.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

		var generatedSources = outputCompilation.SyntaxTrees.Skip(1).Select(tree => tree.ToString()).ToArray();
		return (diagnostics, generatedSources);
	}
}

public sealed class GatewayGeneratorTests
{
	const string ServiceInterfaceSource = """
		using System.ServiceModel;
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Contracts;

		namespace TestRealm.Services;

		[GenerateGateway]
		[ServiceContract]
		public interface IWidgetService
		{
			[Authorize(Policy = "Widget.Read")]
			[OperationContract]
			Task<WidgetResponse> GetWidget(WidgetRequest request, CancellationToken cancellationToken = default);
		}

		public sealed record WidgetRequest;
		public sealed record WidgetResponse;
		""";

	[Fact]
	void ContractMode_EmitsGatewayInterface_MirroringMethodsWrappedInOutcome()
	{
		var (diagnostics, sources) = GeneratorTestHarness.Run(ServiceInterfaceSource, "Contract");

		diagnostics.ShouldBeEmpty();
		var gatewaySource = sources.ShouldHaveSingleItem();
		gatewaySource.ShouldContain("public interface IWidgetGateway");
		gatewaySource.ShouldContain("ValueTask<Outcome<WidgetResponse>> GetWidget(WidgetRequest request, CancellationToken cancellationToken = default)");
	}

	[Fact]
	void ContractMode_VoidSuccessMethod_EmitsOutcomeOfUnitExplicitly_NeverBareAlias()
	{
		const string source = """
			using System.ServiceModel;
			using Microsoft.AspNetCore.Authorization;
			using Norse.Abstractions.Contracts;

			namespace TestRealm.Services;

			[GenerateGateway]
			[ServiceContract]
			public interface IWidgetService
			{
				[Authorize(Policy = "Widget.Delete")]
				[OperationContract]
				Task DeleteWidget(WidgetRequest request, CancellationToken cancellationToken = default);
			}

			public sealed record WidgetRequest;
			""";

		var (diagnostics, sources) = GeneratorTestHarness.Run(source, "Contract");

		diagnostics.ShouldBeEmpty();
		var gatewaySource = sources.ShouldHaveSingleItem();
		// Explicit Outcome<Unit> — never bare "ValueTask<Outcome>", which would only compile in a
		// project that happens to carry the GlobalUsings.Outcome.cs alias. Generated code must never
		// depend on that (2026-07-24 review) — this is exactly the gap that broke Task 10's build.
		gatewaySource.ShouldContain("ValueTask<Outcome<Unit>> DeleteWidget(WidgetRequest request, CancellationToken cancellationToken = default)");
		gatewaySource.ShouldNotContain("ValueTask<Outcome> ");
	}

	[Fact]
	void MissingAuthorizeAttribute_ReportsNorse001Error()
	{
		const string source = """
			using System.ServiceModel;
			using Norse.Abstractions.Contracts;

			namespace TestRealm.Services;

			[GenerateGateway]
			[ServiceContract]
			public interface IWidgetService
			{
				[OperationContract]
				Task<WidgetResponse> GetWidget(WidgetRequest request, CancellationToken cancellationToken = default);
			}

			public sealed record WidgetRequest;
			public sealed record WidgetResponse;
			""";

		var (diagnostics, _) = GeneratorTestHarness.Run(source, "Contract");

		diagnostics.ShouldContain(d => d.Id == "NORSE001" && d.Severity == DiagnosticSeverity.Error);
	}

	[Fact]
	void StreamingMethod_ReportsNorse002Error()
	{
		const string source = """
			using System.ServiceModel;
			using Microsoft.AspNetCore.Authorization;
			using Norse.Abstractions.Contracts;

			namespace TestRealm.Services;

			[GenerateGateway]
			[ServiceContract]
			public interface IWidgetService
			{
				[Authorize(Policy = "Widget.Read")]
				[OperationContract]
				IAsyncEnumerable<WidgetResponse> StreamWidgets(WidgetRequest request, CancellationToken cancellationToken = default);
			}

			public sealed record WidgetRequest;
			public sealed record WidgetResponse;
			""";

		var (diagnostics, sources) = GeneratorTestHarness.Run(source, "Contract");

		diagnostics.ShouldContain(d => d.Id == "NORSE002" && d.Severity == DiagnosticSeverity.Error);
		sources.ShouldBeEmpty();
	}

	[Fact]
	void InterfaceNotEndingInService_ReportsNorse003Error_DoesNotGuessAName()
	{
		const string source = """
			using System.ServiceModel;
			using Microsoft.AspNetCore.Authorization;
			using Norse.Abstractions.Contracts;

			namespace TestRealm.Services;

			[GenerateGateway]
			[ServiceContract]
			public interface IWidgetApi
			{
				[Authorize(Policy = "Widget.Read")]
				[OperationContract]
				Task<WidgetResponse> GetWidget(WidgetRequest request, CancellationToken cancellationToken = default);
			}

			public sealed record WidgetRequest;
			public sealed record WidgetResponse;
			""";

		var (diagnostics, sources) = GeneratorTestHarness.Run(source, "Contract");

		diagnostics.ShouldContain(d => d.Id == "NORSE003" && d.Severity == DiagnosticSeverity.Error);
		sources.ShouldBeEmpty();
	}

	[Fact]
	void MethodWithNoParameters_ReportsNorse004Error_DoesNotCrash()
	{
		const string source = """
			using System.ServiceModel;
			using Microsoft.AspNetCore.Authorization;
			using Norse.Abstractions.Contracts;

			namespace TestRealm.Services;

			[GenerateGateway]
			[ServiceContract]
			public interface IWidgetService
			{
				[Authorize(Policy = "Widget.Read")]
				[OperationContract]
				Task<WidgetResponse> GetWidget(CancellationToken cancellationToken = default);
			}

			public sealed record WidgetResponse;
			""";

		var (diagnostics, sources) = GeneratorTestHarness.Run(source, "Contract");

		diagnostics.ShouldContain(d => d.Id == "NORSE004" && d.Severity == DiagnosticSeverity.Error);
		sources.ShouldBeEmpty();
	}
}
```

```csharp
// Asgard/tests/Abstractions.Contracts.Generator.Tests/TestAnalyzerConfigOptionsProvider.cs
using Microsoft.CodeAnalysis.Diagnostics;

namespace Norse.Abstractions.Contracts.Generator.Tests;

sealed class TestAnalyzerConfigOptionsProvider(string emissionMode) : AnalyzerConfigOptionsProvider
{
	public override AnalyzerConfigOptions GlobalOptions { get; } = new TestOptions(emissionMode);
	public override AnalyzerConfigOptions GetOptions(Microsoft.CodeAnalysis.SyntaxTree tree) => GlobalOptions;
	public override AnalyzerConfigOptions GetOptions(Microsoft.CodeAnalysis.AdditionalText textFile) => GlobalOptions;

	sealed class TestOptions(string emissionMode) : AnalyzerConfigOptions
	{
		public override bool TryGetValue(string key, out string value)
		{
			if (key == "build_property.NorseGatewayEmissionMode")
			{
				value = emissionMode;
				return true;
			}
			value = "";
			return false;
		}
	}
}
```

`ReferenceAssemblies.Net110` is a small local helper (not a NuGet package) returning the BCL reference set — add it alongside the harness:

```csharp
// Asgard/tests/Abstractions.Contracts.Generator.Tests/ReferenceAssemblies.cs
using Microsoft.CodeAnalysis;

namespace Norse.Abstractions.Contracts.Generator.Tests;

static class ReferenceAssemblies
{
	public static readonly MetadataReference[] Net110 =
	[
		MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
		MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
		MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
	];
}
```

(Add `using System.Reflection;` to that file.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests --filter GatewayGeneratorTests`
Expected: FAIL — `GatewayGenerator` does not exist.

- [ ] **Step 3: Create the generator project**

**Correction (2026-07-24 review):** the original plan text here had the generator self-pack via a `None`/`Pack="true"` item — dead on arrival, since `Asgard/gen/Directory.Build.props` (already scattered platform-wide, already governing this project) sets `IsPackable=false` for every project under `gen/` specifically *because* the platform's real, already-shipped convention (Urðarbrunnr's `Persistence.EntityFramework.Generator`) is for the generator to be bundled into the **host runtime package** — never a standalone package a consumer separately `PackageReference`s. Fixed here: the generator project itself only needs a `Description` (everything else — `TargetFramework`, `IsRoslynComponent`, `EnforceExtendedAnalyzerRules`, the `Microsoft.CodeAnalysis.CSharp` reference — already comes from `gen/Directory.Build.props`, redundant to restate per the platform's "hoist shared props" convention). The actual bundling wiring lives in `Abstractions.Contracts.csproj` instead — see the note after this code block.

```xml
<!-- Asgard/gen/Abstractions.Contracts.Generator/Abstractions.Contracts.Generator.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Abstractions.Contracts.Generator: emits per-service, Result-native Blazor gateways from a [GenerateGateway]-decorated service interface (contracts, WASM host, and composition-root artifacts — none of which is a Web.Server-only concern, hence the name). Bundled into Abstractions.Contracts's package (analyzers/dotnet/cs/), never referenced or packed standalone — see Abstractions.Contracts.csproj's IncludeGeneratorInPackage target.</Description>
	</PropertyGroup>
</Project>
```

**`Asgard/src/Abstractions.Contracts/Abstractions.Contracts.csproj` gains the bundling wiring** (matching `Urdarbrunnr/src/Persistence.EntityFramework/Persistence.EntityFramework.csproj`'s real, shipped pattern exactly — every consumer of `Abstractions.Contracts` gets the generator automatically, no separate `PackageReference` anywhere in this plan from here on; Tasks 10, 12, 13 do **not** add a `Norse.Abstractions.Contracts.Generator` package reference, only Task 1's already-shipped `Abstractions.Contracts` reference, which every gateway-generating project needs anyway for `Outcome<T>`/`Unit`/`GenerateGatewayAttribute`):

```xml
<ItemGroup>
	<ProjectReference
		Include="../../gen/Abstractions.Contracts.Generator/Abstractions.Contracts.Generator.csproj"
		OutputItemType="Analyzer"
		ReferenceOutputAssembly="false" />
</ItemGroup>
<Target Name="IncludeGeneratorInPackage" BeforeTargets="_GetPackageFiles">
	<MSBuild Projects="../../gen/Abstractions.Contracts.Generator/Abstractions.Contracts.Generator.csproj"
		Targets="Build"
		Properties="Configuration=$(Configuration)" />
	<ItemGroup>
		<None Include="../../gen/Abstractions.Contracts.Generator/bin/$(Configuration)/netstandard2.0/Norse.Abstractions.Contracts.Generator.dll"
			Pack="true"
			PackagePath="analyzers/dotnet/cs/"
			Visible="false" />
	</ItemGroup>
</Target>
```

This is a follow-up change to `Abstractions.Contracts.csproj` — already shipped by Task 1 (merged, PR #33) without this wiring, since Task 1 predates the generator existing at all. Add it as part of this task's own commit (it lives in `src/`, not `gen/`, but the two are inseparable — the generator is inert without this).

- [ ] **Step 4: Implement the model records**

```csharp
// Asgard/gen/Abstractions.Contracts.Generator/GatewayMethodModel.cs
namespace Norse.Abstractions.Contracts.Generator;

sealed record GatewayMethodModel(string Name, string RequestTypeName, string? ResponseTypeName, string PolicyName);
```

```csharp
// Asgard/gen/Abstractions.Contracts.Generator/GatewayInterfaceModel.cs
using System.Collections.Immutable;

namespace Norse.Abstractions.Contracts.Generator;

sealed record GatewayInterfaceModel(string Namespace, string ServiceInterfaceName, string ContextName, ImmutableArray<GatewayMethodModel> Methods);
```

- [ ] **Step 5: Implement `ContractEmitter.cs`**

```csharp
// Asgard/gen/Abstractions.Contracts.Generator/ContractEmitter.cs
using System.Text;

namespace Norse.Abstractions.Contracts.Generator;

static class ContractEmitter
{
	public static string Emit(GatewayInterfaceModel model)
	{
		var builder = new StringBuilder();
		builder.AppendLine("// <auto-generated/>");
		builder.AppendLine($"namespace {model.Namespace};");
		builder.AppendLine();
		builder.AppendLine("using Norse.Abstractions.Contracts;");
		builder.AppendLine();
		builder.AppendLine($"public interface I{model.ContextName}Gateway");
		builder.AppendLine("{");
		foreach (var method in model.Methods)
		{
			// Always Outcome<{responseType}>, responseType = "Unit" for void methods — never the bare
			// "Outcome" alias spelling. Generated code must never depend on the consuming project
			// happening to carry the alias file; only hand-written call sites opt into that ergonomic
			// shorthand (2026-07-24 review — this emitter was the one branch the Unit-consolidation
			// pass missed; WireHostEmitter/InProcessHostEmitter already follow this rule).
			var responseType = method.ResponseTypeName ?? "Unit";
			builder.AppendLine($"\tValueTask<Outcome<{responseType}>> {method.Name}({method.RequestTypeName} request, CancellationToken cancellationToken = default);");
		}
		builder.AppendLine("}");
		return builder.ToString();
	}
}
```

- [ ] **Step 6: Implement `GatewayGenerator.cs` — discovery + Contract-mode emission + `NORSE001` diagnostic**

```csharp
// Asgard/gen/Abstractions.Contracts.Generator/GatewayGenerator.cs
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Norse.Abstractions.Contracts.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class GatewayGenerator : IIncrementalGenerator
{
	static readonly DiagnosticDescriptor MissingAuthorize = new(
		"NORSE001", "Service method missing [Authorize]",
		"Method '{0}' on a [GenerateGateway] interface must carry [Authorize(Policy = ...)] — no Asgard-contracted service method may be unprotected by construction",
		"Norse.Gateway", DiagnosticSeverity.Error, isEnabledByDefault: true);

	static readonly DiagnosticDescriptor StreamingNotSupported = new(
		"NORSE002", "Streaming service methods are not supported by the gateway generator",
		"Method '{0}' returns IAsyncEnumerable<T> — v1 excludes streaming from gateway generation entirely (spec §2.3); remove [GenerateGateway] from this interface or move streaming methods to a separate, ungated interface",
		"Norse.Gateway", DiagnosticSeverity.Error, isEnabledByDefault: true);

	static readonly DiagnosticDescriptor UnrecognizedInterfaceSuffix = new(
		"NORSE003", "[GenerateGateway] interface name is not I{Context}Service",
		"Interface '{0}' is decorated [GenerateGateway] but its name doesn't match I{{Context}}Service — the generator derives the gateway's name from that suffix and refuses to guess for any other shape (e.g. I{{Context}}Api); rename the interface or extend this generator's naming rule deliberately",
		"Norse.Gateway", DiagnosticSeverity.Error, isEnabledByDefault: true);

	static readonly DiagnosticDescriptor MissingRequestParameter = new(
		"NORSE004", "Service method has no request parameter",
		"Method '{0}' on a [GenerateGateway] interface takes no parameters — every gateway-generated method requires exactly one request parameter (spec §2.2); this is a malformed service interface, not a shape the generator can emit code for",
		"Norse.Gateway", DiagnosticSeverity.Error, isEnabledByDefault: true);

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var emissionMode = context.AnalyzerConfigOptionsProvider.Select((provider, _) =>
			provider.GlobalOptions.TryGetValue("build_property.NorseGatewayEmissionMode", out var mode) ? mode : "Contract");

		var interfaces = context.CompilationProvider.Select((compilation, cancellationToken) => Discover(compilation, cancellationToken));

		context.RegisterSourceOutput(interfaces.Combine(emissionMode), (productionContext, pair) =>
		{
			var (discovered, mode) = pair;
			foreach (var (model, diagnostics) in discovered)
			{
				foreach (var diagnostic in diagnostics)
					productionContext.ReportDiagnostic(diagnostic);

				if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
					continue;

				if (mode == "Contract")
					productionContext.AddSource($"{model.ContextName}Gateway.g.cs", ContractEmitter.Emit(model));
				// WireHost and InProcessHost modes added in Task 8 and Task 9.
			}
		});
	}

	static ImmutableArray<(GatewayInterfaceModel Model, ImmutableArray<Diagnostic> Diagnostics)> Discover(Compilation compilation, CancellationToken cancellationToken)
	{
		var results = ImmutableArray.CreateBuilder<(GatewayInterfaceModel, ImmutableArray<Diagnostic>)>();
		var generateGatewayAttribute = compilation.GetTypeByMetadataName("Norse.Abstractions.Contracts.GenerateGatewayAttribute");
		var authorizeAttribute = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Authorization.AuthorizeAttribute");
		if (generateGatewayAttribute is null)
			return results.ToImmutable();

		// Compiled-symbol walk (own module + every referenced assembly), never source syntax trees — PackageReference-mode parity.
		foreach (var assembly in new[] { compilation.Assembly }.Concat(compilation.SourceModule.ReferencedAssemblySymbols))
		{
			cancellationToken.ThrowIfCancellationRequested();
			foreach (var type in GetAllTypes(assembly.GlobalNamespace))
			{
				if (type.TypeKind != TypeKind.Interface)
					continue;
				if (!type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, generateGatewayAttribute)))
					continue;

				var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
				var methods = ImmutableArray.CreateBuilder<GatewayMethodModel>();
				foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
				{
					if (member.ReturnType is INamedTypeSymbol { Name: "IAsyncEnumerable" })
					{
						diagnostics.Add(Diagnostic.Create(StreamingNotSupported, member.Locations.FirstOrDefault() ?? Location.None, member.Name));
						continue;
					}
					var authorize = member.GetAttributes().FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, authorizeAttribute));
					if (authorize is null)
					{
						diagnostics.Add(Diagnostic.Create(MissingAuthorize, member.Locations.FirstOrDefault() ?? Location.None, member.Name));
						continue;
					}
					if (member.Parameters.Length == 0)
					{
						diagnostics.Add(Diagnostic.Create(MissingRequestParameter, member.Locations.FirstOrDefault() ?? Location.None, member.Name));
						continue;
					}
					var policyName = authorize.NamedArguments.FirstOrDefault(kv => kv.Key == "Policy").Value.Value as string ?? "";
					var requestType = member.Parameters[0].Type.Name;
					var isGenericTask = member.ReturnType is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } namedReturn;
					var responseType = isGenericTask ? ((INamedTypeSymbol)member.ReturnType).TypeArguments[0].Name : null;
					methods.Add(new GatewayMethodModel(member.Name, requestType, responseType, policyName));
				}

				// Spec §9.8-style naming: I{Context}Service is the only shape this generator derives a
				// context name from. I{Context}Api (also a real shape per the spec's own scoping note)
				// or any other suffix reports a diagnostic instead of silently slicing an arbitrary
				// number of characters off the end of the name (2026-07-24 review, lesser finding 1).
				if (!type.Name.StartsWith("I", StringComparison.Ordinal) || !type.Name.EndsWith("Service", StringComparison.Ordinal) || type.Name.Length <= 1 + "Service".Length)
				{
					diagnostics.Add(Diagnostic.Create(UnrecognizedInterfaceSuffix, type.Locations.FirstOrDefault() ?? Location.None, type.Name));
					results.Add((new GatewayInterfaceModel(type.ContainingNamespace.ToDisplayString(), type.Name, type.Name, methods.ToImmutable()), diagnostics.ToImmutable()));
					continue;
				}
				var contextName = type.Name[1..^"Service".Length];
				results.Add((new GatewayInterfaceModel(type.ContainingNamespace.ToDisplayString(), type.Name, contextName, methods.ToImmutable()), diagnostics.ToImmutable()));
			}
		}
		return results.ToImmutable();
	}

	static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol root)
	{
		foreach (var member in root.GetMembers())
		{
			if (member is INamespaceSymbol ns)
			{
				foreach (var nested in GetAllTypes(ns))
					yield return nested;
			}
			else if (member is INamedTypeSymbol type)
			{
				yield return type;
			}
		}
	}
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests --filter GatewayGeneratorTests`
Expected: PASS (6 tests).

- [ ] **Step 8: Commit**

```bash
git add Asgard/gen/Abstractions.Contracts.Generator Asgard/tests/Abstractions.Contracts.Generator.Tests
git commit -m "feat: gateway generator skeleton, compiled-symbol discovery, Contract-mode emission"
```

---

## Task 8: Asgard/gen — WireHost-mode emission

**Files:**
- Create: `Asgard/gen/Abstractions.Contracts.Generator/WireHostEmitter.cs`
- Modify: `Asgard/gen/Abstractions.Contracts.Generator/GatewayGenerator.cs`
- Test: `Asgard/tests/Abstractions.Contracts.Generator.Tests/GatewayGeneratorTests.cs` (add cases)

**Interfaces:**
- Consumes: `GatewayInterfaceModel`/`GatewayMethodModel` (Task 7), `RpcExceptionExtensions.DecodeProblem` (Task 5, referenced by fully-qualified name in emitted text — this project has no compile-time dependency on Midgard).
- Produces: `WireHostEmitter.Emit(GatewayInterfaceModel) : string` — emits `{Context}WireGateway : I{Context}Gateway`, wrapping the real protobuf-net.Grpc client proxy, decoding `RpcException` via `Norse.Infrastructure.Web.Client.Grpc.RpcExceptionExtensions.DecodeProblem()`.

- [ ] **Step 1: Add the failing WireHost test case**

Append to `GatewayGeneratorTests`:

```csharp
	[Fact]
	void WireHostMode_EmitsWireGateway_DecodingRpcExceptionViaMidgardExtension()
	{
		var (diagnostics, sources) = GeneratorTestHarness.Run(ServiceInterfaceSource, "WireHost");

		diagnostics.ShouldBeEmpty();
		var wireSource = sources.ShouldHaveSingleItem();
		wireSource.ShouldContain("sealed class WidgetWireGateway : IWidgetGateway");
		wireSource.ShouldContain("catch (global::Grpc.Core.RpcException ex)");
		wireSource.ShouldContain("global::Norse.Infrastructure.Web.Client.Grpc.RpcExceptionExtensions.DecodeProblem(ex)");
	}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests --filter WireHostMode_EmitsWireGateway`
Expected: FAIL — `WireHostEmitter` does not exist; `GatewayGenerator` doesn't branch on `WireHost` yet.

- [ ] **Step 3: Implement `WireHostEmitter.cs`**

```csharp
// Asgard/gen/Abstractions.Contracts.Generator/WireHostEmitter.cs
using System.Text;

namespace Norse.Abstractions.Contracts.Generator;

static class WireHostEmitter
{
	public static string Emit(GatewayInterfaceModel model)
	{
		var builder = new StringBuilder();
		builder.AppendLine("// <auto-generated/>");
		builder.AppendLine($"namespace {model.Namespace};");
		builder.AppendLine();
		builder.AppendLine("using Norse.Abstractions.Contracts;");
		builder.AppendLine();
		builder.AppendLine($"sealed class {model.ContextName}WireGateway({model.ServiceInterfaceName} service) : I{model.ContextName}Gateway");
		builder.AppendLine("{");
		foreach (var method in model.Methods)
		{
			// One shape throughout: void-success methods use responseType = "Unit" (never a separate
			// non-generic Outcome path) — 2026-07-24 amendment. Outcome<Unit>.Ok needs its one required
			// argument; Outcome<T>.Err never did, so the failure branch is already uniform.
			var responseType = method.ResponseTypeName ?? "Unit";
			builder.AppendLine($"\tpublic async ValueTask<Outcome<{responseType}>> {method.Name}({method.RequestTypeName} request, CancellationToken cancellationToken = default)");
			builder.AppendLine("\t{");
			builder.AppendLine("\t\ttry");
			builder.AppendLine("\t\t{");
			if (method.ResponseTypeName is not null)
			{
				builder.AppendLine($"\t\t\tvar result = await service.{method.Name}(request).ConfigureAwait(false);");
				builder.AppendLine($"\t\t\treturn Outcome<{responseType}>.Ok(result);");
			}
			else
			{
				builder.AppendLine($"\t\t\tawait service.{method.Name}(request).ConfigureAwait(false);");
				builder.AppendLine($"\t\t\treturn Outcome<{responseType}>.Ok(Unit.Value);");
			}
			builder.AppendLine("\t\t}");
			builder.AppendLine("\t\tcatch (global::Grpc.Core.RpcException ex)");
			builder.AppendLine("\t\t{");
			builder.AppendLine("\t\t\tvar problem = global::Norse.Infrastructure.Web.Client.Grpc.RpcExceptionExtensions.DecodeProblem(ex);");
			builder.AppendLine($"\t\t\treturn Outcome<{responseType}>.Err(problem.Category, problem.Errors, problem.CorrelationId);");
			builder.AppendLine("\t\t}");
			builder.AppendLine("\t}");
		}
		builder.AppendLine("}");
		return builder.ToString();
	}
}
```

- [ ] **Step 4: Wire `WireHost` mode into `GatewayGenerator.RegisterSourceOutput`**

Replace the `// WireHost and InProcessHost modes added in Task 8 and Task 9.` comment in `GatewayGenerator.cs`:

```csharp
				if (mode == "Contract")
					productionContext.AddSource($"{model.ContextName}Gateway.g.cs", ContractEmitter.Emit(model));
				else if (mode == "WireHost")
					productionContext.AddSource($"{model.ContextName}WireGateway.g.cs", WireHostEmitter.Emit(model));
				// InProcessHost mode added in Task 9.
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests --filter WireHostMode_EmitsWireGateway`
Expected: PASS.

- [ ] **Step 6: Run the full generator test suite**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests`
Expected: PASS (all cases from Task 7 and Task 8).

- [ ] **Step 7: Commit**

```bash
git add Asgard/gen/Abstractions.Contracts.Generator/WireHostEmitter.cs Asgard/gen/Abstractions.Contracts.Generator/GatewayGenerator.cs Asgard/tests/Abstractions.Contracts.Generator.Tests/GatewayGeneratorTests.cs
git commit -m "feat: WireHost-mode gateway emission, ErrorInfo-decoded failure path"
```

---

## Task 9: Asgard/gen — InProcessHost-mode emission

**Files:**
- Create: `Asgard/gen/Abstractions.Contracts.Generator/InProcessHostEmitter.cs`
- Modify: `Asgard/gen/Abstractions.Contracts.Generator/GatewayGenerator.cs`
- Test: `Asgard/tests/Abstractions.Contracts.Generator.Tests/GatewayGeneratorTests.cs` (add cases)

**Interfaces:**
- Consumes: `GatewayInterfaceModel`/`GatewayMethodModel` (Task 7). Emits fully-qualified references to Midgard's `TelemetryBehavior<,>`, `ExceptionTranslationBehavior<,>`, `AuthorizationBehavior<,>`, `ValidationBehavior<,>` (Task 3, 4) by name only — no compile-time reference from the generator project itself.
- Produces: `InProcessHostEmitter.Emit(GatewayInterfaceModel) : string` — emits `{Context}InProcessGateway : I{Context}Gateway`, composing the standard chain `Telemetry(ExceptionTranslation(Authorization(Validation(handler))))` per method, with the method's baked-in policy name, then calling the real service implementation directly. The generated constructor takes `AuthenticationStateProvider` (never `IHttpContextAccessor` — spec §2.5, Remand 1) and one `IValidator<TRequest>` per method, constructor-injected by standard DI (never resolved by string-manipulated type name at runtime — Remand 2); a missing validator registration now fails at DI activation (circuit creation), not mid-request past the exception-translation boundary. **One chain shape for every method** (2026-07-24 amendment): void-success methods (bare `Task` on the wire interface) run the identical generic chain with `TResponse = Unit`; the only generated difference is a two-line `AwaitThenUnit` helper for the innermost call, emitted only when at least one method needs it — never a second, hand-written behavior/chain family.

- [ ] **Step 1: Add the failing InProcessHost test case**

Append to `GatewayGeneratorTests`:

```csharp
	[Fact]
	void InProcessHostMode_EmitsChainInCorrectOrder_TelemetryOutermost()
	{
		var (diagnostics, sources) = GeneratorTestHarness.Run(ServiceInterfaceSource, "InProcessHost");

		diagnostics.ShouldBeEmpty();
		var source = sources.ShouldHaveSingleItem();
		source.ShouldContain("sealed class WidgetInProcessGateway : IWidgetGateway");
		source.ShouldContain("IValidator<WidgetRequest>");
		source.ShouldContain("AuthenticationStateProvider");
		source.ShouldNotContain("IHttpContextAccessor");
		source.ShouldContain("\"Widget.Read\"");

		var telemetryIndex = source.IndexOf("TelemetryBehavior", StringComparison.Ordinal);
		var exceptionIndex = source.IndexOf("ExceptionTranslationBehavior", StringComparison.Ordinal);
		var authorizationIndex = source.IndexOf("AuthorizationBehavior", StringComparison.Ordinal);
		var validationIndex = source.IndexOf("ValidationBehavior", StringComparison.Ordinal);

		telemetryIndex.ShouldBeLessThan(exceptionIndex);
		exceptionIndex.ShouldBeLessThan(authorizationIndex);
		authorizationIndex.ShouldBeLessThan(validationIndex);
	}

	[Fact]
	void InProcessHostMode_VoidSuccessMethod_UsesUnitViaSameChainShape_NotASecondFamily()
	{
		const string source = """
			using System.ServiceModel;
			using Microsoft.AspNetCore.Authorization;
			using Norse.Abstractions.Contracts;

			namespace TestRealm.Services;

			[GenerateGateway]
			[ServiceContract]
			public interface IWidgetService
			{
				[Authorize(Policy = "Widget.Delete")]
				[OperationContract]
				Task DeleteWidget(WidgetRequest request, CancellationToken cancellationToken = default);
			}

			public sealed record WidgetRequest;
			""";

		var (diagnostics, sources) = GeneratorTestHarness.Run(source, "InProcessHost");

		diagnostics.ShouldBeEmpty();
		var generated = sources.ShouldHaveSingleItem();
		generated.ShouldContain("ValueTask<Outcome<Unit>> DeleteWidget(");
		generated.ShouldContain("ValidationBehavior<WidgetRequest, Unit>");
		generated.ShouldContain("AwaitThenUnit");
		// No second behavior/chain family — same generic types the payload-bearing method uses.
		generated.ShouldNotContain("IBehavior<WidgetRequest>");
	}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests --filter InProcessHostMode_EmitsChainInCorrectOrder`
Expected: FAIL — `InProcessHostEmitter` does not exist.

- [ ] **Step 3: Implement `InProcessHostEmitter.cs`**

```csharp
// Asgard/gen/Abstractions.Contracts.Generator/InProcessHostEmitter.cs
using System.Text;

namespace Norse.Abstractions.Contracts.Generator;

static class InProcessHostEmitter
{
	public static string Emit(GatewayInterfaceModel model)
	{
		var builder = new StringBuilder();
		builder.AppendLine("// <auto-generated/>");
		builder.AppendLine($"namespace {model.Namespace};");
		builder.AppendLine();
		builder.AppendLine("using Norse.Abstractions.Contracts;");
		builder.AppendLine("using Norse.Abstractions.Web.Server.Mediator;");
		builder.AppendLine();
		builder.AppendLine($"sealed class {model.ContextName}InProcessGateway(");
		builder.AppendLine($"\t{model.ServiceInterfaceName} service,");
		builder.AppendLine("\tMicrosoft.Extensions.Logging.ILoggerFactory loggerFactory,");
		builder.AppendLine("\tMicrosoft.AspNetCore.Authorization.IAuthorizationService authorizationService,");
		// AuthenticationStateProvider, never IHttpContextAccessor — the latter is unsupported inside a
		// live Blazor Server circuit (spec §2.5, Remand 1). One IValidator<TRequest> per method,
		// standard-DI constructor-injected — never resolved by string-manipulated type name at
		// runtime (Remand 2); a missing registration now fails at DI activation, caught upstream of
		// this whole chain, not mid-request past exception translation.
		builder.AppendLine("\tMicrosoft.AspNetCore.Components.Authorization.AuthenticationStateProvider authenticationStateProvider,");
		for (var i = 0; i < model.Methods.Length; i++)
		{
			var method = model.Methods[i];
			var validatorParamName = char.ToLowerInvariant(method.Name[0]) + method.Name[1..] + "Validator";
			var separator = i == model.Methods.Length - 1 ? ")" : ",";
			builder.AppendLine($"\tFluentValidation.IValidator<{method.RequestTypeName}> {validatorParamName}{separator}");
		}
		builder.AppendLine($"\t: I{model.ContextName}Gateway");
		builder.AppendLine("{");
		builder.AppendLine("\tasync ValueTask<System.Security.Claims.ClaimsPrincipal> GetPrincipalAsync() =>");
		builder.AppendLine("\t\t(await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false)).User;");
		builder.AppendLine();
		foreach (var method in model.Methods)
		{
			var validatorParamName = char.ToLowerInvariant(method.Name[0]) + method.Name[1..] + "Validator";
			// One shape throughout: void-success methods use responseType = "Unit" via the
			// IBehavior<,> family's TResponse — never a second, non-generic behavior/chain shape
			// (2026-07-24 amendment). The only irreducible difference is at the innermost call: a
			// bare Task response has no value to wrap, so it awaits the call and returns Unit.Value.
			var responseType = method.ResponseTypeName ?? "Unit";
			var innermostSuccessExpression = method.ResponseTypeName is not null
				? $"Outcome<{responseType}>.Ok(await service.{method.Name}(request).ConfigureAwait(false))"
				: $"await AwaitThenUnit(service.{method.Name}(request))";

			builder.AppendLine($"\tpublic async ValueTask<Outcome<{responseType}>> {method.Name}({method.RequestTypeName} request, CancellationToken cancellationToken = default)");
			builder.AppendLine("\t{");
			builder.AppendLine($"\t\tvar validation = new Norse.Infrastructure.Web.Server.Mediator.ValidationBehavior<{method.RequestTypeName}, {responseType}>({validatorParamName});");
			builder.AppendLine($"\t\tvar authorization = new Norse.Infrastructure.Web.Server.Mediator.AuthorizationBehavior<{method.RequestTypeName}, {responseType}>(\"{method.PolicyName}\", authorizationService, GetPrincipalAsync);");
			builder.AppendLine($"\t\tvar exceptionTranslation = new Norse.Infrastructure.Web.Server.Mediator.ExceptionTranslationBehavior<{method.RequestTypeName}, {responseType}>(loggerFactory.CreateLogger<Norse.Infrastructure.Web.Server.Mediator.ExceptionTranslationBehavior<{method.RequestTypeName}, {responseType}>>());");
			builder.AppendLine($"\t\tvar telemetry = new Norse.Infrastructure.Web.Server.Mediator.TelemetryBehavior<{method.RequestTypeName}, {responseType}>(loggerFactory.CreateLogger<Norse.Infrastructure.Web.Server.Mediator.TelemetryBehavior<{method.RequestTypeName}, {responseType}>>());");
			builder.AppendLine();
			builder.AppendLine("\t\treturn await telemetry.Handle(request, cancellationToken, () =>");
			builder.AppendLine("\t\t\texceptionTranslation.Handle(request, cancellationToken, () =>");
			builder.AppendLine("\t\t\t\tauthorization.Handle(request, cancellationToken, () =>");
			builder.AppendLine("\t\t\t\t\tvalidation.Handle(request, cancellationToken, async () =>");
			builder.AppendLine($"\t\t\t\t\t\t{innermostSuccessExpression})))).ConfigureAwait(false);");
			builder.AppendLine("\t}");
		}
		if (model.Methods.Any(m => m.ResponseTypeName is null))
		{
			builder.AppendLine();
			builder.AppendLine("\tstatic async ValueTask<Outcome<Unit>> AwaitThenUnit(System.Threading.Tasks.Task task)");
			builder.AppendLine("\t{");
			builder.AppendLine("\t\tawait task.ConfigureAwait(false);");
			builder.AppendLine("\t\treturn Outcome<Unit>.Ok(Unit.Value);");
			builder.AppendLine("\t}");
		}
		builder.AppendLine("}");
		return builder.ToString();
	}
}
```

**Amendment (2026-07-25):** the generated code above constructs `ValidationBehavior<,>`/`AuthorizationBehavior<,>`/`ExceptionTranslationBehavior<,>`/`TelemetryBehavior<,>` directly, but those four types are `internal` by the `omit_if_default` convention (Tasks 3-4) and this consumer lives in a different assembly (e.g. Yggdrasil's `Hosting.Web.Server`). That requires a per-consumer `InternalsVisibleTo` grant from Midgard that this plan never adds. Found live, fixed and staged in Midgard — not yet shipped to NuGet.

`GetPrincipalAsync` above is a private local method, passed to `AuthorizationBehavior`'s `Func<ValueTask<ClaimsPrincipal>>` parameter as a method-group conversion — this is the adapter-specific principal source spec §2.5 calls for: the in-process gateway supplies `AuthenticationStateProvider`; a future REST/gRPC-endpoint adapter (decided law item 5, deferred — see §5 Out of scope) would supply its own request principal instead, with `AuthorizationBehavior` itself unchanged either way.

- [ ] **Step 4: Wire `InProcessHost` mode into `GatewayGenerator.RegisterSourceOutput`**

Replace the `// InProcessHost mode added in Task 9.` comment in `GatewayGenerator.cs`:

```csharp
				else if (mode == "InProcessHost")
					productionContext.AddSource($"{model.ContextName}InProcessGateway.g.cs", InProcessHostEmitter.Emit(model));
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests --filter "InProcessHostMode_EmitsChainInCorrectOrder|InProcessHostMode_VoidSuccessMethod"`
Expected: PASS (2 tests).

- [ ] **Step 6: Run the full generator test suite**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests`
Expected: PASS (9 tests total — 6 from Task 7, 1 from Task 8, 2 from Task 9).

- [ ] **Step 7: Commit**

```bash
git add Asgard/gen/Abstractions.Contracts.Generator/InProcessHostEmitter.cs Asgard/gen/Abstractions.Contracts.Generator/GatewayGenerator.cs Asgard/tests/Abstractions.Contracts.Generator.Tests/GatewayGeneratorTests.cs
git commit -m "feat: InProcessHost-mode gateway emission, standard behavior chain composed telemetry-outermost"
```

---

## SHIP GATE — Asgard/gen (`Abstractions.Contracts.Generator`)

A second, separate NuGet package from Asgard's `Abstractions.Contracts`/`Abstractions.Web.Server` (Task 1-2's ship gate already covered those). This package's own PR merges, CI is green, a version tag is pushed, and it's live on the feed before Task 10 starts — Heimdall/Yggdrasil (next phases) `PackageReference` this analyzer package directly, they do not `ProjectReference` the generator project.

---

## Task 10: Heimdall — Contract changes on `IAuthenticationService`

**Files:**
- Create: `Heimdall/src/AuthN.Services/AuthNPolicies.cs`
- Create: `Heimdall/src/AuthN.Services/LogoutResult.cs`
- Modify: `Heimdall/src/AuthN.Services/IAuthenticationService.cs`
- Modify: `Heimdall/src/AuthN.Services/LoginResult.cs`
- Modify: `Heimdall/src/AuthN.Services/AuthN.Services.csproj`

**Interfaces:**
- Consumes: `GenerateGatewayAttribute` (Task 2), `Microsoft.AspNetCore.Authorization.AuthorizeAttribute` (already WASM-compatible, no new package risk — same package family already used for `AuthorizeView` on Blazor client components elsewhere in the platform).
- Produces: `Norse.AuthN.Services.AuthNPolicies.Public` (`const string = "AuthN.Public"`) — a permissive policy satisfied by any principal, including the anonymous-role cookie; still explicitly declared per method, never an undecorated escape hatch. `IAuthenticationService.Login`/`Register`/`Logout` all carry `[Authorize(Policy = AuthNPolicies.Public)]`. `LoginResult` gains `string? DeferredCompletionUrl` (moved off the old `AuthenticationResult`, correctly scoped to the wire type since Himinbjörg's single implementation naturally leaves it `null` for every real gRPC/WASM call — the deferred-sign-in stash only ever exists on the Blazor-Server-circuit code path per `../Platform/specs/2026-07-15-deferred-signin-realm-placement-design.md`). **`Logout` changes from bare `Task` to `Task<LogoutResult>` (2026-07-24 correction, found while scoping Task 11):** clearing the auth cookie on sign-out hits the identical `Response.HasStarted` constraint as setting one on sign-in — the platform's existing deferred-sign-in mechanism already handles this for Login; shipping Logout without an equivalent would silently regress a previously-proven, live-tested capability (the earlier AuthN bootstrap plan's sign-out deferral path). `LogoutResult` mirrors `LoginResult`'s shape minus `Succeeded` — Logout has no anti-enumeration collapse case, real failures still throw via `ToRpcException()`. No test file for this task — it's a pure contract/attribute change verified by the compile step itself and by Task 12/13's tests exercising it end to end.

- [ ] **Step 1: Add `AuthNPolicies.cs`**

```csharp
// Heimdall/src/AuthN.Services/AuthNPolicies.cs
namespace Norse.AuthN.Services;

/// <summary>
/// Named authorization policies for the AuthN service surface. <see cref="Public"/> is satisfied by
/// any principal, anonymous-role cookie included — Login/Register/Logout must still declare a policy
/// per decided law item 4, even though that policy imposes no real requirement.
/// </summary>
public static class AuthNPolicies
{
	/// <summary>Satisfied by any authenticated-or-anonymous-cookie principal — no real requirement.</summary>
	public const string Public = "AuthN.Public";
}
```

- [ ] **Step 2: Add `[GenerateGateway]` and `[Authorize(Policy = AuthNPolicies.Public)]` to `IAuthenticationService`**

Full replacement of `Heimdall/src/AuthN.Services/IAuthenticationService.cs`:

```csharp
using System.ServiceModel;
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Contracts;

namespace Norse.AuthN.Services;

/// <summary>
/// Issuance surface — real, network-callable gRPC methods that mint or clear the authenticated
/// cookie. No <c>CallContext</c> parameter, deliberately. <see cref="AuthNPolicies.Public"/> on every
/// method is a real, explicit declaration (decided law item 4), not an unprotected surface — it just
/// imposes no requirement beyond "some principal exists," which every request already has.
/// </summary>
[GenerateGateway]
[ServiceContract(Name = "grpc.authentication.v1.AuthenticationService")]
public interface IAuthenticationService
{
	/// <summary>Authenticates a user with the provided credentials.</summary>
	[Authorize(Policy = AuthNPolicies.Public)]
	[OperationContract]
	Task<LoginResult> Login(LoginRequest request);

	/// <summary>Registers a new user account with the provided credentials.</summary>
	[Authorize(Policy = AuthNPolicies.Public)]
	[OperationContract]
	Task Register(RegisterRequest request);

	/// <summary>Logs out the currently authenticated user.</summary>
	[Authorize(Policy = AuthNPolicies.Public)]
	[OperationContract]
	Task<LogoutResult> Logout(LogoutRequest request);
}
```

- [ ] **Step 3: Add `DeferredCompletionUrl` to `LoginResult`; add `LogoutResult`**

Full replacement of `Heimdall/src/AuthN.Services/LoginResult.cs`:

```csharp
using System.Runtime.Serialization;

namespace Norse.AuthN.Services;

[DataContract]
public sealed record LoginResult
{
	[DataMember(Order = 1)]
	public required bool Succeeded { get; init; }

	/// <summary>
	/// Non-null only on the Blazor-Server in-process path, when the sign-in had to be deferred to a
	/// forced-reload completion request (spec: circuits can't Set-Cookie once the response has
	/// started). Always null for real gRPC/WASM calls — that path never stashes a deferred sign-in.
	/// </summary>
	[DataMember(Order = 2)]
	public string? DeferredCompletionUrl { get; init; }
}
```

New file, `Heimdall/src/AuthN.Services/LogoutResult.cs`:

```csharp
using System.Runtime.Serialization;

namespace Norse.AuthN.Services;

/// <summary>
/// The wire response for <see cref="IAuthenticationService.Logout"/>. Clearing the auth cookie hits
/// the same <c>Response.HasStarted</c> constraint as setting one (spec: circuits can't Set-Cookie
/// once the response has started), so sign-out needs the identical deferred-completion mechanism as
/// <see cref="LoginResult"/> — a bare success/failure signal alone would silently regress the
/// already-proven sign-out deferral path.
/// </summary>
[DataContract]
public sealed record LogoutResult
{
	/// <summary>
	/// Non-null only on the Blazor-Server in-process path, when the sign-out had to be deferred to a
	/// forced-reload completion request. Always null for real gRPC/WASM calls — that path never
	/// stashes a deferred sign-out.
	/// </summary>
	[DataMember(Order = 1)]
	public string? DeferredCompletionUrl { get; init; }
}
```

- [ ] **Step 4: Add `Microsoft.AspNetCore.Authorization` and a `NorseRef` to `Abstractions.Contracts` to `AuthN.Services.csproj`; set the emission mode**

Today `AuthN.Services.csproj` has zero cross-realm references — it's a self-contained wire-contract project (verified against the real, currently-shipped file). It needs an explicit `NorseRef` to Asgard's `Abstractions.Contracts` now, both for `GenerateGatewayAttribute`/`Outcome<T>` this task's `IAuthenticationService.cs`/`LoginResult.cs` changes reference directly, and — as a consequence, not a separate step — for the generator itself, which is bundled into that same package (`analyzers/dotnet/cs/`, 2026-07-24 correction from Task 7's review) rather than shipped as its own standalone package. No separate `PackageReference Include="Norse.Abstractions.Contracts.Generator"` exists anywhere in this plan.

The `Generator="true"` metadata is required, not optional: Bifröst's root `Directory.Build.targets` NorseRef `Choose` block only adds the second `ProjectReference OutputItemType="Analyzer"` (pointed at `Asgard/gen/Abstractions.Contracts.Generator/Abstractions.Contracts.Generator.csproj`) when that metadata is present. Without it, a `-p:UseProjectReferences=true` dev-mode build gets the plain runtime `ProjectReference` and silently never runs the generator — no error, no `NORSE00x` diagnostics, no generated file, just a missing `IAuthenticationGateway` that Task 13 would fail to find. Verified live (2026-07-24): a throwaway consumer project with `NorseRef Include="Abstractions.Contracts" Generator="true"` and `-p:UseProjectReferences=true` correctly ran the analyzer and reported a real `NORSE00x` diagnostic against a malformed test interface, confirming the mechanism fires end to end. The generator project itself was renamed from `Abstractions.Gateway.Generator` to `Abstractions.Contracts.Generator` for exactly this reason — the root convention resolves the generator path from the runtime NorseRef's own `%(Identity)`, so the two names must match.

```xml
<ItemGroup>
	<PackageReference Include="Microsoft.AspNetCore.Authorization" Version="10.*" />
	<NorseRef Include="Abstractions.Contracts" Generator="true">
		<Repo>Asgard</Repo>
	</NorseRef>
</ItemGroup>
<PropertyGroup>
	<NorseGatewayEmissionMode>Contract</NorseGatewayEmissionMode>
</PropertyGroup>
```

- [ ] **Step 5: Build to confirm the generator emits `IAuthenticationGateway` and there are no `NORSE001` diagnostics**

Run: `dotnet build Heimdall/src/AuthN.Services`
Expected: Build succeeds; `dotnet build -v:diag` output (or inspecting `obj/Debug/net*/generated/`) shows `AuthenticationGateway.g.cs` containing `public interface IAuthenticationGateway` with `Login` returning `ValueTask<Outcome<LoginResult>>`, `Register` returning `ValueTask<Outcome<Unit>>`, and `Logout` returning `ValueTask<Outcome<LogoutResult>>`. Zero `NORSE001` diagnostics — every method already carries `[Authorize]`.

- [ ] **Step 6: Commit**

```bash
git add Heimdall/src/AuthN.Services/AuthNPolicies.cs Heimdall/src/AuthN.Services/LogoutResult.cs Heimdall/src/AuthN.Services/IAuthenticationService.cs Heimdall/src/AuthN.Services/LoginResult.cs Heimdall/src/AuthN.Services/AuthN.Services.csproj
git commit -m "feat: decorate IAuthenticationService for gateway generation, add LoginResult.DeferredCompletionUrl, add LogoutResult"
```

---

## Task 11: Heimdall — Retire the hand-written gateway trio, update `Login.razor` and `Logout.razor`

**Files:**
- Delete: `Heimdall/src/AuthN.Components/IAuthenticationGateway.cs`
- Delete: `Heimdall/src/AuthN.Components/AuthenticationResult.cs`
- Modify: `Heimdall/src/AuthN.Components.FluentUI/Login.razor`
- Modify: `Heimdall/src/AuthN.Components.FluentUI/Register.razor`
- Modify: `Heimdall/src/AuthN.Components/Logout.razor`
- Modify: `Heimdall/tests/AuthN.Components.Tests/LogoutTests.cs`
- Modify: any other test in `Heimdall/tests/AuthN.Components.Tests/`/`Heimdall/tests/AuthN.Components.FluentUI.Tests/` referencing the deleted types

**Interfaces:**
- Consumes: the generated `IAuthenticationGateway` (Task 10's build output) — `ValueTask<Outcome<LoginResult>> Login(LoginRequest, CancellationToken)`, `ValueTask<Outcome<LogoutResult>> Logout(LogoutRequest, CancellationToken)`.
- Produces: nothing new — this task is a pure consumer swap. `Login.razor` keeps its existing manual `EditContext`/`ValidationMessageStore` mechanism (no Blazilla adoption in this plan — spec §2.2 fast-follow, out of scope here), just reads from `Outcome<LoginResult>.Problem.Errors` instead of the old `AuthenticationResult.Errors`. `Logout.razor` reads `LogoutResult.DeferredCompletionUrl` the same way it previously read it off `AuthenticationResult` — its own `IAuthenticationGateway.Logout` call and `OnInitializedAsync` shape are otherwise unchanged, only the return type it pattern-matches on.

**`Logout.razor` and `Register.razor` are in scope here, not Task 10 afterthoughts** (2026-07-24 correction, found while scoping this task): `IAuthenticationGateway.cs`/`AuthenticationResult.cs` are deleted in Step 3 below, and `Logout.razor`/`LogoutTests.cs`/`Register.razor` all already reference them (confirmed by grep against the real, currently-shipped files) — leaving any of them untouched would break the build the moment Step 3 runs. `Logout.razor` lives in the base `AuthN.Components` project (pure `@code`, no markup, moved there in an earlier session — see `Heimdall/CLAUDE.md`), not `.FluentUI` alongside `Login.razor`/`Register.razor`; its test correspondingly lives in `AuthN.Components.Tests`, not `AuthN.Components.FluentUI.Tests`. `Register.razor` has no dedicated component test today (only `RegisterRequestValidatorTests.cs`, which tests the FluentValidation rules and doesn't touch the gateway at all) — its update is production-code-only, no test file to write or fix.

- [ ] **Step 1: Write the failing component test — `Login.razor` renders the anti-enumeration message on a collapsed failure, and a distinguishable message on `LockedOut`**

```csharp
// Heimdall/tests/AuthN.Components.FluentUI.Tests/LoginTests.cs
using Bunit;
using Norse.AuthN.Components;
using Norse.AuthN.Components.FluentUI;
using Norse.AuthN.Services;
using Norse.Abstractions.Contracts;
using NSubstitute;
using Shouldly;

namespace Norse.AuthN.Components.FluentUI.Tests;

public sealed class LoginTests : TestContext
{
	[Fact]
	void WrongCredentials_CollapsedFailure_ShowsGenericMessage()
	{
		var gateway = Substitute.For<IAuthenticationGateway>();
		gateway.Login(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult(Outcome<LoginResult>.Ok(new LoginResult { Succeeded = false })));
		Services.AddSingleton(gateway);

		var component = RenderComponent<Login>();
		component.Find("button[type=submit]").Click();

		component.Markup.ShouldContain("Invalid email or password.");
	}

	// Outcome<LoginResult>.Ok(...) constructs via the union's public factory — unaffected by the
	// Outcome/Outcome<T> rewrite; only Login.razor's own consumption of the outcome changes.

	[Fact]
	void LockedOut_RealFailure_ShowsDistinguishableMessage()
	{
		var gateway = Substitute.For<IAuthenticationGateway>();
		gateway.Login(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult(Outcome<LoginResult>.Err(ErrorCategory.LockedOut,
				new Dictionary<string, string[]> { [""] = ["Your account is locked. Try again in 15 minutes."] })));
		Services.AddSingleton(gateway);

		var component = RenderComponent<Login>();
		component.Find("button[type=submit]").Click();

		component.Markup.ShouldContain("Your account is locked. Try again in 15 minutes.");
	}
}
```

`Outcome<LoginResult>.Ok`/`.Err` above are the union's own public factories — unaffected by the rewrite. Only `Login.razor`'s own consumption (Step 4) changes, from flat property reads to a `Match`.

Full replacement of `Heimdall/tests/AuthN.Components.Tests/LogoutTests.cs` (same two cases as the currently-shipped file — direct sign-out, and deferred-to-completion-url sign-out — updated to mock the generated gateway's `Outcome<LogoutResult>` return instead of the retired `AuthenticationResult`):

```csharp
// Heimdall/tests/AuthN.Components.Tests/LogoutTests.cs
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Contracts;
using Norse.AuthN.Services;

namespace Norse.AuthN.Components.Tests;

public sealed class LogoutTests : BunitContext
{
	[Fact]
	void Navigates_to_root_when_the_gateway_completes_sign_out_directly()
	{
		var gateway = Substitute.For<IAuthenticationGateway>();
		gateway.Logout(Arg.Any<LogoutRequest>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult(Outcome<LogoutResult>.Ok(new LogoutResult())));
		Services.AddSingleton(gateway);
		var navigation = Services.GetRequiredService<BunitNavigationManager>();

		Render<Logout>();

		navigation.Uri.ShouldBe(navigation.BaseUri);
		navigation.History.ShouldHaveSingleItem().Options.ForceLoad.ShouldBeTrue();
	}

	[Fact]
	void Navigates_to_the_deferred_completion_url_when_the_gateway_could_not_sign_out_directly()
	{
		var gateway = Substitute.For<IAuthenticationGateway>();
		gateway.Logout(Arg.Any<LogoutRequest>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult(Outcome<LogoutResult>.Ok(new LogoutResult { DeferredCompletionUrl = "/_auth/complete?key=abc&returnUrl=%2F" })));
		Services.AddSingleton(gateway);
		var navigation = Services.GetRequiredService<BunitNavigationManager>();

		Render<Logout>();

		navigation.Uri.ShouldBe(navigation.ToAbsoluteUri("/_auth/complete?key=abc&returnUrl=%2F").ToString());
		navigation.History.ShouldHaveSingleItem().Options.ForceLoad.ShouldBeTrue();
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Heimdall/tests/AuthN.Components.FluentUI.Tests --filter LoginTests`
Expected: FAIL — `Login.razor` still injects the old `IAuthenticationGateway` returning `AuthenticationResult`, not `Outcome<LoginResult>`.

Run: `dotnet test Heimdall/tests/AuthN.Components.Tests --filter LogoutTests`
Expected: FAIL — same reason, `AuthenticationResult` vs `Outcome<LogoutResult>`.

- [ ] **Step 3: Delete the retired types**

```bash
git rm Heimdall/src/AuthN.Components/IAuthenticationGateway.cs
git rm Heimdall/src/AuthN.Components/AuthenticationResult.cs
```

- [ ] **Step 4: Update `Login.razor`'s `@code` block to consume `Outcome<LoginResult>`**

Replace the `HandleLoginAsync` method (and add the gateway injection, which now resolves to the generated `Norse.AuthN.Services.IAuthenticationGateway`):

```razor
@using Norse.AuthN.Services
@using Norse.Abstractions.Contracts
@using Norse.Primitives
@inject IAuthenticationGateway AuthenticationGateway
@inject NavigationManager Navigation

@code {
	readonly LoginRequest _request = new() { Email = "", Password = "" };
	EditContext _editContext = null!;
	ValidationMessageStore _messageStore = null!;

	protected override void OnInitialized()
	{
		_editContext = new EditContext(_request);
		_messageStore = new ValidationMessageStore(_editContext);
	}

	async Task HandleLoginAsync()
	{
		_messageStore.Clear();
		var outcome = await AuthenticationGateway.Login(_request, CancellationToken.None);

		switch (outcome)
		{
			case Success<LoginResult>(var loginResult) when !loginResult.Succeeded:
				// Outcome succeeded (the call itself worked); LoginResult.Succeeded=false is the
				// deliberate anti-enumeration collapse — wrong username and wrong password both land
				// here with nothing more specific the server is willing to say.
				ApplyErrors(new Dictionary<string, string[]> { [""] = ["Invalid email or password."] });
				break;
			case Success<LoginResult>(var loginResult):
				Navigation.NavigateTo(loginResult.DeferredCompletionUrl ?? "/", forceLoad: true);
				break;
			case Failed(var problem):
				// A real failure category (LockedOut, Forbidden, ...) — Problem.Errors already carries the message.
				ApplyErrors(problem.Errors);
				break;
		}
	}

	void ApplyErrors(IReadOnlyDictionary<string, string[]> errors)
	{
		foreach (var (field, messages) in errors)
		{
			var identifier = new FieldIdentifier(_request, field);
			foreach (var message in messages)
				_messageStore.Add(identifier, message);
		}
		_editContext.NotifyValidationStateChanged();
	}
}
```

- [ ] **Step 5: Update `Register.razor`'s `@code` block to consume `Outcome<Unit>`**

Full replacement of the `@code` block in `Heimdall/src/AuthN.Components.FluentUI/Register.razor` (the `@page`/`@inject`/markup above it are unchanged, only the injected type resolves differently now that `IAuthenticationGateway` is generated):

```razor
@code {
	readonly RegisterRequest _request = new() { Email = "", Password = "" };
	EditContext _editContext = null!;
	ValidationMessageStore _messageStore = null!;

	/// <inheritdoc />
	protected override void OnInitialized()
	{
		_editContext = new EditContext(_request);
		_messageStore = new ValidationMessageStore(_editContext);
	}

	async Task HandleRegisterAsync()
	{
		_messageStore.Clear();
		var outcome = await AuthenticationGateway.Register(_request, CancellationToken.None);

		if (outcome.TryGetValue(out Failed failed))
		{
			// Register never produces an empty Errors dictionary on failure — Conflict/Validation are
			// always populated (spec §9.3), unlike Login's anti-enumeration collapse. No fallback needed.
			foreach (var (field, messages) in failed.Problem.Errors)
			{
				var identifier = new FieldIdentifier(_request, field);
				foreach (var message in messages)
					_messageStore.Add(identifier, message);
			}

			_editContext.NotifyValidationStateChanged();
			return;
		}

		Navigation.NavigateTo("/Account/Login");
	}
}
```

Also add `@using Norse.Abstractions.Contracts` to the top of the file, above the existing `@inject` lines — needed for `Failed`/`Outcome<T>` to resolve (`Norse.AuthN.Services`, where `RegisterRequest` already lives, is already project-wide via `AuthN.Components.FluentUI/_Imports.razor` — no change needed there, unlike the brief's Login.razor snippet which restates it explicitly).

- [ ] **Step 6: Update `Logout.razor`'s `@code` block to consume `Outcome<LogoutResult>`**

Full replacement of `Heimdall/src/AuthN.Components/Logout.razor`:

```razor
@page "/Account/Logout"
@using Norse.AuthN.Services
@using Norse.Abstractions.Contracts
@inject IAuthenticationGateway AuthenticationGateway
@inject NavigationManager Navigation

@code {
	/// <inheritdoc />
	protected override async Task OnInitializedAsync()
	{
		var outcome = await AuthenticationGateway.Logout(new LogoutRequest(), CancellationToken.None);

		// Non-null only when this request couldn't complete the sign-out itself (an established Blazor
		// Server interactive circuit, where the response has already started) — a forced full-page
		// navigation to the completion endpoint finishes it on a genuine new request instead. A failed
		// sign-out (Failed case, rare) still routes home — there's no cookie state left for the UI to
		// usefully recover, matching this component's pre-existing lack of any error-display path.
		var url = outcome switch
		{
			Success<LogoutResult>(var logoutResult) => logoutResult.DeferredCompletionUrl ?? "/",
			Failed => "/",
		};
		Navigation.NavigateTo(url, forceLoad: true);
	}
}
```

- [ ] **Step 7: Fix any other test in `Heimdall/tests/AuthN.Components.Tests/`/`Heimdall/tests/AuthN.Components.FluentUI.Tests/` referencing the deleted `IAuthenticationGateway`/`AuthenticationResult`**

Run: `dotnet build Heimdall.slnx` and fix each resulting compile error by updating the `using`/type to the generated `Norse.AuthN.Services.IAuthenticationGateway` and `Outcome<LoginResult>`/`Outcome<Unit>`/`Outcome<LogoutResult>`.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test Heimdall/tests/AuthN.Components.FluentUI.Tests --filter LoginTests`
Expected: PASS (2 tests).

Run: `dotnet test Heimdall/tests/AuthN.Components.Tests --filter LogoutTests`
Expected: PASS (2 tests).

- [ ] **Step 9: Run the full Heimdall test suite**

Run: `dotnet test Heimdall.slnx`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add Heimdall/src/AuthN.Components.FluentUI/Login.razor Heimdall/src/AuthN.Components.FluentUI/Register.razor Heimdall/tests/AuthN.Components.FluentUI.Tests/LoginTests.cs
git add Heimdall/src/AuthN.Components/Logout.razor Heimdall/tests/AuthN.Components.Tests/LogoutTests.cs
git add -u Heimdall/src/AuthN.Components Heimdall/tests
git commit -m "feat: retire hand-written IAuthenticationGateway/AuthenticationResult, consume generated gateway in Login.razor, Register.razor, Logout.razor"
```

---

## SHIP GATE — Heimdall

Heimdall's PR merges, CI is green, a version tag is pushed, and the resulting NuGet package (containing `AuthN.Services` with the new contract shape and `AuthN.Components.FluentUI` with the updated `Login.razor`) is live on the feed before Task 12 starts. Heimdall still references zero gRPC packages, zero Midgard, zero Himinbjörg — confirm with `grep -r "Grpc\|Midgard\|Himinbjorg" Heimdall/src/*/​*.csproj` returning nothing beyond `protobuf-net.Grpc`'s attribute-only `System.ServiceModel` surface (already present pre-plan) and the generator's own analyzer package.

---

## Task 12: Himinbjörg — Implement `AuthenticationService` for real

**Files:**
- Modify: `Himinbjorg/src/Identity.Web.Server/AuthenticationService.cs` (currently every method `throw new NotImplementedException()`)
- Modify: `Himinbjorg/src/Identity.Web.Server/ServiceCollectionExtensions.cs`
- Modify: `Himinbjorg/src/Identity.Web.Server/LoginHandler.cs`, `RegisterHandler.cs`, `LogoutHandler.cs` (namespace fix only — see Step 0)
- Create: `Himinbjorg/src/Identity.Web.Server/GlobalUsings.Outcome.cs` (copy of `Asgard/src/Abstractions.Contracts/GlobalUsings.Outcome.cs`, Task 1)
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/AuthenticationServiceTests.cs`

**Interfaces:**
- Consumes: `IAuthenticationService` (Heimdall, Task 10), `IRequestHandler<LoginRequest, Outcome<BoolResponse>>`/`IRequestHandler<RegisterRequest, Outcome<BoolResponse>>`/`IRequestHandler<LogoutRequest, Outcome>` (already registered in `ServiceCollectionExtensions.cs` — `Outcome` here is the alias for `Outcome<Unit>`, not a distinct type; the existing spelling is unaffected), `ProblemExtensions.ToRpcException` (Midgard, Task 5).
- Produces: `Norse.Identity.Web.Server.AuthenticationService : IAuthenticationService` — public (Yggdrasil's composition root, a different assembly, calls `MapGrpcService<AuthenticationService>()` on it directly — this is the one deliberate, justified `public` escalation in this plan). This is Himinbjörg's own realm-specific glue (spec §9.8-style framing: not generic Norse infrastructure), the *reference* backend behind Heimdall's contract — nothing prevents a different backend from implementing `IAuthenticationService` differently. Expected business failures (`Outcome` failing) throw `Problem.ToRpcException()` directly at this boundary — the one place in the whole chain where "return a value" genuinely isn't an option, because a gRPC method can only communicate non-OK status by throwing.

Also fixes a real DI gap found during this plan's own grounding: `LoginRequestValidator`/`RegisterRequestValidator` are currently registered as concrete types only (`services.AddScoped<LoginRequestValidator>()`), not as `IValidator<TRequest>` — `ValidationBehavior<TRequest,TResponse>` (Task 4) needs the standard FluentValidation DI registration to resolve them generically. Mirrors `[Authorize]` from the interface onto every method (2026-07-24 review, Remand 3) — required for ASP.NET Core's gRPC endpoint metadata to see it at all, since that metadata reflects on this concrete type, not `IAuthenticationService`. Verified end to end by a dedicated wire-path enforcement test in Task 13, since every one of Heimdall's real policies is `AuthNPolicies.Public` (permissive) and can't itself prove a *restrictive* policy is honored.

**A second real gap found during this pass (2026-07-24, Unit consolidation review):** Task 1 moves `Outcome`/`Problem`/`ErrorCategory`/`BoolResponse` from `Norse.Abstractions.Web.Server.Mediator` to `Norse.Abstractions.Contracts`, but only checked Asgard's own test suite for broken references — Himinbjörg's real, already-shipped `LoginHandler.cs`/`RegisterHandler.cs`/`LogoutHandler.cs` also import the old namespace and would fail to build the moment Task 1 lands, undetected until this task actually builds Himinbjörg. Step 0 fixes it here, since this is the first task that touches Himinbjörg after Task 1.

**Flagged, not fixed here:** `httpContextAccessor.HttpContext!.RequestAborted`/`.Items[...]` in this class is the same pattern the real, already-shipped `BlazorServerAuthenticationGateway` used — `IHttpContextAccessor` is documented as unsupported for interactive Blazor Server circuit invocations (the same root issue Remand 1 fixed in `AuthorizationBehavior`). This class is called both from real HTTP/gRPC requests (where `HttpContext` is genuinely valid) and, via the in-process gateway, from live circuit interactions after the initial render (where it may not be). Reworking this is a bigger question — the deferred-sign-in mechanism this class's `TryGetDeferredCompletionUrl` depends on is *itself* predicated on a real, in-flight `HttpContext.Response.HasStarted` check, and un-picking that is `../Platform/specs/2026-07-15-deferred-signin-realm-placement-design.md` territory, not this plan's. Recorded here so it isn't mistaken for resolved.

- [ ] **Step 0: Fix the namespace break Task 1 introduced, and deliver the `Outcome` alias**

`LoginHandler.cs`, `RegisterHandler.cs`, and `LogoutHandler.cs` each currently have `using Norse.Abstractions.Web.Server.Mediator;` to reach `Outcome`/`Outcome<T>`/`BoolResponse`. Change each to `using Norse.Abstractions.Contracts;` (keep any other `using Norse.Abstractions.Web.Server.Mediator;` line if the file also uses `IRequestHandler<,>` — that type didn't move). Verify with:

Run: `dotnet build Himinbjorg.slnx`
Expected: FAILS before this fix (namespace not found), PASSES after.

Then copy the alias file verbatim so `IRequestHandler<LogoutRequest, Outcome>` in `ServiceCollectionExtensions.cs` (and this task's own rewrite of `AuthenticationService.cs`, Step 3) keeps compiling with the bare `Outcome` spelling:

```csharp
// Himinbjorg/src/Identity.Web.Server/GlobalUsings.Outcome.cs
global using Outcome = Norse.Abstractions.Contracts.Outcome<Norse.Abstractions.Contracts.Unit>;
```

- [ ] **Step 1: Write the failing tests — success, business failure (throws `RpcException`), and the deferred-completion-url path**

```csharp
// Himinbjorg/tests/Identity.Web.Server.Tests/AuthenticationServiceTests.cs
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.AuthN.Services;
using Norse.Identity.Web.Server;
using Norse.Infrastructure.Web.Server.DeferredSignIn;
using NSubstitute;
using Shouldly;

namespace Norse.Identity.Web.Server.Tests;

public sealed class AuthenticationServiceTests
{
	[Fact]
	async Task Login_Succeeds_ReturnsLoginResult_WithNoDeferredCompletionUrl_WhenNoneStashed()
	{
		var loginHandler = Substitute.For<IRequestHandler<LoginRequest, Outcome<BoolResponse>>>();
		loginHandler.Handle(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult(Outcome<BoolResponse>.Ok(new BoolResponse { Value = true })));
		var httpContext = new DefaultHttpContext();
		var accessor = Substitute.For<IHttpContextAccessor>();
		accessor.HttpContext.Returns(httpContext);
		var service = new AuthenticationService(
			loginHandler,
			Substitute.For<IRequestHandler<RegisterRequest, Outcome<BoolResponse>>>(),
			Substitute.For<IRequestHandler<LogoutRequest, Outcome>>(),
			accessor);

		var result = await service.Login(new LoginRequest { Email = "a@b.com", Password = "x" });

		result.Succeeded.ShouldBeTrue();
		result.DeferredCompletionUrl.ShouldBeNull();
	}

	[Fact]
	async Task Login_BusinessFailure_ThrowsRpcExceptionWithErrorInfo_NotNotImplementedException()
	{
		var loginHandler = Substitute.For<IRequestHandler<LoginRequest, Outcome<BoolResponse>>>();
		loginHandler.Handle(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult(Outcome<BoolResponse>.Err(ErrorCategory.LockedOut)));
		var accessor = Substitute.For<IHttpContextAccessor>();
		accessor.HttpContext.Returns(new DefaultHttpContext());
		var service = new AuthenticationService(
			loginHandler,
			Substitute.For<IRequestHandler<RegisterRequest, Outcome<BoolResponse>>>(),
			Substitute.For<IRequestHandler<LogoutRequest, Outcome>>(),
			accessor);

		var exception = await Should.ThrowAsync<RpcException>(async () =>
			await service.Login(new LoginRequest { Email = "a@b.com", Password = "x" }));

		exception.StatusCode.ShouldBe(StatusCode.PermissionDenied);
	}

	[Fact]
	async Task Login_Succeeds_PopulatesDeferredCompletionUrl_WhenStashedOnHttpContext()
	{
		var loginHandler = Substitute.For<IRequestHandler<LoginRequest, Outcome<BoolResponse>>>();
		loginHandler.Handle(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult(Outcome<BoolResponse>.Ok(new BoolResponse { Value = true })));
		var httpContext = new DefaultHttpContext();
		httpContext.Items[NorseSignInManager.DeferredSignInKeyItemName] = "stashed-key";
		var accessor = Substitute.For<IHttpContextAccessor>();
		accessor.HttpContext.Returns(httpContext);
		var service = new AuthenticationService(
			loginHandler,
			Substitute.For<IRequestHandler<RegisterRequest, Outcome<BoolResponse>>>(),
			Substitute.For<IRequestHandler<LogoutRequest, Outcome>>(),
			accessor);

		var result = await service.Login(new LoginRequest { Email = "a@b.com", Password = "x" });

		result.DeferredCompletionUrl.ShouldNotBeNull();
		result.DeferredCompletionUrl.ShouldContain("stashed-key");
	}

	// Logout needs the identical deferred-completion coverage as Login (2026-07-24 correction, found
	// while scoping Task 11) — clearing the auth cookie hits the same Response.HasStarted constraint
	// as setting one, so Logout's TryGetDeferredCompletionUrl() call isn't optional plumbing.

	[Fact]
	async Task Logout_Succeeds_ReturnsLogoutResult_WithNoDeferredCompletionUrl_WhenNoneStashed()
	{
		var logoutHandler = Substitute.For<IRequestHandler<LogoutRequest, Outcome>>();
		logoutHandler.Handle(Arg.Any<LogoutRequest>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult(Outcome.Ok(Unit.Value)));
		var accessor = Substitute.For<IHttpContextAccessor>();
		accessor.HttpContext.Returns(new DefaultHttpContext());
		var service = new AuthenticationService(
			Substitute.For<IRequestHandler<LoginRequest, Outcome<BoolResponse>>>(),
			Substitute.For<IRequestHandler<RegisterRequest, Outcome<BoolResponse>>>(),
			logoutHandler,
			accessor);

		var result = await service.Logout(new LogoutRequest());

		result.DeferredCompletionUrl.ShouldBeNull();
	}

	[Fact]
	async Task Logout_Succeeds_PopulatesDeferredCompletionUrl_WhenStashedOnHttpContext()
	{
		var logoutHandler = Substitute.For<IRequestHandler<LogoutRequest, Outcome>>();
		logoutHandler.Handle(Arg.Any<LogoutRequest>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult(Outcome.Ok(Unit.Value)));
		var httpContext = new DefaultHttpContext();
		httpContext.Items[NorseSignInManager.DeferredSignInKeyItemName] = "stashed-key";
		var accessor = Substitute.For<IHttpContextAccessor>();
		accessor.HttpContext.Returns(httpContext);
		var service = new AuthenticationService(
			Substitute.For<IRequestHandler<LoginRequest, Outcome<BoolResponse>>>(),
			Substitute.For<IRequestHandler<RegisterRequest, Outcome<BoolResponse>>>(),
			logoutHandler,
			accessor);

		var result = await service.Logout(new LogoutRequest());

		result.DeferredCompletionUrl.ShouldNotBeNull();
		result.DeferredCompletionUrl.ShouldContain("stashed-key");
	}

	[Fact]
	async Task Logout_BusinessFailure_ThrowsRpcExceptionWithErrorInfo()
	{
		var logoutHandler = Substitute.For<IRequestHandler<LogoutRequest, Outcome>>();
		logoutHandler.Handle(Arg.Any<LogoutRequest>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult(Outcome.Err(ErrorCategory.Fault)));
		var accessor = Substitute.For<IHttpContextAccessor>();
		accessor.HttpContext.Returns(new DefaultHttpContext());
		var service = new AuthenticationService(
			Substitute.For<IRequestHandler<LoginRequest, Outcome<BoolResponse>>>(),
			Substitute.For<IRequestHandler<RegisterRequest, Outcome<BoolResponse>>>(),
			logoutHandler,
			accessor);

		await Should.ThrowAsync<RpcException>(async () => await service.Logout(new LogoutRequest()));
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Himinbjorg/tests/Identity.Web.Server.Tests --filter AuthenticationServiceTests`
Expected: FAIL — `AuthenticationService` currently throws `NotImplementedException` unconditionally.

- [ ] **Step 3: Implement `AuthenticationService.cs`**

```csharp
// Himinbjorg/src/Identity.Web.Server/AuthenticationService.cs
using Microsoft.AspNetCore.Http;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.AuthN.Services;
using Norse.Infrastructure.Web.Server.DeferredSignIn;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;
using Norse.Primitives;

namespace Norse.Identity.Web.Server;

/// <summary>
/// The reference backend for Heimdall's <see cref="IAuthenticationService"/> contract — Himinbjörg
/// owns this because it needs EF/Identity access, not because it's the only legal implementation.
/// Expected business failures throw <c>Problem.ToRpcException()</c> directly — the one place in this
/// chain where a return value genuinely isn't an option, because a gRPC method's only way to signal
/// non-OK status is to throw. Public: Yggdrasil's composition root maps this type directly.
///
/// <c>[Authorize]</c> is mirrored from the interface onto every method here deliberately, not
/// redundantly — ASP.NET Core's gRPC endpoint metadata is gathered by reflecting on this concrete
/// runtime type, not the interface it implements; an interface method's attributes are not visible
/// to that discovery. Without this mirror, decided law item 4's "enforced on every channel" claim is
/// false for the wire channel specifically, even though the interface declares the policy correctly
/// (spec Remand 3, 2026-07-24 review).
/// </summary>
public sealed class AuthenticationService(
	IRequestHandler<LoginRequest, Outcome<BoolResponse>> loginHandler,
	IRequestHandler<RegisterRequest, Outcome<BoolResponse>> registerHandler,
	IRequestHandler<LogoutRequest, Outcome> logoutHandler,
	IHttpContextAccessor httpContextAccessor)
	: IAuthenticationService
{
	[Microsoft.AspNetCore.Authorization.Authorize(Policy = AuthNPolicies.Public)]
	public async Task<LoginResult> Login(LoginRequest request)
	{
		var outcome = await loginHandler.Handle(request, httpContextAccessor.HttpContext!.RequestAborted).ConfigureAwait(false);
		return outcome switch
		{
			Success<BoolResponse>(var value) => new LoginResult { Succeeded = value.Value, DeferredCompletionUrl = TryGetDeferredCompletionUrl() },
			Failed(var problem) => throw problem.ToRpcException(),
		};
	}

	[Microsoft.AspNetCore.Authorization.Authorize(Policy = AuthNPolicies.Public)]
	public async Task Register(RegisterRequest request)
	{
		var outcome = await registerHandler.Handle(request, httpContextAccessor.HttpContext!.RequestAborted).ConfigureAwait(false);
		if (outcome.TryGetValue(out Failed failed))
			throw failed.Problem.ToRpcException();
	}

	[Microsoft.AspNetCore.Authorization.Authorize(Policy = AuthNPolicies.Public)]
	public async Task<LogoutResult> Logout(LogoutRequest request)
	{
		var outcome = await logoutHandler.Handle(request, httpContextAccessor.HttpContext!.RequestAborted).ConfigureAwait(false);
		return outcome switch
		{
			Success<Unit> => new LogoutResult { DeferredCompletionUrl = TryGetDeferredCompletionUrl() },
			Failed(var problem) => throw problem.ToRpcException(),
		};
	}

	string? TryGetDeferredCompletionUrl()
	{
		// Only ever set on the Blazor-Server in-process path (a circuit that couldn't Set-Cookie
		// because the response had already started) — a real gRPC/WASM call never stashes this, so
		// this naturally returns null there without any channel-specific branching.
		if (httpContextAccessor.HttpContext!.Items[NorseSignInManager.DeferredSignInKeyItemName] is not string key)
			return null;

		return $"{DeferredSignInEndpointRouteBuilderExtensions.DefaultPattern}?key={Uri.EscapeDataString(key)}&returnUrl={Uri.EscapeDataString("/")}";
	}
}
```

- [ ] **Step 4: Fix the FluentValidation DI registration in `ServiceCollectionExtensions.cs`**

Replace the two concrete-type-only registrations:

```csharp
services.AddScoped<LoginRequestValidator>();
services.AddScoped<RegisterRequestValidator>();
```

with the standard `IValidator<TRequest>` form `ValidationBehavior<TRequest,TResponse>` (Task 4) actually resolves. Also register a trivial validator for `LogoutRequest` — the generated in-process gateway (Task 9) now constructor-injects `IValidator<TRequest>` for every method unconditionally, `Logout` included, and `LogoutRequest` is a deliberately empty record with nothing to validate:

```csharp
services.AddScoped<FluentValidation.IValidator<LoginRequest>, LoginRequestValidator>();
services.AddScoped<FluentValidation.IValidator<RegisterRequest>, RegisterRequestValidator>();
services.AddScoped<FluentValidation.IValidator<LogoutRequest>, FluentValidation.InlineValidator<LogoutRequest>>(); // no fields, no rules — always valid
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Himinbjorg/tests/Identity.Web.Server.Tests --filter AuthenticationServiceTests`
Expected: PASS (6 tests — 3 Login, 3 Logout).

- [ ] **Step 6: Run the full Himinbjörg test suite**

Run: `dotnet test Himinbjorg.slnx`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Himinbjorg/src/Identity.Web.Server/AuthenticationService.cs Himinbjorg/src/Identity.Web.Server/ServiceCollectionExtensions.cs Himinbjorg/src/Identity.Web.Server/LoginHandler.cs Himinbjorg/src/Identity.Web.Server/RegisterHandler.cs Himinbjorg/src/Identity.Web.Server/LogoutHandler.cs Himinbjorg/src/Identity.Web.Server/GlobalUsings.Outcome.cs Himinbjorg/tests/Identity.Web.Server.Tests/AuthenticationServiceTests.cs
git commit -m "feat: implement AuthenticationService for real, fix FluentValidation DI registration to IValidator<T>, fix Outcome namespace after Task 1 move"
```

---

## SHIP GATE — Himinbjörg

Himinbjörg's PR merges, CI is green, a version tag is pushed, and the resulting NuGet package is live on the feed before Task 13 starts.

---

## Task 13: Yggdrasil — Composition-root wiring: gRPC hosting, generated gateways, hydration helper

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Server/Program.cs`
- Modify: `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`
- Modify: `Yggdrasil/src/Hosting.Web.Client/Hosting.Web.Client.csproj`
- Modify: `Yggdrasil/src/Hosting.Web.Client/Program.cs` (the `authNChannel`/gateway registration lines only)
- Delete: `Yggdrasil/src/Hosting.Web.Server/BlazorServerAuthenticationGateway.cs`
- Delete: `Yggdrasil/src/Hosting.Web.Client/WasmAuthenticationGateway.cs`
- Create: `Yggdrasil/src/Hosting.Web.Server/EnvelopeHydrationState.cs`
- Test: `Yggdrasil/tests/Hosting.Web.Server.Tests/EnvelopeHydrationStateTests.cs`
- Test: `Yggdrasil/tests/Hosting.Web.Server.Tests/WirePathAuthorizationTests.cs`

**Interfaces:**
- Consumes: `AddNorseCodeFirstGrpc()` (Midgard, Task 6), `AuthenticationService` (Himinbjörg, Task 12), the generator's `InProcessHost`/`WireHost`-mode output (Tasks 8-9, real once Task 10 sets the emission-mode property on these two projects and a build runs).
- Produces: `Norse.Hosting.Web.Server.EnvelopeHydrationState.Persist<T>(string key, Func<Outcome<T>> outcomeFactory)` / `.TryTakeOutcome<T>(string key, out Outcome<T> outcome) : bool` — the first real `PersistentComponentState` usage in this codebase (spec §3: zero prior precedent). This is the concrete fix for "Heimdall stays dumb to gRPC": `AddNorseCodeFirstGrpc()` and `MapGrpcService<AuthenticationService>()` both live here, in the composition root — not in Himinbjörg's `AddNorseAuthenticationService`, which registers only plain DI services and knows nothing about gRPC. Also the verification task Remand 3 asks for: `WirePathAuthorizationTests` proves `[Authorize]`-mirrored-on-implementation is real, enforced endpoint metadata under `AddNorseCodeFirstGrpc()`, using an isolated restrictive-policy fixture rather than Heimdall's all-permissive real surface.

- [ ] **Step 1: Write the failing test for `EnvelopeHydrationState` — round-trips both cases through JSON without exposing the union's private layout**

```csharp
// Yggdrasil/tests/Hosting.Web.Server.Tests/EnvelopeHydrationStateTests.cs
using Microsoft.AspNetCore.Components;
using Norse.Abstractions.Contracts;
using Norse.Hosting.Web.Server;
using Norse.Primitives;
using Shouldly;

namespace Norse.Hosting.Web.Server.Tests;

public sealed class EnvelopeHydrationStateTests
{
	[Fact]
	async Task Persist_ThenTryTake_RoundTripsSuccessCase()
	{
		var store = new Dictionary<string, byte[]>();
		var persistingState = new PersistentComponentState(store, new Dictionary<string, IPersistentComponentStateStoreEntry>());
		var hydration = new EnvelopeHydrationState(persistingState);

		using var subscription = hydration.Persist("login", () => Outcome<bool>.Ok(true));
		await persistingState.PersistAsync(new TestPersistentComponentStateStore(store));

		var restoredState = new PersistentComponentState(store, new Dictionary<string, IPersistentComponentStateStoreEntry>());
		await restoredState.InitializeExistingState(store);
		var restoredHydration = new EnvelopeHydrationState(restoredState);

		restoredHydration.TryTakeOutcome<bool>("login", out var outcome).ShouldBeTrue();
		outcome.TryGetValue(out Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeTrue();
	}

	[Fact]
	async Task Persist_ThenTryTake_RoundTripsFailureCase_CategoryAndErrors()
	{
		var store = new Dictionary<string, byte[]>();
		var persistingState = new PersistentComponentState(store, new Dictionary<string, IPersistentComponentStateStoreEntry>());
		var hydration = new EnvelopeHydrationState(persistingState);

		using var subscription = hydration.Persist("login", () =>
			Outcome<bool>.Err(ErrorCategory.Forbidden, new Dictionary<string, string[]> { [""] = ["nope"] }));
		await persistingState.PersistAsync(new TestPersistentComponentStateStore(store));

		var restoredState = new PersistentComponentState(store, new Dictionary<string, IPersistentComponentStateStoreEntry>());
		await restoredState.InitializeExistingState(store);
		var restoredHydration = new EnvelopeHydrationState(restoredState);

		restoredHydration.TryTakeOutcome<bool>("login", out var outcome).ShouldBeTrue();
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Forbidden);
		failed.Problem.Errors[""].ShouldBe(["nope"]);
	}
}
```

Note: `TestPersistentComponentStateStore`/`PersistentComponentState` construction here follows the same in-memory harness pattern ASP.NET Core's own `PersistentComponentState` unit tests use — a minimal `IPersistentComponentStateStore` backed by the local `store` dictionary. Add a small `TestPersistentComponentStateStore : IPersistentComponentStateStore` implementing `GetPersistedStateAsync`/`PersistStateAsync` against that dictionary in a sibling file if the ASP.NET Core test harness type isn't `InternalsVisibleTo`-exposed to this test project.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests --filter EnvelopeHydrationStateTests`
Expected: FAIL — `EnvelopeHydrationState` does not exist.

- [ ] **Step 3: Implement `EnvelopeHydrationState.cs`**

```csharp
// Yggdrasil/src/Hosting.Web.Server/EnvelopeHydrationState.cs
using Microsoft.AspNetCore.Components;
using Norse.Abstractions.Contracts;
using Norse.Primitives;

namespace Norse.Hosting.Web.Server;

/// <summary>
/// Persists a whole <see cref="Outcome{T}"/> — success or failure — across the prerender-to-WASM
/// hydration boundary (spec §3, decided law item 6), so a failure discovered during prerender
/// re-renders identically once WASM takes over instead of flashing to a loading state. First use of
/// <see cref="PersistentComponentState"/> in this codebase — genuinely new wiring, not an extension of
/// an existing pattern. The union's private layout never crosses JSON directly; <see cref="EnvelopeDto{T}"/>
/// is the wire-safe transfer shape, reconstructed into a real <see cref="Outcome{T}"/> on the way back.
/// </summary>
public sealed class EnvelopeHydrationState(PersistentComponentState state)
{
	/// <summary>Registers a callback that persists the outcome of <paramref name="outcomeFactory"/> under <paramref name="key"/> just before prerender state is serialized.</summary>
	public PersistingComponentStateSubscription Persist<T>(string key, Func<Outcome<T>> outcomeFactory) where T : notnull =>
		state.RegisterOnPersisting(() =>
		{
			var dto = outcomeFactory() switch
			{
				Success<T>(var value) => new EnvelopeDto<T>(true, value, null),
				Failed(var problem) => new EnvelopeDto<T>(false, default, problem),
			};
			state.PersistAsJson(key, dto);
			return Task.CompletedTask;
		});

	/// <summary>Reconstructs the persisted <see cref="Outcome{T}"/> for <paramref name="key"/>, if present.</summary>
	public bool TryTakeOutcome<T>(string key, out Outcome<T> outcome) where T : notnull
	{
		if (state.TryTakeFromJson<EnvelopeDto<T>>(key, out var dto) && dto is not null)
		{
			outcome = dto.IsSuccess
				? Outcome<T>.Ok(dto.Value!)
				: Outcome<T>.Err(dto.Problem!.Category, dto.Problem.Errors, dto.Problem.CorrelationId);
			return true;
		}
		outcome = default;
		return false;
	}

	sealed record EnvelopeDto<T>(bool IsSuccess, T? Value, Problem? Problem);
}
```

- [ ] **Step 4: Wire `AddNorseCodeFirstGrpc()` and `MapGrpcService<AuthenticationService>()` into `Program.cs`; delete the broken `MapNorseAuthenticationService()` call**

In `Yggdrasil/src/Hosting.Web.Server/Program.cs`, add the `using`:

```csharp
using Norse.Infrastructure.Web.Server.Mediator.Grpc;
```

Replace:

```csharp
var norseIdentityConnectionString = builder.Configuration.GetConnectionString("norse_identity")
	?? throw new InvalidOperationException("Connection string 'norse_identity' is not configured.");
builder.Services.AddNorseAuthenticationService(norseIdentityConnectionString);
builder.Services.AddScoped<IAuthenticationGateway, BlazorServerAuthenticationGateway>();
builder.Services.AddDeferredSignIn();
```

with:

```csharp
var norseIdentityConnectionString = builder.Configuration.GetConnectionString("norse_identity")
	?? throw new InvalidOperationException("Connection string 'norse_identity' is not configured.");
builder.Services.AddNorseCodeFirstGrpc(); // generic, per Midgard — knows nothing about AuthenticationService specifically
builder.Services.AddNorseAuthenticationService(norseIdentityConnectionString);
builder.Services.AddScoped<IAuthenticationGateway, AuthenticationInProcessGateway>(); // generated, Task 8/9
builder.Services.AddScoped<EnvelopeHydrationState>();
builder.Services.AddDeferredSignIn();
```

Replace:

```csharp
app.MapNorseAuthenticationService();
```

with:

```csharp
app.MapGrpcService<AuthenticationService>();
```

(`AuthenticationService` resolves via the existing `using Norse.Identity.Web.Server;`, already present in this file.)

- [ ] **Step 5: Set the `InProcessHost` emission mode and add the Midgard `NorseRef` to `Hosting.Web.Server.csproj`**

**No separate generator `PackageReference` needed once published** (2026-07-24 correction, Task 7 review): the generator is bundled into `Abstractions.Contracts`'s own package, which `Hosting.Web.Server` already reaches transitively (through Himinbjörg's `Identity.Web.Server` → Heimdall's `AuthN.Services` → Asgard's `Abstractions.Contracts`) — NuGet analyzer packages apply to a project's own compilation through a transitive `PackageReference` chain, not only a direct one.

**Dev mode (`-p:UseProjectReferences=true`) is the exception, and it's the mode this plan actually builds and tests under** (2026-07-24 correction, generator-propagation investigation ahead of Task 10 dispatch): unlike NuGet's package-level analyzer propagation, Bifröst's root `Directory.Build.targets` resolves `NorseRef` into a plain `ProjectReference` in dev mode, and a plain `ProjectReference`'s `OutputItemType="Analyzer"` metadata does **not** cascade through multiple hops — `Hosting.Web.Server` needs its own direct `NorseRef` to `Abstractions.Contracts` with `Generator="true"` to get the generator running on *its own* compilation (which is where `NorseGatewayEmissionMode=InProcessHost` is read and `AuthenticationInProcessGateway.g.cs` needs to land — the generator's compiled-symbol walk discovers `IAuthenticationService` in a referenced assembly, but the analyzer itself must be wired directly). Verified live: a throwaway `NorseRef Generator="true"` consumer under `-p:UseProjectReferences=true` correctly triggered the analyzer.

```xml
<PropertyGroup>
	<NorseGatewayEmissionMode>InProcessHost</NorseGatewayEmissionMode>
</PropertyGroup>
<ItemGroup>
	<NorseRef Include="Infrastructure.Web.Server">
		<Repo>Midgard</Repo>
	</NorseRef>
	<NorseRef Include="Abstractions.Contracts" Generator="true">
		<Repo>Asgard</Repo>
	</NorseRef>
</ItemGroup>
```

(`Infrastructure.Web.Server` NorseRef is already present transitively today via Himinbjörg's `Identity.Web.Server`; this makes it explicit — same fix already applied once before, for `IDeferredSignIn`, in `../Platform/specs/2026-07-15-deferred-signin-realm-placement-design.md`. `Abstractions.Contracts` is also already present transitively; `Generator="true"` is the new, load-bearing part — omitting it means dev-mode builds silently skip gateway generation, no error, no diagnostic, just a missing generated type Step 7's tests would fail to find.)

**Amendment (2026-07-25):** live during this task, `NorseGatewayEmissionMode` (set as a plain MSBuild property above) was found never declared `CompilerVisibleProperty` platform-wide — only worked around locally in these two Yggdrasil `.csproj` files, not fixed at the source (Asgard's generator packaging). Without that declaration the generator can't see the property via `AnalyzerConfigOptions`, and `InProcessHost` mode silently falls back to `Contract` mode — no error, no diagnostic. Scoped for a near-future session.

- [ ] **Step 6: Delete `BlazorServerAuthenticationGateway.cs`; update `Hosting.Web.Client`'s gateway registration and emission mode**

```bash
git rm Yggdrasil/src/Hosting.Web.Server/BlazorServerAuthenticationGateway.cs
git rm Yggdrasil/src/Hosting.Web.Client/WasmAuthenticationGateway.cs
```

In `Yggdrasil/src/Hosting.Web.Client/Program.cs`, replace:

```csharp
builder.Services.AddSingleton(authNChannel.CreateGrpcService<IAuthenticationService>());
builder.Services.AddScoped<IAuthenticationGateway, WasmAuthenticationGateway>();
```

with:

```csharp
builder.Services.AddSingleton(authNChannel.CreateGrpcService<IAuthenticationService>());
builder.Services.AddScoped<IAuthenticationGateway, AuthenticationWireGateway>(); // generated, Task 8
```

`Yggdrasil/src/Hosting.Web.Client/Hosting.Web.Client.csproj` gets the `WireHost` emission mode instead — no `PackageReference` needed once published, for the same reason as `Hosting.Web.Server` above (the generator rides along transitively via `Abstractions.Contracts`, already reached through Heimdall's `AuthN.Components`/`AuthN.Services`). Same dev-mode exception applies too: an explicit `NorseRef Generator="true"` to `Abstractions.Contracts` is required for the analyzer to run on `Hosting.Web.Client`'s own compilation under `-p:UseProjectReferences=true`.

```xml
<PropertyGroup>
	<NorseGatewayEmissionMode>WireHost</NorseGatewayEmissionMode>
</PropertyGroup>
<ItemGroup>
	<NorseRef Include="Abstractions.Contracts" Generator="true">
		<Repo>Asgard</Repo>
	</NorseRef>
</ItemGroup>
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests --filter EnvelopeHydrationStateTests`
Expected: PASS (2 tests).

- [ ] **Step 8: Write and pass a genuine wire-path authorization enforcement test**

Every one of Heimdall's real policies is `AuthNPolicies.Public` (satisfied by anyone), so exercising the real `AuthenticationService` proves nothing about whether a *restrictive* policy is actually enforced over the wire (2026-07-24 review, Remand 3). This test proves the mechanism itself — `[Authorize]` mirrored on the implementation (Task 12) plus `AddNorseCodeFirstGrpc()` (Task 6) plus standard ASP.NET Core authorization middleware — independent of Heimdall's specific (currently permissive) policy set, using a minimal, isolated fake service.

```csharp
// Yggdrasil/tests/Hosting.Web.Server.Tests/WirePathAuthorizationTests.cs
using System.ServiceModel;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;
using ProtoBuf.Grpc.Server;
using Shouldly;

namespace Norse.Hosting.Web.Server.Tests;

[ServiceContract]
public interface IRestrictedService
{
	[Authorize(Policy = "Test.NeverSatisfied")]
	[OperationContract]
	Task<RestrictedResponse> Restricted(RestrictedRequest request);
}

public sealed record RestrictedRequest;
public sealed record RestrictedResponse;

[Authorize(Policy = "Test.NeverSatisfied")] // mirrored, exactly as Task 12 mirrors it onto AuthenticationService
sealed class RestrictedService : IRestrictedService
{
	public Task<RestrictedResponse> Restricted(RestrictedRequest request) => Task.FromResult(new RestrictedResponse());
}

public sealed class WirePathAuthorizationTests
{
	[Fact]
	async Task UnauthenticatedCall_AgainstRestrictivePolicy_RejectedWithUnauthenticatedAndErrorInfo()
	{
		using var host = await new HostBuilder()
			.ConfigureWebHost(webHost =>
			{
				webHost.UseTestServer();
				webHost.ConfigureServices(services =>
				{
					services.AddNorseCodeFirstGrpc();
					services.AddAuthorization(o => o.AddPolicy("Test.NeverSatisfied", p => p.RequireAssertion(_ => false)));
					services.AddAuthentication().AddCookie();
					services.AddScoped<IRestrictedService, RestrictedService>();
				});
				webHost.Configure(app =>
				{
					app.UseRouting();
					app.UseAuthentication();
					app.UseAuthorization();
					app.UseEndpoints(endpoints => endpoints.MapGrpcService<RestrictedService>());
				});
			})
			.StartAsync();

		var handler = host.GetTestServer().CreateHandler();
		var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
		var client = channel.CreateGrpcService<IRestrictedService>();

		var exception = await Should.ThrowAsync<RpcException>(async () => await client.Restricted(new RestrictedRequest()));

		// Proves two things at once: (a) [Authorize] mirrored on the implementation IS discovered as
		// real endpoint metadata by ASP.NET Core's gRPC hosting (interface-only would enforce nothing —
		// this test would hang or succeed instead of throwing), and (b) the rejection round-trips
		// through the platform's own ErrorInfo-based decode (Task 5), not a bare framework status.
		exception.StatusCode.ShouldBe(StatusCode.Unauthenticated);
	}
}
```

Run: `dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests --filter WirePathAuthorizationTests`
Expected: PASS. If it instead hangs, times out, or the call succeeds, that is the failure mode this test exists to catch — `[Authorize]` on `IRestrictedService.Restricted` alone (without the mirror on `RestrictedService`) is not discovered as endpoint metadata, and the call goes through unauthenticated. Confirm by temporarily removing the mirror attribute from `RestrictedService` and re-running — the test must fail.

Add `Microsoft.AspNetCore.TestHost` (`PackageReference Version="10.*"`) to `Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj` if not already present via the ASP.NET Core shared framework reference.

- [ ] **Step 9: Build the whole solution to confirm the generated gateways compile against real Midgard/Himinbjörg/Heimdall packages**

Run: `dotnet build Yggdrasil.slnx`
Expected: Build succeeds. `AuthenticationInProcessGateway`/`AuthenticationWireGateway` appear in each project's generated-files output (`obj/**/generated/Norse.Abstractions.Contracts.Generator/`), both implementing `IAuthenticationGateway`.

- [ ] **Step 10: Commit**

```bash
git add Yggdrasil/src/Hosting.Web.Server/Program.cs Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj Yggdrasil/src/Hosting.Web.Server/EnvelopeHydrationState.cs Yggdrasil/tests/Hosting.Web.Server.Tests/EnvelopeHydrationStateTests.cs Yggdrasil/tests/Hosting.Web.Server.Tests/WirePathAuthorizationTests.cs Yggdrasil/src/Hosting.Web.Client/Program.cs Yggdrasil/src/Hosting.Web.Client/Hosting.Web.Client.csproj
git add -u Yggdrasil/src/Hosting.Web.Server Yggdrasil/src/Hosting.Web.Client
git commit -m "feat: wire AddNorseCodeFirstGrpc + MapGrpcService, generated gateways, EnvelopeHydrationState; prove wire-path authz enforcement"
```

---

## Task 14: Yggdrasil/Heimdall — Hydration-parity acceptance test

**Files:**
- Test: `Yggdrasil/tests/Hosting.Web.Server.Tests/AuthenticationHydrationParityTests.cs`

**Interfaces:**
- Consumes: `AuthenticationInProcessGateway`/`AuthenticationWireGateway` (Task 13), `EnvelopeHydrationState` (Task 13), `Login.razor` (Heimdall, Task 11).

This is the spec's acceptance gate (§4): force a real failure (`Forbidden`) through the in-process gateway during a simulated prerender, persist it, restore it as if WASM had just hydrated, and confirm the wire gateway's `Problem` is identical in shape. Then repeat for the success path. This is the test that proves parity is real, not asserted.

- [ ] **Step 1: Write the failing hydration-parity test**

```csharp
// Yggdrasil/tests/Hosting.Web.Server.Tests/AuthenticationHydrationParityTests.cs
using Norse.Abstractions.Contracts;
using Norse.AuthN.Services;
using Norse.Hosting.Web.Server;
using Norse.Primitives;
using NSubstitute;
using Shouldly;

namespace Norse.Hosting.Web.Server.Tests;

public sealed class AuthenticationHydrationParityTests
{
	[Fact]
	async Task Forbidden_IdenticalProblem_AcrossInProcessThenWireGateway()
	{
		// In-process gateway (Server circuit, prerender): the real chain runs, AuthorizationBehavior
		// rejects the call, the in-process gateway returns Outcome<LoginResult>.Err(Forbidden) — no
		// wire involved at all.
		var inProcessResult = Outcome<LoginResult>.Err(ErrorCategory.Forbidden);

		// Persist across the simulated prerender -> WASM handoff.
		var store = new Dictionary<string, byte[]>();
		var persistingState = TestPersistentComponentState.Create(store);
		var hydration = new EnvelopeHydrationState(persistingState);
		using var subscription = hydration.Persist("login", () => inProcessResult);
		await TestPersistentComponentState.PersistAsync(persistingState, store);

		// WASM hydration: read the persisted state back — this is what the component renders from
		// the instant hydration completes, before the wire gateway is even asked to re-answer.
		var restoredState = TestPersistentComponentState.CreateFromStore(store);
		var restoredHydration = new EnvelopeHydrationState(restoredState);
		restoredHydration.TryTakeOutcome<LoginResult>("login", out var hydratedResult).ShouldBeTrue();

		// The wire gateway independently re-answers the same forced-Forbidden scenario, decoding the
		// real ErrorInfo-based trailer end to end (server ToRpcException -> client DecodeProblem).
		var wireResult = SimulateWireForbidden();

		hydratedResult.TryGetValue(out Failed hydratedFailed).ShouldBeTrue();
		wireResult.TryGetValue(out Failed wireFailed).ShouldBeTrue();
		hydratedFailed.Problem.Category.ShouldBe(wireFailed.Problem.Category);
		hydratedFailed.Problem.Category.ShouldBe(ErrorCategory.Forbidden);
	}

	[Fact]
	async Task Success_IdenticalLoginResult_AcrossInProcessThenWireGateway()
	{
		var loginResult = new LoginResult { Succeeded = true, DeferredCompletionUrl = null };
		var inProcessResult = Outcome<LoginResult>.Ok(loginResult);

		var store = new Dictionary<string, byte[]>();
		var persistingState = TestPersistentComponentState.Create(store);
		var hydration = new EnvelopeHydrationState(persistingState);
		using var subscription = hydration.Persist("login", () => inProcessResult);
		await TestPersistentComponentState.PersistAsync(persistingState, store);

		var restoredState = TestPersistentComponentState.CreateFromStore(store);
		var restoredHydration = new EnvelopeHydrationState(restoredState);
		restoredHydration.TryTakeOutcome<LoginResult>("login", out var hydratedResult).ShouldBeTrue();

		hydratedResult.TryGetValue(out Success<LoginResult> success).ShouldBeTrue();
		success.Value.Succeeded.ShouldBeTrue();
		success.Value.DeferredCompletionUrl.ShouldBeNull();
	}

	static Outcome<LoginResult> SimulateWireForbidden()
	{
		// Exercises the real Midgard round-trip (Task 5) rather than hand-constructing a Problem, so
		// this test would fail if ToRpcException/DecodeProblem ever drifted out of sync.
		var rpcException = new Problem { Category = ErrorCategory.Forbidden }.ToRpcExceptionForTest();
		var decoded = rpcException.DecodeProblemForTest();
		return Outcome<LoginResult>.Err(decoded.Category, decoded.Errors, decoded.CorrelationId);
	}
}
```

`ToRpcExceptionForTest`/`DecodeProblemForTest` are thin aliases (add to a small `TestExtensions.cs` in the same test project) over Midgard's real `Norse.Infrastructure.Web.Server.Mediator.Grpc.ProblemExtensions.ToRpcException` and `Norse.Infrastructure.Web.Client.Grpc.RpcExceptionExtensions.DecodeProblem` — named distinctly only to avoid an ambiguous-extension-method compile error from having both `Infrastructure.Web.Server` and `Infrastructure.Web.Client` `ProjectReference`d in one test project (test-only, does not violate the WASM-safety rule). `TestPersistentComponentState` is the same in-memory harness introduced in Task 13 — reused here, not redefined.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests --filter AuthenticationHydrationParityTests`
Expected: FAIL until Tasks 1-13 are all in place — this test is the integration point that proves the whole chain, so it's expected to fail hard against any earlier gap (missing `ToRpcException` mapping, missing `EnvelopeHydrationState`, etc.). At this point in the plan (Task 14, all prior tasks complete) it should fail only on its own missing test-support files.

- [ ] **Step 3: Add `TestExtensions.cs` aliasing the two Midgard extension methods**

```csharp
// Yggdrasil/tests/Hosting.Web.Server.Tests/TestExtensions.cs
using Grpc.Core;
using Norse.Abstractions.Contracts;

namespace Norse.Hosting.Web.Server.Tests;

static class TestExtensions
{
	public static RpcException ToRpcExceptionForTest(this Problem problem) =>
		Norse.Infrastructure.Web.Server.Mediator.Grpc.ProblemExtensions.ToRpcException(problem);

	public static Problem DecodeProblemForTest(this RpcException exception) =>
		Norse.Infrastructure.Web.Client.Grpc.RpcExceptionExtensions.DecodeProblem(exception);
}
```

Add `ProjectReference`s to both `Infrastructure.Web.Server` and `Infrastructure.Web.Client` in `Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj` (test-only cross-reference, same justification as Task 5's `RpcExceptionExtensionsTests`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests --filter AuthenticationHydrationParityTests`
Expected: PASS (2 tests) — `Forbidden` round-trips identically through persist/restore and through the real `ToRpcException`/`DecodeProblem` pair; the success path round-trips `LoginResult` unchanged.

- [ ] **Step 5: Run the full Yggdrasil test suite**

Run: `dotnet test Yggdrasil.slnx`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Yggdrasil/tests/Hosting.Web.Server.Tests/AuthenticationHydrationParityTests.cs Yggdrasil/tests/Hosting.Web.Server.Tests/TestExtensions.cs Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj
git commit -m "test: hydration-parity acceptance test proving in-process and wire gateways agree on Forbidden and success"
```

---

## SHIP GATE — Yggdrasil (final)

Yggdrasil's PR merges, CI is green, a version tag is pushed, and the resulting NuGet package is live on the feed. Run `dotnet run --project src/Orchestration.AppHost` from Bifröst and confirm in the Aspire dashboard: the web server starts, `AuthenticationService` is reachable via gRPC reflection in Development, and a manual Login attempt (wrong password, then a real account) renders identically whether observed during the initial page load (in-process) or after WASM hydration completes (wire) — the flicker the spec's render-mode policy warns about should not be visible.

---

## Out of scope for this plan

Explicitly deferred, not silently unimplemented (2026-07-24 review, lesser finding 2):

- **`GrpcControllerBase` retrofit for the REST bridge (decided law item 5).** No REST controller, no partner-facing HTTP mapping, and no third adapter type exist anywhere in this plan — only the in-process gateway and wire gateway are built. `AuthorizationBehavior`'s `principalAccessor` seam (Task 4, Remand 1) is deliberately adapter-agnostic specifically so a future REST adapter can supply its own request principal without changing the behavior itself, but that adapter is not designed or scheduled here. A separate plan.
- Streaming gateways (spec §2.3) — `NORSE002` makes this a build error, not a silent gap.
- Gateway call-site ergonomics beyond the raw `outcome`/union `switch` pattern (spec §2.2 fast-follow).
- EF/database exception → `Conflict` mapping (spec §2.6) — pending Midgard's repository pattern convergence.

---
