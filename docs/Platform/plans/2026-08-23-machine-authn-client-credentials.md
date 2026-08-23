# Machine Authn — Client Credentials — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (default) or `superpowers:executing-plans` (separate-session fallback, never interchangeable), paired with `superpowers:test-driven-development` on every task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make OpenIddict's `client_credentials` grant real, end to end — a seeded machine client obtains a JWT from Himinbjörg's `/connect/token`, and every `GrpcControllerBase`-derived REST facade controller platform-wide is protected by construction, proven against a real facade endpoint (Mímir's `CountriesController`) in both JSON and XML.

**Architecture:** Six realms plus Bifröst itself. Asgard declares the `Machine` policy (name + shape) and attaches it to `GrpcControllerBase`. Midgard bridges `NorseSchemes.Machine` — a scheme name already reserved by the principal-at-the-door work — to OpenIddict's own fixed validation scheme, and adds the OpenAPI bearer-scheme declaration. Himinbjörg wires OpenIddict's server and validation builders, authors the client-credentials exchange handler that actually mints a token, and seeds one machine client with conditional (not unconditional) secret reconciliation. Bifröst's AppHost generates and persists the signing certificate and client secret as Aspire parameters. Yggdrasil composes everything and proves it live — a real token round-trips through a real facade controller in both content types. Mímir gets a one-line doc correction; its `CountriesController` is unchanged (it's already routable, as an existing test already proves).

**Tech Stack:** .NET 11 preview, C#, OpenIddict 7.6.0 (`.Server.AspNetCore` + `.Validation.AspNetCore`, verified against the exact installed version — not generic docs), ASP.NET Core policy schemes, Aspire 13.5.2 (`ParameterResourceBuilderExtensions.AddParameter` with a custom `ParameterDefault`), xUnit v3 + Shouldly + NSubstitute on Microsoft Testing Platform v2.

**Spec:** `../specs/2026-08-23-machine-authn-client-credentials-design.md` — APPROVED, amended three times during review (token-endpoint pass-through, certificate atomicity, secret reconciliation) and once more during this plan's own file-level research (the Asgard policy declaration cannot pin `NorseSchemes.Machine` — that would cross the Asgard→Midgard dependency wall the wrong way; `NorsePlatformPolicies.Anonymous`/`.Probe` don't pin schemes either, and the lane selector's routing is already the enforcement). The plan argues from the spec; executors read both.

**Execution model — realm-by-realm phases.** Six phases below, one per realm plus Bifröst. **Fork discipline (Bifröst `CLAUDE.md` §7):** one feature fork per realm, branch `feature/machine-authn`, checked out fresh from each realm's current `master` (all five touched realms — Himinbjörg, Asgard, Midgard, Yggdrasil, Mímir — are confirmed on `master`, synced with `origin/master`, clean, with no other fork open, as of 2026-08-23). Bifröst's own AppHost changes land directly on `master` per that section's narrow exception (Bifröst's own tracked files, every other realm submodule on `master`). **Gate between phases:** the realm's PR merged and CI green — not a full tag-and-publish cycle. Inside Bifröst, `NorseRef` resolves cross-realm references as local project references during development, so a later phase's build and tests see an earlier phase's uncommitted-but-saved changes immediately; the full publish cycle is a release-process concern for after this plan's phases all land, not a blocker between them.

## Global Constraints

- Target framework: whatever each realm's own `global.json`/`Directory.Build.props` already pins (`net11.0`/`11.0.100-` preview, verified per-realm in the CLAUDE.md files read during planning) — no task changes a TFM.
- **`internal`/default accessibility unless a cross-assembly caller is named below.** `sealed` on every new class unless a concrete derived type exists in the same task.
- **`var` for return assignments only.** Construction uses target-typed `new()`.
- Tabs, 4-space width. US English spelling everywhere.
- **Warnings are errors in every touched realm — a single warning fails the build**, including IDE0055 formatting (Himinbjörg specifically).
- **Test classes are `public sealed`; test methods omit the accessibility modifier.** Names are sentence-shaped with underscores. Shouldly + NSubstitute + Xunit are global usings from each realm's `Directory.Test.props` — never re-add them per file.
- **VSTest `--filter` does not work anywhere on this platform.** Use `dotnet test tests/<Project> -- --filter-class "*.<ClassName>"`.
- **No automatic git commits.** Stage (`git add`), show the diff, stop — the human commits. Every "commit" step below means "stage and hand over."
- No force-push, no `--no-verify`, no committed secrets.
- **Never `dotnet test` a project containing zero tests** — xUnit v3 fails the run.
- Package version pins follow each realm's existing convention — `Version="7.*"` for new OpenIddict packages (matching the already-pinned `OpenIddict.EntityFrameworkCore Version="7.*"` in Himinbjörg), `Version="13.*"` for anything new in Bifröst's AppHost (matching `Aspire.Hosting.PostgreSQL Version="13.*"`).
- **Verified, not assumed:** every OpenIddict and Aspire API referenced below was checked against the actual installed package version (OpenIddict 7.6.0, Aspire 13.5.2) via their XML docs during planning — not carried from generic documentation. Where a task references a specific method name, it exists in that exact form in the installed version.

---

## File Map

### Asgard (`Norse.Abstractions.*`)

| File | Responsibility |
|---|---|
| `src/Abstractions.Components/Authorization/NorsePolicies.cs` | **Modify.** Add `Machine = "Norse.Machine"`; update the class doc comment (currently says Machine is "deliberately absent"). |
| `src/Abstractions.Components/Authorization/NorsePlatformPolicies.cs` | **Modify.** Add the `[NorsePolicy(NorsePolicies.Machine)] Machine(AuthorizationPolicyBuilder)` declaration, matching `Anonymous`/`Probe` exactly — no scheme pin. |
| `src/Abstractions.Web.Server/Facade/GrpcControllerBase.cs` | **Modify.** Add `[Authorize(Policy = NorsePolicies.Machine)]` at the class level; add the `Microsoft.AspNetCore.Authorization` and `Norse.Abstractions.Components.Authorization` usings. |
| `src/Abstractions.Web.Server/Abstractions.Web.Server.csproj` | **Modify.** Add `<ProjectReference Include="../Abstractions.Components/Abstractions.Components.csproj" />` — confirmed absent today (this project currently references only `Abstractions.Backend` and its own generator); `NorsePolicies` lives in `Abstractions.Components`. |
| `tests/Abstractions.Components.Tests/Authorization/NorsePolicyDeclarationTests.cs` | **Modify.** Extend the existing namespacing/metadata/signature facts to include `Machine`; add `The_machine_policy_requires_a_principal` and `The_machine_policy_does_not_pin_a_scheme`. |
| `tests/Abstractions.Web.Server.Tests/Facade/GrpcControllerBaseTests.cs` | **Modify.** Add `The_class_requires_the_Machine_policy`, mirroring the existing `The_class_carries_the_1_MiB_request_size_cap_per_spec_8_4` reflection idiom. |

### Midgard (`Norse.Infrastructure.*`)

| File | Responsibility |
|---|---|
| `src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj` | **Modify.** Add `<PackageReference Include="OpenIddict.Validation.AspNetCore" Version="7.*" />`. |
| `src/Infrastructure.Web.Server/Authentication/AuthenticationBuilderExtensions.cs` | **Modify.** Replace the `NorseMachineRejectionHandler` scheme registration with an `AddPolicyScheme` forward to `OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme`. |
| `src/Infrastructure.Web.Server/Authentication/NorseMachineRejectionHandler.cs` | **Delete.** Its own doc comment names this exact story as its retirement trigger. |
| `src/Infrastructure.Web.Server/Authentication/NorseSchemes.cs` | **Modify.** Update the `Machine` doc comment — no longer "until #49," now permanently the forward target. |
| `src/Infrastructure.Web.Server/OpenApi/BearerSecuritySchemeTransformer.cs` | **Create.** `IOpenApiDocumentTransformer` registering the reusable bearer `OpenApiSecurityScheme` component once. |
| `src/Infrastructure.Web.Server/OpenApi/MachineAuthOperationTransformer.cs` | **Create.** `IOpenApiOperationTransformer` attaching the security requirement only to operations whose declaring controller derives from `GrpcControllerBase`. |
| `tests/Infrastructure.Web.Server.Tests/Authentication/AuthenticationBuilderExtensionsTests.cs` | **Modify.** Add `The_machine_scheme_forwards_to_OpenIddicts_validation_scheme`. |
| `tests/Infrastructure.Web.Server.Tests/OpenApi/MachineAuthTransformerTests.cs` | **Create.** New file (not appended to the already-648-line `TransformerTests.cs`) proving the security requirement lands on a facade operation and not on a plain one, against a real generated document. |

### Himinbjörg (`Norse.Identity.*`)

| File | Responsibility |
|---|---|
| `src/Identity.Web.Server/Identity.Web.Server.csproj` | **Modify.** Add `OpenIddict.Server.AspNetCore` and `OpenIddict.Validation.AspNetCore`, both `Version="7.*"`. |
| `src/Identity.Web.Server/IdentityBuilderExtensions.cs` | **Modify.** `AddNorseIdentity` gains an `X509Certificate2 signingCertificate` parameter; chains `.AddServer(...)`/`.AddValidation(...)` off the `OpenIddictBuilder` `AddNorseOpenIddictCore()` already returns (currently discarded). |
| `src/Identity.Web.Server/ServiceCollectionExtensions.cs` | **Modify.** `AddNorseAuthenticationService` gains the same `X509Certificate2 signingCertificate` parameter, threaded to `AddNorseIdentity`. |
| `src/Identity.Web.Server/OpenIddictExchangeEndpoint.cs` | **Create.** The client-credentials exchange handler — reads the validated `OpenIddictRequest`, builds the principal, signs in. |
| `src/Identity.Web.Server/OpenIddictEndpointRouteBuilderExtensions.cs` | **Create.** `MapNorseOpenIddictEndpoints()`, mirroring `IdentityComponentsEndpointRouteBuilderExtensions`'s shape — maps `/connect/token`. |
| `src/Identity.Migrations/MachineClientSeedContributor.cs` | **Create.** `ISeedContributor` — full application descriptor, conditional reconciliation (validate-then-replace, never unconditional rehash). |
| `tests/Identity.Web.Server.Tests/OpenIddictTokenEndpointTests.cs` | **Create.** Testcontainers-Postgres-backed real-host test: a client_credentials round trip against a real `/connect/token`, decoding the returned JWT's `sub`/`aud` claims. |
| `tests/Identity.Migrations.Tests/MachineClientSeedContributorTests.cs` | **Create.** Testcontainers-Postgres-backed (the project's existing `PostgresContainerFixture`, not a new one): create, no-op-on-unchanged (proven via an unchanged `ConcurrencyToken`), rotate-on-changed. |

### Bifröst (`Norse.Orchestration.AppHost`)

| File | Responsibility |
|---|---|
| `src/Orchestration.AppHost/OidcSigningCertificateParameterDefault.cs` | **Create.** `ParameterDefault` subclass generating a self-signed dev cert, exported to an empty-password PFX, base64-encoded. |
| `src/Orchestration.AppHost/AppHost.cs` | **Modify.** Two new `AddParameter(..., secret: true, persist: true)` resources (`oidc-signing-cert-pfx`, `oidc-machine-client-secret`) and `.WithEnvironment(...)` wiring onto the `web`/`migrations` project resources. |
| `src/Orchestration.AppHost/Orchestration.AppHost.csproj` | **Modify.** Add `<InternalsVisibleTo Include="Orchestration.AppHost.Tests" />` so the new test project can construct `OidcSigningCertificateParameterDefault` (`internal`) directly. |
| `tests/Orchestration.AppHost.Tests/Orchestration.AppHost.Tests.csproj` | **Create.** New test project — Bifröst has none today; self-contained (xUnit v3 + Shouldly), no shared `Directory.Test.props` invented for a single project. |
| `tests/Orchestration.AppHost.Tests/OidcSigningCertificateParameterDefaultTests.cs` | **Create.** Proves `GetDefaultValue()` returns a valid, loadable, empty-password PFX. |

### Yggdrasil (`Norse.Hosting.*`)

| File | Responsibility |
|---|---|
| `src/Hosting.Web.Server/Program.cs` | **Modify.** Decode `OIDC_SIGNING_CERT_PFX` into an `X509Certificate2`, thread it into `AddNorseAuthenticationService`; map `MapNorseOpenIddictEndpoints()`; add the two Midgard OpenAPI transformers to the `AddOpenApi(...)` block. |
| `tests/Hosting.Web.Server.Tests/TestHostEnvironment.cs` | **Modify.** Add `OIDC_SIGNING_CERT_PFX` alongside the existing connection-string fakes — required so every existing test booting the real `Program.cs` keeps passing. |
| `tests/Hosting.Web.Server.Tests/Authentication/MachineAuthTestCertificate.cs` | **Create.** `Base64Pfx` (for `TestHostEnvironment`) and `CreateFresh()` (an `X509Certificate2`, for `MachineAuthPostgresFixture`). |
| `tests/Hosting.Web.Server.Tests/CompositionTests.cs` | **Modify.** Comment-only fix in `A_credentialless_call_to_the_facade_is_rejected_before_content_negotiation_runs` (the "currently red for an unrelated reason" caveat no longer applies) — no behavior change, no new test. |
| `tests/Hosting.Web.Server.Tests/Authentication/MachineAuthPostgresFixture.cs` | **Create.** Two real Postgres containers (identity + reference), migrated/seeded through the real contributors, backing a bespoke `WebApplication`/`TestServer` host wired from the same production DI extensions `Program.cs` calls — mirrors `CountryLookupE2ETests`' established pattern, extended to cover identity/OpenIddict, since `Program.cs`'s pre-`Build()` config reads make `WebApplicationFactory<Program>` unusable for a real database. |
| `tests/Hosting.Web.Server.Tests/Authentication/MachineAuthE2ETests.cs` | **Create.** The positive-path JSON+XML round trip (body-parsed and field-asserted, not just status/Content-Type) and the invalid-token/expired-token negative paths, against the real fixture above. Reconciliation/rotation is not re-tested here — that's Himinbjörg's `MachineClientSeedContributorTests` (Phase 3 Task 6), the actual seed contributor, actually invoked. |

### Mímir (`Norse.Reference.*`)

| File | Responsibility |
|---|---|
| `CLAUDE.md` | **Modify.** Line 37 — the flags-enum/XML gap it describes closed in Midgard commit `aba802f3` (2026-08-09); replace with the accurate current state. |

---

## Phase 1 — Asgard

**Fork:** `feature/machine-authn`, from current `master` (`221d75d`).

### Task 1: Declare `NorsePolicies.Machine`

**Files:**
- Modify: `src/Abstractions.Components/Authorization/NorsePolicies.cs`
- Modify: `src/Abstractions.Components/Authorization/NorsePlatformPolicies.cs`
- Test: `tests/Abstractions.Components.Tests/Authorization/NorsePolicyDeclarationTests.cs`

**Interfaces:**
- Produces: `NorsePolicies.Machine` (`const string`, value `"Norse.Machine"`); `NorsePlatformPolicies.Machine(AuthorizationPolicyBuilder)`, decorated `[NorsePolicy(NorsePolicies.Machine)]`.

- [ ] **Step 1: Write the failing tests**

Extend `tests/Abstractions.Components.Tests/Authorization/NorsePolicyDeclarationTests.cs`:

```csharp
[Fact]
void The_platform_standard_names_are_namespaced_to_Norse()
{
	NorsePolicies.Anonymous.ShouldBe("Norse.Anonymous");
	NorsePolicies.Probe.ShouldBe("Norse.Probe");
	NorsePolicies.Machine.ShouldBe("Norse.Machine");
}

[Fact]
void All_three_platform_policies_are_declared_in_metadata()
{
	var declared = typeof(NorsePlatformPolicies)
		.GetMethods(BindingFlags.Public | BindingFlags.Static)
		.Select(m => m.GetCustomAttribute<NorsePolicyAttribute>()?.Name)
		.Where(name => name is not null)
		.ToArray();

	declared.ShouldBe([NorsePolicies.Anonymous, NorsePolicies.Probe, NorsePolicies.Machine], ignoreOrder: true);
}

[Fact]
void The_machine_policy_requires_a_principal() =>
	Build(NorsePolicies.Machine).Requirements
		.ShouldContain(r => r is DenyAnonymousAuthorizationRequirement);

[Fact]
void The_machine_policy_does_not_pin_a_scheme()
{
	// Asgard cannot reference NorseSchemes.Machine (Midgard) -- the dependency wall runs one direction.
	// Scheme selection is entirely NorseLaneSelector's job (Midgard); this policy only checks the
	// principal, exactly like Anonymous and Probe, neither of which pins a scheme either.
	Build(NorsePolicies.Machine).AuthenticationSchemes.ShouldBeEmpty();
}
```

Rename the existing `Both_platform_policies_are_declared_in_metadata` fact to `All_three_platform_policies_are_declared_in_metadata` (above) rather than leaving a stale two-policy assertion beside a three-policy one.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Abstractions.Components.Tests -- --filter-class "*.NorsePolicyDeclarationTests"`
Expected: FAIL — `NorsePolicies.Machine` does not exist (compile error) until Step 3 lands.

- [ ] **Step 3: Implement**

`src/Abstractions.Components/Authorization/NorsePolicies.cs` — replace the doc comment and add the constant:

```csharp
namespace Norse.Abstractions.Components.Authorization;

/// <summary>
///     Platform-standard policy names — the seed of Asgard#57's standard set. Realm-specific names stay in
///     their own <c>{Context}Policies</c> classes; only names every realm can rely on live here.
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
	///     <i>authentication</i> lane keeps them out of the browser composite.
	/// </summary>
	public const string Probe = "Norse.Probe";

	/// <summary>
	///     Every <see cref="Norse.Abstractions.Web.Server.Facade.GrpcControllerBase" />-derived REST facade
	///     controller, platform-wide, by construction (class-level <c>[Authorize]</c>). Satisfied only by
	///     a bearer JWT, never the browser cookie — Midgard's <c>NorseLaneSelector</c> forwards every
	///     facade endpoint to <c>NorseSchemes.Machine</c> structurally, which is what actually keeps a
	///     cookie principal out; this policy only checks that a principal exists, matching
	///     <see cref="Anonymous" />/<see cref="Probe" /> exactly.
	/// </summary>
	public const string Machine = "Norse.Machine";
}
```

`src/Abstractions.Components/Authorization/NorsePlatformPolicies.cs` — add the declaration:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace Norse.Abstractions.Components.Authorization;

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
		policy.RequireAssertion(_ => true);

	/// <summary>Every REST facade controller. Scheme routing is Midgard's lane selector's job, not this policy's.</summary>
	/// <param name="policy">The builder to configure.</param>
	[NorsePolicy(NorsePolicies.Machine)]
	public static void Machine(AuthorizationPolicyBuilder policy) =>
		policy.RequireAuthenticatedUser();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Abstractions.Components.Tests -- --filter-class "*.NorsePolicyDeclarationTests"`
