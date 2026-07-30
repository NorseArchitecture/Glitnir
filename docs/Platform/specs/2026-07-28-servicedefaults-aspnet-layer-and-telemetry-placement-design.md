# ServiceDefaults — The Telemetry Placement Law and the ASP.NET Layer

**Date:** 2026-07-28
**Status:** Brainstorm-ratified 2026-07-28; awaiting written-spec review before planning
**Amends:** `2026-07-28-servicedefaults-layer0-observability-design.md` §2.1, §2.6, §4 — see §3
**Scope:** the placement law that governs where telemetry is declared platform-wide, and the design of `Infrastructure.ServiceDefaults.AspNet`. Layer 3's remnants and client-side (WASM) telemetry are future-dated to named landing spots, not designed here.

---

## 1. Context

Layer 0 shipped and merged (Midgard `335cb41`, Yggdrasil `1e01ba5`). All four containers call `builder.AddServiceDefaults();`, OTLP export is live behind its endpoint guard, and the Aspire dashboard shows traces, metrics, and logs from the composition.

Planning the fast-follow layers surfaced a fact the Layer 0 spec did not have: **most of what Layers 1 and 3 were scoped to build already exists, emitted by the vendor packages the realms reference anyway.** The Migrations Service was already producing Npgsql spans and EF metrics before Layer 0 landed — they had no exporter to reach, which is precisely what the Task 0 addendum found when it audited the Structured-logs pane and read platform-wide emptiness as a per-container silence.

That fact generalizes past EF, and generalizing it is what this document does. The result subtracts two of the four planned projects rather than building them.

---

## 2. The placement law

> **The realm that brings the dependency declares its telemetry. `ServiceDefaults` declares only what it itself brings.**

Corollaries:

- **Vendor telemetry rides in with the vendor's own integration package.** We do not re-derive per-vendor source and meter names, and we do not wrap a vendor's seam in a Norse-branded one.
- **Enforcement is the reference graph, not a rule anyone must remember.** A container that does not reference the realm cannot acquire that realm's telemetry. This is the same compiler-enforced property §2.1 of the Layer 0 spec wanted from separate assemblies — obtained without the assemblies.
- **`ServiceDefaults` stays deliberately dumb.** It declares runtime instrumentation and the `Norse.*` wildcard. That is what it brings; that is all it declares.

### 2.1 Applying the law

| Dependency | Brought by | Declares its telemetry where | Status |
|---|---|---|---|
| Npgsql / SqlClient / Oracle + EF Core | Urðarbrunnr provider bindings | Already, via Aspire's `Enrich*DbContext` | **Live today, no work** |
| NServiceBus | Ratatoskr (`Norse.Messaging.NServiceBus`) | Inside its endpoint configuration | Future-dated, §6 |
| ASP.NET Identity / OpenIddict | Himinbjörg (`Identity.Web.Server`) | Alongside its own registration | §5.5 |
| gRPC transport + health | Midgard (`Infrastructure.Web.Server`) | Alongside the transport it already owns | §5.4 |
| ASP.NET Core hosting | *nobody* — it is a `FrameworkReference` | `ServiceDefaults.AspNet` | §5, this spec |

**ASP.NET Core is the law's one genuine exception, and that exception is the entire justification for the `.AspNet` assembly.** No realm "brings" the ASP.NET shared framework; it arrives through the SDK. Nothing else can own it, and keeping its `FrameworkReference` out of the Worker and the Migrations Service is why a separate assembly must exist at all.

### 2.2 Why the seam is OpenTelemetry's, not ours

An earlier sketch had realms publishing `Action<TracerProviderBuilder>` / `Action<MeterProviderBuilder>` contributions for `ServiceDefaults` to collect. **That seam already exists and it is `IOpenTelemetryBuilder`.** `AddOpenTelemetry().WithTracing(…)` does not apply eagerly — it stores a configure delegate that runs when the provider is constructed at host build, so a realm registering *after* `AddServiceDefaults()` still lands. Our own tree proves it: `AddNorseMigrations()` runs on the line after `AddServiceDefaults()` in `Hosting.Migrations.Service/Program.cs`, and Npgsql's tracing arrives.

A Norse `ITelemetryContributor` would be a re-implementation of `IOpenTelemetryBuilder` wearing our brand — the bespoke abstraction §1.2 of the Layer 0 spec rules out. It is not built.

### 2.3 Why the vendor keeps ownership of the names

The three EF provider families are instrumented by three structurally different mechanisms (evidence: §7.1). Npgsql's telemetry is expressible as a *name*; SqlClient's is a *call* that installs a `DiagnosticListener` subscriber; Oracle's arrives through a vendor package. No uniform binding-data shape describes all three, and each moves independently across versions.

Aspire curates that knowledge across the whole provider roster — SQL Server, PostgreSQL, MySQL, Oracle, Cosmos, SQLite, and the Azure variants. Taking ownership of it would mean re-deriving it three ways today and re-deriving it again on every vendor bump, in exchange for nothing a consumer can observe. **The Aspire EF components stay exactly as they are, unwrapped and unconfigured.**

