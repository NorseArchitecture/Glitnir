# Svartalfheim — HyperUuid/HyperCast Ingestion Design (`Norse.Primitives`)

**Date:** 2026-09-03
**Status:** Design — approved, pending plan
**Realm:** Svartalfheim (`Norse.Primitives`)
**Prior art / upstream relationship:** [SkunkWerkx/HyperUuid](https://github.com/SkunkWerkx/HyperUuid) and [SkunkWerkx/HyperCast](https://github.com/SkunkWerkx/HyperCast) — Buvy's own polyglot Rust-core libraries, maintained upstream by the same author as this realm. Not a cold external dependency: HyperUuid's C# binding already ported byte-order-transform patterns from this realm's own `SequentialGuid`/`DeterministicGuid` (its README cites Svartalfheim by name for the SQL Server ordering permutation), and HyperCast's `corpus/*.json` conformance vectors are explicitly seeded from this realm's own `Primitives.Tests` suites ("this project descends from"). Ingestion and upstream contribution run in both directions.

## 1. Problem

Svartálfheim's `Result<T>`/`Parser`/scalar-parser stack (`src/Primitives`) and `Identifiers` (`SequentialGuid`/`DeterministicGuid`) are 100% managed C#, hand-authored, zero-alloc, AOT-clean — the platform's forge. HyperUuid and HyperCast are Rust-core polyglot libraries covering the same problem shapes (UUID generation, scalar parsing from untrusted text) with published, AOT-friendly, benchmark-proven C# bindings.

Two rounds of real `BenchmarkDotNet` evidence were gathered on this platform (linux-arm64, net11.0 preview, `InProcessEmitToolchain`) before this design was written — not assumed, measured:

**Identifiers (HyperUuid), after fixing a real bug this session found in `SequentialGuid.Fill`** (entropy was drawn per-item via up to 1,000 separate `RandomNumberGenerator.Fill` syscalls instead of batched — see §5 for the fix, already landed independently of this spec):

| Door | Svartálfheim | HyperUuid | Ratio |
|---|---:|---:|---:|
| v7 single generate | 1,808.60 ns | 1,012.45 ns | 1.8× |
| v7 batch fill (1000) | 63,407.62 ns | 21,688.82 ns | 2.9× |
| v5 (deterministic) generate | 773.44 ns | 134.01 ns | 5.8× |
| SQL byte-order round trip | 115.17 ns | 40.95 ns | 2.8× |

**Scalar parsers (HyperCast) vs. the doors it descends from:**

| Door | Svartálfheim | HyperCast | Ratio |
|---|---:|---:|---:|
| Boolean | 32.33 ns | 16.87 ns | 1.9× |
| Int32 (grouped) | 58.80 ns | 46.94 ns | 1.3× |
| Guid | 66.64 ns | 40.89 ns | 1.6× |
| Timestamp (RFC 3339) | 438.46 ns | 56.70 ns | 7.7× |

Both engines are zero-allocation on both sides of every comparison. The performance case is real but not uniform — three of four HyperCast doors show the modest "crossing tax, paid honestly" HyperCast's own README predicts; Timestamp is a outlier plausibly explained by `DateTimeOffsetParser` trying four hardcoded exact-format strings sequentially rather than parsing an actual RFC 3339 grammar (see §6.3) — not yet confirmed as a fix the way the `SequentialGuid.Fill` bug was.

**Amendment (2026-09-03, Task 13):** Confirmed — the 7.7× outlier was entirely a missing-native-routing bug, not a managed-algorithm cost. The pre-Task-13 `ParseIso` never called `HyperCast.Cast.Timestamp` at all; it ran the four-hardcoded-format `TryParseExact` unconditionally on every call, native-capable host or not. Rewriting `ParseIso` to a real RFC 3339 grammar (§6.3) and wiring the missing `NativeCapability.Available` branch together, re-measured on the same rig (linux-arm64, net11.0 preview, `InProcessEmitToolchain`): `TimestampSvartalfheim` 92.99 ns vs. `TimestampHyperCast` 52.77 ns — 1.76×, in line with the other three doors' "crossing tax" (1.3×-1.9×), not an outlier anymore. The residual ~40 ns is `Result<T>`/pattern-match wrapping around the native call, the same tax Boolean and Guid already pay — nothing left to converge. Full grammar rewrite, corpus audit, and test evidence: `.superpowers/sdd/2026-09-03-svartalfheim-hyperuuid-hypercast-ingestion/task-13-report.md`.

**Amendment (2026-09-04, Task 17 — full-suite verification, both benchmark files run together):** Re-measured on the same rig (linux-arm64, net11.0 preview, `InProcessEmitToolchain`) via the actual verification-pass command (`dotnet run -c Release --project benchmarks/Primitives.Benchmarks -- --filter "*IdentifierBenchmarks*" "*HyperCastBenchmarks*"`), not an isolated single-file run. Caveat on reading the raw BenchmarkDotNet output: both `IdentifierBenchmarks` and `HyperCastBenchmarks` declare exactly one `[Baseline]` method each (`GenerateV7Svartalfheim` and `BooleanSvartalfheim` respectively), so BDN's own `Ratio` column is computed against that single baseline for every method in the class — it is **not** a pairwise Svartálfheim-vs-native ratio per door. The ratios below are computed directly from each pair's own Mean.

| Door | Svartálfheim | Native | Ratio |
|---|---:|---:|---:|
| v7 single generate (HyperUuid) | 939.58 ns | 914.83 ns | 1.03× |
| v7 batch fill (1000) (HyperUuid) | 29,967.19 ns | 19,057.06 ns | 1.57× |
| v5 (deterministic) generate (HyperUuid) | 143.78 ns | 119.85 ns | 1.20× |
| SQL byte-order round trip (HyperUuid) | 136.55 ns | 45.43 ns | 3.01× |
| Boolean (HyperCast) | 42.09 ns | 15.87 ns | 2.65× |
| Int32 (grouped) (HyperCast) | 83.18 ns | 45.65 ns | 1.82× |
| Guid (HyperCast) | 94.37 ns | 39.21 ns | 2.41× |
| Timestamp RFC 3339 (HyperCast) | 92.43 ns | 51.70 ns | 1.79× |

The HyperCast (scalar-parser) doors land close to the isolated numbers already on record (§1's table and the Task 13 amendment) — the native path is faster in the full-suite rig too, ratios 1.79×-2.65×, no surprise there.

**Surprise, Identifiers (HyperUuid) side:** the gap has narrowed sharply from §1's original table, and **v7 single generate is now statistically at parity** (939.58 ns vs. 914.83 ns, 1.03×, well inside the ~±20-40 ns StdDev on each side) — not the 1.8× this spec's §1 table records. Batch fill and v5 generate similarly compressed (2.9×→1.57×, 5.8×→1.20×); only the SQL byte-order round trip stayed close to its original ratio (2.8×→3.01×). This is not a regression in HyperUuid — both sides got faster in absolute terms (e.g. v7 single generate: Svartálfheim's own managed door fell from 1,808.60 ns to 939.58 ns, roughly halving), consistent with real managed-side work landing since §1's numbers were taken (the `SequentialGuid.Fill` entropy-batching fix, §5, plus whatever JIT/environment variance separates this container run from the original). **Net effect: the identifier seam's practical benefit today is real but modest** (1.03×-3.01×, mostly clustering under 1.6×) rather than the "5.8× outlier" character of §1's original table — worth a look before citing §1's Identifiers table as current guidance; this amendment supersedes it for that purpose. Full run data: `.superpowers/sdd/2026-09-03-svartalfheim-hyperuuid-hypercast-ingestion/task-17-report.md`.

**Correction (2026-09-04, same-day follow-up — final whole-branch review):** the causal explanation above ("both sides got faster in absolute terms") is true as a description of the raw numbers but attributes the wrong cause for *why the ratio compressed*, confirmed by reading `benchmarks/Primitives.Benchmarks/IdentifierBenchmarks.cs` directly. The `GenerateV7Svartalfheim`/`FillBatch1000Svartalfheim`/`GenerateV5Svartalfheim`/`SqlOrderRoundTripSvartalfheim` benchmark methods call the **public** `SequentialGuid`/`DeterministicGuid` API (`new SequentialGuid()`, `SequentialGuid.Fill(...)`, `new DeterministicGuid(...)`, `.ToSqlOrder()`/`.ToRfcOrder()`) — and every one of those, per this branch's own Phase-1 work, now internally checks `NativeCapability.Available` and routes to HyperUuid when true, exactly like the `*HyperUuid` comparison arm does. **On this native-capable benchmark host, both benchmark arms in each pair now call into HyperUuid** — the "Svartálfheim" arm just does so through one extra layer of struct construction/field extraction. The measured ratio in the table above is therefore (native + wrapper overhead) ÷ (native alone) — wrapper overhead — **not** a managed-vs-native comparison, which is what the amendment's prose above implies it is measuring. The absolute-number-improvement explanation isn't false, but it's incomplete: the dominant reason the ratio compressed is that the ingestion succeeded and the "Svartálfheim" arm stopped being managed code on this host at all, not (primarily) that the managed implementation itself got faster. There is currently no `ForManagedOnly`-forced benchmark arm in this project, so a true managed-vs-native re-measurement is not possible today; adding one requires an `InternalsVisibleTo` grant to the benchmarks project, which doesn't currently exist — tracked as a follow-up, not done in this pass. The measured numbers in the table above are unchanged and remain accurate as wrapper-overhead measurements; only this causal interpretation is corrected.

## 2. Scope

**In scope, this design:**

- **Phase 1 — Identifiers → HyperUuid.** `SequentialGuid`, `DeterministicGuid`, and the SQL byte-order transforms gain a native execution path on platforms/RIDs HyperUuid covers, with the existing managed implementation as the fallback everywhere else. Public API unchanged.
- **Phase 2 — `Result<T>`/`Parser`/scalar parsers → HyperCast.** Same seam pattern, applied to the much larger and doctrine-sensitive surface: `BooleanParser`, `IntegerParser`, `RealParser`, `GuidParser`, `CharParser`, `DateOnlyParser`, `DateTimeParser`, `DateTimeOffsetParser`, `TimeOnlyParser`, `TimeSpanParser`, `TimeZoneParser`, `TemporalFusion`. Includes a real, breaking grammar-convergence pass (§6) using HyperCast's own corpus as the conformance authority — "HyperCast is the source of truth" governs both what counts as correct and what `ParseFailure` can represent, not just which engine runs.

**Explicitly out of scope, deferred:**

- **Phase 3 — `Primitives.Ingestion` → HyperTabular/HyperDelimited/HyperWorkbook.** HyperCast's own roadmap already names this as the platform's third and final capability family (`ITabularReader`/`SepTabularReader`/`ExcelTabularReader`), completing the same three-family mapping Phase 1/2 establish. Blocked, not designed here: per HyperCast's README, HyperTabular/HyperDelimited/HyperWorkbook exist today only as Rust crates — "bindings and the corpus are what remain." This is upstream work in the SkunkWerkx repos, not a Svartálfheim-side gap. Revisit once C# bindings ship.
- **iOS/Android RID coverage for HyperUuid/HyperCast** — a confirmed, absolute gap today (zero mobile RIDs in either repo). Upstream work in the SkunkWerkx repos, tracked in §9, not blocking Phase 1/2 because no MAUI head exists on this platform yet (`Primitives.csproj`'s `NorseFrontendPlatforms=All` is explicitly documented as prep for a future build-out, not a live consumer).
- Any other realm's adoption of anything downstream of this (e.g. Mímisbrunnr's use of `Primitives.Ingestion`) — each realm's own future decision, once this pattern is proven here.

## 3. Architecture — the seam

`src/Primitives.csproj` takes two new `PackageReference`s: `HyperUuid`, `HyperCast`. This is genuinely novel for this project — confirmed by inspection, `Primitives.csproj` today has zero external runtime dependencies (its only references are an in-repo analyzer wired as `ReferenceOutputAssembly="false"`, and `MinVer`, a build-time-only versioning tool with `PrivateAssets="all"`). It does **not** contradict this realm's "permanent architectural leaf, declares no `NorseRef`" self-description — that doctrine is specifically about internal cross-realm references (Asgard/Midgard/etc.); HyperUuid/HyperCast are external, non-Norse packages.

**Every existing public entry point keeps its exact signature.** Internally, each gets a two-layer capability gate:

1. **`OperatingSystem.IsAndroid()`/`IsIOS()`/etc.** — trimmer-foldable. A build published for `ios-arm64` has this branch, and the HyperUuid/HyperCast reference, eliminated entirely from the trimmed output. Mirrors the exact mechanism HyperUuid's own binding already uses to pick its WASM path (`OperatingSystem.IsBrowser()`).
2. **A cached, one-time capability probe** behind the OS check — call a trivial native entry point inside a `try`/`catch` at static init, cache the `bool`. Needed because the OS check alone can't distinguish RID families the OS check reports identically: `OperatingSystem.IsLinux()` is `true` on both glibc and musl, but HyperUuid/HyperCast only ship glibc `linux-x64`/`linux-arm64` — an Alpine container would pass the OS check and then throw `DllNotFoundException` on first call without this second gate.

**Translation happens at the call site, using data the caller already has** — not a new shared vocabulary. Example shape:

```csharp
static Result<bool> Parse(ReadOnlySpan<char> trimmed) =>
    NativeCapability.Available
        ? Translate(Cast.Boolean(trimmed))
        : ParseManaged(trimmed);

static Result<bool> Translate(Verdict<bool> verdict) =>
    verdict switch
    {
        Success<bool> s => new Success<bool>(s.Value),
        Fault f => new Failure(Map(f.Reason), /* the caller's own trimmed span */ ..., ExpectedType),
    };
```

`Failure`'s existing truncation/allocation rules (`MaxInputLength`, the span constructor) apply unchanged regardless of which engine produced the verdict — the native path never needs its own diagnostic-capture story.

## 4. Phase 1 — Identifiers

- `SequentialGuid()` (parameterless ctor): native path calls `UuidGenerator.NewV7()`; `Timestamp`/`Order` extraction is unchanged (`SequentialGuidBytes.ExtractTimestamp` works on either engine's output since both produce byte-identical RFC 9562 v7 layouts).
- `SequentialGuid.Fill`/`CreateMany`: native path calls `UuidGenerator.FillV7(Span<Guid>)` into a scratch buffer, then wraps each element. Exact scratch-buffer sizing (bounded chunking vs. a single `count`-sized buffer) is an implementation-plan-level detail, not decided here.
- `DeterministicGuid` constructors: native path calls `UuidGenerator.NewV5(namespaceId, name)`.
- `SequentialGuid.ToSqlOrder()`/`ToRfcOrder()`: native path calls `UuidGenerator.V7ToSqlOrder`/`V7FromSqlOrder`. HyperUuid's README claims byte-for-byte parity with this realm's own permutation — **this claim gets a pinned test, not trust**: a round-trip/parity test asserting the native and managed transforms produce identical output for the same input, run as part of Phase 1's correctness oracle.

No `INorseGuid`/`GuidByteOrder`/public-surface changes. This phase is the low-risk one — it derisks the seam mechanism (OS check, probe, trimming) cheaply before Phase 2 reuses it for the much larger surface.

## 5. Prerequisite already landed: `SequentialGuid.Fill` entropy batching

Found and fixed during this session's benchmarking, independent of and prior to this spec: `Fill` drew entropy per-item (`RandomNumberGenerator.Fill` called once per UUID — up to 1,000 separate syscalls for a 1,000-item batch), which was the dominant cost in a 39× gap against HyperUuid's own batch API. Fixed by drawing entropy in fixed-size chunks (3,072-byte stack buffer, ~512 items/draw) — took batch fill from 847,955 ns to 63,408 ns (13.4× improvement), closing the gap to the 2.9× shown in §1's table. Already merged into `src/Primitives/Identifiers/SequentialGuid.cs` with a new boundary-crossing regression test in `tests/Primitives.Tests/Identifiers/SequentialGuidBatchTests.cs`; not itself part of this spec's implementation plan, but the evidence base this design's Phase 1 numbers rest on.

## 6. Phase 2 — `Result<T>`/`Parser`/scalar parsers

**Amendment (2026-09-03, Task 13):** "HyperCast is the source of truth" is broader than this
section's original framing stated. Direct ruling from Buvy: *"I want HyperCast to be the source of
truth for the BCL types... we will be cutting the logic out from here."* This is not scoped to
grammar/`ParseFailure`-vocabulary questions alone — it extends to genuine conflicts with
pre-existing Svartálfheim-specific policy where HyperCast's corpus says otherwise (the live case:
`DateTimeOffsetParser`'s ISO-door sentinel guard, §9 of the temporal-parsers design spec, narrowed
by amendment there for exactly this reason). The managed fallback is understood to be transitional
— expect it to shrink over time, not stay in permanent doctrine lockstep with HyperCast. Where a
corpus vector conflicts with existing managed behavior, converge the managed side to match the
corpus; only a genuine two-sided API limitation (neither engine can express what a vector needs)
still gets named-and-excluded rather than converged.

**Standing companion directive, this phase onward:** every task also watches for real feature/
quality-of-life gaps in HyperCast's own C# binding API (not a Svartálfheim-side issue) surfaced by
the corpus audit — report them as upstream candidates rather than working around them silently.
Buvy maintains HyperUuid and HyperCast and wants this realm to be a real first-consumer proving
ground, making HyperCast's parsing surface first-class through actual use, not just adopting it.
Running list tracked in the implementation plan's ledger; compiled at the plan's final review.

### 6.1 `ParseFailure` renumbers to match `CastFailure`

`CastFailure`: `Unspecified(0), Empty(1), Malformed(2), OutOfRange(3)`. Today's `ParseFailure`: `Unspecified(0), Empty(1), Malformed(2), Duplicate(3)`. Three of four already match by name, number, and semantics — HyperCast genuinely descends from this code. The convergence:

```csharp
public enum ParseFailure : byte
{
    Unspecified = 0,
    Empty = 1,
    Malformed = 2,
    OutOfRange = 3,   // new — adopted from CastFailure verbatim
    Duplicate = 4      // Svartálfheim's own addition, renumbered to make room
}
```

**This is a real, deliberate breaking change**, not internal wiring — `ParseFailure`'s own doc comment already states exhaustive switches over it become build errors until updated (`WarningLevel 9999`/`TreatWarningsAsErrors` enforces this platform-wide). Every exhaustive `switch` over `ParseFailure` anywhere on the platform needs inventorying as an implementation-plan task, not just within this realm.

### 6.2 `OutOfRange` requires real new logic, not just a translation-layer remap

Today, `IntegerParser`'s own doc comment states *"byte `256` is `ParseFailure.Malformed` for free"* — malformed and out-of-range are indistinguishable in the managed path. Distinguishing them to match `CastFailure.OutOfRange` means genuine new branches in every range-bounded managed parser (confirmed for `IntegerParser`; the numeric family (`RealParser`) and range-bounded temporal doors (timestamp-past-9999, excel-serial) need the same audit — not yet performed, first task for the plan).

### 6.3 Grammar gaps — confirmed and unaudited

**Confirmed, from direct comparison this session:**
- `RealParser` has no equivalent of HyperCast's `NumFormat.Detect` (structural `.`/`,` separator-role resolution — repeated separator ⇒ grouping, non-3-digit right run ⇒ decimal, genuinely ambiguous ⇒ `Malformed`, never guessed). HyperCast's README documents the exact rules precisely enough to port. Likely the single largest chunk of net-new managed code in this phase.
- `DateTimeOffsetParser.ParseIso` tries four hardcoded exact-format strings sequentially rather than parsing an actual RFC 3339 grammar; HyperCast's timestamp door is a real grammar. Converging plausibly also resolves the 7.7× outlier in §1, as a side effect rather than the goal. **Confirmed (Task 13, see §1 amendment):** the outlier was a missing native-routing branch, not the grammar gap itself — `ParseIso` never called into HyperCast at all pre-Task-13. Fixing both together collapses the ratio to 1.76×, in line with the other three doors.

**Not yet audited against HyperCast's corresponding doors** — plan's first task, door by door: `CharParser`, `DateOnlyParser`, `DateTimeParser`, `TimeOnlyParser`, `TimeSpanParser`, `TimeZoneParser`, `TemporalFusion`. `GuidParser` and `BooleanParser` were checked directly and already match HyperCast's grammar exactly (HyperCast's boolean vocabulary and GUID prefix/format list are verbatim copies).

### 6.4 Corpus as the conformance authority

HyperCast's `corpus/*.json` (380 vectors, 12 files) becomes Svartálfheim's shared test data for both engines — not "we assume they agree," a build-time proof. Both the native path and the managed fallback run against the identical vectors.

**Corpus delivery** is real logistics, not a detail: the corpus lives in the `HyperCast` repo. Proposed mechanism — HyperCast ships a small companion content package (e.g. `HyperCast.Corpus`) that Svartálfheim's test project references, versioned in lockstep with HyperCast itself. This is upstream work (§9), and useful beyond this project: every one of HyperCast's other seven bindings has the identical "how does a downstream consumer get the corpus" problem today.

**Dual-mode CI**, so the managed path stays proven and doesn't bit-rot unexercised: an internal test-only `AppContext` switch forces the managed path regardless of host platform. The full corpus suite runs twice — once native, once forced-managed — on every build, since every dev/CI box available today is native-capable and would otherwise only ever exercise the managed fallback on real MAUI hardware that doesn't exist yet.

## 7. Governance updates required

- `ParseFailure.cs`'s doc comment — new vocabulary, mirrors `CastFailure`.
- Svartálfheim `CLAUDE.md` — new Architecture Facts bullet documenting the seam pattern (two-layer capability gate, translation-at-call-site, corpus-as-authority); new Build & Test bullet for the dual-mode corpus run; this spec's row added to the spec index table.
- No change needed to the "permanent architectural leaf, declares no `NorseRef`" line (§3) or to the parser-template bullet (still static classes, still no runtime registry — HyperCast's doors are called from inside the same static methods, not exposed as a parallel registry).

## 8. Rollout order

Phase 1 (Identifiers/HyperUuid) ships first in the implementation plan — small, self-contained, no wire-format doctrine implications, derisks the capability-gate mechanism cheaply. Phase 2 (`Result<T>`/Parser/HyperCast) ships second, reusing the proven mechanism for the doctrine-sensitive, grammar-convergence-heavy surface. One spec, sequenced phases in the plan (per explicit direction — not decomposed into separate specs).

## 9. Upstream companion work (tracked here, executed in the SkunkWerkx repos — not part of this repo's implementation plan)

1. **iOS/Android RIDs** for both HyperUuid and HyperCast — closes the confirmed MAUI gap. Not blocking Phase 1/2 (no MAUI head exists yet on this platform), but blocking Phase 3 relevance and any future MAUI head's ability to use the native path at all.
2. **HyperTabular/HyperDelimited/HyperWorkbook C# bindings** — blocking Phase 3 entirely; currently Rust-crate-only per HyperCast's own roadmap.
3. **`HyperCast.Corpus` companion content package** — blocking §6.4's corpus-delivery mechanism; also directly useful to HyperCast's other seven bindings.
4. **`NumFormat`/`NumStyles` has no per-call door for a caller-declared, non-`CultureInfo`-backed separator pair** — found auditing `IntegerParser` and `RealParser` against `integer.json`/`real.json`'s own `format`-tagged vectors (Tasks 11 and 12). HyperCast's corpus itself declares vectors with an arbitrary `decimal_sep`/`group_sep` pair that isn't any real culture's convention (e.g. decimal `,` / group `.`, or decimal `.` / group `,` with `flags:0`/`flags:31`/`flags:63`), but the C# binding's own `NumFormat.From(CultureInfo)`/`NumFormat.Invariant`/`NumFormat.Detect` only construct from a real `CultureInfo` or from structural detection — there is no `NumFormat.From(decimalSep, groupSep, NumStyles)` (or equivalent) construction door taking the separator characters and lenience flags directly. Concrete corpus vectors this gap excludes from both engines' Svartálfheim test theories today (not a leniency mismatch — neither engine can even attempt these without the door):
   - `integer.json` (`i32`, 5 vectors): `"1.234"` → `ok` 1234 (decimal `,` / group `.`, flags 31); `"1,5"` → `malformed` (same format); `"1,234"` → `malformed` (decimal `.` / group `,`, flags 0); `"12,345"` → `malformed` (decimal `.` / group `,`, flags 63); `"1.234.567"` → `ok` 1234567 (same format).
   - `real.json` (`f64`, 2 vectors): `"1.234,5"` → `ok` 1234.5 (decimal `,` / group `.`, flags 31); `"1 234,5"` → `ok` 1234.5 (same format, group separator is U+00A0 NO-BREAK SPACE — not just a different ASCII character, a different Unicode code point entirely).
   Once this door exists in the C# binding, Svartálfheim's `IntegerParser`/`RealParser` would gain a matching caller-declared-format overload and re-include all 7 vectors.
5. **`NumFormat`/`NumStyles` has no per-call door to disable percent-suffix (`%`) lenience** — found auditing `RealParser` (Task 12). `real.json` carries `"50%"` tagged `format` with `flags:0` (every lenience explicitly off, including percent) expecting `malformed` — but both `NumFormat.Invariant` and `NumFormat.Detect` always honor a trailing `%` (matching `RealParser`'s own current behavior, which has no way to turn it off either). This is the same class of gap as item 4 — a caller-declared `NumStyles`-equivalent needs a way to explicitly clear the percent bit per call, not just accept the profile's default lenience set.

## 10. Open questions for the plan

1. Exact scratch-buffer strategy for `SequentialGuid.Fill`'s native path (§4) — bounded chunking vs. single buffer.
2. Full door-by-door grammar audit for the seven not-yet-checked parsers (§6.3) — must happen before Phase 2 implementation, not during it.
3. Inventory of every exhaustive `switch` over `ParseFailure` platform-wide (§6.1) that the renumbering/widening touches.
4. Whether the native/managed `CastFailure`↔`ParseFailure` mapping can be a numeric reinterpret cast (identical byte values for the shared four members) rather than a switch — a performance/simplicity detail, not a design blocker either way.
