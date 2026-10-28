# Himinbjörg: Identity Account Lifecycle over gRPC — Design

**Realms touched:** Himinbjörg (handlers, `.Outcome` views, `NorseSignInManager`, scaffold retirement), Heimdall (contracts, validators, component library), Bragi (fakes and stories), Asgard (`NorseRoles.Anonymous`), Midgard (anonymous-role alias, client-side policy emitter), Yggdrasil (composition bumps, logout and passkey script cleanup), Glitnir (this verdict).
**Closes:** [Himinbjorg#48](https://github.com/NorseArchitecture/Himinbjorg/issues/48).
**Depends on nothing open.** **Depended on by:** the OpenIddict authorization-code + PKCE spec ([Himinbjorg#50](https://github.com/NorseArchitecture/Himinbjorg/issues/50), [#52](https://github.com/NorseArchitecture/Himinbjorg/issues/52)) and the MAUI head spec, both unwritten; see §3.
**Amends:** `../../Heimdall/specs/2026-07-13-authn-identity-split-design.md` §3.2 (the `IAccountService` shape proposed there is superseded by §4); `../../Platform/specs/2026-06-07-auth-design.md` §6 (`IAccountApi` is never built; this is its realization); `2026-10-24-identity-explicit-writes-temporal-split-design.md` §4.12 (the `Components/` IL-scan exemption ends with Train 3); `../../Asgard/specs/2026-08-11-session-transition-seam-design.md` §9 (registration-forces-verification stays parked, §11).
**Revision:** r3, 2026-10-26 — r1 was the brainstorm verdict; rulings 1–12 were each approved in-session before it was written. r2 answers the first review pass (four findings, verified against the installed `Microsoft.AspNetCore.App 11.0.0-rc.3.26522.148` and `NorseUserStore`): the two-factor challenge carries and validates the security stamp (§5.3), passkey attestation is bound to the principal completing it (§5.5), recovery-code generation preserves the store's commit outcome instead of collapsing it to `Fault` (§5.2), and removing the last usable sign-in method is refused under a row lock (§5.9). r3 answers the second pass: the lossless recovery-code path reaches `NorseUserManager` through an `IRecoveryCodeReplacement` interface declared in `Identity.Outcomes`, because `Identity.Web.Server` already references `Identity.Outcomes` and the view's receiver is `UserManager<NorseUser>` (§5.2). **Amended 2026-10-28 (Train 3):** amendments A1–A10 of `../plans/2026-10-27-identity-account-lifecycle-train-3.md` are marked inline below, and §15 records Train 3 as built.

## 1. Context

Login and Register run as gRPC operations behind Heimdall components today, on Blazor Server and on WASM. Every other Identity flow — forgot and reset password, email confirmation, two-factor sign-in, the whole `/Account/Manage` tree, passkeys, erasure — still lives in `Himinbjorg/src/Identity.Web.Server/Components` as the static-SSR scaffold ASP.NET Core Identity ships. That folder is why Heimdall is not yet a publishable component library: the pages inject `UserManager` and `SignInManager` directly, use antiforgery form posts, store the two-factor hop in a cookie that only a browser round-trip can set, and carry the one exemption left in `OutcomeSurfaceEnforcementTests` ("until #48").

The 2026-07-13 split spec sketched an `IAccountService` for all of this but never built it; the 2026-08-06 validation-composition spec built the mechanism every converted form now lands on (`OutcomeFormComponentBase`, `ApplyServerErrors`, `Result`-hydrating setters, generated discovery); the 2026-08-11 seam spec gave every sign-in transition one door (`ISessionTransition`); the 2026-08-21 principal-at-the-door spec put every gRPC endpoint on the identity-cookie lane; the 2026-10-24 store spec made every Identity result reach Norse code as an `Outcome<T>`. The remaining work is composition, not invention.

**Outcome sought:** every server-side Identity interaction is a gRPC operation on a Heimdall contract, every page is a Heimdall component that renders identically on Blazor Server and WASM (and on MAUI once a head exists), Himinbjörg's `Components/` folder is gone, and the IL-scan exemption with it.

## 2. Rulings (brainstorm 2026-10-26)

1. **Decomposition.** The original ask — all Identity over gRPC *and* a component library for WASM, WebSocket, and MAUI — is three sub-projects: **(A)** the account lifecycle over gRPC (this spec), **(B)** OpenIddict authorization code + PKCE with bearer validation on the gRPC lane, **(C)** the MAUI head. A ships first; B and C get their own specs.
2. **MAUI sign-in stays the 06-07 §4.2 ruling**: system-browser Authorization Code + PKCE, Login / Register / two-factor web-rendered during the hop, every account-management component native. "Stay signed in" persists the refresh token in `SecureStorage` (Keychain, Android Keystore, DPAPI); the access token lives in memory. Belongs to B/C; recorded here so A's component shapes do not fight it.
3. **Two contract tiers.** Anonymous-tier operations join `IAuthenticationService` under `AuthNPolicies.Public`. Self-service operations go on a new `IAccountService` under `AuthNPolicies.Account`, which requires a non-anonymous principal (§4.4). *(Amended 2026-10-28 (Train 3, T3-20): each tier is still one policy, now spread across several contracts. Train 3 adds `ITwoFactorService` and `IPasskeyService` under `AuthNPolicies.Account` and `ISignInMethodsService` under `AuthNPolicies.Public`, and `IAccountService` keeps its eight Train 2 operations. Growing `IAccountService` would have broken the floating `master` builds of Himinbjörg and Bragi between Heimdall's publish and their merges.)*
4. **The two-factor challenge crosses the wire as a data-protected token**, not a cookie. Login's `RequiresTwoFactor` outcome carries it; the two-factor and recovery-code sign-ins present it with the code. The `TwoFactorUserId` cookie is never written again. Only the remember-this-machine cookie survives, and it rides the deferral (§5.3–§5.4).
5. **External login**: the OAuth redirect endpoints stay HTTP in Himinbjörg (already hidden from OpenAPI); the `Manage/ExternalLogins` list and remove convert to gRPC. Nothing is deleted that a provider could later need; nothing is ported that nothing renders.
6. **Three trains by tier** (§9), one fork per realm, serial within a realm. Asgard and Midgard prerequisites front-load into Train 1 so Trains 2 and 3 are pure Heimdall → Himinbjörg → Bragi → Yggdrasil. *(Amended 2026-10-28 (Train 3, T3-19): Train 3 ran an Asgard phase (`OutcomeComponentBase.CanAct`) and a Midgard phase (an identity principal's failed page navigation goes to AccessDenied) in front of Heimdall, because dockets 3 and 4 are upstream-shaped. §9 item 3 records the development and release orders.)*
7. **Registration behavior is unchanged** (parity). Flipping `RequireConfirmedAccount` and real email delivery wait for the messaging realm (§11).
8. **Story authentication is scenario-driven**: a `FakeAuthenticationStateProvider` yields a fixed principal when the active scenario says signed-in; `FakeAccountService` and `FakeIdentityService` are stateless and keyed off scenario enums (§7).
9. **Manage reads are page-per-operation plus one `GetAccountStatus` query** that feeds the status pages; email and phone come from the existing `IIdentityService.GetMyPersonalData`.
10. **Post-reload status messages ride a server-authored `?status=<key>` on `NextUrl`**; the Manage page maps the key to text. The scaffold's cookie-backed redirect manager stays retired.
11. **`ConfirmPassword` is dropped everywhere.** The validator that runs client-side runs again server-side on the deserialized record, so a client-only confirm buffer cannot exist; a wire field that sends the password twice is not worth the parity. FluentUI's password input has a visibility toggle.
12. **Token-consuming pages confirm on a click, never on load.** InteractiveAuto prerender runs `OnInitializedAsync` twice and mail scanners prefetch links; a load-time confirm would spend a one-shot token before the user arrives.

## 3. Scope and dependents

| Sub-project | Record | Status |
|---|---|---|
| **A** — account lifecycle over gRPC | this spec | designed |
| **B** — OpenIddict `authorization_code` + PKCE, refresh tokens with rotation and reuse detection, `Norse.Machine`-style bearer validation for *user* tokens on the gRPC lane | own spec, after A | not started |
| **C** — `Norse.Hosting.App` MAUI head: iOS, Android, Mac Catalyst, Windows; `BlazorWebView` hosting Heimdall's components; `WebAuthenticator` PKCE; `SecureStorage` refresh token | own spec, after B | not started |

A's exit criterion "verified on all three hosts" is satisfied on Blazor Server and WASM when Train 3 ships and re-verified on MAUI when C lands. Nothing in A assumes a browser except the passkey ceremony (§5.5), which is browser JS by nature.

## 4. Contracts (Heimdall `src/AuthN.Services`)

### 4.1 Conventions

Every record is `[DataContract] sealed record` with `[DataMember(Order = n)]`. Email and phone are `Result<EmailAddress>` / `Result<PhoneNumber>` with the un-serialized `EmailInput` / `PhoneInput` buffer whose setter parses, exactly like `LoginRequest`. Tokens, codes, and credential ids are opaque strings the client never decodes. Request-less operations are `CancellationToken`-only and are wrapped server-side in a `{Op}Command(Unit)` the way `LogoutCommand` is.

**Return-type rule.** Anything that signs in, signs out, or refreshes the sign-in returns `Outcome<NavigationResult>`, because on a Blazor Server circuit those are deferred cookie writes that complete only through `/_auth/complete`. Acknowledgment-only operations return `Outcome<BoolResponse>` (its doc names exactly this use; `Outcome<Unit>` has never crossed the wire and the surrogate emitter registers per payload type). Data operations return a new record.

`Heimdall/tests/AuthN.Services.Tests/RequestContractTests.cs` extends its arrays with every new record and the new contract: purity lock, `[OperationContract]` on every method, trailing `CancellationToken`.

### 4.2 `IAuthenticationService` additions (policy `AuthNPolicies.Public`)

| Operation | Request | Returns | Notes |
|---|---|---|---|
| `TwoFactorSignIn` | `{ Challenge, Code, RememberMachine }` | `NavigationResult` | code stripped of spaces and hyphens server-side |
| `RecoveryCodeSignIn` | `{ Challenge, RecoveryCode }` | `NavigationResult` | never persistent (parity) |
| `ForgotPassword` | `{ Email }` | `NavigationResult` | always `/Account/ForgotPasswordConfirmation`; unknown, erased, and unconfirmed answer identically (anti-enumeration) |
| `ResetPassword` | `{ Email, Code, Password }` | `NavigationResult` | success → `/Account/ResetPasswordConfirmation`; unknown or erased user, malformed token, and invalid token against a real account all answer the handler's one static `InvalidCredentials` instance ("The reset link is invalid or has expired.") — a success answer for an unknown email would let a caller with a junk token tell known accounts from unknown ones (ruled 2026-10-26, Train 1 Task 4.6); password rules → `Validation` regrouped onto `Password` |
| `ConfirmEmail` | `{ UserId, Code }` | `BoolResponse` | `NotFound` / `InvalidCredentials` pass through |
| `ConfirmEmailChange` | `{ UserId, Email, Code }` | `NavigationResult` | `NorseUserManager.ChangeEmailAsync` sets email and username together (law); refreshes the sign-in when the caller *is* that user, otherwise `NextUrl = /Account/Login` |
| `ResendEmailConfirmation` | `{ Email }` | `BoolResponse` | always true; also serves the signed-in "send verification email" button |

**`Login` changes once.** On `RequiresTwoFactor` the handler returns `NextUrl = /Account/LoginWith2fa?challenge=<token>` (PathBase-qualified). Remember-me lives inside the token; the `RememberMe` query parameter and the deferred-completion detour on that branch disappear. `Login.razor` is unchanged: it still hands every result to `ISessionTransition.Begin` and cannot tell a two-factor hop from a completed sign-in. That is the point — no flag to branch on.

### 4.3 `IAccountService` (new; `[ServiceContract(Name = "grpc.account.v1.AccountService")]`; policy `AuthNPolicies.Account`)

*Amended 2026-10-28 (Train 3, T3-20): the policy names the tier, not the contract. `AuthNPolicies.Account` now covers `IAccountService`, `ITwoFactorService` and `IPasskeyService`; the Train 3 rows below moved to those contracts (see the note after the table).*

**Train 2**

| Operation | Request | Returns |
|---|---|---|
| `GetAccountStatus` | — | `AccountStatusResponse { HasPassword, EmailConfirmed, TwoFactorEnabled, HasAuthenticator, RecoveryCodesLeft, IsMachineRemembered, PasskeyCount, ExternalLoginCount }` |
| `SetPhoneNumber` | `{ Phone? }` (null clears) | `NavigationResult` (`SetPhoneNumberAsync` rotates the security stamp; an unchanged number is a no-op success, §14) |
| `ChangePassword` | `{ OldPassword, NewPassword }` | `NavigationResult` |
| `SetPassword` | `{ NewPassword }` | `NavigationResult` (`UserAlreadyHasPassword` → `Conflict`) |
| `ChangeEmail` | `{ NewEmail }` | `BoolResponse` (unchanged email → `Validation`; sends the change link) |
| `GetExternalLogins` | — | `ExternalLoginsResponse { Logins[] { LoginProvider, ProviderKey, DisplayName }, AvailableProviders[] { Name, DisplayName }, CanRemove }` |
| `RemoveExternalLogin` | `{ LoginProvider, ProviderKey }` | `NavigationResult`; refused with `Conflict` when it would remove the last usable sign-in method (§5.9) — `CanRemove` is presentation, not the guard |
| `DeletePersonalData` | `{ Password }` | `NavigationResult` (§5.6) |

**Train 3**

| Operation | Request | Returns |
|---|---|---|
| `GetAuthenticatorSetup` | — | `AuthenticatorSetupResponse { SharedKey, AuthenticatorUri, QrCodeSvg }` — QR rendered server-side so WASM carries no QR library |
| `EnableAuthenticator` | `{ Code }` | `RecoveryCodesResponse { RecoveryCodes[] }` — codes generated only when none exist |
| `ResetAuthenticator` | — | `NavigationResult` |
| `DisableTwoFactor` | — | `BoolResponse` (not enabled → `Validation`) |
| `GenerateRecoveryCodes` | — | `RecoveryCodesResponse` |
| `ForgetTwoFactorClient` | — | `NavigationResult` (cookie delete rides the deferral) |
| `GetPasskeys` | — | `PasskeysResponse { Passkeys[] { CredentialId, Name } }` |
| `GetPasskeyCreationOptions` | — | `PasskeyCreationOptionsResponse { OptionsJson, AttestationState }` |
| `AddPasskey` | `{ CredentialJson, AttestationState }` | `PasskeyEntry` (so the client can route to rename) |
| `RenamePasskey` | `{ CredentialId, Name }` | `BoolResponse` |
| `RemovePasskey` | `{ CredentialId }` | `BoolResponse` |

*Amended 2026-10-28 (Train 3, T3-20, T3-10, T3-2): the Train 3 rows moved to three new contracts (seven, six and three operations), and `IAccountService` stays at eight.*
- *`ITwoFactorService` (`grpc.twofactor.v1.TwoFactorService`, `AuthNPolicies.Account`): `GetAuthenticatorSetup`; `EnableAuthenticator` → `NavigationResult`; `RevealRecoveryCodes { Handoff }` → `RecoveryCodesResponse` (new); `ResetAuthenticator`; `DisableTwoFactor` → `NavigationResult`; `GenerateRecoveryCodes`; `ForgetTwoFactorClient`.*
- *`IPasskeyService` (`grpc.passkey.v1.PasskeyService`, `AuthNPolicies.Account`): `GetPasskeys`; `GetPasskey { CredentialId }` → `PasskeyEntry` (new); `GetPasskeyCreationOptions`; `AddPasskey` → `NavigationResult`; `RenamePasskey` → `NavigationResult`; `RemovePasskey` → `BoolResponse`.*
- *`ISignInMethodsService` (`grpc.signinmethods.v1.SignInMethodsService`, `AuthNPolicies.Public`, all new): `GetSignInMethods { ReturnUrl }` → `SignInMethodsResponse { PasswordSignInUrl, PasskeySignInUrl, Providers[] { DisplayName, StartUrl } }`; `GetPasskeyRequestOptions` → `{ OptionsJson, AssertionState }`; `PasskeySignIn { CredentialJson, AssertionState, RememberMe, ReturnUrl }` → `NavigationResult`.*
- *`PasskeyEntry` carries a server-built `RenameUrl`, because the client authors no flow URL (T3-10). `LoginRequest` gains `ReturnUrl` (Order 4, T3-2).*

### 4.4 Policy: the anonymous principal is authenticated

The anonymous-cookie principal minted by `Norse.Browser` has `IsAuthenticated == true` and the role `anonymous` (`Midgard/src/Infrastructure.Web.Server/Authentication/NorseAnonymousHandler.cs`). `RequireAuthenticatedUser()` therefore admits it; today `IdentityPolicies.Self` is satisfied by an anonymous caller and only the handler's `GetUserAsync` fold refuses. A self-service tier needs an explicit exclusion, and Heimdall cannot name Midgard's constant (NORSE071).

- Asgard `Abstractions.Components/Authorization/NorsePolicies.cs` gains `NorseRoles.Anonymous = "anonymous"`. Midgard's `NorseAnonymousOptions.AnonymousRole` aliases it.
- Heimdall declares `AuthNPolicies.Account = "AuthN.Account"` through `[NorsePolicy]`: `RequireAuthenticatedUser().RequireAssertion(ctx => !ctx.User.IsInRole(NorseRoles.Anonymous))`. `IdentityPolicies.Self` is hardened identically in the same PR.
- Every `IAccountService` command carries `[Authorize(Policy = AuthNPolicies.Account)]` (NORSE011), so the mediator's `AuthorizationBehavior` is the defense behind the route gate; a bypassed route still renders `Forbidden` in place.
- **WASM has no policies registered.** `Hosting.Web.Client/Program.cs` calls only `AddAuthorizationCore()`; the `AddNorsePolicies()` emitter lives in Midgard's *server* generator. Any `[Authorize(Policy = …)]` the WASM router evaluates throws "policy not found". Midgard's `Infrastructure.Web.Client.Generator` links the same `PolicyDeclarationDiscovery` + `PolicyRegistrationEmitter`; the client host adds the one call. `AddAuthenticationStateSerialization` already serializes name and role claims, so `IsInRole("anonymous")` evaluates identically client-side.

## 5. Server (Himinbjörg `src/Identity.Web.Server`)

### 5.1 Handlers

One `{Op}Command` + `{Op}Handler` per operation, the `LoginHandler` / `RegisterHandler` shape. Anonymous-tier handlers sit flat beside `LoginHandler.cs`; account-tier handlers under `Account/` with `AccountService.cs`, a hydrate-and-send mirror of `AuthenticationService.cs`, registered in `ServiceCollectionExtensions.cs`. Every account handler starts `principalAccessor.GetPrincipalAsync` → `userManager.Outcome.GetUserAsync(principal)` (`NotFound` / `Erased` fold), never `HttpContext.User`. Validators are assumed to have run (tripwire guards for stamped scalars, as `RegisterHandler` does); password-rule errors regroup onto the password field through `RegisterHandler.FieldFor`. Email links are built absolute with `UriHelper.BuildAbsolute(scheme, host, pathBase, …)` and handed to `IEmailSender<NorseUser>`, which stays the no-op (§11).

### 5.2 `.Outcome` views

The IL scan bans only `IdentityResult`- and `SignInResult`-returning members. `UserManagerOutcomeView` (`Identity.Outcomes`) gains, each through `IdentityOutcomeMap.GuardAsync` + `ToOutcome`: `GetUserAsync(ClaimsPrincipal)`, `FindByIdAsync`, `CreateAsync`, `ResetPasswordAsync`, `AddPasswordAsync`, `ChangePasswordAsync`, `SetPhoneNumberAsync`, `ConfirmEmailAsync`, `ChangeEmailAsync`, `SetTwoFactorEnabledAsync`, `ResetAuthenticatorKeyAsync`, `AddLoginAsync`, `RemoveLoginAsync`, `AddOrUpdatePasskeyAsync`, `RemovePasskeyAsync`, and `ReplaceRecoveryCodesAsync`.

**Recovery codes keep their commit outcome.** The framework's `GenerateNewTwoFactorRecoveryCodesAsync` calls `UpdateUserAsync` and returns `null` whenever the result is not `Succeeded` — the `IdentityResult` is discarded. On this store that result can carry `NorseIdentityErrorCodes.IndeterminateOutcome` (`NorseUserStore.ResolveAsync`, the 10-24 spec's commit-outcome contract) with no exception in flight, and the replacement codes may already have committed, invalidating the old set, while the caller sees `null`. A view over the framework method would turn that into a definite `Fault`.

The lossless path crosses an assembly boundary: `UserManagerOutcomeView` lives in `Identity.Outcomes` and receives `UserManager<NorseUser>`, while `NorseUserManager` lives in `Identity.Web.Server`, which already references `Identity.Outcomes` (the reverse reference would be a cycle). So the seam is an interface declared beside the view:

- `Identity.Outcomes` declares `IRecoveryCodeReplacement` with one member, `Task<(IdentityResult Result, string[] Codes)> ReplaceRecoveryCodesAsync(NorseUser user, int number)` — `IdentityResult` and `NorseUser` are both visible there today.
- `NorseUserManager` implements it: generate the codes with the protected `CreateTwoFactorRecoveryCode`, call the store's `ReplaceCodesAsync`, then `UpdateUserAsync`, and return the result untouched. `GenerateNewTwoFactorRecoveryCodesAsync` is overridden to delegate to it so no caller can reach the lossy base.
- `UserManagerOutcomeView.ReplaceRecoveryCodesAsync(user, number)` → `Outcome<string[]>` does `userManager is IRecoveryCodeReplacement lossless ? … : throw new InvalidOperationException(…)`. The same type-test shape is planned for the `SignInManagerOutcomeView` members that exist only on `NorseSignInManager` (§5.3, §5.5); neither view carries it today. A stock manager fails loud at the first call rather than silently reporting `Fault`.
- **Wiring.** `GenerateRecoveryCodesHandler` and `EnableAuthenticatorHandler` inject `UserManager<NorseUser>` as every handler does (`AddUserManager<NorseUserManager>()` already resolves it to the Norse manager) and call `userManager.Outcome.ReplaceRecoveryCodesAsync(user, 10)`; neither names `NorseUserManager`. `OutcomeSurfaceEnforcementTests` adds `GenerateNewTwoFactorRecoveryCodesAsync` to its ban list by name — it returns `IEnumerable<string>?`, so the return-type scan alone would never catch a caller drifting back to it.

`SignInManagerOutcomeView` (`Identity.Web.Server`) gains `TwoFactorChallengeSignInAsync`, `RecoveryCodeChallengeSignInAsync`, `ExternalLoginSignInAsync`, `MakePasskeyCreationOptionsAsync`, `PerformPasskeyAttestationAsync`. Attestation-failure mapping lives here, not in `Identity.Outcomes`, which is non-web and cannot see `Microsoft.AspNetCore.Identity`. `IdentityOutcomeMap` gains exactly one row, `NorseIdentityErrorCodes.LastSignInMethod → Conflict` (§5.9); `Every_IdentityErrorDescriber_code_has_an_explicit_mapping` stays green untouched.

*Amended 2026-10-28 (Train 3, T3-13, T3-15, T3-18): the views also gained a passwordless `CreateAsync(user)` for external registration, `ProvisionAuthenticatorKeyAsync`, `ExternalLoginSignInAsync`, and the passkey views over T3-15's manager methods. `ProvisionAuthenticatorKeyAsync` provisions a missing authenticator key losslessly through `IAuthenticatorKeyProvisioning` without rotating the stamp. It is a conditional write that stores the key only where it is still null and answers the stored key, so two first setups in two tabs answer the same key. Both challenge redemptions return the landing carried inside the challenge token.*

Read-only manager calls (`HasPasswordAsync`, `CheckPasswordAsync`, `IsEmailConfirmedAsync`, `GetTwoFactorEnabledAsync`, `GetAuthenticatorKeyAsync`, `CountRecoveryCodesAsync`, `GetLoginsAsync`, `GetPasskeysAsync`, `VerifyTwoFactorTokenAsync`, `Generate*TokenAsync`, …) stay raw: they operate on an already-materialized instance and need no `KeyDestroyedException` fold.

### 5.3 `NorseSignInManager`: the two-factor challenge

`NorseSignInManager` takes `IDataProtectionProvider` and creates two time-limited protectors: `Norse.Identity.TwoFactorChallenge.v1` (five minutes) and `Norse.Identity.PasskeyAttestation.v1` (`IdentityPasskeyOptions.AuthenticatorTimeout`).

- **`SignInOrTwoFactorAsync` override.** When two-factor is enabled and the machine is not remembered: mint `v1|userId|securityStamp|isPersistent|loginProvider` (a delimited string — NORSE070 bans System.Text.Json here), park it on `HttpContext.Items`, return `TwoFactorRequired`. The base is never called on this branch; the `TwoFactorUserId` cookie is never written or stashed; `StoreTwoFactorInfo` is deleted. Every other branch delegates to the base, which reaches the existing deferral through the virtual `SignInWithClaimsAsync`.
- **The stamp is revocation.** The framework's `StoreTwoFactorInfo` puts the security stamp into the two-factor cookie and `RetrieveTwoFactorInfoAsync` validates it, so a password reset or any other stamp rotation kills an outstanding challenge. The five-minute expiry does not preserve that; the token therefore carries the stamp, and both redemption paths compare it against `GetSecurityStampAsync` after `FindByIdAsync` and answer `Failed` on a mismatch — before the second factor is even looked at.
- **`TwoFactorChallengeSignInAsync(token, code, rememberClient)`** reproduces the base `TwoFactorAuthenticatorSignInAsync` with the token as the information source: unprotect (`CryptographicException` → `Failed`) → `FindByIdAsync` → stamp check → `PreSignInCheck` → `VerifyTwoFactorTokenAsync` → `ResetLockout` → optional `RememberTwoFactorClientAsync` → `SignInWithClaimsAsync` with `amr=mfa` (and the provider claim when present). Failure increments `AccessFailedAsync` when lockout is supported and reports `LockedOut` when it tripped.
- **`RecoveryCodeChallengeSignInAsync(token, code)`**: same prelude including the stamp check; `RedeemTwoFactorRecoveryCodeAsync`; completion with `isPersistent: false`, no remember; failure does not count toward lockout (parity).
- `LoginHandler` reads the parked token; a `RequiresTwoFactor` result without one is an `InvalidOperationException` (programming error, fail loud).
- **Stateless by choice.** The token is a bearer for its full five minutes with no one-shot revocation on success: a replay inside the window still needs a fresh second factor, and the stamp check is the only server-side kill switch — the same exposure the framework's `TwoFactorUserId` cookie had, which is also a stateless ticket with no server-side revocation. The token rides the URL (browser history and `Referer` until Heimdall's interactive history strip runs; ASP.NET Core's default `url.query` redaction keeps it out of telemetry).

The `SignInManager` internals this reproduces (`DoTwoFactorSignInAsync`, `StoreRememberClient`) must be re-verified by decompile against the installed `Microsoft.AspNetCore.App 11.0.0-rc.3` before coding — the ritual `NorseSignInManager.cs` already records.

*Amended 2026-10-28 (Train 3, T3-15, T3-18, T3-21):*
- *`NorseSignInManager` holds three time-limited protectors, for the two-factor challenge, the passkey attestation and the passkey assertion, plus the one-shot recovery-codes handoff (T3-12). The passkey manager methods carry T3-15's names, distinct from the base.*
- *The challenge payload is now `v2|userId|stamp|1/0|provider|returnUrl` (split count 6), so the landing crosses the challenge. A v1 token is refused like an expired one.*
- *External login requires the second factor. The `/Account/ExternalLogin` callback signs in with `bypassTwoFactor: false` and clears the external cookie before the challenge redirect. `NorseSignInManager.ExternalLoginSignInAsync` throws `InvalidOperationException` if it is ever passed `true`, so no path can regrow the scaffold's bypass.*

### 5.4 Remember-this-machine and deferred completion

`RememberTwoFactorClientAsync` and `ForgetTwoFactorClientAsync` are the two raw cookie writes left after the `TwoFactorUserId` cookie dies. Both are overridden: when the response has not started they write directly; otherwise they stash under `IdentityConstants.TwoFactorRememberMeScheme` through `IDeferredSignIn`. `RefreshSignInAsync` needs no override — the base authenticates the identity cookie (present on both lanes) and calls the virtual `SignInWithClaimsAsync`; a test pins that it reaches the stash. Operations that rotate the security stamp (`SetPhoneNumber`, `ChangePassword`, `SetPassword`, `RemoveExternalLogin`, `ConfirmEmailChange`, `ResetAuthenticator`) refresh the sign-in and return `NavigationResult`; without the refresh, revalidation signs the user out within thirty minutes.

A two-factor login with "remember this machine" on a circuit stashes two sign-ins. A new `DeferredCompletion.TryBuildUrl(returnUrl)` (Himinbjörg, replacing the per-handler `TryGetDeferredCompletionUrl` copies now that there are a dozen call sites) composes chained `/_auth/complete` hops: the inner completes the remember cookie and returns to `returnUrl`, the outer completes the identity cookie and returns to the inner. No Midgard change; the keys live sixty seconds and two hops fit comfortably. If the chain proves brittle, the fallback is `DeferredSignInAction` carrying multiple actions (an Asgard + Midgard change).

### 5.5 Passkeys

`IPasskeyHandler<NorseUser>` is public in the installed Identity: `MakeCreationOptionsAsync(PasskeyUserEntity, HttpContext)` → `{ CreationOptionsJson, AttestationState }` and `PerformAttestationAsync(PasskeyAttestationContext)`. `NorseSignInManager.MakePasskeyCreationOptionsAsync(user)` calls the handler and protects `AttestationState` with the second protector; `PerformPasskeyAttestationAsync(credentialJson, protectedState)` unprotects and completes. **The ceremony is bound to the account completing it.** The framework's attestation state carries the `UserEntity` the options were made for and `PerformAttestationAsync` hands it back beside the credential, but nothing in the framework compares it with the caller; the scaffold's `AddOrUpdatePasskeyAsync(currentUser, result.Passkey)` would attach a credential minted under A's user handle to B whenever B submits A's untampered ceremony. `AddPasskeyHandler` therefore requires `result.UserEntity.Id` to equal the principal-derived user id before `AddOrUpdatePasskeyAsync`, answering `Forbidden` otherwise. This bypasses the base `MakePasskeyCreationOptionsAsync` / `PerformPasskeyAttestationAsync`, which persist the state in a private data-protected cookie between the two requests — stateless on the server, consistent with ruling 4. The `/Account/PasskeyCreationOptions` and `/Account/PasskeyRequestOptions` endpoints are deleted (the second is used by nothing). ~~Passkey *login* (`PasskeySignInAsync`) is not in scope; `passkeys.js` keeps a `request` export for that follow-on.~~

*Amended 2026-10-28 (Train 3, T3-15, T3-31, T3-32, T3-33, T3-16):*
- *Passkey sign-in is in scope and shipped. Registration runs through `CreatePasskeyOptionsAsync`/`AttestPasskeyAsync`, and sign-in through `CreatePasskeyRequestOptionsAsync`/`PasskeyAssertionSignInAsync`. The IL scan bans the five base passkey members by name.*
- *`IdentityPasskeyOptions.UserVerificationRequirement = "required"`. Passkey sign-in uses `bypassTwoFactor: true`, because a user-verifying passkey is possession plus inherence and so counts as MFA. That is the value the framework's own `PasskeySignInAsync` passes (the 4.6 decompile, §15).*
- *The assertion is one-shot. Its unprotected state is consumed in the `consumed_passkey_challenges` ledger, by a set-based conditional insert, before the handler runs. The ledger is non-temporal runtime state, like `verification_cursors`.*
- *Attestation is not one-shot, and a replay is **refused, not re-saved** (this corrects T3-32's "re-saves"). The framework handler fails a credential that is already registered with `CredentialAlreadyRegistered`, which reaches the caller as `Validation`. An untampered ceremony first submitted by another account is still refused by the `UserEntity.Id` binding.*
- *The server enforces the cap of 100 passkeys (`Conflict`), and the client hides Add at the cap.*

### 5.6 Erasure front door

`DeletePersonalDataHandler`: when `HasPasswordAsync`, `CheckPasswordAsync` must pass (wrong → one static `InvalidCredentials` "Incorrect password."); `ErasureService.ShredWithRetriesAsync(user.Id, 3)`; on failure the outcome keeps `Fault` and `Indeterminate` distinct for telemetry with one user-facing wording ("Your account could not be fully deleted. The failure has been logged for an administrator to finish."); on success the deferred `SignOutAsync`, a `[LoggerMessage]` audit line, and `NextUrl = completion ?? "/"`. The password field is always on the wire; the client renders it only when `GetAccountStatus.HasPassword`.

### 5.7 Surviving HTTP surface

Already hidden: `MapAdditionalIdentityEndpoints` returns `accountGroup.ExcludeFromDescription()` for the whole `/Account` group, and Razor component endpoints never enter ApiExplorer. Trains only delete. After Train 3 the non-gRPC surface is:

- `POST /Account/PerformExternalLogin` — the challenge to the provider.
- `GET /Account/ExternalLogin` — the OAuth callback, a static-SSR page moved out of `Components/` to `ExternalLogin/ExternalLoginCallback.razor` (route unchanged, `[ExcludeFromInteractiveRouting]`), rewritten onto the `.Outcome` views so the IL-scan exemption can be deleted outright.
- `GET /Account/Manage/LinkExternalLogin?provider=` and `GET /Account/Manage/LinkExternalLoginCallback` — the link start becomes an antiforgery-free GET because WASM and MAUI cannot render the scaffold's antiforgery form post; OAuth `state` covers login CSRF.
- `/connect/token`, `/_auth/complete` — unchanged.

Deleted: both passkey endpoints and `POST /Account/Logout` (once `Yggdrasil/src/Hosting.Web.Components/Layout/NavMenu.razor` switches to Heimdall's `Logout.razor`, which already calls `IAuthenticationService.Logout`). `IdentityComponentsEndpointRouteBuilderExtensions` becomes `ExternalLoginEndpointRouteBuilderExtensions.MapNorseExternalLoginEndpoints()`.

*Amended 2026-10-28 (Train 3, T3-34, T3-29, T3-18):*
- *The surviving non-gRPC surface is the fail-closed `GET /Account/PerformExternalLogin`, the fail-closed `LinkExternalLogin` GET pair and the `/Account/ExternalLogin` callback page, plus the unchanged `/connect/token` and `/_auth/complete`. The POST start and `TemporaryFluentButtonFix` are gone.*
- *Both GET starts proceed only for `Sec-Fetch-Site: same-origin` or `none`. Anything else, an absent header included, challenges nothing, so a browser without Fetch Metadata cannot start external sign-in.*
- *`ReturnUrls.Resolve` refuses every external-login route as a landing. The routes come from one set of constants, and a fact enumerates every GET the mapping registers, so no same-origin hop can launder a cross-site start.*
- *Providers are configuration-driven OIDC schemes, one per `Identity:ExternalProviders:{Name}` section. The per-scheme `OnTokenValidated` reads the provider-verified email from the validated id token's `email`/`email_verified` into `AuthenticationProperties` (`Norse.Identity.VerifiedEmail`), never into a claim. This corrects the plan's Task 4.8 mapping of `email` to `ClaimTypes.Email`: ClaimActions run only over user-info, which is never fetched, and NORSE070 bans `JsonElement`. The same event binds the provider key from the token's single `sub` (`BindProviderKey`).*
- *External registration accepts only that verified email. Without one it renders a fixed refusal and creates nothing.*

### 5.8 Pages retired without a port

`InvalidUser` (only served the cookie-status redirect; components render `NotFound` / `Forbidden` in place), `RegisterConfirmation` (unreachable while `RequireConfirmedAccount` is off; returns with §11's verification follow-on), `ExternalLoginPicker` (nothing renders it), `ManageLayout` / `ManageNavMenu` (client concerns, rebuilt in Heimdall).

### 5.9 The last sign-in method

The scaffold hides the remove button when the account has no password and exactly one external login. Nothing enforces it: neither the framework's `RemoveLoginAsync` nor `NorseUserStore.RemoveLoginAsync` (a staged `RemoveLogin` intent) refuses, so a direct gRPC request, or a stale page after another removal, strips the account's final way in — and passkey login is outside this project. The invariant ("a user has a password or at least one external login") spans two tables and cannot be a `CHECK`; under house law §2.2 that means the application enforces it and a verification query fails loudly on violation.

- **Store.** The `RemoveLogin` intent flushes under a lock on the user's `user_credentials` row (`FOR UPDATE` on PostgreSQL, `UPDLOCK, HOLDLOCK` on SQL Server) taken before the count, inside the flush's single transaction, so competing removals from an account with two providers serialize and the second one sees one login left. The guard — `password_hash IS NOT NULL OR count(user_logins) > 1` — failing answers a new `NorseIdentityErrorCodes.LastSignInMethod`, mapped to `Conflict` by `IdentityOutcomeMap`. `ErasureService` is unaffected (it removes everything, key first).
- **Handler.** `RemoveExternalLoginHandler` carries no count of its own; the store's answer is the answer. `GetExternalLogins.CanRemove` stays as presentation.
- **Verification.** A `PostgresTestGroup` fact asserts no row in `users` has a null `password_hash` and zero `user_logins` after the concurrent-removal scenario; the same query is the follow-on candidate for the periodic verification job the house rule asks for, recorded in §11.

## 6. Client (Heimdall)

### 6.1 Shim + unit

Every page that reads a token from the URL is a five-line routable shim (`[SupplyParameterFromQuery]`, no logic) over a visual unit with plain `[Parameter]`s: `LoginWith2fa` → `TwoFactorForm`, `LoginWithRecoveryCode` → `RecoveryCodeForm`, `ResetPassword` → `ResetPasswordForm` (missing code → soft, history-replacing navigation to `Account/InvalidPasswordReset`), `ConfirmEmail` → `ConfirmEmailPanel`, `ConfirmEmailChange` → `ConfirmEmailChangePanel`, `RenamePasskey/{Id}` → `RenamePasskeyForm`. Bragi instantiates components inside BlazingStory's canvas, where the URL is the catalog's, and bUnit tests pass parameters; the unit is the subject, the shim is routing. Query values are copied into the request in `OnParametersSet`, not a field initializer, because they arrive after construction and can change on same-component navigation.

### 6.2 Loader + form

Pages that load state and then submit (profile, Email, ChangePassword, SetPassword, DeletePersonalData, EnableAuthenticator) are an `OutcomeComponentBase` page (`DispatchAsync` in `OnInitializedAsync`, `Problem` rendered in place, child chosen from the status) over an `OutcomeFormComponentBase` form. No Asgard base change; a merged base is a promotion question for a later N.

### 6.3 One continuation rule

| Operation class | Return | Continuation |
|---|---|---|
| Principal transition or security-stamp refresh (two-factor sign-ins, `ChangePassword`, `SetPassword`, `SetPhoneNumber`, `RemoveExternalLogin`, `ConfirmEmailChange`, `ResetAuthenticator`, `ForgetTwoFactorClient`, `DeletePersonalData`) | `NavigationResult` | `ISessionTransition.Begin(result)` |
| Ends on another page, no principal change (`ForgotPassword`, `ResetPassword`, `DisableTwoFactor`, `AddPasskey`, `RenamePasskey`) | `NavigationResult` / payload | soft `NavigationManager.NavigateTo` |
| Stays on the page | `BoolResponse` / payload | in-place status or rendered payload |

The post-reload message for the refresh family is `?status=<key>` on the server-authored `NextUrl` (`password-changed`, `password-set`, `phone-updated`, `email-changed`, `external-login-removed`, `authenticator-reset`); `Heimdall/src/AuthN.Components/Shared/ManageStatus.cs` maps key to text. The server remains the only author of URLs.

*Amended 2026-10-28 (Train 3, T3-10, T3-11, T3-12): the Train 3 continuations are:*
- *`ISessionTransition.Begin(result)`: `EnableAuthenticator`, `ResetAuthenticator`, `DisableTwoFactor`, `ForgetTwoFactorClient`, `PasskeySignIn`. `DisableTwoFactor` moves up from the second row, because every stamp-rotating Train 3 operation now refreshes the sign-in.*
- *Soft `NavigateTo(result.NextUrl)`: `AddPasskey`, `RenamePasskey`.*
- *In place: `GetAuthenticatorSetup`, `GenerateRecoveryCodes`, `RevealRecoveryCodes`, `GetPasskeys`, `GetPasskey`, `RemovePasskey`, `GetPasskeyRequestOptions`, `GetPasskeyCreationOptions`, `GetSignInMethods`.*
- *A full page load: each provider's `StartUrl` (an HTTP endpoint).*
- *New status keys: `authenticator-verified`, `two-factor-disabled`, `browser-forgotten`, `passkey-updated`, `external-login-failed` and `recovery-codes-failed`. `authenticator-reset` is now emitted.*

### 6.4 Placement and authorization

Forms and status views live in `AuthN.Components.FluentUI`; gate pages under `@layout GateLayout`; the Manage tree under `Manage/` with a folder `_Imports.razor` carrying `@layout ManageLayout` and `@attribute [Authorize(Policy = AuthNPolicies.Account)]` — Himinbjörg's own trick, so no page can forget either — locked by a reflection test that every `/Account/Manage/*` route carries the attribute. `AuthorizeRouteView` reads the page's attribute (layouts are not consulted); Yggdrasil's `Routes.razor` already routes `NotAuthorized` to `RedirectToLogin`. `ManageNavMenu` is static markup. The existing `PersonalData.razor` moves into `Manage/` so it inherits layout and policy (today it has neither). Scaffold routes are preserved. Lockout on the two-factor forms renders in-form as `Login.razor` does; `Pages/Lockout.razor` stays for the HTTP external-login path. Click-to-confirm (ruling 12) applies to `ConfirmEmailPanel`, `ConfirmEmailChangePanel`, `ResetAuthenticator`, `DisableTwoFactor`, `GenerateRecoveryCodes`.

### 6.5 The two-factor hand-off

`TwoFactorForm` receives `Challenge` from the shim, posts it with the code, and links to `Account/LoginWithRecoveryCode?challenge=…` as a sibling gate link (the way Login links "Forgot password?"); `RecoveryCodeForm` posts the same token. A missing or expired challenge renders "This sign-in attempt has expired" with a link to `Account/Login`. Success → `Begin(result)`. The URL is the only carrier that survives all three hosts: `Begin` is a forced load on Server (the scope dies) and a hard refresh on WASM, and MAUI's hop is a browser. Costs accepted: one forced load for a two-factor user with no principal change yet, and a token in browser history, mitigated by the five-minute TTL, the single purpose, and the second factor still being required. The shim replaces history state after reading the token.

### 6.6 Passkeys

`PasskeySubmit.razor.js` becomes `Heimdall/src/AuthN.Components.FluentUI/wwwroot/passkeys.js`, a plain ES module — `isSupported()`, `create(optionsJson)`, `request(optionsJson, mediation)` — with no `fetch`, no form, no antiforgery; the `AbortError` / `NotAllowedError` wording stays in JS. `Passkeys.razor` mirrors `PersonalData.razor`'s module import and dispose, calls JS only from the click (prerender has no JS), catches `JSException` narrowly into an in-place error, and gates "Add a passkey" on `isSupported()`. Flow: `GetPasskeyCreationOptions` → `create` → `AddPasskey` → soft navigation to rename. Identical C# on every host; the asset is an RCL static web asset.

### 6.7 Validators

One `{Record}Validator` per request in `AuthN.Components`, the email chain copied from `LoginRequestValidator`: Train 1 `ForgotPassword`, `ResendEmailConfirmation`, `ConfirmEmailChange`, `ResetPassword` (password min 8), `TwoFactorSignIn` (`Challenge` not empty; `Code` length 6–7 raw), `RecoveryCodeSignIn`, `ConfirmEmail`; Train 2 `SetPhoneNumber` (empty allowed, malformed rejected), `ChangeEmail`, `ChangePassword` (`NewPassword` not equal to `OldPassword`), `SetPassword`, `RemoveExternalLogin`, `DeletePersonalData` (max length only — requiredness depends on `HasPassword`, which only the server knows); Train 3 `EnableAuthenticator`, `AddPasskey`, `RenamePasskey` (name ≤ 200), `RemovePasskey`. Empty-record requests get none.

## 7. Stories (Bragi `src/DesignSystem.Stories`)

All fakes are `sealed`, non-public, scoped, and registered only in `ServiceCollectionExtensions.AddNorseStoryFakes()`:

- `AuthenticationScenario` grows `TwoFactorRequired` (Login → `NextUrl = /Account/LoginWith2fa?challenge=story`), `InvalidTwoFactorCode`, `InvalidRecoveryCode`, `InvalidResetToken`, `EmailConfirmationFailed`; `FakeAuthenticationService` gains the seven arms.
- `Account/AccountScenario` + `FakeAccountService`: `Success` (confirmed email, phone, password, 2FA on with authenticator, three codes, two passkeys, one external login and one available), `ExternalOnly` (no password; `CanRemove = false`), `TwoFactorDisabled`, `RecoveryCodesLow`, `NoPasskeys`, `IncorrectPassword`, `PasswordPolicy`, `InvalidVerificationCode`, `Fault`. Immutable fixtures; a fixed inline SVG stands in for the QR.
- `Identity/FakeIdentityService` + `IdentityScenario { Success, Fault }` — closes the PersonalData story deferral from the 08-08 spec §7.
- `Session/SessionScenario { SignedIn, Anonymous }` + `FakeAuthenticationStateProvider` (fixed `ClaimsPrincipal`: NameIdentifier GUID + role `user`; the anonymous-role principal otherwise), registered with `AddCascadingAuthenticationState().AddAuthorizationCore()`. Stories instantiate components directly, so `[Authorize]` is never evaluated in the canvas; the provider serves `AuthorizeView` and cascaded-state consumers and keeps the catalog truthful.
- `Shell/StoryShellLayout : IAppShellLayout` (`@Body` only) — `ManageLayout` nests the app shell and the stories host registers none.
- Every new validator registered so `FormValidator` resolves it.

`StoryDriver` needs no new mode: codes ride the `Email` slot (the `CountryLookup` precedent), password fields get `"aaaaaaaa"`, confirm pages use `ClickOnly`. Story pages land per train under `Authentication/` and `Account/`; `ScenarioCatalog.md`, `Bragi/CLAUDE.md` and `README.md` update per train. `DrivenStoryNavigationTests` gains per-surface facts: transitions record exactly one `RecordingSessionTransition` hop with empty history; soft hops record one non-forced navigation; failures record nothing. Everything is reachable through the stories host's MCP (`Yggdrasil/src/Hosting.Stories`, already Interactive Server with `MapBlazingStoryMcp`).

## 8. Hosting (Yggdrasil)

Discovery is automatic: `ContractDiscovery` keys on `[ServiceContract]` + `I*Service` + `Task<Outcome<T>>`, and server mapping, client proxies, validators, routable assemblies and `[NorsePolicy]` declarations come from the reference closure. Beyond package bumps: `Hosting.Web.Client/Program.cs` adds the generated `AddNorsePolicies()` (Train 1); Train 3 removes the `PasskeySubmit.razor.js` script from `Hosting.Web.Server/Components/App.razor`, switches `NavMenu.razor` to Heimdall's `Logout.razor`, renames the endpoint-mapping call, and updates `Yggdrasil/CLAUDE.md`'s identity page-tree row. Composition facts: the `IAccountService` proxy resolves on the client; `AuthNPolicies.Account` resolves from the client's `IAuthorizationPolicyProvider`; `/Account/Manage` routes from Heimdall's assembly; an anonymous-cookie gRPC call to `IAccountService` is forbidden and an identity-cookie call passes (`ChallengeAndForbidTests` siblings). **Ordering hazard:** Blazor throws on duplicate route templates across assemblies, so each train's Himinbjörg page deletions must be in the Himinbjörg release the Yggdrasil bump consumes.

## 9. Trains and ship gates

Each hop is the usual gate: PR merged, CI green, tagged, published. One fork per realm; trains are serial within a realm.

1. **Prerequisites + anonymous tier + plumbing.** Asgard (`NorseRoles.Anonymous`) → Midgard (alias; client-side policy emitter) → Heimdall (seven `IAuthenticationService` operations, records, validators; `AuthNPolicies.Account` declared; `IdentityPolicies.Self` hardened; `ForgotPassword`, `TwoFactorForm`, `RecoveryCodeForm`, `ResetPasswordForm`, `ConfirmEmailPanel`, `ConfirmEmailChangePanel`, `ResendEmailConfirmation` and their shims) → Himinbjörg (view methods, `NorseSignInManager` challenge and deferral overrides, `DeferredCompletion`, seven handlers, `LoginHandler` challenge URL, deletion of the seven scaffold pages plus `InvalidUser` and `RegisterConfirmation`) → Bragi → Yggdrasil (bumps, client `AddNorsePolicies()`).
2. **Account core.** Heimdall (`IAccountService` Train 2 operations, `ManageLayout` / `ManageNavMenu`, `Manage/_Imports`, profile, Email, ChangePassword, SetPassword, ExternalLogins, DeletePersonalData, `ManageStatus`, `PersonalData` move) → Himinbjörg (`Account/` handlers, `AccountService`, erasure front door, Manage page deletions, the `LinkExternalLogin` GET pair) → Bragi (`FakeAccountService`, `FakeIdentityService`, `FakeAuthenticationStateProvider`, `StoryShellLayout`, stories) → Yggdrasil.
3. **Two-factor + passkeys + retirement.** Heimdall (Train 3 operations, `passkeys.js`, TwoFactorAuthentication, EnableAuthenticator / `VerifyAuthenticatorForm`, ResetAuthenticator, Disable2fa, GenerateRecoveryCodes, Passkeys, RenamePasskey) → Himinbjörg (handlers, passkey pair, `ExternalLogin` callback relocated, `Components/` deleted with both passkey endpoints and `POST /Account/Logout`, IL-scan exemption removed, `ci.yml minimum_coverage` ratcheted, README / CLAUDE.md pair) → Bragi → Yggdrasil (`App.razor` script, `NavMenu` logout, endpoint rename, CLAUDE.md row). *(Amended 2026-10-28 (Train 3, T3-19): the **development** order is Asgard → Midgard → Heimdall → Himinbjörg → Bragi → Yggdrasil. The **release** order is Asgard, Midgard, then Heimdall → Bragi → Himinbjörg, then Yggdrasil. Login now injects `ISignInMethodsService`, so Bragi's floating `master` is red from Heimdall's publish until Bragi's Train 3 PR merges, an accepted window. Himinbjörg squashes `InitialCreate` once; wipe the local Postgres volumes after pulling it.)*

## 10. Testing

Tests first, every task (house rule). Shapes by layer:

- **Himinbjörg.** Handler facts with substituted managers and `FakePrincipal` (`MockUserManager` / `MockSignInManager`); `NorseSignInManagerTests` over the TestServer harness for everything on the sealed manager: a two-factor-required sign-in parks a token and writes no cookie on both started and unstarted responses, the token round-trips and expires, challenge sign-in success / failed / locked out, **a challenge issued before a password reset (or any stamp rotation) is refused on both the authenticator and the recovery-code path**, remember-client written directly versus stashed, forget stashed, `RefreshSignInAsync` reaches the stash, passkey state round-trips and a tampered state fails attestation. `AddPasskeyHandlerTests`: **a valid, untampered ceremony started for account A and submitted while authenticated as B is refused and persists nothing** — not only the tampered case. `DeferredCompletionTests` for null, single and chained URLs. `UserManagerOutcomeViewTests` one fact per new method (success, `Fault` by code, `Indeterminate`, `Erased`), plus **a commit-ambiguity fact driven through `ReplaceRecoveryCodesAsync` itself** (store answers the indeterminate code with no exception → the wire sees `Indeterminate`, never `Fault`); testing the mapper alone would miss the framework's discarded result. Anti-enumeration facts for `ForgotPassword`. Real-Postgres facts: `ConfirmEmailChange` writes one `users` version with email and username changed together; `DeletePersonalData` shreds through the real `ErasureService`; **a passwordless account with one provider cannot remove it, and two concurrent removals from a passwordless account with two providers leave exactly one** (§5.9). `OutcomeSurfaceEnforcementTests` loses its `ComponentsNamespace` branch in Train 3 and the relocated external-login page must pass the scan.
- **Heimdall.** bUnit (`BunitContext`, NSubstitute services, `AddFluentUIComponents()`, loose JS interop, validators registered so `SubmitAsync`'s gate is real). Forms follow `LoginTests`: empty submit never reaches the service; `Failed(ModelError)` lands in `ModelValidationSummary`; field-keyed failures land on the field; success asserts exactly one `Begin` with empty history or one soft navigation; the reject → edit → resubmit lock. Click pages follow `LogoutTests`: a bare render performs nothing. Shims assert the child received the query value and the missing-code redirect. `Passkeys` uses `JSInterop.SetupModule`. `PolicyDeclarationTests`: `AuthNPolicies.Account` rejects an anonymous-role principal. The reflection lock on `Manage/*` routes. Validator tests follow `LoginRequestValidatorTests`.
- **Bragi.** Fake parity facts; `DrivenStoryNavigationTests` per surface (§7).
- **Yggdrasil.** Composition facts (§8); an explicit Playwright phase in `WebServerBrowserRuntimeSmokeTests` for the Manage shell under WASM.
- **End to end through Bifröst.** `dotnet run --project src/Orchestration.AppHost`; every converted flow on the Server host and the WASM host, including a two-factor login with "remember this machine" landing on the right page after two chained completion hops, a password change that survives the forced reload with its status message, and passkey registration in a browser; every story in every scenario through the stories host MCP.

## 11. Known gaps carried forward

- **Email delivery is a no-op**, so `ForgotPassword` is inert (no account has `EmailConfirmed`) and confirmation links only exist in logs. `RequireConfirmedAccount`, `RegisterConfirmation`, and real delivery return together when the messaging realm owns email as an event.
- **`EnableAuthenticator` / `DisableTwoFactor` rotate the security stamp without a refresh**, as the scaffold does; revalidation signs the user out within thirty minutes. Codes-first display is incompatible with a forced reload. Parity chosen; a follow-on may deliver codes on the post-reload page.
- **`returnUrl` is still not honored after login** (seam spec §9); more visible with an authenticated tree.
- **The tracking-consent gate** on the two-factor status page is dropped (no `ITrackingConsentFeature` on the host).
- **Passkey login, MAUI passkeys, and `ValidateOrigin` / `ServerDomain`** for non-browser clients are follow-ons.
- **The authenticator issuer literal** (`Microsoft.AspNetCore.Identity.UI`) is what existing authenticator entries display; renaming it is a UX decision, not parity.
- **The periodic verification job** house law §2.2 asks for on the last-sign-in-method invariant (§5.9) does not exist yet; the query is written as a test fact here and promoted to a job when the platform has a worker to run it.

*Amended 2026-10-28 (Train 3):*
- *Closed: `returnUrl` is honored (T3-2). Every stamp-rotating Train 3 operation refreshes the sign-in, and new recovery codes survive the reload in a one-shot handoff (T3-12, T3-35). Passkey login shipped (T3-31 to T3-33). The authenticator issuer is required configuration, `Identity:Authenticator:Issuer` (T3-27). The verification job exists: "a worker to run it" becomes "the web host runs it" (T3-28).*
- *Out of scope: email delivery, `RequireConfirmedAccount` and `RegisterConfirmation` (the messaging realm); the tracking-consent gate (removing it is a no-op, since no realm calls `UseCookiePolicy`); MAUI and non-browser passkeys, `ValidateOrigin`/`ServerDomain` (sub-project C).*
- *Parked: throttling of ChangePassword, DeletePersonalData and ChangeEmail confirmations, until the platform rate-limits its whole API footprint (T3-1, Buvy).*
- *Moved: step-up re-authentication to Train 4 (T3-37); the local walk of external register, sign-in and the second factor after the provider to Train 5 (T3-36).*

## 12. Alternatives rejected

- **Keep the `TwoFactorUserId` cookie and extend the deferral** to stash it: two deferred hops per two-factor login, and the cookie never reaches a native client. Rejected for ruling 4.
- **Challenge token in scoped client state** instead of the URL: `Begin` is a forced load; the scope dies on Server and a WASM refresh loses it. Rejected for §6.5.
- **One account aggregate query** returning profile, passkeys and external logins at once: one fat record coupling every Manage page to every other's shape, and a native client always paying for lists it does not show. Rejected for ruling 9.
- **Per-flow contracts** (`IPasswordService`, `ITwoFactorService`, …): six proxies, six registrations, six policy declarations for twenty-odd operations. Rejected for ruling 3.
- **Delete external login outright**: robustness through subtraction was tempting, but the list/remove half is a real self-service surface and the redirects cost nothing to keep. Rejected for ruling 5.
- **Flip `RequireConfirmedAccount` now**: a behavior change inside a transport conversion with no delivery behind it. Rejected for ruling 7.
- **Components take the principal as a parameter** instead of reading `AuthenticationState`: every host threads the principal and the shape diverges from Login / Register. Rejected for ruling 8.

## 13. Issue #48 questions, answered

| Question | Answer |
|---|---|
| Batching | three trains by tier (§9) |
| Which flows stay HTTP | the external-login redirects only (§5.7) |
| Bragi mechanics | §7 |
| Ship-gate sequencing | §9; prerequisites front-loaded into Train 1 |
| `DeletePersonalData` as the erasure front door | §5.6 |
| Hiding HTTP-only endpoints from OpenAPI | already done by the group-level `ExcludeFromDescription()`; trains only delete |
| Coverage floor ratchet | Train 3, after `Components/` is gone |

## 14. Train 2 as built (2026-10-27)

Train 2 built the account core as §9 lists it. The end-to-end walk found the last four items below, and each was fixed in the same train.

- **`StoryDriver` gained one fixture, not a mode.** Pages that take a current and a new password get `NewPassword = "bbbbbbbb"` for the later input, so "must differ" never fires on a driven story. §7's "no new mode" holds.
- **The stories host's MCP is metadata-only.** `MapBlazingStoryMcp` lists stories and their parameters but has no render tool, so §10's "every story in every scenario through the stories host MCP" was met by rendering each story in a browser with Playwright.
- **Two click-only stories were dropped (Ruling 33).** "External Logins → Removed" and "Personal Data → Download" are click-only stories whose click changes nothing in the canvas, so the driver's click rejects and ends the circuit. Their states are held by bUnit facts instead, the same way as `Logout` and `ConfirmEmailChange`.
- **An anonymous principal's page navigation into the Manage tree is challenged to login (Ruling 44).**
  - **Found:** the walk first saw a bare 403 (provisional Ruling 37). `[Authorize(Policy = AuthNPolicies.Account)]` is endpoint metadata, so `UseAuthorization` forbids before `AuthorizeRouteView` can render `RedirectToLogin`, and Midgard's browser handler answered every forbid with an empty 403. In a browser that renders as the browser's own error page.
  - **Fix:** `NorseBrowserHandler.HandleForbiddenAsync` now answers an anonymous principal's page navigation with the identity cookie's login challenge, keeping the return URL. A page navigation is a GET or HEAD whose `Accept` names `text/html`, on the browser lane, not XHR, and not `[ApiController]`. Every other forbid, and any forbid of an identity principal, stays a bare 403. This amends `../../Platform/specs/2026-08-21-principal-at-the-door-design.md` §2.4.
  - **Pins:** Yggdrasil's `An_anonymous_browser_principal_is_redirected_from_the_manage_tree_to_login` pins the 302, with and without a prior cookie. §6.4's `RedirectToLogin` sentence describes the result, though the browser lane is what produces it.
- **ExternalLogins offered the platform's own schemes, and its links never left the router (Rulings 41 and 42).**
  - **The schemes:** Midgard registered its `Norse` and `Norse.Machine` policy schemes with their names as display names. `GetExternalAuthenticationSchemesAsync` keeps every scheme that has one, so the page listed both under "Add another service to log in". Both now carry a `null` display name, and a Yggdrasil composition fact pins that the composed host offers no external scheme when no provider is configured.
  - **The links:** the add-provider links were caught by interactive routing and rendered "Not Found". The `LinkExternalLogin` GET is a server endpoint, so the links now carry `ForceLoad="true"`.
- **A statically rendered form no longer reports success it did not earn (Ruling 46).**
  - **Found:** with the WASM runtime failing (below) and no Server circuit, `InteractiveAuto` left the Manage pages statically rendered. Their submits posted as plain forms.
  - **Root cause:** the form model is a component field, not `[SupplyParameterFromForm]`, so the post bound nothing. The handler ran on the loaded value, which saved nothing yet showed "Your profile has been updated.".
  - **Fix:** `OutcomeFormComponentBase` now refuses to dispatch unless `RendererInfo.IsInteractive`, rendering "This page hasn't finished loading — try again." instead. Every form binds its submit to `Disabled="!CanSubmit"`.
  - **Accepted cost:** the button is disabled for the boot window between prerender and interactivity, and stays disabled if interactivity never arrives.
- **Saving an unchanged phone is a no-op success (Ruling 47).** The stock `SetPhoneNumberAsync` unconfirms the phone and rotates the security stamp even when the number is unchanged. That writes a `users` version and signs every other session out. `SetPhoneNumberHandler` now compares the stored number to the write and answers success without calling the setter when they match.

**End to end through Bifröst** (`dotnet run --project src/Orchestration.AppHost`, Playwright, throwaway accounts). The Server host was driven with `.wasm` requests aborted, so `InteractiveAuto` resolved to a Server circuit. The walk ran twice, before and after the fixes above:

- **WASM host:** not exercisable on either walk. `dotnet.native.wasm` fails to compile on Chromium 156 (`… module uses a mix of legacy and new exception handling instructions`), which is the known .NET 11 RC3 environment limit.
- **Profile:** passed.
  - Setting `+15551234567` force-reloads to `?status=phone-updated` with "Your profile has been updated." and writes one `users` version. `5551234567` shows the format message and makes no call, and clearing the number saves `null`.
  - After Ruling 47, saving the unchanged number answers the same reload but leaves `user_credentials.security_stamp` and the version count unchanged, and a second signed-in browser context still opens `/Account/Manage`.
- **Static render (Ruling 46):** passed. With the circuit blocked, the profile, change-password and delete submits render disabled. A forced submit renders "This page hasn't finished loading — try again." and makes no call.
- **Email:** passed. "Send verification email" logs the link; click-to-confirm marks the address ✓. A change to a new address shows "Confirmation link to change email sent."; opening that link while signed in and confirming lands on `/Account/Manage/Email?status=email-changed`, and the new address logs in. On the first walk the logout before that login was taken from `/Account/Manage/Email` (the bare-403 case below) and repeated from `/`.
- **ChangePassword:** passed.
  - A wrong current password shows "Incorrect password." on the field. An equal new password shows the client message and makes no call. A weak password shows the server's three rules on the field.
  - A good password reloads to `?status=password-changed` with "Your password has been changed.". After a logout from `/`, the old password fails and the new one logs in.
- **SetPassword:** passed. On a password account it renders the change form, with no redirect.
- **ExternalLogins:** passed after Rulings 41 and 42. With no provider configured, the page reads "No external logins are linked and none are available.". The real-provider xsrf round trip and the "added" and "removed" messages cannot be exercised locally.
- **DeletePersonalData:** passed.
  - A wrong password shows "Incorrect password." in place and shreds nothing. The right password shreds, signs out through `/_auth/complete`, and lands on `/`.
  - The old login then fails, and `users` and `user_credentials` hold no row for the subject. The dev key store keeps a destroy receipt where the subject's key was, so the `users_history` versions left behind are ciphertext with no key.
- **Signed-out `/Account/Manage`:** passed after Ruling 44. Both the no-cookie and the anonymous-cookie visitor get a 302 to `/Account/Login?ReturnUrl=%2FAccount%2FManage`; the same request with `Accept: application/json` keeps the bare 403.
- **Logout from a Manage page:** passed after Ruling 44. `POST /Account/Logout` redirects back to the page it was posted from, which now challenges to `/Account/Login?ReturnUrl=…` instead of rendering the browser's error page. Who owns the logout endpoint is Train 3's question (§9).

## 15. Train 3 as built (2026-10-28)

Train 3 built two-factor management, passkey management, passkey sign-in and the scaffold retirement as §9 item 3 lists it, as amended (A1–A10, marked inline above). It ran 31 tasks across Asgard, Midgard, Heimdall, Himinbjörg, Bragi and Yggdrasil. The plan is `../plans/2026-10-27-identity-account-lifecycle-train-3.md`. Every task ruling this section cites (T3-n) is recorded in the plan or inline here. Bifröst's AppHost is unchanged, and no third-party container was added (T3-29).

**What was built:**
- **Upstream.** Asgard: `OutcomeComponentBase.CanAct => RendererInfo.IsInteractive && !IsDispatching`, superseding Ruling 49 (T3-4). Midgard: an identity principal's page navigation that fails a policy is forbidden through the identity cookie, which redirects to `/Account/AccessDenied`; every other forbid of an identity principal stays a bare 403 (T3-3). This amends `../../Platform/specs/2026-08-21-principal-at-the-door-design.md` again.
- **Heimdall.** The three contracts (seven, six and three operations), sixteen records and seven validators. Nine pages: the two-factor and passkey Manage pages and `/Account/LoginWithPasskey`. `RecoveryCodesHandoff`, the `SignInMethodLinks` loader, `NotAuthorizedRedirect` and `passkeys.js`. Every click trigger binds `Disabled="!CanAct"`, locked by `ClickStaticRenderLockTests`.
- **Himinbjörg.**
  - The services and handlers (37 handlers in all, up from 21), built on `AccountCaller` and `ManageUrls`. `ReturnUrls.Resolve` decides every landing.
  - Stateless passkey ceremonies with the consumed-challenge ledger. The recovery-codes handoff. The QR code rendered server-side by QRCoder as an SVG data source, never `MarkupString` (T3-14).
  - Configuration-driven OIDC providers behind the fail-closed GET starts. The external-login callback, rebuilt on the `.Outcome` views at `ExternalLogin/ExternalLoginCallback.razor`.
  - The stranded-account verification service.
  - `Components/` is deleted (fifteen files and three helpers). The IL-scan exemption went with it: the eleven raw calls it excused were all in `Components/`.
- **Bragi.** The click driver settles without a mutation, and `StoryDriver` turns a `JSException` into `failed` with the reason logged and rendered. Ruling 33's two stories are back. Three fakes and nine stories, with locks that render the compiled `.stories.razor`.
- **Yggdrasil.** Composes `AddNorseExternalProviders` and `MapNorseExternalLoginEndpoints`. Removes `IdentityRedirectManager` and the `PasskeySubmit.razor.js` script. Points the nav's logout at Heimdall's `Logout.razor`. Supplies `Identity:Authenticator:Issuer = "Norse"` in Development. Adds lane facts over the composed wire (6.1): every fact rides a gRPC channel over HTTP/2, and the final wave runs the two-factor lifecycle, the passkey management lane and the anonymous sign-in methods over gRPC-Web too, through Midgard's `GrpcWebCallInvoker` on a plain `HttpClient`, as `Hosting.Web.Client/Program.cs` composes it. That proves the WASM client's wire leg against the real host, not the WASM render. Gates the nav's Manage and Logout block on `AuthNPolicies.Account` (final wave), labeled by the email claim, then the name, then "Account".

**As-built rulings that changed or decided spec-level behavior:**
- **Passkey sign-in uses `bypassTwoFactor: true`.** A user-verifying passkey counts as MFA (T3-31 with T3-33). `isPersistent` follows the wire's `RememberMe`, as Login does, where the base `PasskeySignInAsync` is never persistent. `PasskeySignInHandler` keeps its two-factor branch, now unreachable, with a comment naming this ruling. Flipping the ruling is one argument and one test name (`A_two_factor_account_signs_in_without_the_challenge`).
- **External login stays at `bypassTwoFactor: false` (T3-18).** `NorseSignInManager.ExternalLoginSignInAsync` throws if it is ever passed `true`. The RC3 three-argument overload forwards `false`, which a guard fact pins.
- **External registration accepts only a provider-verified email.**
  - The email is read from the validated id token's `email`/`email_verified` into `AuthenticationProperties` (`Norse.Identity.VerifiedEmail`), never from claims. The callback page reads only that item, so no token claim in any casing can supply or vouch for the address.
  - The provider key is bound from the token's single `sub` (`BindProviderKey`). It removes every `NameIdentifier` claim case-insensitively, then adds exactly one from `sub`. A missing or non-single `sub` fails the sign-in, which shows the fixed `RemoteError` text. A numeric `sub` is read as its string; a date-shaped one is refused, which fails closed.
  - Duplicate `email` keys in one signed token resolve last-wins, deterministically. Only the signing provider could emit one.
  - Registration creates a new unconfirmed user, sends the confirmation link, and signs in, because the provider vouched for the address. This differs from password Register, which lands on Login. A provider email never links an existing account: a duplicate email fails `CreateAsync`, so there is no takeover path. An unverified email renders the refusal in place, on GET and POST, and creates nothing.
- **`ReturnUrls` refuses external-login routes as landings.** Validation is path-only (T3-2, Buvy r6). PathBase containment is case-sensitive: a miss lands on the app root, the safe direction.
- **Concurrent first authenticator setups** answer the same stored key, through a conditional write pinned on both providers.
- **The ledger runs under the context's execution strategy**, not zero-retry. A retried insert that had landed reads as a replay, so the one-shot fails closed: a legitimate sign-in is asked to retry, and nothing is ever let through twice.
- **Visibility.** `ExternalProviders.VerifiedEmailItem`, `RedirectRemoteFailure` and `ConfigureRemoteFailure`, and `NorseSignInManager`'s item name, ceremony purposes and attestation message, are internal (§2.3; the plan is amended as built). `IPasskeyChallengeLedger` is public, because the public `NorseSignInManager` constructor takes it; its implementation stays internal. `IStrandedAccountsQuery` stays internal, with a hand-written test double, because public-for-testability is never justified.
- **The invariant verification job runs in the web host (T3-28).** `Interval` is validated on start: above zero and at most one day. Yggdrasil proves the composition through the public `norse.identity.stranded_accounts` gauge, with the real service enabled on the Postgres lane host. It uses no reflection into Himinbjörg internals.
- **Himinbjörg's coverage floor is 85 (T3-22).** CI instruments only the six `Norse.Identity.*` assemblies. Measured that way, branch coverage is 87.2%, rounded down to 5.
- **Passkeys are never an account's sole credential.** They do not count in §5.9's guard (T3-30), and `RemovePasskey` carries no last-credential guard. A later passkey-only account path must add one.
- **Attestation store faults surface as `Validation`,** because the framework handler folds every unexpected exception into a failed result. The user-facing text is accepted. The failure's exception is logged at Information on both ceremonies, so a store fault is diagnosable.
- **Hardening landed in the final wave.** User verification is pinned through `PostConfigure`, so no host `Configure` can relax it. The ledger prune keeps rows five minutes past expiry (`SkewMargin`), so a state still valid under clock skew cannot replay. No OIDC scheme reads the user-info endpoint, and every claim action onto `email` or the name identifier is removed (the stock `sub` action stays, mapping only `sub`, and claim actions run only over user-info anyway). A provider name outside `[A-Za-z0-9_-]+`, or one whose derived paths collide with another provider's or a reserved platform path, fails at registration. `ExternalLoginRoutes.All` is immutable. A key provisioning whose write leaves no key stored fails closed (`AuthenticatorKeyNotStored`, a `Fault`). Enable's code step and Reset's key step log a caught `DbException` at Warning, with the exception, before landing as they did. Midgard's `/_auth/complete` lands only on a single-slash-rooted local path, decoded or not, and on the app root otherwise. Heimdall's `NotAuthorizedRedirect` carries the local return URL to AccessDenied, and `PersonalData`'s download moved onto `OutcomeComponentBase`.
- **Forget this browser renders whenever the machine is remembered,** not only under two-factor-enabled. The cookie exists either way, and clearing it is harmless.

**Dockets 1–9 (Train 2):**

| Docket | Ruling | Disposition |
|---|---|---|
| 1 Throttling | T3-1 | parked (Buvy) until the platform rate-limits its whole API footprint |
| 2 `returnUrl` | T3-2 | closed: honored after password, two-factor, recovery-code, passkey and external sign-in (3.3, 3.10, 4.3, 4.10, 4.11) |
| 3 AccessDenied | T3-3 | closed on both lanes (2.1, 3.3), carrying the local return URL (final wave); the terminus is accepted until the refactor load drops |
| 4 Click static render | T3-4 | closed: `CanAct` (1.1), adopted by every click trigger (3.4) |
| 5 `ManageNavMenu` | T3-5 | stays static; a fact asserts every `Href` routes to a Heimdall page (3.9) |
| 6 Link failure keys | T3-6 | closed: `external-login-not-added` only for `LoginAlreadyAssociated`, `external-login-failed` for every other definite failure, no key when indeterminate (4.8) |
| 7 Click driver | T3-7 | closed: settles without a mutation; Ruling 33's two stories return (5.1) |
| 8 `InteractiveBunitContext` | T3-8 | parked (Buvy) until Asgard has shared test behaviors |
| 9 Bifröst pair | T3-9 | closed: Trains 1, 2 and 3 entered in Bifröst's README.md and CLAUDE.md in one change (6.3) |

**The 4.6 decompile record.** These findings come from ilspycmd over the installed `Microsoft.AspNetCore.App 11.0.0-rc.3.26522.148`. None of them changed the design.
- **T3-31's value.** The base `PasskeySignInAsync` runs `RetrievePasskeyAuthenticationInfoAsync`, `PerformPasskeyAssertionAsync`, `PreSignInCheck`, then `AddOrUpdatePasskeyAsync` (the sign-count update). It ends with `SignInOrTwoFactorAsync(user, isPersistent: false, loginProvider: null, bypassTwoFactor: true)`. So the recorded value is **`true`**. The sign-in is never persistent, there is no lockout reset, and a failure does not count toward lockout.
- **The UV enforcement point.** `VerifyAuthenticatorData` runs on both attestation and assertion. When `UserVerificationRequirement` is `"required"`, a missing UV flag throws `UserNotVerified`. The same check rejects user-not-present, an RP id hash mismatch, and backed-up-but-not-eligible. The option also flows into the browser JSON. RC3 already defaults it to `"required"`, and T3-33 pins it explicitly.
- **RP id and origin.** The RP id is `ServerDomain ?? Request.Host.Host`. With no `ValidateOrigin` delegate, the client data's `origin` must equal the request's single `Origin` header. The 6.2 walk proved that a Blazor Server circuit carries it, so no `ServerDomain` configuration was needed.
- **Attestation replay.** `PerformAttestationCoreAsync` refuses an existing credential with `CredentialAlreadyRegistered` (A4's correction of T3-32).
- **Why the base members are banned.** The base `Make…OptionsAsync` and `Perform…Async` keep ceremony state in a `TwoFactorUserIdScheme` sign-in, a cookie write a circuit cannot make. The assertion state is JSON `{challenge, userId?}`, so the ledger hashes the UTF-8 bytes of the unprotected state, which are identical on every replay.

**End to end through Bifröst (Task 6.2).**
- **Stand-up.** The walk ran against a disposable Postgres (`postgres:19rc1`, the image `AppHost.cs` uses, no named volume), with Yggdrasil's migrations service and then its web server run directly. The human owns the wipe of the `norse-pg-primary`/`norse-pg-replica` volumes owed after the 4.9 `InitialCreate` squash. The full `dotnet run --project src/Orchestration.AppHost` stand-up is owed after that wipe.
- **T3-23 re-check.** `DotNetVersion` is still `11.0.0-rc.3.26522.148`, and Playwright's Chromium major is still 156. The environment limit stands. The Server column was walked with `.wasm` requests aborted. The WASM column's wire leg is covered by the gRPC-Web lane facts (final wave); its render is not, until the environment limit lifts.
- **Result.** 27 of the 28 walkable rows passed. W24 passed except one leg (below). No product fix was needed.
- **Train 5 rows.** W10 (external register) and W12 (the second factor after the provider) are Train 5 (T3-36).
- **Halves.** W11 was walked as its provider-free half: Login shows no provider section, the passkey link's `href` is the server's with the return URL carried, and `GET /Account/PerformExternalLogin?provider=nope` returns to Login. W17 was walked as its list, remove and last-method half: a SQL-seeded login is listed and refused as the last method, then removed once a password exists. In both rows the provider leg and linking are Train 5.
- **W24.** Disable rotates the stored stamp and keeps this session. The other-session sign-out waits on the default 30-minute `SecurityStampValidator` interval, so a real-host walk cannot observe it. 6.1's lane facts pin the stale cookie's refusal at `ValidationInterval = TimeSpan.Zero`. Shortening the interval is a cost and security tradeoff, handed to Train 4 with OpenIddict session revocation.
- **Passkeys.** Both ceremonies passed the origin check under Blazor Server. W21's recovery codes render after the URL strip (router instance reuse). W29 logged Critical after exactly two cursor advances, which is the ceil(N / SampleSize) + 1 bound. W30 rendered all 114 stories, and every one of the 60 driven stories reached `complete`.
- **Step 3b.** All 61 `ReturnUrlsTests` rows held under WHATWG URL resolution in the browser. Only the 9 accepted rows corroborate anything; the other 52 expect a constant root.

**Remaining deferrals:**

| Item | Ruled by | Lands with / re-check |
|---|---|---|
| T3-1 throttling | Buvy | the platform-wide rate limiter |
| T3-3 AccessDenied terminus | Buvy | when the refactor load drops |
| T3-8 shared test framework | Buvy | Asgard's shared test behaviors |
| T3-23 WASM browser walk | environment limit | `DotNetVersion` ≠ `11.0.0-rc.3.26522.148`, or Chromium major ≠ 156 |
| T3-37 step-up | Buvy | Train 4 (OpenIddict, spec sub-project B) |
| T3-36 rows 10–12 local walk | Buvy | Train 5 (full OpenIddict / ASP.NET Identity integration) |

**Carried to the final wave / hand-off** (items the table does not cover):
- The AppHost stand-up after the human's volume wipe (above).
- W24: the other-session sign-out waits on the default 30-minute stamp interval. This is a hand-off note pointing at Train 4.
- Midgard's `/_auth/complete` meta-refreshes to a `returnUrl` that the single-use key does not bind. It is pre-existing and gated by a server-minted key, and the final wave's local-path guard confines it to this app. Binding the landing into the deferred action, an Asgard contract change, is a named Train 4 hand-off item.
- A failed key write during Reset leaves two-factor off with the old key stored: the stock order disables two-factor before it replaces the key. The handler refreshes the sign-in from a re-read and lands on the two-factor page, which shows two-factor off; re-enabling from setup recovers it. Nothing repairs it on its own.
- The 4.4 deferral is open: in `EnableAuthenticatorHandler`, a `DbException` from `CountRecoveryCodesAsync` after a committed enable is not caught, so it escapes T3-35's handling: no handoff, no codes issued, and no refreshed sign-in on that answer. The user regenerates codes from the Manage page.
- Heimdall-local: loaders and click pages render `ProblemMessages.Of(problem, fallback)`, so a page-level `Fault` drops `Problem.CorrelationId`, which forms show through `CategoryDisplay`. `ProblemMessages.Of` should append the correlation-id line for `Fault`. It is not a ship blocker.
- The provider-name collision check covers other providers and reserved platform paths. A host's own routed endpoint is not yet known at registration, so a provider path colliding with one is not refused.
- Anonymous passkey options-then-sign-in loops write ledger rows that are bounded only by pruning at expiry. They fall under T3-1.
- Bragi's `FakeSignInMethodsService` ports `ReturnUrls.Resolve`. It is a known duplication, and no drift guard can reach a sealed server assembly by reference.
- Vafthrudnir's collections are refreshed against the Train 3 surface after Train 3 lands (Buvy).

**Dependencies** *(A10, Amended 2026-10-28 (Train 3, T3-36, T3-37))*:
- **Train 4 (OpenIddict, spec sub-project B):** step-up re-authentication through `max_age` / `prompt=login` / `auth_time` (T3-37, Buvy). It must cover, by name: **passkey add** (a stolen session can add a credential that signs in without the second factor), two-factor disable, external-login link, and the authenticator key display (setup and reset).
- **Train 5 (full OpenIddict / ASP.NET Identity integration):** the local end-to-end walk of external register, sign-in and second factor after the provider, federating against OpenIddict's own authorization code flow (T3-36, Buvy).

**Template findings for Microsoft (not filed).** These are recorded here, not filed. They go to dotnet/aspnetcore only after Norse ships end to end.
- The Identity scaffold's `ExternalLogin.razor` signs in with `bypassTwoFactor: true`, so an external sign-in skips the app's own second factor.
- The scaffold's external registration takes a typed, editable email. An attacker can register a victim's address through the attacker's own provider account, and that login survives the victim's later confirmation or password reset (pre-account hijacking).
- RC3's `GetExternalLoginInfoAsync` reads the provider key from the first `ClaimTypes.NameIdentifier`, matched case-insensitively, before `sub`. Under `MapInboundClaims = false`, a token claim of that name in any casing can override `sub`.
- The scaffold's EnableAuthenticator page provisions a missing key with `ResetAuthenticatorKeyAsync` on render. That rotates the security stamp on a page load, and two tabs race, with the last writer winning.
- The scaffold's enable and disable rotate the stamp without refreshing the sign-in, so revalidation signs the user out within thirty minutes (§11, closed here by T3-12).
- The authenticator issuer defaults to `Microsoft.AspNetCore.Identity.UI`.
- The passkey handler folds every unexpected exception, store faults included, into a failed attestation whose message carries `ex.Message`. A database fault therefore reads as a validation failure.
- The base passkey `Make…OptionsAsync`/`Perform…Async` keep ceremony state in a cookie, which an interactive Blazor circuit cannot write.
