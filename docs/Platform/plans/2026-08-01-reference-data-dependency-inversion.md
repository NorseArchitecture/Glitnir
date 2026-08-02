# Reference Data — Dependency Inversion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (the platform default — superpowers:executing-plans is the narrow fallback for a separate-session review checkpoint, never an interchangeable alternative) to implement this plan task-by-task, paired with superpowers:test-driven-development on every task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Kill the Mimir ⇄ Mimisbrunnr dependency cycle by relocation — seeds, generator, and generated surface move to Mimisbrunnr; `Reference.Seeds` dies; the EF projects take `.EntityFramework` names.

**Architecture:** Two new Mimisbrunnr projects (`Reference.Data.Primitives` browser-supported, `Reference.Data.Namespaces` server-only) receive the generated surface from the relocated generator, which dispatches emission on compilation assembly name. Everything else is mechanical relocation and rename; no behavior changes. Spec: `../specs/2026-08-01-reference-data-dependency-inversion-design.md`.

**Tech Stack:** .NET 11 preview / C# preview, Roslyn incremental generators (netstandard2.0), xUnit v3 + Shouldly on MTP v2, EF Core 11 preview migrations, MSBuild `NorseRef` plumbing.

## Global Constraints

- **Immutable files — halt-and-ask if a task seems to need them changed:** every `Directory.Build.props`, `Directory.Build.targets`, `.editorconfig`, `nuget.config`, `global.json` (Ginnungagap scatter targets). This plan touches none of them.
- **Git:** each realm's work happens on a local branch `feature/reference-data-inversion` created from `master` in that realm's submodule. Subagents may commit on that local branch; **never push, never touch `master`**, and **never commit in Bifröst or Glitnir** — Bifröst/Glitnir edits are staged (`git add`) only. Verify `git branch --show-current` before every commit.
- **The root namespace GUID is FOREVER:** `8db01f36-dd6e-4cd1-8233-7ab1ec672fff` moves verbatim. Changing it re-keys every seeded `CountryOrArea.Id`. Any diff to that literal is a task failure.
- **Generated-source law:** all emission through `sb.AppendCSharp(...)` raw string literals (`Norse.Abstractions.Emit`), files written with `Utf8NoBom.Encoding`, LF-only. Never `AppendLine`.
- **House style:** tabs; target-typed `new()`; collection expressions; `sealed` by default; test methods bare `void`/`async Task` with sentence_shaped_names; one `<PropertyGroup>` + one `<ItemGroup>` per csproj, members alphabetical; IDE0005 violations are deleted, never suppressed; US English.
- **Cross-realm dev resolution:** inside Bifröst, `NorseRef` resolves to project references (`UseProjectReferences=true` from the root). Package-mode CI for a downstream realm goes green only after the upstream realm's release is published — ship gates between realms are human checkpoints, marked below.
- Build/test command per realm, run from the Bifröst root: `dotnet test <Realm>/<Realm>.slnx` (also compiles). Expect zero warnings — warnings are errors platform-wide.

## File Structure (end state)

```
Mimisbrunnr/
  seeds/raw/UNSD — Methodology.csv          (moved from Mimir/seeds/raw/)
  seeds/{region,country-or-area}.tsv        (unchanged)
  gen/Reference.Data.Primitives.Generator/  (moved from Mimir/gen/Reference.Contracts.Generator)
  src/Reference.Data.Primitives/            (new — generated enum/parser/dataset)
  src/Reference.Data.Namespaces/            (new — generated ReferenceNamespaces)
  src/Reference.Data.EntityFramework/       (renamed from Reference.Data)
  src/Reference.Data.EntityFramework.Migrations{,.PostgreSQL,.SqlServer}/  (renamed)
  tools/SeedTool/                           (realm-internal now — reads seeds/raw from disk)
  tests/  (renamed + new test projects, 1:1 with the above)
Mimir/
  src/Reference.Contracts/                  (wire records only; generator + CSV + Seeds gone)
  src/Reference.Web.Server/                 (NorseRef → Reference.Data.EntityFramework)
  src/Reference.Seeds/                      DELETED (with tests/Reference.Seeds.Tests, seeds/)
```

---

### Task 1: Mimisbrunnr — seeds move in, SeedTool goes realm-internal

**Files:**
- Create: `Mimisbrunnr/seeds/raw/UNSD — Methodology.csv` (byte-identical copy of `Mimir/seeds/raw/UNSD — Methodology.csv` — copy now, Mimir-side delete happens in Task 7)
- Modify: `Mimisbrunnr/tools/SeedTool/SeedTool.csproj`, `Mimisbrunnr/tools/SeedTool/Commands/GenerateUnsdM49Command.cs`
- Test: `Mimisbrunnr/tests/SeedTool.Tests/UnsdM49RealFileTests.cs`

**Interfaces:**
- Consumes: `TabularReader.OpenDelimited(string path, char delimiter)` (Svartálfheim `Norse.Primitives.Ingestion` — the overload `ReferenceDataSeedContributor` already uses).
- Produces: `GenerateUnsdM49Command.Settings.InputFile` (string, positional arg 1, default `seeds/raw/UNSD — Methodology.csv`). `Norse.Reference.Seeds` disappears from Mimisbrunnr's dependency graph.

- [ ] **Step 1: Branch.** In `Mimisbrunnr/`: `git switch -c feature/reference-data-inversion master`.

- [ ] **Step 2: Copy the CSV.** `mkdir -p Mimisbrunnr/seeds/raw` then copy `"Mimir/seeds/raw/UNSD — Methodology.csv"` (em-dash in the name — quote it) to `"Mimisbrunnr/seeds/raw/UNSD — Methodology.csv"`. Verify byte-identity with `cmp`.

- [ ] **Step 3: Update the failing tests first.** In `UnsdM49RealFileTests.cs`: delete `using Norse.Reference.Seeds;`, add a path constant beside the existing two (same five-hop shape those lines already use), and swap both `TabularReader.OpenDelimited(RawDatasets.UnsdM49(), ';')` calls:

```csharp
	const string RawCsvPath = "../../../../../seeds/raw/UNSD — Methodology.csv";
```
```csharp
		using var reader = TabularReader.OpenDelimited(RawCsvPath, ';');
```

- [ ] **Step 4: Run the tests — expect FAIL** (`dotnet test Mimisbrunnr/Mimisbrunnr.slnx`): compile still passes (Seeds still referenced) but drop the `NorseRef` in the same pass as Step 5 — the red state here is acceptable as a compile error after Step 5's csproj edit if run in that order; the meaningful gate is Step 6 green.

- [ ] **Step 5: Rework SeedTool.** In `SeedTool.csproj`, delete the three-line `NorseRef Include="Reference.Seeds"` block. In `GenerateUnsdM49Command.cs`, delete `using Norse.Reference.Seeds;`, add the input argument, and switch to the disk overload:

```csharp
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "[outputDirectory]")]
		[Description("Directory to write region.tsv and country-or-area.tsv into.")]
		public string OutputDirectory { get; init; } = "seeds";

		[CommandArgument(1, "[inputFile]")]
		[Description("Path to the raw UNSD M49 methodology CSV.")]
		public string InputFile { get; init; } = Path.Combine("seeds", "raw", "UNSD — Methodology.csv");
	}
```
```csharp
		using ITabularReader reader = TabularReader.OpenDelimited(settings.InputFile, ';');
```

- [ ] **Step 6: Run the full realm suite — expect PASS**, including the byte-identity test against the committed TSVs (proves the disk read produces identical output to the old embedded-stream read).

- [ ] **Step 7: Commit** on `feature/reference-data-inversion`: `git add seeds/raw tools/SeedTool tests/SeedTool.Tests && git commit -m "feat: seeds move in-realm; SeedTool reads raw CSV from disk"`.

---

### Task 2: Mimisbrunnr — the generator moves in, splits emission, and dispatches on assembly name

**Files:**
- Create: `Mimisbrunnr/gen/Reference.Data.Primitives.Generator/` — `ReferenceDataPrimitivesGenerator.cs`, `CsvParser.cs`, `NameSanitizer.cs`, `Uuid5.cs`, `IsoCountryCodeEmitter.cs`, `Iso3166Emitter.cs`, `NamespacesEmitter.cs` (new), `AnalyzerReleases.Shipped.md`, `AnalyzerReleases.Unshipped.md`, `Reference.Data.Primitives.Generator.csproj`
- Create: `Mimisbrunnr/tests/Reference.Data.Primitives.Generator.Tests/` — `GeneratorTestHarness.cs`, `IsoCountryCodeEmissionTests.cs`, `Iso3166EmissionTests.cs`, `EmissionDispatchTests.cs` (new), `Reference.Data.Primitives.Generator.Tests.csproj`
- Source material: `Mimir/gen/Reference.Contracts.Generator/*` and `Mimir/tests/Reference.Contracts.Generator.Tests/*` (copy from; Mimir-side deletion is Task 7)

**Interfaces:**
- Consumes: `Norse.Abstractions.Emit` (`AppendCSharp`, `Utf8NoBom`), `Norse.Primitives` types in *emitted* code (`Result<T>`, `Success<T>`, `Failure`, `ParseFailure`).
- Produces (emitted, all in `namespace Norse.Reference`):
  - into assembly `Norse.Reference.Data.Primitives`: `IsoCountryCode` (enum : ushort), `IsoCountryCodes` (`Parse(ReadOnlySpan<char>)`, `Parse(string?)`, `TryParse(ReadOnlySpan<char>, out IsoCountryCode)`), `Iso3166Country` (record), `Iso3166` (`All`, `Ids`)
  - into assembly `Norse.Reference.Data.Namespaces`: `ReferenceNamespaces` (`static readonly Guid Root`, `static readonly Guid Iso3166`)
  - into any other assembly: nothing.
- Diagnostics: `NORSE050` (missing CSV column, ex-`MIMIR001`), `NORSE051` (sanitized identifier collision, ex-`MIMIR002`) — category `Norse.Reference`, both Error. (Both currently sit in `AnalyzerReleases.Unshipped.md`, so renaming is free; the `MIMIR` prefix was a codename used as an operational identifier and is now also the wrong realm.)

- [ ] **Step 1: Copy the generator project** from `Mimir/gen/Reference.Contracts.Generator/` to `Mimisbrunnr/gen/Reference.Data.Primitives.Generator/`, renaming the csproj file to `Reference.Data.Primitives.Generator.csproj` (content unchanged except the `Description`, below) and `ReferenceContractsGenerator.cs` → `ReferenceDataPrimitivesGenerator.cs`. Mimisbrunnr's `gen/Directory.Build.props`/`.targets` already exist and are byte-identical to Mimir's — do not touch them. New csproj `Description`:

```xml
		<Description>Norse.Reference.Data.Primitives.Generator: the Roslyn IIncrementalGenerator that parses the UNSD M49 raw CSV at compile time and emits the IsoCountryCode enum with its tri-form (numeric/alpha-2/alpha-3) span-based parser and the Iso3166 dataset into Reference.Data.Primitives, and the ReferenceNamespaces constants into Reference.Data.Namespaces — dispatching on compilation assembly name.</Description>
```

- [ ] **Step 2: Copy the generator tests** to `Mimisbrunnr/tests/Reference.Data.Primitives.Generator.Tests/` (csproj renamed likewise; its `ProjectReference` retargeted to `../../gen/Reference.Data.Primitives.Generator/Reference.Data.Primitives.Generator.csproj`). Sweep namespaces in all copied files (both projects): `Norse.Reference.Contracts.Generator` → `Norse.Reference.Data.Primitives.Generator`, `Norse.Reference.Contracts.Generator.Tests` → `Norse.Reference.Data.Primitives.Generator.Tests`.

- [ ] **Step 3: Write the failing tests for the new behavior.** In `GeneratorTestHarness.cs`, add an `assemblyName` parameter threaded to `CSharpCompilation.Create` (default keeps existing tests running against the Primitives leg):

```csharp
	internal static string Run(string csv, string assemblyName = "Norse.Reference.Data.Primitives") =>
		string.Join("\n", Execute(csv, assemblyName).Result.GeneratedTrees.Select(tree => tree.ToString()));

	internal static Compilation RunAndCompile(string csv, string assemblyName = "Norse.Reference.Data.Primitives") =>
		Execute(csv, assemblyName).OutputCompilation;
```

(`Execute` and `CreateCompilation` gain the same pass-through parameter; `CSharpCompilation.Create(assemblyName, ...)` replaces the `"GeneratorTestAssembly"` literal. The generator class name in `Execute` becomes `ReferenceDataPrimitivesGenerator`.) New file `EmissionDispatchTests.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace Norse.Reference.Data.Primitives.Generator.Tests;

public sealed class EmissionDispatchTests
{
	const string Csv =
		"""
		Global Code;Global Name;Region Code;Region Name;Sub-region Code;Sub-region Name;Intermediate Region Code;Intermediate Region Name;Country or Area;M49 Code;ISO-alpha2 Code;ISO-alpha3 Code;Least Developed Countries (LDC);Land Locked Developing Countries (LLDC);Small Island Developing States (SIDS)
		001;World;019;Americas;021;Northern America;;;United States of America;840;US;USA;;;
		""";

	[Fact]
	void The_primitives_assembly_gets_the_enum_and_dataset_but_never_the_namespaces_class()
	{
		var generated = GeneratorTestHarness.Run(Csv);
		generated.ShouldContain("public enum IsoCountryCode : ushort");
		generated.ShouldContain("public static class Iso3166");
		generated.ShouldNotContain("class ReferenceNamespaces");
	}

	[Fact]
	void The_namespaces_assembly_gets_only_the_namespaces_class()
	{
		var generated = GeneratorTestHarness.Run(Csv, "Norse.Reference.Data.Namespaces");
		generated.ShouldContain("public static class ReferenceNamespaces");
		generated.ShouldContain("public static readonly global::System.Guid Root = new(\"8db01f36-dd6e-4cd1-8233-7ab1ec672fff\")");
		generated.ShouldContain("public static readonly global::System.Guid Iso3166 = new(\"");
		generated.ShouldNotContain("enum IsoCountryCode");
	}

	[Fact]
	void Any_other_assembly_gets_nothing() =>
		GeneratorTestHarness.Run(Csv, "Norse.Something.Else").ShouldBeEmpty();

	[Fact]
	void The_namespaces_emission_compiles_clean() =>
		GeneratorTestHarness.RunAndCompile(Csv, "Norse.Reference.Data.Namespaces")
			.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
}
```

