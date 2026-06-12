# Svartalfheim Second Increment — Pathway Proof Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove the Svartalfheim pathway end to end — `Map`/`Bind`/`Match` combinators with FsCheck-proven laws, the `Parser` gateway over `ISpanParsable<T>`, and the evidence rigs (BenchmarkDotNet, AOT smoke).

**Architecture:** Combinators are instance methods on `Result<T>` implemented *as* union switches (dogfooding the compiler contract; defaulted values throw `SwitchExpressionException` uniformly). `Parser` routes `bool` to `BooleanParser` via a JIT-eliminated `typeof` branch and everything else through `T.TryParse(span, provider)` with the uniform Empty/Malformed choreography. Benchmarks measure storage A/B, dispatch elimination, and combinator tax; an AOT console proves the published native artifact. Spec: `../specs/2026-06-11-svartalfheim-pathway-proof-design.md`.

**Tech Stack:** .NET 11 preview 5 (SDK `11.0.100-` prerelease), C# `LangVersion=preview`, xUnit v3 on Microsoft.Testing.Platform (`xunit.v3.mtp-v2`), Shouldly, FsCheck 3.x, BenchmarkDotNet.

---

## Repo rules (read first)

- **NO automatic git commits.** Stage your edits with `git add`, show `git status`, and STOP — the human reviews in GitHub Desktop and commits. Every "Stage" step below means exactly that. Never run `git commit`, `git push`, `git reset`, or anything that creates or rewrites commits.
- **Working directory** for all commands: the Svartalfheim repo root (`Svartalfheim/` under the Bifrost workspace). Task 7 alone also touches the sibling court (`../Glitnir`); its staging happens there.
- **Indentation is tabs** in C# (the `.editorconfig` enforces it). All code blocks in this plan already use tabs — preserve them. XML project files also use tabs (match the existing csproj/props files).
- **Warnings are errors** (`TreatWarningsAsErrors`, `WarningLevel 9999`, `EnforceCodeStyleInBuild`). One warning fails the build. `GenerateDocumentationFile` is on: **every public member in `src/` requires an XML doc comment.** Tests/benchmarks/smoke suppress CS1591 via their `Directory.Build.props` instead.
- **Test filtering:** VSTest `--filter` does NOT work on Microsoft.Testing.Platform. Use: `dotnet test tests/Primitives.Tests -- --filter-class "*.ResultCombinatorTests"`.
- **Toolchain facts** (realm CLAUDE.md): the preview-5 runtime SHIPS `UnionAttribute`/`IUnion` — never add local polyfills (CS0436). ReSharper/Rider union squiggles are noise; the compiler is the truth.
- **Truncation knowledge lives in `Failure` alone** — parsers pass the trimmed `ReadOnlySpan<char>` to the span ctor overload; never pre-truncate.
- **US English** in all code, comments, and docs.

### Known risks (report, do not work around)

- **Target-typed switch arms over union conversions.** The combinators return `this switch { Success<T>(var value) => new Success<TResult>(…), Failure failure => failure }` and rely on each arm converting to the target `Result<TResult>` via the compiler-supplied union conversions. If this produces CS8506/CS0029, the preview compiler is not target-typing union conversions in switch arms — STOP and report; the fallback (wrapping each arm in an explicit `new Result<TResult>(…)`) is a design-visible change the court should see.
- **`Unsafe.As` reinterpret in the gateway's bool branch.** Inside `if (typeof(T) == typeof(bool))`, `T` is statically `bool` and `Unsafe.As<Result<bool>, Result<T>>` is an identity the type system cannot express — the BCL's own generic-specialization pattern. If the AOT smoke or any test shows corruption through this path, STOP and report; do not paper over it with boxing.
- **FsCheck 3 C# API drift.** The law tests below use the FsCheck 3 fluent API (`ArbMap.Default`, `Gen` LINQ, `ToArbitrary()`, `Prop.ForAll(…).QuickCheckThrowOnFailure()`). If member names have moved between `FsCheck` and `FsCheck.Fluent` namespaces in the restored version, mechanical adaptation (same generators, same five laws, same assertions) is sanctioned — anything beyond renames/moves is a STOP-and-report.

---

### Task 1: Combinators on `Result<T>` (`Map` / `Bind` / `Match`)

TDD. The combinators are union switches inside the union — the defaulted footgun throws `SwitchExpressionException` through them exactly as a hand-written switch does.

**Files:**
- Test: `tests/Primitives.Tests/ResultCombinatorTests.cs` (create)
- Modify: `src/Primitives/Result{T}.cs` (insert combinators between `TryGetValue(out Failure)` and `ToString`)

- [ ] **Step 1: Write the failing tests — `tests/Primitives.Tests/ResultCombinatorTests.cs`**

