# Seeding Framework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the seeding-framework chassis — `ISeedContributor`, `SeedRunnerService`, and generator support for discovering and wiring seed contributors — as a second phase of the existing migrations realm, proven with test-only stub contributors, exactly as `IMigrationContributor` shipped before Identity existed to consume it.

**Architecture:** Every new type lands inside an already-existing `.Migrations` project — no new assemblies anywhere. `ISeedContributor` (Asgard) mirrors `IMigrationContributor`'s shape exactly. `SeedRunnerService` (Midgard) mirrors `MigrationRunnerService` exactly, and becomes the sole owner of `IHostApplicationLifetime.StopApplication()` — a breaking change to already-shipped code. Both Postgres and SQL Server generators (Urdarbrunnr) gain seed-contributor discovery via the same shared, `<Compile Include>`-linked discovery file they already share for migration contributors, and both emit an `AddNorseSeedingRunner()` call alongside `AddNorseMigrationsRunner()`. Yggdrasil's `Program.cs` and `.csproj` do not change at all.

**Tech Stack:** .NET 10, C# 14, xUnit v3 (Microsoft.Testing.Platform), Shouldly, NSubstitute, Roslyn `IIncrementalGenerator` (netstandard2.0).

## Global Constraints

- **No new assemblies.** Every new type joins an existing `.Migrations`-family project: Asgard's `Abstractions.Migrations`, Midgard's `Infrastructure.Migrations`, Urdarbrunnr's `EntityFramework.Migrations.Generator.Shared` + both provider `.Generator` projects. Yggdrasil's `Hosting.Migrations.Service` gets zero changes.
- **Fail-loud, both phases.** Any contributor (migration or seed) that throws halts the host immediately, non-zero exit. No swallowed exceptions.
- **No `Order`, no `DependsOn` between seed contributors** — a contributor that needs internal ordering (e.g. roles before role-assignments) does that itself, inside its own `SeedAsync`, against its own context.
- **Idempotency is each contributor's own responsibility.** No shared ledger table; `SeedAsync` runs on every startup and is expected to check before it writes.
- **`DeterministicGuid` is NOT built in this plan.** The seeding-framework spec's §2.2 static-helper stopgap in Asgard is superseded — the real `Norse.Primitives.Identifiers.DeterministicGuid` struct already exists in Svartalfheim (RFC 9562 v5, shipped 2026-07-03). Consumers (future seed contributors, e.g. Mimisbrunnr's) reference Svartalfheim's `Norse.Primitives` directly. `Abstractions.Migrations` gains **no** dependency on Svartalfheim in this plan — `ISeedContributor`'s own definition never mentions `DeterministicGuid`.
- **Breaking change, deliberate:** `MigrationRunnerService` no longer calls `StopApplication()` and no longer takes an `IHostApplicationLifetime` constructor parameter at all (Task 3) — the parameter becomes fully unused once the call is removed, so it's deleted rather than left dead.
- **Style:** tabs; `var` for return assignments only, explicit type + `new()` for construction; `sealed` by default; accessibility modifiers omitted when default (test methods are bare `void`/`async Task`, no `public`); US English spelling throughout, including comments and commit messages.
- **Generated-source template uses `AppendCSharp`, not raw `StringBuilder.AppendLine` chains.** Per Buvy's own prior art (`buvinghausen/TaskTupleAwaiter`'s `TaskTupleExtensionsGenerator.cs` + `CSharpEmit.cs`), both generators (Tasks 4–5) gain a small `CSharpEmit.AppendCSharp(this StringBuilder sb, [StringSyntax("C#")] string code) => sb.AppendLine(code)` extension — identical at runtime, but the `[StringSyntax("C#")]` annotation makes VS/Rider syntax-highlight the raw-string content as C# at every call site. `BuildSource` becomes a sequence of `sb.AppendCSharp("""...""")` calls, each a readable, syntactically-shaped block, with `foreach` loops appending one interpolated per-item block each iteration — no escaped quotes, no line-by-line `AppendLine`. `StringSyntaxAttribute` itself needs a polyfill (both generator projects target `netstandard2.0`, predating .NET 7's BCL definition) — copied verbatim from the same prior art. Both the polyfill and `CSharpEmit` land in the shared `EntityFramework.Migrations.Generator.Shared` folder (loose `.cs` files, no `.csproj` of its own — confirmed by inspection), `<Compile Include>`-linked into both provider generator projects alongside the existing `MigrationContributorDiscovery.cs`, so one copy serves both. This replaces the existing `StringBuilder.AppendLine`-based style in the method being rewritten; it does not touch any other already-shipped file.
- **Ship-gate discipline:** each realm's tasks (Asgard → Midgard → Urdarbrunnr) are reviewed and can be merged/tagged/published independently, in that dependency order, mirroring the migrations framework's own six-realm rollout. Local development within this plan uses `UseProjectReferences=true` (the Bifrost default) so every task is immediately buildable and testable without waiting on an actual NuGet publish; the `UseProjectReferences=false` (PackageReference/CI) verification in Task 6 only succeeds once Asgard's and Midgard's packages are actually published — that's a real dependency, not a formality.

