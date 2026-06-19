# Svartalfheim Primitives Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the foundational `Norse.Primitives` primitives — `Result<T>`, the `Error` union, the `Parser` gateway over `ISpanParsable<T>`, the composition surface (sync + async + Present + aggregation), and the `[MustConsume]` attribute — backed by a full unit + property + AOT-smoke + benchmark test suite.

**Architecture:** Single library assembly + three test/benchmark projects in a sibling directory under the meta-repo. Uses native C# 15 unions on .NET 11 preview. Tier policy in the meta-repo's `Directory.Build.props` controls `runtime-async` per project. `[MustConsume]` ships as an attribute here; the `YGG201` diagnostic lives in `norse-primitives-architecture` (separate plan).

**Tech Stack:** .NET 11 preview / C# 15 (native unions, runtime-async-V2), xUnit, Shouldly, NSubstitute, FsCheck, BenchmarkDotNet.

**Companion spec:** `docs/Svartalfheim/specs/2026-05-20-svartalfheim-primitives-design.md`. Read it before starting; every design decision is justified there.

**Out of scope (separate plans / specs):**
- `YGG201` diagnostic implementation in `norse-primitives-architecture` (this plan defines the attribute; the analyzer ships elsewhere).
- Migration from a `./Norse.Primitives/` subdirectory in the meta-repo to a true git submodule with its own remote — mechanical git work after a remote exists.
- `Money` and the UUID v5 namespace registry — their own future brainstorming sessions.

**Per CLAUDE.md §8:** *No automatic git commits.* Every "Stage" step ends with `git add` only. The human runs `git commit` after reviewing the diff. Each task includes a proposed commit message for that review.

---

## File Structure

All paths relative to the meta-repo root.

```
Directory.Build.props                                # NEW: meta-repo tier policy
Norse.Primitives/                                        # subdirectory (future submodule)
├── .editorconfig                                    # NEW: tabs, 2-space width
├── .gitignore                                       # NEW: dotnet defaults
├── Directory.Build.props                            # NEW: Norse.Primitives-local overrides
├── LICENSE                                          # NEW: MIT
├── README.md                                        # NEW: usage + F# interop notes
├── Norse.Primitives.slnx                                # NEW: solution
├── src/
│   └── Norse.Primitives/
│       ├── Norse.Primitives.csproj                      # NEW
│       ├── MustConsumeAttribute.cs                  # NEW
│       ├── Result.cs                                # NEW: Result<T>, Success<T>, Failure
│       ├── Error.cs                                 # NEW: Error union
│       ├── Errors/                                  # NEW: case-type folder
│       │   ├── ParseError.cs
│       │   ├── ValidationError.cs
│       │   ├── NotFoundError.cs
│       │   ├── UnauthorizedError.cs
│       │   ├── ConflictError.cs
│       │   └── AggregateError.cs
│       ├── Parser.cs                                # NEW: gateway
│       ├── ResultFailureException.cs                # NEW: thrown by GetValueOrThrow
│       └── ResultExtensions/                        # NEW: composition extensions
│           ├── Sync.cs                              # Map, MapError, Bind, Match, GetValueOr*, IsSuccess, IsFailure
│           ├── Async.cs                             # MapAsync, BindAsync, MatchAsync
│           ├── Present.cs                           # MapPresent, BindPresent, MatchPresent
│           └── Aggregate.cs                         # Combine, Collect, tuple combinators arity 2-8
├── tests/
│   ├── Norse.Primitives.Tests/
│   │   ├── Norse.Primitives.Tests.csproj                # NEW
│   │   ├── ResultTests.cs
│   │   ├── ErrorTests.cs
│   │   ├── ParserTests.cs
│   │   ├── BclParserCoverageTests.cs
│   │   ├── ResultExtensions/
│   │   │   ├── SyncTests.cs
│   │   │   ├── AsyncTests.cs
│   │   │   ├── PresentTests.cs
│   │   │   └── AggregateTests.cs
│   │   ├── MustConsumeAttributeTests.cs
│   │   └── Properties/                              # FsCheck
│   │       ├── MonadLawProperties.cs
│   │       └── ParserRoundTripProperties.cs
│   └── Norse.Primitives.Aot.SmokeTests/
│       ├── Norse.Primitives.Aot.SmokeTests.csproj       # NEW
│       └── Program.cs                               # AOT executable that exercises parser surface
└── benchmarks/
    └── Norse.Primitives.Benchmarks/
        ├── Norse.Primitives.Benchmarks.csproj           # NEW
        ├── Program.cs
        ├── ParserBenchmarks.cs
        ├── CompositionBenchmarks.cs
        └── AggregationBenchmarks.cs
```

---

## Phase 1 — Scaffolding

### Task 1: Verify .NET 11 preview SDK and C# 15 union syntax

**Files:**
- Create: `/tmp/norse-primitives-smoketest/Program.cs` (throwaway)

- [ ] **Step 1: Verify SDK presence**

Run: `dotnet --list-sdks`

Expected: at least one entry starting with `11.0.` (e.g., `11.0.100-preview.4.xxxxx`). If absent, install from https://dotnet.microsoft.com/download/dotnet/11.0 before proceeding.

- [ ] **Step 2: Create a throwaway smoke-test console app to confirm C# 15 union syntax compiles**

Run:
```powershell
mkdir $env:TEMP\norse-primitives-smoketest
cd $env:TEMP\norse-primitives-smoketest
dotnet new console -f net11.0
```

Edit `Program.cs`:

```csharp
public readonly record struct Cat(string Name);
public readonly record struct Dog(string Name);

public union Pet(Cat, Dog);

class Program
{
	static void Main()
	{
		Pet pet = new Dog("Rex");
		var name = pet switch
		{
			Cat c => c.Name,
			Dog d => d.Name,
		};
		System.Console.WriteLine(name);
	}
}
```

Edit `<csproj>` to add `<LangVersion>preview</LangVersion>` inside `<PropertyGroup>`.

- [ ] **Step 3: Build and run**

Run: `dotnet run`
Expected output: `Rex`

If build fails with `union is not a recognized keyword`, the SDK version is too old or `<LangVersion>preview</LangVersion>` is missing.

- [ ] **Step 4: Delete the smoke test**

Run: `cd ..; rm -r -force $env:TEMP\norse-primitives-smoketest`

No commit (throwaway).

---

### Task 2: Meta-repo `Directory.Build.props` with tier policy

**Files:**
- Create: `Directory.Build.props`

- [ ] **Step 1: Create the file**

```xml
<Project>

	<!--
		Meta-repo Directory.Build.props.
		Inherited by every submodule and subdirectory via MSBuild's implicit-import behavior.
		Each project declares its tier via <NorseTier> in its csproj.
		The tier translates to runtime-async on/off and AOT settings.
		See: docs/Svartalfheim/specs/2026-05-20-svartalfheim-primitives-design.md §9.
	-->

	<PropertyGroup>
		<LangVersion>preview</LangVersion>
		<Nullable>enable</Nullable>
		<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
		<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
		<NorseTier Condition="'$(NorseTier)' == ''">Server</NorseTier>
	</PropertyGroup>

	<PropertyGroup Condition="'$(NorseTier)' == 'Server'">
		<Features>runtime-async=on</Features>
	</PropertyGroup>

	<PropertyGroup Condition="'$(NorseTier)' == 'ExternalCli'">
		<Features>runtime-async=on</Features>
		<PublishAot>true</PublishAot>
		<IsAotCompatible>true</IsAotCompatible>
		<IsTrimmable>true</IsTrimmable>
	</PropertyGroup>

	<PropertyGroup Condition="'$(NorseTier)' == 'Client'">
		<UseRuntimeAsync>false</UseRuntimeAsync>
	</PropertyGroup>

	<PropertyGroup Condition="'$(NorseTier)' == 'Library'">
		<!-- Library tier: stays runtime-async-neutral so it can be consumed by Client (WASM/MAUI) and Server alike. -->
		<UseRuntimeAsync>false</UseRuntimeAsync>
		<IsAotCompatible>true</IsAotCompatible>
		<IsTrimmable>true</IsTrimmable>
	</PropertyGroup>

</Project>
```

- [ ] **Step 2: Stage**

Run: `git add Directory.Build.props`

Proposed commit message: `feat(meta): add Directory.Build.props with NorseTier policy`

---

### Task 3: Norse.Primitives repo skeleton (.editorconfig, .gitignore, LICENSE, README placeholder)

**Files:**
- Create: `Norse.Primitives/.editorconfig`
- Create: `Norse.Primitives/.gitignore`
- Create: `Norse.Primitives/LICENSE`
- Create: `Norse.Primitives/README.md`
- Create: `Norse.Primitives/Directory.Build.props`

- [ ] **Step 1: Create the directory**

Run: `mkdir Norse.Primitives; mkdir Norse.Primitives\src; mkdir Norse.Primitives\src\Norse.Primitives; mkdir Norse.Primitives\src\Norse.Primitives\Errors; mkdir Norse.Primitives\src\Norse.Primitives\ResultExtensions; mkdir Norse.Primitives\tests; mkdir Norse.Primitives\tests\Norse.Primitives.Tests; mkdir Norse.Primitives\tests\Norse.Primitives.Tests\ResultExtensions; mkdir Norse.Primitives\tests\Norse.Primitives.Tests\Properties; mkdir Norse.Primitives\tests\Norse.Primitives.Aot.SmokeTests; mkdir Norse.Primitives\benchmarks; mkdir Norse.Primitives\benchmarks\Norse.Primitives.Benchmarks`

- [ ] **Step 2: Create `Norse.Primitives/.editorconfig`**

```ini
# Norse.Primitives — code style
# Mirrors CLAUDE.md §5: tabs, 2-space width, var for return assignments only.

root = true

[*]
indent_style = tab
indent_size = 2
tab_width = 2
end_of_line = crlf
insert_final_newline = true
charset = utf-8

[*.{md,yml,yaml,json}]
indent_style = space
indent_size = 2

[*.cs]
csharp_style_var_for_built_in_types = false:warning
csharp_style_var_when_type_is_apparent = false:warning
csharp_style_var_elsewhere = false:warning

dotnet_diagnostic.IDE0007.severity = none
dotnet_diagnostic.IDE0008.severity = warning

dotnet_style_predefined_type_for_locals_parameters_members = true:warning
dotnet_style_predefined_type_for_member_access = true:warning

# Accessibility modifiers: omit if default
dotnet_style_require_accessibility_modifiers = omit_if_default:warning
```

- [ ] **Step 3: Create `Norse.Primitives/.gitignore`**

```
# Build output
[Bb]in/
[Oo]bj/
[Oo]ut/
[Ll]og/
[Ll]ogs/

# Visual Studio / Rider
.vs/
.idea/
*.user
*.suo

# Test results
[Tt]est[Rr]esult*/
BenchmarkDotNet.Artifacts/

# NuGet
*.nupkg
*.snupkg
project.lock.json
artifacts/
```

- [ ] **Step 4: Create `Norse.Primitives/LICENSE` (MIT)**

```
MIT License

Copyright (c) 2026 {Company}

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- [ ] **Step 5: Create `Norse.Primitives/README.md` (placeholder; filled in Task 35)**

```markdown
# Norse.Primitives

Foundational primitives for the Norse platform.

See `docs/Svartalfheim/specs/2026-05-20-svartalfheim-primitives-design.md` in the meta-repo for design rationale.

(README content is finalized in Task 35.)
```

- [ ] **Step 6: Create `Norse.Primitives/Directory.Build.props`**

```xml
<Project>

	<!-- Inherits from the meta-repo's Directory.Build.props two levels up. -->
	<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />

	<PropertyGroup>
		<NorseTier>Library</NorseTier>
		<TargetFramework>net11.0</TargetFramework>
	</PropertyGroup>

