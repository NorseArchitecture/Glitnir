# Generator Authoring Toolkit + Raw-String Emitter House Style Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `Norse.Abstractions.Generator` — a real, referenceable netstandard2.0 toolkit assembly holding the polyfills every Roslyn generator project needs — and retrofit all four of Asgard's gateway emitters to the new house style: `sb.AppendCSharp(raw-string-literal)` instead of repeated `sb.AppendLine(...)` calls.

**Architecture:** New project `Asgard/src/Abstractions.Generator/` (netstandard2.0, packable) carries three files ported/relocated from prior art: `CSharpEmit.cs` (`AppendCSharp` extension), `StringSyntaxAttribute.cs`, and `IsExternalInit.cs` (moved out of `Abstractions.Contracts.Generator`, made `public`). `Abstractions.Contracts.Generator` takes a plain in-repo `ProjectReference` to it; `Abstractions.Contracts.csproj`'s existing analyzer-packing target grows one more `None` item so the dependency DLL ships alongside the generator's own. The four emitters (`ContractEmitter`, `OutcomeSurrogatesEmitter`, `WireHostEmitter`, `InProcessHostEmitter`) are retrofitted one at a time, each verified against the existing `GatewayGeneratorTests` suite, which already exercises every mode (Contract/WireHost/InProcessHost) and every diagnostic path.

**Tech Stack:** Roslyn `IIncrementalGenerator` (netstandard2.0), C# raw string literals (`$$"""..."""`), xUnit v3 + Shouldly on Microsoft.Testing.Platform.

**Spec:** `../Glitnir/docs/Asgard/specs/2026-07-25-generator-authoring-toolkit-and-raw-string-house-style-design.md`

**Addendum (final review, 2026-07-25):** the whole-branch review — run after all six tasks were
individually reviewed and approved — found two things only visible across the whole branch. Recorded
here; the task bodies below are left as the historical record of what was planned and executed.

1. **The project shipped as `Norse.Abstractions.Emit`, not `Norse.Abstractions.Generator`** —
   everywhere this plan says `Abstractions.Generator` / `Norse.Abstractions.Generator` (project
   folder, csproj, namespace, test project, slnx entries, packed DLL name), read
   `Abstractions.Emit` / `Norse.Abstractions.Emit`. The planned name collided with the platform's
   duplicate-generator strip target (`_NorseRemoveUnwantedGeneratorAnalyzers` in each realm's
   `src/Directory.Build.targets`), which drops any `@(Analyzer)` item matching
   `^Norse\..+\.Generator$` that isn't the consumer's own wanted generator. It was semantically
   wrong besides: `Norse.X.Generator` means "the generator serving `Norse.X`", and this is a
   generator-*authoring toolkit* serving every realm's generator projects.
2. **Task 2's "plain in-repo `ProjectReference`" does not get the dependency to consumers.**
   `OutputItemType="Analyzer"` pulls only the referenced project's own `GetTargetPath` output into
   `@(Analyzer)`, never its transitive `ProjectReference`s — so `Norse.Abstractions.Emit.dll` never
   reached a consumer's analyzer load context and `GatewayGenerator` died at runtime with
   `FileNotFoundException`, surfaced downstream as `CS8785`. The fix is an explicit forwarding
   target in `Abstractions.Contracts.Generator.csproj` that hooks `GetTargetPathDependsOn` and
   appends the dependency to `@(TargetPathWithTargetPlatformMoniker)` (filtered on both `Filename`
   and `Extension` — `ReferenceCopyLocalPaths` carries the `.pdb`/`.xml` siblings, and feeding those
   to Roslyn yields `CS8034`). Task 2's `dotnet pack` + `unzip` verification proves the *package's*
   contents only; it cannot prove a ProjectReference-mode consumer loads them. The real acceptance
   test is building a downstream consumer — `Heimdall/src/AuthN.Services`, which reaches this
   generator through Bifröst's `NorseRef ... Generator="true"` forwarding.

## Global Constraints

