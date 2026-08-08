# Wire-Stamped Request Scalars — the Credentials Request Family

**Date:** 2026-08-08 · **Status:** draft, revision 3 — first draft remanded on independent review (six findings, discharged in rev 2); round two added two findings and a scoping cleanup, incorporated below. Awaiting greenlight to plan.
**Realms touched:** Svartálfheim (doctrine only) · Midgard · Heimdall · Himinbjörg · Bragi · Yggdrasil (pins)

## 1. Purpose and provenance

Complete the two-unions design on the request side: `Result<T>` members become the *serialized*
fields of wire requests, raw form input becomes a non-serialized binding buffer, and validators
examine the stamp instead of re-validating the string. The current `RegisterRequest` (raw string
serialized, `Result<EmailAddress>` cached unserialized via the setter) was the deliberately-smaller
first increment — it proved deserialization-is-the-parse-event through plain property semantics,
drove the async validation story, and flushed the gRPC interceptor bugs while the payload stayed
boring. This spec is the second increment it earned.

Prior art, in-tree: `Midgard/src/Infrastructure.Web.Grpc` (`ResultSerializer{T}`,
`ResultSerializers`, `WireModelRegistrationGuard`) ships `Result<T>`-on-the-wire for the closed BCL
scalar taxonomy — success unwraps to the scalar's own native encoding, and *"a failed or default
Result<T> is illegal to write"* is one shared literal across the gRPC/JSON/XML legs. Plans:
`../plans/2026-08-06-blocking-surrogate-guard.md`, `../plans/2026-08-06-wire-model-registration-guard.md`.
Doctrine: `../../the-two-unions.md`, including the 2026-08-08 "inverse law" amendment.

## 2. The target shape

```csharp
/// <summary>Deliberately mutable — Blazor two-way binding requires settable properties.</summary>
[DataContract]
public sealed record RegisterRequest // base-vs-flat is Increment B's spike verdict, §5
{
	/// <summary>The email address for the new account.</summary>
	[DataMember(Order = 1)]
	public Result<EmailAddress> Email { get; set; }

	/// <summary>The form's raw buffer — never serialized; assignment stamps Email.</summary>
	public string EmailInput
	{
		get;
		set { field = value; Email = EmailAddress.Parse(value); }
	} = string.Empty;

	/// <summary>The password for the new account.</summary>
	[DataMember(Order = 2)]
	public string Password { get; set; } = string.Empty;
}
```

## 3. Decided law (converged in session; revised on remand — carry into the plan verbatim)

1. **The stamp is the serialized member; the buffer is not.** `Result<T>` fields carry
   `[DataMember]`; `*Input` string buffers are unserialized binding targets whose setters stamp on
   every assignment. Naming: the domain name goes to the domain-typed member (`Email`), the buffer
   is named for what it is (`EmailInput`).
2. **Buffers are client-side artifacts with honest server semantics.** The sanctioned
   deserialization path never assigns a buffer — so buffers carry no `required` modifier and no
   non-null promise the server would violate; they default to `string.Empty` and their
   client-vs-server lifecycle is documented on the member. Round-trip tests inspect both the stamp
   (populated) and the buffer (default) after server-side deserialization.
3. **No failure arm on the wire.** Already law (`IllegalWriteMessage`); the validator gate makes a
   failure at the marshaller unreachable on the sanctioned path, the serializer throw is the
   tripwire. Deserialization re-stamps through the platform's one parsing door; the server-side run
   of the identical validator converts a server-materialized failure to `Failed(Problem)` before
   any handler executes. `Problem` never flows client → server.
4. **The validator examines the stamp; field identity binds the rule.** The business content of
   validation runs against the `Result<T>` (decompose the union; on `Success`, business rules chain
   against the proven domain struct — the parser owns format truth, the validator owns business
   truth, no rule exists twice). But rule *registration* must honor field identity: Blazilla's
   field-change pass selects rules with a bare `MemberNameValidatorSelector` (member name of the
   changed field), and `ServerErrorCoordinator` applies `Problem.Errors` keys to bound field names.
   The changed/bound field is the buffer, not the stamp. Two admissible shapes, settled by the
   Increment B spike's component tests, not by preference:
   - rules registered against `*Input` whose predicates examine the corresponding stamp; or
   - rules registered against the stamp with a proven `Email ↔ EmailInput` name mapping spanning
     all three consumers: Blazilla rule selection, validation-result property names, and
     server-error application (`OverridePropertyName` alone is not accepted without proving
     field-change selection).
   Either shape ships with the component-test lock: blur executes the rule, inline message renders
   beside the field, the async email lookup still fires, a server error renders beside the field,
   and editing the field clears it. Server handlers key `Problem.Errors` by the *bound field name*
   as chosen here — `RegisterHandler`'s current `Email` keys follow the ruling.
