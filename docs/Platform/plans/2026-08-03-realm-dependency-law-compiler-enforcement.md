# The Law of the Realms — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Pairs with superpowers:test-driven-development on every task.

**Goal:** Ship NORSE070–073 — the compiler-enforced realm dependency law — from Svartálfheim as a standalone analyzer package, deliver it platform-wide through the Ginnungagap scatter with no opt-out, and remediate the one live conviction on a master branch (Himinbjörg's personal-data-download endpoint).

**Architecture:** Spec: `../specs/2026-08-03-realm-dependency-law-compiler-enforcement-design.md` — read it before any task; every ruling in it is settled law. One Roslyn analyzer project (`Svartalfheim/gen/Architecture.Analyzers`, assembly `Norse.Architecture.Analyzers`), jurisdiction derived entirely from assembly names. Two analyzers inside it: `WireFormatAnalyzer` (NORSE070, syntax + operation level) and `RealmReferenceAnalyzer` (NORSE071/072/073, compilation level over `ReferencedAssemblyNames`). Delivery: workspace mode via a new unconditional analyzer `ProjectReference` in Bifröst's root `Directory.Build.targets`; standalone/CI mode via a `PackageReference` added to the Ginnungagap-scattered `config/Directory.Build.props`.

**Tech Stack:** .NET 11 preview / C# 15 (analyzer itself netstandard2.0), Roslyn `DiagnosticAnalyzer`, xUnit v3 + Shouldly on MTP, MSBuild `Choose` delivery, Ginnungagap scatter.

## Global Constraints

- Read `../../house-rules.md` in full before implementing any task (tabs, `sealed`, target-typed `new()`, collection expressions, expression bodies, hoisted usings, no string concatenation, XML docs in src — note gen projects NoWarn CS1591, so the analyzer project documents by choice, not obligation).
- **The in-flight PII branch is sacred:** `Svartalfheim` currently has `feature/pii-primitives` checked out (4 unpushed commits). Do NOT touch, rebase, delete, or commit to it. Phase A starts by checking out `master` (the branch stays behind, intact, to be rebased and carried forward after this law ships).
- **Branching:** Phase A on `Svartalfheim` branch `feature/law-of-the-realms` off `master`; Phase C on `Himinbjorg` branch `feature/wire-format-remediation` off `master`; Phase D on `Yggdrasil` branch `feature/law-package-pin` off `master`. Commits local and unpushed; Buvy pushes/PRs at ship gates. **Bifröst itself never branches** — its one tracked-file edit (root `Directory.Build.targets`, Phase B) is staged on `master`, never committed by an agent.
- **Ginnungagap is edited at the source:** Phase B modifies `.github/config/Directory.Build.props` (the scatter SOURCE in the org-defaults repo, workspace path `../.github` relative to Bifröst). The scattered COPIES in each realm remain hands-off — never edit a realm's `Directory.Build.props`/`config/*` directly; the scatter workflow distributes after Buvy commits.
- **Commit policy:** subagents commit only files they authored, named explicitly — never `git add -A`/`git add .`. In Ginnungagap and Bifröst: stage only, no commits.
- **Diagnostic IDs NORSE070–073 + NORSE079 (the meta-strike: any `[SuppressMessage]` naming a `NORSE07x` rule is itself a conviction — ruled at final review 2026-08-03), NotConfigurable, Error severity** — claimed in the forge ledger (`gen/Primitives.Analyzers/Diagnostics.cs` header). NORSE060 stays untouched.
- **Published-surface widening (ruled at final review 2026-08-03):** vendor drops count — a name containing the `.Components.` segment (`Norse.AuthN.Components.FluentUI`) is a legal target under the surface arm (Bragi/hosts consume drops by design); the NORSE073 stricture still keys on the exact `.Components` suffix only. The earlier not-a-surface pins in Task 2/Task 5 fixtures are superseded (final-review fix wave flips them).
- **SDK implicit-usings hazard:** the scattered props (Task 6) also carry `<Using Remove="System.Net.Http.Json" />` — the .NET 11 SDK injects that namespace as a global using into every project, which would convict the entire platform on attach. Border realms re-add locally.
- **Function vocabulary (law):** `Primitives`, `Abstractions`, `Persistence`, `Messaging`, `Infrastructure`, `Hosting`, `DesignSystem`.
- **Exempt suffixes:** `.Tests`, `.Benchmarks`, `.Aot.Smoke`, `.Analyzers`, `.Generator`, `.Generators` (the singular `.Generator` matches the platform's real gen naming — `Abstractions.Web.Server.Generator` — a reality-alignment widening of the spec's `*.Generators`, recorded here).
- **Banned namespace roots (NORSE070 v1):** `System.Text.Json`, `Newtonsoft.Json`, `System.Xml`, `System.Runtime.Serialization.Json`, `System.Net.Http.Json`, `Microsoft.AspNetCore.Http.Json`, `ProtoBuf`, `Grpc`, `Google.Protobuf`, `MessagePack`. Prefix match is segment-exact (`Grpc` matches `Grpc.Core`, never `Grpcish`). **Banned symbols:** `System.Runtime.Serialization.DataContractSerializer`, `System.Runtime.Serialization.XmlObjectSerializer`, `Microsoft.AspNetCore.Http.Results.Json`, `Microsoft.AspNetCore.Http.TypedResults.Json`. Contract attributes (`[DataContract]`/`[DataMember]`/`[ServiceContract]`/`[OperationContract]`) are blessed — never flagged.
- **Test naming:** sentence-shaped (`Strikes_norse070_when_...`); test classes `public sealed`; methods bare `async Task`/`void`; Shouldly/Xunit usings are global — never per-file.
- Test filtering is MTP syntax: `dotnet test tests/Architecture.Analyzers.Tests -- --filter-class "*.WireFormatAnalyzerTests"` (VSTest `--filter` does not work).
- Touched projects' tests green before each commit; `dotnet build Svartalfheim.slnx` zero warnings.

## File Structure

```
Svartalfheim/
  gen/Architecture.Analyzers/Architecture.Analyzers.csproj   (new — packable standalone, overrides gen defaults)
  gen/Architecture.Analyzers/Diagnostics.cs                  (new — NORSE070-073 descriptors)
  gen/Architecture.Analyzers/RealmIdentity.cs                (new — name parsing: vocabulary, brand, family, exemption)
  gen/Architecture.Analyzers/WireFormatAnalyzer.cs           (new — NORSE070)
  gen/Architecture.Analyzers/RealmReferenceAnalyzer.cs       (new — NORSE071/072/073)
  gen/Primitives.Analyzers/Diagnostics.cs                    (modify — header ledger claims NORSE070-079)
  tests/Architecture.Analyzers.Tests/*.cs                    (new project — harness + per-strike tests)
  Svartalfheim.slnx                                          (modify — wire both projects)
Bifrost/
  Directory.Build.targets                                    (modify — workspace-mode analyzer attach; STAGE ONLY)
.github/  (Ginnungagap, ../.github)
  config/Directory.Build.props                               (modify — package-mode delivery Choose; STAGE ONLY)
Himinbjorg/
  src/Identity.Web.Server/IdentityComponentsEndpointRouteBuilderExtensions.cs  (modify — excise DownloadPersonalData)
  src/Identity.Web.Server/Components/Pages/Manage/*          (modify — remove the download affordance; discovered by grep)
Yggdrasil/
  Directory.Packages.props                                   (modify — CPM pin for Norse.Architecture.Analyzers)
```

---

## Phase A — Svartálfheim (`feature/law-of-the-realms`)

> First action: `git checkout master` (leaves `feature/pii-primitives` intact), then `git checkout -b feature/law-of-the-realms`.

### Task 1: Project scaffold, NORSE070–073 descriptors, ledger claim

**Files:**
- Create: `gen/Architecture.Analyzers/Architecture.Analyzers.csproj`, `gen/Architecture.Analyzers/Diagnostics.cs`
- Create: `tests/Architecture.Analyzers.Tests/Architecture.Analyzers.Tests.csproj`
- Modify: `gen/Primitives.Analyzers/Diagnostics.cs` (header ledger only), `Svartalfheim.slnx` (`dotnet sln Svartalfheim.slnx add gen/Architecture.Analyzers tests/Architecture.Analyzers.Tests`)

**Interfaces (Produces):** `static class Diagnostics` with `DiagnosticDescriptor` fields `WireFormatOutsideBorder` (NORSE070), `MidgardTakenAsDependency` (NORSE071), `CrossRealmReach` (NORSE072), `ComponentImpurity` (NORSE073) — all Error, enabled by default, `WellKnownDiagnosticTags.NotConfigurable`, category `"Norse.Architecture"`.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Architecture.Analyzers.Tests;

public sealed class DiagnosticsTests
{
	[Theory]
	[InlineData("NORSE070")]
	[InlineData("NORSE071")]
	[InlineData("NORSE072")]
	[InlineData("NORSE073")]
	void Every_strike_is_a_non_configurable_error(string id)
	{
		var descriptor = All().Single(d => d.Id == id);
		descriptor.DefaultSeverity.ShouldBe(DiagnosticSeverity.Error);
		descriptor.IsEnabledByDefault.ShouldBeTrue();
		descriptor.CustomTags.ShouldContain(WellKnownDiagnosticTags.NotConfigurable);
		descriptor.Category.ShouldBe("Norse.Architecture");
	}

	static DiagnosticDescriptor[] All() =>
		[Diagnostics.WireFormatOutsideBorder, Diagnostics.MidgardTakenAsDependency, Diagnostics.CrossRealmReach, Diagnostics.ComponentImpurity];
}
```

(`using Microsoft.CodeAnalysis;` hoisted at top — the test project references `Microsoft.CodeAnalysis.CSharp` like `Primitives.Analyzers.Tests` does.)

- [ ] **Step 2: Create both csproj files, run to verify failure**

`gen/Architecture.Analyzers/Architecture.Analyzers.csproj` — the gen `Directory.Build.props` supplies netstandard2.0/`IsRoslynComponent`/`Microsoft.CodeAnalysis.CSharp`/`InternalsVisibleTo`; this project overrides packability because unlike every sibling it ships standalone:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Architecture.Analyzers: the Law of the Realms. NORSE070 wire format never leaves Midgard/Yggdrasil (contract attributes are blessed declarations of intent; anything naming or executing a concrete encoding is not); NORSE071 Midgard is consumed by the world tree alone; NORSE072 realms are bounded contexts whose only doors are published surfaces (.Contracts/.Services/.Components); NORSE073 component RCLs stay platform-free. Jurisdiction derives entirely from assembly names — no configuration exists, and none is honored. Delivered to every realm by the Ginnungagap scatter; packed standalone (analyzers/dotnet/cs) because no-opt-out delivery cannot ride inside a host package — attachment would be contingent on referencing the host, and the law leans on no dependency, not even the forge everyone builds on.</Description>
		<IsPackable>true</IsPackable>
		<IncludeBuildOutput>false</IncludeBuildOutput>
	</PropertyGroup>
	<ItemGroup>
		<None Include="bin/$(Configuration)/netstandard2.0/$(AssemblyName).dll" Pack="true" PackagePath="analyzers/dotnet/cs/" Visible="false" />
	</ItemGroup>
</Project>
```

`tests/Architecture.Analyzers.Tests/Architecture.Analyzers.Tests.csproj` (mirror of `Primitives.Analyzers.Tests.csproj`, minus the ServiceModel packages it doesn't need):

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="*" PrivateAssets="all" />
		<ProjectReference Include="../../gen/Architecture.Analyzers/Architecture.Analyzers.csproj" />
	</ItemGroup>
</Project>
```

Run: `dotnet test tests/Architecture.Analyzers.Tests`
Expected: FAIL — `Diagnostics` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

`gen/Architecture.Analyzers/Diagnostics.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace Norse.Architecture.Analyzers;

#pragma warning disable RS2008 // No analyzer-release ledger, matching the platform's other generators/analyzers.

/// <summary>
/// NORSE070-079 — the architecture-law block, claimed 2026-08-03 (grep-confirmed clean at authoring;
/// the authoritative per-block ledger lives in Primitives.Analyzers' Diagnostics.cs header). All four
/// strikes are NotConfigurable errors: the law is not a severity preference, and no consuming realm
/// may downgrade it. Spec: ../Glitnir/docs/Platform/specs/2026-08-03-realm-dependency-law-compiler-enforcement-design.md.
/// </summary>
static class Diagnostics
{
	const string Category = "Norse.Architecture";

	public static readonly DiagnosticDescriptor WireFormatOutsideBorder = new(
		"NORSE070", "Wire format outside Midgard/Yggdrasil",
		"'{0}' is wire-format machinery — encodings exist in Infrastructure (Midgard) and Hosting (Yggdrasil) alone; declare intent with contract attributes and let the edge own the bytes", Category,
		DiagnosticSeverity.Error, isEnabledByDefault: true, customTags: WellKnownDiagnosticTags.NotConfigurable);

	public static readonly DiagnosticDescriptor MidgardTakenAsDependency = new(
		"NORSE071", "Midgard taken as a dependency",
		"Assembly '{0}' references '{1}' — Infrastructure (Midgard) is consumed by Hosting (Yggdrasil) alone and publishes no surface; no realm takes Midgard as a dependency", Category,
		DiagnosticSeverity.Error, isEnabledByDefault: true, customTags: WellKnownDiagnosticTags.NotConfigurable);

	public static readonly DiagnosticDescriptor CrossRealmReach = new(
		"NORSE072", "Cross-realm reach outside published surfaces",
		"Assembly '{0}' references '{1}' — {2}; realms are bounded contexts whose only doors are .Contracts, .Services, and .Components", Category,
		DiagnosticSeverity.Error, isEnabledByDefault: true, customTags: WellKnownDiagnosticTags.NotConfigurable);

	public static readonly DiagnosticDescriptor ComponentImpurity = new(
		"NORSE073", "Component assembly impurity",
		"Component assembly '{0}' references '{1}' — .Components consumes foundation and published surfaces only, even within its own realm, so render mode stays a deployment detail", Category,
		DiagnosticSeverity.Error, isEnabledByDefault: true, customTags: WellKnownDiagnosticTags.NotConfigurable);
}
```

`gen/Primitives.Analyzers/Diagnostics.cs` — extend the header doc comment's ledger sentence (keep everything else byte-identical): after "NORSE050-051 Mímisbrunnr", append "— and NORSE070-079 now claimed for the architecture-law block (Architecture.Analyzers, 2026-08-03)". Do not touch the NORSE060 descriptor.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Architecture.Analyzers.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add gen/Architecture.Analyzers/Architecture.Analyzers.csproj gen/Architecture.Analyzers/Diagnostics.cs gen/Primitives.Analyzers/Diagnostics.cs tests/Architecture.Analyzers.Tests/Architecture.Analyzers.Tests.csproj tests/Architecture.Analyzers.Tests/DiagnosticsTests.cs Svartalfheim.slnx
git commit -m "feat: Architecture.Analyzers scaffold — NORSE070-073 descriptors, ledger claim"
```

### Task 2: `RealmIdentity` — jurisdiction from names alone

**Files:**
- Create: `gen/Architecture.Analyzers/RealmIdentity.cs`
- Test: `tests/Architecture.Analyzers.Tests/RealmIdentityTests.cs`

**Interfaces (Produces):** `static class RealmIdentity` —
- `bool IsExempt(string assemblyName)` — exempt-suffix match (Global Constraints list), ordinal.
- `string? FunctionOf(string assemblyName)` — first `.`-segment present in the function vocabulary, else null.
- `bool IsWireBorder(string assemblyName)` — `FunctionOf` is `"Infrastructure"` or `"Hosting"`.
- `string? BrandOf(string assemblyName)` — segments before the first vocabulary segment, joined with `.`; null when no vocabulary segment exists.
- `string? FamilyOf(string assemblyName, string brand)` — the segment immediately after `brand + "."`, or null when the name doesn't start with that brand.
- `bool IsPublishedSurface(string assemblyName)` — ends `.Contracts`, `.Services`, or `.Components` (ordinal).
- `bool IsFoundation(string assemblyName, string brand)` — `FunctionOf` ∈ {`Primitives`, `Abstractions`, `Persistence`, `Messaging`} with matching brand, OR the exact name `{brand}.DesignSystem.Tokens`.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Architecture.Analyzers.Tests;

public sealed class RealmIdentityTests
{
	[Theory]
	[InlineData("Norse.Primitives.Tests")]
	[InlineData("Norse.Identity.Web.Server.Tests")]
	[InlineData("Norse.Primitives.Benchmarks")]
	[InlineData("Norse.Primitives.Aot.Smoke")]
	[InlineData("Norse.Primitives.Analyzers")]
	[InlineData("Norse.Architecture.Analyzers")]
	[InlineData("Norse.Abstractions.Web.Server.Generator")]
	void Exempts_evidence_rigs_and_build_tooling(string name) =>
		RealmIdentity.IsExempt(name).ShouldBeTrue();

	[Theory]
	[InlineData("Norse.Primitives", "Primitives")]
	[InlineData("Norse.Infrastructure.Web.Server", "Infrastructure")]
	[InlineData("Acme.Corp.Primitives", "Primitives")]
	[InlineData("Norse.Identity.Web.Server", null)]
	void Finds_the_function_segment_by_vocabulary(string name, string? expected) =>
		RealmIdentity.FunctionOf(name).ShouldBe(expected);

	[Theory]
	[InlineData("Norse.Infrastructure.Web.Server", true)]
	[InlineData("Norse.Hosting.Web.Client", true)]
	[InlineData("Norse.Primitives", false)]
	[InlineData("Norse.Identity.Web.Server", false)]
	void Wire_border_is_infrastructure_or_hosting(string name, bool expected) =>
		RealmIdentity.IsWireBorder(name).ShouldBe(expected);

	[Theory]
	[InlineData("Norse.Primitives", "Norse")]
	[InlineData("Acme.Corp.Persistence.EntityFramework", "Acme.Corp")]
	[InlineData("Norse.Identity.Web.Server", null)]
	void Brand_is_everything_before_the_first_vocabulary_segment(string name, string? expected) =>
		RealmIdentity.BrandOf(name).ShouldBe(expected);

	[Theory]
	[InlineData("Norse.Identity.Web.Server", "Norse", "Identity")]
	[InlineData("Norse.Reference.Data.Entities", "Norse", "Reference")]
	[InlineData("Norse.Primitives.Ingestion", "Norse", "Primitives")]
	[InlineData("Acme.Identity", "Norse", null)]
	void Family_is_the_segment_after_the_brand(string name, string brand, string? expected) =>
		RealmIdentity.FamilyOf(name, brand).ShouldBe(expected);

	[Theory]
	[InlineData("Norse.AuthN.Contracts", true)]
	[InlineData("Norse.AuthN.Services", true)]
	[InlineData("Norse.AuthN.Components", true)]
	[InlineData("Norse.AuthN.Components.FluentUI", false)]
	[InlineData("Norse.Identity.EntityFramework", false)]
	void Published_surfaces_are_contracts_services_components(string name, bool expected) =>
		RealmIdentity.IsPublishedSurface(name).ShouldBe(expected);

	[Theory]
	[InlineData("Norse.Primitives", true)]
	[InlineData("Norse.Abstractions.Keys", true)]
	[InlineData("Norse.Persistence.EntityFramework.PostgreSQL", true)]
	[InlineData("Norse.Messaging.NServiceBus", true)]
	[InlineData("Norse.DesignSystem.Tokens", true)]
	[InlineData("Norse.DesignSystem.Stories", false)]
	[InlineData("Norse.Infrastructure.Keys", false)]
	[InlineData("Norse.Identity.EntityFramework", false)]
	void Foundation_is_the_four_families_plus_the_token_seed(string name, bool expected) =>
		RealmIdentity.IsFoundation(name, "Norse").ShouldBe(expected);
}
```

Note the deliberate pin: `Norse.AuthN.Components.FluentUI` is **not** a published surface — the suffix match is exact-end, so vendor-specific component drops stay realm-internal and reachable only via their realm or the tree. If this reads wrong during implementation, HALT and raise it — do not widen the suffix silently.

- [ ] **Step 2: Run to verify failure** — `dotnet test tests/Architecture.Analyzers.Tests -- --filter-class "*.RealmIdentityTests"`

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Immutable;

namespace Norse.Architecture.Analyzers;

/// <summary>
/// Jurisdiction from names alone (spec §3): the function vocabulary is law, the brand is everything
/// before the first recognized segment, realm families are inferred as the segment after the brand —
/// never enumerated, so onboarding a realm needs no analyzer release. Pure string functions;
/// no configuration exists, and none is honored.
/// </summary>
static class RealmIdentity
{
	public static readonly ImmutableHashSet<string> FunctionVocabulary =
		["Primitives", "Abstractions", "Persistence", "Messaging", "Infrastructure", "Hosting", "DesignSystem"];

	static readonly ImmutableArray<string> _exemptSuffixes =
		[".Tests", ".Benchmarks", ".Aot.Smoke", ".Analyzers", ".Generator", ".Generators"];

	static readonly ImmutableHashSet<string> _foundationFunctions =
		["Primitives", "Abstractions", "Persistence", "Messaging"];

	static readonly ImmutableArray<string> _publishedSurfaceSuffixes =
		[".Contracts", ".Services", ".Components"];

	public static bool IsExempt(string assemblyName) =>
		_exemptSuffixes.Any(s => assemblyName.EndsWith(s, StringComparison.Ordinal));

	public static string? FunctionOf(string assemblyName) =>
		assemblyName.Split('.').FirstOrDefault(FunctionVocabulary.Contains);

	public static bool IsWireBorder(string assemblyName) =>
		FunctionOf(assemblyName) is "Infrastructure" or "Hosting";

	public static string? BrandOf(string assemblyName)
	{
		var segments = assemblyName.Split('.');
		var index = Array.FindIndex(segments, FunctionVocabulary.Contains);
		return index > 0 ?
			string.Join(".", segments.Take(index)) :
			null;
	}

	public static string? FamilyOf(string assemblyName, string brand)
	{
		var prefix = $"{brand}.";
		return assemblyName.StartsWith(prefix, StringComparison.Ordinal) ?
			assemblyName[prefix.Length..].Split('.')[0] :
			null;
	}

	public static bool IsPublishedSurface(string assemblyName) =>
		_publishedSurfaceSuffixes.Any(s => assemblyName.EndsWith(s, StringComparison.Ordinal));

	public static bool IsFoundation(string assemblyName, string brand) =>
		FamilyOf(assemblyName, brand) is { } family &&
		(_foundationFunctions.Contains(family) || assemblyName == $"{brand}.DesignSystem.Tokens");
}
```

(netstandard2.0 note: `assemblyName[prefix.Length..]` and collection expressions compile fine — `LangVersion=preview` flows from the scattered props; if `ImmutableHashSet` collection-expression construction fails on netstandard2.0, fall back to `ImmutableHashSet.Create(...)` — match whatever `Primitives.Analyzers` already compiles with.)

- [ ] **Step 4: Run tests** — expected PASS. **Step 5: Commit**

```bash
git add gen/Architecture.Analyzers/RealmIdentity.cs tests/Architecture.Analyzers.Tests/RealmIdentityTests.cs
git commit -m "feat: RealmIdentity — brand/function/family parsing, the jurisdiction map"
```

### Task 3: NORSE070 — `WireFormatAnalyzer`

**Files:**
- Create: `gen/Architecture.Analyzers/WireFormatAnalyzer.cs`
- Create: `tests/Architecture.Analyzers.Tests/AnalyzerTestHarness.cs`, `tests/Architecture.Analyzers.Tests/WireFormatAnalyzerTests.cs`

**Interfaces:**
- Consumes: Task 2's `RealmIdentity`, Task 1's `Diagnostics.WireFormatOutsideBorder`.
- Produces: `[DiagnosticAnalyzer(LanguageNames.CSharp)] public sealed class WireFormatAnalyzer : DiagnosticAnalyzer`. Brand-blind (spec §3): fires purely off `compilation.AssemblyName` function segments — exempt suffix or wire-border function → analyzer disables; otherwise three detection layers: (1) using directives (incl. aliases and `global using`) whose name matches a banned root, (2) qualified names outside using directives whose leading segments match a banned root, (3) operation-level banned-symbol checks (`DataContractSerializer`/`XmlObjectSerializer` creation, `Results.Json`/`TypedResults.Json` invocation) so alias-laundered use still strikes.
- Harness: `AnalyzerTestHarness.GetDiagnosticsAsync(DiagnosticAnalyzer analyzer, string assemblyName, MetadataReference[] extraReferences, params string[] sources)` — mirrors `Primitives.Analyzers.Tests`' harness (compile-clean-first assertion, `LanguageVersion.Preview`), but takes the analyzer instance, the **compilation assembly name** (jurisdiction is name-derived, so every fixture declares who it is), and extra references. Plus `AnalyzerTestHarness.CreateNorseReference(string assemblyName)` → `CSharpCompilation.Create(assemblyName, [], ReferenceAssemblies.Bcl, new(OutputKind.DynamicallyLinkedLibrary)).ToMetadataReference()` for Task 4's reference fixtures. Copy `ReferenceAssemblies.cs` from `Primitives.Analyzers.Tests` verbatim (same BCL probing); fixtures needing ASP.NET types stub them by metadata name instead of referencing the shared framework (see the `TypedResults` stub below).

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.CodeAnalysis;

namespace Norse.Architecture.Analyzers.Tests;

public sealed class WireFormatAnalyzerTests
{
	const string GuiltySerialize =
		"""
		using System.Text.Json;

		namespace App;

		static class Leak
		{
			public static string Emit(object value) =>
				JsonSerializer.Serialize(value);
		}
		""";

	[Fact]
	async Task Strikes_norse070_on_a_banned_using_in_a_realm_assembly()
	{
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireFormatAnalyzer(), "Norse.Identity.Web.Server", [], GuiltySerialize);
		diagnostics.ShouldContain(d => d.Id == "NORSE070");
	}

	[Fact]
	async Task Stays_silent_for_the_same_code_inside_the_wire_border()
	{
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireFormatAnalyzer(), "Norse.Infrastructure.Web.Server", [], GuiltySerialize);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task Stays_silent_for_hosting_and_for_exempt_assemblies()
	{
		(await AnalyzerTestHarness.GetDiagnosticsAsync(new WireFormatAnalyzer(), "Norse.Hosting.Web.Server", [], GuiltySerialize))
			.ShouldBeEmpty();
		(await AnalyzerTestHarness.GetDiagnosticsAsync(new WireFormatAnalyzer(), "Norse.Identity.Web.Server.Tests", [], GuiltySerialize))
			.ShouldBeEmpty();
	}

	[Fact]
	async Task Strikes_the_brand_blind_anchorless_contracts_assembly()
	{
		// Spec §3 brand-blind ruling: no vocabulary segment, no governed references, still convicted.
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireFormatAnalyzer(), "Norse.Identity.Contracts", [], GuiltySerialize);
		diagnostics.ShouldContain(d => d.Id == "NORSE070");
	}

	[Fact]
	async Task Blesses_contract_attributes_as_declarations_of_intent()
	{
		var source =
			"""
			using System.Runtime.Serialization;

			namespace App;

			[DataContract]
			public sealed record LoginRequest
			{
				[DataMember(Order = 1)]
				public string Email { get; set; } = "";
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireFormatAnalyzer(), "Norse.Identity.Contracts", [], source);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task Strikes_a_fully_qualified_use_with_no_using_directive()
	{
		var source =
			"""
			namespace App;

			static class Leak
			{
				public static string Emit(object value) =>
					System.Text.Json.JsonSerializer.Serialize(value);
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireFormatAnalyzer(), "Norse.Identity.Web.Server", [], source);
		diagnostics.ShouldContain(d => d.Id == "NORSE070");
	}

	[Fact]
	async Task Strikes_an_alias_laundered_use()
	{
		var source =
			"""
			using Codec = System.Text.Json.JsonSerializer;

			namespace App;

			static class Leak
			{
				public static string Emit(object value) =>
					Codec.Serialize(value);
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireFormatAnalyzer(), "Norse.Identity.Web.Server", [], source);
		diagnostics.ShouldContain(d => d.Id == "NORSE070");
	}

	[Fact]
	async Task Strikes_the_banned_typed_results_json_symbol()
	{
		// Results.Json/TypedResults.Json live in innocent namespaces — symbol-level ban (spec §4).
		// Stub carries the real metadata name so no ASP.NET shared-framework reference is needed.
		const string typedResultsStub =
			"""
			namespace Microsoft.AspNetCore.Http
			{
				public static class TypedResults
				{
					public static object Json(object? value) => new();
				}
			}
			""";
		var source =
			"""
			using Microsoft.AspNetCore.Http;

			namespace App;

			static class Leak
			{
				public static object Emit(object value) =>
					TypedResults.Json(value);
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireFormatAnalyzer(), "Norse.Identity.Web.Server", [], typedResultsStub, source);
		diagnostics.ShouldContain(d => d.Id == "NORSE070");
	}

	[Fact]
	async Task Survives_a_pragma_suppression_attempt()
	{
		// Spec §7 suppression-proofing: NotConfigurable must hold against #pragma. If this test
		// fails RED because the pragma pierces, implement the Location.None compilation-end backstop
		// described in the task notes — the assertion below stays the authority either way.
		var source =
			"""
			#pragma warning disable NORSE070
			using System.Text.Json;
			#pragma warning restore NORSE070

			namespace App;

			static class Leak
			{
				public static string Emit(object value) =>
					JsonSerializer.Serialize(value);
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireFormatAnalyzer(), "Norse.Identity.Web.Server", [], source);
		diagnostics.Where(d => d.Id == "NORSE070" && !d.IsSuppressed).ShouldNotBeEmpty();
	}

	[Fact]
	async Task Regression_the_forge_conviction_shape_strikes()
	{
		// Day-one conviction #1/#2 (spec §6): a JsonConverter living below the border.
		var source =
			"""
			using System.Text.Json;
			using System.Text.Json.Serialization;

			namespace App;

			public sealed class MaskedValueJsonConverter : JsonConverter<int>
			{
				public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
					throw new NotSupportedException();

				public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
					writer.WriteNumberValue(value);
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireFormatAnalyzer(), "Norse.Primitives", [], source);
		diagnostics.ShouldContain(d => d.Id == "NORSE070");
	}
}
```

- [ ] **Step 2: Write the harness, run to verify failure** (harness compiles, analyzer missing → compile error; then RED on assertions once scaffolded)

Harness shape (adapting the `Primitives.Analyzers.Tests` original — same compile-clean-first law):

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Norse.Architecture.Analyzers.Tests;

static class AnalyzerTestHarness
{
	public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

	public static CSharpCompilation CreateCompilation(string assemblyName, MetadataReference[] extraReferences, params string[] sources) =>
		CSharpCompilation.Create(
			assemblyName,
			[.. sources.Select(s => CSharpSyntaxTree.ParseText(s, ParseOptions))],
			[.. ReferenceAssemblies.Bcl, .. extraReferences],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

	public static MetadataReference CreateNorseReference(string assemblyName) =>
		CSharpCompilation.Create(assemblyName, [], ReferenceAssemblies.Bcl, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
			.ToMetadataReference();

	public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
		DiagnosticAnalyzer analyzer, string assemblyName, MetadataReference[] extraReferences, params string[] sources)
	{
		var compilation = CreateCompilation(assemblyName, extraReferences, sources);
		var compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
		compileErrors.ShouldBeEmpty($"Fixture failed to compile:\n{string.Join("\n", compileErrors)}");

		var withAnalyzers = compilation.WithAnalyzers([analyzer]);
		return await withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
	}
}
```

