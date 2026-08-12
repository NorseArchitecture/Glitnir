# Protobuf-net AOT Spike — Does the Wire Law Survive a Real Native-AOT Publish?

**Date:** 2026-08-11
**Status:** §2.2 spiked and answered 2026-08-12 (see §6, Amendment). §2.1 and §2.3 remain open. Not a design, not for implementation.
**Owner:** Buvy
**Trigger:** live conversation 2026-08-11, working session on Bifröst's `NorseFrontendPlatforms` MAUI-prep sweep (see `Platform/specs/` — no dedicated doc yet, the sweep itself was conversational) plus the same-day protobuf-net 3.3.0 CI break in Midgard it surfaced. `Buvy` shared <https://protobuf-net.github.io/protobuf-net/aot> mid-conversation; this spike exists to work out what that page actually means for this platform, not to speculate about it further in chat.
**Out of scope:** actually adopting the AOT source-generator model. Any MAUI head project (none exists yet). Anything about WASM's own trim/linker story, which is a separate mechanism from Native AOT and already ships today via `Infrastructure.Web.Client`/Yggdrasil's `Hosting.Web.Client` without this question being raised.

---

## 0. How to Use This Document

Written to be handed to a brand-new Claude Code context session with none of today's conversation in memory. §1 reconstructs the problem and cites the real shipped code so a fresh session can verify everything against actual source rather than trust this document blindly. §2 is the questions the spike needs to produce evidence on. §3 is explicit non-goals. §4 is a suggested execution order. §5 is what "done" looks like.

This is a spike — throwaway investigation on an isolated branch/worktree, not a merge target. It produces a recommendation (and ideally the seed of a real design doc if adoption is warranted), not shipped product code.

---

## 1. Context (self-contained)

### 1.1 What triggered this

Today (2026-08-11), `protobuf-net` published a new minor version — `3.3.0` at 12:54 UTC, then rapid patches up through `3.3.8` by 14:53 UTC (confirmed against the NuGet v3 registration API: `https://api.nuget.org/v3/registration5-semver1/protobuf-net/index.json`). Midgard's `src/Infrastructure.Web.Grpc/Infrastructure.Web.Grpc.csproj` floats `<PackageReference Include="protobuf-net" Version="3.*" />` — no Central Package Management, no lock file in this repo — so the very next CI run picked up the new minor and its stricter/new analyzer diagnostics.

Two diagnostics fired and broke `gate / build` on Midgard PR #74 (`sync/platform-config`, the Ginnungagap scatter auto-merge branch, unrelated cause — just unlucky timing):

- **PBN0022** on `tests/Infrastructure.Web.Grpc.Tests/ResultSerializerTests.cs`, `PlainEnvelope<T>.Value` (`[ProtoMember(1)] public T Value { get; set; } = default!;`) — "should use `[ProtoMember(..., IsRequired=true)]` to ensure its value is passed since it's initialized to a non-default value." Fixed for real (not suppressed): added `IsRequired = true`, because without it protobuf-net's normal default-value elision would silently drop `Value` from the wire whenever it equals `T`'s zero-default — exactly the class of bug these round-trip tests exist to catch.
- **PBN2008** ×4 on `tests/Infrastructure.Web.Client.Tests/Grpc/GrpcWebRoundTripTests.cs`, the `IProbeService` interface (`Task<Outcome<ProbeResponse>> SucceedAsync/FailAsync(...)`) — "gRPC methods require inputs/outputs that can be marshalled with gRPC; this type *may* be usable with gRPC, but it could not be verified." Suppressed with `#pragma warning disable/restore PBN2008` and a comment, because the analyzer can't see the file's own `RegisterSurrogates()` method registering `Outcome<ProbeResponse>` against `RuntimeTypeModel.Default` at runtime — proving that registration works end-to-end against a real ASP.NET Core TestServer is this file's entire point.

Both fixes are staged (not committed — house law) on Midgard's `sync/platform-config` branch as of this writing, verified locally with a clean `dotnet build -c Release` (0 warnings, 0 errors) on both affected test projects.

Reading protobuf-net's AOT doc (link above) in response, it turns out PBN2008 is exactly the diagnostic the doc's source-generator model exists to make moot — see §1.3.

