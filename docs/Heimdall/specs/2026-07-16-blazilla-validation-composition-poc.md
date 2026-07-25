# Blazilla Validation Composition POC — Sync, Async-Predicate, Async-Enrichment, and Server-Rejection, One EditContext

**Date:** 2026-07-16
**Status:** Scoped for POC — not a design, not for implementation until the POC reports back
**Owner:** Buvy
**Companion doc:** `Heimdall/specs/2026-07-15-blazor-validation-poc.md` — that doc covers the `Outcome<T>` ↔ Mediator ↔ gRPC door mechanics (server/transport side). This doc covers the client-side validation-composition mechanics (component side): how sync FluentValidation, async gateway-backed validation, and post-submit server rejection all coexist on one `EditContext` without the component ever knowing which transport carried the round trip. The two converge at the same seam (`AuthenticationResult.Errors` / `Outcome<T>.Problem.Errors`) but are independently spikeable and independently useful.
**Trigger:** live design conversation 2026-07-16, continuing directly from the 07-15 conversation after Buvy asked to generalize the hand-rolled `Login.razor` error-application pattern and separately confirmed a `MustAsync` gRPC-backed email-exists check was already on his mind, which converged with a forward-looking US-address zip→city/state lookup component.
**Out of scope:** the address-lookup component itself (a future-dated, unbuilt component — cited here only as motivating evidence that the async-validation mechanism generalizes past AuthN). `Outcome<T>`/Mediator reconciliation and Mapperly evaluation (companion doc's job). `ErrorCategory` vocabulary reconciliation. Any production code change — this is throwaway spike code.

---

## 1. Context (self-contained)

### 1.1 What's already proven, by hand, in real shipped code

- **`Heimdall/src/AuthN.Components.FluentUI/Login.razor`**: `<EditForm>` + Blazilla's `<FluentValidator />` (sync FluentValidation against `LoginRequestValidator`) + stock `<ValidationSummary />`. On submit, `HandleLoginAsync` calls `IAuthenticationGateway.Login(_request)`, and on failure hand-populates a **second, independent** `ValidationMessageStore` (owned by the component, not by Blazilla) from `AuthenticationResult.Errors`, then calls `_editContext.NotifyValidationStateChanged()`. `Register.razor` follows the identical pattern.
- **`Heimdall/src/AuthN.Components/AuthenticationResult.cs`**: `Errors: IReadOnlyDictionary<string,string[]>`, field-name-keyed, empty-string-key reserved for model-level errors — its own XML doc states this is deliberate, matching FluentValidation/Blazor's own convention "so both flow into the same ValidationSummary/ValidationMessageStore with no special-casing in the UI." The generalization opportunity is already recognized in writing; nothing has extracted it into a reusable mechanism yet.
- **`Asgard/src/Abstractions.Web.Server/Mediator/Outcome.cs`**: `Outcome<T>.Problem.Errors` is the same `IReadOnlyDictionary<string,string[]>` shape, one level further back in the pipeline (server-only, pre-gateway-mapping).

**Amendment (2026-07-25):** `IAuthenticationGateway`/`AuthenticationResult` were retired 2026-07-25 by Heimdall's `feature/transport-neutral-gateway` slice (merged, tag v0.0.3). `IAuthenticationService` now carries Asgard's `[GenerateGateway]` attribute and the generated gateway returns `ValueTask<Outcome<T>>` directly, so `Login.razor`'s error-handling seam is now `Outcome<T>.Problem.Errors` itself, not a hand-maintained `AuthenticationResult.Errors` copy — the two bullets above describing separate shapes now describe the same one.

### 1.2 What this session confirmed by decompiling the actual libraries (not just reading docs)

**Blazilla 2.4.0** (`~/.nuget/packages/blazilla/2.4.0`, decompiled via `ilspycmd`):
- `FluentValidator.cs`: `ValidationMessageStore _messages` is `private`; the only method that writes to it, `ApplyValidationResults(...)`, is `private`. Public surface is exactly `Validator`, `RuleSets`, `Selector`, `AllRules`, `AsyncMode` — all governing how Blazilla runs *its own* pass, none accepting an externally-produced `ValidationResult`/`ValidationFailure` list.
- `EditContextExtensions.cs`: `Validate(ruleSets)` / `ValidateAsync(ruleSets)` trigger `editContext.Validate()` and await Blazilla's own pending async task (stashed in `EditContext.Properties["__FluentValidation_Task"]`); neither accepts outside results.
- **Conclusion: there is no supported or hidden hook to inject a server-produced `ValidationResult` into Blazilla's internal store.** This rules out "translate `Outcome<T>.Problem` into a `FluentValidation.ValidationResult` and hand it to Blazilla" as a mechanism — it isn't a design choice to weigh, it's not available.
- **This also confirms why `Login.razor`'s pattern is the sanctioned path, not a workaround**: `ValidationMessageStore` is a standard Blazor primitive and `EditContext` supports multiple independent stores coexisting — Blazilla's private internal store and the component's own store both feed the same `<ValidationSummary>`/`<ValidationMessage>` via `EditContext.GetValidationMessages()`, which aggregates across every store registered against it.

**FluentValidation 12.1.1** (`~/.nuget/packages/fluentvalidation/12.1.1`, decompiled):
- `MustAsync<T,TProperty>(Func<T, TProperty, ValidationContext<T>, CancellationToken, Task<bool>>)` — bool-only contract. FluentValidation's engine only inspects the returned bool; anything else fetched inside the closure is discarded by the framework unless the closure itself persists it (e.g. by writing onto the captured model instance).
- `CustomAsync<T,TProperty>(Func<TProperty, ValidationContext<T>, CancellationToken, Task>)` — decompiled implementation shows it is literally `MustAsync` with a delegate that always returns `true` to the engine; failure is entirely opt-in via `context.AddFailure(...)` inside the action.
- `ValidationContext<T>.InstanceToValidate` (settable model reference) and three `AddFailure` overloads (`AddFailure(ValidationFailure)`, `AddFailure(propertyName, message)`, `AddFailure(message)`) confirmed present on `ValidationContext.cs`.

---

## 2. What's Actually Open — Four Questions for the POC

### 2.1 Generalize post-submit server-rejection application (primary target)

Extract `Login.razor`/`Register.razor`'s hand-rolled clear/populate/notify sequence into one reusable mechanism, callable identically regardless of which gateway (Blazor Server in-process, WASM gRPC, future MAUI gRPC) produced the result.

Two shapes to spike side by side:

- **(a) `EditContext.ApplyServerErrors(IReadOnlyDictionary<string,string[]> errors, string? fallbackMessage = null)` extension method.** Explicit call site (`_editContext.ApplyServerErrors(result.Errors)`), owns its own `ValidationMessageStore` internally (created once, cached), mirrors the `Login.razor` pattern exactly but removes the per-feature boilerplate (the `ValidationMessageStore` field, the `OnInitialized` wiring, the clear/loop/notify dance). Simplest, most explicit, easiest to unit test in isolation.
- **(b) A small wrapper component** (e.g. `<FluentServerValidation @ref="_serverValidation" />`) placed alongside `<FluentValidator />` inside the `EditForm`, exposing an `ApplyAsync(IReadOnlyDictionary<string,string[]>)` method via `@ref`. Removes even the extension-method call-site boilerplate at the cost of one more thing living in the render tree.

**Success criteria:** a second, previously-hand-written feature (pick change-password or forgot-password, whichever has real errors to surface) reimplemented against the chosen mechanism with materially less code than `Login.razor`'s current ~20-line `HandleLoginAsync`, and zero duplicated `ValidationMessageStore` plumbing between the Blazor Server and WASM paths.

### 2.2 Confirm the direct-injection option is genuinely unavailable, not just unspiked

Already answered in §1.2 by decompilation — recorded here as a closed question so a fresh session doesn't re-litigate it. **Do not attempt to route `Outcome<T>.Problem` through Blazilla's own `FluentValidator` internals.** The mechanism does not exist in the shipped 2.4.0 binary.

### 2.3 Prove `MustAsync` for pure async predicate checks

Spike an async, gateway-backed uniqueness-style check (e.g. "is this email already registered" — a real, useful check for `Register.razor` today, not hypothetical) using:
```csharp
RuleFor(r => r.Email).MustAsync(async (request, email, ct) =>
    !await gateway.EmailExistsAsync(email, ct)).WithMessage("This email is already registered.");
```
with `<FluentValidator AsyncMode="true" />`, `<EditForm OnSubmit="...">` (not `OnValidSubmit`), and `editContext.ValidateAsync()` per Blazilla's documented pattern.

**Wire shape — reconsidered, settled on the uniform pipeline, not a shortcut:** a bodyless status-only route (`202`/`404`, no body) was the first instinct, but it was rejected: it would be a *second* transport pattern sitting beside the already-proven `Outcome<T>`/gRPC door table (`Platform/specs/2026-05-26-mediator-design.md` §7, "one table, three doors"), and every host would need its own bespoke implementation of it — WASM/MAUI building and calling an actual URL, Blazor Server skipping the network and calling in-process — duplicating the exact per-host divergence the door table already solved once, just for this one check. That's more platform complexity for a marginal serialization saving on a single boolean, not less.

Settled shape instead: route this through the **same** Mediator → `Outcome<T>` → gRPC pipeline as everything else, wrapping Asgard's existing `BoolResponse` (`Outcome.cs`, `{ required bool Value }` — already shipped, already the platform's answer for "handler whose only signal is a bool"). One door table, one gateway abstraction, no second wire pattern to maintain. The cost is ordinary protobuf serialization overhead on a boolean — negligible next to the complexity of a parallel transport mechanism.