A future provider binding arrives with its telemetry already correct, for free. That is the acceptance test for this decision.

---

## 3. Amendments to the Layer 0 spec

### 3.1 §2.1 — the family is two projects, not four

```
src/Infrastructure.ServiceDefaults          → Norse.Infrastructure.ServiceDefaults   (shipped)
src/Infrastructure.ServiceDefaults.AspNet   → Norse.Infrastructure.ServiceDefaults.AspNet   (this spec)
```

`.Persistence` and `.Messaging` **are never created.** Their content dissolves into the realms that bring the dependencies, per §2. The `FrameworkReference` rationale is unchanged and now carries the full weight of the split: Layer 0 carries none, ever; `.AspNet` carries the only one.

### 3.2 §2.6 — the Migrations Service rule is about reporters, not registrations

**Was:** "No health machinery for the Migrations Service, ever." **Is:**

> Migrations Service: exit code is the contract, forever. **No health reporter, ever.** A check registration arriving incidentally from a provider component is dead weight, not participation — nothing consumes it, and its lifetime is the process.

Rationale: a run-to-completion job's health is binary and already fully expressed by its exit code. Either it is a hard non-starter that halts the cluster, or it exits and its dependents start. There is no window in which "unhealthy but still running" is a state worth reporting. Aspire's `Enrich` registers `AddDbContextCheck<TContext>` today; with no reporter mapped, that registration is inert and costs nothing. We never author participation for this container; we do not fight a vendor registering a check nobody reads.

This resolves the tension logged as an open follow-up after Layer 0 (`EnrichNpgsqlDbContext` registering DbContext health checks by default). The answer is that it does not matter.

Amendment A of the Layer 0 spec ("inert registration is still participation") is narrowed to what it was actually deciding: **what `AddServiceDefaults()` itself composes.** It never composes the health rail. That stands unchanged.

### 3.3 §2.6 / §4 — endpoint names follow Kubernetes, not the Aspire template

**Was:** `/healthz` (readiness) + `/alive` (liveness). **Is:** `/readyz` (readiness) + `/livez` (liveness).

Kubernetes settled this: `/livez`, `/readyz`, and `/healthz` — with **`/healthz` deprecated since v1.16** in favor of the two specific endpoints. The shipped text was wrong twice: `/healthz` is both the wrong endpoint semantically and the deprecated name. Aspire's stock `/health` + `/alive` matches neither convention.

There is nothing to fight to get this. `MapDefaultEndpoints` is not an Aspire API — it is scaffolded source in a project we own, with the paths as string literals. AppHost-side, `WithHttpHealthCheck` takes the path as a parameter. Honoring the standard costs only the choice.

### 3.4 §2.6 — the gRPC health package is named wrong, and it moves

**Was:** `Grpc.HealthCheck`, mapped by Layer 2. **Is:** `Grpc.AspNetCore.HealthChecks`, owned by `Infrastructure.Web.Server` (§5.4).

`Grpc.HealthCheck` is the reference implementation of the service plus its protobuf types — its dependencies are `Google.Protobuf` and `Grpc.Core.Api`, and it has no knowledge of `Microsoft.Extensions.Diagnostics.HealthChecks`. The package that bridges our health rail to `grpc.health.v1.Health` is `Grpc.AspNetCore.HealthChecks`.

### 3.5 §4 — the terminal layer splits

Identity and OpenIddict are in materially different states and no longer defer together:

- **Identity** — real today and currently dark. `Microsoft.AspNetCore.Identity` ships its own `Meter`; Layer 0 subscribes `Norse.*` only, so none of it reaches the dashboard. One line, in Himinbjörg. §5.5.
- **OpenIddict** — nothing to instrument. `OpenIddict.EntityFrameworkCore` in `Identity.Web.Server` is the only reference on the platform; no `OpenIddict.Server*`, `.Validation`, or `.Server.AspNetCore` anywhere. The entities exist, the authorization server does not. Defers with the auth server, not with messaging. §6.

The §4 instruction to "resist any temptation to generalize auth instrumentation upward" is preserved and is now enforced structurally: Himinbjörg is referenced only by Web.Server.

### 3.6 §4 — Layer 3 distributes rather than deferring as a unit

See §6. `.Messaging` as a project is not created.

### 3.7 §3.2 — `AddServiceDefaults()` gains two optional signal delegates

**Was:** `AddServiceDefaults()`, no parameters. **Is:**

```csharp
public IHostApplicationBuilder AddServiceDefaults(
    Action<TracerProviderBuilder>? configureTracing = null,
    Action<MeterProviderBuilder>? configureMetrics = null)
```

Each is `?.Invoke`d inside Layer 0's own `WithTracing`/`WithMetrics` block, after its `Norse.*` wildcard and runtime instrumentation. This is what lets a higher layer contribute to the single OpenTelemetry composition instead of opening a second one — see §5.2.1 for the reasoning, the ordering guarantee it preserves, and the binary-compatibility cascade it triggers.

