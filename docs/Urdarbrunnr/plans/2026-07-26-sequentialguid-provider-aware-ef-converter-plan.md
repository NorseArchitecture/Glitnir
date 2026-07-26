# SequentialGuid Provider-Aware EF Converter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `Norse.Persistence.EntityFramework` a `SequentialGuid` ↔ `Guid` EF value converter that is registered automatically for every `SequentialGuid`-typed property, selects the right byte order per provider, and throws loudly instead of silently reshuffling when a value arrives in the wrong order for its destination.

**Architecture:** An abstract `SequentialGuidValueConverter` holds one shared guard (`Order != expected → throw`); two sealed leaf converters (`Rfc9562SequentialGuidValueConverter`, `SqlServerSequentialGuidValueConverter`) supply the expected order via their type, each with a genuine zero-parameter constructor, since EF's `Properties<T>().HaveConversion(Type)` constructs the converter via `Activator.CreateInstance(Type)`, which requires true zero arity. `NorseModelConventions.Apply` — already given an `applyFixedLength: bool` signal by its one caller, `NorseDbContext.ConfigureConventions` — gains a second, independent, required parameter (`GuidByteOrder sequentialGuidOrder`) that picks the leaf converter type; the two parameters are computed from the same provider check today but are kept separate because they are independent facts that only coincidentally agree for the two providers this platform has. `DeterministicGuid` is untouched (already works via EF's built-in implicit-operator conversion inference).

**Tech Stack:** .NET 11 preview / C# preview, EF Core 11 preview (`Microsoft.EntityFrameworkCore.Relational`), xUnit v3 on Microsoft.Testing.Platform, Shouldly. Repo: Urðarbrunnr (`Norse.Persistence.EntityFramework`), with one companion edit in Himinbjörg (`Norse.Identity.Web.Server`).