**Amendment (2026-07-25):** every `Norse.EntityFramework.*` reference in this plan (Tasks 4–5's generator code, namespaces, and `using`s) names Urðarbrunnr's namespace as it stood when this plan was written. It has since widened to `Norse.Persistence.EntityFramework.*` (PR #31, tag v0.0.4). Read every `Norse.EntityFramework` below as historical.

---

### Task 1: Asgard — `ISeedContributor`

**Files:**
- Create: `Asgard/src/Abstractions.Migrations/Seeding/ISeedContributor.cs`
- Modify: `Asgard/src/Abstractions.Migrations/Abstractions.Migrations.csproj`
- Modify: `Asgard/tests/Abstractions.Migrations.Tests/Abstractions.Migrations.Tests.csproj`
- Test: `Asgard/tests/Abstractions.Migrations.Tests/Seeding/ISeedContributorTests.cs`

**Interfaces:**
- Produces: `Norse.Abstractions.Migrations.Seeding.ISeedContributor` — `string Name { get; }`, `Task SeedAsync(CancellationToken cancellationToken)`, `static abstract void ConfigureServices(IServiceCollection services)`. This is what Task 2's `SeedRunnerService` and Task 4/5's generators consume.

- [ ] **Step 1: Write the failing test**

```csharp
// Asgard/tests/Abstractions.Migrations.Tests/Seeding/ISeedContributorTests.cs
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Abstractions.Migrations.Seeding.Tests;

public sealed class ISeedContributorTests
{
	[Fact]
	async Task SeedAsync_invokes_concrete_implementation()
	{
		StubSeedContributor stub = new();

		await stub.SeedAsync(CancellationToken.None);

		stub.Invoked.ShouldBeTrue();
	}

	[Fact]
	void Name_returns_concrete_value()
	{
		StubSeedContributor stub = new();

		stub.Name.ShouldBe("Stub");
	}

	[Fact]
	void ConfigureServices_is_callable_as_static_interface_member()
	{
		ServiceCollection services = new();

		StubSeedContributor.ConfigureServices(services);

		services.ShouldBeEmpty();
	}

	sealed class StubSeedContributor : ISeedContributor
	{
		public string Name => "Stub";
		public bool Invoked { get; private set; }

		public Task SeedAsync(CancellationToken cancellationToken)
		{
			Invoked = true;
			return Task.CompletedTask;
		}

		public static void ConfigureServices(IServiceCollection services) { }
	}
}
```

- [ ] **Step 2: Add the test-only DI package reference and run the test to verify it fails**

```xml
<!-- Asgard/tests/Abstractions.Migrations.Tests/Abstractions.Migrations.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="*" />
	</ItemGroup>
	<ItemGroup>
		<ProjectReference Include="../../src/Abstractions.Migrations/Abstractions.Migrations.csproj" />
	</ItemGroup>
</Project>
```

Run: `dotnet test Asgard/tests/Abstractions.Migrations.Tests/`
Expected: FAIL — `ISeedContributor` does not exist (CS0246).

- [ ] **Step 3: Add the DI abstractions package to the main project**

```xml
<!-- Asgard/src/Abstractions.Migrations/Abstractions.Migrations.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse migration contract: the IMigrationContributor interface (EF-free) — the single law governing migration contribution across all contexts. Not referenced by Worker or Web.Server; isolation enforced by the absence of a project reference. Also carries ISeedContributor, the second-phase seeding contract.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="*" />
	</ItemGroup>
</Project>
```

- [ ] **Step 4: Write the interface**

```csharp
// Asgard/src/Abstractions.Migrations/Seeding/ISeedContributor.cs
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Abstractions.Migrations.Seeding;

/// <summary>
/// Defines a contract for a seed contributor that bootstraps required data after all migrations
/// have completed.
/// </summary>
public interface ISeedContributor
{
	/// <summary>
	/// Gets the name of the seed contributor.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// Seeds data asynchronously. Invoked on every startup; the contributor is responsible for its
	/// own idempotency (e.g. checking whether a row already exists before writing it).
	/// </summary>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>A task representing the asynchronous seed operation.</returns>
	Task SeedAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Registers any services this contributor's <see cref="SeedAsync"/> needs beyond its own
	/// constructor-injected <c>DbContext</c> (e.g. <c>UserManager</c>, an OpenIddict application
	/// manager). Default is a no-op — a contributor that needs nothing beyond its own context
	/// doesn't need to override this at all.
	/// </summary>
	/// <param name="services">The service collection to register into.</param>
	static virtual void ConfigureServices(IServiceCollection services) { }
}
```

**Correction discovered during Task 2 (not in the original spec text):** this member cannot be `static abstract`. An interface with any `static abstract` member cannot be used as a generic type argument *anywhere* in the codebase — including `IEnumerable<ISeedContributor>`, which `SeedRunnerService`'s constructor requires — because the runtime has no way to resolve which concrete type's static method applies without a compile-time-known type (`CS8920`, verified with a standalone repro: identical interface shape compiles clean as `static virtual` with a default body, fails as `static abstract`). This trades away one guarantee: a contributor can now silently inherit the no-op default instead of being compiler-forced to declare `ConfigureServices` explicitly. There is no alternative — the restriction applies regardless of whether anything ever calls the member polymorphically.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test Asgard/tests/Abstractions.Migrations.Tests/`
Expected: PASS (3 tests)

- [ ] **Step 6: Commit**

```bash
git -C Asgard add src/Abstractions.Migrations/Seeding/ISeedContributor.cs src/Abstractions.Migrations/Abstractions.Migrations.csproj tests/Abstractions.Migrations.Tests/Seeding/ISeedContributorTests.cs tests/Abstractions.Migrations.Tests/Abstractions.Migrations.Tests.csproj
git -C Asgard commit -m "feat: add ISeedContributor, the seeding-phase contract"
```

---

### Task 2: Midgard — `SeedRunnerService` and `AddNorseSeedingRunner()`

**Files:**
- Create: `Midgard/src/Infrastructure.Migrations/SeedRunnerService.cs`
- Modify: `Midgard/src/Infrastructure.Migrations/HostApplicationBuilderExtensions.cs`
- Test: `Midgard/tests/Infrastructure.Migrations.Tests/SeedRunnerServiceTests.cs`

**Interfaces:**
- Consumes: `Norse.Abstractions.Migrations.Seeding.ISeedContributor` (Task 1).
- Produces: `Norse.Infrastructure.Migrations.SeedRunnerService` (an `IHostedService`); `HostApplicationBuilderExtensions.AddNorseSeedingRunner(this IHostApplicationBuilder builder)`. Task 3's ordering test and Task 4/5's generated code both call `AddNorseSeedingRunner()`.

- [ ] **Step 1: Write the failing test**

```csharp
// Midgard/tests/Infrastructure.Migrations.Tests/SeedRunnerServiceTests.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Norse.Abstractions.Migrations.Seeding;
using NSubstitute;

namespace Norse.Infrastructure.Migrations.Tests;

public sealed class SeedRunnerServiceTests
{
	[Fact]
	async Task StartAsync_runs_all_contributors_and_stops_application()
	{
		var contributor = Substitute.For<ISeedContributor>();
		contributor.Name.Returns("Test");
		contributor.SeedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var lifetime = Substitute.For<IHostApplicationLifetime>();
		SeedRunnerService sut = new(
			[contributor],
			lifetime,
			NullLogger<SeedRunnerService>.Instance);

		await sut.StartAsync(CancellationToken.None);

		await contributor.Received(1).SeedAsync(Arg.Any<CancellationToken>());
		lifetime.Received(1).StopApplication();
	}

	[Fact]
	async Task StartAsync_with_multiple_contributors_runs_all()
	{
		var a = Substitute.For<ISeedContributor>();
		a.Name.Returns("A");
		a.SeedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var b = Substitute.For<ISeedContributor>();
		b.Name.Returns("B");
		b.SeedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var lifetime = Substitute.For<IHostApplicationLifetime>();
		SeedRunnerService sut = new(
			[a, b],
			lifetime,
			NullLogger<SeedRunnerService>.Instance);

		await sut.StartAsync(CancellationToken.None);

		await a.Received(1).SeedAsync(Arg.Any<CancellationToken>());
		await b.Received(1).SeedAsync(Arg.Any<CancellationToken>());
		lifetime.Received(1).StopApplication();
	}

	[Fact]
	async Task StartAsync_propagates_exception_and_does_not_stop_application()
	{
		var contributor = Substitute.For<ISeedContributor>();
		contributor.Name.Returns("Bad");
		contributor.SeedAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new InvalidOperationException("seed failed")));

		var lifetime = Substitute.For<IHostApplicationLifetime>();
		SeedRunnerService sut = new(
			[contributor],
			lifetime,
			NullLogger<SeedRunnerService>.Instance);

		var act = () => sut.StartAsync(CancellationToken.None);

		await act.ShouldThrowAsync<InvalidOperationException>();
		lifetime.DidNotReceive().StopApplication();
	}

	[Fact]
	async Task StopAsync_is_always_a_noop()
	{
		SeedRunnerService sut = new(
			[],
			Substitute.For<IHostApplicationLifetime>(),
			NullLogger<SeedRunnerService>.Instance);

		await sut.StopAsync(CancellationToken.None);
	}

	[Fact]
	async Task StartAsync_logs_contributor_lifecycle_when_logger_is_enabled()
	{
		var contributor = Substitute.For<ISeedContributor>();
		contributor.Name.Returns("Test");
		contributor.SeedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var lifetime = Substitute.For<IHostApplicationLifetime>();
		SeedRunnerService sut = new([contributor], lifetime, new AlwaysEnabledLogger());
		await sut.StartAsync(CancellationToken.None);

		lifetime.Received(1).StopApplication();
	}

	sealed class AlwaysEnabledLogger : ILogger<SeedRunnerService>
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
	}

	[Fact]
	void AddNorseSeedingRunner_registers_SeedRunnerService_as_hosted_service()
	{
		var services = new ServiceCollection();
		var builder = Substitute.For<IHostApplicationBuilder>();
		builder.Services.Returns(services);

		builder.AddNorseSeedingRunner();

		services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(SeedRunnerService))
			.ShouldBeTrue();
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Midgard/tests/Infrastructure.Migrations.Tests/`
Expected: FAIL — `SeedRunnerService` and `AddNorseSeedingRunner` do not exist (CS0246/CS1061).

- [ ] **Step 3: Write `SeedRunnerService`**

```csharp
// Midgard/src/Infrastructure.Migrations/SeedRunnerService.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Norse.Abstractions.Migrations.Seeding;