- Tabs for indentation everywhere; US English spelling in code, comments, and commit copy.
- `TreatWarningsAsErrors=true` platform-wide — a malformed raw string literal (mismatched closing-delimiter indentation) is a compile error, not a warning; if a task's build fails on a raw string, fix the offending line's leading whitespace to match its literal's closing `"""` and re-run.
- **Commits happen in Asgard, on the local unpushed `house_style` branch only.** Tasks 1–6 live entirely in Asgard, which is already checked out on `house_style` (clean, unpushed) — implementer subagents commit per task there, per standing policy: subagents may commit on a local unpushed branch the human is watching, never on `master`, never pushed. Task 7 edits Bifröst's own root `CLAUDE.md`, which stays on `master` per Bifröst's own hard law (never branched, never committed by an agent) — that task ends with `git add` (stage) and a stop; the human reviews and commits it.
- No new/changed generated-code *behavior* anywhere in this plan (Tasks 2–6 are structural/formatting only) — every task's verification is the existing `Abstractions.Contracts.Generator.Tests` suite (`GatewayGeneratorTests.cs`) staying green, not a new test asserting new output.
- **Raw string interpolation mechanic** (relevant from Task 3 onward): inside a `$$"""..."""` literal, only the *first* line of a multi-line interpolated value (`{{SomeHelper(...)}}`) lands at the hole's column — every subsequent line of that value is inserted verbatim, with no re-indentation. Helper methods that produce multi-line fragments (e.g. `Methods(model)` below) therefore bake their **full real output indentation** into every line they return, and the hole itself sits flush at the template's own baseline (no extra leading whitespace in the template source), not nested deeper to "look right" next to neighboring static lines.

---

## Task 1: Create `Norse.Abstractions.Generator`

**Files:**
- Create: `Asgard/src/Abstractions.Generator/Abstractions.Generator.csproj`
- Create: `Asgard/src/Abstractions.Generator/CSharpEmit.cs`
- Create: `Asgard/src/Abstractions.Generator/StringSyntaxAttribute.cs`
- Create: `Asgard/src/Abstractions.Generator/IsExternalInit.cs`
- Create: `Asgard/tests/Abstractions.Generator.Tests/Abstractions.Generator.Tests.csproj`
- Create: `Asgard/tests/Abstractions.Generator.Tests/CSharpEmitTests.cs`
- Modify: `Asgard/Asgard.slnx`
- Modify: `Bifrost.slnx`

**Interfaces:**
- Produces: `Norse.Abstractions.Generator.CSharpEmit.AppendCSharp(this StringBuilder, [StringSyntax("C#")] string code) : StringBuilder` — consumed by Task 2 onward.
- Produces: `System.Runtime.CompilerServices.IsExternalInit` (public) — required by any consumer project whose own types use `init` accessors (e.g. `Abstractions.Contracts.Generator`'s `GatewayInterfaceModel`/`GatewayMethodModel` records) once that project references this assembly across a real assembly boundary rather than compiling the polyfill directly into its own sources.
- Produces: `System.Diagnostics.CodeAnalysis.StringSyntaxAttribute` (public).

- [ ] **Step 1: Scaffold the project (no source yet) and wire it into both solutions**

Create `Asgard/src/Abstractions.Generator/Abstractions.Generator.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<TargetFramework>netstandard2.0</TargetFramework>
		<IsAotCompatible>false</IsAotCompatible>
		<Description>Norse.Abstractions.Generator: the platform's netstandard2.0 generator-authoring toolkit — polyfills and helpers every Roslyn IIncrementalGenerator project needs (AppendCSharp raw-string emission, [StringSyntax] and IsExternalInit polyfills), so realm generator projects stop vendoring copies of the same handful of files.</Description>
	</PropertyGroup>
</Project>
```

In `Asgard/Asgard.slnx`, add to the `/src/` folder (alongside the other `Abstractions.*` projects):

```xml
<Project Path="src/Abstractions.Generator/Abstractions.Generator.csproj" />
```

In `Bifrost.slnx`, add to the `/Abstractions/src/` folder:

```xml
<Project Path="Asgard/src/Abstractions.Generator/Abstractions.Generator.csproj" />
```

- [ ] **Step 2: Write the failing test**

Create `Asgard/tests/Abstractions.Generator.Tests/Abstractions.Generator.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Abstractions.Generator/Abstractions.Generator.csproj" />
	</ItemGroup>
</Project>
```

Create `Asgard/tests/Abstractions.Generator.Tests/CSharpEmitTests.cs`:

```csharp
using System.Text;
using Norse.Abstractions.Generator;

namespace Norse.Abstractions.Generator.Tests;

public sealed class CSharpEmitTests
{
	[Fact]
	void AppendCSharp_IsIdenticalToAppendLine()
	{
		const string Code = "public static class Foo\n{\n}";

		var viaAppendCSharp = new StringBuilder().AppendCSharp(Code).ToString();
		var viaAppendLine = new StringBuilder().AppendLine(Code).ToString();

		viaAppendCSharp.ShouldBe(viaAppendLine);
	}
}
```

In `Asgard/Asgard.slnx`, add to the `/tests/` folder:

```xml
<Project Path="tests/Abstractions.Generator.Tests/Abstractions.Generator.Tests.csproj" />
```

In `Bifrost.slnx`, add to the `/Abstractions/tests/` folder:

```xml
<Project Path="Asgard/tests/Abstractions.Generator.Tests/Abstractions.Generator.Tests.csproj" />
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test Asgard/tests/Abstractions.Generator.Tests/Abstractions.Generator.Tests.csproj`
Expected: build FAILS — `AppendCSharp` doesn't exist yet (`CSharpEmit.cs` hasn't been written).

