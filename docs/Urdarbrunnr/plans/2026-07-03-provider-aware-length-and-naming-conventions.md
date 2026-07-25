# Provider-Aware Length and Naming Conventions Implementation Plan

**Status:** Shipped 2026-07-03 — all 9 tasks complete, final whole-branch review clean (one Important
finding caught and fixed: see the spec's addendum), Urdarbrunnr PR #15 and Himinbjörg PR #16 both
merged and released as v0.0.4. Full per-task ledger: `Urdarbrunnr/.superpowers/sdd/progress.md`
(git-ignored scratch, local to whichever checkout ran the plan — not a durable record; this status
line is the durable one).

**Amendment (2026-07-25):** every `Norse.EntityFramework.*` project/namespace/path in this plan
(`EntityFramework.SqlServer`, `EntityFramework.Migrations.PostgreSQL.Generator`, etc.) predates the
widening rename to `Norse.Persistence.EntityFramework.*` (PR #31, merged 2026-07-22, shipped v0.0.4) —
the shipped SQL Server trio lives under the new namespace and `src/Persistence.EntityFramework*` paths
today. Separately, the `MigrationContributorGenerator` built in Tasks 4–5 as pure `EfMigrationContributor<TContext>`
discovery now also discovers Asgard's `ISeedContributor` and wires `AddNorseSeedingRunner()` into the
same generated `AddNorseMigrations()` call — see `../../Platform/specs/2026-07-03-seeding-framework-design.md`.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `[FixedLength(n)]` and table/column naming provider-aware in `Norse.EntityFramework`, ship full SQL Server package parity alongside the existing PostgreSQL packages, and fix the pre-existing bug where snake_case naming was applied to every provider unconditionally.

**Architecture:** Two independent conventions become explicit, required parameters threaded down from `NorseDbContext`/provider registration extensions instead of being hardcoded platform-wide behavior: `applyFixedLength` (computed from `Database.ProviderName` inside `ConfigureConventions`, since it's a model-building decision) and `useSnakeCaseNaming` (an explicit opt-in/opt-out parameter on each provider's registration extension, defaulting to whatever that storage engine's own escape-free native style is — snake_case on Postgres, PascalCase on SQL Server). The SQL Server package trio (`EntityFramework.SqlServer`, `.Migrations.SqlServer`, `.Migrations.SqlServer.Generator`) mirrors the existing PostgreSQL trio exactly; the migration generator's provider-agnostic discovery logic is extracted to a shared linked source file so it isn't duplicated.

**Tech Stack:** .NET, EF Core 10 (`Microsoft.EntityFrameworkCore.SqlServer`, `Npgsql.EntityFrameworkCore.PostgreSQL`), `Aspire.Microsoft.EntityFrameworkCore.SqlServer`, `EFCore.NamingConventions`, Roslyn `IIncrementalGenerator`, xUnit v3 + Shouldly.

## Global Constraints

- Tabs for indentation; `var` for return assignments; explicit types with `new()` for construction.
- Accessibility modifiers: `omit_if_default` — no `public`/`internal` keyword where it's already the default.
- `sealed` by default for every new type.
- US English spelling everywhere.
- No silent fallbacks — required parameters over defaults where a caller must state intent (`NorseModelConventions.Apply`'s `applyFixedLength` has no default).
- `src/Directory.Build.props` and `tests/Directory.Build.props` are immutable in this plan — new projects inherit them automatically by directory nesting; do not edit either file.
- Package versions float (`Version="*"`) per this repo's existing convention — never pin.
- No automatic git commits beyond what each task step explicitly stages/commits per this plan; the human reviews before anything merges.
- Every new/changed public XML doc comment must build clean (`GenerateDocumentationFile` is `true` platform-wide — a missing doc comment on a public member is a build warning treated as an error).

---

## Task 1: FixedLength bifurcation — RequireExplicitLengthConvention, NorseModelConventions, NorseDbContext

This is the core mechanism change and must land as one unit: `RequireExplicitLengthConvention`'s constructor, `NorseModelConventions.Apply`'s signature, and `NorseDbContext.ConfigureConventions`'s call site all change together — none of the three compiles correctly without the other two.

**Files:**
- Modify: `Urdarbrunnr/src/EntityFramework/RequireExplicitLengthConvention.cs`
- Modify: `Urdarbrunnr/src/EntityFramework/NorseModelConventions.cs`
- Modify: `Urdarbrunnr/src/EntityFramework/NorseDbContext.cs`
- Modify: `Urdarbrunnr/src/EntityFramework/NorseDbContextOptionsExtensions.cs`
- Modify: `Urdarbrunnr/src/EntityFramework/FixedLengthAttribute.cs`
- Modify: `Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj`
- Modify: `Urdarbrunnr/tests/EntityFramework.Tests/RequireExplicitLengthConventionTests.cs`
- Modify: `Urdarbrunnr/tests/EntityFramework.Tests/NorseDbContextOptionsExtensionsTests.cs`

**Interfaces:**
- Produces: `NorseModelConventions.Apply(ModelConfigurationBuilder configurationBuilder, bool applyFixedLength)` — required second parameter, no default. Every later task that calls this (none do directly — only `NorseDbContext` and, out-of-repo, Himinbjörg's `NorseIdentityDbContext` in Task 8) must pass it explicitly.
- Produces: `NorseDbContextOptionsExtensions.SqlServerProviderName` — `public const string`, value `"Microsoft.EntityFrameworkCore.SqlServer"`. Task 8 (Himinbjörg) references this by its fully qualified name.
- Produces: `NorseDbContext` no longer has an `OnConfiguring` override at all (removed). Naming conventions are the provider registration extension's job from here on (Tasks 2–3).

- [ ] **Step 1: Add the failing/changed tests first**

Replace the single `FixedLength_attribute_sets_IsFixedLength_and_satisfies_the_convention` test in
`Urdarbrunnr/tests/EntityFramework.Tests/RequireExplicitLengthConventionTests.cs` with two tests, and
add a SQL Server context-building helper next to the existing SQLite one:

```csharp
	[Fact]
	void FixedLength_attribute_does_not_set_IsFixedLength_on_non_SqlServer_providers()
	{
		using var ctx = CreateContext<FixedLengthContext>();

		var property = ctx.Model.FindEntityType(typeof(FixedLengthEntity))!
			.FindProperty(nameof(FixedLengthEntity.Value))!;

		property.GetMaxLength().ShouldBe(10);
		property.IsFixedLength().ShouldNotBe(true);
	}

	[Fact]
	void FixedLength_attribute_sets_IsFixedLength_on_SqlServer()
	{
		using var ctx = CreateSqlServerContext<FixedLengthContext>();

		var property = ctx.Model.FindEntityType(typeof(FixedLengthEntity))!
			.FindProperty(nameof(FixedLengthEntity.Value))!;

		property.GetMaxLength().ShouldBe(10);
		property.IsFixedLength().ShouldBe(true);
	}
```

Find the existing `CreateContext<TContext>()` helper near the bottom of the file:

```csharp
	static TContext CreateContext<TContext>() where TContext : DbContext =>
		(TContext)Activator.CreateInstance(typeof(TContext),
			new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:").Options)!;
```

Add a sibling helper immediately after it:

```csharp
	static TContext CreateSqlServerContext<TContext>() where TContext : DbContext =>
		(TContext)Activator.CreateInstance(typeof(TContext),
			new DbContextOptionsBuilder<TContext>()
				.UseSqlServer("Server=localhost;Database=test;Trusted_Connection=True;TrustServerCertificate=True;")
				.Options)!;
```

`UseSqlServer` never opens a connection just to build `.Model` — the connection string only needs to
parse, the same reasoning the existing Postgres tests already rely on with an unreachable `Host=localhost`.

Add the SQL Server package reference to `Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj`,
next to the existing Sqlite one:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="*" />
		<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="*" />
		<ProjectReference Include="../../src/EntityFramework/EntityFramework.csproj" />
	</ItemGroup>
	<ItemGroup>
		<!--
			SQLitePCLRaw.lib.e_sqlite3 2.1.11 (transitive via Microsoft.EntityFrameworkCore.Sqlite) has
			a known high-severity vulnerability (GHSA-2m69-gcr7-jv3q). No patched version exists as of
			this writing — 2.1.11 is the latest. The vulnerability is in the bundled native sqlite3
			library; exposure is test-only (in-memory, no production data). Suppressed explicitly here;
			revisit when SQLitePCLRaw publishes a fix.
		-->
		<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-2m69-gcr7-jv3q" />
	</ItemGroup>
</Project>
```

Now update `Urdarbrunnr/tests/EntityFramework.Tests/NorseDbContextOptionsExtensionsTests.cs`. Replace the
existing `ApplyNorseConventions_applies_snake_case_naming` test (it currently relies on
`NorseDbContext.OnConfiguring` applying naming automatically, which Step 3 below removes) with a version
that calls `ApplyNorseConventions` directly, and add a regression test proving `NorseDbContext` no longer
applies naming on its own:

```csharp
	[Fact]
	void ApplyNorseConventions_applies_snake_case_naming()
	{
		var optionsBuilder = new DbContextOptionsBuilder<TestContext>().UseSqlite("Data Source=:memory:");
		NorseDbContextOptionsExtensions.ApplyNorseConventions(optionsBuilder);

		using var ctx = new TestContext(optionsBuilder.Options);
		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		tableName.ShouldBe("test_entities");
	}

	[Fact]
	void NorseDbContext_does_not_apply_naming_conventions_on_its_own()
	{
		var options = new DbContextOptionsBuilder<TestContext>()
			.UseSqlite("Data Source=:memory:")
			.Options;

		using var ctx = new TestContext(options);
		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		// EF Core's own default: the DbSet property name, untouched. Naming is now decided
		// exclusively by the provider registration extension used to register a context — see
		// Norse.EntityFramework.PostgreSQL.NorsePostgresContextExtensions.
		tableName.ShouldBe("TestEntities");
	}
```

- [ ] **Step 2: Run the test suite to confirm the expected failures**

Run: `dotnet test Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj`
Expected: Compile errors — `CreateSqlServerContext` doesn't exist yet is fine (it does, you just added
it), but `FixedLength_attribute_sets_IsFixedLength_on_SqlServer` FAILs (still asserts `IsFixedLength()
== true` unconditionally today, so it currently passes — the *other* new test,
`FixedLength_attribute_does_not_set_IsFixedLength_on_non_SqlServer_providers`, FAILs because today's
code sets `IsFixedLength()` on every provider). `NorseDbContext_does_not_apply_naming_conventions_on_its_own`
FAILs (today's `NorseDbContext.OnConfiguring` still applies snake_case unconditionally, so the table
name is `"test_entities"`, not `"TestEntities"`).

- [ ] **Step 3: Implement the provider-aware convention plumbing**

`Urdarbrunnr/src/EntityFramework/RequireExplicitLengthConvention.cs` — full replacement:

```csharp
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Norse.EntityFramework;

sealed class RequireExplicitLengthConvention(bool applyFixedLength) : IModelFinalizingConvention
{
	public void ProcessModelFinalizing(
		IConventionModelBuilder builder, IConventionContext<IConventionModelBuilder> context)
	{
		List<string> violations = [];

		foreach (var property in builder.Metadata.GetEntityTypes().SelectMany(static t => t.GetProperties()))
		{
			if (property.DeclaringType is IConventionEntityType entityType && entityType.IsMappedToJson())
				continue;

			var storageType = property.GetValueConverter()?.ProviderClrType ?? property.ClrType;
			if (storageType != typeof(string) && storageType != typeof(byte[]))
				continue;

			var maxLengthAttr = property.PropertyInfo?.GetCustomAttribute<System.ComponentModel.DataAnnotations.MaxLengthAttribute>();
			if (maxLengthAttr is not null && maxLengthAttr.Length <= 0)
				property.Builder.HasMaxLength(maxLengthAttr.Length, fromDataAnnotation: true);

			// Fixed-length storage only pays off on SQL Server -- see FixedLengthAttribute's remarks.
			if (applyFixedLength && property.PropertyInfo?.GetCustomAttribute<FixedLengthAttribute>() is not null)
				property.Builder.IsFixedLength(true, fromDataAnnotation: true);

			if (property.GetMaxLength() is null)
				violations.Add($"{property.DeclaringType.ClrType.FullName}.{property.Name} ({storageType.Name})");
		}

		if (violations.Count == 0)
			return;

		throw new InvalidOperationException(
			$"{violations.Count} propert{(violations.Count == 1 ? "y has" : "ies have")} no explicit length declared. " +
			"Decorate with [MaxLength(n)]/[FixedLength(n)], configure HasMaxLength(n) in the entity's Configure method, " +
			"or declare HasMaxLength(-1) if truly unbounded:\n  - " + string.Join("\n  - ", violations));
	}
}
```

`Urdarbrunnr/src/EntityFramework/NorseModelConventions.cs` — full replacement:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework;

/// <summary>
/// Registers the model-finalizing conventions every Norse EF context is guaranteed to enforce:
/// explicit string/byte[] length (<see cref="RequireExplicitLengthConvention"/>) and mandatory
/// entity self-configuration (<see cref="RequireEntityConfigurationConvention"/>).
/// </summary>
public static class NorseModelConventions
{
	/// <summary>
	/// Adds both Norse model-finalizing conventions to <paramref name="configurationBuilder"/>.
	/// </summary>
	/// <param name="configurationBuilder">The configuration builder to register conventions on.</param>
	/// <param name="applyFixedLength">
	/// Whether <see cref="FixedLengthAttribute"/> should translate to <c>.IsFixedLength()</c>. Pass
	/// <see langword="true"/> only for providers where fixed-length storage has a real benefit (SQL
	/// Server); Postgres and everything else should pass <see langword="false"/> — see
	/// <see cref="FixedLengthAttribute"/>'s remarks for why. No default: every caller states its
	/// provider explicitly rather than silently inheriting a guess.
	/// </param>
	/// <returns>The same <paramref name="configurationBuilder"/>, for chaining.</returns>
	public static ModelConfigurationBuilder Apply(ModelConfigurationBuilder configurationBuilder, bool applyFixedLength)
	{
		configurationBuilder.Conventions.Add(_ => new RequireExplicitLengthConvention(applyFixedLength));
		configurationBuilder.Conventions.Add(static _ => new RequireEntityConfigurationConvention());
		return configurationBuilder;
	}
}
```

`Urdarbrunnr/src/EntityFramework/NorseDbContextOptionsExtensions.cs` — full replacement:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework;

/// <summary>
/// Options-builder extensions that apply Norse platform naming conventions.
/// </summary>
public static class NorseDbContextOptionsExtensions
{
	/// <summary>
	/// The stable EF Core provider identity string (what <c>Database.ProviderName</c> returns) for
	/// the SQL Server provider. Exposed so contexts that cannot inherit <see cref="NorseDbContext"/>
	/// (auth contexts inheriting <c>IdentityDbContext</c>) can compute the same provider check
	/// <see cref="NorseDbContext.ConfigureConventions"/> uses for fixed-length applicability, without
	/// duplicating the literal.
	/// </summary>
	public const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";

	/// <summary>
	/// Applies snake_case naming to all entity table names and column names via
	/// <c>EFCore.NamingConventions</c>. Called conditionally by each provider's registration
	/// extension (see <c>Norse.EntityFramework.PostgreSQL.NorsePostgresContextExtensions</c> and its
	/// SQL Server counterpart) — never unconditionally by a context itself, since whether snake_case
	/// is the right default is a provider decision, not a Norse-wide one.
	/// </summary>
	/// <param name="optionsBuilder">The options builder to configure.</param>
	/// <returns>The same <paramref name="optionsBuilder"/> for chaining.</returns>
	public static DbContextOptionsBuilder ApplyNorseConventions(DbContextOptionsBuilder optionsBuilder)
	{
		optionsBuilder.UseSnakeCaseNamingConvention();
		return optionsBuilder;
	}
}
```

`Urdarbrunnr/src/EntityFramework/NorseDbContext.cs` — full replacement:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework;

/// <summary>
/// Abstract <see cref="DbContext"/> base for all non-auth Norse EF contexts. Naming conventions are
/// decided by the provider registration extension used to register a context (see
/// <c>Norse.EntityFramework.PostgreSQL.NorsePostgresContextExtensions</c> and its SQL Server
/// counterpart), never here — this base stays provider-neutral. Auth contexts inherit
/// <c>IdentityDbContext</c> instead of this class and replicate its conventions manually.
/// </summary>
/// <param name="options">The options for this context.</param>
public abstract class NorseDbContext(DbContextOptions options) : DbContext(options), INorseDbContext
{
	/// <inheritdoc />
	protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
	{
		base.ConfigureConventions(configurationBuilder);

		// Fixed-length storage (char(n)/nchar(n)) only pays off on SQL Server. Postgres's own docs
		// say character(n) has no storage/performance benefit over character varying(n) there, and
		// is usually the slower of the two — see FixedLengthAttribute's remarks.
		NorseModelConventions.Apply(configurationBuilder,
			applyFixedLength: Database.ProviderName == NorseDbContextOptionsExtensions.SqlServerProviderName);
	}

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		ConfigureNorseEntities(modelBuilder);
	}

	/// <summary>
	/// Empty by default. A Tier-1 consumer project declares its own <c>DbContext</c> subclass
	/// <c>partial</c> — EntityConfigurationApplicationGenerator (in that project's own
	/// compilation, alongside its <c>INorseEntity&lt;TSelf&gt;</c> entities) emits a second partial
	/// declaration overriding this method. Real virtual dispatch, not a generated static extension call —
	/// see the plan's "Design amendments" note on why the static-extension approach can't work for a base
	/// class compiled once and shipped as a package.
	/// </summary>
	protected virtual void ConfigureNorseEntities(ModelBuilder modelBuilder)
	{
	}
}
```

`Urdarbrunnr/src/EntityFramework/FixedLengthAttribute.cs` — full replacement:

```csharp
namespace Norse.EntityFramework;

/// <summary>
/// Marks a string property as fixed-length. On SQL Server, this translates to
/// <c>.HasMaxLength(n).IsFixedLength()</c> (<c>nchar(n)</c>/<c>char(n)</c>) via
/// <see cref="RequireExplicitLengthConvention"/> at model-finalization time. On every other
/// provider — Postgres included — this attribute behaves exactly like plain
/// <see cref="MaxLengthAttribute"/>: still bounded, never <c>.IsFixedLength()</c>. Postgres's own
/// documentation states <c>character(n)</c> has no storage or performance advantage over
/// <c>character varying(n)</c> on that engine, and is usually the slower of the two — unlike SQL
/// Server, where fixed-length storage avoids a per-row length-prefix. Use this attribute to record
/// design intent ("this really is fixed-length data") even on providers where it has no storage
/// effect; the provider-specific benefit is applied automatically, never by hand.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class FixedLengthAttribute(int length)
	: System.ComponentModel.DataAnnotations.MaxLengthAttribute(length);
```

- [ ] **Step 4: Run the full test suite to confirm everything passes**

Run: `dotnet test Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj`
Expected: PASS, including the two new/changed FixedLength tests and both
`NorseDbContextOptionsExtensionsTests` tests.

- [ ] **Step 5: Build the full Urdarbrunnr solution to catch any other broken call site inside this repo**

Run: `dotnet build Urdarbrunnr/Urdarbrunnr.slnx`
Expected: Build succeeds. (Himinbjörg is a separate repo/submodule and is not part of this solution —
its own break, if any, is Task 8.)

- [ ] **Step 6: Commit**

```bash
git -C Urdarbrunnr add src/EntityFramework/RequireExplicitLengthConvention.cs \
	src/EntityFramework/NorseModelConventions.cs \
	src/EntityFramework/NorseDbContext.cs \
	src/EntityFramework/NorseDbContextOptionsExtensions.cs \
	src/EntityFramework/FixedLengthAttribute.cs \
	tests/EntityFramework.Tests/EntityFramework.Tests.csproj \
	tests/EntityFramework.Tests/RequireExplicitLengthConventionTests.cs \
	tests/EntityFramework.Tests/NorseDbContextOptionsExtensionsTests.cs
git -C Urdarbrunnr commit -m "$(cat <<'EOF'
Make FixedLength and naming conventions provider-aware

RequireExplicitLengthConvention/NorseModelConventions.Apply now take an
explicit applyFixedLength flag instead of always translating
[FixedLength(n)] to IsFixedLength(). Postgres's own docs say
character(n) has no storage/performance benefit over character
varying(n) there, and is usually the slower of the two. NorseDbContext
computes the flag from Database.ProviderName and also drops its
unconditional ApplyNorseConventions() call in OnConfiguring -- naming
becomes a provider-registration-site decision, not a base-class one.
EOF
)"
```

---

## Task 2: Postgres registration extensions — symmetric `useSnakeCaseNaming` parameter

**Files:**
- Modify: `Urdarbrunnr/src/EntityFramework.PostgreSQL/NorsePostgresContextExtensions.cs`
- Modify: `Urdarbrunnr/tests/EntityFramework.PostgreSQL.Tests/NorsePostgresContextExtensionsTests.cs`

**Interfaces:**
- Consumes: `NorseDbContextOptionsExtensions.ApplyNorseConventions(DbContextOptionsBuilder)` from Task 1.
- Produces: `AddNorsePostgresContext<TContext>(builder, connectionStringName, bool useSnakeCaseNaming = true)`
  and `AddNorsePostgresMigrationContext<TContext>(builder, connectionStringName, migrationsAssemblyName, bool useSnakeCaseNaming = true)`.
  Task 5's generator continues calling `AddNorsePostgresMigrationContext<T>(name, assemblyName)` with no
  third argument — the default (`true`) applies, unchanged from today's behavior.

- [ ] **Step 1: Write the failing tests**

Add to `Urdarbrunnr/tests/EntityFramework.PostgreSQL.Tests/NorsePostgresContextExtensionsTests.cs`. First,
give `TestContext` an entity to inspect naming on, and add the `INorseEntity<TSelf>`-conforming
`TestEntity` type the platform's `RequireEntityConfigurationConvention` requires:

```csharp
	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options)
	{
		public DbSet<TestEntity> TestEntities => Set<TestEntity>();
	}

	sealed class TestEntity : INorseEntity<TestEntity>
	{
		public int Id { get; set; }

		[MaxLength(100)]
		public string Name { get; set; } = "";

		public static void Configure(EntityTypeBuilder<TestEntity> builder) { }
	}
```

Add `using Microsoft.EntityFrameworkCore.Metadata.Builders;` to the file's using block.

Add two new test methods:

```csharp
	[Fact]
	void AddNorsePostgresMigrationContext_defaults_to_snake_case_naming()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = "Host=localhost;Database=test" });

		builder.AddNorsePostgresMigrationContext<TestContext>("test-db", "Norse.EntityFramework.PostgreSQL.Tests");

		using var host = builder.Build();
		using var scope = host.Services.CreateScope();
		using var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();

		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		tableName.ShouldBe("test_entities");
	}

	[Fact]
	void AddNorsePostgresMigrationContext_opts_out_of_snake_case_naming_when_requested()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = "Host=localhost;Database=test" });

		builder.AddNorsePostgresMigrationContext<TestContext>(
			"test-db", "Norse.EntityFramework.PostgreSQL.Tests", useSnakeCaseNaming: false);

		using var host = builder.Build();
		using var scope = host.Services.CreateScope();
		using var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();

		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		tableName.ShouldBe("TestEntities");
	}
```

- [ ] **Step 2: Run to confirm failure**

Run: `dotnet test Urdarbrunnr/tests/EntityFramework.PostgreSQL.Tests/EntityFramework.PostgreSQL.Tests.csproj`
Expected: Compile error — `AddNorsePostgresMigrationContext` has no `useSnakeCaseNaming` parameter yet.

- [ ] **Step 3: Implement**

`Urdarbrunnr/src/EntityFramework.PostgreSQL/NorsePostgresContextExtensions.cs` — full replacement:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.EntityFramework.PostgreSQL;

/// <summary>
/// Aspire-wired registration extensions for Norse EF Postgres contexts.
/// </summary>
public static class NorsePostgresContextExtensions
{
	/// <summary>
	/// Registers <typeparamref name="TContext"/> in the Aspire host using Npgsql EF Core integration.
	/// The connection string is resolved by <paramref name="connectionStringName"/> from the
	/// application configuration.
	/// </summary>
	/// <typeparam name="TContext">
	/// The <see cref="DbContext"/> type to register. Must implement <see cref="INorseDbContext"/>.
	/// </typeparam>
	/// <param name="builder">The host application builder.</param>
	/// <param name="connectionStringName">The connection string name in application configuration.</param>
	/// <param name="useSnakeCaseNaming">
	/// Whether to apply snake_case table/column naming. Defaults to <see langword="true"/>: Postgres
	/// folds unquoted identifiers to lowercase, so snake_case is this engine's own escape-free
	/// native style, not an opinionated override being imposed on it. Pass <see langword="false"/>
	/// to opt out and keep EF's raw (quoted) PascalCase naming instead.
	/// </param>
	/// <returns>The same <paramref name="builder"/> for chaining.</returns>
	public static IHostApplicationBuilder AddNorsePostgresContext<TContext>(
		this IHostApplicationBuilder builder,
		string connectionStringName,
		bool useSnakeCaseNaming = true)
		where TContext : DbContext, INorseDbContext
	{
		builder.AddNpgsqlDbContext<TContext>(connectionStringName,
			configureDbContextOptions: opts =>
			{
				if (useSnakeCaseNaming)
					NorseDbContextOptionsExtensions.ApplyNorseConventions(opts);
			});
		return builder;
	}

	/// <summary>
	/// Registers <typeparamref name="TContext"/> for one-shot, non-pooled use (migrations and other
	/// short-lived init-container work). Unlike <see cref="AddNorsePostgresContext{TContext}"/>, this
	/// does NOT pool the context — pooling is reserved for long-running runtime hosts (web server,
	/// worker); a migrations service constructs its context once and exits, so pooling only adds risk
	/// (EF Core forbids <c>OnConfiguring</c> from mutating frozen pooled options) for no benefit.
	/// Still gets Aspire's retry policy, health check, and telemetry via <c>EnrichNpgsqlDbContext</c>.
	/// </summary>
	/// <typeparam name="TContext">
	/// The <see cref="DbContext"/> type to register. Must implement <see cref="INorseDbContext"/>.
	/// </typeparam>
	/// <param name="builder">The host application builder.</param>
	/// <param name="connectionStringName">The connection string name in application configuration.</param>
	/// <param name="migrationsAssemblyName">
	/// The name of the assembly containing <typeparamref name="TContext"/>'s EF Core migrations. Norse
	/// convention places migrations in a sibling <c>*.Migrations</c> assembly, never in the context's own
	/// assembly — this must be supplied explicitly rather than inferred, since EF Core defaults to
	/// searching the context's own assembly and finds nothing there.
	/// </param>
	/// <param name="useSnakeCaseNaming">See <see cref="AddNorsePostgresContext{TContext}"/>.</param>
	/// <returns>The same <paramref name="builder"/> for chaining.</returns>
	public static IHostApplicationBuilder AddNorsePostgresMigrationContext<TContext>(
		this IHostApplicationBuilder builder,
		string connectionStringName,
		string migrationsAssemblyName,
		bool useSnakeCaseNaming = true)
		where TContext : DbContext, INorseDbContext
	{
		var connectionString = builder.Configuration.GetConnectionString(connectionStringName)
			?? throw new InvalidOperationException($"Connection string '{connectionStringName}' was not found.");

		builder.Services.AddDbContext<TContext>(opts =>
		{
			opts.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(migrationsAssemblyName));
			if (useSnakeCaseNaming)
				NorseDbContextOptionsExtensions.ApplyNorseConventions(opts);
		});
		builder.EnrichNpgsqlDbContext<TContext>();

		return builder;
	}
}
```

- [ ] **Step 4: Run to confirm pass**

Run: `dotnet test Urdarbrunnr/tests/EntityFramework.PostgreSQL.Tests/EntityFramework.PostgreSQL.Tests.csproj`
Expected: PASS, all tests including the three pre-existing ones (unaffected — they don't check naming).

- [ ] **Step 5: Commit**

```bash
git -C Urdarbrunnr add src/EntityFramework.PostgreSQL/NorsePostgresContextExtensions.cs \
	tests/EntityFramework.PostgreSQL.Tests/NorsePostgresContextExtensionsTests.cs
git -C Urdarbrunnr commit -m "$(cat <<'EOF'
Add explicit useSnakeCaseNaming opt-out to Postgres context registration

Default stays true (Postgres's own escape-free native style), but the
choice is now an explicit, overridable parameter rather than baked
into NorseDbContext -- pairs with the SQL Server side landing next,
which defaults false for the same reason in reverse.
EOF
)"
```

---

## Task 3: `Norse.EntityFramework.SqlServer` — new package, mirrors the Postgres trio

**Files:**
- Create: `Urdarbrunnr/src/EntityFramework.SqlServer/EntityFramework.SqlServer.csproj`
- Create: `Urdarbrunnr/src/EntityFramework.SqlServer/NorseSqlServerContextExtensions.cs`
- Create: `Urdarbrunnr/tests/EntityFramework.SqlServer.Tests/EntityFramework.SqlServer.Tests.csproj`
- Create: `Urdarbrunnr/tests/EntityFramework.SqlServer.Tests/NorseSqlServerContextExtensionsTests.cs`

**Interfaces:**
- Consumes: `NorseDbContextOptionsExtensions.ApplyNorseConventions` from Task 1; `INorseDbContext`, `NorseDbContext` from `Norse.EntityFramework`.
- Produces: `AddNorseSqlServerContext<TContext>(builder, connectionStringName, bool useSnakeCaseNaming = false)`
  and `AddNorseSqlServerMigrationContext<TContext>(builder, connectionStringName, migrationsAssemblyName, bool useSnakeCaseNaming = false)`.
  Task 5's SQL Server generator calls `AddNorseSqlServerMigrationContext<T>(name, assemblyName)` with no
  third argument — the default (`false`, native PascalCase) applies.

- [ ] **Step 1: Write the failing tests (new project)**

Create `Urdarbrunnr/tests/EntityFramework.SqlServer.Tests/EntityFramework.SqlServer.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.Extensions.Hosting" Version="*" />
		<ProjectReference Include="../../src/EntityFramework.SqlServer/EntityFramework.SqlServer.csproj" />
	</ItemGroup>
</Project>
```

Create `Urdarbrunnr/tests/EntityFramework.SqlServer.Tests/NorseSqlServerContextExtensionsTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.EntityFramework.SqlServer.Tests;

public sealed class NorseSqlServerContextExtensionsTests
{
	const string ConnectionString = "Server=localhost;Database=test;Trusted_Connection=True;TrustServerCertificate=True;";

	[Fact]
	void AddNorseSqlServerContext_registers_TContext_in_DI()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });

		builder.AddNorseSqlServerContext<TestContext>("test-db");

		var descriptor = builder.Services
			.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();
	}

	[Fact]
	void AddNorseSqlServerMigrationContext_registers_TContext_non_pooled_in_DI()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });

		builder.AddNorseSqlServerMigrationContext<TestContext>("test-db", "Norse.EntityFramework.SqlServer.Tests");

		var descriptor = builder.Services
			.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();

		// AddDbContext registers TContext as a direct type-to-type mapping (ImplementationType set,
		// no factory). AddDbContextPool instead registers TContext via a factory that leases an
		// instance from an internal pool (ImplementationFactory set, ImplementationType null) --
		// this distinguishes non-pooled registration from pooled registration.
		descriptor.ImplementationType.ShouldBe(typeof(TestContext));
		descriptor.ImplementationFactory.ShouldBeNull();
	}

	[Fact]
	void AddNorseSqlServerMigrationContext_does_not_throw_with_mutating_OnConfiguring()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });

		builder.AddNorseSqlServerMigrationContext<TestContext>("test-db", "Norse.EntityFramework.SqlServer.Tests");

		using var host = builder.Build();
		using var scope = host.Services.CreateScope();
		using var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();

		Should.NotThrow(() => _ = ctx.Model);
	}

	[Fact]
	void AddNorseSqlServerMigrationContext_defaults_to_native_PascalCase_naming()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });

		builder.AddNorseSqlServerMigrationContext<TestContext>("test-db", "Norse.EntityFramework.SqlServer.Tests");

		using var host = builder.Build();
		using var scope = host.Services.CreateScope();
		using var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();

		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		tableName.ShouldBe("TestEntities");
	}

	[Fact]
	void AddNorseSqlServerMigrationContext_opts_into_snake_case_naming_when_requested()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });

		builder.AddNorseSqlServerMigrationContext<TestContext>(
			"test-db", "Norse.EntityFramework.SqlServer.Tests", useSnakeCaseNaming: true);

		using var host = builder.Build();
		using var scope = host.Services.CreateScope();
		using var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();

		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		tableName.ShouldBe("test_entities");
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options)
	{
		public DbSet<TestEntity> TestEntities => Set<TestEntity>();
	}

	sealed class TestEntity : INorseEntity<TestEntity>
	{
		public int Id { get; set; }

		[MaxLength(100)]
		public string Name { get; set; } = "";

		public static void Configure(EntityTypeBuilder<TestEntity> builder) { }
	}
}
```

- [ ] **Step 2: Run to confirm failure**

Run: `dotnet test Urdarbrunnr/tests/EntityFramework.SqlServer.Tests/EntityFramework.SqlServer.Tests.csproj`
Expected: Fails to restore/build — the project and its `ProjectReference` target don't exist yet.

- [ ] **Step 3: Implement the new package**

Create `Urdarbrunnr/src/EntityFramework.SqlServer/EntityFramework.SqlServer.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.EntityFramework.SqlServer: canonical Aspire-wired SQL Server context registration via AddNorseSqlServerContext&lt;TContext&gt;. Referenced by web server and worker hosts; pulled in transitively by Norse.EntityFramework.Migrations.SqlServer for the migrations service.</Description>
		<!-- EF Core is not AOT/trim-compatible; override the src-level default. -->
		<IsAotCompatible>false</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Aspire.Microsoft.EntityFrameworkCore.SqlServer" Version="*" />
	</ItemGroup>
	<ItemGroup>
		<ProjectReference Include="../EntityFramework/EntityFramework.csproj" />
	</ItemGroup>
