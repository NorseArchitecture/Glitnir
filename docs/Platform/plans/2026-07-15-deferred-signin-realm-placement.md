# Deferred Sign-In Realm Placement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (default for this platform — not a recommendation among equals; superpowers:executing-plans is the narrow separate-session fallback) paired with superpowers:test-driven-development on every task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Relocate `IDeferredSignIn` and its consumer so Himinbjörg never depends on Midgard for this feature, and so Himinbjörg's `Identity` project — shared with `Identity.Migrations` console tooling — never carries ASP.NET-Core-web-hosting concerns.

**Architecture:** Contract (`IDeferredSignIn`, `DeferredSignInAction`) moves to Asgard's `Abstractions.Web.Server` (declared law). Midgard's `Infrastructure.Web.Server` keeps the `MemoryCacheDeferredSignIn` implementation, now implementing Asgard's contract instead of declaring its own. Himinbjörg's `NorseSignInManager` moves from the base `Identity` project (shared with migration tooling) into `Identity.Web.Server` (the ASP.NET-Core-hosting-only project), which already NorseRefs Asgard's `Abstractions.Web.Server` for the `Outcome<T>` mediator law — the `IDeferredSignIn` contract rides on that same existing edge. Yggdrasil's `Hosting.Web.Server` gets an explicit NorseRef to Midgard's `Infrastructure.Web.Server` for the implementation it already consumes transitively.

**Tech Stack:** .NET, ASP.NET Core Identity, xUnit + Shouldly + NSubstitute, NorseRef (this platform's cross-repo dependency mechanism).

**Spec:** `../specs/2026-07-15-deferred-signin-realm-placement-design.md`

## Global Constraints

- Tabs for indentation; `omit_if_default` accessibility modifiers.
- `sealed` by default for every new type.
- US English spelling in code, comments, docs, commit messages.
- No automatic git commits — stage each task's changes and stop; the human commits.
- No skipping git hooks, no force-push, no committing secrets.
- Each task in this plan corresponds to one realm's own ship gate (PR, CI green, tag, NuGet publish) — **Task 2 must not start implementation against a published Task 1 NuGet version out of order; local `-p:UseProjectReferences=true` builds during development are fine, but do not consider a task "done" until its own realm's gate clears.**
- `Version="*"` floating package references — this platform's existing convention, keep it.

---

### Task 1: Asgard — add `IDeferredSignIn` contract to `Abstractions.Web.Server`

**Files:**
- Modify: `Asgard/src/Abstractions.Web.Server/Abstractions.Web.Server.csproj`
- Create: `Asgard/src/Abstractions.Web.Server/DeferredSignIn/IDeferredSignIn.cs`
- Create: `Asgard/tests/Abstractions.Web.Server.Tests/DeferredSignIn/DeferredSignInActionTests.cs`

**Interfaces:**
- Consumes: nothing new (existing `Abstractions.Backend` project reference only).
- Produces: `Norse.Abstractions.Web.Server.DeferredSignIn.IDeferredSignIn` (`StashSignIn(string, ClaimsPrincipal, AuthenticationProperties) : string`, `StashSignOut(string) : string`, `TryConsume(string, out DeferredSignInAction) : bool`) and `Norse.Abstractions.Web.Server.DeferredSignIn.DeferredSignInAction` (sealed record: `Scheme`, `SignOut`, `Principal`, `Properties`) — consumed by Task 2 (Midgard) and Task 3 (Himinbjörg).

- [ ] **Step 1: Write the failing test**

Create `Asgard/tests/Abstractions.Web.Server.Tests/DeferredSignIn/DeferredSignInActionTests.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Norse.Abstractions.Web.Server.DeferredSignIn;

namespace Norse.Abstractions.Web.Server.Tests.DeferredSignIn;

public sealed class DeferredSignInActionTests
{
	[Fact]
	void Constructor_round_trips_all_properties()
	{
		var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "buvy")]));
		var properties = new AuthenticationProperties { IsPersistent = true };

		var action = new DeferredSignInAction("Identity.Application", SignOut: false, principal, properties);

		action.Scheme.ShouldBe("Identity.Application");
		action.SignOut.ShouldBeFalse();
		action.Principal.ShouldBeSameAs(principal);
		action.Properties.ShouldBeSameAs(properties);
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `Asgard/`): `dotnet test tests/Abstractions.Web.Server.Tests/Abstractions.Web.Server.Tests.csproj`
Expected: FAIL — `Norse.Abstractions.Web.Server.DeferredSignIn` namespace / `DeferredSignInAction` type does not exist (compile error).

- [ ] **Step 3: Switch to the Web SDK instead of bolting on an explicit framework reference**

`IDeferredSignIn` needs `ClaimsPrincipal`/`AuthenticationProperties`, which this project has never referenced before (its existing contracts — `Outcome`, `Problem`, `BoolResponse`, `IRequestHandler`, `ICommandRequest`, `ErrorCategory` — are plain BCL types). Rather than add an explicit `<FrameworkReference>` to a plain `Sdk="Microsoft.NET.Sdk"` project, switch the project's SDK itself to `Microsoft.NET.Sdk.Web` — it implies the ASP.NET Core shared framework automatically, matching the precedent already set on Himinbjörg's `Identity.Web.Server.csproj` (plain SDK + explicit `FrameworkReference` → `Sdk.Web`, no manual framework wiring). Modify `Asgard/src/Abstractions.Web.Server/Abstractions.Web.Server.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
	<PropertyGroup>
		<Description>Norse web-server abstractions: IWebHostPlugin, the document repository surface (IDocumentRepository&lt;T&gt;), and mediator law (ICommandRequest&lt;T&gt;, validator and authorizer contracts) — the server-side law for the web tier. Mutually invisible with Norse.Abstractions.Worker.</Description>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../Abstractions.Backend/Abstractions.Backend.csproj" />
	</ItemGroup>
</Project>
```

(This flows transitively into `Abstractions.Web.Server.Tests.csproj` via its existing `ProjectReference` — no test-project csproj edit needed, matching how Midgard's `Infrastructure.Web.Server.Tests` already gets `Microsoft.AspNetCore.App` for free the same way. If `Sdk.Web`'s implicit global usings make any existing `using` line in this project redundant — IDE0005 under this platform's warnings-as-errors — remove it, same fallout Himinbjörg's `Identity.Web.Server` switch already hit.)

- [ ] **Step 4: Write the contract**

Create `Asgard/src/Abstractions.Web.Server/DeferredSignIn/IDeferredSignIn.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Norse.Abstractions.Web.Server.DeferredSignIn;

/// <summary>
/// Stashes a sign-in or sign-out that cannot complete on the current request (an already-established
/// Blazor Server interactive circuit, where <c>HttpContext.Response.HasStarted</c> is already true) so
/// it can be completed on a genuine, later HTTP request instead. Zero domain knowledge — reusable by
/// any future realm hosting cookie-based auth behind an interactive Blazor Server component.
/// </summary>
public interface IDeferredSignIn
{
	/// <summary>Stashes a pending sign-in. Returns a one-time completion key.</summary>
	string StashSignIn(string scheme, ClaimsPrincipal principal, AuthenticationProperties properties);

	/// <summary>Stashes a pending sign-out. Returns a one-time completion key.</summary>
	string StashSignOut(string scheme);

	/// <summary>Consumes (and removes) a completion key. Returns false if the key is unknown or expired.</summary>
	bool TryConsume(string key, out DeferredSignInAction action);
}

/// <summary>What to do to complete a deferred sign-in/out. <see cref="Principal"/> is null for sign-out.</summary>
public sealed record DeferredSignInAction(string Scheme, bool SignOut, ClaimsPrincipal? Principal, AuthenticationProperties? Properties);
```

- [ ] **Step 5: Run test to verify it passes**

Run (from `Asgard/`): `dotnet test tests/Abstractions.Web.Server.Tests/Abstractions.Web.Server.Tests.csproj`
Expected: PASS (including the full existing `Abstractions.Web.Server.Tests` suite — `BoolResponseTests`, `OutcomeTests`, `IRequestHandlerTests`, `WiringTests` — regression-free).

- [ ] **Step 6: Commit**

```bash
git add src/Abstractions.Web.Server/Abstractions.Web.Server.csproj src/Abstractions.Web.Server/DeferredSignIn tests/Abstractions.Web.Server.Tests/DeferredSignIn
git commit -m "feat: add IDeferredSignIn contract to Abstractions.Web.Server"
```

**Ship this realm's gate (PR, CI green, tag, NuGet publish) before starting Task 2.**

---

### Task 2: Midgard — repoint `Infrastructure.Web.Server`'s deferred-sign-in implementation at Asgard's contract

**Files:**
- Delete: `Midgard/src/Infrastructure.Web.Server/DeferredSignIn/IDeferredSignIn.cs`
- Modify: `Midgard/src/Infrastructure.Web.Server/DeferredSignIn/MemoryCacheDeferredSignIn.cs`
- Modify: `Midgard/src/Infrastructure.Web.Server/DeferredSignIn/DeferredSignInEndpointRouteBuilderExtensions.cs`
- Modify: `Midgard/src/Infrastructure.Web.Server/DeferredSignIn/ServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: Task 1's `Norse.Abstractions.Web.Server.DeferredSignIn.IDeferredSignIn` / `DeferredSignInAction` (Midgard's `Infrastructure.Web.Server.csproj` already NorseRefs `Asgard/Abstractions.Web.Server` — no csproj change needed).
- Produces: `Norse.Infrastructure.Web.Server.DeferredSignIn.MemoryCacheDeferredSignIn`, `.ServiceCollectionExtensions.AddDeferredSignIn()`, `.DeferredSignInEndpointRouteBuilderExtensions.MapDeferredSignIn()` / `.DefaultPattern` — names unchanged, consumed by Task 4 (Yggdrasil).

