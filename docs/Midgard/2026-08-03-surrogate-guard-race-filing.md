# Filing: Generated Surrogate-Registration Guard Races Under Concurrent First Touch

**Filed 2026-08-03** · Court filing, not a spec — the defect and its interim mitigation are recorded here so the emitter fix is a scheduled act, not a rediscovery.

## The Defect

Both wiring emitters (`Midgard/gen/Infrastructure.Web.Server.Generator/ServerRegistrationEmitter.cs`, `.../Infrastructure.Web.Client.Generator/ClientRegistrationEmitter.cs`) emit `RegisterNorseOutcomeSurrogates()` with a **flag-first, non-blocking** guard:

```csharp
if (Interlocked.Exchange(ref _surrogatesRegistered, 1) == 1)
	return;                       // second caller returns INSTANTLY...
var model = RuntimeTypeModel.Default;
IdentifierSerializers.Register(model);   // ...while the first is still registering
// ...N surrogate guards...
```

A second thread calling during the first thread's registration window proceeds immediately and serializes against a half-built `RuntimeTypeModel.Default`. Observed live (2026-08-03): Yggdrasil's `Hosting.Web.Server.Tests` runs three wire-exercising xUnit collections in parallel; on the x64 CI runner the race produced uniform transport-level failures with no Norse trailers — every wire call decoded as `Fault` with a null correlation id (`MediatorParityTests`, 4 failures) — while arm64-local timing never tripped it. Production exposure is minimal (`MapNorseGrpcServices`/`AddNorseGrpcClients` call it once during single-threaded startup) but the emitted shape is wrong on its own terms.

## Interim Mitigation (shipped)

`Yggdrasil/tests/Hosting.Web.Server.Tests/WireModelWarmup.cs` — a `[ModuleInitializer]` completes the registration single-threaded before xUnit spins up any collection. Test-assembly-local; any future test assembly with parallel wire fixtures needs the same line until the emitter is fixed.

## The Real Fix (scheduled, Midgard)

The emitted guard becomes blocking — the canonical shapes: a `static readonly` initialization (CLR type-init guarantees), or `LazyInitializer`/`Lock` double-check so late callers *wait* for completion instead of skipping past it. One emitter change, both generators, covered by a concurrency fixture in the generator tests. Rides the next Midgard fork per the one-fork law; remove the Yggdrasil warmup's "until" clause when it lands.
