# Svartalfheim First Increment — `Result<T>` Union + BooleanParser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Record note (2026-06-11, post-execution):** this plan was executed as written and is preserved verbatim as the execution record; it was then hoisted from Svartalfheim `docs/superpowers/` into this court. Two later renames supersede the paths shown below — projects went brand-free (`src/Primitives/Primitives.csproj`, `tests/Primitives.Tests/Primitives.Tests.csproj`, with `AssemblyName`/`RootNamespace` injected as `Norse.$(MSBuildProjectName)` from `Directory.Build.props`) and the solution file is lore-named (`Svartalfheim.slnx`, was `Norse.Primitives.slnx`). See the spec's 2026-06-11 court-filing amendment.

**Goal:** Stand up the Svartalfheim repository (`Norse.Primitives`) with the `Result<T>` custom native union, its case types, and `BooleanParser` — fully test-covered.

**Architecture:** `Result<T>` is a hand-authored C# 15 custom union (`[Union]` readonly record struct with inline typed storage and the non-boxing access pattern) over two case types, `Success<T>` and `Failure`. `BooleanParser` is a static specialist parser returning `Result<bool>` / `Result<bool>?` with the full ten-pair Crucible vocabulary via `FrozenSet` alternate lookup. Spec: `docs/superpowers/specs/2026-06-11-svartalfheim-result-union-boolean-parser-design.md`.

**Tech Stack:** .NET 11 preview 5 (SDK `11.0.100-preview.5.26302.115`), C# `LangVersion=preview`, xUnit v3 on Microsoft.Testing.Platform (`xunit.v3.mtp-v2`), Shouldly.

---

## Repo rules (read first)

- **NO automatic git commits.** Per the Norse-family rule (Glitnir CLAUDE.md "Working Agreement"): stage your edits with `git add`, show the diff, and STOP — the human runs `git commit` after review in GitHub Desktop. Every "Stage" step below means exactly that. Never run `git commit`, `git push`, `git reset`, or anything that creates/rewrites commits.
- **Working directory** for all commands: the Svartalfheim repo root. The repo currently contains only `LICENSE`, `.git`, and `docs/`.
- **Indentation is tabs** (the copied `.editorconfig` enforces it). All code blocks in this plan already use tabs — preserve them.
- **Warnings are errors** (`TreatWarningsAsErrors` + `WarningLevel 9999` + `EnforceCodeStyleInBuild`). A single warning fails the build. `GenerateDocumentationFile` is on, so **every public member in `src/` requires an XML doc comment** — the code blocks below include them; do not strip them.
- **Test filtering:** VSTest `--filter` does NOT work on Microsoft.Testing.Platform. Use: `dotnet test tests/Norse.Primitives.Tests -- --filter-class "*.ResultTests"`.
- **US English** in all code, comments, and docs.

---

### Task 1: Repository scaffold

Create the build skeleton: copied root configs, MSBuild props, solution file, and two empty projects that compile green.

**Files:**
- Create: `global.json`, `.editorconfig`, `.gitattributes` (copied from Bifrost root, one level up)
- Create: `.gitignore` (via `dotnet new gitignore`)
- Create: `Directory.Build.props`
- Create: `tests/Directory.Build.props`
- Create: `Norse.Primitives.slnx`
- Create: `src/Norse.Primitives/Norse.Primitives.csproj`
- Create: `tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj`

- [ ] **Step 1: Copy shared configs from the Bifrost meta-repo root**

Run (from the Svartalfheim repo root):

```powershell
Copy-Item ..\global.json .\global.json
Copy-Item ..\.editorconfig .\.editorconfig
Copy-Item ..\.gitattributes .\.gitattributes
dotnet new gitignore
```

Expected: four files exist at the repo root. `global.json` pins SDK `11.0.100-` with `allowPrerelease: true` and the `Microsoft.Testing.Platform` test runner. Verify with `dotnet --version` → `11.0.100-preview.5.26302.115`.

- [ ] **Step 2: Create `Directory.Build.props`**

