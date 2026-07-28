# EF Provider Seam and Repackaging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (default per `../../../CLAUDE.md` §2.8) or superpowers:executing-plans (narrow separate-session fallback) to implement this plan task-by-task, paired with superpowers:test-driven-development on every task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reshape Urðarbrunnr's EF chassis into 3 neutral packages + N thin provider bindings + 1 provider-agnostic generator, per the ratified spec `../specs/2026-07-27-ef-provider-seam-repackaging-design.md`.

**Architecture:** A binding contract pair (`INorseEfProvider` / `INorseEfMigrationProvider`) in the neutral root carries all provider knowledge; one shared options choreography (`ApplyNorseProviderOptions`) is consumed by the runtime path, the migration-host path, and the design-time factory; a single Roslyn generator discovers contributors, seed contributors, and the provider binding from the compilation's reference closure and emits `AddNorseMigrations()`.

**Tech Stack:** .NET 11 preview (C# 15), EF Core 11 preview, Aspire 13 EF integrations, Roslyn incremental generators (netstandard2.0), xUnit v3 + Shouldly + NSubstitute on Microsoft Testing Platform.

## Global Constraints

- **Repository:** all work happens inside the Urðarbrunnr repo (`Urdarbrunnr/` submodule of the Bifröst workspace). All file paths below are relative to the Urðarbrunnr repo root; run all commands from that root (use `env -C <repo-root>`, never `cd &&` — the shell's `_update_prompt` hook breaks compound `cd`).
- **Branch:** create and stay on `feature/ef-provider-seam`. Commit locally after each task; **never push, never touch `master`, never commit in any other repo.** Verify `git branch --show-current` before every commit — Buvy curates trees in parallel.
- **Hands-off files (halt-and-ask, never edit):** `src/Directory.Build.props`, `src/Directory.Build.targets`, `tests/Directory.Build.props`, `tests/Directory.Build.targets`, `gen/Directory.Build.props`, `.editorconfig` — the first five are scatter-managed or realm plumbing this plan needs zero changes to. If a task seems to require editing one, stop and ask.
- **House rules:** `../../house-rules.md` law applies in full. Highlights that bite here: tabs; every class `sealed`/`abstract`/`static`; omit default accessibility; C# 14 extension blocks for new extension members; target-typed `new()`; collection expressions; no string concatenation; XML docs on all publicly visible src members (CS1591 is an error in src); `ConfigureAwait(false)` in src, never in tests; test methods bare `void`/`async Task` with sentence_shaped_names; Shouldly assertions; `InternalsVisibleTo` is already granted to `$(AssemblyName).Tests` — never escalate accessibility for tests.
- **Csproj law:** one `<PropertyGroup>` + one `<ItemGroup>`, members alphabetical within item type; framework-tracking packages `Version="11.*-*"`, Aspire `Version="13.*"`, Roslyn `Version="*"`.
- **Warnings are errors platform-wide.** IDE0005 (unused using): delete the line, never suppress. Repeated same-code warnings: stop and report per the Suppression Law.
- **Verification bar for this plan:** `dotnet build Urdarbrunnr.slnx` and `dotnet test Urdarbrunnr.slnx` green, plus a Release `dotnet pack` smoke on `src/Persistence.EntityFramework.Migrations` proving generator bundling. **The Bifröst AppHost live run is explicitly OUT of this plan** — Yggdrasil still references the deleted `Design.PostgreSQL` package until the adoption sweep (spec §11, dedicated follow-on session), so a workspace-wide build is expected red until then. Do not "fix" downstream realms from this branch.
- **No `dotnet ef` invocations and no database connections anywhere in this plan.**
- **Expected-red map — do not repair projects scheduled for the axe.** `src/Persistence.EntityFramework.PostgreSQL` and `.SqlServer` are red from Task 2 until Tasks 5–6 rebuild them. The two `Design.{Provider}` src projects and all four `Design.{Provider}(.Generator).Tests` projects go red from Task 2 (old `ApplyNorseConventions` signature) and stay red after Task 4 moves the contributor/attribute namespace — they are deleted wholesale in Task 9. A red build in any of those projects between tasks is the plan working, not a defect.

## File Structure (end state)

```
src/
	Persistence.EntityFramework/            # + INorseEfProvider.cs, INorseEfMigrationProvider.cs,
	                                        #   NorseNameRewriters.cs, NorseContextExtensions.cs
	                                        #   (modified: SnakeCaseNameRewriter, naming convention stack,
	                                        #   NorseDbContextOptionsExtensions, csproj)
	Persistence.EntityFramework.Migrations/ # NEW: EfMigrationContributor, MigrationConnectionStringAttribute,
	                                        #   NorseMigrationContextExtensions, generator packing target
	Persistence.EntityFramework.Design/     # + NorseDesignTimeDbContextFactory.cs; loses contributor+attribute
	Persistence.EntityFramework.PostgreSQL/ # NorsePostgresEfProvider.cs only
	Persistence.EntityFramework.SqlServer/  # NorseSqlServerEfProvider.cs only
	(DELETED: Persistence.EntityFramework.Design.PostgreSQL, Persistence.EntityFramework.Design.SqlServer)
gen/
	Persistence.EntityFramework.Generator/  # untouched
	Persistence.EntityFramework.Migrations.Generator/  # NEW: consolidated generator + discovery
	(DELETED: Design.PostgreSQL.Generator, Design.SqlServer.Generator, Design.Generator.Shared)
tests/
	Persistence.EntityFramework.Tests/                       # extended
	Persistence.EntityFramework.Migrations.Tests/            # NEW
	Persistence.EntityFramework.Migrations.Generator.Tests/  # NEW
	Persistence.EntityFramework.PostgreSQL.Tests/            # rewritten
	Persistence.EntityFramework.SqlServer.Tests/             # rewritten
	Persistence.EntityFramework.Design.Tests/                # extended
	(DELETED: Design.PostgreSQL.Tests, Design.SqlServer.Tests,
	 Design.PostgreSQL.Generator.Tests, Design.SqlServer.Generator.Tests)
```

---

### Task 0: Fail-fast pre-flight — prove the Aspire-equivalence claim with zero Norse code

The plan's single acknowledged assumption (spec §5) is that `AddDbContextPool` + `Enrich{P}DbContext`
covers Aspire's `Add{P}DbContext`. That claim needs none of Tasks 1–4's code to test — so test it
first, before four tasks are built on top of it. This is a throwaway test: run it, then delete it;
Task 5 promotes the real version into the binding's permanent suite.

**Files:**
- Create then delete: `tests/Persistence.EntityFramework.PostgreSQL.Tests/AspireEquivalencePreflightTests.cs`

- [ ] **Step 1: Create the branch**

```bash
git branch --show-current   # confirm starting point is master
git checkout -b feature/ef-provider-seam
```

- [ ] **Step 2: Write the throwaway test**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

public sealed class AspireEquivalencePreflightTests
{
	[Fact]
	void AddDbContextPool_plus_Enrich_covers_every_service_AddNpgsqlDbContext_registers()
	{
		// Pure-Aspire comparison — no Norse code involved. Scope note: this compares registered
		// ServiceTypes only, NOT Aspire's connection-string precedence semantics; the AppHost live
		// run in the adoption sweep owns that question, and a green result here does not close it.
		static HostApplicationBuilder CreateBuilder()
		{
			var builder = Host.CreateApplicationBuilder();
			builder.Configuration.AddInMemoryCollection(
				new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = "Host=localhost;Database=test" });
			return builder;
		}

		var aspire = CreateBuilder();
		aspire.AddNpgsqlDbContext<PreflightContext>("test-db");

		var manual = CreateBuilder();
		var connectionString = manual.Configuration.GetConnectionString("test-db")!;
		manual.Services.AddDbContextPool<PreflightContext>(opts => opts.UseNpgsql(connectionString));
		manual.EnrichNpgsqlDbContext<PreflightContext>();

		var aspireTypes = aspire.Services.Select(d => d.ServiceType).ToHashSet();
		var manualTypes = manual.Services.Select(d => d.ServiceType).ToHashSet();
		aspireTypes.Except(manualTypes).ShouldBeEmpty();
	}

	sealed class PreflightContext(DbContextOptions<PreflightContext> options) : NorseDbContext(options);
}
```

- [ ] **Step 3: Run it**

Run: `dotnet test tests/Persistence.EntityFramework.PostgreSQL.Tests/Persistence.EntityFramework.PostgreSQL.Tests.csproj`
Expected: PASS. **If it fails: HALT the plan immediately** — investigate each missing `ServiceType`; a load-bearing difference is a design-level finding against spec §5 and goes back to Buvy before any other task runs. Do not proceed into Task 1 on a red pre-flight.

- [ ] **Step 4: Delete the throwaway and confirm a clean tree**

```bash
rm tests/Persistence.EntityFramework.PostgreSQL.Tests/AspireEquivalencePreflightTests.cs
git status --short   # nothing staged, nothing new — no commit for this task
```

---

### Task 1: Branch + UPPER_SNAKE casing target on the rewriter

**Files:**
- Modify: `src/Persistence.EntityFramework/SnakeCaseNameRewriter.cs`
- Create: `src/Persistence.EntityFramework/NorseNameRewriters.cs`
- Test: `tests/Persistence.EntityFramework.Tests/NorseNameRewritersTests.cs` (new)

**Interfaces:**
- Consumes: existing `internal static class SnakeCaseNameRewriter` with `internal static string RewriteName(string name)`.
- Produces: `public static class NorseNameRewriters` with `public static string LowerSnakeCase(string name)` and `public static string UpperSnakeCase(string name)` (namespace `Norse.Persistence.EntityFramework`); `SnakeCaseNameRewriter.RewriteName(string name, bool uppercase)`. Later tasks pass these as `Func<string, string>` method groups.

- [ ] **Step 1: Confirm the branch**

```bash
git branch --show-current   # must print feature/ef-provider-seam (created in Task 0)
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Persistence.EntityFramework.Tests/NorseNameRewritersTests.cs`:

```csharp
namespace Norse.Persistence.EntityFramework.Tests;

public sealed class NorseNameRewritersTests
{
	[Theory]
	[InlineData("CountryOrArea", "country_or_area")]
	[InlineData("ISOCode", "iso_code")]
	[InlineData("Alpha2", "alpha2")]
	[InlineData("already_snake", "already_snake")]
	void LowerSnakeCase_matches_the_engine_native_postgres_style(string input, string expected) =>
		NorseNameRewriters.LowerSnakeCase(input).ShouldBe(expected);

	[Theory]
	[InlineData("CountryOrArea", "COUNTRY_OR_AREA")]
	[InlineData("ISOCode", "ISO_CODE")]
	[InlineData("Alpha2", "ALPHA2")]
	[InlineData("already_snake", "ALREADY_SNAKE")]
	void UpperSnakeCase_matches_the_engine_native_oracle_style(string input, string expected) =>
		NorseNameRewriters.UpperSnakeCase(input).ShouldBe(expected);

	[Fact]
	void Upper_and_lower_agree_on_word_boundaries()
	{
		var lower = NorseNameRewriters.LowerSnakeCase("PolicyBoundEvent2Handler");
		var upper = NorseNameRewriters.UpperSnakeCase("PolicyBoundEvent2Handler");
		upper.ShouldBe(lower.ToUpperInvariant());
	}
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Persistence.EntityFramework.Tests/Persistence.EntityFramework.Tests.csproj`
Expected: FAIL — `NorseNameRewriters` does not exist (compile error).

- [ ] **Step 4: Implement**

In `SnakeCaseNameRewriter.cs`, change the signature to `internal static string RewriteName(string name, bool uppercase)` and make the final case mapping honor it — in the `UppercaseLetter`/`TitlecaseLetter` branch replace the existing `char.ToLower` line, and in the `LowercaseLetter`/`DecimalDigitNumber` branch add the same mapping before `break` (the `ToUpper`/`ToLower` of a digit is the digit itself, so no digit special-casing):

```csharp
currentChar = uppercase ?
	char.ToUpper(currentChar, CultureInfo.InvariantCulture) :
	char.ToLower(currentChar, CultureInfo.InvariantCulture);
```

Update the one existing internal call-site file (`NorseSnakeCaseNamingConvention.cs` still calls the one-arg form) minimally by passing `uppercase: false` — Task 2 replaces those call sites entirely; this keeps the build green between tasks. Then create `NorseNameRewriters.cs`:

```csharp
namespace Norse.Persistence.EntityFramework;

/// <summary>
/// The engine-native identifier rewriters a provider binding exposes via
/// <c>INorseEfProvider.NameRewriter</c> (declared in Task 3). The rewrite algorithm itself stays
/// internal in <see cref="SnakeCaseNameRewriter"/>; these are the only public entry points, one per
/// engine-native style — the binding picks one (or none), realms never choose.
/// </summary>
public static class NorseNameRewriters
{
	/// <summary>
	/// Rewrites an identifier to lower snake_case — PostgreSQL's escape-free native style
	/// (unquoted identifiers fold to lowercase there).
	/// </summary>
	/// <param name="name">The identifier to rewrite.</param>
	/// <returns>The snake_case identifier.</returns>
	public static string LowerSnakeCase(string name) =>
		SnakeCaseNameRewriter.RewriteName(name, uppercase: false);

	/// <summary>
	/// Rewrites an identifier to UPPER_SNAKE_CASE — Oracle's escape-free native style
	/// (unquoted identifiers fold to uppercase there).
	/// </summary>
	/// <param name="name">The identifier to rewrite.</param>
	/// <returns>The UPPER_SNAKE_CASE identifier.</returns>
	public static string UpperSnakeCase(string name) =>
		SnakeCaseNameRewriter.RewriteName(name, uppercase: true);
}
```

Update `tests/Persistence.EntityFramework.Tests/SnakeCaseNameRewriterTests.cs` call sites for the new second parameter (`uppercase: false`).

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/Persistence.EntityFramework.Tests/Persistence.EntityFramework.Tests.csproj`
Expected: PASS (all tests in project, not just the new ones).

- [ ] **Step 6: Commit**

```bash
git add -A src/Persistence.EntityFramework tests/Persistence.EntityFramework.Tests
git commit -m "Add UPPER_SNAKE casing target and public NorseNameRewriters"
```

---

### Task 2: Thread the rewrite delegate through the naming-convention stack

**Files:**
- Modify: `src/Persistence.EntityFramework/NorseSnakeCaseNamingOptionsExtension.cs`
- Modify: `src/Persistence.EntityFramework/NorseSnakeCaseConventionSetPlugin.cs`
- Modify: `src/Persistence.EntityFramework/NorseSnakeCaseNamingConvention.cs`
- Modify: `src/Persistence.EntityFramework/NorseDbContextOptionsExtensions.cs`
- Test: `tests/Persistence.EntityFramework.Tests/NorseSnakeCaseNamingConventionTests.cs`, `tests/Persistence.EntityFramework.Tests/NorseDbContextOptionsExtensionsTests.cs`

**Interfaces:**
- Consumes: `NorseNameRewriters.LowerSnakeCase` / `.UpperSnakeCase` (Task 1).
- Produces: `public DbContextOptionsBuilder ApplyNorseConventions(Func<string, string> rewriteName, Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames = null)` — the rewrite function is now a required argument; no parameterless overload survives. Every constructor in the extension → plugin → convention chain gains a leading `Func<string, string> rewriteName` parameter.

- [ ] **Step 1: Write the failing tests**

In `NorseSnakeCaseNamingConventionTests.cs`, update every existing `ApplyNorseConventions(...)` / convention-construction call site to pass `NorseNameRewriters.LowerSnakeCase` as the first argument (existing assertions unchanged — lower snake_case is the same output as before). Add an upper-snake model test alongside the existing lower-snake table-name test, mirroring its arrange (same test context type, same options plumbing):

```csharp
[Fact]
void Applies_UPPER_SNAKE_naming_when_the_upper_rewriter_is_supplied()
{
	// Mirror the existing lower-snake test's context/options arrangement exactly,
	// swapping the rewriter argument:
	var options = new DbContextOptionsBuilder<TestContext>()
		.UseSqlite("Data Source=:memory:")
		.ApplyNorseConventions(NorseNameRewriters.UpperSnakeCase)
		.Options;
	using TestContext ctx = new(options);

	var entityType = ctx.Model.FindEntityType(typeof(TestEntity));
	entityType.ShouldNotBeNull();
	entityType.GetTableName().ShouldBe("TEST_ENTITY");
}
```

(Adapt `TestContext`/`TestEntity` names to the fixture types already present in that file — do not invent a parallel fixture.)

In `NorseDbContextOptionsExtensionsTests.cs`, update `ApplyNorseConventions()` call sites to `ApplyNorseConventions(NorseNameRewriters.LowerSnakeCase)`.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Persistence.EntityFramework.Tests/Persistence.EntityFramework.Tests.csproj`
Expected: FAIL — no `ApplyNorseConventions` overload takes a `Func<string, string>` (compile error).

- [ ] **Step 3: Implement**

1. `NorseSnakeCaseNamingConvention.cs`: add a leading `Func<string, string> rewriteName` constructor/primary-constructor parameter; replace **every** `SnakeCaseNameRewriter.RewriteName(x)` call (12 sites — container columns, table, PK, columns, default constraints, keys, FKs, indexes, complex/JSON properties) with `rewriteName(x)`, and the provider-hook line becomes `applyProviderSpecificRenames?.Invoke(entity, rewriteName);`. Update the class XML docs to say the rewrite function is supplied by the provider binding.
2. `NorseSnakeCaseConventionSetPlugin.cs`: add the same leading parameter, forward to the convention.
3. `NorseSnakeCaseNamingOptionsExtension.cs`: add the same leading parameter and a `RewriteName` property beside `ApplyProviderSpecificRenames`; forward both to the plugin in `ApplyServices`; incorporate the rewrite delegate into `GetServiceProviderHashCode()` (`HashCode.Combine(RewriteName, ApplyProviderSpecificRenames)`) and `ShouldUseSameServiceProvider` (compare both delegates with `Equals`).
4. `NorseDbContextOptionsExtensions.cs`: `ApplyNorseConventions` gains required leading `Func<string, string> rewriteName`, forwards both arguments into the options extension. Update its XML docs: the rewrite function is provider-binding data, not a realm choice.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/Persistence.EntityFramework.Tests/Persistence.EntityFramework.Tests.csproj`
Expected: PASS. Note: `src/Persistence.EntityFramework.PostgreSQL` and `.SqlServer` now FAIL to build (their extensions call the old signature) — that is expected until Tasks 5–6; scope this task's test run to the root test project only.

- [ ] **Step 5: Commit**

```bash
git add src/Persistence.EntityFramework tests/Persistence.EntityFramework.Tests
git commit -m "Thread the identifier rewrite delegate through the naming convention stack"
```

---

### Task 3: Binding contracts + shared options choreography + runtime registration

**Files:**
- Create: `src/Persistence.EntityFramework/INorseEfProvider.cs`
- Create: `src/Persistence.EntityFramework/INorseEfMigrationProvider.cs`
- Create: `src/Persistence.EntityFramework/NorseContextExtensions.cs`
- Modify: `src/Persistence.EntityFramework/NorseDbContextOptionsExtensions.cs` (add `ApplyNorseProviderOptions`)
- Modify: `src/Persistence.EntityFramework/Persistence.EntityFramework.csproj` (add `Microsoft.Extensions.Hosting.Abstractions`)
- Test: `tests/Persistence.EntityFramework.Tests/NorseContextExtensionsTests.cs` (new), `tests/Persistence.EntityFramework.Tests/FakeEfProvider.cs` (new)

**Interfaces:**
- Consumes: `ApplyNorseConventions(Func<string, string>, Action<IConventionEntityType, Func<string, string>>?)` and `ApplyNorseTrackingBehavior()` (existing/Task 2).
- Produces (exact, later tasks depend on all of these):
  - `public interface INorseEfProvider` — members below.
  - `public interface INorseEfMigrationProvider : INorseEfProvider;`
  - `public void ApplyNorseProviderOptions(INorseEfProvider provider, string connectionString, string? migrationsAssemblyName)` — extension on `DbContextOptionsBuilder`; **the single choreography** runtime, migration-host, and design-time all call.
  - `public IHostApplicationBuilder AddNorseContext<TContext>(INorseEfProvider provider, string connectionStringName) where TContext : DbContext, INorseDbContext` — extension on `IHostApplicationBuilder`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Persistence.EntityFramework.Tests/FakeEfProvider.cs` — a recording fake shared by this task's and later tasks' root tests. It uses Sqlite (already referenced by this test project) so `DbContextOptions` are provider-complete:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.Tests;

sealed class FakeEfProvider : INorseEfMigrationProvider
{
	public string? SeenConnectionString { get; private set; }
	public string? SeenMigrationsAssemblyName { get; private set; }
	public bool MigrationsAssemblySeen { get; private set; }
	public int EnrichCalls { get; private set; }

	public Func<string, string>? NameRewriter { get; init; }

	public Action<IConventionEntityType, Func<string, string>>? EntityRenameHook { get; init; }

	public void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString,
		string? migrationsAssemblyName)
	{
		SeenConnectionString = connectionString;
		SeenMigrationsAssemblyName = migrationsAssemblyName;
		MigrationsAssemblySeen = true;
		optionsBuilder.UseSqlite(connectionString);
	}

	public void Enrich<TContext>(IHostApplicationBuilder builder)
		where TContext : DbContext, INorseDbContext =>
		EnrichCalls++;

	public string DesignTimePlaceholderConnectionString(string databaseName) =>
		$"Data Source={databaseName}.design.db";
}
```

Create `tests/Persistence.EntityFramework.Tests/NorseContextExtensionsTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.Tests;

public sealed class NorseContextExtensionsTests
{
	static HostApplicationBuilder CreateBuilder(string? connectionString = "Data Source=:memory:")
	{
		var builder = Host.CreateApplicationBuilder();
		if (connectionString is not null)
			builder.Configuration.AddInMemoryCollection(
				new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = connectionString });
		return builder;
	}

	[Fact]
	void AddNorseContext_registers_TContext_pooled()
	{
		var builder = CreateBuilder();

		builder.AddNorseContext<TestContext>(new FakeEfProvider(), "test-db");

		var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();
		// AddDbContextPool registers TContext via a pool-leasing factory (ImplementationFactory set,
		// ImplementationType null) — the inverse of the non-pooled shape asserted in the
		// migration-context tests.
		descriptor.ImplementationFactory.ShouldNotBeNull();
		descriptor.ImplementationType.ShouldBeNull();
	}

	[Fact]
	void AddNorseContext_resolves_the_connection_string_and_passes_no_migrations_assembly()
	{
		var builder = CreateBuilder();
		FakeEfProvider provider = new();

		builder.AddNorseContext<TestContext>(provider, "test-db");
		using var host = builder.Build();
		_ = host.Services.GetRequiredService<DbContextOptions<TestContext>>();

		provider.SeenConnectionString.ShouldBe("Data Source=:memory:");
		provider.MigrationsAssemblySeen.ShouldBeTrue();
		provider.SeenMigrationsAssemblyName.ShouldBeNull();
	}

	[Fact]
	void AddNorseContext_throws_loudly_when_the_connection_string_is_missing()
	{
		var builder = CreateBuilder(connectionString: null);

		var ex = Should.Throw<InvalidOperationException>(() =>
			builder.AddNorseContext<TestContext>(new FakeEfProvider(), "test-db"));
		ex.Message.ShouldContain("test-db");
	}

	[Fact]
	void AddNorseContext_applies_the_no_tracking_law_unconditionally()
	{
		var builder = CreateBuilder();

		builder.AddNorseContext<TestContext>(new FakeEfProvider(), "test-db");
		using var host = builder.Build();
		var options = host.Services.GetRequiredService<DbContextOptions<TestContext>>();

		options.GetExtension<CoreOptionsExtension>()
			.QueryTrackingBehavior.ShouldBe(QueryTrackingBehavior.NoTracking);
	}

	[Fact]
	void AddNorseContext_applies_naming_only_when_the_binding_supplies_a_rewriter()
	{
		var withRewriter = CreateBuilder();
		withRewriter.AddNorseContext<TestContext>(
			new FakeEfProvider { NameRewriter = NorseNameRewriters.LowerSnakeCase }, "test-db");
		using var host1 = withRewriter.Build();
		host1.Services.GetRequiredService<DbContextOptions<TestContext>>()
			.FindExtension<NorseSnakeCaseNamingOptionsExtension>().ShouldNotBeNull();

		var withoutRewriter = CreateBuilder();
		withoutRewriter.AddNorseContext<TestContext>(new FakeEfProvider(), "test-db");
		using var host2 = withoutRewriter.Build();
		host2.Services.GetRequiredService<DbContextOptions<TestContext>>()
			.FindExtension<NorseSnakeCaseNamingOptionsExtension>().ShouldBeNull();
	}

	[Fact]
	void AddNorseContext_enriches_through_the_binding()
	{
		var builder = CreateBuilder();
		FakeEfProvider provider = new();

		builder.AddNorseContext<TestContext>(provider, "test-db");

		provider.EnrichCalls.ShouldBe(1);
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options);
}
```

(If `NorseDbContextTests.cs` already defines a reusable `TestContext` fixture visible to this file, use it instead of the nested one — check before adding.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Persistence.EntityFramework.Tests/Persistence.EntityFramework.Tests.csproj`
Expected: FAIL — `INorseEfProvider`, `AddNorseContext` do not exist (compile errors).

- [ ] **Step 3: Implement**

Add to `Persistence.EntityFramework.csproj`'s ItemGroup (alphabetical among PackageReferences):

```xml
<PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="11.*-*" />
```

Create `src/Persistence.EntityFramework/INorseEfProvider.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// The provider binding — the single seam through which all provider knowledge enters the Norse EF
/// chassis. One sealed, stateless implementation ships per provider package
/// (<c>NorsePostgresEfProvider</c>, <c>NorseSqlServerEfProvider</c>, ...), exposed as a
/// <c>public static Instance</c> singleton by convention (the migrations generator enforces the
/// convention with a compile-time diagnostic). The neutral choreography
/// (<see cref="NorseDbContextOptionsExtensions.ApplyNorseProviderOptions"/>) is the only consumer;
/// realms never implement or invoke this contract directly. Everything here is derived from the
/// provider choice — naming, floors, placeholders — never configured per realm.
/// </summary>
public interface INorseEfProvider
{
	/// <summary>
	/// Applies the provider's <c>Use{Provider}</c> call, including any forced floors (SQL Server
	/// chains its compatibility-level floor here unconditionally). <paramref name="migrationsAssemblyName"/>
	/// is <see langword="null"/> on the pooled runtime path and supplied on migration and design-time
	/// paths — Norse convention places migrations in sibling assemblies EF cannot infer.
	/// </summary>
	/// <param name="optionsBuilder">The options builder to configure.</param>
	/// <param name="connectionString">The already-resolved connection string.</param>
	/// <param name="migrationsAssemblyName">The migrations assembly, when this registration runs migrations.</param>
	void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString,
		string? migrationsAssemblyName);

	/// <summary>
	/// Applies the provider's Aspire enrichment (retry policy, health check, telemetry) to an
	/// already-registered context. Generic because the underlying Aspire
	/// <c>Enrich{Provider}DbContext&lt;TContext&gt;</c> extensions are — which is also why this seam
	/// is a contract and not a delegate: open-generic delegates do not exist.
	/// </summary>
	/// <typeparam name="TContext">The registered context type.</typeparam>
	/// <param name="builder">The host application builder.</param>
	void Enrich<TContext>(IHostApplicationBuilder builder)
		where TContext : DbContext, INorseDbContext;

	/// <summary>
	/// The engine-native identifier rewriter (<see cref="NorseNameRewriters"/>), or
	/// <see langword="null"/> to keep EF's raw names. Binding data, not a realm lever — Postgres
	/// supplies lower snake_case, SQL Server supplies none.
	/// </summary>
	Func<string, string>? NameRewriter { get; }

	/// <summary>
	/// Optional provider-specific per-entity rename hook, invoked by the naming convention alongside
	/// its own renames (SQL Server's temporal history-table rename). Only meaningful when
	/// <see cref="NameRewriter"/> is non-null — the choreography never applies the naming convention
	/// without a rewriter, so a hook paired with a null rewriter is inert by construction.
	/// </summary>
	Action<IConventionEntityType, Func<string, string>>? EntityRenameHook { get; }

	/// <summary>
	/// A syntactically valid, semantically inert connection string for offline design-time model
	/// building (<c>dotnet ef migrations add</c>/<c>remove</c> never open a connection). Points at
	/// nothing; design tooling must never dial infrastructure.
	/// </summary>
	/// <param name="databaseName">The realm's database name, e.g. <c>norse_reference</c>.</param>
	/// <returns>The placeholder connection string.</returns>
	string DesignTimePlaceholderConnectionString(string databaseName);
}
```

Create `src/Persistence.EntityFramework/INorseEfMigrationProvider.cs`:

```csharp
namespace Norse.Persistence.EntityFramework;

/// <summary>
/// The migration-host half of the provider seam. Production-tier providers (Postgres, SQL Server,
/// eventually Oracle) implement this; a local-dev-only provider (SQLite) implements only
/// <see cref="INorseEfProvider"/>, making a SQLite migrations host a compile error rather than a
/// runtime refusal — the tier split is enforced by the type system, not a flag.
/// </summary>
public interface INorseEfMigrationProvider : INorseEfProvider;
```

Add to the existing `extension(DbContextOptionsBuilder optionsBuilder)` block in `NorseDbContextOptionsExtensions.cs`:

```csharp
/// <summary>
/// The single provider-options choreography: the binding's <see cref="INorseEfProvider.Configure"/>
/// call, the unconditional platform no-tracking law, and binding-derived naming. Consumed by the
/// runtime registration (<c>AddNorseContext</c>), the migration-host registration
/// (<c>AddNorseMigrationContext</c>), and the design-time factory — one copy, three consumers, so
/// runtime/design-time drift is unrepresentable.
/// </summary>
/// <param name="provider">The provider binding.</param>
/// <param name="connectionString">The already-resolved (or design-time placeholder) connection string.</param>
/// <param name="migrationsAssemblyName">The migrations assembly, when this registration runs migrations.</param>
/// <returns>The same <paramref name="optionsBuilder"/> for chaining.</returns>
public DbContextOptionsBuilder ApplyNorseProviderOptions(INorseEfProvider provider,
	string connectionString, string? migrationsAssemblyName)
{
	provider.Configure(optionsBuilder, connectionString, migrationsAssemblyName);
	optionsBuilder.ApplyNorseTrackingBehavior();
	if (provider.NameRewriter is not null)
		optionsBuilder.ApplyNorseConventions(provider.NameRewriter, provider.EntityRenameHook);
	return optionsBuilder;
}
```

Create `src/Persistence.EntityFramework/NorseContextExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Provider-neutral runtime registration for Norse EF contexts. The provider binding supplies every
/// provider-varying fact; the remaining levers on a registration are exactly two — the connection
/// string name and the context type.
/// </summary>
public static class NorseContextExtensions
{
	/// <param name="builder">The host application builder.</param>
	extension(IHostApplicationBuilder builder)
	{
		/// <summary>
		/// Registers <typeparamref name="TContext"/> pooled, with the binding's provider
		/// configuration, the platform no-tracking law, binding-derived naming, and the binding's
		/// Aspire enrichment (retry, health check, telemetry). Pooling uses EF's own
		/// <c>AddDbContextPool</c> + the provider's <c>Enrich</c> — Aspire's documented equivalent
		/// of its <c>Add{Provider}DbContext</c> sugar, keeping the <c>Aspire:*</c> settings sections
		/// in force.
		/// </summary>
		/// <typeparam name="TContext">
		/// The <see cref="DbContext"/> type to register. Must implement <see cref="INorseDbContext"/>.
		/// </typeparam>
		/// <param name="provider">The provider binding.</param>
		/// <param name="connectionStringName">The connection string name in application configuration.</param>
		/// <returns>The same <paramref name="builder"/> for chaining.</returns>
		public IHostApplicationBuilder AddNorseContext<TContext>(INorseEfProvider provider,
			string connectionStringName)
			where TContext : DbContext, INorseDbContext
		{
			var connectionString = builder.Configuration.GetConnectionString(connectionStringName) ??
				throw new InvalidOperationException(
					$"Connection string '{connectionStringName}' was not found.");

			builder.Services.AddDbContextPool<TContext>(opts =>
				opts.ApplyNorseProviderOptions(provider, connectionString, migrationsAssemblyName: null));
			provider.Enrich<TContext>(builder);

			return builder;
		}
	}
}
```

Note: `NorseSnakeCaseNamingOptionsExtension` is internal; the naming-presence test reaches it via the existing `InternalsVisibleTo` grant — do not widen it.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/Persistence.EntityFramework.Tests/Persistence.EntityFramework.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Persistence.EntityFramework tests/Persistence.EntityFramework.Tests
git commit -m "Add INorseEfProvider seam, shared options choreography, and AddNorseContext"
```

---

### Task 4: New `.Migrations` package — contributor, attribute, migration-host choreography

**Files:**
- Create: `src/Persistence.EntityFramework.Migrations/Persistence.EntityFramework.Migrations.csproj`
- Move: `src/Persistence.EntityFramework.Design/EfMigrationContributor.cs` → `src/Persistence.EntityFramework.Migrations/EfMigrationContributor.cs`
- Move: `src/Persistence.EntityFramework.Design/MigrationConnectionStringAttribute.cs` → `src/Persistence.EntityFramework.Migrations/MigrationConnectionStringAttribute.cs`
- Create: `src/Persistence.EntityFramework.Migrations/NorseMigrationContextExtensions.cs`
- Modify: `src/Persistence.EntityFramework.Design/Persistence.EntityFramework.Design.csproj` (drop the now-unused `Abstractions.Migrations` NorseRef)
- Create: `tests/Persistence.EntityFramework.Migrations.Tests/Persistence.EntityFramework.Migrations.Tests.csproj`
- Move: `tests/Persistence.EntityFramework.Design.Tests/EfMigrationContributorTests.cs` → `tests/Persistence.EntityFramework.Migrations.Tests/EfMigrationContributorTests.cs`
- Create: `tests/Persistence.EntityFramework.Migrations.Tests/NorseMigrationContextExtensionsTests.cs`
- Create: `tests/Persistence.EntityFramework.Migrations.Tests/FakeEfProvider.cs` (copy of the Task 3 fake — test projects do not share source)
- Modify: `Urdarbrunnr.slnx` (add both projects to their folders)

**Interfaces:**
- Consumes: `INorseEfMigrationProvider`, `ApplyNorseProviderOptions(INorseEfProvider, string, string?)` (Task 3); `Norse.Abstractions.Migrations.IMigrationContributor` (Asgard, unchanged).
- Produces: namespace **`Norse.Persistence.EntityFramework.Migrations`** now owns `EfMigrationContributor<TContext>` and `MigrationConnectionStringAttribute` (bodies unchanged, namespace changed — the generator's metadata-name constants update in Task 8); `public IHostApplicationBuilder AddNorseMigrationContext<TContext>(INorseEfMigrationProvider provider, string connectionStringName, string migrationsAssemblyName) where TContext : DbContext, INorseDbContext`.

- [ ] **Step 1: Create the project and move the types**

`src/Persistence.EntityFramework.Migrations/Persistence.EntityFramework.Migrations.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Persistence.EntityFramework.Migrations: EfMigrationContributor&lt;TContext&gt;, MigrationConnectionStringAttribute, and the provider-neutral AddNorseMigrationContext migration-host choreography. Ships the provider-agnostic Roslyn generator that discovers contributors, seed contributors, and the single provider binding in the compilation and emits AddNorseMigrations(). Reference this one package (Generator="true") from your migrations service.</Description>
		<!-- EF Core is not AOT/trim-compatible; override the src-level default. -->
		<IsAotCompatible>false</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<NorseRef Include="Abstractions.Migrations">
			<Repo>Asgard</Repo>
		</NorseRef>
		<ProjectReference Include="../Persistence.EntityFramework/Persistence.EntityFramework.csproj" />
	</ItemGroup>
</Project>
```

(The generator Analyzer reference and packing target land in Task 8.)

`git mv` both source files; change their namespace declarations from `Norse.Persistence.EntityFramework.Design` to `Norse.Persistence.EntityFramework.Migrations`. Bodies stay byte-identical otherwise. Remove the `Abstractions.Migrations` NorseRef from `Persistence.EntityFramework.Design.csproj` (nothing left in `.Design` uses it). Add both new projects to `Urdarbrunnr.slnx` under `/src/` and `/tests/` following the existing entry format.

- [ ] **Step 2: Write the failing tests**

`tests/Persistence.EntityFramework.Migrations.Tests/Persistence.EntityFramework.Migrations.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="11.*-*" />
		<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="11.*-*" />
		<ProjectReference Include="../../src/Persistence.EntityFramework.Migrations/Persistence.EntityFramework.Migrations.csproj" />
	</ItemGroup>
</Project>
```

(Drop `InMemory` if the moved contributor tests compile without it.) Move `EfMigrationContributorTests.cs` over (`git mv`), updating its namespace and `using Norse.Persistence.EntityFramework.Design;` → `...Migrations;`. Copy `FakeEfProvider.cs` from Task 3 (namespace `Norse.Persistence.EntityFramework.Migrations.Tests`). Create `NorseMigrationContextExtensionsTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.Migrations.Tests;

public sealed class NorseMigrationContextExtensionsTests
{
	static HostApplicationBuilder CreateBuilder()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = "Data Source=:memory:" });
		return builder;
	}

	[Fact]
	void AddNorseMigrationContext_registers_TContext_non_pooled()
	{
		var builder = CreateBuilder();

		builder.AddNorseMigrationContext<TestContext>(new FakeEfProvider(), "test-db",
			"Test.Migrations.Assembly");

		var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();
		// AddDbContext registers TContext type-to-type (ImplementationType set, no factory) — the
		// inverse of the pooled shape; a migrations service constructs its context once and exits,
		// and EF forbids OnConfiguring mutating frozen pooled options.
		descriptor.ImplementationType.ShouldBe(typeof(TestContext));
		descriptor.ImplementationFactory.ShouldBeNull();
	}

	[Fact]
	void AddNorseMigrationContext_forwards_the_migrations_assembly_to_the_binding()
	{
		var builder = CreateBuilder();
		FakeEfProvider provider = new();

		builder.AddNorseMigrationContext<TestContext>(provider, "test-db", "Test.Migrations.Assembly");
		using var host = builder.Build();
		_ = host.Services.GetRequiredService<DbContextOptions<TestContext>>();

		provider.SeenConnectionString.ShouldBe("Data Source=:memory:");
		provider.SeenMigrationsAssemblyName.ShouldBe("Test.Migrations.Assembly");
		provider.EnrichCalls.ShouldBe(1);
	}

	[Fact]
	void AddNorseMigrationContext_throws_loudly_when_the_connection_string_is_missing()
	{
		var builder = Host.CreateApplicationBuilder();

		var ex = Should.Throw<InvalidOperationException>(() =>
			builder.AddNorseMigrationContext<TestContext>(new FakeEfProvider(), "absent-db", "X"));
		ex.Message.ShouldContain("absent-db");
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options);
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Persistence.EntityFramework.Migrations.Tests/Persistence.EntityFramework.Migrations.Tests.csproj`
Expected: FAIL — `AddNorseMigrationContext` does not exist.

- [ ] **Step 4: Implement**

Create `src/Persistence.EntityFramework.Migrations/NorseMigrationContextExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.Migrations;

/// <summary>
/// Provider-neutral migration-host registration. Constrained on
/// <see cref="INorseEfMigrationProvider"/> — the migration-host half of the provider seam — so a
/// local-dev-only provider (SQLite) cannot be pointed at a migrations host at all: the call does
/// not compile.
/// </summary>
public static class NorseMigrationContextExtensions
{
	/// <param name="builder">The host application builder.</param>
	extension(IHostApplicationBuilder builder)
	{
		/// <summary>
		/// Registers <typeparamref name="TContext"/> for one-shot, non-pooled use (migrations and
		/// other short-lived init-container work). Not pooled: a migrations service constructs its
		/// context once and exits, and EF Core forbids <c>OnConfiguring</c> mutating frozen pooled
		/// options — pooling is pure risk here. Still enriched via the binding (retry, health
		/// check, telemetry).
		/// </summary>
		/// <typeparam name="TContext">
		/// The <see cref="DbContext"/> type to register. Must implement <see cref="INorseDbContext"/>.
		/// </typeparam>
		/// <param name="provider">The provider binding (migration-capable tier).</param>
		/// <param name="connectionStringName">The connection string name in application configuration.</param>
		/// <param name="migrationsAssemblyName">
		/// The sibling assembly containing <typeparamref name="TContext"/>'s EF migrations — always
		/// supplied explicitly; EF's default of searching the context's own assembly finds nothing
		/// by Norse convention.
		/// </param>
		/// <returns>The same <paramref name="builder"/> for chaining.</returns>
		public IHostApplicationBuilder AddNorseMigrationContext<TContext>(
			INorseEfMigrationProvider provider, string connectionStringName,
			string migrationsAssemblyName)
			where TContext : DbContext, INorseDbContext
		{
			var connectionString = builder.Configuration.GetConnectionString(connectionStringName) ??
				throw new InvalidOperationException(
					$"Connection string '{connectionStringName}' was not found.");

			builder.Services.AddDbContext<TContext>(opts =>
				opts.ApplyNorseProviderOptions(provider, connectionString, migrationsAssemblyName));
			provider.Enrich<TContext>(builder);

			return builder;
		}
	}
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/Persistence.EntityFramework.Migrations.Tests/Persistence.EntityFramework.Migrations.Tests.csproj` and `dotnet test tests/Persistence.EntityFramework.Design.Tests/Persistence.EntityFramework.Design.Tests.csproj`
Expected: both PASS (Design.Tests lost the moved file and still builds).

- [ ] **Step 6: Commit**

```bash
git add -A src tests Urdarbrunnr.slnx
git commit -m "Add Persistence.EntityFramework.Migrations with contributor, attribute, and migration-host choreography"
```

---

### Task 5: Postgres binding (and the Aspire-equivalence verification gate)

**Files:**
- Delete: `src/Persistence.EntityFramework.PostgreSQL/NorsePostgresContextExtensions.cs`
- Create: `src/Persistence.EntityFramework.PostgreSQL/NorsePostgresEfProvider.cs`
- Modify: `src/Persistence.EntityFramework.PostgreSQL/Persistence.EntityFramework.PostgreSQL.csproj` (Description only — reference set unchanged)
- Rewrite: `tests/Persistence.EntityFramework.PostgreSQL.Tests/NorsePostgresContextExtensionsTests.cs` → `tests/Persistence.EntityFramework.PostgreSQL.Tests/NorsePostgresEfProviderTests.cs`
- Modify: `tests/Persistence.EntityFramework.PostgreSQL.Tests/Persistence.EntityFramework.PostgreSQL.Tests.csproj` (add `.Migrations` ProjectReference)

**Interfaces:**
- Consumes: `INorseEfMigrationProvider`, `NorseNameRewriters.LowerSnakeCase`, `AddNorseContext<TContext>(INorseEfProvider, string)` (Task 3), `AddNorseMigrationContext<TContext>(INorseEfMigrationProvider, string, string)` (Task 4); Aspire's `EnrichNpgsqlDbContext<TContext>` and (test-only) `AddNpgsqlDbContext<TContext>`.
- Produces: `public sealed class NorsePostgresEfProvider : INorseEfMigrationProvider` with `public static NorsePostgresEfProvider Instance { get; }` — the exact type the generator discovers and emits in Task 8.

- [ ] **Step 1: Write the failing tests**

Replace the old test file with `NorsePostgresEfProviderTests.cs`. **The first test is the spec's first verification gate** (spec §5): prove `AddDbContextPool` + `EnrichNpgsqlDbContext` covers Aspire's `AddNpgsqlDbContext` registrations.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

public sealed class NorsePostgresEfProviderTests
{
	const string ConnectionString = "Host=localhost;Database=test";

	static HostApplicationBuilder CreateBuilder()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });
		return builder;
	}

	[Fact]
	void AddNorseContext_covers_every_service_Aspires_AddNpgsqlDbContext_registers()
	{
		// THE ASPIRE-EQUIVALENCE GATE (spec §5): the design drops Aspire's Add{P}DbContext sugar on
		// the claim that AddDbContextPool + Enrich{P}DbContext is its documented equivalent. This
		// test holds that claim to account. If it fails, DO NOT loosen the assertion silently:
		// investigate each missing ServiceType; a difference may only be excluded here with a
		// written justification comment naming the type and why it is not load-bearing. If a
		// difference IS load-bearing (pooling or instrumentation cannot be reconstructed), HALT
		// the plan and surface it — that is a design-level finding, not an implementation detail.
		// Scope note: this compares registered ServiceTypes only, NOT Aspire's connection-string
		// precedence semantics — the AppHost live run in the adoption sweep owns that question,
		// and a green gate here does not close it.
		var aspire = CreateBuilder();
		aspire.AddNpgsqlDbContext<TestContext>("test-db");

		var norse = CreateBuilder();
		norse.AddNorseContext<TestContext>(NorsePostgresEfProvider.Instance, "test-db");

		var aspireTypes = aspire.Services.Select(d => d.ServiceType).ToHashSet();
		var norseTypes = norse.Services.Select(d => d.ServiceType).ToHashSet();
		aspireTypes.Except(norseTypes).ShouldBeEmpty();
	}

	[Fact]
	void AddNorseContext_registers_TContext_pooled()
	{
		var builder = CreateBuilder();

		builder.AddNorseContext<TestContext>(NorsePostgresEfProvider.Instance, "test-db");

		var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();
		descriptor.ImplementationFactory.ShouldNotBeNull();
	}

	[Fact]
	void AddNorseMigrationContext_registers_TContext_non_pooled_and_does_not_throw_building_the_model()
	{
		var builder = CreateBuilder();

		builder.AddNorseMigrationContext<TestContext>(NorsePostgresEfProvider.Instance, "test-db",
			"Norse.Persistence.EntityFramework.PostgreSQL.Tests");

		var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();
		descriptor.ImplementationType.ShouldBe(typeof(TestContext));
		descriptor.ImplementationFactory.ShouldBeNull();

		using var host = builder.Build();
		using var scope = host.Services.CreateScope();
		using var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();
		Should.NotThrow(() => _ = ctx.Model);
	}

	[Fact]
	void Binding_supplies_lower_snake_naming_as_postgres_engine_native_style()
	{
		NorsePostgresEfProvider.Instance.NameRewriter.ShouldNotBeNull();
		NorsePostgresEfProvider.Instance.NameRewriter("CountryOrArea").ShouldBe("country_or_area");
		NorsePostgresEfProvider.Instance.EntityRenameHook.ShouldBeNull();
	}

	[Fact]
	void Design_time_placeholder_parses_but_points_at_nothing()
	{
		var placeholder = NorsePostgresEfProvider.Instance
			.DesignTimePlaceholderConnectionString("norse_reference");

		NpgsqlConnectionStringBuilder parsed = new(placeholder);
		parsed.Database.ShouldBe("norse_reference");
		parsed.Host.ShouldBe("design");
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options);
}
```

(Port any entity-fixture types the old test file defined and still needed; delete the rest with the file.) Add to the test csproj's ItemGroup:

```xml
<ProjectReference Include="../../src/Persistence.EntityFramework.Migrations/Persistence.EntityFramework.Migrations.csproj" />
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Persistence.EntityFramework.PostgreSQL.Tests/Persistence.EntityFramework.PostgreSQL.Tests.csproj`
Expected: FAIL — `NorsePostgresEfProvider` does not exist (and the src project itself still fails to build from Task 2's signature change).

- [ ] **Step 3: Implement**

Delete `NorsePostgresContextExtensions.cs`. Create `NorsePostgresEfProvider.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.PostgreSQL;

