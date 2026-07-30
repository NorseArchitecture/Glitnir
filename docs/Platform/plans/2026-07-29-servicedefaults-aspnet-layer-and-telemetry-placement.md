# ServiceDefaults ASP.NET Layer and Telemetry Placement — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (the default — not a recommendation among equals) or `superpowers:executing-plans` (the narrow separate-session fallback) to implement this plan task-by-task, paired with `superpowers:test-driven-development` on every task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `Norse.Infrastructure.ServiceDefaults.AspNet` — the ASP.NET observability and health root — and land the telemetry placement law's remaining consequences across Midgard, Himinbjörg, Yggdrasil, and Bifröst.

**Architecture:** Layer 0's `AddServiceDefaults()` grows two optional signal delegates so a higher layer can contribute to its single OpenTelemetry composition. `.AspNet` forwards ASP.NET Core instrumentation through them and composes the health rail, exposing one entry point per web-host shape. Probe endpoints follow the Kubernetes convention and diverge in behavior purely through the reference graph, with no configuration. gRPC health moves to the project that already owns the gRPC transport, and the Identity meter moves to the realm that brings ASP.NET Identity.

**Tech Stack:** .NET 11 preview 6 / C# 15 · OpenTelemetry 1.x · `OpenTelemetry.Instrumentation.AspNetCore` 1.17.0 · `Grpc.AspNetCore.HealthChecks` 2.x · xUnit v3 on Microsoft Testing Platform v2 · Shouldly · NSubstitute · `Microsoft.AspNetCore.TestHost`

**Spec:** `../specs/2026-07-28-servicedefaults-aspnet-layer-and-telemetry-placement-design.md`

## Global Constraints

- **Read `Glitnir/docs/house-rules.md` in full before writing any code.** Tabs; `extension(...)` blocks over static extension-method syntax; expression-bodied members with the arrow on the declaration line and the body indented below; target-typed `new()`; collection expressions; `is null` / `is not null`; fluent chains one call per line, dot-leading; `static` lambdas wherever nothing is captured.
- **Every class is `sealed`, `abstract`, or `static`.** Omit accessibility modifiers when adopting the language default.
- **XML docs on every publicly visible member in a `src/` project.** `<summary>` always; `<param>`/`<returns>` only where they say something the signature does not.
- **`Directory.Build.props`, `Directory.Build.targets`, and `.editorconfig` at every level are IMMUTABLE.** They are Ginnungagap-scattered. If a task appears to require editing one, **halt and ask** — do not edit, do not work around.
- **Test projects:** classes `public sealed`; test methods omit the accessibility modifier (bare `void` / bare `async Task`); names sentence-shaped with underscores. `Shouldly`, `NSubstitute`, `Xunit` usings are global via `tests/Directory.Build.props` — never re-add them per file. No `ConfigureAwait` in tests (xUnit1030).
- **`ConfigureAwait(false)` on every await in `src/`.** CA2007 is the enforcement arm.
- **Package versions tagged to the major:** `Version="1.*"`, `Version="2.*"`. Framework-tracking packages are `Version="11.*-*"`.
- **One `<PropertyGroup>` and one `<ItemGroup>` per csproj**, members sorted alphabetically inside each.
- **Leverage transitive dependencies.** Do not add a `<PackageReference>` for something already flowing transitively. Central Package Management is on in **Yggdrasil only** — its references carry no `Version` attribute.
- **Suppression Law.** IDE0005 is never suppressed — delete the using. If the same warning code fires repeatedly, **stop and report** rather than laying a trail of pragmas. Rule 4 governs which way a repeated hit resolves: never silence a warning whose root cause is fixable, so prefer fixing the code over hoisting a `<NoWarn>`.
- **`HttpClient` calls in tests use the `Uri` overload, never the `string` overload.** `AnalysisLevelUsage` is `latest-All` with warnings-as-errors, so `CA2234` makes `client.GetAsync("/path", …)` a build error. The form is an **explicit** `new Uri(...)`: `client.GetAsync(new Uri("/path", UriKind.Relative), TestContext.Current.CancellationToken)`. Target-typed `new(...)` does **not** work here — with both a `string` and a `Uri` overload in scope the compiler cannot infer a target type and emits CS0121, so this is one of the places the "target-typed `new()` whenever the language allows it" rule does not reach. Relative URIs resolve against the test client's `BaseAddress`.
- **Branches.** Midgard, Himinbjörg, and Yggdrasil each work on a local `feature/servicedefaults-aspnet` branch; commits on those branches are expected. **Bifröst NEVER branches** — Task 8 works on `master` and **stages only, never commits.**
- **Ship ceremony is explicitly out of scope.** No PRs, no tags, no `dotnet nuget push`, no pushing any branch. Those are the human's gates.
- **US English spelling** everywhere — code, comments, docs, commit messages.

---

## File Structure

**Midgard** (`Bifrost/Midgard/`)

| File | Responsibility |
|---|---|
| `src/Infrastructure.ServiceDefaults/HostApplicationBuilderExtensions.cs` | *Modify* — Layer 0 gains the two optional signal delegates |
| `src/Infrastructure.ServiceDefaults.AspNet/Infrastructure.ServiceDefaults.AspNet.csproj` | *Create* — the family's only `FrameworkReference` |
| `src/Infrastructure.ServiceDefaults.AspNet/AspNetTraceFilter.cs` | *Create* — the request-tracing predicate |
| `src/Infrastructure.ServiceDefaults.AspNet/HealthEndpoints.cs` | *Create* — the two probe paths |
| `src/Infrastructure.ServiceDefaults.AspNet/AspNetServiceDefaultsExtensions.cs` | *Create* — the two builder entry points |
| `src/Infrastructure.ServiceDefaults.AspNet/WebApplicationExtensions.cs` | *Create* — `MapDefaultEndpoints()` |
| `src/Infrastructure.Web.Server/Mediator/Grpc/ServiceCollectionExtensions.cs` | *Modify* — register gRPC health checks |
| `src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj` | *Modify* — add `Grpc.AspNetCore.HealthChecks` |
| `tests/Infrastructure.ServiceDefaults.Tests/HostApplicationBuilderExtensionsTests.cs` | *Modify* — delegate-forwarding tests |
| `tests/Infrastructure.ServiceDefaults.AspNet.Tests/*` | *Create* — the new layer's tests |
| `Midgard.slnx` | *Modify* — register the two new projects |

**Himinbjörg** (`Bifrost/Himinbjorg/`)

| File | Responsibility |
|---|---|
| `src/Identity.Web.Server/ServiceCollectionExtensions.cs` | *Modify* — subscribe the Identity meter |
| `src/Identity.Web.Server/Identity.Web.Server.csproj` | *Modify* — add `OpenTelemetry.Extensions.Hosting` |

**Yggdrasil** (`Bifrost/Yggdrasil/`)

| File | Responsibility |
|---|---|
| `src/Hosting.Web.Server/Program.cs` | *Modify* — application-host root, probes, gRPC health, asset suppression |
| `src/Hosting.Web.Server/Hosting.Web.Server.csproj` | *Modify* — swap the `NorseRef` to `.AspNet` |
| `src/Hosting.Stories.Server/Program.cs` | *Modify* — asset-host root, probes, asset suppression |
| `src/Hosting.Stories.Server/Hosting.Stories.Server.csproj` | *Modify* — swap the `NorseRef` to `.AspNet` |
| `Directory.Packages.props` | *Modify* — add the new package, bump `MidgardVersion` |

**Bifröst** (`Bifrost/`)

| File | Responsibility |
|---|---|
| `Bifrost.slnx` | *Modify* — add the missing Layer 0 entries **and** the new project's |

---

### Task 1: Layer 0 gains the two optional signal delegates

**Files:**
- Modify: `Midgard/src/Infrastructure.ServiceDefaults/HostApplicationBuilderExtensions.cs`
- Test: `Midgard/tests/Infrastructure.ServiceDefaults.Tests/HostApplicationBuilderExtensionsTests.cs`

**Interfaces:**
- Consumes: nothing — this is the first task.
- Produces: `IHostApplicationBuilder AddServiceDefaults(Action<TracerProviderBuilder>? configureTracing = null, Action<MeterProviderBuilder>? configureMetrics = null)` on `IHostApplicationBuilder`, in namespace `Norse.Infrastructure.ServiceDefaults`. Every later task in Midgard forwards through these two parameter names — they are keyword arguments at every call site.

**Context an implementer needs:** this signature change is **binary-breaking**. Optional parameters are baked at the call site, so the compiler emits `AddServiceDefaults(null, null)` and assemblies compiled against the shipped zero-parameter signature will throw `MissingMethodException` at runtime rather than fail to compile. Bifröst dev mode uses `ProjectReference` and recompiles, so nothing inside this plan trips on it; the consequence is carried by the spec's definition of done (package-mode tests, not a clean build).

- [ ] **Step 1: Create the branch**

```bash
git -C Midgard checkout -b feature/servicedefaults-aspnet
git -C Midgard branch --show-current
```

Expected: `feature/servicedefaults-aspnet`. If the repo is not on `master` beforehand, **halt and ask**.

- [ ] **Step 2: Write the failing tests**

Append to `Midgard/tests/Infrastructure.ServiceDefaults.Tests/HostApplicationBuilderExtensionsTests.cs`, inside the existing `HostApplicationBuilderExtensionsTests` class. The file already has the needed usings — do not add any.