**Success criteria:** the check runs through the same `IAuthenticationGateway`-shaped gateway abstraction, returning `Outcome<BoolResponse>` end to end, with zero host-specific wire-building code — confirming the wire shape stays uniform across §2.3 and §2.4 (both ride the same pipeline; only the response payload richness differs, `BoolResponse` vs. a real view model).

### 2.4 Prove `CustomAsync` for async lookup-with-payload-and-conditional-failure

Spike a throwaway zip→city/state lookup rule (standing in for the future address-form component, not building it) demonstrating the pattern this session converged on:
```csharp
RuleFor(a => a.PostalCode).CustomAsync(async (zip, context, ct) =>
{
    var location = await locationService.GetByZipAsync(zip, ct);
    if (location is null)
    {
        context.AddFailure(nameof(Address.PostalCode), "Zip code not found.");
        return;
    }
    context.InstanceToValidate.City = location.City;
    context.InstanceToValidate.State = location.State;
});
```

**Wire shape — same pipeline as §2.3, richer payload:** this check has real data to carry back (`City`/`State`), so it rides the same Mediator → `Outcome<T>` → gRPC door table, just wrapping a real view model instead of `BoolResponse`. Inject the real gRPC-backed service directly (e.g. `ILocationService`, contract TBD — not built in this POC), get back a deserialized view model, and evaluate/write inside the rule. One uniform wire pattern across both async-validation cases (§2.3, §2.4); only the response payload shape varies with what the check actually needs to communicate.