- [ ] **Step 3: Implement `WireFormatAnalyzer`**

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Norse.Architecture.Analyzers;

/// <summary>
/// NORSE070 (spec §1 Law #1, §4): anything naming or executing a concrete encoding exists in
/// Infrastructure/Hosting alone. Brand-blind — evaluated on the function segments of the compilation's
/// assembly name, so an anchorless .Contracts assembly with zero governed references is still governed.
/// Three layers: using directives (aliases and global usings included), qualified names, and
/// banned-symbol operations so alias-laundered use still strikes. Contract attributes are blessed by
/// construction: System.Runtime.Serialization and System.ServiceModel are not on the banned-root list —
/// only their serializer machinery is, by symbol.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WireFormatAnalyzer : DiagnosticAnalyzer
{
	static readonly ImmutableArray<string> _bannedRoots =
	[
		"System.Text.Json", "Newtonsoft.Json", "System.Xml", "System.Runtime.Serialization.Json",
		"System.Net.Http.Json", "Microsoft.AspNetCore.Http.Json", "ProtoBuf", "Grpc", "Google.Protobuf", "MessagePack"
	];

	// (containing type metadata name, member name or null-for-any-instantiation)
	static readonly ImmutableArray<(string Type, string? Member)> _bannedSymbols =
	[
		("System.Runtime.Serialization.DataContractSerializer", null),
		("System.Runtime.Serialization.XmlObjectSerializer", null),
		("Microsoft.AspNetCore.Http.Results", "Json"),
		("Microsoft.AspNetCore.Http.TypedResults", "Json")
	];

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		[Diagnostics.WireFormatOutsideBorder];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics); // Ruled 2026-08-03: generator output compiles into governed assemblies, so Law #1 governs it — the gen/ exemption covers the generator assembly, not its emissions.
		context.EnableConcurrentExecution();
		context.RegisterCompilationStartAction(static start =>
		{
			var name = start.Compilation.AssemblyName ?? "";
			if (RealmIdentity.IsExempt(name) || RealmIdentity.IsWireBorder(name))
				return;
			start.RegisterSyntaxNodeAction(AnalyzeUsing, SyntaxKind.UsingDirective);
			start.RegisterSyntaxNodeAction(AnalyzeQualifiedName, SyntaxKind.QualifiedName);
			start.RegisterOperationAction(AnalyzeOperation, OperationKind.Invocation, OperationKind.ObjectCreation);
		});
	}

	static void AnalyzeUsing(SyntaxNodeAnalysisContext context)
	{
		var directive = (UsingDirectiveSyntax)context.Node;
		var name = directive.Name?.ToString();
		if (name is not null && MatchesBannedRoot(name))
			Report(context, directive.GetLocation(), name);
	}

	static void AnalyzeQualifiedName(SyntaxNodeAnalysisContext context)
	{
		// Using directives are handled (and reported once) above; skip their interior nodes. Also skip
		// inner QualifiedName nodes whose parent is a longer QualifiedName — only the outermost reports.
		var node = (QualifiedNameSyntax)context.Node;
		if (node.Parent is QualifiedNameSyntax || node.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null)
			return;
		var text = node.ToString();
		if (MatchesBannedRoot(text))
			Report(context, node.GetLocation(), text);
	}

	static void AnalyzeOperation(OperationAnalysisContext context)
	{
		var (symbol, location) = context.Operation switch
		{
			IInvocationOperation invocation => ((ISymbol)invocation.TargetMethod, invocation.Syntax.GetLocation()),
			IObjectCreationOperation { Constructor: { } ctor } creation => (ctor, creation.Syntax.GetLocation()),
			_ => (null!, null!)
		};
		if (symbol is null)
			return;
		var containingType = symbol.ContainingType?.ToDisplayString();
		var containingNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? "";
		var banned =
			MatchesBannedRoot(containingNamespace) ||
			_bannedSymbols.Any(b => b.Type == containingType && (b.Member is null || b.Member == symbol.Name));
		if (banned)
			context.ReportDiagnostic(Diagnostic.Create(Diagnostics.WireFormatOutsideBorder, location, $"{containingType}.{symbol.Name}"));
	}

	static bool MatchesBannedRoot(string name) =>
		_bannedRoots.Any(root =>
			name.StartsWith(root, StringComparison.Ordinal) &&
			(name.Length == root.Length || name[root.Length] == '.'));

	static void Report(SyntaxNodeAnalysisContext context, Location location, string offender) =>
		context.ReportDiagnostic(Diagnostic.Create(Diagnostics.WireFormatOutsideBorder, location, offender));
}
```

**Pragma-pierce contingency (only if `Survives_a_pragma_suppression_attempt` fails):** add a `RegisterCompilationEndAction` inside the compilation-start block that re-walks each syntax tree's using directives and reports every banned match at `Location.None` — positional pragmas cannot reach a `Location.None` diagnostic. Keep the located reports too (IDE experience); the backstop exists solely to make suppression futile. Record which way reality went in the commit message.

- [ ] **Step 4: Run tests** — `dotnet test tests/Architecture.Analyzers.Tests`; expected PASS all. **Step 5: Commit**

```bash
git add gen/Architecture.Analyzers/WireFormatAnalyzer.cs tests/Architecture.Analyzers.Tests/AnalyzerTestHarness.cs tests/Architecture.Analyzers.Tests/ReferenceAssemblies.cs tests/Architecture.Analyzers.Tests/WireFormatAnalyzerTests.cs
git commit -m "feat: NORSE070 — wire format never leaves the Midgard/Yggdrasil border"
```

### Task 4: NORSE071/NORSE072 — `RealmReferenceAnalyzer`, formula + precedence

**Files:**
- Create: `gen/Architecture.Analyzers/RealmReferenceAnalyzer.cs`
- Test: `tests/Architecture.Analyzers.Tests/RealmReferenceAnalyzerTests.cs`

**Interfaces:**
- Consumes: `RealmIdentity`, `Diagnostics.MidgardTakenAsDependency`/`CrossRealmReach`, harness `CreateNorseReference`.
- Produces: `[DiagnosticAnalyzer(LanguageNames.CSharp)] public sealed class RealmReferenceAnalyzer : DiagnosticAnalyzer` — one `RegisterCompilationAction` evaluating every `compilation.ReferencedAssemblyNames` entry through the spec §2 formula with §2's precedence ruling: (0) resolve self identity + brand (own anchor, else inferred from governed references); (1) **NORSE071 first and terminal per reference** — target function `Infrastructure`, self function not `Infrastructure`/`Hosting`; (2) foundation-ordering overrides — self family `Primitives`: any same-brand reference outside own family strikes NORSE072; self family `Abstractions`: same-brand references outside own family and outside the `Primitives` family strike NORSE072; (3) general arms — foundation / same family / published surface / self is Hosting; all fail → NORSE072 with the failed-arms text as `{2}`. All reports at `Location.None` (spec §3 transitive ruling) with source + target named. NORSE073 lands in Task 5 — this task's analyzer already routes `.Components` sources through a hook that Task 5 fills (keep the branch but let it fall through to the general arms for now).

- [ ] **Step 1: Write the failing tests**

```csharp
namespace Norse.Architecture.Analyzers.Tests;

