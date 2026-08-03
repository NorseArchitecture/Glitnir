# Futhark Enum Wire Law — Governed Names Everywhere, Flags Stay Home

**Date:** 2026-08-02
**Status:** **Ratified** — operator verdict rendered in session 2026-08-02, closing the named remainder of the Futhark postmortem gap pass (`../plans/2026-08-02-futhark-xml-serialization.md`, "Postmortem gap closure" item 5). Next gate: `/writing-plans` (human gate; nothing proceeds until it passes).
**Supersedes in part:** `2026-08-01-opinionated-xml-serialization-design.md` — the §6.5 `[Flags]` wire grammar (space-separated list, greedy decomposition, canonical-form selection, duplicate-flags-token accumulable, leftover-bits write law, and the default-initialized-flags sharp edge) is struck for facade-exposed contracts; §7's enum row narrows to non-flags enums; §12 gains the governed-list stamping obligation. The parent spec carries pointer notes at each struck site.

---

## 1. Context — the remainder this closes

The Futhark postmortem gap pass (2026-08-02) closed four of five deferred gaps and reduced the fifth to one named open design: **enums have no JSON wire law.** `Result<TEnum>` has no JSON request funnel (`ResultJsonConverter<T>` is `ISpanParsable<T>`-constrained, which no enum satisfies; `ResultJsonConverterFactory` refuses with a named `NotSupportedException`), and plain response enums ride STJ defaults — numerics — in direct violation of §7's "case-styled member names on both channels." The lexical corpus consequently covers 12 of 13 §7 rows.

The blocker was structural: the name tables and `NameCasing` live in the XML shape generator and emit into the host compilation; Midgard's runtime JSON converters can see neither. Closing the gap forced two decisions — the mechanism (where names come from at runtime) and the flags question (what a set of named options looks like to a stranger).

## 2. The ruling

### 2.1 Non-flags enums: governed strings, one name, zero attributes

Restated as the standing law it already was, with one explicit rejection added:

- Wire form is the **case-styled member name** under the host's one configured style, identical lexeme on both text channels (§7). Integers never appear on a text channel in either direction; the JSON reader rejects **number tokens** on enum members as malformed — names-never-numerics is enforced, not merely emitted.
- **No `[EnumMember]`-style wire-name override attribute.** The historical habit of pinning API text constants in attributes was a manual workaround for serializers with no deterministic naming policy — it forces maintaining two names per member. Futhark's name tables *are* the policy: one name maintained (the C# member), the wire string derived by `NameCasing`, post-transform collisions caught at build time (NORSE026). Zero-attributes stays ratified.
- **Named trigger for revisiting:** the first genuine external-vocabulary case — a partner-mandated wire word that no casing of a legal C# member name can produce. Until a real one exists, an override attribute is a negotiation axis and is rejected before evaluation.

### 2.2 `[Flags]` enums are banned from the facade closure

**The verdict.** Bitwise flags are interior compression. At the ambassador desk — where text channels serve integrators who should never need to understand bit composition — the concept is *a set of named options*, and Futhark already has exactly one set shape: a collection of a role-named record (§5.8's wrap-the-scalar law, unchanged). A `[Flags]` enum reachable from either closure of a facade action is a **build-time diagnostic** (ID assigned at plan time from the platform's live block — expected next-in-sequence after NORSE028, confirmed against shipped IDs then, per the collision lesson recorded in the parent plan):

> *flags don't translate to strangers — model the option set explicitly*

- The boundary models a multi-select as the platform's one set shape, all three channels: `<accessGrant kind="read" /><accessGrant kind="write" />` on XML, `"grants": [{"kind":"read"},{"kind":"write"}]` on JSON, a `repeated` message on protobuf. The item record wraps a **non-flags** enum member.
- The handler maps boundary set ↔ interior flags explicitly. Domain and messaging interiors keep `[Flags]` freely — the ban is **exposure-scoped**, exactly like every other Futhark law: a gRPC-only contract, or an unexposed method of an exposed service, answers to no facade law and keeps flags on the wire as a single composed varint.
- **The ratified price, stated so it is never rediscovered as a bug:** a facade-*exposed* contract is channel-shared, so its gRPC leg also carries the set shape rather than a single composed integral — the tri-channel multi-checkbox operation gets a bulkier protobuf representation. Ruled acceptable 2026-08-02: one price for a smaller, slimmer, more consistent surface, and the overwhelming majority of gRPC traffic (internal, gRPC-only contracts) is untouched.