Expected: PASS

- [ ] **Step 5: Build the whole realm to catch downstream breaks**

Run: `dotnet build Asgard.slnx`
Expected: SUCCESS, zero warnings.

- [ ] **Step 6: Stage (no commit — human commits)**

```bash
git add src/Abstractions.Components/Authorization/NorsePolicies.cs \
  src/Abstractions.Components/Authorization/NorsePlatformPolicies.cs \
  tests/Abstractions.Components.Tests/Authorization/NorsePolicyDeclarationTests.cs
```

### Task 2: Attach `[Authorize(Policy = NorsePolicies.Machine)]` to `GrpcControllerBase`

**Files:**
- Modify: `src/Abstractions.Web.Server/Facade/GrpcControllerBase.cs`
- Modify: `src/Abstractions.Web.Server/Abstractions.Web.Server.csproj`
- Test: `tests/Abstractions.Web.Server.Tests/Facade/GrpcControllerBaseTests.cs`

**Interfaces:**
- Consumes: `NorsePolicies.Machine` (Task 1).
- Produces: every `GrpcControllerBase` descendant now carries the `Machine` policy requirement by inheritance.

**Confirmed during planning:** `Abstractions.Web.Server.csproj` does not reference `Abstractions.Components` today — it references only `Abstractions.Backend` and its own bundled generator. This is a required, not conditional, change.

- [ ] **Step 1: Write the failing test**

Add to `tests/Abstractions.Web.Server.Tests/Facade/GrpcControllerBaseTests.cs`, alongside the existing `The_class_carries_the_1_MiB_request_size_cap_per_spec_8_4` fact:

```csharp
[Fact]
void The_class_requires_the_Machine_policy()
{
	var attribute = typeof(GrpcControllerBase)
		.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
		.Cast<AuthorizeAttribute>()
		.SingleOrDefault();

	attribute.ShouldNotBeNull();
	attribute.Policy.ShouldBe(NorsePolicies.Machine);
}
```

Add `using Microsoft.AspNetCore.Authorization;` and `using Norse.Abstractions.Components.Authorization;` to the test file's using block if not already present (`Microsoft.AspNetCore.Authorization` is not currently imported there).

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Abstractions.Web.Server.Tests -- --filter-class "*.GrpcControllerBaseTests"`
Expected: FAIL — `attribute` is null (no `[Authorize]` on the class yet).

- [ ] **Step 3: Implement**

`src/Abstractions.Web.Server/Abstractions.Web.Server.csproj` — add the new project reference (confirmed absent today) to the existing `<ItemGroup>`, alongside `Abstractions.Backend`:

```xml
<ProjectReference Include="../Abstractions.Components/Abstractions.Components.csproj" />
```

`src/Abstractions.Web.Server/Facade/GrpcControllerBase.cs` — add two usings and the class-level attribute:

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Norse.Abstractions.Components.Authorization;
using Norse.Abstractions.Contracts;

namespace Norse.Abstractions.Web.Server.Facade;

/// <summary>
///     ... (existing doc comment, unchanged) ...
/// </summary>
[ApiController]
[Authorize(Policy = NorsePolicies.Machine)]
// Deliberately no class-level [Consumes] or [Produces] ...
[RequestSizeLimit(1_048_576)] // spec §8.4 — the 1 MiB body cap is declared at the facade, not host config: a formatter (Task 9) cannot enforce body size on its own.
public abstract class GrpcControllerBase : ControllerBase
{
	// ... unchanged body ...
}
```