The delegates are additive only. §2.3's "pure emission, no opt-out, no lightweight variant" stands: nothing a caller passes can subtract emission, and `AddServiceDefaults()` with no arguments behaves exactly as shipped. The `static` modifiers on Layer 0's current tracing and metrics lambdas come off, since the parameters are now captured.

This is the only change to already-shipped Layer 0 *code* in this spec; every other amendment in §3 is spec text or new work.

---

## 4. The three durable runtime containers

`.AspNet`'s shape follows from this table, and every divergence in it is produced by the reference graph rather than by configuration:

| Container | SDK | ASP.NET | gRPC | Telemetry wanted | Health checks present |
|---|---|---|---|---|---|
| Web.Server | `Sdk.Web` | ✓ | ✓ (`Infrastructure.Web.Server`) | metrics + logs + traces | `self` + DbContext (via `Enrich`) |
| Stories.Server | `Sdk.Web` | ✓ | ✗ | metrics + logs, **no traces** | `self` only, forever |
| Worker | `Sdk.Worker` | ✗ | ✗ | metrics + logs (Layer 0 only) | none until Layer 3 |

Stories.Server gets no request tracing on its own merits, not as a limitation: it serves a static WASM catalog with no database, no transport, and no downstream. Its spans would be hundreds of static-asset requests per page load with nothing to correlate against. There is no trace because there is no journey. `Microsoft.AspNetCore.Hosting`'s meters supply exactly the traffic and usage signal it needs.

---

## 5. Specification

### 5.1 Project

`Midgard/src/Infrastructure.ServiceDefaults.AspNet/` → assembly `Norse.Infrastructure.ServiceDefaults.AspNet`. `internal sealed` by default; the public surface is the extension entry points only. One test project per package: `tests/Infrastructure.ServiceDefaults.AspNet.Tests`.

Packages (direct `PackageReference`; CPM is Yggdrasil-only per house rules):

- `OpenTelemetry.Instrumentation.AspNetCore` — **1.17.0, stable, released 2026-07-17**
- `ProjectReference` → `Infrastructure.ServiceDefaults`

Plus `<FrameworkReference Include="Microsoft.AspNetCore.App" />` — the only one in the family, and the reason the assembly exists.

**Why take the instrumentation package when ASP.NET Core emits natively.** The framework's own `ActivitySource` and meters are live without it (evidence: §7.2), so the package is not needed to *produce* signal. It is taken for two things it alone provides: on .NET 8+ a single `AddAspNetCoreInstrumentation()` call subscribes the vendor's curated set of built-in meters — consistent with §2.3, where we let the vendor own the name list — and it carries the `Filter` predicate, which is the only supported way to keep probe and asset traffic out of the trace stream. Stable, current, and it buys capability, so it passes the prerelease test of Layer 0 §3.1 cleanly.

### 5.2 API surface

**Each host makes exactly one builder call.** The `.AspNet` entry point composes Layer 0's rather than sitting beside it, so no host ever calls two `Add*ServiceDefaults` methods.

Builder extensions target **`IHostApplicationBuilder`**, not `WebApplicationBuilder` — they only touch `Services`, `Logging`, and the OpenTelemetry builder, so the narrower type costs nothing and keeps the family's signatures uniform. Only `MapDefaultEndpoints()` needs `WebApplication`, because only it maps routes.

- **`AddAspNetServiceDefaults()`** — the full ASP.NET root, and the one call an application host makes. Forwards both signal delegates into Layer 0's single OpenTelemetry composition, then composes `AddDefaultHealthChecks()` (Layer 0's rail, shipped uncalled and finally called here).
- **`AddAssetHostServiceDefaults()`** — for hosts that serve static content only. Identical, minus the tracing delegate. Named for the reason it is reduced, so a future web host answers one legible question — "am I an asset host?" — instead of reading a boolean or an enum. `Asset` follows the framework's own vocabulary (`MapStaticAssets`), per Layer 0 §2.3.

The two differ by one argument, which is the point — the divergence is visible at a glance instead of being buried in two separate composition bodies:

```csharp
public IHostApplicationBuilder AddAspNetServiceDefaults() =>
    builder
        .AddServiceDefaults(
            configureTracing: static tracing => tracing.AddAspNetCoreInstrumentation(
                static options => options.Filter = AspNetTraceFilter.Include),
            configureMetrics: static metrics => metrics.AddAspNetCoreInstrumentation())
        .AddDefaultHealthChecks();

public IHostApplicationBuilder AddAssetHostServiceDefaults() =>
    builder
        .AddServiceDefaults(configureMetrics: static metrics => metrics.AddAspNetCoreInstrumentation())
        .AddDefaultHealthChecks();
```
- **`MapDefaultEndpoints()`** — maps `/livez` and `/readyz` per §5.3. Keeps its Aspire-conventional name; it is the *paths* that follow the Kubernetes standard (§3.3), not the method that maps them.

| Host | Builder call | Endpoint call |
|---|---|---|
| Web.Server | `AddAspNetServiceDefaults()` | `MapDefaultEndpoints()` |
| Stories.Server | `AddAssetHostServiceDefaults()` | `MapDefaultEndpoints()` |
| Worker, Migrations Service | `AddServiceDefaults()` (Layer 0, unchanged) | — |