/// <summary>
/// The PostgreSQL provider binding. Stateless; consume via <see cref="Instance"/>. Postgres folds
/// unquoted identifiers to lowercase, so lower snake_case is the engine's own escape-free native
/// style — supplied here as binding data, not a realm lever. No forced floors today.
/// </summary>
public sealed class NorsePostgresEfProvider : INorseEfMigrationProvider
{
	static readonly Func<string, string> _lowerSnakeCase = NorseNameRewriters.LowerSnakeCase;

	NorsePostgresEfProvider()
	{
	}

	/// <summary>The well-known singleton — the "enum value" for this provider.</summary>
	public static NorsePostgresEfProvider Instance { get; } = new();

	/// <inheritdoc />
	public void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString,
		string? migrationsAssemblyName) =>
		optionsBuilder.UseNpgsql(connectionString, npgsql =>
		{
			if (migrationsAssemblyName is not null)
				npgsql.MigrationsAssembly(migrationsAssemblyName);
		});

	/// <inheritdoc />
	public void Enrich<TContext>(IHostApplicationBuilder builder)
		where TContext : DbContext, INorseDbContext =>
		builder.EnrichNpgsqlDbContext<TContext>();

	/// <inheritdoc />
	public Func<string, string>? NameRewriter => _lowerSnakeCase;

	/// <inheritdoc />
	public Action<IConventionEntityType, Func<string, string>>? EntityRenameHook => null;

	/// <inheritdoc />
	public string DesignTimePlaceholderConnectionString(string databaseName) =>
		$"Host=design;Database={databaseName};Username=design;Password=design";
}
```

Update the csproj `Description` to: `Norse.Persistence.EntityFramework.PostgreSQL: the PostgreSQL provider binding (NorsePostgresEfProvider) — UseNpgsql wiring, Aspire enrichment, engine-native lower snake_case naming, and the inert design-time placeholder. One sealed class; reference from any host or migrations project targeting Postgres.`

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/Persistence.EntityFramework.PostgreSQL.Tests/Persistence.EntityFramework.PostgreSQL.Tests.csproj`
Expected: PASS — **including the equivalence gate.** If the gate fails, follow its comment's protocol before anything else.

