# OpenIddict Authorization Code + PKCE, Revocation and Step-Up — Train 4 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Pairs with superpowers:test-driven-development on every coding task, per standing law (Glitnir CLAUDE.md §2.8). **Every implementer and reviewer subagent runs on model `opus`.**

**Goal:** put OpenIddict 8's authorization code + PKCE flow, refresh rotation with durable reuse detection, RFC 7009 revocation and lane-neutral step-up on the platform (spec sub-project B), with OpenIddict confined to Himinbjörg and every policy closed to the cookie lane by default.

**Architecture:**
- **Asgard** names things: lanes, the user audience, the `auth_time` claim type, the lane/audience/freshness requirement records and helpers, `RecentAuthentication`, the `reauthentication-required` code, the reauthentication landing attribute, and a `Problem` that can carry a code and a next hop. Its two component bases navigate to a server-authored hop on failure.
- **Midgard** enforces them: the OpenIddict leak removed, `Norse.Bearer` declared, `NorseBearerLaneHandler` for `Norse.Machine` and `Norse.User`, lane stamping, gRPC bearer selection, the lane and freshness handlers, the default-closed lane in the generated `AddNorsePolicies()`, and the freshness failure mapping.
- **Heimdall** pins `Self`/`MaskedDisclosure` to their lanes and adds `AccountRead`.
- **Himinbjörg** carries everything OpenIddict: v8 and six entities, the authorize endpoint and the `norse_reauth` marker, grant-carried authentication, `RedeemTokenEntryGuarded`, `EndGrantOnReuse`, `EndGrant`, revocation, `auth_time`, `Norse.Bearer`, the pruner, and freshness on the four operations.
- **Yggdrasil** composes it and holds the `8.0.0-preview.4` pin. Bifröst's AppHost, Bragi and Mímir are untouched.
  - *r4 / as built: Mímir joined the train (spec amendment r6.2, Task 5.0).*

**Tech Stack:** .NET 11 RC3, C# 15; OpenIddict `8.0.0-preview.4` (Core, EntityFrameworkCore, Server.AspNetCore, Validation.AspNetCore, Validation.ServerIntegration); ASP.NET Core Identity 11 RC3; EF Core 11 RC2 (Npgsql + SqlClient); protobuf-net.Grpc code-first; Testcontainers (`postgres:19rc1`, `mcr.microsoft.com/mssql/server:2025-latest`); xUnit v3 on MTP, Shouldly, NSubstitute.

**Spec:** `../specs/2026-10-29-openiddict-authorization-code-pkce-step-up-design.md` (r6, approved for planning behind its halting gates). § references are to it unless marked. Rulings 1–12 are binding and are not restated.

**Starting state (verified 2026-10-29, every realm on clean `master`):** Asgard v0.0.37, Midgard v0.0.39, Heimdall v0.0.24, Himinbjörg v0.0.28, Yggdrasil v0.0.11. Bragi and Mímir untouched.

**Revision history:** r4, 2026-10-30.
- **r4:** execution halted at Task 5.1: a real machine token was refused by Mímir's REST façade, because `ReferencePolicies.Public` (on both `CountriesController` and `CountryQuery`) is default-closed to `Identity` while the façade requires `Machine`. Buvy brought Mímir into the train (spec amendment r6.2). Task 5.0 is added; Task 5.1 gains the façade and gRPC boundary facts; release order gains Mímir before Yggdrasil.
- **r3:** execution halted at H3/H4 (Task 0.2). The cause: OpenIddict's EF token store `FindAsync` returns a detached entity under the NoTracking default, so the stock relationship getters return null. Buvy chose the store link contract (spec §4.5, amendment r6.1). Task 0.3 and gate H7 are added; H2–H4 re-run unchanged inside Task 0.3. Task 4.6 gains the production-endpoint continuity fact. The guarded redemption stays.
- **r1:** the first draft.
- **r2:** answers the plan review.
  - P1: Task 0.1 upgrades the whole OpenIddict package set, and the premise fixture asserts the resolved versions.
  - P1: `AnyLaneRequirement` succeeds for an empty principal.
  - P2: T4-3 is order-independent: every operation policy is evaluated before freshness.
  - P2: T4-5 navigates only through a shared, tested local-navigation validator, and only on `Forbidden` + `reauthentication-required`.
  - P2: the canonical hash frames each parameter group with its value count.
  - P2: the full code-flow exchange assertion moves to Task 4.5.
  - Erasure asserts absence, not `revoked`.
  - T4-2 gains its preservation and throwing-refresh tests.

## Global Constraints

- Glitnir `docs/house-rules.md` is binding on every line of code and every test. Read it before the first task.
- OpenIddict: Himinbjörg floats `8.*-*`; Yggdrasil `Directory.Packages.props` pins `8.0.0-preview.4`. No other realm references any `OpenIddict.*` package, type, scheme constant or behavior (ruling 7).
- Lifetimes: authorization code 5 min; access token 10 min; identity token 10 min; refresh token 14 days, rolling; zero reuse leeway.
- PKCE: S256 only. Scopes registered: `openid`, `offline_access`. Grants: `authorization_code`, `refresh_token`, `client_credentials`.
- Endpoints: `/connect/authorize` (passthrough), `/connect/token` (passthrough), `/connect/revoke` (OpenIddict-handled).
- Audiences: `Norse.Facade` (machine), `Norse.User` (user access tokens). The id token's audience is the `client_id`.
- Claims: `sub`, `ClaimTypes.NameIdentifier`, `auth_time` (integral epoch seconds), `norse_stamp` (code and refresh token only), never roles, never email or name.
- Marker: `norse_reauth`, data-protected under its own purpose, 5 minutes, burned in `consumed_passkey_challenges`.
- Freshness window: `Norse:Authentication:RecentWindow`, default 10 minutes, validated on start.
- Pruning: `Identity:OpenIddict:Pruning` with `Enabled`, `Interval`, `MinimumAge` (default 14 days), validated on start.
- Telemetry: log event `GrantEndUnconfirmed` (authorization id only); counter `norse.identity.grant_end_unconfirmed` on the existing `Norse.Identity` meter.
- Error code: `reauthentication-required`. Protocol errors carry no `error_description` and no `error_uri`.
- Himinbjörg coverage floor stays 85.
- `InitialCreate` is squashed in place on both providers; Buvy wipes `norse-pg-primary`/`norse-pg-replica` before the next stand-up.
- Process: one feature fork per realm, named `feature/openiddict-v8`, created with `--no-track`. Stage, never commit: every "Commit" step below means `git add` the listed paths and stop for Buvy. No Bifröst branch.

## Review Focus

1. **Non-root `PathBase`.** The marker's landing check ("an authorize request under the live `PathBase`"), the challenge's `returnUrl` and the freshness `NextUrl` must all hold under `PathBase = /app`. A reasonable person expects step-up to work wherever the host is mounted. Test: Task 4.4 `Authorize_reauth_round_trip_under_a_path_base`, Task 2.5 `Next_url_carries_the_path_base`.
2. **A POSTed authorize request.** OIDC permits `POST /connect/authorize`; the challenge cannot return to a POST. Expected: the re-entry `returnUrl` is the GET form of the same parameters, and the canonical transaction hash is identical for both. Test: Task 4.4 `A_posted_authorize_request_re_enters_as_a_get_and_is_issued_a_code`.
3. **A re-entry URL longer than `ReturnUrls.MaxLength` (2048).** `ReturnUrls.Resolve` would drop it to the app root, and the user would sign in and never return: a silent loop. Expected: the authorize endpoint refuses such a request with `invalid_request` to the redirect URI before challenging (ruling T4-6). Test: Task 4.4 `An_authorize_request_too_long_to_re_enter_is_refused_before_the_challenge`.
4. **The freshness boundary and clock skew.** Expected: an `auth_time` exactly `RecentWindow` old is fresh, one second older is stale, and an `auth_time` more than the 5-minute skew allowance in the future is stale (Midgard's own constant; OpenIddict's never crosses the realm line). Test: Task 2.5 `Age_equal_to_the_window_is_fresh`, `One_second_past_the_window_is_stale`, `A_future_auth_time_is_stale`.
5. **Repeated parameters in an authorize request.** Expected: a request with a repeated singular parameter (`state`, `nonce`, `code_challenge`) is refused by OpenIddict before any marker exists. The canonical hash never collapses two distinct parameter sets to one. Every parameter group is framed by its name and value count, and unknown multi-valued parameters take part under the same framing (T4-10). Test: Task 4.3 `Canonical_hash_is_injective_over_parameter_boundaries` and `Canonical_hash_distinguishes_parameter_groups`; Task 4.4 `A_repeated_state_is_refused_before_a_marker_is_minted`.

---

## Plan rulings (T4-n)