This is a pure repoint — no behavior changes, so there is no new red/green cycle. Verify via the existing test suite instead.

- [ ] **Step 1: Delete the now-duplicate contract**

```bash
git rm src/Infrastructure.Web.Server/DeferredSignIn/IDeferredSignIn.cs
```

- [ ] **Step 2: Repoint the implementation at Asgard's contract**

Modify `Midgard/src/Infrastructure.Web.Server/DeferredSignIn/MemoryCacheDeferredSignIn.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Norse.Abstractions.Web.Server.DeferredSignIn;

namespace Norse.Infrastructure.Web.Server.DeferredSignIn;

sealed class MemoryCacheDeferredSignIn(IMemoryCache cache) : IDeferredSignIn
{
	static readonly MemoryCacheEntryOptions _entryOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) };

	readonly IMemoryCache _cache = cache;

	public string StashSignIn(string scheme, ClaimsPrincipal principal, AuthenticationProperties properties)
	{
		var key = Guid.NewGuid().ToString();
		_cache.Set(key, new DeferredSignInAction(scheme, SignOut: false, principal, properties), _entryOptions);
		return key;
	}

	public string StashSignOut(string scheme)
	{
		var key = Guid.NewGuid().ToString();
		_cache.Set(key, new DeferredSignInAction(scheme, SignOut: true, null, null), _entryOptions);
		return key;
	}

	public bool TryConsume(string key, out DeferredSignInAction action)
	{
		if (!_cache.TryGetValue(key, out DeferredSignInAction? found) || found is null)
		{
			action = null!;
			return false;
		}
		_cache.Remove(key);
		action = found;
		return true;
	}
}
```

- [ ] **Step 3: Repoint the endpoint extension**

