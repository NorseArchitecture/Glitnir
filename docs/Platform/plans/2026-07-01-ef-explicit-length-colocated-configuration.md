# EF Explicit-Length Enforcement & Colocated Entity Configuration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (default) or `superpowers:executing-plans` (separate-session fallback, never interchangeable). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close two gaps in `Norse.EntityFramework` and prove them against Himinbjörg's real Identity/OpenIddict schema before it is migrated for the first time in earnest: (1) no `string`/`byte[]` column reaches the database without an explicit length decision, enforced at model-finalization time; (2) every entity is its own configuration via a compiler-enforced `Configure` method, discovered by a Roslyn source generator, never a reflection scan.

**Architecture:** Two realms, strict dependency order. Urdarbrunnr (`Norse.EntityFramework`) ships the attribute trio, two `IModelFinalizingConvention`s, the `INorseEntity<TSelf>`/`NorseEntityBase<TSelf>` two-tier interface, and `EntityConfigurationApplicationGenerator` (a same-compilation Roslyn `IIncrementalGenerator` in the new `Norse.EntityFramework.Configuration.Generator` project, forwarded via the existing `NorseRef Generator="true"` mechanism — mirrors `EntityFramework.Migrations.PostgreSQL`'s wrapper+generator-sibling shape). Himinbjörg (`Norse.Identity`) is the first consumer: every existing Identity entity gets explicit navigation/FK properties and a colocated `Configure`, four new `NorseOpenIddict*` wrapper entities close OpenIddict's own unbounded-column and shadow-FK gaps, and the Identity migration is regenerated once the model builds clean.

**Tech Stack:** .NET 11 (`net11.0`), C#, EF Core (Npgsql + `EFCore.NamingConventions`), ASP.NET Core Identity v3, OpenIddict, Roslyn `IIncrementalGenerator` (`netstandard2.0`), xUnit v3 + Shouldly, Microsoft.EntityFrameworkCore.Sqlite (finalization-pipeline tests only)

**Execution model — realm-by-realm ship gate:** Two phases. Phase 1 (Tasks 1–6, Urdarbrunnr) ends with a `## SHIP GATE` — PR merged, CI green, tagged, published to NuGet — before Phase 2 (Tasks 7–15, Himinbjörg) starts. Himinbjörg's `NorseRef` resolves the new packages from NuGet by the time Phase 2 begins, exactly as the migrations-framework plan proved for `EntityFramework.Migrations.PostgreSQL`.

## Design amendments made during this planning pass (not in the original spec text)

The spec (`../Platform/specs/2026-07-01-ef-explicit-length-colocated-configuration-design.md`) is "Approved, ready for planning," but three gaps surfaced turning it into buildable tasks. Each was resolved with Buvy before any task below was written; recorded here so the "why" isn't archaeology later:

1. **Navigation properties don't exist yet.** §4.2's `NorseUser.Configure` example calls `builder.HasMany(u => u.Claims)...` but `NorseUser` today is a bare `IdentityUser<Guid>` with no `Claims`/`Logins`/`Tokens`/`Passkeys` properties, and the reverse `User` property doesn't exist on the claim/login/token/passkey entities either. **Resolved:** add them — see the new platform-wide law below.
2. **New platform-wide EF law (added to `../conventions.md`, not just this spec):** navigation and foreign-key properties are always explicit CLR properties, never shadow, except cross-cutting audit-stamp columns (`CreatedBy`, `IpAddress`, timestamps). Many-to-many relationships always get an explicit bridge entity, never EF's implicit skip-navigation join table. Verified against actual restored packages, not memory: OpenIddict's own `Authorization`/`Token` entities rely on shadow FKs (`Application`/`Authorization` are navigation-only, no `ApplicationId`/`AuthorizationId` scalar declared anywhere in `OpenIddict.EntityFrameworkCore.Models`) — this law applies to the `NorseOpenIddict*` wrappers exactly as hard as it applies to Norse's own code, same "no namespace exemption" spirit as §3's length convention. ASP.NET Core Identity's own base classes (verified the same way against `Microsoft.Extensions.Identity.Stores`) already declare every FK scalar explicitly (`UserId`, `RoleId`) — only the navigation side was missing there.
3. **`NorseDbContext.OnModelCreating` calling a generated static extension can't work for future Tier-1 contexts.** §4.4 has `NorseDbContext` (compiled once, shipped as a NuGet package) call `builder.ApplyNorseConfigurations()` — a static extension method generated per-consumer. Extension-method binding resolves at the *caller's* compile time (Urdarbrunnr's own build), so a downstream Tier-1 project's generator run cannot retroactively change what method already-compiled IL calls. This is a non-issue for Himinbjörg's `NorseIdentityDbContext` (§4.3) — that call is written directly in Himinbjörg's own source, compiled in the same pass the generator runs against — but it is a real compile-time dead end for the "free by inheritance" promise made for future Tier-1 contexts. **Resolved:** `NorseDbContext.OnModelCreating` calls a new `protected virtual ConfigureNorseEntities(ModelBuilder)` with an empty default body — real polymorphic dispatch, not static binding. The generator, when (in some future project) it finds a `partial` class inheriting `NorseDbContext` with `INorseEntity<T>` types in the same compilation, emits a second partial declaration overriding `ConfigureNorseEntities`. No Tier-1 consumer exists yet (Himinbjörg is 100% Tier 2), so this path is built now (per the spec's own "build the hook ahead of the first real Tier-1 consumer" precedent set by `ConfigureConventions`) but only exercised by a synthetic test type in Task 5/6, not a real bounded context.
4. **`StampConverter`/`HashConverter` duplication.** §4.2 says these are "private static fields on `NorseUser` itself — not generalized into Urdarbrunnr," but its own highlights table has `NorseRole.ConcurrencyStamp` reuse the identical `StampConverter` — a second caller *within the same realm*. Resolved by extracting both converters to an internal `IdentityValueConverters` static class in `Norse.Identity` (Task 7) — DRY within the realm, still not promoted to Urdarbrunnr (spec's "no other realm runs ASP.NET Identity today" reasoning still holds).
5. **`RequireEntityConfigurationConvention` needs the same JSON-owned-type skip §3 already has.** `NorseUserPasskey.Data` is JSON-owned (`OwnsOne(...).ToJson()`); without a skip, its owned CLR type (`IdentityPasskeyData`, not a Norse-authored type) would also need `INorseEntity<TSelf>`, which nothing in the spec asks for. Added `if (entityType.IsMappedToJson()) continue;` to Task 4's convention, mirroring §3 exactly.
6. **Column lengths §4.2 leaves as prose only** (`NorseUserLogin.LoginProvider`/`ProviderKey`/`ProviderDisplayName`, `NorseUserToken.LoginProvider`/`Name`): `LoginProvider` 128, `ProviderKey` 256, `ProviderDisplayName` 256, Token `LoginProvider` 128, Token `Name` 128 (Token `Value` was already decided: `-1`, spec §4.2).

## Global Constraints

- Target framework: `net11.0` for all library/test projects (repo default via `Directory.Build.props` — do not override); `netstandard2.0` for the generator-only project, matching `EntityFramework.Migrations.PostgreSQL.Generator`'s precedent.
- **Norse EF law (new, `../conventions.md`):** navigation + FK properties always explicit, never shadow, except audit-stamp columns. Many-to-many always via an explicit bridge entity.
- `var` for return assignments only; explicit type + `new()` for construction.
- `internal sealed`/`sealed` is the default; omit accessibility keywords when they are the default (`omit_if_default`). `public` only where a real cross-assembly caller exists in this plan.
- Tabs for indentation.
- US English spelling in all identifiers, comments, docs.
- No automatic git commits — stage only (`git add`); human commits.
- Shouldly for all assertions. No mocking framework needed in this plan (no behavior worth substituting — conventions and generators are pure functions of a model/compilation).
- No force-push to `master`. No `--no-verify`.
- `NorseRef` for cross-realm references; plain `<ProjectReference>` for same-realm references; `Generator="true"` metadata on the `NorseRef` that pulls in an analyzer sibling (per `../Platform/specs/2026-07-01-norseref-generator-forwarding-design.md`).
- Generator (`EntityConfigurationApplicationGenerator`) walks a **syntax provider over the compiling project's own class declarations only** — deliberately *not* the cross-assembly compiled-symbol walk `MigrationContributorGenerator` uses (spec §4.4: Himinbjörg colocates entities and `DbContext` in one project; no cross-assembly case to serve yet).
- One public type per file; filename matches type name exactly.

---

## File Map

### Urdarbrunnr

| Action | Path |
|---|---|
| Create | `Urdarbrunnr/src/EntityFramework/MaxLengthAttribute.cs` |
| Create | `Urdarbrunnr/src/EntityFramework/FixedLengthAttribute.cs` |
| Create | `Urdarbrunnr/src/EntityFramework/UnboundedLengthAttribute.cs` |
| Create | `Urdarbrunnr/src/EntityFramework/RequireExplicitLengthConvention.cs` |
| Create | `Urdarbrunnr/src/EntityFramework/INorseEntity.cs` |
| Create | `Urdarbrunnr/src/EntityFramework/NorseEntityBase.cs` |
| Create | `Urdarbrunnr/src/EntityFramework/RequireEntityConfigurationConvention.cs` |
| Create | `Urdarbrunnr/src/EntityFramework/NorseModelConventions.cs` |
| Modify | `Urdarbrunnr/src/EntityFramework/NorseDbContext.cs` |
| Create | `Urdarbrunnr/src/EntityFramework.Configuration.Generator/EntityFramework.Configuration.Generator.csproj` |
| Create | `Urdarbrunnr/src/EntityFramework.Configuration.Generator/EntityConfigurationApplicationGenerator.cs` |
| Create | `Urdarbrunnr/src/EntityFramework.Configuration/EntityFramework.Configuration.csproj` |
| Create | `Urdarbrunnr/tests/EntityFramework.Tests/RequireExplicitLengthConventionTests.cs` |
| Create | `Urdarbrunnr/tests/EntityFramework.Tests/RequireEntityConfigurationConventionTests.cs` |
| Create | `Urdarbrunnr/tests/EntityFramework.Tests/NorseDbContextTests.cs` |
| Create | `Urdarbrunnr/tests/EntityFramework.Configuration.Generator.Tests/EntityFramework.Configuration.Generator.Tests.csproj` |
| Create | `Urdarbrunnr/tests/EntityFramework.Configuration.Generator.Tests/EntityConfigurationApplicationGeneratorTests.cs` |
| Modify | `Urdarbrunnr/Urdarbrunnr.slnx` |
| Modify | `Bifrost.slnx` |

### Himinbjörg

| Action | Path |
|---|---|
| Create | `Himinbjorg/src/Identity/IdentityValueConverters.cs` |
| Modify | `Himinbjorg/src/Identity/NorseUser.cs` |
| Modify | `Himinbjorg/src/Identity/NorseRole.cs` |
| Modify | `Himinbjorg/src/Identity/NorseRoleClaim.cs` |
| Modify | `Himinbjorg/src/Identity/NorseUserClaim.cs` |
| Modify | `Himinbjorg/src/Identity/NorseUserLogin.cs` |
| Modify | `Himinbjorg/src/Identity/NorseUserToken.cs` |
| Modify | `Himinbjorg/src/Identity/NorseUserPasskey.cs` |
| Modify | `Himinbjorg/src/Identity/NorseUserRole.cs` |
| Create | `Himinbjorg/src/Identity/NorseOpenIddictApplication.cs` |
| Create | `Himinbjorg/src/Identity/NorseOpenIddictAuthorization.cs` |
| Create | `Himinbjorg/src/Identity/NorseOpenIddictScope.cs` |
| Create | `Himinbjorg/src/Identity/NorseOpenIddictToken.cs` |
| Modify | `Himinbjorg/src/Identity/NorseIdentityDbContext.cs` |
| Modify | `Himinbjorg/src/Identity/IdentityBuilderExtensions.cs` |
| Modify | `Himinbjorg/src/Identity/Identity.csproj` |
| Create | `Himinbjorg/tests/Identity.Tests/NorseUserConfigureTests.cs` |
| Create | `Himinbjorg/tests/Identity.Tests/NorseRoleConfigureTests.cs` |
| Create | `Himinbjorg/tests/Identity.Tests/NorseUserLoginConfigureTests.cs` |
| Create | `Himinbjorg/tests/Identity.Tests/NorseUserTokenConfigureTests.cs` |
| Create | `Himinbjorg/tests/Identity.Tests/NorseUserPasskeyConfigureTests.cs` |
| Create | `Himinbjorg/tests/Identity.Tests/NorseUserRoleConfigureTests.cs` |
| Create | `Himinbjorg/tests/Identity.Tests/NorseOpenIddictEntitiesConfigureTests.cs` |
| Create | `Himinbjorg/tests/Identity.Tests/NorseIdentityDbContextModelTests.cs` |
| Delete | `Himinbjorg/src/Identity.Migrations/Migrations/20260701171417_InitialCreate.cs` |
| Delete | `Himinbjorg/src/Identity.Migrations/Migrations/20260701171417_InitialCreate.Designer.cs` |
| Modify | `Himinbjorg/src/Identity.Migrations/Migrations/NorseIdentityDbContextModelSnapshot.cs` (regenerated) |
| Create | `Himinbjorg/src/Identity.Migrations/Migrations/{new-timestamp}_InitialCreate.cs` (regenerated) |
| Create | `Himinbjorg/src/Identity.Migrations/Migrations/{new-timestamp}_InitialCreate.Designer.cs` (regenerated) |

---

## Task 1: Urdarbrunnr — the attribute trio

**Files:**
- Create: `Urdarbrunnr/src/EntityFramework/MaxLengthAttribute.cs`
- Create: `Urdarbrunnr/src/EntityFramework/FixedLengthAttribute.cs`
- Create: `Urdarbrunnr/src/EntityFramework/UnboundedLengthAttribute.cs`
- Test: `Urdarbrunnr/tests/EntityFramework.Tests/RequireExplicitLengthConventionTests.cs` (attribute-shape assertions only in this task; convention behavior is Task 2)

**Interfaces:**
- Produces: `Norse.EntityFramework.MaxLengthAttribute(int length)`, `Norse.EntityFramework.FixedLengthAttribute(int length)`, `Norse.EntityFramework.UnboundedLengthAttribute()` — all `sealed`, all restricted to `AttributeTargets.Property | AttributeTargets.Field`, all subclass `System.ComponentModel.DataAnnotations.MaxLengthAttribute`.

- [ ] **Step 1: Write the failing test**

`Urdarbrunnr/tests/EntityFramework.Tests/RequireExplicitLengthConventionTests.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace Norse.EntityFramework.Tests;

public sealed class RequireExplicitLengthConventionTests
{
	[Fact]
	public void MaxLengthAttribute_carries_length()
	{
		MaxLengthAttribute attr = new(25);

		attr.Length.ShouldBe(25);
		attr.ShouldBeAssignableTo<MaxLengthAttribute>();
	}

	[Fact]
	public void FixedLengthAttribute_carries_length()
	{
		FixedLengthAttribute attr = new(10);

		attr.Length.ShouldBe(10);
	}

	[Fact]
	public void UnboundedLengthAttribute_carries_negative_one()
	{
		UnboundedLengthAttribute attr = new();

		attr.Length.ShouldBe(-1);
	}
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj --filter RequireExplicitLengthConventionTests
```

Expected: compile error — `MaxLengthAttribute`/`FixedLengthAttribute`/`UnboundedLengthAttribute` not defined in `Norse.EntityFramework`.

- [ ] **Step 3: Implement the three attributes**

`Urdarbrunnr/src/EntityFramework/MaxLengthAttribute.cs`:
```csharp
namespace Norse.EntityFramework;

/// <summary>
/// Drop-in replacement for <see cref="System.ComponentModel.DataAnnotations.MaxLengthAttribute"/>,
/// restricted to properties and fields — matches the restriction EF Core's own
/// <c>PrecisionAttribute</c> uses, which makes omitting the <c>property:</c> target specifier on a
/// positional record parameter a compile error.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MaxLengthAttribute(int length)
	: System.ComponentModel.DataAnnotations.MaxLengthAttribute(length);
```

`Urdarbrunnr/src/EntityFramework/FixedLengthAttribute.cs`:
```csharp
namespace Norse.EntityFramework;

/// <summary>
/// Marks a string property as fixed-length. Equivalent to <c>.HasMaxLength(n).IsFixedLength()</c> —
/// <c>nchar(n)</c>/<c>char(n)</c> depending on provider. <see cref="RequireExplicitLengthConvention"/>
/// translates presence of this attribute into <c>IsFixedLength()</c> at model-finalization time.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class FixedLengthAttribute(int length)
	: System.ComponentModel.DataAnnotations.MaxLengthAttribute(length);
```

`Urdarbrunnr/src/EntityFramework/UnboundedLengthAttribute.cs`:
```csharp
namespace Norse.EntityFramework;

/// <summary>
/// Marks a string or binary property as explicitly unbounded — <c>nvarchar(max)</c>/<c>text</c>,
/// <c>varbinary(max)</c>/<c>bytea</c>. Passes EF Core's own <c>-1</c> sentinel for "no maximum."
/// The only attribute-path escape hatch from <see cref="RequireExplicitLengthConvention"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class UnboundedLengthAttribute()
	: System.ComponentModel.DataAnnotations.MaxLengthAttribute(-1);
```

- [ ] **Step 4: Run the test to confirm it passes**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj --filter RequireExplicitLengthConventionTests
```

Expected: 3/3 PASS.

- [ ] **Step 5: Stage**

```bash
git -C Urdarbrunnr add \
  src/EntityFramework/MaxLengthAttribute.cs \
  src/EntityFramework/FixedLengthAttribute.cs \
  src/EntityFramework/UnboundedLengthAttribute.cs \
  tests/EntityFramework.Tests/RequireExplicitLengthConventionTests.cs
```

---

## Task 2: Urdarbrunnr — `RequireExplicitLengthConvention`

**Files:**
- Create: `Urdarbrunnr/src/EntityFramework/RequireExplicitLengthConvention.cs`
- Test: `Urdarbrunnr/tests/EntityFramework.Tests/RequireExplicitLengthConventionTests.cs` (extend from Task 1)

**Interfaces:**
- Consumes: the attribute trio (Task 1)
- Produces: `Norse.EntityFramework.RequireExplicitLengthConvention` — `IModelFinalizingConvention`. Not wired into any context yet (Task 4 wires both length and entity-configuration conventions together via `NorseModelConventions.Apply`).

This convention needs a real provider to reach `IModelFinalizingConvention.ProcessModelFinalizing` (only fires when a `DbContext`'s model is finalized — building a bare `ModelBuilder` never calls it). Tests use Sqlite, already referenced by `EntityFramework.Tests.csproj`.

- [ ] **Step 1: Write the failing tests**

Append to `Urdarbrunnr/tests/EntityFramework.Tests/RequireExplicitLengthConventionTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework.Tests;

public sealed class RequireExplicitLengthConventionTests
{
	// ... (attribute tests from Task 1 stay above)

	[Fact]
	public void Unbounded_string_property_throws_on_model_build()
	{
		var act = () => BuildModel<UnboundedContext>();

		var ex = act.ShouldThrow<InvalidOperationException>();
		ex.Message.ShouldContain("UnboundedEntity.Value (String)");
	}

	[Fact]
	public void MaxLength_attribute_satisfies_the_convention()
	{
		Should.NotThrow(() => BuildModel<AttributeBoundedContext>());
	}

	[Fact]
	public void HasMaxLength_fluent_call_satisfies_the_convention()
	{
		Should.NotThrow(() => BuildModel<FluentBoundedContext>());
	}

	[Fact]
	public void UnboundedLength_attribute_passes_as_explicit_negative_one()
	{
		Should.NotThrow(() => BuildModel<ExplicitUnboundedContext>());
	}

	[Fact]
	public void FixedLength_attribute_sets_IsFixedLength_and_satisfies_the_convention()
	{
		using var ctx = CreateContext<FixedLengthContext>();

		var property = ctx.Model.FindEntityType(typeof(FixedLengthEntity))!
			.FindProperty(nameof(FixedLengthEntity.Value))!;

		property.GetMaxLength().ShouldBe(10);
		property.IsFixedLength().ShouldBeTrue();
	}

	[Fact]
	public void Converted_property_with_non_string_storage_type_is_skipped()
	{
		Should.NotThrow(() => BuildModel<ConvertedContext>());
	}

	[Fact]
	public void Collects_every_violation_before_throwing()
	{
		var act = () => BuildModel<MultiUnboundedContext>();

		var ex = act.ShouldThrow<InvalidOperationException>();
		ex.Message.ShouldContain("First (String)");
		ex.Message.ShouldContain("Second (String)");
	}

	static TContext CreateContext<TContext>() where TContext : DbContext =>
		(TContext)Activator.CreateInstance(typeof(TContext),
			new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:").Options)!;

	static void BuildModel<TContext>() where TContext : DbContext
	{
		using var ctx = CreateContext<TContext>();
		_ = ctx.Model;
	}

	sealed class UnboundedEntity
	{
		public int Id { get; set; }
		public string Value { get; set; } = "";
	}

	sealed class UnboundedContext(DbContextOptions<UnboundedContext> options) : NorseDbContext(options)
	{
		public DbSet<UnboundedEntity> Entities => Set<UnboundedEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention());
	}

	sealed class AttributeBoundedEntity
	{
		public int Id { get; set; }

		[MaxLength(25)]
		public string Value { get; set; } = "";
	}

	sealed class AttributeBoundedContext(DbContextOptions<AttributeBoundedContext> options) : NorseDbContext(options)
	{
		public DbSet<AttributeBoundedEntity> Entities => Set<AttributeBoundedEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention());
	}

	sealed class FluentBoundedEntity
	{
		public int Id { get; set; }
		public string Value { get; set; } = "";
	}

	sealed class FluentBoundedContext(DbContextOptions<FluentBoundedContext> options) : NorseDbContext(options)
	{
		public DbSet<FluentBoundedEntity> Entities => Set<FluentBoundedEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention());

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<FluentBoundedEntity>().Property(e => e.Value).HasMaxLength(50);
		}
	}

	sealed class ExplicitUnboundedEntity
	{
		public int Id { get; set; }

		[UnboundedLength]
		public string Value { get; set; } = "";
	}

	sealed class ExplicitUnboundedContext(DbContextOptions<ExplicitUnboundedContext> options) : NorseDbContext(options)
	{
		public DbSet<ExplicitUnboundedEntity> Entities => Set<ExplicitUnboundedEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention());
	}

	sealed class FixedLengthEntity
	{
		public int Id { get; set; }

		[FixedLength(10)]
		public string Value { get; set; } = "";
	}

	sealed class FixedLengthContext(DbContextOptions<FixedLengthContext> options) : NorseDbContext(options)
	{
		public DbSet<FixedLengthEntity> Entities => Set<FixedLengthEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention());
	}

	sealed class ConvertedEntity
	{
		public int Id { get; set; }
		public Guid Value { get; set; }
	}

	sealed class ConvertedContext(DbContextOptions<ConvertedContext> options) : NorseDbContext(options)
	{
		public DbSet<ConvertedEntity> Entities => Set<ConvertedEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention());

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<ConvertedEntity>().Property(e => e.Value).HasConversion<string>().HasMaxLength(36);
		}
	}

	sealed class MultiUnboundedEntity
	{
		public int Id { get; set; }
		public string First { get; set; } = "";
		public string Second { get; set; } = "";
	}

	sealed class MultiUnboundedContext(DbContextOptions<MultiUnboundedContext> options) : NorseDbContext(options)
	{
		public DbSet<MultiUnboundedEntity> Entities => Set<MultiUnboundedEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention());
	}
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj --filter RequireExplicitLengthConventionTests
```

Expected: compile error — `RequireExplicitLengthConvention` not defined.

- [ ] **Step 3: Implement the convention**

`Urdarbrunnr/src/EntityFramework/RequireExplicitLengthConvention.cs`:
```csharp
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Norse.EntityFramework;