Also update the two renamed assertions in `Iso3166EmissionTests.cs`: test `Emits_mimir_namespaces_with_literal_guids` is **replaced** (the namespaces class no longer emits on the Primitives leg — its assertions move to `EmissionDispatchTests`); delete it and keep the remaining three tests, adjusting `Emits_the_iso3166_dataset_with_a_v5_guid_for_the_us_row` and `Emits_the_ids_frozen_dictionary` untouched (they assert dataset content only).

- [ ] **Step 4: Run the new tests — expect FAIL** (`ReferenceDataPrimitivesGenerator` doesn't exist / dispatch not implemented): `dotnet test Mimisbrunnr/Mimisbrunnr.slnx` after adding both projects to `Mimisbrunnr.slnx` (see Step 7 — add the slnx entries now so the projects build).

- [ ] **Step 5: Implement the split.**

  5a. **`NamespacesEmitter.cs`** (new):

```csharp
using System.Text;
using Norse.Abstractions.Emit;

namespace Norse.Reference.Data.Primitives.Generator;

/// <summary>
/// Emits the generated <c>Norse.Reference.ReferenceNamespaces</c> constants — the realm's single
/// hand-minted root and every dataset namespace chained from it (sovereign namespace doctrine,
/// inversion spec §3.8). Emitted only into <c>Norse.Reference.Data.Namespaces</c>; the
/// browser-bound Primitives assembly never carries it.
/// </summary>
static class NamespacesEmitter
{
	/// <summary>Renders the generated namespaces source text.</summary>
	/// <param name="rootUuid">The hand-minted root namespace GUID text.</param>
	/// <param name="iso3166Namespace">The derived ISO 3166-1 dataset namespace.</param>
	internal static string Emit(string rootUuid, Guid iso3166Namespace)
	{
		StringBuilder sb = new();
		sb.AppendCSharp(
			$$"""
			// <auto-generated/>
			#nullable enable
			#pragma warning disable CS1591

			namespace Norse.Reference;

			/// <summary>
			/// Namespace GUIDs every dataset's precomputed RFC 9562 version 5 identifiers chain from,
			/// generated at compile time. <see cref="Root"/> is the realm's single hand-minted act
			/// (sovereign namespace doctrine); every other member derives from it.
			/// </summary>
			public static class ReferenceNamespaces
			{
				/// <summary>
				/// ReferenceNamespaces.Root — the single hand-minted act; every dataset namespace
				/// chains from it. FOREVER: changing it re-keys the universe.
				/// </summary>
				public static readonly global::System.Guid Root = new("{{rootUuid}}");

				/// <summary>The ISO 3166-1 dataset's namespace, chained from <see cref="Root"/>.</summary>
				public static readonly global::System.Guid Iso3166 = new("{{iso3166Namespace}}");
			}

			#pragma warning restore CS1591
			""");
		return sb.ToString();
	}
}
```

  5b. **`Iso3166Emitter.cs`**: delete the entire emitted `MimirNamespaces` class block (the `/// <summary>` through its closing `}` inside the template) and the emitter's own doc-comment references to it; expose the dataset-name constant for the generator (`internal const string Iso3166NamespaceName = "iso3166-1";` — change `const` to `internal const`). In the emitted `Iso3166Country` xmldoc, replace the cref sentence

```
			/// derived from <see cref="global::Norse.Reference.MimirNamespaces.Iso3166"/> and the
```
with prose (Primitives never references Namespaces, so a cref cannot bind):
```
			/// derived from the ISO 3166-1 dataset namespace (<c>ReferenceNamespaces.Iso3166</c>,
			/// published by Norse.Reference.Data.Namespaces) and the
```

  5c. **`ReferenceDataPrimitivesGenerator.cs`**: rename the class, keep `RootUuid` byte-identical, rename the two diagnostics to `NORSE050`/`NORSE051` (same messages, category `Norse.Reference`), update `AnalyzerReleases.Unshipped.md` to match, and restructure `Initialize`:

```csharp
	const string PrimitivesAssemblyName = "Norse.Reference.Data.Primitives";
	const string NamespacesAssemblyName = "Norse.Reference.Data.Namespaces";

	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var assemblyName = context.CompilationProvider
			.Select(static (compilation, _) => compilation.AssemblyName ?? string.Empty);

		context.RegisterSourceOutput(assemblyName, static (ctx, name) =>
		{
			if (name != NamespacesAssemblyName)
				return;

			var iso3166Namespace = Uuid5.Compute(new Guid(RootUuid), Iso3166Emitter.Iso3166NamespaceName);
			ctx.AddSource("ReferenceNamespaces.g.cs",
				SourceText.From(NamespacesEmitter.Emit(RootUuid, iso3166Namespace), Utf8NoBom.Encoding));
		});

		var csvTexts = context.AdditionalTextsProvider
			.Where(static file => Path.GetFileName(file.Path) == CsvFileName)
			.Select(static (file, ct) => file.GetText(ct)?.ToString() ?? string.Empty)
			.Combine(assemblyName);

		context.RegisterSourceOutput(csvTexts, static (ctx, pair) =>
		{
			var (csvText, name) = pair;
			if (name == PrimitivesAssemblyName)
				Emit(ctx, csvText);
		});
	}
```

(`Emit(ctx, csvText)` and everything below it is unchanged from the original except the diagnostic field renames.)

- [ ] **Step 6: Run all generator tests — expect PASS** (dispatch tests plus the pre-existing emission tests on the Primitives default leg).

- [ ] **Step 7: slnx.** Add to `Mimisbrunnr.slnx` a `/gen/` folder (modeled exactly on Mimir.slnx's) and the test project entry:

```xml
	<Folder Name="/gen/">
		<File Path="gen/Directory.Build.props" />
		<File Path="gen/Directory.Build.targets" />
		<Project Path="gen/Reference.Data.Primitives.Generator/Reference.Data.Primitives.Generator.csproj" />
	</Folder>
```
and under `/tests/` (alphabetical): `<Project Path="tests/Reference.Data.Primitives.Generator.Tests/Reference.Data.Primitives.Generator.Tests.csproj" />`.

- [ ] **Step 8: Commit** — `git add gen tests/Reference.Data.Primitives.Generator.Tests Mimisbrunnr.slnx && git commit -m "feat: reference generator moves in-realm, splits ReferenceNamespaces emission behind assembly-name dispatch"`.

---

### Task 3: Mimisbrunnr — `Reference.Data.Primitives` (the browser-supported surface)

**Files:**
- Create: `Mimisbrunnr/src/Reference.Data.Primitives/Reference.Data.Primitives.csproj`
- Create: `Mimisbrunnr/tests/Reference.Data.Primitives.Tests/` — `IsoCountryCodeParseTests.cs` (moved from Mimir's `Reference.Contracts.Tests`), `Iso3166DatasetTests.cs`, `BrowserCharterTests.cs`, `Reference.Data.Primitives.Tests.csproj`
- Modify: `Mimisbrunnr/Mimisbrunnr.slnx`

**Interfaces:**
- Consumes: the generator (Task 2), the CSV (Task 1), Svartálfheim `Norse.Primitives` (`Result<T>`/`Success<T>`/`Failure` in emitted parser).
- Produces: assembly `Norse.Reference.Data.Primitives` carrying `Norse.Reference.IsoCountryCode`, `IsoCountryCodes`, `Iso3166Country`, `Iso3166` — the package Mimir's `Reference.Contracts` (Task 7) and `Reference.Data.EntityFramework` (Task 4) consume.

- [ ] **Step 1: Write the failing tests.** `Reference.Data.Primitives.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Reference.Data.Primitives/Reference.Data.Primitives.csproj" />
	</ItemGroup>
</Project>
```

`IsoCountryCodeParseTests.cs` — verbatim move from `Mimir/tests/Reference.Contracts.Tests/IsoCountryCodeParseTests.cs`, namespace changed to `Norse.Reference.Data.Primitives.Tests` (all five tests unchanged: tri-form parse, unpadded numerics, garbage failure, zero-allocation span gate).

`Iso3166DatasetTests.cs` — the dataset-count pin moves here from `NamespaceSelfVerificationTests` (it needs only Primitives):

```csharp
namespace Norse.Reference.Data.Primitives.Tests;

public sealed class Iso3166DatasetTests
{
	[Fact]
	void The_dataset_carries_every_iso_bearing_row() =>
		// Arithmetic, verified against the committed export 2026-07-31: the raw file is 249 lines
		// = 1 header + 248 data rows, and ZERO rows lack ISO alpha codes — so the ISO-bearing
		// count equals the data-row count exactly. If this assertion ever fails, the EXPORT
		// changed (a UNSD reissue): re-run the arithmetic against the new file and update this
		// number with the new count-minus-ISO-less breakdown in this comment — never edit the
		// number to whatever passes.
		Iso3166.All.Count.ShouldBe(248);
}
```

`BrowserCharterTests.cs` — the WASM charter as a structural pin (spec §3.2 / §4):

```csharp
using System.Reflection;

namespace Norse.Reference.Data.Primitives.Tests;

public sealed class BrowserCharterTests
{
	[Fact]
	void The_assembly_references_no_ef_and_never_the_namespaces_assembly()
	{
		var referenced = typeof(IsoCountryCode).Assembly.GetReferencedAssemblies()
			.Select(a => a.Name!).ToList();
		referenced.Any(n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)).ShouldBeFalse();
		referenced.ShouldNotContain("Norse.Reference.Data.Namespaces");
	}
}
```

- [ ] **Step 2: Run — expect FAIL** (project doesn't exist).

- [ ] **Step 3: Create the project.** `Reference.Data.Primitives.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Reference.Data.Primitives: the browser-supported generated reference-data surface — the IsoCountryCode enum with tri-form (numeric/alpha-2/alpha-3) span parsing and the Iso3166 dataset with precomputed v5 identifiers. Reference-data primitives in Svartálfheim's sense, driven by third-party canonical datasets. No EF, no server types, no namespace constants — ever.</Description>
		<!-- Generated output lands in "Norse.Reference" (not "Norse.Reference.Data.Primitives") so
		     cross-context consumers reach IsoCountryCode/Iso3166 without a ".Data.Primitives"
		     segment — the shared-namespace convention between the two reference realms; the
		     override keeps IDE0130 aligned for any hand-written file that ever lands here. -->
		<RootNamespace>Norse.Reference</RootNamespace>
	</PropertyGroup>
	<ItemGroup>
		<AdditionalFiles Include="../../seeds/raw/UNSD — Methodology.csv" />
		<NorseRef Include="Primitives">
			<Repo>Svartalfheim</Repo>
		</NorseRef>
		<ProjectReference Include="../../gen/Reference.Data.Primitives.Generator/Reference.Data.Primitives.Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
		<SupportedPlatform Include="browser" />
	</ItemGroup>
</Project>
```

The `<SupportedPlatform Include="browser" />` item makes the platform-compatibility analyzer treat browser as a target this project must support — any browser-unsupported API use becomes a build error, which is the compile-time half of the charter (the test in Step 1 is the dependency half).

- [ ] **Step 4: slnx + run — expect PASS.** Add both projects to `Mimisbrunnr.slnx` (`/src/` and `/tests/`, alphabetical), then `dotnet test Mimisbrunnr/Mimisbrunnr.slnx`.

- [ ] **Step 5: Commit** — `git add src/Reference.Data.Primitives tests/Reference.Data.Primitives.Tests Mimisbrunnr.slnx && git commit -m "feat: Reference.Data.Primitives — browser-supported generated surface"`.

---

### Task 4: Mimisbrunnr — `Reference.Data.Namespaces` + self-verification

**Files:**
- Create: `Mimisbrunnr/src/Reference.Data.Namespaces/Reference.Data.Namespaces.csproj`
- Create: `Mimisbrunnr/tests/Reference.Data.Namespaces.Tests/` — `NamespaceSelfVerificationTests.cs` (moved/adapted from Mimir), `Reference.Data.Namespaces.Tests.csproj`
- Modify: `Mimisbrunnr/Mimisbrunnr.slnx`

**Interfaces:**
- Consumes: the generator (Task 2), Primitives (Task 3, tests only), `Norse.Primitives.Identifiers.DeterministicGuid` (Svartálfheim, tests only).
- Produces: assembly `Norse.Reference.Data.Namespaces` carrying `Norse.Reference.ReferenceNamespaces` (`Root`, `Iso3166`) — consumed by Yggdrasil's E2E tests (Task 8) and future server/tooling id computation. Never referenced by Primitives or any WASM-bound assembly.

- [ ] **Step 1: Write the failing tests.** `Reference.Data.Namespaces.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Reference.Data.Namespaces/Reference.Data.Namespaces.csproj" />
		<ProjectReference Include="../../src/Reference.Data.Primitives/Reference.Data.Primitives.csproj" />
	</ItemGroup>
</Project>
```

`NamespaceSelfVerificationTests.cs` — the two drift-guard tests from Mimir's file (the count test moved to Task 3), renamed surface:

```csharp
using System.Globalization;
using Norse.Primitives.Identifiers;

namespace Norse.Reference.Data.Namespaces.Tests;

public sealed class NamespaceSelfVerificationTests
{
	[Fact]
	void The_iso3166_namespace_rechains_from_root() =>
		new DeterministicGuid(ReferenceNamespaces.Root, "iso3166-1").Value.ShouldBe(ReferenceNamespaces.Iso3166);

	[Fact]
	void Every_shipped_row_guid_recomputes_via_deterministic_guid()
	{
		foreach (var country in Iso3166.All)
			new DeterministicGuid(ReferenceNamespaces.Iso3166, ((ushort)country.Code).ToString("D3", CultureInfo.InvariantCulture))
				.Value.ShouldBe(country.Id, $"{country.Alpha3} drifted");
	}
}
```

These are the guard that lets the generator keep its vendored `Uuid5` (netstandard2.0 cannot reference `DeterministicGuid`, net11.0): every emitted GUID is independently recomputed through the real primitive.

- [ ] **Step 2: Run — expect FAIL** (project doesn't exist).

- [ ] **Step 3: Create the project.** `Reference.Data.Namespaces.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Reference.Data.Namespaces: the generated ReferenceNamespaces constants — the realm's single hand-minted v5 root and every dataset namespace chained from it (sovereign namespace doctrine). Server, tooling, and tests only: the browser never computes what Primitives already bakes.</Description>
		<!-- Same shared "Norse.Reference" namespace as the rest of the generated surface. -->
		<RootNamespace>Norse.Reference</RootNamespace>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../../gen/Reference.Data.Primitives.Generator/Reference.Data.Primitives.Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
	</ItemGroup>
</Project>
```

(No `AdditionalFiles` — the namespaces leg derives entirely from the generator's `RootUuid` constant and dataset-name constants; no CSV involved.)

- [ ] **Step 4: slnx + run — expect PASS.**

- [ ] **Step 5: Commit** — `git commit -m "feat: Reference.Data.Namespaces — generated ReferenceNamespaces constants with DeterministicGuid drift guard"` (after `git add`).

---

### Task 5: Mimisbrunnr — rename `Reference.Data` → `Reference.Data.EntityFramework`

**Files:**
- Rename: `Mimisbrunnr/src/Reference.Data/` → `Mimisbrunnr/src/Reference.Data.EntityFramework/` (csproj file renamed with it)
- Rename: `Mimisbrunnr/tests/Reference.Data.Tests/` → `Mimisbrunnr/tests/Reference.Data.EntityFramework.Tests/` (ditto)
- Modify: every `.cs` in both (namespace sweep), `Mimisbrunnr.slnx`, `Mimisbrunnr/src/Reference.Data.Migrations/Reference.Data.Migrations.csproj` (ProjectReference path — temporarily, until Task 6 renames it too)

**Interfaces:**
- Consumes: Primitives (Task 3) — replaces the `NorseRef Reference.Contracts` (Mimir) edge, killing the cycle's data-realm→serving-realm direction.
- Produces: assembly/package `Norse.Reference.Data.EntityFramework`; namespace `Norse.Reference.Data.EntityFramework` for `Region`, `CountryOrArea`, `CountryOrAreaView`, `RegionNode`, `SubregionNode`, `IntermediateRegionNode`, `Classification`, `RegionLevel`, `ReferenceDbContext` (the context stays here in this plan — its excision is the companion well-seam plan's Task, not this one).

- [ ] **Step 1: `git mv`** both directories and both csproj files (`git mv src/Reference.Data src/Reference.Data.EntityFramework` etc. — four `git mv` operations total including the csproj file renames inside each).

- [ ] **Step 2: csproj.** New `Reference.Data.EntityFramework.csproj` content:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Reference.Data.EntityFramework: canonical reference-data entities (Region, CountryOrArea), the ReferenceDbContext, and CountryOrArea's View owned-JSON ancestor chain (named as a deliberate homage to the SQL view it replaced). Runtime library — referenced by the migrations tooling and Mímir's serving layer.</Description>
	</PropertyGroup>
	<ItemGroup>
		<NorseRef Include="Abstractions.Backend">
			<Repo>Asgard</Repo>
		</NorseRef>
		<NorseRef Include="Persistence.EntityFramework">
			<Repo>Urdarbrunnr</Repo>
			<Generator>true</Generator>
		</NorseRef>
		<ProjectReference Include="../Reference.Data.Primitives/Reference.Data.Primitives.csproj" />
	</ItemGroup>
</Project>
```

The `NorseRef Include="Reference.Contracts" / Repo=Mimir` block is **gone** — that line was the cycle.

- [ ] **Step 3: Namespace sweep.** In every `.cs` under `src/Reference.Data.EntityFramework/`: `namespace Norse.Reference.Data;` → `namespace Norse.Reference.Data.EntityFramework;` (9 files: `Classification.cs`, `CountryOrArea.cs`, `CountryOrAreaView.cs`, `IntermediateRegionNode.cs`, `ReferenceDbContext.cs`, `Region.cs`, `RegionLevel.cs`, `RegionNode.cs`, `SubregionNode.cs`). `IsoCountryCode` keeps resolving with no `using` — `Norse.Reference.Data.EntityFramework` nests inside `Norse.Reference`, where the generated surface lives. Same sweep in the test project (`Norse.Reference.Data.Tests` → `Norse.Reference.Data.EntityFramework.Tests`, plus `using Norse.Reference.Data;` → `using Norse.Reference.Data.EntityFramework;` where present). Test csproj's three `ProjectReference` paths update (`Reference.Data` → `Reference.Data.EntityFramework`; the two Migrations paths stay as-is until Task 6 — normalize their `\` separators to `/` while there).

- [ ] **Step 4: Fix the one inbound ProjectReference.** `Reference.Data.Migrations.csproj`: `<ProjectReference Include="../Reference.Data/Reference.Data.csproj" />` → `<ProjectReference Include="../Reference.Data.EntityFramework/Reference.Data.EntityFramework.csproj" />`. Also sweep the two migrations-project namespace *usings* if any file says `using Norse.Reference.Data;` (the contributor and design-time factories resolve entities via their own nesting after Task 6's rename; until then add the using where the compiler asks).

- [ ] **Step 5: slnx rename, build, test — expect PASS** for the whole realm (`InternalsVisibleTo` follows `$(AssemblyName).Tests` automatically).

- [ ] **Step 6: Commit** — `git commit -m "feat!: Reference.Data becomes Reference.Data.EntityFramework; Mimir Contracts edge severed"`.

---

### Task 6: Mimisbrunnr — rename the Migrations family, squash migrations, realm docs

**Files:**
- Rename: `src/Reference.Data.Migrations{,.PostgreSQL,.SqlServer}/` → `src/Reference.Data.EntityFramework.Migrations{,.PostgreSQL,.SqlServer}/` (+ csproj files)
- Rename: `tests/Reference.Data.Migrations{,.PostgreSQL,.SqlServer}.Tests/` → `tests/Reference.Data.EntityFramework.Migrations{,.PostgreSQL,.SqlServer}.Tests/` (+ csproj files)
- Modify: all namespaces within; regenerate `Migrations/` + snapshot in both provider projects; `Mimisbrunnr.slnx`; `Mimisbrunnr/README.md`; `Mimisbrunnr/CLAUDE.md`; the four per-project `README.md`s
- Delete + regenerate: `schema/*.sql` in both provider projects (the design-time DDL emission re-emits on the migrations re-add; verify the wildcard `EmbeddedResource` picks the regenerated file up)

**Interfaces:**
- Consumes: `Reference.Data.EntityFramework` (Task 5).
- Produces: packages `Norse.Reference.Data.EntityFramework.Migrations{,.PostgreSQL,.SqlServer}`; namespaces to match; `ReferenceDataSeedContributor` and `NorseReferenceMigrationContributor` unchanged in behavior — the migrations-service closure (Yggdrasil, Task 8) discovers them through the renamed `.PostgreSQL` package.

- [ ] **Step 1: `git mv`** all six directories + six csproj files.

- [ ] **Step 2: Namespace sweep** — `Norse.Reference.Data.Migrations` → `Norse.Reference.Data.EntityFramework.Migrations` (and `.PostgreSQL`/`.SqlServer`/`.Tests` variants) across every `.cs` in the six projects. Update the contributor's stale doc prose while in `ReferenceDataSeedContributor.cs`: "resolves through Mímir's generated `IsoCountryCode` surface" → "resolves through the realm's own generated `IsoCountryCode` surface (`Reference.Data.Primitives`)" (two spots — the class doc and `ResolveCountryCode`'s doc). Update all internal `ProjectReference` paths (each provider project → `../Reference.Data.EntityFramework.Migrations/...`; test csprojs likewise; normalize any `\` separators to `/`).

- [ ] **Step 3: Squash the migrations** (the assembly + namespace rename invalidates the checked-in designer/snapshot files; platform law is regenerate, never hand-edit — and the squash law keeps exactly one migration per realm). Per provider, from the Mimisbrunnr repo root:

```bash
rm -rf src/Reference.Data.EntityFramework.Migrations.PostgreSQL/Migrations
dotnet ef migrations add InitialCreate --project src/Reference.Data.EntityFramework.Migrations.PostgreSQL --startup-project src/Reference.Data.EntityFramework.Migrations.PostgreSQL
rm -rf src/Reference.Data.EntityFramework.Migrations.SqlServer/Migrations
dotnet ef migrations add InitialCreate --project src/Reference.Data.EntityFramework.Migrations.SqlServer --startup-project src/Reference.Data.EntityFramework.Migrations.SqlServer
```

Verify: the regenerated snapshot pins the same table names (`country_or_area`/`CountryOrArea` singular — diff the new snapshot's `ToTable` calls against the old one before deleting anything is easiest: copy old `Migrations/` aside first, then compare). Verify `schema/*.sql` regenerated by the Design chassis during the add (design-time DDL emission, Urðarbrunnr `Persistence.EntityFramework.Design`); if the file did not regenerate, follow the realm README's documented DDL step.

- [ ] **Step 4: Run the realm suite — expect PASS**, including the Testcontainers migration/seed tests (they exercise the regenerated migration against real Postgres).

- [ ] **Step 5: Realm docs** (boy-scout law, same change):
  - `Mimisbrunnr/README.md`: line 11's project list gains Primitives/Namespaces and the `.EntityFramework` names; line 15's status paragraph keeps its (now-true) `seeds/raw/` claim; the Migrations CLI block's four project paths (lines ~136-157) take the new names.
  - `Mimisbrunnr/CLAUDE.md`: title line and §project list likewise; add one sentence that the generated `IsoCountryCode`/`Iso3166`/`ReferenceNamespaces` surface now generates here (Primitives/Namespaces) and that Mimir consumes it.
  - The four per-project `README.md`s: title lines take the new package ids.
  - Both docs' "Mímir is the companion repository" descriptions: Mimir no longer hosts the generator or the enum — it is wire contracts + serving only.

- [ ] **Step 6: slnx** — rename the six entries; final `Mimisbrunnr.slnx` `/src/` order (alphabetical): `Reference.Data.EntityFramework`, `Reference.Data.EntityFramework.Migrations`, `.Migrations.PostgreSQL`, `.Migrations.SqlServer`, `Reference.Data.Namespaces`, `Reference.Data.Primitives`.

- [ ] **Step 7: Full realm suite once more — expect PASS. Commit** — `git commit -m "feat!: EntityFramework vendor-family names for the migrations projects; migrations squashed"`.

> **SHIP GATE (human):** Mimisbrunnr PR → CI green → merge → tag (expected `v0.0.9`) → packages published. Tasks 7-8's package-mode CI depends on it. Dev-mode work on Task 7 can proceed before the gate; its CI cannot go green until after.

---

### Task 7: Mimir — Contracts slims to wire records; `Reference.Seeds` dies; Web.Server renames its edge

**Files:**
- Modify: `Mimir/src/Reference.Contracts/Reference.Contracts.csproj`, `CountryResponse.cs`
- Delete: `Mimir/src/Reference.Seeds/` (both files), `Mimir/tests/Reference.Seeds.Tests/` (both files), `Mimir/seeds/` (raw CSV — now living in Mimisbrunnr — plus the two stale TSV copies), `Mimir/gen/Reference.Contracts.Generator/` (all files; moved in Task 2), `Mimir/tests/Reference.Contracts.Generator.Tests/` (ditto)
- Delete: `Mimir/tests/Reference.Contracts.Tests/IsoCountryCodeParseTests.cs`, `NamespaceSelfVerificationTests.cs` (moved in Tasks 3-4); Create: `WireContractShapeTests.cs`
- Modify: `Mimir/src/Reference.Web.Server/Reference.Web.Server.csproj`, `CountryQueryHandler.cs`, `ServiceCollectionExtensions.cs`, `Mimir/tests/Reference.Web.Server.Tests/CountryQueryHandlerTests.cs` (using sweep)
- Modify: `Mimir/Mimir.slnx`, `Mimir/README.md`, `Mimir/CLAUDE.md`

**Interfaces:**
- Consumes: `Norse.Reference.Data.Primitives` and `Norse.Reference.Data.EntityFramework` (Mimisbrunnr, Tasks 3/5-6).
- Produces: `Norse.Reference.Contracts` = wire records only (`CountryRequest`, `CountryResponse`, `IReferenceService`, `ReferencePolicies`), with the generated surface arriving by reference instead of by generation. `Norse.Reference.Seeds` ceases to exist.

- [ ] **Step 1: Branch** in `Mimir/`: `git switch -c feature/reference-data-inversion master`.

- [ ] **Step 2: Write the replacement Contracts test first** (the two moved test files leave `Reference.Contracts.Tests` empty, and the one-test-project-per-package law keeps the project). `WireContractShapeTests.cs`:

```csharp
using System.Reflection;
using System.Runtime.Serialization;

namespace Norse.Reference.Contracts.Tests;

public sealed class WireContractShapeTests
{
	[Theory]
	[InlineData(typeof(CountryRequest))]
	[InlineData(typeof(CountryResponse))]
	void Wire_records_carry_data_contract_with_unique_ordered_members(Type wireType)
	{
		wireType.GetCustomAttribute<DataContractAttribute>().ShouldNotBeNull();
		var orders = wireType.GetProperties()
			.Select(p => p.GetCustomAttribute<DataMemberAttribute>().ShouldNotBeNull().Order)
			.ToList();
		orders.ShouldBeUnique();
		orders.ShouldBe([.. orders.OrderBy(o => o)]);
	}
}
```

Delete the two moved test files in the same pass.

- [ ] **Step 3: Slim the Contracts project.** New `Reference.Contracts.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Reference.Contracts: the reference gRPC wire contracts — CountryRequest/CountryResponse and the IReferenceService surface — over Mímisbrunnr's generated reference-data primitives (IsoCountryCode, Iso3166), which flow to consumers by reference. WASM-lean by charter: no EF, no server types, ever.</Description>
		<!-- The wire contracts share the "Norse.Reference" namespace with Mímisbrunnr's generated
		     surface (the shared-namespace convention between the two reference realms); this
		     override keeps IDE0130's folder-match expectation aligned with that namespace. -->
		<RootNamespace>Norse.Reference</RootNamespace>
	</PropertyGroup>
	<ItemGroup>
		<NorseRef Include="Abstractions.Contracts">
			<Repo>Asgard</Repo>
		</NorseRef>
		<NorseRef Include="Reference.Data.Primitives">
			<Repo>Mimisbrunnr</Repo>
		</NorseRef>
		<!-- Overrides a known-vulnerable transitive version pulled in by System.ServiceModel.Primitives —
		     same fix Heimdall's AuthN.Services.csproj carries for the identical dependency. -->
		<PackageReference Include="System.Security.Cryptography.Xml" Version="11.*-*" />
		<PackageReference Include="System.ServiceModel.Primitives" Version="10.*" />
	</ItemGroup>
</Project>
```

Gone: the `AdditionalFiles` CSV line, the generator `ProjectReference`, and the direct Svartálfheim `Primitives` NorseRef (it now flows transitively through `Reference.Data.Primitives` — transitive-first law). The `Reference.Data.Primitives` NorseRef has no compile-time use in the wire records yet — it exists so WASM consumers of the wire contracts get the enum/parse surface in the same pull (spec §3.7).

- [ ] **Step 4: Fix `CountryResponse.cs` line 9** — the cref bound to the old generated class, and the "recomputable client-side" claim is doctrine-false (the browser never computes what's baked):

```csharp
	/// <summary>The deterministic v5 identifier — precomputed at generation time from the ISO 3166-1 dataset namespace (<c>ReferenceNamespaces.Iso3166</c>, published by Norse.Reference.Data.Namespaces) and the zero-padded numeric code. Recomputation is a server/tooling/tests act, never the client's.</summary>
```

- [ ] **Step 5: Deletions.** `git rm -r src/Reference.Seeds tests/Reference.Seeds.Tests gen/Reference.Contracts.Generator tests/Reference.Contracts.Generator.Tests seeds`.

- [ ] **Step 6: Web.Server edge rename.** In `Reference.Web.Server.csproj`: `<NorseRef Include="Reference.Data">` → `<NorseRef Include="Reference.Data.EntityFramework">` (Repo stays `Mimisbrunnr`). Using sweep: `using Norse.Reference.Data;` → `using Norse.Reference.Data.EntityFramework;` in `CountryQueryHandler.cs`, `ServiceCollectionExtensions.cs`, and `tests/Reference.Web.Server.Tests/CountryQueryHandlerTests.cs`. (The Midgard and Urðarbrunnr NorseRefs in this csproj are **left alone here** — their removal is the well-seam plan's business; touching them now would break the build with nothing to replace them.)

- [ ] **Step 7: slnx + docs.**
  - `Mimir.slnx`: remove the `Reference.Seeds`, `Reference.Contracts.Generator`, `Reference.Seeds.Tests`, `Reference.Contracts.Generator.Tests` project entries. Keep the `/gen/` folder with its two `File` entries (provisioned-but-empty, ready for a future generator).
  - `Mimir/README.md` + `Mimir/CLAUDE.md`: both still claim the realm is "a bare shell — no code" (flatly false since well-and-wire). Rewrite the status paragraphs: Mimir is the serving layer — `Reference.Contracts` (wire records), `Reference.Web.Server` (the gRPC implementation bound into Yggdrasil) — and the generated reference surface now generates in Mimisbrunnr (`Reference.Data.Primitives`/`.Namespaces`) and arrives by reference; `Reference.Seeds` is deleted, seeds live in Mimisbrunnr.

- [ ] **Step 8: Build + test the realm — expect PASS** (dev mode resolves the new NorseRefs against the sibling checkout). **Commit** — `git commit -m "feat!: Contracts slims to wire records; Reference.Seeds deleted; generator and surface now consumed from Mimisbrunnr"`.

> **SHIP GATE (human):** Mimir PR → CI green (needs Mimisbrunnr's published release) → merge → tag (expected `v0.0.2`) → publish.

---

### Task 8: Yggdrasil — pins, migrations-service edge, E2E rename

**Files:**
- Modify: `Yggdrasil/Directory.Packages.props`, `Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj`, `Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj`, `Yggdrasil/tests/Hosting.Web.Server.Tests/CountryLookupE2ETests.cs`

**Interfaces:**
- Consumes: the renamed Mimisbrunnr packages (published at the Task 6 gate) and Mimir `v0.0.2` (Task 7 gate).
- Produces: green composition-root CI on the new package ids; the E2E drift check now recomputes through `ReferenceNamespaces`.

- [ ] **Step 1: Branch** in `Yggdrasil/`: `git switch -c feature/reference-data-inversion master` (verify `git branch --show-current` — this realm often has other branches in flight).

- [ ] **Step 2: CPM pins.** In `Directory.Packages.props`: bump `<MimisbrunnrVersion>` and `<MimirVersion>` to the versions actually tagged at the ship gates (expected `0.0.9` / `0.0.2` — verify against the real tags). Replace the `Norse.Reference.*` block:

```xml
		<!-- Mimisbrunnr -->
		<PackageVersion Include="Norse.Reference.Data.EntityFramework" Version="$(MimisbrunnrVersion)" />
		<PackageVersion Include="Norse.Reference.Data.EntityFramework.Migrations" Version="$(MimisbrunnrVersion)" />
		<PackageVersion Include="Norse.Reference.Data.EntityFramework.Migrations.PostgreSQL" Version="$(MimisbrunnrVersion)" />
		<PackageVersion Include="Norse.Reference.Data.EntityFramework.Migrations.SqlServer" Version="$(MimisbrunnrVersion)" />
		<PackageVersion Include="Norse.Reference.Data.Namespaces" Version="$(MimisbrunnrVersion)" />
		<PackageVersion Include="Norse.Reference.Data.Primitives" Version="$(MimisbrunnrVersion)" />
		<!-- Mimir -->
		<PackageVersion Include="Norse.Reference.Contracts" Version="$(MimirVersion)" />
		<PackageVersion Include="Norse.Reference.Web.Server" Version="$(MimirVersion)" />
```

(`Norse.Reference.Seeds` is deleted, not renamed — no pin.)

- [ ] **Step 3: Migrations-service edge.** `Hosting.Migrations.Service.csproj`: `<NorseRef Include="Reference.Data.Migrations.PostgreSQL">` → `<NorseRef Include="Reference.Data.EntityFramework.Migrations.PostgreSQL">` (Repo stays `Mimisbrunnr`; keep any `Generator` metadata exactly as found).

- [ ] **Step 4: E2E test.** `Hosting.Web.Server.Tests.csproj`: `<PackageReference Include="Norse.Reference.Data.Migrations" VersionOverride="0.0.8" />` → `<PackageReference Include="Norse.Reference.Data.EntityFramework.Migrations" VersionOverride="0.0.9" />` (match the real tag), and add `<PackageReference Include="Norse.Reference.Data.Namespaces" VersionOverride="0.0.9" />` (this tests project manages versions locally — CPM is off here). In `CountryLookupE2ETests.cs`: the two `using Norse.Reference.Data...` lines take the `.EntityFramework` names, and line 139 becomes:

```csharp
		DeterministicGuid local = new(ReferenceNamespaces.Iso3166, expectedD3);
```

- [ ] **Step 5: Build + run the Yggdrasil suite — expect PASS** (E2E requires Docker; skip-if-unavailable behavior is already built into the fixtures). **Commit** on the feature branch.

> **SHIP GATE (human):** Yggdrasil PR → CI green → merge.

---

### Task 9: Bifröst + Glitnir living docs (staged only — no commits anywhere in this task)

**Files:**
- Modify: `Bifrost.slnx`, `CLAUDE.md` (Bifröst), `README.md` (Bifröst)
- Modify: `Glitnir/docs/codenames.md`, `Glitnir/docs/decomposition.md`, `Glitnir/README.md`

**Interfaces:** none — documentation truth only. Everything staged with `git add`, nothing committed (Bifröst stays on `master`; Glitnir edits await Buvy's commit).

- [ ] **Step 1: `Bifrost.slnx`.**
  - Rename the four Mimisbrunnr `/Reference/src/` project paths and five `/Reference/tests/` paths to the new names; add the two new src projects and three new test projects (alphabetical).
  - Add a `/Reference/gen/` folder (modeled on `/Persistence/gen/`) with Mimisbrunnr's `gen/Directory.Build.props`, `gen/Directory.Build.targets`, and the generator project.
  - **Gap found during recon, fixed here as boy-scout law:** `Bifrost.slnx` lists no Mimir projects at all. Add them under the existing `/Reference/` tree (one bounded context, two repos — the same-namespace doctrine made visible): `Mimir/src/Reference.Contracts`, `Mimir/src/Reference.Web.Server` in `/Reference/src/`, and `Mimir/tests/Reference.Contracts.Tests`, `Mimir/tests/Reference.Web.Server.Tests` in `/Reference/tests/`, plus Mimir's root `File` entries alongside Mimisbrunnr's in `/Reference/`. (Flagged for review — if the shared-folder shape reads wrong, a sibling `/Reference.Serving/` folder is the fallback; the gap itself must not survive.)
  - Verify: `dotnet build Bifrost.slnx` succeeds.
- [ ] **Step 2: Bifröst realm tables.** `CLAUDE.md` §2 and `README.md` realm rows for Mímisbrunnr/Mímir: Mímisbrunnr's namespace column becomes `Norse.Reference.Data.*` with a phrase noting the generated primitives/namespaces surface lives here; Mímir's description drops any implication it generates the surface. One-line edits, not rewrites.
- [ ] **Step 3: Glitnir dictionaries** — follow the house pattern (stale rows stay; append dated amendments):
  - `decomposition.md`: append `**Amendment (2026-08-01):** Mímisbrunnr's family is now Norse.Reference.Data.EntityFramework[.Migrations.*] plus the new Norse.Reference.Data.Primitives (browser-supported generated IsoCountryCode/Iso3166) and Norse.Reference.Data.Namespaces (generated ReferenceNamespaces); Mímir's Norse.Reference.Seeds is deleted and its generator relocated to Mímisbrunnr. Full design: Platform/specs/2026-08-01-reference-data-dependency-inversion-design.md.`
  - `codenames.md`: append the same amendment note under the existing 2026-07-25 one (codenames themselves are unchanged; only the function columns moved).
  - `Glitnir/README.md` line 64 (Mímisbrunnr row): update the function summary in place — this file is a living doc, not a point-in-time record.
- [ ] **Step 4: Stage everything** — `git add` in Bifröst for its three files, `git -C Glitnir add` for the three docs. **Stop. No commits.**

---

## Self-Review Notes (already applied)

- **Spec coverage:** §3.1 table → Tasks 2-6; §3.2 charter + namespace → Task 3; §3.3 → Task 4; §3.4 ripple → Tasks 5-9 (every bullet has a home); §3.5 → Tasks 1, 7; §3.6 → Task 2; §3.7 → Task 7; §3.8 is doctrine (no code); §4 testing → Tasks 2-4, 6, 8; §5 out-of-scope respected (no Midgard/provider/DbContext changes — Task 7 Step 6 explicitly leaves those NorseRefs alone for the well-seam plan).
- **Type consistency:** `ReferenceDataPrimitivesGenerator`, `NamespacesEmitter.Emit(string, Guid)`, `Iso3166Emitter.Iso3166NamespaceName` (internal const), `GeneratorTestHarness.Run(string, string = "Norse.Reference.Data.Primitives")` — used identically in Tasks 2-4. Namespace `Norse.Reference` for all generated types; `Norse.Reference.Data.EntityFramework[.Migrations...]` for the renamed families.
- **Known-good invariants pinned by tests:** root GUID byte-identity (dispatch test asserts the literal), TSV byte-identity (Task 1), 248-row count (Task 3), v5 drift guard via `DeterministicGuid` (Task 4), table-name stability (Task 6 snapshot diff).
- **Deliberate non-goals:** Mimir keeps its Midgard/Urðarbrunnr NorseRefs (companion plan); no `IncludeGeneratorInPackage` target is added for the relocated generator — its emitted types compile *into* Primitives/Namespaces and ship in those DLLs; the generator itself never runs in consumer compilations, so nothing needs it in `analyzers/dotnet/cs/` (unlike Urðarbrunnr's closure-scanning generators).