Modify `Midgard/src/Infrastructure.Web.Server/DeferredSignIn/DeferredSignInEndpointRouteBuilderExtensions.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Norse.Abstractions.Web.Server.DeferredSignIn;

namespace Norse.Infrastructure.Web.Server.DeferredSignIn;

/// <summary>Completion-endpoint wiring for a deferred sign-in/out.</summary>
public static class DeferredSignInEndpointRouteBuilderExtensions
{
	/// <summary>The default route pattern <see cref="MapDeferredSignIn"/> maps — callers building a completion URL reference this rather than duplicating the literal.</summary>
	public const string DefaultPattern = "/_auth/complete";

	/// <summary>
	/// Maps the completion endpoint for a deferred sign-in/out — a plain minimal-API endpoint (a real,
	/// distinct HTTP request, not a Blazor component), safe to write cookies from. Responds with a
	/// meta-refresh page rather than a redirect: mobile Chrome has a long-standing bug where it silently
	/// drops Set-Cookie on a 302, which would otherwise loop forever.
	/// </summary>
	[SuppressMessage("Trimming", "IL2026", Justification = "MapGet's delegate overload reflects over the supplied delegate's parameters; the delegate here is a fixed, statically-known shape.")]
	[SuppressMessage("AOT", "IL3050", Justification = "MapGet's delegate overload reflects over the supplied delegate's parameters; the delegate here is a fixed, statically-known shape.")]
	public static IEndpointRouteBuilder MapDeferredSignIn(this IEndpointRouteBuilder endpoints, string pattern = DefaultPattern)
	{
		endpoints.MapGet(pattern, async (HttpContext context, IDeferredSignIn deferredSignIn, string key, string returnUrl) =>
		{
			if (!deferredSignIn.TryConsume(key, out var action))
				return Results.Unauthorized();

			if (action.SignOut)
				await context.SignOutAsync(action.Scheme).ConfigureAwait(false);
			else
				await context.SignInAsync(action.Scheme, action.Principal!, action.Properties).ConfigureAwait(false);

			return Results.Content(
				$"""<!DOCTYPE html><html><head><meta http-equiv="refresh" content="0; URL={System.Net.WebUtility.HtmlEncode(returnUrl)}" /></head><body></body></html>""",
				"text/html");
		});
		return endpoints;
	}
}
```

- [ ] **Step 4: Repoint the composition-root extension**

Modify `Midgard/src/Infrastructure.Web.Server/DeferredSignIn/ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Web.Server.DeferredSignIn;

namespace Norse.Infrastructure.Web.Server.DeferredSignIn;

/// <summary>Composition-root wiring for <see cref="IDeferredSignIn"/>.</summary>
public static class ServiceCollectionExtensions
{
	/// <summary>Registers <see cref="IDeferredSignIn"/> and the <see cref="IMemoryCache"/> it depends on.</summary>
	public static IServiceCollection AddDeferredSignIn(this IServiceCollection services)
	{
		services.AddMemoryCache();
		services.AddSingleton<IDeferredSignIn, MemoryCacheDeferredSignIn>();
		return services;
	}
}
```

- [ ] **Step 5: Run the existing test suite to confirm regression-free**

Run (from `Midgard/`): `dotnet test tests/Infrastructure.Web.Server.Tests/Infrastructure.Web.Server.Tests.csproj`
Expected: PASS — all four `MemoryCacheDeferredSignInTests` facts (`StashSignIn_then_TryConsume_returns_the_stashed_sign_in`, `StashSignOut_then_TryConsume_returns_the_stashed_sign_out`, `TryConsume_with_an_unknown_key_returns_false`, `TryConsume_is_one_time_use`) still pass unchanged.

- [ ] **Step 6: Commit**

```bash
git add src/Infrastructure.Web.Server/DeferredSignIn
git commit -m "refactor: implement Asgard's IDeferredSignIn contract instead of declaring it locally"
```

**Ship this realm's gate before starting Task 3.**

---

### Task 3: Himinbjörg — move `NorseSignInManager` into `Identity.Web.Server`; drop `Identity`'s Midgard NorseRef

**Files:**
- Modify: `Himinbjorg/src/Identity/IdentityBuilderExtensions.cs`
- Modify: `Himinbjorg/src/Identity/Identity.csproj`
- Delete: `Himinbjorg/src/Identity/NorseSignInManager.cs`
- Create: `Himinbjorg/src/Identity.Web.Server/NorseSignInManager.cs`
- Modify: `Himinbjorg/src/Identity.Web.Server/ServiceCollectionExtensions.cs`
- Modify: `Himinbjorg/tests/Identity.Tests/IdentityBuilderExtensionsTests.cs`
- Modify: `Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj`
- Delete: `Himinbjorg/tests/Identity.Tests/NorseSignInManagerTests.cs`
- Create: `Himinbjorg/tests/Identity.Web.Server.Tests/NorseSignInManagerTests.cs`
- Modify: `Himinbjorg/tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj`
- Create: `Himinbjorg/tests/Identity.Web.Server.Tests/ServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: Task 1's `IDeferredSignIn` (via `Identity.Web.Server.csproj`'s pre-existing `NorseRef Abstractions.Web.Server (Asgard)` — no new NorseRef needed).
- Produces: `Norse.Identity.Web.Server.NorseSignInManager` (moved from `Norse.Identity`), `public const string DeferredSignInKeyItemName = "Norse.DeferredSignInKey"` — consumed by Task 4 (Yggdrasil's `BlazorServerAuthenticationGateway`).

- [ ] **Step 1: Delete the old test location**

```bash
git rm tests/Identity.Tests/NorseSignInManagerTests.cs
```

- [ ] **Step 2: Write the moved test in its new home (red — `NorseSignInManager` doesn't exist in this namespace yet)**

Create `Himinbjorg/tests/Identity.Web.Server.Tests/NorseSignInManagerTests.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Norse.Abstractions.Web.Server.DeferredSignIn;
using NSubstitute;

namespace Norse.Identity.Web.Server.Tests;

