# Svartalfheim — Temporal Fusion (Fifth Increment)

**Date:** 2026-06-17
**Status:** Approved in design session; plan deferred by explicit instruction (spec-only session)
**Owner:** Buvy
**Parent specs:** `2026-06-17-svartalfheim-temporal-parsers-design.md` (the fourth increment — the five temporal specialists, their ISO-canonical doors, the offset policy, and the sentinel guard this increment consumes wholesale) · `2026-06-11-svartalfheim-pathway-proof-design.md` (the gateway, the `Result<T>`/`Result<T>?` required/optional pair, and the `Combine` ledger item this increment finally adjudicates) · `2026-06-11-svartalfheim-result-union-boolean-parser-design.md` (the parser template and the closed `ParseFailure` set held intact here).
**Prior art consulted:** none lifted — this is a forward composition step with no Crucible analog.

---

## 1. Motivation

Every parser on the forge to date is **arity-1**: one `ReadOnlySpan<char>` in, one `Result<T>` out. The fourth increment completed the BCL scalar struct family, and the pathway ledger has carried `Combine` and "async combinator siblings" since the second increment under one standing condition — *await the consumer*. This increment is that consumer arriving, and it turns out to want something more specific than `Combine`: the forge's first **arity-N** operation, where several scalars are parsed independently and **fused** into one new scalar value.

**The concrete consumer.** A web form collects an event date and an event time as two independent inputs. The browser's IANA timezone rode in on a cookie and is now a claim on the principal. The platform needs **one stored UTC instant**: parse each input on its own, interpret the date+time as a wall-clock reading in the user's zone, convert to UTC, and store a bare `DateTime` (`Kind = Utc`). The offset is **deliberately not stored** — a user can travel, load the app in a new zone, and re-render the same instant there; the offset is a presentation fact, never a stored one. This is the same "normalize to UTC, never local" stance the fourth increment fixed for the time-bearing types (§4–§5 of the temporal-parsers spec), now extended across a composition boundary.

**Why the required/optional pair survives composition — the load-bearing reason this is two doors, not one.** Optionality on this platform is declared at the **property declaration site**: a `DateTime` property on an HTTP request, an API response, or a flat BDX row is required; a `DateTime?` property is optional. The arity-1 parsers honor this with the `ParseRequired → Result<T>` / `ParseOptional → Result<T>?` pair — the binder picks the door that matches the destination property's nullability. Composition must **preserve** that contract, not collapse it. A fusion that shipped only a required door would silently force every fused datetime to be non-nullable, overriding the property-site declaration the rest of the platform binds against. So `TemporalFusion` ships `FuseRequired → Result<DateTime>` **and** `FuseOptional → Result<DateTime>?`: `Result<T>` / `Result<T>?` holds intact from arity-1 through arity-N. This is the motivation for the optional variant — not template symmetry for its own sake.

Everything here remains **scalar→domain conversion only** (Svartalfheim charter). Application error categories (validation/not-found/conflict) remain the mediator's; transport conditions remain the host pipeline's. The closed `Empty`/`Malformed` failure set is unchanged — see §6.

## 2. Scope

### In scope

Two new forge pieces, their doors, and tests:

- **`TimeZoneParser`** — the scalar parser that resolves untrusted IANA-zone text to a `TimeZoneInfo`. A standing gap above the scalar parsers, closed here because the fusion's third input demands it.
- **`TemporalFusion`** — the static composition specialist: three text inputs → one UTC `DateTime`, with disciplined seam-failure semantics.
- **`Primitives.Tests`** coverage for both, plus an extension of the AOT smoke.

### The `Combine` adjudication (recorded so it is not re-opened)

