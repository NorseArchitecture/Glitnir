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

### Step 3 — the bridge folds into the base (architect's step: "mechanics without presentation")

`StampFieldBridge` — a markup-less `ComponentBase`, a tag someone must remember to place — is
deleted. The mechanic moves into `OutcomeFormComponentBase` itself: the base owns the
`EditContext` (`EditContextFor(request)` — created once, echo subscription wired at creation),
forms bind `<EditForm EditContext="EditContextFor(_request)">`, and `SubmitAsync` fails loudly on
any foreign context — so a form bound `Model="..."` is a thrown exception, not a quietly severed
blur mechanic. No cascading parameter, no `Dispose` dance (the context and the page share a
lifetime), one fewer thing to forget. The guard is canary-pinned
(`A_foreign_edit_context_is_rejected_loudly`), and the pre-existing blur test proves the folded
mechanic exactly as it proved the component form.

**Exit gate met:** Heimdall 64/64 green.

### Step 4 — the name (architect's step: "call it NavigationResult for my sanity")

`NextUrlResult` → **`NavigationResult`**; the member stays `NextUrl`. The grammar, as the
architect read it back: *NavigationResult → NextUrl → go there* — the type names what was decided,
the member names what to do about it, and the client contract is the third word. The saga's
charter in the architect's four words: **the Æsir demand answers** — every record in
`Abstractions.Contracts` answers a universal question (`BoolResponse`: is it so?;
`NavigationResult`: where now?; `Unit`: done, that's all), and realms ask their domain questions
in that vocabulary.

**Exit gate met:** Asgard 93/93 · Heimdall 64/64 · Himinbjörg 137/137, all staged.

### Step 5 — the Empty escape hatch, verified (and a collision surfaced the right way)

The architect probed whether `google.protobuf.Empty` exists as a success-side escape hatch —
momentarily as "Register/Login/Logout return `Task<Outcome<Unit>>`," which collided with step 2's
own ruling (`NavigationResult` carries 2FA-rides-success, deferred cookie completion, and
server-resolved routing — none of which `Unit` can say). Per the standing law, the collision was
surfaced in one objection instead of executed; the architect resolved it: **`NavigationResult`
stands; the probe's real intent was confirming the hatch exists.** It does, in two grades:

- **Sanctioned:** `Task<Outcome<Unit>>` — `Unit` erases to zero bytes, byte-identical to
  `google.protobuf.Empty` (proven machinery: the pre-2026-07-27 `Register` shipped this shape),
  failure arm intact. The reach-for when an operation genuinely has nothing to say.
- **Native backstop:** bare `Task`/`ValueTask` → `google.protobuf.Empty` (protobuf-net.Grpc's
  own mapping, mirroring the CT-only request side `Logout` spike-proved). True fire-and-forget
  only — no failure story — which no gate operation has.

Message names never ride protobuf wire — only field tags — so `Unit` and `Empty` are
indistinguishable in binary; the difference is `.proto` text only, mappable at Midgard's wire
layer the day a stranger-facing schema demands Google's spelling.

**Docket (accepted from external review, not yet a step):** mirror the invalid *state* (not the
messages, not the keys) onto the bound control — stamp failures key `Email` (wire-stable, shared
with server error keys) while the control's styling/aria reads its own bound `FieldIdentifier`
(`EmailInput`); the mirror is presentation mechanics and lands in `OutcomeFormComponentBase` by
the same `XInput ↔ X` convention as the blur echo. Summary display, submit blocking, and
edit-to-clear are unaffected today.

*(awaiting the next step)*
