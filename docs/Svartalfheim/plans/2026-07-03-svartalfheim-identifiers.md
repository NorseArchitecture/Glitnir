# Svartalfheim — Identifiers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `Norse.Primitives.Identifiers` to Svartalfheim — a `SequentialGuid` struct (RFC 9562 UUIDv7, with a byte-order tag and bidirectional shuffle so it sorts correctly under SQL Server's `uniqueidentifier` comparison as well as Postgres's plain byte-order comparison) and a `DeterministicGuid` struct (RFC 9562 UUIDv5, for computing lookup-table foreign keys in memory).

**Architecture:** Two independent value types behind a minimal `INorseGuid` marker interface. `SequentialGuid`'s byte-level generation and SQL Server byte-order shuffle live in an internal, independently-testable static class (`SequentialGuidBytes`) so the tricky bit math has its own correctness oracle separate from the public API surface. Both public structs are narrow by design — no parsing, no formatting, no operators — because untrusted input never reaches these types directly (see the design doc, §3.1).

**Tech Stack:** .NET 11 (preview), C# `LangVersion=preview`, xUnit v3 + Shouldly on Microsoft.Testing.Platform, `System.Data.SqlTypes.SqlGuid` as the real SQL Server comparison oracle (verified AOT/trim-safe as part of this plan's research — see Task 3).

**Spec:** `docs/Svartalfheim/specs/2026-07-03-svartalfheim-identifiers-design.md` (this plan implements it in full; no open questions remain).

## Global Constraints

- **.NET 11 only, no multi-targeting.** `global.json` pins SDK `11.0.100-`, `rollForward: latestFeature`. Go straight to modern APIs (`Span<byte>`, `stackalloc`, `Guid` big-endian constructor) — no `#if NET_x_OR_GREATER` conditionals, no legacy fallback branches.
- **Warnings are errors.** `TreatWarningsAsErrors=true`, `WarningLevel=9999`, `EnforceCodeStyleInBuild=true` (root `Directory.Build.props`). A single warning fails the build.
- **`IsAotCompatible=true`** (src `Directory.Build.props`) — every new type must stay AOT/trim-clean.
- **Tabs for indentation.** `var` for return assignments; explicit type + `new()` for construction. Accessibility modifiers `omit_if_default`.
- **XML docs are mandatory on every public `src` member** — `CS1591` is an error in `src` (not in `tests`, which suppresses it).
- **Enums always carry explicit numeric values**, `0 = Unspecified` sentinel where a "forgot to set this" state is possible (matches `ParseFailure` in this same project).
- **Test naming:** `Should_{behavior}_when_{condition}`. Test classes `public sealed`. Test methods omit the access modifier. `Shouldly`/`Xunit` usings are global via `tests/Directory.Build.props` — never add them per file.
- **`sealed` by default** for every new type unless a concrete derived type exists (none do here).
- **No automatic git commits.** Every task below ends with "stage and show the diff" — never `git commit`. The human commits.
- **Build/test commands** (run from the `Svartalfheim` submodule root, i.e. `Bifrost/Svartalfheim`):
  - `dotnet build Svartalfheim.slnx` — warnings-as-errors.
  - `dotnet test tests/Primitives.Tests -- --filter-class "Norse.Primitives.Tests.Identifiers.ClassName"` — **VSTest `--filter` does not work** on this MTP-based test setup; the `--filter-class` form above is the one that works.
  - AOT smoke (Task 7 only): `dotnet publish tests/smoke/Primitives.Aot.Smoke -c Release`, then run the published native executable — zero AOT warnings and exit code 0 required.

---

## File Structure

New sub-namespace `Norse.Primitives.Identifiers`, physically grouped under `src/Primitives/Identifiers/` (mirrored under `tests/Primitives.Tests/Identifiers/` for tests):

| File | Responsibility |
|---|---|
| `src/Primitives/Identifiers/INorseGuid.cs` | Minimal marker interface (`Guid Value { get; }`) |
| `src/Primitives/Identifiers/GuidByteOrder.cs` | `Rfc9562`/`SqlServer` tag enum |
| `src/Primitives/Identifiers/GuidVersionBits.cs` | Internal: checks RFC 9562 version/variant bits on a constructed `Guid`, shared by both public structs |
| `src/Primitives/Identifiers/SequentialGuidBytes.cs` | Internal: RFC 9562 UUIDv7 byte generation + the bidirectional SQL Server byte-order shuffle — the byte math, kept separate from the public API |
| `src/Primitives/Identifiers/SequentialGuid.cs` | Public struct: generation, wrap constructor, `Order`, `Timestamp`, `ToSqlOrder`/`ToRfcOrder`, `Equals`/`CompareTo`, batch generation |
| `src/Primitives/Identifiers/DeterministicGuid.cs` | Public struct: RFC 9562 UUIDv5 generation from namespace + name, well-known namespaces |

Tests mirror this 1:1 under `tests/Primitives.Tests/Identifiers/`, namespace `Norse.Primitives.Tests.Identifiers`.

---

### Task 1: Foundational types — `INorseGuid`, `GuidByteOrder`, `GuidVersionBits`

**Files:**
- Create: `src/Primitives/Identifiers/INorseGuid.cs`
- Create: `src/Primitives/Identifiers/GuidByteOrder.cs`
- Create: `src/Primitives/Identifiers/GuidVersionBits.cs`
- Test: `tests/Primitives.Tests/Identifiers/GuidByteOrderTests.cs`
- Test: `tests/Primitives.Tests/Identifiers/GuidVersionBitsTests.cs`

**Interfaces:**
- Produces: `INorseGuid.Value : Guid` (get-only). `GuidByteOrder` enum with members `Unspecified = 0`, `Rfc9562 = 1`, `SqlServer = 2`. `internal static bool GuidVersionBits.HasVersionAndVariant(Guid value, byte version)` — checks the RFC 9562 version nibble and variant bits on the given `Guid`'s **native** byte layout (i.e. what `Guid.TryWriteBytes(Span<byte>)` produces without a `bigEndian` argument): native byte index 7's top nibble must equal `version`, and native byte index 8's top two bits must equal `10` (0x80 after masking with `0xC0`).

- [ ] **Step 1: Write the failing tests**

Create `tests/Primitives.Tests/Identifiers/GuidByteOrderTests.cs`:

```csharp
namespace Norse.Primitives.Tests.Identifiers;

public sealed class GuidByteOrderTests
{
	[Fact]
	void Should_have_fixed_numeric_values_when_enum_is_inspected()
	{
		// Explicit values are load-bearing: this enum is expected to eventually appear as a
		// persisted integer, and an accidental renumbering must be a visible diff, not a silent bug.
		((int)GuidByteOrder.Unspecified).ShouldBe(0);
		((int)GuidByteOrder.Rfc9562).ShouldBe(1);
		((int)GuidByteOrder.SqlServer).ShouldBe(2);
	}
}
```

Create `tests/Primitives.Tests/Identifiers/GuidVersionBitsTests.cs`:

```csharp
using Norse.Primitives.Identifiers;

namespace Norse.Primitives.Tests.Identifiers;

public sealed class GuidVersionBitsTests
{
	[Fact]
	void Should_return_true_when_version_and_variant_match()
	{
		// Hand-built native-layout bytes: byte[7] top nibble = 7 (version), byte[8] top 2 bits = 10 (variant).
		var bytes = new byte[16];
		bytes[7] = 0x70;
		bytes[8] = 0x80;
		var value = new Guid(bytes);

		GuidVersionBits.HasVersionAndVariant(value, 7).ShouldBeTrue();
	}

	[Fact]
	void Should_return_false_when_version_does_not_match()
	{
		var bytes = new byte[16];
		bytes[7] = 0x50; // version 5, not 7
		bytes[8] = 0x80;
		var value = new Guid(bytes);

		GuidVersionBits.HasVersionAndVariant(value, 7).ShouldBeFalse();
	}

	[Fact]
	void Should_return_false_when_variant_bits_are_not_rfc9562()
	{
		var bytes = new byte[16];
		bytes[7] = 0x70;
		bytes[8] = 0x00; // variant bits 00, not the RFC 9562 10xxxxxx
		var value = new Guid(bytes);

		GuidVersionBits.HasVersionAndVariant(value, 7).ShouldBeFalse();
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "Norse.Primitives.Tests.Identifiers.GuidByteOrderTests"`
Expected: FAIL to compile — `GuidByteOrder` does not exist.

