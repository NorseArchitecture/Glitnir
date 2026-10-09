# The Hyper Engines Cut — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task, paired with superpowers:test-driven-development on every coding task. Steps use checkbox (`- [ ]`) syntax for tracking. Every task starts with its failing test.

**Goal:** Replace Svartálfheim's managed scalar parsers, identifier arithmetic, and tabular ingestion with HyperCast, HyperUuid, and HyperTabular v0.8.0 as the only engines — public `Result<T>`/`Parser`/`SequentialGuid`/`DeterministicGuid` contracts preserved, `ParseFailure` widened with `OutOfRange`, `Primitives.Ingestion` deleted — and carry the consequential changes through Midgard, Mímisbrunnr, Yggdrasil, Ginnungagap, and the record, with the managed baseline benchmarked before the first deletion and the engines benchmarked after the last.

**Architecture:** `Parser` keeps its `typeof` routing table but every branch is a one-line reinterpret into `HyperCast.Cast.Scalar<T>`; `Verdict<T>`/`Fault` are translated into `Result<T>`/`Failure` inside the forge and never appear in a Norse signature. `SequentialGuid`/`DeterministicGuid` are thin wrappers over `HyperUuid.UuidGenerator` (`GuidByteOrder` ≡ `UuidLayout` by number). Mímisbrunnr reads `HyperTabular.DelimitedReader` directly, header-first, asynchronously. No probe, no fallback.

**Tech Stack:** .NET 11 RC1, C# 15 (preview), `HyperCast`/`HyperUuid`/`HyperTabular` 0.8.0 (`LibraryImport` P/Invoke, Native AOT static archives, browser-wasm static linking), xUnit v3 on Microsoft.Testing.Platform, Shouldly, BenchmarkDotNet (`InProcessEmitToolchain`).

**Spec:** `../specs/2026-10-09-hyper-engines-cut-design.md` (ratified 2026-10-09; rulings in its §10 are law here). Supersedes `2026-09-03-svartalfheim-hyperuuid-hypercast-ingestion.md` in this folder — do not read that plan for guidance; it describes the seam.

## Global Constraints

- Session root is Bifröst. Each realm's work lands on **one** feature branch in that realm's submodule (`feature/hyper-engines-cut` in Svartálfheim, Midgard, Mímisbrunnr, Yggdrasil; `feature/ci-workloads` in Ginnungagap). **Bifröst itself is never branched.** No commits by the agent: stage, show the diff, stop — the human commits every task.
- `dotnet build <Realm>.slnx` — warnings are errors in every realm (`WarningLevel 9999`, `EnforceCodeStyleInBuild`); one warning fails. IDE0005 is never suppressed — delete the using.
- Svartálfheim tests: `dotnet test tests/Primitives.Tests -- --filter-class "*.ClassName"` (VSTest `--filter` does not work). Never run a test project that has zero tests.
- House rules (`../../house-rules.md`) in force throughout: target-typed `new()` for construction, collection expressions, `var` elsewhere, expression bodies with the arrow on the declaration line, ternaries chopped `cond ?` / `a :` / `b;`, `is null`/`is not null`, no string concatenation, primary constructors, every class `sealed`/`static`/`abstract`, test classes `public sealed`, test methods without an access modifier and sentence-shaped (`Should_{behavior}_when_{condition}` in `Primitives.Tests`; `{Action}_{observed}` in Mímisbrunnr), one `<PropertyGroup>` and one `<ItemGroup>` per csproj sorted alphabetically, `ConfigureAwait(false)` on every await in src (never in tests), `CancellationToken cancellationToken = default` last on every async method, tabs.
- Package references to the engines are `Version="*"` (spec §10 item 1). Yggdrasil's CPM pins exact versions (`0.8.0`).
- Two type names collide between the forge and HyperCast: `Success<T>` and (in Mímisbrunnr) nothing else. Inside `namespace Norse.Primitives`, bare `Success<T>` is the forge's; HyperCast's is written `HyperCast.Success<T>` at exactly the sites listed in Task 3, each with the one-line justification comment the house rules require for a qualified name. Nowhere else.
- `Failure.Input` is the **trimmed** caller span, bounded by the existing span constructor; the engine is handed the untrimmed span (the `char` door reads one character before trimming).
- Spec §4.7 is the list of observable semantic changes. A test that enshrines the old behavior for one of those inputs is rewritten to the corpus's answer, with the corpus file and vector cited in a comment; any other behavior difference discovered is a finding to report, not something to paper over.
- Benchmarks run in Release on the same machine for the before (Task 1) and after (Task 7) passes; both tables go into the same Glitnir amendment.
- Dependent realms (Tasks 8–12) are developed in Bifröst **dev mode** (`UseProjectReferences`, the workspace crossing) against the Svartálfheim branch, so they build and test before Svartálfheim ships; their standalone/CI builds go green only after the Svartálfheim tag. Order the PRs accordingly.

## Task sequence and gates

| # | Realm | Task | Gate before the next |
|---|---|---|---|
| 1 | Svartálfheim + Yggdrasil | Baseline: identifier benchmark rig, all three rigs run, WASM bundle measured | tables filed in Glitnir |
| 2 | Svartálfheim | Engine references, `ParseFailure.OutOfRange`, `Failure` guard | green |
| 3 | Svartálfheim | `Parser` over `Cast.Scalar<T>` (specialists still present) | green, AOT smoke green |
| 4 | Svartálfheim | Delete the specialists and the extra doors; rewire `TemporalFusion`; smoke and rigs | green, AOT smoke green |
| 5 | Svartálfheim | Identifiers over `UuidGenerator`; delete the byte arithmetic | green, AOT smoke green |
| 6 | Svartálfheim | Delete `Primitives.Ingestion` | green |
| 7 | Svartálfheim | README/CLAUDE.md, after-benchmarks, amendment, full verification | PR, tag (human) |
| 8 | Midgard | `FailureDetail.Render` `OutOfRange` arm | green |
| 9 | Ginnungagap + Bifröst | `dotnet workload restore` in the shared CI workflow and the devcontainer; Yggdrasil docs | workflow merged |
| 10 | Mímisbrunnr | `SeedTool` over `DelimitedReader` | green |
| 11 | Mímisbrunnr | `ReferenceDataSeedContributor` async over `DelimitedReader`; project files; docs | green (container tests) |
| 12 | Yggdrasil | CPM pins, host references, WASM publish after-measurement | green, numbers filed |
| 13 | Bifröst, Glitnir | Record sweep; superseded spec marked; branch deletion (human) | — |

---

## Task 1: Baseline — the managed numbers, before anything is deleted

**Files:**
- Create: `Svartalfheim/benchmarks/Primitives.Benchmarks/IdentifierBenchmarks.cs`
- Create: `Glitnir/docs/Svartalfheim/specs/2026-06-11-svartalfheim-pathway-proof-design.md` — new `## 10. Findings amendment (2026-10-xx) — the engines cut, before and after` section (before half)

**Interfaces:**
- Produces: a BenchmarkDotNet class covering the four identifier doors the September record measured, runnable before and after the cut without change.

- [ ] **Step 1: Create the Svartálfheim branch** `feature/hyper-engines-cut` from `master` (inside `Bifrost/Svartalfheim`; Bifröst stays on `master`).

- [ ] **Step 2: Write the identifier rig** (no test — benchmarks are evidence rigs, not src)

```csharp
namespace Norse.Primitives.Benchmarks;

// The four identifier doors the September record measured (superseded spec §1): run once on the
// managed implementation before the cut, once on the engine after, same machine, same config.
[MemoryDiagnoser]
public class IdentifierBenchmarks
{
	static readonly SequentialGuid _rfc = new();
	readonly SequentialGuid[] _batch = new SequentialGuid[1000];

	[Benchmark(Baseline = true)]
	public SequentialGuid GenerateV7() =>
		new();

	[Benchmark]
	public void FillBatch1000() =>
		SequentialGuid.Fill(_batch);

	[Benchmark]
	public DeterministicGuid DeriveV5() =>
		new(DeterministicGuid.Namespaces.Dns, "example.com");

	[Benchmark]
	public SequentialGuid SqlOrderRoundTrip() =>
		_rfc.ToSqlOrder().ToRfcOrder();
}
```

- [ ] **Step 3: Run all three rigs on the managed code**

```shell
cd Svartalfheim
dotnet run -c Release --project benchmarks/Primitives.Benchmarks -- --filter "*DispatchBenchmarks*" "*ParserAllocationBenchmarks*" "*IdentifierBenchmarks*"
```

Keep the three summary tables verbatim (Mean, Error, StdDev, Ratio, Allocated), plus the BDN host line (OS, CPU, runtime).

- [ ] **Step 4: Measure the WebAssembly bundle before** (Yggdrasil, on `master`, dev mode, no workload needed yet)

```shell
cd Yggdrasil
dotnet publish src/Hosting.Web.Client -c Release -o "$NORSE_BUILD_ARTIFACTS_DIR/wasm-before"
du -sb "$NORSE_BUILD_ARTIFACTS_DIR/wasm-before/wwwroot/_framework"
stat -c '%s %n' "$NORSE_BUILD_ARTIFACTS_DIR/wasm-before/wwwroot/_framework/dotnet.native.wasm"
```

(PowerShell: `Get-ChildItem -Recurse … | Measure-Object Length -Sum`.) Record both numbers and the count of files under `_framework`.

- [ ] **Step 5: File the before half** in the pathway spec as `## 10. Findings amendment (2026-10-xx) — the engines cut, before and after`, with `### 10.1 Before (managed, master at <sha>)` holding the three tables and the bundle numbers, and an empty `### 10.2 After` heading that Task 7 and Task 12 fill. Relative paths only.

- [ ] **Step 6: Stage** `IdentifierBenchmarks.cs` (Svartálfheim) and the amendment (Glitnir); show the diff; stop.