(Only the using list and the one new attribute line change; the rest of the file — `FoldAsync`, `ToResult`, the existing doc comment — is untouched.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Abstractions.Web.Server.Tests -- --filter-class "*.GrpcControllerBaseTests"`
Expected: PASS

- [ ] **Step 5: Run the whole realm's test suite**

Run: `dotnet build Asgard.slnx && dotnet test Asgard.slnx`
Expected: SUCCESS.

- [ ] **Step 6: Stage (no commit — human commits)**

```bash
git add src/Abstractions.Web.Server/Abstractions.Web.Server.csproj \
  src/Abstractions.Web.Server/Facade/GrpcControllerBase.cs \
  tests/Abstractions.Web.Server.Tests/Facade/GrpcControllerBaseTests.cs
```

## SHIP GATE — Asgard

PR opened from `feature/machine-authn`, CI green, merged to `master`.

---

## Phase 2 — Midgard

**Fork:** `feature/machine-authn`, from current `master` (`c4ad80f` — includes the anonymous-cookie fix that landed 2026-08-23).

### Task 3: Bridge `NorseSchemes.Machine` to OpenIddict's validation scheme

**Files:**
- Modify: `src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj`
- Modify: `src/Infrastructure.Web.Server/Authentication/AuthenticationBuilderExtensions.cs`
- Modify: `src/Infrastructure.Web.Server/Authentication/NorseSchemes.cs`
- Delete: `src/Infrastructure.Web.Server/Authentication/NorseMachineRejectionHandler.cs`
- Test: `tests/Infrastructure.Web.Server.Tests/Authentication/AuthenticationBuilderExtensionsTests.cs`

**Interfaces:**
- Produces: `NorseSchemes.Machine` (unchanged name/value, `"Norse.Machine"`) now forwards to `OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme` instead of a rejection handler.

- [ ] **Step 1: Write the failing test**

Add to `tests/Infrastructure.Web.Server.Tests/Authentication/AuthenticationBuilderExtensionsTests.cs`:

```csharp
[Fact]
void The_machine_scheme_forwards_to_OpenIddicts_validation_scheme()
{
	ServiceCollection services = new();
	services.AddNorseAuthentication();

	var provider = services.BuildServiceProvider();
	var options = provider.GetRequiredService<IOptionsMonitor<PolicySchemeOptions>>().Get(NorseSchemes.Machine);

	options.ForwardDefault.ShouldBe(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
}
```

Add `using OpenIddict.Validation.AspNetCore;` to the file's usings.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Infrastructure.Web.Server.Tests -- --filter-class "*.AuthenticationBuilderExtensionsTests"`
Expected: FAIL — compile error (`OpenIddict.Validation.AspNetCore` not yet referenced) until Step 3's package reference lands, then a runtime failure (still resolving `NorseMachineRejectionHandler`) until Step 3's code change lands.

- [ ] **Step 3: Implement**

`src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj` — add to the existing `<ItemGroup>`, alphabetically among the `PackageReference` entries:

```xml
<PackageReference Include="OpenIddict.Validation.AspNetCore" Version="7.*" />
```

`src/Infrastructure.Web.Server/Authentication/AuthenticationBuilderExtensions.cs` — replace the `Machine` scheme registration line and add the using:

```csharp
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Validation.AspNetCore;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>Composition-root wiring for the platform's authentication lanes.</summary>
public static class AuthenticationBuilderExtensions
{
	/// <param name="services">The service collection to configure.</param>
	extension(IServiceCollection services)
	{
		/// <summary>
		///     ... (existing doc comment, unchanged) ...
		/// </summary>
		/// <returns>The <see cref="AuthenticationBuilder" /> for further chaining.</returns>
		public AuthenticationBuilder AddNorseAuthentication() =>
			services
				.AddAuthentication(options =>
				{
					options.DefaultScheme = NorseSchemes.Default;
					options.DefaultAuthenticateScheme = NorseSchemes.Default;
					options.DefaultChallengeScheme = NorseSchemes.Default;
					options.DefaultForbidScheme = NorseSchemes.Default;
				})
				.AddPolicyScheme(NorseSchemes.Default, NorseSchemes.Default,
					options => options.ForwardDefaultSelector =
						context => NorseLaneSelector.Select(context.GetEndpoint()))
				.AddScheme<AuthenticationSchemeOptions, NorseBrowserHandler>(NorseSchemes.Browser, null)
				.AddScheme<NorseAnonymousOptions, NorseAnonymousHandler>(NorseSchemes.Anonymous, null)
				.AddScheme<AuthenticationSchemeOptions, NorseGrpcHandler>(NorseSchemes.IdentityCookieOnly, null)
				.AddPolicyScheme(NorseSchemes.Machine, NorseSchemes.Machine,
					options => options.ForwardDefault = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
				.AddScheme<AuthenticationSchemeOptions, NorseProbeHandler>(NorseSchemes.Probe, null);
	}
}
```

Delete `src/Infrastructure.Web.Server/Authentication/NorseMachineRejectionHandler.cs` entirely.

`src/Infrastructure.Web.Server/Authentication/NorseSchemes.cs` — update the `Machine` doc comment (the constant's value is unchanged):

```csharp
	/// <summary>
	///     The machine lane. Forwards to OpenIddict's own validation scheme
	///     (<c>OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme</c>) — registered as a
	///     <c>AddPolicyScheme</c> forward, not a hand-rolled handler, since OpenIddict's validation
	///     builder always registers under its own fixed scheme name and cannot be told to use this one
	///     directly (Himinbjorg#49).
	/// </summary>
	public const string Machine = "Norse.Machine";
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Infrastructure.Web.Server.Tests -- --filter-class "*.AuthenticationBuilderExtensionsTests"`
Expected: PASS

- [ ] **Step 5: Run the whole realm's test suite**

Run: `dotnet build Midgard.slnx && dotnet test Midgard.slnx`
Expected: SUCCESS. Confirms `NorseLaneSelectorTests` (which asserts `Select(EndpointFactory.Facade())` returns the scheme *name* `NorseSchemes.Machine`, unchanged) still passes — the lane selector itself is untouched, only what `Machine` resolves to changed.

- [ ] **Step 6: Stage (no commit — human commits)**

```bash
git add src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj \
  src/Infrastructure.Web.Server/Authentication/AuthenticationBuilderExtensions.cs \
  src/Infrastructure.Web.Server/Authentication/NorseSchemes.cs \
  tests/Infrastructure.Web.Server.Tests/Authentication/AuthenticationBuilderExtensionsTests.cs
git rm src/Infrastructure.Web.Server/Authentication/NorseMachineRejectionHandler.cs
```

### Task 4: OpenAPI bearer security scheme

**Files:**
- Create: `src/Infrastructure.Web.Server/OpenApi/BearerSecuritySchemeTransformer.cs`
- Create: `src/Infrastructure.Web.Server/OpenApi/MachineAuthOperationTransformer.cs`
- Create: `tests/Infrastructure.Web.Server.Tests/OpenApi/MachineAuthTransformerTests.cs`

**Interfaces:**
- Consumes: `Norse.Abstractions.Web.Server.Facade.GrpcControllerBase` (Asgard, already referenced).
- Produces: `BearerSecuritySchemeTransformer` (`IOpenApiDocumentTransformer`), `MachineAuthOperationTransformer` (`IOpenApiOperationTransformer`) — both registered by Yggdrasil's `AddOpenApi(...)` block in Phase 5.

- [ ] **Step 1: Write the failing test**

Create `tests/Infrastructure.Web.Server.Tests/OpenApi/MachineAuthTransformerTests.cs`, reusing the real-`TestServer` idiom `TransformerTests.cs` already establishes in this project:

```csharp
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Facade;
using Norse.Infrastructure.Web.Server.OpenApi;

namespace Norse.Infrastructure.Web.Server.Tests.OpenApi;

/// <summary>
///     Proves the bearer security scheme lands on a real generated document, and only on operations
///     whose declaring controller derives from <see cref="GrpcControllerBase" /> — the same "call the
///     real ASP.NET Core OpenAPI pipeline, never a stand-in" idiom <c>TransformerTests</c> uses.
/// </summary>
public sealed class MachineAuthTransformerTests
{
	[Fact]
	async Task The_bearer_scheme_component_is_registered_once()
	{
		var document = await BuildDocumentAsync();

		var scheme = document["components"]!["securitySchemes"]!["Bearer"]!;
		scheme["type"]!.GetValue<string>().ShouldBe("http");
		scheme["scheme"]!.GetValue<string>().ShouldBe("bearer");
		scheme["bearerFormat"]!.GetValue<string>().ShouldBe("JWT");
	}

	[Fact]
	async Task A_facade_operation_carries_the_bearer_security_requirement()
	{
		var document = await BuildDocumentAsync();

		var security = document["paths"]!["/facade"]!["get"]!["security"]!.AsArray();
		security.Count.ShouldBe(1);
		security[0]!.AsObject().ContainsKey("Bearer").ShouldBeTrue();
	}

	[Fact]
	async Task A_non_facade_operation_carries_no_security_requirement()
	{
		var document = await BuildDocumentAsync();

		document["paths"]!["/plain"]!["get"]!.AsObject().ContainsKey("security").ShouldBeFalse();
	}

	static async Task<JsonNode> BuildDocumentAsync()
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Services.AddControllers()
			.ConfigureApplicationPartManager(manager =>
			{
				for (var i = manager.FeatureProviders.Count - 1; i >= 0; i--)
					if (manager.FeatureProviders[i] is ControllerFeatureProvider)
						manager.FeatureProviders.RemoveAt(i);

				manager.FeatureProviders.Add(new OnlyMachineAuthFixtureControllersFeatureProvider());
			});
		builder.Services.AddOpenApi(options =>
		{
			options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
			options.AddOperationTransformer<MachineAuthOperationTransformer>();
		});

		await using var app = builder.Build();
		app.MapOpenApi();
		app.MapControllers();

		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative),
			TestContext.Current.CancellationToken);
		var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		await app.StopAsync(TestContext.Current.CancellationToken);

		if (!response.IsSuccessStatusCode)
			throw new InvalidOperationException(json);

		return JsonNode.Parse(json)!;
	}
}

sealed class OnlyMachineAuthFixtureControllersFeatureProvider : ControllerFeatureProvider
{
	protected override bool IsController(TypeInfo typeInfo) =>
		typeInfo == typeof(FacadeFixtureController).GetTypeInfo() ||
		typeInfo == typeof(PlainFixtureController).GetTypeInfo();
}

[Route("facade")]
sealed class FacadeFixtureController : GrpcControllerBase
{
#pragma warning disable CA1822 // ASP.NET Core actions must be instance methods.
	[HttpGet]
	public Task<ActionResult<string>> Get() =>
		FoldAsync(new ValueTask<Outcome<string>>(Outcome<string>.Ok("ok")));
#pragma warning restore CA1822
}

[ApiController]
[Route("plain")]
sealed class PlainFixtureController : ControllerBase
{
#pragma warning disable CA1822
	[HttpGet]
	public ActionResult<string> Get() => Ok("ok");
#pragma warning restore CA1822
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Infrastructure.Web.Server.Tests -- --filter-class "*.MachineAuthTransformerTests"`
Expected: FAIL — `BearerSecuritySchemeTransformer`/`MachineAuthOperationTransformer` don't exist yet (compile error).

- [ ] **Step 3: Implement**

`src/Infrastructure.Web.Server/OpenApi/BearerSecuritySchemeTransformer.cs`:

```csharp
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Norse.Infrastructure.Web.Server.OpenApi;

/// <summary>
///     Registers the reusable bearer <c>securitySchemes</c> component once (Himinbjorg#49) — the scheme
///     declaration itself; <see cref="MachineAuthOperationTransformer" /> is what actually attaches the
///     requirement to individual operations, since a document transformer alone cannot tell a facade
///     operation from any other.
/// </summary>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
	internal const string SchemeId = "Bearer";

	/// <inheritdoc />
	public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(document);

		document.Components ??= new OpenApiComponents();
		document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
		document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
		{
			Type = SecuritySchemeType.Http,
			Scheme = "bearer",
			BearerFormat = "JWT"
		};

		return Task.CompletedTask;
	}
}
```

`src/Infrastructure.Web.Server/OpenApi/MachineAuthOperationTransformer.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Norse.Abstractions.Web.Server.Facade;

namespace Norse.Infrastructure.Web.Server.OpenApi;

/// <summary>
///     Attaches the bearer security requirement to an operation only when its declaring controller
///     derives from <see cref="GrpcControllerBase" /> (Himinbjorg#49) — the same construction that
///     protects the controller at runtime (Asgard's class-level <c>[Authorize(Policy = Machine)]</c>)
///     drives what the document tells partners about it. A document transformer cannot make this
///     distinction on its own; this is why the scheme registration (<see cref="BearerSecuritySchemeTransformer" />)
///     and the per-operation requirement are two separate transformers, mirroring
///     <c>StandardResponsesTransformer</c>'s own operation-level access to controller metadata.
/// </summary>
public sealed class MachineAuthOperationTransformer : IOpenApiOperationTransformer
{
	/// <inheritdoc />
	public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context,
		CancellationToken cancellationToken)
	{
		if (context.Description.ActionDescriptor is not ControllerActionDescriptor descriptor ||
			!typeof(GrpcControllerBase).IsAssignableFrom(descriptor.ControllerTypeInfo))
			return Task.CompletedTask;

		operation.Security ??= [];
		operation.Security.Add(new OpenApiSecurityRequirement
		{
			[new OpenApiSecuritySchemeReference(BearerSecuritySchemeTransformer.SchemeId, context.Document)] = []
		});

		return Task.CompletedTask;
	}
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Infrastructure.Web.Server.Tests -- --filter-class "*.MachineAuthTransformerTests"`
Expected: PASS. `OpenApiOperationTransformerContext.Description` (an `ApiDescription`, whose `.ActionDescriptor` casts to `ControllerActionDescriptor` for `.ControllerTypeInfo`) and `.Document` are both confirmed present on the installed `Microsoft.AspNetCore.OpenApi` 11.0.0-preview.7.26381.103 package.

- [ ] **Step 5: Run the whole realm's test suite**

Run: `dotnet build Midgard.slnx && dotnet test Midgard.slnx`
Expected: SUCCESS.

- [ ] **Step 6: Stage (no commit — human commits)**

```bash
git add src/Infrastructure.Web.Server/OpenApi/BearerSecuritySchemeTransformer.cs \
  src/Infrastructure.Web.Server/OpenApi/MachineAuthOperationTransformer.cs \
  tests/Infrastructure.Web.Server.Tests/OpenApi/MachineAuthTransformerTests.cs
```

## SHIP GATE — Midgard

PR opened from `feature/machine-authn`, CI green, merged to `master`.

---

## Phase 3 — Himinbjörg

**Fork:** `feature/machine-authn`, from current `master` (`3df4e95`). Confirmed no other fork is open in this realm (`feature/access-count-breakout` exists as a remote branch reference from prior work but is not the current checkout state — verify `git branch -a` shows a clean `master` checkout before branching, matching the other four realms' confirmed state).

### Task 5: OpenIddict server, validation, and the client-credentials exchange handler

**Files:**
- Modify: `src/Identity.Web.Server/Identity.Web.Server.csproj`
- Modify: `src/Identity.Web.Server/IdentityBuilderExtensions.cs`
- Modify: `src/Identity.Web.Server/ServiceCollectionExtensions.cs`
- Create: `src/Identity.Web.Server/OpenIddictExchangeEndpoint.cs`
- Create: `src/Identity.Web.Server/OpenIddictEndpointRouteBuilderExtensions.cs`
- Modify: `tests/Identity.Web.Server.Tests/PostgresIdentityFixture.cs` — the platform's one identity-DB-backed test fixture (confirmed, read verbatim during planning); its own doc comment forbids a second in-process instance (an EF `ModelSource`-cache/`IPersonalDataProtector` hazard, root-caused from a real production incident), so the new HTTP-testable surface this task needs extends this fixture rather than inventing a competing one.
- Test: `tests/Identity.Web.Server.Tests/OpenIddictTokenEndpointTests.cs`

**Interfaces:**
- Consumes: `NorseIdentityDbContext`, `NorseOpenIddictApplication` (existing, `Identity.EntityFramework`); `PostgresIdentityFixture`/`PostgresTestGroup` (existing, extended below).
- Produces: `AddNorseAuthenticationService(string connectionStringName, X509Certificate2 signingCertificate, TimeSpan? accessTokenLifetime = null)` (signature change — `signingCertificate` is new and required; `accessTokenLifetime` is new and optional, defaulting to unchanged production behavior, added for Phase 5 Task 11's expired-token test); `OpenIddictEndpointRouteBuilderExtensions.MapNorseOpenIddictEndpoints()` on `IEndpointRouteBuilder`; `PostgresIdentityFixture.CreateTestClient()` (new) and `PostgresIdentityFixture.CreateApplicationManager()` (new).

This is the largest task in the plan — server config, validation config, and the exchange handler are only provable together (the handler's `SignIn` call is the actual proof the server config works), so they ship as one reviewable unit, tested against a real client_credentials round trip over real Postgres.

**Fixture change, load-bearing:** `PostgresIdentityFixture` today builds a generic `IHost` (`Host.CreateApplicationBuilder()`) with no HTTP surface — nothing in this project can currently POST to an endpoint. Proving `/connect/token` for real needs a web host. `WebApplication` implements `IHost` (delegation, not a different contract), so retyping the fixture's builder to `WebApplication.CreateBuilder()` plus `.WebHost.UseTestServer()` keeps every existing method (`CreateScopeAsync`, `SeedUserAsync`, `CreateSignInManager`, `CreateUserManager`, `CreateRoleManager`) working unchanged — they only ever touched `.Services`, which both host types expose identically.

- [ ] **Step 1: Extend the fixture (no test yet — this step's own correctness is proven transitively by every fixture-consuming test, exactly like the fixture's own existing smoke assertion)**

Modify `tests/Identity.Web.Server.Tests/PostgresIdentityFixture.cs`:

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Backend.Keys;
using Norse.Abstractions.Web.Server.DeferredSignIn;
using Norse.Identity.EntityFramework;
using Norse.Identity.Migrations;
using Norse.Identity.Migrations.PostgreSQL;
using Norse.Infrastructure.Backend.Keys;
using Norse.Persistence.EntityFramework;
using Norse.Persistence.EntityFramework.PostgreSQL;
using OpenIddict.Abstractions;
using Testcontainers.PostgreSql;

namespace Norse.Identity.Web.Server.Tests;

/// <summary>
///     ... (existing summary, unchanged) ...
/// </summary>
/// <remarks>
///     ... (existing remarks, unchanged — still the load-bearing "exactly one instance" warning) ...
/// </remarks>
public sealed class PostgresIdentityFixture : IAsyncLifetime
{
	readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:19beta2")
		.WithDatabase("norse_identity")
		.Build();

	readonly List<IServiceScope> _scopes = [];
	WebApplication _app = null!;
	X509Certificate2 _certificate = null!;

	string _keysRoot = null!;

	/// <inheritdoc />
	public async ValueTask InitializeAsync()
	{
		await _container.StartAsync();
		var connectionString = _container.GetConnectionString();

		DbContextOptionsBuilder<NorseIdentityDbContext> migrationOptions = new();
		migrationOptions.ApplyNorseProviderOptions(NorsePostgresEfProvider.Instance, connectionString,
			typeof(NorseIdentityDbContextFactory).Assembly.GetName().Name);
		await using (NorseIdentityDbContext migrationContext = new(migrationOptions.Options))
		{
			NorseIdentityMigrationContributor contributor = new(migrationContext);
			await contributor.MigrateAsync(CancellationToken.None);
		}

		_keysRoot = Path.Combine(Path.GetTempPath(), $"norse-identity-keys-{Guid.NewGuid():N}");
		_certificate = CreateSelfSignedCertificate();

		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Configuration["ConnectionStrings:identity"] = connectionString;
		builder.AddNorseAuthenticationService("identity", _certificate);
		builder.Services
			.AddNorseDevelopmentKeys(_keysRoot)
			.AddSingleton<IDeferredSignIn>(Substitute.For<IDeferredSignIn>())
			.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

		_app = builder.Build();
		_app.UseAuthentication();
		_app.UseAuthorization();
		_app.MapNorseOpenIddictEndpoints();
		await _app.StartAsync(CancellationToken.None);

		// Fixture-level smoke assertion (load-bearing, per Task 18's review): ... (existing smoke
		// assertion body, unchanged — still calls SeedUserAsync/CreateScopeAsync, both untouched below).
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		foreach (var scope in _scopes)
			scope.Dispose();
		await _app.DisposeAsync(); // WebApplication implements IAsyncDisposable directly -- no switch needed.
		_certificate.Dispose();

		await _container.DisposeAsync();
		if (Directory.Exists(_keysRoot))
			Directory.Delete(_keysRoot, recursive: true);
	}

	/// <summary>A real <see cref="TestServer" />-backed <see cref="HttpClient" /> against <c>/connect/token</c>.</summary>
	public HttpClient CreateTestClient() =>
		_app.GetTestServer().CreateClient();

	/// <summary>Resolves a real <see cref="IOpenIddictApplicationManager" /> from a new DI scope, for seeding test clients.</summary>
	public IOpenIddictApplicationManager CreateApplicationManager()
	{
		var scope = _app.Services.CreateScope();
		_scopes.Add(scope);
		return scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
	}

	/// <summary>
	///     Resolves a fresh <see cref="NorseIdentityDbContext" /> and <see cref="ISubjectKeyStore" /> from a new DI
	///     scope.
	/// </summary>
	public Task<(NorseIdentityDbContext Context, ISubjectKeyStore KeyStore)> CreateScopeAsync()
	{
		var scope = _app.Services.CreateScope();
		_scopes.Add(scope);
		return Task.FromResult((
			scope.ServiceProvider.GetRequiredService<NorseIdentityDbContext>(),
			scope.ServiceProvider.GetRequiredService<ISubjectKeyStore>()));
	}

	/// <summary>Seeds a user through the real <c>NorseUserManager</c> chokepoint -- no manual <c>SubjectCryptoScope</c>.</summary>
	/// <param name="email">The user's email -- also stands in as the username.</param>
	/// <param name="phone">
	///     The user's phone number, E.164. Omitted leaves the column null, same as a user who never supplied
	///     one.
	/// </param>
	public async Task<NorseUser> SeedUserAsync(string email, string? phone = null)
	{
		var scope = _app.Services.CreateScope();
		_scopes.Add(scope);
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<NorseUser>>();
		NorseUser user = new() { UserName = email, Email = email, PhoneNumber = phone };
		var result = await userManager.CreateAsync(user);
		return result.Succeeded ?
			user :
			throw new InvalidOperationException(
				$"Seeding user '{email}' failed: {string.Join(", ", result.Errors.Select(e => e.Description))}");
	}

	/// <summary>
	///     Resolves a real <see cref="SignInManager{TUser}" /> from a new DI scope, over a bare
	///     <see cref="DefaultHttpContext" />.
	/// </summary>
	public SignInManager<NorseUser> CreateSignInManager()
	{
		var scope = _app.Services.CreateScope();
		_scopes.Add(scope);
		return scope.ServiceProvider.GetRequiredService<SignInManager<NorseUser>>();
	}

	/// <summary>
	///     Resolves a real <see cref="UserManager{TUser}" /> from a new DI scope -- for tests that need a
	///     shape <see cref="SeedUserAsync" /> can't produce, e.g. a user with no email at all
	///     (<c>SeedUserAsync</c> always sets one, standing in for the username).
	/// </summary>
	public UserManager<NorseUser> CreateUserManager()
	{
		var scope = _app.Services.CreateScope();
		_scopes.Add(scope);
		return scope.ServiceProvider.GetRequiredService<UserManager<NorseUser>>();
	}

	/// <summary>Resolves a real <see cref="RoleManager{TRole}" /> from a new DI scope, for tests that grant and revoke roles.</summary>
	public RoleManager<NorseRole> CreateRoleManager()
	{
		var scope = _app.Services.CreateScope();
		_scopes.Add(scope);
		return scope.ServiceProvider.GetRequiredService<RoleManager<NorseRole>>();
	}

	static X509Certificate2 CreateSelfSignedCertificate()
	{
		using var rsa = RSA.Create(2048);
		var request = new CertificateRequest("CN=Norse Identity Tests", rsa, HashAlgorithmName.SHA256,
			RSASignaturePadding.Pkcs1);
		return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
	}
}
```

(Every method body below `DisposeAsync` is otherwise byte-identical to the existing file — only `_host` → `_app` and the type change. The smoke assertion inside `InitializeAsync` is untouched; only the host-construction lines above it change.)

- [ ] **Step 2: Write the failing test**

Create `tests/Identity.Web.Server.Tests/OpenIddictTokenEndpointTests.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Abstractions;

namespace Norse.Identity.Web.Server.Tests;

[Collection(PostgresTestGroup.Name)]
public sealed class OpenIddictTokenEndpointTests(PostgresIdentityFixture fixture)
{
	[Fact]
	async Task A_seeded_client_obtains_a_JWT_via_client_credentials()
	{
		var manager = fixture.CreateApplicationManager();
		await manager.CreateAsync(new OpenIddictApplicationDescriptor
		{
			ClientId = "test-machine-client",
			ClientSecret = "test-secret-value",
			ClientType = OpenIddictConstants.ClientTypes.Confidential,
			Permissions =
			{
				OpenIddictConstants.Permissions.Endpoints.Token,
				OpenIddictConstants.Permissions.GrantTypes.ClientCredentials
			}
		}, TestContext.Current.CancellationToken);

		using var client = fixture.CreateTestClient();
		using var response = await client.PostAsync(new Uri("/connect/token", UriKind.Relative),
			new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["grant_type"] = "client_credentials",
				["client_id"] = "test-machine-client",
				["client_secret"] = "test-secret-value"
			}), TestContext.Current.CancellationToken);

		response.EnsureSuccessStatusCode();
		var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(TestContext.Current.CancellationToken);
		payload.ShouldNotBeNull();

		var jwt = new JsonWebToken(payload.AccessToken);
		jwt.GetClaim(OpenIddictConstants.Claims.Subject).Value.ShouldBe("test-machine-client");
		jwt.Audiences.ShouldContain("Norse.Facade");
	}

	[Fact]
	async Task An_unsupported_grant_type_is_rejected_before_reaching_application_code()
	{
		using var client = fixture.CreateTestClient();
		using var response = await client.PostAsync(new Uri("/connect/token", UriKind.Relative),
			new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["grant_type"] = "password",
				["username"] = "irrelevant",
				["password"] = "irrelevant"
			}), TestContext.Current.CancellationToken);

		response.IsSuccessStatusCode.ShouldBeFalse();
	}

	// The OAuth2 token response is snake_case on the wire (access_token/token_type/expires_in) -- System.Text.Json's
	// default case-insensitive matching only folds case, not underscores, so without these attributes every
	// property would deserialize to its type default and payload.AccessToken would silently be null instead of
	// throwing, masking a real failure as a false pass.
	sealed record TokenResponse(
		[property: JsonPropertyName("access_token")] string AccessToken,
		[property: JsonPropertyName("token_type")] string TokenType,
		[property: JsonPropertyName("expires_in")] int ExpiresIn);
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/Identity.Web.Server.Tests -- --filter-class "*.OpenIddictTokenEndpointTests"`
Expected: FAIL — compile errors (`AddNorseAuthenticationService` doesn't accept a certificate parameter yet, `MapNorseOpenIddictEndpoints` doesn't exist, `PostgresIdentityFixture.CreateTestClient`/`.CreateApplicationManager` don't exist until Step 1's fixture edit and this step's src edits both land — Step 1 above already contains the fixture edit; if executed strictly in written order, compile this step together with Step 4 below rather than expecting an isolated red state from the fixture alone, since the fixture change and the src changes are mutually dependent).

- [ ] **Step 4: Implement**

`src/Identity.Web.Server/Identity.Web.Server.csproj` — add to the `<ItemGroup>`:

```xml
<PackageReference Include="OpenIddict.Server.AspNetCore" Version="7.*" />
<PackageReference Include="OpenIddict.Validation.AspNetCore" Version="7.*" />
```

`src/Identity.Web.Server/IdentityBuilderExtensions.cs` — thread the certificate through and add the `.AddServer()`/`.AddValidation()` chain:

```csharp
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Identity;
using Norse.Identity.EntityFramework;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;

namespace Norse.Identity.Web.Server;

static class IdentityBuilderExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>
		///     ... (existing summary, unchanged) ...
		/// </summary>
		/// <param name="signingCertificate">
		///     The OpenIddict signing/encryption certificate — a single certificate serving both roles
		///     (spec §2.3). Callers own its lifetime; this method does not dispose it.
		/// </param>
		/// <param name="accessTokenLifetime">
		///     Overrides OpenIddict's default access token lifetime. Omitted in production — exists so
		///     Task 11's expired-token test can mint a token that is already expired by the time it's used,
		///     without hand-constructing a JWT outside OpenIddict's own issuance path.
		/// </param>
		/// <returns>The <see cref="IdentityBuilder" /> for further chaining.</returns>
		public IdentityBuilder AddNorseIdentity(X509Certificate2 signingCertificate, TimeSpan? accessTokenLifetime = null)
		{
			services.Configure<IdentityOptions>(o =>
			{
				o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
				o.Stores.ProtectPersonalData = true;
			});
			services
				.AddSingleton<IPersonalDataProtector, NorsePersonalDataProtector>()
				.AddSingleton<ILookupProtector, NorseLookupProtector>()
				.AddSingleton<ILookupProtectorKeyRing, NorseLookupProtectorKeyRing>();

			var identityBuilder = services
				.AddIdentity<NorseUser, NorseRole>()
				.AddUserStore<NorseUserStore>()
				.AddUserManager<NorseUserManager>()
				.AddEntityFrameworkStores<NorseIdentityDbContext>()
				.AddDefaultTokenProviders()
				.AddClaimsPrincipalFactory<NorseUserClaimsPrincipalFactory>();

			services.ConfigureApplicationCookie(options => options.Cookie.Name = "Norse.Identity");

			services.AddNorseOpenIddictCore()
				.AddServer(o =>
				{
					o.AllowClientCredentialsFlow()
						.SetTokenEndpointUris("/connect/token")
						.DisableAccessTokenEncryption()
						.AddSigningCertificate(signingCertificate)
						.AddEncryptionCertificate(signingCertificate)
						.UseAspNetCore(a => a.EnableTokenEndpointPassthrough());
					if (accessTokenLifetime is { } lifetime)
						o.SetAccessTokenLifetime(lifetime);
				})
				.AddValidation(o =>
				{
					o.UseLocalServer();
					o.AddAudiences("Norse.Facade");
					o.UseAspNetCore();
				});

			return identityBuilder;
		}
	}
}
```

`src/Identity.Web.Server/ServiceCollectionExtensions.cs` — thread the parameter through:

```csharp
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Identity;
using Norse.AuthN.Services;
using Norse.Identity.EntityFramework;
using Norse.Identity.Web.Server.Disclosure;
using Norse.Persistence.EntityFramework;
using Norse.Persistence.EntityFramework.PostgreSQL;

namespace Norse.Identity.Web.Server;

public static class ServiceCollectionExtensions
{
	extension(IHostApplicationBuilder builder)
	{
		/// <summary>
		///     ... (existing summary, unchanged) ...
		/// </summary>
		/// <param name="connectionStringName">The configuration key under <c>ConnectionStrings</c>.</param>
		/// <param name="signingCertificate">The OpenIddict signing/encryption certificate (spec §2.3).</param>
		/// <param name="accessTokenLifetime">Overrides OpenIddict's default access token lifetime — see <see cref="IdentityBuilderExtensions.AddNorseIdentity" />.</param>
		/// <returns>The same <paramref name="builder" /> for chaining.</returns>
		public IHostApplicationBuilder AddNorseAuthenticationService(string connectionStringName,
			X509Certificate2 signingCertificate, TimeSpan? accessTokenLifetime = null)
		{
			builder.Services
				.AddNorseIdentityWebServerHandlers()
				.AddScoped<IAuthenticationService, AuthenticationService>()
				.AddScoped<IIdentityService, IdentityService>()
				.AddSingleton<IEmailSender<NorseUser>, IdentityNoOpEmailSender>()
				.AddNorseIdentity(signingCertificate, accessTokenLifetime)
				.AddSignInManager<NorseSignInManager>();

			builder.Services
				.AddOpenTelemetry()
				.WithMetrics(static metrics => metrics.AddMeter("Microsoft.AspNetCore.Identity"));

			return builder.AddNorseContext<NorseIdentityDbContext>(NorsePostgresEfProvider.Instance,
				connectionStringName);
		}
	}
}
```

`src/Identity.Web.Server/OpenIddictExchangeEndpoint.cs`:

```csharp
using System.Security.Claims;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Norse.Identity.Web.Server;

/// <summary>
///     Mints the token for the client_credentials grant (Himinbjorg#49). OpenIddict has already
///     authenticated the client against the seeded application before this runs (confidential clients
///     require client authentication by default) and has already rejected any grant type other than
///     client_credentials, since only <c>AllowClientCredentialsFlow()</c> is enabled — there is no other
///     flow for a request to arrive as. This handler's job is exactly one thing: build the principal
///     OpenIddict does not invent on its own. Synchronous (no <c>async</c>/<c>await</c> anywhere in this
///     type) — nothing here is asynchronous work, and a warnings-as-errors realm turns an unnecessary
///     `async` modifier into CS1998, not just a style nit.
/// </summary>
static class OpenIddictExchangeEndpoint
{
	internal static IResult Handle(HttpContext context)
	{
		var request = context.GetOpenIddictServerRequest() ??
			throw new InvalidOperationException("The OpenIddict request cannot be retrieved.");

		// Internal invariant, not a user-facing branch: a violation here means the flow-gating in
		// AddServer() broke, not that a caller sent a bad request. See OpenIddictExchangeEndpoint's
		// class doc.
		if (!request.IsClientCredentialsGrantType())
			throw new InvalidOperationException(
				$"{nameof(OpenIddictExchangeEndpoint)} only handles the client_credentials grant, " +
				$"but received grant type '{request.GrantType}'.");

		var clientId = request.ClientId ??
			throw new InvalidOperationException("The authenticated request carries no client_id.");

		// OpenIddictConstants.Claims.Subject ("sub"), not ClaimTypes.NameIdentifier (a different, URI-shaped
		// BCL claim type) -- OpenIddict's token-generation pipeline reads the principal's subject specifically
		// by this claim type, verified by reflection against the installed 7.6.0 package during planning.
		ClaimsIdentity identity = new(
			OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
			OpenIddictConstants.Claims.Subject, ClaimTypes.Role);
		identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, clientId).SetDestinations(
			OpenIddictConstants.Destinations.AccessToken));

		ClaimsPrincipal principal = new(identity);
		principal.SetAudiences("Norse.Facade");

		return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
	}
}
```

`src/Identity.Web.Server/OpenIddictEndpointRouteBuilderExtensions.cs`:

```csharp
namespace Norse.Identity.Web.Server;