- [ ] **Step 5: Commit**

```bash
git add -A src/Persistence.EntityFramework.PostgreSQL tests/Persistence.EntityFramework.PostgreSQL.Tests
git commit -m "Replace Postgres registration extensions with the NorsePostgresEfProvider binding"
```

---

### Task 6: SQL Server binding

**Files:**
- Delete: `src/Persistence.EntityFramework.SqlServer/NorseSqlServerContextExtensions.cs`
- Create: `src/Persistence.EntityFramework.SqlServer/NorseSqlServerEfProvider.cs`
- Modify: `src/Persistence.EntityFramework.SqlServer/Persistence.EntityFramework.SqlServer.csproj` (Description only)
- Rewrite: `tests/Persistence.EntityFramework.SqlServer.Tests/NorseSqlServerContextExtensionsTests.cs` → `tests/Persistence.EntityFramework.SqlServer.Tests/NorseSqlServerEfProviderTests.cs`
- Modify: `tests/Persistence.EntityFramework.SqlServer.Tests/Persistence.EntityFramework.SqlServer.Tests.csproj` (add `.Migrations` ProjectReference)

**Interfaces:**
- Consumes: same as Task 5, plus SQL-Server-only EF APIs (`IsTemporal`, `GetHistoryTableName`, `SetHistoryTableName`, `UseCompatibilityLevel`) — this project remains the only one on the platform allowed to reference them.
- Produces: `public sealed class NorseSqlServerEfProvider : INorseEfMigrationProvider` with `public static NorseSqlServerEfProvider Instance { get; }`.