```csharp
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Norse.Primitives.Tests;

public sealed class ResultCombinatorTests
{
	static Failure MalformedBoolean(string input = "bogus") =>
		new(ParseFailure.Malformed, input, "Boolean");

	[Fact]
	void Should_transform_value_when_mapping_success()
	{
		Result<int> result = new Success<int>(21);
		var mapped = result.Map(x => x * 2);
		mapped.TryGetValue(out Success<int> success).ShouldBeTrue();
		success.Value.ShouldBe(42);
	}

	[Fact]
	void Should_change_type_when_mapping_success()
	{
		Result<int> result = new Success<int>(42);
		var mapped = result.Map(x => x.ToString(CultureInfo.InvariantCulture));
		mapped.TryGetValue(out Success<string> success).ShouldBeTrue();
		success.Value.ShouldBe("42");
	}

	[Fact]
	void Should_flow_failure_through_when_mapping_failure()
	{
		Result<int> result = MalformedBoolean();
		var invoked = false;
		var mapped = result.Map(x =>
		{
			invoked = true;
			return x;
		});
		invoked.ShouldBeFalse();
		mapped.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.ShouldBe(MalformedBoolean());
	}

	[Fact]
	void Should_throw_when_mapping_with_null_selector()
	{
		Result<int> result = new Success<int>(1);
		Should.Throw<ArgumentNullException>(() => result.Map<int>(null!));
	}

	[Fact]
	void Should_throw_when_mapping_defaulted_result() =>
		Should.Throw<SwitchExpressionException>(() => default(Result<int>).Map(x => x));

	[Fact]
	void Should_propagate_selector_exception_when_mapping()
	{
		Result<int> result = new Success<int>(1);
		Should.Throw<InvalidOperationException>(() => result.Map<int>(_ => throw new InvalidOperationException("boom")));
	}

	[Fact]
	void Should_chain_to_new_result_when_binding_success()
	{
		Result<int> result = new Success<int>(21);
		var bound = result.Bind<int>(x => new Success<int>(x * 2));
		bound.TryGetValue(out Success<int> success).ShouldBeTrue();
		success.Value.ShouldBe(42);
	}

	[Fact]
	void Should_chain_to_failure_when_binder_fails()
	{
		Result<int> result = new Success<int>(21);
		var bound = result.Bind<int>(_ => MalformedBoolean());
		bound.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.ShouldBe(MalformedBoolean());
	}

	[Fact]
	void Should_flow_failure_through_when_binding_failure()
	{
		Result<int> result = MalformedBoolean();
		var invoked = false;
		var bound = result.Bind<int>(x =>
		{
			invoked = true;
			return new Success<int>(x);
		});
		invoked.ShouldBeFalse();
		bound.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.ShouldBe(MalformedBoolean());
	}

	[Fact]
	void Should_throw_when_binding_with_null_binder()
	{
		Result<int> result = new Success<int>(1);
		Should.Throw<ArgumentNullException>(() => result.Bind<int>(null!));
	}

	[Fact]
	void Should_throw_when_binding_defaulted_result() =>
		Should.Throw<SwitchExpressionException>(() =>
			default(Result<int>).Bind<int>(x => new Success<int>(x)));

	[Fact]
	void Should_invoke_success_arm_when_matching_success()
	{
		Result<int> result = new Success<int>(42);
		var rendered = result.Match(value => $"ok:{value}", failure => $"fail:{failure.Reason}");
		rendered.ShouldBe("ok:42");
	}

	[Fact]
	void Should_invoke_failure_arm_when_matching_failure()
	{
		Result<int> result = MalformedBoolean();
		var rendered = result.Match(value => $"ok:{value}", failure => $"fail:{failure.Reason}");
		rendered.ShouldBe("fail:Malformed");
	}

	[Fact]
	void Should_throw_when_matching_with_null_success_arm()
	{
		Result<int> result = new Success<int>(1);
		Should.Throw<ArgumentNullException>(() => result.Match(null!, failure => failure.Reason.ToString()));
	}

	[Fact]
	void Should_throw_when_matching_with_null_failure_arm()
	{
		Result<int> result = new Success<int>(1);
		Should.Throw<ArgumentNullException>(() => result.Match(value => value.ToString(CultureInfo.InvariantCulture), null!));
	}

	[Fact]
	void Should_throw_when_matching_defaulted_result() =>
		Should.Throw<SwitchExpressionException>(() =>
			default(Result<int>).Match(value => value, _ => 0));

	[Fact]
	void Should_compose_pathway_when_chaining_combinators()
	{
		Result<int> result = new Success<int>(10);
		var rendered = result
			.Map(x => x + 11)
			.Bind<int>(x =>
			{
				if (x % 2 == 1)
					return new Success<int>(x * 2);
				return MalformedBoolean();
			})
			.Match(value => value.ToString(CultureInfo.InvariantCulture), failure => failure.Reason.ToString());
		rendered.ShouldBe("42");
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/Primitives.Tests`
Expected: compilation FAILS with CS1061 (`Result<int>` contains no definition for `Map`).

- [ ] **Step 3: Implement the combinators in `src/Primitives/Result{T}.cs`**

Insert between the closing brace of `TryGetValue(out Failure value)` and the `ToString` member (keep one blank line on each side):

```csharp
	/// <summary>Transforms the success value; a failure flows through untouched.</summary>
	/// <remarks>
	/// Combinators are composition ergonomics, not the hot path — row-volume loops
	/// switch over the cases directly. Nothing here allocates beyond the caller's
	/// own closures.
	/// </remarks>
	/// <typeparam name="TResult">The transformed value's type. Non-nullable by construction.</typeparam>
	/// <param name="selector">The success-case transform. Exceptions it throws propagate unhandled.</param>
	/// <returns>The transformed result.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="selector"/> is null.</exception>
	/// <exception cref="SwitchExpressionException">This value was defaulted rather than constructed.</exception>
	public Result<TResult> Map<TResult>(Func<T, TResult> selector) where TResult : notnull
	{
		ArgumentNullException.ThrowIfNull(selector);
		return this switch
		{
			Success<T>(var value) => new Success<TResult>(selector(value)),
			Failure failure => failure,
		};
	}

	/// <summary>Chains a dependent conversion; a failure flows through untouched.</summary>
	/// <remarks>
	/// Combinators are composition ergonomics, not the hot path — row-volume loops
	/// switch over the cases directly. Nothing here allocates beyond the caller's
	/// own closures.
	/// </remarks>
	/// <typeparam name="TResult">The chained result's value type. Non-nullable by construction.</typeparam>
	/// <param name="binder">The success-case continuation. Exceptions it throws propagate unhandled.</param>
	/// <returns>The chained result.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="binder"/> is null.</exception>
	/// <exception cref="SwitchExpressionException">This value was defaulted rather than constructed.</exception>
	public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> binder) where TResult : notnull
	{
		ArgumentNullException.ThrowIfNull(binder);
		return this switch
		{
			Success<T>(var value) => binder(value),
			Failure failure => failure,
		};
	}

	/// <summary>Consumes the result by handling both cases.</summary>
	/// <typeparam name="TResult">The handlers' common return type.</typeparam>
	/// <param name="success">The success-case handler. Exceptions it throws propagate unhandled.</param>
	/// <param name="failure">The failure-case handler. Exceptions it throws propagate unhandled.</param>
	/// <returns>Whichever handler ran.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="success"/> or <paramref name="failure"/> is null.</exception>
	/// <exception cref="SwitchExpressionException">This value was defaulted rather than constructed.</exception>
	public TResult Match<TResult>(Func<T, TResult> success, Func<Failure, TResult> failure)
	{
		ArgumentNullException.ThrowIfNull(success);
		ArgumentNullException.ThrowIfNull(failure);
		return this switch
		{
			Success<T>(var value) => success(value),
			Failure failureCase => failure(failureCase),
		};
	}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests`