---

## Task 2: Engine references, `ParseFailure.OutOfRange`, `Failure` guard

**Files:**
- Modify: `Svartalfheim/src/Primitives/Primitives.csproj`
- Modify: `Svartalfheim/src/Primitives/ParseFailure.cs`
- Modify: `Svartalfheim/src/Primitives/Failure.cs`
- Modify: `Svartalfheim/tests/Primitives.Tests/FailureTests.cs`
- Create: `Svartalfheim/tests/Primitives.Tests/ParseFailureTests.cs`

**Interfaces:**
- Produces: `ParseFailure { Unspecified = 0, Empty = 1, Malformed = 2, OutOfRange = 3, Duplicate = 4 }`; `Failure` accepts `OutOfRange`; `HyperCast` and `HyperUuid` resolve for `Norse.Primitives` and, transitively, its tests.

- [ ] **Step 1: Write the failing tests**

`ParseFailureTests.cs` (new):

```csharp
using HyperCast;

namespace Norse.Primitives.Tests;

public sealed class ParseFailureTests
{
	// Spec §4.1: the four shared members are CastFailure by number, so the engine-to-forge map is a
	// reinterpret. A drift on either side fails here before it can fail in a switch.
	[Theory]
	[InlineData(ParseFailure.Unspecified, CastFailure.Unspecified)]
	[InlineData(ParseFailure.Empty, CastFailure.Empty)]
	[InlineData(ParseFailure.Malformed, CastFailure.Malformed)]
	[InlineData(ParseFailure.OutOfRange, CastFailure.OutOfRange)]
	void Should_mirror_cast_failure_numerically(ParseFailure reason, CastFailure cast) =>
		((byte)reason).ShouldBe((byte)cast);

	[Fact]
	void Should_keep_duplicate_as_the_forge_s_own_member_past_the_shared_four() =>
		((byte)ParseFailure.Duplicate).ShouldBe((byte)4);
}
```

`FailureTests.cs`: add `ParseFailure.OutOfRange` to the existing "accepts a real failure" theory's `InlineData` (the one beside `Should_throw_when_reason_is_not_a_real_failure`), so the guard is proven to admit it.

- [ ] **Step 2: Run** `dotnet test tests/Primitives.Tests -- --filter-class "*.ParseFailureTests" "*.FailureTests"` — fails to compile (`HyperCast` unknown, `OutOfRange` unknown).

- [ ] **Step 3: Add the engine references.** `Primitives.csproj`'s single `<ItemGroup>`, alphabetical:

```xml
	<ItemGroup>
		<PackageReference Include="HyperCast" Version="*" />
		<PackageReference Include="HyperUuid" Version="*" />
		<ProjectReference
			Include="../../gen/Primitives.Analyzers/Primitives.Analyzers.csproj"
			OutputItemType="Analyzer"
			ReferenceOutputAssembly="false" />
	</ItemGroup>
```

The `IncludeGeneratorInPackage` target and its own `<ItemGroup>` are untouched. Do not add the packages to `Primitives.Tests.csproj` — they flow transitively (house rule).

- [ ] **Step 4: Widen `ParseFailure`**

```csharp
public enum ParseFailure : byte
{
	/// <summary>Sentinel CLR default — never produced by any parse path.</summary>
	Unspecified = 0,

	/// <summary>Required input was empty or whitespace.</summary>
	Empty = 1,

	/// <summary>Input was present but not recognizable as the target type.</summary>
	Malformed = 2,

	/// <summary>
	///     Input was well-formed but the value falls outside the target's representable range —
	///     <c>"256"</c> for a <see cref="byte"/>, a timestamp past 9999, a code point past
	///     <see cref="char.MaxValue"/>. Adopted from HyperCast's <c>CastFailure.OutOfRange</c>
	///     verbatim: same name, same number, same meaning.
	/// </summary>
	OutOfRange = 3,

	/// <summary>
	///     Input token was individually valid but repeated where each token may appear only once
	///     — first consumer: flags-enum array parsing, a governed name appearing twice. The forge's
	///     own member, past the four it shares with HyperCast.
	/// </summary>
	Duplicate = 4
}
```

Update the type's `<summary>` to say the first four mirror `CastFailure`.

- [ ] **Step 5: Widen the `Failure` guard** — `if (reason is not (ParseFailure.Empty or ParseFailure.Malformed or ParseFailure.OutOfRange or ParseFailure.Duplicate))`.

- [ ] **Step 6: Run** the two test classes — green. Then `dotnet build Svartalfheim.slnx` — green (nothing else switches exhaustively on `ParseFailure` inside the realm; if the build says otherwise, add the arm and report it).

- [ ] **Step 7: Stage; show the diff; stop.**

---

## Task 3: `Parser` routes through `Cast.Scalar<T>`

**Files:**
- Modify: `Svartalfheim/src/Primitives/Parser.cs`
- Rewrite: `Svartalfheim/tests/Primitives.Tests/ParserTests.cs`

**Interfaces:**
- Unchanged: `Parser.ParseRequired<T>(ReadOnlySpan<char>, IFormatProvider)`, `Parser.ParseOptional<T>(ReadOnlySpan<char>, IFormatProvider)`, `where T : ISpanParsable<T>`, provider non-nullable.
- Internal: `Required<TDoor, T>`, `Optional<TDoor, T>`, `Translate<TDoor>` — the engine edge.

The specialists still exist after this task (Task 4 deletes them); only the gateway stops calling them.

- [ ] **Step 1: Write the failing tests — `ParserTests.cs` rewritten in full.** Every expectation below is HyperCast's corpus answer; the comment names the file. Shape, not the exhaustive grammar:

```csharp
using System.Globalization;
using System.Numerics;

namespace Norse.Primitives.Tests;

/// <summary>
///     The gateway's own law — routing, translation, and the fallthrough — not the engine's grammar,
///     which HyperCast's corpus proves upstream. Each engine expectation here is one corpus vector,
///     named so a corpus change is traceable to the test it moves.
/// </summary>
public sealed class ParserTests
{
	const string AllWhitespace = " \t\r\n\f ";

	static readonly IFormatProvider _invariant = CultureInfo.InvariantCulture;

	// --- routing: one success per door (corpus/<file>.json) ---

	[Fact]
	void Should_route_bool_to_the_engine() =>   // boolean.json "yes"
		Parser.ParseRequired<bool>("yes", _invariant).ShouldBe(new Success<bool>(true));

	[Theory]
	[InlineData("42", 42)]            // integer.json i32 "42"
	[InlineData("  7  ", 7)]          // integer.json i32 " 7 "
	[InlineData("1,234", 1234)]       // integer.json i32 "1,234"
	[InlineData("(1,234)", -1234)]    // integer.json i32 "(1,234)"
	[InlineData("0x2A", 42)]          // integer.json i32 "0x2A"
	void Should_route_int_to_the_engine(string input, int expected) =>
		Parser.ParseRequired<int>(input, _invariant).ShouldBe(new Success<int>(expected));

	[Theory]
	[InlineData("1,234.5", 1234.5)]   // real.json f64 "1,234.5"
	[InlineData("50%", 0.5)]          // real.json f64 "50%"
	void Should_route_double_to_the_engine(string input, double expected) =>
		Parser.ParseRequired<double>(input, _invariant).ShouldBe(new Success<double>(expected));

	[Fact]
	void Should_route_decimal_to_the_engine() =>   // decimal.json "1234.5678"
		Parser.ParseRequired<decimal>("1234.5678", _invariant).ShouldBe(new Success<decimal>(1234.5678m));

	[Theory]
	[InlineData("A", 'A')]            // char.json "A"
	[InlineData("65", 'A')]           // char.json "65"
	[InlineData("U+0041", 'A')]       // char.json "U+0041"
	[InlineData("&#x41;", 'A')]       // char.json "&#x41;"
	[InlineData(" ", ' ')]            // char.json " " — one character, verbatim, before trimming (spec §4.7)
	void Should_route_char_to_the_engine(string input, char expected) =>
		Parser.ParseRequired<char>(input, _invariant).ShouldBe(new Success<char>(expected));

	[Fact]
	void Should_route_guid_to_the_engine() =>   // uuid.json "urn:uuid:…"
		Parser.ParseRequired<Guid>("urn:uuid:01020304-0506-0708-090a-0b0c0d0e0f10", _invariant)
			.ShouldBe(new Success<Guid>(new("01020304-0506-0708-090a-0b0c0d0e0f10")));

	[Fact]
	void Should_route_date_to_the_engine() =>   // date.json "2026-01-02"
		Parser.ParseRequired<DateOnly>("2026-01-02", _invariant).ShouldBe(new Success<DateOnly>(new(2026, 1, 2)));

	[Fact]
	void Should_route_time_to_the_engine() =>   // time.json "15:04:05.123"
		Parser.ParseRequired<TimeOnly>("15:04:05.123", _invariant).ShouldBe(new Success<TimeOnly>(new(15, 4, 5, 123)));

	[Fact]
	void Should_route_timestamp_to_the_engine_normalized_to_utc() =>   // timestamp.json "+05:00"
		Parser.ParseRequired<DateTimeOffset>("2026-01-02T15:04:05+05:00", _invariant)
			.ShouldBe(new Success<DateTimeOffset>(new(2026, 1, 2, 10, 4, 5, TimeSpan.Zero)));

	[Fact]
	void Should_route_datetime_to_the_engine_as_utc_kind()   // timestamp.json "Z"; spec §4.7 DateTime row
	{
		Parser.ParseRequired<DateTime>("2026-01-02T15:04:05Z", _invariant).TryGetValue(out Success<DateTime> success).ShouldBeTrue();
		success.Value.ShouldBe(new DateTime(2026, 1, 2, 15, 4, 5, DateTimeKind.Utc));
		success.Value.Kind.ShouldBe(DateTimeKind.Utc);
	}

	[Fact]
	void Should_route_duration_to_the_engine() =>   // duration.json "P1DT6H"
		Parser.ParseRequired<TimeSpan>("P1DT6H", _invariant).ShouldBe(new Success<TimeSpan>(new(1, 6, 0, 0)));

	// --- translation: every CastFailure arrives as its ParseFailure, with the forge's diagnostics ---

	[Fact]
	void Should_translate_malformed_with_the_trimmed_input_and_the_clr_type_name()
	{
		Parser.ParseRequired<int>("  bogus  ", _invariant).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.ShouldBe(new(ParseFailure.Malformed, "bogus", "Int32"));
	}

	[Theory]
	[InlineData("256")]               // integer.json u8 "256"
	[InlineData("-1")]                // integer.json u8 "-1"
	void Should_translate_out_of_range_for_byte(string input)
	{
		Parser.ParseRequired<byte>(input, _invariant).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.OutOfRange);
		failure.ExpectedType.ShouldBe("Byte");
	}

	[Fact]
	void Should_translate_out_of_range_for_a_code_point_past_the_bmp()   // char.json "😀" → OutOfRange in C#
	{
		Parser.ParseRequired<char>("U+1F600", _invariant).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.OutOfRange);
	}

	[Theory]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_translate_empty_when_required_input_is_blank(string input)
	{
		Parser.ParseRequired<int>(input, _invariant).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.ShouldBe(new(ParseFailure.Empty, string.Empty, "Int32"));
	}

	[Fact]
	void Should_bound_the_echoed_input_to_max_input_length()
	{
		var input = new string('x', Failure.MaxInputLength + 50);
		Parser.ParseRequired<long>(input, _invariant).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Input.Length.ShouldBe(Failure.MaxInputLength);
	}

	[Fact]
	void Should_leave_format_and_detail_null_from_the_engine()
	{
		Parser.ParseRequired<Guid>("nope", _invariant).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Format.ShouldBeNull();
		failure.Detail.ShouldBeNull();
	}

	// --- optional ---

	[Theory]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_return_absent_when_optional_input_is_blank(string input) =>
		Parser.ParseOptional<int>(input, _invariant).ShouldBeNull();

	[Fact]
	void Should_return_the_value_when_optional_input_is_present() =>
		Parser.ParseOptional<bool>("no", _invariant).ShouldBe(new Success<bool>(false));

	[Fact]
	void Should_return_the_failure_when_optional_input_is_malformed()
	{
		var actual = Parser.ParseOptional<Guid>("nope", _invariant);
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	// --- culture: the provider passes straight through to the engine's bridge (spec §4.4) ---

	[Fact]
	void Should_honor_a_declared_culture_for_numeric_doors() =>
		Parser.ParseRequired<decimal>("1.234,5", CultureInfo.GetCultureInfo("de-DE")).ShouldBe(new Success<decimal>(1234.5m));

	[Fact]
	void Should_ignore_the_provider_on_culture_insensitive_doors() =>
		Parser.ParseRequired<DateOnly>("2026-01-02", CultureInfo.GetCultureInfo("de-DE")).ShouldBe(new Success<DateOnly>(new(2026, 1, 2)));

	[Fact]
	void Should_throw_when_provider_is_null() =>
		Should.Throw<ArgumentNullException>(() => Parser.ParseRequired<int>("1", null!));

	// --- the fallthrough: no engine door, ISpanParsable<T> as before ---

	[Fact]
	void Should_fall_through_to_span_parsable_for_string() =>
		Parser.ParseRequired<string>("  text  ", _invariant).ShouldBe(new Success<string>("text"));

	[Fact]
	void Should_fall_through_to_span_parsable_for_int128() =>
		Parser.ParseRequired<Int128>("170141183460469231731687303715884105727", _invariant)
			.ShouldBe(new Success<Int128>(Int128.MaxValue));

	[Fact]
	void Should_fall_through_to_span_parsable_for_big_integer() =>
		Parser.ParseRequired<BigInteger>("12345678901234567890123", _invariant)
			.ShouldBe(new Success<BigInteger>(BigInteger.Parse("12345678901234567890123", CultureInfo.InvariantCulture)));

	[Fact]
	void Should_report_malformed_through_the_fallthrough()
	{
		Parser.ParseRequired<Half>("nope", _invariant).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.ShouldBe(new(ParseFailure.Malformed, "nope", "Half"));
	}

	[Fact]
	void Should_not_leak_boolean_vocabulary_into_int()
	{
		Parser.ParseRequired<int>("yes", _invariant).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}
}
```

(`Should_throw_when_provider_is_null` keeps the one sanctioned `null!` in a test that is *about* null — comment it.) Shouldly compares the union record structs by value; `ShouldBe(new Success<T>(…))` relies on the implicit union conversion of the expected value to `Result<T>`.

- [ ] **Step 2: Run** `dotnet test tests/Primitives.Tests -- --filter-class "*.ParserTests"` — fails on the `char " "`, `OutOfRange`, and `DateTime` expectations at least (the specialists still answer).

- [ ] **Step 3: Rewrite `Parser.cs`**

```csharp
using System.Runtime.CompilerServices;
using HyperCast;

namespace Norse.Primitives;

/// <summary>
///     Generic parse gateway over <see cref="ISpanParsable{TSelf}"/>: the bridge from the span world
///     into <see cref="Result{T}"/> with uniform failure semantics.
/// </summary>
/// <remarks>
///     <para>
///         Every type HyperCast has a door for — <see cref="bool"/>, the eleven numerics,
///         <see cref="char"/>, <see cref="Guid"/>, <see cref="DateOnly"/>, <see cref="TimeOnly"/>,
///         <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="TimeSpan"/> — is routed by a
///         <c>typeof</c> branch resolved at JIT/AOT compile time into <c>Cast.Scalar&lt;T&gt;</c>, the one
///         engine door; the engine's <c>Verdict&lt;T&gt;</c> is translated into <see cref="Result{T}"/> at
///         this edge and never appears in a Norse signature. The table cannot be a single generic call:
///         the engine door is constrained <c>where T : struct</c> and this gateway is constrained on
///         <see cref="ISpanParsable{TSelf}"/>, and C# has no way to narrow one into the other without
///         reflection. Every other type falls through to <c>T.TryParse(span, provider)</c>. There is no
///         runtime registry: a type that cannot parse does not compile.
///     </para>
///     <para>
///         The provider is required and passes straight through to the engine, whose bridge maps a
///         culture's decimal and group separators and currency symbol exactly; the invariant culture, which
///         every live call site declares, maps exactly. The culture-insensitive doors (<see cref="char"/>,
///         <see cref="Guid"/>, the temporal types) ignore it on both sides. The remaining differences
///         between the bridge and <c>NumberFormatInfo</c> (non-ASCII signs, the currency decimal separator,
///         the percent symbol, native digits) are HyperCast's roadmap, not this gateway's.
///     </para>
///     <para>
///         Whitespace: the engine trims on every door but reads a lone <see cref="char"/> before
///         trimming, so the untrimmed span goes to the engine; the failure echo is the trimmed span,
///         bounded by <see cref="Failure"/>.
///     </para>
/// </remarks>
public static class Parser
{
	/// <summary>
	///     Parses required scalar text. Empty or whitespace input is <see cref="ParseFailure.Empty"/>;
	///     unrecognized input is <see cref="ParseFailure.Malformed"/>; a well-formed value the target
	///     cannot hold is <see cref="ParseFailure.OutOfRange"/>.
	/// </summary>
	/// <typeparam name="T">The target type. Non-nullable by construction.</typeparam>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <param name="provider">The declared culture for culture-sensitive types. Never null.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<T> ParseRequired<T>(ReadOnlySpan<char> input, IFormatProvider provider)
		where T : ISpanParsable<T>
	{
		ArgumentNullException.ThrowIfNull(provider);
		if (typeof(T) == typeof(bool))
			return Required<bool, T>(input, provider);
		if (typeof(T) == typeof(byte))
			return Required<byte, T>(input, provider);
		if (typeof(T) == typeof(sbyte))
			return Required<sbyte, T>(input, provider);
		if (typeof(T) == typeof(short))
			return Required<short, T>(input, provider);
		if (typeof(T) == typeof(ushort))
			return Required<ushort, T>(input, provider);
		if (typeof(T) == typeof(int))
			return Required<int, T>(input, provider);
		if (typeof(T) == typeof(uint))
			return Required<uint, T>(input, provider);
		if (typeof(T) == typeof(long))
			return Required<long, T>(input, provider);
		if (typeof(T) == typeof(ulong))
			return Required<ulong, T>(input, provider);
		if (typeof(T) == typeof(float))
			return Required<float, T>(input, provider);
		if (typeof(T) == typeof(double))
			return Required<double, T>(input, provider);
		if (typeof(T) == typeof(decimal))
			return Required<decimal, T>(input, provider);
		if (typeof(T) == typeof(char))
			return Required<char, T>(input, provider);
		if (typeof(T) == typeof(Guid))
			return Required<Guid, T>(input, provider);
		if (typeof(T) == typeof(DateOnly))
			return Required<DateOnly, T>(input, provider);
		if (typeof(T) == typeof(TimeOnly))
			return Required<TimeOnly, T>(input, provider);
		if (typeof(T) == typeof(DateTime))
			return Required<DateTime, T>(input, provider);
		if (typeof(T) == typeof(DateTimeOffset))
			return Required<DateTimeOffset, T>(input, provider);
		if (typeof(T) == typeof(TimeSpan))
			return Required<TimeSpan, T>(input, provider);
		var trimmed = input.Trim();
		return trimmed.IsEmpty ?
			new Failure(ParseFailure.Empty, string.Empty, typeof(T).Name) :
			Parse<T>(trimmed, provider);
	}

	/// <summary>
	///     Parses optional scalar text. Empty or whitespace input is absent (<see langword="null"/>);
	///     otherwise exactly <see cref="ParseRequired{T}"/>.
	/// </summary>
	/// <typeparam name="T">The target type. Non-nullable by construction.</typeparam>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <param name="provider">The declared culture for culture-sensitive types. Never null.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<T>? ParseOptional<T>(ReadOnlySpan<char> input, IFormatProvider provider)
		where T : ISpanParsable<T>
	{
		ArgumentNullException.ThrowIfNull(provider);
		if (typeof(T) == typeof(bool))
			return Optional<bool, T>(input, provider);
		// … the same nineteen branches, each `return Optional<TDoor, T>(input, provider);` …
		var trimmed = input.Trim();
		return trimmed.IsEmpty ?
			null :
			Parse<T>(trimmed, provider);
	}

	// In each JIT-eliminated branch T is statically TDoor; the reinterpret is an identity the type
	// system cannot express (the BCL generic-specialization pattern, benchmark-verified at 1.02×).
	static Result<T> Required<TDoor, T>(ReadOnlySpan<char> input, IFormatProvider provider)
		where TDoor : struct
	{
		var routed = Translate(Cast.Scalar<TDoor>(input, provider), input);
		return Unsafe.As<Result<TDoor>, Result<T>>(ref routed);
	}

	// Nullable<X> layout is a function of X alone, so the identity reinterpret holds for the optional
	// shape too. Cast.Optional is the engine's own absent-mapping: Empty becomes null.
	static Result<T>? Optional<TDoor, T>(ReadOnlySpan<char> input, IFormatProvider provider)
		where TDoor : struct
	{
		Result<TDoor>? routed = Cast.Optional(Cast.Scalar<TDoor>(input, provider)) is { } verdict ?
			Translate(verdict, input) :
			null;
		return Unsafe.As<Result<TDoor>?, Result<T>?>(ref routed);
	}

	// The engine edge: a Verdict is a Result, member for member. HyperCast.Success<T> is qualified
	// because the forge's own Success<T> owns the bare name in this namespace.
	static Result<TDoor> Translate<TDoor>(Verdict<TDoor> verdict, ReadOnlySpan<char> input)
		where TDoor : struct =>
		verdict switch
		{
			HyperCast.Success<TDoor> success => new Success<TDoor>(success.Value),
			Fault fault => new Failure((ParseFailure)fault.Reason, input.Trim(), typeof(TDoor).Name)
		};

	static Result<T> Parse<T>(ReadOnlySpan<char> trimmed, IFormatProvider provider)
		where T : ISpanParsable<T> =>
		T.TryParse(trimmed, provider, out var value) ?
			new Success<T>(value) :
			new Failure(ParseFailure.Malformed, trimmed, typeof(T).Name);
}
```

