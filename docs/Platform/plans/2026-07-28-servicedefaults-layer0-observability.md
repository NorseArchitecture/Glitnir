# ServiceDefaults Layer 0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `Norse.Infrastructure.ServiceDefaults` (the shared observability root) in Midgard, wire all four Yggdrasil containers to it with one line each, and compose all four into the Bifröst AppHost so telemetry from every container lands in one Aspire dashboard.

**Architecture:** One new Midgard src project exposing two `IHostApplicationBuilder` extensions (`AddServiceDefaults()`, `AddDefaultHealthChecks()`), Aspire-template-faithful minus everything above Layer 0. No FrameworkReference anywhere in its graph — that is the Worker guarantee, and it gets its own test. Spec: `../../Platform/specs/2026-07-28-servicedefaults-layer0-observability-design.md` (authoritative for every decision below).

**Tech Stack:** .NET 11 preview / C# 15, OpenTelemetry 1.x (Extensions.Hosting, OTLP exporter, Runtime instrumentation), Microsoft.Extensions.Diagnostics.HealthChecks, xUnit v3 on MTP v2 + Shouldly.

## Global Constraints

- **Hands-off files (halt-and-ask, restate in every subagent dispatch):** every `Directory.Build.props`/`Directory.Build.targets` (root, `src/`, `tests/` — all realms), `.editorconfig`, `nuget.config`, `global.json`. Scatter-managed from Ginnungagap; editing any of them is a stop-and-ask event, never a local fix.
- **Branch discipline:** Midgard and Yggdrasil work happens on a local branch `feature/servicedefaults-layer0` in each realm; subagents may commit there (local only — never push, never master). **Bifröst and Glitnir changes are staged only, never committed, and Bifröst stays on `master` — no branch, period.** Re-verify `git branch --show-current` before every commit (Buvy commits in parallel via GitHub Desktop).
- **Spec §2.1 naming:** project folders are brand-free (`Infrastructure.ServiceDefaults`); `Norse.` is injected by the realm's `Directory.Build.props`. No lore in any path, namespace, or identifier.
- **Inherited by every Midgard src project (do not re-declare):** `net11.0`, `LangVersion=preview`, `TreatWarningsAsErrors`, `GenerateDocumentationFile`, `IsAotCompatible=true`, `<InternalsVisibleTo Include="$(AssemblyName).Tests" />`, `AssemblyName`/`RootNamespace`/`PackageId` = `Norse.$(MSBuildProjectName)`.
- **Package versions:** stable OTel packages `Version="1.*"`; framework-tracking `Microsoft.Extensions.*` packages `Version="11.*-*"`. One `<PropertyGroup>` + one `<ItemGroup>` per csproj, members alphabetical.
- **House style (full law: `../../house-rules.md`):** tabs; `sealed`/`static`/`abstract` classes only; extension blocks (C# 14 `extension(...)` syntax) over static extension methods; fluent chains, dot-leading; target-typed `new()`; collection expressions; XML docs on public src members; `ConfigureAwait(false)` in src, never in tests; test classes `public sealed`, test methods bare `void`/`async Task` with sentence_shaped_names; Shouldly for assertions (globally `using`'d — never re-add usings for Shouldly/Xunit/NSubstitute in test files).
- **Suppression Law:** no `<NoWarn>`, no pragmas. If the same warning fires repeatedly (watch for AOT/trim warnings from OTel packages under `IsAotCompatible`): **stop and report** the code, project context, and reason — repeated-hit protocol, not silencing.
- **No health participation anywhere in this plan:** no endpoints, no publishers, no probes — and no composition either. Layer 0 ships `AddDefaultHealthChecks()` as an uncalled rail (spec §2.6, Amendment A); `AddServiceDefaults()` is pure emission and no container registers a single check. The Migrations Service never participates in health at all — exit code is its contract (spec §2.7).
- **The OTLP guard is ours, not the SDK's** (spec §3.7): `UseOtlpExporter()` without a configured endpoint defaults to `localhost:4317` and fails on export — it must sit behind an explicit endpoint-presence `if`.

---

### Task 0: Name the mechanism of the Migrations Service misdiagnosis (spec §1.1 case file)

**Files:** none modified. Produces a finding, and the finding blocks everything: no subsequent task may begin until the mechanism is named and recorded. Context: spec §1.1 already records that the "observably silent" claim was a misdiagnosis (Buvy's same-day correction — the container is not mute); this task pins *how* the misread happened and files the evidence.

- [ ] **Step 1: Run the Migrations Service bare**

```bash
env -C /home/buvy/code/NorseArchitecture/Bifrost dotnet run --project Yggdrasil/src/Hosting.Migrations.Service
```

(Docker up; expect DB-dependent behavior — the interesting evidence is stdout, not success.) Record: does console output appear at all? Which categories (`Microsoft.Hosting.Lifetime`, `MigrationRunnerService` LoggerMessage output)?

- [ ] **Step 2: Run under the AppHost and audit every pane**

```bash
env -C /home/buvy/code/NorseArchitecture/Bifrost dotnet run --project src/Orchestration.AppHost
```

In the dashboard, check the `migrations` resource in BOTH places: the per-resource **Console logs** pane AND the **Structured logs** view. Record what appears where.

- [ ] **Step 3: Adjudicate against the working hypothesis**

Leading hypothesis (spec §1.1): the logs were never missing. `Host.CreateApplicationBuilder` registers the console provider by default, so stdout was live the whole time — but the morning audit read the dashboard's **Structured logs** view, which is OTLP-fed and was empty because nothing exported OTLP yet. The console output sat in the per-resource console pane. If confirmed: the gremlin was a wrong-pane audit, not a mute pipeline — changes nothing about the fix (always-on console + OTLP export remain correct) and everything about the case file. If the hypothesis is DISCONFIRMED — bare stdout is genuinely empty — **halt and report**: something is clearing providers, and the spec's §2.4 assertion may be masking a live bug.

- [ ] **Step 4: Record the finding durably**

Append a short addendum to the spec's §1.1 (`../specs/2026-07-28-servicedefaults-layer0-observability-design.md`, staged in Glitnir — never committed): mechanism named, evidence per pane, hypothesis confirmed/disconfirmed, date. The Gjallarhorn precedent earned its citation by being written down; the misdiagnosis gets the same treatment.

---

### Task 1: Scaffold `Infrastructure.ServiceDefaults` + test project in Midgard

**Files:**
- Create: `Midgard/src/Infrastructure.ServiceDefaults/Infrastructure.ServiceDefaults.csproj`
- Create: `Midgard/tests/Infrastructure.ServiceDefaults.Tests/Infrastructure.ServiceDefaults.Tests.csproj`
- Modify: `Midgard/Midgard.slnx` (add both projects to the existing `/src/` and `/tests/` folders)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: the two csproj files and slnx entries every later Midgard task builds inside. Namespace for all subsequent code: `Norse.Infrastructure.ServiceDefaults`.

- [ ] **Step 1: Create the Midgard feature branch**

```bash
git -C Midgard checkout -b feature/servicedefaults-layer0
```

(If already on `feature/servicedefaults-layer0`, continue.)

- [ ] **Step 2: Create the src csproj**

`Midgard/src/Infrastructure.ServiceDefaults/Infrastructure.ServiceDefaults.csproj` (tabs, one PropertyGroup, one ItemGroup, alphabetical):

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Infrastructure.ServiceDefaults: the shared observability root every container composes — resource attributes, always-on console logging, OTel tracing and metrics with the Norse.* wildcard subscription, OTLP export behind an explicit endpoint guard, and the self liveness check. Targets IHostApplicationBuilder so plain hosts and web hosts call the exact same line; carries no FrameworkReference, ever.</Description>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" Version="11.*-*" />
		<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="11.*-*" />
		<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.*" />
		<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.*" />
		<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.*" />
	</ItemGroup>
</Project>
```

Note what is absent, deliberately: `OpenTelemetry.Instrumentation.Process` (beta, excluded per spec §3.1), `OpenTelemetry.Exporter.Console` (debug-grade, spec §2.4), any `FrameworkReference`.

- [ ] **Step 3: Create the test csproj**

`Midgard/tests/Infrastructure.ServiceDefaults.Tests/Infrastructure.ServiceDefaults.Tests.csproj` (xUnit/Shouldly/MTP stack comes from `tests/Directory.Build.props` — do not re-add):

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.Extensions.Hosting" Version="11.*-*" />
		<PackageReference Include="OpenTelemetry.Exporter.InMemory" Version="1.*" />
		<ProjectReference Include="../../src/Infrastructure.ServiceDefaults/Infrastructure.ServiceDefaults.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 4: Add both projects to `Midgard.slnx`**

Inside the existing `<Folder Name="/src/">` element add:

```xml
<Project Path="src/Infrastructure.ServiceDefaults/Infrastructure.ServiceDefaults.csproj" />
```

Inside the existing `<Folder Name="/tests/">` element add:

```xml
<Project Path="tests/Infrastructure.ServiceDefaults.Tests/Infrastructure.ServiceDefaults.Tests.csproj" />
```

- [ ] **Step 5: Verify the empty pair builds**

```bash
env -C /home/buvy/code/NorseArchitecture/Bifrost/Midgard dotnet build tests/Infrastructure.ServiceDefaults.Tests
```

Expected: build succeeds with zero warnings (warnings are errors). If AOT/trim warnings fire from the OTel packages, **stop — repeated-hit protocol** (Global Constraints).

- [ ] **Step 6: Commit**

```bash
git -C Midgard branch --show-current   # must print feature/servicedefaults-layer0
git -C Midgard add src/Infrastructure.ServiceDefaults tests/Infrastructure.ServiceDefaults.Tests Midgard.slnx
git -C Midgard commit -m "Scaffold Infrastructure.ServiceDefaults and its test project

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: `AddDefaultHealthChecks()` — the registration rail

**Files:**
- Create: `Midgard/src/Infrastructure.ServiceDefaults/HostApplicationBuilderExtensions.cs`
- Create: `Midgard/tests/Infrastructure.ServiceDefaults.Tests/HostApplicationBuilderExtensionsTests.cs`

**Interfaces:**
- Consumes: Task 1's projects.
- Produces: `public IHostApplicationBuilder AddDefaultHealthChecks()` on `IHostApplicationBuilder` (extension block in `public static class HostApplicationBuilderExtensions`, namespace `Norse.Infrastructure.ServiceDefaults`). Registers check name `"self"`, tag `"live"`. Lives in the same class as Task 3's `AddServiceDefaults()`, which deliberately does **not** call it — the rail ships uncomposed in Layer 0 (spec §2.6); no container calls it until Layer 2/3.

- [ ] **Step 1: Write the failing test**

`HostApplicationBuilderExtensionsTests.cs` (Shouldly/Xunit usings are global — only add the ones below):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norse.Infrastructure.ServiceDefaults;

namespace Infrastructure.ServiceDefaults.Tests;

public sealed class HostApplicationBuilderExtensionsTests
{
	[Fact]
	void Add_default_health_checks_registers_the_self_liveness_check()
	{
		HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new());
		builder.AddDefaultHealthChecks();
		using var host = builder.Build();
		var registration = host.Services
			.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
			.Value.Registrations.ShouldHaveSingleItem();
		registration.Name.ShouldBe("self");
		registration.Tags.ShouldContain("live");
	}
}
```

`CreateEmptyApplicationBuilder` is deliberate throughout this test file: it registers **no** defaults, so every assertion proves ServiceDefaults did the work, not the host builder.

- [ ] **Step 2: Run it to verify it fails**

```bash
env -C /home/buvy/code/NorseArchitecture/Bifrost/Midgard dotnet test tests/Infrastructure.ServiceDefaults.Tests
```

Expected: compile failure — `AddDefaultHealthChecks` does not exist.

- [ ] **Step 3: Implement**

`HostApplicationBuilderExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Norse.Infrastructure.ServiceDefaults;

/// <summary>
/// Extension methods for <see cref="IHostApplicationBuilder"/> composing the shared observability
/// root — the one surface every container calls regardless of host shape.
/// </summary>
public static class HostApplicationBuilderExtensions
{
	/// <param name="builder">The host application builder.</param>
	extension(IHostApplicationBuilder builder)
	{
		/// <summary>
		/// Registers health-check services with the <c>self</c> liveness check (tagged <c>live</c>) —
		/// the host-neutral registration rail later layers hang checks on. No reporter is registered
		/// here: web hosts map endpoints in the ASP.NET layer, the worker's publisher arrives with the
		/// messaging layer, and the migrations service never participates (its exit code is the contract).
		/// </summary>
		/// <returns>The same <paramref name="builder"/> for chaining.</returns>
		public IHostApplicationBuilder AddDefaultHealthChecks()
		{
			builder.Services
				.AddHealthChecks()
				.AddCheck("self", static () => HealthCheckResult.Healthy(), ["live"]);
			return builder;
		}
	}
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
env -C /home/buvy/code/NorseArchitecture/Bifrost/Midgard dotnet test tests/Infrastructure.ServiceDefaults.Tests
```

Expected: PASS, zero warnings.

- [ ] **Step 5: Commit**

```bash
git -C Midgard branch --show-current   # must print feature/servicedefaults-layer0
git -C Midgard add src/Infrastructure.ServiceDefaults tests/Infrastructure.ServiceDefaults.Tests
git -C Midgard commit -m "Add AddDefaultHealthChecks with the self liveness check

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: `AddServiceDefaults()` — the OTel core (resource, logging, tracing, metrics)

**Files:**
- Modify: `Midgard/src/Infrastructure.ServiceDefaults/HostApplicationBuilderExtensions.cs` (add `AddServiceDefaults()` to the same extension block)
- Modify: `Midgard/tests/Infrastructure.ServiceDefaults.Tests/HostApplicationBuilderExtensionsTests.cs` (add four tests)

**Interfaces:**
- Consumes: nothing from Task 2 — deliberately. Emission never composes the health rail (spec §2.6, Amendment A); `AddDefaultHealthChecks()` ships uncalled in Layer 0.
- Produces: `public IHostApplicationBuilder AddServiceDefaults()` — pure emission. Resource keys emitted: `service.name`, `service.version`, `service.instance.id`, `deployment.environment.name` (current semconv key — spec §3.3 defers the choice to implementation; this plan picks the new key, and Task 8's live verification confirms the dashboard renders it). Wildcard subscriptions: `AddSource("Norse.*")`, `AddMeter("Norse.*")`. No OTLP yet — that is Task 4.

- [ ] **Step 1: Write the five failing tests**

Append to `HostApplicationBuilderExtensionsTests.cs` — and extend the using block at the top with:

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
```

```csharp
	[Fact]
	void Service_defaults_stamp_the_resource_with_service_identity()
	{
		HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new()
		{
			ApplicationName = "Norse.TestHost",
			EnvironmentName = "Testing",
		});
		builder.AddServiceDefaults();
		using var host = builder.Build();
		var attributes = host.Services
			.GetRequiredService<TracerProvider>()
			.GetResource().Attributes
			.ToDictionary(a => a.Key, a => a.Value);
		attributes["service.name"].ShouldBe("Norse.TestHost");
		attributes.ShouldContainKey("service.instance.id");
		attributes["deployment.environment.name"].ShouldBe("Testing");
	}

	[Fact]
	void Service_defaults_keep_the_console_provider_and_enrich_the_otel_logger()
	{
		HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new());
		builder.AddServiceDefaults();
		using var host = builder.Build();
		host.Services.GetServices<ILoggerProvider>().ShouldContain(p => p is ConsoleLoggerProvider);
		var options = host.Services.GetRequiredService<IOptionsMonitor<OpenTelemetryLoggerOptions>>().CurrentValue;
		options.IncludeFormattedMessage.ShouldBeTrue();
		options.IncludeScopes.ShouldBeTrue();
		options.ParseStateValues.ShouldBeTrue();
	}

	[Fact]
	void Norse_activity_sources_are_captured_and_foreign_sources_are_not()
	{
		List<Activity> exported = [];
		HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new());
		builder.AddServiceDefaults();
		builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exported));
		using var host = builder.Build();
		using ActivitySource
			norse = new("Norse.Test"),
			foreign = new("Foreign.Test");
		norse.StartActivity("norse-op")?.Dispose();
		foreign.StartActivity("foreign-op")?.Dispose();
		host.Services.GetRequiredService<TracerProvider>().ForceFlush();
		exported.ShouldHaveSingleItem().OperationName.ShouldBe("norse-op");
	}

	[Fact]
	void Norse_meters_are_captured_by_the_wildcard_subscription()
	{
		List<Metric> exported = [];
		HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new());
		builder.AddServiceDefaults();
		builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddInMemoryExporter(exported));
		using var host = builder.Build();
		using Meter meter = new("Norse.TestMeter");
		meter.CreateCounter<long>("norse_counter").Add(1);
		host.Services.GetRequiredService<MeterProvider>().ForceFlush();
		exported.ShouldContain(m => m.Name == "norse_counter");
	}

	[Fact]
	void Service_defaults_register_no_health_checks()
	{
		HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new());
		builder.AddServiceDefaults();
		using var host = builder.Build();
		host.Services.GetService<IOptions<HealthCheckServiceOptions>>()
			?.Value.Registrations.ShouldBeEmpty();
	}
