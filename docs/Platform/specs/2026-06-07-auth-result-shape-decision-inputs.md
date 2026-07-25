# Auth Result Shape & gRPC-able Surface — Decision Inputs (NOT a spec)

**Status:** DISCHARGED 2026-06-07 by `2026-06-07-auth-design.md`. Decision inputs were captured here; the design session convened the same day and ratified the thrust (issuance = OAuth/cookie; lifecycle = `IAccountApi` → `Outcome<T>`; `AuthResponse`/`AuthResult` retired; no `SignInAsync` in handlers) plus the open forks (registration server-rendered inside the OAuth flow — no `Register`/`EmailRegistered`/`GetExternalLogins` API; `GetClaims` dropped; Auth ships the standard six-project shape *including* `.Backend`, persistence inverted). Retained as history of the framing. — This was **not** a design spec and decided nothing on its own.
>
> **Amendment (2026-07-25):** the `IAccountApi` thrust this doc fed into was itself not the direction ultimately taken. Heimdall shipped `IAuthenticationService` (`[GenerateGateway]`, `Login`/`Register`/`Logout` returning `Outcome<T>`) instead — narrower, and outside `Norse.Auth.Contracts` (that assembly doesn't exist). See `../../../../Heimdall/CLAUDE.md`.

**Read alongside:** `2026-05-20-auth-federation-design.md` (the authoritative auth spec — **read it first; this doc has not reconciled against its current text**), reconciliation tracker `spec-reconciliation-2026-06-04.md` §1.6 (the `Outcome<T>` / three-result-families ruling), §5.7 + §5.8 (webhook auth-handler ruling + auth-spec absorption), and `2026-05-26-mediator-design.md` §3.3/§7 (`Outcome<T>`, service-entry authorization).

**Origin:** Buvy revisited the parked "auth uses `Result<T>`?" flag (tracker §1.6 follow-on). He supplied his prior-platform (Assurely) `AuthService` as reference — `[Authorize(Policy = …)]` per gRPC method on `ServerGrpcServiceBase`, returning a bespoke `AuthResponse(AuthResult, ClaimsPrincipal? / string[] messages)` envelope, with `HttpContext.SignInAsync` side-effects inside the methods (and a `// TODO: refactor into using MediatR`). His stated end goal: **a service he can make gRPC-able to serve MAUI apps.** He acknowledged the OG code "had good intentions but was probably suboptimal."

---

## 1. The reframe: the OG service conflates two opposite jobs

The OG `AuthService` mixes:

- **Credential issuance** — login, logout, register-then-`SignInAsync`. Mutates the *authentication state* (mints cookies/tokens). Runs for an **anonymous** caller. Web-context-coupled (`HttpContext.SignInAsync`).
- **Account lifecycle** — confirm email, forgot/reset password, manage profile, get claims, external logins, email-registered check. Ordinary application work behind an **already-authorized** caller — the exact shape of the `Outcome<T>` mediator model (mediator §3.3).

The `AuthResponse`/`AuthResult` envelope exists to carry the cookie/principal back out — issuance leaking into the return shape. **That is why it doesn't fit `Outcome<T>`: it isn't a result, it's a side-effect receipt.** Resolving the §1.6 flag = un-conflating these two jobs.

## 2. The MAUI killer point: a bespoke gRPC `LoginAsync` is ROPC, which OAuth 2.1 forbids

A custom gRPC `LoginAsync(email, password)` **is** the OAuth Resource Owner Password Credentials grant — **explicitly removed in OAuth 2.1**, the spec the platform already adopted (CLAUDE.md §4 → Auth: OpenIddict / OAuth 2.1). The decided, correct shape:

- **MAUI** → **Authorization Code + PKCE** via the system browser (`ASWebAuthenticationSession` / Custom Tabs) → OpenIddict token endpoint mints a **JWT**. MAUI stores it, sends it as the bearer on every gRPC call.
- **Web (Blazor Server)** → same OpenIddict, cookie-backed session (BFF).

**The cookie-vs-token transport problem dissolves** — OpenIddict issues the right credential per client; gRPC services just consume a validated principal. This is the direct generalization of the webhook auth-handler ruling (§5.7/5.8): **OpenIddict owns authentication, its client store is the registry, and services never hand-roll credential checks — they receive an enriched principal before the method body runs.** Login is the human-facing sibling of the webhook client-credentials flow.

## 3. Proposed split (the design thrust for the session to ratify or amend)

1. **Credential issuance & session = OpenIddict OAuth endpoints, NOT a gRPC service.** Login, logout (→ token revocation + cookie clear), refresh, register-then-signin. No custom `LoginAsync` / `LogoutAsync` over gRPC. **Retires `AuthResponse` / `AuthResult` entirely.**

2. **Account lifecycle = a gRPC-able `IAccountApi : [MediatorService]` returning `Outcome<T>`.** ConfirmEmail, ForgotPassword, ResetPassword, ManageProfile, EmailRegistered, GetExternalLogins. These:
   - carry the OG policy tiers as **service-entry** `[Authorize(Policy = Anonymous | Authorized | Verified)]` — unchanged intent, correct layer (mediator §3.3; authorization is service-entry, never an `Outcome` value);
   - return `Outcome<T>` cleanly — Identity's `result.Errors.Select(e => e.Description)` maps to `Outcome.Err(Validation, field-keyed)`; the don't-reveal pattern (ForgotPassword on unknown email) stays `Outcome.Ok`;
   - are gRPC-able for MAUI **for free**, riding `I{Context}Api` through the existing door (UI Composition §8).

## 4. The side-effect cleanup (what actually makes it gRPC-able)

The OG `ConfirmEmail` / `ResetPassword` call `SignInAsync` to rebuild the cookie with new claims. **Drop that from the handler.** A claims change publishes a domain event (`EmailConfirmedEvent`, …); the principal picks up the new claim on its next token refresh / security-stamp validation — built-in OpenIddict/Identity machinery. This removes `HttpContext` from the handler, which is the thing currently blocking clean, transport-agnostic gRPC exposure. (`ValidateAsync`'s nonce → security-stamp check is built-in security-stamp validation, not a method to write.)

## 5. Forks the session must decide (left open deliberately 2026-06-07)

1. **`GetClaims` for MAUI** — the JWT already carries claims; MAUI reads them locally. Keep a thin server query only if server-authoritative on-demand claims are wanted, else drop it.
2. **`IAccountApi` placement** — `Norse.Auth` context; lifecycle-over-Identity-stores sits in `Norse.Auth.Server` (Mongo-SoR inversion, CLAUDE.md §4 → Auth). Confirm it needs **no `.Worker` half** (Identity *is* Mongo here; no system-of-record Postgres write). If true, no `.Backend` either (project-shape law).
3. **Register straddles both jobs** — it creates the account (lifecycle, `Outcome<T>`) *and* signs in (issuance). Cleanest: register returns `Outcome.Ok(registered)`, then the client runs the normal Auth Code + PKCE flow — two steps, no `SignInAsync` in the handler. Weigh the extra client choreography against UX.

## 6. What this discharges / touches

- **Discharges** the §1.6 parked flag ("auth uses `Result<T>`?") — answer: account lifecycle uses `Outcome<T>`; issuance uses OAuth tokens/cookies; `AuthResponse`/`AuthResult` retired.
- **Dovetails** with §5.8 (webhook client modeling over the OpenIddict application store) — same "OpenIddict owns the registry + authentication" principle.
- **Auth-spec amendments** likely land in the same session as tracker items 2.11 (Migrations = projection tables only), 2.17 (`Norse.Auth.Components` declaration), 5.8 (webhook client modeling) — batch if coherent.
- **Interacts** with the EncryptedString spec (4.3) where credential/secret storage is concerned — coordinate, don't resolve here.
