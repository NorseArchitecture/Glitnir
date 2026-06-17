# Svartalfheim — Temporal Parsers (Fourth Increment)

**Date:** 2026-06-17
**Status:** Approved in design session; plan pending
**Owner:** Buvy
**Parent specs:** `2026-06-17-svartalfheim-numeric-char-guid-parsers-design.md` (its §9 ledger item 1 — "the temporal family" — is this spec's docket) · `2026-06-11-svartalfheim-pathway-proof-design.md` (the gateway and `(span, provider)` signature this increment forks) · `2026-06-11-svartalfheim-result-union-boolean-parser-design.md` (the parser template generalized here)
**Prior art consulted:** the Crucible `Data.Tool` temporal parsers (`DateOnlyParser`, `DateTimeParser`, `DateTimeOffsetParser`, `TimeOnlyParser`, `TimeSpanParser`, `DateParserHelpers`, `FormatHint`) — cited by name, migrated for vocabulary only, never lifted.

---

## 1. Motivation

The third increment forged the balance of the BCL scalar struct parsers — the numeric family, `char`, and `Guid` — and deferred the **temporal family** (`DateOnly`, `DateTime`, `DateTimeOffset`, `TimeOnly`, `TimeSpan`) because dates carry a **format axis** the others lack. Even with a *declared* culture, `DateTime.TryParse(span, provider)` accepts that culture's whole format buffet (`1/2/2026`, `January 2, 2026`, `2026-01-02` all at once), which is looser than Glitnir §2.6's "exactly one accepted representation per ingress path, declared up front." This increment spends the deferral: it forges the five temporal specialists, resolves the format-axis fork, and closes a standing §2.6 hole the gateway carries today.

**The standing hole.** Every temporal type satisfies `ISpanParsable<T>`, so today they fall straight through `Parser`'s generic tail to `T.TryParse(span, provider)` — the flexible culture buffet. The generic gateway is **already** committing the ambiguous, culture-sensitive date parse §2.6 forbids, silently, for any caller who routes a temporal type through it. This increment is therefore *forced* to give temporal explicit gateway treatment; "add five parsers" is the smaller half of the work.

**The Crucible thesis, confirmed.** The prior art hand-wrote a `FormatHint` enum (`Auto`/`Invariant`/`UnitedStates`/`European`), an Auto mode that tried US then European culture and *guessed*, an `ambiguityFunc` heuristic (`Day < 13 && no-alpha` → ambiguous), and the `DateParserHelpers` machinery that orchestrated a three-strategy fallback. **Mandating the provider deletes all of it.** Culture arrives as the required `IFormatProvider` (settled in the numeric increment); there is no `FormatHint`, no Auto mode, no dual-culture attempt, no ambiguity heuristic, no normalization regex. What survives is *vocabulary* — the curated ISO format lists and the `NoCurrentDateDefault` fail-loud nugget — not machinery.

Everything here remains **scalar→domain conversion only** (Svartalfheim charter). Range/precision/allowed-value validation is the mediator's job; the closed `Empty`/`Malformed` failure set is unchanged.

## 2. Scope

### In scope

Five specialists, their doors, gateway routing, and tests:

- **`DateOnlyParser`, `DateTimeParser`, `DateTimeOffsetParser`, `TimeOnlyParser`, `TimeSpanParser`** — one static class per type (generic math does not span the temporal types; they share no parse surface, so no generic core).
- **A `UnixPrecision` enum** (`Seconds = 1`, `Milliseconds = 2`) — the declared-unit token for epoch parsing.
- **Gateway routing:** five `typeof` branches in `Parser.ParseRequired` / `ParseOptional`, each routing to the specialist's ISO-canonical door.
- **`Primitives.Tests`** coverage per type plus gateway-routing tests; AOT smoke extended.

### What melts from the Crucible (recorded so nobody re-migrates it)

- **`FormatHint` and the culture-guessing axis.** Replaced by the required `provider`. No enum, no Auto mode.
- **The `ambiguityFunc` / Auto-mode heuristic** (`Day < 13 && no-alpha`, the `RemoveTimeSuffix` regex feeding it). The literal §2.6/§8-forbidden guess. Gone.
- **Strategy-2 flexible `TryParse(span, culture, style)`** — the culture buffet. Replaced everywhere by `TryParseExact`.
- **Most of `DateParserHelpers`** — the `TryParseDateFunc`/`TryParseDateExactFunc` delegate dance and the `extension(string)` block existed only to orchestrate the three-strategy fallback. Collapses to near nothing on a single exact path.
- **The magnitude-based Unix auto-detect** (`9–11 digits ⇒ seconds, 12–14 ⇒ ms`). An inference of the same family as culture-sniffing — it guesses *both* "this is a timestamp" and "this is the unit" from digit count. Replaced by an explicit declared-unit door (§7).
- **`ParseConstraints` / the 7-way `ParseErrorType`.** Already ruled out in the third increment — post-parse validation is the mediator's. `ParseFailure` stays `Unspecified`/`Empty`/`Malformed`.

### Out of scope — its own future spec or ledger

- **Multi-format exact parsing** (`string[]` of accepted formats). §2.6 is "exactly one accepted representation per ingress path"; the exact door takes a single required `string format`. A `string[]` overload lands on the ledger if a real consumer demands it.
- **Bare unit shorthand for durations** (`"90m"`, `"1.5h"`). Not migrated; ambiguity-adjacent and unearned. ISO-8601 duration is the structured form we accept (§8).
- **ISO-8601 duration year/month components** (`P1Y`, `P2M`). Not fixed durations — `Malformed` (§8).

## 3. The four doors

Each temporal specialist exposes a subset of four doors. All follow the established template (static class, `Result<T>`/`Result<T>?` returns, the empty⇒`Empty`/absent rule), and all are non-throwing on bad input.

| Door | Signature | Reachable via gateway? | On |
|---|---|---|---|
| **ISO canonical** | `ParseRequired(span)` / `ParseOptional(span)` | **Yes** — the gateway routes here | all five |
| **Declared exact** | `ParseExactRequired(span, format, provider)` / `ParseExactOptional(span, format, provider)` | No — directly called | all five |
| **Unix epoch** | `ParseUnix(span, UnixPrecision)` / `ParseUnixOptional(span, UnixPrecision)` | No — directly called | `DateTimeOffset`, `DateTime` |

**The through-line:** the gateway speaks exactly one unambiguous machine language — ISO 8601, UTC-normalized. The declared-format and Unix doors are explicit, named, off-gateway calls. A caller who wants a US slash date or a Unix epoch *says so* at the call site, by name; a caller who routes a temporal type through the generic `Parser` gets ISO 8601 or `Malformed`, nothing guessed.

**Provider is required and non-nullable on the exact door only** — declared-format parsing is culture-sensitive (named months, AM/PM, separators). The **ISO and Unix doors take no provider**: ISO 8601 is culture-invariant (parsed under `CultureInfo.InvariantCulture`, Gregorian forced — honoring a passed-in calendar would *mis*parse a year, so the provider must not be honored) and epoch is culture-insensitive. This is the same honest-signature stance as `CharParser`/`GuidParser` — a parameter documented as ignored is a lie. The gateway's uniform non-null provider check still precedes routing, so even these culture-insensitive routes demand a declared culture at the call site, exactly as `bool`/`char`/`Guid` do today; the gateway simply does not forward it.

## 4. ISO-canonical door (gateway path)

`TryParseExact` against a **curated profile** under `CultureInfo.InvariantCulture` — a small fixed list of ISO-8601 shapes that differ only in optional fractional seconds and (for the time-bearing types) zone form, never in field order, separator, or culture. The §2.6 evil is culture *ambiguity* (`01/02` = Jan-2 or Feb-1?); ISO 8601 has none of that regardless of precision, so "exactly one representation" is read at the ISO family level. `Failure.Format` = `"ISO 8601"` on every ISO-door failure.

Accepted shapes per type:

- **`DateOnly`:** `yyyy-MM-dd` only. A time-bearing string (`2026-01-02T…`) → `Malformed` — never silently truncated to the date.
- **`TimeOnly`:** `HH:mm:ss`, `HH:mm:ss.FFFFFFF` (optional fractional), and `HH:mm`. 24-hour only. (12-hour `h:mm tt` is a declared-format concern → exact door.)
- **`DateTime` / `DateTimeOffset`:** `yyyy-MM-ddTHH:mm:ss` with optional `.FFFFFFF` fractional, **`T` separator only** (space-separated forms → `Malformed`), and a **mandatory zone** — either a literal `Z` or a numeric offset `±hh:mm`. The accepted set is therefore `{no-frac, frac} × {Z, ±hh:mm}`. A zone-less ISO datetime → `Malformed` (§5).

The result is **normalized to UTC** for the time-bearing types via `DateTimeStyles.AdjustToUniversal`: `DateTime` lands `Kind = Utc`; `DateTimeOffset` lands offset `+00:00`. `DateOnly`/`TimeOnly` have no zone concept and are unaffected.

## 5. Offset policy (no silent timezone assumption)

The temporal analog of §8's "no silent currency assumption." BCL's original sin is assigning the **local machine** offset/kind to a zone-less datetime. On the gateway ISO door:

- **`DateTimeOffset` and `DateTime` require a zone.** A zone-less instant is ambiguous about which moment it denotes, so it fails loud (`Malformed`). The gateway always yields an unambiguous instant.
- **Zone-bearing input normalizes to UTC** (`AdjustToUniversal`) — machine-independent, never local.
- **Zone-unknown wall-clock values** (a faithful "naive" local datetime) are a legitimate need, but they belong to the **exact door**, where the caller declares the format and owns the interpretation — not to the uniform gateway.

The exact door (§6) carries `AssumeUniversal | AdjustToUniversal` so that even there a zone-less exact parse resolves to UTC, never local — the local-machine assumption is forbidden on every path.

## 6. Declared-exact door

`ParseExactRequired` / `ParseExactOptional(span, string format, provider)` on all five types. A **single, required** format string — exactly one accepted representation, declared up front (§2.6). Backed by `TryParseExact(span, format, provider, styles, out …)` with:

- `DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal` for `DateTime`/`DateTimeOffset` (never local), plus `DateTimeStyles.AllowWhiteSpaces`.
- `DateTimeStyles.NoCurrentDateDefault` additionally for `DateTime` — the surviving Crucible nugget: it stops BCL backfilling a missing date component with *today*, turning a silent fallback into a loud failure.

`Failure.Format` = the declared format string on every exact-door failure (the `Failure` type already carries this field; the pathway spec foresaw date/money parsers populating it). The exact door is subject to the same sentinel guard (§9).

## 7. Unix-epoch door

`ParseUnix(span, UnixPrecision)` / `ParseUnixOptional(span, UnixPrecision)` on **`DateTimeOffset` and `DateTime`**. The caller declares the unit (`Seconds` / `Milliseconds`); nothing is guessed from magnitude. Choreography: trim → empty is `Empty`/absent → reject any non-ASCII-digit (after an optional leading `-`) as `Malformed` → `long.TryParse` → `DateTimeOffset.FromUnixTime{Seconds,Milliseconds}`. The `DateTime` overload returns the `UtcDateTime` of that instant (`Kind = Utc`).

- **Integer-only.** A fractional epoch (`1700000000.5`) → `Malformed`.
- **Negatives allowed** (pre-1970 instants).
- **Off-gateway, by name only.** A bare numeric string is never routed into a temporal type through the generic gateway or any ISO/exact door — it is `Malformed` as a date everywhere except this explicit call.
- Subject to the sentinel guard (§9): an epoch landing on `MinValue`/`MaxValue` is rejected.

## 8. `TimeSpan`

The no-format (ISO-canonical) door accepts **both** unambiguous structured forms:

- **BCL colon form** — `[-][d.]hh:mm:ss[.fffffff]` via `TimeSpan.TryParse(span, provider)`. The form `TimeSpan.ToString()` round-trips and the dominant real-world representation.
- **ISO-8601 duration** — `PT1H30M`, `P3DT4H`, etc., parsed by a **hand-rolled allocation-free span scanner** over the grammar `P[nW][nD]T[nH][nM][n[.n]S]`. No throwing built-in is used (`.NET` offers only the throwing `XmlConvert.ToTimeSpan`); the scanner returns `bool` and never raises on bad input, matching the no-magic / span-first house style.

ISO-8601 duration is restricted to **fixed components** (weeks, days, hours, minutes, seconds). Year/month components (`P1Y`, `P2M`) are not fixed durations and are `Malformed`. The declared-exact door uses `TimeSpan.TryParseExact`. `TimeSpan` is subject to the sentinel guard (§9) — `MinValue`/`MaxValue` rejected, `Zero` valid.

## 9. Sentinel guard (value-domain rejection)

A representation the runtime can round-trip is still refused when the *value* is a sentinel — the temporal analog of `RealParser`'s finite guard. `DateTime.MinValue` (`0001-01-01`, the "Baby Jesus date") and `DateTime.MaxValue` (`9999-12-31 23:59:59.9999999`, end of time) never reflect valid state; if either becomes valid the owner needs a new profession. The guard runs after every successful parse — ISO, exact, and Unix doors alike — and converts a sentinel result to `Malformed`:

| Type | Rejected | Valid (never rejected) |
|---|---|---|
| `DateOnly` | `MinValue` (`0001-01-01`), `MaxValue` (`9999-12-31`) | everything between |
| `DateTime` | `MinValue`, `MaxValue` | everything between |
| `DateTimeOffset` | `MinValue`, `MaxValue` (compared on the UTC-normalized instant) | everything between |
| `TimeOnly` | — (**exempt**) | `00:00:00` and `23:59:59.9999999` are real clock readings |
| `TimeSpan` | `MinValue`, `MaxValue` | `Zero` is a valid duration |

No configuration, no opt-out — fail loud, uniformly.

## 10. Gateway integration

Five new `typeof` branches join `Parser.ParseRequired` and `Parser.ParseOptional` — one per concrete temporal type, the model the pathway spec §2.3 fixed. Each sits **before the generic trim**, so the specialist owns its own trimming, and `Unsafe.As`-reinterprets the specialist's `Result<concrete>` to `Result<T>` (sound because `T` is statically the concrete type inside the JIT/AOT-eliminated branch — the established BCL generic-specialization pattern, already proven for the thirteen numeric/`char`/`Guid` branches).

- Each branch calls the specialist's **ISO-canonical `ParseRequired`/`ParseOptional`** (the no-format door) — **no provider forwarded**, exactly as the `char`/`Guid` branches do, because the ISO door is culture-insensitive. The exact and Unix doors are unreachable through the gateway by construction — they carry parameters (`format`, `UnixPrecision`) the uniform `(span, provider)` signature cannot express. That unreachability *is* the §2.6 fork made concrete: declared-format and epoch parsing are explicit named calls, never the uniform default.
- The gateway's uniform non-null provider check still precedes all routing, unchanged.
- **Diagnostics:** `ExpectedType` uses CLR type names (`"DateTime"`, `"DateOnly"`, `"DateTimeOffset"`, `"TimeOnly"`, `"TimeSpan"`); `Format` = `"ISO 8601"` on the gateway path; `Detail` stays null.

The net effect closes the standing hole: `Parser.ParseRequired<DateTime>(span, provider)` now accepts ISO-8601-UTC or fails `Malformed`, where today it silently eats the culture buffet.

## 11. Tests & evidence

`Primitives.Tests`, xUnit v3 + Shouldly on Microsoft.Testing.Platform, `Should_{behavior}_when_{condition}`, `public sealed` classes, Shouldly/Xunit usings global. One test class per specialist plus gateway-routing additions:

- **`DateOnlyParserTests`:** ISO `yyyy-MM-dd` success; time-bearing input → `Malformed`; exact door under US and European providers (`M/d/yyyy` vs `d/M/yyyy` proving the provider is honored); `MinValue`/`MaxValue` → `Malformed`; empty/whitespace ⇒ `Empty`/absent.
- **`TimeOnlyParserTests`:** `HH:mm:ss`, fractional, `HH:mm`; exact door 12-hour `h:mm tt`; midnight `00:00:00` and `23:59:59.9999999` ⇒ `Success` (sentinel exemption pinned); empty ⇒ `Empty`/absent.
- **`DateTimeParserTests` / `DateTimeOffsetParserTests`:** ISO with `Z` and `±hh:mm`, with/without fractional, all UTC-normalized; **zone-less ISO → `Malformed`** (offset policy pinned); space-separator → `Malformed`; exact door with a zone-less declared format resolving to UTC (never local, pinned); `ParseUnix` seconds and milliseconds, negative epoch, fractional/garbage → `Malformed`; `MinValue`/`MaxValue` → `Malformed` across all doors; empty ⇒ `Empty`/absent.
- **`TimeSpanParserTests`:** colon form (`01:30:00`, `1.06:00:00`, negative); ISO-8601 duration (`PT1H30M`, `P3DT4H`, weeks); `P1Y`/`P2M` → `Malformed`; `Zero` ⇒ `Success`; `MinValue`/`MaxValue` → `Malformed`; exact door; empty ⇒ `Empty`/absent.
- **Gateway routing (`Parser` tests):** `Parser.ParseRequired<T>` reaches each ISO door — an ISO date, a `Z`-bearing datetime, an offset datetime, a time, a colon TimeSpan, a `PT`-duration — and a US slash date / zone-less datetime / bare epoch number is `Malformed` through the gateway, proving the branches wire ISO-only and the explicit doors stay off-gateway.

**AOT smoke** (`tests/smoke/Primitives.Aot.Smoke`) extended to exercise each temporal specialist through the gateway plus one `ParseUnix` and one ISO-duration path: zero warnings, exit 0.

**Benchmarks** are an *optional* light filing (one ISO-door parse vs bare `TryParseExact` to confirm the curated-profile loop and sentinel guard are not a hot-path tax), not a gate for this increment. Any finding files as a pathway-spec amendment in Glitnir, never a loose note.

## 12. Repository shape after this increment

New under `src/Primitives/`: `DateOnlyParser.cs`, `DateTimeParser.cs`, `DateTimeOffsetParser.cs`, `TimeOnlyParser.cs`, `TimeSpanParser.cs`, `UnixPrecision.cs`. Edited: `Parser.cs` (five branches in each of `ParseRequired`/`ParseOptional`). New under `tests/Primitives.Tests/`: the five `*ParserTests.cs` files plus gateway-routing additions to the existing `Parser` test class. AOT smoke updated. No new projects.

## 13. Acceptance criteria

1. `Parser.ParseRequired<T>` / `ParseOptional<T>` route all five temporal types to their ISO-canonical door; a US slash date, a zone-less datetime, and a bare epoch number are `Malformed` through the gateway. The standing §2.6 hole is closed.
2. Each specialist honors the template: `Empty`/absent on empty, `Malformed` on unrecognized, bounded capture via `Failure`'s span ctor, CLR-name `ExpectedType`, `Format` populated (`"ISO 8601"` or the declared format).
3. The exact door takes a single required `string format` and a required non-null provider; the ISO and Unix doors take **no** provider (honest signatures, culture-invariant — matching `char`/`Guid`); the Unix door takes a declared `UnixPrecision` and lives on `DateTimeOffset` and `DateTime`. The exact and Unix doors are not reachable through the gateway. The gateway's uniform non-null provider check is unchanged.
4. Offset policy holds: zone mandatory on `DateTime`/`DateTimeOffset` ISO parsing, UTC-normalized, never local on any path.
5. The sentinel guard holds: `MinValue`/`MaxValue` → `Malformed` for `DateOnly`/`DateTime`/`DateTimeOffset` and `TimeSpan`; `TimeOnly` exempt; `TimeSpan.Zero` valid.
6. `ParseFailure` unchanged (no new members); no `FormatHint`, no Auto mode, no validation surface.
7. `dotnet build Svartalfheim.slnx` clean at WarningLevel 9999; `dotnet test Svartalfheim.slnx` green; AOT smoke publishes with zero warnings and exits 0.

## 14. Deferred (this spec's ledger)

1. **Multi-format exact parsing** (`string[]` accepted formats) — when an ingress path genuinely carries more than one declared format.
2. **`Combine`, async combinator siblings, `*Present` variants** — still awaiting their consumer (carried forward from the pathway ledger).
3. **NuGet packaging metadata** — when something consumes the package.
4. **`YGG201` `[MustConsume]` enforcement and the `default(Result<T>)` analyzer rule** — the architecture-analyzers spec, unchanged.

With this increment the BCL scalar struct parsers are complete: `bool`, the integer family, the real family, `char`, `Guid`, and the temporal family all reachable through the gateway and their named specialists.