Neither ASP.NET entry point takes parameters. A host needing more than the default filter configures `AspNetCoreTraceInstrumentationOptions` itself; the surface stays at zero until a concrete need appears.

#### 5.2.1 Why delegate forwarding, not composition-by-calling

The alternative was for `.AspNet` to call `AddServiceDefaults()` and then open its own `AddOpenTelemetry().WithTracing(…)` block. That works — `WithTracing`/`WithMetrics` store deferred configure delegates applied at provider construction, so a later call lands in the same provider, as our own tree proves with `AddNorseMigrations()` running after `AddServiceDefaults()`. Forwarding is chosen anyway, for two reasons that survive:

1. **Ordering guarantee.** Forwarding lets Layer 0 decide what runs after a caller's contributions — the base invokes the delegate inside its own block and can then append registrations it needs to run last. Composition-by-calling cannot: Layer 0's delegates always run first. A `BaseProcessor<Activity>` in Layer 0 whose correctness depends on seeing every span — a baggage-to-tag copier, a redactor, a tenant stamper — is exactly the shape §3.4 of the tenancy design implies we will eventually want, and composition-by-calling forecloses it. Layer 0 registers no processors today; the point is that this shape does not have to be revisited when it does.
2. **One telemetry composition, one place to read it.** Every source, meter, instrumentation, and processor in a container is configured inside a single `WithTracing`/`WithMetrics` pair in Layer 0. Two `AddOpenTelemetry()` blocks in two assemblies work identically at runtime but split the answer to "what is this container emitting" across two files.

The single-call requirement is satisfied either way; this is not the reason.

**What it costs, and why the cost is acceptable.** Layer 0's public signature changes, which means a Midgard re-ship. Both projects live in Midgard, so that is one repo, one PR, one tag, one publish — no cross-realm sequencing, no ship-gate chain. Ruled acceptable 2026-07-29.

**Binary-compatibility note — real, and this platform has been bitten by it.** Optional parameters are baked at the call site: the compiler emits `AddServiceDefaults(null, null)`. Assemblies compiled against the shipped zero-parameter signature will therefore `MissingMethodException` at runtime rather than fail to compile. Yggdrasil is the only consumer; Bifröst's dev mode uses `ProjectReference` and recompiles anyway, and package mode needs the CPM pin bumped and a rebuild. **The done-bar is tests passing in package mode, not a successful build** — per the standing rule on binary-compat cascades from `Norse.Abstractions`, a clean build proves nothing here.

**This does not weaken Layer 0 §2.3 or §3.2.** The delegates are additive only. There is still no opt-out flag, no conditional registration, and no lightweight variant — nothing a caller passes can subtract emission, only add to it. `AddServiceDefaults()` with no arguments behaves exactly as shipped.

**Rejected alternative — overload on `WebApplicationBuilder`.** Naming the `.AspNet` entry point `AddServiceDefaults()` and extending `WebApplicationBuilder` would make every container in the platform call the same literal line, an appealing completion of Layer 0 §3.2. It is rejected: overload resolution would then depend on the *static type* of the local, so declaring `IHostApplicationBuilder builder = WebApplication.CreateBuilder(args)` silently selects the wrong root and quietly drops ASP.NET instrumentation. Behavior hinging on a variable's declared type is not structural enforcement. Distinct names are — a Worker calling `AddAspNetServiceDefaults()` does not compile, because the package is not referenced.

### 5.3 Health endpoints

#### 5.3.1 Shape

```csharp
app.MapHealthChecks("/livez", new() { Predicate = static r => r.Tags.Contains("live") })
   .AllowAnonymous()
   .DisableHttpMetrics();
app.MapHealthChecks("/readyz")
   .AllowAnonymous()
   .DisableHttpMetrics();
```

`/livez` filters the `live` tag — which is the `self` check Layer 0 already ships, and nothing else. It performs no I/O, so it is safe to poll hard. `/readyz` runs every registered check.

**No tagging work is required, and the `ready` tag considered during design is not introduced.** Aspire registers DbContext checks untagged; untagged checks land in `/readyz` naturally and are excluded from `/livez` automatically. `self` is tagged `live` and therefore appears in both. One tag exists platform-wide and Layer 0 already shipped it.

**The Stories/Web divergence needs no configuration.** Both hosts map identical endpoints. Stories.Server registered nothing beyond `self`, so its readiness is self-only — truthful, since a static file server's readiness genuinely is "Kestrel is listening." Web.Server has `self` plus whatever `Enrich` registered. The difference is produced entirely by the reference graph.

`AllowAnonymous()` is load-bearing on Web.Server, which runs `UseAuthentication`/`UseAuthorization`; a probe arrives with no credentials.

#### 5.3.2 Exposure policy

**Both endpoints map in every environment.** The stock template's Development-only mapping is wrong for us — Kubernetes probes need these in production or the container never passes its gates.

