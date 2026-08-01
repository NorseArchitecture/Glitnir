# Well Composition — DbContext Isolation and Construction Unification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (the platform default — `superpowers:executing-plans` is the narrow fallback for a separate-session review checkpoint, never an interchangeable alternative), paired with superpowers:test-driven-development on every task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the DbContext/EF-Core leak out of Yggdrasil's runtime realms and the migration-time/runtime construction drift risk, by adding one missing factory-shaped sibling to Urðarbrunnr's existing provider seam and retargeting every realm's runtime composition through it.

**Architecture:** `Urdarbrunnr.AddNorseContextFactory<TContext>()` (new, `IHostApplicationBuilder`-based, mirrors the existing `AddNorseContext<TContext>()` exactly except for the pooling mechanism) → `Midgard.AddNorseWell<TContext>()` (new, thin, composes the factory registration with the existing `AddWell<TContext>()` discovery) → Mimir's `Reference.Web.Server` and Himinbjörg's `Identity.Web.Server` retarget their composition methods onto it (or onto the existing `AddNorseContext<TContext>()` where a directly-injectable context is required) → Yggdrasil's `Hosting.Web.Server` drops its manual connection-string resolution and never references EF Core again → the E2E test that started this whole investigation gets fixed last, once there's nothing left in `Hosting.Web.Server`'s graph for it to reach around.

**Tech Stack:** .NET 11 preview / C# 15, EF Core 11 preview (Npgsql), xUnit v3 on MTP v2 + Shouldly + NSubstitute.

**Spec:** `../specs/2026-08-01-well-composition-dbcontext-isolation-design.md` — the authority for every law referenced below.

## Global Constraints

- **Immutable files — halt-and-ask, restate in every dispatch prompt:** every realm's `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `.editorconfig`, `config/` scatter files, and `.github/workflows/*` are hands-off. If a task appears to need an edit there, STOP and ask.
- **Git:** each realm gets one local feature branch `feature/well-composition-isolation` created at first touch, commit per task step, **never push, never touch master**. Verify `git branch --show-current` before every commit.
- **House rules govern all code:** tabs; target-typed `new()` always; `var` elsewhere; `is null`/`is not null`; sealed/abstract/static classes only; omit default accessibility; XML docs on all publicly visible src members; `ConfigureAwait(false)` on every await in src (never tests); every async method takes `CancellationToken cancellationToken = default` last and propagates it; primary constructors where they fit; no string concatenation; one `<PropertyGroup>` + one `<ItemGroup>` per csproj, alphabetized.
- **Tests:** xUnit v3 on MTP v2; Shouldly; NSubstitute; test classes `public sealed`; test methods bare `void`/`async Task` (no accessibility modifier); names sentence-shaped with underscores. NEVER `dotnet test` a project with zero tests. No mocked-DB tests for behavior that depends on database semantics — integration tests hit real Postgres via Testcontainers.
- **Warnings are errors platform-wide.** IDE0005 → delete the using, never suppress.
- **Build verification per task:** build the realm's `.slnx` and run that realm's touched test projects; both green before review.
- **Provider explicit, never sniffed.** Every new extension takes `INorseEfProvider` as an explicit parameter — no connection-string-format guessing, ever.
- **Docker may be unreachable in a given sandbox.** If a Testcontainers-backed test fails with `DockerUnavailableException`, confirm via `docker info` before treating it as anything other than environmental — do not treat it as a task failure if confirmed.

## Realm Ship Order and File Structure

```
Urdarbrunnr/
  src/Persistence.EntityFramework/NorseContextExtensions.cs  (modify: + AddNorseContextFactory<TContext>)
  tests/Persistence.EntityFramework.Tests/NorseContextExtensionsTests.cs  (modify or create)
Midgard/
  src/Infrastructure.Persistence.EntityFramework/ServiceCollectionExtensions.cs  (modify: + AddNorseWell<TContext> overload)
  tests/Infrastructure.Persistence.EntityFramework.Tests/  (modify: construction parity test)
Mimir/
  src/Reference.Web.Server/ServiceCollectionExtensions.cs  (modify)
  src/Reference.Web.Server/Reference.Web.Server.csproj  (modify: swap Npgsql PackageReference for a NorseRef)
Himinbjorg/
  src/Identity.Web.Server/ServiceCollectionExtensions.cs  (modify)
  src/Identity.Web.Server/Identity.Web.Server.csproj  (modify: same csproj swap, if it carries the same direct Npgsql reference — verify first)
Yggdrasil/
  src/Hosting.Web.Server/Program.cs  (modify: drop manual connection-string resolution, split the composition chain)
  tests/Hosting.Web.Server.Tests/  (modify: assembly-boundary test, fixed E2E fixture)
  tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj  (modify: drop the two now-unneeded PackageReferences)
```

---

### Task 1: Urðarbrunnr — `AddNorseContextFactory<TContext>()`

