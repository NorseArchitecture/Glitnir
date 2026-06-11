# Mediator Design (`Norse.Abstractions.Mediator` + `Norse.Infrastructure.Mediator`)

**Date:** 2026-05-26 (WIP checkpoint) · **Completed:** 2026-06-03
**Status:** Approved design, pre-implementation
**Owner:** Buvy

> The 2026-05-26 WIP checkpoint locked §1–3 of an earlier shape. The 2026-06-03 completion supersedes parts of that shape deliberately — every superseded decision is listed in §11 with its replacement and rationale. Nothing was dropped silently.

> **Amended 2026-06-07 (error-vocabulary reconciliation, punch-list §1.6):** the "crossing the streams" boundary ruling. The mediator no longer reuses Primitives's `Result<T>` (that type is scoped to scalar→domain *conversion* only); it owns its own **`Outcome<T>`** result type carrying a trimmed `ErrorCategory` of **Validation / NotFound / Conflict**. Authorization left the pipeline entirely (it is service-entry, performed before the mediator runs); `Forbidden`/`Unavailable`/`Internal` are no longer `ErrorCategory` cases. Touched: §0, §1, §2, §3.2, §3.3, §3.4, §4, §5, §6, §7, §8, §9, §10, §11, §13. See primitives spec §4.2 for the matching Primitives shrink.

---

## 0. Context

The mediator is the server-side dispatch layer inside `Norse.Hosting.Web.Server`: it turns an `I{Context}Api` method call into a handler invocation with a fixed pipeline and a normalized `Outcome<T>`, across three front doors — in-process Blazor Server, gRPC (WASM/MAUI), and JSON controllers (partners).

**Related specs:**

- `2026-06-03-messaging-foundation-design.md` — NServiceBus resolved; `IMessageSession` is the command dispatch surface; `.Backend` assembly defined; hard walls between `.Server` and `.Worker`.
- `2026-05-21-midgard-persistence-design.md` — `IDocumentRepository<T>`, the shim → worker → enrichment write flow, CQRS tiers. This spec resolves its §17 #6 (wire-shape placement) and queues two small amendments (§12).
- `2026-05-20-yggdrasil-hosting-design.md` — plugin model, `JsonControllerBase<TService>` lifecycle, native gRPC stack (protobuf-net.Grpc rejected there; this spec conforms).
- `2026-05-20-svartalfheim-primitives-design.md` — owns the conversion `Result<T>` and `[MustConsume]` (YGG201). This spec defines the mediator's own `Outcome<T>` (§3.3); the two are deliberately distinct.

