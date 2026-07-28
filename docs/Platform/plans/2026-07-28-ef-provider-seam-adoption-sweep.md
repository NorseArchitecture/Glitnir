# EF Provider-Seam Adoption Sweep Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (default per `../../../CLAUDE.md` §2.8) or superpowers:executing-plans (narrow separate-session fallback) to implement this plan task-by-task, paired with superpowers:test-driven-development. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adopt Urðarbrunnr v0.0.8's provider seam in Himinbjörg and Mímisbrunnr, move Yggdrasil's migrations generator reference, and bring the migrations service back to life against the live AppHost — plus test-parity uplift in both realms.

**Architecture:** Pure consumer diff per the ratified seam spec §11 (`../../Urdarbrunnr/specs/2026-07-27-ef-provider-seam-repackaging-design.md`): realm design-time factories rebase onto the one neutral `NorseDesignTimeDbContextFactory<TContext>` naming their binding; `NorseDesignRef Design.{Provider}` (deleted packages) becomes `NorseDesignRef .Design` + a flowing `NorseRef` to the thin binding package; contributor projects move from `.Design` (old squatter home) to `.Migrations`. No behavior changes; no new services. Parity uplift copies each realm's proven pattern into its sibling: Mímisbrunnr's Postgres testcontainer goes to Himinbjörg, Himinbjörg's 1:1 test-project layout goes to Mímisbrunnr.

**Tech Stack:** .NET 11 preview / C# 15, EF Core 11 preview, xUnit v3 on MTP v2, Shouldly, Testcontainers.PostgreSql, Aspire AppHost (Bifröst).

## Global Constraints

