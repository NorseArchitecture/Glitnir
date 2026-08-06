# Blazor Validation Composition — DI Wireup, Generated Discovery, Server-Rejection Rendering, and the Pit-of-Success Submit Seam

**Date:** 2026-08-06
**Status:** Design — approved in session; amended same day after third-party review (lifecycle coordinator §3.1, per-host discovery table §7, category display policy §3.4, async-rule semantics §6.1, `SubmitAsync` semantics §5, setter contract §12; the reviewer's `LoginResult.Succeeded` compatibility window was declined in favor of the §4 topology assumption)
**Owner:** Buvy
**Issue:** [Heimdall #39](https://github.com/NorseArchitecture/Heimdall/issues/39)
**Supersedes in part:** `2026-07-16-blazilla-validation-composition-poc.md` — the §2.1 spike this doc replaces with a decided design (the spike never ran; the decompilation evidence in that doc's §1.2 remains the ground truth this design builds on). Its §2.3/§2.4 wire-shape rulings are adopted here unchanged.
**Companion:** a separate visual/layout design note for `Login.razor`/`Register.razor` (same session, different concern) — this doc is mechanism only.

---

## 1. Decisions Made in This Session

1. **Two specs, one session.** This doc is the issue #39 mechanism spec; visual/layout findings from the live Bifröst run land in their own note. Mechanism and aesthetics do not share a blast radius.
2. **No sandbox spike.** The 07-16 doc's §2.1 extension-vs-wrapper question is decided by reasoning here (§3); the decompilation already closed the only hard unknown (no Blazilla injection hook — second-store is the sanctioned mechanism). The retrofit itself is the proof.
3. **`<ValidationSummary />` dies; field errors render inline only.** The live run showed every failure rendered twice (summary bullet + FluentUI field slot). Inline wins.
4. **Model-level errors get an in-form message bar**, not a toast, not a filtered summary list (§3.2).
5. **Async validators are in scope now** — the 07-16 doc's §2.3 (`MustAsync` email-exists) lands for real on Register; §2.4 (`CustomAsync` lookup-with-write-back) is proven at test level only (§6).
6. **Failed login becomes `Failed(Problem)`** — the `Success<LoginResult>({ Succeeded: false })` anti-enumeration special case is retired at the source (§4).
7. **The error story moves behind a component-base seam** (`SubmitAsync`) so page code physically cannot forget it (§5). Hand-rolled, no generator — generation re-enters only if per-service typed helpers are wanted later (N=2 rule).

## 2. Realm Placement and Promotion Discipline

| Piece | Round-one home | Promotion trigger |
|---|---|---|
| `ApplyServerErrors` extension (§3.1) | Heimdall `AuthN.Components` | Next component (change-password or forgot-password) lands on it with materially less code — then candidate for the Æsir, per the 07-16 doc's own `ApplyServerErrors`-belongs-beside-`Outcome<T>` hypothesis, confirmed or refuted then, not now |
| `<ModelValidationSummary />` (§3.2) | Heimdall `AuthN.Components.FluentUI` | Same gate; a vendor-neutral core + FluentUI skin split is evaluated at promotion, not before |
| `OutcomeFormComponentBase` (§5) | Heimdall `AuthN.Components` | Promotes together with `ApplyServerErrors` (it calls it); lands beside `AsyncComponentBase` in Asgard `Abstractions.Components` if confirmed |
| Client discovery emitter (§7) | Midgard `gen/Infrastructure.Web.Client.Generator` (existing project) | None — hosts are its consumers; it cannot be Heimdall-local |
| `EmailExists` contract + validator rule (§6) | Heimdall `AuthN.Services` / `AuthN.Components` | n/a — lives where the AuthN wire contract lives |
| `LoginResult` reshape + handler change (§4) | Heimdall `AuthN.Services`, Himinbjörg `Identity.Web.Server` | n/a — ship-gate ordering in §9 |

Asgard is touched in round one only if `Problem` needs the `ModelError` factory (§3.3) — a single additive member on an existing record, no new law.

## 3. Server-Rejection Rendering — the Split Mechanism

The 07-16 doc weighed an extension method against a wrapper component. Neither alone covers both jobs — an extension method cannot render markup, and a wrapper exposing `ApplyAsync` via `@ref` regrows the per-page field-plus-`@ref` boilerplate this issue exists to delete. The design splits along the natural seam: **applying** errors is an `EditContext` operation; **displaying** model-level errors is a render concern. The two compose through the `EditContext` and never reference each other.

### 3.1 `ApplyServerErrors`

```csharp
public static void ApplyServerErrors(this EditContext editContext, Problem problem)
```

- Accepts Asgard's `Problem` directly — the platform error shape end to end. No raw-dictionary overload; a second door to the same room invites drift.
- On first apply, creates a small **coordinator** cached in `EditContext.Properties` under a library-owned key — reused for the context's lifetime. Same stash-on-Properties trick Blazilla itself uses for its pending async task; multiple independent stores per `EditContext` is the sanctioned Blazor model (07-16 doc §1.2). The coordinator owns:
	- one `ValidationMessageStore` for server-produced messages;
	- an `OnFieldChanged` subscription clearing that field's server messages the moment the user edits it;
	- an `OnValidationRequested` subscription clearing **all** server messages before any fresh validation pass.
- Each apply clears the store, populates field-keyed messages against `new FieldIdentifier(editContext.Model, field)` and empty-key messages against the model-level identifier, then calls `NotifyValidationStateChanged()`.
- An empty `Errors` dictionary on a `Failed` outcome renders the category's display text as a model-level message (§3.4) — a failure may never be invisible (§8). Clearing is a distinct, explicit act — `ClearServerErrors()` on the same extension surface — used by §5's success arm; an applied `Problem` always renders something, so the two operations cannot be conflated.

**Why the subscriptions are load-bearing, not polish:** Blazor aggregates validation messages across every store on the `EditContext`. A server store that only clears on the next apply deadlocks resubmission — the stale message makes `Validate()`/`ValidateAsync()` report invalid, the valid-submit handler (the only place the clear lived) never runs, and the form is permanently stuck until page refresh. **The hand-rolled `Login.razor`/`Register.razor` carry exactly this bug today** — a user who fails one login cannot retry without reloading. `OnValidationRequested` is the same hook the official Blazor `CustomValidation` sample uses for the same reason. The retrofit therefore fixes a live defect, not just boilerplate.

### 3.2 `<ModelValidationSummary />`

Placed inside the `EditForm`, consumes the cascaded `EditContext`, renders **only** empty-key messages (`GetValidationMessages(new FieldIdentifier(Model, string.Empty))`) in a FluentUI MessageBar with error intent. Field-keyed messages stay in FluentUI's own field slots. Because it reads off the `EditContext`, it renders model-level errors regardless of which store produced them — Blazilla's, ours, anyone's. Re-renders on `OnValidationStateChanged`; unsubscribes on dispose.

`<ValidationSummary />` is deleted from every page in the same change.

### 3.3 `Problem.ModelError`

A static factory on `Problem` (Asgard, additive):

```csharp
public static Problem ModelError(ErrorCategory category, string message)
```

producing an empty-key single-message `Errors` dictionary. Exists so call sites (and §4's handler) never hand-build `new Dictionary<string, string[]> { [""] = [...] }` literals. If review finds an equivalent already on `Problem`, this member evaporates.

### 3.4 Category Display Policy

"Fallback text" is a defined mapping, not an improvisation. The rendering seam owns **one** spec'd category→display table (Heimdall-local beside `ApplyServerErrors`, promoted with it) used only when a `Failed` arrives with an empty `Errors` dictionary — a populated dictionary always wins verbatim:

| `ErrorCategory` | Display |
|---|---|
| `Validation` / `Conflict` / `NotFound` / `LockedOut` / `InvalidCredentials` / `NotAllowed` / `Unauthorized` / `Forbidden` | Safe generic sentence per category, spec'd as constants — never derived from the enum member name |
| `Fault` | Generic "something went wrong" sentence **plus the `CorrelationId`** rendered for support reference — `Fault` is the one category that carries one, per `Problem`'s own contract |
| `Unspecified` / `Erased` | Treated as `Fault`-grade display (generic sentence; correlation ID if present) — reaching the form seam with these is a producer defect, surfaced visibly, never hidden |

This table maps the existing vocabulary to display strings and nothing more — it is not a second error vocabulary, and producers remain encouraged to populate model-level errors themselves. Localization is a recorded deferral: the platform has no localization story yet and US English is house law; when one lands, this table is its single seam.

## 4. Failed Login Is a Failure

Today `LoginHandler` collapses wrong-user/wrong-password into `Success<LoginResult>` with `Succeeded = false`, and `Login.razor` carries a dedicated switch arm for it. That inversion — a failure dressed as success — is retired:

- Himinbjörg's `LoginHandler` returns `Failed(Problem.ModelError(ErrorCategory.InvalidCredentials, "Invalid email or password."))` for the anti-enumeration collapse. `ErrorCategory.InvalidCredentials` already exists (= 5) and its own XML doc names this exact family. **The anti-enumeration property is preserved**: wrong username and wrong password produce byte-identical `Problem`s.
- `LoginResult.Succeeded` becomes wire-dead and is deleted; `LoginResult` slims to `DeferredCompletionUrl`. `LockedOut`/`NotAllowed`/etc. were already `Failed` categories and are unaffected.
- Component consequence: the success arm navigates, the failed arm applies — no third state. This is what closes §5's seam over the `Outcome` domain.
- **Topology assumption (recorded deliberately):** `Hosting.Web.Server` serves its own WASM client — one deployable, no independent client versioning, no production consumers. That is why `Succeeded` is deleted outright instead of riding an `[Obsolete]` compatibility window. Both skew directions were examined and are benign regardless (old client + new server reads the absent member as protobuf default `false` and fails closed; new client + old server navigates without a cookie and lands unauthenticated), but the real protection is the topology. Revisit if the client ever deploys independently of the server that feeds it.

## 5. The Pit-of-Success Seam — `OutcomeFormComponentBase`

A Heimdall-local base extending `AsyncComponentBase`:

```csharp
protected async Task SubmitAsync<T>(
	EditContext editContext,
	Func<CancellationToken, Task<Outcome<T>>> call,
	Action<T> onSuccess)
```

`Failed` → `editContext.ApplyServerErrors(problem)`, `Success` → clear prior server errors, then `onSuccess(value)`. The error story is not the page's to get wrong — a page that uses `SubmitAsync` cannot forget rejection rendering, and a page that needs bespoke handling still has `ApplyServerErrors` underneath (the seam wraps the mechanism; it does not replace it). An async-continuation overload (`Func<T, Task>`) ships alongside for navigation-after-await cases.

Scope of the claim, stated precisely: **the seam is total over the `Outcome` domain, not over all failure.** Defined semantics:

- **Exceptions propagate.** The transport seam already folds expected failure into `Outcome` (`OutcomeClientInterceptor` decodes `RpcException` → `Failed(Problem)` on the wire path; the server pipeline maps unhandled handler faults). Anything that still throws through `SubmitAsync` — including a throwing success continuation — is a genuine defect and propagates to the circuit's `ErrorBoundary`/`LoggingCircuitHandler` safety net (shipped structurally 2026-07-27). Deliberately never caught here: swallowing it would be the silent fallback the platform forbids.
- **Overlap guard.** `SubmitAsync` sets a protected `IsSubmitting` flag for its duration; a call arriving while one is in flight returns without dispatching. Pages bind the flag to the submit button's disabled state — the pit of success and the UX affordance are the same member.
- **Success clears.** Prior server errors are cleared on the success arm before the continuation runs, so a continuation that doesn't navigate cannot strand stale rejections in the form.
- **Cancellation.** The call receives `AsyncComponentBase`'s existing `CancellationToken`; cancellation on teardown follows the base's established semantics, not new ones invented here.

Deliberately **not** named for gRPC: the base never knows a transport exists — `IAuthenticationService` resolves to Himinbjörg's in-process implementation on Blazor Server and the generated gRPC-Web proxy on WASM (the existing DI substitution). Naming the role, not the mechanism, per house law.

Pages also shed their `EditContext`/`ValidationMessageStore` fields and `OnInitialized` wiring entirely: `EditForm` gets `Model="_request"` back, and the submit callbacks (`OnValidSubmit`/`OnSubmit`) already deliver the `EditContext` as their argument. `Login.razor`'s `@code` block after the retrofit:

```csharp
readonly LoginRequest _request = new() { Email = "", Password = "" };

Task HandleLoginAsync(EditContext editContext) =>
	SubmitAsync(editContext, ct => AuthenticationService.Login(_request, ct),
		result => Navigation.NavigateTo(result.DeferredCompletionUrl ?? "/", forceLoad: true));
```

## 6. Async Validation

### 6.1 `MustAsync` email-exists (lands for real)

- `IAuthenticationService` gains `Task<Outcome<BoolResponse>> EmailExists(EmailExistsRequest request, CancellationToken cancellationToken)` — one `[DataContract]` wire record carrying the email string, riding the uniform Mediator → `Outcome<T>` → gRPC pipeline wrapping Asgard's existing `BoolResponse`, exactly as the 07-16 doc ruled (one door table, no bespoke status-only route).
- `RegisterRequestValidator` gains the `MustAsync` rule, injecting `IAuthenticationService` itself — transport-dark by the same DI substitution as the pages. A `Failed` outcome from the check itself surfaces as a field-level "could not verify" error and blocks submit — fail loud, never swallow-and-pass (§8) — and a `Fault`'s `CorrelationId` is logged at the validator seam before the message collapses to the field, never discarded.
- **Blur-gated, never per-keystroke (amended 2026-08-06, second review pass — supersedes the original "submit-gated" ruling):** rule-set gating was found unbuildable by decompilation — Blazilla 2.4.0's field-change pass constructs a bare `MemberNameValidatorSelector` (early-returned before its `Selector`/`RuleSets` parameters are consulted), and FluentValidation 12.1.1's `MemberNameValidatorSelector.CanExecute` carries no rule-set guard (that gate lives only in `DefaultValidatorSelector`/`RulesetValidatorSelector`), so a rule-set rule targeting a field fires on that field's every change event regardless of configuration. The async rule therefore lives in the **default** rule set, chained `Cascade(CascadeMode.Stop)` behind the sync shape rules so malformed input never reaches the service, and runs when the email field's change event fires — which is **blur**, not keystroke, because `FluentTextInput` binds on the change event. Blur-time exists-checking is the standard UX for this class of validation; the requirement that binds is no network call per keystroke, and it holds by binding semantics, asserted by component test.
- `Register.razor` switches to `<FluentValidator AsyncMode>`, `OnSubmit` (not `OnValidSubmit`), and `await editContext.ValidateAsync()` before dispatch, per Blazilla's documented async pattern.
- Anti-enumeration: Register's `Conflict` response already discloses account existence; this check adds no new oracle. Login remains collapsed per §4.
- Server side runs the identical rule through the generated `CommandRequestValidator` adapter **unmodified** — with the rule in the default set, the adapter's plain `ValidateAsync` executes it, which is what makes "single source of validation truth, run twice" a true statement (an Asgard adapter test asserts async rules execute through it). And there the check is a **nested `ISender.Send`**: Himinbjörg's `AuthenticationService(ISender)` dispatches `EmailExistsCommand` while `RegisterCommand`'s own `ValidationBehavior` is mid-pipeline. This is legal by construction — behaviors and handlers are stateless scoped DI citizens and a nested send is plain method calls in the same scope — but it is asserted by an integration test (§10), not assumed. Cancellation propagates natively through `MustAsync`'s `CancellationToken` parameter. Single source of validation truth, run twice, now including the async rule.
- **The atomic user-creation conflict remains the authority.** `EmailExists` is inherently race-prone (check-then-act); it exists as UX sugar, and `RegisterHandler`'s `UserManager` create keeps the atomic `Conflict` as the source of truth. The design must never treat a passing `EmailExists` as a guarantee.

### 6.2 `CustomAsync` lookup-with-write-back (proven at test level)

The zip→city/state pattern from the 07-16 doc §2.4 is proven by test — a throwaway validator against an NSubstitute-substituted lookup service, asserting model write-back visibility after `ValidateAsync()` and clean composition beside a `MustAsync` rule in the same validator. No page ships; the address component remains the issue's recorded non-goal.

## 7. Source-Generated Client Discovery

A new discovery + emitter pair in Midgard, following the compiled-symbol-walk pattern Asgard's `HandlerRegistrationGenerator` proved (own + referenced assemblies; PackageReference-mode safe). Placement refinement found at planning: the discovery and emission logic live in `gen/Infrastructure.Web.Grpc.Generator.Shared` (the existing shared generator library) with **two thin generator heads** — one in `Infrastructure.Web.Client.Generator` (WASM host emission), one in `Infrastructure.Web.Server.Generator` (server host emission) — because each host only references its own side's generator package; a single client-side emitter could never reach the server host's compilation. Discovery is per-compilation, which is what makes the per-host divergence stop being hand-maintained knowledge: the server compilation references Himinbjörg and therefore discovers `ExternalLogin`'s assembly; the WASM compilation doesn't and therefore doesn't — today that difference lives in two humans' heads and three hand lists.

**Hand lists being retired (verified against current source):** `Hosting.Web.Client/Program.cs` carries two (validator registrations, `RoutesAdditionalAssemblies`); `Hosting.Web.Server/Program.cs` carries two more (`RoutesAdditionalAssemblies` at line 42 — richer than the client's — and `.AddAdditionalAssemblies(...)` on `MapRazorComponents` at line 125). Router discovery and Razor endpoint discovery are **distinct mechanisms**; missing the latter lets routing recognize a component whose render-mode infrastructure was never registered.

Exact emission per host:

| Host | Generated | Replaces |
|---|---|---|
| WASM (`Hosting.Web.Client`) | `AddScoped<IValidator<T>, TImpl>` per discovered validator + `RoutesAdditionalAssemblies` singleton from discovered `RouteAttribute`-bearing assemblies | Both hand lists in its `Program.cs` |
| Server (`Hosting.Web.Server`) | `RoutesAdditionalAssemblies` singleton + an extension on `RazorComponentsEndpointConventionBuilder` (working name `AddNorseComponentAssemblies()`) feeding the same discovered assembly set to endpoint discovery | Both hand lists in its `Program.cs`. Validator registration stays with `AddNorse{Realm}Handlers()` — already generated, already DI-wide, already what the server circuit's Blazilla resolves |

`RoutesAdditionalAssemblies` and the endpoint builder types are resolved by metadata name at generation time; the generated source compiles in the host's own compilation, so Midgard takes no Yggdrasil dependency.

**Idempotency guard:** all validator registrations — both generators — go through `TryAddEnumerable` semantics. A validator registered twice runs twice under `CommandRequestValidator` and doubles every message; that failure mode is killed structurally and locked by test (§10).

**Exit criterion honored:** adding a validator/component assembly is adding the project reference — zero registration edits in either host, proven by adding one (§11).

## 8. Error-Handling Invariants

1. **No invisible failure.** Every `Failed` renders something: field-keyed → inline slots; empty-key → `<ModelValidationSummary />`; empty `Errors` dictionary → the §3.4 category display table, as a model-level message.
2. **No silent async pass.** An `EmailExists` transport failure blocks submit with a visible field error; validation never defaults to "valid" because a dependency died.
3. **No doubled messages.** Structural (§7 idempotency) plus the summary deletion (§1 decision 3) — one failure, one rendering, one place.
4. **Anti-enumeration preserved.** §4's collapse produces identical bytes for both credential failures; §6.1 adds no oracle Register didn't already have.

## 9. Ship-Gate Ordering

Cross-realm dependency order, each behind its own gate (PR merged, CI green, tagged, published):

1. **Asgard** — `Problem.ModelError` (skipped if an equivalent exists).
2. **Midgard** — discovery emitter in `Infrastructure.Web.Client.Generator`.
3. **Heimdall** — `ApplyServerErrors`, `<ModelValidationSummary />`, `OutcomeFormComponentBase`, `EmailExists` contract + validator rule, `LoginResult` reshape, Login/Register retrofit.
4. **Himinbjörg** — `LoginHandler` reshape (§4), `EmailExistsHandler`; floats on Heimdall's `Norse.AuthN.Services` `Version="*"`, so Heimdall's package must publish first.
5. **Yggdrasil** — both hosts drop all four hand lists (§7's table) for the generated registrations; Playwright smoke on the WASM path.

## 10. Testing

TDD throughout (`../../../CLAUDE.md` §2.8 — subagent-orchestrated, test-first, both, every time):

- `ApplyServerErrors`: unit tests straight against `EditContext` — coordinator created once and cached, clear-on-reapply, field vs empty-key routing, notify raised, empty-dictionary category display (§3.4 table), `OnFieldChanged` clears only that field's server messages, `OnValidationRequested` clears all.
- **Edit-and-resubmit lifecycle (the §3.1 deadlock lock), four steps asserted in one test:** (1) server field rejection applied; (2) user edits the field — its server message clears; (3) a fresh client validation pass succeeds; (4) the second server call actually dispatches. Run for both a field-keyed and a model-level rejection.
- `<ModelValidationSummary />`: bUnit — renders empty-key messages only, nothing when only field errors exist, re-renders on validation-state change, unsubscribes on dispose; `Fault` rendering includes the correlation ID.
- `OutcomeFormComponentBase.SubmitAsync`: both arms, both overloads; `Failed` applies before control returns; success clears prior server errors; overlapping second call returns without dispatching (`IsSubmitting` guard); a throwing continuation propagates (not swallowed).
- Discovery emitter: generator snapshot tests in Midgard's existing pattern, covering **both host shapes** — WASM (validators + router singleton) and server (router singleton + endpoint-discovery extension); double-registration lock (resolving `IEnumerable<IValidator<LoginRequest>>` in a host-shaped container yields exactly one).
- `MustAsync`/`CustomAsync`: validator tests with NSubstitute-substituted services — exists/not-exists/transport-failure for §6.1 (correlation ID logged on `Fault`; cancellation-token identity asserted through the rule); write-back visibility and rule coexistence for §6.2; blur semantics asserted by component test (no call on keystroke input, call permitted on change/blur, call on submit, malformed email short-circuits before the service, a second valid submit calls again).
- **Nested-send integration test:** `RegisterCommand` dispatched through the real pipeline with a validator whose rule sends `EmailExistsCommand` mid-validation — proving scoped-lifetime legality and cancellation propagation, not assuming them.
- Retrofit: existing component behavior held by the diff — hand-rolled plumbing deleted, `RequestContractTests` purity locks extended over `EmailExistsRequest`.

## 11. Exit Criteria (mirrors issue #39)

- [ ] Next component (change-password or forgot-password) lands on the finished mechanism with materially less code than `Login.razor`'s pre-retrofit ~20-line handler, identical across Server and WASM.
- [ ] `Login.razor`/`Register.razor` retrofitted; second-store plumbing deleted from both.
- [ ] New validator/component assembly requires zero host registration edits — proven by adding one.
- [ ] A `Result<T>`-backed request property's parse failure renders in-form through the same seam as FV failures, verified by test (§12).
- [ ] Email-exists check live on Register through the uniform pipeline, both hosts.

## 12. `Result<T>`-Hydrating Setters

Parse-once-in-setter: the wire record's string property setter hydrates a cached `Result<T>` beside the raw value; a companion get-only, non-`[DataMember]` property exposes the parse state; FV rules assert against the cached state (no double parse); failures flow through the same field-keyed seam as every other FV error. Two-unions doctrine governs: `Result<T>` for the parse, `Outcome<T>` for the call, never siblings (`../../the-two-unions.md`).

Contract shape, stated exactly:

- Only the raw string is a `[DataMember]`; the cached `Result<T>` never crosses the wire.
- **Deserialization initializes the cache by construction** — protobuf-net materializes the member through the same setter, so a deserialized record can never hold a stale or unset parse state. There is no code path that assigns the string without hydrating the cache; that is the invariant, not a convention.
- Repeated assignment re-parses and replaces the cache; default construction holds the declared default string's parse state — never an uninitialized union.

Test matrix (all against the one round-one instance): object-initializer assignment; protobuf round-trip (deserialized cache matches a fresh parse); repeated assignment; default construction; and a staleness probe (mutate → cached state always equals `Parse(current raw value)`).

Round one lands **one hand-written instance** — Register's email against the Svartálfheim parsing stack — plus the exit-criterion test. The generator story (partial property + marker attribute emission) is the intended end state but is not built at N=1; confirm the shape at N=2, same discipline as §2's promotion boundary. This leg stays in this spec deliberately: it is issue #39's leg 4 with its own exit criterion, and the lift-and-shift gate waits on all four legs.

## 13. Non-Goals

- The component lift-and-shift itself — gated behind this spec, not part of it.
- `ErrorCategory` vocabulary reconciliation — still deferred; §4 spends an existing member, adds none.
- The US-address lookup component — motivating evidence only (§6.2 proves the pattern testably).
- Visual/layout redesign of Login/Register — the companion visual note's concern.
- Per-service typed submit helpers via generation — N=2 rule (§5).

## References

- `2026-07-16-blazilla-validation-composition-poc.md` — decompilation ground truth (Blazilla 2.4.0 has no injection hook; second-store is sanctioned); §2.3/§2.4 wire-shape rulings adopted unchanged.
- `2026-07-15-blazor-validation-poc.md` — door mechanics, server/transport side.
- `../../Platform/specs/2026-07-27-mediator-pipeline-retires-gateway-design.md` — the pipeline everything here rides.
- `../../the-two-unions.md` — `Result<T>` vs `Outcome<T>` doctrine.
- `../../../../Heimdall/src/AuthN.Components.FluentUI/Login.razor`, `Register.razor` — the hand-rolled pattern this design retires (workspace-relative).