namespace Norse.Infrastructure.Migrations;

sealed partial class SeedRunnerService(
	IEnumerable<ISeedContributor> contributors,
	IHostApplicationLifetime lifetime,
	ILogger<SeedRunnerService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await Task.WhenAll(contributors.Select(c => RunAsync(c, cancellationToken))).ConfigureAwait(false);
		lifetime.StopApplication();
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	async Task RunAsync(ISeedContributor contributor, CancellationToken ct)
	{
		LogStarting(logger, contributor.Name);
		await contributor.SeedAsync(ct).ConfigureAwait(false);
		LogCompleted(logger, contributor.Name);
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "Starting seed contributor {Name}")]
	static partial void LogStarting(ILogger logger, string name);

	[LoggerMessage(Level = LogLevel.Information, Message = "Seed contributor {Name} completed")]
	static partial void LogCompleted(ILogger logger, string name);
}
```

- [ ] **Step 4: Add `AddNorseSeedingRunner()` to the existing extensions class**

```csharp
// Midgard/src/Infrastructure.Migrations/HostApplicationBuilderExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Infrastructure.Migrations;

/// <summary>
/// Extension methods for <see cref="IHostApplicationBuilder"/> to register Norse migrations infrastructure.
/// </summary>
public static class HostApplicationBuilderExtensions
{
	/// <summary>
	/// Registers <see cref="MigrationRunnerService"/> as a hosted service that runs all
	/// <see cref="Norse.Abstractions.Migrations.IMigrationContributor"/> implementations on startup.
	/// </summary>
	/// <param name="builder">The host application builder.</param>
	/// <returns>The same <paramref name="builder"/> for chaining.</returns>
	public static IHostApplicationBuilder AddNorseMigrationsRunner(this IHostApplicationBuilder builder)
	{
		builder.Services.AddHostedService<MigrationRunnerService>();
		return builder;
	}