- Working root is **Bifröst**; all paths below are relative to it. Realms build in dev mode (`UseProjectReferences=true` from Bifröst's root `Directory.Build.props`) — `NorseRef`/`NorseDesignRef` resolve to sibling-submodule `ProjectReference`s.
- **`NorseRef` flows to consumers; `NorseDesignRef` is `PrivateAssets="all"`.** The provider binding package must be a plain `NorseRef` in each `.Migrations.{Provider}` project — the generator discovers the binding in the migration host's reference closure, and a `PrivateAssets`-blocked binding means NORSE030 (zero bindings found) in Yggdrasil. `.Design` stays `NorseDesignRef` (design-time chassis, never a runtime asset).
- **Scatter files are immutable — halt and ask, never edit:** every `src/Directory.Build.props`, `src/Directory.Build.targets`, `tests/Directory.Build.props`, `tests/Directory.Build.targets`, root `Directory.Build.props`, `.editorconfig` in any realm. Restate this in every subagent dispatch prompt.
- **Git:** each touched realm gets a local branch `feature/ef-provider-seam-adoption`; commits allowed there (local only — **never push, never touch master**). Run `git -C {Realm} branch --show-current` immediately before every commit. Urðarbrunnr and Glitnir changes are **stage-only, no commit** (they stay on master). Bifröst's own tracked files are untouched.
- Tabs for indentation; house rules (`../../house-rules.md`) govern all code: target-typed `new()`, `var` for return assignments, expression bodies with arrow-on-declaration-line, `sealed` everywhere, sentence-shaped test names, bare `void`/`async Task` test methods (no accessibility modifier), `CancellationToken` as last parameter, no `ConfigureAwait` in tests, `is null`/`is not null`, usings hoisted (no inline fully-qualified names).
- Package versions tag to the major: framework-tracking packages `Version="11.*-*"`, others `Version="*"` in realms (Yggdrasil pins exactly via CPM).
- Container tests and the live gate need Docker running (WSL2 — same daemon Aspire uses).
- Out of scope (do not touch): Mímir service work, `Identity.Web.Server`'s hand-rolled `UseNpgsql` runtime registration (behavior change — deferred), package-mode (`UseProjectReferences=false`) verification (blocked until Buvy merges realm branches and re-releases), Oracle/SQLite.

**Interfaces consumed everywhere (Urðarbrunnr v0.0.8, exact):**

```csharp
// Norse.Persistence.EntityFramework (neutral)
public interface INorseEfProvider { ... }                                  // binding contract
public DbContextOptionsBuilder ApplyNorseProviderOptions(INorseEfProvider provider,
	string connectionString, string? migrationsAssemblyName)               // extension member on DbContextOptionsBuilder:
	                                                                       // Configure + NoTracking + binding-derived naming, one call
// NOTE: ApplyNorseConventions() is NO LONGER parameterless — it now requires
// Func<string,string> rewriteName. Call sites migrate to ApplyNorseProviderOptions instead.

// Norse.Persistence.EntityFramework.Migrations  (namespace Norse.Persistence.EntityFramework.Migrations)
public abstract class EfMigrationContributor<TContext>                     // moved here FROM .Design
public sealed class MigrationConnectionStringAttribute                     // moved here FROM .Design

// Norse.Persistence.EntityFramework.Design  (namespace Norse.Persistence.EntityFramework.Design)
public abstract class NorseDesignTimeDbContextFactory<TContext>            // the ONE neutral factory base
{
	protected abstract INorseEfProvider ProviderBinding { get; }
	protected abstract string DatabaseName { get; }
	protected virtual void ConfigureOptions(DbContextOptionsBuilder<TContext> builder)  // ONE param — no connectionString
	protected abstract TContext CreateContext(DbContextOptions<TContext> options);
}
// AddNorseDesignTimeServices(string) — unchanged; DesignTimeServices files need no edits.

// Norse.Persistence.EntityFramework.PostgreSQL / .SqlServer (thin bindings)
public sealed class NorsePostgresEfProvider  : INorseEfMigrationProvider { public static NorsePostgresEfProvider Instance { get; } }
public sealed class NorseSqlServerEfProvider : INorseEfMigrationProvider { public static NorseSqlServerEfProvider Instance { get; } }
```

Deleted in v0.0.8 (every remaining reference is a build break): packages `Norse.Persistence.EntityFramework.Design.PostgreSQL` / `.Design.SqlServer`, types `NorsePostgreSqlDesignTimeDbContextFactory<>` / `NorseSqlServerDesignTimeDbContextFactory<>`, the two-param `ConfigureOptions(builder, connectionString)` override shape, `DOTNET_EFTOOLS_CONNECTIONSTRING`.

---

### Task 1: Himinbjörg seam adoption

**Files:**
- Modify: `Himinbjorg/src/Identity.Migrations/Identity.Migrations.csproj`
- Modify: `Himinbjorg/src/Identity.Migrations/NorseIdentityMigrationContributor.cs`
- Modify: `Himinbjorg/src/Identity.Migrations.PostgreSQL/Identity.Migrations.PostgreSQL.csproj`
- Modify: `Himinbjorg/src/Identity.Migrations.PostgreSQL/NorseIdentityDbContextFactory.cs`
- Modify: `Himinbjorg/src/Identity.Migrations.SqlServer/Identity.Migrations.SqlServer.csproj`
- Modify: `Himinbjorg/src/Identity.Migrations.SqlServer/NorseIdentityDbContextFactory.cs`
- Modify: `Himinbjorg/tests/Identity.Migrations.Tests/Identity.Migrations.Tests.csproj`
- Modify: `Himinbjorg/tests/Identity.Migrations.Tests/NorseIdentityMigrationContributorTests.cs`
- Modify: `Himinbjorg/tests/Identity.Migrations.PostgreSQL.Tests/Identity.Migrations.PostgreSQL.Tests.csproj`
- Modify: `Himinbjorg/tests/Identity.Migrations.SqlServer.Tests/Identity.Migrations.SqlServer.Tests.csproj`
- Modify: `Himinbjorg/src/Identity.Web.Server/ServiceCollectionExtensions.cs` — **execution amendment (2026-07-28):** line 24's parameterless `ApplyNorseConventions()` no longer compiles against v0.0.8; it becomes `o.ApplyNorseConventions(NorseNameRewriters.LowerSnakeCase);` (behavior-identical — the parameterless overload applied snake_case, which this Postgres-hardcoded path relied on; `NorseNameRewriters` is already in the imported `Norse.Persistence.EntityFramework` namespace). This is a compile fix, not the quarantined runtime-registration migration — `UseNpgsql` and the registration shape stay untouched; the seam migration of this method remains deferred per the plan's non-goals.
- Not touched: both `DesignTimeServices.cs` files (still correct — `AddNorseDesignTimeServices` lives in `.Design`, unchanged), both `Migrations/` folders and ModelSnapshots, everything else in `Identity.Web.Server`.

**Interfaces:**
- Consumes: everything in Global Constraints' interface block.
- Produces: `Norse.Identity.Migrations` flowing `Norse.Persistence.EntityFramework.Migrations` transitively (Task 3's host build and Task 5's tests rely on this); `Norse.Identity.Migrations.PostgreSQL` flowing `NorsePostgresEfProvider` transitively (Task 3's generator discovery relies on this).

- [ ] **Step 1: Branch, then capture the red build**

```bash
git -C Himinbjorg checkout -b feature/ef-provider-seam-adoption
dotnet build Himinbjorg/Himinbjorg.slnx 2>&1 | tail -20
```

Expected: FAIL — `NU1101`/MSB errors on the deleted `Norse.Persistence.EntityFramework.Design.PostgreSQL`/`.SqlServer` project paths, and/or `CS0234`/`CS0246` on `NorsePostgreSqlDesignTimeDbContextFactory`. This is the failing state the existing test suite already encodes; no new tests are needed for the swap itself.

- [ ] **Step 2: Contributor project — reference and namespace move**

`Identity.Migrations.csproj` — replace the whole `<ItemGroup>`:

```xml
	<ItemGroup>
		<!-- Plain NorseRef, deliberately not NorseDesignRef: EfMigrationContributor<T> and the
		     migration-host choreography live here and must flow to the migrations service — the
		     seam generator also discovers contributors through this closure. -->
		<NorseRef Include="Persistence.EntityFramework.Migrations">
			<Repo>Urdarbrunnr</Repo>
		</NorseRef>
		<ProjectReference Include="../Identity.Web.Server/Identity.Web.Server.csproj" />
	</ItemGroup>
```

`NorseIdentityMigrationContributor.cs` — the only code change is the using:

```csharp
using Norse.Persistence.EntityFramework.Migrations;
```

(replacing `using Norse.Persistence.EntityFramework.Design;`; everything else in the file stays byte-identical.)

- [ ] **Step 3: PostgreSQL provider project — csproj + factory rebase**

`Identity.Migrations.PostgreSQL.csproj` — replace the `NorseDesignRef` block (keep `EmbeddedResource`, the `Microsoft.EntityFrameworkCore.Design` PackageReference, and the `ProjectReference` exactly as they are):

```xml
		<NorseDesignRef Include="Persistence.EntityFramework.Design">
			<Repo>Urdarbrunnr</Repo>
		</NorseDesignRef>
		<!-- Plain NorseRef, deliberately not NorseDesignRef: the binding must flow to the migrations
		     service's compilation — the seam generator discovers exactly one INorseEfMigrationProvider
		     in the host's reference closure (NORSE030 fires if this were PrivateAssets-blocked). -->
		<NorseRef Include="Persistence.EntityFramework.PostgreSQL">
			<Repo>Urdarbrunnr</Repo>
		</NorseRef>
```

`Identity.Migrations.PostgreSQL/NorseIdentityDbContextFactory.cs` — full new content (keep the existing `<summary>`/`<remarks>` doc comments verbatim):

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norse.Identity.Web.Server;
using Norse.Persistence.EntityFramework;
using Norse.Persistence.EntityFramework.Design;
using Norse.Persistence.EntityFramework.PostgreSQL;

namespace Norse.Identity.Migrations.PostgreSQL;

/// (existing summary + remarks doc comments, unchanged)
public sealed class NorseIdentityDbContextFactory : NorseDesignTimeDbContextFactory<NorseIdentityDbContext>
{
	/// <inheritdoc />
	protected override INorseEfProvider ProviderBinding => NorsePostgresEfProvider.Instance;

	/// <inheritdoc />
	protected override string DatabaseName => "norse_identity";

	/// <inheritdoc />
	protected override void ConfigureOptions(DbContextOptionsBuilder<NorseIdentityDbContext> builder)
	{
		base.ConfigureOptions(builder);

		var applicationServices = new ServiceCollection()
			.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
			.BuildServiceProvider();

		builder.UseApplicationServiceProvider(applicationServices);
	}

	/// <inheritdoc />
	protected override NorseIdentityDbContext CreateContext(DbContextOptions<NorseIdentityDbContext> options) =>
		new(options);
}
```

Signature change to note: the base's `ConfigureOptions` is now one-parameter (the placeholder connection string is derived internally from `ProviderBinding` + `DatabaseName`); base is called first per its own doc guidance — order relative to `UseApplicationServiceProvider` is not semantically load-bearing (both mutate independent option axes).

- [ ] **Step 4: SqlServer provider project — same shape**

`Identity.Migrations.SqlServer.csproj` — same replacement as Step 3 with `Persistence.EntityFramework.SqlServer` as the `NorseRef` (same comment).

`Identity.Migrations.SqlServer/NorseIdentityDbContextFactory.cs` — identical to Step 3's file except: `using Norse.Persistence.EntityFramework.SqlServer;` instead of `.PostgreSQL`, `namespace Norse.Identity.Migrations.SqlServer;`, and `ProviderBinding => NorseSqlServerEfProvider.Instance;`. Keep its own existing doc comments.

- [ ] **Step 5: Test project references + moved-namespace using**

`Identity.Migrations.Tests.csproj` — the attribute now flows transitively through Step 2's plain `NorseRef`, so the direct-reference workaround (and its comment) is deleted outright:

```xml
	<ItemGroup>
		<ProjectReference Include="../../src/Identity.Migrations/Identity.Migrations.csproj" />
	</ItemGroup>
```

`NorseIdentityMigrationContributorTests.cs` — first line becomes:

```csharp
using Norse.Persistence.EntityFramework.Migrations;
```

`Identity.Migrations.PostgreSQL.Tests.csproj` — swap the `NorseDesignRef` Include from `Persistence.EntityFramework.Design.PostgreSQL` to `Persistence.EntityFramework.Design`; keep the existing "Needed directly, not just transitively" comment, updating its last sentence to name the new base: `NorseIdentityDbContextFactory (NorseDesignTimeDbContextFactory<>) lives in the same assembly this test instantiates directly.`

`Identity.Migrations.SqlServer.Tests.csproj` — add the same direct `NorseDesignRef` block + comment above its `ProjectReference` (it was missing pre-sweep; its Postgres sibling documents why it's required in package mode — this closes that inconsistency):

```xml
	<ItemGroup>
		<!-- Needed directly, not just transitively via the ProjectReference below: the src project
		     marks its own NorseDesignRef to this package PrivateAssets="all" (correctly, so runtime
		     consumers don't inherit design-time tooling), which blocks it from flowing here in
		     package mode. NorseIdentityDbContextFactory (NorseDesignTimeDbContextFactory<>) lives
		     in the same assembly this test instantiates directly. -->
		<NorseDesignRef Include="Persistence.EntityFramework.Design">
			<Repo>Urdarbrunnr</Repo>
		</NorseDesignRef>
		<ProjectReference Include="../../src/Identity.Migrations.SqlServer/Identity.Migrations.SqlServer.csproj" />
	</ItemGroup>
```

No test-body changes in the two factory test files — snake_case-on-Postgres / PascalCase-on-SqlServer expectations are unchanged (the naming decision moved into the bindings; observable model output is identical).

- [ ] **Step 6: Build and run the whole realm's tests**

```bash
dotnet build Himinbjorg/Himinbjorg.slnx
dotnet test Himinbjorg/Himinbjorg.slnx
```

Expected: build PASS; all 4 test projects PASS (the existing factory tests are the regression net proving the rebase reproduces the old model output — `user_passkeys` present with snake_case on Postgres, `UserPasskeys`/PascalCase on SqlServer).

- [ ] **Step 7: Commit (on the realm branch)**

```bash
git -C Himinbjorg branch --show-current   # must print feature/ef-provider-seam-adoption
git -C Himinbjorg add -A
git -C Himinbjorg commit -m "Adopt the Urdarbrunnr provider seam in the migrations projects"
```

---

### Task 2: Mímisbrunnr seam adoption

**Files:**
- Modify: `Mimisbrunnr/src/Reference.Data.Migrations/Reference.Data.Migrations.csproj`
- Modify: `Mimisbrunnr/src/Reference.Data.Migrations/NorseReferenceMigrationContributor.cs`
- Modify: `Mimisbrunnr/src/Reference.Data.Migrations.PostgreSQL/Reference.Data.Migrations.PostgreSQL.csproj`
- Modify: `Mimisbrunnr/src/Reference.Data.Migrations.PostgreSQL/ReferenceDbContextFactory.cs`
- Modify: `Mimisbrunnr/src/Reference.Data.Migrations.SqlServer/Reference.Data.Migrations.SqlServer.csproj`
- Modify: `Mimisbrunnr/src/Reference.Data.Migrations.SqlServer/ReferenceDbContextFactory.cs`
- Modify: `Mimisbrunnr/tests/Reference.Data.Tests/Reference.Data.Tests.csproj`
- Modify: `Mimisbrunnr/tests/Reference.Data.Tests/NorseReferenceMigrationContributorTests.cs`
- Modify: `Mimisbrunnr/tests/Reference.Data.Tests/ReferenceSeedContributorTests.cs`
- Modify: `Mimisbrunnr/tests/Reference.Data.Tests/CountryOrAreaViewTests.cs`
- Not touched: both `DesignTimeServices.cs`, `Reference.Data`, `ReferenceDataSeedContributor.cs` (no persistence-chassis usings), `SeedTool`, seeds, migrations folders/snapshots, `ReferenceDbContextModelTests.cs` (raw `UseNpgsql`, no conventions call), `PostgresContainerFixture{,Tests}.cs`, `PostgresCollection.cs`.

**Interfaces:**
- Consumes: Global Constraints' interface block.
- Produces: `Norse.Reference.Data.Migrations` flowing `.Migrations` transitively; `Norse.Reference.Data.Migrations.PostgreSQL` flowing `NorsePostgresEfProvider` transitively (Task 3 relies on both). Test helper shape `MigratedContextAsync(string connectionString, CancellationToken cancellationToken)` retained — Task 6 moves these files as-is.

- [ ] **Step 1: Branch, capture red**

```bash
git -C Mimisbrunnr checkout -b feature/ef-provider-seam-adoption
dotnet build Mimisbrunnr/Mimisbrunnr.slnx 2>&1 | tail -20
```

Expected: FAIL, same deleted-package/type errors as Task 1 Step 1 — plus `CS7036` (missing `rewriteName` argument) at the three parameterless `ApplyNorseConventions()` call sites in tests once project resolution is past.

- [ ] **Step 2: Contributor project**

`Reference.Data.Migrations.csproj` — replace only the `NorseDesignRef` block; keep `None` (seeds), both `Primitives` NorseRefs, and the `ProjectReference` untouched:

```xml
		<!-- Plain NorseRef, deliberately not NorseDesignRef: EfMigrationContributor<T> and the
		     migration-host choreography live here and must flow to the migrations service — the
		     seam generator also discovers contributors through this closure. -->
		<NorseRef Include="Persistence.EntityFramework.Migrations">
			<Repo>Urdarbrunnr</Repo>
		</NorseRef>
```

`NorseReferenceMigrationContributor.cs` — first line becomes:

```csharp
using Norse.Persistence.EntityFramework.Migrations;
```

- [ ] **Step 3: Both provider projects — csproj + factory rebase**

Both `.csproj` files: same replacement pattern as Task 1 Step 3 — `NorseDesignRef` → `Persistence.EntityFramework.Design`, plus the flowing `NorseRef` to `Persistence.EntityFramework.PostgreSQL` / `.SqlServer` respectively, with the NORSE030 comment. Keep `EmbeddedResource`, the `Microsoft.EntityFrameworkCore.Design` PackageReference (with its existing leaf-project comment), and the `ProjectReference`.

`Reference.Data.Migrations.PostgreSQL/ReferenceDbContextFactory.cs` — full new content:

```csharp
using Microsoft.EntityFrameworkCore;
using Norse.Persistence.EntityFramework;
using Norse.Persistence.EntityFramework.Design;
using Norse.Persistence.EntityFramework.PostgreSQL;

namespace Norse.Reference.Data.Migrations.PostgreSQL;

/// <summary>
/// Design-time factory for <see cref="ReferenceDbContext"/>, used only by <c>dotnet ef</c> tooling
/// (e.g. <c>dotnet ef migrations add</c>) to construct a context instance outside of DI at design time.
/// </summary>
public sealed class ReferenceDbContextFactory : NorseDesignTimeDbContextFactory<ReferenceDbContext>
{
	/// <inheritdoc />
	protected override INorseEfProvider ProviderBinding => NorsePostgresEfProvider.Instance;

	/// <inheritdoc />
	protected override string DatabaseName => "norse_reference";

	/// <inheritdoc />
	protected override ReferenceDbContext CreateContext(DbContextOptions<ReferenceDbContext> options) =>
		new(options);
}
```

SqlServer sibling: identical except `using Norse.Persistence.EntityFramework.SqlServer;`, `namespace Norse.Reference.Data.Migrations.SqlServer;`, `ProviderBinding => NorseSqlServerEfProvider.Instance;`.

- [ ] **Step 4: Test csproj — drop the deleted package**

`Reference.Data.Tests.csproj` — delete the `NorseDesignRef Include="Persistence.EntityFramework.Design.PostgreSQL"` block **and** its multi-line comment (the binding now flows transitively via the `Migrations.PostgreSQL` project's plain `NorseRef`). Keep: the `Persistence.EntityFramework.Design` NorseDesignRef (the tests still instantiate `ReferenceDbContextFactory`, whose base lives there), `Npgsql.EntityFrameworkCore.PostgreSQL`, `Testcontainers.PostgreSql`, and all three ProjectReferences.

- [ ] **Step 5: Migrate the three broken `ApplyNorseConventions()` call sites onto the seam**

The hand-rolled `UseNpgsql` + `ApplyNorseConventions()` + `ApplyNorseTrackingBehavior()` trio predates the seam and no longer compiles (parameterless `ApplyNorseConventions` is gone). Replace it with the one-call choreography — this is the same code path the migrations service runs, which is exactly what these tests exist to prove.

In `NorseReferenceMigrationContributorTests.cs`, replace

```csharp
		var optionsBuilder = new DbContextOptionsBuilder<ReferenceDbContext>()
			.UseNpgsql(fixture.ConnectionString, o =>
				o.MigrationsAssembly(typeof(ReferenceDbContextFactory).Assembly.GetName().Name));
		optionsBuilder
			.ApplyNorseConventions()
			.ApplyNorseTrackingBehavior();
		var options = optionsBuilder.Options;
		using ReferenceDbContext context = new(options);
```

with

```csharp
		DbContextOptionsBuilder<ReferenceDbContext> optionsBuilder = new();
		optionsBuilder.ApplyNorseProviderOptions(NorsePostgresEfProvider.Instance,
			fixture.ConnectionString, typeof(ReferenceDbContextFactory).Assembly.GetName().Name);
		using ReferenceDbContext context = new(optionsBuilder.Options);
```

and add `using Norse.Persistence.EntityFramework.PostgreSQL;` to the usings (keep `using Norse.Persistence.EntityFramework;`).

In `ReferenceSeedContributorTests.cs` and `CountryOrAreaViewTests.cs`, apply the identical replacement inside each file's `MigratedContextAsync` helper (same using addition; the helper's construction lines become the three lines above with `connectionString` in place of `fixture.ConnectionString` — **dropping the `using` keyword on the context declaration**, since the helper returns the context to a caller who owns disposal, exactly as the original helper does — then `await new NorseReferenceMigrationContributor(context).MigrateAsync(cancellationToken);` continues unchanged).

- [ ] **Step 6: Build and test (Docker required)**

```bash
dotnet build Mimisbrunnr/Mimisbrunnr.slnx
dotnet test Mimisbrunnr/Mimisbrunnr.slnx
```

Expected: build PASS; both test projects PASS including the container suite (12 Reference.Data.Tests + SeedTool.Tests) — proving migration + full UN M49 seed against real Postgres through the new seam.

- [ ] **Step 7: Commit**

```bash
git -C Mimisbrunnr branch --show-current   # must print feature/ef-provider-seam-adoption
git -C Mimisbrunnr add -A
git -C Mimisbrunnr commit -m "Adopt the Urdarbrunnr provider seam in the migrations projects"
```

---

### Task 3: Yggdrasil — generator reference move + CPM pins

**Files:**
- Modify: `Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj`
- Modify: `Yggdrasil/Directory.Packages.props` (this is Yggdrasil's CPM manifest, not a scatter-managed build-props file — editable)
- Not touched: `Hosting.Migrations.Service/Program.cs` (stays three lines), every other Yggdrasil project.

**Interfaces:**
- Consumes: Tasks 1–2's transitive flows; the generator ships as the analyzer asset of `Norse.Persistence.EntityFramework.Migrations` and its dev-mode analyzer project resolves at `Urdarbrunnr/gen/Persistence.EntityFramework.Migrations.Generator/` via the `Generator="true"` mapping in Bifröst's root `Directory.Build.targets`.
- Produces: a compiling migrations host whose generated `AddNorseMigrations()` registers both contributors, both seeders, and the Postgres binding — consumed by Task 4's live gate.

- [ ] **Step 1: Branch, capture red**

```bash
git -C Yggdrasil checkout -b feature/ef-provider-seam-adoption
dotnet build Yggdrasil/Yggdrasil.slnx 2>&1 | tail -15
```

Expected: FAIL — the `Design.PostgreSQL` project path no longer exists under `Urdarbrunnr/src/`.

- [ ] **Step 2: Swap the generator reference**

In `Hosting.Migrations.Service.csproj`, replace

```xml
		<NorseRef Include="Persistence.EntityFramework.Design.PostgreSQL">
			<Repo>Urdarbrunnr</Repo>
			<Generator>true</Generator>
		</NorseRef>
```

with (same alphabetical slot):

```xml
		<NorseRef Include="Persistence.EntityFramework.Migrations">
			<Repo>Urdarbrunnr</Repo>
			<Generator>true</Generator>
		</NorseRef>
```

Provider selection is now expressed entirely by which realm migration packages this host references (`Identity.Migrations.PostgreSQL` + `Reference.Data.Migrations.PostgreSQL` → exactly one binding, `NorsePostgresEfProvider`, in the closure — both packages contributing the same binding *type* is fine; discovery walks types, not references, so shared bindings deduplicate by construction). Swapping the platform to SQL Server later = swapping those two realm refs to their `.SqlServer` siblings. Two distinct failure modes stay compile errors: a mixed closure (Postgres *and* SqlServer binding types both visible) is NORSE031; a doubled per-context closure (both of one realm's provider migration assemblies visible, i.e. two ModelSnapshots for the same context) is NORSE034.

- [ ] **Step 3: CPM pins to v0.0.8 topology**

In `Yggdrasil/Directory.Packages.props`:
1. `<UrdarbrunnrVersion>0.0.7</UrdarbrunnrVersion>` → `0.0.8`.
2. Delete the two lines pinning `Norse.Persistence.EntityFramework.Design.PostgreSQL` and `...Design.SqlServer` (packages no longer exist at 0.0.8).
3. Add, alphabetically between `.Design` and `.PostgreSQL`:

```xml
		<PackageVersion Include="Norse.Persistence.EntityFramework.Migrations" Version="$(UrdarbrunnrVersion)" />
```

4. Update the comment above the block if it still narrates the old package list.

- [ ] **Step 4: Build and test**

```bash
dotnet build Yggdrasil/Yggdrasil.slnx
dotnet test Yggdrasil/Yggdrasil.slnx
```

Expected: build PASS with zero NORSE03x diagnostics; all Yggdrasil test projects PASS. If NORSE030 fires, a binding `NorseRef` in Task 1/2 was wrongly made a `NorseDesignRef`; if NORSE032, a ModelSnapshot didn't resolve — stop and re-check the realm wiring rather than working around it.

- [ ] **Step 5: Commit**

```bash
git -C Yggdrasil branch --show-current   # must print feature/ef-provider-seam-adoption
git -C Yggdrasil add -A
git -C Yggdrasil commit -m "Move the migrations generator reference onto the provider seam"
```

---

### Task 4: Bifröst live gate — the resurrection

**Files:** none modified. Spec §11: no composition change; the AppHost run *is* the verification.

**Interfaces:**
- Consumes: Tasks 1–3 complete; Docker running.
- Produces: the migrations service running to completion — `norse_identity` and `norse_reference` stood up with schema + seed. This is the structural proof that the 2026-07-25 assembly-resolution bug (migrations assembly inferred from the contributor's assembly instead of the snapshot's) is dead.

- [ ] **Step 1: Run the AppHost**

```bash
# From the Bifröst root:
dotnet run --project src/Orchestration.AppHost
```

Run in background; poll the output/dashboard for up to ~5 minutes.

- [ ] **Step 2: Verify completion**

Expected evidence, all three required:
1. The migrations service resource reaches **Finished/Exited with code 0** (it is an init-style run-to-completion worker).
2. Its logs show both contributors applying: `Norse.Identity` and `Norse.Reference` migration runs, followed by the seeding runner completing (UN M49 load).
3. No `MissingMethodException`, no assembly-resolution failure, no NORSE diagnostics at runtime.

If the Postgres primary/replica resources fail to start, that is environmental (Docker), not this sweep — restart Docker and rerun before diagnosing code.

- [ ] **Step 3: Tear down**

Stop the AppHost process. No commit — nothing changed.

---

### Task 5: Himinbjörg container parity — live migration proof for `norse_identity`

Ports Mímisbrunnr's proven Testcontainers pattern. The money assertion: the v3 passkey table physically exists after a real migrate — the exact silent-fallback failure (`SchemaVersion` → `Version1`) this realm's CLAUDE.md warns about, now covered by a live test instead of model-only tests.

**Files:**
- Modify: `Himinbjorg/tests/Identity.Migrations.Tests/Identity.Migrations.Tests.csproj`
- Create: `Himinbjorg/tests/Identity.Migrations.Tests/PostgresContainerFixture.cs`
- Create: `Himinbjorg/tests/Identity.Migrations.Tests/PostgresCollection.cs`
- Create: `Himinbjorg/tests/Identity.Migrations.Tests/NorseIdentityMigrationContributorContainerTests.cs`

**Interfaces:**
- Consumes: `NorseIdentityMigrationContributor(NorseIdentityDbContext context)` with `MigrateAsync(CancellationToken)`; `ApplyNorseProviderOptions` + `NorsePostgresEfProvider.Instance` (flow transitively through the Task 1 references); `NorseIdentityDbContextFactory` (PostgreSQL) for the migrations-assembly name.
- Produces: nothing downstream — terminal test coverage.

- [ ] **Step 1: Write the failing test (and its fixture)**

`PostgresContainerFixture.cs` — Mímisbrunnr's fixture with the database name changed (xUnit collection fixtures are assembly-scoped, so a per-assembly copy is structurally required, not a DRY violation):

```csharp
using Testcontainers.PostgreSql;

namespace Norse.Identity.Migrations.Tests;

public sealed class PostgresContainerFixture : IAsyncLifetime
{
	readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:19beta2")
		.WithDatabase("norse_identity")
		.Build();

	// null! justified: hydrated by InitializeAsync before xUnit hands the fixture to any test.
	public string ConnectionString { get; private set; } = null!;

	public async ValueTask InitializeAsync()
	{
		await _container.StartAsync();
		ConnectionString = _container.GetConnectionString();
	}

	public ValueTask DisposeAsync() =>
		_container.DisposeAsync();
}
```

`PostgresCollection.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Norse.Identity.Migrations.Tests;

[CollectionDefinition("Postgres")]
[SuppressMessage("Design", "CA1711:Identifiers should not have incorrect suffix", Justification = "xUnit collection fixture naming convention")]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>;
```

`NorseIdentityMigrationContributorContainerTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Norse.Identity.Migrations.PostgreSQL;
using Norse.Identity.Web.Server;
using Norse.Persistence.EntityFramework;
using Norse.Persistence.EntityFramework.PostgreSQL;

namespace Norse.Identity.Migrations.Tests;

[Collection("Postgres")]
public sealed class NorseIdentityMigrationContributorContainerTests(PostgresContainerFixture fixture)
{
	[Fact]
	async Task MigrateAsync_applies_InitialCreate_and_stands_up_the_v3_passkey_table()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		DbContextOptionsBuilder<NorseIdentityDbContext> optionsBuilder = new();
		optionsBuilder.ApplyNorseProviderOptions(NorsePostgresEfProvider.Instance,
			fixture.ConnectionString, typeof(NorseIdentityDbContextFactory).Assembly.GetName().Name);
		await using NorseIdentityDbContext context = new(optionsBuilder.Options);
		NorseIdentityMigrationContributor contributor = new(context);

		await contributor.MigrateAsync(cancellationToken);

		(await context.Database.GetAppliedMigrationsAsync(cancellationToken))
			.ShouldContain(m => m.Contains("InitialCreate", StringComparison.Ordinal));
		// Queries the physical table: 42P01 here — not a vacuous green — if SchemaVersion silently
		// fell back to Version1 and the passkey table was never created.
		(await context.Set<NorseUserPasskey>().AnyAsync(cancellationToken)).ShouldBeFalse();
	}
}
```

(Schema v3 on this path comes from `NorseIdentityDbContext.OnConfiguring`'s documented fallback service provider — the same path the migrations service exercises; the test deliberately does not wire `IdentityOptions` itself.)

`Identity.Migrations.Tests.csproj` — full new content:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<!-- Needed directly, not just transitively: Migrations.PostgreSQL marks its NorseDesignRef
		     to the Design chassis PrivateAssets="all", which blocks it from flowing here in package
		     mode — and the container test references NorseIdentityDbContextFactory
		     (NorseDesignTimeDbContextFactory<>) for the migrations-assembly name. -->
		<NorseDesignRef Include="Persistence.EntityFramework.Design">
			<Repo>Urdarbrunnr</Repo>
		</NorseDesignRef>
		<PackageReference Include="Testcontainers.PostgreSql" Version="*" />
		<ProjectReference Include="../../src/Identity.Migrations/Identity.Migrations.csproj" />
		<ProjectReference Include="../../src/Identity.Migrations.PostgreSQL/Identity.Migrations.PostgreSQL.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Run to verify it fails meaningfully**

New-coverage characterization: prove the test can fail by its own machinery before trusting green. Run once with the final assertion temporarily flipped to `ShouldBeTrue()`:

```bash
dotnet test Himinbjorg/Himinbjorg.slnx --filter-class "*ContainerTests"
```

Expected: FAIL on the flipped assertion (container starts, migration applies, table is empty). Flip back to `ShouldBeFalse()`.

- [ ] **Step 3: Run to verify it passes**

```bash
dotnet test Himinbjorg/Himinbjorg.slnx
```

Expected: PASS — all projects, container test included.

- [ ] **Step 4: Commit**

```bash
git -C Himinbjorg branch --show-current   # must print feature/ef-provider-seam-adoption
git -C Himinbjorg add -A
git -C Himinbjorg commit -m "Prove norse_identity migrations against a live Postgres container"
```

---

### Task 6: Mímisbrunnr test-parity split — 1:1 test project per package

Standing platform law: one test project per NuGet package. Src has 4 packable projects; tests get 4 matching projects (SeedTool.Tests already covers the unpacked tool). Existing tests are *moved*, not rewritten; only namespaces change. New coverage is limited to the two factory test classes (characterization of existing behavior, mirroring Himinbjörg's).

**Files:**
- Create: `Mimisbrunnr/tests/Reference.Data.Migrations.Tests/Reference.Data.Migrations.Tests.csproj`
- Move (git mv): `NorseReferenceMigrationContributorTests.cs`, `ReferenceSeedContributorTests.cs` from `tests/Reference.Data.Tests/` → `tests/Reference.Data.Migrations.Tests/`
- Create: `Mimisbrunnr/tests/Reference.Data.Migrations.Tests/PostgresContainerFixture.cs`, `PostgresCollection.cs` (assembly-scoped copies, same rationale as Task 5)
- Create: `Mimisbrunnr/tests/Reference.Data.Migrations.PostgreSQL.Tests/Reference.Data.Migrations.PostgreSQL.Tests.csproj` + `ReferenceDbContextFactoryTests.cs`
- Create: `Mimisbrunnr/tests/Reference.Data.Migrations.SqlServer.Tests/Reference.Data.Migrations.SqlServer.Tests.csproj` + `ReferenceDbContextFactoryTests.cs`
- Modify: `Mimisbrunnr/Mimisbrunnr.slnx` (three new `<Project>` entries in the `/tests/` folder)
- Modify: `Mimisbrunnr/tests/Reference.Data.Tests/Reference.Data.Tests.csproj` (only if the build proves a reference is now unused — `CountryOrAreaViewTests` keeps using the fixture, the contributor, the seeder, and the factory, so expect **no** reference removals)
- Stays put in `Reference.Data.Tests`: `CountryOrAreaViewTests.cs` (subject is `CountryOrArea.View` — an entity concern), `ReferenceDbContextModelTests.cs`, `PostgresContainerFixture.cs` + `PostgresCollection.cs` + `PostgresContainerFixtureTests.cs`.

**Interfaces:**
- Consumes: Task 2's helper shape (moved verbatim); `ReferenceDbContextFactory.CreateDbContext(string[])` from both provider assemblies.
- Produces: `Norse.Reference.Data.Migrations.Tests` / `...Migrations.PostgreSQL.Tests` / `...Migrations.SqlServer.Tests` assemblies whose names satisfy the hoisted `InternalsVisibleTo("$(AssemblyName).Tests")` grants automatically.

- [ ] **Step 1: Create `Reference.Data.Migrations.Tests` and move the two container suites**

```bash
mkdir -p Mimisbrunnr/tests/Reference.Data.Migrations.Tests
git -C Mimisbrunnr mv tests/Reference.Data.Tests/NorseReferenceMigrationContributorTests.cs tests/Reference.Data.Migrations.Tests/
git -C Mimisbrunnr mv tests/Reference.Data.Tests/ReferenceSeedContributorTests.cs tests/Reference.Data.Migrations.Tests/
```

In both moved files: namespace becomes `Norse.Reference.Data.Migrations.Tests`; delete the now-redundant `using Norse.Reference.Data.Migrations;` (namespace nesting resolves those types — and `ReferenceDbContext`/`CountryOrArea`/`Region` too, so do **not** add `using Norse.Reference.Data;`; IDE0005 rides at error). Keep `using Norse.Reference.Data.Migrations.PostgreSQL;` (sibling branch, not reachable by walk-up). All other usings and every test body stay byte-identical.

Copy `PostgresContainerFixture.cs` and `PostgresCollection.cs` from `Reference.Data.Tests` into the new project, changing only the namespace to `Norse.Reference.Data.Migrations.Tests` (no fixture-tests copy — the fixture's own smoke test runs once, in `Reference.Data.Tests`).

`Reference.Data.Migrations.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<!-- Needed directly, not just transitively: Migrations.PostgreSQL marks its NorseDesignRef
		     to the Design chassis PrivateAssets="all", which blocks it from flowing here in package
		     mode — and these tests reference ReferenceDbContextFactory
		     (NorseDesignTimeDbContextFactory<>) for the migrations-assembly name. -->
		<NorseDesignRef Include="Persistence.EntityFramework.Design">
			<Repo>Urdarbrunnr</Repo>
		</NorseDesignRef>
		<PackageReference Include="Testcontainers.PostgreSql" Version="*" />
		<ProjectReference Include="../../src/Reference.Data.Migrations/Reference.Data.Migrations.csproj" />
		<ProjectReference Include="../../src/Reference.Data.Migrations.PostgreSQL/Reference.Data.Migrations.PostgreSQL.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Write the two factory test classes**

`tests/Reference.Data.Migrations.PostgreSQL.Tests/ReferenceDbContextFactoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.Reference.Data.Migrations.PostgreSQL.Tests;

public sealed class ReferenceDbContextFactoryTests
{
	[Fact]
	void CreateDbContext_applies_snake_case_naming()
	{
		ReferenceDbContextFactory factory = new();
		using var context = factory.CreateDbContext([]);

		var entityType = context.Model.FindEntityType(typeof(CountryOrArea));

		entityType.ShouldNotBeNull();
		entityType.GetTableName().ShouldBe("country_or_area");
		entityType.FindProperty(nameof(CountryOrArea.Alpha2))!.GetColumnName().ShouldBe("alpha2");
	}

	[Fact]
	void CreateDbContext_forces_no_tracking()
	{
		ReferenceDbContextFactory factory = new();
		using var context = factory.CreateDbContext([]);

		context.ChangeTracker.QueryTrackingBehavior.ShouldBe(QueryTrackingBehavior.NoTracking);
	}
}
```

SqlServer sibling (`...Migrations.SqlServer.Tests/ReferenceDbContextFactoryTests.cs`): namespace `Norse.Reference.Data.Migrations.SqlServer.Tests`; identical tests except the naming test is `CreateDbContext_keeps_engine_native_pascal_case` asserting `GetTableName().ShouldBe("CountryOrArea")` and `GetColumnName().ShouldBe("Alpha2")` (expected values straight from the house-rules receipts for this exact model).

Both csprojs, same shape (swap `PostgreSQL` ↔ `SqlServer`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<!-- Needed directly, not just transitively via the ProjectReference below: the src project
		     marks its own NorseDesignRef to this package PrivateAssets="all", which blocks it from
		     flowing here in package mode. ReferenceDbContextFactory
		     (NorseDesignTimeDbContextFactory<>) lives in the same assembly this test instantiates
		     directly. -->
		<NorseDesignRef Include="Persistence.EntityFramework.Design">
			<Repo>Urdarbrunnr</Repo>
		</NorseDesignRef>
		<ProjectReference Include="../../src/Reference.Data.Migrations.PostgreSQL/Reference.Data.Migrations.PostgreSQL.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 3: Register the three projects in `Mimisbrunnr.slnx`**

Add to the `/tests/` folder, keeping alphabetical order:

```xml
		<Project Path="tests/Reference.Data.Migrations.PostgreSQL.Tests/Reference.Data.Migrations.PostgreSQL.Tests.csproj" />
		<Project Path="tests/Reference.Data.Migrations.SqlServer.Tests/Reference.Data.Migrations.SqlServer.Tests.csproj" />
		<Project Path="tests/Reference.Data.Migrations.Tests/Reference.Data.Migrations.Tests.csproj" />
```

- [ ] **Step 4: Prove the new factory tests can fail, then run everything**

Temporarily flip one assertion per new class (e.g. `ShouldBe("country_or_area")` → `ShouldBe("countries")`), run, confirm FAIL, flip back. Then:

```bash
dotnet test Mimisbrunnr/Mimisbrunnr.slnx
```

Expected: PASS across all 5 test projects — moved container suites green in their new home, both factory suites green, `Reference.Data.Tests` still green with what remains.

- [ ] **Step 5: Commit**

```bash
git -C Mimisbrunnr branch --show-current   # must print feature/ef-provider-seam-adoption
git -C Mimisbrunnr add -A
git -C Mimisbrunnr commit -m "Split tests to 1:1 per-package parity and add provider factory coverage"
```

---

### Task 7: Documentation sync (boy-scout law)

**Files:**
- Modify: `Urdarbrunnr/CLAUDE.md` — **stage only, no commit** (master; doc-only)
- Modify: `Himinbjorg/CLAUDE.md` + `Himinbjorg/README.md` (commit on realm branch)
- Modify: `Mimisbrunnr/CLAUDE.md` + `Mimisbrunnr/README.md` (commit on realm branch)
- Modify: `Yggdrasil/README.md` / `Yggdrasil/CLAUDE.md` only if the grep below hits (commit on realm branch)

**Interfaces:** none — prose only. No code, no builds beyond a final grep.

- [ ] **Step 1: Retract Urðarbrunnr's "expected red" paragraph**

In `Urdarbrunnr/CLAUDE.md`, replace the paragraph beginning `**Not yet done, explicitly:** the adoption sweep across Himinbjörg, Mímisbrunnr, and Yggdrasil…` with a landed statement, dated 2026-07-28: the §11 adoption sweep is implemented on each realm's local `feature/ef-provider-seam-adoption` branch (Himinbjörg, Mímisbrunnr, Yggdrasil), Bifröst's dev-mode migrations composition is green again, and the AppHost live gate stood up `norse_identity` + `norse_reference`; realm merges/releases are the human's ship gates. Then `git -C Urdarbrunnr add CLAUDE.md` — stop, no commit.

- [ ] **Step 2: Sweep realm docs for the dead surface**

```bash
grep -rn "Design.PostgreSQL\|Design.SqlServer\|NorsePostgreSqlDesignTimeDbContextFactory\|NorseSqlServerDesignTimeDbContextFactory\|DOTNET_EFTOOLS" \
	Himinbjorg/README.md Himinbjorg/CLAUDE.md Mimisbrunnr/README.md Mimisbrunnr/CLAUDE.md \
	Yggdrasil/README.md Yggdrasil/CLAUDE.md
```

Fix every hit to describe the new shape (neutral `.Design` base + thin binding packages + `.Migrations` generator home). Historical narrative that *names* the old packages as history (e.g. Urðarbrunnr's "the repackaging deleted…") stays — only present-tense descriptions of the current shape change.

- [ ] **Step 3: Reflect the new test topology where docs enumerate it**

If `Mimisbrunnr/README.md` or `CLAUDE.md` lists test projects, add the three new ones; mention Himinbjörg's container coverage where that realm's docs describe its test posture. README and CLAUDE.md must tell the same story per repo.

- [ ] **Step 4: Commit realm doc changes (not Urðarbrunnr, not Glitnir)**

```bash
git -C Himinbjorg branch --show-current && git -C Himinbjorg add -A && git -C Himinbjorg commit -m "Sync docs to the provider-seam shape"
git -C Mimisbrunnr branch --show-current && git -C Mimisbrunnr add -A && git -C Mimisbrunnr commit -m "Sync docs to the provider-seam shape"
# Yggdrasil only if Step 2/3 touched it.
```

---

## Deferred / handoff to Buvy (explicitly not in this plan)

- **Merges, pushes, tags, releases** — all three realm branches stay local; Buvy merges and runs the release fan-out.
- **Package-mode verification** (`UseProjectReferences=false` against pure NuGet) — meaningful only after new Himinbjörg/Mímisbrunnr/Yggdrasil versions ship; then bump Yggdrasil's CPM realm pins in the same pass.
- **`Identity.Web.Server` runtime registration onto the seam** (`AddNorseContext(binding, …)` replacing the hand-rolled `UseNpgsql`) — a behavior change; ruled out of scope for this sweep, natural companion to the ISO country-lookup design session.
- **ISO country-code gRPC lookup service (Mímir)** — the next design-court brainstorm, riding on this sweep.
