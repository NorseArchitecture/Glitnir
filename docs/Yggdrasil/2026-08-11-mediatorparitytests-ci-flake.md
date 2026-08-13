# CI flake — `MediatorParityTests`, 2026-08-11

**Status:** observed, not investigated, not fixed. This note exists so a cold session has a starting
point — not a postmortem, no root cause claimed below.

## What happened

Yggdrasil PR #213 (`feature/session-transition-seam`, commit `05855de`), CI run
[31471742614](https://github.com/NorseArchitecture/Yggdrasil/actions/runs/31471742614):

- Attempt 1 ([job 93716360898](https://github.com/NorseArchitecture/Yggdrasil/actions/runs/31471742614/job/93716360898?pr=213)) — `gate / build` **failed**.
- Attempt 2 ([job 93717211243](https://github.com/NorseArchitecture/Yggdrasil/actions/runs/31471742614/job/93717211243?pr=213)) — same commit, zero code changes, **succeeded**.

Confirmed a genuine flake, not a real regression: identical `headSha` both attempts.

## What failed (attempt 1 only)

Three tests in `Norse.Hosting.Web.Server.Tests.MediatorParityTests` (`tests/Hosting.Web.Server.Tests/MediatorParityTests.cs`):

- `LockedOut_renders_identically_through_the_circuit_path_and_the_wire_path` — expected
  `wireFailed.Problem.Category` to be `ErrorCategory.LockedOut`, got `ErrorCategory.Fault`.
- `Parameterless_logout_crosses_the_wire` — threw during `CreateWireClient(host).Logout(cancellationToken)`.
- `A_handler_throw_reaches_the_wire_client_as_Fault_with_a_correlation_id` — assertion on
  `failed.Problem.CorrelationId` failed.

All three failures came out of the same test-assembly run (`Norse.Hosting.Web.Server.Tests.dll`,
`+65/x3/?1`), same ~14s window. Shape of the failures (wrong `ErrorCategory`, wrong/missing
correlation data) reads like cross-test state bleed rather than three independent logic bugs — but
that's an impression, not a finding.

## Where to start looking, if picked back up

Not investigated beyond this. Worth checking first:

- `MediatorParityTests.cs` spins up an in-process `TestServer` + gRPC channel per test
  (`host.GetTestServer().CreateHandler()`), with test-local command/handler types
  (`TestLoginCommand`, `StubLoginHandler`) whose handler behavior is "swapped per test instead of
  per assertion" (per the file's own doc comment) — if any of that is actually shared/static rather
  than genuinely per-test, concurrent test execution would explain the symptom.
  - What ties in here, worth ruling in or out: `PolicyCache<TRequest>` at
    `Midgard/src/Infrastructure.Web.Server/Mediator/PolicyCache.cs` — a generic **static** cache
    closed per request type, consulted by `AuthorizationBehavior`. Not confirmed as the actual
    race — just the one shared-static-state candidate spotted while pulling the CI log — but its
    generic-closed-per-type shape is exactly the kind of thing that looks safe (`static readonly`
    init is thread-safe) but can still surface if something about test parallelization violates the
    per-type-closed assumption.
  - Whether xUnit v3 is parallelizing across these specific tests/classes at all is unconfirmed —
    check `AssemblyInfo`/`.runsettings`-equivalent config before assuming.

## Companion context

- Human noted this is a known, recurring "xUnit race" — implies this isn't the first sighting, just
  the first one written down.
- Landed mid the session-transition-seam plan's Gate 3 (Yggdrasil) — unrelated to that plan's own
  changes (the failing tests are pre-existing `MediatorParityTests`, not anything Gate 3 touched).
