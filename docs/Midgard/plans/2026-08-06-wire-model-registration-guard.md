# Wire Model Registration Guard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `../specs/2026-08-06-wire-model-registration-guard-design.md` — one shared, blocking, keyed `EnsureRegistered` primitive that every `RuntimeTypeModel` registration site goes through, an analyzer (NORSE080) that makes any other path a compile error, and a retrofit of all five known instances of this defect onto the primitive so day one of enforcement finds nothing left to flag.

**Architecture:** A new extension member `RuntimeTypeModel.EnsureRegistered(Type key, Action register)` in `Midgard/src/Infrastructure.Web.Grpc/WireModelRegistrationGuard.cs`, backed by `ConditionalWeakTable<RuntimeTypeModel, ConcurrentDictionary<Type, Lazy<bool>>>`. A new analyzer package `Midgard/gen/Infrastructure.Web.Grpc.Analyzers`, claiming NORSE080. Five retrofits (two hand-written classes, two generator emitters, one Yggdrasil test fixture) switch from their individual hand-rolled `Lazy<bool>` guards to calling the shared primitive. **Superseded 2026-08-06 (twice):** this paragraph originally described the analyzer as packed standalone (mirroring Svartálfheim's `Architecture.Analyzers`) and delivered via a Ginnungagap scatter entry — that shipped, then was corrected: the analyzer is instead bundled into `Norse.Infrastructure.Web.Grpc`'s own package (mirroring Svartálfheim's `Primitives.Analyzers`), reached in Bifröst dev-mode via `NorseGeneratorRef` and in CI/standalone mode via a direct `NorseRef`/`PackageReference` to `Infrastructure.Web.Grpc` on each of the four Yggdrasil projects that can touch the guarded surface — no Ginnungagap entry at all. See the spec's "Delivery, corrected 2026-08-06" section for the reasoning (NORSE070 already confines wire-format code to Midgard/Yggdrasil, so platform-wide scatter was dead weight everywhere else) and Task 6/Task 7 below for the as-shipped mechanism.

## Global Constraints

- **House style is `../../house-rules.md` in full** — read before writing any code in this plan; it governs every line below. The points that most affect this plan specifically:
  - **C# 14 extension blocks, not old-style static extension methods**, for `EnsureRegistered` (house-rules.md "Extension members").
  - **Target-typed `new()`** for construction (e.g. `new ConcurrentDictionary<Type, Lazy<bool>>()`, not a collection expression — an empty dictionary isn't "collection materialization," it's construction).
  - **Generated/emitted code fully-qualifies and calls extension methods in static-invocation form** (`Norse.Infrastructure.Web.Grpc.WireModelRegistrationGuard.EnsureRegistered(model, key, action)`), never bare `model.EnsureRegistered(...)` — generated files carry no `using` directives, and the "always extension style" rule binds hand-written source, not emitted text (house-rules.md "Usings and namespaces" carve-out).
  - **`sealed`/`abstract`/`static` on every class**; XML docs mandatory on every public src member; Shouldly + xUnit v3, test method names sentence-shaped with underscores.
  - **Analyzer/generator projects carry no analyzer-release ledger** (`#pragma warning disable RS2008`), matching every existing Norse analyzer/generator (see `Architecture.Analyzers/Diagnostics.cs`).
- **NORSE080 is a new block.** NORSE070-079 is explicitly claimed for realm-dependency law and fully used (070/071/072/073/079) — do not add to that package or that number range.
- **The analyzer is not realm-scoped.** Unlike `WireFormatAnalyzer`, it applies everywhere, including test projects — the defect this closes was found live in a Yggdrasil test fixture.
- **Every existing test in every touched file keeps passing unmodified** except where a task explicitly changes or adds one.
- **`dotnet` at `/home/buvy/.dotnet/dotnet`**, not bare `dotnet`, in this environment. Run one test project per `dotnet test` invocation — multiple projects in one invocation fail with handshake errors here, unrelated to any change in this plan.

---

### Task 1: The primitive — `WireModelRegistrationGuard`

**Files:**
- Create: `Midgard/src/Infrastructure.Web.Grpc/WireModelRegistrationGuard.cs`
- Test: `Midgard/tests/Infrastructure.Web.Grpc.Tests/WireModelRegistrationGuardTests.cs` (new file)

**Interfaces:**
- Consumes: nothing new.
- Produces: `RuntimeTypeModel.EnsureRegistered(Type key, Action register)` — an extension member, public, callable from any project referencing `Infrastructure.Web.Grpc`. Every later task in this plan calls this exact signature.

- [ ] **Step 1: Write the failing tests**

Create `Midgard/tests/Infrastructure.Web.Grpc.Tests/WireModelRegistrationGuardTests.cs`:

```csharp
using System.Threading;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc.Tests;

public sealed class WireModelRegistrationGuardTests
{
	[Fact]
	void Runs_the_register_action_exactly_once_for_repeated_calls_with_the_same_key()
	{
		var model = RuntimeTypeModel.Create();
		var runCount = 0;

		model.EnsureRegistered(typeof(WireModelRegistrationGuardTests), () => Interlocked.Increment(ref runCount));
		model.EnsureRegistered(typeof(WireModelRegistrationGuardTests), () => Interlocked.Increment(ref runCount));
		model.EnsureRegistered(typeof(WireModelRegistrationGuardTests), () => Interlocked.Increment(ref runCount));

		runCount.ShouldBe(1);
	}

	[Fact]
	void Treats_different_keys_on_the_same_model_as_independent()
	{
		var model = RuntimeTypeModel.Create();
		var firstRuns = 0;
		var secondRuns = 0;

		model.EnsureRegistered(typeof(string), () => Interlocked.Increment(ref firstRuns));
		model.EnsureRegistered(typeof(int), () => Interlocked.Increment(ref secondRuns));

		firstRuns.ShouldBe(1);
		secondRuns.ShouldBe(1);
	}

	[Fact]
	void Treats_the_same_key_on_different_models_as_independent()
	{
		var firstModel = RuntimeTypeModel.Create();
		var secondModel = RuntimeTypeModel.Create();
		var runCount = 0;

		firstModel.EnsureRegistered(typeof(WireModelRegistrationGuardTests), () => Interlocked.Increment(ref runCount));
		secondModel.EnsureRegistered(typeof(WireModelRegistrationGuardTests), () => Interlocked.Increment(ref runCount));

		runCount.ShouldBe(2);
	}

	[Fact]
	void Throws_on_a_null_model() =>
		Should.Throw<ArgumentNullException>(() => WireModelRegistrationGuardExtensions.EnsureRegistered(null!, typeof(string), () => { }));

	[Fact]
	async Task Every_concurrent_first_touch_caller_blocks_until_registration_completes()
	{
		// Regression coverage for the general primitive, generalizing the site-specific concurrency
		// tests already shipped for IdentifierSerializers/ResultSerializers
		// (../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md and its follow-up,
		// ../../../Glitnir/docs/Midgard/specs/2026-08-06-wire-model-registration-guard-design.md).
		const int ModelCount = 500;
		const int CallersPerModel = 8;

		await Task.WhenAll(Enumerable.Range(0, ModelCount).Select(async _ =>
		{
			var model = RuntimeTypeModel.Create();
			var registeredFlag = 0;
			using Barrier barrier = new(CallersPerModel);

			await Task.WhenAll(Enumerable.Range(0, CallersPerModel).Select(_ => Task.Run(() =>
			{
				barrier.SignalAndWait();
				model.EnsureRegistered(typeof(WireModelRegistrationGuardTests), () => Volatile.Write(ref registeredFlag, 1));
				Volatile.Read(ref registeredFlag).ShouldBe(1);
			})));
		}));
	}

	[Fact]
	void A_throwing_register_action_surfaces_the_same_exception_to_every_caller_not_just_the_first()
	{
		var model = RuntimeTypeModel.Create();

		var firstException = Should.Throw<InvalidOperationException>(() =>
			model.EnsureRegistered(typeof(WireModelRegistrationGuardTests), () => throw new InvalidOperationException("registration failed")));
		var secondException = Should.Throw<InvalidOperationException>(() =>
			model.EnsureRegistered(typeof(WireModelRegistrationGuardTests), () => throw new InvalidOperationException("a different message -- never reached")));

		secondException.ShouldBeSameAs(firstException);
	}
}
```

`WireModelRegistrationGuardExtensions` in the null-model test is the compiler-generated backing type name for a C# 14 `extension(RuntimeTypeModel model) { ... }` block declared inside a class named `WireModelRegistrationGuard` — confirm the actual generated static-method name by building Step 3 first if this specific call doesn't compile, and adjust the test to whatever static form the compiler actually emits (inspect via `dotnet build -v diag` or by checking the type's public API in a scratch `csc`-level probe); the intent — proving a null model throws `ArgumentNullException` via *some* directly-callable static form — is what matters, not this exact identifier.

- [ ] **Step 2: Run the tests to confirm they fail (compile error is fine — the type doesn't exist yet)**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Grpc.Tests --filter "WireModelRegistrationGuardTests"`
Expected: FAIL (compilation error — `WireModelRegistrationGuard` doesn't exist yet).

- [ ] **Step 3: Implement the primitive**

Create `Midgard/src/Infrastructure.Web.Grpc/WireModelRegistrationGuard.cs`:

```csharp
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
/// Blocking, keyed once-registration against a shared <see cref="RuntimeTypeModel"/> — the guard every
/// wire-model registration site must go through instead of a hand-rolled check-then-act or flag-first
/// guard, both of which let a concurrent caller observe a half-built model
/// (<c>../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md</c> and its follow-up,
/// <c>../../../Glitnir/docs/Midgard/specs/2026-08-06-wire-model-registration-guard-design.md</c>).
/// </summary>
public static class WireModelRegistrationGuard
{
	static readonly ConditionalWeakTable<RuntimeTypeModel, ConcurrentDictionary<Type, Lazy<bool>>> _guards = [];

	extension(RuntimeTypeModel model)
	{
		/// <summary>
		/// Runs <paramref name="register"/> exactly once for the (<paramref name="model"/>,
		/// <paramref name="key"/>) pair, blocking any concurrent caller until it completes rather than
		/// letting it observe a half-registered model. <paramref name="key"/> identifies what's being
		/// registered — the registrant's own type for a whole-model bootstrap
		/// (<c>typeof(IdentifierSerializers)</c>), or the payload type for a single surrogate
		/// (<c>typeof(Outcome&lt;ParityReport&gt;)</c>). A throwing <paramref name="register"/> has its
		/// exception cached and rethrown to every subsequent caller for that pair, never silently
		/// swallowed.
		/// </summary>
		/// <param name="key">Identifies the registration; independent keys on the same model never block each other.</param>
		/// <param name="register">Runs exactly once, the first time this (model, key) pair is touched.</param>
		public void EnsureRegistered(Type key, Action register)
		{
			ArgumentNullException.ThrowIfNull(model);
			ArgumentNullException.ThrowIfNull(key);
			ArgumentNullException.ThrowIfNull(register);
			var perModel = _guards.GetValue(model, static _ => new ConcurrentDictionary<Type, Lazy<bool>>());
			_ = perModel.GetOrAdd(key, _ => new Lazy<bool>(() =>
			{
				register();
				return true;
			}, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
		}
	}
}
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Grpc.Tests --filter "WireModelRegistrationGuardTests"`
Expected: PASS, all 7 tests. If the null-model test's exact static-call syntax didn't compile in Step 1, fix it now that the real generated name is known, and re-run.

- [ ] **Step 5: Run the full `Infrastructure.Web.Grpc.Tests` project**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Grpc.Tests`
Expected: all tests pass — the new ones plus every pre-existing test (`IdentifierSerializersTests`, `ResultSerializerTests`, etc., untouched by this task).

- [ ] **Step 6: Commit**

```bash
git add Midgard/src/Infrastructure.Web.Grpc/WireModelRegistrationGuard.cs Midgard/tests/Infrastructure.Web.Grpc.Tests/WireModelRegistrationGuardTests.cs
git commit -m "feat: add WireModelRegistrationGuard, a shared blocking-keyed-once primitive for RuntimeTypeModel registration"
```

---

### Task 2: Retrofit `IdentifierSerializers.Register`

**Files:**
- Modify: `Midgard/src/Infrastructure.Web.Grpc/IdentifierSerializers.cs`
- Test: `Midgard/tests/Infrastructure.Web.Grpc.Tests/IdentifierSerializersTests.cs`

**Interfaces:**
- Consumes: `RuntimeTypeModel.EnsureRegistered(Type key, Action register)` from Task 1.
- Produces: `IdentifierSerializers.Register(RuntimeTypeModel model)` — same public signature, same behavior, now delegating to the shared primitive instead of its own `ConditionalWeakTable<RuntimeTypeModel, Lazy<bool>>`.

- [ ] **Step 1: Replace the body**

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
	/// <summary>
	/// Registers the wire law on <paramref name="model"/>. Idempotent and safe under concurrent first
	/// call — see <see cref="WireModelRegistrationGuard.EnsureRegistered"/>. Must run before contract
	/// types enter the model — the sweep only sees types added after registration.
	/// </summary>
	public static void Register(RuntimeTypeModel model) =>
		model.EnsureRegistered(typeof(IdentifierSerializers), () =>
		{
			model.AfterApplyDefaultBehaviour += ApplyWireLaw;
			model.Add(typeof(SequentialGuid), applyDefaultBehaviour: false).SerializerType =
				typeof(SequentialGuidSerializer);
			model.Add(typeof(DeterministicGuid), applyDefaultBehaviour: false).SerializerType =
				typeof(DeterministicGuidSerializer);
		});

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

- [ ] **Step 2: Remove the now-superseded concurrency test**

`IdentifierSerializersTests.cs` carries `Register_does_not_return_until_registration_is_complete_under_concurrent_first_touch` (added by the prior plan) — this proved the OLD hand-rolled `Lazy<bool>` guard; Task 1's `WireModelRegistrationGuardTests.Every_concurrent_first_touch_caller_blocks_until_registration_completes` now proves the mechanism generically. Delete this test method from `IdentifierSerializersTests.cs` (and its now-unused `using System.Threading;`/`using System.Linq;`/`using System.Threading.Tasks;`/`Barrier` usings if nothing else in the file needs them — check before removing each one). Leave every other test in the file untouched.

- [ ] **Step 3: Run the full `Infrastructure.Web.Grpc.Tests` project**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Grpc.Tests`
Expected: all tests pass — every remaining pre-existing `IdentifierSerializersTests` test (idempotency, wire-format, schema), Task 1's tests, and everything else in the project.

- [ ] **Step 4: Commit**

```bash
git add Midgard/src/Infrastructure.Web.Grpc/IdentifierSerializers.cs Midgard/tests/Infrastructure.Web.Grpc.Tests/IdentifierSerializersTests.cs
git commit -m "refactor: retrofit IdentifierSerializers.Register onto the shared WireModelRegistrationGuard"
```

---

### Task 3: Retrofit `ResultSerializers.Register`

**Files:**
- Modify: `Midgard/src/Infrastructure.Web.Grpc/ResultSerializers.cs`
- Test: `Midgard/tests/Infrastructure.Web.Grpc.Tests/ResultSerializerTests.cs`

**Interfaces:**
- Consumes: `RuntimeTypeModel.EnsureRegistered(Type key, Action register)` from Task 1.
- Produces: `ResultSerializers.Register(RuntimeTypeModel model)` — same public signature, same behavior, now delegating to the shared primitive.

- [ ] **Step 1: Replace the field and method**

In `Midgard/src/Infrastructure.Web.Grpc/ResultSerializers.cs`, replace:

```csharp
	static readonly ConditionalWeakTable<RuntimeTypeModel, Lazy<bool>> _registered = [];

	/// <summary>
	/// ... (existing doc comment) ...
	/// </summary>
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

with:

```csharp
	/// <summary>
	/// ... (existing doc comment, unchanged) ...
	/// </summary>
	public static void Register(RuntimeTypeModel model) =>
		model.EnsureRegistered(typeof(ResultSerializers), () =>
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
		});
```

Remove the now-unused `using System.Runtime.CompilerServices;` (for `ConditionalWeakTable`) and `using System.Threading;` (for `LazyThreadSafetyMode`) if nothing else in the file needs them — check before removing each one. Leave `RegisterScalar<T>`, `RegisterEnumResults`, both `UnconditionalSuppressMessage` attributes, `IllegalWriteMessage`, and the class doc comment untouched.

- [ ] **Step 2: Remove the now-superseded concurrency test**

Delete `Register_does_not_return_until_registration_is_complete_under_concurrent_first_touch` from `ResultSerializerTests.cs` (superseded by Task 1's generic test, same reasoning as Task 2 Step 2). Leave every other test untouched.

- [ ] **Step 3: Run the full `Infrastructure.Web.Grpc.Tests` project**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Grpc.Tests`
Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add Midgard/src/Infrastructure.Web.Grpc/ResultSerializers.cs Midgard/tests/Infrastructure.Web.Grpc.Tests/ResultSerializerTests.cs
git commit -m "refactor: retrofit ResultSerializers.Register onto the shared WireModelRegistrationGuard"
```

---

### Task 4: Retrofit the server registration emitter

**Files:**
- Modify: `Midgard/gen/Infrastructure.Web.Server.Generator/ServerRegistrationEmitter.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Generator.Tests/GrpcServerRegistrationGeneratorTests.cs`

**Interfaces:**
- Consumes: `RuntimeTypeModel.EnsureRegistered(Type key, Action register)` from Task 1, called from emitted (generated) code — the emitted assembly always references `Infrastructure.Web.Grpc` already (it already calls `IdentifierSerializers.Register`).
- Produces: emitted `NorseGrpcServerRegistration.RegisterNorseOutcomeSurrogates()` — same public signature, same call site inside `MapNorseGrpcServices`, now delegating the whole body to the shared primitive instead of a hand-rolled `Lazy<bool>` field.

- [ ] **Step 1: Write the failing shape assertions**

In `Midgard/tests/Infrastructure.Web.Server.Generator.Tests/GrpcServerRegistrationGeneratorTests.cs`, replace the existing test:

```csharp
[Fact]
void RegisterNorseOutcomeSurrogates_uses_a_blocking_guard_not_a_flag_first_race()
{
	var generated = Generate(Contract);
	generated.ShouldNotContain("Interlocked.Exchange");
	generated.ShouldContain("global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication");
}
```

with:

```csharp
[Fact]
void RegisterNorseOutcomeSurrogates_delegates_to_the_shared_WireModelRegistrationGuard()
{
	var generated = Generate(Contract);
	generated.ShouldNotContain("Interlocked.Exchange");
	generated.ShouldNotContain("LazyThreadSafetyMode");
	generated.ShouldContain("global::Norse.Infrastructure.Web.Grpc.WireModelRegistrationGuard.EnsureRegistered(");
}
```

(This test file's `RegisterNorseOutcomeSurrogates_uses_a_blocking_guard_not_a_flag_first_race` was added by the prior plan's final-review fix wave — this step replaces it outright rather than adding a second test, since the old assertion set describes a shape this task removes.)

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Generator.Tests --filter "RegisterNorseOutcomeSurrogates_delegates_to_the_shared_WireModelRegistrationGuard"`
Expected: FAIL — the emitter still emits the hand-rolled `Lazy<bool>` shape.

- [ ] **Step 3: Change the emitted shape**

In `Midgard/gen/Infrastructure.Web.Server.Generator/ServerRegistrationEmitter.cs`, replace this block inside the `Emit` method's raw string literal:

```csharp
			public static class NorseGrpcServerRegistration
			{
				// Blocking, not flag-first: a concurrent caller waits for registration to finish instead of
				// observing a "claimed" flag and proceeding against a half-built RuntimeTypeModel.Default
				// (../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md).
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

with:

```csharp
			public static class NorseGrpcServerRegistration
			{
				/// <summary>Registers the Outcome&lt;T&gt; passthrough surrogates, idempotent per type and safe under concurrent first call.</summary>
				public static void RegisterNorseOutcomeSurrogates() =>
					global::Norse.Infrastructure.Web.Grpc.WireModelRegistrationGuard.EnsureRegistered(
						global::ProtoBuf.Meta.RuntimeTypeModel.Default,
						typeof(NorseGrpcServerRegistration),
						() =>
						{
							var model = global::ProtoBuf.Meta.RuntimeTypeModel.Default;
							global::Norse.Infrastructure.Web.Grpc.IdentifierSerializers.Register(model);
			{{SurrogateGuards(payloads)}}
						});
```

Note the static-invocation form (`WireModelRegistrationGuard.EnsureRegistered(model, key, action)`, not `model.EnsureRegistered(...)`) per this plan's Global Constraints — emitted code carries no `using` directives, so it cannot use extension-method call syntax cleanly the way hand-written source must. Leave everything else in the file (`MapNorseGrpcServices`, `SurrogateGuards`, `MapServices`) untouched — the call site and signature of `RegisterNorseOutcomeSurrogates()` don't change, only its body.

- [ ] **Step 4: Run the new test to verify it passes**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Generator.Tests --filter "RegisterNorseOutcomeSurrogates_delegates_to_the_shared_WireModelRegistrationGuard"`
Expected: PASS

- [ ] **Step 5: Run the full generator test project**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Server.Generator.Tests`
Expected: all tests pass, including `Emitted_source_compiles_cleanly_against_real_ASP_NET_Core_and_protobuf_net_references` — this is the strongest signal the new raw-string shape is syntactically correct, since it actually compiles the generated output.

- [ ] **Step 6: Commit**

```bash
git add Midgard/gen/Infrastructure.Web.Server.Generator/ServerRegistrationEmitter.cs Midgard/tests/Infrastructure.Web.Server.Generator.Tests/GrpcServerRegistrationGeneratorTests.cs
git commit -m "refactor: emit a call to the shared WireModelRegistrationGuard from the server registration generator"
```

---

### Task 5: Retrofit the client registration emitter

**Files:**
- Modify: `Midgard/gen/Infrastructure.Web.Client.Generator/ClientRegistrationEmitter.cs`
- Test: `Midgard/tests/Infrastructure.Web.Client.Generator.Tests/GrpcClientRegistrationGeneratorTests.cs`

**Interfaces:**
- Consumes: `RuntimeTypeModel.EnsureRegistered(Type key, Action register)` from Task 1.
- Produces: emitted `NorseGrpcClientRegistration.RegisterNorseOutcomeSurrogates()` — mirror of Task 4, client side.

- [ ] **Step 1: Write the failing shape assertion**

In `Midgard/tests/Infrastructure.Web.Client.Generator.Tests/GrpcClientRegistrationGeneratorTests.cs`, replace the existing `RegisterNorseOutcomeSurrogates_uses_a_blocking_guard_not_a_flag_first_race` test with:

```csharp
[Fact]
void RegisterNorseOutcomeSurrogates_delegates_to_the_shared_WireModelRegistrationGuard()
{
	var generated = Generate(Contract);
	generated.ShouldNotContain("Interlocked.Exchange");
	generated.ShouldNotContain("LazyThreadSafetyMode");
	generated.ShouldContain("global::Norse.Infrastructure.Web.Grpc.WireModelRegistrationGuard.EnsureRegistered(");
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Client.Generator.Tests --filter "RegisterNorseOutcomeSurrogates_delegates_to_the_shared_WireModelRegistrationGuard"`
Expected: FAIL

- [ ] **Step 3: Change the emitted shape**

In `Midgard/gen/Infrastructure.Web.Client.Generator/ClientRegistrationEmitter.cs`, replace this block:

```csharp
			public static class NorseGrpcClientRegistration
			{
				// Blocking, not flag-first: a concurrent caller waits for registration to finish instead of
				// observing a "claimed" flag and proceeding against a half-built RuntimeTypeModel.Default
				// (../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md).
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

with:

```csharp
			public static class NorseGrpcClientRegistration
			{
				/// <summary>Registers the Outcome&lt;T&gt; passthrough surrogates, idempotent per type and safe under concurrent first call.</summary>
				public static void RegisterNorseOutcomeSurrogates() =>
					global::Norse.Infrastructure.Web.Grpc.WireModelRegistrationGuard.EnsureRegistered(
						global::ProtoBuf.Meta.RuntimeTypeModel.Default,
						typeof(NorseGrpcClientRegistration),
						() =>
						{
							var model = global::ProtoBuf.Meta.RuntimeTypeModel.Default;
							global::Norse.Infrastructure.Web.Grpc.IdentifierSerializers.Register(model);
			{{SurrogateGuards(payloads)}}
						});
```

Leave `AddNorseGrpcClients`, `SurrogateGuards`, `AddClients` untouched.

- [ ] **Step 4: Run the new test to verify it passes**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Client.Generator.Tests --filter "RegisterNorseOutcomeSurrogates_delegates_to_the_shared_WireModelRegistrationGuard"`
Expected: PASS

- [ ] **Step 5: Run the full generator test project**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Client.Generator.Tests`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add Midgard/gen/Infrastructure.Web.Client.Generator/ClientRegistrationEmitter.cs Midgard/tests/Infrastructure.Web.Client.Generator.Tests/GrpcClientRegistrationGeneratorTests.cs
git commit -m "refactor: emit a call to the shared WireModelRegistrationGuard from the client registration generator"
```

---

### Task 6: The analyzer — `Infrastructure.Web.Grpc.Analyzers` (NORSE080)

**Files:**
- Create: `Midgard/gen/Infrastructure.Web.Grpc.Analyzers/Infrastructure.Web.Grpc.Analyzers.csproj`
- Create: `Midgard/gen/Infrastructure.Web.Grpc.Analyzers/Diagnostics.cs`
- Create: `Midgard/gen/Infrastructure.Web.Grpc.Analyzers/WireModelGuardAnalyzer.cs`
- Create: `Midgard/tests/Infrastructure.Web.Grpc.Analyzers.Tests/Infrastructure.Web.Grpc.Analyzers.Tests.csproj`
- Create: `Midgard/tests/Infrastructure.Web.Grpc.Analyzers.Tests/AnalyzerTestHarness.cs`
- Create: `Midgard/tests/Infrastructure.Web.Grpc.Analyzers.Tests/WireModelGuardAnalyzerTests.cs`
- Modify: `Midgard/Midgard.slnx` — add both new projects to their existing `gen`/`tests` solution folders.

**Interfaces:**
- Consumes: nothing from Tasks 1-5 — this is a standalone analyzer package with no dependency on `Infrastructure.Web.Grpc` (analyzers run in the Roslyn compiler host process, not the compiled output's dependency graph — same reasoning `Architecture.Analyzers` follows).
- Produces: `NORSE080`, a `NotConfigurable` error firing on any direct `RuntimeTypeModel.Add`/`.Add<T>`/`.IsDefined` invocation outside a type named `Norse.Infrastructure.Web.Grpc.WireModelRegistrationGuard`.

- [ ] **Step 1: Create the analyzer project**

**Superseded 2026-08-06 — this whole note describes a design that shipped and was then corrected; do not follow it.** It argued for overriding `IsPackable` back to `true` (standalone packing, no-opt-out delivery to every realm via Ginnungagap) on the reasoning that bundling into `Infrastructure.Web.Grpc`'s own package would miss consumers who reference that package without also referencing `Infrastructure.Web.Server`/`.Client`. That reasoning had the scope backwards: NORSE070 already confines every consumer capable of touching `RuntimeTypeModel` to Midgard and Yggdrasil, and any such consumer necessarily already depends on `Infrastructure.Web.Grpc` (directly or transitively) for `WireModelRegistrationGuard`/`IdentifierSerializers`/`ResultSerializers` themselves — there is no realm that references `Infrastructure.Web.Grpc` without needing the guard. The as-shipped project is `IsPackable=false` (the `gen/`-wide default, unmodified — no override at all) and mirrors Svartálfheim's `Primitives.Analyzers` instead: a bare `<Description>`, bundled into `Infrastructure.Web.Grpc.csproj`'s own package via an `IncludeGeneratorInPackage` target. See the spec's "Delivery, corrected 2026-08-06" section.

Create `Midgard/gen/Infrastructure.Web.Grpc.Analyzers/Infrastructure.Web.Grpc.Analyzers.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Authors>Norse Architecture</Authors>
		<Description>Norse.Infrastructure.Web.Grpc.Analyzers: NORSE080 bans any direct RuntimeTypeModel.Add/.Add&lt;T&gt;/.IsDefined invocation outside Norse.Infrastructure.Web.Grpc.WireModelRegistrationGuard itself -- the check-then-act and flag-first shapes those methods invite are the defect class filed 2026-08-03 (Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md) and found live five times, including inside a hand-rolled test fixture, before this rule existed. Not realm-scoped: every consumer, test projects included, must go through the guard. Delivered to every realm by the Ginnungagap scatter; packed standalone (analyzers/dotnet/cs) for the same no-opt-out reason Architecture.Analyzers is -- attachment contingent on referencing a host package would let a consumer opt out by simply not taking that dependency.</Description>
		<!-- Deliberate override of gen/Directory.Build.props' repo-wide IsPackable=false -- see the note
		     above this project file in the plan/PR that added it. This analyzer needs no-opt-out reach,
		     which a bundled-into-a-host-package generator cannot provide. -->
		<IncludeBuildOutput>false</IncludeBuildOutput>
		<IsPackable>true</IsPackable>
		<MinVerTagPrefix>v</MinVerTagPrefix>
		<!-- NU5128 fires for any analyzer-only package (no lib/ folder) -- IncludeBuildOutput=false means
		     this package never has one, by design. -->
		<NoWarn>$(NoWarn);NU5128</NoWarn>
		<PackageLicenseFile>LICENSE</PackageLicenseFile>
	</PropertyGroup>
	<ItemGroup>
		<None Include="../../LICENSE" Pack="true" PackagePath="\" Condition="Exists('../../LICENSE')" />
		<None Include="bin/$(Configuration)/netstandard2.0/$(AssemblyName).dll" Pack="true" PackagePath="analyzers/dotnet/cs/" Visible="false" />
		<PackageReference Include="MinVer" Version="*">
			<PrivateAssets>all</PrivateAssets>
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
		</PackageReference>
	</ItemGroup>
</Project>
```

`TargetFramework=netstandard2.0`, `IsRoslynComponent=true`, `EnforceExtendedAnalyzerRules=true`, `IsAotCompatible=false`, and the `InternalsVisibleTo`/`Microsoft.CodeAnalysis.CSharp` reference all already come from `Midgard/gen/Directory.Build.props` — do not redeclare them here, only the properties this project genuinely needs to override or add (matching exactly what `Architecture.Analyzers.csproj` itself does *not* redeclare either, for the same inherited reasons in Svartálfheim's own `gen/Directory.Build.props`).

- [ ] **Step 2: Write `Diagnostics.cs`**

```csharp
using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Grpc.Analyzers;

#pragma warning disable RS2008 // No analyzer-release ledger, matching the platform's other generators/analyzers.

/// <summary>
/// NORSE080 — claimed 2026-08-06. A new block: NORSE070-079 is fully claimed for realm-dependency law
/// specifically (see Svartálfheim's Architecture.Analyzers), a different concern than this one.
/// NotConfigurable: the rule is not a severity preference. Spec:
/// ../../../Glitnir/docs/Midgard/specs/2026-08-06-wire-model-registration-guard-design.md.
/// </summary>
static class Diagnostics
{
	const string Category = "Norse.Infrastructure.Web.Grpc";

	public static readonly DiagnosticDescriptor WireModelMutatedOutsideGuard = new(
		"NORSE080", "RuntimeTypeModel mutated outside the registration guard",
		"'{0}' mutates a shared RuntimeTypeModel directly -- registration must go through WireModelRegistrationGuard.EnsureRegistered, the only call site proven safe under concurrent first touch", Category,
		DiagnosticSeverity.Error, isEnabledByDefault: true, customTags: WellKnownDiagnosticTags.NotConfigurable);
}
```

- [ ] **Step 3: Write `WireModelGuardAnalyzer.cs`**

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Norse.Infrastructure.Web.Grpc.Analyzers;

/// <summary>
/// NORSE080: bans any direct invocation of <c>RuntimeTypeModel.Add</c>/<c>.Add&lt;T&gt;</c>/<c>.IsDefined</c>
/// outside <c>Norse.Infrastructure.Web.Grpc.WireModelRegistrationGuard</c> itself — the check-then-act
/// and flag-first shapes those methods invite are exactly the defect class filed
/// 2026-08-03 (<c>../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md</c>) and found
/// live five times, including inside a hand-rolled test fixture, before this rule existed. Not
/// realm-scoped, unlike Svartálfheim's WireFormatAnalyzer: every consumer, test projects included, must
/// go through the guard, since the defect was found live in test code squarely inside the
/// wire-format-blessed zone.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WireModelGuardAnalyzer : DiagnosticAnalyzer
{
	const string RuntimeTypeModelMetadataName = "ProtoBuf.Meta.RuntimeTypeModel";
	const string GuardTypeMetadataName = "Norse.Infrastructure.Web.Grpc.WireModelRegistrationGuard";
	static readonly ImmutableHashSet<string> _bannedMembers = ["Add", "IsDefined"];

	static readonly ImmutableArray<DiagnosticDescriptor> _supportedDiagnostics =
		[Diagnostics.WireModelMutatedOutsideGuard];

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		_supportedDiagnostics;

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
		context.EnableConcurrentExecution();
		context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
	}

	static void AnalyzeInvocation(OperationAnalysisContext context)
	{
		var invocation = (IInvocationOperation)context.Operation;
		var method = invocation.TargetMethod;
		if (!_bannedMembers.Contains(method.Name))
			return;
		if (method.ContainingType?.ToDisplayString() != RuntimeTypeModelMetadataName)
			return;
		if (context.ContainingSymbol.ContainingType?.ToDisplayString() == GuardTypeMetadataName)
			return;
		context.ReportDiagnostic(Diagnostic.Create(
			Diagnostics.WireModelMutatedOutsideGuard, invocation.Syntax.GetLocation(),
			$"{method.ContainingType!.ToDisplayString()}.{method.Name}"));
	}
}
```

- [ ] **Step 4: Create the test project and harness**

Create `Midgard/tests/Infrastructure.Web.Grpc.Analyzers.Tests/Infrastructure.Web.Grpc.Analyzers.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="*" PrivateAssets="all" />
		<ProjectReference Include="../../gen/Infrastructure.Web.Grpc.Analyzers/Infrastructure.Web.Grpc.Analyzers.csproj" />
		<ProjectReference Include="../../src/Infrastructure.Web.Grpc/Infrastructure.Web.Grpc.csproj" />
	</ItemGroup>
</Project>
```

(The second `ProjectReference`, to `Infrastructure.Web.Grpc` itself, is what makes `typeof(WireModelRegistrationGuard)` resolvable in Step 5's real-guard test — `Architecture.Analyzers.Tests` doesn't need an equivalent because none of its fixtures reference a real Norse type by `typeof`, only by assembly-name string.)

Create `Midgard/tests/Infrastructure.Web.Grpc.Analyzers.Tests/ReferenceAssemblies.cs`:

```csharp
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Grpc.Analyzers.Tests;

static class ReferenceAssemblies
{
	// Every fixture in this project exercises RuntimeTypeModel, unlike Architecture.Analyzers.Tests
	// (where only some fixtures need a specific banned assembly) — so protobuf-net's assembly belongs
	// in the shared baseline here, not threaded through a per-test extraReferences parameter.
	public static readonly MetadataReference[] Bcl =
	[
		MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Exception).Assembly.Location),
		MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
		MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
		MetadataReference.CreateFromFile(typeof(Dictionary<,>).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(ProtoBuf.Meta.RuntimeTypeModel).Assembly.Location),
	];
}
```

Create `Midgard/tests/Infrastructure.Web.Grpc.Analyzers.Tests/AnalyzerTestHarness.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Norse.Infrastructure.Web.Grpc.Analyzers.Tests;

static class AnalyzerTestHarness
{
	public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

	public static CSharpCompilation CreateCompilation(string assemblyName, MetadataReference[] extraReferences, params string[] sources) =>
		CSharpCompilation.Create(
			assemblyName,
			[.. sources.Select(s => CSharpSyntaxTree.ParseText(s, ParseOptions))],
			[.. ReferenceAssemblies.Bcl, .. extraReferences],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

	public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
		DiagnosticAnalyzer analyzer, string assemblyName, MetadataReference[] extraReferences, params string[] sources)
	{
		var compilation = CreateCompilation(assemblyName, extraReferences, sources);
		var compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
		compileErrors.ShouldBeEmpty($"Fixture failed to compile:\n{string.Join("\n", compileErrors)}");

		var withAnalyzers = compilation.WithAnalyzers(
			[analyzer],
			new CompilationWithAnalyzersOptions(
				options: new AnalyzerOptions([]), onAnalyzerException: (Action<Exception, DiagnosticAnalyzer, Diagnostic>?)null,
				concurrentAnalysis: true, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: true));
		return await withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
	}
}
```

This is a narrowed adaptation of `Svartalfheim/tests/Architecture.Analyzers.Tests/AnalyzerTestHarness.cs` — single-analyzer overload only (this project never needs the multi-analyzer `analyzers[]` overload `Architecture.Analyzers.Tests` carries for its NORSE079 suppression-interaction fixtures), and no `CreateNorseReference`/`parseOptions` overloads that have no caller here. If a later step in this task discovers a genuine need for one of the omitted overloads, add it then rather than speculatively now.

- [ ] **Step 5: Write the failing analyzer tests**

Create `Midgard/tests/Infrastructure.Web.Grpc.Analyzers.Tests/WireModelGuardAnalyzerTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Norse.Infrastructure.Web.Grpc;

namespace Norse.Infrastructure.Web.Grpc.Analyzers.Tests;

public sealed class WireModelGuardAnalyzerTests
{
	const string DirectAddOutsideGuard =
		"""
		using ProtoBuf.Meta;

		namespace App;

		static class Leak
		{
			public static void Register(RuntimeTypeModel model)
			{
				if (!model.IsDefined(typeof(string)))
					model.Add(typeof(string), applyDefaultBehaviour: false);
			}
		}
		""";

	[Fact]
	async Task Strikes_norse080_on_a_direct_Add_call_outside_the_guard()
	{
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App", [], DirectAddOutsideGuard);
		diagnostics.ShouldContain(d => d.Id == "NORSE080");
	}

	[Fact]
	async Task Strikes_norse080_on_a_direct_IsDefined_call_outside_the_guard()
	{
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App", [], DirectAddOutsideGuard);
		diagnostics.Count(d => d.Id == "NORSE080").ShouldBe(2); // one for IsDefined, one for Add
	}

	[Fact]
	async Task Stays_silent_inside_WireModelRegistrationGuard_itself()
	{
		const string GuardImplementation =
			"""
			using ProtoBuf.Meta;

			namespace Norse.Infrastructure.Web.Grpc;

			public static class WireModelRegistrationGuard
			{
				public static void Touch(RuntimeTypeModel model)
				{
					if (!model.IsDefined(typeof(string)))
						model.Add(typeof(string), applyDefaultBehaviour: false);
				}
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "Norse.Infrastructure.Web.Grpc", [], GuardImplementation);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task Stays_silent_for_code_that_only_calls_EnsureRegistered()
	{
		const string ThroughTheGuard =
			"""
			using System;
			using ProtoBuf.Meta;
			using Norse.Infrastructure.Web.Grpc;

			namespace App;

			static class Correct
			{
				public static void Register(RuntimeTypeModel model) =>
					WireModelRegistrationGuard.EnsureRegistered(model, typeof(Correct), () => { });
			}
			""";
		// This fixture references the real Infrastructure.Web.Grpc assembly (the actual
		// WireModelRegistrationGuard.EnsureRegistered signature) via the extra-references parameter --
		// see Step 4's harness note on threading MetadataReference.CreateFromFile(typeof(WireModelRegistrationGuard).Assembly.Location)
		// through for this test specifically.
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App",
			[MetadataReference.CreateFromFile(typeof(WireModelRegistrationGuard).Assembly.Location)],
			ThroughTheGuard);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task Strikes_regardless_of_assembly_name_not_realm_scoped()
	{
		// Unlike WireFormatAnalyzer, this rule is not realm-scoped -- the defect it closes was found
		// live in a Yggdrasil TEST project, squarely inside the wire-format-blessed zone. Prove it
		// strikes in a Tests-suffixed assembly name, which WireFormatAnalyzer's RealmIdentity would
		// treat as exempt.
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "Norse.Hosting.Web.Server.Tests", [], DirectAddOutsideGuard);
		diagnostics.ShouldContain(d => d.Id == "NORSE080");
	}
}
```

Note: this test file needs the `Infrastructure.Web.Grpc.Analyzers.Tests.csproj` (Step 4) to reference the real `Infrastructure.Web.Grpc` project too (for `typeof(WireModelRegistrationGuard)` in the last two tests) — add that `ProjectReference` in Step 4 alongside the analyzer project reference.

- [ ] **Step 6: Run the tests to confirm they fail (project doesn't build yet)**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Grpc.Analyzers.Tests`
Expected: FAIL to build/run — nothing exists yet.

- [ ] **Step 7: Run the tests to confirm they pass**

Run: `dotnet test Midgard/tests/Infrastructure.Web.Grpc.Analyzers.Tests`
Expected: PASS, all 5 tests.

- [ ] **Step 8: Add both projects to `Midgard.slnx`**

Add `gen/Infrastructure.Web.Grpc.Analyzers` to the existing `gen` solution folder and `tests/Infrastructure.Web.Grpc.Analyzers.Tests` to the existing `tests` solution folder, matching how the other `gen`/`tests` project pairs are already declared in `Midgard.slnx`.

- [ ] **Step 9: Commit**

```bash
git add Midgard/gen/Infrastructure.Web.Grpc.Analyzers Midgard/tests/Infrastructure.Web.Grpc.Analyzers.Tests Midgard/Midgard.slnx
git commit -m "feat: add Infrastructure.Web.Grpc.Analyzers, NORSE080 -- ban RuntimeTypeModel mutation outside the registration guard"
```

---

### Task 7: Wire NORSE080 into the Ginnungagap scatter

**Superseded 2026-08-06.** The Ginnungagap edit below was staged but never committed, and this task is fully superseded — NORSE080 ships bundled into `Infrastructure.Web.Grpc`'s own package instead, the same shape as Svartálfheim's `Primitives.Analyzers`. See the design doc's "Delivery, corrected 2026-08-06" paragraph (`../specs/2026-08-06-wire-model-registration-guard-design.md` §4) for the actual mechanism that shipped. Left below as a record of what was tried.

**Files:**
- Modify: `../../.github/config/Directory.Build.targets` (Ginnungagap — the peer repo at `../.github` relative to Bifröst, i.e. `/home/buvy/code/NorseArchitecture/.github` from this plan's perspective)

**Interfaces:**
- Consumes: the package name `Norse.Infrastructure.Web.Grpc.Analyzers` from Task 6. **Do not commit this scatter entry until the full three-step ordering below is satisfied** — writing the `<Choose>` block itself is safe (it's inert text until committed), but committing it before Midgard has actually released is not. **Correction, twice-over:** an earlier revision of this task claimed a `Version="*"`/CPM reference against a package with zero published versions "simply won't resolve anything" until a version exists, and a later revision repeated the same silent-no-op framing elsewhere in this task ("has no effect on any consuming repo until..."). Both are factually wrong. It is NU1101, a hard restore failure, not a silent no-op: every CPM-enabled consumer's restore breaks the moment this entry is committed, until a real release exists to resolve against. The correct three-step delivery ordering: (1) Midgard merges this branch and publishes a release of `Norse.Infrastructure.Web.Grpc.Analyzers`; (2) Yggdrasil's `Directory.Packages.props` `PackageVersion` entry (added ahead of time, alongside the final-review fix wave) resolves via `$(MidgardVersion)` — that property stays pinned to whatever Midgard tag was current when the entry was added and does **not** automatically track a new release, so someone must bump `MidgardVersion` itself (the CPM-bump bot handles this the same way it has all session, or a manual edit) to the tag that actually carries the analyzer before this step is genuinely satisfied; (3) only once both (1) and (2) are true does committing this Ginnungagap scatter entry become safe — before then, it NU1101s every CPM-enabled consumer's restore, Yggdrasil included.
- Produces: every repo importing Ginnungagap's scattered `Directory.Build.targets` picks up `Norse.Infrastructure.Web.Grpc.Analyzers` as a `PackageReference` the same way they already pick up `Norse.Architecture.Analyzers`.

- [ ] **Step 1: Read the existing block in full**

Read `/home/buvy/code/NorseArchitecture/.github/config/Directory.Build.targets` in full before editing — this is a shared, platform-wide file; understand the surrounding `<Choose>` block (the `UseProjectReferences`/`Architecture.Analyzers` self-reference guard, the `ManagePackageVersionsCentrally` branch, the `Otherwise` branch with its `Version="*"` comment) before adding a parallel one.

- [ ] **Step 2: Add a parallel `<Choose>` block for the new package**

Immediately following the existing `Architecture.Analyzers` `<Choose>` block (before the file's closing `</Project>`), add:

```xml
<!--
	NORSE080 -- RuntimeTypeModel mutation outside the registration guard. Delivered the same
	no-opt-out way as Norse.Architecture.Analyzers (NORSE070-079), for the same reason: attachment
	contingent on referencing a host package would let a consumer opt out by not taking the
	dependency. Spec: Glitnir/docs/Midgard/specs/2026-08-06-wire-model-registration-guard-design.md.
-->
<Choose>
	<When Condition="'$(UseProjectReferences)' == 'true' OR '$(MSBuildProjectName)' == 'Infrastructure.Web.Grpc.Analyzers'">
		<PropertyGroup />
	</When>
	<When Condition="'$(ManagePackageVersionsCentrally)' == 'true'">
		<ItemGroup>
			<PackageReference Include="Norse.Infrastructure.Web.Grpc.Analyzers" PrivateAssets="all" />
		</ItemGroup>
	</When>
	<Otherwise>
		<ItemGroup>
			<!-- "*" = latest released, never prerelease. -->
			<PackageReference Include="Norse.Infrastructure.Web.Grpc.Analyzers" Version="*" PrivateAssets="all" />
		</ItemGroup>
	</Otherwise>
</Choose>
```

- [ ] **Step 3: Verify the file is still well-formed XML**

Run: `xmllint --noout /home/buvy/code/NorseArchitecture/.github/config/Directory.Build.targets` (or equivalent — confirm the file still parses; this repo may not have a build/test step of its own to run against this specific file).

- [ ] **Step 4: Stage (do not commit — Ginnungagap is a peer repo outside this plan's normal git flow)**

```bash
cd /home/buvy/code/NorseArchitecture/.github
git add config/Directory.Build.targets
git status
```

Per this plan's Global Constraints and every repo's own process law: stage, show the diff, stop — the human commits and decides when this actually ships. **Committing this entry before Midgard has released is not a no-op** — it's a hard restore failure (NU1101) for every CPM-enabled consumer. See this task's Interfaces section above for the full three-step ordering, including the required `$(MidgardVersion)` bump.

---

### Task 8: Retrofit Yggdrasil's `SwoopHostFixture`

**Files:**
- Modify: `Yggdrasil/tests/Hosting.Web.Server.Tests/Swoop/TriProtocolSwoopTests.cs`

**Interfaces:**
- Consumes: `RuntimeTypeModel.EnsureRegistered(Type key, Action register)` from Task 1, via `ProjectReference` (Bifröst dev-mode `UseProjectReferences=true` — this retrofit can be verified locally today even though Yggdrasil's CI won't see the shared primitive until Midgard ships a release and Yggdrasil's CPM bump picks it up, the same bootstrap sequencing every prior fix in this effort went through).
- Produces: `SwoopHostFixture.InitializeAsync()` — same behavior, now calling the shared primitive instead of the local `Lazy<bool>` field added earlier today as an interim fix.

- [ ] **Step 1: Replace the local guard**

In `Yggdrasil/tests/Hosting.Web.Server.Tests/Swoop/TriProtocolSwoopTests.cs`, replace:

```csharp
	// Blocking, not check-then-act: this fixture is constructed once per test CLASS (IClassFixture),
	// and xUnit runs different classes' fixtures concurrently by default -- two SwoopHostFixture
	// instances racing IsDefined/Add against the shared RuntimeTypeModel.Default is the identical
	// TOCTOU shape Midgard's own guards were just hardened against
	// (../../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md), just hand-rolled
	// here instead of behind IdentifierSerializers/ResultSerializers. A second racing caller used to
	// observe IsDefined() still false and call Add() again, throwing "type already added" mid-registration
	// and leaving the shared model in whatever partial state protobuf-net's own Add() left it in --
	// exactly the failure class this platform has already chased once.
	static readonly Lazy<bool> _parityReportSurrogateRegistered = new(() =>
	{
		var model = RuntimeTypeModel.Default;
		if (!model.IsDefined(typeof(Outcome<ParityReport>)))
			model.Add(typeof(Outcome<ParityReport>), applyDefaultBehaviour: false).SetSurrogate(typeof(ParityReport));
		return true;
	}, LazyThreadSafetyMode.ExecutionAndPublication);
```

with:

```csharp
	// This fixture is constructed once per test CLASS (IClassFixture), and xUnit runs different
	// classes' fixtures concurrently by default -- two SwoopHostFixture instances racing registration
	// against the shared RuntimeTypeModel.Default is the identical TOCTOU shape filed in
	// ../../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md, found live here on
	// 2026-08-06 and now closed the same way as every other site: through the shared, tested guard
	// (../../../../Glitnir/docs/Midgard/specs/2026-08-06-wire-model-registration-guard-design.md)
	// rather than a fixture-local Lazy<bool>.
```

(A comment only — the field and its factory are gone entirely, replaced by a call at the point of use.)

Then in `InitializeAsync`, replace:

```csharp
		_ = _parityReportSurrogateRegistered.Value;
```

with:

```csharp
		model.EnsureRegistered(typeof(Outcome<ParityReport>), () =>
		{
			if (!model.IsDefined(typeof(Outcome<ParityReport>)))
				model.Add(typeof(Outcome<ParityReport>), applyDefaultBehaviour: false).SetSurrogate(typeof(ParityReport));
		});
```

Add `using Norse.Infrastructure.Web.Grpc;` to the file's usings if not already present (it already imports this namespace for `IdentifierSerializers`/`ResultSerializers` two lines above — confirm before adding a duplicate).

- [ ] **Step 2: Confirm the project reference reaches the new type**

Since Bifröst dev-mode uses `UseProjectReferences=true`, `Yggdrasil/tests/Hosting.Web.Server.Tests` reaching `Infrastructure.Web.Grpc`'s `WireModelRegistrationGuard` should already resolve via the existing project reference chain (it already references `Norse.Infrastructure.Web.Grpc.IdentifierSerializers`/`ResultSerializers` from the same assembly). If the build fails to resolve `WireModelRegistrationGuard`, check `Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj`'s references — it should need no new reference, since `Infrastructure.Web.Grpc` is already a transitive/direct dependency.

- [ ] **Step 3: Build and run the project multiple times**

Run: `dotnet build Yggdrasil/tests/Hosting.Web.Server.Tests -c Release`
Expected: builds clean.

Run five times: `dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests -c Release --no-build`
Expected: all 5 runs pass, same as the interim fix already proved earlier today — this step reconfirms the retrofit didn't regress that.

- [ ] **Step 4: Stage (Yggdrasil is on its own feature branch, `fix/swoop-host-fixture-surrogate-race`, opened earlier today — this commits onto that same branch, not a new one, per the one-fork-per-realm law)**

```bash
cd Yggdrasil
git branch --show-current
```

Confirm the branch is `fix/swoop-host-fixture-surrogate-race` before committing (it should already be checked out from earlier today's interim fix). If for any reason it isn't, check it out first — do not create a second branch for this.

```bash
git add tests/Hosting.Web.Server.Tests/Swoop/TriProtocolSwoopTests.cs
git commit -m "refactor: retrofit SwoopHostFixture onto the shared WireModelRegistrationGuard"
```

---

### Task 9: Close the loop — filing, full verification

**Files:**
- Modify: `Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md`

**Interfaces:**
- Consumes: Tasks 1-8 complete and committed (Task 7's Ginnungagap edit staged, not committed — that one is explicitly the human's call per Task 7 Step 4).
- Produces: nothing new — documentation closure and a full-repo verification pass across both touched repos.

- [ ] **Step 1: Append a second landed note to the filing**

Add this section to the end of `Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md`, after the existing "Landed (2026-08-06)" section:

```markdown
## Generalized (2026-08-06, same day)

The per-site fixes above closed four instances of this defect; a fifth, unrelated instance
(Yggdrasil's `SwoopHostFixture`, a hand-rolled test fixture never covered by any plan) turned up
the same day. Rather than continue fixing instances as they surface, the pattern itself is now
closed: a shared, tested primitive (`Norse.Infrastructure.Web.Grpc.WireModelRegistrationGuard`,
`EnsureRegistered`) is the only sanctioned path to `RuntimeTypeModel.Add`/`.IsDefined`, and a new
analyzer (NORSE080, `Norse.Infrastructure.Web.Grpc.Analyzers`) makes any other path a compile
error, platform-wide, not realm-scoped. All five known sites are retrofitted onto the primitive.
Full design: `../specs/2026-08-06-wire-model-registration-guard-design.md`.
```

- [ ] **Step 2: Run the full Midgard test suite, one project at a time**

Run `dotnet test <project>` for every project under `Midgard/tests/` individually (`find Midgard/tests -maxdepth 1 -type d -not -path Midgard/tests` lists them) — do not pass multiple project paths to one `dotnet test` invocation (handshake failures in this environment, unrelated to this plan).
Expected: every project passes, including the two new ones from Task 6.

- [ ] **Step 3: Run the full Yggdrasil `Hosting.Web.Server.Tests` project once more**

Run: `dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests -c Release`
Expected: passes.

- [ ] **Step 4: Stage the filing update and stop**

```bash
cd Glitnir
git add docs/Midgard/2026-08-03-surrogate-guard-race-filing.md
git status
```

Per every touched repo's process law: no automatic commits beyond the per-task commits already made in Tasks 1-6 and 8, no push, no PR — stage what's left, show the diff, and stop for the human to review and decide when Midgard ships the release that makes Task 7's Ginnungagap wiring and Task 8's Yggdrasil retrofit actually take effect in CI.
