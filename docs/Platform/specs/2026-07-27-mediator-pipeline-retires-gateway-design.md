# Mediator Pipeline Retires the Gateway Machinery

**Date:** 2026-07-27
**Status:** Ratified 2026-07-27 (desktop review) with the §2.4 cadence cure applied; lesser findings ride into the implementation plan as named obligations
**Supersedes in part:** `2026-07-24-transport-neutral-invocation-pipeline-design.md` (§2.2 gateway surface, §2.4 generator packaging, §2.5 chain composition mechanism, **and the prerender-hydration decided law** — `EnvelopeHydrationState` is deleted and prerender parity deferred, §6; the envelope, wire encoding, and behavior *semantics* survive), `2026-05-26-mediator-design.md` (the martinothamar/Mediator selection)
**Prior art acknowledged:** Jimmy Bogard, "Sharing Context in MediatR Pipelines" — the scoped-context pattern §2.4 adopts; the MediatR composition idiom §2.2 preserves for reader familiarity

---

## 1. Context

### 1.1 The subtraction experiment, executed

The 2026-07-24 pipeline shipped end to end behind the generated-gateway mechanism, with a standing reservation recorded at the time: build it, prove it, then rip it out and see what breaks. This document is the verdict of that examination, grounded in a full code audit (2026-07-27) rather than the design record — and the audit changed the question, because much of what the gateway design mandates turned out to be **dead on arrival**:

- `OutcomeServerInterceptor` — implemented, unit-tested, documented in three repos as "the one throw point in the whole chain" — is **never registered**. `AddNorseCodeFirstGrpc()` registers only `UnhandledExceptionInterceptor`.
- The generated `RegisterOutcomeSurrogates()` has **zero call sites** (and is emitted `internal`, so a foreign composition root could not call it). The §9 "transparent passthrough" wire shape was never in force.
- The server never enables gRPC-Web; the WASM client wraps `GrpcWebHandler`. The WASM→server path cannot function as wired.
- The hydration-parity acceptance test constructs **neither** generated gateway — both sides are hand-built `Outcome` literals. The two generated gateway implementations have zero test coverage.
- Because the behavior chain is composed inside the *in-process* gateway, the wire path bypasses it entirely: a WASM client's requests reach handlers with **no server-side validation and no authorization behavior**. This is not a wiring gap; it is the structural consequence of composing the chain at the gateway instead of around the handlers.

Separately, the audit corrected the record: **martinothamar/Mediator was never a dependency.** Six documents assert the pipeline runs "over martinothamar/Mediator"; no csproj anywhere references it. The four behaviors, `IBehavior<,>`, `BehaviorDelegate<>`, and the chain composition are already 100% hand-rolled — the only thing the gateway generator ever contributed was *where* the chain gets composed.

### 1.2 The generator's structural liabilities

All of the following are consequences of the three-emission-mode design, not bugs in it:

