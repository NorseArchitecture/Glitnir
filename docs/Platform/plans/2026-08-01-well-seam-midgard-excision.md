# The Well Seam — Midgard Excision Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (the platform default — superpowers:executing-plans is the narrow fallback for a separate-session review checkpoint, never an interchangeable alternative) to implement this plan task-by-task, paired with superpowers:test-driven-development on every task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Realms stop referencing Midgard: `[NorseWell]` declarations plus a Midgard seam generator running in Yggdrasil replace `AddNorseWell`, the runtime DbContext is generator-emitted `file`-scoped, and an MSBuild check makes `Repo=Midgard` outside Yggdrasil a build error.

**Architecture:** Asgard declares `NorseWellAttribute` (assembly-level, connection-string name + optional brownfield context type). A new Midgard generator (`Infrastructure.Persistence.EntityFramework.Generator`) walks Yggdrasil's reference closure, discovers `INorseEntity`/`IViewBearer<TView>` pairs per declared well, and emits one file per compilation: a `file`-scoped sealed `NorseDbContext` subclass per greenfield well plus a public `AddNorseWells(INorseEfProvider)` entry point calling Urðarbrunnr's `AddNorseContextFactory` and Midgard's now-public `RegisterWell`. Spec: `../specs/2026-08-01-well-seam-midgard-excision-design.md`. **Prerequisite: the reference-data inversion plan has fully shipped** — all names below are the post-inversion names (`Reference.Data.EntityFramework` etc.).

**Tech Stack:** Roslyn incremental generators (netstandard2.0, symbol-based closure walk), EF Core 11 preview, xUnit v3 + Shouldly on MTP v2, Testcontainers, MSBuild targets (Ginnungagap scatter).

## Global Constraints

- **Immutable in realm repos:** every scattered `Directory.Build.props`/`.targets`, `.editorconfig`, `nuget.config`, `global.json`. Task 8 edits the **scatter source** in `../.github` (Ginnungagap) — the one legal place — and stops at staging.
- **Git:** per-realm local branch `feature/well-seam-excision` from `master`; subagents may commit locally, never push, never touch `master`, never commit in Bifröst/Glitnir/Ginnungagap (those are staged only). Verify `git branch --show-current` before every commit.
- **Diagnostics:** this seam owns the `NORSE040`-`NORSE049` decade, category `Norse.Wells`, all `DiagnosticSeverity.Error`, `#pragma warning disable RS2008` atop the generator file (house convention). Assigned here: `NORSE040` zero declarations, `NORSE041` duplicate view claim, `NORSE042` empty entity set, `NORSE043` invalid brownfield context type.
- **The connection-string key is `norse_reference`** — underscore, matching `[MigrationConnectionString("norse_reference")]` and `Program.cs`. (The spec's §3.1 example showed a hyphen; corrected same-day — see the spec's Plan-time resolutions note.)
- **Generated-source law:** `sb.AppendCSharp(...)` raw literals, `Utf8NoBom.Encoding`, fully-qualified names in emitted code except `using`s required for extension-style invocation (documented-exception comment in the emitted header, matching the Frozen-using precedent).
- **Dangling xmldoc crefs are build errors** (`GenerateDocumentationFile` + warnings-as-errors) — every task that deletes a member fixes its inbound crefs in the same step.
- House style as in the companion plan: tabs, target-typed `new()`, collection expressions, sealed-by-default, sentence_shaped test names, one PropertyGroup/ItemGroup per csproj alphabetical, IDE0005 deletions, US English.
- Build/test per realm from Bifröst root: `dotnet test <Realm>/<Realm>.slnx`, zero warnings.

## Plan-time resolutions of spec gaps (recorded up front, mirrored into the spec as a dated note)

