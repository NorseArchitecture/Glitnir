# Futhark — Opinionated XML Serialization for the REST Ambassador Layer

**Date:** 2026-08-01
**Status:** **Approved as amended** — Forseti + Operator consolidated amendment set ratified 2026-08-01, folded 2026-08-02. Next gate: `/writing-plans` (human gate; nothing proceeds until it passes).
**Narrative name:** *Futhark*, the runic alphabet — fixed carving rules, no calligraphy. A doctrine name in the `the-two-unions.md` tradition, **not** a codename binding: no repository, namespace, package, file path, or type name ever carries "Futhark" or "Rune." `codenames.md` is untouched by this document.
**Downstream dependent:** the PII disclosure surface's REST facade (masked values and the `Erased` problem payload need XML representations). Futhark ships first, standalone, because serializer defects discovered downstream are data-corruption bugs, not inconvenience bugs.
**Prior art acknowledged:** protobuf-net.Grpc issue #264 — the 2022 transcoding discussion with Marc Gravell (<https://github.com/protobuf-net/protobuf-net.Grpc/issues/264>), where the operator's response established the hand-authored-facade pattern §4 inherits: code-first transcoding was "not supported... everything is time," and the answer was the bridge — controllers injecting the gRPC service interface directly, no protobuf on the path, no double serialization, per-action third-party exposure. The `Result<T>` JSON-converter funnel, OpenAPI schema-transformer mechanics, and the inbound-only posture for parse results are mechanisms proven in the operator's prior commercial work and adopted here on merit. The `Result<T>` protobuf surrogate has **no prior art anywhere** — that leg is novel and is flagged as such (§9.3).

---

## 1. Context and sequencing

### 1.1 One consumer, three channels

Futhark's sole consumer is Yggdrasil's `Hosting.Web.Server`: it exists to let the REST facade content-negotiate to XML instead of JSON. There is no second consumer and none is planned; that fact drives the residence verdict (§3) — no new realm, no standalone package family, and after amendment, no footprint outside Midgard and the host save the facade base class Asgard declares for downstream inheritance (§3, ruled 2026-08-02).

The validation case is deliberately wider than the serializer: **the tri-protocol swoop**. The same request record served over gRPC, REST-JSON, and REST-XML, asserted for parity — same success shape, same failure semantics where the channel can express them — before any PII struct enters the picture. The swoop proves the negotiation seam, the `Result<T>` funnel on every channel, and the formatter registration, in one test surface (§15).

### 1.2 Ethos

**One way to write XML.**

- Every scalar is an attribute. No exceptions.
- Every collection is N child elements. No wrapper elements, ever — `<Coverages><Coverage/></Coverages>` ceremony is what serializers produce when nobody made a decision, and it is banned here because somebody did.

The ethos is the acceptance test for every future feature request: **if a proposal introduces a second way to write the same data, it is rejected before it is evaluated.** Everything painful about XML serialization — attribute-vs-element ambiguity, wrapper bikeshedding, `XmlArrayItem` ceremony — exists because general-purpose serializers let callers negotiate every axis. Remove the negotiation and the serializer collapses to a small recursive projection. Take-it-or-leave-it is an API posture *and* an implementation subsidy.

### 1.3 Channel doctrine — text channels are for strangers

Ratified, stated for the record:

> **Text channels are for strangers.** JSON and XML exist solely at the ambassador desk where third parties present themselves. Internal traffic — WASM, MAUI, Server — is binary and typed, gRPC end-to-end. This is *why* strictness is uniform across both text channels and why proto3 unknown-field tolerance on gRPC is acceptable: binary clients drive generated stubs — no fingers, no typos.

Consequence: no internal client ever serializes a request over a text channel. The JSON converters' request-**write** path has no production consumer; it is retained as **test infrastructure** (the round-trip suite must author wire-shaped requests) and the code comments say so honestly (§9.1).

---

## 2. The engine — source generator, argued

The largest architectural fork was reflection-plus-cache versus a Roslyn source generator. **Verdict: source generator. No reflection fallback, ever.**

1. **The complexity cost is already sunk.** The platform carries the Emit toolkit (`AppendCSharp` house style), the analyzer-strip target, and four shipped generators' worth of scar tissue. Futhark's generator is smaller than the gRPC wiring generators: a recursive projection over public properties with a fixed scalar taxonomy. The trigger is the facade-controller closure walk in the host compilation (§4) — no new attributes, no `AdditionalFiles` ceremony.
2. **Every shape law moves to build time, not just the headline one.** One-member-per-complex-type is the motivating case, but the same shape walk catches unsupported scalars, non-sealed complex types, post-case-transform name collisions, dictionaries, scalar collections, generic contract types, cross-direction sharing, and direction violations of the `Result<T>` wrapping law. Each is a diagnostic with a code (§14), not a startup throw and never a runtime corruption. This is the `[RetentionPolicy]` posture applied to the wire: *you cannot compile an exposure Futhark cannot round-trip.*
3. **Reflection here would be the platform's only reflection.** Doctrine is compile-time over runtime everywhere else; the component that must be ironclad before anything counts on it is the worst place for the exception. AOT/trimming on the Yggdrasil trajectory settles any residue.

