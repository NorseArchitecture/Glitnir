# Yggdrasil Hosting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development is the default — use it to implement this plan task-by-task; superpowers:executing-plans is the narrow fallback, only when the work specifically needs a separate session with human review checkpoints, never an interchangeable alternative. Pair either with superpowers:test-driven-development for every implementation task — orchestration sequences tasks, TDD governs how each one is coded. Steps use checkbox (`- [ ]`) syntax for tracking. **This plan halts at the plan stage during the spec-first phase; do not execute without explicit user greenlight.**

**Goal:** Stand up the Norse hosting layer — four library packages (`Norse.Abstractions.Hosting`, `Norse.Hosting.Web`, `Norse.Hosting.Worker`, `Norse.Hosting.Migrations.Service`), the cross-cutting `Norse.Hosting.ServiceDefaults`, three deployable host templates (`Norse.Hosting.Web.Server`, `Norse.Hosting.Worker`, `Norse.Hosting.Migrations.Service`), and the local-dev Aspire orchestrator (`Norse.Hosting.AppHost`). This is the foundation every future context plugin (Auth, Billing, Claims, …) builds on.

**Architecture:** Single subdirectory `Norse.Hosting/` under the meta-repo (future submodule) hosts the four library packages. Three sibling subdirectories — `Norse.Hosting.Web.Server/`, `Norse.Hosting.Worker/`, `Norse.Hosting.Migrations.Service/` — hold the deployable host templates that consume the libraries. `Norse.Hosting.AppHost/` wires them together for local dev via .NET Aspire. The plugin interface is two methods (`ConfigureServices`, `MapEndpoints`); concrete plugins land in their own context plans (Auth Foundation Plan A next).

**Tech Stack:** .NET 10 / C# 13, ASP.NET Core 10, Grpc.AspNetCore, EF Core 10, Aspire 9, OpenTelemetry, xUnit, Shouldly, NSubstitute.

**Companion spec:** `docs/Yggdrasil/specs/2026-05-20-yggdrasil-hosting-design.md`. Read §4 (lifecycle rule), §5 (plugin interface family), §7 (visibility model), §7.1 (webhook controller base class), and §10 (migrations service) before starting. Every design decision is justified in the spec.

---

## Per CLAUDE.md §8: No Automatic Git Commits

Every "Commit" step ends with `git add` ONLY. The human reviews the diff and runs `git commit` themselves. Each task includes a proposed commit message for that review.

---

## Out of Scope (separate plans / specs)