- Asgard's emitters hardcode Midgard `internal` type names as string literals — declared law carrying an invisible downward dependency on infrastructure, unrepresentable in the build graph.
- Every new `InProcessHost` composition root requires an `InternalsVisibleTo` edit in Midgard plus a republish — O(n) in composition roots.
- `NorseGatewayEmissionMode` is read via `AnalyzerConfigOptions` but never shipped as a `CompilerVisibleProperty`; the mode switch has no `default:` arm. Both failure modes are silent (Bifröst Open Decision #2).
- `[Behavior]` — the custom-behavior extension seam — is declared law the generator never reads.
- The generated chain `new`s four behaviors plus two `CreateLogger<T>` calls per invocation.
- All of this machinery serves exactly one decorated interface with three methods.

### 1.3 The constraint that actually matters

Heimdall's law was never "use a generator" — it is "components never reference Midgard or gRPC." The generated `I{Context}Gateway`'s entire contract-level delta over `I{Context}Service` is `Task`→`ValueTask` plus a `CancellationToken` parameter, and protobuf-net.Grpc natively supports a `CancellationToken` parameter on `[OperationContract]` methods. **DI substitution of the service interface, not a second interface, is what satisfies the constraint**: the server host registers the real implementation, the WASM host registers the wire proxy. Components inject `I{Context}Service` and never learn which they got.

---

## 2. Verdicts

### 2.1 The channel matrix

Every transport carries the same two concerns — an **idiom translator** for the DU, and an **unhandled net** — and each expresses them in that channel's native idiom:

| Channel | Idiom translation of the DU | Unhandled net |
|---|---|---|
| gRPC | `OutcomeServerInterceptor` — `Failed` → throw + `ErrorInfo` | `UnhandledExceptionInterceptor` → `INTERNAL` + correlation id |
| REST *(future)* | `OutcomeResultFilter` — `Failed` → ProblemDetails + reason | `OutcomeExceptionHandler` → 500 + correlation id |
| Circuit | **identity — the DU is the idiom** | `ExceptionTranslationBehavior` → `Err(Fault, correlationId)` as data |

The circuit is the one channel whose native idiom *is* the DU: components pattern-match `Outcome` and Blazilla eats `Problem` directly, so its idiom translator is the identity function, and its net is the only one that produces data instead of a transport artifact — crashing the circuit is the failure mode it exists to prevent. `ExceptionTranslationBehavior` keeps its §2.6 (2026-07-24) semantics verbatim, including the cooperative-cancellation carve-out; the v5 amendment already relieved it of business failures, since nothing upstream throws them.

The client side is deliberately asymmetric: the server translates the DU outward into many idioms, but Norse clients translate inward from exactly one.

| Client | Wire | Inbound translation |
|---|---|---|
| WASM / MAUI | gRPC only | client interceptor: `RpcException` + `ErrorInfo` → `Failed(Problem)` — the sole decoder in the land |
| Server circuit | none | identity — the DU never left |
| Partners | REST/JSON | their problem — ProblemDetails is egress-only; no Norse client ever parses it |

**Client decoder mechanics:** a gRPC client interceptor sees `TResponse` unconstrained, so constructing `Outcome<T>.Err` from a caught `RpcException` requires a type-erased factory — a cached per-closed-type delegate in `Infrastructure.Web.Client`, built once via reflection on first miss (one-time wiring, sanctioned; never on the success path). Non-`Outcome` response types pass through untouched.

**Wire mechanics made real (all three were designed and left unwired):** `OutcomeServerInterceptor` is registered by the generated server wiring; `RegisterOutcomeSurrogates()` is invoked against the channel/server `RuntimeTypeModel` by the generated wiring on both sides; the server enables gRPC-Web (`Grpc.AspNetCore.Web` + `UseGrpcWeb()`).

### 2.2 Pipeline composition — Bogard-classic, hand-rolled

Midgard composes the pipeline **once, in DI, around the handlers** — the MediatR idiom every .NET reader recognizes on sight, with no MediatR-shaped package underneath:

```csharp
// Midgard — AddNorsePipeline(), the one composition site; registration order is chain order, and it is law
services.AddScoped(typeof(IBehavior<,>), typeof(TelemetryBehavior<,>));
services.AddScoped(typeof(IBehavior<,>), typeof(ExceptionTranslationBehavior<,>));
services.AddScoped(typeof(IBehavior<,>), typeof(AuthorizationBehavior<,>));
services.AddScoped(typeof(IBehavior<,>), typeof(ValidationBehavior<,>));
services.AddScoped<ISender, Sender>();
```

`Sender` folds `IEnumerable<IBehavior<TRequest, TResponse>>` around the resolved `IRequestHandler<,>` per call — a plain, steppable delegate fold, no expression trees, no proxies. Dispatch from the runtime request type to the closed handler is **emitted by the registration generator** (§2.7), not discovered by reflection: reflection-free, AOT-clean, and the one genuine capability martinothamar/Mediator would have contributed, obtained from machinery the platform already owns.

Because the chain wraps handlers, it runs identically regardless of which channel delivered the request — same facts, same decision. This structurally cures the audit's worst finding (the unvalidated, unauthorized wire path): there is no longer a path to a handler that does not pass through the chain.

**Extension seam:** a product realm adds its own behavior by registering it — `services.AddScoped(typeof(IBehavior<,>), typeof(MyBehavior<,>))` in its own registration extension, ordered relative to the standard four by registration position. This replaces `[Behavior]`, which is deleted. One composition mechanism, not two.

**Rejected: martinothamar/Mediator.** Evaluated against the genuine-need test (protobuf-net.Grpc passes it: contract-first wire format from plain records, keeping gRPC conceptually out of the services). martinothamar's one genuine contribution is source-generated dispatch, which the registration generator emits as a byproduct. Its costs are real: `IPipelineBehavior<TMessage, TResponse>` does not know `Outcome<T>` exists — the envelope-native contract would flatten or need adapters — and its registration generator overlaps the platform's own. Familiarity lives in the seams (`Send(request, ct)`, ordered behaviors, the fold), not the library internals; hand-rolled preserves the seams 1:1.

### 2.3 Contracts (Asgard)

```csharp
// Norse.Abstractions.Web.Server.Mediator — payload-typed; the pipeline owns the envelope.
// (2026-07-27 final placement: the markers are deliberately SERVER-ONLY — with the command-wrapper
// seam, nothing WASM-shipped implements them, and living here means a wire assembly cannot even
// reference them. Structural purity over convention: off-roading requires a csproj edit that screams.)
public interface IRequest<TResponse> where TResponse : notnull;
public interface ICommandRequest<TResponse> : IRequest<TResponse> where TResponse : notnull;
public interface IQueryRequest<TResponse> : IRequest<TResponse> where TResponse : notnull;

// Norse.Abstractions.Web.Server.Mediator
public interface ISender
{
	ValueTask<Outcome<TResponse>> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
		where TResponse : notnull;
}

public interface IRequestHandler<in TRequest, TResponse>
	where TRequest : IRequest<TResponse>
	where TResponse : notnull
{
	ValueTask<Outcome<TResponse>> Handle(TRequest request, CancellationToken cancellationToken = default);
}
```

- `IRequest<TResponse>` is the neutral marker `Send` accepts; `ICommandRequest<>`/`IQueryRequest<>` are its two derived markers. Both flow through the same chain in v1; the split exists so a future behavior can bind to one side only (a transaction behavior being the obvious eventual tenant). Void-success commands use `TResponse = Unit`.
- The marker family revives `ICommandRequest<TResponse>` from dead code — the request→response type binding it was declared for now lives on its new base, `IRequest<TResponse>`, which is what `Send` infers from.
- **Alignment ripple:** `IRequestHandler<TRequest, TResponse>` changes from fully-generic `ValueTask<TResponse>` to envelope-native `ValueTask<Outcome<TResponse>>` with `TResponse` as the *payload* — today's handlers close `TResponse = Outcome<LoginResult>` by hand. The whole chain (`IBehavior<,>`, `BehaviorDelegate<>`, handlers, `Send`) now speaks one type algebra. `IBehavior<,>` keeps its existing shape (it was already envelope-native).
- **Wire and mediator requests are separate types (final form — 2026-07-27 execution ruling, third iteration, ratified):** the `[DataContract]` wire records carry **no mediator coupling at all** — no marker interface, no `[Authorize]` — keeping the WASM-shipped payload assemblies as lean as possible. The properly decorated mediator requests are **server-sovereign types in the implementing realm** (`LoginCommand`/`RegisterCommand`/`LogoutCommand` in Himinbjörg, each `[Authorize(Policy = ...)]` + `ICommandRequest<TWireResult>`). On ingress the gRPC service **hydrates** the command from the wire DTO and `Send`s it; on egress there is no mapping — **handlers respond `Outcome<TResponse>` where `TResponse` *is* the `[DataContract]` wire result** (`LoginResult`, `RegisterResult` — new wire record, since `BoolResponse` is server-only law — `LogoutResult`), deferred-completion URL populated in the handler. Validation splits by type: client-side Blazilla validates wire DTOs (Heimdall), the pipeline's `ValidationBehavior` validates commands (Himinbjörg's own validators — duplicated rules by design; server and client rules may legitimately diverge). `Logout` goes parameterless on the wire (CT-only operation) pending a protobuf-net.Grpc verification spike, with an empty wire marker parameter as the named fallback. `Unit` never crosses a wire — it survives only as an in-process payload.