- **T4-1 — One landing accessor (resolves the r6 wording note).** `NorseSignInManager` exposes one slot, `ReauthLandingItemName` (`"Norse.Identity.ReauthLanding"`). The stamp point (`SignInWithClaimsAsync` override) reads only that slot. It is filled in exactly two places, each clearing it in a `finally`: `SignInOrTwoFactorAsync`, on its direct-sign-in branch, copies the parked landing from `ReturnUrlItemName` into the slot before calling the base sign-in; `CompleteTwoFactorSignInAsync` resolves `challenge.ReturnUrl` through `ReturnUrls.Resolve` and sets it before `SignInWithClaimsAsync`. §5.2's direct-sign-in bullet is amended accordingly in Task 5.2.
- **T4-2 — `auth_time` is stamped by the stamp point unless a refresh is in progress.** The base `RefreshSignInAsync` itself calls `SignInWithClaimsAsync`, so `NorseSignInManager` keeps a private refresh-depth counter. Its `RefreshSignInAsync` override increments the counter around the base call and decrements it in a `finally`, so a throwing refresh leaves it at 0.
  - At depth 0 the stamp point adds `auth_time = now`.
  - At depth > 0 it copies the current principal's `auth_time`, if there is one, and never adds `norse_reauth`.
  - Any other `SignInWithClaimsAsync` is a credential presentation. That includes registration's sign-in, because a password or a provider assertion was just presented.
- **T4-3 — Freshness rides the command only, and is evaluated last.** A freshness-gated command carries two `[Authorize]` attributes: its operation policy and `NorsePolicies.RecentAuthentication`, in either order. The gRPC service method mirrors only the operation policy, because endpoint-edge authorization would answer a bare 403 before the mediator can map the reason.
  - `PolicyCache<TRequest>` partitions the attributes by name, never by order, into `OperationPolicies` (every `[Authorize(Policy)]` other than `RecentAuthentication`, at least one) and `RequiresRecentAuthentication`.
  - `AuthorizationBehavior` evaluates every operation policy. Any failure answers bare, as today.
  - Only when all of them succeed does it evaluate freshness. The reauthentication hop is therefore returned only to a caller who would otherwise be authorized.
- **T4-10 — The canonical transaction is structurally framed.** `AuthorizeTransaction.Hash` writes:
  - the parameter count;
  - then, for each name in ordinal order: a name tag, the length-prefixed UTF-8 name, the value count, and each value as a value tag plus a length-prefixed UTF-8 value, in received order.

  Integers are fixed-width little-endian `int32`. Unknown and multi-valued parameters take part under the same framing, so no two distinct parameter sets serialize alike. Rejecting repeated parameters is OpenIddict's job, upstream of the hash.
  *As built (spec §13.5): OpenIddict 8 reads a repeated singular parameter as absent. The authorize endpoint refuses repeated and case-variant parameters itself.*
- **T4-4 — The landing is declared on the command.** Asgard `[ReauthenticationLanding("/Account/Manage/Passkeys")]` (server-only, `Abstractions.Web.Server.Mediator`). A command naming `RecentAuthentication` without it makes `PolicyCache` throw `InvalidOperationException` on first use, the existing law for a missing `[Authorize]`; Himinbjörg pins every command at build time with a reflection fact (Task 4.9). Landings: `GetPasskeyCreationOptions`/`AddPasskey` → `/Account/Manage/Passkeys`; `DisableTwoFactor` → `/Account/Manage/Disable2fa`; `GetAuthenticatorSetup` → `/Account/Manage/EnableAuthenticator`; `ResetAuthenticator` → `/Account/Manage/ResetAuthenticator`.
- **T4-5 — The carrier, and navigation only through one validator.** `Problem` gains `string? Code` and `string? NextUrl`. `Forbidden`'s transport disposition already permits a body. The gRPC trailer carries the two members as `ErrorInfo.Metadata` entries `code` and `nextUrl`, beside `receipt`, and Midgard's client decoder rehydrates them.
  - Asgard's `OutcomeComponentBase` and `OutcomeFormComponentBase` navigate (a soft `NavigateTo(NextUrl)`, never a forced load: NORSE074 reserves forced loads for Heimdall's `ForceLoadSessionTransition`, and reaching the login page is not a session transition — the sign-in there makes its own; r3 execution ruling) only when the failure is `Forbidden`, `Code == reauthentication-required`, and `LocalNavigation.IsLocal(NextUrl)` holds. Every other failed `Problem` renders as today, a `NextUrl` included.
  - `LocalNavigation.IsLocal` is Asgard's shared validator (`Abstractions.Components`, WASM-safe). A value is local only when it passes every check:
    - it is non-empty and at most 2048 characters;
    - it contains no C0 control, DEL or space anywhere (WHATWG strips tab and newline, so `"/\t/evil.example"` would otherwise resolve off-site);
    - it contains no backslash anywhere;
    - it starts with `/` and its second character is not `/`;
    - all of the above still hold after one percent-decode.

    Midgard's `/_auth/complete` check is the precedent. Adopting this validator there is a later curation item, not this train's.
  - No Heimdall component changes (ruling 9 holds).
- **T4-6 — Too long to re-enter is refused.** If the re-entry URL (`PathBase` + `/connect/authorize?` + parameters + `norse_reauth`) would exceed `ReturnUrls.MaxLength`, the authorize endpoint answers `invalid_request` to the redirect URI instead of challenging (Review Focus 3).
- **T4-7 — A POST re-enters as a GET.** The challenge's `returnUrl` is always the GET form; the canonical hash is computed over the parameter set, never the method (Review Focus 2).
- **T4-8 — The authorize principal's stamp is the cookie's.** The code's `norse_stamp` is the cookie principal's security-stamp claim (`ClaimsIdentityOptions.SecurityStampClaimType`), the stamp of the authentication that produced the grant (ruling 11). A cookie whose stamp is already stale yields a code that fails at redemption.
- **T4-9 — Development order is not release order.** Phase 0 runs first on the Himinbjörg fork against `master` upstream. Asgard and Midgard forks are developed next with `UseProjectReferences=true` and Himinbjörg consumes them locally; publishing gates only each realm's PR (memory: publish gates PRs). Release order is Asgard → Midgard → Heimdall → Himinbjörg → Mímir → Yggdrasil (Mímir added in r4; it depends on Asgard and Midgard only, so it may ship any time after Midgard and before Yggdrasil).

## Halting gates

The plan halts and returns to the controller (no improvising) when any of these fails:

- **H1** (Task 0.1): SQL Server system versioning over the JSON-typed columns of `applications`, `scopes`, `resources`.
- **H2** (Task 0.2): `RemoveEventHandler(RedeemTokenEntry.Descriptor)` removes the built-in handler and the guarded handler runs at its order.
- **H3** (Task 0.2): `EndGrantOnReuse` runs before `ValidateTokenEntry` at both endpoints, only under its filters, and sees a redeemed code at `/connect/revoke` before `ValidateTokenType`.
- **H4** (Task 0.2): the guarded statement moves exactly the specified rows on both providers, and OpenIddict's own validation rejects nothing the statement would accept.
- **H5** (Task 4.3): every one of the five credential paths, immediate and deferred, reaches the stamp point with its landing.
- **H6** (Task 4.4): OpenIddict 8 passes the unknown `norse_reauth` authorize parameter through to the passthrough handler without rejecting it.
- **H7** (Task 0.3, r3): the store link contract's five facts (spec §4.5) pass on both providers, and H2–H4 then pass unchanged. Nothing past Phase 0 starts before H7.

---

## Phase 0 — Himinbjörg premise gates (fork `feature/openiddict-v8`)

### Task 0.1: OpenIddict 8, six entities, the squash and the JSON-temporal spike (H1)

**Files:**
- Modify: `Himinbjorg/src/Identity.EntityFramework/Identity.EntityFramework.csproj` (`OpenIddict.EntityFrameworkCore` → `8.*-*`) and `Himinbjorg/src/Identity.Web.Server/Identity.Web.Server.csproj:17-19` (`OpenIddict.Server.AspNetCore`, `OpenIddict.Validation.AspNetCore`, `OpenIddict.Validation.ServerIntegration` → `8.*-*`). The whole set moves in this task, before any gate runs. `NorseOpenIddictApplication.cs`, `NorseOpenIddictAuthorization.cs`, `NorseOpenIddictToken.cs`, `NorseOpenIddictScope.cs`, `ServiceCollectionExtensions.cs` (`ReplaceDefaultEntities<…>` names all six), `NorseIdentityDbContext.cs`
- Create: `Himinbjorg/src/Identity.EntityFramework/NorseOpenIddictSession.cs`, `NorseOpenIddictResource.cs`
- Regenerate: both providers' `Migrations/` and `schema/norse_identity.sql`
- Test: `Himinbjorg/tests/Identity.EntityFramework.Tests/NorseIdentityTemporalModelTests.cs`, `tests/Identity.Migrations.Tests/NorseIdentityTemporalApparatusContainerTests.cs`, `SqlServerIdentitySchemaTests.cs`