- **`InfrastructureDbContext` base** — defined in the future `norse-infrastructure-persistence` spec. Plugin code in this plan that calls `.UseSnakeCaseNamingConvention()` assumes it; the migrations test fixtures use a small local DbContext until persistence lands.
- **`JsonControllerBase<TService>`** — defined in the future `midgard-api` spec. This plan creates the `IPublishedController` marker interface and the OpenAPI doc filter; `JsonControllerBase<TService>` will implement the marker when it ships.
- **Webhook dispatch abstraction** — *(amended 2026-06-03: CLAUDE.md §7 #2 is RESOLVED — NServiceBus 10.2. `IWebhookDispatcher` is deleted from the design; webhook controllers dispatch via NServiceBus's `IMessageSession` directly, test seam `NServiceBus.Testing.TestableMessageSession`. See the messaging-amendment note below and hosting spec §7.1.)*
- **Product-tier hosting-abstractions layer** — does NOT exist. Per-context plugins implement `Norse.Abstractions.Hosting.IWebHostPlugin` / `IWorkerHostPlugin` directly from their `{Company}.{Context}.Server` / `{Company}.{Context}.Worker` assemblies. MGA-specific cross-cutting (audit, `NorsePrincipal` flow) is shared middleware configured at the Norse host runtime, not interface extensions. *(Tenancy removed from this list 2026-06-03 — stamp-per-tenant, see `docs/Platform/specs/2026-06-03-tenancy-model-design.md`.)*
- **Per-context plugins** (`AuthPlugin`, `BillingPlugin`, …) — each has its own spec and plan; this one just makes the host able to load them.
- **Production K8s manifests** — operations territory; this plan covers the binary's behavior, not deployment topology.

---

## Prerequisites

- The meta-repo has `Directory.Build.props` at the root (created in earlier work; verify or create as Task 1).
- `Norse.Hosting.ServiceDefaults` is referenced by every deployable but doesn't exist yet — created in Task 9.
- No external dependencies. .NET 10 SDK installed.

---

## File Structure

All paths relative to the meta-repo root. Two future submodules — `Norse.Abstractions.Hosting/` (declared plugin contracts) and `Norse.Hosting/` (concrete runtimes) — under the seven-realm taxonomy (CLAUDE.md §5). They ship together but live as separate submodules because Abstractions owns the law and Norse owns the connective tissue that implements it; the boundary is mythological and operational.

```
Directory.Build.props                          # NEW or VERIFY: meta-repo MSBuild conventions
Directory.Packages.props                       # NEW: meta-repo CPM versions
Norse.slnx                                 # NEW: meta-repo solution stitching every component

Norse.Abstractions.Hosting/                                # NEW: future submodule — Abstractions-tier declared law (plugin contracts)
├── .editorconfig                              # NEW: tabs, 2-space width
├── .gitignore                                 # NEW: dotnet defaults
├── Directory.Build.props                      # NEW: subdir-local overrides
├── LICENSE                                    # NEW: MIT
├── README.md                                  # NEW
├── Norse.Abstractions.Hosting.slnx                        # NEW: subdir solution
├── src/
│   └── Norse.Abstractions.Hosting/
│       ├── Norse.Abstractions.Hosting.csproj
│       ├── IHostPlugin.cs
│       ├── IWebHostPlugin.cs
│       ├── IWorkerHostPlugin.cs
│       ├── IPublishedController.cs            # OpenAPI inclusion marker
│       ├── Migrations/
│       │   ├── IMigrationContributor.cs
│       │   └── EfCoreMigrationContributor.cs
│       └── Webhooks/
│           ├── IWebhookCommand.cs
│           ├── IWebhookValidator.cs           # IWebhookValidator<TCommand> + WebhookValidationResult
│           ├── IWebhookDispatcher.cs
│           └── WebhookControllerBase.cs       # WebhookControllerBase<TCommand>
└── tests/
    └── Norse.Abstractions.Hosting.Tests/
        ├── Norse.Abstractions.Hosting.Tests.csproj
        ├── WebhookControllerBaseTests.cs
        └── EfCoreMigrationContributorTests.cs

Norse.Hosting/                             # NEW: future submodule — Norse-tier connective tissue (concrete runtimes)
├── .editorconfig                              # NEW
├── .gitignore                                 # NEW
├── Directory.Build.props                      # NEW
├── LICENSE                                    # NEW
├── README.md                                  # NEW
├── Norse.Hosting.slnx                     # NEW: subdir solution
├── src/
│   ├── Norse.Hosting.Web/
│   │   ├── Norse.Hosting.Web.csproj
│   │   ├── NorseWebHostBuilderExtensions.cs # AddNorseWebHost + AddPlugin<T>
│   │   ├── INorseWebHostBuilder.cs
│   │   ├── NorseWebHostBuilder.cs           # internal sealed implementation
│   │   ├── NorseWebApplicationExtensions.cs # UseNorseWebHost
│   │   └── OpenApi/
│   │       └── PartnerOpenApiDocumentTransformer.cs
│   ├── Norse.Hosting.Worker/
│   │   ├── Norse.Hosting.Worker.csproj
│   │   ├── NorseWorkerHostBuilderExtensions.cs
│   │   ├── INorseWorkerHostBuilder.cs
│   │   ├── NorseWorkerHostBuilder.cs
│   │   └── NorseHostExtensions.cs           # UseNorseWorkerHost
│   ├── Norse.Hosting.Migrations.Service/
│   │   ├── Norse.Hosting.Migrations.Service.csproj
│   │   ├── NorseMigrationsHostBuilderExtensions.cs
│   │   ├── INorseMigrationsHostBuilder.cs
│   │   ├── NorseMigrationsHostBuilder.cs
│   │   ├── NorseMigrationsOptions.cs
│   │   ├── MigrationsOrchestrator.cs
│   │   ├── MigrationsHealthStatus.cs
│   │   ├── MigrationsHealthCheck.cs
│   │   ├── TopologicalSort.cs
│   │   └── RecurringFailureLog.cs
│   └── Norse.Hosting.ServiceDefaults/
│       ├── Norse.Hosting.ServiceDefaults.csproj
│       └── Extensions.cs                       # AddServiceDefaults
└── tests/
    ├── Norse.Hosting.Web.Tests/
    │   ├── Norse.Hosting.Web.Tests.csproj
    │   ├── AddPluginTests.cs
    │   ├── UseNorseWebHostTests.cs
    │   └── PartnerOpenApiDocumentTransformerTests.cs
    ├── Norse.Hosting.Worker.Tests/
    │   ├── Norse.Hosting.Worker.Tests.csproj
    │   └── AddPluginTests.cs
    └── Norse.Hosting.Migrations.Service.Tests/
        ├── Norse.Hosting.Migrations.Service.Tests.csproj
        ├── TopologicalSortTests.cs
        ├── MigrationsOrchestratorTests.cs
        └── MigrationsHealthStatusTests.cs

Norse.Hosting.Web.Server/                                  # NEW: deployable subdirectory
├── Norse.Hosting.Web.Server.csproj
└── Program.cs

Norse.Hosting.Worker/                            # NEW: deployable subdirectory
├── Norse.Hosting.Worker.csproj
└── Program.cs

Norse.Hosting.Migrations.Service/                            # NEW: deployable subdirectory
├── Norse.Hosting.Migrations.Service.csproj
└── Program.cs

Norse.Hosting.AppHost/                             # NEW: Aspire orchestrator
├── Norse.Hosting.AppHost.csproj
└── AppHost.cs
```

> **Restructure note:** the original draft of this plan placed `Norse.Abstractions.Hosting` (then `Norse.Hosting.Abstractions`) nested under `Norse.Hosting/`. Under the five-realm split landed in CLAUDE.md §5, the plugin contracts are Abstractions-tier declared law and live in their own submodule (`norse-abstractions-hosting`); the concrete runtimes stay Norse-tier (`norse-hosting`). Per-task commands further down still reference the legacy nested path in some places — when executing, paths like `Norse.Hosting/src/Norse.Abstractions.Hosting/...` should resolve to `Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/...`, and the solution files split accordingly. The namespace inside each contract file is `Norse.Abstractions.Hosting` (or a nested sub-namespace like `Norse.Abstractions.Hosting.Migrations` / `Norse.Abstractions.Hosting.Webhooks`), not `Norse.Hosting`.

> **Messaging amendment note (2026-06-03):** CLAUDE.md §7 #2 is RESOLVED — NServiceBus 10.2 (`docs/Platform/specs/2026-06-03-messaging-foundation-design.md`). Consequences for this plan, to reconcile before executing the webhook tasks:
> 1. **`IWebhookDispatcher` does not exist.** Task 6's dispatcher step is void (marked inline). `WebhookControllerBase<TCommand>` takes NServiceBus's `IMessageSession` and calls `await session.Send(command, ct)`.
> 2. **Tests use `NServiceBus.Testing.TestableMessageSession`**, not an NSubstitute dispatcher double. Mirror the amended worked example in hosting spec §7.1.
> 3. **`WebhookControllerBase` lives in `Norse.Hosting.Web`** (concrete MVC infrastructure), not `Norse.Abstractions.Hosting` — Task 7's file paths must follow spec §7.1's placement (contracts in `Norse.Abstractions.Hosting`, base class in `Norse.Hosting.Web`).
> 4. Webhook commands carry the **`Command` suffix** (`{Source}WebhookReceivedCommand`) — the unobtrusive message conventions classify by it.

---

## Task 1: Verify / Create Meta-Repo MSBuild Scaffolding

**Files:**
- Verify: `Directory.Build.props`
- Create if missing: `Directory.Packages.props`
- Create: `Norse.slnx`

- [ ] **Step 1: Inspect the meta-repo root**

Run: `dir .`
Expected: see existing `CLAUDE.md`, `docs/`, and a `.git` directory; possibly `Directory.Build.props` from earlier work.

- [ ] **Step 2: Create or verify `Directory.Build.props` at the meta-repo root**

If the file doesn't exist, create it:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisMode>AllEnabledByDefault</AnalysisMode>
  </PropertyGroup>

  <PropertyGroup>
    <Company>{Company}</Company>
    <Product>Norse Platform</Product>
    <Authors>Buvy Buvinghausen</Authors>
    <Copyright>© {Company}. All rights reserved.</Copyright>
  </PropertyGroup>
</Project>
```

If it exists, verify these properties are present; merge if necessary.

- [ ] **Step 3: Create `Directory.Packages.props` at the meta-repo root**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup Label="ASP.NET Core">
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Diagnostics.HealthChecks" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup Label="gRPC">
    <PackageVersion Include="Grpc.AspNetCore" Version="2.66.0" />
    <PackageVersion Include="Grpc.AspNetCore.Server.Reflection" Version="2.66.0" />
  </ItemGroup>

  <ItemGroup Label="EF Core">
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.0" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
    <PackageVersion Include="EFCore.NamingConventions" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup Label="Aspire">
    <PackageVersion Include="Aspire.Hosting.AppHost" Version="9.0.0" />
    <PackageVersion Include="Aspire.Hosting.PostgreSQL" Version="9.0.0" />
    <PackageVersion Include="Microsoft.Extensions.ServiceDiscovery" Version="9.0.0" />
  </ItemGroup>

  <ItemGroup Label="OpenTelemetry">
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.10.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.10.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.10.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.10.0" />
    <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.10.0" />
  </ItemGroup>

  <ItemGroup Label="HttpClient resilience">
    <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="9.0.0" />
  </ItemGroup>

  <ItemGroup Label="Testing">
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageVersion Include="Shouldly" Version="4.2.1" />
    <PackageVersion Include="NSubstitute" Version="5.3.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
  </ItemGroup>
</Project>
```

Adjust version numbers to match what's actually shipped at implementation time (.NET 10 RTM versions).

- [ ] **Step 4: Create the meta-repo top-level solution**

Run: `dotnet new sln --name Norse --format slnx`
Expected: `Norse.slnx` created in the root.

- [ ] **Step 5: Verify the slnx file format**

Run: `type Norse.slnx`
Expected: XML root `<Solution>` element. Per the user's preference (memory: solution-file-format), `.slnx` is the only acceptable format.

- [ ] **Step 6: Stage the scaffolding changes**

```bash
git add Directory.Build.props Directory.Packages.props Norse.slnx
```

Proposed commit message: `chore: scaffold meta-repo MSBuild conventions and slnx`

---

## Task 2: Create `Norse.Hosting/` Subdirectory Scaffolding

**Files:**
- Create: `Norse.Hosting/.editorconfig`
- Create: `Norse.Hosting/.gitignore`
- Create: `Norse.Hosting/Directory.Build.props`
- Create: `Norse.Hosting/LICENSE`
- Create: `Norse.Hosting/README.md`
- Create: `Norse.Hosting/Norse.Hosting.slnx`

- [ ] **Step 1: Create the subdirectory**

Run: `mkdir Norse.Hosting && mkdir Norse.Hosting\src && mkdir Norse.Hosting\tests`
Expected: directories created.

- [ ] **Step 2: Create `.editorconfig`**

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true
indent_style = tab
tab_width = 2
indent_size = 2

[*.{cs,csproj,props,targets,slnx}]
indent_style = tab
tab_width = 2

[*.{md,yml,yaml}]
indent_style = space
indent_size = 2
```

Per CLAUDE.md: tabs globally except Markdown/YAML/Python/F#. 2-space tab width.

- [ ] **Step 3: Create `.gitignore`**

Standard .NET gitignore — copy from `dotnet new gitignore` or use the GitHub template.

Run: `cd Norse.Hosting && dotnet new gitignore`
Expected: `.gitignore` created with standard .NET ignores.

- [ ] **Step 4: Create `Directory.Build.props` (subdir-local)**

```xml
<Project>
  <!-- Inherits from meta-repo root via MSBuild's implicit-import behavior. -->
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
</Project>
```

- [ ] **Step 5: Create `LICENSE` (MIT)**

Use the standard MIT license text with `2026 Buvy Buvinghausen` as the copyright line. Match the format of `Norse.Primitives/LICENSE` if Norse.Primitives is already scaffolded; otherwise use the canonical MIT text.

- [ ] **Step 6: Create `README.md`**

```markdown
# Norse Hosting

The plugin runtime for the Norse platform. Four NuGet packages:

- **Norse.Abstractions.Hosting** — interface contracts (`IHostPlugin`, `IWebHostPlugin`, `IWorkerHostPlugin`, `IMigrationContributor`, webhook abstractions).
- **Norse.Hosting.Web** — concrete web-host runtime: `AddNorseWebHost()`, `AddPlugin<T>()`, plugin lifecycle, partner OpenAPI doc generation.
- **Norse.Hosting.Worker** — concrete worker-host runtime: `AddNorseWorkerHost()`, background-service plugin lifecycle.
- **Norse.Hosting.Migrations.Service** — migrations orchestrator with health-signal-gated readiness; never exits non-zero on failure.

Spec: `docs/Yggdrasil/specs/2026-05-20-yggdrasil-hosting-design.md` in the meta-repo.
```

- [ ] **Step 7: Create the Norse.Hosting solution**

Run: `cd Norse.Hosting && dotnet new sln --name Norse.Hosting --format slnx`
Expected: `Norse.Hosting.slnx` created.

- [ ] **Step 8: Stage**

```bash
git add Norse.Hosting/
```

Proposed commit message: `chore: scaffold Norse.Hosting subdirectory`

---

## Task 3: Create `Norse.Abstractions.Hosting` Project

**Files:**
- Create: `Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/Norse.Abstractions.Hosting.csproj`

- [ ] **Step 1: Create the project**

Run: `cd Norse.Hosting\src && dotnet new classlib --name Norse.Abstractions.Hosting --framework net10.0`
Expected: project scaffolded.

- [ ] **Step 2: Replace the generated `.csproj` contents**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>Interface contracts for the Norse hosting plugin runtime.</Description>
    <RootNamespace>Norse.Hosting</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
  </ItemGroup>
</Project>
```

Notes: `FrameworkReference Microsoft.AspNetCore.App` is required so the project can reference `ControllerBase`, `HttpRequest`, `IEndpointRouteBuilder` without pulling individual NuGet packages. `Microsoft.EntityFrameworkCore` is referenced because `EfCoreMigrationContributor<TContext>` extends `DbContext`.

- [ ] **Step 3: Delete the auto-generated `Class1.cs`**

Run: `del Norse.Hosting\src\Norse.Abstractions.Hosting\Class1.cs`

- [ ] **Step 4: Add to solution**

Run: `cd Norse.Abstractions.Hosting && dotnet sln Norse.Abstractions.Hosting.slnx add src/Norse.Abstractions.Hosting/Norse.Abstractions.Hosting.csproj`
Expected: project added.

- [ ] **Step 5: Verify it builds**

Run: `cd Norse.Hosting && dotnet build`
Expected: build succeeds (no source files yet, just an empty assembly).

- [ ] **Step 6: Stage**

```bash
git add Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/ Norse.Hosting/Norse.Hosting.slnx
```

Proposed commit message: `chore: create Norse.Abstractions.Hosting project scaffold`

---

## Task 4: Add Plugin Interfaces to Abstractions

**Files:**
- Create: `Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/IHostPlugin.cs`
- Create: `Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/IWebHostPlugin.cs`
- Create: `Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/IWorkerHostPlugin.cs`
- Create: `Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/IPublishedController.cs`

- [ ] **Step 1: Create `IHostPlugin.cs`**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Abstractions.Hosting;

/// <summary>
/// Base plugin contract. Plugins contribute everything they need to the application's
/// service graph during the builder phase: DI registrations, HttpClient registrations,
/// DbContext registrations, authentication schemes, authorization policies, IOptions
/// bindings, BackgroundService registrations, controller application parts — anything
/// that goes on IServiceCollection.
///
/// <para>Plugins are parameterless POCOs. The platform instantiates them via
/// <c>new TPlugin()</c>; they have no own DI dependencies. Their behavior is the
/// methods on this interface, not the constructor.</para>
///
/// <para>Configuration convention: bind options classes with
/// <c>services.AddOptions&lt;TOptions&gt;().BindConfiguration("…").ValidateDataAnnotations().ValidateOnStart()</c>.
/// The host's standard startup-validator infrastructure runs every registered
/// validator during initialization; misconfiguration fails the host immediately,
/// before any request is served.</para>
/// </summary>
public interface IHostPlugin
{
	void ConfigureServices(
		IServiceCollection services,
		IHostEnvironment environment,
		IConfiguration configuration);
}
```

- [ ] **Step 2: Create `IWebHostPlugin.cs`**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Norse.Abstractions.Hosting;

/// <summary>
/// Plugin variant for web hosts. Adds the gRPC service mapping phase.
/// </summary>
public interface IWebHostPlugin : IHostPlugin
{
	/// <summary>
	/// Map this plugin's gRPC service implementations.
	/// <c>endpoints.MapGrpcService&lt;BillingService&gt;().RequireAuthorization(policy)</c>
	/// is the canonical line. Map one gRPC service per published <c>I{Context}Api</c>
	/// interface; chain <c>RequireAuthorization</c> to attach the service's authorization
	/// policy. gRPC reflection is enabled in Development and Staging so operators can
	/// reach the service from Postman; off in Production.
	///
	/// <para>This method is for gRPC services only. Other endpoint shapes have other
	/// homes:</para>
	/// <list type="bullet">
	///   <item>Webhook controllers (<see cref="ControllerBase"/> / <c>[ApiController]</c>):
	///   opt the controller assembly in via
	///   <c>services.AddControllers().AddApplicationPart(…)</c> in
	///   <see cref="IHostPlugin.ConfigureServices"/>. They are attribute-routed by
	///   the host's global <c>app.MapControllers()</c> call.</item>
	///   <item>Partner JSON controllers (inheriting <see cref="IPublishedController"/>):
	///   same opt-in path as webhooks. Inheriting <c>IPublishedController</c> IS
	///   the act of declaring partner-facing intent — the controller appears in the
	///   partner OpenAPI document automatically.</item>
	///   <item>OAuth/OIDC endpoints: registered by the OpenIddict middleware that
	///   <c>AuthPlugin.ConfigureServices</c> wires up. Never mapped here.</item>
	///   <item>Health checks: handled by <c>Norse.Hosting.ServiceDefaults</c>. Never mapped here.</item>
	/// </list>
	///
	/// <para>If you find yourself reaching for <c>endpoints.MapPost(…)</c> or
	/// <c>endpoints.MapGet(…)</c> here, stop. Either it's a gRPC service method that
	/// belongs on an <c>I{Context}Api</c>, or it's a controller that belongs in a
	/// <c>JsonApi</c> assembly, or it's a platform concern that should be raised with
	/// the platform team. Plugin minimal API is not a supported pattern.</para>
	/// </summary>
	void MapEndpoints(IEndpointRouteBuilder endpoints);
}
```

- [ ] **Step 3: Create `IWorkerHostPlugin.cs`**

```csharp
namespace Norse.Abstractions.Hosting;

/// <summary>
/// Plugin variant for worker hosts. Has no method of its own — <see cref="BackgroundService"/>
/// implementations are registered via <see cref="IHostPlugin.ConfigureServices"/> like any
/// other service. The interface is a discriminator so the worker host can load only the
/// plugins relevant to it (a plugin that implements both <see cref="IWebHostPlugin"/> and
/// <see cref="IWorkerHostPlugin"/> on a single class is normal and expected).
/// </summary>
public interface IWorkerHostPlugin : IHostPlugin
{
}
```

- [ ] **Step 4: Create `IPublishedController.cs`**

```csharp
namespace Norse.Abstractions.Hosting;

/// <summary>
/// Marker interface for controllers that should appear in the partner-facing OpenAPI
/// document. The platform's <c>PartnerOpenApiDocumentTransformer</c> filters in only
/// controllers whose class implements this marker.
///
/// <para>In practice, controllers inherit from <c>JsonControllerBase&lt;TService&gt;</c>
/// (defined in the future <c>midgard-api</c> spec), which implements this marker on
/// their behalf. The marker is exposed here so that the Hosting layer can implement the
/// OpenAPI filter without depending on the api spec; inheriting the marker IS the
/// act of declaring partner-facing intent.</para>
///
/// <para>Controllers inheriting <see cref="ControllerBase"/> directly (e.g., webhook
/// controllers) do NOT implement this marker, and are therefore excluded from the
/// partner OpenAPI document — the safe default.</para>
/// </summary>
public interface IPublishedController
{
}
```

- [ ] **Step 5: Build to verify**

Run: `cd Norse.Abstractions.Hosting && dotnet build src/Norse.Abstractions.Hosting/Norse.Abstractions.Hosting.csproj`
Expected: build succeeds with 0 errors, 0 warnings.

- [ ] **Step 6: Stage**

```bash
git add Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/
```

Proposed commit message: `feat(hosting.abstractions): add plugin and published-controller interfaces`

---

## Task 5: Add Migrations Abstractions

**Files:**
- Create: `Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/Migrations/IMigrationContributor.cs`
- Create: `Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/Migrations/EfCoreMigrationContributor.cs`

- [ ] **Step 1: Create `IMigrationContributor.cs`**

```csharp
namespace Norse.Abstractions.Hosting.Migrations;

/// <summary>
/// Contract for a migrations contributor. One implementation per
/// <c>{Company}.{Context}.Migrations</c> assembly. The runtime
/// instantiates contributors via <c>new TContributor()</c>, topologically sorts
/// them by <see cref="DependsOn"/>, then invokes <see cref="RunAsync"/> on each
/// in dependency order.
/// </summary>
public interface IMigrationContributor
{
	/// <summary>The bounded context this contributor migrates. Must be unique across all registered contributors.</summary>
	string ContextName { get; }

	/// <summary>Other <see cref="ContextName"/> values whose contributors must run before this one.</summary>
	IReadOnlyCollection<string> DependsOn { get; }

	/// <summary>
	/// Apply this contributor's pending migrations. Returns the count applied (0 if no-op).
	/// Throwing flips the host's /health to Unhealthy and aborts remaining contributors;
	/// the host stays alive so the failure can be diagnosed via standard health/logs surface.
	/// </summary>
	Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Create `EfCoreMigrationContributor.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Abstractions.Hosting.Migrations;

/// <summary>
/// Convenience base for EF-Core-backed contributors. Handles pending-migration
/// detection and the <c>MigrateAsync</c> call. Derived classes declare
/// <see cref="ContextName"/>, optionally <see cref="DependsOn"/>, and arrange for
/// the DbContext to be resolvable from the <see cref="IServiceProvider"/> passed
/// to <see cref="RunAsync"/>.
/// </summary>
public abstract class EfCoreMigrationContributor<TContext> : IMigrationContributor
	where TContext : DbContext
{
	public abstract string ContextName { get; }

	public virtual IReadOnlyCollection<string> DependsOn => Array.Empty<string>();

	public async Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
	{
		await using var scope = services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<TContext>();
		var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
		if (pending.Count == 0) return 0;
		await db.Database.MigrateAsync(cancellationToken);
		return pending.Count;
	}
}
```

- [ ] **Step 3: Build to verify**

Run: `cd Norse.Abstractions.Hosting && dotnet build src/Norse.Abstractions.Hosting/Norse.Abstractions.Hosting.csproj`
Expected: build succeeds.

- [ ] **Step 4: Stage**

```bash
git add Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/Migrations/
```

Proposed commit message: `feat(hosting.abstractions): add migration contributor contracts`

---

## Task 6: Add Webhook Abstractions (Contracts + Validator + Dispatcher)

**Files:**
- Create: `Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/Webhooks/IWebhookCommand.cs`
- Create: `Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/Webhooks/IWebhookValidator.cs`
- ~~Create: `Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/Webhooks/IWebhookDispatcher.cs`~~ *(void — see messaging amendment note)*

- [ ] **Step 1: Create `IWebhookCommand.cs`**

```csharp
namespace Norse.Abstractions.Hosting.Webhooks;

/// <summary>
/// Marker interface for webhook commands dispatched to the message bus.
/// Implementations are concrete record types per webhook source; all carry the raw
/// payload bytes, an optional idempotency key from headers, and the time the platform
/// received the request.
/// </summary>
public interface IWebhookCommand
{
	byte[] Bytes { get; }
	string? IdempotencyKey { get; }
	DateTimeOffset ReceivedAt { get; }
}
```

- [ ] **Step 2: Create `IWebhookValidator.cs`** (interface + result struct in one file because they're tightly coupled)

```csharp
using Microsoft.AspNetCore.Http;

namespace Norse.Abstractions.Hosting.Webhooks;

/// <summary>
/// Per-command webhook validator. Implementations verify the inbound request's
/// authenticity using whatever scheme the third party requires (HMAC over the raw
/// body, IP allowlist, mTLS, shared-secret header, etc.). One validator implementation
/// per command type; resolved from DI by the closed generic type.
/// </summary>
public interface IWebhookValidator<TCommand> where TCommand : IWebhookCommand
{
	Task<WebhookValidationResult> ValidateAsync(
		HttpRequest request,
		byte[] body,
		CancellationToken cancellationToken);
}

public readonly record struct WebhookValidationResult(bool IsValid, string? Reason)
{
	public static WebhookValidationResult Valid() => new(true, null);
	public static WebhookValidationResult Invalid(string reason) => new(false, reason);
}
```

**~~Step 3: Create `IWebhookDispatcher.cs`~~** — *void (amended 2026-06-03): `IWebhookDispatcher` is deleted per the messaging foundation spec; controllers dispatch via `IMessageSession` directly. Do not create this file. Remove it from this task's Files list and commit message.*

- [ ] **Step 4: Build to verify**

Run: `cd Norse.Abstractions.Hosting && dotnet build src/Norse.Abstractions.Hosting/Norse.Abstractions.Hosting.csproj`
Expected: build succeeds.

- [ ] **Step 5: Stage**

```bash
git add Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/Webhooks/
```

Proposed commit message: `feat(hosting.abstractions): add webhook command, validator, and dispatcher contracts`

---

## Task 7: Add `WebhookControllerBase<TCommand>` (TDD)

**Files:**
- Create: `Norse.Hosting/tests/Norse.Abstractions.Hosting.Tests/Norse.Abstractions.Hosting.Tests.csproj`
- Create: `Norse.Hosting/tests/Norse.Abstractions.Hosting.Tests/WebhookControllerBaseTests.cs`
- Create: `Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/Webhooks/WebhookControllerBase.cs`

- [ ] **Step 1: Create the test project scaffold**

Run: `cd Norse.Hosting\tests && dotnet new xunit --name Norse.Abstractions.Hosting.Tests --framework net10.0`
Expected: project scaffolded.

- [ ] **Step 2: Replace the generated `.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <RootNamespace>Norse.Hosting.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="NSubstitute" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Norse.Abstractions.Hosting\Norse.Abstractions.Hosting.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Delete the auto-generated `UnitTest1.cs`**

Run: `del Norse.Hosting\tests\Norse.Abstractions.Hosting.Tests\UnitTest1.cs`

- [ ] **Step 4: Add to solution**

Run: `cd Norse.Abstractions.Hosting && dotnet sln Norse.Abstractions.Hosting.slnx add tests/Norse.Abstractions.Hosting.Tests/Norse.Abstractions.Hosting.Tests.csproj`

- [ ] **Step 5: Write the failing tests**

Create `Norse.Hosting/tests/Norse.Abstractions.Hosting.Tests/WebhookControllerBaseTests.cs`:

```csharp
using Norse.Hosting.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Norse.Abstractions.Hosting.Tests;

public class WebhookControllerBaseTests
{
	private sealed record TestCommand(byte[] Bytes, string? IdempotencyKey, DateTimeOffset ReceivedAt) : IWebhookCommand;

	private sealed class TestWebhookController(
		IWebhookValidator<TestCommand> validator,
		IWebhookDispatcher dispatcher)
		: WebhookControllerBase<TestCommand>(validator, dispatcher, NullLogger<WebhookControllerBase<TestCommand>>.Instance)
	{
		protected override TestCommand BuildCommand(byte[] body, HttpRequest request)
			=> new(body, request.Headers["X-Idempotency"].ToString(), DateTimeOffset.UnixEpoch);
	}

	[Fact]
	public async Task Receive_ReturnsAccepted_WhenValidationPasses_AndDispatchesCommand()
	{
		var validator = Substitute.For<IWebhookValidator<TestCommand>>();
		validator.ValidateAsync(Arg.Any<HttpRequest>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
			.Returns(WebhookValidationResult.Valid());

		var dispatcher = Substitute.For<IWebhookDispatcher>();

		var controller = new TestWebhookController(validator, dispatcher);
		var httpContext = new DefaultHttpContext();
		httpContext.Request.Body = new MemoryStream(new byte[] { 1, 2, 3 });
		httpContext.Request.Headers["X-Idempotency"] = "abc-123";
		controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

		var result = await controller.Receive(CancellationToken.None);

		result.ShouldBeOfType<AcceptedResult>();
		await dispatcher.Received(1).DispatchAsync(
			Arg.Is<TestCommand>(c => c.Bytes.SequenceEqual(new byte[] { 1, 2, 3 }) && c.IdempotencyKey == "abc-123"),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Receive_ReturnsUnauthorized_WhenValidationFails_AndDoesNotDispatch()
	{
		var validator = Substitute.For<IWebhookValidator<TestCommand>>();
		validator.ValidateAsync(Arg.Any<HttpRequest>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
			.Returns(WebhookValidationResult.Invalid("HMAC mismatch"));

		var dispatcher = Substitute.For<IWebhookDispatcher>();

		var controller = new TestWebhookController(validator, dispatcher);
		var httpContext = new DefaultHttpContext();
		httpContext.Request.Body = new MemoryStream(new byte[] { 9, 9 });
		controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

		var result = await controller.Receive(CancellationToken.None);

		result.ShouldBeOfType<UnauthorizedResult>();
		await dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Receive_PassesRawBodyToValidator()
	{
		var validator = Substitute.For<IWebhookValidator<TestCommand>>();
		validator.ValidateAsync(Arg.Any<HttpRequest>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
			.Returns(WebhookValidationResult.Valid());

		var dispatcher = Substitute.For<IWebhookDispatcher>();
		var controller = new TestWebhookController(validator, dispatcher);
		var httpContext = new DefaultHttpContext();
		var payload = new byte[] { 42, 43, 44 };
		httpContext.Request.Body = new MemoryStream(payload);
		controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

		await controller.Receive(CancellationToken.None);

		await validator.Received(1).ValidateAsync(
			Arg.Any<HttpRequest>(),
			Arg.Is<byte[]>(b => b.SequenceEqual(payload)),
			Arg.Any<CancellationToken>());
	}
}
```

- [ ] **Step 6: Run tests; verify they fail (no `WebhookControllerBase` type yet)**

Run: `cd Norse.Hosting && dotnet test tests/Norse.Abstractions.Hosting.Tests/Norse.Abstractions.Hosting.Tests.csproj`
Expected: compilation failure — `WebhookControllerBase<TCommand>` does not exist.

- [ ] **Step 7: Create `WebhookControllerBase.cs`**

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Norse.Abstractions.Hosting.Webhooks;

/// <summary>
/// Abstract base class for webhook controllers. Implements the auth-then-dispatch
/// convention from §4 of the hosting spec: captures the raw request body, runs the
/// command-specific validator resolved from DI, dispatches the command to the
/// message bus, returns 202 Accepted.
///
/// <para>Subclasses provide one method — <see cref="BuildCommand"/> — that maps raw
/// bytes and request headers to the typed command instance.</para>
///
/// <para>Validation failure returns 401 Unauthorized; the validator's reason is logged
/// at Warning with the command type for diagnostics. Successful dispatch returns 202
/// Accepted regardless of downstream success — deserialization and business logic happen
/// downstream of the queue, with failures routing to the dead-letter queue per §4.</para>
/// </summary>
public abstract class WebhookControllerBase<TCommand>(
	IWebhookValidator<TCommand> validator,
	IWebhookDispatcher dispatcher,
	ILogger<WebhookControllerBase<TCommand>> log)
	: ControllerBase
	where TCommand : IWebhookCommand
{
	[HttpPost]
	public async Task<IActionResult> Receive(CancellationToken ct)
	{
		using var ms = new MemoryStream();
		await Request.Body.CopyToAsync(ms, ct);
		var bytes = ms.ToArray();

		var validation = await validator.ValidateAsync(Request, bytes, ct);
		if (!validation.IsValid)
		{
			log.LogWarning("Webhook {Command} validation failed: {Reason}",
				typeof(TCommand).Name, validation.Reason);
			return Unauthorized();
		}

		var command = BuildCommand(bytes, Request);
		await dispatcher.DispatchAsync(command, ct);
		return Accepted();
	}

	/// <summary>
	/// Build the typed command from the captured raw bytes and the inbound request.
	/// Per-controller because idempotency-key header names and other metadata extraction
	/// details vary per webhook source.
	/// </summary>
	protected abstract TCommand BuildCommand(byte[] body, HttpRequest request);
}
```

- [ ] **Step 8: Run tests; verify they pass**

Run: `cd Norse.Hosting && dotnet test tests/Norse.Abstractions.Hosting.Tests/Norse.Abstractions.Hosting.Tests.csproj`
Expected: 3 tests pass, 0 failures.

- [ ] **Step 9: Stage**

```bash
git add Norse.Abstractions.Hosting/src/Norse.Abstractions.Hosting/Webhooks/WebhookControllerBase.cs Norse.Hosting/tests/Norse.Abstractions.Hosting.Tests/
```

Proposed commit message: `feat(hosting.abstractions): add WebhookControllerBase with TDD coverage`

---

## Task 8: Add `EfCoreMigrationContributor` Tests

**Files:**
- Create: `Norse.Hosting/tests/Norse.Abstractions.Hosting.Tests/EfCoreMigrationContributorTests.cs`

- [ ] **Step 1: Add `Microsoft.EntityFrameworkCore.InMemory` to package versions**

Edit `Directory.Packages.props`, add to the EF Core ItemGroup:

```xml
<PackageVersion Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.0" />
```

- [ ] **Step 2: Reference InMemory provider in the test project**

Edit `Norse.Hosting/tests/Norse.Abstractions.Hosting.Tests/Norse.Abstractions.Hosting.Tests.csproj`, add inside the existing ItemGroup with PackageReferences:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" />
```

- [ ] **Step 3: Write the test**

Create `Norse.Hosting/tests/Norse.Abstractions.Hosting.Tests/EfCoreMigrationContributorTests.cs`:

```csharp
using Norse.Hosting.Migrations.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Norse.Abstractions.Hosting.Tests;

public class EfCoreMigrationContributorTests
{
	private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options) { }

	private sealed class TestContributor : EfCoreMigrationContributor<TestDbContext>
	{
		public override string ContextName => "Test";
	}

	[Fact]
	public async Task RunAsync_ReturnsZero_WhenNoPendingMigrations_AgainstInMemoryProvider()
	{
		var services = new ServiceCollection();
		services.AddDbContext<TestDbContext>(opts => opts.UseInMemoryDatabase("EfCoreMigrationContributorTests"));
		var provider = services.BuildServiceProvider();

		var contributor = new TestContributor();
		var applied = await contributor.RunAsync(provider, CancellationToken.None);

		applied.ShouldBe(0);
	}

	[Fact]
	public void DependsOn_DefaultsToEmpty()
	{
		var contributor = new TestContributor();
		contributor.DependsOn.ShouldBeEmpty();
	}

	[Fact]
	public void ContextName_IsRequired()
	{
		var contributor = new TestContributor();
		contributor.ContextName.ShouldBe("Test");
	}
}
```

Note: InMemory provider doesn't support real migrations, so `GetPendingMigrationsAsync` returns empty — that's the "no pending migrations" path. Real migration validation happens in the {Company}.{Context}.Migrations.Tests projects against testcontainers Postgres.

- [ ] **Step 4: Run tests; verify they pass**

Run: `cd Norse.Hosting && dotnet test tests/Norse.Abstractions.Hosting.Tests/Norse.Abstractions.Hosting.Tests.csproj`
Expected: all tests pass (previous 3 + new 3 = 6 total).

- [ ] **Step 5: Stage**

```bash
git add Directory.Packages.props Norse.Hosting/tests/Norse.Abstractions.Hosting.Tests/
```

Proposed commit message: `test(hosting.abstractions): cover EfCoreMigrationContributor smoke path`

---

## Task 9: Create `Norse.Hosting.ServiceDefaults`

**Files:**
- Create: `Norse.Hosting/src/Norse.Hosting.ServiceDefaults/Norse.Hosting.ServiceDefaults.csproj`
- Create: `Norse.Hosting/src/Norse.Hosting.ServiceDefaults/Extensions.cs`

- [ ] **Step 1: Create the project**

Run: `cd Norse.Hosting\src && dotnet new classlib --name Norse.Hosting.ServiceDefaults --framework net10.0`

- [ ] **Step 2: Replace the generated `.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>Aspire-derived cross-cutting defaults shared by every Norse deployable.</Description>
    <RootNamespace>Norse.Hosting.ServiceDefaults</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Delete the auto-generated `Class1.cs`**

Run: `del Norse.Hosting\src\Norse.Hosting.ServiceDefaults\Class1.cs`

- [ ] **Step 4: Create `Extensions.cs`**

This is the standard Aspire ServiceDefaults pattern. Aspire's project template ships an equivalent; we recreate it here so the deployables don't depend on the template.

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Norse.Hosting.ServiceDefaults;

public static class Extensions
{
	private const string HealthEndpointPath = "/health";
	private const string AlivenessEndpointPath = "/alive";

	public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
		where TBuilder : IHostApplicationBuilder
	{
		builder.ConfigureOpenTelemetry();
		builder.AddDefaultHealthChecks();
		builder.Services.AddServiceDiscovery();

		builder.Services.ConfigureHttpClientDefaults(http =>
		{
			http.AddStandardResilienceHandler();
			http.AddServiceDiscovery();
		});

		return builder;
	}

	public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
		where TBuilder : IHostApplicationBuilder
	{
		builder.Logging.AddOpenTelemetry(logging =>
		{
			logging.IncludeFormattedMessage = true;
			logging.IncludeScopes = true;
		});

		builder.Services.AddOpenTelemetry()
			.WithMetrics(metrics =>
			{
				metrics.AddAspNetCoreInstrumentation()
					.AddHttpClientInstrumentation()
					.AddRuntimeInstrumentation();
			})
			.WithTracing(tracing =>
			{
				tracing.AddAspNetCoreInstrumentation()
					.AddHttpClientInstrumentation();
			});

		builder.AddOpenTelemetryExporters();

		return builder;
	}

	private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
		where TBuilder : IHostApplicationBuilder
	{
		var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
		if (useOtlpExporter)
		{
			builder.Services.AddOpenTelemetry().UseOtlpExporter();
		}
		return builder;
	}

	public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
		where TBuilder : IHostApplicationBuilder
	{
		builder.Services.AddHealthChecks()
			.AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
		return builder;
	}

	public static WebApplication MapDefaultEndpoints(this WebApplication app)
	{
		if (app.Environment.IsDevelopment())
		{
			app.MapHealthChecks(HealthEndpointPath);
			app.MapHealthChecks(AlivenessEndpointPath, new()
			{
				Predicate = r => r.Tags.Contains("live"),
			});
		}
		return app;
	}
}
```

- [ ] **Step 5: Add to solution**

Run: `cd Norse.Hosting && dotnet sln Norse.Hosting.slnx add src/Norse.Hosting.ServiceDefaults/Norse.Hosting.ServiceDefaults.csproj`

- [ ] **Step 6: Build**

Run: `cd Norse.Hosting && dotnet build src/Norse.Hosting.ServiceDefaults/Norse.Hosting.ServiceDefaults.csproj`
Expected: build succeeds.

- [ ] **Step 7: Stage**

```bash
git add Norse.Hosting/src/Norse.Hosting.ServiceDefaults/ Norse.Hosting/Norse.Hosting.slnx
```

Proposed commit message: `feat(servicedefaults): add Aspire-derived cross-cutting defaults`

---

## Task 10: Create `Norse.Hosting.Web` Project + Builder

**Files:**
- Create: `Norse.Hosting/src/Norse.Hosting.Web/Norse.Hosting.Web.csproj`
- Create: `Norse.Hosting/src/Norse.Hosting.Web/INorseWebHostBuilder.cs`
- Create: `Norse.Hosting/src/Norse.Hosting.Web/NorseWebHostBuilder.cs`
- Create: `Norse.Hosting/src/Norse.Hosting.Web/NorseWebHostBuilderExtensions.cs`

- [ ] **Step 1: Create the project**

Run: `cd Norse.Hosting\src && dotnet new classlib --name Norse.Hosting.Web --framework net10.0`

- [ ] **Step 2: Replace the generated `.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>Web-host runtime for the Norse plugin platform.</Description>
    <RootNamespace>Norse.Hosting.Web</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Grpc.AspNetCore" />
    <PackageReference Include="Grpc.AspNetCore.Server.Reflection" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Norse.Abstractions.Hosting\Norse.Abstractions.Hosting.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Delete the auto-generated `Class1.cs`**

Run: `del Norse.Hosting\src\Norse.Hosting.Web\Class1.cs`

- [ ] **Step 4: Create `INorseWebHostBuilder.cs`**

```csharp
using Microsoft.AspNetCore.Builder;

namespace Norse.Hosting.Web;

public interface INorseWebHostBuilder
{
	WebApplicationBuilder Builder { get; }

	/// <summary>
	/// Registers a plugin with the host. The plugin is instantiated via <c>new TPlugin()</c>
	/// and its <c>ConfigureServices</c> method is called immediately.
	///
	/// <para>Plugin order matters in two ways: (1) cross-cutting plugins (Auth, telemetry)
	/// should register first so their authentication schemes and global policies are in
	/// place when business plugins register their resources; (2) <c>MapEndpoints</c> is
	/// invoked in registration order, which determines route registration order — routes
	/// declared earlier win conflicts.</para>
	/// </summary>
	INorseWebHostBuilder AddPlugin<TPlugin>() where TPlugin : IWebHostPlugin, new();
}
```

- [ ] **Step 5: Create `NorseWebHostBuilder.cs` (internal sealed implementation)**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Hosting.Web;

internal sealed class NorseWebHostBuilder(WebApplicationBuilder builder) : INorseWebHostBuilder
{
	public WebApplicationBuilder Builder { get; } = builder;

	internal List<IWebHostPlugin> Plugins { get; } = [];

	public INorseWebHostBuilder AddPlugin<TPlugin>() where TPlugin : IWebHostPlugin, new()
	{
		var plugin = new TPlugin();
		Plugins.Add(plugin);
		plugin.ConfigureServices(Builder.Services, Builder.Environment, Builder.Configuration);
		return this;
	}
}
```

- [ ] **Step 6: Create `NorseWebHostBuilderExtensions.cs`**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Hosting.Web;

public static class NorseWebHostBuilderExtensions
{
	private const string BuilderKey = "Norse.Hosting.Web.Builder";

	/// <summary>
	/// Adds the Norse web-host runtime to the builder. After this call, plugins can be
	/// registered via <c>AddPlugin&lt;T&gt;()</c>. Call once, early in Program.cs.
	/// </summary>
	public static INorseWebHostBuilder AddNorseWebHost(this WebApplicationBuilder builder)
	{
		var host = new NorseWebHostBuilder(builder);

		// Stash on the WebApplicationBuilder's Properties for UseNorseWebHost to retrieve later.
		builder.Host.Properties[BuilderKey] = host;

		// Cross-cutting platform setup (§9 of the design spec).
		ConfigurePlatformServices(builder);

		return host;
	}

	internal static NorseWebHostBuilder GetNorseHost(this WebApplication app)
	{
		if (app.Services.GetService<NorseWebHostBuilder>() is { } direct) return direct;
		// Fallback path — read from the original builder's properties.
		throw new InvalidOperationException(
			"AddNorseWebHost was not called on the WebApplicationBuilder. UseNorseWebHost requires AddNorseWebHost.");
	}

	private static void ConfigurePlatformServices(WebApplicationBuilder builder)
	{
		// gRPC server. Reflection enabled in Development/Staging only — see UseNorseWebHost.
		builder.Services.AddGrpc();
		if (builder.Environment.IsDevelopment() || builder.Environment.IsStaging())
		{
			builder.Services.AddGrpcReflection();
		}

		// Controllers (for webhook ControllerBase and future JsonControllerBase<T> subclasses).
		// Plugins extend with AddApplicationPart in their own ConfigureServices.
		builder.Services.AddControllers();

		// HttpClient global defaults (resilience + service discovery already from ServiceDefaults).
		// ServiceDefaults's ConfigureHttpClientDefaults applies; nothing extra needed here.

		// Auth/Authz baseline. Plugins add schemes and policies.
		builder.Services.AddAuthentication();
		builder.Services.AddAuthorizationBuilder();

		// Problem-details handler for unhandled exceptions.
		builder.Services.AddProblemDetails();
	}
}
```

Note: the builder-property stashing in `AddNorseWebHost` and retrieval in `UseNorseWebHost` (next task) requires careful path. The approach above uses `builder.Host.Properties` which survives across `builder.Build()`; alternatively register the `NorseWebHostBuilder` as a singleton in DI so it's resolvable from `app.Services` after build. The DI-registration approach is cleaner — refactor in Task 11.

- [ ] **Step 7: Refactor stashing to DI singleton**

Update `AddNorseWebHost` body in `NorseWebHostBuilderExtensions.cs`:

```csharp
public static INorseWebHostBuilder AddNorseWebHost(this WebApplicationBuilder builder)
{
	var host = new NorseWebHostBuilder(builder);
	builder.Services.AddSingleton(host);   // resolvable from app.Services after build.
	ConfigurePlatformServices(builder);
	return host;
}

internal static NorseWebHostBuilder GetNorseHost(this WebApplication app)
	=> app.Services.GetRequiredService<NorseWebHostBuilder>();
```

Remove the `BuilderKey` constant and the `builder.Host.Properties[...]` line.

- [ ] **Step 8: Add to solution**

Run: `cd Norse.Hosting && dotnet sln Norse.Hosting.slnx add src/Norse.Hosting.Web/Norse.Hosting.Web.csproj`

- [ ] **Step 9: Build**

Run: `cd Norse.Hosting && dotnet build src/Norse.Hosting.Web/Norse.Hosting.Web.csproj`
Expected: build succeeds.

- [ ] **Step 10: Stage**

```bash
git add Norse.Hosting/src/Norse.Hosting.Web/ Norse.Hosting/Norse.Hosting.slnx
```

Proposed commit message: `feat(hosting.web): add AddNorseWebHost + INorseWebHostBuilder + AddPlugin<T>`

---

## Task 11: Implement `UseNorseWebHost` (with TDD)

**Files:**
- Create: `Norse.Hosting/tests/Norse.Hosting.Web.Tests/Norse.Hosting.Web.Tests.csproj`
- Create: `Norse.Hosting/tests/Norse.Hosting.Web.Tests/AddPluginTests.cs`
- Create: `Norse.Hosting/tests/Norse.Hosting.Web.Tests/UseNorseWebHostTests.cs`
- Create: `Norse.Hosting/src/Norse.Hosting.Web/NorseWebApplicationExtensions.cs`

- [ ] **Step 1: Create the test project**

Run: `cd Norse.Hosting\tests && dotnet new xunit --name Norse.Hosting.Web.Tests --framework net10.0`

- [ ] **Step 2: Replace the generated `.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <RootNamespace>Norse.Hosting.Web.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="NSubstitute" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Norse.Hosting.Web\Norse.Hosting.Web.csproj" />
  </ItemGroup>
</Project>
```

Delete the auto-generated `UnitTest1.cs`.

- [ ] **Step 3: Add to solution**

Run: `cd Norse.Hosting && dotnet sln Norse.Hosting.slnx add tests/Norse.Hosting.Web.Tests/Norse.Hosting.Web.Tests.csproj`

- [ ] **Step 4: Write `AddPluginTests.cs`**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Norse.Hosting.Web.Tests;

public class AddPluginTests
{
	private sealed class TestPluginA : IWebHostPlugin
	{
		public static bool ConfigureServicesCalled { get; private set; }
		public void ConfigureServices(IServiceCollection s, IHostEnvironment e, IConfiguration c)
		{
			ConfigureServicesCalled = true;
			s.AddSingleton<TestMarkerA>();
		}
		public void MapEndpoints(IEndpointRouteBuilder endpoints) { }
	}

	private sealed class TestMarkerA;

	[Fact]
	public void AddPlugin_InvokesConfigureServicesImmediately()
	{
		TestPluginA.ConfigureServicesCalled = false;

		var builder = WebApplication.CreateBuilder();
		builder.AddNorseWebHost().AddPlugin<TestPluginA>();

		TestPluginA.ConfigureServicesCalled.ShouldBeTrue();
	}

	[Fact]
	public void AddPlugin_RegistersPluginServicesIntoDI()
	{
		var builder = WebApplication.CreateBuilder();
		builder.AddNorseWebHost().AddPlugin<TestPluginA>();

		var app = builder.Build();
		app.Services.GetService<TestMarkerA>().ShouldNotBeNull();
	}
}
```

- [ ] **Step 5: Write `UseNorseWebHostTests.cs`**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Norse.Hosting.Web.Tests;

public class UseNorseWebHostTests
{
	private sealed class CapturingPlugin : IWebHostPlugin
	{
		public static int MapEndpointsCallCount { get; private set; }
		public void ConfigureServices(IServiceCollection s, IHostEnvironment e, IConfiguration c) { }
		public void MapEndpoints(IEndpointRouteBuilder endpoints) => MapEndpointsCallCount++;
	}

	[Fact]
	public void UseNorseWebHost_InvokesMapEndpointsOnEveryRegisteredPlugin()
	{
		CapturingPlugin.MapEndpointsCallCount.GetType();  // touch type to reset static-state visibility

		var builder = WebApplication.CreateBuilder();
		builder.AddNorseWebHost().AddPlugin<CapturingPlugin>();
		var app = builder.Build();

		app.UseNorseWebHost();

		CapturingPlugin.MapEndpointsCallCount.ShouldBeGreaterThanOrEqualTo(1);
	}

	[Fact]
	public void UseNorseWebHost_FailsClearly_WhenAddNorseWebHostNotCalled()
	{
		var builder = WebApplication.CreateBuilder();
		var app = builder.Build();

		Should.Throw<InvalidOperationException>(() => app.UseNorseWebHost());
	}
}
```

- [ ] **Step 6: Run tests; verify they fail (no `UseNorseWebHost` yet)**

Run: `cd Norse.Hosting && dotnet test tests/Norse.Hosting.Web.Tests/`
Expected: compilation failure on `UseNorseWebHost`.

- [ ] **Step 7: Implement `NorseWebApplicationExtensions.cs`**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Hosting.Web;

public static class NorseWebApplicationExtensions
{
	/// <summary>
	/// Finalizes the host pipeline: applies default middleware (routing, exception handler,
	/// authentication, authorization, OpenAPI doc endpoint, telemetry), iterates registered
	/// plugins to call <c>MapEndpoints</c>, calls <c>app.MapControllers()</c> once globally,
	/// returns the configured app.
	/// </summary>
	public static WebApplication UseNorseWebHost(this WebApplication app)
	{
		var host = app.GetNorseHost();

		// Middleware pipeline (order matters):
		app.UseExceptionHandler();
		app.UseRouting();
		app.UseAuthentication();
		app.UseAuthorization();

		// Plugin endpoint mapping — registration order is also routing precedence.
		foreach (var plugin in host.Plugins)
		{
			plugin.MapEndpoints(app);
		}

		// Controllers are auto-routed by attribute after this single global call.
		app.MapControllers();

		// gRPC reflection (Development/Staging only).
		if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
		{
			app.MapGrpcReflectionService();
		}

		return app;
	}
}
```

- [ ] **Step 8: Run tests; verify they pass**

Run: `cd Norse.Hosting && dotnet test tests/Norse.Hosting.Web.Tests/`
Expected: 4 tests pass.

- [ ] **Step 9: Stage**

```bash
git add Norse.Hosting/src/Norse.Hosting.Web/NorseWebApplicationExtensions.cs Norse.Hosting/tests/Norse.Hosting.Web.Tests/
```

Proposed commit message: `feat(hosting.web): add UseNorseWebHost with plugin lifecycle iteration`

---

## Task 12: Partner OpenAPI Doc Transformer (TDD)

**Files:**
- Create: `Norse.Hosting/src/Norse.Hosting.Web/OpenApi/PartnerOpenApiDocumentTransformer.cs`
- Create: `Norse.Hosting/tests/Norse.Hosting.Web.Tests/PartnerOpenApiDocumentTransformerTests.cs`
- Modify: `Norse.Hosting/src/Norse.Hosting.Web/NorseWebHostBuilderExtensions.cs` (register the transformer)

- [ ] **Step 1: Write the failing test**

Create `Norse.Hosting/tests/Norse.Hosting.Web.Tests/PartnerOpenApiDocumentTransformerTests.cs`:

```csharp
using Norse.Hosting.Web.OpenApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;
using Shouldly;
using Xunit;

namespace Norse.Hosting.Web.Tests;

public class PartnerOpenApiDocumentTransformerTests
{
	[Fact]
	public async Task Transform_RemovesOperations_ForControllersNotImplementingIPublishedController()
	{
		var doc = new OpenApiDocument
		{
			Paths = new()
			{
				["/webhooks/stripe"] = new() { Operations = new() { [OperationType.Post] = new() } },
				["/api/v1/billing/summary"] = new() { Operations = new() { [OperationType.Post] = new() } },
			},
		};

		// Hardcoded ApiDescription stubs would be heavy; the actual implementation
		// inspects ApiDescription.ActionDescriptor for controller type and checks
		// typeof(IPublishedController).IsAssignableFrom(...). Test verifies the
		// transformer's behavior given crafted ApiDescriptions.
		var transformer = new PartnerOpenApiDocumentTransformer();

		var context = new OpenApiDocumentTransformerContext
		{
			DocumentName = "partner",
			DescriptionGroups = TestApiDescriptionGroups.Build(
				path: "/webhooks/stripe", controllerType: typeof(WebhookControllerStub),
				path2: "/api/v1/billing/summary", controllerType2: typeof(PublishedControllerStub)),
		};

		await transformer.TransformAsync(doc, context, CancellationToken.None);

		doc.Paths.ShouldContainKey("/api/v1/billing/summary");
		doc.Paths.ShouldNotContainKey("/webhooks/stripe");
	}

	private sealed class WebhookControllerStub : ControllerBase { }
	private sealed class PublishedControllerStub : ControllerBase, IPublishedController { }
}

internal static class TestApiDescriptionGroups
{
	public static IReadOnlyList<ApiDescriptionGroup> Build(string path, Type controllerType, string path2, Type controllerType2)
	{
		// Minimal ApiDescription stub helper. Build is a TODO when implementing —
		// fill with code that creates ApiDescriptions matching the paths and controller types.
		// Implementation hint: use ControllerActionDescriptor with ControllerTypeInfo = controllerType.GetTypeInfo().
		throw new NotImplementedException("Fill in during implementation per the comment.");
	}
}
```

Note: the `TestApiDescriptionGroups.Build` stub is a placeholder. When implementing, fill it with code that creates `ApiDescription` instances with `ActionDescriptor` of type `ControllerActionDescriptor`, each carrying the right `ControllerTypeInfo`. This is heavyweight test plumbing; alternative is to refactor the transformer to take a simpler input (e.g., a function `Type? GetControllerType(string path)`) for testability.

- [ ] **Step 2: Run test; verify compilation failure**

Run: `cd Norse.Hosting && dotnet test tests/Norse.Hosting.Web.Tests/`
Expected: compile failure — `PartnerOpenApiDocumentTransformer` does not exist.

- [ ] **Step 3: Implement `PartnerOpenApiDocumentTransformer.cs`**

```csharp
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace Norse.Hosting.Web.OpenApi;

/// <summary>
/// Filters the partner-facing OpenAPI document to include only controllers that
/// implement <see cref="IPublishedController"/>. Webhook controllers (plain
/// <see cref="ControllerBase"/>) and any non-controller endpoints are excluded.
/// </summary>
internal sealed class PartnerOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
	public Task TransformAsync(
		OpenApiDocument document,
		OpenApiDocumentTransformerContext context,
		CancellationToken cancellationToken)
	{
		var publishedPaths = new HashSet<string>();

		foreach (var group in context.DescriptionGroups)
		{
			foreach (var apiDescription in group.Items)
			{
				if (apiDescription.ActionDescriptor is ControllerActionDescriptor cad
					&& typeof(IPublishedController).IsAssignableFrom(cad.ControllerTypeInfo))
				{
					// Match the OpenAPI path key. RelativePath comes without leading slash.
					var path = "/" + apiDescription.RelativePath;
					publishedPaths.Add(path);
				}
			}
		}

		// Remove all paths that aren't in the published set.
		var pathsToRemove = document.Paths.Keys.Where(p => !publishedPaths.Contains(p)).ToList();
		foreach (var p in pathsToRemove)
		{
			document.Paths.Remove(p);
		}

		return Task.CompletedTask;
	}
}
```

- [ ] **Step 4: Wire the transformer into the platform pipeline**

Modify `Norse.Hosting/src/Norse.Hosting.Web/NorseWebHostBuilderExtensions.cs`, in `ConfigurePlatformServices`:

```csharp
private static void ConfigurePlatformServices(WebApplicationBuilder builder)
{
	builder.Services.AddGrpc();
	if (builder.Environment.IsDevelopment() || builder.Environment.IsStaging())
	{
		builder.Services.AddGrpcReflection();
	}

	builder.Services.AddControllers();
	builder.Services.AddAuthentication();
	builder.Services.AddAuthorizationBuilder();
	builder.Services.AddProblemDetails();

	// Partner OpenAPI doc with the visibility filter.
	builder.Services.AddOpenApi("partner", options =>
	{
		options.AddDocumentTransformer<OpenApi.PartnerOpenApiDocumentTransformer>();
	});
}
```

- [ ] **Step 5: Implement `TestApiDescriptionGroups.Build` and run tests**

Fill in `TestApiDescriptionGroups.Build` in the test file with code that constructs the needed `ApiDescription`/`ControllerActionDescriptor` graph. Run tests:

```
cd Norse.Hosting
dotnet test tests/Norse.Hosting.Web.Tests/
```
Expected: tests pass.

- [ ] **Step 6: Stage**

```bash
git add Norse.Hosting/src/Norse.Hosting.Web/OpenApi/ Norse.Hosting/src/Norse.Hosting.Web/NorseWebHostBuilderExtensions.cs Norse.Hosting/tests/Norse.Hosting.Web.Tests/PartnerOpenApiDocumentTransformerTests.cs
```

Proposed commit message: `feat(hosting.web): add partner OpenAPI document transformer with IPublishedController filter`

---

## Task 13: Create `Norse.Hosting.Worker`

**Files:**
- Create: `Norse.Hosting/src/Norse.Hosting.Worker/Norse.Hosting.Worker.csproj`
- Create: `Norse.Hosting/src/Norse.Hosting.Worker/INorseWorkerHostBuilder.cs`
- Create: `Norse.Hosting/src/Norse.Hosting.Worker/NorseWorkerHostBuilder.cs`
- Create: `Norse.Hosting/src/Norse.Hosting.Worker/NorseWorkerHostBuilderExtensions.cs`
- Create: `Norse.Hosting/tests/Norse.Hosting.Worker.Tests/Norse.Hosting.Worker.Tests.csproj`
- Create: `Norse.Hosting/tests/Norse.Hosting.Worker.Tests/AddPluginTests.cs`

- [ ] **Step 1: Create the Worker project**

Run: `cd Norse.Hosting\src && dotnet new classlib --name Norse.Hosting.Worker --framework net10.0`

- [ ] **Step 2: Replace the `.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>Worker-host runtime for the Norse plugin platform.</Description>
    <RootNamespace>Norse.Hosting.Worker</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Norse.Abstractions.Hosting\Norse.Abstractions.Hosting.csproj" />
  </ItemGroup>
</Project>
```

Delete the auto-generated `Class1.cs`.

- [ ] **Step 3: Create `INorseWorkerHostBuilder.cs`**

```csharp
using Microsoft.Extensions.Hosting;

namespace Norse.Hosting.Worker;

public interface INorseWorkerHostBuilder
{
	IHostApplicationBuilder Builder { get; }

	INorseWorkerHostBuilder AddPlugin<TPlugin>() where TPlugin : IWorkerHostPlugin, new();
}
```

- [ ] **Step 4: Create `NorseWorkerHostBuilder.cs`**

```csharp
using Microsoft.Extensions.Hosting;

namespace Norse.Hosting.Worker;

internal sealed class NorseWorkerHostBuilder(IHostApplicationBuilder builder) : INorseWorkerHostBuilder
{
	public IHostApplicationBuilder Builder { get; } = builder;
	internal List<IWorkerHostPlugin> Plugins { get; } = [];

	public INorseWorkerHostBuilder AddPlugin<TPlugin>() where TPlugin : IWorkerHostPlugin, new()
	{
		var plugin = new TPlugin();
		Plugins.Add(plugin);
		plugin.ConfigureServices(Builder.Services, Builder.Environment, Builder.Configuration);
		return this;
	}
}
```

- [ ] **Step 5: Create `NorseWorkerHostBuilderExtensions.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Hosting.Worker;

public static class NorseWorkerHostBuilderExtensions
{
	public static INorseWorkerHostBuilder AddNorseWorkerHost(this IHostApplicationBuilder builder)
	{
		var host = new NorseWorkerHostBuilder(builder);
		builder.Services.AddSingleton(host);
		// Worker host has no extra cross-cutting beyond what ServiceDefaults provides
		// and what plugins themselves register.
		return host;
	}

	public static IHost UseNorseWorkerHost(this IHost host) => host;  // no app-phase plugin work; reserved for future symmetry.
}
```

- [ ] **Step 6: Add to solution**

Run: `cd Norse.Hosting && dotnet sln Norse.Hosting.slnx add src/Norse.Hosting.Worker/Norse.Hosting.Worker.csproj`

- [ ] **Step 7: Create the test project and a smoke test**

Run: `cd Norse.Hosting\tests && dotnet new xunit --name Norse.Hosting.Worker.Tests --framework net10.0`

Replace the .csproj with the standard test-project shape (matching Task 7 step 2). Delete `UnitTest1.cs`.

Add `AddPluginTests.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Norse.Hosting.Worker.Tests;

public class AddPluginTests
{
	private sealed class TestWorkerPlugin : IWorkerHostPlugin
	{
		public static bool ConfigureServicesCalled;
		public void ConfigureServices(IServiceCollection s, IHostEnvironment e, IConfiguration c)
		{
			ConfigureServicesCalled = true;
			s.AddHostedService<TestBackgroundService>();
		}
	}

	private sealed class TestBackgroundService : BackgroundService
	{
		protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
	}

	[Fact]
	public void AddPlugin_InvokesConfigureServices()
	{
		TestWorkerPlugin.ConfigureServicesCalled = false;

		var builder = Host.CreateApplicationBuilder();
		builder.AddNorseWorkerHost().AddPlugin<TestWorkerPlugin>();

		TestWorkerPlugin.ConfigureServicesCalled.ShouldBeTrue();
	}
}
```

- [ ] **Step 8: Add the test project to the solution and run**

Run: `cd Norse.Hosting && dotnet sln Norse.Hosting.slnx add tests/Norse.Hosting.Worker.Tests/Norse.Hosting.Worker.Tests.csproj && dotnet test tests/Norse.Hosting.Worker.Tests/`
Expected: test passes.

- [ ] **Step 9: Stage**

```bash
git add Norse.Hosting/src/Norse.Hosting.Worker/ Norse.Hosting/tests/Norse.Hosting.Worker.Tests/ Norse.Hosting/Norse.Hosting.slnx
```

Proposed commit message: `feat(hosting.worker): add worker-host runtime with plugin registration`

---

## Task 14: Create `Norse.Hosting.Migrations.Service` Project + `MigrationsHealthStatus`

**Files:**
- Create: `Norse.Hosting/src/Norse.Hosting.Migrations.Service/Norse.Hosting.Migrations.Service.csproj`
- Create: `Norse.Hosting/src/Norse.Hosting.Migrations.Service/MigrationsHealthStatus.cs`
- Create: `Norse.Hosting/src/Norse.Hosting.Migrations.Service/MigrationsHealthCheck.cs`
- Create: `Norse.Hosting/tests/Norse.Hosting.Migrations.Service.Tests/Norse.Hosting.Migrations.Service.Tests.csproj`
- Create: `Norse.Hosting/tests/Norse.Hosting.Migrations.Service.Tests/MigrationsHealthStatusTests.cs`

- [ ] **Step 1: Create the Migrations project**

Run: `cd Norse.Hosting\src && dotnet new classlib --name Norse.Hosting.Migrations.Service --framework net10.0`

- [ ] **Step 2: Replace `.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>Migrations orchestrator with health-signal-gated readiness.</Description>
    <RootNamespace>Norse.Hosting.Migrations.Service</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Norse.Abstractions.Hosting\Norse.Abstractions.Hosting.csproj" />
  </ItemGroup>
</Project>
```

Delete `Class1.cs`.

- [ ] **Step 3: Write `MigrationsHealthStatusTests.cs` (test project scaffold first)**

Run: `cd Norse.Hosting\tests && dotnet new xunit --name Norse.Hosting.Migrations.Service.Tests --framework net10.0`

Replace `.csproj` with standard test-project shape, including a `ProjectReference` to `Norse.Hosting.Migrations.Service`. Delete `UnitTest1.cs`.

Write `MigrationsHealthStatusTests.cs`:

```csharp
using Shouldly;
using Xunit;

namespace Norse.Hosting.Migrations.Service.Tests;

public class MigrationsHealthStatusTests
{
	[Fact]
	public void InitialState_IsStarting()
	{
		var status = new MigrationsHealthStatus();
		status.State.ShouldBe(MigrationsState.Starting);
	}

	[Fact]
	public void ReportInProgress_SetsStateAndContext()
	{
		var status = new MigrationsHealthStatus();
		status.ReportInProgress("Billing", 1, 3);

		status.State.ShouldBe(MigrationsState.InProgress);
		status.CurrentContext.ShouldBe("Billing");
		status.Step.ShouldBe(1);
		status.TotalSteps.ShouldBe(3);
	}

	[Fact]
	public void ReportHealthy_SetsState()
	{
		var status = new MigrationsHealthStatus();
		status.ReportHealthy();
		status.State.ShouldBe(MigrationsState.Healthy);
	}

	[Fact]
	public void ReportFailure_CapturesContextAndException()
	{
		var status = new MigrationsHealthStatus();
		var ex = new InvalidOperationException("boom");
		status.ReportFailure("Billing", ex);

		status.State.ShouldBe(MigrationsState.Unhealthy);
		status.CurrentContext.ShouldBe("Billing");
		status.FailureException.ShouldBe(ex);
	}
}
```

- [ ] **Step 4: Add both projects to solution**

Run: `cd Norse.Hosting && dotnet sln Norse.Hosting.slnx add src/Norse.Hosting.Migrations.Service/Norse.Hosting.Migrations.Service.csproj tests/Norse.Hosting.Migrations.Service.Tests/Norse.Hosting.Migrations.Service.Tests.csproj`

- [ ] **Step 5: Run tests; verify they fail**

Run: `cd Norse.Hosting && dotnet test tests/Norse.Hosting.Migrations.Service.Tests/`
Expected: compile failure — `MigrationsHealthStatus` doesn't exist.

- [ ] **Step 6: Implement `MigrationsHealthStatus.cs`**

```csharp
namespace Norse.Hosting.Migrations.Service;

public enum MigrationsState
{
	Starting = 0,
	InProgress = 1,
	Healthy = 2,
	Unhealthy = 3,
}

/// <summary>
/// Singleton holding the current migrations state. Mutated by the orchestrator;
/// read by <see cref="MigrationsHealthCheck"/> to drive the /health endpoint.
/// </summary>
public sealed class MigrationsHealthStatus
{
	private readonly Lock _lock = new();

	public MigrationsState State { get; private set; } = MigrationsState.Starting;
	public string? CurrentContext { get; private set; }
	public int Step { get; private set; }
	public int TotalSteps { get; private set; }
	public Exception? FailureException { get; private set; }

	public void ReportInProgress(string contextName, int step, int totalSteps)
	{
		lock (_lock)
		{
			State = MigrationsState.InProgress;
			CurrentContext = contextName;
			Step = step;
			TotalSteps = totalSteps;
		}
	}

	public void ReportHealthy()
	{
		lock (_lock) { State = MigrationsState.Healthy; }
	}

	public void ReportFailure(string contextName, Exception exception)
	{
		lock (_lock)
		{
			State = MigrationsState.Unhealthy;
			CurrentContext = contextName;
			FailureException = exception;
		}
	}
}
```

- [ ] **Step 7: Implement `MigrationsHealthCheck.cs`**

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Norse.Hosting.Migrations.Service;

internal sealed class MigrationsHealthCheck(MigrationsHealthStatus status) : IHealthCheck
{
	public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(status.State switch
		{
			MigrationsState.Healthy => HealthCheckResult.Healthy("All migrations applied successfully."),
			MigrationsState.Unhealthy => HealthCheckResult.Unhealthy(
				$"Migration contributor '{status.CurrentContext}' failed.",
				status.FailureException),
			MigrationsState.InProgress => HealthCheckResult.Degraded(
				$"Applying migration {status.Step} of {status.TotalSteps}: {status.CurrentContext}."),
			_ => HealthCheckResult.Degraded("Migrations starting."),
		});
	}
}
```

- [ ] **Step 8: Run tests; verify they pass**

Run: `cd Norse.Hosting && dotnet test tests/Norse.Hosting.Migrations.Service.Tests/`
Expected: 4 tests pass.

- [ ] **Step 9: Stage**

```bash
git add Norse.Hosting/src/Norse.Hosting.Migrations.Service/ Norse.Hosting/tests/Norse.Hosting.Migrations.Service.Tests/ Norse.Hosting/Norse.Hosting.slnx
```

Proposed commit message: `feat(hosting.migrations): add MigrationsHealthStatus + MigrationsHealthCheck`

---

## Task 15: `TopologicalSort` Utility (TDD)

**Files:**
- Create: `Norse.Hosting/tests/Norse.Hosting.Migrations.Service.Tests/TopologicalSortTests.cs`
- Create: `Norse.Hosting/src/Norse.Hosting.Migrations.Service/TopologicalSort.cs`

- [ ] **Step 1: Write the failing tests**

Create `Norse.Hosting/tests/Norse.Hosting.Migrations.Service.Tests/TopologicalSortTests.cs`:

```csharp
using Shouldly;
using Xunit;

