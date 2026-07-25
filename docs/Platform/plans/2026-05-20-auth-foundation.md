# Auth Foundation Implementation Plan (Plan A of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development is the default — use it to implement this plan task-by-task; superpowers:executing-plans is the narrow fallback, only when the work specifically needs a separate session with human review checkpoints, never an interchangeable alternative. Pair either with superpowers:test-driven-development for every implementation task — orchestration sequences tasks, TDD governs how each one is coded. Steps use checkbox (`- [ ]`) syntax for tracking. **This plan halts at the plan stage during the spec-first phase; do not execute without explicit user greenlight.**

**Goal:** Stand up the `norse-auth` submodule and the foundational assemblies (`Norse.Auth.Contracts`, `Norse.Auth.Server` skeleton) that the rest of the auth-federation spec builds on. Deliver the `NorsePrincipal` envelope, the population/audience taxonomy, and the anonymous-bootstrap middleware that guarantees every Norse request carries a non-null principal.

**Architecture:** Three-assembly bounded context per CLAUDE.md §5 (`Norse.Auth.Contracts`, `Norse.Auth.Components`, `Norse.Auth.Server`). No `Norse.Auth.Worker` at V1 — Auth has no background services in Plan A's scope. The Contracts assembly is a pure value-types library — no server-side dependencies. The Server skeleton wires the anonymous-bootstrap cookie scheme and exposes the plugin entry point that downstream plans (B–E) extend. The Components assembly is scaffolded empty in this plan; sign-in UI lands in Plan D.

**Tech Stack:** .NET 10 / C# 13, ASP.NET Core 10 (cookie authentication + Data Protection), OpenIddict 6.x (server registration only in this plan), xUnit, Shouldly, NSubstitute. *(Amended 2026-06-03: no EF in this plan — Mongo is the identity system of record; the Postgres reporting projection lands with `Norse.Auth.Worker` in Plan B. See auth spec §3.)*

**Companion spec:** `docs/Platform/specs/2026-05-20-auth-federation-design.md`. Read §3 (architecture), §5 (principal model), and §7.0 (anonymous bootstrap) before starting. Every design decision is justified in the spec.

> **Amended 2026-06-03:** CLAUDE.md §7 #4 (tenancy) resolved as stamp-per-tenant (`docs/Platform/specs/2026-06-03-tenancy-model-design.md`). `NorsePrincipal` carries **no** `TenantId` and `ClaimNames` has **no** `nrs:tenant` — the code listings below have been updated; do not re-introduce them.
>
> **Also amended 2026-06-03 (identity storage):** Mongo is the identity system of record (OpenIddict first-party Mongo stores; custom ASP.NET Identity stores) — see auth spec §3. `Norse.Auth.Server` carries **no** EF reference; Task 20 is void; the Postgres `auth` schema is a reporting projection owned by `Norse.Auth.Worker` (Plan B).
>
> **Amendment (2026-07-25):** this direction was not taken. The `norse-auth` submodule and its `NorsePrincipal`/`Population`/`IAccountApi` shape never shipped — the auth realm was rebuilt as Heimdall (`Norse.AuthN.*`) on Himinbjörg (`Norse.Identity.*`), with `IAuthenticationService` (`[GenerateGateway]`, `Login`/`Register`/`Logout`) returning `Outcome<T>` directly. See `../../../../Heimdall/CLAUDE.md`.

---

## Plan Sequence

This is **Plan A of 5** for the auth-federation spec. Sibling plans (not yet written):

- **Plan B — Identity Stores + Migrations:** *(re-scoped 2026-06-03 — Mongo is the identity system of record; see auth spec §3)* Mongo identity stores (OpenIddict first-party MongoDB stores; custom ASP.NET Identity `IUserStore`/`IRoleStore` impls), the `Norse.Auth.Worker` reporting projection (Postgres `auth` schema, entity classes + `IEntityTypeConfiguration<T>` impls, EF migrations for the projected tables), signing-key storage + rotation, JWKS publication.
- **Plan C — OAuth Surface:** OpenIddict server configuration, token endpoint, discovery endpoints, JWT issuance, refresh-rotation with reuse detection, MCP pre-registered client configuration.
- **Plan D — Sign-in Flows:** Staff (Google OIDC federation), Producer (invite-only enrollment + sign-in), Customer (registration, magic-link, social-additive linking), M2M (client credentials grant).
- **Plan E — Cross-Cutting Policies:** Rate limiting, email normalization with plus-addressing config, account-linking strictness rules, signout semantics, MFA enforcement per population, audit-event publication.

This plan halts after the foundation. Plans B–E will be written when the user signals the spec phase is complete (or when explicitly requested earlier).

---

## Prerequisites

The plan's tasks are tagged by readiness:

- **🟢 Ready now:** dependencies in place. Tasks here are execution-ready today.
- **🟡 Awaits prerequisite spec/plan:** task is fully specified but depends on a sibling component without a spec yet.

The following sibling components need specs (and plans) before the 🟡 tasks in this plan can execute. Their absence does **not** block writing this plan, but it does block execution.

| Component | Why this plan needs it | Tasks blocked |
|---|---|---|
| `Norse.Abstractions.Hosting` (spec + plan) | Defines `IWebHostPlugin` — `AuthPlugin` implements it directly (no product-tier wrapper) | 5, 6, 16 |
| `Norse.Hosting.Web.Server` (spec + plan) | Hosts the plugin runtime; defines middleware extension surface | 16, 19, 20 |
| `norse-infrastructure-persistence` / per-service DbContext family (spec + plan) | The Auth DbContext (`AuthDbContext`) is owned by `Norse.Infrastructure.Persistence` and backs only the Postgres reporting projection — `Norse.Auth.Worker` (not `.Server`) ships the projected entity classes + `IEntityTypeConfiguration<T>` impls, Infrastructure scans and wires up. *(Amended 2026-06-03: Mongo is the identity system of record; see auth spec §3.)* | 13 |
| UUID v5 namespace registry (future Primitives spec) | Anonymous bootstrap uses UUID v5 under the auth-context namespace | 9 |

Where a 🟡 task is reached, the executing engineer should pause, raise the prerequisite gap, and wait for the corresponding plan to complete. Tasks 1–8 and 11–12 are 🟢 — they form a usable first commit (~1.5 days of work) that proves out the Contracts assembly and the test infrastructure.

**Per CLAUDE.md §8:** *No automatic git commits.* Every "Stage" step ends with `git add` only. The human runs `git commit` after reviewing the diff. Each task includes a proposed commit message for that review.

---

## File Structure

All paths relative to the meta-repo root.

```
norse-auth/                                            # NEW: subdirectory (future submodule)
├── .editorconfig                                        # NEW: tabs, 2-space width
├── .gitignore                                           # NEW: dotnet defaults
├── Directory.Build.props                                # NEW: context-local overrides
├── LICENSE                                              # NEW: MIT
├── README.md                                            # NEW: usage notes
├── Norse.Auth.slnx                                    # NEW: solution (XML .slnx per [[feedback_solution_file_format]])
├── src/
│   ├── Norse.Auth.Contracts/
│   │   ├── Norse.Auth.Contracts.csproj                # NEW
│   │   ├── Population.cs                                # NEW: enum
│   │   ├── Audience.cs                                  # NEW: enum
│   │   ├── ClaimNames.cs                                # NEW: static constants
│   │   ├── NorsePrincipal.cs                          # NEW: typed envelope
│   │   ├── NorsePrincipalFactory.cs                   # NEW: builds principals from ClaimsPrincipal
│   │   └── PrincipalSource.cs                           # NEW: enum { Cookie, Jwt }
│   ├── Norse.Auth.Components/
│   │   └── Norse.Auth.Components.csproj               # NEW: empty placeholder (UI lands in Plan D)
│   └── Norse.Auth.Server/
│       ├── Norse.Auth.Server.csproj           # NEW
│       ├── AuthOptions.cs                               # NEW: bound configuration
│       ├── AnonymousBootstrap/
│       │   ├── AnonymousCookieAuthenticationHandler.cs  # NEW
│       │   ├── AnonymousCookieDefaults.cs               # NEW
│       │   ├── AnonymousPrincipalGenerator.cs           # NEW
│       │   └── AnonymousBootstrapOptions.cs             # NEW
│       ├── AuthEntityConfigurations.cs                  # ~~NEW~~ VOID (2026-06-03): EF artifacts may not live in .Server; projected entity configs land in Norse.Auth.Worker in Plan B (auth spec §3)
│       └── AuthPlugin.cs                                # NEW: 🟡 awaits Norse.Abstractions.Hosting.IWebHostPlugin
└── tests/
    ├── Norse.Auth.Contracts.Tests/
    │   ├── Norse.Auth.Contracts.Tests.csproj          # NEW
    │   ├── PopulationTests.cs
    │   ├── AudienceTests.cs
    │   ├── ClaimNamesTests.cs
    │   ├── NorsePrincipalTests.cs
    │   └── NorsePrincipalFactoryTests.cs
    └── Norse.Auth.Server.Tests/
        ├── Norse.Auth.Server.Tests.csproj     # NEW
        ├── AuthOptionsTests.cs
        ├── AnonymousPrincipalGeneratorTests.cs
        ├── AnonymousCookieAuthenticationHandlerTests.cs
        └── EndToEnd/
            └── AnonymousBootstrapEndToEndTests.cs       # 🟡 awaits Norse.Hosting.Web.Server
```

---

## Phase 1 — Submodule scaffolding 🟢

### Task 1: Verify .NET 10 SDK and create the norse-auth subdirectory

**Files:**
- Create: `norse-auth/` (directory)

- [ ] **Step 1: Verify SDK presence**

Run: `dotnet --list-sdks`

Expected: at least one entry starting with `10.0.` (e.g., `10.0.100`). If absent, install the latest .NET 10 SDK before proceeding.

- [ ] **Step 2: Create the subdirectory**

Run: `mkdir norse-auth`

Expected: directory `norse-auth\` exists.

- [ ] **Step 3: Stage**

```
git add norse-auth
```

Proposed commit message: `feat(auth): create norse-auth subdirectory for the Auth bounded context`

### Task 2: Add `.gitignore`, `.editorconfig`, `Directory.Build.props`

**Files:**
- Create: `norse-auth/.gitignore`
- Create: `norse-auth/.editorconfig`
- Create: `norse-auth/Directory.Build.props`

- [ ] **Step 1: Write `norse-auth/.gitignore`**

```
bin/
obj/
*.user
.vs/
.idea/
*.suo
TestResults/
.test/
```

- [ ] **Step 2: Write `norse-auth/.editorconfig`**

```
root = false

[*]
indent_style = tab
indent_size = 2
end_of_line = crlf
insert_final_newline = true
charset = utf-8-bom

[*.{md,yml,yaml}]
indent_style = space
indent_size = 2

[*.{cs,csproj,props,targets,json,slnx}]
indent_style = tab
indent_size = 2

[*.cs]
dotnet_style_qualification_for_field = false:warning
dotnet_style_qualification_for_property = false:warning
dotnet_style_qualification_for_method = false:warning
dotnet_style_qualification_for_event = false:warning
csharp_style_var_for_built_in_types = false:warning
csharp_style_var_when_type_is_apparent = true:warning
csharp_style_var_elsewhere = false:warning
dotnet_diagnostic.CS8618.severity = error
dotnet_diagnostic.CS8625.severity = error
dotnet_diagnostic.CS8602.severity = error
dotnet_diagnostic.CS8604.severity = error
```

- [ ] **Step 3: Write `norse-auth/Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <RootNamespace>$(MSBuildProjectName)</RootNamespace>
    <NeutralLanguage>en-US</NeutralLanguage>
    <Authors>{Company} Insurance</Authors>
    <Company>{Company}</Company>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Stage**

```
git add norse-auth/.gitignore norse-auth/.editorconfig norse-auth/Directory.Build.props
```

Proposed commit message: `feat(auth): scaffold .gitignore, .editorconfig, Directory.Build.props for norse-auth`

### Task 3: Add `LICENSE` and `README.md`

**Files:**
- Create: `norse-auth/LICENSE`
- Create: `norse-auth/README.md`

- [ ] **Step 1: Write `norse-auth/LICENSE`** (MIT, standard text)

```
MIT License

Copyright (c) 2026 {Company} Insurance

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- [ ] **Step 2: Write `norse-auth/README.md`**

```markdown
# Norse.Auth

The Auth bounded context for the Norse platform. Implements the federation
topology and principal model described in
`docs/Platform/specs/2026-05-20-auth-federation-design.md`.

## Assemblies

- **Norse.Auth.Contracts** — `NorsePrincipal` envelope, `Population` and
  `Audience` enums, claim-name constants. Pure value types. No infrastructure
  dependencies. Other contexts reference this to read the principal off
  `HttpContext.User` without string-keyed claim lookups.
