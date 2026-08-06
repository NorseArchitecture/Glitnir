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

## Landed (2026-08-06)

All four call sites now use `System.Lazy<bool>` with `LazyThreadSafetyMode.ExecutionAndPublication` in
place of the flag-first guard: `Norse.Infrastructure.Web.Grpc.IdentifierSerializers.Register` and its
sibling `ResultSerializers.Register` (found unpatched during Task 1's review — same defect shape, same
file family, not in the original filing) directly, each covered by a real concurrency regression test
(`Register_does_not_return_until_registration_is_complete_under_concurrent_first_touch` in
`IdentifierSerializersTests.cs` and `ResultSerializerTests.cs` respectively), and both generator emitters
(`ServerRegistrationEmitter`/`ClientRegistrationEmitter`), covered by an emitted-shape assertion in each
generator's test suite. Yggdrasil's `WireModelWarmup.cs` interim mitigation stays in place until
Yggdrasil's own `Directory.Packages.props` picks up the Midgard release carrying this fix — remove it
then, per this filing's original "until" clause.

**Scope boundary (recorded during final whole-branch review, 2026-08-06):** the four guards are
independent per call site and do not serialize against each other. `IdentifierSerializers.Register`
and `ResultSerializers.Register` each hold their own `ConditionalWeakTable<RuntimeTypeModel, Lazy<bool>>`,
and each generator-emitted `RegisterNorseOutcomeSurrogatesCore` (`ServerRegistrationEmitter`'s and
`ClientRegistrationEmitter`'s copies) holds its own separate `Lazy<bool>` field — a caller blocked on
one guard is not blocked on, and provides no ordering guarantee with respect to, any of the other three.
Confirmed unreachable in production today: the client and server registration paths don't run in the
same process. Worth recording anyway for a future reader who changes that — running both paths in one
process does not make registering through one guard block a concurrent caller going through another;
each guard protects only its own call site against `RuntimeTypeModel.Default`.