	/// <summary>
	/// Registers <see cref="SeedRunnerService"/> as a hosted service that runs all
	/// <see cref="Norse.Abstractions.Migrations.Seeding.ISeedContributor"/> implementations after
	/// migrations complete, and stops the application on completion. Always the last phase — register
	/// this after <see cref="AddNorseMigrationsRunner"/> so seeding cannot begin before every migration
	/// contributor has finished.
	/// </summary>
	/// <param name="builder">The host application builder.</param>
	/// <returns>The same <paramref name="builder"/> for chaining.</returns>
	public static IHostApplicationBuilder AddNorseSeedingRunner(this IHostApplicationBuilder builder)
	{
		builder.Services.AddHostedService<SeedRunnerService>();
		return builder;
	}
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test Midgard/tests/Infrastructure.Migrations.Tests/`
Expected: PASS (6 new tests; existing `MigrationRunnerServiceTests` still pass unchanged)

- [ ] **Step 6: Commit**

```bash
git -C Midgard add src/Infrastructure.Migrations/SeedRunnerService.cs src/Infrastructure.Migrations/HostApplicationBuilderExtensions.cs tests/Infrastructure.Migrations.Tests/SeedRunnerServiceTests.cs
git -C Midgard commit -m "feat: add SeedRunnerService and AddNorseSeedingRunner"
```

---

### Task 3: Midgard — `MigrationRunnerService` no longer owns `StopApplication()`

**Files:**
- Modify: `Midgard/src/Infrastructure.Migrations/MigrationRunnerService.cs`
- Modify: `Midgard/tests/Infrastructure.Migrations.Tests/MigrationRunnerServiceTests.cs`
- Test: `Midgard/tests/Infrastructure.Migrations.Tests/MigrationsAndSeedingOrderingTests.cs`

**Interfaces:**
- Consumes: `SeedRunnerService`/`AddNorseSeedingRunner()` (Task 2).
- Produces: `MigrationRunnerService` with a two-parameter constructor (`IEnumerable<IMigrationContributor>`, `ILogger<MigrationRunnerService>`) — the `IHostApplicationLifetime` parameter is gone. Task 4/5's generated code is unaffected (it only calls `AddNorseMigrationsRunner()`, never constructs `MigrationRunnerService` directly — DI does that).

This is a breaking change to already-shipped code: `StopApplication()` becomes `SeedRunnerService`'s exclusive responsibility now that seeding is always the last phase.

- [ ] **Step 1: Write the failing ordering test first**

```csharp
// Midgard/tests/Infrastructure.Migrations.Tests/MigrationsAndSeedingOrderingTests.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norse.Abstractions.Migrations;
using Norse.Abstractions.Migrations.Seeding;
using NSubstitute;

namespace Norse.Infrastructure.Migrations.Tests;

public sealed class MigrationsAndSeedingOrderingTests
{
	[Fact]
	async Task Host_runs_every_migration_to_completion_before_any_seed_contributor_starts()
	{
		List<string> executionLog = [];

		var migrationContributor = Substitute.For<IMigrationContributor>();
		migrationContributor.Name.Returns("Migration");
		migrationContributor.MigrateAsync(Arg.Any<CancellationToken>())
			.Returns(_ =>
			{
				executionLog.Add("migration");
				return Task.CompletedTask;
			});

		var seedContributor = Substitute.For<ISeedContributor>();
		seedContributor.Name.Returns("Seed");
		seedContributor.SeedAsync(Arg.Any<CancellationToken>())
			.Returns(_ =>
			{
				executionLog.Add("seed");
				return Task.CompletedTask;
			});

		var builder = Host.CreateApplicationBuilder();
		builder.Services.AddSingleton(migrationContributor);
		builder.Services.AddSingleton(seedContributor);
		builder.AddNorseMigrationsRunner();
		builder.AddNorseSeedingRunner();

		using var host = builder.Build();
		await host.StartAsync(CancellationToken.None);

		executionLog.ShouldBe(["migration", "seed"]);
	}

	[Fact]
	async Task Host_with_no_seed_contributors_still_runs_migrations_and_completes()
	{
		var migrationContributor = Substitute.For<IMigrationContributor>();
		migrationContributor.Name.Returns("Migration");
		migrationContributor.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var builder = Host.CreateApplicationBuilder();
		builder.Services.AddSingleton(migrationContributor);
		builder.AddNorseMigrationsRunner();
		builder.AddNorseSeedingRunner();

		using var host = builder.Build();
		await host.StartAsync(CancellationToken.None);

		await migrationContributor.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Midgard/tests/Infrastructure.Migrations.Tests/`
Expected: FAIL — the current `MigrationRunnerService.StartAsync` calls `StopApplication()` itself, which (depending on host shutdown timing) is not what this test is asserting against; more concretely, this step establishes the baseline before Step 3's edit. If this passes unexpectedly before Step 3, stop and re-examine — it means the ordering guarantee isn't actually what's being changed.

- [ ] **Step 3: Remove `StopApplication()` and the now-unused `lifetime` parameter from `MigrationRunnerService`**

```csharp
// Midgard/src/Infrastructure.Migrations/MigrationRunnerService.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Norse.Abstractions.Migrations;

namespace Norse.Infrastructure.Migrations;

sealed partial class MigrationRunnerService(
	IEnumerable<IMigrationContributor> contributors,
	ILogger<MigrationRunnerService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await Task.WhenAll(contributors.Select(c => RunAsync(c, cancellationToken))).ConfigureAwait(false);
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	async Task RunAsync(IMigrationContributor contributor, CancellationToken ct)
	{
		LogStarting(logger, contributor.Name);
		await contributor.MigrateAsync(ct).ConfigureAwait(false);
		LogCompleted(logger, contributor.Name);
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "Starting migration contributor {Name}")]
	static partial void LogStarting(ILogger logger, string name);

	[LoggerMessage(Level = LogLevel.Information, Message = "Migration contributor {Name} completed")]
	static partial void LogCompleted(ILogger logger, string name);
}
```

- [ ] **Step 4: Update the existing test file to drop `lifetime` entirely**

```csharp
// Midgard/tests/Infrastructure.Migrations.Tests/MigrationRunnerServiceTests.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Norse.Abstractions.Migrations;
using NSubstitute;

namespace Norse.Infrastructure.Migrations.Tests;

public sealed class MigrationRunnerServiceTests
{
	[Fact]
	async Task StartAsync_runs_all_contributors()
	{
		var contributor = Substitute.For<IMigrationContributor>();
		contributor.Name.Returns("Test");
		contributor.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		MigrationRunnerService sut = new(
			[contributor],
			NullLogger<MigrationRunnerService>.Instance);

		await sut.StartAsync(CancellationToken.None);

		await contributor.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
	}