/// <summary>
/// Proves <see cref="NorseSignInManager"/> actually intercepts sign-in/sign-out once
/// <c>HttpContext.Response.HasStarted</c> is genuinely true (an already-established Blazor Server
/// interactive circuit), and behaves exactly like the unmodified base class otherwise.
///
/// "Genuinely true" is not simulated via reflection or a hand-rolled fake -- these tests host a real,
/// minimal ASP.NET Core pipeline via <see cref="TestServer"/> with a real cookie authentication handler
/// wired in. Writing to the response body before sign-in flips <c>HasStarted</c> for real, the same way
/// Kestrel does, and the unmodified <see cref="SignInManager{TUser}"/> genuinely throws
/// <see cref="InvalidOperationException"/> trying to write the Set-Cookie header afterward.
/// </summary>
public sealed class NorseSignInManagerTests
{
	static readonly string _scheme = IdentityConstants.ApplicationScheme;

	[Fact]
	async Task Unmodified_SignInManager_throws_once_the_response_has_already_started()
	{
		var probe = await RunAsync(useNorseSignInManager: false, responseAlreadyStarted: true, signOut: false);

		probe.Exception.ShouldNotBeNull();
		probe.Exception.ShouldBeOfType<InvalidOperationException>();
	}

	[Fact]
	async Task Unmodified_SignInManager_signs_out_directly_and_throws_once_the_response_has_already_started()
	{
		var probe = await RunAsync(useNorseSignInManager: false, responseAlreadyStarted: true, signOut: true);

		probe.Exception.ShouldNotBeNull();
		probe.Exception.ShouldBeOfType<InvalidOperationException>();
	}

	[Fact]
	async Task NorseSignInManager_defers_sign_in_once_the_response_has_already_started()
	{
		var probe = await RunAsync(useNorseSignInManager: true, responseAlreadyStarted: true, signOut: false);

		probe.Exception.ShouldBeNull();
		probe.DeferredSignIn.Received(1).StashSignIn(_scheme, Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>());
		probe.DeferredSignIn.DidNotReceiveWithAnyArgs().StashSignOut(default!);
		probe.ItemsKey.ShouldBe(StashedSignInKey);
	}

	[Fact]
	async Task NorseSignInManager_defers_sign_out_once_the_response_has_already_started()
	{
		var probe = await RunAsync(useNorseSignInManager: true, responseAlreadyStarted: true, signOut: true);

		probe.Exception.ShouldBeNull();
		probe.DeferredSignIn.Received(1).StashSignOut(_scheme);
		probe.DeferredSignIn.DidNotReceiveWithAnyArgs().StashSignIn(default!, default!, default!);
		probe.ItemsKey.ShouldBe(StashedSignOutKey);
	}

	[Fact]
	async Task NorseSignInManager_signs_in_directly_when_the_response_has_not_started()
	{
		var probe = await RunAsync(useNorseSignInManager: true, responseAlreadyStarted: false, signOut: false);

		probe.Exception.ShouldBeNull();
		probe.DeferredSignIn.DidNotReceiveWithAnyArgs().StashSignIn(default!, default!, default!);
		probe.DeferredSignIn.DidNotReceiveWithAnyArgs().StashSignOut(default!);
		probe.ItemsKey.ShouldBeNull();
		probe.SetCookieHeaderPresent.ShouldBeTrue();
	}

	[Fact]
	async Task NorseSignInManager_signs_out_directly_when_the_response_has_not_started()
	{
		var probe = await RunAsync(useNorseSignInManager: true, responseAlreadyStarted: false, signOut: true);

		probe.Exception.ShouldBeNull();
		probe.DeferredSignIn.DidNotReceiveWithAnyArgs().StashSignIn(default!, default!, default!);
		probe.DeferredSignIn.DidNotReceiveWithAnyArgs().StashSignOut(default!);
		probe.ItemsKey.ShouldBeNull();
		probe.SetCookieHeaderPresent.ShouldBeTrue();
	}

	const string StashedSignInKey = "stashed-sign-in-key";
	const string StashedSignOutKey = "stashed-sign-out-key";

	static async Task<Probe> RunAsync(bool useNorseSignInManager, bool responseAlreadyStarted, bool signOut)
	{
		var deferredSignIn = Substitute.For<IDeferredSignIn>();
		deferredSignIn.StashSignIn(Arg.Any<string>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<AuthenticationProperties>()).Returns(StashedSignInKey);
		deferredSignIn.StashSignOut(Arg.Any<string>()).Returns(StashedSignOutKey);

		Exception? caught = null;
		string? itemsKey = null;
		var setCookiePresent = false;

		using var host = await new HostBuilder()
			.ConfigureWebHost(webHost => webHost
				.UseTestServer()
				.ConfigureServices(services => services.AddAuthentication(_scheme).AddCookie(_scheme))
				.Configure(app => app.Run(async context =>
				{
					if (responseAlreadyStarted)
						await context.Response.WriteAsync(" ").ConfigureAwait(false);

					var user = new NorseUser { UserName = "user@example.com", Email = "user@example.com" };
					var claimsFactory = Substitute.For<IUserClaimsPrincipalFactory<NorseUser>>();
					claimsFactory.CreateAsync(Arg.Any<NorseUser>()).Returns(new ClaimsPrincipal(new ClaimsIdentity(_scheme)));

					var userManager = Substitute.For<UserManager<NorseUser>>(
						Substitute.For<IUserStore<NorseUser>>(), null!, new PasswordHasher<NorseUser>(),
						Array.Empty<IUserValidator<NorseUser>>(), Array.Empty<IPasswordValidator<NorseUser>>(),
						new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!,
						NullLogger<UserManager<NorseUser>>.Instance);

					var accessor = new HttpContextAccessor { HttpContext = context };
					var schemes = Substitute.For<IAuthenticationSchemeProvider>();
					var confirmation = Substitute.For<IUserConfirmation<NorseUser>>();

					SignInManager<NorseUser> signInManager = useNorseSignInManager
						? new NorseSignInManager(
							userManager, accessor, claimsFactory, Options.Create(new IdentityOptions()),
							NullLogger<SignInManager<NorseUser>>.Instance, schemes, confirmation, deferredSignIn)
						: new SignInManager<NorseUser>(
							userManager, accessor, claimsFactory, Options.Create(new IdentityOptions()),
							NullLogger<SignInManager<NorseUser>>.Instance, schemes, confirmation);

					try
					{
						if (signOut)
							await signInManager.SignOutAsync().ConfigureAwait(false);
						else
							await signInManager.SignInWithClaimsAsync(user, isPersistent: false, additionalClaims: []).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						caught = ex;
					}

					itemsKey = context.Items.TryGetValue(NorseSignInManager.DeferredSignInKeyItemName, out var value)
						? value as string
						: null;
					setCookiePresent = context.Response.Headers.ContainsKey("Set-Cookie");
				})))
			.StartAsync().ConfigureAwait(false);

		using var client = host.GetTestServer().CreateClient();
		await client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(false);

		return new Probe(caught, deferredSignIn, itemsKey, setCookiePresent);
	}