1. **§3.4's "internal registration core" is public.** Generated code compiles in Yggdrasil's assembly; an `internal` Midgard member is unnameable there. `RegisterCore` becomes `public static RegisterWell<TContext, TEntity, TView>(IServiceCollection)` — XML-doc'd as generator-facing surface.
2. **The generated context cannot reuse `GeneratedNorseModelConfigurations`** (it's `internal` to the entity assembly). The seam generator emits `builder.Entity<T>(eb => T.Configure(eb))` per entity itself — legal cross-assembly because `INorseEntity<TSelf>.Configure` is `static abstract` public. Identical `Configure` methods ⇒ identical model; drift is structurally impossible at the configuration layer.
3. **Re-homing `ReferenceDbContext` to the Migrations project needs Urðarbrunnr's `EntityConfigurationApplicationGenerator` to grow a closure leg** — today it discovers entities only in its own syntax trees, and post-move the entities live one assembly below the context. Task 2 adds referenced-assembly `INorseEntity` discovery (unioned with the syntax-tree leg) and skips emission entirely when no partial `NorseDbContext` subclass exists in the compiling assembly (the emitted class is `internal` — useless without a local context).

---

### Task 1: Asgard — `NorseWellAttribute`

**Files:**
- Create: `Asgard/src/Abstractions.Backend/NorseWellAttribute.cs`
- Test: `Asgard/tests/Abstractions.Backend.Tests/NorseWellAttributeTests.cs`
- Modify: `Asgard/src/Abstractions.Backend/Abstractions.Backend.csproj` (`Description` gains the well-declaration sentence)

**Interfaces:**
- Produces: `Norse.Abstractions.Backend.NorseWellAttribute` — `[AttributeUsage(AttributeTargets.Assembly)]`, primary ctor `(string connectionStringName)`, `string ConnectionStringName { get; }`, `Type? ContextType { get; init; }` (the brownfield escape hatch — spec §3.5's "overload" lands as a named argument, the attribute-idiomatic form). Consumed by the Task 3 generator via display-string match on `"Norse.Abstractions.Backend.NorseWellAttribute"`.

- [ ] **Step 1: Branch** (`git switch -c feature/well-seam-excision master` in `Asgard/`).

- [ ] **Step 2: Failing test** — `NorseWellAttributeTests.cs`:

```csharp
namespace Norse.Abstractions.Backend.Tests;

public sealed class NorseWellAttributeTests
{
	[Fact]
	void Targets_assemblies_only_and_is_sealed()
	{
		var usage = typeof(NorseWellAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
			.Cast<AttributeUsageAttribute>().Single();
		usage.ValidOn.ShouldBe(AttributeTargets.Assembly);
		typeof(NorseWellAttribute).IsSealed.ShouldBeTrue();
	}

	[Fact]
	void Carries_the_connection_string_name_and_optional_brownfield_context()
	{
		NorseWellAttribute greenfield = new("norse_reference");
		greenfield.ConnectionStringName.ShouldBe("norse_reference");
		greenfield.ContextType.ShouldBeNull();

		NorseWellAttribute brownfield = new("norse_identity") { ContextType = typeof(object) };
		brownfield.ContextType.ShouldBe(typeof(object));
	}
}
```

- [ ] **Step 3: Run — expect FAIL** (type missing). **Step 4: Implement** — `NorseWellAttribute.cs`:

```csharp
namespace Norse.Abstractions.Backend;

/// <summary>
/// Declares that this assembly constitutes a well: its <c>INorseEntity</c> entity set and
/// <c>IViewBearer&lt;TView&gt;</c> pairs are discovered and composed by the composition root's seam
/// generator — the realm itself names only the connection-string key, never a provider and never
/// Midgard (well-seam spec §3.1). <see cref="ContextType"/> is the one designed exception (spec
/// §3.5): a brownfield assembly (ASP.NET Core Identity / OpenIddict — Himinbjörg, permanently
/// confined) binds its declaration to a hand-authored context instead of a generated one.
/// </summary>
/// <param name="connectionStringName">The configuration key under <c>ConnectionStrings</c>.</param>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class NorseWellAttribute(string connectionStringName) : Attribute
{
	/// <summary>The configuration key under <c>ConnectionStrings</c>.</summary>
	public string ConnectionStringName { get; } = connectionStringName;

	/// <summary>The brownfield escape hatch — a hand-authored context type; <see langword="null"/> means the seam generator emits the context.</summary>
	public Type? ContextType { get; init; }
}
```

- [ ] **Step 5: Run — expect PASS.** Append to the csproj `Description` (inside the existing sentence flow): `Well declarations live here too: [assembly: NorseWell] names a realm's entity assembly as a well for the composition root's seam generator.`

- [ ] **Step 6: Commit.**

> **SHIP GATE (human):** Asgard PR → CI → merge → tag → publish. Tasks 3 and 5 consume the attribute from the package in CI.

---

### Task 2: Urðarbrunnr — closure leg for `EntityConfigurationApplicationGenerator`

**Files:**
- Modify: `Urdarbrunnr/gen/Persistence.EntityFramework.Generator/EntityConfigurationApplicationGenerator.cs`
- Test: `Urdarbrunnr/tests/Persistence.EntityFramework.Generator.Tests/` — extend the existing harness/tests with a referenced-assembly case

**Interfaces:**
- Consumes: existing discovery (`INorseEntity`1` self-referential implementors via own syntax trees; `NorseDbContext` partial-subclass detection).
- Produces: unchanged emitted shape (`GeneratedNorseModelConfigurations` + partial `ConfigureNorseEntities` override), but the entity list is now the **union** of own-compilation entities and `INorseEntity` implementors found in referenced assemblies (symbol walk over `compilation.SourceModule.ReferencedAssemblySymbols`, display-string matching per the migrations generator's cross-layer-identity note), deduplicated by fully-qualified name; and **no emission at all when the compiling assembly has no partial `NorseDbContext` subclass** (the emitted class is `internal` — unreachable from anywhere else).

- [ ] **Step 1: Branch. Step 2: Failing tests** — in the existing generator test suite, add two cases using the established harness pattern (an in-memory compilation; for the closure case, first compile a small "entity assembly" with one `INorseEntity` implementor to a `MetadataReference` via `CSharpCompilation.EmitToArray`-style helper the suite's harness supports or gains here):

```csharp
	[Fact]
	void Entities_in_referenced_assemblies_are_configured_when_a_local_context_exists()
	{
		// entityAssembly: record RefEntity : INorseEntity<RefEntity> (no context)
		// mainCompilation: partial sealed class MigrationsContext : NorseDbContext (no entities), references entityAssembly
		var generated = Harness.RunWithReference(MainContextSource, EntityAssemblySource);
		generated.ShouldContain("builder.Entity<global::Referenced.RefEntity>(eb => global::Referenced.RefEntity.Configure(eb));");
		generated.ShouldContain("partial class MigrationsContext");
	}

	[Fact]
	void Nothing_is_emitted_when_the_compilation_declares_no_partial_context()
	{
		// entities only, no NorseDbContext subclass — the internal helper class would be dead code
		Harness.Run(EntityOnlySource).ShouldBeEmpty();
	}
```

(Write the two source-text constants concretely in the test file: `Referenced.RefEntity` implementing `INorseEntity<RefEntity>` with an empty `Configure`, and a `MigrationsContext` partial subclass; the harness compiles the entity source into a `MetadataReference` added to the main compilation's references.)

- [ ] **Step 3: Run — expect FAIL. Step 4: Implement** in `EntityConfigurationApplicationGenerator.cs`:
  - Add a referenced-assembly walk beside the syntax-tree leg: for each `IAssemblySymbol` in `compilation.SourceModule.ReferencedAssemblySymbols`, recurse `GlobalNamespace` for named types whose interfaces include a constructed `INorseEntity`1` (metadata name match + containing-namespace display string `Norse.Persistence.EntityFramework` + `TypeArguments[0]` equals the type — the same three-part check the syntax leg applies). Skip assemblies whose name does not start with `Norse.` **is not acceptable** — product realms fork the brand; walk everything except `System.*`/`Microsoft.*`/`netstandard`/`mscorlib` prefixes (cheap pruning, correctness preserved).
  - Union with the own-compilation list into a sorted set of fully-qualified display strings (ordinal sort — deterministic emission).
  - Guard emission: `if (tier1Context is null) return;` before any `AddSource`.
- [ ] **Step 5: Run full Urðarbrunnr suite — expect PASS** (existing single-compilation tests must be untouched by the union — their closure contributes nothing).
- [ ] **Step 6: Commit.**

> **SHIP GATE (human):** Urðarbrunnr PR → CI → merge → tag → publish. Task 5's re-homed context depends on this generator version.

---

### Task 3: Midgard — `RegisterWell` goes public; the reflection path dies

**Files:**
- Modify: `Midgard/src/Infrastructure.Persistence.EntityFramework/ServiceCollectionExtensions.cs`, `WellValidation.cs` (cref), `Infrastructure.Persistence.EntityFramework.csproj` (`Description`)
- Delete: `Midgard/tests/Infrastructure.Persistence.EntityFramework.Tests/AddNorseWellTests.cs`
- Rewrite: `.../AddWellTests.cs` → `RegisterWellTests.cs`; `.../ConstructionParityTests.cs` (retarget)

**Interfaces:**
- Consumes: unchanged internals (`Repository<,,>`, `WellMap`, `WellValidation`).
- Produces: `public static void RegisterWell<TContext, TEntity, TView>(IServiceCollection services)` on `Norse.Infrastructure.Persistence.EntityFramework.ServiceCollectionExtensions` — same body as today's `RegisterCore` (singleton `IReadRepository<TView>` factory with deferred `WellValidation.Validate` + `WellMap.For`), same `[DynamicallyAccessedMembers]` set on `TEntity` verbatim, same `[RequiresUnreferencedCode]` message, `where TContext : DbContext where TEntity : class, IViewBearer<TView> where TView : notnull`. **`AddWell<TContext>` and `AddNorseWell<TContext>` are deleted** — the DbSet scan, `MakeGenericMethod`, `[RequiresDynamicCode]`, and the duplicate-view runtime throw (now the Task 4 generator's `NORSE041`) go with them. Task 4's emitted code is the intended caller.

- [ ] **Step 1: Branch. Step 2: Failing tests first.**
  - `RegisterWellTests.cs` — port `AddWellTests.cs`'s validation coverage onto the closed-generic entry point (the synthetic contexts/entities in that file move across unchanged; only the invocation changes). One test per preserved law:

```csharp
	[Fact]
	void Registers_a_read_repository_for_the_closed_pair()
	{
		ServiceCollection services = [];
		services.AddSingleton<IDbContextFactory<WellContext>>(new PooledFactoryStub());
		ServiceCollectionExtensions.RegisterWell<WellContext, WidgetEntity, WidgetView>(services);
		services.Any(d => d.ServiceType == typeof(IReadRepository<WidgetView>)).ShouldBeTrue();
	}

	[Fact]
	void The_mirror_law_still_throws_at_first_resolution_naming_the_member()
	{
		// BrokenMirrorContext / BrokenEntity / BrokenView move verbatim from AddWellTests.cs;
		// resolution of IReadRepository<BrokenView> must throw InvalidOperationException
		// naming the missing scalar — the deferred WellValidation path is untouched.
	}
```

  (Write both fully during implementation — the second body is the existing `AddWellTests` mirror-law arrange/assert with `RegisterWell` in place of `AddWell`; the `[NotProjected]` exemption test ports the same way. The DbSet-discovery and duplicate-view tests do **not** port — discovery is no longer runtime behavior; their successors are Task 4 generator tests.)
  - `ConstructionParityTests.cs` — retarget the runtime leg from the deleted `AddNorseWell` onto Urðarbrunnr's factory seam (the exact shape the generated code uses), keeping the table-name/schema assertions verbatim:

```csharp
		runtimeBuilder.AddNorseContextFactory<WellContext>(NorsePostgresEfProvider.Instance, "test");
```

- [ ] **Step 3: Run — expect FAIL. Step 4: Implement** — rename/publicize `RegisterCore` → `RegisterWell` (xmldoc: "Generator-facing registration core — called by the seam generator's emitted `AddNorseWells`, never by hand-written realm code; the closed generics are the discovery output, not an invitation."), delete `AddWell`/`AddNorseWell` and their now-unused usings (`System.Reflection` if orphaned — IDE0005 law), fix `WellValidation.cs:13`'s cref (point it at `RegisterWell`), rewrite the csproj `Description`'s `AddWell<TContext>` clause to name the seam generator + `RegisterWell`. Delete `AddNorseWellTests.cs`.
- [ ] **Step 5: Run full Midgard suite — expect PASS. Step 6: Commit** (no gate yet — Task 4 ships in the same Midgard release).

---

### Task 4: Midgard — the seam generator

**Files:**
- Create: `Midgard/gen/Infrastructure.Persistence.EntityFramework.Generator/` — `WellCompositionGenerator.cs`, `WellCompositionEmitter.cs`, `Infrastructure.Persistence.EntityFramework.Generator.csproj`
- Create: `Midgard/tests/Infrastructure.Persistence.EntityFramework.Generator.Tests/` — `WellGeneratorTestHarness.cs`, `WellDiscoveryTests.cs`, `WellEmissionTests.cs`, `WellDiagnosticTests.cs`, csproj
- Modify: `Midgard/src/Infrastructure.Persistence.EntityFramework/Infrastructure.Persistence.EntityFramework.csproj` — sibling analyzer `ProjectReference` + `IncludeGeneratorInPackage` target (the exact two-DLL shape `Infrastructure.Web.Server.csproj:14-34` carries: generator DLL + `Norse.Abstractions.Emit.dll` into `analyzers/dotnet/cs/`)
- Modify: `Midgard.slnx` (gen + tests entries)

**Interfaces:**
- Consumes: `NorseWellAttribute` (display-string `"Norse.Abstractions.Backend.NorseWellAttribute"` — Task 1), `INorseEntity`1`/`IViewBearer`1` symbol matching, `RegisterWell` (Task 3), `AddNorseContextFactory` (Urðarbrunnr, existing), `NorseDbContext`, `INorseEfProvider`.
- Produces: one emitted file `NorseWellComposition.g.cs` in `namespace {compilation.AssemblyName}` (the `RegisterNorseOutcomeSurrogates` precedent — public, callable from tests) containing, per greenfield well, a `file`-scoped context, and one public entry point:

```csharp
// <auto-generated/>
#nullable enable
#pragma warning disable CS1591
// AddNorseContextFactory is an extension-block member — extension-style invocation requires the
// namespace in scope; a deliberate, documented exception to the fully-qualified emitted style.
using Norse.Persistence.EntityFramework;

namespace Norse.Hosting.Web.Server;

file sealed class ReferenceDataEntityFrameworkWellContext(
	global::Microsoft.EntityFrameworkCore.DbContextOptions<ReferenceDataEntityFrameworkWellContext> options) :
	global::Norse.Persistence.EntityFramework.NorseDbContext(options)
{
	protected override void ConfigureNorseEntities(global::Microsoft.EntityFrameworkCore.ModelBuilder builder)
	{
		base.ConfigureNorseEntities(builder);
		builder.Entity<global::Norse.Reference.Data.EntityFramework.CountryOrArea>(eb => global::Norse.Reference.Data.EntityFramework.CountryOrArea.Configure(eb));
		builder.Entity<global::Norse.Reference.Data.EntityFramework.Region>(eb => global::Norse.Reference.Data.EntityFramework.Region.Configure(eb));
	}
}

public static class NorseWellComposition
{
	/// <summary>Wires every [NorseWell]-declared well in this compilation's reference closure.</summary>
	public static global::Microsoft.Extensions.Hosting.IHostApplicationBuilder AddNorseWells(
		this global::Microsoft.Extensions.Hosting.IHostApplicationBuilder builder,
		global::Norse.Persistence.EntityFramework.INorseEfProvider provider)
	{
		builder.AddNorseContextFactory<ReferenceDataEntityFrameworkWellContext>(provider, "norse_reference");
		global::Norse.Infrastructure.Persistence.EntityFramework.ServiceCollectionExtensions.RegisterWell<ReferenceDataEntityFrameworkWellContext, global::Norse.Reference.Data.EntityFramework.CountryOrArea, global::Norse.Reference.Data.EntityFramework.CountryOrAreaView>(builder.Services);
		return builder;
	}
}
#pragma warning restore CS1591
```

  (Shown closed over the reference well as the worked example; the emitter renders N wells. Context type name: declaring assembly name minus the `Norse.` prefix, dots removed, + `WellContext` — deterministic. A brownfield declaration emits no context and closes both calls over `global::{ContextType}` instead. Entities that are `INorseEntity` but not `IViewBearer` (e.g. `Region`) are configured in the context but get no `RegisterWell` call. `file`-scoped context + entry point are necessarily co-emitted in the one file — the type is unnameable anywhere else.)

- Diagnostics produced: `NORSE040` (generator referenced, zero `[NorseWell]` in closure), `NORSE041` (two `IViewBearer` roots claim one view within a well — carries both entity names, mirroring the deleted runtime throw's message), `NORSE042` (declared well, zero `INorseEntity` implementors), `NORSE043` (brownfield `ContextType` missing or not a `NorseDbContext`/`INorseDbContext`-satisfying `DbContext`).

- [ ] **Step 1: Failing tests.** Harness: in-memory compilation named `Norse.Hosting.Web.Server`, references built from `typeof(...).Assembly.Location` for `Norse.Abstractions.Backend`, `Norse.Persistence.EntityFramework`, EF Core, plus a helper compiling a synthetic "declaring assembly" source (attribute + entities) to a `MetadataReference` — same two-compilation shape Task 2's harness case uses. Tests (write fully):
  - Discovery: declared assembly with `CountryOrArea`-shaped `IViewBearer` entity + plain entity → emitted file contains one `file sealed class`, both `builder.Entity<...>` lines, one `RegisterWell` line, `"norse_reference"` literal.
  - Dispatch/entry: emitted namespace equals compilation assembly name; `AddNorseWells` signature exact.
  - Brownfield: declaration with `ContextType` → no `file` class, both calls close over the named type.
  - Diagnostics: each of `NORSE040`/`041`/`042`/`043` from a minimal arranged closure.
  - Compile gate: `RunAndCompile` of the full emission produces zero errors.
- [ ] **Step 2: Run — expect FAIL. Step 3: Implement** generator + emitter (discovery: `[compilation.Assembly, .. compilation.SourceModule.ReferencedAssemblySymbols]`, assembly-attribute read via `ConstructorArguments[0]` string + `NamedArguments` `ContextType` as `INamedTypeSymbol`; own-assembly inclusion kept for harness parity, documented as the migrations generator does). csproj mirrors `Infrastructure.Web.Server.Generator.csproj` (Description + `NorseRef Abstractions.Emit`).
- [ ] **Step 4: Run — expect PASS. Step 5:** wire the host csproj (`ProjectReference OutputItemType="Analyzer"` + `IncludeGeneratorInPackage` with both DLLs), slnx entries, run the full Midgard suite — expect PASS.
- [ ] **Step 6: Realm docs** — `Midgard/CLAUDE.md`/`README.md`: the persistence section now names the seam generator and `RegisterWell`; the `AddWell` era ends here. **Commit.**

> **SHIP GATE (human):** Midgard PR → CI → merge → tag → publish (Tasks 3+4 together).

---

### Task 5: Mimisbrunnr — the context leaves the runtime surface; the well is declared

**Files:**
- Move: `src/Reference.Data.EntityFramework/ReferenceDbContext.cs` → `src/Reference.Data.EntityFramework.Migrations/ReferenceDbContext.cs`
- Create: `src/Reference.Data.EntityFramework/WellDeclaration.cs`
- Modify: `src/Reference.Data.EntityFramework.Migrations/Reference.Data.EntityFramework.Migrations.csproj`, both provider design-time factories' `using`s, test fixtures' `using`s; regenerate both providers' `Migrations/` + `schema/*.sql`
- Modify: `Mimisbrunnr/CLAUDE.md` + `README.md` (context's new home)

**Interfaces:**
- Consumes: Task 2's closure-leg generator (Urðarbrunnr release), Task 1's attribute (Asgard release).
- Produces: `[assembly: NorseWell("norse_reference")]` on `Norse.Reference.Data.EntityFramework`; `ReferenceDbContext` now `Norse.Reference.Data.EntityFramework.Migrations.ReferenceDbContext`, design-time/migrations-only, **without the DbSet property** (`DbSet<T>` earns nothing here — `Set<T>()` serves the seed contributor, the model comes from the generated `ConfigureNorseEntities`, and table naming falls back to the identical CLR type name).

- [ ] **Step 1: Branch. Step 2: Failing state first** — add `WellDeclaration.cs` to the entity project:

```csharp
using Norse.Abstractions.Backend;

[assembly: NorseWell("norse_reference")]
```

and write the model test **before** moving the context: extend `tests/Reference.Data.EntityFramework.Migrations.Tests` (the context's new test home) with a pinned table-name test so the DbSet removal cannot drift silently:

```csharp
	[Fact]
	void The_model_keeps_the_committed_singular_table_names()
	{
		var context = ReferenceDbContextFactoryTestHelper.CreateDesignTime(); // the existing factory-test path
		context.Model.FindEntityType(typeof(CountryOrArea))!.GetTableName().ShouldBe("country_or_area");
		context.Model.FindEntityType(typeof(Region))!.GetTableName().ShouldBe("region");
	}
```

(Anchor it on whatever the existing `ReferenceDbContextFactoryTests` construct — reuse their construction helper rather than inventing one.)

- [ ] **Step 3: Move the context.** `git mv` the file; namespace → `Norse.Reference.Data.EntityFramework.Migrations`; delete the `public DbSet<CountryOrArea> CountryOrArea => Set<CountryOrArea>();` member and rewrite the class xmldoc (the DbSet-fixes-the-table-name rationale is dead: the entity CLR name now names the table, pinned by the test above and the committed snapshot; the well is declared by `[assembly: NorseWell]` and discovered from the closure). In the Migrations csproj add the generator leg:

```xml
		<NorseRef Include="Persistence.EntityFramework">
			<Repo>Urdarbrunnr</Repo>
			<Generator>true</Generator>
		</NorseRef>
```

Remove `Generator="true"` from the **entity** project's `Persistence.EntityFramework` NorseRef (keep the reference — entities use `EntityTypeBuilder<T>`; the generator just has nothing to emit there anymore, per Task 2's null-context skip). Sweep `using`s: both design-time factories, `NorseReferenceMigrationContributor` (same namespace now — using likely deletable), `ReferenceDataSeedContributor` (ditto), test fixtures that construct the context.

- [ ] **Step 4: Squash again** (context changed assembly — snapshot's `[DbContext(typeof(...))]` must re-home): the same `rm -rf Migrations && dotnet ef migrations add InitialCreate` pair per provider as the companion plan's Task 6, plus the `schema/*.sql` regeneration check. Diff the fresh snapshot's table names against the pinned test's expectations.
- [ ] **Step 5: Full realm suite — expect PASS** (Testcontainers migrate+seed proves the moved context end to end). Realm docs updated (context's home, the declaration). **Commit.**

> **SHIP GATE (human):** Mimisbrunnr PR → CI → merge → tag → publish.

---

### Task 6: Mimir — sheds Midgard and the provider

**Files:**
- Modify: `Mimir/src/Reference.Web.Server/Reference.Web.Server.csproj`, `ServiceCollectionExtensions.cs`
- Modify: `Mimir/tests/Reference.Web.Server.Tests/` (any test wiring `AddNorseReferenceService`)

**Interfaces:**
- Produces: `AddNorseReferenceService()` — parameterless, `extension(IServiceCollection services)`, registering exactly `AddNorseReferenceWebServerHandlers()` and `AddScoped<IReferenceService, ReferenceService>()`. No Midgard, no Urðarbrunnr provider, no connection string. `CountryQueryHandler` is untouched — it was already clean.

- [ ] **Step 1: Branch. Step 2:** update/write the test first — a registration test asserting `AddNorseReferenceService()` on a bare `ServiceCollection` registers `IReferenceService` and the generated handler set, and (the structural pin) that `typeof(ServiceCollectionExtensions).Assembly.GetReferencedAssemblies()` contains no name starting `Norse.Infrastructure` and no `Norse.Persistence.EntityFramework.PostgreSQL`.
- [ ] **Step 3: Implement.** `ServiceCollectionExtensions.cs` becomes:

```csharp
namespace Norse.Reference.Web.Server;

/// <summary>Registration for Reference.Web.Server's gRPC reference-data service — handlers and the service itself; persistence composition is the composition root's act (well-seam spec §3.3).</summary>
public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>Registers the generated mediator handler wiring and <see cref="IReferenceService"/>.</summary>
		/// <returns>The same <paramref name="services"/> for chaining.</returns>
		public IServiceCollection AddNorseReferenceService()
		{
			services.AddNorseReferenceWebServerHandlers();
			services.AddScoped<IReferenceService, ReferenceService>();
			return services;
		}
	}
}
```

Delete the three dead `using`s. Delete the two csproj NorseRef blocks (`Infrastructure.Persistence.EntityFramework`/Midgard, `Persistence.EntityFramework.PostgreSQL`/Urdarbrunnr). `ReferenceService.cs`'s "Mímir stays Midgard-blind" doc claim is finally true — no edit needed.

- [ ] **Step 4: Realm suite — expect PASS. Commit.**

> **SHIP GATE (human):** Mimir PR → CI → merge → tag → publish.

---

### Task 7: Yggdrasil — adopts `AddNorseWells`; the E2E proof

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`, `Program.cs`, `Yggdrasil/Directory.Packages.props`, `Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj`, `CountryLookupE2ETests.cs`

**Interfaces:**
- Consumes: everything above at released versions.
- Produces: the live composition — `builder.AddNorseWells(NorsePostgresEfProvider.Instance)` wiring the reference well through the generated context; the E2E suite as the end-to-end proof (spec §4).

- [ ] **Step 1: Branch (verify current branch first — this realm carries parallel work). Step 2: csproj + pins.** `Hosting.Web.Server.csproj` gains three NorseRefs (alphabetical among the existing ones):

```xml
		<NorseRef Include="Infrastructure.Persistence.EntityFramework" Generator="true">
			<Repo>Midgard</Repo>
		</NorseRef>
		<NorseRef Include="Persistence.EntityFramework.PostgreSQL">
			<Repo>Urdarbrunnr</Repo>
		</NorseRef>
		<NorseRef Include="Reference.Data.EntityFramework">
			<Repo>Mimisbrunnr</Repo>
		</NorseRef>
```

(The declaring assembly and the provider stop being transitive accidents through Mimir — `NORSE030`'s lesson applied to wells; without the explicit refs, Mimir's Task 6 shedding would have silently emptied the closure.) `Directory.Packages.props`: add pins for `Norse.Infrastructure.Persistence.EntityFramework` and `Norse.Persistence.EntityFramework.PostgreSQL` if absent; bump every realm version property to the ship-gate tags.

- [ ] **Step 3: `Program.cs`.** Delete the `norseReferenceConnectionString` local and its throw (the generated factory registration fail-fasts on the missing key with the same message shape); the services chain's `.AddNorseReferenceService(norseReferenceConnectionString)` becomes `.AddNorseReferenceService()`; add, as its own statement immediately before the services chain (builder-shaped — cannot join it):

```csharp
builder.AddNorseWells(NorsePostgresEfProvider.Instance);
```

with `using Norse.Persistence.EntityFramework.PostgreSQL;` hoisted. Run `dotnet build` — the generator emits `AddNorseWells` into `Norse.Hosting.Web.Server`, already in scope.

- [ ] **Step 4: E2E fixture rewrite** (the fiddliest edit — the old `HostBuilder` is not an `IHostApplicationBuilder`, and the container's connection string is dynamic). Restructure `CreateHostAsync` onto `WebApplication.CreateBuilder`:

```csharp
	static async Task<WebApplication> CreateHostAsync(string connectionString, CancellationToken cancellationToken)
	{
		AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
		NorseGrpcServerRegistration.RegisterNorseOutcomeSurrogates();
		var principal = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"));

		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Configuration["ConnectionStrings:norse_reference"] = connectionString;
		builder.AddNorseWells(NorsePostgresEfProvider.Instance);
		builder.Services
			.AddLogging()
			.AddRouting()
			.AddNorsePipeline()
			.AddNorseCodeFirstGrpc()
			.AddNorseReferenceService()
			.AddScoped<IPrincipalAccessor>(_ => new ReferenceTestPrincipalAccessor(principal));
		builder.Services.AddAuthorizationBuilder().AddPolicy(ReferencePolicies.Public, p => p.RequireAssertion(_ => true));

		var app = builder.Build();
		app.MapGrpcService<ReferenceService>();
		await app.StartAsync(cancellationToken);
		return app;
	}
```

(Call sites: `using var host = ...` stays — `WebApplication` is `IAsyncDisposable`; switch to `await using`. `host.GetTestServer()` works on `WebApplication` after `UseTestServer()`.) The fixture's migrate/seed block keeps constructing `ReferenceDbContext` directly — its `using` moves to `Norse.Reference.Data.EntityFramework.Migrations` (the context's new home; the test csproj's `VersionOverride` package refs bump to the new Mimisbrunnr tag). This is the parity proof the deleted `ConstructionParityTests` pointed at, now live: the migration-built schema serves queries through the generated context — any table-name drift fails here, loudly, against real Postgres.

- [ ] **Step 5: Full Yggdrasil suite — expect PASS** (Docker required for the E2E collection). **Commit.**

> **SHIP GATE (human):** Yggdrasil PR → CI green → merge. `dotnet run --project src/Orchestration.AppHost` from Bifröst remains the final smoke: dashboard up, migrations service completes, country lookup answers.

---

### Task 8: Ginnungagap — the build check (scatter source; staged, never committed by agents)

**Files:**
- Modify: `../.github/config/src/Directory.Build.targets` and `../.github/config/tests/Directory.Build.targets` (append the same target to both — a test project NorseRef-ing Midgard is the same sin)

**Interfaces:** none — MSBuild enforcement. Yggdrasil is exempt for free (`manifest.psd1` keeps it out of the `nuget` group; it owns its own copies).

- [ ] **Step 1: Append the target** after `_NorseRemoveUnwantedGeneratorAnalyzers` in both files:

```xml
	<!--
		Midgard is consumed by exactly one realm: Yggdrasil (which owns its own copy of this file
		and never receives this one). Any other realm referencing Midgard has annexed the
		composition root's job — declare a [NorseWell] (or the appropriate seam) instead.
		Ruling and design: Glitnir/docs/Platform/specs/2026-08-01-well-seam-midgard-excision-design.md §2/§3.6
	-->
	<Target Name="_NorseMidgardIsYggdrasilOnly" BeforeTargets="CoreCompile" Condition="'@(NorseRef)' != '' or '@(NorseDesignRef)' != ''">
		<ItemGroup>
			<_NorseMidgardRef Include="@(NorseRef->WithMetadataValue('Repo', 'Midgard'))" />
			<_NorseMidgardRef Include="@(NorseDesignRef->WithMetadataValue('Repo', 'Midgard'))" />
		</ItemGroup>
		<Error Condition="'@(_NorseMidgardRef)' != ''"
			Text="NorseRef '@(_NorseMidgardRef)' targets Repo=Midgard — Midgard is consumed by Yggdrasil alone (well-seam spec §2). Compose through a declared seam instead." />
	</Target>
```

- [ ] **Step 2: Verify both directions by hand** (the scatter repo has no test harness — this is the spec §4 fixture, run manually and recorded in the task report): in a scratch directory under the Bifröst tree, a minimal csproj with `<NorseRef Include="Infrastructure.Web.Server"><Repo>Midgard</Repo></NorseRef>` importing the edited targets file must fail with the message above; removing the NorseRef must build. Delete the scratch when done.
- [ ] **Step 3: Stage in `../.github` and stop.** Buvy merges and runs the scatter; the fan-out lands the check in all eleven nuget-group realms — after Task 6, none of them references Midgard, so the fan-out is green by construction. **Do not commit; do not run the scatter.**

---

### Task 9: Spec back-annotation + doc truth (staged only)

**Files:**
- Modify: `Glitnir/docs/Platform/specs/2026-08-01-well-seam-midgard-excision-design.md`, `Glitnir/docs/the-crooked-path.md` (nothing — entry #12 already covers this; touch only if a step above deviated), `Asgard/src/Abstractions.Backend/IViewBearer{TView}.cs` doc line

**Steps:**
- [ ] The spec's §3.1 underscore fix and the dated **Plan-time resolutions (2026-08-01)** note (§5a) were applied when this plan was written — verify they still match what actually shipped, and extend the note if any task above deviated further. Stage in Glitnir.
- [ ] `IViewBearer{TView}.cs:7`'s "Midgard's `AddWell` validates the mirror at startup" → "Midgard's well registration validates the mirror at first resolution" — fold into Task 1's Asgard branch if caught in time; otherwise a one-line follow-up on an Asgard branch here.

---

## Self-Review Notes (already applied)

- **Spec coverage:** §2 doctrine → Task 8 (Midgard check) with Asgard-slicing explicitly out of scope per spec §6; §3.1 → Task 1; §3.2 → Task 4 (all three numbered behaviors + all four diagnostics; NORSE040-043 from the free decade); §3.3 → Tasks 5-6; §3.4 → Task 3 (with the public-surface deviation recorded in the spec note, Task 9); §3.5 → Task 1's `ContextType` + Task 4's brownfield emission tests, Himinbjörg untouched (verified: zero Midgard refs there today); §3.6 → Task 8; §4 → Tasks 3/4 (generator + validation suites), 7 (E2E), 8 (manual fixture); §5 sequencing honored, with Urðarbrunnr inserted as Task 2 (plan-time discovery, recorded in the spec note); §6 respected (`WellMap.For` untouched, one provider, no analyzer extension).
- **Type consistency:** `RegisterWell<TContext, TEntity, TView>(IServiceCollection)` (Tasks 3, 4's emission, 7's build); `AddNorseWells(this IHostApplicationBuilder, INorseEfProvider)` (Tasks 4, 7); `NorseWellAttribute(string)` + `ContextType` init (Tasks 1, 4, 5); generated context naming rule stated once (Task 4) and only echoed.
- **Failure modes priced:** table-name drift (Task 5's pinned model test + Task 7's live schema proof), closure trimming (Task 7's three explicit NorseRefs), dangling crefs (Tasks 3, 6 fix inbound crefs in-step), scatter ordering (Task 8 last, staged only, fan-out green by construction).