**Files:**
- Modify: `Urdarbrunnr/src/Persistence.EntityFramework/NorseContextExtensions.cs`
- Modify or create: `Urdarbrunnr/tests/Persistence.EntityFramework.Tests/NorseContextExtensionsTests.cs` (check whether a test file for the existing `AddNorseContext<TContext>()` already exists and follow its exact pattern; if none exists, create one following this task's own test as the template)

**Interfaces:**
- Consumes: `INorseEfProvider` (Urðarbrunnr, already shipped), `ApplyNorseProviderOptions` (same file, already shipped), `INorseDbContext` (Urðarbrunnr).
- Produces: `public static IHostApplicationBuilder AddNorseContextFactory<TContext>(this IHostApplicationBuilder builder, INorseEfProvider provider, string connectionStringName) where TContext : DbContext, INorseDbContext`. Task 2 calls this exact signature.

- [ ] **Step 1: Branch.** In `Urdarbrunnr/`: `git switch -c feature/well-composition-isolation` (verify `git branch --show-current` prints it).

- [ ] **Step 2: Read the existing sibling first.** Open `Urdarbrunnr/src/Persistence.EntityFramework/NorseContextExtensions.cs` and read `AddNorseContext<TContext>()` in full — the new method must be byte-for-byte identical except for the one line that changes the DI registration call. Do not deviate from its connection-string-resolution or provider-enrichment shape.

- [ ] **Step 3: Write the failing test.** Add to the test file:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norse.Persistence.EntityFramework;
using Norse.Persistence.EntityFramework.PostgreSQL;

namespace Norse.Persistence.EntityFramework.Tests;

public sealed class NorseContextExtensionsTests
{
	[Fact]
	void AddNorseContextFactory_registers_a_resolvable_IDbContextFactory()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration["ConnectionStrings:test"] = "Host=localhost;Database=test";

		builder.AddNorseContextFactory<FactoryTestDbContext>(NorsePostgresEfProvider.Instance, "test");
		using var host = builder.Build();

		var factory = host.Services.GetRequiredService<IDbContextFactory<FactoryTestDbContext>>();
		factory.ShouldNotBeNull();
	}

	[Fact]
	void AddNorseContextFactory_throws_when_the_connection_string_is_missing()
	{
		var builder = Host.CreateApplicationBuilder();

		Should.Throw<InvalidOperationException>(() =>
			builder.AddNorseContextFactory<FactoryTestDbContext>(NorsePostgresEfProvider.Instance, "missing"));
	}

	sealed class FactoryTestDbContext(DbContextOptions<FactoryTestDbContext> options) :
		DbContext(options), INorseDbContext;
}
```

(If `NorseContextExtensionsTests.cs` already exists with its own tests for `AddNorseContext<TContext>()`, add these two `[Fact]`s to that class rather than creating a new one, and reuse whatever synthetic test `DbContext` that file already defines instead of adding `FactoryTestDbContext` a second time — check first.)

- [ ] **Step 4: Run to verify failure.** `dotnet test Urdarbrunnr/tests/Persistence.EntityFramework.Tests -- --filter-class "*.NorseContextExtensionsTests"` — expected: compile failure, `AddNorseContextFactory` undefined.

- [ ] **Step 5: Implement.** In `NorseContextExtensions.cs`, add the new method beside `AddNorseContext<TContext>()`, inside the same `extension(IHostApplicationBuilder builder)` block:

```csharp
/// <summary>
/// Registers <typeparamref name="TContext"/> as a pooled <see cref="IDbContextFactory{TContext}"/> —
/// the runtime DI shape Midgard's generic well repository needs (create-execute-dispose per
/// operation), as opposed to <see cref="AddNorseContext{TContext}"/>'s directly-injectable pooled
/// context (the shape ASP.NET Core Identity's built-in stores require instead). Same provider seam,
/// same enrichment, same fail-fast-on-missing-connection-string behavior as its sibling — the DI
/// registration call is the only thing that differs.
/// </summary>
/// <param name="provider">The provider binding.</param>
/// <param name="connectionStringName">The configuration key under <c>ConnectionStrings</c>.</param>
/// <returns>The same <paramref name="builder"/> for chaining.</returns>
/// <exception cref="InvalidOperationException"><paramref name="connectionStringName"/> is not configured.</exception>
public IHostApplicationBuilder AddNorseContextFactory<TContext>(INorseEfProvider provider,
	string connectionStringName)
	where TContext : DbContext, INorseDbContext
{
	var connectionString = builder.Configuration.GetConnectionString(connectionStringName) ??
		throw new InvalidOperationException(
			$"Connection string '{connectionStringName}' was not found.");

	builder.Services.AddPooledDbContextFactory<TContext>(opts =>
		opts.ApplyNorseProviderOptions(provider, connectionString, migrationsAssemblyName: null));
	provider.Enrich<TContext>(builder);

	return builder;
}
```

- [ ] **Step 6: Run tests to verify pass.** Same filter as Step 4 — both new facts green.

- [ ] **Step 7: Build + full realm suite.** `dotnet build Urdarbrunnr.slnx` (zero warnings) and `dotnet test Urdarbrunnr.slnx`.

- [ ] **Step 8: Commit:** `feat: AddNorseContextFactory — the IDbContextFactory-shaped sibling of AddNorseContext`.

---

### Task 2: Midgard — `AddNorseWell<TContext>()`

**Files:**
- Modify: `Midgard/src/Infrastructure.Persistence.EntityFramework/ServiceCollectionExtensions.cs`
- Modify: `Midgard/tests/Infrastructure.Persistence.EntityFramework.Tests/` (extend existing test file, likely `ServiceCollectionExtensionsTests.cs` or similar — check for one first)

**Interfaces:**
- Consumes: `AddNorseContextFactory<TContext>()` (Task 1), the existing `AddWell<TContext>()` in the same file.
- Produces: `public static IHostApplicationBuilder AddNorseWell<TContext>(this IHostApplicationBuilder builder, INorseEfProvider provider, string connectionStringName) where TContext : DbContext, INorseDbContext`. Tasks 4 and 5 call this exact signature (Task 5 does not — see its own note).

- [ ] **Step 1: Branch** `feature/well-composition-isolation` in `Midgard/`.

- [ ] **Step 2: Write the failing test.** Using the existing `WellContext`/`WidgetEntity`/`WidgetView` synthetic fixtures already in `Midgard/tests/Infrastructure.Persistence.EntityFramework.Tests/` (built for well-and-wire Tasks 2–6 — read `WellContext.cs` first to confirm its current shape before writing this test):

```csharp
[Fact]
void AddNorseWell_registers_both_the_context_factory_and_the_repository()
{
	var builder = Host.CreateApplicationBuilder();
	builder.Configuration["ConnectionStrings:test"] = "Host=localhost;Database=test";

	builder.AddNorseWell<WellContext>(NorsePostgresEfProvider.Instance, "test");
	using var host = builder.Build();

	host.Services.GetRequiredService<IDbContextFactory<WellContext>>().ShouldNotBeNull();
	host.Services.GetRequiredService<IReadRepository<WidgetView>>().ShouldNotBeNull();
}
```

(Add the needed `using Norse.Persistence.EntityFramework;`/`using Norse.Persistence.EntityFramework.PostgreSQL;`/`using Norse.Abstractions.Backend;` if not already global in this test project.)

- [ ] **Step 3: Run to verify failure.** `dotnet test Midgard/tests/Infrastructure.Persistence.EntityFramework.Tests -- --filter-class "*ServiceCollectionExtensionsTests*"` (adjust to the real class name found in Step 2) — expected: compile failure, `AddNorseWell` undefined.

- [ ] **Step 4: Implement.** In `ServiceCollectionExtensions.cs`, add beside the existing `AddWell<TContext>()`:

```csharp
/// <summary>
/// Composes <see cref="AddNorseContextFactory{TContext}"/> (Urðarbrunnr's provider seam, factory-
/// shaped) with <see cref="AddWell{TContext}"/> (this class's own well/repository discovery) into
/// one call — the entry point every realm's runtime composition should use instead of hand-rolling
/// EF registration and remembering to chain discovery afterward (well-composition spec §3.2).
/// </summary>
/// <param name="provider">The provider binding.</param>
/// <param name="connectionStringName">The configuration key under <c>ConnectionStrings</c>.</param>
/// <returns>The same <paramref name="builder"/> for chaining.</returns>
[RequiresUnreferencedCode("Reflects over TContext's DbSet<T> properties to discover wells.")]
[RequiresDynamicCode("Closes Repository<TContext,TEntity,TView> reflectively per discovered well.")]
public static IHostApplicationBuilder AddNorseWell<TContext>(this IHostApplicationBuilder builder,
	INorseEfProvider provider, string connectionStringName)
	where TContext : DbContext, INorseDbContext
{
	builder.AddNorseContextFactory<TContext>(provider, connectionStringName);
	builder.Services.AddWell<TContext>();
	return builder;
}
```

Match the exact `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` message wording style already used on `AddWell<TContext>()` in this same file (read it first — copy the established phrasing, don't invent new wording).

- [ ] **Step 5: Run tests to verify pass.** Same filter as Step 3.

- [ ] **Step 6: Build + full realm suite.** `dotnet build Midgard.slnx` and `dotnet test Midgard.slnx` (skip any Testcontainers-backed suites that need Docker if unavailable — confirm via `docker info` first, same as Global Constraints).

- [ ] **Step 7: Commit:** `feat: AddNorseWell composes context-factory registration with well discovery`.

---

### Task 3: Midgard — construction parity test

**Files:**
- Modify: `Midgard/tests/Infrastructure.Persistence.EntityFramework.Tests/` (new test file, `ConstructionParityTests.cs`)

**Interfaces:**
- Consumes: `AddNorseWell<TContext>()` (Task 2), `AddNorseMigrationContext<TContext>()` (Urðarbrunnr, already shipped), the existing `WellContext` synthetic fixture.

This is the proof the drift risk from spec §1.3 is actually closed, not just relocated: both call paths must resolve to the identical model.

- [ ] **Step 1: Write the failing test.**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norse.Persistence.EntityFramework;
using Norse.Persistence.EntityFramework.Migrations;
using Norse.Persistence.EntityFramework.PostgreSQL;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

public sealed class ConstructionParityTests
{
	[Fact]
	async Task AddNorseWell_and_AddNorseMigrationContext_construct_the_identical_model()
	{
		var runtimeBuilder = Host.CreateApplicationBuilder();
		runtimeBuilder.Configuration["ConnectionStrings:test"] = "Host=localhost;Database=test";
		runtimeBuilder.AddNorseWell<WellContext>(NorsePostgresEfProvider.Instance, "test");
		await using var runtimeHost = runtimeBuilder.Build();
		await using var runtimeContext = await runtimeHost.Services
			.GetRequiredService<IDbContextFactory<WellContext>>()
			.CreateDbContextAsync(TestContext.Current.CancellationToken);

		var migrationBuilder = Host.CreateApplicationBuilder();
		migrationBuilder.Configuration["ConnectionStrings:test"] = "Host=localhost;Database=test";
		migrationBuilder.AddNorseMigrationContext<WellContext>(NorsePostgresEfProvider.Instance, "test",
			migrationsAssemblyName: null);
		await using var migrationHost = migrationBuilder.Build();
		await using var migrationContext = migrationHost.Services.GetRequiredService<WellContext>();

		var runtimeEntity = runtimeContext.Model.FindEntityType(typeof(WidgetEntity))!;
		var migrationEntity = migrationContext.Model.FindEntityType(typeof(WidgetEntity))!;

		runtimeEntity.GetTableName().ShouldBe(migrationEntity.GetTableName());
		runtimeEntity.GetSchema().ShouldBe(migrationEntity.GetSchema());
	}
}
```