	[Fact]
	async Task StartAsync_with_multiple_contributors_runs_all()
	{
		var a = Substitute.For<IMigrationContributor>();
		a.Name.Returns("A");
		a.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var b = Substitute.For<IMigrationContributor>();
		b.Name.Returns("B");
		b.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		MigrationRunnerService sut = new(
			[a, b],
			NullLogger<MigrationRunnerService>.Instance);

		await sut.StartAsync(CancellationToken.None);

		await a.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
		await b.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
	}

	[Fact]
	async Task StartAsync_propagates_exception()
	{
		var contributor = Substitute.For<IMigrationContributor>();
		contributor.Name.Returns("Bad");
		contributor.MigrateAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new InvalidOperationException("migration failed")));

		MigrationRunnerService sut = new(
			[contributor],
			NullLogger<MigrationRunnerService>.Instance);

		var act = () => sut.StartAsync(CancellationToken.None);

		await act.ShouldThrowAsync<InvalidOperationException>();
	}

	[Fact]
	async Task StopAsync_is_always_a_noop()
	{
		MigrationRunnerService sut = new(
			[],
			NullLogger<MigrationRunnerService>.Instance);

		await sut.StopAsync(CancellationToken.None);
	}

	[Fact]
	async Task StartAsync_logs_contributor_lifecycle_when_logger_is_enabled()
	{
		var contributor = Substitute.For<IMigrationContributor>();
		contributor.Name.Returns("Test");
		contributor.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		MigrationRunnerService sut = new([contributor], new AlwaysEnabledLogger());

		await sut.StartAsync(CancellationToken.None);

		await contributor.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
	}

	sealed class AlwaysEnabledLogger : ILogger<MigrationRunnerService>
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
	}

	[Fact]
	void AddNorseMigrationsRunner_registers_MigrationRunnerService_as_hosted_service()
	{
		var services = new ServiceCollection();
		var builder = Substitute.For<IHostApplicationBuilder>();
		builder.Services.Returns(services);

		builder.AddNorseMigrationsRunner();

		services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(MigrationRunnerService))
			.ShouldBeTrue();
	}
}
```

- [ ] **Step 5: Run tests to verify everything passes**

Run: `dotnet test Midgard/tests/Infrastructure.Migrations.Tests/`
Expected: PASS — all `MigrationRunnerServiceTests`, all `SeedRunnerServiceTests`, both `MigrationsAndSeedingOrderingTests`.

- [ ] **Step 6: Commit**

```bash
git -C Midgard add src/Infrastructure.Migrations/MigrationRunnerService.cs tests/Infrastructure.Migrations.Tests/MigrationRunnerServiceTests.cs tests/Infrastructure.Migrations.Tests/MigrationsAndSeedingOrderingTests.cs
git -C Midgard commit -m "fix!: SeedRunnerService owns StopApplication exclusively; MigrationRunnerService no longer stops the host"
```

---

### Task 4: Urdarbrunnr — Postgres generator discovers and emits seed contributors

**Files:**
- Create: `Urdarbrunnr/src/EntityFramework.Migrations.Generator.Shared/CSharpEmit.cs`
- Create: `Urdarbrunnr/src/EntityFramework.Migrations.Generator.Shared/StringSyntaxAttribute.cs`
- Modify: `Urdarbrunnr/src/EntityFramework.Migrations.Generator.Shared/MigrationContributorDiscovery.cs`
- Modify: `Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL.Generator/MigrationContributorGenerator.cs`
- Modify: `Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj`
- Modify: `Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/MigrationContributorGeneratorTests.cs`

**Interfaces:**
- Consumes: `Norse.Abstractions.Migrations.Seeding.ISeedContributor` (Task 1), `AddNorseSeedingRunner()` (Task 2) — referenced only as emitted source text, no project reference needed by the generator itself.
- Produces: `MigrationContributorDiscovery.FindSeedContributors(Compilation)` returning `IList<SeedContributorInfo>`; `CSharpEmit.AppendCSharp(this StringBuilder, string)`. Task 5 (SqlServer generator) consumes both of these, plus the `StringSyntaxAttribute` polyfill, unchanged.

`MigrationContributorDiscovery.cs` and the two new files are all `<Compile Include>`-linked into **both** provider generator projects, so this task's shared-file edits also compile into the SqlServer generator — Task 5 only needs its own `.csproj` links added and its own `BuildSource` rewritten, not these files touched again.

- [ ] **Step 1: Write the failing test**

```csharp
// Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/MigrationContributorGeneratorTests.cs
// Add this using at the top, alongside the existing ones:
// using Microsoft.Extensions.DependencyInjection;
// using Norse.Abstractions.Migrations.Seeding;

[Fact]
void Generator_discovers_seed_contributors_and_emits_registration()
{
	var source = """
		using Microsoft.Extensions.DependencyInjection;
		using Norse.Abstractions.Migrations.Seeding;

		sealed class TestSeedContributor : ISeedContributor
		{
			public string Name => "Test";
			public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			public static void ConfigureServices(IServiceCollection services) { }
		}
		""";

	var compilation = CreateCompilation(source);
	var generator = new MigrationContributorGenerator();
	GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
	driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
	var result = driver.GetRunResult();

	result.GeneratedTrees.Length.ShouldBe(1);
	var generated = result.GeneratedTrees[0].ToString();
	generated.ShouldContain("TestSeedContributor.ConfigureServices(builder.Services);");
	generated.ShouldContain("AddTransient<global::Norse.Abstractions.Migrations.Seeding.ISeedContributor, TestSeedContributor>");
	generated.ShouldContain("AddNorseSeedingRunner");
}

