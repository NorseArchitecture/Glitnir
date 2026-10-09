# Svartálfheim — The Hyper Engines Cut (`Norse.Primitives`)

**Date:** 2026-10-09
**Status:** Ratified 2026-10-09 by Buvy; the five rulings in §10 are applied in place. Next stage: the implementation plan under `../plans/`, which also carries the before/after benchmark obligation (§4.8, §9)
**Realm:** Svartálfheim (`Norse.Primitives`), with consequential changes in Midgard, Mímisbrunnr, Yggdrasil, and the Bifröst/Glitnir record (§7)
**Supersedes:** `2026-09-03-hyperuuid-hypercast-ingestion-design.md` in full — the seam model (native path plus managed fallback, `NativeCapability`, vendored corpus, dual-mode CI) is retired, not amended. Its branch `origin/feature/hyperuuid-hypercast-ingestion` (29 commits, tip `ec8b435`, never merged) is closed as superseded; nothing is cherry-picked from it.
**Amends:** `2026-07-03-svartalfheim-identifiers-design.md` §5.1–5.2 (the layout and permutation are now HyperUuid's, pinned by HyperUuid's own corpus); `2026-06-17-svartalfheim-numeric-char-guid-parsers-design.md` and `2026-06-17-svartalfheim-temporal-parsers-design.md` (every specialist those specs define is deleted; the grammar they describe is now HyperCast's, by corpus)
**Upstream relationship:** [SkunkWerkx/HyperCast](https://github.com/SkunkWerkx/HyperCast), [SkunkWerkx/HyperUuid](https://github.com/SkunkWerkx/HyperUuid), [SkunkWerkx/HyperTabular](https://github.com/SkunkWerkx/HyperTabular) — Buvy's own Rust-core polyglot libraries, released at v0.8.0 on 2026-10-09 with every ask this realm filed on 2026-10-08 closed (HyperCast #21 #22, HyperUuid #29–#32, HyperTabular #8–#12). This realm is their first downstream consumer; gaps found here are filed upstream, never worked around.

---

## 1. Context

### 1.1 What changed since September

The September design was written against HyperCast 0.2 and HyperUuid 0.3, with HyperTabular existing only as Rust crates. It kept Svartálfheim's managed parsers and identifier arithmetic alive as a fallback behind a capability gate, because the native libraries covered too few platforms and too little of the realm's surface to stand alone. Both constraints are gone:

- **Coverage.** v0.8.0 of all three ships `win-x64`/`win-arm64`, `linux-x64`/`linux-arm64` (glibc 2.28+ and musl), `osx-x64`/`osx-arm64`, `ios-arm64`/`iossimulator-arm64`, `maccatalyst-arm64`/`maccatalyst-x64`, `android-arm64`/`android-x64`, and `browser-wasm` — every platform `NorseFrontendPlatforms=All` declares. Native AOT publishes link the static archives; CoreCLR loads the shared libraries; Blazor WebAssembly statically links through the packages' own `build/net11.0/*.targets`.
- **Surface.** HyperCast `Cast.Scalar<T>` dispatches every door this realm's `Parser` gateway routes (`bool`, the eleven numerics, `char`, `Guid`, `DateOnly`, `TimeOnly`, `DateTime`, `DateTimeOffset`, `TimeSpan`) and takes an `IFormatProvider` directly; HyperUuid inspects version and variant, reads and validates SQL Server–ordered values in place, pins its permutations in a corpus whose SQL vectors equal this realm's `SequentialGuidBytes.ToSqlOrder` byte for byte, and guards the 26-bit counter; HyperTabular opens header-first, resolves names to ordinals, reads rows, decodes text to UTF-16 without a string, opens workbooks from streams, and reads asynchronously with cancellation.

Direct ruling, 2026-10-08: the engines are **cut**, not seamed. HyperCast, HyperUuid, and HyperTabular become the only implementations; there is no managed path to keep in doctrine lockstep, no probe, no fallback. Svartálfheim keeps what is its own — `Result<T>`, `Failure`, `ParseFailure`, the `Parser` gateway's shape, `SequentialGuid`/`DeterministicGuid`'s public type contracts, `Pii`, `TimeZoneParser`, `TemporalFusion` — and deletes what the engines now own.

### 1.2 Why a cut and not a seam

The seam's cost was the fallback: every grammar question had to be answered twice, the managed side had to be converged vector by vector against a corpus it could never fully reach (the September audit named seven vectors neither side could express), and CI had to run the whole suite twice to keep an unexercised path from rotting. The September record's own final correction (§1 of the superseded spec) shows where that leads — on every native-capable host, "managed vs. native" benchmarks were measuring wrapper overhead over the same native call. A fallback nobody runs is a liability, not insurance. With platform coverage complete, the one remaining argument for it is gone.

## 2. Scope

**In scope — Svartálfheim:**

1. **Casting.** The eleven scalar specialists (`BooleanParser`, `IntegerParser`, `RealParser`, `CharParser`, `GuidParser`, `DateOnlyParser`, `DateTimeParser`, `DateTimeOffsetParser`, `TimeOnlyParser`, `TimeSpanParser`) and the `ParseExact*`/`ParseUnix` doors are deleted. `Parser.ParseRequired<T>`/`ParseOptional<T>` keep their signatures and route through `HyperCast.Cast.Scalar<T>`, falling through to `ISpanParsable<T>` for every type HyperCast has no door for. `ParseFailure` adopts `OutOfRange`. `TimeZoneParser` and `TemporalFusion` stay, rewired onto the gateway (§4.6, ruling requested in §10).
2. **Identifiers.** `SequentialGuid` and `DeterministicGuid` keep their public shape and become thin wrappers over `HyperUuid.UuidGenerator`. `SequentialGuidBytes` and `GuidVersionBits` are deleted. `GuidByteOrder` and `INorseGuid` stay.
3. **Tabular.** `src/Primitives.Ingestion`, its tests, its AOT smoke, and the `Norse.Primitives.Ingestion` package are deleted. There is no Norse tabular abstraction any more; the one consumer reads HyperTabular directly.

**In scope — consequential, other realms (§7):** Midgard renders `ParseFailure.OutOfRange`; Mímisbrunnr's seed contributor, `SeedTool`, and their tests move from `ITabularReader` to `HyperTabular.DelimitedReader`; Yggdrasil's composition root pins the three packages under CPM and proves the Blazor WebAssembly head still links; the Bifröst, Glitnir, Svartálfheim, and Mímisbrunnr README/CLAUDE.md pairs stop saying "nietras Sep" and "Primitives.Ingestion".

**Out of scope, by decision:**

- **XLS (BIFF8) and XLSB.** `ExcelTabularReader` read them through Sylvan; HyperTabular reads XLSX, XLSM, and ODS. No consumer on the platform opens any workbook today. The legacy formats are dropped from Norse's surface rather than asked for upstream; a future product ingestion path that needs them raises it then.
- **Culture fidelity in HyperCast's provider bridge** (sign characters beyond ASCII `+`/`-`, `CurrencyDecimalSeparator`, the percent symbol, native digits, wide separators — recorded in HyperCast's `docs/roadmap.md`, "Culture fidelity — the sweep"). Deferred upstream to HyperCast v0.9.0 by Buvy's ruling of 2026-10-08. Every live Norse call site passes `CultureInfo.InvariantCulture`, which the bridge maps exactly (§4.4).
- **A managed v5 for Mímisbrunnr's generator.** `gen/Reference.Data.Contracts.Generator/Uuid5.cs` stays as it is — a Roslyn generator cannot load a native library, and its emitted identifiers are already recomputed through the real `DeterministicGuid` at test time, which after this cut means through HyperUuid.
- **A vendored or packaged HyperCast corpus.** There is no second engine to hold to it. Norse's tests prove the translation layer, not the grammar (§4.8).
- **Any new door** (`Combine`, async combinators, DST policy doors, pre-parsed fusion). The deferred-increment ledger in the realm's CLAUDE.md is untouched.

## 3. Architecture — the engines are the engines

`src/Primitives/Primitives.csproj` takes `HyperCast` and `HyperUuid` as its first external runtime `PackageReference`s. `Mimisbrunnr` takes `HyperTabular`. All three float — `Version="*"` — by ruling (§10 item 1): both ends of the dependency are Buvy's, so the cadence is controlled, and this realm already floats `FsCheck` and `BenchmarkDotNet` the same way. The major-tag law's purpose (no surprise 1.0.0 from a stranger) does not apply to a dependency the same author releases. `HyperTabular` itself floors `HyperCast` at 0.8.0, so the three resolve to one consistent 0.x line.

This does not contradict the "permanent architectural leaf, declares no `NorseRef`" doctrine in the realm's `Directory.Build.props`: that is about internal cross-realm references. External packages were always permitted; this is simply the first time the forge needs one at runtime.

**Nothing is probed.** `Cast.IsAvailable`, `UuidGenerator.IsAvailable`, and `Tabular.IsAvailable` exist upstream for consumers with a fallback; this realm has none and never checks them. A platform the packages do not cover fails at the first call with `DllNotFoundException`, which is the correct outcome under §2.7 of Glitnir's law (fail loud, fail early) — and after v0.8.0 the set of such platforms is `android-arm`/`android-x86` (32-bit, no .NET 11 Android default build includes them) and nothing else. The AOT smoke test, the Yggdrasil WebAssembly publish (§9), and CI on Linux, macOS, and Windows are the proof that the covered platforms resolve.

**Where types cross.** Svartálfheim's public surface keeps speaking `Result<T>`/`Failure`/`ParseFailure` and `SequentialGuid`/`DeterministicGuid`; HyperCast's `Verdict<T>`/`Fault`/`CastFailure` and HyperUuid's `Guid` doors are translated inside the forge and never appear in a Norse signature. Consumers that want HyperCast's or HyperUuid's own surface reference the package themselves — Mímisbrunnr's seed path does exactly that for HyperTabular (`Batch`, `Row`, `Column`, `Dialect` are its vocabulary, not wrapped). The two-unions doctrine (`Result<T>` vs. `Outcome<T>`) is unaffected: `Verdict<T>` is an engine-internal third union that stops at the forge's edge.

## 4. Family 1 — casting

### 4.1 `ParseFailure` adopts `OutOfRange`

```csharp
public enum ParseFailure : byte
{
	Unspecified = 0,
	Empty = 1,
	Malformed = 2,
	OutOfRange = 3,   // adopted from HyperCast.CastFailure verbatim — same name, same number, same meaning
	Duplicate = 4     // Svartálfheim's own addition (2026-08-09), renumbered to make room
}
```

The four shared members are numerically identical to `CastFailure`, so the engine-to-forge mapping is a reinterpret, not a switch. `Duplicate` remains Norse vocabulary: HyperCast never produces it; Midgard's flags-enum lexical parsing does. This is a **deliberate breaking change**, announced by `ParseFailure`'s own doc comment: every exhaustive switch over it is a build error until updated. The platform inventory (2026-10-09, every realm, `bin`/`obj` excluded) finds exactly one outside Svartálfheim — `Midgard/src/Infrastructure.Web.Server/Xml/FailureDetail.cs`, `Render(in Failure)`, whose discard arm throws `ArgumentOutOfRangeException`; it gains an `OutOfRange` arm (§7). `Failure`'s constructor guard widens to accept the new member.

Behavior that moves with it: `"256"` as a `byte`, `1e400` as a `double`, a timestamp past 9999, a `char` code point past `U+FFFF` are `OutOfRange` where the managed parsers said `Malformed`. No consumer branches on `Malformed` for these inputs today; the Midgard renderer is the only place the distinction is user-visible.

### 4.2 `Failure` is unchanged in shape; the engine fills it

`Failure(Reason, Input, ExpectedType, Format, Detail)` stays exactly as it is. Midgard consumes all of `Reason`, `Input`, `ExpectedType`, and `Detail` (JSON, gRPC, XML rendering and `ResultRules`), so none can go. The gateway constructs it from what it already has:

| Field | Source after the cut |
|---|---|
| `Reason` | `(ParseFailure)fault.Reason` — reinterpret |
| `Input` | the caller's span, trimmed, bounded to `MaxInputLength` by the existing span constructor — unchanged |
| `ExpectedType` | `typeof(T).Name` — unchanged |
| `Format` | `null` from the gateway (the `ParseExact*` doors that filled it are gone); the property stays for Midgard's own failures |
| `Detail` | `null` from the gateway; `TemporalFusion` keeps writing `"DST gap"`/`"DST ambiguous"` |

`Fault.Offset`/`Fault.Length` (the offending span inside the input) are not surfaced. Adding them to `Failure` is a widening with no consumer; it goes on the deferred-increment ledger, not into this cut.

### 4.3 The `Parser` gateway keeps its `typeof` table — one line per door

`Parser.ParseRequired<T>(ReadOnlySpan<char>, IFormatProvider) where T : ISpanParsable<T>` and `ParseOptional<T>` keep their signatures, their non-nullable-provider law, and their "a type that cannot parse does not compile" property. Inside, the nineteen specialist branches collapse to nineteen one-line reinterprets into one engine door:

```csharp
if (typeof(T) == typeof(int))
	return Route<int, T>(input, provider);
// … one line per HyperCast door …
var trimmed = input.Trim();
return trimmed.IsEmpty ?
	new Failure(ParseFailure.Empty, string.Empty, typeof(T).Name) :
	Parse<T>(trimmed, provider);   // ISpanParsable<T> fallthrough — unchanged

static Result<T> Route<TDoor, T>(ReadOnlySpan<char> input, IFormatProvider provider) where TDoor : struct
{
	var routed = Translate<TDoor>(Cast.Scalar<TDoor>(input, provider), input);
	return Unsafe.As<Result<TDoor>, Result<T>>(ref routed);
}
```

The table cannot disappear entirely: `Cast.Scalar<T>` is constrained `where T : struct` and `Parser` is constrained `where T : ISpanParsable<T>`, and C# has no way to narrow one into the other inside a generic body without reflection. What the table used to carry — eleven classes of grammar — is gone; what remains is routing, JIT-eliminated exactly as today (the `Unsafe.As` identity reinterpret is the established pattern, benchmark-verified at ratio 1.02 in the pathway spec). HyperCast's own `Scalar<T>` has the same table for the same reason.

The fallthrough is unchanged and now carries real traffic: `string` (Midgard's `ParseRequired<string>`), `Half`, `Int128`/`UInt128`, `BigInteger`, `Version`, `IPAddress`, and any user `ISpanParsable<T>` go through `T.TryParse(trimmed, provider)` as before. A type HyperCast later gains a door for is one line added to the table.

### 4.4 Culture: the provider passes straight through

`Cast.Scalar<T>(ReadOnlySpan<char>, IFormatProvider?)` maps the provider at the door via `NumFormat.From(NumberFormatInfo.GetInstance(provider))` — first character of the culture's decimal and group separators, its currency symbol, and `NumStyles.All`. That lenience set (grouping, parentheses, currency, exponent, percent, `0x`/`0b`/`&H` radix prefixes) is what `IntegerParser` and `RealParser` already accepted — the AOT smoke test's `"0x2A"` → 42 and `"50%"` → 0.5 checks pass unchanged — so the invariant culture, which is every live Norse call site, maps exactly. The gateway keeps `ArgumentNullException.ThrowIfNull(provider)` ahead of routing; HyperCast's "null means current culture" never applies here. Culture-insensitive doors (`char`, `Guid`, temporal) ignore the provider on both sides, as the gateway's doc comment already says.

The known gaps in the bridge — non-ASCII negative signs (`et`, `eu`, and 39 other cultures use U+2212), `CurrencyDecimalSeparator`, the culture's percent symbol, native digits, AM/PM designators, wide separators — are HyperCast's `docs/roadmap.md` sweep, deferred to v0.9.0. The gateway's doc comment states the invariant culture is exact and names the roadmap for the rest; Norse adds no shim.

### 4.5 Deleted doors

| Door | Consumers outside Svartálfheim (2026-10-09 inventory) | Disposition |
|---|---|---|
| The eleven scalar specialists' `ParseRequired`/`ParseOptional` | none — every realm calls `Parser.ParseRequired<T>` | deleted; the gateway routes to HyperCast |
| `*Parser.ParseExactRequired`/`ParseExactOptional` (five temporal types) | none | deleted — BCL `TryParseExact` wrappers, no Svartálfheim logic; HyperCast's declared options (`DateOrder`, `UnixPrecision`, `ExcelEpoch`) cover every live declared-format case |
| `DateTimeParser.ParseUnix`, `DateTimeOffsetParser.ParseUnix`, `UnixPrecision` | none | deleted; `Cast.Unix(span, precision)` exists upstream for a future consumer, and a `Parser` twin can be added over it when one appears |
| `SequentialGuidBytes`, `GuidVersionBits` | none (internal) | deleted — §5 |
| `Primitives.Ingestion` (all of it) | Mímisbrunnr | deleted — §6 |

### 4.6 Kept and rewired: `TimeZoneParser` and `TemporalFusion`

Neither duplicates an engine. `TimeZoneParser` resolves IANA identifiers through `TimeZoneInfo`, which needs the host's time-zone database — a `no_std` Rust core should not carry one, and HyperCast does not. `TemporalFusion` composes a date, a time, and a zone into a UTC `DateTime` with hard failures on DST gaps and ambiguities; its only parsing steps are an ISO date and an ISO time, which become `Parser.ParseRequired<DateOnly>`/`<TimeOnly>` calls (HyperCast's `Date`/`Time` doors — strict ISO, corpus-pinned) instead of the deleted specialists. Both keep their public signatures, their `Detail` strings, and their tests. **Ruling requested (§10 item 2):** the alternative, deleting both because they have no consumer outside this realm, is the plan file's recorded option; the recommendation here is to keep them because they are the realm's own law (the temporal-fusion spec), not engine duplication.

### 4.7 Semantics that change, stated

| Input | Before | After | Why |
|---|---|---|---|
| `"256"` as `byte`, `1e400` as `double`, `"10000-01-01T00:00:00Z"` | `Malformed` | `OutOfRange` | §4.1 |
| a well-formed date whose day is past the month's end (`2026-02-30T00:00:00Z`) | `Malformed` | `OutOfRange` | the engine distinguishes "not a timestamp" from "a timestamp that does not exist"; found by Midgard's `ResultSerializerTests` during execution |
| `DateTimeOffset.MinValue`/`MaxValue` text, `DateTime` sentinels | rejected by the managed sentinel guard | accepted if the corpus accepts them | HyperCast's corpus is the source of truth — already ruled for the seam (superseded spec §6, 2026-09-03 amendment) |
| `" "` as `char` | trimmed to empty → `Empty` | a space, verbatim — one character before trimming | HyperCast's `char` grammar, which the realm asked for in HyperCast #22 and verified |
| a lone surrogate as `char` | `Malformed` | returned verbatim (one UTF-16 unit) | HyperCast #22's recorded choice; a code point past `U+FFFF` is `OutOfRange` |
| `DateTime` from text | mandatory zone, normalized to `Kind.Utc` | identical: `Cast.Timestamp(...).UtcDateTime` | HyperCast #21 adopted this realm's contract |
| Real numbers: structural separator detection | none (`RealParser` had no `Detect`) | still none — the gateway passes the culture's declared separators, never `NumFormat.Detect` | §2.6 of Glitnir's law: declare, never sniff |

Every other door was corpus-verified identical in the 2026-10-08 inventory: boolean vocabulary, integer radix prefixes and two's complement, `Guid` prefixes (`urn:uuid:`, `GUID:`, `UUID:`) and formats N/D/B/P/X, `TimeOnly` `HH:mm` through seven fractional digits, `TimeSpan` colon and ISO-8601 forms including weeks and negatives, RFC 3339 timestamps.

### 4.8 Tests and benchmarks

- **Deleted:** the eleven specialist test files, `ParserTests`' per-door grammar cases, `GuidVersionBitsTests`, `SequentialGuidBytesTests`.
- **Rewritten:** `ParserTests` proves the translation layer and routing, not the grammar — one representative success and one representative failure per door through the gateway (chosen from HyperCast's `corpus/*.json` so the expectation is the corpus's, cited by vector name in the test), `OutOfRange` arriving as `OutOfRange`, `Empty` on whitespace, `ParseOptional` absence, `Input` echo bounded and trimmed, `ExpectedType` set, `Format`/`Detail` null, the provider threaded (`de-DE` `"1,5"` → `1.5m`, the invariant `"1,234"` → `1234`), the `ISpanParsable<T>` fallthrough for `string`/`Half`/`Int128`, and the null-provider throw. `FailureTests` gains `OutOfRange`. `TemporalFusionTests`/`TimeZoneParserTests` run unchanged.
- **Kept untouched:** `ResultTests`, `ResultCombinatorTests`, `ResultLawTests` (FsCheck), `Pii/*`, all analyzer suites.
- **AOT smoke:** every existing check runs unchanged except the deleted doors' lines; the publish must stay at zero AOT warnings and exit 0, which is also the proof the static archives link.
- **Before and after.** By ruling (§10 item 6) the managed baseline is captured on `master` before any deletion — `DispatchBenchmarks`, `ParserAllocationBenchmarks`, and a new `IdentifierBenchmarks` (v7 single, v7 fill 1000, v5 derive, SQL-order round trip) — and the identical rig is re-run after the cut; both tables are filed together.
- **Benchmarks** (`benchmarks/Primitives.Benchmarks`): `DispatchBenchmarks` loses its `DirectSpecialist` baseline and measures the gateway against a direct `Cast.Scalar<T>` call instead — the number that matters is the forge's wrapping tax over the engine, which the September record put at ~40 ns and which this cut should hold or lower; `ParserAllocationBenchmarks` drops the deleted doors and must stay at 0 B on both the success and the bounded-failure path. Findings are filed as an amendment to the pathway spec §8, per realm law.

## 5. Family 2 — identifiers

`SequentialGuid` and `DeterministicGuid` keep every public member, their `readonly record struct` shape, `INorseGuid`, `IComparable<>`, the implicit `Guid` conversion, the SQL-aware `CompareTo`, and `GuidByteOrder`. Urðarbrunnr's conventions and value converters, Midgard's gRPC serializers, Himinbjörg, Mímisbrunnr's entities and seed, and Mímir compile unchanged. Inside:

| Member | Before | After |
|---|---|---|
| `new SequentialGuid()` | `SequentialGuidBytes.GenerateRfc` + a process counter + `RandomNumberGenerator` | `UuidGenerator.NewV7()` |
| `new SequentialGuid(Guid, GuidByteOrder)` | `GuidVersionBits.HasVersionAndVariant(value, 7)` | `UuidGenerator.IsRfc(value, 7, (UuidLayout)order)` — layout-aware, so a SQL-ordered value is validated in place |
| `Timestamp` | `SequentialGuidBytes.ExtractTimestamp(value, order)` | `UuidGenerator.V7Timestamp(value, (UuidLayout)order).UtcDateTime` |
| `ToSqlOrder()` / `ToRfcOrder()` | `SequentialGuidBytes.ToSqlOrder`/`ToRfcOrder` | `UuidGenerator.V7ToSqlOrder`/`V7FromSqlOrder` |
| `Fill(Span<SequentialGuid>)` / `CreateMany` | per-item generation, 2²⁶ ceiling | `UuidGenerator.FillV7(Span<Guid>)` through a scratch span, chunked at a bounded size so a large batch neither stack-allocates unboundedly nor allocates a second array of the full count; the 2²⁶ ceiling is HyperUuid's `MaxV7Batch` (ABI `ArgumentOutOfRangeException`), surfaced unchanged |
| `new DeterministicGuid(Guid, string / ReadOnlySpan<char> / ReadOnlySpan<byte>)` | SHA-1 by hand | `UuidGenerator.NewV5(...)`, the matching overload |
| `new DeterministicGuid(Guid)` | `GuidVersionBits.HasVersionAndVariant(value, 5)` | `UuidGenerator.IsRfc(value, 5)` |
| `DeterministicGuid.Namespaces` | four RFC constants | unchanged (same values as `UuidGenerator.Namespaces`; kept so no consumer changes) |

`GuidByteOrder {Unspecified=0, Rfc9562=1, SqlServer=2}` is numerically identical to `UuidLayout {Unspecified=0, Rfc9562=1, SqlServer=2}`; the cast is a reinterpret and a test pins that the two enums stay shape-identical. `GuidByteOrder` stays public because it is on Urðarbrunnr's and Midgard's surfaces; it is not replaced by `UuidLayout`.

**Persisted-data guarantee.** Urðarbrunnr's `SqlServerSequentialGuidValueConverter` has written SQL-ordered bytes into clustered keys. HyperUuid #31's `corpus/sql_order.json` was generated from an oracle that ports this realm's permutation, hand-checked on 2026-10-08 (RFC `00000000-0000-7000-8007-010203040506` ↔ SQL `03040506-0102-0077-8000-000000000000`, matching `SequentialGuidBytes.ToSqlOrder`), and its `sql` hex equals .NET `Guid.ToByteArray()` of the SQL-ordered value. That corpus is replayed by HyperUuid's own Rust and C# suites on every PR. Svartálfheim's `SequentialGuidBytesTests` parity vectors therefore retire with the type they tested; `SequentialGuidTests` keeps the round-trip, ordering-under-`SqlGuid`, equality-across-orders, and `Fill` monotonicity properties, now against the engine.

**Clock and counter semantics** change in one observable way: HyperUuid's `FillV7` rolls the millisecond forward on a counter wrap inside one batch (HyperUuid #32, RFC 9562 §6.2 method 1) where the managed `Fill` let the counter wrap silently; and successive single `NewV7()` calls are ordered by HyperUuid's own counter, not by this realm's process-wide `_counter`. Both are improvements; neither is a contract any consumer relies on beyond "later is greater within a millisecond", which holds.

## 6. Family 3 — tabular

### 6.1 `Primitives.Ingestion` is deleted

`ITabularReader`, `TabularReader`, `SepTabularReader`, `ExcelTabularReader`, their tests, their AOT smoke, their README, and their `Svartalfheim.slnx` entries go; `Sep` and `Sylvan.Data.Excel` leave the realm; `Norse.Primitives.Ingestion` stops being published and leaves Yggdrasil's `Directory.Packages.props`. The abstraction had one engine pair behind it and one consumer; the `Architecture.Analyzers` tests that use the assembly name `Norse.Primitives.Ingestion` as a jurisdiction fixture keep the string — it is a name under test, not a reference.

### 6.2 Mímisbrunnr reads HyperTabular directly

Both consumers resolve every column by header name and never by position. HyperTabular 0.8.0's header-first open is the shape they need:

- **`ReferenceDataSeedContributor.SeedAsync(CancellationToken)`** opens each embedded TSV with `await DelimitedReader.OpenAsync(stream, Dialect.Tsv, cancellationToken: ct)`, resolves ordinals through `reader.Header!.Ordinal("M49Code")` (a missing column is a `KeyNotFoundException` naming it — the same drift guard the `ITabularReader.Ordinal` throw gave), binds a plan (`Column.Text` for codes and names — the zero-padded M49 code is a dictionary key and must stay text; `Column.Boolean` for the three flag columns, whose literal `true`/`false` is HyperCast's boolean vocabulary; `Column.Text` for `Level`, parsed by `Enum.Parse<RegionLevel>(row.GetChars(i))` as today), and reads with `await reader.ReadAsync(ct)` row by row through `foreach (var row in batch)`. The seed becomes genuinely cancellable, which it was not. `await using` disposes the reader; the embedded-resource stream is owned by the reader (`leaveOpen: false`).
- **`SeedTool`'s `GenerateUnsdM49Command` and `UnsdM49Mapper.Map`** open the raw UNSD CSV with `DelimitedReader.Open(path, Dialect.Csv with { Separator = ';' })`; `Map` takes the open, unbound `DelimitedReader`, binds thirteen `Column.Text` columns by the names it already lists, and iterates `reader.Rows()`. `row.Line` replaces the hand-counted `rowNumber` in every error message. `ValidateM49Code` keeps calling `Parser.ParseRequired<ushort>` — the forge's gateway, now over HyperCast — on the cell's `GetChars`. The raw file has no BOM, no quoted fields, and a `;` separator (checked 2026-10-09); HyperTabular skips a UTF-8 BOM if one ever appears and handles RFC-style quoting.
- **`SeedTool.Tests`** (`UnsdM49MapperTests`, `UnsdM49RealFileTests`) open their temp files and embedded resources the same way; assertions are unchanged.
- **Project files.** `Reference.Data.EntityFramework.Migrations.csproj` and `SeedTool.csproj` drop `<NorseRef Include="Primitives.Ingestion">` and add `<PackageReference Include="HyperTabular" Version="*" />`; `Primitives` stays referenced for the gateway and `DeterministicGuid`.

A `Row` is a `ref struct` view valid for one iteration, and a `Batch` is reused across reads; the mapper copies what it keeps (`ToString()` on names, as today) and never stores a row. `Batch.GetChars` decodes into a per-batch arena so the mapping path stays allocation-free after warm-up except for the strings the output records own.

### 6.3 What is lost, and accepted

`OpenExcelWorksheet` (`.xls`/`.xlsx`/`.xlsb` first-sheet, stringify-everything) has no consumer; HyperTabular's `Workbook` is strictly more capable for XLSX and ODS (typed cells, sheet by name, header-first) and does not read XLS/XLSB (§2).

## 7. Cross-realm obligations and the release train

The cut is one Svartálfheim release followed by three dependent realm changes and a documentation sweep. `NorseRef` floats (`Version="*"`) in package mode, so each dependent realm picks the new Svartálfheim up on its next build; the order below is the order in which things can be green.

1. **Svartálfheim** — everything in §4–§6.1, one feature branch, one PR, one tag. Breaking (`ParseFailure`, deleted public types and a deleted package): the next tag advances the minor per the realm's pre-1.0 practice.
2. **Midgard** — `FailureDetail.Render` gains `ParseFailure.OutOfRange => $"value '{failure.Input}' is out of range for {failure.ExpectedType}"` (wording to be settled in the plan alongside the existing three); `ResultJsonConverterTests`/`ResultSerializerTests` gain an out-of-range vector; no other Midgard code names a specialist (inventory §4.5). Ships in Midgard's next train.
3. **Mímisbrunnr** — §6.2 in full, plus README/CLAUDE.md (the dependency chart loses `Primitives.Ingestion`, the narrative says HyperTabular).
4. **Yggdrasil** — `Directory.Packages.props`: remove `Norse.Primitives.Ingestion`; add a `<!-- SkunkWerkx -->` block pinning `HyperCast`, `HyperUuid`, `HyperTabular` at `0.8.0`; `Hosting.Migrations.Service` (which composes Mímisbrunnr's migrations contributor) and `Hosting.Web.Server`/`Hosting.Web.Client` (which carry `Norse.Primitives` through Asgard's contracts) re-declare the versionless `PackageReference`s per the composition-root law. Plus the WebAssembly proof in §9.
5. **Record** — Bifröst `README.md`/`CLAUDE.md` realm table (Mímisbrunnr: "TSV seeders (HyperTabular)"), Glitnir `docs/decomposition.md` and `docs/codenames.md` (same phrase), Svartálfheim `README.md` and `CLAUDE.md` (the three capability families become two plus a rewritten parser bullet; the Architecture Facts gain the engine bullets in §8; the spec index gains this row and marks the September row superseded), the realm's `src/Primitives/README.md` package blurb. `origin/feature/hyperuuid-hypercast-ingestion` is deleted by Buvy once this spec is ratified — a branch deletion is the human's act.

## 8. Governance updates (Svartálfheim `CLAUDE.md`)

New Architecture Facts, decided here:

- **HyperCast is the casting engine; HyperUuid the identifier engine; there is no managed path.** `Parser` is a routing gateway over `Cast.Scalar<T>` with an `ISpanParsable<T>` fallthrough; grammar questions are answered by HyperCast's corpus and filed upstream, never patched in the forge.
- **`ParseFailure` is `Unspecified / Empty / Malformed / OutOfRange / Duplicate`** — the first four are `CastFailure` by number; `Duplicate` is Norse's.
- **`Verdict<T>` never appears in a Norse signature.** The forge translates at its edge; the two-unions doctrine has exactly two unions on the platform's surface.
- **`GuidByteOrder` ≡ `UuidLayout` by number**, test-pinned; `SequentialGuid`'s SQL permutation is HyperUuid's, corpus-pinned upstream.
- The parser-template bullet (static class, `ParseRequired`/`ParseOptional`, honest signatures) is retired with the specialists; the gateway bullet absorbs the law that survives (provider required and non-nullable, no defaulting overload, no runtime registry).
- Build & Test: the AOT smoke test is also the native-linking proof; benchmarks compare the gateway to a direct `Cast.Scalar<T>` call.

## 9. Risks and the proofs that retire them

| Risk | Proof |
|---|---|
| **Blazor WebAssembly linking.** `Norse.Primitives` reaches Yggdrasil's `Hosting.Web.Client` through Asgard's contracts (`Result<EmailAddress>` on Heimdall's `RegisterRequest`), so the WASM head now statically links `libhypercast.a` and `libhyperuuid.a`. That needs the `wasm-tools` workload — and, found in execution, the SDK does not demand it: without the workload the publish succeeds with a warning and a runtime that has no engine symbols, so `Hosting.Web.Client` sets `WasmBuildNative=true` to turn the missing workload into a build error and make `dotnet workload restore` install it — the packages' `build/net11.0/*.targets` (which they ship and which add the `NativeFileReference`, the `EmccExportedFunction` list, and the exnref `wasm-opt` pass), and a bundle-size cost (~3 MB of archive per core before the linker strips what is unreferenced). | A plan task publishes `Hosting.Web.Client` with native linking, records the `dotnet.native.wasm` delta. The workload itself arrives through `dotnet workload restore` — a generic step added to the shared CI workflow and the devcontainer, driven by what the projects declare, never a per-realm input (no workflow installs any workload today — 2026-10-09 grep; the SDK fails with `NETSDK1147` naming the workload when it is missing, so nothing upstream needs gating). If the delta is unacceptable, the decision is Buvy's (§10 item 4); the fallback shape would be a trimming boundary, not a managed path. |
| **Native AOT** on every server RID | `tests/smoke/Primitives.Aot.Smoke` publish, zero warnings, exit 0 — already a gate; Mímisbrunnr's migrations run under the real migrations service in Bifröst's Aspire composition. |
| **musl / Alpine containers** | HyperCast and HyperUuid ship `linux-musl-x64`/`-arm64` since 0.7.0; the devcontainer is glibc. No action; recorded so the September concern is visibly closed. |
| **Android** | Packages carry `android-arm64`/`android-x64` as of 0.8.0; no MAUI head exists. `NorseFrontendPlatforms=All` is honest for the first time. No action. |
| **Behavior drift at the edges** (§4.7) | The rewritten `ParserTests` cite corpus vectors; Midgard's rendering tests cover `OutOfRange`; the AOT smoke test's semantic checks are unchanged and must pass. |
| **HyperCast 0.9.0's culture sweep changing provider-bridge behavior** | The floating tag will pick it up; every live call site is invariant and unaffected by design; the gateway tests include one non-invariant culture so a bridge regression is caught in the forge, not in a product. |

## 10. Rulings (Buvy, 2026-10-09)

1. **Package version tags: `Version="*"`.** Both sides of the dependency are the same author; the realm floats. Applied in §3 and §6.2.
2. **`TimeZoneParser` and `TemporalFusion`: keep**, rewired onto the gateway (§4.6).
3. **`OutOfRange` rendering:** `value '{input}' is out of range for {type}` — same shape as the `Malformed` line, no detail clause (§7 item 2).
4. **WebAssembly bundle:** expected to end up *smaller* once the managed parsers and their tests of the BCL leave the trimmed output; the plan's proof task measures `dotnet.native.wasm` and the framework payload before and after and reports both numbers. No budget is set; the numbers are reported and judged then.
5. **Placement:** this document stays under `docs/Svartalfheim/specs/`, cross-realm obligations listed inside it (§7).
6. **Benchmark before and after** (added by the same ruling): the plan's first task records the managed baseline on `master` — dispatch, allocation, and a new identifier rig — on the same machine and configuration the post-cut run uses, and the post-cut numbers are filed beside them as an amendment to the pathway spec §8.

## 11. Open questions for the plan (not blockers)

1. Chunk size for `SequentialGuid.Fill`'s scratch span (§5) — a bounded `stackalloc` of `Guid`s (e.g. 256) with a loop, versus renting from `ArrayPool<Guid>` above a threshold. Benchmark both in the plan's identifier task; neither changes the contract.
2. Whether `Route<TDoor, T>` can be expressed once for both `ParseRequired` and `ParseOptional` via `Cast.Optional<T>(Verdict<T>)` (HyperCast's own absent-mapping door) so the optional path has no second table.
3. Which corpus vectors `ParserTests` cites per door — pick during the plan from HyperCast's `corpus/*.json` at the pinned 0.8.0 tag; the test names the file and vector so a future corpus change is traceable.
4. Mímisbrunnr `Batch` size for the seed read — the default 4096 rows holds both TSVs in one batch; confirm no test depends on multiple batches, or add one that forces `batchRows: 1` to prove the row loop.
5. Whether Yggdrasil's CPM pins should sit at `0.8.0` exact (CPM convention there) while the realms float `0.*` — nearest-wins resolution makes the composition root's pin authoritative at runtime, which is the composition-root law working as designed; confirm no `NU1605` arises from HyperTabular's `>= 0.8.0` floor on HyperCast.