### 2.4 Principal — seeded scoped accessor

Asgard declares the context contract; each channel adapter seeds it at entry; behaviors resolve it blind. This is the Bogard scoped-context pattern, typed:

```csharp
// Norse.Abstractions.Web.Server — scoped; supplied by each channel's adapter at that channel's own cadence
public interface IPrincipalAccessor
{
	ValueTask<ClaimsPrincipal> GetPrincipalAsync(CancellationToken cancellationToken = default);
}
```

**Cadence is a per-channel property of the adapter** (2026-07-27 remand, security-relevant): "seed once per scope" is correct only where scope equals request. On a circuit, scope equals circuit lifetime — a principal captured at circuit start keeps authorizing a stale identity after a `RevalidatingAuthenticationStateProvider` invalidates the session mid-circuit. So:

- **gRPC path (scope = request):** a lightweight server interceptor stamps `context.GetHttpContext().User` into the scoped accessor at request entry — seeded once, deterministic for the request's lifetime.
- **Circuit path (scope = circuit):** the accessor is never seeded; it defers to `AuthenticationStateProvider` **live on every access** — the async contract exists precisely so the circuit's principal is always current (`GetAuthenticationStateAsync` is cached-cheap inside a circuit). The one legal principal source there, per the 2026-07-24 ruling that `IHttpContextAccessor` is unsupported in a live circuit.
- **A scope neither seeded nor circuit-shaped fails loudly** (throws, naming the missing channel adapter) — no ambient sniffing, no probe order, no silent anonymous principal.

