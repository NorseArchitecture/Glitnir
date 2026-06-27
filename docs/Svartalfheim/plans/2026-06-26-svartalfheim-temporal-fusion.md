# Svartalfheim Temporal Fusion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Forge `TimeZoneParser` (scalar IANA zone resolver) and `TemporalFusion` (three-span ISO date + ISO time + IANA zone → UTC `DateTime`) — the fifth increment of `Norse.Primitives`.

**Architecture:** `TimeZoneParser` follows the established scalar-parser template (static class, `ParseRequired`/`ParseOptional`, honest no-provider signature, empty/malformed failure semantics) with `TimeZoneInfo.TryFindSystemTimeZoneById` as the backing call. `TemporalFusion` composes the three ISO-canonical doors in a first-failure-wins chain, checks both DST seams explicitly before conversion, and applies the sentinel guard to the UTC result — no new gateway branch in `Parser`, no change to `ParseFailure`, `Failure`, or `Result<T>`.

**Tech Stack:** .NET 11 preview (SDK pinned by `global.json`), C# `LangVersion=preview`, xUnit v3 + Shouldly on Microsoft.Testing.Platform. Run commands from the **Svartalfheim repo root** (`Bifrost/Svartalfheim/`).

## Global Constraints

Every task's requirements implicitly include this section.