Run: `dotnet test tests/Primitives.Tests -- --filter-class "Norse.Primitives.Tests.Identifiers.GuidVersionBitsTests"`
Expected: FAIL to compile — `GuidVersionBits` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Primitives/Identifiers/INorseGuid.cs`:

```csharp
namespace Norse.Primitives.Identifiers;

/// <summary>
/// Marker shared by every well-formed, version/variant-guaranteed Norse identifier type.
/// </summary>
public interface INorseGuid
{
	/// <summary>Gets the underlying <see cref="Guid"/> value.</summary>
	Guid Value { get; }
}
```

Create `src/Primitives/Identifiers/GuidByteOrder.cs`:

```csharp
namespace Norse.Primitives.Identifiers;

/// <summary>
/// Identifies which byte layout a <see cref="SequentialGuid"/>'s <see cref="Guid"/> value is currently in.
/// </summary>
/// <remarks>
/// Not detectable from the bits alone by design — the RFC 9562 version nibble and variant bits sit at
/// identical native byte offsets in both layouts, so an instance always carries its own tag rather than
/// relying on a runtime heuristic to guess.
/// </remarks>
public enum GuidByteOrder
{
	/// <summary>Sentinel CLR default — never a valid argument; guards against <c>default(GuidByteOrder)</c>.</summary>
	Unspecified = 0,

	/// <summary>
	/// RFC 9562 byte order — the layout <see cref="Guid(ReadOnlySpan{byte}, bool)"/> with <c>bigEndian: true</c>
	/// produces, and the layout every newly generated <see cref="SequentialGuid"/> starts in.
	/// </summary>
	Rfc9562 = 1,

	/// <summary>
	/// Byte order that sorts correctly under <see cref="System.Data.SqlTypes.SqlGuid"/> comparison
	/// (SQL Server's <c>uniqueidentifier</c> ordering).
	/// </summary>
	SqlServer = 2
}
```

Create `src/Primitives/Identifiers/GuidVersionBits.cs`:

```csharp
namespace Norse.Primitives.Identifiers;

/// <summary>
/// Checks the RFC 9562 version and variant bits on an already-constructed <see cref="Guid"/>, using
/// .NET's native (non-big-endian) byte layout — the same layout <see cref="Guid.TryWriteBytes(Span{byte})"/>
/// produces without a byte-order argument.
/// </summary>
static class GuidVersionBits
{
	/// <summary>
	/// Returns <see langword="true"/> when <paramref name="value"/> carries the RFC 9562 version nibble
	/// equal to <paramref name="version"/> and the RFC 9562 variant bits (top two bits <c>10</c>).
	/// </summary>
	internal static bool HasVersionAndVariant(Guid value, byte version)
	{
		Span<byte> native = stackalloc byte[16];
		value.TryWriteBytes(native);
		return (native[7] >> 4) == version && (native[8] & 0xC0) == 0x80;
	}
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "Norse.Primitives.Tests.Identifiers.GuidByteOrderTests"`
Expected: PASS

Run: `dotnet test tests/Primitives.Tests -- --filter-class "Norse.Primitives.Tests.Identifiers.GuidVersionBitsTests"`
Expected: PASS

- [ ] **Step 5: Build the whole solution to catch analyzer/warning issues**

Run: `dotnet build Svartalfheim.slnx`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 6: Stage and show the diff**

```bash
git add src/Primitives/Identifiers/INorseGuid.cs src/Primitives/Identifiers/GuidByteOrder.cs src/Primitives/Identifiers/GuidVersionBits.cs tests/Primitives.Tests/Identifiers/GuidByteOrderTests.cs tests/Primitives.Tests/Identifiers/GuidVersionBitsTests.cs
git status --short
git diff --cached --stat
```

Do not run `git commit` — stage only and show the diff for human review, per this repo's process rule.

---

### Task 2: `SequentialGuidBytes.GenerateRfc` — pure RFC 9562 UUIDv7 generation

**Files:**
- Create: `src/Primitives/Identifiers/SequentialGuidBytes.cs`
- Test: `tests/Primitives.Tests/Identifiers/SequentialGuidBytesTests.cs`

**Interfaces:**
- Consumes: `GuidVersionBits.HasVersionAndVariant(Guid, byte)` (Task 1).
- Produces: `internal static Guid SequentialGuidBytes.GenerateRfc(long unixMilliseconds, int counter, ReadOnlySpan<byte> entropy)`. Pure and deterministic — no clock or RNG access, so every input is caller-supplied. Throws `ArgumentOutOfRangeException` if `unixMilliseconds` is negative or exceeds 48 bits; throws `ArgumentException` if `entropy.Length != 6`. Only the low 26 bits of `counter` are used (silently masked — this is an internal helper whose only caller, `SequentialGuid`, already masks the counter before calling).

**RFC 9562 UUIDv7 layout, 16 bytes** (documented as XML remarks on the type — this is the layout every later task in this plan builds on):
```
[0..6)   unix_ts_ms       48 bits, big-endian
[6]      ver(4 bits) | ctrHi nibble(4 bits)
[7]      ctrHi low byte                        \_ rand_a = 12-bit counter chunk
[8]      var(2 bits) | ctrLo(6 bits)
[9]      ctrLo low byte                        \_ rand_b-start = 14-bit counter chunk
[10..16) entropy          48 bits, random
```

- [ ] **Step 1: Write the failing tests**

Create `tests/Primitives.Tests/Identifiers/SequentialGuidBytesTests.cs`:

```csharp
using Norse.Primitives.Identifiers;

namespace Norse.Primitives.Tests.Identifiers;

public sealed class SequentialGuidBytesTests
{
	[Fact]
	void Should_produce_a_well_formed_version7_guid_when_generated()
	{
		var value = SequentialGuidBytes.GenerateRfc(1_800_000_000_000L, 42, new byte[6]);

		GuidVersionBits.HasVersionAndVariant(value, 7).ShouldBeTrue();
	}

	[Theory]
	[InlineData(-1L)]
	[InlineData(0x0001_0000_0000_0000L)]
	void Should_throw_when_timestamp_is_out_of_the_48_bit_range(long unixMilliseconds)
	{
		Should.Throw<ArgumentOutOfRangeException>(() =>
			SequentialGuidBytes.GenerateRfc(unixMilliseconds, 0, new byte[6]));
	}

	[Fact]
	void Should_throw_when_entropy_is_not_six_bytes()
	{
		Should.Throw<ArgumentException>(() =>
			SequentialGuidBytes.GenerateRfc(0, 0, new byte[5]));
	}