5. **Cardinality rides the type system.** Non-nullable `Result<T>` is a required field;
   `Result<T>?` is optional. Absent-on-wire → `default(Result<T>)`, caught by `ResultRules`
   (existing, spec §9.3 series). Every field in the roster (§6) declares its cardinality
   explicitly — nothing becomes mandatory by accident of non-nullability.
6. **Handlers receive stamps; the success projection is explicit law.** A validated
   `Result<T>` is still a union at compile time — validation does not refine the type — and PII
   scalars deliberately mask `ToString()`, reserving plaintext for `WireValue`. The plan specifies
   one sanctioned post-gate projection (an explicit `TryGetValue` prologue, a validated-request
   projection type, or a narrowly named invariant helper — Open Decision 7) and requires
   deliberate `.WireValue` egress at the Identity boundary. Tests prove `UserManager` receives the
   canonical unmasked email, and that a non-success stamp cannot reach it (the equivalent phone
   assertion rides the deferred names/phone increment, §6).
7. **`RegisterResult.Succeeded` gets the 2026-08-06 treatment.** Observable in exactly one state;
   the flag predates the union migration the same way `LoginResult.Succeeded` did. Replacement
   shape is Open Decision 3.
8. **The validator extension's shape** is proven prior art, ported to the union: a C# 14
   `extension` block on `AbstractValidator<TRequest>` exposing `ValidateResult<T>` —
   `Cascade(Stop)` so a parse failure suppresses downstream business-rule noise, a rule-builder
   return so business rules chain after the success gate, and a nullable overload encoding
   cardinality (null passes; present-but-failed fails). What changes in the port: decomposition
   rides the union, registration honors field identity per law 4, and message rendering obeys the
   privacy invariant (Open Decision 6).

## 4. Increment A — PII rows in the wire law, ALL THREE LEGS (Midgard)

The wire law is one law on three legs; extending the taxonomy to the forge's PII scalars
(`EmailAddress`, `PersonalName`, `PhoneNumber`, `BirthDate`) means all three, with tests per leg:

- **protobuf** — new serializer rows beside `ResultSerializer{T}` (the `DateTimeOffset`
  string-fallback branch is the template): wire form is the scalar's sanctioned egress
  (`WireValue`), read funnels through `T.Parse` so a malformed value produces the typed `Failure`,
  never a throw; failed/default write throws `IllegalWriteMessage`. Registration + guard coverage
  (`WireModelRegistrationGuard`).
- **JSON** — `ResultJsonConverterFactory` claims every non-enum `Result<T>` and constructs
  `ResultJsonConverter<T>`, which is constrained to `ISpanParsable<T>`; a PII member today fails at
  converter construction, at runtime. The factory gains PII-aware routing to a PII converter
  variant (`WireValue` out, `Parse` in), same illegal-write law.
- **XML (Futhark)** — the generator's scalar taxonomy is BCL + enums only; a PII struct would be
  walked as a complex object while `ReaderEmitter` assumes `Parser.ParseRequired<T>` for scalar
  results. The taxonomy, closure analysis, reader/writer emission, and diagnostics all gain the
  PII rows.

PII scalars implement no `ISpanParsable` deliberately (their `Parse` returns `Result<T>`); the hook
generalization (per-type registration vs. an `IPiiScalar<TSelf>`-shaped row) is Open Decision 1 and
must answer for all three legs at once.

## 5. Increment B — the request shape (Heimdall) — SPIKE FIRST, ALL TRANSPORTS

The shared-base question is not protobuf's alone. Futhark's contract law today makes the base shape
illegal on the XML leg twice over: reachable contracts must be `sealed` **and** derive directly
from `object` (`ClosureWalker` emits `InvalidContractShape` otherwise). A protobuf-only inheritance
spike therefore cannot approve the base-record shape. The spike covers protobuf, JSON, and Futhark
together, and its verdict selects among:

- **Flat inheritance** (base carries serialized stamps + buffers) — requires a deliberate Futhark
  law amendment (walk base members; keep the sealed-leaf rule) and protobuf-net verification that
  base members serialize flat without `ProtoInclude` nesting;
- **Generated inclusion** — no inheritance on the contract types; a source generator emits the
  shared members into each leaf (contracts stay sealed-and-object-rooted; sharing is authorship-
  time, not type-system);
- **Composition** — leaves declare their own members; shared stamping machinery lives in a
  non-contract helper the setters call.

**Futhark exposure scope, stated precisely:** the spike exercises *synthetic fixtures only* to
prove the PII-scalar and inheritance mechanics — the AuthN contracts remain unexposed to Futhark.
A real `LoginRequest` in a Futhark request closure would convict `Password` and `RememberMe` under
NORSE022 ("request scalars wrap in `Result<T>` or `Result<T>?`") today; whether those members wrap,
NORSE022 gains a carve-out, or AuthN simply never enters an XML closure is a ruling this spec
deliberately does not make — it becomes due the day an AuthN contract first enters a Futhark
closure, and is recorded here so that day doesn't arrive unnoticed.

Decide on spike evidence, not preference. Base name, if a base survives, is Open Decision 2
(working name `CredentialsRequest`).

## 6. Field roster — each field carries a cardinality AND lifecycle ruling

**This increment reshapes the fields that already have a full lifecycle:**

| Record | Member (wire order) | Cardinality | Lifecycle today |
|---|---|---|---|
| `LoginRequest` | `Result<EmailAddress> Email` (1), `Password` (2), `RememberMe` (3) | Email required | Complete — sign-in only |
| `RegisterRequest` | `Result<EmailAddress> Email` (1), `Password` (2) | Email required | Complete — `NorseUser` email, disclosure, erasure all exist |
| `EmailExistsRequest` | `Result<EmailAddress> Email` (1) — stamped, **no buffer** | required | Complete |

**`EmailExistsRequest` rides the chain, and that dissolves its whole problem.** It is not a
form-bound record, so it gets no `*Input` buffer — it carries the stamp alone. On the sanctioned
path, the async existence lookup in `RegisterRequestValidator` *chains after* the
`ValidateResult(x => x.Email)` success gate (`Cascade(Stop)`), so the lookup structurally cannot
fire on an unproven stamp, and the request is constructed by passing `RegisterRequest.Email`'s
already-proven `Result<EmailAddress>` through verbatim — no re-parse, no second format authority.
The gate doubles as an invalid-input traffic filter: mid-typing keystrokes re-stamp as `Failure`,
so the one rule that costs a server round trip fires only for structurally valid emails — partial
or garbage input never generates network traffic or a database query, with no timer, no throttle
state, and nothing to tune. (This is a validity gate, not a debounce: successive *valid* values
can each trigger a lookup; temporal coalescing comes from `FluentTextInput` binding on
change/blur, pinned in `AuthN.Components.FluentUI.Tests`.) The rule *order* is therefore
load-bearing; a refactor that lifts the lookup ahead of the gate reintroduces per-keystroke
traffic and must not survive review.
On the hostile path (the operation is independently callable public wire surface), the standard
law closes it with no bespoke machinery: deserialization re-stamps, a one-line
`ValidateResult(x => x.Email)` server validator (`IValidator<EmailExistsRequest>` — the generated
`CommandRequestValidator` adapter already wires any validator that exists) converts a
malformed/default stamp to `Failed(Problem)`, and the handler projects deliberate `.WireValue` to
`FindByEmailAsync`. Tests: a malformed or default request never reaches `UserManager`.

**Names and phone are explicitly deferred out of this increment.** `NorseUser` has no first/last
name members, `RegisterHandler` creates users from email/password only, and the disclosure path
exposes email/phone only — collecting a name today would transmit PII with no owner, no retention
ruling, and no erasure path: a product defect and needless collection. They land in a follow-on
increment that specifies, per field: entity columns and EF mappings (both providers), migrations,
encrypted-personal-data treatment and `[RetentionPolicy]`, registration assignment, disclosure/
masking/erasure behavior, explicit cardinality (`Result<PhoneNumber>?` unless phone is *ruled*
mandatory — nothing is mandatory by accident), and tests end to end. The stamped-scalar pattern
this spec proves on email is exactly what that increment reuses.

