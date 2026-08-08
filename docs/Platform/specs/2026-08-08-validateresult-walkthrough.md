# ValidateResult — the Walkthrough (square one)

**Date:** 2026-08-08 · **Status:** in session — recorded live, step by step
**Provenance:** the crooked path, entry #13. The prior validator design work (wire-stamped-request-
scalars spec §8 ruling 8 and every validator edit from the unattended run) is void; this walkthrough
supersedes it. The architect drives every step; the AI records and executes exactly what is said.
**Ground rule:** nothing in this document is agent-inferred design. Each step is recorded as given,
the action taken is shown, and the record moves only when the architect says it moves.

---

## Steps

### Exhibit A — the receipt (architect's opening move)

The architect read the session transcript back verbatim: the ruling ("validators need to target the
`Result<T>` so they can run business validation against the actual BCL struct instead of a string,
decomposing the failure for on-blur feedback") was given, acknowledged in the AI's own words
("`RuleFor(x => x.Email)` against the `Result<EmailAddress>` itself… the parser owns format truth,
the validator owns business truth, no rule is ever written twice"), and captured in the spec as
decided law — **and then the unattended run's ruling 8 chose the reviewer's buffer-registration
shape anyway.** The failure on record: not comprehension — fidelity. The ruling was understood,
celebrated, written down, and inverted in execution.

Standing from Exhibit A: the ruling as originally given governs everything that follows in this
walkthrough. Business rules run against the parsed domain struct. The failure decomposes into the
validation display. The buffer is never the rule's target.

### Step 1 — the original cleanup, walked (request/response objects for register, login, logout)

The architect's steps, as given, each executed and shown:

1. **The slate:** stamped requests per the architect's own sketch — `Result<EmailAddress> Email`
   as the `[DataMember]`, `required string EmailInput` buffer whose setter stamps on every
   assignment (with `string.Empty` default so the server path violates no non-null promise),
   `Password` plain, `EmailExistsRequest` stamped with **no buffer** (not form-bound; the async
   rule passes the proven stamp through verbatim).
2. **"Thunk it up into one result — the success of any of them is you got a NextUrl":** the three
   response records (`LoginResult`/`RegisterResult`/`LogoutResult`) deleted; every issuance
   operation returns one shape.
3. **"NextUrl is more Æsir than you spec'd":** the unified record is *platform* vocabulary — the
   wizard case: a form presents a question and the answer governs the next step; only the server
   knows the map. `NextUrlResult` lands in Asgard's `Abstractions.Contracts` beside `BoolResponse`
   and `Unit` (the gRPC saga: `CodeRequest`/`IdResult`/… minted on first real consumer, never
   speculatively). Heimdall now declares **zero** response records.
4. **Validators register on the stamp** (Exhibit A's standing): predicates read the parsed
   verdict, the async lookup receives the proven `Result<EmailAddress>` itself, the
   `EmailAddress()` regex (a second format authority) is deleted, `WithName` carries the buffer's
   name for display only — `PropertyName` and server error keys stay `Email`, wire-stable.
5. **Field identity = `StampFieldBridge`:** a headless component inside the `EditForm` echoes
   every buffer change (`XInput`) as its stamp's change (`X`), so Blazilla's name-matched blur
   pass runs the stamp's rules. The convention (`X` + `Input`) is the contract; the future
   request-buffer source generator owns the mapping end to end.

**Exit gate met:** Heimdall 63/63 green — including the pre-existing bUnit blur test, which now
proves the bridge mechanism rather than assuming it. Asgard 93/93 with `NextUrlResult` in the
saga. Consumers (Himinbjörg handlers, Bragi fake/stories, Yggdrasil pins) are the next trains,
deliberately not this step.

### Step 2 — Himinbjörg matched to the new gate contract (architect's step: "fix Himinbjörg to
match"; Bragi runs in its own dedicated `UseProjectReferences=false` thread)

1. **Handlers hold only proven values:** `LoginHandler`/`RegisterHandler`/`EmailExistsHandler`
   gained the `TryGetValue` prologue with deliberate `.WireValue` egress into Identity's string
   store. Each prologue's fallback is domain-honest: login collapses an unproven stamp into the
   shared anti-enumeration `_invalidCredentials`; register returns Validation keyed to the wire
   field; email-exists answers "not taken" (sugar over a racy lookup — the register conflict is
   the authority).
2. **`NextUrlResult` everywhere:** all three commands and handlers return the Æsir shape.
   Register's next hop moved server-side (`/Account/Login` today, the confirmation page when that
   flow lands); logout folds deferred-completion-or-root into one unconditional hop — the client
   null-branch died with the old record.
3. **The hostile path closed:** `EmailExistsRequestValidator` (one rule, stamp-must-be-success)
   lands in `Identity.Web.Server` and is picked up by the generated
   `CommandRequestValidator` adapter by discovery — the last unstamped public surface now converts
   hostile input to a failed outcome before any handler runs.
4. **Error keys stay wire-stable at `Email`** — `RegisterHandler`'s field mapping untouched.

**Exit gate met:** Himinbjörg 137/137 green, real-Postgres and SqlServer integration suites
included. Staged, not committed.

*(awaiting the next step)*