### 1.2 What the wire law actually does today (runtime `RuntimeTypeModel`, not source-generated)

Midgard's `src/Infrastructure.Web.Grpc` project — "the shared gRPC wire-format recipe consumed at all three levels: MAUI, WASM client, and server" per its own `<Description>` — is built entirely on protobuf-net's **classic runtime reflection API**, not the AOT source-generator model the doc describes:

- `IdentifierSerializers.Register(RuntimeTypeModel model)` (`src/Infrastructure.Web.Grpc/IdentifierSerializers.cs`) hooks `model.AfterApplyDefaultBehaviour += ApplyWireLaw` (sweeps every field of every type added afterward to `CompatibilityLevel.Level300`, and every `Guid`/`Guid?` field to `DataFormat.FixedSize`) and registers custom `SerializerType`s for `SequentialGuid`/`DeterministicGuid` via `model.Add(typeof(...), applyDefaultBehaviour: false).SerializerType = ...`. Several more hand-written serializers sit beside it in the same project: `ResultSerializer(s)`, `ResultEnumSerializer`, `PiiResultSerializer`, `DateTimeOffsetSerializer`, `GuidWire`.
- `WireModelRegistrationGuard.EnsureRegistered(RuntimeTypeModel, Type key, Action register)` (`src/Infrastructure.Web.Grpc/WireModelRegistrationGuard.cs`) is the single choke point every registration site must go through — a blocking, keyed once-registration guard preventing a concurrent caller from observing a half-built model (design history: `Midgard/2026-08-03-surrogate-guard-race-filing.md`, `Midgard/specs/2026-08-06-wire-model-registration-guard-design.md`). Midgard's `gen/Infrastructure.Web.Grpc.Analyzers` enforces this as **NORSE080**: no `RuntimeTypeModel` mutation outside this guard, platform-wide.
- The generated client/server wiring (`AddNorseGrpcClients`/`MapNorseGrpcServices`, emitted by `gen/Infrastructure.Web.Client.Generator`/`gen/Infrastructure.Web.Server.Generator`) registers a per-contract `Outcome<T>` surrogate against `RuntimeTypeModel.Default` the same way, at runtime, guarded the same way — `GrpcWebRoundTripTests.cs`'s `RegisterSurrogates()` (the code PBN2008 fired on) is a hand-written mirror of exactly this generated shape, kept in the test deliberately so this assembly doesn't need `InternalsVisibleTo` from the generator's real output.

None of this is the `[ProtoModel]`/`[ProtoSerializable]` partial-`TypeModel` pattern the AOT doc describes. It's runtime reflection, registered once at process startup, cached in `RuntimeTypeModel.Default`.

### 1.3 What the AOT doc actually says (fetched and summarized 2026-08-11 — verify against the live page, it may move)

<https://protobuf-net.github.io/protobuf-net/aot>:

- **Mechanism:** declare a `partial class` deriving from `TypeModel`, decorated `[ProtoModel]`, listing top-level types via `[ProtoSerializable]`. A source generator builds the serializers **at compile time** — "your model becomes ordinary C# in your own project," not a runtime-reflected graph.
- **Requirements:** C# 12+ (platform is on C# 15 preview — clear). .NET 8.0+ for certain member shapes (init-only setters, non-public accessors, `[UnsafeAccessor]`) — platform is net11.0 preview, clear. Below net8.0, incompatible contracts are dropped with warnings, not build failures (irrelevant here).
- **Payoff claimed:** ~100× faster than runtime reflection on first serialization (JIT warm-up avoided entirely under Native AOT); ~3× even in an ordinary JIT build.
- **The model is closed.** "The generated model never consults the runtime model." An unsupported contract is *dropped*, not silently reflected — correct for AOT correctness, but means a dropped type throws `InvalidOperationException` at runtime instead of falling back. Build-time diagnostics to track: `PBN9001` (experimental, must be explicitly acknowledged/suppressed to opt in), `PBN2001`–`PBN2004` (dropped-contract warnings — these are the ones that would need active monitoring if adopted).
- **Constructor constraint:** generated models emit non-public constructors — anything relying on `Activator.CreateInstance()` against a contract type would break.
- **Known noise:** ~19 IL trim/AOT warnings on native publish, from reflection-based code paths the generated model doesn't actually execute — acknowledged by protobuf-net itself as noise, not a defect.

