# gRPC Wire Format: Identifier Serializers and Code-First Reflection

**Date:** 2026-07-27
**Status:** Approved 2026-07-27 (session design review); §5 feasibility spike executed same day — verdict in §5.1, plain-`Guid` mechanism revised from custom serializer to the hook-driven member sweep
**Builds on:** `2026-07-27-mediator-pipeline-retires-gateway-design.md` (the live pipeline this hardens), `../../Svartalfheim/specs/2026-07-03-svartalfheim-identifiers-design.md` (the identifier types this puts on the wire)

---

## 1. Context

The mediator pipeline is live end to end: WASM→server gRPC-Web runs for real as of Yggdrasil v0.0.5. What has not been decided deliberately is the **wire format** underneath it — protobuf-net is currently running on defaults, and protobuf-net's defaults for identifiers are hostile to every consumer that is not legacy protobuf-net:

- At the default `CompatibilityLevel` (200), `Guid` serializes as `bcl.Guid` — two fixed64 fields carrying .NET's mixed-endian internal layout. A Python or Java consumer reading those bytes as a UUID gets garbage unless it reimplements Microsoft's byte shuffle.
- At `CompatibilityLevel.Level300`, `Guid` becomes a 36-character hyphenated string — cross-platform correct, but 36 bytes on the wire for a 16-byte value, on the platform's highest-traffic scalar.
- Level 300 *does* have the right shape hiding inside it: `DataFormat.FixedSize` on a `Guid` member produces a bare `bytes` field of 16 bytes in **RFC 9562 order**. Verified against protobuf-net source (`src/protobuf-net.Core/Internal/Level300FixedSerializer.cs` → `GuidHelper.Write(..., asBytes: true)`): the bytes are written in hex-string order with no endianness shuffle. But `DataFormat.FixedSize` is opt-in **per member**. Every forgotten annotation silently falls back to the 36-character string — a silent fallback on the exact axis this platform refuses to tolerate, and annotating members would drag `[ProtoMember]` (a protobuf-net dependency) into realm contract assemblies that are deliberately `[DataContract]`-pure.

Separately, `SequentialGuid` and `DeterministicGuid` (Svartálfheim, `Norse.Primitives.Identifiers`) have no protobuf-net story at all yet — and gRPC server reflection is wired with the stock `Grpc.AspNetCore.Server.Reflection` middleware, which builds its catalog from compiled `.proto` descriptors and therefore sees **zero** code-first services. `grpcurl` against the dev host today cannot discover or invoke the Norse surface, which blocks the trust-but-verify workflow the reflection endpoint exists for.

This design settles all three at once, because they are one subject: what the platform's gRPC surface actually looks like to anything that inspects it.

## 2. Wire law

1. **`CompatibilityLevel.Level300` is the platform's setting for every contract member**, applied explicitly at registration time — never inherited as an implicit default. (Mechanism corrected post-merge, §5.2: applied per `ValueMember` by the registration sweep, because protobuf-net forbids `DefaultCompatibilityLevel` on `RuntimeTypeModel.Default` — the semantics are identical, proven bit-for-bit.) Its non-identifier effects are accepted and named: `decimal` → invariant "general" string, `DateTime` → `google.protobuf.Timestamp`, `TimeSpan` → `google.protobuf.Duration`.
2. **`Guid`, `DeterministicGuid`, and `SequentialGuid` serialize as a bare `bytes` field: 16 bytes, RFC 9562 order** — bit-identical to protobuf-net's own Level 300 + `DataFormat.FixedSize` form. A partner in any language reads the field as a standard UUID; a plain protobuf-net peer using `DataFormat.FixedSize` interoperates without knowing Norse exists. The `.proto` a consumer sees is an honest `bytes` field, not a wrapper message.
3. **The legacy `bcl.Guid` encoding is structurally unreachable.** No member annotation, no per-contract discipline, no code review vigilance — the model itself cannot produce the mixed-endian form.
4. **SQL Server byte order never crosses the wire.** `GuidByteOrder.SqlServer` is a persistence-boundary arrangement owned by Urðarbrunnr's value converters at the database edge. The wire form is canonical RFC order, always; `Order` is not a wire concept and gets no discriminator.

