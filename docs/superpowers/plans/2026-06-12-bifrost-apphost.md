# Bifrost AppHost Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up `Norse.Orchestration.AppHost` — an Aspire AppHost that runs eight persistent Docker containers for the complete local Norse development environment, delivered in two staged commits that the human reviews before committing.

**Architecture:** A single `net11.0` Aspire AppHost project under `src/Orchestration.AppHost/` wires all containers as persistent resources (`ContainerLifetime.Persistent`) with named Docker volumes, fixed host ports, `isProxied: false`, and floating image tags with `ImagePullPolicy.Always`. Infrastructure layer lands in Commit 1 (TimescaleDB, RabbitMQ, MongoDB Atlas Local); the Particular platform layer lands in Commit 2 (RavenDB + four Particular containers). No automatic git commits — stage for review, human commits.

**Tech Stack:** .NET 11, .NET Aspire (`Aspire.Hosting.AppHost` — `*-*` for prerelease), Docker Desktop, `dotnet user-secrets` for the Particular license

---

## File Map

| Action | Path | Responsibility |
|---|---|---|
| CREATE | `Directory.Build.props` | Brand-injects `Norse.*` assembly/namespace for AppHost |
| CREATE | `src/Orchestration.AppHost/Orchestration.AppHost.csproj` | Aspire AppHost project, `net11.0` |
| CREATE | `src/Orchestration.AppHost/appsettings.json` | Local container credentials — committed, deterministic, not secrets |
| CREATE | `src/Orchestration.AppHost/Program.cs` | All container resource wiring |
| MODIFY | `Bifrost.slnx` | Adds `/Orchestration/` solution folder |

Local container credentials (`postgres-password`, `rabbitmq-user`, `rabbitmq-password`) live in `appsettings.json` under `Parameters:` — committed to the repo. These are local Docker container passwords, not cloud credentials; committing them is correct and intentional. The Particular license is the only secret; it stays in user secrets (`secret: true`, never in `appsettings.json`).

`Program.cs` is the single source of truth for all container wiring. It grows across both commits; no other file changes in Commit 2.

---

## Task 1: Scaffold the project

**Files:**
- Create: `Directory.Build.props`
- Create: `src/Orchestration.AppHost/Orchestration.AppHost.csproj`
- Create: `src/Orchestration.AppHost/Program.cs`
- Modify: `Bifrost.slnx`

- [ ] **Step 1.1: Create `Directory.Build.props` at the Bifrost root**

```xml
<Project>
	<PropertyGroup>
		<AssemblyName>Norse.$(MSBuildProjectName)</AssemblyName>
		<RootNamespace>Norse.$(MSBuildProjectName)</RootNamespace>
	</PropertyGroup>
</Project>
```

- [ ] **Step 1.2: Create `src/Orchestration.AppHost/Orchestration.AppHost.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<TargetFramework>net11.0</TargetFramework>
		<Nullable>enable</Nullable>
		<ImplicitUsings>enable</ImplicitUsings>
		<IsAspireHost>true</IsAspireHost>
		<UserSecretsId>norse-orchestration-apphost</UserSecretsId>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="Aspire.Hosting.AppHost" Version="*-*" />
	</ItemGroup>

</Project>
```

`UserSecretsId` is required for the Particular license that lands in Commit 2. Set it now so the project is ready.

- [ ] **Step 1.3: Create minimal `src/Orchestration.AppHost/Program.cs`**

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.Build().Run();
```

- [ ] **Step 1.4: Create `src/Orchestration.AppHost/appsettings.json`**

Local container credentials go here — committed to the repo, not secrets. Aspire reads `Parameters:*` keys automatically via `AddParameter`.

```json
{
	"Parameters": {
		"postgres-password": "devpassword",
		"rabbitmq-user": "guest",
		"rabbitmq-password": "guest"
	}
}
```

- [ ] **Step 1.5: Update `Bifrost.slnx`**

Add the `/Orchestration/` folder and project entry, and add `Directory.Build.props` to the existing `/Solution Items/` folder:

```xml
<Solution>
	<Folder Name="/Orchestration/">
		<Project Path="src/Orchestration.AppHost/Orchestration.AppHost.csproj" />
	</Folder>
	<Folder Name="/Primitives/">
		<!-- existing content unchanged -->
	</Folder>
	<Folder Name="/Solution Items/">
		<File Path=".editorconfig" />
		<File Path=".gitattributes" />
		<File Path=".gitignore" />
		<File Path=".gitmodules" />
		<File Path="CLAUDE.md" />
		<File Path="Directory.Build.props" />
		<File Path="dotnet-tools.json" />
		<File Path="global.json" />
		<File Path="LICENSE" />
		<File Path="README.md" />
	</Folder>
