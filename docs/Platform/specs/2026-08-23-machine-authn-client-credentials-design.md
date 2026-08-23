# Machine Authn — OpenIddict Client Credentials — Design

**Status:** DRAFT — pending user review before `writing-plans`.
**Realms touched:** Himinbjörg (`Identity.EntityFramework`, `Identity.Web.Server`, `Identity.Migrations`), Asgard (`NorsePolicies`, `GrpcControllerBase`), Midgard (`AuthenticationBuilderExtensions`, `NorseSchemes`, OpenAPI transformers), Yggdrasil (`Hosting.Web.Server/Program.cs` composition, tests), Mímir (`CLAUDE.md` correction — no code change), Bifröst (AppHost — `oidc-signing-cert-pfx`/`oidc-signing-cert-password`/`oidc-machine-client-secret` parameters), Glitnir (this spec).
**Issues:** NorseArchitecture/Himinbjorg#49 (primary). Shares greenfield server config with #50; #52 and #53 build on this issuance/seeding pattern but are out of scope here.
**Inherits without re-litigation:**
- One issuer, `aud` discriminates (`2026-06-07-auth-design.md` §4.4).
- Every request carries a non-null principal; `GrpcControllerBase` is the machine-lane marker, read structurally by `NorseLaneSelector` (`2026-08-21-principal-at-the-door-design.md` §2.2, §5.1).
- `NorseSchemes.Machine` is already a registered scheme forwarding target; its handler is currently the placeholder `NorseMachineRejectionHandler`, explicitly documented as living "until #49."
- `NorsePolicies.Machine` is a reserved-but-undeclared name — `NorsePolicies.cs` carries a comment naming this issue as the one that declares it.
- Flags-enum wire law: JSON and XML both represent `[Flags]` members as governed-name arrays (`2026-08-02-futhark-enum-wire-law-design.md`, amended 2026-08-09 in Midgard commit `aba802f3` — NORSE029 retired, closure-walk widened to referenced assemblies).

---

## 0. Scope

**Ships in this pass:**
1. OpenIddict's `client_credentials` grant, live in Himinbjörg — token endpoint, one seeded machine client, JWT access tokens, local signing/encryption certs.
2. In-process JWT validation (`.AddValidation().UseLocalServer()`) registered under `NorseSchemes.Machine`, replacing `NorseMachineRejectionHandler`.
3. `NorsePolicies.Machine` declared; `GrpcControllerBase` carries `[Authorize(Policy = NorsePolicies.Machine)]` at the class level — every facade controller protected by construction.
4. OpenAPI bearer security scheme declared on facade routes.
5. End-to-end proof that a real OpenIddict-issued JWT reaches Mímir's already-routable `CountriesController` and gets a correct response in both `application/json` and `application/xml` — the route exists and is reachable today (proven by an existing test); what's unproven is the wire shape, not the routing (§6).
6. Correction of the now-stale flags/XML tripwire passage in Mímir's `CLAUDE.md`.