## 3. Placement — `Infrastructure.Web.Grpc` (Midgard)

New Midgard project `src/Infrastructure.Web.Grpc` (assembly `Norse.Infrastructure.Web.Grpc`, brand injected per house convention). Its scope rule, stated once and enforced at review: **the shared gRPC recipe that touches all three consumption levels — MAUI, WASM client, and server.** App-specific wireup stays out: gRPC-Web browser plumbing belongs to the WASM host, service endpoint mapping to the server host, and whatever transport shape MAUI needs to the MAUI host. It is not walled off from the rest of the web infrastructure family — the point of the platform is arriving at the right recipe without re-deriving it.

Dependency edges:

- `protobuf-net` — direct reference plus CPM pin per the stale-floor cure (protobuf-net.Grpc declares a `>= 2.4.8` floor; lowest-wins hands out a five-year-old assembly unless the composition root pins).
- `NorseRef` to Svartálfheim's `Primitives` — **Midgard's first edge to the forge**, deliberately confined to exactly one csproj.
- `Infrastructure.Web.Client` and `Infrastructure.Web.Server` both reference it, so WASM hosts, the server host, and the future MAUI host all inherit it transitively. It never surfaces in any other realm.

Realm contract assemblies stay `[DataContract]`-pure: no `[ProtoMember]`, no protobuf-net reference, no wire-format annotations, ever. The wire format is imposed entirely at the model level by this one assembly.

## 4. The mechanism (as revised by the §5.1 spike verdict)

Two `internal sealed` implementations of protobuf-net's `ISerializer<T>` for the Svartálfheim types — **custom scalar serializers, not surrogates** — plus a model hook for plain `Guid`. The serializer/surrogate distinction is the wire shape: a `SetSurrogate` surrogate is a message, so every identifier member would become a length-prefixed submessage (`{ bytes value = 1 }`) — schema noise, +2–3 bytes per value, and not bit-compatible with the Level 300 fixed form. A custom serializer writes at the member position (`SerializerFeatures.WireTypeString | CategoryScalar`) and produces the bare `bytes` field of §2.

Plain `Guid` cannot take a custom serializer at all (§5.1) — it instead rides protobuf-net's own Level 300 `DataFormat.FixedSize` serializer, applied structurally: an `AfterApplyDefaultBehaviour` subscription sets `DataFormat.FixedSize` on every `Guid`/`Guid?` member of every type entering the model, however it enters (explicit `Add` or auto-discovery). Spike-proven hole-free: a contract type the registration code never saw still gets swept when the model discovers it.

| Type | Write | Read |
|---|---|---|
| `Guid` | built-in Level 300 fixed serializer via the hook sweep (RFC order, source-verified) | built-in; `Guid.Empty` round-trips as 16 zero bytes — a bare `Guid` carries no version claim, so no bit validation |
| `DeterministicGuid` | custom serializer: `Value` via `TryWriteBytes(span, bigEndian: true)` | exactly 16 bytes → `new DeterministicGuid(new Guid(span, bigEndian: true))` — the existing Svartálfheim ctor validates v5 version/variant bits and throws on garbage; any other length throws |
| `SequentialGuid` | custom serializer: `ToRfcOrder().Value` via the same big-endian write | exactly 16 bytes → `new SequentialGuid(guid, GuidByteOrder.Rfc9562)` — the existing ctor re-validates v7 version/variant bits and throws on garbage; any other length throws |

Notes:

- **One `SequentialGuid` serializer, not an RFC/SQL pair.** `Equals`/`GetHashCode` already normalize both orders to one identity, so normalize-on-write loses nothing, and the receiving side can never be handed an arrangement it was not expecting.
- **Byte-order code is not duplicated.** The RFC write is the framework's `bigEndian: true` overload; `SequentialGuidBytes` keeps sole ownership of the SQL Server shuffle, used here only via the already-public `ToRfcOrder()`.
- **Malformed payloads fail loudly at the boundary.** Zero bytes, truncated bytes, or wrong version bits throw during deserialization — nothing malformed crosses into the platform wrapped in a valid-looking struct.

Registration is a single public entry point:

```csharp
namespace Norse.Infrastructure.Web.Grpc;

public static class IdentifierSerializers
{
	/// <summary>Applies Norse wire law to <paramref name="model"/>: CompatibilityLevel 300, the Guid member sweep hook, and the two identifier serializers. Idempotent.</summary>
	public static void Register(RuntimeTypeModel model) { ... }
}
```

`Register` subscribes the `AfterApplyDefaultBehaviour` sweep and attaches the two custom serializers via `model.Add(type, applyDefaultBehaviour: false).SerializerType = ...`. The sweep applies the whole wire law per member: `CompatibilityLevel = Level300` on every field, `DataFormat.FixedSize` additionally on `Guid`/`Guid?` fields — it deliberately never touches `RuntimeTypeModel.DefaultCompatibilityLevel` (§5.2). Ordering constraint (documented on the method): it must run before contract types enter the model, since the sweep only sees types added after registration. It is idempotent because both generated registrations below may run against the same `RuntimeTypeModel.Default` in one process.

**Wiring:** the existing client and server registration generators (`Infrastructure.Web.Client.Generator`, `Infrastructure.Web.Server.Generator`) each emit one additional call to `IdentifierSerializers.Register(model)` beside the `RegisterNorseOutcomeSurrogates` wiring they already emit. No new generator — the identifier domain is static and known up front; hand-written serializers plus one emitted call line is the whole mechanism.

## 5. Feasibility spike (pre-plan gate)

The one unproven mechanism was overriding protobuf-net's **built-in** `Guid` handling model-wide: `RuntimeTypeModel.Add(typeof(Guid))` may refuse types with inbuilt behavior, and `MetaType.SerializerType` was the presumed v3 door. Twenty-minute POC against the pinned protobuf-net 3.2.56, pass criteria: registration succeeds; golden bytes match a `[ProtoMember(1, DataFormat = DataFormat.FixedSize)]` member at Level 300; `GetSchema` shows bare `bytes` fields. Hard default was the custom serializer for all three types, with a ValueMember sweep as the named fallback for `Guid`.

### 5.1 Spike verdict (executed 2026-07-27, three rounds, protobuf-net 3.2.56)

1. **Built-in `Guid` override: structurally impossible in v3.** `RuntimeTypeModel.Add(typeof(Guid))` throws `ArgumentException` ("Data of this type has inbuilt behaviour"). Source-confirmed: the guard is in the `MetaType` constructor itself (`MetaType.InbuiltType` for any type with an inbuilt core serializer), so no `MetaType` can ever exist for `Guid` — `SerializerType` and `SetSurrogate` are both unreachable. The fallback trigger fired.
2. **The fallback, upgraded: hook-driven sweep, proven hole-free.** `ValueMember.DataFormat` is publicly settable until the type freezes, and `RuntimeTypeModel.AfterApplyDefaultBehaviour` fires for every type entering the model — including auto-discovered types never explicitly `Add`ed. A subscribed handler setting `DataFormat.FixedSize` on `Guid`/`Guid?` members produced wire bytes bit-identical to the Level 300 fixed reference (`0A 10` + RFC-order payload) on a type the model discovered on its own, with `GetSchema` rendering an honest `bytes` field. The spec's stated fallback weakness ("a member in a type the sweep never saw silently falls back to string") is thereby eliminated — the hook *is* the sweep, applied at the moment any type enters the model.
3. **Custom `ISerializer` via `MetaType.SerializerType` for owned types: works on the wire.** A stand-in wrapper struct serialized bit-identical to the reference and round-tripped. **Named wart:** `GetSchema` renders such a member as a dangling type reference (`FakeSequentialGuid Id = 1;` with no message definition emitted) — invalid `.proto`. The wire is right; the schema is not. Resolving what the reflection service actually serves for these members (and whether the schema rendering can be corrected) is a named obligation of the implementation plan (§6 interaction).
4. **Surrogate-to-`Guid` for the owned types: dead, twice.** `SetSurrogate(typeof(Guid))` is accepted at registration but serialization throws (`ProtoException`, wire-type `None`), and `GetSchema` renders the member as `.bcl.Guid` — the legacy encoding by the back door. Rejected empirically; this also removes any temptation to add `Guid`→wrapper conversion operators to Svartálfheim.

