# Urðarbrunnr Abstractions.Emit Adoption Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Retire Urðarbrunnr's independently-vendored `CSharpEmit`/`StringSyntaxAttribute` generator helpers in favor of Asgard's published `Norse.Abstractions.Emit` package, convert the one remaining generator that still calls `StringBuilder.AppendLine` directly, and make every generator's `SourceText.From(...)` call BOM-free.

**Architecture:** All three of Urðarbrunnr's `gen/` source-generator projects (`Persistence.EntityFramework.Generator`, `Persistence.EntityFramework.Design.SqlServer.Generator`, `Persistence.EntityFramework.Design.PostgreSQL.Generator`) gain a `NorseRef` to Asgard's `Abstractions.Emit`, resolved by Bifröst's existing root `Directory.Build.targets` Choose block into a `ProjectReference` (Bifröst dev mode) or a `PackageReference` on the real, already-published `Norse.Abstractions.Emit` NuGet package (standalone/CI mode — confirmed live in the `v0.0.13` Asgard release). Each generator project also gets the same `GetTargetPathDependsOn` forwarding hook Asgard's own `Abstractions.Contracts.Generator.csproj` uses, so `Norse.Abstractions.Emit.dll` reaches the `@(Analyzer)` set of every downstream consumer. Each of the three packable host projects that bundles its generator's DLL into `analyzers/dotnet/cs/` at pack time gains a second `<None>` pack-include for `Norse.Abstractions.Emit.dll`, mirroring `Asgard/src/Abstractions.Contracts/Abstractions.Contracts.csproj`.

**Tech Stack:** Roslyn `IIncrementalGenerator` (netstandard2.0), MSBuild `NorseRef` item convention, `Norse.Abstractions.Emit` (`CSharpEmit.AppendCSharp`, `Utf8NoBom.Encoding`).

## Global Constraints

- **House style:** emitter code calls `sb.AppendCSharp(...)` for every line it emits — never `sb.AppendLine(...)` directly. Source: `Glitnir/docs/Asgard/specs/2026-07-25-generator-authoring-toolkit-and-raw-string-house-style-design.md`, `Bifrost/CLAUDE.md` §5.
- **Consolidation, not line-for-line translation:** a run of sequential `AppendLine` calls collapses into *one* `AppendCSharp` call using a raw string literal (`"""..."""`), so the generated shape reads as a block. Do not emit one `AppendCSharp` call per original `AppendLine` call.
- **Raw string dollar-sign rule:** a block with no interpolation and literal `{`/`}` (e.g. a class/method body opener) uses a plain `"""..."""` — no `$`. A block with interpolation but no literal braces uses single-`$` (`$"""..."""`), holes as `{value}`. A block that needs interpolation *and* literal braces in the same text uses double-`$` (`$$"""..."""`), holes as `{{value}}`, and single braces are literal.
- **BOM-free everywhere:** every generator's `ctx.AddSource(...)`/`SourceText.From(...)` call uses `Norse.Abstractions.Emit.Utf8NoBom.Encoding`, never the bare `Encoding.UTF8` singleton (which emits a BOM). This is the platform's UTF8-no-BOM ethos, not specific to this change — fix it everywhere it's touched in this plan.
- **Cross-repo dependency wiring goes through `NorseRef`, never a hand-written relative `ProjectReference`.** `<NorseRef Include="Abstractions.Emit"><Repo>Asgard</Repo></NorseRef>` resolves via Bifröst's root `Directory.Build.targets` — `ProjectReference` to `Asgard/src/Abstractions.Emit/Abstractions.Emit.csproj` when `$(UseProjectReferences)` is `true` (Bifröst dev mode), `PackageReference` to `Norse.Abstractions.Emit` version `$(NorseRefVersion)` otherwise (standalone/CI). Precedent: `Urdarbrunnr/src/Persistence.EntityFramework.Design/Persistence.EntityFramework.Design.csproj`'s existing `NorseRef Include="Abstractions.Migrations"`.
- **Delete duplicate code outright.** No `#pragma`-suppressed leftovers, no aliasing, no "kept for compat" shims.
- **Tabs for indentation.** `internal` types stay `internal` — do not widen accessibility while touching these files.
- **Verify with `dotnet build` and `dotnet test`**, not by inspection alone. No `.runsettings` files.