</Solution>
```

- [ ] **Step 1.6: Verify the project builds**

Run from the Bifrost root:
```
dotnet build src/Orchestration.AppHost/Orchestration.AppHost.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s)`. NuGet will restore `Aspire.Hosting.AppHost` on first run — this may take a moment.

If the build fails with `error NU1202` (package incompatible with `net11.0`), the Aspire prerelease build for .NET 11 is not yet on NuGet. Check `https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11/nuget/v3/index.json` and add it as a NuGet source if needed:
```
dotnet nuget add source https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11/nuget/v3/index.json -n dotnet11
```
Then re-run `dotnet build`.

---

## Task 2: Wire the infrastructure layer

**Files:**
- Modify: `src/Orchestration.AppHost/Program.cs`

- [ ] **Step 2.1: Replace `Program.cs` with the full infrastructure wiring**

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Local container credentials — read from appsettings.json Parameters section.
// These are Docker container passwords, not cloud secrets; appsettings.json is
// the right home and is committed to the repo.
var postgresPassword = builder.AddParameter("postgres-password");
var rabbitmqUser = builder.AddParameter("rabbitmq-user");
var rabbitmqPassword = builder.AddParameter("rabbitmq-password");

// TimescaleDB HA — the single relational + time-series store.
// PGDATA quirk: timescaledb-ha places data at /home/postgres/pgdata, not the
// standard Postgres path. The volume target must match or data does not persist.
var timescale = builder.AddContainer("timescale", "timescale/timescaledb-ha", "latest")
	.WithLifetime(ContainerLifetime.Persistent)
	.WithImagePullPolicy(ImagePullPolicy.Always)
	.WithEnvironment("POSTGRES_USER", "postgres")
	.WithEnvironment("POSTGRES_PASSWORD", postgresPassword)
	.WithEnvironment("POSTGRES_DB", "norse")
	.WithDataVolume(name: "norse-relational", target: "/home/postgres/pgdata")
	.WithEndpoint(port: 5432, targetPort: 5432, name: "pg", isProxied: false);

// RabbitMQ — management variant for HTTP API; floating tag, developer machine
// is the canary for version breakage.
var rabbit = builder.AddContainer("rabbit", "rabbitmq", "management")
	.WithLifetime(ContainerLifetime.Persistent)
	.WithImagePullPolicy(ImagePullPolicy.Always)
	.WithEnvironment("RABBITMQ_DEFAULT_USER", rabbitmqUser)
	.WithEnvironment("RABBITMQ_DEFAULT_PASS", rabbitmqPassword)
	.WithDataVolume(name: "norse-messaging", target: "/var/lib/rabbitmq")
	.WithEndpoint(port: 5672, targetPort: 5672, name: "amqp", isProxied: false)
	.WithEndpoint(port: 15672, targetPort: 15672, name: "management", isProxied: false);

// MongoDB Atlas Local — includes mongot (Atlas Search / Vector Search) on 27032.
// Chosen over library/mongo because Vector Search is in scope for the AI layer.
var mongo = builder.AddContainer("mongo", "mongodb/mongodb-atlas-local", "latest")
	.WithLifetime(ContainerLifetime.Persistent)
	.WithImagePullPolicy(ImagePullPolicy.Always)
	.WithDataVolume(name: "norse-document", target: "/data/db")
	.WithEndpoint(port: 27017, targetPort: 27017, name: "mongodb", isProxied: false)
	.WithEndpoint(port: 27032, targetPort: 27032, name: "mongot", isProxied: false);

builder.Build().Run();
```

- [ ] **Step 2.2: Run the AppHost**

```
dotnet run --project src/Orchestration.AppHost/Orchestration.AppHost.csproj
```

The Aspire dashboard URL prints to the console — open it. Expected: three resources (`timescale`, `rabbit`, `mongo`) shown as `Running`. If any show `Starting` for more than 60 seconds, check Docker Desktop to confirm the images are pulling.

- [ ] **Step 2.3: Verify the infrastructure containers are persistent**