```csharp
	[Fact]
	void A_forwarded_tracing_delegate_is_invoked_alongside_the_norse_wildcard()
	{
		List<Activity> exported = [];
		HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new());
		builder.AddServiceDefaults(configureTracing: static tracing => tracing.AddSource("Forwarded.Probe"));
		builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exported));
		using var host = builder.Build();
		var tracerProvider = host.Services.GetRequiredService<TracerProvider>();
		using ActivitySource
			norse = new("Norse.Test"),
			forwarded = new("Forwarded.Probe");
		norse.StartActivity("norse-op")?.Dispose();
		forwarded.StartActivity("forwarded-op")?.Dispose();
		tracerProvider.ForceFlush();
		exported.Select(a => a.OperationName).ShouldBe(["norse-op", "forwarded-op"], ignoreOrder: true);
	}

	[Fact]
	void A_forwarded_metrics_delegate_is_invoked_alongside_the_norse_wildcard()
	{
		List<Metric> exported = [];
		HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new());
		builder.AddServiceDefaults(configureMetrics: static metrics => metrics.AddMeter("Forwarded.Probe"));
		builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddInMemoryExporter(exported));
		using var host = builder.Build();
		var meterProvider = host.Services.GetRequiredService<MeterProvider>();
		using Meter
			norse = new("Norse.TestMeter"),
			forwarded = new("Forwarded.Probe");
		norse.CreateCounter<long>("norse_counter").Add(1);
		forwarded.CreateCounter<long>("forwarded_counter").Add(1);
		meterProvider.ForceFlush();
		string[] names = [.. exported.Select(static metric => metric.Name)];
		names.ShouldContain("norse_counter");
		names.ShouldContain("forwarded_counter");
	}
```

**The metrics test asserts a subset, and the tracing test asserts an exact set — the asymmetry is deliberate; do not "harmonize" them.** Layer 0 registers `AddRuntimeInstrumentation()`, so roughly eighteen `dotnet.*` runtime metrics reach any in-memory exporter attached to the metrics pipeline. An exact-set assertion on metric names therefore cannot pass, which is exactly why the pre-existing `Norse_meters_are_captured_by_the_wildcard_subscription` test uses `ShouldContain`. Tracing has no equivalent always-on instrumentation, so its exported activity set really is just the two the test emits, and the stronger exact-set assertion there is both valid and worth keeping — it proves nothing extra is traced.

The two pre-existing tests `Norse_activity_sources_are_captured_and_foreign_sources_are_not` and `Norse_meters_are_captured_by_the_wildcard_subscription` are the additive-only regression guard for a zero-argument call. **Do not modify them.**

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test Midgard/tests/Infrastructure.ServiceDefaults.Tests/Infrastructure.ServiceDefaults.Tests.csproj
```

Expected: compile failure — `CS1739` (no argument named `configureTracing`) or `CS1501` (no overload takes 1 argument).

- [ ] **Step 4: Add the delegates to Layer 0**

In `Midgard/src/Infrastructure.ServiceDefaults/HostApplicationBuilderExtensions.cs`, add one using line to the existing block, keeping alphabetical order — it belongs immediately after `using OpenTelemetry.Resources;`:

```csharp
using OpenTelemetry.Trace;
```

(`MeterProviderBuilder` already resolves via the existing `using OpenTelemetry.Metrics;`. If the build reports IDE0005 on the new line, that means `TracerProviderBuilder` was already reachable — delete the line, per the Suppression Law. Never suppress it.)

Replace the `AddServiceDefaults` method with:

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
		/// <param name="configureTracing">
		/// Optional additional tracing configuration, invoked inside this method's own
		/// <c>WithTracing</c> block after the <c>Norse.*</c> subscription. Additive only — nothing
		/// passed here can subtract emission. Used by a higher layer (for example the ASP.NET root) to
		/// contribute to this single OpenTelemetry composition instead of opening a second one.
		/// </param>
		/// <param name="configureMetrics">
		/// Optional additional metrics configuration, invoked inside this method's own
		/// <c>WithMetrics</c> block after the <c>Norse.*</c> subscription and runtime instrumentation.
		/// Additive only, on the same terms as <paramref name="configureTracing"/>.
		/// </param>
		/// <returns>The same <paramref name="builder"/> for chaining.</returns>
		public IHostApplicationBuilder AddServiceDefaults(
			Action<TracerProviderBuilder>? configureTracing = null,
			Action<MeterProviderBuilder>? configureMetrics = null)
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
				.WithTracing(tracing =>
				{
					tracing.AddSource("Norse.*");
					configureTracing?.Invoke(tracing);
				})
				.WithMetrics(metrics =>
				{
					metrics
						.AddMeter("Norse.*")
						.AddRuntimeInstrumentation();
					configureMetrics?.Invoke(metrics);
				});
			// The guard is ours, not the SDK's: UseOtlpExporter() with no endpoint configured defaults
			// to localhost:4317 and fails on every export attempt (spec §3.7). Behind this check,
			// absence is a genuine no-op and console still works.
			if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
			{
				builder.Services.AddOpenTelemetry().UseOtlpExporter();
			}
			return builder;
		}
```

Note the `static` modifiers are gone from the tracing and metrics lambdas — they now capture the parameters. The logging lambda keeps its `static`.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test Midgard/tests/Infrastructure.ServiceDefaults.Tests/Infrastructure.ServiceDefaults.Tests.csproj
```

Expected: PASS, 11 tests (9 pre-existing + 2 new). Zero warnings — warnings are errors here.

- [ ] **Step 6: Commit**

```bash
git -C Midgard add src/Infrastructure.ServiceDefaults/HostApplicationBuilderExtensions.cs tests/Infrastructure.ServiceDefaults.Tests/HostApplicationBuilderExtensionsTests.cs
git -C Midgard commit -m "$(cat <<'EOF'
Forward optional tracing and metrics delegates through AddServiceDefaults

Lets a higher layer contribute to Layer 0's single OpenTelemetry composition
instead of opening a second one, preserving the base's ability to append
registrations that must run after a caller's contributions. Additive only.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Scaffold `.AspNet` with the probe paths and trace filter

> **AMENDED 2026-07-30, after this task had already shipped as commit `87231ab`.** The original task built a *metadata-driven* trace filter: an endpoint stamped itself unobserved via `DisableNorseObservability()`, and `AspNetTraceFilter.Include` read that marker off `context.GetEndpoint()`. **That design cannot work.** `AspNetCoreTraceInstrumentationOptions.Filter` is invoked from the instrumentation's `OnStartActivity`, which handles the `Microsoft.AspNetCore.Hosting.HttpRequestIn.Start` DiagnosticSource event — *before* the routing middleware has run. `context.GetEndpoint()` is therefore always `null` at filter time, in production exactly as in tests, so the marker was never read and no span was ever suppressed. (Confirmed against `OpenTelemetry.Instrumentation.AspNetCore` 1.17.0: `OnStartActivity` carries both a `g__DisableActivity` local function — the `Filter` consumption point — and a `g__SetUrlPathAttribute` local function, so the URL path *is* available where endpoint metadata is not.) Discovered by the Task 3 implementer; ruled by the human on 2026-07-30.
>
> **The corrected design is path-driven,** matching proven prior art on another platform of ours that filtered by path prefix for precisely this reason. The marker type and the `DisableNorseObservability()` convention are **deleted, not repaired** — with the tracing half gone, the convention wrapped a single framework call and nothing else. Metrics suppression keeps working through the framework's own `DisableHttpMetrics()`, called directly: HTTP metrics are recorded at request *end*, after routing, so `IDisableHttpMetricsMetadata` is genuinely visible there. The asymmetry is real and is a property of the ASP.NET pipeline, not a design preference.
>
> **Apply as a follow-up commit on the same branch** — do not rewrite history. The steps below describe the corrected end state; reconcile the working tree to it.

**Files:**
- Create: `Midgard/src/Infrastructure.ServiceDefaults.AspNet/Infrastructure.ServiceDefaults.AspNet.csproj`
- Create: `Midgard/src/Infrastructure.ServiceDefaults.AspNet/HealthEndpoints.cs`
- Create: `Midgard/src/Infrastructure.ServiceDefaults.AspNet/AspNetTraceFilter.cs`
- **Delete** (shipped in `87231ab`, superseded by the amendment): `Midgard/src/Infrastructure.ServiceDefaults.AspNet/DisableNorseObservabilityMetadata.cs`
- **Delete** (shipped in `87231ab`, superseded by the amendment): `Midgard/src/Infrastructure.ServiceDefaults.AspNet/EndpointConventionBuilderExtensions.cs`
- Create: `Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests/Infrastructure.ServiceDefaults.AspNet.Tests.csproj`
- Create: `Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests/AssemblyInfo.cs`
- Create: `Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests/AspNetTraceFilterTests.cs`
- **Delete** (shipped in `87231ab`, superseded): `Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests/EndpointConventionBuilderExtensionsTests.cs`
- Modify: `Midgard/Midgard.slnx`

**Interfaces:**
- Consumes: Task 1's `AddServiceDefaults(configureTracing:, configureMetrics:)` — not called yet, but the project references it.
- Produces, all in namespace `Norse.Infrastructure.ServiceDefaults.AspNet`:
  - `HealthEndpoints.Liveness` = `"/livez"`, `HealthEndpoints.Readiness` = `"/readyz"` (both `public const string`)
  - `bool AspNetTraceFilter.Include(HttpContext)` — internal
- Removed from the public surface (was in `87231ab`): `DisableNorseObservability()`. Callers use the framework's `DisableHttpMetrics()` directly.

**Design note for the implementer:** the trace filter is **path-driven, and that is forced by the pipeline, not chosen.** See the amendment banner above for why endpoint metadata is unreachable at filter time. Consequences to hold onto:

- The filter must decide from `context.Request.Path` alone. Anything it reaches for that routing populates — the endpoint, its metadata, route values — is `null` or empty when the filter runs.
- The exclusion list is a route-table dependency, and that is the cost of this approach. It is kept small and anchored: two entries are the `HealthEndpoints` constants this project owns, so they cannot drift; the other three are structural prefixes (`/grpc.health.`, `/_`) and a file-extension test, none of which name a specific route.
- **`Path.HasExtension` is the catch-all for static assets and is deliberately blunt.** It scans backward from the end of the string and stops at the first `/`, so `/api/v1.0/users` is *not* treated as having an extension — but `/api/users.json` *is*, and would go untraced. Extension-based content negotiation is therefore incompatible with this filter. That is an accepted trade, recorded here so a future route author finds the reason rather than a mystery.

- [ ] **Step 1: Create the source project**