- **Norse.Auth.Components** — Blazor sign-in pages, account-settings screens,
  agency-admin user-management screens. WASM-bundlable; no infrastructure
  references.
- **Norse.Auth.Server** — OpenIddict server configuration, EF stores,
  federation handlers, anonymous-bootstrap middleware, the
  `AuthPlugin : Norse.Abstractions.Hosting.IWebHostPlugin`.

## Status

Plan A of 5 (foundation). Plans B–E (stores/migrations, OAuth surface,
sign-in flows, cross-cutting policies) follow.
```

- [ ] **Step 3: Stage**

```
git add norse-auth/LICENSE norse-auth/README.md
```

Proposed commit message: `docs(auth): add LICENSE and README for norse-auth`

### Task 4: Create the solution file

**Files:**
- Create: `norse-auth/Norse.Auth.slnx`

- [ ] **Step 1: Create the solution**

Run from `norse-auth/`:

```
dotnet new sln --name Norse.Auth --format slnx
```

Expected: `Norse.Auth.slnx` exists in `norse-auth/`.

- [ ] **Step 2: Verify it's XML `.slnx`, not legacy `.sln`**

Run: `Get-Content norse-auth/Norse.Auth.slnx -TotalCount 3`

Expected: first line starts with `<Solution>` (or an XML declaration), confirming the `.slnx` format. Per `[[feedback_solution_file_format]]`, only `.slnx` is supported across the ecosystem.

- [ ] **Step 3: Stage**

```
git add norse-auth/Norse.Auth.slnx
```

Proposed commit message: `feat(auth): create Norse.Auth.slnx solution file`

---

## Phase 2 — `Norse.Auth.Contracts` 🟢

### Task 5: Create the Contracts project

**Files:**
- Create: `norse-auth/src/Norse.Auth.Contracts/Norse.Auth.Contracts.csproj`

- [ ] **Step 1: Create the project**

Run from `norse-auth/`:

```
dotnet new classlib --name Norse.Auth.Contracts --output src/Norse.Auth.Contracts --framework net10.0
```

- [ ] **Step 2: Remove the auto-generated `Class1.cs`**

Run: `Remove-Item norse-auth/src/Norse.Auth.Contracts/Class1.cs`

- [ ] **Step 3: Edit the csproj to add the explicit shape we want**

Replace `norse-auth/src/Norse.Auth.Contracts/Norse.Auth.Contracts.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>Contracts for the {Company} Auth bounded context: NorsePrincipal, Population, Audience, claim names. Pure value types; no infrastructure dependencies.</Description>
    <IsPackable>true</IsPackable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Add the project to the solution**

Run from `norse-auth/`:

```
dotnet sln Norse.Auth.slnx add src/Norse.Auth.Contracts/Norse.Auth.Contracts.csproj
```

- [ ] **Step 5: Verify it builds**

Run from `norse-auth/`:

```
dotnet build src/Norse.Auth.Contracts/Norse.Auth.Contracts.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 6: Stage**

```
git add norse-auth/src/Norse.Auth.Contracts norse-auth/Norse.Auth.slnx
```

Proposed commit message: `feat(auth-contracts): scaffold Norse.Auth.Contracts project`

### Task 6: `Population` enum

**Files:**
- Create: `norse-auth/src/Norse.Auth.Contracts/Population.cs`
- Create: `norse-auth/tests/Norse.Auth.Contracts.Tests/PopulationTests.cs` (deferred — written in Task 11)

Per spec §5.3, populations are mutually exclusive. Explicit integer values per CLAUDE.md §5 ("Explicit values are required on every enum member"). `0` reserved for "unspecified" — real states start at `1`.

- [ ] **Step 1: Write `Population.cs`**

```csharp
namespace Norse.Auth.Contracts;

/// <summary>
/// Mutually-exclusive identity classes for any principal flowing through the Norse platform.
/// A principal has exactly one population; transitions require a server-side action
/// (registration, role assignment) producing a new principal binding.
/// </summary>
public enum Population
{
	/// <summary>Reserved sentinel. Never assigned in practice; presence indicates a serialization bug.</summary>
	Unspecified = 0,

	/// <summary>First-contact visitor. Stable UUID v5 id; signed cookie; no real authority.</summary>
	Anonymous = 1,

	/// <summary>{Company} internal employee. Federated from Google Workspace.</summary>
	Staff = 2,

	/// <summary>Producer (agent/broker) acting on behalf of a bound agency.</summary>
	Producer = 3,

	/// <summary>Retail policyholder. Local credentials primary; social-additive linking allowed.</summary>
	Customer = 4,

	/// <summary>Machine consumer (client credentials). No human; per-client scopes.</summary>
	Machine = 5,
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-auth/`:

```
dotnet build src/Norse.Auth.Contracts/Norse.Auth.Contracts.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-auth/src/Norse.Auth.Contracts/Population.cs
```

Proposed commit message: `feat(auth-contracts): add Population enum with explicit values`

### Task 7: `Audience` enum

**Files:**
- Create: `norse-auth/src/Norse.Auth.Contracts/Audience.cs`

Per spec §6.1 and §7.6, audiences discriminate between token use-cases. The `MgaMcp` audience is distinct from `MgaStaff` even when the same staff principal is the subject — MCP tokens have narrower scope and potentially different lifetimes.

- [ ] **Step 1: Write `Audience.cs`**

```csharp
namespace Norse.Auth.Contracts;

/// <summary>
/// Token audience taxonomy. Embedded in the <c>aud</c> claim of every JWT
/// and the <c>aud</c> property of every signed cookie. Resource servers
/// validate audience on every request — a token issued for one audience
/// MUST NOT be accepted by a different audience's resource server.
/// </summary>
public enum Audience
{
	/// <summary>Reserved sentinel.</summary>
	Unspecified = 0,

	/// <summary>Anonymous browser cookie. No JWT form.</summary>
	MgaAnonymous = 1,

	/// <summary>Staff portal cookie + staff-facing API JWT.</summary>
	MgaStaff = 2,

	/// <summary>Producer portal cookie + producer-facing API JWT.</summary>
	MgaProducer = 3,

	/// <summary>Customer portal cookie + customer-facing API JWT.</summary>
	MgaCustomer = 4,

	/// <summary>M2M client-credentials JWT. No cookie form.</summary>
	MgaMachine = 5,

	/// <summary>MCP resource-server JWT. Distinct from MgaStaff to allow narrower scope and separate lifetimes.</summary>
	MgaMcp = 6,
}

/// <summary>
/// Extensions for converting <see cref="Audience"/> to and from the canonical
/// string form used in tokens (e.g., <c>mga.staff</c>, <c>mga.mcp</c>).
/// </summary>
public static class AudienceExtensions
{
	public static string ToCanonicalString(this Audience audience) => audience switch
	{
		Audience.MgaAnonymous => "mga.anonymous",
		Audience.MgaStaff => "mga.staff",
		Audience.MgaProducer => "mga.producer",
		Audience.MgaCustomer => "mga.customer",
		Audience.MgaMachine => "mga.machine",
		Audience.MgaMcp => "mga.mcp",
		Audience.Unspecified => throw new ArgumentException("Audience.Unspecified is a sentinel and has no canonical string.", nameof(audience)),
		_ => throw new ArgumentOutOfRangeException(nameof(audience), audience, "Unknown audience value."),
	};

	public static Audience FromCanonicalString(string canonical) => canonical switch
	{
		"mga.anonymous" => Audience.MgaAnonymous,
		"mga.staff" => Audience.MgaStaff,
		"mga.producer" => Audience.MgaProducer,
		"mga.customer" => Audience.MgaCustomer,
		"mga.machine" => Audience.MgaMachine,
		"mga.mcp" => Audience.MgaMcp,
		_ => throw new ArgumentException($"Unknown audience canonical string: '{canonical}'.", nameof(canonical)),
	};
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-auth/`:

```
dotnet build src/Norse.Auth.Contracts/Norse.Auth.Contracts.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-auth/src/Norse.Auth.Contracts/Audience.cs
```

Proposed commit message: `feat(auth-contracts): add Audience enum with canonical string conversions`

### Task 8: `PrincipalSource` enum and `ClaimNames` constants

**Files:**
- Create: `norse-auth/src/Norse.Auth.Contracts/PrincipalSource.cs`
- Create: `norse-auth/src/Norse.Auth.Contracts/ClaimNames.cs`

- [ ] **Step 1: Write `PrincipalSource.cs`**

```csharp
namespace Norse.Auth.Contracts;

/// <summary>
/// Discriminates the on-the-wire form the principal arrived in. Used for diagnostic logging
/// and for invariants that depend on transport (e.g., refresh-token rotation applies only to
/// <see cref="Jwt"/>; antiforgery applies only to <see cref="Cookie"/>).
/// </summary>
public enum PrincipalSource
{
	Unspecified = 0,
	Cookie = 1,
	Jwt = 2,
}
```

- [ ] **Step 2: Write `ClaimNames.cs`**

Claim names are *not* configurable. They're part of the wire contract — changing them is a breaking change to every consumer.

```csharp
namespace Norse.Auth.Contracts;

/// <summary>
/// Canonical claim names for tokens and cookies issued by Norse.Auth.
/// These are part of the wire contract; they MUST NOT be changed without a coordinated
/// migration across every resource server.
/// </summary>
public static class ClaimNames
{
	/// <summary>The principal's UUID v5 identifier. Stable across registration (anonymous → customer).</summary>
	public const string PrincipalId = "nrs:pid";

	/// <summary>The principal's population. Single-valued. See <see cref="Population"/>.</summary>
	public const string Population = "nrs:pop";

	/// <summary>The token/cookie audience. Single-valued. See <see cref="Audience"/>.</summary>
	public const string Audience = "nrs:aud";

	/// <summary>Population-scoped role. Multi-valued; empty for anonymous principals.</summary>
	public const string Role = "nrs:role";

	/// <summary>Agency identifier — Producer / Machine populations only.</summary>
	public const string AgencyId = "nrs:agency";

	/// <summary>Customer identifier — Customer population only. Equal to <see cref="PrincipalId"/> by construction.</summary>
	public const string CustomerId = "nrs:customer";

	/// <summary>Token/cookie issued-at timestamp (Unix seconds).</summary>
	public const string IssuedAt = "iat";

	/// <summary>Token/cookie expires-at timestamp (Unix seconds).</summary>
	public const string ExpiresAt = "exp";