- [ ] **Step 4: Implement the three polyfill files**

Create `Asgard/src/Abstractions.Generator/CSharpEmit.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Norse.Abstractions.Generator;

/// <summary>
/// Emission helper for Roslyn generator projects — see the house style rule in Bifröst's own
/// CLAUDE.md: generator emitter code always calls <see cref="AppendCSharp"/>, never
/// <see cref="StringBuilder.AppendLine(string)"/> directly, collapsing what would otherwise be
/// multiple sequential AppendLine calls into a single raw string literal.
/// </summary>
public static class CSharpEmit
{
	// Identical to StringBuilder.AppendLine at runtime; the [StringSyntax("C#")] annotation is
	// what Visual Studio and Rider use to syntax-highlight the raw-string content at each call
	// site as C# instead of as opaque text.
	public static StringBuilder AppendCSharp(this StringBuilder sb, [StringSyntax("C#")] string code) =>
		sb.AppendLine(code);
}
```

Create `Asgard/src/Abstractions.Generator/StringSyntaxAttribute.cs`:

```csharp
// Polyfill for System.Diagnostics.CodeAnalysis.StringSyntaxAttribute (added in .NET 7). Every
// Roslyn generator project targets netstandard2.0 regardless of what its consumer targets, so the
// BCL definition isn't available there. Roslyn's IDE classifiers recognize the attribute by
// namespace + type name (not assembly identity), so a declaration here still drives the
// embedded-language hint in VS / Rider for any consumer of this package.
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace System.Diagnostics.CodeAnalysis;
#pragma warning restore IDE0130 // Namespace does not match folder structure

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class StringSyntaxAttribute(string syntax) : Attribute
{
	public string Syntax { get; } = syntax;
}
```

Create `Asgard/src/Abstractions.Generator/IsExternalInit.cs`:

```csharp
using System.ComponentModel;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;
#pragma warning restore IDE0130

/// <summary>
/// Reserved to be used by the compiler for tracking metadata about the 'init' keyword and its use.
/// </summary>
/// <remarks>
/// Public — unlike this polyfill's usual internal-by-default shape (e.g. Urðarbrunnr's own copy,
/// which is compiled directly into each consumer via a linked source file, never crossing a real
/// assembly boundary). Here it must be visible from a consuming project's own compilation across
/// a genuine NuGet/ProjectReference boundary, since that project's own <c>init</c>-accessor code
/// (e.g. a positional record) needs to resolve this type from a referenced assembly, not a
/// same-assembly source file.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class IsExternalInit;
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test Asgard/tests/Abstractions.Generator.Tests/Abstractions.Generator.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit in Asgard; stage-only in the Bifröst root**

In Asgard (commits to the local, unpushed `house_style` branch):

```bash
git -C Asgard add src/Abstractions.Generator tests/Abstractions.Generator.Tests Asgard.slnx
git -C Asgard commit -m "Add Norse.Abstractions.Generator toolkit project"
```

In the Bifröst root — Bifröst itself stays on `master` and is never committed by an agent:

```bash
git add Bifrost.slnx
```

Show the Bifröst-root diff and stop there — do not commit it.

---

## Task 2: Wire `Abstractions.Contracts.Generator` to consume the toolkit

**Files:**
- Delete: `Asgard/gen/Abstractions.Contracts.Generator/IsExternalInit.cs`
- Modify: `Asgard/gen/Abstractions.Contracts.Generator/Abstractions.Contracts.Generator.csproj`
- Modify: `Asgard/src/Abstractions.Contracts/Abstractions.Contracts.csproj`

**Interfaces:**
- Consumes: `Norse.Abstractions.Generator.IsExternalInit` (Task 1) — satisfies `GatewayInterfaceModel`/`GatewayMethodModel`'s existing `init`-accessor record properties, now resolved from the referenced assembly instead of a same-project file.
- Consumes (not yet, but proves the packing mechanism): `Norse.Abstractions.Generator.CSharpEmit.AppendCSharp` — not called by any emitter until Task 3.

This task changes no generated-code behavior — it's a dependency wire-up plus a packaging-target fix. There is no new test; the verification is the existing suite staying green across the change, since a break here (e.g. `IsExternalInit` not resolving, or the analyzer package missing a dependency DLL at pack time) would surface as compile errors or test failures, not as a silently-wrong assertion.

- [ ] **Step 1: Run the existing suite to confirm the baseline is green**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests/Abstractions.Contracts.Generator.Tests.csproj`
Expected: PASS (this is the pre-change baseline, not a new test).

