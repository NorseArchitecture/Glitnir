# Himinbjörg: Identity Account Lifecycle over gRPC — Design

**Realms touched:** Himinbjörg (handlers, `.Outcome` views, `NorseSignInManager`, scaffold retirement), Heimdall (contracts, validators, component library), Bragi (fakes and stories), Asgard (`NorseRoles.Anonymous`), Midgard (anonymous-role alias, client-side policy emitter), Yggdrasil (composition bumps, logout and passkey script cleanup), Glitnir (this verdict).
**Closes:** [Himinbjorg#48](https://github.com/NorseArchitecture/Himinbjorg/issues/48).
**Depends on nothing open.** **Depended on by:** the OpenIddict authorization-code + PKCE spec ([Himinbjorg#50](https://github.com/NorseArchitecture/Himinbjorg/issues/50), [#52](https://github.com/NorseArchitecture/Himinbjorg/issues/52)) and the MAUI head spec, both unwritten; see §3.
**Amends:** `../../Heimdall/specs/2026-07-13-authn-identity-split-design.md` §3.2 (the `IAccountService` shape proposed there is superseded by §4); `../../Platform/specs/2026-06-07-auth-design.md` §6 (`IAccountApi` is never built; this is its realization); `2026-10-24-identity-explicit-writes-temporal-split-design.md` §4.12 (the `Components/` IL-scan exemption ends with Train 3); `../../Asgard/specs/2026-08-11-session-transition-seam-design.md` §9 (registration-forces-verification stays parked, §11).
**Revision:** r3, 2026-10-26 — r1 was the brainstorm verdict; rulings 1–12 were each approved in-session before it was written. r2 answers the first review pass (four findings, verified against the installed `Microsoft.AspNetCore.App 11.0.0-rc.3.26522.148` and `NorseUserStore`): the two-factor challenge carries and validates the security stamp (§5.3), passkey attestation is bound to the principal completing it (§5.5), recovery-code generation preserves the store's commit outcome instead of collapsing it to `Fault` (§5.2), and removing the last usable sign-in method is refused under a row lock (§5.9). r3 answers the second pass: the lossless recovery-code path reaches `NorseUserManager` through an `IRecoveryCodeReplacement` interface declared in `Identity.Outcomes`, because `Identity.Web.Server` already references `Identity.Outcomes` and the view's receiver is `UserManager<NorseUser>` (§5.2).

## 1. Context

Login and Register run as gRPC operations behind Heimdall components today, on Blazor Server and on WASM. Every other Identity flow — forgot and reset password, email confirmation, two-factor sign-in, the whole `/Account/Manage` tree, passkeys, erasure — still lives in `Himinbjorg/src/Identity.Web.Server/Components` as the static-SSR scaffold ASP.NET Core Identity ships. That folder is why Heimdall is not yet a publishable component library: the pages inject `UserManager` and `SignInManager` directly, use antiforgery form posts, store the two-factor hop in a cookie that only a browser round-trip can set, and carry the one exemption left in `OutcomeSurfaceEnforcementTests` ("until #48").

The 2026-07-13 split spec sketched an `IAccountService` for all of this but never built it; the 2026-08-06 validation-composition spec built the mechanism every converted form now lands on (`OutcomeFormComponentBase`, `ApplyServerErrors`, `Result`-hydrating setters, generated discovery); the 2026-08-11 seam spec gave every sign-in transition one door (`ISessionTransition`); the 2026-08-21 principal-at-the-door spec put every gRPC endpoint on the identity-cookie lane; the 2026-10-24 store spec made every Identity result reach Norse code as an `Outcome<T>`. The remaining work is composition, not invention.

**Outcome sought:** every server-side Identity interaction is a gRPC operation on a Heimdall contract, every page is a Heimdall component that renders identically on Blazor Server and WASM (and on MAUI once a head exists), Himinbjörg's `Components/` folder is gone, and the IL-scan exemption with it.

## 2. Rulings (brainstorm 2026-10-26)

1. **Decomposition.** The original ask — all Identity over gRPC *and* a component library for WASM, WebSocket, and MAUI — is three sub-projects: **(A)** the account lifecycle over gRPC (this spec), **(B)** OpenIddict authorization code + PKCE with bearer validation on the gRPC lane, **(C)** the MAUI head. A ships first; B and C get their own specs.
2. **MAUI sign-in stays the 06-07 §4.2 ruling**: system-browser Authorization Code + PKCE, Login / Register / two-factor web-rendered during the hop, every account-management component native. "Stay signed in" persists the refresh token in `SecureStorage` (Keychain, Android Keystore, DPAPI); the access token lives in memory. Belongs to B/C; recorded here so A's component shapes do not fight it.
3. **Two contract tiers.** Anonymous-tier operations join `IAuthenticationService` under `AuthNPolicies.Public`. Self-service operations go on a new `IAccountService` under `AuthNPolicies.Account`, which requires a non-anonymous principal (§4.4).
4. **The two-factor challenge crosses the wire as a data-protected token**, not a cookie. Login's `RequiresTwoFactor` outcome carries it; the two-factor and recovery-code sign-ins present it with the code. The `TwoFactorUserId` cookie is never written again. Only the remember-this-machine cookie survives, and it rides the deferral (§5.3–§5.4).
5. **External login**: the OAuth redirect endpoints stay HTTP in Himinbjörg (already hidden from OpenAPI); the `Manage/ExternalLogins` list and remove convert to gRPC. Nothing is deleted that a provider could later need; nothing is ported that nothing renders.
6. **Three trains by tier** (§9), one fork per realm, serial within a realm. Asgard and Midgard prerequisites front-load into Train 1 so Trains 2 and 3 are pure Heimdall → Himinbjörg → Bragi → Yggdrasil.
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
| `ResetPassword` | `{ Email, Code, Password }` | `NavigationResult` | unknown user → the same success as a real reset (`/Account/ResetPasswordConfirmation`); invalid token → `InvalidCredentials`; password rules → `Validation` regrouped onto `Password` |
| `ConfirmEmail` | `{ UserId, Code }` | `BoolResponse` | `NotFound` / `InvalidCredentials` pass through |
| `ConfirmEmailChange` | `{ UserId, Email, Code }` | `NavigationResult` | `NorseUserManager.ChangeEmailAsync` sets email and username together (law); refreshes the sign-in when the caller *is* that user, otherwise `NextUrl = /Account/Login` |
| `ResendEmailConfirmation` | `{ Email }` | `BoolResponse` | always true; also serves the signed-in "send verification email" button |

**`Login` changes once.** On `RequiresTwoFactor` the handler returns `NextUrl = /Account/LoginWith2fa?challenge=<token>` (PathBase-qualified). Remember-me lives inside the token; the `RememberMe` query parameter and the deferred-completion detour on that branch disappear. `Login.razor` is unchanged: it still hands every result to `ISessionTransition.Begin` and cannot tell a two-factor hop from a completed sign-in. That is the point — no flag to branch on.

### 4.3 `IAccountService` (new; `[ServiceContract(Name = "grpc.account.v1.AccountService")]`; policy `AuthNPolicies.Account`)

**Train 2**

| Operation | Request | Returns |
|---|---|---|
| `GetAccountStatus` | — | `AccountStatusResponse { HasPassword, EmailConfirmed, TwoFactorEnabled, HasAuthenticator, RecoveryCodesLeft, IsMachineRemembered, PasskeyCount, ExternalLoginCount }` |
| `SetPhoneNumber` | `{ Phone? }` (null clears) | `NavigationResult` (`SetPhoneNumberAsync` rotates the security stamp) |
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

Read-only manager calls (`HasPasswordAsync`, `CheckPasswordAsync`, `IsEmailConfirmedAsync`, `GetTwoFactorEnabledAsync`, `GetAuthenticatorKeyAsync`, `CountRecoveryCodesAsync`, `GetLoginsAsync`, `GetPasskeysAsync`, `VerifyTwoFactorTokenAsync`, `Generate*TokenAsync`, …) stay raw: they operate on an already-materialized instance and need no `KeyDestroyedException` fold.

### 5.3 `NorseSignInManager`: the two-factor challenge

`NorseSignInManager` takes `IDataProtectionProvider` and creates two time-limited protectors: `Norse.Identity.TwoFactorChallenge.v1` (five minutes) and `Norse.Identity.PasskeyAttestation.v1` (`IdentityPasskeyOptions.AuthenticatorTimeout`).

- **`SignInOrTwoFactorAsync` override.** When two-factor is enabled and the machine is not remembered: mint `v1|userId|securityStamp|isPersistent|loginProvider` (a delimited string — NORSE070 bans System.Text.Json here), park it on `HttpContext.Items`, return `TwoFactorRequired`. The base is never called on this branch; the `TwoFactorUserId` cookie is never written or stashed; `StoreTwoFactorInfo` is deleted. Every other branch delegates to the base, which reaches the existing deferral through the virtual `SignInWithClaimsAsync`.
- **The stamp is revocation.** The framework's `StoreTwoFactorInfo` puts the security stamp into the two-factor cookie and `RetrieveTwoFactorInfoAsync` validates it, so a password reset or any other stamp rotation kills an outstanding challenge. The five-minute expiry does not preserve that; the token therefore carries the stamp, and both redemption paths compare it against `GetSecurityStampAsync` after `FindByIdAsync` and answer `Failed` on a mismatch — before the second factor is even looked at.
- **`TwoFactorChallengeSignInAsync(token, code, rememberClient)`** reproduces the base `TwoFactorAuthenticatorSignInAsync` with the token as the information source: unprotect (`CryptographicException` → `Failed`) → `FindByIdAsync` → stamp check → `PreSignInCheck` → `VerifyTwoFactorTokenAsync` → `ResetLockout` → optional `RememberTwoFactorClientAsync` → `SignInWithClaimsAsync` with `amr=mfa` (and the provider claim when present). Failure increments `AccessFailedAsync` when lockout is supported and reports `LockedOut` when it tripped.
- **`RecoveryCodeChallengeSignInAsync(token, code)`**: same prelude including the stamp check; `RedeemTwoFactorRecoveryCodeAsync`; completion with `isPersistent: false`, no remember; failure does not count toward lockout (parity).
- `LoginHandler` reads the parked token; a `RequiresTwoFactor` result without one is an `InvalidOperationException` (programming error, fail loud).

The `SignInManager` internals this reproduces (`DoTwoFactorSignInAsync`, `StoreRememberClient`) must be re-verified by decompile against the installed `Microsoft.AspNetCore.App 11.0.0-rc.3` before coding — the ritual `NorseSignInManager.cs` already records.

### 5.4 Remember-this-machine and deferred completion

`RememberTwoFactorClientAsync` and `ForgetTwoFactorClientAsync` are the two raw cookie writes left after the `TwoFactorUserId` cookie dies. Both are overridden: when the response has not started they write directly; otherwise they stash under `IdentityConstants.TwoFactorRememberMeScheme` through `IDeferredSignIn`. `RefreshSignInAsync` needs no override — the base authenticates the identity cookie (present on both lanes) and calls the virtual `SignInWithClaimsAsync`; a test pins that it reaches the stash. Operations that rotate the security stamp (`SetPhoneNumber`, `ChangePassword`, `SetPassword`, `RemoveExternalLogin`, `ConfirmEmailChange`, `ResetAuthenticator`) refresh the sign-in and return `NavigationResult`; without the refresh, revalidation signs the user out within thirty minutes.

A two-factor login with "remember this machine" on a circuit stashes two sign-ins. A new `DeferredCompletion.TryBuildUrl(returnUrl)` (Himinbjörg, replacing the per-handler `TryGetDeferredCompletionUrl` copies now that there are a dozen call sites) composes chained `/_auth/complete` hops: the inner completes the remember cookie and returns to `returnUrl`, the outer completes the identity cookie and returns to the inner. No Midgard change; the keys live sixty seconds and two hops fit comfortably. If the chain proves brittle, the fallback is `DeferredSignInAction` carrying multiple actions (an Asgard + Midgard change).

### 5.5 Passkeys

`IPasskeyHandler<NorseUser>` is public in the installed Identity: `MakeCreationOptionsAsync(PasskeyUserEntity, HttpContext)` → `{ CreationOptionsJson, AttestationState }` and `PerformAttestationAsync(PasskeyAttestationContext)`. `NorseSignInManager.MakePasskeyCreationOptionsAsync(user)` calls the handler and protects `AttestationState` with the second protector; `PerformPasskeyAttestationAsync(credentialJson, protectedState)` unprotects and completes. **The ceremony is bound to the account completing it.** The framework's attestation state carries the `UserEntity` the options were made for and `PerformAttestationAsync` hands it back beside the credential, but nothing in the framework compares it with the caller; the scaffold's `AddOrUpdatePasskeyAsync(currentUser, result.Passkey)` would attach a credential minted under A's user handle to B whenever B submits A's untampered ceremony. `AddPasskeyHandler` therefore requires `result.UserEntity.Id` to equal the principal-derived user id before `AddOrUpdatePasskeyAsync`, answering `Forbidden` otherwise. This bypasses the base `MakePasskeyCreationOptionsAsync` / `PerformPasskeyAttestationAsync`, which persist the state in a private data-protected cookie between the two requests — stateless on the server, consistent with ruling 4. The `/Account/PasskeyCreationOptions` and `/Account/PasskeyRequestOptions` endpoints are deleted (the second is used by nothing). Passkey *login* (`PasskeySignInAsync`) is not in scope; `passkeys.js` keeps a `request` export for that follow-on.

### 5.6 Erasure front door

`DeletePersonalDataHandler`: when `HasPasswordAsync`, `CheckPasswordAsync` must pass (wrong → one static `InvalidCredentials` "Incorrect password."); `ErasureService.ShredWithRetriesAsync(user.Id, 3)`; on failure the outcome keeps `Fault` and `Indeterminate` distinct for telemetry with one user-facing wording ("Your account could not be fully deleted. The failure has been logged for an administrator to finish."); on success the deferred `SignOutAsync`, a `[LoggerMessage]` audit line, and `NextUrl = completion ?? "/"`. The password field is always on the wire; the client renders it only when `GetAccountStatus.HasPassword`.

### 5.7 Surviving HTTP surface

Already hidden: `MapAdditionalIdentityEndpoints` returns `accountGroup.ExcludeFromDescription()` for the whole `/Account` group, and Razor component endpoints never enter ApiExplorer. Trains only delete. After Train 3 the non-gRPC surface is:

- `POST /Account/PerformExternalLogin` — the challenge to the provider.
- `GET /Account/ExternalLogin` — the OAuth callback, a static-SSR page moved out of `Components/` to `ExternalLogin/ExternalLoginCallback.razor` (route unchanged, `[ExcludeFromInteractiveRouting]`), rewritten onto the `.Outcome` views so the IL-scan exemption can be deleted outright.
- `GET /Account/Manage/LinkExternalLogin?provider=` and `GET /Account/Manage/LinkExternalLoginCallback` — the link start becomes an antiforgery-free GET because WASM and MAUI cannot render the scaffold's antiforgery form post; OAuth `state` covers login CSRF.
- `/connect/token`, `/_auth/complete` — unchanged.

Deleted: both passkey endpoints and `POST /Account/Logout` (once `Yggdrasil/src/Hosting.Web.Components/Layout/NavMenu.razor` switches to Heimdall's `Logout.razor`, which already calls `IAuthenticationService.Logout`). `IdentityComponentsEndpointRouteBuilderExtensions` becomes `ExternalLoginEndpointRouteBuilderExtensions.MapNorseExternalLoginEndpoints()`.

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
3. **Two-factor + passkeys + retirement.** Heimdall (Train 3 operations, `passkeys.js`, TwoFactorAuthentication, EnableAuthenticator / `VerifyAuthenticatorForm`, ResetAuthenticator, Disable2fa, GenerateRecoveryCodes, Passkeys, RenamePasskey) → Himinbjörg (handlers, passkey pair, `ExternalLogin` callback relocated, `Components/` deleted with both passkey endpoints and `POST /Account/Logout`, IL-scan exemption removed, `ci.yml minimum_coverage` ratcheted, README / CLAUDE.md pair) → Bragi → Yggdrasil (`App.razor` script, `NavMenu` logout, endpoint rename, CLAUDE.md row).

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