- **Target:** `net11.0`; SDK pinned by `global.json` (`11.0.100-` prerelease, rollForward latestFeature); C# `LangVersion=preview`.
- **Warnings are errors** (WarningLevel 9999, EnforceCodeStyleInBuild). XML docs are **mandatory on every public `src` member** (CS1591 is an error in `src`). A single warning fails the build.
- **Tabs** for indentation in all `.cs` files. `var` for return assignments only; construction uses explicit type with target-typed `new()`. Omit default accessibility modifiers; `sealed` where applicable; test classes are `public sealed`.
- **No provider** on either new specialist — both are culture-insensitive by design. Honest signatures: no `IFormatProvider` parameter, no silent local-zone fallback.
- **`ParseFailure` is unchanged** — `Unspecified`/`Empty`/`Malformed` only. No new members.
- **Failure choreography:** empty/whitespace required input ⇒ `new Failure(ParseFailure.Empty, string.Empty, typeof(T).Name)`; optional ⇒ `null`. Unrecognized ⇒ `new Failure(ParseFailure.Malformed, trimmedSpan, expectedType, formatLabel)` using the span ctor (truncation is `Failure`'s job — never pre-truncate). DST seam failures use the string ctor with a composite `Input`; `Detail` carries the stable token.
- **US English** everywhere. Test naming `Should_{behavior}_when_{condition}`; test classes `public sealed`; test methods omit access modifiers and return `void`; Shouldly/xUnit usings are global (injected via tests props — never add per-file).
- **VSTest `--filter` does NOT work.** Filter a single class with `dotnet test tests/Primitives.Tests -- --filter-class "*.ClassName"`.
- **No automatic git commits.** Each task ends by staging (`git add`) and showing the diff; the human commits. Suggested commit messages are provided; do not run `git commit`.
- **Run commands from the Svartalfheim repo root** (`Bifrost/Svartalfheim/`).

## File Map

```
src/Primitives/TimeZoneParser.cs         ← new: IANA zone id → Result<TimeZoneInfo>
src/Primitives/TemporalFusion.cs         ← new: (date, time, zone) → Result<DateTime>
tests/Primitives.Tests/TimeZoneParserTests.cs  ← new
tests/Primitives.Tests/TemporalFusionTests.cs  ← new
tests/smoke/Primitives.Aot.Smoke/Program.cs   ← extend: two new Check() probes
```

No edits to `Parser.cs` — `TimeZoneInfo` does not implement `ISpanParsable<TSelf>`, so no gateway branch is added.

---

### Task 1: `TimeZoneParser`

**Files:**
- Create: `src/Primitives/TimeZoneParser.cs`
- Create: `tests/Primitives.Tests/TimeZoneParserTests.cs`

**Interfaces:**
- Consumes: `Result<T>`, `Success<T>`, `Failure`, `ParseFailure` (all existing).
- Produces:
  - `TimeZoneParser.ParseRequired(ReadOnlySpan<char> input) → Result<TimeZoneInfo>`
  - `TimeZoneParser.ParseOptional(ReadOnlySpan<char> input) → Result<TimeZoneInfo>?`

- [ ] **Step 1: Write the failing tests**

Create `tests/Primitives.Tests/TimeZoneParserTests.cs`:

```csharp
namespace Norse.Primitives.Tests;

public sealed class TimeZoneParserTests
{
	const string AllWhitespace = " \t\r\n\f ";

	[Fact]
	void Should_resolve_value_when_iana_id_is_recognized()
	{
		var actual = TimeZoneParser.ParseRequired("America/Chicago");
		actual.TryGetValue(out Success<TimeZoneInfo> success).ShouldBeTrue();
		success.Value.Id.ShouldBe("America/Chicago");
	}

	[Fact]
	void Should_trim_surrounding_whitespace_before_resolving()
	{
		var actual = TimeZoneParser.ParseRequired("  America/New_York  ");
		actual.TryGetValue(out Success<TimeZoneInfo> success).ShouldBeTrue();
		success.Value.Id.ShouldBe("America/New_York");
	}

	[Theory]
	[InlineData("Not/A/Zone")]
	[InlineData("garbage")]
	[InlineData("America/Bogus")]
	void Should_fail_with_malformed_reason_when_iana_id_is_unrecognized(string input)
	{
		var actual = TimeZoneParser.ParseRequired(input);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("TimeZoneInfo");
		failure.Format.ShouldBe("IANA");
		failure.Detail.ShouldBeNull();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_fail_with_empty_reason_when_required_input_is_absent(string? input)
	{
		var actual = TimeZoneParser.ParseRequired(input);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
		failure.Input.ShouldBe(string.Empty);
		failure.ExpectedType.ShouldBe("TimeZoneInfo");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_return_absent_when_optional_input_is_absent(string? input) =>
		TimeZoneParser.ParseOptional(input).HasValue.ShouldBeFalse();

	[Fact]
	void Should_resolve_value_when_optional_input_is_recognized()
	{
		var actual = TimeZoneParser.ParseOptional("Europe/London");
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Success<TimeZoneInfo> success).ShouldBeTrue();
		success.Value.Id.ShouldBe("Europe/London");
	}

	[Fact]
	void Should_not_fall_back_to_utc_when_id_is_absent()
	{
		// Absence is absence — not a fallback to UTC or the local zone.
		var actual = TimeZoneParser.ParseRequired("");
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
	}

	[Fact]
	void Should_fail_with_malformed_reason_when_optional_input_is_unrecognized()
	{
		var actual = TimeZoneParser.ParseOptional("Not/A/Zone");
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("TimeZoneInfo");
		failure.Format.ShouldBe("IANA");
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.TimeZoneParserTests"`
Expected: FAIL — build error, `TimeZoneParser` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Primitives/TimeZoneParser.cs`:

```csharp
namespace Norse.Primitives;

/// <summary>
/// Span-based resolver for <see cref="TimeZoneInfo"/>. Resolves untrusted IANA zone-id text
/// against the OS/ICU zone database via
/// <see cref="TimeZoneInfo.TryFindSystemTimeZoneById(string, out TimeZoneInfo?)"/>. Empty or
/// whitespace input is <see cref="ParseFailure.Empty"/> or absent; an unrecognized id is
/// <see cref="ParseFailure.Malformed"/> with <see cref="Failure.Format"/> = <c>"IANA"</c>.
/// Culture-insensitive — no <see cref="IFormatProvider"/>. Off-gateway by construction:
/// <see cref="TimeZoneInfo"/> does not implement <see cref="ISpanParsable{TSelf}"/>.
/// </summary>
/// <remarks>
/// Resolving untrusted boundary text against a known table is parsing — the same way
/// culture-sensitive numeric parsing consults ICU. A zone-id lookup hitting the OS/ICU zone
/// database belongs on the forge. No silent fallback to <see cref="TimeZoneInfo.Local"/> or
/// <see cref="TimeZoneInfo.Utc"/> — a missing or unrecognized zone is a loud failure.
/// </remarks>
public static class TimeZoneParser
{
	const string ExpectedType = nameof(TimeZoneInfo);
	const string IanaLabel = "IANA";

	/// <summary>
	/// Resolves a required IANA zone id. Empty or whitespace input is a
	/// <see cref="ParseFailure.Empty"/> failure; an unrecognized id is
	/// <see cref="ParseFailure.Malformed"/> (<see cref="Failure.Format"/> = <c>"IANA"</c>).
	/// </summary>
	/// <param name="input">The raw zone-id text. A null string converts to the empty span.</param>
	/// <returns>The resolve outcome — never throws on bad input.</returns>
	public static Result<TimeZoneInfo> ParseRequired(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return Resolve(trimmed);
	}

	/// <summary>
	/// Resolves an optional IANA zone id. Empty or whitespace input is absent
	/// (<see langword="null"/>); an unrecognized id is
	/// <see cref="ParseFailure.Malformed"/> (<see cref="Failure.Format"/> = <c>"IANA"</c>).
	/// </summary>
	/// <param name="input">The raw zone-id text. A null string converts to the empty span.</param>
	/// <returns><see langword="null"/> when absent; otherwise the resolve outcome.</returns>
	public static Result<TimeZoneInfo>? ParseOptional(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return Resolve(trimmed);
	}

	static Result<TimeZoneInfo> Resolve(ReadOnlySpan<char> trimmed)
	{
		if (TimeZoneInfo.TryFindSystemTimeZoneById(trimmed.ToString(), out var zone))
			return new Success<TimeZoneInfo>(zone);
		return new Failure(ParseFailure.Malformed, trimmed, ExpectedType, IanaLabel);
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.TimeZoneParserTests"`
Expected: PASS (all green).

- [ ] **Step 5: Build the solution clean**

Run: `dotnet build Svartalfheim.slnx`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Stage and stop for human commit**

```bash
git add src/Primitives/TimeZoneParser.cs tests/Primitives.Tests/TimeZoneParserTests.cs
git diff --cached
```

Suggested message (do **not** commit — the human reviews and commits):
`Forge TimeZoneParser: IANA zone-id resolver with empty and malformed failure semantics`

---

### Task 2: `TemporalFusion`

**Files:**
- Create: `src/Primitives/TemporalFusion.cs`
- Create: `tests/Primitives.Tests/TemporalFusionTests.cs`

**Interfaces:**
- Consumes: `DateOnlyParser.ParseRequired(ReadOnlySpan<char>) → Result<DateOnly>` (existing); `TimeOnlyParser.ParseRequired(ReadOnlySpan<char>) → Result<TimeOnly>` (existing); `TimeZoneParser.ParseRequired(ReadOnlySpan<char>) → Result<TimeZoneInfo>` (Task 1).
- Produces:
  - `TemporalFusion.FuseRequired(ReadOnlySpan<char> date, ReadOnlySpan<char> time, ReadOnlySpan<char> zone) → Result<DateTime>`
  - `TemporalFusion.FuseOptional(ReadOnlySpan<char> date, ReadOnlySpan<char> time, ReadOnlySpan<char> zone) → Result<DateTime>?`

**DST reference dates (US, 2026):**
- Spring-forward gap: `2026-03-08 02:30` in `America/Chicago` — clocks skip 02:00→03:00, so this wall time never existed.
- Fall-back ambiguity: `2026-11-01 01:30` in `America/Chicago` — clocks repeat 02:00→01:00, so this wall time occurs twice.

- [ ] **Step 1: Write the failing tests**

Create `tests/Primitives.Tests/TemporalFusionTests.cs`:

```csharp
namespace Norse.Primitives.Tests;

public sealed class TemporalFusionTests
{
	const string AllWhitespace = " \t\r\n\f ";

	// ── Happy path ──────────────────────────────────────────────────────────

	[Fact]
	void Should_fuse_to_utc_datetime_when_all_inputs_are_valid_standard_time()
	{
		// 2026-01-02 15:04:05 CST (UTC-6) = 2026-01-02 21:04:05 UTC
		var actual = TemporalFusion.FuseRequired("2026-01-02", "15:04:05", "America/Chicago");
		actual.TryGetValue(out Success<DateTime> success).ShouldBeTrue();
		success.Value.Kind.ShouldBe(DateTimeKind.Utc);
		success.Value.ShouldBe(new DateTime(2026, 1, 2, 21, 4, 5, DateTimeKind.Utc));
	}

	[Fact]
	void Should_fuse_to_utc_datetime_when_all_inputs_are_valid_daylight_time()
	{
		// 2026-06-15 10:00:00 CDT (UTC-5) = 2026-06-15 15:00:00 UTC
		// Proves the conversion is date-aware (not a fixed +6 hour offset).
		var actual = TemporalFusion.FuseRequired("2026-06-15", "10:00:00", "America/Chicago");
		actual.TryGetValue(out Success<DateTime> success).ShouldBeTrue();
		success.Value.Kind.ShouldBe(DateTimeKind.Utc);
		success.Value.ShouldBe(new DateTime(2026, 6, 15, 15, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	void Should_fuse_when_optional_and_both_fields_are_present()
	{
		var actual = TemporalFusion.FuseOptional("2026-01-02", "12:00:00", "UTC");
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Success<DateTime> success).ShouldBeTrue();
		success.Value.Kind.ShouldBe(DateTimeKind.Utc);
	}

	// ── DST seam failures ───────────────────────────────────────────────────

	[Fact]
	void Should_fail_with_dst_gap_detail_when_wall_clock_falls_in_spring_forward()
	{
		// 2026-03-08: clocks spring forward from 2:00 to 3:00 AM in America/Chicago.
		// 02:30 never existed — the BCL would throw on ConvertTimeToUtc, so we check first.
		var actual = TemporalFusion.FuseRequired("2026-03-08", "02:30:00", "America/Chicago");
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("DateTime");
		failure.Detail.ShouldBe("DST gap");
		failure.Input.ShouldBe("2026-03-08T02:30 America/Chicago");
	}

	[Fact]
	void Should_fail_with_dst_ambiguous_detail_when_wall_clock_falls_in_fall_back()
	{
		// 2026-11-01: clocks fall back from 2:00 to 1:00 AM in America/Chicago.
		// 01:30 occurs twice — the BCL silently picks standard time; we refuse to guess.
		var actual = TemporalFusion.FuseRequired("2026-11-01", "01:30:00", "America/Chicago");
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("DateTime");
		failure.Detail.ShouldBe("DST ambiguous");
		failure.Input.ShouldBe("2026-11-01T01:30 America/Chicago");
	}

	// ── Sub-failure propagation (first-failure-wins: date → time → zone) ───

	[Fact]
	void Should_propagate_date_failure_verbatim_when_date_is_malformed()
	{
		// All three inputs bad — date is checked first.
		var actual = TemporalFusion.FuseRequired("garbage", "also-bad", "Not/A/Zone");
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("DateOnly");
		failure.Format.ShouldBe("ISO 8601");
	}

	[Fact]
	void Should_propagate_time_failure_verbatim_when_date_is_good_but_time_is_malformed()
	{
		var actual = TemporalFusion.FuseRequired("2026-01-02", "also-bad", "Not/A/Zone");
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("TimeOnly");
		failure.Format.ShouldBe("ISO 8601");
	}

	[Fact]
	void Should_propagate_zone_failure_verbatim_when_date_and_time_are_good_but_zone_is_unrecognized()
	{
		var actual = TemporalFusion.FuseRequired("2026-01-02", "15:04:05", "Not/A/Zone");
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("TimeZoneInfo");
		failure.Format.ShouldBe("IANA");
	}

	// ── Partial input ────────────────────────────────────────────────────────

	[Fact]
	void Should_fail_with_partial_instant_detail_when_date_is_present_but_time_is_absent()
	{
		var actual = TemporalFusion.FuseRequired("2026-01-02", "", "America/Chicago");
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("DateTime");
		failure.Detail.ShouldBe("partial instant");
		failure.Input.ShouldBe("2026-01-02");
	}

	[Fact]
	void Should_fail_with_partial_instant_detail_when_time_is_present_but_date_is_absent()
	{
		var actual = TemporalFusion.FuseRequired("", "15:04:05", "America/Chicago");
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("DateTime");
		failure.Detail.ShouldBe("partial instant");
		failure.Input.ShouldBe("15:04:05");
	}

	[Fact]
	void Should_return_partial_failure_on_optional_door_when_exactly_one_field_is_absent()
	{
		// Optional door: partial is still an error, not absence.
		var actual = TemporalFusion.FuseOptional("2026-01-02", "", "America/Chicago");
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Detail.ShouldBe("partial instant");
		failure.Input.ShouldBe("2026-01-02");
	}

	// ── Absence (both fields empty — zone is not consulted) ─────────────────

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_fail_with_empty_reason_when_required_date_and_time_are_both_absent(string? dateAndTime)
	{
		var actual = TemporalFusion.FuseRequired(dateAndTime, dateAndTime, "America/Chicago");
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
		failure.ExpectedType.ShouldBe("DateTime");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_return_absent_when_optional_date_and_time_are_both_absent(string? dateAndTime) =>
		TemporalFusion.FuseOptional(dateAndTime, dateAndTime, "America/Chicago").HasValue.ShouldBeFalse();

	[Fact]
	void Should_not_consult_zone_when_both_date_and_time_are_absent()
	{
		// An invalid zone with both fields empty must still return Empty/null — not a zone failure.
		TemporalFusion.FuseRequired("", "", "Not/A/Zone")
			.TryGetValue(out Failure required).ShouldBeTrue();
		required.Reason.ShouldBe(ParseFailure.Empty);
		TemporalFusion.FuseOptional("", "", "Not/A/Zone").HasValue.ShouldBeFalse();
	}

	// ── Sentinel guard ───────────────────────────────────────────────────────

	[Fact]
	void Should_propagate_date_sentinel_failure_when_date_is_datetime_minvalue()
	{
		// DateOnlyParser blocks DateOnly.MinValue (0001-01-01) before TemporalFusion reaches
		// its own UTC sentinel guard. The sub-parser is the first line of defense.
		var actual = TemporalFusion.FuseRequired("0001-01-01", "00:00:00", "UTC");
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("DateOnly");
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.TemporalFusionTests"`
Expected: FAIL — build error, `TemporalFusion` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Primitives/TemporalFusion.cs`:

```csharp
namespace Norse.Primitives;

/// <summary>
/// Composition specialist: three <see cref="ReadOnlySpan{T}"/> text inputs (ISO 8601 date, ISO
/// 8601 24-hour time, IANA zone id) → one UTC <see cref="DateTime"/> (<see cref="DateTimeKind.Utc"/>).
/// </summary>
/// <remarks>
/// <para>
/// Each input is parsed independently through the established ISO-canonical doors —
/// <see cref="DateOnlyParser.ParseRequired"/>, <see cref="TimeOnlyParser.ParseRequired"/>,
/// <see cref="TimeZoneParser.ParseRequired"/> — in the documented date → time → zone order;
/// the first sub-parse that fails returns its <see cref="Failure"/> verbatim. Both DST seams
/// are checked before the BCL conversion: a spring-forward gap is
/// <see cref="ParseFailure.Malformed"/> with <see cref="Failure.Detail"/> = <c>"DST gap"</c>;
/// a fall-back ambiguity is <see cref="ParseFailure.Malformed"/> with
/// <see cref="Failure.Detail"/> = <c>"DST ambiguous"</c>. The BCL's silent standard-time pick
/// for an ambiguous wall-clock never occurs.
/// </para>
/// <para>
/// The sentinel guard (§4 of the temporal-parsers spec) applies to the fused result:
/// <see cref="DateTime.MinValue"/>/<see cref="DateTime.MaxValue"/> are
/// <see cref="ParseFailure.Malformed"/>. Culture-insensitive — no
/// <see cref="IFormatProvider"/>. Off-gateway (no new branch in <see cref="Parser"/>).
/// </para>
/// <para>
/// Optionality is evaluated on the date and time fields only — the zone is infrastructure,
/// never the thing that makes a value absent. Both fields empty ⇒ absent; exactly one empty ⇒
/// <see cref="ParseFailure.Malformed"/> (<see cref="Failure.Detail"/> = <c>"partial instant"</c>).
/// </para>
/// </remarks>
public static class TemporalFusion
{
	const string ExpectedType = nameof(DateTime);

	/// <summary>
	/// Fuses an ISO date, an ISO time, and an IANA zone id into a UTC <see cref="DateTime"/>.
	/// Both fields empty ⇒ <see cref="ParseFailure.Empty"/>; exactly one empty ⇒
	/// <see cref="ParseFailure.Malformed"/> <c>Detail = "partial instant"</c>; sub-parse failures
	/// propagate verbatim in the order date → time → zone; DST gap ⇒ <c>Detail = "DST gap"</c>;
	/// DST ambiguity ⇒ <c>Detail = "DST ambiguous"</c>.
	/// </summary>
	/// <param name="date">The ISO 8601 <c>yyyy-MM-dd</c> text. A null string converts to the empty span.</param>
	/// <param name="time">The ISO 8601 24-hour time text. A null string converts to the empty span.</param>
	/// <param name="zone">The IANA zone id text. A null string converts to the empty span.</param>
	/// <returns>The fuse outcome — never throws on bad input.</returns>
	public static Result<DateTime> FuseRequired(ReadOnlySpan<char> date, ReadOnlySpan<char> time, ReadOnlySpan<char> zone)
	{
		var dateTrimmed = date.Trim();
		var timeTrimmed = time.Trim();
		if (dateTrimmed.IsEmpty && timeTrimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		if (dateTrimmed.IsEmpty || timeTrimmed.IsEmpty)
			return new Failure(ParseFailure.Malformed, dateTrimmed.IsEmpty ? timeTrimmed : dateTrimmed, ExpectedType, null, "partial instant");
		return Fuse(date, time, zone);
	}

	/// <summary>
	/// Fuses an ISO date, an ISO time, and an IANA zone id into an optional UTC <see cref="DateTime"/>.
	/// Both fields empty ⇒ absent (<see langword="null"/>); exactly one empty ⇒
	/// <see cref="ParseFailure.Malformed"/> <c>Detail = "partial instant"</c>; sub-parse failures
	/// propagate verbatim in the order date → time → zone; DST gap ⇒ <c>Detail = "DST gap"</c>;
	/// DST ambiguity ⇒ <c>Detail = "DST ambiguous"</c>.
	/// </summary>
	/// <param name="date">The ISO 8601 <c>yyyy-MM-dd</c> text. A null string converts to the empty span.</param>
	/// <param name="time">The ISO 8601 24-hour time text. A null string converts to the empty span.</param>
	/// <param name="zone">The IANA zone id text. A null string converts to the empty span.</param>
	/// <returns><see langword="null"/> when both date and time are absent; otherwise the fuse outcome.</returns>
	public static Result<DateTime>? FuseOptional(ReadOnlySpan<char> date, ReadOnlySpan<char> time, ReadOnlySpan<char> zone)
	{
		var dateTrimmed = date.Trim();
		var timeTrimmed = time.Trim();
		if (dateTrimmed.IsEmpty && timeTrimmed.IsEmpty)
			return null;
		if (dateTrimmed.IsEmpty || timeTrimmed.IsEmpty)
			return new Failure(ParseFailure.Malformed, dateTrimmed.IsEmpty ? timeTrimmed : dateTrimmed, ExpectedType, null, "partial instant");
		return Fuse(date, time, zone);
	}

	static Result<DateTime> Fuse(ReadOnlySpan<char> date, ReadOnlySpan<char> time, ReadOnlySpan<char> zone)
	{
		var dateResult = DateOnlyParser.ParseRequired(date);
		if (!dateResult.TryGetValue(out Success<DateOnly> dateSuccess))
		{
			dateResult.TryGetValue(out Failure dateFailure);
			return dateFailure;
		}
		var timeResult = TimeOnlyParser.ParseRequired(time);
		if (!timeResult.TryGetValue(out Success<TimeOnly> timeSuccess))
		{
			timeResult.TryGetValue(out Failure timeFailure);
			return timeFailure;
		}
		var zoneResult = TimeZoneParser.ParseRequired(zone);
		if (!zoneResult.TryGetValue(out Success<TimeZoneInfo> zoneSuccess))
		{
			zoneResult.TryGetValue(out Failure zoneFailure);
			return zoneFailure;
		}
		return ConvertToUtc(dateSuccess.Value, timeSuccess.Value, zoneSuccess.Value);
	}

	static Result<DateTime> ConvertToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
	{
		var wall = date.ToDateTime(time, DateTimeKind.Unspecified);
		var compositeInput = $"{wall:yyyy-MM-ddTHH:mm} {zone.Id}";
		if (zone.IsInvalidTime(wall))
			return new Failure(ParseFailure.Malformed, compositeInput, ExpectedType, null, "DST gap");
		if (zone.IsAmbiguousTime(wall))
			return new Failure(ParseFailure.Malformed, compositeInput, ExpectedType, null, "DST ambiguous");
		var utc = TimeZoneInfo.ConvertTimeToUtc(wall, zone);
		if (utc == DateTime.MinValue || utc == DateTime.MaxValue)
			return new Failure(ParseFailure.Malformed, compositeInput, ExpectedType);
		return new Success<DateTime>(utc);
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.TemporalFusionTests"`
Expected: PASS (all green).

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test Svartalfheim.slnx`
Expected: every test green — all prior tests (`BooleanParserTests`, `IntegerParserTests`, `RealParserTests`, `CharParserTests`, `GuidParserTests`, all five temporal parser test classes, `ParserTests`, `ResultTests`, `ResultLawTests`, `FailureTests`) plus the two new classes.

- [ ] **Step 6: Build the solution clean**

Run: `dotnet build Svartalfheim.slnx`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 7: Stage and stop for human commit**

```bash
git add src/Primitives/TemporalFusion.cs tests/Primitives.Tests/TemporalFusionTests.cs
git diff --cached
```

Suggested message: `Forge TemporalFusion: DST-safe composition of ISO date, time, and IANA zone to UTC DateTime`

---

### Task 3: AOT smoke extension

**Files:**
- Modify: `tests/smoke/Primitives.Aot.Smoke/Program.cs`

**Interfaces:**
- Consumes: `TimeZoneParser.ParseRequired` and `TemporalFusion.FuseRequired` (Tasks 1 and 2).

- [ ] **Step 1: Add the probes**

In `tests/smoke/Primitives.Aot.Smoke/Program.cs`, after the existing `Check("declared unix epoch parses off-gateway", ...)` block and before the `if (failures > 0)` block, add:

```csharp
Check("TimeZoneParser resolves a known IANA id off-gateway", () =>
	TimeZoneParser.ParseRequired("America/Chicago").TryGetValue(out Success<TimeZoneInfo> _));

Check("TemporalFusion fuses ISO date, time, and IANA zone to UTC", () =>
	TemporalFusion.FuseRequired("2026-06-15", "10:00:00", "America/Chicago")
		.TryGetValue(out Success<DateTime> fused)
		&& fused.Value.Kind == DateTimeKind.Utc
		&& fused.Value == new DateTime(2026, 6, 15, 15, 0, 0, DateTimeKind.Utc));
```

- [ ] **Step 2: Publish the AOT smoke and run the native executable**

Run: `dotnet publish tests/smoke/Primitives.Aot.Smoke -c Release`
Expected: publish succeeds with **zero** AOT/trim warnings.

Then run the produced native executable printed by the publish step (under `tests/smoke/Primitives.Aot.Smoke/bin/Release/net11.0/<rid>/publish/`).
Expected: prints `ok` for every check, then `AOT smoke passed: the pathway survives native compilation.`, and exits 0.

- [ ] **Step 3: Build the solution clean one final time**

Run: `dotnet build Svartalfheim.slnx`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 4: Stage and stop for human commit**

```bash
git add tests/smoke/Primitives.Aot.Smoke/Program.cs
git diff --cached
```

Suggested message: `Extend AOT smoke: TimeZoneParser and TemporalFusion survive native compilation`

---

## Self-Review

**Spec coverage:**

| Spec section | Task |
|---|---|
| §3 `TimeZoneParser` — `TryFindSystemTimeZoneById`, off-gateway, no provider, empty/malformed | Task 1 |
| §3 `Format = "IANA"` on malformed, no silent fallback to local/UTC | Task 1 tests |
| §4 `FuseRequired`/`FuseOptional` — three spans, `Result<DateTime>`/`Result<DateTime>?`, no provider | Task 2 |
| §4 Algorithm — date→time→zone parse order, first-failure-wins verbatim | Task 2 `Fuse()` |
| §4 `wall = date.ToDateTime(time, DateTimeKind.Unspecified)` | Task 2 `Convert()` |
| §5 Gap check (`IsInvalidTime`) before conversion → `Detail = "DST gap"` | Task 2 `ConvertToUtc()` |
| §5 Ambiguity check (`IsAmbiguousTime`) before conversion → `Detail = "DST ambiguous"` | Task 2 `ConvertToUtc()` |
| §5 BCL silent standard-time pick proven not to occur | Task 2 test `Should_fail_with_dst_ambiguous_detail...` |
| §4 Sentinel guard on fused UTC result (MinValue/MaxValue → Malformed) | Task 2 `ConvertToUtc()` + test |
| §4 `FuseOptional` partial rule — both empty → null, one empty → Malformed partial instant | Task 2 |
| §6 `ParseFailure` unchanged, Detail stable tokens | Task 2 impl |
| §7 No provider — ISO canonical doors called directly (no gateway routing) | Task 2 (no `Parser.cs` changes) |
| §8 No new files beyond the five listed | File map |
| §9 Test matrix — happy path (std+DST), seams, propagation, partial, absence, sentinel | Task 2 tests |
| §9 AOT smoke extension | Task 3 |
| §10 AC 1–8 | Tasks 1–3 |

**Placeholder scan:** No TBD/TODO. Every step shows complete code. Every test step shows real assertions. Every command shows expected output.

**Type consistency:**
- `TimeZoneParser.ParseRequired(ReadOnlySpan<char>) → Result<TimeZoneInfo>` — matches Task 1 impl and Task 2 `Fuse()` call site.
- `TimeZoneParser.ParseOptional(ReadOnlySpan<char>) → Result<TimeZoneInfo>?` — matches Task 1 impl.
- `TemporalFusion.FuseRequired(ReadOnlySpan<char>, ReadOnlySpan<char>, ReadOnlySpan<char>) → Result<DateTime>` — matches Task 2 impl and Task 3 smoke probe.
- `TemporalFusion.FuseOptional(...)  → Result<DateTime>?` — matches Task 2 impl.
- `Failure.Detail` stable tokens `"DST gap"`, `"DST ambiguous"`, `"partial instant"` — identical across impl and tests.
- `Failure.ExpectedType` values — `"TimeZoneInfo"` in Task 1, `"DateTime"` in Task 2 (including partial and sentinel), `"DateOnly"`/`"TimeOnly"` in sub-failure propagation tests (from the downstream parsers' own failures).
- `compositeInput` format `yyyy-MM-ddTHH:mm` — matches the spec example `"2026-03-08T02:30 America/Chicago"` and the test assertions.