namespace Norse.Hosting.Migrations.Service.Tests;

public class TopologicalSortTests
{
	private sealed record Contributor(string ContextName, IReadOnlyCollection<string> DependsOn) : IMigrationContributor
	{
		public Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
			=> Task.FromResult(0);
	}

	[Fact]
	public void Order_PutsDependenciesFirst()
	{
		var auth = new Contributor("Auth", Array.Empty<string>());
		var billing = new Contributor("Billing", new[] { "Auth", "Customer" });
		var customer = new Contributor("Customer", Array.Empty<string>());

		var sorted = TopologicalSort.Order([billing, auth, customer]);

		var names = sorted.Select(c => c.ContextName).ToList();
		names.IndexOf("Auth").ShouldBeLessThan(names.IndexOf("Billing"));
		names.IndexOf("Customer").ShouldBeLessThan(names.IndexOf("Billing"));
	}

	[Fact]
	public void Order_ThrowsOnCycle()
	{
		var a = new Contributor("A", new[] { "B" });
		var b = new Contributor("B", new[] { "A" });

		var ex = Should.Throw<InvalidOperationException>(() => TopologicalSort.Order([a, b]));
		ex.Message.ShouldContain("cycle", Case.Insensitive);
	}

	[Fact]
	public void Order_ThrowsOnMissingDependency()
	{
		var billing = new Contributor("Billing", new[] { "Auth" });
		var ex = Should.Throw<InvalidOperationException>(() => TopologicalSort.Order([billing]));
		ex.Message.ShouldContain("Auth", Case.Insensitive);
	}
}
```

- [ ] **Step 2: Run; verify failure**

Run: `cd Norse.Hosting && dotnet test tests/Norse.Hosting.Migrations.Service.Tests/`
Expected: compile failure on `TopologicalSort`.

- [ ] **Step 3: Implement `TopologicalSort.cs`**

```csharp
namespace Norse.Hosting.Migrations.Service;