`Midgard/src/Infrastructure.ServiceDefaults.AspNet/Infrastructure.ServiceDefaults.AspNet.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<Description>Norse.Infrastructure.ServiceDefaults.AspNet: the ASP.NET Core observability root. Composes Norse.Infrastructure.ServiceDefaults, subscribes ASP.NET Core metrics and — for application hosts — request tracing, composes the health-check rail, and maps the /livez and /readyz probe endpoints. Carries the family's only FrameworkReference, so a worker or init container cannot reference it.</Description>
	</PropertyGroup>
	<ItemGroup>
		<FrameworkReference Include="Microsoft.AspNetCore.App" />
		<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.*" />
		<ProjectReference Include="../Infrastructure.ServiceDefaults/Infrastructure.ServiceDefaults.csproj" />
	</ItemGroup>
</Project>
```

`Microsoft.NET.Sdk` with an explicit `FrameworkReference`, deliberately **not** `Microsoft.NET.Sdk.Web` — this is a library, not a host, and the Web SDK's content globbing and publish behavior have no place here. (`Infrastructure.Web.Server` does use `Sdk.Web`; that is pre-existing and out of scope.)

- [ ] **Step 2: Create the probe path constants**

`HealthEndpoints.cs`:

```csharp
namespace Norse.Infrastructure.ServiceDefaults.AspNet;

/// <summary>
/// The probe endpoint paths, following the Kubernetes convention rather than the Aspire template's
/// <c>/health</c> and <c>/alive</c>. Kubernetes settled on <c>/livez</c> and <c>/readyz</c>, and
/// deprecated <c>/healthz</c> in v1.16 in favor of the two specific endpoints.
/// </summary>
public static class HealthEndpoints
{
	/// <summary>
	/// The liveness probe path — restart-me semantics. Runs only <c>live</c>-tagged checks, which is
	/// the trivial <c>self</c> check alone, so it performs no I/O and is safe to poll aggressively.
	/// </summary>
	public const string Liveness = "/livez";

	/// <summary>
	/// The readiness probe path — send-me-traffic semantics. Runs every registered check, including
	/// any database check a provider component registered on the host's behalf.
	/// </summary>
	public const string Readiness = "/readyz";
}
```

- [ ] **Step 3: Delete the superseded marker and convention**

```bash
git -C Midgard rm src/Infrastructure.ServiceDefaults.AspNet/DisableNorseObservabilityMetadata.cs \
                  src/Infrastructure.ServiceDefaults.AspNet/EndpointConventionBuilderExtensions.cs \
                  tests/Infrastructure.ServiceDefaults.AspNet.Tests/EndpointConventionBuilderExtensionsTests.cs
```

The assembly-reference guard that lived in `EndpointConventionBuilderExtensionsTests` is not lost — it is re-homed in `AspNetTraceFilterTests` in Step 6.

- [ ] **Step 4: Create the trace filter**

`AspNetTraceFilter.cs`:

```csharp
using Microsoft.AspNetCore.Http;

namespace Norse.Infrastructure.ServiceDefaults.AspNet;

/// <summary>
/// The default request-tracing predicate. Admits application traffic and rejects probe, framework,
/// and static-asset paths — traffic that is volume without signal.
/// </summary>
/// <remarks>
/// <para>
/// This predicate matches on the request path, and that is forced rather than chosen. OpenTelemetry
/// invokes <c>AspNetCoreTraceInstrumentationOptions.Filter</c> while handling the
/// <c>Microsoft.AspNetCore.Hosting.HttpRequestIn.Start</c> event, which fires before the routing
/// middleware. <see cref="EndpointHttpContextExtensions.GetEndpoint"/> returns <see langword="null"/>
/// at that point, so no endpoint-metadata convention can reach this decision — the request path is
/// what the pipeline has produced by then.
/// </para>
/// <para>
/// Metrics are the mirror image: they are recorded at request end, after routing, so the framework's
/// own <c>DisableHttpMetrics()</c> endpoint convention works there and is what hosts call. The two
/// signals are suppressed by two different mechanisms because they are decided at two different
/// points in the pipeline.
/// </para>
/// </remarks>
static class AspNetTraceFilter
{
	/// <summary>
	/// The gRPC health service's route prefix. Its full route is <c>/grpc.health.v1.Health/Check</c>,
	/// but the prefix also covers <c>Watch</c> and any future version of the service.
	/// </summary>
	const string GrpcHealthPrefix = "/grpc.health.";

	/// <summary>
	/// The framework-content prefix, covering Blazor's <c>/_framework</c> and <c>/_content</c> trees
	/// and the <c>/_blazor</c> circuit endpoint in one test.
	/// </summary>
	const string FrameworkPrefix = "/_";

	/// <summary>Returns <see langword="true"/> when the request should be traced.</summary>
	internal static bool Include(HttpContext context) =>
		context.Request.Path.Value is not string path || !IsExcluded(path);

	static bool IsExcluded(string path) =>
		// The probe paths are matched case-insensitively because ASP.NET route matching is, so a
		// probe sent to /LIVEZ reaches the endpoint and must be filtered the same way.
		path.StartsWith(HealthEndpoints.Liveness, StringComparison.OrdinalIgnoreCase) ||
		path.StartsWith(HealthEndpoints.Readiness, StringComparison.OrdinalIgnoreCase) ||
		// A gRPC route is a protobuf full name and is case-sensitive; so is the framework prefix.
		path.StartsWith(GrpcHealthPrefix, StringComparison.Ordinal) ||
		path.StartsWith(FrameworkPrefix, StringComparison.Ordinal) ||
		Path.HasExtension(path);
}
```

Three notes so this compiles and behaves first time. `System.IO` is an implicit using, so `Path` binds without a `using` directive — adding one is an IDE0005 error. `Path.HasExtension` scans backward and stops at the first `/`, which is why it does not mistake `/api/v1.0/users` for an extension. `context.Request.Path.Value` is `string?` and is `null` or empty for a request to the site root, which the `is not string path` test admits — the root is application traffic and must be traced.

- [ ] **Step 5: Create the test project**

`Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests/Infrastructure.ServiceDefaults.AspNet.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<PackageReference Include="Microsoft.AspNetCore.TestHost" Version="11.*-*" />
		<PackageReference Include="OpenTelemetry.Exporter.InMemory" Version="1.*" />
		<ProjectReference Include="../../src/Infrastructure.ServiceDefaults.AspNet/Infrastructure.ServiceDefaults.AspNet.csproj" />
	</ItemGroup>
</Project>
```

The `Microsoft.AspNetCore.App` `FrameworkReference` flows transitively through the `ProjectReference` — do not restate it.

- [ ] **Step 5a: Serialize the assembly's tests**

`AssemblyInfo.cs`:

```csharp
// Every test in this assembly builds a TracerProvider that subscribes ASP.NET Core instrumentation,
// which listens to a process-wide DiagnosticListener. Two providers alive at once both receive every
// host's requests, so one test class's in-memory exporter sees another's traffic and any
// ShouldBeEmpty() assertion goes non-deterministic. Observed as an intermittent 11/13 vs 12/13 while
// Task 3 was being written. The subject here is process-global diagnostic state; it cannot be tested
// in parallel. This is necessary but not sufficient — a span also does not end until its request's
// context is disposed, which the trace tests handle with their own drain; see DrainAsync there.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

- [ ] **Step 6: Write the failing tests**

`AspNetTraceFilterTests.cs`:

```csharp
using Microsoft.AspNetCore.Http;

namespace Norse.Infrastructure.ServiceDefaults.AspNet.Tests;

public sealed class AspNetTraceFilterTests
{
	static HttpContext Request(string path)
	{
		DefaultHttpContext context = new();
		context.Request.Path = path;
		return context;
	}

	[Theory]
	[InlineData("/livez")]
	[InlineData("/readyz")]
	[InlineData("/LIVEZ")]
	[InlineData("/grpc.health.v1.Health/Check")]
	[InlineData("/grpc.health.v1.Health/Watch")]
	[InlineData("/_framework/blazor.boot.json")]
	[InlineData("/_content/Norse.DesignSystem/tokens.css")]
	[InlineData("/_blazor")]
	[InlineData("/app.css")]
	[InlineData("/dotnet.runtime.js")]
	[InlineData("/_framework/dotnet.native.wasm")]
	void Volume_without_signal_is_not_traced(string path) =>
		AspNetTraceFilter.Include(Request(path)).ShouldBeFalse();

	[Theory]
	[InlineData("/")]
	[InlineData("")]
	[InlineData("/ping")]
	[InlineData("/api/policies")]
	[InlineData("/api/v1.0/policies")]
	[InlineData("/Account/Login")]
	[InlineData("/norse.identity.v1.Authentication/Login")]
	void Application_traffic_is_traced(string path) =>
		AspNetTraceFilter.Include(Request(path)).ShouldBeTrue();

	[Fact]
	void The_filter_decides_without_a_routed_endpoint()
	{
		// The regression guard for the defect this design replaces. OpenTelemetry invokes Filter
		// before the routing middleware runs, so the filter sees exactly this context shape: a
		// populated path and no endpoint. A filter that consults endpoint metadata reads null here
		// and silently admits everything.
		var context = Request(HealthEndpoints.Liveness);
		context.GetEndpoint().ShouldBeNull();
		AspNetTraceFilter.Include(context).ShouldBeFalse();
	}