**Verdict:** plain `Guid` = Level 300 + `AfterApplyDefaultBehaviour` sweep; `SequentialGuid`/`DeterministicGuid` = custom serializers; §4 is written in this revised form. Spike artifacts: scratch console project (session scratchpad, disposable); the golden vector `12345678-9abc-def0-1234-56789abcdef0` → `0A 10 12 34 56 78 9A BC DE F0 12 34 56 78 9A BC DE F0` carries into §7's tests.

### 5.2 Post-merge regression and second correction (2026-07-27)

The implementation of the §5.1 verdict shipped in Midgard PR #41 with `Register` doing `model.DefaultCompatibilityLevel = CompatibilityLevel.Level300` — and protobuf-net **categorically refuses that setter on `RuntimeTypeModel.Default`** (`RuntimeTypeModel.DefaultCompatibilityLevel`: "The default compatibility level of the default model cannot be changed"). The generated wiring registers against exactly that model, so every consumer compilation threw inside `RegisterNorseOutcomeSurrogates()` — after the once-only `Interlocked` guard had flipped — leaving the `Outcome<T>` surrogates unregistered. Symptom: six Yggdrasil `MediatorParityTests`/`CompositionTests` failures ("No marshaller available for `Outcome<LoginResult>`"). Midgard's own suite missed it because every test used `RuntimeTypeModel.Create()`; the §5.1 spike had the same blind spot.

**Correction (spike-proven bit-for-bit, fixed same day):** `Register` never touches `DefaultCompatibilityLevel`. The `AfterApplyDefaultBehaviour` sweep applies the whole wire law per member — `ValueMember.CompatibilityLevel = Level300` on every field (member-level wins over every ambient level; `MetaType.CompatibilityLevel` is not usable, it throws once fields exist), `DataFormat.FixedSize` additionally on `Guid`/`Guid?` fields. A swept model's payload for a `Guid`+`DateTime`+`decimal` contract is byte-identical to a true Level 300 + `DataFormat.FixedSize` model's, and registration against the real `RuntimeTypeModel.Default` round-trips the golden vector without throwing — pinned by the regression test `Applies_the_wire_law_on_the_default_model`, which exercises the generated wiring's exact call shape. Lesson recorded: any code destined for `RuntimeTypeModel.Default` must be tested against `RuntimeTypeModel.Default`.

## 6. Code-first reflection (Yggdrasil host)

`Hosting.Web.Server` swaps the stock reflection middleware for protobuf-net.Grpc's code-first implementation:

- Package: `Grpc.AspNetCore.Server.Reflection` → `protobuf-net.Grpc.AspNetCore.Reflection` (dependency-floor check on arrival; pin + direct reference if it drags a stale floor).
- `AddGrpcReflection()` → `AddCodeFirstGrpcReflection()`; `MapGrpcReflectionService()` → `MapCodeFirstGrpcReflectionService()`.
- The existing Development-only gate and its rationale comment stay exactly as they are — reflection hands the full catalog to anyone who can reach the endpoint.

This lives host-side, not in `Infrastructure.Web.Grpc`, per the §3 scope rule: reflection exposure is host policy, not shared recipe. Outcome: `grpcurl` lists and invokes the real code-first services against the dev host — the trust-but-verify door for reviewing live behavior instead of trusting the diff.

### 6.1 Live verification (2026-07-27)

Environment: the composed AppHost's migrations service is down with the known, unrelated Urðarbrunnr assembly-resolution gap (provider split infers the wrong migrations assembly — pre-existing, under separate triage), and the `web` resource gates on `WaitForCompletion(migrations)` — so `Hosting.Web.Server` was run directly (Development, port 7131) against the live `pg-primary` container's existing `norse_identity` database.

`grpcurl -insecure ... list` against the running server:

```
grpc.authentication.v1.AuthenticationService
grpc.reflection.v1alpha.ServerReflection
```

The code-first catalog is served — the stock middleware showed only `ServerReflection`. `describe` returns the full service (`Login`/`Logout`/`Register`) and every message descriptor, and the `Outcome<T>` passthrough surrogate shape is visible exactly as designed: `Login` returns `.grpc.authentication.v1.LoginResult` directly, no envelope message on the wire.