`Password` stays `string` — no domain struct exists for it today; if one lands it enters through
this same pattern.

## 7. Blast radius and sequencing (two-train pattern per ship gate)

1. Midgard: Increment A, all three legs + guards + per-leg tests (train 1).
2. Heimdall: the all-transport spike, then the chosen shape + reshaped records (`EmailExistsRequest`
   goes stamped-no-buffer) + validator retarget under the field-identity law with the existence
   lookup chained after the success gate (the `ValidateResult` extension lands here first; a
   platform-wide home is Open Decision 4) + the one-line `EmailExistsRequest` validator +
   `RegisterResult` execution + `RequestContractTests` extension + the component-test lock from
   law 4 (train 2, consumes train 1).
3. Himinbjörg: handlers adopt the ruled success projection with deliberate `.WireValue` egress
   (`EmailExistsHandler` included); `Problem.Errors` keys follow the field-identity ruling
   (train 3).
4. Bragi: fake + stories follow the new shape; sentinel semantics unchanged (train 3 sibling).
5. Yggdrasil: CPM pins ride the trains; no code.

## 8. Open decisions — RULED 2026-08-08 (unattended run, standing authorization; every ruling
reviewable in the morning PRs)

1. **Generalized `IPiiScalar<TSelf>` row.** The seam already carries `static abstract
   Result<TSelf> Parse(ReadOnlySpan<char>)` and `WireValue` — one generic serializer type per leg,
   with closed per-type registrations (open-generic registration is proven non-functional,
   `ResultSerializers` remarks).
2. **Flat sealed records — no base class this increment.** Futhark's sealed-and-object-rooted
   contract law stands unamended; the shared wrapper is four lines per field, which does not
   justify a law change or a generator tonight. The base/generated-inclusion question revisits
   when a bordereaux-class consumer makes the economics real. The inheritance spike dissolves —
   nothing inheritance-shaped ships.
3. **`RegisterResult` becomes a bare `[DataContract]` record.** The success signal *is* the
   `Outcome` envelope; no registration next-hop exists today (email confirmation is unbuilt).
   `NextUrl` enters when that flow does.
4. **`ValidateResult` extension lives in `AuthN.Components`** (sole consumer today).
5. **Clean-break renumbering sanctioned** (contracts are pre-GA): `LoginResult.NextUrl` → Order 1;
   no ghost orders reserved.
6. **Messages: per-scalar safe text, no input interpolation, ever** — "Enter a valid email
   address." — with tests asserting raw input never appears. Localization rides the platform's
   overall (deferred) l10n posture; en-US literals for now.
7. **Success projection: explicit `TryGetValue` prologue in handlers.** A named invariant helper
   is deferred until a third consumer exists.
8. **Field identity: shape A.** Rules register against the `*Input` buffers (the bound fields)
   with predicates examining the corresponding stamp; server `Problem.Errors` keys use the bound
   field names. `EmailExistsRequest` (no buffer, no form) registers against its stamp directly.

## 8a. Superseded open-decision list (pre-ruling, kept for the record)

1. PII serializer hook: per-type registrations vs. a generalized `IPiiScalar<TSelf>` row — ruled
   once for all three legs.
2. Base/flat/generated/composed request shape (spike-informed), and the base name if one survives.
3. `RegisterResult` replacement shape: bare record, or server-resolved `NextUrl` for
   registration's own next hop (email-confirmation page?) mirroring `LoginResult`.
4. Platform home for the `ValidateResult` extension once a second consumer appears (today:
   `AuthN.Components`).
5. Wire `Order` renumbering policy for the reshaped records — the contracts are pre-GA; is a clean
   break sanctioned, or do ghost orders (`LoginResult`'s Order 2) stay reserved?
6. Failure-message rendering — **the privacy invariant comes first, localization second:** PII
   validation messages never interpolate raw input (the shared renderer interpolates
   `Failure.Input` today; PII parsers record empty input, which would render as
   `cannot parse ''` — unhelpful AND one refactor away from a leak). Define how `Reason` + safe
   format/detail become useful localized text, with tests proving email, phone, names, and birth
   dates never appear in validation messages or logs. Must be answered before the extension ships.
7. The sanctioned success projection for handlers (law 6): explicit `TryGetValue` prologue vs.
   validated-request projection type vs. named invariant helper.