public sealed class RealmReferenceAnalyzerTests
{
	const string Empty =
		"""
		namespace App;

		static class Anchor;
		""";

	static async Task<ImmutableArray<Diagnostic>> RunAsync(string self, params string[] references) =>
		await AnalyzerTestHarness.GetDiagnosticsAsync(
			new RealmReferenceAnalyzer(), self,
			[.. references.Select(AnalyzerTestHarness.CreateNorseReference)], Empty);

	[Fact]
	async Task Strikes_norse071_when_a_realm_references_midgard()
	{
		var diagnostics = await RunAsync("Norse.Identity.Web.Server", "Norse.Infrastructure.Web.Server");
		var diagnostic = diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE071");
		diagnostic.Location.ShouldBe(Location.None);
		diagnostic.GetMessage().ShouldContain("Norse.Identity.Web.Server");
		diagnostic.GetMessage().ShouldContain("Norse.Infrastructure.Web.Server");
	}

	[Fact]
	async Task Precedence_infrastructure_contracts_is_not_a_door()
	{
		// Spec §2 precedence ruling: NORSE071 evaluates before the published-surface arm and wins.
		(await RunAsync("Norse.Identity.Web.Server", "Norse.Infrastructure.Contracts"))
			.ShouldContain(d => d.Id == "NORSE071");
	}