[Fact]
void Generator_produces_AddNorseSeedingRunner_call_even_with_zero_seed_contributors()
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

	var generated = result.GeneratedTrees[0].ToString();
	generated.ShouldContain("AddNorseSeedingRunner");
}
```

Also update the `references` array inside the existing `CreateCompilation` helper in the same file:

```csharp
var references = new[]
{
	typeof(object),
	typeof(Norse.EntityFramework.Migrations.MigrationConnectionStringAttribute),
	typeof(Norse.EntityFramework.NorseDbContext),
	typeof(Norse.Abstractions.Migrations.IMigrationContributor),
	typeof(Norse.Abstractions.Migrations.Seeding.ISeedContributor),
	typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection),
	typeof(Microsoft.EntityFrameworkCore.DbContext),
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/`
Expected: FAIL — generated output doesn't contain seed-contributor registrations or `AddNorseSeedingRunner` yet; also a compile error if `Norse.Abstractions.Migrations.Seeding.ISeedContributor` isn't yet resolvable (confirms Task 1 must be locally built first — expected in `UseProjectReferences=true` dev mode).

- [ ] **Step 3: Add the `StringSyntaxAttribute` polyfill**

```csharp
// Urdarbrunnr/src/EntityFramework.Migrations.Generator.Shared/StringSyntaxAttribute.cs
// Polyfill for System.Diagnostics.CodeAnalysis.StringSyntaxAttribute (added in .NET 7).
// Both generator projects target netstandard2.0, so the BCL definition isn't available.
// Roslyn's IDE classifiers recognise the attribute by namespace + type name (not assembly
// identity), so an internal declaration here drives the embedded-language hint in VS / Rider.
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace System.Diagnostics.CodeAnalysis;
#pragma warning restore IDE0130 // Namespace does not match folder structure

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false)]
sealed class StringSyntaxAttribute(string syntax) : Attribute
{
	public string Syntax { get; } = syntax;
}
```

- [ ] **Step 4: Add the `CSharpEmit.AppendCSharp` extension**

```csharp
// Urdarbrunnr/src/EntityFramework.Migrations.Generator.Shared/CSharpEmit.cs
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Norse.EntityFramework.Migrations.Generator.Shared;

static class CSharpEmit
{
	// Identical to StringBuilder.AppendLine at runtime; the [StringSyntax("C#")] annotation
	// is what Visual Studio and Rider use to syntax-highlight the raw-string content at each
	// call site as C# instead of as opaque text.
	public static StringBuilder AppendCSharp(this StringBuilder sb, [StringSyntax("C#")] string code) =>
		sb.AppendLine(code);
}
```

- [ ] **Step 5: Link both new files into the Postgres generator project**

```xml
<!-- Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj -->
<!-- Existing ItemGroup, add two more Compile Include lines alongside the existing one: -->
<ItemGroup>
	<Compile Include="../EntityFramework.Migrations.Generator.Shared/MigrationContributorDiscovery.cs" Link="Shared/MigrationContributorDiscovery.cs" />
	<Compile Include="../EntityFramework.Migrations.Generator.Shared/CSharpEmit.cs" Link="Shared/CSharpEmit.cs" />
	<Compile Include="../EntityFramework.Migrations.Generator.Shared/StringSyntaxAttribute.cs" Link="Shared/StringSyntaxAttribute.cs" />