- [ ] **Step 2: Delete the now-relocated polyfill and add the ProjectReference**

Delete `Asgard/gen/Abstractions.Contracts.Generator/IsExternalInit.cs`.

In `Asgard/gen/Abstractions.Contracts.Generator/Abstractions.Contracts.Generator.csproj`, add an `ItemGroup`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Abstractions.Contracts.Generator: emits per-service, Result-native Blazor gateways from a [GenerateGateway]-decorated service interface (contracts, WASM host, and composition-root artifacts — none of which is a Web.Server-only concern, hence the name). Bundled into Abstractions.Contracts's package (analyzers/dotnet/cs/), never referenced or packed standalone — see Abstractions.Contracts.csproj's IncludeGeneratorInPackage target.</Description>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../../src/Abstractions.Generator/Abstractions.Generator.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 3: Run the suite to verify it still fails to build cleanly if the reference is wrong, then confirm it passes**

Run: `dotnet build Asgard/gen/Abstractions.Contracts.Generator/Abstractions.Contracts.Generator.csproj`
Expected: PASS — `IsExternalInit` resolves via the new `ProjectReference`, no `CS0518` ("Predefined type ... is not defined or imported").

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests/Abstractions.Contracts.Generator.Tests.csproj`
Expected: PASS, identical results to Step 1's baseline.

- [ ] **Step 4: Bundle the dependency DLL into the analyzer package**

In `Asgard/src/Abstractions.Contracts/Abstractions.Contracts.csproj`, extend the `IncludeGeneratorInPackage` target with a second `None` item. The dependency DLL already lands in the generator's own output folder as a side effect of the `ProjectReference` added in Step 2 (default `ReferenceOutputAssembly=true` copies it there on build) — no separate build step is needed for it:

```xml
	<Target Name="IncludeGeneratorInPackage" BeforeTargets="_GetPackageFiles">
		<MSBuild Projects="../../gen/Abstractions.Contracts.Generator/Abstractions.Contracts.Generator.csproj"
			Targets="Build"
			Properties="Configuration=$(Configuration)" />
		<ItemGroup>
			<None Include="../../gen/Abstractions.Contracts.Generator/bin/$(Configuration)/netstandard2.0/Norse.Abstractions.Contracts.Generator.dll"
				Pack="true"
				PackagePath="analyzers/dotnet/cs/"
				Visible="false" />
			<None Include="../../gen/Abstractions.Contracts.Generator/bin/$(Configuration)/netstandard2.0/Norse.Abstractions.Generator.dll"
				Pack="true"
				PackagePath="analyzers/dotnet/cs/"
				Visible="false" />
		</ItemGroup>
	</Target>