**Success criteria:** confirm the model mutation (`City`/`State` write-back) is visible to the rest of the form after `ValidateAsync()` completes and triggers a re-render (`StateHasChanged`/`NotifyValidationStateChanged` timing), and that this composes cleanly alongside a `MustAsync` rule in the same validator without ordering surprises.

---

## 3. The Transport-Dark Requirement

Every mechanism in §2 must hold under the same constraint already proven for AuthN: **the component and the validator never know whether the gateway underneath is Blazor Server (in-process call, no network), WASM (gRPC-Web), or MAUI (native gRPC channel).** The gateway interface is the transport boundary; everything above it — `EditForm`, `FluentValidator`, `MustAsync`/`CustomAsync` rules, `ApplyServerErrors` — is identical code regardless of host. This is not a new requirement invented for this POC; it is `IAuthenticationGateway`'s existing contract, extended to cover validation-time async calls in addition to submit-time calls.

---

## 4. Suggested POC Structure

Self-contained sandbox under `Glitnir/poc/blazilla-validation-composition/` — its own throwaway solution, real `Blazilla`/`FluentValidation` NuGet references, no `NorseRef` on any live realm (mirrors `poc/pg19-temporal`/`poc/build` precedent: README + harness + `FINDINGS.md`, nothing here merges as-is).