</Project>
```

Create `Urdarbrunnr/src/EntityFramework.SqlServer/NorseSqlServerContextExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.EntityFramework.SqlServer;

/// <summary>
/// Aspire-wired registration extensions for Norse EF SQL Server contexts.
/// </summary>
public static class NorseSqlServerContextExtensions
{
	/// <summary>
	/// Registers <typeparamref name="TContext"/> in the Aspire host using the SQL Server EF Core
	/// integration. The connection string is resolved by <paramref name="connectionStringName"/>
	/// from the application configuration.
	/// </summary>
	/// <typeparam name="TContext">
	/// The <see cref="DbContext"/> type to register. Must implement <see cref="INorseDbContext"/>.
	/// </typeparam>
	/// <param name="builder">The host application builder.</param>
	/// <param name="connectionStringName">The connection string name in application configuration.</param>
	/// <param name="useSnakeCaseNaming">
	/// Whether to apply snake_case table/column naming. Defaults to <see langword="false"/>: SQL
	/// Server's default collation is case-insensitive, so its own raw PascalCase naming already
	/// round-trips without quoting or escaping -- unlike Postgres, there is no engine-native reason
	/// to prefer snake_case here. Pass <see langword="true"/> to opt in anyway (e.g. a deployment
	/// that wants one naming style across both a Postgres and a SQL Server target).
	/// </param>
	/// <returns>The same <paramref name="builder"/> for chaining.</returns>
	public static IHostApplicationBuilder AddNorseSqlServerContext<TContext>(
		this IHostApplicationBuilder builder,
		string connectionStringName,
		bool useSnakeCaseNaming = false)
		where TContext : DbContext, INorseDbContext
	{
		builder.AddSqlServerDbContext<TContext>(connectionStringName,
			configureDbContextOptions: opts =>
			{
				if (useSnakeCaseNaming)
					NorseDbContextOptionsExtensions.ApplyNorseConventions(opts);
			});
		return builder;
	}