**The default response writer only.** Status code plus a bare `Healthy`/`Unhealthy` string. The detailed JSON writer — which discloses check names, dependency topology, and timings — never ships outside Development. A 200 versus a 503 tells an attacker nothing they could not learn by sending a real request.

**Probes are not routed by the ingress.** They are reached by the kubelet on the pod network, directly against the container. `/livez` and `/readyz` are absent from the proxy's route table, so external traffic gets a 404 from the proxy without a byte reaching the stack. This is the primary control.

**Rejected for this layer, recorded with triggers:**

- *A dedicated management port* (`RequireHost("*:<port>")`). Buys a control the ingress already provides, at the cost of a second Kestrel endpoint plus AppHost, compose, and Kubernetes service plumbing — and it would break the Bifröst dashboard verification path for zero local benefit. **Named trigger:** a deployment target that cannot express ingress route exclusion.
- *Output caching and request timeouts on `/readyz`.* This is Microsoft's documented abuse mitigation and it is real, but it is redundant behind the ingress control: `/livez` performs no I/O, and `/readyz` is unreachable externally. It also requires flipping `HealthCheckOptions.AllowCachingResponses` to `true` — by default the middleware actively overrides `Cache-Control`, `Expires`, and `Pragma` to defeat caching, so wiring `CacheOutput` without it silently no-ops while looking correct in review. **Named trigger:** probes become reachable through a public route, or `/readyz` acquires a check materially more expensive than `CanConnectAsync()`.

#### 5.3.3 The trace filter, and why it is not load-bearing

`AddAspNetRequestTracing()` ships a `Filter` excluding `/livez`, `/readyz`, and static-asset requests.

An earlier reading had this predicate solving database noise from health polling — the concern that unauthenticated `/readyz` traffic would drive `CanConnectAsync()` round trips into the trace stream. **It would not have worked, and would have made things worse.** When `Filter` returns `false` the instrumentation never starts the request Activity, so `Activity.Current` is null when the check body runs and Npgsql's `StartActivity()` creates a *root* span that the default sampler records — an orphaned, context-free database span, harder to identify as probe traffic than if nothing had been filtered.

Tag discipline dissolves the problem instead: the aggressively-polled endpoint (`/livez`) touches nothing, and the endpoint that reaches the database (`/readyz`) is polled rarely and deliberately. The filter returns to its proper, modest job.

*Confirm at implementation:* the sampler behavior above is reasoned from the mechanism, not measured. A POC should verify that a filtered request produces no orphaned child spans before the spec text is treated as settled.

### 5.4 gRPC health moves to `Infrastructure.Web.Server`

Per §2, the project that brings the gRPC transport owns gRPC health. `Grpc.AspNetCore.HealthChecks` and the `grpc.health.v1.Health` mapping land in Midgard's `Infrastructure.Web.Server`, alongside the interceptor stack and `MapNorseGrpcServices`.

This collapses the Stories/Web divergence into the reference graph: **Stories.Server does not reference `Infrastructure.Web.Server`, so it structurally cannot acquire gRPC health.** No options flag, no host-kind enum, no documentation anyone must remember. Verified — Stories.Server's references are `Infrastructure.ServiceDefaults`, `Microsoft.AspNetCore.Components.WebAssembly.Server`, and `Hosting.Stories.Client`, and nothing else.

Legal under the attack-surface law of Layer 0 §2.6: that law bans listeners in headless containers, not health protocols in a container already hosting Kestrel and gRPC.

*Confirm at implementation:* `Grpc.AspNetCore.HealthChecks` is contract-first grpc-dotnet, while our idiom is protobuf-net.Grpc code-first with a generated `MapNorseGrpcServices`. Both host on `Grpc.AspNetCore.Server`, so they should compose on the same endpoint routing — but a proto-generated service arriving alongside the `CompatibilityLevel 300` sweep and the `RuntimeTypeModel.Default` registration deserves a proof line, not a confident sentence.