Expected: PASS — all ResultCombinatorTests green plus the pre-existing suites, 0 warnings.

- [ ] **Step 5: Stage (no commit — human commits)**

```powershell
git add src/Primitives/ tests/Primitives.Tests/
git status
```

---

### Task 2: FsCheck law tests (functor + monad)

The increment's actual point: the five laws, property-tested across generated successes and valid failures. Resolve the spec §3.4 integration gate first.

**Files:**
- Modify: `tests/Primitives.Tests/Primitives.Tests.csproj` (add FsCheck)
- Test: `tests/Primitives.Tests/ResultLawTests.cs` (create)

- [ ] **Step 1: Probe the integration gate (spec §3.4)**

Run (non-mutating):

```powershell
dotnet package search FsCheck.Xunit.v3 --exact-match
dotnet package search FsCheck --exact-match
```

Record in the task report whether an `FsCheck.Xunit.v3` integration package exists and the latest stable `FsCheck` 3.x version. **Regardless of the probe's outcome, implement the laws in the portable style below** (plain `[Fact]` + `Prop.ForAll(…).QuickCheckThrowOnFailure()`): it runs identically with or without an attribute-integration package, which resolves the gate by construction. The probe result is recorded for the future, not acted on.

- [ ] **Step 2: Add FsCheck to `tests/Primitives.Tests/Primitives.Tests.csproj`**

Full new file content:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="FsCheck" Version="3.*" />
		<ProjectReference Include="..\..\src\Primitives\Primitives.csproj" />
	</ItemGroup>
</Project>
```

Run: `dotnet restore Svartalfheim.slnx`
Expected: restore succeeds; FsCheck 3.x in `obj/project.assets.json`.

- [ ] **Step 3: Write the law tests — `tests/Primitives.Tests/ResultLawTests.cs`**

These must FAIL only if a law is broken — they compile against the Task 1 surface, so there is no red phase for missing members; the red phase here is the generators (they construct both cases, never `default`).

```csharp
using FsCheck;
using FsCheck.Fluent;

namespace Norse.Primitives.Tests;

public sealed class ResultLawTests
{
	static Gen<Failure> FailureGen =>
		from reason in Gen.Elements(ParseFailure.Empty, ParseFailure.Malformed)
		from input in ArbMap.Default.GeneratorFor<NonNull<string>>()
		select new Failure(reason, input.Get, "Int32");

	static Gen<Result<int>> ResultGen =>
		Gen.OneOf(
			ArbMap.Default.GeneratorFor<int>().Select(value => (Result<int>)new Success<int>(value)),
			FailureGen.Select(failure => (Result<int>)failure));

	static Gen<Func<int, Result<int>>> BinderGen =>
		from addend in ArbMap.Default.GeneratorFor<int>()
		from threshold in ArbMap.Default.GeneratorFor<int>()
		from failure in FailureGen
		select (Func<int, Result<int>>)(x =>
		{
			if (x < threshold)
				return new Success<int>(unchecked(x + addend));
			return failure;
		});

	[Fact]
	void Should_preserve_result_when_mapped_with_identity() =>
		Prop.ForAll(ResultGen.ToArbitrary(), result => result.Map(x => x) == result)
			.QuickCheckThrowOnFailure();

	[Fact]
	void Should_compose_transforms_when_mapped_in_sequence() =>
		Prop.ForAll(ResultGen.ToArbitrary(), ArbMap.Default.ArbFor<int>(), ArbMap.Default.ArbFor<int>(), (result, a, b) =>
		{
			Func<int, int> f = x => unchecked(x + a);
			Func<int, int> g = x => unchecked(x * b);
			return result.Map(f).Map(g) == result.Map(x => g(f(x)));
		})
			.QuickCheckThrowOnFailure();

	[Fact]
	void Should_satisfy_left_identity_when_lifted_value_is_bound() =>
		Prop.ForAll(ArbMap.Default.ArbFor<int>(), BinderGen.ToArbitrary(), (value, binder) =>
		{
			Result<int> lifted = new Success<int>(value);
			return lifted.Bind(binder) == binder(value);
		})
			.QuickCheckThrowOnFailure();

	[Fact]
	void Should_satisfy_right_identity_when_bound_with_lift() =>
		Prop.ForAll(ResultGen.ToArbitrary(), result =>
			result.Bind(x => (Result<int>)new Success<int>(x)) == result)
			.QuickCheckThrowOnFailure();

	[Fact]
	void Should_satisfy_associativity_when_bound_in_sequence() =>
		Prop.ForAll(ResultGen.ToArbitrary(), BinderGen.ToArbitrary(), BinderGen.ToArbitrary(), (result, f, g) =>
			result.Bind(f).Bind(g) == result.Bind(x => f(x).Bind(g)))
			.QuickCheckThrowOnFailure();
}
```

- [ ] **Step 4: Run the law tests**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.ResultLawTests"`
Expected: PASS — five law tests green (each runs 100 generated cases). If the FsCheck API surface differs, see Known risks; mechanical renames only.

- [ ] **Step 5: Run the full test project**

