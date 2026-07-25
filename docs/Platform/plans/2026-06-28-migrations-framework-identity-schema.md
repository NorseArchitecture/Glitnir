# Migrations Framework & Identity Schema — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (default) or `superpowers:executing-plans` (separate-session fallback, never interchangeable). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire `Norse.EntityFramework.Migrations` (with Roslyn source generator), `Norse.Identity` + `Norse.Identity.Migrations`, and `Norse.Infrastructure`'s `MigrationRunnerService` so that the Aspire AppHost starts a Postgres primary+replica pair, runs the migrations service to completion (exit 0), and leaves `norse_identity` with the full ASP.NET Core Identity v3 + OpenIddict schema.

**Architecture:** Six realms share the work in strict dependency order. Asgard declares the `IMigrationContributor` interface. Midgard implements the runner. Urdarbrunnr provides the EF base context, the abstract EF contributor, and a Roslyn generator that discovers all contributors at compile time and emits `AddNorseMigrations()`. Himinbjörg provides the Identity entities, DbContext, UserStore, and the contributor that drives `dotnet ef migrations`. Yggdrasil replaces the stub `Program.cs` with a three-line form that calls only the generated method. Bifrost wires the complete Postgres primary+replica topology and adds the migrations service project reference to the AppHost.

**Amendment (2026-07-25):** every `Norse.EntityFramework*` name below (`Norse.EntityFramework`, `.Migrations`, `.PostgreSQL`, `.Migrations.PostgreSQL`) is the pre-rename name, historically accurate as written on 2026-06-28. The whole family was renamed/widened to `Norse.Persistence.EntityFramework.*` on Urdarbrunnr's `feature/persistence-namespace-widening` branch and merged to `master` (PR #31, tag v0.0.4, 2026-07-22). See `../../Urdarbrunnr/specs/2026-07-22-design-time-ddl-emission-and-chassis-rename-design.md` and `../../Himinbjorg/specs/2026-07-22-persistence-rename-and-migrations-trio-design.md`.

**Execution model — realm-by-realm ship gates:** This plan executes in five phases. Each phase ends with a `## SHIP GATE` section. Do not start the next phase until the gate is cleared: the realm's PR is merged, GitHub CI is green, a version tag is pushed, and the resulting NuGet package(s) are live on the feed. This is what makes the `UseProjectReferences=false` verification real — by Task 10 every cross-realm reference already resolves from NuGet; there is no local-feed workaround. In Bifrost during development (`UseProjectReferences=true`), NorseRef items resolve as ProjectReferences across the submodule tree as designed.

**Tech Stack:** .NET 11, C#, EF Core (Npgsql), ASP.NET Core Identity v3, OpenIddict, Roslyn `IIncrementalGenerator`, `EFCore.NamingConventions`, xUnit v3 + Shouldly + NSubstitute, .NET Aspire 13.x

## Global Constraints

- Target framework: `net11.0` for all library and service projects; `netstandard2.0` for the generator-only project.
- No `var` on the left side of constructor calls — use explicit type + `new()`. `var` is fine for return-value assignments.
- `internal sealed` is the default accessibility; omit accessibility keywords when they are the default (`omit_if_default`). A type that needs to be `public` must have a justified cross-assembly caller in this plan.
- Tabs for indentation everywhere except YAML/JSON (2-space) and shell scripts (2-space or 4-space, consistent within file).
- US English spelling in all identifiers, comments, docs.
- No automatic git commits — stage only (`git add`); human commits.
- No `SelectMany` on `IEnumerable<IMigrationContributor>` in tests — pass the list directly.
- Shouldly for all assertions; NSubstitute for all mocks.
- No force-push to `master`. No `--no-verify`.
- `NorseRef` for cross-realm references; plain `<ProjectReference>` for same-realm references. No NorseRef inside a `<Target>` block (YGG301).
- Generator must walk **compiled symbols** (`compilation.SourceModule.ReferencedAssemblySymbols`), never source syntax trees — PackageReference mode parity depends on it.
- All new realms wired into `Bifrost.slnx` in the same task that creates their solution file.

---

## File Map

### Asgard
| Action | Path |
|---|---|
| Create | `Asgard/src/Abstractions.Migrations/IMigrationContributor.cs` |
| Create | `Asgard/tests/Abstractions.Migrations.Tests/IMigrationContributorTests.cs` |

### Midgard
| Action | Path |
|---|---|
| Create | `Midgard/Midgard.slnx` |
| Create | `Midgard/src/Infrastructure.Migrations/Infrastructure.Migrations.csproj` |
| Create | `Midgard/src/Infrastructure.Migrations/MigrationRunnerService.cs` |
| Create | `Midgard/src/Infrastructure.Migrations/HostApplicationBuilderExtensions.cs` |
| Create | `Midgard/tests/Infrastructure.Migrations.Tests/Infrastructure.Migrations.Tests.csproj` |
| Create | `Midgard/tests/Infrastructure.Migrations.Tests/MigrationRunnerServiceTests.cs` |
| Modify | `Bifrost.slnx` — add `/Infrastructure/` solution folder |

### Urdarbrunnr
| Action | Path |
|---|---|
| Create | `Urdarbrunnr/Urdarbrunnr.slnx` |
| Create | `Urdarbrunnr/src/EntityFramework/EntityFramework.csproj` |
| Create | `Urdarbrunnr/src/EntityFramework/INorseDbContext.cs` |
| Create | `Urdarbrunnr/src/EntityFramework/NorseDbContext.cs` |
| Create | `Urdarbrunnr/src/EntityFramework/NorseModelBuilderExtensions.cs` |
| Create | `Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj` |
| Create | `Urdarbrunnr/tests/EntityFramework.Tests/NorseModelBuilderExtensionsTests.cs` |
| Create | `Urdarbrunnr/src/EntityFramework.PostgreSQL/EntityFramework.PostgreSQL.csproj` |
| Create | `Urdarbrunnr/src/EntityFramework.PostgreSQL/NorsePostgresContextExtensions.cs` |
| Create | `Urdarbrunnr/tests/EntityFramework.PostgreSQL.Tests/EntityFramework.PostgreSQL.Tests.csproj` |
| Create | `Urdarbrunnr/tests/EntityFramework.PostgreSQL.Tests/NorsePostgresContextExtensionsTests.cs` |
| Create | `Urdarbrunnr/src/EntityFramework.Migrations/EntityFramework.Migrations.csproj` |
| Create | `Urdarbrunnr/src/EntityFramework.Migrations/EfMigrationContributor.cs` |
| Create | `Urdarbrunnr/src/EntityFramework.Migrations/MigrationConnectionStringAttribute.cs` |
| Create | `Urdarbrunnr/tests/EntityFramework.Migrations.Tests/EntityFramework.Migrations.Tests.csproj` |
| Create | `Urdarbrunnr/tests/EntityFramework.Migrations.Tests/EfMigrationContributorTests.cs` |
| Create | `Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL/EntityFramework.Migrations.PostgreSQL.csproj` |
| Create | `Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj` |
| Create | `Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL.Generator/MigrationContributorGenerator.cs` |
| Create | `Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests.csproj` |
| Create | `Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/MigrationContributorGeneratorTests.cs` |
| Modify | `Bifrost.slnx` — add `/EntityFramework/` solution folder |

### Himinbjörg
| Action | Path |
|---|---|
| Create | `Himinbjorg/Himinbjorg.slnx` |
| Create | `Himinbjorg/src/Identity/Identity.csproj` |
| Create | `Himinbjorg/src/Identity/NorseUser.cs` |
| Create | `Himinbjorg/src/Identity/NorseRole.cs` |
| Create | `Himinbjorg/src/Identity/NorseUserClaim.cs` |
| Create | `Himinbjorg/src/Identity/NorseUserRole.cs` |
| Create | `Himinbjorg/src/Identity/NorseUserLogin.cs` |
| Create | `Himinbjorg/src/Identity/NorseUserToken.cs` |
| Create | `Himinbjorg/src/Identity/NorseRoleClaim.cs` |
| Create | `Himinbjorg/src/Identity/NorseUserPasskey.cs` |
| Create | `Himinbjorg/src/Identity/NorseIdentityDbContext.cs` |
| Create | `Himinbjorg/src/Identity/NorseUserStore.cs` |
| Create | `Himinbjorg/src/Identity/IdentityBuilderExtensions.cs` |
| Create | `Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj` |
| Create | `Himinbjorg/tests/Identity.Tests/NorseUserStoreTests.cs` |
| Create | `Himinbjorg/src/Identity.Migrations/Identity.Migrations.csproj` |
| Create | `Himinbjorg/src/Identity.Migrations/NorseIdentityMigrationContributor.cs` |
| Create | `Himinbjorg/src/Identity.Migrations/NorseIdentityDbContextFactory.cs` |
| Create | `Himinbjorg/src/Identity.Migrations/Migrations/` (EF scaffold output) |
| Create | `Himinbjorg/tests/Identity.Migrations.Tests/Identity.Migrations.Tests.csproj` |
| Create | `Himinbjorg/tests/Identity.Migrations.Tests/NorseIdentityMigrationContributorTests.cs` |
| Modify | `Bifrost.slnx` — add `/Identity/` solution folder |

### Yggdrasil
| Action | Path |
|---|---|
| Modify | `Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj` |
| Modify | `Yggdrasil/src/Hosting.Migrations.Service/Program.cs` |
| Delete | `Yggdrasil/src/Hosting.Migrations.Service/Placeholder.cs` |

### Bifrost
| Action | Path |
|---|---|
| Create | `src/Orchestration.AppHost/postgres/replication-hba.sh` |
| Create | `src/Orchestration.AppHost/postgres/replica-entrypoint.sh` |
| Modify | `src/Orchestration.AppHost/AppHost.cs` |
| Modify | `src/Orchestration.AppHost/Orchestration.AppHost.csproj` |
| Modify | `src/Orchestration.AppHost/.gitattributes` (or root `.gitattributes`) |

---

## Task 1: Asgard — `IMigrationContributor`

**Files:**
- Create: `Asgard/src/Abstractions.Migrations/IMigrationContributor.cs`
- Create: `Asgard/tests/Abstractions.Migrations.Tests/IMigrationContributorTests.cs`

**Interfaces:**
- Produces: `Norse.Abstractions.Migrations.IMigrationContributor` — `string Name { get; }` + `Task MigrateAsync(CancellationToken)`

- [ ] **Step 1: Write the failing test**

`Asgard/tests/Abstractions.Migrations.Tests/IMigrationContributorTests.cs`:
```csharp
namespace Norse.Abstractions.Migrations;

public sealed class IMigrationContributorTests
{
	[Fact]
	public async Task MigrateAsync_invokes_concrete_implementation()
	{
		StubContributor stub = new();

		await stub.MigrateAsync(CancellationToken.None);

		stub.Invoked.ShouldBeTrue();
	}

	[Fact]
	public void Name_returns_concrete_value()
	{
		StubContributor stub = new();

		stub.Name.ShouldBe("Stub");
	}

	sealed class StubContributor : IMigrationContributor
	{
		public string Name => "Stub";
		public bool Invoked { get; private set; }

		public Task MigrateAsync(CancellationToken cancellationToken)
		{
			Invoked = true;
			return Task.CompletedTask;
		}
	}
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test Asgard/tests/Abstractions.Migrations.Tests/Abstractions.Migrations.Tests.csproj
```

