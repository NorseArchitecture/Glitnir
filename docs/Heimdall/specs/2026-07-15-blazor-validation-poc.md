# Blazor Validation POC — Outcome&lt;T&gt; Door Mechanics, Mediator Reconciliation, Source-Generated Mapping

**Date:** 2026-07-15
**Status:** Scoped for POC — not a design, not for implementation until the POC reports back
**Owner:** Buvy
**Resolves (tracked) follow-up:** `Platform/specs/2026-07-13-protobuf-net-grpc-reinstated-design.md` §4 item 2 ("`Outcome<T>` ↔ protobuf-net.Grpc status mapping ... first realm to actually implement it (Heimdall) proves the mechanics").
**Trigger:** live design conversation 2026-07-14/15, continued after a night's sleep. Follows `Heimdall/plans/2026-07-13-authn-bootstrap-slice.md` Task 1 (shipped) and picks up mid-Task-2.
**Out of scope:** ErrorCategory vocabulary reconciliation (canonical 3-case vs. Asgard's shipped 6-case). `Outcome<T>` `Match`/sealed-hierarchy ergonomics upgrade. Both are independent decisions, not blocking, deliberately deferred.

> **Superseded note (2026-07-27):** every mention of martinothamar/Mediator below (as the "canonical" dispatch core, per §1.2/§2.2/§4) was never true — no `.csproj` on the platform ever referenced it, and this POC's §2.2 spike never happened. `2026-07-27-mediator-pipeline-retires-gateway-design.md` records the actual, hand-rolled dispatch core (`ISender` + `IBehavior<,>` fold). Left in place below as the historical record of what this POC believed at the time.
>
> The §1.1 amendment below (2026-07-25, "`[GenerateGateway]` ... `GatewayGenerator` emits the gateway at compile time") is also superseded — the gateway generator was deleted 2026-07-27; components inject `I{Context}Service` directly. Same reference, `2026-07-27-mediator-pipeline-retires-gateway-design.md`.

---

## 0. How to Use This Document

Written to be handed to a brand-new Claude Code context session with none of the last two nights' conversation in memory. §1 reconstructs the problem and cites the real shipped artifacts so the fresh session can verify everything against actual code rather than trust this document blindly. §2 is the three things the POC needs to produce evidence on. §3 is explicit non-goals. §4 is a suggested execution order. §5 is what "done" looks like.

This is a POC — throwaway spike code on an isolated branch/worktree, not a merge target. It produces a recommendation (and ideally the seed of a real design doc), not shipped product code.

---

## 1. Context (self-contained)

### 1.1 What's already shipped and working

- **Asgard** `Norse.Abstractions.Web.Server` v0.0.4 (github.com/NorseArchitecture/Asgard release v0.0.4, PR #25): `Outcome` / `Outcome<T>` / `Problem` / `ErrorCategory` / `ICommandRequest<T>` (declared, unused anywhere yet) / `IRequestHandler<TRequest,TResponse>`. `Outcome<T>` is a `sealed record` — `IsSuccess` bool discriminant + nullable `Value` + nullable `Problem`, `Ok()`/`Err()` as the only construction path, unattributed (no `[DataContract]`/`[DataMember]`) — server-only, in-process, never marshaled directly, by deliberate design.
- **Himinbjörg** `src/Identity.Web.Server/AuthenticationService.cs`: the gRPC-hosted service, protobuf-net.Grpc code-first (`[ServiceContract]`/`[OperationContract]`). Injects N raw `IRequestHandler<,>` handlers directly (there is no `IMediator` yet). Calls `.ThrowIfFailed()` per method, per handler call.
- **Midgard** `src/Infrastructure.Web.Server/Mediator/Grpc/`: `OutcomeExtensions.ThrowIfFailed` (throws the internal `OutcomeFailedException` when `Outcome.IsSuccess == false`), `OutcomeServerInterceptor` (a `Grpc.Core.Interceptor` catching `OutcomeFailedException`, converting to `RpcException` + a `problem-bin` trailer via `Problem.ToRpcException()`).
- **Midgard** `src/Infrastructure.Web.Client/Grpc/RpcExceptionExtensions.cs`: client-side `DecodeProblem()` — decodes the `problem-bin` trailer back into the error shape. WASM-safe; never references the server-only `Problem`/`ErrorCategory` types directly.
- **Yggdrasil** `src/Hosting.Web.Server/BlazorServerAuthenticationGateway.cs`: Blazor Server's `IAuthenticationGateway`. Injects the same raw handlers directly, gets `Outcome<T>` back, branches on `IsSuccess` inline, **never throws**. Hand-maps to `AuthenticationResult` (Heimdall's own DTO).
- **Yggdrasil** `src/Hosting.Web.Client/WasmAuthenticationGateway.cs`: WASM's `IAuthenticationGateway`. Wraps the real gRPC-Web client proxy, catches `RpcException`, calls `.DecodeProblem()`, hand-maps to the same `AuthenticationResult` shape.
- **Heimdall** `src/AuthN.Components/AuthenticationResult.cs` + `IAuthenticationGateway.cs`: the only things a Razor component ever reads. Never `Outcome<T>`, never a caught exception, never `IAuthenticationService` directly. `AuthenticationResult` = `Succeeded: bool` + `Errors: IReadOnlyDictionary<string,string[]>` + `DeferredCompletionUrl: string?` (realm-specific glue, unrelated to this POC).

Net effect today: the "component is dumb about transport" goal is **already achieved**, but by hand, once, for exactly one feature (login/register/logout). It does not yet generalize.

**Amendment (2026-07-25):** everything in this §1.1 bullet list describing `IAuthenticationGateway`/`AuthenticationResult`/`BlazorServerAuthenticationGateway`/`WasmAuthenticationGateway` was retired 2026-07-25 by Heimdall's `feature/transport-neutral-gateway` slice (merged, tag v0.0.3) — a more direct answer to this POC's §2.1 "generalize the shredder" question than the mapper-based approaches scoped below: `IAuthenticationService` carries Asgard's `[GenerateGateway]` attribute and `AuthN.Services`'s `GatewayGenerator` emits the gateway at compile time, returning `ValueTask<Outcome<T>>` directly — no per-feature `XxxResult` DTO, no hand-written shredder to generalize at all.

### 1.2 The platform's canonical, already-approved pattern this POC must conform to

`Platform/specs/2026-05-26-mediator-design.md` (approved, completed 2026-06-03) plus `Platform/specs/2026-07-13-protobuf-net-grpc-reinstated-design.md` (approved, carries the door table forward unmodified, only swaps which attributes decorate the wire) together already rule:

- **Dispatch core is [martinothamar/Mediator](https://github.com/martinothamar/Mediator), hybrid.** `Mediator.Abstractions` + `Mediator.SourceGenerator` (version floor 3.0) do dispatch — source-generated, `ValueTask`-based, Native-AOT-clean, no reflection. `Norse.Abstractions.Mediator` keeps the law (`[MediatorService]`, `ICommandRequest<T>`, `Outcome<T>`/`ErrorCategory`, the source generator) and `Norse.Infrastructure.Mediator` keeps the fixed pipeline behaviors. **This is "Key Rejections: MediatR → martinothamar/Mediator" in Glitnir's own CLAUDE.md — an already-decided platform default, not an open build-vs-buy question.**
- **Door table (mediator spec §7):** gRPC door — `Ok` → response, `Err` → `RpcException`-equivalent per `ErrorCategory`. In-process (Blazor Server) door — raw `Outcome<T>` value, **no throw, ever**. A client-side gRPC interceptor rebuilds the identical `Outcome<T>` from the wire, so the component "consumes `Outcome<T>` and is dumb about channel ... it never knows which transport carried it." Realm split: the server-side render table + door base classes live in Infrastructure (Midgard); the client-side gRPC rebuild lives in the hosting realm (`Norse.Hosting.Web.Client`/`Maui` — i.e. Yggdrasil, per this platform's naming).
- The 2026-07-13 reinstatement spec explicitly names the exact gap this POC fills, as tracked, non-blocking follow-up work: *"`Outcome<T>` ↔ protobuf-net.Grpc status mapping needs its own worked-out design once a real implementation (Heimdall's bootstrap slice) proves the shape ... not blocking — hand-mapping is fine for a first cut."*

**Conclusion the fresh session should not re-litigate:** whether the gRPC door throws/translates (decided: yes) versus returns `Outcome<T>` as a literal wire response (rejected implicitly by the same door table — not a live option). That fork is closed. What's actually open is narrower than it first looked.

---

## 2. What's Actually Open — Three Questions for the POC

### 2.1 Generalize the shredder (primary target)

Today, `Outcome<T> → AuthenticationResult` is hand-written once per gateway per feature — `BlazorServerAuthenticationGateway` and `WasmAuthenticationGateway` each independently construct `AuthenticationResult`. Confirmed working, but doesn't scale: every future ASP.NET Identity feature (profile, password reset, 2FA, etc.) would repeat this by hand, and each would need its own bespoke `XxxResult` DTO.

Build and evaluate a single generalized conversion — e.g. `Outcome<T>.ToComponentResult<TResult>(...)`, or a source-generated mapper (§2.3) — used identically by both gateway implementations, so the only thing that varies per feature is the shape of the component-facing DTO, never the conversion logic.

**Success criteria:** a second, different feature (pick one small unconverted ASP.NET Identity page — e.g. change-password or forgot-password) implemented against the generalized mechanism with materially less hand-written mapping code than `AuthenticationResult`'s current two gateways, and zero duplicated branching logic between the Blazor Server and WASM paths.

### 2.2 Reconcile Asgard's mediator with the canonical martinothamar/Mediator design

Asgard's shipped `IRequestHandler<TRequest,TResponse>` (Task 1, v0.0.4) doesn't yet implement `Mediator.Abstractions`' `IRequest<TResponse>`/dispatch surface, and `ICommandRequest<TResponse>` is an unused marker not yet aligned to the canonical spec's `ICommandRequest<T> : IRequest<Outcome<T>>`. Determine:

- Can Himinbjörg/Heimdall adopt `Mediator.Abstractions` directly — aligning `IRequestHandler<,>` to the library's own handler interface — without pulling in the rest of the canonical spec's product-realm machinery (NServiceBus, Mongo document repositories, `[Projection<,>]`, YGG401–408) that has no bearing on Identity? Confirm this against Glitnir's own CLAUDE.md §3: `Norse.Identity` / `Norse.AuthN` are listed as "cross-cutting platform services (not bounded contexts)" — meaning Identity/AuthN is exempt from the product-realm-specific parts of the mediator spec by design, not by oversight. Verify this reading is correct before building on it.
- What does `IMediator.HandleAsync<TRequest,TResponse>(TRequest, CancellationToken) where TRequest : ICommandRequest<TResponse>` — the zero-reflection, closed-generic-DI-resolution shape settled in the 2026-07-14/15 conversation — look like once it sits on top of the library's `ISender`, versus fully custom? Does `ISender.Send()` already give this for free, making a bespoke `IMediator` interface redundant?
- Does adopting the library's own source generator (`Mediator.SourceGenerator`) subsume any of the planned custom Norse generator work (emitting `AddNorseMediatorHandlers()`-style DI registration)?

**Success criteria:** a working `IMediator`-equivalent — whether a thin Asgard wrapper or the library's `ISender` used directly — dispatching at least two real Himinbjörg handlers, with a clear recommendation on how much custom Abstractions surface remains necessary versus how much the library now owns outright.

### 2.3 Evaluate a source-generated mapper for the shredder

Candidate: [Mapperly](https://github.com/riok/mapperly) (or scan alternatives). Note: the canonical mediator spec (§4.2) already evaluated Mapperly and parked it — but for a **different seam** (Mongo document → response-record queryable projections, which needs a raw `Expression<Func<TDocument,TProjection>>` that Mapperly's `IQueryable`-extension surface doesn't produce). That prior verdict is not a precedent against using it here: the seam this POC cares about (`Outcome<T>`/decoded-trailer → component Result DTO) is a plain object-to-object map with no expression tree required — arguably Mapperly's actual sweet spot, not the case it was rejected for.

Determine: does Mapperly (or the runner-up) cleanly handle `Outcome<T>`'s shape (bool discriminant + nullable `Value` + nullable `Problem.Errors` dictionary) mapping into an arbitrary `TResult` shape with `Succeeded`/`Errors`-style members? Is the generated code readable? Does it replace the hand-written conversion from §2.1 outright, or only part of it?

---

## 3. Out of Scope (explicit)

- No production code changes merge as a direct result of this POC — it produces a recommendation plus working spike code, feeding a real design doc and implementation plan afterward, per the platform's spec → plan → code discipline.
- ErrorCategory vocabulary reconciliation (§1.2's 3-case canonical vs. Asgard's shipped 6-case `Validation`/`NotFound`/`Conflict`/`LockedOut`/`InvalidCredentials`/`NotAllowed`) — flagged as a known follow-up, deliberately not part of this POC.
- `Outcome<T>` `Match`/sealed-hierarchy consumption-ergonomics upgrade — independent decision, not blocking.
- Re-litigating the wire-contract fork (§1.2) — closed; do not reopen without a fresh, explicit decision from Buvy.
- Any change to the anti-enumeration principle (`Heimdall/plans/2026-07-13-authn-bootstrap-slice.md` Task 2's Login/Register error-collapsing rules) — unrelated, must not regress.

---

## 4. Suggested POC Structure

A concrete ordered list the fresh session can execute directly, on a throwaway branch or worktree — nothing here merges as-is:

1. Spike the generalized shredder (§2.1) against the existing Login/Register/Logout `AuthenticationResult` shape first — this should be a pure refactor with no behavior change, provable against existing (or newly written) tests for `BlazorServerAuthenticationGateway`/`WasmAuthenticationGateway`.
2. Apply the same generalized shredder to one new, previously-unconverted ASP.NET Identity feature end-to-end (component → gateway → gRPC → handler and back) as the real test of "does this actually generalize."
3. In parallel or after, spike martinothamar/Mediator (§2.2) against Himinbjörg's existing handlers — swap `AuthenticationService`'s hand-written forwarding for real dispatch through the library, and confirm it composes cleanly with the generalized shredder from steps 1–2 (the mediator's return type is still `Outcome<T>`, so this should be additive, not conflicting).
4. Spike Mapperly (§2.3) against the shredder's mapping seam; compare generated code against the hand-written version side by side.
5. Write up findings as a short recommendation memo (or directly a design doc, if confident) covering all three questions. Save under `Heimdall/specs/` or `Platform/specs/`, depending on whether the shredder mechanism ends up Heimdall-specific or platform-general — the canonical door-table realm split in §1.2 suggests it belongs in Infrastructure/Yggdrasil, but confirm this as part of the writeup rather than assuming it.

---

## 5. Exit Criteria

This POC is done when the fresh session can answer, with working spike code as evidence:

1. What does the generalized `Outcome<T>` → component-Result conversion look like, and which realm/assembly does it live in?
2. Does Himinbjörg/Heimdall adopt martinothamar/Mediator directly, and if so, what does `IMediator`/`ISender` usage look like in a real handler dispatch?
3. Does Mapperly (or an alternative) get adopted for the shredder mapping, or is the hand-written conversion actually cleaner in practice?

The answers become the input to a real design doc and implementation plan for finishing the ASP.NET Identity component/backend-service conversion wave.

---

## References

- `Platform/specs/2026-05-26-mediator-design.md` — canonical mediator design (dispatch core, door table, `ErrorCategory` vocabulary).
- `Platform/specs/2026-07-13-protobuf-net-grpc-reinstated-design.md` — protobuf-net.Grpc as platform RPC stack; §4 names this POC's core question as tracked follow-up work.
- `Heimdall/plans/2026-07-13-authn-bootstrap-slice.md` — Task 1 (shipped) and Task 2 (wire contract, anti-enumeration principle) this POC continues from.
- `Heimdall/specs/2026-07-13-authn-identity-split-design.md` — Heimdall's own split design; first realm to implement the reinstated protobuf-net.Grpc stack.