**Consequences accepted deliberately:**

- A type outside the exposed closure is not serializable — the formatter refuses it loudly. A reflection fallback would be a second way to write the same data; the ethos rejects it before evaluation.
- Shape-law diagnostics fire at the **host build**, not in the contract author's editor. Under the exposure-scoping law (§4) this is *correct*, not a compromise: a contract is not illegal until something exposes it over XML, and exposure is declared at the composition root. The diagnostic fires exactly where the crime is committed.

---

## 3. Residence and consumer-visible names

**Midgard owns the wire machinery; Yggdrasil runs it; Asgard declares the facade base.** Svartálfheim owns primitives and nothing else — wire-format concerns never enter the forge. The Æsir do not care about wire formats — no XML seam, generator, or converter enters Asgard. **One Asgard exception, ruled 2026-08-02: `GrpcControllerBase` (§4) lives in `Abstractions.Web.Server`** — the facade base is contract law, not wire format, and downstream services must inherit it; under only-Yggdrasil-depends-on-Midgard, a Midgard residence would wall the facade off from every consumer it exists for, adding no value to the realm. The fold needs nothing from Midgard — `Problem()`/`NotFound()` are `ControllerBase` natives; problem+xml rendering is the formatter's job at the host. Contract assemblies are untouched: no forwarding, no generated files in realm repos, no `partial` ceremony, no new attributes.

| Realm | Location | Contents |
|---|---|---|
| Asgard | `src/Abstractions.Web.Server`, `Facade/` subfolder | `GrpcControllerBase` — the facade base + `Outcome<T>` fold (§4.3), inheritable by every downstream service; server-only by the assembly's existing law |
| Midgard | `src/Infrastructure.Web.Server`, `Xml/` subfolder | `IXmlShape<T>`, `XmlCaseStyle`, `XmlReadContext` (the typed seam generated code compiles against); `XmlContractInputFormatter` / `XmlContractOutputFormatter` (MVC `TextInputFormatter`/`TextOutputFormatter`); the RFC 9457 problem writer (§11.1); `AddNorseXml(XmlCaseStyle)` composition extension |
| Midgard | `src/Infrastructure.Web.Server`, `Json/` subfolder | `Result<T>`/`Result<T>?` STJ converter family over the scalar taxonomy (§9.1); the `UnmappedMemberHandling.Disallow` ratchet |
| Midgard | `gen/` beside `Infrastructure.Web.Server` (per repo convention; exact project name is a plan detail, §17) | The Futhark shape generator + shape-law analyzers |
| Midgard | `src/Infrastructure.Web.Grpc` (existing) | `Result<T>` protobuf surrogate registration, riding the existing code-first serializer home (`IdentifierSerializers.cs` precedent) |
| Yggdrasil | `src/Hosting.Web.Server` (existing) | The facade controllers (§4); the generator **executes here**, in the host compilation; negotiation wiring; the tri-protocol swoop test surface |

Notes:

- **The generator is a Midgard project that executes in the Yggdrasil compilation.** It reads referenced contract metadata and emits shapes into the host compilation, where the Midgard seam is legally referenceable. Only-Yggdrasil-depends-on-Midgard holds by construction — there is nothing anywhere else to depend on.
- **Facade controllers are host-compilation source, always — ratified 2026-08-02.** The generator's discovery is a syntax predicate: it sees class declarations in the compilation it runs in and is structurally blind to controllers compiled into referenced assemblies. The Asgard base makes cross-realm *inheritance* legal; shipping reusable *controllers* in a library is not — they would silently generate no shapes, fail no diagnostics, and 500 at runtime on an unregistered type. Each deployment's host authors its own facade, which is philosophically forced regardless: exposure is declared at the composition root, so the controllers declaring it are authored there. **Tripwire:** `AddNorseXml` fails startup loudly when a `GrpcControllerBase` descendant sits in the app's controller feature set with a body or response type carrying no shape in the registry — the silent gap becomes a named error for whoever tries it in year two.
- **Contract vocabulary: the `[ServiceContract]` taxonomy, nothing else.** The platform's single contract vocabulary is the WCF attribution model already forced by protobuf-net.Grpc code-first: `[ServiceContract]`, `[OperationContract]`, `[DataContract]`, `[DataMember]`. Futhark honors it and adds **zero attributes**. (Historical note, ratified as platform position: WCF died of SOAP and WS-*, not of its attribution model — the vocabulary was always sound, which is why protobuf-net.Grpc resurrected it verbatim.)
- The generator emits `internal sealed` shape classes plus a registration method into the host compilation; contract records stay clean.
- The REST facade rides MVC's formatter/negotiation pipeline. Minimal APIs are rejected for this surface: content negotiation **is** the requirement, and minimal APIs do not have it. Automatic gRPC transcoding is likewise rejected (§4).
- Case style is **per-host, fixed**: one `XmlCaseStyle` argument to `AddNorseXml(...)` at the composition root. Not per-endpoint, not negotiated, not sniffed. The generator precomputes all five casings of every name into the shape tables (static strings, trim-safe); the host's option selects a column at startup — zero runtime case transformation.