(Verify `AddNorseMigrationContext<TContext>()`'s real parameter list against `Urdarbrunnr/src/Persistence.EntityFramework.Migrations/NorseMigrationContextExtensions.cs` before writing this — the plan's earlier research paraphrased it as `AddNorseMigrationContext<TContext>(provider, connectionStringName, migrationsAssemblyName)`; confirm exact parameter names/order and whether it's `IHostApplicationBuilder`- or `IServiceCollection`-based, and adjust the test to match exactly. If `WellContext` isn't itself already migration-contributor-compatible (i.e., doesn't already have a real migration set up), use `EnsureCreatedAsync`-style model inspection only — comparing `Model.FindEntityType(...).GetTableName()`/`GetSchema()` never requires a live database or a real migration to exist, only that both `DbContextOptions` were built.)

- [ ] **Step 2: Run to verify it passes on the first real attempt** (this test needs no new production code — Tasks 1–2 already provide everything it exercises). If it fails, the failure is real: it means `AddNorseWell`/`AddNorseMigrationContext` do NOT actually construct identical models yet, and the cause must be found and fixed in Task 1 or 2's implementation before proceeding — do not weaken this test to make it pass.

- [ ] **Step 3: Commit:** `test: prove migration-time and AddNorseWell construction produce the identical model`.

---

### Task 4: Mimir — retarget `Reference.Web.Server`

**Files:**
- Modify: `Mimir/src/Reference.Web.Server/ServiceCollectionExtensions.cs`
- Modify: `Mimir/src/Reference.Web.Server/Reference.Web.Server.csproj`
- Modify: `Mimir/tests/Reference.Web.Server.Tests/` (existing `CountryQueryHandlerTests.cs` and any composition test — check whether `AddNorseReferenceService`'s current signature is exercised by any test and update the call site)

**Interfaces:**
- Consumes: `AddNorseWell<TContext>()` (Task 2), `NorsePostgresEfProvider.Instance` (Urðarbrunnr, already shipped).
- Produces: `public static IHostApplicationBuilder AddNorseReferenceService(this IHostApplicationBuilder builder, string connectionStringName)` — **signature change** from the current `public IServiceCollection AddNorseReferenceService(string connectionString)`. Task 6 updates the one real caller (Yggdrasil `Program.cs`).

- [ ] **Step 1: Branch** `feature/well-composition-isolation` in `Mimir/` (this realm is currently on `feature/well-and-wire`, already merged/shipped per the well-and-wire slice — branch fresh from `master` for this plan).

- [ ] **Step 2: csproj swap first.** In `Reference.Web.Server.csproj`, remove:
```xml
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="11.*-*" />
```
and add, in the existing `NorseRef` group (alphabetized among the others already there):
```xml
<NorseRef Include="Persistence.EntityFramework.PostgreSQL">
	<Repo>Urdarbrunnr</Repo>
</NorseRef>
```
Update the csproj's `<Description>` — it currently doesn't mention the Npgsql dependency directly, so check whether any prose there needs adjusting; if not, leave it.

- [ ] **Step 3: Update `ServiceCollectionExtensions.cs`.** Read the current file in full first (from well-and-wire Task 13, commit `abd7492`) — it currently does:
```csharp
services.AddDbContextFactory<ReferenceDbContext>(o =>
{
	o.UseNpgsql(connectionString);
	o.ApplyNorseConventions(NorseNameRewriters.LowerSnakeCase);
	o.ApplyNorseTrackingBehavior();
});
services.AddWell<ReferenceDbContext>();
services.AddNorseReferenceWebServerHandlers();
services.AddScoped<IReferenceService, ReferenceService>();
return services;
```
Replace with:
```csharp
/// <summary>
/// Registers <see cref="ReferenceDbContext"/> (via <see cref="AddNorseWell{TContext}"/> — Postgres
/// only today, see the well-composition spec §5 for SQL Server's deferred status), the generated
/// mediator handler wiring, and <see cref="IReferenceService"/> itself.
/// </summary>
/// <param name="connectionStringName">The configuration key under <c>ConnectionStrings</c>.</param>
/// <returns>The same <paramref name="builder"/> for chaining.</returns>
public IHostApplicationBuilder AddNorseReferenceService(string connectionStringName)
{
	builder.AddNorseWell<ReferenceDbContext>(NorsePostgresEfProvider.Instance, connectionStringName);
	builder.Services.AddNorseReferenceWebServerHandlers();
	builder.Services.AddScoped<IReferenceService, ReferenceService>();
	return builder;
}
```
(Confirm whether this method currently lives inside an `extension(IServiceCollection services)` block — it needs to move to an `extension(IHostApplicationBuilder builder)` block instead; check if such a block already exists elsewhere in this file for another method, and if not, create one. Remove the now-unused `NorseNameRewriters`/raw EF-related `using` statements this change makes dead — IDE0005, delete don't suppress.)

- [ ] **Step 4: Fix the one real call site inside this realm's own tests**, if any test directly calls `AddNorseReferenceService` with the old signature (check `Reference.Web.Server.Tests` and any composition test). Update to the new `IHostApplicationBuilder`+`connectionStringName` shape, following the same `Host.CreateApplicationBuilder()` + `Configuration["ConnectionStrings:..."]` pattern used elsewhere in this plan.

- [ ] **Step 5: Build + full realm suite.** `dotnet build Mimir.slnx` (zero warnings — confirms the dead usings are actually gone) and `dotnet test Mimir.slnx`.

- [ ] **Step 6: Commit:** `refactor: Reference.Web.Server composes through AddNorseWell, drops direct Npgsql dependency`.

---

### Task 5: Himinbjörg — retarget `Identity.Web.Server`

**Files:**
- Modify: `Himinbjorg/src/Identity.Web.Server/ServiceCollectionExtensions.cs`
- Modify: `Himinbjorg/src/Identity.Web.Server/Identity.Web.Server.csproj` (verify its current package references first — this plan's own grounding research did not confirm whether it carries the same direct `Npgsql.EntityFrameworkCore.PostgreSQL` reference Mimir's project does; check before assuming the same swap applies)
- Modify: `Himinbjorg/tests/Identity.Web.Server.Tests/` (existing composition tests exercising `AddNorseAuthenticationService`)

**Interfaces:**
- Consumes: `AddNorseContext<TContext>()` (Urðarbrunnr, **already shipped, not Task 1's new factory-shaped one**) — see the note below for why.
- Produces: `public static IHostApplicationBuilder AddNorseAuthenticationService(this IHostApplicationBuilder builder, string connectionStringName)` — **signature change** from the current `public IServiceCollection AddNorseAuthenticationService(string connectionString)`. Task 6 updates the one real caller.

**Why this realm uses the existing `AddNorseContext<TContext>()`, not `AddNorseWell<TContext>()`:** ASP.NET Core Identity's built-in `UserManager<TUser>`/`SignInManager<TUser>`/`RoleManager<TRole>` (which `AddNorseIdentity()` wires) require a directly-injectable `DbContext`, not `IDbContextFactory<TContext>` — confirmed by the current code already using `AddDbContext<NorseIdentityDbContext>`, not `AddDbContextFactory`. Identity has no notion of "wells"/`IReadRepository<TView>` at all — it uses its own manager abstractions. `AddNorseContext<TContext>()` (pooled, directly-injectable, already shipped) is the correct fit; `AddNorseWell<TContext>()` would be wrong here, not merely unused.

- [ ] **Step 1: Branch** `feature/well-composition-isolation` in `Himinbjorg/`.

- [ ] **Step 2: Verify the csproj first.** Open `Identity.Web.Server.csproj` and confirm whether it carries a direct `PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL"` (Mimir's did; this plan assumed Himinbjörg's does too but that was not independently confirmed during grounding research). If it does, remove it and add the same `NorseRef Include="Persistence.EntityFramework.PostgreSQL"` (`Repo: Urdarbrunnr`) as Task 4 did. If it does NOT carry that reference (e.g., it already reaches EF only transitively through something else), STOP and report what you actually found rather than guessing — this changes what Step 3 needs to do.

- [ ] **Step 3: Update `ServiceCollectionExtensions.cs`.** Read the current file in full first. It currently does:
```csharp
services.AddDbContext<NorseIdentityDbContext>(o =>
{
	o.UseNpgsql(connectionString);
	o.ApplyNorseConventions(NorseNameRewriters.LowerSnakeCase);
	o.ApplyNorseTrackingBehavior();
});
services.AddNorseIdentity().AddSignInManager<NorseSignInManager>();
services.AddNorseIdentityWebServerHandlers();
services.AddSingleton<IEmailSender<NorseUser>, IdentityNoOpEmailSender>();
services.AddScoped<IAuthenticationService, AuthenticationService>();
services.AddOpenTelemetry().WithMetrics(static metrics => metrics.AddMeter("Microsoft.AspNetCore.Identity"));
return services;
```
Replace the first block and the method's outer shape (keep everything else — the `IEmailSender`/`OpenTelemetry`/comment content — verbatim, only the receiver type and the context-registration block change):
```csharp
public IHostApplicationBuilder AddNorseAuthenticationService(string connectionStringName)
{
	builder.AddNorseContext<NorseIdentityDbContext>(NorsePostgresEfProvider.Instance, connectionStringName);
	builder.Services.AddNorseIdentity().AddSignInManager<NorseSignInManager>();
	builder.Services.AddNorseIdentityWebServerHandlers();

	// Registered here, not by the host: IEmailSender<NorseUser> is closed over an entity the
	// host has no business naming. A host wiring a real sender registers its own afterward and
	// wins the resolution.
	builder.Services.AddSingleton<IEmailSender<NorseUser>, IdentityNoOpEmailSender>();

	builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

	// The realm that brings the dependency declares its telemetry: this project is the only
	// one on the platform referencing ASP.NET Core Identity, and its only consumer is
	// Web.Server — so the meter lands in exactly the container that should have it, with no
	// rule for anyone to remember.
	builder.Services.AddOpenTelemetry()
		.WithMetrics(static metrics => metrics.AddMeter("Microsoft.AspNetCore.Identity"));

	return builder;
}
```
(Same note as Task 4 Step 3 — move this method into an `extension(IHostApplicationBuilder builder)` block if it isn't already in one, and delete now-dead `using` statements.)

- [ ] **Step 4: Fix the one real call site inside this realm's own tests**, same as Task 4 Step 4.

- [ ] **Step 5: Build + full realm suite.** `dotnet build Himinbjorg.slnx` and `dotnet test Himinbjorg.slnx`.

- [ ] **Step 6: Commit:** `refactor: Identity.Web.Server composes through the existing AddNorseContext, drops direct Npgsql dependency`.

---

### Task 6: Yggdrasil — composition + the adversarial assembly-boundary test

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Server/Program.cs`
- Create: `Yggdrasil/tests/Hosting.Web.Server.Tests/AssemblyBoundaryTests.cs`

**Interfaces:**
- Consumes: `AddNorseReferenceService(string connectionStringName)` (Task 4's new signature), `AddNorseAuthenticationService(string connectionStringName)` (Task 5's new signature).

- [ ] **Step 1: Branch** `feature/well-composition-isolation` in `Yggdrasil/` (first touch on this realm for this plan — branch fresh from `master`).

- [ ] **Step 2: Update `Program.cs`.** Current shape (from well-and-wire Task 13):
```csharp
var norseIdentityConnectionString = builder.Configuration.GetConnectionString("norse_identity")
	?? throw new InvalidOperationException("Connection string 'norse_identity' is not configured.");
var norseReferenceConnectionString = builder.Configuration.GetConnectionString("norse_reference")
	?? throw new InvalidOperationException("Connection string 'norse_reference' is not configured.");

builder.Services
	.AddNorsePipeline()
	.AddNorseCodeFirstGrpc()
	.AddNorseAuthenticationService(norseIdentityConnectionString)
	.AddNorseReferenceService(norseReferenceConnectionString)
	.AddDeferredSignIn()
	.AddCodeFirstGrpcReflection();
```
Replace with (the two connection-string-resolving lines are gone — both extensions now resolve and fail-fast internally; the two `IHostApplicationBuilder`-based calls move onto `builder` directly, splitting what was one fluent chain into three statements since `AddNorsePipeline`/`AddNorseCodeFirstGrpc`/`AddDeferredSignIn`/`AddCodeFirstGrpcReflection` remain `IServiceCollection`-based and unrelated to this plan):
```csharp
builder.Services
	.AddNorsePipeline()
	.AddNorseCodeFirstGrpc();

builder.AddNorseAuthenticationService("norse_identity");
builder.AddNorseReferenceService("norse_reference");

builder.Services
	.AddDeferredSignIn()
	.AddCodeFirstGrpcReflection();
```

- [ ] **Step 3: Build to confirm the composition still compiles and starts.** `dotnet build Yggdrasil.slnx` (zero warnings) — this alone proves the signature changes threaded through correctly; `Hosting.Web.Server`'s existing `CompositionTests.cs` (from well-and-wire Task 13) already exercises `WebApplicationFactory<Program>` against this exact `Program.cs` and will catch a startup regression. Run it: `dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests -- --filter-class "*CompositionTests*"`.

- [ ] **Step 4: Write the adversarial assembly-boundary test — this is new, write it failing first is not possible here** (the assertion is already true once Tasks 1–5 land; there is no red state to observe, since this is a regression guard for a property that's already achieved, not a feature being built). Write it directly:

```csharp
using System.Reflection;

namespace Norse.Hosting.Web.Server.Tests;

public sealed class AssemblyBoundaryTests
{
	[Theory]
	[InlineData(typeof(Program))]
	void No_EF_Core_reference_exists_anywhere_in_the_assembly_graph(Type entryPointType)
	{
		var visited = new HashSet<string>();
		var toVisit = new Queue<Assembly>();
		toVisit.Enqueue(entryPointType.Assembly);

		while (toVisit.Count > 0)
		{
			var assembly = toVisit.Dequeue();
			if (!visited.Add(assembly.GetName().Name!))
				continue;

			assembly.GetName().Name.ShouldNotStartWith("Microsoft.EntityFrameworkCore",
				Case.Insensitive, $"{assembly.GetName().Name} must never appear in Hosting.Web.Server's graph");
			assembly.GetName().Name.ShouldNotStartWith("Npgsql.EntityFrameworkCore",
				Case.Insensitive, $"{assembly.GetName().Name} must never appear in Hosting.Web.Server's graph");

			foreach (var reference in assembly.GetReferencedAssemblies())
			{
				try
				{
					toVisit.Enqueue(Assembly.Load(reference));
				}
				catch (System.IO.FileNotFoundException)
				{
					// A referenced-but-unresolvable assembly (e.g. a reference-only/analyzer
					// assembly not present at runtime) cannot be Microsoft.EntityFrameworkCore
					// itself — that package is always runtime-loadable when actually referenced.
				}
			}
		}
	}
}
```

(Confirm the real `Shouldly` `ShouldNotStartWith` overload signature against this codebase's actual Shouldly version before committing to this exact call shape — if it doesn't take a `Case`/message overload the way written here, adjust to whatever the real API supports, keeping the same assertion intent: case-insensitive prefix check, clear failure message naming the offending assembly.)

- [ ] **Step 5: Run the new test — it should already pass** if Tasks 1–5 are correctly implemented (it's a regression guard, not a red-to-green feature). If it fails, that means Task 4 or 5 left a real EF reference in the graph — go find and fix it in the task that introduced it, do not weaken this test.

- [ ] **Step 6: Build + full realm suite.** `dotnet build Yggdrasil.slnx` and `dotnet test Yggdrasil.slnx` (Docker-backed E2E suites may still fail on the OLD fixture shape until Task 7 lands — that's expected and is Task 7's job, not this task's).

- [ ] **Step 7: Commit:** `refactor: Hosting.Web.Server composition simplified, provably EF-Core-free`.

---

### Task 7: Yggdrasil — fix the E2E fixture (the original CI failure)

**Files:**
- Modify: `Yggdrasil/tests/Hosting.Web.Server.Tests/CountryLookupE2ETests.cs`
- Modify: `Yggdrasil/tests/Hosting.Web.Server.Tests/Hosting.Web.Server.Tests.csproj`

**Interfaces:**
- Consumes: `Hosting.Migrations.Service`'s real `AddNorseMigrations()` composition (Urðarbrunnr-generated, already shipped), the `Configuration["ConnectionStrings:<name>"]` test-host pattern already proven in `Hosting.Migrations.Service.Tests/NorseMigrationsGeneratedExtensionsTests.cs`.

This is the task that closes the loop the whole plan started from: once Tasks 4–6 land, `Hosting.Web.Server.Tests` has nothing left to directly reach for in Mimisbrunnr or Urðarbrunnr — the only legitimate path to a migrated/seeded database is composing the real migrations service.

- [ ] **Step 1: Read the current fixture in full** (`CountryLookupE2ETests.cs`, `CountryLookupPostgresFixture` class) before changing anything — reproduced in this plan's grounding research, but the file may have moved since; confirm current line numbers.

- [ ] **Step 2: Replace `CountryLookupPostgresFixture.InitializeAsync`.** Current body directly instantiates `NorseReferenceMigrationContributor`/`ReferenceDataSeedContributor`. Replace with:

```csharp
public async ValueTask InitializeAsync()
{
	await _container.StartAsync();
	ConnectionString = _container.GetConnectionString();

	var migrationsBuilder = Host.CreateApplicationBuilder();
	migrationsBuilder.Configuration["ConnectionStrings:norse_reference"] = ConnectionString;
	migrationsBuilder.AddNorseMigrations();

	await using var migrationsHost = migrationsBuilder.Build();
	var lifetime = migrationsHost.Services.GetRequiredService<IHostApplicationLifetime>();
	TaskCompletionSource stopped = new();
	using var registration = lifetime.ApplicationStopped.Register(() => stopped.SetResult());

	await migrationsHost.StartAsync();
	await stopped.Task; // AddNorseMigrations' generated composition calls StopApplication() when migrate+seed complete.
	await migrationsHost.StopAsync();
}
```

Remove the `using Norse.Reference.Data.Migrations;` and `using Norse.Persistence.EntityFramework.PostgreSQL;`/`ApplyNorseProviderOptions` usings this change makes dead (IDE0005, delete don't suppress) — confirm nothing else in the file still needs them (the `CountryLookupE2ETests` class's own `CreateHostAsync` calls `AddNorseReferenceService` which is unaffected, still needs its own usings).

- [ ] **Step 3: Update the csproj.** Remove these two now-unneeded lines from `Hosting.Web.Server.Tests.csproj`:
```xml
<PackageReference Include="Norse.Persistence.EntityFramework.PostgreSQL" VersionOverride="0.0.8" />
<PackageReference Include="Norse.Reference.Data.Migrations" VersionOverride="0.0.8" />
```
Add a `ProjectReference` to `Hosting.Migrations.Service` instead (needed for `AddNorseMigrations()` to be visible — check whether `Hosting.Migrations.Service.csproj` is itself packable/referenceable as a plain project or whether this needs a different wiring; if `AddNorseMigrations()` is a generated extension local to that project's own compilation and genuinely can't be referenced from a test project the normal way, STOP and report exactly what you found — this is the one place in this task where the mechanism might not transplant cleanly, and guessing at a workaround here is exactly the kind of thing this whole plan exists to avoid).

- [ ] **Step 4: Run the E2E suite.** `dotnet test Yggdrasil/tests/Hosting.Web.Server.Tests -- --filter-class "*CountryLookupE2ETests*"`. If Docker is unreachable in this environment, confirm via `docker info` and report the tests as structurally complete but unverified here — same handling as every other Docker-gated test in this plan and its parent well-and-wire plan.

- [ ] **Step 5: Full realm suite.** `dotnet build Yggdrasil.slnx` (zero warnings) and `dotnet test Yggdrasil.slnx`.

- [ ] **Step 6: Commit:** `fix: E2E fixture seeds through the real Hosting.Migrations.Service composition, not direct Mimisbrunnr/Urdarbrunnr references`.

---

## Plan Self-Review Notes

- Spec coverage: §1.2/§1.3 (leak + drift) → Tasks 1, 2, 4, 5; §3.1/§3.2 (the two new extensions) → Tasks 1, 2; §3.3 (migration-time unchanged) → proven by Task 3, not modified anywhere; §3.4 (seeding unchanged) → touched nowhere in this plan, correctly; §3.5/original CI failure → Task 7; §4 testing (parity test, assembly-boundary test, E2E fixture, existing factory tests untouched) → Tasks 3, 6, 7 respectively.
- Real, planning-time corrections to the approved spec, made because grounding research surfaced facts the spec didn't have: (1) Himinbjörg does NOT use the same `AddDbContextFactory` shape Mimir does — it uses `AddDbContext` (Identity needs direct injection), so Task 5 targets the existing `AddNorseContext<TContext>()`, not Task 2's new `AddNorseWell<TContext>()`; this is a real architectural fit, not a shortcut. (2) `AddNorseContext<TContext>()`/`AddNorseContextFactory<TContext>()` must be `IHostApplicationBuilder`-based (not `IServiceCollection`-based) because `INorseEfProvider.Enrich<TContext>()` requires the full builder for Aspire enrichment — this is why `AddNorseReferenceService`/`AddNorseAuthenticationService` change their outer receiver type, and why Task 6 splits Yggdrasil's composition chain into three statements instead of one.
- Task 5's Step 2 is a genuine "verify before assuming" gate — the plan does not know for certain Himinbjörg's csproj carries the same direct Npgsql reference Mimir's did, and says so rather than asserting it.