</Project>
```

- [ ] **Step 7: Stage**

Run: `git add Norse.Primitives/.editorconfig Norse.Primitives/.gitignore Norse.Primitives/LICENSE Norse.Primitives/README.md Norse.Primitives/Directory.Build.props`

Proposed commit message: `feat(svartalfheim): add repo skeleton (editorconfig, gitignore, license)`

---

### Task 4: Norse.Primitives main csproj

**Files:**
- Create: `Norse.Primitives/src/Norse.Primitives/Norse.Primitives.csproj`

- [ ] **Step 1: Write the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<RootNamespace>Norse.Primitives</RootNamespace>
		<AssemblyName>Norse.Primitives</AssemblyName>
		<IsPackable>true</IsPackable>
		<PackageId>Norse.Primitives</PackageId>
		<Description>Foundational primitives (Result&lt;T&gt;, Error union, parser gateway) for the Norse platform.</Description>
		<Authors>{Company}</Authors>
		<PackageLicenseExpression>MIT</PackageLicenseExpression>
		<PackageReadmeFile>README.md</PackageReadmeFile>
	</PropertyGroup>

	<ItemGroup>
		<None Include="..\..\README.md" Pack="true" PackagePath="\" />
	</ItemGroup>

</Project>
```

- [ ] **Step 2: Verify it builds (empty assembly)**

Run: `dotnet build Norse.Primitives/src/Norse.Primitives/Norse.Primitives.csproj`

Expected: `Build succeeded.` with 0 errors. (Warnings about empty assembly are acceptable.)

- [ ] **Step 3: Stage**

Run: `git add Norse.Primitives/src/Norse.Primitives/Norse.Primitives.csproj`

Proposed commit message: `feat(svartalfheim): add main library csproj`

---

### Task 5: Test project csproj

**Files:**
- Create: `Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj`

- [ ] **Step 1: Write the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<RootNamespace>Norse.Primitives.Tests</RootNamespace>
		<AssemblyName>Norse.Primitives.Tests</AssemblyName>
		<IsPackable>false</IsPackable>
		<NorseTier>Server</NorseTier>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
		<PackageReference Include="xunit.v3" Version="*" />
		<PackageReference Include="xunit.runner.visualstudio" Version="*" />
		<PackageReference Include="Shouldly" Version="*" />
		<PackageReference Include="NSubstitute" Version="*" />
		<PackageReference Include="FsCheck.Xunit" Version="*" />
	</ItemGroup>

	<ItemGroup>
		<ProjectReference Include="..\..\src\Norse.Primitives\Norse.Primitives.csproj" />
	</ItemGroup>

</Project>
```

- [ ] **Step 2: Add a smoke-test file to confirm the project compiles**

Create: `Norse.Primitives/tests/Norse.Primitives.Tests/SmokeTests.cs`

```csharp
using Shouldly;
using Xunit;

namespace Norse.Primitives.Tests;

public sealed class SmokeTests
{
	[Fact]
	public void Should_run_a_trivial_assertion()
	{
		(1 + 1).ShouldBe(2);
	}
}
```

- [ ] **Step 3: Run the test**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj`

Expected: `Passed: 1`.

- [ ] **Step 4: Stage**

Run: `git add Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj Norse.Primitives/tests/Norse.Primitives.Tests/SmokeTests.cs`

Proposed commit message: `feat(svartalfheim): add test project scaffolding with smoke test`

---

### Task 6: Solution file (`.slnx`)

**Files:**
- Create: `Norse.Primitives/Norse.Primitives.slnx`

- [ ] **Step 1: Create the solution and add existing projects**

Run:
```powershell
cd Norse.Primitives
dotnet new sln --format slnx --name Norse.Primitives
dotnet sln Norse.Primitives.slnx add src/Norse.Primitives/Norse.Primitives.csproj
dotnet sln Norse.Primitives.slnx add tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj
cd ..
```

- [ ] **Step 2: Verify the solution builds**

Run: `dotnet build Norse.Primitives/Norse.Primitives.slnx`

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Stage**

Run: `git add Norse.Primitives/Norse.Primitives.slnx`

Proposed commit message: `feat(svartalfheim): add slnx solution file`

---

## Phase 2 — Core Types

### Task 7: `Result<T>`, `Success<T>`, `Failure` and pattern-match tests

**Files:**
- Create: `Norse.Primitives/src/Norse.Primitives/Result.cs`
- Create: `Norse.Primitives/tests/Norse.Primitives.Tests/ResultTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `ResultTests.cs`:

```csharp
using Shouldly;
using Xunit;

namespace Norse.Primitives.Tests;

public sealed class ResultTests
{
	[Fact]
	public void Should_construct_Success_carrying_a_value()
	{
		Result<int> result = new Success<int>(42);

		result.ShouldBeOfType<Result<int>>();
		(result is Success<int>).ShouldBeTrue();
	}

	[Fact]
	public void Should_construct_Failure_carrying_an_error()
	{
		Error error = new ParseError("abc", "Int32", null, null);
		Result<int> result = new Failure(error);

		(result is Failure).ShouldBeTrue();
	}

	[Fact]
	public void Should_pattern_match_against_Success_case()
	{
		Result<int> result = new Success<int>(7);

		int value = result switch
		{
			Success<int>(var v) => v,
			Failure => -1,
		};

		value.ShouldBe(7);
	}

	[Fact]
	public void Should_pattern_match_against_Failure_case()
	{
		Result<int> result = new Failure(new ParseError("abc", "Int32", null, null));

		int value = result switch
		{
			Success<int>(var v) => v,
			Failure => -1,
		};

		value.ShouldBe(-1);
	}

	[Fact]
	public void Should_compare_two_Success_values_structurally()
	{
		Result<int> a = new Success<int>(42);
		Result<int> b = new Success<int>(42);

		a.ShouldBe(b);
	}
}
```

- [ ] **Step 2: Run tests, verify they fail to compile**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj`

Expected: build errors (`Result<T>`, `Success<T>`, `Failure`, `Error`, `ParseError` undefined).

- [ ] **Step 3: Implement `Result.cs`**

```csharp
namespace Norse.Primitives;

public readonly record struct Success<T>(T Value) where T : notnull;

public readonly record struct Failure(Error Error);

[MustConsume]
public union Result<T>(Success<T>, Failure) where T : notnull;
```

Note: `Error`, `ParseError`, and `MustConsumeAttribute` are referenced but not yet defined. The next tasks add them. For now, comment out the body of `Result.cs` if needed to compile in isolation — OR proceed to Task 8 / Task 9 / Task 24 before running tests. Recommended order: Task 8 (Error case types) → Task 9 (Error union) → Task 24 (`[MustConsume]`) — only then re-run ResultTests.

To minimize churn, copy this stub `MustConsumeAttribute` placeholder into `Result.cs` for now (it will be moved in Task 24):

```csharp
namespace Norse.Primitives;

[System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.Class)]
internal sealed class MustConsumeAttribute : System.Attribute;
```

Make it `internal` for now; Task 24 promotes it to `public` and moves it to its own file.

- [ ] **Step 4: Continue to Task 8 before running tests** (Result depends on Error)

- [ ] **Step 5: Stage (deferred — stage together with Task 8 and Task 9)**

---

### Task 8: `Error` case types (six structs)

**Files:**
- Create: `Norse.Primitives/src/Norse.Primitives/Errors/ParseError.cs`
- Create: `Norse.Primitives/src/Norse.Primitives/Errors/ValidationError.cs`
- Create: `Norse.Primitives/src/Norse.Primitives/Errors/NotFoundError.cs`
- Create: `Norse.Primitives/src/Norse.Primitives/Errors/UnauthorizedError.cs`
- Create: `Norse.Primitives/src/Norse.Primitives/Errors/ConflictError.cs`
- Create: `Norse.Primitives/src/Norse.Primitives/Errors/AggregateError.cs`
- Create: `Norse.Primitives/tests/Norse.Primitives.Tests/ErrorTests.cs`

- [ ] **Step 1: Write failing tests**

Create `ErrorTests.cs`:

```csharp
using System.Collections.Immutable;
using Shouldly;
using Xunit;

namespace Norse.Primitives.Tests;

public sealed class ErrorTests
{
	[Fact]
	public void ParseError_should_store_all_fields()
	{
		ParseError pe = new("abc", "Int32", "N0", "non-numeric");
		pe.Input.ShouldBe("abc");
		pe.ExpectedType.ShouldBe("Int32");
		pe.Format.ShouldBe("N0");
		pe.Reason.ShouldBe("non-numeric");
	}

	[Fact]
	public void ParseError_MaxInputLength_should_be_256()
	{
		ParseError.MaxInputLength.ShouldBe(256);
	}

	[Fact]
	public void ValidationError_should_store_field_rule_detail()
	{
		ValidationError ve = new("PolicyNumber", "MustMatchPattern", "did not match ABC-####");
		ve.Field.ShouldBe("PolicyNumber");
		ve.Rule.ShouldBe("MustMatchPattern");
		ve.Detail.ShouldBe("did not match ABC-####");
	}

	[Fact]
	public void NotFoundError_should_store_resource_and_key()
	{
		NotFoundError nfe = new("Policy", "POL-2026-0001");
		nfe.Resource.ShouldBe("Policy");
		nfe.Key.ShouldBe("POL-2026-0001");
	}

	[Fact]
	public void UnauthorizedError_should_store_action()
	{
		UnauthorizedError ue = new("BindPolicy");
		ue.Action.ShouldBe("BindPolicy");
	}

	[Fact]
	public void ConflictError_should_store_resource_and_reason()
	{
		ConflictError ce = new("Policy", "Already bound");
		ce.Resource.ShouldBe("Policy");
		ce.Reason.ShouldBe("Already bound");
	}

	[Fact]
	public void AggregateError_should_store_inner_errors()
	{
		Error e1 = new ValidationError("A", "R1", null);
		Error e2 = new ValidationError("B", "R2", null);
		AggregateError ae = new(ImmutableArray.Create(e1, e2));
		ae.Errors.Length.ShouldBe(2);
	}
}
```

- [ ] **Step 2: Run tests, verify they fail to compile**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj`

Expected: build errors (case types undefined).

- [ ] **Step 3: Implement `ParseError.cs`**

```csharp
namespace Norse.Primitives;

public readonly record struct ParseError(
	string Input,
	string ExpectedType,
	string? Format,
	string? Reason)
{
	public const int MaxInputLength = 256;
}
```

- [ ] **Step 4: Implement `ValidationError.cs`**

```csharp
namespace Norse.Primitives;

public readonly record struct ValidationError(
	string Field,
	string Rule,
	string? Detail);
```

- [ ] **Step 5: Implement `NotFoundError.cs`**

```csharp
namespace Norse.Primitives;

public readonly record struct NotFoundError(
	string Resource,
	string Key);
```

- [ ] **Step 6: Implement `UnauthorizedError.cs`**

```csharp
namespace Norse.Primitives;

public readonly record struct UnauthorizedError(
	string Action);
```

- [ ] **Step 7: Implement `ConflictError.cs`**

```csharp
namespace Norse.Primitives;

public readonly record struct ConflictError(
	string Resource,
	string Reason);
```

- [ ] **Step 8: Implement `AggregateError.cs`**

```csharp
using System.Collections.Immutable;

namespace Norse.Primitives;

public readonly record struct AggregateError(
	ImmutableArray<Error> Errors);
```

- [ ] **Step 9: Continue to Task 9 — `Error` union not yet defined.**

---

### Task 9: `Error` union

**Files:**
- Create: `Norse.Primitives/src/Norse.Primitives/Error.cs`

- [ ] **Step 1: Implement the union**

```csharp
namespace Norse.Primitives;

public union Error(
	ParseError,
	ValidationError,
	NotFoundError,
	UnauthorizedError,
	ConflictError,
	AggregateError);
```

- [ ] **Step 2: Run all tests so far**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj`

Expected: `Passed: <N>` for all `ResultTests` and `ErrorTests`. If pattern-match tests in `ResultTests` fail with a switch-exhaustiveness diagnostic, verify the C# 15 union compiles correctly (Task 1 smoke test should have already confirmed this).