```

(On the last test: if `AddHealthChecks()` was never called, the options service may resolve with zero registrations or be absent entirely — the null-conditional accepts either shape. Verify the actual resolution behavior at implementation and tighten the assertion to match reality. Emission does not imply participation — spec §2.6.)

- [ ] **Step 2: Run to verify the five fail**

```bash
env -C /home/buvy/code/NorseArchitecture/Bifrost/Midgard dotnet test tests/Infrastructure.ServiceDefaults.Tests
```

Expected: compile failure — `AddServiceDefaults` does not exist.

- [ ] **Step 3: Implement**

Extend the using block in `HostApplicationBuilderExtensions.cs` with:

```csharp
using System.Reflection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
```

Add to the extension block (above `AddDefaultHealthChecks`):

```csharp
		/// <summary>
		/// Composes the shared observability root: resource attributes (<c>service.name</c> from
		/// <c>OTEL_SERVICE_NAME</c> or the application name, <c>service.version</c>,
		/// <c>service.instance.id</c>, <c>deployment.environment.name</c>), always-on console logging
		/// alongside the OpenTelemetry <see cref="Microsoft.Extensions.Logging.ILogger"/> provider,
		/// tracing and metrics with the <c>Norse.*</c> wildcard subscription plus .NET runtime
		/// instrumentation. Every container calls this — there is no opt-out and no lightweight
		/// variant. Health registration is deliberately not composed here: registration is
		/// participation, and participation arrives with the layer that guarantees a consumer
		/// (see <see cref="AddDefaultHealthChecks"/>).
		/// </summary>
		/// <returns>The same <paramref name="builder"/> for chaining.</returns>
		public IHostApplicationBuilder AddServiceDefaults()
		{
			builder.Logging
				.AddConsole()
				.AddOpenTelemetry(static options =>
				{
					options.IncludeFormattedMessage = true;
					options.IncludeScopes = true;
					options.ParseStateValues = true;
				});
			builder.Services
				.AddOpenTelemetry()
				.ConfigureResource(resource => resource
					.AddService(
						builder.Configuration["OTEL_SERVICE_NAME"] ?? builder.Environment.ApplicationName,
						serviceVersion: Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
					.AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
				.WithTracing(static tracing => tracing.AddSource("Norse.*"))
				.WithMetrics(static metrics => metrics
					.AddMeter("Norse.*")
					.AddRuntimeInstrumentation());
			return builder;
		}
```

Notes for the implementer:
- `AddService` auto-generates `service.instance.id` (its `autoGenerateServiceInstanceId` default) — do not hand-roll one.
- `AddAttributes([new(...)])` — the collection expression targets `KeyValuePair<string, object>`; if the compiler cannot infer it, the element form is `new KeyValuePair<string, object>("deployment.environment.name", builder.Environment.EnvironmentName)` with the type name hoisted into scope via the existing `System.Collections.Generic` implicit using.
- Console logging is `Microsoft.Extensions.Logging.Console` (BCL), **not** `OpenTelemetry.Exporter.Console` — spec §2.4.

- [ ] **Step 4: Run to verify all tests pass**

```bash
env -C /home/buvy/code/NorseArchitecture/Bifrost/Midgard dotnet test tests/Infrastructure.ServiceDefaults.Tests
```

Expected: 6 passing (Task 2's + these five), zero warnings. If `GetResource()` does not resolve: it is `OpenTelemetry.Resources.ResourceExtensions.GetResource(BaseProvider)` — public, shipped in the core `OpenTelemetry` package; confirm the `OpenTelemetry.Resources` using survived IDE0005.

- [ ] **Step 5: Commit**

```bash
git -C Midgard branch --show-current   # must print feature/servicedefaults-layer0
git -C Midgard add src/Infrastructure.ServiceDefaults tests/Infrastructure.ServiceDefaults.Tests
git -C Midgard commit -m "Add AddServiceDefaults OTel core: resource identity, logging, wildcard tracing and metrics

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: The OTLP endpoint guard

**Files:**
- Modify: `Midgard/src/Infrastructure.ServiceDefaults/HostApplicationBuilderExtensions.cs`
- Modify: `Midgard/tests/Infrastructure.ServiceDefaults.Tests/HostApplicationBuilderExtensionsTests.cs` (two tests)

**Interfaces:**
- Consumes: Task 3's `AddServiceDefaults()`.
- Produces: the same method, now with OTLP export enabled if and only if `OTEL_EXPORTER_OTLP_ENDPOINT` is configured.

- [ ] **Step 1: Write the two failing-or-proving tests**

Append to the test class (`Microsoft.Extensions.Configuration` joins the using block for `AddInMemoryCollection`):

```csharp
	[Fact]
	async Task A_host_with_no_otlp_endpoint_builds_and_starts_cleanly()
	{
		HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new());
		builder.AddServiceDefaults();
		using var host = builder.Build();
		await host.StartAsync(TestContext.Current.CancellationToken);
		await host.StopAsync(TestContext.Current.CancellationToken);
	}

	[Fact]
	async Task A_host_with_an_otlp_endpoint_builds_and_starts_cleanly()
	{
		HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new());
		builder.Configuration.AddInMemoryCollection(
			[new("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317")]);
		builder.AddServiceDefaults();
		using var host = builder.Build();
		await host.StartAsync(TestContext.Current.CancellationToken);
		await host.StopAsync(TestContext.Current.CancellationToken);
	}
```

These are smoke tests by design: `UseOtlpExporter()` offers no clean seam to assert attachment without an export attempt, and the endpoint-present path is *positively* verified live in Task 8 (telemetry visible in the dashboard). What the pair pins down is the spec §3.7 contract — absence must not crash, presence must not require a reachable collector to boot.

- [ ] **Step 2: Run — expect both to pass already, then break the guard to prove the tests bite**

```bash
env -C /home/buvy/code/NorseArchitecture/Bifrost/Midgard dotnet test tests/Infrastructure.ServiceDefaults.Tests
```

Both new tests pass against Task 3's implementation (no OTLP wired yet — nothing to fail). Now add the export wiring **unguarded** first:

In `AddServiceDefaults()`, insert before the `return`:

```csharp
			builder.Services.AddOpenTelemetry().UseOtlpExporter();
```

Run the tests again. Expected: still green — which demonstrates the failure mode is *silent* (export failures happen in the background batch processor, exactly why the spec calls this trap out). This is the one place TDD cannot corner the bug; the guard is law-driven, not test-driven. Document nothing; proceed.

- [ ] **Step 3: Install the guard**

Replace the unguarded line with:

```csharp
			// The guard is ours, not the SDK's: UseOtlpExporter() with no endpoint configured defaults
			// to localhost:4317 and fails on every export attempt (spec §3.7). Behind this check,
			// absence is a genuine no-op and console still works.
			if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
			{
				builder.Services.AddOpenTelemetry().UseOtlpExporter();
			}
```

(`OpenTelemetry` joins the src using block for `UseOtlpExporter`.)

- [ ] **Step 4: Run the full test file**

```bash
env -C /home/buvy/code/NorseArchitecture/Bifrost/Midgard dotnet test tests/Infrastructure.ServiceDefaults.Tests
```

Expected: 8 passing, zero warnings.

- [ ] **Step 5: Commit**

```bash
git -C Midgard branch --show-current   # must print feature/servicedefaults-layer0
git -C Midgard add src/Infrastructure.ServiceDefaults tests/Infrastructure.ServiceDefaults.Tests
git -C Midgard commit -m "Gate OTLP export behind explicit endpoint presence

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: The Worker guarantee test + full Midgard gate

**Files:**
- Modify: `Midgard/tests/Infrastructure.ServiceDefaults.Tests/HostApplicationBuilderExtensionsTests.cs` (one test)

**Interfaces:**
- Consumes: the finished `HostApplicationBuilderExtensions` from Tasks 2–4.
- Produces: the compiler-checked promise Yggdrasil's Worker and Migrations Service rely on in Task 6.

- [ ] **Step 1: Write the guarantee test**

```csharp
	[Fact]
	void The_service_defaults_assembly_references_nothing_from_aspnetcore()
	{
		typeof(HostApplicationBuilderExtensions).Assembly
			.GetReferencedAssemblies()
			.ShouldAllBe(a => !a.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
	}
```

This must pass immediately — it exists as a tripwire for future edits, not as a red-green cycle. If it fails now, a package from Task 1 dragged ASP.NET in: **stop and report** (that contradicts spec §2.6's dependency-graph claim and needs a ruling, not a workaround).

- [ ] **Step 2: Run the whole Midgard solution gate**

```bash
env -C /home/buvy/code/NorseArchitecture/Bifrost/Midgard dotnet build Midgard.slnx
env -C /home/buvy/code/NorseArchitecture/Bifrost/Midgard dotnet test Midgard.slnx
```

Expected: full solution builds warning-free; all test projects green (proves no collateral damage to the other seven test projects).

- [ ] **Step 3: Commit**

```bash
git -C Midgard branch --show-current   # must print feature/servicedefaults-layer0
git -C Midgard add tests/Infrastructure.ServiceDefaults.Tests
git -C Midgard commit -m "Pin the Worker guarantee: no AspNetCore reference in ServiceDefaults

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Wire all four Yggdrasil containers

**Files:**
- Modify: `Yggdrasil/src/Hosting.Migrations.Service/Hosting.Migrations.Service.csproj` and `Program.cs`
- Modify: `Yggdrasil/src/Hosting.Worker/Hosting.Worker.csproj` and `Program.cs`
- Modify: `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj` and `Program.cs`
- Modify: `Yggdrasil/src/Hosting.Stories.Server/Hosting.Stories.Server.csproj` and `Program.cs`
- Modify: `Yggdrasil/Directory.Packages.props` (**one `PackageVersion` line only** — this is the CPM catalog, not a scatter-managed build props; the hands-off law covers `Directory.Build.props`/`.targets`, not this file)

**Interfaces:**
- Consumes: `Norse.Infrastructure.ServiceDefaults` → `AddServiceDefaults()` (Tasks 2–4). `WebApplicationBuilder` implements `IHostApplicationBuilder`, so all four hosts call the identical line.
- Produces: four containers composing the root; the AppHost work in Task 7 assumes exactly this.

- [ ] **Step 1: Create the Yggdrasil feature branch**

```bash
git -C Yggdrasil checkout -b feature/servicedefaults-layer0
```

- [ ] **Step 2: Add the CPM pin**

In `Yggdrasil/Directory.Packages.props`, in the Midgard block (alphabetical among the existing `Norse.Infrastructure.*` entries, before `Norse.Infrastructure.Web.Client`):

```xml
		<PackageVersion Include="Norse.Infrastructure.ServiceDefaults" Version="$(MidgardVersion)" />
```

Known consequence, not a bug: standalone (package-mode) Yggdrasil restore fails until Midgard ships a version containing the package — Bifröst's `UseProjectReferences` composition builds fine meanwhile. That is the established realm ship-gate flow; the realm-pin bump after Midgard's release is Buvy's ceremony, not this plan's.

- [ ] **Step 3: Add the NorseRef to each of the four csproj files**

In each csproj's `<ItemGroup>`, alphabetical among any existing `NorseRef` items:

```xml
		<NorseRef Include="Infrastructure.ServiceDefaults">
			<Repo>Midgard</Repo>
		</NorseRef>
```

(For `Hosting.Worker` and `Hosting.Stories.Server` this is the first `NorseRef` — the item goes in the existing `<ItemGroup>` ahead of the `PackageReference` lines, matching `Hosting.Migrations.Service`'s layout.)

- [ ] **Step 4: Add the one-liner to each Program.cs**

Each file gains `using Norse.Infrastructure.ServiceDefaults;` (hoisted to the top, alphabetical among existing `Norse.*` usings where present) and the call immediately after the builder is created:

`Hosting.Migrations.Service/Program.cs` becomes:

```csharp
using Norse.Infrastructure.ServiceDefaults;

Console.Title = "Norse Migrations Service";
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddNorseMigrations();
await builder.Build().RunAsync().ConfigureAwait(false);
```

`Hosting.Worker/Program.cs` becomes:

```csharp
using Norse.Infrastructure.ServiceDefaults;

Console.Title = "Norse Worker";
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
await builder.Build().RunAsync().ConfigureAwait(false);
```

`Hosting.Web.Server/Program.cs` and `Hosting.Stories.Server/Program.cs`: add the using, then insert `builder.AddServiceDefaults();` as the first statement after `var builder = WebApplication.CreateBuilder(args);` — before any other `builder.*` configuration. Do not touch anything else in either file.

- [ ] **Step 5: Build all four hosts from the Bifröst composition**

```bash
env -C /home/buvy/code/NorseArchitecture/Bifrost dotnet build Yggdrasil/src/Hosting.Migrations.Service
env -C /home/buvy/code/NorseArchitecture/Bifrost dotnet build Yggdrasil/src/Hosting.Worker
env -C /home/buvy/code/NorseArchitecture/Bifrost dotnet build Yggdrasil/src/Hosting.Web.Server
env -C /home/buvy/code/NorseArchitecture/Bifrost dotnet build Yggdrasil/src/Hosting.Stories.Server
```

Expected: all four build warning-free (project-reference mode resolves the new Midgard project). Run Yggdrasil's tests if any exist for touched projects:

```bash
env -C /home/buvy/code/NorseArchitecture/Bifrost/Yggdrasil dotnet test Yggdrasil.slnx
```

- [ ] **Step 6: Commit**

```bash
git -C Yggdrasil branch --show-current   # must print feature/servicedefaults-layer0
git -C Yggdrasil add src/Hosting.Migrations.Service src/Hosting.Worker src/Hosting.Web.Server src/Hosting.Stories.Server Directory.Packages.props
git -C Yggdrasil commit -m "Compose ServiceDefaults in all four hosting containers

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: Compose all four containers in the Bifröst AppHost

**Files:**
- Modify: `src/Orchestration.AppHost/Orchestration.AppHost.csproj` (add the Stories.Server ProjectReference — Worker's already exists)
- Modify: `src/Orchestration.AppHost/AppHost.cs`

**⚠ Bifröst law: stay on `master`; STAGE ONLY — never commit.**

**Interfaces:**
- Consumes: the four wired hosts from Task 6.
- Produces: AppHost resources `migrations`, `web`, `worker`, `stories` — the names Task 8's dashboard verification looks for.

- [ ] **Step 1: Confirm Bifröst is on master**

```bash
git -C /home/buvy/code/NorseArchitecture/Bifrost branch --show-current
```

Must print `master`. If not: **stop and ask** — never create or switch branches in Bifröst.

- [ ] **Step 2: Add the Stories.Server project reference**

In `src/Orchestration.AppHost/Orchestration.AppHost.csproj`, alphabetical among the existing ProjectReferences:

```xml
		<ProjectReference Include="..\..\Yggdrasil\src\Hosting.Stories.Server\Hosting.Stories.Server.csproj" />
```

- [ ] **Step 3: Add the two resources to `AppHost.cs`**

After the existing `web` registration and before the final `await builder.Build().RunAsync()...` line:

```csharp
builder
	.AddProject<Projects.Hosting_Worker>("worker")
	.WaitForCompletion(migrationsService);

builder.AddProject<Projects.Hosting_Stories_Server>("stories");
```

The Worker waits on migrations (it will grow DB dependencies at Layer 1; ordering it now costs nothing and matches `web`). Stories has no dependencies — it composes nothing but static catalog content. Identifier verified against current `AppHost.cs`: the migrations resource builder is already captured as `var migrationsService = builder.AddProject<Projects.Hosting_Migrations_Service>("migrations")…` — the snippet's `migrationsService` matches reality as-is.

- [ ] **Step 4: Build the AppHost**

```bash
env -C /home/buvy/code/NorseArchitecture/Bifrost dotnet build src/Orchestration.AppHost
```

Expected: builds warning-free; the `Projects.Hosting_Stories_Server` type is generated by the reference added in Step 2.

- [ ] **Step 5: Stage — do NOT commit**

```bash
git -C /home/buvy/code/NorseArchitecture/Bifrost add src/Orchestration.AppHost
```

Stop there. No commit, no push — Buvy commits Bifröst himself.

---

### Task 8: Live verification against the dashboard (DoD sweep)

**Files:** none created or modified — this task produces evidence, and evidence only. If anything below fails, **halt and report**; do not patch code outside the plan (that is a review round-trip, not an improvisation license).

**Interfaces:**
- Consumes: everything.
- Produces: the checked-off spec §6 definition of done.

- [ ] **Step 1: Run the composition**

```bash
env -C /home/buvy/code/NorseArchitecture/Bifrost dotnet run --project src/Orchestration.AppHost
```

Docker must be up (Postgres primary + replica containers). Note the dashboard URL from stdout.

- [ ] **Step 2: Verify the DoD line by line**

Against the Aspire dashboard (Playwright browser tools or manual — either is fine):

1. **Four resources present:** `migrations`, `web`, `worker`, `stories`; `migrations` runs to completion and exits 0; the other three stay running.
2. **Structured logs from all four** in the dashboard's Structured logs view — including `migrations` (its `Starting migration contributor …` lines) — proving OTLP log export; console output additionally visible per-resource (BCL console provider).
3. **Resource attributes:** pick any resource's telemetry detail — `service.name` matches the resource, `service.instance.id` present, `deployment.environment.name` shows `Development`. **If the dashboard renders the environment attribute oddly or not at all, record which key it wanted — this is the spec §3.3 semconv verification point** (report back; a key change is a one-line edit + spec note, inside this plan's scope).
4. **Metrics:** `worker` (or `web`) shows .NET runtime instruments (GC, thread pool, exception count) under Metrics.
5. **Traces:** `web` shows spans only if something emits them — with no ASP.NET instrumentation until Layer 2, an empty traces view is **correct**, not a failure. The wildcard subscription is already proven by unit test; do not chase spans here.
6. **OTLP-absence resilience:** stop the AppHost, then run one container bare — `env -C /home/buvy/code/NorseArchitecture/Bifrost dotnet run --project Yggdrasil/src/Hosting.Worker` with no `OTEL_EXPORTER_OTLP_ENDPOINT` set — console logs appear, no crash, clean Ctrl+C shutdown.

- [ ] **Step 3: Report**

Deliverable: the DoD checklist above with pass/fail per line plus the semconv finding from point 3, reported to Buvy. Midgard and Yggdrasil sit committed on their `feature/servicedefaults-layer0` branches; Bifröst and Glitnir sit staged. Ship ceremony (PRs, CI, tags, publish, realm-pin bumps) is Buvy's — offer nothing.

---

## Self-review notes (writing session, 2026-07-28; amendments applied same day)

- **Spec coverage:** §1.1 case-file mechanism→Task 0, §3.1→Task 1, §3.2/§2.6→Task 2 (the rail, shipped uncomposed per Amendment A), §3.3–§3.6→Task 3 (pure emission — the health chain is deliberately absent, with the negative test proving it), §3.7→Task 4, §2.6 dependency-graph claim→Task 5, §6 DoD lines 2–5→Tasks 6–8, §2.8 AppHost composition→Task 7. §2.7 (migrations exit code) requires no code — already how AppHost works. Health exposure/composition, gRPC health (Web.Server, Layer 2 per Amendment C), EF/ASP.NET/messaging instrumentation, collector artifacts: out of scope by spec §4, no task touches them.
- **Semconv key:** plan commits to `deployment.environment.name`; Task 8 point 3 is the spec-mandated verification with an in-plan correction path.
- **Type consistency:** `HostApplicationBuilderExtensions` / `AddServiceDefaults()` / `AddDefaultHealthChecks()` are the only produced symbols; the two methods never call each other — `AddServiceDefaults()` returns `builder` plain.