This retires `AuthorizationBehavior`'s `Func<ValueTask<ClaimsPrincipal>>` constructor closure; all four behaviors become plain DI citizens with ordinary constructor injection.

### 2.5 Authorization — policy on the request type

Open-generic `AuthorizationBehavior<TRequest, TResponse>` sees only the request and `next`; nothing bakes a policy string into a constructor anymore. Therefore **`[Authorize(Policy = ...)]` decorates the mediator request record** (the server-sovereign command type, per §2.3's final form — never the wire DTO) — the request names its policy:

```csharp
[Authorize(Policy = AuthNPolicies.Public)]
public sealed record LoginRequest : IQueryRequest<LoginResult> { ... }
```

- Read once into a `static readonly` per closed generic type (a `PolicyCache<TRequest>`) — zero per-call reflection.
- **Enforcement is compile-time first** (2026-07-27 review finding): the registration generator already walks every handled request type for the dispatch map, so a request lacking `[Authorize(Policy = ...)]` is a build error (NORSE011) — restoring the enforcement latency the deleted NORSE001 had. `PolicyCache<TRequest>`'s hard failure at first dispatch is the runtime backstop, not the primary arm. Fail loudly, never fall back to allow.
- `[Authorize]` decorates only the server-side command wrappers (§2.3 final form) — wire records ship to the browser policy-free and marker-free.
- The `[Authorize]` mirror on the concrete gRPC service class **stays unchanged** — ASP.NET Core endpoint metadata enforcement is the wire path's outer wall; the behavior is the single source of `Unauthorized`/`Forbidden` *as data*. Defense in depth, not duplication: same policy, same decision.
- Unauthenticated → `Unauthorized`; authenticated-but-lacks-policy → `Forbidden`, unchanged from the shipped behavior.

### 2.6 Validation — absence is a pass

`ValidationBehavior<TRequest, TResponse>` injects `IEnumerable<IValidator<TRequest>>`:

- Empty collection → no rules declared → straight to `next()`. This is the platform's own "absence is `[]`" law and the exact idiom MediatR readers expect — queries and commands both flow through the chain, and most queries will never carry a validator.
- One or more validators → all run; failures aggregate by property name into the single `Problem.Errors` dictionary (`ErrorCategory.Validation`).

### 2.7 Generators — discovery and wiring, never composition

Three generators, each living in the realm whose types it emits (the placement rule that dissolves the inverted-dependency finding instead of relocating it):

| Generator | Realm | Discovers | Emits |
|---|---|---|---|
| Registration | Asgard `gen/` | `IRequestHandler<,>` implementations and `IValidator<T>` implementations in the compiling realm (compiled-symbol walk, PackageReference-parity) | `AddNorse{Realm}Handlers()` — handler + validator `AddScoped` lines, plus the sender dispatch-map entries (request type → closed invoker) |
| gRPC server wiring | Midgard `gen/` | `[ServiceContract]` implementations visible to the composition root | `MapNorseGrpcServices()` — `MapGrpcService<T>` per service, interceptor registration (`OutcomeServerInterceptor`, `UnhandledExceptionInterceptor`, the principal-seeding interceptor), surrogate registration, gRPC-Web enablement |
| gRPC client wiring | Midgard `gen/` | `[ServiceContract]` interfaces referenced by the client project | channel + `CreateGrpcService<T>` registration as `I{Context}Service`, the client decoder interceptor, surrogate registration |

- Generators replace MediatR-style assembly scanning (banned) with compile-time registration; the runtime is a fold you step through in a debugger.
- No `NorseGatewayEmissionMode`, no `CompilerVisibleProperty`, no emission modes: each generator has one job and emits it wherever it is installed. A generator installed in the wrong project emits registrations that fail to compile — loud, not silent.
- The registration generator emits only Asgard-contract, FluentValidation, and realm-own type references (all already legal references in any service realm), so service realms (Himinbjörg) install an Asgard analyzer package — already legal. The gRPC wiring generators emit Midgard type references and are installed only in composition roots — the one place Midgard is already a legal reference. **The Midgard-type-name coupling becomes realm-internal.**
- Analyzer packaging (the `gen/` folder shape, `IncludeGeneratorInPackage`, the `Abstractions.Emit` forwarding target) reuses the proven Asgard/Urðarbrunnr layout unchanged. All emitters use `AppendCSharp` raw-string house style.

### 2.8 Components and hosts

- Components inject **`I{Context}Service`** directly. `IAuthenticationGateway` is deleted. The service contract gains a `CancellationToken cancellationToken = default` parameter per method (protobuf-net.Grpc carries it natively; the in-process implementation honors it directly).
- **Server host (circuit path):** registers Himinbjörg's `AuthenticationService` as `IAuthenticationService`; the service becomes three `Send(request, ct)` calls against `ISender` — thin, Midgard-blind, zero throws, exactly its current character.
- **WASM host (wire path):** the generated client wiring registers the protobuf-net proxy as `IAuthenticationService` with the decoder interceptor on the channel. No hand-written or generated per-contract adapter class exists — the interceptor is the sole decoder.
- **Stories host:** fakes `IAuthenticationService` (replacing `FakeAuthenticationGateway`) — one fewer interface to fake is the point.

### 2.9 Circuit safety net

Rides along in this pass (spec §2.6 of 2026-07-24 requires it; nothing satisfies it today):

- A layout-level `ErrorBoundary` in Yggdrasil's interactive root renders the shared `Problem`/Fault UI (correlation id minted and logged server-side) instead of letting a lifecycle exception tear down the circuit with only the reconnect modal as evidence.
- A `CircuitHandler` logs circuit lifecycle faults with the same correlation vocabulary.
- Scope note: the pipeline's net covers the invocation path; the `ErrorBoundary` covers everything outside it (`OnInitializedAsync` doing non-`Send` work, event callbacks, render faults). Both exist because they cover disjoint failure classes.

---

## 3. Resulting component model

| Realm | Delta |
|---|---|
| Asgard | `IRequest<>`/`ICommandRequest<>`/`IQueryRequest<>` declared; `ISender` declared; `IRequestHandler<,>` re-aligned envelope-native; `IPrincipalAccessor` declared; registration generator lands in `gen/`; `GenerateGatewayAttribute`, `BehaviorAttribute`, and all four gateway emitters deleted |
| Midgard | `AddNorsePipeline()` + hand-rolled `Sender`; four behaviors re-plumbed for DI (accessor, `IEnumerable` validators, policy cache); gRPC server + client wiring generators land in `gen/`; principal-seeding interceptor; `OutcomeServerInterceptor` finally registered; per-consumer `InternalsVisibleTo` grant deleted |
| Heimdall | `[GenerateGateway]` removed; request records gain markers + `[Authorize]`; service contract gains `CancellationToken`; components re-point `IAuthenticationGateway` → `IAuthenticationService` (mechanical) |
| Himinbjörg | `AuthenticationService` injects `ISender`, three `Send` calls; handlers implement the re-aligned `IRequestHandler<,>`; hand-written registration lines replaced by the generated `AddNorseIdentityHandlers()` |
| Yggdrasil | Both `CompilerVisibleProperty` workarounds, `EmitCompilerGeneratedFiles` plumbing, `Generated/` trees, and both generated gateways deleted; generated gRPC wiring adopted; `ErrorBoundary` + `CircuitHandler`; `UseGrpcWeb` |

## 4. Deletion inventory

`[GenerateGateway]` · `GatewayGenerator` + `ContractEmitter`/`WireHostEmitter`/`InProcessHostEmitter`/`OutcomeSurrogatesEmitter` (surrogate *emission* moves into the gRPC wiring generators; the surrogate *concept* survives) + the 16 generator tests · `I{Context}Gateway` and both generated implementations · `BehaviorAttribute` · `NorseGatewayEmissionMode` and both `CompilerVisibleProperty` workarounds · Midgard's `InternalsVisibleTo Include="Norse.Hosting.Web.Server"` · `EnvelopeHydrationState` + tests (zero consumers; reintroduce when real prerender-parity work starts) · `FakeAuthenticationGateway` · NORSE001–005 as gateway diagnostics (NORSE001's "every method carries `[Authorize]`" obligation is inherited by `PolicyCache<TRequest>`'s hard failure; the registration generator defines its own diagnostic range).

**Bifröst Open Decision #2 dissolves outright** — the `CompilerVisibleProperty` bug cannot exist without the property.

## 5. Acceptance proof

Heimdall's authentication flow, re-proven on the new shape:

1. **Circuit path:** component → `IAuthenticationService` (Himinbjörg) → `Send` → chain → handler; a `Failed(LockedOut)` renders through Blazilla; an injected handler throw renders as `Fault` with a correlation id — as data, circuit intact.
2. **Wire path:** WASM component → proxy → gRPC-Web → endpoint (`[Authorize]` wall) → seeding interceptor → service → `Send` → same chain → handler; `Failed(LockedOut)` → `OutcomeServerInterceptor` throw → `ErrorInfo` trailer → client decoder interceptor → `Failed(LockedOut)`.
3. **The parity test becomes real:** both paths constructed from actual DI composition (the pipeline composes in a plain test `ServiceProvider` — no generator in the loop), asserting the same `ErrorCategory` side by side. The 2026-07-24 plan's parity test faked both sides because the generated gateways were untestable; that excuse is gone.
4. **Wire-path validation regression:** a request that fails FluentValidation, sent over the wire, comes back `Validation` with field errors — impossible before this design; the wire path had no validator in it.

## 6. Out of scope / deferred

- REST channel (`OutcomeResultFilter`, `OutcomeExceptionHandler`) — the matrix names its shape; nothing builds it until a partner-facing REST surface exists.
- Streaming (`IAsyncEnumerable<T>`) — unchanged stance from 2026-07-24 §2.3.
- Transaction behavior on `ICommandRequest<>` — the marker split exists for it; designing it waits on Midgard's repository/unit-of-session convergence.
- Prerender→WASM hydration parity — `EnvelopeHydrationState`'s successor, designed when the work is real.
- Notifications/eventing through the sender — Ratatoskr's territory, not this pipeline's.
- Client decoder AOT hardening — the one-time-reflection `Err`-factory (§2.1) is sanctioned and fine, but the client wiring generator already walks the same `[ServiceContract]` interfaces and could emit the closed factory map, making the client fully AOT-pure. Recorded here so it stays a decision, not a discovery (2026-07-27 review finding).
- **§2.9's shared Fault UI and correlation-id thread** (2026-07-27 final-review ruling, Buvy): the circuit net ships **structurally only** — `ErrorBoundary` + `LoggingCircuitHandler` keep the circuit alive and observable at lifecycle level, with a generic recovery message. The shared Problem/Fault component and the correlation-id minting/vocabulary §2.9 describes ride the **Outcome + Blazilla design session** (the platform's next design court date, which owns the error-rendering surface end to end) — designed once, properly, not twice.