Run: `dotnet test tests/Primitives.Tests`
Expected: PASS — every suite green, 0 warnings.

- [ ] **Step 6: Stage (no commit — human commits)**

```powershell
git add tests/Primitives.Tests/
git status
```

---

### Task 3: The `Parser` gateway

TDD. The bool route is proven by vocabulary `bool.TryParse` rejects ("yes", "on"); the generic route by `int`/`decimal`; the provider law by an invariant/de-DE pair.

**Files:**
- Test: `tests/Primitives.Tests/ParserTests.cs` (create)
- Create: `src/Primitives/Parser.cs`

- [ ] **Step 1: Write the failing tests — `tests/Primitives.Tests/ParserTests.cs`**

```csharp
using System.Globalization;

namespace Norse.Primitives.Tests;

public sealed class ParserTests
{
	const string AllWhitespace = " \t\r\n\f ";

	static readonly IFormatProvider Invariant = CultureInfo.InvariantCulture;

	[Theory]
	[InlineData("yes")]
	[InlineData("on")]
	[InlineData("1")]
	void Should_route_to_boolean_specialist_when_parsing_bool(string input)
	{
		var actual = Parser.ParseRequired<bool>(input, Invariant);
		actual.TryGetValue(out Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeTrue();
	}

	[Fact]
	void Should_route_to_boolean_specialist_when_parsing_optional_bool()
	{
		var actual = Parser.ParseOptional<bool>("no", Invariant);
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeFalse();
	}

	[Theory]
	[InlineData("42", 42)]
	[InlineData("  7  ", 7)]
	[InlineData("-13", -13)]
	void Should_parse_value_when_int_input_is_recognized(string input, int expected)
	{
		var actual = Parser.ParseRequired<int>(input, Invariant);
		actual.TryGetValue(out Success<int> success).ShouldBeTrue();
		success.Value.ShouldBe(expected);
	}

	[Fact]
	void Should_honor_declared_provider_when_parsing_decimal()
	{
		Parser.ParseRequired<decimal>("1.5", Invariant)
			.TryGetValue(out Success<decimal> invariantSuccess).ShouldBeTrue();
		invariantSuccess.Value.ShouldBe(1.5m);
		Parser.ParseRequired<decimal>("1,5", CultureInfo.GetCultureInfo("de-DE"))
			.TryGetValue(out Success<decimal> germanSuccess).ShouldBeTrue();
		germanSuccess.Value.ShouldBe(1.5m);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_fail_with_empty_reason_when_required_input_is_absent(string? input)
	{
		var actual = Parser.ParseRequired<int>(input, Invariant);
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
		Parser.ParseOptional<int>(input, Invariant).HasValue.ShouldBeFalse();

	[Theory]
	[InlineData("abc")]
	[InlineData("12.5")]
	[InlineData("fourty-two")]
	void Should_fail_with_malformed_reason_when_int_input_is_unrecognized(string input)
	{
		var actual = Parser.ParseRequired<int>(input, Invariant);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Input.ShouldBe(input.Trim());
		failure.ExpectedType.ShouldBe("Int32");
		failure.Format.ShouldBeNull();
		failure.Detail.ShouldBeNull();
	}

	[Fact]
	void Should_truncate_captured_input_when_malformed_input_is_oversized()
	{
		var oversized = new string('9', Failure.MaxInputLength + 44);
		var actual = Parser.ParseRequired<int>(oversized, Invariant);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Input.Length.ShouldBe(Failure.MaxInputLength);
	}

	[Fact]
	void Should_fail_with_malformed_reason_when_optional_input_is_unrecognized()
	{
		var actual = Parser.ParseOptional<int>("abc", Invariant);
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Should_throw_when_required_provider_is_null() =>
		Should.Throw<ArgumentNullException>(() => Parser.ParseRequired<int>("42", null!));

	[Fact]
	void Should_throw_when_optional_provider_is_null() =>
		Should.Throw<ArgumentNullException>(() => Parser.ParseOptional<int>("42", null!));
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/Primitives.Tests`
Expected: compilation FAILS with CS0103 (`Parser` not found).

- [ ] **Step 3: Implement `src/Primitives/Parser.cs`**

```csharp
using System.Runtime.CompilerServices;

namespace Norse.Primitives;

/// <summary>
/// Generic parse gateway over <see cref="ISpanParsable{TSelf}"/>: the bridge from the
/// span world into <see cref="Result{T}"/> with uniform failure semantics.
/// </summary>
/// <remarks>
/// <para>
/// Hot-path specialists are routed by <c>typeof</c> branches resolved at JIT/AOT compile
/// time — <see cref="bool"/> routes to <see cref="BooleanParser"/>, whose richer vocabulary
/// the bare <see cref="bool.TryParse(ReadOnlySpan{char}, out bool)"/> lacks; the provider is
/// deliberately not forwarded there (boolean text is culture-insensitive). Every other type
/// parses through its own <see cref="ISpanParsable{TSelf}"/> implementation. There is no
/// runtime registry: a type that cannot parse does not compile.
/// </para>
/// <para>
/// The provider is required. A call site parsing culture-sensitive text declares its culture
/// out loud (e.g. <see cref="System.Globalization.CultureInfo.InvariantCulture"/>) or it does
/// not compile — there is no defaulting overload.
/// </para>
/// </remarks>
public static class Parser
{
	/// <summary>
	/// Parses required scalar text. Empty or whitespace input is a
	/// <see cref="ParseFailure.Empty"/> failure; unrecognized input is
	/// <see cref="ParseFailure.Malformed"/>.
	/// </summary>
	/// <typeparam name="T">The target type. Non-nullable by construction.</typeparam>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <param name="provider">The declared culture for culture-sensitive types. Never null.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<T> ParseRequired<T>(ReadOnlySpan<char> input, IFormatProvider provider)
		where T : notnull, ISpanParsable<T>
	{
		ArgumentNullException.ThrowIfNull(provider);
		if (typeof(T) == typeof(bool))
		{
			// In this JIT-eliminated branch T is statically bool; the reinterpret is an
			// identity the type system cannot express (the BCL generic-specialization pattern).
			var routed = BooleanParser.ParseRequired(input);
			return Unsafe.As<Result<bool>, Result<T>>(ref routed);
		}
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, typeof(T).Name);
		return Parse<T>(trimmed, provider);
	}

	/// <summary>
	/// Parses optional scalar text. Empty or whitespace input is absent
	/// (<see langword="null"/>); unrecognized input is <see cref="ParseFailure.Malformed"/>.
	/// </summary>
	/// <typeparam name="T">The target type. Non-nullable by construction.</typeparam>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <param name="provider">The declared culture for culture-sensitive types. Never null.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
	public static Result<T>? ParseOptional<T>(ReadOnlySpan<char> input, IFormatProvider provider)
		where T : notnull, ISpanParsable<T>
	{
		ArgumentNullException.ThrowIfNull(provider);
		if (typeof(T) == typeof(bool))
		{
			var routed = BooleanParser.ParseOptional(input);
			return Unsafe.As<Result<bool>?, Result<T>?>(ref routed);
		}
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return Parse<T>(trimmed, provider);
	}

	static Result<T> Parse<T>(ReadOnlySpan<char> trimmed, IFormatProvider provider)
		where T : notnull, ISpanParsable<T>
	{
		if (T.TryParse(trimmed, provider, out var value))
			return new Success<T>(value);
		return new Failure(ParseFailure.Malformed, trimmed, typeof(T).Name);
	}
}
```

