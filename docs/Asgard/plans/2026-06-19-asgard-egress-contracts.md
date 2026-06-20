# Asgard Egress Contracts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:subagent-driven-development` is the default orchestration skill for this plan — not a recommendation among equals; `superpowers:executing-plans` is the narrow fallback for a separate session with human review checkpoints. Pair it with `superpowers:test-driven-development` on every task — orchestration sequences tasks, TDD governs how each one is coded (`../../CLAUDE.md` §2.8). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up `Norse.Abstractions.Egress` in Asgard — the pure-contracts slice of the platform egress spec: the `HttpResult<T>` discriminated union, the closed `EgressError`/`FailureKind` vocabulary, the `EgressClassifier` response-classification seam, and the `IHttpEgress`/`IResponseParser<T>` injection surfaces. Nothing past HttpClient mechanics and the union — no auth seam, no resilience-profile enum, no registration extensions (those are Infrastructure/Hosting work, future Midgard/Yggdrasil plans).

**Architecture:** A hand-authored, zero-boxing C# union (mirroring `Result<T>` in `Norse.Primitives` exactly: `[MustConsume]` + `[Union]`/`IUnion`, per-case constructors, `TryGetValue` overloads, `Match`) plus a delegate-based classification seam and two narrow interfaces. Asgard is a bare shell today — this plan scaffolds the repo from nothing.

**Tech Stack:** .NET 11 preview, C# `LangVersion=preview` (for the native union feature), xUnit v3 + Shouldly on Microsoft.Testing.Platform, NSubstitute for test doubles.