	/// <summary>Standard JWT subject claim. Norse.Auth stamps this with the canonical string of <see cref="PrincipalId"/>.</summary>
	public const string Subject = "sub";
}
```

- [ ] **Step 3: Verify it builds**

Run from `norse-auth/`:

```
dotnet build src/Norse.Auth.Contracts/Norse.Auth.Contracts.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 4: Stage**

```
git add norse-auth/src/Norse.Auth.Contracts/PrincipalSource.cs norse-auth/src/Norse.Auth.Contracts/ClaimNames.cs
```

Proposed commit message: `feat(auth-contracts): add PrincipalSource enum and ClaimNames constants`

### Task 9: `NorsePrincipal` envelope

**Files:**
- Create: `norse-auth/src/Norse.Auth.Contracts/NorsePrincipal.cs`

This is the load-bearing type. Downstream contexts read `principal.Population` rather than string-keyed claim lookups. The envelope wraps a `ClaimsPrincipal` (it does *not* derive from it — we want a closed, typed surface, not the inherited string-claim API).

- [ ] **Step 1: Write `NorsePrincipal.cs`**

```csharp
using System.Collections.Frozen;
using System.Security.Claims;

namespace Norse.Auth.Contracts;

/// <summary>
/// Typed envelope over the <see cref="ClaimsPrincipal"/> attached to every Norse request.
/// Every Norse surface — portal page load, JSON API call, gRPC service, message handler —
/// receives a non-null instance. <c>[AllowAnonymous]</c> does not exist in the platform;
/// the <c>YGG110</c> analyzer (see norse-primitives-architecture) enforces this at build time.
/// </summary>
public sealed class NorsePrincipal
{
	public NorsePrincipal(
		Guid principalId,
		Population population,
		Audience audience,
		FrozenSet<string> roles,
		PrincipalSource source,
		DateTimeOffset issuedAt,
		DateTimeOffset expiresAt,
		Guid? agencyId = null,
		Guid? customerId = null,
		ClaimsPrincipal? underlying = null)
	{
		if (principalId == Guid.Empty)
			throw new ArgumentException("PrincipalId must be non-empty.", nameof(principalId));
		if (population == Population.Unspecified)
			throw new ArgumentException("Population must not be Unspecified.", nameof(population));
		if (audience == Audience.Unspecified)
			throw new ArgumentException("Audience must not be Unspecified.", nameof(audience));
		if (source == PrincipalSource.Unspecified)
			throw new ArgumentException("Source must not be Unspecified.", nameof(source));
		if (expiresAt <= issuedAt)
			throw new ArgumentException("ExpiresAt must be after IssuedAt.", nameof(expiresAt));

		// Population-coherence checks: invariants the spec commits to.
		if (population == Population.Customer && customerId is null)
			throw new ArgumentException("Customer principals must carry a non-null CustomerId.", nameof(customerId));
		if (population == Population.Customer && customerId != principalId)
			throw new ArgumentException("Customer.CustomerId must equal PrincipalId by construction (spec §5.4).", nameof(customerId));
		if (population is Population.Producer or Population.Machine && agencyId is null)
			throw new ArgumentException($"{population} principals must carry a non-null AgencyId.", nameof(agencyId));
		if (population == Population.Anonymous && roles.Count > 0)
			throw new ArgumentException("Anonymous principals must have an empty role set (spec §5.3).", nameof(roles));

		PrincipalId = principalId;
		Population = population;
		Audience = audience;
		Roles = roles;
		Source = source;
		IssuedAt = issuedAt;
		ExpiresAt = expiresAt;
		AgencyId = agencyId;
		CustomerId = customerId;
		Underlying = underlying;
	}

	public Guid PrincipalId { get; }
	public Population Population { get; }
	public Audience Audience { get; }
	public FrozenSet<string> Roles { get; }
	public PrincipalSource Source { get; }
	public DateTimeOffset IssuedAt { get; }
	public DateTimeOffset ExpiresAt { get; }
	public Guid? AgencyId { get; }
	public Guid? CustomerId { get; }

	/// <summary>
	/// The underlying <see cref="ClaimsPrincipal"/>, if available. Provided as an escape hatch
	/// for interop with framework APIs (ASP.NET authorization policies, antiforgery). Downstream
	/// code SHOULD prefer the typed properties on this envelope.
	/// </summary>
	public ClaimsPrincipal? Underlying { get; }

	public bool IsAnonymous => Population == Population.Anonymous;

	public bool IsInRole(string role) => Roles.Contains(role);
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-auth/`:

```
dotnet build src/Norse.Auth.Contracts/Norse.Auth.Contracts.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-auth/src/Norse.Auth.Contracts/NorsePrincipal.cs
```

Proposed commit message: `feat(auth-contracts): add NorsePrincipal envelope with construction-time invariants`

### Task 10: `NorsePrincipalFactory` — build envelopes from `ClaimsPrincipal`

**Files:**
- Create: `norse-auth/src/Norse.Auth.Contracts/NorsePrincipalFactory.cs`

The factory is what bridges from the framework's `ClaimsPrincipal` (which is what cookie / JwtBearer middleware produces) to our typed envelope. Failures are loud — per CLAUDE.md §2.7, a malformed token never produces a silent default.

- [ ] **Step 1: Write `NorsePrincipalFactory.cs`**

```csharp
using System.Collections.Frozen;
using System.Globalization;
using System.Security.Claims;

namespace Norse.Auth.Contracts;

/// <summary>
/// Builds a <see cref="NorsePrincipal"/> from a <see cref="ClaimsPrincipal"/> emitted by
/// the cookie or JWT middleware. Fails loudly on missing/malformed required claims —
/// the spec forbids silent fallback (CLAUDE.md §2.7).
/// </summary>
public static class NorsePrincipalFactory
{
	public static NorsePrincipal FromClaimsPrincipal(ClaimsPrincipal claimsPrincipal, PrincipalSource source)
	{
		ArgumentNullException.ThrowIfNull(claimsPrincipal);
		if (source == PrincipalSource.Unspecified)
			throw new ArgumentException("Source must not be Unspecified.", nameof(source));

		string principalIdString = RequireSingle(claimsPrincipal, ClaimNames.PrincipalId);
		if (!Guid.TryParse(principalIdString, out var principalId) || principalId == Guid.Empty)
			throw new InvalidNorsePrincipalException($"Claim '{ClaimNames.PrincipalId}' is not a valid non-empty GUID: '{principalIdString}'.");

		string populationString = RequireSingle(claimsPrincipal, ClaimNames.Population);
		if (!Enum.TryParse<Population>(populationString, ignoreCase: false, out var population) || population == Population.Unspecified)
			throw new InvalidNorsePrincipalException($"Claim '{ClaimNames.Population}' is not a valid Population: '{populationString}'.");

		string audienceString = RequireSingle(claimsPrincipal, ClaimNames.Audience);
		Audience audience;
		try { audience = AudienceExtensions.FromCanonicalString(audienceString); }
		catch (ArgumentException ex)
		{
			throw new InvalidNorsePrincipalException($"Claim '{ClaimNames.Audience}' is not a valid canonical audience: '{audienceString}'.", ex);
		}

		FrozenSet<string> roles = claimsPrincipal.FindAll(ClaimNames.Role)
			.Select(c => c.Value)
			.ToFrozenSet(StringComparer.Ordinal);

		DateTimeOffset issuedAt = RequireUnixSeconds(claimsPrincipal, ClaimNames.IssuedAt);
		DateTimeOffset expiresAt = RequireUnixSeconds(claimsPrincipal, ClaimNames.ExpiresAt);

		Guid? agencyId = OptionalGuid(claimsPrincipal, ClaimNames.AgencyId);
		Guid? customerId = OptionalGuid(claimsPrincipal, ClaimNames.CustomerId);

		return new NorsePrincipal(
			principalId: principalId,
			population: population,
			audience: audience,
			roles: roles,
			source: source,
			issuedAt: issuedAt,
			expiresAt: expiresAt,
			agencyId: agencyId,
			customerId: customerId,
			underlying: claimsPrincipal);
	}

	private static string RequireSingle(ClaimsPrincipal claimsPrincipal, string claimType)
	{
		Claim[] claims = claimsPrincipal.FindAll(claimType).ToArray();
		if (claims.Length == 0)
			throw new InvalidNorsePrincipalException($"Claim '{claimType}' is missing.");
		if (claims.Length > 1)
			throw new InvalidNorsePrincipalException($"Claim '{claimType}' must be single-valued; found {claims.Length} values.");
		return claims[0].Value;
	}

	private static DateTimeOffset RequireUnixSeconds(ClaimsPrincipal claimsPrincipal, string claimType)
	{
		string value = RequireSingle(claimsPrincipal, claimType);
		if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds))
			throw new InvalidNorsePrincipalException($"Claim '{claimType}' is not a valid Unix-seconds integer: '{value}'.");
		return DateTimeOffset.FromUnixTimeSeconds(seconds);
	}

	private static Guid? OptionalGuid(ClaimsPrincipal claimsPrincipal, string claimType)
	{
		Claim[] claims = claimsPrincipal.FindAll(claimType).ToArray();
		if (claims.Length == 0)
			return null;
		if (claims.Length > 1)
			throw new InvalidNorsePrincipalException($"Optional claim '{claimType}' must be single-valued; found {claims.Length} values.");
		if (!Guid.TryParse(claims[0].Value, out var guid) || guid == Guid.Empty)
			throw new InvalidNorsePrincipalException($"Optional claim '{claimType}' is not a valid non-empty GUID: '{claims[0].Value}'.");
		return guid;
	}
}

/// <summary>
/// Thrown when a <see cref="ClaimsPrincipal"/> cannot be projected onto a valid
/// <see cref="NorsePrincipal"/>. Always indicates a wire-contract violation, not user error.
/// </summary>
public sealed class InvalidNorsePrincipalException : Exception
{
	public InvalidNorsePrincipalException(string message) : base(message) { }
	public InvalidNorsePrincipalException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-auth/`:

```
dotnet build src/Norse.Auth.Contracts/Norse.Auth.Contracts.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-auth/src/Norse.Auth.Contracts/NorsePrincipalFactory.cs
```

Proposed commit message: `feat(auth-contracts): add NorsePrincipalFactory with strict claim validation`

---

## Phase 3 — `Norse.Auth.Contracts.Tests` 🟢

### Task 11: Create the Contracts test project

**Files:**
- Create: `norse-auth/tests/Norse.Auth.Contracts.Tests/Norse.Auth.Contracts.Tests.csproj`

- [ ] **Step 1: Create the test project**

Run from `norse-auth/`:

```
dotnet new xunit --name Norse.Auth.Contracts.Tests --output tests/Norse.Auth.Contracts.Tests --framework net10.0
```

- [ ] **Step 2: Remove the auto-generated test class**

Run: `Remove-Item norse-auth/tests/Norse.Auth.Contracts.Tests/UnitTest1.cs`

- [ ] **Step 3: Replace the csproj contents**

Replace `norse-auth/tests/Norse.Auth.Contracts.Tests/Norse.Auth.Contracts.Tests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Shouldly" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Norse.Auth.Contracts\Norse.Auth.Contracts.csproj" />
  </ItemGroup>
</Project>
```

Note: package versions are governed centrally; this csproj does not pin them. The `Directory.Packages.props` for `norse-auth` ships in a later task once Plan B's persistence work settles which test packages graduate to centrally-managed status. Until then, NuGet restore uses the latest stable.

- [ ] **Step 4: Add to the solution**

Run from `norse-auth/`:

```
dotnet sln Norse.Auth.slnx add tests/Norse.Auth.Contracts.Tests/Norse.Auth.Contracts.Tests.csproj
```

- [ ] **Step 5: Verify it builds**

Run from `norse-auth/`:

```
dotnet build tests/Norse.Auth.Contracts.Tests/Norse.Auth.Contracts.Tests.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 6: Stage**

```
git add norse-auth/tests/Norse.Auth.Contracts.Tests norse-auth/Norse.Auth.slnx
```

Proposed commit message: `test(auth-contracts): scaffold Norse.Auth.Contracts.Tests xUnit project`

### Task 12: Tests for `Population`, `Audience`, `ClaimNames`

**Files:**
- Create: `norse-auth/tests/Norse.Auth.Contracts.Tests/PopulationTests.cs`
- Create: `norse-auth/tests/Norse.Auth.Contracts.Tests/AudienceTests.cs`
- Create: `norse-auth/tests/Norse.Auth.Contracts.Tests/ClaimNamesTests.cs`

Each enum test guards the invariant that explicit values never change (CLAUDE.md §5: reordering enum members must be a no-op for any persisted value).

- [ ] **Step 1: Write `PopulationTests.cs`**

```csharp
using Norse.Auth.Contracts;
using Shouldly;
using Xunit;

namespace Norse.Auth.Contracts.Tests;

public sealed class PopulationTests
{
	[Theory]
	[InlineData(Population.Unspecified, 0)]
	[InlineData(Population.Anonymous, 1)]
	[InlineData(Population.Staff, 2)]
	[InlineData(Population.Producer, 3)]
	[InlineData(Population.Customer, 4)]
	[InlineData(Population.Machine, 5)]
	public void Each_member_has_the_expected_integer_value(Population member, int expected)
	{
		((int)member).ShouldBe(expected);
	}

	[Fact]
	public void Member_count_is_six_so_a_silent_addition_breaks_this_test()
	{
		Enum.GetValues<Population>().Length.ShouldBe(6);
	}

	[Fact]
	public void Zero_is_Unspecified_so_a_default_value_is_visibly_a_sentinel()
	{
		default(Population).ShouldBe(Population.Unspecified);
	}
}
```

- [ ] **Step 2: Write `AudienceTests.cs`**

```csharp
using Norse.Auth.Contracts;
using Shouldly;
using Xunit;

namespace Norse.Auth.Contracts.Tests;

public sealed class AudienceTests
{
	[Theory]
	[InlineData(Audience.Unspecified, 0)]
	[InlineData(Audience.MgaAnonymous, 1)]
	[InlineData(Audience.MgaStaff, 2)]
	[InlineData(Audience.MgaProducer, 3)]
	[InlineData(Audience.MgaCustomer, 4)]
	[InlineData(Audience.MgaMachine, 5)]
	[InlineData(Audience.MgaMcp, 6)]
	public void Each_member_has_the_expected_integer_value(Audience member, int expected)
	{
		((int)member).ShouldBe(expected);
	}

	[Theory]
	[InlineData(Audience.MgaAnonymous, "mga.anonymous")]
	[InlineData(Audience.MgaStaff, "mga.staff")]
	[InlineData(Audience.MgaProducer, "mga.producer")]
	[InlineData(Audience.MgaCustomer, "mga.customer")]
	[InlineData(Audience.MgaMachine, "mga.machine")]
	[InlineData(Audience.MgaMcp, "mga.mcp")]
	public void Canonical_string_roundtrip(Audience audience, string canonical)
	{
		audience.ToCanonicalString().ShouldBe(canonical);
		AudienceExtensions.FromCanonicalString(canonical).ShouldBe(audience);
	}

	[Fact]
	public void Unspecified_throws_on_ToCanonicalString_because_it_is_a_sentinel()
	{
		Should.Throw<ArgumentException>(() => Audience.Unspecified.ToCanonicalString());
	}