/// <summary>Maps the OpenIddict token endpoint (Himinbjorg#49).</summary>
public static class OpenIddictEndpointRouteBuilderExtensions
{
	extension(IEndpointRouteBuilder endpoints)
	{
		/// <summary>Maps <c>/connect/token</c> onto <see cref="OpenIddictExchangeEndpoint" />.</summary>
		/// <returns>A convention builder for the mapped endpoint.</returns>
		public IEndpointConventionBuilder MapNorseOpenIddictEndpoints() =>
			endpoints.MapPost("/connect/token", OpenIddictExchangeEndpoint.Handle);
	}
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Identity.Web.Server.Tests -- --filter-class "*.OpenIddictTokenEndpointTests"`
Expected: PASS (needs Docker, per this realm's existing container-fact convention). The fixture's own existing smoke assertion (`InitializeAsync`) also re-runs and must keep passing — it is untouched by this task's changes beyond the host-construction lines Step 1 edits.

- [ ] **Step 6: Run the whole realm's build and test suite**

Run: `dotnet build Himinbjorg.slnx`
Expected: SUCCESS, zero warnings, including IDE0055.

Run (needs Docker): `dotnet test Himinbjorg.slnx`
Expected: SUCCESS — including every other test class in `Identity.Web.Server.Tests` that already consumes `PostgresIdentityFixture` (`DisclosureHandlerTests`, `ErasureServiceTests`, `TemporalIdentityVersioningTests`), since Step 1's fixture edit is a shared, load-bearing change.

- [ ] **Step 7: Stage (no commit — human commits)**

```bash
git add src/Identity.Web.Server/Identity.Web.Server.csproj \
  src/Identity.Web.Server/IdentityBuilderExtensions.cs \
  src/Identity.Web.Server/ServiceCollectionExtensions.cs \
  src/Identity.Web.Server/OpenIddictExchangeEndpoint.cs \
  src/Identity.Web.Server/OpenIddictEndpointRouteBuilderExtensions.cs \
  tests/Identity.Web.Server.Tests/PostgresIdentityFixture.cs \
  tests/Identity.Web.Server.Tests/OpenIddictTokenEndpointTests.cs
```

### Task 6: Seed the machine client

**Files:**
- Create: `src/Identity.Migrations/MachineClientSeedContributor.cs`
- Test: `tests/Identity.Migrations.Tests/MachineClientSeedContributorTests.cs`

**Interfaces:**
- Consumes: `NorseIdentityDbContext`, `NorseOpenIddictApplication` (existing); `PostgresContainerFixture`/`PostgresCollection` (existing, confirmed during planning — a bare Testcontainers connection-string fixture in `Identity.Migrations.Tests`, distinct from `Identity.Web.Server.Tests`' heavier `PostgresIdentityFixture` and carrying none of that fixture's singleton hazard, since it builds no host or DI graph itself).
- Produces: `MachineClientSeedContributor : ISeedContributor` — auto-discovered and registered by Urðarbrunnr's `MigrationContributorGenerator` in production (confirmed during planning: the generator calls a discovered `ISeedContributor`'s static `ConfigureServices` automatically). This test constructs it directly rather than through that generator, since the generator's job is composition-root wiring, not something this test needs to re-prove.

**Test project correction:** the actual container fixture for `Identity.Migrations.*` lives in `Identity.Migrations.Tests` (confirmed by reading the project directly — `PostgresContainerFixture.cs` + `PostgresCollection.cs`, `[CollectionDefinition("Postgres")]`), not `Identity.Migrations.PostgreSQL.Tests`. `NorseIdentityMigrationContributorContainerTests.cs` in that same project is the exact precedent this task's test mirrors.

**Test isolation, named explicitly:** `PostgresContainerFixture` is one container shared across every test in the `"Postgres"` collection for the whole run — nothing resets it between test methods, and `MachineClientSeedContributor.ClientId` is a single fixed constant (mirroring production reality: there really is only one machine client). Every test below therefore deletes that one row first, so each test starts from a known, empty state regardless of execution order — this is the isolation/cleanup step the design requires, not an assumption.

- [ ] **Step 1: Write the failing test**

Create `tests/Identity.Migrations.Tests/MachineClientSeedContributorTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Norse.Identity.EntityFramework;
using Norse.Identity.Migrations.PostgreSQL;
using Norse.Persistence.EntityFramework;
using Norse.Persistence.EntityFramework.PostgreSQL;
using OpenIddict.Abstractions;

namespace Norse.Identity.Migrations.Tests;

[Collection("Postgres")]
public sealed class MachineClientSeedContributorTests(PostgresContainerFixture fixture)
{
	[Fact]
	async Task First_run_creates_the_application_with_both_required_permissions()
	{
		await using var context = await CreateMigratedContextAsync();
		var manager = BuildApplicationManager(context);
		await ResetMachineClientAsync(manager);
		var contributor = new MachineClientSeedContributor(BuildConfiguration("first-secret"), manager);

		await contributor.SeedAsync(TestContext.Current.CancellationToken);

		var application = await manager.FindByClientIdAsync(MachineClientSeedContributor.ClientId,
			TestContext.Current.CancellationToken);
		application.ShouldNotBeNull();
		var permissions = await manager.GetPermissionsAsync(application, TestContext.Current.CancellationToken);
		permissions.ShouldContain(OpenIddictConstants.Permissions.Endpoints.Token);
		permissions.ShouldContain(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
	}

	[Fact]
	async Task Reseeding_with_the_same_secret_writes_nothing_new()
	{
		await using var context = await CreateMigratedContextAsync();
		var manager = BuildApplicationManager(context);
		await ResetMachineClientAsync(manager);
		var configuration = BuildConfiguration("stable-secret");
		var contributor = new MachineClientSeedContributor(configuration, manager);
		await contributor.SeedAsync(TestContext.Current.CancellationToken);
		var afterFirstRun = await manager.FindByClientIdAsync(MachineClientSeedContributor.ClientId,
			TestContext.Current.CancellationToken);
		var tokenAfterFirstRun = await manager.GetIdAsync(afterFirstRun!, TestContext.Current.CancellationToken);
		var concurrencyTokenAfterFirstRun = await GetConcurrencyTokenAsync(context, tokenAfterFirstRun!);

		await contributor.SeedAsync(TestContext.Current.CancellationToken); // identical config, second run

		var concurrencyTokenAfterSecondRun = await GetConcurrencyTokenAsync(context, tokenAfterFirstRun!);
		// The real, observable proof no write happened: OpenIddict's own optimistic-concurrency column is
		// untouched. A validate-then-skip check alone (the prior draft's mistake) only proves the secret is
		// still valid -- it cannot tell "nothing was written" from "something was written back to the same
		// value," and this table has no audit log to check instead.
		concurrencyTokenAfterSecondRun.ShouldBe(concurrencyTokenAfterFirstRun);
		(await manager.ValidateClientSecretAsync(afterFirstRun!, "stable-secret", TestContext.Current.CancellationToken))
			.ShouldBeTrue();
	}

	[Fact]
	async Task Reseeding_with_a_changed_secret_invalidates_the_old_one_and_writes_a_new_one()
	{
		await using var context = await CreateMigratedContextAsync();
		var manager = BuildApplicationManager(context);
		await ResetMachineClientAsync(manager);
		var contributor = new MachineClientSeedContributor(BuildConfiguration("old-secret"), manager);
		await contributor.SeedAsync(TestContext.Current.CancellationToken);
		var application = await manager.FindByClientIdAsync(MachineClientSeedContributor.ClientId,
			TestContext.Current.CancellationToken);
		var id = await manager.GetIdAsync(application!, TestContext.Current.CancellationToken);
		var concurrencyTokenBeforeRotation = await GetConcurrencyTokenAsync(context, id!);

		var rotatedContributor = new MachineClientSeedContributor(BuildConfiguration("new-secret"), manager);
		await rotatedContributor.SeedAsync(TestContext.Current.CancellationToken);

		var concurrencyTokenAfterRotation = await GetConcurrencyTokenAsync(context, id!);
		concurrencyTokenAfterRotation.ShouldNotBe(concurrencyTokenBeforeRotation); // a write did happen this time.
		var rotated = await manager.FindByClientIdAsync(MachineClientSeedContributor.ClientId,
			TestContext.Current.CancellationToken);
		(await manager.ValidateClientSecretAsync(rotated!, "old-secret", TestContext.Current.CancellationToken))
			.ShouldBeFalse();
		(await manager.ValidateClientSecretAsync(rotated!, "new-secret", TestContext.Current.CancellationToken))
			.ShouldBeTrue();
	}

	async Task<NorseIdentityDbContext> CreateMigratedContextAsync()
	{
		DbContextOptionsBuilder<NorseIdentityDbContext> optionsBuilder = new();
		optionsBuilder.ApplyNorseProviderOptions(NorsePostgresEfProvider.Instance,
			fixture.ConnectionString, typeof(NorseIdentityDbContextFactory).Assembly.GetName().Name);
		NorseIdentityDbContext context = new(optionsBuilder.Options);
		await new NorseIdentityMigrationContributor(context).MigrateAsync(TestContext.Current.CancellationToken);
		return context;
	}

	static IOpenIddictApplicationManager BuildApplicationManager(NorseIdentityDbContext context)
	{
		ServiceCollection services = new();
		services.AddSingleton(context);
		MachineClientSeedContributor.ConfigureServices(services);
		return services.BuildServiceProvider().GetRequiredService<IOpenIddictApplicationManager>();
	}

	static IConfiguration BuildConfiguration(string secret) =>
		new ConfigurationBuilder()
			.AddInMemoryCollection([new("OIDC_MACHINE_CLIENT_SECRET", secret)])
			.Build();

	static async Task ResetMachineClientAsync(IOpenIddictApplicationManager manager)
	{
		var existing = await manager.FindByClientIdAsync(MachineClientSeedContributor.ClientId,
			TestContext.Current.CancellationToken);
		if (existing is not null)
			await manager.DeleteAsync(existing, TestContext.Current.CancellationToken);
	}

	static async Task<string> GetConcurrencyTokenAsync(NorseIdentityDbContext context, string id) =>
		(await context.Set<Norse.Identity.EntityFramework.NorseOpenIddictApplication>()
			.AsNoTracking()
			.SingleAsync(a => a.Id == Guid.Parse(id), TestContext.Current.CancellationToken))
		.ConcurrencyToken!;
}
```

`IOpenIddictApplicationManager.DeleteAsync(application, ct)` and `.GetIdAsync(application, ct)` are both confirmed present on the installed 7.6.0 `OpenIddictApplicationManager<TApplication>`, verified the same way as the rest of this plan's OpenIddict API references (a reflection probe against the actual installed assembly, not generic docs).

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Identity.Migrations.Tests -- --filter-class "*.MachineClientSeedContributorTests"`
Expected: FAIL — `MachineClientSeedContributor` doesn't exist.

- [ ] **Step 3: Implement**

`src/Identity.Migrations/MachineClientSeedContributor.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Migrations.Seeding;
using Norse.Identity.EntityFramework;
using OpenIddict.Abstractions;

namespace Norse.Identity.Migrations;

/// <summary>
///     Seeds the platform's one machine client (Himinbjorg#49) — the seeded application every
///     client_credentials caller authenticates as until #51/#53 add more. Idempotent by reconciliation,
///     not by skip-if-exists: an unconditional re-hash on every run would rewrite the stored secret hash
///     even when the configured secret has not changed (<see cref="IOpenIddictApplicationManager" />
///     salts on write), so the secret is validated before it is ever replaced.
/// </summary>
/// <param name="configuration">Configuration — reads <c>OIDC_MACHINE_CLIENT_SECRET</c>.</param>
/// <param name="applicationManager">Resolved via <see cref="ConfigureServices" />.</param>
public sealed class MachineClientSeedContributor(
	IConfiguration configuration,
	IOpenIddictApplicationManager applicationManager) : ISeedContributor
{
	internal const string ClientId = "norse-machine";
	internal const string DisplayName = "Norse Machine Client";

	static readonly HashSet<string> RequiredPermissions =
	[
		OpenIddictConstants.Permissions.Endpoints.Token,
		OpenIddictConstants.Permissions.GrantTypes.ClientCredentials
	];

	/// <inheritdoc />
	public string Name => "Norse.Identity.MachineClient";

	/// <inheritdoc />
	public static void ConfigureServices(IServiceCollection services) =>
		services.AddNorseOpenIddictCore();

	/// <inheritdoc />
	public async Task SeedAsync(CancellationToken cancellationToken)
	{
		var secret = configuration["OIDC_MACHINE_CLIENT_SECRET"] ??
			throw new InvalidOperationException("Configuration value 'OIDC_MACHINE_CLIENT_SECRET' is not configured.");

		var application = await applicationManager.FindByClientIdAsync(ClientId, cancellationToken)
			.ConfigureAwait(false);

		if (application is null)
		{
			await applicationManager.CreateAsync(BuildDescriptor(secret), cancellationToken).ConfigureAwait(false);
			return;
		}

		var secretStillValid = await applicationManager
			.ValidateClientSecretAsync(application, secret, cancellationToken)
			.ConfigureAwait(false);
		var permissions = await applicationManager.GetPermissionsAsync(application, cancellationToken)
			.ConfigureAwait(false);
		var clientType = await applicationManager.GetClientTypeAsync(application, cancellationToken)
			.ConfigureAwait(false);
		var displayName = await applicationManager.GetDisplayNameAsync(application, cancellationToken)
			.ConfigureAwait(false);

		var permissionsMatch = permissions.ToHashSet().SetEquals(RequiredPermissions);
		var clientTypeMatches = string.Equals(clientType, OpenIddictConstants.ClientTypes.Confidential,
			StringComparison.Ordinal);
		var displayNameMatches = string.Equals(displayName, DisplayName, StringComparison.Ordinal);

		if (secretStillValid && permissionsMatch && clientTypeMatches && displayNameMatches)
			return; // Nothing differs -- no write.

		// ClientSecret is set on the update descriptor ONLY when it actually needs replacing. Setting it
		// unconditionally -- even to a value that already validates -- would make
		// IOpenIddictApplicationManager re-salt-and-hash on every run that touches any other field, a
		// write with no real change to make. secretStillValid ? null : secret is the whole fix.
		await applicationManager.UpdateAsync(application, BuildDescriptor(secretStillValid ? null : secret),
			cancellationToken).ConfigureAwait(false);
	}

	static OpenIddictApplicationDescriptor BuildDescriptor(string? secret)
	{
		OpenIddictApplicationDescriptor descriptor = new()
		{
			ClientId = ClientId,
			ClientSecret = secret,
			ClientType = OpenIddictConstants.ClientTypes.Confidential,
			DisplayName = DisplayName
		};
		foreach (var permission in RequiredPermissions)
			descriptor.Permissions.Add(permission);
		return descriptor;
	}
}
```

`OpenIddictApplicationDescriptor.ClientSecret` is confirmed nullable (`string?`) on the installed 7.6.0 package (reflection-verified during planning) — `BuildDescriptor(null)` is only ever reached from the `UpdateAsync` path, meaning "leave the stored secret alone"; the `application is null` branch always passes a real secret to `CreateAsync`.

`permissions.ToHashSet().SetEquals(...)` — `GetPermissionsAsync` returns `ValueTask<ImmutableArray<string>>` (confirmed by reflection during planning), which has no `.SetEquals` of its own; `.ToHashSet()` first is required, not stylistic.

**`context` (`NorseIdentityDbContext`) is dropped from the constructor entirely** — the reviewed draft injected it but `SeedAsync` never reads it (every operation goes through `applicationManager`, which is itself backed by the context internally via OpenIddict's own EF store). An unread primary-constructor parameter is CS9113 in this warnings-as-errors realm; the fix is removing the parameter, not silencing the warning. `GetDisplayNameAsync(application, ct)` is confirmed present on the installed 7.6.0 `OpenIddictApplicationManager<TApplication>` (same reflection probe as the rest of this plan's OpenIddict references) and is now part of the reconciliation comparison — a stale `DisplayName` alone now triggers an update, closing the gap where it was set but never compared.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Identity.Migrations.Tests -- --filter-class "*.MachineClientSeedContributorTests"`
Expected: PASS (needs Docker).

- [ ] **Step 5: Run the whole realm's build and test suite**

Run: `dotnet build Himinbjorg.slnx`
Expected: SUCCESS.

Run (needs Docker): `dotnet test Himinbjorg.slnx`
Expected: SUCCESS — including the existing `NorseIdentityMigrationContributorContainerTests`/`NorseIdentityTemporalApparatusContainerTests` in the same `"Postgres"` collection, since they share the one container this task's tests now also use.

- [ ] **Step 6: Stage (no commit — human commits)**

```bash
git add src/Identity.Migrations/MachineClientSeedContributor.cs \
  tests/Identity.Migrations.Tests/MachineClientSeedContributorTests.cs
```

## SHIP GATE — Himinbjörg

PR opened from `feature/machine-authn`, CI green, merged to `master`.

---

## Phase 4 — Bifröst (direct to `master`, no fork, per CLAUDE.md §7's AppHost exception)

### Task 7: The certificate `ParameterDefault` and its unit test

**Files:**
- Create: `src/Orchestration.AppHost/OidcSigningCertificateParameterDefault.cs`
- Create: `tests/Orchestration.AppHost.Tests/Orchestration.AppHost.Tests.csproj`
- Create: `tests/Orchestration.AppHost.Tests/OidcSigningCertificateParameterDefaultTests.cs`

**Interfaces:**
- Produces: `OidcSigningCertificateParameterDefault : ParameterDefault`, `GetDefaultValue() : string` returning a base64-encoded, empty-password PKCS12 (PFX).

- [ ] **Step 1: Scaffold the test project**

`tests/Orchestration.AppHost.Tests/Orchestration.AppHost.Tests.csproj` (self-contained — Bifröst has no existing `Directory.Test.props` to inherit from, and one project does not justify inventing shared test infrastructure):

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<TargetFramework>net11.0</TargetFramework>
		<IsPackable>false</IsPackable>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Aspire.Hosting" Version="13.*" />
		<PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" Version="*" />
		<PackageReference Include="Microsoft.Testing.Platform.MSBuild" Version="*" />
		<PackageReference Include="Shouldly" Version="*" />
		<PackageReference Include="xunit.v3.mtp-v2" Version="*" />
		<Using Include="Shouldly" />
		<Using Include="Xunit" />
	</ItemGroup>
	<ItemGroup>
		<ProjectReference Include="../../src/Orchestration.AppHost/Orchestration.AppHost.csproj" />
	</ItemGroup>
</Project>
```

Add this project to `Bifrost.slnx` in an `/AppHost/tests/` (or equivalent) solution folder, matching the existing folder-per-layer convention visible in the file already read during planning.

- [ ] **Step 2: Write the failing test**

`tests/Orchestration.AppHost.Tests/OidcSigningCertificateParameterDefaultTests.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using Norse.Orchestration.AppHost;

namespace Norse.Orchestration.AppHost.Tests;

public sealed class OidcSigningCertificateParameterDefaultTests
{
	[Fact]
	void GetDefaultValue_returns_a_loadable_empty_password_PFX()
	{
		OidcSigningCertificateParameterDefault subject = new();

		var base64 = subject.GetDefaultValue();

		using var certificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(base64), password: null);
		certificate.HasPrivateKey.ShouldBeTrue();
	}

	[Fact]
	void GetDefaultValue_returns_a_certificate_valid_right_now()
	{
		OidcSigningCertificateParameterDefault subject = new();

		using var certificate = X509CertificateLoader.LoadPkcs12(
			Convert.FromBase64String(subject.GetDefaultValue()), password: null);

		certificate.NotBefore.ShouldBeLessThanOrEqualTo(DateTime.Now);
		certificate.NotAfter.ShouldBeGreaterThan(DateTime.Now);
	}

	[Fact]
	void Two_calls_generate_two_independent_certificates()
	{
		// Every call to AddParameter's ParameterDefault only runs once per process in practice (Aspire
		// persists the first result), but GetDefaultValue itself must not memoize incorrectly -- two
		// independent instances must not collide on thumbprint.
		OidcSigningCertificateParameterDefault first = new();
		OidcSigningCertificateParameterDefault second = new();

		using var firstCert = X509CertificateLoader.LoadPkcs12(
			Convert.FromBase64String(first.GetDefaultValue()), password: null);
		using var secondCert = X509CertificateLoader.LoadPkcs12(
			Convert.FromBase64String(second.GetDefaultValue()), password: null);

		firstCert.Thumbprint.ShouldNotBe(secondCert.Thumbprint);
	}
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/Orchestration.AppHost.Tests -- --filter-class "*.OidcSigningCertificateParameterDefaultTests"`
Expected: FAIL — `OidcSigningCertificateParameterDefault` doesn't exist.

- [ ] **Step 4: Implement**

`src/Orchestration.AppHost/Orchestration.AppHost.csproj` — add to the `<ItemGroup>` so the new test project (Step 1) can construct the `internal` class below directly, matching the platform's standard `InternalsVisibleTo` law:

```xml
<ItemGroup>
	<InternalsVisibleTo Include="Orchestration.AppHost.Tests" />
</ItemGroup>
```

`src/Orchestration.AppHost/OidcSigningCertificateParameterDefault.cs`:

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;

namespace Norse.Orchestration.AppHost;

/// <summary>
///     Generates the local-dev OpenIddict signing/encryption certificate (Himinbjorg#49 spec §2.3) — one
///     self-signed cert exported as an empty-password PFX, base64-encoded. Empty password is deliberate:
///     the parameter carrying this value is already <c>secret: true, persist: true</c>, so a second
///     secret protecting the PFX container itself adds no defense-in-depth for a local-dev, same-process
///     credential. Reused as both signing and encryption certificate — a single generation callback
///     avoids the unsolvable coordination problem of two independently-generated parameters (a PFX and
///     its password) that cannot reach each other.
/// </summary>
sealed class OidcSigningCertificateParameterDefault : ParameterDefault
{
	/// <inheritdoc />
	public override string GetDefaultValue()
	{
		using var rsa = RSA.Create(2048);
		var request = new CertificateRequest("CN=Norse OpenIddict (local dev)", rsa, HashAlgorithmName.SHA256,
			RSASignaturePadding.Pkcs1);
		using var certificate = request.CreateSelfSigned(
			DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(2));

		return Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, password: string.Empty));
	}

