# Himinbjörg: OpenIddict Authorization Code + PKCE, Revocation and Step-Up — Design

**Realms touched:** Himinbjörg (OpenIddict 8, the six entities, the protocol surface, revocation, `auth_time`, the bearer source scheme, pruning), Midgard (the OpenIddict leak removed, the lane handlers and lane stamping, the default-closed lane requirement in the generated policy registration, the freshness requirement, gRPC bearer selection, the re-authentication failure mapping), Asgard (neutral names and policy helpers: lanes, audiences, the freshness policy, the re-authentication error code; the `Machine` and `Probe` policies), Heimdall (the `AccountRead`, `Self` and `MaskedDisclosure` policies), Mímir (`ReferencePolicies.Public` names its lanes, r6.2), Yggdrasil (composition, the OpenIddict pin, policy registration), Glitnir (this verdict).
**Closes:** spec sub-project **B** of `2026-10-26-identity-account-lifecycle-over-grpc-design.md` §3, and the Train 3 hand-off T3-37 (step-up). Partially serves [Himinbjorg#50](https://github.com/NorseArchitecture/Himinbjorg/issues/50) (the authorization-code core; its MCP door is deferred, §11) and [#52](https://github.com/NorseArchitecture/Himinbjorg/issues/52) (the flow MAUI will ride; the MAUI client is Train 6).
**Depends on nothing open.** **Depended on by:** Train 5 (the full OpenIddict / ASP.NET Identity end-to-end integration, including T3-36's local external-login walk), Train 6 (MAUI), and the MCP door (§11).
**Amends:** Himinbjörg `CLAUDE.md` Architecture Facts — the principal allowlist (three claims become four persistent ones plus one transient: `auth_time`, and the `norse_reauth` binding, §6.1) and the temporal/non-temporal rulings (two new entities, §4.3); `2026-10-26-identity-account-lifecycle-over-grpc-design.md` §11 W24 (OpenIddict session revocation is answered by §5.5, not by OpenIddict sessions).
*As built (§13): the allowlist also keeps the framework's authentication-method markers (`amr`, `ClaimTypes.AuthenticationMethod`).*
**Revision:** r6, 2026-10-29. r1 was the brainstorm verdict; rulings 1–9 were each approved in-session before it was written. r2 answers the first review pass. Every OpenIddict claim was verified against the `8.0.0-preview.4` source tag, and every policy claim against Heimdall's `AuthNPolicies.cs`/`IdentityPolicies.cs`. The changes:

- Policies did not distinguish lanes, so lanes are now stamped and required explicitly (ruling 10, §7.3–§7.5).
- The token paths carry the authentication facts from the presented grant instead of re-deriving them, so a code cannot bridge a stamp rotation and `auth_time` survives every refresh (ruling 11, §5.4).
- The replay and self-revoke contracts now say what OpenIddict actually does, with the chain boundary defined (§5.5).
- The authorize algorithm handles `prompt=none`, `max_age=0`, and a missing or malformed `auth_time` (§5.2).
- Protocol errors are standard OAuth/OIDC errors, not empty bodies (§5.7).
- Session pruning is concrete (§5.6).
- The id-token audience is the client (§5.3).
- User tokens carry no roles (§5.3).

r3 answers the second review pass. Every claim was verified against the code and the pinned source. The changes:

- `Public` was reachable by a bearer, and so were Mímir's `ReferencePolicies.Public` and `NorsePolicies.Anonymous`. Lanes are now closed by default in the generated policy registration (ruling 10, §7.5).
- OpenIddict ignores a lost refresh-token redemption to tolerate concurrent requests, so zero leeway alone did not guarantee one live successor. A Norse redemption gate now makes a lost race a reuse (ruling 12, §5.5).
- The re-authentication marker was reusable across requests. It is now bound to the whole authorize transaction, is one-shot, and demands an `auth_time` strictly later than the challenge's second (§5.2).
- The machine lane had no Midgard interception point. Midgard now owns both lane schemes over one Himinbjörg-registered bearer source (§7.1, §7.3).
- Revocation of an invalid token is a success response, per RFC 7009 §2.2 (§5.7).

r4 answers the third review pass, verified against `OpenIddictTokenManager.TryRedeemAsync` and `GenerateRefreshToken` at the pinned tag. The changes:

- `TryRedeemAsync` is not a conditional transition, and a token sweep cannot reach a successor created after it. So a guarded `valid → redeemed` statement replaces the built-in redemption handler, and the durable grant boundary is the authorization's own status, revoked on every grant-ending event (ruling 12, §5.5).
- Same-second re-authentication had no progress guarantee. A per-authentication `norse_auth_id` was the marker's evidence of a new authentication, which r5 withdraws.
- Duplicate refresh is described as reuse, a lost-response retry included, and the client contract records the recovery (§5.5).

r5 answers the fourth review pass, verified against `ValidateGenericToken`, `ValidateTokenEntry` and `ValidateAuthorizationEntry` at the pinned tag. The changes:

- A different authentication id proved a different sign-in, not a later one. Re-authentication is now bound to the pending transaction: only a credential presentation whose landing carries the marker yields a cookie that satisfies it (§5.2, §6.1). `norse_auth_id` and the timestamp bound are withdrawn.
- OpenIddict dispatches `ValidateTokenContext` for revocation too, and its own `ValidateTokenEntry` sweeps a grant when a redeemed token is presented there. One reuse rule now covers both endpoints, with explicit filters (§5.5).
- A database exception does not prove the redemption rolled back. Definite and indeterminate failures are distinguished, and `EndGrant` reports whether the revocation was confirmed (§5.5).

r6 makes two bounded corrections from the fifth review pass, which approved the design for planning behind its halting gates. Both were verified against `NorseSignInManager.cs` and the pinned `ValidateTokenType`:

- The two-factor completion path drops the parked landing before its sign-in, so the landing has to be plumbed to the stamp point. That is a known implementation task, no longer an assumed premise (§5.2).
- `/connect/revoke` accepts only access and refresh tokens. Self-revoke is restricted to refresh tokens, and code reuse at that endpoint is stated separately (§5.5).

**Amendment r6.1 (2026-10-30, execution halt at H3/H4, Buvy's ruling):** adds §4.5, the store link contract. Plan Task 0.2's premise facts failed for refresh tokens.
- OpenIddict's EF token store looks a token up by id with `DbSet.FindAsync`. EF Core 11 RC3's `EntityFinder` queries its ordinary query root without `AsTracking()`, so under the platform's NoTracking default the entity comes back detached.
- The stock `GetAuthorizationIdAsync`/`GetSessionIdAsync` read the navigation and return null for a detached entity with an unloaded navigation.
- `ValidateTokenEntry` then overwrites the principal's authorization id with that null. The predicted consequence, derived from the source and pinned by §4.5's continuity fact, is that `AttachAuthorization` would create a fresh ad-hoc authorization on every refresh, splitting the grant.
- The reference-id lookup works only because it uses `Include` plus `AsTracking()`. That is not evidence the id lookup works.

The guarded redemption, the grant boundary and every other ruling stand unchanged.

**Amendment r6.2 (2026-10-30, execution halt at plan Task 5.1, Buvy's ruling):** Mímir joins the train.
- Mímir's REST façade (`CountriesController`, whose base requires `Machine`) and its `CountryQuery` both require `ReferencePolicies.Public`. Default-closed to `Identity` (§7.5), that policy can never be satisfied together with `Machine`, so a real machine token was refused at the façade. "Mímir untouched" (§9) and "a machine token reaches a façade controller" (§10.12) could not both hold, and the first yields: this is a broken existing capability, not deferred functionality.
- `ReferencePolicies.Public` keeps `RequireAuthenticatedUser()` and declares `RequireLane(Identity, Machine)` explicitly. This is a specific exception for reference reads. The generated default is not widened, and neither is Heimdall's `AuthNPolicies.Public`. The façade keeps its `Machine` policy and its `Norse.Facade` audience pin.
- `Machine` in a shared operation policy does not open gRPC to machine tokens: `NorseLaneSelector` routes gRPC through the identity-cookie lane (and `Norse.User` for a bearer header), never `Norse.Machine`. That boundary is pinned (§10.12).
- Rejected: splitting the façade from `Public` (a second query or a lane-aware policy, with no demonstrated need), and deferring the façade to Train 5.

**Amendment r6.3 (2026-10-30, final whole-branch review halt, Buvy's rulings):** three rulings.
- **Lockout no longer ends a grant.** Answering `invalid_grant` tells the client to discard its tokens and re-authorize (§5.5; RFC 6749 §5.2). Failed-login lockout is a temporary throttle on credential sign-in, and that answer would turn it into a permanent sign-out of every native client. Any attacker who knows an email could then drive that sign-out with wrong passwords. So lockout blocks credential sign-in and nothing else. An already-issued code or refresh grant is still checked for PKCE and client binding, expiry, stamp, erasure, authorization status and reuse, and is not refused because of lockout.
  - Administrative withdrawal is an explicit stamp change or grant revocation. It is never inferred from a far-future lockout date.
  - If temporary suspension is ever needed, it needs its own recoverable response and a client retry contract.
  - A stamp mismatch on the token endpoint ends *the presented grant* on exchange, not every grant the user holds. Independent grants meet the same check on their own next exchange.
- **The authorize endpoint re-reads the user** (§5.2 step 1). The cookie alone is not proof the account still stands. After authenticating the cookie, the endpoint reloads the user through `userManager.Outcome`. A missing or erased user, or a current stamp different from the cookie's, counts as no principal. Lockout is not a reason, for the same throttle-not-withdrawal reason.
- **A lane checks its token's audience** (§7.3). `NorseBearerLaneHandler` checks the validated identity after `Norse.Bearer` succeeds and before stamping. `Machine` requires exactly `aud = Norse.Facade`, and `User` requires exactly `aud = Norse.User`, compared ordinally. The other platform audience, both platform audiences, or no audience fail authentication in either lane, with no fallback to a cookie or another lane. A lane now means the token's kind, not only the route that selected it. The policy audience pins stay as defence in depth. No generator diagnostic this train.
  - The wrong-kind refusals that were a 403 at the policy become a bare 401 at the authentication challenge: `WWW-Authenticate: Bearer error="invalid_token"` (§5.7).

**As built (2026-10-30, staging date):** §13 records what landed in each realm, the halting gates, the two execution halts, the end-to-end walk and the errata.

## 1. Context

On `master`, OpenIddict is `client_credentials` only (Himinbjorg#49): one token endpoint (`/connect/token`, `OpenIddictExchangeEndpoint`), one audience (`Norse.Facade`), local validation, and a `Norse.Machine` lane that Midgard registers as a policy-scheme forward to OpenIddict's validation scheme. No human can obtain a token. The Train 3 hand-off left three things owed to this train: step-up re-authentication (T3-37, by name for passkey add, two-factor disable, external-login link and the authenticator key display), the "other session dies" gap W24, and the sub-project B core itself.

OpenIddict 8 is in preview (`8.0.0-preview.4`, 2026-09-06; no stable). Its EF Core models were modernized in preview 3, and it adds `Session` and `Resource` entities. `InitialCreate` still squashes in place, so this is the cheapest point the platform will ever have to absorb a schema-moving upgrade.

**Outcome sought:** the platform is its own OIDC provider for native and machine clients. A first-party public client can run authorization code + PKCE against an Identity account and refresh with rotation and reuse detection. Erasure or a security-stamp rotation ends its grant, including any code still unredeemed; a failed-login lockout blocks only credential sign-in (r6.3). A user bearer token reaches exactly two read-only gRPC operations. The four sensitive account operations demand a recent authentication on either lane. OpenIddict is invisible outside Himinbjörg. All of it is proven at the protocol level, with no new UI.

## 2. Rulings (brainstorm 2026-10-28/29; 10–11 from review r2)

1. **Scope is the core only.** Authorization code + PKCE, refresh rotation and reuse detection, user-bearer validation, step-up and the erasure kill, all with pre-registered first-party clients. The MCP door from #50 (dynamic client registration, RFC 8414 / RFC 9728 metadata, an MCP resource server and audience) is its own later train.
2. **OpenIddict 8, now.** Code against `8.0.0-preview.4`. Himinbjörg floats `8.*-*` as it floats `7.*` today, and Yggdrasil's `Directory.Packages.props` holds the pin, the way everything else is pinned. Breaking previews are accepted until RTM.
3. **Revocation is erasure, stamp rotation and self-revoke** (§5.5; lockout removed in r6.3). Signing out of a browser session does not end a native client's tokens. OpenIddict 8 sessions are carried in the schema and left unused: no `LoginId` claim.
4. **Step-up is a lane-neutral freshness requirement plus OIDC compliance** (§6). The four operations require a recent `auth_time` on whichever principal arrives. A stale caller is answered with a server-authored re-authentication `NextUrl`. The authorize endpoint honors `prompt` and `max_age` and emits `auth_time`.
5. **User bearer tokens reach a read-only slice** (§7.5): self-disclosure and `GetAccountStatus`, and nothing else. Every mutation stays cookie-only until the MAUI train decides how a token caller rotates a stamp.
6. **The realm split follows existing law** (approach A). Himinbjörg owns the server, Midgard the lanes and the freshness requirement, Asgard and Heimdall the names and policies, Yggdrasil the composition. There is one OpenIddict validation scheme accepting both audiences, and each policy pins its audience (§7.4).
7. **OpenIddict never leaves Himinbjörg.** No package reference, type, scheme constant or behavioral workaround for OpenIddict exists in any other realm. The leak that already exists in Midgard is fixed in this train (§7.1).
8. **`auth_time` is an explicit claim** stamped only by credential presentation (§6.1). The cookie's `IssuedUtc` is not authentication time.
9. **No Heimdall UI change.** `Login.razor` does not redirect a signed-in user, so the re-authentication hop lands on the existing login page as is (§6.4).
10. **Lanes are explicit in the principal and closed by default in every policy** (r2, amended r3). Midgard stamps the lane that authenticated a request onto the principal. The generated policy registration adds an `Identity`-lane requirement to every policy that names no lane, so a new policy, in any realm, admits the cookie identity and nothing else until it opts a lane in by name. No policy infers a lane from roles or claims a token could carry, and no policy re-authenticates another scheme to find out.
11. **A grant carries its authentication; the token endpoint never re-derives it** (r2). The authorization code and every refresh token carry the `auth_time` and the security stamp of the authentication that produced them. Redemption checks that stamp against the user's current one and carries both facts forward unchanged. Only authorization facts (does the user exist, is it erased) are re-read. Lockout is not one of them (r6.3).
12. **Any reuse of a code or refresh token ends the grant, durably** (r3, amended r4). Redemption is a guarded `valid → redeemed` transition that also requires a valid authorization. Every grant-ending event revokes the authorization itself, so no successor created around it can ever be redeemed (§5.5). Reuse means any second presentation, including a serialized client's retry after a lost response. That ends the grant, and the client signs in again.

## 3. Scope

| In | Out (where it goes) |
|---|---|
| `authorization_code` + PKCE (S256 only), `refresh_token`, `client_credentials` kept | `password` (ROPC), implicit, hybrid, device code: never |
| `/connect/authorize`, `/connect/token`, `/connect/revoke` (RFC 7009) | userinfo, end-session, introspection: not this train (§11) |
| First-party public clients, pre-registered, implicit consent | Dynamic client registration, consent UI: the MCP door (§11) |
| `openid`, `offline_access` | A scope vocabulary, and roles in user tokens: the authz-lockdown arc |
| User bearer on self-disclosure and `GetAccountStatus` | Bearer on any mutation: Train 6 |
| Revocation by erasure, stamp, self-revoke of the presented token (lockout removed, r6.3) | "Revoke all my grants" (an authorization-wide revoke) and its UI: Train 5 |
| Pruning of expired OpenIddict rows | — |

## 4. Schema (Himinbjörg `src/Identity.EntityFramework`)

### 4.1 Six entities

`NorseOpenIddictSession` and `NorseOpenIddictResource` join the existing four. In OpenIddict 8, `OpenIddictEntityFrameworkCoreApplication`, `…Authorization` and `…Token` each take a `TSession` type parameter, and `ReplaceDefaultEntities<…>` in `AddNorseOpenIddictCore()` names all six. `NorseOpenIddictSession` declares explicit `ApplicationId` and `AuthorizationId` FK scalars for the same reason the authorization and token entities already do: the platform's navigation/FK law forbids shadow FKs.

### 4.2 Typed columns

OpenIddict 8 types what were JSON strings: `Properties` becomes `IDictionary<string, JsonElement>`, `Permissions`/`RedirectUris`/`Requirements`/`Scopes`/`Resources` become `string[]`, `DisplayNames`/`Descriptions`/`Settings` become `IDictionary<string, string>`, and the application gains a typed `JsonWebKeySet`. The `HasMaxLength(-1)` lines on `Properties`, `Scopes` and `Payload` were string-column configuration and are removed or replaced by whatever each property's mapping needs (`Payload` stays a string). The EF stores now use JSON operators. PostgreSQL and SQL Server 2025 both support them, and the dual-provider container suites prove it.

*As built (§13): preview.4 stores the `IDictionary` columns as JSON strings, so `HasMaxLength(-1)` stays on `Properties`, `DisplayNames`, `Descriptions` and `Settings`; only the `string[]` columns lost it.*

**Spike first (§10, task 1):** SQL Server engine-native system versioning over JSON-typed columns on `applications`, `scopes` and `resources`. This is the one interaction that cannot be settled from source. If it fails, the plan halts and the finding returns to the controller before anything is built on it.

### 4.3 Temporal rulings, amended

- `NorseOpenIddictResource` → `resources`, **temporal**. It is part of the durable authorization record, like `applications` and `scopes`. The temporal set grows from seven to eight.
- `NorseOpenIddictSession` → `sessions`, **non-temporal**. It is bulk-pruned runtime state, like `authorizations` and `tokens`.
- `NorseIdentityTemporalModelTests` (including its closing fact that no other entity carries the stamp), `NorseIdentityTemporalApparatusContainerTests` and `SqlServerIdentitySchemaTests` are amended in the same change. Tables are named for the entity, never `openiddict_*`.

### 4.4 Squash

`InitialCreate` is squashed in place on both providers, and the embedded `schema/norse_identity.sql` regenerates. Buvy wipes the local `norse-pg-primary`/`norse-pg-replica` volumes before the next Bifröst stand-up.

### 4.5 The store link contract (r6.1)

Himinbjörg replaces OpenIddict's EF stores with Norse adapters whose relationship accessors answer from the database-derived link, whether or not the entity is tracked or its navigation is loaded. The adapters and their registration (`ReplaceTokenStore` and its siblings, in `AddNorseOpenIddictCore()`) live in `Identity.EntityFramework`.

| Store | Links covered |
|---|---|
| Token | application, authorization, session |
| Authorization | application |
| Session | application, authorization |

- **Getters** answer from the explicit FK scalars the entities already carry (`ApplicationId`, `AuthorizationId`, `SessionId`). A null link stays null. A getter issues no relationship-loading query.
- **Setters** keep the scalar and the navigation consistent for setting, replacing and clearing each link, on tracked and detached entities alike, before and after persistence. A getter never disagrees with a preceding setter.
- **No recovery from claims.** A missing relationship is never recovered from token claims, and no adapter ever creates an authorization. The link is the database's or it is absent.
- NoTracking stays the platform default. The adapters do not depend on tracking, so they need no tracking exception.
- **Facts, on both providers.** These gate the resumption of execution (plan H7):
  1. **Store contract.** Fresh-scope, detached entities with unloaded navigations return the persisted ids. Null links return null. Getter calls issue no relationship-loading query, observed by a command interceptor.
  2. **Write round trip.** Set, replace and clear each supported link, then verify immediately and again after save and reload. Creation is verified through the real OpenIddict managers.
  3. **Grant continuity.** Code → R0 → R1 → R2 keeps one non-null authorization id with no extra authorization created by refresh, asserted on the database rows, not only on principal claims.
  4. **Security consequence.** Replaying R0 after two rotations ends the original authorization, and R2 cannot refresh. An independent grant survives. The concurrent successor-after-sweep facts (§10.7) are retained.
  5. **Registration.** The replacement stores resolve through production DI, across separate request scopes and under OpenIddict's normal cache configuration, and the H2–H4 premise facts re-run unchanged and pass.

## 5. Protocol surface (Himinbjörg `src/Identity.Web.Server`)

### 5.1 Server configuration

`AddNorseIdentity`'s `AddServer` gains `AllowAuthorizationCodeFlow()`, `AllowRefreshTokenFlow()`, `RequireProofKeyForCodeExchange()`, S256 as the only code-challenge method, `SetAuthorizationEndpointUris("/connect/authorize")` and `SetRevocationEndpointUris("/connect/revoke")`, with authorization-endpoint passthrough. `client_credentials` stays. Registered scopes: `openid`, `offline_access`. OpenIddict 8's `ValidateOnStart()` is called on the core, server and validation builders.

*As built (§13): the endpoint URIs are relative (`connect/authorize`, `connect/token`, `connect/revoke`); with a leading slash OpenIddict ignores `PathBase`.*

**Lifetimes** (options, overridable as `accessTokenLifetime` is today): authorization code 5 minutes, access token 10 minutes, identity token 10 minutes, refresh token 14 days, rolling, with zero reuse leeway.

### 5.2 The authorize endpoint

A passthrough handler (`OpenIddictAuthorizeEndpoint`, beside `OpenIddictExchangeEndpoint`) requires a registered client with an exact-match redirect URI, then decides in this order.

**1. Read the authentication.** Authenticate the Identity cookie. The anonymous principal counts as no principal. Then reload the user through `userManager.Outcome` (r6.3): a missing or erased user, or a current security stamp different from the cookie's, also counts as no principal. Lockout does not. A principal whose `auth_time` is missing, unparseable as integral epoch seconds, or in the future beyond the clock-skew allowance counts as *stale*. It is never treated as fresh, and never as absent.

**2. Decide whether re-authentication is required.** It is required when:

- there is no principal;
- the principal is stale;
- `prompt=login` is present;
- `max_age` is present and `now − auth_time > max_age`. `max_age=0` is therefore always required, the OIDC equivalent of `prompt=login`.

A malformed `max_age` (not a non-negative integer) is an `invalid_request` protocol error.

**3. `prompt=none` never shows a page.** If re-authentication is required and `prompt=none` is present, the endpoint returns the protocol error to the redirect URI, carrying `state`: `login_required`. `prompt=none` combined with any other `prompt` value is `invalid_request`.

**4. The completed-re-authentication continuation.** When re-authentication is required and `prompt=none` is absent, the endpoint challenges the cookie scheme. The challenge lands on `/Account/Login?returnUrl=<the authorize request>`, resolved through `ReturnUrls.Resolve` (`/connect/authorize` is a local path).

- **The marker.** The authorize request the challenge returns to carries `norse_reauth`: a data-protected marker under its own purpose, valid for 5 minutes. It carries:
  - a random 128-bit marker id;
  - the user id the challenge was issued to (empty when there was no principal);
  - the SHA-256 of the **canonical authorize transaction**: every authorize parameter except `norse_reauth` itself, sorted by name ordinally, values exactly as received, `client_id`, `redirect_uri`, `response_type`, `scope`, `state`, `nonce`, `code_challenge`, `code_challenge_method`, `prompt` and `max_age` among them.
- **The binding (r5).** Re-authentication is proved by binding a credential presentation to *this* marker, not by comparing timestamps or authentication ids.
  - The challenge sends the user to `/Account/Login?returnUrl=<the authorize request, marker included>`.
  - **The stamp point and its one input (r6).** The stamp point is `NorseSignInManager`'s `SignInWithClaimsAsync` override, which every credential sign-in reaches. It reads the landing from one scoped `HttpContext.Items` slot (`ReauthLandingItemName`). Two paths fill that slot, and the second is new plumbing:
    - **Direct sign-ins** (a password with no second factor, a passkey assertion): the landing parked through `ParkedReturnUrl` (T3-2) is still on `ReturnUrlItemName` when `SignInOrTwoFactorAsync` takes its direct-sign-in branch. That branch copies it into `ReauthLandingItemName` before calling the base sign-in and clears the slot in a `finally`. The stamp point reads only the slot, never `ReturnUrlItemName` (plan ruling T4-1, amended as built, §13).
    - **Two-factor completions** (authenticator code, recovery code, and external login completed with the second factor, which always takes this path): today `SignInOrTwoFactorAsync` removes the parked landing and seals it into the `TwoFactorChallenge`. `CompleteTwoFactorSignInAsync` then calls `SignInWithClaimsAsync` without it and returns `challenge.ReturnUrl` only afterward, so the handlers resolve the landing after the sign-in. The fix: `CompleteTwoFactorSignInAsync` resolves `challenge.ReturnUrl` through `ReturnUrls.Resolve` *before* calling `SignInWithClaimsAsync`, sets it on `ReauthLandingItemName`, and removes it in a `finally`, so the slot never outlives the one sign-in it serves. Its returned landing is unchanged.
    - **Deferred sign-ins.** The claims, `norse_reauth` included, are computed at the stamp point before `IDeferredSignIn` stashes the principal. So a deferred sign-in carries the binding exactly as an immediate one does.
  - When `NorseSignInManager` completes one of those credential sign-ins and the resolved landing is an authorize request under the live `PathBase` carrying a marker that unprotects, is unexpired and names this user (or no user), it stamps the new cookie with `norse_reauth` = that marker's id (§6.1). Otherwise it stamps nothing.
  - **Why this proves ordering.** A cookie can carry a marker's id only if a credential was presented while that marker already existed and was the landing. A cookie issued before the challenge, or from a different sign-in, cannot carry it. That holds for a second pre-existing cookie inside any time window, and for any cookie presented after an anonymous challenge.
  - **The end-to-end gate (§10).** Every one of the five credential paths, immediate and deferred, reaches the stamp point with its landing after the plumbing above. If any path still doesn't, the plan halts.
- **Re-entry.** A re-entered request is satisfied by its marker only when all of these hold:
  1. The marker is valid and unexpired.
  2. The request's canonical transaction hash equals the marker's, so any changed `state`, `nonce`, PKCE challenge, scope or other parameter voids it.
  3. The principal's `norse_reauth` claim equals the marker's id.
  4. When the marker names a user, the principal is that user. When it names none (an anonymous challenge), the principal is whoever presented the credential bound by condition 3.
  5. **The marker is burned exactly once,** in the existing one-shot ledger (`consumed_passkey_challenges`, the SHA-256 of the marker id under a marker-specific prefix, the same conditional insert `PasskeyChallengeLedger` and the recovery-code handoff use) before a code is issued. A lost acknowledgement reads as a replay and fails closed.
- **Bounded progress.** The first credential presentation carrying the marker produces a cookie that satisfies conditions 3 and 4. The marker carries no clock comparison, so any number of same-second presentations each satisfy their own marker, and a user who re-proves a credential once is never re-challenged for timing.
- **What a satisfied marker does.** It satisfies `prompt=login` and any `max_age`, `max_age=0` included. Nothing is stripped from the request. The marker is the only thing that overrides `prompt` and `max_age`.
- **Failure.** A marker that fails any condition, including an already-burned one, is ignored, and the request is evaluated as if it had none, so it challenges again and mints a fresh marker. With `prompt=none` it answers `login_required`. A marker cannot buy a pass for a different transaction, a different user, or a second use.

**5. Consent and minting.** Consent is implicit (`ConsentType = implicit`): every client is first-party. A non-implicit consent type is refused with `consent_required`, because there is no consent page. The endpoint then mints the principal (§5.3), creates one **ad-hoc authorization** for this request (never a permanent one, never reused across requests; §5.5), and signs in to the OpenIddict server scheme.

### 5.3 The minted principal

| Claim | Access token | Id token | Code and refresh token |
|---|---|---|---|
| `sub` (opaque user `Guid`) | ✓ | ✓ | ✓ |
| `ClaimTypes.NameIdentifier` (same `Guid`; Midgard's `PrincipalAccessor.Seed` requires it, as the machine exchange already does) | ✓ | — | ✓ |
| `auth_time` (§6.1) | ✓ | ✓ | ✓ |
| The security stamp (a Norse private claim, `norse_stamp`) | — | — | ✓ |
| Roles | — | — | — |

- **No roles in user tokens.** Nothing in the bearer slice needs one, and a role-bearing token is exactly what would let a bearer satisfy a role-only policy (§7.5). Roles in user tokens belong to the authz-lockdown arc.
- **No email or name anywhere,** consistent with the PII allowlist.
- **Destinations.** The stamp claim is given no access-token or id-token destination. Authorization codes and refresh tokens are server-only and encrypted by OpenIddict by default, and they carry the principal's claims regardless of destination. §10 pins that the stamp is present in both and absent from both public tokens. Access tokens stay signed, unencrypted JWTs (`DisableAccessTokenEncryption`, unchanged).
- **Audiences.** The access token's audience is `Norse.User` (§7.4). The id token's audience is the requesting `client_id`, OpenIddict's own behavior, unchanged.

### 5.4 The token endpoint

`OpenIddictExchangeEndpoint` grows a grant-type switch. Its invariant "only `client_credentials`" becomes "only the three enabled grants", still an `InvalidOperationException`, because OpenIddict has already rejected anything else. `client_credentials` is unchanged.

For `authorization_code` and `refresh_token`, by ruling 11:

1. **Read the grant.** Authenticate the OpenIddict server scheme to obtain the presented grant's principal (the code's or the refresh token's), and read its `sub`, `auth_time` and `norse_stamp`. A grant missing any of the three is `invalid_grant`.
2. **Check the user.** Reload the user through `userManager.Outcome` and answer `invalid_grant` when the user is missing or erased, or has a current stamp that differs from the grant's. Every such rejection, on a code or a refresh token, also runs `EndGrant` on the presented grant's authorization (§5.5). Lockout is not checked here (r6.3). The reads that decide whether `EndGrant` runs ignore request cancellation, like `EndGrant`'s step 1.
3. **Mint from the grant.** Build the new principal from the grant's `sub`, `auth_time` and `norse_stamp`, never from a fresh read. On success the stamp equals the current one anyway, and `auth_time` is carried unchanged through every refresh, never advanced by one.

So a code issued before a password reset cannot be redeemed after it. A refresh at day 13 still carries the original sign-in's `auth_time`.

*As built (§13): step 1 reads the validated grant principal from OpenIddict's server transaction, not through `AuthenticateAsync`.*

### 5.5 Revocation

**The chain boundary.** One authorize request creates one ad-hoc authorization (§5.2). Its code, and every access and refresh token derived from that code, are attached to that authorization. "The grant" means that authorization and exactly its tokens: one client, one sign-in. Two sign-ins by the same user on the same client are two independent grants.

**The durable boundary is the authorization's status (ruling 12, r4).** A token sweep cannot end a grant on its own, because OpenIddict creates the successor refresh token late in the sign-in pipeline (`GenerateRefreshToken`). A sweep that runs while a winning request is between redemption and successor creation misses the row that request is about to insert. So every event that ends a grant first sets the **authorization** to `revoked`, durably, and only then sweeps its tokens. Every redemption (below) requires the authorization to be `valid` in the same statement that redeems. A successor created after the authorization was revoked therefore exists as a row but can never be redeemed. The interleaving costs at most that successor's access token, which lives out its lifetime of up to 10 minutes. No transaction or lock is held across OpenIddict's pipeline.

This reverses r2's rejection of Norse-side authorization revocation. OpenIddict leaves the authorization valid on replay so that a client can start a new flow, and here a new flow creates a new ad-hoc authorization anyway, so revoking the old one costs nothing.

**The guarded redemption (replaces OpenIddict's `RedeemTokenEntry`).** `TryRedeemAsync` is not a conditional transition. It sets `Redeemed` and saves, without first refusing an already-redeemed or revoked token, and a freshly loaded instance carries a current concurrency value. Its `false` also covers nonfatal storage errors, not just lost races. So Himinbjörg removes the built-in `RedeemTokenEntry` handler (`RemoveEventHandler(RedeemTokenEntry.Descriptor)`) and registers `RedeemTokenEntryGuarded` in its place, at the same order on `ProcessSignInContext`, for authorization codes and refresh tokens alike. It is one provider statement through `ProviderStatements`, both providers:

```sql
UPDATE tokens SET status = 'redeemed', redemption_date = @now, concurrency_token = @fresh
WHERE id = @token AND status = 'valid'
  AND authorization_id = @authorization
  AND EXISTS (SELECT 1 FROM authorizations WHERE id = @authorization AND status = 'valid')
```

| Result | Meaning | Outcome |
|---|---|---|
| One row | The token was valid, its authorization was valid, and this request alone moved it to `redeemed` | The request proceeds. Nothing else in the pipeline redeems it again, because the built-in handler is gone |
| Zero rows | The token is no longer valid, or its authorization is no longer valid | **Reuse.** End the grant (below) and reject with `invalid_grant` |
| Any database exception | **Not reuse, and not known to be unchanged** | The request fails as an HTTP 500 with nothing minted, the statement is never retried, and the exception is never classified as reuse. The token's status afterward is *definite* (still `valid`) only when the failure happened before the statement was sent, such as a failed connection open. Any failure after it was sent, a lost commit acknowledgement included, is *indeterminate*: the token may be `valid` or `redeemed`. The identity store's zero-retry strategy holds here for that reason, since a blind retry of a committed redemption returns zero rows and would misread the server's own write as reuse. A client that retries after an indeterminate failure presents a token that may now be `redeemed`, which the accepted reuse policy then treats as reuse |

**What reuse means.** Reuse is any second presentation of a code or refresh token. That includes a correct, serialized client retrying after the server committed its rotation but the response was lost. Under ruling 12 that retry also ends the grant (see the client contract below). Reuse is not evidence of misbehavior on its own.

**Ending a grant** is one procedure, `EndGrant(authorizationId)`, used by every path below:
1. **Set the authorization to `revoked`.** A conditional update from `valid`, and idempotent: zero rows when it is already `revoked` is still success. Because it is idempotent, unlike the guarded redemption, it is retried, up to 3 attempts. The outcome is reported as one of two states:
   - *confirmed*: a definite success on some attempt;
   - *unconfirmed*: every attempt failed or was indeterminate.
2. **Revoke every token of the authorization** (`RevokeByAuthorizationIdAsync`, through the `.Outcome` views), only after step 1 is confirmed.

Step 1 is the boundary. Step 2 is hygiene, which pruning would eventually do anyway.

**What failures mean:**
- **Step 1 unconfirmed.** The presented token is still refused, because it was not valid or its user check failed either way, so the request still answers its rejection. But the grant end is **not** reported as confirmed: it logs an Error (`GrantEndUnconfirmed`, authorization id only) and increments `norse.identity.grant_end_unconfirmed`. Until a later reuse or erasure confirms it, a successor created around this event is not protected by the durable boundary. This is the one residual window, and it is surfaced, never silent.
- **Step 2 failed after a confirmed step 1.** It logs a Warning. The durable boundary is intact, and no token of a revoked authorization can be redeemed.

**The paths that end a grant:**

- **Reuse caught at redemption.** The guarded statement returns zero rows (above).
- **Reuse caught at validation, at either endpoint (r5).** OpenIddict dispatches `ValidateTokenContext` for the token endpoint and for the revocation endpoint (`ValidateGenericToken`). Its own `ValidateTokenEntry`, which carries no endpoint filter, sweeps a grant's tokens whenever a redeemed token is presented at either. So the platform's rule is one rule: **presenting a code or refresh token that is not `valid` is reuse, at the token endpoint or the revocation endpoint, and ends the grant.**
  - Himinbjörg registers `EndGrantOnReuse` on `ValidateTokenContext`, ordered immediately before `ValidateTokenEntry`, with explicit filters: the endpoint is `Token` (grant `authorization_code` or `refresh_token`) or `Revocation`; the validated token type is an authorization code or a refresh token; token storage is enabled.
  - For such a token whose entry is not `valid` (`redeemed`, `revoked` or `inactive`), it runs `EndGrant` and rejects: `invalid_grant` at the token endpoint, `invalid_token` at the revocation endpoint, which OpenIddict normalizes to HTTP 200 there (§5.7).
  - Every other `ValidateTokenContext` dispatch (access tokens, id-token hints, client assertions) passes through untouched.
  - The built-in sweep then never runs for these cases, and when it would have, the same durable boundary has already been applied.
- **A late successor under a revoked authorization** may be refused before the guarded statement by OpenIddict's own `ValidateAuthorizationEntry`, which follows `ValidateTokenEntry`. Either refusal point is correct. The guarded statement is the backstop, so a token the earlier checks pass can still never redeem under a revoked authorization.
- **Stamp, erasure.** The token endpoint's check (§5.4) rejects the presented grant and runs `EndGrant` on it. Lockout ends nothing (r6.3). Erasure goes further: `ErasureService.ShredAsync`'s one transaction already deletes the subject's tokens and authorizations, and it now also deletes the subject's sessions, or relies on the FK cascade if one reaches them, whichever the schema produces. After commit no code or refresh token can be redeemed. Outstanding access tokens live out their remaining lifetime of up to 10 minutes.
- **Self-revoke (refresh tokens only).** `/connect/revoke` (RFC 7009) with a **`valid`** refresh token revokes that one token (`TryRevokeAsync`), does not run `EndGrant`, and leaves the authorization `valid`.
  - That ends the grant's ability to mint, because the grant's only live refresh token is now `revoked`.
  - A later presentation of that revoked token, at either endpoint, is reuse and ends the grant (the rule above).
  - With a refresh token that is already **not `valid`**, a self-revoke is reuse and ends the grant. That includes a predecessor after rotation, or the token a refresh just redeemed in a race. This is exactly what OpenIddict's own sweep would do there, made durable.
  - **Authorization codes are not revocation targets.** OpenIddict's revocation `ValidateTokenType` accepts only access and refresh tokens. A **valid** code presented there answers `unsupported_token_type` (HTTP 400) and changes nothing: not the code, not its authorization.
  - **A redeemed code** presented there is still reuse under the separate detection rule, because `EndGrantOnReuse` sees it during generic validation, before `ValidateTokenType`. It ends the grant and answers HTTP 200. That ordering is a halting premise below. Code reuse is authoritatively detected at the token endpoint either way.
  - **Access tokens** presented there are accepted by OpenIddict, which marks their entry `revoked` and answers 200. `EndGrantOnReuse` never handles them, so the grant is not ended. Bearer validation does not consult token entries (§11), so a revoked access token keeps working until it expires. An access token is not a meaningful revocation target, and no first-party client should send one.
- **Authorization-wide revoke.** "Revoke all my grants", for a user or a client, is Train 5's. It will run `EndGrant` per authorization.

**Unverified premises, gated in §10:**

- that `RemoveEventHandler` removes `RedeemTokenEntry` and that the guarded handler then runs at its order;
- that OpenIddict's own validation rejects nothing the guarded statement would accept;
- that `EndGrantOnReuse` runs before `ValidateTokenEntry` at both endpoints, and only under its filters;
- that, with §5.2's two-factor plumbing in place, every credential sign-in path, immediate and deferred, reaches the `norse_reauth` stamp point with its resolved landing;
- that `EndGrantOnReuse` sees a redeemed authorization code presented at `/connect/revoke` during generic token validation, before revocation's `ValidateTokenType` refuses the type.

If any of these fails, the plan halts.

**The client contract** (recorded for Train 6 and every first-party native client):
- Serialize refreshes.
- On `invalid_grant` from a refresh, discard both tokens and start a new authorization through the system browser. Never retry the refresh, and never retry it with an older token.
- A lost response to a successful refresh is unrecoverable by design. The retry is reuse and ends the grant, so the client signs in again. The cost is one sign-in after a network failure at exactly the wrong moment, accepted under ruling 12.

### 5.6 Pruning

Nothing prunes OpenIddict rows today. `AddNorseIdentity` registers an `OpenIddictPruningService` (`BackgroundService`, `Identity:OpenIddict:Pruning`, with `Enabled`, `Interval` and `MinimumAge`; `MinimumAge` defaults to 14 days, the refresh lifetime; all validated on start). Each run prunes, in this order, through `.Outcome` views over the three managers:

1. tokens;
2. authorizations;
3. sessions.

Each step uses the `MinimumAge` threshold. The order is required: `IOpenIddictSessionManager.PruneAsync` removes only invalid sessions with no token attached and documents that tokens are pruned first, and the same holds for authorizations. A failed run logs an Error and never stops the host. The Quartz integration is not taken: it would bring Quartz.NET into the platform for one job.

*As built (§13): the order is tokens → sessions → authorizations; sessions carry an authorization FK, and authorization pruning checks tokens only.*

### 5.7 Protocol errors

The authorize, token and revocation endpoints answer **standard OAuth 2.0 / OIDC error responses** with the optional `error_description` and `error_uri` suppressed, and no `Outcome` envelope:

- The token endpoint answers a JSON body with the required `error` member and the RFC 6749 §5.2 status, `invalid_client` included where applicable.
- The revocation endpoint answers a request error (a malformed request, client authentication failure, an unsupported token type) the same way. An unknown, expired, already-revoked or already-redeemed token is **not** an error: per RFC 7009 §2.2 it answers HTTP 200, indistinguishable from a successful revocation. OpenIddict's `NormalizeErrorResponse` already does this, and §10 pins it.
- The authorize endpoint answers a redirect to the client's redirect URI carrying `error` and `state` once the client and redirect URI are validated, and an error page response with no redirect before then (an unknown client or a mismatched redirect URI is never redirected to).

The resource edge's bare-401 discipline (#49, #50) applies to the gRPC lanes, not to these endpoints.

*As built (§13): OpenIddict's own authorize refusals (missing or `plain` challenge, unknown client, wrong redirect URI, the `prompt=none` combination) answer 400 pages without redirect; the endpoint refuses a repeated or case-variant singular parameter with `invalid_request`.*

### 5.8 The `.Outcome` surface

The IL scan `No_handler_service_or_endpoint_calls_raw_Identity_or_OpenIddict_manager_methods` covers the new endpoints and the pruner. Each OpenIddict manager call they make (token, authorization and session managers) goes through a new or extended `.Outcome` view in `Identity.Outcomes`, mapped by `IdentityOutcomeMap`.

## 6. Freshness and step-up

### 6.1 The `auth_time` claim

The cookie's `IssuedUtc` is not authentication time: sliding renewal resets it, Train 3's stamp-rotation refreshes re-sign the cookie, and the security-stamp validator rebuilds the principal every 30 minutes. So the principal carries an explicit `auth_time` claim, in integral epoch seconds, which is not PII:

- **Stamped** by `NorseSignInManager` on every sign-in that presents a credential: password, two-factor completion, recovery code, passkey assertion, and external login completed with the second factor.
- **Carried, never re-stamped, on the cookie:** by `NorseSignInManager.RefreshSignInAsync` and by `SecurityStampValidatorOptions.OnRefreshingPrincipal`.
- **Carried, never re-stamped, on the token paths:** cookie → authorize endpoint → code → initial access, id and refresh tokens → every refreshed token, by §5.4's mint-from-the-grant rule. Nothing on the token paths depends on OpenIddict restoring the claim: `RestoreInternalClaims` restores only `oi_`-prefixed private claims, and `auth_time` is carried explicitly.
- **`norse_reauth`, the transient re-authentication binding (r5).** Stamped only by `NorseSignInManager`, only on a credential sign-in whose resolved landing carries a valid marker (§5.2). Its value is that marker's id: opaque, not PII.
  - It is never carried by `RefreshSignInAsync` or `OnRefreshingPrincipal`. A refreshed cookie drops it, which can only cost a re-challenge.
  - It never enters a token.
  - It is meaningless once its marker is burned or expired.
- **The allowlist amendment:** the cookie principal is allowlisted to four persistent claims (opaque `Guid` id, roles, security stamp, `auth_time`) plus the transient `norse_reauth`. The Himinbjörg `CLAUDE.md` fact is amended in the same change.
  - *As built (§13): the cookie also keeps the framework's authentication-method markers (`amr`, `ClaimTypes.AuthenticationMethod`).*
- **Existing cookies.** A cookie issued before this train carries no `auth_time`, so it reads as stale (§5.2 step 1, §6.3). Its holder re-authenticates once at the first protected operation or authorize request.

### 6.2 One claim, both lanes

The authorize endpoint enforces `max_age` against it and copies it into the tokens. Midgard's requirement reads it off any principal, cookie or bearer, and never learns who minted it.

### 6.3 The requirement

- **Asgard** declares the policy name (`NorsePolicies.RecentAuthentication`, in `Abstractions.Components`, declared through `[NorsePolicy]` like the platform policies) and the error code `reauthentication-required`.
- **Midgard** implements `RecentAuthenticationRequirement`. A principal with a missing, unparseable or future `auth_time`, or one older than the window, fails with a distinguishable `AuthorizationFailureReason`. The window is `Norse:Authentication:RecentWindow`, default 10 minutes, validated on start.
- **The failure mapping.** Midgard's pipeline maps that reason, and only that reason, to `Outcome` `Failed(Problem)` with category `Forbidden`, the code `reauthentication-required` and a server-built `NextUrl` of `/Account/Login?returnUrl=<the page the operation serves>`.
  - Every other authorization failure stays the bare 403 it is today.
  - The return URL is server-authored, never client-composed.
  - On the HTTP lane, the external-login link start (a browser GET, with no `Outcome` to carry) answers the same failure reason with a redirect to that same URL.
- **Applied to** the commands behind passkey add (`GetPasskeyCreationOptions` and `AddPasskey`), two-factor disable (`DisableTwoFactor`), the external-login link start (`GET /Account/Manage/LinkExternalLogin`, as endpoint metadata), and the authenticator key display (`GetAuthenticatorSetup` and `ResetAuthenticator`). Each carries the freshness policy alongside `AuthNPolicies.Account`.
  - *As built (§13): the link start declares its landing as endpoint metadata but evaluates freshness in an endpoint filter, after the account policy, redirecting only on `NorseErrorCodes.ReauthenticationRequired`.*

### 6.4 The re-authentication hop

`Login.razor` does not redirect a signed-in user, so the existing password and passkey sign-ins re-prove the credential, re-issue the cookie with a new `auth_time`, and land back on the return URL. No Heimdall UI change. §10 pins it, so a later "already signed in" redirect on the login page breaks a test rather than silently breaking step-up.

## 7. Lanes and policies

### 7.1 The Midgard leak, removed

Midgard today references `OpenIddict.Validation.AspNetCore`, forwards `NorseSchemes.Machine` to `OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme` in `AuthenticationBuilderExtensions`, and carries an authenticate-before-challenge workaround in `NorseGrpcHandler` that exists only because of OpenIddict's challenge handler (decompiled 7.6.0, #49). After this train:

- The package reference and the `using` are gone. `AddNorseAuthentication()` no longer registers `Norse.Machine`.
  - *As built (§13): it no longer registers `Norse.Machine` as an OpenIddict forward; `Norse.Machine` is Midgard's own `NorseBearerLaneHandler`.*
- **One bearer source, registered by Himinbjörg.** Midgard declares the scheme name `NorseSchemes.Bearer` (`Norse.Bearer`), a contract that a bearer-validating realm must register and Midgard never implements. Himinbjörg registers `Norse.Bearer` as a forward to the one OpenIddict validation scheme. It owns whatever authenticate-before-challenge behavior OpenIddict 8 still needs, re-verified against 8.0.0-preview.4, since the workaround may no longer be needed. A host that registers `NorseSchemes.Machine` or `NorseSchemes.User` traffic without a `Norse.Bearer` scheme fails at startup, not at the first request.
- **Both bearer lanes, owned by Midgard.** `Norse.Machine` and `Norse.User` become Midgard's own handlers (`NorseBearerLaneHandler`, one type, two registrations parameterized by lane):
  - each authenticates `Norse.Bearer` by name, rebuilds the resulting identity with its lane stamped (§7.3), and delegates challenge and forbid to `Norse.Bearer`;
  - `NorseLaneSelector` keeps routing façade controllers straight to `Norse.Machine`, which is now Midgard's handler, so the machine result passes through Midgard's interception point;
  - `NorseGrpcHandler` reaches `Norse.User` the same way (§7.2);
  - `NorseGrpcHandler` contains nothing OpenIddict-shaped.
- A reference scan, in the style of `Migration_assembly_does_not_reference_Identity_Web_Server_or_AspNetCore`, pins that no Midgard assembly references any `OpenIddict.*` assembly. The #49 machine-lane regression facts move with the code and stay green.

### 7.2 gRPC lane selection

`NorseLaneSelector` keeps routing gRPC endpoints to `NorseGrpcHandler`. Inside it, an `Authorization: Bearer` header selects `Norse.User`, and the cookie is then not authenticated at all. A request carrying both is decided by the bearer alone, so two identities never mix in one request. With no bearer header, the cookie path runs exactly as today.

### 7.3 Lane stamping

Asgard declares the lane names (`NorseLanes.Identity`, `NorseLanes.User` and `NorseLanes.Machine`, in `Abstractions.Components`, so Heimdall's client-safe policy classes can name them).

Every Midgard handler that completes authentication rebuilds the authenticated `ClaimsIdentity` with its `AuthenticationType` set to the lane that produced it. The interception points:

| Handler | Stamps |
|---|---|
| `NorseBrowserHandler` | `Identity`, for the identity cookie, the anonymous cookie included (the anonymous role still distinguishes it) |
| `NorseGrpcHandler`, cookie path | `Identity`, same |
| `NorseBearerLaneHandler` as `Norse.User` | `User` (reached through `NorseGrpcHandler`, §7.2) |
| `NorseBearerLaneHandler` as `Norse.Machine` | `Machine` (reached directly from `NorseLaneSelector` for façade controllers) |
| `NorseProbeHandler` | nothing, because it authenticates nothing |

Nothing a token or cookie carries can set the lane: it is the authentication type of the identity Midgard builds, not a claim.

**The helpers and requirement.** Asgard declares the policy helpers `RequireLane(params string[])`, `AllowAnyLane()` and `RequireAudience(string)` (an `aud` claim equal to the value). They are `AuthorizationPolicyBuilder` extensions adding requirement records with no server types. The lane requirement's handler is Midgard's:

- On the server, it reads the stamped `AuthenticationType`.
- **The lane's audience (r6.3).** Before stamping, `NorseBearerLaneHandler` requires the validated identity's `aud` to be exactly its lane's audience (`Norse.Facade` for `Machine`, `Norse.User` for `User`), compared ordinally. The other platform audience, both, or none fail authentication, with no fallback. So a `Machine`-stamped principal is always a machine token and a `User`-stamped one always a user token, whatever route selected the lane.
- On the client side (WASM), Midgard's `Infrastructure.Web.Client` registers a handler that treats the client's principal as the `Identity` lane. The browser only ever holds the cookie identity, and the client-side mirror of the policies must keep evaluating as it does today.

### 7.4 Audiences

Asgard declares the user audience `Norse.User` beside the existing `Norse.Facade`. The scheme and the audience share a string but are separate constants with separate checks. Validation accepts both audiences. Because one validation scheme now accepts both, every bearer-admitting policy pins its audience. The pin matters concretely: a machine principal carries a `Guid` `NameIdentifier` (the application's id).

### 7.5 The policy table

**Closed by default.** The generated `AddNorsePolicies()` (Midgard's policy-registration generator, the one that already discovers every `[NorsePolicy]` declaration) appends `RequireLane(Identity)` to every declared policy whose configure method adds no lane requirement of its own: no `RequireLane`, no `AllowAnyLane`.

- A generator diagnostic is not needed, because the default is the enforcement: a new policy in any realm admits the cookie identity and nothing else until it names another lane.
- `AllowAnyLane()` is the explicit, greppable opt-out. Its two users are `Probe`, which has no principal, and `RecentAuthentication`, which always stacks with the operation's own policy and so must never narrow the lane on its own.
- This closes every existing lane-blind policy: Heimdall's `Public` and Asgard's `Anonymous` without touching their realms, and Mímir's `ReferencePolicies.Public`, which then declares its lanes explicitly (r6.2). Every one of them is only `RequireAuthenticatedUser()` today.
- `Public` matters concretely: `AuthenticationService` exposes eleven operations under it, among them `ForgotPassword`, which can send an email.

| Policy (owner) | Today | After this train |
|---|---|---|
| `NorsePolicies.Machine` (Asgard) | `RequireAuthenticatedUser()` | `RequireLane(Machine)` + `RequireAudience(Norse.Facade)` |
| `NorsePolicies.Probe` (Asgard) | always succeeds | `AllowAnyLane()`, otherwise unchanged |
| `NorsePolicies.Anonymous` (Asgard) | `RequireAuthenticatedUser()`: **lane-blind** | default-closed to `Identity`; declaration unchanged |
| `AuthNPolicies.Public` (Heimdall) | `RequireAuthenticatedUser()`: **lane-blind; eleven gRPC operations a bearer would reach** | default-closed to `Identity`; declaration unchanged |
| `ReferencePolicies.Public` (Mímir) | `RequireAuthenticatedUser()`: **lane-blind** | `RequireAuthenticatedUser()` + `RequireLane(Identity, Machine)`, explicit (r6.2): reference reads are the one operation policy a machine principal shares with the cookie; the REST façade still requires `Machine` + `Norse.Facade`, and gRPC never selects the machine lane (§7.2) |
| `AuthNPolicies.Account` (Heimdall) | authenticated, not anonymous: **lane-blind** | default-closed to `Identity`; declaration unchanged |
| `AuthNPolicies.AccountRead` (Heimdall, new) | — | `RequireLane(Identity, User)`, plus: not anonymous, and a `User`-lane principal must carry `aud = Norse.User` |
| `IdentityPolicies.Self` (Heimdall) | authenticated, not anonymous: **lane-blind** | same shape as `AccountRead` |
| `IdentityPolicies.MaskedDisclosure` (Heimdall) | `RequireRole(System)`: **lane-blind** | `RequireLane(Identity)` + `RequireRole(System)`, explicit because it is a disclosure door |
| `NorsePolicies.RecentAuthentication` (Asgard name, Midgard requirement) | — | §6.3, admitted lanes as the operation's other policy decides; declared `AllowAnyLane()` so it never narrows on its own |

Two consequences:

- `GetAccountStatus` moves from `Account` to `AccountRead`, and the method's endpoint metadata mirrors it.
- `AccountCaller` reads the configured `UserIdClaimType`, which the bearer principal carries, so it works unchanged.

A user bearer is refused by every `Account` operation and by `MaskedDisclosure` twice over: wrong lane, and no roles in the token (§5.3). Yggdrasil's own `RequireAuthorization(IdentityPolicies.MaskedDisclosure)` endpoint inherits the lane pin.

## 8. Hosting (Yggdrasil)

One PR with every bump folded:

- It pins `OpenIddict.*` `8.0.0-preview.4` in `Directory.Packages.props`.
- `AddNorsePolicies()` picks up `RecentAuthentication` and `AccountRead` through generated discovery.
- `Identity:OpenIddict:Pruning` and `Norse:Authentication:RecentWindow` are added to configuration.
- `MapNorseOpenIddictEndpoints()` additionally maps the authorize and revocation endpoints. Its name is unchanged this train (§11).
- It folds Mímir's bump (r6.2) with the others; the composition facts of §10.12 pin Mímir's façade and gRPC boundary there.

## 9. Train and ship gates

One fork per realm, serial within a realm, each behind its own gate (PR merged, CI green, tagged, published):

1. **Asgard:**
   - `NorseLanes`, the `Norse.User` audience constant, `RequireLane`/`AllowAnyLane`/`RequireAudience`;
   - `NorsePolicies.RecentAuthentication`, the `reauthentication-required` code;
   - `NorsePolicies.Machine` pinned to its lane and audience, `Probe` on `AllowAnyLane()`.
2. **Midgard:**
   - the leak removed, `NorseSchemes.Bearer`, `NorseBearerLaneHandler` for both bearer lanes (§7.1);
   - lane stamping (§7.3) and the lane-requirement handlers, server and client;
   - the default-closed lane in the generated `AddNorsePolicies()` (§7.5);
   - gRPC bearer selection (§7.2);
   - `RecentAuthenticationRequirement` and the failure-reason mapping.
3. **Heimdall:** `Self` and `MaskedDisclosure` lane-pinned; `AccountRead` added. `Public` and `Account` are closed by Midgard's default without a declaration change. No component change.
4. **Himinbjörg:**
   - OpenIddict 8 and the six entities, the squash;
   - the protocol surface, the reauthentication marker and its ledger use, the grant-carried authentication;
   - `RedeemTokenEntryGuarded` (replacing the built-in `RedeemTokenEntry`), `EndGrantOnReuse`, `EndGrant`, and revocation;
   - `auth_time`, the `Norse.Bearer` source scheme, the pruner;
   - the freshness policy on the four operations, `GetAccountStatus` onto `AccountRead`.
5. **Mímir (r6.2):** `ReferencePolicies.Public` declares `RequireLane(Identity, Machine)`. Its fork ships after its updated dependencies (Asgard, Midgard) and before Yggdrasil's composition.
6. **Yggdrasil:** §8.

Bragi is untouched. Mímir's declaration is pinned in Mímir, and its façade and gRPC boundary by Yggdrasil facts at the composition root. Himinbjörg's coverage floor stays at 85. The end-to-end protocol walk runs against a disposable Postgres; the full AppHost stand-up follows the volume wipe.

## 10. Testing

Test-first in every task. Real-manager TestServer facts over the dual-provider container fixtures, in the `OpenIddictTokenEndpointTests` pattern, with test-registered public clients and a controllable `TimeProvider`.

1. **Spike (first task, halting).** SQL Server system versioning over the JSON-typed OpenIddict columns (§4.2).
2. **Schema.**
   - The amended temporal-model facts: `resources` temporal, `sessions` not.
   - Catalog facts on both providers.
   - An erased subject's sessions are gone.
3. **Code + PKCE.**
   - The happy path.
   - Each of these is refused: no `code_challenge`; `plain`; a redirect URI mismatch, with no redirect issued; an unknown client, with no redirect issued.
   - The anonymous principal is challenged to login.
4. **`prompt` and `max_age`.**
   - `prompt=none` returns `login_required` to the redirect URI with `state` preserved, both for an anonymous principal and for a stale one, and never challenges.
   - `prompt=none` combined with `login` returns `invalid_request`.
   - `prompt=login` challenges, and re-entry with the marker issues a code.
   - `max_age=0` with a deliberate delay before re-entry issues a code, never a second challenge.
   - A stale `max_age` challenges.
   - The marker is transaction-bound. Each of these re-challenges: a re-entry with a changed `code_challenge`, `state`, `nonce` or `scope`; a marker for another client or redirect URI; an expired marker; a marker issued to another user.
   - The marker is one-shot. After request A completes, a new request B with the same client and redirect URI and `prompt=login`, presenting A's marker inside five minutes, re-challenges and is not issued a code. Two concurrent re-entries with one marker issue at most one code.
   - Binding, positive. Each of the five credential paths, signing in with the marker's authorize request as its landing, stamps `norse_reauth` with that marker's id, and re-entry is issued a code. Each runs twice: once with immediate cookie issuance, once through deferred sign-in (`IDeferredSignIn` and the `/_auth/complete` hop).
   - The two-factor plumbing. For authenticator, recovery-code and external-login completions, the landing is on `ReauthLandingItemName` during `SignInWithClaimsAsync` and absent afterward, on success and on a thrown sign-in alike. The landing `CompleteTwoFactorSignInAsync` returns is unchanged.
   - Same-second progress, on a frozen clock that is never advanced. A challenge and several successive re-authentications all complete in one second, and each re-entry is satisfied by its own bound sign-in. None is re-challenged.
   - Two pre-existing cookies. Cookies A and B come from separate sign-ins, with B issued two seconds before the challenge. The challenge receives A, and re-entry presents B. It re-challenges, and with `prompt=none` answers `login_required`.
   - Anonymous challenge. A marker is obtained with no cookie, and re-entry presents a pre-existing, recent cookie for some user. It re-challenges.
   - Re-entry with the challenged cookie itself re-challenges.
   - A sign-in whose landing carries an expired or invalid marker, or a marker naming another user, stamps nothing.
   - A cookie bound to marker M does not satisfy a different pending marker M′.
   - A refreshed cookie (`RefreshSignInAsync`) has lost `norse_reauth`.
   - A missing or malformed `auth_time` on the cookie is stale, never fresh.
   - A malformed `max_age` returns `invalid_request`.
5. **Grant-carried authentication.**
   - Each of stamp rotation and erasure between code issuance and redemption makes the redemption fail with `invalid_grant`. A lockout between them does not (r6.3).
   - After repeated refreshes across advanced time, every token's `auth_time` equals the original cookie's.
   - `norse_stamp` is present in the code and the refresh token and absent from the access and id tokens.
   - User tokens carry no role claims.
6. **Refresh and replay.**
   - Rotation issues a new refresh token.
   - Presenting the redeemed one again (sequentially, through validation) fails with `invalid_grant`. The authorization reads `revoked` and every one of its tokens reads `revoked` in the store.
   - Presenting a redeemed code again does the same.
   - An independent grant (a second sign-in by the same user on the same client) still refreshes throughout.
7. **The guarded redemption and grant boundary (deterministic interleavings, halting gate).** Each interleaving is driven by named test-only pause points (test-registered `ProcessSignInContext` and `ValidateTokenContext` handlers awaiting per-request signals), never by releasing two requests at one barrier.
   - **Premises first.**
     - The built-in `RedeemTokenEntry` is absent from the server's handler list after `RemoveEventHandler`.
     - `RedeemTokenEntryGuarded` runs at its order.
     - `EndGrantOnReuse` runs before `ValidateTokenEntry`.
     - The guarded statement moves exactly one row from `valid` to `redeemed` on both providers, and zero rows for an already-`redeemed` token, a `revoked` token, and a `valid` token under a `revoked` authorization.
   - **Simultaneous load.** Two requests both pass validation for one refresh token, then redeem. Exactly one succeeds. The other answers `invalid_grant` and ends the grant.
   - **Late load.** Request A passes validation and pauses before redemption. Request B redeems and completes. A resumes, its guarded statement returns zero rows, and A answers `invalid_grant` and ends the grant. The same with a revocation of the token, instead of B, before A resumes.
   - **Successor after the sweep.** Request A redeems and pauses after redemption, before `GenerateRefreshToken`. Request B presents the same token, loses, ends the grant and returns. A resumes and is issued a successor refresh token. That successor's next refresh answers `invalid_grant`. The test asserts the rejection and the authorization's `revoked` status, not which check refused it: `ValidateAuthorizationEntry` may refuse it before the guarded statement is reached. A's access token is the only surviving artifact.
   - **The SQL backstop, directly.** At statement level on both providers, the guarded statement returns zero rows for a `valid` token whose authorization is `revoked`. This is independent of whether any request ever reaches it.
   - **Successor after reuse at validation.** The same, with B's reuse caught by `EndGrantOnReuse` during validation instead of at redemption.
   - **Definite failure.** A failure injected before the guarded statement is sent (a connection-open failure) answers HTTP 500, mints nothing, leaves the token and authorization `valid`, is not retried, and is not classified as reuse.
   - **Indeterminate failure.** The statement commits and its acknowledgement is then dropped (an interceptor that throws after `ExecuteNonQuery` returns). The request answers HTTP 500, mints nothing, is not retried, and does not run `EndGrant`. The token reads `redeemed` afterward. The client's retry with that token is then reuse and ends the grant, as the policy accepts.
   - **`EndGrant` failure.**
     - Step 1 failing on every attempt logs `GrantEndUnconfirmed`, increments the counter, still rejects the presented token, and reports nothing as confirmed.
     - Step 1 confirmed with step 2 failing leaves the authorization `revoked` and every token unredeemable.
8. **Revocation.**
   - Stamp rotation and erasure each make the next code redemption or refresh fail with `invalid_grant`. After a stamp rotation the authorization reads `revoked`, and every one of its tokens too.
   - **Lockout (r6.3).** A lockout reached through real wrong passwords leaves an existing refresh grant usable and an otherwise-valid pending code redeemable, while credential sign-in stays blocked. Stamp rotation during lockout still rejects the presented grant and ends it. Reuse during lockout still ends the grant. Independent grants are unaffected. After erasure the authorization and its tokens are absent, because `ErasureService` deletes them (§5.5; corrected as built, §13).
   - Self-revoke of the current refresh token answers 200, revokes that token only, and leaves the authorization `valid`. The next refresh with it is reuse: it answers `invalid_grant`, and the authorization reads `revoked` afterward. An independent grant survives throughout.
   - **Revoking a predecessor.** Rotate R0 into R1, then submit R0 to `/connect/revoke`. The response is 200. The authorization reads `revoked`, R0 and R1 both read `revoked`, and a refresh with R1 answers `invalid_grant`.
   - A refresh racing self-revocation, at named pause points in both orders:
     - revocation first: the refresh's guarded statement returns zero rows, the refresh is refused, and the grant ends;
     - refresh first: the successor is issued, and the revocation then presents a `redeemed` token, which is reuse and ends the grant, so the successor cannot refresh.
   - `EndGrantOnReuse` filters: an access token or an id-token hint presented at either endpoint is never handled by it.
   - Codes at the revocation endpoint. A **valid** authorization code answers `unsupported_token_type` with no state change: the code still redeems at the token endpoint afterward, and the authorization stays `valid`. A **redeemed** code answers 200 and ends the grant: the authorization reads `revoked`, and the grant's refresh token cannot refresh.
   - An access token at the revocation endpoint answers 200, leaves the authorization `valid`, and the grant's refresh token still refreshes.
   - The client contract: a retry after a successful refresh whose response was dropped, with the old token, ends the grant, and the client's next step is a new authorization.
9. **Protocol errors.**
   - The token endpoint answers JSON with `error` and the RFC status, without `error_description` or `error_uri`.
   - The revocation endpoint answers a malformed request or failed client authentication the same way.
   - Revoking an unknown, expired, already-revoked or already-redeemed token answers HTTP 200, identical to a successful revocation.
   - Authorize errors arrive as redirects carrying `error` and `state` once the client is validated, and never as redirects before then.
   - No endpoint answers an `Outcome` envelope.
10. **`auth_time` on the cookie.**
   - Stamped by each of the five credential paths.
   - Unchanged across `RefreshSignInAsync` and `OnRefreshingPrincipal`, and never stamped by either.
   - `norse_reauth` never appears in any token.
   - The allowlist, four persistent claims plus the transient binding, is pinned.
     - *As built (§13): plus the framework's authentication-method markers, pinned by name.*
   - A pre-train cookie with no `auth_time` reads as stale.
11. **Freshness.**
    - On each of the four operations, a stale caller gets `reauthentication-required` with the server-authored `NextUrl`.
    - The link start redirects instead.
    - A fresh caller passes.
    - Other authorization failures are still a bare 403.
    - A signed-in user re-proves through `/Account/Login` and lands back (§6.4).
12. **Lanes and policies.**
    - A user bearer alone, and a user bearer plus a valid identity cookie, are each refused by every `AuthNPolicies.Account` operation.
    - The same two cases are refused by `Public` operations that would otherwise succeed: `ForgotPassword` for a confirmed address (no email sent) and `GetSignInMethods`.
    - A cookie caller still reaches both.
    - The generated `AddNorsePolicies()` appends the `Identity` lane to a policy that declares no lane, and leaves `RequireLane` and `AllowAnyLane` declarations alone.
    - Mímir's façade and gRPC boundary (r6.2), each a Yggdrasil fact through the real pipeline:
      - a real machine token reaches the REST country lookup, through both the endpoint's and the mediator's authorization (positive, through `Norse.Machine`'s stamping);
      - an identity cookie alone is refused by the REST façade, and a cookie gRPC lookup still succeeds;
      - a user bearer, alone and with a valid identity cookie, is refused by the REST façade (`ReferencePolicies.Public` refuses the `User` lane);
        - *As built (§13): the selector routes a façade request to `Norse.Machine`. Before r6.3 the user bearer was stamped `Machine` and refused by the `Norse.Facade` audience pin (403). Since r6.3 the lane handler refuses its `Norse.User` audience at authentication, a bare 401.*
      - a machine token alone is refused by the gRPC lookup.
    - The same machine token is refused by `IdentityPolicies.Self`, `AuthNPolicies.AccountRead`, `AuthNPolicies.Account` and `AuthNPolicies.Public`.
    - A probe still reaches its endpoint with no principal.
    - WASM-side policy evaluation is unchanged for the cookie principal.
    - A host with no `Norse.Bearer` registration fails at startup.
    - A user bearer reaches `GetMyPersonalData` and `GetAccountStatus`.
    - A user bearer for a `System`-role user is refused by `MaskedDisclosure`.
    - A user token is refused by `Machine`.
    - The lane is the identity's authentication type, and no claim in a token can change it.
    - **The lane's audience (r6.3).** At the Midgard handler: the right audience authenticates, while a wrong, missing or dual audience fails. Through the composed host: a policy requiring only the `User` lane refuses a real machine token, and one requiring only `Machine` refuses a real user token. A wrong-kind bearer plus a valid identity cookie fails on the bearer path. Existing machine façade reads and user-bearer operations stay green. Every wrong-kind refusal above, including the façade and gRPC facts in this item, is the bare 401 of the authentication challenge (`Bearer error="invalid_token"`), not a 403.
    - **The authorize reload (r6.3).** An erased user's surviving cookie mints no authorization or code. A stamp-rotated cookie is challenged and never issued a code. A locked-out user's valid cookie still gets a code.
13. **The leak.** The Midgard reference scan; the #49 machine-lane facts green after the move.
14. **Pruning.**
    - Expired tokens, then authorizations, then sessions are pruned past `MinimumAge`, and live rows are kept.
      - *As built (§13): the order is tokens, then sessions, then authorizations.*
    - A session with an attached token survives until its token is pruned.
    - `Enabled = false` prunes nothing.

## 11. Known gaps carried forward

- **The MCP door** (#50 scope items 2 and 3): dynamic client registration or its deliberate alternative, RFC 8414 / RFC 9728 metadata, the MCP resource server and its audience, and consent. A later train on top of this one.
- **Bearer on mutations:** how a token caller rotates a stamp without a cookie to refresh. Train 6.
- **Roles in user tokens** and a scope vocabulary: the authz-lockdown arc.
- **"Revoke all my grants"** (an authorization-wide revoke) and a grants list in Manage: Train 5.
- **Reuse is fail-closed:** any second presentation of a refresh token ends the grant (ruling 12), including a serialized client retrying after a lost response. The client contract (§5.5) records the recovery: discard both tokens and authorize again. A reuse leeway for a client that cannot tolerate this is a later decision, never a default.
- **Access tokens outlive grant revocation by up to 10 minutes.** Enabling OpenIddict validation's authorization-entry check would end them at once, at a database read per bearer request. That is a later decision for the MAUI train, with its own measurement.
- **OpenIddict 8 sessions and back-channel logout:** carried in the schema, unused, until a train wants sign-out to propagate to native clients.
- **userinfo, end-session and introspection endpoints:** not needed by any first-party client yet.
- **The `MapNorseOpenIddictEndpoints()` name** reveals OpenIddict at Yggdrasil's composition root. A neutral rename (for example `MapNorseTokenEndpoints()`) is a breaking chore across Himinbjörg and Yggdrasil, deferred to its own pass.
- **OpenIddict 8 RTM:** the Yggdrasil pin moves to the stable release when it ships. Any preview-to-RTM schema change means another squash, which the squash cycle already absorbs.

## 12. Alternatives rejected

- **Staying on OpenIddict 7.x.** It is stable, but the EF model rewrite and the new entities would arrive after `InitialCreate` is memorialized, as a real migration instead of a squash.
- **Per-login OpenIddict sessions as the revocation unit.** Signing out of the web would log the phone app out. That contradicts browser == cookie, native == tokens.
- **Erasure as the only revocation.** A password reset would leave existing refresh tokens alive.
- **The cookie's `IssuedUtc` as authentication time.** Sliding renewal and every refresh reset it.
- **Re-deriving `auth_time` and the stamp from the user at redemption** (r1). A code issued before a stamp rotation would mint a chain carrying the new stamp, and `auth_time` would either reset or be lost on every refresh.
- **Relying on OpenIddict to restore `auth_time`.** `RestoreInternalClaims` restores only `oi_`-prefixed private claims.
- **Stripping `prompt` from the return URL on re-entry** (r1). It does nothing for `max_age=0`, which re-fails by the time the browser returns. The data-protected continuation marker covers both.
- **Leaving the authorization valid on reuse** (r2, r3; reversed in r4). OpenIddict leaves it valid so a client can start a new flow, but a token sweep cannot reach a successor created after it. The authorization's status is the only durable boundary, and a new flow creates a new ad-hoc authorization anyway.
- **A transaction or lock held across OpenIddict's sign-in pipeline.** It would serialize grant events, but it means owning a transaction across code OpenIddict saves in several steps. The durable authorization status gives the same guarantee, "no successor can refresh", with no long-held lock.
- **`TryRedeemAsync` as the gate** (r3). It sets `Redeemed` unconditionally and reports nonfatal storage errors as `false`, so it is neither a `valid → redeemed` transition nor a reliable signal of reuse.
- **Permanent, reused authorizations.** Replay would revoke every token of every sign-in the user ever made on that client, and "the chain" would have no useful boundary.
- **Leaving `Public` lane-agnostic** (r2). Eleven anonymous-tier gRPC operations, `ForgotPassword`'s email among them, would answer a bearer.
- **Pinning lanes policy by policy.** It leaves the next lane-blind policy, in any realm, open; three existed already. The generated default closes them all and every future one.
- **Trusting OpenIddict's refresh redemption alone** (r2). It ignores a lost redemption to tolerate concurrent requests, so two successors can be minted from one token.
- **Timestamps as the evidence of re-authentication** (r3). Whole-second `auth_time` either accepts a pre-challenge sign-in in the challenge's second or re-challenges same-second sign-ins without bound.
- **A changed authentication id as the evidence** (r4). It proves a different sign-in, not a later one. A second pre-existing cookie, or any cookie after an anonymous challenge, passes. Only binding the credential presentation to the marker proves ordering.
- **Self-revoke as token-only regardless of the token's state** (r4). OpenIddict's own `ValidateTokenEntry` sweeps the grant when a redeemed token is presented at the revocation endpoint, so "token-only" was never true of the pipeline. The platform makes that sweep durable instead of pretending it doesn't happen.
- **Retrying the guarded redemption after an exception.** A committed redemption whose acknowledgement was lost would return zero rows on retry, and be misread as reuse of the server's own write.
- **A transaction-blind, reusable marker** (r2). It let one re-authentication satisfy a later `prompt=login` request inside the marker's lifetime.
- **Comparing `auth_time` against a sub-second challenge instant.** Integral seconds can't order the two within one second.
- **Inferring the lane from roles, claims or the identity's original authentication type.** A token-carried value can be issued; the cookie's type names a framework; re-authenticating the other scheme inside a policy would undo the bearer-over-cookie precedence.
- **OIDC-only step-up, or RFC 9470 on the token lane only.** Either leaves the four browser operations unguarded.
- **Bearer for everything under `AuthNPolicies.Account`.** It drags MAUI design into a backend train, and every cookie-refreshing handler would need a token-caller answer first.
- **A validation scheme per audience.** OpenIddict's validation registers under one fixed scheme name.
- **Everything in Himinbjörg, lanes and policies included.** Lanes are Midgard's, and policy names register at Yggdrasil's root.
- **An eager stamp-rotation sweep.** It needs every rotation call site. The check at redemption is one door.
- **OpenIddict's Quartz integration for pruning.** It takes a scheduler dependency for one job.

## 13. As built (2026-10-30, staging date)

Executed from plan r4 (`../plans/2026-10-29-openiddict-authorization-code-pkce-step-up.md`), one `feature/openiddict-v8` fork per realm: Asgard, Midgard, Heimdall, Himinbjörg, Mímir and Yggdrasil. Bragi is untouched. Every realm's work is staged on its fork, reviewed task by task; nothing is shipped yet. The release order is Asgard → Midgard → Heimdall → Himinbjörg → Mímir → Yggdrasil, each behind its own gate. Mímir needs only Asgard and Midgard, so it may ship any time after Midgard and before Yggdrasil. Yggdrasil's realm-tag bumps are folded into its one PR at release. `OpenIddict.*` is pinned at `8.0.0-preview.4.26456.66` in Yggdrasil's `Directory.Packages.props`.

### 13.1 What landed, by realm

- **Asgard (Phase 1; 246 tests green).**
  - `Abstractions.Components`: `NorseLanes`, `NorseAudiences` (`Norse.User` beside `Norse.Facade`), `NorseClaimTypes`, and `NorseLanePolicyExtensions` (`RequireLane`, `AllowAnyLane`, `RequireAudience`). The helpers add the `LaneRequirement`, `AnyLaneRequirement` and `AudienceRequirement` records. Blank lanes and audiences throw `ArgumentException`, so a misdeclaration cannot fail open.
  - `NorsePolicies.RecentAuthentication` with `RecentAuthenticationRequirement`, and `NorseErrorCodes.ReauthenticationRequired` (`reauthentication-required`).
  - `NorsePolicies.Machine` is `RequireLane(Machine)` + `RequireAudience(Norse.Facade)`, and `Probe` is `AllowAnyLane()`.
  - `Problem` gains `Code` and `NextUrl`. `LocalNavigation.IsLocal` is the shared, WASM-safe validator. `ReauthenticationLandingAttribute` (server-only, `Abstractions.Web.Server.Mediator`) declares a command's landing.
  - `OutcomeComponentBase` and `OutcomeFormComponentBase` navigate softly to a local `NextUrl` on `Forbidden` + `reauthentication-required`, through a private `ReauthenticationNavigation` injection.
- **Midgard (Phase 2 and final fix F1; 1021 tests green).**
  - The leak is gone: no `OpenIddict.*` reference in any `src` assembly, pinned by a scan of every `src/**/*.csproj` with a count guard. `NorseSchemes.Bearer` is the declared contract, `BearerSchemeValidation` fails a lane host without it at startup, and `NorseBearerLaneHandler` (with `NorseBearerLaneOptions`) is registered twice, as `Norse.Machine` and `Norse.User`.
  - Lane stamping: `IdentityLaneStamping` on the two cookie paths and `NorseBearerLaneHandler` on the bearer lanes rebuild every Midgard-authenticated principal as exactly one stamped identity. An `Authorization: Bearer` header on gRPC selects `Norse.User` alone, with no cookie fallback.
  - Server handlers `LaneRequirementHandler`, `AnyLaneRequirementHandler` and `AudienceRequirementHandler`; client mirrors `ClientLaneRequirementHandler`, `ClientAnyLaneRequirementHandler` and `ClientAudienceRequirementHandler`, registered by `AddNorseClientAuthorization()`. The client generator's `AddNorsePolicies()` bootstraps over that call and stands down without it.
  - The generated `AddNorsePolicies()` appends `RequireLane(Identity)` to every policy that names no lane.
  - Freshness: `RecentAuthenticationHandler` over `RecentAuthenticationOptions` (`Norse:Authentication:RecentWindow`, validated on start), `ReauthenticationRequiredReason`, and `ReauthenticationHop`, which builds `pathBase + /Account/Login + returnUrl`. `PolicyCache` partitions operation policies from freshness by name, and `AuthorizationBehavior` evaluates freshness last. The gRPC trailer carries `code` and `nextUrl`, which the client decoder rehydrates.
- **Heimdall (Phase 3; 445 tests green).** `AuthNPolicies.AccountRead` is new. `IdentityPolicies.Self` takes the same shape, `RequireLane(Identity, User)` with `aud = Norse.User` required on the user lane. `MaskedDisclosure` is `RequireLane(Identity)` + `RequireRole(System)`. No component change.
- **Himinbjörg (Phases 0 and 4 and final fix F2; 1873 tests green; `Identity.Web.Server` branch coverage 87.1%, floor 85).**
  - Schema: OpenIddict 8 with `NorseOpenIddictSession` (non-temporal `sessions`) and `NorseOpenIddictResource` (temporal `resources`). `NorseOpenIddictToken.SessionId` is an explicit FK scalar. `InitialCreate` is squashed on both providers.
  - The store link contract (§4.5): `NorseOpenIddictTokenStore`, `NorseOpenIddictAuthorizationStore` and `NorseOpenIddictSessionStore` over `OpenIddictLinks`, registered in `AddNorseOpenIddictCore()`.
  - The grant boundary: `AddNorseGrantBoundary()` removes `RedeemTokenEntry` and registers `RedeemTokenEntryGuarded` and `EndGrantOnReuse`. `GrantStatements` holds the guarded `UPDATE`, and `GrantTermination` (`IGrantTermination`, `GrantEnd`, `UnanchoredGrant`) ends a grant. The redemption runs under `ZeroRetryExecutionStrategy`.
  - The protocol surface: `OpenIddictAuthorizeEndpoint` with `AuthorizeTransaction`, `ReauthMarkers` and `ReauthMarker`. `OpenIddictExchangeEndpoint` mints from the grant through `UserPrincipalFactory`. Revocation is OpenIddict's own endpoint. `AddNorseErrorSuppression()` (`SuppressErrorDetails`, `SuppressChallengeErrorDetails`) strips `error_description` and `error_uri`.
  - The bearer source: `Norse.Bearer` is spelled once, in `ServiceCollectionExtensions.BearerScheme`, pinned equal to Midgard's constant, and `BearerChallengeHandler` gives it its bare-401 contract. Validation reads tokens from the `Authorization` header only.
  - `NorseSignInManager` stamps `auth_time` on credential sign-ins and carries it on refresh (T4-2). It stamps `norse_reauth` only through the `ReauthLandingItemName` slot (T4-1).
  - `OpenIddictPruningService` (`OpenIddictPruningOptions`). `ErasureService` deletes the subject's sessions too.
  - The `.Outcome` views over the token, authorization and session managers.
  - Freshness and its landings on the four commands; `GetAccountStatus` on `AccountRead`; the link-start freshness filter.
- **Mímir (Phase 5, Task 5.0, r6.2; 32 tests green).** `ReferencePolicies.Public` keeps `RequireAuthenticatedUser()` and declares `RequireLane(Identity, Machine)`. The façade is unchanged.
- **Yggdrasil (Task 5.1 and final fix F3; 196 pass, 0 fail, 4 skipped).** The skips are the RC3/Chromium 156 WASM browser limit. The OpenIddict pin is made effective through versionless `PackageReference`s in `Hosting.Web.Server` and `Hosting.Migrations.Service`. `appsettings.json` adds `Identity:OpenIddict:Pruning` and `Norse:Authentication:RecentWindow`. The WASM client's redundant `AddAuthorizationCore()` is removed. `CompositionLaneTests` pins Mímir's façade and gRPC boundary, Buvy's five halt-lift facts among them.

### 13.2 The halting gates

| Gate | Task | Outcome |
|---|---|---|
| H1: SQL Server versioning over the JSON-typed columns | 0.1 | **Passed.** The coverage is real on `applications` and on `scopes.resources`. `resources` itself has no JSON column on SQL Server (only `nvarchar`), so its leg proves versioning but no JSON interaction (§13.5) |
| H2: `RedeemTokenEntry` removed, guarded handler at its order | 0.2 | **Passed** |
| H3: `EndGrantOnReuse` before `ValidateTokenEntry`, under its filters, and sees a redeemed code at `/connect/revoke` | 0.2 → 0.3 | **Failed in 0.2 for refresh tokens, passed in 0.3** (§13.3) |
| H4: the guarded statement's row counts; OpenIddict accepts nothing the statement refuses | 0.2 → 0.3 | **The statement-level backstop passed in 0.2; the pipeline leg failed with H3 and passed in 0.3** |
| H5: every credential path, immediate and deferred, reaches the stamp point with its landing | 4.3 | **Passed:** five paths × {immediate, deferred through the real `IDeferredSignIn` and `/_auth/complete`}, all ten carrying `norse_reauth` equal to the marker id, all ten red before the plumbing |
| H6: OpenIddict 8 passes `norse_reauth` through to the passthrough | 4.4 | **Passed** on both providers |
| H7: the store link contract's five facts, then H2–H4 unchanged | 0.3 | **Passed:** five facts green on both providers; `PipelinePremiseSuite` 16/16 unchanged |

### 13.3 The three execution halts

- **H3/H4 → the store link contract (r6.1).** Task 0.2's premise facts failed for refresh tokens: 12 of 16 green, the same two red per provider. Under the platform's NoTracking default, OpenIddict's EF token store `FindByIdAsync` returns a detached entity. `GetAuthorizationIdAsync` then reads the unloaded navigation and returns null, so refresh, access and id tokens carried no authorization id into validation. `EndGrantOnReuse` was filtered out, `ValidateAuthorizationEntry` never ran, and the guarded handler saw no authorization. Buvy chose the complete store link contract (§4.5, plan Task 0.3, gate H7). Option C was rejected, and option B (a tracking exception) was ruled unnecessary.
  - **The 0.3 observation.** §4.5 and the r6.1 note predict that the stock stores would split the grant, with `AttachAuthorization` creating a fresh ad-hoc authorization on every refresh. On the stock stores the guarded handler refused R0 → R1 with `invalid_grant` first, leaving one authorization. The split was not observed, because the guard intercepted first. The note stays a prediction, derived from the source; the continuity fact pins the contract either way.
- **Task 5.1 → Mímir joins the train (r6.2).** A real `client_credentials` token was answered 403 at Mímir's REST façade. `CountriesController` and `CountryQuery` carry `ReferencePolicies.Public`, which the generated default closed to `Identity`, while the façade requires `Machine`, and no principal satisfies both. Buvy chose option A, and Task 5.0 landed it. Options B (split the façade from `Public`) and C (defer to Train 5) were rejected.
- **The final whole-branch review → r6.3.** The review raised two findings that needed a ruling.
  - Lockout ended grants, so wrong passwords against a known email could sign out every native client. Buvy chose the fourth option: lockout throttles credential sign-in only, and an issued grant survives it. The option of answering `invalid_grant` was rejected.
  - Lanes were chosen by route, not by token. A machine token on a `User`-lane route was stamped `User`, and only a policy's audience pin stood between it and a `RequireLane(User)`-only policy. Buvy chose the lane-handler audience check, with an explicit rule for ambiguous audiences.
  - A third finding was ruled inline: the authorize endpoint trusted a stale cookie without re-reading the user.
  - The fixes landed as F1 (Midgard), F2 (Himinbjörg) and F3 (Asgard's docs, Yggdrasil's composed facts, a Heimdall doc line).
  - F2 found that a wrong-kind bearer answered 403. `BearerChallengeHandler` re-authenticated the still-valid token, and OpenIddict then fell back to `insufficient_access`. The handler now sets `invalid_token` when the bearer itself succeeded and the caller named no error.

### 13.4 The end-to-end walk

Run 2026-10-30 against Yggdrasil's real `Hosting.Web.Server` on Kestrel (`https://localhost:5001`, Development, the dev certificate), backed by a disposable `postgres:19rc1` container on a throwaway port. The AppHost's `norse-pg-primary`/`norse-pg-replica` volumes were never touched. `Hosting.Migrations.Service` stood up `norse_identity` and `norse_reference` from the squashed `InitialCreate` and seeded `norse-machine`. Walk-only inputs:
- a throwaway self-signed signing certificate;
- `Norse:Authentication:RecentWindow` = 90 seconds;
- one public client inserted as a row: `norse-walk-native`, implicit consent, `ft:pkce` required, redirect `http://127.0.0.1:8765/cb`. No first-party native client is seeded by any realm yet.

The user registered through Heimdall's Register page. The browser was a real Chromium driven by Playwright, the protocol calls were real HTTP, and the loopback redirect was captured by Playwright's request routing.

**The browser environment limit.** Heimdall's pages render `InteractiveAuto`. Once Chromium had cached the WASM payload, the Login page booted the WebAssembly runtime, which failed to compile ("module uses a mix of legacy and new exception handling instructions", the RC3/Chromium 156 limit). That left a prerendered form that cannot submit (Ruling 46). The walk cleared the cache and aborted `_framework/*.wasm` downloads, so every page ran on the Server circuit. Every WASM leg is not walked for that reason.

| # | Leg | Request | Response |
|---|---|---|---|
| 1 | Discovery | `GET /.well-known/openid-configuration` | 200. Grants `client_credentials`, `authorization_code`, `refresh_token`; `code_challenge_methods_supported` `["S256"]`; scopes `openid`, `offline_access`; authorize, token and revocation endpoints |
| 2 | Anonymous authorize | `GET /connect/authorize` (code, S256, `openid offline_access`, state, nonce) with no cookie | 302 to `/Account/Login?ReturnUrl=` the authorize request plus `norse_reauth` |
| 3 | Login on the challenge | Password sign-in on the rendered Login page | Deferred sign-in, `GET /_auth/complete` 200, then the re-entered `GET /connect/authorize` 302 to `http://127.0.0.1:8765/cb?code=…&state=walk-state-1&iss=https://localhost:5001/` |
| 4 | Code redemption, then replay | `POST /connect/token` (`authorization_code`, `code_verifier`) twice | First 200, with tokens issued (body lost to a capture error in the walk script). Second 400 `{"error":"invalid_grant"}`: the authorization reads `revoked`, and its code, access, refresh and id tokens all read `revoked` |
| 5 | Signed-in authorize | `GET /connect/authorize` with the cookie | 302 to the redirect URI with `code` and `state` (no page) |
| 6 | Code + PKCE | `POST /connect/token` | 200: `access_token`, `id_token`, `refresh_token`, `expires_in` 600, `scope` `openid offline_access`. Access token: `aud` `Norse.User`, `sub` and `nameidentifier` the user's `Guid`, `auth_time`, no roles, no email, no `norse_stamp`. Id token: `aud` the `client_id`, `nonce`, `auth_time`, no `norse_stamp` |
| 7 | Refresh 1 (R0) | `POST /connect/token` (`refresh_token`) | 200, new refresh token R1, `auth_time` unchanged |
| 8 | Refresh 2 (R1) | same | 200, R2, `auth_time` unchanged. One authorization `valid`; code and R0, R1 `redeemed`, R2 `valid` |
| 9 | Replay R0 | `POST /connect/token` with R0 | 400 `{"error":"invalid_grant"}`. The authorization reads `revoked`, every token of it `revoked` |
| 10 | R2 after the replay | `POST /connect/token` with R2 | 400 `{"error":"invalid_grant"}`: the grant is ended |
| 11 | Self-revoke | New grant (code → S0 → S1), then `POST /connect/revoke` with S1 (`token_type_hint=refresh_token`) | 200, empty JSON body. S1 reads `revoked`, and the authorization stays `valid` |
| 12 | The revoked token again | `POST /connect/token` with S1 | 400 `{"error":"invalid_grant"}`. Reuse: the authorization reads `revoked`, every token `revoked` |
| 13 | Authorize refusals | `curl`: no `code_challenge`; `plain`; an unregistered redirect URI | 400 each, no redirect |
| 14 | `prompt=none` | `curl`, no cookie, `prompt=none` | 302 to the redirect URI with `error=login_required`, `state` and `iss`; no login page |
| 15 | User bearer at the REST façade | `GET /api/reference/countries/US` with the step 11 access token | 403 (walked before r6.3; now the bare 401, pinned in Yggdrasil's `CompositionLaneTests`) |
| 16 | Step-up | After the 90-second window, `/Account/Manage/Passkeys`, then "Add a new passkey" | Soft in-circuit navigation to `/Account/Login?returnUrl=%2FAccount%2FManage%2FPasskeys`, with no HTTP response for the hop |
| 17 | Re-proving | Password sign-in on that Login page | `GET /_auth/complete` 200, then `GET /Account/Manage/Passkeys` 200: back on Passkeys |
| 18 | The fresh operation | "Add a new passkey" again, with a Chromium virtual authenticator | Passed freshness, the ceremony completed, and the page moved to `/Account/Manage/RenamePasskey/{id}`. One `user_passkeys` row |

**The two parked observations:**
- **Login under the soft hop renders correctly** (the soft-navigation ruling's stated cost, Task 1.2). It rendered interactive on the Server circuit: named inputs, the "Use a passkey" link carrying the same `returnUrl`, and a working submit. No forced load is owed. Under a cached WASM payload it would not be interactive, but that is the environment limit above, not the hop.
- **The in-circuit `NextUrl` at the root host** is `/Account/Login?returnUrl=%2FAccount%2FManage%2FPasskeys`: correct at the root, with no `PathBase` in it. The Task 2.5 ruling stands. A circuit-side `PathBase` source is owed only for a non-root host.

**Cleanup:** the host was stopped, the container removed, and the throwaway key material deleted. Host-written `norse-dev-keys/` stays ignored by `Yggdrasil/src/.gitignore`. The full AppHost stand-up waits for Buvy's volume wipe after this train's squash.

### 13.5 Errata

§5.2's direct-sign-in bullet and §10.8's erasure expectation are corrected in place. Every other erratum that changes stated behavior carries a one-line *As built (§13)* note in its own section; this list is the full account.

- **§4.2, CLR-only typing.** Preview.4 types `Properties`, `DisplayNames`, `Descriptions` and `Settings` as dictionaries in the CLR only. The EF mapping still stores them as JSON *strings*, and only the `string[]` members became native `json`/`text[]` columns. So `HasMaxLength(-1)` stays on the `IDictionary` columns: the explicit-length entity law refuses an unbounded string without it. "Removed" holds only for the `string[]` columns.
- **H1's `resources` leg has no JSON column** on SQL Server. The real JSON-versioning coverage is `applications` and `scopes.resources`.
- **§5.1, relative endpoint URIs.** The endpoints are registered as `connect/authorize`, `connect/token` and `connect/revoke`, with no leading slash. With one, OpenIddict ignores `PathBase`, and nothing under a base path matches. A path-base round trip pins it.
- **§5.2, direct sign-in (T4-1).** Corrected in place: the stamp point reads only `ReauthLandingItemName`, which the direct-sign-in branch fills from `ReturnUrlItemName`.
- **§5.4, the transaction-read principal.** The exchange reads the validated grant principal from OpenIddict's server transaction: the principal `AuthenticateAsync(server scheme)` would return, and the one the redemption handler reads. It does not call `AuthenticateAsync`, so the IL ban that signers authenticate only the Identity cookie stays exact. Re-check the transaction-property key on every OpenIddict bump.
- **§5.6 and §10.14, prune order.** The order is **tokens → sessions → authorizations**, not tokens → authorizations → sessions. Sessions carry an authorization FK, nothing cascades, and authorization `PruneAsync` checks tokens, not sessions. Six of eight facts go red when the order is swapped.
- **§5.7, scope.**
  - The redirect rule holds for endpoint-originated errors only. OpenIddict's own refusals answer an HTTP 400 page with no redirect: a missing or `plain` challenge, an unknown client, a wrong redirect URI, and the `prompt=none` combination. That is conservative, since an unvalidated request is never redirected to, but a missing PKCE challenge reaches the client as a page rather than a redirected error. The walk confirms it (§13.4, step 13).
  - The endpoint itself refuses a repeated singular parameter, and any case-variant spelling of a parameter it reads (`norse_reauth` and every singular OIDC parameter), with `invalid_request`. RC3's query merge keeps the *later* spelling, so `max_age=0&MAX_AGE=999` was a freshness bypass until this refusal.
  - OpenIddict 8 does not refuse repeats: it reads a repeated singular parameter as absent. Review Focus 5's premise, and T4-10's "rejecting repeated parameters is OpenIddict's job", are wrong on that point.
  - The suppression of `error_description`/`error_uri` covers every OpenIddict server response type, discovery included (it leaked too), and the bearer challenge and forbid headers.
- **§6.1 and §10, the allowlist wording.** The cookie keeps ASP.NET Identity's `amr` (`pwd`/`mfa`) and `ClaimTypes.AuthenticationMethod`. Read the allowlist as "four persistent claims plus the framework's authentication-method markers, plus the transient `norse_reauth`". The markers carry no personal data, and Train 3's passkey-as-MFA fact rests on `amr=mfa`. The exact-set fact pins them by name.
- **§6.3, the link start.** It is not freshness "as endpoint metadata". `ExternalLoginEndpointRouteBuilderExtensions` declares the landing as metadata (`ReauthenticationLandingAttribute`) and adds an endpoint filter that evaluates `NorsePolicies.RecentAuthentication` after the group's `Account` policy has admitted the caller. It redirects to sign in again only when a failure reason's message is `NorseErrorCodes.ReauthenticationRequired`, and answers any other freshness failure with a bare forbid. `RequireAuthorization` would have answered a bare 403 instead of the hop.
- **`SignInManager.IsSignedIn` is banned** (an addition to §5.8's IL scan). It matches `Identity.Application` by authentication type, so it answers false on every lane-stamped principal. No caller existed. `IsSignedIn_is_banned_because_it_never_sees_a_lane_stamped_principal` pins the ban, and `No_sign_in_takes_its_principal_from_HttpContext_User` pins that no Himinbjörg sign-in builds its principal from `HttpContext.User`: credential and refresh paths take it from the factory or the ticket.
- **§7.1, bullet 1.** "No longer registers `Norse.Machine`" means it no longer registers `Norse.Machine` *as an OpenIddict forward*. Midgard still registers `Norse.Machine` and `Norse.User`, as its own `NorseBearerLaneHandler`.
- **§10.8, erasure.** Corrected in place: erasure leaves the authorization and its tokens absent, not `revoked`.
- **§10.12, the user bearer at the façade.** At the REST façade a user bearer is stamped `Machine`, because the selector routes façade controllers to `Norse.Machine`. As walked, it was refused by the `Norse.Facade` audience pin, not by `ReferencePolicies.Public`'s lane list as the fact's parenthetical says. Since r6.3 it fails earlier, at lane authentication, as a bare 401 (§7.3).
- **No revocation passthrough.** OpenIddict 8 offers none. Revocation is OpenIddict's own endpoint, and the grant boundary (§5.5) supplies every Norse behavior there, so no production revocation code exists. Re-check the suppression response-type list and the bearer handler's order on every OpenIddict bump.
- **Pre-existing exposures closed.**
  - Before Task 4.1's fix round, the façade *accepted* access tokens from the query string and the form body (200). Validation now reads the `Authorization` header only (OAuth 2.1 §5.1).
  - Phase 0's skeleton redemption minted tokens for a code or refresh token with no resolved token id. Task 4.6 refuses it: `invalid_grant` at the token endpoint, `invalid_token` at revocation.
- **The 0.3 observation.** The r6.1 note's predicted grant split was not observed on the stock stores; the guard refused first (§13.3).
- **The lane's audience, as built (r6.3).** An `aud` value outside the two platform audiences is ignored, not refused. Only the other platform audience, both, or none fail. Himinbjörg mints one audience per token today. Refusing any extra audience is a one-line tightening if a resource audience ever appears.
- **The pin string.** `8.0.0-preview.4` does not exist on the feed (NU1603). §2 ruling 2 and §8 mean `8.0.0-preview.4.26456.66`.

### 13.6 Carried forward from execution

- **Discovery's `claims_supported`** lists `aud`, `exp`, `iat`, `iss` and `sub`, not `auth_time`, though every token carries it. OpenIddict's default document; cosmetic until a third-party client reads it.
- **An unpruned session pinning a prunable authorization** fails the authorization prune step each run. It is unreachable while sessions are unused, the same as stock OpenIddict, and is collected as an upstream finding to file after delivery.
- **A grant missing `sub`, `auth_time` or `norse_stamp`** answers `invalid_grant` without ending the grant (per §5.4's wording). Only Norse mints these grants, so a malformed one is a bug signal; its siblings stay live until expiry.
- **The in-circuit `NextUrl` omits `PathBase`** (§13.4): owed for a non-root host only.
