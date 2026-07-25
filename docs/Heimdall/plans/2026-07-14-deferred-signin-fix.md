# Fix: deferred sign-in for Blazor Server interactive circuits

**Not a formal spec-first cycle** — this is a working design note for an unplanned fix discovered live during Task 10 (E2E verification) of the AuthN bootstrap slice. Buvy's explicit direction: port the proven prior-art solution from Assurely directly ("boyscout rule it into our stack"), then amend `2026-07-13-authn-identity-split-design.md` with the lessons afterward. This doc is the source material for that later amendment, not a substitute for it.

## The problem, confirmed live

`Login.razor`/`Logout.razor` are `@rendermode InteractiveAuto`. By the time their event handlers run, the Blazor Server interactive circuit (SignalR) has already taken over and the original HTTP response has already started — `HttpContext.Response.HasStarted == true`. `SignInManager<NorseUser>.PasswordSignInAsync`/`.SignOutAsync()` both try to write a `Set-Cookie` response header at that point and throw:

```
System.InvalidOperationException: Headers are read-only, response has already started.
```

`Register` never hits this — `RegisterHandler` never calls `SignInManager`, so it has no cookie to write. Confirmed live against the real composed system (Aspire + real Postgres): Register works end-to-end; Login crashes the circuit every time.

## Prior art: Assurely (`../../../../Assurely/Hosting`)

Buvy solved this exact problem before. Read directly from the source (not re-derived from memory):

- `src/Server/Extensions/HttpContextExtensions.cs` — `HttpContext.SignInAsync(user, isPersistent)` checks `context.WebSockets.IsWebSocketRequest`. If false, signs in directly (a real, distinct HTTP request — headers are writable). If true, it can't sign in on this request — it stashes the user + persistence flag in `IMemoryCache` under a fresh one-time `Guid` key (60s TTL) and returns that key instead of completing sign-in.
- The interactive Razor component gets the key back and does `NavigationManager.NavigateTo($"?login={key}", forceLoad: true)` — a forced full-page reload, tearing down the circuit and re-entering the pipeline as a genuine new HTTP request.
- `src/Server/Middleware/UserId/UserIdMiddleware.cs` — registered early, before routing/Blazor — intercepts `?login={key}`, looks up the cached user, performs the *real* `HttpContext.SignInAsync` (now safe — a real, fresh, non-WebSocket request), removes the cache entry (one-time use), and responds with a **meta-refresh HTML page**, not a 302 redirect — because mobile Chrome silently drops `Set-Cookie` on a 302, which loops forever otherwise (the "Android" workaround the Assurely CLAUDE.md flags as pivotal). The meta-refresh's own page load lands the browser on the real destination under a fresh, authenticated circuit.

## The adaptation for NorseArchitecture

Same mechanism, mapped onto our vocabulary and shipped with a much smaller blast radius than Assurely's original, because we can hook lower in the call graph. Confirmed via `ilspycmd` against the real installed `Microsoft.AspNetCore.Identity.dll` (not assumed) that `SignInManager<TUser>` exposes:

```
public virtual Task SignInWithClaimsAsync(TUser user, bool isPersistent, IEnumerable<Claim> additionalClaims)
public virtual Task SignInWithClaimsAsync(TUser user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
public virtual Task SignOutAsync()
public virtual Task<ClaimsPrincipal> CreateUserPrincipalAsync(TUser user)
public string AuthenticationScheme { get; set; }
public HttpContext Context { get; }
```

`PasswordSignInAsync`/`SignInOrTwoFactorAsync` (the paths `LoginHandler` already calls) funnel through `SignInWithClaimsAsync` internally to do the actual cookie write. **Subclassing `SignInManager<NorseUser>` and overriding these three methods is the entire fix** — `LoginHandler`/`LogoutHandler` (Himinbjörg, already shipped, already reviewed, already tested) do not change at all. All existing lockout/anti-enumeration/2FA-path behavior stays exactly as tested.