	/// <inheritdoc />
	public override void WriteToManifest(ManifestPublishingContext context) =>
		// Dev-only generated secret, exactly like postgres-password's own posture -- never a publish
		// concern until this AppHost actually targets a cloud publish profile, which it does not today.
		throw new NotSupportedException(
			"The OpenIddict signing certificate is a local-dev-only generated secret and is not manifest-publishable.");
}
```

**Note for the implementing task:** confirm `ParameterDefault`'s exact member set (`GetDefaultValue()` and `WriteToManifest(ManifestPublishingContext)` were verified present in the installed Aspire 13.5.2 XML docs during planning; whether either is `abstract` vs. `virtual`, and whether any third member exists, is a one-line compiler check the build itself will surface immediately — fix based on the actual compile error, not by re-guessing here).

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Orchestration.AppHost.Tests -- --filter-class "*.OidcSigningCertificateParameterDefaultTests"`
Expected: PASS

- [ ] **Step 6: Build the whole AppHost**

Run: `dotnet build Bifrost.slnx`
Expected: SUCCESS.

- [ ] **Step 7: Stage (no commit — human commits per repo law)**

```bash
git add src/Orchestration.AppHost/OidcSigningCertificateParameterDefault.cs \
  tests/Orchestration.AppHost.Tests/Orchestration.AppHost.Tests.csproj \
  tests/Orchestration.AppHost.Tests/OidcSigningCertificateParameterDefaultTests.cs \
  Bifrost.slnx
```