---

### Task 1: SqlServer + PostgreSQL migration-contributor generators adopt `Norse.Abstractions.Emit`

Both of these generators already call `AppendCSharp` — today it resolves to a **duplicate**, independently-vendored implementation in `Urdarbrunnr/gen/Persistence.EntityFramework.Design.Generator.Shared/CSharpEmit.cs`, linked into both projects via `<Compile Include>`. That duplicate forwards to `sb.AppendLine(code)` (`Environment.NewLine`, non-deterministic across OSes); Asgard's real `Norse.Abstractions.Emit.CSharpEmit.AppendCSharp` uses a hardcoded `\n` instead — deliberately, for deterministic generator output. Switching the `using` to point at the real package is therefore not a no-op rename; it's also this bug fix, verified by the existing tests (which use `ShouldContain` substring assertions, not exact-text/golden comparisons, so they're insensitive to the newline change).

**Files:**
- Modify: `Urdarbrunnr/gen/Persistence.EntityFramework.Design.SqlServer.Generator/Persistence.EntityFramework.Design.SqlServer.Generator.csproj`
- Modify: `Urdarbrunnr/gen/Persistence.EntityFramework.Design.PostgreSQL.Generator/Persistence.EntityFramework.Design.PostgreSQL.Generator.csproj`
- Modify: `Urdarbrunnr/gen/Persistence.EntityFramework.Design.SqlServer.Generator/MigrationContributorGenerator.cs`
- Modify: `Urdarbrunnr/gen/Persistence.EntityFramework.Design.PostgreSQL.Generator/MigrationContributorGenerator.cs`
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework.Design.SqlServer/Persistence.EntityFramework.Design.SqlServer.csproj`
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework.Design.PostgreSQL/Persistence.EntityFramework.Design.PostgreSQL.csproj`
- Delete: `Urdarbrunnr/gen/Persistence.EntityFramework.Design.Generator.Shared/CSharpEmit.cs`
- Delete: `Urdarbrunnr/gen/Persistence.EntityFramework.Design.Generator.Shared/StringSyntaxAttribute.cs`
- Test (existing, run only): `Urdarbrunnr/tests/Persistence.EntityFramework.Design.SqlServer.Generator.Tests/MigrationContributorGeneratorTests.cs`, `Urdarbrunnr/tests/Persistence.EntityFramework.Design.PostgreSQL.Generator.Tests/MigrationContributorGeneratorTests.cs`

**Interfaces:**
- Consumes: `Norse.Abstractions.Emit.CSharpEmit.AppendCSharp(this StringBuilder, [StringSyntax("C#")] string)` and `Norse.Abstractions.Emit.Utf8NoBom.Encoding` — both public, in the already-published `Norse.Abstractions.Emit` package (`Asgard/src/Abstractions.Emit/`).
- Produces: nothing new consumed by later tasks — Task 2 is independent of this one.

- [ ] **Step 1: Confirm the current (pre-fix) state compiles and tests pass**

Run:
```bash
dotnet build Urdarbrunnr/Urdarbrunnr.slnx
dotnet test Urdarbrunnr/tests/Persistence.EntityFramework.Design.SqlServer.Generator.Tests/Persistence.EntityFramework.Design.SqlServer.Generator.Tests.csproj
dotnet test Urdarbrunnr/tests/Persistence.EntityFramework.Design.PostgreSQL.Generator.Tests/Persistence.EntityFramework.Design.PostgreSQL.Generator.Tests.csproj
```
Expected: build succeeds, all tests pass. This is the baseline — if it doesn't pass before you touch anything, stop and report BLOCKED rather than proceeding.

- [ ] **Step 2: Add the `NorseRef` and the Analyzer-forwarding hook to the SqlServer generator project**

Replace the full contents of `Urdarbrunnr/gen/Persistence.EntityFramework.Design.SqlServer.Generator/Persistence.EntityFramework.Design.SqlServer.Generator.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Persistence.EntityFramework.Design.SqlServer.Generator: the Roslyn IIncrementalGenerator that discovers EfMigrationContributor&lt;TContext&gt; implementations at compile time and emits AddNorseMigrations() with SQL Server connection wiring.</Description>
	</PropertyGroup>
	<ItemGroup>
		<NorseRef Include="Abstractions.Emit">
			<Repo>Asgard</Repo>
		</NorseRef>
		<Compile Include="../Persistence.EntityFramework.Design.Generator.Shared/MigrationContributorDiscovery.cs" Link="Shared/MigrationContributorDiscovery.cs" />
	</ItemGroup>
	<!--
		A ProjectReference with OutputItemType="Analyzer" only pulls THIS project's own GetTargetPath
		output into the consumer's @(Analyzer) set — never this project's transitive references. Without
		the hook below, Norse.Abstractions.Emit.dll never reaches the consumer's analyzer load context
		and the generator dies at runtime with FileNotFoundException (surfaced to the consumer as CS8785).
		Mirrors Asgard/gen/Abstractions.Contracts.Generator/Abstractions.Contracts.Generator.csproj.
	-->
	<PropertyGroup>
		<GetTargetPathDependsOn>$(GetTargetPathDependsOn);_NorseIncludeEmitDependencyTargetPath</GetTargetPathDependsOn>
	</PropertyGroup>
	<Target Name="_NorseIncludeEmitDependencyTargetPath">
		<ItemGroup>
			<TargetPathWithTargetPlatformMoniker Include="@(ReferenceCopyLocalPaths->WithMetadataValue('Filename', 'Norse.Abstractions.Emit')->WithMetadataValue('Extension', '.dll'))" IncludeRuntimeDependency="false" />
		</ItemGroup>
	</Target>
</Project>
```

Note what changed from the current file: the `<Compile Include>` lines for `CSharpEmit.cs` and `StringSyntaxAttribute.cs` are gone (those files are deleted in Step 6); `MigrationContributorDiscovery.cs`'s `<Compile Include>` stays (it's unrelated domain logic, not part of this duplication); the `NorseRef` item and the forwarding hook are new.