	[Fact]
	async Task The_tree_may_reference_midgard_and_midgard_may_reference_itself()
	{
		(await RunAsync("Norse.Hosting.Web.Server", "Norse.Infrastructure.Web.Server")).ShouldBeEmpty();
		(await RunAsync("Norse.Infrastructure.Web.Server", "Norse.Infrastructure.Web.Client")).ShouldBeEmpty();
	}

	[Fact]
	async Task Realms_inherit_the_foundation_freely()
	{
		(await RunAsync("Norse.Identity.EntityFramework",
			"Norse.Primitives", "Norse.Abstractions.Contracts", "Norse.Persistence.EntityFramework", "Norse.Messaging.NServiceBus", "Norse.DesignSystem.Tokens"))
			.ShouldBeEmpty();
	}

	[Fact]
	async Task Own_realm_and_published_surfaces_are_legal_doors()
	{
		(await RunAsync("Norse.Identity.Web.Server",
			"Norse.Primitives", "Norse.Identity.EntityFramework", "Norse.AuthN.Services", "Norse.AuthN.Contracts", "Norse.Reference.Components"))
			.ShouldBeEmpty();
	}

	[Fact]
	async Task Strikes_norse072_when_a_realm_reaches_into_a_foreign_realm()
	{
		var diagnostics = await RunAsync("Norse.Reference.Data.Entities", "Norse.Primitives", "Norse.Identity.EntityFramework");
		var diagnostic = diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE072");
		diagnostic.Location.ShouldBe(Location.None);
		diagnostic.GetMessage().ShouldContain("not foundation");
	}