```

- [ ] **Step 5: Verify the packed analyzer folder actually contains both DLLs**

Run: `dotnet pack Asgard/src/Abstractions.Contracts/Abstractions.Contracts.csproj -o /tmp/norse-pack-check`
Run: `unzip -l /tmp/norse-pack-check/Norse.Abstractions.Contracts.*.nupkg | grep analyzers/dotnet/cs/`
Expected: both `Norse.Abstractions.Contracts.Generator.dll` and `Norse.Abstractions.Generator.dll` listed.

- [ ] **Step 6: Commit**

```bash
git -C Asgard add gen/Abstractions.Contracts.Generator src/Abstractions.Contracts/Abstractions.Contracts.csproj
git -C Asgard commit -m "Wire Abstractions.Contracts.Generator to Norse.Abstractions.Generator"
```

---

## Task 3: Retrofit `ContractEmitter`

**Files:**
- Modify: `Asgard/gen/Abstractions.Contracts.Generator/ContractEmitter.cs`

**Interfaces:**
- Consumes: `Norse.Abstractions.Generator.CSharpEmit.AppendCSharp` (Task 1).

No behavior change — verification is the existing `GatewayGeneratorTests` Contract-mode tests staying green.

- [ ] **Step 1: Run the Contract-mode tests to confirm the baseline is green**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests/Abstractions.Contracts.Generator.Tests.csproj --filter "ContractMode|MissingAuthorizeAttribute|StreamingMethod|InterfaceNotEndingInService|MethodWithNoParameters|MethodReturningBarePayload"`
Expected: PASS (baseline).

- [ ] **Step 2: Rewrite the emitter**

Replace the contents of `Asgard/gen/Abstractions.Contracts.Generator/ContractEmitter.cs`:

```csharp
using System.Text;
using Norse.Abstractions.Generator;

namespace Norse.Abstractions.Contracts.Generator;

static class ContractEmitter
{
	internal static string Emit(GatewayInterfaceModel model)
	{
		StringBuilder builder = new();
		builder.AppendCSharp(
			$$"""
			// <auto-generated/>
			namespace {{model.Namespace}};

			using Norse.Abstractions.Contracts;

			public interface I{{model.ContextName}}Gateway
			{
			{{Methods(model)}}
			}
			""");
		return builder.ToString();
	}

	// Always Outcome<{responseType}>, responseType = "Unit" for void methods — never the bare
	// "Outcome" alias spelling. Generated code must never depend on the consuming project
	// happening to carry the alias file; only handwritten call sites opt into that ergonomic
	// shorthand (2026-07-24 review — this emitter was the one branch the Unit-consolidation
	// pass missed; WireHostEmitter/InProcessHostEmitter already follow this rule).
	static string Methods(GatewayInterfaceModel model) =>
		string.Join("\n", model.Methods.Select(method =>
			$"\tValueTask<Outcome<{method.ResponseTypeName ?? "Unit"}>> {method.Name}({method.RequestTypeName} request, CancellationToken cancellationToken = default);"));
}
```

