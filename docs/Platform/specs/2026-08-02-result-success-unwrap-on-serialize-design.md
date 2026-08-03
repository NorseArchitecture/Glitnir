# Result&lt;T&gt; Success-Unwrap on Serialize — One Funnel, Both Ends of the Wire

**Date:** 2026-08-02
**Status:** **Ratified** — operator verdict rendered in session 2026-08-02 (Arm A of the first-party request-write analysis), closing the wall surfaced by `2026-08-02-futhark-enum-wire-law-design.md` §5. Next gate: `/writing-plans` (human gate; plans for this spec and the enum wire law spec are expected to land together — same write paths, one pass).
**Naming note, learned the hard way in the ratifying session:** this document is deliberately *not* titled "write law restored" — that phrasing reads as "the union rides the wire," which was never on any arm's table. The union has no wire representation on any channel, in any arm, ever. What this ratifies is exactly one serializer branch: a `Success<T>` member emits the naked `T`; `Failure` and `default` still throw.
**Restores:** `2026-08-01-opinionated-xml-serialization-design.md` §9.1 ("failed/default `Result` → throw") and §9.3 ("serialize: success unwraps to the value; failed `Result<T>` throws client-side") as ratified. **No amendment to `../../the-two-unions.md` is needed — it was right all along** ("a proposal to make `Result<T>` ephemeral or strip its serialization is equally wrong… its whole value is that it composes into consuming types"). The Task 13 unconditional-throw hardening is the artifact being reverted: it contradicted two standing documents, uniformly, on all three channels.

---

## 1. Context — the wall, and who was standing on which side

The Task 13 implementation hardened every `Result<T>` write path — the generated XML writer, both STJ converters, both protobuf serializers — to **throw unconditionally, every state including success** ("a deserialization-only type has no legal outbound form"). Internally consistent, and it walled off the typed gRPC client proxy from authoring a request against any facade-exposed (Result-wrapped) contract: the tri-protocol swoop only functions through hand-built plain-field mirror contracts and raw-byte marshallers — the tests had to become the client that could not exist. A real WASM/MAUI client hits the throw on the first tri-channel operation.

Blast radius today is zero — the platform's only shipped first-party wire flow (`LoginRequest`) is plain-typed and gRPC-only, and the only Result-wrapped contract in existence is the swoop's test-local `ParityRequest`. The wall sat entirely in the future, which is why it is ruled now, before the first real tri-channel operation arrives with a deadline attached.

## 2. The ruling

**Success unwraps. Failure and default throw. All three channels, uniformly.**

| Member state | On write |
|---|---|
| `Success<T>` | The clean unwrapped `T`, in the channel's §7 lexical/native form — the wire never sees the union (symmetry law, Futhark §10) |
| `Result<T>?` = `null` (optional, absent) | Omitted — attribute absent / property omitted / field skipped |
| `default(Result<T>)` (required, never set) | **Throws** — an unset required member dies loudly client-side, before the wire; a free authoring tripwire, not a wart |
| `Failure` | **Throws** — failures are never shipped, in either direction |

The read side is untouched everywhere.

**The doctrinal statement, replacing "deserialization-only":** `Result<T>` is the **boundary member state** — a validated value or a captured failure — with two legitimate authors:

1. **The parse funnel**, turning untrusted outside text into the state — the text-channel deserializers, and equally any tabular ingestion boundary (TSV/BDX/XLSX): every untrusted third-party scalar cell lands as `Result<T>`/`Result<T>?` so violations are reported as data, cell by cell, instead of dying on the first bad row. One law for every untrusted scalar crossing into the platform.
2. **The validated first-party client**, which holds compile-time-typed values — and which may itself run the *same* funnel over raw form input (`Parser` is not server-only): raw text → `Result<T>` members → the *same* FluentValidation rules — to green, client-side, before ever invoking.

What can never cross the wire is a failure, or an absence pretending to be a value. That is the law the throw arms enforce; the success arm was never the danger.

## 3. The recorded why — ubiquity over fit

Ratified rationale, operator's own, recorded so the trade is never re-litigated as an accident:

- **One validator, written once, the whole ladder:** unconstrained user text → `Result` parser → BCL type or domain struct (`Email`, `Ssn`, `ZipCode` — the two-unions doctrine's own worked example) → chained FluentValidation rules. The WASM app runs that artifact to green before invocation (instant field-level feedback, same failure wording as the server); the server re-runs it unchanged (zero trust — the client's green is UX, never authorization). No second validation dialect exists anywhere.
- **Promotion is free:** any gRPC operation promotes to REST with zero code changes — the full auth → validation → `Outcome<T>` pipeline rides along, because both channels speak the same Result-wrapped contract into the same mediator command.
- **The gRPC fit tax is paid knowingly.** `Result<T>` fits STJ like a glove and is round-peg-square-hole on protobuf (surrogates, presence mechanics). That awkwardness is the price of the two bullets above, accepted with eyes open. Proposals to "clean up" the gRPC leg by splitting contracts re-litigate this section first.

## 4. Authoring ergonomics — the implicit conversion

Svartálfheim adds **`implicit operator Result<T>(T)`** beside the existing `Success<T>`/`Failure` union conversions, so client authoring reads as plain assignment (`Amount = 1234.56m`); nullable lifting gives `T → Result<T>?` for free. Doctrine check performed: `the-two-unions.md` starves `Outcome<T>`'s API, not `Result<T>`'s — "compose, project, convert — it is a value and behaves like one" — the conversion is that sentence made sugar. Plan-time verification flagged: overload-resolution interference (methods overloaded on `T` and `Result<T>`) checked before it ships; fallback is the already-implicit `Success<T>` construction, degraded ergonomics, same law.

## 4a. The binding shadow — how a Blazor form authors a Result-wrapped contract

Two-way form binding cannot target `Result<T>` members directly; the binding surface is a **stateless string shadow per member** whose accessors are the funnel itself:

- **get:** derived entirely from the `Result` member — `Success` renders the canonical lexical form; **`Failure` renders `Failure.Input`**, so the user's rejected text round-trips back into the input for correction (the union already carries it — no backing field, no view-state); default renders empty.
- **set:** `Parser.ParseRequired<T>`/`ParseOptional<T>` into the `Result` member — the same funnel, keystroke to wire; the shared FluentValidation validator sees fresh member state on every edit and runs to green client-side before invocation (§3).

**Placement, ruled (2026-08-02, revised same session): on-contract, undecorated — carried by the opt-in law below.** Shadows are plain `get; set;` string properties on the request record with **no attribute at all**; under §4b they are invisible to every serializer and to the generator. Earlier drafts of this section weighed extension-member placement and `[IgnoreDataMember]` exclusion wiring — both are obsoleted by §4b and recorded only as the road not needed: extension members remain a legal style choice, never a structural requirement, and `[IgnoreDataMember]` never enters the codebase.

**Mutability consequence, ratified:** bindable request contracts carry `get; set;` `Result` members — the `LoginRequest` "deliberately mutable" exception becomes the standing rule for any request contract a form binds. Response contracts stay `init`-only.

## 4b. The opt-in law — `[DataContract]` means opt-in on every channel

Surfaced by §4a's placement question; ratified as standing contract law because the platform was found running **three definitions of contract membership**: protobuf-net honors `[DataContract]`/`[DataMember]` opt-in natively; STJ ignores the WCF vocabulary entirely and serializes every public property, both directions; and the XML closure walker (`ClosureWalker.GetInstanceProperties`, verified against source) projects all public instance properties with no `[DataMember]` filter. Three definitions is zero definitions.

**The law:** on a `[DataContract]` type, **`[DataMember]` members are the contract — everything else does not exist to any channel.** The `[ServiceContract]` taxonomy remains the platform's single contract vocabulary (Futhark §3); STJ's parallel attribute family (`[JsonIgnore]`, `[JsonPropertyName]`, …) never enters contract code. Enforcement, per channel:

- **protobuf-net:** native behavior — nothing to do.
- **STJ:** `AddNorseJson` gains a `TypeInfoResolver` modifier that strips non-`[DataMember]` properties from `[DataContract]` types — one wiring, platform-wide, both directions. Incoming members naming a stripped property are thereby *unmapped* and die loudly under the existing `UnmappedMemberHandling.Disallow` ratchet — the "second door" a bindable shadow setter would otherwise open is closed by construction.
- **XML generator:** the closure walker's property projection gains the same `[DataMember]` filter — completing the law its own NORSE028 (body types must be `[DataContract]`) already implies. A shadow property therefore never enters a closure and NORSE022 cannot fire on it.