- [ ] **Step 3: Stage Tasks 7, 8, 9 together**

Run:
```powershell
git add Norse.Primitives/src/Norse.Primitives/Result.cs Norse.Primitives/src/Norse.Primitives/Error.cs Norse.Primitives/src/Norse.Primitives/Errors/ Norse.Primitives/tests/Norse.Primitives.Tests/ResultTests.cs Norse.Primitives/tests/Norse.Primitives.Tests/ErrorTests.cs
```

Proposed commit message: `feat(svartalfheim): add Result<T> union and Error case types`

---

### Task 10: Pattern-match unwrapping rule documentation

**Files:**
- Modify: `Norse.Primitives/src/Norse.Primitives/Result.cs`

- [ ] **Step 1: Add XML documentation calling out the unwrapping rule**

Replace the contents of `Result.cs` with:

```csharp
namespace Norse.Primitives;

/// <summary>
/// Carries the success value of a parsed/computed <see cref="Result{T}"/>.
/// </summary>
public readonly record struct Success<T>(T Value) where T : notnull;

/// <summary>
/// Carries the error of a failed <see cref="Result{T}"/>.
/// </summary>
public readonly record struct Failure(Error Error);

/// <summary>
/// Closed two-case union: either a <see cref="Success{T}"/> carrying a value, or a <see cref="Failure"/> carrying an <see cref="Error"/>.
/// <para>
/// Must be consumed. The <c>[MustConsume]</c> attribute, enforced by the <c>YGG201</c> analyzer
/// in <c>norse-primitives-architecture</c>, rejects discarded <see cref="Result{T}"/> values at compile time.
/// </para>
/// <para>
/// <b>Pattern-match unwrapping rule:</b> C# 15 union patterns apply to the union's contained case,
/// not to the union itself. The canonical switch is:
/// <code>
/// return result switch
/// {
/// 	Success&lt;int&gt;(var v) =&gt; UseValue(v),
/// 	Failure(var err) =&gt; HandleError(err),
/// };
/// </code>
/// Writing <c>result is Result&lt;int&gt; r</c> does <b>not</b> match — <c>Result&lt;T&gt;</c> itself
/// is not a case type. Match against <see cref="Success{T}"/> or <see cref="Failure"/> directly,
/// or use the lambda-based <c>Match</c> extension for language-agnostic exhaustive consumption.
/// </para>
/// </summary>
[MustConsume]
public union Result<T>(Success<T>, Failure) where T : notnull;
```

Remove the temporary internal `MustConsumeAttribute` stub from this file (Task 24 will define it as a public attribute in its own file; for now leave the internal stub at the bottom of `Result.cs` to keep the build green until Task 24).

Concretely: keep at the bottom of `Result.cs`:

```csharp
[System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.Class)]
internal sealed class MustConsumeAttribute : System.Attribute;
```

- [ ] **Step 2: Re-run tests**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj`

Expected: all green.

- [ ] **Step 3: Stage**

Run: `git add Norse.Primitives/src/Norse.Primitives/Result.cs`

Proposed commit message: `docs(svartalfheim): add XML docs to Result<T> with pattern-match unwrapping rule`

---

## Phase 3 — Parser Gateway

### Task 11: `Parser.ParseRequired<T>`

**Files:**
- Create: `Norse.Primitives/src/Norse.Primitives/Parser.cs`
- Create: `Norse.Primitives/tests/Norse.Primitives.Tests/ParserTests.cs`

- [ ] **Step 1: Write failing tests**

Create `ParserTests.cs`:

```csharp
using System.Globalization;
using Shouldly;
using Xunit;

namespace Norse.Primitives.Tests;

public sealed class ParserTests
{
	private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

	[Fact]
	public void ParseRequired_should_return_Success_for_valid_input()
	{
		Result<int> result = Parser.ParseRequired<int>("42", Ci);
		(result is Success<int>(42)).ShouldBeTrue();
	}

	[Fact]
	public void ParseRequired_should_return_Failure_for_invalid_input()
	{
		Result<int> result = Parser.ParseRequired<int>("abc", Ci);
		(result is Failure).ShouldBeTrue();
	}

	[Fact]
	public void ParseRequired_should_capture_input_in_ParseError()
	{
		Result<int> result = Parser.ParseRequired<int>("abc", Ci);
		(result is Failure(ParseError pe)
			&& pe.Input == "abc"
			&& pe.ExpectedType == nameof(System.Int32)).ShouldBeTrue();
	}

	[Fact]
	public void ParseRequired_should_truncate_overly_long_input_to_MaxInputLength()
	{
		string longInput = new('x', ParseError.MaxInputLength + 100);
		Result<int> result = Parser.ParseRequired<int>(longInput.AsSpan(), Ci);
		(result is Failure(ParseError pe)
			&& pe.Input.Length == ParseError.MaxInputLength).ShouldBeTrue();
	}

	[Fact]
	public void ParseRequired_should_treat_empty_input_as_Failure()
	{
		Result<int> result = Parser.ParseRequired<int>("", Ci);
		(result is Failure).ShouldBeTrue();
	}
}
```

(Note: the `((Failure)((object)result)).Error switch` pattern in tests is a workaround for the union unwrapping rule; in production code, consumers should use `switch` or `Match`.)

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj`

Expected: build errors (`Parser` undefined).

- [ ] **Step 3: Implement `Parser.cs`**

```csharp
using System;

namespace Norse.Primitives;

public static class Parser
{
	public static Result<T> ParseRequired<T>(
		ReadOnlySpan<char> input,
		IFormatProvider provider)
		where T : ISpanParsable<T>, notnull
	{
		if (input.IsEmpty || input.IsWhiteSpace())
		{
			return new Failure(new Error(BuildParseError(input, typeof(T).Name, reason: "input was empty")));
		}

		return T.TryParse(input, provider, out T value)
			? new Success<T>(value)
			: new Failure(new Error(BuildParseError(input, typeof(T).Name, reason: null)));
	}

	internal static ParseError BuildParseError(ReadOnlySpan<char> input, string expectedType, string? reason)
	{
		string captured = input.Length <= ParseError.MaxInputLength
			? input.ToString()
			: input[..ParseError.MaxInputLength].ToString();

		return new ParseError(captured, expectedType, Format: null, Reason: reason);
	}
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj --filter "FullyQualifiedName~ParserTests"`

Expected: `Passed: 5`.

- [ ] **Step 5: Stage**

Run: `git add Norse.Primitives/src/Norse.Primitives/Parser.cs Norse.Primitives/tests/Norse.Primitives.Tests/ParserTests.cs`

Proposed commit message: `feat(svartalfheim): add Parser.ParseRequired<T> over ISpanParsable<T>`

---

### Task 12: `Parser.ParseOptional<T>`

**Files:**
- Modify: `Norse.Primitives/src/Norse.Primitives/Parser.cs`
- Modify: `Norse.Primitives/tests/Norse.Primitives.Tests/ParserTests.cs`

- [ ] **Step 1: Add failing tests to `ParserTests.cs`**

```csharp
[Fact]
public void ParseOptional_should_return_null_for_empty_input()
{
	Result<int>? result = Parser.ParseOptional<int>("", Ci);
	result.HasValue.ShouldBeFalse();
}

[Fact]
public void ParseOptional_should_return_null_for_whitespace_input()
{
	Result<int>? result = Parser.ParseOptional<int>("   ", Ci);
	result.HasValue.ShouldBeFalse();
}

[Fact]
public void ParseOptional_should_return_Success_for_valid_input()
{
	Result<int>? result = Parser.ParseOptional<int>("42", Ci);
	result.HasValue.ShouldBeTrue();
	(result!.Value is Success<int>(42)).ShouldBeTrue();
}

[Fact]
public void ParseOptional_should_return_Failure_for_invalid_input()
{
	Result<int>? result = Parser.ParseOptional<int>("abc", Ci);
	result.HasValue.ShouldBeTrue();
	(result!.Value is Failure).ShouldBeTrue();
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj`

Expected: build errors (`ParseOptional` undefined).

- [ ] **Step 3: Add `ParseOptional<T>` to `Parser.cs`**

```csharp
public static Result<T>? ParseOptional<T>(
	ReadOnlySpan<char> input,
	IFormatProvider provider)
	where T : ISpanParsable<T>, notnull
{
	if (input.IsEmpty || input.IsWhiteSpace())
	{
		return null;
	}

	return T.TryParse(input, provider, out T value)
		? new Success<T>(value)
		: new Failure(new Error(BuildParseError(input, typeof(T).Name, reason: null)));
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj --filter "FullyQualifiedName~ParserTests"`

Expected: all green.

- [ ] **Step 5: Stage**

Run: `git add Norse.Primitives/src/Norse.Primitives/Parser.cs Norse.Primitives/tests/Norse.Primitives.Tests/ParserTests.cs`

Proposed commit message: `feat(svartalfheim): add Parser.ParseOptional<T> returning Result<T>?`

---

### Task 13: BCL parser coverage tests

**Files:**
- Create: `Norse.Primitives/tests/Norse.Primitives.Tests/BclParserCoverageTests.cs`

- [ ] **Step 1: Write coverage tests for every type in spec §6.3**