	[Fact]
	async Task The_forge_references_no_foreign_norse_assembly()
	{
		(await RunAsync("Norse.Primitives.Ingestion", "Norse.Primitives")).ShouldBeEmpty();
		(await RunAsync("Norse.Primitives", "Norse.Abstractions.Contracts"))
			.ShouldContain(d => d.Id == "NORSE072");
	}

	[Fact]
	async Task Asgard_references_only_the_forge()
	{
		(await RunAsync("Norse.Abstractions.Keys", "Norse.Primitives", "Norse.Abstractions.Contracts")).ShouldBeEmpty();
		(await RunAsync("Norse.Abstractions.Keys", "Norse.Persistence.EntityFramework"))
			.ShouldContain(d => d.Id == "NORSE072");
	}

	[Fact]
	async Task Brand_is_inferred_from_references_when_the_name_has_no_anchor()
	{
		// Norse.Identity.Web.Server carries no vocabulary segment; the Norse.Abstractions.Contracts
		// reference anchors the brand, making Norse.Identity.EntityFramework same-family-legal and
		// Norse.Reference.Data.Entities a conviction.
		(await RunAsync("Norse.Identity.Web.Server", "Norse.Abstractions.Contracts", "Norse.Identity.EntityFramework"))
			.ShouldBeEmpty();
		(await RunAsync("Norse.Identity.Web.Server", "Norse.Abstractions.Contracts", "Norse.Reference.Data.Entities"))
			.ShouldContain(d => d.Id == "NORSE072");
	}