**Explicitly not here** (per the issue's own non-goals, unchanged): interactive/authorization-code flows (#50), gRPC-Web/MAUI channels (#52), scope/permission vocabulary beyond "authenticated machine may reach the facade," the authz-lockdown arc's richer policy contribution mechanism, and any custody-seam integration (Bifröst#14 is blocked on unstarted PII work — dev certs are not an interim measure, they are the only option today).

## 1. Rulings

1. **Same-process topology, committed.** The REST facade and the OpenIddict authorization server run in the same host (Yggdrasil's `Hosting.Web.Server`) today, with no plan to split them. Validation uses OpenIddict's own `.AddValidation().UseLocalServer()` — no JWKS fetch, no metadata address, no standalone `JwtBearer` middleware.
2. **One audience for the whole facade.** A single `aud` — `"Norse.Facade"`, matching the `Norse.X` naming convention already established by `NorseSchemes`/`NorsePolicies` — covers every `GrpcControllerBase`-derived controller platform-wide. Per-endpoint authorization is a scope/policy concern, not an audience concern. No per-bounded-context audience registry is built here.
3. **Signing/encryption certs: Aspire-managed parameters, persisted.** Generated once by the AppHost, held as parameter resources, fed into `.AddSigningCertificate(...)`/`.AddEncryptionCertificate(...)`. Survives AppHost restarts, so previously issued tokens stay verifiable across a dev-loop restart. The seeded machine client's secret follows the same custody pattern — an Aspire parameter, never a checked-in value. §2.3 names the exact parameters and resource wiring.
4. **Client custom data rides real scalar columns, not the stock `Properties` JSON dictionary.** No new column lands in this pass (one facade-wide audience means nothing per-client to store yet), but the convention is set now: when #51's CIDR block or a per-client audience override needs a home, it becomes a typed, queryable column on `NorseOpenIddictApplication`, consistent with the platform's general preference for typed schema over JSON blobs.
5. **Access tokens are signed, not encrypted (JWS, not JWE).** `.DisableAccessTokenEncryption()` on the server builder. Same-process validation never needed decryption; the deciding factor is #52's eventual external validator, which can verify a signature without custody of the encryption key, but would need that key shared out-of-band for a JWE. A signing-only credential removes that future custody problem entirely.

## 2. Himinbjörg — OpenIddict server config

### 2.1 Server + validation registration

Verified against the installed `OpenIddict.Server`/`.Server.AspNetCore`/`.Validation.AspNetCore` 7.6.0 API surface (no assumption carried from generic docs). `Identity.Web.Server` gains an `.AddServer(...)` call, most likely inside `AddNorseAuthenticationService(...)` or a sibling extension:

- `AllowClientCredentialsFlow()`
- `SetTokenEndpointUris("/connect/token")`
- `DisableAccessTokenEncryption()` (§1.5)
- `AddSigningCertificate(...)` / `AddEncryptionCertificate(...)` sourced from the Aspire-managed cert parameters (§2.3) — an encryption certificate is still registered even though access tokens go unencrypted, because #50's interactive flow will need it for other token types.
- `UseAspNetCore(options => options.EnableTokenEndpointPassthrough())` — verified as the load-bearing call: without `EnableTokenEndpointPassthrough()`, OpenIddict owns `/connect/token` entirely internally and never reaches the application-authored handler in §2.2. `.UseAspNetCore()` alone is not sufficient, corrected from the prior draft.

There is **no server-builder call that sets the audience on issued tokens** — `RegisterAudiences(string[])` exists but its own doc comment scopes it to RFC 8693 Token Exchange, not client_credentials, so it plays no role here. Audience assignment happens in the exchange handler (§2.2); audience *enforcement* happens on the validation side:

```csharp
.AddValidation(options =>
{
    options.UseLocalServer();
    options.AddAudiences("Norse.Facade");   // OpenIddictValidationBuilder — verified 7.6.0 API
    options.UseAspNetCore();
});
```

`.UseAspNetCore()` here registers a handler under the fixed name `OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme` — confirmed no override exists on `OpenIddictValidationAspNetCoreOptions` in this version. §4 covers how `NorseSchemes.Machine` bridges to that fixed name.

### 2.2 The client-credentials exchange handler

This is the piece that actually mints a token — OpenIddict validates the client's credentials against the seeded application and calls into application code to build the principal; it does not invent one. A new handler (minimal API endpoint or a small controller, host-agnostic-adjacent, living in `Identity.Web.Server`) reacts to the `client_credentials` grant type on the token endpoint:

1. Read the already-authenticated `OpenIddictRequest` — OpenIddict has verified the client secret against the seeded application before this handler runs (`RequireClientAuthentication` is the default for a confidential client), and has already rejected any grant type other than `client_credentials` before reaching this handler, since only `AllowClientCredentialsFlow()` is enabled (§2.1) — there is no other flow for a request to arrive as.
2. Assert `request.IsClientCredentialsGrantType()` as an **internal invariant**, not a user-facing branch — a violation here means the flow-gating in §2.1 broke, not that a caller sent a bad request. No `invalid_grant`/`unsupported_grant_type` response is authored by this handler; corrected from the prior draft, which wrongly modeled OpenIddict's own pre-handler rejection as something this code needed to do itself.
3. Build a `ClaimsIdentity` — `sub` claim set to the application's client id, with `SetDestinations` marking it for the access token. **Correction (2026-08-23, post-implementation, whole-branch review):** `sub` alone is not sufficient — Midgard's `PrincipalAccessor.Seed` (`2026-08-21-principal-at-the-door-design.md` §2.6) unconditionally requires every principal reaching the mediator to also carry a `ClaimTypes.NameIdentifier` claim parsing as a non-empty GUID, on every lane, and no claims transformation exists anywhere on the platform to derive one from a client_id string. This step also stamps `ClaimTypes.NameIdentifier`, sourced from the seeded `NorseOpenIddictApplication`'s own `Id` (looked up via `IOpenIddictApplicationManager.FindByClientIdAsync`/`GetIdAsync`), `SetDestinations`-marked the same as `sub`. The two rulings were independently correct and never reconciled until an end-to-end proof caught the gap; enforcement lives in `OpenIddictTokenEndpointTests`, this note exists so the next reader of this step doesn't reproduce the original, incomplete shape.
4. `identity.SetAudiences("Norse.Facade")`.
5. `return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);`

### 2.3 Signing/encryption cert and client-secret custody — concrete mechanism

Verified against the installed Aspire 13.5.2 API (`Aspire.Hosting.ParameterResourceBuilderExtensions`), not left open:

- **`oidc-signing-cert-pfx` — one parameter, not two.** The prior draft split PFX bytes and their password into independent generated parameters, which can't safely coordinate — the PFX callback needs the exact password the password callback produces, and nothing sequences that. Resolved instead with a single custom `ParameterDefault` subclass (`Aspire.Hosting.ApplicationModel.ParameterDefault`, overriding `GetDefaultValue()`) whose one callback generates a self-signed dev `X509Certificate2` and exports it to PFX bytes **with an empty password**, base64-encoded. An empty PFX password is a deliberate simplification, not an oversight: the PFX itself is already `secret: true, persist: true` (below), so a second secret protecting the container adds no real defense-in-depth for a local-dev, same-process credential. Reused as both the signing and encryption certificate (§2.1) — single cert, both roles; #50 revisits if an interactive flow needs a distinct encryption cert.
- **`oidc-machine-client-secret`** — the seeded client's plaintext secret, via Aspire's built-in generated-password default (`GenerateParameterDefault`/`CreateDefaultPasswordParameter`), not a custom generator.
- **Both parameters use `AddParameter(name, ParameterDefault, secret: true, persist: true)`.** `persist: true` is Aspire's own documented mechanism for exactly this case — *"typically done when the value is generated, so that it stays stable across runs"* — and is what actually makes the "tokens survive an AppHost restart" claim true, rather than an assumption riding on unspecified behavior.

AppHost wiring (extends `AppHost.cs`'s existing `WithReference`/`WithEnvironment` pattern):

- `web` (`Hosting.Web.Server`, the issuance + validation host) receives `oidc-signing-cert-pfx` — it mints and validates tokens, never the plaintext client secret.
- `migrations` receives `oidc-machine-client-secret` — it seeds the application row and needs the plaintext to reconcile against `NorseOpenIddictApplication.ClientSecret` via `OpenIddictApplicationManager` (§3).
- A test host exercising `/connect/token` reads the same `oidc-machine-client-secret` parameter (or an equivalent test-configuration value), never a hardcoded literal.

`Identity.EntityFramework`'s existing `AddNorseOpenIddictCore()` (the `.AddCore(...)` call) is untouched — this pass only adds the server/validation/exchange-handler pieces beside it.

## 3. Seeding

A new `ISeedContributor` (Asgard's platform-wide seeding contract, unused in Himinbjörg today) lands in `Identity.Migrations`, alongside the existing `NorseIdentityMigrationContributor`, following Mímisbrunnr's reference implementation shape:

- `ConfigureServices` registers whatever `OpenIddictApplicationManager<NorseOpenIddictApplication>` the seeder needs.
- `SeedAsync` seeds one machine client with the full descriptor:
  - `ClientId` — a stable, well-known value (not generated per run).
  - `ClientType = ClientTypes.Confidential`.
  - `ClientSecret` — from `oidc-machine-client-secret` (§2.3); `OpenIddictApplicationManager` hashes it on write.
  - `Permissions`: `Permissions.Endpoints.Token` **and** `Permissions.GrantTypes.ClientCredentials` — both are required (one without the other rejects every call to `/connect/token`, whether from a missing endpoint grant or a missing flow grant).
- **Idempotency is reconcile, not skip-if-exists — and reconcile is conditional, not unconditional.** A find-then-insert-only seeder would silently keep an old secret hash alive after `oidc-machine-client-secret`'s value changes underneath it. But unconditionally calling `UpdateAsync` with the plaintext secret on every run is its own bug: `OpenIddictApplicationManager` salts on write, so re-hashing an *unchanged* secret still produces a different stored hash and writes to the database every migrations run for no reason. The corrected sequence, every run:
  1. Find the application by `ClientId`. Not found → create with the full descriptor (§ above).
  2. Found → compare `ClientType`/`Permissions`/`DisplayName` against current configuration; call `OpenIddictApplicationManager.ValidateClientSecretAsync(application, configuredSecret)` to check the secret specifically.
  3. Update only the fields that actually differ — replace `ClientSecret` only when `ValidateClientSecretAsync` fails (the configured value no longer matches the stored hash), never unconditionally.
  4. No differences found → no write at all.

## 4. Asgard/Midgard — the policy wall

- **Asgard:** `NorsePolicies.cs` gains `Machine = "Norse.Machine"`. The declaration method lands in `NorsePlatformPolicies.cs` (same file as `Anonymous`/`Probe`, not a new file) as `[NorsePolicy(NorsePolicies.Machine)] public static void Machine(AuthorizationPolicyBuilder policy) => policy.RequireAuthenticatedUser();` — matching `Anonymous`/`Probe` exactly, no scheme pin. **Correction from the reviewed draft:** the prior text called for `.AddAuthenticationSchemes(NorseSchemes.Machine)` as "defense-in-depth," but `NorseSchemes` lives in Midgard (`Infrastructure.Web.Server`) and `Abstractions.Components` cannot reference it — Asgard is declared law, Midgard is infrastructure, and the dependency only runs one direction. Neither `Anonymous` nor `Probe` pins a scheme on their policy either, despite each having one; scheme selection is entirely `NorseLaneSelector`'s job (§ this section, below), and that structural routing is already the enforcement — a second, assembly-illegal pin would have added nothing.
- `GrpcControllerBase` gains `[Authorize(Policy = NorsePolicies.Machine)]` at the class level. This is new — the class carries no `[Authorize]` today. Every facade controller is now protected by inheritance; no per-controller opt-in exists or is needed.
- **The scheme bridge (verified, §2.1):** OpenIddict's validation handler always registers under its own fixed scheme name (`OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme`) — there is no version of this design where it registers directly as `"Norse.Machine"`. So `NorseSchemes.Machine` stays the literal name `NorseLaneSelector` already forwards to, but its *registration* in `AuthenticationBuilderExtensions.AddNorseAuthentication()` changes from the placeholder handler to a one-scheme `AddPolicyScheme(NorseSchemes.Machine, _ => { }, options => options.ForwardDefault = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)` — the same forwarding idiom `NorseSchemes.Default` already uses, just with a constant target instead of a request-shape selector. `NorseMachineRejectionHandler` is deleted outright, fulfilling its own doc comment's stated fate.
- The generated `NorsePolicyRegistration.AddNorsePolicies()` picks up the new `[NorsePolicy(Machine)]` declaration automatically — no hand-edit to `Program.cs` beyond what's already there.

## 5. OpenAPI

Two transformers, not one — a document transformer alone can't tell a facade operation from any other operation, and marking every operation as bearer-protected would be wrong the moment a non-facade endpoint exists:

- A **document transformer** (Midgard-owned, alongside `UnionLeakGuardTransformer`) registers the reusable `OpenApiSecurityScheme` component once (`SecuritySchemeType.Http`, `bearerFormat: "JWT"`).
- An **operation transformer** — matching the existing precedent already in this codebase (`AddOperationTransformer<StandardResponsesTransformer>()` in `Hosting.Web.Server/Program.cs`) — inspects each operation's controller/action metadata and attaches the security requirement only where the declaring type is a `GrpcControllerBase` descendant (equivalently, carries the `Norse.Machine` policy). Non-facade operations get no requirement.

Both register from Yggdrasil's existing `AddOpenApi(...)` block.

## 6. Mímir — proving the wire shape, not "host-wiring"

**Correction from the reviewed draft:** there is no host-wiring gap. `CountriesController` is already routable in the real host today — `CompositionTests.cs`'s `A_credentialless_call_to_the_facade_is_rejected_before_content_negotiation_runs` hits `/api/reference/countries/banana` against the real `WebApplicationFactory`-backed host and gets **401**, not 404, which is only possible if routing already resolved the endpoint. `Hosting.Web.Server` already references `Norse.Reference.Web.Server` and calls `AddControllers()`, which discovers controllers from every referenced assembly by default. Nothing needs to change to make the controller reachable.

What's actually unproven: whether the XML shape generator emits a **working** shape for `CountryResponse`/`Classification` when a real authenticated request reaches this real controller — every existing proof of the flags-array wire law (§ inherited rulings) is at the generator/formatter unit-test level or against the test-only `ParityController`/`SwoopHostFixture`, never against this specific response type in this specific host. This pass:

- Adds a composition test proving the *current* baseline first (credentialed request reaches the controller and negotiation runs) before adding the new authenticated-JWT coverage — so a failure is attributable to the new work, not a pre-existing gap being discovered for the first time under a misleading test name.
- Proves the authenticated JSON and XML round trip (§7).
- Corrects Mímir's `CLAUDE.md` line 37, which currently describes a flags-enum/XML *capability* gap that closed in Midgard commit `aba802f3` (2026-08-09) — hours after that doc passage was written. The only thing that remained true was proving the wire shape end-to-end, which is what this pass actually does.

## 7. Testing

Extends the existing real-host test pattern (`CompositionTests.cs`, `LaneCompositionTests.cs` — `WebApplicationFactory`/`WebServerHost.StartAsync()`):

- **Baseline (§6):** an authenticated request to `CountriesController` reaches content negotiation at all — establishes the floor before layering the new proof on top.
- **Positive path:** seeded client obtains a JWT from `/connect/token` via `client_credentials`; calls `GET /api/reference/countries/US` with `Accept: application/json` → 200, correct body; repeats with `Accept: application/xml` → 200, correct body, correct `Content-Type`.
- **Negative path (already exists, must keep passing unchanged):** `A_credentialless_call_to_the_facade_is_rejected_before_content_negotiation_runs` — bare 401, pre-negotiation.
- **Reseed-on-rotation:** seeding the same client three times — run 1 creates it, run 2 with a *changed* `oidc-machine-client-secret` proves the new value authenticates and the old one no longer does, run 3 with the *same* value as run 2 proves no write happens (§3's conditional-reconcile behavior). Conditional reconciliation is a design requirement (§3), not an optimization, so this test is required, not optional: the run-3 no-write assertion needs an observability seam (a captured `SaveChanges`/`UpdateAsync` call count, or an equivalent point the test can assert against) — if `Identity.Migrations`' seeding path doesn't already expose one, the plan adds it rather than dropping the assertion.
- **OpenAPI:** the facade operation carries the bearer security requirement; a non-facade operation does not (§5).
- **Issue exit criteria, explicit tests:** invalid/expired token → bare 401; zero checked-in secrets (verified by grep/CI check, not a runtime test); facade controller with no auth-related code of its own still rejects anonymous callers (structural proof, not per-controller).

## 8. Non-goals (restated from the issue, unchanged)

Interactive/user-facing flows, gRPC-Web/MAUI authentication, scope/permission vocabulary beyond "authenticated machine may reach the facade," scheme segregation for other channels, the authz policy-contribution function, custody-seam integration.
