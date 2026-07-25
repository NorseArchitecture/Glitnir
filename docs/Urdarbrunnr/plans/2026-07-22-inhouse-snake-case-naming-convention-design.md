# Norse.EntityFramework In-House Snake_Case Naming Convention Implementation Plan

**Amendment (2026-07-25):** every `Norse.EntityFramework`/`src/EntityFramework*` reference below (title
included) predates the widening rename to `Norse.Persistence.EntityFramework`/`src/Persistence.EntityFramework*`
(PR #31, merged 2026-07-22, shipped v0.0.4) — the code this plan produced lives under the new namespace
and path today; see the paired spec's own 2026-07-25 amendment for the confirming file path.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (default for this repo) paired with superpowers:test-driven-development on every task — never one without the other. `superpowers:executing-plans` is the narrow separate-session fallback only; do not substitute it silently. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the stubbed-out `NorseDbContextOptionsExtensions.ApplyNorseConventions` (dead since `EFCore.NamingConventions` was pulled for EF Core 11 compatibility) with an in-house snake_case naming convention, ported from the pasted `SnakeCaseNameRewriter` algorithm and prior-art model-walking code, wired through EF Core's real convention-plugin pipeline.

**Architecture:** A pure static rewrite function (`SnakeCaseNameRewriter`), a provider-neutral `IModelFinalizingConvention` that walks every EF metadata object and calls it, and an additive `IDbContextOptionsExtension`/`IConventionSetPlugin` pair that registers the convention from `ApplyNorseConventions(DbContextOptionsBuilder)` exactly as before. SQL-Server-only temporal-table renaming is injected from `Norse.EntityFramework.SqlServer` through an optional delegate parameter, so the provider-neutral base project never references the SqlServer package.

**Tech Stack:** .NET 11, EF Core 11.x (`Microsoft.EntityFrameworkCore.Relational`, `Microsoft.EntityFrameworkCore.SqlServer`), xUnit v3 on Microsoft.Testing.Platform, Shouldly.

## Global Constraints

- Design source of truth: `../specs/2026-07-22-inhouse-snake-case-naming-convention-design.md` — read it before starting any task; this plan implements it exactly, including its Addendum.
- `sealed` by default for every new type (Bifröst CLAUDE.md §5).
- `omit_if_default` accessibility — no visible modifier on anything that would already be the default.
- Tabs for indentation (this is C#, not one of the whitespace-aware-language exceptions).
- No automatic git commits beyond what each task step explicitly stages/commits per this plan — the human reviews before pushing.
- `Norse.EntityFramework` (`src/EntityFramework`) must never gain a `Microsoft.EntityFrameworkCore.SqlServer` package reference — this is the entire point of the injected-action seam in Task 4. If any task's implementation seems to need one there, stop and re-read the spec's Addendum.
- All EF Core API surfaces used below (`IDbContextOptionsExtension`, `DbContextOptionsExtensionInfo`, `IConventionSetPlugin`, `ConventionSet`, `IConventionModel.GetEntityTypes()`, `IConventionTypeBase.GetProperties()`, and every `RelationalXxxExtensions`/`SqlServerEntityTypeExtensions` method referenced) were confirmed by reflecting the real `Microsoft.EntityFrameworkCore` / `.Relational` / `.SqlServer` 11.x assemblies before this plan was written — not recalled from memory, not decompiled guesswork. Signatures in this plan are load-bearing, not sketches.

---

## Task 1: `SnakeCaseNameRewriter`

**Files:**
- Create: `src/EntityFramework/SnakeCaseNameRewriter.cs`
- Test: `tests/EntityFramework.Tests/SnakeCaseNameRewriterTests.cs`

**Interfaces:**
- Produces: `internal static class SnakeCaseNameRewriter { internal static string RewriteName(string name); }` in namespace `Norse.EntityFramework` — Task 2 depends on this exact signature.

- [ ] **Step 1: Write the failing tests**

Create `tests/EntityFramework.Tests/SnakeCaseNameRewriterTests.cs`:

```csharp
namespace Norse.EntityFramework.Tests;

public sealed class SnakeCaseNameRewriterTests
{
	[Fact]
	void PascalCase_multi_word_name_gets_underscore_at_each_word_boundary()
	{
		SnakeCaseNameRewriter.RewriteName("CustomerId").ShouldBe("customer_id");
	}

	[Fact]
	void camelCase_name_gets_underscore_at_each_word_boundary()
	{
		SnakeCaseNameRewriter.RewriteName("customerId").ShouldBe("customer_id");
	}

	[Fact]
	void Acronym_run_at_start_of_name_has_no_leading_or_internal_underscore()
	{
		SnakeCaseNameRewriter.RewriteName("ID").ShouldBe("id");
	}

	[Fact]
	void Acronym_run_followed_by_a_word_splits_before_the_last_acronym_letter()
	{
		SnakeCaseNameRewriter.RewriteName("HTTPClient").ShouldBe("http_client");
	}

	[Fact]
	void Digit_immediately_followed_by_uppercase_does_not_insert_an_underscore()
	{
		SnakeCaseNameRewriter.RewriteName("Value2Text").ShouldBe("value2text");
	}

	[Fact]
	void Pre_existing_underscores_are_preserved_and_reset_word_boundary_state()
	{
		SnakeCaseNameRewriter.RewriteName("already_snake").ShouldBe("already_snake");
	}

	[Fact]
	void Empty_string_returns_empty_string()
	{
		SnakeCaseNameRewriter.RewriteName("").ShouldBe("");
	}

	[Fact]
	void Single_uppercase_letter_lowercases_without_a_leading_underscore()
	{
		SnakeCaseNameRewriter.RewriteName("A").ShouldBe("a");
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/EntityFramework.Tests --filter SnakeCaseNameRewriterTests`
Expected: FAIL to compile — `SnakeCaseNameRewriter` does not exist yet.

- [ ] **Step 3: Write the implementation**

Create `src/EntityFramework/SnakeCaseNameRewriter.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace Norse.EntityFramework;

/// <summary>
/// Rewrites an identifier (table name, column name, constraint name, ...) to snake_case. Ported from
/// prior art's Unicode-category-aware rewrite algorithm — handles acronym runs, embedded digits, and
/// pre-existing underscores correctly, unlike a naive case-boundary regex. Culture is fixed to
/// <see cref="CultureInfo.InvariantCulture"/>: nothing on this platform plumbs a locale through for
/// database identifier casing today, and adding one later is a small, easy change if that ever changes.
/// </summary>
static class SnakeCaseNameRewriter
{
	internal static string RewriteName(string name)
	{
		var builder = new StringBuilder(name.Length + Math.Min(2, name.Length / 5));
		var previousCategory = default(UnicodeCategory?);

		for (var currentIndex = 0; currentIndex < name.Length; currentIndex++)
		{
			var currentChar = name[currentIndex];
			if (currentChar == '_')
			{
				builder.Append('_');
				previousCategory = null;
				continue;
			}

			var currentCategory = char.GetUnicodeCategory(currentChar);
			switch (currentCategory)
			{
				case UnicodeCategory.UppercaseLetter:
				case UnicodeCategory.TitlecaseLetter:
					if (previousCategory == UnicodeCategory.SpaceSeparator ||
						previousCategory == UnicodeCategory.LowercaseLetter ||
						previousCategory != UnicodeCategory.DecimalDigitNumber &&
						previousCategory != null &&
						currentIndex > 0 &&
						currentIndex + 1 < name.Length &&
						char.IsLower(name[currentIndex + 1]))
					{
						builder.Append('_');
					}

					currentChar = char.ToLower(currentChar, CultureInfo.InvariantCulture);
					break;

				case UnicodeCategory.LowercaseLetter:
				case UnicodeCategory.DecimalDigitNumber:
					if (previousCategory == UnicodeCategory.SpaceSeparator)
					{
						builder.Append('_');
					}
					break;

				default:
					if (previousCategory != null)
					{
						previousCategory = UnicodeCategory.SpaceSeparator;
					}
					continue;
			}

			builder.Append(currentChar);
			previousCategory = currentCategory;
		}

		return builder.ToString();
	}
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/EntityFramework.Tests --filter SnakeCaseNameRewriterTests`
Expected: PASS, all 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/EntityFramework/SnakeCaseNameRewriter.cs tests/EntityFramework.Tests/SnakeCaseNameRewriterTests.cs
git commit -m "Port SnakeCaseNameRewriter algorithm in-house"
```

---

## Task 2: `NorseSnakeCaseNamingConvention`

**Files:**
- Create: `src/EntityFramework/NorseSnakeCaseNamingConvention.cs`
- Test: `tests/EntityFramework.Tests/NorseSnakeCaseNamingConventionTests.cs`

**Interfaces:**
- Consumes: `SnakeCaseNameRewriter.RewriteName(string) : string` (Task 1).
- Produces: `sealed class NorseSnakeCaseNamingConvention(Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames) : IModelFinalizingConvention` in namespace `Norse.EntityFramework` — Task 3 depends on this exact constructor signature.

- [ ] **Step 1: Write the failing tests**

Create `tests/EntityFramework.Tests/NorseSnakeCaseNamingConventionTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Norse.EntityFramework.Tests;

public sealed class NorseSnakeCaseNamingConventionTests
{
	[Fact]
	void Table_name_is_rewritten_to_snake_case()
	{
		using var ctx = CreateContext<RewriteTestContext>();

		var tableName = ctx.Model.FindEntityType(typeof(RewriteTestEntity))!.GetTableName();

		tableName.ShouldBe("rewrite_test_entities");
	}

	[Fact]
	void Primary_key_name_is_rewritten_to_snake_case()
	{
		using var ctx = CreateContext<RewriteTestContext>();

		var primaryKey = ctx.Model.FindEntityType(typeof(RewriteTestEntity))!.FindPrimaryKey();

		primaryKey!.GetName().ShouldBe("pk_rewrite_test_entities");
	}

	[Fact]
	void Column_name_is_rewritten_to_snake_case()
	{
		using var ctx = CreateContext<RewriteTestContext>();

		var property = ctx.Model.FindEntityType(typeof(RewriteTestEntity))!
			.FindProperty(nameof(RewriteTestEntity.CustomerName));

		property!.GetColumnName().ShouldBe("customer_name");
	}

	[Fact]
	void Json_mapped_entity_has_only_its_container_column_name_rewritten()
	{
		using var ctx = CreateContext<JsonMappedContext>();

		var jsonEntity = ctx.Model.GetEntityTypes().Single(e => e.IsMappedToJson());

		jsonEntity.GetContainerColumnName().ShouldBe("shipping_detail");
	}

	[Fact]
	void Injected_action_receives_every_entity_and_the_rewrite_function()
	{
		List<string> invokedEntityClrNames = [];
		Func<string, string>? capturedRewrite = null;

		using var ctx = new InjectedActionContext(
			new DbContextOptionsBuilder<InjectedActionContext>().UseSqlite("Data Source=:memory:").Options,
			(entity, rewrite) =>
			{
				invokedEntityClrNames.Add(entity.ClrType.Name);
				capturedRewrite = rewrite;
			});

		_ = ctx.Model;

		invokedEntityClrNames.ShouldContain(nameof(RewriteTestEntity));
		capturedRewrite.ShouldNotBeNull();
		capturedRewrite!("CustomerId").ShouldBe("customer_id");
	}

	static TContext CreateContext<TContext>() where TContext : DbContext =>
		(TContext)Activator.CreateInstance(typeof(TContext),
			new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:").Options)!;

	sealed class RewriteTestEntity
	{
		public int Id { get; set; }
		public string CustomerName { get; set; } = "";
	}

	sealed class RewriteTestContext(DbContextOptions<RewriteTestContext> options) : NorseDbContext(options)
	{
		public DbSet<RewriteTestEntity> RewriteTestEntities => Set<RewriteTestEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new NorseSnakeCaseNamingConvention(null));
	}

	sealed class JsonMappedOwner
	{
		public int Id { get; set; }
		public JsonMappedDetail ShippingDetail { get; set; } = new();
	}

	sealed class JsonMappedDetail
	{
		public string Value { get; set; } = "";
	}

	sealed class JsonMappedContext(DbContextOptions<JsonMappedContext> options) : NorseDbContext(options)
	{
		public DbSet<JsonMappedOwner> JsonMappedOwners => Set<JsonMappedOwner>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new NorseSnakeCaseNamingConvention(null));

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<JsonMappedOwner>().OwnsOne(e => e.ShippingDetail, o => o.ToJson());
		}
	}

	sealed class InjectedActionContext(
		DbContextOptions<InjectedActionContext> options,
		Action<IConventionEntityType, Func<string, string>> applyProviderSpecificRenames) : NorseDbContext(options)
	{
		public DbSet<RewriteTestEntity> RewriteTestEntities => Set<RewriteTestEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(_ => new NorseSnakeCaseNamingConvention(applyProviderSpecificRenames));
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/EntityFramework.Tests --filter NorseSnakeCaseNamingConventionTests`
Expected: FAIL to compile — `NorseSnakeCaseNamingConvention` does not exist yet.

- [ ] **Step 3: Write the implementation**

Create `src/EntityFramework/NorseSnakeCaseNamingConvention.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Norse.EntityFramework;

/// <summary>
/// Model-finalizing convention that renames every relational EF metadata object to snake_case: table
/// names, primary key names, column names, default-constraint names, key names, foreign key constraint
/// names, and index names. JSON-mapped entities only have their container column name rewritten — EF
/// migrations fail if a JSON-mapped entity's table/column identity is touched the normal way, so those
/// entities short-circuit the rest of the walk.
/// </summary>
/// <param name="applyProviderSpecificRenames">
/// Optional provider-specific extension point, invoked once per entity after this convention's own
/// renames. This convention has no idea what it does — it only hands the entity and its own
/// <see cref="SnakeCaseNameRewriter.RewriteName"/> function to whatever the registering provider
/// supplied via <see cref="NorseDbContextOptionsExtensions.ApplyNorseConventions"/>, or nothing at all.
/// SQL Server temporal history table renaming is supplied this way from
/// <c>Norse.EntityFramework.SqlServer</c> — see that project's
/// <c>NorseSqlServerContextExtensions</c> — because <c>IsTemporal()</c>/<c>GetHistoryTableName()</c> are
/// SQL-Server-only EF APIs this provider-neutral project must never reference directly.
/// </param>
sealed class NorseSnakeCaseNamingConvention(
	Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames) : IModelFinalizingConvention
{
	public void ProcessModelFinalizing(
		IConventionModelBuilder builder, IConventionContext<IConventionModelBuilder> context)
	{
		foreach (var entity in builder.Metadata.GetEntityTypes())
		{
			if (entity.IsMappedToJson())
			{
				var containerColumnName = entity.GetContainerColumnName();
				if (!string.IsNullOrWhiteSpace(containerColumnName))
					entity.SetContainerColumnName(SnakeCaseNameRewriter.RewriteName(containerColumnName));
				continue;
			}

			var tableName = entity.GetTableName();
			if (string.IsNullOrWhiteSpace(tableName))
				continue;

			entity.SetTableName(SnakeCaseNameRewriter.RewriteName(tableName));

			var primaryKey = entity.FindPrimaryKey();
			if (primaryKey is not null)
			{
				var primaryKeyName = primaryKey.GetName();
				if (!string.IsNullOrWhiteSpace(primaryKeyName))
					primaryKey.SetName(SnakeCaseNameRewriter.RewriteName(primaryKeyName));
			}

			foreach (var property in entity.GetProperties())
			{
				property.SetColumnName(SnakeCaseNameRewriter.RewriteName(property.GetColumnName()));

				var defaultConstraintName = property.GetDefaultConstraintName();
				if (!string.IsNullOrWhiteSpace(defaultConstraintName))
					property.SetDefaultConstraintName(SnakeCaseNameRewriter.RewriteName(defaultConstraintName));
			}

			foreach (var key in entity.GetKeys())
			{
				var keyName = key.GetName();
				if (!string.IsNullOrWhiteSpace(keyName))
					key.SetName(SnakeCaseNameRewriter.RewriteName(keyName));
			}

			foreach (var foreignKey in entity.GetForeignKeys())
			{
				var constraintName = foreignKey.GetConstraintName();
				if (!string.IsNullOrWhiteSpace(constraintName))
					foreignKey.SetConstraintName(SnakeCaseNameRewriter.RewriteName(constraintName));
			}

			foreach (var index in entity.GetIndexes())
			{
				var databaseName = index.GetDatabaseName();
				if (!string.IsNullOrWhiteSpace(databaseName))
					index.SetDatabaseName(SnakeCaseNameRewriter.RewriteName(databaseName));
			}

			applyProviderSpecificRenames?.Invoke(entity, SnakeCaseNameRewriter.RewriteName);
		}
	}
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/EntityFramework.Tests --filter "NorseSnakeCaseNamingConventionTests|SnakeCaseNameRewriterTests"`
Expected: PASS, all tests from both Task 1 and Task 2.

- [ ] **Step 5: Commit**

```bash
git add src/EntityFramework/NorseSnakeCaseNamingConvention.cs tests/EntityFramework.Tests/NorseSnakeCaseNamingConventionTests.cs
git commit -m "Add NorseSnakeCaseNamingConvention model-finalizing convention"
```

---

## Task 3: Wire `NorseSnakeCaseNamingConvention` into `ApplyNorseConventions`

**Files:**
- Create: `src/EntityFramework/NorseSnakeCaseNamingOptionsExtension.cs`
- Create: `src/EntityFramework/NorseSnakeCaseConventionSetPlugin.cs`
- Modify: `src/EntityFramework/NorseDbContextOptionsExtensions.cs`
- Modify: `tests/EntityFramework.Tests/NorseDbContextOptionsExtensionsTests.cs`

**Interfaces:**
- Consumes: `NorseSnakeCaseNamingConvention(Action<IConventionEntityType, Func<string, string>>?)` (Task 2).
- Produces: `NorseDbContextOptionsExtensions.ApplyNorseConventions(DbContextOptionsBuilder, Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames = null) : DbContextOptionsBuilder` — Task 4 depends on this exact signature (the new second parameter).

- [ ] **Step 1: Write the failing test**

`tests/EntityFramework.Tests/NorseDbContextOptionsExtensionsTests.cs` already has a red test —
`ApplyNorseConventions_applies_snake_case_naming` (asserts `TestContext`'s table renames to
`test_entities`). Read the current file first:

Run: `cat tests/EntityFramework.Tests/NorseDbContextOptionsExtensionsTests.cs`

Add one new test to that file, proving a metadata kind beyond the table name gets renamed too — a
foreign key constraint name and an index name, generated by EF's own default conventions from a
one-to-many relationship. Insert this using block and this test method into the existing file
(the file currently starts with `using Microsoft.EntityFrameworkCore;` /
`using Microsoft.EntityFrameworkCore.Metadata.Builders;` and one `namespace` line — keep those, add
the test and the two new nested entity classes alongside the existing `TestContext`/`TestEntity`):

```csharp
[Fact]
void ApplyNorseConventions_renames_foreign_key_and_index_names()
{
	var optionsBuilder = new DbContextOptionsBuilder<RelatedEntitiesContext>().UseSqlite("Data Source=:memory:");
	NorseDbContextOptionsExtensions.ApplyNorseConventions(optionsBuilder);

	using var ctx = new RelatedEntitiesContext(optionsBuilder.Options);
	var childEntity = ctx.Model.FindEntityType(typeof(ChildEntity))!;
	var foreignKey = childEntity.GetForeignKeys().Single();
	var index = childEntity.GetIndexes().Single();

	foreignKey.GetConstraintName().ShouldBe("fk_child_entities_parent_entities_parent_entity_id");
	index.GetDatabaseName().ShouldBe("ix_child_entities_parent_entity_id");
}

sealed class ParentEntity
{
	public int Id { get; set; }
	public List<ChildEntity> Children { get; set; } = [];
}

sealed class ChildEntity
{
	public int Id { get; set; }
	public int ParentEntityId { get; set; }
	public ParentEntity ParentEntity { get; set; } = null!;
}

sealed class RelatedEntitiesContext(DbContextOptions<RelatedEntitiesContext> options) : NorseDbContext(options)
{
	public DbSet<ParentEntity> ParentEntities => Set<ParentEntity>();
	public DbSet<ChildEntity> ChildEntities => Set<ChildEntity>();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/EntityFramework.Tests --filter NorseDbContextOptionsExtensionsTests`
Expected: FAIL — both `ApplyNorseConventions_applies_snake_case_naming` (pre-existing red test) and
the new `ApplyNorseConventions_renames_foreign_key_and_index_names` fail, since `ApplyNorseConventions`
is currently a no-op.

- [ ] **Step 3: Write the implementation**

Create `src/EntityFramework/NorseSnakeCaseConventionSetPlugin.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Norse.EntityFramework;

sealed class NorseSnakeCaseConventionSetPlugin(
	Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames) : IConventionSetPlugin
{
	public ConventionSet ModifyConventions(ConventionSet conventionSet)
	{
		conventionSet.ModelFinalizingConventions.Add(new NorseSnakeCaseNamingConvention(applyProviderSpecificRenames));
		return conventionSet;
	}
}
```

Create `src/EntityFramework/NorseSnakeCaseNamingOptionsExtension.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.EntityFramework;

/// <summary>
/// Additive <see cref="IDbContextOptionsExtension"/> that registers
/// <see cref="NorseSnakeCaseConventionSetPlugin"/> via <see cref="IServiceCollection.AddSingleton"/> —
/// deliberately not <c>ReplaceService</c>, which would silently clobber the DI registration slot if a
/// second <see cref="Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure.IConventionSetPlugin"/>
/// is ever added later. EF Core resolves that interface as
/// <see cref="IEnumerable{T}"/> when building the convention set, designed for exactly this kind of
/// additive composition.
/// </summary>
sealed class NorseSnakeCaseNamingOptionsExtension(
	Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames) : IDbContextOptionsExtension
{
	// A primary-constructor-captured parameter is only resolvable as a bare identifier inside this
	// class's own instance members -- it is not a real member reachable via instance.parameterName,
	// even from a nested class after a cast. ExtensionInfo needs a genuine named member to read.
	internal Action<IConventionEntityType, Func<string, string>>? ApplyProviderSpecificRenames { get; }
		= applyProviderSpecificRenames;

	public void ApplyServices(IServiceCollection services)
		=> services.AddSingleton<IConventionSetPlugin>(new NorseSnakeCaseConventionSetPlugin(ApplyProviderSpecificRenames));

	public IDbContextOptionsExtension ApplyDefaults(IDbContextOptions options) => this;

	public void Validate(IDbContextOptions options)
	{
	}

	public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

	sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
	{
		public override bool IsDatabaseProvider => false;

		public override string LogFragment => "using Norse snake_case naming";

		public override int GetServiceProviderHashCode()
			=> ((NorseSnakeCaseNamingOptionsExtension)Extension).ApplyProviderSpecificRenames?.GetHashCode() ?? 0;

		public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
			=> other is ExtensionInfo otherInfo
			   && Equals(
				   ((NorseSnakeCaseNamingOptionsExtension)Extension).ApplyProviderSpecificRenames,
				   ((NorseSnakeCaseNamingOptionsExtension)otherInfo.Extension).ApplyProviderSpecificRenames);

		public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
		{
		}
	}
}
```

Modify `src/EntityFramework/NorseDbContextOptionsExtensions.cs` — replace the whole file:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

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
	/// Applies snake_case naming to all entity table names, column names, keys, foreign keys, indexes,
	/// and JSON container columns, via Urðarbrunnr's own <see cref="NorseSnakeCaseNamingConvention"/>.
	/// Called conditionally by each provider's registration extension (see
	/// <c>Norse.EntityFramework.PostgreSQL.NorsePostgresContextExtensions</c> and its SQL Server
	/// counterpart) — never unconditionally by a context itself, since whether snake_case is the right
	/// default is a provider decision, not a Norse-wide one.
	/// </summary>
	/// <param name="optionsBuilder">The options builder to configure.</param>
	/// <param name="applyProviderSpecificRenames">
	/// Optional provider-specific rename hook, invoked once per entity in addition to this method's own
	/// renames. Used by <c>Norse.EntityFramework.SqlServer</c> to rename temporal history tables — an
	/// EF API this provider-neutral project must never reference directly. See
	/// <see cref="NorseSnakeCaseNamingConvention"/>'s remarks for the full rationale.
	/// </param>
	/// <returns>The same <paramref name="optionsBuilder"/> for chaining.</returns>
	public static DbContextOptionsBuilder ApplyNorseConventions(
		DbContextOptionsBuilder optionsBuilder,
		Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames = null)
	{
		((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
			.AddOrUpdateExtension(new NorseSnakeCaseNamingOptionsExtension(applyProviderSpecificRenames));
		return optionsBuilder;
	}
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/EntityFramework.Tests`
Expected: PASS, the full `EntityFramework.Tests` suite — including the pre-existing
`ApplyNorseConventions_applies_snake_case_naming` (now green), the new
`ApplyNorseConventions_renames_foreign_key_and_index_names`, and everything from Tasks 1–2.

- [ ] **Step 5: Commit**

```bash
git add src/EntityFramework/NorseSnakeCaseNamingOptionsExtension.cs src/EntityFramework/NorseSnakeCaseConventionSetPlugin.cs src/EntityFramework/NorseDbContextOptionsExtensions.cs tests/EntityFramework.Tests/NorseDbContextOptionsExtensionsTests.cs
git commit -m "Wire NorseSnakeCaseNamingConvention into ApplyNorseConventions"
```

---

## Task 4: SQL Server temporal history table rename injection

**Files:**
- Modify: `src/EntityFramework.SqlServer/NorseSqlServerContextExtensions.cs`
- Modify: `tests/EntityFramework.SqlServer.Tests/NorseSqlServerContextExtensionsTests.cs`

**Interfaces:**
- Consumes: `NorseDbContextOptionsExtensions.ApplyNorseConventions(DbContextOptionsBuilder, Action<IConventionEntityType, Func<string, string>>? = null)` (Task 3).

- [ ] **Step 1: Write the failing test**

`tests/EntityFramework.SqlServer.Tests/NorseSqlServerContextExtensionsTests.cs` already has a red test
— `AddNorseSqlServerMigrationContext_opts_into_snake_case_naming_when_requested` — that will go green
as a side effect of this task (it depends on the same `ApplyNorseConventions` this task's call sites
invoke). Add one new test proving the temporal-history-table injection specifically. Insert into the
existing file (keep its current `using` lines and existing tests/nested types untouched):

```csharp
[Fact]
void AddNorseSqlServerMigrationContext_renames_temporal_history_table_when_snake_case_requested()
{
	var builder = Host.CreateApplicationBuilder();
	builder.Configuration.AddInMemoryCollection(
		new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });

	builder.AddNorseSqlServerMigrationContext<TemporalTestContext>(
		"test-db", "Norse.EntityFramework.SqlServer.Tests", useSnakeCaseNaming: true);

	using var host = builder.Build();
	using var scope = host.Services.CreateScope();
	using var ctx = scope.ServiceProvider.GetRequiredService<TemporalTestContext>();

	var historyTableName = ctx.Model.FindEntityType(typeof(TemporalTestEntity))!.GetHistoryTableName();

	historyTableName.ShouldBe("temporal_test_entity_history");
}

sealed class TemporalTestEntity
{
	public int Id { get; set; }
	public string Value { get; set; } = "";
}

sealed class TemporalTestContext(DbContextOptions<TemporalTestContext> options) : NorseDbContext(options)
{
	public DbSet<TemporalTestEntity> TemporalTestEntities => Set<TemporalTestEntity>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.Entity<TemporalTestEntity>().ToTable(
			"TemporalTestEntities",
			tb => tb.IsTemporal(t => t.UseHistoryTable("TemporalTestEntityHistory")));
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/EntityFramework.SqlServer.Tests`
Expected: FAIL — both the pre-existing red `AddNorseSqlServerMigrationContext_opts_into_snake_case_naming_when_requested`
and the new `AddNorseSqlServerMigrationContext_renames_temporal_history_table_when_snake_case_requested`
fail, since `AddNorseSqlServerMigrationContext` doesn't yet pass a temporal-rename delegate.

- [ ] **Step 3: Write the implementation**

Modify `src/EntityFramework.SqlServer/NorseSqlServerContextExtensions.cs`. Add a `using` for
`Microsoft.EntityFrameworkCore.Metadata`, extend both methods' `useSnakeCaseNaming` XML doc with a note
about temporal history tables, add a private static rename-delegate method, and change both
`ApplyNorseConventions` call sites to pass it.

Append this sentence to the end of `AddNorseSqlServerContext`'s `useSnakeCaseNaming` `<param>` doc
(the one starting "Whether to apply snake_case table/column naming..."), just before the closing
`</param>` tag:

```
When <see langword="true"/>, a temporal entity's history table name is renamed too — see
<see cref="RenameTemporalHistoryTable"/>.
```

Change `AddNorseSqlServerMigrationContext`'s `useSnakeCaseNaming` `<param>` doc from
`<param name="useSnakeCaseNaming">See <see cref="AddNorseSqlServerContext{TContext}"/>.</param>` to:

```
/// <param name="useSnakeCaseNaming">See <see cref="AddNorseSqlServerContext{TContext}"/>, including the temporal history table note.</param>
```

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.EntityFramework.SqlServer;
```

Replace the body of `AddNorseSqlServerContext`'s `configureDbContextOptions` lambda:

```csharp
		builder.AddSqlServerDbContext<TContext>(connectionStringName,
			configureDbContextOptions: opts =>
			{
				if (useSnakeCaseNaming)
					NorseDbContextOptionsExtensions.ApplyNorseConventions(opts, RenameTemporalHistoryTable);
			});
		return builder;
	}
```

Replace the body of `AddNorseSqlServerMigrationContext`'s `AddDbContext` lambda:

```csharp
		builder.Services.AddDbContext<TContext>(opts =>
		{
			opts.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(migrationsAssemblyName));
			if (useSnakeCaseNaming)
				NorseDbContextOptionsExtensions.ApplyNorseConventions(opts, RenameTemporalHistoryTable);
		});
		builder.EnrichSqlServerDbContext<TContext>();

		return builder;
	}
```

Add this private static method at the bottom of the `NorseSqlServerContextExtensions` class, just
before its closing brace:

```csharp
	/// <summary>
	/// Renames a temporal entity's history table to snake_case. <c>IsTemporal()</c> and
	/// <c>GetHistoryTableName()</c>/<c>SetHistoryTableName()</c> are SQL-Server-only EF APIs
	/// (<c>Microsoft.EntityFrameworkCore.SqlServerEntityTypeExtensions</c>) — this is the only project
	/// in the platform allowed to reference them; <c>Norse.EntityFramework</c> stays provider-neutral
	/// and only ever sees this method as an opaque injected action.
	/// </summary>
	static void RenameTemporalHistoryTable(IConventionEntityType entity, Func<string, string> rewrite)
	{
		if (!entity.IsTemporal())
			return;

		var historyTableName = entity.GetHistoryTableName();
		if (!string.IsNullOrWhiteSpace(historyTableName))
			entity.SetHistoryTableName(rewrite(historyTableName));
	}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/EntityFramework.SqlServer.Tests`
Expected: PASS, the full `EntityFramework.SqlServer.Tests` suite — including both previously-red tests,
now green.

- [ ] **Step 5: Run the full Urðarbrunnr test suite**

Run: `dotnet test Urdarbrunnr.slnx`
Expected: PASS across every test project — confirms nothing in Tasks 1–4 regressed
`EntityFramework.Migrations.Tests`, `EntityFramework.Migrations.PostgreSQL.Generator.Tests`, or any
other project in the solution.

- [ ] **Step 6: Commit**

```bash
git add src/EntityFramework.SqlServer/NorseSqlServerContextExtensions.cs tests/EntityFramework.SqlServer.Tests/NorseSqlServerContextExtensionsTests.cs
git commit -m "Inject SQL Server temporal history table rename into ApplyNorseConventions"
```

---

## Task 5: Documentation

**Files:**
- Modify: `CLAUDE.md` (Urðarbrunnr repo root)

**Interfaces:** None — documentation only, no code.

- [ ] **Step 1: Update the state-of-the-union note**

Read the current `CLAUDE.md` §1 "What This Repository Is" paragraph describing the four live
assemblies and the provider-aware length/naming work. Add a short note (2–3 sentences, matching the
file's existing prose density) stating: the `EFCore.NamingConventions` dependency has been removed;
snake_case naming is now Urðarbrunnr's own code
(`NorseSnakeCaseNamingConvention`/`SnakeCaseNameRewriter` in `Norse.EntityFramework`); no functional or
call-site change to `AddNorsePostgresContext`/`AddNorseSqlServerContext`'s `useSnakeCaseNaming`
parameter or its provider-specific defaults. Point at
`../Glitnir/docs/Urdarbrunnr/specs/2026-07-22-inhouse-snake-case-naming-convention-design.md` for the
full design, matching how the file already points at the 2026-06-28 and 2026-07-03 plans/specs.

- [ ] **Step 2: Verify README.md doesn't also need the update**

Run: `grep -n "EFCore.NamingConventions\|snake_case\|snake case" README.md`

If README.md references `EFCore.NamingConventions` by name or otherwise describes naming-convention
mechanics in a way this change makes stale, update it too — Bifröst CLAUDE.md §6's boy-scout rule
("README.md and CLAUDE.md stay in sync") applies here even though this is Urðarbrunnr's own
README/CLAUDE.md pair, not Bifröst's. If the grep finds nothing relevant, no README change is needed —
don't force one.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md README.md
git commit -m "Document in-house snake_case naming convention in CLAUDE.md"
```