sealed class RequireExplicitLengthConvention : IModelFinalizingConvention
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

			if (property.PropertyInfo?.GetCustomAttribute<FixedLengthAttribute>() is not null)
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

- [ ] **Step 4: Run the tests to confirm they pass**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj --filter RequireExplicitLengthConventionTests
```

Expected: 10/10 PASS (3 from Task 1 + 7 above).

- [ ] **Step 5: Stage**

```bash
git -C Urdarbrunnr add \
  src/EntityFramework/RequireExplicitLengthConvention.cs \
  tests/EntityFramework.Tests/RequireExplicitLengthConventionTests.cs
```

---

## Task 3: Urdarbrunnr — `INorseEntity<TSelf>` + `NorseEntityBase<TSelf>`

**Files:**
- Create: `Urdarbrunnr/src/EntityFramework/INorseEntity.cs`
- Create: `Urdarbrunnr/src/EntityFramework/NorseEntityBase.cs`
- Test: `Urdarbrunnr/tests/EntityFramework.Tests/RequireEntityConfigurationConventionTests.cs`

**Interfaces:**
- Produces:
  - `Norse.EntityFramework.INorseEntity<TSelf>` — `static abstract void Configure(EntityTypeBuilder<TSelf> builder);`
  - `Norse.EntityFramework.NorseEntityBase<TSelf>` — abstract, implements `INorseEntity<TSelf>` but deliberately leaves `Configure` unfulfilled (static interface members aren't inherited via virtual dispatch; every concrete `TSelf` must supply its own or the build fails).

- [ ] **Step 1: Write the failing test**

`Urdarbrunnr/tests/EntityFramework.Tests/RequireEntityConfigurationConventionTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.EntityFramework.Tests;