	/// <summary>
	/// Registers <typeparamref name="TContext"/> for one-shot, non-pooled use (migrations and other
	/// short-lived init-container work). Unlike <see cref="AddNorseSqlServerContext{TContext}"/>,
	/// this does NOT pool the context — pooling is reserved for long-running runtime hosts (web
	/// server, worker); a migrations service constructs its context once and exits, so pooling only
	/// adds risk (EF Core forbids <c>OnConfiguring</c> from mutating frozen pooled options) for no
	/// benefit. Still gets Aspire's retry policy, health check, and telemetry via
	/// <c>EnrichSqlServerDbContext</c>.
	/// </summary>
	/// <typeparam name="TContext">
	/// The <see cref="DbContext"/> type to register. Must implement <see cref="INorseDbContext"/>.
	/// </typeparam>
	/// <param name="builder">The host application builder.</param>
	/// <param name="connectionStringName">The connection string name in application configuration.</param>
	/// <param name="migrationsAssemblyName">
	/// The name of the assembly containing <typeparamref name="TContext"/>'s EF Core migrations. Norse
	/// convention places migrations in a sibling <c>*.Migrations</c> assembly, never in the context's own
	/// assembly — this must be supplied explicitly rather than inferred, since EF Core defaults to
	/// searching the context's own assembly and finds nothing there.
	/// </param>
	/// <param name="useSnakeCaseNaming">See <see cref="AddNorseSqlServerContext{TContext}"/>.</param>
	/// <returns>The same <paramref name="builder"/> for chaining.</returns>
	public static IHostApplicationBuilder AddNorseSqlServerMigrationContext<TContext>(
		this IHostApplicationBuilder builder,
		string connectionStringName,
		string migrationsAssemblyName,
		bool useSnakeCaseNaming = false)
		where TContext : DbContext, INorseDbContext
	{
		var connectionString = builder.Configuration.GetConnectionString(connectionStringName)
			?? throw new InvalidOperationException($"Connection string '{connectionStringName}' was not found.");

		builder.Services.AddDbContext<TContext>(opts =>
		{
			opts.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(migrationsAssemblyName));
			if (useSnakeCaseNaming)
				NorseDbContextOptionsExtensions.ApplyNorseConventions(opts);
		});
		builder.EnrichSqlServerDbContext<TContext>();