```csharp
using System;
using System.Globalization;
using System.Net;
using System.Numerics;
using Shouldly;
using Xunit;

namespace Norse.Primitives.Tests;

public sealed class BclParserCoverageTests
{
	private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

	[Theory]
	[InlineData("true", true)]
	[InlineData("false", false)]
	public void Bool_should_parse(string s, bool expected)
	{
		Result<bool> r = Parser.ParseRequired<bool>(s, Ci);
		(r is Success<bool> succ && succ.Value == expected).ShouldBeTrue();
	}

	[Fact] public void Byte_should_parse()    => Parser.ParseRequired<byte>("128", Ci).ShouldSatisfyAllConditions(r => (r is Success<byte>(128)).ShouldBeTrue());
	[Fact] public void SByte_should_parse()   => Parser.ParseRequired<sbyte>("-1", Ci).ShouldSatisfyAllConditions(r => (r is Success<sbyte>(-1)).ShouldBeTrue());
	[Fact] public void Short_should_parse()   => Parser.ParseRequired<short>("32000", Ci).ShouldSatisfyAllConditions(r => (r is Success<short>(32000)).ShouldBeTrue());
	[Fact] public void UShort_should_parse()  => Parser.ParseRequired<ushort>("65000", Ci).ShouldSatisfyAllConditions(r => (r is Success<ushort>(65000)).ShouldBeTrue());
	[Fact] public void Int_should_parse()     => Parser.ParseRequired<int>("42", Ci).ShouldSatisfyAllConditions(r => (r is Success<int>(42)).ShouldBeTrue());
	[Fact] public void UInt_should_parse()    => Parser.ParseRequired<uint>("42", Ci).ShouldSatisfyAllConditions(r => (r is Success<uint>(42u)).ShouldBeTrue());
	[Fact] public void Long_should_parse()    => Parser.ParseRequired<long>("9000000000", Ci).ShouldSatisfyAllConditions(r => (r is Success<long>(9000000000L)).ShouldBeTrue());
	[Fact] public void ULong_should_parse()   => Parser.ParseRequired<ulong>("9000000000", Ci).ShouldSatisfyAllConditions(r => (r is Success<ulong>(9000000000UL)).ShouldBeTrue());
	[Fact] public void Int128_should_parse()  => (Parser.ParseRequired<Int128>("100", Ci) is Success<Int128> s && s.Value == (Int128)100).ShouldBeTrue();
	[Fact] public void UInt128_should_parse() => (Parser.ParseRequired<UInt128>("100", Ci) is Success<UInt128> s && s.Value == (UInt128)100).ShouldBeTrue();
	[Fact] public void NInt_should_parse()    => (Parser.ParseRequired<nint>("100", Ci) is Success<nint>(100)).ShouldBeTrue();
	[Fact] public void NUInt_should_parse()   => (Parser.ParseRequired<nuint>("100", Ci) is Success<nuint> s && s.Value == (nuint)100).ShouldBeTrue();

	[Fact] public void Float_should_parse()   => (Parser.ParseRequired<float>("3.14", Ci) is Success<float> s && System.Math.Abs(s.Value - 3.14f) < 0.001f).ShouldBeTrue();
	[Fact] public void Double_should_parse()  => (Parser.ParseRequired<double>("3.14", Ci) is Success<double> s && System.Math.Abs(s.Value - 3.14) < 0.001).ShouldBeTrue();
	[Fact] public void Half_should_parse()    => (Parser.ParseRequired<Half>("3.14", Ci) is Success<Half>).ShouldBeTrue();
	[Fact] public void Decimal_should_parse() => (Parser.ParseRequired<decimal>("3.14", Ci) is Success<decimal> s && s.Value == 3.14m).ShouldBeTrue();

	[Fact] public void Guid_should_parse()    => (Parser.ParseRequired<Guid>("00000000-0000-0000-0000-000000000001", Ci) is Success<Guid>).ShouldBeTrue();

	[Fact] public void DateOnly_should_parse()       => (Parser.ParseRequired<DateOnly>("2026-05-20", Ci) is Success<DateOnly>).ShouldBeTrue();
	[Fact] public void TimeOnly_should_parse()       => (Parser.ParseRequired<TimeOnly>("12:34:56", Ci) is Success<TimeOnly>).ShouldBeTrue();
	[Fact] public void DateTime_should_parse()       => (Parser.ParseRequired<DateTime>("2026-05-20T12:34:56", Ci) is Success<DateTime>).ShouldBeTrue();
	[Fact] public void DateTimeOffset_should_parse() => (Parser.ParseRequired<DateTimeOffset>("2026-05-20T12:34:56+00:00", Ci) is Success<DateTimeOffset>).ShouldBeTrue();
	[Fact] public void TimeSpan_should_parse()       => (Parser.ParseRequired<TimeSpan>("01:02:03", Ci) is Success<TimeSpan>).ShouldBeTrue();

	[Fact] public void BigInteger_should_parse() => (Parser.ParseRequired<BigInteger>("99999999999999999999", Ci) is Success<BigInteger>).ShouldBeTrue();
	[Fact] public void Version_should_parse()    => (Parser.ParseRequired<Version>("1.2.3.4", Ci) is Success<Version>).ShouldBeTrue();

	[Fact] public void IPAddress_should_parse()  => (Parser.ParseRequired<IPAddress>("127.0.0.1", Ci) is Success<IPAddress>).ShouldBeTrue();
	[Fact] public void IPNetwork_should_parse()  => (Parser.ParseRequired<IPNetwork>("10.0.0.0/8", Ci) is Success<IPNetwork>).ShouldBeTrue();
	[Fact] public void IPEndPoint_should_parse() => (Parser.ParseRequired<IPEndPoint>("127.0.0.1:8080", Ci) is Success<IPEndPoint>).ShouldBeTrue();
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj --filter "FullyQualifiedName~BclParserCoverageTests"`

Expected: all green. If any single BCL type fails because it implements only `IParsable<T>` (not `ISpanParsable<T>`), remove that type from the v1 list, update the spec §6.3, and proceed.

- [ ] **Step 3: Stage**

Run: `git add Norse.Primitives/tests/Norse.Primitives.Tests/BclParserCoverageTests.cs`

Proposed commit message: `test(svartalfheim): cover every BCL type listed in spec §6.3`

---

## Phase 4 — Sync Composition

### Task 14: `Map`, `MapError` extension methods

**Files:**
- Create: `Norse.Primitives/src/Norse.Primitives/ResultExtensions/Sync.cs`
- Create: `Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/SyncTests.cs`

- [ ] **Step 1: Write failing tests**

Create `SyncTests.cs`:

```csharp
using Shouldly;
using Xunit;

namespace Norse.Primitives.Tests.ResultExtensions;

public sealed class MapTests
{
	[Fact]
	public void Map_should_transform_Success_value()
	{
		Result<int> source = new Success<int>(2);
		Result<int> mapped = source.Map(v => v * 21);
		(mapped is Success<int>(42)).ShouldBeTrue();
	}

	[Fact]
	public void Map_should_pass_through_Failure_unchanged()
	{
		Error err = new Error(new ParseError("x", "Int32", null, null));
		Result<int> source = new Failure(err);
		Result<int> mapped = source.Map(v => v * 2);
		(mapped is Failure).ShouldBeTrue();
	}

	[Fact]
	public void MapError_should_transform_Failure_error()
	{
		Error err = new Error(new ParseError("x", "Int32", null, null));
		Result<int> source = new Failure(err);
		Result<int> remapped = source.MapError(_ => new Error(new ValidationError("Field", "Rule", null)));
		(remapped is Failure(var newErr) && newErr is ValidationError).ShouldBeTrue();
	}

	[Fact]
	public void MapError_should_pass_through_Success_unchanged()
	{
		Result<int> source = new Success<int>(42);
		Result<int> remapped = source.MapError(_ => new Error(new ValidationError("F", "R", null)));
		(remapped is Success<int>(42)).ShouldBeTrue();
	}
}
```

- [ ] **Step 2: Run, verify failing build**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj --filter "FullyQualifiedName~MapTests"`

Expected: build errors.

- [ ] **Step 3: Implement `Map`, `MapError` in `Sync.cs`**

```csharp
using System;

namespace Norse.Primitives;

public static class ResultSyncExtensions
{
	public static Result<U> Map<T, U>(this Result<T> source, Func<T, U> map)
		where T : notnull
		where U : notnull
	{
		return source switch
		{
			Success<T>(var v) => new Success<U>(map(v)),
			Failure(var err) => new Failure(err),
		};
	}

	public static Result<T> MapError<T>(this Result<T> source, Func<Error, Error> map)
		where T : notnull
	{
		return source switch
		{
			Success<T> s => s,
			Failure(var err) => new Failure(map(err)),
		};
	}
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj --filter "FullyQualifiedName~MapTests"`

Expected: 4 passed.

- [ ] **Step 5: Stage**

Run: `git add Norse.Primitives/src/Norse.Primitives/ResultExtensions/Sync.cs Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/SyncTests.cs`

Proposed commit message: `feat(svartalfheim): add Map and MapError extensions`

---

### Task 15: `Bind` extension method

**Files:**
- Modify: `Norse.Primitives/src/Norse.Primitives/ResultExtensions/Sync.cs`
- Modify: `Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/SyncTests.cs`

- [ ] **Step 1: Add failing tests**

Append to `SyncTests.cs`:

```csharp
public sealed class BindTests
{
	[Fact]
	public void Bind_should_chain_Success_to_a_new_Result()
	{
		Result<int> source = new Success<int>(2);
		Result<string> bound = source.Bind(v => (Result<string>)new Success<string>($"value-{v}"));
		(bound is Success<string> s && s.Value == "value-2").ShouldBeTrue();
	}

	[Fact]
	public void Bind_should_short_circuit_on_Failure()
	{
		bool called = false;
		Result<int> source = new Failure(new Error(new ParseError("x", "Int32", null, null)));
		Result<string> bound = source.Bind(v =>
		{
			called = true;
			return (Result<string>)new Success<string>("ok");
		});
		called.ShouldBeFalse();
		(bound is Failure).ShouldBeTrue();
	}

