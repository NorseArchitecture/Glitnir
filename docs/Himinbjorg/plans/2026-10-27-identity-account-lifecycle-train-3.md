# Identity Account Lifecycle over gRPC — Train 3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Pairs with superpowers:test-driven-development on every coding task, per standing law (Glitnir CLAUDE.md §2.8). **Every implementer and reviewer subagent runs on model `opus`.**

**Goal:** finish the identity account lifecycle (Himinbjörg#48 sub-project A). This is sub-project A's last train. Every flow must work end to end on the Blazor Server host, with one exception: Buvy assigned the external-provider walk of rows 10–12 to Train 5 (T3-36). Train 4 is OpenIddict (spec sub-project B). The work:
- Two-factor management, passkey management, and user-verifying, one-shot passkey sign-in, all over gRPC behind Heimdall pages.
- The last Himinbjörg scaffold (`Components/`) retired. The external-login callback rebuilt on `.Outcome` views, requiring the second factor.
- `returnUrl` honored, with every link between sign-in pages built by the server.
- External sign-in started from Login through a fail-closed GET start, proven by real-manager TestServer facts over a test-only remote scheme.
- Every security-stamp rotation refreshes the sign-in.
- The last-sign-in-method invariant gets its verification job, hosted in the web server.
- The nine Train 2 dockets each closed by a task or parked by Buvy.

**Architecture:**
- **Upstream:** Asgard adds `OutcomeComponentBase.CanAct`. Midgard forbids an identity principal's page navigation to the access-denied page.
- **Heimdall:**
  - `ITwoFactorService` and `IPasskeyService` (Account), and `ISignInMethodsService` (Public: the sign-in links and passkey sign-in).
  - Nine pages, a `SignInMethodLinks` loader, and `passkeys.js`.
- **Himinbjörg:**
  - Services, handlers and views.
  - Stateless passkey ceremonies, with a consumed-challenge ledger.
  - The recovery-codes handoff.
  - `ReturnUrls.Resolve`.
  - Configuration-driven OIDC providers and the GET start.
  - The verification service.
  - Deletion of `Components/`.
- **Downstream:**
  - Bragi fixes its click driver and adds the stories.
  - Yggdrasil composes everything.
  - Bifröst's AppHost is unchanged: no third-party container (Buvy).

**Tech Stack:**
- Runtime: .NET 11 RC3 (`DotNetVersion` `11.0.0-rc.3.26522.148` in Yggdrasil CPM), C# 15.
- Identity and auth: ASP.NET Core Identity 11 RC3 passkeys (`IPasskeyHandler<TUser>`, `PasskeyUserEntity`, `PasskeyAttestationResult`, `PasskeyAssertionResult<TUser>`, `UserPasskeyInfo`, `IdentityPasskeyOptions`); `Microsoft.AspNetCore.Authentication.OpenIdConnect` 11 (new, Himinbjörg); QRCoder `1.*` (new, Himinbjörg, Buvy-approved); protobuf-net.Grpc code-first.
- UI: Blazor (Server, WASM, bUnit 2.x); FluentUI Blazor 5.0.1; FluentValidation 12 via Blazilla.
- Data: EF Core 11 RC2 (Npgsql + SqlClient).
- Tests and tools: Testcontainers; xUnit v3 on MTP; Shouldly; NSubstitute; BlazingStory and node:test (Bragi); Playwright (walk).

**Spec:** `../specs/2026-10-26-identity-account-lifecycle-over-grpc-design.md` (r3 + §14). § references are to it unless marked.
- The binding Train 2 ledger rulings are 41, 42/45, 44, 46, 47 and 49 (`.superpowers/sdd/2026-10-26-identity-account-lifecycle-train-2/progress.md` at the Bifröst root, workspace-local).
- Task 6.3 writes the amendments below into the spec.

**Starting state (verified 2026-10-27, every realm on clean `master`):** Asgard v0.0.36, Midgard v0.0.38, Heimdall v0.0.23, Himinbjörg v0.0.27, Bragi v0.0.15 (+2 untagged test commits), Mímir v0.0.11 (untouched). Yggdrasil CPM pins those versions.

**Revision history:** r6, 2026-10-28.
- **r1:** the first draft.
- **r2:** answered the first review and Buvy's eight rulings.
- **r3:** answered the re-review and Buvy's rulings (no third-party containers; Trains 4 and 5; the contract split).
- **r4:** answers the r3 re-review.
  - C1: the GET starts fail closed.
  - I1: the container suites move to `Identity.Web.Server.Tests`.
  - I2: the ledger is a set-based statement.
  - I3: the user is re-read before a post-failure refresh.
  - I4: one shared `OnRemoteFailure`.
  - I5: the plan is compressed.
  - M10 (controller ruling): the recovery-codes handoff is one-shot.
  - M3: the Bragi-red window is accepted.
  - All ten Minors are applied.
- **r5:** answers Buvy's external review of r4 (`.superpowers/sdd/train-3-plan-rereview-r4.md`): `ReturnUrls.Resolve` rejects dot segments and encoded separators before containment (Glitnir §2.6); OIDC keeps the typed `AddOpenIdConnect` and shares only `ConfigureRemoteFailure`, proven by a real OIDC challenge; the lifecycle lane re-enables before a successful disable; the verification job traverses every account by keyset with a persisted, wrapping cursor (Glitnir §2.2); A1 separates development order from release order.
- **r6:** answers Buvy's second review: `returnUrl` validation is path-only (split at the first literal `?`/`#` before decoding; query and fragment kept verbatim; length and control characters stay global), with a WHATWG browser corroboration in the walk; the verification job detects the last page in the same run (`SampleSize + 1` read, so ceil(N / SampleSize) holds exactly) and advances its cursor by compare-and-set on a `Version`, so it never moves backward under several replicas.
- **r6.1:** Buvy's plan-level sign-off (2026-10-28) with two corrections: the concurrent-first-run fact asserts one cursor row only, and a barrier sibling pins exactly one advance; the traversal bound is qualified to a stable population, with reporting on the observing replica's next successful run.

The per-finding detail lives with the reviews: `.superpowers/sdd/train-3-plan-review.md`, `train-3-plan-rereview.md`, `train-3-plan-rereview-r3.md`.

## End state

Each row lists the flow, the tasks whose tests prove it, the Blazor Server walk assertion (Task 6.2 walks row *n* as W*n*), and the WASM proof.
- **WASM column:** the browser leg is an environment limit (T3-23). "Lane" means a Task 6.1 `CompositionTests` fact driving the operation over the gRPC-Web lane (`WebServerHost.CreateIdentityClient()` or the anonymous client). "—" means the flow has no gRPC operation of its own (an HTTP redirect, cookie, download or static-SSR page), walked once in the Server column.
- **MAUI:** sub-project C (spec ruling 1).

| # | Flow | Proof | Server walk asserts | WASM |
|---|---|---|---|---|
| 1 | Register (password) | Train 1 | account created | lane |
| 2 | Login honoring `returnUrl` | 3.3, 4.3 | `?returnUrl=%2FAccount%2FManage%2FPasskeys` lands there; `https://evil.example/` lands on `/` | lane `A_local_return_url_survives_login` |
| 3 | Logout from the nav | 6.1 `NavMenuTests` | nav link → Heimdall's page → signed out | lane |
| 4 | Forgot / reset password | Train 1 | logged link → reset → log in | lane |
| 5 | Confirm email, resend | Train 1, 3.4 | logged link → click-to-confirm | lane |
| 6 | Two-factor login to `returnUrl` | 4.3 | from `/Account/Manage/Email` → TOTP → lands there | lane |
| 7 | Remember this machine | Train 1, 4.3 | two chained `/_auth/complete` hops; next login skips the challenge | lane `Remember_machine_round_trips_over_the_wire` |
| 8 | Recovery-code login | Train 1, 4.3 | signs in | lane |
| 9 | Lockout: password and external | Train 1, 4.11 | five wrong passwords → lockout (the external lockout is 4.11's fact) | lane `Login_lockout_over_the_wire` |
| 10 | External register | 4.8, 4.11 | **Train 5** (T3-36, Buvy) | — |
| 11 | External sign-in from Login's link | 3.3, 4.8, 4.11 | provider-free half: no provider section; the passkey link's `href` is the server's; `GET /Account/PerformExternalLogin?provider=nope` returns to Login. Provider leg: **Train 5** | lane `Sign_in_methods_resolve_anonymously` |
| 12 | Second factor after the provider | 4.11 | **Train 5** | — |
| 13 | Passkey sign-in | 3.10, 4.9, 4.10 | from Login's passkey link → lands on `returnUrl`; replay is 4.10's fact (circuit call, nothing to replay) | lane, options only (T3-25) |
| 14 | Phone set / clear / unchanged | Train 2 | each | lane |
| 15 | Change email + confirm | Train 2 | confirmed | lane |
| 16 | Change / set password | Train 2 | change works; set renders the change form | lane |
| 17 | External logins list / remove / last-method refusal | Train 2, 4.8 | a seeded login (SQL) is listed and refused as the last method; seed removed. Linking is Train 5 | lane |
| 18 | Personal data download | Train 2, 3.4 | downloads | — |
| 19 | Erasure | Train 2 | shredded, signed out | lane |
| 20 | Two-factor page warnings | 3.5 | 3-left warning after spending codes | lane |
| 21 | Enable → refresh → codes after reload | 3.6, 4.4 | ten codes; the current history entry has no handoff (the `/_auth/complete` entry still does, T3-12); a second reveal shows the already-shown state | lane `The_two_factor_lifecycle_runs_over_the_wire` |
| 22 | Generate recovery codes | 3.7, 4.5 | ten codes; still signed in | lane, same fact |
| 23 | Reset authenticator, then re-enable with the replacement key | 3.6, 3.7, 4.4 | reset text; still signed in; the new key's code enables with "Your authenticator app has been verified." | lane, same fact |
| 24 | Disable two-factor | 3.7, 4.5 | disabled text; still signed in; a pre-disable session elsewhere is signed out | lane, same fact |
| 25 | Forget this browser | 3.5, 4.5 | forgotten text; next login challenges | lane `Forget_this_browser_over_the_wire` |
| 26 | Passkeys add / rename / remove | 3.8, 3.9, 4.6, 4.7 | rename pre-filled → "Passkey updated successfully." → "Passkey deleted successfully." | lane, options + unknown-id `NotFound` (T3-25) |
| 27 | Signed-out Manage visit | Train 2 | `/Account/Login?ReturnUrl=…` | lane |
| 28 | Policy failure → AccessDenied | 2.1, 3.3 | `/protected-probe` → `/Account/AccessDenied`; `Accept: application/json` → bare 403 | lane |
| 29 | Stranded-account verification | 4.12 | web resource logs a run; a seeded stranded row logs Critical within ceil(N / SampleSize) + 1 runs and not before its recheck; seed removed | n/a |
| 30 | Every story renders | 5.1–5.3 | each `data-norse-story-driver-state` recorded | n/a |

## Global Constraints

- **Stage only, never commit.** Every task ends "Stage (`git add` …) — the human commits". No commit, push, tag or publish by a subagent.
- **Branch law.** Never branch Bifröst. One fork per touched realm, made at the start of its phase with `git switch -c <branch> --no-track origin/master`:
  - Asgard: `feature/outcome-component-can-act`
  - Midgard: `feature/identity-forbid-access-denied`
  - Heimdall and Himinbjörg: `feature/account-lifecycle-two-factor-passkeys`
  - Bragi: `feature/two-factor-passkeys-stories`
  - Yggdrasil: `feature/account-lifecycle-train-3`
  - Glitnir and Bifröst docs are staged on `master`.
- **Workspace crossing.** Every phase is developed with `UseProjectReferences=true`. CI and publish gate only at each realm's PR. Yggdrasil builds on project references only from Phase 6.
- **Yggdrasil is the only CPM realm; its `Directory.Packages.props` is bot-owned — never edit it in a task.** QRCoder and OpenIdConnect enter through non-CPM Himinbjörg and reach Yggdrasil transitively.
- **Duplicate-route hazard (§8):** the Himinbjörg release that Yggdrasil consumes must already have deleted `Components/`.
- **House rules:** `../../house-rules.md` in full, plus:
  - US English;
  - every class `sealed`/`abstract`/`static`;
  - `CancellationToken cancellationToken = default` last;
  - `ConfigureAwait(false)` in src only;
  - warnings are errors (`[Parameter]` strings `= ""`; required references `= null!` with `[EditorRequired]`).
- **Wire records:**
  - shape: `[DataContract] sealed record` with `[DataMember(Order = n)]`;
  - strings `{ get; set; } = string.Empty`;
  - arrays `= []`, never `required`, with the per-member `CA1819` suppression `ExternalLoginsResponse` uses;
  - no `[Authorize]` and no mediator marker on wire types (`RequestContractTests`);
  - request scalars follow NORSE022.
- **Identity access law:**
  - Anything returning `IdentityResult`/`SignInResult` goes through a `.Outcome` view, returning `ValueTask<Outcome<…>>` (`UserManagerOutcomeView.cs:22-223`, `SignInManagerOutcomeView.cs:29,56,85`).
  - After Task 4.13, `Identity.Web.Server` has no IL-scan exemption.
  - Raw calls stay only inside `NorseSignInManager`/`NorseUserManager` reproductions, as `TwoFactorChallengeSignInAsync` handles `ResetLockout`.
- **Account handlers** start from `AccountCaller.ResolveAsync` (Task 4.2) and pass `Failed` through. Account-tier members carry `[Authorize(Policy = AuthNPolicies.Account)]`; Public-tier members carry `AuthNPolicies.Public`.
- **The server authors every flow URL (Buvy; `Heimdall/CLAUDE.md:54` unamended).**
  - Every continuation, status key, id, token, return URL and redirect comes from the server.
  - Component side: root-relative, PathBase-qualified via `UriHelper.BuildRelative`, and wrapped by `deferredCompletion.TryBuildUrl(url) ?? url`.
  - Endpoint side: every redirect is `UriHelper.BuildRelative(PathBase, "/Account/…", …)`, never a relative `Account/…`.
  - Status keys come only from `ManageUrls.Status`.
  - The client composes only static, stateless page-to-page links (`ManageNavMenu`, the two-factor page's sibling links). The one existing exception is spec §6.5's recovery-code sibling link.
  - Links between sign-in pages come from `GetSignInMethods` (T3-38).
- **Continuations (§6.3 as amended):**
  - `ISessionTransition.Begin(result)`: `EnableAuthenticator`, `ResetAuthenticator`, `DisableTwoFactor`, `ForgetTwoFactorClient`, `PasskeySignIn`.
  - Soft `NavigateTo(result.NextUrl)`: `AddPasskey`, `RenamePasskey`.
  - In place: `GetAuthenticatorSetup`, `GenerateRecoveryCodes`, `RevealRecoveryCodes`, `GetPasskeys`, `GetPasskey`, `RemovePasskey`, `GetPasskeyRequestOptions`, `GetPasskeyCreationOptions`, `GetSignInMethods`.
  - Full page load: each provider `StartUrl` (an HTTP endpoint).
- **Status keys** (`Heimdall/src/AuthN.Components/Shared/ManageStatus.cs`):
  - Already present, now emitted: `authenticator-reset`.
  - New: `authenticator-verified`, `two-factor-disabled`, `browser-forgotten`, `passkey-updated`, `external-login-failed`, `recovery-codes-failed`.
- **Click-to-confirm (ruling 12, §6.4).** `ResetAuthenticator`, `Disable2fa`, `GenerateRecoveryCodes`, `ConfirmEmailPanel` and `ConfirmEmailChangePanel` perform nothing on render.
  - A click trigger on an `OutcomeComponentBase` binds `Disabled="!CanAct"`.
  - On any other base, including `OutcomeFormComponentBase`, which derives `AsyncComponentBase` (`Asgard/src/Abstractions.Components/OutcomeFormComponentBase.cs:22`), it binds `Disabled="!RendererInfo.IsInteractive"`.
  - Every submit binds `Disabled="!CanSubmit"` (Ruling 46).
- **Parity literals (do not reword):**
  - The URI format `otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6`, with the key shown in lowercase groups of four.
  - Recovery codes in batches of `10`; passkey cap `100`; passkey name max `200`.
  - Messages: "Your authenticator app has been verified.", "You have generated new recovery codes.", "Passkey updated successfully.", "Passkey deleted successfully.", "Verification code is invalid.", "No passkey was provided by the authenticator.", "Some passkey features are missing. Please update your browser.", "You have reached the maximum number of allowed passkeys.".
  - The issuer is configuration (T3-27).
- **`returnUrl` law:** only `ReturnUrls.Resolve` decides a landing.
- **Generators:** none are touched. If one is, emitters use `sb.AppendCSharp(...)`.
- **Upstream filing:** none. Template findings are collected in §15 and filed only after delivery.
- **Tests:**
  - Run `dotnet test <Realm>.slnx`, or `dotnet test tests/<Project> -- --filter-class "*.<ClassName>"` (MTP; VSTest `--filter` does not work).
  - Container facts need Docker and live in `Identity.Web.Server.Tests` (`Himinbjorg/CLAUDE.md`, Build & Test).
  - Bragi JS: `npm test` in `Bragi/`.
- **Relative paths only**, workspace-relative from the Bifröst root. Ginnungagap is `../Ginnungagap`.

## Review Focus

1. **A non-local or hostile `returnUrl`.** These land on the app root:
   - `https://evil.example/`, `//evil.example`, `/\evil.example`, `javascript:alert(1)`;
   - a path outside a non-empty `PathBase`, including through `..`, `%2e%2e`, `%252e%252e` or mixed-case encodings;
   - in the **path** (before the first literal `?` or `#`): a `.`/`..` segment, an encoded `/`, `.` or `\`, a backslash, or `//`, including content an encoded `%3F`/`%23` might try to hide;
   - a control character anywhere (path, query or fragment).

   An accepted candidate's query and fragment survive verbatim, URL-shaped or not;
   - more than 2,048 characters.

   A local one is honored after password, two-factor, recovery-code, passkey and external sign-in. → 4.3 `ReturnUrlsTests`, 3.3 `Both_query_casings_reach_the_request`, 4.8 `Every_url_carries_only_the_resolved_landing`, 4.10/4.11 landing facts.
2. **Another account's passkey ceremony, or a replay.**
   - Registration: B posting A's attestation → `Forbidden`, nothing persisted. A replayed attestation re-saves the same credential to its own account and grants nothing (T3-32).
   - Sign-in: the assertion is purpose-bound, time-boxed, one-shot (`A_replayed_assertion_signs_in_nothing`) and user-verifying (`User_verification_is_required_for_passkeys`).
   → 4.6, 4.9, 4.10.
3. **A two-factor account signs in through a provider.** The challenge is required, and the external cookie is cleared before the challenge redirect. → 4.11 `An_external_sign_in_for_a_two_factor_account_requires_the_challenge`.
4. **A stamp rotation keeps this session, and the old cookie dies.** This includes a failure after the rotating write (T3-35), where the refresh uses a re-read user.
   - The lane host runs at `ValidationInterval = TimeSpan.Zero`, because the default 30 minutes makes the check vacuous.
   - After each rotating step, the pre-step cookie is refused and the refreshed cookie passes.

   → 4.4/4.5 real-store facts, 6.1 `The_two_factor_lifecycle_runs_over_the_wire`.
5. **No WebAuthn JSON support, or a dismissed prompt.**
   - Unsupported hides Add and Use.
   - `NotAllowedError` shows "No passkey was provided by the authenticator." and calls nothing.
   - `AbortError` is silent.

   → 3.8, 3.10.
6. **Login CSRF through either GET start.** Only `Sec-Fetch-Site: same-origin` or `none` starts a challenge. `cross-site`, `same-site` and an absent header challenge nothing. A callback that arrives without the starting browser's correlation cookie signs in nothing. → 4.8 (one fact per refused value, on both starts), 4.11 `A_callback_without_the_browsers_correlation_cookie_signs_in_nothing`.

## Rulings

Each ruling has the shape `Ruling: what — why — cost if wrong`. "Buvy" marks a human ruling of 2026-10-28; it binds over any review. The mechanism lives in the named task.

- **T3-1** (Buvy) Ruling: throttling of ChangePassword, DeletePersonalData and ChangeEmail confirmations is parked until the platform rate-limits its whole API footprint. — A per-operation lockout would let an attacker block the owner's erasure. — A live session can probe passwords at request speed until then.
- **T3-2** (Buvy) Ruling: `returnUrl` is honored.
  - `LoginRequest.ReturnUrl` (Order 4), `PasskeySignInRequest.ReturnUrl` and `GetSignInMethodsRequest.ReturnUrl` carry it, and the query key is read case-insensitively.
  - `ReturnUrls.Resolve` (4.3) decides every landing; the landing crosses the challenge inside the token (T3-21).
  - **Path-only validation (Buvy r6, verbatim):** "Split the original candidate at its first literal ? or #, before decoding. Apply separator, encoding, dot-segment and PathBase checks to the path only. Preserve an accepted candidate's query and fragment verbatim. Length and control-character rejection remain global." The order matters: decoding first would let `%3F`/`%23` become a new boundary that hides part of the path from validation. Query and fragment cannot change the immediate redirect's authority or path ([WHATWG URL, path state](https://url.spec.whatwg.org/#path-state): the path ends at the first `?` or `#`). A destination that later interprets a `next` (or any URL-shaped) query value validates it itself; `Resolve` vouches only for the landing's path.
  - This closes §11 and the session-transition seam spec's `:490` gap.

  — The server stays the only author of the final URL. — If `[SupplyParameterFromQuery]` ever matches case-sensitively, the cookie path's `ReturnUrl` stops binding; a bUnit fact pins both casings.
- **T3-3** (Buvy) Ruling: an identity principal that fails a policy gets AccessDenied on both lanes: Midgard on HTTP (2.1), and `NotAuthorizedRedirect` in-circuit (3.3). It is an accepted terminus until the refactor load drops. — A bare 403 renders the browser's error page. — principal-at-the-door is amended a fourth time.
- **T3-4** Ruling: `CanAct => RendererInfo.IsInteractive && !IsDispatching` on `OutcomeComponentBase`, as a property only, with the bindings in Global Constraints. It supersedes Ruling 49. — A live-looking button with no circuit reads as broken; Ruling 46 gave forms the same contract. — One binding per trigger, locked by `ClickStaticRenderLockTests`.
- **T3-5** Ruling: `ManageNavMenu` stays static. A fact asserts every `Href` routes to a Heimdall page. — The nav carries no state. — None.
- **T3-6** Ruling: `external-login-not-added` is only for `LoginAlreadyAssociated`; every other definite link failure answers `external-login-failed`, and indeterminate answers no key. — A message blaming association for a missing cookie is false. — One key.
- **T3-7** Ruling: Bragi's click driver settles without a mutation, and `StoryDriver` catches `JSException` into `failed`. Ruling 33's two stories return. — Train 3 adds click-only surfaces. — A no-op click passes the driver; the driven locks still assert outcomes.
- **T3-8** (Buvy) Ruling: `InteractiveBunitContext` consolidation is parked; Asgard needs a shared test framework, a known callout. No fourth copy: plain `BunitContext` tests call `SetRendererInfo`. — It is cross-realm test infrastructure. — The three copies drift until then.
- **T3-9** Ruling: Bifröst's README/CLAUDE.md pair gains state-of-the-union entries for Trains 1, 2 and 3 in one change (6.3). — The pair must tell one story; Train 1 shipped without an entry. — The Train 3 date may need a touch-up at merge.
- **T3-10** (Buvy) Ruling: no client-authored flow URLs.
  - `DisableTwoFactor`, `RenamePasskey`, `AddPasskey` and `EnableAuthenticator` answer `NavigationResult`.
  - `PasskeyEntry` carries a server `RenameUrl` (A2).

  — Flow rules never leak to the browser. — Wire shapes differ from §4.3.
- **T3-11** Ruling: `ForgetTwoFactorClient` answers a `DeferredCompletion` URL to `browser-forgotten`, and the client calls `Begin`. — The cookie delete rides the deferral on a circuit. — One forced load.
- **T3-12** Ruling: every stamp-rotating Train 3 operation (`EnableAuthenticator`, `DisableTwoFactor`, `ResetAuthenticator`) refreshes and answers `NavigationResult`. New codes survive the forced reload in a one-shot, data-protected handoff (4.4), revealed by `/Account/Manage/ShowRecoveryCodes`. This closes §11's stamp gap. — Codes-first display survives a reload once the codes ride a sealed token (spec ruling 4). — For five minutes the encrypted codes sit in browser history: the current entry is stripped, the `/_auth/complete?returnUrl=…` entry is not. A reveal needs the same user, the same stamp and an unchanged stored set, and works once; `url.query` redaction keeps the codes out of telemetry.
- **T3-13** Ruling: `GetAuthenticatorSetup` provisions a missing key losslessly without rotating the stamp, through `IAuthenticatorKeyProvisioning` (4.1). — An unverified key is not a factor; rotation starts at enable. — Two tabs provisioning at once: last write wins, and the loser's code is "invalid" until reload.
- **T3-14** (Buvy) Ruling: QRCoder `1.*` (`SvgQRCode`) renders on the server; the client shows `<img src="data:image/svg+xml;base64,…">`, never `MarkupString`. — An `<img>` cannot run script. — A new MIT dependency on a sealed server assembly.
- **T3-15** Ruling: the passkey manager methods get names distinct from the base:
  - registration: `CreatePasskeyOptionsAsync`, `AttestPasskeyAsync`;
  - sign-in: `CreatePasskeyRequestOptionsAsync`, `PasskeyAssertionSignInAsync`.

  The IL scan bans the five base members by name (A4). — A same-named member invites the cookie path. — Two names per concept.
- **T3-16** Ruling: the passkey cap of 100 is enforced on the server (`Conflict`), and the client hides Add at the cap. — The cap is a resource bound, not an invariant. — Two concurrent adds at 99 both pass.
- **T3-17** Ruling: RenamePasskey is a loader over a form, pre-filled from `GetPasskey`. — Parity. — One read per rename.
- **T3-18** (Buvy, the factor) Ruling: the callback page `/Account/ExternalLogin` signs in with `bypassTwoFactor: false`.
  - It clears the external cookie before the challenge redirect, and a lockout goes to `Account/Lockout`.
  - A provider failure shows a fixed `RemoteError` text, never the provider's.
  - Registration parses `EmailAddress` and calls Identity only through views.
  - This resolves `NorseSignInManager.cs:47-50`.

  — The platform's second factor must not depend on the provider's. — One more step for two-factor users.
- **T3-19** (Buvy) Ruling: Asgard and Midgard phases run in front of Heimdall (A1). — Dockets 3 and 4 are upstream-shaped. — Two more ship gates.
- **T3-20** (Buvy confirmed) Ruling: Train 3 adds three contracts instead of growing `IAccountService`. This amends spec ruling 3 and §4.3's header (A8): each tier is still one policy, spread across several contracts. — Growing `IAccountService` breaks the floating `master` builds of Himinbjörg and Bragi between Heimdall's publish and their merges. — Three proxies; two-click passkey sign-in; Login gains one injection (accepted red window, release order).
- **T3-21** Ruling: the `TwoFactorChallenge` payload becomes `v2|userId|stamp|1/0|provider|returnUrl`, split count 6; a v1 token is refused like an expired one. — v1 lives at most five minutes. — Anyone mid-challenge at deploy re-enters their password.
- **T3-22** Ruling: Himinbjörg's `minimum_coverage` is the measured branch coverage, rounded down to 5. — CI gates on branch coverage. — The human may lower it.
- **T3-23** Ruling: the WASM browser walk is an **environment limit**: RC3's Mono `dotnet.native.wasm` fails to compile on Chromium 156 ("module uses a mix of legacy and new exception handling instructions").
  - **Re-check** (Task 6.2 Step 1): Yggdrasil `DotNetVersion` ≠ `11.0.0-rc.3.26522.148`, or Playwright's Chromium major ≠ 156.
  - If either holds, walk the WASM column and add the Playwright phase to `WebServerBrowserRuntimeSmokeTests`.

  — Outside the platform's control. — The WASM browser leg is unverified until then; the lane facts cover the server side.
- **T3-24** Ruling: `AccountScenario.MachineRemembered = 10`. — §7 has no forget state. — None.
- **T3-25** Ruling: `passkeys.js` is pinned by bUnit `JSInterop.SetupModule` facts plus the walk's CDP virtual authenticator; there is no node harness. — Node cannot exercise WebAuthn. — Walk cost.
- **T3-26** Ruling: `AccountCaller` and `ManageUrls` land in 4.2, and the eight Train 2 handlers migrate in the same task. — Sixteen new handlers use them. — The shipped handlers' facts are the net.
- **T3-27** (Buvy) Ruling: the issuer is required configuration, `Identity:Authenticator:Issuer` (`ValidateOnStart`, no code default). Yggdrasil's `appsettings.Development.json` supplies `"Norse"`; a deployed host supplies its own. — An issuer of `Microsoft.AspNetCore.Identity.UI` is wrong, and only new enrollments read it. — Old enrollments keep their label.
- **T3-28** (Buvy, I1) Ruling: the stranded-account verification is a `BackgroundService` in `Identity.Web.Server`, hosted by Yggdrasil's web server. It walks every account by ordered keyset over the id with a persisted cursor that wraps (`verification_cursors`, written through `ProviderStatements`), so for a stable account population every account is visited within ceil(N / SampleSize) successful advances (Task 4.12 states the qualification), with the last page detected in the same run (no empty wrap run); the advance is compare-and-set on `Version`, so the cursor only moves forward within a traversal, under any number of replicas. A candidate is reported only when an explicit recheck on the replica's next run still finds it stranded, so on one replica every violation is reported within ceil(N / SampleSize) + 1 runs. It has an `Enabled` switch. A6 amends §11's "worker". — `Yggdrasil/CLAUDE.md:20` keeps the worker a stub; `Identity.EntityFramework` must not take a hosting dependency; two sightings absorb the `CreateAsync`→`AddLoginAsync` window. — Every web replica runs one keyset page read, one recheck read and one cursor upsert per interval; replicas share the cursor and a losing replica's advance is discarded (its page is read twice, never skipped); a restart forgets the pending candidates and delays their report one cycle; moving it to the worker later is a registration move.
- **T3-29** (Buvy) Ruling: `AddNorseExternalProviders` registers one OIDC scheme per `Identity:ExternalProviders:{Name}` section. **No container is added**: the AppHost and Bifröst `CLAUDE.md` §9 are unchanged, and Keycloak is a later enhancement. — Configuration is how real providers arrive. The platform's own provider is OpenIddict, which is client-credentials-only on `master` (`Himinbjorg/src/Identity.Web.Server/IdentityBuilderExtensions.cs:72-79`); authorization code + PKCE is Train 4. — Rows 10–12 have no local walk in Train 3 (T3-36).
- **T3-30** Ruling: passkeys do not count in §5.9's guard. — The guard is conservative and never strands an account. — A passwordless account with one login and a passkey cannot remove that login.
- **T3-31** Ruling: passkey sign-in mirrors the framework's `bypassTwoFactor` value, recorded by the 4.6 decompile; with UV required (T3-33), the assertion is possession plus inherence. — The framework decides, so the platform does not silently diverge. — If the value is `false`, passkey sign-in yields the challenge, and the facts follow the recorded value.
- **T3-32** (Buvy, I5) Ruling: the assertion is one-shot through a `consumed_passkey_challenges` ledger, consumed before the handler runs, by a set-based conditional insert (4.9). Attestation is not one-shot: a replay re-saves the same credential to the same account, and another account is refused by the `UserEntity.Id` binding. — A database ledger survives replicas and restarts. — One insert per passkey sign-in, plus an `InitialCreate` squash.
- **T3-33** (Buvy, I4) Ruling: `IdentityPasskeyOptions.UserVerificationRequirement = "required"`. — Without UV, a PIN-less key would be possession only. — Authenticators without UV cannot register.
- **T3-34** (Controller, fail closed) Ruling: both external starts are antiforgery-free GETs (`/Account/PerformExternalLogin`, `/Account/Manage/LinkExternalLogin`), and they proceed only for `Sec-Fetch-Site: same-origin` or `none`. Everything else, an absent header included, challenges nothing. A9 amends §5.7.
  - **Why:** correlation protects only the callback leg. A forced start runs entirely in the victim's browser, so a planted provider session would turn it into forced registration, or, on the link path, account takeover. Glitnir §2.7 forbids a silent fallback.
  - **Cost:** a browser that sends no Fetch Metadata (a non-secure context, or a pre-2023 engine) cannot start external sign-in and gets Login.

    Every legitimate path sends a passing value:
    - in-app links and force loads: `same-origin`;
    - typed URLs, bookmarks and a MAUI system-browser open: `none`;
    - the dev origin `https://yggdrasil.dev.localhost:5001` (`Yggdrasil/src/Hosting.Web.Server/Properties/launchSettings.json:8`) is a secure context, so browsers send the header;
    - TestServer harnesses set the header explicitly;
    - Vafthrudnir's Bruno collections are not a path the guard accommodates: they are stale, awaiting the Heimdall/Himinbjörg refactor, and none calls either start (Buvy); when refreshed, a start request sets `Sec-Fetch-Site: same-origin` itself;
    - a plain-HTTP LAN origin is not a supported dev origin; use the HTTPS endpoint.
- **T3-35** (I3) Ruling: once a stamp-rotating write succeeded or is indeterminate, the handler **re-reads the user** and refreshes even if a later step fails, then answers `NavigationResult` (`recovery-codes-failed`, or the two-factor page with no key). — Skipping the refresh brings back the 30-minute sign-out. Refreshing from the poisoned instance (`NorseUserStore.cs:58-62`) would write an uncommitted stamp into the cookie. — One more read and one more key.
- **T3-36** (Buvy) Ruling: the local walk of rows 10–12 lands with **Train 5 (full OpenIddict / ASP.NET Identity integration)**, federating against OpenIddict's own authorization code flow. Train 3 proves them by TestServer facts over a test-only remote scheme (4.11). — No third-party container; OpenIddict is the right target. — A defect seen only against a real provider surfaces in Train 5.
- **T3-37** (Buvy) Ruling: step-up re-authentication (`max_age` / `prompt=login` / `auth_time`) is **Train 4 (OpenIddict, spec sub-project B)**. — It is a session-assurance policy across every client. — Until Train 4, a hijacked live session can disable two-factor without a password (scaffold parity).
- **T3-38** (Buvy, I8) Ruling: every link between sign-in pages is server-built by `GetSignInMethods` and rendered by `SignInMethodLinks`. There is no new exception to `Heimdall/CLAUDE.md:54`. — The client never composes a URL that carries a return URL. — One anonymous read per gate-page render.

## Out of scope (spec-level, argued) and remaining deferrals

**Out of scope:**
- **Email delivery, `RequireConfirmedAccount` and `RegisterConfirmation`:** these belong to the messaging realm (spec ruling 7; the broker is undecided, Bifröst `CLAUDE.md` §9 item 1). The `IEmailSender<NorseUser>` seam is complete.
- **The tracking-consent gate:** removing it is a no-op. The check is `ITrackingConsentFeature?.CanTrack ?? true` (`Himinbjorg/src/Identity.Web.Server/Components/Pages/Manage/TwoFactorAuthentication.razor:84`), and no realm calls `UseCookiePolicy`/`AddCookiePolicy`.
- **MAUI and non-browser passkeys (`ValidateOrigin`/`ServerDomain`):** sub-project C.
- **The Blazilla submit flake and Asgard's `IAppShellLayout` `[DynamicallyAccessedMembers]`:** both are workstreams outside the lifecycle.

**Remaining deferrals:**

| Item | Ruled by | Lands with / re-check |
|---|---|---|
| T3-1 throttling | Buvy | the platform-wide rate limiter |
| T3-3 AccessDenied terminus | Buvy | when the refactor load drops |
| T3-8 shared test framework | Buvy | Asgard's shared test behaviors |
| T3-23 WASM browser walk | environment limit | `DotNetVersion` ≠ `11.0.0-rc.3.26522.148`, or Chromium major ≠ 156 |
| T3-37 step-up | Buvy | Train 4 (OpenIddict, spec sub-project B) |
| T3-36 rows 10–12 local walk | Buvy | Train 5 (full OpenIddict / ASP.NET Identity integration) |

## Spec amendments (Task 6.3)

- **A1, §2 ruling 6 / §9 item 3:** the **development** order is Asgard → Midgard → Heimdall → Himinbjörg → Bragi → Yggdrasil (T3-19). The **release** order is Asgard, Midgard, then Heimdall → Bragi → Himinbjörg, then Yggdrasil (Gates and release).
- **A2, §4.3:** the Train 3 rows move to the three contracts (Task 3.1).
  - `EnableAuthenticator`, `DisableTwoFactor`, `AddPasskey`, `RenamePasskey` → `NavigationResult`.
  - New operations: `RevealRecoveryCodes`, `GetPasskey`, `GetPasskeyRequestOptions`, `PasskeySignIn`, `GetSignInMethods`.
  - `PasskeyEntry.RenameUrl`.
- **A3, §5.2:** new views — passwordless `CreateAsync`, `ProvisionAuthenticatorKeyAsync`, `ExternalLoginSignInAsync`, the passkey views. The challenge redemptions return the landing.
- **A4, §5.3/§5.5:**
  - Three protectors (challenge, attestation, assertion) plus the one-shot handoff, and T3-15's names.
  - External login requires the second factor.
  - Strike "Passkey *login* (`PasskeySignInAsync`) is not in scope; `passkeys.js` keeps a `request` export for that follow-on".
  - The assertion is one-shot, UV is required, and `consumed_passkey_challenges` joins §5's schema list as non-temporal.
- **A5, §6.3:** the continuation table matches Global Constraints.
- **A6, §11:**
  - Closed: returnUrl, stamp-without-refresh, passkey login, issuer, verification job ("a worker to run it" → "the web host runs it").
  - Out of scope: email delivery, tracking consent, MAUI/non-browser passkeys.
  - Parked: throttling.
  - Moved: step-up → Train 4; the rows 10–12 walk → Train 5.
- **A7:** the session-transition seam spec (`../../Asgard/specs/2026-08-11-session-transition-seam-design.md:490`) records that returnUrl is honored.
- **A8, §2 ruling 3 and §4.3 header:** each tier is one policy across several contracts (T3-20).
- **A9, §5.7:** the surviving HTTP surface is the fail-closed `GET /Account/PerformExternalLogin`, the fail-closed `LinkExternalLogin` GET pair, and the `/Account/ExternalLogin` page. The POST start and `TemporaryFluentButtonFix` are gone. A browser without Fetch Metadata cannot start external sign-in.
- **A10, §15 dependency lines:**
  - "**Train 4 (OpenIddict, spec sub-project B):** step-up re-authentication through `max_age` / `prompt=login` / `auth_time` (T3-37, Buvy)."
  - "**Train 5 (full OpenIddict / ASP.NET Identity integration):** the local end-to-end walk of external register, sign-in and second factor after the provider, federating against OpenIddict's own authorization code flow (T3-36, Buvy)."

## Gates and release

| Phase | Realm | Gate before it starts | Ship gate at its end |
|---|---|---|---|
| 1 | Asgard | none | PR, tag, publish |
| 2 | Midgard | none | PR, tag, publish |
| 3 | Heimdall | Phase 1 code exists locally | PR (consumes Asgard), tag, publish |
| 4 | Himinbjörg | Phase 3 code exists locally | PR (consumes Heimdall), tag, publish |
| 5 | Bragi | Phase 3 code exists locally; **code-complete before Heimdall ships** | PR, tag, publish |
| 6 | Yggdrasil; Bifröst/Glitnir docs | Phases 2, 4, 5 code-complete locally | one Yggdrasil PR with every bump folded; docs staged on `master`; no AppHost change |

**Release order (human):**
1. **Asgard** first, published before any downstream PR builds against it (NU1605).
2. **Midgard**, any time before Yggdrasil.
3. **Heimdall → Bragi → Himinbjörg.**
   - **Accepted red window (controller ruling, Train 2 precedent):** Login now injects `ISignInMethodsService`. Bragi `NorseRef`s Heimdall unpinned, and `Bragi/tests/DesignSystem.Stories.Tests/DrivenStoryNavigationTests.cs:44,83` render Login. So Bragi's `master` is red from Heimdall's publish until Bragi's Train 3 PR merges.
   - Bragi's PR is queued to merge immediately after Heimdall's publish. There is no second Heimdall release, and the handoff records the red window as expected so the bot traffic is not chased.
   - No other floating consumer renders Login: Himinbjörg maps no Heimdall components, and Yggdrasil pins through CPM.
   - **Himinbjörg squashes `InitialCreate`** once (4.9, which creates both runtime-state tables). Wipe the local `norse-pg-primary`/`norse-pg-replica` volumes after pulling it.
4. **Yggdrasil** last. Its bot bumps for Midgard, Heimdall and Himinbjörg each go red alone:
   - `ChallengeAndForbidTests.cs:96`;
   - duplicate Manage routes against Himinbjörg v0.0.27;
   - `Program.cs:52,140`.

   The human folds all five bumps into the one Train 3 PR.

**Follow-up (not a Train 3 task):** Vafthrudnir's collections are refreshed against the Train 3 surface after Train 3 lands (Buvy).

A Ginnungagap scatter PR that lands mid-train is rebased after that realm's merge. Bragi's two untagged commits ride its Train 3 tag.

**Re-plan triggers:**
- **The 4.6 decompile disagrees with the Interfaces blocks:** `PasskeyAttestationResult` lacks `UserEntity`; `PasskeyAssertionResult<TUser>` lacks `User`; the `IPasskeyHandler<TUser>` signatures differ from §5.5; or the handler ignores `UserVerificationRequirement`. Stop.
- **`[SupplyParameterFromQuery(Name = "returnUrl")]` does not bind `?ReturnUrl=`:** parse `QueryHelpers.ParseQuery(new Uri(NavigationManager.Uri).Query)` in `OnParametersSet` (Tasks 3.3 and 3.10 only).
- **Midgard's `ForbidAsync(IdentityConstants.ApplicationScheme, …)` does not redirect:** stop; 2.1 re-plans to a `Response.Redirect` from `CookieAuthenticationOptions.AccessDeniedPath`.
- **QRCoder `1.*` is not warning-clean:** stop and ask. Never hand-roll a QR encoder.
- **The callback harness cannot compile Razor:** switch the render facts to bUnit with a cascaded `HttpContext`, and keep one TestServer route fact.
- **The test-only remote scheme cannot carry its correlation under TestServer:** drive the cookie container by hand (the `LinkExternalLoginEndpointTests` pattern). Never seed the external cookie for the start-leg or correlation facts.

## New folders (index)

The per-task **Files** lines are authoritative.
- Heimdall `src/AuthN.Components.FluentUI/Manage/` gains seven pages plus `RecoveryCodesHandoff`.
- Himinbjörg `src/Identity.Web.Server/` gains `TwoFactor/`, `Passkeys/`, `SignInMethods/`, `ExternalLogin/`, `Verification/`.
- Himinbjörg `tests/Identity.Web.Server.Tests/` gains the same folders.
- Bragi gains `TwoFactor/` and `Passkeys/`.

---

## Phase 1 — Asgard

### Task 1.1: `OutcomeComponentBase.CanAct`

**Files:** Modify `Asgard/src/Abstractions.Components/OutcomeComponentBase.cs`, `Asgard/README.md:16`, `Asgard/CLAUDE.md:16`. Create `Asgard/tests/Abstractions.Components.Tests/OutcomeComponentCanActTests.cs`.

**Interfaces:** `protected bool CanAct => RendererInfo.IsInteractive && !IsDispatching;`. `DispatchAsync` is unchanged.

- [ ] **Step 1: Fork.** `git -C Asgard switch -c feature/outcome-component-can-act --no-track origin/master`.
- [ ] **Step 2: Write the failing tests.**
  - `OutcomeComponentCanActTests : BunitContext` is a new class: `OutcomeComponentBaseTests` news its `Harness` up directly, so `RendererInfo` would throw there.
  - It uses a test component that renders `CanAct` and exposes a dispatch trigger.
  - Facts:
    - `An_interactive_idle_component_can_act` (`SetRendererInfo(new RendererInfo("Server", isInteractive: true))`);
    - `A_static_render_cannot_act`;
    - `An_in_flight_dispatch_cannot_act` (`TaskCompletionSource`: false while pending, true after).
- [ ] **Step 3: Run** `dotnet test tests/Abstractions.Components.Tests -- --filter-class "*.OutcomeComponentCanActTests"`. Expected: a build error.
- [ ] **Step 4: Implement.** The XML doc names the binding and why loaders are not gated.
- [ ] **Step 5: Add the guard** `A_static_render_still_dispatches_a_loader`. It is green before and after, and is labeled a guard.
- [ ] **Step 6: Run** `dotnet test Asgard.slnx` — green.
- [ ] **Step 7: Docs.** In README:16 and CLAUDE.md:16, after `OutcomeComponentBase`, add: "(click triggers bind `Disabled="!CanAct"` — interactive and not dispatching; loaders are not gated)".
- [ ] **Step 8: Stage** (`git -C Asgard add -A`) — the human commits. Ship gate.

## Phase 2 — Midgard

### Task 2.1: Identity principal page navigation forbids to the access-denied page

**Files:** Modify `Midgard/src/Infrastructure.Web.Server/Authentication/NorseBrowserHandler.cs`. Test `tests/Infrastructure.Web.Server.Tests/Authentication/NorseBrowserHandlerTests.cs:188`. Glitnir: `docs/Platform/specs/2026-08-21-principal-at-the-door-design.md` (`:3`, §2.4, §4.4 `:203`, §7 `:276`) and `docs/Platform/plans/2026-08-21-principal-at-the-door.md`.

**Interfaces:** an identity (non-anonymous) principal on a page navigation → `Context.ForbidAsync(IdentityConstants.ApplicationScheme, properties)`, with the same `properties` the anonymous challenge branch builds. Every other forbid is unchanged.

- [ ] **Step 1: Fork.** `git -C Midgard switch -c feature/identity-forbid-access-denied --no-track origin/master`.
- [ ] **Step 2: Write the failing tests.**
  - Rename `:188` to `Forbid_of_an_identity_principal_on_a_page_navigation_redirects_to_access_denied`: a 302 to exactly `/Account/AccessDenied`, with `ReturnUrl` = the original path and query.
  - Add the theory `Forbid_of_an_identity_principal_off_a_page_navigation_is_a_bare_403` over `Accept: application/json`, `X-Requested-With: XMLHttpRequest` and `POST`: 403, no `Location`.
- [ ] **Step 3: Run.** Expected: the renamed fact fails.
- [ ] **Step 4: Implement.** The doc comment says: "2026-10-28, Ruling T3-3; AccessDenied is the accepted terminus until the flow is revisited".
- [ ] **Step 5: Run** `dotnet test Midgard.slnx` — green.
- [ ] **Step 6: Amend Glitnir.**
  - The spec header (`:3`) records a fourth amendment.
  - §2.4 gains: "An identity principal's page navigation that fails a policy is forbidden through the identity cookie, which redirects to `/Account/AccessDenied`; every other forbid of an identity principal stays a bare 403 (Himinbjörg lifecycle Train 3, Ruling T3-3)."
  - §4.4 `:203` and §7 `:276` say the same, as does every plan line `grep -n "identity principal"` finds.
  - `git -C Glitnir add` both files.
- [ ] **Step 7: Stage** (`git -C Midgard add -A`) — the human commits. Ship gate.

## Phase 3 — Heimdall

### Task 3.1: Three contracts, sixteen records, `LoginRequest.ReturnUrl`

**Files:** Create the three contracts and sixteen records under `Heimdall/src/AuthN.Services/`. Modify `LoginRequest.cs` and the `AuthN.Services.csproj` Description. Test `tests/AuthN.Services.Tests/RequestContractTests.cs`, `ContractShapeTests.cs` (new).

**Interfaces (consumed by Phases 3–6):** every operation is `[OperationContract] Task<Outcome<T>> X(…, CancellationToken cancellationToken = default)`.
- **`[ServiceContract(Name = "grpc.twofactor.v1.TwoFactorService")] ITwoFactorService`** (Account):
  - `GetAuthenticatorSetup() → AuthenticatorSetupResponse`
  - `EnableAuthenticator(EnableAuthenticatorRequest) → NavigationResult`
  - `RevealRecoveryCodes(RevealRecoveryCodesRequest) → RecoveryCodesResponse`
  - `ResetAuthenticator() → NavigationResult`
  - `DisableTwoFactor() → NavigationResult`
  - `GenerateRecoveryCodes() → RecoveryCodesResponse`
  - `ForgetTwoFactorClient() → NavigationResult`
- **`[ServiceContract(Name = "grpc.passkey.v1.PasskeyService")] IPasskeyService`** (Account):
  - `GetPasskeys() → PasskeysResponse`
  - `GetPasskey(GetPasskeyRequest) → PasskeyEntry`
  - `GetPasskeyCreationOptions() → PasskeyCreationOptionsResponse`
  - `AddPasskey(AddPasskeyRequest) → NavigationResult`
  - `RenamePasskey(RenamePasskeyRequest) → NavigationResult`
  - `RemovePasskey(RemovePasskeyRequest) → BoolResponse`
- **`[ServiceContract(Name = "grpc.signinmethods.v1.SignInMethodsService")] ISignInMethodsService`** (Public):
  - `GetSignInMethods(GetSignInMethodsRequest) → SignInMethodsResponse`
  - `GetPasskeyRequestOptions() → PasskeyRequestOptionsResponse`
  - `PasskeySignIn(PasskeySignInRequest) → NavigationResult`
- **Records** (order in parentheses):
  - `AuthenticatorSetupResponse { SharedKey (1), AuthenticatorUri (2), QrCodeSvg (3) }`
  - `EnableAuthenticatorRequest { Code (1) }`
  - `RevealRecoveryCodesRequest { Handoff (1) }`
  - `RecoveryCodesResponse { RecoveryCodes (1): string[] }`
  - `PasskeysResponse { Passkeys (1): PasskeyEntry[] }`
  - `PasskeyEntry { CredentialId (1) (base64url), Name (2) ("" when unnamed), RenameUrl (3) }`
  - `GetPasskeyRequest { CredentialId (1) }`
  - `PasskeyCreationOptionsResponse { OptionsJson (1), AttestationState (2) }`
  - `AddPasskeyRequest { CredentialJson (1), AttestationState (2) }`
  - `RenamePasskeyRequest { CredentialId (1), Name (2) }`
  - `RemovePasskeyRequest { CredentialId (1) }`
  - `PasskeyRequestOptionsResponse { OptionsJson (1), AssertionState (2) }`
  - `PasskeySignInRequest { CredentialJson (1), AssertionState (2), RememberMe (3): bool, ReturnUrl (4) }`
  - `GetSignInMethodsRequest { ReturnUrl (1) }`
  - `SignInMethodsResponse { PasswordSignInUrl (1), PasskeySignInUrl (2), Providers (3): ExternalProviderLink[] }`
  - `ExternalProviderLink { DisplayName (1), StartUrl (2) }`
- **`LoginRequest.ReturnUrl`:** `[DataMember(Order = 4)] public string ReturnUrl { get; set; } = string.Empty;`.

- [ ] **Step 1: Fork.** `git -C Heimdall switch -c feature/account-lifecycle-two-factor-passkeys --no-track origin/master`.
- [ ] **Step 2: Write the failing tests.**
  - `ContractShapeTests`:
    - `Each_train_3_contract_has_its_service_name_and_operations` (7/6/3 methods, each `Task<Outcome<>>`, the last parameter a defaulted token);
    - `IAccountService_is_unchanged` (8 methods);
    - `ProtoBuf.Serializer.DeepClone` round trips: `PasskeysResponse` (two entries), an empty `RecoveryCodesResponse` (comes back `[]`), `LoginRequest` with `ReturnUrl = "/Account/Manage"`, a full `PasskeySignInRequest`, and `SignInMethodsResponse` with one provider and with none.
  - Extend `RequestContractTests`' explicit lists if it keeps any.
- [ ] **Step 3: Run** `dotnet test tests/AuthN.Services.Tests`. Expected: build errors.
- [ ] **Step 4: Implement**, with XML docs naming each continuation class. Update the Description.
- [ ] **Step 5: Run** — green.
- [ ] **Step 6: Stage** (`git -C Heimdall add -A`) — the human commits.

### Task 3.2: Seven validators

**Files:** Create in `Heimdall/src/AuthN.Components/`: `EnableAuthenticatorRequestValidator`, `RevealRecoveryCodesRequestValidator`, `GetPasskeyRequestValidator`, `AddPasskeyRequestValidator`, `RenamePasskeyRequestValidator`, `RemovePasskeyRequestValidator`, `PasskeySignInRequestValidator`. Test: one `{Name}Tests.cs` each in `tests/AuthN.Components.Tests/`.

**Interfaces:**
- `EnableAuthenticator`: `Code` NotEmpty, raw Length(6, 7).
- `RevealRecoveryCodes`: `Handoff` NotEmpty.
- `GetPasskey`, `RemovePasskey`: `CredentialId` NotEmpty.
- `AddPasskey`: `CredentialJson` and `AttestationState` NotEmpty.
- `RenamePasskey`: `CredentialId` NotEmpty; `Name` NotEmpty, MaximumLength(200).
- `PasskeySignIn`: `CredentialJson` and `AssertionState` NotEmpty. No rule on `ReturnUrl`.
- `GetSignInMethods` has no validator: its only member is the server-resolved `ReturnUrl`.
- Heimdall registers no validators (`src/AuthN.Components/ServiceCollectionExtensions.cs` registers only `ISessionTransition`). Yggdrasil discovers them (§8); Bragi lists them (5.2).

- [ ] **Step 1: Write the failing tests** in the `TwoFactorSignInRequestValidatorTests` shape.
  - Each valid case passes, and each failing case names its property.
  - `RenamePasskey`: 201 characters fails, 200 passes.
  - `EnableAuthenticator`: `"123 456"` passes, `"12345"` fails.
- [ ] **Step 2: Run.** Expected: build errors. **Step 3: Implement.** **Step 4: Run** `dotnet test tests/AuthN.Components.Tests` — green.
- [ ] **Step 5: Stage** — the human commits.

### Task 3.3: Login `returnUrl`, `SignInMethodLinks`, `NotAuthorizedRedirect`, status keys

**Files:**
- Modify `src/AuthN.Components.FluentUI/Login.razor`, `src/AuthN.Components/Shared/ManageStatus.cs`.
- Create `src/AuthN.Components.FluentUI/SignInMethodLinks.razor`, `src/AuthN.Components/NotAuthorizedRedirect.razor`.
- Test: `tests/AuthN.Components.FluentUI.Tests/LoginTests.cs`, `SignInMethodLinksTests.cs`; `tests/AuthN.Components.Tests/NotAuthorizedRedirectTests.cs`, `Shared/ManageStatusTests.cs`.

**Interfaces:**
- **Login:**
  - `[SupplyParameterFromQuery(Name = "returnUrl")] public string? ReturnUrl { get; set; }`, copied into `_request.ReturnUrl` (`?? string.Empty`).
  - It renders `<SignInMethodLinks ReturnUrl="@ReturnUrl" ShowPasskey="true" />`.
- **`SignInMethodLinks`** (an `OutcomeComponentBase` loader):
  - Parameters: `string? ReturnUrl`, `bool ShowPassword`, `bool ShowPasskey`.
  - It loads `GetSignInMethods` once per `ReturnUrl`.
  - It renders, as the server gave them:
    - "Use a passkey" (`PasskeySignInUrl`);
    - "Use a password" (`PasswordSignInUrl`);
    - one "Sign in with {DisplayName}" per provider (`StartUrl`, `<a target="_top">`, so the interactive router never intercepts it).
  - No providers: no provider section. A failed load: only the problem line.
  - It composes no URL.
- **`NotAuthorizedRedirect`:** `[Parameter, EditorRequired] public ClaimsPrincipal User { get; set; } = null!;`.
  - An identity principal (authenticated, not in `NorseRoles.Anonymous`) → `NavigateTo("Account/AccessDenied", replace: true)`.
  - Otherwise → `<RedirectToLogin />`.
- **`ManageStatus` keys and their exact texts:**
  - `AuthenticatorVerified = "authenticator-verified"`: "Your authenticator app has been verified."
  - `TwoFactorDisabled = "two-factor-disabled"`: "Two-factor authentication has been disabled. You can re-enable it when you set up an authenticator app."
  - `BrowserForgotten = "browser-forgotten"`: "The current browser has been forgotten. When you log in again from this browser you will be prompted for your two-factor code."
  - `PasskeyUpdated = "passkey-updated"`: "Passkey updated successfully."
  - `ExternalLoginFailed = "external-login-failed"`: "Error: The external login could not be added. Try again."
  - `RecoveryCodesFailed = "recovery-codes-failed"`: "Two-factor authentication is on, but your recovery codes could not be confirmed. Generate new recovery codes now."

- [ ] **Step 1: Write the failing tests.**
  - `LoginTests`:
    - the theory `Both_query_casings_reach_the_request` (`?returnUrl=` and `?ReturnUrl=` with `%2FAccount%2FManage` → `Received(1).Login(Arg.Is<LoginRequest>(r => r.ReturnUrl == "/Account/Manage"), …)`);
    - `No_return_url_sends_an_empty_string`;
    - `Login_hands_its_return_url_to_the_sign_in_links`.
  - `SignInMethodLinksTests`:
    - `Every_link_is_the_servers_url_verbatim`;
    - `Each_provider_renders_one_top_level_link` (`target="_top"`);
    - `No_providers_renders_no_provider_section`;
    - `Show_flags_select_the_password_and_passkey_links`;
    - `A_failed_load_renders_the_problem_and_no_links`.
  - `NotAuthorizedRedirectTests`:
    - `An_identity_principal_is_sent_to_access_denied_replacing_history`;
    - `An_anonymous_role_principal_is_sent_to_login_with_the_return_url`;
    - `An_unauthenticated_principal_is_sent_to_login_with_the_return_url`.
  - `ManageStatusTests`: a theory over the six keys and their texts.
- [ ] **Step 2: Run.** Expected: failures. **Step 3: Implement.** **Step 4: Run** — green.
- [ ] **Step 5: Stage** — the human commits.

### Task 3.4: `CanAct` adoption and the click static-render lock

**Files:**
- Modify:
  - `src/AuthN.Components/Logout.razor` (`disabled="@(!CanAct)"`);
  - in `src/AuthN.Components.FluentUI/`: `ConfirmEmailPanel.razor`, `ConfirmEmailChangePanel.razor`, `Manage/ExternalLogins.razor`, `Manage/PersonalData.razor`, `Manage/ChangeEmailForm.razor`.
- Create `tests/AuthN.Components.FluentUI.Tests/ClickStaticRenderLockTests.cs`.
- Add `SetRendererInfo(new RendererInfo("Server", isInteractive: true))` to these constructors: `tests/AuthN.Components.Tests/LogoutTests.cs`; in `tests/AuthN.Components.FluentUI.Tests/`, `ConfirmEmailPanelTests.cs`, `ConfirmEmailChangePanelTests.cs`, `Manage/ExternalLoginsTests.cs`, `Manage/PersonalDataTests.cs`.
- `Manage/ChangeEmailFormTests.cs` (already `InteractiveBunitContext`, `:12`) is untouched.

**Interfaces:**
- `CanAct`: the two panels and the `ExternalLogins` remove buttons.
- `!RendererInfo.IsInteractive`: `PersonalData` download and `ChangeEmailForm` resend.

- [ ] **Step 1: Write the failing test** `ClickStaticRenderLockTests : BunitContext` (static renderer, substitutes as in `FormStaticRenderLockTests`).
  - The theory `A_statically_rendered_click_trigger_is_disabled(Type component, string selector)` has rows for the six triggers; Tasks 3.5, 3.7, 3.8 and 3.10 add rows.
  - `Every_outcome_component_with_a_click_trigger_is_in_the_theory`: each concrete `OutcomeComponentBase` subclass in both assemblies is a row or is in `LoaderOnly`.
    - Today `LoaderOnly` is `Index`, `Email`, `ChangePassword`, `SetPassword`, `DeletePersonalData`, `SignInMethodLinks`.
    - Task 3.6 adds `EnableAuthenticator` and `RecoveryCodesHandoff`; Task 3.9 adds `RenamePasskey`.
- [ ] **Step 2: Run.** Expected: six failures. **Step 3: Implement.** **Step 4: Run** `dotnet test Heimdall.slnx` — green.
- [ ] **Step 5: Stage** — the human commits.

### Task 3.5: `TwoFactorAuthentication` + forget this browser

**Files:** Create `Manage/TwoFactorAuthentication.razor`. Test `Manage/TwoFactorAuthenticationTests.cs`.

**Interfaces:** `@page "/Account/Manage/TwoFactorAuthentication"`, a loader over `IAccountService.GetAccountStatus`.
- Recovery-code warnings (with a link to GenerateRecoveryCodes): "You have no recovery codes left." / "You have 1 recovery code left." / "You have {n} recovery codes left." (n of 2–3); nothing above 3.
- Static links to Disable 2FA, reset recovery codes, and Add, Set up or Reset the authenticator.
- "Forget this browser" shows only when `IsMachineRemembered` → `ForgetTwoFactorClient` → `Begin`.
- No tracking-consent gate.

- [ ] **Step 1: Write the failing tests:**
  - `Two_factor_off_shows_only_the_add_authenticator_link`;
  - `The_recovery_code_warning_matches_the_count` (0/1/3/4);
  - `Forget_this_browser_is_shown_only_when_the_machine_is_remembered`;
  - `Forget_this_browser_begins_one_transition_with_the_server_url` (`NextUrl == "/Account/Manage/TwoFactorAuthentication?status=browser-forgotten"`);
  - `A_failed_forget_renders_the_problem_and_begins_nothing`;
  - `A_failed_status_load_renders_the_problem`.

  Add the forget row to `ClickStaticRenderLockTests`.
- [ ] **Step 2: Run.** Expected: a build error. **Step 3: Implement.** **Step 4: Run** the classes plus `ClickStaticRenderLockTests` and `ManagePolicyLockTests` — green.
- [ ] **Step 5: Stage** — the human commits.

### Task 3.6: `EnableAuthenticator` + `VerifyAuthenticatorForm` + `RecoveryCodesHandoff`

**Files:** Create `Manage/EnableAuthenticator.razor`, `Manage/VerifyAuthenticatorForm.razor`, `Manage/RecoveryCodesHandoff.razor`. Test: one class each. Modify `FormStaticRenderLockTests`, `ClickStaticRenderLockTests` (`LoaderOnly`).

**Interfaces:**
- **EnableAuthenticator** (`/Account/Manage/EnableAuthenticator`): a loader over `GetAuthenticatorSetup`. It renders `SharedKey` in `<kbd>` and `<img alt="QR code for your authenticator app" src="data:image/svg+xml;base64,{base64 of UTF-8 QrCodeSvg}">`.
- **`VerifyAuthenticatorForm : OutcomeFormComponentBase`:** `FormName = "authn-enable-authenticator"`, `autocomplete="one-time-code"`; success → `Begin(result)`.
- **`RecoveryCodesHandoff`:**
  - Route `@page "/Account/Manage/ShowRecoveryCodes"`.
  - Its name differs from `Shared/ShowRecoveryCodes` because `_Imports.razor` imports `Manage`; a same-named page would shadow the panel and recurse.
  - `[SupplyParameterFromQuery] string? Handoff`.
  - On the first interactive render it replaces the history entry with the bare route, then `RevealRecoveryCodes`, then the panel with "Your authenticator app has been verified.".
  - A missing handoff, a failure or an already-revealed handoff renders the problem in place, with no codes.

- [ ] **Step 1: Write the failing tests.**
  - Page:
    - `The_shared_key_and_qr_render_from_the_setup` (`src` starts `data:image/svg+xml;base64,`; no `<svg>` element);
    - `A_failed_setup_load_renders_the_problem_and_no_form`.
  - Form:
    - `An_empty_submit_never_reaches_the_service`;
    - `An_invalid_code_lands_on_the_field`;
    - `Success_begins_one_transition_with_the_server_url`;
    - `Reject_edit_resubmit_dispatches_twice`.
  - Handoff page:
    - `The_handoff_is_revealed_and_the_codes_render_with_the_verified_message`;
    - `The_history_entry_is_replaced_without_the_handoff`;
    - `A_failed_reveal_renders_the_problem_and_no_codes`;
    - `An_already_revealed_handoff_renders_the_already_shown_state` (the server's "These recovery codes have already been shown." problem, and no codes);
    - `No_handoff_renders_an_error_and_calls_nothing`;
    - `The_handoff_page_and_the_shared_panel_are_distinct_types` (the route attribute is `/Account/Manage/ShowRecoveryCodes`).
- [ ] **Step 2: Run.** Expected: a build error. **Step 3: Implement.** Add the form to `FormStaticRenderLockTests.Forms`. **Step 4: Run** — green.
- [ ] **Step 5: Stage** — the human commits.

### Task 3.7: `ResetAuthenticator`, `Disable2fa`, `GenerateRecoveryCodes`

**Files:** Create the three pages under `Manage/`. Test: one class each.

**Interfaces:** each is a click-only `OutcomeComponentBase` page with a `Disabled="!CanAct"` button and the scaffold's warning copy.
- Reset → `Begin`.
- Disable → `DisableTwoFactor` → `Begin`.
- Generate → `Shared/ShowRecoveryCodes` in place with "You have generated new recovery codes.".

- [ ] **Step 1: Write the failing tests:**
  - per page: `A_bare_render_performs_nothing` and `A_validation_failure_renders_in_place`;
  - success: one `Begin` with the server URL (Reset, Disable), or the codes rendered (Generate).

  Add three `ClickStaticRenderLockTests` rows.
- [ ] **Step 2: Run** (expect build errors). **Step 3: Implement.** **Step 4: Run** — green. **Step 5: Stage** — the human commits.

### Task 3.8: `passkeys.js` and `Passkeys`

**Files:** Create `wwwroot/passkeys.js`, `Manage/Passkeys.razor`. Test `Manage/PasskeysTests.cs`.

**Interfaces:**
- **`passkeys.js`** (an ES module; no fetch):
  - `isSupported()`: `parseCreationOptionsFromJSON` and `parseRequestOptionsFromJSON` both exist.
  - `create(optionsJson)` and `request(optionsJson, mediation)` → `{ credentialJson, error }`.
  - Errors: `NotAllowedError` → "No passkey was provided by the authenticator."; `AbortError` → both null; otherwise `error.message`.
- **`Passkeys`** (`/Account/Manage/Passkeys`, `OutcomeComponentBase` + `IAsyncDisposable`):
  - It loads `GetPasskeys` and imports the module after the first render.
  - Unsupported or at the cap: shows the literal, and no Add.
  - Add: options → `create` (private `sealed record PasskeyCeremonyResult(string? CredentialJson, string? Error)`) → `AddPasskey` → soft `NavigateTo(NextUrl)`. `JSException` is caught narrowly.
  - Rows link to the server's `RenameUrl`. Delete → `RemovePasskey` → "Passkey deleted successfully." and a reload.

- [ ] **Step 1: Write the failing tests** (`JSInterop.SetupModule("./_content/Norse.AuthN.Components.FluentUI/passkeys.js")`):
  - `The_list_renders_each_passkey_with_its_server_rename_url_and_delete`;
  - `An_unsupported_browser_hides_add_and_says_why`;
  - `A_dismissed_prompt_shows_the_authenticator_message_and_calls_nothing`;
  - `An_aborted_ceremony_calls_nothing_and_shows_nothing`;
  - `A_completed_ceremony_adds_and_navigates_softly_to_the_server_url`;
  - `A_js_exception_renders_in_place`;
  - `Delete_removes_and_reloads_with_the_message`;
  - `At_the_cap_add_is_hidden`;
  - `A_static_prerender_makes_no_js_call`.

  Add the add and delete lock rows.
- [ ] **Step 2: Run** (expect a build error). **Step 3: Implement.** **Step 4: Run** — green. **Step 5: Stage** — the human commits.

### Task 3.9: `RenamePasskey` loader + form; policy and nav locks

**Files:** Create `Manage/RenamePasskey.razor`, `Manage/RenamePasskeyForm.razor`. Test `Manage/RenamePasskeyTests.cs`, `Manage/RenamePasskeyFormTests.cs`. Modify `Manage/ManagePolicyLockTests.cs:18`, `Manage/ManageNavMenuTests.cs`, `FormStaticRenderLockTests`, `ClickStaticRenderLockTests`.

**Interfaces:**
- **Page** `@page "/Account/Manage/RenamePasskey/{Id}"` (`string Id = ""`): it loads `GetPasskey`, renders `NotFound` in place, and pre-fills `<RenamePasskeyForm CredentialId CurrentName>`.
- **Form:** `RenamePasskeyForm : OutcomeFormComponentBase`, `FormName = "authn-rename-passkey"`, with `[EditorRequired]` `CredentialId`/`CurrentName = ""`. Success → soft `NavigateTo(NextUrl)`; `NotFound` → the summary.

- [ ] **Step 1: Write the failing tests:**
  - page: `The_current_name_is_loaded_and_prefilled`, `An_unknown_id_renders_not_found_and_no_form`;
  - form: `An_empty_name_never_reaches_the_service`, `A_201_character_name_never_reaches_the_service`, `Success_navigates_softly_to_the_server_url`, `A_missing_passkey_renders_in_the_summary`;
  - `ManagePolicyLockTests`: floor `7` → `15`, against the exact list (the seven Train 2 pages + `TwoFactorAuthentication`, `EnableAuthenticator`, `RecoveryCodesHandoff`, `ResetAuthenticator`, `Disable2fa`, `GenerateRecoveryCodes`, `Passkeys`, `RenamePasskey`);
  - `ManageNavMenuTests.Every_nav_link_resolves_to_a_routable_heimdall_page` (each `Href` equals a route template minus the leading `/`, case-insensitively).
- [ ] **Step 2: Run.** Expected: failures. **Step 3: Implement.** Add the form to `FormStaticRenderLockTests` and `RenamePasskey` to `LoaderOnly`. **Step 4: Run** — green.
- [ ] **Step 5: Stage** — the human commits.

### Task 3.10: `LoginWithPasskey`; docs; ship gate

**Files:** Create `src/AuthN.Components.FluentUI/LoginWithPasskey.razor`. Test `LoginWithPasskeyTests.cs`. Docs: `Heimdall/README.md`, `Heimdall/CLAUDE.md`, the `AuthN.Components.FluentUI.csproj` Description.

**Interfaces:** `@page "/Account/LoginWithPasskey"` under `GateLayout`, an `OutcomeComponentBase` + `IAsyncDisposable`.
- It reads `[SupplyParameterFromQuery(Name = "returnUrl")] string? ReturnUrl`.
- A "Remember me" checkbox and a "Use a passkey" button.
- The sign-in sequence: `GetPasskeyRequestOptions` → `request(optionsJson, "optional")` → `PasskeySignIn(new() { CredentialJson, AssertionState, RememberMe, ReturnUrl = ReturnUrl ?? "" })` → `Begin`.
- `InvalidCredentials` → "The passkey could not be used to sign in." (which also covers a replay or a missing UV flag).
- Links: `<SignInMethodLinks ReturnUrl="@ReturnUrl" ShowPassword="true" />`.

- [ ] **Step 1: Write the failing tests:**
  - `A_bare_render_performs_nothing`;
  - `A_completed_assertion_signs_in_and_begins_one_transition` (either query casing);
  - `A_dismissed_prompt_shows_the_message_and_calls_nothing`;
  - `A_failed_sign_in_renders_in_place`;
  - `An_unsupported_browser_hides_the_button`;
  - `The_page_hands_its_return_url_to_the_sign_in_links`.

  Add a lock row.
- [ ] **Step 2: Run** (expect a build error). **Step 3: Implement.** **Step 4: Run** `dotnet test Heimdall.slnx` — green.
- [ ] **Step 5: Docs.** The README/CLAUDE.md pair covers:
  - the three contracts and their policies;
  - the nine pages, `SignInMethodLinks` and `passkeys.js`;
  - `CanAct` beside `CanSubmit`;
  - `NotAuthorizedRedirect`;
  - Login's `returnUrl` and its server-built links (`:54` covers them, with no exception);
  - every continuation URL is the server's.

  Update the Description page list.
- [ ] **Step 6: Stage** — the human commits. Ship gate: Heimdall PR (consumes Asgard), tag, publish.

## Phase 4 — Himinbjörg

### Task 4.1: Views and the lossless key provisioning

**Files:**
- Modify `src/Identity.Outcomes/UserManagerOutcomeView.cs`, `src/Identity.Web.Server/NorseUserManager.cs`, `SignInManagerOutcomeView.cs`, `tests/Identity.Web.Server.Tests/OutcomeSurfaceEnforcementTests.cs:39-46`.
- Create `src/Identity.Outcomes/IAuthenticatorKeyProvisioning.cs`.
- Test: `UserManagerOutcomeViewTests`, `NorseUserManagerTests`, `SignInManagerOutcomeViewTests`, `OutcomeSurfaceEnforcementTests`.

**Interfaces:**
- **`IAuthenticatorKeyProvisioning`:** `Task<(IdentityResult Result, string Key)> ProvisionAuthenticatorKeyAsync(NorseUser user)`, implemented by `NorseUserManager`, following the `IRecoveryCodeReplacement` pattern.
  - An existing key → `(Success, key)` with no write.
  - Otherwise → `GenerateNewAuthenticatorKey()`, then store `SetAuthenticatorKeyAsync`, then `UpdateUserAsync`, with no stamp rotation.
- **`UserManagerOutcomeView`** (each `ValueTask<Outcome<…>>`):
  - `SetTwoFactorEnabledAsync(NorseUser, bool)`;
  - `ResetAuthenticatorKeyAsync(NorseUser)`;
  - `AddOrUpdatePasskeyAsync(NorseUser, UserPasskeyInfo)`;
  - `RemovePasskeyAsync(NorseUser, byte[] credentialId)`;
  - passwordless `CreateAsync(NorseUser)`;
  - `ProvisionAuthenticatorKeyAsync(NorseUser) → Outcome<string>`, which type-tests the interface and throws `InvalidOperationException` otherwise.
- **IL scan:** `_identityManagers` gains `typeof(IAuthenticatorKeyProvisioning).FullName!`.
- **`SignInManagerOutcomeView.ExternalLoginSignInAsync(string loginProvider, string providerKey, bool isPersistent, CancellationToken cancellationToken = default) → ValueTask<Outcome<SignInCompletion>>`:** it always passes `bypassTwoFactor: false`.

- [ ] **Step 1: Fork.** `git -C Himinbjorg switch -c feature/account-lifecycle-two-factor-passkeys --no-track origin/master`.
- [ ] **Step 2: Write the failing tests.**
  - Each new user-manager view: success, `Fault` by code, `Indeterminate`, `Erased`.
  - Provisioning: `An_indeterminate_provisioning_answers_Indeterminate`, `A_stock_manager_fails_loud`.
  - `NorseUserManagerTests`: `Provisioning_a_missing_key_does_not_rotate_the_stamp`, `Provisioning_an_existing_key_writes_nothing`.
  - `OutcomeSurfaceEnforcementTests`: `The_manager_set_names_every_norse_identity_seam`. Mutation check: a temporary raw `ProvisionAuthenticatorKeyAsync` call turns the scan (`:67`) red. Record it in the handoff and never stage it.
  - External view: success → `SignedIn`; a two-factor account → `RequiresTwoFactor`; a failure → `InvalidCredentials`; `It_never_bypasses_two_factor` (`Received(1).ExternalLoginSignInAsync(p, k, false, false)`).
- [ ] **Step 3: Run** (expect build errors). **Step 4: Implement.** **Step 5: Run** the classes and `OutcomeSurfaceEnforcementTests` — green.
- [ ] **Step 6: Stage** (`git -C Himinbjorg add -A`) — the human commits.

### Task 4.2: Three services; `AccountCaller`; `ManageUrls`

**Files:**
- Create:
  - `TwoFactor/TwoFactorService.cs` + seven commands;
  - `Passkeys/PasskeyService.cs` + six commands;
  - `SignInMethods/SignInMethodsService.cs` + three commands;
  - `Account/AccountCaller.cs`, `Account/ManageUrls.cs`.
- Modify the eight Train 2 `Account/*Handler.cs` and `ServiceCollectionExtensions.cs`.
- Test: `TwoFactor/TwoFactorServiceTests.cs`, `Passkeys/PasskeyServiceTests.cs`, `SignInMethods/SignInMethodsServiceTests.cs`, `Account/AccountCallerTests.cs`, `Account/ManageUrlsTests.cs`.

**Interfaces:**
- `sealed class AccountCaller(IPrincipalAccessor principalAccessor, UserManager<NorseUser> userManager)`: `ValueTask<Outcome<NorseUser>> ResolveAsync(CancellationToken cancellationToken = default)`, registered scoped.
- `static class ManageUrls`: `string Status(PathString pathBase, string route, string statusKey)` → `UriHelper.BuildRelative(pathBase, route, QueryString.Create("status", statusKey))`.
- Commands: `[Authorize(Policy = …)] sealed record XCommand(XRequest Request) : CommandRequest<XRequest, XResponse>(Request);` (parameterless: the `GetAccountStatusCommand` shape).
- Services: each method is `[Authorize]` → `sender.Send(…).AsTask()`.
- Handlers land in 4.4–4.8 and 4.10. `RegistrationCompositionTests` (`:47`) goes 21 → 25 → 28 → 32 → 34 → 35 → 37.

- [ ] **Step 1: Write the failing tests:**
  - per service: `Every_operation_carries_its_policy` and one dispatch fact per method;
  - `AccountCallerTests`: success, `NotFound`, `Erased`;
  - `ManageUrlsTests`: an empty `PathBase` and `/base`.
- [ ] **Step 2: Run** (expect build errors). **Step 3: Implement**, and migrate the eight handlers (T3-26). **Step 4: Run** `dotnet test tests/Identity.Web.Server.Tests` — green, with the Train 2 facts unchanged.
- [ ] **Step 5: Stage** — the human commits.

### Task 4.3: `returnUrl` on the server

**Files:**
- Create `ReturnUrls.cs`.
- Modify `TwoFactorChallenge.cs`, `NorseSignInManager.cs`, `SignInManagerOutcomeView.cs`, `LoginHandler.cs` (including the stale `IdentityComponentsEndpointRouteBuilderExtensions` comment), `TwoFactorSignInHandler.cs`, `RecoveryCodeSignInHandler.cs`.
- Test the matching `*Tests` classes, plus `ReturnUrlsTests` and `TwoFactorChallengeTests`.

**Interfaces:**
- **`ReturnUrls`:** `const int MaxLength = 2048`; `string Resolve(string? candidate, PathString pathBase)`. Hard-fail on ambiguity (Glitnir §2.6); nothing is normalized and accepted (T3-2, Buvy r6). In order:
  1. **Global:** reject when the candidate is empty or longer than `MaxLength`, or when it, or the whole candidate decoded once, contains a `char.IsControl` character (path, query or fragment).
  2. **Split** the original candidate at its first literal `?` or `#`, **before any decoding**: `path` and `suffix` (the `?`/`#` onward, possibly empty).
  3. **Path only:** let `decodedPath = Uri.UnescapeDataString(path)`, applied **once**, and never re-split at a decoded `?` or `#`. Reject when:
     - `path` does not start with `/`;
     - `path` or `decodedPath` contains `\` or `//`;
     - `path` or `decodedPath` contains `%2e`, `%2f` or `%5c` (ordinal, ignoring case), so double encoding (`%252e`) is caught after the one decode;
     - `decodedPath`, split only at `/`, has a segment equal to `.` or `..` (a decoded `?` or `#` stays inside its segment, so `/app/x%3F/../../outside` still shows its `..` segments).
  4. **Containment** runs on what survives: when `PathBase` is set, `path` must start with `PathBase` by segments.

  An accepted candidate is returned **unchanged**, `path + suffix`, the query and fragment verbatim. Anything else returns `UriHelper.BuildRelative(pathBase, "/")`.
- **`TwoFactorChallenge`:** gains `string ReturnUrl` as its last member, with payload v2 (T3-21).
- **`NorseSignInManager`:**
  - `public const string ReturnUrlItemName = "Norse.Identity.ReturnUrl";`. The mint takes the item and removes it; without it, the landing is the app root.
  - `TwoFactorChallengeSignInAsync`/`RecoveryCodeChallengeSignInAsync` → `Task<ChallengeSignInResult>`, with `public sealed record ChallengeSignInResult(SignInResult Result, string ReturnUrl)`.
- **Views:** the two challenge views → `ValueTask<Outcome<string>>` (the landing). The IL scan's return-type check learns `ChallengeSignInResult`.
- **`LoginHandler`:** resolves the landing, parks it immediately before `PasswordSignInAsync`, removes it in a `finally`, and completes to `deferredCompletion.TryBuildUrl(landing) ?? landing`. The two-factor and recovery handlers re-resolve the view's landing.

- [ ] **Step 1: Write the failing tests.** The theory `ReturnUrlsTests.Resolve_answers_the_candidate_only_when_it_is_local`:

  | Candidate | `PathBase` | Expected |
  |---|---|---|
  | `/Account/Manage?x=1` | (empty) | same |
  | null | (empty) | `/` |
  | `""` | (empty) | `/` |
  | `https://evil.example/` | (empty) | `/` |
  | `//evil.example` | (empty) | `/` |
  | `/\evil.example` | (empty) | `/` |
  | `javascript:alert(1)` | (empty) | `/` |
  | `/%2F%2Fevil.example` | (empty) | `/` |
  | `/a\u0001b` | (empty) | `/` |
  | `"/" + new string('a', 2048)` | (empty) | `/` |
  | `/app/Account/Manage` | `/app` | same |
  | `/Account/Manage` | `/app` | `/app/` |
  | `/app/../outside` (repro) | `/app` | `/app/` |
  | `/app/%2e%2e/outside` (repro) | `/app` | `/app/` |
  | `/app/a/../../outside` | `/app` | `/app/` |
  | `/app/%2E%2e/outside` (mixed case) | `/app` | `/app/` |
  | `/app/%252e%252e/outside` (double encoding) | `/app` | `/app/` |
  | `/app/./Account` | `/app` | `/app/` |
  | `/app/x%2Fy` | `/app` | `/app/` |
  | `/app/x%5cy` | `/app` | `/app/` |
  | `/app/x\y` | `/app` | `/app/` |
  | `/app//Account` | `/app` | `/app/` |
  | `/app/Account?next=//evil.example` | `/app` | same (query verbatim) |
  | `/app/Account?next=https://evil.example/` | `/app` | same |
  | `/app/Account?returnUrl=%2Fapp%2F..%2Foutside` | `/app` | same |
  | `/app/Account#https://evil.example/x/../y` | `/app` | same |
  | `/app/../outside?next=https://evil.example/` | `/app` | `/app/` |
  | `/app/x%3F/../../outside` | `/app` | `/app/` |
  | `/app/x%3f/..%2F..%2Foutside` | `/app` | `/app/` |
  | `/app/x%253F/../../outside` (double-encoded `?`) | `/app` | `/app/` |
  | `/app/x%23/../../outside` | `/app` | `/app/` |
  | `/app/x%2523/../../outside` (double-encoded `#`) | `/app` | `/app/` |
  | `/app/a\u0001b` (control, path) | `/app` | `/app/` |
  | `/app/Account?x=a\u0001b` (control, query) | `/app` | `/app/` |
  | `/app/Account#a\u0001b` (control, fragment) | `/app` | `/app/` |
  | `/app/Account?x=%01` (encoded control, query) | `/app` | `/app/` |
  | `/app/Account/Manage?x=1` | `/app` | same |

  Plus:
  - `ReturnUrlsTests.Every_landing_resolves_inside_the_path_base`: for every row with `PathBase = /app`, `System.Uri` resolution, `new Uri(new Uri("https://example.test/app/"), Resolve(…))`, keeps the origin and an `AbsolutePath` starting with `/app/`. `System.Uri` is not the browser's resolver; Task 6.2 Step 3 corroborates the same matrix with WHATWG `URL` in the browser.
  - `TwoFactorChallengeTests`: a v2 round trip with `|` inside the URL; v1 refused.
  - `NorseSignInManagerTests`: `A_local_return_url_survives_the_two_factor_challenge`, `A_mint_without_a_parked_url_lands_on_the_app_root`.
  - `LoginHandlerTests`: `A_local_return_url_is_the_landing`, `A_foreign_return_url_lands_on_the_app_root`, `The_parked_item_is_removed_after_sign_in`.
  - The two-factor and recovery handler facts: the landing comes from the view.
- [ ] **Step 2: Run** (expect failures). **Step 3: Implement.** **Step 4: Run** the classes, `OutcomeSurfaceEnforcementTests` and the project — green.
- [ ] **Step 5: Stage** — the human commits.

### Task 4.4: `GetAuthenticatorSetup`, `EnableAuthenticator`, `RevealRecoveryCodes`, `ResetAuthenticator`

**Files:**
- Create `AuthenticatorSetup.cs`, `AuthenticatorOptions.cs`, `RecoveryCodesHandoff.cs`, and four handlers under `TwoFactor/`.
- Modify `Identity.Web.Server.csproj` (`<PackageReference Include="QRCoder" Version="1.*" />`, alphabetical) and `ServiceCollectionExtensions.cs`.
- Test: `AuthenticatorSetupTests`, `RecoveryCodesHandoffTests`, one class per handler, and `TwoFactor/StampRefreshSuite.cs` + `PostgresStampRefreshTests.cs` + `SqlServerStampRefreshTests.cs` (real store, `CommitFailureInterceptor`).

**Interfaces:**
- **`AuthenticatorOptions`:** `required string Issuer`, `[Required, MinLength(1)]`, bound from `Identity:Authenticator`, `ValidateDataAnnotations` + `ValidateOnStart`.
- **`AuthenticatorSetup`:**
  - `FormatKey(string)`;
  - `Uri(string issuer, string email, string unformattedKey)`, the parity format, with `UrlEncoder.Default` on issuer and email;
  - `QrSvg(string uri)` (`SvgQRCode`, ECC Q).
- **`RecoveryCodesHandoff`:**
  - Purpose `"Norse.Identity.RecoveryCodesHandoff.v1"`, five minutes, payload `v1|userId|stamp|code;code;…`.
  - `Protect(ITimeLimitedDataProtector, userId, stamp, string[] codes)`.
  - `bool TryUnprotect(…, out userId, out stamp, out codes)`, false on `CryptographicException`, version or shape.
- **Handler decisions** (one line each; the tests below fix the rest):
  - Setup provisions without rotation (T3-13).
  - Enable strips spaces and hyphens. It verifies through `Options.Tokens.AuthenticatorTokenProvider`; a failure → `Validation` on `Code`, "Verification code is invalid.", with nothing written.
  - Enable generates ten codes only when none remain. It refreshes, then answers either the handoff URL (`/Account/Manage/ShowRecoveryCodes?handoff=…`, protected with the stamp read after the writes) or `authenticator-verified`.
  - Reveal refuses with "These recovery codes are no longer available." when any of these hold:
    - the token is bad;
    - the user differs;
    - the stamp differs;
    - the stored set differs from the token's codes (`GetAuthenticationTokenAsync(user, "[AspNetUserStore]", "RecoveryCodes")`, `NorseUserStore.cs:72,75`, ordinal against `string.Join(';', codes)`).
  - Reveal is **one-shot**. It consumes the SHA-256 of the unprotected handoff through `IPasskeyChallengeLedger.TryConsumeAsync` (4.9; the ledger is a nonce ledger, not passkey-specific). A second reveal → `Validation`, "These recovery codes have already been shown."
    - Order: 4.4 lands before 4.9, so the reveal handler takes the ledger interface, and 4.4 adds `IPasskeyChallengeLedger` with an in-memory registration that 4.9 replaces.
  - Reset: disable → reset key → refresh → `authenticator-reset` on EnableAuthenticator.
  - **T3-35:** once a rotating write succeeded or is indeterminate, a later failure or an indeterminate result **re-reads the user through `AccountCaller.ResolveAsync`**, refreshes from that instance, and answers `recovery-codes-failed` (Enable's code step) or the two-factor page with no key. A definite failure before any rotating write refreshes nothing.

- [ ] **Step 1: Write the failing tests.**
  - `AuthenticatorSetupTests`:
    - `FormatKey("ABCDEFGHIJKLMNOP")` → `"abcd efgh ijkl mnop"`;
    - `Uri("Norse", "a+b@example.com", "KEY")` → `otpauth://totp/Norse:a%2Bb%40example.com?secret=KEY&issuer=Norse&digits=6`;
    - `QrSvg` starts `<svg` and contains no `<script`;
    - `A_missing_issuer_fails_at_startup`.
  - `RecoveryCodesHandoffTests`: a round trip; tampered, expired, and another purpose → false.
  - Handlers:
    - `A_missing_key_is_provisioned_without_rotating_the_stamp`;
    - `An_existing_key_is_read_without_a_write`;
    - `An_invalid_code_answers_Validation_and_enables_nothing`;
    - `A_first_enable_refreshes_the_sign_in_and_hands_off_ten_codes`;
    - `An_enable_with_existing_codes_refreshes_and_answers_the_verified_url`;
    - `A_failed_code_replacement_after_enabling_refreshes_and_answers_the_codes_failed_url` (a theory over a fault and `Indeterminate`);
    - `An_indeterminate_enable_refreshes_and_answers_the_two_factor_page`;
    - `A_definite_enable_failure_refreshes_nothing`;
    - `A_handoff_for_another_user_answers_Validation`;
    - `A_handoff_after_a_stamp_rotation_answers_Validation`;
    - `A_handoff_whose_codes_were_replaced_answers_Validation`;
    - `A_handoff_after_a_redeemed_code_answers_Validation`;
    - **`A_second_reveal_of_the_same_handoff_answers_already_shown`**;
    - `Reset_disables_resets_refreshes_and_answers_the_status_url`;
    - `A_failed_disable_writes_no_key_and_refreshes_nothing`;
    - `A_failed_key_reset_after_disabling_still_refreshes`;
    - `An_erased_subject_answers_Erased` (each handler);
    - `Each_post_failure_refresh_uses_a_re_read_user` (the substitute's `RefreshSignInAsync` receives the instance `ResolveAsync` returned second, not the first).
  - `StampRefreshSuite` (both providers, real `NorseUserStore`, real managers):
    - `An_enable_whose_commit_lands_but_reports_indeterminate_refreshes_with_the_stored_stamp` (`CommitFailure.AfterCommit`);
    - `A_code_step_that_fails_definitely_after_enable_refreshes_with_the_stored_stamp` (`CommitFailure.BeforeCommit` on the second flush).

    In both, the refreshed principal's security-stamp claim equals the stamp read back from the database. The interceptor has no "indeterminate and not landed" mode, and the re-read makes the claim match the database in every case.
- [ ] **Step 2: Run** (expect build errors). **Step 3: Implement.** The count goes to 25.
- [ ] **Step 4: Run** the classes, the suite (Docker), `RegistrationCompositionTests` and `OutcomeSurfaceEnforcementTests` — green.
- [ ] **Step 5: Stage** — the human commits.

### Task 4.5: `DisableTwoFactor`, `GenerateRecoveryCodes`, `ForgetTwoFactorClient`

**Files:** Create the three handlers under `TwoFactor/`. Test: one class each, plus two disable cases in `StampRefreshSuite`. Count 25 → 28.

**Interfaces:**
- **Disable:**
  - already off → `Validation`, "Two-factor authentication is not enabled.", nothing written;
  - otherwise → disable → refresh → `two-factor-disabled`;
  - T3-35 applies (re-read on `Indeterminate`).
- **Generate:**
  - off → `Validation`, "Cannot generate recovery codes because two-factor authentication is not enabled.";
  - otherwise → ten codes, with no rotation and no refresh.
- **Forget:** `ForgetTwoFactorClientAsync()` → a deferred `browser-forgotten` URL.

- [ ] **Step 1: Write the failing tests:**
  - `Disabling_when_already_off_answers_Validation_and_writes_nothing`;
  - `Disabling_refreshes_the_sign_in_and_answers_the_status_url`;
  - `An_indeterminate_disable_refreshes_and_answers_the_two_factor_page`;
  - `A_definite_disable_failure_refreshes_nothing`;
  - `Generating_when_off_answers_Validation_and_writes_nothing`;
  - `Generating_returns_ten_codes_and_refreshes_nothing`;
  - `An_indeterminate_generation_answers_Indeterminate`;
  - `Forget_answers_the_status_url_through_the_deferral`;
  - `An_erased_subject_answers_Erased` (each handler);
  - the suite facts `A_successful_disable_rotates_the_stored_stamp` (the stored stamp differs from the pre-disable stamp, and the refreshed claim equals the new one) and `An_indeterminate_disable_refreshes_with_the_stored_stamp`.
- [ ] **Step 2: Run.** **Step 3: Implement.** **Step 4: Run** — green. **Step 5: Stage** — the human commits.

### Task 4.6: Passkey registration

**Files:**
- Modify `IdentityBuilderExtensions.cs` (passkey options), `NorseSignInManager.cs`, `SignInManagerOutcomeView.cs`, `OutcomeSurfaceEnforcementTests.cs`, `TwoFactorHarness.cs`, `MockSignInManager.cs`.
- Create `PasskeyCeremony.cs`, `Passkeys/CredentialIds.cs`, and four handlers under `Passkeys/`.
- Test: `CredentialIdsTests`, `NorseSignInManagerTests`, `SignInManagerOutcomeViewTests`, `IdentityBuilderExtensionsTests`, one class per handler. Count 28 → 32.

**Interfaces:**
- `sealed record PasskeyCeremony(string OptionsJson, string State);`.
- **Passkey options:** `IdentityPasskeyOptions.UserVerificationRequirement = "required"` (T3-33).
- **`NorseSignInManager`:**
  - Gains `IPasskeyHandler<NorseUser>` and `IOptions<IdentityPasskeyOptions>`.
  - Protector `Norse.Identity.PasskeyAttestation.v1`, lifetime `AuthenticatorTimeout`.
  - `CreatePasskeyOptionsAsync(NorseUser user, CancellationToken cancellationToken = default) → Task<PasskeyCeremony>`: a `PasskeyUserEntity` from `GetUserIdAsync` and `GetUserNameAsync`.
  - `AttestPasskeyAsync(string credentialJson, string attestationState, CancellationToken cancellationToken = default) → Task<PasskeyAttestationResult>`: a failed unprotect → `PasskeyAttestationResult.Fail(new PasskeyException("The passkey could not be verified. Try again."))`.
- **Views:**
  - `MakePasskeyCreationOptionsAsync(NorseUser) → Outcome<PasskeyCeremony>`.
  - `PerformPasskeyAttestationAsync(string, string) → Outcome<PasskeyAttestationResult>`: not succeeded → `Validation`, the same text.
  - Both type-test `NorseSignInManager`.
- **IL ban list:** `MakePasskeyCreationOptionsAsync`, `PerformPasskeyAttestationAsync`, `MakePasskeyRequestOptionsAsync`, `PerformPasskeyAssertionAsync`, `PasskeySignInAsync` on `SignInManager<>`.
- **`CredentialIds.TryDecode(string, out byte[])`:** `Base64Url.DecodeFromChars` inside `catch (FormatException)`. A malformed id → `Validation` on `CredentialId`, "The passkey id is not valid.".
- **Handlers:**
  - `GetPasskeys`: base64url ids, `Name ?? ""`, `RenameUrl = UriHelper.BuildRelative(PathBase, "/Account/Manage/RenamePasskey/" + id)`.
  - `GetPasskey`: `NotFound`, "The passkey was not found.".
  - `GetPasskeyCreationOptions`: at 100 → `Conflict` with the cap literal.
  - `AddPasskey`: cap → attestation → if `result.UserEntity.Id != GetUserIdAsync(user)`, `Forbidden` "This passkey was created for a different account." with nothing persisted → persist → the rename URL.

- [ ] **Step 1: Decompile** the installed `Microsoft.AspNetCore.App 11.0.0-rc.3` `Microsoft.AspNetCore.Identity.dll`. Record:
  - the four `IPasskeyHandler<TUser>` signatures;
  - the shapes of `PasskeyCreationOptionsResult`, `PasskeyRequestOptionsResult`, `PasskeyAttestationContext`, `PasskeyAssertionContext`, `PasskeyAttestationResult` (`Succeeded`, `Passkey`, `UserEntity`, `Failure`) and `PasskeyAssertionResult<TUser>`;
  - whether `UserPasskeyInfo` is a record, and whether `Name` and the sign count are settable;
  - `AuthenticatorTimeout`;
  - RP id and origin derivation, including under the circuit's `HttpContext`;
  - the base `PasskeySignInAsync` sequence, its `bypassTwoFactor` value (T3-31) and its sign-count update;
  - where the handler enforces UV;
  - what the assertion state holds, so the ledger hash is over stable bytes.

  A mismatch is a re-plan trigger.
- [ ] **Step 2: Write the failing tests.**
  - `NorseSignInManagerTests` (substituted handler):
    - `Creation_options_protect_the_attestation_state`;
    - `Attestation_unprotects_the_state_before_the_handler_sees_it`;
    - `A_tampered_state_fails_attestation_without_calling_the_handler`;
    - `An_expired_state_fails_attestation`;
    - `A_state_from_another_purpose_fails_attestation`.
  - Views: success; failure → `Validation`; a stock manager throws.
  - `IdentityBuilderExtensionsTests.User_verification_is_required_for_passkeys`: the option, and the real handler's creation JSON (over a `DefaultHttpContext` with `Request.Host`) carries `"userVerification":"required"`.
  - `AddPasskeyHandlerTests`:
    - `A_ceremony_started_for_another_account_is_forbidden_and_persists_nothing`;
    - `A_failed_attestation_persists_nothing`;
    - `Success_persists_and_answers_the_rename_url`;
    - `At_the_cap_answers_Conflict_before_attestation`.
  - The cap fact for creation options.
  - `GetPasskeysHandlerTests`: base64url, an empty name, a PathBase `RenameUrl`.
  - `GetPasskeyHandlerTests`: found, `NotFound`, malformed.
  - `CredentialIdsTests`: round trip; malformed.
  - The ban-list fact.
- [ ] **Step 3: Run.** **Step 4: Implement** (count 32). **Step 5: Run** `dotnet test tests/Identity.Web.Server.Tests` — green.
- [ ] **Step 6: Stage** — the human commits.

### Task 4.7: `RenamePasskey`, `RemovePasskey`

**Files:** Create the two handlers. Test: one class each. Count 32 → 34.

**Interfaces:**
- Rename: lookup → `NotFound`, or set the name (as 4.6 recorded) → `AddOrUpdatePasskeyAsync` → `ManageUrls.Status(PathBase, "/Account/Manage/Passkeys", ManageStatus.PasskeyUpdated)`.
- Remove: lookup → `NotFound`, or `RemovePasskeyAsync` → `BoolResponse { Value = true }`.

- [ ] **Step 1: Write the failing tests:**
  - `A_malformed_id_answers_Validation_and_writes_nothing`;
  - `A_removed_passkey_answers_NotFound_and_writes_nothing`;
  - `Rename_persists_the_new_name_and_answers_the_status_url`;
  - `Remove_removes`;
  - `An_erased_subject_answers_Erased`.
- [ ] **Step 2: Run.** **Step 3: Implement.** **Step 4: Run** — green. **Step 5: Stage** — the human commits.

### Task 4.8: External providers, the fail-closed GET starts, `GetSignInMethods`, endpoints pruned

**Files:**
- Create `ExternalProviders.cs`, `FetchMetadata.cs`, `SignInMethods/GetSignInMethodsHandler.cs`, `tests/Identity.Web.Server.Tests/ExternalLogin/FakeRemoteAuthenticationHandler.cs`.
- Rename `IdentityComponentsEndpointRouteBuilderExtensions.cs` → `ExternalLoginEndpointRouteBuilderExtensions.cs`.
- Modify `Identity.Web.Server.csproj` (`<PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="11.*-*" />`).
- Test: `ExternalProvidersTests`, `ExternalLoginStartEndpointTests`, `SignInMethods/GetSignInMethodsHandlerTests`, `Account/LinkExternalLoginEndpointTests`. Count 34 → 35.

**Interfaces:**
- **`ExternalProviders`:**
  - `AddNorseExternalProviders(this AuthenticationBuilder, IConfiguration) → AuthenticationBuilder`. For each child of `Identity:ExternalProviders` it calls `AddOpenIdConnect(name, displayName, …)` with:
    - `SignInScheme = IdentityConstants.ExternalScheme`;
    - `Authority`, `ClientId`, `ClientSecret`, `RequireHttpsMetadata` (default `true`);
    - `ResponseType = "code"`, `UsePkce = true`, scopes `openid profile email`;
    - `CallbackPath = "/signin-" + name.ToLowerInvariant()`;
    - `MapInboundClaims = false` plus `email` → `ClaimTypes.Email`;
    - `ConfigureRemoteFailure(options)`.

    It is the framework's **typed** `AddOpenIdConnect`, which registers `OpenIdConnectConfigureOptions` and `OpenIdConnectPostConfigureOptions` (the protected state format, backchannel and configuration manager) before `AddRemoteScheme`: [OpenIdConnectExtensions.cs](https://raw.githubusercontent.com/dotnet/aspnetcore/main/src/Security/Authentication/OpenIdConnect/src/OpenIdConnectExtensions.cs), [OpenIdConnectPostConfigureOptions.cs](https://raw.githubusercontent.com/dotnet/aspnetcore/main/src/Security/Authentication/OpenIdConnect/src/OpenIdConnectPostConfigureOptions.cs). No generic helper registers OIDC.

    Missing `Authority`/`ClientId` → `OptionsValidationException`. No section → no scheme. `DisplayName` is required (Ruling 41 keeps null-display schemes hidden).
  - `public static Task RedirectRemoteFailure(RemoteFailureContext context)`: redirect to `UriHelper.BuildRelative(PathBase, "/Account/ExternalLogin", QueryString.Create("RemoteError", "The sign-in provider did not complete the sign-in."))`, then `HandleResponse()`. It is the one production failure handler (T3-18), and the fake scheme uses it too.
  - `public static void ConfigureRemoteFailure(RemoteAuthenticationOptions options)`: sets `options.Events.OnRemoteFailure = RedirectRemoteFailure`. It is the only shared wiring. The OIDC registration calls it inside its configure delegate; the test fake is registered with the framework's `AddRemoteScheme<FakeRemoteOptions, FakeRemoteAuthenticationHandler>(name, displayName, o => { o.SignInScheme = IdentityConstants.ExternalScheme; ExternalProviders.ConfigureRemoteFailure(o); })`.
- **`FetchMetadata.IsSameOriginOrUserInitiated(HttpRequest request)`:** true only when `Sec-Fetch-Site` is `same-origin` or `none` (ordinal, ignoring case). An absent header or any other value → false (T3-34, fail closed).
- **`MapNorseExternalLoginEndpoints(this IEndpointRouteBuilder) → IEndpointConventionBuilder`** keeps `ExcludeFromDescription()` and maps only:
  - **`GET /Account/PerformExternalLogin?provider=&returnUrl=`:**
    1. `landing = ReturnUrls.Resolve(returnUrl, PathBase)`.
    2. A failed Fetch-Metadata check, an unknown scheme, or a null-display scheme → a redirect to `UriHelper.BuildRelative(PathBase, "/Account/Login", QueryString.Create("ReturnUrl", landing))`, with no challenge.
    3. Otherwise → `ConfigureExternalAuthenticationProperties(provider, callbackUrl)` → `Results.Challenge`, where `callbackUrl = UriHelper.BuildRelative(PathBase, "/Account/ExternalLogin", QueryString.Create([("Action", "LoginCallback"), ("ReturnUrl", landing)]))`.

    No antiforgery; `TemporaryFluentButtonFix` is deleted.
  - **`GET /Account/Manage/LinkExternalLogin`:** the same fail-closed check. A failure redirects to `ManageUrls.Status(PathBase, "/Account/Manage/ExternalLogins", ManageStatus.ExternalLoginFailed)`.
  - **`GET /Account/Manage/LinkExternalLoginCallback`:** keys per T3-6. `LoginAlreadyAssociated` → not-added. Unknown provider, unloadable caller, missing cookie, lookup failure and other definite failures → failed. `Indeterminate` → none.
  - **Gone:** `POST /Account/PerformExternalLogin`, `POST /Account/Logout`, `/Account/PasskeyCreationOptions`, `/Account/PasskeyRequestOptions`.
- **`GetSignInMethodsHandler`** (Public). With `landing` resolved and `q = QueryString.Create("returnUrl", landing)`:
  - `PasswordSignInUrl = BuildRelative(PathBase, "/Account/Login", q)`;
  - `PasskeySignInUrl = BuildRelative(PathBase, "/Account/LoginWithPasskey", q)`;
  - `Providers`: displayed schemes, ordered by display name, with `StartUrl = BuildRelative(PathBase, "/Account/PerformExternalLogin", QueryString.Create([("provider", name), ("returnUrl", landing)]))`.

  These are links, not continuations, so they are not deferral-wrapped.
- **`FakeRemoteAuthenticationHandler : RemoteAuthenticationHandler<FakeRemoteOptions>`** (test-only):
  - `FakeRemoteOptions.StateDataFormat` is a `PropertiesDataFormat` over the host's `IDataProtectionProvider` (purpose `"FakeRemote.State"`).
  - Its challenge writes the correlation cookie (`GenerateCorrelationId`) and redirects to `/fake-provider/authorize?state=<StateDataFormat.Protect(properties)>`.
  - A test endpoint there redirects to the scheme's `CallbackPath` with `state` and the `sub`/`email` the test chose.
  - `HandleRemoteAuthenticateAsync` unprotects `state`, runs `ValidateCorrelationId`, and returns a ticket carrying `ClaimTypes.NameIdentifier` and `ClaimTypes.Email`.

- [ ] **Step 1: Write the failing tests.**
  - `ExternalProvidersTests` (built `ServiceProvider`):
    - `No_section_registers_no_scheme`;
    - `A_section_registers_an_oidc_scheme_with_its_display_name`;
    - `The_scheme_signs_into_the_external_cookie_with_pkce`;
    - `A_provider_without_an_authority_fails_at_startup`;
    - `Every_configured_scheme_uses_the_shared_remote_failure_handler` (`Events.OnRemoteFailure` is `ExternalProviders.RedirectRemoteFailure`);
    - `The_remote_failure_handler_redirects_to_the_callback_page_with_the_fixed_message` (`Location` is PathBase-qualified; `HandleResponse` was called).
  - `ExternalLoginStartEndpointTests` (TestServer, real `SignInManager` composition, the fake registered through `AddRemoteScheme` + `ConfigureRemoteFailure`, a cookie jar driven by hand):
    - `A_same_origin_start_challenges_the_provider_with_the_resolved_landing` (protected properties carry `LoginProvider`, and `RedirectUri`'s `ReturnUrl` is the landing);
    - `A_typed_url_start_challenges` (`none`);
    - **`A_configured_oidc_scheme_challenges_to_the_authorization_endpoint_with_state_and_nonce`**: the production `AddNorseExternalProviders` with an `Identity:ExternalProviders:Example` section (`Authority = https://provider.test`), and `services.Configure<OpenIdConnectOptions>("Example", o => o.BackchannelHttpHandler = stub)` where the stub serves a static `/.well-known/openid-configuration` (issuer, `authorization_endpoint = https://provider.test/authorize`, token and jwks endpoints). A same-origin GET start → 302 to `https://provider.test/authorize` carrying `client_id`, `response_type=code`, `code_challenge_method=S256`, `state`, `nonce` and a `redirect_uri` ending `/signin-example`, with `.AspNetCore.Correlation.` and `.AspNetCore.OpenIdConnect.Nonce.` cookies set. No container, no browser;
    - refusals, one per value, each a 302 to exactly `/Account/Login?ReturnUrl=…`, with no correlation cookie and the provider never reached:
      - `A_cross_site_start_challenges_nothing`;
      - `A_same_site_start_challenges_nothing`;
      - `A_start_without_fetch_metadata_challenges_nothing`;
    - `A_foreign_return_url_starts_with_the_app_root_landing`;
    - `An_unknown_provider_returns_to_login_and_challenges_nothing`;
    - `The_start_needs_no_antiforgery_token`;
    - `No_post_start_logout_or_passkey_endpoint_remains` (404/405).
  - `GetSignInMethodsHandlerTests`:
    - `Every_url_carries_only_the_resolved_landing` (a theory: local, foreign, empty, PathBase `/app`, and `/app/../outside` under `/app` → every URL carries `returnUrl=%2Fapp%2F`);
    - `Each_displayed_scheme_is_a_provider_link_and_hidden_schemes_are_not`;
    - `No_schemes_answers_an_empty_array`.
  - `LinkExternalLoginEndpointTests`:
    - repoint to `MapNorseExternalLoginEndpoints()`;
    - flip `:80`, `:150` and `:165` to the failed key, and keep `:119` as not-added;
    - add the link refusals, one per value: `A_cross_site_link_start_challenges_nothing`, `A_same_site_link_start_challenges_nothing`, `A_link_start_without_fetch_metadata_challenges_nothing`;
    - add `A_same_origin_link_start_challenges`.
- [ ] **Step 2: Run** (expect failures). **Step 3: Implement** (count 35). **Step 4: Run** the classes, `OutcomeSurfaceEnforcementTests` and `RegistrationCompositionTests` — green.
- [ ] **Step 5: Stage** — the human commits.

### Task 4.9: The consumed-challenge ledger (schema)

**Files:**
- Create `src/Identity.EntityFramework/ConsumedPasskeyChallenge.cs`, `src/Identity.EntityFramework/VerificationCursor.cs` (the table 4.12 uses, created here so `InitialCreate` is squashed once), `src/Identity.Web.Server/PasskeyChallengeLedger.cs`.
- Modify `src/Identity.EntityFramework/NorseIdentityDbContext.cs`, `src/Identity.Web.Server/Store/ProviderStatements.cs`, the identity registration (replacing 4.4's in-memory registration), and `tests/Identity.EntityFramework.Tests/NorseIdentityTemporalModelTests.cs:66-81`.
- Squash: run `dotnet tool restore`, delete both providers' `Migrations/`, then `dotnet ef migrations add InitialCreate --project src/Identity.Migrations.<Provider> --startup-project src/Identity.Migrations.<Provider>` (`Himinbjorg/CLAUDE.md:43`). `schema/norse_identity.sql` regenerates.
- Test (in `tests/Identity.Web.Server.Tests/`, beside `LastSignInMethodSuite`): `ConsumedPasskeyChallengeSuite.cs` + `PostgresConsumedPasskeyChallengeTests.cs` + `SqlServerConsumedPasskeyChallengeTests.cs` on the existing fixtures and test groups, plus `PasskeyChallengeLedgerTests.cs`.

**Interfaces (T3-32):**
- **`public sealed record ConsumedPasskeyChallenge : NorseEntityBase<ConsumedPasskeyChallenge>, INorseEntity<ConsumedPasskeyChallenge>`** (the `SubjectKey.cs:14` shape):
  - `byte[] ChallengeHash` (key, 32 bytes) and `DateTimeOffset ExpiresAt` (indexed);
  - table `consumed_passkey_challenges`;
  - non-temporal: a nonce ledger, pruned, never history.
- **`interface IPasskeyChallengeLedger`:** `ValueTask<bool> TryConsumeAsync(ReadOnlyMemory<byte> stateBytes, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)`. It is used by passkey sign-in (4.10) and by the handoff reveal (4.4).
- **The consume** is one set-based statement in `ProviderStatements`, picked by `Database.ProviderName` (as `CommitBoundary.cs:59` does). It returns rows affected, and 0 means a replay:
  - PostgreSQL: `INSERT … ON CONFLICT (challenge_hash) DO NOTHING`.
  - SQL Server: `INSERT … SELECT … WHERE NOT EXISTS (SELECT 1 … WITH (UPDLOCK, HOLDLOCK) WHERE challenge_hash = @p0)`.

  Nothing is tracked, no exception is classified, and no SqlClient type is referenced.
- **`public sealed record VerificationCursor : NorseEntityBase<VerificationCursor>, INorseEntity<VerificationCursor>`**: `string Name` (key, max 64), `Guid? LastId` (null = start of the traversal), `long Version` (the compare-and-set token, starts at 0), `DateTimeOffset UpdatedAt`; table `verification_cursors`; non-temporal (runtime position, never history). The prune before it is `ExecuteDeleteAsync(r => r.ExpiresAt < now)` (`TimeProvider`). The ledger is registered scoped.

- [ ] **Step 1: Write the failing tests.**
  - Suite (both providers):
    - `The_first_consumption_succeeds`;
    - `A_second_consumption_of_the_same_state_fails`;
    - `Expired_rows_are_pruned_on_the_next_consumption`;
    - `Two_concurrent_consumptions_of_one_state_succeed_once` (two contexts, `Task.WhenAll`);
    - `A_replay_leaves_the_context_clean` (after a refused consume, `ChangeTracker.Entries()` is empty and a following store write on the same context succeeds);
    - `The_tables_exist_with_their_keys` (both tables; catalog check through `pg_class`/`sys.tables`, never a migration name).
  - `NorseIdentityTemporalModelTests`: `ConsumedPasskeyChallenge` and `VerificationCursor` rows in `Secret_stores_counters_and_prunable_runtime_state_stay_non_temporal`.
  - `PasskeyChallengeLedgerTests`: different states never share a hash; registered scoped.
- [ ] **Step 2: Run** (expect build errors). **Step 3: Implement**, then squash.
- [ ] **Step 4: Run** the suite classes plus `tests/Identity.EntityFramework.Tests`, `tests/Identity.Migrations.PostgreSQL.Tests`, `tests/Identity.Migrations.SqlServer.Tests` and `tests/Identity.Migrations.Tests` (Docker) — green.
- [ ] **Step 5: Stage** — the human commits. The handoff says to wipe the local Postgres volumes.

### Task 4.10: Passkey sign-in

**Files:**
- Modify `NorseSignInManager.cs`, `SignInManagerOutcomeView.cs`, `TwoFactorHarness.cs`, `MockSignInManager.cs` (the ledger constructor argument).
- Create `SignInMethods/GetPasskeyRequestOptionsHandler.cs`, `SignInMethods/PasskeySignInHandler.cs`, `tests/Identity.Web.Server.Tests/InMemoryPasskeyChallengeLedger.cs`.
- Test: `NorseSignInManagerTests`, `SignInManagerOutcomeViewTests`, `SignInMethods/PasskeySignInHandlerTests`, and `PasskeySignInWritePathSuite.cs` + `PostgresPasskeySignInWritePathTests.cs` (in `Identity.Web.Server.Tests`). Count 35 → 37.

**Interfaces:**
- **`NorseSignInManager`:**
  - Gains `IPasskeyChallengeLedger` and protector `Norse.Identity.PasskeyAssertion.v1` (`AuthenticatorTimeout`).
  - `CreatePasskeyRequestOptionsAsync(CancellationToken cancellationToken = default) → Task<PasskeyCeremony>` (`MakeRequestOptionsAsync(user: null, Context)`).
  - `PasskeyAssertionSignInAsync(string credentialJson, string assertionState, bool isPersistent, CancellationToken cancellationToken = default) → Task<SignInResult>` reproduces the recorded base sequence, with these decisions:
    - **the ledger is consumed after unprotecting and before `PerformAssertionAsync`**, so a replay never reaches the handler or the sign-count write, and a failed first assertion still burns its challenge;
    - a failed sign-count update → `Failed`;
    - `SignInOrTwoFactorAsync(…, bypassTwoFactor: <recorded>)`;
    - the landing parks through `ReturnUrlItemName`.
- **Views:** `MakePasskeyRequestOptionsAsync() → ValueTask<Outcome<PasskeyCeremony>>`; `PasskeyAssertionSignInAsync(…) → ValueTask<Outcome<SignInCompletion>>`.
- **Handlers:**
  - `GetPasskeyRequestOptionsHandler` → `PasskeyRequestOptionsResponse`.
  - `PasskeySignInHandler`:
    1. Resolve and park the landing.
    2. Call the view.
    3. `SignInRefusal.Collapse` with a static `_invalidPasskey` ("The passkey could not be used to sign in.").
    4. `RequiresTwoFactor` → the challenge URL.
    5. Success → the deferred landing.

- [ ] **Step 1: Write the failing tests.**
  - `NorseSignInManagerTests`, with a substituted handler and `InMemoryPasskeyChallengeLedger`, a test double that refuses a repeated hash (`TwoFactorHarness` has no database, `:52`; 4.9 proves the real ledger):
    - `Request_options_protect_the_assertion_state`;
    - `A_tampered_assertion_state_fails_without_calling_the_handler`;
    - `An_attestation_state_cannot_complete_an_assertion`;
    - `A_successful_assertion_signs_in_and_updates_the_sign_count`;
    - **`A_replayed_assertion_signs_in_nothing`** (the same pair twice: the first `Succeeded`, the second `Failed`, the handler called once);
    - `A_failed_first_assertion_still_burns_its_challenge`;
    - `A_locked_out_user_cannot_sign_in_with_a_passkey`;
    - **`A_two_factor_account_follows_the_recorded_bypass`** (`false` → `RequiresTwoFactor` and a minted token; `true` → `Succeeded`);
    - `Request_options_require_user_verification` (the real handler's JSON carries `"userVerification":"required"`).
  - `PasskeySignInHandlerTests`:
    - `A_local_return_url_is_the_landing`;
    - `A_foreign_return_url_lands_on_the_app_root`;
    - `A_failed_assertion_answers_the_static_instance`;
    - `A_locked_out_account_answers_the_locked_out_message`;
    - `A_two_factor_requirement_answers_the_challenge_url`, or `A_two_factor_account_signs_in_without_the_challenge`, per the recorded value (the choice is recorded).
  - **`A_passkey_sign_in_writes_no_users_history_row`** (Postgres, real store; the 10-25 explicit-writes invariant): the sign count updates and the `users_history` count is unchanged.
- [ ] **Step 2: Run.** **Step 3: Implement** (count 37). **Step 4: Run** `dotnet test tests/Identity.Web.Server.Tests` (Docker) — green.
- [ ] **Step 5: Stage** — the human commits.

### Task 4.11: The external-login callback, proven over the real start leg

**Files:**
- Create `ExternalLogin/ExternalLoginCallback.razor`, `ExternalLogin/_Imports.razor`, `tests/Identity.Web.Server.Tests/ExternalLogin/CallbackTestRoot.razor`.
- Modify `NorseSignInManager.cs:47-50` (the comment) and `Identity.Web.Server.Tests.csproj` (Razor, if needed).
- Delete `Components/Pages/ExternalLogin.razor`.
- Test: `ExternalLogin/ExternalLoginCallbackSuite.cs` + `PostgresExternalLoginCallbackTests.cs`, `ScaffoldWritePathSuite`.

**Interfaces:** `@page "/Account/ExternalLogin"`, `[ExcludeFromInteractiveRouting]`, static SSR, `[CascadingParameter] HttpContext`, keeping `LoginCallbackAction = "LoginCallback"`.
- **`RemoteError`:** renders the fixed text in place, with a link to `Account/Login`.
- **Login branch:** `GetExternalLoginInfoAsync` (missing → error in place) → park `ReturnUrls.Resolve(ReturnUrl, PathBase)` → the `ExternalLoginSignInAsync` view. Then:
  - `SignedIn` → the deferred landing;
  - `RequiresTwoFactor` → **sign out `IdentityConstants.ExternalScheme`**, then `/Account/LoginWith2fa?challenge=<token>` (the token carries the provider and the landing);
  - `LockedOut` → `/Account/Lockout`;
  - `NotAllowed` → the `SignInRefusal` wording in place;
  - no account → the registration form.
- **Registration branch:** `EmailAddress` parse (`RegisterHandler.cs:23-32`) → `new NorseUser { UserName = email.WireValue, Email = email.WireValue }` → `CreateAsync` → `AddLoginAsync` → the confirmation link (`IEmailSender<NorseUser>` + `AccountLinks.ConfirmEmail`) → `SignInAsync` → the landing. **A failed `AddLoginAsync` after a create erases the new user through `ErasureService`.**
- **`NorseSignInManager.cs:47-50`:** external login requires the second factor, and the callback clears the external cookie before the challenge; the completion's circuit-skipped sign-out is a backstop.

- [ ] **Step 1: Write the failing tests.**
  - **Harness:** a `WebApplication.CreateSlimBuilder()` + `TestServer` host with:
    - `AddRazorComponents()`, `UseAntiforgery()`, `MapRazorComponents<CallbackTestRoot>().AddAdditionalAssemblies(typeof(ExternalLoginCallback).Assembly)` and `MapNorseExternalLoginEndpoints()`;
    - the real managers over the real `NorseUserStore` on the `PostgresIdentityFixture` (the registration branch persists and erases; `TwoFactorHarness` substitutes the manager);
    - the fake scheme from 4.8 registered through `AddRemoteScheme` + `ConfigureRemoteFailure`, so its failure handler is the production member;
    - every request sends `Sec-Fetch-Site: same-origin`, and a hand-driven cookie jar.

    Each fact drives start → fake provider → `CallbackPath` → `/Account/ExternalLogin`, so `Items["LoginProvider"]` comes from `ConfigureExternalAuthenticationProperties`. No fact seeds the external cookie.
  - **Facts:**
    - `A_known_login_signs_in_and_lands_on_the_resolved_return_url`;
    - `A_foreign_return_url_lands_on_the_app_root`;
    - `An_external_sign_in_for_a_two_factor_account_requires_the_challenge`: a 302 to `/Account/LoginWith2fa?challenge=…`, the external cookie deleted, no identity cookie; redeeming with a TOTP from the seeded key yields the landing and the provider claim;
    - `An_external_sign_in_for_a_locked_out_account_redirects_to_lockout`;
    - `A_missing_external_cookie_renders_an_error_in_place`;
    - **`A_callback_without_the_browsers_correlation_cookie_signs_in_nothing`**: start and provider in jar A, then the callback URL replayed from an empty jar B → a 302 from the production `RedirectRemoteFailure` to the `RemoteError` page, no external or identity cookie;
    - `A_provider_failure_renders_the_fixed_remote_error`;
    - `Registration_creates_a_passwordless_user_adds_the_login_and_sends_a_confirmation_link`;
    - `A_malformed_email_renders_in_place_and_creates_nothing`;
    - `A_login_add_failure_after_create_leaves_no_user`: render the registration form, then seed the same `(provider, key)` on another user, then post with the antiforgery token → `LoginAlreadyAssociated` after the create → the new user is erased.
- [ ] **Step 2: Run.** **Step 3: Implement**, deleting `Components/Pages/ExternalLogin.razor` in this task. **Step 4: Run** the suite, `ScaffoldWritePathSuite` (both providers) and `OutcomeSurfaceEnforcementTests` — green.
- [ ] **Step 5: Stage** — the human commits.

### Task 4.12: The stranded-account verification service

**Files:**
- Create in `src/Identity.Web.Server/Verification/`: `IdentityVerificationOptions.cs`, `IStrandedAccountsQuery.cs`, `StrandedAccountsQuery.cs`, `StrandedPage.cs`, `IdentityInvariantVerificationService.cs`, `ServiceCollectionExtensions.cs`.
- Modify `src/Identity.Web.Server/Store/ProviderStatements.cs` (three statements, both dialects).
- Test in `tests/Identity.Web.Server.Tests/`: `StrandedAccountsSuite.cs` + `PostgresStrandedAccountsTests.cs` + `SqlServerStrandedAccountsTests.cs` (existing fixtures and test groups), and `Verification/IdentityInvariantVerificationServiceTests.cs`.

**Interfaces (T3-28; Glitnir §2.2, Buvy r5: every account is verified, not sampled):**
- **Cursor:** the `verification_cursors` row named `"stranded-accounts"` (4.9's `VerificationCursor`). It lives in `norse_identity` beside the accounts it walks and is written only through `ProviderStatements`, the store's explicit-statement law (no tracker).
- **`ProviderStatements`** (dialect picked by `Database.ProviderName`, tables and columns from the model as the existing statements do):
  - `ReadStrandedPageAsync(NorseIdentityDbContext context, Guid? after, int take, CancellationToken cancellationToken = default) → Task<(Guid Id, bool Stranded)[]>`: `users` left-joined to its `user_credentials` fragment, `WHERE (@after IS NULL OR id > @after) ORDER BY id`, `LIMIT`/`TOP (@take)`; `Stranded` is `password_hash IS NULL AND NOT EXISTS (user_logins for the id)`. The comparison and the order are the database's own, so the keyset is consistent per provider (SQL Server's `uniqueidentifier` order differs from PostgreSQL's, and neither matters).
  - `ReadStillStrandedAsync(NorseIdentityDbContext context, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) → Task<Guid[]>`: the same predicate over exactly those ids.
  - `EnsureVerificationCursorAsync(NorseIdentityDbContext context, string name, DateTimeOffset now, CancellationToken cancellationToken = default) → Task<int>`: insert `(name, last_id NULL, version 0)` if absent. PostgreSQL `INSERT … ON CONFLICT (name) DO NOTHING`; SQL Server `INSERT … SELECT … WHERE NOT EXISTS (SELECT 1 … WITH (UPDLOCK, HOLDLOCK) WHERE name = @name)`. Two concurrent first runs create exactly one row.
  - `ReadVerificationCursorAsync(NorseIdentityDbContext context, string name, CancellationToken cancellationToken = default) → Task<(Guid? LastId, long Version)>`.
  - `AdvanceVerificationCursorAsync(NorseIdentityDbContext context, string name, Guid? next, long read, DateTimeOffset now, CancellationToken cancellationToken = default) → Task<int>`: `UPDATE verification_cursors SET last_id = @next, version = version + 1, updated_at = @now WHERE name = @name AND version = @read`. One row means this run advanced; zero rows means another replica advanced first, and this run's advance is discarded.
- **`sealed record StrandedPage(Guid[] Candidates, Guid? NextCursor, bool Wrapped, bool Advanced)`.**
- **`IStrandedAccountsQuery`** (scoped, `StrandedAccountsQuery` over the host's `NorseIdentityDbContext`):
  - `NextPageAsync(int take, CancellationToken cancellationToken = default) → ValueTask<StrandedPage>`: ensure the row, read `(LastId, Version)`, then `ReadStrandedPageAsync(after: LastId, take: take + 1)`. More than `take` rows → process the first `take`, `next` = the `take`-th id. At most `take` rows → **this is the last page**: process them all and wrap, `next = null`, in the same write. Then `AdvanceVerificationCursorAsync(next, read: Version)`. The page's candidates are returned whether or not the advance won: an observation is valid either way, and the recheck and the report are idempotent (a recheck re-reads current state; reporting a confirmed id twice repeats a true Critical line, never a false one).
  - `StillStrandedAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) → ValueTask<Guid[]>`.
- **`IdentityVerificationOptions`:** `bool Enabled = true`, `TimeSpan Interval = 1 hour`, `int SampleSize = 100` (`[Range(1, 10_000)]`, `ValidateOnStart`). `SampleSize` is the page size, not a sample.
- **`IdentityInvariantVerificationService : BackgroundService`:**
  - Dependencies: `IServiceScopeFactory`, the options, a logger, `IMeterFactory`, `TimeProvider`.
  - `Enabled = false` → returns at once after one Information line.
  - Otherwise it runs at startup, then on a `PeriodicTimer(Interval, timeProvider)`, one scope per run. Each run:
    1. `confirmed = StillStrandedAsync(pending)` (the previous run's candidates; the recheck is the second sighting);
    2. `page = NextPageAsync(SampleSize)`; `pending = page.Candidates` minus `confirmed`.
  - A non-empty `confirmed` → `[LoggerMessage(Level = LogLevel.Critical)]` "Identity invariant violated: {Count} account(s) have no password and no external login: {UserIds}".
  - **Bound:** the cursor advances monotonically within a traversal (compare-and-set), so for a **stable account population** every account is visited within ceil(N / SampleSize) **successful advances**, across however many replicas run, and a stranded account is reported on the observing replica's next successful run. Concurrent inserts, deletions, failures and restarts rule out an unconditional fixed-run guarantee, and the plan claims none. "No empty page" means no extra wrap-only page for a stable, nonempty population; an empty database legitimately returns none. A transient account (the `CreateAsync`→`AddLoginAsync` window) that gains a login before its recheck is never reported.
  - The gauge `norse.identity.stranded_accounts` (meter `Norse.Identity`) reports the confirmed count; the run logs the page position at Debug.
  - A query exception → `LogError`; `pending` is kept, the cursor is not advanced, and the host never stops. A lost compare-and-set logs at Debug only.
- **`AddNorseIdentityInvariantVerification(this IServiceCollection, IConfiguration)`:** registers the options (`Identity:Verification`), the query, `TryAddSingleton(TimeProvider.System)` and the hosted service.

- [ ] **Step 1: Write the failing tests.**
  - Suite (both providers; the test group serializes it; it asserts only on ids it seeded, `LastSignInMethodSuite.cs:200-203`; each fact first deletes the `"stranded-accounts"` cursor row by raw SQL):
    - `A_passwordless_account_without_logins_is_a_candidate` (seeded by raw SQL);
    - `An_account_with_a_password_is_not_a_candidate`;
    - `An_account_with_a_login_is_not_a_candidate`;
    - `Pages_follow_the_id_order_and_advance_the_cursor` (`take = 2`: consecutive pages are disjoint and ascending in the provider's order; the stored `LastId` is each page's last id, and `Version` rises by one per advance);
    - **`Every_violating_account_is_reported_within_the_bound`**: `SampleSize = 3`; seed seven stranded accounts and three clean ones; count `N` users; drive the real service (fake `TimeProvider`, `FakeLogger`) for ceil(N / 3) + 1 runs; every seeded stranded id appears in a Critical line, and no clean id does;
    - **`The_cursor_wraps_to_the_start`**: seed a stranded account with id `00000000-0000-0000-0000-000000000001` (first in both providers' order) and set the cursor to the second-greatest stored id; the first page holds one row (fewer than `SampleSize + 1`), so it is the last page: `Wrapped` is true and the stored `LastId` is null in that same run; the second page's candidates include the seeded id;
    - **`An_exact_multiple_of_the_page_size_never_reads_an_empty_page`** (a theory, `SampleSize = 3`, on a fresh database created on the fixture's container for the fact (`CREATE DATABASE`, then `Database.MigrateAsync()`, dropped after), so N is exactly the seeded count: N = 6 → 2 runs, N = 3 → 1 run): every account is visited within N / 3 runs, the last of them reports `Wrapped`, and no run returns zero rows;
    - `A_non_multiple_visits_every_account_within_the_ceiling` (the same fresh-database shape, N = 7, `SampleSize = 3` → 3 runs, the third reporting `Wrapped`);
    - **`Interleaved_replicas_never_move_the_cursor_backward`**: query A reads `(LastId, Version = v)` and stalls before its advance; query B completes two runs (version v + 2); A's compare-and-set returns zero rows, A reports `Advanced = false`, and the stored `LastId` stays B's;
    - **`Two_concurrent_first_runs_create_exactly_one_cursor`** (no row; two contexts, `Task.WhenAll` on `NextPageAsync`; exactly one cursor row exists. Both serial outcomes are valid, A advances to 1 then B to 2 or one advance loses, so this fact asserts nothing about `Version` or the advance count). Its sibling **`Two_replicas_that_read_the_same_version_advance_exactly_once`** holds both replicas on a barrier after each has read the same `(LastId, Version = v)`, then releases them: `Version` is v + 1 and exactly one run reports `Advanced = true`;
    - `The_cursor_survives_a_restart` (a new query instance resumes after the stored `LastId`);
    - `A_still_stranded_recheck_drops_an_account_that_gained_a_login`.
  - Service (fake `TimeProvider`, `FakeLogger`, a substituted query):
    - `A_candidate_is_not_reported_before_its_recheck`;
    - `A_candidate_still_stranded_on_the_next_run_logs_critical_with_the_count_and_id`;
    - `A_candidate_that_gains_a_login_before_its_recheck_is_never_reported`;
    - `A_clean_run_logs_nothing_above_information`;
    - `A_query_failure_logs_an_error_keeps_the_pending_set_and_keeps_running`;
    - `It_runs_at_startup_and_on_each_interval`;
    - `The_gauge_reports_the_confirmed_count`;
    - `A_disabled_service_queries_nothing`.
- [ ] **Step 2: Run.** **Step 3: Implement.** **Step 4: Run** the suite (Docker) and the service class — green.
- [ ] **Step 5: Stage** — the human commits.

### Task 4.13: Retire `Components/`; IL exemption removed; coverage ratchet; docs; ship gate

**Files:**
- Delete the rest of `src/Identity.Web.Server/Components/`, `IdentityRedirectManager.cs`, `PasskeyInputModel.cs`, `PasskeyOperation.cs`.
- Modify `OutcomeSurfaceEnforcementTests.cs:37,92`, `ScaffoldRetirementTests.cs`, `.github/workflows/ci.yml:16`, `README.md`, `CLAUDE.md`, `src/Identity.Web.Server/README.md`.

- [ ] **Step 1: Write the failing tests.**
  - `ScaffoldRetirementTests`:
    - retire `/Account/Manage/TwoFactorAuthentication`, `/EnableAuthenticator`, `/ResetAuthenticator`, `/Disable2fa`, `/GenerateRecoveryCodes`, `/Passkeys`, `/RenamePasskey/{Id}`;
    - add `The_only_routable_component_is_the_external_login_callback`;
    - delete `RedirectToInvalidUser_sends_the_stale_session_to_heimdalls_logout_page`.
  - Remove `ComponentsNamespace` and its branch from `OutcomeSurfaceEnforcementTests`.
- [ ] **Step 2: Run** (seven route failures). **Step 3: Delete.** **Step 4: Run** `dotnet build Himinbjorg.slnx` and `dotnet test Himinbjorg.slnx` (Docker) — green.
- [ ] **Step 5: Measure coverage** (T3-22):
  1. Write `coverage-settings.xml` exactly as `../Ginnungagap/.github/workflows/ci-build-test.yml` does, into scratch.
  2. `dotnet test -c Release --coverage --coverage-output-format cobertura --coverage-settings <file>`.
  3. `reportgenerator … -reporttypes:JsonSummary`.
  4. Set `minimum_coverage` to `summary.branchcoverage` rounded down to 5, and record the measurement.
  5. Remove any `TestResults/` left behind.
- [ ] **Step 6: Docs** (keep the pair in sync):
  - the three services;
  - the HTTP surface is §5.7 as amended (A9);
  - `Components/` is gone, and so is the "until #48" line;
  - Architecture Facts: the passkey ceremonies (UV, the one-shot ledger, caller binding), the one-shot handoff, `ReturnUrls.Resolve`, external 2FA, the fail-closed starts, `GetSignInMethods`, `AddNorseExternalProviders`, the verification service (web host, keyset traversal with a wrapping `verification_cursors` cursor, a recheck before any report), the configurable issuer;
  - add `consumed_passkey_challenges` (a nonce ledger, pruned, never history) and `verification_cursors` (a runtime traversal position, never history) to `Himinbjorg/CLAUDE.md`'s "Non-temporal, and this is a ruling" list.
- [ ] **Step 7: Stage** — the human commits. Ship gate: Himinbjörg PR (consumes Heimdall), tag, publish. The PR description says to wipe the local Postgres volumes (`InitialCreate` was squashed in 4.9).

## Phase 5 — Bragi

### Task 5.1: `StoryDriver` click settle and failure state; the two restored stories

**Files:** Modify `Bragi/src/DesignSystem.Stories/wwwroot/storyDriver.js`, `Scenarios/StoryDriver.razor`, `Account/ExternalLogins.stories.razor`, `Identity/PersonalData.stories.razor`. Test `tests/DesignSystem.Stories.Tests/Scenarios/storyDriver.test.mjs`, `Scenarios/StoryDriverTests.cs`, `DrivenStoryNavigationTests.cs`.

**Interfaces:**
- `waitForPostSubmitSettle(root, requirePostSubmitTurn, requireMutation = true)`. `driveClick` passes `false`, and the 5-second rejection stays for submits.
- `StoryDriver.razor` catches `JSException` beside `JSDisconnectedException` → `_state = "failed"`.

- [ ] **Step 1: Fork.** `git -C Bragi switch -c feature/two-factor-passkeys-stories --no-track origin/master`.
- [ ] **Step 2: Write the failing tests:**
  - `driveClick resolves when the click changes nothing`;
  - `driveSubmit still rejects when nothing settles`;
  - `A_rejected_drive_marks_the_driver_failed_without_throwing`;
  - `External_logins_removed_story_records_one_transition`;
  - `Personal_data_download_story_completes`.
- [ ] **Step 3: Run** `npm test` and `dotnet test tests/DesignSystem.Stories.Tests` (expect failures). **Step 4: Implement**, and restore the stories. **Step 5: Run** — green.
- [ ] **Step 6: Stage** (`git -C Bragi add -A`) — the human commits.

### Task 5.2: Three fakes; scenarios; registrations

**Files:**
- Create `TwoFactor/FakeTwoFactorService.cs`, `Passkeys/FakePasskeyService.cs`, `Authentication/FakeSignInMethodsService.cs`.
- Modify `Account/AccountScenario.cs`, `Account/FakeAccountService.cs`, `Authentication/AuthenticationScenario.cs` (`ExternalProviders`, the next free value), `ServiceCollectionExtensions.cs`.
- Test: one fake class each, and `ServiceCollectionExtensionsTests.cs`.

**Interfaces:**
- `AccountScenario.MachineRemembered = 10`.
- The fakes use immutable static fixtures:
  - the key `"abcd efgh ijkl mnop"`, a fixed URI and SVG;
  - ten codes;
  - `"cred-one"` "Work laptop" and an unnamed `"cred-two"`, with server-shaped `RenameUrl`s;
  - options `"{}"` with state `"story"`;
  - sign-in methods in the server's shape for the requested `ReturnUrl` (non-local → `/`), with no providers except in `ExternalProviders` ("Example provider");
  - `NextUrl`s byte-identical to the server's.
- Scenario arms:
  - `TwoFactorDisabled` → `Validation` from Disable and Generate;
  - `NoPasskeys` → `[]`;
  - `InvalidVerificationCode` → a `Code` error;
  - `Fault` → `Fault`;
  - `MachineRemembered` → `IsMachineRemembered = true`.
- `ServiceCollectionExtensions` registers the three fakes and the seven validators. Login's stories depend on `FakeSignInMethodsService` from here on.

- [ ] **Step 1: Write the failing tests:** one parity fact per operation per relevant scenario, and the validator sweep. **Step 2: Run.** **Step 3: Implement.** **Step 4: Run** — green. **Step 5: Stage** — the human commits.

### Task 5.3: Nine stories, driven locks, catalog, docs; ship gate

**Files:** Create stories for `Account/TwoFactorAuthentication`, `EnableAuthenticator`, `RecoveryCodesHandoff`, `ResetAuthenticator`, `Disable2fa`, `GenerateRecoveryCodes`, `Passkeys`, `RenamePasskey`, and `Authentication/LoginWithPasskey`. Modify `Authentication/Login.stories.razor`, `DrivenStoryNavigationTests.cs`, `ScenarioCatalog.md`, `Bragi/CLAUDE.md`, `Bragi/README.md`.

**Interfaces:**

| Story | Scenarios | Driver |
|---|---|---|
| TwoFactorAuthentication | Success, TwoFactorDisabled, RecoveryCodesLow, Fault, MachineRemembered | `ClickOnly` forget |
| EnableAuthenticator | Success, InvalidVerificationCode, Fault | `FillAndSubmit`, `"123456"` in the Email slot |
| RecoveryCodesHandoff | Success, Fault | — |
| ResetAuthenticator, Disable2fa, GenerateRecoveryCodes | Success, TwoFactorDisabled, Fault | `ClickOnly` |
| Passkeys | Success, NoPasskeys, Fault | none (WebAuthn) |
| RenamePasskey | Success, Fault | `FillAndSubmit`, the name in the Email slot |
| LoginWithPasskey | Success, Fault | none (WebAuthn) |
| Login | gains `ExternalProviders`; every scenario renders the passkey link from the fake | — |

- [ ] **Step 1: Write the failing tests** (`DrivenStoryNavigationTests`):
  - Forget, Reset, Disable and Enable each record one `RecordingSessionTransition` hop to the fixture `NextUrl`.
  - RenamePasskey records one soft navigation to `/Account/Manage/Passkeys?status=passkey-updated`.
  - GenerateRecoveryCodes and RecoveryCodesHandoff render ten codes and record nothing.
  - Failures record nothing.
  - `Login_external_providers_story_renders_the_server_shaped_start_link`.
- [ ] **Step 2: Run.** **Step 3: Implement.** The `ScenarioCatalog.md` Train 3 section and the two restored rows; CLAUDE.md and README record T3-7. **Step 4: Run** `npm test` and `dotnet test Bragi.slnx` — green.
- [ ] **Step 5: Stage** — the human commits. Ship gate: Bragi PR, merged immediately after Heimdall's publish (release order).

## Phase 6 — Yggdrasil, Bifröst, Glitnir

### Task 6.1: Composition changes and facts

**Files:**
- Modify `Yggdrasil/src/Hosting.Web.Server/Program.cs`:
  - `:41-44` comment: Himinbjörg now contributes only the external-login callback page, and every two-factor and passkey page is Heimdall's;
  - delete `:52` `.AddScoped<IdentityRedirectManager>()`;
  - `:140` → `app.MapNorseExternalLoginEndpoints()`;
  - the `:152-161` probe comment → "a bare 403 for a non-navigation; a login challenge for an anonymous page navigation (Ruling 44) and the access-denied redirect for an identity principal's page navigation (T3-3)";
  - `.AddNorseExternalProviders(builder.Configuration)` (no section is configured in Train 3);
  - `builder.Services.AddNorseIdentityInvariantVerification(builder.Configuration)`, with a comment citing `Yggdrasil/CLAUDE.md:20`.
- Modify `src/Hosting.Web.Server/appsettings.Development.json`: `"Identity": { "Authenticator": { "Issuer": "Norse" }, "Verification": { "Interval": "00:01:00" } }` (not `appsettings.json`; the one-minute interval is development only).
- Modify `Components/App.razor:28` (delete the `PasskeySubmit.razor.js` tag), `src/Hosting.Web.Components/Routes.razor` (`NotAuthorized` → `<NotAuthorizedRedirect User="context.User" />`), and `Layout/NavMenu.razor` (`<NavLink href="Account/Logout">Logout</NavLink>`).
- `Hosting.Worker` and `Directory.Packages.props` are not touched.
- Test: `tests/Hosting.Web.Server.Tests/Authentication/ChallengeAndForbidTests.cs:96`, `CompositionTests.cs`, `tests/Hosting.Web.Components.Tests/NavMenuTests.cs`.
- Test hosts set `Identity:Verification:Enabled = false`, except the one fact that asserts the service.

- [ ] **Step 1: Fork.** `git -C Yggdrasil switch -c feature/account-lifecycle-train-3 --no-track origin/master`.
- [ ] **Step 2: Write the failing tests.**
  - `ChallengeAndForbidTests`: `An_identity_principal_failing_a_policy_is_sent_to_access_denied_on_a_page_navigation` (renamed from `:96`), plus `…_off_a_page_navigation_gets_a_bare_403`.
  - `CompositionTests`, structure:
    - `The_train_3_pages_route_from_heimdalls_assembly` (nine routes);
    - `The_passkeys_module_is_served_as_a_static_web_asset` (200 `text/javascript`);
    - `The_logout_post_is_heimdalls_component_endpoint` (`ComponentTypeMetadata` from Heimdall; no Himinbjörg delegate);
    - `No_passkey_post_endpoint_remains`;
    - `The_external_start_is_a_get_endpoint`;
    - `The_two_factor_passkey_and_sign_in_methods_contracts_resolve_on_the_client`;
    - `A_missing_authenticator_issuer_fails_startup` (Production, no issuer);
    - `The_web_host_runs_the_invariant_verification_service` (enabled, with the query substituted).
  - `CompositionTests`, lane facts: a Postgres-backed host with **`SecurityStampValidatorOptions.ValidationInterval = TimeSpan.Zero`**, so the stock `SecurityStampValidator` checks the stamp on every request through `NorseSignInManager.ValidateSecurityStampAsync` (`:339`).
    - `A_local_return_url_survives_login`;
    - `The_two_factor_lifecycle_runs_over_the_wire` (rows 21–24, in order): setup → enable (TOTP from the key) → the handoff reveals ten codes once (a second reveal → already shown) → generate → reset (which disables) → setup again (the replacement key differs) → re-enable with a fresh TOTP from the replacement key (codes remain, so `authenticator-verified`) → disable → disable again (`Validation`, "Two-factor authentication is not enabled.", stamp unchanged). After each rotating step (enable, reset, re-enable, disable) the stored stamp differs from the pre-step stamp, the pre-step cookie is refused (`Unauthenticated`) and the refreshed one passes; Generate and the refused second disable leave the pre-step cookie valid.
    - `Forget_this_browser_over_the_wire`;
    - `Remember_machine_round_trips_over_the_wire`;
    - `Login_lockout_over_the_wire`;
    - `The_passkey_management_lane_resolves`: empty list, creation JSON with `"userVerification":"required"`, and rename/remove of an unknown well-formed id → `NotFound`;
    - `Sign_in_methods_resolve_anonymously`: local and foreign `ReturnUrl`, no providers, and request options JSON.
  - `NavMenuTests.Logout_is_a_link_to_heimdalls_logout_page`.
- [ ] **Step 3: Run** `dotnet test Yggdrasil.slnx` on project references (expect failures). **Step 4: Implement.** **Step 5: Run** — green.
- [ ] **Step 6: Stage** (`git -C Yggdrasil add -A`) — the human commits.

### Task 6.2: End to end through Bifröst; Yggdrasil docs

**Files:** Modify `Yggdrasil/CLAUDE.md:43`: Heimdall owns every `/Account/*` page except `/Account/ExternalLogin`, and Himinbjörg keeps `ExternalLogin`, the `PerformExternalLogin` GET and the `LinkExternalLogin` GET pair. Also modify `Yggdrasil/README.md` if it narrates the tree. Bifröst's `AppHost.cs` is not modified.

- [ ] **Step 1: Re-check T3-23**, and record `DotNetVersion` and Playwright's Chromium major.
  - If either changed, also walk the WASM column, and add the Playwright phase to `WebServerBrowserRuntimeSmokeTests`.
  - Otherwise, walk the Server host with `.wasm` aborted.
- [ ] **Step 2: Wipe and stand up.** Wipe `norse-pg-primary`/`norse-pg-replica`, then `dotnet run --project src/Orchestration.AppHost` from the Bifröst root.
- [ ] **Step 3: Walk** with Playwright, throwaway accounts, and a CDP virtual authenticator (`WebAuthn.enable` + `addVirtualAuthenticator`: `ctap2`, `internal`, resident keys, `hasUserVerification: true`, `isUserVerified: true`). W*n* asserts End state row *n*'s Server column. W21–W24 run in that order on one account (enable, generate, reset then re-enable, disable). W10 and W12 are recorded as "Train 5"; W11 and W17 are their provider-free halves. Record each outcome; any failure is fixed in its owning realm inside this train.
- [ ] **Step 3b: WHATWG corroboration of `ReturnUrls`.** In the same Playwright session, `page.evaluate` over every row of the Task 4.3 matrix with `PathBase = /app`: `new URL(landing, origin + "/app/")`, where `landing` is the row's expected `Resolve` output. Assert the resolved `origin` equals the page's origin and the `pathname` starts with `/app/`. For each row whose expected output is the candidate itself, also assert the candidate's query and fragment survive in `search` and `hash`. Record the result beside the walk.
- [ ] **Step 4: Update** `Yggdrasil/CLAUDE.md:43` (and README). **Step 5: Run** `dotnet test Yggdrasil.slnx` — green.
- [ ] **Step 6: Stage** — the human commits.

### Task 6.3: Spec "Train 3 as built", amendments; Bifröst state of the union

**Files:** Modify `Glitnir/docs/Himinbjorg/specs/2026-10-26-identity-account-lifecycle-over-grpc-design.md`, `Glitnir/docs/Asgard/specs/2026-08-11-session-transition-seam-design.md` (`:490`), and the Bifröst root `README.md` and `CLAUDE.md` (§1 only; §9 unchanged).

- [ ] **Step 1: Write A1–A10** inline, each marked "Amended 2026-10-28 (Train 3, T3-n)". Then write **§15 "Train 3 as built"** in the §14 shape:
  - what was built, and the dispositions of dockets 1–9;
  - the 4.6 decompile record (the T3-31 value, the UV enforcement point);
  - the walk results (W10/W12 as Train 5, W11/W17 as halves) and the T3-23 re-check;
  - the remaining deferrals exactly as tabled above;
  - the A10 dependency lines verbatim;
  - the template findings for Microsoft (not filed).
- [ ] **Step 2: Write the Bifröst pair.** `CLAUDE.md` §1 gains three entries:
  - "**State of the union (2026-10-26):**" for Train 1: the anonymous tier over gRPC, the challenge token, the deferral chain.
  - "**State of the union (2026-10-27):**" for Train 2: account core, the Manage tree in Heimdall, the last-sign-in-method guard, Rulings 44/46/47.
  - "**State of the union (<Train 3 ship date>):**" for Train 3:
    - two-factor and passkeys over gRPC, with user-verifying, one-shot passkey sign-in;
    - the scaffold gone;
    - returnUrl with server-built sign-in links;
    - stamp refreshes;
    - external login requiring the second factor, behind fail-closed GET starts, proven by TestServer facts;
    - the invariant job in the web host;
    - AccessDenied on both lanes;
    - `CanAct`.

    It closes by naming **Train 4 (OpenIddict, spec sub-project B: authorization code + PKCE, step-up)** and **Train 5 (full end-to-end OpenIddict / ASP.NET Identity integration, which walks external login locally against OpenIddict)**.

  Each entry names its plan and spec by relative path. `README.md`'s "What's live" gains matching paragraphs, with the same train names. The realm tables, §9 and the AppHost are untouched.
- [ ] **Step 3: Verify** relative paths only, and that the pair agrees on dates, scope and train names.
- [ ] **Step 4: Stage** (`git -C Glitnir add <both specs>`; `git add README.md CLAUDE.md` at the Bifröst root) — the human commits. Ship gate: the Yggdrasil PR with every bump folded. Train 3 is done; the lifecycle continues in Trains 4 and 5.

---

## Self-review record

- **Spec coverage:**
  - §2 ruling 3 → T3-20/A8.
  - §4.3 → 3.1, 4.4–4.8, 4.10.
  - §4.4 → 4.2, 3.9.
  - §5.2 → 4.1, 4.6, 4.10.
  - §5.3–5.5 → 4.3–4.6, 4.9, 4.10.
  - §5.7 → 4.8, 4.11, 4.13, 6.1 (A9).
  - §5.8 → 4.13.
  - §5.9 → 4.12.
  - §6.1–6.7 → 3.2–3.10.
  - §7 → 5.1–5.3.
  - §8 → 6.1, 6.2 and the release order.
  - §9 item 3 → A1.
  - §10 → every task, plus T3-23.
  - §11: each bullet is closed (T3-2, T3-12/13/35, T3-27, T3-28, 4.10), out of scope, or moved by Buvy.
  - §13 → 4.13.
  - Dockets 1–9 → T3-1…T3-9.
- **Interface consistency:**
  - Contract methods 7/6/3; sixteen records; `IAccountService` stays at 8.
  - Handler count 21 → 25 → 28 → 32 → 34 → 35 → 37; 4.9 adds none.
  - `ManagePolicyLockTests` 7 → 15.
  - `LoaderOnly` plus the theory rows cover every new `OutcomeComponentBase`.
  - `IPasskeyChallengeLedger` is declared in 4.4 (in-memory) and made real in 4.9, used by 4.4 and 4.10.
  - `ConfigureRemoteFailure`/`RedirectRemoteFailure` (4.8) are used by the OIDC registration and by 4.11's fake.
  - `FakeRemoteAuthenticationHandler` (4.8) is used by 4.11.
- **Placement:** every container suite is in `Identity.Web.Server.Tests`. `Identity.EntityFramework.Tests` gains only the temporal-model row.
- **Proportion:** 31 tasks — Asgard 1, Midgard 1, Heimdall 10, Himinbjörg 13, Bragi 3, Phase 6 3. Code bodies are limited to rules no signature or test name decides.
