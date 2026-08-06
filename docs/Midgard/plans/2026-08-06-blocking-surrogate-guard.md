# Blocking Surrogate-Registration Guard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the defect filed in `../2026-08-03-surrogate-guard-race-filing.md` for good — replace every flag-first, non-blocking "registered" guard around `RuntimeTypeModel.Default` with a genuinely blocking one, so a concurrent caller waits for registration to finish instead of racing a half-built model.

**Architecture:** Three independent call sites carry the identical defect shape (`Interlocked.Exchange`/`ConditionalWeakTable.TryAdd` as a one-shot "claimed" flag, with the real work happening *after* the flag is already visibly set to other threads): `Norse.Infrastructure.Web.Grpc.IdentifierSerializers.Register` and its sibling `ResultSerializers.Register` (both hand-written, directly unit-testable — the latter's own doc comment says it "mirrors `IdentifierSerializers`'s registration mechanism", including the bug) and the two generator emitters' `RegisterNorseOutcomeSurrogates()` (source-generated, testable only via emitted-shape assertions, consistent with this suite's existing style). All switch to `System.Lazy<bool>` with `LazyThreadSafetyMode.ExecutionAndPublication` — the BCL's own blocking-run-once primitive: every concurrent caller either runs the factory (exactly one winner) or blocks until the winner finishes, then all see the same completed state. No new shared abstraction is introduced — per-call-site `Lazy<T>` is sufficient and this plan does not attempt to design a longer-term shared testing/concurrency abstraction for Asgard or Midgard; that is explicitly deferred.

`ResultSerializers.Register` (Task 1b) was not in the original filing or the first pass of this plan — it surfaced as a ⚠️ finding during Task 1's review (same file family, same defect, unpatched) and is added here rather than left as a known gap, per the standing instruction to fix this defect class for good regardless of blast radius.

**Tech Stack:** C#, `System.Lazy<T>`, `System.Runtime.CompilerServices.ConditionalWeakTable<TKey,TValue>`, xUnit v3 + Shouldly, Roslyn `CSharpGeneratorDriver` (existing generator test harness).

## Global Constraints

- Tabs for indentation; `omit_if_default` accessibility; `sealed` by default (already the case for both touched classes).
- Generator emitters build C# text via `sb.AppendCSharp(...)`/raw string literals — no direct `AppendLine` calls (`../../Asgard/specs/2026-07-25-generator-authoring-toolkit-and-raw-string-house-style-design.md`). Both emitters already follow this; preserve the pattern.
- xUnit v3 on Microsoft.Testing.Platform, Shouldly assertions only, no FluentAssertions/Moq.
- No new public API surface beyond what's specified below — this is a bug fix, not a feature.
- Every existing assertion in the four touched test files must keep passing unmodified except where a task explicitly adds a new one.

---

### Task 1: Blocking guard in `IdentifierSerializers.Register`

**Files:**
- Modify: `Midgard/src/Infrastructure.Web.Grpc/IdentifierSerializers.cs`
- Test: `Midgard/tests/Infrastructure.Web.Grpc.Tests/IdentifierSerializersTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `IdentifierSerializers.Register(RuntimeTypeModel model)` — same public signature, same idempotent-per-model behavior, now blocking under concurrent first touch instead of racing.

- [ ] **Step 1: Write the failing concurrency test**

Add to `Midgard/tests/Infrastructure.Web.Grpc.Tests/IdentifierSerializersTests.cs` (needs `using System.Threading;` and `using Norse.Primitives.Identifiers;` added to the top of the file alongside the existing `using ProtoBuf;`/`using ProtoBuf.Meta;`):

```csharp
[Fact]
async Task Register_does_not_return_until_registration_is_complete_under_concurrent_first_touch()
{
	// Regression test for the race filed 2026-08-03
	// (../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md): the old flag-first
	// guard let a second caller observe "claimed" and return immediately while the first caller's
	// registration was still mid-flight. Every concurrent first-touch caller, across many fresh
	// models, must see SequentialGuid registered by the time its OWN call to Register returns -- not
	// just eventually, and not just "no exception was thrown".
	const int ModelCount = 500;
	const int CallersPerModel = 8;

	await Task.WhenAll(Enumerable.Range(0, ModelCount).Select(async _ =>
	{
		var model = RuntimeTypeModel.Create();
		using Barrier barrier = new(CallersPerModel);

		await Task.WhenAll(Enumerable.Range(0, CallersPerModel).Select(_ => Task.Run(() =>
		{
			barrier.SignalAndWait();
			IdentifierSerializers.Register(model);
			model.IsDefined(typeof(SequentialGuid)).ShouldBeTrue();
		})));
	}));
}
```

This needs `using System.Linq;` too if not already implied — check the file's existing usings; if `System.Linq`/`System.Threading.Tasks` aren't present, add them. **Correction (final whole-branch review, 2026-08-06):** this step originally claimed the test "passes against the CURRENT (buggy) implementation almost always in practice" and told the implementer not to rely on it failing red. That claim was empirically false. The final reviewer built a replica of the old flag-first guard and ran this test's exact logic against it, measuring 63–620 race violations per 4000 concurrent callers (depending on which type the assertion targets) against the old guard, and 0 against the new `Lazy<bool>` guard — the test reliably fails red against the pre-fix code, every run. Proceed to Step 2 regardless; this step remains "test compiles and the assertions describe the required behavior," now backed by verified true red/green behavior instead of the incorrect assumption that it wouldn't fail red.

- [ ] **Step 2: Run the test to confirm it compiles and passes (may already pass — the point is the fix must not regress it)**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Grpc.Tests --filter "Register_does_not_return_until_registration_is_complete_under_concurrent_first_touch"`
Expected: compiles; passes or fails is not diagnostic on its own (see note above) — just confirm it runs.

- [ ] **Step 3: Replace the flag-first guard with a blocking one**

Replace the full contents of `Midgard/src/Infrastructure.Web.Grpc/IdentifierSerializers.cs` with:

```csharp
using System.Runtime.CompilerServices;
using Norse.Primitives.Identifiers;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
/// Applies the Norse wire law to a protobuf-net <see cref="RuntimeTypeModel"/>: every member of every
/// contract type at <see cref="CompatibilityLevel.Level300"/>, and every identifier on the wire as a
/// bare <c>bytes</c> field carrying 16 bytes in RFC 9562 order — never the legacy <c>bcl.Guid</c>
/// encoding, never the 36-character string.
/// </summary>
/// <remarks>
/// The level is applied per <see cref="ValueMember"/> rather than via
/// <see cref="RuntimeTypeModel.DefaultCompatibilityLevel"/> because protobuf-net categorically refuses
/// that setter on <see cref="RuntimeTypeModel.Default"/> — and the default model is exactly where the
/// generated client/server wiring registers. Member-level configuration wins over every ambient level,
/// so the two paths are wire-identical.
/// </remarks>
public static class IdentifierSerializers
{
	// Lazy<T> with ExecutionAndPublication, not a flag-first guard: a second caller for the same model
	// blocks until the winning caller's registration completes instead of racing a half-built model
	// (../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md).
	static readonly ConditionalWeakTable<RuntimeTypeModel, Lazy<bool>> _registered = [];

	/// <summary>
	/// Registers the wire law on <paramref name="model"/>. Idempotent and safe under concurrent first
	/// call per model — a concurrent caller blocks until registration completes rather than observing a
	/// half-built model. Must run before contract types enter the model — the sweep only sees types
	/// added after registration.
	/// </summary>
	public static void Register(RuntimeTypeModel model)
	{
		ArgumentNullException.ThrowIfNull(model);
		_ = _registered.GetValue(model, CreateGuard).Value;
	}

	static Lazy<bool> CreateGuard(RuntimeTypeModel model) =>
		new(() =>
		{
			model.AfterApplyDefaultBehaviour += ApplyWireLaw;
			model.Add(typeof(SequentialGuid), applyDefaultBehaviour: false).SerializerType =
				typeof(SequentialGuidSerializer);
			model.Add(typeof(DeterministicGuid), applyDefaultBehaviour: false).SerializerType =
				typeof(DeterministicGuidSerializer);
			return true;
		}, LazyThreadSafetyMode.ExecutionAndPublication);

	static void ApplyWireLaw(object? sender, TypeAddedEventArgs e)
	{
		foreach (var field in e.MetaType.GetFields())
		{
			field.CompatibilityLevel = CompatibilityLevel.Level300;
			if (field.MemberType == typeof(Guid) || field.MemberType == typeof(Guid?))
				field.DataFormat = DataFormat.FixedSize;
		}
	}
}
```

`ConditionalWeakTable<TKey,TValue>.GetValue(key, createValueCallback)` may invoke `createValueCallback` more than once under a race (constructing a throwaway `Lazy<bool>` is cheap and side-effect-free — it doesn't run the factory), but it atomically publishes exactly one winning `Lazy<bool>` per key to every caller, so `.Value` on the returned instance is what provides the actual once-and-blocking guarantee.

- [ ] **Step 4: Run the full `Infrastructure.Web.Grpc.Tests` project**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Grpc.Tests`
Expected: all tests pass, including every pre-existing test in `IdentifierSerializersTests.cs` (idempotency, wire-format, schema tests) and the new concurrency test from Step 1.

- [ ] **Step 5: Commit**

```bash
git add Midgard/src/Infrastructure.Web.Grpc/IdentifierSerializers.cs Midgard/tests/Infrastructure.Web.Grpc.Tests/IdentifierSerializersTests.cs
git commit -m "fix: block concurrent callers of IdentifierSerializers.Register instead of racing a half-built model"
```

---

### Task 1b: Blocking guard in `ResultSerializers.Register`

**Files:**
- Modify: `Midgard/src/Infrastructure.Web.Grpc/ResultSerializers.cs`
- Test: `Midgard/tests/Infrastructure.Web.Grpc.Tests/ResultSerializerTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1 (independent call site, identical fix shape — see Task 1's finished `IdentifierSerializers.cs` for the pattern this task mirrors, but implement this task from the code below, not by copying that file).
- Produces: `ResultSerializers.Register(RuntimeTypeModel model)` — same public signature, same idempotent-per-model behavior, now blocking under concurrent first touch instead of racing.

- [ ] **Step 1: Write the failing concurrency test**

Add to `Midgard/tests/Infrastructure.Web.Grpc.Tests/ResultSerializerTests.cs` (add `using System.Threading;` and `using System.Threading.Tasks;` and `using System.Linq;` to the top of the file if not already present — check the existing usings first and only add what's missing):

```csharp
[Fact]
async Task Register_does_not_return_until_registration_is_complete_under_concurrent_first_touch()
{
	// Regression test for the race filed 2026-08-03
	// (../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md), same defect shape
	// as IdentifierSerializers.Register (see IdentifierSerializersTests.cs for the sibling test):
	// the old flag-first guard let a second caller observe "claimed" and return immediately while
	// the first caller's registration was still mid-flight. Every concurrent first-touch caller,
	// across many fresh models, must see Result<int> registered by the time its OWN call to
	// Register returns -- not just eventually, and not just "no exception was thrown".
	const int ModelCount = 500;
	const int CallersPerModel = 8;

	await Task.WhenAll(Enumerable.Range(0, ModelCount).Select(async _ =>
	{
		var model = RuntimeTypeModel.Create();
		using Barrier barrier = new(CallersPerModel);

		await Task.WhenAll(Enumerable.Range(0, CallersPerModel).Select(_ => Task.Run(() =>
		{
			barrier.SignalAndWait();
			ResultSerializers.Register(model);
			model.IsDefined(typeof(Result<int>)).ShouldBeTrue();
		})));
	}));
}
```

Check the top of `ResultSerializerTests.cs` for the existing namespace import needed for `Result<T>` (it is `Norse.Primitives`, per `ResultSerializers.cs`'s own usings) and add it if the test file does not already have it.

- [ ] **Step 2: Run the test to confirm it compiles and runs**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Grpc.Tests --filter "Register_does_not_return_until_registration_is_complete_under_concurrent_first_touch"`
Expected: both this test and Task 1's identically-named test in `IdentifierSerializersTests.cs` run (the filter matches the method name in both files) — compiles, runs. **Correction (final whole-branch review, 2026-08-06):** this step originally said pass/fail here "is not diagnostic on its own," mirroring Task 1's now-corrected claim. That is wrong — the final reviewer independently verified true red/green behavior: 63–620 race violations per 4000 concurrent callers against a replica of the old flag-first guard (depending on assertion target), 0 against the new `Lazy<bool>` guard. The fix must not regress it.

- [ ] **Step 3: Replace the flag-first guard with a blocking one**

In `Midgard/src/Infrastructure.Web.Grpc/ResultSerializers.cs`, change the field declaration:

```csharp
	static readonly ConditionalWeakTable<RuntimeTypeModel, RuntimeTypeModel> _registered = [];
```

to:

```csharp
	// Lazy<T> with ExecutionAndPublication, not a flag-first guard: a second caller for the same
	// model blocks until the winning caller's registration completes instead of racing a half-built
	// model (../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md).
	static readonly ConditionalWeakTable<RuntimeTypeModel, Lazy<bool>> _registered = [];
```

and change the body of `Register`:

```csharp
	public static void Register(RuntimeTypeModel model)
	{
		ArgumentNullException.ThrowIfNull(model);
		if (!_registered.TryAdd(model, model))
			return;

		model.AfterApplyDefaultBehaviour += (_, e) => RegisterEnumResults(model, e);
		model.Add(typeof(DateTimeOffset), applyDefaultBehaviour: false).SerializerType = typeof(DateTimeOffsetSerializer);

		RegisterScalar<bool>(model);
		RegisterScalar<byte>(model);
		RegisterScalar<sbyte>(model);
		RegisterScalar<short>(model);
		RegisterScalar<ushort>(model);
		RegisterScalar<int>(model);
		RegisterScalar<uint>(model);
		RegisterScalar<long>(model);
		RegisterScalar<ulong>(model);
		RegisterScalar<float>(model);
		RegisterScalar<double>(model);
		RegisterScalar<decimal>(model);
		RegisterScalar<char>(model);
		RegisterScalar<string>(model);
		RegisterScalar<Guid>(model);
		RegisterScalar<DateOnly>(model);
		RegisterScalar<DateTime>(model);
		RegisterScalar<DateTimeOffset>(model);
		RegisterScalar<TimeOnly>(model);
		RegisterScalar<TimeSpan>(model);
	}
```

to:

```csharp
	public static void Register(RuntimeTypeModel model)
	{
		ArgumentNullException.ThrowIfNull(model);
		_ = _registered.GetValue(model, CreateGuard).Value;
	}

	static Lazy<bool> CreateGuard(RuntimeTypeModel model) =>
		new(() =>
		{
			model.AfterApplyDefaultBehaviour += (_, e) => RegisterEnumResults(model, e);
			model.Add(typeof(DateTimeOffset), applyDefaultBehaviour: false).SerializerType = typeof(DateTimeOffsetSerializer);

			RegisterScalar<bool>(model);
			RegisterScalar<byte>(model);
			RegisterScalar<sbyte>(model);
			RegisterScalar<short>(model);
			RegisterScalar<ushort>(model);
			RegisterScalar<int>(model);
			RegisterScalar<uint>(model);
			RegisterScalar<long>(model);
			RegisterScalar<ulong>(model);
			RegisterScalar<float>(model);
			RegisterScalar<double>(model);
			RegisterScalar<decimal>(model);
			RegisterScalar<char>(model);
			RegisterScalar<string>(model);
			RegisterScalar<Guid>(model);
			RegisterScalar<DateOnly>(model);
			RegisterScalar<DateTime>(model);
			RegisterScalar<DateTimeOffset>(model);
			RegisterScalar<TimeOnly>(model);
			RegisterScalar<TimeSpan>(model);
			return true;
		}, LazyThreadSafetyMode.ExecutionAndPublication);
```

Leave `RegisterScalar<T>` and `RegisterEnumResults` exactly as they are — only the field declaration and the body of `Register` change; everything else in the file (the class doc comment, `IllegalWriteMessage`, the two `UnconditionalSuppressMessage` attributes on `RegisterEnumResults`) stays untouched.

- [ ] **Step 4: Run the full `Infrastructure.Web.Grpc.Tests` project**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Grpc.Tests`
Expected: all tests pass, including every pre-existing test in `ResultSerializerTests.cs`, Task 1's `IdentifierSerializersTests.cs` tests, and this task's new concurrency test.

- [ ] **Step 5: Commit**

```bash
git add Midgard/src/Infrastructure.Web.Grpc/ResultSerializers.cs Midgard/tests/Infrastructure.Web.Grpc.Tests/ResultSerializerTests.cs
git commit -m "fix: block concurrent callers of ResultSerializers.Register instead of racing a half-built model"
```

---

### Task 2: Blocking guard in the server registration emitter

**Files:**
- Modify: `Midgard/gen/Infrastructure.Web.Server.Generator/ServerRegistrationEmitter.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Generator.Tests/GrpcServerRegistrationGeneratorTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1 (independent call site, same fix shape).
- Produces: emitted `NorseGrpcServerRegistration.RegisterNorseOutcomeSurrogates()` — same public signature and call sites (`MapNorseGrpcServices` still calls it first), now backed by a blocking `Lazy<bool>` instead of `Interlocked.Exchange`.

- [ ] **Step 1: Write the failing shape assertion**

Add to `Midgard/tests/Infrastructure.Web.Server.Generator.Tests/GrpcServerRegistrationGeneratorTests.cs` (any position among the existing `[Fact]` methods):

```csharp
[Fact]
void RegisterNorseOutcomeSurrogates_uses_a_blocking_guard_not_a_flag_first_race()
{
	var generated = Generate(Contract);
	generated.ShouldNotContain("Interlocked.Exchange");
	generated.ShouldContain("global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication");
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Generator.Tests --filter "RegisterNorseOutcomeSurrogates_uses_a_blocking_guard_not_a_flag_first_race"`
Expected: FAIL — the current emitter still contains `Interlocked.Exchange` and no `LazyThreadSafetyMode`.

- [ ] **Step 3: Change the emitted guard shape**

In `Midgard/gen/Infrastructure.Web.Server.Generator/ServerRegistrationEmitter.cs`, replace this block inside the `Emit` method's raw string literal:

```csharp
			public static class NorseGrpcServerRegistration
			{
				static int _surrogatesRegistered;

				/// <summary>Registers the Outcome&lt;T&gt; passthrough surrogates, idempotent per type.</summary>
				public static void RegisterNorseOutcomeSurrogates()
				{
					if (global::System.Threading.Interlocked.Exchange(ref _surrogatesRegistered, 1) == 1)
						return;
					var model = global::ProtoBuf.Meta.RuntimeTypeModel.Default;
					global::Norse.Infrastructure.Web.Grpc.IdentifierSerializers.Register(model);
			{{SurrogateGuards(payloads)}}
				}
```

with:

```csharp
			public static class NorseGrpcServerRegistration
			{
				// Blocking, not flag-first: a concurrent caller waits for registration to finish instead of
				// observing a "claimed" flag and proceeding against a half-built RuntimeTypeModel.Default.
				static readonly global::System.Lazy<bool> _surrogatesRegistered = new(
					RegisterNorseOutcomeSurrogatesCore, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

				/// <summary>Registers the Outcome&lt;T&gt; passthrough surrogates, idempotent per type and safe under concurrent first call.</summary>
				public static void RegisterNorseOutcomeSurrogates() => _ = _surrogatesRegistered.Value;

				static bool RegisterNorseOutcomeSurrogatesCore()
				{
					var model = global::ProtoBuf.Meta.RuntimeTypeModel.Default;
					global::Norse.Infrastructure.Web.Grpc.IdentifierSerializers.Register(model);
			{{SurrogateGuards(payloads)}}
					return true;
				}
```

Leave everything else in the file (the `MapNorseGrpcServices` method, `SurrogateGuards`, `MapServices` helpers) untouched — `RegisterNorseOutcomeSurrogates()`'s call sites and signature are unchanged, only its body.

- [ ] **Step 4: Run the new test to verify it passes**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Generator.Tests --filter "RegisterNorseOutcomeSurrogates_uses_a_blocking_guard_not_a_flag_first_race"`
Expected: PASS

- [ ] **Step 5: Run the full generator test project to confirm no regressions**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Generator.Tests`
Expected: all tests pass, including `Emits_one_guarded_SetSurrogate_per_distinct_payload_including_Unit`, `RegisterNorseOutcomeSurrogates_is_called_first_inside_MapNorseGrpcServices`, `Registers_the_identifier_serializers_before_the_Outcome_surrogates`, and `Emitted_source_compiles_cleanly_against_real_ASP_NET_Core_and_protobuf_net_references` — none of these assert on the internals this task changed, only on call ordering and emitted surrogate/namespace text that Step 3 preserves.

- [ ] **Step 6: Commit**

```bash
git add Midgard/gen/Infrastructure.Web.Server.Generator/ServerRegistrationEmitter.cs Midgard/tests/Infrastructure.Web.Server.Generator.Tests/GrpcServerRegistrationGeneratorTests.cs
git commit -m "fix: emit a blocking surrogate-registration guard from the server registration generator"
```

---

### Task 3: Blocking guard in the client registration emitter

**Files:**
- Modify: `Midgard/gen/Infrastructure.Web.Client.Generator/ClientRegistrationEmitter.cs`
- Test: `Midgard/tests/Infrastructure.Web.Client.Generator.Tests/GrpcClientRegistrationGeneratorTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1 or 2 (independent call site, identical fix shape to Task 2, mirrored for the client-side generator).
- Produces: emitted `NorseGrpcClientRegistration.RegisterNorseOutcomeSurrogates()` — same public signature and call site (`AddNorseGrpcClients` still calls it first), now backed by a blocking `Lazy<bool>`.

- [ ] **Step 1: Write the failing shape assertion**

Add to `Midgard/tests/Infrastructure.Web.Client.Generator.Tests/GrpcClientRegistrationGeneratorTests.cs`:

```csharp
[Fact]
void RegisterNorseOutcomeSurrogates_uses_a_blocking_guard_not_a_flag_first_race()
{
	var generated = Generate(Contract);
	generated.ShouldNotContain("Interlocked.Exchange");
	generated.ShouldContain("global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication");
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Client.Generator.Tests --filter "RegisterNorseOutcomeSurrogates_uses_a_blocking_guard_not_a_flag_first_race"`
Expected: FAIL

- [ ] **Step 3: Change the emitted guard shape**

In `Midgard/gen/Infrastructure.Web.Client.Generator/ClientRegistrationEmitter.cs`, replace this block inside the `Emit` method's raw string literal:

```csharp
			public static class NorseGrpcClientRegistration
			{
				static int _surrogatesRegistered;

				/// <summary>Registers the Outcome&lt;T&gt; passthrough surrogates, idempotent per type.</summary>
				public static void RegisterNorseOutcomeSurrogates()
				{
					if (global::System.Threading.Interlocked.Exchange(ref _surrogatesRegistered, 1) == 1)
						return;
					var model = global::ProtoBuf.Meta.RuntimeTypeModel.Default;
					global::Norse.Infrastructure.Web.Grpc.IdentifierSerializers.Register(model);
			{{SurrogateGuards(payloads)}}
				}
```

with:

```csharp
			public static class NorseGrpcClientRegistration
			{
				// Blocking, not flag-first: a concurrent caller waits for registration to finish instead of
				// observing a "claimed" flag and proceeding against a half-built RuntimeTypeModel.Default.
				static readonly global::System.Lazy<bool> _surrogatesRegistered = new(
					RegisterNorseOutcomeSurrogatesCore, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

				/// <summary>Registers the Outcome&lt;T&gt; passthrough surrogates, idempotent per type and safe under concurrent first call.</summary>
				public static void RegisterNorseOutcomeSurrogates() => _ = _surrogatesRegistered.Value;

				static bool RegisterNorseOutcomeSurrogatesCore()
				{
					var model = global::ProtoBuf.Meta.RuntimeTypeModel.Default;
					global::Norse.Infrastructure.Web.Grpc.IdentifierSerializers.Register(model);
			{{SurrogateGuards(payloads)}}
					return true;
				}
```

Leave `AddNorseGrpcClients` and the `SurrogateGuards`/`AddClients` helpers untouched.

- [ ] **Step 4: Run the new test to verify it passes**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Client.Generator.Tests --filter "RegisterNorseOutcomeSurrogates_uses_a_blocking_guard_not_a_flag_first_race"`
Expected: PASS

- [ ] **Step 5: Run the full generator test project to confirm no regressions**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Client.Generator.Tests`
Expected: all tests pass, including `Emits_one_guarded_SetSurrogate_per_distinct_payload_including_Unit`, `RegisterNorseOutcomeSurrogates_is_called_first_inside_AddNorseGrpcClients`, `Registers_the_identifier_serializers_before_the_Outcome_surrogates`, and `Emitted_source_compiles_cleanly_against_real_protobuf_net_grpc_and_client_references`.

- [ ] **Step 6: Commit**

```bash
git add Midgard/gen/Infrastructure.Web.Client.Generator/ClientRegistrationEmitter.cs Midgard/tests/Infrastructure.Web.Client.Generator.Tests/GrpcClientRegistrationGeneratorTests.cs
git commit -m "fix: emit a blocking surrogate-registration guard from the client registration generator"
```

---

### Task 4: Close the filing and verify the whole repo

**Files:**
- Modify: `Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md`

**Interfaces:**
- Consumes: Tasks 1–3 complete and committed.
- Produces: nothing new — documentation closure and a full-repo verification pass.

- [ ] **Step 1: Append a landed note to the filing**

Add this section to the end of `Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md`:

```markdown
## Landed (2026-08-06)

All four call sites now use `System.Lazy<bool>` with `LazyThreadSafetyMode.ExecutionAndPublication` in
place of the flag-first guard: `Norse.Infrastructure.Web.Grpc.IdentifierSerializers.Register` and its
sibling `ResultSerializers.Register` (found unpatched during Task 1's review — same defect shape, same
file family, not in the original filing) directly, each covered by a real concurrency regression test
(`Register_does_not_return_until_registration_is_complete_under_concurrent_first_touch` in
`IdentifierSerializersTests.cs` and `ResultSerializerTests.cs` respectively), and both generator emitters
(`ServerRegistrationEmitter`/`ClientRegistrationEmitter`), covered by an emitted-shape assertion in each
generator's test suite. Yggdrasil's `WireModelWarmup.cs` interim mitigation stays in place until
Yggdrasil's own `Directory.Packages.props` picks up the Midgard release carrying this fix — remove it
then, per this filing's original "until" clause.
```

- [ ] **Step 2: Run the full Midgard test suite**

Run: `dotnet test Midgard.slnx` (or `dotnet test` from the `Midgard` directory, matching however the repo normally invokes the full suite)
Expected: all tests pass, zero failures, zero new warnings-as-errors.

- [ ] **Step 3: Stage everything and stop**

```bash
git add -A
git status
```

Per Bifröst/Midgard process law: no automatic commits beyond the per-task commits already made in Tasks 1–3, no push, no PR — stage the filing-doc change, show the diff, and stop for the human to review and push.