- [ ] **Step 1: Write the failing tests**

`NorseSqlServerEfProviderTests.cs`, mirroring Task 5's shape with SQL Server specifics:

```csharp
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.SqlServer.Tests;

public sealed class NorseSqlServerEfProviderTests
{
	const string ConnectionString =
		"Server=localhost;Database=test;Trusted_Connection=True;TrustServerCertificate=True;";

	static HostApplicationBuilder CreateBuilder()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });
		return builder;
	}

	[Fact]
	void AddNorseContext_covers_every_service_Aspires_AddSqlServerDbContext_registers()
	{
		// Same Aspire-equivalence gate and same failure protocol as the Postgres binding test —
		// investigate, justify exclusions in writing, halt on a load-bearing difference.
		var aspire = CreateBuilder();
		aspire.AddSqlServerDbContext<TestContext>("test-db");

		var norse = CreateBuilder();
		norse.AddNorseContext<TestContext>(NorseSqlServerEfProvider.Instance, "test-db");

		var aspireTypes = aspire.Services.Select(d => d.ServiceType).ToHashSet();
		var norseTypes = norse.Services.Select(d => d.ServiceType).ToHashSet();
		aspireTypes.Except(norseTypes).ShouldBeEmpty();
	}

	[Fact]
	void Configure_forces_the_2025_compatibility_level_floor_unconditionally()
	{
		DbContextOptionsBuilder<TestContext> optionsBuilder = new();

		NorseSqlServerEfProvider.Instance.Configure(optionsBuilder, ConnectionString,
			migrationsAssemblyName: null);

		// EF1001 (internal EF API): SqlServerOptionsExtension is the only observable carrier of
		// UseCompatibilityLevel's value; asserting the platform floor is exactly what this test
		// exists for, and the alternative (generating SQL against a live server) violates the
		// no-database law. Wrong-in-context, hence inline per the Suppression Law.
#pragma warning disable EF1001
		var extension = optionsBuilder.Options.FindExtension<SqlServerOptionsExtension>();
		extension.ShouldNotBeNull();
		extension.CompatibilityLevel.ShouldBe(170);
#pragma warning restore EF1001
	}

	[Fact]
	void Configure_forwards_the_migrations_assembly_when_supplied()
	{
		DbContextOptionsBuilder<TestContext> optionsBuilder = new();

		NorseSqlServerEfProvider.Instance.Configure(optionsBuilder, ConnectionString,
			"Test.Migrations.Assembly");

#pragma warning disable EF1001 // same justification as above
		var extension = optionsBuilder.Options.FindExtension<SqlServerOptionsExtension>();
		extension.ShouldNotBeNull();
		extension.MigrationsAssembly.ShouldBe("Test.Migrations.Assembly");
#pragma warning restore EF1001
	}

	[Fact]
	void Binding_keeps_engine_native_PascalCase_but_pairs_the_temporal_hook_for_a_naming_binding()
	{
		// SQL Server's case-insensitive collation round-trips raw PascalCase without quoting, so no
		// rewriter — and with no rewriter the choreography never applies the naming convention, so
		// the paired hook is inert by construction. It stays wired so any future binding variant
		// that enables renaming on SQL Server inherits the history-table rename instead of
		// rediscovering the drift bug the old design-time factory had.
		NorseSqlServerEfProvider.Instance.NameRewriter.ShouldBeNull();
		NorseSqlServerEfProvider.Instance.EntityRenameHook.ShouldNotBeNull();
	}

	[Fact]
	void Design_time_placeholder_parses_but_points_at_nothing()
	{
		var placeholder = NorseSqlServerEfProvider.Instance
			.DesignTimePlaceholderConnectionString("norse_identity");

		SqlConnectionStringBuilder parsed = new(placeholder);
		parsed.InitialCatalog.ShouldBe("norse_identity");
		parsed.DataSource.ShouldBe("design");
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options);
}
```