	[Fact]
	public void Bind_should_propagate_inner_Failure_from_chained_function()
	{
		Result<int> source = new Success<int>(2);
		Error inner = new Error(new ValidationError("F", "R", null));
		Result<string> bound = source.Bind(_ => (Result<string>)new Failure(inner));
		(bound is Failure(var e) && e is ValidationError).ShouldBeTrue();
	}
}
```

- [ ] **Step 2: Run, verify build fails**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj --filter "FullyQualifiedName~BindTests"`

Expected: build errors.

- [ ] **Step 3: Add `Bind` to `Sync.cs`**

```csharp
public static Result<U> Bind<T, U>(this Result<T> source, Func<T, Result<U>> bind)
	where T : notnull
	where U : notnull
{
	return source switch
	{
		Success<T>(var v) => bind(v),
		Failure(var err) => new Failure(err),
	};
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj --filter "FullyQualifiedName~BindTests"`

Expected: 3 passed.

- [ ] **Step 5: Stage**

Run: `git add Norse.Primitives/src/Norse.Primitives/ResultExtensions/Sync.cs Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/SyncTests.cs`

Proposed commit message: `feat(svartalfheim): add Bind extension`

---

### Task 16: `Match` extension method

**Files:**
- Modify: `Norse.Primitives/src/Norse.Primitives/ResultExtensions/Sync.cs`
- Modify: `Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/SyncTests.cs`

- [ ] **Step 1: Add failing tests**

Append:

```csharp
public sealed class MatchTests
{
	[Fact]
	public void Match_should_invoke_onSuccess_for_Success()
	{
		Result<int> source = new Success<int>(7);
		string s = source.Match(v => $"ok-{v}", err => "fail");
		s.ShouldBe("ok-7");
	}

	[Fact]
	public void Match_should_invoke_onFailure_for_Failure()
	{
		Result<int> source = new Failure(new Error(new ParseError("x", "Int32", null, null)));
		string s = source.Match(v => "ok", err => "fail");
		s.ShouldBe("fail");
	}
}
```

- [ ] **Step 2: Run, verify failing build**

Run: `dotnet test ... --filter "FullyQualifiedName~MatchTests"`

- [ ] **Step 3: Add `Match` to `Sync.cs`**

```csharp
public static U Match<T, U>(
	this Result<T> source,
	Func<T, U> onSuccess,
	Func<Error, U> onFailure)
	where T : notnull
{
	return source switch
	{
		Success<T>(var v) => onSuccess(v),
		Failure(var err) => onFailure(err),
	};
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test ... --filter "FullyQualifiedName~MatchTests"`

- [ ] **Step 5: Stage**

Run: `git add Norse.Primitives/src/Norse.Primitives/ResultExtensions/Sync.cs Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/SyncTests.cs`

Proposed commit message: `feat(svartalfheim): add Match extension`

---

### Task 17: `GetValueOrThrow` + `ResultFailureException`

**Files:**
- Create: `Norse.Primitives/src/Norse.Primitives/ResultFailureException.cs`
- Modify: `Norse.Primitives/src/Norse.Primitives/ResultExtensions/Sync.cs`
- Modify: `Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/SyncTests.cs`

- [ ] **Step 1: Add failing tests**

```csharp
public sealed class GetValueOrThrowTests
{
	[Fact]
	public void Should_return_value_for_Success()
	{
		Result<int> source = new Success<int>(42);
		source.GetValueOrThrow().ShouldBe(42);
	}

	[Fact]
	public void Should_throw_ResultFailureException_for_Failure()
	{
		Error err = new Error(new ParseError("x", "Int32", null, null));
		Result<int> source = new Failure(err);
		Should.Throw<ResultFailureException>(() => source.GetValueOrThrow())
			.Error.ShouldBe(err);
	}
}
```

- [ ] **Step 2: Run, verify failing build**

- [ ] **Step 3: Create `ResultFailureException.cs`**

```csharp
using System;

namespace Norse.Primitives;

public sealed class ResultFailureException : Exception
{
	public ResultFailureException(Error error)
		: base($"Result was Failure: {error}")
	{
		Error = error;
	}

	public Error Error { get; }
}
```

- [ ] **Step 4: Add `GetValueOrThrow` to `Sync.cs`**

```csharp
public static T GetValueOrThrow<T>(this Result<T> source)
	where T : notnull
{
	return source switch
	{
		Success<T>(var v) => v,
		Failure(var err) => throw new ResultFailureException(err),
	};
}
```

- [ ] **Step 5: Run, verify pass**

- [ ] **Step 6: Stage**

Run: `git add Norse.Primitives/src/Norse.Primitives/ResultFailureException.cs Norse.Primitives/src/Norse.Primitives/ResultExtensions/Sync.cs Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/SyncTests.cs`

Proposed commit message: `feat(svartalfheim): add GetValueOrThrow and ResultFailureException`

---

### Task 18: `GetValueOrDefault`

**Files:**
- Modify: `Norse.Primitives/src/Norse.Primitives/ResultExtensions/Sync.cs`
- Modify: `Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/SyncTests.cs`

- [ ] **Step 1: Add failing tests**

```csharp
public sealed class GetValueOrDefaultTests
{
	[Fact]
	public void Should_return_value_for_Success()
	{
		Result<int> source = new Success<int>(42);
		source.GetValueOrDefault(-1).ShouldBe(42);
	}

	[Fact]
	public void Should_return_fallback_for_Failure()
	{
		Result<int> source = new Failure(new Error(new ParseError("x", "Int32", null, null)));
		source.GetValueOrDefault(-1).ShouldBe(-1);
	}
}
```

- [ ] **Step 2: Run, verify failing build**

- [ ] **Step 3: Add `GetValueOrDefault` to `Sync.cs`**

```csharp
public static T GetValueOrDefault<T>(this Result<T> source, T fallback)
	where T : notnull
{
	return source switch
	{
		Success<T>(var v) => v,
		Failure => fallback,
	};
}
```

- [ ] **Step 4: Run, verify pass**

- [ ] **Step 5: Stage**

Run: `git add Norse.Primitives/src/Norse.Primitives/ResultExtensions/Sync.cs Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/SyncTests.cs`

Proposed commit message: `feat(svartalfheim): add GetValueOrDefault extension`

---

### Task 19: `IsSuccess` / `IsFailure` boolean accessors

**Files:**
- Modify: `Norse.Primitives/src/Norse.Primitives/ResultExtensions/Sync.cs`
- Modify: `Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/SyncTests.cs`

- [ ] **Step 1: Add failing tests**

```csharp
public sealed class IsSuccessFailureTests
{
	[Fact]
	public void IsSuccess_should_be_true_for_Success()
	{
		Result<int> source = new Success<int>(42);
		source.IsSuccess().ShouldBeTrue();
		source.IsFailure().ShouldBeFalse();
	}

	[Fact]
	public void IsSuccess_should_be_false_for_Failure()
	{
		Result<int> source = new Failure(new Error(new ParseError("x", "Int32", null, null)));
		source.IsSuccess().ShouldBeFalse();
		source.IsFailure().ShouldBeTrue();
	}
}
```

- [ ] **Step 2: Run, verify failing build**

- [ ] **Step 3: Add to `Sync.cs`**

```csharp
/// <summary>
/// Returns true when the result is a <see cref="Success{T}"/>.
/// <para>
/// Note: a bare <c>IsSuccess()</c> call does NOT satisfy the <c>[MustConsume]</c> obligation
/// (see <c>YGG201</c>). To consume the result, extract the value via pattern match,
/// <see cref="Match"/>, <see cref="GetValueOrThrow"/>, or <see cref="GetValueOrDefault"/>,
/// or explicitly discard with <c>_ = result;</c>.
/// </para>
/// </summary>
public static bool IsSuccess<T>(this Result<T> source)
	where T : notnull
	=> source is Success<T>;

/// <summary>
/// Returns true when the result is a <see cref="Failure"/>.
/// <para>See <see cref="IsSuccess"/> for the consumption-obligation note.</para>
/// </summary>
public static bool IsFailure<T>(this Result<T> source)
	where T : notnull
	=> source is Failure;
```

- [ ] **Step 4: Run, verify pass**

- [ ] **Step 5: Stage**

Run: `git add Norse.Primitives/src/Norse.Primitives/ResultExtensions/Sync.cs Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/SyncTests.cs`

Proposed commit message: `feat(svartalfheim): add IsSuccess and IsFailure boolean accessors`

---

## Phase 5 — Async Composition

### Task 20: `MapAsync`, `BindAsync`, `MatchAsync`

**Files:**
- Create: `Norse.Primitives/src/Norse.Primitives/ResultExtensions/Async.cs`
- Create: `Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/AsyncTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace Norse.Primitives.Tests.ResultExtensions;

public sealed class AsyncTests
{
	[Fact]
	public async Task MapAsync_should_transform_Success_value()
	{
		Task<Result<int>> source = Task.FromResult<Result<int>>(new Success<int>(2));
		Result<int> result = await source.MapAsync(v => Task.FromResult(v * 21));
		(result is Success<int>(42)).ShouldBeTrue();
	}

	[Fact]
	public async Task MapAsync_should_pass_through_Failure()
	{
		Task<Result<int>> source = Task.FromResult<Result<int>>(new Failure(new Error(new ParseError("x", "Int32", null, null))));
		Result<int> result = await source.MapAsync(v => Task.FromResult(v * 2));
		(result is Failure).ShouldBeTrue();
	}

	[Fact]
	public async Task BindAsync_should_chain_Success_to_a_new_async_Result()
	{
		Task<Result<int>> source = Task.FromResult<Result<int>>(new Success<int>(2));
		Result<string> result = await source.BindAsync(v => Task.FromResult<Result<string>>(new Success<string>($"v-{v}")));
		(result is Success<string> s && s.Value == "v-2").ShouldBeTrue();
	}

	[Fact]
	public async Task MatchAsync_should_invoke_onSuccess_for_Success()
	{
		Task<Result<int>> source = Task.FromResult<Result<int>>(new Success<int>(7));
		string s = await source.MatchAsync(v => Task.FromResult($"ok-{v}"), err => Task.FromResult("fail"));
		s.ShouldBe("ok-7");
	}

	[Fact]
	public async Task MatchAsync_should_invoke_onFailure_for_Failure()
	{
		Task<Result<int>> source = Task.FromResult<Result<int>>(new Failure(new Error(new ParseError("x", "Int32", null, null))));
		string s = await source.MatchAsync(v => Task.FromResult("ok"), err => Task.FromResult("fail"));
		s.ShouldBe("fail");
	}
}
```

- [ ] **Step 2: Run, verify failing build**

- [ ] **Step 3: Implement `Async.cs`**

```csharp
using System;
using System.Threading.Tasks;

namespace Norse.Primitives;

public static class ResultAsyncExtensions
{
	public static async Task<Result<U>> MapAsync<T, U>(
		this Task<Result<T>> source,
		Func<T, Task<U>> map)
		where T : notnull
		where U : notnull
	{
		Result<T> r = await source.ConfigureAwait(false);
		return r switch
		{
			Success<T>(var v) => new Success<U>(await map(v).ConfigureAwait(false)),
			Failure(var err) => new Failure(err),
		};
	}

	public static async Task<Result<U>> BindAsync<T, U>(
		this Task<Result<T>> source,
		Func<T, Task<Result<U>>> bind)
		where T : notnull
		where U : notnull
	{
		Result<T> r = await source.ConfigureAwait(false);
		return r switch
		{
			Success<T>(var v) => await bind(v).ConfigureAwait(false),
			Failure(var err) => new Failure(err),
		};
	}

	public static async Task<U> MatchAsync<T, U>(
		this Task<Result<T>> source,
		Func<T, Task<U>> onSuccess,
		Func<Error, Task<U>> onFailure)
		where T : notnull
	{
		Result<T> r = await source.ConfigureAwait(false);
		return r switch
		{
			Success<T>(var v) => await onSuccess(v).ConfigureAwait(false),
			Failure(var err) => await onFailure(err).ConfigureAwait(false),
		};
	}
}
```

- [ ] **Step 4: Run, verify pass**

- [ ] **Step 5: Stage**

Run: `git add Norse.Primitives/src/Norse.Primitives/ResultExtensions/Async.cs Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/AsyncTests.cs`

Proposed commit message: `feat(svartalfheim): add async composition extensions (MapAsync, BindAsync, MatchAsync)`

---

## Phase 6 — Present Composition

### Task 21: `MapPresent`, `BindPresent`, `MatchPresent` on `Result<T>?`

**Files:**
- Create: `Norse.Primitives/src/Norse.Primitives/ResultExtensions/Present.cs`
- Create: `Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/PresentTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using Shouldly;
using Xunit;

namespace Norse.Primitives.Tests.ResultExtensions;

public sealed class PresentTests
{
	[Fact]
	public void MapPresent_should_pass_through_null()
	{
		Result<int>? source = null;
		Result<int>? mapped = source.MapPresent(v => v * 2);
		mapped.HasValue.ShouldBeFalse();
	}

	[Fact]
	public void MapPresent_should_transform_Success_when_present()
	{
		Result<int>? source = new Success<int>(2);
		Result<int>? mapped = source.MapPresent(v => v * 21);
		mapped!.Value.ShouldBe<Result<int>>(new Success<int>(42));
	}

	[Fact]
	public void MapPresent_should_pass_through_Failure_when_present()
	{
		Result<int>? source = new Failure(new Error(new ParseError("x", "Int32", null, null)));
		Result<int>? mapped = source.MapPresent(v => v * 2);
		(mapped!.Value is Failure).ShouldBeTrue();
	}

	[Fact]
	public void BindPresent_should_chain_when_Success_present()
	{
		Result<int>? source = new Success<int>(2);
		Result<string>? bound = source.BindPresent(v => (Result<string>)new Success<string>($"v-{v}"));
		bound.HasValue.ShouldBeTrue();
		(bound!.Value is Success<string> s && s.Value == "v-2").ShouldBeTrue();
	}

	[Fact]
	public void BindPresent_should_pass_through_null()
	{
		Result<int>? source = null;
		Result<string>? bound = source.BindPresent(v => (Result<string>)new Success<string>("x"));
		bound.HasValue.ShouldBeFalse();
	}

	[Fact]
	public void MatchPresent_should_invoke_onAbsent_for_null()
	{
		Result<int>? source = null;
		string s = source.MatchPresent(v => "success", err => "failure", "absent");
		s.ShouldBe("absent");
	}

	[Fact]
	public void MatchPresent_should_invoke_onSuccess_when_Success_present()
	{
		Result<int>? source = new Success<int>(7);
		string s = source.MatchPresent(v => $"ok-{v}", err => "fail", "absent");
		s.ShouldBe("ok-7");
	}

	[Fact]
	public void MatchPresent_should_invoke_onFailure_when_Failure_present()
	{
		Result<int>? source = new Failure(new Error(new ParseError("x", "Int32", null, null)));
		string s = source.MatchPresent(v => "ok", err => "fail", "absent");
		s.ShouldBe("fail");
	}
}
```

- [ ] **Step 2: Run, verify failing build**

- [ ] **Step 3: Implement `Present.cs`**

```csharp
using System;

namespace Norse.Primitives;

public static class ResultPresentExtensions
{
	public static Result<U>? MapPresent<T, U>(this Result<T>? source, Func<T, U> map)
		where T : notnull
		where U : notnull
	{
		if (!source.HasValue) return null;
		return source.Value.Map(map);
	}

	public static Result<U>? BindPresent<T, U>(this Result<T>? source, Func<T, Result<U>> bind)
		where T : notnull
		where U : notnull
	{
		if (!source.HasValue) return null;
		return source.Value.Bind(bind);
	}

	public static U MatchPresent<T, U>(
		this Result<T>? source,
		Func<T, U> onSuccess,
		Func<Error, U> onFailure,
		U onAbsent)
		where T : notnull
	{
		if (!source.HasValue) return onAbsent;
		return source.Value.Match(onSuccess, onFailure);
	}
}
```

- [ ] **Step 4: Run, verify pass**

- [ ] **Step 5: Stage**

Run: `git add Norse.Primitives/src/Norse.Primitives/ResultExtensions/Present.cs Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/PresentTests.cs`

Proposed commit message: `feat(svartalfheim): add Present composition (MapPresent, BindPresent, MatchPresent)`

---

## Phase 7 — Aggregation

### Task 22: `Combine` and `Collect` over `IEnumerable<Result<T>>`

**Files:**
- Create: `Norse.Primitives/src/Norse.Primitives/ResultExtensions/Aggregate.cs`
- Create: `Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/AggregateTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System.Collections.Immutable;
using Shouldly;
using Xunit;

namespace Norse.Primitives.Tests.ResultExtensions;

public sealed class CombineTests
{
	[Fact]
	public void Combine_should_short_circuit_on_first_Failure()
	{
		Result<int>[] inputs = [
			new Success<int>(1),
			new Failure(new Error(new ParseError("x", "Int32", null, null))),
			new Success<int>(3),
		];
		Result<ImmutableArray<int>> combined = Result.Combine(inputs);
		(combined is Failure).ShouldBeTrue();
	}

	[Fact]
	public void Combine_should_return_all_values_when_every_Result_is_Success()
	{
		Result<int>[] inputs = [
			new Success<int>(1),
			new Success<int>(2),
			new Success<int>(3),
		];
		Result<ImmutableArray<int>> combined = Result.Combine(inputs);
		(combined is Success<ImmutableArray<int>> s && s.Value.SequenceEqual([1, 2, 3])).ShouldBeTrue();
	}

	[Fact]
	public void Collect_should_accumulate_every_Failure_into_AggregateError()
	{
		Error e1 = new(new ValidationError("A", "R1", null));
		Error e2 = new(new ValidationError("B", "R2", null));
		Result<int>[] inputs = [
			new Success<int>(1),
			new Failure(e1),
			new Success<int>(3),
			new Failure(e2),
		];
		Result<ImmutableArray<int>> collected = Result.Collect(inputs);
		(collected is Failure(var err) && err is AggregateError ae && ae.Errors.Length == 2).ShouldBeTrue();
	}

	[Fact]
	public void Collect_should_return_Success_when_every_Result_is_Success()
	{
		Result<int>[] inputs = [
			new Success<int>(1),
			new Success<int>(2),
		];
		Result<ImmutableArray<int>> collected = Result.Collect(inputs);
		(collected is Success<ImmutableArray<int>> s && s.Value.SequenceEqual([1, 2])).ShouldBeTrue();
	}
}
```

- [ ] **Step 2: Run, verify failing build**

- [ ] **Step 3: Implement `Aggregate.cs`**

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Norse.Primitives;

public static class Result
{
	public static Result<ImmutableArray<T>> Combine<T>(IEnumerable<Result<T>> results)
		where T : notnull
	{
		ImmutableArray<T>.Builder builder = ImmutableArray.CreateBuilder<T>();
		foreach (Result<T> r in results)
		{
			switch (r)
			{
				case Success<T>(var v):
					builder.Add(v);
					break;
				case Failure f:
					return f;
			}
		}
		return new Success<ImmutableArray<T>>(builder.ToImmutable());
	}