	[Fact]
	async Task Cross_brand_references_are_ungoverned()
	{
		(await RunAsync("Norse.Identity.Web.Server", "Norse.Abstractions.Contracts", "Acme.Identity.EntityFramework"))
			.ShouldBeEmpty();
	}

	[Fact]
	async Task Non_norse_assemblies_are_ignored_entirely()
	{
		(await RunAsync("Norse.Identity.Web.Server", "Norse.Abstractions.Contracts", "FluentValidation", "Npgsql"))
			.ShouldBeEmpty();
	}
}
```

(`using System.Collections.Immutable;` + `using Microsoft.CodeAnalysis;` hoisted.)

Fixture law: every `RealmReferenceAnalyzer` fixture that expects governed evaluation must include at least one anchor-bearing reference (a name containing a vocabulary segment whose brand prefixes the self name). A fixture without one tests the ungoverned early-exit, nothing else.

- [ ] **Step 2: Run to verify failure** — `dotnet test tests/Architecture.Analyzers.Tests -- --filter-class "*.RealmReferenceAnalyzerTests"`

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Norse.Architecture.Analyzers;

/// <summary>
/// NORSE071/NORSE072/NORSE073 (spec §2): the reference formula with its precedence ruling. Evaluated
/// over Compilation.ReferencedAssemblyNames — transitively-flowing compile assets included, which is
/// correct law (a transitive dependency is still a dependency); reports land at Location.None naming
/// source, target, and the failed arms, so a transitive strike costs a glance, not an archaeology dig.
/// Brand is the compilation's own anchor when its name carries a vocabulary segment, otherwise
/// inferred from the first referenced assembly whose anchor-derived brand prefixes the compilation's
/// name. Cross-brand and non-Norse references are ungoverned — deliberate and recorded.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RealmReferenceAnalyzer : DiagnosticAnalyzer
{
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		[Diagnostics.MidgardTakenAsDependency, Diagnostics.CrossRealmReach, Diagnostics.ComponentImpurity];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationAction(static compilationContext =>
		{
			var compilation = compilationContext.Compilation;
			var self = compilation.AssemblyName ?? "";
			if (self.Length == 0 || RealmIdentity.IsExempt(self))
				return;

			ImmutableArray<string> references = [.. compilation.ReferencedAssemblyNames.Select(r => r.Name)];
			var brand = ResolveBrand(self, references);
			if (brand is null)
				return; // No anchor anywhere: nothing is governed (NORSE070 covers Law #1 regardless).

			var selfFunction = RealmIdentity.FunctionOf(self);
			var selfFamily = RealmIdentity.FamilyOf(self, brand);
			var isComponents = self.EndsWith(".Components", StringComparison.Ordinal);

			foreach (var target in references)
			{
				if (RealmIdentity.FamilyOf(target, brand) is not { } targetFamily)
					continue; // cross-brand / non-Norse: ungoverned.

				// Precedence 1 (spec §2 ruling): Midgard as a target beats every arm, doors included.
				if (RealmIdentity.FunctionOf(target) == "Infrastructure" && selfFunction is not ("Infrastructure" or "Hosting"))
				{
					compilationContext.ReportDiagnostic(Diagnostic.Create(
						Diagnostics.MidgardTakenAsDependency, Location.None, self, target));
					continue;
				}

				var sameFamily = targetFamily == selfFamily;

				// Precedence 2: foundation internal ordering replaces the general formula.
				if (selfFamily == "Primitives" && !sameFamily)
				{
					ReportReach(compilationContext, self, target, "the forge references no Norse assembly outside its own family");
					continue;
				}
				if (selfFamily == "Abstractions" && !sameFamily && targetFamily != "Primitives")
				{
					ReportReach(compilationContext, self, target, "Asgard references only Svartalfheim");
					continue;
				}

				// Task 5 fills the .Components stricture here (NORSE073).

				if (RealmIdentity.IsFoundation(target, brand) || sameFamily ||
					RealmIdentity.IsPublishedSurface(target) || selfFunction == "Hosting")
					continue;

				ReportReach(compilationContext, self, target,
					"not foundation, a different realm, and not a published surface");
			}
		});
	}

	static string? ResolveBrand(string self, ImmutableArray<string> references) =>
		RealmIdentity.BrandOf(self) ??
		references
			.Select(RealmIdentity.BrandOf)
			.FirstOrDefault(b => b is not null && self.StartsWith($"{b}.", StringComparison.Ordinal));

	static void ReportReach(CompilationAnalysisContext context, string self, string target, string failedArms) =>
		context.ReportDiagnostic(Diagnostic.Create(Diagnostics.CrossRealmReach, Location.None, self, target, failedArms));
}
```

- [ ] **Step 4: Run the full test project** — all Task 1–4 tests green. **Step 5: Commit**

```bash
git add gen/Architecture.Analyzers/RealmReferenceAnalyzer.cs tests/Architecture.Analyzers.Tests/RealmReferenceAnalyzerTests.cs
git commit -m "feat: NORSE071/NORSE072 — the reference formula with Midgard precedence"
```

### Task 5: NORSE073 — component purity, realm self-jurisdiction, pack proof

**Files:**
- Modify: `gen/Architecture.Analyzers/RealmReferenceAnalyzer.cs` (fill the `.Components` hook)
- Test: `tests/Architecture.Analyzers.Tests/ComponentPurityTests.cs`

**Interfaces:** Consumes everything prior. Produces the completed analyzer: a `.Components` source assembly passes only foundation + published-surface targets — same-family non-surface targets (the own-realm `.EntityFramework`/`.Web.Server`) strike NORSE073 **instead of** riding the same-family arm; NORSE071 precedence still beats it for Infrastructure targets.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Norse.Architecture.Analyzers.Tests;

public sealed class ComponentPurityTests
{
	const string Empty =
		"""
		namespace App;

		static class Anchor;
		""";

	static async Task<ImmutableArray<Diagnostic>> RunAsync(string self, params string[] references) =>
		await AnalyzerTestHarness.GetDiagnosticsAsync(
			new RealmReferenceAnalyzer(), self,
			[.. references.Select(AnalyzerTestHarness.CreateNorseReference)], Empty);

	[Fact]
	async Task Strikes_norse073_when_components_reference_their_own_realm_server_side()
	{
		var diagnostics = await RunAsync("Norse.AuthN.Components", "Norse.Abstractions.Contracts", "Norse.Identity.EntityFramework");
		diagnostics.ShouldContain(d => d.Id == "NORSE073");

		// Even the SAME realm's server assembly is out of reach — that is the whole point of Law #3.
		(await RunAsync("Norse.AuthN.Components", "Norse.Abstractions.Contracts", "Norse.AuthN.Web.Server"))
			.ShouldContain(d => d.Id == "NORSE073");
	}