- [ ] **Step 3: Repeat Step 2 for the PostgreSQL generator project**

Replace the full contents of `Urdarbrunnr/gen/Persistence.EntityFramework.Design.PostgreSQL.Generator/Persistence.EntityFramework.Design.PostgreSQL.Generator.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Persistence.EntityFramework.Design.PostgreSQL.Generator: the Roslyn IIncrementalGenerator that discovers EfMigrationContributor&lt;TContext&gt; implementations at compile time and emits AddNorseMigrations() with PostgreSQL connection wiring.</Description>
	</PropertyGroup>
	<ItemGroup>
		<NorseRef Include="Abstractions.Emit">
			<Repo>Asgard</Repo>
		</NorseRef>
		<Compile Include="../Persistence.EntityFramework.Design.Generator.Shared/MigrationContributorDiscovery.cs" Link="Shared/MigrationContributorDiscovery.cs" />
	</ItemGroup>
	<PropertyGroup>
		<GetTargetPathDependsOn>$(GetTargetPathDependsOn);_NorseIncludeEmitDependencyTargetPath</GetTargetPathDependsOn>
	</PropertyGroup>
	<Target Name="_NorseIncludeEmitDependencyTargetPath">
		<ItemGroup>
			<TargetPathWithTargetPlatformMoniker Include="@(ReferenceCopyLocalPaths->WithMetadataValue('Filename', 'Norse.Abstractions.Emit')->WithMetadataValue('Extension', '.dll'))" IncludeRuntimeDependency="false" />
		</ItemGroup>
	</Target>
</Project>
```