**Side effect, named as a benefit:** before this law, a stray public helper property on any response contract silently leaked into JSON responses (STJ default). After it, a non-`[DataMember]` member structurally cannot reach any wire — the pit-of-success answer to a leak class that review was never going to catch reliably.

## 5. Consequences — plan-time inventory

1. **Midgard, gRPC:** `ResultSerializer<T>` and `ResultEnumSerializer<TEnum>` gain the success-unwrap write branch (each type's established wire form; enum write is the varint — an undefined enum value throws, the illegal-to-write law unchanged). The shared message constant changes from "deserialization-only…" to the failure-state message §9.1 originally specified: **"a failed Result&lt;T&gt; is illegal to write"** (default state included in wording at plan time).
2. **Midgard, JSON:** both converters gain the success branch (§7 lexical forms via the existing converter recursion). Production consumers remain gRPC-only — text channels are for strangers (§1.3) — the honesty comments survive with updated wording: the path is *legal* everywhere, *exercised* by tests and any future first-party SDK. `AddNorseJson` additionally gains the §4b opt-in `TypeInfoResolver` modifier, with a test proving a non-`[DataMember]` property neither serializes nor binds and that an incoming member naming one dies under `Disallow`.
3. **Midgard, XML generator:** `WriterEmitter` restores unwrap-on-success emission for Result members (and drops its truncate-on-unconditional-throw machinery). `ClosureWalker.GetInstanceProperties` gains the §4b `[DataMember]` filter, with generator tests proving an undecorated property enters no closure and fires no diagnostic.
4. **Tests:** the mirror-contract estate is deleted where it exists to dodge the throw — the swoop's raw-byte `EchoRawAsync` marshallers and `ParityRequestWireFixture`, `ResultSerializerTests`' `PlainEnvelope<T>` idiom — replaced by typed-proxy/typed-envelope round trips that exercise the real client path. **Exception, kept deliberately:** hand-built wire bytes remain the honest fixture for *absent-field* semantics (an omitting client cannot be authored through a proxy that throws on default — absence on the wire simulates a foreign or stale binary client) and for malformed-lexeme fixtures. The plan states which fixtures fall in which bucket.
5. **Docs:** the divergence note stamped into the parent spec §9.3 (2026-08-02) resolves to this ruling; `2026-08-02-futhark-enum-wire-law-design.md` §5 gains its ruled pointer. `the-two-unions.md` is untouched.
6. **Sequencing:** Svartálfheim (implicit conversion) → Midgard (write paths + message + tests) → Yggdrasil (swoop rework). Plans after the human gate, expected to ride with the enum wire law plan.

## 6. Rejected arms — recorded

- **Arm B — first-party clients never speak facade contracts** (twin plain contracts per tri-channel operation): the validation river forks (parse-capture rules on the text projection, semantic rules on the plain twin), contracts duplicate, mapping layers return — the envelope machinery buried 2026-07-27, repainted. **Named trigger:** when a partner-facing shape and the internal shape genuinely diverge (versioning, PII masking, anti-enumeration), contracts split *for shape reasons* — never for serialization mechanics.
- **Arm C — remove `Result<T>` from contracts entirely** (plain members, side-channel failure accumulation): JSON would need generated readers to replicate per-member absence/failure capture (rebuilding Futhark's machinery to delete a type), and gRPC loses the surrogate's absent-vs-default distinction, forcing `T?` members and NRT pollution through every handler. Capture-as-data would leave the type system for ambient runtime plumbing. Coherent greenfield answer; brutal negative-value rework here. Recorded so it is not rediscovered as a clever idea in year two.
- **Keeping the unconditional throw** (status quo): contradicts §9.1/§9.3 as ratified and the two-unions doctrine's explicit consequence line, and makes every tri-channel operation unauthorable by the platform's own clients — the mirror-fixture gymnastics in the test suite was the symptom, not a technique.
