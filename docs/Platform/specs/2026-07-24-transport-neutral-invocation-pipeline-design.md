# Transport-Neutral Invocation Pipeline & Generated Gateways

**Date:** 2026-07-24
**Status:** Approved design, pre-implementation
**Owner:** Buvy
**Realms touched:** Svartalfheim (appeal only, no change), Asgard, Midgard, Yggdrasil, Heimdall, Himinbjorg
**Precedent this generalizes:** Heimdall's hand-written `IAuthenticationGateway` / `BlazorServerAuthenticationGateway` / `WasmAuthenticationGateway` trio, and Urdarbrunnr's just-shipped `gen/` Roslyn generator packaging layout.
**Prior art referenced:** `GrpcControllerBase` pattern — https://github.com/protobuf-net/protobuf-net.Grpc/issues/264#issuecomment-1336253645

---

## 1. Context

A dumb Blazor component must render identically across every consumption channel — Server circuit, WASM, MAUI BlazorWebView — while the same service code also serves gRPC (protobuf-net.Grpc code-first) and, later, a partner-facing REST bridge. The component never knows or cares how its data or failure arrived; it receives a result envelope and renders.

Render-mode policy forces the issue: global render mode is Auto on the server, so the **same user in the same session** runs a component in-process during prerender/circuit and over gRPC after WASM hydrates. Any behavioral divergence between those two paths is a user-visible flicker, not a latent bug. Parity — including error parity and authorization parity — is a hard requirement.

### 1.1 Decided law (carried in from the brief, not reopened here)

1. The pipeline is transport-neutral — every channel (gRPC, REST, Server circuit, later Ratatoskr) is an adapter hosting the same behavior chain.
2. Components inject generated, per-service gateways — never the raw service contract. Two implementations per service (wire, in-process); DI binds the right one per host; the component cannot tell which one answered.
3. Service interfaces keep returning plain `TResponse` — the envelope is the internal error model, never the wire method signature. Nothing in-process throws to communicate.
4. Authorization is declared once (`[Authorize(Policy=...)]` on the service method) and enforced everywhere it applies, redundantly where channels overlap. Every Asgard-contracted service method without it is a build error.
5. `GrpcControllerBase` is retrofitted onto the in-process gateway for the REST bridge.
6. Auto-mode hydration persists the whole envelope — success or failure — across prerender-to-WASM handoff.
7. Realm placement: envelope + error-arm types in Svartalfheim; behavior contract + gateway contract shape in Asgard; standard behaviors in Midgard; adapters live with their hosts. *(Appealed — see §7.)*

### 1.2 What already exists (grounding, not assumption)

Before this design, the platform already had:

- **`Outcome<T>` / `Problem` / `ErrorCategory` / `BoolResponse`** — live in Asgard's `Abstractions.Web.Server/Mediator/`. `ErrorCategory` (byte enum): `Validation=1, NotFound=2, Conflict=3, LockedOut=4, InvalidCredentials=5, NotAllowed=6`. This is the platform's real in-process error envelope, not a new concept.
- **`OutcomeServerInterceptor`** (Midgard) — a single-purpose gRPC interceptor that catches a thrown `OutcomeFailedException` and translates it to `RpcException` + a custom JSON `problem-bin` trailer. Client-side mirror: `RpcExceptionExtensions.DecodeProblem`.
- **`IAuthenticationGateway`** (Heimdall) — the one worked example of "same contract, per-host implementation": `BlazorServerAuthenticationGateway` (in-process, calls the mediator handler directly) and `WasmAuthenticationGateway` (wraps the real gRPC proxy, decodes trailers). This design mechanizes exactly this pattern via a generator.
- **No pipeline/behavior-chain abstraction, no `[Authorize]`/policy contract, and zero `PersistentComponentState` usage anywhere in the platform.** These are new law, not extensions of something that exists.
- **protobuf-net.Grpc reinstated as the platform RPC stack** (`2026-07-13-protobuf-net-grpc-reinstated-design.md`) — `[ServiceContract]`/`[OperationContract]` decorate the C# interface directly; no `.proto` files; no `CallContext` parameter on interfaces that ship in a widely shared contracts assembly.

---

## 2. Verdicts

### 2.1 Envelope schema