```xml
<Project>
	<PropertyGroup>
		<!--
			Analyzer tiers follow the platform baseline: Security/Performance/Reliability/Usage
			at latest-All; Design stays at the global baseline because latest-All enables rules
			(e.g. CA1034) that conflict with discriminated-union-style type shapes.
		-->
		<AnalysisLevel>latest-Recommended</AnalysisLevel>
		<AnalysisLevelSecurity>latest-All</AnalysisLevelSecurity>
		<AnalysisLevelPerformance>latest-All</AnalysisLevelPerformance>
		<AnalysisLevelReliability>latest-All</AnalysisLevelReliability>
		<AnalysisLevelUsage>latest-All</AnalysisLevelUsage>
		<Authors>Norse Architecture</Authors>
		<Deterministic>true</Deterministic>
		<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
		<GenerateDocumentationFile>true</GenerateDocumentationFile>
		<ImplicitUsings>enable</ImplicitUsings>
		<LangVersion>preview</LangVersion>
		<Nullable>enable</Nullable>
		<TargetFramework>net11.0</TargetFramework>
		<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
		<WarningLevel>9999</WarningLevel>
	</PropertyGroup>
</Project>
```

- [ ] **Step 3: Create `tests/Directory.Build.props`**

Mirrors the Crucible's proven test-stack pinning (xUnit v3 transitive MTP is stale; float MTP right):

```xml
<Project>
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
	<PropertyGroup>
		<IsPackable>false</IsPackable>
		<IsTestProject>true</IsTestProject>
		<NoWarn>$(NoWarn);CA1812;CA1859;CS1591;IDE0051</NoWarn>
		<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.Testing.Platform.MSBuild" Version="2.*" />
		<PackageReference Include="Shouldly" Version="4.*" />
		<PackageReference Include="xunit.v3.mtp-v2" Version="3.*" />
		<Using Include="Shouldly" />
		<Using Include="Xunit" />
	</ItemGroup>
</Project>
```

- [ ] **Step 4: Create the two project files**

`src/Norse.Primitives/Norse.Primitives.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse forged primitives: the Result&lt;T&gt; discriminated union, closed parse-failure vocabulary, and hot-path scalar parsers for every boundary crossing into the Norse ecosystem from untrusted sources.</Description>
		<!-- Self-certify AOT/trim at the source so violations fail here, not downstream. -->
		<IsAotCompatible>true</IsAotCompatible>
	</PropertyGroup>
</Project>
```

`tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="..\..\src\Norse.Primitives\Norse.Primitives.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 5: Create `Norse.Primitives.slnx`**

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
	<Folder Name="/src/">
		<Project Path="src/Norse.Primitives/Norse.Primitives.csproj" />
	</Folder>
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<Project Path="tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj" />
	</Folder>
</Solution>
```

- [ ] **Step 6: Build the empty solution**

Run: `dotnet build Norse.Primitives.slnx`
Expected: `Build succeeded` with **0 warnings, 0 errors**. (Do not run `dotnet test` yet — xUnit v3 fails a run that discovers zero tests.)

- [ ] **Step 7: Stage (no commit — human commits)**

```powershell
git add global.json .editorconfig .gitattributes .gitignore Directory.Build.props Norse.Primitives.slnx src/ tests/
git status
```

Expected: all new files staged; report the file list and stop staging work for this task.

---

### Task 2: Union runtime-types probe (spec §6.1 gate)

Determine whether preview 5's `System.Runtime` ships `UnionAttribute` and `IUnion`. Preview 2 did not; the docs sanction declaring them locally until the runtime catches up.

**Files:**
- Possibly create: `src/Norse.Primitives/UnionCompilerServices.cs`

- [ ] **Step 1: Probe the ref pack's XML doc index**

Run:

```powershell
Select-String -Path "$env:ProgramFiles\dotnet\packs\Microsoft.NETCore.App.Ref\11.0.0-preview.5*\ref\net11.0\System.Runtime.xml" -Pattern "UnionAttribute|T:System.Runtime.CompilerServices.IUnion" -List
```

Expected: either matches (runtime HAS the types — skip Step 2, record the finding) or no output (runtime LACKS them — do Step 2). The build in Task 3 is the final arbiter either way; if the probe and the build disagree, trust the build.

- [ ] **Step 2 (only if the probe found nothing): Create `src/Norse.Primitives/UnionCompilerServices.cs`**

