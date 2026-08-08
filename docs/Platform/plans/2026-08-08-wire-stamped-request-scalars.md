# Wire-Stamped Request Scalars — Execution Plan

**Spec:** `../specs/2026-08-08-wire-stamped-request-scalars-design.md` (rev 3, adversarial review
passed; Open Decisions ruled §8). **Mode:** unattended run, standing authorization — human gates
waived, staging discipline kept (no commits), PR + independent review follow in the morning.
**REQUIRED SUB-SKILL:** `superpowers:subagent-driven-development` + `superpowers:test-driven-development`
(run inline this session under the unattended authorization; TDD per task regardless).

## Train 1 — Midgard: PII rows in the wire law (three legs)

1. `Infrastructure.Web.Grpc`: `PiiResultSerializer<T>` (`where T : struct, IPiiScalar<T>`) — read:
   `ReadString` → `T.Parse` (typed `Failure`, never a throw); write: success unwraps to
   `WireValue` via `WriteString`, failed/default throws `IllegalWriteMessage`. Four closed
   registrations in `ResultSerializers.Register` (EmailAddress, PersonalName, PhoneNumber,
   BirthDate). Tests: round-trip, malformed→Failure, illegal-write throw, absent→default.
2. `Infrastructure.Web.Server/Json`: `PiiResultJsonConverter<T>` + factory routing (today the
   factory claims every non-enum `Result<T>` then fails constructing the `ISpanParsable`-constrained
   converter at runtime). Same wire form (`WireValue` string), same illegal-write law. Tests mirror leg 1.
3. Futhark (`Infrastructure.Web.Server.Xml.Generator`): PII rows in the scalar taxonomy +
   reader/writer emission via `T.Parse`/`WireValue`. Tests via the generator test harness with
   synthetic fixtures. If generator surgery exceeds the session, this task alone may hand off —
   legs 1–2 do not depend on it.

## Train 2 — Heimdall: the reshaped flat records

4. `AuthN.Services`: `LoginRequest` → `Result<EmailAddress> Email` (Order 1, serialized) +
   `EmailInput` buffer (unserialized, `string.Empty` default, documented server semantics) +
   `Password` (2) + `RememberMe` (3); `RegisterRequest` → same email shape + `Password` (2);
   `EmailExistsRequest.Email` → `Result<EmailAddress>` stamped, no buffer; `RegisterResult` →
   bare record; `LoginResult.NextUrl` → Order 1. All flat sealed records.
5. `AuthN.Components`: `ResultRuleExtensions` (`ValidateResult` — buffer-registered, stamp-
   examining, `Cascade(Stop)`, safe message, chainable); validators rewritten under field-identity
   shape A; async EmailExists rule chained after the success gate, request built from the proven
   stamp. `AuthN.Components.FluentUI`: Login/Register bind `EmailInput`.
6. Tests: `RequestContractTests` extension (stamped members serialized, buffers not), validator
   unit tests (gate order, safe messages, no raw input), component-test lock (blur/inline/async/
   server-error/edit-to-clear) to the extent the existing bUnit harness supports.

## Train 3 — Himinbjörg + Bragi: consumers

7. Himinbjörg: handlers gain the `TryGetValue` prologue + deliberate `.WireValue` egress
   (`LoginHandler`, `RegisterHandler`, `EmailExistsHandler`); `EmailExistsRequestValidator`
   one-liner (`ValidateResult` on the stamp); `Problem.Errors` keys → bound field names; tests
   updated + hostile-path test (malformed/default never reaches `UserManager`).
8. Bragi: `FakeAuthenticationService` + scenario tests follow the new shapes (sentinel compares
   `EmailInput`; `RegisterResult` bare; error keys → bound names); stories compile.
9. Full test runs per realm; stage everything, commit nothing.
