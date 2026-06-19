# Svartalfheim Second Increment — Pathway Proof: Gateway, Combinators, Evidence Rigs

**Date:** 2026-06-11
**Status:** Approved in design session; plan pending
**Owner:** Buvy
**Parent specs:** `2026-05-20-svartalfheim-primitives-design.md` (as amended 2026-06-07) · `2026-06-11-svartalfheim-result-union-boolean-parser-design.md` (the first increment; its §10 deferred ledger is this spec's docket)

## 1. Scope ruling

The first increment proved the union shape. This increment proves **the pathway**: generic dispatch in (`Parser`), composition through (`Map` / `Bind` / `Match`, law-proven), and evidence out (benchmarks, AOT smoke). It is one spec deliberately — the three pieces interlock (the gateway returns what the combinators consume; the rigs measure both), and the verdict is a single claim: *the pathway is zero-cost and correct end to end*.

**Explicitly out of scope, recorded so nobody re-litigates silently:** additional parsers (the cartesian explosion waits until the pathway is proven — owner's ruling, this session); `Combine`, async combinator siblings, and the `*Present` variants (no consumer forces them yet; the parsers increment will); NuGet packaging.

## 2. The gateway — `Parser`

### 2.1 Why it exists (the registry's ghost, exorcised)

The Crucible's shape was an implicit `string → Result<T>` conversion backed by a `ConcurrentDictionary` parser registry: the assignment hid the parse, and "no parser registered for T" was a runtime `InvalidOperationException`. The first increment killed the implicit conversion permanently — parsing is always an explicit, named call. The gateway is the registry's *compile-time* replacement: the same one-call-fits-any-T convenience, but dispatch is a generic constraint plus JIT-eliminated `typeof` branches, so "no parser for T" is a **compile error**.

Who calls it, honestly:

- **Ingestion plumbing (the real consumer).** Sep hands BDX cells over as `ReadOnlySpan<char>`, alloc-free. Sep's own `Parse<T>()` throws and `TryParse<T>()` returns a bare null — both lose what the forge cares about: which failure (`Empty` vs `Malformed`), the captured bounded input, the expected type. The gateway is the bridge from the span world into the Result world with diagnostics intact.
- **Every future `ISpanParsable<T>` domain type, for free.** Domain types (`PolicyNumber`, `Money`, …) implement `ISpanParsable<TSelf>` themselves — which also buys native citizenship in Sep, System.Text.Json, and route binding — and the gateway adapts any of them into Result shape without each type hand-writing the trim/Empty/Malformed/truncation choreography. The choreography law is written once, here.
- **Not humans at ordinary call sites** — they call the named specialist (`BooleanParser.ParseRequired`) directly; it reads better and skips nothing.

### 2.2 Surface

```csharp
public static class Parser
{
	public static Result<T> ParseRequired<T>(ReadOnlySpan<char> input, IFormatProvider provider)
		where T : notnull, ISpanParsable<T>;

	public static Result<T>? ParseOptional<T>(ReadOnlySpan<char> input, IFormatProvider provider)
		where T : notnull, ISpanParsable<T>;
}
```

- **Constraint is the BCL's `ISpanParsable<T>`** — the ecosystem already named this; we do not mint a house parsing interface. The explicit `notnull` rides alongside because `ISpanParsable<TSelf>` alone does not satisfy `Result<T>`'s `where T : notnull`.
- **Provider is required and non-nullable** — hard-fail-on-ambiguity as a signature. There is no defaulting overload: a call site parsing a `decimal` says `CultureInfo.InvariantCulture` out loud or it does not compile. (First-increment §5.1 precedent, now generalized.)

### 2.3 Routing and choreography

- `typeof(T) == typeof(bool)` routes to `BooleanParser` — the richer ten-pair vocabulary; the provider is deliberately not forwarded (boolean text is culture-insensitive). The branch is resolved at JIT/AOT compile time per first-increment §5.5 — the benchmarks rig verifies the elimination rather than asserting it (§4).
- Everything else: trim → empty/whitespace is `Failure(Empty, "", typeof(T).Name)` (required) or `null` (optional) → `T.TryParse(trimmed, provider, out var value)` → `Success<T>` or `Failure(Malformed, <trimmed, truncated>, typeof(T).Name)`. `ExpectedType` uses CLR type names (`"Boolean"`, `"Int32"`, `"Decimal"`) — consistent with `BooleanParser`'s established `"Boolean"`.
- `Format` and `Detail` stay null on the generic path — the generic gateway has no format axis; specialists that do (future date/money parsers) populate them when routed.

New specialists join by adding a `typeof` branch in exactly one place; the spec for each future parser records its branch. Dispatch never becomes data.

## 3. Combinators — the law-proving core

### 3.1 Surface

Instance methods on `Result<T>` — discoverable on the dot, no namespace import, struct receiver, no boxing:

```csharp
public Result<TResult> Map<TResult>(Func<T, TResult> selector) where TResult : notnull;
public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> binder) where TResult : notnull;
public TResult Match<TResult>(Func<T, TResult> success, Func<Failure, TResult> failure);
```

- `Map` / `Bind` transform the success case; `Failure` flows through untouched. `Match` is the terminal consumer.
- `TResult : notnull` carries the no-nullable-results law through every composition.
- Null delegates throw `ArgumentNullException` immediately. Exceptions thrown *inside* a selector propagate — catching them would be a silent fallback wearing a try block.
- `Map`/`Bind` return `Result<TResult>`, which already carries `[MustConsume]` — the consumption obligation survives composition with no extra machinery.

### 3.2 Two deliberate implementation rulings

1. **Combinators are implemented as union switches** — `this switch { Success<T>(var value) => …, Failure failure => … }`. They dogfood the compiler contract the first increment pinned, and a defaulted `Result<T>` reaching any combinator throws `SwitchExpressionException` exactly as a hand-written switch does. One failure behavior for the defaulted footgun, not two.
2. **Combinators are composition ergonomics, not the hot path** — recorded in the XML docs. BDX-volume loops hand-switch over the cases; nothing in the combinators allocates beyond the caller's own closures, but delegate-shaped code is not where row throughput lives. The benchmarks rig measures the actual tax (§4).

### 3.3 The laws (FsCheck)

The increment's point is not the three methods — it is the proof:

- **Functor:** identity (`r.Map(x => x) == r`) and composition (`r.Map(f).Map(g) == r.Map(x => g(f(x)))`).
- **Monad:** left identity (`lift(a).Bind(f) == f(a)`), right identity (`r.Bind(lift) == r`), associativity (`r.Bind(f).Bind(g) == r.Bind(x => f(x).Bind(g))`) — where `lift` is the union conversion from `Success<T>`.

`Result<T>`'s structural equality (first-increment §4.1) makes every law a direct equality assertion. Generators produce both cases — success values and *valid* `Failure`s only (real reasons, bounded inputs, non-empty `ExpectedType`) — and never `default(Result<T>)`: the defaulted footgun has its own pinned tests; the laws govern constructed values.

### 3.4 Tech gate: FsCheck on xUnit v3 / Microsoft.Testing.Platform

Unverified, recorded exactly like the first increment's §6.1 union-runtime-types gate: the probe tries the `FsCheck.Xunit.v3` integration package; the sanctioned fallback is plain `[Fact]` bodies driving `Prop.ForAll(…).QuickCheckThrowOnFailure()`, which runs on any framework. The build is the arbiter; the plan records which branch was taken.

## 4. Evidence rigs

### 4.1 Benchmarks — `benchmarks/Primitives.Benchmarks`

BenchmarkDotNet with `MemoryDiagnoser`; `benchmarks/Directory.Build.props` mirrors the `tests/` pattern; the root props injects `Norse.Primitives.Benchmarks`. Compiles under the full warnings-as-errors regime on every build; *runs* manually in Release. Three families, each measuring success **and** failure paths:

1. **Storage A/B** (first increment §10.1's demand): inline `Result<bool>` vs a `BoxedResult<bool>` reference twin in construct-and-consume loops at BDX-row volumes. The boxed twin lives in the benchmark project only — it never enters `src/`; it exists to win or lose on the record.
2. **Dispatch:** `BooleanParser.ParseRequired` direct vs `Parser.ParseRequired<bool>` (the `typeof` branch) vs `Parser.ParseRequired<int>` (the generic path) — proving the branch elimination instead of asserting it.
3. **Combinator tax:** a `Map`/`Bind`/`Match` chain vs its hand-rolled switch equivalent.

**Findings are a court filing:** results land as a dated findings amendment to this spec in Glitnir (the `pg19-temporal` precedent). If the storage evidence disagrees with inline, that re-opens §10.1 as a court ruling — never a quiet code change.

### 4.2 AOT smoke — `tests/smoke/Primitives.Aot.Smoke`

A console app (the Glitnir `poc/build` smoke precedent) with `PublishAot=true` exercising the whole pathway: the gateway through the bool specialist, the gateway through the generic `int` path, a combinator chain, and a deliberate failure with its diagnostics read back. Acceptance is two-fold:

1. `dotnet publish` completes with **zero** trim/AOT warnings (`IsAotCompatible` on `src/` self-certifies the analyzers; this proves the published artifact).
2. The native binary itself runs and exits `0` on expected outcomes, non-zero otherwise — fail loud, even in a smoke test.

## 5. Repository shape after this increment

```
Svartalfheim/
  src/Primitives/                      (+ Parser.cs; Result{T}.cs gains the combinators)
  tests/Primitives.Tests/              (+ ParserTests.cs, ResultCombinatorTests.cs, ResultLawTests.cs)
  tests/smoke/Primitives.Aot.Smoke/    (new)
  benchmarks/Primitives.Benchmarks/    (new, with benchmarks/Directory.Build.props)
  Svartalfheim.slnx                    (gains /benchmarks/ folder; smoke under /tests/)
```

## 6. Acceptance criteria

- `Parser.ParseRequired<T>` / `ParseOptional<T>` exist with exactly the §2.2 surface; bool routes to `BooleanParser`; at least one BCL type (e.g. `int`) proves the generic path — both under test including the Empty/Malformed/truncation choreography and the provider law.
- `Map`, `Bind`, `Match` exist with exactly the §3.1 surface — nothing more (no `Combine`, no async, no `*Present`).
- All five laws (§3.3) hold under FsCheck across generated success and failure cases; the §3.4 integration gate is resolved and recorded.
- Defaulted-`Result<T>` behavior through every combinator is pinned (throws `SwitchExpressionException`).
- The three benchmark families compile clean and run; findings filed in this spec's amendment when executed.
- The AOT smoke publishes with zero warnings and the native binary exits 0.
- Full solution: `dotnet build` 0 warnings 0 errors; `dotnet test` green.

## 7. Deferred (this spec's ledger)

1. **`Combine`, async siblings, `*Present` variants** — await the consumer (the parsers/ingestion increment).
2. **Additional parsers** — deliberately held until this pathway is proven (owner's ruling, 2026-06-11).
3. **Storage re-ruling** — only if §4.1 evidence disagrees with inline; a court act, not a code change.
4. **`YGG201` `[MustConsume]` enforcement and the `default(Result<T>)` analyzer rule** — architecture-analyzers spec, unchanged.
5. **NuGet packaging metadata** — when something consumes the package.

## 8. Findings amendment (2026-06-11, post-execution)

Benchmark evidence from the §4.1 rig. Environment: BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8390/25H2), .NET 11.0.0 (11.0.0-preview.5.26302.115), X64 RyuJIT x86-64-v3, `InProcessEmitToolchain` (see §8.1).

### 8.1 Two rig findings that are themselves evidence

1. **BDN 0.15.x cannot run net11.0 preview out-of-process** — its SDK validator throws `NotImplementedException` on the unrecognized moniker. The rig bakes `InProcessEmitToolchain` into its config as the default job; revisit when BDN learns .NET 11.
2. **.NET 11 escape analysis stack-allocates single-frame boxes.** The storage A/B's boxed arms initially reported `Allocated: 0 B` — the JIT proved the box never escaped the benchmark frame and elided the heap allocation entirely, which would have filed false evidence for a design whose results always escape (a parser returns its result). The rig now forces escape with `[MethodImpl(MethodImplOptions.NoInlining)]` factory boundaries, modeling the real parser-returns-result shape. Caught in review by running the rig, not reading it.

### 8.2 Storage A/B

| Method        | Mean      | Ratio | Allocated |
|---------------|----------:|------:|----------:|
| InlineSuccess |  4.920 ns |  1.00 |         – |
| BoxedSuccess  |  3.652 ns |  0.74 |      24 B |
| InlineFailure |  7.592 ns |  1.54 |         – |
| BoxedFailure  | 10.874 ns |  2.21 |      56 B |

**Reading:** the time column at this scale is floor-level (sub-ns deltas through a forced call boundary) and is NOT the verdict; the allocation column is. Inline allocates nothing on either path; boxed pays 24 B per success and 56 B per failure — per row, at BDX volumes, forever. The one cell boxed wins (success-path time, by ~1.3 ns: pointer-copying a reference beats copying a ~64-byte struct through a call boundary) does not outweigh sustained Gen0 pressure on the criterion the design names. **§10.1 stays closed: inline storage confirmed.** Re-measure if `Failure` ever grows.

### 8.3 Dispatch

| Method           | Mean     | Ratio | Allocated |
|------------------|---------:|------:|----------:|
| DirectSpecialist | 14.74 ns |  1.00 |         – |
| GatewayBool      | 15.03 ns |  1.02 |         – |
| GatewayInt       | 11.75 ns |  0.80 |         – |

**Reading:** the `typeof` branch is eliminated as designed — the gateway's bool route costs 2% over calling `BooleanParser` directly (the uniform provider null-check), within noise of free. The generic `int` path is allocation-free. `GatewayInt`'s ratio is a reference point, not a comparison (different parse work). **§2.3's JIT-elimination claim verified, not asserted.**

### 8.4 Combinator tax

| Method           | Mean      | Ratio | Allocated |
|------------------|----------:|------:|----------:|
| HandRolledSwitch |  8.009 ns |  1.00 |         – |
| CombinatorChain  | 22.764 ns |  2.84 |         – |

**Reading:** identical delegate instances on both sides, so the delta is pure combinator plumbing — null-checks, intermediate `Result` copies, switch dispatch. 2.84× in time, **zero allocation delta**. The ~15 ns absolute tax per chain vindicates the doc-comment law: combinators are composition ergonomics; row-volume loops switch over the cases directly.

### 8.5 AOT smoke

**Native gate CLOSED (2026-06-11, same day).** The gate was briefly open on environment, not code: no VS C++ build tools on the workstation, so the platform linker was unavailable (the managed compile and a managed run of all six checks were already green). With the Desktop development with C++ workload installed, `dotnet publish tests/smoke/Primitives.Aot.Smoke -c Release` (win-x64) completed with **zero AOT/trim warnings** and the native `Norse.Primitives.Aot.Smoke.exe` ran **six `ok` checks, exit 0** — the pathway survives native compilation. Standing notes from the code-side review remain law: no reflection, no dynamic code, no trim-analysis surface; `CultureInfo.GetCultureInfo("de-DE")` is safe under default Windows AOT, and `InvariantGlobalization` must never be added to this project — the de-DE probe is a deliberate canary.

### 8.6 Gate results

- FsCheck integration gate (§3.4): `FsCheck.Xunit.v3` **exists** (3.3.3) — recorded for future use; the portable `Prop.ForAll` style was adopted regardless, so the laws run attribute-free on any framework.
- FsCheck restored: 3.3.3 via the `3.*` float. Zero API adaptations — the spec's generator/property code compiled as written.

## 9. Findings amendment (2026-06-17) — parser allocation sweep, post numeric/char/Guid + temporal increments

The §4.1 rig grew a fourth benchmark family, `ParserAllocationBenchmarks`, after the numeric/`char`/`Guid` (2026-06-17) and temporal (2026-06-17) increments landed parsers the original three families never measured. Its single job is the `Allocated` column: every value-returning door of the new specialists must read **0 B** (the inline union's standing claim, §8.2), with one deliberate failure probe to keep the rig honest. Same environment as §8: BenchmarkDotNet v0.15.8, .NET 11.0.0 (11.0.0-preview.5.26302.115), X64 RyuJIT x86-64-v3, `InProcessEmitToolchain` (§8.1). No `NoInlining` factories are needed here — `Result<T>` is the inline zero-boxing union, so a success-path 0 B is real, not an escape-analysis artifact (contrast §8.1's boxed twin).

### 9.1 Success-path sweep — all doors 0 B

Each row is a single representative success input through one door (ISO/direct, exact-format, or Unix-epoch). Means are reference points, not the verdict; the `Allocated` column is.

| Door (input) | Mean | Allocated |
|---|---:|---:|
| `IntegerParser.ParseRequired<int>` (`"1742"`) | 30.09 ns | – |
| `RealParser.ParseRequired<decimal>` (`"1234.5678"`) | 52.51 ns | – |
| `GuidParser.ParseRequired` (D-format) | 26.55 ns | – |
| `CharParser.ParseRequired` (`"U+0041"`, code-point branch) | 13.53 ns | – |
| `DateOnlyParser` ISO (`"2026-06-17"`) | 99.85 ns | – |
| `DateOnlyParser` exact (`"MM/dd/yyyy"`) | 86.49 ns | – |
| `TimeOnlyParser` ISO (`"13:45:30"`) | 126.93 ns | – |
| `TimeOnlyParser` exact (`"h:mm:ss tt"`) | 182.74 ns | – |
| `DateTimeParser` ISO (`"…T12:30:00Z"`) | 208.44 ns | – |
| `DateTimeParser` exact | 194.13 ns | – |
| `DateTimeParser` Unix (seconds) | 20.33 ns | – |
| `DateTimeOffsetParser` ISO (`"…+00:00"`) | 582.37 ns | – |
| `DateTimeOffsetParser` exact | 188.01 ns | – |
| `DateTimeOffsetParser` Unix (seconds) | 16.04 ns | – |
| `TimeSpanParser` colon (`"1.02:03:04"`) | 101.56 ns | – |
| `TimeSpanParser` exact (`"c"`) | 25.27 ns | – |
| `TimeSpanParser` ISO duration (`"P3DT4H30M"`) | *see §9.2* | *see §9.2* |

**Reading:** sixteen of seventeen success doors allocate nothing — the inline-union claim holds across the full scalar surface, not just the bool/`int` pathway §8 proved. The lone `DateTimeOffsetParser` ISO latency outlier (582 ns, ~2.8× the `DateTime` twin) is real but allocation-free; it is not crooked and is left as an observation, not a work item.

### 9.2 The crooked path made straight — `TimeSpanParser` ISO duration

The ISO-8601 duration door allocated **424 B** on its *success* path — the only non-zero success row in the sweep, and exactly the kind of thing this rig exists to catch. The hand-rolled `TryParseIso8601Duration` is pure span/value work; the tell was in the neighbors (colon and exact doors both 0 B). Root cause: `ParseDuration` speculatively ran the BCL colon parser (`TimeSpan.TryParse`) **first**, and the colon parser allocates 424 B on its reject path when fed a `P…` input it was never going to accept.

Fix (a `ParseDuration` ordering change, not a grammar change): sniff the leading `P` (optionally signed) — the clean discriminator between the two grammars, since colon form never carries one — and route ISO inputs straight to the hand-rolled parser, skipping the doomed colon attempt. Behavior is byte-for-byte preserved (the 28 `TimeSpanParser` tests stay green; the grammars partition disjointly on the `P` prefix). Re-measured after the fix: **0 B**, and mean fell from 293.53 ns to ~59 ns — the speculative reject was most of the cost. The bend stays in the record per the crooked-path ethos: the door allocated, here is why, here is the straightening.

### 9.3 Failure probe — the rig is live

| Door (input) | Mean | Allocated |
|---|---:|---:|
| `IntegerParser.ParseRequired<int>` (`"not-a-number"`) | 42.82 ns | 48 B |

One `Malformed` probe, expected non-zero: the `Failure` span ctor bounds to `MaxInputLength` and then allocates the captured-input string. This is by design (truncation knowledge lives in `Failure`, first-increment law) and doubles as proof the `MemoryDiagnoser` is measuring — the 0 B rows above are real, not a dead benchmark.