Keep this file's `<Description>` verbatim as it exists today if it differs from the text above — only the `ItemGroup`/`Compile Include`/hook shape matters; check the file before overwriting and preserve its existing `<Description>` text exactly.

- [ ] **Step 4: Update the `using` directives and encoding call in the SqlServer generator**

In `Urdarbrunnr/gen/Persistence.EntityFramework.Design.SqlServer.Generator/MigrationContributorGenerator.cs`, change the top of the file from:

```csharp
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Norse.Persistence.EntityFramework.Design.Generator.Shared;
```

to:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Norse.Abstractions.Emit;
using Norse.Persistence.EntityFramework.Design.Generator.Shared;
```

(`System.Text` is dropped because `Encoding.UTF8` is being replaced below and `StringBuilder` — used elsewhere in the file — is accessible without it since it's a commonly-imported BCL type already in scope via implicit usings; if the build complains `StringBuilder` is not found, add `using System.Text;` back above `Microsoft.CodeAnalysis` rather than guessing.)

Then change:

```csharp
ctx.AddSource("NorseMigrationsExtensions.g.cs", SourceText.From(source, Encoding.UTF8));
```

to:

```csharp
ctx.AddSource("NorseMigrationsExtensions.g.cs", SourceText.From(source, Utf8NoBom.Encoding));
```

- [ ] **Step 5: Repeat Step 4 for the PostgreSQL generator**

Apply the identical `using` and `Encoding.UTF8` → `Utf8NoBom.Encoding` change to `Urdarbrunnr/gen/Persistence.EntityFramework.Design.PostgreSQL.Generator/MigrationContributorGenerator.cs`.

- [ ] **Step 6: Delete the duplicate shared files**

```bash
git rm Urdarbrunnr/gen/Persistence.EntityFramework.Design.Generator.Shared/CSharpEmit.cs
git rm Urdarbrunnr/gen/Persistence.EntityFramework.Design.Generator.Shared/StringSyntaxAttribute.cs
```

Do not delete `Urdarbrunnr/gen/Persistence.EntityFramework.Design.Generator.Shared/MigrationContributorDiscovery.cs` — it is unrelated contributor-discovery logic, still linked by both generator projects.

- [ ] **Step 7: Add the `Norse.Abstractions.Emit.dll` pack-include to the SqlServer host project**

In `Urdarbrunnr/src/Persistence.EntityFramework.Design.SqlServer/Persistence.EntityFramework.Design.SqlServer.csproj`, find the existing `IncludeGeneratorInPackage` target:

```xml
	<Target Name="IncludeGeneratorInPackage" BeforeTargets="_GetPackageFiles">
		<MSBuild Projects="../../gen/Persistence.EntityFramework.Design.SqlServer.Generator/Persistence.EntityFramework.Design.SqlServer.Generator.csproj"
			Targets="Build"
			Properties="Configuration=$(Configuration)" />
		<ItemGroup>
			<None Include="../../gen/Persistence.EntityFramework.Design.SqlServer.Generator/bin/$(Configuration)/netstandard2.0/Norse.Persistence.EntityFramework.Design.SqlServer.Generator.dll"
				Pack="true"
				PackagePath="analyzers/dotnet/cs/"
				Visible="false" />
		</ItemGroup>
	</Target>
```

Add a second `<None Include>` inside the same `<ItemGroup>`, so the target reads:

```xml
	<Target Name="IncludeGeneratorInPackage" BeforeTargets="_GetPackageFiles">
		<MSBuild Projects="../../gen/Persistence.EntityFramework.Design.SqlServer.Generator/Persistence.EntityFramework.Design.SqlServer.Generator.csproj"
			Targets="Build"
			Properties="Configuration=$(Configuration)" />
		<ItemGroup>
			<None Include="../../gen/Persistence.EntityFramework.Design.SqlServer.Generator/bin/$(Configuration)/netstandard2.0/Norse.Persistence.EntityFramework.Design.SqlServer.Generator.dll"
				Pack="true"
				PackagePath="analyzers/dotnet/cs/"
				Visible="false" />
			<None Include="../../gen/Persistence.EntityFramework.Design.SqlServer.Generator/bin/$(Configuration)/netstandard2.0/Norse.Abstractions.Emit.dll"
				Pack="true"
				PackagePath="analyzers/dotnet/cs/"
				Visible="false" />
		</ItemGroup>
	</Target>