**Source spec:** `../Platform/specs/2026-06-07-egress-http-resilience-parsing-design.md` (status: "awaiting plan greenlight" — this plan is that greenlight, scoped to the Abstractions slice only; §3, §4 are this plan's direct source).

## Global Constraints

- `net11.0` target, `LangVersion=preview`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `WarningLevel=9999` — copied verbatim from Svartalfheim's `Directory.Build.props` (the only other realm with a built scaffold).
- `global.json` SDK floor `11.0.100-`, `rollForward: latestFeature`, `allowPrerelease: true` — must match Svartalfheim's pin so both repos build under the same local SDK.
- Tabs for indentation (`.editorconfig`, copied from Svartalfheim). `var` for return assignments only; construction uses explicit type + `new()`. Accessibility by omission (`omit_if_default`).
- One public type per file; filename matches the type exactly, including the `{T}` generic-arity suffix convention (`HttpResult{T}.cs`, `Found{T}.cs`) already used by `Result{T}.cs` in Svartalfheim.
- Test classes are `public sealed`; test methods omit the access modifier entirely (default `private` — xUnit v3/MTP discovers them; this is the existing house style in `Svartalfheim/tests/Primitives.Tests/ResultTests.cs`, not a deviation). Test naming: `Should_{behavior}_when_{condition}`.
- Shouldly assertions only (not FluentAssertions); NSubstitute test doubles only (not Moq) — **except** `IResponseParser<T>.Parse` takes a `ReadOnlySpan<byte>` (a ref struct), which dynamic-proxy frameworks including NSubstitute cannot intercept. Don't attempt `Substitute.For<IResponseParser<T>>()` and assert on a `Parse` call — write a real fake implementation instead (Task 4). `IHttpEgress` has no ref-struct parameters and substitutes normally (Task 5).
- **Dependency call (confirmed):** `Norse.Abstractions.Egress` takes a plain relative `ProjectReference` to Svartalfheim's `Primitives.csproj`, for `[MustConsume]` and `Result<T>`. This is the Mjolnir precedent: Asgard (the gods, Thor included) uses a tool forged in Svartalfheim (the dwarves) — lore and dependency direction agree. `Norse.Primitives` is "forged **below** the domain" platform-wide (Glitnir `CLAUDE.md` §1); Asgard's own `CLAUDE.md` §1 sentence ("Asgard rides on nothing") is the outlier and gets corrected in Task 6 Step 3, not the dependency. The reference is a raw relative path (`../../../Svartalfheim/src/Primitives/Primitives.csproj`) — wiring it into something more standardized is the separate "`UseProjectReferences` infrastructure" work flagged for a future Bifrost session; a raw `ProjectReference` needs none of that to function today.

---

## File Structure

```
Asgard/
  .editorconfig                                  (copied verbatim from Svartalfheim)
  .gitattributes                                  (copied verbatim from Svartalfheim)
  .gitignore                                      (copied verbatim from Svartalfheim)
  Directory.Build.props                           (new — root build settings)
  global.json                                     (new — SDK pin)
  Asgard.slnx                                     (new — solution)
  src/
    Directory.Build.props                         (new — InternalsVisibleTo seam)
    Abstractions.Egress/
      Abstractions.Egress.csproj
      FailureKind.cs
      EgressError.cs
      Found{T}.cs
      NotFound.cs
      Failure.cs
      HttpResult{T}.cs
      ResponseDisposition.cs
      EgressClassifier.cs
      Classify.cs
      IResponseParser{T}.cs
      IHttpEgress.cs
  tests/
    Directory.Build.props                         (new — xUnit v3/MTP + Shouldly, copied from Svartalfheim)
    Abstractions.Egress.Tests/
      Abstractions.Egress.Tests.csproj
      FailureKindTests.cs
      EgressErrorTests.cs
      HttpResultTests.cs
      ClassifyTests.cs
      IResponseParserTests.cs
      IHttpEgressTests.cs
```

---

### Task 1: Scaffold the repository and the failure vocabulary

**Files:**
- Create: `Asgard/Directory.Build.props`, `Asgard/global.json`, `Asgard/Asgard.slnx`, `Asgard/src/Directory.Build.props`, `Asgard/tests/Directory.Build.props`
- Copy: `Asgard/.editorconfig`, `Asgard/.gitattributes`, `Asgard/.gitignore` (from `Svartalfheim/.editorconfig` etc.)
- Create: `Asgard/src/Abstractions.Egress/Abstractions.Egress.csproj`
- Create: `Asgard/tests/Abstractions.Egress.Tests/Abstractions.Egress.Tests.csproj`
- Create: `Asgard/src/Abstractions.Egress/FailureKind.cs`
- Create: `Asgard/src/Abstractions.Egress/EgressError.cs`
- Test: `Asgard/tests/Abstractions.Egress.Tests/FailureKindTests.cs`
- Test: `Asgard/tests/Abstractions.Egress.Tests/EgressErrorTests.cs`

**Interfaces:**
- Produces: `enum FailureKind { Unspecified = 0, Transport = 1, Status = 2, EmptyBody = 3, Parse = 4 }`; `readonly record struct EgressError(FailureKind Kind, HttpStatusCode? StatusCode, string RawBody)` — both in `Norse.Abstractions.Egress`. Every later task's `Failure` case carries `EgressError`.

- [ ] **Step 1: Copy the repo-wide scaffold files**

```bash
cp /home/buvy/code/NorseArchitecture/Bifrost/Svartalfheim/.editorconfig /home/buvy/code/NorseArchitecture/Bifrost/Asgard/.editorconfig
cp /home/buvy/code/NorseArchitecture/Bifrost/Svartalfheim/.gitattributes /home/buvy/code/NorseArchitecture/Bifrost/Asgard/.gitattributes
cp /home/buvy/code/NorseArchitecture/Bifrost/Svartalfheim/.gitignore /home/buvy/code/NorseArchitecture/Bifrost/Asgard/.gitignore
```

- [ ] **Step 2: Write `Asgard/global.json`**

```json
{
  "sdk": {
    "version": "11.0.100-",
    "rollForward": "latestFeature",
    "allowPrerelease": true
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

- [ ] **Step 3: Write `Asgard/Directory.Build.props`**

```xml
<Project>
	<PropertyGroup>
		<AnalysisLevel>latest-Recommended</AnalysisLevel>
		<AnalysisLevelSecurity>latest-All</AnalysisLevelSecurity>
		<AnalysisLevelPerformance>latest-All</AnalysisLevelPerformance>
		<AnalysisLevelReliability>latest-All</AnalysisLevelReliability>
		<AnalysisLevelUsage>latest-All</AnalysisLevelUsage>
		<AssemblyName>Norse.$(MSBuildProjectName)</AssemblyName>
		<Authors>Norse Architecture</Authors>
		<Deterministic>true</Deterministic>
		<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
		<GenerateDocumentationFile>true</GenerateDocumentationFile>
		<ImplicitUsings>enable</ImplicitUsings>
		<LangVersion>preview</LangVersion>
		<Nullable>enable</Nullable>
		<RootNamespace>Norse.$(MSBuildProjectName)</RootNamespace>
		<TargetFramework>net11.0</TargetFramework>
		<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
		<WarningLevel>9999</WarningLevel>
	</PropertyGroup>
</Project>
```

- [ ] **Step 4: Write `Asgard/src/Directory.Build.props`**

```xml
<Project>
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
	<ItemGroup>
		<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
			<_Parameter1>$(AssemblyName).Tests</_Parameter1>
		</AssemblyAttribute>
	</ItemGroup>
</Project>
```

- [ ] **Step 5: Write `Asgard/tests/Directory.Build.props`**

```xml
<Project>
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
	<PropertyGroup>
		<IsPackable>false</IsPackable>
		<IsTestProject>true</IsTestProject>
		<NoWarn>$(NoWarn);CA1812;CA1859;CS1591;IDE0051</NoWarn>
		<OutputType>Exe</OutputType>
		<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.Testing.Platform.MSBuild" Version="2.*" />
		<PackageReference Include="Shouldly" Version="4.*" />
		<PackageReference Include="xunit.v3.mtp-v2" Version="3.*" />
		<PackageReference Include="NSubstitute" Version="5.*" />
		<Using Include="Shouldly" />
		<Using Include="Xunit" />
	</ItemGroup>
</Project>
```

- [ ] **Step 6: Write `Asgard/Asgard.slnx`**

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
		<File Path="src/Directory.Build.props" />
		<Project Path="src/Abstractions.Egress/Abstractions.Egress.csproj" />
	</Folder>
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<Project Path="tests/Abstractions.Egress.Tests/Abstractions.Egress.Tests.csproj" />
	</Folder>
</Solution>
```

- [ ] **Step 7: Write `Asgard/src/Abstractions.Egress/Abstractions.Egress.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse Abstractions' egress contracts: the HttpResult&lt;T&gt; discriminated union, the closed EgressError/FailureKind vocabulary, the EgressClassifier response-classification seam, and the IHttpEgress/IResponseParser&lt;T&gt; injection surfaces for outbound calls to external/third-party APIs.</Description>
		<IsAotCompatible>true</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../../../Svartalfheim/src/Primitives/Primitives.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 8: Write `Asgard/tests/Abstractions.Egress.Tests/Abstractions.Egress.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Abstractions.Egress/Abstractions.Egress.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 9: Write the failing tests**

`Asgard/tests/Abstractions.Egress.Tests/FailureKindTests.cs`:

```csharp
namespace Norse.Abstractions.Egress.Tests;

public sealed class FailureKindTests
{
	[Theory]
	[InlineData(FailureKind.Unspecified, 0)]
	[InlineData(FailureKind.Transport, 1)]
	[InlineData(FailureKind.Status, 2)]
	[InlineData(FailureKind.EmptyBody, 3)]
	[InlineData(FailureKind.Parse, 4)]
	void Should_pin_explicit_underlying_value_when_cast_to_int(FailureKind kind, int expected)
	{
		((int)kind).ShouldBe(expected);
	}
}
```

`Asgard/tests/Abstractions.Egress.Tests/EgressErrorTests.cs`:

```csharp
using System.Net;

namespace Norse.Abstractions.Egress.Tests;

public sealed class EgressErrorTests
{
	[Fact]
	void Should_expose_constructor_arguments_when_constructed_with_a_status_code()
	{
		var error = new EgressError(FailureKind.Status, HttpStatusCode.BadGateway, "raw body snippet");

		error.Kind.ShouldBe(FailureKind.Status);
		error.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
		error.RawBody.ShouldBe("raw body snippet");
	}

	[Fact]
	void Should_allow_a_null_status_code_when_the_failure_is_transport_level()
	{
		var error = new EgressError(FailureKind.Transport, null, "");

		error.StatusCode.ShouldBeNull();
	}
}
```

- [ ] **Step 10: Run the tests to verify they fail**

Run: `dotnet test Asgard/tests/Abstractions.Egress.Tests -- --filter-class "*.FailureKindTests|*.EgressErrorTests"`
Expected: compile error — `FailureKind` and `EgressError` do not exist yet.

- [ ] **Step 11: Implement `FailureKind` and `EgressError`**

`Asgard/src/Abstractions.Egress/FailureKind.cs`:

```csharp
namespace Norse.Abstractions.Egress;

/// <summary>
/// The closed set of reasons an egress HTTP call can fail. Adding a member is a
/// deliberate breaking change: every exhaustive switch over this enum becomes a
/// build error until updated.
/// </summary>
public enum FailureKind
{
	/// <summary>Sentinel CLR default — never a real state.</summary>
	Unspecified = 0,

	/// <summary>Resilience exhausted: network failure or timeout.</summary>
	Transport = 1,

	/// <summary>A non-success, non-not-found HTTP status.</summary>
	Status = 2,

	/// <summary>A 2xx response with no body to parse.</summary>
	EmptyBody = 3,

	/// <summary>The body arrived but the parser rejected it.</summary>
	Parse = 4,
}
```

`Asgard/src/Abstractions.Egress/EgressError.cs`:

```csharp
using System.Net;

namespace Norse.Abstractions.Egress;

/// <summary>The failure detail carried by the <see cref="Failure"/> case of <see cref="HttpResult{T}"/>.</summary>
/// <param name="Kind">The closed-set failure reason.</param>
/// <param name="StatusCode">The response status, when one was received; null for a transport failure.</param>
/// <param name="RawBody">
/// The captured body: full for <see cref="FailureKind.Parse"/> (the drift-corpus seed), a bounded
/// diagnostic snippet for <see cref="FailureKind.Status"/>, empty for <see cref="FailureKind.Transport"/>
/// and <see cref="FailureKind.EmptyBody"/>.
/// </param>
public readonly record struct EgressError(
	FailureKind Kind,
	HttpStatusCode? StatusCode,
	string RawBody);
```

- [ ] **Step 12: Run the tests to verify they pass**

Run: `dotnet test Asgard/tests/Abstractions.Egress.Tests -- --filter-class "*.FailureKindTests|*.EgressErrorTests"`
Expected: PASS (7 tests).

- [ ] **Step 13: Commit**

```bash
git -C Asgard add .editorconfig .gitattributes .gitignore Directory.Build.props global.json Asgard.slnx src tests
git -C Asgard commit -m "feat: scaffold Asgard and the egress failure vocabulary"
```

---

### Task 2: `HttpResult<T>` — the three-case union

**Files:**
- Create: `Asgard/src/Abstractions.Egress/Found{T}.cs`
- Create: `Asgard/src/Abstractions.Egress/NotFound.cs`
- Create: `Asgard/src/Abstractions.Egress/Failure.cs`
- Create: `Asgard/src/Abstractions.Egress/HttpResult{T}.cs`
- Test: `Asgard/tests/Abstractions.Egress.Tests/HttpResultTests.cs`

**Interfaces:**
- Consumes: `EgressError` (Task 1).
- Produces: `readonly record struct Found<T>(T Value) where T : notnull`; `readonly record struct NotFound`; `readonly record struct Failure(EgressError Error)`; `[MustConsume] readonly record struct HttpResult<T> : IUnion where T : notnull` with `IsFound`/`IsNotFound`/`IsFailure`, three `TryGetValue` overloads, and `Match<TResult>(Func<T,TResult> onFound, Func<TResult> onNotFound, Func<EgressError,TResult> onFailure)`. Every later task constructs `HttpResult<T>` via implicit conversion from these three case types (e.g. `HttpResult<string> r = new Found<string>("x");`) — this is the proven Svartalfheim pattern (`Svartalfheim/tests/Primitives.Tests/ResultTests.cs` line 13's comment: "Assignment without an explicit constructor call IS the implicit union conversion"), not a guess.

- [ ] **Step 1: Write the failing tests**

`Asgard/tests/Abstractions.Egress.Tests/HttpResultTests.cs`:

```csharp
using System.Net;

namespace Norse.Abstractions.Egress.Tests;

public sealed class HttpResultTests
{
	[Fact]
	void Should_report_IsFound_when_constructed_from_Found()
	{
		HttpResult<string> result = new Found<string>("value");

		result.IsFound.ShouldBeTrue();
		result.IsNotFound.ShouldBeFalse();
		result.IsFailure.ShouldBeFalse();
	}

	[Fact]
	void Should_report_IsNotFound_when_constructed_from_NotFound()
	{
		HttpResult<string> result = new NotFound();

		result.IsNotFound.ShouldBeTrue();
		result.IsFound.ShouldBeFalse();
		result.IsFailure.ShouldBeFalse();
	}

	[Fact]
	void Should_report_IsFailure_when_constructed_from_Failure()
	{
		var error = new EgressError(FailureKind.Status, HttpStatusCode.NotFound, "");
		HttpResult<string> result = new Failure(error);

		result.IsFailure.ShouldBeTrue();
		result.IsFound.ShouldBeFalse();
		result.IsNotFound.ShouldBeFalse();
	}

	[Fact]
	void Should_invoke_onFound_when_matching_a_Found_result()
	{
		HttpResult<string> result = new Found<string>("value");

		var matched = result.Match(
			onFound: value => $"found:{value}",
			onNotFound: () => "not-found",
			onFailure: error => $"failure:{error.Kind}");

		matched.ShouldBe("found:value");
	}

	[Fact]
	void Should_invoke_onNotFound_when_matching_a_NotFound_result()
	{
		HttpResult<string> result = new NotFound();

		var matched = result.Match(
			onFound: value => $"found:{value}",
			onNotFound: () => "not-found",
			onFailure: error => $"failure:{error.Kind}");

		matched.ShouldBe("not-found");
	}

	[Fact]
	void Should_invoke_onFailure_when_matching_a_Failure_result()
	{
		var error = new EgressError(FailureKind.Transport, null, "");
		HttpResult<string> result = new Failure(error);

		var matched = result.Match(
			onFound: value => $"found:{value}",
			onNotFound: () => "not-found",
			onFailure: error => $"failure:{error.Kind}");

		matched.ShouldBe("failure:Transport");
	}

	[Fact]
	void Should_render_NotFound_in_ToString_when_constructed_from_NotFound()
	{
		HttpResult<string> result = new NotFound();

		result.ToString().ShouldBe("NotFound");
	}

	[Fact]
	void Should_have_null_value_when_defaulted()
	{
		var result = default(HttpResult<string>);

		result.Value.ShouldBeNull();
		result.HasValue.ShouldBeFalse();
		result.TryGetValue(out Found<string> _).ShouldBeFalse();
		result.TryGetValue(out NotFound _).ShouldBeFalse();
		result.TryGetValue(out Failure _).ShouldBeFalse();
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Asgard/tests/Abstractions.Egress.Tests -- --filter-class "*.HttpResultTests"`
Expected: compile error — `HttpResult<T>`, `Found<T>`, `NotFound` do not exist yet.

- [ ] **Step 3: Implement the case types**

`Asgard/src/Abstractions.Egress/Found{T}.cs`:

```csharp
namespace Norse.Abstractions.Egress;

/// <summary>The found case of <see cref="HttpResult{T}"/>: a 2xx response whose body parsed.</summary>
/// <typeparam name="T">The parsed value's type. Non-nullable by construction.</typeparam>
/// <param name="Value">The parsed value.</param>
public readonly record struct Found<T>(T Value) where T : notnull;
```

`Asgard/src/Abstractions.Egress/NotFound.cs`:

```csharp
namespace Norse.Abstractions.Egress;

/// <summary>
/// The not-found case of <see cref="HttpResult{T}"/>: the per-client <see cref="EgressClassifier"/>
/// mapped the response to "not there" — never a thrown exception, never a null.
/// </summary>
public readonly record struct NotFound;
```

`Asgard/src/Abstractions.Egress/Failure.cs`:

```csharp
namespace Norse.Abstractions.Egress;

/// <summary>The failure case of <see cref="HttpResult{T}"/>: everything that is not Found or NotFound.</summary>
/// <param name="Error">The error detail.</param>
public readonly record struct Failure(EgressError Error);
```

- [ ] **Step 4: Implement `HttpResult<T>`**

`Asgard/src/Abstractions.Egress/HttpResult{T}.cs`:

```csharp
using System.Runtime.CompilerServices;
using Norse.Primitives;

namespace Norse.Abstractions.Egress;

/// <summary>
/// The outcome of an egress HTTP call: exactly one of <see cref="Found{T}"/>,
/// <see cref="NotFound"/>, or <see cref="Failure"/>, as a native C# union.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern matching unwraps to the case types.</b> Match against
/// <see cref="Found{T}"/>, <see cref="NotFound"/>, or <see cref="Failure"/> — never against
/// <c>HttpResult&lt;T&gt;</c> itself; the compiler rejects <c>result is HttpResult&lt;T&gt;</c> (CS8121).
/// A three-arm switch over all three case types is exhaustive.
/// </para>
/// <para>
/// <b>Do not use <c>default(HttpResult&lt;T&gt;)</c> or <c>new HttpResult&lt;T&gt;()</c>.</b>
/// A defaulted value is malformed by construction: it matches none of the three cases, and an
/// exhaustive switch throws <see cref="SwitchExpressionException"/> at first consumption.
/// </para>
/// <para>
/// This type hand-implements the union pattern (mirroring <c>Result&lt;T&gt;</c> in
/// <c>Norse.Primitives</c>) so all three cases are stored inline and nothing boxes on any path.
/// </para>
/// </remarks>
/// <typeparam name="T">The parsed value's type. Non-nullable by construction.</typeparam>
[MustConsume]
[Union]
public readonly record struct HttpResult<T> : IUnion where T : notnull
{
	enum State : byte
	{
		Default = 0,
		Found = 1,
		NotFound = 2,
		Failure = 3,
	}

	readonly Found<T> _found;
	readonly NotFound _notFound;
	readonly Failure _failure;
	readonly State _state;

	/// <summary>Creates a found result. Also reachable as an implicit union conversion.</summary>
	/// <param name="value">The found case.</param>
	public HttpResult(Found<T> value)
	{
		_found = value;
		_state = State.Found;
	}

	/// <summary>Creates a not-found result. Also reachable as an implicit union conversion.</summary>
	/// <param name="value">The not-found case.</param>
	public HttpResult(NotFound value)
	{
		_notFound = value;
		_state = State.NotFound;
	}

	/// <summary>Creates a failure result. Also reachable as an implicit union conversion.</summary>
	/// <param name="value">The failure case.</param>
	public HttpResult(Failure value)
	{
		_failure = value;
		_state = State.Failure;
	}

	/// <summary>The boxed case contents, or <see langword="null"/> for a defaulted value.</summary>
	public object? Value =>
		_state switch
		{
			State.Found => _found,
			State.NotFound => _notFound,
			State.Failure => _failure,
			_ => null,
		};

	/// <summary><see langword="true"/> unless this value was defaulted rather than constructed.</summary>
	public bool HasValue =>
		_state != State.Default;

	/// <summary><see langword="true"/> when this is the <see cref="Found{T}"/> case.</summary>
	public bool IsFound =>
		_state == State.Found;

	/// <summary><see langword="true"/> when this is the <see cref="NotFound"/> case.</summary>
	public bool IsNotFound =>
		_state == State.NotFound;

	/// <summary><see langword="true"/> when this is the <see cref="Failure"/> case.</summary>
	public bool IsFailure =>
		_state == State.Failure;

	/// <summary>Retrieves the found case without boxing.</summary>
	public bool TryGetValue(out Found<T> value)
	{
		value = _found;
		return _state == State.Found;
	}

	/// <summary>Retrieves the not-found case without boxing.</summary>
	public bool TryGetValue(out NotFound value)
	{
		value = _notFound;
		return _state == State.NotFound;
	}

	/// <summary>Retrieves the failure case without boxing.</summary>
	public bool TryGetValue(out Failure value)
	{
		value = _failure;
		return _state == State.Failure;
	}

	/// <summary>Consumes the result by handling all three cases.</summary>
	/// <typeparam name="TResult">The handlers' common return type.</typeparam>
	/// <param name="onFound">The found-case handler.</param>
	/// <param name="onNotFound">The not-found-case handler.</param>
	/// <param name="onFailure">The failure-case handler.</param>
	/// <exception cref="ArgumentNullException">Any handler is null.</exception>
	/// <exception cref="SwitchExpressionException">This value was defaulted rather than constructed.</exception>
	public TResult Match<TResult>(Func<T, TResult> onFound, Func<TResult> onNotFound, Func<EgressError, TResult> onFailure)
	{
		ArgumentNullException.ThrowIfNull(onFound);
		ArgumentNullException.ThrowIfNull(onNotFound);
		ArgumentNullException.ThrowIfNull(onFailure);
		return this switch
		{
			Found<T>(var value) => onFound(value),
			NotFound => onNotFound(),
			Failure(var error) => onFailure(error),
		};
	}

	/// <summary>Renders "Found(value)", "NotFound", "Failure(Kind)", or "Default(invalid)".</summary>
	public override string ToString() =>
		_state switch
		{
			State.Found => $"Found({_found.Value})",
			State.NotFound => "NotFound",
			State.Failure => $"Failure({_failure.Error.Kind})",
			_ => "Default(invalid)",
		};
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Asgard/tests/Abstractions.Egress.Tests -- --filter-class "*.HttpResultTests"`
Expected: PASS (8 tests). If the compiler rejects `[Union]`/`IUnion` on a 3-case struct, that is new information the union language feature doesn't support — stop and report back rather than working around it; this is the first 3-case union on the platform (Svartalfheim's `Result<T>` only proved 2 cases).

- [ ] **Step 6: Commit**

```bash
git -C Asgard add src/Abstractions.Egress/Found{T}.cs src/Abstractions.Egress/NotFound.cs src/Abstractions.Egress/Failure.cs "src/Abstractions.Egress/HttpResult{T}.cs" tests/Abstractions.Egress.Tests/HttpResultTests.cs
git -C Asgard commit -m "feat: add the HttpResult<T> three-case union"
```

---

### Task 3: Response classification — `ResponseDisposition`, `EgressClassifier`, `Classify`

**Files:**
- Create: `Asgard/src/Abstractions.Egress/ResponseDisposition.cs`
- Create: `Asgard/src/Abstractions.Egress/EgressClassifier.cs`
- Create: `Asgard/src/Abstractions.Egress/Classify.cs`
- Test: `Asgard/tests/Abstractions.Egress.Tests/ClassifyTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (this triad is self-contained — it classifies a raw `HttpResponseMessage`, before any parsing).
- Produces: `enum ResponseDisposition { Unspecified = 0, Success = 1, NotFound = 2, Transient = 3, Permanent = 4 }`; `delegate ResponseDisposition EgressClassifier(HttpResponseMessage response)`; `static class Classify` with `ResponseDisposition Default(HttpResponseMessage response)` and `EgressClassifier NotFoundOnStatus(params HttpStatusCode[] statuses)`. Task 6's verification references `Classify.Default` and `Classify.NotFoundOnStatus` as the public surface; no other task depends on this one.

- [ ] **Step 1: Write the failing tests**

`Asgard/tests/Abstractions.Egress.Tests/ClassifyTests.cs`:

```csharp
using System.Net;

namespace Norse.Abstractions.Egress.Tests;

public sealed class ClassifyTests
{
	[Theory]
	[InlineData(HttpStatusCode.OK, ResponseDisposition.Success)]
	[InlineData(HttpStatusCode.Created, ResponseDisposition.Success)]
	[InlineData(HttpStatusCode.NotFound, ResponseDisposition.NotFound)]
	[InlineData(HttpStatusCode.Gone, ResponseDisposition.NotFound)]
	[InlineData(HttpStatusCode.RequestTimeout, ResponseDisposition.Transient)]
	[InlineData(HttpStatusCode.TooManyRequests, ResponseDisposition.Transient)]
	[InlineData(HttpStatusCode.InternalServerError, ResponseDisposition.Transient)]
	[InlineData(HttpStatusCode.BadGateway, ResponseDisposition.Transient)]
	[InlineData(HttpStatusCode.BadRequest, ResponseDisposition.Permanent)]
	[InlineData(HttpStatusCode.Unauthorized, ResponseDisposition.Permanent)]
	void Should_classify_status_per_the_default_table(HttpStatusCode status, ResponseDisposition expected)
	{
		using var response = new HttpResponseMessage(status);

		Classify.Default(response).ShouldBe(expected);
	}

	[Fact]
	void Should_throw_when_response_is_null()
	{
		Should.Throw<ArgumentNullException>(() => Classify.Default(null!));
	}

	[Fact]
	void Should_remap_the_named_status_to_NotFound_when_using_NotFoundOnStatus()
	{
		// Nexsure: a 500 means "the record isn't there", not an outage.
		var classifier = Classify.NotFoundOnStatus(HttpStatusCode.InternalServerError);
		using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);

		classifier(response).ShouldBe(ResponseDisposition.NotFound);
	}

	[Fact]
	void Should_fall_back_to_the_default_classifier_when_the_status_is_not_remapped()
	{
		var classifier = Classify.NotFoundOnStatus(HttpStatusCode.InternalServerError);
		using var response = new HttpResponseMessage(HttpStatusCode.BadGateway);

		classifier(response).ShouldBe(ResponseDisposition.Transient);
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Asgard/tests/Abstractions.Egress.Tests -- --filter-class "*.ClassifyTests"`
Expected: compile error — `ResponseDisposition` and `Classify` do not exist yet.

- [ ] **Step 3: Implement `ResponseDisposition`, `EgressClassifier`, and `Classify`**

`Asgard/src/Abstractions.Egress/ResponseDisposition.cs`:

```csharp
namespace Norse.Abstractions.Egress;

/// <summary>
/// The disposition an <see cref="EgressClassifier"/> assigns to a raw HTTP response,
/// before any parsing happens. Drives both the <see cref="HttpResult{T}"/> case and
/// whether the resilience pipeline retries.
/// </summary>
public enum ResponseDisposition
{
	/// <summary>Sentinel CLR default — never returned by a real classifier.</summary>
	Unspecified = 0,

	/// <summary>Proceed to parse: <see cref="Found{T}"/>, or <see cref="FailureKind.EmptyBody"/> if the body is empty.</summary>
	Success = 1,

	/// <summary>The partner's way of saying "not there" — maps to <see cref="NotFound"/>. No parse, no retry.</summary>
	NotFound = 2,

	/// <summary>Retryable. If the resilience pipeline exhausts its retries, maps to <see cref="FailureKind.Transport"/>.</summary>
	Transient = 3,

	/// <summary>Terminal. Maps to <see cref="FailureKind.Status"/>. Never retried.</summary>
	Permanent = 4,
}
```

`Asgard/src/Abstractions.Egress/EgressClassifier.cs`:

```csharp
namespace Norse.Abstractions.Egress;

/// <summary>
/// Maps a raw HTTP response to a <see cref="ResponseDisposition"/> before the body is parsed —
/// the single per-client seam where a partner's status-code quirks are declared once and drive
/// both the <see cref="HttpResult{T}"/> case and resilience-retry eligibility.
/// </summary>
/// <param name="response">The raw response. Never null.</param>
public delegate ResponseDisposition EgressClassifier(HttpResponseMessage response);
```

`Asgard/src/Abstractions.Egress/Classify.cs`:

```csharp
using System.Net;

namespace Norse.Abstractions.Egress;

/// <summary>The default response classifier and the factory for per-partner overrides.</summary>
public static class Classify
{
	/// <summary>
	/// The well-behaved-partner classifier: 2xx maps to <see cref="ResponseDisposition.Success"/>;
	/// 404/410 to <see cref="ResponseDisposition.NotFound"/>; 408/429/5xx to <see cref="ResponseDisposition.Transient"/>;
	/// every other status to <see cref="ResponseDisposition.Permanent"/>.
	/// </summary>
	/// <param name="response">The raw response.</param>
	/// <exception cref="ArgumentNullException"><paramref name="response"/> is null.</exception>
	public static ResponseDisposition Default(HttpResponseMessage response)
	{
		ArgumentNullException.ThrowIfNull(response);
		var status = (int)response.StatusCode;
		return status switch
		{
			>= 200 and < 300 => ResponseDisposition.Success,
			(int)HttpStatusCode.NotFound or (int)HttpStatusCode.Gone => ResponseDisposition.NotFound,
			(int)HttpStatusCode.RequestTimeout or (int)HttpStatusCode.TooManyRequests => ResponseDisposition.Transient,
			>= 500 and < 600 => ResponseDisposition.Transient,
			_ => ResponseDisposition.Permanent,
		};
	}

	/// <summary>
	/// Returns <see cref="Default"/> with the named statuses remapped to <see cref="ResponseDisposition.NotFound"/> —
	/// the seam a partner's status-code quirk (e.g. a 500 that means "not there") is declared through.
	/// </summary>
	/// <param name="statuses">The statuses this partner overloads to mean "not found".</param>
	/// <exception cref="ArgumentNullException"><paramref name="statuses"/> is null.</exception>
	public static EgressClassifier NotFoundOnStatus(params HttpStatusCode[] statuses)
	{
		ArgumentNullException.ThrowIfNull(statuses);
		return response =>
			Array.IndexOf(statuses, response.StatusCode) >= 0
				? ResponseDisposition.NotFound
				: Default(response);
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Asgard/tests/Abstractions.Egress.Tests -- --filter-class "*.ClassifyTests"`
Expected: PASS (13 tests — 10 theory cases + 3 facts).

- [ ] **Step 5: Commit**

```bash
git -C Asgard add src/Abstractions.Egress/ResponseDisposition.cs src/Abstractions.Egress/EgressClassifier.cs src/Abstractions.Egress/Classify.cs tests/Abstractions.Egress.Tests/ClassifyTests.cs
git -C Asgard commit -m "feat: add response classification (ResponseDisposition, EgressClassifier, Classify)"
```

---

### Task 4: `IResponseParser<T>` — the stateful parser shape

**Files:**
- Create: `Asgard/src/Abstractions.Egress/IResponseParser{T}.cs`
- Test: `Asgard/tests/Abstractions.Egress.Tests/IResponseParserTests.cs`

**Interfaces:**
- Consumes: `Result<T>` (`Norse.Primitives`, via the existing `ProjectReference`).
- Produces: `interface IResponseParser<T> where T : notnull { Result<T> Parse(ReadOnlySpan<byte> body); }`. Task 5's `IHttpEgress.GetAsync<T>(string, IResponseParser<T>, CancellationToken)` overload consumes this exact interface.

- [ ] **Step 1: Write the failing test**

`Asgard/tests/Abstractions.Egress.Tests/IResponseParserTests.cs`:

```csharp
using System.Text;
using Norse.Primitives;

namespace Norse.Abstractions.Egress.Tests;

public sealed class IResponseParserTests
{
	sealed class UppercaseAsciiParser : IResponseParser<string>
	{
		public Result<string> Parse(ReadOnlySpan<byte> body) =>
			new Success<string>(Encoding.ASCII.GetString(body).ToUpperInvariant());
	}

	[Fact]
	void Should_parse_the_body_when_implemented_directly()
	{
		IResponseParser<string> parser = new UppercaseAsciiParser();

		var result = parser.Parse("abc"u8);

		result.TryGetValue(out Success<string> success).ShouldBeTrue();
		success.Value.ShouldBe("ABC");
	}
}
```

*(NSubstitute cannot proxy `Parse`'s `ReadOnlySpan<byte>` ref-struct parameter — see Global Constraints. A real fake implementation is the only way to exercise this interface, and it is sufficient: the test proves the contract shape compiles and behaves.)*

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Asgard/tests/Abstractions.Egress.Tests -- --filter-class "*.IResponseParserTests"`
Expected: compile error — `IResponseParser<T>` does not exist yet.

- [ ] **Step 3: Implement `IResponseParser<T>`**

`Asgard/src/Abstractions.Egress/IResponseParser{T}.cs`:

```csharp
using Norse.Primitives;

namespace Norse.Abstractions.Egress;

/// <summary>
/// Parser shape 4 — a full interface for stateful or configurable response-body parsers
/// (e.g. one carrying a configured <c>XmlReaderSettings</c>), where the delegate shapes
/// (<c>Func&lt;ReadOnlySpan&lt;byte&gt;, T&gt;</c>, <c>Func&lt;ReadOnlySpan&lt;char&gt;, T&gt;</c>,
/// <c>Func&lt;string, T&gt;</c>) are too thin.
/// </summary>
/// <typeparam name="T">The parsed value's type. Non-nullable by construction.</typeparam>
public interface IResponseParser<T> where T : notnull
{
	/// <summary>Parses the response body. A rejected body is a <see cref="Result{T}"/> failure, never a thrown exception.</summary>
	/// <param name="body">The raw response body bytes.</param>
	Result<T> Parse(ReadOnlySpan<byte> body);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test Asgard/tests/Abstractions.Egress.Tests -- --filter-class "*.IResponseParserTests"`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git -C Asgard add "src/Abstractions.Egress/IResponseParser{T}.cs" tests/Abstractions.Egress.Tests/IResponseParserTests.cs
git -C Asgard commit -m "feat: add the IResponseParser<T> stateful parser shape"
```

---

### Task 5: `IHttpEgress` — the injection surface

**Files:**
- Create: `Asgard/src/Abstractions.Egress/IHttpEgress.cs`
- Test: `Asgard/tests/Abstractions.Egress.Tests/IHttpEgressTests.cs`

**Interfaces:**
- Consumes: `HttpResult<T>` (Task 2), `IResponseParser<T>` (Task 4).
- Produces: `interface IHttpEgress` with `GetAsync<T>(string path, CancellationToken ct = default)`, `PostAsync<TResponse>(string path, object body, CancellationToken ct = default)`, and `GetAsync<T>(string path, IResponseParser<T> parser, CancellationToken ct = default)`. This is the final type in the Abstractions slice — Infrastructure's future `HttpEgress` facade implements this interface directly.

- [ ] **Step 1: Write the failing tests**

`Asgard/tests/Abstractions.Egress.Tests/IHttpEgressTests.cs`:

```csharp
using NSubstitute;

namespace Norse.Abstractions.Egress.Tests;

public sealed class IHttpEgressTests
{
	[Fact]
	async Task Should_return_a_Found_result_when_the_substitute_is_configured_to_succeed()
	{
		var egress = Substitute.For<IHttpEgress>();
		HttpResult<string> expected = new Found<string>("value");
		egress.GetAsync<string>("nexsure/branches/1").Returns(Task.FromResult(expected));

		var result = await egress.GetAsync<string>("nexsure/branches/1");

		result.IsFound.ShouldBeTrue();
	}

	[Fact]
	async Task Should_accept_a_per_call_parser_override()
	{
		var egress = Substitute.For<IHttpEgress>();
		var parser = Substitute.For<IResponseParser<string>>();
		HttpResult<string> expected = new NotFound();
		egress.GetAsync("nexsure/branches/1", parser).Returns(Task.FromResult(expected));

		var result = await egress.GetAsync("nexsure/branches/1", parser);

		result.IsNotFound.ShouldBeTrue();
	}

	[Fact]
	async Task Should_post_a_body_and_return_a_typed_result()
	{
		var egress = Substitute.For<IHttpEgress>();
		HttpResult<string> expected = new Found<string>("created");
		egress.PostAsync<string>("nexsure/branches", Arg.Any<object>()).Returns(Task.FromResult(expected));

		var result = await egress.PostAsync<string>("nexsure/branches", new { Name = "new branch" });

		result.IsFound.ShouldBeTrue();
	}
}
```

*(`Substitute.For<IResponseParser<string>>()` here is never invoked — it is passed only as an opaque argument to `IHttpEgress.GetAsync`, which has no ref-struct parameters. Constructing the substitute itself is fine; calling its `Parse` method would not be — see Global Constraints.)*

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Asgard/tests/Abstractions.Egress.Tests -- --filter-class "*.IHttpEgressTests"`
Expected: compile error — `IHttpEgress` does not exist yet.

- [ ] **Step 3: Implement `IHttpEgress`**

`Asgard/src/Abstractions.Egress/IHttpEgress.cs`:

```csharp
namespace Norse.Abstractions.Egress;

/// <summary>
/// The injection surface for egress calls to a single external/third-party API —
/// the platform's only sanctioned way to talk HTTP to the outside world.
/// </summary>
public interface IHttpEgress
{
	/// <summary>Calls the named API's parser-registered shape for a GET.</summary>
	/// <typeparam name="T">The expected response value's type. Non-nullable by construction.</typeparam>
	/// <param name="path">The request path, relative to the registered base address.</param>
	/// <param name="ct">The cancellation token.</param>
	Task<HttpResult<T>> GetAsync<T>(string path, CancellationToken ct = default) where T : notnull;

	/// <summary>Calls the named API's parser-registered shape for a POST.</summary>
	/// <typeparam name="TResponse">The expected response value's type. Non-nullable by construction.</typeparam>
	/// <param name="path">The request path, relative to the registered base address.</param>
	/// <param name="body">The request body, serialized per the registered API's conventions.</param>
	/// <param name="ct">The cancellation token.</param>
	Task<HttpResult<TResponse>> PostAsync<TResponse>(string path, object body, CancellationToken ct = default) where TResponse : notnull;

	/// <summary>Calls a GET with a per-call parser override, for partners whose endpoints are internally inconsistent.</summary>
	/// <typeparam name="T">The expected response value's type. Non-nullable by construction.</typeparam>
	/// <param name="path">The request path, relative to the registered base address.</param>
	/// <param name="parser">The parser to use for this call only.</param>
	/// <param name="ct">The cancellation token.</param>
	Task<HttpResult<T>> GetAsync<T>(string path, IResponseParser<T> parser, CancellationToken ct = default) where T : notnull;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Asgard/tests/Abstractions.Egress.Tests -- --filter-class "*.IHttpEgressTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git -C Asgard add src/Abstractions.Egress/IHttpEgress.cs tests/Abstractions.Egress.Tests/IHttpEgressTests.cs
git -C Asgard commit -m "feat: add the IHttpEgress injection surface"
```

---

### Task 6: Solution-wide verification and documentation sync

**Files:**
- Modify: `Asgard/README.md`, `Asgard/CLAUDE.md` (boy-scout law — Bifrost `CLAUDE.md` §6)
- Modify: `Glitnir/docs/Platform/specs/2026-06-07-egress-http-resilience-parsing-design.md` (status line)

**Interfaces:**
- Consumes: nothing new — this task verifies and documents Tasks 1–5's combined output.
- Produces: nothing new — no later task depends on this one.

- [ ] **Step 1: Build and test the whole solution**

Run: `dotnet build Asgard/Asgard.slnx`
Expected: 0 warnings, 0 errors (warnings are errors platform-wide).

Run: `dotnet test Asgard/tests/Abstractions.Egress.Tests`
Expected: all tests from Tasks 1–5 PASS (33 tests total: 5 + 8 + 13 + 1 + 3 + 3 — recount against the actual suite and treat any drift as a signal something above was miscounted, not as acceptable).

- [ ] **Step 2: Update `Asgard/README.md`**

Replace the bare-shell description with the actual contents: `Norse.Abstractions.Egress` — `HttpResult<T>`, `EgressError`/`FailureKind`, `ResponseDisposition`/`EgressClassifier`/`Classify`, `IResponseParser<T>`, `IHttpEgress`. Link to the source spec (`../Glitnir/docs/Platform/specs/2026-06-07-egress-http-resilience-parsing-design.md`) and this plan (`../Glitnir/docs/Asgard/plans/2026-06-19-asgard-egress-contracts.md`). Read the current file first — it documents Asgard as a bare shell; update that paragraph in place rather than appending.

- [ ] **Step 3: Update `Asgard/CLAUDE.md`**

§1 currently reads: "This repo is currently a bare shell (LICENSE only) — no specs have converged here yet." Replace with a statement that `Norse.Abstractions.Egress` is built, naming the source spec and this plan by relative path, and correct the "Asgard rides on nothing" sentence to reflect the real dependency on `Norse.Primitives` (per this plan's Global Constraints note) — don't leave the contradiction standing once the code proves which side of it is true.

- [ ] **Step 4: Update the spec's status line**

In `Glitnir/docs/Platform/specs/2026-06-07-egress-http-resilience-parsing-design.md`, line 4 reads `**Status:** Spec — awaiting plan greenlight`. Change to record that the Abstractions slice has a plan and landed; the Infrastructure (Midgard) and Hosting (Yggdrasil) slices remain to be planned separately.

- [ ] **Step 5: Stage and stop**

```bash
git -C Asgard status
git -C Glitnir status
```

Show both diffs to the human. Per Bifrost `CLAUDE.md` §6: no automatic commits beyond what Tasks 1–5 already made per-task; this final documentation change is staged, not committed, pending human review.