If the compiler does not accept the two-arm union `switch` as exhaustive over `Verdict<TDoor>` (it should: both are `[Union]` with two case types, and the realm already relies on it for `Result<T>`), use `TryGetValue(out HyperCast.Success<TDoor> success)` with a `Fault` fallback and report the finding — do not add a discard arm that throws.

- [ ] **Step 4: Run** `ParserTests` — green. Run `dotnet test Svartalfheim.slnx` — the specialist test classes still pass (they call the specialists directly). Run the AOT smoke:

```shell
dotnet publish tests/smoke/Primitives.Aot.Smoke -c Release
"$NORSE_BUILD_ARTIFACTS_DIR"/…/Norse.Primitives.Aot.Smoke.exe   # or the publish dir's exe
```

Zero AOT warnings, exit 0. `hypercast.dll`/`hyperuuid.dll` must resolve next to the exe for the CoreCLR run and link in for the AOT publish — if `Tabular`-style `DllNotFoundException` appears, the package's `runtimes/` did not flow; report it, do not hand-copy a binary.

- [ ] **Step 5: Stage; show the diff; stop.**

---

## Task 4: Delete the specialists and the extra doors; rewire `TemporalFusion`; smoke and rigs

**Files:**
- Delete: `Svartalfheim/src/Primitives/{Boolean,Integer,Real,Char,Guid,DateOnly,DateTime,DateTimeOffset,TimeOnly,TimeSpan}Parser.cs`, `UnixPrecision.cs`
- Delete: `Svartalfheim/tests/Primitives.Tests/{Boolean,Integer,Real,Char,Guid,DateOnly,DateTime,DateTimeOffset,TimeOnly,TimeSpan}ParserTests.cs`
- Modify: `Svartalfheim/src/Primitives/TemporalFusion.cs`
- Modify: `Svartalfheim/tests/Primitives.Tests/TemporalFusionTests.cs` (only if it names a deleted type)
- Modify: `Svartalfheim/tests/smoke/Primitives.Aot.Smoke/Program.cs`
- Modify: `Svartalfheim/benchmarks/Primitives.Benchmarks/DispatchBenchmarks.cs`, `ParserAllocationBenchmarks.cs`

- [ ] **Step 1: Write the failing test** — in `TemporalFusionTests`, add one case proving the fusion's date and time steps are now the gateway's doors: `FuseRequired(" 2026-06-15 ", "10:00", "America/Chicago")` fuses (both whitespace-padded date and `HH:mm` time are corpus-accepted; the old `TimeOnlyParser` already took `HH:mm`, so the new evidence is that the result survives the rewire), and `FuseRequired("2026-06-15", "25:00", "America/Chicago")` fails with `ParseFailure.OutOfRange` and `ExpectedType == "TimeOnly"` (time.json `"25:00"` → `out_of_range`). Run — the second fails (`Malformed` today).

- [ ] **Step 2: Rewire `TemporalFusion`** — lines 85 and 91 become `Parser.ParseRequired<DateOnly>(date, CultureInfo.InvariantCulture)` and `Parser.ParseRequired<TimeOnly>(time, CultureInfo.InvariantCulture)` (hoist `using System.Globalization;`); the `<see cref>`s on lines 10–11 point at `Parser.ParseRequired{T}`. Nothing else in the file changes; the zone step and the DST checks are untouched.

- [ ] **Step 3: Delete the eleven source files and the ten test files** listed above. `dotnet build Svartalfheim.slnx` — expect errors in the smoke test and the two benchmark files only.

- [ ] **Step 4: Smoke test** — remove the `"declared unix epoch parses off-gateway"` check (the door is gone); keep every other check; add:

```csharp
Check("gateway translates the engine's out-of-range verdict", () =>
	Parser.ParseRequired<byte>("256", invariant).TryGetValue(out Failure outOfRange)
		&& outOfRange.Reason == ParseFailure.OutOfRange);
```

- [ ] **Step 5: Benchmarks.** `DispatchBenchmarks`:

```csharp
using HyperCast;

namespace Norse.Primitives.Benchmarks;

// The forge's wrapping tax over the engine: a direct engine call against the gateway's routed,
// translated Result<T>. The September record put the tax at ~40 ns; this cut should hold or lower it.
[MemoryDiagnoser]
public class DispatchBenchmarks
{
	const string BoolInput = "yes";
	const string IntInput = "1742";

	static readonly IFormatProvider _invariant = CultureInfo.InvariantCulture;

	[Benchmark(Baseline = true)]
	public Verdict<bool> DirectEngineBool() =>
		Cast.Scalar<bool>(BoolInput, _invariant);

	[Benchmark]
	public Result<bool> GatewayBool() =>
		Parser.ParseRequired<bool>(BoolInput, _invariant);

	[Benchmark]
	public Verdict<int> DirectEngineInt() =>
		Cast.Scalar<int>(IntInput, _invariant);

	[Benchmark]
	public Result<int> GatewayInt() =>
		Parser.ParseRequired<int>(IntInput, _invariant);
}
```

`ParserAllocationBenchmarks`: every row becomes `Parser.ParseRequired<T>(input, _invariant)`; delete the `*Exact` and `*Unix` rows and their constants; keep `MalformedAllocates` (now through the gateway) and add `OutOfRangeAllocates` (`ParseRequired<byte>("256")`) — both honestly non-zero. Update the header comment: the 0 B contract now covers the engine crossing.

- [ ] **Step 6: Verify** — `dotnet build Svartalfheim.slnx`, `dotnet test Svartalfheim.slnx`, AOT smoke publish and run (zero warnings, exit 0), and `dotnet run -c Release --project benchmarks/Primitives.Benchmarks -- --filter "*ParserAllocationBenchmarks*"` once to confirm every success row reads 0 B (the full after-run is Task 7).

- [ ] **Step 7: Stage; show the diff; stop.**

---

## Task 5: Identifiers over `UuidGenerator`

**Files:**
- Rewrite: `Svartalfheim/src/Primitives/Identifiers/SequentialGuid.cs`, `DeterministicGuid.cs`
- Delete: `Svartalfheim/src/Primitives/Identifiers/SequentialGuidBytes.cs`, `GuidVersionBits.cs`
- Delete: `Svartalfheim/tests/Primitives.Tests/Identifiers/SequentialGuidBytesTests.cs`, `GuidVersionBitsTests.cs`
- Modify: `Svartalfheim/tests/Primitives.Tests/Identifiers/SequentialGuidTests.cs`, `SequentialGuidBatchTests.cs`, `DeterministicGuidTests.cs`, `GuidByteOrderTests.cs`

**Interfaces:**
- Unchanged, every public member of both structs, `INorseGuid`, `GuidByteOrder`.

- [ ] **Step 1: Write the failing tests**

`GuidByteOrderTests`: add

```csharp
using HyperUuid;
// …
	// Spec §5: the cast between the forge's byte-order tag and the engine's layout is a reinterpret.
	[Theory]
	[InlineData(GuidByteOrder.Unspecified, UuidLayout.Unspecified)]
	[InlineData(GuidByteOrder.Rfc9562, UuidLayout.Rfc9562)]
	[InlineData(GuidByteOrder.SqlServer, UuidLayout.SqlServer)]
	void Should_mirror_uuid_layout_numerically(GuidByteOrder order, UuidLayout layout) =>
		((uint)order).ShouldBe((uint)layout);
```