	[Fact]
	public void Unknown_canonical_string_throws()
	{
		Should.Throw<ArgumentException>(() => AudienceExtensions.FromCanonicalString("mga.bogus"));
	}
}
```

- [ ] **Step 3: Write `ClaimNamesTests.cs`**

The point of this test is purely to catch accidental rename. Every claim name is part of the wire contract.

```csharp
using Norse.Auth.Contracts;
using Shouldly;
using Xunit;

namespace Norse.Auth.Contracts.Tests;

public sealed class ClaimNamesTests
{
	[Theory]
	[InlineData(nameof(ClaimNames.PrincipalId), "nrs:pid")]
	[InlineData(nameof(ClaimNames.Population), "nrs:pop")]
	[InlineData(nameof(ClaimNames.Audience), "nrs:aud")]
	[InlineData(nameof(ClaimNames.Role), "nrs:role")]
	[InlineData(nameof(ClaimNames.AgencyId), "nrs:agency")]
	[InlineData(nameof(ClaimNames.CustomerId), "nrs:customer")]
	[InlineData(nameof(ClaimNames.IssuedAt), "iat")]
	[InlineData(nameof(ClaimNames.ExpiresAt), "exp")]
	[InlineData(nameof(ClaimNames.Subject), "sub")]
	public void Wire_contract_string_is_stable(string memberName, string expected)
	{
		typeof(ClaimNames)
			.GetField(memberName)!
			.GetValue(null)
			.ShouldBe(expected);
	}
}
```

- [ ] **Step 4: Run all tests in the project**

Run from `norse-auth/`:

```
dotnet test tests/Norse.Auth.Contracts.Tests/Norse.Auth.Contracts.Tests.csproj
```

Expected: all tests pass; zero warnings.

- [ ] **Step 5: Stage**

```
git add norse-auth/tests/Norse.Auth.Contracts.Tests/PopulationTests.cs norse-auth/tests/Norse.Auth.Contracts.Tests/AudienceTests.cs norse-auth/tests/Norse.Auth.Contracts.Tests/ClaimNamesTests.cs
```

Proposed commit message: `test(auth-contracts): cover Population, Audience, and ClaimNames invariants`

### Task 13: Tests for `NorsePrincipal` and `NorsePrincipalFactory`

**Files:**
- Create: `norse-auth/tests/Norse.Auth.Contracts.Tests/NorsePrincipalTests.cs`
- Create: `norse-auth/tests/Norse.Auth.Contracts.Tests/NorsePrincipalFactoryTests.cs`

- [ ] **Step 1: Write `NorsePrincipalTests.cs`**

```csharp
using System.Collections.Frozen;
using Norse.Auth.Contracts;
using Shouldly;
using Xunit;

namespace Norse.Auth.Contracts.Tests;

public sealed class NorsePrincipalTests
{
	private static readonly DateTimeOffset IssuedAt = DateTimeOffset.UtcNow;
	private static readonly DateTimeOffset ExpiresAt = IssuedAt.AddHours(12);

	[Fact]
	public void Anonymous_principal_with_empty_roles_is_constructible()
	{
		Guid id = Guid.NewGuid();
		NorsePrincipal subject = new(
			principalId: id,
			population: Population.Anonymous,
			audience: Audience.MgaAnonymous,
			roles: FrozenSet<string>.Empty,
			source: PrincipalSource.Cookie,
			issuedAt: IssuedAt,
			expiresAt: ExpiresAt);

		subject.PrincipalId.ShouldBe(id);
		subject.Population.ShouldBe(Population.Anonymous);
		subject.IsAnonymous.ShouldBeTrue();
		subject.Roles.ShouldBeEmpty();
	}

	[Fact]
	public void Customer_principal_requires_CustomerId_equal_to_PrincipalId()
	{
		Guid id = Guid.NewGuid();

		Should.Throw<ArgumentException>(() => new NorsePrincipal(
			principalId: id,
			population: Population.Customer,
			audience: Audience.MgaCustomer,
			roles: FrozenSet<string>.Empty,
			source: PrincipalSource.Cookie,
			issuedAt: IssuedAt,
			expiresAt: ExpiresAt,
			customerId: null));

		Should.Throw<ArgumentException>(() => new NorsePrincipal(
			principalId: id,
			population: Population.Customer,
			audience: Audience.MgaCustomer,
			roles: FrozenSet<string>.Empty,
			source: PrincipalSource.Cookie,
			issuedAt: IssuedAt,
			expiresAt: ExpiresAt,
			customerId: Guid.NewGuid()));

		Should.NotThrow(() => new NorsePrincipal(
			principalId: id,
			population: Population.Customer,
			audience: Audience.MgaCustomer,
			roles: FrozenSet<string>.Empty,
			source: PrincipalSource.Cookie,
			issuedAt: IssuedAt,
			expiresAt: ExpiresAt,
			customerId: id));
	}

	[Theory]
	[InlineData(Population.Producer)]
	[InlineData(Population.Machine)]
	public void Producer_and_Machine_require_AgencyId(Population population)
	{
		Should.Throw<ArgumentException>(() => new NorsePrincipal(
			principalId: Guid.NewGuid(),
			population: population,
			audience: Audience.MgaProducer,
			roles: FrozenSet<string>.Empty,
			source: PrincipalSource.Cookie,
			issuedAt: IssuedAt,
			expiresAt: ExpiresAt,
			agencyId: null));

		Should.NotThrow(() => new NorsePrincipal(
			principalId: Guid.NewGuid(),
			population: population,
			audience: Audience.MgaProducer,
			roles: FrozenSet<string>.Empty,
			source: PrincipalSource.Cookie,
			issuedAt: IssuedAt,
			expiresAt: ExpiresAt,
			agencyId: Guid.NewGuid()));
	}

	[Fact]
	public void Anonymous_with_nonempty_roles_throws()
	{
		Should.Throw<ArgumentException>(() => new NorsePrincipal(
			principalId: Guid.NewGuid(),
			population: Population.Anonymous,
			audience: Audience.MgaAnonymous,
			roles: FrozenSet.ToFrozenSet(new[] { "admin" }),
			source: PrincipalSource.Cookie,
			issuedAt: IssuedAt,
			expiresAt: ExpiresAt));
	}

	[Fact]
	public void Empty_PrincipalId_throws()
	{
		Should.Throw<ArgumentException>(() => new NorsePrincipal(
			principalId: Guid.Empty,
			population: Population.Anonymous,
			audience: Audience.MgaAnonymous,
			roles: FrozenSet<string>.Empty,
			source: PrincipalSource.Cookie,
			issuedAt: IssuedAt,
			expiresAt: ExpiresAt));
	}

	[Fact]
	public void ExpiresAt_before_IssuedAt_throws()
	{
		Should.Throw<ArgumentException>(() => new NorsePrincipal(
			principalId: Guid.NewGuid(),
			population: Population.Anonymous,
			audience: Audience.MgaAnonymous,
			roles: FrozenSet<string>.Empty,
			source: PrincipalSource.Cookie,
			issuedAt: IssuedAt,
			expiresAt: IssuedAt.AddSeconds(-1)));
	}

	[Fact]
	public void IsInRole_reads_from_the_frozen_set()
	{
		NorsePrincipal subject = new(
			principalId: Guid.NewGuid(),
			population: Population.Staff,
			audience: Audience.MgaStaff,
			roles: FrozenSet.ToFrozenSet(new[] { "underwriter", "claims-adjuster" }, StringComparer.Ordinal),
			source: PrincipalSource.Jwt,
			issuedAt: IssuedAt,
			expiresAt: ExpiresAt);

		subject.IsInRole("underwriter").ShouldBeTrue();
		subject.IsInRole("UNDERWRITER").ShouldBeFalse();
		subject.IsInRole("policy-admin").ShouldBeFalse();
	}
}
```

- [ ] **Step 2: Write `NorsePrincipalFactoryTests.cs`**

```csharp
using System.Globalization;
using System.Security.Claims;
using Norse.Auth.Contracts;
using Shouldly;
using Xunit;

namespace Norse.Auth.Contracts.Tests;

public sealed class NorsePrincipalFactoryTests
{
	private static readonly DateTimeOffset IssuedAt = DateTimeOffset.UtcNow;
	private static readonly DateTimeOffset ExpiresAt = IssuedAt.AddHours(12);