- [ ] **Step 3: Run the tests to verify they still pass**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests/Abstractions.Contracts.Generator.Tests.csproj --filter "ContractMode|MissingAuthorizeAttribute|StreamingMethod|InterfaceNotEndingInService|MethodWithNoParameters|MethodReturningBarePayload"`
Expected: PASS, identical to Step 1.

If it fails to *compile* with a raw-string-literal indentation error, align the offending line's leading tabs with the closing `"""` on the line above `return builder.ToString();` — see Global Constraints.

- [ ] **Step 4: Commit**

```bash
git -C Asgard add gen/Abstractions.Contracts.Generator/ContractEmitter.cs
git -C Asgard commit -m "Retrofit ContractEmitter to AppendCSharp raw-string house style"
```

---

## Task 4: Retrofit `OutcomeSurrogatesEmitter`

**Files:**
- Modify: `Asgard/gen/Abstractions.Contracts.Generator/OutcomeSurrogatesEmitter.cs`

**Interfaces:**
- Consumes: `Norse.Abstractions.Generator.CSharpEmit.AppendCSharp` (Task 1).

- [ ] **Step 1: Run the surrogate tests to confirm the baseline is green**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests/Abstractions.Contracts.Generator.Tests.csproj --filter "OutcomeSurrogates|NeverEmitsProtobufNetReferences"`
Expected: PASS (baseline).

- [ ] **Step 2: Rewrite the emitter**

Replace the contents of `Asgard/gen/Abstractions.Contracts.Generator/OutcomeSurrogatesEmitter.cs`:

```csharp
using System.Text;
using Norse.Abstractions.Generator;

namespace Norse.Abstractions.Contracts.Generator;

/// <summary>
/// Emits a <c>RegisterOutcomeSurrogates</c> extension registering <c>Outcome&lt;T&gt;</c> → <c>T</c>
/// passthrough surrogates for every closed response type on the contract (spec §9's "transparent
/// passthrough" wire-format mandate) — one line per distinct payload type, deduplicated, since
/// registering the same closed type twice throws at runtime. Emitted only alongside WireHost and
/// InProcessHost output — never Contract mode, which ships into service realms that must never
/// carry a protobuf-net reference (architect's ruling, 2026-07-24: gRPC/protobuf never sifts into
/// a service realm).
/// </summary>
static class OutcomeSurrogatesEmitter
{
	internal static string Emit(GatewayInterfaceModel model)
	{
		StringBuilder builder = new();
		builder.AppendCSharp(
			$$"""
			// <auto-generated/>
			namespace {{model.Namespace}};

			using Norse.Abstractions.Contracts;
			using ProtoBuf.Meta;

			static class {{model.ContextName}}OutcomeSurrogates
			{
				public static RuntimeTypeModel RegisterOutcomeSurrogates(this RuntimeTypeModel model)
				{
			{{Surrogates(model)}}
					return model;
				}
			}
			""");
		return builder.ToString();
	}

	static string Surrogates(GatewayInterfaceModel model) =>
		string.Join("\n", model.Methods
			.Select(m => m.ResponseTypeName)
			.Distinct(StringComparer.Ordinal)
			.Select(responseType => $"\t\tmodel.Add(typeof(Outcome<{responseType}>), applyDefaultBehaviour: false).SetSurrogate(typeof({responseType}));"));
}
```

- [ ] **Step 3: Run the tests to verify they still pass**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests/Abstractions.Contracts.Generator.Tests.csproj --filter "OutcomeSurrogates|NeverEmitsProtobufNetReferences"`
Expected: PASS, identical to Step 1.

- [ ] **Step 4: Commit**

```bash
git -C Asgard add gen/Abstractions.Contracts.Generator/OutcomeSurrogatesEmitter.cs
git -C Asgard commit -m "Retrofit OutcomeSurrogatesEmitter to AppendCSharp raw-string house style"
```

---

## Task 5: Retrofit `WireHostEmitter`

**Files:**
- Modify: `Asgard/gen/Abstractions.Contracts.Generator/WireHostEmitter.cs`

**Interfaces:**
- Consumes: `Norse.Abstractions.Generator.CSharpEmit.AppendCSharp` (Task 1).

- [ ] **Step 1: Run the WireHost tests to confirm the baseline is green**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests/Abstractions.Contracts.Generator.Tests.csproj --filter "WireHostMode"`
Expected: PASS (baseline).

- [ ] **Step 2: Rewrite the emitter**

Replace the contents of `Asgard/gen/Abstractions.Contracts.Generator/WireHostEmitter.cs`:

```csharp
using System.Text;
using Norse.Abstractions.Generator;

namespace Norse.Abstractions.Contracts.Generator;

static class WireHostEmitter
{
	internal static string Emit(GatewayInterfaceModel model)
	{
		StringBuilder builder = new();
		builder.AppendCSharp(
			$$"""
			// <auto-generated/>
			namespace {{model.Namespace}};

			using Norse.Abstractions.Contracts;

			sealed class {{model.ContextName}}WireGateway({{model.ServiceInterfaceName}} service) : I{{model.ContextName}}Gateway
			{
			{{Methods(model)}}
			}
			""");
		return builder.ToString();
	}

	static string Methods(GatewayInterfaceModel model) =>
		string.Join("\n", model.Methods.Select(Method));

	// The service already returns Outcome<TResponse> directly (spec §9, 2026-07-24 amendment) —
	// the server never sends the Failed arm over the wire (it throws server-side before
	// marshalling), so a successful call already carries a real Success<T> Outcome<T> the client
	// can return unchanged, no Ok-wrapping ceremony.
	static string Method(GatewayMethodModel method)
	{
		var (name, requestTypeName, responseType, _) = method;
		return
			$$"""
				public async ValueTask<Outcome<{{responseType}}>> {{name}}({{requestTypeName}} request, CancellationToken cancellationToken = default)
				{
					try
					{
						return await service.{{name}}(request).ConfigureAwait(false);
					}
					catch (global::Grpc.Core.RpcException ex)
					{
						var problem = global::Norse.Infrastructure.Web.Client.Grpc.RpcExceptionExtensions.DecodeProblem(ex);
						return Outcome<{{responseType}}>.Err(problem.Category, problem.Errors, problem.CorrelationId);
					}
				}
			""";
	}
}
```

- [ ] **Step 3: Run the tests to verify they still pass**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests/Abstractions.Contracts.Generator.Tests.csproj --filter "WireHostMode"`
Expected: PASS, identical to Step 1.

- [ ] **Step 4: Commit**

```bash
git -C Asgard add gen/Abstractions.Contracts.Generator/WireHostEmitter.cs
git -C Asgard commit -m "Retrofit WireHostEmitter to AppendCSharp raw-string house style"
```

---

## Task 6: Retrofit `InProcessHostEmitter`

**Files:**
- Modify: `Asgard/gen/Abstractions.Contracts.Generator/InProcessHostEmitter.cs`

**Interfaces:**
- Consumes: `Norse.Abstractions.Generator.CSharpEmit.AppendCSharp` (Task 1).

This is the largest of the four — four separate per-method loops in the original (validator field decls, constructor params, constructor field assignments, method bodies) become four small helper methods, each producing its own fully-indented fragment (see Global Constraints on the interpolation mechanic). `ValidatorFieldName`/`ValidatorParamName` factor out the `_x`/`x` lowering logic the original inlined four times — a direct byproduct of splitting into named helpers, not a separate refactor.

- [ ] **Step 1: Run the InProcessHost tests to confirm the baseline is green**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests/Abstractions.Contracts.Generator.Tests.csproj --filter "InProcessHostMode"`
Expected: PASS (baseline).

- [ ] **Step 2: Rewrite the emitter**

Replace the contents of `Asgard/gen/Abstractions.Contracts.Generator/InProcessHostEmitter.cs`:

```csharp
using System.Text;
using Norse.Abstractions.Generator;

namespace Norse.Abstractions.Contracts.Generator;

// ReSharper disable ReplaceSubstringWithRangeIndexer
static class InProcessHostEmitter
{
	internal static string Emit(GatewayInterfaceModel model)
	{
		StringBuilder builder = new();
		builder.AppendCSharp(
			$$"""
			// <auto-generated/>
			namespace {{model.Namespace}};

			using Norse.Abstractions.Contracts;
			using Norse.Abstractions.Web.Server.Mediator;

			sealed class {{model.ContextName}}InProcessGateway : I{{model.ContextName}}Gateway
			{
				readonly {{model.ServiceInterfaceName}} _service;
				readonly Microsoft.Extensions.Logging.ILoggerFactory _loggerFactory;
				readonly Microsoft.AspNetCore.Authorization.IAuthorizationService _authorizationService;
				readonly Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider _authenticationStateProvider;
			{{ValidatorFields(model)}}

				public {{model.ContextName}}InProcessGateway(
					{{model.ServiceInterfaceName}} service,
					Microsoft.Extensions.Logging.ILoggerFactory loggerFactory,
					Microsoft.AspNetCore.Authorization.IAuthorizationService authorizationService,
					Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider authenticationStateProvider,
			{{ConstructorParams(model)}}
				{
					_service = service;
					_loggerFactory = loggerFactory;
					_authorizationService = authorizationService;
					_authenticationStateProvider = authenticationStateProvider;
			{{FieldAssignments(model)}}
				}

				async ValueTask<System.Security.Claims.ClaimsPrincipal> GetPrincipalAsync() =>
					(await _authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false)).User;

			{{Methods(model)}}
			}
			""");
		return builder.ToString();
	}

	static string ValidatorFieldName(string methodName) =>
		$"_{ValidatorParamName(methodName)}";

	static string ValidatorParamName(string methodName) =>
		$"{char.ToLowerInvariant(methodName[0])}{methodName.Substring(1)}Validator";

	static string ValidatorFields(GatewayInterfaceModel model) =>
		string.Join("\n", model.Methods.Select(method =>
			$"\treadonly FluentValidation.IValidator<{method.RequestTypeName}> {ValidatorFieldName(method.Name)};"));

	static string ConstructorParams(GatewayInterfaceModel model) =>
		string.Join("\n", model.Methods.Select((method, i) =>
		{
			var separator = i == model.Methods.Length - 1 ? ")" : ",";
			return $"\t\tFluentValidation.IValidator<{method.RequestTypeName}> {ValidatorParamName(method.Name)}{separator}";
		}));

	static string FieldAssignments(GatewayInterfaceModel model) =>
		string.Join("\n", model.Methods.Select(method =>
			$"\t\t{ValidatorFieldName(method.Name)} = {ValidatorParamName(method.Name)};"));

	static string Methods(GatewayInterfaceModel model) =>
		string.Join("\n", model.Methods.Select(Method));

	// One shape throughout: void-success methods use responseType = "Unit" via the IBehavior<,>
	// family's TResponse — never a second, non-generic behavior/chain shape (2026-07-24
	// amendment). The service already returns Outcome<TResponse> directly (spec §9, 2026-07-24
	// amendment) — the innermost call awaits and returns it unchanged, no Ok-wrapping ceremony;
	// that was only needed when the service returned a bare payload.
	static string Method(GatewayMethodModel method)
	{
		var (name, requestTypeName, responseType, policyName) = method;
		var validatorFieldName = ValidatorFieldName(name);
		var innermostSuccessExpression = $"await _service.{name}(request).ConfigureAwait(false)";

		return
			$$"""
				public async ValueTask<Outcome<{{responseType}}>> {{name}}({{requestTypeName}} request, CancellationToken cancellationToken = default)
				{
					var telemetry = new Norse.Infrastructure.Web.Server.Mediator.TelemetryBehavior<{{requestTypeName}}, {{responseType}}>(_loggerFactory.CreateLogger<Norse.Infrastructure.Web.Server.Mediator.TelemetryBehavior<{{requestTypeName}}, {{responseType}}>>());
					var exceptionTranslation = new Norse.Infrastructure.Web.Server.Mediator.ExceptionTranslationBehavior<{{requestTypeName}}, {{responseType}}>(_loggerFactory.CreateLogger<Norse.Infrastructure.Web.Server.Mediator.ExceptionTranslationBehavior<{{requestTypeName}}, {{responseType}}>>());
					var authorization = new Norse.Infrastructure.Web.Server.Mediator.AuthorizationBehavior<{{requestTypeName}}, {{responseType}}>("{{policyName}}", _authorizationService, GetPrincipalAsync);
					var validation = new Norse.Infrastructure.Web.Server.Mediator.ValidationBehavior<{{requestTypeName}}, {{responseType}}>({{validatorFieldName}});

					return await telemetry.Handle(request, cancellationToken, () =>
						exceptionTranslation.Handle(request, cancellationToken, () =>
							authorization.Handle(request, cancellationToken, () =>
								validation.Handle(request, cancellationToken, async () =>
									{{innermostSuccessExpression}})))).ConfigureAwait(false);
				}
			""";
	}
}
```

- [ ] **Step 3: Run the tests to verify they still pass**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests/Abstractions.Contracts.Generator.Tests.csproj --filter "InProcessHostMode"`
Expected: PASS, identical to Step 1.

- [ ] **Step 4: Run the full suite one more time**

Run: `dotnet test Asgard/tests/Abstractions.Contracts.Generator.Tests/Abstractions.Contracts.Generator.Tests.csproj`
Expected: PASS — every test in the file, confirming Tasks 3–6 together didn't regress anything across modes.

- [ ] **Step 5: Commit**

```bash
git -C Asgard add gen/Abstractions.Contracts.Generator/InProcessHostEmitter.cs
git -C Asgard commit -m "Retrofit InProcessHostEmitter to AppendCSharp raw-string house style"
```

---

## Task 7: Document the house style rule

**Files:**
- Modify: `CLAUDE.md` (Bifröst's own, repo root)

**Interfaces:** None — documentation only.

- [ ] **Step 1: Add the rule**

In `CLAUDE.md`, §5 "Conventions", under the existing bullet list of Bifröst-specific additions, add:

```markdown
- **Generator emitters never call `AppendLine` directly.** Always `sb.AppendCSharp(...)` (`Norse.Abstractions.Generator.CSharpEmit`, a `[StringSyntax("C#")]`-annotated `AppendLine` wrapper) — including single-line appends. What would otherwise be multiple sequential `AppendLine` calls collapses into one `AppendCSharp` call with a raw string literal (`"""..."""`), so the generated shape reads as a block instead of being reconstructed line-by-line at the call site. Design: `../Glitnir/docs/Asgard/specs/2026-07-25-generator-authoring-toolkit-and-raw-string-house-style-design.md`.
```

- [ ] **Step 2: Stage**

```bash
git add CLAUDE.md
```

Show the diff and stop — do not commit.