`SequentialGuidTests`: replace `GuidVersionBits.HasVersionAndVariant(value.Value, 7)` with `UuidGenerator.IsRfc(value.Value, 7)`; add

```csharp
	[Fact]
	void Should_validate_a_sql_ordered_value_in_place_when_wrapped()
	{
		var sql = new SequentialGuid().ToSqlOrder();
		SequentialGuid wrapped = new(sql.Value, GuidByteOrder.SqlServer);
		wrapped.Timestamp.ShouldBe(sql.Timestamp);
		wrapped.ToRfcOrder().Value.ShouldBe(sql.ToRfcOrder().Value);
	}

	[Fact]
	void Should_match_the_engine_s_permutation_byte_for_byte()
	{
		// HyperUuid corpus/sql_order.json, the vector hand-checked against this realm's retired
		// SequentialGuidBytes.ToSqlOrder on 2026-10-08.
		Guid rfc = new("00000000-0000-7000-8007-010203040506");
		new SequentialGuid(rfc, GuidByteOrder.Rfc9562).ToSqlOrder().Value
			.ShouldBe(new Guid("03040506-0102-0077-8000-000000000000"));
	}
```

`SequentialGuidBatchTests`: the `GuidVersionBits` line becomes `UuidGenerator.IsRfc(…, 7)`; `Should_share_one_timestamp_capture_across_the_batch` stays (the rewrite captures one timestamp for the whole batch); add `Should_fill_more_than_one_chunk_in_order` — `Fill` over 1_000 entries, every `Timestamp` equal, `CompareTo` strictly increasing across the chunk boundary (the chunk is 256, so 1_000 crosses three).

`DeterministicGuidTests`: the `GuidVersionBits` line becomes `UuidGenerator.IsRfc(value.Value, 5)`. Everything else stands — the RFC DNS vector test is the proof the engine derives the same value the managed SHA-1 did.

Run the four classes — compile failure (`UuidLayout`, `UuidGenerator` unknown in tests? No — transitive from `Primitives`; the failures are the new assertions and the deleted-type references). Actually: run before touching src and expect the two new `SequentialGuidTests` facts and the batch fact to fail or not compile.

- [ ] **Step 2: Rewrite `SequentialGuid.cs`** (doc comments carried over, updated where the remark described the managed arithmetic):

```csharp
using System.Data.SqlTypes;
using System.Diagnostics.CodeAnalysis;
using HyperUuid;

namespace Norse.Primitives.Identifiers;

[SuppressMessage("Design", "CA1036:Override methods on comparable types",
	Justification = "Deliberately narrow public surface (design doc §3.1): CompareTo covers in-memory sorting and EF-key comparisons; operator sugar is deferred until a concrete caller needs it.")]
public readonly record struct SequentialGuid : INorseGuid, IComparable<SequentialGuid>
{
	/// <summary>Values the engine fills per native call while a batch is being wrapped: 4 KB of stack.</summary>
	const int FillChunk = 256;

	public Guid Value { get; }

	public GuidByteOrder Order { get; }

	public DateTime Timestamp { get; }

	public SequentialGuid() : this(UuidGenerator.NewV7(), GuidByteOrder.Rfc9562)
	{
	}

	public SequentialGuid(Guid value, GuidByteOrder order)
	{
		if (order == GuidByteOrder.Unspecified)
			throw new ArgumentOutOfRangeException(nameof(order), order,
				"GuidByteOrder.Unspecified is never a valid argument.");
		// Layout-aware: a SQL-ordered value is inspected where its version and variant actually sit.
		if (!UuidGenerator.IsRfc(value, 7, (UuidLayout)order))
			throw new ArgumentException("Value must be a version 7 UUID with RFC 9562 variant bits.", nameof(value));

		Value = value;
		Order = order;
		Timestamp = UuidGenerator.V7Timestamp(value, (UuidLayout)order).UtcDateTime;
	}

	public SequentialGuid ToSqlOrder() =>
		Order switch
		{
			GuidByteOrder.Unspecified => throw new InvalidOperationException(
				"default(SequentialGuid) is malformed by construction -- Order is Unspecified. Only wrap a value this platform already produced via the two-arg constructor, or generate a new one with SequentialGuid()."),
			GuidByteOrder.SqlServer => this,
			_ => new(UuidGenerator.V7ToSqlOrder(Value), GuidByteOrder.SqlServer)
		};

	public SequentialGuid ToRfcOrder() =>
		Order switch
		{
			GuidByteOrder.Unspecified => throw new InvalidOperationException(
				"default(SequentialGuid) is malformed by construction -- Order is Unspecified. Only wrap a value this platform already produced via the two-arg constructor, or generate a new one with SequentialGuid()."),
			GuidByteOrder.Rfc9562 => this,
			_ => new(UuidGenerator.V7FromSqlOrder(Value), GuidByteOrder.Rfc9562)
		};

	// implicit operator Guid, Equals, GetHashCode, CompareTo: unchanged, verbatim.

	public static void Fill(Span<SequentialGuid> destination)
	{
		if (destination.Length > UuidGenerator.MaxV7Batch)
			throw new ArgumentOutOfRangeException(nameof(destination),
				"Batch size must not exceed the 26-bit counter space (67,108,864).");
		if (destination.IsEmpty)
			return;

		// One timestamp for the whole batch, as before; the engine's counter carries across the
		// chunks and rolls the millisecond forward itself if it ever wraps.
		var unixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		Span<Guid> scratch = stackalloc Guid[Math.Min(destination.Length, FillChunk)];
		for (var offset = 0; offset < destination.Length; offset += scratch.Length)
		{
			var chunk = scratch[..Math.Min(scratch.Length, destination.Length - offset)];
			UuidGenerator.FillV7(chunk, unixMilliseconds);
			for (var i = 0; i < chunk.Length; i++)
				destination[offset + i] = new(chunk[i], GuidByteOrder.Rfc9562);
		}
	}

	public static SequentialGuid[] CreateMany(int count)
	{
		switch (count)
		{
			case < 0 or > UuidGenerator.MaxV7Batch:
				throw new ArgumentOutOfRangeException(nameof(count),
					"Count must be between 0 and the 26-bit counter space (67,108,864).");
			case 0:
				return [];
		}

		var result = new SequentialGuid[count];
		Fill(result);
		return result;
	}
}
```

Spec §11 item 1 asked for a measurement between the stack chunk and `ArrayPool<Guid>` above a threshold: the `FillBatch1000` row in Task 7 is that measurement; if a pooled variant beats the chunked stack by more than noise, file it as a finding and leave the chunk (the contract is the same either way).

- [ ] **Step 3: Rewrite `DeterministicGuid.cs`** — the three deriving constructors become `Value = UuidGenerator.NewV5(namespaceId, name);` (string / `ReadOnlySpan<char>` / `ReadOnlySpan<byte>` overloads match one for one); the wrapping constructor's guard becomes `UuidGenerator.IsRfc(value, 5)`; delete `StackThreshold`, `WriteNamespace`, `HashAndFinalize`, the `[SkipLocalsInit]`s, the CA5350 suppression, and the `System.Security.Cryptography`/`System.Text`/`System.Runtime.CompilerServices` usings. `Namespaces`, `Value`, the operator, `Equals`, `GetHashCode`, `CompareTo` stay verbatim.

- [ ] **Step 4: Delete** `SequentialGuidBytes.cs`, `GuidVersionBits.cs`, and their two test files.