**What this deletes.** The entire §6.5 flags subtree for text channels: greedy decomposition and canonical-form selection at write, the duplicate-flags-token accumulable at read, the leftover-bits illegal-to-write law, the xsd:list idiom, and the default-initialized-flags-with-no-zero-member throw edge — including the already-generated per-enum `ParseFlags` machinery in the XML shape generator (pre-launch, no consumers; the gateway-retirement precedent applies to deleting shipped machinery that a better ruling obsoletes).

**What this keeps.** The undefined-value laws for non-flags enums are unchanged (unknown name → accumulable with suggestion on read; undefined value → illegal to write). On the gRPC leg, `ResultEnumSerializer<TEnum>` ships as-is — varint wire form, undefined values and flags leftover bits funneling to the typed `Failure` — because gRPC-only contracts may legally carry `Result<TEnum>` flags members; its flags branch simply never fires for facade-exposed contracts once the diagnostic exists.

### 2.3 The mechanism: tables are generated data, the algorithm lives once in Midgard

The generate-tables/own-mechanism split (Option D of the session's fork analysis) is ratified:

1. **The generator emits per-enum name tables only** — the five-style precomputed columns it already builds — plus a registry surface handed to the host's composition root, and **stops emitting per-enum parse/write logic**. Generator emission shrinks; its jurisdiction (the facade closure walk) is unchanged.
2. **Midgard owns `EnumLexical`** — the one format/parse mechanism over a table and a style index. With flags banned from the closure, it collapses to table lookup both directions: format is a column read; parse is an exact-match scan yielding `Success<TEnum>` or the platform's typed `Failure`. No reflection anywhere; the tables are compile-time data.
3. **Three consumers, one source:** the generated XML shapes fold onto `EnumLexical` (they already compile against Midgard's seam — `XmlLexical`, `XmlReadContext` — so this is the established pattern, not a new door); the new JSON converters (plain enum + the `Result<TEnum>`/`Result<TEnum>?` funnel family) consume the registry; the OpenAPI transformers read the same tables to stamp governed lists (§2.5). Cross-channel parity is by construction — one `NameCasing`, one algorithm, one table.
4. **Fail loud, never numeric:** the JSON converter factory consults the registry; an enum with no table is a named startup/serialization error, mirroring the XML output formatter's loud refusal for unregistered types. STJ's silent numeric default is dead permanently.
5. **The composition seam** gains the registry on the JSON side, symmetric with `AddNorseXml(style, registry)`. Constraint, stated as law: **the host states the case style exactly once** — the seam must make it structurally impossible to hand the XML and JSON legs different styles. Exact seam shape (one combined call, one options object, or JSON resolving the XML-registered style) is a plan-time detail (§5).

### 2.4 JSON read/write posture for enum members

- Write: the case-styled name as a JSON string, byte-identical lexeme to the XML attribute value.
- Read: **exact match against the host's style column only.** Case-insensitive tolerance would be forward tolerance — rejected (§8.1 of the parent spec governs). Off-list string → `Failure` (malformed). Number token → `Failure` (malformed) — never funneled through invariant-stringify like true numerics. JSON `null` → the `ParseRequired(string.Empty)` required-missing funnel for `Result<TEnum>`, `null` for `Result<TEnum>?` — presence semantics identical to every other row (§8.2).
- Plain (response-side) enum read — round-trip suites and future XML/JSON-consuming clients — uses the same exact-match table scan; a malformed value there throws the channel's deserialization error, mirroring `LexicalScalars`' posture for plain scalars.

### 2.5 OpenAPI: the governed list is the deliverable

The transformers stamp **`enum:` string lists in the host's case style** onto every enum-typed schema — plain response members and `Result<TEnum>`-unwrapped request members alike, as JSON Schema's `enum` keyword on a `type: string` schema. This is the partner-facing half of the ruling: pick from a governed list in the document, never map an integer — and because `enum:` is the vocabulary every mainstream client generator (Kiota, NSwag, openapi-generator) already turns into a real typed enum, the partner's own codegen hands them a governed picklist in their language. The stamping transformer is DI-activated and reads the **same generated name-table registry** the XML shapes and JSON converters consume — one source, three projections (§2.3.3). A multi-select renders as the collection schema it now is (`type: array` → items `$ref` to the role-named record whose member carries the `enum:` list) — no per-media-type schema fork exists anywhere, because the flags ban removed the only member shape that would have needed one.

### 2.6 First-party clients (WASM/MAUI): no flags ride up, no interceptor shredding

Walked explicitly so the question never resurfaces as an interceptor feature request: the `[Flags]` type never enters the client. A multi-checkbox UI binds set membership (a `HashSet` of the non-flags kind enum, enumerable client-side via `Enum.GetValues` — compile-time, no reflection concern) and the callsite builds the wire collection. Interceptors are structurally incapable of the conversion (they see the typed message whole; the contract member *is* the collection) and doctrinally barred from it (`OutcomeClientInterceptor` decodes `Outcome<T>`, nothing else). Set → interior-flags composition happens in the handler, once, beside the domain that owns the flags type.

## 3. Test doctrine deltas

- **The lexical corpus reaches 13 of 13 §7 rows:** `Result<ParityStatus>` lands on `ParityRequest`, enum lexemes (accepted: governed names; rejected: off-list strings, wrong-case names, numeric spellings) asserted identical across both text channels; the gRPC leg already carries the enum wire law from the postmortem gap pass.
- **The flags diagnostic gets a generator test** proving the build failure fires for a flags enum reachable from either closure, plus the exposure-scoping negative (flags on an unexposed contract compiles clean).
- **A registry-miss test** proves the JSON converter's named refusal (no silent numerics), replacing the interim `NotSupportedException` guard's test.
- **An OpenAPI wiring test** asserts the governed `enum:` list appears in the live host's document for both a request-side and a response-side enum member — the "wired not just designed" law applied to §2.5.
- Cross-channel byte-parity of the enum lexeme (XML attribute value vs JSON string) asserted literally, per row, in the corpus.

## 4. Rejected in this ruling

- **Space-separated flags string on JSON** (parent spec §6.5 as ratified) — non-idiomatic JSON; partners' deserializers need custom code, the exact friction the text channels exist to avoid.
- **Channel-native flags composition** (JSON array of bare names, XML xsd:list) — pierces the identical-content law and carves a flags-only exception to the scalar-collection ban, and forces per-media-type OpenAPI schema forking. Rejected in favor of the ban; recorded as the fallback that was *not* taken.
- **`[EnumMember]`-style override attribute** — rejected with a named trigger (§2.1).
- **Runtime reflection name tables** — one-time reflection is doctrinally allowed, but two casing implementations (or two compilations of shared source) plus metadata-order vs declaration-order divergence on canonicalization make parity rest on tests instead of construction. Rejected as the default; named fallback only for a hypothetical JSON surface with no host compilation, and then strictly as a reflection-built *table* fed to the same `EnumLexical` — the algorithm never forks.
- **Amending §7 to PascalCase-everywhere enums** — a snake_case document carrying `ReadWrite` values is the "nobody made a decision" look the ethos bans, and it saves almost nothing (the funnel converter family is needed regardless).

## 5. Surfaced, not ruled here — the first-party request-write wall

Walking §2.6's WASM flow exposed a question **larger than enums, deliberately not ruled in this document**: the Task 13 hardening made `Result<T>` serialization throw unconditionally — every state, every channel — which means the typed gRPC client proxy **cannot author a request against any facade-exposed (Result-wrapped) contract**. The tri-protocol swoop only works via a hand-built plain-field mirror contract (test infrastructure); a real WASM/MAUI client driving a tri-channel operation hits the throw. The parent spec's §9.3 originally said client-side serialize unwraps success and throws only on failure/default; the implementation deliberately hardened past that mid-task, and no real first-party client had yet walked into the wall. Candidate arms, recorded for the next session: restore §9.3's client-write law (success unwraps; ergonomic if `Result<T>` carries an implicit conversion from `T`), or rule that first-party clients never speak facade contracts (own plain-membered internal contracts — contract-duplication smell). **Ruled same day:** the first arm, ratified as `2026-08-02-result-success-unwrap-on-serialize-design.md` — success unwraps on serialize (the union still never rides the wire), failure and default still throw, implicit `T → Result<T>` conversion in Svartálfheim, binding-shadow pattern for Blazor forms.

## 6. Plan-time details — flagged, not improvised

1. Diagnostic ID for the flags ban — next in the live NORSE0xx block, confirmed against shipped IDs at plan time.
2. Registry and table shapes (the generated-data ↔ `EnumLexical` contract) and their namespace residence in the host compilation.
3. The composition-seam shape enforcing the state-the-style-once law (§2.3.5), including whether `AddNorseJson` grows parameters or the pair collapses into one call.
4. `EnumLexical` API surface and how generated XML shapes consume it (direct static calls, per the `XmlLexical` precedent).
5. Migration order for deleting the generator's per-enum parse/write emission and the swoop/parity fixture updates (corpus row 13, `ParityRequest` change ripples to validator, handler, wire fixture).
6. Whether `ResultRules`' required-rule wording needs an enum-specific message source touch (expected: no — `FailureDetail.Render` is type-agnostic; verify).
