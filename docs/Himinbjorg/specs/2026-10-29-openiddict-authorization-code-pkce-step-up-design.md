# Himinbjörg: OpenIddict Authorization Code + PKCE, Revocation and Step-Up — Design

**Realms touched:** Himinbjörg (OpenIddict 8, the six entities, the protocol surface, revocation, `auth_time`, the bearer source scheme, pruning), Midgard (the OpenIddict leak removed, the lane handlers and lane stamping, the default-closed lane requirement in the generated policy registration, the freshness requirement, gRPC bearer selection, the re-authentication failure mapping), Asgard (neutral names and policy helpers: lanes, audiences, the freshness policy, the re-authentication error code; the `Machine` and `Probe` policies), Heimdall (the `AccountRead`, `Self` and `MaskedDisclosure` policies), Yggdrasil (composition, the OpenIddict pin, policy registration), Glitnir (this verdict).
**Closes:** spec sub-project **B** of `2026-10-26-identity-account-lifecycle-over-grpc-design.md` §3, and the Train 3 hand-off T3-37 (step-up). Partially serves [Himinbjorg#50](https://github.com/NorseArchitecture/Himinbjorg/issues/50) (the authorization-code core; its MCP door is deferred, §11) and [#52](https://github.com/NorseArchitecture/Himinbjorg/issues/52) (the flow MAUI will ride; the MAUI client is Train 6).
**Depends on nothing open.** **Depended on by:** Train 5 (the full OpenIddict / ASP.NET Identity end-to-end integration, including T3-36's local external-login walk), Train 6 (MAUI), and the MCP door (§11).
**Amends:** Himinbjörg `CLAUDE.md` Architecture Facts — the principal allowlist (three claims become four persistent ones plus one transient: `auth_time`, and the `norse_reauth` binding, §6.1) and the temporal/non-temporal rulings (two new entities, §4.3); `2026-10-26-identity-account-lifecycle-over-grpc-design.md` §11 W24 (OpenIddict session revocation is answered by §5.5, not by OpenIddict sessions).
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

## 1. Context

On `master`, OpenIddict is `client_credentials` only (Himinbjorg#49): one token endpoint (`/connect/token`, `OpenIddictExchangeEndpoint`), one audience (`Norse.Facade`), local validation, and a `Norse.Machine` lane that Midgard registers as a policy-scheme forward to OpenIddict's validation scheme. No human can obtain a token. The Train 3 hand-off left three things owed to this train: step-up re-authentication (T3-37, by name for passkey add, two-factor disable, external-login link and the authenticator key display), the "other session dies" gap W24, and the sub-project B core itself.

OpenIddict 8 is in preview (`8.0.0-preview.4`, 2026-09-06; no stable). Its EF Core models were modernized in preview 3, and it adds `Session` and `Resource` entities. `InitialCreate` still squashes in place, so this is the cheapest point the platform will ever have to absorb a schema-moving upgrade.

**Outcome sought:** the platform is its own OIDC provider for native and machine clients. A first-party public client can run authorization code + PKCE against an Identity account and refresh with rotation and reuse detection. Erasure, a security-stamp rotation or a lockout ends its grant, including any code still unredeemed. A user bearer token reaches exactly two read-only gRPC operations. The four sensitive account operations demand a recent authentication on either lane. OpenIddict is invisible outside Himinbjörg. All of it is proven at the protocol level, with no new UI.

## 2. Rulings (brainstorm 2026-10-28/29; 10–11 from review r2)

1. **Scope is the core only.** Authorization code + PKCE, refresh rotation and reuse detection, user-bearer validation, step-up and the erasure kill, all with pre-registered first-party clients. The MCP door from #50 (dynamic client registration, RFC 8414 / RFC 9728 metadata, an MCP resource server and audience) is its own later train.
2. **OpenIddict 8, now.** Code against `8.0.0-preview.4`. Himinbjörg floats `8.*-*` as it floats `7.*` today, and Yggdrasil's `Directory.Packages.props` holds the pin, the way everything else is pinned. Breaking previews are accepted until RTM.
3. **Revocation is erasure, stamp rotation, lockout and self-revoke** (§5.5). Signing out of a browser session does not end a native client's tokens. OpenIddict 8 sessions are carried in the schema and left unused: no `LoginId` claim.
4. **Step-up is a lane-neutral freshness requirement plus OIDC compliance** (§6). The four operations require a recent `auth_time` on whichever principal arrives. A stale caller is answered with a server-authored re-authentication `NextUrl`. The authorize endpoint honors `prompt` and `max_age` and emits `auth_time`.
5. **User bearer tokens reach a read-only slice** (§7.5): self-disclosure and `GetAccountStatus`, and nothing else. Every mutation stays cookie-only until the MAUI train decides how a token caller rotates a stamp.
6. **The realm split follows existing law** (approach A). Himinbjörg owns the server, Midgard the lanes and the freshness requirement, Asgard and Heimdall the names and policies, Yggdrasil the composition. There is one OpenIddict validation scheme accepting both audiences, and each policy pins its audience (§7.4).
7. **OpenIddict never leaves Himinbjörg.** No package reference, type, scheme constant or behavioral workaround for OpenIddict exists in any other realm. The leak that already exists in Midgard is fixed in this train (§7.1).
8. **`auth_time` is an explicit claim** stamped only by credential presentation (§6.1). The cookie's `IssuedUtc` is not authentication time.
9. **No Heimdall UI change.** `Login.razor` does not redirect a signed-in user, so the re-authentication hop lands on the existing login page as is (§6.4).
10. **Lanes are explicit in the principal and closed by default in every policy** (r2, amended r3). Midgard stamps the lane that authenticated a request onto the principal. The generated policy registration adds an `Identity`-lane requirement to every policy that names no lane, so a new policy, in any realm, admits the cookie identity and nothing else until it opts a lane in by name. No policy infers a lane from roles or claims a token could carry, and no policy re-authenticates another scheme to find out.
11. **A grant carries its authentication; the token endpoint never re-derives it** (r2). The authorization code and every refresh token carry the `auth_time` and the security stamp of the authentication that produced them. Redemption checks that stamp against the user's current one and carries both facts forward unchanged. Only authorization facts (does the user exist, are they locked out) are re-read.
12. **Any reuse of a code or refresh token ends the grant, durably** (r3, amended r4). Redemption is a guarded `valid → redeemed` transition that also requires a valid authorization. Every grant-ending event revokes the authorization itself, so no successor created around it can ever be redeemed (§5.5). Reuse means any second presentation, including a serialized client's retry after a lost response. That ends the grant, and the client signs in again.

## 3. Scope

| In | Out (where it goes) |
|---|---|
| `authorization_code` + PKCE (S256 only), `refresh_token`, `client_credentials` kept | `password` (ROPC), implicit, hybrid, device code: never |
| `/connect/authorize`, `/connect/token`, `/connect/revoke` (RFC 7009) | userinfo, end-session, introspection: not this train (§11) |
| First-party public clients, pre-registered, implicit consent | Dynamic client registration, consent UI: the MCP door (§11) |
| `openid`, `offline_access` | A scope vocabulary, and roles in user tokens: the authz-lockdown arc |
| User bearer on self-disclosure and `GetAccountStatus` | Bearer on any mutation: Train 6 |
| Revocation by erasure, stamp, lockout, self-revoke of the presented token | "Revoke all my grants" (an authorization-wide revoke) and its UI: Train 5 |
| Pruning of expired OpenIddict rows | — |

## 4. Schema (Himinbjörg `src/Identity.EntityFramework`)

### 4.1 Six entities

`NorseOpenIddictSession` and `NorseOpenIddictResource` join the existing four. In OpenIddict 8, `OpenIddictEntityFrameworkCoreApplication`, `…Authorization` and `…Token` each take a `TSession` type parameter, and `ReplaceDefaultEntities<…>` in `AddNorseOpenIddictCore()` names all six. `NorseOpenIddictSession` declares explicit `ApplicationId` and `AuthorizationId` FK scalars for the same reason the authorization and token entities already do: the platform's navigation/FK law forbids shadow FKs.

### 4.2 Typed columns

OpenIddict 8 types what were JSON strings: `Properties` becomes `IDictionary<string, JsonElement>`, `Permissions`/`RedirectUris`/`Requirements`/`Scopes`/`Resources` become `string[]`, `DisplayNames`/`Descriptions`/`Settings` become `IDictionary<string, string>`, and the application gains a typed `JsonWebKeySet`. The `HasMaxLength(-1)` lines on `Properties`, `Scopes` and `Payload` were string-column configuration and are removed or replaced by whatever each property's mapping needs (`Payload` stays a string). The EF stores now use JSON operators. PostgreSQL and SQL Server 2025 both support them, and the dual-provider container suites prove it.

**Spike first (§10, task 1):** SQL Server engine-native system versioning over JSON-typed columns on `applications`, `scopes` and `resources`. This is the one interaction that cannot be settled from source. If it fails, the plan halts and the finding returns to the controller before anything is built on it.

### 4.3 Temporal rulings, amended

- `NorseOpenIddictResource` → `resources`, **temporal**. It is part of the durable authorization record, like `applications` and `scopes`. The temporal set grows from seven to eight.
- `NorseOpenIddictSession` → `sessions`, **non-temporal**. It is bulk-pruned runtime state, like `authorizations` and `tokens`.
- `NorseIdentityTemporalModelTests` (including its closing fact that no other entity carries the stamp), `NorseIdentityTemporalApparatusContainerTests` and `SqlServerIdentitySchemaTests` are amended in the same change. Tables are named for the entity, never `openiddict_*`.

### 4.4 Squash

`InitialCreate` is squashed in place on both providers, and the embedded `schema/norse_identity.sql` regenerates. Buvy wipes the local `norse-pg-primary`/`norse-pg-replica` volumes before the next Bifröst stand-up.

## 5. Protocol surface (Himinbjörg `src/Identity.Web.Server`)

### 5.1 Server configuration

`AddNorseIdentity`'s `AddServer` gains `AllowAuthorizationCodeFlow()`, `AllowRefreshTokenFlow()`, `RequireProofKeyForCodeExchange()`, S256 as the only code-challenge method, `SetAuthorizationEndpointUris("/connect/authorize")` and `SetRevocationEndpointUris("/connect/revoke")`, with authorization-endpoint passthrough. `client_credentials` stays. Registered scopes: `openid`, `offline_access`. OpenIddict 8's `ValidateOnStart()` is called on the core, server and validation builders.

**Lifetimes** (options, overridable as `accessTokenLifetime` is today): authorization code 5 minutes, access token 10 minutes, identity token 10 minutes, refresh token 14 days, rolling, with zero reuse leeway.

### 5.2 The authorize endpoint

A passthrough handler (`OpenIddictAuthorizeEndpoint`, beside `OpenIddictExchangeEndpoint`) requires a registered client with an exact-match redirect URI, then decides in this order.

**1. Read the authentication.** Authenticate the Identity cookie. The anonymous principal counts as no principal. A principal whose `auth_time` is missing, unparseable as integral epoch seconds, or in the future beyond the clock-skew allowance counts as *stale*. It is never treated as fresh, and never as absent.

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
    - **Direct sign-ins** (a password with no second factor, a passkey assertion): the landing parked through `ParkedReturnUrl` (T3-2) is still on `ReturnUrlItemName` when `SignInOrTwoFactorAsync` takes the base path to `SignInWithClaimsAsync`, so the stamp point reads it from there.
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
2. **Check the user.** Reload the user through `userManager.Outcome` and answer `invalid_grant` when the user is missing or erased, is locked out, or has a current stamp that differs from the grant's. Every such rejection, on a code or a refresh token, also runs `EndGrant` on the grant's authorization (§5.5).
3. **Mint from the grant.** Build the new principal from the grant's `sub`, `auth_time` and `norse_stamp`, never from a fresh read. On success the stamp equals the current one anyway, and `auth_time` is carried unchanged through every refresh, never advanced by one.

So a code issued before a password reset cannot be redeemed after it. A refresh at day 13 still carries the original sign-in's `auth_time`.

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
- **Stamp, lockout, erasure.** The token endpoint's check (§5.4) rejects the grant and runs `EndGrant`. Erasure goes further: `ErasureService.ShredAsync`'s one transaction already deletes the subject's tokens and authorizations, and it now also deletes the subject's sessions, or relies on the FK cascade if one reaches them, whichever the schema produces. After commit no code or refresh token can be redeemed. Outstanding access tokens live out their remaining lifetime of up to 10 minutes.
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

### 5.7 Protocol errors

The authorize, token and revocation endpoints answer **standard OAuth 2.0 / OIDC error responses** with the optional `error_description` and `error_uri` suppressed, and no `Outcome` envelope:

- The token endpoint answers a JSON body with the required `error` member and the RFC 6749 §5.2 status, `invalid_client` included where applicable.
- The revocation endpoint answers a request error (a malformed request, client authentication failure, an unsupported token type) the same way. An unknown, expired, already-revoked or already-redeemed token is **not** an error: per RFC 7009 §2.2 it answers HTTP 200, indistinguishable from a successful revocation. OpenIddict's `NormalizeErrorResponse` already does this, and §10 pins it.
- The authorize endpoint answers a redirect to the client's redirect URI carrying `error` and `state` once the client and redirect URI are validated, and an error page response with no redirect before then (an unknown client or a mismatched redirect URI is never redirected to).

The resource edge's bare-401 discipline (#49, #50) applies to the gRPC lanes, not to these endpoints.

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

### 6.4 The re-authentication hop

`Login.razor` does not redirect a signed-in user, so the existing password and passkey sign-ins re-prove the credential, re-issue the cookie with a new `auth_time`, and land back on the return URL. No Heimdall UI change. §10 pins it, so a later "already signed in" redirect on the login page breaks a test rather than silently breaking step-up.

## 7. Lanes and policies

### 7.1 The Midgard leak, removed

Midgard today references `OpenIddict.Validation.AspNetCore`, forwards `NorseSchemes.Machine` to `OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme` in `AuthenticationBuilderExtensions`, and carries an authenticate-before-challenge workaround in `NorseGrpcHandler` that exists only because of OpenIddict's challenge handler (decompiled 7.6.0, #49). After this train:

- The package reference and the `using` are gone. `AddNorseAuthentication()` no longer registers `Norse.Machine`.
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
- On the client side (WASM), Midgard's `Infrastructure.Web.Client` registers a handler that treats the client's principal as the `Identity` lane. The browser only ever holds the cookie identity, and the client-side mirror of the policies must keep evaluating as it does today.

### 7.4 Audiences

Asgard declares the user audience `Norse.User` beside the existing `Norse.Facade`. The scheme and the audience share a string but are separate constants with separate checks. Validation accepts both audiences. Because one validation scheme now accepts both, every bearer-admitting policy pins its audience. The pin matters concretely: a machine principal carries a `Guid` `NameIdentifier` (the application's id).

### 7.5 The policy table

**Closed by default.** The generated `AddNorsePolicies()` (Midgard's policy-registration generator, the one that already discovers every `[NorsePolicy]` declaration) appends `RequireLane(Identity)` to every declared policy whose configure method adds no lane requirement of its own: no `RequireLane`, no `AllowAnyLane`.

- A generator diagnostic is not needed, because the default is the enforcement: a new policy in any realm admits the cookie identity and nothing else until it names another lane.
- `AllowAnyLane()` is the explicit, greppable opt-out. Its two users are `Probe`, which has no principal, and `RecentAuthentication`, which always stacks with the operation's own policy and so must never narrow the lane on its own.
- This closes, without touching their realms, every existing lane-blind policy: Heimdall's `Public`, Asgard's `Anonymous` and Mímir's `ReferencePolicies.Public`, every one of them only `RequireAuthenticatedUser()` today.
- `Public` matters concretely: `AuthenticationService` exposes eleven operations under it, among them `ForgotPassword`, which can send an email.

| Policy (owner) | Today | After this train |
|---|---|---|
| `NorsePolicies.Machine` (Asgard) | `RequireAuthenticatedUser()` | `RequireLane(Machine)` + `RequireAudience(Norse.Facade)` |
| `NorsePolicies.Probe` (Asgard) | always succeeds | `AllowAnyLane()`, otherwise unchanged |
| `NorsePolicies.Anonymous` (Asgard) | `RequireAuthenticatedUser()`: **lane-blind** | default-closed to `Identity`; declaration unchanged |
| `AuthNPolicies.Public` (Heimdall) | `RequireAuthenticatedUser()`: **lane-blind; eleven gRPC operations a bearer would reach** | default-closed to `Identity`; declaration unchanged |
| `ReferencePolicies.Public` (Mímir) | `RequireAuthenticatedUser()`: **lane-blind** | default-closed to `Identity`; declaration unchanged, Mímir untouched |
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
5. **Yggdrasil:** §8.

Bragi and Mímir are untouched; Mímir's `Public` is closed by Midgard's generated default at Yggdrasil's composition root, and pinned there by a Yggdrasil fact. Himinbjörg's coverage floor stays at 85. The end-to-end protocol walk runs against a disposable Postgres; the full AppHost stand-up follows the volume wipe.

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
   - Each of stamp rotation, lockout and erasure between code issuance and redemption makes the redemption fail with `invalid_grant`.
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
   - Stamp rotation, lockout and erasure each make the next code redemption or refresh fail with `invalid_grant`. The authorization reads `revoked`, and every one of its tokens too.
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
    - A Yggdrasil fact pins Mímir's `ReferencePolicies.Public` refusing a user bearer.
    - A real machine token reaches a façade controller (positive, through `Norse.Machine`'s stamping), and the same token is refused by `Self`, `AccountRead`, `Account` and `Public`.
    - A probe still reaches its endpoint with no principal.
    - WASM-side policy evaluation is unchanged for the cookie principal.
    - A host with no `Norse.Bearer` registration fails at startup.
    - A user bearer reaches `GetMyPersonalData` and `GetAccountStatus`.
    - A user bearer for a `System`-role user is refused by `MaskedDisclosure`.
    - A user token is refused by `Machine`.
    - The lane is the identity's authentication type, and no claim in a token can change it.
13. **The leak.** The Midgard reference scan; the #49 machine-lane facts green after the move.
14. **Pruning.**
    - Expired tokens, then authorizations, then sessions are pruned past `MinimumAge`, and live rows are kept.
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