	sealed record Probe(Exception? Exception, IDeferredSignIn DeferredSignIn, string? ItemsKey, bool SetCookieHeaderPresent);
}
```

- [ ] **Step 3: Add the `Microsoft.AspNetCore.TestHost` package to its new test project**

Modify `Himinbjorg/tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.AspNetCore.TestHost" Version="*" />
		<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="*" />
		<PackageReference Include="NSubstitute" Version="*" />
		<ProjectReference Include="../../src/Identity.Web.Server/Identity.Web.Server.csproj" />
	</ItemGroup>
	<ItemGroup>
		<!--
			SQLitePCLRaw.lib.e_sqlite3 (transitive via Microsoft.EntityFrameworkCore.Sqlite) has a known
			high-severity vulnerability with no patched release. Exposure is test-only (in-memory). Same
			precedent as Identity.Tests.csproj. Revisit when SQLitePCLRaw publishes a fix.
		-->
		<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-2m69-gcr7-jv3q" />
	</ItemGroup>
</Project>
```

- [ ] **Step 4: Run the new test project to verify it fails to compile**

Run (from `Himinbjorg/`): `dotnet test tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj`
Expected: FAIL — `Norse.Identity.Web.Server.NorseSignInManager` does not exist (compile error); `Norse.Abstractions.Web.Server.DeferredSignIn.IDeferredSignIn` resolves fine already (existing NorseRef).

- [ ] **Step 5: Delete `NorseSignInManager` from the base `Identity` project**

```bash
git rm src/Identity/NorseSignInManager.cs
```

- [ ] **Step 6: Create `NorseSignInManager` in `Identity.Web.Server`**

Create `Himinbjorg/src/Identity.Web.Server/NorseSignInManager.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norse.Abstractions.Web.Server.DeferredSignIn;

namespace Norse.Identity.Web.Server;

/// <summary>
/// Overrides every seam ASP.NET Core Identity's sign-in/sign-out paths funnel through to detect when the
/// caller is an already-established Blazor Server interactive circuit (<c>Context.Response.HasStarted</c>)
/// — cookie writes are impossible there, not merely inconvenient. When detected, defers via
/// <see cref="IDeferredSignIn"/> instead of writing the cookie directly and stashes the completion key on
/// <c>HttpContext.Items</c> for the caller to read back. Every other call path (WASM/MAUI over gRPC-Web,
/// any static-SSR request) is a real, distinct HTTP request with <c>Response.HasStarted == false</c> and
/// behaves exactly as the unmodified base class would — zero behavior change for those paths.
///
/// Lives in <c>Identity.Web.Server</c>, not the base <c>Identity</c> project — <c>Identity</c> is shared
/// with <c>Identity.Migrations</c> (a console tool), and everything this type touches
/// (<see cref="HttpContext"/>, <see cref="AuthenticationProperties"/>, <see cref="IDeferredSignIn"/>) is an
/// ASP.NET-Core-web-hosting concern migration tooling has no business depending on.
/// </summary>
public sealed class NorseSignInManager(
	UserManager<NorseUser> userManager, IHttpContextAccessor contextAccessor,
	IUserClaimsPrincipalFactory<NorseUser> claimsFactory, IOptions<IdentityOptions> optionsAccessor,
	ILogger<SignInManager<NorseUser>> logger, IAuthenticationSchemeProvider schemes,
	IUserConfirmation<NorseUser> confirmation, IDeferredSignIn deferredSignIn)
	: SignInManager<NorseUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
	/// <summary>The <c>HttpContext.Items</c> key under which a deferred completion key is stashed, when one is needed.</summary>
	public const string DeferredSignInKeyItemName = "Norse.DeferredSignInKey";

	// Both overloads override explicitly, independently -- do NOT assume one delegates to the other
	// inside the base class and skip overriding it. Getting this wrong silently reintroduces the crash
	// on whichever overload isn't actually hooked. Verify this claim yourself if you have any doubt
	// (e.g. decompile the real installed assembly), don't take this comment on faith either.
	/// <summary>Forwards to the <see cref="AuthenticationProperties"/> overload, which carries the actual deferral logic.</summary>
	public override async Task SignInWithClaimsAsync(NorseUser user, bool isPersistent, IEnumerable<Claim> additionalClaims) =>
		await SignInWithClaimsAsync(user, new AuthenticationProperties { IsPersistent = isPersistent }, additionalClaims).ConfigureAwait(false);

	/// <summary>Signs in normally when the response can still write a cookie; otherwise stashes the sign-in via <see cref="IDeferredSignIn"/> and records the completion key on <see cref="DeferredSignInKeyItemName"/>.</summary>
	public override async Task SignInWithClaimsAsync(NorseUser user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
	{
		if (!Context.Response.HasStarted)
		{
			await base.SignInWithClaimsAsync(user, authenticationProperties, additionalClaims).ConfigureAwait(false);
			return;
		}

		var principal = await CreateUserPrincipalAsync(user).ConfigureAwait(false);
		((ClaimsIdentity)principal.Identity!).AddClaims(additionalClaims);
		var key = deferredSignIn.StashSignIn(AuthenticationScheme, principal, authenticationProperties ?? new AuthenticationProperties());
		Context.Items[DeferredSignInKeyItemName] = key;
	}

	/// <summary>Signs out normally when the response can still write a cookie; otherwise stashes the sign-out via <see cref="IDeferredSignIn"/> and records the completion key on <see cref="DeferredSignInKeyItemName"/>.</summary>
	public override async Task SignOutAsync()
	{
		if (!Context.Response.HasStarted)
		{
			await base.SignOutAsync().ConfigureAwait(false);
			return;
		}

		var key = deferredSignIn.StashSignOut(AuthenticationScheme);
		Context.Items[DeferredSignInKeyItemName] = key;
	}
}
```

Note: no `using Norse.Identity;` is needed for `NorseUser` — `Norse.Identity` is a textual ancestor namespace of `Norse.Identity.Web.Server`, so its members are visible without a `using` directive, exactly as `LoginHandler.cs` in this same project already relies on.

- [ ] **Step 7: Run the moved test to verify it passes**

Run (from `Himinbjorg/`): `dotnet test tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj`
Expected: PASS — all six `NorseSignInManagerTests` facts.

- [ ] **Step 8: Change `AddNorseIdentity()` to return `IdentityBuilder`, drop the `SignInManager` registration**

Modify `Himinbjorg/src/Identity/IdentityBuilderExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Identity;