```csharp
// Local declarations of the C# 15 union support types, sanctioned by the .NET 11
// preview documentation for previews whose runtime does not yet ship them.
// DELETE THIS FILE when System.Runtime provides UnionAttribute and IUnion —
// the public surface is identical either way.
namespace System.Runtime.CompilerServices;

/// <summary>Marks a class or struct as a C# union type. Local stand-in for the runtime type.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class UnionAttribute : Attribute;

/// <summary>Provides access to a union's contents at runtime. Local stand-in for the runtime type.</summary>
public interface IUnion
{
	/// <summary>The value of the union, or <see langword="null"/>.</summary>
	object? Value { get; }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build Norse.Primitives.slnx`
Expected: `Build succeeded`, 0 warnings. If Step 2 was skipped, nothing changed and this confirms the baseline still builds.

- [ ] **Step 4: Stage (no commit — human commits)**

```powershell
git add src/Norse.Primitives/
git status
```

Record in your task report which branch the probe took — the spec's acceptance (§9) requires this gate resolved and visible in the code.

---

### Task 3: `ParseFailure`, `MustConsumeAttribute`, `Success<T>`, `Failure`

TDD on `Failure` (the only type with behavior); the other three are declarations its tests pull in.

**Files:**
- Test: `tests/Norse.Primitives.Tests/FailureTests.cs`
- Create: `src/Norse.Primitives/ParseFailure.cs`
- Create: `src/Norse.Primitives/MustConsumeAttribute.cs`
- Create: `src/Norse.Primitives/Success{T}.cs`
- Create: `src/Norse.Primitives/Failure.cs`

- [ ] **Step 1: Write the failing tests — `tests/Norse.Primitives.Tests/FailureTests.cs`**

```csharp
namespace Norse.Primitives.Tests;

public sealed class FailureTests
{
	[Fact]
	void Should_pass_input_through_when_within_bound()
	{
		var failure = new Failure(ParseFailure.Malformed, "bogus", "Boolean");
		failure.Input.ShouldBe("bogus");
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.ExpectedType.ShouldBe("Boolean");
		failure.Format.ShouldBeNull();
		failure.Detail.ShouldBeNull();
	}

	[Fact]
	void Should_truncate_input_when_longer_than_max()
	{
		var oversized = new string('x', Failure.MaxInputLength + 44);
		var failure = new Failure(ParseFailure.Malformed, oversized, "Boolean");
		failure.Input.Length.ShouldBe(Failure.MaxInputLength);
		failure.Input.ShouldBe(oversized[..Failure.MaxInputLength]);
	}

	[Fact]
	void Should_be_equal_when_all_fields_match()
	{
		var left = new Failure(ParseFailure.Empty, "", "Boolean");
		var right = new Failure(ParseFailure.Empty, "", "Boolean");
		left.ShouldBe(right);
	}

	[Fact]
	void Should_not_be_equal_when_reason_differs()
	{
		var left = new Failure(ParseFailure.Empty, "", "Boolean");
		var right = new Failure(ParseFailure.Malformed, "", "Boolean");
		left.ShouldNotBe(right);
	}

	[Theory]
	[InlineData(ParseFailure.Unspecified)]
	[InlineData((ParseFailure)99)]
	void Should_throw_when_reason_is_not_a_real_failure(ParseFailure reason) =>
		Should.Throw<ArgumentOutOfRangeException>(() => new Failure(reason, "x", "Boolean"));

	[Fact]
	void Should_throw_when_input_is_null() =>
		Should.Throw<ArgumentNullException>(() => new Failure(ParseFailure.Malformed, null!, "Boolean"));

	[Fact]
	void Should_throw_when_expected_type_is_missing() =>
		Should.Throw<ArgumentException>(() => new Failure(ParseFailure.Malformed, "x", ""));
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/Norse.Primitives.Tests`
Expected: compilation FAILS with CS0246 (`Failure`/`ParseFailure` not found).

- [ ] **Step 3: Implement the four types**

`src/Norse.Primitives/ParseFailure.cs`:

```csharp
namespace Norse.Primitives;

/// <summary>
/// The closed set of reasons a scalar→domain conversion can fail.
/// Adding a member is a deliberate breaking change: every exhaustive switch
/// over this enum becomes a build error until updated.
/// </summary>
public enum ParseFailure
{
	/// <summary>Sentinel CLR default — never produced by any parse path.</summary>
	Unspecified = 0,

	/// <summary>Required input was empty or whitespace.</summary>
	Empty = 1,

	/// <summary>Input was present but not recognizable as the target type.</summary>
	Malformed = 2,
}
```

`src/Norse.Primitives/MustConsumeAttribute.cs`:

```csharp
namespace Norse.Primitives;

/// <summary>
/// Marks a type whose values must be consumed by the caller — pattern matched,
/// composed, returned, stored, or explicitly discarded. Enforced at build time
/// by the YGG201 analyzer (Norse.Primitives.Architecture, separate package).
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
public sealed class MustConsumeAttribute : Attribute;
```

`src/Norse.Primitives/Success{T}.cs`:

```csharp
namespace Norse.Primitives;

/// <summary>The success case of <see cref="Result{T}"/>: a validated domain value.</summary>
/// <typeparam name="T">The validated value's type. Non-nullable by construction.</typeparam>
/// <param name="Value">The validated value.</param>
public readonly record struct Success<T>(T Value) where T : notnull;
```

`src/Norse.Primitives/Failure.cs`:

```csharp
namespace Norse.Primitives;

/// <summary>
/// The failure case of <see cref="Result{T}"/>: a closed conversion reason
/// plus bounded diagnostics for logs and error rendering.
/// </summary>
public readonly record struct Failure
{
	/// <summary>Upper bound on captured input length — keeps failures log-safe.</summary>
	public const int MaxInputLength = 256;

	/// <summary>Creates a failure, truncating <paramref name="input"/> to <see cref="MaxInputLength"/>.</summary>
	/// <param name="reason">The conversion reason. The <see cref="ParseFailure.Unspecified"/> sentinel is rejected.</param>
	/// <param name="input">The raw input that failed. Captured bounded, never null.</param>
	/// <param name="expectedType">The CLR type name the input was expected to convert to, e.g. "Boolean".</param>
	/// <param name="format">The declared format, when an explicit one was given.</param>
	/// <param name="detail">Optional human-readable detail from richer parsers.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="reason"/> is not a real failure reason.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
	/// <exception cref="ArgumentException"><paramref name="expectedType"/> is null or empty.</exception>
	public Failure(ParseFailure reason, string input, string expectedType, string? format = null, string? detail = null)
	{
		if (reason is not (ParseFailure.Empty or ParseFailure.Malformed))
			throw new ArgumentOutOfRangeException(nameof(reason), reason, "Reason must be a real failure, not the Unspecified sentinel.");
		ArgumentNullException.ThrowIfNull(input);
		ArgumentException.ThrowIfNullOrEmpty(expectedType);
		Reason = reason;
		Input = input.Length <= MaxInputLength ? input : input[..MaxInputLength];
		ExpectedType = expectedType;
		Format = format;
		Detail = detail;
	}

	/// <summary>The closed-set conversion reason.</summary>
	public ParseFailure Reason { get; }

	/// <summary>The raw input, truncated to <see cref="MaxInputLength"/>.</summary>
	public string Input { get; }

	/// <summary>The CLR type name the input was expected to convert to.</summary>
	public string ExpectedType { get; }

	/// <summary>The declared format, when an explicit one was given; otherwise null.</summary>
	public string? Format { get; }

	/// <summary>Optional human-readable detail; otherwise null.</summary>
	public string? Detail { get; }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Norse.Primitives.Tests`
Expected: PASS — all FailureTests green, 0 warnings.

- [ ] **Step 5: Stage (no commit — human commits)**

```powershell
git add src/Norse.Primitives/ tests/Norse.Primitives.Tests/
git status
```

---

### Task 4: `Result<T>` — the custom union

TDD with tests that pin the **compiler contract** (union conversion, unwrapping, exhaustiveness) as much as our own code. If any expected-compile step fails to compile in a way the plan doesn't predict, STOP and report — that's a broken design assumption, not something to code around.

**Files:**
- Test: `tests/Norse.Primitives.Tests/ResultTests.cs`
- Create: `src/Norse.Primitives/Result{T}.cs`

- [ ] **Step 1: Write the failing tests — `tests/Norse.Primitives.Tests/ResultTests.cs`**