</ItemGroup>
```

- [ ] **Step 6: Extend the shared discovery file**

Add to the end of the `MigrationContributorDiscovery` class (before its closing brace) in `Urdarbrunnr/src/EntityFramework.Migrations.Generator.Shared/MigrationContributorDiscovery.cs`:

```csharp
	const string SeedContributorInterfaceMetadataName =
		"Norse.Abstractions.Migrations.Seeding.ISeedContributor";

	public static IList<SeedContributorInfo> FindSeedContributors(Compilation compilation)
	{
		IList<SeedContributorInfo> results = [];

		foreach (var type in AllTypes(compilation))
		{
			if (type.IsAbstract)
				continue;

			if (!ImplementsSeedContributorInterface(type))
				continue;

			results.Add(new SeedContributorInfo(
				type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
		}

		return results;
	}

	static bool ImplementsSeedContributorInterface(INamedTypeSymbol type) =>
		type.AllInterfaces.Any(i => i.ToDisplayString() == SeedContributorInterfaceMetadataName);
```

Add this new struct after the existing `ContributorInfo` struct, at the bottom of the file:

```csharp
readonly struct SeedContributorInfo
{
	public SeedContributorInfo(string contributorType)
	{
		ContributorType = contributorType;
	}

	public string ContributorType { get; }
}
```

- [ ] **Step 7: Extend the Postgres generator to discover and emit seed contributors, using `AppendCSharp`**

```csharp
// Urdarbrunnr/src/EntityFramework.Migrations.PostgreSQL.Generator/MigrationContributorGenerator.cs
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

		var seedContributors = context.CompilationProvider.Select(static (compilation, _) =>
			MigrationContributorDiscovery.FindSeedContributors(compilation));

		var combined = contributors.Combine(seedContributors);

		context.RegisterSourceOutput(combined, static (ctx, pair) =>
		{
			var (list, seedList) = pair;
			if (list.Count == 0 && seedList.Count == 0)
				return;

			var source = BuildSource(list, seedList);
			ctx.AddSource("NorseMigrationsExtensions.g.cs", SourceText.From(source, Encoding.UTF8));
		});
	}

	static string BuildSource(IList<ContributorInfo> contributors, IList<SeedContributorInfo> seedContributors)
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
			using Norse.EntityFramework.PostgreSQL;
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
						builder.AddNorsePostgresMigrationContext<{c.ContextType}>("{c.ConnectionStringName}", "{c.MigrationsAssemblyName}");
						builder.Services.AddTransient<global::Norse.Abstractions.Migrations.IMigrationContributor, {c.ContributorType}>();
				""");

		foreach (var s in seedContributors)
			sb.AppendCSharp(
				$"""
						{s.ContributorType}.ConfigureServices(builder.Services);
						builder.Services.AddTransient<global::Norse.Abstractions.Migrations.Seeding.ISeedContributor, {s.ContributorType}>();
				""");

		sb.AppendCSharp(
			"""
					builder.AddNorseMigrationsRunner();
					builder.AddNorseSeedingRunner();
					return builder;
				}
			}
			""");

		return sb.ToString();
	}
}
```

Every append call is a `[StringSyntax("C#")]`-annotated raw string literal — open VS or Rider on this file after this edit and each `"""..."""` block renders with full C# syntax highlighting, not as plain text.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/`
Expected: PASS — including the pre-existing `Generator_produces_AddNorseMigrations_method` test, which now also implicitly emits `AddNorseSeedingRunner` (its existing assertions don't check for its absence, so it keeps passing unchanged).

- [ ] **Step 9: Commit**

```bash
git -C Urdarbrunnr add src/EntityFramework.Migrations.Generator.Shared/ src/EntityFramework.Migrations.PostgreSQL.Generator/MigrationContributorGenerator.cs src/EntityFramework.Migrations.PostgreSQL.Generator/EntityFramework.Migrations.PostgreSQL.Generator.csproj tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/MigrationContributorGeneratorTests.cs
git -C Urdarbrunnr commit -m "feat: Postgres migrations generator discovers and wires ISeedContributor"
```

---

### Task 5: Urdarbrunnr — SQL Server generator mirrors the same seed support

**Files:**
- Modify: `Urdarbrunnr/src/EntityFramework.Migrations.SqlServer.Generator/MigrationContributorGenerator.cs`
- Modify: `Urdarbrunnr/src/EntityFramework.Migrations.SqlServer.Generator/EntityFramework.Migrations.SqlServer.Generator.csproj`
- Modify: `Urdarbrunnr/tests/EntityFramework.Migrations.SqlServer.Generator.Tests/MigrationContributorGeneratorTests.cs`

**Interfaces:**
- Consumes: `MigrationContributorDiscovery.FindSeedContributors`, `CSharpEmit.AppendCSharp`, `StringSyntaxAttribute` (all added to the shared folder in Task 4 — no further edit to those files needed here, just linking them into this project too).
- Produces: identical generated-source shape to Task 4's Postgres generator, with `AddNorseSqlServerMigrationContext` in place of `AddNorsePostgresMigrationContext`.

- [ ] **Step 1: Write the failing tests (identical to Task 4's, SqlServer namespace)**

```csharp
// Urdarbrunnr/tests/EntityFramework.Migrations.SqlServer.Generator.Tests/MigrationContributorGeneratorTests.cs
// Add alongside existing tests:

[Fact]
void Generator_discovers_seed_contributors_and_emits_registration()
{
	var source = """
		using Microsoft.Extensions.DependencyInjection;
		using Norse.Abstractions.Migrations.Seeding;

		sealed class TestSeedContributor : ISeedContributor
		{
			public string Name => "Test";
			public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			public static void ConfigureServices(IServiceCollection services) { }
		}
		""";

	var compilation = CreateCompilation(source);
	var generator = new MigrationContributorGenerator();
	GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
	driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
	var result = driver.GetRunResult();

	result.GeneratedTrees.Length.ShouldBe(1);
	var generated = result.GeneratedTrees[0].ToString();
	generated.ShouldContain("TestSeedContributor.ConfigureServices(builder.Services);");
	generated.ShouldContain("AddTransient<global::Norse.Abstractions.Migrations.Seeding.ISeedContributor, TestSeedContributor>");
	generated.ShouldContain("AddNorseSeedingRunner");
}

[Fact]
void Generator_produces_AddNorseSeedingRunner_call_even_with_zero_seed_contributors()
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

	var generated = result.GeneratedTrees[0].ToString();
	generated.ShouldContain("AddNorseSeedingRunner");
}
```

Update the `references` array in this file's own `CreateCompilation` helper the same way as Task 4:

```csharp
var references = new[]
{
	typeof(object),
	typeof(Norse.EntityFramework.Migrations.MigrationConnectionStringAttribute),
	typeof(Norse.EntityFramework.NorseDbContext),
	typeof(Norse.Abstractions.Migrations.IMigrationContributor),
	typeof(Norse.Abstractions.Migrations.Seeding.ISeedContributor),
	typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection),
	typeof(Microsoft.EntityFrameworkCore.DbContext),
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Urdarbrunnr/tests/EntityFramework.Migrations.SqlServer.Generator.Tests/`
Expected: FAIL — no seed-contributor emission yet.

- [ ] **Step 3: Link the shared `CSharpEmit` and `StringSyntaxAttribute` files into the SQL Server generator project**

```xml
<!-- Urdarbrunnr/src/EntityFramework.Migrations.SqlServer.Generator/EntityFramework.Migrations.SqlServer.Generator.csproj -->
<!-- Existing ItemGroup, add two more Compile Include lines alongside the existing one: -->
<ItemGroup>
	<Compile Include="../EntityFramework.Migrations.Generator.Shared/MigrationContributorDiscovery.cs" Link="Shared/MigrationContributorDiscovery.cs" />
	<Compile Include="../EntityFramework.Migrations.Generator.Shared/CSharpEmit.cs" Link="Shared/CSharpEmit.cs" />
	<Compile Include="../EntityFramework.Migrations.Generator.Shared/StringSyntaxAttribute.cs" Link="Shared/StringSyntaxAttribute.cs" />
</ItemGroup>
```

- [ ] **Step 4: Extend the SQL Server generator identically to Task 4's Postgres edit**

```csharp
// Urdarbrunnr/src/EntityFramework.Migrations.SqlServer.Generator/MigrationContributorGenerator.cs
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

		var seedContributors = context.CompilationProvider.Select(static (compilation, _) =>
			MigrationContributorDiscovery.FindSeedContributors(compilation));

		var combined = contributors.Combine(seedContributors);

		context.RegisterSourceOutput(combined, static (ctx, pair) =>
		{
			var (list, seedList) = pair;
			if (list.Count == 0 && seedList.Count == 0)
				return;

			var source = BuildSource(list, seedList);
			ctx.AddSource("NorseMigrationsExtensions.g.cs", SourceText.From(source, Encoding.UTF8));
		});
	}

	static string BuildSource(IList<ContributorInfo> contributors, IList<SeedContributorInfo> seedContributors)
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
			using Norse.EntityFramework.SqlServer;
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
						builder.AddNorseSqlServerMigrationContext<{c.ContextType}>("{c.ConnectionStringName}", "{c.MigrationsAssemblyName}");
						builder.Services.AddTransient<global::Norse.Abstractions.Migrations.IMigrationContributor, {c.ContributorType}>();
				""");

		foreach (var s in seedContributors)
			sb.AppendCSharp(
				$"""
						{s.ContributorType}.ConfigureServices(builder.Services);
						builder.Services.AddTransient<global::Norse.Abstractions.Migrations.Seeding.ISeedContributor, {s.ContributorType}>();
				""");

		sb.AppendCSharp(
			"""
					builder.AddNorseMigrationsRunner();
					builder.AddNorseSeedingRunner();
					return builder;
				}
			}
			""");

		return sb.ToString();
	}
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Urdarbrunnr/tests/EntityFramework.Migrations.SqlServer.Generator.Tests/`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git -C Urdarbrunnr add src/EntityFramework.Migrations.SqlServer.Generator/MigrationContributorGenerator.cs src/EntityFramework.Migrations.SqlServer.Generator/EntityFramework.Migrations.SqlServer.Generator.csproj tests/EntityFramework.Migrations.SqlServer.Generator.Tests/MigrationContributorGeneratorTests.cs
git -C Urdarbrunnr commit -m "feat: SQL Server migrations generator discovers and wires ISeedContributor"
```