Note the failure construction passes the **span** — `Failure`'s span ctor owns the bounded capture (realm law: never pre-truncate in a parser).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests`
Expected: PASS — all ParserTests green plus every prior suite, 0 warnings.

- [ ] **Step 5: Stage (no commit — human commits)**

```powershell
git add src/Primitives/ tests/Primitives.Tests/
git status
```

---

### Task 4: Benchmarks rig

Three families: storage A/B (the boxed twin exists to be measured, never shipped), dispatch elimination, combinator tax. Compiles on every build; runs manually (Task 6).

**Files:**
- Create: `benchmarks/Directory.Build.props`
- Create: `benchmarks/Primitives.Benchmarks/Primitives.Benchmarks.csproj`
- Create: `benchmarks/Primitives.Benchmarks/Program.cs`
- Create: `benchmarks/Primitives.Benchmarks/BoxedResult{T}.cs`
- Create: `benchmarks/Primitives.Benchmarks/StorageBenchmarks.cs`
- Create: `benchmarks/Primitives.Benchmarks/DispatchBenchmarks.cs`
- Create: `benchmarks/Primitives.Benchmarks/CombinatorBenchmarks.cs`
- Modify: `Svartalfheim.slnx`

- [ ] **Step 1: Create `benchmarks/Directory.Build.props`**

```xml
<Project>
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
	<PropertyGroup>
		<!--
			BenchmarkDotNet shapes: benchmark classes are public and unsealed (BDN derives
			from them) and benchmark methods are instance methods (CA1822). XML-doc
			obligation stays a src/ law (CS1591).
		-->
		<IsPackable>false</IsPackable>
		<NoWarn>$(NoWarn);CA1822;CS1591</NoWarn>
		<OutputType>Exe</OutputType>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="BenchmarkDotNet" Version="0.*" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Create `benchmarks/Primitives.Benchmarks/Primitives.Benchmarks.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="..\..\src\Primitives\Primitives.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 3: Create `benchmarks/Primitives.Benchmarks/Program.cs`**

```csharp
using BenchmarkDotNet.Running;
using Norse.Primitives.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(StorageBenchmarks).Assembly).Run(args);
```

- [ ] **Step 4: Create `benchmarks/Primitives.Benchmarks/BoxedResult{T}.cs`**

```csharp
namespace Norse.Primitives.Benchmarks;

/// <summary>
/// The road not taken: a <see cref="Result{T}"/> twin that boxes its case into a single
/// object field. Exists only as the storage A/B comparator (pathway-proof spec §4.1) —
/// it is never shipped and never grows.
/// </summary>
public readonly record struct BoxedResult<T> where T : notnull
{
	readonly object? _value;

	public BoxedResult(Success<T> value) => _value = value;

	public BoxedResult(Failure value) => _value = value;

	public bool TryGetValue(out Success<T> value)
	{
		if (_value is Success<T> success)
		{
			value = success;
			return true;
		}
		value = default;
		return false;
	}

	public bool TryGetValue(out Failure value)
	{
		if (_value is Failure failure)
		{
			value = failure;
			return true;
		}
		value = default;
		return false;
	}
}
```

- [ ] **Step 5: Create `benchmarks/Primitives.Benchmarks/StorageBenchmarks.cs`**

```csharp
using BenchmarkDotNet.Attributes;

namespace Norse.Primitives.Benchmarks;

[MemoryDiagnoser]
public class StorageBenchmarks
{
	static readonly Failure MalformedBoolean = new(ParseFailure.Malformed, "bogus", "Boolean");

	readonly bool _flag = true;

	[Benchmark(Baseline = true)]
	public bool InlineSuccess()
	{
		Result<bool> result = new Success<bool>(_flag);
		return result.TryGetValue(out Success<bool> success) && success.Value;
	}

	[Benchmark]
	public bool BoxedSuccess()
	{
		BoxedResult<bool> result = new(new Success<bool>(_flag));
		return result.TryGetValue(out Success<bool> success) && success.Value;
	}

	[Benchmark]
	public ParseFailure InlineFailure()
	{
		Result<bool> result = MalformedBoolean;
		return result.TryGetValue(out Failure failure) ? failure.Reason : ParseFailure.Unspecified;
	}

	[Benchmark]
	public ParseFailure BoxedFailure()
	{
		BoxedResult<bool> result = new(MalformedBoolean);
		return result.TryGetValue(out Failure failure) ? failure.Reason : ParseFailure.Unspecified;
	}
}
```