```csharp
using System.Runtime.CompilerServices;

namespace Norse.Primitives.Tests;

public sealed class ResultTests
{
	static Failure MalformedBoolean(string input = "bogus") =>
		new(ParseFailure.Malformed, input, "Boolean");

	[Fact]
	void Should_match_success_case_when_constructed_from_success()
	{
		// Assignment without an explicit constructor call IS the implicit union conversion.
		Result<bool> result = new Success<bool>(true);
		var matched = result switch
		{
			Success<bool>(var value) => value,
			Failure => false,
		};
		matched.ShouldBeTrue();
	}

	[Fact]
	void Should_match_failure_case_when_constructed_from_failure()
	{
		Result<bool> result = MalformedBoolean();
		var matched = result switch
		{
			Success<bool> => null as ParseFailure?,
			Failure failure => failure.Reason,
		};
		matched.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Should_not_match_result_type_pattern_when_tested()
	{
		// Union patterns unwrap to the contents — Result<bool> itself is not a case type.
		Result<bool> result = new Success<bool>(true);
		(result is Result<bool>).ShouldBeFalse();
	}

	[Fact]
	void Should_expose_boxed_success_when_value_read_directly()
	{
		Result<bool> result = new Success<bool>(true);
		result.Value.ShouldBeOfType<Success<bool>>().Value.ShouldBeTrue();
	}

	[Fact]
	void Should_expose_boxed_failure_when_value_read_directly()
	{
		Result<bool> result = MalformedBoolean();
		result.Value.ShouldBeOfType<Failure>().Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Should_report_access_pattern_consistent_with_value_when_success()
	{
		Result<bool> result = new Success<bool>(true);
		result.HasValue.ShouldBeTrue();
		result.TryGetValue(out Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeTrue();
		result.TryGetValue(out Failure _).ShouldBeFalse();
	}

	[Fact]
	void Should_report_access_pattern_consistent_with_value_when_failure()
	{
		Result<bool> result = MalformedBoolean();
		result.HasValue.ShouldBeTrue();
		result.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		result.TryGetValue(out Success<bool> _).ShouldBeFalse();
	}

	[Fact]
	void Should_have_null_value_when_defaulted()
	{
		// The struct-union footgun, pinned: default(Result<T>) is neither case.
		var result = default(Result<bool>);
		result.Value.ShouldBeNull();
		result.HasValue.ShouldBeFalse();
		result.TryGetValue(out Success<bool> _).ShouldBeFalse();
		result.TryGetValue(out Failure _).ShouldBeFalse();
	}

	[Fact]
	void Should_throw_when_switching_defaulted_result() =>
		Should.Throw<SwitchExpressionException>(() =>
			default(Result<bool>) switch
			{
				Success<bool> => "success",
				Failure => "failure",
			});

	[Fact]
	void Should_be_equal_when_same_success_value()
	{
		Result<bool> left = new Success<bool>(true);
		Result<bool> right = new Success<bool>(true);
		left.ShouldBe(right);
	}

	[Fact]
	void Should_not_be_equal_when_cases_differ()
	{
		Result<bool> left = new Success<bool>(false);
		Result<bool> right = MalformedBoolean();
		left.ShouldNotBe(right);
	}

	[Fact]
	void Should_render_case_shape_when_converted_to_string()
	{
		Result<bool> success = new Success<bool>(true);
		Result<bool> failure = MalformedBoolean();
		success.ToString().ShouldBe("Success(True)");
		failure.ToString().ShouldBe("Failure(Malformed, \"bogus\")");
	}

	[Fact]
	void Should_carry_must_consume_attribute_when_inspected() =>
		typeof(Result<>).IsDefined(typeof(MustConsumeAttribute), inherit: false).ShouldBeTrue();
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/Norse.Primitives.Tests`
Expected: compilation FAILS with CS0246 (`Result<>` not found).

- [ ] **Step 3: Implement `src/Norse.Primitives/Result{T}.cs`**