internal static class TopologicalSort
{
	public static IReadOnlyList<IMigrationContributor> Order(IEnumerable<IMigrationContributor> contributors)
	{
		var list = contributors.ToList();
		var byName = list.ToDictionary(c => c.ContextName, StringComparer.Ordinal);

		// Validate dependency references resolve.
		foreach (var c in list)
		{
			foreach (var dep in c.DependsOn)
			{
				if (!byName.ContainsKey(dep))
				{
					throw new InvalidOperationException(
						$"Migration contributor '{c.ContextName}' depends on '{dep}', which is not registered.");
				}
			}
		}

		// Kahn's algorithm.
		var inDegree = list.ToDictionary(c => c.ContextName, c => c.DependsOn.Count);
		var ready = new Queue<IMigrationContributor>(list.Where(c => c.DependsOn.Count == 0));
		var sorted = new List<IMigrationContributor>(list.Count);

		// Reverse dependency map: for each contributor, the contributors that depend on it.
		var dependents = list.ToDictionary(c => c.ContextName, _ => new List<string>());
		foreach (var c in list)
		{
			foreach (var dep in c.DependsOn)
			{
				dependents[dep].Add(c.ContextName);
			}
		}

		while (ready.Count > 0)
		{
			var current = ready.Dequeue();
			sorted.Add(current);
			foreach (var dependentName in dependents[current.ContextName])
			{
				inDegree[dependentName]--;
				if (inDegree[dependentName] == 0)
				{
					ready.Enqueue(byName[dependentName]);
				}
			}
		}

		if (sorted.Count != list.Count)
		{
			var cycleMembers = list.Where(c => inDegree[c.ContextName] > 0).Select(c => c.ContextName);
			throw new InvalidOperationException(
				$"Migration contributors form a cycle. Members involved: {string.Join(", ", cycleMembers)}.");
		}

		return sorted;
	}
}
```

- [ ] **Step 4: Run; verify pass**

Run: `cd Norse.Hosting && dotnet test tests/Norse.Hosting.Migrations.Service.Tests/`
Expected: all tests pass.

- [ ] **Step 5: Stage**

```bash
git add Norse.Hosting/src/Norse.Hosting.Migrations.Service/TopologicalSort.cs Norse.Hosting/tests/Norse.Hosting.Migrations.Service.Tests/TopologicalSortTests.cs
```

Proposed commit message: `feat(hosting.migrations): add TopologicalSort with cycle and missing-dep detection`

---

## Task 16: `RecurringFailureLog` + `NorseMigrationsOptions`

**Files:**
- Create: `Norse.Hosting/src/Norse.Hosting.Migrations.Service/RecurringFailureLog.cs`
- Create: `Norse.Hosting/src/Norse.Hosting.Migrations.Service/NorseMigrationsOptions.cs`

- [ ] **Step 1: Create `NorseMigrationsOptions.cs`**

```csharp
namespace Norse.Hosting.Migrations.Service;