The pathway ledger's `Combine` item is **resolved by this consumer, in the negative**: the fusion's combiner is not `(DateOnly, TimeOnly, TimeZoneInfo) → DateTime` but `(DateOnly, TimeOnly, TimeZoneInfo) → Result<DateTime>`, because the conversion itself can fail at a DST seam (§5). A failing combiner is `Combine` **then** `Bind`, not `Combine` alone — so "build `Combine` first" would not even capture this fusion. The genuinely reusable, error-prone thing this consumer needs is the **DST-correct conversion owned once, in a tested place** — that is `TemporalFusion`, a self-contained specialist. The general N-ary `Combine` (collect heterogeneous `Result`s, short-circuit, apply a non-failing combiner) is a different abstraction for a different consumer, and remains on the ledger awaiting one (§10).

### Out of scope — its own future spec or ledger

- **Caller-declared DST resolution** (`Earliest`/`Latest`/`Reject`) — a named, off-default door for a caller who legitimately resolves an ambiguous wall-clock by policy, mirroring the `ParseExact`/`ParseUnix` off-gateway doors. Named on the ledger (§10); **unbuilt** — the only current consumer hard-fails on ambiguity and re-prompts, so no resolution policy is earned.
- **Collect-all failure aggregation** — reporting every bad field at once. `Result<T>` carries exactly one `Failure`; aggregated multi-field validation is the mediator/validation layer's job (it runs the parses), not the forge's. First-failure-wins here (§4).
- **General `Combine` and the async combinator siblings** — carried forward from the pathway ledger (§10).
- **Offset-as-input.** A fixed `TimeSpan` offset in place of an IANA zone is rejected outright: an offset captured at cookie-write time is wrong for half the year and flatly wrong for any entered date on the far side of a DST boundary from "now." It reintroduces the silent-mispricing failure class. Not a door, not a ledger item — refused.

## 3. `TimeZoneParser`

The third input's specialist. Follows the established parser template (static class, `Result<T>`/`Result<T>?` returns, the empty⇒`Empty`/absent rule, non-throwing on bad input), with one structural difference from the gateway family.

| Door | Signature | Reachable via gateway? |
|---|---|---|
| **Resolve** | `ParseRequired(span)` / `ParseOptional(span)` | **No** — directly called |

- **Off-gateway by construction.** `TimeZoneInfo` does not implement `ISpanParsable<TSelf>`, so it cannot satisfy the generic gateway's `where T : notnull, ISpanParsable<T>` constraint and is unreachable through `Parser` — exactly as the `ParseExact`/`ParseUnix` doors are unreachable. It is a named, directly-called specialist. No new gateway branch.
- **Backing call:** `TimeZoneInfo.TryFindSystemTimeZoneById(id, out zone)` (.NET cross-platform IANA resolution via ICU). A `string` is materialized from the trimmed span for the lookup — this is an ingress-once path, not a row-volume hot loop, so the allocation is acceptable.
- **Failure shape:** empty/whitespace ⇒ `Empty`/absent; an unrecognized id ⇒ `Malformed`, `ExpectedType = "TimeZoneInfo"`, `Input` = the bounded id text, `Format` = `"IANA"`. No silent fallback to the machine's local zone or to UTC — a missing or unknown zone is a loud failure.
- **No provider.** Zone-id resolution is culture-insensitive; an honest signature carries no `IFormatProvider`, matching `CharParser`/`GuidParser` and the ISO temporal doors.

**Charter note (settled).** Resolving untrusted boundary text against a known table is **parsing**, the same way culture parsing already consults ICU. A `TimeZoneInfo` lookup hitting the OS/ICU zone database is not "the forge doing I/O" in any sense that culture-sensitive numeric parsing is not; it is the canonical scalar→domain conversion for a zone id and belongs on the forge.

## 4. `TemporalFusion` — the composition specialist

Static class. Two doors, mapping the destination property's optionality (§1):

| Door | Signature |
|---|---|
| **Fuse (required)** | `FuseRequired(date, time, zone) → Result<DateTime>` |
| **Fuse (optional)** | `FuseOptional(date, time, zone) → Result<DateTime>?` |