		return builder;
	}
}
```

**Note for the implementer:** `AddSqlServerDbContext<TContext>` and `EnrichSqlServerDbContext<TContext>`
are `Aspire.Microsoft.EntityFrameworkCore.SqlServer`'s published API names, mirroring
`AddNpgsqlDbContext`/`EnrichNpgsqlDbContext` from the Postgres package exactly (confirmed the package
exists on nuget.org, owned by `aspire`/`Microsoft`). If the restored package's actual method names differ
in a patch release, adjust the two call sites — the rest of the design is unaffected either way.

- [ ] **Step 4: Run to confirm pass**

Run: `dotnet test Urdarbrunnr/tests/EntityFramework.SqlServer.Tests/EntityFramework.SqlServer.Tests.csproj`
Expected: PASS, all five tests.

- [ ] **Step 5: Commit**

```bash
git -C Urdarbrunnr add src/EntityFramework.SqlServer tests/EntityFramework.SqlServer.Tests
git -C Urdarbrunnr commit -m "$(cat <<'EOF'
Add Norse.EntityFramework.SqlServer

Mirrors Norse.EntityFramework.PostgreSQL: AddNorseSqlServerContext
(pooled runtime) and AddNorseSqlServerMigrationContext (non-pooled
migrations), both with the same useSnakeCaseNaming opt-in/opt-out
shape, defaulting false -- SQL Server's own case-insensitive collation
means raw PascalCase already round-trips without escaping.
EOF
)"
```

---

## Task 4: Extract shared migration-contributor discovery logic (pure refactor)

Prep step before Task 5. `MigrationContributorGenerator.cs` in the Postgres migrations generator is ~140
lines, of which only two emitted lines (the `using` namespace and the `AddNorsePostgresMigrationContext`
call) are provider-specific. Extract everything else to a linked shared source file so the SQL Server
generator in Task 5 doesn't duplicate it. **No behavior change** — the existing generator tests are the
regression guard; they must still pass unchanged, byte-for-byte, after this task.

**Files:**
- Create: `Urdarbrunnr/src/EntityFramework.Migrations.Generator.Shared/MigrationContributorDiscovery.cs`
- Modify: `Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL.Generator/MigrationContributorGenerator.cs`
- Modify: `Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj`

**Interfaces:**
- Produces: `Norse.EntityFramework.Migrations.Generator.Shared.MigrationContributorDiscovery.FindContributors(Compilation)`
  returning `IList<ContributorInfo>`, and the `ContributorInfo` type itself (`ContributorType`,
  `ContextType`, `ConnectionStringName`, `MigrationsAssemblyName` — all `string`). Task 5's SQL Server
  generator links the same file and consumes both.

- [ ] **Step 1: Confirm the existing tests pass before touching anything (baseline)**

Run: `dotnet test Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests.csproj`
Expected: PASS (both `Generator_produces_AddNorseMigrations_method` and
`Generator_emits_no_source_when_no_contributors_found`). This is the safety net the refactor must not break.

- [ ] **Step 2: Create the shared discovery file**

Create `Urdarbrunnr/src/EntityFramework.Migrations.Generator.Shared/MigrationContributorDiscovery.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace Norse.EntityFramework.Migrations.Generator.Shared;