Port any temporal-entity fixture the old file had (it exercises `RenameTemporalHistoryTable`); keep its model-level assertion by invoking `NorseSqlServerEfProvider.Instance.EntityRenameHook` through `ApplyNorseConventions(NorseNameRewriters.LowerSnakeCase, hook)` on an options builder exactly as the old test arranged it. Add the `.Migrations` ProjectReference to the test csproj (same line as Task 5).

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Persistence.EntityFramework.SqlServer.Tests/Persistence.EntityFramework.SqlServer.Tests.csproj`
Expected: FAIL — `NorseSqlServerEfProvider` does not exist.

- [ ] **Step 3: Implement**

Delete `NorseSqlServerContextExtensions.cs`. Create `NorseSqlServerEfProvider.cs` — carry the existing `SqlServerCompatibilityLevel` const and `RenameTemporalHistoryTable` method over verbatim with their current XML docs (trim the doc sentence referencing the deleted design-time factory):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.SqlServer;

/// <summary>
/// The SQL Server provider binding. Stateless; consume via <see cref="Instance"/>. SQL Server's
/// default collation is case-insensitive, so raw PascalCase is already the engine-native,
/// escape-free style — no rewriter. The 2025 compatibility-level floor is forced unconditionally
/// in <see cref="Configure"/>; it is a floor, not a lever.
/// </summary>
public sealed class NorseSqlServerEfProvider : INorseEfMigrationProvider
{
	// [carry over the existing SqlServerCompatibilityLevel const + full XML doc here]
	const int SqlServerCompatibilityLevel = 170;

	NorseSqlServerEfProvider()
	{
	}

	/// <summary>The well-known singleton — the "enum value" for this provider.</summary>
	public static NorseSqlServerEfProvider Instance { get; } = new();

	/// <inheritdoc />
	public void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString,
		string? migrationsAssemblyName) =>
		optionsBuilder.UseSqlServer(connectionString, sql =>
		{
			sql.UseCompatibilityLevel(SqlServerCompatibilityLevel);
			if (migrationsAssemblyName is not null)
				sql.MigrationsAssembly(migrationsAssemblyName);
		});

	/// <inheritdoc />
	public void Enrich<TContext>(IHostApplicationBuilder builder)
		where TContext : DbContext, INorseDbContext =>
		builder.EnrichSqlServerDbContext<TContext>();

	/// <inheritdoc />
	public Func<string, string>? NameRewriter => null;

	/// <inheritdoc />
	public Action<IConventionEntityType, Func<string, string>>? EntityRenameHook =>
		RenameTemporalHistoryTable;

	/// <inheritdoc />
	public string DesignTimePlaceholderConnectionString(string databaseName) =>
		$"Server=design;Database={databaseName};User Id=design;Password=design;TrustServerCertificate=true";

	// [carry over the existing RenameTemporalHistoryTable method + full XML doc here]
	static void RenameTemporalHistoryTable(IConventionEntityType entity, Func<string, string> rewrite)
	{
		if (!entity.IsTemporal())
			return;

		var historyTableName = entity.GetHistoryTableName();
		if (!string.IsNullOrWhiteSpace(historyTableName))
			entity.SetHistoryTableName(rewrite(historyTableName));
	}
}
```