	[Fact]
	void Should_embed_the_exact_timestamp_and_counter_supplied()
	{
		// Read the generated bytes back out manually (native layout) to prove the RFC layout
		// documented on SequentialGuidBytes is exactly what GenerateRfc produces.
		const long unixMilliseconds = 1_750_000_000_123L;
		const int counter = 0x2A_BCDE; // arbitrary 26-bit value within range

		var value = SequentialGuidBytes.GenerateRfc(unixMilliseconds, counter, [1, 2, 3, 4, 5, 6]);

		Span<byte> native = stackalloc byte[16];
		value.TryWriteBytes(native);

		// native[3,2,1,0] = rfc timestamp bytes 0..3 (Data1 4-byte reversal); native[5,4] = rfc bytes 4,5.
		var readBackMs =
			((long)native[3] << 40) | ((long)native[2] << 32) | ((long)native[1] << 24) |
			((long)native[0] << 16) | ((long)native[5] << 8) | native[4];
		readBackMs.ShouldBe(unixMilliseconds);

		// native[7] bottom nibble + native[6] = rand_a (ctrHi, top 12 bits of the 26-bit counter);
		// native[8] bottom 6 bits + native[9] = rand_b-start (ctrLo, bottom 14 bits).
		var ctrHi = ((native[7] & 0x0F) << 8) | native[6];
		var ctrLo = ((native[8] & 0x3F) << 8) | native[9];
		var readBackCounter = (ctrHi << 14) | ctrLo;
		readBackCounter.ShouldBe(counter & 0x3FFFFFF);
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "Norse.Primitives.Tests.Identifiers.SequentialGuidBytesTests"`
Expected: FAIL to compile — `SequentialGuidBytes` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Primitives/Identifiers/SequentialGuidBytes.cs`:

```csharp
using System.Runtime.CompilerServices;

namespace Norse.Primitives.Identifiers;

/// <summary>
/// RFC 9562 UUID version 7 byte-level generation and the bidirectional SQL Server byte-order shuffle
/// that <see cref="SequentialGuid"/> wraps. Kept separate from the public struct so the byte math can be
/// exercised directly by its own correctness-oracle tests without going through timestamp/RNG capture.
/// </summary>
/// <remarks>
/// <para>
/// RFC 9562 layout (16 bytes): <c>[0..6)</c> unix_ts_ms (48 bits, big-endian) · <c>[6]</c> version nibble
/// (top) + counter-high nibble (bottom) · <c>[7]</c> counter-high low byte (rand_a = 12-bit counter chunk)
/// · <c>[8]</c> variant bits (top 2) + counter-low top 6 bits · <c>[9]</c> counter-low low byte
/// (rand_b-start = 14-bit counter chunk) · <c>[10..16)</c> entropy (48 bits, random).
/// </para>
/// </remarks>
static class SequentialGuidBytes
{
	/// <summary>
	/// Builds a new RFC 9562-ordered UUID version 7 from an explicit timestamp, counter, and entropy —
	/// no clock or RNG access, so callers (and tests) can pin every input.
	/// </summary>
	/// <param name="unixMilliseconds">Milliseconds since the Unix epoch; must fit in 48 bits.</param>
	/// <param name="counter">The monotonic counter value; only the low 26 bits are used.</param>
	/// <param name="entropy">Exactly 6 bytes of random tail.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="unixMilliseconds"/> is negative or exceeds 48 bits.</exception>
	/// <exception cref="ArgumentException"><paramref name="entropy"/> is not exactly 6 bytes.</exception>
	[SkipLocalsInit]
	internal static Guid GenerateRfc(long unixMilliseconds, int counter, ReadOnlySpan<byte> entropy)
	{
		if (unixMilliseconds is < 0 or > 0x0000_FFFF_FFFF_FFFF)
			throw new ArgumentOutOfRangeException(nameof(unixMilliseconds),
				"Unix millisecond timestamp must be non-negative and fit within 48 bits.");
		if (entropy.Length != 6)
			throw new ArgumentException("Entropy must be exactly 6 bytes.", nameof(entropy));

		var maskedCounter = counter & 0x3FFFFFF;

		Span<byte> bytes = stackalloc byte[16];
		entropy.CopyTo(bytes[10..]);

		bytes[0] = (byte)(unixMilliseconds >> 40);
		bytes[1] = (byte)(unixMilliseconds >> 32);
		bytes[2] = (byte)(unixMilliseconds >> 24);
		bytes[3] = (byte)(unixMilliseconds >> 16);
		bytes[4] = (byte)(unixMilliseconds >> 8);
		bytes[5] = (byte)unixMilliseconds;

		bytes[6] = (byte)(maskedCounter >> 22);
		bytes[7] = (byte)((maskedCounter >> 14) & 0xFF);
		bytes[8] = (byte)((maskedCounter >> 8) & 0x3F);
		bytes[9] = (byte)(maskedCounter & 0xFF);

		bytes[6] = (byte)((bytes[6] & 0x0F) | (7 << 4));
		bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

		return new Guid(bytes, bigEndian: true);
	}
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "Norse.Primitives.Tests.Identifiers.SequentialGuidBytesTests"`
Expected: PASS (4 tests)

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build Svartalfheim.slnx`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 6: Stage and show the diff**

```bash
git add src/Primitives/Identifiers/SequentialGuidBytes.cs tests/Primitives.Tests/Identifiers/SequentialGuidBytesTests.cs
git status --short
git diff --cached --stat
```

Do not run `git commit`.

---

### Task 3: `SequentialGuidBytes` — SQL Server byte-order shuffle + correctness oracle

This is the task that proves the whole point of this feature: a UUIDv7 reordered so it sorts correctly under **actual SQL Server semantics**, not a guess at them. The byte math below was derived and empirically verified against the real `System.Data.SqlTypes.SqlGuid` type before this plan was written (round-trip fidelity, a counter-carry-boundary sort test, and a timestamp-boundary sort test all passed against 2000+ generated values, cross-checked against an independent test fixture already in the `SequentialGuid` prior-art repo). This step reproduces that as permanent, committed tests.

**Empirically confirmed facts this design relies on** (verified via a live probe against `System.Data.SqlTypes.SqlGuid`, and independently cross-checked against `SequentialGuidTests.SortedSqlGuidList` in the prior-art repo — both agree exactly):

- **Native ↔ RFC byte index mapping:** `native[0..4) = rfc[3,2,1,0]` (Data1 4-byte reversal), `native[4,5] = rfc[5,4]` (Data2 reversal), `native[6,7] = rfc[7,6]` (Data3 reversal), `native[8..16) = rfc[8..16)` (Data4, unchanged).
- **`SqlGuid` comparison significance order, most→least significant native byte index:** `[10,11,12,13,14,15] > [8,9] > [6,7] > [4,5] > [0,1,2,3]`.
- Group `[10..16)` has 48-bit capacity (exactly the timestamp size); group `[8,9]` has 14-bit capacity; group `[6,7]` has 12-bit capacity (together exactly the 26-bit counter size); groups `[4,5]`+`[0..4)` together have 48-bit capacity (exactly the entropy size).
- The version nibble (`native[7]` top nibble) and variant bits (`native[8]` top 2 bits) never move and never change value between the two layouts — SQL Server's significance groups `[6,7]` and `[8,9]` happen to contain them, but since they're constant across every instance, their group placement doesn't affect relative ordering between different instances.
- The 26-bit counter is **re-split 14-high/12-low** for `SqlServer` order (mirrored from the RFC order's 12-high/14-low split), so its more significant half lands in group `[8,9]`, which SQL Server ranks above group `[6,7]`. Without this mirrored repack, ordering would still be correct across millisecond boundaries but could invert at counter carry boundaries (e.g. `0xFFF → 0x1000`) within the same millisecond.

**Files:**
- Modify: `src/Primitives/Identifiers/SequentialGuidBytes.cs` (add `ToSqlOrder`, `ToRfcOrder`, `ExtractTimestamp`)
- Modify: `tests/Primitives.Tests/Identifiers/SequentialGuidBytesTests.cs` (add the correctness-oracle tests)
- Modify: `tests/Primitives.Tests/Primitives.Tests.csproj` — no change needed; `System.Data.SqlTypes.SqlGuid` ships in the shared framework, no additional package reference required (confirmed: a scratch console app referencing it built and published AOT with zero warnings using nothing but the default SDK-style project).

**Interfaces:**
- Consumes: `SequentialGuidBytes.GenerateRfc` (Task 2).
- Produces: `internal static Guid SequentialGuidBytes.ToSqlOrder(Guid rfcOrdered)`, `internal static Guid SequentialGuidBytes.ToRfcOrder(Guid sqlOrdered)`, `internal static DateTime SequentialGuidBytes.ExtractTimestamp(Guid value, GuidByteOrder order)`. `ExtractTimestamp` normalizes `SqlServer`-ordered input to `Rfc9562` first (via `ToRfcOrder`) before reading the timestamp bytes — the timestamp bytes only live at a fixed offset in RFC order.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Primitives.Tests/Identifiers/SequentialGuidBytesTests.cs` (add `using System.Data.SqlTypes;` at the top, and these methods inside the existing `SequentialGuidBytesTests` class):

```csharp
	[Fact]
	void Should_round_trip_through_sql_order_and_back_for_many_generated_values()
	{
		var random = new Random(1);
		for (var trial = 0; trial < 2000; trial++)
		{
			var unixMilliseconds = random.NextInt64(0, 0x0000_FFFF_FFFF_FFFF);
			var counter = random.Next(0, 0x400_0000);
			var entropy = new byte[6];
			random.NextBytes(entropy);

			var rfcGuid = SequentialGuidBytes.GenerateRfc(unixMilliseconds, counter, entropy);
			var sqlGuid = SequentialGuidBytes.ToSqlOrder(rfcGuid);
			var roundTripped = SequentialGuidBytes.ToRfcOrder(sqlGuid);

			roundTripped.ShouldBe(rfcGuid);
		}
	}

	[Fact]
	void Should_keep_version_and_variant_bits_fixed_when_converted_to_sql_order()
	{
		var rfcGuid = SequentialGuidBytes.GenerateRfc(1_800_000_000_000L, 123, [1, 2, 3, 4, 5, 6]);
		var sqlGuid = SequentialGuidBytes.ToSqlOrder(rfcGuid);

		GuidVersionBits.HasVersionAndVariant(rfcGuid, 7).ShouldBeTrue();
		GuidVersionBits.HasVersionAndVariant(sqlGuid, 7).ShouldBeTrue();
	}

	[Fact]
	void Should_sort_correctly_under_sql_server_semantics_across_a_counter_carry_boundary()
	{
		// The counter's mirrored 14/12 repack exists specifically so ordering survives the
		// 12-bit carry boundary (0xFFF -> 0x1000) under SQL Server's comparison, not just plain Guid's.
		const long fixedMs = 1_800_000_000_000L;
		var random = new Random(2);
		var sequence = new List<(int Counter, SqlGuid Sql)>();
		for (var counter = 4090; counter <= 4100; counter++)
		{
			var entropy = new byte[6];
			random.NextBytes(entropy);
			var rfcGuid = SequentialGuidBytes.GenerateRfc(fixedMs, counter, entropy);
			var sqlGuid = SequentialGuidBytes.ToSqlOrder(rfcGuid);
			sequence.Add((counter, new SqlGuid(sqlGuid)));
		}

		var byCounter = sequence.OrderBy(x => x.Counter).Select(x => x.Counter).ToArray();
		var bySqlOrder = sequence.OrderBy(x => x.Sql).Select(x => x.Counter).ToArray();

		bySqlOrder.ShouldBe(byCounter);
	}

	[Fact]
	void Should_sort_correctly_under_sql_server_semantics_across_a_millisecond_boundary()
	{
		const long fixedMs = 1_800_000_000_000L;
		var random = new Random(3);
		var sequence = new List<(int Index, SqlGuid Sql)>();
		var index = 0;
		for (var msOffset = 0; msOffset < 5; msOffset++)
		{
			for (var counter = 0; counter < 3; counter++)
			{
				var entropy = new byte[6];
				random.NextBytes(entropy);
				var rfcGuid = SequentialGuidBytes.GenerateRfc(fixedMs + msOffset, counter, entropy);
				var sqlGuid = SequentialGuidBytes.ToSqlOrder(rfcGuid);
				sequence.Add((index++, new SqlGuid(sqlGuid)));
			}
		}

		var expected = sequence.Select(x => x.Index).ToArray();
		var actual = sequence.OrderBy(x => x.Sql).Select(x => x.Index).ToArray();

		actual.ShouldBe(expected);
	}

	[Fact]
	void Should_extract_the_matching_timestamp_from_both_byte_orders()
	{
		const long unixMilliseconds = 1_750_000_000_123L;
		var rfcGuid = SequentialGuidBytes.GenerateRfc(unixMilliseconds, 7, [9, 8, 7, 6, 5, 4]);
		var sqlGuid = SequentialGuidBytes.ToSqlOrder(rfcGuid);

		var expected = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).UtcDateTime;

		SequentialGuidBytes.ExtractTimestamp(rfcGuid, GuidByteOrder.Rfc9562).ShouldBe(expected);
		SequentialGuidBytes.ExtractTimestamp(sqlGuid, GuidByteOrder.SqlServer).ShouldBe(expected);
	}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "Norse.Primitives.Tests.Identifiers.SequentialGuidBytesTests"`
Expected: FAIL to compile — `ToSqlOrder`/`ToRfcOrder`/`ExtractTimestamp` do not exist.

- [ ] **Step 3: Write the implementation**

Add to `src/Primitives/Identifiers/SequentialGuidBytes.cs` (inside the `SequentialGuidBytes` class, after `GenerateRfc`):

```csharp
	/// <summary>Converts an <see cref="GuidByteOrder.Rfc9562"/>-ordered value to <see cref="GuidByteOrder.SqlServer"/> order.</summary>
	[SkipLocalsInit]
	internal static Guid ToSqlOrder(Guid rfcOrdered)
	{
		Span<byte> native = stackalloc byte[16];
		rfcOrdered.TryWriteBytes(native);

		var counterHi = ((native[7] & 0x0F) << 8) | native[6];
		var counterLo = ((native[8] & 0x3F) << 8) | native[9];
		var counter = (counterHi << 14) | counterLo;

		var top14 = (counter >> 12) & 0x3FFF;
		var bottom12 = counter & 0xFFF;

		var version = (byte)(native[7] & 0xF0);
		var variant = (byte)(native[8] & 0xC0);

		Span<byte> sql = stackalloc byte[16];
		sql[10] = native[3]; sql[11] = native[2]; sql[12] = native[1]; sql[13] = native[0];
		sql[14] = native[5]; sql[15] = native[4];
		sql[8] = (byte)(variant | ((top14 >> 8) & 0x3F));
		sql[9] = (byte)(top14 & 0xFF);
		sql[7] = (byte)(version | ((bottom12 >> 8) & 0x0F));
		sql[6] = (byte)(bottom12 & 0xFF);
		sql[4] = native[10]; sql[5] = native[11];
		sql[0] = native[12]; sql[1] = native[13]; sql[2] = native[14]; sql[3] = native[15];

		return new Guid(sql);
	}

	/// <summary>Converts a <see cref="GuidByteOrder.SqlServer"/>-ordered value back to <see cref="GuidByteOrder.Rfc9562"/> order.</summary>
	[SkipLocalsInit]
	internal static Guid ToRfcOrder(Guid sqlOrdered)
	{
		Span<byte> sql = stackalloc byte[16];
		sqlOrdered.TryWriteBytes(sql);

		var top14 = ((sql[8] & 0x3F) << 8) | sql[9];
		var bottom12 = ((sql[7] & 0x0F) << 8) | sql[6];
		var counter = (top14 << 12) | bottom12;

		var counterHi = (counter >> 14) & 0xFFF;
		var counterLo = counter & 0x3FFF;

		var version = (byte)(sql[7] & 0xF0);
		var variant = (byte)(sql[8] & 0xC0);

		Span<byte> native = stackalloc byte[16];
		native[3] = sql[10]; native[2] = sql[11]; native[1] = sql[12]; native[0] = sql[13];
		native[5] = sql[14]; native[4] = sql[15];
		native[6] = (byte)(counterHi & 0xFF);
		native[7] = (byte)(version | ((counterHi >> 8) & 0x0F));
		native[8] = (byte)(variant | ((counterLo >> 8) & 0x3F));
		native[9] = (byte)(counterLo & 0xFF);
		native[10] = sql[4]; native[11] = sql[5];
		native[12] = sql[0]; native[13] = sql[1]; native[14] = sql[2]; native[15] = sql[3];

		return new Guid(native);
	}

	/// <summary>
	/// Extracts the embedded 48-bit Unix millisecond timestamp, normalizing to RFC order first if
	/// <paramref name="order"/> is <see cref="GuidByteOrder.SqlServer"/>.
	/// </summary>
	internal static DateTime ExtractTimestamp(Guid value, GuidByteOrder order)
	{
		var rfcValue = order == GuidByteOrder.SqlServer ? ToRfcOrder(value) : value;

		Span<byte> native = stackalloc byte[16];
		rfcValue.TryWriteBytes(native);

		var unixMilliseconds =
			((long)native[3] << 40) | ((long)native[2] << 32) | ((long)native[1] << 24) |
			((long)native[0] << 16) | ((long)native[5] << 8) | native[4];

		return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).UtcDateTime;
	}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "Norse.Primitives.Tests.Identifiers.SequentialGuidBytesTests"`
Expected: PASS (9 tests total: 4 from Task 2 + 5 new)

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build Svartalfheim.slnx`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 6: Stage and show the diff**

```bash
git add src/Primitives/Identifiers/SequentialGuidBytes.cs tests/Primitives.Tests/Identifiers/SequentialGuidBytesTests.cs
git status --short
git diff --cached --stat
```

Do not run `git commit`.

---

### Task 4: Public `SequentialGuid` struct

**Files:**
- Create: `src/Primitives/Identifiers/SequentialGuid.cs`
- Test: `tests/Primitives.Tests/Identifiers/SequentialGuidTests.cs`

**Interfaces:**
- Consumes: `INorseGuid` (Task 1), `GuidByteOrder` (Task 1), `GuidVersionBits.HasVersionAndVariant` (Task 1), `SequentialGuidBytes.GenerateRfc`/`ToSqlOrder`/`ToRfcOrder`/`ExtractTimestamp` (Tasks 2–3).
- Produces: `public readonly record struct SequentialGuid : INorseGuid, IComparable<SequentialGuid>, IEquatable<SequentialGuid>` with `Guid Value { get; }`, `GuidByteOrder Order { get; }`, `DateTime Timestamp { get; }`, parameterless constructor (generates new, `Order = Rfc9562`), `SequentialGuid(Guid value, GuidByteOrder order)` (validating wrap constructor), `SequentialGuid ToSqlOrder()`, `SequentialGuid ToRfcOrder()`, implicit conversion to `Guid`. This is the public surface Task 5 (batch generation) extends and Task 6 (`DeterministicGuid`) parallels.

- [ ] **Step 1: Write the failing tests**

Create `tests/Primitives.Tests/Identifiers/SequentialGuidTests.cs`:

```csharp
using System.Data.SqlTypes;
using Norse.Primitives.Identifiers;

namespace Norse.Primitives.Tests.Identifiers;

public sealed class SequentialGuidTests
{
	[Fact]
	void Should_generate_a_well_formed_rfc_ordered_value_when_constructed()
	{
		var value = new SequentialGuid();

		value.Order.ShouldBe(GuidByteOrder.Rfc9562);
		GuidVersionBits.HasVersionAndVariant(value.Value, 7).ShouldBeTrue();
	}

	[Fact]
	void Should_embed_the_current_time_when_constructed()
	{
		var before = DateTime.UtcNow;
		var value = new SequentialGuid();
		var after = DateTime.UtcNow;

		value.Timestamp.ShouldBeInRange(before.AddSeconds(-1), after.AddSeconds(1));
	}

	[Theory]
	[InlineData(GuidByteOrder.Rfc9562)]
	[InlineData(GuidByteOrder.SqlServer)]
	void Should_throw_when_wrapped_value_is_not_a_version7_guid(GuidByteOrder order)
	{
		Should.Throw<ArgumentException>(() => new SequentialGuid(Guid.NewGuid(), order));
	}

	[Fact]
	void Should_throw_when_order_is_unspecified()
	{
		var generated = new SequentialGuid();

		Should.Throw<ArgumentOutOfRangeException>(() => new SequentialGuid(generated.Value, GuidByteOrder.Unspecified));
	}

	[Fact]
	void Should_round_trip_through_sql_order_and_back()
	{
		var original = new SequentialGuid();

		var roundTripped = original.ToSqlOrder().ToRfcOrder();

		roundTripped.ShouldBe(original);
		roundTripped.Value.ShouldBe(original.Value);
	}

	[Fact]
	void Should_be_a_no_op_when_already_in_the_requested_order()
	{
		var value = new SequentialGuid();

		value.ToRfcOrder().ShouldBe(value);
		value.ToSqlOrder().ToSqlOrder().ShouldBe(value.ToSqlOrder());
	}

	[Fact]
	void Should_be_equal_regardless_of_byte_order_tag()
	{
		var rfcTagged = new SequentialGuid();
		var sqlTagged = rfcTagged.ToSqlOrder();

		rfcTagged.Equals(sqlTagged).ShouldBeTrue();
		rfcTagged.GetHashCode().ShouldBe(sqlTagged.GetHashCode());
	}

	[Fact]
	void Should_compare_equal_to_zero_when_instances_are_equal()
	{
		var rfcTagged = new SequentialGuid();
		var sqlTagged = rfcTagged.ToSqlOrder();

		rfcTagged.CompareTo(sqlTagged).ShouldBe(0);
	}

	[Fact]
	void Should_sort_using_sql_server_semantics_when_tagged_sql_server()
	{
		var sqlTaggedValues = new List<SequentialGuid>();
		for (var i = 0; i < 20; i++)
			sqlTaggedValues.Add(new SequentialGuid().ToSqlOrder());

		var expectedBySqlGuid = sqlTaggedValues.OrderBy(x => new SqlGuid(x.Value)).ToArray();
		var actualByCompareTo = sqlTaggedValues.OrderBy(x => x).ToArray();

		actualByCompareTo.ShouldBe(expectedBySqlGuid);
	}

	[Fact]
	void Should_unwrap_implicitly_to_guid()
	{
		var value = new SequentialGuid();

		Guid unwrapped = value;

		unwrapped.ShouldBe(value.Value);
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "Norse.Primitives.Tests.Identifiers.SequentialGuidTests"`
Expected: FAIL to compile — `SequentialGuid` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Primitives/Identifiers/SequentialGuid.cs`:

```csharp
using System.Data.SqlTypes;
using System.Security.Cryptography;
using System.Threading;

namespace Norse.Primitives.Identifiers;

/// <summary>
/// A guaranteed-well-formed RFC 9562 UUID version 7 value: time-ordered, safe to mint at any boundary
/// (including client-side, e.g. WASM/MAUI), and convertible to a byte order that sorts correctly under
/// SQL Server's <c>uniqueidentifier</c> comparison when a transactional table needs it.
/// </summary>
/// <remarks>
/// See <see cref="SequentialGuidBytes"/> for the byte-level layout and the SQL Server shuffle contract.
/// The public surface is deliberately narrow: no <see cref="object.ToString"/> override, no parsing, no
/// comparison operators. Untrusted input always goes through <see cref="GuidParser"/>'s <see cref="Result{T}"/>
/// gateway, never through this type directly — the only supported construction paths are "generate a new
/// one" and "wrap a <see cref="Guid"/> this platform already produced."
/// </remarks>
public readonly record struct SequentialGuid : INorseGuid, IComparable<SequentialGuid>, IEquatable<SequentialGuid>
{
	static int _counter;

	static SequentialGuid()
	{
		_counter = RandomNumberGenerator.GetInt32(0x200);
	}

	/// <inheritdoc />
	public Guid Value { get; }

	/// <summary>Gets which byte layout <see cref="Value"/> is currently in.</summary>
	public GuidByteOrder Order { get; }

	/// <summary>Gets the UTC timestamp embedded in <see cref="Value"/>.</summary>
	public DateTime Timestamp { get; }

	/// <summary>Generates a new value from the current time. Always <see cref="GuidByteOrder.Rfc9562"/>.</summary>
	public SequentialGuid()
	{
		var unixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var counter = Interlocked.Increment(ref _counter) & 0x3FFFFFF;

		Span<byte> entropy = stackalloc byte[6];
		RandomNumberGenerator.Fill(entropy);

		Value = SequentialGuidBytes.GenerateRfc(unixMilliseconds, counter, entropy);
		Order = GuidByteOrder.Rfc9562;
		Timestamp = SequentialGuidBytes.ExtractTimestamp(Value, Order);
	}

	/// <summary>Wraps an existing value that this platform already produced, tagging it with its known byte order.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="order"/> is <see cref="GuidByteOrder.Unspecified"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="value"/> is not a version 7 UUID with RFC 9562 variant bits.</exception>
	public SequentialGuid(Guid value, GuidByteOrder order)
	{
		if (order == GuidByteOrder.Unspecified)
			throw new ArgumentOutOfRangeException(nameof(order), order, "GuidByteOrder.Unspecified is never a valid argument.");
		if (!GuidVersionBits.HasVersionAndVariant(value, 7))
			throw new ArgumentException("Value must be a version 7 UUID with RFC 9562 variant bits.", nameof(value));

		Value = value;
		Order = order;
		Timestamp = SequentialGuidBytes.ExtractTimestamp(value, order);
	}

	/// <summary>Returns this value converted to <see cref="GuidByteOrder.SqlServer"/> order (a no-op if already there).</summary>
	public SequentialGuid ToSqlOrder() =>
		Order == GuidByteOrder.SqlServer ? this : new SequentialGuid(SequentialGuidBytes.ToSqlOrder(Value), GuidByteOrder.SqlServer);

	/// <summary>Returns this value converted to <see cref="GuidByteOrder.Rfc9562"/> order (a no-op if already there).</summary>
	public SequentialGuid ToRfcOrder() =>
		Order == GuidByteOrder.Rfc9562 ? this : new SequentialGuid(SequentialGuidBytes.ToRfcOrder(Value), GuidByteOrder.Rfc9562);

	/// <summary>Implicitly unwraps to the underlying <see cref="Guid"/> (storage/wire representation).</summary>
	public static implicit operator Guid(SequentialGuid value) => value.Value;

	/// <inheritdoc />
	public bool Equals(SequentialGuid other) =>
		ToRfcOrder().Value == other.ToRfcOrder().Value;

	/// <inheritdoc />
	public override int GetHashCode() =>
		ToRfcOrder().Value.GetHashCode();

	/// <inheritdoc />
	public int CompareTo(SequentialGuid other)
	{
		var normalizedOther = other.Order == Order
			? other
			: Order == GuidByteOrder.SqlServer ? other.ToSqlOrder() : other.ToRfcOrder();

		return Order == GuidByteOrder.SqlServer
			? new SqlGuid(Value).CompareTo(new SqlGuid(normalizedOther.Value))
			: Value.CompareTo(normalizedOther.Value);
	}
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "Norse.Primitives.Tests.Identifiers.SequentialGuidTests"`
Expected: PASS (10 tests)

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build Svartalfheim.slnx`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 6: Stage and show the diff**

```bash
git add src/Primitives/Identifiers/SequentialGuid.cs tests/Primitives.Tests/Identifiers/SequentialGuidTests.cs
git status --short
git diff --cached --stat
```

Do not run `git commit`.

---

### Task 5: `SequentialGuid` batch generation — `Fill`/`CreateMany`

**Files:**
- Modify: `src/Primitives/Identifiers/SequentialGuid.cs` (add two static members)
- Test: `tests/Primitives.Tests/Identifiers/SequentialGuidBatchTests.cs`

**Interfaces:**
- Consumes: `SequentialGuid` (Task 4), its private static `_counter` field and `SequentialGuidBytes.GenerateRfc`.
- Produces: `public static void SequentialGuid.Fill(Span<SequentialGuid> destination)`, `public static SequentialGuid[] SequentialGuid.CreateMany(int count)`. Both share one `DateTimeOffset.UtcNow` capture across the whole batch and claim a contiguous block of the process-global counter via a single `Interlocked.Add`.

**Note on test scope:** `Fill`'s own bound check (`destination.Length > 0x400_0000`) is not exercised by a dedicated test here — constructing a real `Span<SequentialGuid>` that large would require allocating roughly 2 GB (67,108,865 × ~32 bytes), which is not a reasonable thing for a unit test to do. `CreateMany`'s identical bound is tested below (cheap: it's a check on the `int count` parameter, before any array is allocated) and gives confidence in the same code path `Fill` uses.

- [ ] **Step 1: Write the failing tests**

Create `tests/Primitives.Tests/Identifiers/SequentialGuidBatchTests.cs`:

```csharp
using Norse.Primitives.Identifiers;

namespace Norse.Primitives.Tests.Identifiers;

public sealed class SequentialGuidBatchTests
{
	[Fact]
	void Should_fill_destination_with_distinct_well_formed_values()
	{
		Span<SequentialGuid> destination = new SequentialGuid[10];

		SequentialGuid.Fill(destination);

		var array = destination.ToArray();
		array.Distinct().Count().ShouldBe(10);
		foreach (var value in array)
			GuidVersionBits.HasVersionAndVariant(value.Value, 7).ShouldBeTrue();
	}

	[Fact]
	void Should_share_one_timestamp_capture_across_the_batch()
	{
		Span<SequentialGuid> destination = new SequentialGuid[25];

		SequentialGuid.Fill(destination);

		var distinctTimestamps = destination.ToArray().Select(x => x.Timestamp).Distinct().ToArray();
		distinctTimestamps.Length.ShouldBe(1);
	}

	[Fact]
	void Should_produce_a_contiguous_increasing_sequence()
	{
		Span<SequentialGuid> destination = new SequentialGuid[25];

		SequentialGuid.Fill(destination);

		var array = destination.ToArray();
		for (var i = 1; i < array.Length; i++)
			array[i].CompareTo(array[i - 1]).ShouldBeGreaterThan(0);
	}

	[Fact]
	void Should_do_nothing_when_destination_is_empty()
	{
		Span<SequentialGuid> destination = [];

		Should.NotThrow(() => SequentialGuid.Fill(destination));
	}

	[Fact]
	void Should_create_many_matching_the_requested_count()
	{
		var values = SequentialGuid.CreateMany(15);

		values.Length.ShouldBe(15);
	}

	[Fact]
	void Should_return_an_empty_array_when_count_is_zero()
	{
		SequentialGuid.CreateMany(0).ShouldBeEmpty();
	}

	[Fact]
	void Should_throw_when_count_is_negative()
	{
		Should.Throw<ArgumentOutOfRangeException>(() => SequentialGuid.CreateMany(-1));
	}

	[Fact]
	void Should_throw_when_count_exceeds_the_counter_space()
	{
		Should.Throw<ArgumentOutOfRangeException>(() => SequentialGuid.CreateMany(0x400_0001));
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "Norse.Primitives.Tests.Identifiers.SequentialGuidBatchTests"`
Expected: FAIL to compile — `Fill`/`CreateMany` do not exist.

- [ ] **Step 3: Write the implementation**

Add to `src/Primitives/Identifiers/SequentialGuid.cs` (inside the `SequentialGuid` struct, after `CompareTo`):

```csharp
	/// <summary>
	/// Fills <paramref name="destination"/> with new values sharing a single current-time capture, each
	/// claiming a contiguous slot in the process-global counter. All <see cref="GuidByteOrder.Rfc9562"/>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> exceeds the 26-bit counter space (67,108,864).</exception>
	public static void Fill(Span<SequentialGuid> destination)
	{
		if (destination.Length > 0x400_0000)
			throw new ArgumentOutOfRangeException(nameof(destination),
				"Batch size must not exceed the 26-bit counter space (67,108,864).");
		if (destination.IsEmpty)
			return;

		var unixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var count = destination.Length;
		var start = Interlocked.Add(ref _counter, count) - count + 1;

		Span<byte> entropy = stackalloc byte[6];
		for (var i = 0; i < count; i++)
		{
			RandomNumberGenerator.Fill(entropy);
			var counter = (start + i) & 0x3FFFFFF;
			var value = SequentialGuidBytes.GenerateRfc(unixMilliseconds, counter, entropy);
			destination[i] = new SequentialGuid(value, GuidByteOrder.Rfc9562);
		}
	}

	/// <summary>Creates an array of <paramref name="count"/> new values sharing a single current-time capture.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative or exceeds the 26-bit counter space.</exception>
	public static SequentialGuid[] CreateMany(int count)
	{
		if (count is < 0 or > 0x400_0000)
			throw new ArgumentOutOfRangeException(nameof(count),
				"Count must be between 0 and the 26-bit counter space (67,108,864).");
		if (count == 0)
			return [];

		var result = new SequentialGuid[count];
		Fill(result);
		return result;
	}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "Norse.Primitives.Tests.Identifiers.SequentialGuidBatchTests"`
Expected: PASS (8 tests)

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build Svartalfheim.slnx`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 6: Stage and show the diff**

```bash
git add src/Primitives/Identifiers/SequentialGuid.cs tests/Primitives.Tests/Identifiers/SequentialGuidBatchTests.cs
git status --short
git diff --cached --stat
```

Do not run `git commit`.

---

### Task 6: `DeterministicGuid` struct

**Files:**
- Create: `src/Primitives/Identifiers/DeterministicGuid.cs`
- Test: `tests/Primitives.Tests/Identifiers/DeterministicGuidTests.cs`

**Interfaces:**
- Consumes: `INorseGuid` (Task 1), `GuidVersionBits.HasVersionAndVariant` (Task 1). Independent of `SequentialGuid`/`SequentialGuidBytes` — no dependency on Tasks 2–5.
- Produces: `public readonly record struct DeterministicGuid : INorseGuid, IComparable<DeterministicGuid>, IEquatable<DeterministicGuid>` with `Guid Value { get; }`, a nested `static class Namespaces` (RFC 9562 §6.6 well-known namespaces: `Dns`, `Url`, `Oid`, `X500`), constructors `DeterministicGuid(Guid namespaceId, string name)` / `ReadOnlySpan<char> name` / `ReadOnlySpan<byte> name`, a validating wrap constructor `DeterministicGuid(Guid value)`, and an implicit conversion to `Guid`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Primitives.Tests/Identifiers/DeterministicGuidTests.cs`:

```csharp
using System.Text;
using Norse.Primitives.Identifiers;

namespace Norse.Primitives.Tests.Identifiers;

public sealed class DeterministicGuidTests
{
	[Fact]
	void Should_produce_the_same_value_when_namespace_and_name_are_the_same()
	{
		var first = new DeterministicGuid(DeterministicGuid.Namespaces.Dns, "example.com");
		var second = new DeterministicGuid(DeterministicGuid.Namespaces.Dns, "example.com");

		first.ShouldBe(second);
	}

	[Fact]
	void Should_produce_a_different_value_when_the_name_differs()
	{
		var first = new DeterministicGuid(DeterministicGuid.Namespaces.Dns, "example.com");
		var second = new DeterministicGuid(DeterministicGuid.Namespaces.Dns, "example.org");

		first.ShouldNotBe(second);
	}

	[Fact]
	void Should_produce_a_different_value_when_the_namespace_differs()
	{
		var first = new DeterministicGuid(DeterministicGuid.Namespaces.Dns, "example.com");
		var second = new DeterministicGuid(DeterministicGuid.Namespaces.Url, "example.com");

		first.ShouldNotBe(second);
	}

	[Fact]
	void Should_produce_the_same_value_from_string_char_span_and_byte_span_overloads()
	{
		const string name = "example.com";
		var fromString = new DeterministicGuid(DeterministicGuid.Namespaces.Dns, name);
		var fromCharSpan = new DeterministicGuid(DeterministicGuid.Namespaces.Dns, name.AsSpan());
		var fromByteSpan = new DeterministicGuid(DeterministicGuid.Namespaces.Dns, Encoding.UTF8.GetBytes(name));

		fromString.ShouldBe(fromCharSpan);
		fromCharSpan.ShouldBe(fromByteSpan);
	}

	[Fact]
	void Should_be_well_formed_version5_when_generated()
	{
		var value = new DeterministicGuid(DeterministicGuid.Namespaces.Url, "https://example.com");

		GuidVersionBits.HasVersionAndVariant(value.Value, 5).ShouldBeTrue();
	}

	[Fact]
	void Should_match_the_known_rfc_9562_dns_namespace_value()
	{
		DeterministicGuid.Namespaces.Dns.ShouldBe(new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8"));
	}

	[Fact]
	void Should_throw_when_wrapped_value_is_not_a_version5_guid()
	{
		Should.Throw<ArgumentException>(() => new DeterministicGuid(Guid.NewGuid()));
	}

	[Fact]
	void Should_not_throw_when_wrapping_an_already_generated_value()
	{
		var generated = new DeterministicGuid(DeterministicGuid.Namespaces.Dns, "example.com");

		Should.NotThrow(() => new DeterministicGuid(generated.Value));
	}

	[Fact]
	void Should_unwrap_implicitly_to_guid()
	{
		var value = new DeterministicGuid(DeterministicGuid.Namespaces.Dns, "example.com");

		Guid unwrapped = value;

		unwrapped.ShouldBe(value.Value);
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "Norse.Primitives.Tests.Identifiers.DeterministicGuidTests"`
Expected: FAIL to compile — `DeterministicGuid` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Primitives/Identifiers/DeterministicGuid.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Norse.Primitives.Identifiers;

/// <summary>
/// A guaranteed-well-formed RFC 9562 UUID version 5 value, deterministically derived from a namespace
/// and a name via SHA-1 (RFC 9562 §5.5 / §A.4 mandates SHA-1 for name-based v5 identifiers — a
/// specification requirement, not a security primitive).
/// </summary>
/// <remarks>
/// Exists so a lookup/reference-table foreign key can be computed in memory from a namespace + natural
/// key, without a database round trip. No <c>Timestamp</c>, no byte-order concept — a content hash has
/// no time component and no meaningful sort order.
/// </remarks>
public readonly record struct DeterministicGuid : INorseGuid, IComparable<DeterministicGuid>, IEquatable<DeterministicGuid>
{
	const int StackThreshold = 256;

	/// <summary>RFC 9562 §6.6 well-known namespace UUIDs.</summary>
	public static class Namespaces
	{
		/// <summary>Name string is a fully-qualified domain name.</summary>
		public static readonly Guid Dns = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

		/// <summary>Name string is a URL.</summary>
		public static readonly Guid Url = new("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

		/// <summary>Name string is an ISO OID.</summary>
		public static readonly Guid Oid = new("6ba7b812-9dad-11d1-80b4-00c04fd430c8");

		/// <summary>Name string is an X.500 DN (in DER or a text output format).</summary>
		public static readonly Guid X500 = new("6ba7b814-9dad-11d1-80b4-00c04fd430c8");
	}

	/// <inheritdoc />
	public Guid Value { get; }

	/// <summary>Derives a new value from <paramref name="namespaceId"/> and <paramref name="name"/>.</summary>
	public DeterministicGuid(Guid namespaceId, string name) : this(namespaceId, name.AsSpan()) { }

	/// <summary>Derives a new value from <paramref name="namespaceId"/> and <paramref name="name"/>.</summary>
	[SkipLocalsInit]
	public DeterministicGuid(Guid namespaceId, ReadOnlySpan<char> name)
	{
		var maxByteCount = checked(16 + Encoding.UTF8.GetMaxByteCount(name.Length));
		Span<byte> stackBuffer = stackalloc byte[StackThreshold];
		var buffer = maxByteCount <= StackThreshold ? stackBuffer[..maxByteCount] : new byte[maxByteCount];
		WriteNamespace(namespaceId, buffer);
		var nameByteLength = Encoding.UTF8.GetBytes(name, buffer[16..]);
		Value = HashAndFinalize(buffer[..(16 + nameByteLength)]);
	}

	/// <summary>Derives a new value from <paramref name="namespaceId"/> and raw <paramref name="name"/> bytes.</summary>
	[SkipLocalsInit]
	public DeterministicGuid(Guid namespaceId, ReadOnlySpan<byte> name)
	{
		var totalLength = checked(16 + name.Length);
		Span<byte> stackBuffer = stackalloc byte[StackThreshold];
		var buffer = totalLength <= StackThreshold ? stackBuffer[..totalLength] : new byte[totalLength];
		WriteNamespace(namespaceId, buffer);
		name.CopyTo(buffer[16..]);
		Value = HashAndFinalize(buffer);
	}

	/// <summary>Wraps an already-computed value.</summary>
	/// <exception cref="ArgumentException"><paramref name="value"/> is not a version 5 UUID with RFC 9562 variant bits.</exception>
	public DeterministicGuid(Guid value)
	{
		if (!GuidVersionBits.HasVersionAndVariant(value, 5))
			throw new ArgumentException("Value must be a version 5 UUID with RFC 9562 variant bits.", nameof(value));

		Value = value;
	}

	static void WriteNamespace(Guid namespaceId, Span<byte> destination) =>
		namespaceId.TryWriteBytes(destination[..16], bigEndian: true, out _);

	[SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
		Justification = "RFC 9562 §A.4 mandates SHA-1 for UUIDv5 name-based identifiers; this is a specification requirement, not a security primitive.")]
	static Guid HashAndFinalize(ReadOnlySpan<byte> input)
	{
		Span<byte> digest = stackalloc byte[20];
		SHA1.HashData(input, digest);

		var head = digest[..16];
		head[6] = (byte)((head[6] & 0x0F) | (5 << 4));
		head[8] = (byte)((head[8] & 0x3F) | 0x80);

		return new Guid(head, bigEndian: true);
	}

	/// <summary>Implicitly unwraps to the underlying <see cref="Guid"/> (storage/wire representation).</summary>
	public static implicit operator Guid(DeterministicGuid value) => value.Value;

	/// <inheritdoc />
	public bool Equals(DeterministicGuid other) => Value.Equals(other.Value);

	/// <inheritdoc />
	public override int GetHashCode() => Value.GetHashCode();

	/// <inheritdoc />
	public int CompareTo(DeterministicGuid other) => Value.CompareTo(other.Value);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "Norse.Primitives.Tests.Identifiers.DeterministicGuidTests"`
Expected: PASS (9 tests)

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build Svartalfheim.slnx`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 6: Stage and show the diff**

```bash
git add src/Primitives/Identifiers/DeterministicGuid.cs tests/Primitives.Tests/Identifiers/DeterministicGuidTests.cs
git status --short
git diff --cached --stat
```

Do not run `git commit`.

---

### Task 7: AOT smoke verification

**Files:**
- Modify: `tests/smoke/Primitives.Aot.Smoke/Program.cs`

**Interfaces:**
- Consumes: `SequentialGuid`, `DeterministicGuid` (Tasks 4, 6). No new production code — this task only exercises what already exists, then proves it survives native compilation. `System.Data.SqlTypes.SqlGuid` (used internally by `SequentialGuid.CompareTo`) was independently verified during this plan's research to be fully AOT/trim-safe: a scratch console app referencing it built and published with `PublishAot`, `EnableAotAnalyzer`, and `EnableTrimAnalyzer` all enabled, produced zero warnings at both build and native-publish time, and the published native binary ran correctly.

- [ ] **Step 1: Add Identifiers checks to the smoke test**

Add to `tests/smoke/Primitives.Aot.Smoke/Program.cs` — add `using Norse.Primitives.Identifiers;` near the top (alongside the existing `using Norse.Primitives;`), and add these `Check(...)` calls before the `if (failures > 0)` block at the end:

```csharp
Check("SequentialGuid generates a well-formed, current-time-stamped value", () =>
{
	var value = new SequentialGuid();
	return value.Order == GuidByteOrder.Rfc9562 && value.Timestamp > DateTime.UtcNow.AddMinutes(-1);
});

Check("SequentialGuid round-trips through SQL Server byte order", () =>
{
	var original = new SequentialGuid();
	return original.ToSqlOrder().ToRfcOrder() == original;
});

Check("SequentialGuid CompareTo respects SQL Server ordering when tagged SqlServer", () =>
{
	var first = new SequentialGuid();
	var second = new SequentialGuid();
	var firstSql = first.ToSqlOrder();
	var secondSql = second.ToSqlOrder();
	return firstSql.CompareTo(secondSql) == first.CompareTo(second);
});

Check("DeterministicGuid derives a stable value from namespace and name", () =>
{
	var first = new DeterministicGuid(DeterministicGuid.Namespaces.Dns, "example.com");
	var second = new DeterministicGuid(DeterministicGuid.Namespaces.Dns, "example.com");
	return first == second;
});
```

- [ ] **Step 2: Publish and run natively to verify**

Run: `dotnet publish tests/smoke/Primitives.Aot.Smoke -c Release`
Expected: publish succeeds with zero AOT warnings.

Run the published native executable (path printed by the publish step, typically `tests/smoke/Primitives.Aot.Smoke/bin/Release/net11.0/<rid>/publish/Primitives.Aot.Smoke`).
Expected: every `ok` line prints, no `FAIL` lines, exit code `0`.

- [ ] **Step 3: Run the full test suite once more as a final gate**

Run: `dotnet test Svartalfheim.slnx`
Expected: all tests pass, including every test added in Tasks 1–6.

Run: `dotnet build Svartalfheim.slnx`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Stage and show the diff**

```bash
git add tests/smoke/Primitives.Aot.Smoke/Program.cs
git status --short
git diff --cached --stat
```

Do not run `git commit`.

---

## Self-Review

**Spec coverage:** §3 (`INorseGuid`) → Task 1. §3.1 (narrow surface) → reflected in Tasks 4 and 6 (no `ToString` override, no `Parse`/`TryParse`, no operators, implicit-to-`Guid` only). §4 (`DeterministicGuid`) → Task 6. §5.1 (RFC layout, enum explicit values) → Tasks 1–2. §5.2 (SQL shuffle contract + correctness oracle) → Task 3, using the exact three-test oracle the spec names (round-trip, monotonic-sort, fixed-offset) plus a millisecond-boundary variant for extra confidence. §5.3 (equality/comparison semantics) → Task 4. §5.4 (batch generation) → Task 5. §6 (file layout) → File Structure section above, matches exactly. §7 (testing conventions, AOT smoke) → every task's test file follows `Should_{behavior}_when_{condition}`/`public sealed`, and Task 7 closes the AOT gate. No spec section is without a task.

**Placeholder scan:** No `TBD`/`TODO` in any step; every code block is complete, compilable code, not a sketch. The one deliberate scope note (Task 5's "no dedicated oversized-`Fill` test") is an explicit, justified trade-off, not a gap.

**Type consistency:** Traced every cross-task reference — `SequentialGuidBytes.GenerateRfc(long, int, ReadOnlySpan<byte>)` (Task 2) is called identically in Task 3's oracle tests, Task 4's constructors, and Task 5's `Fill`. `SequentialGuidBytes.ToSqlOrder`/`ToRfcOrder(Guid) : Guid` (Task 3) match their usage in Task 4's `SequentialGuid.ToSqlOrder`/`ToRfcOrder`. `GuidVersionBits.HasVersionAndVariant(Guid, byte) : bool` (Task 1) is used with the same signature in Tasks 2, 4, and 6. `GuidByteOrder.Unspecified/Rfc9562/SqlServer` values (`0`/`1`/`2`, Task 1) match every reference in Tasks 3–5. No drift found.
