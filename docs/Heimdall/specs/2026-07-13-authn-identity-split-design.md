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