- [ ] **Step 5: Verify** — `dotnet test Svartalfheim.slnx` green; AOT smoke publish and run green (the identifier checks exercise the engine's static archive).

- [ ] **Step 6: Stage; show the diff; stop.**

---

## Task 6: Delete `Primitives.Ingestion`

**Files:**
- Delete: `Svartalfheim/src/Primitives.Ingestion/` (whole directory), `Svartalfheim/tests/Primitives.Ingestion.Tests/`, `Svartalfheim/tests/smoke/Primitives.Ingestion.Aot.Smoke/`
- Modify: `Svartalfheim/Svartalfheim.slnx` (remove the three `<Project>` entries)

- [ ] **Step 1: The failing "test" is the build** — remove the three directories and the slnx entries, then `dotnet build Svartalfheim.slnx` and `dotnet test Svartalfheim.slnx`: green. The `Architecture.Analyzers.Tests` cases that name `Norse.Primitives.Ingestion` as a jurisdiction string keep passing — the string is a fixture, not a reference (spec §6.1).

- [ ] **Step 2: Check for stragglers** — `grep -rn "Ingestion" Svartalfheim --include=*.cs --include=*.csproj --include=*.props --include=*.slnx` returns only the two analyzer-test fixtures.

- [ ] **Step 3: Stage (including the deletions); show the diff; stop.**

---

## Task 7: Docs, the after-benchmarks, the amendment, full verification

**Files:**
- Modify: `Svartalfheim/README.md`, `Svartalfheim/src/Primitives/README.md`, `Svartalfheim/CLAUDE.md`
- Modify: `Glitnir/docs/Svartalfheim/specs/2026-06-11-svartalfheim-pathway-proof-design.md` (§10.2 after half, Svartálfheim rows)
- Modify: `Glitnir/docs/Svartalfheim/specs/2026-09-03-hyperuuid-hypercast-ingestion-design.md` (status line only)

- [ ] **Step 1: `README.md`** — "What's forged here": the `ParseFailure` bullet lists `Empty`, `Malformed`, `OutOfRange`, `Duplicate`; the `Parser` bullet says the gateway routes to HyperCast's doors and falls through to `ISpanParsable<T>`; the "Hot-path parsers" bullet is replaced by one bullet naming the engines ("the grammar is HyperCast's, proven by its corpus; the identifiers are HyperUuid's") with links to both repos; the `Identifiers` bullet drops `GuidVersionBits`; the `Primitives.Ingestion` bullet is deleted. Intro sentence: "the hot-path scalar parsers" → "the gateway to the engines that parse them". Evidence-rigs paragraph: add the identifier rig.

- [ ] **Step 2: `src/Primitives/README.md`** — rewrite the package blurb to the same facts (engines named, `GuidVersionBits` gone).

- [ ] **Step 3: `CLAUDE.md`** — "What This Realm Is": two capability families (`src/Primitives`, `gen/`); the `src/Primitives` line names the gateway, the engines, `TimeZoneParser` + `TemporalFusion`, `Identifiers`, `Pii`. Spec index: add this spec's row (`The engines cut | 2026-10-09-hyper-engines-cut-design.md`) and mark the September row "superseded by the engines cut". Architecture Facts: replace the parser-template bullet and the `Parser` bullet with the bullets in spec §8; update the `ParseFailure` bullet; add the `GuidByteOrder ≡ UuidLayout` bullet. Build & Test: AOT smoke is also the native-linking proof; benchmarks compare the gateway to a direct `Cast.Scalar<T>` call. Deferred Increments: unchanged.

- [ ] **Step 4: After-benchmarks** — the Task 1 command, verbatim, same machine, Release. File the three tables under `### 10.2 After (engines, branch at <sha>)` in the pathway spec beside the before tables, with a one-paragraph reading per rig (dispatch tax vs. the ~40 ns of the September record; allocation rows all 0 B on success; the four identifier ratios). Numbers only, no adjectives beyond what the table supports.

- [ ] **Step 5: Mark the September spec** — its `**Status:**` line becomes `Superseded 2026-10-09 by 2026-10-09-hyper-engines-cut-design.md — the seam was never merged; this document is history.` Nothing else in it changes.

- [ ] **Step 6: Full verification** — `dotnet build Svartalfheim.slnx` (zero warnings), `dotnet test Svartalfheim.slnx`, AOT smoke publish + run. Paste the test summary and the smoke output into the task report.

- [ ] **Step 7: Stage; show the diff; stop.** The human opens the PR, merges, and tags. Tasks 8–12 can be developed in dev mode before that; their CI needs the tag.

---

## Task 8: Midgard renders `OutOfRange`

**Files:**
- Modify: `Midgard/src/Infrastructure.Web.Server/Xml/FailureDetail.cs`
- Modify: `Midgard/tests/Infrastructure.Web.Server.Tests/Json/ResultJsonConverterTests.cs` (one out-of-range vector)
- Create: `Midgard/tests/Infrastructure.Web.Server.Tests/Xml/FailureDetailTests.cs`
- Modify: `Midgard/CLAUDE.md` (the `ParseFailure.Duplicate` bullet gains the `OutOfRange` fact)

- [ ] **Step 1: Branch** `feature/hyper-engines-cut` in Midgard. Bifröst in dev mode resolves `Norse.Primitives` from the Svartálfheim branch checkout.

- [ ] **Step 2: Failing tests** — `FailureDetailTests.cs`:

```csharp
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

public sealed class FailureDetailTests
{
	[Fact]
	void Render_out_of_range_names_the_value_and_the_type() =>
		FailureDetail.Render(new(ParseFailure.OutOfRange, "256", "Byte"))
			.ShouldBe("value '256' is out of range for Byte");

	[Fact]
	void Render_malformed_without_detail_is_unchanged() =>
		FailureDetail.Render(new(ParseFailure.Malformed, "x", "Int32"))
			.ShouldBe("cannot parse 'x' as Int32");
}
```

`ResultJsonConverterTests`: one case deserializing `"256"` into `Result<byte>` and asserting the rendered detail equals `FailureDetail.Render(new(ParseFailure.OutOfRange, "256", "Byte"))` (same one-message-source shape the existing enum tests use).

- [ ] **Step 3: The arm** (spec §10 item 3), between `Empty` and `Duplicate`:

```csharp
			ParseFailure.OutOfRange => $"value '{failure.Input}' is out of range for {failure.ExpectedType}",
```

Remarks: one sentence — an `OutOfRange` failure never says "cannot parse"; the text parsed, the value did not fit.

- [ ] **Step 4: Verify** `dotnet test Midgard.slnx` green. Stage; show the diff; stop.

---

## Task 9: Workloads become self-describing — CI, devcontainer, docs

**Files:**
- Modify: `../.github/.github/workflows/ci-build-test.yml` (Ginnungagap, sibling of Bifröst)
- Modify: `.devcontainer/devcontainer.json` (Bifröst root — one of the rare Bifröst-tracked changes; it meets both conditions of CLAUDE.md §7, so it is made on `master`, staged, not branched)
- Modify: `Yggdrasil/CLAUDE.md` (Build & Test) and `../.github/CLAUDE.md` (the workflow's step list, if it carries one)

**Why.** HyperCast's and HyperUuid's consumer targets add a `NativeFileReference` whenever `RuntimeIdentifier == browser-wasm`. That is correct and unavoidable: statically linking a native archive into `dotnet.native.wasm` means relinking the runtime, and the WASM SDK does that at **build**, not only at publish, because a debug run would otherwise reach a runtime with no `cast_*` symbols and throw at the first call. Relinking needs the `wasm-tools` workload. The SDK already fails loudly without it (`NETSDK1147`, naming the workload), so nothing upstream needs gating or softening; what is missing is the workload itself, on every box that builds Yggdrasil's client: the shared CI runner, the devcontainer, and Buvy's own machine (which carries `android`, `ios`, `maccatalyst` and not `wasm-tools` — checked 2026-10-09).

**How.** Not a per-realm `workloads` input. `dotnet workload restore` reads the projects and installs whatever workloads they declare they need — a no-op in every realm today, `wasm-tools` in Yggdrasil the moment the engines land, and whatever a future MAUI head asks for without another workflow edit. The build describes its own toolchain; the environments run one generic command.

- [ ] **Step 1: Branch** `feature/ci-workloads` in Ginnungagap.

- [ ] **Step 2: The shared workflow** — immediately after `Setup .NET` in `ci-build-test.yml`:

```yaml
      # The solution says which workloads it needs (wasm-tools for a Blazor WebAssembly head that
      # statically links a native library; android/ios/maccatalyst for a MAUI head). A realm that needs
      # none restores nothing. Never a per-realm input: the projects are the source of truth.
      - name: Restore .NET workloads
        run: dotnet workload restore
```

`dotnet workload restore` with no argument resolves the single `.slnx` at the repo root, as `dotnet test` already does on the next step. If the runner image ever lacks the manifest for the pinned SDK quality, the step fails naming it — report, do not pin a workload version.

- [ ] **Step 3: The devcontainer** — `.devcontainer/devcontainer.json`: chain `dotnet workload restore ${containerWorkspaceFolder}/Yggdrasil/Yggdrasil.slnx` onto the end of the `dev-cert-trust` entry's `&&` sequence (object-form `postCreateCommand` entries run concurrently; this one writes under `~/.dotnet`, which that entry chowns first — a sibling entry would race it, for the same reason the `git-config` comment gives). Comment above it:

```jsonc
    // Workloads the realms declare — wasm-tools for Yggdrasil's WebAssembly client since the engines
    // cut (Glitnir/docs/Svartalfheim/specs/2026-10-09-hyper-engines-cut-design.md §9). Driven by the
    // projects, so a future MAUI head adds its workloads here without an edit.
```

(`Bifrost.slnx` composes the realms, so a root-level `dotnet workload restore` also works once the submodule pointers advance; the Yggdrasil path is explicit so the first rebuild after this change is right before that.)

- [ ] **Step 4: Docs** — `Yggdrasil/CLAUDE.md` Build & Test: "`dotnet workload restore` once per machine — the WebAssembly client statically links HyperCast and HyperUuid since the engines cut and needs `wasm-tools`; CI and the devcontainer run it for you." `../.github/CLAUDE.md`: the workflow's step list, if present, gains the restore step and its one-line rationale.

- [ ] **Step 5: Prove it** — on this machine, `dotnet workload restore Yggdrasil/Yggdrasil.slnx` is a no-op today and installs `wasm-tools` once Task 12's references land; the CI proof is Task 12's run. Stage Ginnungagap, Bifröst (`.devcontainer/devcontainer.json` only), and the Yggdrasil doc line; show the diffs; stop. The Ginnungagap PR merges first (the reusable workflow is referenced `@master`).


---

## Task 10: `SeedTool` over `DelimitedReader`

**Files:**
- Modify: `Mimisbrunnr/tools/SeedTool/SeedTool.csproj`
- Modify: `Mimisbrunnr/tools/SeedTool/Mappers/UnsdM49Mapper.cs`
- Modify: `Mimisbrunnr/tools/SeedTool/Commands/GenerateUnsdM49Command.cs`
- Modify: `Mimisbrunnr/tests/SeedTool.Tests/UnsdM49MapperTests.cs`, `UnsdM49RealFileTests.cs`

- [ ] **Step 1: Branch** `feature/hyper-engines-cut` in Mímisbrunnr.

- [ ] **Step 2: Failing tests** — in both test files, every `TabularReader.OpenDelimited(path, ';')` becomes `DelimitedReader.Open(path, UnsdM49Mapper.SourceDialect)` and every `TabularReader.OpenDelimited(stream, ';')` becomes `new DelimitedReader(stream, UnsdM49Mapper.SourceDialect)`; `using Norse.Primitives.Ingestion;` becomes `using HyperTabular;`; the `ITabularReader` declared type becomes `var`. Add to `UnsdM49MapperTests`:

```csharp
	[Fact]
	void Map_reports_the_source_line_of_a_malformed_row()
	{
		var path = WriteFixture(_rows[0].Replace("566", "abc"));
		try
		{
			using var reader = DelimitedReader.Open(path, UnsdM49Mapper.SourceDialect);
			Should.Throw<InvalidOperationException>(() => UnsdM49Mapper.Map(reader)).Message.ShouldContain("Row 2");
		}
		finally
		{
			File.Delete(path);
		}
	}
```

(`Row 2`: the header is line 1, as the old hand counter also had it.) Run — compile failure.

- [ ] **Step 3: `SeedTool.csproj`** — the `<NorseRef Include="Primitives.Ingestion">` goes; `<PackageReference Include="HyperTabular" Version="*" />` joins the single `<ItemGroup>`, alphabetically (`HyperTabular`, `Spectre.Console.Cli`, then the `NorseRef`); fold the two item groups into one.

- [ ] **Step 4: `UnsdM49Mapper.Map`** takes an open, unbound `DelimitedReader`:

```csharp
using System.Globalization;
using HyperTabular;
using Norse.Primitives;

namespace Norse.SeedTool.Mappers;

static class UnsdM49Mapper
{
	/// <summary>The UNSD methodology CSV: semicolon-separated, header first, RFC quoting (unused), UTF-8 (a BOM is skipped).</summary>
	internal static readonly Dialect SourceDialect = Dialect.Csv with { Separator = ';' };

	// Output ordinals of the plan bound below, in plan order.
	const int
		RegionCode = 0,
		RegionName = 1,
		SubregionCode = 2,
		SubregionName = 3,
		IntermediateCode = 4,
		IntermediateName = 5,
		CountryName = 6,
		M49 = 7,
		Iso2 = 8,
		Iso3 = 9,
		Ldc = 10,
		Llc = 11,
		Sids = 12;

	static readonly Dictionary<string, int> _levelRank = …;   // unchanged

	public static (IReadOnlyList<RegionRow> Regions, IReadOnlyList<CountryOrAreaRow> Countries) Map(DelimitedReader reader)
	{
		var header = reader.Header ?? throw new InvalidOperationException("The UNSD source declares a header row.");
		reader.Bind(
		[
			Column.Text(header.Ordinal("Region Code")),
			Column.Text(header.Ordinal("Region Name")),
			Column.Text(header.Ordinal("Sub-region Code")),
			Column.Text(header.Ordinal("Sub-region Name")),
			Column.Text(header.Ordinal("Intermediate Region Code")),
			Column.Text(header.Ordinal("Intermediate Region Name")),
			Column.Text(header.Ordinal("Country or Area")),
			Column.Text(header.Ordinal("M49 Code")),
			Column.Text(header.Ordinal("ISO-alpha2 Code")),
			Column.Text(header.Ordinal("ISO-alpha3 Code")),
			Column.Text(header.Ordinal("Least Developed Countries (LDC)")),
			Column.Text(header.Ordinal("Land Locked Developing Countries (LLDC)")),
			Column.Text(header.Ordinal("Small Island Developing States (SIDS)"))
		]);

		Dictionary<string, RegionRow> regions = [];
		List<CountryOrAreaRow> countries = [];

		foreach (var row in reader.Rows())
		{
			var line = row.Line;
			var regionCode = row.GetChars(RegionCode);
			var subregionCode = row.GetChars(SubregionCode);
			var intermediateCode = row.GetChars(IntermediateCode);

			if (!regionCode.IsEmpty)
				AddRegionIfAbsent(regions, regionCode, row.GetChars(RegionName), "Region", null, line, "Region Code");
			// … the three region levels, the parent resolution, and the CountryOrAreaRow exactly as
			// today, with reader[xOrdinal] → row.GetChars(X) and rowNumber → line …
		}
		// ordering and return unchanged
	}

	// AddRegionIfAbsent, ValidateM49Code, ValidateIsoAlpha, AllUpperAscii, ValidateFlag: unchanged,
	// their `int rowNumber` parameter renamed `int line`, their messages still "Row {line}".
}
```

`Header.Ordinal` throws `KeyNotFoundException` naming a missing column — the same loud drift signal the old `Ordinal` gave; no wrapping. `Row` is a `ref struct` view: nothing in the loop stores it (the existing code already copies with `ToString()`).

- [ ] **Step 5: `GenerateUnsdM49Command`** — `using var reader = DelimitedReader.Open(settings.InputFile, UnsdM49Mapper.SourceDialect);` and `using HyperTabular;` in place of the ingestion using.

- [ ] **Step 6: Verify** `dotnet test tests/SeedTool.Tests` — green, including `Map_emits_byte_identical_tsv_output_against_the_committed_seed_files` (the committed TSVs must not change by a byte). Stage; show the diff; stop.

---

## Task 11: `ReferenceDataSeedContributor` reads asynchronously; project files; docs

**Files:**
- Modify: `Mimisbrunnr/src/Reference.Data.EntityFramework.Migrations/Reference.Data.EntityFramework.Migrations.csproj`
- Modify: `Mimisbrunnr/src/Reference.Data.EntityFramework.Migrations/ReferenceDataSeedContributor.cs`
- Modify: `Mimisbrunnr/tests/Reference.Data.EntityFramework.Migrations.Tests/ReferenceDataSeedContributorUnitTests.cs`
- Modify: `Mimisbrunnr/README.md` (chart: `Ingestion` node and its edge go; narrative says HyperTabular), `Mimisbrunnr/CLAUDE.md` (project table row, dependency posture)

- [ ] **Step 1: Failing test** — the seed now observes its token before any I/O:

```csharp
	[Fact]
	async Task SeedAsync_throws_before_any_work_when_already_cancelled()
	{
		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync();
		ReferenceDataSeedContributor contributor = new(null!);   // never touched: the token is checked first — comment it

		await Should.ThrowAsync<OperationCanceledException>(() => contributor.SeedAsync(cancelled.Token));
	}
```

Run — fails today (the synchronous TSV loop runs, then EF throws on the null context or a different exception).

- [ ] **Step 2: `.csproj`** — drop `<NorseRef Include="Primitives.Ingestion">`; add `<PackageReference Include="HyperTabular" Version="*" />` to the single `<ItemGroup>` (alphabetical: `EmbeddedResource`, `NorseRef`×2, `PackageReference`, `ProjectReference` — keep the existing comments).

- [ ] **Step 3: The contributor** — `using HyperTabular;` replaces `using Norse.Primitives.Ingestion;`. `SeedAsync` begins with `cancellationToken.ThrowIfCancellationRequested();`. Each reader:

```csharp
	async Task<Dictionary<string, RegionRow>> SeedRegionsAsync(CancellationToken cancellationToken)
	{
		Dictionary<string, RegionRow> regionsByCode = [];

		await using var reader = await DelimitedReader
			.OpenAsync(OpenSeedStream("region.tsv"), Dialect.Tsv, cancellationToken: cancellationToken)
			.ConfigureAwait(false);
		var header = reader.Header ?? throw new InvalidOperationException("region.tsv declares a header row.");
		reader.Bind(
		[
			Column.Text(header.Ordinal("M49Code")),
			Column.Text(header.Ordinal("Name")),
			Column.Text(header.Ordinal("Level")),
			Column.Text(header.Ordinal("ParentM49Code"))
		]);

		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) is { } batch)
			foreach (var row in batch)
			{
				var m49Code = row.GetChars(0).ToString();
				var level = Enum.Parse<RegionLevel>(row.GetChars(2));
				var parentCode = row.GetChars(3).ToString();
				DeterministicGuid id = new(_namespaceRegion, m49Code);

				regionsByCode[m49Code] = new(id, m49Code, row.GetChars(1).ToString(), level, parentCode.Length == 0 ?
					null :
					parentCode);
			}
		// EF half unchanged
	}
```

`SeedCountriesAsync` the same way over its eight columns; the three flag columns stay `Column.Text` read by `bool.Parse(row.GetChars(i))` as today (a typed `Column.Boolean` is a later refinement, not part of the cut). `GetChars` yields an empty span for an empty cell, which `ToString()` turns into `""` — the old `reader[i].ToString()` contract, so the `Length == 0` parent checks still hold.

- [ ] **Step 4: Docs** — README chart: delete `Ingestion["Primitives.Ingestion"]` and `Migrations --> Ingestion`; narrative line 59: "via Svartálfheim's `Norse.Primitives.Ingestion`" → "via HyperTabular"; line 11: "TSV seeders (nietras Sep)" → "TSV seeders (HyperTabular)". CLAUDE.md: the Migrations row and the dependency-posture sentence lose `Primitives.Ingestion` and gain "SkunkWerkx's `HyperTabular` (header-first, async)".

- [ ] **Step 5: Verify** — `dotnet test Mimisbrunnr.slnx`, which includes the container-backed `ReferenceSeedContributorTests` (Postgres via Testcontainers — Docker must be up): the 248-country load, idempotence, byte-identical ids, and the `View` hydration must all pass unchanged. Stage; show the diff; stop.

---

## Task 12: Yggdrasil — CPM pins, host references, the WebAssembly after-measurement

**Files:**
- Modify: `Yggdrasil/Directory.Packages.props`
- Modify: `Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj`, `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`, `Yggdrasil/src/Hosting.Web.Client/Hosting.Web.Client.csproj`
- Modify: `Yggdrasil/CLAUDE.md` (Build & Test: `wasm-tools`)
- Modify: `Glitnir/docs/Svartalfheim/specs/2026-06-11-svartalfheim-pathway-proof-design.md` (§10.2 bundle rows)

- [ ] **Step 1: `Directory.Packages.props`** — remove `Norse.Primitives.Ingestion`; add after the Scalar line, keeping the file's provenance-block style:

```xml
		<!-- SkunkWerkx: Svartalfheim's casting and identifier engines; Mimisbrunnr's tabular reader -->
		<PackageVersion Include="HyperCast" Version="0.8.0" />
		<PackageVersion Include="HyperTabular" Version="0.8.0" />
		<PackageVersion Include="HyperUuid" Version="0.8.0" />
```

- [ ] **Step 2: Hosts re-declare their transitives' direct dependencies** (the composition-root law), versionless:
  - `Hosting.Migrations.Service.csproj`: `<!-- Pull CPM versions of Norse.Primitives (Svartalfheim) and Reference.Data.EntityFramework.Migrations (Mimisbrunnr) -->` then `HyperCast`, `HyperTabular`, `HyperUuid`.
  - `Hosting.Web.Server.csproj` and `Hosting.Web.Client.csproj`: `<!-- Pull CPM versions of Norse.Primitives (Svartalfheim) -->` then `HyperCast`, `HyperUuid`.
  Alphabetical within the `PackageReference` run of each file's single `<ItemGroup>`.

- [ ] **Step 3: Build and test in dev mode** — `dotnet workload restore Yggdrasil.slnx` once (Task 9's mechanism — it installs `wasm-tools` now that the client reaches a native reference through the engines' targets); `dotnet build Yggdrasil.slnx` (the WASM client now relinks `dotnet.native.wasm` with both archives — expect the Emscripten step and `wasm-opt` to run); `dotnet test Yggdrasil.slnx`.

- [ ] **Step 4: After-measurement** — the Task 1 Step 4 commands with `wasm-after`. File under `### 10.2 After`: `_framework` total, file count, `dotnet.native.wasm` size, side by side with the before numbers, and the delta. No judgment beyond the numbers; Buvy reads them (spec §10 item 4).

- [ ] **Step 5: `CLAUDE.md`** Build & Test note: the WebAssembly client requires the `wasm-tools` workload since the engines cut (why, in one sentence, pointing at the spec). Stage; show the diff; stop. CI for this branch needs Task 9's workflow merged and the Svartálfheim, Midgard, and Mímisbrunnr tags published.

---

## Task 13: The record

**Files:**
- Modify: `README.md` and `CLAUDE.md` (Bifröst, realm table: Mímisbrunnr "TSV seeders (nietras Sep)" → "TSV seeders (HyperTabular)"; Svartálfheim row: "the parsing stack" → "the gateway to the HyperCast/HyperUuid engines")
- Modify: `Glitnir/docs/decomposition.md`, `Glitnir/docs/codenames.md` (same Mímisbrunnr phrase)
- Human: delete `origin/feature/hyperuuid-hypercast-ingestion` in Svartálfheim

- [ ] **Step 1:** Make the four edits; `grep -rn "nietras\|Primitives.Ingestion" README.md CLAUDE.md Glitnir/docs/*.md` returns nothing but history sections.
- [ ] **Step 2:** Stage (Bifröst root files and Glitnir); show the diff; stop. Remind the human that the superseded branch is theirs to delete and that the submodule pointers advance through the usual bot train.

---

## Findings ledger

Append here during execution, per task: anything HyperCast, HyperUuid, or HyperTabular made harder than it should be (an upstream issue candidate), any corpus vector that contradicts a Norse expectation beyond spec §4.7, and any house-rule conflict the plan forced. Compiled at the final review and filed upstream or in Glitnir.

### Entries (2026-10-09, execution)

1. **HyperCast — union `switch` with designations.** A two-arm `switch` expression over `Verdict<T>` with `HyperCast.Success<T> s => …` / `Fault f => …` arms is CS8780 ("a variable may not be declared within … a union matching involving matching against either the instance, or its underlying value"); the forge's own `Result<T>.Match` already avoids it with a `TryGetValue` chain, which `Parser.Translate` now mirrors. Worth a line in HyperCast's C# README beside its `is Success<int> s` examples, since the switch form is the first thing a consumer reaches for.
2. **HyperUuid — corpus `sql` vectors are `ToByteArray()` order.** The hex in `corpus/sql_order.json`'s `sql` field is the native-order byte array of the SQL-ordered `Guid`, not its string form; the first draft of the forge's pin test wrote the string form and failed. The corpus README says so; a consumer-facing example in the C# README (`Convert.ToHexStringLower(guid.ToByteArray())`) would have saved the round trip.
3. **HyperCast — crossing cost on short inputs** (pathway spec §10.2): a roughly constant ~30–40 ns per call over the managed specialists on `int`/`Guid`/`char`/`decimal`, while temporal doors are 2.6×–8.9× faster. One upstream benchmark row — `Cast.Scalar<int>(span, NumFormat.Invariant)` beside `Cast.Scalar<int>(span, (IFormatProvider)CultureInfo.InvariantCulture)` — would say whether the per-call `NumberFormatInfo.GetInstance` + `NumFormat.From` bridge is a share of it worth caching upstream.
4. **HyperCast — fault path cost.** `MalformedAllocates` went 44.6 → 139.9 ns (48 B both sides); the engine's reject path is ~95 ns dearer than the managed one. Not hot, recorded for the upstream profile.
5. **Svartálfheim tests that enshrined specialist behavior**, rewritten per spec §4.7: `TemporalFusionTests` asserted `Failure.Format == "ISO 8601"` (now null from the gateway) and that `0001-01-01` is refused by the date door (the corpus accepts it; the fusion's own sentinel guard is now the line of defense).
6. **Environment:** the AOT publish of `Primitives.Aot.Smoke` cannot run on the authoring machine (no Visual Studio C++ workload; `vswhere` finds no `VC.Tools.x86.x64`); the CoreCLR run of the same program passes every check, including the engine calls. The native-AOT link proof is outstanding until a machine with the workload (or a CI job) runs it.
7. **Semantic change beyond the spec's §4.7 list, now added there:** `2026-02-30T00:00:00Z` is `OutOfRange` on the engine where the managed `DateTimeOffsetParser` said `Malformed` (Midgard `ResultSerializerTests.A_malformed_DateTimeOffset_wire_string_reads_as_a_typed_failure_not_a_throw`, rewritten to the engine's verdict — the law under test, "a typed failure, not a throw", is unchanged). Midgard renders it as `value '…' is out of range for DateTimeOffset`.
8. **WebAssembly native linking is opt-in on the consumer, not automatic (corrects Task 9's "Why").** The .NET 11 WASM SDK evaluates the packages' `NativeFileReference` but links it only when the `wasm-tools` workload is present; when it is absent the publish *succeeds* with a warning ("the native references won't be linked in, because neither $(WasmBuildNative), nor $(RunAOTCompilation) are 'true'") and a runtime with no engine symbols — the after-publish measurement came back byte-identical to the before (`dotnet.native.wasm` 3,149,679 B, same hash), which is how this was caught. `NETSDK1147` does not fire. The fix on this side: `Hosting.Web.Client.csproj` sets `WasmBuildNative=true`, which makes the missing workload a build error and makes `dotnet workload restore` install it (restore installs only what projects declare they need). **Upstream candidate, both HyperCast and HyperUuid:** the consumer targets' "no configuration at all" claim holds only with the workload already installed; setting `<WasmBuildNative Condition="'$(RuntimeIdentifier)' == 'browser-wasm'">true</WasmBuildNative>` in the same targets — or erroring when `WasmNativeWorkloadAvailable` is false — would make the silent-skip state impossible for every consumer, exactly the "strictly worse" trap the targets file's own comment describes.
9. **`dotnet workload restore` on a Visual Studio-managed SDK is not one command.** On the authoring machine the rc.1 band had the `wasm-tools` and Emscripten *manifests* (laid down by Visual Studio) but only the .NET 10 *packs* — so `dotnet workload list` reported `wasm-tools` installed while `WasmNativeWorkloadAvailable` evaluated false. The first `dotnet workload restore` tried to roll the whole band forward to a newer workload set, failed to find the Android manifest MSI in the installer cache ("Failed to retrieve MSI payload from cache … microsoft.net.sdk.android.manifest"), and rolled back, removing the Emscripten manifests it had just added and asking for `dotnet workload repair`; a second restore against the unchanged manifests installed the missing packs (Emscripten 6.0.2, `Microsoft.NETCore.App.Runtime.Mono.browser-wasm 11.0.0-rc.1.26425.128`) and the property went true. Expect the same two-step on any box where Visual Studio owns the SDK; the devcontainer and CI paths (Task 9) have no Visual Studio and did not show it. Nothing for upstream; recorded so the next person does not read the first failure as the end of the road.
10. **HyperCast/HyperUuid — the browser-wasm archives ship under `nativeassets/net10.0/`** and are linked by the packages' `net11.0` targets; the .NET 11 rc.1 toolchain (Emscripten 6.0.2) linked them without complaint. A `net11.0` folder, or a note that the archive is toolchain-portable, would stop a consumer wondering whether a mismatch is being papered over.
11. **HyperUuid — `uuid_variant` is missing from the relinked runtime's exports.** It is declared in the package's `EmccExportedFunction` list and present in `libhyperuuid.a` (`llvm-nm` shows it as `T`), yet the published `dotnet.native.wasm` carries every other declared door (26 `cast_*`, 17 `uuid_*`, both `*_version`) and not this one; the JavaScript glue still names `_uuid_variant`. No Norse call site reaches `Variant`, so nothing here is affected, and the cause was not chased — flagged for upstream to reproduce with a browser call before anyone depends on it.
12. **HyperCast — the archive links even when the managed assembly is trimmed away.** The client has no code path to the `Parser` gateway today, so the linker trimmed `HyperCast.Managed` out of the bundle, but `NativeFileReference` is unconditional and `libhypercast.a` went into `dotnet.native.wasm` anyway (the 26 `cast_*` exports are there with no managed caller). Harmless at 66 KB raw / 2 KB compressed for both archives together, and not a Norse concern; noted because the "pay only for what you call" story upstream might want to tell does not hold across the native/managed split.