### 1.4 Why this is live right now, not hypothetical

This platform already makes two claims that this spike needs to reconcile against reality:

1. **`IsAotCompatible=true` is already platform-wide**, set in Ginnungagap's `config/src/Directory.Build.props` (comment: "Self-certify AOT/trim at the source so violations fail here, not downstream") — scattered into every realm's `src/` project, `Infrastructure.Web.Grpc`/`Infrastructure.Web.Client` included. That claim has never actually been exercised by a Native-AOT publish, because nothing on the platform does one yet.
2. **`NorseFrontendPlatforms` (new today, same working session)** — an opt-in MSBuild property in the same Ginnungagap `config/src/Directory.Build.props`, `All` tier declaring `<SupportedPlatform Include="ios/android/maccatalyst/windows/browser" />`. `Infrastructure.Web.Grpc` and `Infrastructure.Web.Client` are both on the `All` tier — set as local uncommitted edits this session, currently sitting in a `git stash` in the local Midgard checkout (not yet popped, committed, or pushed anywhere), alongside matching edits in Asgard/Heimdall/Naglfar/Mímisbrunnr/Yggdrasil/Svartálfheim. iOS mandates full Native AOT at publish time — there is no JIT fallback on that platform. The moment a real MAUI head exists and tries to publish for iOS, this is the code path that either works or doesn't.

No MAUI head exists yet (confirmed by repo scan: no `Microsoft.NET.Sdk.Maui`/`UseMaui` project anywhere on the platform, 2026-08-11). Per Buvy's own stated roadmap, real MAUI work is gated behind sweeping Himinbjörg's remaining AuthN components into Heimdall and standing up a real auth server first — so this spike is deliberately ahead of the need, not blocking anything today. That's the point: land the recommendation before the MAUI head shows up needing it, not after.

---

## 2. What's Actually Open — Questions For the Spike

### 2.1 Does the current runtime-reflection wire law survive a Native-AOT publish at all? (primary question)