```csharp
using System.Runtime.CompilerServices;

namespace Norse.Primitives;

/// <summary>
/// The outcome of a scalar→domain conversion: exactly one of
/// <see cref="Success{T}"/> or <see cref="Failure"/>, as a native C# union.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern matching unwraps to the case types.</b> Match against
/// <see cref="Success{T}"/> or <see cref="Failure"/> — never against
/// <c>Result&lt;T&gt;</c> itself; <c>result is Result&lt;T&gt;</c> does not match.
/// A two-arm switch over both case types is exhaustive.
/// </para>
/// <para>
/// <b>Do not use <c>default(Result&lt;T&gt;)</c> or <c>new Result&lt;T&gt;()</c>.</b>
/// Like <c>default(ImmutableArray&lt;T&gt;)</c>, a defaulted value is malformed by
/// construction: union well-formedness requires its <see cref="Value"/> to be null,
/// so it matches neither case and an exhaustive switch throws
/// <see cref="SwitchExpressionException"/> at first consumption.
/// </para>
/// <para>
/// This type hand-implements the union pattern (rather than using a shorthand
/// <c>union</c> declaration) so both cases are stored inline and nothing boxes
/// on either path. The compiler routes pattern matching through
/// <see cref="TryGetValue(out Success{T})"/> / <see cref="TryGetValue(out Failure)"/>;
/// only a direct read of <see cref="Value"/> boxes.
/// </para>
/// </remarks>
/// <typeparam name="T">The validated value's type. Non-nullable by construction.</typeparam>
[MustConsume]
[Union]
public readonly record struct Result<T> : IUnion where T : notnull
{
	enum State : byte
	{
		Default = 0,
		Success = 1,
		Failure = 2,
	}

	readonly Success<T> _success;
	readonly Failure _failure;
	readonly State _state;

	/// <summary>Creates a successful result. Also reachable as an implicit union conversion.</summary>
	/// <param name="value">The validated value.</param>
	public Result(Success<T> value)
	{
		_success = value;
		_state = State.Success;
	}

	/// <summary>Creates a failed result. Also reachable as an implicit union conversion.</summary>
	/// <param name="value">The conversion failure.</param>
	public Result(Failure value)
	{
		_failure = value;
		_state = State.Failure;
	}

	/// <summary>
	/// The boxed case contents, or <see langword="null"/> for a defaulted value.
	/// Pattern matching does not read this property; a direct read boxes.
	/// </summary>
	public object? Value =>
		_state switch
		{
			State.Success => _success,
			State.Failure => _failure,
			_ => null,
		};

	/// <summary><see langword="true"/> unless this value was defaulted rather than constructed.</summary>
	public bool HasValue =>
		_state != State.Default;

	/// <summary>Retrieves the success case without boxing.</summary>
	/// <param name="value">The success case when present; default otherwise.</param>
	/// <returns><see langword="true"/> if this result is the success case.</returns>
	public bool TryGetValue(out Success<T> value)
	{
		value = _success;
		return _state == State.Success;
	}

	/// <summary>Retrieves the failure case without boxing.</summary>
	/// <param name="value">The failure case when present; default otherwise.</param>
	/// <returns><see langword="true"/> if this result is the failure case.</returns>
	public bool TryGetValue(out Failure value)
	{
		value = _failure;
		return _state == State.Failure;
	}

	/// <summary>Renders "Success(value)", "Failure(Reason, "input")", or "Default(invalid)".</summary>
	public override string ToString() =>
		_state switch
		{
			State.Success => $"Success({_success.Value})",
			State.Failure => $"Failure({_failure.Reason}, \"{_failure.Input}\")",
			_ => "Default(invalid)",
		};
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Norse.Primitives.Tests`
Expected: PASS — all ResultTests and FailureTests green, 0 warnings.

Known risks to watch (report, do not work around):
- If the exhaustive two-arm switch in `Should_match_success_case_when_constructed_from_success` produces CS8509 (non-exhaustive), the preview compiler is not honoring union exhaustiveness for this custom union — STOP and report.
- If `(result is Result<bool>)` produces CS0183 ("is always true"), the compiler is not applying union unwrapping to the is-operator — STOP and report.
- If the implicit assignments (`Result<bool> result = new Success<bool>(true);`) fail with CS0029, union conversions are not being generated from the constructors — STOP and report.

- [ ] **Step 5: Stage (no commit — human commits)**

```powershell
git add src/Norse.Primitives/ tests/Norse.Primitives.Tests/
git status
```

---

### Task 5: `BooleanParser`

TDD: port the Crucible's regression matrix first (extended with `Failure`-shape assertions it never made), then implement.

**Files:**
- Test: `tests/Norse.Primitives.Tests/BooleanParserTests.cs`
- Create: `src/Norse.Primitives/BooleanParser.cs`

- [ ] **Step 1: Write the failing tests — `tests/Norse.Primitives.Tests/BooleanParserTests.cs`**