- [ ] **Step 6: Create `benchmarks/Primitives.Benchmarks/DispatchBenchmarks.cs`**

```csharp
using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace Norse.Primitives.Benchmarks;

[MemoryDiagnoser]
public class DispatchBenchmarks
{
	const string BoolInput = "yes";
	const string IntInput = "1742";

	static readonly IFormatProvider Invariant = CultureInfo.InvariantCulture;

	[Benchmark(Baseline = true)]
	public Result<bool> DirectSpecialist() =>
		BooleanParser.ParseRequired(BoolInput);

	[Benchmark]
	public Result<bool> GatewayBool() =>
		Parser.ParseRequired<bool>(BoolInput, Invariant);

	[Benchmark]
	public Result<int> GatewayInt() =>
		Parser.ParseRequired<int>(IntInput, Invariant);
}
```

- [ ] **Step 7: Create `benchmarks/Primitives.Benchmarks/CombinatorBenchmarks.cs`**

```csharp
using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace Norse.Primitives.Benchmarks;

[MemoryDiagnoser]
public class CombinatorBenchmarks
{
	static readonly Func<int, int> AddEleven = x => x + 11;

	static readonly Func<int, Result<int>> DoubleOdd = x =>
	{
		if (x % 2 == 1)
			return new Success<int>(x * 2);
		return new Failure(ParseFailure.Malformed, "even", "Int32");
	};

	static readonly Func<int, string> RenderValue = x => x.ToString(CultureInfo.InvariantCulture);

	static readonly Func<Failure, string> RenderFailure = failure => failure.Reason.ToString();

	readonly int _seed = 10;

	[Benchmark(Baseline = true)]
	public string HandRolledSwitch()
	{
		Result<int> seeded = new Success<int>(_seed);
		if (!seeded.TryGetValue(out Success<int> first))
			return RenderFailureOf(seeded);
		var bound = DoubleOdd(AddEleven(first.Value));
		return bound.TryGetValue(out Success<int> second)
			? RenderValue(second.Value)
			: RenderFailureOf(bound);
	}

	[Benchmark]
	public string CombinatorChain()
	{
		Result<int> seeded = new Success<int>(_seed);
		return seeded.Map(AddEleven).Bind(DoubleOdd).Match(RenderValue, RenderFailure);
	}

	static string RenderFailureOf(Result<int> result) =>
		result.TryGetValue(out Failure failure) ? RenderFailure(failure) : "default";
}
```

- [ ] **Step 8: Add the benchmarks folder to `Svartalfheim.slnx`**

Full new file content:

```xml
<Solution>
	<Folder Name="/Solution Items/">
		<File Path=".editorconfig" />
		<File Path=".gitattributes" />
		<File Path=".gitignore" />
		<File Path="Directory.Build.props" />
		<File Path="global.json" />
		<File Path="LICENSE" />
	</Folder>
	<Folder Name="/benchmarks/">
		<File Path="benchmarks/Directory.Build.props" />
		<Project Path="benchmarks/Primitives.Benchmarks/Primitives.Benchmarks.csproj" />
	</Folder>
	<Folder Name="/src/">
		<Project Path="src/Primitives/Primitives.csproj" />
	</Folder>
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<Project Path="tests/Primitives.Tests/Primitives.Tests.csproj" />
	</Folder>
</Solution>
```

- [ ] **Step 9: Build and smoke-list the benchmarks**

Run: `dotnet build Svartalfheim.slnx`
Expected: `Build succeeded`, 0 warnings, 0 errors.

Run: `dotnet run -c Release --project benchmarks/Primitives.Benchmarks -- --list flat`
Expected: nine benchmark names listed (4 storage, 3 dispatch, 2 combinator). No execution yet.

- [ ] **Step 10: Stage (no commit — human commits)**

```powershell
git add benchmarks/ Svartalfheim.slnx
git status
```

---

### Task 5: AOT smoke

A console that exercises the whole pathway and must survive native compilation. Lives under `tests/smoke/` with its own `Directory.Build.props` that **bypasses** `tests/Directory.Build.props` (this is a console, not an xUnit project — inheriting the xUnit packages and `IsTestProject` would poison it).

**Files:**
- Create: `tests/smoke/Directory.Build.props`
- Create: `tests/smoke/Primitives.Aot.Smoke/Primitives.Aot.Smoke.csproj`
- Create: `tests/smoke/Primitives.Aot.Smoke/Program.cs`
- Modify: `Svartalfheim.slnx`

- [ ] **Step 1: Create `tests/smoke/Directory.Build.props`**

MSBuild stops at the nearest `Directory.Build.props` walking up, so this file shadows `tests/Directory.Build.props` for everything under `tests/smoke/`; the import reaches past it to the repo root.

```xml
<Project>
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../../'))" />
	<PropertyGroup>
		<!-- Console smoke projects, not xUnit projects: deliberately bypasses tests/Directory.Build.props. -->
		<IsPackable>false</IsPackable>
		<NoWarn>$(NoWarn);CS1591</NoWarn>
	</PropertyGroup>
</Project>
```

- [ ] **Step 2: Create `tests/smoke/Primitives.Aot.Smoke/Primitives.Aot.Smoke.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<PublishAot>true</PublishAot>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="..\..\..\src\Primitives\Primitives.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 3: Create `tests/smoke/Primitives.Aot.Smoke/Program.cs`**

```csharp
using System.Globalization;
using Norse.Primitives;

var invariant = CultureInfo.InvariantCulture;
var failures = 0;

Check("gateway routes the bool specialist's vocabulary", () =>
	Parser.ParseRequired<bool>("yes", invariant) == (Result<bool>)new Success<bool>(true));

Check("gateway parses int through the generic ISpanParsable path", () =>
	Parser.ParseRequired<int>("42", invariant) == (Result<int>)new Success<int>(42));