### Task 8: Wire the two parameters into the AppHost

**Files:**
- Modify: `src/Orchestration.AppHost/AppHost.cs`

**Interfaces:**
- Consumes: `OidcSigningCertificateParameterDefault` (Task 7).
- Produces: `web` project resource receives `OIDC_SIGNING_CERT_PFX`; `migrations` project resource receives `OIDC_MACHINE_CLIENT_SECRET`.

- [ ] **Step 1: Implement (no new automated test — this step is AppHost composition wiring, verified by the manual run in Step 2; the risky logic it depends on, certificate generation, already has its own unit test from Task 7)**

`src/Orchestration.AppHost/AppHost.cs` — insert after the existing `pgPassword` parameter declaration (before `pgPrimary`), and modify the `web`/`migrations` project registrations:

```csharp
using Aspire.Hosting;
using Norse.Orchestration.AppHost;

Console.Title = "Norse Architecture — Aspire App Host";

var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL primary + streaming replica. ... (unchanged)
var pgPassword = builder.AddParameter("postgres-password", secret: true);

// OpenIddict signing/encryption certificate and the seeded machine client's secret (Himinbjorg#49 spec
// §2.3) -- generated once, persisted to this AppHost's user secrets, stable across restarts.
var oidcSigningCertPfx = builder.AddParameter("oidc-signing-cert-pfx",
	new OidcSigningCertificateParameterDefault(), secret: true, persist: true);
var oidcMachineClientSecret = builder.AddParameter("oidc-machine-client-secret",
	new GenerateParameterDefault(), secret: true, persist: true);

var pgPrimary = builder
	.AddPostgres("pg-primary", password: pgPassword, port: 5432)
	// ... (unchanged)
```

```csharp
var migrationsService = builder
	.AddProject<Projects.Hosting_Migrations_Service>("migrations")
	.WithReference(norseIdentity, connectionName: "norse_identity")
	.WithReference(norseReference, connectionName: "norse_reference")
	.WithEnvironment("OIDC_MACHINE_CLIENT_SECRET", oidcMachineClientSecret)
	.WaitFor(norseIdentity)
	.WaitFor(norseReference);

builder
	.AddProject<Projects.Hosting_Web_Server>("web")
	.WithReference(norseIdentity, connectionName: "norse_identity")
	.WithReference(norseReference, connectionName: "norse_reference")
	.WithEnvironment("OIDC_SIGNING_CERT_PFX", oidcSigningCertPfx)
	.WaitFor(norseIdentity)
	.WaitFor(norseReference)
	.WaitForCompletion(migrationsService);
```

**Note for the implementing task:** `new GenerateParameterDefault()` for the client secret is Aspire's own built-in password generator — confirmed as a settable-properties type (`MinLength`, `Lower`, `Upper`, `Numeric`, `Special`, `MinLower`, `MinUpper`, `MinNumeric`, `MinSpecial`, all documented) with no documented custom constructor, so the compiler-generated parameterless constructor applies and the defaults are sensible per its own 128-bit-entropy remarks — `new GenerateParameterDefault()` with no property initializers is correct as written.

- [ ] **Step 2: Manual verification — run the AppHost**

