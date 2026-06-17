# Svartalfheim Temporal Parsers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Forge the five temporal scalar parsers (`DateOnly`, `DateTime`, `DateTimeOffset`, `TimeOnly`, `TimeSpan`) for `Norse.Primitives`, each with an ISO-canonical door (gateway-routed), a declared-exact-format door, and — for the instant types — a Unix-epoch door, closing the standing §2.6 hole where temporal types fall through the gateway to a flexible culture-buffet parse.

**Architecture:** One static specialist class per type following the established `BooleanParser`/`IntegerParser` template (`ParseRequired`/`ParseOptional` + private `Parse`, `Failure`-based diagnostics, non-throwing on bad input). The gateway speaks exactly one unambiguous machine language — ISO 8601, UTC-normalized; the declared-format and Unix doors are explicit, named, off-gateway calls. Mandating culture-as-provider on the exact door (and fixing the ISO/Unix doors to culture-invariant) deletes the Crucible's `FormatHint`/Auto-mode/ambiguity machinery entirely; only vocabulary (the ISO format lists, `NoCurrentDateDefault`) survives.

**Tech Stack:** .NET 11 preview (SDK pinned by `global.json`, `LangVersion=preview`), C# 15 custom unions, xUnit v3 + Shouldly on Microsoft.Testing.Platform, Native AOT smoke.

**Spec:** `../Glitnir/docs/superpowers/specs/2026-06-17-svartalfheim-temporal-parsers-design.md` (paths relative to the Svartalfheim repo root, where all work happens).

## Global Constraints

- **Stage only — never commit.** The repo law (Svartalfheim CLAUDE.md, top banner) and the platform process forbid Claude committing or pushing. Every task ends with `git add` of the touched files; the human reviews the diff in GitHub Desktop and commits. **Do not run `git commit`, `git push`, or `--no-verify`.** Where this plan's steps say "Stage," they mean `git add` and stop.
- **Warnings are errors.** `dotnet build Svartalfheim.slnx` runs at WarningLevel 9999 with `EnforceCodeStyleInBuild`; a single warning fails the build. XML docs are mandatory on every public `src` member (CS1591 is an error in `src`).
- **Tabs for indentation** (not spaces) in all `.cs` files.
- **`var` for return assignments only;** construction uses an explicit type with target-typed `new()`.
- **Omit default accessibility modifiers;** least accessibility until a caller demands the door open. Specialist classes are `public static`; helpers are private/`static`.
- **No silent fallbacks; fail loud.** Empty required input ⇒ `ParseFailure.Empty`; unrecognized ⇒ `Malformed`; absent optional ⇒ `null`. Bad programmer input (null provider, null/empty format, undefined `UnixPrecision`) throws immediately.
- **US English** in code, comments, and docs.
- **Test conventions:** `public sealed class {Type}ParserTests`, methods named `Should_{behavior}_when_{condition}` with omitted access modifiers; Shouldly/Xunit usings are global (injected by the tests props — never add per-file). `_invariant`/`_enUs`/`_deDe` provider fields per the existing `IntegerParserTests` pattern.
- **VSTest `--filter` does NOT work.** Filter a single class with `dotnet test tests/Primitives.Tests -- --filter-class "*.{ClassName}"`.
- **Run commands from the Svartalfheim repo root.**

---

## File Structure

**New under `src/Primitives/`:**
- `UnixPrecision.cs` — the declared epoch-unit enum (`Seconds`/`Milliseconds`, `Unspecified = 0` sentinel).
- `DateOnlyParser.cs` — ISO `yyyy-MM-dd`, exact door, sentinel guard.
- `TimeOnlyParser.cs` — ISO 24-hour profile, exact door, **no** sentinel guard.
- `DateTimeOffsetParser.cs` — ISO profile (zone mandatory, UTC-normalized), exact door, Unix door, sentinel guard.
- `DateTimeParser.cs` — same as DateTimeOffset but yields `Kind=Utc` `DateTime`; exact door adds `NoCurrentDateDefault`.
- `TimeSpanParser.cs` — colon form + hand-rolled ISO-8601-duration scanner, exact door, sentinel guard.

**Modified:**
- `src/Primitives/Parser.cs` — five `typeof` branches in each of `ParseRequired`/`ParseOptional`, after the `Guid` branch, calling the specialist ISO door **without forwarding the provider** (the ISO door is culture-insensitive, exactly like `char`/`Guid`).

**New under `tests/Primitives.Tests/`:**
- `DateOnlyParserTests.cs`, `TimeOnlyParserTests.cs`, `DateTimeOffsetParserTests.cs`, `DateTimeParserTests.cs`, `TimeSpanParserTests.cs`.

**Modified tests:**
- `tests/Primitives.Tests/ParserTests.cs` — gateway-routing additions for the five temporal types.
- `tests/smoke/Primitives.Aot.Smoke/Program.cs` — temporal `Check(...)` probes.

Each specialist is independently testable and reviewable; the gateway task depends on all five; the AOT task depends on the gateway.

---

### Task 1: `DateOnlyParser`

**Files:**
- Create: `src/Primitives/DateOnlyParser.cs`
- Test: `tests/Primitives.Tests/DateOnlyParserTests.cs`

**Interfaces:**
- Consumes: `Result<T>`, `Success<T>`, `Failure`, `ParseFailure` (existing).
- Produces:
  - `DateOnlyParser.ParseRequired(ReadOnlySpan<char>) → Result<DateOnly>`
  - `DateOnlyParser.ParseOptional(ReadOnlySpan<char>) → Result<DateOnly>?`
  - `DateOnlyParser.ParseExactRequired(ReadOnlySpan<char>, string format, IFormatProvider) → Result<DateOnly>`
  - `DateOnlyParser.ParseExactOptional(ReadOnlySpan<char>, string format, IFormatProvider) → Result<DateOnly>?`

- [ ] **Step 1: Write the failing test**

Create `tests/Primitives.Tests/DateOnlyParserTests.cs`:

```csharp
using System.Globalization;

namespace Norse.Primitives.Tests;

public sealed class DateOnlyParserTests
{
	const string AllWhitespace = " \t\r\n\f ";

	static readonly IFormatProvider _enUs = CultureInfo.GetCultureInfo("en-US");
	static readonly IFormatProvider _enGb = CultureInfo.GetCultureInfo("en-GB");

	[Theory]
	[InlineData("2026-01-02")]
	[InlineData("  2026-01-02  ")]
	void Should_parse_value_when_iso_date_is_recognized(string input)
	{
		var actual = DateOnlyParser.ParseRequired(input);
		actual.TryGetValue(out Success<DateOnly> success).ShouldBeTrue();
		success.Value.ShouldBe(new DateOnly(2026, 1, 2));
	}

	[Theory]
	[InlineData("1/2/2026")]          // US slash — not ISO
	[InlineData("2026-01-02T00:00:00")] // time-bearing — never truncated to the date
	[InlineData("2026/01/02")]
	[InlineData("garbage")]
	void Should_fail_with_malformed_reason_when_iso_date_is_unrecognized(string input)
	{
		var actual = DateOnlyParser.ParseRequired(input);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("DateOnly");
		failure.Format.ShouldBe("ISO 8601");
	}

	[Fact]
	void Should_reject_sentinel_dates_as_malformed()
	{
		DateOnlyParser.ParseRequired("0001-01-01").TryGetValue(out Failure min).ShouldBeTrue();
		min.Reason.ShouldBe(ParseFailure.Malformed);
		DateOnlyParser.ParseRequired("9999-12-31").TryGetValue(out Failure max).ShouldBeTrue();
		max.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Should_honor_declared_format_and_provider_on_the_exact_door()
	{
		DateOnlyParser.ParseExactRequired("1/2/2026", "M/d/yyyy", _enUs)
			.TryGetValue(out Success<DateOnly> us).ShouldBeTrue();
		us.Value.ShouldBe(new DateOnly(2026, 1, 2));
		DateOnlyParser.ParseExactRequired("1/2/2026", "d/M/yyyy", _enGb)
			.TryGetValue(out Success<DateOnly> gb).ShouldBeTrue();
		gb.Value.ShouldBe(new DateOnly(2026, 2, 1));
	}

	[Fact]
	void Should_set_format_to_declared_format_when_exact_input_is_malformed()
	{
		DateOnlyParser.ParseExactRequired("nope", "M/d/yyyy", _enUs)
			.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Format.ShouldBe("M/d/yyyy");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_fail_with_empty_reason_when_required_input_is_absent(string? input)
	{
		var actual = DateOnlyParser.ParseRequired(input);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
		failure.Input.ShouldBe(string.Empty);
		failure.ExpectedType.ShouldBe("DateOnly");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_return_absent_when_optional_input_is_absent(string? input) =>
		DateOnlyParser.ParseOptional(input).HasValue.ShouldBeFalse();

	[Fact]
	void Should_parse_value_when_optional_iso_input_is_recognized()
	{
		var actual = DateOnlyParser.ParseOptional("2026-01-02");
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Success<DateOnly> success).ShouldBeTrue();
		success.Value.ShouldBe(new DateOnly(2026, 1, 2));
	}

	[Fact]
	void Should_throw_when_exact_provider_is_null() =>
		Should.Throw<ArgumentNullException>(() => DateOnlyParser.ParseExactRequired("2026-01-02", "yyyy-MM-dd", null!));

	[Fact]
	void Should_throw_when_exact_format_is_empty() =>
		Should.Throw<ArgumentException>(() => DateOnlyParser.ParseExactRequired("2026-01-02", "", _enUs));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build Svartalfheim.slnx`