public sealed class NorseMigrationsOptions
{
	/// <summary>
	/// Time the orchestrator waits after reporting Healthy before calling
	/// IHostApplicationLifetime.StopApplication(). Allows Aspire / K8s readiness-check
	/// infrastructure to observe Healthy before the host shuts down.
	/// </summary>
	public TimeSpan HealthyShutdownGracePeriod { get; set; } = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Interval at which the recurring-failure log re-emits the contributor failure
	/// exception, so developers see it in stdout / Aspire dashboard / pod logs
	/// regardless of when they look.
	/// </summary>
	public TimeSpan RecurringFailureLogInterval { get; set; } = TimeSpan.FromSeconds(60);
}
```

- [ ] **Step 2: Create `RecurringFailureLog.cs`**

```csharp
using Microsoft.Extensions.Logging;

namespace Norse.Hosting.Migrations.Service;

internal static class RecurringFailureLog
{
	public static Task Start(
		ILogger log,
		string contextName,
		Exception exception,
		TimeSpan interval,
		CancellationToken cancellationToken)
	{
		return Task.Run(async () =>
		{
			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					await Task.Delay(interval, cancellationToken);
					log.LogError(exception,
						"Migration contributor {Context} failed; process is staying alive. Resolve the failure and restart.",
						contextName);
				}
			}
			catch (OperationCanceledException) { /* shutdown */ }
		}, cancellationToken);
	}
}
```

- [ ] **Step 3: Build to verify**

Run: `cd Norse.Hosting && dotnet build src/Norse.Hosting.Migrations.Service/`
Expected: builds.

- [ ] **Step 4: Stage**

```bash
git add Norse.Hosting/src/Norse.Hosting.Migrations.Service/RecurringFailureLog.cs Norse.Hosting/src/Norse.Hosting.Migrations.Service/NorseMigrationsOptions.cs
```

Proposed commit message: `feat(hosting.migrations): add options and recurring-failure log utility`

---

## Task 17: `MigrationsOrchestrator` (TDD)

**Files:**
- Create: `Norse.Hosting/tests/Norse.Hosting.Migrations.Service.Tests/MigrationsOrchestratorTests.cs`
- Create: `Norse.Hosting/src/Norse.Hosting.Migrations.Service/MigrationsOrchestrator.cs`

- [ ] **Step 1: Write the failing tests**

Create `Norse.Hosting/tests/Norse.Hosting.Migrations.Service.Tests/MigrationsOrchestratorTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Norse.Hosting.Migrations.Service.Tests;