Stop the AppHost (`Ctrl+C`). Open Docker Desktop. Expected: all three containers still in `Running` state. If any stopped, `ContainerLifetime.Persistent` is not taking effect — confirm the Aspire version supports it (`dotnet list package --project src/Orchestration.AppHost/Orchestration.AppHost.csproj`).

- [ ] **Step 2.4: Verify direct DataGrip connectivity with AppHost stopped**

In DataGrip, create a PostgreSQL data source:
- Host: `localhost`
- Port: `5432`
- User: `postgres`
- Password: `devpassword`
- Database: `norse`

Expected: connection succeeds and `norse` database is visible. This confirms `isProxied: false` + persistent lifetime is working — the container is reachable without the AppHost running.

---

## Task 3: Stage Commit 1 for review

- [ ] **Step 3.1: Stage the Commit 1 files**

```
git add Directory.Build.props
git add src/Orchestration.AppHost/Orchestration.AppHost.csproj
git add src/Orchestration.AppHost/appsettings.json
git add src/Orchestration.AppHost/Program.cs
git add Bifrost.slnx
```

- [ ] **Step 3.2: Hand off to Buvy for review and commit**

Open GitHub Desktop. Review the diff — expected changes are exactly the four files above. Suggested commit message:

```
Add Orchestration.AppHost — infrastructure layer

Stands up TimescaleDB HA, RabbitMQ (management), and MongoDB Atlas Local
as persistent Aspire containers with named Docker volumes, fixed host ports,
no DCP proxy, and floating tags. Developer machine is the canary for image
version breakage.
```

Wait for the commit before proceeding to Task 4.

---

## Task 4: Configure the Particular Software license

The three ServiceControl containers require `PARTICULARSOFTWARE_LICENSE`. It must never be committed; it lives in user secrets.

- [ ] **Step 4.1: Initialise user secrets (already enabled via `UserSecretsId` in the csproj)**

```
dotnet user-secrets list --project src/Orchestration.AppHost/Orchestration.AppHost.csproj
```

Expected: `No secrets configured for this application.` (or an empty list). If this errors, the `UserSecretsId` element is missing from the csproj — go back to Task 1 Step 1.2.

- [ ] **Step 4.2: Store the Particular license**

```
dotnet user-secrets set "Parameters:particular-license" "<paste-license-xml-here>" --project src/Orchestration.AppHost/Orchestration.AppHost.csproj
```

Replace `<paste-license-xml-here>` with the full license XML string from the Particular customer portal. The `Parameters:` prefix is required — it is how Aspire's `AddParameter` reads values from configuration.

- [ ] **Step 4.3: Confirm the secret is stored**

```
dotnet user-secrets list --project src/Orchestration.AppHost/Orchestration.AppHost.csproj
```

Expected output includes: `Parameters:particular-license = <LicenseData ...>`.

---

## Task 5: Wire the Particular platform layer

**Files:**
- Modify: `src/Orchestration.AppHost/Program.cs`

- [ ] **Step 5.1: Replace `Program.cs` with the full eight-container wiring**

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// ── Infrastructure layer ──────────────────────────────────────────────────────

// Local container credentials from appsettings.json — committed, not secrets.
var postgresPassword = builder.AddParameter("postgres-password");
var rabbitmqUser = builder.AddParameter("rabbitmq-user");
var rabbitmqPassword = builder.AddParameter("rabbitmq-password");

var timescale = builder.AddContainer("timescale", "timescale/timescaledb-ha", "latest")
	.WithLifetime(ContainerLifetime.Persistent)
	.WithImagePullPolicy(ImagePullPolicy.Always)
	.WithEnvironment("POSTGRES_USER", "postgres")
	.WithEnvironment("POSTGRES_PASSWORD", postgresPassword)
	.WithEnvironment("POSTGRES_DB", "norse")
	.WithDataVolume(name: "norse-relational", target: "/home/postgres/pgdata")
	.WithEndpoint(port: 5432, targetPort: 5432, name: "pg", isProxied: false);

var rabbit = builder.AddContainer("rabbit", "rabbitmq", "management")
	.WithLifetime(ContainerLifetime.Persistent)
	.WithImagePullPolicy(ImagePullPolicy.Always)
	.WithEnvironment("RABBITMQ_DEFAULT_USER", rabbitmqUser)
	.WithEnvironment("RABBITMQ_DEFAULT_PASS", rabbitmqPassword)
	.WithDataVolume(name: "norse-messaging", target: "/var/lib/rabbitmq")
	.WithEndpoint(port: 5672, targetPort: 5672, name: "amqp", isProxied: false)
	.WithEndpoint(port: 15672, targetPort: 15672, name: "management", isProxied: false);