### 1. Midgard — `Infrastructure.Web.Server` — new, zero-domain-knowledge primitive

Same tier as the existing `OutcomeServerInterceptor`/`ProblemExtensions` — reusable by any future realm that hits this same Blazor-Server-cookie problem, no `NorseUser`/`Outcome<T>`/AuthN-specific types anywhere in it.

`Midgard/src/Infrastructure.Web.Server/DeferredSignIn/IDeferredSignIn.cs`:
```csharp
namespace Norse.Infrastructure.Web.Server.DeferredSignIn;

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

/// <summary>What to do to complete a deferred sign-in/out. <paramref name="Principal"/> is null for sign-out.</summary>
public sealed record DeferredSignInAction(string Scheme, bool SignOut, ClaimsPrincipal? Principal, AuthenticationProperties? Properties);
```

`DeferredSignIn.cs` (the implementation, `IMemoryCache`-backed, 60s TTL, one-time use — matches Assurely's own TTL choice, no reason to deviate):
```csharp
namespace Norse.Infrastructure.Web.Server.DeferredSignIn;

sealed class MemoryCacheDeferredSignIn(IMemoryCache cache) : IDeferredSignIn
{
	static readonly MemoryCacheEntryOptions EntryOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) };

	public string StashSignIn(string scheme, ClaimsPrincipal principal, AuthenticationProperties properties)
	{
		var key = Guid.NewGuid().ToString();
		cache.Set(key, new DeferredSignInAction(scheme, SignOut: false, principal, properties), EntryOptions);
		return key;
	}

	public string StashSignOut(string scheme)
	{
		var key = Guid.NewGuid().ToString();
		cache.Set(key, new DeferredSignInAction(scheme, SignOut: true, null, null), EntryOptions);
		return key;
	}

	public bool TryConsume(string key, out DeferredSignInAction action)
	{
		if (!cache.TryGetValue(key, out DeferredSignInAction? found) || found is null)
		{
			action = null!;
			return false;
		}
		cache.Remove(key);
		action = found;
		return true;
	}
}
```

`ServiceCollectionExtensions.cs` addition (or a new one if none exists yet in this project — check first): `services.AddMemoryCache(); services.AddSingleton<IDeferredSignIn, MemoryCacheDeferredSignIn>();`

`DeferredSignInEndpointExtensions.cs` — the completion endpoint, mapped by the *host* (Yggdrasil), not by Midgard itself (Midgard never maps endpoints directly, matches the platform's existing separation):
```csharp
namespace Norse.Infrastructure.Web.Server.DeferredSignIn;

public static class DeferredSignInEndpointRouteBuilderExtensions
{
	/// <summary>
	/// Maps the completion endpoint for a deferred sign-in/out — a plain minimal-API endpoint (a real,
	/// distinct HTTP request, not a Blazor component), safe to write cookies from. Responds with a
	/// meta-refresh page rather than a redirect: mobile Chrome has a long-standing bug where it silently
	/// drops Set-Cookie on a 302, which would otherwise loop forever (see Assurely prior art, same fix).
	/// </summary>
	public static IEndpointRouteBuilder MapDeferredSignIn(this IEndpointRouteBuilder endpoints, string pattern = "/_auth/complete")
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

### 2. Himinbjörg — `Identity` (the base project, alongside `NorseUser`/`IdentityBuilderExtensions`)

**Amendment (2026-07-25):** the `Identity` project this section places `NorseSignInManager` in was deleted 2026-07-23, folded into `src/Identity.Web.Server`. Current shape is four live projects: `Identity.Web.Server`, `Identity.Migrations`, `Identity.Migrations.PostgreSQL`, `Identity.Migrations.SqlServer`.

`NorseSignInManager.cs`:
```csharp
namespace Norse.Identity;

/// <summary>
/// Overrides every seam ASP.NET Core Identity's sign-in/sign-out paths funnel through to detect when the
/// caller is an already-established Blazor Server interactive circuit (<c>Context.Response.HasStarted</c>)
/// — cookie writes are impossible there, not merely inconvenient. When detected, defers via
/// <see cref="IDeferredSignIn"/> instead of writing the cookie directly and stashes the completion key on
/// <c>HttpContext.Items</c> for the caller to read back. Every other call path (WASM/MAUI over gRPC-Web,
/// any static-SSR request) is a real, distinct HTTP request with <c>Response.HasStarted == false</c> and
/// behaves exactly as the unmodified base class would — zero behavior change for those paths.
/// </summary>
sealed class NorseSignInManager(
	UserManager<NorseUser> userManager, IHttpContextAccessor contextAccessor,
	IUserClaimsPrincipalFactory<NorseUser> claimsFactory, IOptions<IdentityOptions> optionsAccessor,
	ILogger<SignInManager<NorseUser>> logger, IAuthenticationSchemeProvider schemes,
	IUserConfirmation<NorseUser> confirmation, IDeferredSignIn deferredSignIn)
	: SignInManager<NorseUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
	public const string DeferredSignInKeyItemName = "Norse.DeferredSignInKey";

	// Both overloads override explicitly, independently -- do not rely on one delegating to the other
	// internally in the base class; that's an assumption, not a verified fact, and getting it wrong
	// silently reintroduces the crash on whichever overload isn't actually hooked.
	public override async Task SignInWithClaimsAsync(NorseUser user, bool isPersistent, IEnumerable<Claim> additionalClaims) =>
		await SignInWithClaimsAsync(user, new AuthenticationProperties { IsPersistent = isPersistent }, additionalClaims).ConfigureAwait(false);

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

`IdentityBuilderExtensions.AddNorseIdentity()` gains `.AddSignInManager<NorseSignInManager>()` in the `AddIdentity<NorseUser, NorseRole>()` chain (currently `.AddUserStore<NorseUserStore>().AddEntityFrameworkStores<NorseIdentityDbContext>().AddDefaultTokenProviders()` — add the new call there). `Identity.csproj` needs a new `NorseRef` to Midgard's `Infrastructure.Web.Server` (check whether it's already reachable transitively first — Himinbjörg's `Identity.Web.Server` project already references it, but the base `Identity` project may not).

**Verification requirement, not optional**: a real integration test proving `NorseSignInManager`'s override actually intercepts — e.g. force `HttpContext.Response.HasStarted` true in a test (write to the response body / call `StartAsync()`), call `PasswordSignInAsync`, assert `Context.Items` contains the deferred key and no exception was thrown. Don't accept "it compiles" as proof this works — that's exactly the class of assumption that broke twice already tonight.

### 3. Heimdall — `AuthN.Components` — contract addition

**Amendment (2026-07-25):** `AuthenticationResult` was retired 2026-07-25 (Heimdall `feature/transport-neutral-gateway`, tag v0.0.3) — replaced by `IAuthenticationService` carrying Asgard's `[GenerateGateway]` attribute, with the generated gateway returning `ValueTask<Outcome<T>>` directly. See `../specs/2026-07-13-authn-identity-split-design.md` §9.8's amendment for the full picture.

`AuthenticationResult` gains one new optional field:
```csharp
/// <summary>
/// Non-null only when sign-in/out couldn't complete on this request (an interactive Blazor Server
/// circuit) and must be completed via a forced full-page navigation instead. WASM/MAUI never see this
/// — their calls are always real HTTP requests, so sign-in always completes immediately.
/// </summary>
[DataMember(Order = 3)]
public string? DeferredCompletionUrl { get; init; }
```

### 4. Yggdrasil — `Hosting.Web.Server`

- `Program.cs`: map the completion endpoint — `app.MapDeferredSignIn();` (add near `app.MapAdditionalIdentityEndpoints()`), and register the Midgard service (`AddMemoryCache()`/`AddSingleton<IDeferredSignIn,...>` — check whether `AddNorseAuthenticationService` should own this registration instead, since it's AuthN-adjacent plumbing; lean toward Midgard's own `AddDeferredSignIn()` extension method registered directly in `Program.cs`, matching how `AddGrpcReflection()` is wired inline rather than folded into `AddNorseAuthenticationService`).
- `BlazorServerAuthenticationGateway.cs`: after calling `loginHandler`/`logoutHandler` via the mediator, check `httpContextAccessor.HttpContext!.Items[NorseSignInManager.DeferredSignInKeyItemName]` (this couples Yggdrasil to a Himinbjörg-internal constant string — a shared, well-known `HttpContext.Items` key name is the deliberately loose coupling point here, same idiom ASP.NET Core itself uses for `IAuthenticateResultFeature` etc.; consider whether this constant should instead live in Midgard's `IDeferredSignIn` namespace as a shared, provider-agnostic key name rather than the Himinbjörg-specific `NorseSignInManager` — **flag this exact placement question to the implementer, don't silently pick one**). If present, build `$"/_auth/complete?key={key}&returnUrl={Uri.EscapeDataString(destinationUrl)}"` and set it as `AuthenticationResult.DeferredCompletionUrl`; `destinationUrl` for Login is `"/"`, for Logout is also `"/"` — matching the existing hardcoded navigation targets already in `Login.razor`/`Logout.razor` today, don't invent a new convention here.

### 5. Heimdall — `AuthN.Components.FluentUI`

`Login.razor`'s success branch and `Logout.razor`'s only branch: check `result.DeferredCompletionUrl` first — if non-null, `Navigation.NavigateTo(result.DeferredCompletionUrl, forceLoad: true)`; else keep the existing direct `Navigation.NavigateTo("/", forceLoad: true)`.

## Deliberately not ported from Assurely

- The Android-Chrome-specific comment framing — keep the *mechanism* (meta-refresh over redirect), the specific browser-bug citation is Assurely-era trivia, not load-bearing.
- `UserIdMiddleware`'s anonymous-cookie/lead-id/partner-code machinery — this platform has no equivalent concepts yet; only the login/logout completion shape is relevant here.
- Manual `ClaimsIdentity`/claims-list construction (Assurely's `HttpContextExtensions` builds claims by hand) — `SignInManager.CreateUserPrincipalAsync` already does this correctly against `NorseUser`'s real claims/roles; reuse it instead of re-deriving it.

## Task order (subagent-driven-development, brick by brick, same discipline as the rest of tonight)

1. Midgard — `IDeferredSignIn`/`MemoryCacheDeferredSignIn`/`DeferredSignInEndpointRouteBuilderExtensions`. No dependencies on anything else in this list.
2. Himinbjörg — `NorseSignInManager` + `AddSignInManager<NorseSignInManager>()` wiring + the required integration test proving the override actually intercepts. Depends on (1).
3. Heimdall — `AuthenticationResult.DeferredCompletionUrl` field. No code dependency on (1)/(2), but logically feeds (4).
4. Yggdrasil — completion endpoint mapping + `BlazorServerAuthenticationGateway` changes. Depends on (1) and (3).
5. Heimdall — `Login.razor`/`Logout.razor` navigate-on-deferred. Depends on (3).
6. Re-verify against the live composed system (Playwright + the real Aspire AppHost, same as the rest of Task 10) — Login and Logout both need to actually complete a real sign-in/sign-out round-trip, cookie observed, before Task 10 can be marked done.

After landing and verifying: amend `Glitnir/docs/Heimdall/specs/2026-07-13-authn-identity-split-design.md` with a new section capturing this pattern and its Assurely lineage — that amendment is the deliverable Buvy asked for after the code lands, not before.