	[Fact]
	async Task Components_ride_foundation_and_published_surfaces_freely()
	{
		(await RunAsync("Norse.AuthN.Components",
			"Norse.Primitives", "Norse.Abstractions.Contracts", "Norse.DesignSystem.Tokens", "Norse.AuthN.Services", "Norse.Reference.Components"))
			.ShouldBeEmpty();
	}

	[Fact]
	async Task Midgard_precedence_still_beats_the_component_stricture()
	{
		(await RunAsync("Norse.AuthN.Components", "Norse.Abstractions.Contracts", "Norse.Infrastructure.Web.Client"))
			.ShouldContain(d => d.Id == "NORSE071");
	}

	[Fact]
	async Task A_components_fluentui_drop_is_not_itself_a_components_assembly()
	{
		// Norse.AuthN.Components.FluentUI does not end ".Components" — it is governed by the general
		// formula (own realm legal), not the purity stricture. Deliberate: vendor drops may reference
		// their sibling base Components assembly and vendor packages freely.
		(await RunAsync("Norse.AuthN.Components.FluentUI", "Norse.Abstractions.Contracts", "Norse.AuthN.Components"))
			.ShouldBeEmpty();
	}
}
```

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement** — replace the Task 4 hook comment in the reference loop, directly before the general arms:

```csharp
				if (isComponents)
				{
					if (RealmIdentity.IsFoundation(target, brand) || RealmIdentity.IsPublishedSurface(target))
						continue;
					compilationContext.ReportDiagnostic(Diagnostic.Create(
						Diagnostics.ComponentImpurity, Location.None, self, target));
					continue;
				}
```

- [ ] **Step 4: Full suite + realm proof**

Run: `dotnet test tests/Architecture.Analyzers.Tests`, then `dotnet build Svartalfheim.slnx && dotnet test Svartalfheim.slnx` (zero warnings — the forge's own projects don't yet load the new analyzer; platform-wide attachment is Phase B), then the pack proof: `dotnet pack gen/Architecture.Analyzers -c Release` and verify the nupkg contains `analyzers/dotnet/cs/Norse.Architecture.Analyzers.dll` and **no** `lib/` folder (`IncludeBuildOutput=false`).

- [ ] **Step 5: Commit**

```bash
git add gen/Architecture.Analyzers/RealmReferenceAnalyzer.cs tests/Architecture.Analyzers.Tests/ComponentPurityTests.cs
git commit -m "feat: NORSE073 — components stay platform-free; pack proof green"
```

**SHIP GATE (human): Svartálfheim** — PR, CI green, tag, publish `Norse.Architecture.Analyzers` to the feed. Update `Svartalfheim/CLAUDE.md` + `README.md` (the forge now ships the Law of the Realms) per boy-scout law. **The package must be live on the feed before Phase B's scatter lands** (spec §8 bootstrap ordering).

---

## Phase B — Delivery (Bifröst root + Ginnungagap scatter; STAGE ONLY, no commits)

### Task 6: Workspace attach + scattered package reference

**Files:**
- Modify: `Bifrost/Directory.Build.targets` (workspace mode)
- Modify: `../.github/config/Directory.Build.props` (Ginnungagap scatter source — package mode)

**Interfaces:** Produces the two delivery arms of spec §5. Workspace mode: every project compiled under Bifröst gets the analyzer as a `ProjectReference` analyzer item. Standalone/CI mode: every realm project gets the NuGet package via the scattered props. Both arms exclude the analyzer project itself (self-reference guard); test/gen assemblies still load it but are exempt by name — harmless.

- [ ] **Step 1: Bifröst root `Directory.Build.targets`** — inside the existing `<When Condition="'$(UseProjectReferences)' == 'true'">`, after the `NorseDesignRef` ItemGroup, add:

```xml
				<!--
					The Law of the Realms (NORSE070-073) attaches to every workspace compilation
					unconditionally — no NorseRef opt-in, no realm opt-out. The analyzer project
					itself is the one exclusion (self-reference). Spec:
					Glitnir/docs/Platform/specs/2026-08-03-realm-dependency-law-compiler-enforcement-design.md
				-->
				<ItemGroup Condition="'$(MSBuildProjectName)' != 'Architecture.Analyzers'">
					<ProjectReference Include="$(MSBuildThisFileDirectory)Svartalfheim/gen/Architecture.Analyzers/Architecture.Analyzers.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
				</ItemGroup>
```

- [ ] **Step 2: Ginnungagap `config/Directory.Build.props`** — after the existing `<Import Project="$(_ParentProps)" ...>` line, add:

```xml
	<!--
		The Law of the Realms (NORSE070-073), delivered with the void itself: standalone/CI builds
		resolve the analyzer package here, before a realm writes its first line; workspace builds get
		the same analyzer as a ProjectReference from Bifrost's root Directory.Build.targets instead.
		The analyzer project is excluded from its own law reference (self-reference guard). Spec:
		Glitnir/docs/Platform/specs/2026-08-03-realm-dependency-law-compiler-enforcement-design.md
	-->
	<Choose>
		<When Condition="'$(UseProjectReferences)' == 'true' OR '$(MSBuildProjectName)' == 'Architecture.Analyzers'">
			<PropertyGroup />
		</When>
		<When Condition="'$(ManagePackageVersionsCentrally)' == 'true'">
			<ItemGroup>
				<PackageReference Include="Norse.Architecture.Analyzers" PrivateAssets="all" />
			</ItemGroup>
		</When>
		<Otherwise>
			<ItemGroup>
				<!-- "*" = latest released, never prerelease — the analyzer ships no beta packages (ruled 2026-08-03). -->
				<PackageReference Include="Norse.Architecture.Analyzers" Version="*" PrivateAssets="all" />
			</ItemGroup>
		</Otherwise>
	</Choose>
```

- [ ] **Step 2b: the `<Using Remove>` carriers (targets-time — a props-level Remove is a no-op, proven at verification).** Add to each of the three scattered targets sources — `.github/config/src/Directory.Build.targets`, `.github/config/tests/Directory.Build.targets`, `.github/config/gen/Directory.Build.targets` — and, for workspace mode, to Bifröst's root `Directory.Build.targets` (outside the `Choose`, unconditional):

```xml
	<ItemGroup>
		<!--
			The .NET 11 SDK injects this namespace as an implicit global using; it is a banned wire-format
			root (NORSE070), so the injection is removed at the source platform-wide. Must live at targets
			evaluation time — a props-level Remove precedes the SDK's Include and removes nothing. An
			authored `using System.Net.Http.Json;` still convicts; Midgard/Yggdrasil re-add locally where legal.
		-->
		<Using Remove="System.Net.Http.Json" />
	</ItemGroup>