public sealed class RequireEntityConfigurationConventionTests
{
	[Fact]
	public void Tier1_entity_via_NorseEntityBase_must_implement_Configure()
	{
		typeof(Tier1Entity).ShouldBeAssignableTo(typeof(INorseEntity<Tier1Entity>));
	}

	sealed class Tier1Entity : NorseEntityBase<Tier1Entity>
	{
		public int Id { get; set; }

		public static void Configure(EntityTypeBuilder<Tier1Entity> builder) =>
			builder.Property(e => e.Id);
	}
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj --filter RequireEntityConfigurationConventionTests
```

Expected: compile error — `INorseEntity<>`/`NorseEntityBase<>` not defined.

- [ ] **Step 3: Implement `INorseEntity<TSelf>`**

`Urdarbrunnr/src/EntityFramework/INorseEntity.cs`:
```csharp
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.EntityFramework;

/// <summary>
/// Every Norse entity is its own configuration. Implementing this interface obligates the concrete
/// type to supply <see cref="Configure"/> — the compiler refuses to build until it exists. Static
/// (not instance-based like EF Core's own <c>IEntityTypeConfiguration&lt;T&gt;</c>) so the generator
/// (<c>EntityConfigurationApplicationGenerator</c>, Norse.EntityFramework.Configuration.Generator)
/// never constructs an instance purely to call this method.
/// </summary>
public interface INorseEntity<TSelf> where TSelf : INorseEntity<TSelf>
{
	static abstract void Configure(EntityTypeBuilder<TSelf> builder);
}
```

- [ ] **Step 4: Implement `NorseEntityBase<TSelf>`**

`Urdarbrunnr/src/EntityFramework/NorseEntityBase.cs`:
```csharp
namespace Norse.EntityFramework;

/// <summary>
/// Base for Norse-owned entities with no competing base class (Tier 1). Brownfield entities that must
/// inherit a third-party base (<c>IdentityUser&lt;Guid&gt;</c>, etc.) implement
/// <see cref="INorseEntity{TSelf}"/> directly instead (Tier 2) — C# is single-inheritance and the slot
/// is already spent.
/// </summary>
public abstract class NorseEntityBase<TSelf> : INorseEntity<TSelf>
	where TSelf : NorseEntityBase<TSelf>, INorseEntity<TSelf>;
```

- [ ] **Step 5: Run the test to confirm it passes**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj --filter RequireEntityConfigurationConventionTests
```

Expected: PASS.

- [ ] **Step 6: Stage**

```bash
git -C Urdarbrunnr add \
  src/EntityFramework/INorseEntity.cs \
  src/EntityFramework/NorseEntityBase.cs \
  tests/EntityFramework.Tests/RequireEntityConfigurationConventionTests.cs
```

---

## Task 4: Urdarbrunnr — `RequireEntityConfigurationConvention` + `NorseModelConventions.Apply` + `NorseDbContext.ConfigureConventions`

**Files:**
- Create: `Urdarbrunnr/src/EntityFramework/RequireEntityConfigurationConvention.cs`
- Create: `Urdarbrunnr/src/EntityFramework/NorseModelConventions.cs`
- Modify: `Urdarbrunnr/src/EntityFramework/NorseDbContext.cs`
- Test: `Urdarbrunnr/tests/EntityFramework.Tests/RequireEntityConfigurationConventionTests.cs` (extend), `Urdarbrunnr/tests/EntityFramework.Tests/NorseDbContextTests.cs` (new)

**Interfaces:**
- Consumes: `RequireExplicitLengthConvention` (Task 2), `INorseEntity<TSelf>` (Task 3)
- Produces:
  - `Norse.EntityFramework.RequireEntityConfigurationConvention` — `IModelFinalizingConvention`; throws naming every entity type in the finalized model whose CLR type does not close `INorseEntity<TSelf>` over itself. Skips JSON-mapped owned types (`entityType.IsMappedToJson()`) — an owned type serialized into its owner's JSON column was never meant to declare its own `Configure`.
  - `Norse.EntityFramework.NorseModelConventions.Apply(ModelConfigurationBuilder)` — registers both conventions together, one call site.
  - `NorseDbContext.ConfigureConventions` override calling `NorseModelConventions.Apply` — every context inheriting it gets both guarantees for free.

- [ ] **Step 1: Write the failing tests**

Append to `Urdarbrunnr/tests/EntityFramework.Tests/RequireEntityConfigurationConventionTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework.Tests;

public sealed class RequireEntityConfigurationConventionTests
{
	// ... (Tier1Entity test from Task 3 stays above)

	[Fact]
	public void Entity_not_implementing_INorseEntity_throws_on_model_build()
	{
		var act = () => BuildModel<PlainContext>();

		var ex = act.ShouldThrow<InvalidOperationException>();
		ex.Message.ShouldContain(nameof(PlainEntity));
	}

	[Fact]
	public void Entity_implementing_INorseEntity_directly_satisfies_the_convention()
	{
		Should.NotThrow(() => BuildModel<DirectImplementationContext>());
	}

	static void BuildModel<TContext>() where TContext : DbContext
	{
		using TContext ctx = (TContext)Activator.CreateInstance(typeof(TContext),
			new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:").Options)!;
		_ = ctx.Model;
	}

	sealed class PlainEntity
	{
		public int Id { get; set; }
	}

	sealed class PlainContext(DbContextOptions<PlainContext> options) : NorseDbContext(options)
	{
		public DbSet<PlainEntity> Entities => Set<PlainEntity>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<PlainEntity>().Property(e => e.Id);
		}
	}

	sealed class DirectImplementationEntity : INorseEntity<DirectImplementationEntity>
	{
		public int Id { get; set; }

		public static void Configure(
			Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<DirectImplementationEntity> builder) =>
			builder.Property(e => e.Id);
	}

	sealed class DirectImplementationContext(DbContextOptions<DirectImplementationContext> options)
		: NorseDbContext(options)
	{
		public DbSet<DirectImplementationEntity> Entities => Set<DirectImplementationEntity>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<DirectImplementationEntity>().Property(e => e.Id);
		}
	}
}
```

`Urdarbrunnr/tests/EntityFramework.Tests/NorseDbContextTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework.Tests;

public sealed class NorseDbContextTests
{
	[Fact]
	public void ConfigureConventions_registers_both_conventions_by_default()
	{
		// PlainEntity has no length problem (no string/byte[] properties) but does violate
		// RequireEntityConfigurationConvention purely by inheriting NorseDbContext with no override —
		// proving both conventions are wired in without any per-context opt-in call.
		var options = new DbContextOptionsBuilder<UnconfiguredContext>()
			.UseSqlite("Data Source=:memory:").Options;
		using UnconfiguredContext ctx = new(options);

		var act = () => ctx.Model;

		act.ShouldThrow<InvalidOperationException>();
	}

	sealed class PlainEntity
	{
		public int Id { get; set; }
	}

	sealed class UnconfiguredContext(DbContextOptions<UnconfiguredContext> options) : NorseDbContext(options)
	{
		public DbSet<PlainEntity> Entities => Set<PlainEntity>();
	}
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj --filter "RequireEntityConfigurationConventionTests|NorseDbContextTests"
```

Expected: compile error / `RequireEntityConfigurationConvention` not defined; `NorseDbContext.ConfigureConventions` doesn't yet register anything so `UnconfiguredContext` builds without throwing (test fails, not compile-fails, once the type exists — confirm the RED state matches "doesn't throw" before Step 3).

- [ ] **Step 3: Implement `RequireEntityConfigurationConvention`**

`Urdarbrunnr/src/EntityFramework/RequireEntityConfigurationConvention.cs`:
```csharp
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Norse.EntityFramework;

sealed class RequireEntityConfigurationConvention : IModelFinalizingConvention
{
	public void ProcessModelFinalizing(
		IConventionModelBuilder builder, IConventionContext<IConventionModelBuilder> context)
	{
		List<string> violations = [];

		foreach (var entityType in builder.Metadata.GetEntityTypes())
		{
			if (entityType.IsMappedToJson())
				continue;

			var clrType = entityType.ClrType;
			var implementsSelf = clrType.GetInterfaces().Any(i =>
				i.IsGenericType &&
				i.GetGenericTypeDefinition() == typeof(INorseEntity<>) &&
				i.GetGenericArguments()[0] == clrType);

			if (!implementsSelf)
				violations.Add(clrType.FullName!);
		}

		if (violations.Count == 0)
			return;

		throw new InvalidOperationException(
			$"{violations.Count} entit{(violations.Count == 1 ? "y does" : "ies do")} not implement " +
			"INorseEntity<TSelf>. Every Norse entity is its own configuration:\n  - " +
			string.Join("\n  - ", violations));
	}
}
```

- [ ] **Step 4: Implement `NorseModelConventions`**

`Urdarbrunnr/src/EntityFramework/NorseModelConventions.cs`:
```csharp
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Norse.EntityFramework;

public static class NorseModelConventions
{
	public static ModelConfigurationBuilder Apply(ModelConfigurationBuilder configurationBuilder)
	{
		configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention());
		configurationBuilder.Conventions.Add(static _ => new RequireEntityConfigurationConvention());
		return configurationBuilder;
	}
}
```

- [ ] **Step 5: Wire `NorseDbContext.ConfigureConventions`**

Modify `Urdarbrunnr/src/EntityFramework/NorseDbContext.cs` to add the override (alongside the existing `OnConfiguring`):
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Norse.EntityFramework;

public abstract class NorseDbContext(DbContextOptions options) : DbContext(options), INorseDbContext
{
	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		base.OnConfiguring(optionsBuilder);
		NorseDbContextOptionsExtensions.ApplyNorseConventions(optionsBuilder);
	}

	protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
	{
		base.ConfigureConventions(configurationBuilder);
		NorseModelConventions.Apply(configurationBuilder);
	}
}
```

- [ ] **Step 6: Run the tests to confirm they pass**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj --filter "RequireEntityConfigurationConventionTests|NorseDbContextTests"
```

Expected: all PASS. Re-run the full `RequireExplicitLengthConventionTests` suite from Task 2 too — those test classes added their own `RequireExplicitLengthConvention` registration explicitly in `ConfigureConventions`; `NorseDbContext` now also registers it, so confirm no double-registration failure (`ConventionSet` additions are idempotent per-type-instance, not per-call, so this is expected to still pass, but confirm rather than assume):

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj
```

Expected: all tests in the project PASS. If Task 2's manual `ConfigureConventions` overrides in `RequireExplicitLengthConventionTests` now conflict with the base's automatic registration (duplicate convention instances double-running is harmless — both produce the same violations list — but if the test framework surfaces an unexpected duplicate-message assertion failure, simplify those test contexts by deleting their now-redundant manual `ConfigureConventions` override, since `NorseDbContext` provides it already).

- [ ] **Step 7: Stage**

```bash
git -C Urdarbrunnr add \
  src/EntityFramework/RequireEntityConfigurationConvention.cs \
  src/EntityFramework/NorseModelConventions.cs \
  src/EntityFramework/NorseDbContext.cs \
  tests/EntityFramework.Tests/RequireEntityConfigurationConventionTests.cs \
  tests/EntityFramework.Tests/NorseDbContextTests.cs
```

---

## Task 5: Urdarbrunnr — `NorseDbContext.OnModelCreating` + `ConfigureNorseEntities` virtual hook

**Files:**
- Modify: `Urdarbrunnr/src/EntityFramework/NorseDbContext.cs`
- Test: `Urdarbrunnr/tests/EntityFramework.Tests/NorseDbContextTests.cs` (extend)

**Interfaces:**
- Produces: `NorseDbContext.OnModelCreating` override calling `ConfigureNorseEntities(builder)`; `protected virtual void ConfigureNorseEntities(ModelBuilder builder)` with an empty default body. Real polymorphic dispatch — see "Design amendments," point 3, for why this replaces the spec's static-extension-call approach for the Tier-1 case.

This is deliberately the *base-class half* only. The generator's Tier-1 partial-class emission (finding a `partial class : NorseDbContext` with `INorseEntity<T>` types in the same compilation and overriding `ConfigureNorseEntities`) is Task 6 — no Tier-1 consumer exists yet, so this task's test proves the hook fires and is overridable, not that a generator populates it.

- [ ] **Step 1: Write the failing test**

Append to `Urdarbrunnr/tests/EntityFramework.Tests/NorseDbContextTests.cs`:
```csharp
public sealed class NorseDbContextTests
{
	// ... (ConfigureConventions test from Task 4 stays above)

	[Fact]
	public void ConfigureNorseEntities_is_called_during_OnModelCreating_and_is_overridable()
	{
		var options = new DbContextOptionsBuilder<HookOverrideContext>()
			.UseSqlite("Data Source=:memory:").Options;
		using HookOverrideContext ctx = new(options);

		_ = ctx.Model;

		ctx.HookInvoked.ShouldBeTrue();
	}

	sealed class HookEntity : NorseEntityBase<HookEntity>
	{
		public int Id { get; set; }