---

## 4. The binding — hand-authored facade controllers

**Prior art (operator's own, 2022):** protobuf-net.Grpc issue #264 (<https://github.com/protobuf-net/protobuf-net.Grpc/issues/264>) — the transcoding discussion with Marc Gravell, where the operator's response established the pattern this platform inherits: the REST facade is **hand-authored `[ApiController]` classes**, never automatic transcoding. Each controller injects the gRPC service *interface* — the same implementation invoked in-process with no protobuf on the path and no double serialization, running the full mediator pipeline (validation, authorization) underneath. A shared `GrpcControllerBase` — declared in Asgard's `Abstractions.Web.Server` so every downstream service can inherit it (§3) — supplies the cross-cutting attribution and the fold from service response to `ActionResult<T>`.

Two 2022 constraints are dissolved by 2026 platform machinery, stated so the citation reads correctly:

- `EntityResult<T>` existed to return null without throwing `RpcException` — exactly the not-found state `Outcome<T>` now carries natively. The wrapper fiesta is gone; the structure remains.
- The pattern's acknowledged con — "you will need something to run your validation framework in both contexts" — is dissolved by the failure river (§10.3): validation runs once, inside the pipeline both channels invoke, and each channel folds the same `Outcome<T>` into its native failure shape (problem details vs `RpcException` metadata), which the 2022 discussion already predicted was "preferred in the long run."

The WCF framing holds in spirit: contracts are attributed (`[DataContract]`), **bindings decide exposure** — and the modern binding is the hand-authored controller. Ratified consequences:

1. **Futhark shape law binds only the facade-controller closure.** The generator walks controller action signatures in the host compilation: **body-bound parameter types form the request closure; `ActionResult<T>` payload types form the response closure.** Route- and query-bound primitives never touch a body and fall outside Futhark's jurisdiction by construction. A gRPC-only service — or an unexposed *method* of an exposed service — answers to no XML law, because exposure is per-action: you surface exactly the methods you write controllers for, and nothing else. This is finer-grained than any service-level mapping marker, and it is the control the facade exists to provide.
2. **Direction is derived from usage, never declared.** Direction is a fact of position: the controller action states direction and exposure in one signature. The `Result<T>` wrapping law (§5.4) applies to the **full reachable closure** of each position.
3. **The `Outcome<T>` fold lives in the base controller, once.** Success → 200; not-found → 404; failure → problem details per §11. This fold is the single future landing site for the PII `Erased` state → **410 Gone** carrying the Syn receipt reference as a problem extension member — the hook exists by construction, no speculative code now.
4. **Credential segregation, ratified as law:** client-credentials grants (and third-party credentials generally) are confined to the REST pathway — **they never run gRPC**, even for partners who speak protobuf. The text channels are the strangers' only door; gRPC endpoints are reserved for first-party authentication schemes. Defense-in-depth by construction, and the reason the facade's per-action curation is a security surface, not merely an API-design nicety.
5. **OpenAPI serves the same curation:** the facade controllers are where the contract document is authored (§12's transformers apply to them); what a partner can see and what a partner can call are the same hand-picked set.

## 5. Contract shape law

What an exposed contract may look like, all enforced at build time in the host compilation (diagnostics in §14):

1. **Direction is positional (§4.2).** Request closure and response closure are derived from the facade action signatures; no direction attributes exist.
2. **Sealed, flat, closed.** Contract types and every complex type reachable from them are `sealed`, derive from `object` only, and are non-generic. Polymorphism is banned — element-name→type stays injective. Interfaces are permitted (they do not affect shape).
3. **Scalar taxonomy (closed set):** primitives, `string`, `Guid`, `decimal`, enums, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`. One pinned lexical form per type, both text channels (§7). No locale leakage, ever.
4. **Request scalars wrap; response scalars never do.** In the request closure every scalar member is `Result<T>` (required) or `Result<T>?` (optional); a raw scalar is a build failure. In the response closure any `Result<T>` member is a build failure. `Result<T>` is a parsing result — inbound capture only; the wire is always the clean value (§9, §10).
5. **No type serves both masters.** A complex type reachable from both the request closure and the response closure is a build error naming that crime directly — "you shared a type across the boundary" — not the confusing pair of scalar-law diagnostics the structural contradiction would otherwise produce.
6. **The nullable wrapper is the optionality signal, everywhere.** `Result<T>?` / `T?` = optional (omitted attribute → null); `Result<T>` / `T` = required. Complex members follow the same rule: nullable complex member = optional element, non-nullable = required.
7. **One member per complex type per contract — any arity.** The original law covered collections; the same collision exists for singletons, because element names come from type names. Two `PostalAddress` properties emit two indistinguishable `<PostalAddress>` elements, which the reader cannot tell apart — so the writer was never allowed to mean it. A contract needing two addresses defines two record types (`HomeAddress`, `MailingAddress`) and the wire is better for it: name-the-role, made structural.
8. **Collection items are complex types only.** A scalar collection has no legal shape: items must be elements, scalars must be attributes, and element text content is banned — the intersection is empty. Wrap the scalar in a role-named record (`<Tag value="…"/>`). Collections of collections and dictionaries likewise have no shape; all three are diagnostics.
9. **Collections are always non-null, empty when absent.** Zero child elements ⇔ empty collection — one wire state, one in-memory meaning, the EF navigation law applied to the wire.

## 6. Wire grammar — the writer

1. **Names.** Element names come from type names; attribute names come from property names; the one configured case style applies to both, including enum member names. The root element is the contract type's name, case-styled.
2. **Scalars are attributes.** Null scalars omit the attribute — no `xsi:nil`, the problem evaporates by construction. On request shapes (test-side writing, §1.3), `Result<T>` success writes the unwrapped formatted value; **a failed `Result<T>` is illegal to write** and throws.
3. **Collections are N child elements**, one per item, named by item type. Empty collection ⇒ zero elements.
4. **Determinism.** Attributes in member-declaration order; child elements in member-declaration order; collection items in list order. Output is byte-stable for a given contract, casing, and value.
5. **Enums: names, never numerics.** The generator emits the name↔value table at build time — no `Enum.Parse`, no reflection. Undefined values (non-flags value with no member; flags with leftover bits) are illegal to write. `[Flags]`: space-separated name list (the `xsd:list` idiom); canonical form is exact-defined-match first (`ReadWrite`, never `Read Write`), else greedy decomposition descending by value. Zero writes the defined zero member's name. **Stated sharp edge, documented so a consultant does not discover it in month one:** a default-initialized `[Flags]` field with no defined zero member is an undefined value and **throws at write** — consistent with the law, and the fix is defining `None = 0` or setting the field.
6. **Plumbing.** XML declaration always emitted; UTF-8, no BOM; no indentation; no XML namespaces, ever; no comments, PIs, or CDATA. All writing goes through `XmlWriter` so embedded tabs/newlines survive attribute-value normalization as character references. Strings — and `char` values — carrying XML 1.0-illegal control characters are illegal to write, same hammer.
7. **Symmetric limits.** The writer honors the same max depth as the reader (§8.4) — round-trip symmetry includes the limits.

"Illegal to write" is the round-trip law's hammer: anything the reader could not reconstruct losslessly throws at write time, loudly, before it ever reaches a wire.

## 7. The lexical table — one grammar per scalar, both text channels

"ISO 8601 / invariant culture" is not a specification — for `TimeSpan` the two are different grammars (ISO 8601 duration `P1DT2H3M4S` vs invariant constant `1.02:03:04`). The wire form is therefore **pinned per type, one row each, identical on JSON and XML**, consumed by the shared `Parser` stack and emitted by both writers. STJ defaults do not apply where they disagree — the converter family owns the JSON channel by construction. Every row carries a cross-channel parity test (§15).

**Reader lexical space, pinned:** the table below is the **canonical emission form** — the writer emits it 100%, byte-exact, nothing else. The reader accepts the `Parser` stack's full lexical space per type — whatever the parser handles into a correctly-valued BCL struct — identical on both text channels. This is not forward tolerance (unknown members still die loudly); it is canonical writer, defined-lexical-space reader, which the round-trip law already implies: writer output is a subset of reader input.

| Type | Pinned wire form | Example |
|---|---|---|
| `bool` | `true` / `false` | `true` |
| integral types | invariant decimal digits, `-` permitted | `-42` |
| `decimal` | invariant plain decimal notation, no exponent, no separators | `1234.56` |
| `float` / `double` | invariant shortest round-trippable form — **supported, discouraged**; see note below | `0.1` |
| `char` | the single character | `A` |
| `string` | verbatim (channel-escaped) | |
| `Guid` | lowercase hyphenated (`D`) | `0b917371-…` |
| `DateTime` | ISO 8601 round-trip (`O`), kind suffix preserved | `2026-08-01T14:30:00.0000000Z` |
| `DateTimeOffset` | ISO 8601 round-trip (`O`) | `2026-08-01T14:30:00.0000000+02:00` |
| `DateOnly` | `yyyy-MM-dd` | `2026-08-01` |
| `TimeOnly` | `HH:mm:ss.fffffff` (`O`) | `14:30:00.0000000` |
| `TimeSpan` | **ISO 8601 duration** — ratified; culture-proof and XML-native | `P1DT2H3M4S` |
| enums | case-styled member name(s); `[Flags]` space-separated (§6.5) | `read_write` |

On JSON, `bool` and numeric types ride as native JSON tokens with the identical lexical form; everything else rides as JSON strings with identical content — including enums, which emit the same case-styled names on both channels.

**`float`/`double` — supported, discouraged.** Binary floating point is for measurements, not business values; contracts carrying money, rates, or quantities use `decimal` for exact precision — reach for `float`/`double` only when the domain is genuinely one of measurement, and question whether a boundary contract is the right home for it at all. **Non-finite values (`NaN`, `±Infinity`) are illegal to write and a malformed-scalar accumulable on read, both channels** — XSD's `INF` spellings and JSON's non-representation would otherwise diverge, and a non-finite value at a boundary is an upstream bug escaping, not data.

## 8. The reader

The reader defines what the writer was allowed to mean. It is also a security surface the writer never was.

### 8.1 Strictness — two failure classes

**Session-fatal** (the parse cannot meaningfully continue; single-error 400):

- Malformed XML (including duplicate attributes — a well-formedness violation the parser rejects natively)
- Non-UTF-8 encoding (declared or BOM-signaled), DTD appearance, depth exceeded, body-size exceeded
- Processing instructions, CDATA, XML namespaces anywhere (`xmlns` included)

**Accumulable** (the walk continues; **every** failure in the document is collected and reported in one payload — nobody plays 400-whack-a-mole):

- Unknown attribute or unknown child element (with a nearest-name suggestion when a close match exists)
- Duplicate singleton element; element text content
- Root-element name mismatch against the negotiated contract
- Malformed scalar, undefined enum name, duplicate flags token
- Required attribute or required element missing

There is **no forward tolerance**. The `birthday`-for-`birthDate` client gets `unknown attribute` *plus* `required missing` in one response and fixes their integration in one round-trip — the alternative is a typo'd optional field silently vanishing into an issued policy. Strictness parity across text channels is ratcheted **up**, not down: the JSON leg sets `UnmappedMemberHandling.Disallow` so XML and JSON enforce the same posture. gRPC stays proto3-tolerant of unknown fields by construction (§1.3).

### 8.2 The Result funnel — presence-aware

Generated request readers never throw on scalar content. Each attribute value routes through the parsing stack — `Parser.ParseRequired<T>` / `ParseOptional<T>`, invariant culture — capturing success or failure *as data* with the path attached.

**Presence is distinguished from emptiness.** `name=""` (present, legitimately empty) and `name` absent are different wire states:

- **Absent** + `Result<T>` → the `ParseRequired(string.Empty)` funnel, so the "required" wording comes from the domain's one message source, never from the serializer or ASP.NET internals. Absent + `Result<T>?` → `null`.
- **Present-empty** → parses `""` as actual content; succeeds for `string`, fails through the funnel for types where `""` is not a lexical form.

The alternative — declaring empty strings illegal to write — was considered and rejected: a domain restriction smuggled into a serializer. Required `Result<string>` carrying `""` round-trips, and §15 makes that mandatory.

`"it's a beautiful life"` sent as a date yields the channel-appropriate failure at `Policy/@birthDate`, alongside every other failure in the document.

Response readers (round-trip tests; future XML-consuming client SDKs) read plain scalars; a malformed scalar there is an accumulable failure with the same path grammar.

### 8.3 Reader dispatch

Attributes and child elements dispatch by name, order-insensitive. Collection items may appear at any position among siblings; document order is preserved into the list. Order-insensitivity on read creates no second way to *write* — the writer stays canonical (§6.4) — so the ethos holds.

### 8.4 Security settings — in the formatter, not in configuration

Non-negotiable, constructed in code, not bindable, not options:

| Setting | Value |
|---|---|
| `DtdProcessing` | `Prohibit` |
| `XmlResolver` | `null` (XXE dead by construction) |
| Max depth | **32**, both directions |
| `MaxCharactersFromEntities` | `0` |
| Request body cap | **1 MiB** (`1_048_576` bytes), declared at the facade — boundary contracts, not document transfer; Edda exists for documents |
| Encoding | UTF-8 only, both directions |

These numbers are doctrine. Changing one is an edit to this document with a Forseti verdict, not a config tweak, and never an incident response.

---

## 9. `Result<T>` across the three channels

One parsing stack, three channels, one message source. **`Result<T>` is a parsing result; the wire is always clean** — the wrapper never has a wire representation on any channel.

### 9.1 JSON — the STJ converter family (`Infrastructure.Web.Server`, `Json/`)

- `Read`: string tokens funnel to `ParseRequired<T>`/`ParseOptional<T>`; number/bool tokens are invariant-stringified into the same funnel so every failure message comes from one place; JSON `null` → `ParseRequired(string.Empty)` for `Result<T>` (domain-worded required failure), → `null` for `Result<T>?`; object/array tokens are skipped whole and captured as a typed failure.
- `Write`: success writes the clean unwrapped value; a failed `Result<T>` throws — you do not ship failures. Per §1.3 the request-write path has **no production consumer** (internal clients are gRPC end-to-end); it is retained as test infrastructure for the round-trip suite, and the code comments say so honestly.
- Coverage spans the full taxonomy under `Result<T>`'s `where T : notnull` — including `string`, which narrower `struct`-constrained designs miss.
- The same options pass ratchets `UnmappedMemberHandling.Disallow` (§8.1) and pins the lexical table (§7) where STJ defaults disagree.

### 9.2 XML — Futhark

The generated reader/writer emit exactly the parser calls and unwrap semantics described in §6–§8. No separate mechanism: §9.1's converter behavior and Futhark's generated code are two projections of the same funnel.

### 9.3 gRPC — the protobuf surrogate (novel; no prior art)

Wire form is presence-tracked `T` (proto3 `optional`) for both `Result<T>` and `Result<T>?`:

- **Serialize:** success unwraps to the value; failed `Result<T>` throws client-side, loudly.
- **Deserialize:** present → success (no parsing — the binary wire is typed; a malformed date is unrepresentable on this channel); absent + `Result<T>` → the failed required-missing `Result`, mirroring `ParseRequired` semantics without a parse; absent + `Result<T>?` → `null`.
- **Registration** rides the existing code-first serializer configuration in `Infrastructure.Web.Grpc`. The scalar taxonomy is doctrinally finite, so the worst case is a closed set of ~13 surrogate registrations; whether protobuf-net honors an open-generic registration is a plan-time verification, not a design risk.

## 10. The symmetry law — the Two Unions never see the wire

Ratified as doctrine, both directions, every channel:

1. **Outbound — `Outcome<Response>` dissolves at the edge.** The gRPC interceptor already unwraps it (`OutcomeServerInterceptor`); the REST facade's base-controller fold (§4.3) is the same office for text channels. **The OpenAPI transformer must unwrap it too:** response schemas present `Response`, never the envelope — the exact mechanic §12 applies to `Result<T>` on request schemas, applied to `Outcome<T>` on response schemas. A partner reading the contract document sees clean shapes; the envelope is a process-internal fact.
2. **Inbound — `Result<T>` dissolves at the edge.** The wire carries the naked scalar; the serializer (all three channels, §9) resurrects the union on read, loaded with the parser's verdict. Wire = value; process = union.
3. **The failure river, stated end-to-end:** serializer captures parse outcomes as data (never throwing on content) → **FluentValidation consumes the `Result<T>` members**, folding parse failures and semantic rules into one accumulated failure set → that set feeds the operation's `Outcome<T>` → the fold/interceptor translates to the channel's problem payload. One river, every tributary joins before the client hears anything — §8.1's full-document accumulation extended through the validation stage by construction.
4. **Wiring debt, named so it is not repeated:** the platform's own audit found `OutcomeServerInterceptor` designed and tested but **never wired into any composition root**. The REST fold and both OpenAPI transformers (request `Result<T>` unwrap, response `Outcome<T>` unwrap) therefore each carry a "wired not just designed" test from day one: remove the registration, the suite fails. The symmetry law is only law where it is wired.

---

## 11. The error channel

### 11.1 ProblemDetails in XML — follow the RFC, not Futhark

Strict-400 responses render as `application/problem+xml` in **the registered RFC 9457 XML format** (element-based, `urn:ietf:rfc:7807` namespace carried forward from RFC 7807, arrays as `<i>` item elements). This is the one deliberate exception to Futhark rules, argued rather than assumed:

1. **It is not our document.** For the problem-details format the IETF already decided the one way; carving a private dialect under the RFC's media type is a media type that lies to exactly the RFC-aware tooling conservative buyers point at it. The ethos survives: our contracts have one way (Futhark), the error channel has one way (the RFC's).
2. **Problems structurally cannot be Futhark output.** `ProblemDetails.Extensions` is `IDictionary<string, object?>` — dictionaries have no Futhark shape and never will. The problem writer is a small bespoke hand-written emitter either way; following the RFC costs nothing extra. Fixed shape, not generator territory.
3. **The `Erased` payload fits.** RFC extension members render as extension child elements; the future PII problem type carries its extras there (§4.3's 410 fold), exactly like the validation `errors` member below.

Validation failures surface through `ModelState` into standard `ProblemDetails` with an `errors` extension member: an array of `{path, detail}` entries — every accumulated failure from §8.1 and §10.3, one response. **The shape deviates from `ValidationProblemDetails`' dictionary deliberately, and is better** — paths repeat, and dictionaries need value-array ceremony; the deviation is chosen, not accidental. The JSON channel emits the **identical payload shape**, not merely the same paths (§15).

### 11.2 Error-path grammar — specified, not an accident

```
path      = segment *( "/" segment ) [ "/@" attribute-name ]
segment   = element-name [ "[" index "]" ]     ; index is 1-based, present only on collection items
```

- Names appear exactly as they appear on the wire — the host's configured case style.
- The root element is always the first segment.
- Message form is `{path}: {detail}` — `Policy/Coverage[2]/@limit: cannot parse 'x' as decimal`.
- The failure catalog (unknown attribute, unknown element, duplicate singleton, text content, root mismatch, malformed scalar, undefined enum name, duplicate flags token, required missing) each get a literal-asserted message format test (§15).

### 11.3 Content types

- `application/xml` — canonical, emitted on output.
- `text/xml` — accepted from clients who send it; same formatter, zero behavioral difference.
- `application/problem+xml` / `application/problem+json` — errors, per channel.
- **No vendor media type.** A vendor type is negotiation ceremony for a serializer whose thesis is that there is nothing to negotiate.
- `charset` parameter: `utf-8` accepted (redundantly); anything else is a 415.

## 12. OpenAPI

The document surface is **`Microsoft.AspNetCore.OpenApi`** — the native generation pipeline (.NET 9, 2024) and its `IOpenApiSchemaTransformer` seam — never Swashbuckle/Swagger; the 2022 prior art predates it, and the caveat is recorded so nobody ports the Swagger-era wiring forward. The facade controllers author the contract document (§4.5); the document describes both representations and neither union:

- **`Result<T>` unwrapping (requests):** request schemas present the underlying scalar's schema (`Result<DateOnly>` → `string`/`date`); `Result<T>?` members leave the `required` list; request schemas mark `writeOnly`, response schemas `readOnly`. Schema metadata (type, format, pattern, example) is read from static abstract interface members on the scalar types — no reflection, per doctrine.
- **`Outcome<T>` unwrapping (responses):** response schemas present the payload type, never the envelope (§10.1).
- **XML metadata:** because Futhark's rules are fixed, the transformer stamps OpenAPI's `xml` object mechanically. **Correction (Task 11, verified live against the resolved package):** the classic `attribute`/`wrapped` boolean pair this section originally specified is `internal`+`[Obsolete]` in `Microsoft.OpenApi` 3.6.0 (OpenAPI 3.2) — replaced by a single `NodeType` enum. The transformer stamps `NodeType = Attribute` on every scalar and item element names from item types; no `wrapped` signal is emitted at all, because OpenAPI 3.2 defaults array `nodeType` to `none` specifically to preserve pre-3.2 "unwrapped by default" behavior — the law holds with zero code for the collection case. The negotiation story is visible to the buyer in the contract document itself.

## 13. Versioning posture

Strict rejection means additive contract changes break existing XML clients **by design** — the strict reader is the contract's enforcement arm, and that is a selling point, written down here so nobody rediscovers it as a bug report:

- Breaking additions ship as a **new URL path version** (`/v2/…`), never as silent tolerance, and never as XML-namespace versioning — Futhark documents carry no namespaces (§6.6, §8.1), so the namespace axis does not exist to abuse.
- A new required field on an existing contract version is definitionally breaking, and the reader makes it loud on day one instead of quietly divergent forever.

## 14. Build-time diagnostics catalog

One diagnostic per shape law, emitted by the generator/analyzer pair; IDs assigned at plan time in the platform's existing convention. **All fire in the host compilation (§2, §4) — by design, at the scene of the exposure.** All are errors:

| Law | Diagnostic condition |
|---|---|
| Direction | `Result<T>` reachable in a response closure; raw scalar reachable in a request closure |
| Cross-direction | A complex type reachable from both the request closure and the response closure — named directly as boundary-sharing, not left to fall out as a contradictory pair of scalar-law errors |
| Shape closure | Non-sealed complex type; base type other than `object`; generic contract type |
| Uniqueness | Two members of one complex type on a contract (any arity); post-case-transform name collision in any of the five styles |
| Taxonomy | Scalar type outside the closed set; dictionary member; scalar collection; nested collection |
| Binding | A facade action's body-bound type is not a `[DataContract]` |
| Enums | *(none at build time — undefined values are write-time by nature; the name table is total over defined members)* |

## 15. Test doctrine

- **Round-trip property tests are the spine:** for every supported shape, write→read→structural-equality, randomized/property-based where practical (`RandomNumberGenerator`, per house rule). Request shapes round-trip through the `Result` funnel (write unwraps success, read re-captures success); failed `Result` write attempts assert the throw. **Required `Result<string>` carrying `""` round-trips** — presence vs emptiness proven, not presumed (§8.2).
- **Lexical parity, both directions:** canonical emission asserted **byte-exact** against the §7 table on both channels, plus a shared accepted/rejected lexeme corpus per type asserted identical across JSON and XML — the reader's defined lexical space proven congruent, not assumed. Non-finite `float`/`double` lexemes (`NaN`, `INF`, `-INF`, `Infinity`) sit in the rejected corpus on both channels.
- **Shape-law violation tests:** each §14 diagnostic gets a generator test proving the build failure fires — the earliest boundary, proven, not presumed. Cross-direction sharing asserts the §14 diagnostic literally.
- **Exposure scoping:** a `[ServiceContract]` whose contracts violate XML shape law compiles clean in the host while **no facade controller action** touches those types; adding a controller action that does flips the build red. This is the exposure law's "wired not just designed" test.
- **Security corpus:** DTD/XXE payloads (external entity, internal entity expansion, parameter entities), depth bombs, encoding attacks — all must produce rejection, never resolution. Literal assertions on the failure class.
- **Error-path grammar tests:** §11.2's grammar and every catalog message format asserted literally.
- **The tri-protocol swoop (wired, not just designed):** one request record driven through gRPC, REST-JSON, and REST-XML against the live Yggdrasil host — success parity, failure parity on the text channels (same accumulated paths, details, **and payload shape**), and required-absent parity on gRPC. Removing the formatter registration fails the suite.
- **Symmetry-law wiring tests:** the REST `Outcome` fold and both OpenAPI union-unwrap transformers each fail the suite when their registration is removed (§10.4).
- **Library-controller tripwire test:** a `GrpcControllerBase` descendant introduced via a referenced assembly (no host-compilation source, no generated shape) fails startup with the named error, never a runtime 500 (§3).
- **Strictness parity test:** the same unknown-member payload rejected identically on JSON and XML.
- House rules apply throughout: warnings-as-errors, BOM-free UTF-8 LF generator output, suppression law, `ConfigureAwait(false)` in src, one test project per package.

## 16. Rejected and out of scope

- **Reflection engine or fallback** — rejected (§2).
- **New attributes** (`[XmlRequest]`/`[XmlResponse]` were drafted and abolished) — the `[ServiceContract]` taxonomy is the platform's single contract vocabulary; direction is positional (§4).
- **Residence in Svartálfheim or Asgard for the wire machinery** — struck; primitives never learn wire formats, the Æsir do not care about them. (The facade base is the ruled exception — §3; it is contract law, not wire machinery.)
- **Automatic gRPC transcoding** — rejected; the facade is hand-authored curation (§4).
- **Forward tolerance / unknown-member leniency** — rejected (§8.1); the consulting posture wants explicit contracts that reject loudly with a path.
- **Wrapper elements, `xsi:nil`, XML namespaces in contract documents, vendor media types, per-endpoint or negotiated casing** — all rejected as negotiation axes; the thesis is that there is nothing to negotiate.
- **Singularized item names** — rejected; type-name items are deterministic, and renaming a property never changes the wire shape of its items.
- **Polymorphic contracts** — banned outright; revisit only if a strangler-fig scenario ever demands it, as its own Forseti case.
- **Declaring empty strings illegal to write** — rejected; a domain restriction smuggled into a serializer (§8.2).
- **Protobuf-generated types and PII primitives (`IMaskedValue` implementers) reaching Futhark** — banned per transport doctrine; "protobuf-generated" reads as protoc/tooling-generated — the platform's hand-authored code-first `[DataContract]` records are the contracts and are welcome. PII crosses into contracts as wire strings (masked or unmasked per the disclosure verdict); Futhark serializes strings and never knows they were PII.
- **XML comments/PIs/CDATA as carriers of anything** — comments ignored on read and never written; PIs and CDATA rejected.

## 17. Plan-time details — flagged, not improvised

1. **Generator discovery predicate** for facade controllers: the `GrpcControllerBase` descendant set in the host compilation is the natural key.
2. **Binding-source discrimination** (`[FromBody]` and ASP.NET Core inference rules) for closure membership.
3. **Non-`[DataContract]` body type** behavior: the §14 binding diagnostic.
4. **Generator project name** under Midgard `gen/`, aligned with the existing Midgard generator family.
5. **protobuf-net open-generic surrogate support** — verify; the closed-set fallback (~13 registrations) is designed and acceptable (§9.3).
6. **Body-size number:** 1 MiB proposed for the facade default; confirm against expected contract sizes before it hardens into doctrine.
