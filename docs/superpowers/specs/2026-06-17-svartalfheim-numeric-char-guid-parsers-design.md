# Svartalfheim — Numeric, `char` & `Guid` Parsers (Third Increment)

**Date:** 2026-06-17
**Status:** Approved in design session; plan pending
**Owner:** Buvy
**Parent specs:** `2026-06-11-svartalfheim-pathway-proof-design.md` (its §7 ledger item 2 — "additional parsers" — is this spec's docket) · `2026-06-11-svartalfheim-result-union-boolean-parser-design.md` (the `BooleanParser` template this increment generalizes)

---

## 1. Motivation

The first increment proved the union shape; the second proved the pathway (gateway in, combinators through, evidence out). This increment spends the pathway: it forges the **balance of the BCL scalar struct parsers** the ecosystem actually ingests — the numeric family, `char`, and `Guid` — each carrying the real-world vocabulary that untrusted sources send and the bare BCL `TryParse` lacks.

`BooleanParser` was declared "the precedent for ~20 more." This increment is most of that docket. It is deliberately **not** a transcription of the Crucible's prior art: the Crucible hand-wrote one class per type because it predated generic math, and it wrapped a culture-guessing `FormatHint` heuristic plus a post-parse constraint engine that the platform's own laws now forbid. We migrate the **vocabulary**, not the machinery — and we forge that vocabulary once, twice generic.

Everything here remains **scalar→domain conversion only** (Svartalfheim charter). Range/precision/allowed-value validation is the mediator's job, not the forge's; the closed `Empty`/`Malformed` failure set is unchanged.

## 2. Scope

### In scope

Thirteen specialists across three shapes, plus their gateway routing and tests:

- **Integer family (8):** `byte sbyte short ushort int uint long ulong` — one generic `IntegerParser` over `where T : IBinaryInteger<T>`.
- **Real family (3):** `float double decimal` — one generic `RealParser` over `where T : IFloatingPoint<T>`. (`decimal` implements `IFloatingPoint<decimal>` though not `IFloatingPointIeee754<decimal>`; the build proves the constraint or fails loud.)
- **`char`** — `CharParser` (culture-insensitive; no provider).
- **`Guid`** — `GuidParser` (culture-insensitive; no provider).
- Gateway routing: a `typeof` branch per concrete type in `Parser.ParseRequired` / `ParseOptional`, so the vocabulary is reachable through the generic gateway as well as the named specialist.
- `Primitives.Tests` coverage mirroring the Crucible test-matrix shape, plus gateway-routing tests; AOT smoke extended to exercise the new paths.

### Deferred — its own spec

The **temporal family** (`DateOnly DateTime DateTimeOffset TimeOnly TimeSpan`). Dates carry a **format axis** the numeric/`char`/`Guid` types do not: even with a *declared* culture, `DateTime.TryParse(span, provider)` accepts a culture's whole format buffet (`1/2/2026`, `January 2, 2026`, `2026-01-02`), which is looser than §2.6's "exactly one accepted representation per ingress path, declared up front." Honoring §2.6 means exact-format pinning — a declared format parameter the gateway's uniform `(span, provider)` signature does not carry, forcing pinned date specialists to be directly-called outside the gateway. That fork, plus Unix-timestamp support, deserves its own convergence. **`TimeSpan` defers with the family** to keep the temporal surface congruent rather than shipping one temporal type alone.

### Out of scope — the skeletons, recorded so nobody re-migrates them silently

- **The Crucible `FormatHint` / culture auto-detect heuristic.** Its Auto mode tried US then European and *guessed* — the exact anti-pattern Glitnir §2.6 ("no silent date-culture inference") and §8 forbid. Norse already ruled the opposite: culture arrives as the **required `provider`**, declared out loud at the call site. There is no `FormatHint` type, no Auto mode, no normalization regex.
- **`ParseConstraints` (Min/Max/Precision/Scale/MaxLength/allowed-values) and the 7-way `ParseErrorType`.** These are post-parse *validation* (`RangeFailure`, `PrecisionFailure`, `ValueFailure`, `LengthFailure`), not parsing. Svartalfheim is scalar→domain only; validation belongs to the mediator. `ParseFailure` stays the closed `Unspecified`/`Empty`/`Malformed` set — no new members.
- **`Guid` internal-separator normalization** (`_`/`:` → `-`). Non-standard and ambiguity-adjacent; prefix stripping is the only `Guid` leniency that earns its place.
- **The eleven duplicated per-type numeric classes.** Replaced by two generic cores.

## 3. The numeric cores

Both cores follow the established parser template (the `BooleanParser` precedent, generalized): a static class, `ParseRequired(ReadOnlySpan<char>, IFormatProvider) → Result<T>` and `ParseOptional(…) → Result<T>?`, a shared private `Parse`. **Provider is required and non-nullable** — numeric text is culture-sensitive (thousands and decimal glyphs, currency symbol), so the call site declares its culture (`CultureInfo.InvariantCulture` out loud) or it does not compile. No defaulting overload, ever.

Choreography per call: trim → empty/whitespace is `Failure(Empty, "", typeof(T).Name)` (required) or `null` (optional) → detect special notation → `T.TryParse(span, styles, provider, out var value)` → `Success<T>` or `Failure(Malformed, <trimmed, bounded>, typeof(T).Name)`. Truncation knowledge stays in `Failure` (span ctor overload bounds to `MaxInputLength`); the core never pre-truncates. `Format`/`Detail` stay null — these specialists have no format axis.

**The governing principle (owner's ruling, this session):** the numeric parser accepts any representation the .NET runtime can round-trip. A value never fails parse merely for arriving in a legal-but-unusual notation — so a hex or thousands-grouped value shipped over `Asgard.Egress` or into `Yggdrasil.Api` lands cleanly in `Success<T>` rather than being rejected for its notation.

### 3.1 `IntegerParser` — `where T : IBinaryInteger<T>`

- **Base styles:** `NumberStyles.Integer | AllowThousands | AllowParentheses | AllowCurrencySymbol`. The provider's `NumberFormatInfo` supplies the glyphs (thousands separator, currency symbol); `AllowParentheses` admits accounting negatives `(1,234)` → `-1234`.
- **Hex:** a leading `0x` or `&H` (case-insensitive) routes to `NumberStyles.HexNumber` (culture-insensitive). The value is parsed across the type's full width — `0xFFFFFFFF` is `uint.MaxValue`, and a value exceeding the type is `Malformed`.
- **Binary:** a leading `0b` routes to `NumberStyles.BinaryNumber` (culture-insensitive).
- **Exponent:** `AllowExponent` is on — `1e3` → `1000`. A non-integral exponent result (`1.5e0` → `1.5`) is `Malformed`, because the target is an integer. *(Owner ruling: keep exponent on integers — matches the Crucible and the round-trip principle.)*
- **Range** is the type's own: `byte.TryParse("256", …)` returns false → `Malformed`, for free. No `ParseConstraints` — range is the type, not configuration.

### 3.2 `RealParser` — `where T : IFloatingPoint<T>` (`float`, `double`, `decimal`)

- **Base styles:** `NumberStyles.Number | AllowExponent | AllowParentheses | AllowCurrencySymbol` (thousands, decimal point, sign, scientific, currency, accounting negatives).
- **Percentage:** a trailing `%` is stripped and the parsed value divided by 100 — `"50%"` → `0.50`. This transforms the value, deliberately, and is exposed on all three real types.
- **Non-finite values are failures** *(owner ruling, revised 2026-06-17)*: `NaN`, `Infinity`, and `-Infinity` are `Malformed` for `float`/`double` — whether they arrive as the literal symbol (`"NaN"`, `"Infinity"`) or as the result of magnitude overflow (`"1e400"` → `Infinity` → `Malformed`). A parsed value that is not finite is rejected via `INumberBase<T>.IsFinite`; the forge admits only finite reals. This is a value-domain rejection layered over the round-trip notation principle — a *representation* the runtime can round-trip is still refused when the *value* is non-finite.
- **Overflow is uniform** *(owner ruling, revised 2026-06-17)*: an overflowing `decimal` is `Malformed` because `decimal.TryParse` itself returns false; an overflowing `double`/`float` parses to `Infinity` and is then caught by the finite guard — also `Malformed`. No asymmetry: overflow fails loud across all three real types.
- **Digit guard:** the Crucible's fail-fast survives — input with an absurd significant-digit count is rejected as `Malformed` before the parse attempt, so a pathological cell cannot drive an expensive parse path.

## 4. `char` & `Guid` — culture-insensitive specialists

Both take **no provider** (honest signatures — a provider parameter documented as ignored is a lie). Both follow the `ParseRequired`/`ParseOptional` template with the empty⇒`Empty`/absent rule.

### 4.1 `CharParser`

Precedence, in order:

1. **Single literal char** — input of length 1 *is* that char, with **no trim** (so `" "` parses to a literal space, `"6"` to `'6'`). This precedes any trimming, so a meaningful whitespace char is never eaten.
2. **Decimal code point** — `"65"` → `'A'`, validated to the UTF-16 range `0–65535`.
3. **Hex / `U+` code point** — `"0x41"`, `"&H41"`, `"U+0041"` → `'A'`.
4. **HTML entity** — `"&#65;"` (decimal) / `"&#x41;"` (hex) → `'A'`.
5. Otherwise `Malformed`.

Empty/whitespace-only input of length ≠ 1 follows the standard `Empty`/absent rule. (Note the deliberate consequence of rule 1: `"5"` is the literal `'5'`, never code point 5.)

### 4.2 `GuidParser`

Trim → strip a leading case-insensitive `urn:uuid:` / `GUID:` / `UUID:` prefix if present → `Guid.TryParse` (all five BCL formats: N, D, B, P, X). No internal-separator normalization, no internal whitespace removal. A URN-shaped or prefixed id lands in `Success<Guid>`; anything `Guid.TryParse` rejects after prefix stripping is `Malformed`.

## 5. Gateway integration

Thirteen new `typeof` branches join `Parser.ParseRequired` and `Parser.ParseOptional` — one per concrete type, the model the pathway spec §2.3 fixed ("new specialists join by adding a `typeof` branch in exactly one place; dispatch never becomes data"). Each branch sits **before the generic trim** (the `bool` precedent), so the specialist owns its own trimming, and `Unsafe.As`-reinterprets the specialist's `Result<concrete>` to `Result<T>` (sound because `T` is statically the concrete type inside the JIT-eliminated branch — the established BCL generic-specialization pattern).

- **Numeric branches** name their concrete type and call the generic core with it: `if (typeof(T) == typeof(int)) { var r = IntegerParser.ParseRequired<int>(input, provider); return Unsafe.As<…>(ref r); }` — eight integer branches to `IntegerParser`, three real branches to `RealParser`.
- **`char` / `Guid` branches** do **not** forward the provider (culture-insensitive — exactly as `bool`). The gateway's uniform non-null provider check still precedes all routing, unchanged: `Parser.ParseRequired<char>(span, provider)` requires a non-null provider it then ignores, identical to today's `bool` behavior.
- **Diagnostics:** `ExpectedType` uses CLR type names (`"Int32"`, `"Double"`, `"Char"`, `"Guid"`, …); `Format`/`Detail` stay null.

The net effect: the vocabulary is reachable both ways — `IntegerParser.ParseRequired<int>(span, Invariant)` for the direct, generic-named call, and `Parser.ParseRequired<int>(span, Invariant)` for the one-call-fits-any-`T` gateway, with `"$1,234"` succeeding through either.

## 6. Tests & evidence

`Primitives.Tests`, xUnit v3 + Shouldly on Microsoft.Testing.Platform, `Should_{behavior}_when_{condition}`, `public sealed` classes, Shouldly/Xunit usings global. The matrix mirrors the Crucible's shape, per family:

- **Integer** (`IntegerParserTests`, run across all eight types for boundary proof): basic ±, leading `+`, parentheses-negatives, thousands under a US provider and under a European provider, currency symbol, hex (`0x`/`&H`, including full-width wrap), binary (`0b`), integral exponent; invalid — overflow per type, non-integral exponent, decimal point, garbage; empty/whitespace ⇒ `Empty`/absent.
- **Real** (`RealParserTests`): decimals, thousands (US/European provider), currency, parentheses, scientific, percentage, whitespace; invalid — `NaN`/`±Infinity` literals ⇒ `Malformed`, `double`/`float` overflow-to-`Infinity` ⇒ `Malformed`, `decimal` overflow ⇒ `Malformed`, digit-guard rejection, garbage; the finite guard asserted explicitly so the non-finite rejection is pinned, not accidental; empty/whitespace ⇒ `Empty`/absent.
- **`char`** (`CharParserTests`): single literal incl. whitespace char, decimal code point, hex/`U+` code point, HTML entity; invalid — out-of-range code point, multi-char non-coded input, garbage; the `"5"`-is-literal precedence pinned; empty ⇒ `Empty`/absent.
- **`Guid`** (`GuidParserTests`): N/D/B/P/X formats, each accepted prefix (`urn:uuid:`/`GUID:`/`UUID:`), surrounding whitespace; invalid — bad hex, malformed-after-prefix; empty ⇒ `Empty`/absent.
- **Gateway routing** (`Parser` tests): `Parser.ParseRequired<T>` reaches the specialist vocabulary for a representative integer, real, `char`, and `Guid` — e.g. `"$1,234"` ⇒ `Success<int>`, a prefixed `Guid`, a code-point `char` — proving the branches wire the vocabulary through, not just the bare `TryParse`.

**AOT smoke** (`tests/smoke/Primitives.Aot.Smoke`) is extended to exercise an `IntegerParser`/`RealParser` core, `CharParser`, and `GuidParser` through the gateway: generic-math static-virtual dispatch must stay AOT-clean (zero warnings, exit 0).

**Benchmarks** are an *optional* light filing (one core vs bare `TryParse` to confirm the vocabulary detection is not a hot-path tax), not a gate for this increment — keeping scope tight per the owner's "don't get carried away" framing. Any finding filed as a pathway-spec amendment in Glitnir, never a loose note.

## 7. Repository shape after this increment

New under `src/Primitives/`: `IntegerParser.cs`, `RealParser.cs`, `CharParser.cs`, `GuidParser.cs`. Edited: `Parser.cs` (thirteen branches). New under `tests/Primitives.Tests/`: `IntegerParserTests.cs`, `RealParserTests.cs`, `CharParserTests.cs`, `GuidParserTests.cs`, and gateway-routing additions to the existing `Parser` test class. AOT smoke updated. No new projects.

## 8. Acceptance criteria

1. `Parser.ParseRequired<T>` / `ParseOptional<T>` route all eight integers, all three reals, `char`, and `Guid` to their specialists, with the documented vocabulary reachable through the gateway.
2. Each specialist honors the template: `Empty`/absent on empty, `Malformed` on unrecognized, bounded capture via `Failure`'s span ctor, CLR-name `ExpectedType`.
3. Provider required and non-nullable on the numeric specialists and the gateway; `char`/`Guid` specialists take no provider.
4. `ParseFailure` unchanged (no new members); no `ParseConstraints`, no `FormatHint`, no validation surface.
5. `dotnet build Svartalfheim.slnx` clean at WarningLevel 9999; `dotnet test Svartalfheim.slnx` green; AOT smoke publishes with zero warnings and exits 0.

## 9. Deferred (this spec's ledger)

1. **The temporal family** (`DateOnly DateTime DateTimeOffset TimeOnly TimeSpan`) — its own spec, carrying the format-axis decision (exact-format pinning vs. flexible), Unix-timestamp support, and the directly-called-vs-gateway fork.
2. **`Combine`, async combinator siblings, `*Present` variants** — still awaiting their consumer (carried forward from the pathway ledger).
3. **NuGet packaging metadata** — when something consumes the package.
4. **`YGG201` `[MustConsume]` enforcement and the `default(Result<T>)` analyzer rule** — the architecture-analyzers spec, unchanged.