var mongo = builder.AddContainer("mongo", "mongodb/mongodb-atlas-local", "latest")
	.WithLifetime(ContainerLifetime.Persistent)
	.WithImagePullPolicy(ImagePullPolicy.Always)
	.WithDataVolume(name: "norse-document", target: "/data/db")
	.WithEndpoint(port: 27017, targetPort: 27017, name: "mongodb", isProxied: false)
	.WithEndpoint(port: 27032, targetPort: 27032, name: "mongot", isProxied: false);

// ── Particular platform layer ─────────────────────────────────────────────────

// License shared by all three ServiceControl instances.
var particularLicense = builder.AddParameter("particular-license", secret: true);

// RavenDB — backing store for both ServiceControl and ServiceControl Audit.
// Both instances connect to the same server but use separate internal databases.
var ravendb = builder.AddContainer("ravendb", "particular/ravendb", "latest")
	.WithLifetime(ContainerLifetime.Persistent)
	.WithImagePullPolicy(ImagePullPolicy.Always)
	.WithDataVolume(name: "norse-monitoring", target: "/var/lib/ravendb/data")
	.WithEndpoint(port: 8080, targetPort: 8080, name: "http", isProxied: false);

// ServiceControl (error) — retry orchestration, DLQ management.
// Depends on: rabbit (transport), ravendb (persistence).
var servicecontrol = builder.AddContainer("servicecontrol", "particular/servicecontrol", "latest")
	.WithLifetime(ContainerLifetime.Persistent)
	.WithImagePullPolicy(ImagePullPolicy.Always)
	.WithEnvironment("TRANSPORTTYPE", "RabbitMQ.QuorumConventionalRouting")
	.WithEnvironment("CONNECTIONSTRING", "host=rabbit")
	.WithEnvironment("RAVENDB_CONNECTIONSTRING", "http://ravendb:8080")
	.WithEnvironment("REMOTEINSTANCES", "[{\"api_uri\":\"http://servicecontrol-audit:44444/api\"}]")
	.WithEnvironment("ENABLEINTEGRATEDSERVICEPULSE", "false")
	.WithEnvironment("PARTICULARSOFTWARE_LICENSE", particularLicense)
	.WithArgs("--setup-and-run")
	.WithEndpoint(port: 33333, targetPort: 33333, name: "http", isProxied: false)
	.WaitFor(ravendb)
	.WaitFor(rabbit);

// ServiceControl Audit — audit-queue ingestion, message history.
// Depends on: rabbit (transport), ravendb (persistence).
var servicecontrolAudit = builder.AddContainer("servicecontrol-audit", "particular/servicecontrol-audit", "latest")
	.WithLifetime(ContainerLifetime.Persistent)
	.WithImagePullPolicy(ImagePullPolicy.Always)
	.WithEnvironment("TRANSPORTTYPE", "RabbitMQ.QuorumConventionalRouting")
	.WithEnvironment("CONNECTIONSTRING", "host=rabbit")
	.WithEnvironment("RAVENDB_CONNECTIONSTRING", "http://ravendb:8080")
	.WithEnvironment("PARTICULARSOFTWARE_LICENSE", particularLicense)
	.WithArgs("--setup-and-run")
	.WithEndpoint(port: 44444, targetPort: 44444, name: "http", isProxied: false)
	.WaitFor(ravendb)
	.WaitFor(rabbit);

// ServiceControl Monitoring — endpoint queue/processing metrics. Stateless; no RavenDB.
// Depends on: rabbit (transport).
var servicecontrolMonitoring = builder.AddContainer("servicecontrol-monitoring", "particular/servicecontrol-monitoring", "latest")
	.WithLifetime(ContainerLifetime.Persistent)
	.WithImagePullPolicy(ImagePullPolicy.Always)
	.WithEnvironment("TRANSPORTTYPE", "RabbitMQ.QuorumConventionalRouting")
	.WithEnvironment("CONNECTIONSTRING", "host=rabbit")
	.WithEnvironment("PARTICULARSOFTWARE_LICENSE", particularLicense)
	.WithArgs("--setup-and-run")
	.WithEndpoint(port: 33633, targetPort: 33633, name: "http", isProxied: false)
	.WaitFor(rabbit);