	public static Result<ImmutableArray<T>> Collect<T>(IEnumerable<Result<T>> results)
		where T : notnull
	{
		ImmutableArray<T>.Builder values = ImmutableArray.CreateBuilder<T>();
		ImmutableArray<Error>.Builder errors = ImmutableArray.CreateBuilder<Error>();
		foreach (Result<T> r in results)
		{
			switch (r)
			{
				case Success<T>(var v):
					values.Add(v);
					break;
				case Failure(var e):
					errors.Add(e);
					break;
			}
		}
		if (errors.Count > 0)
		{
			return new Failure(new Error(new AggregateError(errors.ToImmutable())));
		}
		return new Success<ImmutableArray<T>>(values.ToImmutable());
	}
}
```

- [ ] **Step 4: Run, verify pass**

- [ ] **Step 5: Stage**

Run: `git add Norse.Primitives/src/Norse.Primitives/ResultExtensions/Aggregate.cs Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/AggregateTests.cs`

Proposed commit message: `feat(svartalfheim): add Combine and Collect aggregation`

---

### Task 23: Tuple combinators arity 2–8

**Files:**
- Modify: `Norse.Primitives/src/Norse.Primitives/ResultExtensions/Aggregate.cs`
- Modify: `Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/AggregateTests.cs`

- [ ] **Step 1: Add failing tests (arity 2 and arity 8 as representative cases)**

```csharp
public sealed class TupleCombineTests
{
	[Fact]
	public void Combine_arity2_should_pair_two_Successes()
	{
		Result<(int, string)> combined = Result.Combine<int, string>(
			new Success<int>(1),
			new Success<string>("two"));
		(combined is Success<(int, string)> s && s.Value == (1, "two")).ShouldBeTrue();
	}

	[Fact]
	public void Combine_arity2_should_return_first_Failure()
	{
		Error err = new(new ParseError("x", "Int32", null, null));
		Result<(int, string)> combined = Result.Combine<int, string>(
			new Failure(err),
			new Success<string>("two"));
		(combined is Failure(var e) && e is ParseError).ShouldBeTrue();
	}

	[Fact]
	public void Combine_arity8_should_aggregate_eight_Successes()
	{
		Result<(int, int, int, int, int, int, int, int)> combined = Result.Combine<int, int, int, int, int, int, int, int>(
			new Success<int>(1),
			new Success<int>(2),
			new Success<int>(3),
			new Success<int>(4),
			new Success<int>(5),
			new Success<int>(6),
			new Success<int>(7),
			new Success<int>(8));
		(combined is Success<(int, int, int, int, int, int, int, int)> s && s.Value == (1, 2, 3, 4, 5, 6, 7, 8)).ShouldBeTrue();
	}
}
```

- [ ] **Step 2: Run, verify failing build**

- [ ] **Step 3: Add tuple combinators to `Aggregate.cs` (arity 2 through 8)**

Add inside `public static partial class Result` (make the class `partial`):

```csharp
public static Result<(T1, T2)> Combine<T1, T2>(Result<T1> r1, Result<T2> r2)
	where T1 : notnull where T2 : notnull
{
	if (r1 is Failure(var e1)) return new Failure(e1);
	if (r2 is Failure(var e2)) return new Failure(e2);
	if (r1 is Success<T1>(var v1) && r2 is Success<T2>(var v2))
	{
		return new Success<(T1, T2)>((v1, v2));
	}
	throw new System.InvalidOperationException("unreachable");
}

public static Result<(T1, T2, T3)> Combine<T1, T2, T3>(Result<T1> r1, Result<T2> r2, Result<T3> r3)
	where T1 : notnull where T2 : notnull where T3 : notnull
{
	if (r1 is Failure(var e1)) return new Failure(e1);
	if (r2 is Failure(var e2)) return new Failure(e2);
	if (r3 is Failure(var e3)) return new Failure(e3);
	if (r1 is Success<T1>(var v1)
		&& r2 is Success<T2>(var v2)
		&& r3 is Success<T3>(var v3))
	{
		return new Success<(T1, T2, T3)>((v1, v2, v3));
	}
	throw new System.InvalidOperationException("unreachable");
}

// ... continue for arity 4, 5, 6, 7, 8 following the same template ...

public static Result<(T1, T2, T3, T4, T5, T6, T7, T8)> Combine<T1, T2, T3, T4, T5, T6, T7, T8>(
	Result<T1> r1, Result<T2> r2, Result<T3> r3, Result<T4> r4,
	Result<T5> r5, Result<T6> r6, Result<T7> r7, Result<T8> r8)
	where T1 : notnull where T2 : notnull where T3 : notnull where T4 : notnull
	where T5 : notnull where T6 : notnull where T7 : notnull where T8 : notnull
{
	if (r1 is Failure(var e1)) return new Failure(e1);
	if (r2 is Failure(var e2)) return new Failure(e2);
	if (r3 is Failure(var e3)) return new Failure(e3);
	if (r4 is Failure(var e4)) return new Failure(e4);
	if (r5 is Failure(var e5)) return new Failure(e5);
	if (r6 is Failure(var e6)) return new Failure(e6);
	if (r7 is Failure(var e7)) return new Failure(e7);
	if (r8 is Failure(var e8)) return new Failure(e8);
	if (r1 is Success<T1>(var v1)
		&& r2 is Success<T2>(var v2)
		&& r3 is Success<T3>(var v3)
		&& r4 is Success<T4>(var v4)
		&& r5 is Success<T5>(var v5)
		&& r6 is Success<T6>(var v6)
		&& r7 is Success<T7>(var v7)
		&& r8 is Success<T8>(var v8))
	{
		return new Success<(T1, T2, T3, T4, T5, T6, T7, T8)>((v1, v2, v3, v4, v5, v6, v7, v8));
	}
	throw new System.InvalidOperationException("unreachable");
}
```

Write the arity 4–7 overloads following the same template; pattern is mechanical.

- [ ] **Step 4: Run, verify pass**

- [ ] **Step 5: Stage**

Run: `git add Norse.Primitives/src/Norse.Primitives/ResultExtensions/Aggregate.cs Norse.Primitives/tests/Norse.Primitives.Tests/ResultExtensions/AggregateTests.cs`

Proposed commit message: `feat(svartalfheim): add tuple combinators arity 2–8`

---

## Phase 8 — `[MustConsume]` Attribute

### Task 24: Promote `MustConsumeAttribute` to its own file

**Files:**
- Create: `Norse.Primitives/src/Norse.Primitives/MustConsumeAttribute.cs`
- Modify: `Norse.Primitives/src/Norse.Primitives/Result.cs` (remove temporary stub)
- Create: `Norse.Primitives/tests/Norse.Primitives.Tests/MustConsumeAttributeTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System;
using System.Reflection;
using Shouldly;
using Xunit;

namespace Norse.Primitives.Tests;

public sealed class MustConsumeAttributeTests
{
	[Fact]
	public void Attribute_should_be_public()
	{
		typeof(MustConsumeAttribute).IsPublic.ShouldBeTrue();
	}

	[Fact]
	public void Attribute_should_be_sealed()
	{
		typeof(MustConsumeAttribute).IsSealed.ShouldBeTrue();
	}

	[Fact]
	public void Attribute_should_target_struct_or_class()
	{
		AttributeUsageAttribute usage = typeof(MustConsumeAttribute)
			.GetCustomAttribute<AttributeUsageAttribute>(inherit: false)!;
		usage.ValidOn.ShouldBe(AttributeTargets.Struct | AttributeTargets.Class);
	}

	[Fact]
	public void Result_T_should_be_annotated_with_MustConsume()
	{
		typeof(Result<int>)
			.GetCustomAttribute<MustConsumeAttribute>(inherit: false)
			.ShouldNotBeNull();
	}
}
```

- [ ] **Step 2: Run, verify failing test (`Attribute_should_be_public` fails because the stub is internal)**

- [ ] **Step 3: Create `MustConsumeAttribute.cs`**

```csharp
using System;

namespace Norse.Primitives;

/// <summary>
/// Marks a type whose values must be consumed at every call site.
/// <para>
/// Enforced by the <c>YGG201</c> diagnostic in <c>Norse.Primitives.Architecture</c>:
/// a return value of an annotated type that is discarded with no consumption is a compile error.
/// Consumption includes pattern match, calling a composition method (<c>Map</c>, <c>Bind</c>,
/// <c>Match</c>, <c>GetValueOrThrow</c>, etc.), returning the value upward, storing it in a
/// field or property, or explicitly discarding with <c>_ = value;</c>.
/// </para>
/// <para>
/// A bare <c>IsSuccess()</c> / <c>IsFailure()</c> read alone is NOT consumption — the consumer
/// must extract the value or error, or explicitly discard.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
public sealed class MustConsumeAttribute : Attribute;
```

- [ ] **Step 4: Remove the temporary stub from `Result.cs`**

Open `Norse.Primitives/src/Norse.Primitives/Result.cs` and delete the bottom block:

```csharp
[System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.Class)]
internal sealed class MustConsumeAttribute : System.Attribute;
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj`

Expected: all green; in particular the 4 `MustConsumeAttributeTests` pass.

- [ ] **Step 6: Stage**

Run: `git add Norse.Primitives/src/Norse.Primitives/MustConsumeAttribute.cs Norse.Primitives/src/Norse.Primitives/Result.cs Norse.Primitives/tests/Norse.Primitives.Tests/MustConsumeAttributeTests.cs`

Proposed commit message: `feat(svartalfheim): promote MustConsumeAttribute to public own-file type`

---

## Phase 9 — AOT Smoke Tests

### Task 25: Create AOT smoke-test project

**Files:**
- Create: `Norse.Primitives/tests/Norse.Primitives.Aot.SmokeTests/Norse.Primitives.Aot.SmokeTests.csproj`
- Create: `Norse.Primitives/tests/Norse.Primitives.Aot.SmokeTests/Program.cs`

- [ ] **Step 1: Create csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<RootNamespace>Norse.Primitives.Aot.SmokeTests</RootNamespace>
		<AssemblyName>Norse.Primitives.Aot.SmokeTests</AssemblyName>
		<IsPackable>false</IsPackable>
		<NorseTier>ExternalCli</NorseTier>
	</PropertyGroup>

	<ItemGroup>
		<ProjectReference Include="..\..\src\Norse.Primitives\Norse.Primitives.csproj" />
	</ItemGroup>

</Project>
```