	[Fact]
	void The_aspnet_layer_references_aspnetcore_and_the_base_layer_still_does_not()
	{
		typeof(AspNetTraceFilter).Assembly
			.GetReferencedAssemblies()
			.ShouldContain(a => a.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
		typeof(HostApplicationBuilderExtensions).Assembly
			.GetReferencedAssemblies()
			.ShouldAllBe(a => !a.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
	}
}
```

**These unit tests are necessary and not sufficient, and the plan says so on purpose.** The design they replace passed a unit test of exactly this shape while being completely broken in production, because the unit test hand-built the `HttpContext` the filter wanted instead of the one the pipeline delivers. `The_filter_decides_without_a_routed_endpoint` closes that specific hole by asserting the *absence* of an endpoint, but the real gate is Task 3's end-to-end tests, which drive requests through a live `TestServer` and read what the instrumentation actually exported. Neither layer is optional.

- [ ] **Step 7: Run the tests to verify they fail**

```bash
dotnet test Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests/Infrastructure.ServiceDefaults.AspNet.Tests.csproj
```

Expected: green. If any test fails, fix the source, not the test.

- [ ] **Step 8: Register both new projects in `Midgard.slnx`**

In `Midgard/Midgard.slnx`, add to the `/src/` folder immediately after the existing `Infrastructure.ServiceDefaults` line:

```xml
		<Project Path="src/Infrastructure.ServiceDefaults.AspNet/Infrastructure.ServiceDefaults.AspNet.csproj" />
```

and to the `/tests/` folder immediately after the existing `Infrastructure.ServiceDefaults.Tests` line:

```xml
		<Project Path="tests/Infrastructure.ServiceDefaults.AspNet.Tests/Infrastructure.ServiceDefaults.AspNet.Tests.csproj" />
```

**Under the amendment this step is a no-op** — `87231ab` already registered both projects. Confirm the two lines are present and move on.

- [ ] **Step 9: Verify the whole solution builds and all tests pass**

```bash
dotnet build Midgard/Midgard.slnx
dotnet test Midgard/Midgard.slnx
```

Expected: build succeeded, 0 warnings, 0 errors; every test green.

**If `IsAotCompatible` analyzers (IL2xxx / IL3xxx) fire on the new project:** that is the repeated-hit protocol. **Stop and report** the rule IDs and why they fire. Do not add `<NoWarn>`, do not add pragmas, and do not set `<IsAotCompatible>false</IsAotCompatible>` without asking — `src/Directory.Build.props` is immutable and a per-project override is a design decision, not a build fix.

- [ ] **Step 10: Commit**

Stage the six amended paths **explicitly** — a blanket `add -A` over the two directories would sweep Task 3's in-flight uncommitted files into this commit.

```bash
git -C Midgard add src/Infrastructure.ServiceDefaults.AspNet/AspNetTraceFilter.cs \
                   src/Infrastructure.ServiceDefaults.AspNet/DisableNorseObservabilityMetadata.cs \
                   src/Infrastructure.ServiceDefaults.AspNet/EndpointConventionBuilderExtensions.cs \
                   tests/Infrastructure.ServiceDefaults.AspNet.Tests/AspNetTraceFilterTests.cs \
                   tests/Infrastructure.ServiceDefaults.AspNet.Tests/AssemblyInfo.cs \
                   tests/Infrastructure.ServiceDefaults.AspNet.Tests/EndpointConventionBuilderExtensionsTests.cs
git -C Midgard commit -m "$(cat <<'EOF'
Make the ASP.NET trace filter path-driven

OpenTelemetry invokes the ASP.NET trace filter from OnStartActivity, which
handles HttpRequestIn.Start and therefore runs before the routing middleware.
GetEndpoint() is always null there, so the metadata-driven filter shipped in
87231ab never suppressed a span. Match on the request path instead, which is
what the pipeline has produced at that point, and delete the marker type and
the DisableNorseObservability() convention it fed: with the tracing half gone
the convention only wrapped DisableHttpMetrics(), which hosts now call direct.

Metrics keep working through endpoint metadata because they are recorded at
request end, after routing. Two mechanisms for two signals, because they are
decided at two different points in the pipeline.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: The two ASP.NET builder entry points

**Files:**
- Create: `Midgard/src/Infrastructure.ServiceDefaults.AspNet/AspNetServiceDefaultsExtensions.cs`
- Create: `Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests/AspNetServiceDefaultsExtensionsTests.cs`

**Interfaces:**
- Consumes: Task 1's `AddServiceDefaults(configureTracing:, configureMetrics:)`; Task 2's `AspNetTraceFilter.Include`.
- Produces, on `IHostApplicationBuilder` in namespace `Norse.Infrastructure.ServiceDefaults.AspNet`:
  - `IHostApplicationBuilder AddAspNetServiceDefaults()` — metrics + request tracing + health rail
  - `IHostApplicationBuilder AddAssetHostServiceDefaults()` — metrics + health rail, no request tracing

**Why two names instead of one method with a flag:** the difference between the two web hosts is permanent, not configurable. `AddAssetHostServiceDefaults` is named for *why* it is reduced, so a future web host answers one legible question — "am I an asset host?" — rather than decoding a boolean. `Asset` borrows the framework's own `MapStaticAssets` vocabulary.

**`An_application_host_does_not_trace_volume_without_signal` is the load-bearing test in this plan.** It is the only place the trace filter is exercised the way production exercises it: a real request through a real `TestServer`, filtered by the real instrumentation, asserting against what was really exported. Task 2's unit tests can pass against a filter that never runs — that is exactly how the metadata design got as far as a reviewed commit. If this theory is ever weakened to make an implementation pass, the plan has been defeated. Fix the filter instead.

- [ ] **Step 1: Write the failing tests**

`AspNetServiceDefaultsExtensionsTests.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace Norse.Infrastructure.ServiceDefaults.AspNet.Tests;

public sealed class AspNetServiceDefaultsExtensionsTests
{
	/// <summary>A route the filter always admits, used to prove the exporter was live.</summary>
	const string Sentinel = "/sentinel";

	static WebApplication BuildHost(
		bool applicationHost,
		List<Activity> exportedActivities,
		Action<IHostApplicationBuilder>? configure = null)
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.WebHost.UseTestServer();
		_ = applicationHost ?
			builder.AddAspNetServiceDefaults() :
			builder.AddAssetHostServiceDefaults();
		builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exportedActivities));
		configure?.Invoke(builder);
		return builder.Build();
	}

	// A request's span does not exist yet when GetAsync returns. TestServer hands the response body
	// to the client in CompleteResponseAsync and only later, in DisposeContext, does
	// HostingApplicationDiagnostics stop the Activity — which is when the export processor fires.
	// ForceFlush cannot flush a span that has not ended, so a bare assert-after-GetAsync is a race
	// in BOTH directions: ShouldNotBeEmpty can miss a span that is about to land, and ShouldBeEmpty
	// can pass vacuously against a filter that does nothing. Measured at ~18/20 arriving in time.
	static async Task DrainAsync(WebApplication app, List<Activity> exported, int count)
	{
		var tracer = app.Services.GetRequiredService<TracerProvider>();
		for (var attempt = 0; attempt < 100; attempt++)
		{
			tracer.ForceFlush();
			if (exported.Count >= count)
				return;
			await Task.Delay(10, TestContext.Current.CancellationToken);
		}
		exported.Count.ShouldBeGreaterThanOrEqualTo(count, "the expected span never arrived");
	}

	// The asset host registers no tracing instrumentation at all, so there is no span to wait for
	// and no sentinel is possible — emptiness can only be established by giving the pipeline the
	// full window a span would have needed and finding nothing in it.
	static async Task SettleAsync(WebApplication app, int milliseconds = 1_000)
	{
		var tracer = app.Services.GetRequiredService<TracerProvider>();
		await Task.Delay(milliseconds, TestContext.Current.CancellationToken);
		tracer.ForceFlush();
	}

	[Fact]
	async Task An_application_host_traces_ordinary_requests()
	{
		List<Activity> exported = [];
		await using var app = BuildHost(applicationHost: true, exported);
		app.MapGet("/ping", static () => Results.Ok());
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		_ = await client.GetAsync(new Uri("/ping", UriKind.Relative), TestContext.Current.CancellationToken);
		await DrainAsync(app, exported, 1);
		exported.ShouldNotBeEmpty();
	}

	[Fact]
	async Task An_asset_host_traces_nothing()
	{
		List<Activity> exported = [];
		await using var app = BuildHost(applicationHost: false, exported);
		app.MapGet("/ping", static () => Results.Ok());
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		_ = await client.GetAsync(new Uri("/ping", UriKind.Relative), TestContext.Current.CancellationToken);
		await SettleAsync(app);
		exported.ShouldBeEmpty();
	}

	[Theory]
	[InlineData("/livez")]
	[InlineData("/grpc.health.v1.Health/Check")]
	[InlineData("/_blazor")]
	[InlineData("/app.css")]
	async Task An_application_host_does_not_trace_volume_without_signal(string path)
	{
		List<Activity> exported = [];
		await using var app = BuildHost(applicationHost: true, exported);
		app.MapGet(path, static () => Results.Ok());
		app.MapGet(Sentinel, static () => Results.Ok());
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		_ = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
		_ = await client.GetAsync(new Uri(Sentinel, UriKind.Relative), TestContext.Current.CancellationToken);
		// Waiting for the sentinel's span is what makes the emptiness claim mean something: it proves
		// the exporter was live and delivering during the window in which the excluded request's span
		// would have arrived. The excluded path contributed nothing, so the sentinel stands alone.
		await DrainAsync(app, exported, 1);
		exported.ShouldHaveSingleItem().DisplayName.ShouldContain(Sentinel);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	void Both_roots_compose_the_health_rail_with_the_self_liveness_check(bool applicationHost)
	{
		List<Activity> exported = [];
		using var app = BuildHost(applicationHost, exported);
		var registration = app.Services
			.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
			.Value.Registrations.ShouldHaveSingleItem();
		registration.Name.ShouldBe("self");
		registration.Tags.ShouldContain("live");
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	void Both_roots_stamp_the_resource_the_same_way_the_base_layer_does(bool applicationHost)
	{
		List<Activity> exported = [];
		using var app = BuildHost(applicationHost, exported);
		var attributes = app.Services
			.GetRequiredService<TracerProvider>()
			.GetResource().Attributes
			.ToDictionary(a => a.Key, a => a.Value);
		attributes.ShouldContainKey("service.name");
		attributes.ShouldContainKey("service.instance.id");
		attributes.ShouldContainKey("deployment.environment.name");
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	void Composing_the_base_layer_does_not_double_register_the_console_provider(bool applicationHost)
	{
		List<Activity> exported = [];
		using var app = BuildHost(applicationHost, exported);
		app.Services
			.GetServices<ILoggerProvider>()
			.Count(static provider => provider is ConsoleLoggerProvider)
			.ShouldBe(1);
	}
}
```

The last theory is the spec §8 assertion that composing Layer 0 rather than calling it twice leaves no duplicate registrations. It needs two more usings in the file's block, in alphabetical position:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests/Infrastructure.ServiceDefaults.AspNet.Tests.csproj
```

Expected: compile failure — `AddAspNetServiceDefaults` and `AddAssetHostServiceDefaults` do not exist.

- [ ] **Step 3: Implement the two entry points**

`AspNetServiceDefaultsExtensions.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Norse.Infrastructure.ServiceDefaults.AspNet;

/// <summary>
/// The ASP.NET observability root. Each ASP.NET host calls exactly one of these — they compose
/// <c>AddServiceDefaults()</c> rather than sitting beside it, so no host calls two roots.
/// </summary>
public static class AspNetServiceDefaultsExtensions
{
	/// <param name="builder">The host application builder.</param>
	extension(IHostApplicationBuilder builder)
	{
		/// <summary>
		/// The full ASP.NET root, and the one call an application host makes: the shared observability
		/// root, ASP.NET Core metrics, request tracing filtered to observed endpoints, and the health
		/// rail with its <c>self</c> liveness check.
		/// </summary>
		/// <returns>The same <paramref name="builder"/> for chaining.</returns>
		public IHostApplicationBuilder AddAspNetServiceDefaults() =>
			builder
				.AddServiceDefaults(
					configureTracing: static tracing => tracing.AddAspNetCoreInstrumentation(
						static options => options.Filter = AspNetTraceFilter.Include),
					configureMetrics: static metrics => metrics.AddAspNetCoreInstrumentation())
				.AddDefaultHealthChecks();

		/// <summary>
		/// The root for a host that serves static content only — identical to
		/// <see cref="AddAspNetServiceDefaults"/> minus request tracing. An asset host has no database,
		/// no transport, and no downstream, so its spans would be asset fetches with nothing to
		/// correlate against; its traffic and usage signal comes from metrics instead.
		/// </summary>
		/// <returns>The same <paramref name="builder"/> for chaining.</returns>
		public IHostApplicationBuilder AddAssetHostServiceDefaults() =>
			builder
				.AddServiceDefaults(configureMetrics: static metrics => metrics.AddAspNetCoreInstrumentation())
				.AddDefaultHealthChecks();
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests/Infrastructure.ServiceDefaults.AspNet.Tests.csproj
```

Expected: PASS. This task adds 12 test cases (2 facts + the 4-case volume theory + 3 theories × 2 inline data) on top of Task 2's 20 (an 11-case theory, a 7-case theory, and 2 facts) — 32 in the project. Confirm all green and zero warnings.

- [ ] **Step 5: Commit**

```bash
git -C Midgard add src/Infrastructure.ServiceDefaults.AspNet/AspNetServiceDefaultsExtensions.cs tests/Infrastructure.ServiceDefaults.AspNet.Tests/AspNetServiceDefaultsExtensionsTests.cs
git -C Midgard commit -m "$(cat <<'EOF'
Add the application-host and asset-host ASP.NET roots

Each ASP.NET host calls exactly one, and both compose AddServiceDefaults()
through its forwarding delegates. The two differ by one argument: an asset host
gets no request tracing.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: `MapDefaultEndpoints()` — `/livez` and `/readyz`

**Files:**
- Create: `Midgard/src/Infrastructure.ServiceDefaults.AspNet/WebApplicationExtensions.cs`
- Create: `Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests/WebApplicationExtensionsTests.cs`

**Interfaces:**
- Consumes: Task 2's `HealthEndpoints.Liveness` / `.Readiness`; Task 3's two roots. Traces are suppressed by `AspNetTraceFilter`, which already knows both constants; metrics are suppressed here, per endpoint, with the framework's `DisableHttpMetrics()`.
- Produces: `WebApplication MapDefaultEndpoints(this WebApplication)` in namespace `Norse.Infrastructure.ServiceDefaults.AspNet`.

**Design note:** both hosts map identical endpoints; the divergence in what "ready" means is produced entirely by which checks the reference graph put in the container. Stories.Server registered nothing beyond `self`, so its readiness is self-only — truthful for a static file server. Web.Server has `self` plus whatever a provider component's `Enrich` registered. Nobody configures that difference.

- [ ] **Step 1: Write the failing tests**

`WebApplicationExtensionsTests.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Norse.Infrastructure.ServiceDefaults.AspNet.Tests;

public sealed class WebApplicationExtensionsTests
{
	static WebApplication BuildProbeHost(Action<IHostApplicationBuilder>? configure = null)
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.WebHost.UseTestServer();
		builder.AddAspNetServiceDefaults();
		configure?.Invoke(builder);
		var app = builder.Build();
		app.MapDefaultEndpoints();
		return app;
	}

	[Fact]
	async Task Liveness_and_readiness_both_report_healthy_when_only_the_self_check_is_registered()
	{
		await using var app = BuildProbeHost();
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		(await client.GetAsync(new Uri(HealthEndpoints.Liveness, UriKind.Relative), TestContext.Current.CancellationToken))
			.StatusCode.ShouldBe(HttpStatusCode.OK);
		(await client.GetAsync(new Uri(HealthEndpoints.Readiness, UriKind.Relative), TestContext.Current.CancellationToken))
			.StatusCode.ShouldBe(HttpStatusCode.OK);
	}

	[Fact]
	async Task An_untagged_failing_check_fails_readiness_and_leaves_liveness_healthy()
	{
		await using var app = BuildProbeHost(static builder => builder.Services
			.AddHealthChecks()
			.AddCheck("database", static () => HealthCheckResult.Unhealthy()));
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		(await client.GetAsync(new Uri(HealthEndpoints.Liveness, UriKind.Relative), TestContext.Current.CancellationToken))
			.StatusCode.ShouldBe(HttpStatusCode.OK);
		(await client.GetAsync(new Uri(HealthEndpoints.Readiness, UriKind.Relative), TestContext.Current.CancellationToken))
			.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
	}

	[Fact]
	async Task The_probe_response_discloses_no_check_names_or_timings()
	{
		await using var app = BuildProbeHost(static builder => builder.Services
			.AddHealthChecks()
			.AddCheck("database", static () => HealthCheckResult.Healthy()));
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		var body = await (await client.GetAsync(new Uri(HealthEndpoints.Readiness, UriKind.Relative), TestContext.Current.CancellationToken))
			.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		body.ShouldBe("Healthy");
		body.ShouldNotContain("database");
	}

	[Fact]
	void Both_probe_endpoints_are_anonymous_and_carry_no_http_metrics()
	{
		using var app = BuildProbeHost();
		Endpoint[] probes = [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints)];
		probes.Length.ShouldBe(2);
		probes.ShouldAllBe(e => e.Metadata.GetMetadata<IAllowAnonymous>() != null);
		probes.ShouldAllBe(e => e.Metadata.GetMetadata<IDisableHttpMetricsMetadata>() != null);
	}

	[Theory]
	[InlineData(HealthEndpoints.Liveness)]
	[InlineData(HealthEndpoints.Readiness)]
	async Task Probe_traffic_produces_no_spans(string path)
	{
		const string Sentinel = "/sentinel";
		List<Activity> exported = [];
		var builder = WebApplication.CreateSlimBuilder();
		builder.WebHost.UseTestServer();
		builder.AddAspNetServiceDefaults();
		builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exported));
		await using var app = builder.Build();
		app.MapDefaultEndpoints();
		app.MapGet(Sentinel, static () => Results.Ok());
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		_ = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
		_ = await client.GetAsync(new Uri(Sentinel, UriKind.Relative), TestContext.Current.CancellationToken);
		var tracer = app.Services.GetRequiredService<TracerProvider>();
		for (var attempt = 0; attempt < 100 && exported.Count == 0; attempt++)
		{
			tracer.ForceFlush();
			if (exported.Count == 0)
				await Task.Delay(10, TestContext.Current.CancellationToken);
		}
		exported.ShouldHaveSingleItem().DisplayName.ShouldContain(Sentinel);
	}
}
```

`Probe_traffic_produces_no_spans` needs `using System.Diagnostics;` and `using OpenTelemetry.Trace;` on top of the list above (**not** `using OpenTelemetry;` — the tracing `AddInMemoryExporter(ICollection<Activity>)` overload lives in `OpenTelemetry.Trace`, so adding it is an IDE0005 error). It closes the loop the other direction from Task 3: Task 3 proves the filter rejects these paths when a test maps them by hand, and this proves the paths `MapDefaultEndpoints()` actually maps are the same ones the filter knows about — which is why the inline data is the constants themselves rather than string literals. A rename of either constant that missed the other fails here.

It carries the same sentinel-and-drain shape as Task 3's volume theory, for the same reason: a span does not exist until the request's context is disposed, so asserting emptiness the instant `GetAsync` returns would pass against a filter that does nothing at all. Waiting for the sentinel's span proves the exporter was live and delivering across the window the probe's span would have landed in. **Do not simplify either one back to a bare `ForceFlush()` + `ShouldBeEmpty()`.**

**Endpoint introspection note, learned the hard way in Task 1.** These tests read `((IEndpointRouteBuilder)app).DataSources` — the application's own route data sources — rather than resolving `EndpointDataSource` from DI, which is not reliably registered. It also means the assertions see exactly what this test mapped and nothing the framework added elsewhere, which is what makes `ShouldHaveSingleItem()` and `Length.ShouldBe(2)` safe here. The same caution that made the Task 1 metrics assertion a subset applies to every count-based assertion in this plan: before asserting an exact count, confirm nothing always-on contributes to the same collection.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests/Infrastructure.ServiceDefaults.AspNet.Tests.csproj
```

Expected: compile failure — `MapDefaultEndpoints` does not exist.

- [ ] **Step 3: Implement `MapDefaultEndpoints()`**

`WebApplicationExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Builder;

namespace Norse.Infrastructure.ServiceDefaults.AspNet;

/// <summary>
/// Probe endpoint mapping for ASP.NET hosts. Keeps the Aspire-conventional method name — it is the
/// paths that follow the Kubernetes standard, not the method that maps them.
/// </summary>
public static class WebApplicationExtensions
{
	/// <param name="app">The web application.</param>
	extension(WebApplication app)
	{
		/// <summary>
		/// Maps <see cref="HealthEndpoints.Liveness"/> (only <c>live</c>-tagged checks) and
		/// <see cref="HealthEndpoints.Readiness"/> (every registered check). Both are mapped in every
		/// environment, because an orchestrator's probes are required in production or the container
		/// never passes its gates; both are anonymous, because a probe arrives with no credentials;
		/// both are excluded from HTTP metrics here, and from tracing by the default trace filter,
		/// which knows these two paths. The default plain-text response writer is deliberate — no
		/// check name, dependency topology, or timing is disclosed.
		/// </summary>
		/// <returns>The same <paramref name="app"/> for chaining.</returns>
		public WebApplication MapDefaultEndpoints()
		{
			app.MapHealthChecks(HealthEndpoints.Liveness, new()
			{
				Predicate = static registration => registration.Tags.Contains("live"),
			})
				.AllowAnonymous()
				.DisableHttpMetrics();
			app.MapHealthChecks(HealthEndpoints.Readiness)
				.AllowAnonymous()
				.DisableHttpMetrics();
			return app;
		}
	}
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests/Infrastructure.ServiceDefaults.AspNet.Tests.csproj
```

Expected: PASS, every test green, zero warnings.

- [ ] **Step 5: Commit**

```bash
git -C Midgard add src/Infrastructure.ServiceDefaults.AspNet/WebApplicationExtensions.cs tests/Infrastructure.ServiceDefaults.AspNet.Tests/WebApplicationExtensionsTests.cs
git -C Midgard commit -m "$(cat <<'EOF'
Map the /livez and /readyz probe endpoints

Kubernetes-convention paths, anonymous, unobserved, mapped in every environment
with the plain-text writer so no check names or timings leak. Liveness filters
the live tag; readiness runs everything the reference graph registered.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: gRPC health checks in `Infrastructure.Web.Server`

**Files:**
- Modify: `Midgard/src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj`
- Modify: `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ServiceCollectionExtensions.cs`
- Test: `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/GrpcHealthCheckRegistrationTests.cs` (create)

**Interfaces:**
- Consumes: nothing from earlier tasks. `AddNorseCodeFirstGrpc()` already exists and returns `IServiceCollection`.
- Produces: `AddNorseCodeFirstGrpc()` additionally registers the gRPC health-check services. Yggdrasil calls `app.MapGrpcHealthChecksService()` (the ecosystem name, kept not fought) in Task 7.

**Placement rationale:** the project that brings the gRPC transport owns gRPC health. This makes the Stories/Web divergence structural — `Hosting.Stories.Server` does not reference `Infrastructure.Web.Server`, so it cannot acquire gRPC health at all. Legal under the attack-surface law: that law bans listeners in headless containers, not health protocols in a container already hosting Kestrel and gRPC.

**The publisher model, and why it is accepted.** `AddGrpcHealthChecks()` bridges the health rail to `grpc.health.v1.Health` via an `IHealthCheckPublisher`, not on demand. Publishers run on a timer (`HealthCheckPublisherOptions.Period`, 30 seconds by default) and execute every registered check regardless of whether any client ever calls `Check` — so the database check runs periodically for the life of the process, which the on-demand REST endpoints do not do.

This is accepted rather than mitigated, on the strength of prior art: an earlier production system of ours wired this exact package at this exact version with a real database check and **no publisher tuning at all** — no `Period` override, no `Predicate` narrowing. The default has been lived with in production. The cost is bounded and known, and `/readyz` needs the database probed anyway.

**One improvement over that prior art, and it matters.** There, the same database check was registered **twice** — once on `AddHealthChecks()` and once on `AddGrpcHealthChecks()`, under two different names to dodge the duplicate-registration-name error — so the publisher's timer evaluated it twice per tick. That is not necessary. Both builders funnel into the same `HealthCheckServiceOptions.Registrations`, and `GrpcHealthChecksOptions.Services` is the seam for deciding which registrations feed which gRPC service name (all of them, by default). **Register checks exactly once** — Layer 0's `self` plus whatever a provider component's `Enrich` added — and let both the REST endpoints and the gRPC service read that one rail. Do not add a gRPC-specific check registration.

- [ ] **Step 1: Confirm no duplicate registration**

There is no separate gRPC check set to populate. Confirm the branch is current and proceed — `AddNorseCodeFirstGrpc()` registers the *service*, never a check.

```bash
git -C Midgard branch --show-current
```

Expected: `feature/servicedefaults-aspnet`.

- [ ] **Step 2: Write the failing test**

Create `Midgard/tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/GrpcHealthCheckRegistrationTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class GrpcHealthCheckRegistrationTests
{
	[Fact]
	void Code_first_grpc_registration_bridges_health_results_to_grpc()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddNorseCodeFirstGrpc();
		using var provider = services.BuildServiceProvider();
		provider.GetServices<IHealthCheckPublisher>().ShouldNotBeEmpty();
	}

	[Fact]
	void Code_first_grpc_registration_adds_no_health_check_of_its_own()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddNorseCodeFirstGrpc();
		using var provider = services.BuildServiceProvider();
		provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
			.Value.Registrations.ShouldBeEmpty();
	}
}
```

**Why these two assertions and not a check on the gRPC service type.** The first proves the bridge exists at all: `AddGrpcHealthChecks()`'s entire mechanism is an `IHealthCheckPublisher` that pushes results into the gRPC health service, so no publisher means no bridge. The second is the one that guards the design decision — it asserts `AddNorseCodeFirstGrpc()` contributes **zero** check registrations, which is precisely the divergence from the prior art described above (that system registered its database check twice, once per builder, and paid for it on every publisher tick). Asserting on the generated `grpc.health.v1.Health` service base type instead would couple the test to `Grpc.AspNetCore.HealthChecks`'s internal DI shape while testing something less important.

- [ ] **Step 3: Run the test to verify it fails**

```bash
dotnet test Midgard/tests/Infrastructure.Web.Server.Tests/Infrastructure.Web.Server.Tests.csproj --filter-query "/*/*/GrpcHealthCheckRegistrationTests/*"
```

Expected: FAIL — `IHealthCheckPublisher` has no registrations, because nothing has called `AddGrpcHealthChecks()` yet. (The second test passes from the start; it is a guard against a later regression, not a red-to-green driver.)

- [ ] **Step 4: Add the package**

In `Midgard/src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj`, add to the single `<ItemGroup>`, in alphabetical position immediately before `Grpc.AspNetCore.Web`:

```xml
		<PackageReference Include="Grpc.AspNetCore.HealthChecks" Version="2.*" />
```

- [ ] **Step 5: Register the health service**

In `Midgard/src/Infrastructure.Web.Server/Mediator/Grpc/ServiceCollectionExtensions.cs`, replace the body of `AddNorseCodeFirstGrpc()` and extend its doc comment:

```csharp
		/// <summary>
		/// Wires protobuf-net.Grpc code-first hosting with the platform interceptor stack (spec §2.1):
		/// UnhandledExceptionInterceptor outermost (the net), PrincipalSeedingInterceptor (channel adapter),
		/// OutcomeServerInterceptor innermost (the DU's idiom translator — Failed → throw + ErrorInfo).
		/// Also registers the standard <c>grpc.health.v1.Health</c> service against the host's health
		/// rail: this project brings the gRPC transport, so it owns gRPC health. A host that does not
		/// reference this project — Stories.Server — cannot acquire it, which is the point.
		/// </summary>
		public IServiceCollection AddNorseCodeFirstGrpc()
		{
			services.AddCodeFirstGrpc(options =>
			{
				options.Interceptors.Add<UnhandledExceptionInterceptor>();
				options.Interceptors.Add<PrincipalSeedingInterceptor>();
				options.Interceptors.Add<OutcomeServerInterceptor>();
			});
			services.AddGrpcHealthChecks();
			return services;
		}
```

`AddCodeFirstGrpc` returns `IGrpcServerBuilder` and `AddGrpcHealthChecks` returns `IHealthChecksBuilder`, so neither can chain into the other — two statements is correct here, not a fluent-chain violation.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test Midgard/tests/Infrastructure.Web.Server.Tests/Infrastructure.Web.Server.Tests.csproj
```

Expected: PASS, including every pre-existing test in the project.

- [ ] **Step 7: Commit**

```bash
git -C Midgard add src/Infrastructure.Web.Server/Infrastructure.Web.Server.csproj src/Infrastructure.Web.Server/Mediator/Grpc/ServiceCollectionExtensions.cs tests/Infrastructure.Web.Server.Tests/Mediator/Grpc/GrpcHealthCheckRegistrationTests.cs
git -C Midgard commit -m "$(cat <<'EOF'
Own gRPC health in the project that owns the gRPC transport

Registers grpc.health.v1.Health against the health rail from
AddNorseCodeFirstGrpc(), so a host that does not reference this project cannot
acquire gRPC health.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Himinbjörg subscribes the ASP.NET Identity meter

**Files:**
- Modify: `Himinbjorg/src/Identity.Web.Server/Identity.Web.Server.csproj`
- Modify: `Himinbjorg/src/Identity.Web.Server/ServiceCollectionExtensions.cs`
- Modify: `Himinbjorg/tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj`
- Test: `Himinbjorg/tests/Identity.Web.Server.Tests/ServiceCollectionExtensionsTests.cs` — **this file already exists** with one test (`AddNorseAuthenticationService_registers_NorseSignInManager_as_SignInManager`). Append to the existing class; do not create a new file and do not modify the existing test.

**Interfaces:**
- Consumes: nothing from earlier tasks — independent of Midgard.
- Produces: `AddNorseAuthenticationService(string connectionString)` additionally subscribes the `Microsoft.AspNetCore.Identity` meter.

**Placement rationale:** `Identity.Web.Server` is the only project on the platform referencing `Microsoft.AspNetCore.Identity.EntityFrameworkCore` or `OpenIddict.EntityFrameworkCore`. Under the placement law, the realm that brings the dependency declares its telemetry — and because Web.Server is Himinbjörg's only consumer, the meter lands in exactly the one container the terminal-layer rule names, without anyone enforcing it.

The meter's individual instrument names were not recoverable by assembly inspection; only the meter name is asserted, which is what the subscription needs.

- [ ] **Step 1: Create the branch**

```bash
git -C Himinbjorg checkout -b feature/servicedefaults-aspnet
git -C Himinbjorg branch --show-current
```

- [ ] **Step 2: Add both packages**

The test asserts through `services.AddOpenTelemetry()`, whose extension lives in `OpenTelemetry.Extensions.Hosting` — so the src package has to be present for the test to compile at all. Adding it here buys a genuine behavioral red in Step 4 (an empty exporter) instead of a compile error.

In `Himinbjorg/src/Identity.Web.Server/Identity.Web.Server.csproj`, add to the single `<ItemGroup>` in alphabetical position among the `PackageReference` entries:

```xml
		<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.*" />
```

This is a genuine new direct dependency — `Identity.Web.Server` references the neutral `Persistence.EntityFramework`, not a provider binding, so no OpenTelemetry package flows transitively today.

In `Himinbjorg/tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj`, add to the single `<ItemGroup>` in alphabetical position after the two `Microsoft.*` entries:

```xml
		<PackageReference Include="OpenTelemetry.Exporter.InMemory" Version="1.*" />
```

- [ ] **Step 3: Write the failing test**

Append this test to the existing `ServiceCollectionExtensionsTests` class in `Himinbjorg/tests/Identity.Web.Server.Tests/ServiceCollectionExtensionsTests.cs`, and add three usings to that file's existing block in alphabetical position (`Microsoft.AspNetCore.Identity` and `Microsoft.Extensions.DependencyInjection` are already there):

```csharp
using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;
```

```csharp
	[Fact]
	void AddNorseAuthenticationService_subscribes_the_aspnet_identity_meter()
	{
		List<Metric> exported = [];
		ServiceCollection services = new();
		services.AddLogging();
		services.AddNorseAuthenticationService("Host=localhost;Database=norse_identity_test");
		services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddInMemoryExporter(exported));
		using var provider = services.BuildServiceProvider();
		var meterProvider = provider.GetRequiredService<MeterProvider>();
		using Meter meter = new("Microsoft.AspNetCore.Identity");
		meter.CreateCounter<long>("identity_probe").Add(1);
		meterProvider.ForceFlush();
		exported.ShouldContain(m => m.Name == "identity_probe");
	}
```

This asserts observed behavior — a meter under the subscribed name reaches an exporter — rather than the registration mechanism, so it does not depend on any OpenTelemetry internal type being visible. The connection string matches the style the existing test in this class already uses; no database is contacted, because `AddDbContext` does not connect at registration time.

Two deliberate differences from the existing test in this class: `services.AddLogging()` is called (OpenTelemetry's provider construction reaches for `ILoggerFactory`, and the same line appears in the equivalent Task 5 test), and the provider is actually built rather than merely inspected for descriptors. If `BuildServiceProvider()` throws on a dependency this registration path does not supply, **report it rather than papering over it by registering extra services** — that would mean `AddNorseAuthenticationService` has a latent composition gap worth knowing about.

Also add `using Microsoft.Extensions.Logging;` to the file's using block if it is not already present.

- [ ] **Step 4: Run the test to verify it fails**

```bash
dotnet test Himinbjorg/tests/Identity.Web.Server.Tests/Identity.Web.Server.Tests.csproj
```

Expected: FAIL on the new test only — `exported` contains no `identity_probe` metric, because nothing subscribed the meter. Every pre-existing test in the project stays green. If the failure is a compile error instead, Step 2's src package reference did not land.

- [ ] **Step 5: Subscribe the meter**

In `Himinbjorg/src/Identity.Web.Server/ServiceCollectionExtensions.cs`, add one using in alphabetical position (after the existing `Norse.*` entries):

```csharp
using OpenTelemetry.Metrics;
```

(**Corrected 2026-07-30 during execution — this file needs no new using at all.** The original text here claimed `WithMetrics` and `AddMeter` require `OpenTelemetry.Metrics`; both claims are wrong. `AddMeter` is an abstract *instance* method on `MeterProviderBuilder` in `OpenTelemetry.Api`, not an extension, and `WithMetrics` lives in `Microsoft.Extensions.DependencyInjection`, already reachable through the Web SDK's implicit usings. Adding `using OpenTelemetry.Metrics;` here is an IDE0005 error. `using Microsoft.Extensions.Logging;` is likewise unnecessary — `AddLogging()` is `LoggingServiceCollectionExtensions`, also in `Microsoft.Extensions.DependencyInjection`.)

Then, inside `AddNorseAuthenticationService`, immediately before `return services;`:

```csharp
			// The realm that brings the dependency declares its telemetry: this project is the only
			// one on the platform referencing ASP.NET Core Identity, and its only consumer is
			// Web.Server — so the meter lands in exactly the container that should have it, with no
			// rule for anyone to remember.
			services.AddOpenTelemetry()
				.WithMetrics(static metrics => metrics.AddMeter("Microsoft.AspNetCore.Identity"));
```

Extend the method's `<summary>` with a final sentence:

```
			/// Also subscribes the <c>Microsoft.AspNetCore.Identity</c> meter — ASP.NET Core Identity
			/// ships its own metrics, and Layer 0's <c>Norse.*</c> wildcard does not reach them.
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test Himinbjorg/Himinbjorg.slnx
```

Expected: PASS, all tests, zero warnings.

- [ ] **Step 7: Commit**

```bash
git -C Himinbjorg add src/Identity.Web.Server tests/Identity.Web.Server.Tests
git -C Himinbjorg commit -m "$(cat <<'EOF'
Subscribe the ASP.NET Identity meter where the dependency is brought

ASP.NET Core Identity ships its own meter and Layer 0's Norse.* wildcard does
not reach it. This project is the platform's only consumer of ASP.NET Identity,
so the subscription lands in exactly the one container that should have it.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Yggdrasil host adoption

**Files:**
- Modify: `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`
- Modify: `Yggdrasil/src/Hosting.Web.Server/Program.cs`
- Modify: `Yggdrasil/src/Hosting.Stories.Server/Hosting.Stories.Server.csproj`
- Modify: `Yggdrasil/src/Hosting.Stories.Server/Program.cs`
- Modify: `Yggdrasil/Directory.Packages.props`

**Interfaces:**
- Consumes: `AddAspNetServiceDefaults()`, `AddAssetHostServiceDefaults()`, `MapDefaultEndpoints()` (Tasks 2–4); the framework's `DisableHttpMetrics()`; `MapGrpcHealthChecksService()` from `Grpc.AspNetCore.HealthChecks`, whose registration Task 5 added.
- Produces: the final host shapes. Nothing consumes these.

**Note on reference swaps:** `.AspNet` project-references `Infrastructure.ServiceDefaults`, so a web host needs only the `.AspNet` `NorseRef`. **Remove** the plain `Infrastructure.ServiceDefaults` `NorseRef` from both web hosts. `Hosting.Worker` and `Hosting.Migrations.Service` keep theirs untouched — they must never acquire the `FrameworkReference`.

- [ ] **Step 1: Create the branch**

```bash
git -C Yggdrasil checkout -b feature/servicedefaults-aspnet
git -C Yggdrasil branch --show-current
```

The `appsettings.json` added to `Hosting.Stories.Server` on 2026-07-29 may already be staged or committed on `master`; if it is staged and uncommitted, carry it onto this branch.

- [ ] **Step 2: Add the package to Central Package Management**

In `Yggdrasil/Directory.Packages.props`, add to the Midgard block, in alphabetical position immediately after the `Norse.Infrastructure.ServiceDefaults` line:

```xml
		<PackageVersion Include="Norse.Infrastructure.ServiceDefaults.AspNet" Version="$(MidgardVersion)" />
```

Leave `<MidgardVersion>` at `0.0.10` for now — this plan validates in Bifröst dev mode (`UseProjectReferences=true`), where `NorseRef` resolves to `ProjectReference` and the version is unused. Bumping it belongs to the human's ship ceremony, after the Midgard tag exists.

- [ ] **Step 3: Swap the references**

In `Yggdrasil/src/Hosting.Web.Server/Hosting.Web.Server.csproj`, replace:

```xml
		<NorseRef Include="Infrastructure.ServiceDefaults">
			<Repo>Midgard</Repo>
		</NorseRef>
```

with (keeping alphabetical order among the `NorseRef` entries):

```xml
		<NorseRef Include="Infrastructure.ServiceDefaults.AspNet">
			<Repo>Midgard</Repo>
		</NorseRef>
```

Make the identical replacement in `Yggdrasil/src/Hosting.Stories.Server/Hosting.Stories.Server.csproj`.

- [ ] **Step 4: Adopt the application-host root in Web.Server**

In `Yggdrasil/src/Hosting.Web.Server/Program.cs`:

Change the using line

```csharp
using Norse.Infrastructure.ServiceDefaults;
```

to

```csharp
using Norse.Infrastructure.ServiceDefaults.AspNet;
```

Change

```csharp
builder.AddServiceDefaults();
```

to

```csharp
builder.AddAspNetServiceDefaults();
```

Change

```csharp
app.MapStaticAssets();
```

to

```csharp
app.MapStaticAssets().DisableHttpMetrics();
```

And immediately after the `app.MapNorseGrpcServices();` line, add:

```csharp
app.MapDefaultEndpoints();
app.MapGrpcHealthChecksService().DisableHttpMetrics();
```

**The `.DisableHttpMetrics()` on the gRPC health service is load-bearing, not decoration.** `MapGrpcHealthChecksService()` maps an endpoint at `/grpc.health.v1.Health/Check` that this project does not own, and a gRPC health client polls it exactly as aggressively as an HTTP probe. `MapGrpcHealthChecksService()` returns a `GrpcServiceEndpointConventionBuilder`, which is an `IEndpointConventionBuilder`, so the chain binds. The *tracing* half of that endpoint needs nothing here — `AspNetTraceFilter` already excludes the `/grpc.health.` prefix, which is the same fix the prior art applied and the reason that prefix is in the filter at all.

- [ ] **Step 5: Adopt the asset-host root in Stories.Server**

In `Yggdrasil/src/Hosting.Stories.Server/Program.cs`:

Change the using line to `using Norse.Infrastructure.ServiceDefaults.AspNet;`, change `builder.AddServiceDefaults();` to `builder.AddAssetHostServiceDefaults();`, and change

```csharp
app.MapStaticAssets();
```

to

```csharp
app.MapStaticAssets().DisableHttpMetrics();
app.MapDefaultEndpoints();
```

**Corrected 2026-07-30 during execution — this ordering does not matter, and the original claim here was false.** The plan said `MapDefaultEndpoints()` must precede `app.MapFallbackToFile("index.html")` or the fallback would swallow the probe paths. It would not: `MapFallbackToFile` registers its route at `Order = int.MaxValue`, so route precedence decides, not registration order, and the probes win either way. Disproved by mutation — moving the call after the fallback left all four Stories.Server probe tests green. Keep the ordering shown below because it reads better, but do not defend it as a rule.

- [ ] **Step 6: Build and test the solution**

```bash
dotnet build Yggdrasil/Yggdrasil.slnx
dotnet test Yggdrasil/Yggdrasil.slnx
```

Expected: build succeeded, 0 warnings, 0 errors; every test green. If `Hosting.Worker` or `Hosting.Migrations.Service` fails to build, a `FrameworkReference` has leaked — **stop and report**, do not work around it.

- [ ] **Step 7: Commit**

```bash
git -C Yggdrasil add src/Hosting.Web.Server src/Hosting.Stories.Server Directory.Packages.props
git -C Yggdrasil commit -m "$(cat <<'EOF'
Adopt the ASP.NET service-defaults roots in both web hosts

Web.Server takes the application-host root plus gRPC health; Stories.Server
takes the asset-host root. Both map /livez and /readyz and suppress
observability on static assets. Worker and Migrations are untouched.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Bifröst solution repair and live verification

**Files:**
- Modify: `Bifrost.slnx`

**Interfaces:**
- Consumes: every prior task.
- Produces: nothing — this is the verification gate.

**⚠ Bifröst NEVER branches.** Work on `master`. **Stage only — do not commit.** The human commits.

**Pre-existing defect this task also fixes:** `Infrastructure.ServiceDefaults` and `Infrastructure.ServiceDefaults.Tests` were never added to `Bifrost.slnx` when Layer 0 shipped (`grep -c ServiceDefaults Bifrost.slnx` returns `0`). Both are added here alongside the new project's entries.

- [ ] **Step 1: Confirm Bifröst is on `master`**

```bash
git -C . branch --show-current
```

Expected: `master`. If not, **halt and ask**.

- [ ] **Step 2: Add all four project entries to `Bifrost.slnx`**

In the `/Infrastructure/src/` folder, immediately after the `Infrastructure.Migrations` line:

```xml
		<Project Path="Midgard/src/Infrastructure.ServiceDefaults/Infrastructure.ServiceDefaults.csproj" />
		<Project Path="Midgard/src/Infrastructure.ServiceDefaults.AspNet/Infrastructure.ServiceDefaults.AspNet.csproj" />
```

In the `/Infrastructure/tests/` folder, immediately after the `Infrastructure.Migrations.Tests` line:

```xml
		<Project Path="Midgard/tests/Infrastructure.ServiceDefaults.Tests/Infrastructure.ServiceDefaults.Tests.csproj" />
		<Project Path="Midgard/tests/Infrastructure.ServiceDefaults.AspNet.Tests/Infrastructure.ServiceDefaults.AspNet.Tests.csproj" />
```

- [ ] **Step 3: Build and test the whole bridge**

```bash
dotnet build Bifrost.slnx
dotnet test Bifrost.slnx
```

Expected: build succeeded, 0 warnings, 0 errors; every test green across every realm.

- [ ] **Step 4: Run the AppHost and verify the live gate**

```bash
dotnet run --project src/Orchestration.AppHost
```

Docker must be running (Postgres primary + replica). In the Aspire dashboard, confirm:

1. **Metrics** — both `web` and `stories` report `http.server.request.duration` and `http.server.active_requests`.
2. **Traces** — `web` shows request spans; `stories` shows **none**.
3. **No static-asset noise** — neither host's metrics or logs carry entries for `.wasm`, `.js`, `.css`, or `.dll` fetches. Stories.Server's log volume should be dramatically lower than before its `appsettings.json` landed.

   **Check `_framework/*` and `_content/*` specifically, and check the two signals separately — they are suppressed by different mechanisms and can fail independently.**

   *Traces* should be clean regardless of how the payload is served: `AspNetTraceFilter` matches on the request path, so `/_framework/dotnet.native.wasm` is excluded by the `/_` prefix and by `Path.HasExtension` both, whether a routed endpoint or a middleware serves it. A framework asset appearing in either host's **traces** means the filter is not wired or not matching — stop and report; do not widen the filter to chase a symptom.

   *Metrics* are the genuine open question, and this is the gap the path filter does not close. `MapStaticAssets().DisableHttpMetrics()` reaches only what `MapStaticAssets()` owns as endpoints. In .NET 9+ the static-web-asset manifest is expected to cover the fingerprinted `_framework` payload, but that is an expectation this plan has not verified. If `_framework/blazor.boot.json`, `dotnet.*.js`, or the `.wasm` payload appear in either host's **metrics**, they are being served by middleware rather than a routed endpoint, and endpoint metadata cannot reach them. **Named remediation in that case:** a small middleware that sets `IHttpMetricsTagsFeature.MetricsDisabled = true` for paths `AspNetTraceFilter` already rejects, giving both signals one predicate. **Report before building it** — it is a design addition, not a build fix, and it is deliberately out of this plan's scope.

4. **Identity meter** — `Microsoft.AspNetCore.Identity` instruments appear for `web` and **not** for `stories`.
5. **Probes** — take the two hosts' ports from the dashboard and run:

```bash
curl -ks https://localhost:<web-port>/livez      ; echo
curl -ks https://localhost:<web-port>/readyz     ; echo
curl -ks https://localhost:<stories-port>/livez  ; echo
curl -ks https://localhost:<stories-port>/readyz ; echo
```

Expected: `Healthy` on all four, with no check names or timings in any body.

6. **Probe traffic produces no spans** — re-check the `web` traces view after those `curl` calls. Neither the two REST probes nor `/grpc.health.v1.Health/Check` may appear. This is the live confirmation of the amended Task 2 design: all three paths are excluded by `AspNetTraceFilter` matching the request path, with no endpoint convention involved. The gRPC health endpoint is the one to look hardest at — it is polled by its own clients on a timer, so leave the AppHost running a minute and look again rather than trusting a single glance.

Record the observed results. If any of the six fails, **stop and report** — do not adjust the verification to match the behavior.

- [ ] **Step 5: Stage, do not commit**

```bash
git -C . add Bifrost.slnx
git -C . status --short
```

Expected: `M  Bifrost.slnx` (plus whatever else the human already had in flight — leave it alone).

- [ ] **Step 6: Report the handoff**

Summarize for the human: the three realm branches and their commit counts, the Bifröst staged diff, the six live-gate results, and the Task 5 ruling. State explicitly that PRs, CI, tags, `dotnet nuget push`, and the `MidgardVersion` bump in `Yggdrasil/Directory.Packages.props` are outstanding and are theirs.

---

## Spec follow-ups this plan generates

Fold these into the spec after implementation, in the same pass that records the results:

- **§5.4 gains the publisher note.** The gRPC health path is `IHealthCheckPublisher`-driven and therefore timer-evaluated, not on demand — a behavioral asymmetry with the REST endpoints and a source of unconditional periodic database traffic. Accepted with the default 30-second period on prior-art grounds (an earlier production system of ours ran this package untuned against a real database check). Also record the deliberate divergence from that prior art: checks are registered **once** on the shared rail rather than duplicated across `AddHealthChecks()` and `AddGrpcHealthChecks()`, because both funnel into the same `HealthCheckServiceOptions.Registrations` and `GrpcHealthChecksOptions.Services` is the real seam for per-service mapping.
- **§5.4 gains the gRPC health endpoint's observability opt-out.** `MapGrpcHealthChecksService()` maps an endpoint the ASP.NET layer does not own. Its HTTP metrics are suppressed by chaining `DisableHttpMetrics()` at the map site; its traces are suppressed by the `/grpc.health.` prefix in `AspNetTraceFilter` — the same prefix, for the same reason, as the prior art that proved it.
- **§5.3.3 loses its "confirm at implementation" caveat.** The orphaned-root-span concern is moot under the final design: the trace filter rejects both probe paths outright, and Task 8 Step 4 item 6 verifies no probe spans appear. Replace the caveat with the observed result.
- **§5.6 gains the two-mechanism rule, and it is the most important thing this plan learned.** The spec describes `DisableHttpMetrics()` directly, which turns out to be exactly right for metrics and unavailable for traces. Record the pipeline fact that forces it: OpenTelemetry's ASP.NET `Filter` runs from `OnStartActivity` on `HttpRequestIn.Start`, before routing, where `GetEndpoint()` is `null`; HTTP metrics are recorded at request end, after routing, where endpoint metadata is live. **Traces are suppressed by path, metrics by endpoint metadata, and no single convention can cover both.** A `DisableNorseObservability()` convention that appeared to do so shipped in Midgard `87231ab` and suppressed nothing at all; it is deleted. Anyone who proposes reunifying the two needs this paragraph.
- **§5.5 has already lost its "confirm at implementation" caveat** — done in-flight on 2026-07-30. Task 6's implementer recovered all twelve instrument names by decompiling the 11.0.0-preview.6 shared framework after confirming reflection could not reach them (both metrics types are `internal`), and the spec now carries the full table. Task 8's dashboard check is live confirmation of the same fact, not the source of it.
- **`codenames.md` needs no change** — `Infrastructure.ServiceDefaults.AspNet` is function-named, and no codename leaves the bench in this work.
- **Midgard's `CLAUDE.md` and `README.md`** gain `Infrastructure.ServiceDefaults.AspNet` in their live-project narrative (boy-scout law: the pair tells one story at two altitudes, and both must move together).