**Amendments (2026-07-26, found during Task 2 implementation — see the design doc's Amendments 2-3 for full reasoning):**
1. Task 2 no longer renames `applyFixedLength` to `isSqlServer`. It stays as-is, unchanged. `NorseModelConventions.Apply` instead gains a second, independent, required parameter: `GuidByteOrder sequentialGuidOrder`. Reusing one bool for two unrelated provider facts (fixed-length benefit vs. SQL-Server-specific GUID byte shuffling) was wrong even though both facts currently agree for the two providers this platform has.
2. Task 1's `Rfc9562SequentialGuidValueConverter`/`SqlServerSequentialGuidValueConverter` need a real fix: their `(ConverterMappingHints? mappingHints = null)` constructors have arity 1, not 0, so EF's `Properties<T>().HaveConversion(Type)` — which constructs via `Activator.CreateInstance(Type)`, requiring true zero-arity — throws `MissingMethodException` at model-build time. Drop the unused `mappingHints` parameter and use a literal empty-parameter-list primary constructor on each leaf instead. This is a small follow-up fix to Task 1's already-committed, already-reviewed code, not a reopening of Task 1's review cycle.
Both amendments are folded into the task text below — Task 1's section now includes the constructor fix as a required follow-up step, and Task 2's section reflects the two-parameter signature.

## Global Constraints

- Tabs for indentation; `var` for return assignments, explicit type + `new()` for construction.
- Accessibility modifiers omitted when default (`omit_if_default`); new converter types are `internal` (least-accessible until a concrete caller needs more — none exists).
- `sealed` by default for every new type; the abstract base is the only non-sealed new type, and only because it has two real subclasses.
- No silent fallbacks — the converter throws `InvalidOperationException` on a byte-order mismatch, never reshuffles.
- `NorseModelConventions.Apply`'s provider-signal parameter stays **required, no default** (decided during design review — matches the pre-existing "no silent guess" doc comment on this exact parameter).
- US English spelling everywhere.
- Test classes `public sealed`; test methods omit access modifiers (bare `void`). `Shouldly`/`Xunit` usings are global via `tests/Directory.Build.props` — never add them per file.
- Warnings are errors (`TreatWarningsAsErrors`, `WarningLevel 9999`) in both repos — a build with any warning fails.
- Design doc: `../Glitnir/docs/Urdarbrunnr/specs/2026-07-26-sequentialguid-provider-aware-ef-converter-design.md` (including its 2026-07-26 amendment on the Himinbjörg companion edit).

---

### Task 1: `SequentialGuid` value converters

**Files:**
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework/Persistence.EntityFramework.csproj`
- Create: `Urdarbrunnr/src/Persistence.EntityFramework/SequentialGuidValueConverter.cs`
- Create: `Urdarbrunnr/src/Persistence.EntityFramework/Rfc9562SequentialGuidValueConverter.cs`
- Create: `Urdarbrunnr/src/Persistence.EntityFramework/SqlServerSequentialGuidValueConverter.cs`
- Test: `Urdarbrunnr/tests/Persistence.EntityFramework.Tests/SequentialGuidValueConverterTests.cs`

**Interfaces:**
- Produces (as originally shipped by this task — **superseded by Task 2 Step 0**, which drops `mappingHints` entirely and gives each leaf a literal zero-arg constructor; that is the shape every later task actually depends on): `internal abstract class SequentialGuidValueConverter : ValueConverter<SequentialGuid, Guid>` with `protected SequentialGuidValueConverter(GuidByteOrder expectedOrder, ConverterMappingHints? mappingHints = null)`. `internal sealed class Rfc9562SequentialGuidValueConverter(ConverterMappingHints? mappingHints = null) : SequentialGuidValueConverter`. `internal sealed class SqlServerSequentialGuidValueConverter(ConverterMappingHints? mappingHints = null) : SequentialGuidValueConverter`.
- Consumes: `Norse.Primitives.Identifiers.SequentialGuid`, `.GuidByteOrder` (Svartálfheim, via the new `NorseRef`). `Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<TModel, TProvider>` / `ConverterMappingHints` (EF Core, already referenced transitively via `Microsoft.EntityFrameworkCore.Relational`).

- [ ] **Step 1: Add the `Norse.Primitives` dependency**

Edit `Urdarbrunnr/src/Persistence.EntityFramework/Persistence.EntityFramework.csproj` — add a `NorseRef` item to the existing `<ItemGroup>` that already holds the `PackageReference`/`ProjectReference`:

```xml
<NorseRef Include="Primitives">
	<Repo>Svartalfheim</Repo>
</NorseRef>
```

- [ ] **Step 2: Write the failing tests**

Create `Urdarbrunnr/tests/Persistence.EntityFramework.Tests/SequentialGuidValueConverterTests.cs`:

```csharp
using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework.Tests;

public sealed class SequentialGuidValueConverterTests
{
	[Fact]
	void Rfc9562_converter_passes_through_an_Rfc9562_ordered_value()
	{
		SequentialGuid guid = new();
		Rfc9562SequentialGuidValueConverter converter = new();

		var result = converter.ConvertToProvider(guid);

		result.ShouldBe(guid.Value);
	}

	[Fact]
	void Rfc9562_converter_throws_on_a_SqlServer_ordered_value()
	{
		var guid = new SequentialGuid().ToSqlOrder();
		Rfc9562SequentialGuidValueConverter converter = new();

		var act = () => converter.ConvertToProvider(guid);

		var ex = act.ShouldThrow<InvalidOperationException>();
		ex.Message.ShouldContain("SqlServer");
		ex.Message.ShouldContain("Rfc9562");
	}

	[Fact]
	void Rfc9562_converter_tags_a_value_read_from_the_provider_as_Rfc9562()
	{
		SequentialGuid source = new();
		Rfc9562SequentialGuidValueConverter converter = new();

		var result = (SequentialGuid)converter.ConvertFromProvider(source.Value)!;

		result.Order.ShouldBe(GuidByteOrder.Rfc9562);
		result.Value.ShouldBe(source.Value);
	}

	[Fact]
	void SqlServer_converter_passes_through_a_SqlServer_ordered_value()
	{
		var guid = new SequentialGuid().ToSqlOrder();
		SqlServerSequentialGuidValueConverter converter = new();

		var result = converter.ConvertToProvider(guid);

		result.ShouldBe(guid.Value);
	}

	[Fact]
	void SqlServer_converter_throws_on_an_Rfc9562_ordered_value()
	{
		SequentialGuid guid = new();
		SqlServerSequentialGuidValueConverter converter = new();

		var act = () => converter.ConvertToProvider(guid);

		var ex = act.ShouldThrow<InvalidOperationException>();
		ex.Message.ShouldContain("Rfc9562");
		ex.Message.ShouldContain("SqlServer");
	}

	[Fact]
	void SqlServer_converter_tags_a_value_read_from_the_provider_as_SqlServer()
	{
		var source = new SequentialGuid().ToSqlOrder();
		SqlServerSequentialGuidValueConverter converter = new();

		var result = (SequentialGuid)converter.ConvertFromProvider(source.Value)!;

		result.Order.ShouldBe(GuidByteOrder.SqlServer);
		result.Value.ShouldBe(source.Value);
	}
}
```

- [ ] **Step 3: Run the tests to verify they fail to compile**

Run: `dotnet test tests/Persistence.EntityFramework.Tests -- --filter-class "*.SequentialGuidValueConverterTests"` (from the `Urdarbrunnr` directory).
Expected: build error — `Rfc9562SequentialGuidValueConverter`/`SqlServerSequentialGuidValueConverter` do not exist yet.

- [ ] **Step 4: Implement the abstract base**

Create `Urdarbrunnr/src/Persistence.EntityFramework/SequentialGuidValueConverter.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Converts <see cref="SequentialGuid"/> to and from a stored <see cref="Guid"/>, refusing to
/// convert a value whose <see cref="SequentialGuid.Order"/> doesn't match the destination
/// provider's expected byte order. Never reshuffles: SQL Server's <c>uniqueidentifier</c> sort
/// order disagrees with RFC 9562's own byte order, and silently "fixing" a mismatched value would
/// make debugging which byte order a stored GUID is actually in a nightmare. Callers must call
/// <see cref="SequentialGuid.ToSqlOrder"/>/<see cref="SequentialGuid.ToRfcOrder"/> explicitly before
/// assigning a value bound for the other provider.
/// </summary>
abstract class SequentialGuidValueConverter(GuidByteOrder expectedOrder, ConverterMappingHints? mappingHints = null) :
	ValueConverter<SequentialGuid, Guid>(
		guid => Guard(guid, expectedOrder),
		value => new SequentialGuid(value, expectedOrder),
		mappingHints)
{
	static Guid Guard(SequentialGuid guid, GuidByteOrder expectedOrder) =>
		guid.Order == expectedOrder
			? guid.Value
			: throw new InvalidOperationException(
				$"SequentialGuid is in {guid.Order} byte order but this provider requires {expectedOrder}. " +
				$"Call {(expectedOrder == GuidByteOrder.SqlServer ? "ToSqlOrder()" : "ToRfcOrder()")} explicitly " +
				"before assigning -- this converter never silently reshuffles.");
}
```

- [ ] **Step 5: Implement the two leaf converters**

Create `Urdarbrunnr/src/Persistence.EntityFramework/Rfc9562SequentialGuidValueConverter.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework;

/// <summary>Expects and produces <see cref="GuidByteOrder.Rfc9562"/> — every provider except SQL Server.</summary>
sealed class Rfc9562SequentialGuidValueConverter(ConverterMappingHints? mappingHints = null) :
	SequentialGuidValueConverter(GuidByteOrder.Rfc9562, mappingHints);
```

Create `Urdarbrunnr/src/Persistence.EntityFramework/SqlServerSequentialGuidValueConverter.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework;

/// <summary>Expects and produces <see cref="GuidByteOrder.SqlServer"/> — SQL Server's own <c>uniqueidentifier</c> sort order.</summary>
sealed class SqlServerSequentialGuidValueConverter(ConverterMappingHints? mappingHints = null) :
	SequentialGuidValueConverter(GuidByteOrder.SqlServer, mappingHints);
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Persistence.EntityFramework.Tests -- --filter-class "*.SequentialGuidValueConverterTests"` (from the `Urdarbrunnr` directory).
Expected: all 6 tests pass.

- [ ] **Step 7: Build the whole solution to confirm no warnings**

Run: `dotnet build Urdarbrunnr.slnx` (from the `Urdarbrunnr` directory).
Expected: succeeds with zero warnings (warnings are errors).

- [ ] **Step 8: Commit**

```bash
git add src/Persistence.EntityFramework/Persistence.EntityFramework.csproj \
	src/Persistence.EntityFramework/SequentialGuidValueConverter.cs \
	src/Persistence.EntityFramework/Rfc9562SequentialGuidValueConverter.cs \
	src/Persistence.EntityFramework/SqlServerSequentialGuidValueConverter.cs \
	tests/Persistence.EntityFramework.Tests/SequentialGuidValueConverterTests.cs
git commit -m "feat: add provider-aware SequentialGuid value converters"
```

---

### Task 2: Wire the converter into the model-wide convention

**Files:**
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework/SequentialGuidValueConverter.cs` (Task 1 fix — see Step 0)
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework/Rfc9562SequentialGuidValueConverter.cs` (Task 1 fix)
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework/SqlServerSequentialGuidValueConverter.cs` (Task 1 fix)
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework/NorseModelConventions.cs`
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework/NorseDbContext.cs`
- Test: `Urdarbrunnr/tests/Persistence.EntityFramework.Tests/SequentialGuidConversionWiringTests.cs`

**Interfaces:**
- Consumes: `Rfc9562SequentialGuidValueConverter`, `SqlServerSequentialGuidValueConverter` (Task 1, fixed in Step 0 below). `NorseEntityBase<TSelf>`, `INorseEntity<TSelf>`, `NorseDbContext`, `NorseDbContextOptionsExtensions.SqlServerProviderName` (all pre-existing in this project).
- Produces: `NorseModelConventions.Apply(ModelConfigurationBuilder configurationBuilder, bool applyFixedLength, GuidByteOrder sequentialGuidOrder)` — `applyFixedLength` is unchanged from before this feature; `sequentialGuidOrder` is new.

- [ ] **Step 0: Fix Task 1's converter constructors (real defect, found while implementing this task)**

`Properties<T>().HaveConversion(Type)` constructs the registered type via `Activator.CreateInstance(Type)`, which requires a true zero-parameter constructor. Task 1's leaf converters only have `(ConverterMappingHints? mappingHints = null)` — arity 1, not 0 — so this throws `MissingMethodException` at model-build time. Drop the unused `mappingHints` parameter (nothing in this design ever passes custom hints) and give each leaf a literal empty-parameter-list primary constructor instead.

Edit `Urdarbrunnr/src/Persistence.EntityFramework/SequentialGuidValueConverter.cs` — change the class declaration line only (the `Guard` method and its body are unchanged):

```csharp
abstract class SequentialGuidValueConverter(GuidByteOrder expectedOrder) :
	ValueConverter<SequentialGuid, Guid>(
		guid => Guard(guid, expectedOrder),
		value => new SequentialGuid(value, expectedOrder))
```

Edit `Urdarbrunnr/src/Persistence.EntityFramework/Rfc9562SequentialGuidValueConverter.cs` — replace the class declaration line:

```csharp
sealed class Rfc9562SequentialGuidValueConverter() : SequentialGuidValueConverter(GuidByteOrder.Rfc9562);
```

Edit `Urdarbrunnr/src/Persistence.EntityFramework/SqlServerSequentialGuidValueConverter.cs` — replace the class declaration line:

```csharp
sealed class SqlServerSequentialGuidValueConverter() : SequentialGuidValueConverter(GuidByteOrder.SqlServer);
```

Remove the now-unused `using Microsoft.EntityFrameworkCore.Storage.ValueConversion;` from the two leaf files only if `ConverterMappingHints` was the only reason it was imported there (check — the base file still needs it for `ValueConverter<,>`).

Run Task 1's existing tests to confirm they still pass unchanged: `dotnet test tests/Persistence.EntityFramework.Tests -- --filter-class "*.SequentialGuidValueConverterTests"` (from the `Urdarbrunnr` directory). Expected: all 6 still pass — `new Rfc9562SequentialGuidValueConverter()` / `new SqlServerSequentialGuidValueConverter()` call sites in that test file are unaffected by dropping a parameter that was always called with its default.

Commit this fix on its own before proceeding:

```bash
git add src/Persistence.EntityFramework/SequentialGuidValueConverter.cs \
	src/Persistence.EntityFramework/Rfc9562SequentialGuidValueConverter.cs \
	src/Persistence.EntityFramework/SqlServerSequentialGuidValueConverter.cs
git commit -m "fix: give SequentialGuid converters real zero-arity constructors"
```

- [ ] **Step 1: Write the failing wiring tests**

Create `Urdarbrunnr/tests/Persistence.EntityFramework.Tests/SequentialGuidConversionWiringTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework.Tests;

public sealed class SequentialGuidConversionWiringTests
{
	[Fact]
	void Non_SqlServer_providers_get_the_Rfc9562_converter()
	{
		using var ctx = CreateContext<SequentialGuidContext>();

		var property = ctx.Model.FindEntityType(typeof(SequentialGuidEntity))!
			.FindProperty(nameof(SequentialGuidEntity.Id))!;

		property.GetValueConverter().ShouldBeOfType<Rfc9562SequentialGuidValueConverter>();
	}

	[Fact]
	void SqlServer_gets_the_SqlServer_converter()
	{
		using var ctx = CreateSqlServerContext<SequentialGuidContext>();

		var property = ctx.Model.FindEntityType(typeof(SequentialGuidEntity))!
			.FindProperty(nameof(SequentialGuidEntity.Id))!;

		property.GetValueConverter().ShouldBeOfType<SqlServerSequentialGuidValueConverter>();
	}

	static TContext CreateContext<TContext>() where TContext : DbContext =>
		(TContext)Activator.CreateInstance(typeof(TContext),
			new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:").Options)!;

	static TContext CreateSqlServerContext<TContext>() where TContext : DbContext =>
		(TContext)Activator.CreateInstance(typeof(TContext),
			new DbContextOptionsBuilder<TContext>()
				.UseSqlServer("Server=localhost;Database=test;Trusted_Connection=True;TrustServerCertificate=True;")
				.Options)!;

	sealed record SequentialGuidEntity(SequentialGuid Id) : NorseEntityBase<SequentialGuidEntity>, INorseEntity<SequentialGuidEntity>
	{
		public static void Configure(EntityTypeBuilder<SequentialGuidEntity> builder) =>
			builder.HasKey(e => e.Id);
	}

	sealed class SequentialGuidContext(DbContextOptions<SequentialGuidContext> options) : NorseDbContext(options)
	{
		public DbSet<SequentialGuidEntity> Entities => Set<SequentialGuidEntity>();
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Persistence.EntityFramework.Tests -- --filter-class "*.SequentialGuidConversionWiringTests"` (from the `Urdarbrunnr` directory).
Expected: both tests fail — `GetValueConverter()` returns `null` (no converter registered yet for `SequentialGuid`).

- [ ] **Step 3: Add the second parameter and register the converter**

Edit `Urdarbrunnr/src/Persistence.EntityFramework/NorseModelConventions.cs` — replace the whole `Apply` method body and its doc comment:

```csharp
using Microsoft.EntityFrameworkCore;
using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Registers the model-finalizing conventions and provider-aware value conversions every Norse EF
/// context is guaranteed to enforce: explicit string/byte[] length
/// (<see cref="RequireExplicitLengthConvention"/>), mandatory entity self-configuration
/// (<see cref="RequireEntityConfigurationConvention"/>), and the correct <see cref="SequentialGuid"/>
/// byte-order converter for the destination provider.
/// </summary>
public static class NorseModelConventions
{
	/// <summary>
	/// Adds both Norse model-finalizing conventions, and the provider-correct
	/// <see cref="SequentialGuid"/> converter, to <paramref name="configurationBuilder"/>.
	/// </summary>
	/// <param name="configurationBuilder">The configuration builder to register conventions on.</param>
	/// <param name="applyFixedLength">
	/// Whether <see cref="FixedLengthAttribute"/> should translate to <c>.IsFixedLength()</c>. Pass
	/// <see langword="true"/> only for providers where fixed-length storage has a real benefit (SQL
	/// Server); Postgres and everything else should pass <see langword="false"/> — see
	/// <see cref="FixedLengthAttribute"/>'s remarks for why. No default: every caller states its
	/// provider explicitly rather than silently inheriting a guess.
	/// </param>
	/// <param name="sequentialGuidOrder">
	/// Which <see cref="GuidByteOrder"/> the model-wide <see cref="SequentialGuid"/> converter expects
	/// for this provider — <see cref="GuidByteOrder.SqlServer"/> selects
	/// <see cref="SqlServerSequentialGuidValueConverter"/>, anything else selects
	/// <see cref="Rfc9562SequentialGuidValueConverter"/>. Deliberately independent of
	/// <paramref name="applyFixedLength"/>: both happen to be driven by the same provider check today,
	/// but they are unrelated facts (a general storage-engine question vs. a SQL-Server-specific
	/// comparison quirk) — a future provider could decouple them, and folding both into one flag would
	/// silently break whichever one didn't win. No default, for the same reason as
	/// <paramref name="applyFixedLength"/>.
	/// </param>
	/// <returns>The same <paramref name="configurationBuilder"/>, for chaining.</returns>
	public static ModelConfigurationBuilder Apply(ModelConfigurationBuilder configurationBuilder,
		bool applyFixedLength, GuidByteOrder sequentialGuidOrder)
	{
		configurationBuilder.Conventions.Add(_ => new RequireExplicitLengthConvention(applyFixedLength));
		configurationBuilder.Conventions.Add(static _ => new RequireEntityConfigurationConvention());
		configurationBuilder.Properties<SequentialGuid>().HaveConversion(
			sequentialGuidOrder == GuidByteOrder.SqlServer
				? typeof(SqlServerSequentialGuidValueConverter)
				: typeof(Rfc9562SequentialGuidValueConverter));
		return configurationBuilder;
	}
}
```

- [ ] **Step 4: Update the call site**

Edit `Urdarbrunnr/src/Persistence.EntityFramework/NorseDbContext.cs` — in `ConfigureConventions`, change:

```csharp
		NorseModelConventions.Apply(configurationBuilder,
			applyFixedLength: Database.ProviderName == NorseDbContextOptionsExtensions.SqlServerProviderName);
```

to:

```csharp
		var isSqlServer = Database.ProviderName == NorseDbContextOptionsExtensions.SqlServerProviderName;
		NorseModelConventions.Apply(configurationBuilder,
			applyFixedLength: isSqlServer,
			sequentialGuidOrder: isSqlServer ? GuidByteOrder.SqlServer : GuidByteOrder.Rfc9562);
```

(A local `isSqlServer` variable avoids duplicating the `Database.ProviderName` comparison at this call site — the two arguments are conceptually independent per the doc comment above, but there is no reason to compute the identical boolean expression twice in the same method body. Add `using Norse.Primitives.Identifiers;` to this file's usings if not already present, for `GuidByteOrder`.)

- [ ] **Step 5: Run the wiring tests to verify they pass**

Run: `dotnet test tests/Persistence.EntityFramework.Tests -- --filter-class "*.SequentialGuidConversionWiringTests"` (from the `Urdarbrunnr` directory).
Expected: both tests pass.

- [ ] **Step 6: Run the full Urdarbrunnr test suite**

Run: `dotnet test Urdarbrunnr.slnx` (from the `Urdarbrunnr` directory).
Expected: every test project passes, including the pre-existing `RequireExplicitLengthConventionTests` and `NorseDbContextTests` (unaffected by this task's changes — they construct `RequireExplicitLengthConvention` directly and never call `NorseModelConventions.Apply`).

- [ ] **Step 7: Build the whole solution to confirm no warnings**

Run: `dotnet build Urdarbrunnr.slnx` (from the `Urdarbrunnr` directory).
Expected: succeeds with zero warnings.

- [ ] **Step 8: Commit**

```bash
git add src/Persistence.EntityFramework/NorseModelConventions.cs \
	src/Persistence.EntityFramework/NorseDbContext.cs \
	tests/Persistence.EntityFramework.Tests/SequentialGuidConversionWiringTests.cs
git commit -m "feat: select provider-correct SequentialGuid converter in NorseModelConventions"
```

---

### Task 3: Himinbjörg companion fix

**Files:**
- Modify: `Himinbjorg/src/Identity.Web.Server/NorseIdentityDbContext.cs`

**Interfaces:**
- Consumes: `NorseModelConventions.Apply(ModelConfigurationBuilder, bool applyFixedLength, GuidByteOrder sequentialGuidOrder)` (Task 2's two-parameter signature). In Bifröst dev mode this resolves via the existing `NorseRef`/`ProjectReference` to Urðarbrunnr, so Task 2's change is visible here without any package republish.
- Produces: nothing new — this task only keeps Himinbjörg compiling against Task 2's new required parameter.

- [ ] **Step 1: Add the new argument**

Edit `Himinbjorg/src/Identity.Web.Server/NorseIdentityDbContext.cs` — in `ConfigureConventions` (around line 57-58), change:

```csharp
		NorseModelConventions.Apply(configurationBuilder,
			applyFixedLength: Database.ProviderName == NorseDbContextOptionsExtensions.SqlServerProviderName);
```

to:

```csharp
		var isSqlServer = Database.ProviderName == NorseDbContextOptionsExtensions.SqlServerProviderName;
		NorseModelConventions.Apply(configurationBuilder,
			applyFixedLength: isSqlServer,
			sequentialGuidOrder: isSqlServer ? GuidByteOrder.SqlServer : GuidByteOrder.Rfc9562);
```

Add `using Norse.Primitives.Identifiers;` to this file's usings if not already present, for `GuidByteOrder`. `applyFixedLength`'s value and meaning are unchanged — only a new required argument is added, plus the local variable that now backs both.

- [ ] **Step 2: Build Himinbjörg to confirm it compiles**

Run: `dotnet build Himinbjorg.slnx` (from the `Himinbjorg` directory).
Expected: succeeds with zero warnings. This proves Task 2's new required parameter didn't break the only other real call site on the platform.

- [ ] **Step 3: Run the Himinbjörg test suite**

Run: `dotnet test Himinbjorg.slnx` (from the `Himinbjorg` directory).
Expected: all existing tests still pass — this task only adds an argument that reproduces existing behavior for both providers; nothing changes at runtime.

- [ ] **Step 4: Commit**

```bash
git add src/Identity.Web.Server/NorseIdentityDbContext.cs
git commit -m "fix: pass sequentialGuidOrder to Urdarbrunnr's NorseModelConventions.Apply"
```

---

### Task 4 (added 2026-07-26, final whole-branch review): fix `default(SequentialGuid)` in Svartálfheim

**Why:** the final review traced a real hazard: EF derives a default `ValueComparer<SequentialGuid>` from `Equals`/`GetHashCode` when none is registered. Both call `ToRfcOrder()`, which — for `default(SequentialGuid)` (`Order == GuidByteOrder.Unspecified`) — takes its "convert" branch and calls the two-arg constructor on `Guid.Empty`, which fails the RFC 9562 version-7 check and throws a confusing `ArgumentException` ("Value must be a version 7 UUID...") instead of a clear diagnosis. The same dead-end applies to `SequentialGuidValueConverter.Guard`'s own remediation advice ("call ToSqlOrder()/ToRfcOrder()") when the mismatched value is itself `default`. Svartálfheim already has a house pattern for exactly this hazard class — `default(Result<T>)`/`default(Failure)` are "malformed by construction," documented in XML remarks and pinned by canary tests. `SequentialGuid` gets the same treatment here: not made to silently work, but made to fail with an immediate, clear exception instead of a confusing one two calls deep.

**Files:**
- Modify: `Svartalfheim/src/Primitives/Identifiers/SequentialGuid.cs`
- Test: `Svartalfheim/tests/Primitives.Tests/Identifiers/SequentialGuidTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `ToRfcOrder()`/`ToSqlOrder()` now throw `InvalidOperationException` (not a downstream `ArgumentException`) when `Order == GuidByteOrder.Unspecified`. `Equals`/`GetHashCode` inherit this automatically (both already route through `ToRfcOrder()`).

- [ ] **Step 1: Write the failing tests**

Add to `Svartalfheim/tests/Primitives.Tests/Identifiers/SequentialGuidTests.cs` (inside the existing `SequentialGuidTests` class, matching the file's own `Should_{behavior}_when_{condition}` naming):

```csharp
[Fact]
void Should_throw_a_clear_exception_when_ToRfcOrder_is_called_on_a_default_value()
{
	var act = () => default(SequentialGuid).ToRfcOrder();

	var ex = Should.Throw<InvalidOperationException>(act);
	ex.Message.ShouldContain("malformed by construction");
}

[Fact]
void Should_throw_a_clear_exception_when_ToSqlOrder_is_called_on_a_default_value()
{
	var act = () => default(SequentialGuid).ToSqlOrder();

	var ex = Should.Throw<InvalidOperationException>(act);
	ex.Message.ShouldContain("malformed by construction");
}

[Fact]
void Should_throw_a_clear_exception_when_Equals_is_called_on_a_default_value()
{
	var act = () => default(SequentialGuid).Equals(new SequentialGuid());

	Should.Throw<InvalidOperationException>(act);
}

[Fact]
void Should_throw_a_clear_exception_when_GetHashCode_is_called_on_a_default_value()
{
	var act = () => default(SequentialGuid).GetHashCode();

	Should.Throw<InvalidOperationException>(act);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.SequentialGuidTests"` (from the `Svartalfheim` directory).
Expected: the four new tests fail — today, `ToRfcOrder()`/`ToSqlOrder()` throw `ArgumentException`, not `InvalidOperationException`, on a default value (verify this is genuinely the failure, not a compile error).

- [ ] **Step 3: Add the explicit guard**

Edit `Svartalfheim/src/Primitives/Identifiers/SequentialGuid.cs` — replace the `ToSqlOrder`/`ToRfcOrder` method bodies:

```csharp
	/// <summary>Returns this value converted to <see cref="GuidByteOrder.SqlServer"/> order (a no-op if already there).</summary>
	/// <exception cref="InvalidOperationException"><see cref="Order"/> is <see cref="GuidByteOrder.Unspecified"/> -- <c>default(SequentialGuid)</c> is malformed by construction.</exception>
	public SequentialGuid ToSqlOrder() =>
		Order switch
		{
			GuidByteOrder.Unspecified => throw new InvalidOperationException(
				"default(SequentialGuid) is malformed by construction -- Order is Unspecified. Only wrap a value this platform already produced via the two-arg constructor, or generate a new one with SequentialGuid()."),
			GuidByteOrder.SqlServer => this,
			_ => new(SequentialGuidBytes.ToSqlOrder(Value), GuidByteOrder.SqlServer)
		};

	/// <summary>Returns this value converted to <see cref="GuidByteOrder.Rfc9562"/> order (a no-op if already there).</summary>
	/// <exception cref="InvalidOperationException"><see cref="Order"/> is <see cref="GuidByteOrder.Unspecified"/> -- <c>default(SequentialGuid)</c> is malformed by construction.</exception>
	public SequentialGuid ToRfcOrder() =>
		Order switch
		{
			GuidByteOrder.Unspecified => throw new InvalidOperationException(
				"default(SequentialGuid) is malformed by construction -- Order is Unspecified. Only wrap a value this platform already produced via the two-arg constructor, or generate a new one with SequentialGuid()."),
			GuidByteOrder.Rfc9562 => this,
			_ => new(SequentialGuidBytes.ToRfcOrder(Value), GuidByteOrder.Rfc9562)
		};
```

Do not touch `CompareTo` — it has a separate, narrower quirk (comparing two `default` values short-circuits to "equal" without calling either conversion method) that the final review did not flag and is out of scope here; do not fix it as a drive-by.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Primitives.Tests -- --filter-class "*.SequentialGuidTests"` (from the `Svartalfheim` directory).
Expected: all tests in the file pass, including the four new ones.

- [ ] **Step 5: Build and run the full solution**

Run: `dotnet build Svartalfheim.slnx` (zero warnings — WarningLevel 9999, EnforceCodeStyleInBuild) then `dotnet test Svartalfheim.slnx` (from the `Svartalfheim` directory).
Expected: succeeds, zero warnings, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Primitives/Identifiers/SequentialGuid.cs tests/Primitives.Tests/Identifiers/SequentialGuidTests.cs
git commit -m "fix: make default(SequentialGuid) fail loudly and clearly, not via a confusing downstream exception"
```

---

### Task 5 (added 2026-07-26, real defect found in downstream Mímisbrunnr work): `DeterministicGuid` value converter

**Why:** the design doc's original Finding section claimed `DeterministicGuid` already round-trips with zero converter code, based on reading `CountryOrArea`'s already-shipped migration and seeing a plain `uuid` column with no `HasConversion` in the repo. That was a misread: `CountryOrArea.Id` was still plain `System.Guid` when that migration was generated — the `uuid` column is `Guid`'s own native mapping, nothing to do with `DeterministicGuid` at all. Buvy's own in-progress (uncommitted) work retyping `CountryOrArea.Id`/`Region.Id`/`ParentRegionId` to `DeterministicGuid` hit the real error live: `InvalidOperationException: The 'DeterministicGuid' property 'CountryOrArea.Id' could not be mapped because the database provider does not support this type.` EF has no automatic conversion inference for this type; it needs the same explicit treatment `SequentialGuid` just got — just without any byte-order concept, since `DeterministicGuid` is a pure content hash with no ordering semantics at all (Svartálfheim `DeterministicGuid.cs` remarks: "no `Timestamp`, no byte-order concept — a content hash has no time component and no meaningful sort order").

**Files:**
- Create: `Urdarbrunnr/src/Persistence.EntityFramework/DeterministicGuidValueConverter.cs`
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework/NorseModelConventions.cs`
- Test: `Urdarbrunnr/tests/Persistence.EntityFramework.Tests/DeterministicGuidValueConverterTests.cs`

**Interfaces:**
- Consumes: `Norse.Primitives.Identifiers.DeterministicGuid` (already referenced via Task 1's `NorseRef`, no new dependency).
- Produces: `internal sealed class DeterministicGuidValueConverter() : ValueConverter<DeterministicGuid, Guid>` — unconditional, provider-agnostic, registered for every `NorseDbContext` regardless of `applyFixedLength`/`sequentialGuidOrder`.

- [ ] **Step 1: Write the failing tests**

Create `Urdarbrunnr/tests/Persistence.EntityFramework.Tests/DeterministicGuidValueConverterTests.cs`:

```csharp
using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework.Tests;

public sealed class DeterministicGuidValueConverterTests
{
	[Fact]
	void Converts_to_the_underlying_Guid_value()
	{
		DeterministicGuid id = new(DeterministicGuid.Namespaces.Dns, "example.norsearchitecture.dev");
		DeterministicGuidValueConverter converter = new();

		var result = converter.ConvertToProvider(id);

		result.ShouldBe(id.Value);
	}

	[Fact]
	void Converts_from_a_stored_Guid_back_to_the_same_DeterministicGuid()
	{
		DeterministicGuid source = new(DeterministicGuid.Namespaces.Dns, "example.norsearchitecture.dev");
		DeterministicGuidValueConverter converter = new();

		var result = (DeterministicGuid)converter.ConvertFromProvider(source.Value)!;

		result.ShouldBe(source);
	}
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/Persistence.EntityFramework.Tests -- --filter-class "*.DeterministicGuidValueConverterTests"` (from the `Urdarbrunnr` directory).
Expected: build error — `DeterministicGuidValueConverter` does not exist yet.

- [ ] **Step 3: Implement the converter**

Create `Urdarbrunnr/src/Persistence.EntityFramework/DeterministicGuidValueConverter.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Converts <see cref="DeterministicGuid"/> to and from a stored <see cref="Guid"/>. Unlike
/// <see cref="SequentialGuidValueConverter"/>, there is no provider-specific byte order to guard —
/// <see cref="DeterministicGuid"/> is a pure content hash with no time component and no meaningful
/// sort order (see its own remarks), so this converter is a plain, unconditional round trip.
/// </summary>
sealed class DeterministicGuidValueConverter() :
	ValueConverter<DeterministicGuid, Guid>(
		id => id.Value,
		value => new DeterministicGuid(value));
```

Use a literal empty-parameter-list primary constructor (learned from Task 1/2's real bug: `Properties<T>().HaveConversion(Type)` needs true zero arity, not a defaulted `ConverterMappingHints?` parameter).

- [ ] **Step 4: Register it unconditionally in `NorseModelConventions.Apply`**

Edit `Urdarbrunnr/src/Persistence.EntityFramework/NorseModelConventions.cs` — add one line inside `Apply`, alongside the existing `SequentialGuid` registration:

```csharp
configurationBuilder.Properties<DeterministicGuid>().HaveConversion<DeterministicGuidValueConverter>();
```

Unlike `SequentialGuid`'s registration, this needs no provider branching — add it unconditionally, before or after the `SequentialGuid` line (either order is fine).

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Persistence.EntityFramework.Tests -- --filter-class "*.DeterministicGuidValueConverterTests"` (from the `Urdarbrunnr` directory).
Expected: both tests pass.

- [ ] **Step 6: Add a model-building regression test proving the registration actually fixes the reported error**

Add to `Urdarbrunnr/tests/Persistence.EntityFramework.Tests/SequentialGuidConversionWiringTests.cs` (or a new file if that one doesn't fit — controller's call): a `DeterministicGuid`-typed entity property, build the model via a `NorseDbContext` subclass, assert `ctx.Model` builds without throwing and `property.GetValueConverter()` is `DeterministicGuidValueConverter`. This is the test that would have caught the real Mímisbrunnr failure before it shipped.

- [ ] **Step 7: Run the full Urdarbrunnr test suite and build**

Run: `dotnet test Urdarbrunnr.slnx` then `dotnet build Urdarbrunnr.slnx` (from the `Urdarbrunnr` directory).
Expected: all tests pass, zero warnings.

- [ ] **Step 8: Commit**

```bash
git add src/Persistence.EntityFramework/DeterministicGuidValueConverter.cs \
	src/Persistence.EntityFramework/NorseModelConventions.cs \
	tests/Persistence.EntityFramework.Tests/DeterministicGuidValueConverterTests.cs
git commit -m "feat: add DeterministicGuid value converter -- EF has no automatic inference for it"
```