Expected: compile error — `IMigrationContributor` not defined.

- [ ] **Step 3: Add the interface**

`Asgard/src/Abstractions.Migrations/IMigrationContributor.cs`:
```csharp
namespace Norse.Abstractions.Migrations;

public interface IMigrationContributor
{
	string Name { get; }
	Task MigrateAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Run the test to confirm it passes**

```bash
dotnet test Asgard/tests/Abstractions.Migrations.Tests/Abstractions.Migrations.Tests.csproj
```

Expected: PASS.

- [ ] **Step 5: Stage**

```bash
git -C Asgard add src/Abstractions.Migrations/IMigrationContributor.cs \
                  tests/Abstractions.Migrations.Tests/IMigrationContributorTests.cs
```

---

## SHIP GATE — Asgard

**STOP. Do not start Task 2 until this gate is cleared.**

1. Commit and push `Asgard/src/Abstractions.Migrations/IMigrationContributor.cs` and its test.
2. Open a PR against `master`; confirm GitHub CI (build + test) is green.
3. Merge the PR.
4. Push a version tag (e.g., `v0.1.0`) on `master` to trigger the release pipeline.
5. Confirm `Norse.Abstractions.Migrations` is published to the NuGet feed.

Only after the package is live does Task 2 begin.

---

## Task 2: Midgard — `MigrationRunnerService` + `AddNorseMigrationsRunner()`

**Files:**
- Create: `Midgard/Midgard.slnx`
- Create: `Midgard/src/Infrastructure.Migrations/Infrastructure.Migrations.csproj`
- Create: `Midgard/src/Infrastructure.Migrations/MigrationRunnerService.cs`
- Create: `Midgard/src/Infrastructure.Migrations/HostApplicationBuilderExtensions.cs`
- Create: `Midgard/tests/Infrastructure.Migrations.Tests/Infrastructure.Migrations.Tests.csproj`
- Create: `Midgard/tests/Infrastructure.Migrations.Tests/MigrationRunnerServiceTests.cs`
- Modify: `Bifrost.slnx`

**Interfaces:**
- Consumes: `IMigrationContributor` from Task 1 (`Norse.Abstractions.Migrations`)
- Produces:
  - `Norse.Infrastructure.MigrationRunnerService` — `IHostedService`, resolves all `IEnumerable<IMigrationContributor>`, runs them with `Task.WhenAll`, calls `StopApplication()` on success, throws on any failure.
  - `IHostApplicationBuilderExtensions.AddNorseMigrationsRunner(this IHostApplicationBuilder)` — registers `MigrationRunnerService` as a hosted service.

- [ ] **Step 1: Create the solution file**

`Midgard/Midgard.slnx`:
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
		<Project Path="src/Infrastructure.Migrations/Infrastructure.Migrations.csproj" />
	</Folder>
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<File Path="tests/Directory.Build.targets" />
		<Project Path="tests/Infrastructure.Migrations.Tests/Infrastructure.Migrations.Tests.csproj" />
	</Folder>
</Solution>
```

- [ ] **Step 2: Create the project file**

`Midgard/src/Infrastructure.Migrations/Infrastructure.Migrations.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Infrastructure.Migrations: the MigrationRunnerService and AddNorseMigrationsRunner extension — the hosted service that calls all IMigrationContributor implementations and exits cleanly. Consumed only by the migrations service; never referenced from runtime containers.</Description>
	</PropertyGroup>
	<ItemGroup>
		<NorseRef Include="Abstractions.Migrations">
			<Repo>Asgard</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

- [ ] **Step 3: Create the test project**

`Midgard/tests/Infrastructure.Migrations.Tests/Infrastructure.Migrations.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="NSubstitute" Version="*" />
		<ProjectReference Include="../../src/Infrastructure.Migrations/Infrastructure.Migrations.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 4: Write the failing tests**

`Midgard/tests/Infrastructure.Migrations.Tests/MigrationRunnerServiceTests.cs`:
```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Norse.Abstractions.Migrations;
using NSubstitute;

namespace Norse.Infrastructure.Migrations.Tests;

public sealed class MigrationRunnerServiceTests
{
	[Fact]
	public async Task StartAsync_runs_all_contributors_and_stops_application()
	{
		var contributor = Substitute.For<IMigrationContributor>();
		contributor.Name.Returns("Test");
		contributor.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var lifetime = Substitute.For<IHostApplicationLifetime>();
		var sut = new MigrationRunnerService(
			[contributor],
			lifetime,
			NullLogger<MigrationRunnerService>.Instance);

		await sut.StartAsync(CancellationToken.None);

		await contributor.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
		lifetime.Received(1).StopApplication();
	}

	[Fact]
	public async Task StartAsync_with_multiple_contributors_runs_all()
	{
		var a = Substitute.For<IMigrationContributor>();
		a.Name.Returns("A");
		a.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var b = Substitute.For<IMigrationContributor>();
		b.Name.Returns("B");
		b.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var lifetime = Substitute.For<IHostApplicationLifetime>();
		var sut = new MigrationRunnerService(
			[a, b],
			lifetime,
			NullLogger<MigrationRunnerService>.Instance);

		await sut.StartAsync(CancellationToken.None);

		await a.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
		await b.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
		lifetime.Received(1).StopApplication();
	}

	[Fact]
	public async Task StartAsync_propagates_exception_and_does_not_stop_application()
	{
		var contributor = Substitute.For<IMigrationContributor>();
		contributor.Name.Returns("Bad");
		contributor.MigrateAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new InvalidOperationException("migration failed")));

		var lifetime = Substitute.For<IHostApplicationLifetime>();
		var sut = new MigrationRunnerService(
			[contributor],
			lifetime,
			NullLogger<MigrationRunnerService>.Instance);

		var act = () => sut.StartAsync(CancellationToken.None);

		await act.ShouldThrowAsync<InvalidOperationException>();
		lifetime.DidNotReceive().StopApplication();
	}

	[Fact]
	public async Task StopAsync_is_always_a_noop()
	{
		var sut = new MigrationRunnerService(
			[],
			Substitute.For<IHostApplicationLifetime>(),
			NullLogger<MigrationRunnerService>.Instance);

		await sut.StopAsync(CancellationToken.None);
	}
}
```

- [ ] **Step 5: Run the tests to confirm they fail**

```bash
dotnet test Midgard/tests/Infrastructure.Migrations.Tests/Infrastructure.Migrations.Tests.csproj
```

Expected: compile error — `MigrationRunnerService` not defined.

- [ ] **Step 6: Implement `MigrationRunnerService`**

`Midgard/src/Infrastructure.Migrations/MigrationRunnerService.cs`:
```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Norse.Abstractions.Migrations;

namespace Norse.Infrastructure.Migrations;

sealed class MigrationRunnerService(
	IEnumerable<IMigrationContributor> contributors,
	IHostApplicationLifetime lifetime,
	ILogger<MigrationRunnerService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await Task.WhenAll(contributors.Select(c => RunAsync(c, cancellationToken)));
		lifetime.StopApplication();
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	async Task RunAsync(IMigrationContributor contributor, CancellationToken ct)
	{
		logger.LogInformation("Starting migration contributor {Name}", contributor.Name);
		await contributor.MigrateAsync(ct);
		logger.LogInformation("Migration contributor {Name} completed", contributor.Name);
	}
}
```

- [ ] **Step 7: Implement `AddNorseMigrationsRunner()`**

`Midgard/src/Infrastructure.Migrations/HostApplicationBuilderExtensions.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Infrastructure.Migrations;

public static class HostApplicationBuilderExtensions
{
	public static IHostApplicationBuilder AddNorseMigrationsRunner(this IHostApplicationBuilder builder)
	{
		builder.Services.AddHostedService<MigrationRunnerService>();
		return builder;
	}
}
```

- [ ] **Step 8: Run the tests to confirm they pass**

```bash
dotnet test Midgard/tests/Infrastructure.Migrations.Tests/Infrastructure.Migrations.Tests.csproj
```

Expected: all 4 tests PASS.

- [ ] **Step 9: Add Midgard to `Bifrost.slnx`**

Add the following folder block inside the `<Solution>` element of `Bifrost/Bifrost.slnx` after the existing `/Primitives/` folder:

```xml
<Folder Name="/Infrastructure/">
	<File Path="Midgard/.editorconfig" />
	<File Path="Midgard/.gitattributes" />
	<File Path="Midgard/.gitignore" />
	<File Path="Midgard/CLAUDE.md" />
	<File Path="Midgard/Directory.Build.props" />
	<File Path="Midgard/global.json" />
	<File Path="Midgard/LICENSE" />
	<File Path="Midgard/Midgard.slnx" />
	<File Path="Midgard/nuget.config" />
	<File Path="Midgard/README.md" />
</Folder>
<Folder Name="/Infrastructure/src/">
	<File Path="Midgard/src/Directory.Build.props" />
	<File Path="Midgard/src/Directory.Build.targets" />
	<Project Path="Midgard/src/Infrastructure.Migrations/Infrastructure.Migrations.csproj" />
</Folder>
<Folder Name="/Infrastructure/tests/">
	<File Path="Midgard/tests/Directory.Build.props" />
	<Project Path="Midgard/tests/Infrastructure.Migrations.Tests/Infrastructure.Migrations.Tests.csproj" />
</Folder>
```

- [ ] **Step 10: Stage**

```bash
git -C Midgard add Midgard.slnx \
  src/Infrastructure.Migrations/Infrastructure.Migrations.csproj \
  src/Infrastructure.Migrations/MigrationRunnerService.cs \
  src/Infrastructure.Migrations/HostApplicationBuilderExtensions.cs \
  tests/Infrastructure.Migrations.Tests/Infrastructure.Migrations.Tests.csproj \
  tests/Infrastructure.Migrations.Tests/MigrationRunnerServiceTests.cs
git add Bifrost.slnx
```

---

## SHIP GATE — Midgard

**STOP. Do not start Task 3 until this gate is cleared.**

1. Commit, push, and open a PR for `Midgard/src/Infrastructure/` and its tests.
2. Confirm GitHub CI is green.
3. Merge and tag (e.g., `v0.1.0`).
4. Confirm `Norse.Infrastructure` is published to the NuGet feed.

---

## Task 3: Urdarbrunnr — `Norse.EntityFramework` (runtime base + conventions)

**Files:**
- Create: `Urdarbrunnr/Urdarbrunnr.slnx`
- Create: `Urdarbrunnr/src/EntityFramework/EntityFramework.csproj`
- Create: `Urdarbrunnr/src/EntityFramework/NorseDbContext.cs`
- Create: `Urdarbrunnr/src/EntityFramework/NorseModelBuilderExtensions.cs`
- Create: `Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj`
- Create: `Urdarbrunnr/tests/EntityFramework.Tests/NorseModelBuilderExtensionsTests.cs`
- Modify: `Bifrost.slnx`