All three parameters are `ReadOnlySpan<char>`. **No provider** — see §7. Both doors are non-throwing on bad input.

### Algorithm (`FuseRequired`)

1. **Parse each input, first-failure-wins, in the fixed order date → time → zone.** Date via `DateOnlyParser`'s ISO-canonical door, time via `TimeOnlyParser`'s ISO-canonical door, zone via `TimeZoneParser` (§3). The first sub-parse that fails returns its `Failure` **verbatim** — that `Failure` already carries the correct `Input` (the offending field's text), `ExpectedType` (`"DateOnly"` / `"TimeOnly"` / `"TimeZoneInfo"`), and `Reason`. No re-wrapping. The evaluation order is contractual and documented (a fully-blank form surfaces the date failure first).
2. **Compose the wall-clock:** `date.ToDateTime(time, DateTimeKind.Unspecified)` — a zone-less local reading, to be interpreted in the parsed zone.
3. **Gap check:** if `zone.IsInvalidTime(wall)`, the wall-clock denotes a local time that never existed (spring-forward). → seam failure (§6), `Detail = "DST gap"`.
4. **Ambiguity check:** if `zone.IsAmbiguousTime(wall)`, the wall-clock denotes a local time that occurred twice (fall-back). → seam failure (§6), `Detail = "DST ambiguous"`.
5. **Convert:** `TimeZoneInfo.ConvertTimeToUtc(wall, zone)` → `DateTime`, `Kind = Utc`.
6. **Sentinel guard:** apply the fourth increment's guard to the result — `DateTime.MinValue`/`MaxValue` ⇒ `Malformed`. Uniform with every other temporal door; no opt-out.

Steps 3 and 4 run **before** step 5 deliberately: `IsInvalidTime`/`IsAmbiguousTime` are the explicit guards that dodge the BCL's two failure modes — `ConvertTimeToUtc` *throws* on a gap time and *silently assumes standard time* on an ambiguous one. Checking first converts both into loud, uniform `Malformed` failures and never lets the silent standard-time guess happen.

### `FuseOptional` and the partial-input rule

Optionality is evaluated on the **date and time fields only** — the zone is infrastructure, never the thing that makes a value absent.

- **Both date and time empty/whitespace** ⇒ absent. `FuseOptional` returns `null`; `FuseRequired` returns `Empty`. The zone is **not** examined.
- **Exactly one of date/time empty** ⇒ `Malformed`. A half-specified instant is precisely the silent guess the laws forbid — "date with no time" means midnight, "time with no date" means today; both are inferences, not data. Fails loud on both doors. `ExpectedType = "DateTime"`, `Detail = "partial instant"`.
- **Both present** ⇒ run the algorithm above; the zone must now resolve or its sub-failure propagates (§4 step 1).

## 5. The two DST seams (why composition has failures arity-1 never had)

A wall-clock value interpreted in a real IANA zone has two structural break points, and both are governed by the platform's no-silent-guess law:

- **The gap (spring-forward).** A local time that never existed (e.g. `02:30` on a US spring-forward morning). No instant denotes it. Rolling forward to `03:30` is a guess. → hard failure. There is no policy alternative; the gap is non-negotiably loud.
- **The ambiguity (fall-back).** A local time that occurred twice (e.g. `01:30` on a US fall-back morning — once in daylight, once in standard). Two instants denote it. The BCL silently picks standard time; that silent pick is the forbidden fallback. → hard failure on the only door built.

This is the temporal analog of the fourth increment's offset policy ("the gateway always yields an unambiguous instant") and of the culture-guessing axis the fourth increment deleted ("declare the provider, do not guess the culture"). The fusion **yields exactly one unambiguous UTC instant or it fails loud** — it never guesses across a seam. A future caller that legitimately resolves ambiguity by a declared policy gets a named off-default door, never a silent default (§10).

## 6. Failure semantics — the closed set holds

`ParseFailure` gains **no member**. Both seams, and the partial-input case, are `Malformed` — the composite parsed clean but is not a valid value of the target type, the exact logic the fourth increment's sentinel guard already uses (`MinValue`/`MaxValue` parsed fine, rejected as `Malformed`).

Seam and partial-input failures populate `Failure` as:

- `Reason = Malformed`
- `ExpectedType = "DateTime"`
- `Input` = the rendered composite wall-clock (`"2026-03-08T02:30 America/Chicago"`) for a seam failure; the field text(s) for the partial-input case.
- `Detail` = a **stable token** — `"DST gap"`, `"DST ambiguous"`, or `"partial instant"` — so a consumer that wants to render distinct user-facing messages can switch on it. The gap-vs-ambiguity *distinction itself is a rendering nuance*; for the current hard-fail-and-re-prompt consumer, "enter a valid time for that date" covers both, and `Detail` is there for the consumer that wants finer copy.

A new `ParseFailure` member was considered and **rejected**: a platform-wide breaking change (every exhaustive switch over the enum) to teach a deliberately domain-agnostic set (`Empty`/`Malformed`) about daylight saving, for a single temporal consumer that hard-fails either way, is the over-reach the closed set exists to prevent.

## 7. No provider — the keystone, and why the consumer fits the strictest door

The fusion takes **no `IFormatProvider`**, and this is not a compromise — it is the design falling exactly into place against the real consumer:

- An HTML `<input type="date">` submits `yyyy-MM-dd`. An HTML `<input type="time">` submits `HH:mm` or `HH:mm:ss`. **Both are native ISO 8601, culture-invariant** — and both are exactly what the fourth increment's `DateOnlyParser` / `TimeOnlyParser` ISO-canonical doors already accept (`yyyy-MM-dd`; `HH:mm:ss`, `HH:mm:ss.FFFFFFF`, `HH:mm`).
- So `TemporalFusion` calls the **ISO-canonical doors directly** (which take no provider), never the generic gateway (whose uniform non-null provider check it would otherwise have to satisfy with a culture it does not use). The fusion is itself provider-free — honest signature, the same stance as `char`/`Guid` and the ISO temporal doors.
- The web form hands the forge precisely what the strictest door on the forge already speaks, without a single culture declaration. The §2.6 "exactly one accepted representation per ingress path" law is satisfied for free: the ingress representation is ISO, and ISO is what the door takes.

A consumer whose date/time arrive in a *declared non-ISO format* (a slash-dated BDX column, say) is a different ingress path: it parses its fields through the `ParseExact` doors first and would want a fusion overload that accepts pre-parsed `DateOnly`/`TimeOnly` — a clean future extension, noted on the ledger (§10), not built now.

## 8. Repository shape after this increment

New under `src/Primitives/`: `TimeZoneParser.cs`, `TemporalFusion.cs`. No edits to `Parser.cs` — neither piece is gateway-routed (§3, §4). New under `tests/Primitives.Tests/`: `TimeZoneParserTests.cs`, `TemporalFusionTests.cs`. AOT smoke (`tests/smoke/Primitives.Aot.Smoke`) extended. No new projects, no change to `ParseFailure`, `Failure`, or `Result<T>`.

## 9. Tests & evidence

`Primitives.Tests`, xUnit v3 + Shouldly on Microsoft.Testing.Platform, `Should_{behavior}_when_{condition}`, `public sealed` classes, Shouldly/Xunit usings global.

- **`TimeZoneParserTests`:** a known IANA id (`"America/Chicago"`) ⇒ `Success`; an unknown/garbage id ⇒ `Malformed` with `Format = "IANA"`; empty/whitespace ⇒ `Empty`/absent; no silent fallback to local/UTC pinned.
- **`TemporalFusionTests`:**
  - Happy path — ISO date + ISO time + a known zone ⇒ `Success`, `Kind = Utc`, with the offset correctly applied (a standard-time date and a daylight-time date in the same zone proving the conversion is date-aware, not a fixed offset).
  - **Spring-forward gap** — a real US gap wall-clock (`2026-03-08 02:30` in `America/Chicago`) ⇒ `Malformed`, `Detail = "DST gap"`.
  - **Fall-back ambiguity** — a real US ambiguous wall-clock (`2026-11-01 01:30` in `America/Chicago`) ⇒ `Malformed`, `Detail = "DST ambiguous"` (the silent standard-time pick explicitly proven *not* to happen).
  - **Verbatim sub-failure propagation** — a malformed date, a malformed time, and an unknown zone each surface their own `ExpectedType`; first-failure-wins order (date before time before zone) pinned with multiple bad fields.
  - **Partial input** — date present / time empty and time present / date empty each ⇒ `Malformed`, `Detail = "partial instant"`, on both doors.
  - **Absence** — both fields empty ⇒ `FuseOptional` `null`, `FuseRequired` `Empty`; the zone is proven *not* consulted (an unknown zone with both fields empty still returns absent).
  - **Sentinel guard** — a fused result landing on `MinValue`/`MaxValue` ⇒ `Malformed`.
- **AOT smoke** extended to exercise one `FuseRequired` happy path and one `TimeZoneParser` resolution: zero warnings, exit 0.

Benchmarks are not a gate for this increment (the fusion is an ingress-once path, not a row-volume loop); any finding files as a pathway-spec amendment in Glitnir.

## 10. Acceptance criteria

1. `TimeZoneParser.ParseRequired`/`ParseOptional` resolve a known IANA id to `Result<TimeZoneInfo>`/`Result<TimeZoneInfo>?`; unknown ⇒ `Malformed` (`Format = "IANA"`), empty ⇒ `Empty`/absent; off-gateway, no provider, no silent fallback.
2. `TemporalFusion.FuseRequired`/`FuseOptional` return `Result<DateTime>`/`Result<DateTime>?`, `Kind = Utc`, from three `ReadOnlySpan<char>` inputs and no provider, calling the ISO-canonical doors directly.
3. First-failure-wins in the fixed date→time→zone order; each sub-`Failure` propagated verbatim.
4. The gap ⇒ `Malformed` `Detail = "DST gap"`; the ambiguity ⇒ `Malformed` `Detail = "DST ambiguous"`, with the BCL silent standard-time pick proven not to occur; both checked before conversion.
5. Partial input (exactly one of date/time empty) ⇒ `Malformed` `Detail = "partial instant"` on both doors; both empty ⇒ absent (`null` / `Empty`) without consulting the zone.
6. The sentinel guard holds on the fused result (`MinValue`/`MaxValue` ⇒ `Malformed`).
7. `ParseFailure`, `Failure`, and `Result<T>` are unchanged; no new gateway branch; the required/optional pair maps the destination property's optionality.
8. `dotnet build Svartalfheim.slnx` clean at WarningLevel 9999; `dotnet test Svartalfheim.slnx` green; AOT smoke publishes with zero warnings and exits 0.

## 11. Deferred (this spec's ledger)

1. **Caller-declared DST resolution door** (`Earliest`/`Latest`/`Reject`) — named here; built when a consumer legitimately needs to resolve an ambiguous wall-clock by policy rather than re-prompt.
2. **Pre-parsed fusion overload** — `Fuse(DateOnly, TimeOnly, TimeZoneInfo)` for an ingress path whose fields arrive in a declared non-ISO format and were parsed through the `ParseExact` doors first (§7).
3. **General `Combine`, async combinator siblings, `*Present` variants** — carried forward from the pathway ledger; this consumer proved fusion-with-a-failing-combiner is *not* `Combine`, so the deferral stands awaiting its own consumer.
4. **Collect-all failure aggregation** — when an ingress path genuinely needs every bad field reported at once rather than first-failure-wins; likely a validation-layer concern above the forge, recorded here so the boundary is explicit.
5. **NuGet packaging metadata** — when something consumes the package.
