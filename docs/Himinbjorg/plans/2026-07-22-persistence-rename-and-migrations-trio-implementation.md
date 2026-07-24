# Himinbjörg Persistence Rename + Migrations Trio Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix Himinbjörg's stale `Norse.EntityFramework.*` → `Norse.Persistence.EntityFramework.*` references and split the Postgres-only `Identity.Migrations` project into a provider-agnostic base plus `Identity.Migrations.PostgreSQL` and `Identity.Migrations.SqlServer` siblings, mirroring Mímisbrunnr's already-shipped `Reference.Data.Migrations*` split.

**Architecture:** The two provider projects each `ProjectReference` the agnostic base directly (never each other) — identical shape to Urðarbrunnr's own `Persistence.EntityFramework.Design.PostgreSQL`/`.SqlServer` referencing `Persistence.EntityFramework.Design`. A downstream consumer (Yggdrasil's migrations service, in a later piece of work) takes exactly one provider package and gets the contributor transitively. Test projects follow the same 1:1-per-package split.

**Tech Stack:** .NET 11 preview, EF Core 11 preview (`dotnet-ef` 11.0.0-preview.6 installed globally), MSBuild `NorseRef` custom item (resolves to `ProjectReference` in this Bifröst dev-mode tree, confirmed via `UseProjectReferences=true` in `Bifrost/Directory.Build.props`), `.slnx` solution format.

**Spec:** `docs/Himinbjorg/specs/2026-07-22-persistence-rename-and-migrations-trio-design.md` (this same Glitnir repo).

## Global Constraints

- Repo root for all work: `Himinbjorg/`, on the existing branch `feature/identity-web-server` (unpushed, already carries the Phase 2 web-server work — do not switch branches).
- No automatic git commits — stage (`git add`) and show `git status --short` at the end of each task, then stop for human review. Never push, never touch `master`.
- Package versions in this repo float to the major version only: `Version="11.*-*"` for every EF-family `PackageReference` — never a bare `*`, never an exact pin. Exact pins live only in Yggdrasil's `Directory.Packages.props` (out of scope here).
- `sealed` by default; both new factory classes are `public sealed` (required — `dotnet ef` discovers `IDesignTimeDbContextFactory<T>` via reflection across the assembly, same reason the existing Postgres-only factory is `public`).
- `IsAotCompatible=false` on every project touching EF Core (`src/Directory.Build.props` defaults it to `true` platform-wide — every EF project in this repo already overrides it; new projects must too).
- One test project per shipped NuGet package, no exceptions — mechanically required by `src/Directory.Build.props`'s `<InternalsVisibleTo Include="$(AssemblyName).Tests" />`, which only grants internals access to a test assembly named exactly `{AssemblyName}.Tests`.
- **Out of scope, explicitly:** Yggdrasil's matching stale `NorseRef`/CPM drift, the Yggdrasil ASP.NET Identity reference excision, gRPC/Blazor/Mediator/validation hardening, and any new features. None of this plan's tasks touch Yggdrasil.
- `IDeferredSignIn` needs no code change — `NorseSignInManager.cs` already correctly consumes `Norse.Abstractions.Web.Server.DeferredSignIn.IDeferredSignIn`. Task 6 verifies this still compiles/passes once the build is green again; no task modifies that file.
- Every core ASP.NET Identity entity's table name drops the `AspNet` prefix at the canonical (provider-agnostic) layer and uses PascalCase (`Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `UserTokens`, `RoleClaims`, `UserPasskeys`) — never hardcoded lowercase/snake_case in `src/Identity/*.cs`. Each provider's own naming convention does the casing: Postgres's `NorseSnakeCaseNamingConvention` rewrites to `snake_case` (unchanged end result from before this plan for the three entities that already had explicit names; newly correct for the five that didn't), SQL Server's default (`UseSnakeCaseNaming => false`) leaves it PascalCase, `AspNet`-free. Same rule for index database names: no explicit `.HasDatabaseName(...)` override — EF's own default `IX_{Table}_{Property}` naming, provider-converted the same way.

---

## File Structure

```
src/Identity/Identity.csproj                        (modified — NorseRef rename only)

src/Identity.Migrations/                             (existing project, slimmed to provider-agnostic)
  NorseIdentityMigrationContributor.cs                (modified — using statement fix only)
  Identity.Migrations.csproj                          (modified — drops Npgsql/EF-Design refs, fixes NorseRef)
  README.md                                           (rewritten)
  NorseIdentityDbContextFactory.cs                    (deleted — recreated, adapted, in .PostgreSQL below)

src/Identity/                                          (existing, table/index naming normalized — Task 3)
  NorseUser.cs                                          (modified — ToTable("Users"), HasDatabaseName calls dropped)
  NorseRole.cs                                          (modified — ToTable("Roles"), HasDatabaseName call dropped)
  NorseUserRole.cs                                      (modified — ToTable("UserRoles"))
  NorseUserClaim.cs                                     (modified — ToTable("UserClaims") added)
  NorseUserLogin.cs                                     (modified — ToTable("UserLogins") added)
  NorseUserToken.cs                                     (modified — ToTable("UserTokens") added)
  NorseRoleClaim.cs                                     (modified — ToTable("RoleClaims") added)
  NorseUserPasskey.cs                                   (modified — ToTable("UserPasskeys") added)

tests/Identity.Migrations.PostgreSQL.Tests/            (existing from Task 2, one assertion fixed — Task 3)
  NorseIdentityDbContextFactoryTests.cs                 (modified — passkey table assertion "user_passkeys")

src/Identity.Migrations.PostgreSQL/                   (new)
  NorseIdentityDbContextFactory.cs                    (new — inherits NorsePostgreSqlDesignTimeDbContextFactory<T>)
  DesignTimeServices.cs                                (new)
  Identity.Migrations.PostgreSQL.csproj                (new)
  README.md                                            (new)
  Migrations/                                          (generated in Task 5)
  schema/norse_identity.sql                            (generated in Task 5)

src/Identity.Migrations.SqlServer/                    (new)
  NorseIdentityDbContextFactory.cs                    (new — inherits NorseSqlServerDesignTimeDbContextFactory<T>)
  DesignTimeServices.cs                                (new)
  Identity.Migrations.SqlServer.csproj                 (new)
  README.md                                            (new)
  Migrations/                                          (generated in Task 5)
  schema/norse_identity.sql                            (generated in Task 5)

tests/Identity.Migrations.Tests/                      (existing, contributor test fixed)
  NorseIdentityMigrationContributorTests.cs            (modified — using statement fix only)
  NorseIdentityDbContextFactoryTests.cs                (deleted — recreated, adapted, in the two new test projects)

tests/Identity.Migrations.PostgreSQL.Tests/           (new)
  NorseIdentityDbContextFactoryTests.cs                (new — same assertions as today's, new namespace)
  Identity.Migrations.PostgreSQL.Tests.csproj          (new)

tests/Identity.Migrations.SqlServer.Tests/            (new)
  NorseIdentityDbContextFactoryTests.cs                (new — passkey test identical; naming test asserts NO snake_case)
  Identity.Migrations.SqlServer.Tests.csproj           (new)

Himinbjorg.slnx                                        (modified — four new <Project> entries)
```

---

### Task 1: Urðarbrunnr rename — `Identity.csproj`

**Files:**
- Modify: `src/Identity/Identity.csproj`

**Interfaces:**
- Consumes: `Norse.Persistence.EntityFramework` (Urðarbrunnr, `NorseRef`) — the renamed target of the previously-broken `EntityFramework` reference.
- Produces: nothing new — this task only repairs an existing reference so `Identity.csproj` builds again.

- [ ] **Step 1: Confirm the current break**