/// <summary>
/// Dependency-injection wiring for the Norse Identity stack.
/// </summary>
public static class IdentityBuilderExtensions
{
	/// <summary>
	/// Registers ASP.NET Core Identity (with <see cref="NorseUserStore"/> and
	/// <see cref="NorseIdentityDbContext"/> as its EF stores) and OpenIddict's core services against the
	/// same context. Returns the <see cref="IdentityBuilder"/>, not <see cref="IServiceCollection"/> — this
	/// project is shared with migration tooling and must not reference a <c>SignInManager</c> override; a
	/// caller that needs one chains <c>.AddSignInManager&lt;T&gt;()</c> on the returned builder itself.
	/// </summary>
	/// <param name="services">The service collection to configure.</param>
	/// <returns>The <see cref="IdentityBuilder"/> for further chaining.</returns>
	public static IdentityBuilder AddNorseIdentity(this IServiceCollection services)
	{
		services.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3);

		var identityBuilder = services
			.AddIdentity<NorseUser, NorseRole>()
			.AddUserStore<NorseUserStore>()
			.AddEntityFrameworkStores<NorseIdentityDbContext>()
			.AddDefaultTokenProviders();

		services
			.AddOpenIddict()
			.AddCore(o => o
				.UseEntityFrameworkCore()
				.UseDbContext<NorseIdentityDbContext>()
				.ReplaceDefaultEntities<
					NorseOpenIddictApplication, NorseOpenIddictAuthorization,
					NorseOpenIddictScope, NorseOpenIddictToken, Guid>());

		return identityBuilder;
	}
}
```

- [ ] **Step 9: Drop `Identity.csproj`'s Midgard NorseRef**

Modify `Himinbjorg/src/Identity/Identity.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Identity: ASP.NET Core Identity v3 entity types, NorseIdentityDbContext (Identity + OpenIddict), NorseUserStore with projection overrides, and DI extension. Runtime library — referenced by Norse.Identity.Web.Server; never by migration tooling.</Description>
		<!-- ASP.NET Core Identity does not support trimming/AOT; override the src-level default. -->
		<IsAotCompatible>false</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<FrameworkReference Include="Microsoft.AspNetCore.App" />
	</ItemGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="*" />
		<PackageReference Include="OpenIddict.EntityFrameworkCore" Version="*" />
	</ItemGroup>
	<ItemGroup>
		<NorseRef Include="EntityFramework">
			<Repo>Urdarbrunnr</Repo>
			<Generator>true</Generator>
		</NorseRef>
	</ItemGroup>
</Project>
```

- [ ] **Step 10: Fix the return-type test in `Identity.Tests`**

Modify `Himinbjorg/tests/Identity.Tests/IdentityBuilderExtensionsTests.cs` — replace the `_returns_same_services_for_chaining` fact:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Norse.Identity.Tests;

public sealed class IdentityBuilderExtensionsTests
{
	[Fact]
	void AddNorseIdentity_registers_NorseUserStore_as_IUserStore()
	{
		ServiceCollection services = new();

		services.AddNorseIdentity();

		var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IUserStore<NorseUser>));
		descriptor.ShouldNotBeNull();
		descriptor.ImplementationType.ShouldBe(typeof(NorseUserStore));
	}

	[Fact]
	void AddNorseIdentity_returns_the_identity_builder_wrapping_the_same_services()
	{
		ServiceCollection services = new();

		var result = services.AddNorseIdentity();

		result.Services.ShouldBeSameAs(services);
	}

	[Fact]
	void AddNorseIdentity_configures_SchemaVersion_to_Version3()
	{
		ServiceCollection services = new();
		services.AddDbContext<NorseIdentityDbContext>(o => o.UseSqlite("Data Source=:memory:"));

		services.AddNorseIdentity();

		using var provider = services.BuildServiceProvider();
		var options = provider.GetRequiredService<IOptions<IdentityOptions>>();

		options.Value.Stores.SchemaVersion.ShouldBe(IdentitySchemaVersions.Version3);
	}
}
```

- [ ] **Step 11: Remove the now-unused `Microsoft.AspNetCore.TestHost` package from `Identity.Tests`**

Modify `Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj` (only `NorseSignInManagerTests.cs` used it, and it just moved out):

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="*" />
		<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="*" />
		<PackageReference Include="NSubstitute" Version="*" />
		<ProjectReference Include="../../src/Identity/Identity.csproj" />
	</ItemGroup>
	<ItemGroup>
		<!--
			SQLitePCLRaw.lib.e_sqlite3 (transitive via Microsoft.EntityFrameworkCore.Sqlite) has a known
			high-severity vulnerability with no patched release. Exposure is test-only (in-memory). Revisit
			when SQLitePCLRaw publishes a fix.
		-->
		<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-2m69-gcr7-jv3q" />
	</ItemGroup>
