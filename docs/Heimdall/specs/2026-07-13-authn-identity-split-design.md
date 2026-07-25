# Heimdall/Himinbjörg — Splitting Yggdrasil's ASP.NET Identity Template

**Date:** 2026-07-13
**Status:** Approved design, pre-implementation
**Owner:** Buvy
**Amends:** `2026-06-07-auth-design.md` — the naming (`Norse.Auth.*` → `Norse.AuthN.*`, per the 07-11 rename), the issuance ruling (§3.1's "no `LoginAsync`/`RegisterAsync`/`LogoutAsync` as gRPC methods" is overridden — see §2), and the principal model (`Population` taxonomy is retired from the platform-tier contract — see §4). Its Mongo-SoR retirement (§10) and `SecurityStampValidator`-over-hand-written-validation ruling (§3.3) both still hold and are carried forward unchanged.
**Converges (per):** `2026-07-11-blazor-component-architecture-design.md` §2.2 and §7 — this is Heimdall's own behavioral spec, deferred there pending exactly this session.
**Companion:** `2026-06-28-migrations-framework-identity-schema-design.md` (Himinbjörg's shipped `Norse.Identity` schema — `NorseUser`, `NorseIdentityDbContext`, Postgres `norse_identity`, all referenced as-is, no changes).
**Rides on:** `Platform/specs/2026-07-13-protobuf-net-grpc-reinstated-design.md` — protobuf-net.Grpc is the platform's RPC stack, full stop, not a Heimdall-only exception (see §0).

---

## 0. protobuf-net.Grpc, Per the Reinstated Platform Norm

An earlier draft of this section framed protobuf-net.Grpc as a scoped exception carved out for Heimdall/Himinbjörg alone, against `2026-06-05-ui-composition-design.md` §8's native-stack ruling. That framing is now obsolete: `Platform/specs/2026-07-13-protobuf-net-grpc-reinstated-design.md` reverses §8 platform-wide — no realm carries a native `Grpc.AspNetCore`/`.proto` stack, and none ever shipped one (zero blast radius, see that document's header). Heimdall is simply the **first realm to build against the reinstated norm**, not a carve-out from it.

`AuthN.Components`'s contracts use protobuf-net.Grpc's WCF-derived attribute model directly — `[ServiceContract]` on the interface, `[OperationContract]` per method, `[DataContract]`/`[DataMember]` on request/response records — for the same reason recorded platform-wide: C# records and domain classes are the lingua franca, and a hand-authored `.proto` file would be a second source of truth for a shape the interface already fully expresses.

**What doesn't change:** the mediator discipline. **Every `[OperationContract]` method has a matching Mediator handler** — no business logic lives directly in the gRPC service implementation class; it forwards to `ISender.Send(...)`, same as every other context's generated `{Context}Service` (§3). The generator doesn't yet emit this forwarder for a gRPC-decorated interface (tracked as follow-up work in the reinstatement spec §4) — Heimdall's bootstrap phase hand-writes it until that lands.

---

## 1. Why This Spec Exists

Yggdrasil's `Hosting.Web.Server/Components/Account/*` is, today, the untouched stock `dotnet new blazor --auth Individual` scaffold: `ApplicationUser : IdentityUser`, in-process `UserManager`/`SignInManager`, cookie auth — not even wired to Himinbjörg's real `NorseUser`/`NorseIdentityDbContext`. Splitting it apart is simultaneously the first real integration of this scaffold with the platform's actual identity store.

Two prior specs bear on this work and neither was fully current:

- `2026-07-11-blazor-component-architecture-design.md` fixed the **project shape** — `AuthN.Components` (contract) / `AuthN.Components.FluentUI` (rendering) in Heimdall — but explicitly deferred Heimdall's behavioral spec and left the server-side implementation's home unstated.
- `2026-06-07-auth-design.md` is a full **behavioral** spec, written directly against Buvy's prior-platform (Assurely) `AuthService` (its Appendix A is a method-by-method teaching table). It ruled out `LoginAsync`/`RegisterAsync`/`LogoutAsync` as gRPC methods entirely, calling them ROPC — a conflation this spec corrects (§2). Its Mongo-SoR assumption was formally retired by the migrations-framework spec; its `Norse.Auth.*` naming and `Population`-bearing principal model predate the platform/product split and are retired here.

No `docs/Heimdall/` spec existed before this document.

---

## 2. Issuance Model — Real gRPC, Not OAuth ROPC

**Correction to `2026-06-07-auth-design.md` §3.1:** OAuth 2.1's ROPC prohibition is specifically the shape "an untrusted client collects the resource owner's password and exchanges it for a *token* at an authorization server it doesn't own." A same-trust-boundary client submitting a password to the platform's own server, which verifies it against its own store and mints a **cookie** (never a token), was never an OAuth flow and OAuth 2.1 has nothing to say about it — the same as any ordinary login form. The old spec's blanket "no `LoginAsync`" verdict conflated these two shapes. It is overridden for the cookie-issuing case; the actual ROPC concern (credentials-for-token) is real and is preserved for the one case where it applies (MAUI, below).

**The transport/result matrix that resolves this:**

| Caller | Transport | Result | ROPC? |
|---|---|---|---|
| Blazor Server (Razor component, server-rendered or the server-side half of an Auto-mode component before WASM hydration) | In-process method call — password never serializes onto a wire | Cookie (`HttpContext.SignInAsync`) | No — same process, same trust boundary |
| Blazor WASM (post-hydration, running in the browser) | Real gRPC-over-HTTP/2, TLS | Cookie (browser holds it like any same-origin first-party client) | No — first-party client, same trust boundary, still a cookie not a token |
| MAUI (native) | **Not this contract** — deferred (§6) | N/A | Would be ROPC if it called `Login` directly for a token; instead keeps the existing Auth Code + PKCE design (`2026-06-07-auth-design.md` §4.2) once OpenIddict's authorization endpoints exist |

`IAuthenticationService.Login`/`Register`/`Logout` are real, network-callable `[OperationContract]` methods. They are allowed `HttpContext` coupling (reached via protobuf-net.Grpc's `CallContext`) because minting the credential is their entire job — this is the one deliberate exception to §3.3's "no `HttpContext` in handlers" rule, and it applies to exactly these three methods, nowhere else.

---

## 3. Service Contracts

Both live in `AuthN.Components` (Heimdall), using protobuf-net.Grpc's WCF-derived attribute model — `[ServiceContract]` / `[OperationContract]` / `[DataContract]` (§0). **Every `[OperationContract]` method routes through the platform's mediator** — the gRPC service implementation in `Identity.Web.Server` is a thin forwarder (`Login(request, ct) => sender.Send(request, ct)`), and the actual work lives in an `IRequestHandler<TRequest, Outcome<TResponse>>` per method, same discipline `2026-06-05-ui-composition-design.md` describes for every other context's generated `{Context}Service`, just hand-forwarded here instead of generator-emitted (the generator's current rule pairs `[MediatorService]` with *no* gRPC decoration — see §0 — so it doesn't fire against these interfaces as they stand today; revisit whether the generator should be taught this shape once the exception proves out).

### 3.1 `IAuthenticationService` — issuance

```
[ServiceContract]
IAuthenticationService
  Login(LoginRequest)     -> Outcome<LoginResponse>     [OperationContract]
  Register(RegisterRequest) -> Outcome<RegisterResponse> [OperationContract]
  Logout(LogoutRequest)   -> Outcome                     [OperationContract]
```

`LoginResponse` carries the `SignInResult` state machine as data, not as separate methods: a status (`Succeeded` / `RequiresTwoFactor` / `RequiresConfirmedEmail`) on the `Outcome<T>` success payload; `LockedOut` / `InvalidCredentials` / `NotAllowed` surface as `Outcome` failure categories. One return-type vocabulary (`Outcome`/`Outcome<T>`) across the entire contract — no bespoke `LoginResult` type standing outside it, even though Login/Register are gRPC-native and not mediator-dispatched.

### 3.2 `IAccountService` — lifecycle (the full `Manage/*` surface, not a narrowed subset)

```
[ServiceContract]
IAccountService
  ChangePassword(ChangePasswordRequest)         -> Outcome                [OperationContract]
  SetPassword(SetPasswordRequest)               -> Outcome                [OperationContract]   (external-login-only account adding a password)
  ChangeEmail(ChangeEmailRequest)                -> Outcome                [OperationContract]
  ConfirmEmailChange(ConfirmEmailChangeRequest) -> Outcome                [OperationContract]
  ConfirmEmail(ConfirmEmailRequest)             -> Outcome                [OperationContract]
  ResendEmailConfirmation(ResendEmailConfirmationRequest) -> Outcome      [OperationContract]
  ForgotPassword(ForgotPasswordRequest)         -> Outcome                [OperationContract]
  ResetPassword(ResetPasswordRequest)           -> Outcome                [OperationContract]
  EnableAuthenticator(EnableAuthenticatorRequest) -> Outcome<AuthenticatorSetup> [OperationContract]
  Disable2fa(Disable2faRequest)                 -> Outcome                [OperationContract]
  ResetAuthenticatorKey(ResetAuthenticatorKeyRequest) -> Outcome          [OperationContract]
  GenerateRecoveryCodes(GenerateRecoveryCodesRequest) -> Outcome<RecoveryCodes> [OperationContract]
  GetTwoFactorStatus(GetTwoFactorStatusRequest) -> Outcome<TwoFactorStatus> [OperationContract]
  AddPasskey(AddPasskeyRequest)                 -> Outcome<PasskeySummary> [OperationContract]
  RenamePasskey(RenamePasskeyRequest)           -> Outcome                [OperationContract]
  RemovePasskey(RemovePasskeyRequest)           -> Outcome                [OperationContract]
  ManageProfile(ManageProfileRequest)           -> Outcome<Profile>       [OperationContract]
  DownloadPersonalData(DownloadPersonalDataRequest) -> Outcome<PersonalDataExport> [OperationContract]
  DeletePersonalData(DeletePersonalDataRequest) -> Outcome                [OperationContract]
```

No `HttpContext.SignInAsync` anywhere in `IAccountService` (carried unchanged from `2026-06-07-auth-design.md` §3.3). A claims-affecting operation (email confirmed, password changed, 2FA state changed) bumps the security stamp; `SecurityStampValidator` rebuilds the principal on the next validation cycle — built-in Identity machinery, not hand-written.

**Deferred, not in this contract:** external-login (social provider) linking/challenge/callback. It depends on federation wiring (`2026-06-07-auth-design.md` §8.1's strict-linking policy) that hasn't been re-validated against the product-agnostic scope (§4) yet. Tracked as follow-up, not silently dropped.

### 3.3 Validation

FluentValidation validators for every request type, run **twice**: client-side in the Razor component (`EditForm` integration) for immediate feedback, and server-side in `Identity.Web.Server` as the authoritative check — the wire is never trusted regardless of what the client already validated.

---

## 4. Product-Agnostic Contract — No `Population` Taxonomy

Himinbjörg's shipped schema (`NorseUser`, `NorseRole`, …) is plain ASP.NET Identity v3 — no `Population`/`Producer`/`AgencyId`/fronting-carrier concept anywhere. `2026-06-07-auth-design.md`'s `NorsePrincipal` (`Population` enum: Anonymous/Staff/Producer/Customer/Machine) is insurance-product-specific material that predates the platform/product split; it does not belong in a platform-tier (`Norse.AuthN`/`Norse.Identity`) contract consumed by every future product.

**This spec's contracts carry no `Population`, no `AgencyId`, no `NorsePrincipal` wrapper.** Handlers work against the ordinary `ClaimsPrincipal`/`HttpContext.User`. Any B2B2C taxonomy is a future product's own concern, layered on top via `IClaimsTransformation` or equivalent — not baked into the platform substrate. This retires the `Population`-bearing principal model from Heimdall/Himinbjörg's scope entirely; if a product later needs it, it is that product's own spec.

---

## 5. Project Shape

| Project | Realm | Contents |
|---|---|---|
| `AuthN.Components` | Heimdall | `IAuthenticationService`, `IAccountService`, all `[DataContract]` request/response records, FluentValidation validators. No implementation. |
| `AuthN.Components.FluentUI` | Heimdall | Lift-and-shift-and-clean of Yggdrasil's `Components/Account/*` scaffold, restyled against FluentUI Blazor v5 (RC4), wired against `AuthN.Components`'s contracts instead of calling `SignInManager`/`UserManager` directly. |
| `Identity.Web.Server` *(new)* | Himinbjörg | Implements both service contracts against the existing `Identity` project's `NorseUserStore`/`NorseIdentityDbContext` (in-repo reference — no cross-context violation). Named for the coupling it can't avoid: it always runs inside an HTTP context, bound into Yggdrasil's `Hosting.Web.Server` process via the existing plugin pattern — the same reason a Blazor Server/Auto-mode component's initial render never actually crosses a gRPC wire (§2). |

Himinbjörg's existing `Identity` / `Identity.Migrations` projects are unchanged.

**Amendment (2026-07-25):** the `Identity` project referenced throughout this section was deleted 2026-07-23, folded into `Identity.Web.Server`. Current shape is four live projects: `Identity.Web.Server`, `Identity.Migrations`, `Identity.Migrations.PostgreSQL`, `Identity.Migrations.SqlServer` — the old single-assembly `Norse.Identity` framing is gone.

---

## 6. Explicitly Out of Scope (Deferred)

- **OpenIddict's authorization-server endpoints** (`/connect/authorize`, `/connect/token`) — MAUI's Auth Code + PKCE flow needs these to exist, but nothing in the platform builds them yet, and MAUI's UI isn't part of this work. Separate future spec.
- **External-login (social provider) linking** — see §3.2.
- **`NorsePrincipal`/authorization-model work** (RBAC/ABAC, role taxonomy) — untouched, unretired for whichever product eventually needs it; simply not part of a platform-tier contract per §4.

---

## 7. Phased Delivery (piece by piece, not full-blast)

Each phase gets its own implementation plan; none starts until the prior phase's plan is written and reviewed.

1. **This document** — the reconciliation + contract design.
2. **Bootstrap slice — `IAuthenticationService` only (`Login`, `Register`, `Logout`).** Deliberately narrowed to the 3 issuance methods, end to end, before touching `IAccountService`'s larger surface. The point of this phase is proving the whole pipe with minimal surface area, not shipping a feature:
   - `AuthN.Components` — `IAuthenticationService`, its 3 request/response `[DataContract]`s, validators.
   - `Identity.Web.Server` (Himinbjörg) — the 3 Mediator handlers + thin gRPC forwarders, against the existing `NorseUserStore`.
   - **`Hosting.Web.Client`** — the gRPC-Web client wiring (channel factory, generated client registration) gets stood up for the first time here.
   - **`Hosting.Web.Server`** — hosting the gRPC service + wiring the Mediator pipeline end-to-end gets proven for the first time here.
   - The 3 matching Razor components (`Login`, `Register`, a logout action) in `AuthN.Components.FluentUI`, so the slice is genuinely end-to-end (UI → gRPC → Mediator → Himinbjörg persistence → cookie), not backend-only.
3. **`IAccountService`'s full surface** (`ChangePassword` through `DeletePersonalData`, §3.2) — added now that the pipe from step 2 is proven, in whatever batches make sense once that work starts (e.g., password/email lifecycle first, then 2FA, then passkeys, then personal data) — but the contract, gRPC hosting, and Mediator wiring are already proven, so this is additive, not another bootstrap.
4. **`AuthN.Components.FluentUI`** — the remaining Razor lift-and-shift (everything beyond Login/Register/Logout from step 2), FluentUI v5, wired against `IAccountService`.
5. **Yggdrasil cutover** — delete `Hosting.Web.Server/Components/Account/*` entirely; wire `Hosting.Web.Server`'s `Program.cs` to `AuthN.Components.FluentUI` + Himinbjörg's `Identity.Web.Server`.

---

## 8. References

- `2026-06-07-auth-design.md` (amended by this spec — see header).
- `2026-07-11-blazor-component-architecture-design.md` (project shape converged here).
- `2026-06-28-migrations-framework-identity-schema-design.md` (Himinbjörg's shipped schema, referenced as-is).
- `Platform/specs/2026-07-13-protobuf-net-grpc-reinstated-design.md` (protobuf-net.Grpc reinstated as the platform-wide RPC stack — §0 rides on this).
- `2026-06-05-ui-composition-design.md` §3.2, §7 (`I{Context}Api` / mediator door pattern this spec's forwarder discipline mirrors; §8's native-stack ruling itself is superseded by the reinstatement spec above).
- `2026-05-26-mediator-design.md` (`Outcome<T>`, `[MediatorService]`, the generated-forwarder pattern this spec adapts).
- `Bifrost/CLAUDE.md` §2 (naming model); `Glitnir/CLAUDE.md` §4 (Auth: OpenIddict/OAuth 2.1 decided), §2.8 (subagent-driven, test-driven implementation).

---

## 9. Addendum (2026-07-13/14): The Mediator Is Server-Only; Channel Adapters Decompose `Outcome<T>`

Worked out live with Buvy across two sessions, after Asgard's mediator vocabulary (`Outcome`/`Outcome<T>`/`Problem`/`ErrorCategory`/`ICommandRequest<T>`/`IRequestHandler<T,T>`) shipped as `Norse.Abstractions.Web.Server` v0.0.4 (`Abstractions.Web.Server/Mediator/`, per that realm's own pre-existing project description). This addendum **amends §3.1's contract** and **§3's "thin forwarder … `sender.Send(...)`" framing** — both described `Outcome`/`Outcome<T>` as the thing that crosses the wire, which is now superseded. Everything else in §1–§8 stands unchanged.

### 9.1 The core correction

`Outcome`/`Outcome<T>` never cross the wire, in any direction, on any channel. They are server-only, in-process, and never `[DataContract]`. Each channel — gRPC today, a hypothetical JSON controller later, an in-process Blazor Server call — gets its own separate, dedicated adapter that decomposes `Outcome<T>` into *that channel's own native idiom*. The mediator itself stays completely channel-agnostic; it never knows or cares what's calling it.

This resolves what looked like a real conflict with the platform's "`.Components` never references server-side types" rule: `AuthN.Components` (WASM-referenced) never needs to see `Outcome<T>` at all, because nothing wire-crossing ever is one.

**Asgard follow-up (v0.0.5) — done, staged on `feature/web-server-mediator-channel-adapters`, not yet shipped:** `IRequestHandler<TRequest,TResponse>`'s shipped `where TRequest : ICommandRequest<TResponse>` constraint is dropped. Nothing in this bootstrap dispatches through a generic `ISender` that needs it, and keeping it would have forced `LoginRequest`/`RegisterRequest` (declared in `AuthN.Components`, WASM-referenced) to implement `ICommandRequest<Outcome<BoolResponse>>` — which lives in `Abstractions.Web.Server.Mediator`, server-only — reopening the exact boundary problem this addendum resolves. `ICommandRequest<T>` itself stays, declared but unused, for whenever a real generic dispatcher gets built. `BoolResponse` (§9.4) ships in the same v0.0.5. Buvy's call, confirmed: "we can go back and fix it… that is why I like forcing the walls along the way."

**A second Asgard follow-up was proposed and then explicitly reverted — record this so it doesn't get re-added by mistake.** An earlier draft of this v0.0.5 also added `MediatorFailureException`/`OutcomeExtensions.ThrowIfFailed()` as generic, Asgard-hosted platform law — a `throw`-on-failure convenience sitting right next to `Outcome<T>`. Buvy caught this before it shipped: *"I wanted to go `Outcome<T>` to force handling both success & failure without exception management."* A generic, easily-reached-for throw-helper living in platform law is exactly the discipline leak `Outcome<T>` exists to prevent — every future consumer would reach for the convenient shortcut instead of actually pattern-matching. **`Outcome`/`Outcome<T>` ship with no throw-helper of any kind, anywhere in Asgard, full stop.** Where a channel genuinely needs an exception (gRPC does — see §9.5), that bridge is scoped as a private implementation detail of the specific realm building that channel adapter, never exposed as shared law.

### 9.2 `IAuthenticationService` — revised contract (supersedes §3.1)

```
[ServiceContract]
IAuthenticationService
  Login(LoginRequest)      -> LoginResult   [OperationContract]   // LoginResult { bool Succeeded }
  Register(RegisterRequest) -> void          [OperationContract]   // success = no exception
  Logout(LogoutRequest)    -> void          [OperationContract]
```

No `CallContext` parameter on any method. `CallContext` is a concrete struct from the actual `protobuf-net.Grpc` core package (not the attribute-only `System.ServiceModel.Primitives` package `[ServiceContract]`/`[OperationContract]` come from) — referencing it in the interface would force the widely-shared `AuthN.Components` to carry the full protobuf-net.Grpc package, unjustifiable WASM bloat for a shared contract library every future consumer (MAUI eventually) also references. `Hosting.Web.Client` still needs the real package to build the channel/proxy (Task 5) — that cost is real and unavoidable, but it belongs to the one project that actually needs it, not the contract every consumer pulls in.

**`LoginResponse`/`LoginStatus` (the old `Succeeded`/`RequiresTwoFactor`/`RequiresConfirmedEmail` three-state enum) retire entirely.** 2FA is out of scope for this bootstrap slice (§6), and the original `AuthN.Components.FluentUI` sketch already treated `RequiresTwoFactor` as an error message ("not yet supported here") — nothing is lost, a whole enum disappears.

**`RegisterResponse{ Guid UserId }` retires.** The new user's `Id` is already a claim on the `ClaimsPrincipal` the sign-in cookie carries once they log in — it doesn't need to also travel as a separate wire field, and nothing in `AuthN.Components.FluentUI`'s original sketch ever read it.

`LoginRequest`/`RegisterRequest`/`LogoutRequest` stay plain `[DataContract]` DTOs (§3.3's validators unchanged) — per 9.1's Asgard follow-up, they do **not** implement `ICommandRequest<T>`.

### 9.3 The anti-enumeration principle — which `ErrorCategory` cases survive as real errors

Governing test, stated by Buvy for Register's case and generalized to Login: **collapse a failure into a bare, non-error boolean only when there's no distinct next-action a legitimate user could take from knowing which specific thing failed. Keep a real, distinguishable `Outcome.Err` when there is one.**

- **Login's raw credential check collapses.** "No such user" and "wrong password" both produce `Outcome<BoolResponse>.Ok(new BoolResponse { Value = result.Succeeded })` — `false` included, **never** `Outcome.Err(ErrorCategory.InvalidCredentials)`. `ErrorCategory.InvalidCredentials` retires from this contract's active use. This preserves a protection `SignInManager.PasswordSignInAsync` already has built in (it collapses these itself) rather than re-splitting them one layer up.
- **`LockedOut`/`NotAllowed` (Login) stay real, distinguishable errors.** A locked-out legitimate user needs a different next step (wait, or reset) than "try again" — telling them nothing just sends them into the same retry loop the anti-enumeration collapse elsewhere is trying to prevent.
- **`Conflict` (Register — email already registered) stays a real, distinguishable error.** Buvy's explicit, deliberate call, verbatim: *"we definitely want to bubble up that the email already exists so that they don't try 10000 times."* The standard mitigation for this exact case (always claim success, silently notify the existing account owner) is real and not wrong in principle — it's not built here because it needs actual email-sending infrastructure this bootstrap has nowhere in scope. Named as a **deliberate, deferred gap**, not a silent oversight; revisit when `IAccountService`'s email-confirmation surface gets built (§3.2, §7 phase 3).
- **`Validation` stays real everywhere.** Request-shape errors (empty/malformed email, short password) are never an enumeration vector — they say nothing about whether an account exists.
- **`RegisterHandler`'s `IdentityResult` mapping is corrected, independent of the above:** only `IdentityError.Code is "DuplicateUserName" or "DuplicateEmail"` maps to `ErrorCategory.Conflict`; every other `CreateAsync` failure (password-policy codes: `PasswordTooShort`, `PasswordRequiresDigit`, etc.) maps to `ErrorCategory.Validation`. The original sketch lumped all of these under `Conflict`, which was simply wrong — a rejected password isn't a conflict.

### 9.4 Handlers — ASP.NET Identity boxed entirely inside them

```csharp
public sealed class LoginHandler(SignInManager<NorseUser> signInManager, LoginRequestValidator validator)
	: IRequestHandler<LoginRequest, Outcome<BoolResponse>>
{
	public async ValueTask<Outcome<BoolResponse>> Handle(LoginRequest request, CancellationToken ct)
	{
		var validation = await validator.ValidateAsync(request, ct);
		if (!validation.IsValid)
			return Outcome<BoolResponse>.Err(ErrorCategory.Validation, validation.ToDictionary());

		var result = await signInManager.PasswordSignInAsync(request.Email, request.Password, request.RememberMe, lockoutOnFailure: true);

		if (result.IsLockedOut) return Outcome<BoolResponse>.Err(ErrorCategory.LockedOut);
		if (result.IsNotAllowed) return Outcome<BoolResponse>.Err(ErrorCategory.NotAllowed);

		// Succeeded=false covers "no such user" and "wrong password" identically — deliberate, see §9.3.
		return Outcome<BoolResponse>.Ok(new BoolResponse { Value = result.Succeeded });
	}
}

public sealed class RegisterHandler(UserManager<NorseUser> userManager, RegisterRequestValidator validator)
	: IRequestHandler<RegisterRequest, Outcome<BoolResponse>>
{
	public async ValueTask<Outcome<BoolResponse>> Handle(RegisterRequest request, CancellationToken ct)
	{
		var validation = await validator.ValidateAsync(request, ct);
		if (!validation.IsValid)
			return Outcome<BoolResponse>.Err(ErrorCategory.Validation, validation.ToDictionary());

		var user = new NorseUser { UserName = request.Email, Email = request.Email };
		var result = await userManager.CreateAsync(user, request.Password);

		if (!result.Succeeded)
		{
			var isDuplicate = result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
			var category = isDuplicate ? ErrorCategory.Conflict : ErrorCategory.Validation;
			var errors = result.Errors.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
			return Outcome<BoolResponse>.Err(category, errors);
		}

		return Outcome<BoolResponse>.Ok(new BoolResponse { Value = true });
	}
}
```

`LogoutHandler` unchanged in spirit — `IRequestHandler<LogoutRequest, Outcome>`, always succeeds, calls `signInManager.SignOutAsync()`.

`BoolResponse { bool Value }` — new type, server-only, `Abstractions.Web.Server.Mediator` (Asgard, same v0.0.5 follow-up as §9.1). Not the same type as `LoginResult` (§9.2, wire-crossing, `AuthN.Components`) — same shape, deliberately different types in different assemblies, so a WASM-safe wire type and a server-only mediator payload never get conflated.

`CancellationToken`/`HttpContext` come from a directly-injected `IHttpContextAccessor` (forwarder-level always; per-handler only if that specific handler needs `HttpContext` for something beyond what `SignInManager`/`UserManager` already carry internally — none of these three do). Never from `CallContext`, per §9.2.

### 9.5 Midgard owns the entire channel-translation layer — server and client sides both — so every realm's substrate stays dumb

Converged after three passes live with Buvy — recorded in order since each correction narrowed the design, not just restated it:

1. *"I wanted to go `Outcome<T>` to force handling both success & failure without exception management."* A generic, Asgard-hosted throw-helper sitting next to `Outcome<T>` is exactly the discipline leak `Outcome<T>` exists to prevent. `Outcome`/`Outcome<T>` ship with no throw-helper anywhere in Asgard (§9.1) — settled, unchanged by what follows.
2. An intermediate draft removed exception-based translation entirely (explicit `if (!outcome.IsSuccess) throw ...` inline in every forwarder method, no interceptor at all). Buvy corrected this too: *"the interceptor needs to live in Midgard"* / *"I don't want to roll one for every realm."* A real interceptor is still wanted — automatic, zero boilerplate in any forwarder method, in any realm — just not hand-rolled per realm and not sitting next to `Outcome<T>` in Asgard. The fix for point 1 was never "no interceptor," it was "no interceptor coupled to Asgard's declared law."
3. *"They are Midgard law. Mímir/Mímisbrunnr and Heimdall/Himinbjörg need to be only mindful of the aesir."* Going further than "prove it in Himinbjörg first, extract later" — Midgard (`Norse.Infrastructure.*`) already exists as a live realm (`Infrastructure.Migrations`, `Infrastructure.Components.Theme` both ship there), its charter already names "mediator runtime," and Mímir/Mímisbrunnr is a real, already-named future gRPC-hosted context, not a hypothetical. Building the translation once, now, in Midgard — both the server side (encoding `Outcome<T>` failure into `RpcException`+trailers) and the client side (decoding it back) — means Himinbjörg's `Identity.Web.Server` and Yggdrasil's `Hosting.Web.Client`/`Hosting.Web.Server` are each mindful only of Asgard's declared law and Midgard's embodiment of it. Neither authors translation logic itself; neither will Mímir when its turn comes. *"That's the point of the infrastructure layer — let each service substrate be dumb, just like its components are dumb. Nobody cares except the humans in Midgard who have to make it real — that's where the serialization happens, the deserialization happens, the query context is provided. Nobody else should care, they just ride the rails."*

**New Midgard project: `Infrastructure.Web.Server`** — pairs with Asgard's `Abstractions.Web.Server` the same way every other realm's declared-law/embodied-law split does. Houses `Mediator/Grpc/` (namespace `Norse.Infrastructure.Web.Server.Mediator.Grpc`) — gRPC is the only channel this platform has today; a `Mediator/{OtherChannel}/` sibling is how a JSON channel or any future transport would get the same treatment later, not a rewrite of this one.

**Open technical question, not yet resolved — verify at Task 5, don't solve hypothetically now:** the client-side half of this (below) has to run inside `Hosting.Web.Client`, which compiles to WASM. `Infrastructure.Web.Server`'s name implies server-only, mirroring Asgard's `Abstractions.Web.Server` which genuinely is. Whether the client interceptor can live in the same project as the server interceptor without breaking the WASM build is a real fact, not a design preference — Buvy's call: the DI registration in each host's `Program.cs` is what actually decides which piece gets used where; let a real compile error (if there is one) surface the correct split when Task 5 is actually built, rather than pre-designing around a hypothetical.

**Server side** — a real, automatic interceptor. Nothing a forwarder does looks any different whether this exists or not, except it never has to branch:

```csharp
namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>Thrown only by <see cref="OutcomeExtensions.ThrowIfFailed{T}"/>, caught only by <see cref="OutcomeServerInterceptor"/> — scoped to this project so it's never visible to code that isn't already building a gRPC-hosted mediator handler.</summary>
sealed class OutcomeFailedException(Problem problem) : Exception
{
	public Problem Problem { get; } = problem;
}

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

static class ProblemExtensions
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

Himinbjörg's forwarder (`AuthenticationService : IAuthenticationService`) becomes genuinely one line per method — `ThrowIfFailed()` throws, the interceptor (registered once, wrapping the whole gRPC service) catches, the forwarder itself never branches:

```csharp
public async Task<LoginResult> Login(LoginRequest request) =>
	new() { Succeeded = (await loginHandler.Handle(request, httpContextAccessor.HttpContext!.RequestAborted)).ThrowIfFailed().Value };

public async Task Register(RegisterRequest request) =>
	(await registerHandler.Handle(request, httpContextAccessor.HttpContext!.RequestAborted)).ThrowIfFailed();

public async Task Logout(LogoutRequest request) =>
	(await logoutHandler.Handle(request, httpContextAccessor.HttpContext!.RequestAborted)).ThrowIfFailed();
```

### 9.6 Client-side — a second, WASM-friendly Midgard project mirrors the server one

Corrected once more, same session: an earlier draft of this section put the client interceptor in `Infrastructure.Web.Server` too. Buvy: *"No — client interceptor needs to be `Infrastructure.Web.Client` and be WASM friendly, I may have mistyped."* Separate project, mirroring the exact split Asgard itself already uses (`Abstractions.Web.Server` vs. the WASM-safe `Abstractions.Components`) — a project literally named `...Web.Server` should never be the thing a WASM build references, regardless of whether its actual contents would technically compile.

**New Midgard project: `Infrastructure.Web.Client`** — WASM-friendly, houses `Grpc/` directly (namespace `Norse.Infrastructure.Web.Client.Grpc`), **not** nested under a `Mediator/` folder like the server side. Buvy's correction: *"`Infrastructure.Web.Client` doesn't know what a Mediator is — shrink it to `Grpc`."* Right — the client side never touches `Outcome<T>`/`Problem`/`ErrorCategory` or any other mediator vocabulary (those stay server-only, per §9.1); it's purely gRPC trailer decoding, so the folder shouldn't claim a concept it has no relationship to. `Infrastructure.Web.Server`'s `Mediator/Grpc/` nesting stands unchanged — that side genuinely does translate mediator vocabulary, the nesting there is earned.

This also simplifies the boundary further: the client interceptor never needs to touch Asgard's `Problem`/`ErrorCategory` at all. It only ever needs to decode the trailer bytes directly into the same plain shape `AuthenticationResult` already carries:

```csharp
namespace Norse.Infrastructure.Web.Client.Grpc;

/// <summary>Client-side companion to the server interceptor (Infrastructure.Web.Server, §9.5) — decodes
/// an RpcException's problem-bin trailer directly into the caller's own result shape. Never references
/// Asgard's Problem/ErrorCategory (server-only) — just raw trailer bytes in, a plain dictionary out.</summary>
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

**`AuthenticationResult { bool Succeeded; IReadOnlyDictionary<string,string[]> Errors }`** — new type, `AuthN.Components` (Heimdall), plain record, WASM-safe, no protobuf-net.Grpc dependency. This is what any Razor component actually reads — never `IAuthenticationService` directly, never a caught exception. Convention for `Errors`: field name → messages for field-level errors (unchanged, matches existing FluentValidation `ToDictionary()` output); **empty string (`""`) key → general/model-level messages** (`LockedOut`, `NotAllowed`, `Conflict` — failures not tied to a specific field), matching FluentValidation/Blazor's own convention for a validation message with no associated property, so these flow into the exact same `ValidationSummary`/`ValidationMessageStore` rendering as field errors — no special-casing needed in the UI for "this one isn't about a specific input."

**Amendment (2026-07-25):** `AuthenticationResult` was retired 2026-07-25 by Heimdall's `feature/transport-neutral-gateway` slice (merged, tag v0.0.3) — see §9.8's amendment below for the replacement shape (`IAuthenticationService` + Asgard's `[GenerateGateway]`, returning `Outcome<T>` directly, no wrapper type).

- **`Hosting.Web.Client`** (Task 5) wraps the real gRPC-Web client proxy, using Midgard's `Infrastructure.Web.Client` interceptor to decode `RpcException`, producing `AuthenticationResult`.
- **`Hosting.Web.Server`** (Task 4), for Blazor Server's own Razor components specifically (never for the real gRPC-hosted endpoint, which stays as described in §9.5) — calls the handler directly, no wire, no gRPC concept involved, gets `Outcome<BoolResponse>` back, maps it directly to `AuthenticationResult`. This transform lives in Midgard's `Infrastructure.Web.Server/Mediator/Grpc/`, alongside the server interceptor — same non-WASM constraint, same realm.
- Net effect: whichever host a Razor component renders on, the thing it injects returns `AuthenticationResult` — the component reads `.Succeeded` and populates its `ValidationMessageStore` from `.Errors`. No `try`/`catch` in `Login.razor`/`Register.razor` at all. Exact wiring (what the injected type is called, how it reaches the component — return value vs. some other mechanism) is explicitly left open, Buvy's call: *"the machinery to get there doesn't matter to me as long as everyone else is dumb along the way."* Task 2 (client-facing gateway shape) / Task 7 (component wiring) decide concretely.

### 9.7 Summary of new files this adds, beyond the original bootstrap slice plan

| Realm | File | Note |
|---|---|---|
| Asgard | `Abstractions.Web.Server/Mediator/BoolResponse.cs` | v0.0.5 follow-up, alongside the `ICommandRequest<T>` constraint fix (§9.1) |
| Midgard *(new realm for this plan)* | `Infrastructure.Web.Server/Mediator/Grpc/` — `OutcomeFailedException`, `OutcomeExtensions`, `OutcomeServerInterceptor`, `ProblemExtensions` | §9.5 — new task, sequenced before Himinbjörg's handlers |
| Midgard | `Infrastructure.Web.Client/Grpc/` — `RpcExceptionExtensions` | §9.6 — WASM-friendly sibling, new task, sequenced before Yggdrasil's `Hosting.Web.Client` |
| Heimdall | `AuthN.Components/LoginResult.cs`, `AuthenticationResult.cs`, `IAuthenticationGateway.cs` | wire type + client-safe result + gateway interface, §9.2/§9.6/§9.8 |
| Himinbjörg | `AuthenticationService` forwarder — one line per method, `ThrowIfFailed()` | §9.5 |
| Yggdrasil `Hosting.Web.Server` | `BlazorServerAuthenticationGateway` — calls handlers directly, maps `Outcome<T>` to `AuthenticationResult` inline, no Midgard involvement | Task 5, §9.8 |
| Yggdrasil `Hosting.Web.Client` | `WasmAuthenticationGateway` — wraps the real gRPC client, uses Midgard's `Infrastructure.Web.Client.DecodeProblem()` for the trailer only | Task 6, §9.8 |

Real, reusable platform infrastructure surfacing for the first time through this bootstrap's three operations — two Midgard projects (`Infrastructure.Web.Server`, `Infrastructure.Web.Client`, both new) any future gRPC-hosted mediator handler (starting with Mímir) references directly, no reinvention. Bigger than "hand-wire three ops, prove the pattern" originally implied, but proven against a real caller (this bootstrap) before any second consumer touches it — and built shared from day one specifically because that second consumer (Mímir/Mímisbrunnr) is already named, not speculative.

### 9.8 Correction (2026-07-14, autonomous run): the `Outcome<T>`-to-`AuthenticationResult` mapping is NOT Midgard's job, and neither host had an actual interface to implement

§9.6's own text says the Blazor Server transform "lives in Midgard's `Infrastructure.Web.Server/Mediator/Grpc/`, alongside the server interceptor." That's wrong, caught while writing Task 5/6's actual code (never built, only described in prose up to this point): mapping `Outcome<BoolResponse>` to `AuthenticationResult` requires knowing what `AuthenticationResult` *is* — a Heimdall type. Midgard building that mapping would mean Midgard referencing Heimdall, which breaks the "zero domain knowledge" property that makes `Infrastructure.Web.Server`/`Infrastructure.Web.Client` genuinely reusable by a future, unrelated gRPC-hosted context (Mímir). Midgard's actual job stays exactly what §9.5/§9.6 already correctly scoped: translating `Outcome<T>` ↔ the *wire's* failure idiom (`RpcException`+trailers). Deciding what a *specific* realm's UI-facing result type looks like, and mapping into it, is that realm's own business — here, Heimdall's/Yggdrasil's.

**New shared interface, Heimdall (`AuthN.Components`) — `IAuthenticationGateway`.** Neither host was ever given something concrete to implement; §9.6 left "what the injected type is called" open and this closes it:

```csharp
namespace Norse.AuthN.Components;

/// <summary>
/// The only thing any Razor component injects — never IAuthenticationService directly. Two
/// implementations exist, one per host: Yggdrasil's Hosting.Web.Server (Blazor Server, wraps the
/// mediator handlers directly, no wire) and Hosting.Web.Client (WASM, wraps the real gRPC-Web client
/// proxy). Both produce the same AuthenticationResult shape.
/// </summary>
public interface IAuthenticationGateway
{
	Task<AuthenticationResult> Login(LoginRequest request);
	Task<AuthenticationResult> Register(RegisterRequest request);
	Task<AuthenticationResult> Logout(LogoutRequest request);
}
```

**Amendment (2026-07-25):** this hand-written `IAuthenticationGateway`/`AuthenticationResult` pair — and the per-host `BlazorServerAuthenticationGateway`/`WasmAuthenticationGateway` implementations below — were retired 2026-07-25 by Heimdall's `feature/transport-neutral-gateway` slice (merged, tag v0.0.3). `IAuthenticationService` (in `AuthN.Services`) now carries Asgard's `[GenerateGateway]` attribute; `AuthN.Services` (`NorseGatewayEmissionMode=Contract`) is where Asgard's `GatewayGenerator` emits the equivalent `IAuthenticationGateway` at compile time instead. Login/Register/Logout components inject the generated interface and consume `ValueTask<Outcome<T>>` directly — there is no hand-written gateway type or `AuthenticationResult` wrapper anywhere on this path anymore.

**Yggdrasil `Hosting.Web.Server`'s implementation** — calls the handlers directly (registered by Task 4's `AddNorseAuthenticationService`), no wire, no gRPC, maps `Outcome<T>` to `AuthenticationResult` inline (two branches, not a shared helper — this is realm-specific glue, not generic infrastructure):

```csharp
namespace Norse.Hosting.Web.Server;

using Microsoft.AspNetCore.Http;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.AuthN.Components;

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

**Yggdrasil `Hosting.Web.Client`'s implementation** — wraps the real `IAuthenticationService` gRPC-Web client proxy, using Midgard's `Infrastructure.Web.Client.Grpc.RpcExceptionExtensions.DecodeProblem()` (the one piece of this that IS generic Midgard infrastructure — decoding the wire trailer, not knowing what `AuthenticationResult` is):

```csharp
namespace Norse.Hosting.Web.Client;

using Grpc.Core;
using Norse.AuthN.Components;
using Norse.Infrastructure.Web.Client.Grpc;

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

### 9.9 Correction (2026-07-14, first live E2E run): Blazor Server interactive circuits cannot write auth cookies — `BlazorServerAuthenticationGateway`'s `Login`/`Logout` need a deferred-completion mechanism §9.8's sample code omits

**The defect, as it presented.** The first time this bootstrap slice actually ran end-to-end — Aspire AppHost, real Postgres, a real browser, Docker finally available in the dev environment — `Login.razor`/`Logout.razor` (both `@rendermode InteractiveAuto`) crashed the Blazor Server circuit every time:

```
System.InvalidOperationException: Headers are read-only, response has already started.
   at ...CookieAuthenticationHandler.HandleSignInAsync(...)
   at ...SignInManager<TUser>.PasswordSignInAsync(...)
```

`Register` never hit this — `RegisterHandler` never calls `SignInManager`, no cookie to write. `Login` and `Logout` both do, unconditionally.

**Root cause.** By the time an `InteractiveAuto` component's event handler runs, the page has already switched from its initial HTTP response to the persistent SignalR circuit — `HttpContext.Response.HasStarted` is already `true`. Writing a `Set-Cookie` header at that point is not merely awkward, it is impossible; ASP.NET Core throws rather than silently failing. `§9.8`'s `BlazorServerAuthenticationGateway` sample code calls `loginHandler.Handle(...)` (which internally reaches `SignInManager.PasswordSignInAsync`) directly, in-process, from whatever request context is live when the component's handler runs — for `InteractiveAuto` after the circuit has taken over, that is always a doomed cookie write. This is not new-to-Norse — it is a well-known ASP.NET Core Blazor Server constraint, which is why Microsoft's own stock ASP.NET Core Identity Razor Components template keeps `Login`/`Register`/`Logout` static-SSR-only (no `@rendermode` at all) even inside an otherwise fully-interactive Blazor Web App.

**The fix is proven prior art, not a new design.** Buvy solved this exact problem years earlier at Assurely (his prior D&O insurance MGA), kept as read-only reference material under `../../Assurely` specifically for pearl-mining into this platform. The mechanism, read directly from `Assurely/Hosting/src/Server/Extensions/HttpContextExtensions.cs` and `Middleware/UserId/UserIdMiddleware.cs`:

1. When a sign-in/out can't complete on the current request (there: `context.WebSockets.IsWebSocketRequest`), stash the pending sign-in (or sign-out) server-side under a short-lived, one-time key instead of throwing.
2. Hand the key back to the interactive caller, who forces a full page reload (`NavigationManager.NavigateTo(url, forceLoad: true)`) to a small completion endpoint — a real, distinct HTTP request, where headers are writable again.
3. The completion endpoint consumes the key, performs the actual `HttpContext.SignInAsync`/`SignOutAsync`, and responds with a **meta-refresh HTML page**, not a 302 redirect — Assurely's own comment cites a genuine, long-standing mobile Chrome bug where `Set-Cookie` is silently dropped on a redirect response, looping forever. The mechanism (defer → forced reload → complete on a real request) is what's load-bearing; the specific browser-bug citation is carried forward as cheap insurance, not re-verified.

**The Norse port hooks lower than Assurely's original**, and as a direct consequence needed zero changes to already-shipped, already-tested code. Confirmed via `ilspycmd` against the real installed `Microsoft.AspNetCore.Identity.dll` (not assumed): `SignInManager<TUser>.SignInWithClaimsAsync` (both overloads) and `.SignOutAsync()` are `public virtual` — every one of `PasswordSignInAsync`/`SignInOrTwoFactorAsync`/`SignOutAsync` funnels through them to perform the actual cookie write. Subclassing `SignInManager<NorseUser>` and overriding exactly these three methods intercepts every sign-in/sign-out path uniformly. `LoginHandler.cs`/`LogoutHandler.cs` — Task 4's already-shipped, already-tested handlers — did not change at all; they still call `signInManager.PasswordSignInAsync(...)`/`.SignOutAsync()` exactly as before. All prior lockout/anti-enumeration/2FA-path behavior is untouched by construction, not merely by care.

**New pieces, in dependency order:**

- **Midgard, `Infrastructure.Web.Server/DeferredSignIn/`** — `IDeferredSignIn` (zero domain knowledge: only `ClaimsPrincipal`/`AuthenticationProperties`/`string scheme`, no `NorseUser`, no `Outcome<T>`, reusable by any future realm hosting cookie auth behind an interactive Blazor Server component — same bar as the existing `Mediator/Grpc/` folder), backed by `IMemoryCache` with a 60-second, one-time-use entry; `MapDeferredSignIn()` maps the completion endpoint (a plain minimal-API `MapGet`, meta-refresh response); `AddDeferredSignIn()` registers both.
- **Himinbjörg, `Norse.Identity`** — `NorseSignInManager : SignInManager<NorseUser>` overrides the three methods above. When `Context.Response.HasStarted`, it builds the principal via the base class's own `CreateUserPrincipalAsync`, stashes it via `IDeferredSignIn`, and records the completion key on `HttpContext.Items[NorseSignInManager.DeferredSignInKeyItemName]` for the caller to read back. Otherwise it calls straight through to the base implementation — zero behavior change for every real HTTP request (WASM/MAUI over gRPC-Web, any static-SSR request). Wired via one line in `IdentityBuilderExtensions.AddNorseIdentity()`: `.AddSignInManager<NorseSignInManager>()`.
- **Heimdall, `AuthN.Components`** — `AuthenticationResult` gains `string? DeferredCompletionUrl { get; init; }`. Non-null only when the caller must force-navigate there instead of its normal success path. `WasmAuthenticationGateway` never sets it — gRPC-Web calls are always real, distinct HTTP requests, so sign-in/out always completes immediately there; this field exists purely for the Blazor Server path.
- **Yggdrasil, `Hosting.Web.Server`** — `Program.cs` calls `AddDeferredSignIn()`/`MapDeferredSignIn()`. `BlazorServerAuthenticationGateway.Login`/`.Logout` (not `Register` — nothing to defer there) check `HttpContext.Items[NorseSignInManager.DeferredSignInKeyItemName]` after the handler call and populate `DeferredCompletionUrl` with `$"{DeferredSignInEndpointRouteBuilderExtensions.DefaultPattern}?key={key}&returnUrl=/"` when present. This supersedes §9.8's `BlazorServerAuthenticationGateway` sample for `Login`/`Logout` specifically — `Register`'s branch is unchanged.
- **Heimdall, `AuthN.Components.FluentUI`** — `Login.razor`/`Logout.razor`'s success navigation becomes `Navigation.NavigateTo(result.DeferredCompletionUrl ?? "/", forceLoad: true)` — the deferred case and the (theoretical, never actually hit today) immediate-success case share one line.

**Deliberately not carried forward from Assurely:** the WebSocket-specific detection (`Context.Response.HasStarted` is the more precise, transport-agnostic signal — Microsoft's own recommended check); the anonymous-cookie/lead-id/partner-code machinery baked into Assurely's `UserIdMiddleware` (no equivalent concept exists on this platform yet); manual `ClaimsIdentity`/claims-list construction (`SignInManager.CreateUserPrincipalAsync` already builds the correct principal from `NorseUser`'s real claims/roles — no need to re-derive it by hand).

**Verified live, not just unit-tested.** Beyond `NorseSignInManagerTests`' real `TestServer`-hosted RED/GREEN proof (unmodified `SignInManager<NorseUser>` throws; `NorseSignInManager` defers cleanly and the non-deferred path still writes a real `Set-Cookie`), the full fix was re-verified against the actual composed system: register → login → real `.AspNetCore.Identity.Application` cookie present and `HttpOnly`; logout → cookie confirmed cleared. The crash reported at the top of this section does not reproduce.

Full working notes, including the live investigation trail and the exact Assurely files read: `Glitnir/docs/Heimdall/plans/2026-07-14-deferred-signin-fix.md`.

This is the one design call in this addendum made without Buvy's live sign-off (autonomous run) — flagged clearly for morning review. The reasoning follows directly from principles he already set (Midgard stays domain-agnostic; each realm's substrate owns its own UI-facing glue) rather than introducing a new one, but the concrete shape (`IAuthenticationGateway`'s exact members, the two implementations above) is new and worth a look before Task 8 builds Razor components against it.