Check("gateway honors the declared provider", () =>
	Parser.ParseRequired<decimal>("1,5", CultureInfo.GetCultureInfo("de-DE")) == (Result<decimal>)new Success<decimal>(1.5m));

Check("combinator chain composes through the pathway", () =>
	Parser.ParseRequired<int>("21", invariant)
		.Map(x => x * 2)
		.Match(value => value == 42, _ => false));

Check("failure diagnostics survive the generic path", () =>
	Parser.ParseRequired<int>("bogus", invariant).TryGetValue(out Failure failure)
		&& failure is { Reason: ParseFailure.Malformed, Input: "bogus", ExpectedType: "Int32" });

Check("optional absence is null, not a failure", () =>
	Parser.ParseOptional<int>("   ", invariant) is null);

if (failures > 0)
{
	Console.Error.WriteLine($"AOT smoke FAILED: {failures} check(s) failed.");
	return 1;
}

Console.WriteLine("AOT smoke passed: the pathway survives native compilation.");
return 0;

void Check(string description, Func<bool> probe)
{
	bool passed;
	try
	{
		passed = probe();
	}
	catch (Exception exception)
	{
		Console.Error.WriteLine($"FAIL {description}: {exception}");
		failures++;
		return;
	}
	if (passed)
	{
		Console.WriteLine($"ok   {description}");
	}
	else
	{
		Console.Error.WriteLine($"FAIL {description}");
		failures++;
	}
}
```

(The catch-all records the failure and fails the run loudly — it is the opposite of a swallow.)

- [ ] **Step 4: Add the smoke project to `Svartalfheim.slnx`**

In the `/tests/` folder element, add the smoke props file and project so the folder reads:

```xml
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<File Path="tests/smoke/Directory.Build.props" />
		<Project Path="tests/Primitives.Tests/Primitives.Tests.csproj" />
		<Project Path="tests/smoke/Primitives.Aot.Smoke/Primitives.Aot.Smoke.csproj" />
	</Folder>