</Project>
```

- [ ] **Step 12: Run `Identity.Tests` to verify it still builds and passes without the Midgard NorseRef**

Run (from `Himinbjorg/`): `dotnet test tests/Identity.Tests/Identity.Tests.csproj`
Expected: PASS. This is the proof that `Identity.csproj`'s own doc comment ("never by migration tooling") is now actually true — the project (and `Identity.Migrations`, which `ProjectReference`s it) no longer transitively touches Midgard at all.

- [ ] **Step 13: Write the failing wiring test proving `AddNorseAuthenticationService` doesn't yet register `NorseSignInManager`**

Create `Himinbjorg/tests/Identity.Web.Server.Tests/ServiceCollectionExtensionsTests.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Identity.Web.Server.Tests;

public sealed class ServiceCollectionExtensionsTests
{
	[Fact]
	void AddNorseAuthenticationService_registers_NorseSignInManager_as_SignInManager()
	{
		ServiceCollection services = new();

		services.AddNorseAuthenticationService("Host=localhost;Database=norse_identity_test");

		var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(SignInManager<NorseUser>));
		descriptor.ShouldNotBeNull();
		descriptor.ImplementationType.ShouldBe(typeof(NorseSignInManager));
	}
}
```

(Uses `LastOrDefault`, not `FirstOrDefault` — `AddIdentity<TUser,TRole>()` registers the base `SignInManager<NorseUser>` via `TryAddScoped`, and `.AddSignInManager<T>()` adds a second, later registration; the DI container resolves the *last* registration for a given service type, so the test must inspect the one that will actually be resolved.)

- [ ] **Step 14: Run to verify it fails**

Run (from `Himinbjorg/`): `dotnet test tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj`
Expected: FAIL — the last matching descriptor's `ImplementationType` is `SignInManager<NorseUser>`, not `NorseSignInManager` (`AddNorseAuthenticationService` doesn't chain `.AddSignInManager<NorseSignInManager>()` yet).

- [ ] **Step 15: Wire `NorseSignInManager` into `AddNorseAuthenticationService`**

Modify `Himinbjorg/src/Identity.Web.Server/ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.AuthN.Components;
using Norse.EntityFramework;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;
using ProtoBuf.Grpc.Server;

namespace Norse.Identity.Web.Server;