**Interfaces:**
- Produces: `sealed record NorseOpenIddictSession : OpenIddictEntityFrameworkCoreSession<Guid, NorseOpenIddictApplication, NorseOpenIddictAuthorization, NorseOpenIddictToken>` with explicit `Guid? ApplicationId`, `Guid? AuthorizationId`; `NorseOpenIddictResource : …Resource<Guid>, ITemporalEntity`. Tables `sessions`, `resources`. (Match whatever generic arity preview.4's base types declare; the explicit FK scalars are the law.)

- [ ] **Step 1: Write the spike fact first.** `SqlServerIdentitySchemaTests.Json_typed_columns_survive_system_versioning`: on the SQL Server fixture, insert an application with two redirect URIs and a property `{"k": 1}`, update its permissions, then read `TemporalAll()` and assert two versions whose `RedirectUris` and `Properties` round-trip exactly. Same for a scope and a resource.
- [ ] **Step 2: Run** `dotnet test tests/Identity.Web.Server.Tests -- --filter-class "*.SqlServerIdentitySchemaTests"`. Expected: compile failure (no v8 types).
- [ ] **Step 3: Bump to `8.*-*`, add the two entities, re-type columns per §4.2** (drop the `HasMaxLength(-1)` lines on `Properties`/`Scopes`; `Payload` stays a string with its length), squash `InitialCreate` on both providers.
- [ ] **Step 4: Run the spike.** Expected: PASS. **If it fails, halt (H1)** and report the SQL Server error verbatim.
- [ ] **Step 5: Amend the temporal facts.** `NorseIdentityTemporalModelTests`: `resources` temporal, `sessions` not; the closing "no other entity" fact lists eight temporal tables. Catalog facts on both providers: `sessions` exists without a history table, `resources` with one.
- [ ] **Step 6: Run** `dotnet test Himinbjorg.slnx`. Expected: PASS, with `OpenIddictTokenEndpointTests` (machine flow) still green on v8.
- [ ] **Step 7: Stage.**

### Task 0.2: The pipeline premises (H2–H4)

**Files:**
- Create: `Himinbjorg/src/Identity.Web.Server/OpenIddict/RedeemTokenEntryGuarded.cs`, `OpenIddict/EndGrantOnReuse.cs` (skeleton: filters and order only; `EndGrant` arrives in Task 4.5, here it calls an injected `IGrantTermination` the test substitutes), `OpenIddict/GrantStatements.cs` (the two provider statements, through `Store/ProviderStatements.cs`)
- Modify: `Store/ProviderStatements.cs`
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/OpenIddict/PipelinePremiseSuite.cs` (+ `PostgresPipelinePremiseTests`, `SqlServerPipelinePremiseTests`), `OpenIddict/OpenIddictTestHost.cs` (shared TestServer builder: code + refresh flows, test-registered public client `test-native` with redirect `http://127.0.0.1/cb`, controllable `TimeProvider`, named pause points)

**Interfaces:**
- Produces:
  - `sealed class RedeemTokenEntryGuarded : IOpenIddictServerHandler<ProcessSignInContext>`, `static OpenIddictServerHandlerDescriptor Descriptor` at `RedeemTokenEntry.Descriptor.Order`.
  - `sealed class EndGrantOnReuse : IOpenIddictServerHandler<ValidateTokenContext>`, order `ValidateTokenEntry.Descriptor.Order - 1`.
  - `interface IGrantTermination { Task<GrantEnd> EndAsync(Guid authorizationId, CancellationToken cancellationToken); }`, `enum GrantEnd { None = 0, Confirmed = 1, Unconfirmed = 2 }`.
  - `GrantStatements.RedeemAsync(DbContext, Guid token, Guid authorization, DateTimeOffset now, CancellationToken) -> Task<int>` (the §5.5 statement verbatim, both providers); `GrantStatements.RevokeAuthorizationAsync(DbContext, Guid authorization, CancellationToken) -> Task<int>` (`UPDATE authorizations SET status = 'revoked', concurrency_token = @fresh WHERE id = @authorization AND status = 'valid'`).
  - `OpenIddictTestHost.PausePoint` names: `BeforeRedemption`, `AfterRedemption` (before `GenerateRefreshToken`), `BeforeValidateTokenEntry`.

- [ ] **Step 1: Write the premise facts** (spec §10.7 "Premises first"):
  - `Resolved_openiddict_is_one_v8_version` (runs first; every other premise depends on it). Every loaded `OpenIddict.*` assembly (`Abstractions`, `Core`, `EntityFrameworkCore`, `Server`, `Server.AspNetCore`, `Validation`, `Validation.AspNetCore`, `Validation.ServerIntegration`) reports the same `AssemblyInformationalVersionAttribute`, and it starts with `8.0.0-preview.4`, Yggdrasil's pin. The fact writes the version to test output. A float that resolves past preview.4 fails it on purpose: moving the pin is a deliberate act.
  - `Built_in_redeem_token_entry_is_absent`: the resolved `OpenIddictServerOptions.Handlers` contains no descriptor whose `ServiceDescriptor.ServiceType` is `RedeemTokenEntry`, and contains `RedeemTokenEntryGuarded.Descriptor` at that order.
  - `End_grant_on_reuse_runs_before_validate_token_entry_at_both_endpoints`: a recording handler pair shows `EndGrantOnReuse` invoked before `ValidateTokenEntry` for a refresh at `/connect/token` and for the same token at `/connect/revoke`.
  - `A_redeemed_code_at_revoke_reaches_end_grant_on_reuse`: the substitute `IGrantTermination` receives the code's authorization id, and the response is 200.
  - `Filters_exclude_access_tokens_and_id_token_hints`: the substitute is never called for those.
  - `Guarded_statement_row_counts`: one row for valid/valid; zero for `redeemed`, `revoked`, and valid-under-`revoked`-authorization. (This is also §10.7's SQL backstop, direct.)
  - `OpenIddict_validation_accepts_what_the_statement_accepts`: a valid refresh token under a valid authorization reaches `RedeemTokenEntryGuarded`.
- [ ] **Step 2: Run** `dotnet test tests/Identity.Web.Server.Tests -- --filter-class "*PipelinePremiseTests"`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement the two handler skeletons and `GrantStatements`**, registered in `AddNorseIdentity`'s `AddServer` (`RemoveEventHandler(RedeemTokenEntry.Descriptor)`, `AddEventHandler(...)` for both). The test host enables the code and refresh flows locally; production configuration arrives in Task 4.4.
- [ ] **Step 4: Run.** Expected: PASS on both providers. **Any failure halts (H2–H4)**; report which premise and the observed handler order.
- [ ] **Step 5: Stage.**

### Task 0.3: The store link contract (H7, r3)

**Files:**
- Create in `Himinbjorg/src/Identity.EntityFramework/`: `NorseOpenIddictTokenStore.cs`, `NorseOpenIddictAuthorizationStore.cs` and `NorseOpenIddictSessionStore.cs`. Each derives from OpenIddict's EF Core store for its entity, closed over the six Norse entities, `NorseIdentityDbContext` and `Guid`.
- Modify: `Himinbjorg/src/Identity.EntityFramework/ServiceCollectionExtensions.cs`. `AddNorseOpenIddictCore()` registers the three stores through `ReplaceTokenStore`, `ReplaceAuthorizationStore` and `ReplaceSessionStore`, or preview.4's equivalents.
- Test: create `Himinbjorg/tests/Identity.Web.Server.Tests/OpenIddict/StoreLinkContractSuite.cs` (+ `PostgresStoreLinkContractTests`, `SqlServerStoreLinkContractTests`) and `OpenIddict/GrantContinuitySuite.cs` (+ per provider). Re-run `PipelinePremiseSuite` (Task 0.2) unchanged.

**Interfaces:**
- Consumes: Task 0.2's `OpenIddictTestHost`, `GrantStatements` and `IGrantTermination`.
- Produces: the three stores. The links each one owns are listed in spec §4.5's table.
  - The getters (`GetApplicationIdAsync`, `GetAuthorizationIdAsync`, `GetSessionIdAsync`, each where its store owns the link) return the explicit scalar's `ToString()`, or null when the scalar is null.
  - The setters (`SetApplicationIdAsync`, `SetAuthorizationIdAsync`, `SetSessionIdAsync`) set the scalar and the navigation together: set, replace and clear. For the navigation they use a stub or attached reference, never a claim-derived value.
  - No store ever creates an authorization.

- [ ] **Step 1: Write the facts** (spec §4.5, items 1–5; both providers):
  - Store contract:
    - `Detached_entities_return_persisted_links`: each store, each link, on an entity read in a fresh scope with navigations unloaded.
    - `Null_links_return_null`.
    - `Getters_issue_no_query`: a `DbCommandInterceptor` counts zero commands across the getter calls.
  - Write round trip:
    - `Set_replace_and_clear_round_trip`: a theory over store × link × {set, replace, clear} × {tracked, detached}. It asserts the getter immediately after the setter, then again after `UpdateAsync` and a fresh-scope reload.
    - `Managers_create_with_links`: create a token, an authorization and a session through `IOpenIddictTokenManager`, `IOpenIddictAuthorizationManager` and `IOpenIddictSessionManager` with descriptors carrying every link, then reload in a fresh scope and assert each link.
  - Grant continuity:
    - `Refresh_chain_keeps_one_authorization`: through `OpenIddictTestHost` (PF2), run code → R0 → R1 → R2. Query the `tokens` and `authorizations` rows directly: exactly one authorization exists for the grant, and every token of the chain carries its non-null id.
  - Security consequence:
    - `Replaying_r0_after_two_rotations_ends_the_grant`. The test host's `IGrantTermination` runs `GrantStatements.RevokeAuthorizationAsync` (step 1 only; the full `EndGrant` arrives in Task 4.5). The authorization reads `revoked`, R2's refresh is refused, and an independent grant (a second code for the same user and client) still refreshes.
  - Registration:
    - `Production_di_resolves_the_norse_stores`: build the production service collection, resolve `IOpenIddictTokenStoreResolver` and its siblings in two separate scopes, and assert each yields the Norse type with OpenIddict's default entity-cache options.
- [ ] **Step 2: Run** `dotnet test tests/Identity.Web.Server.Tests -- --filter-class "*StoreLinkContractTests" --filter-class "*GrantContinuityTests"`. Expected: FAIL. `Refresh_chain_keeps_one_authorization` should fail on the stock stores, either by splitting the grant (record the observed authorization count) or by the guarded handler refusing. Record which, because it confirms or refutes the spec's prediction.
- [ ] **Step 3: Implement** the three stores and their registration.
- [ ] **Step 4: Run** the two suites, then `PipelinePremiseSuite` unchanged. Expected: all PASS on both providers. **Any failure halts (H7).**
- [ ] **Step 5: Run** `dotnet test Himinbjorg.slnx`. Expected: PASS. Stage.

---

## Phase 1 — Asgard (fork `feature/openiddict-v8`)

### Task 1.1: Lanes, audiences, requirements, helpers and the platform policies

**Files:**
- Create: `Asgard/src/Abstractions.Components/Authorization/NorseLanes.cs`, `NorseAudiences.cs`, `NorseClaimTypes.cs`, `LaneRequirement.cs`, `AnyLaneRequirement.cs`, `AudienceRequirement.cs`, `RecentAuthenticationRequirement.cs`, `NorseLanePolicyExtensions.cs`
- Modify: `NorsePlatformPolicies.cs`, `NorsePolicies.cs`
- Test: `Asgard/tests/Abstractions.Components.Tests/Authorization/NorsePolicyDeclarationTests.cs`, `NorseLanePolicyExtensionsTests.cs`

**Interfaces:**
- Produces:
  - `static class NorseLanes { const string Identity = "Norse.Lane.Identity"; User = "Norse.Lane.User"; Machine = "Norse.Lane.Machine"; }`
  - `static class NorseAudiences { const string Facade = "Norse.Facade"; User = "Norse.User"; }` (`Facade` replaces any existing literal).
  - `static class NorseClaimTypes { const string AuthTime = "auth_time"; }`
  - `sealed record LaneRequirement(IReadOnlyList<string> Lanes) : IAuthorizationRequirement`; `sealed record AnyLaneRequirement : IAuthorizationRequirement` (imposes no lane restriction: its handlers succeed it for every principal, an empty one included, because a probe authenticates nothing); `sealed record AudienceRequirement(string Audience, string? Lane) : IAuthorizationRequirement` (null `Lane` = applies to every lane); `sealed record RecentAuthenticationRequirement : IAuthorizationRequirement`.
  - `AuthorizationPolicyBuilder` extension block: `RequireLane(params string[] lanes)`, `AllowAnyLane()`, `RequireAudience(string audience, string? lane = null)`, `CloseLaneByDefault()` (adds `LaneRequirement([Identity])` iff `Requirements` holds no `LaneRequirement` and no `AnyLaneRequirement`).
  - `NorsePolicies.RecentAuthentication` (name `"Norse.RecentAuthentication"`) declared `[NorsePolicy]` as `AllowAnyLane()` + `RecentAuthenticationRequirement`.
  - `NorsePlatformPolicies.Machine` → `RequireLane(Machine)` + `RequireAudience(Facade)`; `Probe` → `AllowAnyLane()` + its existing assertion.

- [ ] **Step 1: Write the tests.** `Close_lane_by_default_adds_identity_when_none_declared`; `Close_lane_by_default_leaves_require_lane_alone`; `Close_lane_by_default_leaves_allow_any_lane_alone`; `Machine_requires_machine_lane_and_facade_audience` (inspect `policy.Requirements`); `Probe_allows_any_lane`; `Recent_authentication_allows_any_lane_and_requires_freshness`.
- [ ] **Step 2: Run** `dotnet test tests/Abstractions.Components.Tests`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement** the types above. Records only, no server types (this assembly compiles to WASM).
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Stage.**

### Task 1.2: The `Problem` carrier, the landing attribute and the component bases

**Files:**
- Modify: `Asgard/src/Abstractions.Contracts/Problem.cs`, `Asgard/src/Abstractions.Components/OutcomeComponentBase.cs`, `OutcomeFormComponentBase.cs`
- Create: `Asgard/src/Abstractions.Contracts/NorseErrorCodes.cs`, `Asgard/src/Abstractions.Web.Server/Mediator/ReauthenticationLandingAttribute.cs`, `Asgard/src/Abstractions.Components/LocalNavigation.cs`
- Test: `Asgard/tests/Abstractions.Components.Tests/LocalNavigationTests.cs`, `OutcomeComponentBaseTests.cs`, `OutcomeFormComponentBaseTests.cs`

**Interfaces:**
- Produces: `Problem.Code` (`string?`), `Problem.NextUrl` (`string?`); `static class NorseErrorCodes { const string ReauthenticationRequired = "reauthentication-required"; }`; `[AttributeUsage(AttributeTargets.Class)] sealed class ReauthenticationLandingAttribute(string path) : Attribute { string Path { get; } }`; `static class LocalNavigation { static bool IsLocal([NotNullWhen(true)] string? url); }` (T4-5's rules).

- [ ] **Step 1: Write the tests.**
  - `LocalNavigationTests`:
    - accepts `/Account/Login?returnUrl=%2FAccount%2FManage%2FPasskeys` and `/app/x`;
    - refuses each of `""`, `"x"`, `"//evil.example"`, `"/\\evil.example"`, `"/\t/evil.example"`, `"/\n/evil.example"`, `"/\r/evil.example"`, `" /x"`, `"/%2F/evil.example"`, `"/%5Cevil.example"`, `"/%09/evil.example"`, `"https://evil.example/"`, and a 2049-character path.
  - For each base, through bUnit with a fake `NavigationManager`:
    - `A_reauthentication_failure_with_a_local_next_url_navigates_to_it`: the manager records `/Account/Login?returnUrl=%2FAccount%2FManage%2FPasskeys` with `forceLoad: false`, and no `Problem` is rendered.
    - `A_reauthentication_failure_with_a_non_local_next_url_renders_the_problem`: theory over `"//evil.example"`, `"/\t/evil.example"` and `"/\n/evil.example"`. Nothing is navigated.
    - `A_next_url_on_any_other_failure_never_navigates`: `Forbidden` with no code, `Unauthorized` with the code, and `Fault`, each carrying a local `NextUrl`.
    - `A_failure_without_next_url_renders_the_problem_as_today`.
- [ ] **Step 2: Run** `dotnet test tests/Abstractions.Components.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement** T4-5. The bases inject `NavigationManager`, and the navigation decision is the single predicate `Category is Forbidden && Code == NorseErrorCodes.ReauthenticationRequired && LocalNavigation.IsLocal(NextUrl)`.
- [ ] **Step 4: Run** `dotnet test Asgard.slnx`. Expected: PASS.
- [ ] **Step 5: Stage.** Asgard ship gate: PR, CI green, tag, publish.

---

## Phase 2 — Midgard (fork `feature/openiddict-v8`)

### Task 2.1: The leak removed, `Norse.Bearer`, `NorseBearerLaneHandler`

**Files:**
- Modify: `Midgard/src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj` (remove `OpenIddict.Validation.AspNetCore`), `Authentication/NorseSchemes.cs`, `Authentication/AuthenticationBuilderExtensions.cs`
- Create: `Authentication/NorseBearerLaneHandler.cs`, `Authentication/BearerSchemeValidation.cs`
- Move/Modify: the #49 machine-lane facts in `tests/Infrastructure.Web.Server.Tests/Authentication/LaneWireupTests.cs` (a test-registered `Norse.Bearer` stub scheme replaces OpenIddict)
- Test: `tests/Infrastructure.Web.Server.Tests/Authentication/NorseBearerLaneHandlerTests.cs`, `tests/Infrastructure.Web.Server.Tests/OpenIddictReferenceScanTests.cs`

**Interfaces:**
- Produces: `NorseSchemes.Bearer = "Norse.Bearer"`, `NorseSchemes.User = "Norse.User"` (`Machine` kept); `sealed class NorseBearerLaneHandler(...) : AuthenticationHandler<NorseBearerLaneOptions>` with `NorseBearerLaneOptions.Lane` (`string`, required); `BearerSchemeValidation : IValidateOptions<AuthenticationOptions>` registered with `ValidateOnStart`, failing when `Norse.Bearer` is not in the scheme map.

- [ ] **Step 1: Write the tests.** `No_midgard_assembly_references_openiddict` (scan every `Norse.Infrastructure.*` assembly's `GetReferencedAssemblies()` for names starting `OpenIddict.`); `Machine_lane_stamps_machine` and `User_lane_stamps_user` (stub `Norse.Bearer` returns a principal; result identity's `AuthenticationType` is the lane, claims preserved); `Challenge_and_forbid_delegate_to_norse_bearer`; `A_host_without_norse_bearer_fails_at_startup` (`OptionsValidationException` on host start).
- [ ] **Step 2: Run** `dotnet test tests/Infrastructure.Web.Server.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement.** `AddNorseAuthentication()` registers `Norse.Machine` and `Norse.User` as `NorseBearerLaneHandler` with their lanes; no `using OpenIddict`.
- [ ] **Step 4: Run.** Expected: PASS, the moved #49 facts included.
- [ ] **Step 5: Stage.**

### Task 2.2: Lane stamping and gRPC bearer selection

**Files:**
- Modify: `Authentication/NorseBrowserHandler.cs`, `Authentication/NorseGrpcHandler.cs` (remove the challenge workaround, lines 65–78)
- Test: `tests/.../Authentication/NorseBrowserHandlerTests.cs`, `NorseGrpcHandlerTests.cs`

**Interfaces:**
- Consumes: `NorseLanes` (1.1), `NorseSchemes.User` (2.1).

- [ ] **Step 1: Write the tests.** `Identity_cookie_is_stamped_identity`; `Anonymous_cookie_is_stamped_identity_and_keeps_its_role`; `A_bearer_header_selects_the_user_lane_and_never_reads_the_cookie` (cookie present, bearer present → `AuthenticationType == NorseLanes.User`, cookie handler not invoked); `No_bearer_header_runs_the_cookie_path_as_today`; `A_claim_cannot_set_the_lane` (a token claim named like a lane leaves `AuthenticationType` unchanged).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** Stamp by rebuilding `new ClaimsIdentity(identity.Claims, lane, identity.NameClaimType, identity.RoleClaimType)`; bearer detection is an `Authorization` header whose scheme is `Bearer` (ordinal-ignore-case).
- [ ] **Step 4: Run** `dotnet test Midgard.slnx`. Expected: PASS.
- [ ] **Step 5: Stage.**

### Task 2.3: Lane and audience requirement handlers, server and client

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/Authorization/LaneRequirementHandler.cs`, `AudienceRequirementHandler.cs`; `Midgard/src/Infrastructure.Web.Client/Authorization/ClientLaneRequirementHandler.cs`
- Modify: the server and client authorization registrations (`AddNorseAuthentication()` server side; the client's `AddNorseGrpcClients()` companion registration)
- Test: `tests/Infrastructure.Web.Server.Tests/Authorization/LaneRequirementHandlerTests.cs`, `tests/Infrastructure.Web.Client.Tests/ClientLaneRequirementHandlerTests.cs`

- [ ] **Step 1: Write the tests.** Server: `Succeeds_when_authentication_type_is_listed`, `Fails_for_an_unlisted_lane`, `Any_lane_succeeds_for_any_stamped_identity`, `Any_lane_succeeds_for_an_empty_principal` (a `new ClaimsPrincipal()` with no identity), `Probe_policy_succeeds_for_an_empty_principal` (the full `NorsePlatformPolicies.Probe` through `IAuthorizationService`), `Audience_matches_any_aud_claim`, `Lane_scoped_audience_ignores_other_lanes`. Client: `Client_principal_is_the_identity_lane` (a WASM principal with any `AuthenticationType` satisfies `RequireLane(Identity)` and fails `RequireLane(Machine)`), `Client_any_lane_succeeds_for_an_empty_principal`. The real probe endpoint stays a Yggdrasil fact (Task 5.1).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** as `AuthorizationHandler<T>` singletons.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Stage.**

### Task 2.4: Default-closed lanes in the generated `AddNorsePolicies()`

**Files:**
- Modify: `Midgard/gen/Infrastructure.Web.Server.Generator/Policies/PolicyRegistrationEmitter.cs` (shared by the client generator by source link)
- Test: `tests/Infrastructure.Web.Server.Generator.Tests/Policies/PolicyRegistrationEmitterTests.cs` (snapshot), `tests/Infrastructure.Web.Server.Tests/Authorization/GeneratedPolicyLaneTests.cs`, `tests/Infrastructure.Web.Client.Tests/GeneratedPolicyLaneTests.cs`

- [ ] **Step 1: Write the tests.** Emitter snapshot: each entry is `builder.AddPolicy("<name>", static policy => { global::<T>.<M>(policy); policy.CloseLaneByDefault(); });`, emitted with `AppendCSharp` and a raw string. Behavioral (server and client): `A_policy_declaring_no_lane_admits_only_identity`, `Require_lane_declarations_are_untouched`, `Allow_any_lane_declarations_are_untouched`.
- [ ] **Step 2: Run** the generator and both behavioral test projects. Expected: FAIL.
- [ ] **Step 3: Implement** the emitter change.
- [ ] **Step 4: Run** `dotnet test Midgard.slnx`. Expected: PASS.
- [ ] **Step 5: Stage.**

### Task 2.5: Freshness: the requirement, the policy list, the failure mapping, the trailer

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Server/Authorization/RecentAuthenticationHandler.cs`, `RecentAuthenticationOptions.cs`, `ReauthenticationRequiredReason.cs`
- Modify: `Mediator/PolicyCache.cs` (T4-3, T4-4), `Mediator/AuthorizationBehavior.cs`, `Mediator/Grpc/ProblemExtensions.cs`, `Midgard/src/Infrastructure.Web.Client/Grpc/RpcExceptionExtensions.cs`
- Test: `tests/.../Authorization/RecentAuthenticationHandlerTests.cs`, `tests/.../Mediator/AuthorizationBehaviorTests.cs`, `PolicyCacheTests.cs`, `tests/.../Mediator/Grpc/ProblemExtensionsTests.cs`, `tests/Infrastructure.Web.Client.Tests/RpcExceptionExtensionsTests.cs`

**Interfaces:**
- Produces: `RecentAuthenticationOptions { TimeSpan RecentWindow = 10 min }` bound to `Norse:Authentication`, validated on start (positive, ≤ 1 day); `sealed class ReauthenticationRequiredReason(IAuthorizationHandler handler) : AuthorizationFailureReason(handler, NorseErrorCodes.ReauthenticationRequired)`; `PolicyCache<TRequest>.OperationPolicies : IReadOnlyList<string>`, `PolicyCache<TRequest>.RequiresRecentAuthentication : bool`, `PolicyCache<TRequest>.ReauthenticationLanding : string?` (T4-3).

- [ ] **Step 1: Write the tests.**
  - Handler, on a `FakeTimeProvider`: `Age_equal_to_the_window_is_fresh`, `One_second_past_the_window_is_stale`, `A_future_auth_time_is_stale` (more than `RecentAuthenticationHandler.Skew`, a 5-minute constant of Midgard's own, ahead of now; one within it is fresh), `A_missing_auth_time_is_stale`, `A_non_integral_auth_time_is_stale`, each failing with exactly one `ReauthenticationRequiredReason`; `It_reads_the_claim_on_any_lane`.
  - `PolicyCache`:
    - `Partition_is_by_name_not_order`: two fixture commands with the attributes in opposite orders produce equal `OperationPolicies` and `RequiresRecentAuthentication`.
    - `Recent_authentication_without_a_landing_throws`.
    - `Recent_authentication_alone_throws`: no operation policy.
  - Behavior:
    - `A_stale_caller_gets_forbidden_with_the_code_and_next_url`: `Code == "reauthentication-required"`, `NextUrl == "/Account/Login?returnUrl=%2FAccount%2FManage%2FPasskeys"`.
    - `Next_url_carries_the_path_base`: `/app/Account/Login?returnUrl=%2Fapp%2FAccount%2FManage%2FPasskeys`.
    - `Any_other_failure_stays_bare`: no `Code`, no `NextUrl`.
    - `An_operation_failure_on_a_stale_caller_stays_bare_in_either_attribute_order`: a theory over both orders. A stale anonymous caller fails `Account`, and the answer is bare and never the hop.
    - `Multiple_failing_operation_policies_stay_bare`: two operation policies both fail on a stale caller.
    - `Freshness_is_not_evaluated_when_an_operation_policy_fails`: a counting freshness handler records zero calls.
  - Trailer: `Code_and_next_url_round_trip_through_the_trailer` (server encode → client decode equal); `A_problem_without_them_writes_no_metadata`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** The behavior reads `PathBase` from `IHttpContextAccessor` and builds the URL with `QueryString.Create("returnUrl", pathBase + landing)`.
- [ ] **Step 4: Run** `dotnet test Midgard.slnx`. Expected: PASS.
- [ ] **Step 5: Stage.** Midgard ship gate.

---

## Phase 3 — Heimdall (fork `feature/openiddict-v8`)

### Task 3.1: `Self`, `MaskedDisclosure`, `AccountRead`

**Files:**
- Modify: `Heimdall/src/AuthN.Services/IdentityPolicies.cs`, `AuthNPolicies.cs`
- Test: `Heimdall/tests/AuthN.Services.Tests/PolicyContributorTests.cs`

**Interfaces:**
- Produces: `AuthNPolicies.AccountRead` (`"AuthN.AccountRead"`): `RequireLane(Identity, User)`, not anonymous, `RequireAudience(NorseAudiences.User, NorseLanes.User)`. `IdentityPolicies.Self`: the same shape. `IdentityPolicies.MaskedDisclosure`: `RequireLane(Identity)` + `RequireRole("System")`.

- [ ] **Step 1: Write the tests** (evaluate through `IAuthorizationService` with the Midgard handlers): `Account_read_admits_a_cookie_identity`, `Account_read_admits_a_user_bearer_with_the_user_audience`, `Account_read_refuses_a_user_bearer_with_the_facade_audience`, `Account_read_refuses_the_anonymous_identity`, `Masked_disclosure_refuses_a_system_role_on_the_user_lane`.
- [ ] **Step 2: Run** `dotnet test tests/AuthN.Services.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `dotnet test Heimdall.slnx`. Expected: PASS.
- [ ] **Step 5: Stage.** Heimdall ship gate.

---

## Phase 4 — Himinbjörg (continuing the Phase 0 fork)

### Task 4.1: `Norse.Bearer` and validation

**Files:**
- Modify: `Himinbjorg/src/Identity.Web.Server/IdentityBuilderExtensions.cs` (`AddValidation`: `AddAudiences(NorseAudiences.Facade, NorseAudiences.User)`; `ValidateOnStart()` on core, server, validation), `ServiceCollectionExtensions.cs` (register `Norse.Bearer` as `AddPolicyScheme` forwarding to `OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme`)
- Create, only if Step 4 shows it is still needed: `OpenIddict/BearerChallengeHandler.cs` (the authenticate-before-challenge behavior moved from Midgard)
- Test: `tests/Identity.Web.Server.Tests/OpenIddict/BearerSchemeTests.cs`

- [ ] **Step 1: Write the tests.** `Norse_bearer_is_registered`; `A_machine_token_authenticates_through_norse_machine` (positive #49 fact, now via Midgard's handler forwarding to `Norse.Bearer`); `An_expired_token_challenges_with_a_bare_401`; `A_missing_token_challenges_with_a_bare_401`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** the forward.
- [ ] **Step 4: Run.** If the bare-401 facts fail on preview.4, add `BearerChallengeHandler` reproducing the #49 behavior inside Himinbjörg, and record the finding in the task report. Expected: PASS.
- [ ] **Step 5: Stage.**

### Task 4.2: `auth_time` on the cookie and the allowlist

**Files:**
- Modify: `NorseSignInManager.cs` (stamp point, T4-2 refresh depth), `ServiceCollectionExtensions.cs` (`SecurityStampValidatorOptions.OnRefreshingPrincipal` copies `auth_time` from `CurrentPrincipal` to `NewPrincipal`), `Himinbjorg/CLAUDE.md` (allowlist fact: four persistent claims plus transient `norse_reauth`)
- Test: `tests/Identity.Web.Server.Tests/NorseSignInManagerTests.cs`, `AuthTimeSuite.cs` (+ per provider)

- [ ] **Step 1: Write the tests.** `Each_credential_path_stamps_auth_time` (theory over password, authenticator, recovery code, passkey, external login; `auth_time == FakeTimeProvider.GetUtcNow().ToUnixTimeSeconds()`); `Refresh_sign_in_carries_auth_time_and_never_stamps` (advance clock, refresh, value unchanged); `Refresh_sign_in_never_adds_norse_reauth` (a refresh whose current landing carries a valid marker); `A_throwing_refresh_resets_the_depth` (the refresh's base call throws, then a password sign-in on the same scoped manager stamps a fresh `auth_time`); `Nested_refresh_preserves_auth_time` (depth 2 then back to 0); `On_refreshing_principal_carries_auth_time`; `The_cookie_principal_carries_exactly_the_allowlisted_claims`; `A_cookie_without_auth_time_stays_without_it_across_refresh`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Stage.**

### Task 4.3: The landing slot, the two-factor plumbing and the `norse_reauth` stamp (H5)

**Files:**
- Create: `OpenIddict/ReauthMarker.cs`, `OpenIddict/ReauthMarkers.cs`, `OpenIddict/AuthorizeTransaction.cs`
- Modify: `NorseSignInManager.cs` (T4-1: `ReauthLandingItemName`, `SignInOrTwoFactorAsync` direct branch, `CompleteTwoFactorSignInAsync` at line 585, stamp point)
- Test: `tests/Identity.Web.Server.Tests/ReauthBindingSuite.cs` (+ per provider), `OpenIddict/AuthorizeTransactionTests.cs`, `OpenIddict/ReauthMarkersTests.cs`

**Interfaces:**
- Produces:
  - `sealed record ReauthMarker(Guid MarkerId, Guid? UserId, byte[] TransactionHash, DateTimeOffset ExpiresAt)`.
  - `sealed class ReauthMarkers(IDataProtectionProvider, TimeProvider)`: `string Protect(ReauthMarker)`, `bool TryUnprotect(string protectedMarker, [NotNullWhen(true)] out ReauthMarker? marker)` (false when expired), purpose `"Norse.Identity.ReauthMarker.v1"`, `static TimeSpan Lifetime = 5 min`.
  - `static class AuthorizeTransaction { byte[] Hash(IEnumerable<KeyValuePair<string, StringValues>> parameters); }`: excludes `norse_reauth`, then SHA-256 over T4-10's framing.
  - `NorseSignInManager.ReauthLandingItemName`, `const string ReauthClaimType = "norse_reauth"`.
  - `static bool ReauthMarkers.TryReadFromLanding(string landing, PathString pathBase, out ReauthMarker marker)` (landing path equals `pathBase + "/connect/authorize"`, `norse_reauth` present and unprotects).

- [ ] **Step 1: Write the tests** (spec §10.4 "Binding, positive" and "two-factor plumbing", §10.10 `norse_reauth` items):
  - `Each_credential_path_stamps_the_marker_id`: theory over the five paths × {immediate, deferred through `IDeferredSignIn` and `/_auth/complete`}; each signs in with landing `/connect/authorize?…&norse_reauth=<M>` and the resulting principal's `norse_reauth == M.MarkerId`.
  - `Two_factor_landing_is_on_the_slot_during_sign_in_and_gone_after` (authenticator, recovery code, external login; success and a thrown `SignInWithClaimsAsync`); `Complete_two_factor_returns_the_landing_unchanged`.
  - `An_expired_marker_stamps_nothing`, `An_invalid_marker_stamps_nothing`, `A_marker_for_another_user_stamps_nothing`, `A_landing_outside_the_path_base_stamps_nothing`.
  - `Refresh_drops_norse_reauth`.
  - `Canonical_hash_is_order_independent_by_name_and_excludes_the_marker`; `Canonical_hash_is_injective_over_parameter_boundaries` (`a=bc` vs `ab=c`; `x=["1","2"]` vs `x=["12"]`); `Canonical_hash_distinguishes_parameter_groups` (`{ a: ["b","c"], d: ["e"] }` vs `{ a: ["b"], c: ["d","e"] }`, the review's counterexample); `Canonical_hash_frames_unknown_multi_valued_parameters` (`{ x: ["1","2"] }` vs `{ x: ["2","1"] }` differ, `{ x: [""] }` vs `{}` differ).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** T4-1 and the stamp point (stamps only at refresh depth 0).
- [ ] **Step 4: Run.** Expected: PASS. **Any path that does not reach the stamp point with its landing halts (H5).**
- [ ] **Step 5: Stage.**

### Task 4.4: Server configuration and the authorize endpoint (H6)

**Files:**
- Modify: `IdentityBuilderExtensions.cs` (§5.1 verbatim: flows, PKCE S256, endpoints, scopes, lifetimes, authorization passthrough), `OpenIddictEndpointRouteBuilderExtensions.cs` (map GET and POST `/connect/authorize`)
- Create: `OpenIddict/OpenIddictAuthorizeEndpoint.cs`, `OpenIddict/UserPrincipalFactory.cs` (§5.3 claims and destinations, shared with Task 4.5)
- Modify: `Himinbjorg/src/Identity.Outcomes/` — new `OpenIddictAuthorizationManagerOutcomeView.cs` (create ad-hoc), extend `OutcomeViewExtensions.cs` and `IdentityOutcomeMap.cs`
- Test: `tests/Identity.Web.Server.Tests/OpenIddict/AuthorizeEndpointSuite.cs` (+ per provider)

**Interfaces:**
- Consumes: `ReauthMarkers`, `AuthorizeTransaction` (4.3); `IPasskeyChallengeLedger` (existing; the marker key is SHA-256 of `"norse_reauth:" + MarkerId.ToString("N")`).
- Produces: `UserPrincipalFactory.Create(Guid userId, long authTime, string stamp, IEnumerable<string> scopes) -> ClaimsPrincipal` (sets `sub`, `NameIdentifier`, `auth_time`, `norse_stamp`; destinations per §5.3; audience `Norse.User`).

- [ ] **Step 1: Write the tests** — spec §10.3, §10.4 (everything not already in 4.3), §10.9's authorize items, and Review Focus 1–3, 5. Notable assertions:
  - `Premise_the_marker_parameter_reaches_the_passthrough` (H6, first).
  - `A_fresh_cookie_is_issued_a_code`: the redirect to `http://127.0.0.1/cb` carries `code` and `state`. The token manager holds the code under a new ad-hoc authorization (type `ad-hoc`, status `valid`), and a second authorize request creates a second authorization. The exchange of that code is Task 4.5's, through the production endpoint.
  - Refusals: no `code_challenge`; `plain`; mismatched redirect (no `Location`); unknown client (no `Location`).
  - `prompt_none_answers_login_required_with_state` (anonymous and stale); `prompt_none_with_login_is_invalid_request`; `Malformed_max_age_is_invalid_request`; `Max_age_zero_with_a_delay_re_enters_once` (advance 30 s before re-entry; one challenge total).
  - Marker conditions: each changed `code_challenge`, `state`, `nonce`, `scope`, another client, another redirect, expired, another user → re-challenge; `A_burned_marker_re_challenges`; `Two_concurrent_re_entries_issue_at_most_one_code`; `Same_second_successive_reauthentications_each_succeed` (frozen clock); `Two_pre_existing_cookies` and `Anonymous_challenge_with_a_pre_existing_cookie` (§10.4 verbatim); `Re_entry_with_the_challenged_cookie_re_challenges`; `A_cookie_bound_to_M_does_not_satisfy_M_prime`.
  - `A_missing_or_malformed_auth_time_is_stale`.
  - `Non_implicit_consent_is_consent_required`.
  - Review Focus: `Authorize_reauth_round_trip_under_a_path_base`; `A_posted_authorize_request_re_enters_as_a_get_and_is_issued_a_code`; `An_authorize_request_too_long_to_re_enter_is_refused_before_the_challenge`; `A_repeated_state_is_refused_before_a_marker_is_minted`.
  - `Login_page_reproves_a_signed_in_user_and_lands_back` (§6.4, rendered through Heimdall's `Login`).
- [ ] **Step 2: Run.** Expected: FAIL; then `Premise_the_marker_parameter_reaches_the_passthrough` alone after Step 3's skeleton. **If OpenIddict rejects `norse_reauth`, halt (H6).**
- [ ] **Step 3: Implement** §5.2 steps 1–5 in that order, with T4-6, T4-7, T4-8.
- [ ] **Step 4: Run.** Expected: PASS on both providers.
- [ ] **Step 5: Stage.**

### Task 4.5: The token endpoint, grant-carried authentication and `EndGrant`

**Files:**
- Modify: `OpenIddictExchangeEndpoint.cs` (grant switch; invariant message "only the three enabled grants")
- Create: `OpenIddict/GrantTermination.cs` (implements `IGrantTermination` from 0.2)
- Create: `Identity.Outcomes/OpenIddictTokenManagerOutcomeView.cs` (`RevokeByAuthorizationIdAsync`)
- Test: `tests/Identity.Web.Server.Tests/OpenIddict/TokenEndpointSuite.cs`, `GrantTerminationSuite.cs` (+ per provider)

**Interfaces:**
- Consumes: `GrantStatements.RevokeAuthorizationAsync` (0.2), `UserPrincipalFactory` (4.4).
- Produces: `sealed class GrantTermination(...) : IGrantTermination` — step 1 up to 3 attempts, any exception counts as a failed attempt, zero rows is success; step 2 only after a confirmed step 1; logs `GrantEndUnconfirmed` (Error) or the step-2 Warning through `LoggerMessage` delegates; increments `norse.identity.grant_end_unconfirmed`.

- [ ] **Step 1: Write the tests** — spec §10.5 and §10.7 "`EndGrant` failure":
  - `Code_flow_happy_path`, moved here from 4.4 so that it runs through the production exchange: code → `/connect/token` → an access token with `aud == Norse.User`, `auth_time` equal to the cookie's and no role claim, an id token with `aud == client_id`, and a refresh token.
  - `Stamp_rotation_and_lockout_between_issuance_and_redemption_fail_with_invalid_grant`: a theory. The authorization reads `revoked` afterward, and so does every one of its tokens.
  - `Erasure_between_issuance_and_redemption_fails_with_invalid_grant`: the authorization and every one of its tokens are **absent** afterward, because `ErasureService` deletes them, and the redemption is still refused.
  - `Auth_time_is_carried_unchanged_across_refreshes` (advance 1 day between five refreshes).
  - `Norse_stamp_is_in_code_and_refresh_only` (unprotect through `IOpenIddictServerDispatcher`-backed test helper or the token manager's payload; assert absence from access and id tokens).
  - `User_tokens_carry_no_role_claims`.
  - `A_grant_missing_sub_auth_time_or_stamp_is_invalid_grant`.
  - `Step_one_failing_every_attempt_is_unconfirmed` (interceptor fails the statement three times; one Error log, counter +1, rejection still sent); `Step_two_failing_after_confirmation_leaves_the_authorization_revoked`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** §5.4 steps 1–3.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Stage.**

### Task 4.6: Guarded redemption and the grant boundary, interleaved

**Files:**
- Modify: `OpenIddict/RedeemTokenEntryGuarded.cs` (full behavior: one row proceeds; zero rows → `EndGrant` + `invalid_grant`; a database exception propagates unhandled, so the request fails as HTTP 500 with nothing minted, never retried, never `EndGrant`), `OpenIddict/EndGrantOnReuse.cs` (full behavior)
- Test: `tests/Identity.Web.Server.Tests/OpenIddict/GrantBoundarySuite.cs` (+ per provider), using `OpenIddictTestHost` pause points and `CommitFailureInterceptor`

- [ ] **Step 1: Write the tests** — spec §10.6 and §10.7 interleavings, verbatim in their assertions: `Rotation_issues_a_new_refresh_token`; `Production_refresh_chain_keeps_one_authorization` (r3: Task 0.3's continuity fact again, through the production endpoints, asserted on the rows); `Sequential_replay_ends_the_grant`; `Redeemed_code_replay_ends_the_grant`; `An_independent_grant_survives`; `Simultaneous_load`; `Late_load` (and its revocation variant); `Successor_after_the_sweep` (asserts rejection and `revoked`, not which check refused); `Successor_after_reuse_at_validation`; `Definite_failure_is_a_500_and_mints_nothing` (connection-open failure; token and authorization `valid`); `Indeterminate_failure_is_a_500_and_does_not_end_the_grant` (token reads `redeemed`; the retry then ends the grant).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the suite 20 times (`for i in $(seq 20); do dotnet test … || break; done`). Expected: PASS every run.
- [ ] **Step 5: Stage.**

### Task 4.7: Revocation

**Files:**
- Modify: `IdentityBuilderExtensions.cs` (revocation endpoint already set in 4.4; confirm no passthrough)
- Test: `tests/Identity.Web.Server.Tests/OpenIddict/RevocationSuite.cs` (+ per provider)

- [ ] **Step 1: Write the tests** — spec §10.8 and §10.9 (token and revocation items), each as written except erasure: for stamp rotation and lockout, the authorization and its tokens read `revoked`; for erasure, they read **absent**, and the next redemption is still `invalid_grant`. Then self-revoke of the current refresh token; revoking a predecessor; refresh racing revocation in both orders; filters; valid code → `unsupported_token_type` with no state change; redeemed code → 200 and grant ended; access token → 200, authorization `valid`; the client-contract retry; error bodies have `error` only; unknown/expired/revoked/redeemed → 200 identical bodies; `No_endpoint_answers_an_outcome_envelope`.
- [ ] **Step 2: Run.** Expected: FAIL where behavior is Norse's, PASS where OpenIddict's own (record which).
- [ ] **Step 3: Implement** whatever the failures demand (expected: error-description suppression via an `ApplyTokenResponseContext`/`ApplyRevocationResponseContext`/`ApplyAuthorizationResponseContext` handler clearing `ErrorDescription` and `ErrorUri`).
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Stage.**

### Task 4.8: Erasure of sessions and the pruner

**Files:**
- Modify: `ErasureService.cs` (delete order tokens → sessions → authorizations → users)
- Create: `OpenIddict/OpenIddictPruningService.cs`, `OpenIddict/OpenIddictPruningOptions.cs`, `Identity.Outcomes/OpenIddictSessionManagerOutcomeView.cs`; extend the token and authorization views with `PruneAsync`
- Test: `tests/Identity.Web.Server.Tests/ErasureSuite.cs` (extend), `OpenIddict/PruningSuite.cs` (+ per provider)

**Interfaces:**
- Produces: `OpenIddictPruningOptions { bool Enabled = true; TimeSpan Interval = 1 h; TimeSpan MinimumAge = 14 d; }` at `Identity:OpenIddict:Pruning`, validated on start (`Interval` in (0, 1 day], `MinimumAge` ≥ refresh lifetime); `sealed class OpenIddictPruningService : BackgroundService` using `TimeProvider`.

- [ ] **Step 1: Write the tests.** `An_erased_subjects_sessions_are_gone`; `Prunes_tokens_then_authorizations_then_sessions_past_minimum_age`; `Live_rows_are_kept`; `A_session_with_an_attached_token_survives_until_its_token_is_pruned`; `Disabled_prunes_nothing`; `A_failed_run_logs_an_error_and_the_next_run_proceeds`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Stage.**

### Task 4.9: Freshness on the four operations, `AccountRead`, the link start

**Files:**
- Modify: `Passkeys/GetPasskeyCreationOptionsCommand.cs`, `Passkeys/AddPasskeyCommand.cs`, `TwoFactor/DisableTwoFactorCommand.cs`, `TwoFactor/GetAuthenticatorSetupCommand.cs`, `TwoFactor/ResetAuthenticatorCommand.cs` (second `[Authorize]` + `[ReauthenticationLanding]`, T4-4); `Account/GetAccountStatusCommand.cs` and `Account/AccountService.cs` (`AccountRead`); `ExternalLoginEndpointRouteBuilderExtensions.cs` (link start: freshness as endpoint metadata, failure → redirect to `/Account/Login?returnUrl=<PathBase>/Account/Manage/ExternalLogins`)
- Test: `tests/Identity.Web.Server.Tests/FreshnessSuite.cs` (+ per provider), `CommandPolicyTests.cs`, `OutcomeSurfaceEnforcementTests.cs` (scan covers the new endpoints and pruner)

- [ ] **Step 1: Write the tests** — spec §10.11 and §10.12 (Himinbjörg-side items): `Each_gated_operation_answers_reauthentication_required_when_stale` (theory over the five commands, asserting `Code` and `NextUrl`); `A_fresh_caller_passes`; `The_link_start_redirects_when_stale`; `Other_failures_stay_bare`; `Every_command_naming_recent_authentication_declares_a_landing` (reflection over the assembly); `User_bearer_is_refused_by_every_account_operation` and with a cookie too; `User_bearer_is_refused_by_forgot_password_and_get_sign_in_methods` (no email sent); `User_bearer_reaches_get_my_personal_data_and_get_account_status`; `Machine_token_is_refused_by_self_account_read_account_and_public`; `User_token_is_refused_by_machine`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `dotnet test Himinbjorg.slnx`. Expected: PASS, coverage ≥ 85.
- [ ] **Step 5: Amend `Himinbjorg/CLAUDE.md` and `README.md`** (project table: OpenIddict endpoints, the pruner; architecture facts: grant boundary, marker, `auth_time`, `Norse.Bearer`; temporal list eight/non-temporal adds `sessions`). Stage. Himinbjörg ship gate.

---

## Phase 5 — Mímir, Yggdrasil and the record

### Task 5.0: Mímir's reference reads name their lanes (r4, fork `feature/openiddict-v8`)

**Files:**
- Modify: `Mimir/src/Reference.Web.Server/ReferencePolicyDeclarations.cs` (`ReferencePolicies.Public`: keep `RequireAuthenticatedUser()`, add `RequireLane(NorseLanes.Identity, NorseLanes.Machine)`), Mímir `CLAUDE.md`/`README.md`
- Test: `Mimir/tests/Reference.Web.Server.Tests/` policy facts

- [ ] **Step 1: Write the tests** (through `IAuthorizationService` with Midgard's real handlers, the policy registered as the generated `AddNorsePolicies()` registers it): `Reference_public_admits_the_identity_cookie`; `Reference_public_admits_a_machine_principal`; `Reference_public_refuses_a_user_bearer`; `Reference_public_refuses_an_empty_principal`.
- [ ] **Step 2: Run.** Expected: FAIL on the machine row.
- [ ] **Step 3: Implement.** The façade's `Machine` policy and audience pin are untouched; neither the generated default nor Heimdall's `AuthNPolicies.Public` is widened.
- [ ] **Step 4: Run** `dotnet test Mimir.slnx`. Expected: PASS.
- [ ] **Step 5: Stage.** Mímir ship gate.

### Task 5.1: Composition

**Files:**
- Modify: `Yggdrasil/Directory.Packages.props` (every `OpenIddict.*` → `8.0.0-preview.4`; realm bumps to the tags shipped above), `Yggdrasil/src/Hosting.Web.Server/appsettings.json` (`Identity:OpenIddict:Pruning`, `Norse:Authentication:RecentWindow`), the composition root
- Test: `Yggdrasil/tests/Hosting.Web.Server.Tests/CompositionLaneTests.cs`, `WirePathAuthorizationTests.cs`, `CompositionTests.cs`

- [ ] **Step 1: Write the tests.** the r4 façade and gRPC boundary facts (spec §10.12): `A_real_machine_token_reaches_the_rest_country_lookup` (endpoint and mediator authorization both pass), `An_identity_cookie_alone_is_refused_by_the_rest_facade`, `A_cookie_grpc_lookup_still_succeeds`, `A_user_bearer_is_refused_by_the_rest_facade` (theory: alone, and with a valid cookie), `A_machine_token_alone_is_refused_by_the_grpc_lookup`, `A_machine_token_is_refused_by_self_account_read_account_and_public`; `A_probe_reaches_its_endpoint_with_no_principal`; `Wasm_policy_evaluation_is_unchanged_for_the_cookie_principal`; `The_host_starts_with_norse_bearer_registered`; `Recent_authentication_and_account_read_are_registered`; `Authorize_and_revoke_are_mapped`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `dotnet test Yggdrasil.slnx`. Expected: PASS.
- [ ] **Step 5: Stage.** Yggdrasil PR with every bump folded.

### Task 5.2: The end-to-end walk and the record

**Files:**
- Modify: `Glitnir/docs/Himinbjorg/specs/2026-10-29-openiddict-authorization-code-pkce-step-up-design.md` (§13 "As built"; T4-1's amendment of §5.2's direct-sign-in bullet; §10.8's erasure expectation corrected from `revoked` to absent; any finding from H1–H6), `Bifrost/CLAUDE.md` and `Bifrost/README.md` (state of the union, in sync), memory (`train-4-is-openiddict.md` updated to shipped)

- [ ] **Step 1: Walk** against a disposable Postgres with Yggdrasil's web server: code + PKCE with a loopback client (`curl` plus a browser for the login), refresh twice, replay the first refresh token (expect `invalid_grant`), revoke, step-up from `/Account/Manage/Passkeys` with an aged cookie (expect the login hop and a return). Record each response status in §13.
- [ ] **Step 2: Write the record** and stage. The full AppHost stand-up waits for Buvy's volume wipe.

---

## Self-review record

- **Spec coverage:** §4 → 0.1, 4.8; §5.1 → 4.4; §5.2 → 4.3, 4.4; §5.3 → 4.4; §5.4 → 4.5; §5.5 → 0.2, 4.5, 4.6, 4.7, 4.8; §5.6 → 4.8; §5.7 → 4.4, 4.7; §5.8 → 4.4, 4.5, 4.8, 4.9; §6.1 → 4.2, 4.3; §6.3 → 1.1, 1.2, 2.5, 4.9; §6.4 → 4.4; §7.1 → 2.1, 4.1; §7.2 → 2.2; §7.3 → 2.2, 2.3; §7.4 → 1.1, 4.1; §7.5 → 1.1, 2.4, 3.1, 4.9, 5.1; §8 → 5.1; §9 → phase order; §10.1–14 → mapped in each task's Step 1.
- **Carriers decided here, not in the spec:** T4-3 (freshness on the command only), T4-4 (landing attribute), T4-5 (`Problem.Code`/`NextUrl` and the trailer), T4-6/T4-7 (authorize edge cases). A reviewer may reject any of them independently.
- **Type consistency:** `IGrantTermination`/`GrantEnd` (0.2 → 4.5, 4.6); `GrantStatements` (0.2 → 4.5, 4.6); `ReauthMarkers`/`AuthorizeTransaction` (4.3 → 4.4); `UserPrincipalFactory` (4.4 → 4.5); `NorseLanes`/`NorseAudiences`/`NorseClaimTypes` (1.1 → 2.x, 3.1, 4.x); `ReauthenticationLandingAttribute` (1.2 → 2.5, 4.9); `LocalNavigation.IsLocal` (1.2, used only by the two component bases); `PolicyCache.OperationPolicies`/`RequiresRecentAuthentication` (2.5, T4-3); `AuthorizeTransaction` framing (T4-10 → 4.3, 4.4).
- **Spec errata found in plan review:** §10.8 says erasure leaves the authorization `revoked`, but `ErasureService` deletes it. The plan asserts absence, and Task 5.2 corrects the spec.
- **Proportion:** no bodies beyond the spec's SQL and the emitter's one-line shape; tests are named with their discriminating assertions.
