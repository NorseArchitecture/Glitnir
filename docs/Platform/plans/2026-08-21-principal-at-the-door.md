# Principal at the Door — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (default) or `superpowers:executing-plans` (separate-session fallback, never interchangeable), paired with `superpowers:test-driven-development` on every task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make three invariants structurally true across the platform — every request that reaches the mediator carries a principal with a GUID, every policy is declared once and registers everywhere, and every authentication or authorization failure answers one of exactly two questions.

**Architecture:** Five realms in strict dependency order. Asgard declares the transport contract (`TransportDisposition`), the platform policy names and their declarations, and `[NorsePolicy]` — the one attribute a realm needs to declare a policy. Svartálfheim ships NORSE013, which bans authored `IAllowAnonymous` metadata. Midgard implements the anonymous, browser-composite, probe, and machine-rejection schemes, the endpoint-shaped lane selector, the policy-registration generator, and reprojects both transport folds onto the single contract. Heimdall and Mímir add `[NorsePolicy]` declaration methods beside the policy names they already own, additively. Yggdrasil composes the lanes, replaces four hand-written `AddPolicy` lambdas with one generated call, and enables NORSE013.

**Tech Stack:** .NET 11, C#, ASP.NET Core authentication handlers and policy schemes, Grpc.AspNetCore + protobuf-net.Grpc, Roslyn `IIncrementalGenerator` + `DiagnosticAnalyzer`, xUnit v3 + Shouldly + NSubstitute on Microsoft Testing Platform v2.

**Spec:** `../specs/2026-08-21-principal-at-the-door-design.md` — APPROVED at the design gate 2026-08-21, then **amended twice during planning** (§2.2 probe lane, §3 policy hook) where plan review proved an approved section unbuildable. The plan argues from the spec; executors read both. **Where the spec carries an inline amendment, the amendment is current and the text above it is history** — an executor following an un-amended section would rebuild an architecture this plan deliberately deleted.

**Execution model — realm-by-realm ship gates:** Five phases. Each ends with a `## SHIP GATE` section. Do not start the next phase until the gate is cleared: the realm's PR is merged, GitHub CI is green, a version tag is pushed, and the resulting NuGet package(s) are live on the feed. Inside Bifröst during development (`UseProjectReferences=true`) NorseRef items resolve as ProjectReferences across the submodule tree; the ship gate is what proves the package crossing.

**Fork discipline:** One feature fork per realm (Bifröst `CLAUDE.md` §7). Himinbjörg is deliberately absent from this plan — its fork is held by the EF thread (`feature/access-count-breakout`), and Himinbjorg#49 opens a second one only when this train has published.

## Global Constraints

- Target framework `net11.0` for library and service projects; `netstandard2.0` for analyzer/generator-only projects.
- **`internal sealed` is the default accessibility**, expressed by omission (`omit_if_default`): write `class Foo`, never `internal class Foo`. `public` requires a justified cross-assembly caller named in this plan. CA1852 is an error platform-wide.
- **`var` for return assignments only.** Construction uses target-typed `new()` with the explicit type on the left.
- Tabs for indentation, 4-space width. YAML/JSON/Markdown 2-space, Razor 4-space.
- US English spelling in every identifier, comment, doc, and commit message.
- **Warnings are errors, including IDE0055 formatting.** A single warning fails the build. Build after any format sweep.
- **Test classes are `public sealed`** (public only because xUnit must see them). **Test methods omit the accessibility modifier** — bare `void` / bare `async Task`. Names are sentence-shaped with underscores.
- **Shouldly for every assertion, NSubstitute for every mock.** Both, plus `Xunit`, are global usings from `Directory.Test.props` — never re-add them per file.
- **VSTest `--filter` does not work.** Use `dotnet test tests/<Project> -- --filter-class "*.<ClassName>"`.
- **Generator emitters never call `AppendLine`.** Always `sb.AppendCSharp(...)` (`Norse.Abstractions.Emit`, `[StringSyntax("C#")]`) with raw string literals; decompose repeating sections into interpolated helper methods. BOM-free UTF-8, LF-only, byte-identical across build machines.
- Generators walk **compiled symbols** (`compilation.SourceModule.ReferencedAssemblySymbols`), never source syntax trees — package-mode parity depends on it.
- `NorseRef` for cross-realm references, plain `<ProjectReference>` for same-realm. Never a NorseRef inside a `<Target>` block (YGG301).
- **No automatic git commits.** Stage (`git add`), show the diff, stop — the human commits. This holds even where a step below says "commit"; the step means "stage and hand over."
- No force-push to `master`, no `--no-verify`, no committed secrets.
- **Never `dotnet test` a project containing zero tests** — xUnit v3 fails the run.

---

## File Map

### Asgard (`Norse.Abstractions.*`)

| File | Responsibility |
|---|---|
| `src/Abstractions.Contracts/TransportDisposition.cs` | **Create.** `readonly record struct` carrying `int HttpStatus`, `int GrpcStatus`, `bool BodyPermitted`. Ints deliberately — this assembly is client-safe and may reference neither ASP.NET Core nor Grpc.Core. |
| `src/Abstractions.Contracts/TransportDispositions.cs` | **Create.** The single table: one switch expression over `ErrorCategory` with **no default arm**, so a new member is CS8509. |
| `src/Abstractions.Contracts/ErrorCategory.cs` | **Modify.** Amend `NotAllowed`'s doc comment (spec §1.8) — it is an authorization answer, not a precondition. |
| `src/Abstractions.Web.Server/Authorization/NorsePolicies.cs` | **Create.** Platform-standard policy names. Seeds Asgard#57's set with `Anonymous` and `Probe`. `Machine` is **not** here — it arrives with Himinbjorg#49. |
| `src/Abstractions.Web.Server/Authorization/NorsePolicyAttribute.cs` | **Create.** `[NorsePolicy(name)]` on a `public static void M(AuthorizationPolicyBuilder)` — name in the attribute, shape in the method, one declaration. |
| `src/Abstractions.Web.Server/Authorization/NorsePlatformPolicies.cs` | **Create.** The platform's own declarations for `Anonymous` and `Probe`. |
| `src/Abstractions.Web.Server/Facade/GrpcControllerBase.cs` | **Modify.** `ToProblemResult` projects from `TransportDispositions`; silent categories return a bare `StatusCodeResult`. Same realm as the table, so it ships in the same gate — never a second Asgard tag. |

### Svartálfheim (`Norse.Primitives.*`)

| File | Responsibility |
|---|---|
| `gen/Architecture.Analyzers/AllowAnonymousAnalyzer.cs` | **Create.** NORSE013 — strikes both `[AllowAnonymous]` and fluent `.AllowAnonymous()`. |
| `gen/Architecture.Analyzers/Diagnostics.cs` | **Modify.** Add the NORSE013 descriptor beside the existing NORSE07x family. |

### Midgard (`Norse.Infrastructure.*`)

| File | Responsibility |
|---|---|
| `src/Infrastructure.Web.Server/Authentication/NorseSchemes.cs` | **Create.** The six scheme-name constants. |
| `src/Infrastructure.Web.Server/Authentication/NorseAnonymousOptions.cs` | **Create.** Cookie name, DP purpose, lifetime, attributes (spec §2.3). |
| `src/Infrastructure.Web.Server/Authentication/NorseAnonymousHandler.cs` | **Create.** Mints/reads the anonymous cookie. Never self-selects. |
| `src/Infrastructure.Web.Server/Authentication/NorseBrowserHandler.cs` | **Create.** The composite that owns fallback: identity → delete-on-invalid → anonymous → mint. |
| `src/Infrastructure.Web.Server/Authentication/NorseMachineRejectionHandler.cs` | **Create.** Pre-Bearer machine lane: `NoResult`, silent 401 on challenge, never a cookie. Deleted by #49. |
| `src/Infrastructure.Web.Server/Authentication/NorseProbeHandler.cs` | **Create.** Orchestrator-probe lane: `NoResult`, never a cookie. Keeps a kubelet out of the browser composite. |
| `src/Infrastructure.Web.Server/Authentication/NorseLaneSelector.cs` | **Create.** Endpoint-shaped forwarding. Reads no credentials. |
| `src/Infrastructure.Web.Server/Authentication/AuthenticationBuilderExtensions.cs` | **Create.** `AddNorseAuthentication()` — the one public wireup. |
| `src/Infrastructure.Web.Server/Mediator/PrincipalAccessor.cs` | **Modify.** `Seed` refuses a principal without a GUID identifier (backstop). |
| `src/Infrastructure.Web.Server/Mediator/Grpc/ProblemExtensions.cs` | **Modify.** Project from `TransportDispositions`; omit `ErrorInfo` for silent categories. |
| `src/Infrastructure.Web.Client/Grpc/RpcExceptionExtensions.cs` | **Modify.** Trailerless decode contract (spec §4.3) + amended class comment. |
| `src/Infrastructure.ServiceDefaults.AspNet/WebApplicationExtensions.cs` | **Modify.** Health endpoints move from `.AllowAnonymous()` to `NorsePolicies.Probe`. |
| `gen/Infrastructure.Web.Server.Generator/Policies/PolicyDeclarationDiscovery.cs` | **Create.** Reads `[NorsePolicy]` from metadata across the resolved reference set. |
| `gen/Infrastructure.Web.Server.Generator/Policies/PolicyRegistrationEmitter.cs` | **Create.** Emits `AddNorsePolicies()`. |
| `gen/Infrastructure.Web.Server.Generator/Policies/PolicyRegistrationGenerator.cs` | **Create.** The generator + NORSE014 duplicate-name diagnostic. |

### Heimdall (`Norse.AuthN.*`) and Mímir (`Norse.Reference.*`)

| File | Responsibility |
|---|---|
| `Heimdall/src/AuthN.Services/AuthNPolicies.cs` | **Modify.** Adds the `[NorsePolicy]` configure method beside the existing name constant. |
| `Heimdall/src/AuthN.Services/IdentityPolicies.cs` | **Modify.** Adds `[NorsePolicy]` configure methods for `Self` and `MaskedDisclosure`. |
| `Heimdall/src/AuthN.Components/Login.razor` | **Modify.** Renders a fixed local string for a silent 401. |
| `Mimir/src/Reference.Web.Server/ReferencePolicyDeclarations.cs` | **Create.** Server-side `[NorsePolicy]` declaration; the name constant stays in the thin contracts assembly. |

### Yggdrasil (`Norse.Hosting.*`)

| File | Responsibility |
|---|---|
| `src/Hosting.Web.Server/Program.cs` | **Modify.** `AddNorseAuthentication()`; `AddNorsePolicies()` replaces four lambdas; challenge/forbid per spec §4.4. |
| `tests/Hosting.Web.Server.Tests/Swoop/SwoopHostFixture.cs` | **Modify.** Facade calls carry the machine lane's 401 expectation. |
| `tests/Hosting.Web.Server.Tests/CountryLookupE2ETests.cs` | **Modify.** Same. |
| `Directory.Analyzers.props` | **Modify.** Enable NORSE013. |

---

## Task 0: Discovery — does `GrpcMethodMetadata` reach code-first endpoints?

Spec §2.2's gRPC selector row matches on `Grpc.AspNetCore.Server.GrpcMethodMetadata` in endpoint metadata (namespace corrected 2026-08-21 per Task 0's verdict below — the spec assumed a `.Model` segment this platform's pinned `Grpc.AspNetCore.Server 2.83.0` does not carry). That is verified for `MapGrpcService` with protobuf contracts; it is **asserted but unverified** for protobuf-net.Grpc's code-first binder, which is what `ReferenceService` and `AuthenticationService` actually use. The spec carries a ruled fallback. This task decides which branch the rest of the plan takes, and it is cheap.

**Files:**
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Authentication/GrpcEndpointMetadataDiscoveryTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: the ruling consumed by Task 7's selector — either `GrpcMethodMetadata` is the marker, or `NorseGrpcLaneMetadata` is added at `MapGrpcService` time.

- [ ] **Step 1: Write the probe test**

```csharp
using Grpc.AspNetCore.Server.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProtoBuf.Grpc.Server;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

public sealed class GrpcEndpointMetadataDiscoveryTests
{
	[Fact]
	async Task Code_first_grpc_endpoints_carry_GrpcMethodMetadata()
	{
		using IHost host = await new HostBuilder()
			.ConfigureWebHost(web => web
				.UseTestServer()
				.ConfigureServices(services => services.AddCodeFirstGrpc().AddRouting())
				.Configure(app =>
				{
					app.UseRouting();
					app.UseEndpoints(endpoints => endpoints.MapGrpcService<ProbeService>());
				}))
			.StartAsync();

		var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints;

		endpoints.ShouldNotBeEmpty();
		endpoints.ShouldContain(endpoint => endpoint.Metadata.GetMetadata<GrpcMethodMetadata>() is not null);
	}
}
```

`ProbeService` is a two-line code-first service declared in the same file — one `IProbeService` contract with a single unary method and its implementation, mirroring how `ReferenceService` is shaped.

- [ ] **Step 2: Run it**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests -- --filter-class "*.GrpcEndpointMetadataDiscoveryTests"`

- [ ] **Step 3: Record the verdict in the plan**

**Verdict recorded 2026-08-21: PASS.** `AddCodeFirstGrpc()` + `MapGrpcService<T>()` endpoints carry `GrpcMethodMetadata` on their `EndpointDataSource` metadata, exactly as they do for protobuf-contract `MapGrpcService`. Task 7's selector matches on `GrpcMethodMetadata` directly — the `NorseGrpcLaneMetadata` fallback below is **not needed** and was not built. The probe test stays as a regression guard at `Midgard/tests/Infrastructure.Web.Server.Tests/Authentication/GrpcEndpointMetadataDiscoveryTests.cs` (committed). One correction surfaced while writing the probe: the type's actual namespace is `Grpc.AspNetCore.Server`, not `Grpc.AspNetCore.Server.Model` — fixed at every reference in this document (verified against the pinned `Grpc.AspNetCore.Server 2.83.0` package's metadata directly).

If **PASS** — Task 7 uses `GrpcMethodMetadata`; delete nothing, this test stays as a regression guard that a future Grpc.AspNetCore version has not moved the metadata.

If **FAIL** — take the spec's ruled fallback: add a `NorseGrpcLaneMetadata` marker attached by a `MapGrpcService` wrapper in `Infrastructure.Web.Server`, and Task 7 matches on that instead. The selector row's **position and behavior do not change**; only the type it matches on does. Amend this step with the verdict and the date before proceeding.

- [ ] **Step 4: Stage**

```bash
git add Midgard/tests/Infrastructure.Web.Server.Tests/Authentication/GrpcEndpointMetadataDiscoveryTests.cs
git add Glitnir/docs/Platform/plans/2026-08-21-principal-at-the-door.md
```

---

## Task 1: Asgard — `TransportDisposition` and the single table

Replaces two hand-written switch statements whose agreement rests on the comment *"Verified category by category against `ProblemExtensions.cs`, not assumed."* After this task both edges project from one declaration and cannot disagree.

**Files:**
- Create: `Asgard/src/Abstractions.Contracts/TransportDisposition.cs`
- Create: `Asgard/src/Abstractions.Contracts/TransportDispositions.cs`
- Modify: `Asgard/src/Abstractions.Contracts/ErrorCategory.cs` (the `NotAllowed` doc comment)
- Test: `Asgard/tests/Abstractions.Contracts.Tests/TransportDispositionsTests.cs`

**Interfaces:**
- Consumes: `ErrorCategory` (existing, `Abstractions.Contracts`).
- Produces: `public readonly record struct TransportDisposition(int HttpStatus, int GrpcStatus, bool BodyPermitted)` and `public static TransportDisposition TransportDispositions.For(ErrorCategory category)`. Consumed by Task 7 (Midgard's gRPC fold and client decode), Task 8 (Asgard's REST fold), and Task 14 (Yggdrasil's challenge/forbid).

- [ ] **Step 1: Write the failing test**

```csharp
using Norse.Abstractions.Contracts;

namespace Norse.Abstractions.Contracts.Tests;

public sealed class TransportDispositionsTests
{
	[Theory]
	[InlineData(ErrorCategory.Unauthorized, 401, 16, false)]
	[InlineData(ErrorCategory.InvalidCredentials, 401, 16, false)]
	[InlineData(ErrorCategory.LockedOut, 403, 7, true)]
	[InlineData(ErrorCategory.NotAllowed, 403, 7, true)]
	[InlineData(ErrorCategory.Forbidden, 403, 7, true)]
	[InlineData(ErrorCategory.Validation, 400, 3, true)]
	[InlineData(ErrorCategory.Conflict, 409, 6, true)]
	[InlineData(ErrorCategory.NotFound, 404, 5, false)]
	[InlineData(ErrorCategory.Erased, 410, 5, true)]
	[InlineData(ErrorCategory.Fault, 500, 13, true)]
	[InlineData(ErrorCategory.MultipleMatches, 500, 13, true)]
	[InlineData(ErrorCategory.Unspecified, 500, 2, false)]
	void Declares_the_ruled_disposition_for(ErrorCategory category, int http, int grpc, bool bodyPermitted)
	{
		var disposition = TransportDispositions.For(category);

		disposition.HttpStatus.ShouldBe(http);
		disposition.GrpcStatus.ShouldBe(grpc);
		disposition.BodyPermitted.ShouldBe(bodyPermitted);
	}

	[Fact]
	void No_member_escapes_the_table()
	{
		foreach (var category in Enum.GetValues<ErrorCategory>())
			Should.NotThrow(() => TransportDispositions.For(category));
	}

	[Fact]
	void Silent_categories_never_permit_a_body()
	{
		TransportDispositions.For(ErrorCategory.Unauthorized).BodyPermitted.ShouldBeFalse();
		TransportDispositions.For(ErrorCategory.InvalidCredentials).BodyPermitted.ShouldBeFalse();
	}
}
```

The gRPC integers are `Grpc.Core.StatusCode` values: `Unauthenticated = 16`, `PermissionDenied = 7`, `InvalidArgument = 3`, `AlreadyExists = 6`, `NotFound = 5`, `Internal = 13`, `Unknown = 2`. They are written as ints here because `Abstractions.Contracts` must not reference Grpc.Core — that is the whole point of the shape.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Tests -- --filter-class "*.TransportDispositionsTests"`
Expected: FAIL — `TransportDispositions` does not exist.

- [ ] **Step 3: Write `TransportDisposition`**

```csharp
namespace Norse.Abstractions.Contracts;

/// <summary>
///     The transport shape one <see cref="ErrorCategory" /> answers with, declared once and projected by
///     every edge. Carries plain integers deliberately: this assembly is client-safe and ships into WASM
///     and MAUI, so it may reference neither <c>Microsoft.AspNetCore.Http.StatusCodes</c> nor
///     <c>Grpc.Core.StatusCode</c>. Each edge casts to its own enum at the point of use.
/// </summary>
/// <param name="HttpStatus">The HTTP status code this category folds to at a text-channel edge.</param>
/// <param name="GrpcStatus">The <c>Grpc.Core.StatusCode</c> integer value this category folds to.</param>
/// <param name="BodyPermitted">
///     Whether a response for this category may carry a body at all. <see langword="false" /> for the
///     silent categories: the platform never explains a failed authentication attempt, so there is no
///     branch anywhere that may attach one.
/// </param>
public readonly record struct TransportDisposition(int HttpStatus, int GrpcStatus, bool BodyPermitted);
```

- [ ] **Step 4: Write the table**

```csharp
namespace Norse.Abstractions.Contracts;

/// <summary>
///     The one declaration both transport edges project from — Asgard's <c>GrpcControllerBase.FoldAsync</c>
///     and Midgard's <c>ProblemExtensions.ToRpcException</c>. Before this table the two were hand-written
///     switch statements whose agreement rested on a doc comment; they can no longer disagree because
///     there is only one of them.
/// </summary>
public static class TransportDispositions
{
	/// <summary>Resolves the declared transport shape for <paramref name="category" />.</summary>
	/// <remarks>
	///     Deliberately a switch expression with <b>no default arm</b>: adding an <see cref="ErrorCategory" />
	///     member without declaring its disposition is CS8509, which is an error under the platform's
	///     warnings-as-errors posture. Compile time, not test time.
	/// </remarks>
	public static TransportDisposition For(ErrorCategory category) =>
		category switch
		{
			ErrorCategory.Validation => new(400, 3, true),
			ErrorCategory.NotFound => new(404, 5, false),
			ErrorCategory.Conflict => new(409, 6, true),
			ErrorCategory.LockedOut => new(403, 7, true),
			ErrorCategory.InvalidCredentials => new(401, 16, false),
			ErrorCategory.NotAllowed => new(403, 7, true),
			ErrorCategory.Unauthorized => new(401, 16, false),
			ErrorCategory.Forbidden => new(403, 7, true),
			ErrorCategory.Fault => new(500, 13, true),
			ErrorCategory.MultipleMatches => new(500, 13, true),
			ErrorCategory.Erased => new(410, 5, true),
			ErrorCategory.Unspecified => new(500, 2, false)
		};
}
```