/// <summary>Composition-root wiring for Identity.Web.Server's gRPC authentication service.</summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Registers <see cref="NorseIdentityDbContext"/>, ASP.NET Core Identity (with the
	/// <see cref="NorseSignInManager"/> override), the code-first gRPC host with
	/// <see cref="OutcomeServerInterceptor"/>, and the mediator handlers backing
	/// <see cref="IAuthenticationService"/>.
	/// </summary>
	public static IServiceCollection AddNorseAuthenticationService(this IServiceCollection services, string connectionString)
	{
		services.AddDbContext<NorseIdentityDbContext>(o =>
		{
			o.UseNpgsql(connectionString);
			NorseDbContextOptionsExtensions.ApplyNorseConventions(o);
		});
		services.AddNorseIdentity().AddSignInManager<NorseSignInManager>();
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

(Added `using Microsoft.AspNetCore.Identity;` for the `AddSignInManager<T>` extension method — every other using stays as-is.)

- [ ] **Step 16: Run to verify it passes**

Run (from `Himinbjorg/`): `dotnet test tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj`
Expected: PASS — full suite (`IdentityNoOpEmailSenderTests`, `LoginHandlerTests`, `LogoutHandlerTests`, `RegisterHandlerTests`, the moved `NorseSignInManagerTests`, and the new `ServiceCollectionExtensionsTests`).

- [ ] **Step 17: Run the full Himinbjörg solution once to confirm no other project regressed**

Run (from `Himinbjorg/`): `dotnet test Himinbjorg.slnx`
Expected: PASS across every project.

- [ ] **Step 18: Commit**

```bash
git add src/Identity/IdentityBuilderExtensions.cs src/Identity/Identity.csproj src/Identity.Web.Server/NorseSignInManager.cs src/Identity.Web.Server/ServiceCollectionExtensions.cs tests/Identity.Tests/IdentityBuilderExtensionsTests.cs tests/Identity.Tests/Identity.Tests.csproj tests/Identity.Web.Server.Tests/NorseSignInManagerTests.cs tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj tests/Identity.Web.Server.Tests/ServiceCollectionExtensionsTests.cs
git commit -m "refactor: move NorseSignInManager into Identity.Web.Server, drop Identity's Midgard NorseRef"
```

**Ship this realm's gate before starting Task 4 — this is the change that unblocks `feature/identity-web-server`'s merge.**

---

### Task 4: Yggdrasil — explicit NorseRef to Midgard; repoint the `NorseSignInManager` using directive

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`
- Modify: `Yggdrasil/src/Hosting.Web.Server/BlazorServerAuthenticationGateway.cs`

**Interfaces:**
- Consumes: Task 2's `Norse.Infrastructure.Web.Server.DeferredSignIn.AddDeferredSignIn()` / `MapDeferredSignIn()` (already used in `Program.cs`, was resolving transitively); Task 3's `Norse.Identity.Web.Server.NorseSignInManager.DeferredSignInKeyItemName` (was `Norse.Identity.NorseSignInManager.DeferredSignInKeyItemName`, now moved).
- Produces: nothing new — this task only makes an existing, working dependency explicit and follows the type to its new namespace.

No behavior changes and no dedicated test project exists yet for `Hosting.Web.Server` beyond the scaffolded `Hosting.Web.Server.Tests.csproj` (currently empty of test files) — verify via build success.

- [ ] **Step 1: Add the explicit NorseRef**

Modify `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
	<PropertyGroup>
		<BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>
		<ContainerBaseImage>mcr.microsoft.com/dotnet/aspnet:$(ContainerVersion)</ContainerBaseImage>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Grpc.AspNetCore.Server.Reflection" />
		<PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.Server" />
		<ProjectReference Include="..\Hosting.Web.Client\Hosting.Web.Client.csproj" />
		<NorseRef Include="Infrastructure.Components.Theme.FluentUI">
			<Repo>Midgard</Repo>
		</NorseRef>
		<NorseRef Include="Infrastructure.Web.Server">
			<Repo>Midgard</Repo>
		</NorseRef>
		<NorseRef Include="Identity.Web.Server">
			<Repo>Himinbjorg</Repo>
		</NorseRef>
		<NorseRef Include="AuthN.Components.FluentUI">
			<Repo>Heimdall</Repo>
		</NorseRef>
		<NorseRef Include="Abstractions.Web.Server">
			<Repo>Asgard</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Repoint the `NorseSignInManager` using directive**

Modify `Yggdrasil/src/Hosting.Web.Server/BlazorServerAuthenticationGateway.cs` — replace `using Norse.Identity;` with `using Norse.Identity.Web.Server;`:

```csharp
using Norse.Abstractions.Web.Server.Mediator;
using Norse.AuthN.Components;
using Norse.Identity.Web.Server;
using Norse.Infrastructure.Web.Server.DeferredSignIn;

namespace Norse.Hosting.Web.Server;

/// <summary>
/// Blazor Server's own <see cref="IAuthenticationGateway"/> — calls the mediator handlers directly,
/// in-process, no gRPC involved at all (per §2's transport matrix). Maps <c>Outcome&lt;T&gt;</c> to
/// <see cref="AuthenticationResult"/> inline — this glue is realm-specific, not generic Midgard
/// infrastructure (spec §9.8).
/// </summary>
sealed class BlazorServerAuthenticationGateway(
	IRequestHandler<LoginRequest, Outcome<BoolResponse>> loginHandler,
	IRequestHandler<RegisterRequest, Outcome<BoolResponse>> registerHandler,
	IRequestHandler<LogoutRequest, Outcome> logoutHandler,
	IHttpContextAccessor httpContextAccessor)
	: IAuthenticationGateway
{
	public async Task<AuthenticationResult> Login(LoginRequest request)
	{
		var outcome = await loginHandler.Handle(request, httpContextAccessor.HttpContext!.RequestAborted).ConfigureAwait(false);
		if (!outcome.IsSuccess)
			return new AuthenticationResult { Succeeded = false, Errors = outcome.Problem!.Errors };

		return new AuthenticationResult { Succeeded = outcome.Value!.Value, DeferredCompletionUrl = TryGetDeferredCompletionUrl() };
	}

	public async Task<AuthenticationResult> Register(RegisterRequest request)
	{
		var outcome = await registerHandler.Handle(request, httpContextAccessor.HttpContext!.RequestAborted).ConfigureAwait(false);
		return new AuthenticationResult { Succeeded = outcome.IsSuccess, Errors = outcome.Problem?.Errors ?? new Dictionary<string, string[]>() };
	}

	public async Task<AuthenticationResult> Logout(LogoutRequest request)
	{
		var outcome = await logoutHandler.Handle(request, httpContextAccessor.HttpContext!.RequestAborted).ConfigureAwait(false);
		return new AuthenticationResult { Succeeded = outcome.IsSuccess, DeferredCompletionUrl = TryGetDeferredCompletionUrl() };
	}

	string? TryGetDeferredCompletionUrl()
	{
		if (httpContextAccessor.HttpContext!.Items[NorseSignInManager.DeferredSignInKeyItemName] is not string key)
			return null;

		return $"{DeferredSignInEndpointRouteBuilderExtensions.DefaultPattern}?key={Uri.EscapeDataString(key)}&returnUrl={Uri.EscapeDataString("/")}";
	}
}
```

(`Program.cs` needs no change — its `using Norse.Identity;` is for `NorseUser`/`IEmailSender<NorseUser>`, unrelated to this move, and its `AddDeferredSignIn()`/`MapDeferredSignIn()` calls are unaffected since those names didn't change in Task 2.)

- [ ] **Step 3: Build to verify**

Run (from `Yggdrasil/`): `dotnet build Yggdrasil.slnx`
Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add src/Hosting.Web.Server/Hosting.Web.Server.csproj src/Hosting.Web.Server/BlazorServerAuthenticationGateway.cs
git commit -m "chore: make Hosting.Web.Server's Midgard NorseRef explicit; follow NorseSignInManager's move"
```

**Ship this realm's gate.** After all four gates clear:
- Remove Open Decision #2 from `../../../CLAUDE.md` (Bifrost) — it's resolved.
- Amend `../Heimdall/specs/2026-07-13-authn-identity-split-design.md` per that plan's own follow-up instruction, noting the final realm placement.

---

## Self-Review

**Spec coverage:** Task 1 covers the spec's Asgard section; Task 2 covers Midgard; Task 3 covers Himinbjörg (both the `IDeferredSignIn`-consumption fix and the `Identity`/migration-tooling-sharing fix); Task 4 covers Yggdrasil. The spec's "Out of scope" section (Heimdall's `DeferredCompletionUrl`, `Login.razor`/`Logout.razor`) is correctly untouched by any task — `BlazorServerAuthenticationGateway.cs` already has `DeferredCompletionUrl` wired in the current branch state, confirmed by direct read, so no task needed to add it.

**Placeholder scan:** No TBDs; every step has complete, exact code read from or verified against the real current files on each repo's active branch.

**Type consistency:** `IDeferredSignIn`/`DeferredSignInAction` namespace (`Norse.Abstractions.Web.Server.DeferredSignIn`) is identical across Tasks 1–4. `NorseSignInManager`'s namespace (`Norse.Identity.Web.Server`) and its `DeferredSignInKeyItemName` constant name are identical across Tasks 3–4. `AddDeferredSignIn()`/`MapDeferredSignIn()`/`DefaultPattern` names are unchanged from Task 2 through Task 4's consumption in `Program.cs`.