---

### Task 6: Full-solution verification

**Files:** None (verification-only task; no code changes).

- [ ] **Step 1: Build and test the whole solution in ProjectReference (dev) mode**

Run: `dotnet build Bifrost.slnx`
Expected: succeeds, zero warnings (warnings are errors platform-wide).

Run: `dotnet test Asgard/tests/Abstractions.Migrations.Tests/ Midgard/tests/Infrastructure.Migrations.Tests/ Urdarbrunnr/tests/EntityFramework.Migrations.PostgreSQL.Generator.Tests/ Urdarbrunnr/tests/EntityFramework.Migrations.SqlServer.Generator.Tests/`
Expected: all tests pass.

- [ ] **Step 2: Confirm the Yggdrasil migrations service still builds untouched**

Run: `dotnet build Yggdrasil/src/Hosting.Migrations.Service/`
Expected: succeeds with no source changes to `Program.cs` or its `.csproj` — this is the proof that seeding slotted into the existing `AddNorseMigrations()` call with zero consumer-side changes, per the spec's §1.1 promise.

- [ ] **Step 3 (defer until Asgard, Midgard, and Urdarbrunnr are each merged, tagged, and published to NuGet): PackageReference-mode verification**

Run: `dotnet build Bifrost.slnx -p:UseProjectReferences=false`
Expected: succeeds, resolving `ISeedContributor`, `SeedRunnerService`, and both generators from the published NuGet packages rather than local `ProjectReference`s. This step cannot pass until the real ship gates (PR merge → CI green → tag → `dotnet pack` publish) have happened for all three realms in order — it is not runnable as part of the same session that writes the code, the same way the migrations framework's own `UseProjectReferences` gate wasn't.

- [ ] **Step 4: Report status**

If Steps 1–2 pass and Step 3 is pending real publishes, the chassis is done and reviewable now; Step 3 is the final sign-off once Buvy has shipped each realm.

---

## Self-Review

**Spec coverage:** Every section of `2026-07-03-seeding-framework-design.md` is covered — §2.1 `ISeedContributor` (Task 1), §2.2 `DeterministicGuid` (explicitly NOT built, per the Global Constraints note and Buvy's 2026-07-04 decision to consume Svartalfheim's real struct instead), §3 generator extension (Tasks 4–5), §4 `SeedRunnerService` + the `StopApplication()` handoff (Tasks 2–3), §1.1's "no new assemblies" (every task modifies an existing project), §7's success criteria (stub contributor proof in Task 1, migrate-then-seed ordering proof in Task 3, zero-seed-contributor regression proof in Task 3's second test and Task 4/5's second test each).

**Placeholder scan:** No TBDs. Every step has complete code. Task 6 Step 3 is explicitly marked as deferred with a stated reason (real publishes required), not left vague.

**Type consistency:** `ISeedContributor` (Task 1) → consumed identically by `SeedRunnerService` (Task 2), the ordering test (Task 3), and both generators' emitted `AddTransient<global::Norse.Abstractions.Migrations.Seeding.ISeedContributor, ...>()` calls (Tasks 4–5) — same fully-qualified name throughout. `AddNorseSeedingRunner()` (Task 2) is called identically by the ordering test (Task 3) and both generators' `BuildSource` (Tasks 4–5). `MigrationRunnerService`'s two-parameter constructor (Task 3) matches every call site across its own test file — no lingering three-parameter construction left behind. `CSharpEmit.AppendCSharp` and the `StringSyntaxAttribute` polyfill are created exactly once (Task 4) and only *linked* (not re-created) into the SqlServer generator project (Task 5) — no duplicate type definitions across the two generator assemblies.