```csharp
namespace Norse.Primitives.Tests;

public sealed class BooleanParserTests
{
	const string AllWhitespace = " \t\r\n\f ";

	[Theory]
	[InlineData("true")]
	[InlineData("True")]
	[InlineData("TRUE")]
	[InlineData("t")]
	[InlineData("T")]
	[InlineData("false", false)]
	[InlineData("False", false)]
	[InlineData("FALSE", false)]
	[InlineData("f", false)]
	[InlineData("F", false)]
	[InlineData("yes")]
	[InlineData("Yes")]
	[InlineData("YES")]
	[InlineData("y")]
	[InlineData("Y")]
	[InlineData("no", false)]
	[InlineData("No", false)]
	[InlineData("NO", false)]
	[InlineData("n", false)]
	[InlineData("N", false)]
	[InlineData("1")]
	[InlineData("0", false)]
	[InlineData("on")]
	[InlineData("On")]
	[InlineData("ON")]
	[InlineData("off", false)]
	[InlineData("Off", false)]
	[InlineData("OFF", false)]
	[InlineData("enabled")]
	[InlineData("Enabled")]
	[InlineData("ENABLED")]
	[InlineData("disabled", false)]
	[InlineData("Disabled", false)]
	[InlineData("DISABLED", false)]
	[InlineData("active")]
	[InlineData("Active")]
	[InlineData("inactive", false)]
	[InlineData("InAcTiVe", false)]
	[InlineData("checked")]
	[InlineData("CheckeD")]
	[InlineData("unchecked", false)]
	[InlineData("UnchEcked", false)]
	[InlineData("in")]
	[InlineData("In")]
	[InlineData("out", false)]
	[InlineData("Out", false)]
	[InlineData("\ttrue\n")]
	[InlineData("  Y  ")]
	void Should_parse_value_when_input_is_recognized(string input, bool expected = true)
	{
		var actual = BooleanParser.ParseRequired(input);
		actual.TryGetValue(out Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBe(expected);
	}

	[Theory]
	[InlineData("yes")]
	[InlineData("0")]
	void Should_parse_value_when_optional_input_is_recognized(string input)
	{
		var actual = BooleanParser.ParseOptional(input);
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Success<bool> _).ShouldBeTrue();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_fail_with_empty_reason_when_required_input_is_absent(string? input)
	{
		var actual = BooleanParser.ParseRequired(input);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
		failure.Input.ShouldBe(string.Empty);
		failure.ExpectedType.ShouldBe("Boolean");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(AllWhitespace)]
	void Should_return_absent_when_optional_input_is_absent(string? input)
	{
		var actual = BooleanParser.ParseOptional(input);
		actual.HasValue.ShouldBeFalse();
	}

	[Theory]
	[InlineData("invalid")]
	[InlineData("2")]
	[InlineData("maybe")]
	[InlineData("unknown")]
	// ReSharper disable StringLiteralTypo
	[InlineData("truee")]
	[InlineData("\tyess\n")]
	// ReSharper restore StringLiteralTypo
	void Should_fail_with_malformed_reason_when_input_is_unrecognized(string input)
	{
		var actual = BooleanParser.ParseRequired(input);
		actual.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Input.ShouldBe(input.Trim());
		failure.ExpectedType.ShouldBe("Boolean");
		failure.Format.ShouldBeNull();
		failure.Detail.ShouldBeNull();
	}

	[Fact]
	void Should_fail_with_malformed_reason_when_optional_input_is_unrecognized()
	{
		var actual = BooleanParser.ParseOptional("maybe");
		actual.HasValue.ShouldBeTrue();
		actual.Value.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}
}
```

Note: in the optional tests, `actual` is `Result<bool>?` — `.HasValue`/`.Value` there are `Nullable<T>` members (presence), and the union's own members are reached through `.Value`. That layering is the spec's presence/validity separation, exercised deliberately.

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/Norse.Primitives.Tests`
Expected: compilation FAILS with CS0103 (`BooleanParser` not found).

- [ ] **Step 3: Implement `src/Norse.Primitives/BooleanParser.cs`**

```csharp
using System.Collections.Frozen;

namespace Norse.Primitives;

/// <summary>
/// Span-based parser for <see cref="bool"/>. Extends
/// <see cref="bool.TryParse(ReadOnlySpan{char}, out bool)"/> with the numeric and
/// natural-language conventions untrusted sources actually send.
/// </summary>
/// <remarks>
/// Recognized true values: <c>true</c>, <c>t</c>, <c>yes</c>, <c>y</c>, <c>1</c>,
/// <c>on</c>, <c>enabled</c>, <c>active</c>, <c>checked</c>, <c>in</c>.
/// Recognized false values: <c>false</c>, <c>f</c>, <c>no</c>, <c>n</c>, <c>0</c>,
/// <c>off</c>, <c>disabled</c>, <c>inactive</c>, <c>unchecked</c>, <c>out</c>.
/// Matching is case-insensitive; leading and trailing whitespace is ignored.
/// Boolean text is culture-insensitive, so no format provider is accepted.
/// </remarks>
public static class BooleanParser
{
	const string ExpectedType = "Boolean";