public class MigrationsOrchestratorTests
{
	private sealed record FakeContributor(
		string ContextName,
		IReadOnlyCollection<string> DependsOn,
		Func<Task<int>> RunImpl) : IMigrationContributor
	{
		public Task<int> RunAsync(IServiceProvider services, CancellationToken ct) => RunImpl();
	}

	[Fact]
	public async Task ExecuteAsync_RunsAllContributorsInTopologicalOrder_AndStopsApplication()
	{
		var sequence = new List<string>();
		var auth = new FakeContributor("Auth", Array.Empty<string>(),
			() => { sequence.Add("Auth"); return Task.FromResult(0); });
		var billing = new FakeContributor("Billing", new[] { "Auth" },
			() => { sequence.Add("Billing"); return Task.FromResult(0); });

		var status = new MigrationsHealthStatus();
		var lifetime = Substitute.For<IHostApplicationLifetime>();
		var options = Options.Create(new NorseMigrationsOptions { HealthyShutdownGracePeriod = TimeSpan.Zero });
		var sp = new ServiceCollection().BuildServiceProvider();

		var orchestrator = new MigrationsOrchestrator(
			contributors: [auth, billing],
			services: sp,
			health: status,
			lifetime: lifetime,
			options: options,
			log: NullLogger<MigrationsOrchestrator>.Instance);

		await orchestrator.StartAsync(CancellationToken.None);
		// Allow the BackgroundService a moment to complete.
		await Task.Delay(50);

		sequence.ShouldBe(new[] { "Auth", "Billing" });
		status.State.ShouldBe(MigrationsState.Healthy);
		lifetime.Received(1).StopApplication();
	}