Expected: FAIL — compile error, `DateOnlyParser` does not exist (the red).

- [ ] **Step 3: Write minimal implementation**

Create `src/Primitives/DateOnlyParser.cs`:

```csharp
using System.Globalization;

namespace Norse.Primitives;

/// <summary>
/// Span-based parser for <see cref="DateOnly"/>. The ISO door accepts exactly
/// <c>yyyy-MM-dd</c> under <see cref="CultureInfo.InvariantCulture"/>; the exact door accepts a
/// single caller-declared format under a required provider. The sentinel guard rejects
/// <see cref="DateOnly.MinValue"/> and <see cref="DateOnly.MaxValue"/> — neither ever reflects valid
/// state. Culture-insensitive on the ISO door (no provider — ISO 8601 is invariant).
/// </summary>
public static class DateOnlyParser
{
	const string ExpectedType = nameof(DateOnly);
	const string IsoFormat = "yyyy-MM-dd";
	const string IsoLabel = "ISO 8601";

	/// <summary>Parses an ISO <c>yyyy-MM-dd</c> date. Empty ⇒ <see cref="ParseFailure.Empty"/>; unrecognized or sentinel ⇒ <see cref="ParseFailure.Malformed"/>.</summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	public static Result<DateOnly> ParseRequired(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return ParseIso(trimmed);
	}

	/// <summary>Parses an optional ISO date. Empty ⇒ absent (<see langword="null"/>); unrecognized or sentinel ⇒ <see cref="ParseFailure.Malformed"/>.</summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	public static Result<DateOnly>? ParseOptional(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return ParseIso(trimmed);
	}

	/// <summary>Parses a date against a single caller-declared <paramref name="format"/>.</summary>
	/// <param name="input">The raw scalar text.</param>
	/// <param name="format">The exact format. Required, non-empty.</param>
	/// <param name="provider">The declared culture. Never null.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	/// <exception cref="ArgumentException"><paramref name="format"/> is null or empty.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<DateOnly> ParseExactRequired(ReadOnlySpan<char> input, string format, IFormatProvider provider)
	{
		ArgumentException.ThrowIfNullOrEmpty(format);
		ArgumentNullException.ThrowIfNull(provider);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return ParseExact(trimmed, format, provider);
	}

	/// <summary>Parses an optional date against a single caller-declared <paramref name="format"/>.</summary>
	/// <param name="input">The raw scalar text.</param>
	/// <param name="format">The exact format. Required, non-empty.</param>
	/// <param name="provider">The declared culture. Never null.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	/// <exception cref="ArgumentException"><paramref name="format"/> is null or empty.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<DateOnly>? ParseExactOptional(ReadOnlySpan<char> input, string format, IFormatProvider provider)
	{
		ArgumentException.ThrowIfNullOrEmpty(format);
		ArgumentNullException.ThrowIfNull(provider);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return ParseExact(trimmed, format, provider);
	}

	static Result<DateOnly> ParseIso(ReadOnlySpan<char> trimmed)
	{
		if (DateOnly.TryParseExact(trimmed, IsoFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
			&& !IsSentinel(value))
			return new Success<DateOnly>(value);
		return new Failure(ParseFailure.Malformed, trimmed, ExpectedType, IsoLabel);
	}

	static Result<DateOnly> ParseExact(ReadOnlySpan<char> trimmed, string format, IFormatProvider provider)
	{
		if (DateOnly.TryParseExact(trimmed, format, provider, DateTimeStyles.AllowWhiteSpaces, out var value)
			&& !IsSentinel(value))
			return new Success<DateOnly>(value);
		return new Failure(ParseFailure.Malformed, trimmed, ExpectedType, format);
	}

	static bool IsSentinel(DateOnly value) =>
		value == DateOnly.MinValue || value == DateOnly.MaxValue;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Svartalfheim.slnx && dotnet test tests/Primitives.Tests -- --filter-class "*.DateOnlyParserTests"`
Expected: build clean (zero warnings), all `DateOnlyParserTests` pass.

- [ ] **Step 5: Stage (do not commit)**

```bash
git add src/Primitives/DateOnlyParser.cs tests/Primitives.Tests/DateOnlyParserTests.cs
```
Show the diff; the human commits in GitHub Desktop.

---

### Task 2: `TimeOnlyParser`

**Files:**
- Create: `src/Primitives/TimeOnlyParser.cs`
- Test: `tests/Primitives.Tests/TimeOnlyParserTests.cs`

**Interfaces:**
- Produces:
  - `TimeOnlyParser.ParseRequired(ReadOnlySpan<char>) → Result<TimeOnly>`
  - `TimeOnlyParser.ParseOptional(ReadOnlySpan<char>) → Result<TimeOnly>?`
  - `TimeOnlyParser.ParseExactRequired(ReadOnlySpan<char>, string, IFormatProvider) → Result<TimeOnly>`
  - `TimeOnlyParser.ParseExactOptional(ReadOnlySpan<char>, string, IFormatProvider) → Result<TimeOnly>?`

- [ ] **Step 1: Write the failing test**

Create `tests/Primitives.Tests/TimeOnlyParserTests.cs`:

```csharp
using System.Globalization;

namespace Norse.Primitives.Tests;

public sealed class TimeOnlyParserTests
{
	const string AllWhitespace = " \t\r\n\f ";

	static readonly IFormatProvider _invariant = CultureInfo.InvariantCulture;

	[Theory]
	[InlineData("15:04:05", 15, 4, 5, 0)]
	[InlineData("15:04:05.123", 15, 4, 5, 123)]
	[InlineData("15:04", 15, 4, 0, 0)]
	void Should_parse_value_when_iso_time_is_recognized(string input, int h, int m, int s, int ms)
	{
		var actual = TimeOnlyParser.ParseRequired(input);
		actual.TryGetValue(out Success<TimeOnly> success).ShouldBeTrue();
		success.Value.ShouldBe(new TimeOnly(h, m, s, ms));
	}

	[Fact]
	void Should_accept_midnight_and_last_tick_as_valid_clock_readings()
	{
		TimeOnlyParser.ParseRequired("00:00:00").TryGetValue(out Success<TimeOnly> midnight).ShouldBeTrue();
		midnight.Value.ShouldBe(TimeOnly.MinValue);
		TimeOnlyParser.ParseRequired("23:59:59.9999999").TryGetValue(out Success<TimeOnly> lastTick).ShouldBeTrue();
		lastTick.Value.ShouldBe(TimeOnly.MaxValue);
	}

	[Theory]
	[InlineData("3:04:05 PM")]   // 12-hour is a declared-format concern, not ISO
	[InlineData("25:00")]
	[InlineData("noon")]
	void Should_fail_with_malformed_reason_when_iso_time_is_unrecognized(string input)
	{
		var actual = TimeOnlyParser.ParseRequired(input);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("TimeOnly");
		failure.Format.ShouldBe("ISO 8601");
	}

	[Fact]
	void Should_honor_declared_12_hour_format_on_the_exact_door()
	{
		TimeOnlyParser.ParseExactRequired("3:04:05 PM", "h:mm:ss tt", _invariant)
			.TryGetValue(out Success<TimeOnly> success).ShouldBeTrue();
		success.Value.ShouldBe(new TimeOnly(15, 4, 5));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_fail_with_empty_reason_when_required_input_is_absent(string? input)
	{
		var actual = TimeOnlyParser.ParseRequired(input);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
		failure.ExpectedType.ShouldBe("TimeOnly");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_return_absent_when_optional_input_is_absent(string? input) =>
		TimeOnlyParser.ParseOptional(input).HasValue.ShouldBeFalse();

	[Fact]
	void Should_throw_when_exact_format_is_empty() =>
		Should.Throw<ArgumentException>(() => TimeOnlyParser.ParseExactRequired("15:04", "", _invariant));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build Svartalfheim.slnx`
Expected: FAIL — `TimeOnlyParser` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Primitives/TimeOnlyParser.cs`:

```csharp
using System.Globalization;

namespace Norse.Primitives;

/// <summary>
/// Span-based parser for <see cref="TimeOnly"/>. The ISO door accepts the 24-hour profile
/// <c>HH:mm:ss[.fffffff]</c> and <c>HH:mm</c> under <see cref="CultureInfo.InvariantCulture"/>; the
/// exact door accepts a single caller-declared format (e.g. 12-hour <c>h:mm:ss tt</c>) under a
/// required provider. No sentinel guard — <see cref="TimeOnly.MinValue"/> (midnight) and
/// <see cref="TimeOnly.MaxValue"/> are real clock readings. Culture-insensitive on the ISO door.
/// </summary>
public static class TimeOnlyParser
{
	const string ExpectedType = nameof(TimeOnly);
	const string IsoLabel = "ISO 8601";

	static readonly string[] _isoFormats = ["HH:mm:ss.FFFFFFF", "HH:mm:ss", "HH:mm"];

