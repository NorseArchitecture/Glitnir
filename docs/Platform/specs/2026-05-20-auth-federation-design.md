# Auth Federation — Yggdrasil Identity Topology Design

**Date:** 2026-05-20
**Status:** SUPERSEDED 2026-06-07 by `2026-06-07-auth-design.md` (federation topology, principal model, token formats, and cross-cutting policies carried forward intact; the service surface, issuance/lifecycle split, identity-storage inversion, and webhook client modeling were added there, and all post-05-20 rulings consolidated). Retained as history.
**Owner:** Buvy
**Supersedes:** none
**Companion specs:** `2026-05-19-architecture-analyzers-design.md` (will gain `YGG110` to forbid `[AllowAnonymous]`); future `auth-identity-contract-design.md` (claim shape detail, role taxonomy); future `auth-authorization-model-design.md` (RBAC/ABAC/policy decisions)

---

## 1. Motivation

CLAUDE.md §7 #3 flags auth federation as a decision required *before* any auth code is written. Federation choices reach into every bounded context: every product API, every MCP server, every event handler, every audit row eventually depends on the shape of the principal flowing through the request. Picking a topology after consumers have settled on a claim shape is the most expensive auth decision a platform can make.

The platform's user populations are heterogeneous — internal staff, agency-scoped producers, retail customers, machine consumers, and the fronting carrier — and have wildly different lifecycles, scales, and trust models. A topology that's correct for one population can be hostile to another. This spec settles the federation shape for **all** of them in one stroke so downstream specs (the identity contract, the authorization model, individual contexts' API surfaces) have firm ground.

This spec also commits to one architectural choice that is unusual enough to be load-bearing: **every Norse surface receives a non-null principal, including unauthenticated visitors.** Anonymous-as-principal is foundational to how downstream contexts attach event-sourced rows, audit trails, and abandoned-flow recovery to a stable id. Picking that posture later means rewriting every context's audit and event-sourcing assumptions.

The work here is deliberately ADR-style: settle topology and the principal contract, defer claim-detail and authorization-model questions to dedicated specs. Per CLAUDE.md §2.5, scope is set to the dragon we know — a curated MGA at launch, with documented re-entry points if scope grows.

---

## 2. Scope and Non-Goals

### In scope (this spec)

- The federation map: which user populations exist, where their identity lives, which upstream IdPs we federate to.
- The authorization-server topology: single OpenIddict instance, single issuer, audience-scoped tokens.
- The principal model: every request carries a non-null `NorsePrincipal`; anonymous is a real, signed identity with a stable UUID.
- Token formats by surface (signed cookie vs. JWT), lifetimes, refresh-rotation policy.
- Per-population sign-in mechanics at the federation level (who federates where, how registration works).
- OAuth 2.1 + MCP requirements: pre-registered Claude Desktop client, no DCR in V1.
- Cross-cutting policies that affect federation security: account linking, email normalization, magic-link mechanics, MFA per population, cookie/CSRF flags, signout, rate limiting.

### Out of scope (separate specs)

- **Detailed claim shape** — exact role names per population, scope taxonomy, claim-enrichment via `IClaimsTransformation`. (Identity-contract spec.)
- **Authorization model** — RBAC vs. ABAC vs. policy-based; who-can-do-what *within* a population. (Authorization-model spec.)
- **Password policy specifics** — minimum length, breach-list integration, lockout backoff. NIST 800-63B alignment is the working recommendation; detail in a later spec.
- **Audit log schema and retention** — `auth_events` table shape, retention by event kind, regulatory alignment.
- **Self-service customer UX** — registration screens, MFA setup, account-settings UI. (UI-composition territory.)
- **Admin UIs** — staff admin, agency admin (producer self-management), M2M client management. (UI-composition territory.)
- **Dynamic Client Registration** (DCR) for MCP — deferred; spec re-opened when MCP expands beyond staff.
- **Producer SAML federation, customer IAM vendor adoption** — deferred. Re-enter this spec if B-scale producers or C-scale customer counts emerge.
- **Tenancy model** (CLAUDE.md §7 #4) — *resolved 2026-06-03: stamp-per-tenant (`2026-06-03-tenancy-model-design.md`). Each stamp runs its own OpenIddict, so principals never cross stamps and `NorsePrincipal` carries no tenant claim — the `TenantId` slot §5 originally reserved is removed. A future shared-compute model re-adds it per the tenancy spec's §6 re-entry triggers.*

---

## 3. Architecture Overview

One OpenIddict instance, one issuer URL, direct OIDC federation to upstream identity for the only population that has one (staff via Google Workspace). Everything else — producers, customers, machine clients, MCP — is local in OpenIddict's stores. No identity broker (no Keycloak in front), no per-population issuers, no per-context auth servers.

```
                                ┌──────────────────────────────┐
                                │      Google Workspace        │
                                │  ({company}.com domain only) │
                                └─────────────┬────────────────┘
                                              │ OIDC code flow
                                              │ (hd={company}.com)
                                              ▼
┌──────────────────────────────────────────────────────────────────────┐
│                       Norse.Hosting.Web.Server (single deployable)               │
│                                                                      │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │  Norse.Auth (Plugin)                                         │  │
│  │  ─────────────────────                                         │  │
│  │  OpenIddict authorization server                               │  │
│  │  Issuer:  https://auth.{company}.com                           │  │
│  │  Discovery:                                                    │  │
│  │    /.well-known/openid-configuration                           │  │
│  │    /.well-known/oauth-authorization-server                     │  │
│  │    /.well-known/jwks.json                                      │  │
│  │                                                                │  │
│  │  Federation handlers:                                          │  │
│  │    GoogleOidcHandler              (wired V1)                   │  │
│  │    AppleSignInHandler             (wired V1, customer use)     │  │
│  │    CarrierKeycloakHandler [V2Slot] (placeholder, throws)       │  │
│  │                                                                │  │
│  │  Stores: Postgres (auth schema), EF Core                       │  │
│  │  Signing: RS256 via Azure Key Vault (prod) / Data Protection   │  │
│  │           ring (dev); 90-day rotation, 30-day overlap          │  │
│  └────────────────────────────────────────────────────────────────┘  │
│                                                                      │
│  Other context plugins (Policy, Claims, Billing, Customer, …)        │
│  validate JWTs locally via cached JWKS; receive NorsePrincipal     │
│  through HttpContext.User in the same process.                       │
└──────────────────────────────────────────────────────────────────────┘
                                  ▲
                                  │ tokens (cookie or JWT)
                                  │
   ┌──────────────────────────────┼──────────────────────────────┐
   │                              │                              │
   ▼                              ▼                              ▼
Browser session            Native (MAUI Blazor)            API consumers
(cookie auth)              (cookie via WebView)            (JWT bearer)
                                                           ├ Cross-context
                                                           ├ Partner / carrier
                                                           ├ Producer AMS
                                                           └ Claude Desktop (MCP)
```

The Auth context lives at `Norse.Auth.*` and follows the standard three-assembly bounded-context layout per CLAUDE.md §5:

- **`Norse.Auth.Contracts`** — `NorsePrincipal` envelope, `Population` enum, `Audience` enum, claim-name constants, the published `AuthenticationEvent` types (`PrincipalSignedInEvent`, `PrincipalRegisteredEvent`, `PrincipalLinkedExternalIdentityEvent`, etc.) that other contexts subscribe to.
- **`Norse.Auth.Components`** — Blazor sign-in pages, enrollment landing pages, magic-link redemption pages, account-settings screens (linked identities, MFA enrollment, password change), agency-admin user-management screens, staff admin user-management screens. WASM-bundlable; no infrastructure references.
- **`Norse.Auth.Server`** — OpenIddict server configuration, federation handlers, magic-link issuance service, signing-key rotation service, and the `AuthPlugin : Norse.Abstractions.Hosting.IWebHostPlugin` that registers everything with the Yggdrasil host runtime. *(Amended 2026-06-03 — identity storage posture decided.)* **MongoDB is the system of record for identity.** OpenIddict uses its first-party MongoDB stores; ASP.NET Core Identity runs against custom `IUserStore<TUser>` / `IRoleStore<TRole>` implementations over the same per-context Mongo database. No EF, no entity classes, no DbContext in this assembly — the `.Server` hard wall holds with no exemption. Credential verification, password reset, and token issuance are synchronous web-tier operations against Mongo.
- **`Norse.Auth.Worker`** *(added 2026-06-03)* — thin projection worker. Subscribes to Auth's own published events (`PrincipalRegisteredEvent`, `PrincipalSignedInEvent`, `PrincipalLinkedExternalIdentityEvent`, …) and projects them into the Postgres `auth` schema — the entity classes and `IEntityTypeConfiguration<T>` impls live here, and `Norse.Infrastructure.Persistence`'s `AuthDbContext` backs them — so Warehouse and reporting see auth data like any other context's. Password hashes, security stamps, MFA secrets, recovery codes, and refresh tokens are never on events and never reach Postgres. **This deliberately inverts the platform default** — everywhere else Postgres is the system of record and Mongo the derived read store; Auth flips it because its hot path is synchronous credential verification in the web tier, and a queue cannot sit in the login path. The Postgres `auth` tables are read-only projections, written only by these handlers and never consulted for an auth decision.

The Auth context is not codenamed. CLAUDE.md §3 describes Auth as a Plugin "like any other context" but doesn't list it as a bounded context. Codenaming is deferred. `Var` (goddess of oaths and contracts, in the §6 available list) is a candidate if Auth ever grows into something beyond identity plumbing; this spec does not adopt it.

---

## 4. Federation Map

The committed federation topology for V1:

| Population | Identity location | V1 federation | Federation notes |
|---|---|---|---|
| **A. Staff** | Google Workspace (upstream) | Direct OIDC, mandatory | `hd={company}.com` restricts the audience to the company's Workspace tenant. Google enforces the domain restriction; non-{company}.com users never reach the consent screen. |
| **B. Producers** | Local in OpenIddict | None | Agency-scoped local accounts. Agency admins manage users via a self-serve `Norse.Auth.Components` admin screen. |
| **C. Customers** | Local in OpenIddict + optional social | Per-user additive (Google, Apple) | Local credentials primary; social subjects bind to an existing customer principal via an authenticated linking action. |
| **D. M2M** | Client credentials in OpenIddict | N/A | One OpenIddict `application` row per machine consumer; per-client scopes; per-client secret rotation. |
| **E. Fronting carrier** | Same as D in V1 | Carrier Keycloak slot documented | The `Norse.Auth.Server` project ships `CarrierKeycloakHandler` as a placeholder. V2 wiring is configuration only — no architectural change. |
| **MCP** | Reuses A/B/C principal identity | OAuth 2.1 Auth Code + PKCE | V1 = staff only (A). Pre-registered Claude Desktop `client_id`; no DCR. Eventual rollout order: A → B → C. |
| **F. Anonymous** | Generated UUID v5, signed cookie | N/A | First contact issues a stable id. Registration promotes the anonymous principal to a real population without remap. |

### What the topology forecloses (intentionally)

- **Keycloak as an identity broker.** Keycloak earns its keep when the federation map needs 3+ upstream IdPs or SAML federation for large producers. Today's map has at most two OIDC upstreams in V2 (Google + the carrier's Keycloak) and one in V1 (Google). Adopting a broker pre-emptively is gold-plating, per CLAUDE.md §2.5.
- **Per-population issuers.** A single `aud` claim taxonomy discriminates between audiences; multiple issuers would force every resource server to maintain a trusted-issuer list (a value, not a setting), and would prevent cross-population flows (staff impersonating a customer for support) from being expressed naturally.
- **Silent identity fallback.** Per CLAUDE.md §2.7, every federation path either produces a populated principal or fails loudly with a logged reason. There is no "couldn't verify Google token; fall back to anonymous" path. The anonymous principal is a deliberate state, never a degraded one.

### Re-entry points (what would re-open this spec)

- A producer agency with >100 users demanding SAML SSO → Approach 2 from the brainstorming notes (broker pattern) re-opens.
- Customer count crossing into millions → customer-IAM vendor (Auth0 / Okta CIC / Cognito) becomes worth comparing.
- A third upstream IdP arriving (e.g., a partnership requiring federation from a partner's Workspace tenant) → broker pattern re-opens.
- Shared-compute multi-tenancy (re-entry triggers in `2026-06-03-tenancy-model-design.md` §6) → a `TenantId` claim is re-added to `NorsePrincipal`; federation handlers gain tenant-routing responsibility. Under the resolved stamp-per-tenant model (2026-06-03), neither exists — each stamp is its own IdP.

---

## 5. The Principal Model

### 5.1 Every request carries a `NorsePrincipal`

There is no anonymous-as-absent. Every Norse surface — portal page load, JSON API call, gRPC service call, message handler dispatched from a queue — receives a `NorsePrincipal` whose `PrincipalId` is non-null. ASP.NET Core's `[AllowAnonymous]` attribute does not appear in the platform; an analyzer rule (`YGG110`, added in a follow-up to the architecture-analyzers spec) flags any occurrence as a build error.

This posture is foundational, not stylistic. Downstream contexts (Reporting, Tyr, Observability, every event-sourced aggregate) get to attach rows to a stable principal id without dealing with nullable principals or "merge anonymous-cart-into-customer" logic at registration time. The principal id generated on first contact *is* the customer id post-registration. No remap, no orphan reconciliation, no audit re-tagging.

### 5.2 The envelope

`NorsePrincipal` lives in `Norse.Auth.Contracts`. It wraps a `ClaimsPrincipal` and exposes typed accessors so downstream code never reads string-keyed claims directly:

```
Norse.Auth.Contracts.NorsePrincipal
  - PrincipalId:    Guid              (the UUID v5 from the auth-context namespace)
  - Population:     Population enum   { Anonymous, Staff, Producer, Customer, Machine }
  - Audience:       Audience enum     { MgaAnonymous, MgaStaff, MgaProducer, MgaCustomer, MgaMachine, MgaMcp }
  - Roles:          IReadOnlySet<string>   (population-scoped; empty for Anonymous)
  - AgencyId:       Guid?             (Producer / Machine populations only)
  - CustomerId:     Guid?             (Customer population only; equals PrincipalId by construction)
  - IssuedAt:       DateTimeOffset
  - ExpiresAt:      DateTimeOffset
  - SourceCookie / SourceToken:  discriminated source marker
```

> **Amendment (2026-07-25):** this direction was not taken. `NorsePrincipal`/`Population` never shipped anywhere in the platform (confirmed by repo-wide grep) — the auth realm that shipped is Heimdall/Himinbjörg, whose `IAuthenticationService` (`[GenerateGateway]`) returns `Outcome<T>` directly, with no principal-envelope type of this shape. See `../../../../Heimdall/CLAUDE.md`.

The wrapping is mechanical, but the contract matters: a `Policy` context handler reads `principal.Population` rather than `principal.HasClaim("population", "Customer")`. Compile-time checks beat string-keyed lookups everywhere — CLAUDE.md §2.7.

### 5.3 The population taxonomy

Populations are mutually exclusive. A principal has exactly one population; transitions between populations require a server-side action (registration, role assignment) and produce a new principal binding to a new cookie or token.

| Population | Source of identity | Capabilities-at-base |
|---|---|---|
| `Anonymous` | UUID v5 generated under auth namespace, signed cookie | Browsing public pages; starting a quote; starting an FNOL; initiating registration |
| `Staff` | Federated from Google Workspace | Role-gated; zero capabilities until an admin assigns roles |
| `Producer` | Local OpenIddict account, agency-scoped | Acting on behalf of the bound agency; subset depends on assigned role within agency |
| `Customer` | Local OpenIddict account with optional linked social | Self-service over policies/claims/billing for that customer |
| `Machine` | OpenIddict `application` row (client credentials) | API surface gated by configured scopes |

Authorization policies discriminate on `Population` first, then `Roles` within that population. Cross-population authorization (staff support impersonating a customer) is an explicit, audited mechanism handled by the authorization-model spec, not an implicit role escalation.

### 5.4 Anonymous-to-real promotion

Promotion is a server-side UPDATE in spirit, INSERT in implementation: the customer row is INSERTed with `customer_id = anonymous_principal_id`. The previous anonymous cookie is replaced with a new customer-population cookie carrying the same `PrincipalId`. All events, audit rows, and operational state already tagged with that id are retroactively "the customer's" because the id never changed.

**First-write-wins** is mandatory: registration on an anonymous id that has already been promoted is rejected. This forecloses a stolen-cookie attack where an attacker re-registers an account onto an existing customer's id.

Logout from a customer (or any non-anonymous) population:
1. Revokes the server-side session record.
2. Clears the authenticated cookie.
3. Issues a fresh anonymous cookie with a **new** UUID — not the customer's old principal id. The customer's id remains bound to the customer row; the new anonymous principal is a clean slate.

---

## 6. Token Formats and Lifetimes

### 6.1 Two formats, no more

| Surface | Format | Validated by |
|---|---|---|
| Browser session (portal, Blazor) | Signed cookie (ASP.NET Core cookie auth scheme; Data Protection key ring) | Cookie auth middleware in `Norse.Hosting.Web.Server` |
| Anonymous browser session | Same scheme, `Population = Anonymous`, `Audience = MgaAnonymous` | Same cookie auth middleware |
| MAUI client (Blazor Hybrid) | Same signed cookie via embedded WebView | Same cookie auth middleware |
| API consumer (cross-context, partner, MCP) | JWT access token (RS256) + refresh token | `JwtBearer` middleware, local JWKS validation |
| Inter-context same-process | None — `HttpContext.User` flows in-process | n/a (single host) |

No "JWT-as-cookie" hybrid, no opaque/reference tokens, no per-context bespoke schemes. Two formats, picked by surface, validated by stock middleware. The single-host topology (CLAUDE.md §4) lets in-process cross-context calls skip token round-trips entirely.

### 6.2 Lifetimes

| Kind | Lifetime | Sliding? | Notes |
|---|---|---|---|
| Anonymous cookie | 30 days | Yes | Returning visitors recognized; abandoned trails not eternal |
| Authenticated cookie (Staff / Producer / Customer) | 12 hours; sliding to 7 days max | Yes | Web-session norms; revocation latency bounded |
| Access token (JWT) | 15 minutes | No | OpenIddict default territory; short enough that stale-permission risk is bounded |
| Refresh token (portal / Web) | 14 days | Rotation on each use | Reuse-detection invalidates the whole token family |
| Refresh token (MAUI native) | 90 days | Rotation on each use | Mobile device-session norms |
| Magic link | 15 minutes | No, single-use | Server-tracked redemption |

### 6.3 Refresh-token rotation and reuse detection (mandatory)

Per OAuth 2.1 §6.1, refresh-token rotation is mandatory: every refresh issues a new refresh token and invalidates the old one. **Reuse detection is mandatory**: if a refresh token is presented after its successor has been issued, the entire token family for that session is revoked and all derived sessions are terminated. OpenIddict's stock implementation supports this; we configure it on with no override path.

### 6.4 Signing keys, JWKS, discovery

- **Asymmetric only.** RS256 (RSA-SHA256). Private key on the auth server; public keys exposed via JWKS for every resource server to validate locally.
- **Storage.** Azure Key Vault in production; user secrets / Data Protection key ring on disk in local dev. OpenIddict's Data Protection integration carries the wiring.
- **Rotation.** 90-day cadence. JWKS publishes both current and previous keys during a 30-day overlap window so in-flight tokens stay valid. Rotation is automatic on schedule, not a manual operation.
- **Discovery endpoints** (standard locations):
  - `https://auth.{company}.com/.well-known/openid-configuration`
  - `https://auth.{company}.com/.well-known/jwks.json`
  - `https://auth.{company}.com/.well-known/oauth-authorization-server` (for OAuth 2.1 clients that prefer the OAuth profile over OIDC)

Resource servers cache JWKS with periodic refresh. Cache miss / unknown `kid` → 401 with logged reason. No silent fallback.

---

## 7. Per-Population Sign-in Flows

### 7.0 Anonymous bootstrap

First GET to any Norse surface without a cookie:
1. Cookie-auth middleware interceptor generates a UUID v5 under the auth-context namespace.
2. Builds a signed cookie with `Population = Anonymous`, `Audience = MgaAnonymous`, `PrincipalId = <uuid>`.
3. Attaches the cookie to the response.
4. The request proceeds with an anonymous principal already in `HttpContext.User`.

No round-trip to OpenIddict — the cookie is signed by the Auth plugin's Data Protection key ring directly. Bootstrap is rate-limited per source IP (default: 60 issuances per minute) — enough for humans, hostile to drive-by crawlers.

### 7.1 Staff (Population A) — Google federation

- Sign-in surface offers exactly one button: **Sign in with Google**. No local-account fallback for staff.
- Server initiates OIDC Authorization Code flow against `https://accounts.google.com` with `hd={company}.com`. Non-{company}.com users never reach the consent screen.
- On callback, the server validates the ID token, extracts `sub` and `email`, and **auto-provisions on first sign-in** with `Population = Staff` and *no roles*. An admin assigns roles before the user can do anything role-gated.
- Subsequent sign-ins: same flow; the staff record is upserted on email.
- MFA is enforced upstream by Google Workspace admin policy; the platform does not double-enforce. The Workspace admin policy *must* require MFA for {company}.com accounts (a configuration assertion, not a code assertion).

### 7.2 Producer (Population B) — Invite-only local accounts, agency-scoped

- Internal staff create an agency (in an internal admin UI) and seed an `AgencyAdmin` user for that agency.
- The seeded user receives a **single-use enrollment link** (magic-link mechanics §8.3, `purpose = Enrollment`) by email. They land on `/auth/producer/enroll/{token}`, set a password, configure TOTP MFA (mandatory at enrollment), and the account becomes active.
- Subsequent sign-ins: email + password + TOTP. No magic-link option for routine producer sign-in — producers transact on behalf of an agency; we want the explicit credential path, not the convenience path.
- Agency admins invite additional users within their agency from `Norse.Auth.Components`' agency-admin screen. Same enrollment-link flow.
- No social login for producers. Producers represent a business; conflating personal Google identity with agency representation is the wrong default.

### 7.3 Customer (Population C) — Local primary, social additive, magic-link bootstrap

Three entry paths:

1. **Self-service registration.** Anonymous principal fills the registration form (email, password, optional TOTP). Server validates (no plus-addressing in prod per §8.2, email format, password policy). Server promotes the existing anonymous principal to `Population = Customer` with `CustomerId = PrincipalId`. Cookie rotates to `Audience = MgaCustomer`. **First-write-wins**: registration on a promoted id is rejected.
2. **Magic-link bootstrap.** Customer service sends an email link. Token carries `purpose = Action` (or `Reauthentication` for sign-in revalidation). 15-minute lifetime, single-use. Successful redemption optionally prompts to set a password for future sign-ins.
3. **Producer-initiated bootstrap.** A producer hands a customer off (post-bind, claim FNOL). Same magic-link mechanism; the email originates from the MGA, not the producer.

**Sign-in:** email + password (+ TOTP if enrolled). MFA is optional in V1; the identity-contract spec determines when MFA becomes mandatory (working assumption: mandatory before binding a policy, optional for browsing). Failed sign-in does not reveal whether the email exists — uniform response for "no such account" and "wrong password".

**Social login is strictly additive.** A customer signed into their local account can click *Add Google* or *Add Apple* → OIDC flow → returning IdP subject binds to that customer in `customer_external_identity`. A subsequent unauthenticated sign-in via Google or Apple:
- Subject found in the linking table → authenticate as that customer.
- Subject not found → reject with a clear message: *"link this Google account from your account settings to sign in this way"*. We **do not** auto-create customers from social sign-in; we **do not** match by email.

Apple-specific behaviors the spec acknowledges (Apple Sign In is implicit if Google is offered on iOS, per App Store policy, and the product has a MAUI client):
- Apple may return a private-relay email (`<opaque>@privaterelay.appleid.com`). Stored as-is; the customer's "real" email is whatever they registered locally.
- Apple returns the user's name only on the *first* sign-in. Captured then, never expected again.

### 7.4 M2M (Population D) — Client credentials

- Internal staff (with appropriate role) create an OAuth `application` via an internal admin screen. Form captures: friendly name, owning agency (nullable; for producer-AMS clients), allowed scopes, lifetime profile.
- Server generates `client_id` and `client_secret`. Secret is **shown once** and stored hashed.
- Partner systems POST `grant_type=client_credentials` to `/connect/token` with `client_id` + `client_secret`, receive a JWT (`Audience = MgaMachine`, `Population = Machine`).
- **Secret rotation** is a first-class admin action: generates a new secret with a configurable overlap window (default 14 days) where both secrets are valid, then revokes the old one.
- Per-agency M2M: when an agency is created, an M2M `application` is auto-provisioned in disabled state. Agency admin opts in via their admin screen if their AMS needs API access.

### 7.5 Fronting carrier (Population E) — V1 = M2M, V2-ready federation slot

- V1: the fronting carrier is an ordinary M2M client (§7.4 mechanics). One `application`, one `client_id`/`client_secret`, scoped to bordereaux feeds.
- V2 hook documented in code: `IUpstreamFederationHandler` interface in `Norse.Auth.Server` with a Google-OIDC implementation today and `CarrierKeycloakHandler` (annotated `[V2Slot]`, throwing `NotSupportedException`) reserved. When the carrier's staff need access, the work is implementing the handler and configuring the upstream — no architectural redesign.

### 7.6 MCP — OAuth 2.1 Auth Code + PKCE, staff-only in V1

- **Pre-registered Claude Desktop client.** Auth plugin seed data creates one `application` with `client_id = {company}.mcp.claude-desktop`, type `public`, allowed redirect URIs covering Claude Desktop's accepted callbacks (per Anthropic's published MCP client guidance). No client secret — PKCE carries the integrity guarantee.
- **Flow.** Claude Desktop launches an OAuth 2.1 Authorization Code + PKCE request → opens the user's default browser → server detects the existing staff cookie → presents a consent screen scoped to MCP tools and the calling client → on consent, issues an authorization code → Claude Desktop exchanges code + verifier for an access token + refresh token. Tokens carry `Audience = MgaMcp`, `Population = Staff`, and a `roles` claim filtered to MCP-permitted roles only.
- **MCP servers** are resource servers. Each context can expose one. They validate JWTs locally via JWKS, check `Audience = MgaMcp`, and authorize tool invocations against the principal's MCP-scoped role set.
- **Revocation.** OAuth 2.0 token revocation (RFC 7009) endpoint at `/connect/revoke`. Refresh-token reuse triggers full session revocation for that client, identical to the portal refresh policy.
- **Deferred.** Dynamic Client Registration (RFC 7591). When MCP scope expands to producers or external partners, DCR's operational story (rate limiting, abuse monitoring, scope-gating dynamic clients) gets its own spec and an enable-flip in OpenIddict.

---

## 8. Cross-Cutting Policies

### 8.1 Account linking — strict on the social-to-local boundary

A social IdP subject (Google `sub`, Apple `sub`) binds to a customer principal **only** via an authenticated linking action initiated from within the signed-in customer session. We never auto-merge by email match. This forecloses:

- **Account takeover by social hijack.** A leaked Google account whose email matches a registered customer email cannot, on its own, sign in as that customer. The customer must have actively linked their Google identity from inside a signed-in session.
- **Silent account fork.** A returning visitor who forgot they have a local account and signs in with Google does not get a brand-new customer record. The flow rejects with a clear message explaining how to link from the existing account.

Linking failure modes:
- Social subject is already linked to a different customer → reject (*"this Google account is associated with a different customer account"*).
- Social provider returns a different email than the one on the local account → still linkable, but UI surfaces the discrepancy so the customer is aware (no silent acceptance).

### 8.2 Email normalization

- **Case-insensitive comparison** on the local part. Stored canonically lowercased.
- **Plus-addressing disallowed in prod.** Registration normalizes incoming emails by stripping `+...` before `@`. The normalized form is what's stored and what's used for uniqueness checks. The address with the plus segment is retained only for outbound verification-email delivery on that registration, then discarded.
- **Configuration flag** `AuthOptions.AllowPlusAddressing` — `false` in prod, `true` in dev/test so developers can test multi-account flows from a single inbox. No per-request override.
- **Gmail dot-normalization** (`a.b.c@gmail.com` ≡ `abc@gmail.com`) is **not** implemented. It's a provider-specific quirk; treating it as universal would misclassify other providers. If a particular abuse pattern surfaces, the identity-contract spec adds it.

### 8.3 Magic-link mechanics

- Server-side opaque token (32 bytes, `RandomNumberGenerator`-sourced, URL-safe base64). Not a JWT — single-use enforcement and revocation require server-side state.
- Stored in `auth.magic_link_tokens`:
  - `token_hash` (SHA-256; the raw token is never persisted)
  - `principal_id` (the target — anonymous principal for enrollment, customer principal for re-auth/action)
  - `purpose` enum (`Enrollment | Reauthentication | Action`)
  - `expires_at`
  - `consumed_at` (nullable)
- Lifetime 15 minutes. Single-use — first successful redemption flips `consumed_at`; subsequent redemptions reject.
- Rate-limited per principal: at most 3 outstanding links per principal at any time; older links revoked when a new one is issued; 5 issuances per hour per principal hard cap.
- Link surface: `https://auth.{company}.com/magic/{token}`. Server resolves token → principal → executes the bound action → 302 to the post-action page.

### 8.4 MFA policy by population

| Population | V1 policy | Enforced where |
|---|---|---|
| Staff (A) | Mandatory | Google Workspace admin policy (upstream). The platform does not double-enforce. |
| Producer (B) | Mandatory at enrollment (TOTP) | OpenIddict, locally |
| Customer (C) | Optional in V1 | Local; identity-contract spec finalizes when mandatory |
| M2M (D, E) | N/A | Equivalent strength via secret rotation and per-client scope |

### 8.5 Cookie security and CSRF

- `Secure` always (HTTPS-only). Local dev uses the Aspire-issued self-signed cert.
- `HttpOnly` always — JavaScript on any Norse surface cannot read the auth cookie.
- `SameSite = Lax`. Not `Strict` — OAuth callback round-trips are top-level navigations and `Strict` breaks them on first sign-in. `Lax` is correct for portal cookies that participate in OAuth flows.
- ASP.NET Core antiforgery is required on every non-idempotent endpoint. The cookie auth scheme is not a substitute for antiforgery — they layer.

### 8.6 Signout semantics

- Logout from any Norse surface:
  1. Revoke the cookie's server-side session record (the Auth context maintains `auth.cookie_sessions` for revocation — the cookie alone is not the source of truth).
  2. Clear the authenticated cookie.
  3. Issue a fresh anonymous cookie with a **new** UUID v5 (not the principal's old id).
- **Global logout** (all sessions for a principal): admin action; revokes every session and refresh-token family for that principal. Available from the staff admin UI.
- OAuth token revocation at `/connect/revoke` (RFC 7009) for clients that hold tokens.

### 8.7 Rate limiting (defaults; configuration-tunable)

| Surface | Default limit |
|---|---|
| Anonymous bootstrap | 60 / minute per source IP |
| Sign-in (email + password) | 10 / minute per IP, 5 / hour per account email |
| Sign-in (Google / Apple OIDC callback) | 30 / minute per IP |
| Magic-link issuance | 3 outstanding per principal; 5 issuances per hour per principal |
| Token endpoint | 60 / minute per `client_id` |
| Token revocation | 60 / minute per `client_id` |

Lockout policy on repeated sign-in failure: account locks for 15 minutes after 5 failures within 1 hour. Lockout itself is a logged event.

---

## 9. Approaches Considered

For the record, two non-recommended approaches were evaluated against Approach 1 (single OpenIddict, direct federation) during brainstorming.

### Approach 2 — Keycloak as identity broker in front of OpenIddict

Keycloak handles all upstream federation; OpenIddict is still the OAuth 2.1 authorization server the product owns. Earns its keep at 3+ upstreams or large-producer SAML. **Rejected for V1** because the federation map has only one upstream in V1 (Google) and at most two in V2 (Google + the carrier's Keycloak). The broker pattern adds two stateful services, two DBs, two upgrade paths, and a layer where claims can silently mutate during token transformation — every cost, no benefit at this scope. Migration path from Approach 1 → Approach 2 is well-trodden; the inverse is harder.

### Approach 3 — Per-population issuers

Separate OpenIddict instances per population (`auth-staff`, `auth-producer`, `auth-customer`, `auth-machine`). Hardest possible boundary between populations. **Rejected** because:
- 4x operational overhead. 4 DBs, 4 sign-in surfaces, 4 signing-key rotations.
- Every resource server must trust a *list* of issuers, not a value — `JwtBearer` validation gains a per-context configuration matrix.
- Cross-population flows (staff support impersonating a customer; a customer's policy events flowing to a producer's view) require explicit cross-issuer trust — significant complexity for benefit the platform's threat model does not require.

Approach 1 is unambiguously the recommendation given the dragon we have.

---

## 10. Decisions Resolved by This Spec

CLAUDE.md §7's open-questions list is updated as follows:

- **§7 #3 — Auth federation.** Resolved. Direct OIDC federation to Google Workspace for staff; local accounts in OpenIddict for producers and customers; M2M via client credentials; carrier federation slot documented for V2; no Keycloak broker.

CLAUDE.md §7's open-questions list was **not** changed by this spec for the items below; their later status is noted:

- **§7 #4 — Tenancy model.** *Since resolved, 2026-06-03 — stamp-per-tenant (`2026-06-03-tenancy-model-design.md`). The `TenantId` slot is removed from §5's principal model; each stamp is its own IdP, and the federation topology applies per stamp unchanged.*
- **§7 #11 — PII / encryption posture.** Adjacent but separate. `YGG101` (the bare-string PII analyzer) applies to event types regardless of federation choice.

## 11. Follow-up Work

This spec creates the following inbox items for downstream work, in the order they should be picked up:

1. **`YGG110` analyzer.** Add to the architecture-analyzers spec: forbid `[AllowAnonymous]` anywhere in the Norse solution. Build error.
2. **Identity-contract spec.** Detailed claim shape per population, role taxonomy per population, scope taxonomy for OAuth clients, claim-enrichment via `IClaimsTransformation`, audit-event schema and retention.
3. **Authorization-model spec.** RBAC vs. ABAC vs. policy-based decisions. Cross-population impersonation mechanics (staff support → customer).
4. **Auth admin UIs spec.** Staff admin, agency admin, M2M client management. UI-composition territory.
5. **Customer self-service UX spec.** Registration screens, MFA setup screens, account settings (linked identities, password change, sign-in history). UI-composition territory.
6. **Auth migrations.** `Norse.Auth.Migrations` package (lives above the Server/Worker split): initial schema (users, roles, sessions, magic-link tokens, external identities, OpenIddict tables, audit events). Follows DDLC conventions per CLAUDE.md.

## 12. References

- CLAUDE.md §2.5 (simplicity over ceremony), §2.6 (hard-fail on ambiguity), §2.7 (push errors upstream), §3 (bounded contexts), §4 (hosting + plugin model), §5 (naming and project layout), §6 (codename registry), §7 #3 (auth federation open question).
- `docs/Platform/specs/2026-05-19-architecture-analyzers-design.md` — gains `YGG110` in a follow-up.
- `docs/Svartalfheim/specs/2026-05-20-svartalfheim-primitives-design.md` — `Result<T>` shape is the return type for auth flows that can fail (parser-style, not exception-style).
- OAuth 2.1 draft — `draft-ietf-oauth-v2-1`.
- OpenID Connect Core 1.0.
- RFC 7009 — OAuth 2.0 Token Revocation.
- RFC 7591 — OAuth 2.0 Dynamic Client Registration (deferred; referenced for MCP).
- RFC 8414 — OAuth 2.0 Authorization Server Metadata.
- NIST SP 800-63B — Digital Identity Guidelines (authentication and lifecycle management) — referenced as the working baseline for password and MFA policy in downstream specs.
- Anthropic MCP authorization profile — referenced for the OAuth 2.1 + PKCE expectations of MCP clients.