	private static ClaimsPrincipal BuildClaims(
		Guid principalId,
		Population population,
		Audience audience,
		IEnumerable<string>? roles = null,
		Guid? agencyId = null,
		Guid? customerId = null)
	{
		List<Claim> claims =
		[
			new(ClaimNames.PrincipalId, principalId.ToString()),
			new(ClaimNames.Population, population.ToString()),
			new(ClaimNames.Audience, audience.ToCanonicalString()),
			new(ClaimNames.IssuedAt, IssuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
			new(ClaimNames.ExpiresAt, ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
		];
		if (roles is not null)
			claims.AddRange(roles.Select(r => new Claim(ClaimNames.Role, r)));
		if (agencyId is not null) claims.Add(new(ClaimNames.AgencyId, agencyId.Value.ToString()));
		if (customerId is not null) claims.Add(new(ClaimNames.CustomerId, customerId.Value.ToString()));

		return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
	}

	[Fact]
	public void Builds_anonymous_principal_from_claims()
	{
		Guid id = Guid.NewGuid();
		ClaimsPrincipal claims = BuildClaims(id, Population.Anonymous, Audience.MgaAnonymous);

		NorsePrincipal subject = NorsePrincipalFactory.FromClaimsPrincipal(claims, PrincipalSource.Cookie);

		subject.PrincipalId.ShouldBe(id);
		subject.Population.ShouldBe(Population.Anonymous);
		subject.Audience.ShouldBe(Audience.MgaAnonymous);
		subject.Source.ShouldBe(PrincipalSource.Cookie);
		subject.Roles.ShouldBeEmpty();
	}

	[Fact]
	public void Builds_customer_principal_with_CustomerId()
	{
		Guid id = Guid.NewGuid();
		ClaimsPrincipal claims = BuildClaims(id, Population.Customer, Audience.MgaCustomer, customerId: id);

		NorsePrincipal subject = NorsePrincipalFactory.FromClaimsPrincipal(claims, PrincipalSource.Cookie);

		subject.CustomerId.ShouldBe(id);
	}

	[Fact]
	public void Missing_PrincipalId_throws()
	{
		ClaimsPrincipal claims = new(new ClaimsIdentity(new[]
		{
			new Claim(ClaimNames.Population, "Anonymous"),
			new Claim(ClaimNames.Audience, "mga.anonymous"),
			new Claim(ClaimNames.IssuedAt, IssuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
			new Claim(ClaimNames.ExpiresAt, ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
		}, authenticationType: "test"));

		Should.Throw<InvalidNorsePrincipalException>(() =>
			NorsePrincipalFactory.FromClaimsPrincipal(claims, PrincipalSource.Cookie));
	}

	[Fact]
	public void Malformed_PrincipalId_throws()
	{
		ClaimsPrincipal claims = new(new ClaimsIdentity(new[]
		{
			new Claim(ClaimNames.PrincipalId, "not-a-guid"),
			new Claim(ClaimNames.Population, "Anonymous"),
			new Claim(ClaimNames.Audience, "mga.anonymous"),
			new Claim(ClaimNames.IssuedAt, IssuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
			new Claim(ClaimNames.ExpiresAt, ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
		}, authenticationType: "test"));

		Should.Throw<InvalidNorsePrincipalException>(() =>
			NorsePrincipalFactory.FromClaimsPrincipal(claims, PrincipalSource.Cookie));
	}

	[Fact]
	public void Unknown_audience_canonical_throws()
	{
		ClaimsPrincipal claims = new(new ClaimsIdentity(new[]
		{
			new Claim(ClaimNames.PrincipalId, Guid.NewGuid().ToString()),
			new Claim(ClaimNames.Population, "Anonymous"),
			new Claim(ClaimNames.Audience, "mga.bogus"),
			new Claim(ClaimNames.IssuedAt, IssuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
			new Claim(ClaimNames.ExpiresAt, ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
		}, authenticationType: "test"));

		Should.Throw<InvalidNorsePrincipalException>(() =>
			NorsePrincipalFactory.FromClaimsPrincipal(claims, PrincipalSource.Cookie));
	}

	[Fact]
	public void Multi_valued_PrincipalId_throws()
	{
		Guid id = Guid.NewGuid();
		ClaimsPrincipal claims = new(new ClaimsIdentity(new[]
		{
			new Claim(ClaimNames.PrincipalId, id.ToString()),
			new Claim(ClaimNames.PrincipalId, Guid.NewGuid().ToString()),
			new Claim(ClaimNames.Population, "Anonymous"),
			new Claim(ClaimNames.Audience, "mga.anonymous"),
			new Claim(ClaimNames.IssuedAt, IssuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
			new Claim(ClaimNames.ExpiresAt, ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
		}, authenticationType: "test"));

		Should.Throw<InvalidNorsePrincipalException>(() =>
			NorsePrincipalFactory.FromClaimsPrincipal(claims, PrincipalSource.Cookie));
	}

	[Fact]
	public void Roles_round_trip_into_a_frozen_ordinal_set()
	{
		Guid id = Guid.NewGuid();
		ClaimsPrincipal claims = BuildClaims(id, Population.Staff, Audience.MgaStaff,
			roles: new[] { "underwriter", "claims-adjuster" });

		NorsePrincipal subject = NorsePrincipalFactory.FromClaimsPrincipal(claims, PrincipalSource.Jwt);

		subject.Roles.ShouldContain("underwriter");
		subject.Roles.ShouldContain("claims-adjuster");
		subject.IsInRole("UNDERWRITER").ShouldBeFalse();
	}
}
```

- [ ] **Step 3: Run all tests**

Run from `norse-auth/`:

```
dotnet test tests/Norse.Auth.Contracts.Tests/Norse.Auth.Contracts.Tests.csproj
```

Expected: all tests pass; zero warnings.

- [ ] **Step 4: Stage**

```
git add norse-auth/tests/Norse.Auth.Contracts.Tests/NorsePrincipalTests.cs norse-auth/tests/Norse.Auth.Contracts.Tests/NorsePrincipalFactoryTests.cs
```

Proposed commit message: `test(auth-contracts): cover NorsePrincipal invariants and factory failure modes`

---

## Phase 4 — `Norse.Auth.Components` placeholder 🟢

### Task 14: Create the Components project (empty for now)

**Files:**
- Create: `norse-auth/src/Norse.Auth.Components/Norse.Auth.Components.csproj`

The sign-in UI lands in Plan D. We scaffold the project now so consumers (`Norse.Hosting.Web.Client`, `Norse.Hosting.App`) have a stable assembly to reference, and so `Norse.Auth.slnx` has the canonical three-assembly shape from the start.

- [ ] **Step 1: Create the project**

Run from `norse-auth/`:

```
dotnet new razorclasslib --name Norse.Auth.Components --output src/Norse.Auth.Components --framework net10.0 --support-pages-and-views false
```

- [ ] **Step 2: Remove the auto-generated content**

Run:

```
Remove-Item norse-auth/src/Norse.Auth.Components/Component1.razor -ErrorAction SilentlyContinue
Remove-Item norse-auth/src/Norse.Auth.Components/Component1.razor.css -ErrorAction SilentlyContinue
Remove-Item norse-auth/src/Norse.Auth.Components/ExampleJsInterop.cs -ErrorAction SilentlyContinue
Remove-Item -Recurse norse-auth/src/Norse.Auth.Components/wwwroot -ErrorAction SilentlyContinue
```

- [ ] **Step 3: Replace the csproj**

Replace `norse-auth/src/Norse.Auth.Components/Norse.Auth.Components.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <Description>Blazor components for the {Company} Auth bounded context. Sign-in pages, account-settings, agency-admin user management. WASM-bundlable; no infrastructure dependencies.</Description>
    <IsPackable>true</IsPackable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Norse.Auth.Contracts\Norse.Auth.Contracts.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add to the solution**

Run from `norse-auth/`:

```
dotnet sln Norse.Auth.slnx add src/Norse.Auth.Components/Norse.Auth.Components.csproj
```

- [ ] **Step 5: Verify it builds**

Run from `norse-auth/`:

```
dotnet build src/Norse.Auth.Components/Norse.Auth.Components.csproj
```

Expected: build succeeds, zero warnings. The assembly is empty; that's fine.

- [ ] **Step 6: Stage**

```
git add norse-auth/src/Norse.Auth.Components norse-auth/Norse.Auth.slnx
```

Proposed commit message: `feat(auth-components): scaffold Norse.Auth.Components placeholder (UI lands in Plan D)`

---

## Phase 5 — `Norse.Auth.Server` foundation

### Task 15: Create the Infrastructure project 🟢

**Files:**
- Create: `norse-auth/src/Norse.Auth.Server/Norse.Auth.Server.csproj`

- [ ] **Step 1: Create the project**

Run from `norse-auth/`:

```
dotnet new classlib --name Norse.Auth.Server --output src/Norse.Auth.Server --framework net10.0
```

- [ ] **Step 2: Remove the auto-generated class**

Run: `Remove-Item norse-auth/src/Norse.Auth.Server/Class1.cs`

- [ ] **Step 3: Replace the csproj**

Replace `norse-auth/src/Norse.Auth.Server/Norse.Auth.Server.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>{Company} Auth infrastructure: OpenIddict server configuration, federation handlers, anonymous-bootstrap middleware, EF stores, AuthPlugin entry point.</Description>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.Cookies" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Norse.Auth.Contracts\Norse.Auth.Contracts.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add to the solution**

Run from `norse-auth/`:

```
dotnet sln Norse.Auth.slnx add src/Norse.Auth.Server/Norse.Auth.Server.csproj
```

- [ ] **Step 5: Verify it builds**

Run from `norse-auth/`:

```
dotnet build src/Norse.Auth.Server/Norse.Auth.Server.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 6: Stage**

```
git add norse-auth/src/Norse.Auth.Server norse-auth/Norse.Auth.slnx
```

Proposed commit message: `feat(auth-infra): scaffold Norse.Auth.Server project`

### Task 16: `AuthOptions` configuration 🟢

**Files:**
- Create: `norse-auth/src/Norse.Auth.Server/AuthOptions.cs`

Bound from configuration. Default values mirror the spec; production overrides flow from Azure App Configuration. Plus-addressing defaults to `false` (prod-safe) — dev overrides via `AuthOptions__AllowPlusAddressing=true`.

- [ ] **Step 1: Write `AuthOptions.cs`**

```csharp
namespace Norse.Auth.Server;

/// <summary>
/// Bound configuration for the Auth bounded context. Section name: <c>"{Company}:Auth"</c>.
/// </summary>
public sealed class AuthOptions
{
	public const string SectionName = "{Company}:Auth";

	/// <summary>
	/// The canonical issuer URL emitted in tokens and used for OIDC/OAuth discovery.
	/// Production: <c>https://auth.{company}.com</c>. Local dev: <c>https://auth.{company}.local</c>.
	/// </summary>
	public required Uri Issuer { get; init; }

	/// <summary>
	/// Whether plus-addressing is permitted at registration. Production: false.
	/// Dev/test: true (so a single inbox can spawn many test customers).
	/// </summary>
	public bool AllowPlusAddressing { get; init; }

	/// <summary>Lifetime defaults. Mirrors spec §6.2.</summary>
	public LifetimeOptions Lifetimes { get; init; } = new();

	public sealed class LifetimeOptions
	{
		public TimeSpan AnonymousCookie { get; init; } = TimeSpan.FromDays(30);
		public TimeSpan AuthenticatedCookieIdle { get; init; } = TimeSpan.FromHours(12);
		public TimeSpan AuthenticatedCookieAbsolute { get; init; } = TimeSpan.FromDays(7);
		public TimeSpan AccessToken { get; init; } = TimeSpan.FromMinutes(15);
		public TimeSpan RefreshTokenWeb { get; init; } = TimeSpan.FromDays(14);
		public TimeSpan RefreshTokenNative { get; init; } = TimeSpan.FromDays(90);
		public TimeSpan MagicLink { get; init; } = TimeSpan.FromMinutes(15);
	}
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-auth/`:

```
dotnet build src/Norse.Auth.Server/Norse.Auth.Server.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-auth/src/Norse.Auth.Server/AuthOptions.cs
```

Proposed commit message: `feat(auth-infra): add AuthOptions with spec lifetime defaults`

### Task 17: Anonymous-bootstrap supporting types 🟢

**Files:**
- Create: `norse-auth/src/Norse.Auth.Server/AnonymousBootstrap/AnonymousCookieDefaults.cs`
- Create: `norse-auth/src/Norse.Auth.Server/AnonymousBootstrap/AnonymousBootstrapOptions.cs`

- [ ] **Step 1: Write `AnonymousCookieDefaults.cs`**

```csharp
namespace Norse.Auth.Server.AnonymousBootstrap;

/// <summary>Canonical constants for the anonymous-cookie authentication scheme.</summary>
public static class AnonymousCookieDefaults
{
	/// <summary>The ASP.NET Core authentication scheme name used for anonymous-bootstrap cookies.</summary>
	public const string AuthenticationScheme = "{Company}Anonymous";

	/// <summary>The cookie name on the wire.</summary>
	public const string CookieName = "nrs.anon";
}
```

- [ ] **Step 2: Write `AnonymousBootstrapOptions.cs`**

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Norse.Auth.Server.AnonymousBootstrap;

/// <summary>
/// Configuration for the anonymous-cookie authentication scheme. Sliding lifetime;
/// HTTP-only; secure; SameSite=Lax (Strict would break OAuth callback round-trips).
/// </summary>
public sealed class AnonymousBootstrapOptions : AuthenticationSchemeOptions
{
	public string CookieName { get; set; } = AnonymousCookieDefaults.CookieName;
	public TimeSpan CookieLifetime { get; set; } = TimeSpan.FromDays(30);
	public bool SecureCookie { get; set; } = true;
	public SameSiteMode SameSite { get; set; } = SameSiteMode.Lax;
	public CookieSecurePolicy SecurePolicy { get; set; } = CookieSecurePolicy.Always;
	public PathString CookiePath { get; set; } = "/";

	/// <summary>
	/// Optional explicit cookie domain. Leave null in dev (host-only cookie); set in prod
	/// to the apex domain (e.g., <c>.{company}.com</c>) when the host fronts multiple subdomains.
	/// </summary>
	public string? CookieDomain { get; set; }
}
```

- [ ] **Step 3: Verify it builds**

Run from `norse-auth/`:

```
dotnet build src/Norse.Auth.Server/Norse.Auth.Server.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 4: Stage**

```
git add norse-auth/src/Norse.Auth.Server/AnonymousBootstrap
```

Proposed commit message: `feat(auth-infra): add AnonymousBootstrap defaults and options`

### Task 18: `AnonymousPrincipalGenerator` 🟡 awaits UUID v5 namespace registry

**Files:**
- Create: `norse-auth/src/Norse.Auth.Server/AnonymousBootstrap/AnonymousPrincipalGenerator.cs`

This task is **🟡** because the spec uses UUID v5 under the *auth-context namespace*, and the canonical UUID v5 namespace registry hasn't shipped yet (it's a forthcoming Primitives spec). Implementation strategy:

- **For Plan A execution today:** use a placeholder namespace constant (`AuthAnonymousNamespaceV5`) defined locally with a *temporary* GUID value. The constant is annotated with `[Obsolete("Awaiting Primitives UUID v5 namespace registry.")]` so future work surfaces it.
- **When the registry ships:** delete the local constant; reference the registry's auth-anonymous namespace.

- [ ] **Step 1: Write `AnonymousPrincipalGenerator.cs`**

```csharp
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Norse.Auth.Contracts;

namespace Norse.Auth.Server.AnonymousBootstrap;

/// <summary>
/// Produces stable anonymous principal identifiers. Deterministic where required by the spec
/// (UUID v5 over the auth-anonymous namespace seeded with a high-entropy per-cookie nonce
/// generated by <see cref="RandomNumberGenerator"/>). The nonce is what makes each anonymous
/// principal unique; the namespace is what binds it to this realm so future cross-realm
/// principal ids never collide.
/// </summary>
public interface IAnonymousPrincipalGenerator
{
	Guid NewAnonymousPrincipalId();
}

/// <inheritdoc />
public sealed class AnonymousPrincipalGenerator : IAnonymousPrincipalGenerator
{
	/// <summary>
	/// TEMPORARY: until the Primitives UUID v5 namespace registry ships, this constant holds the
	/// anonymous-auth namespace. Replace with the registry's value when available. Do NOT change
	/// this value once Plan A has shipped — every anonymous principal id depends on it.
	/// </summary>
	[Obsolete("Awaiting Primitives UUID v5 namespace registry. Do not change the value.")]
	public static readonly Guid AuthAnonymousNamespaceV5 = new("8b7e9d36-3a3a-5d8f-9c41-1b4e4f5a6c70");

	public Guid NewAnonymousPrincipalId()
	{
		Span<byte> nonce = stackalloc byte[16];
		RandomNumberGenerator.Fill(nonce);
#pragma warning disable CS0618 // referencing the deliberately-Obsolete constant by design
		return UuidV5.From(AuthAnonymousNamespaceV5, nonce);
#pragma warning restore CS0618
	}
}

/// <summary>
/// Inline UUID v5 (RFC 4122) implementation. Replaced by Primitives' published utility when
/// the namespace registry ships. Keeping the surface narrow (single method) makes that swap a
/// type-rename, not a refactor.
/// </summary>
internal static class UuidV5
{
	public static Guid From(Guid @namespace, ReadOnlySpan<byte> name)
	{
		Span<byte> namespaceBytes = stackalloc byte[16];
		WriteGuidNetworkOrder(@namespace, namespaceBytes);

		int totalLen = namespaceBytes.Length + name.Length;
		byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(totalLen);
		try
		{
			Span<byte> buffer = rented.AsSpan(0, totalLen);
			namespaceBytes.CopyTo(buffer);
			name.CopyTo(buffer[namespaceBytes.Length..]);

			Span<byte> hash = stackalloc byte[20];
			SHA1.HashData(buffer, hash);

			Span<byte> resultBytes = stackalloc byte[16];
			hash[..16].CopyTo(resultBytes);

			// Version 5 (RFC 4122 §4.1.3) — set high nibble of byte 6 to 0101.
			resultBytes[6] = (byte)((resultBytes[6] & 0x0F) | 0x50);
			// Variant RFC 4122 (§4.1.1) — set high two bits of byte 8 to 10.
			resultBytes[8] = (byte)((resultBytes[8] & 0x3F) | 0x80);

			return ReadGuidNetworkOrder(resultBytes);
		}
		finally
		{
			System.Buffers.ArrayPool<byte>.Shared.Return(rented);
		}
	}

	private static void WriteGuidNetworkOrder(Guid guid, Span<byte> destination)
	{
		guid.TryWriteBytes(destination);
		// Guid serializes its first three fields little-endian on Windows; RFC 4122 requires
		// big-endian (network order). Swap them.
		BinaryPrimitives.WriteUInt32BigEndian(destination[..4], BinaryPrimitives.ReadUInt32LittleEndian(destination[..4]));
		BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(4, 2), BinaryPrimitives.ReadUInt16LittleEndian(destination.Slice(4, 2)));
		BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(6, 2), BinaryPrimitives.ReadUInt16LittleEndian(destination.Slice(6, 2)));
	}

	private static Guid ReadGuidNetworkOrder(ReadOnlySpan<byte> source)
	{
		Span<byte> local = stackalloc byte[16];
		source.CopyTo(local);
		BinaryPrimitives.WriteUInt32LittleEndian(local[..4], BinaryPrimitives.ReadUInt32BigEndian(local[..4]));
		BinaryPrimitives.WriteUInt16LittleEndian(local.Slice(4, 2), BinaryPrimitives.ReadUInt16BigEndian(local.Slice(4, 2)));
		BinaryPrimitives.WriteUInt16LittleEndian(local.Slice(6, 2), BinaryPrimitives.ReadUInt16BigEndian(local.Slice(6, 2)));
		return new Guid(local);
	}
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-auth/`:

```
dotnet build src/Norse.Auth.Server/Norse.Auth.Server.csproj
```

Expected: build succeeds, zero warnings (the `[Obsolete]` is suppressed locally with `#pragma warning disable` — intentional).

- [ ] **Step 3: Stage**

```
git add norse-auth/src/Norse.Auth.Server/AnonymousBootstrap/AnonymousPrincipalGenerator.cs
```

Proposed commit message: `feat(auth-infra): add AnonymousPrincipalGenerator with temporary UUID v5 namespace`

### Task 19: `AnonymousCookieAuthenticationHandler` 🟡 awaits Norse.Hosting.Web.Server

**Files:**
- Create: `norse-auth/src/Norse.Auth.Server/AnonymousBootstrap/AnonymousCookieAuthenticationHandler.cs`

The handler does two things on every request:
1. If a valid signed cookie exists, decode it and attach the resulting `ClaimsPrincipal` to the request.
2. If no cookie exists, generate a new anonymous principal, write the signed cookie, and attach the principal.

The handler stores the cookie payload via ASP.NET Core Data Protection — the same key ring OpenIddict uses (configured in Plan B). For Plan A, we use the default Data Protection key ring; production posture (Azure Key Vault) is wired in Plan B.

Marked 🟡 because the *registration* of this handler depends on `Norse.Hosting.Web.Server`'s authentication-builder surface. The handler itself compiles standalone; only its wiring into the host needs the hosting abstraction.

- [ ] **Step 1: Write `AnonymousCookieAuthenticationHandler.cs`**

```csharp
using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Norse.Auth.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Norse.Auth.Server.AnonymousBootstrap;

/// <summary>
/// Authentication handler that guarantees every request carries a principal. If a valid
/// anonymous cookie is present, decodes it; otherwise mints a fresh anonymous principal,
/// writes the cookie, and proceeds. Per spec §7.0.
/// </summary>
public sealed class AnonymousCookieAuthenticationHandler : AuthenticationHandler<AnonymousBootstrapOptions>
{
	private readonly IAnonymousPrincipalGenerator _generator;
	private readonly IDataProtector _protector;
	private readonly TimeProvider _time;

	public AnonymousCookieAuthenticationHandler(
		IOptionsMonitor<AnonymousBootstrapOptions> options,
		ILoggerFactory loggerFactory,
		UrlEncoder urlEncoder,
		IAnonymousPrincipalGenerator generator,
		IDataProtectionProvider dataProtectionProvider,
		TimeProvider time)
		: base(options, loggerFactory, urlEncoder)
	{
		_generator = generator;
		_protector = dataProtectionProvider.CreateProtector("Norse.Auth.AnonymousCookie.v1");
		_time = time;
	}

	protected override Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		string? cookieValue = Context.Request.Cookies[Options.CookieName];

		if (cookieValue is not null)
		{
			AuthenticateResult? existing = TryDecodeCookie(cookieValue);
			if (existing is not null && existing.Succeeded)
				return Task.FromResult(existing);

			// Cookie present but invalid (tampered or expired). Fail loudly via log; we'll mint
			// a new one in HandleChallengeAsync-like flow below.
			Logger.LogWarning("Discarding invalid anonymous cookie; minting a fresh principal.");
		}

		AuthenticateResult fresh = MintFreshPrincipal();
		return Task.FromResult(fresh);
	}

	private AuthenticateResult? TryDecodeCookie(string cookieValue)
	{
		try
		{
			string payload = _protector.Unprotect(cookieValue);
			string[] parts = payload.Split('|');
			if (parts.Length != 4) return AuthenticateResult.Fail("Malformed anonymous cookie payload.");

			if (!Guid.TryParse(parts[0], out Guid principalId) || principalId == Guid.Empty)
				return AuthenticateResult.Fail("Anonymous cookie principal id was not a valid GUID.");
			if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long iatSeconds))
				return AuthenticateResult.Fail("Anonymous cookie issued-at was not parseable.");
			if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long expSeconds))
				return AuthenticateResult.Fail("Anonymous cookie expires-at was not parseable.");
			if (parts[3] != "v1")
				return AuthenticateResult.Fail($"Anonymous cookie version is not 'v1': '{parts[3]}'.");

			DateTimeOffset issuedAt = DateTimeOffset.FromUnixTimeSeconds(iatSeconds);
			DateTimeOffset expiresAt = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
			if (expiresAt <= _time.GetUtcNow()) return AuthenticateResult.Fail("Anonymous cookie has expired.");

			ClaimsPrincipal claimsPrincipal = BuildClaimsPrincipal(principalId, issuedAt, expiresAt);
			AuthenticationTicket ticket = new(claimsPrincipal, Scheme.Name);
			return AuthenticateResult.Success(ticket);
		}
		catch (System.Security.Cryptography.CryptographicException)
		{
			return AuthenticateResult.Fail("Anonymous cookie signature failed to validate.");
		}
	}

	private AuthenticateResult MintFreshPrincipal()
	{
		Guid principalId = _generator.NewAnonymousPrincipalId();
		DateTimeOffset issuedAt = _time.GetUtcNow();
		DateTimeOffset expiresAt = issuedAt.Add(Options.CookieLifetime);

		string payload = string.Join('|',
			principalId.ToString("D", CultureInfo.InvariantCulture),
			issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
			expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
			"v1");
		string protectedPayload = _protector.Protect(payload);

		WriteCookie(protectedPayload, expiresAt);

		ClaimsPrincipal claimsPrincipal = BuildClaimsPrincipal(principalId, issuedAt, expiresAt);
		AuthenticationTicket ticket = new(claimsPrincipal, Scheme.Name);
		return AuthenticateResult.Success(ticket);
	}

	private void WriteCookie(string cookieValue, DateTimeOffset expiresAt)
	{
		CookieOptions cookieOptions = new()
		{
			Path = Options.CookiePath,
			Domain = Options.CookieDomain,
			Secure = Options.SecureCookie,
			HttpOnly = true,
			SameSite = Options.SameSite,
			SecurePolicy = Options.SecurePolicy,
			Expires = expiresAt,
			IsEssential = true,
		};
		Context.Response.Cookies.Append(Options.CookieName, cookieValue, cookieOptions);
	}

	private static ClaimsPrincipal BuildClaimsPrincipal(Guid principalId, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
	{
		List<Claim> claims =
		[
			new(ClaimNames.PrincipalId, principalId.ToString("D", CultureInfo.InvariantCulture)),
			new(ClaimNames.Subject, principalId.ToString("D", CultureInfo.InvariantCulture)),
			new(ClaimNames.Population, Population.Anonymous.ToString()),
			new(ClaimNames.Audience, Audience.MgaAnonymous.ToCanonicalString()),
			new(ClaimNames.IssuedAt, issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
			new(ClaimNames.ExpiresAt, expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
		];
		return new ClaimsPrincipal(new ClaimsIdentity(claims, AnonymousCookieDefaults.AuthenticationScheme));
	}
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-auth/`:

```
dotnet build src/Norse.Auth.Server/Norse.Auth.Server.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-auth/src/Norse.Auth.Server/AnonymousBootstrap/AnonymousCookieAuthenticationHandler.cs
```

Proposed commit message: `feat(auth-infra): add AnonymousCookieAuthenticationHandler with cookie issue/decode`

### ~~Task 20: Auth entity configurations stub~~ — VOID (2026-06-03)

> **Void note (identity storage decided 2026-06-03):** Mongo is the identity system of record (auth spec §3); `Norse.Auth.Server` carries **no** EF reference, entity classes, or DbContext of any kind — the `.Server` hard wall admits no placeholder. The Postgres side of Auth is a read-only reporting projection whose entity classes + `IEntityTypeConfiguration<T>` impls land in `Norse.Auth.Worker` (Plan B), backed by `Norse.Infrastructure.Persistence`'s `AuthDbContext`. **Do not execute any step of this task** — skip to Task 21. The original task body is retained below for historical continuity only.

**Files:**
- Create: `norse-auth/src/Norse.Auth.Server/AuthDbContext.cs`

A stub that compiles standalone (deriving from `Microsoft.EntityFrameworkCore.DbContext`) with a TODO marker to swap the base type to `InfrastructureDbContext` when the persistence base ships. The full entity model (users, sessions, magic-link tokens, external identities, OpenIddict tables) lands in Plan B.

- [ ] **Step 1: Add the EF Core package reference**

Edit `norse-auth/src/Norse.Auth.Server/Norse.Auth.Server.csproj` and add inside the existing `<ItemGroup>` that holds `<PackageReference>` entries:

```xml
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
```

- [ ] **Step 2: Write `AuthDbContext.cs`**

```csharp
using Microsoft.EntityFrameworkCore;

namespace Norse.Auth.Server;

/// <summary>
/// EF Core context for the Auth bounded context. The full entity model (users, sessions,
/// magic-link tokens, external identities, OpenIddict tables) lands in Plan B. This stub
/// exists so Plan A's `AuthPlugin` can wire the context into DI today.
/// </summary>
/// <remarks>
/// 🟡 TODO (Plan B): change the base type from <see cref="DbContext"/> to
/// <c>Norse.Infrastructure.Persistence.InfrastructureDbContext</c> once <c>norse-infrastructure-persistence</c> ships.
/// Inheriting from <see cref="DbContext"/> directly is a deliberate placeholder — it lets the
/// rest of Plan A compile without blocking on the persistence base spec.
/// </remarks>
public class AuthDbContext : DbContext
{
	public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		modelBuilder.HasDefaultSchema("auth");
		// Entity configuration lands in Plan B.
	}
}
```

- [ ] **Step 3: Verify it builds**

Run from `norse-auth/`:

```
dotnet build src/Norse.Auth.Server/Norse.Auth.Server.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 4: Stage**

```
git add norse-auth/src/Norse.Auth.Server/AuthDbContext.cs norse-auth/src/Norse.Auth.Server/Norse.Auth.Server.csproj
```

Proposed commit message: `feat(auth-infra): add AuthDbContext stub (full model lands in Plan B)`

### Task 21: `AuthPlugin` skeleton 🟡 awaits Norse.Abstractions.Hosting.IWebHostPlugin

**Files:**
- Create: `norse-auth/src/Norse.Auth.Server/AuthPlugin.cs`

Marked 🟡 because `Norse.Abstractions.Hosting.IWebHostPlugin` lives in `Norse.Abstractions.Hosting`, which doesn't have a spec yet. For Plan A, the plugin is a regular `internal sealed class` with conventional DI-extension methods. When the abstraction lands, the class gains `: Norse.Abstractions.Hosting.IWebHostPlugin` and the methods rename to the interface members.

- [ ] **Step 1: Write `AuthPlugin.cs`**

```csharp
using Norse.Auth.Server.AnonymousBootstrap;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Auth.Server;

/// <summary>
/// Plugin entry point for the Auth bounded context. Registers the anonymous-cookie
/// authentication scheme, the principal generator, and bound options.
/// </summary>
/// <remarks>
/// 🟡 TODO (when <c>Norse.Abstractions.Hosting</c> ships): change this to
/// <c>internal sealed class AuthPlugin : Norse.Abstractions.Hosting.IWebHostPlugin</c> and rename
/// <see cref="RegisterServices"/> / <see cref="MapEndpoints"/> to the interface members.
/// </remarks>
public sealed class AuthPlugin
{
	public void RegisterServices(IServiceCollection services, IConfiguration configuration)
	{
		services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
		services.AddSingleton<IAnonymousPrincipalGenerator, AnonymousPrincipalGenerator>();
		services.AddSingleton(TimeProvider.System);

		services.AddDataProtection();

		services
			.AddAuthentication(AnonymousCookieDefaults.AuthenticationScheme)
			.AddScheme<AnonymousBootstrapOptions, AnonymousCookieAuthenticationHandler>(
				AnonymousCookieDefaults.AuthenticationScheme,
				configure: _ => { });

		services.AddAuthorization(authorization =>
		{
			authorization.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
					AnonymousCookieDefaults.AuthenticationScheme)
				.RequireAuthenticatedUser()
				.Build();
		});
	}

	public void MapEndpoints(IEndpointRouteBuilder endpoints)
	{
		// Sign-in / sign-out / magic-link / discovery endpoints land in Plans C–E.
		// Plan A intentionally maps nothing.
	}
}
```

- [ ] **Step 2: Verify it builds**

Run from `norse-auth/`:

```
dotnet build src/Norse.Auth.Server/Norse.Auth.Server.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 3: Stage**

```
git add norse-auth/src/Norse.Auth.Server/AuthPlugin.cs
```

Proposed commit message: `feat(auth-infra): add AuthPlugin skeleton with anonymous-scheme registration`

---

## Phase 6 — `Norse.Auth.Server.Tests` 🟢

### Task 22: Create the Infrastructure test project

**Files:**
- Create: `norse-auth/tests/Norse.Auth.Server.Tests/Norse.Auth.Server.Tests.csproj`

- [ ] **Step 1: Create the test project**

Run from `norse-auth/`:

```
dotnet new xunit --name Norse.Auth.Server.Tests --output tests/Norse.Auth.Server.Tests --framework net10.0
```

- [ ] **Step 2: Remove the auto-generated test class**

Run: `Remove-Item norse-auth/tests/Norse.Auth.Server.Tests/UnitTest1.cs`

- [ ] **Step 3: Replace the csproj contents**

Replace `norse-auth/tests/Norse.Auth.Server.Tests/Norse.Auth.Server.Tests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Norse.Auth.Server\Norse.Auth.Server.csproj" />
    <ProjectReference Include="..\..\src\Norse.Auth.Contracts\Norse.Auth.Contracts.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add to the solution**

Run from `norse-auth/`:

```
dotnet sln Norse.Auth.slnx add tests/Norse.Auth.Server.Tests/Norse.Auth.Server.Tests.csproj
```

- [ ] **Step 5: Verify it builds**

Run from `norse-auth/`:

```
dotnet build tests/Norse.Auth.Server.Tests/Norse.Auth.Server.Tests.csproj
```

Expected: build succeeds, zero warnings.

- [ ] **Step 6: Stage**

```
git add norse-auth/tests/Norse.Auth.Server.Tests norse-auth/Norse.Auth.slnx
```

Proposed commit message: `test(auth-infra): scaffold Norse.Auth.Server.Tests xUnit project`

### Task 23: `AnonymousPrincipalGenerator` tests

**Files:**
- Create: `norse-auth/tests/Norse.Auth.Server.Tests/AnonymousPrincipalGeneratorTests.cs`

- [ ] **Step 1: Write `AnonymousPrincipalGeneratorTests.cs`**

```csharp
using Norse.Auth.Server.AnonymousBootstrap;
using Shouldly;
using Xunit;

namespace Norse.Auth.Server.Tests;

public sealed class AnonymousPrincipalGeneratorTests
{
	[Fact]
	public void Returns_a_non_empty_Guid()
	{
		AnonymousPrincipalGenerator generator = new();
		generator.NewAnonymousPrincipalId().ShouldNotBe(Guid.Empty);
	}

	[Fact]
	public void Returns_a_different_Guid_on_each_call()
	{
		AnonymousPrincipalGenerator generator = new();
		HashSet<Guid> ids = new();
		for (int i = 0; i < 1000; i++)
			ids.Add(generator.NewAnonymousPrincipalId()).ShouldBeTrue($"duplicate id on iteration {i}");
		ids.Count.ShouldBe(1000);
	}

	[Fact]
	public void Returned_Guid_is_UUID_v5_shape()
	{
		AnonymousPrincipalGenerator generator = new();
		Guid id = generator.NewAnonymousPrincipalId();
		byte[] bytes = id.ToByteArray();
		// Per System.Guid byte layout, the version nibble lands at byte index 7 (network order
		// bytes [6..8] are stored at little-endian positions [7,6]). We probe both.
		int version = (bytes[7] & 0xF0) >> 4;
		version.ShouldBe(5);
	}
}
```

- [ ] **Step 2: Run tests**

Run from `norse-auth/`:

```
dotnet test tests/Norse.Auth.Server.Tests/Norse.Auth.Server.Tests.csproj --filter "FullyQualifiedName~AnonymousPrincipalGenerator"
```

Expected: all 3 tests pass.

- [ ] **Step 3: Stage**

```
git add norse-auth/tests/Norse.Auth.Server.Tests/AnonymousPrincipalGeneratorTests.cs
```

Proposed commit message: `test(auth-infra): cover AnonymousPrincipalGenerator uniqueness and UUID v5 shape`

### Task 24: `AnonymousCookieAuthenticationHandler` integration tests

**Files:**
- Create: `norse-auth/tests/Norse.Auth.Server.Tests/AnonymousCookieAuthenticationHandlerTests.cs`

Tests use `Microsoft.AspNetCore.TestHost` to spin up a tiny in-memory web host whose only middleware is the anonymous-cookie scheme. Each test exercises one path (mint, decode, re-mint on tamper, re-mint on expiry).

- [ ] **Step 1: Write `AnonymousCookieAuthenticationHandlerTests.cs`**

```csharp
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using Norse.Auth.Contracts;
using Norse.Auth.Server.AnonymousBootstrap;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace Norse.Auth.Server.Tests;

public sealed class AnonymousCookieAuthenticationHandlerTests
{
	private static async Task<TestServer> BuildServerAsync(FakeTimeProvider? time = null)
	{
		IHost host = await new HostBuilder()
			.ConfigureWebHost(web => web
				.UseTestServer()
				.ConfigureServices(services =>
				{
					services.AddSingleton<TimeProvider>(time ?? new FakeTimeProvider(DateTimeOffset.UtcNow));
					services.AddSingleton<IAnonymousPrincipalGenerator, AnonymousPrincipalGenerator>();
					services.AddDataProtection();
					services
						.AddAuthentication(AnonymousCookieDefaults.AuthenticationScheme)
						.AddScheme<AnonymousBootstrapOptions, AnonymousCookieAuthenticationHandler>(
							AnonymousCookieDefaults.AuthenticationScheme,
							o => o.SecureCookie = false /* TestHost is http */);
				})
				.Configure(app =>
				{
					app.UseAuthentication();
					app.Run(async ctx =>
					{
						AuthenticateResult result = await ctx.AuthenticateAsync(AnonymousCookieDefaults.AuthenticationScheme);
						if (!result.Succeeded || result.Principal is null)
						{
							ctx.Response.StatusCode = 500;
							await ctx.Response.WriteAsync("auth failed");
							return;
						}
						string principalId = result.Principal.FindFirst(ClaimNames.PrincipalId)?.Value ?? "(missing)";
						await ctx.Response.WriteAsync(principalId);
					});
				}))
			.StartAsync();
		return host.GetTestServer();
	}

	[Fact]
	public async Task First_request_mints_a_cookie_and_returns_a_principal_id()
	{
		using TestServer server = await BuildServerAsync();
		using HttpClient client = server.CreateClient();

		HttpResponseMessage response = await client.GetAsync("/");

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		string body = await response.Content.ReadAsStringAsync();
		Guid.TryParse(body, out Guid id).ShouldBeTrue();
		id.ShouldNotBe(Guid.Empty);

		response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookies).ShouldBeTrue();
		setCookies!.ShouldContain(c => c.StartsWith(AnonymousCookieDefaults.CookieName));
	}

	[Fact]
	public async Task Second_request_with_the_first_cookie_returns_the_same_principal_id()
	{
		using TestServer server = await BuildServerAsync();
		using HttpClient client = server.CreateClient();

		HttpResponseMessage first = await client.GetAsync("/");
		string firstId = await first.Content.ReadAsStringAsync();
		string cookie = first.Headers.GetValues("Set-Cookie").First();
		string cookieKv = cookie.Split(';')[0];

		HttpRequestMessage secondRequest = new(HttpMethod.Get, "/");
		secondRequest.Headers.Add("Cookie", cookieKv);
		HttpResponseMessage second = await client.SendAsync(secondRequest);
		string secondId = await second.Content.ReadAsStringAsync();

		secondId.ShouldBe(firstId);
	}

	[Fact]
	public async Task A_tampered_cookie_is_discarded_and_a_fresh_principal_is_minted()
	{
		using TestServer server = await BuildServerAsync();
		using HttpClient client = server.CreateClient();

		HttpResponseMessage first = await client.GetAsync("/");
		string firstId = await first.Content.ReadAsStringAsync();

		// Tamper: replace cookie value with junk that has the same shape.
		string junk = $"{AnonymousCookieDefaults.CookieName}=this-is-not-a-valid-payload";
		HttpRequestMessage tamperedRequest = new(HttpMethod.Get, "/");
		tamperedRequest.Headers.Add("Cookie", junk);
		HttpResponseMessage tampered = await client.SendAsync(tamperedRequest);
		string tamperedId = await tampered.Content.ReadAsStringAsync();

		tampered.StatusCode.ShouldBe(HttpStatusCode.OK);
		Guid.TryParse(tamperedId, out _).ShouldBeTrue();
		tamperedId.ShouldNotBe(firstId);
	}

	[Fact]
	public async Task An_expired_cookie_is_discarded_and_a_fresh_principal_is_minted()
	{
		FakeTimeProvider time = new(DateTimeOffset.UtcNow);
		using TestServer server = await BuildServerAsync(time);
		using HttpClient client = server.CreateClient();

		HttpResponseMessage first = await client.GetAsync("/");
		string firstId = await first.Content.ReadAsStringAsync();
		string cookie = first.Headers.GetValues("Set-Cookie").First();
		string cookieKv = cookie.Split(';')[0];

		// Advance virtual time past the default 30-day lifetime.
		time.Advance(TimeSpan.FromDays(31));

		HttpRequestMessage expiredRequest = new(HttpMethod.Get, "/");
		expiredRequest.Headers.Add("Cookie", cookieKv);
		HttpResponseMessage expired = await client.SendAsync(expiredRequest);
		string expiredId = await expired.Content.ReadAsStringAsync();

		expired.StatusCode.ShouldBe(HttpStatusCode.OK);
		expiredId.ShouldNotBe(firstId);
	}
}
```

Note: the test uses `Microsoft.Extensions.TimeProvider.Testing`'s `FakeTimeProvider`. Add the package to the test csproj — edit `norse-auth/tests/Norse.Auth.Server.Tests/Norse.Auth.Server.Tests.csproj` and add inside the existing `<ItemGroup>` that holds `<PackageReference>` entries:

```xml
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
```

- [ ] **Step 2: Run tests**

Run from `norse-auth/`:

```
dotnet test tests/Norse.Auth.Server.Tests/Norse.Auth.Server.Tests.csproj --filter "FullyQualifiedName~AnonymousCookieAuthenticationHandler"
```

Expected: 4 tests pass.

- [ ] **Step 3: Stage**

```
git add norse-auth/tests/Norse.Auth.Server.Tests/AnonymousCookieAuthenticationHandlerTests.cs norse-auth/tests/Norse.Auth.Server.Tests/Norse.Auth.Server.Tests.csproj
```

Proposed commit message: `test(auth-infra): cover anonymous-cookie mint, decode, tamper, and expiry`

### Task 25: `AuthOptions` binding tests

**Files:**
- Create: `norse-auth/tests/Norse.Auth.Server.Tests/AuthOptionsTests.cs`

- [ ] **Step 1: Write `AuthOptionsTests.cs`**

```csharp
using Norse.Auth.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Norse.Auth.Server.Tests;

public sealed class AuthOptionsTests
{
	[Fact]
	public void Binds_from_configuration_with_explicit_values()
	{
		IConfiguration configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["{Company}:Auth:Issuer"] = "https://auth.{company}.local",
				["{Company}:Auth:AllowPlusAddressing"] = "true",
				["{Company}:Auth:Lifetimes:AnonymousCookie"] = "10.00:00:00", // 10 days
			})
			.Build();

		ServiceCollection services = new();
		services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
		IOptions<AuthOptions> options = services.BuildServiceProvider().GetRequiredService<IOptions<AuthOptions>>();

		options.Value.Issuer.ShouldBe(new Uri("https://auth.{company}.local"));
		options.Value.AllowPlusAddressing.ShouldBeTrue();
		options.Value.Lifetimes.AnonymousCookie.ShouldBe(TimeSpan.FromDays(10));
	}

	[Fact]
	public void Defaults_match_the_spec_table()
	{
		AuthOptions.LifetimeOptions defaults = new();
		defaults.AnonymousCookie.ShouldBe(TimeSpan.FromDays(30));
		defaults.AuthenticatedCookieIdle.ShouldBe(TimeSpan.FromHours(12));
		defaults.AuthenticatedCookieAbsolute.ShouldBe(TimeSpan.FromDays(7));
		defaults.AccessToken.ShouldBe(TimeSpan.FromMinutes(15));
		defaults.RefreshTokenWeb.ShouldBe(TimeSpan.FromDays(14));
		defaults.RefreshTokenNative.ShouldBe(TimeSpan.FromDays(90));
		defaults.MagicLink.ShouldBe(TimeSpan.FromMinutes(15));
	}
}
```

- [ ] **Step 2: Run tests**

Run from `norse-auth/`:

```
dotnet test tests/Norse.Auth.Server.Tests/Norse.Auth.Server.Tests.csproj --filter "FullyQualifiedName~AuthOptionsTests"
```

Expected: 2 tests pass.

- [ ] **Step 3: Stage**

```
git add norse-auth/tests/Norse.Auth.Server.Tests/AuthOptionsTests.cs
```

Proposed commit message: `test(auth-infra): cover AuthOptions binding and spec-default lifetimes`

---

## Phase 7 — Wire-up validation 🟡 awaits Norse.Hosting.Web.Server

### Task 26: End-to-end smoke test through `AuthPlugin`

**Files:**
- Create: `norse-auth/tests/Norse.Auth.Server.Tests/EndToEnd/AnonymousBootstrapEndToEndTests.cs`

This task **🟡 awaits `Norse.Hosting.Web.Server`**. Why: the test wants to exercise `AuthPlugin.RegisterServices` through the canonical host runtime, not a hand-rolled `HostBuilder`. Until that runtime ships, we have two options:

- **Defer this task to Plan A.5** (a small follow-up plan that lands after `Norse.Hosting.Web.Server` exists). Recommended.
- **Write a hand-rolled host equivalent now**, with a `🟡 TODO` note to swap it for the real runtime when available. Acceptable but creates throwaway code.

**Decision:** defer. The unit-level tests in Tasks 23–25 prove the handler and options work; an end-to-end pass through `AuthPlugin` is meaningful only against the real host. Plan A is considered complete with this task documented and unimplemented.

Sketch (for future execution):

```csharp
// 🟡 To implement after Norse.Hosting.Web.Server ships:
// 1. Spin up Norse.Hosting.Web.Server's TestHost equivalent
// 2. Register AuthPlugin via the Norse.Abstractions.Hosting.IWebHostPlugin contract
// 3. Issue an HTTP GET; assert a fresh anonymous cookie is set
// 4. Issue a second HTTP GET with that cookie; assert the principal id is stable
// 5. Issue a GET with no cookie after invalidating the data-protection key ring;
//    assert a new anonymous principal is minted
```

- [ ] **Step 1: Create the placeholder file with the sketch above**

Write `norse-auth/tests/Norse.Auth.Server.Tests/EndToEnd/AnonymousBootstrapEndToEndTests.cs`:

```csharp
// 🟡 Deferred to Plan A.5 — awaits Norse.Hosting.Web.Server.
//
// The unit-level tests in AnonymousCookieAuthenticationHandlerTests cover the handler
// in isolation. End-to-end verification through AuthPlugin requires the host runtime.
//
// To implement after Norse.Hosting.Web.Server ships:
//   1. Spin up Norse.Hosting.Web.Server's TestHost equivalent.
//   2. Register AuthPlugin via the Norse.Abstractions.Hosting.IWebHostPlugin contract.
//   3. Issue an HTTP GET; assert a fresh anonymous cookie is set.
//   4. Issue a second HTTP GET with that cookie; assert the principal id is stable.
//   5. Invalidate the data-protection key ring; assert a fresh principal is minted on next GET.
namespace Norse.Auth.Server.Tests.EndToEnd;
```

- [ ] **Step 2: Stage**

```
git add norse-auth/tests/Norse.Auth.Server.Tests/EndToEnd/AnonymousBootstrapEndToEndTests.cs
```

Proposed commit message: `test(auth-infra): document deferred end-to-end suite (awaits Norse.Hosting.Web.Server)`

---

## Phase 8 — Plan wrap-up

### Task 27: Full-solution build and test run

**Files:** none (verification only)

- [ ] **Step 1: Build the whole solution**

Run from `norse-auth/`:

```
dotnet build Norse.Auth.slnx
```

Expected: every project builds; zero warnings, zero errors.

- [ ] **Step 2: Run every test in the solution**

Run from `norse-auth/`:

```
dotnet test Norse.Auth.slnx
```

Expected: ~17 tests pass (3 in Population, 6 in Audience, 1 in ClaimNames, 7 in NorsePrincipal, 7 in NorsePrincipalFactory, 3 in AnonymousPrincipalGenerator, 4 in AnonymousCookieAuthenticationHandler, 2 in AuthOptions). End-to-end tests deferred per Task 26.

- [ ] **Step 3: Confirm the 🟡 tasks did NOT execute**

Run: `Get-Content norse-auth/src/Norse.Auth.Server/AuthPlugin.cs | Select-String 'Norse.Abstractions.Hosting.IWebHostPlugin'`

Expected: **no output**. `AuthPlugin` is the standalone class form per Task 21; the interface-implementing form is what arrives when `Norse.Abstractions.Hosting` ships.

### Task 28: Update the meta-repo `.gitmodules` placeholder

**Files:**
- Modify (or Create): `.gitmodules`

For Plan A's duration the `norse-auth` directory is a subdirectory, not a true submodule. We add a placeholder entry in `.gitmodules` so the eventual submodule conversion is mechanical (rewrite the entry to point at a remote, run `git submodule add`).

- [ ] **Step 1: Check whether `.gitmodules` exists**

Run: `Test-Path .gitmodules`

Expected: probably `False` (the Primitives submodule is also a subdirectory in the current state). Skip Step 2 if `True`.

- [ ] **Step 2: Create or append**

If `.gitmodules` does not exist, create it with this content. If it does exist, append (preserving existing entries):

```ini
# Placeholder entries — these directories are subdirectories today.
# When each becomes a true git submodule with its own remote, rewrite the
# entry with `path = ...` and `url = ...` and run `git submodule add`.
[submodule "norse-auth"]
	path = norse-auth
	url = TODO-when-remote-exists
```

- [ ] **Step 3: Stage**

```
git add .gitmodules
```

Proposed commit message: `chore(meta): add norse-auth placeholder to .gitmodules`

### Task 29: Update `docs/Platform/specs/` cross-references

**Files:**
- Modify: `docs/Platform/specs/2026-05-20-auth-federation-design.md`

Add a note at the top of the spec pointing readers to Plan A. (Plan B–E will append themselves when written.)

- [ ] **Step 1: Edit the spec's frontmatter**

In `docs/Platform/specs/2026-05-20-auth-federation-design.md`, change the line:

```
**Companion specs:** `2026-05-19-architecture-analyzers-design.md` (will gain `YGG110` to forbid `[AllowAnonymous]`); future `auth-identity-contract-design.md` (claim shape detail, role taxonomy); future `auth-authorization-model-design.md` (RBAC/ABAC/policy decisions)
```

to:

```
**Companion specs:** `2026-05-19-architecture-analyzers-design.md` (will gain `YGG110` to forbid `[AllowAnonymous]`); future `auth-identity-contract-design.md` (claim shape detail, role taxonomy); future `auth-authorization-model-design.md` (RBAC/ABAC/policy decisions)
**Implementation plans:** `docs/Platform/plans/2026-05-20-auth-foundation.md` (Plan A of 5; B–E forthcoming)
```

- [ ] **Step 2: Stage**

```
git add docs/Platform/specs/2026-05-20-auth-federation-design.md
```

Proposed commit message: `docs(auth): cross-reference Plan A from the auth-federation spec`

---

## Plan A — Summary

> **Amendment (2026-07-25):** this plan halted at the plan stage (see the header note) and was never executed against `norse-auth` — the summary below describes an intended outcome, not a shipped one. The auth realm that actually shipped is Heimdall/Himinbjörg, with `IAuthenticationService`/`Outcome<T>`/`[GenerateGateway]` in place of `NorsePrincipal`/`Population`/`IAccountApi`. See `../../../../Heimdall/CLAUDE.md`.

After executing Tasks 1–29:

**Built (🟢):**
- `norse-auth` subdirectory scaffold (`.gitignore`, `.editorconfig`, `Directory.Build.props`, `LICENSE`, `README.md`, `Norse.Auth.slnx`).
- `Norse.Auth.Contracts` — `Population`, `Audience`, `ClaimNames`, `PrincipalSource`, `NorsePrincipal`, `NorsePrincipalFactory`, `InvalidNorsePrincipalException`.
- `Norse.Auth.Components` — empty Razor class library placeholder.
- `Norse.Auth.Server` (partial) — `AuthOptions`, `AnonymousBootstrap/*`, `AuthDbContext` stub, `AuthPlugin` skeleton, `AnonymousPrincipalGenerator` with inline UUID v5.
- `Norse.Auth.Contracts.Tests` — full coverage of contracts.
- `Norse.Auth.Server.Tests` — full coverage of `AnonymousPrincipalGenerator`, `AnonymousCookieAuthenticationHandler`, `AuthOptions` binding.
- `.gitmodules` placeholder.
- Spec cross-reference updated.

**Deferred to prerequisite plans/specs (🟡):**
- `AuthDbContext` migration to `Norse.Infrastructure.Persistence` per the persistence-inversion model (Norse.Auth.Server contributes entity classes + `IEntityTypeConfiguration<T>` impls; the DbContext itself lives in Infrastructure) — awaits `norse-infrastructure-persistence` spec/plan.
- `AuthPlugin` implements `Norse.Abstractions.Hosting.IWebHostPlugin` — awaits `Norse.Abstractions.Hosting` spec/plan.
- End-to-end `AnonymousBootstrapEndToEndTests` — awaits `Norse.Hosting.Web.Server` spec/plan.
- UUID v5 namespace constant replaced with the Primitives registry value — awaits the UUID registry spec.

**What Plan A delivers in isolation:**
- The typed envelope every other Norse context will read from `HttpContext.User`.
- The cookie-bound anonymous-principal mechanism that makes "every request carries a principal" implementable.
- A canonical wire-contract surface (claim names, audience strings, population taxonomy) that downstream plans (and analyzers) anchor to.

**Plans B–E roadmap (not written yet):**
- **Plan B — Identity Stores + Migrations:** Postgres `auth` schema; EF migrations for users, sessions, magic-link tokens, external identities, OpenIddict tables; signing-key storage + 90-day rotation; JWKS publication.
- **Plan C — OAuth Surface:** OpenIddict server configuration; token endpoint; discovery endpoints; JWT issuance with audience-scoped claims; refresh-token rotation with reuse detection; MCP pre-registered client.
- **Plan D — Sign-in Flows:** Staff (Google OIDC federation); Producer (invite-only enrollment + sign-in + agency-admin UI); Customer (registration + magic-link + social-additive linking); M2M (client credentials grant + admin UI for application management).
- **Plan E — Cross-Cutting Policies:** Rate limiting middleware; email normalization with plus-addressing config; account-linking strictness; signout semantics (revoke session + clear cookie + issue fresh anonymous); MFA enforcement per population; audit-event publication to `auth_events`.