// Linked into both EntityFramework.Migrations.PostgreSQL.Generator and
// EntityFramework.Migrations.SqlServer.Generator via <Compile Include> -- provider-agnostic
// discovery of EfMigrationContributor<TContext> implementations. Roslyn generators can't reference
// other analyzer-only assemblies, so this is plain shared source (compiled once per consuming
// assembly), not a shared package reference.
static class MigrationContributorDiscovery
{
	const string AttributeMetadataName =
		"Norse.EntityFramework.Migrations.MigrationConnectionStringAttribute";

	const string ContributorInterfaceMetadataName =
		"Norse.Abstractions.Migrations.IMigrationContributor";

	public static IList<ContributorInfo> FindContributors(Compilation compilation)
	{
		IList<ContributorInfo> results = [];

		foreach (var type in AllTypes(compilation))
		{
			if (type.IsAbstract)
				continue;

			if (!ImplementsContributorInterface(type))
				continue;

			var attr = type.GetAttributes()
				.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AttributeMetadataName);

			if (attr is null || attr.ConstructorArguments.Length == 0)
				continue;

			var connectionStringName = attr.ConstructorArguments[0].Value as string;
			if (connectionStringName is null)
				continue;

			var dbContextType = FindEfContextType(type);
			if (dbContextType is null)
				continue;

			results.Add(new ContributorInfo(
				type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
				dbContextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
				connectionStringName,
				type.ContainingAssembly.Name));
		}

		return results;
	}

	// Covers both production (contributors in referenced packages) and
	// test scenarios (contributor defined in compilation source trees).
	static IEnumerable<INamedTypeSymbol> AllTypes(Compilation compilation)
	{
		foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
			foreach (var type in GetAllTypes(assembly.GlobalNamespace))
				yield return type;

		foreach (var type in GetAllTypes(compilation.Assembly.GlobalNamespace))
			yield return type;
	}

	// Match by metadata name to avoid cross-layer symbol identity issues in the
	// generator's CompilationProvider context.
	static bool ImplementsContributorInterface(INamedTypeSymbol type) =>
		type.AllInterfaces.Any(i => i.ToDisplayString() == ContributorInterfaceMetadataName);

	static INamedTypeSymbol? FindEfContextType(INamedTypeSymbol type)
	{
		var current = type.BaseType;
		while (current is not null)
		{
			if (current.OriginalDefinition?.MetadataName == "EfMigrationContributor`1" &&
				current.OriginalDefinition?.ContainingNamespace?.ToDisplayString() == "Norse.EntityFramework.Migrations" &&
				current.TypeArguments.Length == 1)
			{
				return current.TypeArguments[0] as INamedTypeSymbol;
			}

			current = current.BaseType;
		}

		return null;
	}

	static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
	{
		foreach (var type in ns.GetTypeMembers())
			yield return type;

		foreach (var child in ns.GetNamespaceMembers())
			foreach (var type in GetAllTypes(child))
				yield return type;
	}
}

readonly struct ContributorInfo
{
	public ContributorInfo(
		string contributorType,
		string contextType,
		string connectionStringName,
		string migrationsAssemblyName)
	{
		ContributorType = contributorType;
		ContextType = contextType;
		ConnectionStringName = connectionStringName;
		MigrationsAssemblyName = migrationsAssemblyName;
	}

	public string ContributorType { get; }
	public string ContextType { get; }
	public string ConnectionStringName { get; }
	public string MigrationsAssemblyName { get; }
}
```

- [ ] **Step 3: Slim `MigrationContributorGenerator.cs` down to the provider-specific two lines**

Replace `Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL.Generator/MigrationContributorGenerator.cs` in full:

```csharp
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Norse.EntityFramework.Migrations.Generator.Shared;

namespace Norse.EntityFramework.Migrations.PostgreSQL.Generator;

[Generator]
public sealed class MigrationContributorGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var contributors = context.CompilationProvider.Select(static (compilation, _) =>
			MigrationContributorDiscovery.FindContributors(compilation));

		context.RegisterSourceOutput(contributors, static (ctx, list) =>
		{
			if (list.Count == 0)
				return;

			var source = BuildSource(list);
			ctx.AddSource("NorseMigrationsExtensions.g.cs", SourceText.From(source, Encoding.UTF8));
		});
	}

	static string BuildSource(IList<ContributorInfo> contributors)
	{
		StringBuilder sb = new();
		sb.AppendLine("// <auto-generated />");
		sb.AppendLine("#nullable enable");
		sb.AppendLine("using Microsoft.EntityFrameworkCore;");
		sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
		sb.AppendLine("using Microsoft.Extensions.Hosting;");
		sb.AppendLine("using Norse.Abstractions.Migrations;");
		sb.AppendLine("using Norse.EntityFramework.PostgreSQL;");
		sb.AppendLine("using Norse.Infrastructure.Migrations;");
		sb.AppendLine();
		sb.AppendLine("static class NorseMigrationsGeneratedExtensions");
		sb.AppendLine("{");
		sb.AppendLine("\tpublic static global::Microsoft.Extensions.Hosting.IHostApplicationBuilder AddNorseMigrations(");
		sb.AppendLine("\t\tthis global::Microsoft.Extensions.Hosting.IHostApplicationBuilder builder)");
		sb.AppendLine("\t{");

		foreach (var c in contributors)
		{
			sb.AppendLine($"\t\tbuilder.AddNorsePostgresMigrationContext<{c.ContextType}>(\"{c.ConnectionStringName}\", \"{c.MigrationsAssemblyName}\");");
			sb.AppendLine($"\t\tbuilder.Services.AddTransient<global::Norse.Abstractions.Migrations.IMigrationContributor, {c.ContributorType}>();");
		}

		sb.AppendLine("\t\tbuilder.AddNorseMigrationsRunner();");
		sb.AppendLine("\t\treturn builder;");
		sb.AppendLine("\t}");
		sb.AppendLine("}");

		return sb.ToString();
	}
}
```

Add the linked-source item to `Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj`
— insert a new `<ItemGroup>` before the closing `</Project>` tag:

```xml
	<ItemGroup>
		<Compile Include="../EntityFramework.Migrations.Generator.Shared/MigrationContributorDiscovery.cs" Link="Shared/MigrationContributorDiscovery.cs" />
	</ItemGroup>