	static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> _trueValues =
		new[] { "t", "yes", "y", "1", "on", "enabled", "active", "checked", "in" }
			.ToFrozenSet(StringComparer.OrdinalIgnoreCase)
			.GetAlternateLookup<ReadOnlySpan<char>>();

	static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> _falseValues =
		new[] { "f", "no", "n", "0", "off", "disabled", "inactive", "unchecked", "out" }
			.ToFrozenSet(StringComparer.OrdinalIgnoreCase)
			.GetAlternateLookup<ReadOnlySpan<char>>();

	/// <summary>
	/// Parses required boolean text. Empty or whitespace input is a
	/// <see cref="ParseFailure.Empty"/> failure; unrecognized input is
	/// <see cref="ParseFailure.Malformed"/>.
	/// </summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <returns>The parse outcome — never throws on bad input.</returns>
	public static Result<bool> ParseRequired(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return new Failure(ParseFailure.Empty, string.Empty, ExpectedType);
		return Parse(trimmed);
	}

	/// <summary>
	/// Parses optional boolean text. Empty or whitespace input is absent
	/// (<see langword="null"/>); unrecognized input is <see cref="ParseFailure.Malformed"/>.
	/// </summary>
	/// <param name="input">The raw scalar text. A null string converts to the empty span.</param>
	/// <returns><see langword="null"/> when absent; otherwise the parse outcome.</returns>
	public static Result<bool>? ParseOptional(ReadOnlySpan<char> input)
	{
		var trimmed = input.Trim();
		if (trimmed.IsEmpty)
			return null;
		return Parse(trimmed);
	}

	static Result<bool> Parse(ReadOnlySpan<char> trimmed)
	{
		if (bool.TryParse(trimmed, out var parsed))
			return new Success<bool>(parsed);
		if (_trueValues.Contains(trimmed))
			return new Success<bool>(true);
		if (_falseValues.Contains(trimmed))
			return new Success<bool>(false);
		return new Failure(ParseFailure.Malformed, trimmed.ToString(), ExpectedType);
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Norse.Primitives.Tests`
Expected: PASS — the full suite green, 0 warnings.

- [ ] **Step 5: Stage (no commit — human commits)**

```powershell
git add src/Norse.Primitives/ tests/Norse.Primitives.Tests/
git status
```

---

### Task 6: Full-solution verification and handoff

**Files:** none created — verification only.

- [ ] **Step 1: Clean build of the whole solution**

Run: `dotnet build Norse.Primitives.slnx`
Expected: `Build succeeded`, **0 warnings, 0 errors** (warnings-as-errors makes any warning a failure, so success implies clean).

- [ ] **Step 2: Full test run**

Run: `dotnet test Norse.Primitives.slnx`
Expected: PASS — every test in FailureTests, ResultTests, and BooleanParserTests green. Capture the test count in the report.

- [ ] **Step 3: Acceptance check against the spec (§9)**

Verify each, reporting yes/no:
- The five public types (`Result<T>`, `Success<T>`, `Failure`, `ParseFailure`, `MustConsumeAttribute`) and `BooleanParser` exist with exactly the spec'd surfaces — nothing more (no extra public members snuck in).
- The §6.1 union-runtime-types gate is resolved and recorded (either consuming the runtime types or `UnionCompilerServices.cs` exists with its removal note).
- AOT/trim analyzers are clean (implied by the 0-warning build with `IsAotCompatible`).

- [ ] **Step 4: Stage everything and hand off to the human**

```powershell
git add -A
git status
git diff --cached --stat
```

Expected: full file inventory staged. Report the staged file list and the test count. **Do not commit** — the human reviews in GitHub Desktop and commits.

---

## Deferred (recorded in the spec §10 — do not build these)

Generic `ISpanParsable<T>` gateway · combinators + FsCheck monad laws · AOT smoke-test project · benchmarks · NuGet packaging metadata · additional parsers.