### 5.5 Identity telemetry lands in Himinbjörg

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("Microsoft.AspNetCore.Identity"));
```

A name, not a bridge — no instrumentation package, no beta dependency. It rides alongside Himinbjörg's own registration, and because `Identity.Web.Server` is referenced only by Web.Server, it lands in exactly the one container the terminal-layer rule names. The rule enforces itself.

**Instrument set — recovered 2026-07-30, during Task 6.** The names were not reachable by reflection (both metrics types are `internal`) but are plainly readable by decompiling the 11.0.0-preview.6 shared framework. Both types declare `public const string MeterName = "Microsoft.AspNetCore.Identity"`, confirming the single meter name the subscription targets. Twelve instruments:

`SignInManagerMetrics` (`Microsoft.AspNetCore.Identity.dll`):

| Instrument | Kind | Unit |
|---|---|---|
| `aspnetcore.identity.sign_in.authenticate.duration` | histogram | `s` |
| `aspnetcore.identity.sign_in.check_password_attempts` | counter | `{attempt}` |
| `aspnetcore.identity.sign_in.sign_ins` | counter | `{sign_in}` |
| `aspnetcore.identity.sign_in.sign_outs` | counter | `{sign_out}` |
| `aspnetcore.identity.sign_in.two_factor_clients_remembered` | counter | `{client}` |
| `aspnetcore.identity.sign_in.two_factor_clients_forgotten` | counter | `{client}` |

`UserManagerMetrics` (`Microsoft.Extensions.Identity.Core.dll`):

| Instrument | Kind | Unit |
|---|---|---|
| `aspnetcore.identity.user.create.duration` | histogram | `s` |
| `aspnetcore.identity.user.update.duration` | histogram | `s` |
| `aspnetcore.identity.user.delete.duration` | histogram | `s` |
| `aspnetcore.identity.user.check_password_attempts` | counter | `{attempt}` |
| `aspnetcore.identity.user.verify_token_attempts` | counter | `{attempt}` |
| `aspnetcore.identity.user.generated_tokens` | counter | `{count}` |

The three histograms carry `InstrumentAdvice<double>` bucket boundaries from `MetricsConstants.ShortSecondsBucketBoundaries` — relevant the moment anyone configures a view or a custom aggregation over them.

### 5.6 Static assets emit neither metrics nor logs

**Metrics** — endpoint-scoped, via in-box API:

```csharp
app.MapStaticAssets().DisableHttpMetrics();
```

This suppresses at the endpoint serving the assets. It works because HTTP metrics are recorded at request *end*, after the routing middleware has run, so `IDisableHttpMetricsMetadata` is live by the time the decision is made. Applies on both web hosts.

**Traces cannot use the same mechanism, and this is a pipeline fact rather than a preference.** OpenTelemetry invokes `AspNetCoreTraceInstrumentationOptions.Filter` from `OnStartActivity`, handling the `Microsoft.AspNetCore.Hosting.HttpRequestIn.Start` event — *before* routing. `HttpContext.GetEndpoint()` returns `null` there, so no endpoint convention can reach a tracing decision; the request path is what the pipeline has produced by that point. Trace suppression is therefore a path predicate (`AspNetTraceFilter`), covering the two probe constants, the `/grpc.health.` and `/_` prefixes, and any path with a file extension.

**Two signals, two mechanisms, and no single convention can cover both.** A `DisableNorseObservability()` convention that appeared to unify them shipped in Midgard `87231ab` and suppressed no spans whatever; it is deleted. Confirmed against `OpenTelemetry.Instrumentation.AspNetCore` 1.17.0, whose `OnStartActivity` carries a `g__DisableActivity` local function (the `Filter` consumption point) alongside `g__SetUrlPathAttribute` — the URL path is available exactly where endpoint metadata is not.

**Logs — fixed ahead of this spec (2026-07-29).** `Hosting.Stories.Server` shipped with **no `appsettings.json` at all** and therefore logged at `Information` across the board, emitting a "Request starting"/"Request finished" pair for every `.wasm`, `.js`, `.css`, and `.dll` fetch. `Hosting.Web.Server` already carried `"Microsoft.AspNetCore": "Warning"`, which suppresses them.

Stories.Server now carries the same configuration. This was an oversight in the 2026-07-12 hosting drop — the host was scaffolded as a skeleton HTTP server so the catalog could ride Yggdrasil's existing container and release cycle into the cluster, and it never got the config file the template would otherwise have supplied. It was a live defect on `master`, not a requirement this spec introduces, so it was corrected directly rather than being carried as planned work. Only the base file was added; `Hosting.Web.Server`'s `appsettings.Development.json` merely repeats the base minus `AllowedHosts` and that redundancy was not cloned.

---

## 6. Future-dated, with landing spots

Enumerated so the dependency graph can respect them; deliberately not designed here.

- **Messaging telemetry → Ratatoskr.** NServiceBus v10 and later enable OpenTelemetry **by default** — no `EnableOpenTelemetry()` call. Meters are `NServiceBus.Core.Pipeline.Incoming`, `NServiceBus.TransactionalSession`, and `NServiceBus.Envelope.CloudEvents`; spans cover incoming, outgoing, and published messages. There is no Aspire *client* integration for NServiceBus (`Particular.Aspire.Hosting.ServicePlatform` is an AppHost-side hosting integration and belongs to Bifröst's still-open broker-container decision), so the subscription is ours to write — one line inside Ratatoskr's endpoint configuration when `AddNorseEndpoint()` lands:

  ```csharp
  builder.Services.AddOpenTelemetry()
      .WithTracing(t => t.AddSource("NServiceBus.*"))
      .WithMetrics(m => m.AddMeter("NServiceBus.*"));
  ```

  Particular's own sample places this in *their* ServiceDefaults. We deliberately diverge: in Ratatoskr, Stories.Server and the Migrations Service never subscribe a meter family with no emitter in their process. **No project is created for this now** — a package holding two wildcard lines would sit orphaned until the endpoint configuration exists.

- **HttpClient egress → `2026-06-07-egress-http-resilience-parsing-design.md`.** Observability, resilience, retry, backoff, jitter, the abstract types Asgard provides, and how services expose them for delegating-handler wrapping belong to one overarching design against that existing spec, pending its curation pass. It is the law's one unresolved case — nothing "brings" `HttpClient`, so nothing owns it under §2, and it cannot belong to `.AspNet` because the Worker needs egress and must never acquire that `FrameworkReference`.

- **Worker health reporter → with the messaging layer.** `IHealthCheckPublisher` → heartbeat file → exec-form AOT probe, and the tmpfs mount that a chiseled image with `readOnlyRootFilesystem` forces into the Worker's container contract. Unchanged from Layer 0 §2.6.

- **OpenIddict telemetry → with the authorization server.** Nothing to instrument until `OpenIddict.Server*` exists on the platform.

- **Client-side (WASM) telemetry → its own future spec.** Not a variation on this family. Two facts govern it: `WebAssemblyHostBuilder` does **not** implement `IHostApplicationBuilder`, so `AddServiceDefaults()` is uncallable from a client and any client story is a third assembly with a different builder type; and Blazor WASM ships an ILLink substitution stubbing metrics support on `WebAssemblyHostBuilder` when `System.Diagnostics.Metrics.Meter.IsSupported` is false, with `EventSourceSupport` defaulting to `false` in the BlazorWebAssembly SDK targets — so client metrics require flipping a trimming switch and paying bundle size. Beyond that it needs an OTLP relay endpoint on Web.Server (an unauthenticated span-ingest surface requiring its own authorization and rate-limit ruling) and a hand-written `DelegatingHandler` for `traceparent` over gRPC-Web, since `OpenTelemetry.Instrumentation.Http` hooks `DiagnosticsHandler`, which is unlikely to sit in the browser fetch path. The payoff — a trace spanning user interaction through gRPC-Web to handler to database — is real but gated on authn/authz, wire formats, and validation landing first. **Nothing in this spec is blocked by it.**

---

## 7. Evidence

Captured 2026-07-28 against packages on disk and the running framework, in the case-file discipline the Gjallarhorn precedent established.

### 7.1 The EF provider families are instrumented three different ways

| | Mechanism | Verified by |
|---|---|---|
| Npgsql 10.0.3 | `NpgsqlActivitySource` and `StartActivity` live **in the driver**. `Npgsql.OpenTelemetry` is a **6,144-byte** shim whose entire content is `TracerProviderBuilderExtensions.AddNpgsql()` → `AddSource("Npgsql")`. | assembly strings; package size |
| Microsoft.Data.SqlClient 7.0.2 | **No `ActivitySource`.** Emits `SqlDiagnosticListener` / `SqlClientDiagnostics` over `System.Diagnostics.DiagnosticSource`; `OpenTelemetry.Instrumentation.SqlClient` owns its own `ActivitySource` and subscribes the listener. The bridge is load-bearing, not legacy compatibility. | runtime impl assembly (`runtimes/unix/lib/net9.0`), not the `lib/` ref assembly |
| Oracle | `Aspire.Oracle.EntityFrameworkCore` 13.4.6 depends on `Oracle.ManagedDataAccess.OpenTelemetry` ≥ 23.26.200 | nuspec |

All three Aspire components additionally depend on `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` and subscribe the `Microsoft.EntityFrameworkCore` meter. EF Core itself carries a `MeterName` and no `ActivitySource` — metrics native, traces not.

**Settings asymmetry:** the Npgsql component exposes `DisableHealthChecks` / `DisableMetrics` / `DisableRetry` / `DisableTracing`; the SqlServer component exposes the same set **minus `DisableMetrics`**. Any uniform options record across providers would therefore have carried a knob that silently no-ops on SQL Server — the silent fallback §2.7 of `../../CLAUDE.md` bans. This is a second, independent reason not to build the seam.

### 7.2 `OpenTelemetry.Instrumentation.EntityFrameworkCore` is rejected

`1.17.0-beta.1` buys nothing here and would double-count. It bridges EF's `Microsoft.EntityFrameworkCore.Database.Command` DiagnosticSource into spans — the same command execution the driver span already covers, one altitude up, so every query would produce two nested spans carrying the same SQL. It adds no EF-level signal the driver span lacks: no SaveChanges-as-a-unit span, no query-compilation span; EF's higher-level signals are metrics, and that meter is already subscribed. No Aspire component depends on it, deliberately — it is the fallback for providers whose driver has no native OTel, and all three of ours do. It has been in beta since 2021 and has never shipped stable, failing the same prerelease test that rejected `OpenTelemetry.Instrumentation.Process`.

**Named trigger to revisit:** a provider binding lands whose driver has no native OTel instrumentation — SQLite, or Oracle's managed driver without its OTel companion. EF-level spans would then be the only spans available, and the package earns its place inside that binding, nowhere else.

### 7.3 ASP.NET Core emits natively

`Microsoft.AspNetCore.Hosting.dll` carries both an `ActivitySource` and a `MeterName`, with the literals `Microsoft.AspNetCore.Hosting` and `Microsoft.AspNetCore.Routing`; `Microsoft.AspNetCore.Server.Kestrel.Core.dll` carries its own `MeterName`. The instrumentation package is taken for subscription curation and the `Filter` hook (§5.1), not to produce signal.

### 7.4 The static-asset metrics API exists

`HttpMetricsEndpointConventionBuilderExtensions.DisableHttpMetrics` and `DisableHttpMetricsAttribute` in `Microsoft.AspNetCore.Http.Extensions.dll`; `IDisableHttpMetricsMetadata` in `Microsoft.AspNetCore.Http.Abstractions.dll`; `IHttpMetricsTagsFeature` with `get_MetricsDisabled`/`set_MetricsDisabled` in `Microsoft.AspNetCore.Hosting.dll`.

### 7.5 Host reference graph

| Host | SDK | References `Infrastructure.Web.Server` |
|---|---|---|
| `Hosting.Web.Server` | `Microsoft.NET.Sdk.Web` | **Yes** (`Generator="true"`), plus `Grpc.AspNetCore.Web`, `protobuf-net.Grpc.AspNetCore.Reflection` |
| `Hosting.Stories.Server` | `Microsoft.NET.Sdk.Web` | **No** |
| `Hosting.Worker` | `Microsoft.NET.Sdk.Worker` | No |

`Hosting.Web.Server` is also the only project on the platform referencing `Microsoft.AspNetCore.Identity.EntityFrameworkCore` or `OpenIddict.EntityFrameworkCore` — both via `Identity.Web.Server` (Himinbjörg).

---

## 8. Testing

TDD per house rules. The assertions that matter:

- `AddAspNetServiceDefaults()` registers the `self` check tagged `live` — the Layer 0 rail is composed exactly once, here — and produces the same resource attributes as a bare `AddServiceDefaults()` host, proving it composes Layer 0 rather than reimplementing it.
- `AddAssetHostServiceDefaults()` registers ASP.NET metrics and no ASP.NET tracing; `AddAspNetServiceDefaults()` registers both. Asserted as a pair, since the difference between the two web hosts is the entire reason two entry points exist.
- Neither ASP.NET entry point requires a prior `AddServiceDefaults()` call, and calling one does not double-register Layer 0's console provider or runtime instrumentation.
- `AddServiceDefaults()` with no arguments produces byte-identical registrations to the shipped version — the delegates are additive, asserted rather than assumed (§3.7).
- A delegate passed as `configureTracing` is invoked, and its sources are captured alongside `Norse.*` rather than replacing it. Same for `configureMetrics`.
- `MapDefaultEndpoints()` maps `/livez` and `/readyz`, and neither requires authorization.
- `/livez` returns healthy for a host whose only registration is `self`; a failing untagged check fails `/readyz` and leaves `/livez` healthy.
- A host with `self` only reports ready — the Stories.Server shape, asserted rather than assumed.
- The trace filter excludes `/livez`, `/readyz`, and static-asset paths, and admits an ordinary application request.
- `Infrastructure.ServiceDefaults` still contains no `Microsoft.AspNetCore.*` in its package graph — the Worker guarantee, re-asserted now that a sibling carries the `FrameworkReference`.

---

## 9. Definition of done

- `Infrastructure.ServiceDefaults.AspNet` exists in Midgard with `AddAspNetServiceDefaults()`, `AddAssetHostServiceDefaults()`, and `MapDefaultEndpoints()`; tests green.
- **Every host makes exactly one `Add*ServiceDefaults` call.** `Hosting.Web.Server` calls `AddAspNetServiceDefaults()`, `Hosting.Stories.Server` calls `AddAssetHostServiceDefaults()`, and neither retains a call to Layer 0's `AddServiceDefaults()` — the ASP.NET root composes it. `Hosting.Worker` and `Hosting.Migrations.Service` keep their existing single call, unchanged.
- `Infrastructure.ServiceDefaults.AddServiceDefaults()` carries the two optional signal delegates (§3.7), a zero-argument call behaves exactly as the shipped version, and the whole change ships as one Midgard PR/tag/publish covering both projects.
- **Package-mode verification, not just build.** Yggdrasil's CPM pin is bumped and its **tests pass against the published package** — the optional-parameter signature change is binary-breaking, so a clean build is not evidence.
- Both web hosts call `MapDefaultEndpoints()`; neither maps a health endpoint by hand.
- Both web hosts call `.DisableHttpMetrics()` on their static-asset endpoints. (`Hosting.Stories.Server`'s `appsettings.json` with `"Microsoft.AspNetCore": "Warning"` landed ahead of this spec, 2026-07-29, as an oversight fix — the host shipped without one at all.)
- `Infrastructure.Web.Server` serves `grpc.health.v1.Health` against the registered checks; `Hosting.Stories.Server` cannot, and the compiler is the reason.
- Himinbjörg subscribes the `Microsoft.AspNetCore.Identity` meter, and it appears in the dashboard only for Web.Server.
- `dotnet run --project src/Orchestration.AppHost` shows ASP.NET request metrics for both web hosts, request traces for Web.Server only, and no static-asset entries in either host's metrics or logs.
- `curl /livez` and `curl /readyz` against both web hosts return plain status with no detail body.
- No container's dependency graph contains a package outside its column in the §4 table.