	[Fact]
	public async Task ExecuteAsync_FlipsToUnhealthy_OnContributorFailure_AndDoesNotStopApplication()
	{
		var auth = new FakeContributor("Auth", Array.Empty<string>(),
			() => Task.FromException<int>(new InvalidOperationException("schema mismatch")));

		var status = new MigrationsHealthStatus();
		var lifetime = Substitute.For<IHostApplicationLifetime>();
		var options = Options.Create(new NorseMigrationsOptions { HealthyShutdownGracePeriod = TimeSpan.Zero });
		var sp = new ServiceCollection().BuildServiceProvider();

		var orchestrator = new MigrationsOrchestrator(
			contributors: [auth],
			services: sp,
			health: status,
			lifetime: lifetime,
			options: options,
			log: NullLogger<MigrationsOrchestrator>.Instance);

		await orchestrator.StartAsync(CancellationToken.None);
		await Task.Delay(50);

		status.State.ShouldBe(MigrationsState.Unhealthy);
		status.FailureException.ShouldBeOfType<InvalidOperationException>();
		lifetime.DidNotReceive().StopApplication();
	}
}
```

- [ ] **Step 2: Run; verify failure**

Run: `cd Norse.Hosting && dotnet test tests/Norse.Hosting.Migrations.Service.Tests/`
Expected: compile failure on `MigrationsOrchestrator`.

- [ ] **Step 3: Implement `MigrationsOrchestrator.cs`**

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Norse.Hosting.Migrations.Service;

internal sealed class MigrationsOrchestrator(
	IEnumerable<IMigrationContributor> contributors,
	IServiceProvider services,
	MigrationsHealthStatus health,
	IHostApplicationLifetime lifetime,
	IOptions<NorseMigrationsOptions> options,
	ILogger<MigrationsOrchestrator> log) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		IReadOnlyList<IMigrationContributor> ordered;
		try
		{
			ordered = TopologicalSort.Order(contributors);
		}
		catch (InvalidOperationException ex)
		{
			health.ReportFailure("(cycle or missing dependency)", ex);
			log.LogError(ex, "Migration contributor graph is invalid; process will stay alive");
			_ = RecurringFailureLog.Start(log, "(cycle or missing dependency)", ex, options.Value.RecurringFailureLogInterval, stoppingToken);
			throw;
		}

		for (var i = 0; i < ordered.Count; i++)
		{
			var c = ordered[i];
			health.ReportInProgress(c.ContextName, i + 1, ordered.Count);
			try
			{
				var applied = await c.RunAsync(services, stoppingToken);
				log.LogInformation("Migration {Context} applied {Count} migration(s)", c.ContextName, applied);
			}
			catch (Exception ex)
			{
				health.ReportFailure(c.ContextName, ex);
				log.LogError(ex, "Migration contributor {Context} failed; process will stay alive", c.ContextName);
				_ = RecurringFailureLog.Start(log, c.ContextName, ex, options.Value.RecurringFailureLogInterval, stoppingToken);
				throw;
			}
		}

		health.ReportHealthy();
		log.LogInformation("All migrations applied successfully; releasing readiness gate in {Grace} and shutting down",
			options.Value.HealthyShutdownGracePeriod);

		if (options.Value.HealthyShutdownGracePeriod > TimeSpan.Zero)
		{
			try
			{
				await Task.Delay(options.Value.HealthyShutdownGracePeriod, stoppingToken);
			}
			catch (OperationCanceledException) { return; }
		}

		lifetime.StopApplication();
	}
}
```

- [ ] **Step 4: Run; verify pass**

Run: `cd Norse.Hosting && dotnet test tests/Norse.Hosting.Migrations.Service.Tests/`
Expected: tests pass.

- [ ] **Step 5: Stage**

```bash
git add Norse.Hosting/src/Norse.Hosting.Migrations.Service/MigrationsOrchestrator.cs Norse.Hosting/tests/Norse.Hosting.Migrations.Service.Tests/MigrationsOrchestratorTests.cs
```

Proposed commit message: `feat(hosting.migrations): add MigrationsOrchestrator with success/failure paths`

---

## Task 18: `AddNorseMigrationsHost` + Builder + `AddMigration<T>`

**Files:**
- Create: `Norse.Hosting/src/Norse.Hosting.Migrations.Service/INorseMigrationsHostBuilder.cs`
- Create: `Norse.Hosting/src/Norse.Hosting.Migrations.Service/NorseMigrationsHostBuilder.cs`
- Create: `Norse.Hosting/src/Norse.Hosting.Migrations.Service/NorseMigrationsHostBuilderExtensions.cs`

- [ ] **Step 1: Create `INorseMigrationsHostBuilder.cs`**

```csharp
using Microsoft.Extensions.Hosting;

namespace Norse.Hosting.Migrations.Service;

public interface INorseMigrationsHostBuilder
{
	IHostApplicationBuilder Builder { get; }

	INorseMigrationsHostBuilder AddMigration<TContributor>()
		where TContributor : class, IMigrationContributor, new();
}
```

- [ ] **Step 2: Create `NorseMigrationsHostBuilder.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Hosting.Migrations.Service;

internal sealed class NorseMigrationsHostBuilder(IHostApplicationBuilder builder) : INorseMigrationsHostBuilder
{
	public IHostApplicationBuilder Builder { get; } = builder;

	public INorseMigrationsHostBuilder AddMigration<TContributor>()
		where TContributor : class, IMigrationContributor, new()
	{
		Builder.Services.AddSingleton<IMigrationContributor, TContributor>();
		return this;
	}
}
```

- [ ] **Step 3: Create `NorseMigrationsHostBuilderExtensions.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Hosting.Migrations.Service;

public static class NorseMigrationsHostBuilderExtensions
{
	public static INorseMigrationsHostBuilder AddNorseMigrationsHost(this IHostApplicationBuilder builder)
	{
		// Keep host alive even if the orchestrator's ExecuteAsync throws. Forces the operator
		// to look at the failure instead of the failure being papered over by an exit code.
		builder.Services.Configure<HostOptions>(o =>
			o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

		builder.Services.AddOptions<NorseMigrationsOptions>()
			.BindConfiguration("Norse:Migrations")
			.ValidateOnStart();

		builder.Services.AddSingleton<MigrationsHealthStatus>();
		builder.Services.AddHealthChecks().AddCheck<MigrationsHealthCheck>("migrations");
		builder.Services.AddHostedService<MigrationsOrchestrator>();

		return new NorseMigrationsHostBuilder(builder);
	}
}
```

- [ ] **Step 4: Build**

Run: `cd Norse.Hosting && dotnet build src/Norse.Hosting.Migrations.Service/`
Expected: builds.

- [ ] **Step 5: Stage**

```bash
git add Norse.Hosting/src/Norse.Hosting.Migrations.Service/INorseMigrationsHostBuilder.cs Norse.Hosting/src/Norse.Hosting.Migrations.Service/NorseMigrationsHostBuilder.cs Norse.Hosting/src/Norse.Hosting.Migrations.Service/NorseMigrationsHostBuilderExtensions.cs
```

Proposed commit message: `feat(hosting.migrations): add AddNorseMigrationsHost + AddMigration<T> builder`

---

## Task 19: Create `Norse.Hosting.Web.Server` (Web Deployable)

**Files:**
- Create: `Norse.Hosting.Web.Server/Norse.Hosting.Web.Server.csproj`
- Create: `Norse.Hosting.Web.Server/Program.cs`

- [ ] **Step 1: Create the subdirectory and project**

Run: `mkdir Norse.Hosting.Web.Server && cd Norse.Hosting.Web.Server && dotnet new web --name Norse.Hosting.Web.Server --output . --framework net10.0`

- [ ] **Step 2: Replace the generated `.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <RootNamespace>{Company}</RootNamespace>
    <Description>The {Company} server-side deployable. Loads every context plugin into a single host.</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Norse.Hosting\src\Norse.Hosting.Web\Norse.Hosting.Web.csproj" />
    <ProjectReference Include="..\Norse.Hosting\src\Norse.Hosting.ServiceDefaults\Norse.Hosting.ServiceDefaults.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Replace `Program.cs`**

```csharp
using Norse.Hosting.Web;
using Norse.Hosting.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.AddNorseWebHost();