1. **Baseline** — a minimal `EditForm` + `<FluentValidator />` + sync `AbstractValidator<T>`, proving nothing regressed from the documented happy path.
2. **§2.1** — implement `ApplyServerErrors` (both shapes (a) and (b)), backed by a fake async "server" call returning a `Problem`-shaped `IReadOnlyDictionary<string,string[]>` after a simulated delay. Compare the two shapes side by side; recommend one.
3. **§2.3** — `MustAsync` email-exists spike against a fake gateway.
4. **§2.4** — `CustomAsync` zip-lookup spike against a fake gateway, confirming model write-back + re-render timing.
5. **Transport-dark acid test** — host the *same* RCL component, unmodified, from two real host projects in the sandbox (a Blazor Server host and a Blazor WASM host), each wired to a differently-implemented fake gateway (in-process call vs. simulated HTTP fetch). This is the literal proof of §3, not an inference from it.
6. Write up findings in `FINDINGS.md`: which of (a)/(b) wins for §2.1, confirmation that §2.2 stays closed, and where the recommended `ApplyServerErrors` mechanism should live (Asgard, alongside `Outcome<T>`, since this is fundamentally "wire error shape → Blazor validation primitive," not a hosting concern — confirm or refute as part of the writeup rather than assuming).

---

## 5. Exit Criteria

This POC is done when the fresh session can answer, with working spike code as evidence:

1. What does `ApplyServerErrors` (or its winning shape) look like, and which assembly does it live in?
2. Is `MustAsync` sufficient for pure async predicate checks, confirmed against a real gateway-shaped call?
3. Is `CustomAsync` the right mechanism for async-lookup-with-payload, confirmed against a real model write-back, and does it compose cleanly with `MustAsync` rules in the same validator?
4. Does the transport-dark acid test (§4.5) actually hold — same component, two hosts, zero component-side transport awareness?

The answers become input to a real design doc (and, combined with the companion 07-15 doc's `Outcome<T>`/Mediator findings, an implementation plan) for finishing the ASP.NET Identity component/backend-service conversion wave.

---

## References

- `Heimdall/specs/2026-07-15-blazor-validation-poc.md` — companion doc, server/transport side of the same conversion wave.
- `Heimdall/src/AuthN.Components.FluentUI/Login.razor`, `Register.razor` — the hand-rolled pattern this POC generalizes.
- `Heimdall/src/AuthN.Components/AuthenticationResult.cs` — the `Errors` shape both this and the companion doc build on.
- `Asgard/src/Abstractions.Web.Server/Mediator/Outcome.cs` — `Outcome<T>`/`Problem`, the server-side origin of the same error shape.
- Blazilla 2.4.0 (`github.com/loresoft/Blazilla`) and FluentValidation 12.1.1 — decompiled directly for this doc; no source package exists for Blazilla, so findings in §1.2 are grounded in the shipped binary, not documentation alone.