Update the csproj `Description` analogously to Task 5's.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/Persistence.EntityFramework.SqlServer.Tests/Persistence.EntityFramework.SqlServer.Tests.csproj`
Expected: PASS, equivalence gate included.

- [ ] **Step 5: Commit**

```bash
git add -A src/Persistence.EntityFramework.SqlServer tests/Persistence.EntityFramework.SqlServer.Tests
git commit -m "Replace SQL Server registration extensions with the NorseSqlServerEfProvider binding"
```

---

### Task 7: Neutral design-time factory

**Files:**
- Create: `src/Persistence.EntityFramework.Design/NorseDesignTimeDbContextFactory.cs`
- Modify: `src/Persistence.EntityFramework.Design/Persistence.EntityFramework.Design.csproj` (Description)
- Test: `tests/Persistence.EntityFramework.Design.Tests/NorseDesignTimeDbContextFactoryTests.cs` (new), `tests/Persistence.EntityFramework.Design.Tests/FakeEfProvider.cs` (copy of Task 3's fake, namespace adjusted)

**Interfaces:**
- Consumes: `INorseEfProvider`, `ApplyNorseProviderOptions` (Task 3).
- Produces: `public abstract class NorseDesignTimeDbContextFactory<TContext> : IDesignTimeDbContextFactory<TContext> where TContext : DbContext, INorseDbContext` with `protected abstract INorseEfProvider Provider { get; }`, `protected abstract string DatabaseName { get; }`, `protected abstract TContext CreateContext(DbContextOptions<TContext> options)`, `protected virtual void ConfigureOptions(DbContextOptionsBuilder<TContext> builder)`. **No environment-variable escape hatch** — `DOTNET_EFTOOLS_CONNECTIONSTRING` is dead (spec §8).

- [ ] **Step 1: Write the failing tests**

Copy `FakeEfProvider.cs` into the Design tests project (namespace `Norse.Persistence.EntityFramework.Design.Tests`). Create `NorseDesignTimeDbContextFactoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.Persistence.EntityFramework.Design.Tests;

public sealed class NorseDesignTimeDbContextFactoryTests
{
	[Fact]
	void Factory_builds_the_context_from_the_bindings_inert_placeholder()
	{
		TestFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		factory.Provider.SeenConnectionString.ShouldBe("Data Source=norse_test.design.db");
	}

	[Fact]
	void Factory_supplies_its_own_assembly_as_the_migrations_assembly()
	{
		TestFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		factory.Provider.SeenMigrationsAssemblyName
			.ShouldBe(typeof(TestFactory).Assembly.GetName().Name);
	}

	[Fact]
	void ConfigureOptions_is_an_override_point_that_can_layer_on_top_of_the_base_wiring()
	{
		OverridingFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		factory.OverrideRan.ShouldBeTrue();
		factory.Provider.SeenConnectionString.ShouldBe("Data Source=norse_test.design.db");
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options);

	abstract class TestFactoryBase : NorseDesignTimeDbContextFactory<TestContext>
	{
		public FakeEfProvider Provider { get; } = new();

		protected override INorseEfProvider ProviderBinding => Provider;

		protected override string DatabaseName => "norse_test";

		protected override TestContext CreateContext(DbContextOptions<TestContext> options) =>
			new(options);
	}

	sealed class TestFactory : TestFactoryBase;

	sealed class OverridingFactory : TestFactoryBase
	{
		public bool OverrideRan { get; private set; }

		protected override void ConfigureOptions(DbContextOptionsBuilder<TestContext> builder)
		{
			base.ConfigureOptions(builder);
			OverrideRan = true;
		}
	}
}
```

**Naming note:** the abstract provider property is `ProviderBinding` (not `Provider`) so concrete factories can expose their own members without collision and the name says what it is. Use `ProviderBinding` consistently — this is the name realm factories override in the adoption sweep.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Persistence.EntityFramework.Design.Tests/Persistence.EntityFramework.Design.Tests.csproj`
Expected: FAIL — `NorseDesignTimeDbContextFactory` does not exist.

- [ ] **Step 3: Implement**

Create `src/Persistence.EntityFramework.Design/NorseDesignTimeDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Norse.Persistence.EntityFramework.Design;

/// <summary>
/// The one provider-neutral <see cref="IDesignTimeDbContextFactory{TContext}"/> base for Norse
/// contexts, used only by <c>dotnet ef</c> tooling. Consumes the same
/// <c>ApplyNorseProviderOptions</c> choreography as the runtime and migration-host registrations —
/// one copy, so design-time output cannot drift from what the running container produces. The
/// connection string is always the binding's inert placeholder: <c>migrations add</c>/<c>remove</c>
/// build the model offline and never open a connection; running migrations against real
/// infrastructure is the migration host's job, never design tooling's. There is deliberately no
/// environment-variable escape hatch.
/// </summary>
/// <typeparam name="TContext">The Norse EF context this factory constructs at design time.</typeparam>
public abstract class NorseDesignTimeDbContextFactory<TContext> : IDesignTimeDbContextFactory<TContext>
	where TContext : DbContext, INorseDbContext
{
	/// <summary>The provider binding — names the provider this factory's realm project targets.</summary>
	protected abstract INorseEfProvider ProviderBinding { get; }

	/// <summary>The realm's database name — e.g. <c>"norse_reference"</c>.</summary>
	protected abstract string DatabaseName { get; }

	/// <inheritdoc />
	public TContext CreateDbContext(string[] args)
	{
		DbContextOptionsBuilder<TContext> optionsBuilder = new();
		ConfigureOptions(optionsBuilder);
		return CreateContext(optionsBuilder.Options);
	}

	/// <summary>
	/// Applies the shared provider-options choreography with the binding's placeholder connection
	/// string and this factory's own assembly as the migrations assembly. Override to layer in
	/// additional configuration (e.g. an ASP.NET Core Identity-style context calling
	/// <c>UseApplicationServiceProvider</c> to control schema version); call
	/// <c>base.ConfigureOptions(builder)</c> first unless deliberately replacing the wiring.
	/// </summary>
	/// <param name="builder">The options builder to configure.</param>
	protected virtual void ConfigureOptions(DbContextOptionsBuilder<TContext> builder) =>
		builder.ApplyNorseProviderOptions(ProviderBinding,
			ProviderBinding.DesignTimePlaceholderConnectionString(DatabaseName),
			GetType().Assembly.GetName().Name);

	/// <summary>Constructs <typeparamref name="TContext"/> from the configured options.</summary>
	/// <param name="options">The configured options.</param>
	/// <returns>The context instance.</returns>
	protected abstract TContext CreateContext(DbContextOptions<TContext> options);
}
```

Update the `.Design` csproj `Description` to: `Norse.Persistence.EntityFramework.Design: DdlEmittingMigrationsScaffolder, AddNorseDesignTimeServices(), and the provider-neutral NorseDesignTimeDbContextFactory base — referenced only by realm *.Migrations.{Provider} projects; never by runtime containers; never connects to a database.`

**Class law check:** `NorseDesignTimeDbContextFactory<TContext>` and the test fixture's `TestFactoryBase` are both `abstract`; the two concrete factories are `sealed`. Fully compliant — no exception to document.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/Persistence.EntityFramework.Design.Tests/Persistence.EntityFramework.Design.Tests.csproj`
Expected: PASS (including the pre-existing scaffolder/schema-path tests).

- [ ] **Step 5: Commit**

```bash
git add src/Persistence.EntityFramework.Design tests/Persistence.EntityFramework.Design.Tests
git commit -m "Add the provider-neutral design-time factory; the env-var escape hatch dies with the provider factories"
```

---

### Task 8: The one provider-agnostic migrations generator

**Files:**
- Create: `gen/Persistence.EntityFramework.Migrations.Generator/Persistence.EntityFramework.Migrations.Generator.csproj`
- Create: `gen/Persistence.EntityFramework.Migrations.Generator/MigrationContributorDiscovery.cs` (moved from `Generator.Shared`, extended)
- Create: `gen/Persistence.EntityFramework.Migrations.Generator/MigrationContributorGenerator.cs` (consolidated from the two provider generators)
- Modify: `src/Persistence.EntityFramework.Migrations/Persistence.EntityFramework.Migrations.csproj` (Analyzer reference + packing target)
- Create: `tests/Persistence.EntityFramework.Migrations.Generator.Tests/Persistence.EntityFramework.Migrations.Generator.Tests.csproj`
- Create: `tests/Persistence.EntityFramework.Migrations.Generator.Tests/MigrationContributorGeneratorTests.cs`
- Modify: `Urdarbrunnr.slnx` (add both projects)

**Interfaces:**
- Consumes: metadata names — attribute `Norse.Persistence.EntityFramework.Migrations.MigrationConnectionStringAttribute`; contributor base `EfMigrationContributor\`1` in namespace `Norse.Persistence.EntityFramework.Migrations` (both moved in Task 4); provider interface `Norse.Persistence.EntityFramework.INorseEfMigrationProvider` (Task 3); `Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot` and `...Infrastructure.DbContextAttribute`; bindings' `public static Instance` convention (Tasks 5–6); emitted calls target `AddNorseMigrationContext` (Task 4) and Midgard's `AddNorseMigrationsRunner()`/`AddNorseSeedingRunner()` (unchanged).
- Produces: generator assembly `Norse.Persistence.EntityFramework.Migrations.Generator` (the name the `_NorseRemoveUnwantedGeneratorAnalyzers` strip target matches against `NorseRef Include="Persistence.EntityFramework.Migrations" Generator="true"`); diagnostics `NORSE030`–`NORSE033`, category `Norse.Migrations`, all errors.

- [ ] **Step 1: Create the generator project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Persistence.EntityFramework.Migrations.Generator: the provider-agnostic Roslyn IIncrementalGenerator that discovers EfMigrationContributor&lt;TContext&gt; implementations, ISeedContributor implementations, and the single INorseEfMigrationProvider binding in the compilation, derives each context's migrations assembly from its ModelSnapshot, and emits AddNorseMigrations().</Description>
	</PropertyGroup>
	<ItemGroup>
		<NorseRef Include="Abstractions.Emit">
			<Repo>Asgard</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

Move `MigrationContributorDiscovery.cs` in from `gen/Persistence.EntityFramework.Design.Generator.Shared/` (`git mv`), namespace → `Norse.Persistence.EntityFramework.Migrations.Generator`. (The old gen projects still link the old path until Task 9 deletes them — they keep building because the Shared file is not removed until then; do NOT delete the Shared folder in this task.) Actually, `git mv` would break them mid-task: **copy** the file in this task, delete the original with the old generators in Task 9.

- [ ] **Step 2: Write the failing tests**

Test csproj:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="*">
			<PrivateAssets>all</PrivateAssets>
		</PackageReference>
		<ProjectReference Include="../../gen/Persistence.EntityFramework.Migrations.Generator/Persistence.EntityFramework.Migrations.Generator.csproj" />
		<ProjectReference Include="../../src/Persistence.EntityFramework/Persistence.EntityFramework.csproj" />
		<ProjectReference Include="../../src/Persistence.EntityFramework.Migrations/Persistence.EntityFramework.Migrations.csproj" />
		<ProjectReference Include="../../src/Persistence.EntityFramework.PostgreSQL/Persistence.EntityFramework.PostgreSQL.csproj" />
		<ProjectReference Include="../../src/Persistence.EntityFramework.SqlServer/Persistence.EntityFramework.SqlServer.csproj" />
	</ItemGroup>
</Project>
```

Port the test harness from the old `Design.PostgreSQL.Generator.Tests/MigrationContributorGeneratorTests.cs` (its `CreateCompilation` helper and the seed-contributor/compiles-clean tests port with mechanical using/namespace updates — attribute and contributor now come from `Norse.Persistence.EntityFramework.Migrations`). The harness gains: a `MetadataReference` for `typeof(Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot).Assembly`, a **parameterizable reference set** (so tests control which provider binding assemblies are referenced), and:

```csharp
static MetadataReference CreateReferencedAssembly(string assemblyName, string source,
	params MetadataReference[] extraReferences)
{
	var compilation = CSharpCompilation.Create(assemblyName,
		[CSharpSyntaxTree.ParseText(source)],
		[.. StandardReferences, .. extraReferences],
		new(OutputKind.DynamicallyLinkedLibrary));
	using MemoryStream stream = new();
	var emit = compilation.Emit(stream);
	emit.Success.ShouldBeTrue(string.Join(Environment.NewLine, emit.Diagnostics));
	stream.Position = 0;
	return MetadataReference.CreateFromStream(stream);
}
```

(`StandardReferences` = the harness's shared runtime + EF + Norse reference list.) New/changed tests — exact behaviors:

1. **`Generator_emits_the_discovered_provider_binding_and_neutral_choreography`** — contributor + context + snapshot in the main source (snapshot below), references include the PostgreSQL binding assembly (`typeof(NorsePostgresEfProvider).Assembly.Location`) but NOT SqlServer. Assert generated text contains `AddNorseMigrationContext`, `global::Norse.Persistence.EntityFramework.PostgreSQL.NorsePostgresEfProvider.Instance`, `"test-db"`, `AddNorseMigrationsRunner`, and does NOT contain `AddNorsePostgresMigrationContext`. Snapshot fixture to append to the main source (needs `using Microsoft.EntityFrameworkCore;` and `using Microsoft.EntityFrameworkCore.Infrastructure;`):

```csharp
[DbContext(typeof(TestContext))]
sealed class TestContextModelSnapshot : ModelSnapshot
{
	protected override void BuildModel(ModelBuilder modelBuilder)
	{
	}
}
```

2. **`Generator_derives_the_migrations_assembly_from_the_snapshots_assembly_not_the_contributors`** — the real split shape: context in referenced assembly `"TestAssembly.Data"`; snapshot (with `[DbContext(typeof(TestContext))]`) in referenced assembly `"TestAssembly.Data.Migrations.PostgreSQL"` built with a reference to the first; contributor in the main compilation (`"TestAssembly"`). Assert the generated text contains `"TestAssembly.Data.Migrations.PostgreSQL"` and does not contain `"TestAssembly"` as the migrations-assembly argument.
3. **`Generator_reports_NORSE030_when_contributors_exist_but_no_provider_binding_is_referenced`** — contributor + snapshot, no provider assembly in the references. Assert one diagnostic with Id `"NORSE030"`, Severity Error, and zero generated trees.
4. **`Generator_reports_NORSE031_when_two_provider_bindings_are_referenced`** — reference both PostgreSQL and SqlServer binding assemblies. Assert diagnostic `"NORSE031"` whose message contains both type names, and zero generated trees.
5. **`Generator_reports_NORSE032_when_a_context_has_no_ModelSnapshot`** — contributor + context, no snapshot anywhere. Assert diagnostic `"NORSE032"` naming the context type, zero generated trees.
6. **`Generator_reports_NORSE033_when_the_binding_has_no_public_static_Instance`** — reference a `CreateReferencedAssembly`-built assembly containing a minimal `INorseEfMigrationProvider` implementation without an `Instance` property (implement the interface members trivially; `NameRewriter`/`EntityRenameHook` return null, `Configure`/`Enrich` empty, placeholder returns `""`). Assert diagnostic `"NORSE033"`.
7. **`Generator_emits_no_source_and_no_diagnostics_for_an_empty_compilation`** — port of the existing empty test, extended: also assert zero diagnostics (a compilation with no contributors must not demand a provider — the neutral packages themselves compile under this generator in dev mode).
8. **`Generator_discovers_seed_contributors_and_emits_registration`** and **`Generator_emitted_source_compiles_for_seed_contributor_that_does_not_override_ConfigureServices`** — ports. Seed-only compilations (no migration contributors) must emit WITHOUT requiring a provider binding. The compiles-clean test's stand-in stubs for Midgard's runner extensions port as-is; its stand-ins for `AddNorsePostgresMigrationContext` are replaced by the real `AddNorseMigrationContext` from the referenced `.Migrations` assembly.

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Persistence.EntityFramework.Migrations.Generator.Tests/Persistence.EntityFramework.Migrations.Generator.Tests.csproj`
Expected: FAIL — the generator project has no `MigrationContributorGenerator` yet.

- [ ] **Step 4: Implement the discovery extensions**

In the copied `MigrationContributorDiscovery.cs`:

0. **First, verify `AllTypes` walks the full reference closure.** Provider bindings and
   `ModelSnapshot`s live *only in referenced assemblies* — unlike contributors, which the proven
   pattern also found in the main compilation. The helper must traverse
   `compilation.SourceModule.ReferencedAssemblySymbols` (it does today) — if it were ever
   source-only (`compilation.Assembly.GlobalNamespace` alone), NORSE030 would fire unconditionally
   and every discovery test in this task would go red at once for a reason none of them names.
   Confirm before extending; extend the helper if the traversal is source-only.
1. Update the metadata-name constants: `AttributeMetadataName` → `"Norse.Persistence.EntityFramework.Migrations.MigrationConnectionStringAttribute"`; in `FindEfContextType`, the containing-namespace check → `"Norse.Persistence.EntityFramework.Migrations"`.
2. `ContributorInfo.MigrationsAssemblyName` becomes `string?` — populated by snapshot derivation, null when none found (the generator reports NORSE032):

```csharp
const string ModelSnapshotMetadataName =
	"Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot";

const string DbContextAttributeMetadataName =
	"Microsoft.EntityFrameworkCore.Infrastructure.DbContextAttribute";

// The 2026-07-25 AppHost failure, fixed structurally: the migrations assembly is wherever the
// context's ModelSnapshot actually compiles — never the contributor's own assembly, which the
// shared-contributor/provider-split project shape made wrong.
static string? FindMigrationsAssemblyName(Compilation compilation, INamedTypeSymbol contextType)
{
	var contextDisplay = contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

	foreach (var type in AllTypes(compilation))
	{
		if (type.IsAbstract || !DerivesFromModelSnapshot(type))
			continue;

		var attr = type.GetAttributes().FirstOrDefault(a =>
			a.AttributeClass?.ToDisplayString() == DbContextAttributeMetadataName);

		if (attr?.ConstructorArguments is [{ Value: INamedTypeSymbol snapshotContext }] &&
			snapshotContext.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == contextDisplay)
			return type.ContainingAssembly.Name;
	}

	return null;
}

// Display-string matching, not SymbolEqualityComparer -- same cross-layer symbol-identity
// rationale as ImplementsContributorInterface above.
static bool DerivesFromModelSnapshot(INamedTypeSymbol type)
{
	var current = type.BaseType;
	while (current is not null)
	{
		if (current.ToDisplayString() == ModelSnapshotMetadataName)
			return true;
		current = current.BaseType;
	}
	return false;
}
```

In `FindContributors`, replace `type.ContainingAssembly.Name` with `FindMigrationsAssemblyName(compilation, dbContextType)`.

3. Add provider discovery:

```csharp
const string MigrationProviderInterfaceMetadataName =
	"Norse.Persistence.EntityFramework.INorseEfMigrationProvider";

public static IList<ProviderInfo> FindMigrationProviders(Compilation compilation)
{
	IList<ProviderInfo> results = [];

	foreach (var type in AllTypes(compilation))
	{
		if (type.IsAbstract || type.TypeKind != TypeKind.Class)
			continue;

		if (!type.AllInterfaces.Any(i =>
			i.ToDisplayString() == MigrationProviderInterfaceMetadataName))
			continue;

		var hasInstance = type.GetMembers("Instance").OfType<IPropertySymbol>()
			.Any(p => p.IsStatic && p.DeclaredAccessibility == Accessibility.Public);

		results.Add(new ProviderInfo(
			type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), hasInstance));
	}

	return results;
}
```

with, beside the existing info structs:

```csharp
readonly struct ProviderInfo(string typeDisplayName, bool hasInstance)
{
	public string TypeDisplayName { get; } = typeDisplayName;
	public bool HasInstance { get; } = hasInstance;
}
```

- [ ] **Step 5: Implement the consolidated generator**

Create `MigrationContributorGenerator.cs` in the new gen project — the old PostgreSQL generator's shape with provider-neutral emission and the diagnostic gate. Descriptors follow the platform's existing style (`NORSE010`/`NORSE020` families):

```csharp
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Norse.Abstractions.Emit;

namespace Norse.Persistence.EntityFramework.Migrations.Generator;

[Generator]
public sealed class MigrationContributorGenerator : IIncrementalGenerator
{
	static readonly DiagnosticDescriptor _noProvider = new(
		"NORSE030", "No provider binding referenced",
		"Migration contributors were found but no INorseEfMigrationProvider implementation is visible to this compilation — reference exactly one provider binding package (e.g. Norse.Persistence.EntityFramework.PostgreSQL)", "Norse.Migrations",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	static readonly DiagnosticDescriptor _multipleProviders = new(
		"NORSE031", "Multiple provider bindings referenced",
		"Exactly one INorseEfMigrationProvider implementation must be visible to a migrations compilation; found: {0}", "Norse.Migrations",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	static readonly DiagnosticDescriptor _noSnapshot = new(
		"NORSE032", "No ModelSnapshot for context",
		"No ModelSnapshot annotated with [DbContext(typeof({0}))] is visible to this compilation — the migrations assembly cannot be derived; reference the realm's *.Migrations.{{Provider}} project", "Norse.Migrations",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	static readonly DiagnosticDescriptor _noInstance = new(
		"NORSE033", "Provider binding missing Instance",
		"Provider binding '{0}' must expose a public static Instance property — the generated AddNorseMigrations() consumes the binding through it", "Norse.Migrations",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var model = context.CompilationProvider.Select(static (compilation, _) => (
			Contributors: MigrationContributorDiscovery.FindContributors(compilation),
			SeedContributors: MigrationContributorDiscovery.FindSeedContributors(compilation),
			Providers: MigrationContributorDiscovery.FindMigrationProviders(compilation)));

		context.RegisterSourceOutput(model, static (ctx, model) =>
		{
			if (model.Contributors.Count == 0 && model.SeedContributors.Count == 0)
				return;

			var provider = default(ProviderInfo);
			if (model.Contributors.Count > 0)
			{
				if (model.Providers.Count == 0)
				{
					ctx.ReportDiagnostic(Diagnostic.Create(_noProvider, Location.None));
					return;
				}

				if (model.Providers.Count > 1)
				{
					ctx.ReportDiagnostic(Diagnostic.Create(_multipleProviders, Location.None,
						string.Join(", ", model.Providers.Select(p => p.TypeDisplayName))));
					return;
				}

				provider = model.Providers[0];
				if (!provider.HasInstance)
				{
					ctx.ReportDiagnostic(Diagnostic.Create(_noInstance, Location.None,
						provider.TypeDisplayName));
					return;
				}

				var missingSnapshots = model.Contributors
					.Where(c => c.MigrationsAssemblyName is null).ToList();
				if (missingSnapshots.Count > 0)
				{
					foreach (var c in missingSnapshots)
						ctx.ReportDiagnostic(Diagnostic.Create(_noSnapshot, Location.None,
							c.ContextType));
					return;
				}
			}

			var source = BuildSource(model.Contributors, model.SeedContributors, provider);
			ctx.AddSource("NorseMigrationsExtensions.g.cs", SourceText.From(source, Utf8NoBom.Encoding));
		});
	}

	static string BuildSource(IList<ContributorInfo> contributors,
		IList<SeedContributorInfo> seedContributors, ProviderInfo provider)
	{
		StringBuilder sb = new();

		sb.AppendCSharp(
			"""
			// <auto-generated />
			#nullable enable
			using Microsoft.EntityFrameworkCore;
			using Microsoft.Extensions.DependencyInjection;
			using Microsoft.Extensions.Hosting;
			using Norse.Abstractions.Migrations;
			using Norse.Abstractions.Migrations.Seeding;
			using Norse.Persistence.EntityFramework.Migrations;
			using Norse.Infrastructure.Migrations;

			static class NorseMigrationsGeneratedExtensions
			{
				public static global::Microsoft.Extensions.Hosting.IHostApplicationBuilder AddNorseMigrations(
					this global::Microsoft.Extensions.Hosting.IHostApplicationBuilder builder)
				{
			""");

		foreach (var c in contributors)
			sb.AppendCSharp(
				$"""
						builder.AddNorseMigrationContext<{c.ContextType}>({provider.TypeDisplayName}.Instance, "{c.ConnectionStringName}", "{c.MigrationsAssemblyName}");
						builder.Services.AddTransient<global::Norse.Abstractions.Migrations.IMigrationContributor, {c.ContributorType}>();
				""");

		foreach (var s in seedContributors)
			sb.AppendCSharp(
				$"""
						ConfigureSeedContributor<{s.ContributorType}>(builder.Services);
						builder.Services.AddTransient<global::Norse.Abstractions.Migrations.Seeding.ISeedContributor, {s.ContributorType}>();
				""");

		sb.AppendCSharp(
			"""
					builder.AddNorseMigrationsRunner();
					builder.AddNorseSeedingRunner();
					return builder;
				}
			""");

		if (seedContributors.Count > 0)
			sb.AppendCSharp(
				"""
					static void ConfigureSeedContributor<T>(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
						where T : global::Norse.Abstractions.Migrations.Seeding.ISeedContributor
						=> T.ConfigureServices(services);
				""");

		sb.AppendCSharp(
			"}");

		return sb.ToString();
	}
}
```

(`ProviderInfo.TypeDisplayName` is already `global::`-prefixed via `FullyQualifiedFormat`, matching the emitted-code fully-qualification law. `AddNorseMigrationsRunner`/`AddNorseSeedingRunner` remain emitted unconditionally, exactly as today.)

- [ ] **Step 6: Wire the generator into the `.Migrations` package**

In `src/Persistence.EntityFramework.Migrations/Persistence.EntityFramework.Migrations.csproj`, add to the ItemGroup:

```xml
<ProjectReference
	Include="../../gen/Persistence.EntityFramework.Migrations.Generator/Persistence.EntityFramework.Migrations.Generator.csproj"
	OutputItemType="Analyzer"
	ReferenceOutputAssembly="false" />
```

and add the packing target after the ItemGroup (the exact pattern of the root package's `IncludeGeneratorInPackage`, names swapped):

```xml
<Target Name="IncludeGeneratorInPackage" BeforeTargets="_GetPackageFiles">
	<MSBuild Projects="../../gen/Persistence.EntityFramework.Migrations.Generator/Persistence.EntityFramework.Migrations.Generator.csproj"
		Targets="Build"
		Properties="Configuration=$(Configuration)" />
	<ItemGroup>
		<None Include="../../gen/Persistence.EntityFramework.Migrations.Generator/bin/$(Configuration)/netstandard2.0/Norse.Persistence.EntityFramework.Migrations.Generator.dll"
			Pack="true"
			PackagePath="analyzers/dotnet/cs/"
			Visible="false" />
		<None Include="../../gen/Persistence.EntityFramework.Migrations.Generator/bin/$(Configuration)/netstandard2.0/Norse.Abstractions.Emit.dll"
			Pack="true"
			PackagePath="analyzers/dotnet/cs/"
			Visible="false" />
	</ItemGroup>
</Target>
```

Add both new projects to `Urdarbrunnr.slnx` (`/gen/` and `/tests/` folders).

- [ ] **Step 7: Run to verify pass**

Run: `dotnet test tests/Persistence.EntityFramework.Migrations.Generator.Tests/Persistence.EntityFramework.Migrations.Generator.Tests.csproj`
Expected: PASS — all ported and new tests.

- [ ] **Step 8: Commit**

```bash
git add -A gen src/Persistence.EntityFramework.Migrations tests/Persistence.EntityFramework.Migrations.Generator.Tests Urdarbrunnr.slnx
git commit -m "Consolidate the migration generators into one provider-agnostic generator with binding discovery and snapshot-derived migrations assemblies"
```

---

### Task 9: Delete the provider-specific Design packages and old generators

**Files:**
- Delete (entire directories): `src/Persistence.EntityFramework.Design.PostgreSQL/`, `src/Persistence.EntityFramework.Design.SqlServer/`, `gen/Persistence.EntityFramework.Design.PostgreSQL.Generator/`, `gen/Persistence.EntityFramework.Design.SqlServer.Generator/`, `gen/Persistence.EntityFramework.Design.Generator.Shared/`, `tests/Persistence.EntityFramework.Design.PostgreSQL.Tests/`, `tests/Persistence.EntityFramework.Design.SqlServer.Tests/`, `tests/Persistence.EntityFramework.Design.PostgreSQL.Generator.Tests/`, `tests/Persistence.EntityFramework.Design.SqlServer.Generator.Tests/`
- Modify: `Urdarbrunnr.slnx` (remove all nine entries, including the Shared file entry under `/gen/`)

**Interfaces:**
- Consumes: nothing — everything these projects provided now lives in `.Design` (factory), `.Migrations` (contributor/attribute/choreography/generator), and the binding packages (Tasks 4–8).
- Produces: the spec §3 end-state package set. Downstream realms referencing the deleted packages break **by design** until the adoption sweep — do not touch them.

- [ ] **Step 1: Delete**

```bash
git rm -r src/Persistence.EntityFramework.Design.PostgreSQL src/Persistence.EntityFramework.Design.SqlServer \
	gen/Persistence.EntityFramework.Design.PostgreSQL.Generator gen/Persistence.EntityFramework.Design.SqlServer.Generator \
	gen/Persistence.EntityFramework.Design.Generator.Shared \
	tests/Persistence.EntityFramework.Design.PostgreSQL.Tests tests/Persistence.EntityFramework.Design.SqlServer.Tests \
	tests/Persistence.EntityFramework.Design.PostgreSQL.Generator.Tests tests/Persistence.EntityFramework.Design.SqlServer.Generator.Tests
```

Remove the nine corresponding entries from `Urdarbrunnr.slnx` (four `/src|gen/` projects, four test projects, plus the `gen/Persistence.EntityFramework.Design.Generator.Shared/MigrationContributorDiscovery.cs` file entry).

- [ ] **Step 2: Verify the whole solution is green**

Run: `dotnet build Urdarbrunnr.slnx` then `dotnet test Urdarbrunnr.slnx`
Expected: build succeeds with zero warnings-as-errors; all remaining test projects pass. Stray references to deleted projects surface here — fix by deletion, not suppression.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "Delete the provider-specific Design packages and per-provider generators"
```

---

### Task 10: Realm docs + full verification (pack smoke included)

**Files:**
- Modify: `README.md`, `CLAUDE.md` (Urðarbrunnr repo root — boy-scout law: the pair updates together)

**Interfaces:**
- Consumes: the shipped end state of Tasks 1–9.
- Produces: realm docs describing the five live assemblies + generator; the staged branch ready for Buvy's review.

- [ ] **Step 1: Update the realm doc pair**

Rewrite the assembly inventory in both files to the end state (same story, two altitudes — README public narrative, CLAUDE.md working law):

- `Norse.Persistence.EntityFramework` — neutral foundation: contexts, conventions, converters, **the provider seam** (`INorseEfProvider`/`INorseEfMigrationProvider`, `NorseNameRewriters`, `AddNorseContext`, `ApplyNorseProviderOptions`).
- `Norse.Persistence.EntityFramework.Migrations` — contributor, attribute, `AddNorseMigrationContext`, and the provider-agnostic generator (`Generator="true"` reference point for migrations services; diagnostics NORSE030–033).
- `Norse.Persistence.EntityFramework.Design` — DDL-emitting scaffolder, design-time services, and the one neutral `NorseDesignTimeDbContextFactory` (never connects; no env-var escape hatch).
- `Norse.Persistence.EntityFramework.PostgreSQL` / `.SqlServer` — one sealed binding each; note the spec's acceptance test (Oracle/SQLite arrive as one thin package each, zero neutral edits).
- Remove every mention of `Design.PostgreSQL`/`Design.SqlServer`, the per-provider generators, `useSnakeCaseNaming`, and `DOTNET_EFTOOLS_CONNECTIONSTRING`. Link the spec: `../Glitnir/docs/Urdarbrunnr/specs/2026-07-27-ef-provider-seam-repackaging-design.md`.
- CLAUDE.md additionally records: the adoption sweep (Himinbjörg/Mímisbrunnr/Yggdrasil) is a follow-on session per spec §11, and until it lands the Bifröst dev-mode composition of the migrations path is expected red.

- [ ] **Step 2: Full verification**

```bash
dotnet build Urdarbrunnr.slnx
dotnet test Urdarbrunnr.slnx
dotnet pack src/Persistence.EntityFramework.Migrations/Persistence.EntityFramework.Migrations.csproj -c Release -o /tmp/norse-pack-smoke
```

Expected: build + tests green; pack succeeds. Then verify the generator bundle:

```bash
unzip -l /tmp/norse-pack-smoke/Norse.Persistence.EntityFramework.Migrations.*.nupkg | grep analyzers
```

Expected: `analyzers/dotnet/cs/Norse.Persistence.EntityFramework.Migrations.Generator.dll` and `analyzers/dotnet/cs/Norse.Abstractions.Emit.dll` both present.

- [ ] **Step 3: Commit and stop**

```bash
git add README.md CLAUDE.md
git commit -m "Update realm docs to the provider-seam package topology"
git log --oneline master..HEAD
```

**Stop here.** Do not push, do not open a PR, do not merge — show the branch summary and hand off to Buvy. The adoption sweep (spec §11) and the AppHost live gate are the next session's plan.

---

## Self-Review Record

- **Spec coverage:** §3 topology → Tasks 4, 8, 9; §4 contract (incl. `useSnakeCaseNaming` deletion — it ceases to exist because the extension methods carrying it are deleted in Tasks 5–6 and the naming decision moves to binding data) → Tasks 3, 5, 6; §5 choreography + Aspire gate → Task 0 (pure-Aspire pre-flight, fail-fast before anything is built on the claim), Tasks 3, 4, 5 (permanent gate test), 6; §6 generator + NORSE03x + snapshot derivation → Task 8; §7 naming table → Tasks 1, 2, 5, 6 (Oracle's UPPER_SNAKE rewriter ships now, its binding ships with Oracle); §8 design-time → Task 7; §9 tiers → the split contract (Tasks 3–4); the SQLite binding itself and its Development guard are explicitly future work (spec §10) — nothing to build here; §11/§12 adoption + AppHost gate → explicitly deferred, Global Constraints + Task 10.
- **Type consistency:** `INorseEfProvider` / `INorseEfMigrationProvider` / `NorseNameRewriters` / `ApplyNorseProviderOptions` / `AddNorseContext` / `AddNorseMigrationContext` / `NorsePostgresEfProvider.Instance` / `NorseSqlServerEfProvider.Instance` / `ProviderBinding` are used with identical spellings and signatures across Tasks 3–8.
- **Known judgment calls surfaced:** (1) the SQL Server binding keeps `RenameTemporalHistoryTable` wired against a null rewriter — inert but paired, tested in Task 6; (2) EF1001 inline pragmas in Task 6 tests carry written justification per the Suppression Law; (3) the equivalence gates (Task 0 pre-flight and Tasks 5–6) compare registration sets, not Aspire's connection-string precedence semantics — that question stays open until the adoption sweep's AppHost run, by ruling.