		public static void Configure(
			Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<HookEntity> builder) =>
			builder.Property(e => e.Id);
	}

	sealed class HookOverrideContext(DbContextOptions<HookOverrideContext> options) : NorseDbContext(options)
	{
		public bool HookInvoked { get; private set; }
		public DbSet<HookEntity> Entities => Set<HookEntity>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<HookEntity>(eb => HookEntity.Configure(eb));
		}

		protected override void ConfigureNorseEntities(ModelBuilder builder)
		{
			base.ConfigureNorseEntities(builder);
			HookInvoked = true;
		}
	}
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj --filter ConfigureNorseEntities_is_called
```

Expected: compile error — `ConfigureNorseEntities` not defined on `NorseDbContext`.

- [ ] **Step 3: Add the hook**

Modify `Urdarbrunnr/src/EntityFramework/NorseDbContext.cs`, adding `OnModelCreating` and `ConfigureNorseEntities` alongside the existing overrides:
```csharp
protected override void OnModelCreating(ModelBuilder builder)
{
	base.OnModelCreating(builder);
	ConfigureNorseEntities(builder);
}

/// <summary>
/// Empty by default. A Tier-1 consumer project declares its own <c>DbContext</c> subclass
/// <c>partial</c> — <see cref="EntityConfigurationApplicationGenerator"/> (in that project's own
/// compilation, alongside its <c>INorseEntity&lt;TSelf&gt;</c> entities) emits a second partial
/// declaration overriding this method. Real virtual dispatch, not a generated static extension call —
/// see the plan's "Design amendments" note on why the static-extension approach can't work for a base
/// class compiled once and shipped as a package.
/// </summary>
protected virtual void ConfigureNorseEntities(ModelBuilder builder)
{
}
```

- [ ] **Step 4: Run the test to confirm it passes**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj --filter ConfigureNorseEntities_is_called
```

Expected: PASS.

- [ ] **Step 5: Stage**

```bash
git -C Urdarbrunnr add \
  src/EntityFramework/NorseDbContext.cs \
  tests/EntityFramework.Tests/NorseDbContextTests.cs
```

---

## Task 6: Urdarbrunnr — `EntityConfigurationApplicationGenerator`

**Files:**
- Create: `Urdarbrunnr/src/EntityFramework.Configuration.Generator/EntityFramework.Configuration.Generator.csproj`
- Create: `Urdarbrunnr/src/EntityFramework.Configuration.Generator/EntityConfigurationApplicationGenerator.cs`
- Create: `Urdarbrunnr/src/EntityFramework.Configuration/EntityFramework.Configuration.csproj`
- Create: `Urdarbrunnr/tests/EntityFramework.Configuration.Generator.Tests/EntityFramework.Configuration.Generator.Tests.csproj`
- Create: `Urdarbrunnr/tests/EntityFramework.Configuration.Generator.Tests/EntityConfigurationApplicationGeneratorTests.cs`
- Modify: `Urdarbrunnr/Urdarbrunnr.slnx`
- Modify: `Bifrost.slnx`

**Interfaces:**
- Consumes: `INorseEntity<TSelf>` (Task 3), `NorseDbContext.ConfigureNorseEntities` (Task 5)
- Produces:
  - `Norse.EntityFramework.Configuration.Generator` NuGet package (analyzer only, `IsPackable=false`, referenced only as an `Analyzer` item — never a runtime dependency).
  - `Norse.EntityFramework.Configuration` NuGet package — thin wrapper (no source of its own) that references `Norse.EntityFramework` and forwards the generator as an analyzer, mirroring `EntityFramework.Migrations.PostgreSQL`'s wrapper+generator-sibling shape exactly (same `IncludeGeneratorInPackage` target). Consumers reference this one package via `NorseRef Generator="true"`.
  - Emitted, per consuming compilation: `internal static class GeneratedNorseModelConfigurations { public static ModelBuilder ApplyNorseConfigurations(this ModelBuilder builder) { ... } }` in the global namespace (one `builder.Entity<T>(eb => T.Configure(eb));` line per discovered `INorseEntity<T>`) — same shape Himinbjörg's `NorseIdentityDbContext` calls directly (Task 14). Additionally: if the compilation contains a `partial class` deriving (directly or transitively) from `Norse.EntityFramework.NorseDbContext`, a second partial declaration overriding `ConfigureNorseEntities(ModelBuilder)` to call `builder.ApplyNorseConfigurations()` — the Tier-1 "free by inheritance" path (Task 5's amendment). No Tier-1 consumer exists in this plan, so this path is proven only by a synthetic test type below, not a real bounded context.

- [ ] **Step 1: Create the generator project**

`Urdarbrunnr/src/EntityFramework.Configuration.Generator/EntityFramework.Configuration.Generator.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.EntityFramework.Configuration.Generator: the Roslyn IIncrementalGenerator that discovers INorseEntity&lt;TSelf&gt; implementations in the compiling project's own syntax tree and emits ApplyNorseConfigurations(), plus a Tier-1 partial-class ConfigureNorseEntities override when a partial NorseDbContext subclass is present.</Description>
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
</Project>
```

- [ ] **Step 2: Create the wrapper package**

`Urdarbrunnr/src/EntityFramework.Configuration/EntityFramework.Configuration.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.EntityFramework.Configuration: pulls in Norse.EntityFramework and ships the Roslyn generator that discovers INorseEntity&lt;TSelf&gt; implementations and emits ApplyNorseConfigurations(). Reference this single package from any project that colocates entities and their DbContext.</Description>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../EntityFramework/EntityFramework.csproj" />
		<ProjectReference
			Include="../EntityFramework.Configuration.Generator/EntityFramework.Configuration.Generator.csproj"
			OutputItemType="Analyzer"
			ReferenceOutputAssembly="false" />
	</ItemGroup>
	<Target Name="IncludeGeneratorInPackage" BeforeTargets="_GetPackageFiles">
		<MSBuild Projects="../EntityFramework.Configuration.Generator/EntityFramework.Configuration.Generator.csproj"
			Targets="Build"
			Properties="Configuration=$(Configuration)" />
		<ItemGroup>
			<None Include="../EntityFramework.Configuration.Generator/bin/$(Configuration)/netstandard2.0/Norse.EntityFramework.Configuration.Generator.dll"
				Pack="true"
				PackagePath="analyzers/dotnet/cs/"
				Visible="false" />
		</ItemGroup>
	</Target>
</Project>
```

- [ ] **Step 3: Create the test project**

`Urdarbrunnr/tests/EntityFramework.Configuration.Generator.Tests/EntityFramework.Configuration.Generator.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="*">
			<PrivateAssets>all</PrivateAssets>
		</PackageReference>
		<ProjectReference Include="../../src/EntityFramework/EntityFramework.csproj" />
		<ProjectReference Include="../../src/EntityFramework.Configuration.Generator/EntityFramework.Configuration.Generator.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 4: Write the failing tests**

`Urdarbrunnr/tests/EntityFramework.Configuration.Generator.Tests/EntityConfigurationApplicationGeneratorTests.cs`:
```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Norse.EntityFramework.Configuration.Generator.Tests;

public sealed class EntityConfigurationApplicationGeneratorTests
{
	[Fact]
	void Generator_emits_ApplyNorseConfigurations_for_Tier1_and_Tier2_entities()
	{
		var source = """
			using Microsoft.EntityFrameworkCore.Metadata.Builders;
			using Norse.EntityFramework;

			sealed class Tier1Entity : NorseEntityBase<Tier1Entity>
			{
				public static void Configure(EntityTypeBuilder<Tier1Entity> builder) { }
			}

			sealed class Tier2Entity : INorseEntity<Tier2Entity>
			{
				public static void Configure(EntityTypeBuilder<Tier2Entity> builder) { }
			}
			""";

		var compilation = CreateCompilation(source);
		var generator = new EntityConfigurationApplicationGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		var generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
		generated.ShouldContain("ApplyNorseConfigurations");
		generated.ShouldContain("Tier1Entity.Configure");
		generated.ShouldContain("Tier2Entity.Configure");
	}

	[Fact]
	void Generator_emits_Tier1_partial_override_for_partial_NorseDbContext_subclass()
	{
		var source = """
			using Microsoft.EntityFrameworkCore;
			using Microsoft.EntityFrameworkCore.Metadata.Builders;
			using Norse.EntityFramework;

			sealed class Tier1Entity : NorseEntityBase<Tier1Entity>
			{
				public static void Configure(EntityTypeBuilder<Tier1Entity> builder) { }
			}

			partial class MyContext(DbContextOptions<MyContext> options) : NorseDbContext(options);
			""";

		var compilation = CreateCompilation(source);
		var generator = new EntityConfigurationApplicationGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		var generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
		generated.ShouldContain("partial class MyContext");
		generated.ShouldContain("ConfigureNorseEntities");
	}

	[Fact]
	void Generator_emits_no_source_when_no_entities_found()
	{
		var compilation = CreateCompilation("// empty");
		var generator = new EntityConfigurationApplicationGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		result.GeneratedTrees.ShouldBeEmpty();
	}

	[Fact]
	void Generator_skips_abstract_and_generic_candidates()
	{
		var source = """
			using Microsoft.EntityFrameworkCore.Metadata.Builders;
			using Norse.EntityFramework;

			abstract class AbstractEntity : NorseEntityBase<AbstractEntity>
			{
				public static void Configure(EntityTypeBuilder<AbstractEntity> builder) { }
			}
			""";

		var compilation = CreateCompilation(source);
		var generator = new EntityConfigurationApplicationGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		result.GeneratedTrees.ShouldBeEmpty();
	}