- [ ] **Step 2: Create `Program.cs`**

```csharp
using System;
using System.Globalization;
using System.Net;
using System.Numerics;
using Norse.Primitives;

CultureInfo ci = CultureInfo.InvariantCulture;
int failures = 0;

static void Assert(bool condition, string label, ref int failures)
{
	if (!condition)
	{
		Console.Error.WriteLine($"AOT smoke FAIL: {label}");
		failures++;
	}
	else
	{
		Console.WriteLine($"AOT smoke OK: {label}");
	}
}

Assert(Parser.ParseRequired<int>("42", ci) is Success<int>(42), "int", ref failures);
Assert(Parser.ParseRequired<long>("9000000000", ci) is Success<long>(9000000000L), "long", ref failures);
Assert(Parser.ParseRequired<decimal>("3.14", ci) is Success<decimal>(3.14m), "decimal", ref failures);
Assert(Parser.ParseRequired<Guid>("00000000-0000-0000-0000-000000000001", ci) is Success<Guid>, "Guid", ref failures);
Assert(Parser.ParseRequired<DateOnly>("2026-05-20", ci) is Success<DateOnly>, "DateOnly", ref failures);
Assert(Parser.ParseRequired<DateTimeOffset>("2026-05-20T12:34:56+00:00", ci) is Success<DateTimeOffset>, "DateTimeOffset", ref failures);
Assert(Parser.ParseRequired<TimeSpan>("01:02:03", ci) is Success<TimeSpan>, "TimeSpan", ref failures);
Assert(Parser.ParseRequired<BigInteger>("99999999999999999999", ci) is Success<BigInteger>, "BigInteger", ref failures);
Assert(Parser.ParseRequired<IPAddress>("127.0.0.1", ci) is Success<IPAddress>, "IPAddress", ref failures);
Assert(Parser.ParseRequired<IPNetwork>("10.0.0.0/8", ci) is Success<IPNetwork>, "IPNetwork", ref failures);
Assert(Parser.ParseRequired<IPEndPoint>("127.0.0.1:8080", ci) is Success<IPEndPoint>, "IPEndPoint", ref failures);

Assert(Parser.ParseOptional<int>("", ci) is null, "optional empty -> null", ref failures);
Assert(Parser.ParseOptional<int>("42", ci) is Result<int> { } r && r is Success<int>(42), "optional present -> Success", ref failures);

return failures;
```

- [ ] **Step 3: Add the project to the solution**

Run: `dotnet sln Norse.Primitives/Norse.Primitives.slnx add Norse.Primitives/tests/Norse.Primitives.Aot.SmokeTests/Norse.Primitives.Aot.SmokeTests.csproj`

- [ ] **Step 4: Build under JIT to confirm correctness first**

Run: `dotnet run --project Norse.Primitives/tests/Norse.Primitives.Aot.SmokeTests/Norse.Primitives.Aot.SmokeTests.csproj`

Expected: every line `AOT smoke OK: <label>`, exit code 0.

- [ ] **Step 5: Publish AOT and run the published binary**

Run:
```powershell
dotnet publish Norse.Primitives/tests/Norse.Primitives.Aot.SmokeTests/Norse.Primitives.Aot.SmokeTests.csproj -c Release -o ./aot-out
./aot-out/Norse.Primitives.Aot.SmokeTests.exe
```

Expected: same `AOT smoke OK` output, exit 0. If the publish step emits trim warnings, capture them and resolve before continuing. Common fixes: add `[DynamicallyAccessedMembers(...)]` annotations or remove the offending type from the BCL set in the spec.

- [ ] **Step 6: Clean up the AOT publish output**

Run: `rm -r -force ./aot-out`

- [ ] **Step 7: Stage**

Run: `git add Norse.Primitives/tests/Norse.Primitives.Aot.SmokeTests/ Norse.Primitives/Norse.Primitives.slnx`

Proposed commit message: `test(svartalfheim): add AOT smoke-test project covering the parser surface`

---

## Phase 10 — Benchmarks

### Task 26: Create benchmark project

**Files:**
- Create: `Norse.Primitives/benchmarks/Norse.Primitives.Benchmarks/Norse.Primitives.Benchmarks.csproj`
- Create: `Norse.Primitives/benchmarks/Norse.Primitives.Benchmarks/Program.cs`

- [ ] **Step 1: Create csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<RootNamespace>Norse.Primitives.Benchmarks</RootNamespace>
		<AssemblyName>Norse.Primitives.Benchmarks</AssemblyName>
		<IsPackable>false</IsPackable>
		<NorseTier>Server</NorseTier>
		<Configuration Condition="'$(Configuration)' == ''">Release</Configuration>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="BenchmarkDotNet" Version="*" />
		<ProjectReference Include="..\..\src\Norse.Primitives\Norse.Primitives.csproj" />
	</ItemGroup>

</Project>
```

- [ ] **Step 2: Create `Program.cs`**

```csharp
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

public partial class Program;
```

- [ ] **Step 3: Add to solution**

Run: `dotnet sln Norse.Primitives/Norse.Primitives.slnx add Norse.Primitives/benchmarks/Norse.Primitives.Benchmarks/Norse.Primitives.Benchmarks.csproj`

- [ ] **Step 4: Verify build**

Run: `dotnet build Norse.Primitives/benchmarks/Norse.Primitives.Benchmarks/Norse.Primitives.Benchmarks.csproj -c Release`

Expected: build succeeds (no benchmark classes yet; the runner will report "no benchmarks found" if executed).

- [ ] **Step 5: Stage**

Run: `git add Norse.Primitives/benchmarks/Norse.Primitives.Benchmarks/ Norse.Primitives/Norse.Primitives.slnx`

Proposed commit message: `feat(svartalfheim): add BenchmarkDotNet project skeleton`

---

### Task 27: Parser benchmarks

**Files:**
- Create: `Norse.Primitives/benchmarks/Norse.Primitives.Benchmarks/ParserBenchmarks.cs`

- [ ] **Step 1: Write benchmarks**

```csharp
using System.Globalization;
using BenchmarkDotNet.Attributes;
using Norse.Primitives;

namespace Norse.Primitives.Benchmarks;

[MemoryDiagnoser]
public class ParserBenchmarks
{
	private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

	[Benchmark] public Result<int>             Int_success()           => Parser.ParseRequired<int>("42", Ci);
	[Benchmark] public Result<int>             Int_failure()           => Parser.ParseRequired<int>("abc", Ci);
	[Benchmark] public Result<decimal>         Decimal_success()       => Parser.ParseRequired<decimal>("3.14", Ci);
	[Benchmark] public Result<System.DateOnly> DateOnly_success()      => Parser.ParseRequired<System.DateOnly>("2026-05-20", Ci);
	[Benchmark] public Result<System.Guid>     Guid_success()          => Parser.ParseRequired<System.Guid>("00000000-0000-0000-0000-000000000001", Ci);
	[Benchmark] public Result<int>?            Int_optional_empty()    => Parser.ParseOptional<int>("", Ci);
	[Benchmark] public Result<int>?            Int_optional_present()  => Parser.ParseOptional<int>("42", Ci);
}
```

- [ ] **Step 2: Run benchmarks** (one-off, not part of CI)

Run:
```powershell
dotnet run -c Release --project Norse.Primitives/benchmarks/Norse.Primitives.Benchmarks/Norse.Primitives.Benchmarks.csproj -- --filter "*ParserBenchmarks*"
```

Expected: completes; success-path benchmarks report `0 B` allocated. If any success-path benchmark allocates, investigate (the non-boxing access pattern or a closure may not be applied correctly).

- [ ] **Step 3: Stage**

Run: `git add Norse.Primitives/benchmarks/Norse.Primitives.Benchmarks/ParserBenchmarks.cs`

Proposed commit message: `bench(svartalfheim): add parser benchmarks with MemoryDiagnoser`

---

### Task 28: Composition benchmarks (Map/Bind/Match chains)

**Files:**
- Create: `Norse.Primitives/benchmarks/Norse.Primitives.Benchmarks/CompositionBenchmarks.cs`

- [ ] **Step 1: Write benchmarks**

```csharp
using BenchmarkDotNet.Attributes;
using Norse.Primitives;

namespace Norse.Primitives.Benchmarks;

[MemoryDiagnoser]
public class CompositionBenchmarks
{
	private static readonly Result<int> Source = new Success<int>(2);
	private static readonly Result<int> SourceFailure = new Failure(new Error(new ParseError("x", "Int32", null, null)));

	[Benchmark] public Result<int> Map_single()      => Source.Map(v => v * 2);
	[Benchmark] public Result<int> Map_depth3()      => Source.Map(v => v + 1).Map(v => v + 1).Map(v => v + 1);
	[Benchmark] public Result<int> Map_depth5()      => Source.Map(v => v + 1).Map(v => v + 1).Map(v => v + 1).Map(v => v + 1).Map(v => v + 1);
	[Benchmark] public Result<int> Bind_single()     => Source.Bind(v => (Result<int>)new Success<int>(v * 2));
	[Benchmark] public Result<int> Bind_depth3()     => Source.Bind(v => (Result<int>)new Success<int>(v + 1)).Bind(v => (Result<int>)new Success<int>(v + 1)).Bind(v => (Result<int>)new Success<int>(v + 1));
	[Benchmark] public int         Match_success()  => Source.Match(v => v, _ => -1);
	[Benchmark] public int         Match_failure()  => SourceFailure.Match(v => v, _ => -1);
}
```

- [ ] **Step 2: Run** (one-off)

Run: `dotnet run -c Release --project Norse.Primitives/benchmarks/Norse.Primitives.Benchmarks/Norse.Primitives.Benchmarks.csproj -- --filter "*CompositionBenchmarks*"`

Expected: completes; `Map_single` and `Bind_single` should be 0 B allocated.

- [ ] **Step 3: Stage**

Run: `git add Norse.Primitives/benchmarks/Norse.Primitives.Benchmarks/CompositionBenchmarks.cs`

Proposed commit message: `bench(svartalfheim): add composition benchmarks for Map/Bind/Match`

---

### Task 29: Aggregation benchmarks

**Files:**
- Create: `Norse.Primitives/benchmarks/Norse.Primitives.Benchmarks/AggregationBenchmarks.cs`

- [ ] **Step 1: Write benchmarks**

```csharp
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Norse.Primitives;

namespace Norse.Primitives.Benchmarks;

[MemoryDiagnoser]
public class AggregationBenchmarks
{
	[Params(10, 100, 1000)]
	public int Size;

	private Result<int>[] _allSuccess = null!;
	private Result<int>[] _mixed = null!;

	[GlobalSetup]
	public void Setup()
	{
		_allSuccess = new Result<int>[Size];
		_mixed = new Result<int>[Size];
		for (int i = 0; i < Size; i++)
		{
			_allSuccess[i] = new Success<int>(i);
			_mixed[i] = (i % 3 == 0)
				? new Failure(new Error(new ValidationError($"F{i}", "R", null)))
				: new Success<int>(i);
		}
	}