**§5.1(3) obligation status: open, unexercisable live today.** Every member of the current AuthN wire surface is `string`/`bool` — no contract yet places `Guid`, `SequentialGuid`, or `DeterministicGuid` on the wire, so how the served descriptors render identifier members cannot be observed against a real contract. The unit-level evidence stands (`GetSchema`: `Guid` members render an honest `bytes` field; custom-serialized members render a dangling type reference). The obligation transfers forward as a named watch-item: **the first contract that puts an identifier on the wire must check the served descriptor for that member before shipping** — if the dangling-reference wart reproduces in the reflection service's output, it becomes a defect to rule on then, with the wire itself already correct either way.

## 7. Testing

New `tests/Infrastructure.Web.Grpc.Tests` (one test project per package, `InternalsVisibleTo` per house convention):

- **Golden bytes** — a known UUID (e.g. `12345678-9abc-def0-1234-56789abcdef0`) serializes to the exact RFC byte sequence `12 34 56 78 9A BC DE F0 12 34 56 78 9A BC DE F0`; the assertion is on the raw payload bytes, not on a round-trip alone.
- **Round-trips** for all three types, including `Guid.Empty` and a `SequentialGuid` constructed in SQL Server order (asserting the wire value equals its `ToRfcOrder()` form and the rehydrated instance reports `GuidByteOrder.Rfc9562`).
- **Loud failures** — zero-byte and truncated payloads throw; well-formed 16-byte payloads with wrong version bits throw for `SequentialGuid` (non-v7) and `DeterministicGuid` (non-v5).
- **Hook coverage** — a contract type never explicitly `Add`ed to the model (auto-discovered at first serialization) still gets its `Guid` members swept to `DataFormat.FixedSize`; this is the structural no-silent-fallback guarantee, pinned as a test.
- **Schema assertion** — `GetSchema` shows bare `bytes` fields for `Guid` members; the expected rendering for custom-serialized members is settled by the §5.1(3) named obligation and pinned once known.
- **Cross-implementation equivalence** — the same value through a Level 300 + `DataFormat.FixedSize` `[ProtoContract]` member produces bit-identical payload bytes, proving "indistinguishable from protobuf-net's own form" as a test, not a claim.
- **Generator tests** — the existing client/server generator test suites extend to cover the one emitted `Register` call.

The reflection swap gets its end-to-end proof in Yggdrasil at plan time: `grpcurl` against the running dev host lists the code-first services and invokes one.

## 8. What does not change

- **Svartálfheim: nothing.** `DeterministicGuid(Guid)` and `SequentialGuid(Guid, GuidByteOrder)` already exist as validating wrap ctors; the forge's deliberately narrow surface already has every door the read side needs, and it takes no protobuf-net dependency.
- **Asgard: nothing.** Wire contracts stay `[DataContract]`-pure.
- **Urðarbrunnr: nothing.** It keeps sole ownership of SQL Server byte order at the persistence edge.
- **Realm contract assemblies: nothing** — the entire wire format is model-level.

## 9. Considered and rejected

- **Strict `SetSurrogate` surrogates** — wrapper submessages on the wire; breaks bit-compatibility with Level 300 fixed peers and adds schema noise. Rejected on wire-shape grounds — and the surrogate-to-`Guid` variant was additionally proven broken empirically (§5.1(4)): runtime `ProtoException` plus `.bcl.Guid` schema leakage.
- **Hand-rolling RFC byte order** (via `SequentialGuidBytes` or manual shuffles) — duplicates byte-order logic the framework (`bigEndian: true`) and Svartálfheim already own between them.
- **Per-member `[ProtoMember(DataFormat = FixedSize)]` in realm contracts** — drags protobuf-net into `[DataContract]`-pure assemblies and reintroduces the silent per-member fallback; the exact dependency creep this design exists to prevent.
- **A dual RFC/SQL `SequentialGuid` wire representation** — leaks a persistence arrangement into transport; the type's own equality already declares the two orders one identity.
- **A source generator for the serializers** — the identifier domain is three static, known types; hand-written serializers with one emitted registration call line is the entire need.
- **Folding reflection into the generated server mapping** — reflection exposure is a per-host security posture decision, not shared recipe.