- [ ] **Step 5: Amend `ErrorCategory.NotAllowed`**

Replace the existing one-line doc comment on `NotAllowed = 6` with:

```csharp
	/// <summary>
	///     The caller may not perform this operation in the current state — an authorization answer, not a
	///     request-shape one. Folds to 403 (spec §1.8, ruled 2026-08-21): the question it answers is
	///     "can I do the thing?", never "is this well-formed?". Its prior contract named it a precondition
	///     failure folding to 400; that reading was amended rather than left to contradict the mapping.
	///     Sole production producer is Himinbjörg's <c>LoginHandler</c> for <c>SignInResult.IsNotAllowed</c>.
	/// </summary>
	NotAllowed = 6,
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Tests -- --filter-class "*.TransportDispositionsTests"`
Expected: PASS, all three facts.

- [ ] **Step 7: Prove the exhaustiveness mechanism is real**

Temporarily add a `Probe = 99` member to `ErrorCategory`, build, and confirm the build **fails with CS8509** on `TransportDispositions.For`. Remove the member. This step is verification, not a change — nothing is staged from it.

Run: `dotnet build Asgard/src/Abstractions.Contracts/Abstractions.Contracts.csproj`
Expected: `error CS8509`.

- [ ] **Step 8: Stage**

```bash
git add Asgard/src/Abstractions.Contracts/TransportDisposition.cs
git add Asgard/src/Abstractions.Contracts/TransportDispositions.cs
git add Asgard/src/Abstractions.Contracts/ErrorCategory.cs
git add Asgard/tests/Abstractions.Contracts.Tests/TransportDispositionsTests.cs
```

Commit message for the human: `feat(contracts): declare TransportDisposition as the single transport contract`

---

## Task 2: Asgard — `NorsePolicies`, `[NorsePolicy]`, and the platform declarations

**Files:**
- Create: `Asgard/src/Abstractions.Web.Server/Authorization/NorsePolicies.cs`
- Create: `Asgard/src/Abstractions.Web.Server/Authorization/NorsePolicyAttribute.cs`
- Create: `Asgard/src/Abstractions.Web.Server/Authorization/NorsePlatformPolicies.cs`
- Create: `Asgard/gen/Abstractions.Web.Server.Generator/NorsePolicyDeclarationAnalyzer.cs`
- Modify: `Asgard/gen/Abstractions.Web.Server.Generator/Diagnostics.cs` (add NORSE015)
- Test: `Asgard/tests/Abstractions.Web.Server.Tests/Authorization/NorsePolicyDeclarationTests.cs`
- Test: `Asgard/tests/Abstractions.Web.Server.Generator.Tests/NorsePolicyDeclarationAnalyzerTests.cs`