Stand up the smallest possible Native-AOT-published console app (or reuse Svartálfheim's existing AOT smoke-test pattern — `tests/smoke/Primitives.Aot.Smoke` in that repo, `dotnet publish -c Release` then run the native exe, zero AOT warnings and exit 0 required — as the shape to imitate) that references `Infrastructure.Web.Grpc`, calls `IdentifierSerializers.Register` against a fresh `RuntimeTypeModel`, and actually serializes/deserializes a contract carrying a `SequentialGuid`, a `DeterministicGuid`, and an `Outcome<T>`-wrapped payload through the registered surrogate.

**Success criteria:** either (a) it just works — zero AOT/trim warnings, correct round-trip bytes, in which case the spike's answer is "no change needed, the platform-wide `IsAotCompatible=true` claim is actually true for this project" — or (b) it fails/warns in a specific, reproducible way, in which case capture exactly what breaks (missing metadata for a reflected type, a trimmed member, a `DynamicallyAccessedMembers` gap) as the evidence a real design decision would act on.

### 2.2 If it doesn't survive cleanly, does the `[ProtoModel]` source-generator model actually fit this platform's wire law?

The wire law isn't just "serialize some POCOs" — it's `CompatibilityLevel.Level300` swept via an `AfterApplyDefaultBehaviour` hook, custom `SerializerType`s for two identifier types, several more hand-written serializers, and a runtime-registered `Outcome<T>` surrogate *per gRPC contract* (there's no way to know the full set of contracts at the time `Infrastructure.Web.Grpc` itself compiles — that only becomes known once a specific realm's contracts assembly is compiled, which is exactly why the surrogate registration happens at each generated wiring call site, not once centrally).

Spike whether `[ProtoModel]`'s compile-time model can express: (a) a global per-member `CompatibilityLevel`/`DataFormat` sweep equivalent to `AfterApplyDefaultBehaviour`, or whether that has to move to a source generator of our own; (b) custom `SerializerType` registration for `SequentialGuid`/`DeterministicGuid` the same way; (c) a workable per-contract `Outcome<T>` surrogate story when the compile-time model can only know about types visible to *its own* compilation, not a downstream realm's contracts.

**Success criteria:** a concrete verdict — "yes, here's the shape" (ideally with a tiny working spike against one real contract, e.g. Heimdall's `IAuthenticationService`) or "no, here's specifically what doesn't fit, and here's what open protobuf-net issue/limitation blocks it."

### 2.3 Is this an all-or-nothing migration, or can the two models coexist?

`RuntimeTypeModel.Default` is shared, static, and mutated from multiple sites today (`IdentifierSerializers`, every generated wiring call, `WireModelRegistrationGuard`-guarded). Determine whether a `[ProtoModel]`-generated model can run *alongside* `RuntimeTypeModel.Default` for the contracts that need AOT-clean serialization (a future MAUI head) while server-only/WASM paths keep using the existing runtime model unchanged — or whether adopting the source-generator model is necessarily platform-wide, all realms, one migration.

**Success criteria:** a clear answer on coexistence, since it directly determines whether this is a narrow "Yggdrasil's future MAUI head only" slice or a "every realm with a gRPC contract" migration.

---

## 3. Explicit Non-Goals

- **No production code changes.** This spike does not touch `Infrastructure.Web.Grpc`, `Infrastructure.Web.Client`, or any generated wiring in a way that ships. Throwaway spike project only, isolated branch/worktree.
- **No MAUI head project.** Don't scaffold one to test this — the AOT smoke-test pattern (§2.1) doesn't need a real MAUI app, just `PublishAot=true` (or MAUI's own iOS-equivalent AOT publish flags, if that turns out to matter) on a disposable console project.
- **No decision on *when* to adopt.** Even a clean "yes, this works, here's the shape" verdict does not itself authorize implementation — that's a separate brainstorm → spec → plan cycle per `../house-rules.md`/`../../CLAUDE.md` §2.8, same as everything else on this platform, and it's gated behind the MAUI head actually existing per the roadmap note in §1.4.
- **No re-litigating protobuf-net as the wire library.** That choice (protobuf-net.Grpc over `Grpc.AspNetCore` + `.proto` files) is settled platform history; this spike is about *how* protobuf-net is used, not *whether*.

---

## 4. Suggested Execution Order

1. §2.1 first — it's the cheapest to falsify and gates everything else. If the current runtime-reflection approach just works under Native AOT, §2.2/§2.3 may be moot (or at most "nice to have, not needed").
2. Only if §2.1 finds real breakage: §2.2, spiked against one real contract rather than a toy type, so the verdict means something.
3. §2.3 last — it's a scoping question that only matters once §2.2 has established there's a real migration to scope.

## 5. What "Done" Looks Like

A short recommendation doc (or an amendment appended to this one) answering, in order: does the current wire law survive Native AOT as-is; if not, does the source-generator model fit; if adoption is warranted, is it a narrow slice or a platform-wide migration. Evidence — actual AOT publish output, actual warning/error text, actual round-trip byte comparisons — cited inline, not asserted. No code merged as part of this spike; the next brainstorm/spec/plan cycle picks up from the recommendation if one is warranted.

---

## 6. Amendment (2026-08-12) — §2.2 Spiked and Answered

Executed in an isolated worktree off `origin/master` (branch `spike/protobuf-net-aot-outcome-result`, one local commit, never pushed, since removed). Original Midgard checkout (`sync/platform-config`, its staged changes, its stash) was never touched. §2.1 (does the *current* runtime-reflection wire law survive a Native AOT publish as-is) was **not** spiked — out of scope for this pass, still open. §2.3 (can `RuntimeTypeModel.Default` and a `[ProtoModel]`-generated model coexist in one process) was **not** directly tested either, though the verdict below implies an answer: see the closing note.

### §0 — a stale doctrine claim, corrected

Svartálfheim's `CLAUDE.md` claimed Heimdall's `RegisterRequest` carries a wire-level `Result<EmailAddress> EmailParsed`, hydrated by a setter side-effect. It never did: `git log --all -S "EmailParsed"` in Heimdall returns zero commits. `Result<T>` as a *direct* `[ProtoMember]`/`[DataMember]` field exists **only** in Midgard's test fixtures (`PiiResultSerializerTests.cs`, `ResultSerializerTests.cs`) — every shipped production wire request today is pure scalars. Doctrine (`the-two-unions.md`) still describes `Result<T>` composing directly onto wire records as the *intended* future shape, and NORSE060 only forbids it on responses, leaving requests open — so this spike targeted the doctrine's intended shape as the thing worth answering for, correctly flagged as not-yet-used-in-production-anywhere. (Svartálfheim's `CLAUDE.md` itself needs its own fix for the stale claim — not done as part of this amendment; flagging it here so it doesn't get lost.)

### §2.2(a) — `Outcome<T>` on responses: **yes**, cleanly, verified under real Native AOT

`[ProtoSurrogate(typeof(Outcome<Payload>), typeof(Payload))]`, cast-based (`Outcome<T>`'s own `implicit`/`explicit` operators, no `Converter`). Compiled with zero dropped-contract diagnostics (`PBN2001`–`PBN2004` never fired against it). Round-tripped byte-identical to the bare payload:

```
Outcome<Payload> bytes (8): 0A04627576791003
bare Payload    bytes (8): 0A04627576791003
```

A `Failed` outcome correctly throws rather than reaching the wire. Verified twice — once under JIT, once inside an actually-published `PublishAot=true` native ELF binary (2.5 MB, real, not a stub) — identical output both times.

One real (minor) protobuf-net generator defect surfaced along the way: the generator emits an `[UnsafeAccessor]` to reach a surrogate-target member's `init` setter, but types the accessor's `target` parameter as the *surrogated* type instead of the *surrogate* — compiles (Roslyn doesn't fully verify an `UnsafeAccessor` target across a closed generic instantiation), throws `MissingMethodException` at first real deserialize. Workaround: use `set` instead of `init` on a `[ProtoSurrogate]` target's own members. **This bug is specific to the surrogate mechanism's "inline the surrogate's members" codegen path — confirmed (2026-08-12, live re-test) to NOT reproduce for an ordinary `init` member whose *type* carries a hand-written `[ProtoContract(Serializer=...)]` scalar serializer** (see §2.2(b) below); different codegen shape entirely, no body emitted at all for that path.

### §2.2(b) — `Result<T>` on requests: **no** via `[ProtoSurrogate]`, but the real fix is a different, better-fitting mechanism

Bare BCL scalars are refused outright as surrogate targets (`PBN2002` — the target must itself be `[ProtoContract]`/`[DataContract]`/`[XmlType]`-shaped or a tuple). Wrapping in a minimal `[ProtoContract] record NullableInt32 { [ProtoMember(1)] public int? Number; }` compiles, and the surrogate itself is free — `ResultEnvelope` (via surrogate) and `WrapperEnvelope` (the wrapper type directly) produce byte-identical wire bytes (`0A02082A`) — but that's **+2 bytes** versus a genuinely bare `int` (`082A`), an unavoidable nested-message cost the runtime `ResultSerializer<T>` doesn't pay today.

The deeper, structural failure: the generated `Read` scaffolding for *any* `[ProtoSurrogate]` primes the surrogate by calling the user's `ToSurrogate` conversion on the incoming merge-target **before any wire byte is read** — `default(Result<int>)` on a fresh deserialize — to support protobuf's merge-into-existing-instance semantics (confirmed straight from `ProtoModelGenerator.Emit.cs`: `var surrogate = ToSurrogate(value); surrogate = serializer.Read(...); return ToUnderlying(surrogate);`, same shape on Write). `Result<T>`'s illegal-write law (throw on default/failed) fires inside that priming call, before real data is ever read — confirmed by a controlled experiment: temporarily making `ToSurrogate` tolerate `default` (return an empty wrapper instead of throwing) made the round trip pass immediately; reverted once diagnosed. **This is not a struct-vs-class limitation** — `Outcome<T>` (a class) survives the identical priming call only because *its own* conversion operator was deliberately written null-tolerant for exactly this reason; the axis is total-vs-partial conversion function, not value type vs reference type. A partial conversion function enforcing a domain invariant is structurally incompatible with `[ProtoSurrogate]`'s Read scaffolding as it stands, for any type.

**The better fix, identified and spot-verified 2026-08-12**: don't use `[ProtoSurrogate]` for `Result<T>` at all — use the *hand-written-serializer* mechanism (`[ProtoContract(Serializer = typeof(X), IsScalar = true)]`), which today only works contract-level (on a type you own). This is the compile-time equivalent of exactly what the runtime model already does for `Result<T>` (`ResultSerializer<T> : ISerializer<Result<T>>`) — and it sidesteps the merge-priming problem **entirely, by construction**, not just in practice: protobuf-net's own contributor notes describe this route as emitting "no body at all" — the generator wires up `SerializerCache.Get<X,T>()` and controls passes straight into the hand-written `Read`, no generator-injected priming call at all. Confirmed by reading the real `ResultSerializer<T>.Read` (Midgard's shipped code): it never touches its incoming merge-target parameter — only `Write` enforces the illegal-write law. There is no priming call for a throw to happen inside.

A live spot-check (`[ProtoContract(Serializer = typeof(BoxedIntSerializer), IsScalar = true)] readonly record struct BoxedInt(int Value)`, used as an `init`-only member on an ordinary `[ProtoContract]`) compiled and round-tripped cleanly — confirming the `init`/`[UnsafeAccessor]` bug found in §2.2(a) doesn't reproduce on this path either. Separately, real `SequentialGuidSerializer`/`ResultSerializer<T>` source confirms Guid-shaped hand-written serializers already hardcode their own wire format (`GuidWire.Read`/`Write`, explicit `SerializerFeatures`), independent of any model-level `DataFormat` sweep — so a `Result<Guid>` hand-written serializer under this route needs no cross-cutting Guid `DataFormat` attribute either.

**The gap**: `[ProtoContract(Serializer=...)]` has no assembly/model-level form for declaring a hand-written serializer on a type you *don't* own — unlike `[ProtoSurrogate]`, which already has exactly that (how the NodaTime package ships surrogates for `Instant`/`Duration` without touching them). Adding one closes this cleanly. Follow-on brainstorm doc for the upstream contribution: `2026-08-12-protobuf-net-aot-upstream-serializer-dataformat-brainstorm.md` (same directory).

### §2.2(c) / §4 — `CompatibilityLevel`/`DataFormat` global sweep

`[ProtoModel]` exposes exactly one cross-cutting knob (`AllowParseableTypes`; confirmed against `protobuf-net.Core.xml` 3.3.8 — no others exist). `[module: CompatibilityLevel(...)]`/`[assembly: CompatibilityLevel(...)]` are real and give a genuine compile-time equivalent to half of `IdentifierSerializers`' sweep. **No equivalent exists for `DataFormat.FixedSize`-per-`Guid`** at any cross-cutting scope — but per §2.2(b) above, this only matters for a genuinely *bare* `Guid` field; every `Guid` reached through a hand-written serializer (`Result<Guid>`, `SequentialGuid`, `DeterministicGuid`) is unaffected, since those already hardcode their own wire format today. Also covered in the follow-on brainstorm doc as Proposal 2.

### Verdict against §5's ask

**Partial fit, not a clean yes.** `Outcome<T>` (responses) is AOT-ready today, no upstream changes needed. `Result<T>` (requests) is not, via the mechanism this spike first tried (`[ProtoSurrogate]`) — but a different, better-fitting mechanism (`[ProtoContract(Serializer=...)]` extended to assembly scope) looks like it would close the gap cleanly, pending the actual upstream change existing to test against. Until that lands, any real adoption would necessarily be **mixed** — `Outcome<T>`-bearing responses on a `[ProtoModel]`-generated model, `Result<T>`-bearing requests staying on `RuntimeTypeModel.Default` — which is suggestive evidence for §2.3 (coexistence is *necessary*, not just possible) without being a direct test of it (whether the two models can literally run side-by-side in one process, sharing a gRPC channel, remains unverified). §2.1 remains fully open. Not a decision to adopt — the upstream gap needs to close first, per the brainstorm doc.