```

- [ ] **Step 4: Run the existing generator tests to confirm the refactor is behavior-preserving**

Run: `dotnet test Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests.csproj`
Expected: PASS, same two tests as Step 1, unchanged assertions, unchanged results.

- [ ] **Step 5: Build the full solution**

Run: `dotnet build Urdarbrunnr/Urdarbrunnr.slnx`
Expected: Build succeeds. (The new shared-source project has no `.csproj` of its own — it's a plain
folder of linked source, so it won't appear as a separate build output; Task 7 adds it as a solution
item for visibility, not as a project.)

- [ ] **Step 6: Commit**

```bash
git -C Urdarbrunnr add src/EntityFramework.Migrations.Generator.Shared \
	src/EntityFramework.Migrations.PostgreSQL.Generator/MigrationContributorGenerator.cs \
	src/EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj
git -C Urdarbrunnr commit -m "$(cat <<'EOF'
Extract provider-agnostic migration-contributor discovery to shared source

Pure refactor, no behavior change -- existing generator tests pass
unchanged. Prep for the SQL Server migrations generator, which links
the same file instead of duplicating ~130 lines of symbol-walking logic.
EOF
)"
```

---

## Task 5: `Norse.EntityFramework.Migrations.SqlServer.Generator` — new generator project

**Files:**
- Create: `Urdarbrunnr/src/EntityFramework.Migrations.SqlServer.Generator/EntityFramework.Migrations.SqlServer.Generator.csproj`
- Create: `Urdarbrunnr/src/EntityFramework.Migrations.SqlServer.Generator/MigrationContributorGenerator.cs`
- Create: `Urdarbrunnr/tests/EntityFramework.Migrations.SqlServer.Generator.Tests/EntityFramework.Migrations.SqlServer.Generator.Tests.csproj`
- Create: `Urdarbrunnr/tests/EntityFramework.Migrations.SqlServer.Generator.Tests/MigrationContributorGeneratorTests.cs`

**Interfaces:**
- Consumes: `Norse.EntityFramework.Migrations.Generator.Shared.MigrationContributorDiscovery` from Task 4 (linked source, not a package reference).
- Produces: generated `AddNorseMigrations()` extension calling `AddNorseSqlServerMigrationContext<T>(...)` — consumed by a migrations service that references `Norse.EntityFramework.Migrations.SqlServer` (Task 6).

- [ ] **Step 1: Write the failing tests (new project)**

Create `Urdarbrunnr/tests/EntityFramework.Migrations.SqlServer.Generator.Tests/EntityFramework.Migrations.SqlServer.Generator.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="*">
			<PrivateAssets>all</PrivateAssets>
		</PackageReference>
		<ProjectReference Include="../../src/EntityFramework/EntityFramework.csproj" />
		<ProjectReference Include="../../src/EntityFramework.Migrations/EntityFramework.Migrations.csproj" />
		<ProjectReference Include="../../src/EntityFramework.Migrations.SqlServer.Generator/EntityFramework.Migrations.SqlServer.Generator.csproj" />
	</ItemGroup>
</Project>
```

Create `Urdarbrunnr/tests/EntityFramework.Migrations.SqlServer.Generator.Tests/MigrationContributorGeneratorTests.cs`
— identical in shape to the Postgres generator's test, asserting the SQL Server-specific emitted call:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Norse.EntityFramework.Migrations.SqlServer.Generator.Tests;

public sealed class MigrationContributorGeneratorTests
{
	[Fact]
	void Generator_produces_AddNorseMigrations_method()
	{
		var source = """
			using Norse.EntityFramework;
			using Norse.EntityFramework.Migrations;
			using Microsoft.EntityFrameworkCore;

			[MigrationConnectionString("test-db")]
			sealed class TestContributor(TestContext ctx) : EfMigrationContributor<TestContext>(ctx)
			{
				public override string Name => "Test";
			}

			sealed class TestContext(DbContextOptions<TestContext> opts) : NorseDbContext(opts);
			""";

		var compilation = CreateCompilation(source);
		var generator = new MigrationContributorGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		result.GeneratedTrees.Length.ShouldBe(1);
		var generated = result.GeneratedTrees[0].ToString();
		generated.ShouldContain("AddNorseMigrations");
		generated.ShouldContain("AddNorseMigrationsRunner");
		generated.ShouldContain("TestContributor");
		generated.ShouldContain("test-db");
		generated.ShouldContain("AddNorseSqlServerMigrationContext");
		generated.ShouldContain("\"TestAssembly\"");
	}

	[Fact]
	void Generator_emits_no_source_when_no_contributors_found()
	{
		var compilation = CreateCompilation("// empty");
		var generator = new MigrationContributorGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		result.GeneratedTrees.ShouldBeEmpty();
	}

	static Compilation CreateCompilation(string source)
	{
		// Build metadata references from explicit assembly locations — AppDomain.GetAssemblies()
		// is unreliable in .NET 11 due to metadata pre-sharing; typeof().Assembly.Location is stable.
		// In .NET 5+ the public Attribute/Object surface lives in System.Runtime.dll (a facade), not
		// System.Private.CoreLib — both must be present for Roslyn to bind attribute constructors.
		var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
		var references = new[]
		{
			typeof(object),
			typeof(Norse.EntityFramework.Migrations.MigrationConnectionStringAttribute),
			typeof(Norse.EntityFramework.NorseDbContext),
			typeof(Norse.Abstractions.Migrations.IMigrationContributor),
			typeof(Microsoft.EntityFrameworkCore.DbContext),
		}
		.Select(t => MetadataReference.CreateFromFile(t.Assembly.Location))
		.Cast<MetadataReference>()
		.Append(MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")))
		.ToList();

		return CSharpCompilation.Create(
			"TestAssembly",
			[CSharpSyntaxTree.ParseText(source)],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
	}
}
```

- [ ] **Step 2: Run to confirm failure**

Run: `dotnet test Urdarbrunnr/tests/EntityFramework.Migrations.SqlServer.Generator.Tests/EntityFramework.Migrations.SqlServer.Generator.Tests.csproj`
Expected: Fails to restore/build — the generator project doesn't exist yet.

- [ ] **Step 3: Implement the new generator project**

Create `Urdarbrunnr/src/EntityFramework.Migrations.SqlServer.Generator/EntityFramework.Migrations.SqlServer.Generator.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.EntityFramework.Migrations.SqlServer.Generator: the Roslyn IIncrementalGenerator that discovers EfMigrationContributor&lt;TContext&gt; implementations at compile time and emits AddNorseMigrations() with SQL Server connection wiring.</Description>
		<TargetFramework>netstandard2.0</TargetFramework>
		<IsRoslynComponent>true</IsRoslynComponent>
		<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
		<IsAotCompatible>false</IsAotCompatible>
		<IsPackable>false</IsPackable>
		<NoWarn>$(NoWarn);CS1591</NoWarn>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="*">
			<PrivateAssets>all</PrivateAssets>
		</PackageReference>
		<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="*">
			<PrivateAssets>all</PrivateAssets>
		</PackageReference>
	</ItemGroup>
	<ItemGroup>
		<Compile Include="../EntityFramework.Migrations.Generator.Shared/MigrationContributorDiscovery.cs" Link="Shared/MigrationContributorDiscovery.cs" />
	</ItemGroup>
</Project>
```

Create `Urdarbrunnr/src/EntityFramework.Migrations.SqlServer.Generator/MigrationContributorGenerator.cs`:

```csharp
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Norse.EntityFramework.Migrations.Generator.Shared;

namespace Norse.EntityFramework.Migrations.SqlServer.Generator;

[Generator]
public sealed class MigrationContributorGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var contributors = context.CompilationProvider.Select(static (compilation, _) =>
			MigrationContributorDiscovery.FindContributors(compilation));

		context.RegisterSourceOutput(contributors, static (ctx, list) =>
		{
			if (list.Count == 0)
				return;

			var source = BuildSource(list);
			ctx.AddSource("NorseMigrationsExtensions.g.cs", SourceText.From(source, Encoding.UTF8));
		});
	}

	static string BuildSource(IList<ContributorInfo> contributors)
	{
		StringBuilder sb = new();
		sb.AppendLine("// <auto-generated />");
		sb.AppendLine("#nullable enable");
		sb.AppendLine("using Microsoft.EntityFrameworkCore;");
		sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
		sb.AppendLine("using Microsoft.Extensions.Hosting;");
		sb.AppendLine("using Norse.Abstractions.Migrations;");
		sb.AppendLine("using Norse.EntityFramework.SqlServer;");
		sb.AppendLine("using Norse.Infrastructure.Migrations;");
		sb.AppendLine();
		sb.AppendLine("static class NorseMigrationsGeneratedExtensions");
		sb.AppendLine("{");
		sb.AppendLine("\tpublic static global::Microsoft.Extensions.Hosting.IHostApplicationBuilder AddNorseMigrations(");
		sb.AppendLine("\t\tthis global::Microsoft.Extensions.Hosting.IHostApplicationBuilder builder)");
		sb.AppendLine("\t{");

		foreach (var c in contributors)
		{
			sb.AppendLine($"\t\tbuilder.AddNorseSqlServerMigrationContext<{c.ContextType}>(\"{c.ConnectionStringName}\", \"{c.MigrationsAssemblyName}\");");
			sb.AppendLine($"\t\tbuilder.Services.AddTransient<global::Norse.Abstractions.Migrations.IMigrationContributor, {c.ContributorType}>();");
		}

		sb.AppendLine("\t\tbuilder.AddNorseMigrationsRunner();");
		sb.AppendLine("\t\treturn builder;");
		sb.AppendLine("\t}");
		sb.AppendLine("}");

		return sb.ToString();
	}
}
```