**Interfaces:**
- Produces:
  - `Norse.EntityFramework.INorseDbContext` — marker interface; implemented by all Norse EF contexts. Allows `EfMigrationContributor<TContext>` to constrain `TContext : DbContext, INorseDbContext` without forcing a single base class (auth contexts must inherit `IdentityDbContext`, not `NorseDbContext`).
  - `Norse.EntityFramework.NorseDbContext` — abstract `DbContext` base implementing `INorseDbContext`; overrides `OnConfiguring` to call `NorseDbContextOptionsExtensions.ApplyNorseConventions(optionsBuilder)`, ensuring snake_case naming is always applied regardless of how the context is constructed. Used by non-auth EF contexts.
  - `Norse.EntityFramework.NorseDbContextOptionsExtensions.ApplyNorseConventions(DbContextOptionsBuilder)` — applies `UseSnakeCaseNamingConvention()` from `EFCore.NamingConventions`. Called manually by auth contexts (e.g. `NorseIdentityDbContext`) that cannot inherit `NorseDbContext` and must override `OnConfiguring` themselves.

- [ ] **Step 1: Create solution and project files**

`Urdarbrunnr/Urdarbrunnr.slnx`:
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
	</Folder>
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<File Path="tests/Directory.Build.targets" />
		<Project Path="tests/EntityFramework.Tests/EntityFramework.Tests.csproj" />
	</Folder>
</Solution>

Each subsequent task adds its own projects to `Urdarbrunnr.slnx` — never pre-populate future projects that don't exist yet.
```

`Urdarbrunnr/src/EntityFramework/EntityFramework.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.EntityFramework: the INorseDbContext marker interface, the abstract NorseDbContext base, and snake_case naming conventions — provider-agnostic EF foundation shared by all Norse contexts regardless of RDBMS.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="EFCore.NamingConventions" />
		<PackageReference Include="Microsoft.EntityFrameworkCore" />
	</ItemGroup>
</Project>
```

`Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
		<ProjectReference Include="../../src/EntityFramework/EntityFramework.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing test**

`Urdarbrunnr/tests/EntityFramework.Tests/NorseModelBuilderExtensionsTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework.Tests;

public sealed class NorseModelBuilderExtensionsTests
{
	[Fact]
	public void ApplyNorseConventions_applies_snake_case_naming()
	{
		var options = new DbContextOptionsBuilder<TestContext>()
			.UseSqlite("Data Source=:memory:")
			.Options;

		using var ctx = new TestContext(options);
		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		tableName.ShouldBe("test_entities");
	}

	[Fact]
	public void NorseDbContext_implements_INorseDbContext()
	{
		var options = new DbContextOptionsBuilder<TestContext>()
			.UseSqlite("Data Source=:memory:")
			.Options;

		using var ctx = new TestContext(options);

		ctx.ShouldBeAssignableTo<INorseDbContext>();
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options)
	{
		public DbSet<TestEntity> TestEntities => Set<TestEntity>();
	}

	sealed class TestEntity
	{
		public int Id { get; set; }
		public string Name { get; set; } = "";
	}
}
```

- [ ] **Step 3: Run the test to confirm it fails**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj
```

Expected: compile error — `NorseModelBuilderExtensions` not defined.

- [ ] **Step 4: Implement `INorseDbContext`**

`Urdarbrunnr/src/EntityFramework/INorseDbContext.cs`:
```csharp
namespace Norse.EntityFramework;

public interface INorseDbContext;
```

- [ ] **Step 5: Implement `NorseDbContextOptionsExtensions`**

`Urdarbrunnr/src/EntityFramework/NorseDbContextOptionsExtensions.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework;

public static class NorseDbContextOptionsExtensions
{
	public static DbContextOptionsBuilder ApplyNorseConventions(DbContextOptionsBuilder optionsBuilder)
	{
		optionsBuilder.UseSnakeCaseNamingConvention();
		return optionsBuilder;
	}
}
```

Note: `UseSnakeCaseNamingConvention()` is an extension on `DbContextOptionsBuilder` (from `EFCore.NamingConventions`), not on `ModelBuilder`. Naming conventions are registered at the options level and applied when EF Core builds the model.

- [ ] **Step 6: Implement `NorseDbContext`**

`Urdarbrunnr/src/EntityFramework/NorseDbContext.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework;

public abstract class NorseDbContext(DbContextOptions options) : DbContext(options), INorseDbContext
{
	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		base.OnConfiguring(optionsBuilder);
		NorseDbContextOptionsExtensions.ApplyNorseConventions(optionsBuilder);
	}
}
```

Note: `OnConfiguring` is called by EF Core even when options are supplied via the constructor (DI path). The optionsBuilder is seeded from the pre-built options and is fully mutable — naming conventions are added on top of whatever the consumer configured.

- [ ] **Step 7: Run the tests to confirm they pass**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj
```

Expected: 2/2 PASS.

- [ ] **Step 8: Add Urdarbrunnr to `Bifrost.slnx`**

Add the following folder block after the `/Infrastructure/` folders added in Task 2:

```xml
<Folder Name="/EntityFramework/">
	<File Path="Urdarbrunnr/.editorconfig" />
	<File Path="Urdarbrunnr/.gitattributes" />
	<File Path="Urdarbrunnr/.gitignore" />
	<File Path="Urdarbrunnr/CLAUDE.md" />
	<File Path="Urdarbrunnr/Directory.Build.props" />
	<File Path="Urdarbrunnr/global.json" />
	<File Path="Urdarbrunnr/LICENSE" />
	<File Path="Urdarbrunnr/nuget.config" />
	<File Path="Urdarbrunnr/README.md" />
	<File Path="Urdarbrunnr/Urdarbrunnr.slnx" />
</Folder>
<Folder Name="/EntityFramework/src/">
	<File Path="Urdarbrunnr/src/Directory.Build.props" />
	<File Path="Urdarbrunnr/src/Directory.Build.targets" />
	<Project Path="Urdarbrunnr/src/EntityFramework/EntityFramework.csproj" />
	<Project Path="Urdarbrunnr/src/EntityFramework.PostgreSQL/EntityFramework.PostgreSQL.csproj" />
	<Project Path="Urdarbrunnr/src/EntityFramework.Migrations/EntityFramework.Migrations.csproj" />
	<Project Path="Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL/EntityFramework.Migrations.PostgreSQL.csproj" />
	<Project Path="Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj" />
</Folder>
<Folder Name="/EntityFramework/tests/">
	<File Path="Urdarbrunnr/tests/Directory.Build.props" />
	<Project Path="Urdarbrunnr/tests/EntityFramework.Tests/EntityFramework.Tests.csproj" />
	<Project Path="Urdarbrunnr/tests/EntityFramework.PostgreSQL.Tests/EntityFramework.PostgreSQL.Tests.csproj" />
	<Project Path="Urdarbrunnr/tests/EntityFramework.Migrations.Tests/EntityFramework.Migrations.Tests.csproj" />
	<Project Path="Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests.csproj" />
</Folder>
```

- [ ] **Step 9: Stage**

```bash
git -C Urdarbrunnr add Urdarbrunnr.slnx \
  src/EntityFramework/EntityFramework.csproj \
  src/EntityFramework/INorseDbContext.cs \
  src/EntityFramework/NorseDbContext.cs \
  src/EntityFramework/NorseDbContextOptionsExtensions.cs \
  tests/EntityFramework.Tests/EntityFramework.Tests.csproj \
  tests/EntityFramework.Tests/NorseDbContextOptionsExtensionsTests.cs
git add Bifrost.slnx
```

---

## Task 4: Urdarbrunnr — `Norse.EntityFramework.PostgreSQL` (Aspire-wired Postgres runtime)

**Files:**
- Create: `Urdarbrunnr/src/EntityFramework.PostgreSQL/EntityFramework.PostgreSQL.csproj`
- Create: `Urdarbrunnr/src/EntityFramework.PostgreSQL/NorsePostgresContextExtensions.cs`
- Create: `Urdarbrunnr/tests/EntityFramework.PostgreSQL.Tests/EntityFramework.PostgreSQL.Tests.csproj`
- Create: `Urdarbrunnr/tests/EntityFramework.PostgreSQL.Tests/NorsePostgresContextExtensionsTests.cs`

**Interfaces:**
- Consumes: `Norse.EntityFramework` (Task 3) — same-realm `ProjectReference`
- Produces:
  - `Norse.EntityFramework.PostgreSQL.NorsePostgresContextExtensions.AddNorsePostgresContext<TContext>(this IHostApplicationBuilder, string connectionStringName)` — wires `TContext` into the Aspire host using `AddNpgsqlDbContext<TContext>` with `UseSnakeCaseNamingConvention()`. Referenced by web.server and worker; also emitted by the generator in Task 6.

This package is the canonical runtime Postgres wiring for Norse contexts. The constraint `where TContext : DbContext, INorseDbContext` ensures only Norse-registered contexts can be wired through this path. The migrations service does NOT reference this package directly; it gets it transitively through `Norse.EntityFramework.Migrations.PostgreSQL`.

- [ ] **Step 1: Create the project files**

`Urdarbrunnr/src/EntityFramework.PostgreSQL/EntityFramework.PostgreSQL.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.EntityFramework.PostgreSQL: canonical Aspire-wired Postgres context registration via AddNorsePostgresContext&lt;TContext&gt;. Referenced by web server and worker hosts; pulled in transitively by Norse.EntityFramework.Migrations.PostgreSQL for the migrations service.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" Version="*" />
	</ItemGroup>
	<ItemGroup>
		<ProjectReference Include="../EntityFramework/EntityFramework.csproj" />
	</ItemGroup>
</Project>
```

`Urdarbrunnr/tests/EntityFramework.PostgreSQL.Tests/EntityFramework.PostgreSQL.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.Extensions.Hosting" Version="*" />
		<ProjectReference Include="../../src/EntityFramework.PostgreSQL/EntityFramework.PostgreSQL.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing test**

`Urdarbrunnr/tests/EntityFramework.PostgreSQL.Tests/NorsePostgresContextExtensionsTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.EntityFramework.PostgreSQL.Tests;

public sealed class NorsePostgresContextExtensionsTests
{
	[Fact]
	public void AddNorsePostgresContext_registers_TContext_in_DI()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = "Host=localhost;Database=test" });

		builder.AddNorsePostgresContext<TestContext>("test-db");

		var descriptor = builder.Services
			.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options);
}
```

- [ ] **Step 3: Run the test to confirm it fails**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.PostgreSQL.Tests/EntityFramework.PostgreSQL.Tests.csproj
```

Expected: compile error — `AddNorsePostgresContext` not defined.

- [ ] **Step 4: Implement `NorsePostgresContextExtensions`**

`Urdarbrunnr/src/EntityFramework.PostgreSQL/NorsePostgresContextExtensions.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Norse.EntityFramework.PostgreSQL;

public static class NorsePostgresContextExtensions
{
	public static IHostApplicationBuilder AddNorsePostgresContext<TContext>(
		this IHostApplicationBuilder builder,
		string connectionStringName)
		where TContext : DbContext, INorseDbContext
	{
		builder.AddNpgsqlDbContext<TContext>(connectionStringName,
			configureDbContextOptions: opts => opts.UseSnakeCaseNamingConvention());
		return builder;
	}
}
```