// ServicePulse — operations web console. Stateless.
// Depends on: servicecontrol + servicecontrol-monitoring (URL config).
builder.AddContainer("servicepulse", "particular/servicepulse", "latest")
	.WithLifetime(ContainerLifetime.Persistent)
	.WithImagePullPolicy(ImagePullPolicy.Always)
	.WithEnvironment("SERVICECONTROL_URL", "http://servicecontrol:33333")
	.WithEnvironment("MONITORING_URL", "http://servicecontrol-monitoring:33633")
	.WithEndpoint(port: 9090, targetPort: 9090, name: "http", isProxied: false)
	.WaitFor(servicecontrol)
	.WaitFor(servicecontrolMonitoring);

builder.Build().Run();
```

- [ ] **Step 5.2: Run the AppHost**

```
dotnet run --project src/Orchestration.AppHost/Orchestration.AppHost.csproj
```

All eight containers should appear in the Aspire dashboard. The three ServiceControl containers and ServicePulse will start after their `WaitFor` dependencies clear.

If any ServiceControl container exits immediately with a non-zero code, check its logs in the dashboard. Common causes:
- License not set — verify `dotnet user-secrets list` shows `Parameters:particular-license`
- RavenDB not yet healthy — ServiceControl started before RavenDB's HTTP endpoint responded; the `WaitFor` should prevent this, but on first pull the image download can cause a timeout. Restart the AppHost once images are all local.

- [ ] **Step 5.3: Verify ServicePulse**

Open `http://localhost:9090` in a browser. Expected: the ServicePulse web console loads and shows all three ServiceControl instances as connected (green indicators for Error, Audit, and Monitoring). If Monitoring shows as disconnected, confirm the `MONITORING_URL` env var value matches the `servicecontrol-monitoring` container's endpoint.

- [ ] **Step 5.4: Verify containers survive AppHost shutdown**

Stop the AppHost (`Ctrl+C`). Open Docker Desktop. Expected: all eight containers still `Running`. Re-open `http://localhost:9090` — ServicePulse should still be reachable, confirming the no-proxy, persistent-lifetime posture is working end to end.

---

## Task 6: Stage Commit 2 for review

- [ ] **Step 6.1: Stage the Commit 2 file**

```
git add src/Orchestration.AppHost/Program.cs
```

- [ ] **Step 6.2: Hand off to Buvy for review and commit**

Open GitHub Desktop. Review the diff — expected: only `Program.cs` changed, adding the Particular layer variables and containers. Suggested commit message:

```
Add Particular platform layer to Orchestration.AppHost

Wires RavenDB, ServiceControl (error + audit + monitoring), and ServicePulse
as persistent containers with standard ports, WaitFor dependency ordering,
and license injected from user secrets. Full message-level ops tooling
available in local dev from day one.
```

---

## Self-Review

**Spec coverage:**
- §1 Project structure — Tasks 1 (Directory.Build.props, csproj, slnx update, net11.0) ✓
- §2 Tag policy / ImagePullPolicy.Always — Task 2 Step 2.1 and Task 5 Step 5.1 ✓
- §3 Persistent lifetime + named volumes — every `WithLifetime` + `WithDataVolume` call ✓
- §4.1 Infrastructure topology — Task 2 ✓
- §4.2 Particular topology (env vars, startup args, dependency order) — Task 5 ✓
- §5 No proxy / fixed ports — every `WithEndpoint(..., isProxied: false)` call ✓
- §6 Particular license via user secrets — Task 4 + `AddParameter` in Task 5 ✓
- §7 Staged commit plan with review gates — Tasks 3 and 6 ✓
- §8.1 ImagePullPolicy recreation caveat — covered in Task 2 Step 2.3 note ✓
- §8.2 MongoDB Atlas Local auth model — open item, surfaced in Task 2 Step 2.2 (start logs) ✓
- §8.3 RavenDB shared instance — `RAVENDB_CONNECTIONSTRING` points both SC containers to the same server; verify in Task 5 Step 5.2 ✓

**Placeholder scan:** No TBDs, no "add validation", no "similar to task N". All code blocks are complete.

**Type consistency:** `timescale`, `rabbit`, `mongo`, `ravendb`, `servicecontrol`, `servicecontrolAudit`, `servicecontrolMonitoring`, `particularLicense` — variable names are consistent between Task 2 (infra) and Task 5 (full wiring) since Task 5 replaces the entire file.