- [ ] **Step 4: Run to confirm pass**

Run: `dotnet test Urdarbrunnr/tests/EntityFramework.Migrations.SqlServer.Generator.Tests/EntityFramework.Migrations.SqlServer.Generator.Tests.csproj`
Expected: PASS, both tests.

- [ ] **Step 5: Commit**

```bash
git -C Urdarbrunnr add src/EntityFramework.Migrations.SqlServer.Generator \
	tests/EntityFramework.Migrations.SqlServer.Generator.Tests
git -C Urdarbrunnr commit -m "$(cat <<'EOF'
Add Norse.EntityFramework.Migrations.SqlServer.Generator

Mirrors the Postgres migrations generator, linking the shared
discovery source from Task 4 instead of duplicating it -- only the
emitted namespace and AddNorseSqlServerMigrationContext call differ.
EOF
)"
```

---

## Task 6: `Norse.EntityFramework.Migrations.SqlServer` — meta-package

Packaging-only project, same shape as `EntityFramework.Migrations.PostgreSQL.csproj` — no C# source of
its own, so this task is build-verified rather than TDD (there is no behavior to red/green test, only
a package composition to prove out).

**Files:**
- Create: `Urdarbrunnr/src/EntityFramework.Migrations.SqlServer/EntityFramework.Migrations.SqlServer.csproj`

**Interfaces:**
- Consumes: `EntityFramework.Migrations.csproj` (Task 1, unchanged), `EntityFramework.SqlServer.csproj` (Task 3), `EntityFramework.Migrations.SqlServer.Generator.csproj` (Task 5).
- Produces: the single package a migrations service references for SQL Server, matching what `Norse.EntityFramework.Migrations.PostgreSQL` already does for Postgres.

- [ ] **Step 1: Create the project**

Create `Urdarbrunnr/src/EntityFramework.Migrations.SqlServer/EntityFramework.Migrations.SqlServer.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.EntityFramework.Migrations.SqlServer: pulls in Norse.EntityFramework.Migrations (contributor base) and Norse.EntityFramework.SqlServer (Aspire SQL Server wiring) and ships the Roslyn generator that emits AddNorseMigrations(). Reference this single package from your migrations service.</Description>
		<!-- EF Core is not AOT/trim-compatible; override the src-level default. -->
		<IsAotCompatible>false</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../EntityFramework.Migrations/EntityFramework.Migrations.csproj" />
		<ProjectReference Include="../EntityFramework.SqlServer/EntityFramework.SqlServer.csproj" />
		<ProjectReference
			Include="../EntityFramework.Migrations.SqlServer.Generator/EntityFramework.Migrations.SqlServer.Generator.csproj"
			OutputItemType="Analyzer"
			ReferenceOutputAssembly="false" />
	</ItemGroup>
	<Target Name="IncludeGeneratorInPackage" BeforeTargets="_GetPackageFiles">
		<MSBuild Projects="../EntityFramework.Migrations.SqlServer.Generator/EntityFramework.Migrations.SqlServer.Generator.csproj"
			Targets="Build"
			Properties="Configuration=$(Configuration)" />
		<ItemGroup>
			<None Include="../EntityFramework.Migrations.SqlServer.Generator/bin/$(Configuration)/netstandard2.0/Norse.EntityFramework.Migrations.SqlServer.Generator.dll"
				Pack="true"
				PackagePath="analyzers/dotnet/cs/"
				Visible="false" />
		</ItemGroup>
	</Target>
</Project>
```

- [ ] **Step 2: Build and pack to verify the composition**

Run: `dotnet build Urdarbrunnr/src/EntityFramework.Migrations.SqlServer/EntityFramework.Migrations.SqlServer.csproj`
Expected: Build succeeds.

Run: `dotnet pack Urdarbrunnr/src/EntityFramework.Migrations.SqlServer/EntityFramework.Migrations.SqlServer.csproj -c Release`
Expected: Pack succeeds and produces `Norse.EntityFramework.Migrations.SqlServer.<version>.nupkg`. Verify
the analyzer DLL landed correctly:

Run: `unzip -l Urdarbrunnr/src/EntityFramework.Migrations.SqlServer/bin/Release/Norse.EntityFramework.Migrations.SqlServer.*.nupkg | grep analyzers`
Expected: Lists `analyzers/dotnet/cs/Norse.EntityFramework.Migrations.SqlServer.Generator.dll`.

- [ ] **Step 3: Commit**

```bash
git -C Urdarbrunnr add src/EntityFramework.Migrations.SqlServer
git -C Urdarbrunnr commit -m "$(cat <<'EOF'
Add Norse.EntityFramework.Migrations.SqlServer meta-package

Bundles EntityFramework.Migrations + EntityFramework.SqlServer + the
SQL Server migrations generator into the single package a migrations
service references, mirroring the Postgres meta-package exactly.
EOF
)"
```

---

## Task 7: Solution file, CLAUDE.md state-of-the-union

**Files:**
- Modify: `Urdarbrunnr/Urdarbrunnr.slnx`
- Modify: `Urdarbrunnr/CLAUDE.md`

**Interfaces:**
- None — visibility/documentation only, no code interfaces produced or consumed.

- [ ] **Step 1: Add the five new projects to the solution**

Modify `Urdarbrunnr/Urdarbrunnr.slnx` — replace its full contents:

```xml
<Solution>
	<Folder Name="/Solution Items/">
		<File Path=".editorconfig" />
		<File Path=".gitattributes" />
		<File Path=".gitignore" />
		<File Path="CLAUDE.md" />
		<File Path="Directory.Build.props" />
		<File Path="global.json" />
		<File Path="LICENSE" />
		<File Path="nuget.config" />
		<File Path="README.md" />
	</Folder>
	<Folder Name="/src/">
		<File Path="src/Directory.Build.props" />
		<File Path="src/Directory.Build.targets" />
		<Project Path="src/EntityFramework/EntityFramework.csproj" />
		<Project Path="src/EntityFramework.PostgreSQL/EntityFramework.PostgreSQL.csproj" />
		<Project Path="src/EntityFramework.SqlServer/EntityFramework.SqlServer.csproj" />
		<Project Path="src/EntityFramework.Migrations/EntityFramework.Migrations.csproj" />
		<File Path="src/EntityFramework.Migrations.Generator.Shared/MigrationContributorDiscovery.cs" />
		<Project Path="src/EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj" />
		<Project Path="src/EntityFramework.Migrations.PostgreSQL/EntityFramework.Migrations.PostgreSQL.csproj" />
		<Project Path="src/EntityFramework.Migrations.SqlServer.Generator/EntityFramework.Migrations.SqlServer.Generator.csproj" />
		<Project Path="src/EntityFramework.Migrations.SqlServer/EntityFramework.Migrations.SqlServer.csproj" />
		<Project Path="src/EntityFramework.Generator/EntityFramework.Generator.csproj" />
	</Folder>
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<File Path="tests/Directory.Build.targets" />
		<Project Path="tests/EntityFramework.Tests/EntityFramework.Tests.csproj" />
		<Project Path="tests/EntityFramework.PostgreSQL.Tests/EntityFramework.PostgreSQL.Tests.csproj" />
		<Project Path="tests/EntityFramework.SqlServer.Tests/EntityFramework.SqlServer.Tests.csproj" />
		<Project Path="tests/EntityFramework.Migrations.Tests/EntityFramework.Migrations.Tests.csproj" />
		<Project Path="tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests.csproj" />
		<Project Path="tests/EntityFramework.Migrations.SqlServer.Generator.Tests/EntityFramework.Migrations.SqlServer.Generator.Tests.csproj" />
		<Project Path="tests/EntityFramework.Generator.Tests/EntityFramework.Generator.Tests.csproj" />
	</Folder>
</Solution>
```

- [ ] **Step 2: Verify the solution opens/builds with the updated file**

Run: `dotnet build Urdarbrunnr/Urdarbrunnr.slnx`
Expected: Build succeeds, all projects including the five new ones participate.

- [ ] **Step 3: Update the CLAUDE.md state-of-the-union**

In `Urdarbrunnr/CLAUDE.md`, in the "Four assemblies are live" bullet list (§1), add two new bullets after
the `Norse.EntityFramework.Migrations.PostgreSQL` one, and update the section's lead-in sentence from
"Four assemblies are live" to reflect the new count:

Find this text:
```
**Four assemblies are live**, shipped, tagged, and published to NuGet as Tasks 3–6 of the cross-realm
migrations framework rollout (`../Glitnir/docs/Platform/plans/2026-06-28-migrations-framework-identity-schema.md`):
```