Run: `dotnet run --project src/Orchestration.AppHost`
Expected: the Aspire dashboard comes up; `web` and `migrations` resources start without a missing-configuration error; the two new parameters appear in the AppHost's resource list. This is the AppHost's own "clone once and run" proof — no automated test replaces actually running it, matching how the Postgres primary+replica wiring above it was itself verified (per this repo's existing CLAUDE.md state-of-the-union entries, which describe verification as "run `dotnet run --project src/Orchestration.AppHost` today").

- [ ] **Step 3: Stage (no commit — human commits)**

```bash
git add src/Orchestration.AppHost/AppHost.cs
```

Present the full staged diff (Tasks 7 + 8 together) to the user for review before they commit — this is the one phase with no PR/CI gate, so the human review at commit time is the gate.

---

## Phase 5 — Yggdrasil

**Fork:** `feature/machine-authn`, from current `master` (`ce74ad8`).

### Task 9: Compose the real host — cert decoding, `AddNorseAuthenticationService` call site, endpoint mapping, OpenAPI transformers

**Files:**
- Modify: `src/Hosting.Web.Server/Program.cs`

**Interfaces:**
- Consumes: `AddNorseAuthenticationService(string, X509Certificate2)` (Phase 3 Task 5); `MapNorseOpenIddictEndpoints()` (Phase 3 Task 5); `BearerSecuritySchemeTransformer`/`MachineAuthOperationTransformer` (Phase 2 Task 4).

This task has no new automated test of its own — it is composition-root wiring. Its correctness is verified two ways: a successful build (below) proves it compiles against Phase 2-4's new signatures, and Task 10 proves every *existing* test that boots the real `Program.cs` still passes with the new required configuration in place; the new *behavioral* proof (a real token reaching a real facade endpoint) is Task 11's, against a bespoke composition root that calls these same production DI extensions directly rather than through `Program.cs` — see Task 11's note on why `WebApplicationFactory<Program>` cannot support that proof.

- [ ] **Step 1: Implement**

`src/Hosting.Web.Server/Program.cs` — add the using, decode the cert, thread it through, add the endpoint mapping and transformers:

```csharp
using System.Security.Cryptography.X509Certificates;
// ... (existing usings, unchanged) ...

// ... (existing setup, unchanged, through the AddRazorComponents block) ...

var norseReferenceConnectionString = builder.Configuration.GetConnectionString("norse_reference")
	?? throw new InvalidOperationException("Connection string 'norse_reference' is not configured.");
var oidcSigningCertPfx = builder.Configuration["OIDC_SIGNING_CERT_PFX"]
	?? throw new InvalidOperationException("Configuration value 'OIDC_SIGNING_CERT_PFX' is not configured.");
var oidcSigningCertificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(oidcSigningCertPfx), password: null);
builder
	.AddNorseAuthenticationService("norse_identity", oidcSigningCertificate)
	.Services
	// ... (rest of this block, unchanged) ...
```

```csharp
builder.Services.AddOpenApi(options =>
{
	options.AddSchemaTransformer<ResultSchemaTransformer>();
	options.AddSchemaTransformer<EnumSchemaTransformer>();
	options.AddSchemaTransformer<XmlMetadataTransformer>();
	options.AddDocumentTransformer<UnionLeakGuardTransformer>();
	options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
	options.AddOperationTransformer<StandardResponsesTransformer>();
	options.AddOperationTransformer<MachineAuthOperationTransformer>();
});
```

And, alongside the existing `app.MapAdditionalIdentityEndpoints();` line:

```csharp
app.MapAdditionalIdentityEndpoints();
app.MapNorseOpenIddictEndpoints();
```

- [ ] **Step 2: Build**

Run: `dotnet build Yggdrasil.slnx`
Expected: SUCCESS — this is a compile-time check only (`dotnet build` never executes `Program.cs`'s top-level statements, so a missing runtime configuration value cannot fail it; that failure mode only shows up when a test actually *runs* the host, which is Task 10's job, not this step's).

- [ ] **Step 3: Staging deferred to the end of Task 11**

Do not stage yet — Tasks 9, 10, and 11 land together as one reviewable composition-plus-proof unit, since Task 10 depends on this task's `Program.cs` change to even boot, and Task 11 depends on both.

### Task 10: The test signing certificate — keeps every existing Program.cs-booting test passing

**Files:**
- Create: `tests/Hosting.Web.Server.Tests/Authentication/MachineAuthTestCertificate.cs`
- Modify: `tests/Hosting.Web.Server.Tests/TestHostEnvironment.cs`
- Modify: `tests/Hosting.Web.Server.Tests/CompositionTests.cs`

**Interfaces:**
- Produces: `MachineAuthTestCertificate.Base64Pfx` (a `static readonly string`, generated once per test-assembly load) — consumed by `TestHostEnvironment`'s module initializer.

**Scope, narrowed from the reviewed draft:** this task does not attempt a DB-backed behavioral proof — `Program.cs`'s pre-`Build()` connection-string reads make `WebApplicationFactory<Program>` fundamentally unable to point at a real Testcontainers database (there is no hook that runs before that read; only a process env var set before the factory's first build trigger can reach it, and this test assembly already relies on `TestHostEnvironment`'s fake connection strings for every other composition test). This task's only job: every *existing* test that boots the real `Program.cs` (`CompositionTests`, `LaneCompositionTests`) needs a signing certificate to exist now that `Program.cs` requires `OIDC_SIGNING_CERT_PFX` unconditionally — without it, `Program.cs`'s own pre-`Build()` read throws and every such test breaks, not just new ones. Task 11 carries the actual new behavioral proof, against a database-backed bespoke host that sidesteps this exact constraint (the same pattern `CountryLookupE2ETests` already established for the reference facade, extended to also cover identity).

- [ ] **Step 1: Implement the certificate (no separate test — its correctness is proven transitively by every existing test that boots `Program.cs`, run in Step 3)**

`tests/Hosting.Web.Server.Tests/Authentication/MachineAuthTestCertificate.cs`:

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Norse.Hosting.Web.Server.Tests.Authentication;

/// <summary>
///     The one self-signed cert every composition test in this project needs for OpenIddict to start —
///     generated once per test-assembly load, mirroring how <see cref="TestHostEnvironment" /> fakes
///     connection strings the same way. Empty PFX password, same rationale as the AppHost's own
///     <c>OidcSigningCertificateParameterDefault</c> (Bifröst): the value is already env-var-scoped to
///     this test process, and a second secret protecting it adds nothing.
/// </summary>
static class MachineAuthTestCertificate
{
	/// <summary>The base64-encoded, empty-password PFX — set as <c>OIDC_SIGNING_CERT_PFX</c> for tests that boot the real <c>Program.cs</c>.</summary>
	internal static readonly string Base64Pfx = ExportFresh();

	static string ExportFresh()
	{
		using var certificate = CreateFresh();
		return Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, password: string.Empty));
	}

	/// <summary>
	///     A freshly generated certificate object — for Task 11's bespoke fixture, which calls
	///     <c>AddNorseAuthenticationService</c> directly and needs an <see cref="X509Certificate2" />, not
	///     the base64 form <see cref="Base64Pfx" /> exists for.
	/// </summary>
	internal static X509Certificate2 CreateFresh()
	{
		using var rsa = RSA.Create(2048);
		var request = new CertificateRequest("CN=Norse Composition Tests", rsa, HashAlgorithmName.SHA256,
			RSASignaturePadding.Pkcs1);
		return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
	}
}
```

`tests/Hosting.Web.Server.Tests/TestHostEnvironment.cs` — add the one new env var `Program.cs` now reads unconditionally (no `OIDC_MACHINE_CLIENT_SECRET` here — `Program.cs` never reads that key; only the migrations service's seed contributor does, and that's a separate process entirely, not this test assembly's concern):

```csharp
using System.Runtime.CompilerServices;
using Norse.Hosting.Web.Server.Tests.Authentication;

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
		Environment.SetEnvironmentVariable("OIDC_SIGNING_CERT_PFX", MachineAuthTestCertificate.Base64Pfx);
	}
}
```

- [ ] **Step 2: Fix the now-stale comment in the existing credentialless-rejection test**

Modify `tests/Hosting.Web.Server.Tests/CompositionTests.cs` — in `A_credentialless_call_to_the_facade_is_rejected_before_content_negotiation_runs`, remove the sentence *"currently red for an unrelated pre-existing reason (`PrincipalAccessor.Seed` rejecting that fixture's GUID-less test principal)... not a claim this comment makes about its current pass/fail state"* — that caveat described a state that predates this story; the fact is meaningful again now that a real machine lane exists to be credentialless *against*. No behavior in this test changes — only the comment.

- [ ] **Step 3: Run the existing composition/lane test suites — the actual proof this task's own change didn't break anything**

Run: `dotnet build Yggdrasil.slnx`
Expected: SUCCESS.

Run: `dotnet test tests/Hosting.Web.Server.Tests -- --filter-class "*.CompositionTests"`
Expected: PASS — every existing fact, unchanged behavior.

Run: `dotnet test tests/Hosting.Web.Server.Tests -- --filter-class "*.LaneCompositionTests"`
Expected: PASS.

- [ ] **Step 4: Staging deferred to the end of Task 11**

### Task 11: The end-to-end proof — a real DB-backed host, JSON/XML round trip

**Files:**
- Create: `tests/Hosting.Web.Server.Tests/Authentication/MachineAuthPostgresFixture.cs`
- Create: `tests/Hosting.Web.Server.Tests/Authentication/MachineAuthE2ETests.cs`

**Interfaces:**
- Consumes: `MachineAuthTestCertificate` (Task 10); `NorseIdentityMigrationContributor` (Himinbjörg, existing); `NorseReferenceMigrationContributor`/`ReferenceDataSeedContributor` (Mímisbrunnr, existing — the exact contributors `CountryLookupE2ETests` already uses); `AddNorseAuthenticationService`/`AddNorseAuthentication`/`AddNorsePolicies`/`AddNorseReferenceService`/`AddWell<ReferenceDbContext>`/`AddNorsePipeline`/`MapNorseOpenIddictEndpoints` (all real production DI extensions, called directly).

**Why not `WebApplicationFactory<Program>`, stated plainly:** `Program.cs` reads every connection string and `OIDC_SIGNING_CERT_PFX` from configuration *before* `builder.Build()` — earlier than any `WithWebHostBuilder` hook can run (confirmed in this realm's own CLAUDE.md: *"Connection strings must be set as process env vars in a static constructor... env vars are the one override its pre-Build read can see"*). A real Testcontainers connection string is only known after the container starts asynchronously, which cannot happen inside a `[ModuleInitializer]`. There is no way to point the real `Program.cs` at a real per-test database through `WebApplicationFactory`. This is exactly the constraint `CountryLookupE2ETests` (confirmed by reading it directly during planning) already solved for the reference facade alone — it builds its own bespoke composition root via `WebApplication`/`TestServer`, calling the same production DI extensions `Program.cs` calls but passing the container's connection string as a plain method argument, no configuration indirection. This task extends that exact pattern to also cover identity/OpenIddict issuance, since proving `Program.cs` cannot be automated but proving the same *wiring* — the real extension methods, called the same way — can.

- [ ] **Step 1: Implement the fixture**

`tests/Hosting.Web.Server.Tests/Authentication/MachineAuthPostgresFixture.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Norse.Identity.EntityFramework;
using Norse.Identity.Migrations;
using Norse.Identity.Migrations.PostgreSQL;
using Norse.Identity.Web.Server;
using Norse.Infrastructure.Backend.Keys;
using Norse.Infrastructure.Persistence.EntityFramework;
using Norse.Infrastructure.Web.Server.Authentication;
using Norse.Infrastructure.Web.Server.Json;
using Norse.Infrastructure.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Xml;
using Norse.Persistence.EntityFramework;
using Norse.Persistence.EntityFramework.PostgreSQL;
using Norse.Reference.Data.EntityFramework;
using Norse.Reference.Data.EntityFramework.Migrations;
using Norse.Reference.Data.EntityFramework.Migrations.PostgreSQL;
using Norse.Reference.Web.Server;
using Testcontainers.PostgreSql;

namespace Norse.Hosting.Web.Server.Tests.Authentication;

/// <summary>
///     Two real Postgres containers (identity + reference), migrated and seeded through the exact same
///     contributors the migrations service runs, standing behind a real bespoke <see cref="TestServer" />
///     host wired from the same production DI extensions <c>Program.cs</c> calls — the
///     <see cref="Norse.Hosting.Web.Server.Tests.CountryLookupE2ETests" /> "own composition root" pattern
///     (confirmed by reading that fixture during planning), extended to also cover identity/OpenIddict
///     issuance, which <c>Program.cs</c>'s pre-<c>Build()</c> configuration reads make impossible to test
///     through <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}" />.
/// </summary>
public sealed class MachineAuthPostgresFixture : IAsyncLifetime
{
	readonly PostgreSqlContainer _identityContainer = new PostgreSqlBuilder("postgres:19beta2")
		.WithDatabase("norse_identity")
		.Build();

	readonly PostgreSqlContainer _referenceContainer = new PostgreSqlBuilder("postgres:19beta2")
		.WithDatabase("norse_reference")
		.Build();

	X509Certificate2 _certificate = null!;
	string _keysRoot = null!;

	public string IdentityConnectionString { get; private set; } = null!;
	public string ReferenceConnectionString { get; private set; } = null!;

	public async ValueTask InitializeAsync()
	{
		await Task.WhenAll(_identityContainer.StartAsync(), _referenceContainer.StartAsync());
		IdentityConnectionString = _identityContainer.GetConnectionString();
		ReferenceConnectionString = _referenceContainer.GetConnectionString();
		_certificate = MachineAuthTestCertificate.CreateFresh();
		// One directory, reused by every host CreateHostAsync builds -- same rationale as
		// PostgresIdentityFixture's own "exactly one instance" warning: EF's ModelSource cache keys the
		// compiled model by an options fingerprint that does not include the connection string, so two
		// hosts in this process could otherwise share a cached model built against one host's key seam
		// while believing they registered their own. Every host this fixture builds points at the same
		// connection string anyway (one container), so one shared keys directory is both correct and safe.
		_keysRoot = Path.Combine(Path.GetTempPath(), $"norse-identity-keys-{Guid.NewGuid():N}");

		DbContextOptionsBuilder<NorseIdentityDbContext> identityOptions = new();
		identityOptions.ApplyNorseProviderOptions(NorsePostgresEfProvider.Instance,
			IdentityConnectionString, typeof(NorseIdentityDbContextFactory).Assembly.GetName().Name);
		await using (NorseIdentityDbContext identityContext = new(identityOptions.Options))
			await new NorseIdentityMigrationContributor(identityContext).MigrateAsync(CancellationToken.None);

		DbContextOptionsBuilder<ReferenceDbContext> referenceOptions = new();
		referenceOptions.ApplyNorseProviderOptions(NorsePostgresEfProvider.Instance,
			ReferenceConnectionString, typeof(ReferenceDbContextFactory).Assembly.GetName().Name);
		await using ReferenceDbContext referenceContext = new(referenceOptions.Options);
		await new NorseReferenceMigrationContributor(referenceContext).MigrateAsync(CancellationToken.None);
		await new ReferenceDataSeedContributor(referenceContext).SeedAsync(CancellationToken.None);
	}

	public async ValueTask DisposeAsync()
	{
		_certificate.Dispose();
		await Task.WhenAll(_identityContainer.DisposeAsync().AsTask(), _referenceContainer.DisposeAsync().AsTask());
		if (Directory.Exists(_keysRoot))
			Directory.Delete(_keysRoot, recursive: true);
	}

	/// <summary>
	///     Boots a fresh, real <see cref="WebApplication" /> against this fixture's two containers, wiring
	///     the exact production DI extensions <c>Program.cs</c> calls for identity issuance/validation and
	///     the reference facade — same shape as <c>CountryLookupE2ETests.CreateHostAsync</c>, extended to
	///     cover OpenIddict.
	/// </summary>
	/// <param name="accessTokenLifetime">
	///     Overrides the issued access token's lifetime, for the expired-token test — omitted everywhere
	///     else, matching OpenIddict's own default.
	/// </param>
	public async Task<WebApplication> CreateHostAsync(TimeSpan? accessTokenLifetime = null)
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Configuration["ConnectionStrings:identity"] = IdentityConnectionString;
		builder.AddNorseAuthenticationService("identity", _certificate, accessTokenLifetime);
		builder.Services
			.AddNorseDevelopmentKeys(_keysRoot)
			.AddNorseReferenceService(ReferenceConnectionString)
			.AddWell<ReferenceDbContext>()
			.AddNorsePipeline()
			.AddNorseAuthentication()
			.AddNorsePolicies()
			.AddControllers(options => options.ReturnHttpNotAcceptable = true)
			.AddNorseJson(NorseEnumNameRegistration.Build())
			.AddNorseXml(XmlCaseStyle.CamelCase, NorseXmlShapeRegistration.Build());

		var app = builder.Build();
		app.UseAuthentication();
		app.UseAuthorization();
		app.MapControllers();
		app.MapNorseOpenIddictEndpoints();

