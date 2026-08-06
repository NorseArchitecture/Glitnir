# Wire Model Registration Guard — Design

**Status:** Approved for planning. Follows `../2026-08-03-surrogate-guard-race-filing.md` and `../plans/2026-08-06-blocking-surrogate-guard.md`, which fixed four instances of this defect by hand. A fifth, unrelated instance (Yggdrasil's `SwoopHostFixture`) turned up the same day in a hand-rolled test fixture no plan had touched. This spec closes the pattern for good instead of continuing to fix instances as they surface.

## 1. Problem

Five independent call sites have now carried the identical defect: a check-then-act or flag-first guard around mutating the shared, process-wide `RuntimeTypeModel.Default` (protobuf-net's runtime type model), racing under concurrent first touch. Each was fixed individually with a hand-rolled `Lazy<bool>` + `LazyThreadSafetyMode.ExecutionAndPublication`. That fix is correct per-site, but the *pattern* — reach for `RuntimeTypeModel.Add`/`.IsDefined` directly, guard it by hand, hope the guard is right — is what keeps reproducing the bug. Two of the five sites were hand-written application code, two were generator-emitted text, and one was a hand-rolled test fixture nobody expected to need registration-safety at all. There is nothing today that stops a sixth.

## 2. Goal

Make the wrong usage not compile. One shared, tested primitive owns the blocking-guard logic; an analyzer bans every other path to `RuntimeTypeModel.Add`/`.IsDefined`. Retrofit all five known sites onto the primitive so the analyzer's first day of enforcement starts clean, not red.

## 3. The Primitive

New file: `Midgard/src/Infrastructure.Web.Grpc/WireModelRegistrationGuard.cs`.

```csharp
public static class WireModelRegistrationGuard
{
	public static void EnsureRegistered(this RuntimeTypeModel model, Type key, Action register);
}
```

One generic API covers both shapes found in the wild:
- **Whole-model-once** (`IdentifierSerializers.Register`, `ResultSerializers.Register`, both generator-emitted guards): `key` is the registrant's own type (e.g. `typeof(IdentifierSerializers)`).
- **Per-type** (`SwoopHostFixture`'s `Outcome<ParityReport>` surrogate, and every per-payload `SetSurrogate` guard the generators already emit): `key` is the payload type itself.

Implementation: `ConditionalWeakTable<RuntimeTypeModel, ConcurrentDictionary<Type, Lazy<bool>>>` — get-or-add the per-model dictionary, then `GetOrAdd` the per-key `Lazy<bool>` with `ExecutionAndPublication`, `.Value` on it. Every caller either runs `register()` (exactly one winner) or blocks until the winner finishes; a throwing `register()` caches and rethrows its exception to every caller (documented once here, per the finding from the first fix's final review, instead of repeated at each call site).

**Test:** one concurrency regression test in the same spirit as the ones already shipped against `IdentifierSerializers`/`ResultSerializers` — many fresh models, many barrier-synchronized concurrent callers per model, asserting every caller observes the registered state by the time its own call returns. This test proves the *primitive*, once; call sites no longer need their own copy of this proof.

## 4. The Analyzer

New Midgard package: `gen/Infrastructure.Web.Grpc.Analyzers` → `Norse.Infrastructure.Web.Grpc.Analyzers`, mirroring Svartálfheim's `Architecture.Analyzers` project shape (netstandard2.0, `DiagnosticAnalyzer`, no analyzer-release ledger, matching the platform's existing generators/analyzers).

**NORSE080** — claims a new block. NORSE070-079 is explicitly the realm-dependency law ("architecture-law block"); this is a different concern (a single class's registration-safety pattern, not cross-realm boundaries) and doesn't belong folded into that package or that number range.

- **Rule, narrowed 2026-08-06 during Task 9's closing sweep: `Add`/`.Add<T>` only, not `IsDefined`.** The first draft banned `IsDefined` too, on the theory that the dangerous shape is "check `IsDefined`, then `Add`." But the danger is entirely in the unguarded *write* — a bare `IsDefined` read never mutates the model, so it can never itself be the TOCTOU race this rule exists to close, and banning `Add` alone already makes the check-then-act pattern impossible (nothing can `Add` outside the guard, so a preceding `IsDefined` read is inert either way). Concretely, `CompositionTests.cs` (Yggdrasil) has a legitimate read-only `RuntimeTypeModel.Default.IsDefined(...).ShouldBeTrue()` assertion with no paired `Add` nearby — banning `IsDefined` unconditionally would convict that with no correctness benefit. Any direct invocation of `ProtoBuf.Meta.RuntimeTypeModel.Add`/`.Add<T>` on a `RuntimeTypeModel`-typed receiver is banned, by symbol match (the same technique `WireFormatAnalyzer` already uses for its banned-symbol list).
- **Exemption, corrected 2026-08-06 during Task 6 review — read before touching this analyzer again.** The first draft of this rule exempted by *containing type* (the invocation's enclosing type must be `WireModelRegistrationGuard`). That's wrong: every legitimate call site — `IdentifierSerializers.Register`, `ResultSerializers.Register`, both generator-emitted guards — calls `Add`/`IsDefined` from inside the `register` *callback it passes to* `EnsureRegistered`, not from inside the guard's own type. The callback's containing type is the *caller's*, so a type-based exemption convicts every sanctioned call site the moment it's scattered. The correct exemption walks the operation's ancestor chain looking for an enclosing invocation of `WireModelRegistrationGuard.EnsureRegistered` (matching through the C# 14 extension-block wrapper type, since a call to an `extension(RuntimeTypeModel)` member's `TargetMethod.ContainingType` resolves to the compiler-synthesized extension grouping, not `WireModelRegistrationGuard` directly) — "is this call lexically part of an `EnsureRegistered(...)` invocation," not "is this call inside a specific type." This also means the guard's own body needs no separate exemption: `WireModelRegistrationGuard.EnsureRegistered` never calls `Add`/`IsDefined` itself, so nothing inside it ever needed exempting in the first place — a raw `Add`/`IsDefined` call added there directly (outside the callback mechanism) *should* still strike, and correctly does under the corrected design.
- **Not scoped by call-site context, but IS scoped by realm — corrected 2026-08-06, second correction.** The original delivery design scattered this analyzer platform-wide via Ginnungagap, on the reasoning "every consumer, including test projects, must go through the guard, unlike `WireFormatAnalyzer`'s realm exemption." That conflated two different axes. `WireFormatAnalyzer`'s realm exemption answers *which realms may touch wire format at all* — and NORSE070 already restricts that to Midgard and Yggdrasil, full stop; no other realm can even reference `ProtoBuf.Meta` without tripping NORSE070 first, so NORSE080 firing anywhere else is structurally impossible. What's genuinely *not* scoped is the second axis — *which code within an allowed realm* must go through the guard — and there the "not realm-scoped like WireFormatAnalyzer" framing was right: the bug was found in a Yggdrasil *test* fixture, and `WireFormatAnalyzer` exempts test assemblies entirely, so NORSE080 correctly does not. Scattering the analyzer to every realm platform-wide was therefore dead weight everywhere outside Midgard/Yggdrasil — harmless (it could never fire there) but unnecessary build-time cost with no corresponding benefit. Fresh, locally-created models (`RuntimeTypeModel.Create()`, not `.Default`) are never shared across threads by construction, but the analyzer can't safely tell "this local model will stay local" from "this local model will leak" by static analysis alone — so within the realms where it applies, the rule bans the `Add` symbol everywhere outside the guard file, full stop, regardless of which model instance receives the call. That's a deliberate over-approximation: a false positive here costs one extra `EnsureRegistered` call at a call site that didn't strictly need it; a false negative is the bug we keep finding.
- **Severity:** `NotConfigurable` error, matching NORSE070-079's posture — this is not a downgradeable preference.
- **Delivery, corrected 2026-08-06 (supersedes the original "scattered via Ginnungagap" design and Task 7 entirely):** bundled into `Norse.Infrastructure.Web.Grpc`'s own package (`analyzers/dotnet/cs/`), the exact same shape Svartálfheim's `Primitives.Analyzers` already uses for `Norse.Primitives` — not packed standalone, not delivered by Ginnungagap's platform-wide scatter mechanism. Every consumer of `Infrastructure.Web.Grpc` (which is precisely the set of code that could plausibly call `RuntimeTypeModel.Add` in the first place, since `WireModelRegistrationGuard`/`IdentifierSerializers`/`ResultSerializers` all live there) gets the analyzer automatically, transitively, through NuGet's own analyzer-asset propagation — no per-realm opt-in, no Ginnungagap entry, no Bifröst dev-mode unconditional block. In Bifröst dev-mode (`UseProjectReferences=true`), where analyzer `ProjectReference`s do *not* propagate transitively the way NuGet packages do, each Yggdrasil project whose own code (hand-written or generated) could call the guard declares an explicit `<NorseGeneratorRef Include="Infrastructure.Web.Grpc.Analyzers"><Repo>Midgard</Repo></NorseGeneratorRef>` — the same escape-hatch mechanism `Hosting.Web.Server.csproj` already uses for `Infrastructure.Web.Server.Xml.Generator`, chosen over `NorseRef Generator="true"` because that mechanism hardcodes a `.Generator` project-name suffix this project doesn't have.

**Tests:** same harness/style as `Architecture.Analyzers.Tests` (`Svartalfheim/tests/Architecture.Analyzers.Tests`) — positive case (direct `.Add`/`.IsDefined` outside the guard flags NORSE080), negative case (the same calls *inside* `WireModelRegistrationGuard.cs` do not), and a case proving `EnsureRegistered` itself is clean.

## 5. Retrofit

All five existing sites switch from their hand-rolled `Lazy<bool>` to `model.EnsureRegistered(key, () => { ... })`:

1. `Midgard/src/Infrastructure.Web.Grpc/IdentifierSerializers.cs` — key `typeof(IdentifierSerializers)`.
2. `Midgard/src/Infrastructure.Web.Grpc/ResultSerializers.cs` — key `typeof(ResultSerializers)`.
3. `Midgard/gen/Infrastructure.Web.Server.Generator/ServerRegistrationEmitter.cs` — emitted text calls `model.EnsureRegistered(typeof(NorseGrpcServerRegistration), ...)` instead of hand-emitting the `Lazy<bool>` field/property pair.
4. `Midgard/gen/Infrastructure.Web.Client.Generator/ClientRegistrationEmitter.cs` — same shape, `typeof(NorseGrpcClientRegistration)`.
5. `Yggdrasil/tests/Hosting.Web.Server.Tests/Swoop/TriProtocolSwoopTests.cs` (`SwoopHostFixture`) — the fix already staged there today gets replaced with a call through the shared primitive instead of its own local `Lazy<bool>` field, once the Midgard package carrying the primitive is available to reference.

This is the point of the exercise: once all five are retrofitted, turning NORSE080 on finds nothing left to flag. If it does, that's a sixth site this design didn't know about, and the analyzer just did its job.

## 6. Non-Goals

- No change to `IdentifierSerializers`/`ResultSerializers`'s public `Register(RuntimeTypeModel)` signatures — callers outside this defect class don't need to know the internals changed.
- No attempt to make `RuntimeTypeModel.Default` itself less of a shared-mutable-singleton — that's protobuf-net's own design; this closes the platform's *usage* pattern around it, not the library.
- No generalization beyond `RuntimeTypeModel` — this is not a general-purpose "once" utility for arbitrary resources, even though the underlying `Lazy<T>` technique would generalize. YAGNI until a second shared-singleton-registration problem actually shows up.
