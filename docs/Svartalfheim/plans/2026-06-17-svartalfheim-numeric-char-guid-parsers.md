# Numeric, `char` & `Guid` Parsers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Forge the balance of the BCL scalar-struct parsers — the integer family, the real family, `char`, and `Guid` — each carrying real-world ingestion vocabulary, routed through the existing `Parser` gateway.

**Architecture:** Two generic cores carry the numeric vocabulary once — `IntegerParser` over `where T : IBinaryInteger<T>` and `RealParser` over `where T : IFloatingPoint<T>` (which covers `float`, `double`, and `decimal`). `char` and `Guid` get single-type, provider-free specialists. The `Parser` gateway gains thirteen JIT-eliminated `typeof` branches that reinterpret each specialist's `Result<concrete>` to `Result<T>` via `Unsafe.As`, exactly as the existing `bool` branch does.

**Tech Stack:** .NET 11 (preview), C# `LangVersion=preview`, `System.Numerics` generic-math static virtuals, xUnit v3 + Shouldly on Microsoft.Testing.Platform.

## Global Constraints

Every task's requirements implicitly include this section. Values copied verbatim from the spec (`../specs/2026-06-17-svartalfheim-numeric-char-guid-parsers-design.md`) and the realm CLAUDE.md.

- **Target:** `net11.0`; SDK pinned by `global.json` (`11.0.100-` prerelease); C# `LangVersion=preview`.
- **Warnings are errors** (WarningLevel 9999, EnforceCodeStyleInBuild) — a single warning fails the build. XML docs are **mandatory on every public `src` member** (CS1591 is an error in `src`).
- **Tabs** for indentation. `var` for return assignments only; construction uses explicit type with target-typed `new()`. Omit default accessibility modifiers; types `sealed` where applicable (test classes are `public sealed`).
- **Provider is required and non-nullable** on the numeric specialists (`IntegerParser`, `RealParser`) and the gateway — `ArgumentNullException.ThrowIfNull(provider)`. `CharParser` and `GuidParser` take **no provider** (honest signatures — culture-insensitive).
- **`ParseFailure` is unchanged** — the closed `Unspecified`/`Empty`/`Malformed` set. No new members, no `ParseConstraints`, no `FormatHint`, no validation surface.
- **Failure choreography:** empty/whitespace required input ⇒ `new Failure(ParseFailure.Empty, string.Empty, typeof(T).Name)`; optional ⇒ `null`. Unrecognized ⇒ `new Failure(ParseFailure.Malformed, <trimmed span>, typeof(T).Name)` using the span ctor (bounded capture lives in `Failure`; never pre-truncate). `Format`/`Detail` stay null. `ExpectedType` is the CLR type name (`typeof(T).Name` → `"Int32"`, `"Double"`, `nameof(Char)` → `"Char"`, `nameof(Guid)` → `"Guid"`).
- **US English** everywhere. Test method naming `Should_{behavior}_when_{condition}`; test methods omit access modifiers and return `void`.
- **Culture in tests is explicit:** InvariantCulture's group separator is `,` (so `"1,234"` parses under invariant) but its currency symbol is `¤` — currency tests use `en-US`; German-grouping tests use `de-DE`.
- **No automatic git commits.** Each task ends by staging (`git add`) and showing the diff; **the human commits** after GitHub Desktop review. A suggested house-voice message is provided; do not run `git commit`.
- **Test commands use the MTP filter** (VSTest `--filter` does not work): `dotnet test tests/Primitives.Tests -- --filter-class "*.ClassName"`. Never `dotnet test` a project containing zero tests.

---

### Task 1: `IntegerParser` — the binary-integer family

**Files:**
- Create: `src/Primitives/IntegerParser.cs`
- Test: `tests/Primitives.Tests/IntegerParserTests.cs`

**Interfaces:**
- Consumes: `Result<T>`, `Success<T>`, `Failure`, `ParseFailure` (existing).
- Produces:
  - `IntegerParser.ParseRequired<T>(ReadOnlySpan<char> input, IFormatProvider provider) → Result<T> where T : IBinaryInteger<T>`
  - `IntegerParser.ParseOptional<T>(ReadOnlySpan<char> input, IFormatProvider provider) → Result<T>? where T : IBinaryInteger<T>`

- [ ] **Step 1: Write the failing tests**

Create `tests/Primitives.Tests/IntegerParserTests.cs`:

```csharp
using System.Globalization;

namespace Norse.Primitives.Tests;

public sealed class IntegerParserTests
{
	const string AllWhitespace = " \t\r\n\f ";

	static readonly IFormatProvider _invariant = CultureInfo.InvariantCulture;
	static readonly IFormatProvider _enUs = CultureInfo.GetCultureInfo("en-US");
	static readonly IFormatProvider _deDe = CultureInfo.GetCultureInfo("de-DE");

	[Theory]
	[InlineData("42", 42)]
	[InlineData("  7  ", 7)]
	[InlineData("+13", 13)]
	[InlineData("-13", -13)]
	[InlineData("1,234", 1234)]      // thousands, invariant group separator
	[InlineData("(1,234)", -1234)]   // accounting negative
	[InlineData("1e3", 1000)]        // integral exponent
	[InlineData("0x2A", 42)]         // hex prefix
	[InlineData("&H2A", 42)]         // legacy hex prefix
	[InlineData("0b1010", 10)]       // binary prefix
	void Should_parse_value_when_int_input_is_recognized(string input, int expected)
	{
		var actual = IntegerParser.ParseRequired<int>(input, _invariant);
		actual.TryGetValue(out Success<int> success).ShouldBeTrue();
		success.Value.ShouldBe(expected);
	}

	[Fact]
	void Should_parse_currency_when_provider_declares_the_symbol()
	{
		var actual = IntegerParser.ParseRequired<int>("$1,234", _enUs);
		actual.TryGetValue(out Success<int> success).ShouldBeTrue();
		success.Value.ShouldBe(1234);
	}

	[Fact]
	void Should_honor_declared_grouping_when_provider_is_german()
	{
		var actual = IntegerParser.ParseRequired<int>("1.234", _deDe);
		actual.TryGetValue(out Success<int> success).ShouldBeTrue();
		success.Value.ShouldBe(1234);
	}

	[Fact]
	void Should_read_hex_as_bit_pattern_when_value_overflows_signed_width()
	{
		// 0xFF is the two's-complement bit pattern -1 for sbyte.
		var actual = IntegerParser.ParseRequired<sbyte>("0xFF", _invariant);
		actual.TryGetValue(out Success<sbyte> success).ShouldBeTrue();
		success.Value.ShouldBe((sbyte)-1);
	}

	[Theory]
	[InlineData("12.5")]     // decimal point never allowed on an integer
	[InlineData("1.5e0")]    // non-integral exponent result
	[InlineData("abc")]
	[InlineData("-0x1F")]    // signed hex is not recognized
	void Should_fail_with_malformed_reason_when_int_input_is_unrecognized(string input)
	{
		var actual = IntegerParser.ParseRequired<int>(input, _invariant);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Input.ShouldBe(input.Trim());
		failure.ExpectedType.ShouldBe("Int32");
		failure.Format.ShouldBeNull();
		failure.Detail.ShouldBeNull();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_fail_with_empty_reason_when_required_input_is_absent(string? input)
	{
		var actual = IntegerParser.ParseRequired<int>(input, _invariant);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
		failure.Input.ShouldBe(string.Empty);
		failure.ExpectedType.ShouldBe("Int32");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_return_absent_when_optional_input_is_absent(string? input) =>
		IntegerParser.ParseOptional<int>(input, _invariant).HasValue.ShouldBeFalse();

	[Fact]
	void Should_parse_value_when_optional_input_is_recognized()
	{
		var actual = IntegerParser.ParseOptional<int>("0x2A", _invariant);
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Success<int> success).ShouldBeTrue();
		success.Value.ShouldBe(42);
	}

	[Fact]
	void Should_truncate_captured_input_when_malformed_input_is_oversized()
	{
		var oversized = "z" + new string('9', Failure.MaxInputLength + 44);
		var actual = IntegerParser.ParseRequired<int>(oversized, _invariant);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Input.Length.ShouldBe(Failure.MaxInputLength);
	}

	[Fact]
	void Should_throw_when_required_provider_is_null() =>
		Should.Throw<ArgumentNullException>(() => IntegerParser.ParseRequired<int>("42", null!));

	[Fact]
	void Should_throw_when_optional_provider_is_null() =>
		Should.Throw<ArgumentNullException>(() => IntegerParser.ParseOptional<int>("42", null!));

	[Theory]
	[InlineData("0")]
	[InlineData("255")]
	void Should_parse_byte_within_range(string input)
	{
		IntegerParser.ParseRequired<byte>(input, _invariant)
			.TryGetValue(out Success<byte> success).ShouldBeTrue();
		success.Value.ShouldBe(byte.Parse(input));
	}

	[Theory]
	[InlineData("256")]
	[InlineData("-1")]
	void Should_fail_when_byte_is_out_of_range(string input) =>
		IntegerParser.ParseRequired<byte>(input, _invariant)
			.TryGetValue(out Failure _).ShouldBeTrue();

	[Fact]
	void Should_parse_each_integer_width_at_its_documented_maximum()
	{
		IntegerParser.ParseRequired<sbyte>("127", _invariant).TryGetValue(out Success<sbyte> a).ShouldBeTrue();
		a.Value.ShouldBe(sbyte.MaxValue);
		IntegerParser.ParseRequired<short>("32767", _invariant).TryGetValue(out Success<short> b).ShouldBeTrue();
		b.Value.ShouldBe(short.MaxValue);
		IntegerParser.ParseRequired<ushort>("65535", _invariant).TryGetValue(out Success<ushort> c).ShouldBeTrue();
		c.Value.ShouldBe(ushort.MaxValue);
		IntegerParser.ParseRequired<uint>("4294967295", _invariant).TryGetValue(out Success<uint> d).ShouldBeTrue();
		d.Value.ShouldBe(uint.MaxValue);
		IntegerParser.ParseRequired<long>("9223372036854775807", _invariant).TryGetValue(out Success<long> e).ShouldBeTrue();
		e.Value.ShouldBe(long.MaxValue);
		IntegerParser.ParseRequired<ulong>("18446744073709551615", _invariant).TryGetValue(out Success<ulong> f).ShouldBeTrue();
		f.Value.ShouldBe(ulong.MaxValue);
	}

	[Fact]
	void Should_fail_when_value_overflows_long() =>
		IntegerParser.ParseRequired<long>("99999999999999999999999", _invariant)
			.TryGetValue(out Failure failure).ShouldBeTrue();
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.IntegerParserTests"`
Expected: FAIL — build error, `IntegerParser` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Primitives/IntegerParser.cs`:

```csharp
using System.Globalization;
using System.Numerics;

namespace Norse.Primitives;

/// <summary>
/// Span-based parser for the binary-integer family (<see cref="byte"/> through
/// <see cref="ulong"/>). Extends the bare <see cref="IBinaryInteger{TSelf}"/> parse with the
/// notations untrusted sources actually send: provider-declared thousands grouping and currency,
/// accounting parentheses, exponent form, and culture-insensitive hex (<c>0x</c>/<c>&amp;H</c>)
/// and binary (<c>0b</c>) radix prefixes.
/// </summary>
/// <remarks>
/// Range is the type's own — <c>byte "256"</c> is <see cref="ParseFailure.Malformed"/> for free.
/// A decimal point is never accepted on an integer, so <c>1e3</c> parses to 1000 but
/// <c>1.5e0</c> and <c>1.5e3</c> are malformed. Hex is read as the two's-complement bit pattern,
/// so <c>0xFF</c> is <c>-1</c> for <see cref="sbyte"/>. The provider is required and non-nullable
/// (numeric grouping and currency are culture-sensitive); the radix forms ignore it by nature.
/// </remarks>
public static class IntegerParser
{
	const NumberStyles DecimalStyles =
		NumberStyles.Integer
		| NumberStyles.AllowThousands
		| NumberStyles.AllowParentheses
		| NumberStyles.AllowCurrencySymbol
		| NumberStyles.AllowExponent;