```

Without this, the generator's own package builds and runs fine in Bifröst project-reference mode (masking the gap), but a real downstream `PackageReference` consumer of `Norse.Persistence.EntityFramework.Design.SqlServer` hits `FileNotFoundException`/`CS8785` at their own build time, because `Norse.Abstractions.Emit.dll` never made it into the package's `analyzers/dotnet/cs/` folder. Mirrors `Asgard/src/Abstractions.Contracts/Abstractions.Contracts.csproj`'s `IncludeGeneratorInPackage` target, which already does the equivalent dual-include for its own generator + Emit pair.

- [ ] **Step 8: Repeat Step 7 for the PostgreSQL host project**

Apply the identical second `<None Include>` (same `Norse.Abstractions.Emit.dll` filename, same `PackagePath`/`Visible` attributes) to the `IncludeGeneratorInPackage` target in `Urdarbrunnr/src/Persistence.EntityFramework.Design.PostgreSQL/Persistence.EntityFramework.Design.PostgreSQL.csproj`, adjusting only the `../../gen/...` path prefix to `Persistence.EntityFramework.Design.PostgreSQL.Generator`.

- [ ] **Step 9: Build and run the affected tests**

```bash
dotnet build Urdarbrunnr/Urdarbrunnr.slnx
dotnet test Urdarbrunnr/tests/Persistence.EntityFramework.Design.SqlServer.Generator.Tests/Persistence.EntityFramework.Design.SqlServer.Generator.Tests.csproj
dotnet test Urdarbrunnr/tests/Persistence.EntityFramework.Design.PostgreSQL.Generator.Tests/Persistence.EntityFramework.Design.PostgreSQL.Generator.Tests.csproj
```
Expected: build succeeds (confirming the `NorseRef` resolves and the Analyzer-forwarding hook works — a broken hook fails at build time with `CS8785`/`FileNotFoundException` from the generator, not silently), all existing tests still pass.

- [ ] **Step 10: Commit**

```bash
git add Urdarbrunnr/gen/Persistence.EntityFramework.Design.SqlServer.Generator Urdarbrunnr/gen/Persistence.EntityFramework.Design.PostgreSQL.Generator Urdarbrunnr/src/Persistence.EntityFramework.Design.SqlServer/Persistence.EntityFramework.Design.SqlServer.csproj Urdarbrunnr/src/Persistence.EntityFramework.Design.PostgreSQL/Persistence.EntityFramework.Design.PostgreSQL.csproj
git commit -m "Adopt Norse.Abstractions.Emit in SqlServer/PostgreSQL migration generators, drop vendored duplicate"
```

---

### Task 2: `Persistence.EntityFramework.Generator` adopts `Norse.Abstractions.Emit`

Unlike Task 1's pair, this generator has **zero** Emit-toolkit wiring today — it calls raw `sb.AppendLine(...)` 24 times in `BuildSource`. This task wires the same `NorseRef` + forwarding hook + pack-include pattern from Task 1, then rewrites `BuildSource` to emit via consolidated `AppendCSharp` raw-string blocks instead of one `AppendLine` per line, and switches its `Encoding.UTF8` to `Utf8NoBom.Encoding`.

**Files:**
- Modify: `Urdarbrunnr/gen/Persistence.EntityFramework.Generator/Persistence.EntityFramework.Generator.csproj`
- Modify: `Urdarbrunnr/gen/Persistence.EntityFramework.Generator/EntityConfigurationApplicationGenerator.cs`
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework/Persistence.EntityFramework.csproj`
- Test (existing, run only): `Urdarbrunnr/tests/Persistence.EntityFramework.Generator.Tests/EntityConfigurationApplicationGeneratorTests.cs`