**Prior-art lineage (Buvy's previous platform):** the `GrpcControllerBase` HOF controllers are the ancestor of the JSON door (§7); the `QueryHandler<T>` / `ProjectionQueryHandler<TEntity, TProjection>` generic query family is the ancestor of the read side (§4) — superseded by per-request handlers; the god-service failure mode ("mediator never got wired into the gRPC service") is structurally unreachable here because the service is generated (§8).

---

## 1. Position and Scope

**HTTP-server-only.** The mediator lives in `{Company}.{Context}.Server` inside `Norse.Hosting.Web.Server`. Workers use NServiceBus handlers as their dispatch surface and never reference any mediator package (YGG405).

**What the mediator owns:**

- `I{Context}Api` method → handler dispatch via the source-generated `{Context}Service` forwarder.
- The fixed pipeline: telemetry → validate → handler (§6).
- The read side: per-request handlers querying Mongo with server-side filter + projection expressions (§4).
- The write side: validate → shim → `IMessageSession.Send` → response (§5).
- The `ErrorCategory` → door translation contract (§7) — declared here, honored by each door.

**What it does not touch:** workers (YGG405) · in-process pub/sub (no `Publish`, no `INotification`; all eventing on the bus) · cross-context calls (go through the other context's `I{Context}Api`) · transactions (no Postgres path exists in `.Server` at all — the entity types live in `.Worker`) · Mongo document *updates* (worker-side update-builder mechanics are a future Mongo-update story; explicitly out of scope here).

---

## 2. Library Decision — Hybrid

**Dispatch core: [martinothamar/Mediator](https://github.com/martinothamar/Mediator) (`Mediator.Abstractions` + `Mediator.SourceGenerator`), version floor 3.0.** Source-generated dispatch, `ValueTask`-based, full Native AOT, OTEL semantic-convention telemetry built in, explicit assembly configuration, `GenerateTypesAsInternal`. A near-drop-in MediatR replacement with the reflection removed — familiarity is a feature.

**Law and generation stay ours:**

| Package | Realm | Contains |
|---|---|---|
| `Norse.Abstractions.Mediator` | Abstractions (declared law) | `[MediatorService]`, `ICommandRequest<T>`, `IRequestValidator<T>`, the `Outcome<T>` / `ErrorCategory` result types, `[Projection<TDocument, TResponse>]`, **the source generator** (forwarder + registration + projections + YGG401–408 diagnostics) |
| `Norse.Infrastructure.Mediator` | Infrastructure (embodied law) | The fixed pipeline behaviors (validation), the strict-single query helper, paging clamps, the `ErrorCategory` render table (server side; the client-side rebuild is Norse — §7) |

**Why hybrid and not full custom:** the lib replaces only the dispatch core (~40 lines in the old design) but brings AOT-proven plumbing and OTEL telemetry we'd otherwise rebuild. **Why not lib-wholesale:** the forwarder generator, projection generator, and YGG diagnostics don't exist in any library — they're the law, and they're ours under every option.

**Accepted trade, recorded:** request types implement the lib's `IRequest<TResponse>`, so **`Mediator.Abstractions` becomes a dependency of `{Company}.{Context}.Contracts`** (and therefore of WASM bundles). It is a tiny netstandard abstractions package with no transitive baggage. This is a deliberate exception to the contracts-stay-clean instinct, justified by not owning a dispatch abstraction layer whose only purpose is avoiding the dependency.

**No open-generic handler risk:** the 3.0 rewrite removed open-generic registrations for AOT's sake — irrelevant here, because every handler in this design is per-request concrete (the lib generator's happy path).

---

## 3. Contract Surface

### 3.1 The `I{Context}Api` method shape (YGG401)

```csharp
ValueTask<Outcome<TResponse>> MethodName(TRequest request, CancellationToken cancellationToken)
  // where TRequest : IRequest<Outcome<TResponse>>
```

`ValueTask` (lib-aligned, fewer allocations); exactly one request parameter; `CancellationToken` required; `Outcome<T>` mandatory. Any deviation is a build error. The interface carries `[MediatorService]` only — **no protobuf-net.Grpc decoration** (the hosting spec rejected that stack; the old §2.4 of this spec is superseded).

### 3.2 Request kinds

```csharp
// Read request — plain lib marker.
public sealed record GetInvoiceRequest(Guid Id)
  : IRequest<Outcome<InvoiceDetail>>;

// Mutating request — declares itself. Validator REQUIRED (YGG406).
public interface ICommandRequest<TResponse> : IRequest<Outcome<TResponse>>;

public sealed record BindPolicyRequest(Guid Id, /* … */)
  : ICommandRequest<PolicyBindAccepted>;
```

The old "no ICommand/IQuery split" rule is superseded **with cause**: handler shape no longer carries the read/write distinction (open-generic read handlers are dead, §4), and the validator-required rule needs a compile-time discriminator. The split is one marker interface, not a parallel hierarchy.

### 3.3 The `Outcome<T>` result type and the validation contract

The mediator does **not** reuse Primitives's `Result<T>`. That type is scoped to scalar→domain *conversion* — a `Failure` carries a `ParseFailure` reason and nothing else (primitives spec §4.2). An application operation's outcome is a different concern (a record didn't exist, a uniqueness rule was violated), and conflating the two crosses a boundary the platform keeps separate (2026-06-07 ruling). The mediator owns **`Outcome<T>`**: a success value, or a failure carrying an `ErrorCategory` (§7) and its structured detail. `Ok`/`Err` are its factory surface.

```csharp
// Norse.Abstractions.Mediator — the application-operation result, distinct from
// Primitives's conversion Result<T> and the egress HttpResult<T>.
[MustConsume]
public union Outcome<T>(Success<T>, Problem);   // value, or a Problem (ErrorCategory + detail, §7)
[MustConsume]
public union Outcome(Done, Problem);            // non-generic — operations with no return value

public static class Outcome  // factory surface used throughout this spec
{
  public static Outcome<T> Ok<T>(T value);
  public static Outcome<T> Err<T>(ErrorCategory category, /* detail per category, §7 */);
  // non-generic Ok / Err siblings
}
```

The failure case is named `Problem` (not `Failure`) precisely so it never collides with Primitives's conversion `Failure` — two result families, two vocabularies, by design.

`IRequestValidator<TRequest>` returns the **non-generic `Outcome`**; its failure is `ErrorCategory.Validation`, field-keyed and **aggregated across the request's failed fields**. This is the home Primitives's `Collect` relocated to (primitives §4.2): the validate step inspects each `Result<T>` field on the request, folds every conversion `Failure` plus any cross-field rule break into one `Validation` outcome.

```csharp
public interface IRequestValidator<TRequest>
{
  ValueTask<Outcome> ValidateAsync(TRequest request, CancellationToken ct);
  // Err carries Validation detail: (FieldPath, Code, Message)[] — field-level, aggregated.
}
```

Validators are plain hand-written classes — **FluentValidation is rejected for this seam** (runtime-compiled expressions and convention registration, against the grain of the platform). A command request without a registered validator is a build error (YGG406); reads are validator-optional.

**Authorization is not a mediator concern (2026-06-07 ruling).** The mediator runs *inside* an already-authorized service. Authorization is per-method `[Authorize(Policy = …)]` on the service surface — the prior platform's `ServerGrpcServiceBase` pattern (e.g. `Policies.Anonymous` for login/register, `Policies.Authorized` for logout, `Policies.Verified` for profile management), enforced by ASP.NET **before the method body runs**. Every entry door — Blazor Server in-process, gRPC, the JSON controller — carries that policy at the service boundary. By the time a request reaches the pipeline, the caller is authorized; there is **no `IRequestAuthorizer` and no `Forbidden` outcome.** Resource-scoped checks that genuinely need a loaded entity are handler-internal and surface as `NotFound` — don't reveal existence to a caller who shouldn't see the resource (the same instinct as the prior platform's `ForgotPassword` returning success for an unknown email) — never as a pipeline authorization step.

**Validators live in `Contracts`, beside the requests they validate.** The server picks them up through its client-chain dependency (it serves the WASM app), so one validator runs in all three scenarios: WASM/MAUI forms validate client-side for immediate UX, Blazor Server validates in-process, and the server pipeline re-runs the **same validator** as the enforcement gate. Client-side validation is courtesy; the pipeline run is law — never trust the client. Validators are therefore pure over the request (no repository, no server dependencies); checks that need server-side data belong in the handler (returning `Err(Validation)` / `Err(Conflict)`) or the authorizer.

### 3.4 Placement — resolves persistence §17 #6

| Type | Assembly | Why |
|---|---|---|
| `I{Context}Api`, request records, response records (projection targets), **validators** | `{Company}.{Context}.Contracts` | Components inject the interface; everything in its signatures must be WASM-safe. Validators ride the client bundle so the same rules run in WASM/MAUI forms, Blazor Server, and the server pipeline |
| Mongo document records (+ `ProcessingStatus` block, `IWireShape`) | `{Company}.{Context}.Backend` | Server projects from them; worker writes them; never in a client bundle |
| NSB commands | `.Backend` (server→worker) / `.Worker` (`internal`, chain-private) | Per the messaging spec |
| Handlers, **projection declarations** | `{Company}.{Context}.Server` | Server tier — the only compilation where document types (`.Backend`) and response types (`Contracts`) are both visible, which the projection expression requires |

A response record is sometimes shaped 1:1 with a tight document — that's fine; it is still a distinct `Contracts` type and the read still projects (§4).

---

## 4. The Read Side

**Per-request handlers calling `IDocumentRepository<TDocument>` directly.** The generic query-request family from the prior platform (`QueryEntity<T,TProjection>`, `QueryList`, `QueryCount`, `QueryExists`) is superseded: it existed so hand-written services had reusable plumbing, but handlers *are* the plumbing now, and dispatching a second mediator request from inside a handler is indirection without payoff (§2.5).

```csharp
internal sealed class ListInvoicesHandler(IDocumentRepository<InvoiceDocument> docs)
  : IRequestHandler<ListInvoicesRequest, Outcome<IReadOnlyList<InvoiceSummary>>>
{
  public async ValueTask<Outcome<IReadOnlyList<InvoiceSummary>>> Handle(
    ListInvoicesRequest req, CancellationToken ct)
    => Result.Ok(await docs.QueryAsync(
         filter:     d => d.CustomerId == req.CustomerId && d.Status != ProcessingStatus.Rejected,
         projection: BillingProjections.InvoiceSummary,    // source-generated (§4.2)
         sort:       d => d.CreatedAt,
         skip: req.Page * req.PageSize, take: req.PageSize, ct));
}
```

**Every read projects.** The filter and projection expressions are constructed server-side in the handler and fed to the Mongo driver (`$project`) — Mongo reads only what's needed; the driver materializes the `Contracts` response directly; no per-item .NET mapping. Expressions never appear on the API surface (they don't serialize; the same interface must work over gRPC and JSON).

### 4.1 Platform read guardrails (`Norse.Infrastructure.Mediator`)

- **Paging clamps:** `take` is clamped to a platform max (150), default 25; text-search reads clamp tighter (100, default 5). No endpoint can unboundedly list — declared here, enforced in the repository implementation.
- **Strict-single helper:** single-document reads resolve through a helper with strict cardinality — 0 matches → `Err(NotFound)`, 1 → `Ok`, ≥2 → `Err(Conflict)`. This supersedes the prior platform's `#if DEBUG SingleOrDefault #else FirstOrDefault` divergence: the strict behavior runs in **every** environment, as an `Outcome` value instead of an exception, and frontier idempotency (SequentialGuid `_id`) removes the duplicate-row excuse.
- **Exists/count: deferred.** The Mongo driver supports `Any`/`Count`, but no concrete consumer exists yet on the document side — per the dragon-sizing rule, `IDocumentRepository<T>` gains them when demand shows up, not before. Re-entry trigger: the first handler that needs an existence gate (the approved-builder-gate shape).

### 4.2 Source-generated projections

The projection expression must reference the document type (`.Backend`) *and* the response type (`Contracts`) — so its declaration lives in `.Server`, the only compilation that sees both. The Mongo C# driver must understand the emitted expression tree; it is a server artifact, never a contract one. The response record in `Contracts` stays clean — no attribute, no document reference.

```csharp
// {Company}.Billing.Server — declaration site; generator fills the body.
[Projection<InvoiceDocument, InvoiceSummary>]
[Projection<InvoiceDocument, InvoiceDetail>]
internal static partial class BillingProjections;

// Generator emits (per pair):
//   internal static partial class BillingProjections
//   {
//     public static Expression<Func<InvoiceDocument, InvoiceSummary>> InvoiceSummary { get; }
//       = doc => new InvoiceSummary { Id = doc.Id, BoundPremium = doc.BoundPremium, … };
//   }
// Handler usage: projection: BillingProjections.InvoiceSummary
```

Reductionary maps (member-init matched by name + type) are generated; a response member with **no matching document member is a build error** (YGG408) — no silent dropping. Complex maps skip the attribute pair and hand-write the expression property on the same class: one mechanism, visible escape hatch.

**Mapperly considered and parked:** its queryable-projection surface is `IQueryable` extensions, which doesn't yield the raw `Expression<Func<TDocument, TProjection>>` the repository contract takes; the reductionary emit is trivial for a generator we already own.

---

## 5. The Write Side

The command handler shape, after the pipeline has already validated (§6):

```csharp
internal sealed class BindPolicyHandler(
  IDocumentRepository<PolicyDocument> policies,
  IMessageSession bus)                                 // raw NServiceBus — no wrapper
  : IRequestHandler<BindPolicyRequest, Outcome<PolicyBindAccepted>>
{
  public async ValueTask<Outcome<PolicyBindAccepted>> Handle(BindPolicyRequest req, CancellationToken ct)
  {
    var shim = req.ToShim();                           // ProcessingStatus.Pending
    await policies.ShimAsync(shim.Id, shim, ct);       // best-effort; idempotent upsert
    await bus.Send(new ExecutePolicyBindCommand(shim.Id, req.Payload), ct);
    return Result.Ok(PolicyBindAccepted.From(shim));
  }
}
```

- **Raw `IMessageSession`, no platform wrapper.** NServiceBus *is* the abstraction — over RabbitMQ, SQS, Azure Service Bus, all of it. Wrapping it to hedge a decision that is already codified (§7 #2 RESOLVED) is wheel-reinvention; the platform's energy goes into being a place developers enjoy working, not into a shim against a migration that isn't coming. Same reasoning that killed `IWebhookDispatcher`.
- **Deterministic bus `MessageId` via NSB outgoing behavior, not a wrapper.** A platform outgoing-pipeline behavior (registered in the hosting runtime's endpoint defaults, messaging spec §3) stamps `MessageId = UUIDv5(command type, ResourceId)` for commands implementing a small Abstractions marker (`Guid ResourceId { get; }`). Frontier idempotency extends into broker-level dedup; handlers never touch `SendOptions`; forgetting is impossible because nothing needs remembering. Worker-side chain sends are already covered by the outbox.
- **Computed responses are sanctioned.** A handler may read Mongo/reference data and run pure computation (the rate-quote pattern: factors → rater → premium), then shim + send + return the enriched response. The constraint is *what it can touch* (Mongo + pure code + the bus — never Postgres, which is type-system unreachable), not *what it returns*.
- **Shim-then-dispatch semantics** per the persistence spec §6.3: shim is best-effort and idempotent; dispatch must succeed; transient infrastructure failure propagates as an exception and the door maps it (§7).
- Handlers don't catch infrastructure exceptions. Business-rule rejection discovered *server-side pre-dispatch* returns `Err(Validation)` / `Err(Conflict)`; rejection discovered worker-side lands in the document as `ProcessingStatus.Rejected` per the persistence spec.

---

## 6. The Fixed Pipeline

```
telemetry (lib OTEL) → validate → handler
```

- Implemented as lib pipeline behaviors inside `Norse.Infrastructure.Mediator`, registered by the generated `Add{Context}Mediator()` via `MediatorOptions`. **No per-context behavior extension surface exists** — the pipeline is fixed law; the lib's behavior mechanism is an implementation detail, not an invitation.
- **Validate:** resolves `IRequestValidator<TRequest>` if registered (mandatory for `ICommandRequest`, YGG406). `Err(Validation)` short-circuits — the handler never runs.
- **No authorize step.** Authorization is service-entry, performed before the mediator runs (§3.3); the pipeline assumes an authorized caller.
- **Telemetry:** the lib's OTEL activity/meter instrumentation, on by default, aligned with messaging semantic conventions. Inbound/outbound log scope (request type, elapsed, ok/err) rides the same behavior.
- Exceptions: the pipeline logs and **rethrows**. Mapping to a wire shape is door territory (§7); `OperationCanceledException` passes through untouched.

This re-litigates the WIP's "no validation in the mediator" (old decision #6) by explicit user request: business-state validation needs the request *and* server-side data, which no marshaller layer can see. The marshaller follow-on spec shrinks to deserialization-shape concerns only.

---

## 7. Door Translation — One Table, Three Doors

The same `{Context}Service` instance sits behind every door. `Outcome<T>` is the lingua franca; each door maps the **three** `ErrorCategory` cases idiomatically. The table is declared **once** in `Infrastructure` and consumed by all doors:

| `ErrorCategory` | JSON door (RFC 9457) | gRPC door | In-process (Blazor Server) |
|---|---|---|---|
| `Validation` | 400 + `ValidationProblemDetails` (field-keyed errors) | `InvalidArgument` + `google.rpc.BadRequest` field violations | `Outcome` failure as a value |
| `NotFound` | 404 | `NotFound` | value |
| `Conflict` | 409 | `Aborted` | value |

`Forbidden`, `Unavailable`, and `Internal` are **not** `ErrorCategory` cases — a handler never returns them:

- **401 / 403** are rendered by the **service-entry authorization** (the door's `[Authorize]` / `RequireAuthorization`), before the request reaches the mediator (§3.3).
- **503** (transient infrastructure, e.g. broker-down on dispatch) and **500** (uncaught exception) are **synthesized by the host pipeline**, the §2.7 "production runtime, last resort" — never an `Outcome` value.

- **JSON door** — `JsonControllerBase<TService>` (Norse.Infrastructure.Api): HOF helpers in the `GrpcControllerBase` lineage (`GetAsync`, `ListAsync`, `CreateAsync` → `CreatedAtRoute` + Location, `SendAsync` → 202) that invoke the `I{Context}Api` method and route the `Outcome` through the table. Pattern-matching `ErrorCategory` replaces null-checking `EntityResult`.
- **gRPC door** — the native-stack adapter (proto-generated `{Service}Base` wrapping `I{Context}Api`, per the hosting/UI-composition specs): `Ok` → proto response; `Err` → `RpcException` per the table. Proto-message ↔ request/response mapping is the adapter's job and is specified in the UI Composition amendment, not here.
- **In-process door** — components and InteractiveServer callers receive `ValueTask<Outcome<T>>` raw. Failure is a value; Blazor renders it. No throwing across the in-process boundary.
- **Render-table realm split (2026-06-07).** The server-side render table above and the door base classes are **Infrastructure** (embodied platform infra, where `JsonControllerBase` already lives). The **client-side counterpart** — the gRPC client interceptor in `Norse.Hosting.Web.Client` / `Maui` that rebuilds the wire status back into an `Outcome<T>` — is **Norse**. The two are one symmetric channel-adaptation concern, split across realms only because they deploy on opposite sides of the wire. The payoff: a component **consumes `Outcome<T>` and is dumb about channel** — in Blazor Server it gets the `Outcome` in-process; over gRPC (WASM/MAUI) the client interceptor reconstructs the identical `Outcome` from the wire. It never knows which transport carried it.
- **Unhandled exceptions never carry detail across a wire.** Each door's global layer maps uncaught exceptions to a host-synthesized 500 (`Internal`) with a correlation id only; transient infrastructure failures surface as a host-synthesized 503 (`Unavailable`) where the door can distinguish them.
- `OperationCanceledException` → gRPC `Cancelled` / JSON connection-abort; logged as information, never as error.

---

## 8. The Source Generator (`Norse.Abstractions.Mediator`)

Runs in each `{Company}.{Context}.Server` compilation. Inputs: the `[MediatorService]` interface (each method validated against YGG401), `IRequestHandler<,>` implementations in the assembly, `IRequestValidator<>` implementations in the referenced `Contracts` assembly (declared client-side, registered server-side into the pipeline), and `[Projection<,>]` declarations in the assembly.

**Emits:**

1. **`{Context}Service : I{Context}Api`** — `internal sealed`, non-partial; every method forwards to the lib's `ISender`. No hand-written service exists; the god-service failure mode has no home.
2. **`Add{Context}Mediator()`** — registers the service, handlers, validators, and the lib's `MediatorOptions` (pipeline behaviors, explicit assemblies, internal generated types). Called from `{Context}Plugin.ConfigureServices`.
3. **Projection expressions** for `[ProjectionOf]` records (§4.2).

---

## 9. Enforcement (YGG401–408, proposed; analyzer catalog ratifies)

| ID | Trigger |
|---|---|
| **YGG401** | `[MediatorService]` method deviates from `ValueTask<Outcome<TResponse>> M(TRequest, CancellationToken)` |
| **YGG402** | API request type with no `IRequestHandler<,>` in the assembly |
| **YGG403** | Two handlers for the same request |
| **YGG404** | Handler response type doesn't match the API method's declared response |
| **YGG405** | `{Company}.{Context}.Worker` references `Norse.Abstractions.Mediator` / `Norse.Infrastructure.Mediator` / `Mediator.Abstractions` |
| **YGG406** | `ICommandRequest<T>` with no registered `IRequestValidator<>` |
| **YGG407** | Handler injects `IMessageSession` but its request is not `ICommandRequest<T>` |
| **YGG408** | `[Projection<TDocument, TResponse>]` pair where a response member has no matching document member |

YGG406 + YGG407 close the mutation loophole from both directions: a command can't skip validation, and a "read" can't smuggle a send.

---

## 10. Testing

- **Handler units:** NSubstitute for `IDocumentRepository<T>` / `IMessageSession`; Shouldly; real `Outcome<T>` (never mock the ADT).
- **Generator snapshots:** emitted forwarder, registration, and projection source against golden files; diagnostic cases for each of YGG401–408.
- **Pipeline:** validator short-circuit behavior; command-without-validator caught at build, not test.
- **Door table:** one test fixture per door asserting every `ErrorCategory` row; exception-mapping cases (no detail leakage, correlation id present).
- **AOT acceptance:** NativeAOT publish smoke on a representative `{Company}.{Context}.Server` — zero reflection warnings from the dispatch path.
- **Worker exclusion:** assembly-graph assertion that no worker references any mediator package.

---

## 11. Resolved Decisions

Carried forward from the WIP unchanged: **(1)** HTTP-server-only, workers never reference the mediator · **(2)** mediator sits behind `I{Context}Api`; `{Context}Service` is generated · **(4)** no `Publish`/`INotification` · **(7)** `Outcome<T>` mandatory (was `Result<T>` before the 2026-06-07 stream-uncrossing) · **(10)** one handler per request.

Superseded — each by explicit decision this session:

| Old (WIP) | New | Why |
|---|---|---|
| Custom `IMediator`/dispatch runtime | **Hybrid:** martinothamar/Mediator dispatch core; Abstractions keeps law + generator | The lib replaces the cheap part well (AOT, OTEL, familiarity); the expensive parts are ours either way |
| `Task<Result<T>>` signatures | **`ValueTask<Result<T>>`** | Lib-aligned; fewer allocations; in-process callers indifferent |
| Open-generic `DocumentQuery<,>` / `DocumentSingleQuery<,>` on the API surface | **Dead.** Per-request read handlers; expressions constructed server-side | Expressions don't serialize; the same interface must work over gRPC/JSON. Strict-single semantics survive as a platform helper |
| No ICommand/IQuery split | **`ICommandRequest<T>` marker** | Handler shape no longer carries the distinction; YGG406/407 need a compile-time discriminator |
| No validation in the mediator (#6) | **Fixed pipeline: validate before handler** (authorize step added here, then removed 2026-06-07 — authorization is service-entry, §3.3) | Business-state validation needs server-side data; re-litigated by explicit user request. Marshaller spec shrinks to deserialization |
| `I{Context}Api` carries protobuf-net.Grpc decoration (#12) | **Removed** | Hosting spec rejected protobuf-net.Grpc (native stack); this spec conforms |
| Generic query-request family (prior-platform lineage) | **Direct repo calls in handlers** | Handlers are the plumbing; double dispatch is indirection without payoff |

New decisions: placement (§3.4, resolves persistence §17 #6) · **validators live in `Contracts`** — one validator runs in WASM/MAUI forms, Blazor Server, and the server pipeline; client runs are courtesy, the pipeline run is law (§3.3) · projection declarations live in `.Server` (the only compilation seeing both document and response types) with build-error on unmatched members (§4.2) · paging clamps + strict-single as platform guardrails (§4.1) · **raw `IMessageSession` in handlers — NServiceBus is the abstraction; no platform wrapper** with deterministic `MessageId` stamped by an NSB outgoing behavior (§5) · computed responses sanctioned (§5) · FluentValidation rejected for the validation seam (§3.3) · one `ErrorCategory` table — `Validation`/`NotFound`/`Conflict` — consumed by all doors (§7).

---

## 12. Cross-Spec Handoffs

- **Persistence spec:** §17 #6 marked resolved (documents → `.Backend`; requests/responses/validators → `Contracts`) — queued amendment. `AnyAsync`/`CountAsync` on `IDocumentRepository<T>` deferred until a concrete consumer (dragon-sizing).
- **Analyzer catalog (`Norse.Abstractions.Architecture`):** YGG401–408 proposed for ratification.
- **CLAUDE.md:** key-libs/Why-Not text updated — "source-generated mediator" becomes "martinothamar/Mediator + Norse.Abstractions.Mediator generator"; MediatR rejection rationale unchanged.
- **Marshaller follow-on spec:** scope shrinks to deserialization-shape concerns (per-scalar `Result<T>` parsing); business validation now lives here.
- **UI Composition spec:** the gRPC adapter's proto ↔ request/response mapping picks up the door contract from §7; the **client-side `Outcome<T>` rebuild** (§7 realm split) lands in `Norse.Hosting.Web.Client` / `Maui` so components stay channel-dumb — queued amendment there.
- **Messaging spec:** the platform endpoint defaults (§3 there) gain the outgoing `MessageId`-stamping behavior + the Abstractions `ResourceId` marker — queued amendment.
- **Future Mongo-update story:** worker-side document updates via Mongo update builders — explicitly not designed here.

## 13. Open Items

1. ~~**`ErrorCategory` vocabulary home**~~ **RESOLVED 2026-06-07 (punch-list §1.6).** The mediator owns its own `Outcome<T>` (§3.3), distinct from Primitives's conversion `Result<T>`. `ErrorCategory` trims to **`Validation` / `NotFound` / `Conflict`**. `Forbidden` left for service-entry authz (§3.3); `Unavailable` / `Internal` are host-synthesized transport conditions, never `Outcome` values (§7). Primitives's `Error` union collapsed to a `ParseFailure` reason (primitives §4.2).
2. **Lib version pin** — 3.0.x floor now; re-evaluate 3.1 when it leaves RC.
3. **Paging clamp values** (150/25, 100/5) — platform constants until operational evidence argues otherwise.