**Realm placement:** `Outcome`, `Outcome<T>`, `Problem`, `ErrorCategory`, `BoolResponse` move from `Asgard/src/Abstractions.Web.Server` to `Asgard/src/Abstractions.Contracts` — currently an empty, purpose-built project ("the only cross-context-referenceable project" per Glitnir's naming convention). These types are plain records/enums with zero ASP.NET dependency; their current home is server-only by project name and by the platform's own boundary rule (`.Components` never references server-side types). WASM/MAUI gateways need to return the identical `Outcome<T>` the in-process gateway returns — that only compiles if the type lives somewhere WASM-safe. `IRequestHandler<,>`, `ICommandRequest<T>`, and `IDeferredSignIn` stay in `Abstractions.Web.Server` — those are genuinely server-only dispatch concerns a component never touches directly.

**`ErrorCategory` grows three members**, explicit values, never renumbering the existing six:

```csharp
public enum ErrorCategory : byte
{
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    LockedOut = 4,
    InvalidCredentials = 5,
    NotAllowed = 6,
    Unauthorized = 7,   // not authenticated — real path, anonymous-role callers hit real endpoints
    Forbidden = 8,      // authenticated, lacks the policy
    Fault = 9,          // unmapped failure
}
```

`Unauthorized` vs `Forbidden` split is deliberate, not REST-idiom cargo cult: every request carries an anonymous-cookie principal, so "not authenticated" (anonymous role attempting an endpoint that requires real sign-in) is a live, common case distinct from "authenticated but lacks permission." `InvalidCredentials` stays defined but is not actively produced, per the anti-enumeration ruling already recorded in `../Heimdall/specs/2026-07-13-authn-identity-split-design.md` §9.3 — never remove a shipped enum member; a service author chooses per call site whether revealing "not found" vs. a generic false gives a caller a distinguishable, useful next action. `NotFound` is not blanket-unsafe; it's evaluated case by case.

**`Problem` gains one field:**

```csharp
public sealed record Problem
{
    public required ErrorCategory Category { get; init; }
    public IReadOnlyDictionary<string, string[]> Errors { get; init; } = new Dictionary<string, string[]>();
    public Guid? CorrelationId { get; init; }   // populated only for Fault
}
```

**Deliberate, not inherited:** `CorrelationId` stays `Fault`-only, considered explicitly rather than assumed. Every other arm is deterministic and reproducible from the request itself — `Validation`'s field errors, `NotFound`'s resource, `Forbidden`'s policy are all already self-describing without a trace handle. `Fault` is the one arm where the cause isn't visible from the response at all, which is exactly why it's the one arm that needs one.

**Wire encoding — hybrid, with an explicit decode key.** The gRPC interceptor maps `Problem` onto `google.rpc.Status` + `error_details.proto` well-known types instead of today's custom JSON `problem-bin` trailer. The status-code column below is the partner-legible idiom — any standard gRPC client reads it correctly without knowing Norse exists. It is **not** the decode path for the platform's own client interceptor, because it isn't injective: `LockedOut`/`Forbidden` both land on `PERMISSION_DENIED`, and `Unauthorized`/`InvalidCredentials` both land on `UNAUTHENTICATED`, and a component must render those differently (lockout UI vs. a forbidden banner). Every response additionally carries a `google.rpc.ErrorInfo{ Reason = nameof(ErrorCategory-member), Domain = "norse.io" }` detail alongside the status code. The Norse client interceptor decodes `ErrorInfo.Reason` authoritatively — never the status code — so the round-trip is exact regardless of how many categories share a status.

| ErrorCategory | google.rpc.Status (partner-legible, not decoded from) | ErrorInfo.Reason (authoritative decode key) | REST |
|---|---|---|---|
| Validation | INVALID_ARGUMENT (3) + `BadRequest.FieldViolation[]` | `VALIDATION` | 400 ValidationProblemDetails |
| NotFound | NOT_FOUND (5) | `NOT_FOUND` | 404 |
| Conflict | ALREADY_EXISTS (6) | `CONFLICT` | 409 |
| Unauthorized | UNAUTHENTICATED (16) | `UNAUTHORIZED` | 401 |
| Forbidden | PERMISSION_DENIED (7) | `FORBIDDEN` | 403 |
| LockedOut | PERMISSION_DENIED (7) | `LOCKED_OUT` | 423 Locked |
| NotAllowed | FAILED_PRECONDITION (9) | `NOT_ALLOWED` | 422 Unprocessable |
| InvalidCredentials | UNAUTHENTICATED (16), vestigial | `INVALID_CREDENTIALS` | 401 |
| Fault | INTERNAL (13) + `DebugInfo{ CorrelationId }`, never a stack trace on the wire | `FAULT` | 500 |

**Changed from today's shipped mapping:** `NotAllowed` moves off the shared `LockedOut`/`NotAllowed → PermissionDenied` mapping onto `FAILED_PRECONDITION` — it's a state-precondition failure ("can't cancel an already-cancelled policy"), not an authorization failure, and now that `Forbidden` exists as a real, distinct arm, overloading `PermissionDenied` for both is no longer necessary.

**Structural retirement:** today's `OutcomeServerInterceptor` signals failure by catching a thrown `OutcomeFailedException`. That contradicts decided law item 3 ("nothing in-process throws-to-communicate") and is retired by this design. Handlers return `Outcome<T>` as data; the interceptor, the REST controller, and the in-process gateway all pattern-match the return value directly. No exception path for expected business failures, anywhere.

**Rejected alternative:** a brand-new union type in Svartalfheim per decided law item 7 as literally written, with `Outcome<T>` migrated onto it. Rejected because it contradicts Svartalfheim's own charter (explicitly disclaims application error categories as out of scope) and would force churn on a type that already ships and already works. See §7 for the formal appeal.

### 2.2 Gateway surface ergonomics

The generator produces three artifacts from one decorated service interface:

1. **`I{Context}Gateway`** — generated into the same `.Components` project as the service interface. Mirrors the service interface 1:1 by method name and parameters; wraps the return type in `Outcome<TResponse>`. No principal parameter — identity is ambient, resolved inside whichever implementation answers. `CancellationToken cancellationToken = default` on every method.
2. **Wire implementation** — generated into the WASM/MAUI host project (Yggdrasil's `Hosting.Web.Client`, later the MAUI client). Wraps the protobuf-net.Grpc client proxy; decodes `google.rpc.Status`/`ErrorInfo` back into `Problem`.
3. **In-process implementation** — generated wherever the standard behaviors (§2.5, Midgard) are a legal reference, which is a consequence of the platform's own dependency law ("only Yggdrasil may reference Midgard"), not an independent placement choice. Today that project is `Hosting.Web.Server`, because it is Yggdrasil's composition root, not merely because it's "the server host project" — the moment a second product stands up its own composition root, the in-process gateway is generated there instead, on the same rule. Runs the baked behavior chain against the circuit's principal, then calls the real service implementation directly. This also resolves §2.4's `InProcessHost` emission mode: it is precisely "wherever this project may reference Midgard," a compile-time-checkable condition, not an arbitrary label.

This is the mechanized shape of Heimdall's existing hand-written trio — the generator formalizes a pattern already proven by hand, it does not invent a new one. The Razor samples below use an illustrative `IReferenceDataGateway` purely as a generic, self-explanatory `I{Context}Gateway` stand-in — the actual acceptance target is Heimdall (§4), not Mimir.

**Before** (hypothetical direct injection — what this replaces):

```razor
@inject IReferenceDataService ReferenceData
@code {
    protected override async Task OnInitializedAsync()
    {
        try { countries = (await ReferenceData.ListCountries(new(), Token)).Countries; }
        catch (RpcException ex) { error = ex.Status.Detail; }   // WASM-only shape, no Server-circuit equivalent
    }
}
```

**After — read path:**

```razor
@inject IReferenceDataGateway ReferenceData
@code {
    Problem? problem;
    protected override async Task OnInitializedAsync()
    {
        var outcome = await ReferenceData.ListCountries(new(), Token);
        (countries, problem) = outcome is { IsSuccess: true }
            ? (outcome.Value!.Countries, null)
            : ([], outcome.Problem);
    }
}
@if (problem is not null) { <ErrorBanner Problem="problem" /> } else { <CountryList Items="countries" /> }
```

**After — write path**, merging server-side validation into the same Blazilla-managed `EditContext`:

```razor
<EditForm EditContext="editContext" OnSubmit="Submit">
    <FluentValidator Validator="validator" AsyncMode="true" />
    <ValidationSummary />
    <InputText @bind-Value="model.Name" />
</EditForm>
@code {
    async Task Submit()
    {
        if (!await editContext.ValidateAsync()) return;          // Blazilla's client-side pass

        var outcome = await Gateway.SaveThing(model.ToRequest(), Token);
        if (outcome is { IsSuccess: false, Problem.Category: ErrorCategory.Validation })
        {
            editContext.MergeServerErrors(outcome.Problem.Errors);  // new extension, see below
            return;
        }
        if (!outcome.IsSuccess) { /* Forbidden/Conflict/Fault -> shared ErrorBanner, not field-level */ }
    }
}
```

`EditContext.MergeServerErrors(...)` is a small new extension method (lives in `Abstractions.Components` — a Blazor-rendering concern, unlike the envelope types themselves) that pushes `Problem.Errors` into Blazilla's own `ValidationMessageStore`, matched by field name against the `EditContext`'s bound model. This only lands on the right field if the edit model's member names mirror the request DTO's member names exactly (`model.Name` ↔ `request.Name`) — a convention this document states as a requirement, not an assumption; a shape divergence between the two silently orphans a server-side error off any rendered field. Whether this is analyzer-enforced (flag a `ToRequest()`-style mapper whose source/target member names diverge) or left to code review is implementation-plan detail. One error-rendering path for both client-side and server-side validation failures, not two. Neither "Blazilla" nor an "ErrorContext" type exist anywhere in the platform prior to this document — Blazilla is a real third-party library (loresoft/Blazilla, FluentValidation + `EditContext` integration with `AsyncMode`/`ValidateAsync()` for async-rule timing); the merge extension is new law this document introduces, not a wrapper around a pre-existing "ErrorContext" concept.

**Deferred, fast-follow, explicitly out of scope here:** further abstracting the `outcome switch`/pattern-match boilerplate away from the typical component author (e.g. a `<GatewayView>` render-fragment helper). Design once the base gateway pattern is proven on Heimdall (§4) — inventing an ergonomics layer on top of an unproven primitive risks building the wrong abstraction.

### 2.3 Streaming stance

`IAsyncEnumerable<T>` service methods are a **build error** wherever the gateway generator processes the interface, v1. No streaming envelope is designed in this document. Rationale: no realm has a streaming use case today (Mimir/Heimdall are both request/response), and per-item vs. terminal vs. mid-stream failure semantics deserve their own spec once a real streaming consumer exists — designing that blind is exactly the kind of speculative extension point the platform's conventions reject.

### 2.4 Generator packaging & realm assignment

`Asgard/gen/Abstractions.Gateway.Generator` — named for what it emits (contracts, WASM host, and composition-root artifacts), not `Abstractions.Web.Server.Gateway.Generator` as an earlier draft of this document had it; none of its three outputs are a `Web.Server`-only concern, and that name would mislead the moment someone reads it, per the platform's own "naming is a deliberate act" convention. Its own analyzer NuGet package (`analyzers/dotnet/cs`, netstandard2.0), sibling to but separate from Asgard's runtime contracts packages. Mirrors Urdarbrunnr's just-shipped EF generator layout (`Urdarbrunnr/gen/Persistence.EntityFramework.Generator` and siblings) exactly — same `gen/` top-level folder, same per-generator `Directory.Build.props`, same "own package, not bundled with the runtime assembly" shape. `gen/Directory.Build.props` is already scattered to every NuGet-publishing realm via the platform config-scatter pipeline, so adding this generator to Asgard's `gen/` costs no new scatter-source work.

Any service-authoring realm `PackageReference`s this analyzer package directly. It produces no runtime surface of its own, so the "only Yggdrasil references Midgard" rule is never in tension with it — analyzer packages carry no runtime dependency graph the way a `ProjectReference`/`PackageReference` to a library does.

**Trigger mechanism** (which of the three artifacts in §2.2 the generator emits, and in which project) is implementation-plan detail, not spec-level: the generator determines its emission mode from project context (most likely an MSBuild/analyzer-config property set per project kind — `Contract` / `WireHost` / `InProcessHost`). Not fully worked out here; the implementation plan resolves the exact mechanism.

**Rejected alternative:** a dedicated `Norse.Generators` realm. Rejected as premature ceremony — this is the second cross-cutting generator on the platform (after Urdarbrunnr's EF generator), and the `gen/`-inside-the-owning-realm pattern already handles it cleanly. Revisit if a third, genuinely platform-wide generator with no natural realm owner shows up.

### 2.5 Behavior chain composition

Fixed order, outermost first: **`TelemetryBehavior` → `ExceptionTranslationBehavior` → `AuthorizationBehavior` → `ValidationBehavior` → handler.** Telemetry sits outside exception translation, not inside it: `ExceptionTranslationBehavior` returns `Outcome<T>` as data (never rethrows past itself), so `TelemetryBehavior` reads the finished `Problem` — including its `CorrelationId` — directly off the return value and tags its span/log entry with it. The reverse order (exception translation outermost, an earlier draft of this document) mints the correlation id *after* telemetry's own frame has already unwound past the throw, so telemetry could observe "this call failed" but never the id meant to correlate it. `TelemetryBehavior` itself is trusted not to throw — it is not further wrapped; that's an implementation/test obligation on that one behavior, not another layer of translation. Exception translation still wraps everything downstream of it (authz, validation, the handler) so nothing from those layers escapes unconverted. Authorization runs before validation so a caller who can't touch the resource never learns *why* a malformed request would have failed.

Standard behaviors live in Midgard, as concrete `Norse.Infrastructure.*` implementations of an Asgard-declared `IBehavior<TRequest, TResponse>` contract. **Scoping note:** the "handler" step here is the concrete service implementation method (`{Context}Service : I{Context}Api`), not a dispatch through the still-unresolved generic `IRequestHandler<,>` sender — Asgard's own code comment already flags that no generic dispatcher exists yet ("nothing in the platform dispatches through a generic sender yet ... revisit once a real generic dispatcher exists"). This design does not take a dependency on that unresolved question; the behavior chain wraps whatever method actually implements the service interface today.

`AuthorizationBehavior` lifts the method's `[Authorize(Policy=...)]` attribute (analyzer-enforced: every Asgard-contracted service method must carry one) and evaluates through `IAuthorizationService` against whichever principal the host adapter supplies — the circuit's `AuthenticationStateProvider` principal in-process, the ASP.NET request principal on the gRPC/REST endpoints.

**Extension seam:** a `{Company}.{Context}` product realm adds its own behavior via `[Behavior(typeof(MyBehavior), After = typeof(ValidationBehavior))]` on the **service implementation class**, not the service interface — the interface lives in the `.Components` project that ships to the browser, and `typeof(MyBehavior)` on it would force that WASM-shipped assembly to reference the behavior's (server-side) implementation assembly, exactly the boundary `.Components`-never-references-server-types exists to prevent. Only the in-process gateway generator needs to see this attribute — it compiles where the implementation is visible, and the wire gateway needs no behavior knowledge at all, since behaviors already ran server-side before the wire response was ever produced. The generator reads the attribute at the consumer's own compile time and bakes it directly into that consumer's generated in-process chain — no runtime `IEnumerable<IBehavior>` resolution, no assembly scanning, fully static per the manifesto's compile-time bias.

**Rejected alternative:** a fixed, non-extensible generated chain with custom behavior applied via a hand-written DI decorator wrapping the generated gateway. Rejected because it produces two composition mechanisms side by side (generator-baked standard chain plus a hand-written decorator for anything custom) instead of one coherent extension point.

### 2.6 Exception → envelope translation table

One shared table, invoked identically by the gRPC interceptor and the in-process gateway. Lives in Midgard, alongside the standard behaviors.

- **Never caught:** `OperationCanceledException` where the token is the caller's own supplied token — respected as cooperative cancellation, propagated as-is, the channel's native cancellation handling takes over. Exceptions during DI construction/startup never reach the per-request chain at all.
- **Everything else unmapped → `Fault`.** Fresh `Guid` correlation id; full exception (type, message, stack trace) logged server-side at `Error`; generic message ("An unexpected error occurred") on the wire. "Fail loudly" per the manifesto means logged loudly server-side with full detail and a traceable correlation id — never that the caller sees internals.
- **Applies uniformly across every channel, including the Server circuit.** An unhandled exception in-process must degrade to a rendered `Fault` envelope, not crash or reload the circuit — otherwise error parity (a hard requirement per the render-mode policy) breaks the instant WASM hydrates and the same failure now looks different to the same user in the same session.
- **Explicitly out of scope for this pass:** mapping EF/database exceptions (e.g. unique-constraint violations) to `Conflict`. Midgard's repository pattern hasn't converged yet — inventing a mapping for infrastructure that doesn't exist would be speculative. Flagged as a named follow-up, not silently skipped.

---

## 3. Resulting component model

A component injects `I{Context}Gateway` only, never the raw service interface. DI binds the wire or in-process implementation per host — the component has no way to detect which one answered, by construction, because both return the identical `Outcome<TResponse>`.

Auto-mode hydration (decided law item 6) persists the *whole* `Outcome<T>` — success or failure — via `PersistentComponentState`, so a failure discovered during prerender doesn't flash to a loading spinner when WASM takes over; it re-renders the same `Problem` instantly. `PersistentComponentState` has zero precedent in this codebase today — this is genuinely new wiring, not an extension of something proven, and the implementation plan should treat it as such (its own task, its own test).

---

## 4. Acceptance proof — Heimdall retrofit

Heimdall, not Mimir, is the proving ground. Mimir is a bare shell today (no service contract, no components, nothing to retrofit); Heimdall already has live components and a live hand-written gateway trio to mechanize, which is a stronger proof than building from nothing.

Heimdall's existing hand-written `IAuthenticationGateway` / `BlazorServerAuthenticationGateway` / `WasmAuthenticationGateway` trio is replaced by the generated equivalent. This is proven out live and incrementally, as components migrate from Himinbjorg to Heimdall during the ongoing Identity refactor — each migrated component is wired through the new generated gateway as it lands, not a single big-bang cutover.

**Hydration-parity test:** render a real Heimdall component (e.g. `Login.razor`) through the in-process gateway during prerender, force a real failure (`LockedOut` or `Forbidden`), and assert the rendered `Problem` is identical in shape after WASM hydration takes over and re-answers via the wire gateway. Repeat for the success path. This is the test that proves parity is real, not asserted.

---

## 5. Out of scope / deferred

- Streaming envelope (§2.3).
- Gateway call-site ergonomics beyond the raw `outcome switch` pattern (§2.2 fast-follow) — a future spec once the base pattern is proven.
- EF/database exception → `Conflict` mapping (§2.6) — pending Midgard's repository pattern convergence.
- The exact generator trigger/mode-selection mechanism (§2.4) — implementation-plan detail.
- REST bridge implementation detail beyond the mapping table in §2.1 — `GrpcControllerBase` retrofit mechanics (decided law item 5) are a plan-level concern, not re-litigated here.

---

## 6. Realm placement changes to existing shipped code

This document moves and changes behavior of code that already ships. Called out explicitly, not buried in the verdicts above:

1. **`Outcome`, `Outcome<T>`, `Problem`, `ErrorCategory`, `BoolResponse`** move from `Asgard/src/Abstractions.Web.Server/Mediator/` to `Asgard/src/Abstractions.Contracts/` (§2.1).
2. **`ErrorCategory`** gains three members: `Unauthorized=7`, `Forbidden=8`, `Fault=9` (§2.1).
3. **`Problem`** gains `Guid? CorrelationId` (§2.1).
4. **`NotAllowed`'s gRPC status mapping changes** from the shared `PERMISSION_DENIED` (with `LockedOut`) to its own `FAILED_PRECONDITION` (§2.1) — a real behavior change for any existing consumer pattern-matching on gRPC status code rather than `ErrorCategory`.
5. **`OutcomeServerInterceptor`'s throw-based signaling (`OutcomeFailedException`) is retired.** Replaced by direct pattern-matching on the `Outcome<T>` return value at every channel boundary (§2.1).

---

## 7. Appeals

**Decided law item 7's Svartalfheim placement is appealed.** The brief places "envelope + error-arm types in Svartalfheim." Two independent facts contradict this:

1. **Svartalfheim's own CLAUDE.md explicitly disclaims the responsibility** — "application error categories (validation/not-found/conflict) belong to the mediator, transport conditions to the host pipeline," not the forge.
2. **The equivalent type already ships, in Asgard, and is already consumed** by Midgard's `OutcomeServerInterceptor`. Building a new union in Svartalfheim and migrating `Outcome<T>`'s callers onto it would be pure churn against a charter violation, not a genuine design need.

This document proceeds on the appealed position (§2.1: envelope stays in Asgard, relocates only from `Abstractions.Web.Server` to `Abstractions.Contracts` within Asgard) rather than the literal text of item 7. Flagged here per the brief's own instruction to record disagreement rather than silently redesign around it.

---

## 8. References

- `Heimdall/specs/2026-07-13-authn-identity-split-design.md` §9.3 — anti-enumeration principle, governs `NotFound`/`Unauthorized` usage discretion.
- `Platform/specs/2026-07-13-protobuf-net-grpc-reinstated-design.md` — RPC stack, `[ServiceContract]`/`[OperationContract]` decoration, no `.proto`, no `CallContext` on shared interfaces.
- `Platform/specs/2026-06-28-migrations-framework-identity-schema-design.md` — the realm-by-realm ship-gate execution model this design's implementation plan follows.
- `Platform/specs/2026-07-11-blazor-component-architecture-design.md` — headless/`.FluentUI` component split this design's gateways are injected into.
- `Platform/specs/2026-07-01-norseref-generator-forwarding-design.md` — prior art for generator-produced forwarders and analyzer packaging under CPM.
- GrpcControllerBase pattern — https://github.com/protobuf-net/protobuf-net.Grpc/issues/264#issuecomment-1336253645

---

## 9. Amendment — 2026-07-24 (dated, post-implementation-start)

**Trigger:** while scoping Himinbjörg's `AuthenticationService` (the concrete gRPC-hosted `IAuthenticationService` implementation), the realm-boundary consequence of decided law item 3 as originally written surfaced for real: a class whose entire job is "translate `Outcome<T>` to wire failure" cannot be Himinbjörg-resident without either (a) Himinbjörg naming `RpcException`/`Grpc.*`/Midgard types directly (a peer-realm dependency violation — Himinbjörg and Midgard are architectural peers, neither rides on the other), or (b) relocating the class to a realm that legally depends on both (Yggdrasil). Both were explored live; the architect ruled a third path instead: **reverse the wire half of decided law item 3**, keeping the throw-based interceptor pattern this design originally retired.

**(a) Decided law item 3, reversed in part.** Original text: *"Service interfaces keep returning plain `TResponse` — the envelope is the internal error model, never the wire method signature. Nothing in-process throws to communicate."* The **first clause is reversed**: Asgard-contracted service interfaces (`IAuthenticationService` and every future one) now return `Task<Outcome<TPayload>>` directly — the envelope **is** the wire method signature. The **second clause is unchanged and remains load-bearing**: nothing in-process throws to communicate — that guarantee now holds one layer further out than originally scoped. In-process (the Blazor Server circuit path, the in-process gateway, every handler) still never throws for an expected business failure; `Outcome<T>` flows as data end to end. The one place that throws is the gRPC server interceptor — a transport adapter at the wire boundary, not "in-process" in the sense law 3 was protecting.

**(b) `OutcomeServerInterceptor`'s retirement (§6 item 5, §2.1 "Structural retirement") is reinstated as the lawful design.** The retirement was correct under the original law — a service interface returning plain `TResponse` genuinely had no data channel for failure, so a thrown `OutcomeFailedException` was the only signal available, which the interceptor had to catch. Under the amended law, the interceptor's input is no longer an exception to catch — it's the `Outcome<T>` return value itself, pattern-matched directly (`Failed(problem)` → throw `problem.ToRpcException()`; anything else → pass through unchanged). The *shape* of "interceptor is the wire-boundary throw point" is unchanged from the original design; only its trigger moves from catch-a-thrown-exception to pattern-match-the-return-value, consistent with every other channel boundary in this document (§2.1's "the interceptor, the REST controller, and the in-process gateway all pattern-match the return value directly" — the interceptor was always meant to pattern-match; §6 item 5's phrasing described the pre-existing shipped code being replaced, not the new interceptor's own mechanism, and should not have been read as retiring the interceptor's *existence* as a throw point).

**Detection mechanism (mechanical, recorded for implementers):** the interceptor is generic over `TResponse` and cannot statically know whether a given response is some `Outcome<T>` without knowing `T`. `Failed` (the failure case) is deliberately non-generic (§2.1: `public readonly record struct Failed(Problem Problem)`), and `IUnion.Value` (Svartálfheim, `Norse.Primitives`) is the type-erased escape hatch every union case type carries for exactly this situation. Detection is `response is IUnion { Value: Failed failed }` — no reflection, no knowledge of `T`, correct for any `Outcome<T>` regardless of payload type. A response that isn't an `IUnion` at all, or is a `Success<T>` case, passes through unchanged.

**Known, accepted consequence — recorded so it is chosen, not discovered:** a successful gRPC response now serializes the envelope's success arm (`Outcome<T>`'s `Success<T>` case), not a bare `TResponse` — the wire payload is partner-visible union structure, not the plain type the original design intended. This is a real, deliberate trade against decided law item 3's original "never the wire method signature" framing, made because the alternative (Himinbjörg depending on Midgard, or relocating a thin adapter across a realm boundary for one class) was judged worse. Protobuf-net must be configured to serialize `Outcome<T>`'s success arm without Asgard itself carrying any serialization attributes — a Midgard-side runtime type-model surrogate registration, consumed by Yggdrasil's composition root, per the same "wire concerns are mortal, Asgard carries none of them" principle §2.1 already establishes for the failure arm's `google.rpc.Status`/`ErrorInfo` encoding. The future REST bridge is not harmed by this — it maps `Outcome<T>`'s arms directly, which is simpler than unwrapping would have been.

**§6 (realm placement changes to existing shipped code) item 5 is superseded by this amendment** — read "retired" there as "retired, then reinstated in reversed form, 2026-07-24" and follow §9, not §6 item 5's original text, for implementation.

**Not reopened by this amendment:** decided law items 1, 2, 4, 5, 6, 7 (§1.1) and every other verdict in §2 stand as written. This amendment is scoped exactly to law item 3's wire-signature clause and the interceptor design it drove.

**(c) §9(a)/(b)'s "known, accepted consequence" (the paragraph above ending "...simpler than unwrapping would have been") is corrected, not superseded — the trade it described was never actually paid.** That paragraph was written against the *first* draft of the wire mechanism, before the two structural CLR walls below were discovered and resolved. The resolution changes what the wire actually carries:

- **Wall 1 (marshaller rejection):** protobuf-net's default binder cannot serialize `Outcome<T>` at all — `CanSerialize` rejects it outright. Fixed with a protobuf-net `RuntimeTypeModel` surrogate per response type (`model.Add(typeof(Outcome<TPayload>), applyDefaultBehaviour: false).SetSurrogate(typeof(TPayload))`), generator-emitted into Yggdrasil's composition roots only (never hand-written in a service realm — Heimdall and Himinbjörg stay dumb to transport, per §2.1's existing "wire concerns are mortal" principle). Empirically verified byte-identical: an `Outcome<T>.Ok(payload)` serializes to the exact same bytes as a bare `payload` — **the wire payload is the plain `TResponse`, not the envelope's `Success<T>` arm.** §9(a)/(b)'s predicted consequence does not occur; the wire signature stays exactly what decided law item 3 originally intended, even though the C# method signature now returns `Task<Outcome<TPayload>>`.
- **Wall 2 (CLR generic constraint):** `where TResponse : class` is a hard constraint on both `Grpc.Core.Interceptors.Interceptor.UnaryServerHandler<TRequest,TResponse>` and `ProtoBuf.Grpc.Internal.Reshape.UnaryTaskAsync<TRequest,TResponse>` (the client-proxy invocation path) — confirmed via direct compilation (CS0452) and a runtime constraint-violation exception respectively. No serialization mechanism can fix a CLR generic-constraint violation; it operates on a different layer entirely. Fixed by making `Outcome<T>` a `sealed class` instead of a `readonly record struct` — grounded in a companion doctrine ("The Two Unions"): `Result<T>` (Svartálfheim, boundary-facing, tight parsing loops, allocation is the workload) stays a struct; `Outcome<T>` (Asgard, interior-facing, one allocation per operation adjacent to I/O is silence) becomes a class. The `[Union]`/`IUnion` native-union compiler feature requires a public `Value` property and a public constructor regardless of struct-vs-class (CS9385/CS9386, verified by direct compilation) — matching Svartálfheim's `Result<T>` precedent exactly, not a widened API surface.
- **Corollary — protobuf-net's reference-type surrogate contract:** now that `Outcome<T>` is a class, protobuf-net's deserializer round-trips a default/no-existing-value merge target through the surrogate's conversion operators before populating it, per protobuf-net's own documented convention (`SurrogateForObjectUsage.cs`) that both directions null-check and pass `null` through unchanged. `Outcome<T>`'s implicit lift and explicit unwrap operators do this (`Asgard/src/Abstractions.Contracts/Outcome{T}.cs`) — a wire-only scaffolding path; real application code can never hit it, since `T` stays `notnull`.

All three points proven end-to-end via a real hosted `TestServer` + `GrpcChannel` + `ClientFactory.Create(binderConfiguration)` round trip (not isolated unit tests) — the same empirical standard this amendment's own discovery process used throughout.
