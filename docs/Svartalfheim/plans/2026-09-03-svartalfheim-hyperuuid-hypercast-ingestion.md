# HyperUuid/HyperCast Ingestion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Svartálfheim's `Identifiers` and `Result<T>`/`Parser` stack a native (Rust-core, via HyperUuid/HyperCast) execution path on platforms/RIDs they cover, with the existing managed C# as the fallback everywhere else — public API unchanged, `HyperCast`'s corpus as the cross-engine conformance authority.

**Architecture:** A shared `NativeCapability` gate (OS-family check, trimmer-foldable, plus a cached native-probe for RID-family gaps the OS check can't see) decides, per call, whether a static parser/generator method routes to the native binding or the existing managed code. Translation from `Verdict<T>`/`Fault`/`Guid` to `Result<T>`/`Failure`/`SequentialGuid` happens at the call site using data the caller already has — no new public vocabulary.

**Tech Stack:** .NET 11 preview, C# 15 (preview), `HyperUuid` + `HyperCast` NuGet packages (P/Invoke, `LibraryImport`-generated), xUnit v3 on Microsoft.Testing.Platform, Shouldly.

**Spec:** `Glitnir/docs/Svartalfheim/specs/2026-09-03-hyperuuid-hypercast-ingestion-design.md`

## Global Constraints

- `dotnet build Svartalfheim.slnx` — warnings are errors (`WarningLevel 9999`, `EnforceCodeStyleInBuild`); a single warning fails the build.
- xUnit v3 on Microsoft.Testing.Platform — `dotnet test tests/Primitives.Tests -- --filter-class "*.ClassName"` (VSTest `--filter` does not work). Never run a test project with zero tests.
- `tests/smoke/Primitives.Aot.Smoke` must publish with zero AOT warnings and exit 0, every task that touches a native call path.
- `src/Primitives.csproj` has never carried an external runtime `PackageReference` before this plan (only an in-repo analyzer wired `ReferenceOutputAssembly="false"`, and `MinVer` with `PrivateAssets="all"`).
- One `<PropertyGroup>` and one `<ItemGroup>` per `.csproj`, alphabetically sorted within each (`Glitnir/docs/house-rules.md`).
- `ParseFailure` renumbers to `Unspecified=0, Empty=1, Malformed=2, OutOfRange=3, Duplicate=4` — a deliberate, documented breaking change, not internal wiring.
- Test classes `public sealed`; test methods omit access modifiers; sentence-shaped names with underscores (`Should_{behavior}_when_{condition}` for parser tests, matching existing files).
- Enums: every member carries an explicit integer value; `0` always claims the sentinel meaning.
- Phase 3 (`Primitives.Ingestion` → HyperTabular/HyperDelimited/HyperWorkbook) is explicitly out of scope — do not plan or implement it here.

---

## Task 1: Real `HyperUuid`/`HyperCast` dependencies on `src/Primitives`, benchmarks de-duplicated

**Files:**
- Modify: `src/Primitives/Primitives.csproj`
- Modify: `benchmarks/Primitives.Benchmarks/Primitives.Benchmarks.csproj`
- Modify: `benchmarks/Primitives.Benchmarks/IdentifierBenchmarks.cs`
- Modify: `benchmarks/Primitives.Benchmarks/HyperCastBenchmarks.cs`

**Interfaces:**
- Produces: `src/Primitives.csproj` carries `PackageReference` for `HyperUuid` and `HyperCast` (floated `Version="*"`, matching this realm's existing `BenchmarkDotNet` convention — no CPM in this repo).

- [ ] **Step 1: Add the real package references to `src/Primitives.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<AllowUnsafeBlocks>true</AllowUnsafeBlocks>
		<Description>Norse forged primitives: the Result&lt;T&gt; discriminated union, closed parse-failure vocabulary, and hot-path scalar parsers for every boundary crossing into the Norse ecosystem from untrusted sources.</Description>
		<NorseFrontendPlatforms>All</NorseFrontendPlatforms>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="HyperCast" Version="*" />
		<PackageReference Include="HyperUuid" Version="*" />
	</ItemGroup>
	<ItemGroup>
		<ProjectReference
			Include="../../gen/Primitives.Analyzers/Primitives.Analyzers.csproj"
			OutputItemType="Analyzer"
			ReferenceOutputAssembly="false" />
	</ItemGroup>
	<Target Name="IncludeGeneratorInPackage" BeforeTargets="_GetPackageFiles">
		<MSBuild Projects="../../gen/Primitives.Analyzers/Primitives.Analyzers.csproj"
			Targets="Build"
			Properties="Configuration=$(Configuration)" />
		<ItemGroup>
			<None Include="../../gen/Primitives.Analyzers/bin/$(Configuration)/netstandard2.0/Norse.Primitives.Analyzers.dll"
				Pack="true"
				PackagePath="analyzers/dotnet/cs/"
				Visible="false" />
		</ItemGroup>
	</Target>
</Project>
```

- [ ] **Step 2: Remove the now-redundant direct `PackageReference`s from the benchmarks project — they flow transitively through `Primitives.csproj`**

Per house rules ("leverage transitive dependencies whenever possible — do not add a `PackageReference` for something already flowing transitively"):

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="..\..\src\Primitives\Primitives.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 3: Update the "throwaway rig" comments in the two benchmark files — they're now exercising the real production dependency, not a disconnected comparison**

In `benchmarks/Primitives.Benchmarks/IdentifierBenchmarks.cs`, replace the file-header comment:

```csharp
// HyperUuid identifier benchmarks -- proves the native path (wired behind NativeCapability in
// src/Primitives/Identifiers) stays faster than the managed fallback. Permanent fixture: run
// before/after any change to either engine's identifier generation.
```

In `benchmarks/Primitives.Benchmarks/HyperCastBenchmarks.cs`, replace the file-header comment:

```csharp
// HyperCast parser benchmarks -- proves the native path (wired behind NativeCapability in
// src/Primitives) stays faster than the managed fallback. Permanent fixture: run before/after
// any change to either engine's parsers.
```

- [ ] **Step 4: Build and confirm zero warnings**

Run: `dotnet build Svartalfheim.slnx`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 5: Commit**

```bash
git add src/Primitives/Primitives.csproj benchmarks/Primitives.Benchmarks/Primitives.Benchmarks.csproj benchmarks/Primitives.Benchmarks/IdentifierBenchmarks.cs benchmarks/Primitives.Benchmarks/HyperCastBenchmarks.cs
git commit -m "build: take HyperUuid/HyperCast as real Primitives dependencies"
```

---

## Task 2: `NativeCapability` — the two-layer gate

**Files:**
- Create: `src/Primitives/NativeCapability.cs`
- Test: `tests/Primitives.Tests/NativeCapabilityTests.cs`

**Interfaces:**
- Produces: `internal static class NativeCapability { internal static bool Available { get; } }` — every later task's native branch reads this. Also produces `internal static void ForManagedOnly(Action test)` (test-only, described below) that every later corpus test uses to exercise the managed path deterministically regardless of host platform.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Primitives.Tests;

// Runs in its own collection: NativeCapability.ForManagedOnly mutates process-global state and
// must not race against any other test reading NativeCapability.Available concurrently.
[Collection(nameof(NativeCapabilityCollection))]
public sealed class NativeCapabilityTests
{
	[Fact]
	void Available_reflects_a_real_probe_on_this_platform()
	{
		// This dev/CI box is native-capable (linux glibc) -- Available should be true here
		// without any override in play.
		NativeCapability.Available.ShouldBeTrue();
	}

	[Fact]
	void ForManagedOnly_forces_the_managed_path_for_the_duration_of_the_callback()
	{
		var observedInsideOverride = true;

		NativeCapability.ForManagedOnly(() =>
			observedInsideOverride = NativeCapability.Available);

		observedInsideOverride.ShouldBeFalse();
	}

	[Fact]
	void ForManagedOnly_restores_the_prior_state_after_the_callback_returns()
	{
		var before = NativeCapability.Available;

		NativeCapability.ForManagedOnly(() => { });

		NativeCapability.Available.ShouldBe(before);
	}

	[Fact]
	void ForManagedOnly_restores_the_prior_state_even_when_the_callback_throws()
	{
		var before = NativeCapability.Available;

		Should.Throw<InvalidOperationException>(() =>
			NativeCapability.ForManagedOnly(() => throw new InvalidOperationException()));

		NativeCapability.Available.ShouldBe(before);
	}
}

[CollectionDefinition(nameof(NativeCapabilityCollection), DisableParallelization = true)]
public sealed class NativeCapabilityCollection;
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.NativeCapabilityTests"`
Expected: FAIL — `NativeCapability` does not exist.

- [ ] **Step 3: Implement `NativeCapability`**

```csharp
namespace Norse.Primitives;

/// <summary>
/// Gates whether a parser/generator routes to its native (HyperUuid/HyperCast) execution path
/// or the managed fallback. Two layers: an <see cref="OperatingSystem"/> platform check the
/// trimmer/NativeAOT constant-folds per publish target, and a cached one-time native probe for
/// RID-family gaps the platform check can't see (e.g. glibc vs. musl Linux -- both report
/// <see cref="OperatingSystem.IsLinux"/> <see langword="true"/>, but only glibc ships a native
/// asset today).
/// </summary>
static class NativeCapability
{
	[ThreadStatic]
	static bool _forcedManagedOnly;

	static readonly Lazy<bool> _probe = new(Probe);

	/// <summary>
	/// <see langword="true"/> when this call should route to the native engine: the platform
	/// family is one HyperUuid/HyperCast ship for, the cached native probe succeeded, and no
	/// test has forced the managed path via <see cref="ForManagedOnly"/> on this thread.
	/// </summary>
	internal static bool Available =>
		!_forcedManagedOnly && PlatformCovered && _probe.Value;

	// HyperUuid/HyperCast ship linux-x64/arm64, osx-x64/arm64, win-x64/arm64, and browser-wasm
	// today -- no ios/android RID exists yet (tracked upstream, see the design's §9). Neither
	// the mobile checks nor the browser check below fires today (no MAUI/WASM head exists on
	// this platform yet), but they're the trimmer-foldable half of the gate regardless of what
	// runs today, so a future head gets this for free.
	static bool PlatformCovered =>
		!OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS() && !OperatingSystem.IsTvOS();

	static bool Probe()
	{
		try
		{
			// A trivial, side-effect-free native call -- proves the P/Invoke library actually
			// resolved and loaded for this exact RID, not just that the platform family matches.
			HyperUuid.UuidGenerator.TryNewV4(out _);
			return true;
		}
		catch (DllNotFoundException)
		{
			return false;
		}
		catch (EntryPointNotFoundException)
		{
			return false;
		}
	}

	/// <summary>
	/// Test-only: forces <see cref="Available"/> to <see langword="false"/> for the duration of
	/// <paramref name="test"/>, so the managed fallback is exercised deterministically
	/// regardless of the host platform's own native capability. Restores the prior state even
	/// if <paramref name="test"/> throws. Thread-local, not process-global, so parallel test
	/// runs on other threads are unaffected -- callers still isolate via
	/// <c>DisableParallelization</c> on their own collection to avoid two overrides racing on
	/// the *same* thread's reentrant call.
	/// </summary>
	internal static void ForManagedOnly(Action test)
	{
		var previous = _forcedManagedOnly;
		_forcedManagedOnly = true;
		try
		{
			test();
		}
		finally
		{
			_forcedManagedOnly = previous;
		}
	}
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.NativeCapabilityTests"`
Expected: PASS, 4/4.

- [ ] **Step 5: Commit**

```bash
git add src/Primitives/NativeCapability.cs tests/Primitives.Tests/NativeCapabilityTests.cs
git commit -m "feat: add NativeCapability gate for the HyperUuid/HyperCast seam"
```

---

## Task 3: `SequentialGuid()` native path

**Files:**
- Modify: `src/Primitives/Identifiers/SequentialGuid.cs`
- Test: `tests/Primitives.Tests/Identifiers/SequentialGuidTests.cs`

**Interfaces:**
- Consumes: `NativeCapability.Available` (Task 2), `NativeCapability.ForManagedOnly` (Task 2), `HyperUuid.UuidGenerator.NewV7()`.
- Produces: `SequentialGuid()` unchanged signature; internal `GenerateManagedV7()` extracted from the existing constructor body for the managed fallback to call.

- [ ] **Step 1: Write the failing tests**

```csharp
// Add to the existing SequentialGuidTests class:

[Fact]
void Constructor_produces_a_well_formed_v7_value_on_the_native_path()
{
	var value = new SequentialGuid();

	GuidVersionBits.HasVersionAndVariant(value.Value, 7).ShouldBeTrue();
	value.Order.ShouldBe(GuidByteOrder.Rfc9562);
}

[Fact]
void Constructor_produces_a_well_formed_v7_value_on_the_managed_path()
{
	SequentialGuid value = default;

	NativeCapability.ForManagedOnly(() =>
		value = new SequentialGuid());

	GuidVersionBits.HasVersionAndVariant(value.Value, 7).ShouldBeTrue();
	value.Order.ShouldBe(GuidByteOrder.Rfc9562);
}
```

- [ ] **Step 2: Run to verify the managed-path test fails**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.SequentialGuidTests"`
Expected: the native-path test passes (constructor already works); the managed-path test also currently passes since there's no branch yet — both are green before the change, which is expected (this task adds a *second, distinct* code path, not new behavior on the existing one). Confirm both are green now as the baseline.

- [ ] **Step 3: Extract the existing generation into `GenerateManagedV7`, add the native branch**

In `src/Primitives/Identifiers/SequentialGuid.cs`, replace the existing constructor body:

```csharp
/// <summary>Generates a new value from the current time. Always <see cref="GuidByteOrder.Rfc9562"/>.</summary>
public SequentialGuid()
{
	Value = NativeCapability.Available ? HyperUuid.UuidGenerator.NewV7() : GenerateManagedV7();
	Order = GuidByteOrder.Rfc9562;
	Timestamp = SequentialGuidBytes.ExtractTimestamp(Value, Order);
}

static Guid GenerateManagedV7()
{
	var unixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
	var counter = Interlocked.Increment(ref _counter) & 0x3FFFFFF;

	Span<byte> entropy = stackalloc byte[6];
	RandomNumberGenerator.Fill(entropy);

	return SequentialGuidBytes.GenerateRfc(unixMilliseconds, counter, entropy);
}
```

- [ ] **Step 4: Run to verify both tests pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.SequentialGuidTests"`
Expected: PASS, both new tests plus every pre-existing test in the class.

- [ ] **Step 5: Commit**

```bash
git add src/Primitives/Identifiers/SequentialGuid.cs tests/Primitives.Tests/Identifiers/SequentialGuidTests.cs
git commit -m "feat: SequentialGuid() routes to HyperUuid.NewV7 on native-capable platforms"
```

---

## Task 4: `SequentialGuid.Fill`/`CreateMany` native path

**Files:**
- Modify: `src/Primitives/Identifiers/SequentialGuid.cs`
- Test: `tests/Primitives.Tests/Identifiers/SequentialGuidBatchTests.cs`

**Interfaces:**
- Consumes: `NativeCapability.Available`/`ForManagedOnly`, `HyperUuid.UuidGenerator.FillV7(Span<Guid>)`.
- Produces: `Fill(Span<SequentialGuid>)` unchanged signature.

- [ ] **Step 1: Write the failing test**

```csharp
// Add to the existing SequentialGuidBatchTests class:

[Fact]
void Should_fill_destination_with_distinct_well_formed_values_on_the_managed_path()
{
	Span<SequentialGuid> destination = new SequentialGuid[10];

	NativeCapability.ForManagedOnly(() =>
		SequentialGuid.Fill(destination));

	SequentialGuid[] array = [.. destination];
	array.Distinct().Count().ShouldBe(10);
	foreach (var value in array)
		GuidVersionBits.HasVersionAndVariant(value.Value, 7).ShouldBeTrue();
}
```

- [ ] **Step 2: Run to verify it passes as a baseline (existing `Fill` already handles this correctly on the managed-only code path — the test above targets the *coming* branch, but until Step 3 lands there's only one path, so it's green now for the same reason Task 3's was)**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.SequentialGuidBatchTests"`
Expected: PASS (baseline, single existing code path).

- [ ] **Step 3: Add the native branch to `Fill`**

```csharp
public static void Fill(Span<SequentialGuid> destination)
{
	if (destination.Length > 0x400_0000)
		throw new ArgumentOutOfRangeException(nameof(destination),
			"Batch size must not exceed the 26-bit counter space (67,108,864).");
	if (destination.IsEmpty)
		return;

	if (NativeCapability.Available)
	{
		FillNative(destination);
		return;
	}

	FillManaged(destination);
}

static void FillNative(Span<SequentialGuid> destination)
{
	Span<Guid> native = destination.Length <= 256 ? stackalloc Guid[destination.Length] : new Guid[destination.Length];
	HyperUuid.UuidGenerator.FillV7(native);
	for (var i = 0; i < destination.Length; i++)
		destination[i] = new SequentialGuid(native[i], GuidByteOrder.Rfc9562);
}

static void FillManaged(Span<SequentialGuid> destination)
{
	var unixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
	var count = destination.Length;
	var start = Interlocked.Add(ref _counter, count) - count + 1;

	Span<byte> entropyChunk = stackalloc byte[EntropyChunkBytes];
	var chunkItemCapacity = EntropyChunkBytes / 6;

	for (var offset = 0; offset < count; offset += chunkItemCapacity)
	{
		var chunkCount = Math.Min(chunkItemCapacity, count - offset);
		var chunk = entropyChunk[..(chunkCount * 6)];
		RandomNumberGenerator.Fill(chunk);

		for (var i = 0; i < chunkCount; i++)
		{
			var counter = (start + offset + i) & 0x3FFFFFF;
			var value = SequentialGuidBytes.GenerateRfc(unixMilliseconds, counter, chunk.Slice(i * 6, 6));
			destination[offset + i] = new SequentialGuid(value, GuidByteOrder.Rfc9562);
		}
	}
}
```

`FillManaged` is the existing chunked-entropy body from the prior fix (§5 of the spec), unchanged — only extracted into its own method and given a native sibling. `Span<Guid>` is a blittable 16 bytes, so a 256-item stack threshold (4 KB) is safe; larger batches fall to a single heap array, matching `DeterministicGuid`'s existing stack/heap-fallback pattern in this same file family.

- [ ] **Step 4: Run to verify both native and managed batch tests pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.SequentialGuidBatchTests"`
Expected: PASS, every test in the class including the new one and the existing chunk-boundary regression test.

- [ ] **Step 5: Commit**

```bash
git add src/Primitives/Identifiers/SequentialGuid.cs tests/Primitives.Tests/Identifiers/SequentialGuidBatchTests.cs
git commit -m "feat: SequentialGuid.Fill routes to HyperUuid.FillV7 on native-capable platforms"
```

---

## Task 5: `DeterministicGuid` native path + cross-engine determinism parity

**Files:**
- Modify: `src/Primitives/Identifiers/DeterministicGuid.cs`
- Test: `tests/Primitives.Tests/Identifiers/DeterministicGuidTests.cs`

**Interfaces:**
- Consumes: `NativeCapability.Available`/`ForManagedOnly`, `HyperUuid.UuidGenerator.NewV5(Guid, string)`.
- Produces: `DeterministicGuid(Guid, string)`/`(Guid, ReadOnlySpan<char>)`/`(Guid, ReadOnlySpan<byte>)` unchanged signatures.

- [ ] **Step 1: Write the failing test — this is the load-bearing one: both engines must agree bit-for-bit, not just "both work"**

```csharp
// Add to the existing DeterministicGuidTests class:

[Fact]
void Native_and_managed_paths_produce_the_identical_value_for_the_same_input()
{
	var namespaceId = DeterministicGuid.Namespaces.Dns;
	const string name = "example.com";

	var native = new DeterministicGuid(namespaceId, name);

	DeterministicGuid managed = default;
	NativeCapability.ForManagedOnly(() =>
		managed = new DeterministicGuid(namespaceId, name));

	native.Value.ShouldBe(managed.Value);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.DeterministicGuidTests"`
Expected: PASS actually — both paths already produce SHA-1-derived output per RFC 9562 §5.5, and there's only one path today. This test is a regression pin for the *coming* branch; confirm it's green as the baseline before Step 3 adds the second path, the same shape as Tasks 3/4.

- [ ] **Step 3: Add the native branch to every constructor that generates (not wraps) a value**

```csharp
public DeterministicGuid(Guid namespaceId, string name) : this(namespaceId, name.AsSpan()) { }

[SkipLocalsInit]
public DeterministicGuid(Guid namespaceId, ReadOnlySpan<char> name)
{
	if (NativeCapability.Available)
	{
		Value = HyperUuid.UuidGenerator.NewV5(namespaceId, name.ToString());
		return;
	}

	var maxByteCount = checked(16 + Encoding.UTF8.GetMaxByteCount(name.Length));
	Span<byte> stackBuffer = stackalloc byte[StackThreshold];
	var buffer = maxByteCount <= StackThreshold ? stackBuffer[..maxByteCount] : new byte[maxByteCount];
	WriteNamespace(namespaceId, buffer);
	var nameByteLength = Encoding.UTF8.GetBytes(name, buffer[16..]);
	Value = HashAndFinalize(buffer[..(16 + nameByteLength)]);
}

[SkipLocalsInit]
public DeterministicGuid(Guid namespaceId, ReadOnlySpan<byte> name)
{
	if (NativeCapability.Available)
	{
		Value = HyperUuid.UuidGenerator.NewV5(namespaceId, Encoding.UTF8.GetString(name));
		return;
	}

	var totalLength = checked(16 + name.Length);
	Span<byte> stackBuffer = stackalloc byte[StackThreshold];
	var buffer = totalLength <= StackThreshold ? stackBuffer[..totalLength] : new byte[totalLength];
	WriteNamespace(namespaceId, buffer);
	name.CopyTo(buffer[16..]);
	Value = HashAndFinalize(buffer);
}
```

`UuidGenerator.NewV5` takes a `string`, not a span — the native path pays one allocation converting the span to a string, which is honest and worth noting rather than hiding: this is the one door where the native path is not itself zero-alloc at the C# call boundary, unlike the managed path's stack-buffer-first design. Acceptable because `NativeCapability.Available` already means the whole point is "prefer the faster engine," and HyperCast's own benchmarks (§1 of the spec) show native still winning despite this.

- [ ] **Step 4: Run to verify the parity test and every existing test in the class pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.DeterministicGuidTests"`
Expected: PASS, all green.

- [ ] **Step 5: Commit**

```bash
git add src/Primitives/Identifiers/DeterministicGuid.cs tests/Primitives.Tests/Identifiers/DeterministicGuidTests.cs
git commit -m "feat: DeterministicGuid routes to HyperUuid.NewV5 on native-capable platforms"
```

---

## Task 6: SQL byte-order native path + parity test against HyperUuid's own claim

**Files:**
- Modify: `src/Primitives/Identifiers/SequentialGuid.cs`
- Test: `tests/Primitives.Tests/Identifiers/SequentialGuidTests.cs`

**Interfaces:**
- Consumes: `HyperUuid.UuidGenerator.V7ToSqlOrder(Guid)`, `V7FromSqlOrder(Guid)`.
- Produces: `ToSqlOrder()`/`ToRfcOrder()` unchanged signatures.

- [ ] **Step 1: Write the failing parity test — HyperUuid's README claims byte-identical output to this realm's own permutation; this pins it as a fact instead of trusting the claim**

```csharp
// Add to the existing SequentialGuidTests class:

[Fact]
void Native_sql_order_transform_matches_the_managed_permutation_byte_for_byte()
{
	var rfcOrdered = new SequentialGuid();

	var managedSqlOrder = default(SequentialGuid);
	NativeCapability.ForManagedOnly(() =>
		managedSqlOrder = rfcOrdered.ToSqlOrder());

	var nativeSqlOrder = new SequentialGuid(HyperUuid.UuidGenerator.V7ToSqlOrder(rfcOrdered.Value), GuidByteOrder.SqlServer);

	nativeSqlOrder.Value.ShouldBe(managedSqlOrder.Value);
}

[Fact]
void Native_sql_order_round_trip_reproduces_the_original_value()
{
	var rfcOrdered = new SequentialGuid();

	var sqlOrdered = HyperUuid.UuidGenerator.V7ToSqlOrder(rfcOrdered.Value);
	var roundTripped = HyperUuid.UuidGenerator.V7FromSqlOrder(sqlOrdered);

	roundTripped.ShouldBe(rfcOrdered.Value);
}
```

- [ ] **Step 2: Run to verify these fail (or pass) as the honest baseline**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.SequentialGuidTests"`
Expected: `Native_sql_order_round_trip_reproduces_the_original_value` passes today (it only exercises HyperUuid's own API, already referenced via the package from Task 1). `Native_sql_order_transform_matches_the_managed_permutation_byte_for_byte` is the one that actually proves or disproves HyperUuid's parity claim — record its result. **If it fails, stop and report** — that means HyperUuid's own README claim (§6 of the design spec) is wrong, and `ToSqlOrder()`'s native branch (Step 3) cannot ship until that's resolved upstream or the managed permutation is treated as authoritative instead.

- [ ] **Step 3: If Step 2's parity test passed, add the native branch**

```csharp
public SequentialGuid ToSqlOrder() =>
	Order switch
	{
		GuidByteOrder.Unspecified => throw new InvalidOperationException(
			"default(SequentialGuid) is malformed by construction -- Order is Unspecified. Only wrap a value this platform already produced via the two-arg constructor, or generate a new one with SequentialGuid()."),
		GuidByteOrder.SqlServer => this,
		_ when NativeCapability.Available => new(HyperUuid.UuidGenerator.V7ToSqlOrder(Value), GuidByteOrder.SqlServer),
		_ => new(SequentialGuidBytes.ToSqlOrder(Value), GuidByteOrder.SqlServer)
	};

public SequentialGuid ToRfcOrder() =>
	Order switch
	{
		GuidByteOrder.Unspecified => throw new InvalidOperationException(
			"default(SequentialGuid) is malformed by construction -- Order is Unspecified. Only wrap a value this platform already produced via the two-arg constructor, or generate a new one with SequentialGuid()."),
		GuidByteOrder.Rfc9562 => this,
		_ when NativeCapability.Available => new(HyperUuid.UuidGenerator.V7FromSqlOrder(Value), GuidByteOrder.Rfc9562),
		_ => new(SequentialGuidBytes.ToRfcOrder(Value), GuidByteOrder.Rfc9562)
	};
```

- [ ] **Step 4: Run the full `SequentialGuidTests`/`SequentialGuidBatchTests` classes**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.SequentialGuid*Tests"`
Expected: PASS, all green.

- [ ] **Step 5: Commit**

```bash
git add src/Primitives/Identifiers/SequentialGuid.cs tests/Primitives.Tests/Identifiers/SequentialGuidTests.cs
git commit -m "feat: SQL byte-order transforms route to HyperUuid on native-capable platforms"
```

---

## Task 7: Phase 1 AOT smoke verification

**Files:**
- Modify: `tests/smoke/Primitives.Aot.Smoke/Program.cs`

**Interfaces:**
- Consumes: everything Tasks 3-6 produced.

- [ ] **Step 1: Add smoke assertions exercising the native identifier paths**

```csharp
// Add to Program.cs, after the existing Check() calls:

Check("SequentialGuid() publishes clean under AOT", () =>
	GuidVersionBits.HasVersionAndVariant(new SequentialGuid().Value, 7));

Check("DeterministicGuid publishes clean under AOT", () =>
	new DeterministicGuid(DeterministicGuid.Namespaces.Dns, "aot-smoke").Value != Guid.Empty);

Check("SQL byte-order round trip publishes clean under AOT", () =>
{
	var value = new SequentialGuid();
	return value.ToSqlOrder().ToRfcOrder().Value == value.Value;
});
```

- [ ] **Step 2: Publish and run**

Run: `dotnet publish tests/smoke/Primitives.Aot.Smoke -c Release`
Expected: zero AOT/trim warnings.

Run the published native executable.
Expected: exit code 0, every `Check` reports pass.

- [ ] **Step 3: Commit**

```bash
git add tests/smoke/Primitives.Aot.Smoke/Program.cs
git commit -m "test: extend AOT smoke coverage to the HyperUuid native paths"
```

---

## Task 8: `ParseFailure` renumbering + platform-wide switch inventory

**Files:**
- Modify: `src/Primitives/ParseFailure.cs`
- Test: `tests/Primitives.Tests/ParseFailureTests.cs` (new)

**Interfaces:**
- Produces: `ParseFailure` with `OutOfRange = 3` inserted, `Duplicate` renumbered to `4`. Every later Phase 2 task depends on `ParseFailure.OutOfRange` existing.

- [ ] **Step 1: Write the failing pinning test**

```csharp
namespace Norse.Primitives.Tests;

public sealed class ParseFailureTests
{
	[Theory]
	[InlineData(ParseFailure.Unspecified, 0)]
	[InlineData(ParseFailure.Empty, 1)]
	[InlineData(ParseFailure.Malformed, 2)]
	[InlineData(ParseFailure.OutOfRange, 3)]
	[InlineData(ParseFailure.Duplicate, 4)]
	void Values_mirror_HyperCasts_CastFailure_for_the_shared_cases(ParseFailure failure, byte expected) =>
		((byte)failure).ShouldBe(expected);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.ParseFailureTests"`
Expected: FAIL — `ParseFailure.OutOfRange` does not exist; `Duplicate` is `3`, not `4`.

- [ ] **Step 3: Renumber**

```csharp
namespace Norse.Primitives;

/// <summary>
/// The closed set of reasons a scalar→domain conversion can fail. Mirrors HyperCast's
/// <c>CastFailure</c> for the four shared cases (<see cref="Unspecified"/>-<see cref="OutOfRange"/>)
/// by name, number, and semantics, plus this realm's own <see cref="Duplicate"/> — HyperCast is the
/// source of truth for the parsing grammar and its failure vocabulary; this realm's own addition
/// stays additive, never conflicting. Adding a member is a deliberate breaking change: every
/// exhaustive switch over this enum becomes a build error until updated.
/// </summary>
public enum ParseFailure : byte
{
	/// <summary>Sentinel CLR default — never produced by any parse path.</summary>
	Unspecified = 0,

	/// <summary>Required input was empty or whitespace.</summary>
	Empty = 1,

	/// <summary>Input was present but not recognizable as the target type.</summary>
	Malformed = 2,

	/// <summary>
	/// Input was well-formed but the value falls outside the target's representable range —
	/// e.g. <c>256</c> for a <see cref="byte"/>, a timestamp past <c>9999-12-31</c>.
	/// </summary>
	OutOfRange = 3,

	/// <summary>
	/// Input token was individually valid but repeated where each token may appear only once
	/// — first consumer: flags-enum array parsing, a governed name appearing twice.
	/// </summary>
	Duplicate = 4
}
```

- [ ] **Step 4: Run to verify the pinning test passes**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.ParseFailureTests"`
Expected: PASS, 5/5.

- [ ] **Step 5: Inventory every exhaustive switch over `ParseFailure` platform-wide**

Run: `grep -rn "ParseFailure\." --include="*.cs" src tests gen benchmarks | grep -i switch`

Also search every other realm submodule from the Bifröst root, since the design spec calls out this renumbering as platform-wide, not realm-local:

Run: `grep -rln "ParseFailure" --include="*.cs" ../Asgard ../Midgard ../Yggdrasil ../Himinbjorg ../Heimdall ../Mimisbrunnr ../Mimir 2>/dev/null`

For every hit, open the file, confirm whether it's an exhaustive `switch`/`switch` expression over `ParseFailure` (not just a reference or a `new Failure(ParseFailure.X, ...)` construction), and if so add an arm for `ParseFailure.OutOfRange`. **Report the full list of files touched (or confirm none were found) before proceeding** — this step's output gates whether Task 9 onward can assume a clean platform build.

- [ ] **Step 6: Full solution build**

Run: `dotnet build Svartalfheim.slnx`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 7: Commit**

```bash
git add src/Primitives/ParseFailure.cs tests/Primitives.Tests/ParseFailureTests.cs
git commit -m "feat!: renumber ParseFailure to mirror HyperCast's CastFailure, add OutOfRange

BREAKING CHANGE: ParseFailure.Duplicate moves from 3 to 4; ParseFailure.OutOfRange is
new at 3. Any exhaustive switch over ParseFailure needs an OutOfRange arm."
```

---

## Task 9: Vendor HyperCast's corpus as test data

**Files:**
- Create: `tests/Primitives.Tests/TestData/HyperCastCorpus/` (JSON files, fetched from the HyperCast repo)
- Create: `tests/Primitives.Tests/Primitives.Tests.csproj` — modify to copy the corpus to output

**Interfaces:**
- Produces: a local, versioned snapshot of `corpus/*.json` for Task 10's harness to load. Interim mechanism — the design spec (§9) names a future `HyperCast.Corpus` companion package as the long-term delivery mechanism; this task's snapshot is explicitly superseded once that ships, not a permanent fixture.

- [ ] **Step 1: List the actual corpus files in the HyperCast repo — don't assume names**

Run: `gh api repos/SkunkWerkx/HyperCast/contents/corpus -q '.[].name'`

- [ ] **Step 2: Fetch every listed file into the new test-data directory**

For each filename from Step 1:

```bash
mkdir -p tests/Primitives.Tests/TestData/HyperCastCorpus
gh api repos/SkunkWerkx/HyperCast/contents/corpus/<filename> -q '.content' | base64 -d > tests/Primitives.Tests/TestData/HyperCastCorpus/<filename>
```

- [ ] **Step 3: Record the HyperCast commit/tag the snapshot came from, so drift is detectable later**

```bash
gh api repos/SkunkWerkx/HyperCast/commits/master -q '.sha' > tests/Primitives.Tests/TestData/HyperCastCorpus/SNAPSHOT_SHA.txt
```

- [ ] **Step 4: Wire the corpus files to copy to the test output directory**

Add to `tests/Primitives.Tests/Primitives.Tests.csproj`:

```xml
<ItemGroup>
	<None Include="TestData/HyperCastCorpus/**/*.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 5: Build and confirm the files land in the output directory**

Run: `dotnet build tests/Primitives.Tests`
Verify: `ls /tmp/norse-artifacts/Primitives.Tests/bin/Debug/net11.0/TestData/HyperCastCorpus/` shows every fetched file.

- [ ] **Step 6: Commit**

```bash
git add tests/Primitives.Tests/TestData/HyperCastCorpus tests/Primitives.Tests/Primitives.Tests.csproj
git commit -m "test: vendor a HyperCast corpus snapshot for cross-engine conformance tests"
```

---

## Task 10: Corpus-driven conformance harness — proven on Boolean and Guid first

**Files:**
- Create: `tests/Primitives.Tests/CorpusVector.cs` (shared model)
- Create: `tests/Primitives.Tests/CorpusConformanceTests.cs`
- Modify: `src/Primitives/BooleanParser.cs`
- Modify: `src/Primitives/GuidParser.cs`

**Interfaces:**
- Produces: `internal sealed record CorpusVector(string Input, bool ExpectSuccess, string? ExpectedValue, ParseFailure? ExpectedFailure)` and a loader `internal static IEnumerable<CorpusVector> Load(string fileName)` — every subsequent parser task's corpus tests consume this.

- [ ] **Step 1: Inspect one corpus file's actual JSON shape before modeling it**

Run: `cat tests/Primitives.Tests/TestData/HyperCastCorpus/boolean.json` (or whatever Task 9 Step 1 named the boolean vector file) and note the exact field names before writing `CorpusVector`.

- [ ] **Step 2: Write `CorpusVector` and its loader, matching the shape found in Step 1**

```csharp
using System.Text.Json;

namespace Norse.Primitives.Tests;

/// <summary>One HyperCast corpus test vector: an input string and its expected verdict.</summary>
internal sealed record CorpusVector(string Input, bool ExpectSuccess, string? ExpectedValue, string? ExpectedFailure)
{
	static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

	internal static IEnumerable<object[]> Load(string fileName)
	{
		var path = Path.Combine(AppContext.BaseDirectory, "TestData", "HyperCastCorpus", fileName);
		var json = File.ReadAllText(path);
		var vectors = JsonSerializer.Deserialize<CorpusVector[]>(json, _options) ?? [];
		return vectors.Select(v => new object[] { v });
	}
}
```

(Adjust field names to match whatever Step 1 actually found — the fields above are a starting hypothesis, not a guess to ship unverified.)

- [ ] **Step 3: Write the failing dual-mode Boolean and Guid conformance tests**

```csharp
namespace Norse.Primitives.Tests;

[Collection(nameof(NativeCapabilityCollection))]
public sealed class CorpusConformanceTests
{
	public static IEnumerable<object[]> BooleanVectors() => CorpusVector.Load("boolean.json");
	public static IEnumerable<object[]> GuidVectors() => CorpusVector.Load("uuid.json");

	[Theory]
	[MemberData(nameof(BooleanVectors))]
	void Boolean_native_path_matches_the_corpus(CorpusVector vector) =>
		AssertBooleanMatchesCorpus(vector);

	[Theory]
	[MemberData(nameof(BooleanVectors))]
	void Boolean_managed_path_matches_the_corpus(CorpusVector vector) =>
		NativeCapability.ForManagedOnly(() => AssertBooleanMatchesCorpus(vector));

	[Theory]
	[MemberData(nameof(GuidVectors))]
	void Guid_native_path_matches_the_corpus(CorpusVector vector) =>
		AssertGuidMatchesCorpus(vector);

	[Theory]
	[MemberData(nameof(GuidVectors))]
	void Guid_managed_path_matches_the_corpus(CorpusVector vector) =>
		NativeCapability.ForManagedOnly(() => AssertGuidMatchesCorpus(vector));

	static void AssertBooleanMatchesCorpus(CorpusVector vector)
	{
		var result = BooleanParser.ParseRequired(vector.Input);
		if (vector.ExpectSuccess)
			result.TryGetValue(out Success<bool> success).ShouldBeTrue();
		else
			result.TryGetValue(out Failure _).ShouldBeTrue();
	}

	static void AssertGuidMatchesCorpus(CorpusVector vector)
	{
		var result = GuidParser.ParseRequired(vector.Input);
		if (vector.ExpectSuccess)
			result.TryGetValue(out Success<Guid> success).ShouldBeTrue();
		else
			result.TryGetValue(out Failure _).ShouldBeTrue();
	}
}
```

- [ ] **Step 4: Run to verify the tests fail (before the native branch exists in `BooleanParser`/`GuidParser`)**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.CorpusConformanceTests"`
Expected: the managed-path variants pass (existing code already matches this vocabulary per the spec's §6.3 finding); the native-path variants fail to compile/resolve since neither parser has a native branch yet.

- [ ] **Step 5: Add the native branch to `BooleanParser` and `GuidParser`**

In `src/Primitives/BooleanParser.cs`, replace the `Parse` method body:

```csharp
static Result<bool> Parse(ReadOnlySpan<char> trimmed)
{
	if (NativeCapability.Available)
		return HyperCast.Cast.Boolean(trimmed) switch
		{
			HyperCast.Success<bool> s => new Success<bool>(s.Value),
			HyperCast.Fault => new Failure(ParseFailure.Malformed, trimmed, ExpectedType),
		};

	return _trueValues.Contains(trimmed) ? new Success<bool>(true) :
		_falseValues.Contains(trimmed) ? new Success<bool>(false) :
		new Failure(ParseFailure.Malformed, trimmed, ExpectedType);
}
```

(Match against the actual pre-existing managed body rather than reconstructing it from memory — this task's implementer opens the current file and only inserts the native branch ahead of the existing logic.)

In `src/Primitives/GuidParser.cs`, replace the `Parse` method body:

```csharp
static Result<Guid> Parse(ReadOnlySpan<char> trimmed)
{
	if (NativeCapability.Available)
		return HyperCast.Cast.Uuid(StripPrefix(trimmed)) switch
		{
			HyperCast.Success<Guid> s => new Success<Guid>(s.Value),
			HyperCast.Fault => new Failure(ParseFailure.Malformed, trimmed, ExpectedType),
		};

	return Guid.TryParse(StripPrefix(trimmed), out var value) ?
		new Success<Guid>(value) :
		new Failure(ParseFailure.Malformed, trimmed, ExpectedType);
}
```

- [ ] **Step 6: Run to verify all corpus tests pass, both modes**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.CorpusConformanceTests"`
Expected: PASS, every vector, both native and forced-managed.

- [ ] **Step 7: Run the full existing `BooleanParserTests`/`GuidParserTests` classes to confirm no regression**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.BooleanParserTests|*.GuidParserTests"`
Expected: PASS, all green.

- [ ] **Step 8: Commit**

```bash
git add tests/Primitives.Tests/CorpusVector.cs tests/Primitives.Tests/CorpusConformanceTests.cs src/Primitives/BooleanParser.cs src/Primitives/GuidParser.cs
git commit -m "feat: BooleanParser/GuidParser route to HyperCast natively, proven against its own corpus"
```

---

## Task 11: `IntegerParser` — native path + `OutOfRange` distinction + corpus tests

**Files:**
- Modify: `src/Primitives/IntegerParser.cs`
- Modify: `tests/Primitives.Tests/CorpusConformanceTests.cs`
- Test: `tests/Primitives.Tests/IntegerParserTests.cs` (extend existing)

**Interfaces:**
- Consumes: `NativeCapability`, `HyperCast.Cast.Int32`/`Int64`/etc., `HyperCast.NumFormat`, `ParseFailure.OutOfRange` (Task 8), `CorpusVector.Load` (Task 10).
- Produces: `IntegerParser.ParseRequired<T>`/`ParseOptional<T>` unchanged signatures, now distinguishing malformed from out-of-range on both engines.

- [ ] **Step 1: Write the failing `OutOfRange` distinction test on the managed path**

```csharp
// Add to IntegerParserTests:

[Fact]
void Should_return_OutOfRange_when_text_is_numerically_well_formed_but_exceeds_the_target_type()
{
	var result = IntegerParser.ParseRequired<byte>("256", CultureInfo.InvariantCulture);

	result.TryGetValue(out Failure failure).ShouldBeTrue();
	failure.Reason.ShouldBe(ParseFailure.OutOfRange);
}

[Fact]
void Should_still_return_Malformed_for_genuinely_unrecognizable_text()
{
	var result = IntegerParser.ParseRequired<byte>("not-a-number", CultureInfo.InvariantCulture);

	result.TryGetValue(out Failure failure).ShouldBeTrue();
	failure.Reason.ShouldBe(ParseFailure.Malformed);
}
```

- [ ] **Step 2: Run to verify both fail (today both return `Malformed`)**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.IntegerParserTests"`
Expected: FAIL on the first test — `256` for `byte` currently comes back `Malformed`, not `OutOfRange`.

- [ ] **Step 3: Distinguish `OutOfRange` on the managed path via a `BigInteger` fallback parse**

```csharp
static Result<T> Parse<T>(ReadOnlySpan<char> trimmed, IFormatProvider provider)
	where T : IBinaryInteger<T>
{
	if (NativeCapability.Available && TryParseNative<T>(trimmed, out var nativeResult))
		return nativeResult;

	if (TryRadix<T>(trimmed, out var radix))
		return new Success<T>(radix);
	if (T.TryParse(trimmed, DecimalStyles, provider, out var value))
		return new Success<T>(value);

	// T rejected it -- was the text numerically well-formed but out of T's range, or
	// genuinely not a number at all? BigInteger has no practical ceiling, so a successful
	// BigInteger parse under the same styles/provider proves the text itself was fine.
	return BigInteger.TryParse(trimmed, DecimalStyles, provider, out _) ?
		new Failure(ParseFailure.OutOfRange, trimmed, typeof(T).Name) :
		new Failure(ParseFailure.Malformed, trimmed, typeof(T).Name);
}

// Every IBinaryInteger<T> this parser supports gets its own HyperCast door -- there is no
// generic native entry point, so this dispatches on typeof(T) once per call. Each concrete
// instantiation of Parse<T> JIT-compiles with a single true branch here (the same
// typeof(T)-branch-elimination pattern Parser's own bool routing already relies on),
// so this costs nothing at runtime for any one T.
static bool TryParseNative<T>(ReadOnlySpan<char> trimmed, out Result<T> result) where T : IBinaryInteger<T>
{
	var format = HyperCast.NumFormat.Invariant;
	switch (typeof(T))
	{
		case Type t when t == typeof(sbyte):
			result = Translate<T, sbyte>(HyperCast.Cast.SByte(trimmed, format), trimmed);
			return true;
		case Type t when t == typeof(short):
			result = Translate<T, short>(HyperCast.Cast.Int16(trimmed, format), trimmed);
			return true;
		case Type t when t == typeof(int):
			result = Translate<T, int>(HyperCast.Cast.Int32(trimmed, format), trimmed);
			return true;
		case Type t when t == typeof(long):
			result = Translate<T, long>(HyperCast.Cast.Int64(trimmed, format), trimmed);
			return true;
		case Type t when t == typeof(byte):
			result = Translate<T, byte>(HyperCast.Cast.Byte(trimmed, format), trimmed);
			return true;
		case Type t when t == typeof(ushort):
			result = Translate<T, ushort>(HyperCast.Cast.UInt16(trimmed, format), trimmed);
			return true;
		case Type t when t == typeof(uint):
			result = Translate<T, uint>(HyperCast.Cast.UInt32(trimmed, format), trimmed);
			return true;
		case Type t when t == typeof(ulong):
			result = Translate<T, ulong>(HyperCast.Cast.UInt64(trimmed, format), trimmed);
			return true;
		default:
			result = default!;
			return false;
	}
}

static Result<T> Translate<T, TNative>(HyperCast.Verdict<TNative> verdict, ReadOnlySpan<char> trimmed)
	where T : IBinaryInteger<T>
	where TNative : IBinaryInteger<TNative> =>
	verdict switch
	{
		HyperCast.Success<TNative> s => new Success<T>(T.CreateChecked(s.Value)),
		HyperCast.Fault { Reason: HyperCast.CastFailure.OutOfRange } => new Failure(ParseFailure.OutOfRange, trimmed, typeof(T).Name),
		HyperCast.Fault => new Failure(ParseFailure.Malformed, trimmed, typeof(T).Name),
	};
```

- [ ] **Step 4: Run to verify the `OutOfRange`/`Malformed` distinction tests pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.IntegerParserTests"`
Expected: PASS, all green, both new tests and every pre-existing one in the class.

- [ ] **Step 5: Add integer corpus conformance tests, same dual-mode shape as Task 10**

```csharp
// Add to CorpusConformanceTests:

public static IEnumerable<object[]> Int32Vectors() => CorpusVector.Load("integers.json");

[Theory]
[MemberData(nameof(Int32Vectors))]
void Int32_native_path_matches_the_corpus(CorpusVector vector) =>
	AssertInt32MatchesCorpus(vector);

[Theory]
[MemberData(nameof(Int32Vectors))]
void Int32_managed_path_matches_the_corpus(CorpusVector vector) =>
	NativeCapability.ForManagedOnly(() => AssertInt32MatchesCorpus(vector));

static void AssertInt32MatchesCorpus(CorpusVector vector)
{
	var result = IntegerParser.ParseRequired<int>(vector.Input, CultureInfo.InvariantCulture);
	if (vector.ExpectSuccess)
		result.TryGetValue(out Success<int> success).ShouldBeTrue();
	else
		result.TryGetValue(out Failure _).ShouldBeTrue();
}
```

- [ ] **Step 6: Run the full corpus + `IntegerParserTests` suites**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.CorpusConformanceTests|*.IntegerParserTests"`
Expected: PASS, all green.

- [ ] **Step 7: Commit**

```bash
git add src/Primitives/IntegerParser.cs tests/Primitives.Tests/CorpusConformanceTests.cs tests/Primitives.Tests/IntegerParserTests.cs
git commit -m "feat: IntegerParser routes to HyperCast natively, distinguishes OutOfRange from Malformed"
```

---

## Task 12: `RealParser` — audit, `NumFormat.Detect`-equivalent port, native path, corpus tests

**Files:**
- Modify: `src/Primitives/RealParser.cs`
- Modify: `tests/Primitives.Tests/CorpusConformanceTests.cs`
- Test: `tests/Primitives.Tests/RealParserTests.cs` (extend existing)

**Interfaces:**
- Consumes: `NativeCapability`, `HyperCast.Cast.Double`/`Single`, `HyperCast.NumFormat.Detect`, `CorpusVector.Load`.

- [ ] **Step 1: Audit — read `RealParser.cs` (already read this session: no `Detect`-equivalent exists today, `RealStyles` is a fixed `NumberStyles` with no structural separator resolution) against HyperCast's `reals.json` corpus and its README's real-number door description. Run every vector from `reals.json` through the *current* `RealParser.ParseRequired<double>` and record every mismatch (a vector HyperCast accepts that the managed parser rejects, or vice versa, or a value mismatch on a shared-accept case)**

Write the audit as a throwaway console check or a first failing corpus theory (Step 3 below does the latter, which doubles as the audit output) — do not hand-write a mismatch table from memory; run it.

- [ ] **Step 2: Add real-number corpus conformance tests (managed path only, first — this is deliberately red until Step 4)**

```csharp
// Add to CorpusConformanceTests:

public static IEnumerable<object[]> RealVectors() => CorpusVector.Load("reals.json");

[Theory]
[MemberData(nameof(RealVectors))]
void Real_managed_path_matches_the_corpus(CorpusVector vector) =>
	NativeCapability.ForManagedOnly(() =>
	{
		var result = RealParser.ParseRequired<double>(vector.Input, CultureInfo.InvariantCulture);
		if (vector.ExpectSuccess)
			result.TryGetValue(out Success<double> success).ShouldBeTrue();
		else
			result.TryGetValue(out Failure _).ShouldBeTrue();
	});
```

- [ ] **Step 3: Run and record every failing vector**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.CorpusConformanceTests"`
Expected: some failures — this list is the actual scope of the `Detect`-equivalent port. Report the failing input strings before writing any implementation.

- [ ] **Step 4: Port the structural separator-resolution algorithm HyperCast's README documents (repeated separator ⇒ grouping; both present ⇒ rightmost is decimal; a non-3-digit right run ⇒ decimal; a zero-led fraction ⇒ decimal; genuinely ambiguous ⇒ `Malformed`, never guessed) into a new private `DetectSeparators` helper in `RealParser.cs`, gated behind a caller-declared `NumberStyles`-equivalent flag mirroring `NumFormat.Detect`'s own opt-in shape — exact signature is this task's own design decision, informed by Step 3's failing-vector list, not prescribed here**

- [ ] **Step 5: Add the native branch alongside the managed `Detect` port**

```csharp
static Result<T> Parse<T>(ReadOnlySpan<char> trimmed, IFormatProvider provider)
	where T : IFloatingPoint<T>
{
	if (NativeCapability.Available && typeof(T) == typeof(double))
		return HyperCast.Cast.Double(trimmed, HyperCast.NumFormat.From((CultureInfo)provider)) switch
		{
			HyperCast.Success<double> s => new Success<T>(T.CreateChecked(s.Value)),
			HyperCast.Fault { Reason: HyperCast.CastFailure.OutOfRange } => new Failure(ParseFailure.OutOfRange, trimmed, typeof(T).Name),
			HyperCast.Fault => new Failure(ParseFailure.Malformed, trimmed, typeof(T).Name),
		};

	// ... existing managed body, extended with the Step 4 Detect port ...
}
```

- [ ] **Step 6: Run the full `RealParserTests` and corpus real-number suite until every corpus vector passes on both engines**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.RealParserTests|*.CorpusConformanceTests"`
Expected: PASS, all green — iterate Steps 4-5 until this is true.

- [ ] **Step 7: Commit**

```bash
git add src/Primitives/RealParser.cs tests/Primitives.Tests/CorpusConformanceTests.cs tests/Primitives.Tests/RealParserTests.cs
git commit -m "feat: RealParser gains structural separator detection, routes to HyperCast natively"
```

---

## Task 13: `DateTimeOffsetParser` — RFC 3339 grammar rewrite, native path, corpus tests

**Files:**
- Modify: `src/Primitives/DateTimeOffsetParser.cs`
- Modify: `tests/Primitives.Tests/CorpusConformanceTests.cs`
- Test: `tests/Primitives.Tests/DateTimeOffsetParserTests.cs` (extend existing)

**Interfaces:**
- Consumes: `NativeCapability`, `HyperCast.Cast.Timestamp`, `CorpusVector.Load`.

- [ ] **Step 1: Add timestamp corpus conformance tests against the current implementation first (managed path)**

```csharp
// Add to CorpusConformanceTests:

public static IEnumerable<object[]> TimestampVectors() => CorpusVector.Load("timestamp.json");

[Theory]
[MemberData(nameof(TimestampVectors))]
void Timestamp_managed_path_matches_the_corpus(CorpusVector vector) =>
	NativeCapability.ForManagedOnly(() =>
	{
		var result = DateTimeOffsetParser.ParseRequired(vector.Input);
		if (vector.ExpectSuccess)
			result.TryGetValue(out Success<DateTimeOffset> success).ShouldBeTrue();
		else
			result.TryGetValue(out Failure _).ShouldBeTrue();
	});
```

- [ ] **Step 2: Run and record every failing vector — this is the actual scope of the grammar gap, not the format-array theory from the spec until proven here**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.CorpusConformanceTests"`
Expected: failures on any RFC 3339-valid fractional-second precision (or other) shape not covered by the four hardcoded `_isoFormats` strings. Report the list.

- [ ] **Step 3: Replace `ParseIso`'s fixed-format-array approach with a real RFC 3339 grammar, keeping the mandatory-zone requirement `ParseIso`'s doc comment already promises**

```csharp
static Result<DateTimeOffset> ParseIso(ReadOnlySpan<char> trimmed)
{
	// DateTimeOffset.TryParse (not TryParseExact) accepts the full RFC 3339 grammar
	// including any fractional-second precision, but also accepts zone-less input under
	// AssumeUniversal -- so the mandatory-zone requirement is enforced explicitly by
	// checking for a trailing 'Z'/'z' or an explicit +hh:mm/-hh:mm offset before trusting
	// the parse, not by TryParse's styles alone.
	if (!HasExplicitZone(trimmed))
		return new Failure(ParseFailure.Malformed, trimmed, ExpectedType, IsoLabel);

	return DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, IsoStyles, out var value) &&
		!IsSentinel(value) ?
			new Success<DateTimeOffset>(value) :
			new Failure(ParseFailure.Malformed, trimmed, ExpectedType, IsoLabel);
}

static bool HasExplicitZone(ReadOnlySpan<char> trimmed)
{
	if (trimmed.IsEmpty)
		return false;
	if (trimmed[^1] is 'Z' or 'z')
		return true;
	// A numeric offset looks like ...+hh:mm or ...-hh:mm in the last 6 characters --
	// distinguished from the date's own '-' separators by requiring a ':' two
	// characters before the end.
	return trimmed.Length >= 6 && trimmed[^3] == ':' && trimmed[^6] is '+' or '-';
}
```

Reconcile this against Step 2's actual failing-vector list before finalizing — `HasExplicitZone`'s exact shape is this task's design decision, informed by what the corpus actually exercises, not prescribed to the byte here.

- [ ] **Step 4: Add the native branch**

```csharp
static Result<DateTimeOffset> ParseIso(ReadOnlySpan<char> trimmed)
{
	if (NativeCapability.Available)
		return HyperCast.Cast.Timestamp(trimmed) switch
		{
			HyperCast.Success<DateTimeOffset> s => new Success<DateTimeOffset>(s.Value),
			HyperCast.Fault { Reason: HyperCast.CastFailure.OutOfRange } => new Failure(ParseFailure.OutOfRange, trimmed, ExpectedType, IsoLabel),
			HyperCast.Fault => new Failure(ParseFailure.Malformed, trimmed, ExpectedType, IsoLabel),
		};

	// ... Step 3's managed body ...
}
```

- [ ] **Step 5: Run until every corpus vector and every pre-existing test passes on both engines**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.DateTimeOffsetParserTests|*.CorpusConformanceTests"`
Expected: PASS, all green — iterate Steps 3-4 until true.

- [ ] **Step 6: Re-run the identifier/parser benchmark rig to confirm the 7.7× timestamp outlier from the spec's §1 either closes or is explained**

Run: `dotnet run -c Release --project benchmarks/Primitives.Benchmarks -- --filter "*HyperCastBenchmarks*"`
Report the new `TimestampSvartalfheim` number against the spec's baseline (438.46 ns) — file the result as a spec amendment per this realm's benchmark doctrine, not a loose note.

- [ ] **Step 7: Commit**

```bash
git add src/Primitives/DateTimeOffsetParser.cs tests/Primitives.Tests/CorpusConformanceTests.cs tests/Primitives.Tests/DateTimeOffsetParserTests.cs
git commit -m "feat: DateTimeOffsetParser parses real RFC 3339 grammar, routes to HyperCast natively"
```

---

## Task 14: Remaining parsers, group A — `CharParser`, `TimeSpanParser`

**Files:**
- Modify: `src/Primitives/CharParser.cs`, `src/Primitives/TimeSpanParser.cs`
- Modify: `tests/Primitives.Tests/CorpusConformanceTests.cs`
- Test: `tests/Primitives.Tests/CharParserTests.cs`, `tests/Primitives.Tests/TimeSpanParserTests.cs` (extend existing)

**Interfaces:**
- Consumes: `NativeCapability`, `HyperCast.Cast.Duration` (for `TimeSpanParser`), `CorpusVector.Load`. `CharParser` has no direct HyperCast door (HyperCast has no `char` primitive) — audit whether it's in scope for a native path at all, or stays managed-only permanently.

- [ ] **Step 1: Audit `CharParser.cs` against HyperCast's door list (§1 of the design spec: boolean, integers, reals, uuid, timestamp, date/time, local datetime, excel serial, duration — no char door exists)**

Read `src/Primitives/CharParser.cs`. If it's a thin wrapper over integer code-point parsing with no independent grammar of its own, confirm in this step's output that it has no HyperCast counterpart and **stays managed-only** — do not invent a native path that doesn't exist upstream. If it has real independent grammar, report that finding instead before proceeding.

- [ ] **Step 2: Audit `TimeSpanParser.cs` against HyperCast's `duration.json` corpus and its README's duration-door description (ISO 8601 fixed components, invariant colon form, protobuf JSON seconds, comma decimal mark)**

Add the corpus theory test (same shape as Task 13 Step 1) against the current managed implementation, run it, record every mismatch.

- [ ] **Step 3: Add the native branch to `TimeSpanParser`, informed by Step 2's findings**

Open `src/Primitives/TimeSpanParser.cs` and insert this guard at the top of the existing `Parse` method (the method's remaining body — whatever Step 2's audit found needs to change about it, if anything — stays below it as the managed fallback):

```csharp
static Result<TimeSpan> Parse(ReadOnlySpan<char> trimmed)
{
	if (NativeCapability.Available)
		return HyperCast.Cast.Duration(trimmed) switch
		{
			HyperCast.Success<TimeSpan> s => new Success<TimeSpan>(s.Value),
			HyperCast.Fault { Reason: HyperCast.CastFailure.OutOfRange } => new Failure(ParseFailure.OutOfRange, trimmed, ExpectedType),
			HyperCast.Fault => new Failure(ParseFailure.Malformed, trimmed, ExpectedType),
		};

	// ... existing managed body continues here, unchanged unless Step 2 found a real gap ...
}
```

- [ ] **Step 4: Run until `TimeSpanParserTests` and the duration corpus theory pass on both engines**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.TimeSpanParserTests|*.CorpusConformanceTests"`
Expected: PASS, all green.

- [ ] **Step 5: Commit**

```bash
git add src/Primitives/TimeSpanParser.cs tests/Primitives.Tests/CorpusConformanceTests.cs tests/Primitives.Tests/TimeSpanParserTests.cs
git commit -m "feat: TimeSpanParser routes to HyperCast natively; CharParser confirmed managed-only (no upstream door)"
```

---

## Task 15: Remaining parsers, group B — `DateOnlyParser`, `DateTimeParser`, `TimeOnlyParser`, `TimeZoneParser`, `TemporalFusion`

**Files:**
- Modify: `src/Primitives/DateOnlyParser.cs`, `src/Primitives/DateTimeParser.cs`, `src/Primitives/TimeOnlyParser.cs`, `src/Primitives/TimeZoneParser.cs`, `src/Primitives/TemporalFusion.cs`
- Modify: `tests/Primitives.Tests/CorpusConformanceTests.cs`
- Test: the five matching existing test files, extended.

**Interfaces:**
- Consumes: `NativeCapability`, `HyperCast.Cast.Date`/`DateTime`/`Time` (declared-order doors), `CorpusVector.Load`. `TimeZoneParser` and `TemporalFusion` are audited for whether they have a direct HyperCast counterpart at all (HyperCast's door list has no explicit time-zone-name resolution or fusion concept — likely composition logic over the other doors, not a door itself).

- [ ] **Step 1: Audit `TimeZoneParser.cs` and `TemporalFusion.cs` first — determine whether either has an independent HyperCast counterpart or is pure composition over doors already native-wired by Tasks 11-14**

If either is composition-only (calling into `DateOnlyParser`/`TimeOnlyParser`/etc. and combining results, with no text-grammar of its own), it needs **no native branch of its own** — it inherits native behavior automatically once its dependencies are wired. Report which case applies before proceeding; do not add a branch that has nothing to route to.

- [ ] **Step 2: Audit `DateOnlyParser.cs`, `DateTimeParser.cs`, `TimeOnlyParser.cs` against HyperCast's date/date-time/time doors (declared field order for separated dates, strict `yyyy-MM-dd`, 24-hour `HH:mm[:ss[.f≤9]]`) and their corresponding corpus files, same audit-first shape as Tasks 12-14**

- [ ] **Step 3: Add native branches to the three doors confirmed in Step 2 to have one, informed by that audit's findings**

For whichever of the three this task's Step 2 confirms have a matching door, insert the corresponding guard at the top of that parser's existing `Parse` method, same shape as every prior native branch in this plan:

```csharp
// DateOnlyParser.cs — HyperCast.DateOrder is this task's own mapping from whatever
// caller-declared field-order concept DateOnlyParser already carries (see Step 2's audit).
static Result<DateOnly> Parse(ReadOnlySpan<char> trimmed, HyperCast.DateOrder order)
{
	if (NativeCapability.Available)
		return HyperCast.Cast.Date(trimmed, order) switch
		{
			HyperCast.Success<DateOnly> s => new Success<DateOnly>(s.Value),
			HyperCast.Fault { Reason: HyperCast.CastFailure.OutOfRange } => new Failure(ParseFailure.OutOfRange, trimmed, ExpectedType),
			HyperCast.Fault => new Failure(ParseFailure.Malformed, trimmed, ExpectedType),
		};

	// ... existing managed body continues here ...
}
```

```csharp
// DateTimeParser.cs — HyperCast.Cast.DateTime(ReadOnlySpan<char>, DateOrder) -> Verdict<System.DateTime>
static Result<DateTime> Parse(ReadOnlySpan<char> trimmed, HyperCast.DateOrder order)
{
	if (NativeCapability.Available)
		return HyperCast.Cast.DateTime(trimmed, order) switch
		{
			HyperCast.Success<DateTime> s => new Success<DateTime>(s.Value),
			HyperCast.Fault { Reason: HyperCast.CastFailure.OutOfRange } => new Failure(ParseFailure.OutOfRange, trimmed, ExpectedType),
			HyperCast.Fault => new Failure(ParseFailure.Malformed, trimmed, ExpectedType),
		};

	// ... existing managed body continues here ...
}
```

```csharp
// TimeOnlyParser.cs — HyperCast.Cast.Time(ReadOnlySpan<char>) -> Verdict<TimeOnly>, no declared order
static Result<TimeOnly> Parse(ReadOnlySpan<char> trimmed)
{
	if (NativeCapability.Available)
		return HyperCast.Cast.Time(trimmed) switch
		{
			HyperCast.Success<TimeOnly> s => new Success<TimeOnly>(s.Value),
			HyperCast.Fault { Reason: HyperCast.CastFailure.OutOfRange } => new Failure(ParseFailure.OutOfRange, trimmed, ExpectedType),
			HyperCast.Fault => new Failure(ParseFailure.Malformed, trimmed, ExpectedType),
		};

	// ... existing managed body continues here ...
}
```

Only insert the guard for doors Step 2 actually confirmed exist and match — skip any of the three Step 2 ruled out.

- [ ] **Step 4: Run every affected test class and corpus theory until green on both engines**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.DateOnlyParserTests|*.DateTimeParserTests|*.TimeOnlyParserTests|*.TimeZoneParserTests|*.TemporalFusionTests|*.CorpusConformanceTests"`
Expected: PASS, all green.

- [ ] **Step 5: Commit**

```bash
git add src/Primitives/DateOnlyParser.cs src/Primitives/DateTimeParser.cs src/Primitives/TimeOnlyParser.cs src/Primitives/TimeZoneParser.cs src/Primitives/TemporalFusion.cs tests/Primitives.Tests/CorpusConformanceTests.cs tests/Primitives.Tests/DateOnlyParserTests.cs tests/Primitives.Tests/DateTimeParserTests.cs tests/Primitives.Tests/TimeOnlyParserTests.cs tests/Primitives.Tests/TimeZoneParserTests.cs tests/Primitives.Tests/TemporalFusionTests.cs
git commit -m "feat: remaining temporal parsers route to HyperCast natively where a door exists"
```

---

## Task 16: `CLAUDE.md` doctrine updates

**Files:**
- Modify: `Svartalfheim/CLAUDE.md`

**Interfaces:** none — documentation only.

- [ ] **Step 1: Add the spec to the spec index table**

In the "Spec index" table, add:

```markdown
| HyperUuid/HyperCast ingestion | `2026-09-03-hyperuuid-hypercast-ingestion-design.md` |
```

- [ ] **Step 2: Add a new Architecture Facts bullet documenting the seam**

```markdown
- **The native-engine seam (`NativeCapability`)** — `Identifiers` and the scalar parsers route
  to HyperUuid/HyperCast on platforms/RIDs they cover (a trimmer-foldable `OperatingSystem`
  check plus a cached native probe for RID-family gaps like glibc vs. musl), falling back to
  the original managed implementation everywhere else, including the not-yet-existing MAUI
  target. Public API is unchanged; translation from `Verdict<T>`/`Fault` to `Result<T>`/
  `Failure` happens at the call site. HyperCast is the source of truth for parsing grammar —
  its `corpus/*.json` vectors are the cross-engine, cross-platform conformance authority, run
  in CI against both engines via `NativeCapability.ForManagedOnly`. Design:
  `2026-09-03-hyperuuid-hypercast-ingestion-design.md`.
```

- [ ] **Step 3: Update the `ParseFailure` mention in the NORSE ledger bullet, if it names the old member set**

Check whether the existing "The NORSE ledger" bullet or any other prose in this file enumerates `ParseFailure`'s members by name; if so, update to the renumbered set from Task 8.

- [ ] **Step 4: Add a Build & Test bullet for the dual-mode corpus run**

```markdown
- **Corpus conformance runs twice** — `dotnet test tests/Primitives.Tests -- --filter-class
  "*.CorpusConformanceTests"` exercises both the native and (`NativeCapability.ForManagedOnly`-
  forced) managed paths against HyperCast's own corpus in the same run. Every dev/CI box
  available today is native-capable, so this is the only thing that keeps the managed fallback
  proven rather than dead code until a real MAUI target exists.
```

- [ ] **Step 5: Stage (do not commit — human commits per this repo's policy)**

```bash
git add Svartalfheim/CLAUDE.md
```

---

## Task 17: Full-suite verification pass

**Files:** none created/modified — verification only.

- [ ] **Step 1: Full solution build**

Run: `dotnet build Svartalfheim.slnx`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2: Full test suite**

Run: `dotnet test Svartalfheim.slnx`
Expected: every project passes, including `Primitives.Tests`' full corpus + parser + identifier suites.

- [ ] **Step 3: AOT smoke, full surface**

Run: `dotnet publish tests/smoke/Primitives.Aot.Smoke -c Release`, run the published binary.
Expected: zero warnings, exit 0, every `Check` reports pass.

- [ ] **Step 4: Benchmark rig, both files, confirm the seam is actually faster in practice, not just in isolation**

Run: `dotnet run -c Release --project benchmarks/Primitives.Benchmarks -- --filter "*IdentifierBenchmarks*|*HyperCastBenchmarks*"`
Record the final numbers as a spec amendment to `2026-09-03-hyperuuid-hypercast-ingestion-design.md`'s §1 table, per this realm's "benchmark findings are court filings" doctrine — do not leave them as a loose PR comment.

- [ ] **Step 5: Report final status** — do not commit this task's output; it's verification, not a change.