	static Compilation CreateCompilation(string source)
	{
		var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
		var references = new[]
		{
			typeof(object),
			typeof(Norse.EntityFramework.INorseEntity<>),
			typeof(Microsoft.EntityFrameworkCore.DbContext),
			typeof(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<>),
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

- [ ] **Step 5: Run the tests to confirm they fail**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Configuration.Generator.Tests/EntityFramework.Configuration.Generator.Tests.csproj
```

Expected: compile error — `EntityConfigurationApplicationGenerator` not defined.

- [ ] **Step 6: Implement the generator**

`Urdarbrunnr/src/EntityFramework.Configuration.Generator/EntityConfigurationApplicationGenerator.cs`:
```csharp
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Norse.EntityFramework.Configuration.Generator;

[Generator]
public sealed class EntityConfigurationApplicationGenerator : IIncrementalGenerator
{
	const string NorseEntityInterfaceMetadataName = "Norse.EntityFramework.INorseEntity`1";
	const string NorseDbContextMetadataName = "Norse.EntityFramework.NorseDbContext";

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var entityDeclarations = context.SyntaxProvider.CreateSyntaxProvider(
			static (node, _) => node is ClassDeclarationSyntax { TypeParameterList: null },
			static (ctx, _) => (ClassDeclarationSyntax)ctx.Node);

		var compilationAndClasses = context.CompilationProvider.Combine(entityDeclarations.Collect());

		context.RegisterSourceOutput(compilationAndClasses, static (ctx, source) =>
		{
			var (compilation, classes) = source;
			var entities = FindEntities(compilation, classes);
			var tier1Context = FindPartialTier1Context(compilation, classes);

			if (entities.Count == 0 && tier1Context is null)
				return;

			var text = BuildSource(entities, tier1Context);
			ctx.AddSource("NorseEntityConfigurationExtensions.g.cs", SourceText.From(text, Encoding.UTF8));
		});
	}

	static IList<string> FindEntities(Compilation compilation, IList<ClassDeclarationSyntax> classes)
	{
		List<string> results = [];

		foreach (var classDeclaration in classes)
		{
			var model = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
			if (model.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol type)
				continue;

			if (type.IsAbstract)
				continue;

			var implementsSelf = type.AllInterfaces.Any(i =>
				i.OriginalDefinition.ToDisplayString() == NorseEntityInterfaceMetadataName &&
				i.TypeArguments.Length == 1 &&
				SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type));

			if (implementsSelf)
				results.Add(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
		}

		return results;
	}

	static string? FindPartialTier1Context(Compilation compilation, IList<ClassDeclarationSyntax> classes)
	{
		foreach (var classDeclaration in classes)
		{
			if (!classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
				continue;

			var model = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
			if (model.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol type)
				continue;

			if (InheritsNorseDbContext(type))
				return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		}

		return null;
	}

	static bool InheritsNorseDbContext(INamedTypeSymbol type)
	{
		for (var current = type.BaseType; current is not null; current = current.BaseType)
			if (current.ToDisplayString() == NorseDbContextMetadataName)
				return true;

		return false;
	}

	static string BuildSource(IList<string> entities, string? tier1Context)
	{
		StringBuilder sb = new();
		sb.AppendLine("// <auto-generated />");
		sb.AppendLine("#nullable enable");
		sb.AppendLine("using Microsoft.EntityFrameworkCore;");
		sb.AppendLine();
		sb.AppendLine("internal static class GeneratedNorseModelConfigurations");
		sb.AppendLine("{");
		sb.AppendLine("\tpublic static ModelBuilder ApplyNorseConfigurations(this ModelBuilder builder)");
		sb.AppendLine("\t{");

		foreach (var entity in entities)
			sb.AppendLine($"\t\tbuilder.Entity<{entity}>(eb => {entity}.Configure(eb));");

		sb.AppendLine("\t\treturn builder;");
		sb.AppendLine("\t}");
		sb.AppendLine("}");

		if (tier1Context is not null)
		{
			sb.AppendLine();
			sb.AppendLine($"partial class {StripGlobalPrefix(tier1Context)}");
			sb.AppendLine("{");
			sb.AppendLine("\tprotected override void ConfigureNorseEntities(ModelBuilder builder)");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\tbase.ConfigureNorseEntities(builder);");
			sb.AppendLine("\t\tbuilder.ApplyNorseConfigurations();");
			sb.AppendLine("\t}");
			sb.AppendLine("}");
		}

		return sb.ToString();
	}

	static string StripGlobalPrefix(string fullyQualifiedName) =>
		fullyQualifiedName.StartsWith("global::", StringComparison.Ordinal)
			? fullyQualifiedName["global::".Length..]
			: fullyQualifiedName;
}
```

- [ ] **Step 7: Run the tests to confirm they pass**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Configuration.Generator.Tests/EntityFramework.Configuration.Generator.Tests.csproj
```

Expected: 4/4 PASS.

- [ ] **Step 8: Add projects to `Urdarbrunnr.slnx` and `Bifrost.slnx`**

Add to `Urdarbrunnr/Urdarbrunnr.slnx` (in `/src/` and `/tests/` folders):
```xml
<Project Path="src/EntityFramework.Configuration.Generator/EntityFramework.Configuration.Generator.csproj" />
<Project Path="src/EntityFramework.Configuration/EntityFramework.Configuration.csproj" />
```
```xml
<Project Path="tests/EntityFramework.Configuration.Generator.Tests/EntityFramework.Configuration.Generator.Tests.csproj" />
```

Add the matching two `<Project Path="Urdarbrunnr/src/...` lines to `Bifrost.slnx`'s existing `/EntityFramework/src/` folder, and the test project line to `/EntityFramework/tests/`.

- [ ] **Step 9: Stage**

```bash
git -C Urdarbrunnr add \
  Urdarbrunnr.slnx \
  src/EntityFramework.Configuration.Generator/EntityFramework.Configuration.Generator.csproj \
  src/EntityFramework.Configuration.Generator/EntityConfigurationApplicationGenerator.cs \
  src/EntityFramework.Configuration/EntityFramework.Configuration.csproj \
  tests/EntityFramework.Configuration.Generator.Tests/EntityFramework.Configuration.Generator.Tests.csproj \
  tests/EntityFramework.Configuration.Generator.Tests/EntityConfigurationApplicationGeneratorTests.cs
git add Bifrost.slnx
```

---

## SHIP GATE — Urdarbrunnr

**STOP. Do not start Task 7 until this gate is cleared.**

1. Commit and push all of Tasks 1–6 (`Norse.EntityFramework` changes, the two new `EntityFramework.Configuration*` projects, and their tests).
2. Open a PR against `master`; confirm GitHub CI (build + test) is green.
3. Merge the PR.
4. Push a version tag to trigger the release pipeline.
5. Confirm `Norse.EntityFramework` (updated), `Norse.EntityFramework.Configuration`, and `Norse.EntityFramework.Configuration.Generator` are published to the NuGet feed.

Only after the packages are live does Task 7 begin.

---

## Task 7: Himinbjörg — `IdentityValueConverters` + `NorseUser`

**Files:**
- Create: `Himinbjorg/src/Identity/IdentityValueConverters.cs`
- Modify: `Himinbjorg/src/Identity/NorseUser.cs`
- Modify: `Himinbjorg/src/Identity/NorseUserClaim.cs` (add `User` navigation only — `Configure` for this entity is Task 9)
- Modify: `Himinbjorg/src/Identity/NorseUserLogin.cs` (add `User` navigation only — `Configure` is Task 10)
- Modify: `Himinbjorg/src/Identity/NorseUserToken.cs` (add `User` navigation only — `Configure` is Task 10)
- Modify: `Himinbjorg/src/Identity/NorseUserPasskey.cs` (add `User` navigation only — `Configure` is Task 11)
- Test: `Himinbjorg/tests/Identity.Tests/NorseUserConfigureTests.cs`

**Interfaces:**
- Consumes: `INorseEntity<TSelf>` (Urdarbrunnr, Task 3, now published)
- Produces: `Norse.Identity.IdentityValueConverters.Stamp`/`.Hash` — `ValueConverter<string?, Guid?>`/`ValueConverter<string?, byte[]?>`, internal, reused by `NorseUser.Configure` (this task) and `NorseRole.Configure` (Task 8). `NorseUser` implements `INorseEntity<NorseUser>` with `Claims`/`Logins`/`Tokens`/`Passkeys` navigation collections.

Navigation properties are added to the four dependent entities *now* (this task) because `NorseUser.Configure`'s `HasMany(...).WithOne(c => c.User)` calls need the reverse `User` property to compile — but each dependent entity's own `Configure` method (and its own test) lands in the task that owns that entity, to keep this task's diff focused on `NorseUser` itself.

- [ ] **Step 1: Write the failing test**

`Himinbjorg/tests/Identity.Tests/NorseUserConfigureTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.Identity.Tests;

public sealed class NorseUserConfigureTests
{
	[Fact]
	public void Configure_sets_table_name()
	{
		var entityType = BuildEntityType();

		entityType.GetTableName().ShouldBe("users");
	}

	[Fact]
	public void Configure_bounds_PasswordHash_and_converts_it()
	{
		var entityType = BuildEntityType();
		var property = entityType.FindProperty(nameof(NorseUser.PasswordHash))!;

		property.GetMaxLength().ShouldBe(128);
		property.GetValueConverter().ShouldNotBeNull();
	}

	[Fact]
	public void Configure_bounds_PhoneNumber()
	{
		var entityType = BuildEntityType();

		entityType.FindProperty(nameof(NorseUser.PhoneNumber))!.GetMaxLength().ShouldBe(20);
	}

	[Fact]
	public void Configure_converts_ConcurrencyStamp_and_SecurityStamp()
	{
		var entityType = BuildEntityType();

		entityType.FindProperty(nameof(NorseUser.ConcurrencyStamp))!.GetValueConverter().ShouldNotBeNull();
		entityType.FindProperty(nameof(NorseUser.SecurityStamp))!.GetValueConverter().ShouldNotBeNull();
	}

	[Fact]
	public void Configure_wires_Claims_relationship_through_the_User_navigation()
	{
		var model = BuildModel();
		var claimType = model.FindEntityType(typeof(NorseUserClaim))!;
		var fk = claimType.GetForeignKeys().Single();

		fk.DependentToPrincipal!.Name.ShouldBe(nameof(NorseUserClaim.User));
		fk.IsRequired.ShouldBeTrue();
	}

	[Fact]
	public void Configure_sets_unique_index_on_NormalizedUserName()
	{
		var entityType = BuildEntityType();
		var index = entityType.GetIndexes().Single(i => i.GetDatabaseName() == "ix_users_normalized_user_name");

		index.IsUnique.ShouldBeTrue();
	}

	static Microsoft.EntityFrameworkCore.Metadata.IEntityType FindType<T>(
		Microsoft.EntityFrameworkCore.Metadata.IModel model) => model.FindEntityType(typeof(T))!;

	static Microsoft.EntityFrameworkCore.Metadata.IEntityType BuildEntityType() => FindType<NorseUser>(BuildModel());

	static Microsoft.EntityFrameworkCore.Metadata.IModel BuildModel()
	{
		ModelBuilder builder = new();
		builder.Entity<NorseUser>(eb => NorseUser.Configure(eb));
		builder.Entity<NorseUserClaim>();
		builder.Entity<NorseUserLogin>();
		builder.Entity<NorseUserToken>();
		builder.Entity<NorseUserPasskey>(eb => eb.HasKey(p => p.CredentialId));
		return builder.Model;
	}
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj --filter NorseUserConfigureTests
```

Expected: compile error — `NorseUser` doesn't implement `INorseEntity<NorseUser>`, no `Configure` method, no `Claims`/`Logins`/`Tokens`/`Passkeys` properties; `NorseUserClaim`/`Login`/`Token`/`Passkey` have no `User` property.

- [ ] **Step 3: Create `IdentityValueConverters`**

`Himinbjorg/src/Identity/IdentityValueConverters.cs`:
```csharp
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Norse.Identity;

/// <summary>
/// Shared value converters for ASP.NET Core Identity's stamp/hash columns — used by both
/// <see cref="NorseUser"/> and <see cref="NorseRole"/>. Realm-internal; not promoted to Urdarbrunnr
/// since no other realm runs ASP.NET Core Identity today.
/// </summary>
static class IdentityValueConverters
{
	public static readonly ValueConverter<string?, Guid?> Stamp = new(
		static s => s != null ? Guid.Parse(s) : null,
		static g => g.HasValue ? g.ToString() : null);

	public static readonly ValueConverter<string?, byte[]?> Hash = new(
		static s => s != null ? Convert.FromBase64String(s) : null,
		static b => b != null ? Convert.ToBase64String(b) : null);
}
```

- [ ] **Step 4: Add `User` navigation to the four dependent entities**

Modify `Himinbjorg/src/Identity/NorseUserClaim.cs`:
```csharp
using Microsoft.AspNetCore.Identity;

namespace Norse.Identity;

/// <summary>
/// Norse platform ASP.NET Core Identity user-claim entity, keyed by <see cref="Guid"/>.
/// </summary>
public sealed class NorseUserClaim : IdentityUserClaim<Guid>
{
	public required NorseUser User { get; init; }
}
```

Modify `Himinbjorg/src/Identity/NorseUserLogin.cs`:
```csharp
using Microsoft.AspNetCore.Identity;

namespace Norse.Identity;

/// <summary>
/// Norse platform ASP.NET Core Identity external-login entity, keyed by <see cref="Guid"/>.
/// </summary>
public sealed class NorseUserLogin : IdentityUserLogin<Guid>
{
	public required NorseUser User { get; init; }
}
```

Modify `Himinbjorg/src/Identity/NorseUserToken.cs`:
```csharp
using Microsoft.AspNetCore.Identity;

namespace Norse.Identity;

/// <summary>
/// Norse platform ASP.NET Core Identity user-token entity, keyed by <see cref="Guid"/>.
/// </summary>
public sealed class NorseUserToken : IdentityUserToken<Guid>
{
	public required NorseUser User { get; init; }
}
```

Modify `Himinbjorg/src/Identity/NorseUserPasskey.cs`:
```csharp
using Microsoft.AspNetCore.Identity;

namespace Norse.Identity;

/// <summary>
/// Norse platform ASP.NET Core Identity passkey entity, keyed by <see cref="Guid"/>.
/// </summary>
public sealed class NorseUserPasskey : IdentityUserPasskey<Guid>
{
	public required NorseUser User { get; init; }
}
```

(These four do not yet implement `INorseEntity<TSelf>` or declare `Configure` — that lands in Tasks 9–11, the tasks that own each entity's full configuration. This task only adds the navigation property Task 7's own test needs to compile.)

- [ ] **Step 5: Implement `NorseUser`**

Modify `Himinbjorg/src/Identity/NorseUser.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.EntityFramework;

namespace Norse.Identity;

/// <summary>
/// Norse platform ASP.NET Core Identity user entity, keyed by <see cref="Guid"/>.
/// </summary>
public sealed class NorseUser : IdentityUser<Guid>, INorseEntity<NorseUser>
{
	public ICollection<NorseUserClaim> Claims { get; init; } = [];
	public ICollection<NorseUserLogin> Logins { get; init; } = [];
	public ICollection<NorseUserToken> Tokens { get; init; } = [];
	public ICollection<NorseUserPasskey> Passkeys { get; init; } = [];

	/// <inheritdoc />
	public static void Configure(EntityTypeBuilder<NorseUser> builder)
	{
		builder.ToTable("users");
		builder.Property(u => u.ConcurrencyStamp).HasConversion(IdentityValueConverters.Stamp);
		builder.Property(u => u.SecurityStamp).HasConversion(IdentityValueConverters.Stamp);
		builder.Property(u => u.PasswordHash).HasConversion(IdentityValueConverters.Hash).HasMaxLength(128);
		builder.Property(u => u.PhoneNumber).HasMaxLength(20);

		builder.HasMany(u => u.Claims).WithOne(c => c.User).HasForeignKey(c => c.UserId).IsRequired();
		builder.HasMany(u => u.Logins).WithOne(l => l.User).HasForeignKey(l => l.UserId).IsRequired();
		builder.HasMany(u => u.Tokens).WithOne(t => t.User).HasForeignKey(t => t.UserId).IsRequired();
		builder.HasMany(u => u.Passkeys).WithOne(p => p.User).HasForeignKey(p => p.UserId).IsRequired();
		builder.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ix_users_normalized_email");
		builder.HasIndex(u => u.NormalizedUserName).IsUnique().HasDatabaseName("ix_users_normalized_user_name");
	}
}
```

- [ ] **Step 6: Run the test to confirm it passes**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj --filter NorseUserConfigureTests
```

Expected: 6/6 PASS.

- [ ] **Step 7: Stage**

```bash
git -C Himinbjorg add \
  src/Identity/IdentityValueConverters.cs \
  src/Identity/NorseUser.cs \
  src/Identity/NorseUserClaim.cs \
  src/Identity/NorseUserLogin.cs \
  src/Identity/NorseUserToken.cs \
  src/Identity/NorseUserPasskey.cs \
  tests/Identity.Tests/NorseUserConfigureTests.cs
```

---

## Task 8: Himinbjörg — `NorseRole`

**Files:**
- Modify: `Himinbjorg/src/Identity/NorseRole.cs`
- Modify: `Himinbjorg/src/Identity/NorseRoleClaim.cs` (add `Role` navigation only — `Configure` is Task 9)
- Test: `Himinbjorg/tests/Identity.Tests/NorseRoleConfigureTests.cs`

**Interfaces:**
- Consumes: `IdentityValueConverters.Stamp` (Task 7)
- Produces: `NorseRole : IdentityRole<Guid>, INorseEntity<NorseRole>` with `Claims` navigation.

- [ ] **Step 1: Write the failing test**

`Himinbjorg/tests/Identity.Tests/NorseRoleConfigureTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.Identity.Tests;

public sealed class NorseRoleConfigureTests
{
	[Fact]
	public void Configure_sets_table_name()
	{
		BuildEntityType().GetTableName().ShouldBe("roles");
	}

	[Fact]
	public void Configure_converts_ConcurrencyStamp()
	{
		BuildEntityType().FindProperty(nameof(NorseRole.ConcurrencyStamp))!.GetValueConverter().ShouldNotBeNull();
	}

	[Fact]
	public void Configure_sets_unique_index_on_NormalizedName()
	{
		var index = BuildEntityType().GetIndexes()
			.Single(i => i.GetDatabaseName() == "ix_roles_normalized_name");

		index.IsUnique.ShouldBeTrue();
	}

	[Fact]
	public void Configure_wires_Claims_relationship_through_the_Role_navigation()
	{
		ModelBuilder builder = new();
		builder.Entity<NorseRole>(eb => NorseRole.Configure(eb));
		builder.Entity<NorseRoleClaim>();

		var claimType = builder.Model.FindEntityType(typeof(NorseRoleClaim))!;
		var fk = claimType.GetForeignKeys().Single();

		fk.DependentToPrincipal!.Name.ShouldBe(nameof(NorseRoleClaim.Role));
		fk.IsRequired.ShouldBeTrue();
	}

	static Microsoft.EntityFrameworkCore.Metadata.IEntityType BuildEntityType()
	{
		ModelBuilder builder = new();
		builder.Entity<NorseRole>(eb => NorseRole.Configure(eb));
		builder.Entity<NorseRoleClaim>();
		return builder.Model.FindEntityType(typeof(NorseRole))!;
	}
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj --filter NorseRoleConfigureTests
```

Expected: compile error.

- [ ] **Step 3: Add `Role` navigation to `NorseRoleClaim`**

Modify `Himinbjorg/src/Identity/NorseRoleClaim.cs`:
```csharp
using Microsoft.AspNetCore.Identity;

namespace Norse.Identity;

/// <summary>
/// Norse platform ASP.NET Core Identity role-claim entity, keyed by <see cref="Guid"/>.
/// </summary>
public sealed class NorseRoleClaim : IdentityRoleClaim<Guid>
{
	public required NorseRole Role { get; init; }
}
```

- [ ] **Step 4: Implement `NorseRole`**

Modify `Himinbjorg/src/Identity/NorseRole.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.EntityFramework;

namespace Norse.Identity;

/// <summary>
/// Norse platform ASP.NET Core Identity role entity, keyed by <see cref="Guid"/>.
/// </summary>
public sealed class NorseRole : IdentityRole<Guid>, INorseEntity<NorseRole>
{
	public ICollection<NorseRoleClaim> Claims { get; init; } = [];

	/// <inheritdoc />
	public static void Configure(EntityTypeBuilder<NorseRole> builder)
	{
		builder.ToTable("roles");
		builder.Property(r => r.ConcurrencyStamp).HasConversion(IdentityValueConverters.Stamp);
		builder.HasMany(r => r.Claims).WithOne(c => c.Role).HasForeignKey(c => c.RoleId).IsRequired();
		builder.HasIndex(r => r.NormalizedName).IsUnique().HasDatabaseName("ix_roles_normalized_name");
	}
}
```

- [ ] **Step 5: Run the test to confirm it passes**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj --filter NorseRoleConfigureTests
```

Expected: 4/4 PASS.

- [ ] **Step 6: Stage**

```bash
git -C Himinbjorg add \
  src/Identity/NorseRole.cs \
  src/Identity/NorseRoleClaim.cs \
  tests/Identity.Tests/NorseRoleConfigureTests.cs
```

---

## Task 9: Himinbjörg — `NorseRoleClaim` + `NorseUserClaim` `Configure`

**Files:**
- Modify: `Himinbjorg/src/Identity/NorseRoleClaim.cs`
- Modify: `Himinbjorg/src/Identity/NorseUserClaim.cs`

Both entities' `ClaimType`/`ClaimValue` columns are already bounded by `IdentityDbContext`'s own base `OnModelCreating` — verify this assumption directly rather than trust the spec's claim (Task 14's integration test is the real arbiter; if either column is in fact unbounded, `RequireExplicitLengthConvention` throws there by name and this task's `Configure` gets a `HasMaxLength` call added). `Configure` exists on both purely to satisfy `RequireEntityConfigurationConvention` and to colocate for discoverability, per spec §4.2.

No new test file — `NorseRoleClaim`/`NorseUserClaim`'s relationship wiring is already asserted from the `NorseRole`/`NorseUser` side (Tasks 7–8); this task only adds the (currently missing) `INorseEntity<TSelf>` implementation these two need to stop violating `RequireEntityConfigurationConvention` once Task 14 builds the full model.

- [ ] **Step 1: Implement `Configure` on both**

Modify `Himinbjorg/src/Identity/NorseRoleClaim.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.EntityFramework;

namespace Norse.Identity;

/// <summary>
/// Norse platform ASP.NET Core Identity role-claim entity, keyed by <see cref="Guid"/>.
/// </summary>
public sealed class NorseRoleClaim : IdentityRoleClaim<Guid>, INorseEntity<NorseRoleClaim>
{
	public required NorseRole Role { get; init; }

	/// <inheritdoc />
	public static void Configure(EntityTypeBuilder<NorseRoleClaim> builder)
	{
		// ClaimType/ClaimValue are already bounded by IdentityDbContext's own base OnModelCreating.
		// Configure exists to satisfy RequireEntityConfigurationConvention and colocate for discoverability.
	}
}
```

Modify `Himinbjorg/src/Identity/NorseUserClaim.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.EntityFramework;

namespace Norse.Identity;

/// <summary>
/// Norse platform ASP.NET Core Identity user-claim entity, keyed by <see cref="Guid"/>.
/// </summary>
public sealed class NorseUserClaim : IdentityUserClaim<Guid>, INorseEntity<NorseUserClaim>
{
	public required NorseUser User { get; init; }

	/// <inheritdoc />
	public static void Configure(EntityTypeBuilder<NorseUserClaim> builder)
	{
		// ClaimType/ClaimValue are already bounded by IdentityDbContext's own base OnModelCreating.
		// Configure exists to satisfy RequireEntityConfigurationConvention and colocate for discoverability.
	}
}
```

- [ ] **Step 2: Build to confirm no regressions**

```bash
dotnet build Himinbjorg/src/Identity/Identity.csproj
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj --filter "NorseUserConfigureTests|NorseRoleConfigureTests"
```

Expected: build succeeds; both test classes still pass (their relationship assertions from Tasks 7–8 are unaffected by adding `Configure`).

- [ ] **Step 3: Stage**

```bash
git -C Himinbjorg add \
  src/Identity/NorseRoleClaim.cs \
  src/Identity/NorseUserClaim.cs
```

---

## Task 10: Himinbjörg — `NorseUserLogin` + `NorseUserToken` `Configure`

**Files:**
- Modify: `Himinbjorg/src/Identity/NorseUserLogin.cs`
- Modify: `Himinbjorg/src/Identity/NorseUserToken.cs`
- Test: `Himinbjorg/tests/Identity.Tests/NorseUserLoginConfigureTests.cs`
- Test: `Himinbjorg/tests/Identity.Tests/NorseUserTokenConfigureTests.cs`

**Lengths** (resolved during planning — spec left these as prose only): `LoginProvider` 128, `ProviderKey` 256, `ProviderDisplayName` 256 on `NorseUserLogin`; `LoginProvider` 128, `Name` 128 on `NorseUserToken`; `Value` `-1` (already decided, spec §4.2).

- [ ] **Step 1: Write the failing tests**

`Himinbjorg/tests/Identity.Tests/NorseUserLoginConfigureTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.Identity.Tests;

public sealed class NorseUserLoginConfigureTests
{
	[Fact]
	public void Configure_bounds_LoginProvider_ProviderKey_and_ProviderDisplayName()
	{
		ModelBuilder builder = new();
		builder.Entity<NorseUserLogin>(eb => NorseUserLogin.Configure(eb));

		var entityType = builder.Model.FindEntityType(typeof(NorseUserLogin))!;
		entityType.FindProperty(nameof(NorseUserLogin.LoginProvider))!.GetMaxLength().ShouldBe(128);
		entityType.FindProperty(nameof(NorseUserLogin.ProviderKey))!.GetMaxLength().ShouldBe(256);
		entityType.FindProperty(nameof(NorseUserLogin.ProviderDisplayName))!.GetMaxLength().ShouldBe(256);
	}
}
```

`Himinbjorg/tests/Identity.Tests/NorseUserTokenConfigureTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.Identity.Tests;

public sealed class NorseUserTokenConfigureTests
{
	[Fact]
	public void Configure_bounds_LoginProvider_and_Name()
	{
		ModelBuilder builder = new();
		builder.Entity<NorseUserToken>(eb => NorseUserToken.Configure(eb));

		var entityType = builder.Model.FindEntityType(typeof(NorseUserToken))!;
		entityType.FindProperty(nameof(NorseUserToken.LoginProvider))!.GetMaxLength().ShouldBe(128);
		entityType.FindProperty(nameof(NorseUserToken.Name))!.GetMaxLength().ShouldBe(128);
	}

	[Fact]
	public void Configure_declares_Value_explicitly_unbounded()
	{
		ModelBuilder builder = new();
		builder.Entity<NorseUserToken>(eb => NorseUserToken.Configure(eb));

		builder.Model.FindEntityType(typeof(NorseUserToken))!
			.FindProperty(nameof(NorseUserToken.Value))!.GetMaxLength().ShouldBe(-1);
	}
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj --filter "NorseUserLoginConfigureTests|NorseUserTokenConfigureTests"
```

Expected: compile error — no `Configure` method on either type yet.

- [ ] **Step 3: Implement both `Configure` methods**

Modify `Himinbjorg/src/Identity/NorseUserLogin.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.EntityFramework;

namespace Norse.Identity;

/// <summary>
/// Norse platform ASP.NET Core Identity external-login entity, keyed by <see cref="Guid"/>.
/// </summary>
public sealed class NorseUserLogin : IdentityUserLogin<Guid>, INorseEntity<NorseUserLogin>
{
	public required NorseUser User { get; init; }

	/// <inheritdoc />
	public static void Configure(EntityTypeBuilder<NorseUserLogin> builder)
	{
		builder.Property(l => l.LoginProvider).HasMaxLength(128);
		builder.Property(l => l.ProviderKey).HasMaxLength(256);
		builder.Property(l => l.ProviderDisplayName).HasMaxLength(256);
	}
}
```

Modify `Himinbjorg/src/Identity/NorseUserToken.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.EntityFramework;

namespace Norse.Identity;

/// <summary>
/// Norse platform ASP.NET Core Identity user-token entity, keyed by <see cref="Guid"/>.
/// </summary>
public sealed class NorseUserToken : IdentityUserToken<Guid>, INorseEntity<NorseUserToken>
{
	public required NorseUser User { get; init; }

	/// <inheritdoc />
	public static void Configure(EntityTypeBuilder<NorseUserToken> builder)
	{
		builder.Property(t => t.LoginProvider).HasMaxLength(128);
		builder.Property(t => t.Name).HasMaxLength(128);
		builder.Property(t => t.Value).HasMaxLength(-1);
	}
}
```

- [ ] **Step 4: Run the tests to confirm they pass**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj --filter "NorseUserLoginConfigureTests|NorseUserTokenConfigureTests"
```

Expected: 3/3 PASS.

- [ ] **Step 5: Stage**

```bash
git -C Himinbjorg add \
  src/Identity/NorseUserLogin.cs \
  src/Identity/NorseUserToken.cs \
  tests/Identity.Tests/NorseUserLoginConfigureTests.cs \
  tests/Identity.Tests/NorseUserTokenConfigureTests.cs
```

---

## Task 11: Himinbjörg — `NorseUserPasskey` `Configure`

**Files:**
- Modify: `Himinbjorg/src/Identity/NorseUserPasskey.cs`
- Test: `Himinbjorg/tests/Identity.Tests/NorseUserPasskeyConfigureTests.cs`

**Interfaces:**
- Produces: `NorseUserPasskey : IdentityUserPasskey<Guid>, INorseEntity<NorseUserPasskey>` — `HasKey(p => p.CredentialId)`, `Data` owned and JSON-mapped.

- [ ] **Step 1: Write the failing test**

`Himinbjorg/tests/Identity.Tests/NorseUserPasskeyConfigureTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.Identity.Tests;

public sealed class NorseUserPasskeyConfigureTests
{
	[Fact]
	public void Configure_sets_CredentialId_as_the_primary_key()
	{
		ModelBuilder builder = new();
		builder.Entity<NorseUserPasskey>(eb => NorseUserPasskey.Configure(eb));

		var entityType = builder.Model.FindEntityType(typeof(NorseUserPasskey))!;
		entityType.FindPrimaryKey()!.Properties.Single().Name.ShouldBe(nameof(NorseUserPasskey.CredentialId));
	}

	[Fact]
	public void Configure_maps_Data_as_an_owned_JSON_column()
	{
		ModelBuilder builder = new();
		builder.Entity<NorseUserPasskey>(eb => NorseUserPasskey.Configure(eb));

		var ownedType = builder.Model.GetEntityTypes()
			.Single(t => t.ClrType == typeof(Microsoft.AspNetCore.Identity.IdentityPasskeyData));

		ownedType.IsOwned().ShouldBeTrue();
		ownedType.IsMappedToJson().ShouldBeTrue();
	}
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj --filter NorseUserPasskeyConfigureTests
```

Expected: compile error.

- [ ] **Step 3: Implement `Configure`**

Modify `Himinbjorg/src/Identity/NorseUserPasskey.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.EntityFramework;

namespace Norse.Identity;

/// <summary>
/// Norse platform ASP.NET Core Identity passkey entity, keyed by <see cref="Guid"/>.
/// </summary>
public sealed class NorseUserPasskey : IdentityUserPasskey<Guid>, INorseEntity<NorseUserPasskey>
{
	public required NorseUser User { get; init; }

	/// <inheritdoc />
	public static void Configure(EntityTypeBuilder<NorseUserPasskey> builder)
	{
		builder.HasKey(p => p.CredentialId);
		builder.OwnsOne(p => p.Data, o => o.ToJson());
	}
}
```

- [ ] **Step 4: Run the test to confirm it passes**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj --filter NorseUserPasskeyConfigureTests
```

Expected: 2/2 PASS. If `Configure_maps_Data_as_an_owned_JSON_column` fails because `IdentityPasskeyData` isn't the exact type name, check the actual property type of `IdentityUserPasskey<TKey>.Data` (`dotnet-trace`/IDE "Go to Definition" against the restored `Microsoft.Extensions.Identity.Stores` package) and correct the test — do not weaken the assertion to skip the type check.

- [ ] **Step 5: Stage**

```bash
git -C Himinbjorg add \
  src/Identity/NorseUserPasskey.cs \
  tests/Identity.Tests/NorseUserPasskeyConfigureTests.cs
```

---

## Task 12: Himinbjörg — `NorseUserRole` (explicit bridge entity)

**Files:**
- Modify: `Himinbjorg/src/Identity/NorseUserRole.cs`
- Test: `Himinbjorg/tests/Identity.Tests/NorseUserRoleConfigureTests.cs`

Per the new Norse EF law (this plan's "Design amendments," point 2): the `User`↔`Role` many-to-many gets an explicit bridge entity, never EF's implicit skip-navigation join. `NorseUserRole` already exists as that explicit join type (`IdentityUserRole<Guid>`, with `UserId`/`RoleId` FK scalars already declared by the ASP.NET Core Identity base) — this task adds the two navigation properties and `Configure`.

- [ ] **Step 1: Write the failing test**

`Himinbjorg/tests/Identity.Tests/NorseUserRoleConfigureTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.Identity.Tests;

public sealed class NorseUserRoleConfigureTests
{
	[Fact]
	public void Configure_sets_table_name()
	{
		BuildEntityType().GetTableName().ShouldBe("user_roles");
	}

	[Fact]
	public void Configure_wires_explicit_User_and_Role_navigations()
	{
		var entityType = BuildEntityType();
		var foreignKeys = entityType.GetForeignKeys().ToList();

		foreignKeys.ShouldContain(fk =>
			fk.DependentToPrincipal!.Name == nameof(NorseUserRole.User) && fk.IsRequired);
		foreignKeys.ShouldContain(fk =>
			fk.DependentToPrincipal!.Name == nameof(NorseUserRole.Role) && fk.IsRequired);
	}

	static Microsoft.EntityFrameworkCore.Metadata.IEntityType BuildEntityType()
	{
		ModelBuilder builder = new();
		builder.Entity<NorseUserRole>(eb => NorseUserRole.Configure(eb));
		return builder.Model.FindEntityType(typeof(NorseUserRole))!;
	}
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj --filter NorseUserRoleConfigureTests
```

Expected: compile error.

- [ ] **Step 3: Implement `NorseUserRole`**

Modify `Himinbjorg/src/Identity/NorseUserRole.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.EntityFramework;

namespace Norse.Identity;

/// <summary>
/// Norse platform ASP.NET Core Identity user-role join entity, keyed by <see cref="Guid"/>. The
/// explicit bridge entity for the User&#8596;Role many-to-many — enables projection queries directly
/// against the join row, which EF Core's implicit skip-navigation many-to-many cannot do without
/// dropping into raw SQL.
/// </summary>
public sealed class NorseUserRole : IdentityUserRole<Guid>, INorseEntity<NorseUserRole>
{
	public required NorseUser User { get; init; }
	public required NorseRole Role { get; init; }

	/// <inheritdoc />
	public static void Configure(EntityTypeBuilder<NorseUserRole> builder)
	{
		builder.ToTable("user_roles");
		builder.HasOne(ur => ur.User).WithMany().HasForeignKey(ur => ur.UserId).IsRequired();
		builder.HasOne(ur => ur.Role).WithMany().HasForeignKey(ur => ur.RoleId).IsRequired();
	}
}
```

- [ ] **Step 4: Run the test to confirm it passes**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj --filter NorseUserRoleConfigureTests
```

Expected: 2/2 PASS.

- [ ] **Step 5: Stage**

```bash
git -C Himinbjorg add \
  src/Identity/NorseUserRole.cs \
  tests/Identity.Tests/NorseUserRoleConfigureTests.cs
```

---

## Task 13: Himinbjörg — `NorseOpenIddict*` wrapper entities (explicit FK properties, closing OpenIddict's shadow FKs)

**Files:**
- Create: `Himinbjorg/src/Identity/NorseOpenIddictApplication.cs`
- Create: `Himinbjorg/src/Identity/NorseOpenIddictAuthorization.cs`
- Create: `Himinbjorg/src/Identity/NorseOpenIddictScope.cs`
- Create: `Himinbjorg/src/Identity/NorseOpenIddictToken.cs`
- Test: `Himinbjorg/tests/Identity.Tests/NorseOpenIddictEntitiesConfigureTests.cs`

**Interfaces:**
- Consumes: `INorseEntity<TSelf>` (Urdarbrunnr)
- Produces: four wrapper entities. `NorseOpenIddictAuthorization`/`NorseOpenIddictToken` add explicit `Guid? ApplicationId`/`AuthorizationId` FK scalar properties OpenIddict's own base classes don't declare (verified by reflection during planning: `OpenIddictEntityFrameworkCoreAuthorization<,,>`/`Token<,,>` declare only navigation properties — `Application`, `Authorization` — no FK scalars; EF would otherwise fall back to shadow properties, which the new Norse EF law forbids outside audit columns).

- [ ] **Step 1: Write the failing tests**

`Himinbjorg/tests/Identity.Tests/NorseOpenIddictEntitiesConfigureTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.Identity.Tests;

public sealed class NorseOpenIddictEntitiesConfigureTests
{
	[Fact]
	public void Application_Configure_bounds_ClientSecret_and_DisplayName()
	{
		var entityType = BuildEntityType<NorseOpenIddictApplication>(eb => NorseOpenIddictApplication.Configure(eb));

		entityType.FindProperty(nameof(NorseOpenIddictApplication.ClientSecret))!.GetMaxLength().ShouldBe(-1);
		entityType.FindProperty(nameof(NorseOpenIddictApplication.DisplayName))!.GetMaxLength().ShouldBe(200);
	}

	[Fact]
	public void Authorization_Configure_bounds_Scopes_and_declares_explicit_ApplicationId()
	{
		ModelBuilder builder = new();
		builder.Entity<NorseOpenIddictAuthorization>(eb => NorseOpenIddictAuthorization.Configure(eb));
		builder.Entity<NorseOpenIddictApplication>();

		var entityType = builder.Model.FindEntityType(typeof(NorseOpenIddictAuthorization))!;
		entityType.FindProperty(nameof(NorseOpenIddictAuthorization.Scopes))!.GetMaxLength().ShouldBe(-1);

		var fk = entityType.GetForeignKeys().Single();
		fk.Properties.Single().Name.ShouldBe(nameof(NorseOpenIddictAuthorization.ApplicationId));
		fk.DependentToPrincipal!.Name.ShouldBe(nameof(NorseOpenIddictAuthorization.Application));
	}

	[Fact]
	public void Scope_Configure_bounds_Description_and_DisplayName()
	{
		var entityType = BuildEntityType<NorseOpenIddictScope>(eb => NorseOpenIddictScope.Configure(eb));

		entityType.FindProperty(nameof(NorseOpenIddictScope.Description))!.GetMaxLength().ShouldBe(1000);
		entityType.FindProperty(nameof(NorseOpenIddictScope.DisplayName))!.GetMaxLength().ShouldBe(200);
	}

	[Fact]
	public void Token_Configure_bounds_Payload_and_declares_explicit_FKs()
	{
		ModelBuilder builder = new();
		builder.Entity<NorseOpenIddictToken>(eb => NorseOpenIddictToken.Configure(eb));
		builder.Entity<NorseOpenIddictApplication>();
		builder.Entity<NorseOpenIddictAuthorization>(eb => NorseOpenIddictAuthorization.Configure(eb));

		var entityType = builder.Model.FindEntityType(typeof(NorseOpenIddictToken))!;
		entityType.FindProperty(nameof(NorseOpenIddictToken.Payload))!.GetMaxLength().ShouldBe(-1);

		var foreignKeys = entityType.GetForeignKeys().ToList();
		foreignKeys.ShouldContain(fk => fk.Properties.Single().Name == nameof(NorseOpenIddictToken.ApplicationId));
		foreignKeys.ShouldContain(fk => fk.Properties.Single().Name == nameof(NorseOpenIddictToken.AuthorizationId));
	}

	static Microsoft.EntityFrameworkCore.Metadata.IEntityType BuildEntityType<T>(
		Action<Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T>> configure) where T : class
	{
		ModelBuilder builder = new();
		builder.Entity(configure);
		return builder.Model.FindEntityType(typeof(T))!;
	}
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj --filter NorseOpenIddictEntitiesConfigureTests
```

Expected: compile error — none of the four types exist yet.

- [ ] **Step 3: Implement the four wrapper entities**

`Himinbjorg/src/Identity/NorseOpenIddictApplication.cs`:
```csharp
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.EntityFramework;
using OpenIddict.EntityFrameworkCore.Models;

namespace Norse.Identity;

/// <summary>
/// Norse wrapper over OpenIddict's EF Core application entity, keyed by <see cref="Guid"/>. Closes
/// two non-JSON columns OpenIddict leaves unbounded by omission (verified against
/// <c>openiddict-core</c> tag <c>7.5.0</c>).
/// </summary>
public sealed class NorseOpenIddictApplication
	: OpenIddictEntityFrameworkCoreApplication<Guid, NorseOpenIddictAuthorization, NorseOpenIddictToken>,
	  INorseEntity<NorseOpenIddictApplication>
{
	/// <inheritdoc />
	public static void Configure(EntityTypeBuilder<NorseOpenIddictApplication> builder)
	{
		builder.Property(a => a.ClientSecret).HasMaxLength(-1);
		builder.Property(a => a.DisplayName).HasMaxLength(200);
	}
}
```

`Himinbjorg/src/Identity/NorseOpenIddictAuthorization.cs`:
```csharp
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.EntityFramework;
using OpenIddict.EntityFrameworkCore.Models;

namespace Norse.Identity;

/// <summary>
/// Norse wrapper over OpenIddict's EF Core authorization entity, keyed by <see cref="Guid"/>. Adds an
/// explicit <see cref="ApplicationId"/> FK scalar — OpenIddict's own base class declares only the
/// <see cref="OpenIddictEntityFrameworkCoreAuthorization{TKey,TApplication,TToken}.Application"/>
/// navigation, leaving EF to fall back to a shadow FK, which the platform's navigation/FK law forbids
/// outside audit columns.
/// </summary>
public sealed class NorseOpenIddictAuthorization
	: OpenIddictEntityFrameworkCoreAuthorization<Guid, NorseOpenIddictApplication, NorseOpenIddictToken>,
	  INorseEntity<NorseOpenIddictAuthorization>
{
	public Guid? ApplicationId { get; init; }

	/// <inheritdoc />
	public static void Configure(EntityTypeBuilder<NorseOpenIddictAuthorization> builder)
	{
		builder.Property(a => a.Scopes).HasMaxLength(-1);
		builder.HasOne(a => a.Application).WithMany(app => app.Authorizations).HasForeignKey(a => a.ApplicationId);
	}
}
```

`Himinbjorg/src/Identity/NorseOpenIddictScope.cs`:
```csharp
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.EntityFramework;
using OpenIddict.EntityFrameworkCore.Models;

namespace Norse.Identity;

/// <summary>
/// Norse wrapper over OpenIddict's EF Core scope entity, keyed by <see cref="Guid"/>.
/// </summary>
public sealed class NorseOpenIddictScope
	: OpenIddictEntityFrameworkCoreScope<Guid>, INorseEntity<NorseOpenIddictScope>
{
	/// <inheritdoc />
	public static void Configure(EntityTypeBuilder<NorseOpenIddictScope> builder)
	{
		builder.Property(s => s.Description).HasMaxLength(1000);
		builder.Property(s => s.DisplayName).HasMaxLength(200);
	}
}
```

`Himinbjorg/src/Identity/NorseOpenIddictToken.cs`:
```csharp
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.EntityFramework;
using OpenIddict.EntityFrameworkCore.Models;

namespace Norse.Identity;

/// <summary>
/// Norse wrapper over OpenIddict's EF Core token entity, keyed by <see cref="Guid"/>. Adds explicit
/// <see cref="ApplicationId"/>/<see cref="AuthorizationId"/> FK scalars for the same reason
/// <see cref="NorseOpenIddictAuthorization.ApplicationId"/> exists — OpenIddict declares navigation
/// only, no FK scalar, on both relationships.
/// </summary>
public sealed class NorseOpenIddictToken
	: OpenIddictEntityFrameworkCoreToken<Guid, NorseOpenIddictApplication, NorseOpenIddictAuthorization>,
	  INorseEntity<NorseOpenIddictToken>
{
	public Guid? ApplicationId { get; init; }
	public Guid? AuthorizationId { get; init; }

	/// <inheritdoc />
	public static void Configure(EntityTypeBuilder<NorseOpenIddictToken> builder)
	{
		builder.Property(t => t.Payload).HasMaxLength(-1);
		builder.HasOne(t => t.Application).WithMany(a => a.Tokens).HasForeignKey(t => t.ApplicationId);
		builder.HasOne(t => t.Authorization).WithMany(a => a.Tokens).HasForeignKey(t => t.AuthorizationId);
	}
}
```

- [ ] **Step 4: Run the tests to confirm they pass**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj --filter NorseOpenIddictEntitiesConfigureTests
```

Expected: 4/4 PASS.

- [ ] **Step 5: Stage**

```bash
git -C Himinbjorg add \
  src/Identity/NorseOpenIddictApplication.cs \
  src/Identity/NorseOpenIddictAuthorization.cs \
  src/Identity/NorseOpenIddictScope.cs \
  src/Identity/NorseOpenIddictToken.cs \
  tests/Identity.Tests/NorseOpenIddictEntitiesConfigureTests.cs
```

---

## Task 14: Himinbjörg — wire `NorseIdentityDbContext` + `AddNorseIdentity`, integration test

**Files:**
- Modify: `Himinbjorg/src/Identity/NorseIdentityDbContext.cs`
- Modify: `Himinbjorg/src/Identity/IdentityBuilderExtensions.cs`
- Modify: `Himinbjorg/src/Identity/Identity.csproj`
- Test: `Himinbjorg/tests/Identity.Tests/NorseIdentityDbContextModelTests.cs`

**Interfaces:**
- Consumes: `Norse.EntityFramework.Configuration` (Urdarbrunnr, Task 6, published) — new `NorseRef Generator="true"` entry
- Produces: `NorseIdentityDbContext.OnModelCreating` calling the newly-generated `builder.ApplyNorseConfigurations()` (same-compilation, Tier 2 explicit-call path — this is the call site that already works correctly per the plan's "Design amendments," point 3); `NorseIdentityDbContext` gets `HasDefaultSchema("identity")` and the fully-specified `UseOpenIddict<NorseOpenIddictApplication, NorseOpenIddictAuthorization, NorseOpenIddictScope, NorseOpenIddictToken, Guid>()`; `AddNorseIdentity` replaces `ReplaceDefaultEntities<Guid>()` with the fully-specified overload naming all four wrapper types.

This is the task where every prior task's work either proves out together or throws — the integration test is the real arbiter for the "already bounded by base config" assumption in Task 9.

- [ ] **Step 1: Add the generator reference**

Modify `Himinbjorg/src/Identity/Identity.csproj`, adding to the existing `<NorseRef>` item group (alongside `EntityFramework`):
```xml
<NorseRef Include="EntityFramework.Configuration">
	<Repo>Urdarbrunnr</Repo>
	<Generator>true</Generator>
</NorseRef>
```

- [ ] **Step 2: Write the failing integration test**

`Himinbjorg/tests/Identity.Tests/NorseIdentityDbContextModelTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.Identity.Tests;

public sealed class NorseIdentityDbContextModelTests
{
	[Fact]
	public void Model_builds_without_throwing()
	{
		var options = new DbContextOptionsBuilder<NorseIdentityDbContext>()
			.UseNpgsql("Host=localhost;Database=norse_identity_model_test")
			.Options;
		using NorseIdentityDbContext ctx = new(options);

		Should.NotThrow(() => ctx.Model);
	}

	[Fact]
	public void Every_entity_in_the_model_implements_INorseEntity_including_OpenIddict_wrappers()
	{
		var options = new DbContextOptionsBuilder<NorseIdentityDbContext>()
			.UseNpgsql("Host=localhost;Database=norse_identity_model_test")
			.Options;
		using NorseIdentityDbContext ctx = new(options);

		var openIddictTypes = new[]
		{
			typeof(NorseOpenIddictApplication), typeof(NorseOpenIddictAuthorization),
			typeof(NorseOpenIddictScope), typeof(NorseOpenIddictToken),
		};

		foreach (var type in openIddictTypes)
			ctx.Model.FindEntityType(type).ShouldNotBeNull();
	}
}
```

Note: `UseNpgsql` doesn't open a real connection just to build/finalize the model — `ctx.Model` triggers finalization without hitting the database, matching the spec's own testing note (§5): "no live database needed — `context.Model` triggers finalization."

- [ ] **Step 3: Run the test to confirm it fails**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj --filter NorseIdentityDbContextModelTests
```

Expected: throws — `NorseIdentityDbContext` still calls the old `builder.UseOpenIddict<Guid>()` (raw default entities, not the wrapper types), so `RequireEntityConfigurationConvention` fails on the stock OpenIddict entities, which don't implement `INorseEntity<TSelf>`.

- [ ] **Step 4: Wire `NorseIdentityDbContext`**

Modify `Himinbjorg/src/Identity/NorseIdentityDbContext.cs`, replacing the `OnModelCreating` override:
```csharp
/// <inheritdoc />
protected override void OnModelCreating(ModelBuilder builder)
{
	base.OnModelCreating(builder);
	builder.HasDefaultSchema("identity");
	builder.UseOpenIddict<
		NorseOpenIddictApplication, NorseOpenIddictAuthorization,
		NorseOpenIddictScope, NorseOpenIddictToken, Guid>();
	builder.ApplyNorseConfigurations();
}
```

- [ ] **Step 5: Wire `IdentityBuilderExtensions.AddNorseIdentity`**

Modify `Himinbjorg/src/Identity/IdentityBuilderExtensions.cs`, replacing the `.AddCore(...)` call:
```csharp
services
	.AddOpenIddict()
	.AddCore(o => o
		.UseEntityFrameworkCore()
		.UseDbContext<NorseIdentityDbContext>()
		.ReplaceDefaultEntities<
			NorseOpenIddictApplication, NorseOpenIddictAuthorization,
			NorseOpenIddictScope, NorseOpenIddictToken, Guid>());
```

- [ ] **Step 6: Run the test to confirm it passes**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj --filter NorseIdentityDbContextModelTests
```

Expected: 2/2 PASS. If it still throws, read the exception message closely — it names every violating property/entity by full type name (both conventions are collect-all-then-throw, never whack-a-mole). Common cause at this step: Task 9's "already bounded" assumption for `ClaimType`/`ClaimValue` was wrong for one of the two claim entities — add the missing `HasMaxLength` call in that entity's `Configure` (Task 9's file) rather than weakening this test.

- [ ] **Step 7: Run the full Identity.Tests suite**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj
```

Expected: every test from Tasks 7–14 PASS, including the pre-existing `NorseIdentityDbContextTests`, `NorseUserStoreTests`, `IdentityBuilderExtensionsTests`.

- [ ] **Step 8: Stage**

```bash
git -C Himinbjorg add \
  src/Identity/Identity.csproj \
  src/Identity/NorseIdentityDbContext.cs \
  src/Identity/IdentityBuilderExtensions.cs \
  tests/Identity.Tests/NorseIdentityDbContextModelTests.cs
```

---

## Task 15: Himinbjörg — regenerate the `InitialCreate` migration

**Files:**
- Delete: `Himinbjorg/src/Identity.Migrations/Migrations/20260701171417_InitialCreate.cs`
- Delete: `Himinbjorg/src/Identity.Migrations/Migrations/20260701171417_InitialCreate.Designer.cs`
- Modify (regenerated): `Himinbjorg/src/Identity.Migrations/Migrations/NorseIdentityDbContextModelSnapshot.cs`
- Create (regenerated): `Himinbjorg/src/Identity.Migrations/Migrations/{new-timestamp}_InitialCreate.cs` + `.Designer.cs`

The existing migration predates this work and is already known-ephemeral per the identity-schema-provider-defaults decision (2026-07-01) — it gets regenerated once configuration lands, per spec §5's own testing note. No hand-editing: `dotnet ef` produces these files; do not touch them directly.

- [ ] **Step 1: Remove the old migration**

```bash
rm Himinbjorg/src/Identity.Migrations/Migrations/20260701171417_InitialCreate.cs
rm Himinbjorg/src/Identity.Migrations/Migrations/20260701171417_InitialCreate.Designer.cs
```

- [ ] **Step 2: Regenerate**

```bash
dotnet ef migrations add InitialCreate \
  --project Himinbjorg/src/Identity.Migrations/Identity.Migrations.csproj \
  --startup-project Himinbjorg/src/Identity.Migrations/Identity.Migrations.csproj
```

Expected: succeeds, producing a new `{timestamp}_InitialCreate.cs`/`.Designer.cs` pair and an updated `NorseIdentityDbContextModelSnapshot.cs`. If this throws instead, the failure message names every entity/property still in violation — fix them in the owning task's file (Tasks 7–13) before re-running; do not proceed with a broken migration.

- [ ] **Step 3: Verify the migration reflects the new schema**

```bash
grep -c "identity\." Himinbjorg/src/Identity.Migrations/Migrations/*_InitialCreate.cs
```

Expected: non-zero — table names now carry the `identity` schema prefix (`HasDefaultSchema("identity")`, Task 14), confirming this is genuinely the regenerated migration and not a stale leftover.

- [ ] **Step 4: Stage**

```bash
git -C Himinbjorg add \
  src/Identity.Migrations/Migrations/
```

---

## SHIP GATE — Himinbjörg

**STOP. This is the last task in this plan.**

1. Commit and push Tasks 7–15.
2. Open a PR against `master`; confirm GitHub CI (build + test) is green.
3. Merge the PR.
4. Push a version tag to trigger the release pipeline.
5. Confirm `Norse.Identity` and `Norse.Identity.Migrations` are published to the NuGet feed.
6. Run the Aspire AppHost end to end (`dotnet run --project src/Orchestration.AppHost`) and confirm the migrations service exits 0 against `norse_identity`, same verification gate the migrations-framework plan used — this is the real proof the whole chain (length enforcement, colocated configuration, the regenerated migration) survives contact with a live Postgres instance, not just in-process model finalization.