```

- [ ] **Step 5: Verify `dotnet test` still discovers only the xUnit project**

Run: `dotnet test Svartalfheim.slnx`
Expected: PASS — same test count as Task 3 Step 4; the smoke and benchmarks projects are not test-discovered (no `IsTestProject`).

- [ ] **Step 6: Publish native and run the binary**

Prerequisite: Native AOT on Windows needs the Visual Studio C++ build tools ("Desktop development with C++"). If `dotnet publish` fails with "Platform linker not found", STOP and report — that is an environment gap for the human, not a code problem.

Run: `dotnet publish tests/smoke/Primitives.Aot.Smoke -c Release`
Expected: publish succeeds with **zero** AOT/trim warnings (any warning is an error under the ratchet anyway).

Run the produced native executable:

```powershell
& (Get-ChildItem tests/smoke/Primitives.Aot.Smoke/bin/Release/net11.0/*/publish/Primitives.Aot.Smoke.exe) ; $LASTEXITCODE
```

Expected: six `ok` lines, "AOT smoke passed: the pathway survives native compilation.", exit code `0`.

- [ ] **Step 7: Stage (no commit — human commits)**

```powershell
git add tests/smoke/ Svartalfheim.slnx
git status
```

---

### Task 6: Run the benchmarks, capture the evidence

**Files:** none created in the repo — output goes to `BenchmarkDotNet.Artifacts/` (gitignored) and into the Task 7 findings.

- [ ] **Step 1: Run all three families in Release**

Run: `dotnet run -c Release --project benchmarks/Primitives.Benchmarks -- --filter *`
Expected: BenchmarkDotNet completes all nine benchmarks (takes several minutes). Capture the three summary tables (Mean, Ratio, Allocated) verbatim.

- [ ] **Step 2: Read the evidence against the three claims**

- **Storage:** does inline beat boxed on the success path (and `Allocated` show `-` for inline vs nonzero for boxed)? Failure path comparable or better?
- **Dispatch:** is `GatewayBool` within noise of `DirectSpecialist` (branch eliminated), and `GatewayInt` allocation-free on success?
- **Combinator tax:** is `CombinatorChain` within a small constant factor of `HandRolledSwitch` with identical `Allocated`?

If any claim fails, that is a finding, not a defect to hide — record it and continue; §10.1 re-opens in court on the evidence (spec §4.1).

---

### Task 7: Court filings and boy-scout docs

**Files:**
- Modify: `../Glitnir/docs/superpowers/specs/2026-06-11-svartalfheim-pathway-proof-design.md` (append findings)
- Modify: `CLAUDE.md` (realm)
- Modify: `README.md` (realm)

- [ ] **Step 1: Append the findings amendment to the pathway spec (in `../Glitnir`)**

Append a new section at the end of `2026-06-11-svartalfheim-pathway-proof-design.md`, filling the bracketed slots with the Task 6 measurements (machine line comes from BenchmarkDotNet's own header):

```markdown
## 8. Findings amendment (2026-06-11, post-execution)

Benchmark evidence from the §4.1 rig, run on: [BenchmarkDotNet environment header — OS, CPU, .NET version].

### Storage A/B

[verbatim BenchmarkDotNet summary table]

**Reading:** [inline vs boxed, success and failure paths, allocation column. State plainly whether §10.1 stays closed (inline confirmed) or re-opens.]

### Dispatch

[verbatim BenchmarkDotNet summary table]

**Reading:** [GatewayBool vs DirectSpecialist — is the typeof branch eliminated? GatewayInt success-path allocation.]

### Combinator tax

[verbatim BenchmarkDotNet summary table]

**Reading:** [chain vs hand-rolled switch — time ratio and allocation parity.]

### AOT smoke

Published with PublishAot on [RID]: [zero warnings — yes/no]; native binary exit code [0/nonzero]. [One line: the pathway survives native compilation, or what failed.]

### Gate results

- FsCheck integration gate (§3.4): [probe result — FsCheck.Xunit.v3 exists/does not exist; portable Prop.ForAll style adopted either way.]
- FsCheck package version restored: [version].
```

- [ ] **Step 2: Update the realm `CLAUDE.md`**

Three edits:

1. In **Authoritative documents**, insert as the new item 1 (renumber the rest):

```markdown
1. `../Glitnir/docs/superpowers/specs/2026-06-11-svartalfheim-pathway-proof-design.md` — the second-increment spec (gateway, combinators, evidence rigs), amended with benchmark findings. (Its execution plan sits beside it under `plans/`.)
```

2. In **Architecture Facts**, append:

```markdown
- **`Parser` is the generic gateway** over `where T : notnull, ISpanParsable<T>` — `bool` routes to `BooleanParser` via a JIT-eliminated `typeof` branch (`Unsafe.As` identity reinterpret; sound because `T` is statically `bool` inside the branch); everything else goes through `T.TryParse(span, provider)`. The provider is required and non-nullable — no defaulting overload, ever. No runtime registry: a type that cannot parse does not compile.
- **Combinators are `Map`/`Bind`/`Match` only** (law-proving core; `Combine`/async/`*Present` wait for a consumer). They are instance methods implemented as union switches — a defaulted `Result<T>` throws `SwitchExpressionException` through them identically to a hand-written switch. The five functor/monad laws are FsCheck-pinned in `ResultLawTests`.
```

3. In **Build & Test**, append:

```markdown
- Benchmarks (manual, Release): `dotnet run -c Release --project benchmarks/Primitives.Benchmarks -- --filter *`. Findings are court filings — file them as amendments to the pathway spec in Glitnir, never as loose notes.
- AOT smoke: `dotnet publish tests/smoke/Primitives.Aot.Smoke -c Release`, then run the native exe — zero AOT warnings and exit 0 required. Needs VS C++ build tools on Windows.
```

4. Replace the **Deferred Increments** section body with:

```markdown
1. Remaining hot-path parsers (the cartesian explosion, deliberately held until the pathway proof landed — it has).
2. `Combine`, async combinator siblings, `*Present` variants — await the consumer (parsers/ingestion increment).
3. NuGet packaging metadata.

Each increment is spec-first: brainstorm → spec → plan → code, with explicit human greenlights at each transition.
```

- [ ] **Step 3: Update the realm `README.md`**

In **What's forged here**, replace the `Result<T>` bullet and add a gateway bullet so the list reads:

```markdown
- **`Result<T>`** — a hand-authored C# custom native union over `Success<T>` / `Failure`: zero boxing on both paths, exhaustive two-arm switches, no way to construct an invalid value, and a law-proven combinator core (`Map` / `Bind` / `Match` — the functor and monad laws are FsCheck-pinned).
- **`ParseFailure`** — the closed conversion-failure vocabulary (`Empty`, `Malformed`); adding a member is a deliberate breaking change.
- **`Parser`** — the generic gateway over `ISpanParsable<T>`: span in, `Result<T>` out, uniform failure semantics, required format provider. Specialists ride JIT-eliminated `typeof` routes; there is no runtime registry — a type that cannot parse does not compile.
- **Hot-path parsers** — static specialists (`BooleanParser` first; siblings to follow) with `ParseRequired` / `ParseOptional` entry points over `ReadOnlySpan<char>` and honest signatures. Ambiguous input fails loudly; nothing is guessed, nothing falls back silently.
```

In **Build and test**, after the existing code block, add:

```markdown
Evidence rigs: `benchmarks/Primitives.Benchmarks` (BenchmarkDotNet — storage, dispatch, and combinator cost, run manually in Release) and `tests/smoke/Primitives.Aot.Smoke` (the pathway must survive `PublishAot` with zero warnings and exit 0).
```

- [ ] **Step 4: Stage both repos (no commit — human commits)**

In the Svartalfheim repo root:

```powershell
git add CLAUDE.md README.md
git status
```

In the court (`../Glitnir`):

```powershell
git -C ../Glitnir add docs/superpowers/specs/2026-06-11-svartalfheim-pathway-proof-design.md docs/superpowers/plans/2026-06-11-svartalfheim-pathway-proof.md
git -C ../Glitnir status
```

---

### Task 8: Full-solution verification and handoff

**Files:** none — verification only.

- [ ] **Step 1: Clean build**

Run: `dotnet build Svartalfheim.slnx`
Expected: `Build succeeded`, **0 warnings, 0 errors**.

- [ ] **Step 2: Full test run**

Run: `dotnet test Svartalfheim.slnx`
Expected: PASS — FailureTests, ResultTests, BooleanParserTests, ResultCombinatorTests, ResultLawTests, ParserTests all green. Capture the total test count for the report.

- [ ] **Step 3: Acceptance check against the spec (§6)**

Verify each, reporting yes/no:

- `Parser.ParseRequired<T>` / `ParseOptional<T>` exist with exactly the §2.2 surface; bool routes to `BooleanParser`; `int` proves the generic path; choreography and provider law under test.
- `Map`, `Bind`, `Match` exist with exactly the §3.1 surface — nothing more (no `Combine`, no async, no `*Present`).
- All five laws hold under FsCheck; the §3.4 gate is resolved and recorded in the findings amendment.
- Defaulted-`Result<T>` behavior through every combinator is pinned (`SwitchExpressionException`).
- Three benchmark families ran; findings filed in the spec amendment.
- AOT smoke published with zero warnings; native binary exited 0.

- [ ] **Step 4: Final stage and handoff (no commit — human commits)**

```powershell
git add -A
git status
git diff --cached --stat
```

Expected: full Svartalfheim inventory staged (Glitnir was staged in Task 7). Report the staged file list, the test count, and the benchmark headline numbers. **Do not commit** — the human reviews in GitHub Desktop and commits both repos; the Bifrost gitlink advances after that.
