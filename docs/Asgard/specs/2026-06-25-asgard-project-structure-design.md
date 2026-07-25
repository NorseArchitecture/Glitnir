# Asgard Project Structure Design

**Date:** 2026-06-25
**Status:** Approved — greenlight for implementation planning

## Decision

Six assemblies under `Norse.Abstractions.*`, split by dependency wall and consumer context. Everything shared lands in the smallest viable assembly; splits happen only where a hard wall demands it.

## Assembly Set

### `Norse.Abstractions.Contracts`

- **Project:** `src/Abstractions.Contracts/Abstractions.Contracts.csproj`
- **Upstream dependencies:** none
- **Contents:** `NorsePrincipal`, `Population`, published event interfaces, `IAccountApi`

  **Amendment (2026-07-25):** this direction was not taken. `Abstractions.Contracts` shipped instead via the transport-neutral-gateway track (PR #36, tag v0.0.10) with `Outcome<T>`/`Problem`/`ErrorCategory`/`BoolResponse`/`Unit` and `GenerateGatewayAttribute` (driving `gen/Abstractions.Contracts.Generator`) — no `NorsePrincipal`, `Population`, or `IAccountApi` anywhere in current source. See `docs/the-two-unions.md`.
- **Consumers:** `Norse.Abstractions.Worker`, `Norse.Abstractions.Web.Server`, all product `.Contracts` assemblies

### `Norse.Abstractions.Components`

- **Project:** `src/Abstractions.Components/Abstractions.Components.csproj`
- **Upstream dependencies:** none — must never pull in ASP.NET Core, EF Core, or any server-side infrastructure; MAUI pulls this directly
- **Contents:** Razor component base abstractions
- **Consumers:** `{Company}.{Context}.Components`, MAUI app

### `Norse.Abstractions.Backend`

- **Project:** `src/Abstractions.Backend/Abstractions.Backend.csproj`
- **Upstream dependencies:** `Norse.Primitives`, `Norse.Abstractions.Contracts`
- **Contents:** Shared server-side contracts visible to both Worker and Web.Server. Egress contracts live under the `Norse.Abstractions.Backend.Egress` namespace — `HttpResult<T>`, `EgressError`, `FailureKind`, `ResponseDisposition`, `EgressClassifier`, `IResponseParser<T>`, `IHttpEgress`. Additional server-side shared concerns land here as they emerge; a concern graduates to its own assembly only if a hard wall requires it.
- **Consumers:** `Norse.Abstractions.Worker`, `Norse.Abstractions.Web.Server`

### `Norse.Abstractions.Worker`

- **Project:** `src/Abstractions.Worker/Abstractions.Worker.csproj`
- **Upstream dependencies:** `Norse.Abstractions.Backend` (`Norse.Abstractions.Contracts` and `Norse.Primitives` are transitive)
- **Contents:** `IWorkerHostPlugin`, `ICommandRepository<T>`, `ICachedRepository<T>`, NServiceBus handler contract seams
- **Hard wall:** mutually invisible with `Norse.Abstractions.Web.Server`

### `Norse.Abstractions.Web.Server`

- **Project:** `src/Abstractions.Web.Server/Abstractions.Web.Server.csproj`
- **Upstream dependencies:** `Norse.Abstractions.Backend` (`Norse.Abstractions.Contracts` and `Norse.Primitives` are transitive)
- **Contents:** `IWebHostPlugin`, `IDocumentRepository<T>`, mediator law (`ICommandRequest<T>`, validator/authorizer contracts)
- **Hard wall:** mutually invisible with `Norse.Abstractions.Worker`

### `Norse.Abstractions.Migrations`

- **Project:** `src/Abstractions.Migrations/Abstractions.Migrations.csproj`
- **Upstream dependencies:** none
- **Contents:** `IMigrationContributor` (EF-free interface)
- **Isolation:** `Norse.Abstractions.Worker` and `Norse.Abstractions.Web.Server` carry no reference to this assembly — enforced by the absence of a project reference from either

## Dependency Graph

Arrows point from dependent → dependency.

```
Norse.Abstractions.Worker     ──┐
                                ├──→ Norse.Abstractions.Backend ──┬──→ Norse.Primitives
Norse.Abstractions.Web.Server ──┘                               └──→ Norse.Abstractions.Contracts

Norse.Abstractions.Components   (no upstream dependencies)
Norse.Abstractions.Migrations   (no upstream dependencies; not referenced by Worker or Web.Server)
```

## Solution Structure

```
Asgard/
  Asgard.slnx
  src/
    Directory.Build.props          (InternalsVisibleTo seam)
    Abstractions.Contracts/
      Abstractions.Contracts.csproj
    Abstractions.Components/
      Abstractions.Components.csproj
    Abstractions.Backend/
      Abstractions.Backend.csproj
      Egress/                      (namespace: Norse.Abstractions.Backend.Egress)
    Abstractions.Worker/
      Abstractions.Worker.csproj
    Abstractions.Web.Server/
      Abstractions.Web.Server.csproj
    Abstractions.Migrations/
      Abstractions.Migrations.csproj
  tests/
    Directory.Build.props
    Abstractions.Contracts.Tests/
    Abstractions.Components.Tests/
    Abstractions.Backend.Tests/
    Abstractions.Worker.Tests/
    Abstractions.Web.Server.Tests/
    Abstractions.Migrations.Tests/
```

## Shelved

`Norse.Abstractions.Infrastructure` (shared Docker container contracts — entity markers, audit/timestamp interfaces) is shelved. If a concrete need emerges it starts as a namespace in `Norse.Abstractions.Backend` and graduates to its own assembly only if a hard wall forces it.

## Impact on Prior Plans

The egress implementation plan (`../plans/2026-06-19-asgard-egress-contracts.md`) was written before this structure settled. It specifies a standalone `Norse.Abstractions.Egress` project. That project does not exist. The types and tests it defines are unchanged but they land in `Norse.Abstractions.Backend` under the `Norse.Abstractions.Backend.Egress` namespace, with source files under `src/Abstractions.Backend/Egress/` and tests in `tests/Abstractions.Backend.Tests/`. See the amendment note at the top of that plan.
