# Principal at the Door — Design

**Status:** APPROVED at the design gate 2026-08-21; **amended twice during planning** (2026-08-21) where plan review proved an approved section unbuildable. Both amendments are marked inline in the sections they change (§2.2 probe lane, §3 policy hook) and both are retractions, not deferrals. Rev 1 was remanded on seven blocking objections, rev 2 on three authentication-selection blockers; planning surfaced two more. All verified against source, none contested.
**Reading order:** this spec and `../plans/2026-08-21-principal-at-the-door.md` must agree. Where an inline amendment appears, the amendment is current and the text above it is history — an executor following an un-amended section would rebuild something deliberately deleted.
**Realms touched:** Svartálfheim (NORSE013), Asgard (`NorsePolicies` + `NorsePlatformPolicies`, `NorsePolicyAttribute`, transport contract, `ErrorCategory` amendment), Midgard (anonymous/browser/probe/machine schemes + selector, policy generator, both folds, client decode), Yggdrasil (composition), Heimdall + Mímir (policy declarations), Glitnir (this verdict).
**Issues:** NorseArchitecture/Asgard#71 (foundation) · consumers: NorseArchitecture/Himinbjorg#49 · #50 · #51 · #52 · NorseArchitecture/Asgard#57 · #58.
**Inherits without re-litigation:** one issuer, `aud` discriminates (`2026-06-07-auth-design.md` §4.4); every request carries a non-null principal, `[AllowAnonymous]` does not appear in the platform (§5.1, declared 2026-06-07, unbuilt until now); the anonymous GUID carries over at registration (Himinbjorg#55); `ErrorCategory.InvalidCredentials` is live, not vestigial (ruled 2026-08-08); facade controllers are realm source carrying no wire-format machinery (Futhark §4 + 2026-08-09 residence amendment).

---

## 0. Scope

> **Every request that reaches the mediator carries a principal with a GUID. Every policy is declared once and registers everywhere. Every authentication or authorization failure answers one of exactly two questions.**

The Himinbjorg#49 brainstorm kept discovering its lane story had no floor: #49 could not say what a machine token is worth without the other doors existing, could not register its policy without becoming the fourth hand-rolled `AddPolicy` lambda, and could not state its 401 discipline without touching two hand-written transport folds whose agreement is asserted by a doc comment rather than enforced. All three are foundation concerns; none are about machines.

**Not here:** OpenIddict server config and any grant flow (Himinbjorg#49, #50, #52) · the bearer scheme (#49) · NORSE012 (#49 — see §5.1) · CIDR and signed-webhook doors (#51) · the policy *vocabulary* above the anonymous and probe lanes (Asgard#57 — this story ships the hook it registers through) · the per-channel matrix (#58) · seed attribution stamping (§9.1).

## 1. Rulings

1. **`[AllowAnonymous]` is banned outright**, and the law means *any authored `IAllowAnonymous` metadata* — attribute or fluent `.AllowAnonymous()` (§5.2). The ban ships with the door, never ahead of it.
2. **Anonymous is a real authentication scheme**, not middleware patching `HttpContext.User`. Answers Himinbjorg#51 open question 5 early and makes #51's later handlers additive.
3. **Exactly one authentication result contributes per request** (§2.2) — the browser composite may internally invoke the Identity or Anonymous handler, but two results never merge into one principal. Naming two schemes on a policy is not a selection mechanism: ASP.NET Core authenticates each and merges identities, which would mint a second identity for an already-authenticated user.
4. **The anonymous cookie is issued on the browser lane only.** A facade request with no JWT is rejected at the authorization middleware; nothing is minted, nothing is stamped, and the realm never sees the request.
5. **`GrpcControllerBase` is the machine-lane marker**, read by three mechanisms (§5.1). Inheritance *is* lane assignment; no route lists, no path predicates.
6. **No ambient default scheme.** Each lane's policy names its own. An endpoint declaring nothing gets no principal rather than the wrong one.
7. **Two questions, two answers.** *Who am I? Unknown* → **401, silent.** *Can I do the thing? No* → **403, minimal context.** Scoped to authentication and authorization failures; validation, conflict, absence, erasure, and faults keep their own answers.
8. **`NotAllowed` is 403.** Ruled 2026-08-21. Its declaration (`ErrorCategory.cs:37`, *"a precondition failure, not an authorization failure"*) is amended in the same change — never a mapping that silently contradicts its own contract. Blast radius verified: the sole production producer is `LoginHandler:79` (`SignInResult.IsNotAllowed`), so no genuine precondition consumer exists to break.
9. **The transport mapping is one declaration** (§4.1), dependency-safe, exhaustive at compile time.
10. **Silent categories carry no body structurally.** No branch exists that emits one.
11. **The decode contract is lossy and says so** (§4.3). Status is not injective across `ErrorCategory`; the silence ruling makes that a deliberate contract rather than a defect.
12. **The anti-enumeration collapse dissolves.** `LoginHandler`'s `_invalidCredentials` static instance exists so two `Problem`s compare equal by reference; with nothing on the wire there is nothing to compare.
13. **Every ruling ships as a mechanism.** §8 is the ledger; anything prose-only is named there as a gap.

## 2. Principal at the door

### 2.1 The anonymous scheme

`NorseAnonymousHandler` (Midgard `Infrastructure.Web.Server`, `internal sealed`), a named authentication scheme. When selected it mints a v4 GUID, builds a `ClaimsPrincipal` carrying that id and the anonymous role, returns `AuthenticateResult.Success`, and writes the cookie on the response.

It is **selected**, never self-deciding — §2.2 owns that. It is never selected for the machine lane, and never for a request whose response cannot carry a Set-Cookie.

### 2.2 Scheme selection — the executable mechanism

The invariant is **exactly one authentication result contributes** — not that exactly one handler runs. The browser composite may internally invoke the Identity or Anonymous handler; what it never does is let two results merge into one principal.

Two layers, because a policy scheme **cannot** provide result-dependent fallback: `ForwardDefaultSelector` resolves one scheme name, and a failed `AuthenticateAsync` on the forwarded handler is a failure, not a retry. Fallback therefore lives *inside* a handler that owns it.

**Layer 1 — the lane selector.** `AddPolicyScheme(NorseSchemes.Default)` with a `ForwardDefaultSelector` that reads **endpoint shape only** — never credentials, never a handler, so there is no recursion and no result dependence:

| Endpoint shape, in order | Forwards to |
|---|---|
| `GrpcControllerBase` descendant (REST facade) | `NorseSchemes.Machine` |
| gRPC endpoint — `GrpcMethodMetadata` present in `GetEndpoint().Metadata` | `NorseSchemes.IdentityCookieOnly` |
| Declares `NorsePolicies.Probe` in its `IAuthorizeData` | `NorseSchemes.Probe` |
| Everything else (the browser lane) | `NorseSchemes.Browser` |

The gRPC row is structural and framework-supplied: `MapGrpcService` attaches `Grpc.AspNetCore.Server.Model.GrpcMethodMetadata` to every gRPC endpoint, and protobuf-net.Grpc's code-first services ride the same binder. `GrpcControllerBase` identifies REST facades **only** — it does not identify `ReferenceService` or `AuthenticationService`, which is why this row is required and why its absence in rev 1 would have minted anonymous cookies for credentialless gRPC calls. *(Verify the metadata type at plan time; if code-first binding does not attach it, a Norse marker is added at `MapGrpcService` time instead — the row's position and behavior do not change.)*

> **Amendment (2026-08-21, during planning).** This table originally had three rows and this section claimed probe endpoints *"never enter this selector's jurisdiction."* **That was wrong and the claim is retracted.** Naming `NorsePolicies.Probe` governs authorization; it does not prevent authentication from running. With no probe row, a liveness probe was neither facade nor gRPC, fell through to the browser composite, and was handed an anonymous cookie — contradicting §5.2's own "a kubelet is not a browser and mints no cookie." The probe row matches on the policy name the endpoint already declares rather than on a second marker, so one declaration drives both authorization and lane assignment and an endpoint cannot be in the probe lane for one and the browser lane for the other. `NorseSchemes.Probe` authenticates nothing and writes nothing; `NorsePolicies.Probe`'s always-succeed assertion is what lets a `NoResult` authentication still pass authorization — the two halves are designed together.

**Layer 2 — `NorseSchemes.Browser`, the composite that owns fallback.** One handler, one result, fallback internal:

1. Identity application cookie present → authenticate it. **Success → return it.** No anonymous identity, no anonymous Set-Cookie.
2. Identity cookie present but invalid or expired → **delete it** and continue to 3. Deletion is `Response.Cookies.Delete(name, options)` with `Path`, `Domain`, `Secure`, and `SameSite` matching the values it was written with — a browser ignores a delete whose attributes do not match, so rejecting the cookie is not the same act as removing it.
3. Anonymous cookie present and unprotects → return that principal.
4. Otherwise mint a fresh anonymous GUID, write the cookie, return it.

Step 2 into step 4 is §5.5's "new UUID for everyone," applied to expiry as well as logout.

**Provenance precedence** is deterministic and identity outranks anonymous. A request presenting both cookies stops at step 1; the anonymous cookie is never read and can never contribute a claim, so a forged one cannot influence authenticated provenance.

**`NorseSchemes.IdentityCookieOnly`** forwards to the Identity application cookie with no fallback and no minting. Failure is failure: a credentialless gRPC or gRPC-Web call authenticates as nothing and is rejected at `UseAuthorization()`.

**`NorseSchemes.Machine`** is a registered scheme from day one, not a dangling name — forwarding to an unregistered scheme throws a handler-lookup exception rather than producing a clean 401. Until #49 its handler is `NorseMachineRejectionHandler`: `NoResult` on authenticate, a silent 401 on challenge (§4.4), never a cookie, never a body. #49 replaces the forward target with Bearer and deletes the handler.

**Schemes registered by this story, named explicitly:** the ASP.NET Core Identity application cookie and its external/two-factor auxiliaries (already registered by `AddIdentity`, untouched here) · `NorseSchemes.Anonymous` · `NorseSchemes.Browser` · `NorseSchemes.IdentityCookieOnly` · `NorseSchemes.Probe` · `NorseSchemes.Machine` · `NorseSchemes.Default` (the selector). Bearer arrives with #49; #52 adds it to the gRPC lane for MAUI.

### 2.3 The anonymous cookie protocol

| Concern | Ruling |
|---|---|
| Name | `Norse.Anonymous` — matching the identity cookie's existing de-fingerprinting posture; never `.AspNetCore.*` |
| Format | Data Protection–protected payload, purpose string `Norse.Anonymous.v1` |
| Canonical claim | `ClaimTypes.NameIdentifier`, GUID `"D"` format — the same claim type `IdentityOptions.ClaimsIdentity.UserIdClaimType` resolves to, so downstream code reads one claim regardless of lane |
| Authentication type | Non-empty, so `Identity.IsAuthenticated` is **true** — see §2.4 |
| Attributes | `Secure`, `HttpOnly`, `SameSite=Lax`, `Path=/`, host-only, `IsEssential = true` (identity, not tracking — outside consent gating) |
| Lifetime | 30 days sliding, per `2026-06-07-auth-design.md` §12 |
| Renewal | On the browser lane only. Never renewed or issued on a machine or gRPC lane |
| Duplicated or malformed payload | Recovered only when it reaches the handler: treated as absent, fresh mint, overwrite. Never a failed request |
| Oversized | **Not a handler concern.** A cookie or header exceeding Kestrel's request-header limits is rejected by the host before authentication runs — 431, host default, unmodified. Recovery is not promised where the handler never executes |
| Registration | The GUID carries over into the identity record (Himinbjorg#55); this story writes no user row |

### 2.4 `IsAuthenticated` is true, and 403 follows — deliberately

`AuthorizationBehavior:28` selects the failure category as `user.Identity is { IsAuthenticated: true } ? Forbidden : Unauthorized`. An anonymous principal is authenticated, so **an anonymous visitor failing a real policy receives 403, not 401.**

That is affirmed, not tolerated. The visitor carries a GUID: *who am I* is answered, *can I* is the failing question, and §1.7 assigns 403. The consequence is that a browser's login redirect must come from the cookie handler's challenge path — it is a presentation concern at the edge, never the mediator's business.

### 2.5 Circuits and cookieless lanes

**Deferred sign-in is not used here.** `IDeferredSignIn` stashes an already-decided sign-in and yields a completion URL; it neither detects that a browser will return a cookie nor delivers a URL to a caller who has not asked for one.

Instead: **a circuit inherits the identity established during its initial HTTP handshake.** The Blazor Server page load and the WASM host page are ordinary requests that can write a cookie, so by the time a circuit exists its identity already does. No mid-circuit minting, no completion dance, no concurrent-mint race — there is exactly one mint per browser, on a request that was always able to carry the cookie.

A gRPC call arriving with no cookie is not a browser and mints nothing — enforced by the selector's gRPC row (§2.2) forwarding to `NorseSchemes.IdentityCookieOnly`, which has no anonymous fallback. This is structural, not a convention: native gRPC and gRPC-Web both carry the metadata the row matches on.

### 2.6 What this makes true, and how it is enforced

The invariant is **"every request that reaches the mediator carries a principal with a GUID"** — deliberately narrower than "every request," because health probes and static files never touch the pipeline.

Enforcement is two-layer:

1. **The gate** — `UseAuthorization()`. A lane that establishes no principal is rejected there, above MVC and above the realm. `PrincipalSeedingFilter`/`PrincipalSeedingInterceptor` never run; the handler never runs.
2. **The backstop** — `PrincipalAccessor.Seed` refuses a principal without a GUID identifier. Unreachable if layer 1 holds; it exists so a future lane that forgets layer 1 fails loudly at the seam rather than silently seeding an empty principal.

**Consequence, in scope for this story:** `AuthNPolicies.Public` and `ReferencePolicies.Public` are `RequireAssertion(_ => true)` today and pass an empty principal. They become "any principal, anonymous included" — `RequireAuthenticatedUser()` under the new composition. Since the anonymous scheme is never selected on the machine lane, **Mímir's REST facade returns 401 for a request with no JWT or a bad JWT, and #49 reopens it for machines.** Mímir's source does not change. Yggdrasil's swoop and `CountryLookupE2ETests` fixtures are updated in the same train.

## 3. The policy hook

Three `{Context}Policies` classes exist — Heimdall's `AuthNPolicies` and `IdentityPolicies`, Mímir's `ReferencePolicies` — each hand-registered at Yggdrasil's composition root. A fourth guarantees this conversation repeats.

**Declaration (Asgard, `Abstractions.Web.Server`).** A realm declares a policy as a `public static void` method taking an `AuthorizationPolicyBuilder`, decorated with `[NorsePolicy(Name)]`:

```csharp
[NorsePolicy(AuthNPolicies.Public)]
public static void ConfigurePublic(AuthorizationPolicyBuilder policy) => policy.RequireAuthenticatedUser();
```

The **name** lives in the attribute, the **shape** lives in the method. One declaration with two facets — never two representations that could disagree. The method receives the full `AuthorizationPolicyBuilder`, so a realm may build any policy expressible today, including the refined ability-style setups Asgard#57 anticipates.

> **Amendment (2026-08-21, during planning).** This section originally specified an `INorsePolicyContributor` / `INorsePolicyRegistry` interface pair plus a class-level declaration attribute, with a NORSE015 analyzer keeping name and body in agreement. **Plan review found that unbuildable and it is retired, not deferred.** The analyzer would have lived in Midgard's generator while Asgard declares the platform policies *upstream of Midgard*, so the one declaration most needing the check could never receive it without inverting the realm dependency graph; Heimdall and Mímir would each have needed a generator reference dragged into their contract projects. Collapsing the two representations into one removes the need for the diagnostic entirely. `INorsePolicyContributor`, `INorsePolicyRegistry`, `NorsePolicyDeclarationAttribute`, and **the agreement form of NORSE015** do not exist and must not be reintroduced. The identifier NORSE015 is reused for a different and reachable rule — striking malformed `[NorsePolicy]` declarations — described in the bullets below.

**Registration (Midgard).** A source generator reads `[NorsePolicy]` **from metadata** across the compilation's **resolved reference set** (`SourceModule.ReferencedAssemblySymbols`) and emits `AddNorsePolicies()`, registering each name against its declaring method as a method group.

Metadata, not method bodies, because every realm reaches the composition root as a published package and a body does not cross that boundary. The resolved reference set and nothing beyond it, because the emitted code names each declaring type directly — discovering a symbol the consumer cannot resolve a reference to would emit code that does not compile. For SDK-style projects MSBuild flattens the NuGet closure into `@(ReferencePath)`, so a declaration two hops away is normally present; an assembly hidden by `PrivateAssets="all"` or reached only at runtime is genuinely out of scope and is documented as such.

**Composition (Yggdrasil).** One call replaces four lambdas.

**Ruled details, so this is plannable:**

- **Duplicate names fail at build time**, not last-write-wins — NORSE014, sibling to NORSE010's duplicate-handler strike, reading declared names from metadata so it is not blind in package mode.
- **A malformed declaration fails loudly**, never silently — NORSE015 (the number, reused for a rule that can actually run) strikes an attributed method that is not public, not static, not `void`, does not take exactly one `AuthorizationPolicyBuilder`, is generic, sits on an inaccessible or generic type, or carries a null/empty name. Attributes are inspected *before* any filtering, so an attributed private method is a build error rather than a policy that quietly never registers. **Enforced twice, disjointly:** Asgard ships a bundled analyzer that strikes source declarations in the project that authors them, with a real syntax location — the same placement reasoning as NORSE010/011, since every realm declaring a policy already references `Norse.Abstractions.Web.Server` to name the attribute. Midgard's generator strikes declarations arriving from referenced assemblies, which have no syntax to point at, reporting `Location.None` plus the qualified method name in the consuming build. The generator skips anything with a syntax reference, so a declaration is never struck twice. One rule, one id, two reachable places, no overlap.
- **Client/server split:** policy *names* stay client-safe constants on the existing `{Context}Policies` classes, so `AuthorizeView` keeps trimming UI. Declaration methods are server-side — a WASM assembly can name a policy and can never define one. Where a `{Context}Policies` class lives in a client-safe assembly (Mímir's `Reference.Contracts`), the constant stays there and the declaration goes on a server-side sibling.
- **Client evaluation is UX; the server is the law.** Trimming is never enforcement.
- **Policy names must be compile-time constants.** A vocabulary computed at runtime belongs to a custom `IAuthorizationPolicyProvider`, which composes alongside this mechanism — the sanctioned escape hatch, named so it is not mistaken for a gap.
- **`NorsePolicies`** (Asgard) is the home for platform-standard names and seeds Asgard#57's set with the anonymous and probe lanes; `NorsePlatformPolicies` carries their declarations. `NorsePolicies.Machine` is **not** here — it arrives with #49, declared through this same mechanism rather than beside it.

**Retrofit is in this story**, and sequenced ahead of the cutover (§6) so no publication interval exists in which the generator cannot see a realm's declarations.

## 4. The transport contract

### 4.1 Shape

```
readonly record struct TransportDisposition(int HttpStatus, int GrpcStatus, bool BodyPermitted)
```

Declared in `Abstractions.Contracts` beside `ErrorCategory`, carrying **ints**, not `StatusCodes` or `Grpc.Core.StatusCode`. That keeps the client-safe assembly free of ASP.NET Core and Grpc.Core — the reason a shared type could not simply reference both. Each edge casts to its own enum at the point of use.

One static table maps every member, written as a **switch expression with no default arm**. Adding an `ErrorCategory` member without declaring its disposition is CS8509, an error under warnings-as-errors — compile time, not test time. `Unspecified` maps explicitly to 500/`Unknown`/no body and is never emitted.

Both `GrpcControllerBase.ToProblemResult` and `ProblemExtensions.ToRpcException` project from this table. Two implementations can no longer disagree because there is only one.

### 4.2 The table

| Category | Question | HTTP | gRPC | Body |
|---|---|---|---|---|
| `Unauthorized` | who am I? unknown | 401 | `Unauthenticated` | **none** |
| `InvalidCredentials` | who am I? unknown | 401 | `Unauthenticated` | **none** |
| `LockedOut` | can I? no | 403 | `PermissionDenied` | minimal |
| `NotAllowed` | can I? no | **403** *(was 400)* | **`PermissionDenied`** *(was `FailedPrecondition`)* | minimal |
| `Forbidden` | can I? no | 403 | `PermissionDenied` | minimal |
| `Validation` | malformed | 400 | `InvalidArgument` | permitted |
| `Conflict` | | 409 | `AlreadyExists` | permitted |
| `NotFound` | | 404 | `NotFound` | none *(unchanged)* |
| `Erased` | | 410 | `NotFound` + `ErrorInfo.Reason` | receipt |
| `Fault`, `MultipleMatches` | | 500 | `Internal` | correlation id |

### 4.3 The lossy decode contract

`RpcExceptionExtensions` decodes `ErrorInfo.Reason` authoritatively today precisely because status is not injective. Silent categories carry no `ErrorInfo`, so the contract is stated rather than inferred:

- **Trailerless `Unauthenticated` → `Unauthorized`.** `InvalidCredentials` stops crossing the wire as a distinguishable category. That is the silence ruling working, not information lost by accident.
- **Trailerless `PermissionDenied` → `Forbidden`**; `NotFound` → `NotFound`; `Internal` → `Fault`; any other trailerless status → `Fault`.
- **Malformed trailer, or trailers stripped by a proxy** → decode as if trailerless. Never an exception from the decode path.
- The class doc comment's *"never the gRPC status code"* is amended to state the fallback and its reason.

**Every `DecodeProblem` consumer is audited in this train**, not only Heimdall's login surface. The login UI reacts to transport status and renders a fixed local string; the server sends nothing, so there is nothing to leak.

### 4.4 Silence at the middleware boundary

Fixing the two folds does not control responses the framework generates. 401-with-no-body requires explicit challenge and forbid handling at each boundary: MVC's authorization filter, the gRPC interceptor path, Razor Components/endpoint routing, and the cookie handler's `OnRedirectToLogin`/`OnRedirectToAccessDenied` (which must not emit a redirect body on API lanes).

**The anonymous-browser forbid path is pinned explicitly.** §2.4 rules that an anonymous principal failing a policy is a deliberate **403** — it is authenticated, so *who am I* is answered. Challenge/forbid customization must not quietly convert that into a login redirect or attach a response body: the cookie handler's redirect belongs to **challenge** (401, no principal at all), never to **forbid**. A login redirect on the anonymous-403 path would silently restore the 401-shaped behavior this design removed, and it would do so at the one boundary where nobody is looking. Browser, REST, and gRPC boundaries are exercised separately — each has its own customization surface and they fail independently.

## 5. The ban

### 5.1 The machine-lane marker

`GrpcControllerBase` is the machine lane by inheritance. Three mechanisms read it:

1. the scheme selector's first rule (§2.2) — **this story**;
2. the base class's `[Authorize(Policy = NorsePolicies.Machine)]` gate — Himinbjorg#49;
3. **NORSE012**, striking `[AllowAnonymous]` or a non-bearer-satisfiable policy on a descendant — Himinbjorg#49.

NORSE012 is deliberately **not** here: it convicts any facade action naming a policy the bearer scheme cannot satisfy, and `NorsePolicies.Machine` does not exist until #49, so shipping it now would strike Mímir's `CountriesController` with no legal fix. Same error as banning `[AllowAnonymous]` before building the door, one lane over. When it lands it ships bundled inside `Norse.Abstractions.Web.Server`, as NORSE010/011 do, numbered adjacent to NORSE011 rather than in the NORSE07x architecture band.

### 5.2 NORSE013 — the ban (Svartálfheim)

**Any authored construct adding `IAllowAnonymous` endpoint metadata is a build error** — `[AllowAnonymous]` and the fluent `.AllowAnonymous()` alike. An attribute-only rule would leave the escape hatch open while the ledger claimed the law was enforced; `Midgard/src/Infrastructure.ServiceDefaults.AspNet/WebApplicationExtensions.cs:27,31` uses the fluent form today, deliberately, on both health endpoints.

Lives in Svartálfheim (it keys on an ASP.NET Core type, not a platform type) and reaches every realm through the analyzer manifest / NORSE080 machinery. Governs **authored source only** — framework-emitted metadata is not its business.

**Health probes get a lane, in this train.** `NorsePolicies.Probe` requires nothing and is declared through the §3 hook; the two `MapHealthChecks` calls name it instead of `.AllowAnonymous()`. A kubelet is not a browser and mints no cookie; health endpoints never reach the mediator, so §2.6's invariant does not cover them. The point is that the exemption becomes named, greppable, and reviewable — #58's "nothing is anonymous by accident" satisfied rather than dodged.

NORSE013 ships in the same train as §2, never ahead of it.

## 6. Sequencing and ship gates

Strict dependency order, each behind its own gate (PR merged, CI green, tagged, published) — the migrations-framework shape.

1. **Asgard** — `TransportDisposition` + the table; `ErrorCategory` amendment (`NotAllowed`); `NorsePolicies` (anonymous, probe) + `NorsePlatformPolicies` declarations; `NorsePolicyAttribute`.
2. **Svartálfheim** — NORSE013, shipped inert until step 5, so the ban never precedes the door.
3. **Midgard** — the anonymous handler; the browser composite; `IdentityCookieOnly`; `NorseMachineRejectionHandler`; the lane selector; the policy-registration generator + duplicate-name diagnostic; both folds reprojected; `DecodeProblem`'s trailerless contract; `PrincipalAccessor` backstop; health endpoints onto `NorsePolicies.Probe`.
4. **Heimdall, Mímir** — policy classes gain `[NorsePolicy]` declaration methods (**additive**; the manual lambdas still work); Heimdall's login surface renders locally.
5. **Yggdrasil** — three-lane composition, no default; `AddNorsePolicies()` replaces the four lambdas; challenge/forbid handling per §4.4; swoop and E2E fixtures updated for the closed facade. NORSE013 enabled here.

Step 4 precedes step 5 deliberately: the retrofit is additive, so there is no interval in which the generated hook cannot discover a realm's declarations.

Himinbjörg is absent. Its fork is held by the EF thread (`feature/access-count-breakout`); #49 opens a second one only when there is something published to consume.

## 7. Testing

TDD throughout; `superpowers:subagent-driven-development` paired with `superpowers:test-driven-development` per Glitnir §2.8.

**Lane selection (§2.2 layer 1) — endpoint shape decides, credentials never do**

- REST facade endpoint → `NorseSchemes.Machine`, regardless of what cookies are presented.
- Native gRPC endpoint, with and without cookies → `NorseSchemes.IdentityCookieOnly`.
- gRPC-Web endpoint, with and without cookies → `NorseSchemes.IdentityCookieOnly`.
- Razor/page endpoint → `NorseSchemes.Browser`.
- A credentialless gRPC call mints nothing and writes **no `Set-Cookie`** — the rev-1 regression, pinned.
- `NorseSchemes.Machine` is resolvable before #49: forwarding to it produces a silent 401, never a handler-lookup exception.

**Browser composite (§2.2 layer 2) — exactly one result, fallback internal**

- Valid identity cookie → no anonymous identity and **no anonymous `Set-Cookie`**.
- Invalid/expired identity cookie → fresh anonymous GUID **and** a `Set-Cookie` deleting the identity cookie whose `Path`/`Domain`/`Secure`/`SameSite` match how it was written.
- Both cookies presented → identity wins; the anonymous cookie is never read and contributes no claim.
- A forged anonymous cookie alongside a valid identity cookie cannot alter the authenticated principal's claims.
- Tampered anonymous cookie alone → fresh mint, overwrite, request succeeds.
- Anonymous cookie alone, valid → that principal is returned; nothing is re-minted.

**The invariant (§2.6)**

- Every lane that reaches the mediator seeds a principal with a GUID — browser page, Blazor circuit, WASM, gRPC.
- A facade request with no credential is rejected at `UseAuthorization()`: `PrincipalSeedingFilter` does not run, the action does not execute, and the response carries **no `Set-Cookie`**. The absent header and the un-executed action are both assertions.
- `PrincipalAccessor.Seed` throws on a principal without a GUID (backstop, asserted directly).
- A circuit inherits the handshake's identity; no second GUID is minted across concurrent circuit operations.

**Transport (§4)**

- A fact enumerating every `ErrorCategory` member, asserting both folds project the declared disposition, with a closing fact that no member escapes the table.
- Silence is byte-exact: two different silent categories are indistinguishable on the wire.
- Trailerless decode for `Unauthenticated`, `PermissionDenied`, `NotFound`, `Internal`, an unmapped status, a malformed trailer, and stripped trailers.
- Challenge/forbid emit no body at each §4.4 boundary — browser, REST, and gRPC asserted **separately**, never as one fact.
- An anonymous browser principal failing a policy receives **403 with no redirect and no body** — not a login redirect, not a 401. Pinned at the browser boundary specifically, because that is the only lane whose challenge path has a redirect to leak.

**Policy hook (§3)**

- A realm's policy, declared once, authorizes on Blazor Server and WASM with no per-host edit — proven by adding one.
- Discovery reaches a declaration two hops away when the reference set is flattened as MSBuild flattens it, **and the generated output compiles clean**; discovery reaches nothing outside the resolved reference set, and still compiles clean.
- Every malformed `[NorsePolicy]` declaration is a build error — attributed private, instance, non-`void`, wrong-parameter, generic, and null/empty-name cases each strike NORSE015 rather than vanishing.
- A policy name containing a quote, backslash, or newline emits source that compiles.
- Duplicate policy names fail the build.

**NORSE013** — strikes the attribute and the fluent call; inert against framework-emitted metadata; health endpoints pass under `NorsePolicies.Probe`.

## 8. Enforcement ledger

| Ruling | Mechanism | Tier |
|---|---|---|
| No authored `IAllowAnonymous` anywhere | NORSE013 (attribute + fluent) | won't compile |
| Every `ErrorCategory` declares its transport shape | switch expression, no default arm → CS8509 | won't compile |
| Duplicate policy names | generator diagnostic | won't compile |
| Both transport edges agree | one `TransportDisposition` table, both folds project | single-source |
| Silent categories carry no body | no branch emits one | structural |
| Exactly one authentication *result* contributes | endpoint-shaped lane selector; fallback owned inside the browser composite, never asked of a policy scheme, never merged | structural |
| gRPC never mints an anonymous cookie | selector row on `GrpcMethodMetadata` | structural |
| No dangling forward target | `NorseSchemes.Machine` registered from day one (rejection handler until #49) | structural |
| Anonymous never minted on the machine lane | `GrpcControllerBase` inheritance is the marker | structural |
| Unauthenticated never reaches the realm | rejected at `UseAuthorization()`, above MVC | structural |
| No empty principal reaches the mediator | `PrincipalAccessor.Seed` guard | structural backstop |
| Policies register without per-host edits | compile-time discovery across the closure | structural |
| Anti-enumeration on credential failure | nothing on the wire to compare | dissolved |
| Health exemptions are named, not hidden | `NorsePolicies.Probe` through the hook | structural + greppable |

**Gaps — prose only, named deliberately:**

- **Cookie lifetime and attributes** (§2.3) are configuration; pinned by test, not by the compiler.
- **The anonymous GUID carrying over at registration** (Himinbjorg#55) is behavior; pinned by test.
- **Selector row order and the composite's four steps** (§2.2) are authored logic. The no-default ruling makes a miss fail loudly rather than silently, and the lane-selection and composite facts pin both.
- **Identity-cookie deletion attribute matching** (§2.2 step 2) is authored and asserted by test; nothing in the type system requires the delete options to match the write options.

## 9. Open items recorded for future stories

1. **Seed attribution has no seam.** No `user_id` stamping exists — no `ICurrentUser`, no principal accessor in Urðarbrunnr's chassis or Midgard's persistence; `SeedRunnerService` resolves contributors and calls `SeedAsync`. Himinbjorg#53's attribution criterion cannot be met by #49; the seam belongs to Ratatoskr#29's layer 1 and the EF thread. #53's other half — computing the deterministic application id and synthesizing the principal in-process — is free and rides with #49.
2. **`NorseSignInManager`'s lockout reorder** belongs to Himinbjorg#49. Verified against `aspnetcore/src/Identity/Core/src/SignInManager.cs`: `CheckPasswordSignInCoreAsync` runs `PreSignInCheck` *before* password verification, which is why `NotAllowed` leaks account existence on the first attempt with any password today.
3. **§8.7 rate limiting is unbuilt**, and bounds the hashing cost the item-2 reorder introduces. Ruled acceptable; recorded as a real ordering dependency.
4. **Amendments owed on merge:** `ErrorCategory.cs` `NotAllowed` (§1.8). `RpcExceptionExtensions` class comment (§4.3). `Heimdall/specs/2026-07-13-authn-identity-split-design.md` §9.3 — the "10000 times" quote is Register/`Conflict`'s and is miscited at `LoginHandler.cs:72` against the `LockedOut` branch. `2026-06-07-auth-design.md` §5.1's `YGG110` becomes NORSE013. `CategoryDisplay`'s `NotAllowed` string is re-read for a 403 context.
5. **Out-of-band lockout notification** — already named in §9.3 as the deferred mitigation for Register's `Conflict`; re-entry when Notifications exists.
6. **MAUI vs MCP separation** is not solved here or in #49. Grant type seals the REST lane; `aud` separates the two `authorization_code` lanes, in #50/#52.
7. **Anonymous principal rate limiting.** Minting on first contact is an unauthenticated write-free operation, but it is unbounded. §8.7's anonymous-bootstrap limit (60/min/IP) is the intended bound and is unbuilt.