```

- [ ] **Step 3: Workspace verification — the live conviction must strike.**

Run (from `Bifrost/`): `dotnet build Himinbjorg/src/Identity.Web.Server`
Expected: **build FAILS with NORSE070** pointing at `IdentityComponentsEndpointRouteBuilderExtensions.cs:11` (`using System.Text.Json;`). This failure is the verification — the law reaching the pre-existing leak on first contact. Then confirm innocents build green: `dotnet build Asgard/src/Abstractions.Contracts`, `dotnet build Svartalfheim/src/Primitives` (master carries no PII converter — the branch does, and stays untouched), `dotnet build Bragi/src/DesignSystem.Stories` and `dotnet build Himinbjorg/src/Identity.Web.Server` **must not report NORSE072 for `AuthN.Components.FluentUI`** (vendor-drop door ruling — Himinbjörg still fails NORSE070 until Task 7, expected). Then `dotnet build Mimir/src/Reference.Web.Server` → expect **NORSE071** on `Infrastructure.Persistence.EntityFramework` (the live Mímir conviction; goes green after Task 9).

- [ ] **Step 4: Stage only — the human commits both repos**

```bash
git -C /path-to/Bifrost add Directory.Build.targets
git -C /path-to/.github add config/Directory.Build.props
```

(Workspace-relative: Bifröst root and `../.github`. No commit — Buvy commits Ginnungagap, runs the scatter, and merges the bot PRs realm by realm.)

**GATE (human): Ginnungagap** — commit, scatter-the-runes runs, bot PRs land the props change in every realm. Realms with no violations go green immediately; Himinbjörg's CI goes red until Phase C merges — sequencing note: merge Phase C's remediation PR into Himinbjörg **before** (or together with) its scatter PR.

---

## Phase C — Himinbjörg (`feature/wire-format-remediation`)

### Task 7: Excise the personal-data-download JSON path

**Files:**
- Modify: `src/Identity.Web.Server/IdentityComponentsEndpointRouteBuilderExtensions.cs`
- Modify: whatever `Components/Pages/Manage/` page posts to `Account/Manage/DownloadPersonalData` (discover: `grep -rn "DownloadPersonalData" src/Identity.Web.Server/Components`)

**Context:** Spec §6 conviction #3. The `/Account/Manage/DownloadPersonalData` endpoint hand-rolls `JsonSerializer.SerializeToUtf8Bytes` over reflection-harvested `[PersonalData]` properties — it is a scaffolded stand-in for the disclosure surface the (halted, resuming-later) PII plan builds properly, and its reflection-over-properties approach is exactly the shape that plan replaces. The lawful remediation today is **excision, not relocation**: delete the endpoint and its UI affordance; the disclosure surface supersedes it. Record the pointer in the commit message.

- [ ] **Step 1: Excise the endpoint.** In `IdentityComponentsEndpointRouteBuilderExtensions.cs` delete: the `using System.Text.Json;` directive (line 11); the `loggerFactory`/`downloadLogger` locals (lines 124–125, used only by this endpoint); the entire `manageGroup.MapPost("/DownloadPersonalData", ...)` block (lines 127–157); and the `LogUserPersonalDataRequested` `[LoggerMessage]` method (lines 177–178). The `manageGroup` declaration stays — `LinkExternalLogin` still uses it.

- [ ] **Step 2: Excise the UI affordance.** `grep -rn "DownloadPersonalData" src/Identity.Web.Server/Components` — expect a `Manage/PersonalData.razor` (or similar) with a form posting to the endpoint. Remove the form/button that posts to `Account/Manage/DownloadPersonalData`; if the page then renders nothing meaningful, replace its body with a short static notice that data disclosure ships with the platform disclosure surface (keep the page + route so nav doesn't 404). Read the page first; keep the excision minimal — no redesign.

- [ ] **Step 3: Verify under the law + tests.**

Run (from `Bifrost/`, workspace mode — Phase B's targets attach the analyzer): `dotnet build Himinbjorg/src/Identity.Web.Server`
Expected: green — NORSE070 satisfied.
Run (from `Himinbjorg/`): `dotnet test Himinbjorg.slnx`
Expected: green.

- [ ] **Step 4: Commit**

```bash
git checkout master && git checkout -b feature/wire-format-remediation
git add src/Identity.Web.Server/IdentityComponentsEndpointRouteBuilderExtensions.cs src/Identity.Web.Server/Components/Pages/Manage/PersonalData.razor
git commit -m "fix: excise scaffolded personal-data JSON download — NORSE070; superseded by the PII disclosure surface"
```

(Adjust the staged page path to whatever Step 2's grep actually found.)

**SHIP GATE (human): Himinbjörg** — PR, CI, merge before/with the realm's scatter PR, tag if warranted.

---

## Phase D — Yggdrasil (`feature/law-package-pin`)

### Task 8: CPM pin

**Files:**
- Modify: `Directory.Packages.props` (add `<PackageVersion Include="Norse.Architecture.Analyzers" Version="<the version Phase A's gate published>" />`, alphabetical position)

- [ ] **Step 1: Add the pin** with the exact version the Svartálfheim ship gate published (Yggdrasil pins explicitly — one-file hotfix doctrine; no floating wildcard here).
- [ ] **Step 2: Verify** — `dotnet build Yggdrasil.slnx && dotnet test Yggdrasil.slnx` (green: Hosting is inside the wire border for NORSE070 and formula-exempt for NORSE072; this build is the tree's proof under the law).
- [ ] **Step 3: Commit**

```bash
git checkout master && git checkout -b feature/law-package-pin
git add Directory.Packages.props
git commit -m "chore: pin Norse.Architecture.Analyzers — the law reaches the tree"
```

**SHIP GATE (human): Yggdrasil** — PR, CI, merge with/after its scatter PR.

---

## Phase E — Mímir (`feature/midgard-excision-reference-web-server`)

### Task 9: Remediate the live NORSE071 — Midgard out of Mímir's dependency graph

**Files:**
- Modify: `src/Reference.Web.Server/Reference.Web.Server.csproj` (remove `<NorseRef Include="Infrastructure.Persistence.EntityFramework" ...>`)
- Modify: whatever wiring consumed it (discover: `grep -rn "Infrastructure.Persistence\|AddNorse.*Persistence\|Infrastructure\." src/Reference.Web.Server --include='*.cs'`)
- The registrations that needed Midgard move to Yggdrasil's composition root if they aren't already there (investigate `Yggdrasil/src/Hosting.Web.Server` for the existing `AddNorse*` call sites; Mímir consumes the Asgard read-contract seam — `IReadRepository<TView>` from `Norse.Abstractions.Backend` — never the Midgard implementation).

**This is an investigation-led task** (spec §6 conviction #4, ruled 2026-08-03: remediation, not a door — the postmortem shape). Steps: (1) inventory exactly which Midgard types `Reference.Web.Server` touches; (2) if only DI registration — move the registration to Yggdrasil's composition root and delete the NorseRef; (3) if runtime types leak into handlers/services — STOP, report BLOCKED with the inventory (that is a design conversation, not an in-plan fix); (4) verify under the law: workspace-mode `dotnet build Mimir/src/Reference.Web.Server` green, `dotnet test Mimir.slnx` green; (5) commit on `feature/midgard-excision-reference-web-server`, then Yggdrasil companion change (if any) commits on a `feature/mimir-composition` branch there.

**SHIP GATE (human): Mímir (+ Yggdrasil companion if touched)** — PR, CI, merge before/with Mímir's scatter PR.

---

## After the law holds

The halted PII effort resumes on `feature/pii-primitives` (rebase onto the new Svartálfheim master): the rebased branch fails NORSE070 on `MaskedValueJsonConverter.cs` + `EmailAddress.cs` by design — strip both (delete the converter + its tests, drop the `[JsonConverter]` attribute and `using System.Text.Json.Serialization;` from `EmailAddress`), and the PII plan's Midgard phase gains the relocated `IMaskedValue`-aware converter in `Infrastructure.Web.Server/Json/`. That amendment belongs to the PII plan's own record (`2026-08-03-pii-primitives-identity-erasure-seam.md`), not this one.

## Self-Review Notes (performed at authoring)

1. **Spec coverage:** §1 statutes → Tasks 3 (Law #1), 4 (Laws #2/#4), 5 (Law #3); §2 formula + precedence → Task 4 (+5 components arm); §3 jurisdiction/brand-blind/transitive rulings → Tasks 2, 3 (brand-blind fixture), 4 (Location.None + message content, brand inference, cross-brand ungoverned); §4 strikes + banned set + document-model ruling → Tasks 1, 3 (document-model types need no fixture — they live under the `System.Text.Json` root already covered); §5 delivery → Tasks 1 (packable), 6 (both arms); §6 convictions → Task 3 regression fixture (forge shape), Task 6 Step 3 (live Himinbjörg strike as verification), Task 7 (remediation); §7 fixtures incl. suppression-proofing and the Ratatoskr standing fixture — Ratatoskr has zero assemblies today, so its "standing fixture" is the delivery itself (scatter reaches it at birth); recorded here rather than as a test against nothing; §8 sequencing incl. bootstrap → gate ordering + Task 6 notes; §9 out-of-scope honored (no BuildCheck, no YGG101/301).
2. **Type consistency:** `RealmIdentity` member names/signatures identical across Tasks 2/3/4/5; `Diagnostics` field names identical across Tasks 1/3/4/5; harness signature identical across Tasks 3/4/5.
3. **Known deliberate choices:** `.Aot.Smoke` suffix matches the real project (`Primitives.Aot.Smoke`); `.Generator` singular added to the spec's exemption list (reality alignment, recorded in Global Constraints); `Norse.AuthN.Components.FluentUI` pinned as NOT a published surface (Task 2 test) and NOT under the purity stricture (Task 5 test) — both deliberate, both flagged for the reviewer in-fixture.
