# Heimdall — Auth Realm Design (Identity, Issuance, Account Lifecycle)

> **Heimdall** is the cross-cutting authentication/authorization realm — the watchman of Bifrost who guards the one crossing into Asgard, sees a hundred leagues by day and night, hears the grass and the wool grow (knows every identity), and as **Ríg** fathered and ranked the stations of mankind. He is the single Norse figure who answers *both* halves of the gate: who you are **and** what you may do. Top-level codename (not `{Company}.Auth.*`): auth is platform infrastructure consumed by every product realm — the insurance MGA, the deregulated energy retailer, and the many more to follow — so it cannot nest under any one of them. Yggdrasil is a multi-product platform; Heimdall is its single identity plane. "Who you are" is the declared principal contract (`Norse.Abstractions.Identity.NorsePrincipal`, ruling 1.2); "may you pass / what you can do" is Heimdall, the embodied gate. **Reassigned from observability** (its prior holder, now **Gjallarhorn** — Heimdall's alarm-horn) — auth is the more exact fit for the watchman at the bridge.

**Date:** 2026-06-07
**Status:** Approved design, pre-implementation
**Owner:** Buvy
**Supersedes:** `2026-05-20-auth-federation-design.md` (federation topology carried forward intact; service surface added; all post-05-20 rulings absorbed). The 05-20 file is retained as history, marked SUPERSEDED.
**Discharges:** `2026-06-07-auth-result-shape-decision-inputs.md` (the §1.6 follow-on); reconciliation tracker items 2.11, 2.17, 5.8; the §1.6 "auth uses `Result<T>`?" flag.
**Companion specs:** `2026-05-26-mediator-design.md` (`Outcome<T>`, service-entry authorization, `I{Context}Api` dispatch); `2026-06-03-messaging-foundation-design.md` (endpoint flavors, hard walls); `2026-05-21-midgard-persistence-design.md` (CQRS tiers, `IDocumentRepository<T>`); `2026-06-03-tenancy-model-design.md` (stamp-per-tenant); `2026-05-20-yggdrasil-hosting-design.md` (plugin model, webhook auth handlers); future `EncryptedString` spec (credential/secret storage); future Notifications spec (outbound confirm/reset email).

> **Amendment (2026-07-25):** this "Approved design, pre-implementation" was never carried out as written. The realm that actually shipped is Heimdall (`Norse.AuthN.*`) on Himinbjörg (`Norse.Identity.*`): `IAuthenticationService` (`[GenerateGateway]`, currently `Login`/`Register`/`Logout`) returns `Outcome<T>` directly — no `NorsePrincipal`, `Population`, or `IAccountApi` exists anywhere in current source. See `../../../../Heimdall/CLAUDE.md`.

---

## 1. Why This Spec Supersedes the Federation Spec

The 2026-05-20 federation spec settled **where identity lives and how populations federate**. It was correct and stands. But three things happened after it landed that it could not foresee, and a fourth that it deferred:

1. **The error-vocabulary ruling (1.6, 2026-06-07)** gave the platform three deliberately-distinct result families — `Result<T>` (conversion), `Outcome<T>` (application), `HttpResult<T>` (transport) — and pulled authorization out of the mediator to service-entry. Auth's return shapes have to land somewhere in that vocabulary.
2. **The identity-storage inversion (2026-06-03, refined by 1.8)** made Mongo the system of record for identity and Postgres a worker-written projection, split by durability.
3. **The webhook auth-handler ruling (5.7, EXECUTED 2026-06-07)** made OpenIddict's client store the partner registry and `client_id` the UUID v5 namespace — work the auth spec owns the client modeling for (5.8).
4. **The service-surface question** the federation spec explicitly deferred to a "future identity-contract spec": what shape do login, logout, register, confirm-email, reset-password actually take when a real client (Blazor, MAUI, Claude Desktop) calls them?

The catalyst for (4) was Buvy's prior-platform (Assurely) `AuthService` — a single gRPC service that conflated two opposite jobs. Un-conflating it is the spine of this spec (§3). The federation map, principal model, token formats, and cross-cutting policies are carried forward from 05-20 with the post-05-20 rulings folded in; the genuinely new material is the **issuance/lifecycle split** and its consequences for the project shape, the deletions, and the OpenIddict surface.

This spec is also a **reference-implementation teaching artifact** (Appendix A maps the OG service method-by-method): "here is a real-world auth service that grew the wrong shape, and here is the pit-of-success refactor, with the *why* on every line."

---

## 2. Scope and Non-Goals

### In scope

- The two-jobs split: **credential issuance** (OAuth/cookie endpoints) vs. **account lifecycle** (`IAccountApi` → `Outcome<T>`).
- `IAccountApi`: its exact membership, return shapes, service-entry authorization tiers, and the side-effect cleanup that makes it transport-agnostic.
- The issuance surface: one authorize surface, three client shapes, split terminal credential (cookie vs. token); per-population sign-in mechanics; registration as a server-rendered step inside the OAuth flow.
- The federation map (carried from 05-20).
- The principal model (`NorsePrincipal` in `Norse.Abstractions.Identity`; anonymous-as-principal; population taxonomy; anonymous→customer promotion; logout identity behavior).
- Token formats, lifetimes, rotation, signing keys, discovery (carried from 05-20).
- Identity storage: Mongo SoR + Postgres projection, the durability split, and the `.Server`-authority / `.Worker`-projection relationship.
- Auth's project shape (standard six projects including `.Backend`; persistence inverted, assembly shape ordinary).
- Webhook client modeling over the OpenIddict application store (5.8 absorption).
- Cross-cutting policies: account linking, email normalization, magic-link mechanics, MFA per population, cookie/CSRF, signout, rate limiting (carried from 05-20).

### Out of scope (separate specs)

- **Authorization model** — RBAC vs. ABAC vs. policy-based; who-can-do-what *within* a population; cross-population impersonation. (Authorization-model spec.)
- **Detailed role/scope taxonomy** per population, claim enrichment via `IClaimsTransformation`. (Authorization-model spec.)
- **Password policy specifics** — length, breach-list, lockout backoff. NIST 800-63B is the working baseline.
- **Audit-event schema and retention** — exact `auth` projection table shapes, retention by event kind.
- **Self-service / admin UX** — registration screens, MFA setup, account settings, agency-admin and staff-admin user management. (`Norse.Auth.Components` exists for these; UI-composition territory.)
- **Outbound confirm/reset email delivery** — the *sending* of confirmation and reset mail is a Notifications-spec concern; this spec owns only the event that triggers it.
- **`EncryptedString` mechanics** — wrapper, blind index, KV envelope, DEK lifecycle. This spec consumes it (signing secrets, MFA secrets); it does not design it.
- **Dynamic Client Registration** for MCP — deferred; re-opens when MCP expands beyond staff.
- **Producer SAML / customer-IAM vendor adoption** — deferred (re-entry triggers §4.4).
- **Shared-compute multi-tenancy** — under stamp-per-tenant there is no tenant claim and no tenant-routing; re-entry triggers in the tenancy spec §6.

---

## 3. The Spine: Two Jobs, Cleanly Split

The OG `AuthService` (Appendix A) mixed two jobs that pull in opposite directions:

- **Credential issuance** — login, logout, register-then-sign-in. *Mutates the authentication state* (mints cookies/tokens). Runs for an **anonymous** caller. Web-context-coupled (`HttpContext.SignInAsync`).
- **Account lifecycle** — confirm email, forgot/reset password, manage profile. Ordinary application work behind an **already-resolved** principal. The exact shape of the mediator's `Outcome<T>` model.

The OG `AuthResponse(AuthResult, ClaimsPrincipal? / string[] messages)` envelope existed to carry the freshly-minted cookie/principal back out. **That is why it never fit `Outcome<T>`: it is not a result, it is a side-effect receipt.** Resolving the §1.6 flag is un-conflating these jobs and retiring the receipt.

### 3.1 Issuance is OAuth/cookie endpoints — never a custom RPC method

A bespoke gRPC `LoginAsync(email, password)` **is** the OAuth Resource Owner Password Credentials grant — explicitly removed in OAuth 2.1, the profile the platform already adopted. There is no `LoginAsync`, `LogoutAsync`, `RegisterAsync`, or `LeadLoginAsync` anywhere in the platform's service surface. Issuance happens at:

- **OpenIddict's OAuth 2.1 endpoints** (`/connect/authorize`, `/connect/token`, `/connect/revoke`) for the clients that hold tokens (MAUI, Claude/MCP, M2M).
- **The same-host cookie sign-in** (`SignInAsync` on the co-hosted authorize pages) for the browser session.

Both terminals are fed by **one authorize surface** and **one credential pipeline** (§4).

### 3.2 Account lifecycle is `IAccountApi` returning `Outcome<T>`

```
Norse.Auth.Contracts.IAccountApi  :  [MediatorService]
  ConfirmEmail(ConfirmEmailRequest)        -> Outcome           [Authorize(Verified-after? see §6.2)]
  ForgotPassword(ForgotPasswordRequest)    -> Outcome           [Authorize(Policy = Anonymous)]
  ResetPassword(ResetPasswordRequest)      -> Outcome           [Authorize(Policy = Anonymous)]
  ManageProfile(ManageProfileRequest)      -> Outcome<Profile>  [Authorize(Policy = Verified)]
```

That is the **entire** membership. Each method:

- carries its authorization tier as a **service-entry** `[Authorize(Policy = …)]` (host pipeline, *before* the mediator runs — mediator §3.3 / ruling 1.6). Authorization is never an `Outcome` value; there is no `Forbidden` category.
- returns `Outcome` / `Outcome<T>` cleanly. Identity's `result.Errors.Select(e => e.Description)` maps to `Outcome.Err(Validation, field-keyed)` via the validate step's field aggregation (mediator §6). The don't-reveal pattern (forgot-password on an unknown email) returns `Outcome.Ok`, not a failure.
- is **gRPC-able for MAUI for free**, riding `I{Context}Api` through the existing door (mediator §7, UI Composition §8). MAUI calls these with its bearer token like any other context API.

The `Anonymous` / `Authorized` / `Verified` policy tiers are carried verbatim from the OG's intent. The "negative" tiers (`Anonymous` = the caller must **not** be signed in, for forgot/reset) are satisfiable precisely because the anonymous principal is a real, non-null principal (§5.1) — an anonymous caller is still an authenticated request, just in the `Anonymous` population.

### 3.3 No `SignInAsync` in any handler

The OG `ConfirmEmail` / `ResetPassword` called `HttpContext.SignInAsync` to rebuild the cookie with updated claims. **That coupling is deleted.** A claims-affecting lifecycle operation instead **bumps the security stamp**; ASP.NET Core's `SecurityStampValidator` (cookie) and the next token refresh (JWT) rebuild the principal with the new claim on the next validation cycle — built-in Identity/OpenIddict machinery, not code we write. Dropping `HttpContext` from the handler is the precise thing that unblocks clean, transport-agnostic gRPC exposure. (The OG `ValidateAsync` — "verify the logged-in user is still valid… nonce claim → security-stamp check" — is exactly this built-in validator; we do not hand-write it.)

### 3.4 The governing principle: robustness through subtraction

Every move in §3 is a **removal**, and that is the point. This is the functional-programming insight — *removing capability is what makes a system robust* (immutability removes the ability to mutate; the result is code you can reason about). The OG could be called eight ways; this design **deletes most of the surface** and the remainder gets *more* trustworthy, not less:

- Removing `LoginAsync` (a capability) removes the ROPC attack surface entirely.
- Removing `SignInAsync` from handlers removes `HttpContext` coupling, which *is* what makes the handlers transport-agnostic and gRPC-able.
- Removing `EmailRegistered` / `GetExternalLogins` removes an enumeration oracle and a config leak.
- Removing the server's ability to *publish events* (§10 — it may only *send commands*) removes the class of bug where a non-authoritative tier emits a fact it didn't commit.
- Removing the mutable `AuthResponse` receipt removes the conflation of result and side-effect.

The pit of success here is carved by subtraction. When a future maintainer asks "can I misuse this?", the honest answer is increasingly "there is no longer a method to misuse."

---

## 4. Issuance Architecture — One Surface, Three Terminals

> **One authorize surface. One credential pipeline. The terminal credential differs by client shape.**

There is a single set of **server-rendered pages** in Auth (login, signup, TOTP challenge/enrollment, social-link, OAuth consent) and a single credential pipeline (Mongo Identity stores for local accounts + Google OIDC federation for staff). Every client hits that surface. What differs is only what the client walks away holding.

```
                    ┌─────────────────────────────────────────────┐
                    │   ONE authorize surface (Auth)            │
                    │   server-rendered: login / signup / TOTP /    │
                    │   social-link / consent                       │
                    │   pipeline: Mongo Identity stores +           │
                    │             Google OIDC (staff)               │
                    └───────────────┬───────────────────────────────┘
            ┌───────────────────────┼───────────────────────────────┐
            ▼                       ▼                               ▼
   Browser (Blazor Web)     MAUI (system browser)          Claude Desktop (MCP)
   terminal = COOKIE        terminal = TOKEN               terminal = TOKEN
   SignInAsync (same-host)  Auth Code + PKCE → JWT         Auth Code + PKCE → JWT
   HttpOnly, never in JS    + refresh, secure storage      aud = MgaMcp, staff-only V1
```

### 4.1 Browser — cookie via same-host `SignInAsync`

The authorization server and the Blazor Web App are the **same `Norse.Hosting.Web.Server` process**, sharing one Data Protection key ring. The authenticated browser cookie is therefore minted **directly** via `SignInAsync` on the co-hosted authorize pages — **no self-OIDC redirect, no BFF token exchange against ourselves.** Per CLAUDE.md §2.5, a self-redirect Auth Code dance against your own in-process server is ceremony with no security benefit; and under single-host, in-process gRPC calls ride `HttpContext.User` (§6.1 of the token table below), so any token the BFF stashed would be vestigial.

- **Anonymous** — first GET to any surface without a cookie issues a directly-signed anonymous cookie (UUID v5, `Population = Anonymous`). No OAuth flow: there is no credential to exchange. (§7.0.)
- **Staff** — "Sign in with Google" → OIDC Authorization Code flow *to Google* (`hd={company}.com`) → callback validates the ID token → auto-provision on first sign-in → `SignInAsync` mints the authenticated cookie.
- **Customer / Producer** — server-rendered login (email + password + TOTP) → credential check against Mongo Identity stores → `SignInAsync`.
- **Customer registration** — server-rendered signup → create account → anonymous→customer promotion (§5.4) reads the anonymous cookie server-side → `SignInAsync`.

The browser never holds a JWT. Cookie middleware in `Norse.Hosting.Web.Server` validates every request.

**Re-entry trigger:** if the Blazor Web App is ever split into a deployable separate from the auth server, the same-host shortcut no longer holds and the browser is promoted to a full BFF (web app becomes an OIDC client of OpenIddict, server-side code exchange, cookie materialized from the result). Under the deliberate single-host topology (CLAUDE.md §4 → Hosting) this is not planned.

### 4.2 MAUI — token via Auth Code + PKCE

MAUI (Blazor Hybrid) launches the **system browser** (`ASWebAuthenticationSession` on iOS, Custom Tabs on Android) against the **same authorize surface**. The user signs in *or signs up* on the same server-rendered pages — which is exactly why registration works from MAUI with **zero MAUI-specific auth UI** (the §3 ratification: registration is server-rendered inside the OAuth flow, so it is reached identically through the native system browser). On success, OpenIddict issues an authorization code → the system browser redirects to the app's custom-scheme callback → MAUI exchanges code + PKCE verifier at `/connect/token` → receives a JWT access token + refresh token → stores them in platform secure storage → sends the access token as the bearer on every gRPC call. The ephemeral system-browser session establishes no persistent cookie; the token is the credential.

### 4.3 Claude Desktop (MCP) — token via Auth Code + PKCE, staff-only V1

Pre-registered public client (`client_id = {company}.mcp.claude-desktop`, no secret; PKCE carries integrity). Launches the user's default browser → if a staff cookie already exists there, SSO; else the same login pages → consent screen scoped to MCP tools + the calling client → authorization code → token exchange → JWT (`Audience = MgaMcp`, `Population = Staff`, roles filtered to the MCP-permitted set). MCP servers are resource servers validating JWTs locally via JWKS. Eventual rollout order A → B → C. DCR (RFC 7591) deferred.

### 4.4 Federation map (carried from 05-20)

| Population | Identity location | V1 federation | Notes |
|---|---|---|---|
| **A. Staff** | Google Workspace (upstream) | Direct OIDC, mandatory | `hd={company}.com`; Google enforces the domain restriction; MFA upstream by Workspace policy. Auto-provision on first sign-in with **no roles**. |
| **B. Producers** | Local in OpenIddict (Mongo) | None | Agency-scoped local accounts; invite-only enrollment; TOTP mandatory at enrollment. |
| **C. Customers** | Local in OpenIddict (Mongo) + optional social | Per-user additive (Google, Apple) | Local primary; social subjects bind to an existing customer via authenticated linking only (§8.1). |
| **D. M2M** | Client credentials in OpenIddict | N/A | One `application` per consumer; per-client scopes; secret rotation. Guid `client_id` (§9). |
| **E. Fronting carrier** | Same as D in V1 | Carrier Keycloak slot documented | `CarrierKeycloakHandler [V2Slot]` placeholder; V2 wiring is configuration only. |
| **MCP** | Reuses A/B/C principal | OAuth 2.1 Auth Code + PKCE | V1 = staff only; pre-registered Claude Desktop client; no DCR. |
| **F. Anonymous** | Generated UUID v5, signed cookie | N/A | First contact issues a stable id; registration promotes without remap. |

**What the topology forecloses (intentionally):** Keycloak-as-broker (earns its keep only at 3+ upstreams or large-producer SAML); per-population issuers (a single `aud` taxonomy discriminates audiences; multiple issuers force every resource server to trust a *list*); silent identity fallback (every federation path produces a populated principal or fails loudly — the anonymous principal is a deliberate state, never a degraded one).

**Re-entry points:** producer agency >100 users demanding SAML → broker pattern re-opens; customer count into millions → customer-IAM vendor comparison; a third upstream IdP → broker re-opens; shared-compute multi-tenancy → a tenant claim and tenant-routing return per the tenancy spec §6.

---

## 5. The Principal Model

### 5.1 Every request carries a non-null principal

Every Norse surface — portal page load, JSON API call, gRPC call, message handler dispatched from a queue — receives a principal whose id is non-null. There is no anonymous-as-absent. `[AllowAnonymous]` does not appear in the platform; `YGG110` flags any occurrence as a build error. This is foundational, not stylistic: downstream contexts attach event-sourced rows, audit trails, and abandoned-flow recovery to a stable id without nullable-principal handling or "merge anonymous cart into customer" reconciliation. **The principal id generated on first contact *is* the customer id after registration.**

### 5.2 The envelope lives in `Norse.Abstractions.Identity` (ruling 1.2)

`NorsePrincipal` (renamed from `NorsePrincipal` per the `NorseTier` precedent, ruling 1.2 sub-point) lives in **`Norse.Abstractions.Identity`**, not `Norse.Auth.Contracts` — platform realms (Norse host middleware, `Norse.ReferenceData.Audit`) consume it, so it must sit at Abstractions tier or it inverts the realm DAG. Auth remains the authority that *populates* it. It wraps a `ClaimsPrincipal` and exposes typed accessors so downstream code never reads string-keyed claims:

```
Norse.Abstractions.Identity.NorsePrincipal
  PrincipalId : Guid               (UUID v5, auth-context namespace)
  Population  : Population enum     { Anonymous=1, Staff=2, Producer=3, Customer=4, Machine=5 }
  Audience    : Audience enum       { MgaAnonymous=1, MgaStaff=2, MgaProducer=3, MgaCustomer=4, MgaMachine=5, MgaMcp=6 }
  Roles       : IReadOnlySet<string>  (population-scoped; empty for Anonymous)
  AgencyId    : Guid?               (Producer / Machine only)
  CustomerId  : Guid?               (Customer only; equals PrincipalId by construction)
  IssuedAt / ExpiresAt : DateTimeOffset
  Source      : discriminated source marker (cookie | token)
```

> **Amendment (2026-07-25):** this direction was not taken — `NorsePrincipal`/`Population` were never built (zero hits repo-wide outside stale docs). Heimdall's shipped shape has no principal-envelope type; `IAuthenticationService` handlers return `Outcome<T>` directly. See `../../../../Heimdall/CLAUDE.md`.

No `TenantId` — stamp-per-tenant means each stamp is its own IdP and principals never cross stamps. Enum values are explicit integers per CLAUDE.md §5; `0` is reserved sentinel. A handler reads `principal.Population`, never `principal.HasClaim("population", "Customer")`.

### 5.3 Population taxonomy

Populations are mutually exclusive; a principal has exactly one. Transitions (registration, role assignment) are server-side actions producing a new cookie/token. Authorization discriminates on `Population` first, then `Roles` within it. The `Population` values are deliberately generic B2B2C shapes ("Producer" = channel partner) so they survive a multi-vertical aspiration (ruling 1.2).

### 5.4 Anonymous→customer promotion

Promotion is an UPDATE in spirit, INSERT in implementation: the customer record is created with `customer_id = anonymous_principal_id`. The anonymous cookie is replaced with a customer-population cookie carrying the **same `PrincipalId`**. Every event, audit row, and operational record already tagged with that id is retroactively "the customer's" — the id never changed. Because registration is server-rendered inside the OAuth flow (§4.1), the promotion reads the anonymous cookie **server-side in the same host** — there is no cookie to thread through a gRPC call.

**First-write-wins is mandatory:** registration on an id that has already been promoted is rejected, foreclosing a stolen-cookie re-registration attack. (The OG approximated this with `FindByIdAsync(Id) == null ? Id : NewGuid()`; the spec makes it an explicit rejection, not a silent new-id fallback — no silent fallbacks, CLAUDE.md §2.7.)

### 5.5 Logout identity behavior — new UUID for everyone (RULED 2026-06-07)

The OG kept a customer's principal id alive on logout (new id only for employees). **The spec overrides this.** Logout — for staff and customer alike — :

1. Revokes the server-side session record (the live cookie-session record in Mongo, §10).
2. For token clients, revokes the token (and its refresh family).
3. Clears the authenticated cookie.
4. Issues a fresh anonymous cookie with a **brand-new** UUID v5 — never the principal's old id.

The customer's id stays bound to their customer row; the post-logout anonymous principal is a clean slate. Keeping a customer's id alive in a new anonymous cookie leaks it to the next user on a shared device — the safer behavior wins, and the staff/customer distinction the OG drew disappears.

---

## 6. `IAccountApi` — The Lifecycle Surface in Detail

`IAccountApi` lives in `Norse.Auth.Contracts`, is annotated `[MediatorService]`, and is dispatched by the source-generated `AuthService` forwarder (the generated mediator forwarder — not the OG/Assurely hand-written `AuthService` this spec retires) inside `Norse.Auth.Server` (mediator §8). Handlers run against the Mongo Identity stores (`UserManager`/`SignInManager` over the custom `IUserStore`/`IRoleStore` — §10). No EF, no DbContext, no `HttpContext`.

> **Amendment (2026-07-25):** this direction was not taken. `IAccountApi` never shipped. What shipped instead, narrower in scope, is Heimdall's `IAuthenticationService` (`Norse.AuthN.Services`, `[GenerateGateway]`) — `Login`/`Register`/`Logout` only; no `ConfirmEmail`/`ForgotPassword`/`ResetPassword`/`ManageProfile` members. See `../../../../Heimdall/CLAUDE.md`.

### 6.1 The members

| Member | Tier (service-entry) | Returns | Behavior |
|---|---|---|---|
| `ConfirmEmail` | `Authorized` | `Outcome` | Confirm the email token; **bump the security stamp** so the principal picks up `email_verified` on next validation. No `SignInAsync`. Sends a record-command to `.Worker`, which publishes `EmailConfirmedEvent`. |
| `ForgotPassword` | `Anonymous` | `Outcome` | Generate an Identity password-reset token; send a record-command to `.Worker`, which publishes `PasswordResetRequestedEvent` (the Notifications spec sends the mail). **Always `Outcome.Ok`** — never reveal whether the email exists. |
| `ResetPassword` | `Anonymous` | `Outcome` | Validate the reset token, set the new password. **No auto-sign-in** (fresh login is safer). Uniform response whether or not the account exists. Bump the security stamp (invalidates existing sessions). |
| `ManageProfile` | `Verified` | `Outcome<Profile>` | Update profile fields (name, phone, timezone). No email-verification round-trip (already verified). |

### 6.2 Why `ConfirmEmail` is `Authorized`, not `Anonymous`

The OG gated `ConfirmEmail` behind `Authorized` ("we only want someone who has logged in to be able to verify their email"). That intent is carried: the confirm-email link lands the user in an authenticated session first, then confirms. The security-stamp bump then refreshes the principal's `email_verified` claim, promoting them from `Authorized` to `Verified` capability on the next validation cycle — without a manual sign-in. (The exact tier nuance — whether confirm requires an authenticated session or can run anonymously off the token alone — is the kind of detail the authorization-model spec finalizes; the working stance is `Authorized`, matching the OG.)

### 6.3 What `IAccountApi` deliberately does **not** carry

- **No `Register`** — server-rendered inside the OAuth flow (§4.1).
- **No `Login` / `Logout`** — OAuth/cookie issuance (§3.1).
- **No `EmailRegistered`** — it was an account-enumeration oracle (any caller could probe whether an email had an account). The server-rendered signup page answers "email already in use" **in-process**, never as an API.
- **No `GetExternalLogins`** — that is *static configuration* (which social providers are wired), not user data; the server-rendered authorize page renders its own provider buttons. The client never enumerates it over gRPC.
- **No `GetClaims`** — the JWT already carries claims (MAUI reads them locally); cookie clients have `HttpContext.User` server-side. Redundant. (Fork 5.a resolved: drop. If a server-authoritative on-demand claims read is ever genuinely needed, it returns as a typed query then — not a string-KVP bag.)
- **No `Validate`** — `SecurityStampValidator` is built-in.

### 6.4 Password-reset token vs. magic-link table

Two single-use mechanisms, two jobs, no conflation:

- **Password reset** uses Identity's purpose-built `GeneratePasswordResetTokenAsync` (stateless, data-protection-bound, security-stamp-aware). Reset is `IAccountApi.ResetPassword`.
- **Enrollment / passwordless bootstrap** (producer enrollment, customer-service or producer-initiated handoff — the legitimate intent behind the OG's `LeadLogin`) uses the server-side **magic-link table** (Mongo + TTL per 1.8): opaque 32-byte token, SHA-256-hashed at rest, single-use, 15-minute lifetime, `purpose` ∈ `{ Enrollment | Reauthentication | Action }`. The OG `LeadLoginAsync` — a credential-free sign-in keyed on `LeadId` + `PartnerCode` — is **deleted**: it is replaced by a server-issued single-use magic link, never a direct sign-in call.

---

## 7. Per-Population Sign-in Flows (carried from 05-20, reconciled)

### 7.0 Anonymous bootstrap

First GET without a cookie: middleware generates a UUID v5 under the auth-context namespace, builds a signed cookie (`Population = Anonymous`, `Audience = MgaAnonymous`), attaches it, and the request proceeds with an anonymous principal already in `HttpContext.User`. No OpenIddict round-trip — the cookie is signed by the Data Protection key ring directly. Rate-limited per source IP (60/min default).

### 7.1 Staff (A) — Google federation

One button: **Sign in with Google**. No local-account fallback. OIDC Authorization Code to `accounts.google.com` with `hd={company}.com`. Callback validates the ID token, auto-provisions on first sign-in with `Population = Staff` and **no roles** (an admin assigns roles before anything role-gated works). MFA enforced upstream by Workspace policy.

### 7.2 Producer (B) — invite-only, agency-scoped

Staff create an agency and seed an `AgencyAdmin`. The seeded user gets a single-use **enrollment** magic link → sets a password, enrolls TOTP (mandatory) → active. Routine sign-in is email + password + TOTP (no magic-link convenience path for producers — they transact on behalf of an agency; the explicit credential path is correct). Agency admins invite further users via the agency-admin screen (same enrollment flow). No social login.

### 7.3 Customer (C) — local primary, social additive, magic-link bootstrap

Three entry paths: (1) **self-service registration** — server-rendered signup inside the OAuth flow, promotes the anonymous principal (§5.4), first-write-wins; (2) **magic-link bootstrap** — customer-service-initiated single-use link; (3) **producer-initiated bootstrap** — same magic-link mechanism, email originates from the insurance product. Sign-in is email + password (+ TOTP if enrolled); failed sign-in gives a uniform response for "no such account" and "wrong password." **Social is strictly additive** (§8.1): link from inside a signed-in session; an unlinked social subject is rejected with guidance, never auto-matched by email or auto-created. Apple specifics acknowledged (private-relay email stored as-is; name captured only on first sign-in).

### 7.4 M2M (D) — client credentials

Staff create an `application` (friendly name, owning agency nullable, allowed scopes, lifetime profile). `client_id` is a **Guid** (§9); `client_secret` shown once, stored hashed. Partners POST `grant_type=client_credentials` → JWT (`Audience = MgaMachine`). Secret rotation is a first-class admin action with a configurable overlap window. Per-agency M2M auto-provisioned disabled; agency admin opts in.

### 7.5 Fronting carrier (E) — V1 = M2M, V2 federation slot

V1: ordinary M2M client scoped to bordereaux feeds. V2 hook: `IUpstreamFederationHandler` with `CarrierKeycloakHandler [V2Slot]` (throws `NotSupportedException`); V2 is implementing the handler + configuring the upstream, no redesign.

---

## 8. Cross-Cutting Policies (carried from 05-20)

- **8.1 Account linking — strict.** A social subject binds to a customer **only** via an authenticated linking action from inside the signed-in session. Never auto-merge by email. Forecloses social-hijack account takeover and silent account fork. Subject already linked elsewhere → reject; provider email differs from local → linkable but UI surfaces the discrepancy (no silent acceptance).
- **8.2 Email normalization.** Case-insensitive local part, stored lowercased. Plus-addressing stripped for uniqueness in prod (`AuthOptions.AllowPlusAddressing = false` prod / `true` dev; no per-request override). Gmail dot-normalization **not** implemented (provider-specific quirk).
- **8.3 Magic-link mechanics.** Server-side opaque token (32 bytes), SHA-256-hashed at rest, stored in Mongo with a **TTL index** (ruling 1.8 — operational, system of record, no Postgres TTL churn): `principal_id`, `purpose`, `expires_at`, `consumed_at`. 15-minute lifetime, single-use. Rate-limited: ≤3 outstanding per principal, 5 issuances/hour/principal.
- **8.4 MFA by population.** Staff — mandatory upstream (Workspace). Producer — mandatory TOTP at enrollment. Customer — optional in V1 (authorization-model spec finalizes when mandatory; working assumption: mandatory before binding a policy). M2M — N/A (secret rotation + per-client scope).
- **8.5 Cookie security / CSRF.** `Secure` always, `HttpOnly` always, `SameSite = Lax` (OAuth callbacks are top-level navigations; `Strict` breaks first sign-in). ASP.NET Core antiforgery on every non-idempotent endpoint — layered with, not replaced by, the cookie scheme.
- **8.6 Signout.** Per §5.5 — revoke the live session record (Mongo), revoke tokens (token clients), clear the cookie, issue a fresh anonymous cookie with a new UUID v5. **Global logout** (all sessions for a principal) is a staff-admin action. Token revocation at `/connect/revoke` (RFC 7009).
- **8.7 Rate limiting.** Anonymous bootstrap 60/min/IP; sign-in 10/min/IP + 5/hr/email; OIDC callback 30/min/IP; magic-link 3 outstanding + 5/hr/principal; token endpoint 60/min/`client_id`; revocation 60/min/`client_id`. Lockout: 15 min after 5 failures within 1 hour; lockout is a logged (and projected) event.

---

## 9. Webhook Client Modeling (5.8 absorption)

Auth owns the OpenIddict client modeling the webhook authentication handlers (hosting spec §7.1, ruling 5.7) depend on:

- **Guid `client_id`s for partner/producer/machine clients**, by registration convention. The `client_id` doubles as the **webhook UUID v5 namespace** (5.7's unification — no separate per-partner namespace registry; OpenIddict's application store *is* the partner registry).
- **Webhook config lives as OpenIddict application properties**, on the Mongo application store: a queryable `partner_code` (route-lookup key, Mongo-indexed), the capability tier (`none | signature | client-credentials`), the signing secret as an **`EncryptedString`** (signature tier — explicitly **not** the hashed `client_secret`, which is unrecoverable and serves only the token flow), and the IP allowlist (whitelist tier).
- **`Norse.Auth.Server` implements `IWebhookClientResolver`** (`partnerCode` → `WebhookClient`) over the OpenIddict application store. The contract is Abstractions-tier (hosting); the implementation is Auth's. Route partner-code is untrusted until the looked-up client's signature/IP check passes.
- **The three `WebhookSchemes`** (`ClientCredentials` JWT bearer / `Signature` HMAC / `Whitelist` source-IP) register in the web host alongside the OpenIddict bearer scheme; auth and hosting agree on scheme names. Signing-secret storage coordinates with the `EncryptedString` spec when it lands.

---

## 10. Identity Storage — Mongo SoR, Postgres Projection

**Mongo is the system of record for identity** (deliberate inversion of the platform default; ruling 2026-06-03). The hot path — credential verification, token issuance, password reset — is synchronous web-tier work, and a queue cannot sit in the login path. `Norse.Auth.Server` runs OpenIddict's first-party MongoDB stores and ASP.NET Core Identity against custom `IUserStore<TUser>` / `IRoleStore<TRole>` over the same per-context Mongo database. **No EF, no entity classes, no DbContext in `.Server`** — the hard wall holds with no exemption.

**Split by durability (ruling 1.8):** Postgres holds durable, audit-grade history; Mongo owns TTL-churning operational state. TTL workloads in Postgres are rejected outright (no expiry-cleanup jobs, no vacuum churn on the reporting store).

| Concern | Store | Shape |
|---|---|---|
| Credentials, users, roles, OpenIddict apps/tokens | **Mongo** | System of record; `.Server`-owned; never on events; never in Postgres |
| Magic-link tokens (issue/redeem hot path) | **Mongo, TTL index** | Operational SoR; single-use |
| Live cookie-session records (per-request revocation check) | **Mongo, TTL index** | Operational SoR |
| Session lifecycle history (sign-in, sign-out, revocation, global logout) | **Postgres `auth` projection** | Worker-written, insert-only stance, no TTL |
| Auth failures + lockouts | **Postgres `auth` projection** | Worker-written, insert-only — durable security audit |

**The messaging topology rule governs the handoff: `.Server` sends commands; only `.Worker` publishes events.** `Norse.Auth.Server` does the authoritative Mongo write synchronously, then **sends a record-command** to `Norse.Auth.Worker` (`IMessageSession.Send`) — by which point the request is already authenticated and authorized at the door, so the command handler re-checks nothing. `Norse.Auth.Worker` handles the command, **writes the Postgres `auth` projection** (the entity classes + `IEntityTypeConfiguration<T>` live here; `Norse.Infrastructure.Persistence`'s `AuthDbContext` backs them), **and publishes** the cross-context event (`PrincipalRegisteredEvent`, `PrincipalSignedInEvent`, `PrincipalSignInFailedEvent`, `PrincipalLockedOutEvent`, session-lifecycle events, `PrincipalLinkedExternalIdentityEvent`, `EmailConfirmedEvent`, …) for any other context that subscribes. **Password hashes, security stamps, MFA secrets, recovery codes, and refresh tokens are never on a command or an event and never reach Postgres.** The Postgres `auth` tables are read-only projections — written only by these worker handlers, never consulted for an auth decision. The record-commands themselves live in `Norse.Auth.Backend` (§11). **The projection schema name defaults to the service name `auth`** — exactly as the `billing` / `claims` / `policy` sibling services name their schemas — **and is specifiable**; Infrastructure owns the override knob, since it already chooses the connection string and isolation posture (§4 → Persistence: shared-connection schema isolation vs. distinct DB).

---

## 11. Auth's Project Shape — Standard, Including `.Backend` (RULED 2026-06-07, corrected)

Auth's authority lives **entirely in `.Server`**: by the time `.Server` has rendered judgment (verified the credential against the Mongo SoR, issued the token, minted the cookie), it is done deciding. But `.Server` **cannot publish events** — the messaging topology rule is absolute: the server *sends commands*, only the worker *publishes events* (§10). So `.Server` "passes the info on" by **sending a record-command** to `.Worker`; the worker writes the Postgres projection and publishes the cross-context event.

Those server→worker commands are the canonical content of `.Backend`. So **Auth ships a `.Backend`** — and Auth is an ordinary, standard-shaped context (all six projects). The *persistence direction* is inverted (Mongo SoR in `.Server`, Postgres projection in `.Worker`), but the *assembly shape* is normal. The mechanical project-shape law (`project-structure.md`: `.Backend` exists iff `.Server` and `.Worker` both exist) holds with **no refinement** — an earlier draft proposed a "purely event-driven handoff" exception, but that exception was built on a false premise: there is no event-driven server→worker handoff, because the server can't publish. (The decision-inputs §5.b "no `.Worker`, therefore no `.Backend`" premise was likewise falsified by ruling 1.8 — Auth *has* a projection worker.)

**Auth's assemblies:**

- `Norse.Auth.Contracts` — published auth events (`PrincipalRegisteredEvent`, `PrincipalSignedInEvent`, …); `IAccountApi` + its request/response records. `Audience`/`Population`/`NorsePrincipal` are in `Norse.Abstractions.Identity` (1.2), not here. The single project other contexts may reference.
- `Norse.Auth.Components` *(declared per item 2.17)* — Blazor login/signup/TOTP/social-link/consent pages, account-settings and admin screens. WASM/MAUI-bundlable; references `Server`/`Worker`/`Backend` — none. Shell references it for the login/profile surface.
- `Norse.Auth.Backend` — the server→worker **record-commands** (`RecordPrincipalRegisteredCommand`, `RecordSignInCommand`, `RecordSignInFailedCommand`, `RecordLockoutCommand`, `RecordPasswordResetRequestedCommand`, session-lifecycle record-commands, …) and any shared server-side constants/options (auth scheme-name constants). Referenced by `.Server` (sends them) and `.Worker` (handles them); never client-reachable. **Carries no Mongo document records** — the Mongo SoR is `.Server`-only here (the inversion), unlike a normal context where `.Backend` also holds the server-serves/worker-writes Mongo shapes.
- `Norse.Auth.Server` — OpenIddict server config, the authorize pages' server logic, Mongo Identity stores (`IUserStore`/`IRoleStore`), `IAccountApi` handlers (the generated `AuthService` mediator forwarder — not the OG/Assurely hand-written `AuthService`), federation handlers, magic-link issuance, signing-key rotation, `IWebhookClientResolver` impl, the `AuthPlugin : IWebHostPlugin`. The authority tier. Sends record-commands; never publishes events.
- `Norse.Auth.Worker` — the Postgres projection: `auth`-schema entities (schema name defaults to the service name, specifiable — §10), `IEntityTypeConfiguration<T>`, the record-command handlers (which write the projection **and** publish the cross-context events), the `AuthWorkerPlugin : IWorkerHostPlugin`. Strictly downstream of `.Server`'s judgment.
- `Norse.Auth.Migrations` *(item 2.11)* — **projection tables only** (`auth` schema: session lifecycle, failures, lockouts). Independently versioned NuGet, applied by `Norse.Hosting.Migrations.Service`. **Not** identity tables (those are Mongo).

**Heimdall is a top-level codenamed realm, not a `{Company}.{Context}`** (RULED 2026-06-07). This honors CLAUDE.md §6 rule #5 ("do not codename a bounded context"): Heimdall is **not** an MGA business context — it is cross-cutting authentication/authorization infrastructure consumed by the insurance and energy product realms alike, so it cannot nest under either, and it earns a codename as a platform service (§5 future-codenamed-services rule: load-bearing, separable, depended-on-by-every-realm → own top-level namespace).

**Codename reassignment** (RULED 2026-06-07): `Heimdall` was previously assigned to **observability**; it moves to the auth realm here — the watchman who guards the one crossing and knows every identity is the more exact fit for authentication/authorization than for telemetry. **Observability becomes `Gjallarhorn`** (Heimdall's alarm-horn — the telemetry/alert signal; resolved in the same change set; see `codenames.md` and the CLAUDE.md §3 amendment). The federation spec's tentative `Var` candidacy is moot. The energy product realm (the peer that, with the insurance product, consumes Heimdall) needs its own registry entry once its purpose is recorded.

---

## 12. Token Formats, Lifetimes, Keys (carried from 05-20)

| Surface | Format | Validated by |
|---|---|---|
| Browser session (portal, Blazor) | Signed cookie (Data Protection key ring) | Cookie auth middleware in `Norse.Hosting.Web.Server` |
| Anonymous browser session | Same scheme, `Population = Anonymous` | Same middleware |
| MAUI (Blazor Hybrid) | JWT access + refresh (secure storage) | `JwtBearer` middleware, local JWKS |
| API consumer (M2M, partner, MCP) | JWT access + refresh | `JwtBearer` middleware, local JWKS |
| Inter-context same-process | None — `HttpContext.User` flows in-process | n/a (single host) |

**Lifetimes:** anonymous cookie 30 d sliding; authenticated cookie 12 h sliding to 7 d max; access token (JWT) 15 min; refresh token 14 d (portal) / 90 d (MAUI native), **rotation on each use with reuse detection** (OAuth 2.1 §6.1, mandatory, no override — reuse revokes the whole token family); magic link 15 min single-use.

**Signing:** RS256 asymmetric only; private key in Azure Key Vault (prod) / Data Protection ring (dev); 90-day rotation with a 30-day JWKS overlap window; discovery at `/.well-known/openid-configuration`, `/jwks.json`, `/oauth-authorization-server`. Resource servers cache JWKS; unknown `kid` → 401 with logged reason, no silent fallback.

---

## 13. Decisions Resolved, Superseded, and Amendments Spawned

### Resolved by this spec
- **The auth service surface** (the federation spec's deferred "identity-contract" question): issuance = OAuth/cookie; lifecycle = `IAccountApi` → `Outcome<T>`; `AuthResponse`/`AuthResult` retired; no `SignInAsync` in handlers.
- **§1.6 "auth uses `Result<T>`?"** — account lifecycle uses `Outcome<T>`; issuance uses OAuth tokens/cookies (no result type — issuance is not a result).
- **Registration model** — server-rendered inside the OAuth flow; no `IAccountApi.Register`; `EmailRegistered` + `GetExternalLogins` deleted as APIs.
- **Logout identity** — new UUID for everyone (overrides the OG; §5.5).
- **Browser cookie minting** — same-host `SignInAsync` shortcut; full-BFF deferred behind a documented re-entry trigger.
- **Auth project shape** — standard six-project shape **including `.Backend`** (server→worker record-commands); persistence inverted, assembly shape ordinary. The project-shape law is **unchanged** — `.Server` sends commands, `.Worker` publishes events (§10, §11).
- **Auth is a top-level codenamed realm `Heimdall`** — not `{Company}.Auth.*`; cross-cutting infrastructure consumed by the insurance and energy product realms (§11, title note). **`Heimdall` is reassigned from observability**, which becomes `Gjallarhorn` (CLAUDE.md §3 + `codenames.md` amendment, same change set). `{Company}.Shell.Components` now references `Norse.Auth.Components` for the login/profile surface.
- **Postgres schema name** defaults to the service name (`auth`), specifiable via Infrastructure (§10) — consistent with billing/claims/policy sibling schemas.

### Superseded
- `2026-05-20-auth-federation-design.md` in full (carried forward + extended). Its inline 2026-06-03/06-04 amendments are now consolidated here.

### Amendments this spec spawns (for the reconciliation tracker)
- **No project-shape law change** — Auth obeys the existing law (`.Backend` iff both halves exist). The messaging topology rule (`.Server` sends commands, `.Worker` publishes events) is the load-bearing constraint; confirm it is stated as a first-class rule in the messaging foundation spec and cross-referenced where the `.Backend` shape is described.
- **Tracker 2.11** — `Norse.Auth.Migrations` = projection tables only. **Discharged here** (§11).
- **Tracker 2.17** — `Norse.Auth.Components` declared. **Discharged here** (§11).
- **Tracker 5.8** — webhook client modeling absorbed. **Discharged here** (§9).
- **Decision-inputs doc** (`2026-06-07-auth-result-shape-decision-inputs.md`) — discharged; mark it resolved-by-this-spec.
- **`YGG110`** (forbid `[AllowAnonymous]`) remains queued for the analyzers catalog (tracker 2.10).
- **Notifications spec (4.1)** gains the `PasswordResetRequestedEvent` / `EmailConfirmationRequestedEvent` consumers (`Norse.Auth.Worker` publishes; Notifications sends).
- **`EncryptedString` spec (4.3)** — webhook signing-secret and MFA-secret storage coordinate when it lands.

---

## 14. Follow-up Work (downstream specs, in pickup order)

1. **`YGG110` analyzer** — forbid `[AllowAnonymous]` solution-wide (analyzers catalog, tracker 2.10).
2. **Authorization-model spec** — RBAC/ABAC/policy; role + scope taxonomy per population; cross-population impersonation (staff support → customer); finalize the `ConfirmEmail` tier nuance (§6.2) and when customer MFA becomes mandatory.
3. **Auth admin UIs spec** — staff admin, agency admin, M2M client management (UI-composition territory).
4. **Customer self-service UX spec** — registration/MFA/account-settings screens (UI-composition territory).
5. **Notifications spec** — confirm/reset email delivery; the auth events above are its inputs.

---

## Appendix A — The OG `AuthService`, Method by Method

The reference-implementation teaching table. Buvy's prior-platform (Assurely) gRPC `AuthService` is the ancestor; each method's verdict shows the pit-of-success refactor and *why*.

| OG method | OG behavior | Verdict | New home |
|---|---|---|---|
| `LoginAsync` | email+password → `CheckPasswordAsync` → `SignInAsync` | **Delete** — textbook ROPC, forbidden in OAuth 2.1 | Auth Code + PKCE → `/connect/token` (token clients) / same-host `SignInAsync` (browser) |
| `LogoutAsync` | `SignInAsync(employee ? newGuid : Id)` | **Delete** — pure issuance; also behavior overridden (§5.5) | OAuth logout: revoke + clear cookie + new anonymous UUID for all |
| `RegisterAsync` | create user, carry anon id, `SignInAsync` | **Delete as a method** — issuance welded to creation | Server-rendered signup inside the OAuth flow (§4.1); promotion server-side (§5.4) |
| `LeadLoginAsync` | `SignInAsync(LeadId, PartnerCode)` | **Delete — security hole** (credential-free sign-in) | Server-issued single-use magic link (§6.4, §7.3) |
| `ValidateAsync` | nonce-claim → security-stamp check | **Delete** — built-in | `SecurityStampValidator` |
| `GetClaimsAsync` | return all claims as KVPs | **Delete** — JWT/cookie already carries them | — (fork 5.a: drop) |
| `GetExternalLoginsAsync` | enumerate social schemes | **Delete** — static config, not user data | Server-rendered authorize page renders its own buttons |
| `EmailRegisteredAsync` | bool: does this email exist | **Delete — enumeration oracle** | Server-side "email in use" inside signup |
| `ConfirmEmailAsync` | confirm + `SignInAsync` to refresh claim | **Keep as lifecycle; strip `SignInAsync`** | `IAccountApi.ConfirmEmail` → `Outcome`; security-stamp bump |
| `ForgotPasswordAsync` | find user, send reset mail, don't reveal | **Keep**; don't-reveal correct (`Outcome.Ok`) | `IAccountApi.ForgotPassword` → `Outcome`, `Anonymous` tier |
| `ResetPasswordAsync` | reset + `SignInAsync` (auto-login) | **Keep; drop auto-login; uniform unknown-user response** | `IAccountApi.ResetPassword` → `Outcome`, `Anonymous` tier |
| `ManageAsync` | (NotImplemented) profile mgmt | **Keep as lifecycle** | `IAccountApi.ManageProfile` → `Outcome<Profile>`, `Verified` tier |

Cross-cutting OG residue retired: `AuthResponse`/`AuthResult` (side-effect receipt), `BoolResponse`, `ServerGrpcServiceBase`'s `HttpContext`-reading `User`/`Id` accessors (→ `NorsePrincipal.PrincipalId` / service-entry authz), and the `// TODO: refactor into using MediatR` (→ the source-generated mediator). The timezone-at-registration NodaTime IANA-alias cleanup becomes a signup-page field feeding `ManageProfile`/registration, not a wire concern.

---

## 15. References

- CLAUDE.md §2 (decision rules), §3 (bounded contexts), §4 (hosting, auth, persistence, tenancy), §5 (naming, project shape), §7 #3 (auth federation — RESOLVED), §8 (anti-patterns).
- `2026-05-20-auth-federation-design.md` (superseded — federation map, principal model, token formats, cross-cutting policies carried forward).
- `2026-06-07-auth-result-shape-decision-inputs.md` (discharged).
- `2026-05-26-mediator-design.md` (`Outcome<T>`, service-entry authorization, `I{Context}Api` dispatch).
- `2026-06-03-messaging-foundation-design.md`; `2026-05-21-midgard-persistence-design.md`; `2026-06-03-tenancy-model-design.md`; `2026-05-20-yggdrasil-hosting-design.md` (webhook auth handlers).
- `spec-reconciliation-2026-06-04.md` §1.2, §1.6, §1.8, §2.11, §2.17, §5.7, §5.8.
- OAuth 2.1 draft (`draft-ietf-oauth-v2-1`); OpenID Connect Core 1.0; RFC 7009 (revocation); RFC 7591 (DCR, deferred); RFC 8414 (AS metadata); NIST SP 800-63B; Anthropic MCP authorization profile.