Run: `dotnet build src/Identity/Identity.csproj --nologo -v q`
Expected: `Build FAILED` — 26 errors, including `MSB9008: The referenced project /home/buvy/code/NorseArchitecture/Bifrost/Urdarbrunnr/src/EntityFramework/EntityFramework.csproj does not exist.` This confirms the starting state before the fix.

- [ ] **Step 2: Fix the `NorseRef`**

Edit `src/Identity/Identity.csproj` — change:

```xml
		<NorseRef Include="EntityFramework">
			<Repo>Urdarbrunnr</Repo>
			<Generator>true</Generator>
		</NorseRef>
```

to:

```xml
		<NorseRef Include="Persistence.EntityFramework">
			<Repo>Urdarbrunnr</Repo>
			<Generator>true</Generator>
		</NorseRef>
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/Identity/Identity.csproj --nologo -v q`
Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/Identity/Identity.csproj
git status --short
```

Confirm the status shows exactly one modified file, then commit locally (this branch is local and unpushed — Buvy reviews via the task-review gate, not a pre-commit stop):

```bash
git commit -m "$(cat <<'EOF'
Fix stale Urdarbrunnr NorseRef on Identity.csproj

Norse.EntityFramework renamed to Norse.Persistence.EntityFramework
(Urdarbrunnr PR #31). Identity.csproj's NorseRef still pointed at the old
name, breaking the build with 26 errors.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Restructure `Identity.Migrations` into the provider-agnostic base + `Identity.Migrations.PostgreSQL`

**Files:**
- Modify: `src/Identity.Migrations/Identity.Migrations.csproj`
- Modify: `src/Identity.Migrations/README.md`
- Modify: `src/Identity.Migrations/NorseIdentityMigrationContributor.cs`
- Modify: `tests/Identity.Migrations.Tests/NorseIdentityMigrationContributorTests.cs`
- Delete: `src/Identity.Migrations/NorseIdentityDbContextFactory.cs`
- Delete: `tests/Identity.Migrations.Tests/NorseIdentityDbContextFactoryTests.cs`
- Create: `src/Identity.Migrations.PostgreSQL/Identity.Migrations.PostgreSQL.csproj`
- Create: `src/Identity.Migrations.PostgreSQL/README.md`
- Create: `src/Identity.Migrations.PostgreSQL/DesignTimeServices.cs`
- Create: `src/Identity.Migrations.PostgreSQL/NorseIdentityDbContextFactory.cs`
- Create: `tests/Identity.Migrations.PostgreSQL.Tests/Identity.Migrations.PostgreSQL.Tests.csproj`
- Create: `tests/Identity.Migrations.PostgreSQL.Tests/NorseIdentityDbContextFactoryTests.cs`
- Modify: `Himinbjorg.slnx`

**Interfaces:**
- Consumes: `EfMigrationContributor<TContext>` / `MigrationConnectionStringAttribute` (Urðarbrunnr, `Norse.Persistence.EntityFramework.Design` — agnostic, no provider). `NorsePostgreSqlDesignTimeDbContextFactory<TContext>` (Urðarbrunnr, `Norse.Persistence.EntityFramework.Design.PostgreSQL`) — abstract members `DatabaseName`, `CreateContext(DbContextOptions<TContext>)`, and the `protected virtual void ConfigureOptions(DbContextOptionsBuilder<TContext>, string)` extension point.
- Produces: `Norse.Identity.Migrations.NorseIdentityMigrationContributor` (unchanged shape, fixed `using`), `Norse.Identity.Migrations.PostgreSQL.NorseIdentityDbContextFactory` — a `public sealed class`. Task 4 (`Identity.Migrations.SqlServer`) `ProjectReference`s the same slimmed base this task produces.

- [ ] **Step 1: Fix the base project's stale reference**

Edit `src/Identity.Migrations/NorseIdentityMigrationContributor.cs` — the file's only stale line is its `using`. Change:

```csharp
using Norse.EntityFramework.Migrations;
```

to:

```csharp
using Norse.Persistence.EntityFramework.Design;
```

The rest of the file (the class body, the `[MigrationConnectionString("norse_identity")]` attribute usage, `EfMigrationContributor<NorseIdentityDbContext>`) is unchanged — `Urdarbrunnr/src/EntityFramework.Migrations/` is a fully dead leftover directory (confirmed: contains only a stray `obj/Debug` folder, no `.cs` or `.csproj`); `EfMigrationContributor<TContext>` and `MigrationConnectionStringAttribute` now live in `Norse.Persistence.EntityFramework.Design`, confirmed by `grep` against `Urdarbrunnr/src/Persistence.EntityFramework.Design/`.

- [ ] **Step 2: Fix the matching test file's stale reference**

Edit `tests/Identity.Migrations.Tests/NorseIdentityMigrationContributorTests.cs` — same one-line fix. Change:

```csharp
using Norse.EntityFramework.Migrations;
```

to:

```csharp
using Norse.Persistence.EntityFramework.Design;
```

No other line in this file changes.

- [ ] **Step 3: Delete the factory and its test (recreated below, adapted)**

```bash
git rm src/Identity.Migrations/NorseIdentityDbContextFactory.cs
git rm tests/Identity.Migrations.Tests/NorseIdentityDbContextFactoryTests.cs
```

- [ ] **Step 4: Rewrite the base project's csproj**

Replace the full contents of `src/Identity.Migrations/Identity.Migrations.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Identity.Migrations: the migration contributor for NorseIdentityDbContext, provider-agnostic. Migration tooling only — never referenced from a runtime container.</Description>
		<!-- EF Core is not AOT/trim-compatible; override the src-level default. -->
		<IsAotCompatible>false</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../Identity/Identity.csproj" />
	</ItemGroup>
	<ItemGroup>
		<NorseRef Include="Persistence.EntityFramework.Design">
			<Repo>Urdarbrunnr</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

This drops the `Npgsql.EntityFrameworkCore.PostgreSQL` package (only the provider projects need a provider) and the direct `Microsoft.EntityFrameworkCore.Design` package (only needed by the factory, which now lives in the provider projects), and renames the stale `EntityFramework.Migrations` `NorseRef` to `Persistence.EntityFramework.Design`.

- [ ] **Step 5: Rewrite the base project's README**

Replace the full contents of `src/Identity.Migrations/README.md`:

```markdown
# Norse.Identity.Migrations

The migration contributor for `NorseIdentityDbContext`, provider-agnostic. Migration tooling only — never referenced from a runtime container.

Provider-specific `IDesignTimeDbContextFactory` implementations and checked-in EF migrations live in the sibling `Identity.Migrations.PostgreSQL` and `Identity.Migrations.SqlServer` projects, each of which references this one.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
```

- [ ] **Step 6: Create the PostgreSQL project's csproj**

```bash
mkdir -p src/Identity.Migrations.PostgreSQL
```

Write `src/Identity.Migrations.PostgreSQL/Identity.Migrations.PostgreSQL.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Identity.Migrations.PostgreSQL: the PostgreSQL-targeted IDesignTimeDbContextFactory and checked-in EF migrations for NorseIdentityDbContext. Migration tooling only — never referenced from a runtime container.</Description>
		<!-- EF Core is not AOT/trim-compatible; override the src-level default. -->
		<IsAotCompatible>false</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../Identity.Migrations/Identity.Migrations.csproj" />
		<NorseRef Include="Persistence.EntityFramework.Design.PostgreSQL">
			<Repo>Urdarbrunnr</Repo>
		</NorseRef>
		<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="11.*-*">
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
		</PackageReference>
		<!-- Ships the DDL scaffolder's own output inside the package DLL, so a DBA can pull the raw
		     CREATE script out of the NuGet package and run it against a blank instance without the
		     dotnet-ef CLI. Wildcard, not the literal schema/norse_identity.sql path: self-adapts if
		     the database name changes again without a csproj edit. -->
		<EmbeddedResource Include="schema/*.sql">
			<LogicalName>CreateScript.sql</LogicalName>
		</EmbeddedResource>
	</ItemGroup>
</Project>
```

- [ ] **Step 7: Create the PostgreSQL project's `DesignTimeServices`**

Write `src/Identity.Migrations.PostgreSQL/DesignTimeServices.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Norse.Persistence.EntityFramework.Design;

namespace Norse.Identity.Migrations.PostgreSQL;

/// <summary>
/// Installs <see cref="DdlEmittingMigrationsScaffolder"/> for <c>dotnet ef</c> tooling, so every
/// <c>migrations add</c>/<c>migrations remove</c> run against this project also refreshes
/// <c>schema/norse_identity.sql</c>.
/// </summary>
sealed class DesignTimeServices : IDesignTimeServices
{
	public void ConfigureDesignTimeServices(IServiceCollection services) =>
		services.AddNorseDesignTimeServices("norse_identity");
}
```

- [ ] **Step 8: Create the PostgreSQL factory, carrying forward the Version3 passkey gotcha**

Write `src/Identity.Migrations.PostgreSQL/NorseIdentityDbContextFactory.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norse.Persistence.EntityFramework.Design.PostgreSQL;

namespace Norse.Identity.Migrations.PostgreSQL;

/// <summary>
/// Design-time factory for <see cref="NorseIdentityDbContext"/>, used only by <c>dotnet ef</c> tooling
/// (e.g. <c>dotnet ef migrations add</c>) to construct a context instance outside of DI at design time.
/// </summary>
/// <remarks>
/// ASP.NET Core Identity's base <c>OnModelCreating</c> reads
/// <c>IOptions&lt;IdentityOptions&gt;.Value.Stores.SchemaVersion</c> off the context's
/// <c>ApplicationServiceProvider</c> — not the (dead, never-consulted) protected <c>SchemaVersion</c>
/// property — to decide which passkey/OpenIddict schema shape to emit. Without an application service
/// provider supplying <see cref="IdentitySchemaVersions.Version3"/>, ASP.NET Core Identity silently
/// falls back to <see cref="IdentitySchemaVersions.Version1"/> and omits the passkey table entirely.
/// </remarks>
public sealed class NorseIdentityDbContextFactory : NorsePostgreSqlDesignTimeDbContextFactory<NorseIdentityDbContext>
{
	/// <inheritdoc />
	protected override string DatabaseName => "norse_identity";

	/// <inheritdoc />
	protected override void ConfigureOptions(DbContextOptionsBuilder<NorseIdentityDbContext> builder, string connectionString)
	{
		var applicationServices = new ServiceCollection()
			.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
			.BuildServiceProvider();

		builder.UseApplicationServiceProvider(applicationServices);

		base.ConfigureOptions(builder, connectionString);
	}

	/// <inheritdoc />
	protected override NorseIdentityDbContext CreateContext(DbContextOptions<NorseIdentityDbContext> options) =>
		new(options);
}
```

`NorseIdentityDbContext` resolves unqualified: `Norse.Identity.Migrations.PostgreSQL` is nested under `Norse.Identity`, so C#'s outward namespace lookup finds it (declared in `Norse.Identity`) without a `using`, the same way it did in the old single-project factory.

- [ ] **Step 9: Write the PostgreSQL project's README**

Write `src/Identity.Migrations.PostgreSQL/README.md`:

```markdown
# Norse.Identity.Migrations.PostgreSQL

The PostgreSQL-targeted `IDesignTimeDbContextFactory` and checked-in EF migrations for `NorseIdentityDbContext`. Migration tooling only — never referenced from a runtime container.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
```

- [ ] **Step 10: Add the PostgreSQL project to the solution**

Edit `Himinbjorg.slnx` — in the `/src/` folder, immediately after the existing `Identity.Migrations` entry, add:

```xml
		<Project Path="src/Identity.Migrations.PostgreSQL/Identity.Migrations.PostgreSQL.csproj" />
```

So the `/src/` folder reads:

```xml
	<Folder Name="/src/">
		<File Path="src/Directory.Build.props" />
		<File Path="src/Directory.Build.targets" />
		<Project Path="src/Identity.Migrations/Identity.Migrations.csproj" />
		<Project Path="src/Identity.Migrations.PostgreSQL/Identity.Migrations.PostgreSQL.csproj" />
		<Project Path="src/Identity/Identity.csproj" />
		<Project Path="src/Identity.Web.Server/Identity.Web.Server.csproj" />
	</Folder>
```

- [ ] **Step 11: Build to verify**

Run: `dotnet build src/Identity.Migrations.PostgreSQL/Identity.Migrations.PostgreSQL.csproj --nologo -v q`
Expected: `Build succeeded.` with `0 Error(s)`. (`schema/` doesn't exist yet — the `EmbeddedResource` glob matching zero files is not a build error.)

Note for whoever executes this task: Himinbjörg (unlike Mímisbrunnr, the prior art this plan otherwise mirrors exactly) already has a real, checked-in `InitialCreate` migration from an earlier plan cycle — it doesn't start from zero. `git mv` it into `Migrations/` here (along with its `.Designer.cs` and the `NorseIdentityDbContextModelSnapshot.cs`), updating the `namespace` line from `Norse.Identity.Migrations.Migrations` to `Norse.Identity.Migrations.PostgreSQL.Migrations` (the only line that changes in any of the three files) — this preserves file history instead of losing it. Task 5 deletes and freshly regenerates it anyway (both because Task 3's naming changes make its DDL stale, and because a migration needs to exist for `dotnet build` to succeed once the `Npgsql` package reference is dropped from the base project two steps ago), so don't spend effort reconciling its contents — just relocate it so this step's build succeeds.

- [ ] **Step 12: Create the PostgreSQL test project**

```bash
mkdir -p tests/Identity.Migrations.PostgreSQL.Tests
```

Write `tests/Identity.Migrations.PostgreSQL.Tests/Identity.Migrations.PostgreSQL.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Identity.Migrations.PostgreSQL/Identity.Migrations.PostgreSQL.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 13: Move the factory test, adapted to the new namespace**

Write `tests/Identity.Migrations.PostgreSQL.Tests/NorseIdentityDbContextFactoryTests.cs` — identical assertions to the deleted original (Step 3), only the `namespace` line changes:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.Identity.Migrations.PostgreSQL.Tests;

public sealed class NorseIdentityDbContextFactoryTests
{
	[Fact]
	void CreateDbContext_configures_schema_version_3_with_passkeys()
	{
		NorseIdentityDbContextFactory factory = new();
		using var context = factory.CreateDbContext([]);

		var entityType = context.Model.FindEntityType(typeof(NorseUserPasskey));

		// A null-conditional chain here would let this test pass vacuously if the entity type is
		// missing entirely (exactly what happens when SchemaVersion silently falls back to Version1
		// and Ignore<TUserPasskey>() strips it from the model) — assert not-null first.
		entityType.ShouldNotBeNull();
		entityType.GetTableName().ShouldBe("AspNetUserPasskeys");
	}

	[Fact]
	void CreateDbContext_applies_snake_case_naming()
	{
		NorseIdentityDbContextFactory factory = new();
		using var context = factory.CreateDbContext([]);

		var entityType = context.Model.FindEntityType(typeof(NorseOpenIddictApplication));

		entityType.ShouldNotBeNull();
		entityType.FindProperty(nameof(NorseOpenIddictApplication.ClientId))!.GetColumnName().ShouldBe("client_id");
	}
}
```

- [ ] **Step 14: Add the PostgreSQL test project to the solution**

Edit `Himinbjorg.slnx` — in the `/tests/` folder, immediately after the existing `Identity.Migrations.Tests` entry, add:

```xml
		<Project Path="tests/Identity.Migrations.PostgreSQL.Tests/Identity.Migrations.PostgreSQL.Tests.csproj" />
```

So the `/tests/` folder reads:

```xml
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<File Path="tests/Directory.Build.targets" />
		<Project Path="tests/Identity.Migrations.Tests/Identity.Migrations.Tests.csproj" />
		<Project Path="tests/Identity.Migrations.PostgreSQL.Tests/Identity.Migrations.PostgreSQL.Tests.csproj" />
		<Project Path="tests/Identity.Tests/Identity.Tests.csproj" />
		<Project Path="tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj" />
	</Folder>
```

- [ ] **Step 15: Run both migrations test suites to verify**

Run: `dotnet test tests/Identity.Migrations.Tests/Identity.Migrations.Tests.csproj --nologo`
Expected: all tests pass (the two contributor tests, now building against the fixed `using`).

Run: `dotnet test tests/Identity.Migrations.PostgreSQL.Tests/Identity.Migrations.PostgreSQL.Tests.csproj --nologo`
Expected: both tests pass — `context.Model` builds the EF model from entity configuration alone, so these assertions hold with no live database and no migration files on disk yet.

- [ ] **Step 16: Commit**

```bash
git add src/Identity.Migrations src/Identity.Migrations.PostgreSQL tests/Identity.Migrations.Tests tests/Identity.Migrations.PostgreSQL.Tests Himinbjorg.slnx
git status --short
```

Confirm the status shows the base project's csproj/README/contributor as modified, the factory and its test as deleted, the two new projects as added, and the slnx change — then commit locally:

```bash
git commit -m "$(cat <<'EOF'
Split Identity.Migrations into provider-agnostic base + PostgreSQL

Mirrors Mimisbrunnr's shipped Reference.Data.Migrations* split. The base
project keeps only the migration contributor; NorseIdentityDbContextFactory
moves into the new Identity.Migrations.PostgreSQL project, inheriting
NorsePostgreSqlDesignTimeDbContextFactory<T> and carrying forward the
Version3 passkey ConfigureOptions override. Also fixes the base project's
stale Norse.EntityFramework.Migrations NorseRef/using (renamed to
Norse.Persistence.EntityFramework.Design).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Normalize ASP.NET Identity table/index naming (strip `AspNet` prefix, provider-aware casing)

**Why this task exists:** discovered live, not in the original spec. Every core ASP.NET Identity entity either inherits ASP.NET's own `AspNet`-prefixed default table name (`NorseUserClaim`, `NorseUserLogin`, `NorseUserToken`, `NorseRoleClaim`, `NorseUserPasskey` — none of these have an explicit `ToTable(...)` call today) or hardcodes a lowercase snake_case name directly (`NorseUser` → `"users"`, `NorseRole` → `"roles"`, `NorseUserRole` → `"user_roles"`). Both are wrong for a dual-provider realm: the `AspNet`-prefixed defaults survive untouched on SQL Server (default `UseSnakeCaseNaming => false`) and get mechanically mangled to `asp_net_user_passkeys`-style names on Postgres (the convention has no `AspNet`-prefix carve-out); the hardcoded-lowercase entities bake Postgres-style casing onto SQL Server too, since an explicit name isn't touched when the convention doesn't run. Same problem for the two entities with explicit `.HasIndex(...).HasDatabaseName(...)` calls (`NorseUser`, `NorseRole`) — hardcoded lowercase index names.

The fix: every entity gets an explicit `AspNet`-free PascalCase `ToTable(...)` name, and the two explicit `HasDatabaseName(...)` calls are dropped entirely, letting EF Core's own default `IX_{Table}_{Property}` naming take over. From there, each provider's own convention does the casing — nothing else changes:
- **Postgres** (`UseSnakeCaseNaming => true`, the default): `NorseSnakeCaseNamingConvention` rewrites `"Users"` → `"users"`, `"UserPasskeys"` → `"user_passkeys"`, `"IX_Users_NormalizedEmail"` → `"ix_users_normalized_email"`, etc. — verified by hand-tracing `SnakeCaseNameRewriter.RewriteName` against every one of these strings; the three entities that already had explicit lowercase names produce byte-identical output to before this task, and the five that didn't now produce the same clean pattern instead of an `asp_net_`-prefixed one.
- **SQL Server** (`UseSnakeCaseNaming => false`, the default): nothing rewrites, so the canonical PascalCase name — `AspNet`-free — is exactly what lands, e.g. `UserPasskeys`, `IX_Users_NormalizedEmail`.

OpenIddict's own wrapper entities (`NorseOpenIddictApplication`/`Authorization`/`Scope`/`Token`) are explicitly out of scope — none of them have an `AspNet` prefix to strip (OpenIddict's own default naming doesn't use one), and Buvy's instruction was specific to "the ASP.NET identity stack."

**Files:**
- Modify: `src/Identity/NorseUser.cs`
- Modify: `src/Identity/NorseRole.cs`
- Modify: `src/Identity/NorseUserRole.cs`
- Modify: `src/Identity/NorseUserClaim.cs`
- Modify: `src/Identity/NorseUserLogin.cs`
- Modify: `src/Identity/NorseUserToken.cs`
- Modify: `src/Identity/NorseRoleClaim.cs`
- Modify: `src/Identity/NorseUserPasskey.cs`
- Modify: `tests/Identity.Migrations.PostgreSQL.Tests/NorseIdentityDbContextFactoryTests.cs` (Task 2's moved passkey test — its expected table name changes from the old, now-provably-wrong `"AspNetUserPasskeys"` to the correct post-fix Postgres output, `"user_passkeys"`)

**Interfaces:**
- Consumes: nothing new — every entity's `Configure(EntityTypeBuilder<T>)` static method already exists (the platform's colocated-configuration pattern); this task only edits table/index-naming calls inside bodies that already exist.
- Produces: the corrected canonical table names every later task's generated migration DDL (Task 5) must reflect — `Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `UserTokens`, `RoleClaims`, `UserPasskeys`.

- [ ] **Step 1: Fix `NorseUser.cs`**

Edit `src/Identity/NorseUser.cs` — change:

```csharp
		builder.ToTable("users");
```

to:

```csharp
		builder.ToTable("Users");
```

And change:

```csharp
		builder.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ix_users_normalized_email");
		builder.HasIndex(u => u.NormalizedUserName).IsUnique().HasDatabaseName("ix_users_normalized_user_name");
```

to:

```csharp
		builder.HasIndex(u => u.NormalizedEmail);
		builder.HasIndex(u => u.NormalizedUserName).IsUnique();
```

Nothing else in this file changes — the `ConcurrencyStamp`/`SecurityStamp`/`PasswordHash`/`PhoneNumber` property configuration and the four `HasMany(...)` relationship lines are untouched.

- [ ] **Step 2: Fix `NorseRole.cs`**

Edit `src/Identity/NorseRole.cs` — change:

```csharp
		builder.ToTable("roles");
```

to:

```csharp
		builder.ToTable("Roles");
```

And change:

```csharp
		builder.HasIndex(r => r.NormalizedName).IsUnique().HasDatabaseName("ix_roles_normalized_name");
```

to:

```csharp
		builder.HasIndex(r => r.NormalizedName).IsUnique();
```

Nothing else in this file changes.

- [ ] **Step 3: Fix `NorseUserRole.cs`**

Edit `src/Identity/NorseUserRole.cs` — change:

```csharp
		builder.ToTable("user_roles");
```

to:

```csharp
		builder.ToTable("UserRoles");
```

Nothing else in this file changes.

- [ ] **Step 4: Fix `NorseUserClaim.cs`**

Edit `src/Identity/NorseUserClaim.cs` — add a `ToTable` call as the first line of `Configure`:

```csharp
	public static void Configure(EntityTypeBuilder<NorseUserClaim> builder)
	{
		builder.ToTable("UserClaims");
		// Contrary to Task 9's assumption, IdentityDbContext's own base OnModelCreating leaves
```

(the rest of the method — the `ClaimType`/`ClaimValue` length configuration and its explanatory comment — is unchanged, just pushed down by one line).

- [ ] **Step 5: Fix `NorseUserLogin.cs`**

Edit `src/Identity/NorseUserLogin.cs` — add a `ToTable` call as the first line of `Configure`:

```csharp
	public static void Configure(EntityTypeBuilder<NorseUserLogin> builder)
	{
		builder.ToTable("UserLogins");
		builder.Property(l => l.LoginProvider).HasMaxLength(128);
```

Nothing else in this file changes.

- [ ] **Step 6: Fix `NorseUserToken.cs`**

Edit `src/Identity/NorseUserToken.cs` — add a `ToTable` call as the first line of `Configure`:

```csharp
	public static void Configure(EntityTypeBuilder<NorseUserToken> builder)
	{
		builder.ToTable("UserTokens");
		builder.Property(t => t.LoginProvider).HasMaxLength(128);
```

Nothing else in this file changes.

- [ ] **Step 7: Fix `NorseRoleClaim.cs`**

Edit `src/Identity/NorseRoleClaim.cs` — add a `ToTable` call as the first line of `Configure`:

```csharp
	public static void Configure(EntityTypeBuilder<NorseRoleClaim> builder)
	{
		builder.ToTable("RoleClaims");
		// Contrary to Task 9's assumption, IdentityDbContext's own base OnModelCreating leaves
```

(the rest of the method is unchanged, just pushed down by one line).

- [ ] **Step 8: Fix `NorseUserPasskey.cs`**

Edit `src/Identity/NorseUserPasskey.cs` — add a `ToTable` call as the first line of `Configure`:

```csharp
	public static void Configure(EntityTypeBuilder<NorseUserPasskey> builder)
	{
		builder.ToTable("UserPasskeys");
		builder.HasKey(p => p.CredentialId);
		builder.OwnsOne(p => p.Data, o => o.ToJson());
	}
```

- [ ] **Step 9: Fix the already-committed PostgreSQL factory test's now-provably-wrong assertion**

Edit `tests/Identity.Migrations.PostgreSQL.Tests/NorseIdentityDbContextFactoryTests.cs` — change:

```csharp
		entityType.GetTableName().ShouldBe("AspNetUserPasskeys");
```

to:

```csharp
		entityType.GetTableName().ShouldBe("user_passkeys");
```

This test was already failing before this task (Task 2's own report flagged it as a known pre-existing defect, not fixed there because it required this exact naming decision) — this step is the fix that decision unblocks.

- [ ] **Step 10: Build to verify**

Run: `dotnet build Himinbjorg.slnx --nologo -v q`
Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 11: Run the PostgreSQL migrations test suite to verify**

Run: `dotnet test tests/Identity.Migrations.PostgreSQL.Tests/Identity.Migrations.PostgreSQL.Tests.csproj --nologo` (if the MTP handshake issue from Task 2's report recurs in this environment, build the project and run the resulting test `.dll` directly via `dotnet exec` instead, exactly as Task 2's report describes working around it).
Expected: both tests pass now — `CreateDbContext_configures_schema_version_3_with_passkeys` (now asserting `"user_passkeys"`) and `CreateDbContext_applies_snake_case_naming`.

- [ ] **Step 12: Commit**

```bash
git add src/Identity tests/Identity.Migrations.PostgreSQL.Tests
git status --short
```

Confirm the status shows exactly the 8 entity files plus the one test file, then commit locally:

```bash
git commit -m "$(cat <<'EOF'
Normalize ASP.NET Identity table/index naming across providers

Every core Identity entity gets an explicit AspNet-free PascalCase
ToTable(...) name at the canonical (provider-agnostic) layer instead of
either inheriting ASP.NET's own AspNet-prefixed default or hardcoding
Postgres-style lowercase directly. Explicit HasDatabaseName(...) index
overrides are dropped in favor of EF's own default IX_{Table}_{Property}
naming. Each provider's own convention now does the casing correctly:
Postgres's NorseSnakeCaseNamingConvention rewrites to snake_case (byte-
identical output to before this change for the three entities that
already had explicit names), SQL Server's default (no conversion) leaves
it PascalCase and AspNet-free. Fixes the passkey table naming drift Task 2
found and flagged but couldn't fix without this naming decision.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Scaffold `Identity.Migrations.SqlServer`

**Files:**
- Create: `src/Identity.Migrations.SqlServer/Identity.Migrations.SqlServer.csproj`
- Create: `src/Identity.Migrations.SqlServer/README.md`
- Create: `src/Identity.Migrations.SqlServer/DesignTimeServices.cs`
- Create: `src/Identity.Migrations.SqlServer/NorseIdentityDbContextFactory.cs`
- Create: `tests/Identity.Migrations.SqlServer.Tests/Identity.Migrations.SqlServer.Tests.csproj`
- Create: `tests/Identity.Migrations.SqlServer.Tests/NorseIdentityDbContextFactoryTests.cs`
- Modify: `Himinbjorg.slnx`

**Interfaces:**
- Consumes: `Identity.Migrations.csproj` (Task 2's final project shape, `ProjectReference`; its entity configuration was further updated by Task 3's naming normalization, but that doesn't change this task's own interface with it), `NorseSqlServerDesignTimeDbContextFactory<TContext>` (Urðarbrunnr, `Norse.Persistence.EntityFramework.Design.SqlServer` — same abstract-member shape as the PostgreSQL base class, plus the identical `ConfigureOptions` extension point; its own default is `UseSnakeCaseNaming => false`, confirmed by reading the class directly).
- Produces: `Norse.Identity.Migrations.SqlServer.NorseIdentityDbContextFactory`, a `public sealed class`.

- [ ] **Step 1: Create the project directory and csproj**

```bash
mkdir -p src/Identity.Migrations.SqlServer
```

Write `src/Identity.Migrations.SqlServer/Identity.Migrations.SqlServer.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Identity.Migrations.SqlServer: the SQL Server-targeted IDesignTimeDbContextFactory and checked-in EF migrations for NorseIdentityDbContext. Migration tooling only — never referenced from a runtime container.</Description>
		<!-- EF Core is not AOT/trim-compatible; override the src-level default. -->
		<IsAotCompatible>false</IsAotCompatible>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../Identity.Migrations/Identity.Migrations.csproj" />
		<NorseRef Include="Persistence.EntityFramework.Design.SqlServer">
			<Repo>Urdarbrunnr</Repo>
		</NorseRef>
		<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="11.*-*">
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
		</PackageReference>
		<!-- Ships the DDL scaffolder's own output inside the package DLL, so a DBA can pull the raw
		     CREATE script out of the NuGet package and run it against a blank instance without the
		     dotnet-ef CLI. Wildcard, not the literal schema/norse_identity.sql path: self-adapts if
		     the database name changes again without a csproj edit. -->
		<EmbeddedResource Include="schema/*.sql">
			<LogicalName>CreateScript.sql</LogicalName>
		</EmbeddedResource>
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Create the SqlServer project's `DesignTimeServices`**

Write `src/Identity.Migrations.SqlServer/DesignTimeServices.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Norse.Persistence.EntityFramework.Design;

namespace Norse.Identity.Migrations.SqlServer;

/// <summary>
/// Installs <see cref="DdlEmittingMigrationsScaffolder"/> for <c>dotnet ef</c> tooling, so every
/// <c>migrations add</c>/<c>migrations remove</c> run against this project also refreshes
/// <c>schema/norse_identity.sql</c>.
/// </summary>
sealed class DesignTimeServices : IDesignTimeServices
{
	public void ConfigureDesignTimeServices(IServiceCollection services) =>
		services.AddNorseDesignTimeServices("norse_identity");
}
```

- [ ] **Step 3: Create the SqlServer factory, carrying forward the Version3 passkey gotcha**

Write `src/Identity.Migrations.SqlServer/NorseIdentityDbContextFactory.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norse.Persistence.EntityFramework.Design.SqlServer;

namespace Norse.Identity.Migrations.SqlServer;

/// <summary>
/// Design-time factory for <see cref="NorseIdentityDbContext"/>, used only by <c>dotnet ef</c> tooling
/// (e.g. <c>dotnet ef migrations add</c>) to construct a context instance outside of DI at design time.
/// </summary>
/// <remarks>
/// Same ASP.NET Core Identity <c>SchemaVersion</c> gotcha as the PostgreSQL factory — see that type's
/// doc comment for the full explanation. Provider-independent: the fallback to
/// <see cref="IdentitySchemaVersions.Version1"/> happens in Identity's own model-building code, not
/// anything provider-specific.
/// </remarks>
public sealed class NorseIdentityDbContextFactory : NorseSqlServerDesignTimeDbContextFactory<NorseIdentityDbContext>
{
	/// <inheritdoc />
	protected override string DatabaseName => "norse_identity";

	/// <inheritdoc />
	protected override void ConfigureOptions(DbContextOptionsBuilder<NorseIdentityDbContext> builder, string connectionString)
	{
		var applicationServices = new ServiceCollection()
			.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
			.BuildServiceProvider();

		builder.UseApplicationServiceProvider(applicationServices);

		base.ConfigureOptions(builder, connectionString);
	}

	/// <inheritdoc />
	protected override NorseIdentityDbContext CreateContext(DbContextOptions<NorseIdentityDbContext> options) =>
		new(options);
}
```

- [ ] **Step 4: Write the SqlServer project's README**

Write `src/Identity.Migrations.SqlServer/README.md`:

```markdown
# Norse.Identity.Migrations.SqlServer

The SQL Server-targeted `IDesignTimeDbContextFactory` and checked-in EF migrations for `NorseIdentityDbContext`. Migration tooling only — never referenced from a runtime container.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
```

- [ ] **Step 5: Add the SqlServer project to the solution**

Edit `Himinbjorg.slnx` — in the `/src/` folder, immediately after the `Identity.Migrations.PostgreSQL` entry added in Task 2, add:

```xml
		<Project Path="src/Identity.Migrations.SqlServer/Identity.Migrations.SqlServer.csproj" />
```

So the `/src/` folder reads:

```xml
	<Folder Name="/src/">
		<File Path="src/Directory.Build.props" />
		<File Path="src/Directory.Build.targets" />
		<Project Path="src/Identity.Migrations/Identity.Migrations.csproj" />
		<Project Path="src/Identity.Migrations.PostgreSQL/Identity.Migrations.PostgreSQL.csproj" />
		<Project Path="src/Identity.Migrations.SqlServer/Identity.Migrations.SqlServer.csproj" />
		<Project Path="src/Identity/Identity.csproj" />
		<Project Path="src/Identity.Web.Server/Identity.Web.Server.csproj" />
	</Folder>
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build src/Identity.Migrations.SqlServer/Identity.Migrations.SqlServer.csproj --nologo -v q`
Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 7: Create the SqlServer test project**

```bash
mkdir -p tests/Identity.Migrations.SqlServer.Tests
```

Write `tests/Identity.Migrations.SqlServer.Tests/Identity.Migrations.SqlServer.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Identity.Migrations.SqlServer/Identity.Migrations.SqlServer.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 8: Write the SqlServer factory test, adapted for SqlServer's own naming default**

Write `tests/Identity.Migrations.SqlServer.Tests/NorseIdentityDbContextFactoryTests.cs`. The passkey test's `Version3` gotcha check is provider-independent, but its expected table name is **not** copied from the PostgreSQL test: Task 3 gave every core Identity entity an explicit `AspNet`-free PascalCase `ToTable(...)` name, and `NorseSqlServerDesignTimeDbContextFactory`'s own `UseSnakeCaseNaming` default is `false` (confirmed by reading the base class — SQL Server's case-insensitive collation round-trips PascalCase fine, unlike Postgres), so this factory does not snake_case anything — the canonical `"UserPasskeys"` name survives untouched. The naming test asserts that same non-conversion behavior on a different entity:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.Identity.Migrations.SqlServer.Tests;

public sealed class NorseIdentityDbContextFactoryTests
{
	[Fact]
	void CreateDbContext_configures_schema_version_3_with_passkeys()
	{
		NorseIdentityDbContextFactory factory = new();
		using var context = factory.CreateDbContext([]);

		var entityType = context.Model.FindEntityType(typeof(NorseUserPasskey));

		entityType.ShouldNotBeNull();
		entityType.GetTableName().ShouldBe("UserPasskeys");
	}

	[Fact]
	void CreateDbContext_does_not_apply_snake_case_naming()
	{
		NorseIdentityDbContextFactory factory = new();
		using var context = factory.CreateDbContext([]);

		var entityType = context.Model.FindEntityType(typeof(NorseOpenIddictApplication));

		entityType.ShouldNotBeNull();
		entityType.FindProperty(nameof(NorseOpenIddictApplication.ClientId))!.GetColumnName().ShouldBe("ClientId");
	}
}
```

- [ ] **Step 9: Add the SqlServer test project to the solution**

Edit `Himinbjorg.slnx` — in the `/tests/` folder, immediately after the `Identity.Migrations.PostgreSQL.Tests` entry added in Task 2, add:

```xml
		<Project Path="tests/Identity.Migrations.SqlServer.Tests/Identity.Migrations.SqlServer.Tests.csproj" />
```

So the `/tests/` folder reads:

```xml
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<File Path="tests/Directory.Build.targets" />
		<Project Path="tests/Identity.Migrations.Tests/Identity.Migrations.Tests.csproj" />
		<Project Path="tests/Identity.Migrations.PostgreSQL.Tests/Identity.Migrations.PostgreSQL.Tests.csproj" />
		<Project Path="tests/Identity.Migrations.SqlServer.Tests/Identity.Migrations.SqlServer.Tests.csproj" />
		<Project Path="tests/Identity.Tests/Identity.Tests.csproj" />
		<Project Path="tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj" />
	</Folder>
```

- [ ] **Step 10: Run the SqlServer test suite to verify**

Run: `dotnet test tests/Identity.Migrations.SqlServer.Tests/Identity.Migrations.SqlServer.Tests.csproj --nologo`
Expected: both tests pass.

- [ ] **Step 11: Commit**

```bash
git add src/Identity.Migrations.SqlServer tests/Identity.Migrations.SqlServer.Tests Himinbjorg.slnx
git status --short
```

Commit locally:

```bash
git commit -m "$(cat <<'EOF'
Scaffold Identity.Migrations.SqlServer

Mirrors Identity.Migrations.PostgreSQL's shape: NorseIdentityDbContextFactory
inherits NorseSqlServerDesignTimeDbContextFactory<T>, same Version3 passkey
ConfigureOptions override (provider-independent — the fallback lives in
Identity's own model-building code). Test suite asserts SqlServer's actual
default (no snake_case naming), not a copy of the PostgreSQL assertions.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Regenerate migrations for both providers

**Files:**
- Create: `src/Identity.Migrations.PostgreSQL/Migrations/*.cs` (generated fresh)
- Create: `src/Identity.Migrations.PostgreSQL/schema/norse_identity.sql` (generated)
- Create: `src/Identity.Migrations.SqlServer/Migrations/*.cs` (generated)
- Create: `src/Identity.Migrations.SqlServer/schema/norse_identity.sql` (generated)

**Interfaces:**
- Consumes: `NorseIdentityDbContextFactory` from both Task 2 and Task 4 (`dotnet ef` locates each project's `IDesignTimeDbContextFactory<T>` via reflection — no `--startup-project` flag needed, matching the existing single-provider factory's usage today).
- Produces: nothing consumed by a later task in this plan — this is the last piece of new content before verification.

**Precondition (Buvy's own action, not this task's job):** the original plan assumed no migration existed yet when Task 2 ran (true for Mímisbrunnr, the prior art this plan mirrors — false for Himinbjörg, which already had a real `InitialCreate` migration from an earlier, unrelated plan cycle). Task 2's implementer `git mv`'d that existing migration into `Identity.Migrations.PostgreSQL/Migrations/` rather than losing it. Buvy is deleting those carried-over files himself before this task starts — do not perform that deletion as part of this task. Step 1 below starts with a check that the precondition actually holds; if it doesn't, stop and report NEEDS_CONTEXT rather than deleting the files yourself.

- [ ] **Step 1: Confirm the precondition, then generate the PostgreSQL migration**

```bash
ls src/Identity.Migrations.PostgreSQL/Migrations 2>/dev/null && echo "STOP: Migrations/ still exists — do not proceed, report NEEDS_CONTEXT" || echo "confirmed empty, proceeding"
```

Run: `dotnet ef migrations add InitialCreate --project src/Identity.Migrations.PostgreSQL/Identity.Migrations.PostgreSQL.csproj`
Expected: `Done.` — creates `src/Identity.Migrations.PostgreSQL/Migrations/{timestamp}_InitialCreate.cs`, `{timestamp}_InitialCreate.Designer.cs`, and `NorseIdentityDbContextModelSnapshot.cs`. `DesignTimeServices` (Task 2, Step 7) also emits `src/Identity.Migrations.PostgreSQL/schema/norse_identity.sql`.

- [ ] **Step 2: Generate the SqlServer migration**

Run: `dotnet ef migrations add InitialCreate --project src/Identity.Migrations.SqlServer/Identity.Migrations.SqlServer.csproj`
Expected: `Done.` — same three-file shape under `src/Identity.Migrations.SqlServer/Migrations/`, plus `src/Identity.Migrations.SqlServer/schema/norse_identity.sql`.

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build Himinbjorg.slnx --nologo -v q`
Expected: `Build succeeded.` with `0 Error(s)` across all seven projects (`Identity`, `Identity.Migrations`, `Identity.Migrations.PostgreSQL`, `Identity.Migrations.SqlServer`, `Identity.Web.Server`, and their four test projects).

- [ ] **Step 4: Human SQL review checkpoint — do not proceed to Task 6 without this**

Stop here. Buvy reviews the generated `schema/norse_identity.sql` on both sides (`Identity.Migrations.PostgreSQL/schema/` and `Identity.Migrations.SqlServer/schema/`) and the generated migration C# files, and confirms the schema shape is correct for both providers — this is the explicit checkpoint called out in the spec ("Buvy reviews the generated SQL on both sides before this ships"). Confirm in particular that the Task 3 naming changes (no `AspNet` prefix, `Users`/`Roles`/`UserRoles`/`UserClaims`/`UserLogins`/`UserTokens`/`RoleClaims`/`UserPasskeys` on SQL Server, `users`/`roles`/`user_roles`/etc. on Postgres) came through correctly in both generated schemas. Do not move to Task 6 until that review is done.

- [ ] **Step 5: Commit**

```bash
git add src/Identity.Migrations.PostgreSQL/Migrations src/Identity.Migrations.PostgreSQL/schema src/Identity.Migrations.SqlServer/Migrations src/Identity.Migrations.SqlServer/schema
git status --short
```

Commit locally (only after Step 4's human review checkpoint is cleared):

```bash
git commit -m "$(cat <<'EOF'
Regenerate InitialCreate migrations for both providers

Fresh InitialCreate for Identity.Migrations.PostgreSQL and
Identity.Migrations.SqlServer, reviewed against the generated schema/*.sql
DDL on both sides. Supersedes the old single-provider (Postgres-only)
migration removed in the Identity.Migrations restructuring.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Full-solution verification and `IDeferredSignIn` checkpoint

**Files:** none modified — verification only.

**Interfaces:**
- Consumes: everything produced by Tasks 1–4, plus the already-correct `NorseSignInManager.cs` (unmodified by this plan, per the Global Constraints note).
- Produces: nothing — this task's deliverable is a green build/test run across the whole repo, confirming the branch is ready for the ship gate.

- [ ] **Step 1: Full solution build**

Run: `dotnet build Himinbjorg.slnx --nologo -v q`
Expected: `Build succeeded.` with `0 Error(s)`, `0 Warning(s)` beyond any pre-existing ones unrelated to this work (this repo treats warnings as errors platform-wide, so any real regression fails this step outright rather than showing as a warning).

- [ ] **Step 2: Full solution test run**

Run: `dotnet test Himinbjorg.slnx --nologo`
Expected: all test projects pass — `Identity.Tests`, `Identity.Migrations.Tests`, `Identity.Migrations.PostgreSQL.Tests`, `Identity.Migrations.SqlServer.Tests`, `Identity.Web.Server.Tests`.

- [ ] **Step 3: Confirm `IDeferredSignIn` usage is intact**

Run: `grep -n "IDeferredSignIn" src/Identity.Web.Server/NorseSignInManager.cs`
Expected: the same five matches present before this plan started (the `using Norse.Abstractions.Web.Server.DeferredSignIn;` line plus four doc-comment/parameter references) — confirming Step 1's green build proves this file still compiles against Asgard's `IDeferredSignIn` contract with no code change needed here.

- [ ] **Step 4: Confirm no stale references remain**

Run: `grep -rn "NorseRef Include=\"EntityFramework\"" --include=*.csproj . ; grep -rn "NorseRef Include=\"EntityFramework.Migrations\"" --include=*.csproj . ; grep -rln "Norse.EntityFramework" --include=*.cs .`
Expected: no matches anywhere in the repo. (This intentionally does not check Yggdrasil — that repo's identical stale references are the explicitly out-of-scope follow-up.)

---

### Task 7: Fix the version-skew bug behind the two known test failures

**Why this task exists:** Task 6 found and accepted two pre-existing test failures (`Identity.Tests` 5/30, `Identity.Web.Server.Tests` 4/20, both `System.MissingMethodException: Method not found: 'Void Microsoft.EntityFrameworkCore.Storage.JsonTypeMapping..ctor(...)'`) as out-of-scope, confirmed unrelated to Tasks 1-5 via baseline comparison. Buvy corrected that framing live: "out of scope" meant not touching Yggdrasil, not leaving broken tests on a branch headed for master. He pointed at the root cause directly — several EF-provider `PackageReference`s in this repo still float on a bare `Version="*"` instead of the platform's `11.*-*` major-version-floating convention (the same convention Task 1 already applied to the two `Identity.Migrations.{PostgreSQL,SqlServer}.csproj` files, just missed on these). A bare `*` can resolve a provider package (Npgsql, Sqlite) to a different major version than the EF11-preview core packages it needs to binary-match against — `JsonTypeMapping`'s constructor signature changed between preview builds, and a mismatched provider assembly throws exactly this `MissingMethodException` at runtime. Confirmed against Mímisbrunnr's own `Npgsql.EntityFrameworkCore.PostgreSQL` reference, already correctly pinned `11.*-*`.

**Files:**
- Modify: `src/Identity/Identity.csproj` (`Microsoft.AspNetCore.Identity.EntityFrameworkCore`)
- Modify: `src/Identity.Web.Server/Identity.Web.Server.csproj` (`Npgsql.EntityFrameworkCore.PostgreSQL`)
- Modify: `tests/Identity.Tests/Identity.Tests.csproj` (`Microsoft.EntityFrameworkCore.Sqlite`, `Npgsql.EntityFrameworkCore.PostgreSQL`)
- Modify: `tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj` (`Microsoft.EntityFrameworkCore.Sqlite`)

**Interfaces:**
- Consumes: nothing new — this only tightens existing `PackageReference` version constraints.
- Produces: a full solution where `Identity.Tests` and `Identity.Web.Server.Tests` both pass 100%, not just build clean.

- [ ] **Step 1: Confirm the current failure state**

Run: `dotnet build tests/Identity.Tests/Identity.Tests.csproj --nologo -v q && dotnet exec tests/Identity.Tests/bin/Debug/net11.0/Norse.Identity.Tests.dll`
Expected: 25/30 pass, 5 failures, all `System.MissingMethodException` on `JsonTypeMapping..ctor`.

- [ ] **Step 2: Fix the four bare-`*` EF-provider package references**

Edit each of the four files listed above — change `Version="*"` to `Version="11.*-*"` on exactly the one EF-provider `PackageReference` line named per file above. Do not touch any other `PackageReference` in these files (e.g. `OpenIddict.EntityFrameworkCore` in `Identity.csproj` stays on its own independent versioning scheme — OpenIddict does not track EF Core's major version number, so `11.*-*` would be wrong there, not a fix).

- [ ] **Step 3: Rebuild and rerun both previously-failing test projects**

Run: `dotnet build Himinbjorg.slnx --nologo -v q`
Expected: `Build succeeded.` with `0 Error(s)`.

Run: `dotnet exec tests/Identity.Tests/bin/Debug/net11.0/Norse.Identity.Tests.dll`
Expected: `30/30` pass — all 5 previously-failing tests now pass, nothing new fails.

Run: `dotnet exec tests/Identity.Web.Server.Tests/bin/Debug/net11.0/Norse.Identity.Web.Server.Tests.dll`
Expected: `20/20` pass — all 4 previously-failing tests now pass, nothing new fails.

If either project still shows failures after this fix, stop and report — that would mean the version skew has a different or additional cause than the bare-`*` package refs, and needs fresh diagnosis rather than a second guess at the same fix.

- [ ] **Step 4: Full-solution re-verification**

Run: `dotnet exec tests/Identity.Migrations.Tests/bin/Debug/net11.0/Norse.Identity.Migrations.Tests.dll`, then the same for `Identity.Migrations.PostgreSQL.Tests` and `Identity.Migrations.SqlServer.Tests` — confirm all three are still green (this fix shouldn't touch them, but confirm nothing regressed).

- [ ] **Step 5: Commit**

```bash
git add src/Identity/Identity.csproj src/Identity.Web.Server/Identity.Web.Server.csproj tests/Identity.Tests/Identity.Tests.csproj tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj
git status --short
```

Confirm the status shows exactly these four files, then commit locally:

```bash
git commit -m "$(cat <<'EOF'
Pin EF-provider packages to 11.*-* to fix JsonTypeMapping version skew

Four PackageReferences still floated on a bare Version="*" instead of
this platform's major-version-floating convention (Microsoft.
AspNetCore.Identity.EntityFrameworkCore, Npgsql.EntityFrameworkCore.
PostgreSQL x2, Microsoft.EntityFrameworkCore.Sqlite x2) -- the same
convention Task 1 already applied to both Identity.Migrations.*.csproj
files, just missed here. A bare * could resolve a provider package to a
different major version than the EF11-preview core packages it needs to
binary-match, and JsonTypeMapping's constructor signature changed between
preview builds -- a mismatched provider assembly threw
MissingMethodException on every touch of Model in Identity.Tests (5
failures) and Identity.Web.Server.Tests (4 failures). Confirmed against
Mimisbrunnr's own already-correct Npgsql.EntityFrameworkCore.PostgreSQL
reference, pinned 11.*-* since it was authored.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Ship-gate handoff

No code changes — this task is a checklist, not an implementation step, matching the ship-gate discipline this platform has followed since the original migrations framework rollout.

- [ ] **Step 1: Confirm every prior task's changes are staged, not committed**

Run: `git status --short`
Expected: every file touched across Tasks 1–5 shows as staged (or, if the human already committed incrementally after each task's hand-off point, `git log --oneline -6` shows one commit per task). Either is fine — this plan never commits or pushes on its own.

- [ ] **Step 2: Hand off to Buvy for the ship gate**

The remaining steps are Buvy's, not this plan's: review and commit (if not already done per-task), open the PR against `master`, confirm CI is green, tag the release, and confirm all Himinbjörg packages (`Norse.Identity`, `Norse.Identity.Migrations`, `Norse.Identity.Migrations.PostgreSQL`, `Norse.Identity.Migrations.SqlServer`, `Norse.Identity.Web.Server`) publish to NuGet — same sequencing as every prior realm's ship gate in this framework rollout.

---

## Self-Review

**Spec coverage:** Task 1 → spec's "Urðarbrunnr Rename Uptake." Tasks 2, 4, 5 → spec's "Migrations Trio Breakout" (project shape table, package versioning convention, migration regeneration, test reorg — all four spec subsections have a matching task/step). Task 3 (ASP.NET Identity table/index naming normalization) has no matching spec section — it wasn't part of the original spec, it was added live during execution per Buvy's direct instruction (strip the `AspNet` prefix, PascalCase canonical, provider-specific casing via the existing snake_case convention's on/off switch); recorded as a spec addendum rather than a full spec rewrite, consistent with how this platform documents mid-flight decisions elsewhere (e.g. Urðarbrunnr's own CLAUDE.md "hardened … against two real bugs found downstream" pattern). Task 6 → spec's "IDeferredSignIn (verification only)" and the "Dependency Graph" confirmation. Task 7 → spec's "Completion criterion" and ship-gate note. No original spec section lacks a task.

**Placeholder scan:** no TBD/TODO; every step shows complete file contents or exact commands with expected output; no step says "similar to Task N" without repeating the actual code.

**Type consistency:** `NorseIdentityDbContextFactory` (PostgreSQL, Task 2) and `NorseIdentityDbContextFactory` (SqlServer, Task 4) are same-named types in different namespaces/assemblies, matching the existing single-project factory's name — verified no collision since Task 4's project never references Task 2's PostgreSQL project (both independently reference only the base). `DatabaseName => "norse_identity"` and `CreateContext(DbContextOptions<NorseIdentityDbContext> options) => new(options);` are identical across both factories, matching both base classes' identical abstract-member shapes (confirmed by reading `NorsePostgreSqlDesignTimeDbContextFactory` and `NorseSqlServerDesignTimeDbContextFactory` directly, both in `Urdarbrunnr/src/Persistence.EntityFramework.Design.*`). The `ConfigureOptions` override signature (`DbContextOptionsBuilder<NorseIdentityDbContext> builder, string connectionString`) matches both base classes' `protected virtual void ConfigureOptions(...)` exactly. Test project naming (`Identity.Migrations.PostgreSQL.Tests`, `Identity.Migrations.SqlServer.Tests`) matches the `{AssemblyName}.Tests` pattern `InternalsVisibleTo` requires.