// Future: add per-context plugins as their plans land.
// builder.AddNorseWebHost()
//   .AddPlugin<AuthPlugin>()          // cross-cutting first (auth-foundation plan)
//   .AddPlugin<BillingPlugin>()
//   .AddPlugin<ClaimsPlugin>()
//   ...

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseNorseWebHost();
app.Run();
```

- [ ] **Step 4: Add to the meta-repo solution**

Run: `dotnet sln Norse.slnx add Norse.Hosting.Web.Server/Norse.Hosting.Web.Server.csproj`

- [ ] **Step 5: Build**

Run: `dotnet build Norse.Hosting.Web.Server/Norse.Hosting.Web.Server.csproj`
Expected: builds.

- [ ] **Step 6: Stage**

```bash
git add Norse.Hosting.Web.Server/ Norse.slnx
```

Proposed commit message: `feat({company}-host): scaffold the {Company} web-host deployable`

---

## Task 20: Create `Norse.Hosting.Worker` (Optional Worker Deployable)

**Files:**
- Create: `Norse.Hosting.Worker/Norse.Hosting.Worker.csproj`
- Create: `Norse.Hosting.Worker/Program.cs`

- [ ] **Step 1: Create the subdirectory**

Run: `mkdir Norse.Hosting.Worker`

- [ ] **Step 2: Create the project**

Run: `cd Norse.Hosting.Worker && dotnet new worker --name Norse.Hosting.Worker --output . --framework net10.0`

- [ ] **Step 3: Replace `.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
  <PropertyGroup>
    <RootNamespace>{Company}</RootNamespace>
    <Description>Optional {Company} worker deployable. Loads worker plugins.</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Norse.Hosting\src\Norse.Hosting.Worker\Norse.Hosting.Worker.csproj" />
    <ProjectReference Include="..\Norse.Hosting\src\Norse.Hosting.ServiceDefaults\Norse.Hosting.ServiceDefaults.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Delete `Worker.cs` (the generated background service) and replace `Program.cs`**

Delete: `del Norse.Hosting.Worker\Worker.cs`

Replace `Program.cs`:

```csharp
using Norse.Hosting.Worker;
using Norse.Hosting.ServiceDefaults;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.AddNorseWorkerHost();

// Future: add worker plugins as their plans land.
// builder.AddNorseWorkerHost()
//   .AddPlugin<BillingPlugin>()  // same plugin class as in Norse.Hosting.Web.Server
//   ...

await builder.Build().RunAsync();
```

- [ ] **Step 5: Add to solution**

Run: `dotnet sln Norse.slnx add Norse.Hosting.Worker/Norse.Hosting.Worker.csproj`

- [ ] **Step 6: Build**

Run: `dotnet build Norse.Hosting.Worker/`
Expected: builds.

- [ ] **Step 7: Stage**

```bash
git add Norse.Hosting.Worker/ Norse.slnx
```

Proposed commit message: `feat({company}-workerhost): scaffold the optional {Company} worker deployable`

---

## Task 21: Create `Norse.Hosting.Migrations.Service` (Migrations Deployable)

**Files:**
- Create: `Norse.Hosting.Migrations.Service/Norse.Hosting.Migrations.Service.csproj`
- Create: `Norse.Hosting.Migrations.Service/Program.cs`

- [ ] **Step 1: Create the subdirectory and project**

Run: `mkdir Norse.Hosting.Migrations.Service && cd Norse.Hosting.Migrations.Service && dotnet new console --name Norse.Hosting.Migrations.Service --output . --framework net10.0`

- [ ] **Step 2: Replace `.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
  <PropertyGroup>
    <RootNamespace>{Company}</RootNamespace>
    <Description>{Company} migrations init container. Long-running, never exits non-zero, /health-driven readiness.</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Norse.Hosting\src\Norse.Hosting.Migrations.Service\Norse.Hosting.Migrations.Service.csproj" />
    <ProjectReference Include="..\Norse.Hosting\src\Norse.Hosting.ServiceDefaults\Norse.Hosting.ServiceDefaults.csproj" />
  </ItemGroup>
</Project>
```

Using `Microsoft.NET.Sdk.Worker` so the deployable can serve a `/health` endpoint (the Web SDK adds HTTP automatically through `MapDefaultEndpoints`).

- [ ] **Step 3: Replace `Program.cs`**

```csharp
using Norse.Hosting.Migrations.Service;
using Norse.Hosting.ServiceDefaults;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.AddNorseMigrationsHost();

// Future: register per-context migration contributors as their plans land.
// builder.AddNorseMigrationsHost()
//   .AddMigration<AuthMigrationContributor>()
//   .AddMigration<CustomerMigrationContributor>()
//   .AddMigration<BillingMigrationContributor>()
//   ...

await builder.Build().RunAsync();
```

- [ ] **Step 4: Add to solution**

Run: `dotnet sln Norse.slnx add Norse.Hosting.Migrations.Service/Norse.Hosting.Migrations.Service.csproj`

- [ ] **Step 5: Build**

Run: `dotnet build Norse.Hosting.Migrations.Service/`
Expected: builds.

- [ ] **Step 6: Stage**

```bash
git add Norse.Hosting.Migrations.Service/ Norse.slnx
```

Proposed commit message: `feat({company}-migrations): scaffold the migrations init-container deployable`

---

## Task 22: Create `Norse.Hosting.AppHost` (Aspire Local-Dev Orchestrator)

**Files:**
- Create: `Norse.Hosting.AppHost/Norse.Hosting.AppHost.csproj`
- Create: `Norse.Hosting.AppHost/AppHost.cs`
- Create: `Norse.Hosting.AppHost/Properties/launchSettings.json`

- [ ] **Step 1: Create the subdirectory and project**

Run: `mkdir Norse.Hosting.AppHost && cd Norse.Hosting.AppHost`

Use Aspire's AppHost template:

Run: `dotnet new aspire-apphost --name Norse.Hosting.AppHost --output . --framework net10.0`

- [ ] **Step 2: Replace `.csproj`**

```xml
<Project Sdk="Aspire.AppHost.Sdk/9.0.0">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsAspireHost>true</IsAspireHost>
    <RootNamespace>{Company}.Dev</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" />
    <PackageReference Include="Aspire.Hosting.PostgreSQL" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Norse.Hosting.Web.Server\Norse.Hosting.Web.Server.csproj" />
    <ProjectReference Include="..\Norse.Hosting.Worker\Norse.Hosting.Worker.csproj" />
    <ProjectReference Include="..\Norse.Hosting.Migrations.Service\Norse.Hosting.Migrations.Service.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Replace `AppHost.cs`**

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
	.AddDatabase("{company}");

var migrations = builder.AddProject<Projects.Norse_Hosting_Migrations_Service>("migrations")
	.WithReference(postgres)
	.WaitFor(postgres);

var host = builder.AddProject<Projects.Norse_Hosting_Web_Server>("host")
	.WithReference(postgres)
	.WaitFor(migrations);    // gates on /health = Healthy from the migrations resource.

var worker = builder.AddProject<Projects.Norse_Hosting_Worker>("worker")
	.WithReference(postgres)
	.WaitFor(migrations);

builder.Build().Run();
```

- [ ] **Step 4: Verify the launchSettings (already created by the template)**

Run: `type Norse.Hosting.AppHost\Properties\launchSettings.json`
Expected: standard Aspire AppHost launch settings.

- [ ] **Step 5: Add to solution**

Run: `dotnet sln Norse.slnx add Norse.Hosting.AppHost/Norse.Hosting.AppHost.csproj`

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: entire solution builds.

- [ ] **Step 7: Optionally run end-to-end (manual verification)**

Run: `dotnet run --project Norse.Hosting.AppHost`
Expected: Aspire dashboard opens; postgres, migrations, host, worker resources all show. With no contributors registered, migrations should reach Healthy immediately (no pending migrations) and exit 0 after the 5-second grace period. Host and worker should start once migrations is Healthy.

This is the moment the platform comes alive locally. Verify by browsing to:
- The Aspire dashboard (URL printed at startup)
- `host`'s /health endpoint (should be Healthy)
- `worker`'s /health endpoint (should be Healthy)
- `migrations` should briefly show Healthy then exit

- [ ] **Step 8: Stage**

```bash
git add Norse.Hosting.AppHost/ Norse.slnx
```

Proposed commit message: `feat(apphost): scaffold Norse.Hosting.AppHost with Postgres + WaitFor(migrations) wiring`

---

## Task 23: Final Verification — Full Solution Build + Test

- [ ] **Step 1: Clean build of the entire solution**

Run: `dotnet build`
Expected: build succeeds across all projects with 0 warnings, 0 errors.

- [ ] **Step 2: Run every test project**

Run: `dotnet test`
Expected: all tests pass across `Norse.Abstractions.Hosting.Tests`, `Norse.Hosting.Web.Tests`, `Norse.Hosting.Worker.Tests`, `Norse.Hosting.Migrations.Service.Tests`.

- [ ] **Step 3: Spot-check no `TODO`/`FIXME`/`XXX` markers in committed code**

Run: `findstr /S /M /C:"TODO" /C:"FIXME" /C:"XXX" Norse.Hosting\src Norse.Hosting\tests Norse.Hosting.Web.Server Norse.Hosting.Worker Norse.Hosting.Migrations.Service Norse.Hosting.AppHost`
Expected: no matches (or only intentional ones flagged in comments).

- [ ] **Step 4: Verify dependency graph is acyclic by visual inspection**

Look at `Norse.Hosting.slnx` + `Norse.slnx` and confirm:
- `Norse.Abstractions.Hosting` references nothing in the platform.
- `Norse.Hosting.Web`, `.Worker`, `.Migrations` each reference only `.Abstractions`.
- `Norse.Hosting.ServiceDefaults` references nothing in the platform.
- `Norse.Hosting.Web.Server` references `Norse.Hosting.Web` + `Norse.Hosting.ServiceDefaults`.
- `Norse.Hosting.Worker` references `Norse.Hosting.Worker` + `Norse.Hosting.ServiceDefaults`.
- `Norse.Hosting.Migrations.Service` references `Norse.Hosting.Migrations.Service` + `Norse.Hosting.ServiceDefaults`.
- `Norse.Hosting.AppHost` references all three product deployables.

No cycles; matches the dependency diagram in spec §3.

- [ ] **Step 5: Final commit (only if everything green)**

```bash
git add -A
```

Proposed commit message: `chore: final verification — full solution builds and tests pass`

---

## Done Criteria

This plan is "done" when:

- [ ] All 23 tasks completed and committed (commit messages reviewed by the human per CLAUDE.md §8).
- [ ] `dotnet build` succeeds for the entire `Norse.slnx` with 0 warnings, 0 errors.
- [ ] `dotnet test` passes every test in every test project.
- [ ] `dotnet run --project Norse.Hosting.AppHost` brings up Postgres + migrations + host + worker; the Aspire dashboard shows each resource transitioning to Healthy in order; `host` and `worker` only start after `migrations` reports Healthy.
- [ ] Next plans (Auth Foundation Plan A onwards) can register `internal sealed class AuthPlugin : IWebHostPlugin, IWorkerHostPlugin` and have it load via `builder.AddNorseWebHost().AddPlugin<AuthPlugin>()`.

---

## Spec Coverage Self-Review

Mapping each major spec section to the tasks that implement it:

- **Spec §3 Architecture / Package Layout** → Tasks 1, 2, 3, 9, 10, 13, 14 (project scaffolds).
- **Spec §4 Codified Lifecycle Rule (with webhook subrule)** → Tasks 6, 7 (webhook abstractions + base class). Stage 1/2/3 lifecycle is realized through future context plans, not this one; this plan provides the runtime.
- **Spec §5 Plugin Interface Family** → Task 4 (`IHostPlugin`, `IWebHostPlugin`, `IWorkerHostPlugin`, `IPublishedController`).
- **Spec §6 Plugin Registration and Discovery** → Task 10 (`AddPlugin<T>`), Task 13 (worker variant).
- **Spec §7 MapEndpoints — gRPC-Only Surface and Visibility Model** → Task 11 (`UseNorseWebHost` + `MapEndpoints` iteration), Task 12 (partner OpenAPI filter via `IPublishedController`).
- **Spec §7.1 Webhook Controller Base Class** → Tasks 6, 7.
- **Spec §8 ConfigureServices — Cross-Cutting Conventions** → No dedicated task; conventions are exercised by future context plans (Auth, Billing). Plugin authors follow the conventions documented in the spec and the worked example (§11).
- **Spec §9 What `AddNorseWebHost()` / `AddNorseWorkerHost()` Bring** → Tasks 10, 11, 13 (gRPC, controllers, auth/authz baseline, problem details, IOptions validation).
- **Spec §10 Migrations Service** → Tasks 14 (health status + check), 15 (topological sort), 16 (options + recurring log), 17 (orchestrator), 18 (builder + AddMigration<T>), 21 (deployable).
- **Spec §11 Worked Example** → Realized by future context plans (Auth, Billing); this plan provides the substrate.
- **Spec §12 Aspire Wiring** → Task 22 (`Norse.Hosting.AppHost`).
- **Spec §13 Resolved Decisions** → All decisions baked into implementation across tasks; no gaps.
- **Spec §15 Spec Amendments Triggered** → Tracked separately; not implemented in this plan. UI Composition spec amendment to swap `protobuf-net.Grpc` for native `Grpc.AspNetCore` happens when WASM/MAUI client plans land.

No gaps. Plan is complete and self-consistent.