	/// <summary>Parses an ISO 24-hour time. Empty ⇒ <see cref="ParseFailure.Empty"/>; unrecognized ⇒ <see cref="ParseFailure.Malformed"/>.</summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	public static Result<TimeOnly> ParseRequired(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return ParseIso(trimmed);
	}

	/// <summary>Parses an optional ISO time. Empty ⇒ absent; unrecognized ⇒ <see cref="ParseFailure.Malformed"/>.</summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	public static Result<TimeOnly>? ParseOptional(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return ParseIso(trimmed);
	}

	/// <summary>Parses a time against a single caller-declared <paramref name="format"/>.</summary>
	/// <param name="input">The raw scalar text.</param>
	/// <param name="format">The exact format. Required, non-empty.</param>
	/// <param name="provider">The declared culture. Never null.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	/// <exception cref="ArgumentException"><paramref name="format"/> is null or empty.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<TimeOnly> ParseExactRequired(ReadOnlySpan<char> input, string format, IFormatProvider provider)
	{
		ArgumentException.ThrowIfNullOrEmpty(format);
		ArgumentNullException.ThrowIfNull(provider);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return ParseExact(trimmed, format, provider);
	}

	/// <summary>Parses an optional time against a single caller-declared <paramref name="format"/>.</summary>
	/// <param name="input">The raw scalar text.</param>
	/// <param name="format">The exact format. Required, non-empty.</param>
	/// <param name="provider">The declared culture. Never null.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	/// <exception cref="ArgumentException"><paramref name="format"/> is null or empty.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<TimeOnly>? ParseExactOptional(ReadOnlySpan<char> input, string format, IFormatProvider provider)
	{
		ArgumentException.ThrowIfNullOrEmpty(format);
		ArgumentNullException.ThrowIfNull(provider);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return ParseExact(trimmed, format, provider);
	}

	static Result<TimeOnly> ParseIso(ReadOnlySpan<char> trimmed)
	{
		if (TimeOnly.TryParseExact(trimmed, _isoFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
			return new Success<TimeOnly>(value);
		return new Failure(ParseFailure.Malformed, trimmed, ExpectedType, IsoLabel);
	}

	static Result<TimeOnly> ParseExact(ReadOnlySpan<char> trimmed, string format, IFormatProvider provider)
	{
		if (TimeOnly.TryParseExact(trimmed, format, provider, DateTimeStyles.AllowWhiteSpaces, out var value))
			return new Success<TimeOnly>(value);
		return new Failure(ParseFailure.Malformed, trimmed, ExpectedType, format);
	}
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Svartalfheim.slnx && dotnet test tests/Primitives.Tests -- --filter-class "*.TimeOnlyParserTests"`
Expected: build clean, all pass.

- [ ] **Step 5: Stage (do not commit)**

```bash
git add src/Primitives/TimeOnlyParser.cs tests/Primitives.Tests/TimeOnlyParserTests.cs
```

---

### Task 3: `UnixPrecision` + `DateTimeOffsetParser`

This task introduces the shared `UnixPrecision` enum (consumed by Task 4 too) and the first instant parser.

**Files:**
- Create: `src/Primitives/UnixPrecision.cs`
- Create: `src/Primitives/DateTimeOffsetParser.cs`
- Test: `tests/Primitives.Tests/DateTimeOffsetParserTests.cs`

**Interfaces:**
- Produces:
  - `enum UnixPrecision { Unspecified = 0, Seconds = 1, Milliseconds = 2 }`
  - `DateTimeOffsetParser.ParseRequired(ReadOnlySpan<char>) → Result<DateTimeOffset>`
  - `DateTimeOffsetParser.ParseOptional(ReadOnlySpan<char>) → Result<DateTimeOffset>?`
  - `DateTimeOffsetParser.ParseExactRequired(ReadOnlySpan<char>, string, IFormatProvider) → Result<DateTimeOffset>`
  - `DateTimeOffsetParser.ParseExactOptional(ReadOnlySpan<char>, string, IFormatProvider) → Result<DateTimeOffset>?`
  - `DateTimeOffsetParser.ParseUnix(ReadOnlySpan<char>, UnixPrecision) → Result<DateTimeOffset>`
  - `DateTimeOffsetParser.ParseUnixOptional(ReadOnlySpan<char>, UnixPrecision) → Result<DateTimeOffset>?`

- [ ] **Step 1: Write the failing test**

Create `tests/Primitives.Tests/DateTimeOffsetParserTests.cs`:

```csharp
using System.Globalization;

namespace Norse.Primitives.Tests;

public sealed class DateTimeOffsetParserTests
{
	const string AllWhitespace = " \t\r\n\f ";

	static readonly IFormatProvider _invariant = CultureInfo.InvariantCulture;

	[Theory]
	[InlineData("2026-01-02T15:04:05Z")]
	[InlineData("2026-01-02T15:04:05.123Z")]
	void Should_parse_utc_zone_to_zero_offset(string input)
	{
		var actual = DateTimeOffsetParser.ParseRequired(input);
		actual.TryGetValue(out Success<DateTimeOffset> success).ShouldBeTrue();
		success.Value.Offset.ShouldBe(TimeSpan.Zero);
		success.Value.Hour.ShouldBe(15);
	}

	[Fact]
	void Should_normalize_explicit_offset_to_utc()
	{
		// 15:04:05+05:00 is 10:04:05Z
		DateTimeOffsetParser.ParseRequired("2026-01-02T15:04:05+05:00")
			.TryGetValue(out Success<DateTimeOffset> success).ShouldBeTrue();
		success.Value.Offset.ShouldBe(TimeSpan.Zero);
		success.Value.Hour.ShouldBe(10);
	}

	[Theory]
	[InlineData("2026-01-02T15:04:05")]      // zone-less — ambiguous instant, rejected
	[InlineData("2026-01-02 15:04:05Z")]     // space separator — not ISO
	[InlineData("1/2/2026 3:04 PM")]
	void Should_fail_with_malformed_reason_when_iso_is_unrecognized_or_zoneless(string input)
	{
		var actual = DateTimeOffsetParser.ParseRequired(input);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("DateTimeOffset");
		failure.Format.ShouldBe("ISO 8601");
	}

	[Fact]
	void Should_reject_sentinel_instants_as_malformed()
	{
		DateTimeOffsetParser.ParseRequired("0001-01-01T00:00:00Z").TryGetValue(out Failure min).ShouldBeTrue();
		min.Reason.ShouldBe(ParseFailure.Malformed);
		DateTimeOffsetParser.ParseRequired("9999-12-31T23:59:59.9999999Z").TryGetValue(out Failure max).ShouldBeTrue();
		max.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Should_resolve_zoneless_exact_format_to_utc_never_local()
	{
		DateTimeOffsetParser.ParseExactRequired("2026-01-02 15:04:05", "yyyy-MM-dd HH:mm:ss", _invariant)
			.TryGetValue(out Success<DateTimeOffset> success).ShouldBeTrue();
		success.Value.Offset.ShouldBe(TimeSpan.Zero);
		success.Value.Hour.ShouldBe(15);
	}

	[Theory]
	[InlineData("1700000000", UnixPrecision.Seconds, 2023, 11, 14, 22)]
	[InlineData("1700000000000", UnixPrecision.Milliseconds, 2023, 11, 14, 22)]
	void Should_parse_declared_unix_epoch(string input, UnixPrecision precision, int year, int month, int day, int hour)
	{
		DateTimeOffsetParser.ParseUnix(input, precision)
			.TryGetValue(out Success<DateTimeOffset> success).ShouldBeTrue();
		success.Value.Offset.ShouldBe(TimeSpan.Zero);
		success.Value.Year.ShouldBe(year);
		success.Value.Month.ShouldBe(month);
		success.Value.Day.ShouldBe(day);
		success.Value.Hour.ShouldBe(hour);
	}

	[Fact]
	void Should_parse_negative_unix_epoch_before_1970()
	{
		DateTimeOffsetParser.ParseUnix("-1", UnixPrecision.Seconds)
			.TryGetValue(out Success<DateTimeOffset> success).ShouldBeTrue();
		success.Value.ShouldBe(new DateTimeOffset(1969, 12, 31, 23, 59, 59, TimeSpan.Zero));
	}

	[Theory]
	[InlineData("1700000000.5")] // fractional epoch is not an integer
	[InlineData("not-a-number")]
	void Should_fail_with_malformed_reason_when_unix_input_is_not_integer(string input)
	{
		DateTimeOffsetParser.ParseUnix(input, UnixPrecision.Seconds)
			.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Should_not_guess_a_bare_number_as_a_date_on_the_iso_door() =>
		DateTimeOffsetParser.ParseRequired("1700000000").TryGetValue(out Failure _).ShouldBeTrue();

	[Fact]
	void Should_throw_when_unix_precision_is_undefined() =>
		Should.Throw<ArgumentOutOfRangeException>(() => DateTimeOffsetParser.ParseUnix("1700000000", default));

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_fail_with_empty_reason_when_required_input_is_absent(string? input)
	{
		DateTimeOffsetParser.ParseRequired(input).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
		failure.ExpectedType.ShouldBe("DateTimeOffset");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_return_absent_when_optional_input_is_absent(string? input) =>
		DateTimeOffsetParser.ParseOptional(input).HasValue.ShouldBeFalse();

	[Fact]
	void Should_return_absent_when_optional_unix_input_is_absent() =>
		DateTimeOffsetParser.ParseUnixOptional("   ", UnixPrecision.Seconds).HasValue.ShouldBeFalse();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build Svartalfheim.slnx`
Expected: FAIL — `UnixPrecision` and `DateTimeOffsetParser` do not exist.

- [ ] **Step 3a: Create the enum**

Create `src/Primitives/UnixPrecision.cs`:

```csharp
namespace Norse.Primitives;

/// <summary>
/// The declared unit of a Unix-epoch value. There is no magnitude guessing — the caller states the
/// unit, so a bare number is never silently interpreted as seconds or milliseconds.
/// </summary>
public enum UnixPrecision
{
	/// <summary>Sentinel CLR default — never a valid precision; rejected by the Unix parse doors.</summary>
	Unspecified = 0,

	/// <summary>Seconds since 1970-01-01T00:00:00Z.</summary>
	Seconds = 1,

	/// <summary>Milliseconds since 1970-01-01T00:00:00Z.</summary>
	Milliseconds = 2,
}
```

- [ ] **Step 3b: Create the parser**

Create `src/Primitives/DateTimeOffsetParser.cs`:

```csharp
using System.Globalization;

namespace Norse.Primitives;

/// <summary>
/// Span-based parser for <see cref="DateTimeOffset"/>. The ISO door accepts the
/// <c>yyyy-MM-ddTHH:mm:ss[.fffffff]</c> profile with a <b>mandatory</b> zone (literal <c>Z</c> or a
/// numeric <c>±hh:mm</c> offset), normalized to UTC; a zone-less or space-separated form is
/// <see cref="ParseFailure.Malformed"/>. The exact door honors a single caller-declared format
/// under a required provider, also resolving to UTC (never local). <see cref="ParseUnix"/> reads a
/// declared Unix epoch. The sentinel guard rejects <see cref="DateTimeOffset.MinValue"/>/<see cref="DateTimeOffset.MaxValue"/>.
/// </summary>
public static class DateTimeOffsetParser
{
	const string ExpectedType = nameof(DateTimeOffset);
	const string IsoLabel = "ISO 8601";

	const long MinUnixSeconds = -62135596800L;
	const long MaxUnixSeconds = 253402300799L;
	const long MinUnixMilliseconds = -62135596800000L;
	const long MaxUnixMilliseconds = 253402300799999L;

	static readonly string[] _isoFormats =
	[
		"yyyy-MM-ddTHH:mm:ss.FFFFFFF'Z'",
		"yyyy-MM-ddTHH:mm:ss'Z'",
		"yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz",
		"yyyy-MM-ddTHH:mm:sszzz",
	];

	const DateTimeStyles IsoStyles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
	const DateTimeStyles ExactStyles = IsoStyles | DateTimeStyles.AllowWhiteSpaces;

	/// <summary>Parses an ISO datetime with a mandatory zone, normalized to UTC. Empty ⇒ <see cref="ParseFailure.Empty"/>; unrecognized, zone-less, or sentinel ⇒ <see cref="ParseFailure.Malformed"/>.</summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	public static Result<DateTimeOffset> ParseRequired(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return ParseIso(trimmed);
	}

	/// <summary>Parses an optional ISO datetime. Empty ⇒ absent; unrecognized, zone-less, or sentinel ⇒ <see cref="ParseFailure.Malformed"/>.</summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	public static Result<DateTimeOffset>? ParseOptional(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return ParseIso(trimmed);
	}

	/// <summary>Parses a datetime against a single caller-declared <paramref name="format"/>, resolving to UTC (never local).</summary>
	/// <param name="input">The raw scalar text.</param>
	/// <param name="format">The exact format. Required, non-empty.</param>
	/// <param name="provider">The declared culture. Never null.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	/// <exception cref="ArgumentException"><paramref name="format"/> is null or empty.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<DateTimeOffset> ParseExactRequired(ReadOnlySpan<char> input, string format, IFormatProvider provider)
	{
		ArgumentException.ThrowIfNullOrEmpty(format);
		ArgumentNullException.ThrowIfNull(provider);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return ParseExact(trimmed, format, provider);
	}

	/// <summary>Parses an optional datetime against a single caller-declared <paramref name="format"/>.</summary>
	/// <param name="input">The raw scalar text.</param>
	/// <param name="format">The exact format. Required, non-empty.</param>
	/// <param name="provider">The declared culture. Never null.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	/// <exception cref="ArgumentException"><paramref name="format"/> is null or empty.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<DateTimeOffset>? ParseExactOptional(ReadOnlySpan<char> input, string format, IFormatProvider provider)
	{
		ArgumentException.ThrowIfNullOrEmpty(format);
		ArgumentNullException.ThrowIfNull(provider);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return ParseExact(trimmed, format, provider);
	}

	/// <summary>Parses a declared Unix epoch (integer; negatives allowed). Empty ⇒ <see cref="ParseFailure.Empty"/>; non-integer, out-of-range, or sentinel ⇒ <see cref="ParseFailure.Malformed"/>.</summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <param name="precision">The declared unit. Must be <see cref="UnixPrecision.Seconds"/> or <see cref="UnixPrecision.Milliseconds"/>.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="precision"/> is undefined.</exception>
	public static Result<DateTimeOffset> ParseUnix(ReadOnlySpan<char> input, UnixPrecision precision)
	{
		GuardPrecision(precision);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return ParseUnixCore(trimmed, precision);
	}

	/// <summary>Parses an optional declared Unix epoch. Empty ⇒ absent; non-integer, out-of-range, or sentinel ⇒ <see cref="ParseFailure.Malformed"/>.</summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <param name="precision">The declared unit. Must be <see cref="UnixPrecision.Seconds"/> or <see cref="UnixPrecision.Milliseconds"/>.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="precision"/> is undefined.</exception>
	public static Result<DateTimeOffset>? ParseUnixOptional(ReadOnlySpan<char> input, UnixPrecision precision)
	{
		GuardPrecision(precision);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return ParseUnixCore(trimmed, precision);
	}

	static Result<DateTimeOffset> ParseIso(ReadOnlySpan<char> trimmed)
	{
		if (DateTimeOffset.TryParseExact(trimmed, _isoFormats, CultureInfo.InvariantCulture, IsoStyles, out var value)
			&& !IsSentinel(value))
			return new Success<DateTimeOffset>(value);
		return new Failure(ParseFailure.Malformed, trimmed, ExpectedType, IsoLabel);
	}

	static Result<DateTimeOffset> ParseExact(ReadOnlySpan<char> trimmed, string format, IFormatProvider provider)
	{
		if (DateTimeOffset.TryParseExact(trimmed, format, provider, ExactStyles, out var value)
			&& !IsSentinel(value))
			return new Success<DateTimeOffset>(value);
		return new Failure(ParseFailure.Malformed, trimmed, ExpectedType, format);
	}

	static Result<DateTimeOffset> ParseUnixCore(ReadOnlySpan<char> trimmed, UnixPrecision precision)
	{
		if (!long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var epoch)
			|| !InRange(epoch, precision))
			return new Failure(ParseFailure.Malformed, trimmed, ExpectedType);
		var value = precision == UnixPrecision.Seconds
			? DateTimeOffset.FromUnixTimeSeconds(epoch)
			: DateTimeOffset.FromUnixTimeMilliseconds(epoch);
		if (IsSentinel(value))
			return new Failure(ParseFailure.Malformed, trimmed, ExpectedType);
		return new Success<DateTimeOffset>(value);
	}

	static bool InRange(long epoch, UnixPrecision precision) =>
		precision == UnixPrecision.Seconds
			? epoch is >= MinUnixSeconds and <= MaxUnixSeconds
			: epoch is >= MinUnixMilliseconds and <= MaxUnixMilliseconds;

	static bool IsSentinel(DateTimeOffset value) =>
		value == DateTimeOffset.MinValue || value == DateTimeOffset.MaxValue;

	static void GuardPrecision(UnixPrecision precision)
	{
		if (precision is not (UnixPrecision.Seconds or UnixPrecision.Milliseconds))
			throw new ArgumentOutOfRangeException(nameof(precision), precision, "Precision must be Seconds or Milliseconds.");
	}
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Svartalfheim.slnx && dotnet test tests/Primitives.Tests -- --filter-class "*.DateTimeOffsetParserTests"`
Expected: build clean, all pass.

- [ ] **Step 5: Stage (do not commit)**

```bash
git add src/Primitives/UnixPrecision.cs src/Primitives/DateTimeOffsetParser.cs tests/Primitives.Tests/DateTimeOffsetParserTests.cs
```

---

### Task 4: `DateTimeParser`

Mirrors `DateTimeOffsetParser`, yielding a `Kind=Utc` `DateTime`; the exact door additionally carries `NoCurrentDateDefault` so a missing date component is never backfilled with today.

**Files:**
- Create: `src/Primitives/DateTimeParser.cs`
- Test: `tests/Primitives.Tests/DateTimeParserTests.cs`

**Interfaces:**
- Consumes: `UnixPrecision` (Task 3).
- Produces:
  - `DateTimeParser.ParseRequired(ReadOnlySpan<char>) → Result<DateTime>`
  - `DateTimeParser.ParseOptional(ReadOnlySpan<char>) → Result<DateTime>?`
  - `DateTimeParser.ParseExactRequired(ReadOnlySpan<char>, string, IFormatProvider) → Result<DateTime>`
  - `DateTimeParser.ParseExactOptional(ReadOnlySpan<char>, string, IFormatProvider) → Result<DateTime>?`
  - `DateTimeParser.ParseUnix(ReadOnlySpan<char>, UnixPrecision) → Result<DateTime>`
  - `DateTimeParser.ParseUnixOptional(ReadOnlySpan<char>, UnixPrecision) → Result<DateTime>?`

- [ ] **Step 1: Write the failing test**

Create `tests/Primitives.Tests/DateTimeParserTests.cs`:

```csharp
using System.Globalization;

namespace Norse.Primitives.Tests;

public sealed class DateTimeParserTests
{
	const string AllWhitespace = " \t\r\n\f ";

	static readonly IFormatProvider _invariant = CultureInfo.InvariantCulture;

	[Fact]
	void Should_parse_utc_zone_to_utc_kind()
	{
		DateTimeParser.ParseRequired("2026-01-02T15:04:05Z")
			.TryGetValue(out Success<DateTime> success).ShouldBeTrue();
		success.Value.Kind.ShouldBe(DateTimeKind.Utc);
		success.Value.Hour.ShouldBe(15);
	}

	[Fact]
	void Should_normalize_offset_to_utc()
	{
		DateTimeParser.ParseRequired("2026-01-02T15:04:05+05:00")
			.TryGetValue(out Success<DateTime> success).ShouldBeTrue();
		success.Value.Kind.ShouldBe(DateTimeKind.Utc);
		success.Value.Hour.ShouldBe(10);
	}

	[Theory]
	[InlineData("2026-01-02T15:04:05")]   // zone-less rejected
	[InlineData("2026-01-02 15:04:05Z")]  // space separator rejected
	void Should_fail_with_malformed_reason_when_iso_is_zoneless_or_spaced(string input)
	{
		DateTimeParser.ParseRequired(input).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("DateTime");
		failure.Format.ShouldBe("ISO 8601");
	}

	[Fact]
	void Should_reject_sentinel_datetimes_as_malformed()
	{
		DateTimeParser.ParseRequired("0001-01-01T00:00:00Z").TryGetValue(out Failure min).ShouldBeTrue();
		min.Reason.ShouldBe(ParseFailure.Malformed);
		DateTimeParser.ParseRequired("9999-12-31T23:59:59.9999999Z").TryGetValue(out Failure max).ShouldBeTrue();
		max.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Should_parse_declared_unix_epoch_to_utc_datetime()
	{
		DateTimeParser.ParseUnix("1700000000", UnixPrecision.Seconds)
			.TryGetValue(out Success<DateTime> success).ShouldBeTrue();
		success.Value.Kind.ShouldBe(DateTimeKind.Utc);
		success.Value.Year.ShouldBe(2023);
	}

	[Fact]
	void Should_honor_declared_format_on_the_exact_door()
	{
		DateTimeParser.ParseExactRequired("2026-01-02 15:04:05", "yyyy-MM-dd HH:mm:ss", _invariant)
			.TryGetValue(out Success<DateTime> success).ShouldBeTrue();
		success.Value.Hour.ShouldBe(15);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_fail_with_empty_reason_when_required_input_is_absent(string? input)
	{
		DateTimeParser.ParseRequired(input).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
		failure.ExpectedType.ShouldBe("DateTime");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_return_absent_when_optional_input_is_absent(string? input) =>
		DateTimeParser.ParseOptional(input).HasValue.ShouldBeFalse();

	[Fact]
	void Should_throw_when_unix_precision_is_undefined() =>
		Should.Throw<ArgumentOutOfRangeException>(() => DateTimeParser.ParseUnix("1700000000", default));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build Svartalfheim.slnx`
Expected: FAIL — `DateTimeParser` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Primitives/DateTimeParser.cs`:

```csharp
using System.Globalization;

namespace Norse.Primitives;

/// <summary>
/// Span-based parser for <see cref="DateTime"/>. Identical ISO profile to
/// <see cref="DateTimeOffsetParser"/> — mandatory zone, normalized to UTC (<see cref="DateTimeKind.Utc"/>) —
/// plus a declared-exact door (carrying <see cref="DateTimeStyles.NoCurrentDateDefault"/> so a missing
/// date component fails loud rather than defaulting to today) and a declared <see cref="ParseUnix"/>
/// epoch door. The sentinel guard rejects <see cref="DateTime.MinValue"/>/<see cref="DateTime.MaxValue"/>.
/// </summary>
public static class DateTimeParser
{
	const string ExpectedType = nameof(DateTime);
	const string IsoLabel = "ISO 8601";

	const long MinUnixSeconds = -62135596800L;
	const long MaxUnixSeconds = 253402300799L;
	const long MinUnixMilliseconds = -62135596800000L;
	const long MaxUnixMilliseconds = 253402300799999L;

	static readonly string[] _isoFormats =
	[
		"yyyy-MM-ddTHH:mm:ss.FFFFFFF'Z'",
		"yyyy-MM-ddTHH:mm:ss'Z'",
		"yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz",
		"yyyy-MM-ddTHH:mm:sszzz",
	];

	const DateTimeStyles IsoStyles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
	const DateTimeStyles ExactStyles = IsoStyles | DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault;

	/// <summary>Parses an ISO datetime with a mandatory zone to a UTC <see cref="DateTime"/>. Empty ⇒ <see cref="ParseFailure.Empty"/>; unrecognized, zone-less, or sentinel ⇒ <see cref="ParseFailure.Malformed"/>.</summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	public static Result<DateTime> ParseRequired(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return ParseIso(trimmed);
	}

	/// <summary>Parses an optional ISO datetime. Empty ⇒ absent; unrecognized, zone-less, or sentinel ⇒ <see cref="ParseFailure.Malformed"/>.</summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	public static Result<DateTime>? ParseOptional(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return ParseIso(trimmed);
	}

	/// <summary>Parses a datetime against a single caller-declared <paramref name="format"/>, resolving to UTC (never local), with no current-date default.</summary>
	/// <param name="input">The raw scalar text.</param>
	/// <param name="format">The exact format. Required, non-empty.</param>
	/// <param name="provider">The declared culture. Never null.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	/// <exception cref="ArgumentException"><paramref name="format"/> is null or empty.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<DateTime> ParseExactRequired(ReadOnlySpan<char> input, string format, IFormatProvider provider)
	{
		ArgumentException.ThrowIfNullOrEmpty(format);
		ArgumentNullException.ThrowIfNull(provider);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return ParseExact(trimmed, format, provider);
	}

	/// <summary>Parses an optional datetime against a single caller-declared <paramref name="format"/>.</summary>
	/// <param name="input">The raw scalar text.</param>
	/// <param name="format">The exact format. Required, non-empty.</param>
	/// <param name="provider">The declared culture. Never null.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	/// <exception cref="ArgumentException"><paramref name="format"/> is null or empty.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<DateTime>? ParseExactOptional(ReadOnlySpan<char> input, string format, IFormatProvider provider)
	{
		ArgumentException.ThrowIfNullOrEmpty(format);
		ArgumentNullException.ThrowIfNull(provider);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return ParseExact(trimmed, format, provider);
	}

	/// <summary>Parses a declared Unix epoch to a UTC <see cref="DateTime"/> (integer; negatives allowed). Empty ⇒ <see cref="ParseFailure.Empty"/>; non-integer, out-of-range, or sentinel ⇒ <see cref="ParseFailure.Malformed"/>.</summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <param name="precision">The declared unit. Must be <see cref="UnixPrecision.Seconds"/> or <see cref="UnixPrecision.Milliseconds"/>.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="precision"/> is undefined.</exception>
	public static Result<DateTime> ParseUnix(ReadOnlySpan<char> input, UnixPrecision precision)
	{
		GuardPrecision(precision);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return ParseUnixCore(trimmed, precision);
	}

	/// <summary>Parses an optional declared Unix epoch to a UTC <see cref="DateTime"/>. Empty ⇒ absent; non-integer, out-of-range, or sentinel ⇒ <see cref="ParseFailure.Malformed"/>.</summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <param name="precision">The declared unit. Must be <see cref="UnixPrecision.Seconds"/> or <see cref="UnixPrecision.Milliseconds"/>.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="precision"/> is undefined.</exception>
	public static Result<DateTime>? ParseUnixOptional(ReadOnlySpan<char> input, UnixPrecision precision)
	{
		GuardPrecision(precision);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return ParseUnixCore(trimmed, precision);
	}

	static Result<DateTime> ParseIso(ReadOnlySpan<char> trimmed)
	{
		if (DateTime.TryParseExact(trimmed, _isoFormats, CultureInfo.InvariantCulture, IsoStyles, out var value)
			&& !IsSentinel(value))
			return new Success<DateTime>(value);
		return new Failure(ParseFailure.Malformed, trimmed, ExpectedType, IsoLabel);
	}

	static Result<DateTime> ParseExact(ReadOnlySpan<char> trimmed, string format, IFormatProvider provider)
	{
		if (DateTime.TryParseExact(trimmed, format, provider, ExactStyles, out var value)
			&& !IsSentinel(value))
			return new Success<DateTime>(value);
		return new Failure(ParseFailure.Malformed, trimmed, ExpectedType, format);
	}

	static Result<DateTime> ParseUnixCore(ReadOnlySpan<char> trimmed, UnixPrecision precision)
	{
		if (!long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var epoch)
			|| !InRange(epoch, precision))
			return new Failure(ParseFailure.Malformed, trimmed, ExpectedType);
		var value = (precision == UnixPrecision.Seconds
			? DateTimeOffset.FromUnixTimeSeconds(epoch)
			: DateTimeOffset.FromUnixTimeMilliseconds(epoch)).UtcDateTime;
		if (IsSentinel(value))
			return new Failure(ParseFailure.Malformed, trimmed, ExpectedType);
		return new Success<DateTime>(value);
	}

	static bool InRange(long epoch, UnixPrecision precision) =>
		precision == UnixPrecision.Seconds
			? epoch is >= MinUnixSeconds and <= MaxUnixSeconds
			: epoch is >= MinUnixMilliseconds and <= MaxUnixMilliseconds;

	static bool IsSentinel(DateTime value) =>
		value == DateTime.MinValue || value == DateTime.MaxValue;

	static void GuardPrecision(UnixPrecision precision)
	{
		if (precision is not (UnixPrecision.Seconds or UnixPrecision.Milliseconds))
			throw new ArgumentOutOfRangeException(nameof(precision), precision, "Precision must be Seconds or Milliseconds.");
	}
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Svartalfheim.slnx && dotnet test tests/Primitives.Tests -- --filter-class "*.DateTimeParserTests"`
Expected: build clean, all pass.

- [ ] **Step 5: Stage (do not commit)**

```bash
git add src/Primitives/DateTimeParser.cs tests/Primitives.Tests/DateTimeParserTests.cs
```

---

### Task 5: `TimeSpanParser`

Colon form (`TimeSpan.TryParse` under invariant) **plus** a hand-rolled ISO-8601-duration scanner; declared-exact door; sentinel guard (`Zero` valid, `Min`/`Max` rejected).

**Files:**
- Create: `src/Primitives/TimeSpanParser.cs`
- Test: `tests/Primitives.Tests/TimeSpanParserTests.cs`

**Interfaces:**
- Produces:
  - `TimeSpanParser.ParseRequired(ReadOnlySpan<char>) → Result<TimeSpan>`
  - `TimeSpanParser.ParseOptional(ReadOnlySpan<char>) → Result<TimeSpan>?`
  - `TimeSpanParser.ParseExactRequired(ReadOnlySpan<char>, string, IFormatProvider) → Result<TimeSpan>`
  - `TimeSpanParser.ParseExactOptional(ReadOnlySpan<char>, string, IFormatProvider) → Result<TimeSpan>?`

- [ ] **Step 1: Write the failing test**

Create `tests/Primitives.Tests/TimeSpanParserTests.cs`:

```csharp
using System.Globalization;

namespace Norse.Primitives.Tests;

public sealed class TimeSpanParserTests
{
	const string AllWhitespace = " \t\r\n\f ";

	static readonly IFormatProvider _invariant = CultureInfo.InvariantCulture;

	[Theory]
	[InlineData("01:30:00")]        // 1h30m, colon form
	[InlineData("1.06:00:00")]      // 1d6h
	[InlineData("PT1H30M")]         // ISO 8601 duration
	[InlineData("P1DT6H")]          // 1d6h ISO duration
	void Should_parse_value_when_duration_is_recognized(string input)
	{
		var actual = TimeSpanParser.ParseRequired(input);
		actual.TryGetValue(out Success<TimeSpan> success).ShouldBeTrue();
	}

	[Fact]
	void Should_parse_colon_and_iso_to_the_same_span()
	{
		TimeSpanParser.ParseRequired("01:30:00").TryGetValue(out Success<TimeSpan> colon).ShouldBeTrue();
		TimeSpanParser.ParseRequired("PT1H30M").TryGetValue(out Success<TimeSpan> iso).ShouldBeTrue();
		colon.Value.ShouldBe(new TimeSpan(1, 30, 0));
		iso.Value.ShouldBe(new TimeSpan(1, 30, 0));
	}

	[Fact]
	void Should_parse_iso_weeks_and_fractional_seconds()
	{
		TimeSpanParser.ParseRequired("P2W").TryGetValue(out Success<TimeSpan> weeks).ShouldBeTrue();
		weeks.Value.ShouldBe(TimeSpan.FromDays(14));
		TimeSpanParser.ParseRequired("PT1.5S").TryGetValue(out Success<TimeSpan> frac).ShouldBeTrue();
		frac.Value.ShouldBe(TimeSpan.FromSeconds(1.5));
	}

	[Fact]
	void Should_parse_negative_iso_duration()
	{
		TimeSpanParser.ParseRequired("-PT1H").TryGetValue(out Success<TimeSpan> success).ShouldBeTrue();
		success.Value.ShouldBe(TimeSpan.FromHours(-1));
	}

	[Fact]
	void Should_accept_zero_as_valid()
	{
		TimeSpanParser.ParseRequired("00:00:00").TryGetValue(out Success<TimeSpan> success).ShouldBeTrue();
		success.Value.ShouldBe(TimeSpan.Zero);
	}

	[Theory]
	[InlineData("P1Y")]   // years are not fixed durations
	[InlineData("P2M")]   // months (before T) are not fixed durations
	[InlineData("P")]     // no component
	[InlineData("PT")]    // T with no time component
	[InlineData("P3DT")]  // trailing T with no time component
	[InlineData("PT1H30")] // number with no unit
	[InlineData("90m")]   // bare unit shorthand not supported
	[InlineData("garbage")]
	void Should_fail_with_malformed_reason_when_duration_is_unrecognized(string input)
	{
		var actual = TimeSpanParser.ParseRequired(input);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("TimeSpan");
	}

	[Fact]
	void Should_honor_declared_format_on_the_exact_door()
	{
		TimeSpanParser.ParseExactRequired("01:30", @"hh\:mm", _invariant)
			.TryGetValue(out Success<TimeSpan> success).ShouldBeTrue();
		success.Value.ShouldBe(new TimeSpan(1, 30, 0));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_fail_with_empty_reason_when_required_input_is_absent(string? input)
	{
		TimeSpanParser.ParseRequired(input).TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
		failure.ExpectedType.ShouldBe("TimeSpan");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_return_absent_when_optional_input_is_absent(string? input) =>
		TimeSpanParser.ParseOptional(input).HasValue.ShouldBeFalse();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build Svartalfheim.slnx`
Expected: FAIL — `TimeSpanParser` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Primitives/TimeSpanParser.cs`:

```csharp
using System.Globalization;

namespace Norse.Primitives;

/// <summary>
/// Span-based parser for <see cref="TimeSpan"/>. The no-format door accepts both the BCL colon form
/// (<c>[-][d.]hh:mm:ss[.fffffff]</c>, parsed under <see cref="CultureInfo.InvariantCulture"/>) and an
/// ISO-8601 duration (<c>PT1H30M</c>, <c>P3DT4H</c>, weeks) restricted to fixed components — year and
/// month (<c>P1Y</c>, <c>P2M</c>) are not fixed durations and are <see cref="ParseFailure.Malformed"/>.
/// The exact door honors <see cref="TimeSpan.TryParseExact(System.ReadOnlySpan{char}, System.ReadOnlySpan{char}, System.IFormatProvider, out System.TimeSpan)"/>.
/// The sentinel guard rejects <see cref="TimeSpan.MinValue"/>/<see cref="TimeSpan.MaxValue"/>; <see cref="TimeSpan.Zero"/> is valid.
/// </summary>
public static class TimeSpanParser
{
	const string ExpectedType = nameof(TimeSpan);
	const string IsoLabel = "ISO 8601";
	const int MaxDigits = 18;

	/// <summary>Parses a colon-form or ISO-8601-duration span. Empty ⇒ <see cref="ParseFailure.Empty"/>; unrecognized or sentinel ⇒ <see cref="ParseFailure.Malformed"/>.</summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	public static Result<TimeSpan> ParseRequired(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return ParseDuration(trimmed);
	}

	/// <summary>Parses an optional span. Empty ⇒ absent; unrecognized or sentinel ⇒ <see cref="ParseFailure.Malformed"/>.</summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	public static Result<TimeSpan>? ParseOptional(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return ParseDuration(trimmed);
	}

	/// <summary>Parses a span against a single caller-declared <paramref name="format"/>.</summary>
	/// <param name="input">The raw scalar text.</param>
	/// <param name="format">The exact format. Required, non-empty.</param>
	/// <param name="provider">The declared culture. Never null.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	/// <exception cref="ArgumentException"><paramref name="format"/> is null or empty.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<TimeSpan> ParseExactRequired(ReadOnlySpan<char> input, string format, IFormatProvider provider)
	{
		ArgumentException.ThrowIfNullOrEmpty(format);
		ArgumentNullException.ThrowIfNull(provider);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return ParseExact(trimmed, format, provider);
	}

	/// <summary>Parses an optional span against a single caller-declared <paramref name="format"/>.</summary>
	/// <param name="input">The raw scalar text.</param>
	/// <param name="format">The exact format. Required, non-empty.</param>
	/// <param name="provider">The declared culture. Never null.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	/// <exception cref="ArgumentException"><paramref name="format"/> is null or empty.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<TimeSpan>? ParseExactOptional(ReadOnlySpan<char> input, string format, IFormatProvider provider)
	{
		ArgumentException.ThrowIfNullOrEmpty(format);
		ArgumentNullException.ThrowIfNull(provider);
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return ParseExact(trimmed, format, provider);
	}

	static Result<TimeSpan> ParseDuration(ReadOnlySpan<char> trimmed)
	{
		if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out var colon) && !IsSentinel(colon))
			return new Success<TimeSpan>(colon);
		if (TryParseIso8601Duration(trimmed, out var iso) && !IsSentinel(iso))
			return new Success<TimeSpan>(iso);
		return new Failure(ParseFailure.Malformed, trimmed, ExpectedType, IsoLabel);
	}

	static Result<TimeSpan> ParseExact(ReadOnlySpan<char> trimmed, string format, IFormatProvider provider)
	{
		if (TimeSpan.TryParseExact(trimmed, format, provider, out var value) && !IsSentinel(value))
			return new Success<TimeSpan>(value);
		return new Failure(ParseFailure.Malformed, trimmed, ExpectedType, format);
	}

	// Grammar: [-] 'P' { n('W'|'D') } [ 'T' { n('H'|'M') | n[.n]('S') } ] — at least one component;
	// year/month and any misplaced unit are rejected.
	static bool TryParseIso8601Duration(ReadOnlySpan<char> span, out TimeSpan result)
	{
		result = TimeSpan.Zero;
		var index = 0;
		var negative = false;
		if (index < span.Length && span[index] == '-')
		{
			negative = true;
			index++;
		}
		if (index >= span.Length || span[index] is not ('P' or 'p'))
			return false;
		index++;

		long ticks = 0;
		var inTime = false;
		var sawDateComponent = false;
		var sawTimeComponent = false;
		while (index < span.Length)
		{
			if (span[index] is 'T' or 't')
			{
				if (inTime)
					return false;
				inTime = true;
				index++;
				continue;
			}

			var start = index;
			while (index < span.Length && char.IsAsciiDigit(span[index]))
				index++;
			var hasFraction = false;
			if (index < span.Length && span[index] == '.')
			{
				hasFraction = true;
				index++;
				while (index < span.Length && char.IsAsciiDigit(span[index]))
					index++;
			}
			if (index == start || index - start > MaxDigits || index >= span.Length)
				return false;

			var number = span[start..index];
			var unit = span[index];
			index++;
			switch (unit)
			{
				case 'W' or 'w' when !inTime:
					if (hasFraction || !long.TryParse(number, out var weeks))
						return false;
					ticks += weeks * 7 * TimeSpan.TicksPerDay;
					sawDateComponent = true;
					break;
				case 'D' or 'd' when !inTime:
					if (hasFraction || !long.TryParse(number, out var days))
						return false;
					ticks += days * TimeSpan.TicksPerDay;
					sawDateComponent = true;
					break;
				case 'H' or 'h' when inTime:
					if (hasFraction || !long.TryParse(number, out var hours))
						return false;
					ticks += hours * TimeSpan.TicksPerHour;
					sawTimeComponent = true;
					break;
				case 'M' when inTime:
					if (hasFraction || !long.TryParse(number, out var minutes))
						return false;
					ticks += minutes * TimeSpan.TicksPerMinute;
					sawTimeComponent = true;
					break;
				case 'S' or 's' when inTime:
					if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds))
						return false;
					ticks += (long)(seconds * TimeSpan.TicksPerSecond);
					sawTimeComponent = true;
					break;
				default:
					return false; // Y, M-before-T (months), or a misplaced unit
			}
		}

		if (!sawDateComponent && !sawTimeComponent)
			return false;
		if (inTime && !sawTimeComponent)
			return false; // a 'T' with no time component

		result = negative ? new TimeSpan(-ticks) : new TimeSpan(ticks);
		return true;
	}

	static bool IsSentinel(TimeSpan value) =>
		value == TimeSpan.MinValue || value == TimeSpan.MaxValue;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Svartalfheim.slnx && dotnet test tests/Primitives.Tests -- --filter-class "*.TimeSpanParserTests"`
Expected: build clean, all pass.

- [ ] **Step 5: Stage (do not commit)**

```bash
git add src/Primitives/TimeSpanParser.cs tests/Primitives.Tests/TimeSpanParserTests.cs
```

---

### Task 6: Gateway integration

Wire the five ISO doors into `Parser.ParseRequired`/`ParseOptional` and prove the routing, closing the standing §2.6 hole.

**Files:**
- Modify: `src/Primitives/Parser.cs` (add five branches after the `Guid` branch in each method)
- Test: `tests/Primitives.Tests/ParserTests.cs` (append routing tests)

**Interfaces:**
- Consumes: the ISO doors of all five specialists (Tasks 1–5).
- Produces: no new public surface — the existing `Parser.ParseRequired<T>`/`ParseOptional<T>` now route temporal types.

- [ ] **Step 1: Write the failing test**

Append to `tests/Primitives.Tests/ParserTests.cs` (inside the class, before the closing brace):

```csharp
	[Fact]
	void Should_route_iso_date_through_the_gateway()
	{
		Parser.ParseRequired<DateOnly>("2026-01-02", _invariant)
			.TryGetValue(out Success<DateOnly> success).ShouldBeTrue();
		success.Value.ShouldBe(new DateOnly(2026, 1, 2));
	}

	[Fact]
	void Should_route_iso_datetimeoffset_to_utc_through_the_gateway()
	{
		Parser.ParseRequired<DateTimeOffset>("2026-01-02T15:04:05+05:00", _invariant)
			.TryGetValue(out Success<DateTimeOffset> success).ShouldBeTrue();
		success.Value.Offset.ShouldBe(TimeSpan.Zero);
		success.Value.Hour.ShouldBe(10);
	}

	[Fact]
	void Should_route_iso_datetime_and_time_and_timespan_through_the_gateway()
	{
		Parser.ParseRequired<DateTime>("2026-01-02T15:04:05Z", _invariant)
			.TryGetValue(out Success<DateTime> dateTime).ShouldBeTrue();
		dateTime.Value.Kind.ShouldBe(DateTimeKind.Utc);
		Parser.ParseRequired<TimeOnly>("15:04:05", _invariant)
			.TryGetValue(out Success<TimeOnly> time).ShouldBeTrue();
		time.Value.ShouldBe(new TimeOnly(15, 4, 5));
		Parser.ParseRequired<TimeSpan>("PT1H30M", _invariant)
			.TryGetValue(out Success<TimeSpan> span).ShouldBeTrue();
		span.Value.ShouldBe(new TimeSpan(1, 30, 0));
	}

	[Theory]
	[InlineData("1/2/2026")]              // US slash date
	[InlineData("2026-01-02T15:04:05")]   // zone-less datetime
	void Should_reject_non_iso_temporal_through_the_gateway(string input)
	{
		Parser.ParseRequired<DateTimeOffset>(input, _invariant)
			.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Format.ShouldBe("ISO 8601");
	}

	[Fact]
	void Should_not_guess_a_bare_number_as_a_date_through_the_gateway() =>
		Parser.ParseRequired<DateTimeOffset>("1700000000", _invariant)
			.TryGetValue(out Failure _).ShouldBeTrue();

	[Fact]
	void Should_route_optional_temporal_absence_as_null_through_the_gateway() =>
		Parser.ParseOptional<DateOnly>("  ", _invariant).HasValue.ShouldBeFalse();

	[Fact]
	void Should_require_provider_even_for_culture_insensitive_temporal() =>
		Should.Throw<ArgumentNullException>(() => Parser.ParseRequired<DateOnly>("2026-01-02", null!));
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build Svartalfheim.slnx`
Expected: FAIL — `Parser.ParseRequired<DateOnly>` currently routes through the generic `ISpanParsable` tail; `Should_reject_non_iso_temporal_through_the_gateway` and `Should_not_guess_a_bare_number...` fail (the generic path accepts `1/2/2026` and a bare number under invariant flexible parse), proving the hole exists.

- [ ] **Step 3: Add the branches**

In `src/Primitives/Parser.cs`, in `ParseRequired<T>`, immediately after the `Guid` branch (the block ending at the `}` before `var trimmed = input.Trim();`), insert:

```csharp
		if (typeof(T) == typeof(DateOnly))
		{
			var routed = DateOnlyParser.ParseRequired(input);
			return Unsafe.As<Result<DateOnly>, Result<T>>(ref routed);
		}
		if (typeof(T) == typeof(DateTime))
		{
			var routed = DateTimeParser.ParseRequired(input);
			return Unsafe.As<Result<DateTime>, Result<T>>(ref routed);
		}
		if (typeof(T) == typeof(DateTimeOffset))
		{
			var routed = DateTimeOffsetParser.ParseRequired(input);
			return Unsafe.As<Result<DateTimeOffset>, Result<T>>(ref routed);
		}
		if (typeof(T) == typeof(TimeOnly))
		{
			var routed = TimeOnlyParser.ParseRequired(input);
			return Unsafe.As<Result<TimeOnly>, Result<T>>(ref routed);
		}
		if (typeof(T) == typeof(TimeSpan))
		{
			var routed = TimeSpanParser.ParseRequired(input);
			return Unsafe.As<Result<TimeSpan>, Result<T>>(ref routed);
		}
```

In `ParseOptional<T>`, immediately after the `Guid` branch, insert:

```csharp
		if (typeof(T) == typeof(DateOnly))
		{
			var routed = DateOnlyParser.ParseOptional(input);
			return Unsafe.As<Result<DateOnly>?, Result<T>?>(ref routed);
		}
		if (typeof(T) == typeof(DateTime))
		{
			var routed = DateTimeParser.ParseOptional(input);
			return Unsafe.As<Result<DateTime>?, Result<T>?>(ref routed);
		}
		if (typeof(T) == typeof(DateTimeOffset))
		{
			var routed = DateTimeOffsetParser.ParseOptional(input);
			return Unsafe.As<Result<DateTimeOffset>?, Result<T>?>(ref routed);
		}
		if (typeof(T) == typeof(TimeOnly))
		{
			var routed = TimeOnlyParser.ParseOptional(input);
			return Unsafe.As<Result<TimeOnly>?, Result<T>?>(ref routed);
		}
		if (typeof(T) == typeof(TimeSpan))
		{
			var routed = TimeSpanParser.ParseOptional(input);
			return Unsafe.As<Result<TimeSpan>?, Result<T>?>(ref routed);
		}
```

Also extend the `Parser` class XML `<remarks>` to mention the temporal routes (the temporal types route to their ISO door — no provider forwarded — exactly as `char`/`Guid`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build Svartalfheim.slnx && dotnet test tests/Primitives.Tests -- --filter-class "*.ParserTests"`
Expected: build clean, all `ParserTests` pass — the temporal routing tests now green, the §2.6 hole closed.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test Svartalfheim.slnx`
Expected: every test green (the four new specialist classes + gateway routing + all prior tests).

- [ ] **Step 6: Stage (do not commit)**

```bash
git add src/Primitives/Parser.cs tests/Primitives.Tests/ParserTests.cs
```

---

### Task 7: AOT smoke extension

Prove the temporal routes survive native compilation (generic-math/`Unsafe.As` dispatch stays AOT-clean).

**Files:**
- Modify: `tests/smoke/Primitives.Aot.Smoke/Program.cs`

- [ ] **Step 1: Add the probes**

In `tests/smoke/Primitives.Aot.Smoke/Program.cs`, after the existing `Check("gateway routes guid prefix stripping", ...)` block and before the `if (failures > 0)` block, insert:

```csharp
Check("gateway routes an ISO date", () =>
	Parser.ParseRequired<DateOnly>("2026-01-02", invariant) == (Result<DateOnly>)new Success<DateOnly>(new DateOnly(2026, 1, 2)));

Check("gateway normalizes an offset datetime to UTC", () =>
	Parser.ParseRequired<DateTimeOffset>("2026-01-02T15:04:05+05:00", invariant)
		== (Result<DateTimeOffset>)new Success<DateTimeOffset>(new DateTimeOffset(2026, 1, 2, 10, 4, 5, TimeSpan.Zero)));

Check("gateway rejects a zone-less datetime", () =>
	Parser.ParseRequired<DateTimeOffset>("2026-01-02T15:04:05", invariant).TryGetValue(out Failure _));

Check("gateway routes an ISO-8601 duration", () =>
	Parser.ParseRequired<TimeSpan>("PT1H30M", invariant) == (Result<TimeSpan>)new Success<TimeSpan>(new TimeSpan(1, 30, 0)));

Check("declared unix epoch parses off-gateway", () =>
	DateTimeOffsetParser.ParseUnix("1700000000", UnixPrecision.Seconds)
		.TryGetValue(out Success<DateTimeOffset> epoch) && epoch.Value.Year == 2023);
```

- [ ] **Step 2: Publish and run the native smoke**

Run: `dotnet publish tests/smoke/Primitives.Aot.Smoke -c Release`
Then run the produced native executable (path printed by publish, under `bin/Release/net11.0/<rid>/publish/`).
Expected: zero AOT warnings during publish; the exe prints `ok` for every check and `AOT smoke passed: ...`, exit code 0.

> Requires the VS "Desktop development with C++" workload on Windows (per Svartalfheim CLAUDE.md).

- [ ] **Step 3: Stage (do not commit)**

```bash
git add tests/smoke/Primitives.Aot.Smoke/Program.cs
```

- [ ] **Step 4: Final verification**

Run: `dotnet build Svartalfheim.slnx && dotnet test Svartalfheim.slnx`
Expected: build clean at WarningLevel 9999; entire suite green. Report the test count and the AOT result; do not claim completion without this output.

---

## Self-Review

**1. Spec coverage:**
- §3 four doors → Tasks 1–5 (ISO + exact on all; Unix on Tasks 3–4). ✓
- §4 ISO profiles (DateOnly `yyyy-MM-dd`; TimeOnly 24-hour; DateTime/DTO `T`-only, zone-mandatory, UTC-normalized) → Tasks 1–4 formats + IsoStyles. ✓
- §5 offset policy (zone mandatory, never local) → Tasks 3–4 (`_isoFormats` carry only `'Z'`/`zzz`; `AssumeUniversal|AdjustToUniversal`; zone-less test pinned). ✓
- §6 exact door (single required `string format`, provider required, `NoCurrentDateDefault` for DateTime) → all tasks; DateTime `ExactStyles`. ✓
- §7 Unix door (declared `UnixPrecision`, integer-only, negatives, DTO+DateTime, off-gateway) → Task 3 (enum + DTO) and Task 4 (DateTime). ✓
- §8 TimeSpan (colon + ISO duration fixed-components, hand-rolled scanner, Y/M rejected) → Task 5. ✓
- §9 sentinel guard (date-bearing + TimeSpan reject Min/Max; TimeOnly exempt; Zero valid) → `IsSentinel` in Tasks 1, 3, 4, 5; absent in Task 2; tests pin each. ✓
- §10 gateway integration (five branches, no provider forwarded, Format="ISO 8601") → Task 6. ✓
- §11 tests + AOT → Tasks 1–7. ✓
- ISO/Unix doors take no provider (corrected spec §3) → all specialist ISO/Unix signatures omit `IFormatProvider`; gateway calls them without forwarding. ✓

**2. Placeholder scan:** No TBD/TODO; every code step shows complete code; every test step shows real assertions. ✓

**3. Type consistency:** `ParseRequired`/`ParseOptional`/`ParseExactRequired`/`ParseExactOptional`/`ParseUnix`/`ParseUnixOptional`, `IsSentinel`, `GuardPrecision`, `InRange`, `TryParseIso8601Duration`, `UnixPrecision { Unspecified, Seconds, Milliseconds }` are spelled identically across the producing and consuming tasks and the gateway. Return types (`Result<T>` / `Result<T>?`) match the `Unsafe.As` reinterprets in Task 6. ✓

---

## Post-Implementation Amendment (2026-06-17, final whole-branch review)

The increment was implemented subagent-driven and is fully staged (not committed). All seven tasks landed verbatim from the literal code above **except** the `TimeSpanParser` ISO-8601 duration scanner, where the final whole-branch review caught two real defects in the scanner as written in Task 5:

- **C1 (Critical):** `(long)(seconds * TimeSpan.TicksPerSecond)` threw `OverflowException` on an 18-digit seconds component (e.g. `PT999999999999999999S`) — the exception escaped the parser, violating the spec's "never raises on bad input" contract.
- **I1 (Important):** the raw `long` accumulations (`ticks += weeks * 7 * TimeSpan.TicksPerDay;` and the D/H/M cases) wrapped silently on large components and returned a wrong `Success` — a silent fallback (§2.7/§8 violation).

**Fix (exception-free, overflow-safe):** a private `TryAddTicks(ref long ticks, long quantity, long ticksPerUnit)` bounds-checks `quantity > (long.MaxValue - ticks) / ticksPerUnit` before each W/D/H/M multiply-add (all quantities are non-negative; the sign is applied once at the end), and the seconds case bounds-checks `secondTicks > long.MaxValue - ticks` before the decimal→long cast. Overflow now returns `false` ⇒ `Malformed`. `MaxDigits = 18` was kept and re-commented as a digit-run sanity bound, explicitly **not** the overflow guard.

**Tests added (M3 + regression):** `TimeSpan.MinValue`/`MaxValue` round-trip strings ⇒ `Malformed` (the §11-mandated sentinel cases), and the three overflow inputs above ⇒ `Malformed`.

**Final state:** `dotnet build Svartalfheim.slnx` clean at WarningLevel 9999; `dotnet test Svartalfheim.slnx` = **368 passed / 0 failed / 0 skipped**; Native AOT smoke publishes with 0 warnings and the native exe passes all 16 checks, exit 0. The corrected scanner is the authoritative code; this amendment is the record of the divergence.