	[Benchmark] public Result<System.Collections.Immutable.ImmutableArray<int>> Combine_all_success() => Result.Combine(_allSuccess);
	[Benchmark] public Result<System.Collections.Immutable.ImmutableArray<int>> Combine_mixed()       => Result.Combine(_mixed);
	[Benchmark] public Result<System.Collections.Immutable.ImmutableArray<int>> Collect_all_success() => Result.Collect(_allSuccess);
	[Benchmark] public Result<System.Collections.Immutable.ImmutableArray<int>> Collect_mixed()       => Result.Collect(_mixed);
}
```

- [ ] **Step 2: Run** (one-off)

Run: `dotnet run -c Release --project Norse.Primitives/benchmarks/Norse.Primitives.Benchmarks/Norse.Primitives.Benchmarks.csproj -- --filter "*AggregationBenchmarks*"`

Expected: allocation should be ~`Size * sizeof(int)` for `Combine_all_success`, plus the AggregateError's array for `Collect_mixed`.

- [ ] **Step 3: Stage**

Run: `git add Norse.Primitives/benchmarks/Norse.Primitives.Benchmarks/AggregationBenchmarks.cs`

Proposed commit message: `bench(svartalfheim): add aggregation benchmarks (Combine, Collect across sizes)`

---

## Phase 11 — Property Tests

### Task 30: Monad-law property tests

**Files:**
- Create: `Norse.Primitives/tests/Norse.Primitives.Tests/Properties/MonadLawProperties.cs`

- [ ] **Step 1: Write property tests**

```csharp
using FsCheck;
using FsCheck.Xunit;
using Norse.Primitives;

namespace Norse.Primitives.Tests.Properties;

public sealed class MonadLawProperties
{
	[Property]
	public bool Map_identity_law(int value)
	{
		Result<int> r = new Success<int>(value);
		Result<int> mapped = r.Map(v => v);
		return r.Equals(mapped);
	}

	[Property]
	public bool Map_composition_law(int value, int a, int b)
	{
		Result<int> r = new Success<int>(value);
		System.Func<int, int> f = v => v + a;
		System.Func<int, int> g = v => v * b;
		return r.Map(f).Map(g).Equals(r.Map(v => g(f(v))));
	}

	[Property]
	public bool Bind_left_identity_law(int value, int delta)
	{
		System.Func<int, Result<int>> f = v => new Success<int>(v + delta);
		Result<int> lhs = new Success<int>(value).Bind(f);
		Result<int> rhs = f(value);
		return lhs.Equals(rhs);
	}

	[Property]
	public bool Bind_right_identity_law(int value)
	{
		Result<int> r = new Success<int>(value);
		System.Func<int, Result<int>> ofSuccess = v => new Success<int>(v);
		return r.Bind(ofSuccess).Equals(r);
	}

	[Property]
	public bool Bind_associativity_law(int value, int a, int b)
	{
		Result<int> r = new Success<int>(value);
		System.Func<int, Result<int>> f = v => new Success<int>(v + a);
		System.Func<int, Result<int>> g = v => new Success<int>(v * b);
		Result<int> lhs = r.Bind(f).Bind(g);
		Result<int> rhs = r.Bind(v => f(v).Bind(g));
		return lhs.Equals(rhs);
	}
}
```

- [ ] **Step 2: Run**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj --filter "FullyQualifiedName~MonadLawProperties"`

Expected: 5 passed, each with 100 random iterations.

- [ ] **Step 3: Stage**

Run: `git add Norse.Primitives/tests/Norse.Primitives.Tests/Properties/MonadLawProperties.cs`

Proposed commit message: `test(svartalfheim): add FsCheck monad-law property tests`

---

### Task 31: Parser round-trip property tests

**Files:**
- Create: `Norse.Primitives/tests/Norse.Primitives.Tests/Properties/ParserRoundTripProperties.cs`

- [ ] **Step 1: Write property tests**

```csharp
using System.Globalization;
using FsCheck;
using FsCheck.Xunit;
using Norse.Primitives;

namespace Norse.Primitives.Tests.Properties;

public sealed class ParserRoundTripProperties
{
	private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

	[Property]
	public bool Int_round_trip(int value)
	{
		string s = value.ToString(Ci);
		Result<int> r = Parser.ParseRequired<int>(s, Ci);
		return r is Success<int> succ && succ.Value == value;
	}

	[Property]
	public bool Long_round_trip(long value)
	{
		string s = value.ToString(Ci);
		Result<long> r = Parser.ParseRequired<long>(s, Ci);
		return r is Success<long> succ && succ.Value == value;
	}

	[Property]
	public bool Decimal_round_trip(decimal value)
	{
		string s = value.ToString(Ci);
		Result<decimal> r = Parser.ParseRequired<decimal>(s, Ci);
		return r is Success<decimal> succ && succ.Value == value;
	}

	[Property]
	public bool Guid_round_trip()
	{
		System.Guid g = System.Guid.NewGuid();
		string s = g.ToString();
		Result<System.Guid> r = Parser.ParseRequired<System.Guid>(s, Ci);
		return r is Success<System.Guid> succ && succ.Value == g;
	}
}
```

- [ ] **Step 2: Run**

Run: `dotnet test Norse.Primitives/tests/Norse.Primitives.Tests/Norse.Primitives.Tests.csproj --filter "FullyQualifiedName~ParserRoundTripProperties"`

Expected: 4 passed.

- [ ] **Step 3: Stage**

Run: `git add Norse.Primitives/tests/Norse.Primitives.Tests/Properties/ParserRoundTripProperties.cs`

Proposed commit message: `test(svartalfheim): add FsCheck parser round-trip properties`

---

## Phase 12 — Documentation

### Task 32: Write `README.md`

**Files:**
- Modify: `Norse.Primitives/README.md`

- [ ] **Step 1: Replace placeholder with the real README**

```markdown
# Norse.Primitives

Foundational primitives for the Norse platform: `Result<T>`, a canonical `Error` union, a parser gateway over `ISpanParsable<T>`, and the `[MustConsume]` attribute.

## Why

Every boundary the platform crosses — file ingestion, HTTP, third-party APIs, message deserialization — must return a value the caller cannot ignore and cannot mishandle silently. Norse.Primitives provides the type-level enforcement: `Result<T>` is a closed union (`Success<T>` or `Failure(Error)`), `[MustConsume]` rejects discarded results at compile time, and the parser gateway makes culture-mandatory parsing the only available shape.

See `docs/Svartalfheim/specs/2026-05-20-svartalfheim-primitives-design.md` in the meta-repo for the full design rationale.

## Quick start

```csharp
using System.Globalization;
using Norse.Primitives;

CultureInfo ci = CultureInfo.InvariantCulture;

// Required field — empty input is a Failure.
Result<int> count = Parser.ParseRequired<int>(input, ci);

// Optional field — empty input is null (absent).
Result<int>? optional = Parser.ParseOptional<int>(input, ci);

// Pattern-match consumption.
string message = count switch
{
    Success<int>(var v) => $"got {v}",
    Failure(var err)    => $"parse failed: {err}",
};

// Composition.
Result<string> label = count
    .Map(v => v * 2)
    .Bind(v => v > 100
        ? (Result<string>)new Failure(new Error(new ValidationError("count", "too large", null)))
        : (Result<string>)new Success<string>($"label-{v}"));
```

## Outer-nullability vs. inner-Result

The orthogonal concerns of *presence* and *validity*:

| Outer | Inner | Meaning |
|---|---|---|
| `null` | — | Absent |
| `Result<T>` | `Success(v)` | Present and valid |
| `Result<T>` | `Failure(err)` | Present but invalid |

OpenAPI generators mirror outer `?` to schema `required`. EF Core column nullability mirrors outer `?` to `NULL` / `NOT NULL`. Validation results live inside.

## F# consumer support

Norse.Primitives is a first-class consumer target for F#. The lambda-based `Match` API works from any CLR language regardless of F# tooling support for C# 15 union pattern matching:

```fsharp
let v =
    Parser.ParseRequired<int>(input, CultureInfo.InvariantCulture)
        .Match(
            (fun v -> Some v),
            (fun err -> None))
```

See spec §11 for details.

## License

MIT.
```

- [ ] **Step 2: Stage**

Run: `git add Norse.Primitives/README.md`

Proposed commit message: `docs(svartalfheim): write README with quick-start and F# notes`

---

### Task 33: Final integration check — run the full test suite

- [ ] **Step 1: Run every test in the solution**

Run: `dotnet test Norse.Primitives/Norse.Primitives.slnx`

Expected: all tests pass. Note count for the record (Tasks 7–24 + property tests + AOT smoke logic, ~50+ tests total).

- [ ] **Step 2: Run the AOT smoke test against a JIT build**

Run: `dotnet run --project Norse.Primitives/tests/Norse.Primitives.Aot.SmokeTests/Norse.Primitives.Aot.SmokeTests.csproj`

Expected: every line `AOT smoke OK`, exit 0.

- [ ] **Step 3: Publish AOT smoke test and run**

Run:
```powershell
dotnet publish Norse.Primitives/tests/Norse.Primitives.Aot.SmokeTests/Norse.Primitives.Aot.SmokeTests.csproj -c Release -o ./aot-out
./aot-out/Norse.Primitives.Aot.SmokeTests.exe
rm -r -force ./aot-out
```

Expected: same output, no trim warnings during publish.

- [ ] **Step 4: Run a quick benchmark to verify zero-alloc claim**

Run: `dotnet run -c Release --project Norse.Primitives/benchmarks/Norse.Primitives.Benchmarks/Norse.Primitives.Benchmarks.csproj -- --filter "*Int_success*"`

Expected: `Allocated` column reads `0 B` (or `-`). If non-zero, investigate before declaring Norse.Primitives done.

- [ ] **Step 5: Hand off to user for review**

Report results to user. Do not commit the cumulative work — every task above already staged its changes. User runs `git commit` for each (or amalgamates them) after their review.

---

## Self-Review Checklist (run before handing off)

1. **Spec coverage** — every section of `docs/Svartalfheim/specs/2026-05-20-svartalfheim-primitives-design.md` has at least one task implementing it:
   - §4 Result + Error + ParseError → Tasks 7, 8, 9, 10
   - §5 Pattern matching → Task 10 (XML doc) + ResultTests in Task 7
   - §6 Parser gateway → Tasks 11, 12, 13
   - §7.1 Sync composition → Tasks 14–19
   - §7.2 Async composition → Task 20
   - §7.3 Present composition → Task 21
   - §7.4 Aggregation → Tasks 22, 23
   - §7.5 Monad laws → Task 30
   - §8 [MustConsume] → Task 24
   - §9 AOT + runtime-async tier policy → Tasks 2, 4, 25
   - §10 Project structure + slnx → Tasks 3, 4, 5, 6
   - §11 F# consumer support → README in Task 32
   - §12 Testing + benchmarks → Tasks 13, 25, 27, 28, 29, 30, 31

2. **Placeholder scan** — no `TBD`, `TODO`, `implement later`, "add appropriate error handling", or "similar to Task N" anywhere above. Every step has actual code or an actual command.

3. **Type consistency** — `Result<T>`, `Success<T>`, `Failure`, `Error`, `ParseError` (etc.), `Parser.ParseRequired<T>` / `Parser.ParseOptional<T>`, `Map` / `MapError` / `Bind` / `Match` / `GetValueOrThrow` / `GetValueOrDefault` / `IsSuccess` / `IsFailure`, `MapAsync` / `BindAsync` / `MatchAsync`, `MapPresent` / `BindPresent` / `MatchPresent`, `Result.Combine` / `Result.Collect`, `MustConsumeAttribute`, `ResultFailureException` — names are consistent across all tasks and tests.

4. **Per-CLAUDE.md §8 — no automatic commits.** Every "Stage" step uses `git add` only; the commit step is the human's. Proposed commit messages are in the body of each step for the human to use or rewrite.

---