Replace with:
```
**Four assemblies are live**, shipped, tagged, and published to NuGet as Tasks 3–6 of the cross-realm
migrations framework rollout (`../Glitnir/docs/Platform/plans/2026-06-28-migrations-framework-identity-schema.md`).
A SQL Server-parallel trio (`Norse.EntityFramework.SqlServer`, `.Migrations.SqlServer`,
`.Migrations.SqlServer.Generator`) landed alongside them per
`../Glitnir/docs/Urdarbrunnr/specs/2026-07-03-provider-aware-length-and-naming-conventions.md` — `[FixedLength(n)]`
now only translates to `.IsFixedLength()` on SQL Server (Postgres's own docs say `character(n)` has no
storage/performance benefit over `character varying(n)` there), and snake_case naming is an explicit,
overridable opt-in/opt-out on every provider's registration extension rather than baked into
`NorseDbContext`:
```

- [ ] **Step 4: Commit**

```bash
git -C Urdarbrunnr add Urdarbrunnr.slnx CLAUDE.md
git -C Urdarbrunnr commit -m "$(cat <<'EOF'
Wire new SQL Server projects into the solution, update CLAUDE.md

Boy-scout law: state-of-the-union note reflects the SQL Server trio
landing alongside the existing Postgres one.
EOF
)"
```

---

## Task 8: Himinbjörg companion edit

`NorseIdentityDbContext` cannot inherit `NorseDbContext` (it inherits `IdentityDbContext` for ASP.NET
Core Identity), so it independently replicates the two conventions Task 1 changed. This keeps Himinbjörg
compiling and correct against the new Urdarbrunnr contract — it does not change anything else about
Himinbjörg's behavior.

**Files:**
- Modify: `Himinbjorg/src/Identity/NorseIdentityDbContext.cs`

**Interfaces:**
- Consumes: `NorseModelConventions.Apply(ModelConfigurationBuilder, bool)` and `NorseDbContextOptionsExtensions.SqlServerProviderName` from Task 1.

- [ ] **Step 1: Confirm the current failure**

Run: `dotnet build Himinbjorg/Himinbjorg.slnx`
Expected: Build error in `NorseIdentityDbContext.cs` — `NorseModelConventions.Apply(ModelConfigurationBuilder)`
no longer exists (Task 1 made the second parameter required).

- [ ] **Step 2: Update the two call sites**

In `Himinbjorg/src/Identity/NorseIdentityDbContext.cs`, remove the `ApplyNorseConventions` call from
`OnConfiguring` (naming is now a provider-registration-site decision, never a context's own job) — find:

```csharp
		if (!optionsBuilder.Options.IsFrozen)
		{
			NorseDbContextOptionsExtensions.ApplyNorseConventions(optionsBuilder);
			optionsBuilder.UseApplicationServiceProvider(_fallbackIdentityServices);
		}
```

Replace with:

```csharp
		if (!optionsBuilder.Options.IsFrozen)
			optionsBuilder.UseApplicationServiceProvider(_fallbackIdentityServices);
```

Update `ConfigureConventions` to pass the new required parameter — find:

```csharp
	/// <inheritdoc />
	protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
	{
		base.ConfigureConventions(configurationBuilder);
		NorseModelConventions.Apply(configurationBuilder);
	}
```

Replace with:

```csharp
	/// <inheritdoc />
	protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
	{
		base.ConfigureConventions(configurationBuilder);

		// Fixed-length storage (char(n)/nchar(n)) only pays off on SQL Server -- see
		// Norse.EntityFramework.FixedLengthAttribute's remarks.
		NorseModelConventions.Apply(configurationBuilder,
			applyFixedLength: Database.ProviderName == NorseDbContextOptionsExtensions.SqlServerProviderName);
	}
```

Also update the class's XML doc comment, which currently says it "Applies snake_case naming
conventions via `ApplyNorseConventions` since it cannot inherit `NorseDbContext` directly" — that's no
longer true of `NorseIdentityDbContext` on its own (naming is the registration extension's job now).
Find:

```csharp
/// <summary>
/// Norse platform Identity <see cref="IdentityDbContext{TUser,TRole,TKey,TUserClaim,TUserRole,TUserLogin,TRoleClaim,TUserToken,TUserPasskey}"/>,
/// combining ASP.NET Core Identity and OpenIddict entity sets. Applies snake_case naming conventions
/// via <see cref="NorseDbContextOptionsExtensions.ApplyNorseConventions"/> since it cannot inherit
/// <see cref="NorseDbContext"/> directly.
/// </summary>
```

Replace with:

```csharp
/// <summary>
/// Norse platform Identity <see cref="IdentityDbContext{TUser,TRole,TKey,TUserClaim,TUserRole,TUserLogin,TRoleClaim,TUserToken,TUserPasskey}"/>,
/// combining ASP.NET Core Identity and OpenIddict entity sets. Naming conventions are applied by
/// whichever provider registration extension registers this context (see
/// <c>Norse.EntityFramework.PostgreSQL.NorsePostgresContextExtensions</c> and its SQL Server
/// counterpart) — this class replicates <see cref="NorseDbContext"/>'s fixed-length provider check
/// independently since it inherits <c>IdentityDbContext</c>, not <see cref="NorseDbContext"/>.
/// </summary>
```

- [ ] **Step 3: Run Himinbjörg's own test suite**

Run: `dotnet test Himinbjorg/Himinbjorg.slnx`
Expected: PASS. In particular, `NorseUserConfigureTests.Configure_bounds_SecurityStamp_without_converting_it`
(`Himinbjorg/tests/Identity.Tests/NorseUserConfigureTests.cs:41-55`) must still pass — it asserts
`SecurityStamp` gets `IsFixedLength().ShouldNotBe(true)`, which is now true automatically for any
non-SQL-Server provider rather than because `SecurityStamp` avoids `[FixedLength]` by hand.

- [ ] **Step 4: Commit**

```bash
git -C Himinbjorg add src/Identity/NorseIdentityDbContext.cs
git -C Himinbjorg commit -m "$(cat <<'EOF'
Follow Urdarbrunnr's provider-aware convention contract

NorseModelConventions.Apply now requires an applyFixedLength argument;
NorseIdentityDbContext computes it the same way NorseDbContext does.
Naming conventions are no longer this context's job -- they're decided
at the provider registration call site, so the unconditional
ApplyNorseConventions() call in OnConfiguring is removed.
EOF
)"
```

---

## Task 9: Full cross-repo verification

**Files:** none (verification only).

- [ ] **Step 1: Build and test all of Urdarbrunnr**

Run: `dotnet build Urdarbrunnr/Urdarbrunnr.slnx`
Expected: Build succeeds, zero warnings (warnings are errors platform-wide).

Run: `dotnet test Urdarbrunnr/Urdarbrunnr.slnx`
Expected: All tests pass across all ten test projects (five pre-existing, five touched/added by this plan).

- [ ] **Step 2: Build and test Himinbjörg**

Run: `dotnet build Himinbjorg/Himinbjorg.slnx`
Expected: Build succeeds.

Run: `dotnet test Himinbjorg/Himinbjorg.slnx`
Expected: All tests pass, including `NorseUserConfigureTests`.

- [ ] **Step 3: Build the full Bifrost AppHost to confirm nothing upstream broke**

Run: `dotnet build Bifrost.slnx` (from the Bifrost repo root)
Expected: Build succeeds — this exercises `UseProjectReferences` mode across every submodule at once,
the earliest point a cross-repo break would surface.

- [ ] **Step 4: Report completion**

No commit for this task — it's a verification gate. If every build/test above is green, this plan is
complete: `[FixedLength(n)]` and table/column naming are both provider-aware, SQL Server has full
package parity with PostgreSQL, and the pre-existing naming-convention bug is fixed everywhere it
appeared. Proving the bifurcation against a real running SQL Server instance ("test the mettle in
Himinbjörg") remains separate future work, per the spec's stated scope.

---

## Self-Review Notes

**Spec coverage:**
- §1 FixedLength bifurcation → Task 1.
- §2 Naming-convention fix (final, symmetric, per-provider defaults) → Tasks 1–3.
- §3 New SQL Server packages, full parity, shared generator source → Tasks 3, 4, 5, 6.
- §4 Testing approach (explicit flags, no live DB needed) → woven through every task's test steps.
- Documentation updates required (spec's own list) → `FixedLengthAttribute.cs`/`NorseModelConventions.cs`/`NorseDbContext.cs`/`NorseDbContextOptionsExtensions.cs` doc updates in Task 1; `NorsePostgresContextExtensions.cs` in Task 2; CLAUDE.md in Task 7.
- Open item 1 (verify `Database.ProviderName` timing) → Task 1 Step 2/4 is exactly that red/green proof, using the real SQLite and SQL Server providers rather than assuming.
- Open item 2 (does the SQL Server generator need its own dedicated shared-logic test project) → resolved during planning: no, its own `MigrationContributorGeneratorTests.cs` (Task 5) exercises the shared discovery logic transitively, same as the Postgres one already does.

**Placeholder scan:** none found — every step has complete file contents, exact commands, and expected output.

**Type consistency:** `applyFixedLength` (bool, no default) is spelled identically in `RequireExplicitLengthConvention`'s constructor, `NorseModelConventions.Apply`, `NorseDbContext.ConfigureConventions`, and Himinbjörg's `NorseIdentityDbContext.ConfigureConventions`. `useSnakeCaseNaming` (bool, default `true` on Postgres methods / `false` on SQL Server methods) is spelled identically across `NorsePostgresContextExtensions` and `NorseSqlServerContextExtensions`. `ContributorInfo`'s four `string` properties (`ContributorType`, `ContextType`, `ConnectionStringName`, `MigrationsAssemblyName`) are used with the same names and order in both generator projects' `BuildSource`.