- [ ] **Step 5: Run the test to confirm it passes**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.PostgreSQL.Tests/EntityFramework.PostgreSQL.Tests.csproj
```

Expected: PASS.

- [ ] **Step 6: Add projects to `Urdarbrunnr.slnx`**

Add the new project lines to `Urdarbrunnr/Urdarbrunnr.slnx` under the existing `/src/` and `/tests/` folders:

```xml
<!-- in /src/ folder, after EntityFramework.csproj -->
<Project Path="src/EntityFramework.PostgreSQL/EntityFramework.PostgreSQL.csproj" />
```
```xml
<!-- in /tests/ folder, after EntityFramework.Tests.csproj -->
<Project Path="tests/EntityFramework.PostgreSQL.Tests/EntityFramework.PostgreSQL.Tests.csproj" />
```

- [ ] **Step 7: Stage**

```bash
git -C Urdarbrunnr add \
  Urdarbrunnr.slnx \
  src/EntityFramework.PostgreSQL/EntityFramework.PostgreSQL.csproj \
  src/EntityFramework.PostgreSQL/NorsePostgresContextExtensions.cs \
  tests/EntityFramework.PostgreSQL.Tests/EntityFramework.PostgreSQL.Tests.csproj \
  tests/EntityFramework.PostgreSQL.Tests/NorsePostgresContextExtensionsTests.cs
```

---

## Task 5: Urdarbrunnr — `Norse.EntityFramework.Migrations` (base contributor + attribute)

**Files:**
- Create: `Urdarbrunnr/src/EntityFramework.Migrations/EntityFramework.Migrations.csproj`
- Create: `Urdarbrunnr/src/EntityFramework.Migrations/EfMigrationContributor.cs`
- Create: `Urdarbrunnr/src/EntityFramework.Migrations/MigrationConnectionStringAttribute.cs`
- Create: `Urdarbrunnr/tests/EntityFramework.Migrations.Tests/EntityFramework.Migrations.Tests.csproj`
- Create: `Urdarbrunnr/tests/EntityFramework.Migrations.Tests/EfMigrationContributorTests.cs`

**Interfaces:**
- Consumes: `IMigrationContributor` (Task 1), `Norse.EntityFramework` (Task 3)
- Produces:
  - `Norse.EntityFramework.Migrations.EfMigrationContributor<TContext>` — abstract base implementing `IMigrationContributor`; `MigrateAsync` delegates to `TContext.Database.MigrateAsync`; constrained `where TContext : DbContext, INorseDbContext` so only Norse-registered contexts can be wired in.
  - `Norse.EntityFramework.Migrations.MigrationConnectionStringAttribute(string connectionStringName)` — annotates EF contributor classes with the Aspire connection string name; the generator reads this to emit `GetConnectionString(name)` and `AddDbContext<TContext>` calls.

- [ ] **Step 1: Create the project files**

`Urdarbrunnr/src/EntityFramework.Migrations/EntityFramework.Migrations.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.EntityFramework.Migrations: EfMigrationContributor&lt;TContext&gt; base class and the MigrationConnectionString attribute — referenced only by migrations service and realm *.Migrations projects; never by runtime containers.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
	</ItemGroup>
	<ItemGroup>
		<ProjectReference Include="../EntityFramework/EntityFramework.csproj" />
	</ItemGroup>
	<ItemGroup>
		<NorseRef Include="Abstractions.Migrations">
			<Repo>Asgard</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

`Urdarbrunnr/tests/EntityFramework.Migrations.Tests/EntityFramework.Migrations.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" />
		<PackageReference Include="NSubstitute" />
		<ProjectReference Include="../../src/EntityFramework.Migrations/EntityFramework.Migrations.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing tests**

`Urdarbrunnr/tests/EntityFramework.Migrations.Tests/EfMigrationContributorTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework.Migrations.Tests;

public sealed class EfMigrationContributorTests
{
	[Fact]
	public void MigrationConnectionStringAttribute_stores_name()
	{
		MigrationConnectionStringAttribute attr = new("my-db");

		attr.ConnectionStringName.ShouldBe("my-db");
	}

	[Fact]
	public void Name_returns_subclass_value()
	{
		StubContext ctx = CreateContext();
		StubContributor sut = new(ctx);

		sut.Name.ShouldBe("Stub");
	}

	static StubContext CreateContext() =>
		new(new DbContextOptionsBuilder<StubContext>()
			.UseInMemoryDatabase("test-ef-migrations")
			.Options);

	[MigrationConnectionString("stub-db")]
	sealed class StubContributor(StubContext context) : EfMigrationContributor<StubContext>(context)
	{
		public override string Name => "Stub";
	}

	sealed class StubContext(DbContextOptions<StubContext> options) : NorseDbContext(options);
}
```

- [ ] **Step 3: Run the test to confirm it fails**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Migrations.Tests/EntityFramework.Migrations.Tests.csproj
```

Expected: compile error — types not defined.

- [ ] **Step 4: Implement `MigrationConnectionStringAttribute`**

`Urdarbrunnr/src/EntityFramework.Migrations/MigrationConnectionStringAttribute.cs`:
```csharp
namespace Norse.EntityFramework.Migrations;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MigrationConnectionStringAttribute(string connectionStringName) : Attribute
{
	public string ConnectionStringName { get; } = connectionStringName;
}
```

- [ ] **Step 5: Implement `EfMigrationContributor<TContext>`**

`Urdarbrunnr/src/EntityFramework.Migrations/EfMigrationContributor.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Norse.Abstractions.Migrations;
using Norse.EntityFramework;

namespace Norse.EntityFramework.Migrations;

public abstract class EfMigrationContributor<TContext>(TContext context) : IMigrationContributor
	where TContext : DbContext, INorseDbContext
{
	public abstract string Name { get; }

	public Task MigrateAsync(CancellationToken cancellationToken) =>
		context.Database.MigrateAsync(cancellationToken);
}
```

- [ ] **Step 6: Run the tests to confirm they pass**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Migrations.Tests/EntityFramework.Migrations.Tests.csproj
```

Expected: PASS. (The `MigrateAsync` behavior is proven by the integration test in Task 10, not here — in-memory provider does not support `MigrateAsync`.)

- [ ] **Step 7: Add projects to `Urdarbrunnr.slnx`**

Add the new project lines to `Urdarbrunnr/Urdarbrunnr.slnx`:

```xml
<!-- in /src/ folder, after EntityFramework.PostgreSQL.csproj -->
<Project Path="src/EntityFramework.Migrations/EntityFramework.Migrations.csproj" />
```
```xml
<!-- in /tests/ folder, after EntityFramework.PostgreSQL.Tests.csproj -->
<Project Path="tests/EntityFramework.Migrations.Tests/EntityFramework.Migrations.Tests.csproj" />
```

- [ ] **Step 8: Stage**

```bash
git -C Urdarbrunnr add \
  Urdarbrunnr.slnx \
  src/EntityFramework.Migrations/EntityFramework.Migrations.csproj \
  src/EntityFramework.Migrations/EfMigrationContributor.cs \
  src/EntityFramework.Migrations/MigrationConnectionStringAttribute.cs \
  tests/EntityFramework.Migrations.Tests/EntityFramework.Migrations.Tests.csproj \
  tests/EntityFramework.Migrations.Tests/EfMigrationContributorTests.cs
```

---

## Task 6: Urdarbrunnr — `Norse.EntityFramework.Migrations.PostgreSQL`

**Files:**
- Create: `Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj`
- Create: `Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL.Generator/MigrationContributorGenerator.cs`
- Create: `Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL/EntityFramework.Migrations.PostgreSQL.csproj`
- Create: `Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests.csproj`
- Create: `Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/MigrationContributorGeneratorTests.cs`

**Interfaces:**
- Consumes: `MigrationConnectionStringAttribute`, `EfMigrationContributor<TContext>`, `IMigrationContributor` (all from prior tasks) — read from compiled assembly symbols, never source trees.
- Produces:
  - `Norse.EntityFramework.Migrations.PostgreSQL` NuGet package — references `Norse.EntityFramework.Migrations` (Task 5) AND `Norse.EntityFramework.PostgreSQL` (Task 4) transitively; ships the generator DLL in `analyzers/dotnet/cs/`. Migrations service projects reference this one package and get everything.
  - `AddNorseMigrations(this IHostApplicationBuilder)` — emitted into the migrations service project's compilation; calls `AddNorsePostgresContext<TContext>(connectionStringName)` for each discovered contributor (from `Norse.EntityFramework.PostgreSQL`), registers each contributor as `IMigrationContributor`, then calls `AddNorseMigrationsRunner()`.

The generator must walk `compilation.SourceModule.ReferencedAssemblySymbols` (not syntax trees) so it works identically in both ProjectReference and PackageReference modes. `Norse.EntityFramework.Migrations` is unchanged by this task — the generator ships in a separate Postgres-specific package.

- [ ] **Step 1: Create the generator project**

`Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.EntityFramework.Migrations.PostgreSQL.Generator: the Roslyn IIncrementalGenerator that discovers EfMigrationContributor&lt;TContext&gt; implementations at compile time and emits AddNorseMigrations() with Npgsql connection wiring.</Description>
		<TargetFramework>netstandard2.0</TargetFramework>
		<IsRoslynComponent>true</IsRoslynComponent>
		<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
		<IsAotCompatible>false</IsAotCompatible>
		<IsPackable>false</IsPackable>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="*">
			<PrivateAssets>all</PrivateAssets>
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
		</PackageReference>
		<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="*">
			<PrivateAssets>all</PrivateAssets>
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
		</PackageReference>
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Create the PostgreSQL migrations wrapper package**

`Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL/EntityFramework.Migrations.PostgreSQL.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.EntityFramework.Migrations.PostgreSQL: pulls in Norse.EntityFramework.Migrations (contributor base) and Norse.EntityFramework.PostgreSQL (Aspire Postgres wiring) and ships the Roslyn generator that emits AddNorseMigrations(). Reference this single package from your migrations service.</Description>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../EntityFramework.Migrations/EntityFramework.Migrations.csproj" />
		<ProjectReference Include="../EntityFramework.PostgreSQL/EntityFramework.PostgreSQL.csproj" />
		<ProjectReference
			Include="../EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj"
			OutputItemType="Analyzer"
			ReferenceOutputAssembly="false" />
	</ItemGroup>
	<Target Name="IncludeGeneratorInPackage" BeforeTargets="_GetPackageFiles">
		<MSBuild Projects="../EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj"
			Targets="Build"
			Properties="Configuration=$(Configuration)" />
		<ItemGroup>
			<None Include="../EntityFramework.Migrations.PostgreSQL.Generator/bin/$(Configuration)/netstandard2.0/Norse.EntityFramework.Migrations.PostgreSQL.Generator.dll"
				Pack="true"
				PackagePath="analyzers/dotnet/cs/"
				Visible="false" />
		</ItemGroup>
	</Target>
</Project>
```

- [ ] **Step 3: Create the test project**

`Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<TargetFramework>net11.0</TargetFramework>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="*">
			<PrivateAssets>all</PrivateAssets>
		</PackageReference>
		<ProjectReference Include="../../src/EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 4: Write the failing tests**

`Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/MigrationContributorGeneratorTests.cs`:
```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Norse.EntityFramework.Migrations.PostgreSQL.Generator.Tests;

public sealed class MigrationContributorGeneratorTests
{
	[Fact]
	public void Generator_produces_AddNorseMigrations_method()
	{
		var source = """
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
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
		var result = driver.GetRunResult();

		result.GeneratedTrees.Length.ShouldBe(1);
		var generated = result.GeneratedTrees[0].ToString();
		generated.ShouldContain("AddNorseMigrations");
		generated.ShouldContain("AddNorseMigrationsRunner");
		generated.ShouldContain("TestContributor");
		generated.ShouldContain("test-db");
		generated.ShouldContain("AddNorsePostgresContext");
	}

	[Fact]
	public void Generator_emits_no_source_when_no_contributors_found()
	{
		var compilation = CreateCompilation("// empty");
		var generator = new MigrationContributorGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
		var result = driver.GetRunResult();

		result.GeneratedTrees.ShouldBeEmpty();
	}

	static Compilation CreateCompilation(string source)
	{
		var references = AppDomain.CurrentDomain.GetAssemblies()
			.Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
			.Select(a => MetadataReference.CreateFromFile(a.Location))
			.Cast<MetadataReference>()
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
dotnet test Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests.csproj
```

Expected: compile error — `MigrationContributorGenerator` not defined.

- [ ] **Step 6: Implement the generator**

`Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL.Generator/MigrationContributorGenerator.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Norse.EntityFramework.Migrations.PostgreSQL.Generator;

[Generator]
public sealed class MigrationContributorGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var contributors = context.CompilationProvider.Select(static (compilation, _) =>
			FindContributors(compilation));

		context.RegisterSourceOutput(contributors, static (ctx, contributors) =>
		{
			if (contributors.Count == 0)
				return;

			var source = BuildSource(contributors);
			ctx.AddSource("NorseMigrationsExtensions.g.cs", SourceText.From(source, Encoding.UTF8));
		});
	}

	static List<ContributorInfo> FindContributors(Compilation compilation)
	{
		var contributorInterface = compilation.GetTypeByMetadataName(
			"Norse.Abstractions.Migrations.IMigrationContributor");
		var attrType = compilation.GetTypeByMetadataName(
			"Norse.EntityFramework.Migrations.MigrationConnectionStringAttribute");
		var efBase = compilation.GetTypeByMetadataName(
			"Norse.EntityFramework.Migrations.EfMigrationContributor`1");

		if (contributorInterface is null || attrType is null || efBase is null)
			return [];

		var results = new List<ContributorInfo>();

		foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
		{
			foreach (var type in GetAllTypes(assembly.GlobalNamespace))
			{
				if (type.IsAbstract)
					continue;

				if (!ImplementsInterface(type, contributorInterface))
					continue;

				var attr = type.GetAttributes()
					.FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attrType));

				if (attr is null || attr.ConstructorArguments.Length == 0)
					continue;

				var connectionStringName = attr.ConstructorArguments[0].Value as string;
				if (connectionStringName is null)
					continue;

				var dbContextType = FindEfContextType(type, efBase);
				if (dbContextType is null)
					continue;

				results.Add(new ContributorInfo(
					type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
					dbContextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
					connectionStringName));
			}
		}

		return results;
	}

	static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol interfaceType)
	{
		return type.AllInterfaces.Any(i =>
			SymbolEqualityComparer.Default.Equals(i, interfaceType));
	}

	static INamedTypeSymbol? FindEfContextType(INamedTypeSymbol type, INamedTypeSymbol efBase)
	{
		var current = type.BaseType;
		while (current is not null)
		{
			if (current.OriginalDefinition is INamedTypeSymbol original &&
				SymbolEqualityComparer.Default.Equals(original, efBase) &&
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

	static string BuildSource(List<ContributorInfo> contributors)
	{
		var sb = new StringBuilder();
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
			sb.AppendLine($"\t\tbuilder.AddNorsePostgresContext<{c.ContextType}>(\"{c.ConnectionStringName}\");");
			sb.AppendLine($"\t\tbuilder.Services.AddTransient<global::Norse.Abstractions.Migrations.IMigrationContributor, {c.ContributorType}>();");
		}

		sb.AppendLine("\t\tbuilder.AddNorseMigrationsRunner();");
		sb.AppendLine("\t\treturn builder;");
		sb.AppendLine("\t}");
		sb.AppendLine("}");

		return sb.ToString();
	}

	sealed record ContributorInfo(string ContributorType, string ContextType, string ConnectionStringName);
}
```

- [ ] **Step 7: Run the tests to confirm they pass**

```bash
dotnet test Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests.csproj
```

Expected: PASS.

- [ ] **Step 8: Add projects to `Urdarbrunnr.slnx`**

Add the new project lines to `Urdarbrunnr/Urdarbrunnr.slnx`:

```xml
<!-- in /src/ folder, after EntityFramework.Migrations.csproj -->
<Project Path="src/EntityFramework.Migrations.PostgreSQL/EntityFramework.Migrations.PostgreSQL.csproj" />
<Project Path="src/EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj" />
```
```xml
<!-- in /tests/ folder, after EntityFramework.Migrations.Tests.csproj -->
<Project Path="tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests.csproj" />
```

- [ ] **Step 9: Stage**

```bash
git -C Urdarbrunnr add \
  Urdarbrunnr.slnx \
  src/EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj \
  src/EntityFramework.Migrations.PostgreSQL.Generator/MigrationContributorGenerator.cs \
  src/EntityFramework.Migrations.PostgreSQL/EntityFramework.Migrations.PostgreSQL.csproj \
  tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests.csproj \
  tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/MigrationContributorGeneratorTests.cs
```

---

## SHIP GATE — Urdarbrunnr

**STOP. Do not start Task 7 until this gate is cleared.**

1. Commit and push all five Urdarbrunnr projects (`EntityFramework`, `EntityFramework.PostgreSQL`, `EntityFramework.Migrations`, `EntityFramework.Migrations.PostgreSQL`, `EntityFramework.Migrations.PostgreSQL.Generator`) and their tests.
2. Confirm GitHub CI is green on Urdarbrunnr's repo.
3. Merge and tag (e.g., `v0.1.0`).
4. Confirm `Norse.EntityFramework`, `Norse.EntityFramework.PostgreSQL`, `Norse.EntityFramework.Migrations`, and `Norse.EntityFramework.Migrations.PostgreSQL` are all published to the NuGet feed. The generator DLL must be present in the `Norse.EntityFramework.Migrations.PostgreSQL` package's `analyzers/dotnet/cs/` folder — verify with `dotnet nuget locals all --list` or inspect the `.nupkg` contents with 7-Zip/NuGet Package Explorer before marking this gate clear.

---

## Task 7: Himinbjörg — `Norse.Identity` (entities + DbContext + UserStore)

**Files:**
- Create: `Himinbjorg/Himinbjorg.slnx`
- Create: `Himinbjorg/src/Identity/Identity.csproj`
- Create: `Himinbjorg/src/Identity/NorseUser.cs` … `NorseUserPasskey.cs` (8 entity files)
- Create: `Himinbjorg/src/Identity/NorseIdentityDbContext.cs`
- Create: `Himinbjorg/src/Identity/NorseUserStore.cs`
- Create: `Himinbjorg/src/Identity/IdentityBuilderExtensions.cs`
- Create: `Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj`
- Create: `Himinbjorg/tests/Identity.Tests/NorseUserStoreTests.cs`
- Modify: `Bifrost.slnx`

**Interfaces:**
- Consumes: `Norse.EntityFramework.NorseDbContextOptionsExtensions.ApplyNorseConventions` (Task 3)
- Produces:
  - 8 sealed entity types (`NorseUser`, `NorseRole`, `NorseUserClaim`, `NorseUserRole`, `NorseUserLogin`, `NorseUserToken`, `NorseRoleClaim`, `NorseUserPasskey`) — each extending its `IdentityXxx<Guid>` base with no added properties.
  - `NorseIdentityDbContext` — sealed `IdentityDbContext<..., Guid, ...>` with OpenIddict stores; applies `ApplyNorseConventions` in `OnModelCreating`.
  - `NorseUserStore` — sealed `UserStore<..., NorseUserPasskey>(context, describer)` with projection overrides.
  - `IdentityBuilderExtensions.AddNorseIdentity(this IServiceCollection)` — wires `AddIdentity`, `AddUserStore<NorseUserStore>`, `AddEntityFrameworkStores<NorseIdentityDbContext>`, and OpenIddict DI.

- [ ] **Step 1: Create solution and project files**

`Himinbjorg/Himinbjorg.slnx`:
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
		<Project Path="src/Identity/Identity.csproj" />
		<Project Path="src/Identity.Migrations/Identity.Migrations.csproj" />
	</Folder>
	<Folder Name="/tests/">
		<File Path="tests/Directory.Build.props" />
		<File Path="tests/Directory.Build.targets" />
		<Project Path="tests/Identity.Tests/Identity.Tests.csproj" />
		<Project Path="tests/Identity.Migrations.Tests/Identity.Migrations.Tests.csproj" />
	</Folder>
</Solution>
```

`Himinbjorg/src/Identity/Identity.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Identity: ASP.NET Core Identity v3 entity types, NorseIdentityDbContext (Identity + OpenIddict), NorseUserStore with projection overrides, and DI extension. Runtime library — referenced by Norse.Auth.Server; never by migration tooling.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" />
		<PackageReference Include="OpenIddict.EntityFrameworkCore" />
	</ItemGroup>
	<ItemGroup>
		<NorseRef Include="EntityFramework">
			<Repo>Urdarbrunnr</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

`Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="*" />
		<PackageReference Include="NSubstitute" />
		<ProjectReference Include="../../src/Identity/Identity.csproj" />
	</ItemGroup>
	<ItemGroup>
		<!--
			SQLitePCLRaw.lib.e_sqlite3 (transitive via Microsoft.EntityFrameworkCore.Sqlite) has a known
			high-severity vulnerability with no patched release. Exposure is test-only (in-memory). Revisit
			when SQLitePCLRaw publishes a fix.
		-->
		<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-2m69-gcr7-jv3q" />
	</ItemGroup>
</Project>
```

Note: SQLite is required here because `NorseIdentityDbContext.OnConfiguring` calls `NorseDbContextOptionsExtensions.ApplyNorseConventions(optionsBuilder)` which calls `UseSnakeCaseNamingConvention()`. The InMemory provider does not support relational naming conventions.

- [ ] **Step 2: Write the failing test**

`Himinbjorg/tests/Identity.Tests/NorseUserStoreTests.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Norse.Identity.Tests;

public sealed class NorseUserStoreTests
{
	[Fact]
	public async Task FindByIdAsync_returns_null_for_missing_user()
	{
		var ctx = CreateContext();
		var store = new NorseUserStore(ctx, new IdentityErrorDescriber());

		var result = await store.FindByIdAsync(Guid.NewGuid().ToString(), CancellationToken.None);

		result.ShouldBeNull();
	}

	[Fact]
	public async Task FindByIdAsync_projects_required_fields()
	{
		var ctx = CreateContext();
		var userId = Guid.NewGuid();
		ctx.Users.Add(new NorseUser
		{
			Id = userId,
			UserName = "test@example.com",
			Email = "test@example.com",
			NormalizedUserName = "TEST@EXAMPLE.COM",
			NormalizedEmail = "TEST@EXAMPLE.COM",
			SecurityStamp = "stamp",
			ConcurrencyStamp = "stamp"
		});
		await ctx.SaveChangesAsync();

		var store = new NorseUserStore(ctx, new IdentityErrorDescriber());
		var result = await store.FindByIdAsync(userId.ToString(), CancellationToken.None);

		result.ShouldNotBeNull();
		result.Id.ShouldBe(userId);
		result.UserName.ShouldBe("test@example.com");
		result.Email.ShouldBe("test@example.com");
	}

	static NorseIdentityDbContext CreateContext()
	{
		var ctx = new NorseIdentityDbContext(
			new DbContextOptionsBuilder<NorseIdentityDbContext>()
				.UseSqlite($"Data Source=:memory:;Mode=Memory;Cache=Shared;URI={Guid.NewGuid()}")
				.Options);
		ctx.Database.EnsureCreated();
		return ctx;
	}
}
```

- [ ] **Step 3: Run the test to confirm it fails**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj
```

Expected: compile error — types not defined.

- [ ] **Step 4: Create all 8 entity files**

`Himinbjorg/src/Identity/NorseUser.cs`:
```csharp
using Microsoft.AspNetCore.Identity;

namespace Norse.Identity;

public sealed class NorseUser : IdentityUser<Guid>;
```

`Himinbjorg/src/Identity/NorseRole.cs`:
```csharp
using Microsoft.AspNetCore.Identity;

namespace Norse.Identity;

public sealed class NorseRole : IdentityRole<Guid>;
```

`Himinbjorg/src/Identity/NorseUserClaim.cs`:
```csharp
using Microsoft.AspNetCore.Identity;

namespace Norse.Identity;

public sealed class NorseUserClaim : IdentityUserClaim<Guid>;
```

`Himinbjorg/src/Identity/NorseUserRole.cs`:
```csharp
using Microsoft.AspNetCore.Identity;

namespace Norse.Identity;

public sealed class NorseUserRole : IdentityUserRole<Guid>;
```

`Himinbjorg/src/Identity/NorseUserLogin.cs`:
```csharp
using Microsoft.AspNetCore.Identity;

namespace Norse.Identity;

public sealed class NorseUserLogin : IdentityUserLogin<Guid>;
```

`Himinbjorg/src/Identity/NorseUserToken.cs`:
```csharp
using Microsoft.AspNetCore.Identity;

namespace Norse.Identity;

public sealed class NorseUserToken : IdentityUserToken<Guid>;
```

`Himinbjorg/src/Identity/NorseRoleClaim.cs`:
```csharp
using Microsoft.AspNetCore.Identity;

namespace Norse.Identity;

public sealed class NorseRoleClaim : IdentityRoleClaim<Guid>;
```

`Himinbjorg/src/Identity/NorseUserPasskey.cs`:
```csharp
using Microsoft.AspNetCore.Identity;

namespace Norse.Identity;

public sealed class NorseUserPasskey : IdentityUserPasskey<Guid>;
```

- [ ] **Step 5: Create `NorseIdentityDbContext`**

`Himinbjorg/src/Identity/NorseIdentityDbContext.cs`:
```csharp
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Norse.EntityFramework;

namespace Norse.Identity;

public sealed class NorseIdentityDbContext(DbContextOptions<NorseIdentityDbContext> options)
	: IdentityDbContext<
		NorseUser, NorseRole, Guid,
		NorseUserClaim, NorseUserRole, NorseUserLogin,
		NorseUserToken, NorseRoleClaim, NorseUserPasskey>(options), INorseDbContext
{
	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		base.OnConfiguring(optionsBuilder);
		NorseDbContextOptionsExtensions.ApplyNorseConventions(optionsBuilder);
	}

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.UseOpenIddict<Guid>();
	}
}
```

Note: `NorseIdentityDbContext` cannot inherit `NorseDbContext` (it must inherit `IdentityDbContext`), so it overrides `OnConfiguring` directly to call `ApplyNorseConventions`. This ensures snake_case naming whether the context is constructed via DI, `AddNorsePostgresContext`, or the design-time factory — the factory does not need to call `UseSnakeCaseNamingConvention()` separately.

- [ ] **Step 6: Create `NorseUserStore`**

`Himinbjorg/src/Identity/NorseUserStore.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Norse.Identity;

public sealed class NorseUserStore(NorseIdentityDbContext context, IdentityErrorDescriber describer)
	: UserStore<NorseUser, NorseRole, NorseIdentityDbContext, Guid,
		NorseUserClaim, NorseUserRole, NorseUserLogin,
		NorseUserToken, NorseRoleClaim, NorseUserPasskey>(context, describer)
{
	public override Task<NorseUser?> FindByIdAsync(string userId, CancellationToken ct = default)
	{
		var id = Guid.Parse(userId);
		return Users
			.Where(u => u.Id == id)
			.Select(u => new NorseUser
			{
				Id = u.Id,
				UserName = u.UserName,
				NormalizedUserName = u.NormalizedUserName,
				Email = u.Email,
				NormalizedEmail = u.NormalizedEmail,
				SecurityStamp = u.SecurityStamp,
				ConcurrencyStamp = u.ConcurrencyStamp
			})
			.SingleOrDefaultAsync(ct);
	}
}
```

- [ ] **Step 7: Create `IdentityBuilderExtensions`**

`Himinbjorg/src/Identity/IdentityBuilderExtensions.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Identity;

public static class IdentityBuilderExtensions
{
	public static IServiceCollection AddNorseIdentity(this IServiceCollection services)
	{
		services
			.AddIdentity<NorseUser, NorseRole>()
			.AddUserStore<NorseUserStore>()
			.AddEntityFrameworkStores<NorseIdentityDbContext>()
			.AddDefaultTokenProviders();

		services
			.AddOpenIddict()
			.AddCore(o => o
				.UseEntityFrameworkCore()
				.UseDbContext<NorseIdentityDbContext>()
				.ReplaceDefaultEntities<Guid>());

		return services;
	}
}
```

- [ ] **Step 8: Run the tests to confirm they pass**

```bash
dotnet test Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj
```

Expected: PASS. (The `NorseIdentityDbContext` in-memory provider supports LINQ; the OpenIddict `UseOpenIddict()` call is guarded by a `try/catch` if it throws on in-memory — if OpenIddict rejects in-memory, instantiate `NorseIdentityDbContext` without `UseOpenIddict` in the test factory.)

**Note:** If OpenIddict's `UseOpenIddict()` call requires a relational provider in `OnModelCreating`, override the test context factory to use a minimal context without OpenIddict:
```csharp
static NorseIdentityDbContext CreateContext() =>
	new TestIdentityDbContext(new DbContextOptionsBuilder<NorseIdentityDbContext>()
		.UseInMemoryDatabase(Guid.NewGuid().ToString())
		.Options);
```
Where `TestIdentityDbContext` is a subclass that skips the OpenIddict call. If no override is needed, proceed as written.

- [ ] **Step 9: Add Himinbjörg to `Bifrost.slnx`**

Add the following folder block after the `/EntityFramework/` folders:

```xml
<Folder Name="/Identity/">
	<File Path="Himinbjorg/.editorconfig" />
	<File Path="Himinbjorg/.gitattributes" />
	<File Path="Himinbjorg/.gitignore" />
	<File Path="Himinbjorg/CLAUDE.md" />
	<File Path="Himinbjorg/Directory.Build.props" />
	<File Path="Himinbjorg/global.json" />
	<File Path="Himinbjorg/Himinbjorg.slnx" />
	<File Path="Himinbjorg/LICENSE" />
	<File Path="Himinbjorg/nuget.config" />
	<File Path="Himinbjorg/README.md" />
</Folder>
<Folder Name="/Identity/src/">
	<File Path="Himinbjorg/src/Directory.Build.props" />
	<File Path="Himinbjorg/src/Directory.Build.targets" />
	<Project Path="Himinbjorg/src/Identity/Identity.csproj" />
	<Project Path="Himinbjorg/src/Identity.Migrations/Identity.Migrations.csproj" />
</Folder>
<Folder Name="/Identity/tests/">
	<File Path="Himinbjorg/tests/Directory.Build.props" />
	<Project Path="Himinbjorg/tests/Identity.Tests/Identity.Tests.csproj" />
	<Project Path="Himinbjorg/tests/Identity.Migrations.Tests/Identity.Migrations.Tests.csproj" />
</Folder>
```

- [ ] **Step 10: Stage**

```bash
git -C Himinbjorg add \
  Himinbjorg.slnx \
  src/Identity/Identity.csproj \
  src/Identity/NorseUser.cs \
  src/Identity/NorseRole.cs \
  src/Identity/NorseUserClaim.cs \
  src/Identity/NorseUserRole.cs \
  src/Identity/NorseUserLogin.cs \
  src/Identity/NorseUserToken.cs \
  src/Identity/NorseRoleClaim.cs \
  src/Identity/NorseUserPasskey.cs \
  src/Identity/NorseIdentityDbContext.cs \
  src/Identity/NorseUserStore.cs \
  src/Identity/IdentityBuilderExtensions.cs \
  tests/Identity.Tests/Identity.Tests.csproj \
  tests/Identity.Tests/NorseUserStoreTests.cs
git add Bifrost.slnx
```

---

## Task 8: Himinbjörg — `Norse.Identity.Migrations` (contributor + factory + EF migrations)

**Files:**
- Create: `Himinbjorg/src/Identity.Migrations/Identity.Migrations.csproj`
- Create: `Himinbjorg/src/Identity.Migrations/NorseIdentityMigrationContributor.cs`
- Create: `Himinbjorg/src/Identity.Migrations/NorseIdentityDbContextFactory.cs`
- Run: `dotnet ef migrations add InitialCreate` — populates `Migrations/` folder
- Create: `Himinbjorg/tests/Identity.Migrations.Tests/Identity.Migrations.Tests.csproj`
- Create: `Himinbjorg/tests/Identity.Migrations.Tests/NorseIdentityMigrationContributorTests.cs`

**Interfaces:**
- Consumes: `NorseIdentityDbContext` (Task 7), `EfMigrationContributor<TContext>` + `MigrationConnectionStringAttribute` (Task 5)
- Produces:
  - `NorseIdentityMigrationContributor` — `Name = "Norse.Identity"`; decorated with `[MigrationConnectionString("norse_identity")]`; the generator reads this type.
  - `NorseIdentityDbContextFactory` — `IDesignTimeDbContextFactory<NorseIdentityDbContext>` reading `DOTNET_EFTOOLS_CONNECTIONSTRING` env var; used only by `dotnet ef` at design time.
  - `Migrations/` folder with `InitialCreate` migration (full ASP.NET Core Identity v3 + OpenIddict + passkey schema).

- [ ] **Step 1: Create the project file**

`Himinbjorg/src/Identity.Migrations/Identity.Migrations.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Identity.Migrations: migration contributor, IDesignTimeDbContextFactory, and the EF migration files for NorseIdentityDbContext. Migration tooling only — never referenced from a runtime container.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.EntityFrameworkCore.Design">
			<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
		</PackageReference>
	</ItemGroup>
	<ItemGroup>
		<ProjectReference Include="../Identity/Identity.csproj" />
	</ItemGroup>
	<ItemGroup>
		<NorseRef Include="EntityFramework.Migrations">
			<Repo>Urdarbrunnr</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

`Himinbjorg/tests/Identity.Migrations.Tests/Identity.Migrations.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<ProjectReference Include="../../src/Identity.Migrations/Identity.Migrations.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing test**

`Himinbjorg/tests/Identity.Migrations.Tests/NorseIdentityMigrationContributorTests.cs`:
```csharp
using Norse.EntityFramework.Migrations;

namespace Norse.Identity.Migrations.Tests;

public sealed class NorseIdentityMigrationContributorTests
{
	[Fact]
	public void Name_returns_Norse_Identity()
	{
		var attr = typeof(NorseIdentityMigrationContributor)
			.GetCustomAttributes(typeof(MigrationConnectionStringAttribute), false)
			.Cast<MigrationConnectionStringAttribute>()
			.Single();

		attr.ConnectionStringName.ShouldBe("norse_identity");
		// Name check deferred: constructor requires a real DbContext
	}

	[Fact]
	public void Contributor_is_annotated_with_connection_string_attribute()
	{
		var attr = typeof(NorseIdentityMigrationContributor)
			.GetCustomAttributes(typeof(MigrationConnectionStringAttribute), false);

		attr.ShouldNotBeEmpty();
	}
}
```

- [ ] **Step 3: Run the test to confirm it fails**

```bash
dotnet test Himinbjorg/tests/Identity.Migrations.Tests/Identity.Migrations.Tests.csproj
```

Expected: compile error — `NorseIdentityMigrationContributor` not defined.

- [ ] **Step 4: Implement `NorseIdentityMigrationContributor`**

`Himinbjorg/src/Identity.Migrations/NorseIdentityMigrationContributor.cs`:
```csharp
using Norse.EntityFramework.Migrations;

namespace Norse.Identity.Migrations;

[MigrationConnectionString("norse_identity")]
public sealed class NorseIdentityMigrationContributor(NorseIdentityDbContext context)
	: EfMigrationContributor<NorseIdentityDbContext>(context)
{
	public override string Name => "Norse.Identity";
}
```

- [ ] **Step 5: Implement `NorseIdentityDbContextFactory`**

`Himinbjorg/src/Identity.Migrations/NorseIdentityDbContextFactory.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Norse.Identity.Migrations;

public sealed class NorseIdentityDbContextFactory : IDesignTimeDbContextFactory<NorseIdentityDbContext>
{
	public NorseIdentityDbContext CreateDbContext(string[] args)
	{
		var connectionString =
			Environment.GetEnvironmentVariable("DOTNET_EFTOOLS_CONNECTIONSTRING")
			?? "Host=localhost;Port=5432;Database=norse_identity;Username=postgres;Password=devpassword";

		var options = new DbContextOptionsBuilder<NorseIdentityDbContext>()
			.UseNpgsql(connectionString,
				o => o.MigrationsAssembly(typeof(NorseIdentityDbContextFactory).Assembly.GetName().Name))
			.Options;

		return new NorseIdentityDbContext(options);
	}
}
```

- [ ] **Step 6: Run the tests to confirm they pass**

```bash
dotnet test Himinbjorg/tests/Identity.Migrations.Tests/Identity.Migrations.Tests.csproj
```

Expected: PASS.

- [ ] **Step 7: Generate the initial EF migration**

Ensure the primary Postgres container is running (start it from the AppHost if needed, or run `docker run -d -e POSTGRES_PASSWORD=devpassword -p 5432:5432 postgres:19beta1-trixie`). Then:

```bash
dotnet tool restore
dotnet ef migrations add InitialCreate \
  --project Himinbjorg/src/Identity.Migrations/Identity.Migrations.csproj \
  --startup-project Himinbjorg/src/Identity.Migrations/Identity.Migrations.csproj \
  --output-dir Migrations
```

If `dotnet ef` is not in `dotnet-tools.json`, install it first:

```bash
dotnet tool install dotnet-ef --create-manifest-if-needed
dotnet tool restore
```

Expected output: `Build succeeded. Done. To undo this action, use 'ef migrations remove'`

Verify that `Himinbjorg/src/Identity.Migrations/Migrations/` contains `{timestamp}_InitialCreate.cs` and `NorseIdentityDbContextModelSnapshot.cs`.

- [ ] **Step 8: Stage**

```bash
git -C Himinbjorg add \
  src/Identity.Migrations/Identity.Migrations.csproj \
  src/Identity.Migrations/NorseIdentityMigrationContributor.cs \
  src/Identity.Migrations/NorseIdentityDbContextFactory.cs \
  src/Identity.Migrations/Migrations/ \
  tests/Identity.Migrations.Tests/Identity.Migrations.Tests.csproj \
  tests/Identity.Migrations.Tests/NorseIdentityMigrationContributorTests.cs
```

---

## SHIP GATE — Himinbjörg

**STOP. Do not start Task 8 until this gate is cleared.**

1. Commit and push all Himinbjörg files — both `Identity` and `Identity.Migrations` projects plus their tests.
2. Confirm GitHub CI is green on Himinbjörg's repo.
3. Merge and tag (e.g., `v0.1.0`).
4. Confirm both `Norse.Identity` and `Norse.Identity.Migrations` are published to the NuGet feed.

At this point **all four upstream realms have live NuGet packages.** Task 9 (Yggdrasil) and Task 10 (Bifrost) will run in Bifrost with `UseProjectReferences=true` (ProjectReference mode), but every NorseRef now also resolves via PackageReference — the toggle flip in Task 10 step 9 is real verification against the published packages.

---

## Task 9: Yggdrasil — Wire the Migrations Service

**Files:**
- Modify: `Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj`
- Modify: `Yggdrasil/src/Hosting.Migrations.Service/Program.cs`
- Delete: `Yggdrasil/src/Hosting.Migrations.Service/Placeholder.cs`
- Modify: `Bifrost.slnx` — add Yggdrasil realm folders

**Interfaces:**
- Consumes: `AddNorseMigrations()` (generated from Tasks 5+6+8 via `Norse.EntityFramework.Migrations.PostgreSQL`), `Norse.Infrastructure` (Task 2), `Norse.Identity.Migrations` (Task 8)
- Produces: compilable migrations service with three-line `Program.cs`

- [ ] **Step 1: Update the project file**

`Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
	<PropertyGroup>
		<ContainerBaseImage>mcr.microsoft.com/dotnet/runtime:$(ContainerVersion)</ContainerBaseImage>
		<Description>Norse.Hosting.Migrations.Service: the init-container that runs all IMigrationContributor implementations to completion and exits. Program.cs is three lines and contains no reference to any contributor type.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.Extensions.Hosting" />
	</ItemGroup>
	<ItemGroup>
		<NorseRef Include="EntityFramework.Migrations.PostgreSQL">
			<Repo>Urdarbrunnr</Repo>
		</NorseRef>
		<NorseRef Include="Infrastructure.Migrations">
			<Repo>Midgard</Repo>
		</NorseRef>
		<NorseRef Include="Identity.Migrations">
			<Repo>Himinbjorg</Repo>
		</NorseRef>
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Replace `Program.cs`**

`Yggdrasil/src/Hosting.Migrations.Service/Program.cs`:
```csharp
Console.Title = "Norse Migrations Service";
var builder = Host.CreateApplicationBuilder(args);
builder.AddNorseMigrations();
await builder.Build().RunAsync().ConfigureAwait(false);
```

- [ ] **Step 3: Delete `Placeholder.cs`**

```bash
git -C Yggdrasil rm src/Hosting.Migrations.Service/Placeholder.cs
```

- [ ] **Step 4: Verify the service compiles**

```bash
dotnet build Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj
```

Expected: `Build succeeded.` If `AddNorseMigrations` is not found, verify the generator is wired as an analyzer reference in `Norse.EntityFramework.Migrations.PostgreSQL` and that the NorseRef to `Urdarbrunnr/EntityFramework.Migrations.PostgreSQL` resolves correctly via the `Bifrost/Directory.Build.targets` Choose block.

- [ ] **Step 5: Confirm `Program.cs` contains zero references to Identity or contributor types**

```bash
grep -n "Identity\|Contributor\|OpenIddict" Yggdrasil/src/Hosting.Migrations.Service/Program.cs
```

Expected: no output (zero matches).

- [ ] **Step 6: Add Yggdrasil to `Bifrost.slnx`**

Add the following folder block after the `/Identity/` folders:

```xml
<Folder Name="/Hosting/">
	<File Path="Yggdrasil/.editorconfig" />
	<File Path="Yggdrasil/.gitattributes" />
	<File Path="Yggdrasil/.gitignore" />
	<File Path="Yggdrasil/CLAUDE.md" />
	<File Path="Yggdrasil/Directory.Build.props" />
	<File Path="Yggdrasil/global.json" />
	<File Path="Yggdrasil/LICENSE" />
	<File Path="Yggdrasil/nuget.config" />
	<File Path="Yggdrasil/README.md" />
	<File Path="Yggdrasil/Yggdrasil.slnx" />
</Folder>
<Folder Name="/Hosting/src/">
	<File Path="Yggdrasil/src/Directory.Build.props" />
	<File Path="Yggdrasil/src/Directory.Build.targets" />
	<Project Path="Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj" />
	<Project Path="Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj" />
	<Project Path="Yggdrasil/src/Hosting.Worker/Hosting.Worker.csproj" />
</Folder>
<Folder Name="/Hosting/tests/">
	<File Path="Yggdrasil/tests/Directory.Build.props" />
	<Project Path="Yggdrasil/tests/Hosting.Migrations.Service.Tests/Hosting.Migrations.Service.Tests.csproj" />
	<Project Path="Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj" />
	<Project Path="Yggdrasil/tests/Hosting.Worker.Tests/Hosting.Worker.Tests.csproj" />
</Folder>
```

- [ ] **Step 7: Stage**

```bash
git -C Yggdrasil add \
  src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj \
  src/Hosting.Migrations.Service/Program.cs
git add Bifrost.slnx
```

---

## Task 10: Bifrost — Postgres Primary+Replica + AppHost Wiring

**Files:**
- Create: `src/Orchestration.AppHost/postgres/replication-hba.sh`
- Create: `src/Orchestration.AppHost/postgres/replica-entrypoint.sh`
- Modify: `src/Orchestration.AppHost/AppHost.cs`
- Modify: `src/Orchestration.AppHost/Orchestration.AppHost.csproj`
- Modify: `.gitattributes` (root or AppHost-level)

**Interfaces:**
- Consumes: Yggdrasil `Norse.Hosting.Migrations.Service` (Task 9), Postgres 19 beta1 container
- Produces: running Aspire AppHost with `pg-primary` (5432), `pg-replica` (5433), `migrations` service completing and exiting 0; `norse_identity` database with full Identity + OpenIddict schema visible in DataGrip.

- [ ] **Step 1: Add `.sh` eol=lf to `.gitattributes`**

At the Bifrost root `.gitattributes` (create if absent), add:
```
*.sh text eol=lf
```

- [ ] **Step 2: Create the replication HBA init script**

`src/Orchestration.AppHost/postgres/replication-hba.sh`:
```bash
#!/bin/bash
# Appended by initdb to pg_hba.conf on the primary's first start.
# Allows pg_basebackup on the replica to connect via scram-sha-256.
# Runs once (/docker-entrypoint-initdb.d/); the server reads the updated
# pg_hba.conf on start after initdb completes.
set -e
cat >> "$PGDATA/pg_hba.conf" <<'EOF'
host replication all all scram-sha-256
EOF
```

- [ ] **Step 3: Create the replica entrypoint script**

`src/Orchestration.AppHost/postgres/replica-entrypoint.sh`:
```bash
#!/bin/bash
# On first start (no standby.signal), clones the primary via pg_basebackup.
# On subsequent starts, resumes streaming replication from existing PGDATA.
# Retries until the primary is accepting replication connections.
set -e
export PGDATA=/var/lib/postgresql/data

if [ ! -s "$PGDATA/standby.signal" ]; then
  echo "replica: cloning primary via pg_basebackup..."
  rm -rf "$PGDATA"/*
  until PGPASSWORD="$POSTGRES_PASSWORD" pg_basebackup \
    -h host.docker.internal -p 5432 -U postgres \
    -D "$PGDATA" -Fp -Xs -R -w -P; do
    echo "replica: primary not ready, retrying in 2s..."
    sleep 2
  done
  chown -R postgres:postgres "$PGDATA"
  chmod 700 "$PGDATA"
fi

exec gosu postgres postgres
```

**Note on `host.docker.internal`:** This resolves to the Docker host on Windows (Docker Desktop) and macOS. On Linux Docker hosts without the `host.docker.internal` mapping, replace with the `pg-primary` container name if Aspire's internal network resolves container names. Test empirically on first run: if cloning fails with "could not translate host name", try the container name `pg-primary` as the `-h` argument. See spec §7.

- [ ] **Step 4: Update the AppHost project file**

`src/Orchestration.AppHost/Orchestration.AppHost.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<Sdk Name="Aspire.AppHost.Sdk" Version="13.4.3" />

	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<TargetFramework>net11.0</TargetFramework>
		<Nullable>enable</Nullable>
		<ImplicitUsings>enable</ImplicitUsings>
		<UserSecretsId>norse-orchestration-apphost</UserSecretsId>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="Aspire.Hosting.AppHost" Version="*-*" />
	</ItemGroup>

	<ItemGroup>
		<ProjectReference Include="../../Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj" />
	</ItemGroup>

</Project>
```

- [ ] **Step 5: Rewrite `AppHost.cs`**

`src/Orchestration.AppHost/AppHost.cs`:
```csharp
Console.Title = "Norse Architecture — Aspire App Host";

var builder = DistributedApplication.CreateBuilder(args);

var pgPassword = builder.AddParameter("postgres-password", secret: true);

var pgPrimary = builder
	.AddPostgres("pg-primary", password: pgPassword, port: 5432)
	.WithImageTag("19beta1-trixie")
	.WithDataVolume("norse-pg-primary")
	.WithContainerRuntimeArgs(
		"-c", "wal_level=replica",
		"-c", "max_wal_senders=10",
		"-c", "max_replication_slots=10",
		"-c", "hot_standby=on")
	.WithBindMount("postgres/replication-hba.sh",
		"/docker-entrypoint-initdb.d/01-replication-hba.sh",
		isReadOnly: true)
	.WithEndpoint(port: 5432, targetPort: 5432, name: "tcp", isProxied: false)
	.WithPgAdmin(container => container
		.WithUrlForEndpoint("http", static url => url.DisplayText = "pgAdmin"));

var norseIdentity = pgPrimary.AddDatabase("norse_identity");

builder
	.AddContainer("pg-replica", "postgres", "19beta1-trixie")
	.WithDataVolume("norse-pg-replica")
	.WithEnvironment("POSTGRES_PASSWORD", pgPassword)
	.WithBindMount("postgres/replica-entrypoint.sh", "/entrypoint.sh", isReadOnly: true)
	.WithEntrypoint("/bin/bash")
	.WithArgs("/entrypoint.sh")
	.WithEndpoint(port: 5433, targetPort: 5432, name: "tcp", isProxied: false)
	.WaitFor(pgPrimary);

builder
	.AddProject<Projects.Hosting_Migrations_Service>("migrations")
	.WithReference(norseIdentity)
	.WaitFor(norseIdentity);

await builder.Build().RunAsync().ConfigureAwait(false);
```

**Note on Aspire API calls:** `WithContainerRuntimeArgs`, `WithEntrypoint`, `WithArgs` extension method availability depends on Aspire 13.x. If any of these are not present on the fluent builder, check the Aspire 13.x release notes for the correct method names. The `WithEndpoint(port, targetPort, name, isProxied)` overload may differ — consult IntelliSense. The intent is: unproxied fixed host port, direct connection regardless of AppHost state.

- [ ] **Step 6: Build the AppHost to verify compilation**

```bash
dotnet build src/Orchestration.AppHost/Orchestration.AppHost.csproj
```

Expected: `Build succeeded.` If `Projects.Hosting_Migrations_Service` is not found, confirm the `<ProjectReference>` to the Yggdrasil migrations service was added in Step 4 and the Aspire tooling has regenerated the `Projects` static class.

- [ ] **Step 7: Run the AppHost and verify the Aspire dashboard**

```bash
dotnet run --project src/Orchestration.AppHost/Orchestration.AppHost.csproj
```

Open the Aspire dashboard URL (printed to console). Verify:
- `pg-primary` shows **Running** (persistent; green health).
- `pg-replica` shows **Running**; first-start log contains `"replica: cloning primary via pg_basebackup..."`.
- `migrations` shows **Finished** (exit 0) — green checkmark.

- [ ] **Step 8: Verify the schema in DataGrip**

Connect DataGrip to `localhost:5432` with user `postgres`, password `devpassword`.

Verify:
1. Database `norse_identity` exists.
2. Tables follow snake_case naming (e.g., `asp_net_users`, `asp_net_roles`, `openiddict_applications`, `openiddict_tokens`, `asp_net_user_passkeys`).
3. Connect to `localhost:5433`; run `SELECT now()` — returns successfully (replica is serving reads).
4. Run `SELECT * FROM asp_net_users LIMIT 1` on the replica — no error (schema replicated).

- [ ] **Step 9: Verify the `UseProjectReferences=false` toggle**

All four upstream realms are live on NuGet (ship gates cleared before Task 9 started). Restore and build in package mode:

```bash
dotnet restore Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj \
  -p:UseProjectReferences=false
dotnet build Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj \
  -p:UseProjectReferences=false --no-restore
```

Expected: `Build succeeded.` — the published `Norse.EntityFramework.Migrations.PostgreSQL` NuGet package includes the generator in `analyzers/dotnet/cs/`, which runs over the compilation and emits `AddNorseMigrations()` in the same form as the ProjectReference build.

Verify the generated source is identical to the ProjectReference build:

```bash
# Capture ProjectReference build output
dotnet build Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj \
  -p:UseProjectReferences=true 2>&1 | grep "NorseMigrationsGeneratedExtensions"

# Capture PackageReference build output
dotnet build Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj \
  -p:UseProjectReferences=false 2>&1 | grep "NorseMigrationsGeneratedExtensions"
```

If both builds succeed and the generated `NorseMigrationsExtensions.g.cs` in each `obj/` folder contains the same contributor registrations, the toggle is verified.

- [ ] **Step 10: Stage**

```bash
git add \
  src/Orchestration.AppHost/postgres/replication-hba.sh \
  src/Orchestration.AppHost/postgres/replica-entrypoint.sh \
  src/Orchestration.AppHost/AppHost.cs \
  src/Orchestration.AppHost/Orchestration.AppHost.csproj \
  .gitattributes
```

---

## Self-Review

**Spec coverage check:**

| Spec section | Task |
|---|---|
| §2.1 `IMigrationContributor` — no Order, no DependsOn | Task 1 |
| §2.2 `EfMigrationContributor<TContext>` in `Norse.EntityFramework.Migrations` | Task 5 |
| §2.3 Source-generated contributor registration; `UseProjectReferences` verification gate | Tasks 6, 10 step 9 |
| §2.4 `MigrationRunnerService`; `AddNorseMigrationsRunner()` | Task 2 |
| §2.5 Three-line `Program.cs` | Task 9 |
| §3.1 Eight entity types, `Guid` key, no extra properties | Task 7 |
| §3.2 `NorseUserStore` with projection overrides | Task 7 |
| §3.3 `NorseIdentityDbContext` fully generic; OpenIddict registered on same context | Task 7 |
| §3.5 Himinbjörg split: `Norse.Identity` runtime / `Norse.Identity.Migrations` tooling-only | Tasks 7, 8 |
| §3.6 `.NET 11` TimeProvider (picked up by `AddDefaultTokenProviders`); passkey type `NorseUserPasskey` included | Task 7 step 4 |
| §4 Bifrost AppHost: `WaitFor(pgPrimary)`; migrations service; no `CREATE DATABASE` | Task 10 |
| §6 Success criteria: dashboard exit 0, `norse_identity` schema, replica streaming confirmed | Task 10 steps 7–9 |
| §6 `UseProjectReferences=false` parity | Task 10 step 9 |
| §7 Open decisions: none | n/a |
| 2026-06-16 spec: PG primary+replica, pinned tag, scripts, named volumes, unproxied ports | Task 10 |

**Placeholder scan:** No TBD, no "implement later", no "similar to" references. The Aspire API note in Task 10 step 5 is a verification instruction, not a deferral. The in-memory provider note in Task 7 step 8 covers a known edge case.

**Type consistency:**
- `IMigrationContributor` → `MigrationRunnerService.contributors` ✓
- `EfMigrationContributor<NorseIdentityDbContext>` → `NorseIdentityMigrationContributor` ✓
- `NorseIdentityDbContext` → `NorseIdentityDbContextFactory` → `NorseIdentityMigrationContributor` ✓
- `MigrationConnectionString("norse_identity")` → generated `GetConnectionString("norse_identity")` → AppHost `.AddDatabase("norse_identity")` ✓
- `Projects.Hosting_Migrations_Service` → `Hosting.Migrations.Service.csproj` (Aspire type-name convention: `.` → `_`) ✓
- `AddNorseIdentity()` registers `NorseUserStore` and `NorseIdentityDbContext` ✓
- `AddNorseMigrationsRunner()` in `HostApplicationBuilderExtensions` matches the name the generator emits ✓