	/// <summary>
	/// Parses required integer text. Empty or whitespace input is a
	/// <see cref="ParseFailure.Empty"/> failure; unrecognized input is
	/// <see cref="ParseFailure.Malformed"/>.
	/// </summary>
	/// <typeparam name="T">The target binary-integer type.</typeparam>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <param name="provider">The declared culture for grouping and currency. Never null.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<T> ParseRequired<T>(ReadOnlySpan<char> input, IFormatProvider provider)
		where T : IBinaryInteger<T>
	{
		ArgumentNullException.ThrowIfNull(provider);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, typeof(T).Name);
		return Parse<T>(trimmed, provider);
	}

	/// <summary>
	/// Parses optional integer text. Empty or whitespace input is absent
	/// (<see langword="null"/>); unrecognized input is <see cref="ParseFailure.Malformed"/>.
	/// </summary>
	/// <typeparam name="T">The target binary-integer type.</typeparam>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <param name="provider">The declared culture for grouping and currency. Never null.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<T>? ParseOptional<T>(ReadOnlySpan<char> input, IFormatProvider provider)
		where T : IBinaryInteger<T>
	{
		ArgumentNullException.ThrowIfNull(provider);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return Parse<T>(trimmed, provider);
	}

	static Result<T> Parse<T>(ReadOnlySpan<char> trimmed, IFormatProvider provider)
		where T : IBinaryInteger<T>
	{
		if (TryRadix(trimmed, out T radix))
			return new Success<T>(radix);
		if (T.TryParse(trimmed, DecimalStyles, provider, out T value))
			return new Success<T>(value);
		return new Failure(ParseFailure.Malformed, trimmed, typeof(T).Name);
	}

	static bool TryRadix<T>(ReadOnlySpan<char> trimmed, out T value) where T : IBinaryInteger<T>
	{
		if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
			|| trimmed.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
			return T.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
		if (trimmed.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
			return T.TryParse(trimmed[2..], NumberStyles.BinaryNumber, CultureInfo.InvariantCulture, out value);
		value = T.Zero;
		return false;
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.IntegerParserTests"`
Expected: PASS (all green).

- [ ] **Step 5: Build the solution clean**

Run: `dotnet build Svartalfheim.slnx`
Expected: Build succeeded, 0 warnings (warnings are errors).

- [ ] **Step 6: Stage and stop for human commit**

```bash
git add src/Primitives/IntegerParser.cs tests/Primitives.Tests/IntegerParserTests.cs
git diff --cached
```
Suggested message (do **not** commit — the human reviews and commits): `Forge IntegerParser: the binary-integer family with radix, grouping, and currency vocabulary`

---

### Task 2: `RealParser` — `float`, `double`, `decimal`

**Files:**
- Create: `src/Primitives/RealParser.cs`
- Test: `tests/Primitives.Tests/RealParserTests.cs`

**Interfaces:**
- Consumes: `Result<T>`, `Success<T>`, `Failure`, `ParseFailure`.
- Produces:
  - `RealParser.ParseRequired<T>(ReadOnlySpan<char> input, IFormatProvider provider) → Result<T> where T : IFloatingPoint<T>`
  - `RealParser.ParseOptional<T>(ReadOnlySpan<char> input, IFormatProvider provider) → Result<T>? where T : IFloatingPoint<T>`

- [ ] **Step 1: Write the failing tests**

Create `tests/Primitives.Tests/RealParserTests.cs`:

```csharp
using System.Globalization;

namespace Norse.Primitives.Tests;

public sealed class RealParserTests
{
	const string AllWhitespace = " \t\r\n\f ";

	static readonly IFormatProvider _invariant = CultureInfo.InvariantCulture;
	static readonly IFormatProvider _enUs = CultureInfo.GetCultureInfo("en-US");
	static readonly IFormatProvider _deDe = CultureInfo.GetCultureInfo("de-DE");

	[Theory]
	[InlineData("1.5", 1.5)]
	[InlineData("  2.25  ", 2.25)]
	[InlineData("-3.5", -3.5)]
	[InlineData("1,234.5", 1234.5)]   // thousands + decimal, invariant
	[InlineData("(2.5)", -2.5)]       // accounting negative
	[InlineData("2.5e3", 2500)]       // scientific
	[InlineData("50%", 0.5)]          // percentage -> divide by 100
	[InlineData("25.5%", 0.255)]
	void Should_parse_value_when_double_input_is_recognized(string input, double expected)
	{
		var actual = RealParser.ParseRequired<double>(input, _invariant);
		actual.TryGetValue(out Success<double> success).ShouldBeTrue();
		success.Value.ShouldBe(expected);
	}

	[Fact]
	void Should_parse_currency_when_provider_declares_the_symbol()
	{
		var actual = RealParser.ParseRequired<decimal>("$1,234.50", _enUs);
		actual.TryGetValue(out Success<decimal> success).ShouldBeTrue();
		success.Value.ShouldBe(1234.50m);
	}

	[Fact]
	void Should_honor_declared_decimal_separator_when_provider_is_german()
	{
		var actual = RealParser.ParseRequired<decimal>("1.234,5", _deDe);
		actual.TryGetValue(out Success<decimal> success).ShouldBeTrue();
		success.Value.ShouldBe(1234.5m);
	}

	[Theory]
	[InlineData("NaN")]
	[InlineData("Infinity")]
	[InlineData("-Infinity")]
	void Should_fail_when_double_input_is_non_finite(string input)
	{
		// The forge admits only finite reals — NaN/±Infinity literals are Malformed.
		var actual = RealParser.ParseRequired<double>(input, _invariant);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("Double");
	}

	[Fact]
	void Should_fail_when_double_overflows_to_infinity()
	{
		// Overflow produces Infinity, which the finite guard rejects — fail loud, no asymmetry.
		var actual = RealParser.ParseRequired<double>("1e400", _invariant);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Should_parse_decimal_at_its_documented_maximum()
	{
		var actual = RealParser.ParseRequired<decimal>("79228162514264337593543950335", _invariant);
		actual.TryGetValue(out Success<decimal> success).ShouldBeTrue();
		success.Value.ShouldBe(decimal.MaxValue);
	}

	[Fact]
	void Should_fail_with_malformed_reason_when_decimal_exceeds_digit_guard()
	{
		// 30 significant digit characters — beyond any in-range decimal; fail loud, not silent zero.
		var actual = RealParser.ParseRequired<decimal>("123456789012345678901234567890", _invariant);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("Decimal");
	}

	[Theory]
	[InlineData("abc")]
	[InlineData("1.2.3")]
	[InlineData("%")]
	void Should_fail_with_malformed_reason_when_double_input_is_unrecognized(string input)
	{
		var actual = RealParser.ParseRequired<double>(input, _invariant);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("Double");
		failure.Format.ShouldBeNull();
		failure.Detail.ShouldBeNull();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_fail_with_empty_reason_when_required_input_is_absent(string? input)
	{
		var actual = RealParser.ParseRequired<double>(input, _invariant);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
		failure.ExpectedType.ShouldBe("Double");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_return_absent_when_optional_input_is_absent(string? input) =>
		RealParser.ParseOptional<double>(input, _invariant).HasValue.ShouldBeFalse();

	[Fact]
	void Should_parse_value_when_optional_input_is_recognized()
	{
		var actual = RealParser.ParseOptional<float>("1.5", _invariant);
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Success<float> success).ShouldBeTrue();
		success.Value.ShouldBe(1.5f);
	}

	[Fact]
	void Should_throw_when_required_provider_is_null() =>
		Should.Throw<ArgumentNullException>(() => RealParser.ParseRequired<double>("1.5", null!));

	[Fact]
	void Should_throw_when_optional_provider_is_null() =>
		Should.Throw<ArgumentNullException>(() => RealParser.ParseOptional<double>("1.5", null!));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.RealParserTests"`
Expected: FAIL — build error, `RealParser` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Primitives/RealParser.cs`:

```csharp
using System.Globalization;
using System.Numerics;

namespace Norse.Primitives;

/// <summary>
/// Span-based parser for the real family (<see cref="float"/>, <see cref="double"/>,
/// <see cref="decimal"/> — every <see cref="IFloatingPoint{TSelf}"/>). Extends the bare parse with
/// provider-declared thousands grouping and currency, accounting parentheses, exponent form, and
/// trailing-percentage notation (<c>50%</c> → <c>0.5</c>).
/// </summary>
/// <remarks>
/// <para>
/// The forge admits only finite reals. <c>NaN</c>, <c>Infinity</c>, and <c>-Infinity</c> are
/// <see cref="ParseFailure.Malformed"/> — whether they arrive as the literal symbol or as the
/// result of magnitude overflow (<c>1e400</c> → <c>Infinity</c> → rejected). The finite check is
/// <see cref="System.Numerics.INumberBase{TSelf}.IsFinite"/>; it is a no-op for
/// <see cref="decimal"/> (which has no non-finite values, and whose overflow already fails
/// <c>TryParse</c>). Overflow fails loud uniformly across all three real types.
/// </para>
/// <para>
/// A <see cref="decimal"/> with more than 29 digit characters is rejected up front: no in-range
/// decimal carries that many significant digits, and the guard turns a silent round-to-zero into a
/// loud failure. The guard is <see cref="decimal"/>-only — the <c>typeof</c> test is eliminated for
/// the IEEE types, which carry far more magnitude. The provider is required and non-nullable.
/// </para>
/// </remarks>
public static class RealParser
{
	const NumberStyles RealStyles =
		NumberStyles.Number
		| NumberStyles.AllowExponent
		| NumberStyles.AllowParentheses
		| NumberStyles.AllowCurrencySymbol;

	const int DecimalDigitGuard = 29;

	/// <summary>
	/// Parses required real text. Empty or whitespace input is a
	/// <see cref="ParseFailure.Empty"/> failure; unrecognized input is
	/// <see cref="ParseFailure.Malformed"/>.
	/// </summary>
	/// <typeparam name="T">The target floating-point type.</typeparam>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <param name="provider">The declared culture for grouping, decimal point, and currency. Never null.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<T> ParseRequired<T>(ReadOnlySpan<char> input, IFormatProvider provider)
		where T : IFloatingPoint<T>
	{
		ArgumentNullException.ThrowIfNull(provider);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, typeof(T).Name);
		return Parse<T>(trimmed, provider);
	}

	/// <summary>
	/// Parses optional real text. Empty or whitespace input is absent
	/// (<see langword="null"/>); unrecognized input is <see cref="ParseFailure.Malformed"/>.
	/// </summary>
	/// <typeparam name="T">The target floating-point type.</typeparam>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <param name="provider">The declared culture for grouping, decimal point, and currency. Never null.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<T>? ParseOptional<T>(ReadOnlySpan<char> input, IFormatProvider provider)
		where T : IFloatingPoint<T>
	{
		ArgumentNullException.ThrowIfNull(provider);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return Parse<T>(trimmed, provider);
	}

	static Result<T> Parse<T>(ReadOnlySpan<char> trimmed, IFormatProvider provider)
		where T : IFloatingPoint<T>
	{
		if (typeof(T) == typeof(decimal) && CountDigits(trimmed) > DecimalDigitGuard)
			return new Failure(ParseFailure.Malformed, trimmed, typeof(T).Name);
		if (trimmed[^1] == '%')
		{
			var body = trimmed[..^1].TrimEnd();
			if (T.TryParse(body, RealStyles, provider, out T percent) && T.IsFinite(percent))
				return new Success<T>(percent / T.CreateChecked(100));
			return new Failure(ParseFailure.Malformed, trimmed, typeof(T).Name);
		}
		if (T.TryParse(trimmed, RealStyles, provider, out T value) && T.IsFinite(value))
			return new Success<T>(value);
		return new Failure(ParseFailure.Malformed, trimmed, typeof(T).Name);
	}

	static int CountDigits(ReadOnlySpan<char> span)
	{
		var count = 0;
		foreach (var character in span)
			if (char.IsAsciiDigit(character))
				count++;
		return count;
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.RealParserTests"`
Expected: PASS.

> Note on the non-finite tests: `T.TryParse` *does* parse `"NaN"`/`"Infinity"` to the non-finite value, and `"1e400"` overflows to `Infinity` — the `&& T.IsFinite(value)` guard is what converts those into `Malformed`. If a non-finite case unexpectedly succeeds, the finite guard is missing or short-circuited, not a styles problem. Do not widen `RealStyles`.

- [ ] **Step 5: Build the solution clean**

Run: `dotnet build Svartalfheim.slnx`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Stage and stop for human commit**

```bash
git add src/Primitives/RealParser.cs tests/Primitives.Tests/RealParserTests.cs
git diff --cached
```
Suggested message: `Forge RealParser: float/double/decimal with grouping, scientific, and percentage vocabulary`

---

### Task 3: `CharParser`

**Files:**
- Create: `src/Primitives/CharParser.cs`
- Test: `tests/Primitives.Tests/CharParserTests.cs`

**Interfaces:**
- Consumes: `Result<T>`, `Success<T>`, `Failure`, `ParseFailure`.
- Produces:
  - `CharParser.ParseRequired(ReadOnlySpan<char> input) → Result<char>`
  - `CharParser.ParseOptional(ReadOnlySpan<char> input) → Result<char>?`

- [ ] **Step 1: Write the failing tests**

Create `tests/Primitives.Tests/CharParserTests.cs`:

```csharp
namespace Norse.Primitives.Tests;

public sealed class CharParserTests
{
	[Theory]
	[InlineData("A", 'A')]
	[InlineData("7", '7')]       // a single char is itself, never code point 7
	[InlineData(" ", ' ')]       // a literal space is preserved, not trimmed away
	[InlineData("\t", '\t')]
	[InlineData("65", 'A')]      // decimal code point
	[InlineData("  65  ", 'A')]  // surrounding whitespace trimmed for the multi-char form
	[InlineData("0x41", 'A')]    // hex code point
	[InlineData("&H41", 'A')]
	[InlineData("U+0041", 'A')]
	[InlineData("&#65;", 'A')]   // HTML entity, decimal
	[InlineData("&#x41;", 'A')]  // HTML entity, hex
	void Should_parse_value_when_char_input_is_recognized(string input, char expected)
	{
		var actual = CharParser.ParseRequired(input);
		actual.TryGetValue(out Success<char> success).ShouldBeTrue();
		success.Value.ShouldBe(expected);
	}

	[Theory]
	[InlineData("70000")]   // beyond the UTF-16 range 0..65535
	[InlineData("-5")]      // negative code point
	[InlineData("AB")]      // two literal chars, no coded form
	[InlineData("0xZZ")]
	[InlineData("&#70000;")]
	void Should_fail_with_malformed_reason_when_char_input_is_unrecognized(string input)
	{
		var actual = CharParser.ParseRequired(input);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("Char");
		failure.Format.ShouldBeNull();
		failure.Detail.ShouldBeNull();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	void Should_fail_with_empty_reason_when_required_input_is_absent(string? input)
	{
		var actual = CharParser.ParseRequired(input);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
		failure.Input.ShouldBe(string.Empty);
		failure.ExpectedType.ShouldBe("Char");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	void Should_return_absent_when_optional_input_is_absent(string? input) =>
		CharParser.ParseOptional(input).HasValue.ShouldBeFalse();

	[Fact]
	void Should_preserve_literal_space_when_optional()
	{
		var actual = CharParser.ParseOptional(" ");
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Success<char> success).ShouldBeTrue();
		success.Value.ShouldBe(' ');
	}

	[Fact]
	void Should_parse_value_when_optional_input_is_recognized()
	{
		var actual = CharParser.ParseOptional("&#65;");
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Success<char> success).ShouldBeTrue();
		success.Value.ShouldBe('A');
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.CharParserTests"`
Expected: FAIL — build error, `CharParser` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Primitives/CharParser.cs`:

```csharp
using System.Globalization;

namespace Norse.Primitives;

/// <summary>
/// Span-based parser for <see cref="char"/>. A single-character input is that character verbatim
/// — whitespace included, never trimmed away — and any longer input is read as a code point in
/// decimal (<c>65</c>), hex/<c>U+</c> (<c>0x41</c>, <c>&amp;H41</c>, <c>U+0041</c>), or HTML-entity
/// form (<c>&amp;#65;</c>, <c>&amp;#x41;</c>). Culture-insensitive — no format provider.
/// </summary>
/// <remarks>
/// The single-character rule has precedence by design: <c>"5"</c> is the literal <c>'5'</c>, never
/// code point 5. Code points are validated to the UTF-16 range 0..65535.
/// </remarks>
public static class CharParser
{
	const string ExpectedType = nameof(Char);

	/// <summary>
	/// Parses required character text. Empty or whitespace input is a
	/// <see cref="ParseFailure.Empty"/> failure; unrecognized input is
	/// <see cref="ParseFailure.Malformed"/>.
	/// </summary>
	/// <param name="input">The raw scalar text. A single character is taken verbatim.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	public static Result<char> ParseRequired(ReadOnlySpan<char> input)
	{
		if (input.Length == 1)
			return new Success<char>(input[0]);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return Parse(trimmed);
	}

	/// <summary>
	/// Parses optional character text. Empty or whitespace input is absent
	/// (<see langword="null"/>); unrecognized input is <see cref="ParseFailure.Malformed"/>.
	/// </summary>
	/// <param name="input">The raw scalar text. A single character is taken verbatim.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	public static Result<char>? ParseOptional(ReadOnlySpan<char> input)
	{
		if (input.Length == 1)
			return new Success<char>(input[0]);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return Parse(trimmed);
	}

	static Result<char> Parse(ReadOnlySpan<char> trimmed)
	{
		if (trimmed.Length == 1)
			return new Success<char>(trimmed[0]);
		if (TryCodePoint(trimmed, out char point))
			return new Success<char>(point);
		if (TryHtmlEntity(trimmed, out char entity))
			return new Success<char>(entity);
		return new Failure(ParseFailure.Malformed, trimmed, ExpectedType);
	}

	static bool TryCodePoint(ReadOnlySpan<char> trimmed, out char value)
	{
		int code;
		if (trimmed.StartsWith("U+", StringComparison.OrdinalIgnoreCase)
			|| trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
			|| trimmed.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
		{
			if (!int.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
			{
				value = '\0';
				return false;
			}
		}
		else if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out code))
		{
			value = '\0';
			return false;
		}
		return InRange(code, out value);
	}

	static bool TryHtmlEntity(ReadOnlySpan<char> trimmed, out char value)
	{
		if (trimmed.Length < 4 || trimmed[0] != '&' || trimmed[1] != '#' || trimmed[^1] != ';')
		{
			value = '\0';
			return false;
		}
		var inner = trimmed[2..^1];
		int code;
		if (inner.StartsWith("x", StringComparison.OrdinalIgnoreCase))
		{
			if (!int.TryParse(inner[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
			{
				value = '\0';
				return false;
			}
		}
		else if (!int.TryParse(inner, NumberStyles.None, CultureInfo.InvariantCulture, out code))
		{
			value = '\0';
			return false;
		}
		return InRange(code, out value);
	}

	static bool InRange(int code, out char value)
	{
		if (code is >= 0 and <= char.MaxValue)
		{
			value = (char)code;
			return true;
		}
		value = '\0';
		return false;
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.CharParserTests"`
Expected: PASS.

- [ ] **Step 5: Build the solution clean**

Run: `dotnet build Svartalfheim.slnx`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Stage and stop for human commit**

```bash
git add src/Primitives/CharParser.cs tests/Primitives.Tests/CharParserTests.cs
git diff --cached
```
Suggested message: `Forge CharParser: literal, code-point, and HTML-entity char vocabulary`

---

### Task 4: `GuidParser`

**Files:**
- Create: `src/Primitives/GuidParser.cs`
- Test: `tests/Primitives.Tests/GuidParserTests.cs`

**Interfaces:**
- Consumes: `Result<T>`, `Success<T>`, `Failure`, `ParseFailure`.
- Produces:
  - `GuidParser.ParseRequired(ReadOnlySpan<char> input) → Result<Guid>`
  - `GuidParser.ParseOptional(ReadOnlySpan<char> input) → Result<Guid>?`

- [ ] **Step 1: Write the failing tests**

Create `tests/Primitives.Tests/GuidParserTests.cs`:

```csharp
namespace Norse.Primitives.Tests;

public sealed class GuidParserTests
{
	const string Known = "01020304-0506-0708-090a-0b0c0d0e0f10";

	static readonly Guid _expected = new(Known);

	[Theory]
	[InlineData("01020304-0506-0708-090a-0b0c0d0e0f10")]              // D
	[InlineData("0102030405060708090a0b0c0d0e0f10")]                  // N
	[InlineData("{01020304-0506-0708-090a-0b0c0d0e0f10}")]            // B
	[InlineData("(01020304-0506-0708-090a-0b0c0d0e0f10)")]            // P
	[InlineData("  01020304-0506-0708-090a-0b0c0d0e0f10  ")]          // surrounding whitespace
	[InlineData("urn:uuid:01020304-0506-0708-090a-0b0c0d0e0f10")]     // URN prefix
	[InlineData("GUID:01020304-0506-0708-090a-0b0c0d0e0f10")]         // GUID: prefix
	[InlineData("uuid:01020304-0506-0708-090a-0b0c0d0e0f10")]         // case-insensitive UUID:
	void Should_parse_value_when_guid_input_is_recognized(string input)
	{
		var actual = GuidParser.ParseRequired(input);
		actual.TryGetValue(out Success<Guid> success).ShouldBeTrue();
		success.Value.ShouldBe(_expected);
	}

	[Theory]
	[InlineData("not-a-guid")]
	[InlineData("GUID:not-a-guid")]
	[InlineData("01020304-0506-0708-090a-0b0c0d0e0f10-extra")]
	void Should_fail_with_malformed_reason_when_guid_input_is_unrecognized(string input)
	{
		var actual = GuidParser.ParseRequired(input);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("Guid");
		failure.Format.ShouldBeNull();
		failure.Detail.ShouldBeNull();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	void Should_fail_with_empty_reason_when_required_input_is_absent(string? input)
	{
		var actual = GuidParser.ParseRequired(input);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
		failure.Input.ShouldBe(string.Empty);
		failure.ExpectedType.ShouldBe("Guid");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	void Should_return_absent_when_optional_input_is_absent(string? input) =>
		GuidParser.ParseOptional(input).HasValue.ShouldBeFalse();

	[Fact]
	void Should_parse_value_when_optional_input_is_recognized()
	{
		var actual = GuidParser.ParseOptional("urn:uuid:" + Known);
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Success<Guid> success).ShouldBeTrue();
		success.Value.ShouldBe(_expected);
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.GuidParserTests"`
Expected: FAIL — build error, `GuidParser` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Primitives/GuidParser.cs`:

```csharp
namespace Norse.Primitives;

/// <summary>
/// Span-based parser for <see cref="Guid"/>. Strips a leading case-insensitive
/// <c>urn:uuid:</c> / <c>GUID:</c> / <c>UUID:</c> prefix, then parses every format
/// <see cref="Guid.TryParse(ReadOnlySpan{char}, out Guid)"/> accepts (N, D, B, P, X).
/// Culture-insensitive — no format provider.
/// </summary>
public static class GuidParser
{
	const string ExpectedType = nameof(Guid);

	static readonly string[] _prefixes = ["urn:uuid:", "GUID:", "UUID:"];

	/// <summary>
	/// Parses required GUID text. Empty or whitespace input is a
	/// <see cref="ParseFailure.Empty"/> failure; unrecognized input is
	/// <see cref="ParseFailure.Malformed"/>.
	/// </summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	public static Result<Guid> ParseRequired(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return Parse(trimmed);
	}

	/// <summary>
	/// Parses optional GUID text. Empty or whitespace input is absent
	/// (<see langword="null"/>); unrecognized input is <see cref="ParseFailure.Malformed"/>.
	/// </summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	public static Result<Guid>? ParseOptional(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return Parse(trimmed);
	}

	static Result<Guid> Parse(ReadOnlySpan<char> trimmed)
	{
		if (Guid.TryParse(StripPrefix(trimmed), out Guid value))
			return new Success<Guid>(value);
		return new Failure(ParseFailure.Malformed, trimmed, ExpectedType);
	}

	static ReadOnlySpan<char> StripPrefix(ReadOnlySpan<char> trimmed)
	{
		foreach (var prefix in _prefixes)
			if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				return trimmed[prefix.Length..].Trim();
		return trimmed;
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.GuidParserTests"`
Expected: PASS.

- [ ] **Step 5: Build the solution clean**

Run: `dotnet build Svartalfheim.slnx`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Stage and stop for human commit**

```bash
git add src/Primitives/GuidParser.cs tests/Primitives.Tests/GuidParserTests.cs
git diff --cached
```
Suggested message: `Forge GuidParser: prefix-stripping over the five BCL Guid formats`

---

### Task 5: Gateway routing — wire all thirteen specialists into `Parser`

**Files:**
- Modify: `src/Primitives/Parser.cs` (add thirteen `typeof` branches to each of `ParseRequired` and `ParseOptional`)
- Test: `tests/Primitives.Tests/ParserTests.cs` (add routing tests; existing tests must stay green)

**Interfaces:**
- Consumes: `IntegerParser`, `RealParser`, `CharParser`, `GuidParser` (Tasks 1–4); `Unsafe.As` (existing `using System.Runtime.CompilerServices;`).
- Produces: no new signatures — `Parser.ParseRequired<T>` / `ParseOptional<T>` now route the integer family, real family, `char`, and `Guid` to their specialists.

- [ ] **Step 1: Write the failing tests**

Add these methods inside the existing `ParserTests` class in `tests/Primitives.Tests/ParserTests.cs` (keep every existing test — they verify the specialists are backward-compatible supersets of the old generic path):

```csharp
	[Fact]
	void Should_route_integer_vocabulary_through_the_gateway()
	{
		Parser.ParseRequired<int>("1,234", _invariant)
			.TryGetValue(out Success<int> thousands).ShouldBeTrue();
		thousands.Value.ShouldBe(1234);
		Parser.ParseRequired<int>("0x2A", _invariant)
			.TryGetValue(out Success<int> hex).ShouldBeTrue();
		hex.Value.ShouldBe(42);
	}

	[Fact]
	void Should_route_real_percentage_through_the_gateway()
	{
		Parser.ParseRequired<double>("50%", _invariant)
			.TryGetValue(out Success<double> success).ShouldBeTrue();
		success.Value.ShouldBe(0.5);
	}

	[Fact]
	void Should_route_char_code_point_through_the_gateway()
	{
		Parser.ParseRequired<char>("65", _invariant)
			.TryGetValue(out Success<char> success).ShouldBeTrue();
		success.Value.ShouldBe('A');
	}

	[Fact]
	void Should_route_guid_prefix_through_the_gateway()
	{
		var expected = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
		Parser.ParseRequired<Guid>("urn:uuid:01020304-0506-0708-090a-0b0c0d0e0f10", _invariant)
			.TryGetValue(out Success<Guid> success).ShouldBeTrue();
		success.Value.ShouldBe(expected);
	}

	[Fact]
	void Should_route_optional_integer_vocabulary_through_the_gateway()
	{
		var actual = Parser.ParseOptional<int>("(7)", _invariant);
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Success<int> success).ShouldBeTrue();
		success.Value.ShouldBe(-7);
	}

	[Fact]
	void Should_require_provider_even_for_culture_insensitive_char() =>
		Should.Throw<ArgumentNullException>(() => Parser.ParseRequired<char>("A", null!));
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.ParserTests"`
Expected: FAIL — the new routing assertions fail (e.g. `"0x2A"` is `Malformed` on the old generic `int` path; `"(7)"` likewise), proving the gateway is not yet wired to the specialists.

- [ ] **Step 3: Write the implementation**

Edit `src/Primitives/Parser.cs`. In `ParseRequired<T>`, immediately **after** the existing `bool` branch and **before** `var trimmed = input.Trim();`, insert the thirteen branches:

```csharp
			if (typeof(T) == typeof(byte))
			{
				var routed = IntegerParser.ParseRequired<byte>(input, provider);
				return Unsafe.As<Result<byte>, Result<T>>(ref routed);
			}
			if (typeof(T) == typeof(sbyte))
			{
				var routed = IntegerParser.ParseRequired<sbyte>(input, provider);
				return Unsafe.As<Result<sbyte>, Result<T>>(ref routed);
			}
			if (typeof(T) == typeof(short))
			{
				var routed = IntegerParser.ParseRequired<short>(input, provider);
				return Unsafe.As<Result<short>, Result<T>>(ref routed);
			}
			if (typeof(T) == typeof(ushort))
			{
				var routed = IntegerParser.ParseRequired<ushort>(input, provider);
				return Unsafe.As<Result<ushort>, Result<T>>(ref routed);
			}
			if (typeof(T) == typeof(int))
			{
				var routed = IntegerParser.ParseRequired<int>(input, provider);
				return Unsafe.As<Result<int>, Result<T>>(ref routed);
			}
			if (typeof(T) == typeof(uint))
			{
				var routed = IntegerParser.ParseRequired<uint>(input, provider);
				return Unsafe.As<Result<uint>, Result<T>>(ref routed);
			}
			if (typeof(T) == typeof(long))
			{
				var routed = IntegerParser.ParseRequired<long>(input, provider);
				return Unsafe.As<Result<long>, Result<T>>(ref routed);
			}
			if (typeof(T) == typeof(ulong))
			{
				var routed = IntegerParser.ParseRequired<ulong>(input, provider);
				return Unsafe.As<Result<ulong>, Result<T>>(ref routed);
			}
			if (typeof(T) == typeof(float))
			{
				var routed = RealParser.ParseRequired<float>(input, provider);
				return Unsafe.As<Result<float>, Result<T>>(ref routed);
			}
			if (typeof(T) == typeof(double))
			{
				var routed = RealParser.ParseRequired<double>(input, provider);
				return Unsafe.As<Result<double>, Result<T>>(ref routed);
			}
			if (typeof(T) == typeof(decimal))
			{
				var routed = RealParser.ParseRequired<decimal>(input, provider);
				return Unsafe.As<Result<decimal>, Result<T>>(ref routed);
			}
			if (typeof(T) == typeof(char))
			{
				var routed = CharParser.ParseRequired(input);
				return Unsafe.As<Result<char>, Result<T>>(ref routed);
			}
			if (typeof(T) == typeof(Guid))
			{
				var routed = GuidParser.ParseRequired(input);
				return Unsafe.As<Result<Guid>, Result<T>>(ref routed);
			}
```

In `ParseOptional<T>`, immediately **after** the existing `bool` branch and **before** `var trimmed = input.Trim();`, insert the nullable-reinterpret counterparts:

```csharp
			if (typeof(T) == typeof(byte))
			{
				var routed = IntegerParser.ParseOptional<byte>(input, provider);
				return Unsafe.As<Result<byte>?, Result<T>?>(ref routed);
			}
			if (typeof(T) == typeof(sbyte))
			{
				var routed = IntegerParser.ParseOptional<sbyte>(input, provider);
				return Unsafe.As<Result<sbyte>?, Result<T>?>(ref routed);
			}
			if (typeof(T) == typeof(short))
			{
				var routed = IntegerParser.ParseOptional<short>(input, provider);
				return Unsafe.As<Result<short>?, Result<T>?>(ref routed);
			}
			if (typeof(T) == typeof(ushort))
			{
				var routed = IntegerParser.ParseOptional<ushort>(input, provider);
				return Unsafe.As<Result<ushort>?, Result<T>?>(ref routed);
			}
			if (typeof(T) == typeof(int))
			{
				var routed = IntegerParser.ParseOptional<int>(input, provider);
				return Unsafe.As<Result<int>?, Result<T>?>(ref routed);
			}
			if (typeof(T) == typeof(uint))
			{
				var routed = IntegerParser.ParseOptional<uint>(input, provider);
				return Unsafe.As<Result<uint>?, Result<T>?>(ref routed);
			}
			if (typeof(T) == typeof(long))
			{
				var routed = IntegerParser.ParseOptional<long>(input, provider);
				return Unsafe.As<Result<long>?, Result<T>?>(ref routed);
			}
			if (typeof(T) == typeof(ulong))
			{
				var routed = IntegerParser.ParseOptional<ulong>(input, provider);
				return Unsafe.As<Result<ulong>?, Result<T>?>(ref routed);
			}
			if (typeof(T) == typeof(float))
			{
				var routed = RealParser.ParseOptional<float>(input, provider);
				return Unsafe.As<Result<float>?, Result<T>?>(ref routed);
			}
			if (typeof(T) == typeof(double))
			{
				var routed = RealParser.ParseOptional<double>(input, provider);
				return Unsafe.As<Result<double>?, Result<T>?>(ref routed);
			}
			if (typeof(T) == typeof(decimal))
			{
				var routed = RealParser.ParseOptional<decimal>(input, provider);
				return Unsafe.As<Result<decimal>?, Result<T>?>(ref routed);
			}
			if (typeof(T) == typeof(char))
			{
				var routed = CharParser.ParseOptional(input);
				return Unsafe.As<Result<char>?, Result<T>?>(ref routed);
			}
			if (typeof(T) == typeof(Guid))
			{
				var routed = GuidParser.ParseOptional(input);
				return Unsafe.As<Result<Guid>?, Result<T>?>(ref routed);
			}
```

Then update the `Parser` XML `<remarks>` so it tells the truth: the routed specialists now include the integer family, the real family, `char`, and `Guid` (each via a JIT-eliminated `typeof` branch; `char` and `Guid` deliberately do not receive the provider — culture-insensitive — exactly as `bool`), and only types without a specialist fall through to the generic `T.TryParse(span, provider)` path.

- [ ] **Step 4: Run the full test project to verify everything passes**

Run: `dotnet test tests/Primitives.Tests`
Expected: PASS — the new routing tests AND every pre-existing test (`ParserTests`, `BooleanParserTests`, `ResultTests`, `FailureTests`, `ResultCombinatorTests`, `ResultLawTests`). Confirm the pre-existing `Should_honor_declared_provider_when_parsing_decimal` and `Should_parse_value_when_guid_rides_the_generic_path` still pass — they now exercise `RealParser`/`GuidParser` and prove backward compatibility.

- [ ] **Step 5: Build the solution clean**

Run: `dotnet build Svartalfheim.slnx`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Stage and stop for human commit**

```bash
git add src/Primitives/Parser.cs tests/Primitives.Tests/ParserTests.cs
git diff --cached
```
Suggested message: `Wire the numeric, char, and Guid specialists into the Parser gateway`

---

### Task 6: AOT smoke — exercise the new pathways under native compilation

**Files:**
- Modify: `tests/smoke/Primitives.Aot.Smoke/Program.cs`

**Interfaces:**
- Consumes: `Parser`, `Result<T>`, `Success<T>`, `Failure` (all existing); the gateway routing from Task 5.

- [ ] **Step 1: Add smoke checks**

In `tests/smoke/Primitives.Aot.Smoke/Program.cs`, after the existing `Check(...)` calls and before the `if (failures > 0)` block, add:

```csharp
Check("gateway routes integer grouping vocabulary", () =>
	Parser.ParseRequired<int>("1,234", invariant) == (Result<int>)new Success<int>(1234));

Check("gateway routes hex integer through generic math", () =>
	Parser.ParseRequired<int>("0x2A", invariant) == (Result<int>)new Success<int>(42));

Check("gateway routes real percentage", () =>
	Parser.ParseRequired<double>("50%", invariant) == (Result<double>)new Success<double>(0.5));

Check("gateway routes char code point", () =>
	Parser.ParseRequired<char>("65", invariant) == (Result<char>)new Success<char>('A'));

Check("gateway routes guid prefix stripping", () =>
	Parser.ParseRequired<Guid>("urn:uuid:01020304-0506-0708-090a-0b0c0d0e0f10", invariant)
		== (Result<Guid>)new Success<Guid>(new Guid("01020304-0506-0708-090a-0b0c0d0e0f10")));
```

- [ ] **Step 2: Publish the AOT smoke and run the native executable**

Run: `dotnet publish tests/smoke/Primitives.Aot.Smoke -c Release`
Expected: publish succeeds with **zero** AOT/trim warnings (generic-math static-virtual dispatch must be AOT-clean).

Then run the produced native executable (path printed by `publish`, under `tests/smoke/Primitives.Aot.Smoke/bin/Release/net11.0/<rid>/publish/`).
Expected: prints `ok` for every check, then `AOT smoke passed: the pathway survives native compilation.`, and exits 0.

> Needs the VS "Desktop development with C++" workload on Windows (per realm CLAUDE.md).

- [ ] **Step 3: Stage and stop for human commit**

```bash
git add tests/smoke/Primitives.Aot.Smoke/Program.cs
git diff --cached
```
Suggested message: `Extend AOT smoke: numeric, char, and Guid specialists survive native compilation`

---

### Task 7 (optional): Benchmark filing — specialist vs bare `TryParse`

Optional per the spec (§6) — evidence, not a gate. Skip unless the owner wants the filing this increment. If done, it ends as a Glitnir amendment to the pathway spec, never a loose note.

**Files:**
- Create: `benchmarks/Primitives.Benchmarks/SpecialistBenchmarks.cs`
- Modify: `benchmarks/Primitives.Benchmarks/Program.cs` (only if the runner enumerates benchmark types explicitly — check first)

- [ ] **Step 1: Add a benchmark comparing `IntegerParser.ParseRequired<int>` against bare `int.TryParse`**

Create `benchmarks/Primitives.Benchmarks/SpecialistBenchmarks.cs`, following the existing `DispatchBenchmarks.cs` structure (same attributes, `InProcessEmitToolchain` is inherited from the shared config — see realm CLAUDE.md). Benchmark `IntegerParser.ParseRequired<int>("1,234", CultureInfo.InvariantCulture)` against `int.TryParse("1,234", NumberStyles.Number, CultureInfo.InvariantCulture, out _)` as the baseline, and `RealParser.ParseRequired<double>("1.5", …)` against `double.TryParse`.

- [ ] **Step 2: Run the benchmarks (Release)**

Run: `dotnet run -c Release --project benchmarks/Primitives.Benchmarks -- --filter *Specialist*`
Expected: completes and reports ratios.

- [ ] **Step 3: File the finding as a spec amendment**

Append the ratio finding to `../specs/2026-06-11-svartalfheim-pathway-proof-design.md` §8 (the findings amendment section), in the established court-filing voice. Stage both the benchmark and the amended spec; stop for the human commit.

---

## Self-Review

**Spec coverage** — every spec section maps to a task:
- §2 in-scope 13 specialists → Tasks 1 (8 ints), 2 (3 reals), 3 (char), 4 (Guid); gateway routing → Task 5.
- §3.1 integer vocabulary (thousands/currency/parens/hex/binary/exponent, range-is-the-type, no decimal point) → Task 1 impl + tests.
- §3.2 real vocabulary (grouping/currency/parens/scientific/percentage, NaN/∞ for IEEE, double-overflow-is-Infinity, decimal digit guard) → Task 2 impl + tests.
- §4.1 char precedence (length-1 literal incl. whitespace, decimal/hex/U+ code point, HTML entity, 0..65535) → Task 3.
- §4.2 Guid prefix stripping over five BCL formats, no separator normalization → Task 4.
- §5 gateway (13 typeof branches, char/Guid provider-free, CLR-name ExpectedType, Format/Detail null) → Task 5.
- §6 tests (matrix per family + gateway routing), AOT smoke, optional benchmark → Tasks 1–4 (matrices), 5 (routing), 6 (AOT), 7 (optional benchmark).
- §8 acceptance criteria 1–5 → covered by Task 5 Step 4 (routing + green suite), Task 5/all (template/provider), build-clean steps, Task 6 (AOT).
- Out-of-scope items (FormatHint, ParseConstraints, ParseErrorType additions, temporal) → no task creates them, by design.

**Placeholder scan:** no TBD/TODO; every code step carries full source; Task 7 is explicitly optional with concrete commands.

**Type consistency:** `ParseRequired`/`ParseOptional` signatures match between each specialist's Produces block, its implementation, and the gateway branch that calls it. `IBinaryInteger<T>`/`IFloatingPoint<T>` constraints are consistent across Tasks 1/2 and the gateway calls. `ExpectedType` strings (`"Int32"`, `"Double"`, `"Decimal"`, `"Char"`, `"Guid"`) match between impl and tests. `Unsafe.As` source/target types match each branch's concrete type.