		await app.StartAsync(CancellationToken.None);
		return app;
	}
}
```

**Notes for the implementing task, named explicitly rather than left silent:**
- `MachineAuthTestCertificate` needs a second entry point — `internal static X509Certificate2 CreateFresh()` returning a real `X509Certificate2` object (not the base64 string `Base64Pfx` already exposes for `TestHostEnvironment`), since this fixture calls `AddNorseAuthenticationService` directly rather than decoding from configuration. Add this method to Task 10's `MachineAuthTestCertificate.cs` in the same edit that creates this fixture, extracting the existing `Generate()` body into a method returning the certificate object and having both `Base64Pfx` and `CreateFresh()` call it.
- `NorseEnumNameRegistration.Build()`/`NorseXmlShapeRegistration.Build()` are the exact calls `Program.cs` already makes (confirmed in this plan's earlier research) — reused verbatim, not reinvented.
- `AddNorsePipeline()` is required because `CountriesController` calls `IReferenceService.GetCountry` in-process, which dispatches through the mediator chain like every other Norse request — `CountryLookupE2ETests` registers it for the identical reason.
- This fixture deliberately does not map gRPC endpoints or call `AddNorseCodeFirstGrpc()` — this task's tests only exercise the REST facade and the token endpoint, and #49's scope never touches the gRPC leg.

- [ ] **Step 2: Write the tests**

`tests/Hosting.Web.Server.Tests/Authentication/MachineAuthE2ETests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Norse.Hosting.Web.Server.Tests.Authentication;

[CollectionDefinition("MachineAuthPostgres")]
public sealed class MachineAuthPostgresCollection : ICollectionFixture<MachineAuthPostgresFixture>;

/// <summary>
///     The end-to-end proof (Himinbjorg#49 spec §7): a real OpenIddict-issued JWT reaches Mímir's
///     <c>CountriesController</c> in both JSON and XML — the wire shape that has never been proven end to
///     end before this story, per the spec's correction of the stale "host-wiring gap" premise (Mímir's
///     own CLAUDE.md line 37, which this plan's Phase 6 corrects).
/// </summary>
[Collection("MachineAuthPostgres")]
public sealed class MachineAuthE2ETests(MachineAuthPostgresFixture fixture)
{
	[Fact]
	async Task A_seeded_client_reaches_the_facade_in_JSON()
	{
		await using var app = await fixture.CreateHostAsync();
		await SeedClientAsync(app.Services, "json-test-client", "json-test-secret");
		using var client = app.GetTestServer().CreateClient();
		var token = await ObtainAccessTokenAsync(client, "json-test-client", "json-test-secret");

		using var request = new HttpRequestMessage(HttpMethod.Get, "/api/reference/countries/US");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

		using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");

		// The actual wire-shape proof, not just "some 200 came back" -- CountryResponse's real fields
		// (Reference.Contracts/CountryResponse.cs, [DataContract]/[DataMember], no explicit wire names, so
		// AddNorseJson's camelCase policy governs) round-trip correctly, including the flags-as-array law
		// (spec's inherited ruling, 2026-08-02-futhark-enum-wire-law-design.md): the US holds none of the
		// three UN classification flags, so Classification renders as an empty array, not a bare number and
		// not absent.
		using var document = JsonDocument.Parse(
			await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
		var root = document.RootElement;
		root.GetProperty("alpha2").GetString().ShouldBe("US");
		root.GetProperty("alpha3").GetString().ShouldBe("USA");
		root.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
		var classification = root.GetProperty("classification");
		classification.ValueKind.ShouldBe(JsonValueKind.Array);
		classification.GetArrayLength().ShouldBe(0);
	}

	[Fact]
	async Task A_seeded_client_reaches_the_facade_in_XML()
	{
		await using var app = await fixture.CreateHostAsync();
		await SeedClientAsync(app.Services, "xml-test-client", "xml-test-secret");
		using var client = app.GetTestServer().CreateClient();
		var token = await ObtainAccessTokenAsync(client, "xml-test-client", "xml-test-secret");

		using var request = new HttpRequestMessage(HttpMethod.Get, "/api/reference/countries/US");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

		using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.ShouldBe("application/xml");

		// Same wire-shape proof as the JSON fact, over the XML channel -- Program.cs configures
		// AddNorseXml(XmlCaseStyle.CamelCase, ...), so element names are camelCase, same as the JSON
		// property names. Looked up by local name (not a hardcoded root element name) so this doesn't
		// depend on a namespace/prefix assumption this plan's research didn't independently verify.
		var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		xml.Descendants().Single(e => e.Name.LocalName == "alpha2").Value.ShouldBe("US");
		xml.Descendants().Single(e => e.Name.LocalName == "alpha3").Value.ShouldBe("USA");
		xml.Descendants().Single(e => e.Name.LocalName == "name").Value.ShouldNotBeNullOrWhiteSpace();
		// Zero flags emits no child elements under classification (the write-side law) -- true whether the
		// parent element itself appears empty or is omitted, so this assertion doesn't need to resolve
		// which of those two the generator actually chose. The .Value check is load-bearing, not
		// redundant: .Elements() alone would also pass for the old, wrong scalar shape
		// (<classification>0</classification> has zero child *elements* but non-empty text) -- asserting
		// both together is what actually distinguishes "governed-name array, empty" from "bare number."
		foreach (var classification in xml.Descendants().Where(e => e.Name.LocalName == "classification"))
		{
			classification.Elements().ShouldBeEmpty();
			classification.Value.ShouldBe(string.Empty);
		}
	}

	[Fact]
	async Task An_invalid_token_is_rejected_bare()
	{
		await using var app = await fixture.CreateHostAsync();
		using var client = app.GetTestServer().CreateClient();

		using var request = new HttpRequestMessage(HttpMethod.Get, "/api/reference/countries/US");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

		using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
		(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
	}

	[Fact]
	async Task An_expired_token_is_rejected_bare()
	{
		// A one-second lifetime, minted for real through OpenIddict's own issuance path (never a
		// hand-constructed JWT outside it, which would prove nothing about this platform's actual expiry
		// handling) -- CreateHostAsync's accessTokenLifetime override exists for exactly this test.
		await using var app = await fixture.CreateHostAsync(accessTokenLifetime: TimeSpan.FromSeconds(1));
		await SeedClientAsync(app.Services, "expiry-test-client", "expiry-test-secret");
		using var client = app.GetTestServer().CreateClient();
		var token = await ObtainAccessTokenAsync(client, "expiry-test-client", "expiry-test-secret");
		await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

		using var request = new HttpRequestMessage(HttpMethod.Get, "/api/reference/countries/US");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

		using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
		(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
	}

	static async Task SeedClientAsync(IServiceProvider services, string clientId, string secret)
	{
		await using var scope = services.CreateAsyncScope();
		var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
		await manager.CreateAsync(new OpenIddictApplicationDescriptor
		{
			ClientId = clientId,
			ClientSecret = secret,
			ClientType = OpenIddictConstants.ClientTypes.Confidential,
			Permissions =
			{
				OpenIddictConstants.Permissions.Endpoints.Token,
				OpenIddictConstants.Permissions.GrantTypes.ClientCredentials
			}
		}, TestContext.Current.CancellationToken).ConfigureAwait(false);
	}

	static async Task<string> ObtainAccessTokenAsync(HttpClient client, string clientId, string secret)
	{
		using var response = await client.PostAsync(new Uri("/connect/token", UriKind.Relative),
			new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["grant_type"] = "client_credentials",
				["client_id"] = clientId,
				["client_secret"] = secret
			}), TestContext.Current.CancellationToken);
		response.EnsureSuccessStatusCode();
		var payload = await response.Content
			.ReadFromJsonAsync<TokenResponse>(TestContext.Current.CancellationToken)
			.ConfigureAwait(false);
		return payload!.AccessToken;
	}

	// Snake_case on the wire (access_token/token_type/expires_in) -- STJ's case-insensitive matching only
	// folds case, never underscores, so these attributes are required, not decorative (same fix as
	// Himinbjörg's OpenIddictTokenEndpointTests, Phase 3 Task 5).
	sealed record TokenResponse(
		[property: JsonPropertyName("access_token")] string AccessToken,
		[property: JsonPropertyName("token_type")] string TokenType,
		[property: JsonPropertyName("expires_in")] int ExpiresIn);
}
```

**`CountryResponse`'s wire field names, confirmed by reading `Reference.Contracts/CountryResponse.cs` directly during planning:** `Alpha2`, `Alpha3`, `Name`, `Classification` (`[DataContract]`/`[DataMember(Order = N)]`, no explicit wire-name overrides) — so `AddNorseJson`/`AddNorseXml`'s camelCase policy (matching `Program.cs`'s own `AddNorseXml(XmlCaseStyle.CamelCase, ...)` call) governs both channels identically: `alpha2`/`alpha3`/`name`/`classification`. The US is not a Least Developed Country, Land Locked Developing Country, or Small Island Developing State, so `Classification` is `None` — the empty-array assertion above is a real, checkable fact about this specific seeded row, not an assumption.

Each test seeds its own distinct client id/secret pair rather than sharing one — the fixture's two containers persist across every test in this class, so distinct identifiers avoid any cross-test collision without needing per-test cleanup (unlike Himinbjörg's `MachineClientSeedContributorTests`, which must clean up because `MachineClientSeedContributor.ClientId` is a fixed production constant; these are ad hoc test-only clients with no such constraint).

**Reconciliation rotation is deliberately not re-tested here.** It is fully proven, with a real observable no-write assertion, in Himinbjörg's `MachineClientSeedContributorTests` (Phase 3 Task 6) — the actual seed contributor, actually invoked. A prior draft of this task attempted a "three-run" proof at this layer by calling `IOpenIddictApplicationManager.CreateAsync`/`UpdateAsync` directly, which does not invoke `MachineClientSeedContributor` at all (it isn't even reachable from `Hosting.Web.Server` — it runs in the separate migrations-service process) and therefore proved nothing about reconciliation; removed rather than kept as a misleading pass.

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/Hosting.Web.Server.Tests -- --filter-class "*.MachineAuthE2ETests"`
Expected: PASS (needs Docker — two real Testcontainers Postgres instances).

- [ ] **Step 4: Run the whole realm's test suite**

Run: `dotnet build Yggdrasil.slnx && dotnet test Yggdrasil.slnx`
Expected: SUCCESS.

- [ ] **Step 5: Stage Tasks 9, 10, and 11 together (no commit — human commits)**

```bash
git add src/Hosting.Web.Server/Program.cs \
  tests/Hosting.Web.Server.Tests/Authentication/MachineAuthTestCertificate.cs \
  tests/Hosting.Web.Server.Tests/TestHostEnvironment.cs \
  tests/Hosting.Web.Server.Tests/CompositionTests.cs \
  tests/Hosting.Web.Server.Tests/Authentication/MachineAuthPostgresFixture.cs \
  tests/Hosting.Web.Server.Tests/Authentication/MachineAuthE2ETests.cs
```

## SHIP GATE — Yggdrasil

PR opened from `feature/machine-authn`, CI green, merged to `master`.

---

## Phase 6 — Mímir

**Fork:** `feature/machine-authn`, from current `master` (`0954529`).

### Task 12: Correct the stale CLAUDE.md passage

**Files:**
- Modify: `CLAUDE.md`

**Interfaces:** none — documentation only, no code change, no test.

- [ ] **Step 1: Edit line 37**

Replace:

> **`CountryResponse` digs the whole document, and `Classification` rides as the bare `[Flags]` member** (2026-08-09 amendment to the enum wire law — the channels translate; no interim "kind" enums, no record wrap, no handler mapping). Live today: the gRPC leg's composed varint. **Pending in Midgard before the facade can be host-wired:** the text channels' governed-name array form (JSON converters, XML shape generator) and the deletion of NORSE029, plus the shape generator's closure-walk widening to referenced assemblies — until both land, exposing this response through a shape-generating host strikes the diagnostic/tripwire by design, not by accident.

With:

> **`CountryResponse` digs the whole document, and `Classification` rides as the bare `[Flags]` member** (2026-08-09 amendment to the enum wire law — the channels translate; no interim "kind" enums, no record wrap, no handler mapping). The governed-name array form (JSON converters, XML shape generator), the deletion of NORSE029, and the shape generator's closure-walk widening to referenced assemblies all landed the same evening in Midgard commit `aba802f3` — the capability gap this passage used to describe is closed. Live today, proven end to end: `CountriesController` reachable through the real host in JSON, XML, and gRPC alike, authenticated by Himinbjörg#49's machine bearer flow (`Glitnir/docs/Platform/specs/2026-08-23-machine-authn-client-credentials-design.md` §6).

- [ ] **Step 2: Verify no other passage in this file references the now-resolved gap**

Run: `grep -n "Pending in Midgard\|host-wired\|strikes the diagnostic" CLAUDE.md`
Expected: no remaining hits describing the closed gap as open.

- [ ] **Step 3: Stage (no commit — human commits)**

```bash
git add CLAUDE.md
```

## SHIP GATE — Mímir

PR opened from `feature/machine-authn`, CI green, merged to `master`.

---

## Final Verification

After all six ship gates clear:

- [ ] Run `dotnet run --project src/Orchestration.AppHost` from a clean checkout (fresh volumes) and confirm the Aspire dashboard shows `web` and `migrations` healthy, with the two new parameters generated and visible.
- [ ] From the dashboard or a terminal, obtain a token: `curl -X POST https://localhost:<web-port>/connect/token -d grant_type=client_credentials -d client_id=norse-machine -d client_secret=<the generated oidc-machine-client-secret value>` and confirm a JWT comes back.
- [ ] Call `GET https://localhost:<web-port>/api/reference/countries/US` with that bearer token, once with `Accept: application/json` and once with `Accept: application/xml`, and confirm both succeed.
- [ ] Call the same endpoint with no `Authorization` header and confirm a bare 401.
- [ ] Confirm no literal secret value was committed — `git log --grep` searches commit *messages*, not file contents, so it proves nothing here. Instead, in each touched realm, scan the actual tracked content of every commit this plan adds: `git log --all -p -- '*.cs' '*.csproj' '*.md' | grep -inE "client_secret\s*=\s*[\"'][a-z0-9]|BEGIN (RSA |EC )?PRIVATE KEY|oidc-machine-client-secret\s*=\s*[\"'][a-z0-9]"` (adjust the path filter per realm) — expect zero hits. Only parameter *names* and configuration *keys* (`OIDC_MACHINE_CLIENT_SECRET`, `oidc-machine-client-secret`) are allowed to appear; an actual secret value never should.