**Acceptance policy, binding on the implementation plan (2026-07-27 ratification):** "designed" and "wired" are different claims. Every registration this design mandates — interceptors, surrogates, pipeline, dispatch map — must have a test that fails when the registration is removed. `OutcomeServerInterceptor` sat implemented, unit-tested, documented, and dead for three days because nothing asserted its presence in composition. Never again is a test away.

## 7. Documentation reconciliation (same change, boy-scout law)

- Six files claim martinothamar/Mediator is in use (never was): `Bifrost/CLAUDE.md` state-of-the-union, `Glitnir/CLAUDE.md` §4 key-libs, `docs/decomposition.md`, `2026-05-26-mediator-design.md` (supersession notice), `2026-07-15-blazor-validation-poc.md`, plus this design's own predecessors.
- Glitnir key-rejections row becomes: MediatR, martinothamar/Mediator → hand-rolled Norse pipeline (this document).
- `Bifrost/CLAUDE.md` Open Decision #2 removed (dissolved, §4); state-of-the-union updated; stale "staged, not shipped" IVT claims corrected (it was committed).
- Realm CLAUDE.md files for Asgard, Midgard, Heimdall, Himinbjörg, Yggdrasil updated to the new shape in their own ship-gate changes.

## 8. References

- `2026-07-24-transport-neutral-invocation-pipeline-design.md` + §9 amendment — envelope, wire encoding, behavior semantics (all retained)
- `../plans/2026-07-24-transport-neutral-invocation-pipeline.md` — the executed plan this design subtracts from
- `the-two-unions.md` — `Outcome<T>` doctrine (untouched by this design)
- Jimmy Bogard, *Sharing Context in MediatR Pipelines* (jimmybogard.com) — scoped-context prior art
- Code audit, 2026-07-27 session — dead-wiring findings (§1.1) and generator liabilities (§1.2)