**Interfaces:**
- Consumes: `Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder` (already referenced by this assembly).
- Produces: `public static class NorsePolicies` with `const string Anonymous` and `const string Probe`; `[AttributeUsage(AttributeTargets.Method)] public sealed class NorsePolicyAttribute(string name)`; `public static class NorsePlatformPolicies` carrying one attributed configure method per platform policy. Consumed by Task 10 (Midgard's generator), Task 11 (probe lane), Tasks 12–13 (realm retrofits), Task 14 (Yggdrasil).

### The declaration shape, and why it is one thing rather than two

**One declaration, not two.** A realm declares a policy as a `public static void` method taking an `AuthorizationPolicyBuilder`, decorated with `[NorsePolicy(Name)]`:

```csharp
[NorsePolicy(AuthNPolicies.Public)]
public static void Public(AuthorizationPolicyBuilder policy) => policy.RequireAuthenticatedUser();
```

The **name** lives in the attribute; the **shape** lives in the method; the generator (Task 10) reads the attribute from metadata and emits a call to the method. There is no second representation, so there is nothing to drift and no agreement to enforce.

An earlier revision of this plan used `INorsePolicyContributor.Contribute(registry)` plus a class-level declaration attribute, with a NORSE015 analyzer keeping the two in sync. That did not survive review, for a reason worth recording so it is not reproposed: the analyzer would have lived in Midgard's generator, and **Asgard declares the platform contributor while sitting upstream of Midgard** — so the one declaration most in need of checking could never have been checked without reversing the realm dependency graph. Heimdall and Mímir would each have needed the generator referenced into their contract projects too. The agreement diagnostic was load-bearing and unreachable; deleting the duplication deletes the need for it.

**What this shape gives up, deliberately.** Policy names must be compile-time constants, so a vocabulary computed at runtime (one policy per permission row, say) cannot be declared this way. That is not a regression — dynamic policy vocabularies belong to a custom `IAuthorizationPolicyProvider`, which composes alongside this mechanism and is the sanctioned escape hatch when Asgard#57 gets there. Every policy that exists on the platform today is a constant.

### NORSE015 is enforced twice, with no overlap

A malformed declaration must fail in the build of whoever wrote it. Midgard's generator alone cannot do that: no realm runs it while declaring its own policies, so Asgard's, Heimdall's, and Mímir's mistakes would surface in Yggdrasil's build with no source location. The rule therefore has two enforcement points, split by where the declaration lives:

| Declaration | Enforced by | Runs in | Location |
|---|---|---|---|
| Source in the compilation being built | **Asgard's bundled analyzer** (this task) | the declaring project | the attribute's own syntax |
| Arrived from a referenced assembly | **Midgard's generator** (Task 10) | the consuming project | `Location.None` + qualified name |

This changes the plan's earlier "fails the first consuming compilation" wording, which was accurate for the generator-only design: with the analyzer in place a malformed declaration **fails the build of whoever wrote it**, and the consuming-build path is the backstop rather than the primary.

One diagnostic id, one set of validation rules, two reachable places — and **the halves are disjoint by construction**, so a source declaration is never reported twice. Task 10's generator skips any invalid declaration whose `ApplicationSyntaxReference` is non-null, because that one already belongs to the analyzer.

Placement follows NORSE010/011's recorded reasoning exactly: the rule keys on `NorsePolicyAttribute`, an Asgard type, and every realm declaring a policy already references `Norse.Abstractions.Web.Server` to name it. Nothing new is plumbed anywhere.

The generator half is not redundant. It is what catches a package built against an older Asgard that had no analyzer, and it is what makes the guarantee hold for a consumer who trusts a dependency they did not build.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Norse.Abstractions.Web.Server.Authorization;

namespace Norse.Abstractions.Web.Server.Tests.Authorization;

public sealed class NorsePolicyDeclarationTests
{
	static MethodInfo Declaration(string name) =>
		typeof(NorsePlatformPolicies)
			.GetMethods(BindingFlags.Public | BindingFlags.Static)
			.Single(m => m.GetCustomAttribute<NorsePolicyAttribute>()?.Name == name);

	static AuthorizationPolicy Build(string name)
	{
		AuthorizationPolicyBuilder builder = new();
		Declaration(name).Invoke(null, [builder]);
		return builder.Build();
	}

	[Fact]
	void The_platform_standard_names_are_namespaced_to_Norse()
	{
		NorsePolicies.Anonymous.ShouldBe("Norse.Anonymous");
		NorsePolicies.Probe.ShouldBe("Norse.Probe");
	}

	[Fact]
	void Both_platform_policies_are_declared_in_metadata()
	{
		var declared = typeof(NorsePlatformPolicies)
			.GetMethods(BindingFlags.Public | BindingFlags.Static)
			.Select(m => m.GetCustomAttribute<NorsePolicyAttribute>()?.Name)
			.Where(name => name is not null)
			.ToArray();

		declared.ShouldBe([NorsePolicies.Anonymous, NorsePolicies.Probe], ignoreOrder: true);
	}

	[Fact]
	void Every_declaration_has_the_signature_the_generator_will_emit_a_call_to()
	{
		foreach (var method in typeof(NorsePlatformPolicies)
			.GetMethods(BindingFlags.Public | BindingFlags.Static)
			.Where(m => m.GetCustomAttribute<NorsePolicyAttribute>() is not null))
		{
			method.ReturnType.ShouldBe(typeof(void));
			method.GetParameters().Select(p => p.ParameterType)
				.ShouldBe([typeof(AuthorizationPolicyBuilder)]);
		}
	}

	[Fact]
	void The_anonymous_policy_requires_a_principal() =>
		Build(NorsePolicies.Anonymous).Requirements
			.ShouldContain(r => r is DenyAnonymousAuthorizationRequirement);

	[Fact]
	void The_probe_policy_builds_despite_requiring_nothing()
	{
		// AuthorizationPolicy's constructor throws InvalidOperationException on an empty requirement set
		// (verified against aspnetcore AuthorizationPolicy.cs), so "requires nothing" cannot be expressed as
		// zero requirements. It is one always-succeed assertion, which is a different thing from
		// RequireAuthenticatedUser and is exactly right here: a kubelet carries no principal at all.
		Build(NorsePolicies.Probe).Requirements.Count.ShouldBe(1);
	}

	[Fact]
	void The_probe_policy_does_not_demand_a_principal() =>
		Build(NorsePolicies.Probe).Requirements
			.ShouldNotContain(r => r is DenyAnonymousAuthorizationRequirement);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Asgard/tests/Abstractions.Web.Server.Tests -- --filter-class "*.NorsePolicyDeclarationTests"`
Expected: FAIL — the types do not exist.

- [ ] **Step 3: Write the declaration attribute**

```csharp
namespace Norse.Abstractions.Web.Server.Authorization;

/// <summary>
///     Declares that the decorated method configures the named authorization policy. The attribute carries
///     the <b>name</b> and the method carries the <b>shape</b> — one declaration with two facets, never two
///     representations that could disagree.
/// </summary>
/// <remarks>
///     Applied to a <c>public static void</c> method taking a single
///     <see cref="Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder" />. Midgard's generator
///     reads this attribute from <b>metadata</b>, so a realm's policies are discoverable when it arrives as
///     a published package — which is how every realm reaches the composition root. Public because the
///     generated registration lives in a different assembly and has to call it.
/// </remarks>
/// <param name="name">The policy name, owned by the declaring realm's <c>{Context}Policies</c> class.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class NorsePolicyAttribute(string name) : Attribute
{
	/// <summary>The declared policy name.</summary>
	public string Name { get; } = name;
}
```

- [ ] **Step 4: Write the platform-standard names**

```csharp
namespace Norse.Abstractions.Web.Server.Authorization;

/// <summary>
///     Platform-standard policy names — the seed of Asgard#57's standard set. Realm-specific names stay in
///     their own <c>{Context}Policies</c> classes; only names every realm can rely on live here.
///     <c>Machine</c> is deliberately absent: it arrives with Himinbjorg#49, declared through
///     <see cref="NorsePolicyAttribute" /> rather than beside it.
/// </summary>
public static class NorsePolicies
{
	/// <summary>
	///     Satisfied by any principal, the anonymous role included. Every request carries a principal, so
	///     this is a real requirement (<c>RequireAuthenticatedUser</c>) rather than the
	///     <c>RequireAssertion(_ =&gt; true)</c> placeholder it replaces.
	/// </summary>
	public const string Anonymous = "Norse.Anonymous";

	/// <summary>
	///     The orchestrator-probe lane: liveness and readiness. Requires nothing, and that is the point —
	///     the exemption is named, greppable, and reviewable instead of an <c>AllowAnonymous</c> escape
	///     hatch NORSE013 would strike. Probe endpoints never reach the mediator, and the probe
	///     <i>authentication</i> lane (Task 7) keeps them out of the browser composite.
	/// </summary>
	public const string Probe = "Norse.Probe";
}
```

- [ ] **Step 5: Write the platform declarations**

The platform's own policies are declared exactly like a realm's — the platform is not a special case in its own mechanism. Without these, `NorsePolicies.Probe` is a constant nothing registers and Task 11's health endpoints name a policy that never resolves.

```csharp
using Microsoft.AspNetCore.Authorization;

namespace Norse.Abstractions.Web.Server.Authorization;

/// <summary>Declares the platform-standard authorization policies.</summary>
public static class NorsePlatformPolicies
{
	/// <summary>Any principal, the anonymous role included.</summary>
	/// <param name="policy">The builder to configure.</param>
	[NorsePolicy(NorsePolicies.Anonymous)]
	public static void Anonymous(AuthorizationPolicyBuilder policy) =>
		policy.RequireAuthenticatedUser();

	/// <summary>The orchestrator-probe lane.</summary>
	/// <param name="policy">The builder to configure.</param>
	[NorsePolicy(NorsePolicies.Probe)]
	public static void Probe(AuthorizationPolicyBuilder policy) =>
		// Deliberately an always-succeed assertion rather than nothing: AuthorizationPolicy's constructor
		// throws on an empty requirement set, so "requires nothing" has to be spelled. It is also the honest
		// shape -- an orchestrator probe carries no principal at all, so RequireAuthenticatedUser would be
		// wrong, not merely stricter. This is the one place the RequireAssertion(_ => true) pattern the rest
		// of this train deletes is actually correct.
		policy.RequireAssertion(_ => true);
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Asgard/tests/Abstractions.Web.Server.Tests -- --filter-class "*.NorsePolicyDeclarationTests"`
Expected: PASS, all six facts.

- [ ] **Step 7: Write the failing analyzer tests**

```csharp
namespace Norse.Abstractions.Web.Server.Generator.Tests;

public sealed class NorsePolicyDeclarationAnalyzerTests
{
	const string Preamble = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Web.Server.Authorization;
		""";

	static string Declaring(string member) => $$"""
		{{Preamble}}
		public static class Sample
		{
			{{member}}
		}
		""";

	[Fact]
	async Task Accepts_a_well_formed_declaration() =>
		await Verify.Analyzer(Declaring("""
			[NorsePolicy("Sample.Public")]
			public static void ConfigurePublic(AuthorizationPolicyBuilder policy) =>
				policy.RequireAuthenticatedUser();
			""")).ShouldReportNothing();

	[Theory]
	[InlineData("""[NorsePolicy("X")] static void M(AuthorizationPolicyBuilder p) { }""")]
	[InlineData("""[NorsePolicy("X")] internal static void M(AuthorizationPolicyBuilder p) { }""")]
	[InlineData("""[NorsePolicy("X")] public void M(AuthorizationPolicyBuilder p) { }""")]
	[InlineData("""[NorsePolicy("X")] public static int M(AuthorizationPolicyBuilder p) => 0;""")]
	[InlineData("""[NorsePolicy("X")] public static void M() { }""")]
	[InlineData("""[NorsePolicy("X")] public static void M(string s) { }""")]
	[InlineData("""[NorsePolicy("X")] public static void M(AuthorizationPolicyBuilder p, int extra) { }""")]
	[InlineData("""[NorsePolicy("X")] public static void M<T>(AuthorizationPolicyBuilder p) { }""")]
	[InlineData("""[NorsePolicy("")] public static void M(AuthorizationPolicyBuilder p) { }""")]
	[InlineData("""[NorsePolicy(null)] public static void M(AuthorizationPolicyBuilder p) { }""")]
	async Task Strikes_a_malformed_declaration(string member) =>
		await Verify.Analyzer(Declaring(member)).ShouldReport("NORSE015");

	[Fact]
	async Task Strikes_a_declaration_on_a_generic_containing_type() =>
		await Verify.Analyzer($$"""
			{{Preamble}}
			public static class Outer<T>
			{
				[NorsePolicy("X")]
				public static void M(AuthorizationPolicyBuilder p) { }
			}
			""").ShouldReport("NORSE015");

	[Fact]
	async Task Ignores_an_undecorated_private_method() =>
		await Verify.Analyzer(Declaring("static void M(AuthorizationPolicyBuilder p) { }"))
			.ShouldReportNothing();
}
```

The private-method case is the one that matters most: before this rule it compiled clean in the declaring project, registered nothing, and surfaced only when a request asked for the missing policy. The last fact is its guard — an *undecorated* private method is nobody's business, so the rule must key on the attribute rather than the shape.

- [ ] **Step 8: Add the NORSE015 descriptor**

In Asgard's `gen/Abstractions.Web.Server.Generator/Diagnostics.cs`, beside NORSE010/011:

```csharp
	public static readonly DiagnosticDescriptor InvalidPolicyDeclaration = new(
		"NORSE015",
		"Invalid [NorsePolicy] declaration",
		"'{0}' is decorated with [NorsePolicy] but {1}",
		"Norse.Mediator",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description:
		"A decorated method is either a valid declaration or a build error -- never silently skipped. This "
		+ "analyzer catches declarations in the project that authors them, where the diagnostic has a real "
		+ "source location; Midgard's policy generator enforces the same rule for declarations arriving from "
		+ "referenced assemblies, which have no syntax to point at. The two halves are disjoint, so a "
		+ "declaration is never reported twice.");
```

- [ ] **Step 9: Write the analyzer**

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Norse.Abstractions.Web.Server.Generator;

/// <summary>
///     NORSE015 in the project that authors the declaration. Ships bundled in
///     <c>Norse.Abstractions.Web.Server</c> for the same reason NORSE010/011 do: it keys on this assembly's
///     own <c>NorsePolicyAttribute</c>, and every realm declaring a policy already references this package
///     to name the attribute at all.
/// </summary>
/// <remarks>
///     Shares its validation rules with Midgard's policy generator and its diagnostic id. The split is by
///     provenance, not by rule: this analyzer sees source, the generator sees metadata, and neither sees
///     what the other does.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NorsePolicyDeclarationAnalyzer : DiagnosticAnalyzer
{
	const string AttributeMetadataName = "Norse.Abstractions.Web.Server.Authorization.NorsePolicyAttribute";
	const string BuilderMetadataName = "Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder";

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[Diagnostics.InvalidPolicyDeclaration];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(start =>
		{
			var attribute = start.Compilation.GetTypeByMetadataName(AttributeMetadataName);
			var builder = start.Compilation.GetTypeByMetadataName(BuilderMetadataName);
			if (attribute is null)
				return;

			start.RegisterSymbolAction(symbol => Inspect(symbol, attribute, builder), SymbolKind.Method);
		});
	}

	static void Inspect(SymbolAnalysisContext context, INamedTypeSymbol attribute, INamedTypeSymbol? builder)
	{
		var method = (IMethodSymbol)context.Symbol;
		var declaration = method.GetAttributes().FirstOrDefault(a =>
			SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));
		if (declaration is null)
			return;

		var reason = Reason(method, declaration, builder);
		if (reason is null)
			return;

		// Non-null by construction here: a symbol action on this compilation's own source always has one.
		var location = declaration.ApplicationSyntaxReference is { } reference ?
			Location.Create(reference.SyntaxTree, reference.Span) :
			method.Locations.FirstOrDefault() ?? Location.None;

		context.ReportDiagnostic(Diagnostic.Create(
			Diagnostics.InvalidPolicyDeclaration, location,
			$"{method.ContainingType.ToDisplayString()}.{method.Name}", reason));
	}

	static string? Reason(IMethodSymbol method, AttributeData declaration, INamedTypeSymbol? builder)
	{
		if (declaration.ConstructorArguments is not [{ Value: string name }] || string.IsNullOrWhiteSpace(name))
			return "the policy name must be a non-empty constant string";
		if (!method.IsStatic)
			return "the method must be static";
		if (method.DeclaredAccessibility != Accessibility.Public)
			return "the method must be public -- generated registration lives in another assembly";
		if (method.IsGenericMethod || method.ContainingType.IsGenericType)
			return "neither the method nor its containing type may be generic";
		if (!method.ReturnsVoid)
			return "the method must return void";

		return method.Parameters is [{ Type: var parameter }]
			&& builder is not null
			&& SymbolEqualityComparer.Default.Equals(parameter, builder) ?
			null :
			"the method must take exactly one AuthorizationPolicyBuilder parameter";
	}
}
```

`Reason` is deliberately near-identical to Task 10's `Validate`. **Keep them in sync by hand and say so in both files' doc comments** — this is the one duplication this design accepts, because the alternative is a shared netstandard2.0 package that Midgard's generator and Asgard's analyzer both take, which is more coupling than a nine-line rule list is worth.

**Two known gaps in this analyzer, deliberately deferred to the cleanup wave.** Both are already covered by Midgard's metadata backstop, so neither is a correctness hole — what they cost is the *lifecycle*: you find out at the consumer's build instead of your own.

1. **Containing-type accessibility is not checked here.** A `public static` declaration inside an `internal` class passes this analyzer and is rejected by Midgard's `IsSymbolAccessibleWithin`. Fix is a walk up the containing-type chain requiring external accessibility and non-genericity at every level, plus source facts for an internal outer type and a public-nested-in-internal type. Additive; a few lines and two tests.
2. **A malformed declaration in generated source can be skipped by both halves.** This analyzer sets `GeneratedCodeAnalysisFlags.None`, and Task 10's generator skips anything with a non-null `ApplicationSyntaxReference` — so a generated declaration falls between them. Nothing on the platform generates policy declarations today. Fix is one condition either way: enable this analyzer for generated code, or narrow the generator's skip to non-generated syntax.

Recorded rather than fixed, per the cleanup-wave ruling (2026-08-21). Neither becomes harder later; both are additive to code that already works.

- [ ] **Step 10: Run the analyzer tests**

Run: `dotnet test Asgard/tests/Abstractions.Web.Server.Generator.Tests -- --filter-class "*.NorsePolicyDeclarationAnalyzerTests"`
Expected: PASS.

- [ ] **Step 11: Prove it fires on a real declaration**

Temporarily make `NorsePlatformPolicies.Anonymous` private, build, confirm **NORSE015 fires as an error with a source location**, then restore. Verification only; nothing is staged from this step.

Run: `dotnet build Asgard/src/Abstractions.Web.Server/Abstractions.Web.Server.csproj`
Expected: `error NORSE015`.


- [ ] **Step 12: Build the whole realm**

Run: `dotnet build Asgard/Asgard.slnx`
Expected: zero warnings (warnings are errors, IDE0055 included).

- [ ] **Step 13: Stage**

```bash
git add Asgard/src/Abstractions.Web.Server/Authorization/
git add Asgard/gen/Abstractions.Web.Server.Generator/NorsePolicyDeclarationAnalyzer.cs
git add Asgard/gen/Abstractions.Web.Server.Generator/Diagnostics.cs
git add Asgard/tests/Abstractions.Web.Server.Tests/Authorization/
git add Asgard/tests/Abstractions.Web.Server.Generator.Tests/NorsePolicyDeclarationAnalyzerTests.cs
```

Commit message for the human: `feat(web.server): declare the policy contributor hook and platform policy names`

---

## Task 3: Asgard — the REST fold projects from the table

`GrpcControllerBase.ToProblemResult` is one of the two hand-written switches Task 1 exists to retire. It lives in the same realm as the table and `Abstractions.Web.Server` already references `Abstractions.Contracts`, so it ships inside the Asgard gate rather than forcing a second tag.

**Files:**
- Modify: `Asgard/src/Abstractions.Web.Server/Facade/GrpcControllerBase.cs`
- Test: `Asgard/tests/Abstractions.Web.Server.Tests/Facade/GrpcControllerBaseFoldTests.cs`

**Interfaces:**
- Consumes: `TransportDispositions.For(ErrorCategory)` from Task 1.
- Produces: no new public surface. `FoldAsync` behavior changes: silent categories now return a bare `StatusCodeResult`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.AspNetCore.Mvc;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Facade;

namespace Norse.Abstractions.Web.Server.Tests.Facade;

public sealed class GrpcControllerBaseFoldTests
{
	sealed class Probe : GrpcControllerBase
	{
		public Task<ActionResult<string>> Fold(Outcome<string> outcome) =>
			FoldAsync(new ValueTask<Outcome<string>>(outcome));
	}

	static Outcome<string> Failure(ErrorCategory category) =>
		Outcome<string>.Err(category, new Dictionary<string, string[]> { [""] = ["leaked detail"] });

	[Theory]
	[InlineData(ErrorCategory.Unauthorized)]
	[InlineData(ErrorCategory.InvalidCredentials)]
	async Task Silent_categories_fold_to_a_bare_status_with_no_body(ErrorCategory category)
	{
		var result = await new Probe().Fold(Failure(category));

		var bare = result.Result.ShouldBeOfType<StatusCodeResult>();
		bare.StatusCode.ShouldBe(401);
	}

	[Fact]
	async Task NotAllowed_folds_to_403_with_a_body()
	{
		var result = await new Probe().Fold(Failure(ErrorCategory.NotAllowed));

		var problem = result.Result.ShouldBeOfType<ObjectResult>();
		problem.StatusCode.ShouldBe(403);
	}

	[Fact]
	async Task Every_category_folds_to_its_declared_http_status()
	{
		foreach (var category in Enum.GetValues<ErrorCategory>().Where(c => c != ErrorCategory.Unspecified))
		{
			var expected = TransportDispositions.For(category).HttpStatus;
			var result = await new Probe().Fold(Failure(category));

			var status = result.Result switch
			{
				StatusCodeResult bare => bare.StatusCode,
				ObjectResult obj => obj.StatusCode,
				NotFoundResult => 404,
				_ => -1
			};
			status.ShouldBe(expected, $"{category} folded to {status}, expected {expected}");
		}
	}
}
```

The third fact is the agreement guard: it reads the expectation from the table rather than restating it, so a table edit cannot silently diverge from the fold.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Asgard/tests/Abstractions.Web.Server.Tests -- --filter-class "*.GrpcControllerBaseFoldTests"`
Expected: FAIL — `Unauthorized` currently returns an `ObjectResult` carrying problem details, and `NotAllowed` returns 400.

- [ ] **Step 3: Reproject the fold**

Replace the `statusCode` switch in `ToProblemResult` and add the silent short-circuit. `FoldAsync`'s match arm becomes:

```csharp
		var outcome = await operation.ConfigureAwait(false);
		return outcome.Match<ActionResult<TResponse>>(
			success => Ok(success),
			problem => ToResult(problem));
```

and `ToProblemResult` becomes:

```csharp
	ActionResult ToResult(Problem problem)
	{
		var disposition = TransportDispositions.For(problem.Category);

		// The silent categories and the bodyless 404 share one exit, and it is the only exit that can
		// produce them: there is no branch below that could attach a body to a disposition which does not
		// permit one. That is the structural half of the "401 explains nothing" ruling -- not a
		// convention a future edit could forget.
		if (!disposition.BodyPermitted)
			return new StatusCodeResult(disposition.HttpStatus);

		Dictionary<string, object?>? extensions = null;
		if (problem.Errors.Count > 0)
		{
			extensions = new Dictionary<string, object?>
			{
				["errors"] = problem.Errors
					.SelectMany(entry => entry.Value.Select(message => new ProblemErrorEntry(entry.Key, message)))
					.ToArray()
			};
		}

		if (problem.CorrelationId is { } correlationId)
		{
			extensions ??= [];
			extensions["correlationId"] = correlationId;
		}

		if (problem.Receipt is { } receipt)
		{
			extensions ??= [];
			extensions["receipt"] = receipt.ReceiptId;
			extensions["severedAt"] = receipt.SeveredAt.ToString("O", CultureInfo.InvariantCulture);
		}

		var result = Problem(statusCode: disposition.HttpStatus, title: problem.Category.ToString(),
			extensions: extensions);
		result.ContentTypes.Add("application/problem+json");
		result.ContentTypes.Add("application/problem+xml");
		return result;
	}
```

Amend the class doc comment: the sentence *"Verified category by category against `ProblemExtensions.cs`, not assumed"* is deleted and replaced with a pointer to `TransportDispositions` as the shared source. The comment claimed a verification a human had to keep performing; the table makes the claim structural.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Asgard/tests/Abstractions.Web.Server.Tests -- --filter-class "*.GrpcControllerBaseFoldTests"`
Expected: PASS.

- [ ] **Step 5: Run the whole realm's tests**

Run: `dotnet test Asgard/Asgard.slnx`
Expected: green. Existing fold tests asserting `Unauthorized` → problem-details body will fail — **that is the intended behavior change**; update them to the bare-status expectation rather than weakening the new rule.

- [ ] **Step 6: Stage**

```bash
git add Asgard/src/Abstractions.Web.Server/Facade/GrpcControllerBase.cs
git add Asgard/tests/Abstractions.Web.Server.Tests/Facade/GrpcControllerBaseFoldTests.cs
```

Commit message for the human: `refactor(facade): fold from TransportDispositions; silent categories carry no body`

---

## SHIP GATE — Asgard

Do not start Task 4 until every box is checked.

- [ ] `dotnet build Asgard/Asgard.slnx` — zero warnings.
- [ ] `dotnet test Asgard/Asgard.slnx` — green.
- [ ] PR opened from Asgard's feature fork, reviewed, **merged to `master`**.
- [ ] GitHub CI green on `master`.
- [ ] Version tag pushed.
- [ ] `Norse.Abstractions.Contracts` and `Norse.Abstractions.Web.Server` live on the NuGet feed at that version.

**Why this gate is real:** Midgard's folds and its policy generator both consume `TransportDispositions` and `NorsePolicyAttribute` across a genuine package crossing. Inside Bifröst they resolve as ProjectReferences; the published package is what proves the crossing works for a consumer who is not standing in this tree.

---

## Task 4: Svartálfheim — NORSE013, the `AllowAnonymous` ban

Bans **any authored construct adding `IAllowAnonymous` endpoint metadata** — the attribute and the fluent call alike. An attribute-only rule would leave the escape hatch open while the enforcement ledger claimed the law was enforced; `Midgard/src/Infrastructure.ServiceDefaults.AspNet/WebApplicationExtensions.cs` uses the fluent form on both health endpoints today.

Ships **inert** — the diagnostic is authored and tested here, and only enabled at Yggdrasil (Task 15) once the door exists. The ban never precedes the road.

**Files:**
- Create: `Svartalfheim/gen/Architecture.Analyzers/AllowAnonymousAnalyzer.cs`
- Modify: `Svartalfheim/gen/Architecture.Analyzers/Diagnostics.cs`
- Test: `Svartalfheim/tests/Architecture.Analyzers.Tests/AllowAnonymousAnalyzerTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: diagnostic id `NORSE013`, default severity `Warning` and `isEnabledByDefault: false`, promoted to error per-realm via `Directory.Analyzers.props` in Task 15.

- [ ] **Step 1: Add the descriptor**

In `Diagnostics.cs`, beside the existing NORSE07x family:

```csharp
	public static readonly DiagnosticDescriptor AllowAnonymousBanned = new(
		"NORSE013",
		"AllowAnonymous is banned",
		"'{0}' adds AllowAnonymous metadata; every request carries a principal, so declare a named policy instead",
		"Norse.Architecture",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: false,
		description:
		"Every request carries a ClaimsPrincipal with a GUID — the anonymous visitor included — so no "
		+ "endpoint needs an anonymity exemption. Routes that must be reachable without a credential "
		+ "(OAuth endpoints, orchestrator probes, cookieless email-token links) declare a named policy, "
		+ "which is greppable and reviewable where an attribute is neither. Governs authored source only; "
		+ "framework-emitted metadata is out of scope.");
```

- [ ] **Step 2: Write the failing tests**

```csharp
namespace Norse.Architecture.Analyzers.Tests;

public sealed class AllowAnonymousAnalyzerTests
{
	[Fact]
	async Task Strikes_the_attribute_on_an_action()
	{
		const string source = """
			using Microsoft.AspNetCore.Authorization;
			using Microsoft.AspNetCore.Mvc;

			public sealed class SampleController : ControllerBase
			{
				[AllowAnonymous]
				public IActionResult Get() => Ok();
			}
			""";

		await Verify.Analyzer(source).ShouldReport("NORSE013");
	}

	[Fact]
	async Task Strikes_the_fluent_call_on_an_endpoint_builder()
	{
		const string source = """
			using Microsoft.AspNetCore.Builder;

			public static class Wireup
			{
				public static void Map(WebApplication app) =>
					app.MapGet("/health", () => "ok").AllowAnonymous();
			}
			""";

		await Verify.Analyzer(source).ShouldReport("NORSE013");
	}

	[Fact]
	async Task Allows_a_named_policy()
	{
		const string source = """
			using Microsoft.AspNetCore.Builder;
			using Microsoft.AspNetCore.Authorization;

			public static class Wireup
			{
				public static void Map(WebApplication app) =>
					app.MapGet("/health", () => "ok").RequireAuthorization("Norse.Probe");
			}
			""";

		await Verify.Analyzer(source).ShouldReportNothing();
	}

	[Fact]
	async Task Strikes_a_custom_attribute_that_implements_the_marker()
	{
		const string source = """
			using Microsoft.AspNetCore.Authorization;
			using Microsoft.AspNetCore.Mvc;

			public sealed class OpenAttribute : System.Attribute, IAllowAnonymous;

			public sealed class SampleController : ControllerBase
			{
				[Open]
				public IActionResult Get() => Ok();
			}
			""";

		await Verify.Analyzer(source).ShouldReport("NORSE013");
	}

	[Fact]
	async Task Ignores_an_unrelated_user_method_that_happens_to_be_named_AllowAnonymous()
	{
		const string source = """
			public sealed class Doorman
			{
				public Doorman AllowAnonymous() => this;
			}

			public static class Wireup
			{
				public static void Open() => new Doorman().AllowAnonymous();
			}
			""";

		await Verify.Analyzer(source).ShouldReportNothing();
	}

	[Fact]
	async Task Ignores_a_user_extension_named_AllowAnonymous_on_an_unrelated_receiver()
	{
		const string source = """
			public static class StringExtensions
			{
				public static string AllowAnonymous(this string value) => value;
			}

			public static class Wireup
			{
				public static string Open() => "x".AllowAnonymous();
			}
			""";

		await Verify.Analyzer(source).ShouldReportNothing();
	}
}
```

`Verify` is the realm's existing analyzer test harness — follow the shape already used by `RealmReferenceAnalyzerTests` in the same project rather than introducing a second harness.

**The two negative facts are the point.** The stated law is *"adds `IAllowAnonymous` metadata"*, and an analyzer that matches on the method name `AllowAnonymous` alone strikes anyone's unrelated method while an attribute check that tests exact `AllowAnonymousAttribute` equality misses a custom attribute implementing the marker. False positives and false negatives, from the same imprecision.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test Svartalfheim/tests/Architecture.Analyzers.Tests -- --filter-class "*.AllowAnonymousAnalyzerTests"`
Expected: FAIL — analyzer does not exist.

- [ ] **Step 4: Write the analyzer**

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Norse.Architecture.Analyzers;

/// <summary>
///     NORSE013 — strikes every authored construct that adds <c>IAllowAnonymous</c> endpoint metadata.
///     Two shapes reach that metadata and both are covered: an attribute implementing
///     <c>IAllowAnonymous</c> (<c>[AllowAnonymous]</c> and any custom one), and the framework's fluent
///     <c>.AllowAnonymous()</c> convention-builder extension.
/// </summary>
/// <remarks>
///     Both halves are matched <b>semantically</b>, not by name. The attribute test is
///     "implements <c>IAllowAnonymous</c>", so a custom marker attribute cannot slip past exact-type
///     equality; the invocation test is "the framework's extension in
///     <c>Microsoft.AspNetCore.Builder</c> constrained to <c>IEndpointConventionBuilder</c>", so a user's
///     own method that happens to be called <c>AllowAnonymous</c> is not convicted for its name. Matching
///     on the name alone produces false positives and exact-attribute-equality produces false negatives —
///     from the same imprecision, in opposite directions.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AllowAnonymousAnalyzer : DiagnosticAnalyzer
{
	const string MarkerMetadataName = "Microsoft.AspNetCore.Authorization.IAllowAnonymous";
	const string BuilderMetadataName = "Microsoft.AspNetCore.Builder.IEndpointConventionBuilder";
	const string ExtensionsNamespace = "Microsoft.AspNetCore.Builder";

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
		[Diagnostics.AllowAnonymousBanned];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(start =>
		{
			var marker = start.Compilation.GetTypeByMetadataName(MarkerMetadataName);
			if (marker is null)
				return;

			var conventionBuilder = start.Compilation.GetTypeByMetadataName(BuilderMetadataName);

			start.RegisterSymbolAction(symbol => InspectAttributes(symbol, marker),
				SymbolKind.NamedType, SymbolKind.Method, SymbolKind.Property);

			if (conventionBuilder is not null)
				start.RegisterOperationAction(operation => InspectInvocation(operation, conventionBuilder),
					OperationKind.Invocation);
		});
	}

	static void InspectAttributes(SymbolAnalysisContext context, INamedTypeSymbol marker)
	{
		foreach (var data in context.Symbol.GetAttributes())
		{
			// "Implements the marker", not "is AllowAnonymousAttribute" -- the law is about the metadata an
			// attribute contributes, and a custom attribute implementing IAllowAnonymous contributes exactly
			// the same thing.
			if (data.AttributeClass is not { } applied
				|| !applied.AllInterfaces.Contains(marker, SymbolEqualityComparer.Default))
				continue;
			if (data.ApplicationSyntaxReference?.GetSyntax() is not { } syntax)
				continue;

			context.ReportDiagnostic(Diagnostic.Create(
				Diagnostics.AllowAnonymousBanned, syntax.GetLocation(), context.Symbol.Name));
		}
	}

	static void InspectInvocation(OperationAnalysisContext context, INamedTypeSymbol conventionBuilder)
	{
		var invocation = (IInvocationOperation)context.Operation;
		var method = invocation.TargetMethod;

		if (method.Name != "AllowAnonymous"
			|| !method.IsExtensionMethod
			|| method.ContainingType?.ContainingNamespace?.ToDisplayString() != ExtensionsNamespace)
			return;

		// The receiver must be a convention builder. A user extension on string, or on their own type, is
		// none of this rule's business no matter what it is called.
		var receiver = method.ReducedFrom?.Parameters.FirstOrDefault()?.Type ?? method.Parameters
			.FirstOrDefault()?.Type;
		if (receiver is null || !SatisfiesConventionBuilder(receiver, conventionBuilder))
			return;

		context.ReportDiagnostic(Diagnostic.Create(
			Diagnostics.AllowAnonymousBanned,
			invocation.Syntax.GetLocation(),
			method.ContainingType!.Name));
	}

	static bool SatisfiesConventionBuilder(ITypeSymbol receiver, INamedTypeSymbol conventionBuilder) =>
		receiver switch
		{
			// The framework declares it as AllowAnonymous<TBuilder>(this TBuilder) where TBuilder :
			// IEndpointConventionBuilder, so at the call site the receiver is a type parameter with that
			// constraint rather than the interface itself.
			ITypeParameterSymbol parameter =>
				parameter.ConstraintTypes.Any(t =>
					SymbolEqualityComparer.Default.Equals(t, conventionBuilder)),
			_ => SymbolEqualityComparer.Default.Equals(receiver, conventionBuilder)
				|| receiver.AllInterfaces.Contains(conventionBuilder, SymbolEqualityComparer.Default)
		};
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Svartalfheim/tests/Architecture.Analyzers.Tests -- --filter-class "*.AllowAnonymousAnalyzerTests"`
Expected: PASS, all three facts.

- [ ] **Step 6: Confirm it is inert**

Run: `dotnet build Svartalfheim/Svartalfheim.slnx`
Expected: zero warnings. `isEnabledByDefault: false` means NORSE013 reports nothing until a realm opts in — Midgard still uses `.AllowAnonymous()` at this point in the train and must keep building.

- [ ] **Step 7: Stage**

```bash
git add Svartalfheim/gen/Architecture.Analyzers/AllowAnonymousAnalyzer.cs
git add Svartalfheim/gen/Architecture.Analyzers/Diagnostics.cs
git add Svartalfheim/tests/Architecture.Analyzers.Tests/AllowAnonymousAnalyzerTests.cs
```

Commit message for the human: `feat(analyzers): add NORSE013 banning authored AllowAnonymous metadata`

---

## SHIP GATE — Svartálfheim

- [ ] `dotnet build Svartalfheim/Svartalfheim.slnx` — zero warnings.
- [ ] `dotnet test Svartalfheim/Svartalfheim.slnx` — green.
- [ ] PR merged to `master`, CI green, version tag pushed, `Norse.Primitives.*` analyzer package live on the feed.
- [ ] Confirm NORSE013 is **not** firing anywhere yet — `dotnet build Midgard/Midgard.slnx` still succeeds with the two `.AllowAnonymous()` calls in place.

---

## Task 5: Midgard — scheme names, anonymous options, and the anonymous handler

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/Authentication/NorseSchemes.cs`
- Create: `Midgard/src/Infrastructure.Web.Server/Authentication/NorseAnonymousOptions.cs`
- Create: `Midgard/src/Infrastructure.Web.Server/Authentication/NorseAnonymousHandler.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Authentication/NorseAnonymousHandlerTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `public static class NorseSchemes` with `Default`, `Browser`, `Anonymous`, `IdentityCookieOnly`, `Machine` (all `const string`); `NorseAnonymousOptions : AuthenticationSchemeOptions`; `NorseAnonymousHandler : AuthenticationHandler<NorseAnonymousOptions>`. Consumed by Tasks 6, 7.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Norse.Infrastructure.Web.Server.Authentication;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

public sealed class NorseAnonymousHandlerTests
{
	[Fact]
	async Task Mints_a_guid_principal_when_no_anonymous_cookie_is_present()
	{
		var harness = AuthenticationHarness.ForAnonymous();

		var result = await harness.AuthenticateAsync();

		result.Succeeded.ShouldBeTrue();
		Guid.TryParse(result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier), out _).ShouldBeTrue();
	}

	[Fact]
	async Task Writes_the_anonymous_cookie_when_it_mints()
	{
		var harness = AuthenticationHarness.ForAnonymous();

		await harness.AuthenticateAsync();

		harness.SetCookies.ShouldContain(header => header.StartsWith("Norse.Anonymous=", StringComparison.Ordinal));
	}

	[Fact]
	async Task Returns_the_existing_principal_without_reminting()
	{
		var harness = AuthenticationHarness.ForAnonymous();
		var first = await harness.AuthenticateAsync();
		var id = first.Principal!.FindFirstValue(ClaimTypes.NameIdentifier);
		harness.ReplayCookies();

		var second = await harness.AuthenticateAsync();

		second.Principal!.FindFirstValue(ClaimTypes.NameIdentifier).ShouldBe(id);
		harness.SetCookies.ShouldBeEmpty();
	}

	[Fact]
	async Task A_tampered_cookie_mints_fresh_rather_than_failing()
	{
		var harness = AuthenticationHarness.ForAnonymous();
		harness.Request.Headers.Cookie = "Norse.Anonymous=not-a-protected-payload";

		var result = await harness.AuthenticateAsync();

		result.Succeeded.ShouldBeTrue();
		harness.SetCookies.ShouldContain(header => header.StartsWith("Norse.Anonymous=", StringComparison.Ordinal));
	}

	[Fact]
	async Task The_principal_carries_the_anonymous_role_and_is_authenticated()
	{
		var harness = AuthenticationHarness.ForAnonymous();

		var result = await harness.AuthenticateAsync();

		result.Principal!.Identity!.IsAuthenticated.ShouldBeTrue();
		result.Principal.IsInRole(NorseAnonymousOptions.AnonymousRole).ShouldBeTrue();
	}

	[Fact]
	async Task A_validly_protected_empty_guid_is_treated_as_absent_and_reminted()
	{
		var harness = AuthenticationHarness.ForAnonymous().WithProtectedAnonymousPayload(Guid.Empty);

		var result = await harness.AuthenticateAsync();

		// Never hands the pipeline a principal the mediator seam is guaranteed to reject: Seed refuses
		// Guid.Empty, so authenticating one here would only defer the failure to a worse place.
		Guid.Parse(result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!).ShouldNotBe(Guid.Empty);
		harness.SetCookies.ShouldContain(h => h.StartsWith("Norse.Anonymous=", StringComparison.Ordinal));
	}
}
```

`AuthenticationHarness` is a test-local fixture created in this task: it builds a `DefaultHttpContext`, a real `IDataProtectionProvider` (`DataProtectionProvider.Create(nameof(NorseAnonymousHandlerTests))`), initializes the handler against a scheme, exposes `SetCookies` (the response's `Set-Cookie` values) and `ReplayCookies()` (copies response cookies onto the next request). Put it beside the tests in `Authentication/AuthenticationHarness.cs`; Tasks 6 and 7 reuse it.

`IsAuthenticated` being true is the spec §2.4 ruling under test — an anonymous principal is identified, so a policy failure is 403, not 401.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests -- --filter-class "*.NorseAnonymousHandlerTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Write the scheme names**

```csharp
namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     The platform's authentication scheme names. Public because Yggdrasil's composition root and
///     Himinbjorg#49's bearer wireup both name them.
/// </summary>
public static class NorseSchemes
{
	/// <summary>The lane selector — the only scheme any policy names by default.</summary>
	public const string Default = "Norse";

	/// <summary>The browser lane's composite: identity cookie, then anonymous, fallback owned internally.</summary>
	public const string Browser = "Norse.Browser";

	/// <summary>The anonymous handler. Never selected directly by a policy; the composite invokes it.</summary>
	public const string Anonymous = "Norse.Anonymous";

	/// <summary>The gRPC lane: identity cookie only, no fallback, no minting.</summary>
	public const string IdentityCookieOnly = "Norse.IdentityCookieOnly";

	/// <summary>
	///     The orchestrator-probe lane. Authenticates nothing and mints nothing — a kubelet is not a
	///     browser. Its own lane rather than a fallthrough into <see cref="Browser" />, because assigning
	///     <c>NorsePolicies.Probe</c> governs authorization and does not stop authentication from running:
	///     without this lane a liveness probe would enter the browser composite and be handed a cookie.
	/// </summary>
	public const string Probe = "Norse.Probe";

	/// <summary>
	///     The machine lane. Until Himinbjorg#49 lands its handler is
	///     <c>NorseMachineRejectionHandler</c>; #49 forwards this name to bearer instead. Registered from
	///     day one either way — forwarding to an unregistered scheme throws a handler-lookup exception
	///     rather than producing a clean 401.
	/// </summary>
	public const string Machine = "Norse.Machine";
}
```

- [ ] **Step 4: Write the options**

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     The anonymous cookie's protocol (design §2.3). <see cref="IsEssential" /> is deliberately true:
///     this cookie carries identity, not tracking, so it sits outside consent gating.
/// </summary>
public sealed class NorseAnonymousOptions : AuthenticationSchemeOptions
{
	/// <summary>The role every anonymous principal carries.</summary>
	public const string AnonymousRole = "anonymous";

	/// <summary>The Data Protection purpose string. Versioned so a format change is a new purpose, not a silent reinterpretation.</summary>
	public const string ProtectionPurpose = "Norse.Anonymous.v1";

	/// <summary>Cookie name — never <c>.AspNetCore.*</c>, matching the identity cookie's de-fingerprinting posture.</summary>
	public string CookieName { get; set; } = "Norse.Anonymous";

	/// <summary>Sliding lifetime, 30 days per the 2026-06-07 auth design §12.</summary>
	public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(30);

	/// <summary>Builds the cookie options used for both writing and deleting — one source, so a delete always matches its write.</summary>
	public CookieOptions BuildCookieOptions(DateTimeOffset now) =>
		new()
		{
			HttpOnly = true,
			Secure = true,
			SameSite = SameSiteMode.Lax,
			Path = "/",
			IsEssential = true,
			Expires = now.Add(Lifetime)
		};
}
```

`BuildCookieOptions` existing as one method is the mechanism behind the spec's delete-must-match-write rule: Task 6's deletion path calls the same builder, so the attributes cannot drift apart.

- [ ] **Step 5: Write the handler**

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     Mints or reads the anonymous identity. Never self-selects: the lane selector (§2.2 layer 1) decides
///     which lane a request is in, and only the browser composite invokes this handler. That is what keeps
///     a facade or gRPC caller from ever being handed a free identity.
/// </summary>
sealed class NorseAnonymousHandler(
	IOptionsMonitor<NorseAnonymousOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	IDataProtectionProvider protection,
	TimeProvider clock)
	: AuthenticationHandler<NorseAnonymousOptions>(options, logger, encoder)
{
	IDataProtector Protector => protection.CreateProtector(NorseAnonymousOptions.ProtectionPurpose);

	protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
		Task.FromResult(AuthenticateResult.Success(ReadOrMint()));

	AuthenticationTicket ReadOrMint()
	{
		if (Request.Cookies.TryGetValue(Options.CookieName, out var payload) && TryUnprotect(payload, out var existing))
			return Ticket(existing);

		var minted = Guid.NewGuid();
		var now = clock.GetUtcNow();
		Response.Cookies.Append(Options.CookieName, Protector.Protect(minted.ToString("D")),
			Options.BuildCookieOptions(now));
		return Ticket(minted);
	}

	bool TryUnprotect(string payload, out Guid id)
	{
		id = Guid.Empty;
		try
		{
			// Guid.Empty is rejected here, not only at PrincipalAccessor.Seed. A protected all-zero payload
			// is well-formed and would authenticate cleanly, then fail at the mediator seam -- an
			// authentication layer must not mint a principal it knows the pipeline will refuse. Treated as
			// absence: fresh mint, overwrite.
			return Guid.TryParse(Protector.Unprotect(payload), out id) && id != Guid.Empty;
		}
		catch (System.Security.Cryptography.CryptographicException)
		{
			// A tampered, truncated, or key-rotated payload is indistinguishable from absence for our
			// purposes: mint fresh and overwrite. Never a failed request -- a hostile cookie must not be
			// able to deny service to the visitor holding it.
			return false;
		}
	}

	AuthenticationTicket Ticket(Guid id)
	{
		ClaimsIdentity identity = new(
			[
				new Claim(ClaimTypes.NameIdentifier, id.ToString("D")),
				new Claim(ClaimTypes.Role, NorseAnonymousOptions.AnonymousRole)
			],
			Scheme.Name);
		return new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
	}
}
```

Passing `Scheme.Name` as the `ClaimsIdentity` authentication type is what makes `IsAuthenticated` true — the §2.4 ruling, expressed in one argument.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests -- --filter-class "*.NorseAnonymousHandlerTests"`
Expected: PASS, all five facts.

- [ ] **Step 7: Stage**

```bash
git add Midgard/src/Infrastructure.Web.Server/Authentication/
git add Midgard/tests/Infrastructure.Web.Server.Tests/Authentication/
```

Commit message for the human: `feat(auth): add the anonymous authentication scheme and its cookie protocol`

---

## Task 6: Midgard — the browser composite

The composite owns credential fallback, because a policy scheme cannot: `ForwardDefaultSelector` resolves one scheme name and a failed `AuthenticateAsync` is a failure, not a retry. This is the correction that took rev 1 of the design off the table.

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/Authentication/NorseBrowserHandler.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Authentication/NorseBrowserHandlerTests.cs`

**Interfaces:**
- Consumes: `NorseSchemes`, `NorseAnonymousOptions` (Task 5).
- Produces: `NorseBrowserHandler : AuthenticationHandler<AuthenticationSchemeOptions>`, registered under `NorseSchemes.Browser`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Norse.Infrastructure.Web.Server.Authentication;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

public sealed class NorseBrowserHandlerTests
{
	[Fact]
	async Task A_valid_identity_cookie_wins_and_mints_nothing()
	{
		var harness = AuthenticationHarness.ForBrowser().WithValidIdentityCookie("user@example.test");

		var result = await harness.AuthenticateAsync();

		result.Principal!.FindFirstValue(ClaimTypes.Name).ShouldBe("user@example.test");
		harness.SetCookies.ShouldNotContain(h => h.StartsWith("Norse.Anonymous=", StringComparison.Ordinal));
	}

	[Fact]
	async Task An_expired_identity_cookie_is_deleted_and_a_fresh_anonymous_is_minted()
	{
		var harness = AuthenticationHarness.ForBrowser().WithExpiredIdentityCookie();

		var result = await harness.AuthenticateAsync();

		result.Principal!.IsInRole(NorseAnonymousOptions.AnonymousRole).ShouldBeTrue();
		harness.DeletedCookies.ShouldContain(harness.IdentityCookieName);
		harness.SetCookies.ShouldContain(h => h.StartsWith("Norse.Anonymous=", StringComparison.Ordinal));
	}

	[Fact]
	async Task The_identity_cookie_delete_matches_the_attributes_it_was_written_with()
	{
		var harness = AuthenticationHarness.ForBrowser().WithExpiredIdentityCookie();

		await harness.AuthenticateAsync();

		var delete = harness.SetCookies.Single(h => h.StartsWith(harness.IdentityCookieName, StringComparison.Ordinal));
		delete.ShouldContain("path=/", Case.Insensitive);
		delete.ShouldContain("secure", Case.Insensitive);
		delete.ShouldContain("samesite=lax", Case.Insensitive);
	}

	[Fact]
	async Task Identity_outranks_anonymous_when_both_cookies_are_present()
	{
		var harness = AuthenticationHarness.ForBrowser()
			.WithValidIdentityCookie("user@example.test")
			.WithAnonymousCookie(Guid.NewGuid());

		var result = await harness.AuthenticateAsync();

		result.Principal!.FindFirstValue(ClaimTypes.Name).ShouldBe("user@example.test");
		result.Principal.IsInRole(NorseAnonymousOptions.AnonymousRole).ShouldBeFalse();
	}

	[Fact]
	async Task A_forged_anonymous_cookie_cannot_add_claims_to_an_authenticated_principal()
	{
		var harness = AuthenticationHarness.ForBrowser().WithValidIdentityCookie("user@example.test");
		harness.Request.Headers.Append("Cookie", "Norse.Anonymous=forged");

		var result = await harness.AuthenticateAsync();

		result.Principal!.Claims.ShouldNotContain(c =>
			c.Type == ClaimTypes.Role && c.Value == NorseAnonymousOptions.AnonymousRole);
	}

	[Fact]
	async Task A_valid_anonymous_cookie_alone_is_returned_without_reminting()
	{
		var id = Guid.NewGuid();
		var harness = AuthenticationHarness.ForBrowser().WithAnonymousCookie(id);

		var result = await harness.AuthenticateAsync();

		result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier).ShouldBe(id.ToString("D"));
		harness.SetCookies.ShouldBeEmpty();
	}

	[Fact]
	async Task Challenge_forwards_to_the_identity_cookies_login_presentation()
	{
		var harness = AuthenticationHarness.ForBrowser();

		await harness.ChallengeAsync();

		harness.Response.StatusCode.ShouldBe(302);
		harness.Response.Headers.Location.ToString().ShouldContain("/Account/Login");
	}

	[Fact]
	async Task Forbid_is_a_bare_403_with_no_redirect_and_no_body()
	{
		var harness = AuthenticationHarness.ForBrowser().WithAnonymousCookie(Guid.NewGuid());

		await harness.ForbidAsync();

		harness.Response.StatusCode.ShouldBe(403);
		harness.Response.Headers.Location.ToString().ShouldBeEmpty();
		harness.Response.ContentLength.ShouldBe(0);
	}

	[Fact]
	async Task Deletion_of_an_insecure_request_cookie_does_not_force_the_secure_flag()
	{
		var harness = AuthenticationHarness.ForBrowser(https: false)
			.WithExpiredIdentityCookie(securePolicy: CookieSecurePolicy.SameAsRequest);

		await harness.AuthenticateAsync();

		var delete = harness.SetCookies.Single(h => h.StartsWith(harness.IdentityCookieName, StringComparison.Ordinal));
		delete.ShouldNotContain("secure", Case.Insensitive);
	}
}
```

The last three are the blocker-3 and cookie-attribute facts. `NorseBrowserHandler` overriding only authentication would leave challenge and forbid on `AuthenticationHandler`'s base behavior, so the identity cookie's `OnRedirectToLogin` would never run for the browser lane — and a hand-rolled `CookieOptions` that maps `SameAsRequest` to `Secure = true` emits a delete a plain-HTTP browser ignores, which is the same "rejected is not removed" failure one level down.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests -- --filter-class "*.NorseBrowserHandlerTests"`
Expected: FAIL — handler does not exist.

- [ ] **Step 3: Write the composite**

```csharp
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     The browser lane's composite. Exactly one authentication <i>result</i> contributes — the handler may
///     internally invoke the identity-cookie or anonymous handler, but two results never merge into one
///     principal. Fallback lives here rather than in the lane selector because a policy scheme cannot
///     supply it: <c>ForwardDefaultSelector</c> resolves one scheme name and a failed
///     <c>AuthenticateAsync</c> stays failed. The selector is therefore endpoint-shaped and result-blind,
///     and everything credential-dependent happens inside this type.
/// </summary>
sealed class NorseBrowserHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	IOptionsMonitor<NorseAnonymousOptions> anonymousOptions,
	IOptionsMonitor<CookieAuthenticationOptions> cookieOptions,
	TimeProvider clock)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		var identityScheme = IdentityConstants.ApplicationScheme;
		var identityCookieName = cookieOptions.Get(identityScheme).Cookie.Name ?? identityScheme;

		if (Request.Cookies.ContainsKey(identityCookieName))
		{
			var identity = await Context.AuthenticateAsync(identityScheme).ConfigureAwait(false);
			if (identity.Succeeded)
				return identity;

			// Present but not valid -- expired, revoked, or key-rotated. Delete it with the same options it
			// was written with: a browser silently ignores a delete whose Path/Domain/Secure/SameSite do not
			// match, so rejecting the cookie and removing it are two different acts and only one of them is
			// what we mean. CookieBuilder.Build(Context) is what produces those options in the first place,
			// so calling it here is what makes "same options" true rather than approximately true -- a
			// hand-rolled copy would map SecurePolicy.SameAsRequest to Secure = true and emit a delete a
			// plain-HTTP browser discards.
			Response.Cookies.Delete(identityCookieName, cookieOptions.Get(identityScheme).Cookie.Build(Context));
		}

		return await Context.AuthenticateAsync(NorseSchemes.Anonymous).ConfigureAwait(false);
	}

	// Challenge and forbid are separate operations from authenticate, and the base handler answers both with
	// a bare status. That is right for forbid and wrong for challenge: the browser lane's challenge is the
	// identity cookie's login presentation, and nothing forwards to it unless this override does.
	protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
		Context.ChallengeAsync(IdentityConstants.ApplicationScheme, properties);

	// Never a redirect. A forbidden caller is already identified -- anonymous principals included, which is
	// the whole point of design §2.4 -- so sending them to a login page would answer "who are you?" to
	// someone who has already told us. Bare 403, no body.
	protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
	{
		Response.StatusCode = StatusCodes.Status403Forbidden;
		Response.ContentLength = 0;
		return Task.CompletedTask;
	}
}
```

The anonymous handler already covers "no anonymous cookie → mint" and "tampered anonymous cookie → mint fresh," so steps 3 and 4 of the design's four-step sequence are one delegation here rather than two branches. That is deliberate: the mint/read decision has exactly one home.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests -- --filter-class "*.NorseBrowserHandlerTests"`
Expected: PASS, all six facts.

- [ ] **Step 5: Stage**

```bash
git add Midgard/src/Infrastructure.Web.Server/Authentication/NorseBrowserHandler.cs
git add Midgard/tests/Infrastructure.Web.Server.Tests/Authentication/NorseBrowserHandlerTests.cs
```

Commit message for the human: `feat(auth): add the browser composite that owns credential fallback`

---

## Task 7: Midgard — the machine rejection handler, the lane selector, and the wireup

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/Authentication/NorseMachineRejectionHandler.cs`
- Create: `Midgard/src/Infrastructure.Web.Server/Authentication/NorseLaneSelector.cs`
- Create: `Midgard/src/Infrastructure.Web.Server/Authentication/AuthenticationBuilderExtensions.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Authentication/NorseLaneSelectorTests.cs`

**Interfaces:**
- Consumes: `NorseSchemes` (Task 5), `NorseBrowserHandler` (Task 6), Task 0's metadata verdict.
- Produces: `public static AuthenticationBuilder AddNorseAuthentication(this IServiceCollection services)` — the single public wireup Yggdrasil calls in Task 14.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.AspNetCore.Http;
using Norse.Infrastructure.Web.Server.Authentication;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

public sealed class NorseLaneSelectorTests
{
	[Fact]
	void A_facade_endpoint_selects_the_machine_lane() =>
		NorseLaneSelector.Select(EndpointFactory.Facade()).ShouldBe(NorseSchemes.Machine);

	[Fact]
	void A_facade_endpoint_selects_the_machine_lane_even_with_cookies_present() =>
		NorseLaneSelector.Select(EndpointFactory.Facade()).ShouldBe(NorseSchemes.Machine);

	[Fact]
	void A_grpc_endpoint_selects_the_identity_cookie_only_lane() =>
		NorseLaneSelector.Select(EndpointFactory.Grpc()).ShouldBe(NorseSchemes.IdentityCookieOnly);

	[Fact]
	void A_probe_endpoint_selects_the_probe_lane() =>
		NorseLaneSelector.Select(EndpointFactory.Probe()).ShouldBe(NorseSchemes.Probe);

	[Fact]
	void A_probe_endpoint_never_falls_through_to_the_browser_lane() =>
		NorseLaneSelector.Select(EndpointFactory.Probe()).ShouldNotBe(NorseSchemes.Browser);

	[Fact]
	void A_razor_endpoint_selects_the_browser_lane() =>
		NorseLaneSelector.Select(EndpointFactory.Razor()).ShouldBe(NorseSchemes.Browser);

	[Fact]
	void An_endpointless_request_selects_the_browser_lane() =>
		NorseLaneSelector.Select(endpoint: null).ShouldBe(NorseSchemes.Browser);
}
```

`EndpointFactory` is test-local: `Facade()` builds an `Endpoint` whose metadata carries a `ControllerActionDescriptor` for a `GrpcControllerBase` descendant, `Grpc()` one carrying the marker Task 0 ruled on, `Probe()` one carrying an `IAuthorizeData` whose `Policy` is `NorsePolicies.Probe`, `Razor()` one carrying none of them.

The probe row matches on the **policy name already declared on the endpoint** rather than on a second marker. That keeps the pattern the rest of the design uses — one declaration, several consumers — so an endpoint cannot end up in the probe lane for authorization while sitting in the browser lane for authentication.

Then the wireup-level facts, which are the ones that pin the rev-1 regression:

```csharp
public sealed class LaneWireupTests
{
	[Fact]
	async Task A_credentialless_grpc_call_mints_nothing_and_writes_no_cookie()
	{
		using var host = await LaneHost.StartAsync();

		var response = await host.Client.PostAsync("/probe.ProbeService/Ping", LaneHost.EmptyGrpcBody());

		response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
	}

	[Fact]
	async Task A_credentialless_facade_call_is_rejected_before_the_action_runs()
	{
		using var host = await LaneHost.StartAsync();

		var response = await host.Client.GetAsync("/api/probe/1");

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
		(await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
		host.FacadeActionInvocations.ShouldBe(0);
		response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
	}

	[Fact]
	async Task A_browser_request_mints_an_anonymous_cookie()
	{
		using var host = await LaneHost.StartAsync();

		var response = await host.Client.GetAsync("/");

		response.Headers.GetValues("Set-Cookie")
			.ShouldContain(h => h.StartsWith("Norse.Anonymous=", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("/alive")]
	[InlineData("/health")]
	async Task A_probe_request_succeeds_and_is_handed_no_cookie(string path)
	{
		using var host = await LaneHost.StartAsync();

		var response = await host.Client.GetAsync(path);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
	}
}
```

The probe facts are the ones that pin blocker 2: before the probe lane existed, a health endpoint was neither facade nor gRPC, fell through to the browser composite, and was handed an anonymous cookie — contradicting the design's own "a kubelet is not a browser" ruling. Assigning `NorsePolicies.Probe` alone does not prevent that; it governs authorization, not whether authentication runs.

`LaneHost` is a `TestServer` fixture mapping three endpoints — a code-first gRPC probe service, a `GrpcControllerBase` probe controller, and a Razor-shaped GET — with `AddNorseAuthentication()` wired. `FacadeActionInvocations` is a counter the probe controller increments, so "rejected before the action runs" is asserted directly rather than inferred from the status code.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests -- --filter-class "*.NorseLaneSelectorTests"`
Expected: FAIL — selector does not exist.

- [ ] **Step 3: Write the rejection handler**

```csharp
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     The machine lane's handler until Himinbjorg#49 lands bearer. Registered rather than left dangling:
///     forwarding to an unregistered scheme name throws a handler-lookup exception, which surfaces as a 500
///     and reads like a server fault instead of the clean 401 a credentialless facade caller must get.
///     Authenticates nothing, challenges silently, never writes a cookie. #49 repoints
///     <see cref="NorseSchemes.Machine" /> at bearer and deletes this type.
/// </summary>
sealed class NorseMachineRejectionHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
		Task.FromResult(AuthenticateResult.NoResult());

	protected override Task HandleChallengeAsync(AuthenticationProperties properties)
	{
		Response.StatusCode = StatusCodes.Status401Unauthorized;
		Response.ContentLength = 0;
		return Task.CompletedTask;
	}

	protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
	{
		Response.StatusCode = StatusCodes.Status403Forbidden;
		Response.ContentLength = 0;
		return Task.CompletedTask;
	}
}
```

Both operations are spelled out. The base class already answers bare, but a machine lane that inherits its transport semantics by accident is one refactor away from inheriting something else.

- [ ] **Step 4: Write the probe handler**

```csharp
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     The orchestrator-probe lane. Authenticates nothing and writes nothing: a liveness probe arrives with
///     no credentials and must not be handed an identity for its trouble. Exists as its own lane because
///     naming <c>NorsePolicies.Probe</c> governs authorization only — it does not stop authentication from
///     running, so without this a probe would fall through to the browser composite and collect a cookie.
/// </summary>
sealed class NorseProbeHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
		Task.FromResult(AuthenticateResult.NoResult());
}
```

`NorsePolicies.Probe`'s always-succeed assertion is what lets a `NoResult` authentication still pass authorization — the two halves are designed together.

- [ ] **Step 5: Write the selector**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Norse.Abstractions.Web.Server.Authorization;
using Norse.Abstractions.Web.Server.Facade;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     Layer 1 of scheme selection (design §2.2): decides a request's lane from <b>endpoint shape only</b>.
///     It reads no cookies, no headers, and invokes no handler, so it is result-blind and cannot recurse.
///     Everything credential-dependent belongs to <see cref="NorseBrowserHandler" />.
/// </summary>
static class NorseLaneSelector
{
	internal static string Select(Endpoint? endpoint)
	{
		if (endpoint is null)
			return NorseSchemes.Browser;

		if (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action
			&& typeof(GrpcControllerBase).IsAssignableFrom(action.ControllerTypeInfo))
			return NorseSchemes.Machine;

		// Task 0's verdict decides this line. If GrpcMethodMetadata is present on code-first endpoints it is
		// the marker; otherwise NorseGrpcLaneMetadata added at MapGrpcService time is. The row's position and
		// behavior do not change either way -- only the type it matches on.
		if (endpoint.Metadata.GetMetadata<Grpc.AspNetCore.Server.GrpcMethodMetadata>() is not null)
			return NorseSchemes.IdentityCookieOnly;

		// Reads the policy name the endpoint already declares rather than a second marker: one declaration,
		// two consumers, so an endpoint cannot be in the probe lane for authorization and the browser lane
		// for authentication. Must precede the browser fallthrough -- that ordering IS the fix for a probe
		// being handed a cookie.
		foreach (var data in endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>())
		{
			if (string.Equals(data.Policy, NorsePolicies.Probe, StringComparison.Ordinal))
				return NorseSchemes.Probe;
		}

		return NorseSchemes.Browser;
	}
}
```

- [ ] **Step 6: Write the wireup**

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>Composition-root wiring for the platform's authentication lanes.</summary>
public static class AuthenticationBuilderExtensions
{
	/// <param name="services">The service collection to configure.</param>
	extension(IServiceCollection services)
	{
		/// <summary>
		///     Registers the lane selector and every lane behind it. Deliberately sets <b>no</b> default
		///     scheme beyond the selector: an endpoint that declares nothing gets no principal rather than
		///     the wrong one, which is §2.7's preference order applied to authentication.
		/// </summary>
		/// <returns>The <see cref="AuthenticationBuilder" /> for further chaining (Himinbjorg#49 adds bearer).</returns>
		public AuthenticationBuilder AddNorseAuthentication() =>
			services
				.AddAuthentication(NorseSchemes.Default)
				.AddPolicyScheme(NorseSchemes.Default, NorseSchemes.Default,
					options => options.ForwardDefaultSelector =
						context => NorseLaneSelector.Select(context.GetEndpoint()))
				.AddScheme<AuthenticationSchemeOptions, NorseBrowserHandler>(NorseSchemes.Browser, null)
				.AddScheme<NorseAnonymousOptions, NorseAnonymousHandler>(NorseSchemes.Anonymous, null)
				.AddPolicyScheme(NorseSchemes.IdentityCookieOnly, NorseSchemes.IdentityCookieOnly,
					options =>
					{
						// Authenticate against the identity cookie -- but never inherit its challenge, which
						// is a 302 to a login page. A gRPC client cannot follow a redirect and must not be
						// sent one; both non-authenticate operations go bare.
						options.ForwardAuthenticate = IdentityConstants.ApplicationScheme;
						options.ForwardChallenge = NorseSchemes.Machine;
						options.ForwardForbid = NorseSchemes.Machine;
					})
				.AddScheme<AuthenticationSchemeOptions, NorseMachineRejectionHandler>(NorseSchemes.Machine, null)
				.AddScheme<AuthenticationSchemeOptions, NorseProbeHandler>(NorseSchemes.Probe, null);
	}
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests -- --filter-class "*.NorseLaneSelectorTests"` then `--filter-class "*.LaneWireupTests"`
Expected: PASS.

- [ ] **Step 8: Stage**

```bash
git add Midgard/src/Infrastructure.Web.Server/Authentication/
git add Midgard/tests/Infrastructure.Web.Server.Tests/Authentication/
```

Commit message for the human: `feat(auth): add the endpoint-shaped lane selector and machine rejection handler`

---

## Task 8: Midgard — the gRPC fold and the trailerless decode contract

**Files:**
- Modify: `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ProblemExtensions.cs`
- Modify: `Midgard/src/Infrastructure.Web.Client/Grpc/RpcExceptionExtensions.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/SilentCategoryTests.cs`
- Test: `Midgard/tests/Infrastructure.Web.Client.Tests/Grpc/TrailerlessDecodeTests.cs`

**Interfaces:**
- Consumes: `TransportDispositions.For` (Task 1).
- Produces: no new public surface; `ToRpcException` omits `ErrorInfo` for silent categories, `DecodeProblem` gains a status-only path.

- [ ] **Step 1: Write the failing server-side tests**

```csharp
using Grpc.Core;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class SilentCategoryTests
{
	static Problem Detailed(ErrorCategory category) =>
		Problem.ModelError(category, "leaked detail");

	[Theory]
	[InlineData(ErrorCategory.Unauthorized)]
	[InlineData(ErrorCategory.InvalidCredentials)]
	void Silent_categories_carry_no_status_details_trailer(ErrorCategory category)
	{
		var exception = Detailed(category).ToRpcException();

		exception.StatusCode.ShouldBe(StatusCode.Unauthenticated);
		exception.Trailers.Get("grpc-status-details-bin").ShouldBeNull();
	}

	[Fact]
	void Two_silent_categories_are_indistinguishable_on_the_wire()
	{
		var unauthorized = Detailed(ErrorCategory.Unauthorized).ToRpcException();
		var invalid = Detailed(ErrorCategory.InvalidCredentials).ToRpcException();

		invalid.StatusCode.ShouldBe(unauthorized.StatusCode);
		invalid.Status.Detail.ShouldBe(unauthorized.Status.Detail);
		invalid.Trailers.Count.ShouldBe(unauthorized.Trailers.Count);
	}

	[Fact]
	void Every_category_maps_to_its_declared_grpc_status()
	{
		foreach (var category in Enum.GetValues<ErrorCategory>().Where(c => c != ErrorCategory.Unspecified))
		{
			var expected = (StatusCode)TransportDispositions.For(category).GrpcStatus;
			Detailed(category).ToRpcException().StatusCode
				.ShouldBe(expected, $"{category} should map to {expected}");
		}
	}
}
```

- [ ] **Step 2: Write the failing client-side tests**

```csharp
using Grpc.Core;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Client.Grpc;

namespace Norse.Infrastructure.Web.Client.Tests.Grpc;

public sealed class TrailerlessDecodeTests
{
	static RpcException Trailerless(StatusCode code) => new(new Status(code, string.Empty), Metadata.Empty);

	[Theory]
	[InlineData(StatusCode.Unauthenticated, ErrorCategory.Unauthorized)]
	[InlineData(StatusCode.PermissionDenied, ErrorCategory.Forbidden)]
	[InlineData(StatusCode.NotFound, ErrorCategory.NotFound)]
	[InlineData(StatusCode.Internal, ErrorCategory.Fault)]
	[InlineData(StatusCode.Unavailable, ErrorCategory.Fault)]
	void A_trailerless_status_decodes_to_its_declared_category(StatusCode code, ErrorCategory expected) =>
		Trailerless(code).DecodeProblem().Category.ShouldBe(expected);

	[Fact]
	void A_malformed_trailer_decodes_as_if_trailerless()
	{
		Metadata trailers = new() { { "grpc-status-details-bin", [0x01, 0x02, 0x03] } };
		RpcException exception = new(new Status(StatusCode.Unauthenticated, string.Empty), trailers);

		Should.NotThrow(() => exception.DecodeProblem()).Category.ShouldBe(ErrorCategory.Unauthorized);
	}

	[Fact]
	void A_trailerless_decode_carries_no_field_errors()
	{
		Trailerless(StatusCode.Unauthenticated).DecodeProblem().Errors.ShouldBeEmpty();
	}
}
```

- [ ] **Step 3: Run both to verify they fail**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests -- --filter-class "*.SilentCategoryTests"`
Run: `dotnet test Midgard/tests/Infrastructure.Web.Client.Tests -- --filter-class "*.TrailerlessDecodeTests"`
Expected: FAIL — details are currently always attached, and `DecodeProblem` has no status-only path.

- [ ] **Step 4: Reproject `ToRpcException`**

Replace the `statusCode` switch with `TransportDispositions.For(problem.Category)`, and gate the trailer:

```csharp
			var disposition = TransportDispositions.For(problem.Category);
			var statusCode = (StatusCode)disposition.GrpcStatus;

			// A silent category answers "who am I? -- unknown", and the platform never explains that answer.
			// No ErrorInfo, no metadata, no detail string: the response is the status and nothing else, which
			// is what makes two silent categories provably indistinguishable rather than merely similar.
			if (!disposition.BodyPermitted)
				return new RpcException(new Status(statusCode, string.Empty));
```

leaving the existing `ErrorInfo`/`Metadata` construction below for the body-permitted categories, unchanged.

- [ ] **Step 5: Add the trailerless path to `DecodeProblem`**

Where the method currently reads `ErrorInfo.Reason`, add the fallback when the trailer is absent or will not parse:

```csharp
	static ErrorCategory FromStatusAlone(StatusCode code) =>
		code switch
		{
			// Deliberately lossy and declared so (design §4.3). Unauthenticated is reached by both
			// Unauthorized and InvalidCredentials; collapsing them here is the silence ruling working, not
			// information lost by accident -- the whole point is that the caller cannot tell which it was.
			StatusCode.Unauthenticated => ErrorCategory.Unauthorized,
			StatusCode.PermissionDenied => ErrorCategory.Forbidden,
			StatusCode.NotFound => ErrorCategory.NotFound,
			StatusCode.InvalidArgument => ErrorCategory.Validation,
			StatusCode.AlreadyExists => ErrorCategory.Conflict,
			_ => ErrorCategory.Fault
		};
```

and wrap the trailer parse so a malformed payload falls through to it rather than throwing.

- [ ] **Step 6: Amend the class doc comment**

`RpcExceptionExtensions`' summary currently reads *"Decodes the `grpc-status-details-bin` trailer's `google.rpc.ErrorInfo.Reason` field authoritatively — never the gRPC status code, which is not injective."* Replace "never" with the declared fallback: the trailer remains authoritative **when present**, and its absence is a deliberate signal rather than a defect. Say why: silent categories omit it by design, so a trailerless `Unauthenticated` decodes to `Unauthorized` and cannot be distinguished from `InvalidCredentials` — on purpose.

- [ ] **Step 7: Audit every `DecodeProblem` consumer**

Run: `grep -rn "DecodeProblem" --include=*.cs .` from the Bifröst root.

For each call site, confirm it behaves correctly when `Problem.Errors` is empty and the category is `Unauthorized`. Record the audited list in the commit message. Heimdall's login surface is handled in Task 12; any other consumer that renders `Problem.Errors` without an empty-check is fixed here.

- [ ] **Step 8: Run both test classes to verify they pass**

Expected: PASS.

- [ ] **Step 9: Stage**

```bash
git add Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ProblemExtensions.cs
git add Midgard/src/Infrastructure.Web.Client/Grpc/RpcExceptionExtensions.cs
git add Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/SilentCategoryTests.cs
git add Midgard/tests/Infrastructure.Web.Client.Tests/Grpc/TrailerlessDecodeTests.cs
```

Commit message for the human: `refactor(grpc): project both edges from TransportDispositions; declare trailerless decode`

---

## Task 9: Midgard — the `PrincipalAccessor` backstop

**Files:**
- Modify: `Midgard/src/Infrastructure.Web.Server/Mediator/PrincipalAccessor.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/PrincipalAccessorTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `Seed` throws `InvalidOperationException` on a principal lacking a GUID `NameIdentifier`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Security.Claims;
using Norse.Infrastructure.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class PrincipalAccessorTests
{
	static ClaimsPrincipal WithId(string? id) =>
		new(new ClaimsIdentity(id is null ? [] : [new Claim(ClaimTypes.NameIdentifier, id)], "test"));

	[Fact]
	void Seeding_a_guid_bearing_principal_succeeds()
	{
		PrincipalAccessorProbe accessor = new();

		Should.NotThrow(() => accessor.Seed(WithId(Guid.NewGuid().ToString("D"))));
	}

	[Fact]
	void Seeding_a_principal_with_no_identifier_throws()
	{
		PrincipalAccessorProbe accessor = new();

		Should.Throw<InvalidOperationException>(() => accessor.Seed(WithId(null)))
			.Message.ShouldContain("GUID");
	}

	[Fact]
	void Seeding_a_principal_whose_identifier_is_not_a_guid_throws()
	{
		PrincipalAccessorProbe accessor = new();

		Should.Throw<InvalidOperationException>(() => accessor.Seed(WithId("not-a-guid")));
	}

	[Fact]
	void Seeding_a_principal_whose_identifier_is_the_empty_guid_throws()
	{
		PrincipalAccessorProbe accessor = new();

		// Guid.Empty parses. It is not an identity, and it is the value most likely to arrive from a
		// default-constructed claim -- so it is stated and rejected rather than left to the reader.
		Should.Throw<InvalidOperationException>(() => accessor.Seed(WithId(Guid.Empty.ToString("D"))));
	}
}
```

`PrincipalAccessorProbe` is a test-local subclass exposing `Seed`, which is `internal` — `InternalsVisibleTo` already grants the `.Tests` assembly access per Glitnir §2.3, so no accessibility change is needed.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests -- --filter-class "*.PrincipalAccessorTests"`
Expected: FAIL — `Seed` currently accepts anything.

- [ ] **Step 3: Add the guard**

```csharp
	internal void Seed(ClaimsPrincipal principal)
	{
		ArgumentNullException.ThrowIfNull(principal);

		// The backstop, not the gate. UseAuthorization() rejects a lane that established no principal long
		// before anything reaches here, so this throw should be unreachable -- which is exactly why it
		// exists. A future lane that forgets to declare its schemes fails loudly at the seam instead of
		// quietly seeding an empty principal and letting a RequireAssertion(_ => true) policy wave it
		// through, which is the hole this whole design closes.
		var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);

		// Guid.Empty parses and is not an identity. Every mint is Guid.NewGuid(), so the all-zero value can
		// only arrive from a default-constructed claim or a bug -- exactly the class of thing a backstop
		// exists to catch, and precisely the value that would look like a valid namespace to the idempotency
		// spine (Midgard#58) while identifying nobody.
		if (!Guid.TryParse(identifier, out var id) || id == Guid.Empty)
		{
			throw new InvalidOperationException(
				"A principal reaching the mediator must carry a GUID identifier. Received "
				+ (identifier is null ? "no identifier claim" : $"'{identifier}'")
				+ ". Every lane establishes a principal before authorization runs -- see "
				+ "Glitnir/docs/Platform/specs/2026-08-21-principal-at-the-door-design.md §2.6.");
		}

		_seeded = principal;
	}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Tests -- --filter-class "*.PrincipalAccessorTests"`
Expected: PASS.

- [ ] **Step 5: Run the realm's full suite**

Run: `dotnet test Midgard/Midgard.slnx`
Expected: green. Existing tests seeding a bare `new ClaimsPrincipal()` will fail — give them a GUID identifier rather than relaxing the guard.

- [ ] **Step 6: Stage**

```bash
git add Midgard/src/Infrastructure.Web.Server/Mediator/PrincipalAccessor.cs
git add Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/PrincipalAccessorTests.cs
```

Commit message for the human: `feat(mediator): refuse a seeded principal without a GUID identifier`

---

## Task 10: Midgard — the policy-registration generator

**Files:**
- Create: `Midgard/gen/Infrastructure.Web.Server.Generator/Policies/PolicyDeclarationDiscovery.cs`
- Create: `Midgard/gen/Infrastructure.Web.Server.Generator/Policies/PolicyRegistrationEmitter.cs`
- Create: `Midgard/gen/Infrastructure.Web.Server.Generator/Policies/PolicyRegistrationGenerator.cs`
- Modify: `Midgard/gen/Infrastructure.Web.Server.Generator/Diagnostics.cs` (add NORSE014)
- Test: `Midgard/tests/Infrastructure.Web.Server.Generator.Tests/Policies/PolicyRegistrationEmitterTests.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Generator.Tests/Policies/PolicyRegistrationGeneratorTests.cs`

**Interfaces:**
- Consumes: `NorsePolicyAttribute` (Task 2).
- Produces: generated `public static IServiceCollection AddNorsePolicies(this IServiceCollection services)` in the consuming assembly's root namespace. Consumed by Yggdrasil in Task 14.

### The discovery contract, stated precisely

**Scope is the consumer compilation's resolved reference set** — `compilation.SourceModule.ReferencedAssemblySymbols` — and nothing beyond it.

That boundary is not laziness, it is correctness. The generated code names each declaring type directly (`global::Norse.AuthN.Services.AuthNPolicies.Public`), so a symbol the consumer cannot resolve a reference to is a symbol the generated code **cannot legally compile against**. An earlier revision walked each referenced assembly's own module references to reach further; that walk could surface a type C has no reference to, and emit a call C then fails to build. Finding more than you can name is worse than finding less.

For SDK-style projects MSBuild flattens the whole NuGet closure into `@(ReferencePath)`, so a contributor two hops away is normally *in* the resolved set and is found. What is genuinely out of scope, and is documented rather than papered over:

- an assembly hidden from consumers by `PrivateAssets="all"`;
- an assembly reached only at runtime.

**A missed contributor is loud, not silent:** its policy is never registered, and the first request naming it throws `InvalidOperationException: The AuthorizationPolicy named 'X' was not found`. Loud at request time rather than at startup is not ideal, and Asgard#57 is the place to add a startup assertion that every `[NorsePolicy]` name a referenced assembly declares actually resolved. Recorded here, not built here.

- [ ] **Step 1: Write the failing emitter tests**

```csharp
using Norse.Infrastructure.Web.Server.Generator.Policies;

namespace Norse.Infrastructure.Web.Server.Generator.Tests.Policies;

public sealed class PolicyRegistrationEmitterTests
{
	static readonly PolicyDeclaration[] Two =
	[
		new("AuthN.Public", "Norse.AuthN.Services.AuthNPolicies", "Public"),
		new("Reference.Public", "Norse.Reference.ReferencePolicies", "Public")
	];

	[Fact]
	void Emits_a_registration_per_declaration()
	{
		var emitted = PolicyRegistrationEmitter.Emit("Norse.Hosting.Web.Server", Two);

		emitted.ShouldContain("""AddPolicy("AuthN.Public", global::Norse.AuthN.Services.AuthNPolicies.Public)""");
		emitted.ShouldContain("""AddPolicy("Reference.Public", global::Norse.Reference.ReferencePolicies.Public)""");
	}

	[Fact]
	void Emits_into_the_consuming_assemblys_namespace() =>
		PolicyRegistrationEmitter.Emit("Norse.Hosting.Web.Server", []).ShouldContain("namespace Norse.Hosting.Web.Server;");

	[Fact]
	void Emits_lf_only_with_no_bom()
	{
		var emitted = PolicyRegistrationEmitter.Emit("Norse.Hosting.Web.Server", Two);

		emitted.ShouldNotContain("\r");
		emitted[0].ShouldNotBe('﻿');
	}

	[Fact]
	void Emits_a_compiling_shape_with_no_declarations() =>
		PolicyRegistrationEmitter.Emit("Norse.Hosting.Web.Server", []).ShouldContain("AddNorsePolicies");

	[Fact]
	void Orders_declarations_deterministically()
	{
		var forward = PolicyRegistrationEmitter.Emit("N", Two);
		var reversed = PolicyRegistrationEmitter.Emit("N", [Two[1], Two[0]]);

		reversed.ShouldBe(forward);
	}

	[Theory]
	[InlineData("""Realm."Quoted".Policy""")]
	[InlineData(@"Realm\Backslash")]
	[InlineData("Realm\nNewline")]
	[InlineData("RealmBell")]
	void Escapes_hostile_policy_names_into_valid_csharp(string name)
	{
		var emitted = PolicyRegistrationEmitter.Emit("N",
			[new PolicyDeclaration(name, "A.B", "Configure")]);

		// Parsed, not pattern-matched: the only question that matters is whether the emitted file compiles.
		SyntaxFactory.ParseCompilationUnit(emitted).GetDiagnostics()
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.ShouldBeEmpty();
	}
}
```

A policy name is authored data reaching an emitter, so it is escaped as data. The last fact parses the output rather than asserting on substrings — a test that only checked for `\"` would pass on output that still failed to compile.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Generator.Tests -- --filter-class "*.PolicyRegistrationEmitterTests"`
Expected: FAIL.

- [ ] **Step 3: Write the discovery**

```csharp
using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Server.Generator.Policies;

/// <summary>One declared policy: its name, the type that declares it, and the method that configures it.</summary>
readonly record struct PolicyDeclaration(string Name, string DeclaringType, string MethodName);

/// <summary>
///     One decorated method that is not a usable declaration, with the reason and where to report it.
///     <paramref name="Location" /> is the attribute's own syntax location when the declaration is source in
///     this compilation, and <see cref="Microsoft.CodeAnalysis.Location.None" /> when it arrived as
///     metadata — a referenced assembly has no syntax to point at.
/// </summary>
readonly record struct InvalidDeclaration(string QualifiedMethod, string Reason, Location Location);

/// <summary>Both halves of discovery: what may be emitted, and what must be reported instead.</summary>
readonly record struct PolicyDiscoveryResult(
	ImmutableArray<PolicyDeclaration> Valid,
	ImmutableArray<InvalidDeclaration> Invalid);

/// <summary>
///     Finds every <c>[NorsePolicy]</c>-decorated method in the compilation and in the assemblies the
///     compiler resolved a reference to. Reads <b>attributes from metadata</b>, never method bodies: a
///     realm's declarations arrive as a published package, and a body does not cross that boundary.
/// </summary>
/// <remarks>
///     Scope is deliberately <c>SourceModule.ReferencedAssemblySymbols</c> and no further. The emitter names
///     each declaring type directly, so discovering a symbol this compilation cannot resolve a reference to
///     would emit code that does not compile. See the task's discovery-contract note.
/// </remarks>
static class PolicyDeclarationDiscovery
{
	const string AttributeMetadataName = "Norse.Abstractions.Web.Server.Authorization.NorsePolicyAttribute";

	internal static PolicyDiscoveryResult Discover(Compilation compilation)
	{
		var attribute = compilation.GetTypeByMetadataName(AttributeMetadataName);
		if (attribute is null)
			return new PolicyDiscoveryResult([], []);

		var found = ImmutableArray.CreateBuilder<PolicyDeclaration>();
		var invalid = ImmutableArray.CreateBuilder<InvalidDeclaration>();

		foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols.Append(compilation.Assembly))
			Walk(assembly.GlobalNamespace, attribute, compilation, found, invalid);

		// Reference order varies by machine; unsorted output would make the generated file differ between
		// agents and break the deterministic-build convention. Invalid entries are sorted too, so diagnostic
		// order is stable across builds.
		return new PolicyDiscoveryResult(
			[.. found.OrderBy(d => d.Name, StringComparer.Ordinal)],
			[.. invalid.OrderBy(d => d.QualifiedMethod, StringComparer.Ordinal)]);
	}

	static void Walk(INamespaceSymbol ns, INamedTypeSymbol attribute, Compilation compilation,
		ImmutableArray<PolicyDeclaration>.Builder found, ImmutableArray<InvalidDeclaration>.Builder invalid)
	{
		foreach (var member in ns.GetMembers())
		{
			switch (member)
			{
				case INamespaceSymbol nested:
					Walk(nested, attribute, compilation, found, invalid);
					break;
				case INamedTypeSymbol type:
					Collect(type, attribute, compilation, found, invalid);
					break;
			}
		}
	}

	static void Collect(INamedTypeSymbol type, INamedTypeSymbol attribute, Compilation compilation,
		ImmutableArray<PolicyDeclaration>.Builder found, ImmutableArray<InvalidDeclaration>.Builder invalid)
	{
		foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
		{
			// The attribute is inspected FIRST, before any filtering. Filtering first would make an
			// attributed private or instance method vanish silently -- a declared policy that never
			// registers and only fails when a request asks for it, which is precisely the failure mode this
			// mechanism exists to eliminate. Anything decorated is either a valid declaration or a build
			// error; there is no third outcome.
			var declaration = method.GetAttributes().FirstOrDefault(a =>
				SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));
			if (declaration is null)
				continue;

			if (Validate(method, type, compilation, declaration) is { } reason)
			{
				// Source declarations belong to Asgard's bundled analyzer, which reports them with a real
				// location in the project that authored them. Reporting here too would double-strike the
				// same mistake in the same build. The halves are disjoint by provenance.
				if (declaration.ApplicationSyntaxReference is not null)
					continue;

				// Metadata only, so there is never syntax to point at. The qualified method name carries the
				// identification instead -- a diagnostic with no location must still say exactly what is
				// wrong and where it lives.
				invalid.Add(new InvalidDeclaration(
					$"{type.ToDisplayString()}.{method.Name}", reason, Location.None));
				continue;
			}

			var name = (string)declaration.ConstructorArguments[0].Value!;
			found.Add(new PolicyDeclaration(name, type.ToDisplayString(), method.Name));
		}

		foreach (var nested in type.GetTypeMembers())
			Collect(nested, attribute, compilation, found, invalid);
	}

	/// <summary>Returns null when the declaration is well-formed, or the human-readable reason it is not.</summary>
	static string? Validate(IMethodSymbol method, INamedTypeSymbol type, Compilation compilation,
		AttributeData declaration)
	{
		if (declaration.ConstructorArguments is not [{ Value: string name }] || string.IsNullOrWhiteSpace(name))
			return "the policy name must be a non-empty constant string";
		if (!method.IsStatic)
			return "the method must be static";
		if (method.DeclaredAccessibility != Accessibility.Public)
			return "the method must be public -- generated registration lives in another assembly";
		if (method.IsGenericMethod || type.IsGenericType)
			return "neither the method nor its containing type may be generic";
		if (!compilation.IsSymbolAccessibleWithin(method, compilation.Assembly))
			return "the method must be accessible from the consuming compilation";
		if (!method.ReturnsVoid)
			return "the method must return void";

		var builder = compilation.GetTypeByMetadataName(
			"Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder");
		return method.Parameters is [{ Type: var parameter }]
			&& builder is not null
			&& SymbolEqualityComparer.Default.Equals(parameter, builder) ?
			null :
			"the method must take exactly one AuthorizationPolicyBuilder parameter";
	}
}
```

`Discover` returns `PolicyDiscoveryResult`. The generator reports NORSE015 for every entry in `Invalid` **before** emitting anything, and emits registrations only from `Valid` — a malformed declaration never reaches generated code, and a build that reports one still produces a file that compiles.

**This half handles metadata declarations only.** Source declarations belong to Asgard's bundled analyzer (Task 2), which reports them with a real location in the project that authored them; this generator skips any invalid declaration carrying a non-null `ApplicationSyntaxReference` so the same mistake is never struck twice in one build.

`AttributeData.ApplicationSyntaxReference` is null for metadata, so a declaration arriving from a referenced assembly has no syntax to point at: it reports `Location.None` plus the fully qualified method name, in the **consuming** build. That path is the backstop — it catches a package built against an Asgard old enough to lack the analyzer, and it is what lets a consumer trust a dependency they did not build.

The `IsSymbolAccessibleWithin` check does double duty: it is blocker 3's second half (never name a symbol the emitter cannot legally reference) *and* a validation reason, so an inaccessible attributed method is reported rather than skipped.

- [ ] **Step 4: Write the emitter**

```csharp
using System.Text;
using Norse.Abstractions.Emit;

namespace Norse.Infrastructure.Web.Server.Generator.Policies;

/// <summary>Emits <c>AddNorsePolicies()</c> — the one call that replaces every hand-written policy lambda.</summary>
static class PolicyRegistrationEmitter
{
	internal static string Emit(string rootNamespace, ImmutableArray<PolicyDeclaration> declarations)
	{
		StringBuilder builder = new();
		builder.AppendCSharp($$"""
			// <auto-generated/>
			#nullable enable
			using Microsoft.Extensions.DependencyInjection;

			namespace {{rootNamespace}};

			/// <summary>Generated policy registration. Every [NorsePolicy] declaration in the reference set, registered once.</summary>
			static class NorsePolicyRegistration
			{
				/// <summary>Registers every discovered authorization policy.</summary>
				public static IServiceCollection AddNorsePolicies(this IServiceCollection services)
				{
					var builder = services.AddAuthorizationBuilder();
			{{Registrations(declarations)}}
					return services;
				}
			}
			""");
		return builder.ToString();
	}

	static string Registrations(ImmutableArray<PolicyDeclaration> declarations) =>
		declarations.Length == 0 ?
			"\t\t// No [NorsePolicy] declarations in this compilation's reference set." :
			string.Join("\n", declarations.Select(d =>
				// SymbolDisplay.FormatLiteral, never manual quoting: a policy name is authored data, and a
				// perfectly valid constant containing a quote, a backslash, or a newline would otherwise
				// emit source that does not parse. Roslyn owns C# literal escaping; we do not reimplement it.
				$"\t\tbuilder.AddPolicy({SymbolDisplay.FormatLiteral(d.Name, quote: true)}, "
				+ $"global::{d.DeclaringType}.{d.MethodName});"));
}
```

Passing the method as a method group is what makes the single-declaration shape work end to end: the name comes from the attribute, the shape comes from the method, and the generator never reproduces either.

- [ ] **Step 5: Write the generator and the duplicate-name diagnostic**

The generator is an `IIncrementalGenerator` combining `CompilationProvider` with the root-namespace analyzer-config value, calling `PolicyDeclarationDiscovery.Discover` then `PolicyRegistrationEmitter.Emit`. Add to `Diagnostics.cs`:

```csharp
	public static readonly DiagnosticDescriptor DuplicatePolicyName = new(
		"NORSE014",
		"Duplicate authorization policy name",
		"Policy '{0}' is declared more than once: {1}",
		"Norse.Mediator",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description:
		"Two declarations of the same policy name would resolve last-write-wins at runtime, making the "
		+ "effective policy depend on reference order. Sibling of NORSE010's duplicate-handler strike: the "
		+ "ambiguity is refused at build time rather than resolved arbitrarily. Reads [NorsePolicy] from "
		+ "metadata, so it sees declarations arriving as packages -- which is how every realm reaches the "
		+ "composition root.");
```

Group the discovered declarations by `Name`; report once per group with more than one member, listing every declaring type in the message. Emit no registration for a duplicated name — a build error must not also produce ambiguous code.

Add NORSE015 for malformed declarations:

```csharp
	public static readonly DiagnosticDescriptor InvalidPolicyDeclaration = new(
		"NORSE015",
		"Invalid [NorsePolicy] declaration",
		"'{0}' is decorated with [NorsePolicy] but {1}",
		"Norse.Mediator",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description:
		"A decorated method is either a valid declaration or a build error -- never silently skipped. "
		+ "Filtering for public/static before reading the attribute would make an attributed private or "
		+ "instance method vanish, producing a policy that is declared in source, absent from registration, "
		+ "and discovered only when a request asks for it. The generator reports every rejection instead.");
```

Report every `Invalid` entry at its carried location before emitting. Together with Asgard's analyzer (Task 2) this is what makes the claim *"a realm cannot declare a policy without the generator seeing it"* true rather than aspirational: the analyzer fails the author's own build, and this half fails the consumer's build for anything that reached it as metadata.

- [ ] **Step 6: Write generator tests, including the compile-the-output fact**

Add `PolicyRegistrationGeneratorTests` using the project's existing `ReferenceAssemblies` harness (the shape `HandlerRegistrationGeneratorTests` uses in Asgard). Four facts:

1. **Direct.** Declaration in A, compilation B references A → B's output registers it.
2. **Two hops, composed as MSBuild composes.** Declaration in **A only**; B references A and declares nothing; compilation **C references both B and A**, which is what `@(ReferencePath)` flattening actually produces → C's output registers A's declaration, **and the generated output is compiled and asserted diagnostic-free.** Compiling the output is the fact that matters: it is what proves the emitter only ever names types C can resolve.
3. **Two hops, unflattened — the guarantee's honest edge.** Same A and B, but C references **B only**. Assert C's output registers **nothing from A** and still **compiles clean**. This pins the narrowed contract rather than pretending it does not exist: the generator does not reach past the resolved reference set, and it never emits a name it cannot resolve.
4. **Duplicate across packages.** Declarations of the same name in A and in B, both metadata-only from C's perspective → **NORSE014 fires in C**, and no registration is emitted for that name. This is what proves the guarantee is not blind in package mode, which is the only mode Yggdrasil uses.

Then one NORSE015 fact per rejection class, each asserting the diagnostic fires rather than the declaration being skipped:

| Malformed declaration | Why it must not be silent |
|---|---|
| attributed **private** method | the case that vanished entirely under the old filter-then-read order |
| attributed **internal** method | same, one accessibility up |
| attributed **instance** method | generated code calls it statically |
| attributed method on an **inaccessible type** | emitter cannot name it |
| **non-`void`** return | wrong shape for a method group |
| **wrong or extra parameters** | ditto |
| **generic** method, and generic containing type | cannot be named without type arguments |
| `[NorsePolicy(null)]` and `[NorsePolicy("")]` | a nameless policy is not a policy |

The private-method fact is the important one: before this change it compiled clean, registered nothing, and failed at the first request naming the policy.

**Each rejection class is tested twice — source and metadata-only** — because the two paths report different locations and only one of them has syntax to point at. The metadata variant compiles the malformed declaration into assembly A, references A from compilation B, and asserts NORSE015 fires **in B** with `Location.None` and the fully qualified method name in the message. Testing only the source path would leave the normal case — a realm's declaration arriving as a package — unexercised.

- [ ] **Step 7: Run to verify all pass**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Generator.Tests`
Expected: PASS.

- [ ] **Step 8: Stage**

```bash
git add Midgard/gen/Infrastructure.Web.Server.Generator/Policies/
git add Midgard/gen/Infrastructure.Web.Server.Generator/Diagnostics.cs
git add Midgard/tests/Infrastructure.Web.Server.Generator.Tests/Policies/
```

Commit message for the human: `feat(generator): discover [NorsePolicy] declarations and emit AddNorsePolicies`

---

## Task 11: Midgard — health endpoints onto the probe policy

**Files:**
- Modify: `Midgard/src/Infrastructure.ServiceDefaults.AspNet/WebApplicationExtensions.cs`
- Test: `Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests/HealthEndpointPolicyTests.cs`

**Interfaces:**
- Consumes: `NorsePolicies.Probe` (Task 2).
- Produces: no new surface; the two `MapHealthChecks` calls carry a named policy instead of `.AllowAnonymous()`.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Norse.Abstractions.Web.Server.Authorization;

namespace Norse.Infrastructure.ServiceDefaults.AspNet.Tests;

public sealed class HealthEndpointPolicyTests
{
	[Fact]
	async Task Health_endpoints_declare_the_probe_policy_and_no_anonymity_exemption()
	{
		using var host = await ProbeHost.StartAsync();
		var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints;

		var health = endpoints
			.Where(e => e.DisplayName?.Contains("Health", StringComparison.Ordinal) == true)
			.ToArray();

		health.Length.ShouldBe(2);
		foreach (var endpoint in health)
		{
			endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
			endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
				.ShouldContain(data => data.Policy == NorsePolicies.Probe);
		}
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests -- --filter-class "*.HealthEndpointPolicyTests"`
Expected: FAIL — `IAllowAnonymous` is present and no policy is declared.

- [ ] **Step 3: Swap the exemption for the policy**

In `MapDefaultEndpoints()`, replace both `.AllowAnonymous()` calls with `.RequireAuthorization(NorsePolicies.Probe)`, and amend the existing doc comment. It currently reads *"both are anonymous, because a probe arrives with no credentials"* — that reasoning stands, but the mechanism changes: the probe lane is now a **named policy that requires nothing**, so the exemption is greppable and reviewable instead of an attribute NORSE013 would strike. Health endpoints never reach the mediator, so §2.6's principal invariant does not cover them; say so in the comment, because the next reader will ask.

- [ ] **Step 4: Run to verify it passes**

Expected: PASS.

- [ ] **Step 5: Verify NORSE013 would now be satisfiable**

Run: `grep -rn "AllowAnonymous" --include=*.cs Midgard/src/`
Expected: no hits in production source. If any remain, they are in scope for this task — the whole point is that Task 16 can turn NORSE013 on without a waiver.

- [ ] **Step 6: Stage**

```bash
git add Midgard/src/Infrastructure.ServiceDefaults.AspNet/WebApplicationExtensions.cs
git add Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests/HealthEndpointPolicyTests.cs
```

Commit message for the human: `refactor(servicedefaults): health probes declare Norse.Probe instead of AllowAnonymous`

---

## SHIP GATE — Midgard

- [ ] `dotnet build Midgard/Midgard.slnx` — zero warnings.
- [ ] `dotnet test Midgard/Midgard.slnx` — green.
- [ ] `grep -rn "AllowAnonymous" --include=*.cs Midgard/src/` returns nothing.
- [ ] PR merged to `master`, CI green, version tag pushed, `Norse.Infrastructure.Web.Server` / `.Web.Client` / `.ServiceDefaults.AspNet` live on the feed.

---

## Task 12: Heimdall — policy contributors and locally-rendered credential failure

Two changes, one realm, one gate. The contributors are **additive** — the hand-written lambdas at Yggdrasil still work until Task 14 deletes them, so there is no interval in which a policy is unregistered.

**Files:**
- Modify: `Heimdall/src/AuthN.Services/AuthNPolicies.cs`
- Modify: `Heimdall/src/AuthN.Services/IdentityPolicies.cs`
- Modify: `Heimdall/src/AuthN.Components/Login.razor`
- Test: `Heimdall/tests/AuthN.Services.Tests/PolicyContributorTests.cs`
- Test: `Heimdall/tests/AuthN.Components.Tests/LoginSilentFailureTests.cs`

**Interfaces:**
- Consumes: `NorsePolicyAttribute` (Task 2); the trailerless decode contract (Task 8).
- Produces: `[NorsePolicy]`-decorated public static configure methods on `AuthNPolicies` and `IdentityPolicies` — discovered from metadata by Task 10's generator.

- [ ] **Step 1: Write the failing declaration tests**

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Norse.Abstractions.Web.Server.Authorization;

namespace Norse.AuthN.Services.Tests;

public sealed class PolicyDeclarationTests
{
	static MethodInfo Declaration(Type owner, string name) =>
		owner.GetMethods(BindingFlags.Public | BindingFlags.Static)
			.Single(m => m.GetCustomAttribute<NorsePolicyAttribute>()?.Name == name);

	static AuthorizationPolicy Build(Type owner, string name)
	{
		AuthorizationPolicyBuilder builder = new();
		Declaration(owner, name).Invoke(null, [builder]);
		return builder.Build();
	}

	[Fact]
	void AuthN_declares_its_public_policy_in_metadata() =>
		Should.NotThrow(() => Declaration(typeof(AuthNPolicies), AuthNPolicies.Public));

	[Fact]
	void The_public_policy_now_requires_a_principal_rather_than_asserting_true() =>
		Build(typeof(AuthNPolicies), AuthNPolicies.Public).Requirements
			.ShouldContain(r => r is DenyAnonymousAuthorizationRequirement);

	[Fact]
	void Identity_declares_both_of_its_policies_in_metadata()
	{
		Should.NotThrow(() => Declaration(typeof(IdentityPolicies), IdentityPolicies.Self));
		Should.NotThrow(() => Declaration(typeof(IdentityPolicies), IdentityPolicies.MaskedDisclosure));
	}

	[Fact]
	void Masked_disclosure_still_requires_the_system_role() =>
		Build(typeof(IdentityPolicies), IdentityPolicies.MaskedDisclosure).Requirements
			.ShouldContain(r => r is RolesAuthorizationRequirement);

	[Fact]
	void Every_declaration_in_this_realm_carries_the_generator_visible_signature()
	{
		foreach (var owner in new[] { typeof(AuthNPolicies), typeof(IdentityPolicies) })
		{
			foreach (var method in owner.GetMethods(BindingFlags.Public | BindingFlags.Static)
				.Where(m => m.GetCustomAttribute<NorsePolicyAttribute>() is not null))
			{
				method.ReturnType.ShouldBe(typeof(void));
				method.GetParameters().Select(p => p.ParameterType)
					.ShouldBe([typeof(AuthorizationPolicyBuilder)]);
			}
		}
	}
}
```

The second fact is the §2.6 consequence under test: `Public` was `RequireAssertion(_ => true)`, which passed an *empty* principal and is precisely how an unauthenticated facade request reached the mediator. It becomes `RequireAuthenticatedUser()` — "any principal, anonymous included," which is what the name always meant.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test Heimdall/tests/AuthN.Services.Tests -- --filter-class "*.PolicyDeclarationTests"`
Expected: FAIL — no declarations exist.

- [ ] **Step 3: Add the declarations to the existing policy classes**

The name constants already live on these classes; the configure methods join them, so name and shape sit in one file per context.

```csharp
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Web.Server.Authorization;

namespace Norse.AuthN.Services;

public static class AuthNPolicies
{
	/// <summary>Satisfied by any principal, the anonymous role included.</summary>
	public const string Public = "AuthN.Public";

	/// <summary>Configures <see cref="Public" />.</summary>
	/// <param name="policy">The builder to configure.</param>
	[NorsePolicy(Public)]
	public static void ConfigurePublic(AuthorizationPolicyBuilder policy) =>
		// "Any principal, anonymous role included" -- which is what Public always meant, and now says. The
		// prior RequireAssertion(_ => true) passed an unauthenticated empty principal too, which is the hole
		// the principal-at-the-door design closes.
		policy.RequireAuthenticatedUser();
}
```

```csharp
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Web.Server.Authorization;

namespace Norse.AuthN.Services;

public static class IdentityPolicies
{
	/// <summary>The disclosure subject reading their own data.</summary>
	public const string Self = "Identity.Self";

	/// <summary>A caller reading someone else's data back masked.</summary>
	public const string MaskedDisclosure = "Identity.MaskedDisclosure";

	/// <summary>The role a masked-disclosure caller must hold.</summary>
	public const string SystemRole = "system";

	/// <summary>Configures <see cref="Self" />.</summary>
	/// <param name="policy">The builder to configure.</param>
	[NorsePolicy(Self)]
	public static void ConfigureSelf(AuthorizationPolicyBuilder policy) =>
		policy.RequireAuthenticatedUser();

	/// <summary>Configures <see cref="MaskedDisclosure" />.</summary>
	/// <param name="policy">The builder to configure.</param>
	[NorsePolicy(MaskedDisclosure)]
	public static void ConfigureMaskedDisclosure(AuthorizationPolicyBuilder policy) =>
		policy.RequireRole(SystemRole);
}
```

Keep whatever members these classes already carry; this task adds the attributed methods and leaves the existing constants untouched.

- [ ] **Step 4: Write the failing login-rendering test**

```csharp
using Norse.Abstractions.Contracts;

namespace Norse.AuthN.Components.Tests;

public sealed class LoginSilentFailureTests : TestContext
{
	[Fact]
	void Renders_a_local_message_when_the_server_sends_a_bodyless_unauthorized()
	{
		// What a silent 401 decodes to on the client: category only, zero errors.
		var outcome = Outcome<NavigationResult>.Err(ErrorCategory.Unauthorized);
		var page = RenderLoginWith(outcome);

		page.Markup.ShouldContain("Invalid email or password.");
	}

	[Fact]
	void The_local_message_is_identical_for_every_silent_category()
	{
		var unauthorized = RenderLoginWith(Outcome<NavigationResult>.Err(ErrorCategory.Unauthorized)).Markup;
		var invalid = RenderLoginWith(Outcome<NavigationResult>.Err(ErrorCategory.InvalidCredentials)).Markup;

		invalid.ShouldBe(unauthorized);
	}
}
```

`RenderLoginWith` is a test-local helper that substitutes `IAuthenticationService` (NSubstitute) to return the given outcome, renders `Login`, submits the form, and returns the rendered fragment. Follow the shape already used by the realm's existing bUnit tests, and register the validator explicitly as Mímir's CLAUDE.md notes bUnit requires.

- [ ] **Step 5: Render the message locally**

`Login.razor` currently surfaces `Outcome<T>.Problem.Errors` through the `ServerValidation` bridge. For a silent category there are no errors, so add the local fallback: when the failed outcome's category is `Unauthorized` or `InvalidCredentials` and `Errors` is empty, display the fixed string `"Invalid email or password."` authored in the component.

This is the point of the whole silence ruling — the message is authored where it is displayed, the server sends nothing, and there is no wire content to leak or to compare.

- [ ] **Step 6: Run both test classes**

Expected: PASS.

- [ ] **Step 7: Confirm the old lambdas still work**

Run: `dotnet build Heimdall/Heimdall.slnx`
Expected: zero warnings. Nothing at Yggdrasil has changed yet; the contributors are additive and inert until Task 14.

- [ ] **Step 8: Stage**

```bash
git add Heimdall/src/AuthN.Services/AuthNPolicies.cs
git add Heimdall/src/AuthN.Services/IdentityPolicies.cs
git add Heimdall/src/AuthN.Components/Login.razor
git add Heimdall/tests/AuthN.Services.Tests/PolicyContributorTests.cs
git add Heimdall/tests/AuthN.Components.Tests/LoginSilentFailureTests.cs
```

Commit message for the human: `feat(authn): declare policies through the contributor hook; render credential failure locally`

---

## Task 13: Mímir — the reference policy contributor

**Files:**
- Create: `Mimir/src/Reference.Web.Server/ReferencePolicyDeclarations.cs`
- Test: `Mimir/tests/Reference.Web.Server.Tests/PolicyDeclarationTests.cs`

**Interfaces:**
- Consumes: `NorsePolicyAttribute` (Task 2).
- Produces: a `[NorsePolicy]`-decorated public static configure method on `ReferencePolicies`.

Mímir's `CountriesController` is **not modified**. Its `[Authorize(Policy = ReferencePolicies.Public)]` stays exactly as written; what changes is what `Public` requires and which lane the endpoint is selected into. The realm stays policy-name-only, as its CLAUDE.md requires, and never learns that its REST surface closed.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Norse.Abstractions.Web.Server.Authorization;

namespace Norse.Reference.Web.Server.Tests;

public sealed class PolicyDeclarationTests
{
	static MethodInfo Declaration(string name) =>
		typeof(ReferencePolicies).GetMethods(BindingFlags.Public | BindingFlags.Static)
			.Single(m => m.GetCustomAttribute<NorsePolicyAttribute>()?.Name == name);

	[Fact]
	void Declares_the_reference_public_policy_in_metadata() =>
		Should.NotThrow(() => Declaration(ReferencePolicies.Public));

	[Fact]
	void The_public_policy_requires_a_principal()
	{
		AuthorizationPolicyBuilder builder = new();
		Declaration(ReferencePolicies.Public).Invoke(null, [builder]);

		builder.Build().Requirements.ShouldContain(r => r is DenyAnonymousAuthorizationRequirement);
	}

	[Fact]
	void The_declaration_carries_the_generator_visible_signature()
	{
		var method = Declaration(ReferencePolicies.Public);

		method.ReturnType.ShouldBe(typeof(void));
		method.GetParameters().Select(p => p.ParameterType).ShouldBe([typeof(AuthorizationPolicyBuilder)]);
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Mimir/tests/Reference.Web.Server.Tests -- --filter-class "*.PolicyDeclarationTests"`
Expected: FAIL.

- [ ] **Step 3: Add the declaration**

`ReferencePolicies` lives in `Reference.Contracts` (thin by law — pure wire shapes and policy names). The configure method needs `AuthorizationPolicyBuilder`, a server-side type, so **the declaration goes on a server-side class in `Reference.Web.Server`** rather than widening the contracts assembly's dependencies. The name constant stays where it is.

```csharp
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Web.Server.Authorization;

namespace Norse.Reference.Web.Server;

/// <summary>
///     Declares the reference surface's authorization policies. Lives server-side because a policy's shape
///     needs <see cref="AuthorizationPolicyBuilder" />, and <c>Reference.Contracts</c> is thin by law — the
///     name constant stays there, the shape lives here, and the generator joins them.
/// </summary>
public static class ReferencePolicyDeclarations
{
	/// <summary>Configures <see cref="ReferencePolicies.Public" />.</summary>
	/// <param name="policy">The builder to configure.</param>
	[NorsePolicy(ReferencePolicies.Public)]
	public static void ConfigurePublic(AuthorizationPolicyBuilder policy) =>
		policy.RequireAuthenticatedUser();
}
```

**Amend the tests above** to look at `ReferencePolicyDeclarations` rather than `ReferencePolicies` — the split is real and the tests should describe it. Heimdall does not face this because `AuthNPolicies` and `IdentityPolicies` already live in `AuthN.Services`, which is server-side.

- [ ] **Step 4: Run to verify it passes, and build the realm**

Run: `dotnet test Mimir/tests/Reference.Web.Server.Tests -- --filter-class "*.PolicyDeclarationTests"`
Run: `dotnet build Mimir/Mimir.slnx`
Expected: PASS, zero warnings.

- [ ] **Step 5: Stage**

```bash
git add Mimir/src/Reference.Web.Server/ReferencePolicyDeclarations.cs
git add Mimir/tests/Reference.Web.Server.Tests/PolicyDeclarationTests.cs
```

Commit message for the human: `feat(reference): declare Reference.Public through the contributor hook`

---

## SHIP GATE — Heimdall and Mímir

Both realms ship independently; neither depends on the other. Clear both before Task 14.

- [ ] `dotnet build` and `dotnet test` green on `Heimdall/Heimdall.slnx` and `Mimir/Mimir.slnx`.
- [ ] Both PRs merged to `master`, CI green, version tags pushed.
- [ ] `Norse.AuthN.Services`, `Norse.AuthN.Components`, and `Norse.Reference.Web.Server` live on the feed.

**Why this gate precedes Yggdrasil:** the contributors must be *discoverable in a published package* before Yggdrasil deletes the hand-written lambdas. Reversing the order would open an interval in which the generator finds no contributors and no policy is registered at all.

---

## Task 14: Yggdrasil — compose the lanes and generate the policies

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Server/Program.cs`
- Test: `Yggdrasil/tests/Hosting.Web.Server.Tests/Authentication/LaneCompositionTests.cs`

**Interfaces:**
- Consumes: `AddNorseAuthentication()` (Task 7), generated `AddNorsePolicies()` (Task 10), every contributor (Tasks 12–13).
- Produces: no new surface.

- [ ] **Step 1: Write the failing composition tests**

```csharp
namespace Norse.Hosting.Web.Server.Tests.Authentication;

public sealed class LaneCompositionTests
{
	[Fact]
	async Task Every_previously_hand_registered_policy_is_still_registered()
	{
		using var host = await WebServerHost.StartAsync();
		var provider = host.Services.GetRequiredService<IAuthorizationPolicyProvider>();

		foreach (var name in new[]
			{
				AuthNPolicies.Public, ReferencePolicies.Public,
				IdentityPolicies.Self, IdentityPolicies.MaskedDisclosure,
				NorsePolicies.Probe
			})
		{
			(await provider.GetPolicyAsync(name)).ShouldNotBeNull($"policy '{name}' was not registered");
		}
	}

	[Fact]
	async Task A_browser_request_to_the_root_receives_an_anonymous_principal_and_cookie()
	{
		using var host = await WebServerHost.StartAsync();

		var response = await host.Client.GetAsync("/");

		response.Headers.GetValues("Set-Cookie")
			.ShouldContain(h => h.StartsWith("Norse.Anonymous=", StringComparison.Ordinal));
	}

	[Fact]
	async Task The_reference_facade_is_closed_to_a_credentialless_caller()
	{
		using var host = await WebServerHost.StartAsync();

		var response = await host.Client.GetAsync("/api/reference/countries/US");

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
		(await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
		response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
	}

	[Fact]
	async Task A_circuit_inherits_the_handshake_identity_and_concurrency_mints_no_second_guid()
	{
		using var host = await WebServerHost.StartAsync();

		// The handshake: an ordinary HTTP page load, which is the only request in a circuit's life that can
		// carry a Set-Cookie -- and therefore the only place a mint can happen.
		var handshake = await host.Client.GetAsync("/");
		var minted = WebServerHost.ReadAnonymousId(handshake);

		await using var circuit = await host.ConnectCircuitAsync(handshake);

		// Twenty concurrent operations against one circuit. If anything mid-circuit could mint, this is where
		// a second GUID appears -- and it must not, because the circuit has no response to write a cookie on.
		var observed = await Task.WhenAll(Enumerable.Range(0, 20)
			.Select(_ => circuit.ReadPrincipalIdAsync()));

		observed.ShouldAllBe(id => id == minted);
		circuit.SetCookieHeadersObserved.ShouldBeEmpty();
	}
}
```

The last fact is spec §7's circuit requirement, and it is testable precisely *because* of the §2.5 ruling: minting happens on the handshake and nowhere else, so "no second GUID under concurrency" is an assertion about a path that should not exist rather than a race to be won. An earlier revision of this plan claimed there was no mid-circuit path to test — that mistook the design's guarantee for the absence of something to verify.

The third fact is the visible behavior change of this whole train: Mímir's REST route was reachable by anyone and now answers a bare 401 until Himinbjorg#49 gives the machine lane a bearer scheme.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests -- --filter-class "*.LaneCompositionTests"`
Expected: FAIL — no lane composition yet, and the facade still answers 200.

- [ ] **Step 3: Replace the composition block**

In `Program.cs`, delete the four-lambda `AddAuthorizationBuilder()` block together with the comment above it, and replace with:

```csharp
// Lane composition (Glitnir Platform/specs/2026-08-21-principal-at-the-door-design.md §2.2). No ambient
// default scheme: the selector reads endpoint shape and forwards to exactly one lane, so an endpoint that
// declares nothing gets no principal rather than the wrong one.
builder.Services.AddNorseAuthentication();

// Every [NorsePolicy] declaration in the resolved reference set, registered once. This call replaces four
// hand-written AddPolicy lambdas -- adding a policy anywhere upstream now needs no edit here, which is the
// point: a fifth hand-rolled registration is what guaranteed the argument would be had again.
builder.Services.AddNorsePolicies();
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests -- --filter-class "*.LaneCompositionTests"`
Expected: PASS.

- [ ] **Step 5: Stage**

```bash
git add Yggdrasil/src/Hosting.Web.Server/Program.cs
git add Yggdrasil/tests/Hosting.Web.Server.Tests/Authentication/LaneCompositionTests.cs
```

Commit message for the human: `feat(hosting): compose the authentication lanes and generate policy registration`

---

## Task 15: Yggdrasil — challenge and forbid carry no body

Fixing the two `Outcome<T>` folds does not control responses the framework generates. This task pins the middleware boundaries at the composed host, and each lane is exercised **separately** because each has its own handler and they fail independently.

This is a **verification task**, not a configuration task — the behavior was implemented where each lane lives (Tasks 6 and 7). If it needs `Program.cs` changes to pass, something is wrong upstream.

**Files:**
- Test: `Yggdrasil/tests/Hosting.Web.Server.Tests/Authentication/ChallengeAndForbidTests.cs`
- Modify (only if step 3 applies): `Midgard/src/Infrastructure.Web.Server/Authentication/` — the owning handler

**Interfaces:**
- Consumes: Task 14's composition; the per-handler challenge/forbid overrides from Tasks 6 and 7.
- Produces: no new surface.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Hosting.Web.Server.Tests.Authentication;

public sealed class ChallengeAndForbidTests
{
	[Fact]
	async Task The_rest_lane_challenges_with_a_bare_401()
	{
		using var host = await WebServerHost.StartAsync();

		var response = await host.Client.GetAsync("/api/reference/countries/US");

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
		(await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
		response.Headers.Location.ShouldBeNull();
	}

	[Fact]
	async Task The_grpc_lane_challenges_without_a_redirect()
	{
		using var host = await WebServerHost.StartAsync();

		var response = await host.Client.PostAsync("/norse.Reference.IReferenceService/GetCountry",
			WebServerHost.EmptyGrpcBody());

		response.Headers.Location.ShouldBeNull();
		(await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
	}

	[Fact]
	async Task An_anonymous_browser_principal_failing_a_policy_gets_403_not_a_login_redirect()
	{
		using var host = await WebServerHost.StartAsync();
		var browser = host.CreateBrowserClient();     // follows no redirects, carries the anonymous cookie

		var response = await browser.GetAsync("/protected-probe"); // requires IdentityPolicies.Self

		response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
		response.Headers.Location.ShouldBeNull();
		(await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
	}
}
```

The third fact is the one most likely to regress silently. §2.4 rules that an anonymous principal is authenticated, so failing a policy is **forbid**, not **challenge** — and the cookie handler's redirect belongs to challenge only. A login redirect on the forbid path would quietly restore 401-shaped behavior at the one boundary nobody watches.

- [ ] **Step 2: Run to verify the composition already satisfies them**

Run: `dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests -- --filter-class "*.ChallengeAndForbidTests"`

**These should PASS on the composition Tasks 6 and 7 already shipped**, and that is the point of running them here: challenge and forbid are per-handler operations, so each lane's behavior was decided where the lane lives —

| Lane | Challenge | Forbid | Owner |
|---|---|---|---|
| Browser | 302 to login (forwarded to the identity cookie) | bare 403 | `NorseBrowserHandler` (Task 6) |
| gRPC | bare 401 | bare 403 | `IdentityCookieOnly`'s forwards (Task 7) |
| REST facade | bare 401 | bare 403 | `NorseMachineRejectionHandler` (Task 7) |

An earlier revision of this plan tried to achieve this by configuring the identity cookie's `OnRedirectToLogin`/`OnRedirectToAccessDenied` events with a lane check inside them. That approach was wrong twice over: `NorseBrowserHandler` overrode only authentication, so those events never fired for the browser lane at all, and putting lane logic inside a cookie-handler event would have created a second place that decides which lane a request is in.

- [ ] **Step 3: If any fact fails, fix the owning handler — not the host**

A failure here means a lane's handler does not own its transport semantics. Fix it in Task 6's or Task 7's type and re-run both realms' suites. Do **not** add lane-sniffing to `Program.cs`; the selector is the one definition of "which lane is this" and it stays that way.

- [ ] **Step 4: Add the anonymous-forbid regression guard at the host level**

The third fact is the one most likely to regress silently, so it is asserted at the composed host as well as at the handler: a future change to cookie options, a `FallbackPolicy`, or an added middleware could reintroduce a login redirect on the forbid path, and none of those would fail Task 6's unit-level fact.

Expected: PASS, all three facts.

- [ ] **Step 5: Stage**

```bash
git add Yggdrasil/tests/Hosting.Web.Server.Tests/Authentication/ChallengeAndForbidTests.cs
```

Commit message for the human: `test(hosting): pin challenge and forbid semantics per lane at the composed host`

---

## Task 16: Yggdrasil — update the fixtures and enable NORSE013

**Files:**
- Modify: `Yggdrasil/tests/Hosting.Web.Server.Tests/Swoop/SwoopHostFixture.cs`
- Modify: `Yggdrasil/tests/Hosting.Web.Server.Tests/CountryLookupE2ETests.cs`
- Modify: `Yggdrasil/Directory.Analyzers.props`

**Interfaces:**
- Consumes: everything above.
- Produces: NORSE013 promoted to error across the realm.

- [ ] **Step 1: Update the swoop fixture**

`SwoopHostFixture` maps `ParityController` (a `GrpcControllerBase` descendant) and exercises it over REST-JSON and REST-XML. Those calls now select the machine lane and answer 401. Give the fixture's REST legs an authenticated machine principal by registering a **test-only** scheme forwarded from `NorseSchemes.Machine`, so the parity assertions continue to test what they were written to test — fold behavior and content negotiation — rather than becoming assertions about authentication.

Do **not** weaken the lane rule to make the fixture pass. The fixture models a caller that has credentials; supply them.

- [ ] **Step 2: Update `CountryLookupE2ETests`**

The gRPC leg is unaffected (identity-cookie lane, and the test already authenticates). The REST leg, if any, needs the same machine principal as step 1. Where a test asserted the facade was reachable anonymously, invert it: that is now the `LaneCompositionTests` expectation and it is deliberate.

- [ ] **Step 3: Run the full realm suite**

Run: `dotnet test Yggdrasil/Yggdrasil.slnx`
Expected: green.

- [ ] **Step 4: Enable NORSE013**

In `Yggdrasil/Directory.Analyzers.props`, promote NORSE013 to error following the pattern the existing NORSE07x entries use.

- [ ] **Step 5: Build and confirm the ban holds with no waivers**

Run: `dotnet build Yggdrasil/Yggdrasil.slnx`
Expected: zero warnings, zero NORSE013 hits. If any fire, fix the call site with a named policy — never a `#pragma` or a `[SuppressMessage]`, both of which NORSE079 convicts anyway.

- [ ] **Step 6: Prove the analyzer is live**

Temporarily add `.AllowAnonymous()` to any endpoint in `Hosting.Web.Server`, build, confirm **NORSE013 fires as an error**, then remove it. Verification only; nothing is staged from this step.

- [ ] **Step 7: Stage**

```bash
git add Yggdrasil/tests/Hosting.Web.Server.Tests/
git add Yggdrasil/Directory.Analyzers.props
```

Commit message for the human: `test(hosting): supply machine credentials to facade fixtures; enable NORSE013`

---

## SHIP GATE — Yggdrasil

- [ ] `dotnet build Yggdrasil/Yggdrasil.slnx` — zero warnings, NORSE013 live.
- [ ] `dotnet test Yggdrasil/Yggdrasil.slnx` — green.
- [ ] `dotnet run --project Bifrost/src/Orchestration.AppHost` — the dashboard comes up, the browser lane mints an anonymous cookie on first contact, and `/api/reference/countries/US` answers a bare 401.
- [ ] PR merged to `master`, CI green, version tag pushed.
- [ ] Bifröst's submodule pointers updated; README.md / CLAUDE.md pair reviewed for anything this train changed (boy-scout law).

---

## Post-Train Amendments

Owed on merge, per spec §9.4. These are documentation, not code, and they close the loop the enforcement ledger opens.

- [ ] `Asgard/src/Abstractions.Contracts/ErrorCategory.cs` — `NotAllowed` amended (done in Task 1; confirm it shipped).
- [ ] `Midgard/src/Infrastructure.Web.Client/Grpc/RpcExceptionExtensions.cs` — class comment amended (done in Task 8; confirm).
- [ ] `Glitnir/docs/Heimdall/specs/2026-07-13-authn-identity-split-design.md` §9.3 — record that the *"so they don't try 10000 times"* quote belongs to Register/`Conflict`, and that `LoginHandler.cs:72` miscited it against the `LockedOut` branch. Also reconcile line 199, which retires `ErrorCategory.InvalidCredentials` while shipped code uses it (already ruled 2026-08-08).
- [ ] `Glitnir/docs/Platform/specs/2026-06-07-auth-design.md` §5.1 — `YGG110` becomes NORSE013.
- [ ] `Asgard/src/Abstractions.Components/ServerValidation/CategoryDisplay.cs` — re-read the `NotAllowed` string ("This operation isn't allowed right now.") for a 403 context.
- [ ] Open a follow-on issue for anonymous-principal rate limiting (spec §9.7): minting on first contact is unbounded, and §8.7's 60/min/IP bootstrap limit is unbuilt.

---

## Self-Review

**Spec coverage.** Every spec section maps to a task:

| Spec | Task |
|---|---|
| §2.1 anonymous scheme | 5 |
| §2.2 layer 1 lane selector | 7 (+ 0 for the metadata verdict) |
| §2.2 layer 2 browser composite | 6 |
| §2.3 cookie protocol | 5 (options), 6 (delete-matches-write) |
| §2.4 `IsAuthenticated` → 403 | 5 (identity type), 6 (forbid override), 15 (host-level guard) |
| §2.5 circuits at handshake | 7 (browser lane covers the page load), 14 (inheritance + concurrency fact) |
| §2.6 invariant + backstop | 9 (backstop), 14 (gate), 2/12–13 (every `Public`-shaped policy requires a principal) |
| §3 policy hook | 2 (attribute + platform declarations), 10 (generator + NORSE014/015), 12–13 (realm declarations), 14 (composition) |
| §4.1–4.2 transport contract | 1 (table), 3 (REST fold), 8 (gRPC fold) |
| §4.3 lossy decode | 8 |
| §4.4 challenge/forbid | 15 |
| §5.1 machine-lane marker | 7 |
| §5.2 NORSE013 + probe lane | 4 (analyzer), 2 (probe policy), 5/7 (probe *lane*), 11 (endpoints adopt it), 16 (enable) |
| §6 sequencing | five SHIP GATE sections |
| §7 test inventory | distributed; every listed fact has a home |
| §9 open items | Post-Train Amendments |

**Gaps found and closed during the first self-review:**

1. `GrpcControllerBase`'s fold was originally scheduled after Asgard's ship gate, which would have forced a second Asgard tag for a same-realm change. Moved into phase 1 as Task 3.
2. Spec §7 asks for a fact that `PrincipalAccessor.Seed` throws on a GUID-less principal — it had no task. Added as Task 9.
3. Spec §7's "discovery reaches a transitively-referenced contributor" had no task. Added as Task 10.

**Blocking defects found at plan review (2026-08-21) and closed in rev 2:**

1. **`NorsePolicies.Anonymous` and `.Probe` were constants nothing registered.** Task 11 named `Probe` and Task 14 asserted it resolved, but no contributor declared either. Added `NorsePlatformPolicies` (Task 2) — and `Probe` must carry an explicit always-succeed assertion, because `AuthorizationPolicy`'s constructor throws on an empty requirement set (verified against `aspnetcore/src/Security/Authorization/Core/src/AuthorizationPolicy.cs`).
2. **Health probes fell through to the browser lane and were handed cookies**, contradicting the design's "a kubelet is not a browser." Naming `NorsePolicies.Probe` governs authorization and does not stop authentication running. Added `NorseSchemes.Probe` + `NorseProbeHandler` and a selector row matching the policy the endpoint already declares (Tasks 5, 7), plus no-`Set-Cookie` facts on both endpoints.
3. **Browser challenge and forbid never reached the identity cookie handler.** `NorseBrowserHandler` overrode only authentication, so the base handler answered both. The old Task 15 configured cookie events with a lane check inside them — wrong twice: those events never fired for the browser lane, and it would have created a second definition of "which lane is this." Challenge/forbid are now per-handler (Tasks 6, 7); Task 15 is a verification task.
4. **"Transitive discovery" was neither implemented nor tested.** `ReferencedAssemblySymbols` is the compiler's reference set, not a closure walk, and the old test proved direct reference only. The walk now follows the reference graph, the claim is stated precisely (including what stays invisible under `PrivateAssets="all"`), and Task 10 tests the genuine C → B → A case.
5. **NORSE014 contradicted the compile-time guarantee it claimed.** Reading policy names from `Contribute` bodies is blind in package mode — the only mode Yggdrasil uses.

**Blocking defects found at the second plan review and closed in rev 3:**

1. **Realm contributors carried no declaration attribute**, so the metadata guarantee held only for the platform's own policies. Superseded by the redesign below — declaration is now inseparable from the method that implements it, so a realm cannot declare a policy without the generator seeing it.
2. **The agreement diagnostic was unreachable at the root.** NORSE015 lived in Midgard's generator while **Asgard declares the platform policies upstream of Midgard**, so the one declaration most needing the check could never receive it without inverting the realm graph — and Heimdall and Mímir would each have needed a generator reference dragged into their contract projects. **Resolved by deletion, not by plumbing:** the contributor interface and the class-level declaration attribute are both gone, replaced by `[NorsePolicy(name)]` on the configure method itself (Task 2). Name and shape are two facets of one declaration, so there is no agreement left to enforce. (NORSE015's *number* is reused in rev 4 for a rule that can actually run — striking malformed declarations — which is a different job in a reachable place.)
3. **The reference-graph walk could discover types the generated code cannot legally name.** Walking `module.ReferencedAssemblySymbols` could surface a symbol the consumer has no resolved reference to, and the emitter names declaring types directly — so the generator would emit code that does not compile. The walk is now bounded to `SourceModule.ReferencedAssemblySymbols` plus an `IsSymbolAccessibleWithin` check, the contract is stated as "the resolved reference set" with `PrivateAssets="all"` named as the hole, and Task 10 tests **both** the flattened two-hop case (registers, and the generated output compiles clean) and the unflattened one (registers nothing from the unreferenced assembly, and still compiles clean).

Also closed: `NorseAnonymousHandler.TryUnprotect` accepted a validly-protected `Guid.Empty` while `PrincipalAccessor.Seed` rejected it — the authentication layer would have minted a principal guaranteed to fail at the mediator seam. It now treats an empty GUID as absence and remints, with a regression fact.
6. **NORSE013 was overbroad and under-specified.** It struck any method named `AllowAnonymous` and matched attributes by exact type while the law is about `IAllowAnonymous` metadata — false positives and false negatives from the same imprecision. Both halves now match semantically, with negative facts for unrelated user methods and a positive fact for a custom marker attribute.

Also closed: the cookie-deletion path built `CookieOptions` by hand and mapped `SecurePolicy.SameAsRequest` to `Secure = true`, emitting a delete a plain-HTTP browser discards — it now calls `CookieBuilder.Build(Context)`, the same builder that wrote the cookie. `PrincipalAccessor.Seed` now rejects `Guid.Empty`, which parses and is not an identity. And the circuit concurrency fact spec §7 requires is in Task 14; the earlier claim that "no mid-circuit path exists to test" mistook the design's guarantee for the absence of anything to verify.

**No remaining known gaps between the enforcement ledger's claims and the plan.** Every "won't compile" row is now backed by a mechanism that works in package mode, which is how the composition root actually consumes every one of these assemblies.

**Type consistency.** `TransportDispositions.For` (Task 1) is consumed by Tasks 3 and 8 under that exact name. `NorsePolicyAttribute` (Task 2) decorates methods of the one signature `void M(AuthorizationPolicyBuilder)` in Tasks 2, 12, and 13, and Task 10's emitter names exactly that shape as a method group. `NorseSchemes` constants (Task 5) are referenced by Tasks 6, 7, 14, 15. `NorseAnonymousOptions.AnonymousRole` and `.BuildCookieOptions` (Task 5) are used by Tasks 5 and 6. `AuthenticationHarness` (Task 5) is reused by Task 6. No name drifts between tasks.


**Blocking defects found at the third plan review and closed in rev 4:**

1. **The approved spec still mandated the deleted contributor architecture.** §3 described `INorsePolicyContributor`, and the plan tells executors to read both documents — so an executor following the spec could have rebuilt exactly what rev 3 deleted. The spec's §3 is rewritten to the `[NorsePolicy]` model with an inline amendment recording why the old shape was unbuildable, and its status block now states that an amendment supersedes the text above it. **A second contradiction of the same class was found while fixing this one and was not in the review:** spec §2.2 still carried the three-row selector table and claimed probe endpoints "never enter this selector's jurisdiction" — the exact belief the previous review demolished. Both are now amended in place.
2. **Invalid `[NorsePolicy]` declarations were silently discarded.** Discovery filtered for public/static/accessible *before* reading the attribute, so an attributed private, internal, or instance method vanished — a policy declared in source, absent from registration, discovered only when a request asked for it. That directly contradicted the claim a realm cannot declare a policy the generator does not see. Attributes are now inspected first and every malformed declaration strikes **NORSE015**, with a fact per rejection class and the private-method case called out as the one that previously compiled clean and did nothing.
3. **Policy names were interpolated into C# without escaping.** A valid constant containing a quote, backslash, or newline emitted source that does not parse. The emitter now uses `SymbolDisplay.FormatLiteral`, and the test parses the emitted output rather than asserting on substrings.

**Blocking defects found at the fourth plan review and closed in rev 5:**

1. **Task 10's discovery snippet did not compile.** `Discover` and `Walk` created and threaded only `found` while `Collect` had been changed to require `invalid` too; `InvalidDeclaration` was referenced but never defined; and there was no result type carrying both halves. Added `InvalidDeclaration` and `PolicyDiscoveryResult`, threaded both builders through every frame, and stated how the generator consumes them — report all `Invalid`, emit only from `Valid`.
2. **NORSE015's reporting location was overstated.** `AttributeData.ApplicationSyntaxReference` is null for a declaration arriving from metadata, and no realm runs Midgard's generator while declaring its own policies — so "lands in the declaring project" was true only for source. Fixed in two stages: the location claim was corrected to `Location.None` plus the fully qualified method name, and then (at Buvy's direction, below) a second enforcement point in Asgard's bundled analyzer restored the property the original claim assumed. Every rejection class is tested twice, source and metadata-only, because only one of those paths has syntax to point at.

Also closed: the spec's explicit registered-schemes list omitted `NorseSchemes.Probe`, contradicting its own amended selector table.

**Scope added at Buvy's direction after the fourth review (rev 5):** NORSE015 gained a second enforcement point in Asgard's bundled analyzer, so a malformed declaration fails in the project that authored it rather than in Yggdrasil's build. The generator half remains as the backstop for packages built against an older Asgard, and the two are disjoint by provenance — source belongs to the analyzer, metadata to the generator. The one accepted duplication is the nine-line validation rule list, which exists in both and is named as a gap in Task 2 step 9 rather than resolved with a shared package.

**Deferred at Buvy's direction (2026-08-21), to the post-ship cleanup wave:**

Both are covered by Midgard's metadata backstop today — they cost lifecycle, not correctness, and both are additive to working code.

- [ ] **Asgard's NORSE015 analyzer: check containing-type accessibility.** A `public static` declaration inside an `internal` class passes the declaring-project analyzer and is caught only at the consumer's build. Walk the containing-type chain requiring external accessibility and non-genericity; add source facts for an internal outer type and a public-nested-in-internal type.
- [ ] **Close the generated-source seam.** The analyzer sets `GeneratedCodeAnalysisFlags.None` and the generator skips non-null `ApplicationSyntaxReference`, so a malformed declaration in generated source is skipped by both. One condition either way. No platform code generates policy declarations today.
- [ ] **Reconcile the two validation rule lists** (Asgard analyzer `Reason`, Midgard generator `Validate`) — kept in sync by hand today, and the two gaps above are exactly where they drifted apart.