**Interfaces:**
- Consumes: same `Norse.Abstractions.Emit.CSharpEmit.AppendCSharp`/`Utf8NoBom.Encoding` as Task 1. Independent of Task 1's commits — this task can run before or after it.
- Produces: nothing consumed by other tasks — this is the last task in the plan.

- [ ] **Step 1: Confirm the current (pre-fix) state compiles and tests pass**

```bash
dotnet build Urdarbrunnr/Urdarbrunnr.slnx
dotnet test Urdarbrunnr/tests/Persistence.EntityFramework.Generator.Tests/Persistence.EntityFramework.Generator.Tests.csproj
```
Expected: build succeeds, all tests pass. If this baseline fails before you touch anything, stop and report BLOCKED.

- [ ] **Step 2: Add the `NorseRef` and Analyzer-forwarding hook to the generator project**

Replace the full contents of `Urdarbrunnr/gen/Persistence.EntityFramework.Generator/Persistence.EntityFramework.Generator.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Persistence.EntityFramework.Generator: the Roslyn IIncrementalGenerator that discovers INorseEntity&lt;TSelf&gt; implementations in the compiling project's own syntax tree and emits ApplyNorseConfigurations(), plus a Tier-1 partial-class ConfigureNorseEntities override when a partial NorseDbContext subclass is present.</Description>
	</PropertyGroup>
	<ItemGroup>
		<NorseRef Include="Abstractions.Emit">
			<Repo>Asgard</Repo>
		</NorseRef>
	</ItemGroup>
	<PropertyGroup>
		<GetTargetPathDependsOn>$(GetTargetPathDependsOn);_NorseIncludeEmitDependencyTargetPath</GetTargetPathDependsOn>
	</PropertyGroup>
	<Target Name="_NorseIncludeEmitDependencyTargetPath">
		<ItemGroup>
			<TargetPathWithTargetPlatformMoniker Include="@(ReferenceCopyLocalPaths->WithMetadataValue('Filename', 'Norse.Abstractions.Emit')->WithMetadataValue('Extension', '.dll'))" IncludeRuntimeDependency="false" />
		</ItemGroup>
	</Target>
</Project>
```

Preserve the file's existing `<Description>` text verbatim if it differs from the text above — check the file before overwriting.

- [ ] **Step 3: Add the `Norse.Abstractions.Emit.dll` pack-include to the host project**

In `Urdarbrunnr/src/Persistence.EntityFramework/Persistence.EntityFramework.csproj`, find:

```xml
	<Target Name="IncludeGeneratorInPackage" BeforeTargets="_GetPackageFiles">
		<MSBuild Projects="../../gen/Persistence.EntityFramework.Generator/Persistence.EntityFramework.Generator.csproj"
			Targets="Build"
			Properties="Configuration=$(Configuration)" />
		<ItemGroup>
			<None Include="../../gen/Persistence.EntityFramework.Generator/bin/$(Configuration)/netstandard2.0/Norse.Persistence.EntityFramework.Generator.dll"
				Pack="true"
				PackagePath="analyzers/dotnet/cs/"
				Visible="false" />
		</ItemGroup>
	</Target>
```

Add a second `<None Include>` for Emit inside the same `ItemGroup`:

```xml
			<None Include="../../gen/Persistence.EntityFramework.Generator/bin/$(Configuration)/netstandard2.0/Norse.Abstractions.Emit.dll"
				Pack="true"
				PackagePath="analyzers/dotnet/cs/"
				Visible="false" />
```

- [ ] **Step 4: Rewrite `BuildSource` to emit via `AppendCSharp`, and switch the encoding**

In `Urdarbrunnr/gen/Persistence.EntityFramework.Generator/EntityConfigurationApplicationGenerator.cs`, change the top of the file from:

```csharp
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
```

to:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Norse.Abstractions.Emit;
```

(if the build complains `StringBuilder` is not found after dropping `System.Text`, add `using System.Text;` back rather than guessing — `System.Text` is dropped here only because nothing else in the file references it directly once `Encoding.UTF8` is gone below.)

Change the `ctx.AddSource` call:

```csharp
ctx.AddSource("NorseEntityConfigurationExtensions.g.cs", SourceText.From(text, Encoding.UTF8));
```

to:

```csharp
ctx.AddSource("NorseEntityConfigurationExtensions.g.cs", SourceText.From(text, Utf8NoBom.Encoding));
```

Replace the entire `BuildSource` method body (all 24 `AppendLine` calls) with:

```csharp
	static string BuildSource(IList<string> entities, INamedTypeSymbol? tier1Context)
	{
		StringBuilder sb = new();

		sb.AppendCSharp("""
			// <auto-generated />
			#nullable enable
			using Microsoft.EntityFrameworkCore;

			internal static class GeneratedNorseModelConfigurations
			{
				public static ModelBuilder ApplyNorseConfigurations(this ModelBuilder builder)
				{
			""");

		foreach (var entity in entities)
			sb.AppendCSharp($"""
						builder.Entity<{entity}>(eb => {entity}.Configure(eb));
				""");

		sb.AppendCSharp("""
					return builder;
				}
			}
			""");

		if (tier1Context is not null)
		{
			var ns = tier1Context.ContainingNamespace;
			var name = tier1Context.Name;

			if (ns.IsGlobalNamespace)
				sb.AppendCSharp($$"""

					partial class {{name}}
					{
						protected override void ConfigureNorseEntities(ModelBuilder builder)
						{
							base.ConfigureNorseEntities(builder);
							builder.ApplyNorseConfigurations();
						}
					}
					""");
			else
				sb.AppendCSharp($$"""

					namespace {{ns.ToDisplayString()}}
					{
						partial class {{name}}
						{
							protected override void ConfigureNorseEntities(ModelBuilder builder)
							{
								base.ConfigureNorseEntities(builder);
								builder.ApplyNorseConfigurations();
							}
						}
					}
					""");
		}

		return sb.ToString();
	}
```

This is a deliberate restructuring, not a mechanical line-for-line `AppendLine` → `AppendCSharp` rename: the original method built the `indent`/conditional-namespace-wrapping logic line-by-line (`{indent}partial class ...`, `{indent}{{`, etc.) because each line was its own `AppendLine` call. Collapsing to `AppendCSharp` blocks makes that per-line indent-threading awkward and unreadable, so the two branches (global namespace vs. namespaced) are written as two complete, differently-indented raw-string blocks instead — same emitted output, no `indent` variable needed. Verify the emitted text is byte-for-byte equivalent in shape (ignoring the `\n`-vs-`Environment.NewLine` change, which is intentional per Global Constraints) by re-reading both branches against the original `AppendLine` sequence (lines 111–134 in the pre-change file) before moving on.

- [ ] **Step 5: Build and run the affected tests**

```bash
dotnet build Urdarbrunnr/Urdarbrunnr.slnx
dotnet test Urdarbrunnr/tests/Persistence.EntityFramework.Generator.Tests/Persistence.EntityFramework.Generator.Tests.csproj
```
Expected: build succeeds, all 5 existing tests pass — including `Generator_emits_namespaced_Tier1_partial_override_as_valid_C_sharp`, which re-parses the generated tree and asserts zero diagnostics, so it will catch a malformed raw-string block if the two-branch rewrite above introduced one.

- [ ] **Step 6: Commit**

```bash
git add Urdarbrunnr/gen/Persistence.EntityFramework.Generator Urdarbrunnr/src/Persistence.EntityFramework/Persistence.EntityFramework.csproj
git commit -m "Adopt Norse.Abstractions.Emit in Persistence.EntityFramework.Generator, drop raw AppendLine emission"
```
