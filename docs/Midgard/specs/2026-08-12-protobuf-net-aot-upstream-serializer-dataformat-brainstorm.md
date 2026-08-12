# protobuf-net AOT — Two Upstream Contribution Candidates

**Date:** 2026-08-12
**Status:** Brainstorm draft — input to a protobuf-net upstream clone/PR, not a Norse platform design. No Glitnir ratification needed; this doesn't touch platform law, it proposes patches to a third-party library.
**Owner:** Buvy
**Provenance:** Follow-on from `2026-08-11-protobuf-net-aot-poc.md` §6 (the spike that first found the gap). Read that amendment first — this document assumes its findings.
**Target repo:** `github.com/protobuf-net/protobuf-net`, version spiked against: `3.3.8`.

---

## 0. The one-line pitch for each proposal

1. **`Result<T>` needs an assembly-level way to declare a hand-written serializer for a type you don't own** — `[ProtoSurrogate]` already has this; the sibling hand-written-serializer mechanism (`[ProtoContract(Serializer=...)]`) doesn't.
2. **`DataFormat` needs a cross-cutting default, the way `CompatibilityLevel` already has one** — for bare BCL scalars (starting with `Guid`) that never touch a custom serializer.

Both are additive. Neither changes existing behavior for anyone not opting in.

---

## 1. Problem 1 — `Result<T>` can't reach AOT parity through `[ProtoSurrogate]`

### 1.1 What was tried and why it fails

The natural-looking approach — `[ProtoSurrogate(typeof(Result<int>), typeof(NullableInt32), Converter = typeof(ResultInt32Converter), ...)]` — compiles and *mostly* works, but fails on read with:

```
System.InvalidOperationException: a failed or default Result<T> is illegal to write
   at ResultInt32Converter.ToSurrogate(Result`1 value)
   at ISerializer<Result<int>>.Read(ref ProtoReader.State state, Result<int> value)
```

Root cause, read straight out of `ProtoModelGenerator.Emit.cs`'s emitted shape:

```csharp
var surrogate = ToSurrogate(value);              // value = default(Result<int>) — no bytes read yet
surrogate = surrogateSerializer.Read(ref state, surrogate);
return ToUnderlying(surrogate);
```

`value` is the incoming merge-target, not real data — the generator calls the user's `ToSurrogate` on it purely to obtain a starting instance to populate, so that repeated/nested fields merge onto an *existing* surrogate rather than replacing it (legitimate, documented, load-bearing merge semantics — not a bug in isolation). `Result<T>`'s illegal-write law throws exactly there, before any wire byte is read.

**Confirmed by controlled experiment**, not inferred: temporarily changing `ToSurrogate` to return `new NullableInt32()` instead of throwing on `default` made the round trip pass immediately. Reverted once diagnosed — the point was to isolate the cause, not to ship a weakened law.

### 1.2 Why "support structs" is the wrong diagnosis

Structs already work as `[ProtoSurrogate]` underlying/target types in general — protobuf-net's own test suite proves it (`Ticks` in `ModelSurrogate.input.cs`, predating this investigation), and this spike's own `NullableInt32` wrapper compiled and partially round-tripped. The failure isn't type-system-level; it's behavioral.

The real axis: **total vs. partial conversion function.** `Outcome<T>` survives the identical priming call only because *its own* `explicit operator T(Outcome<T>)` was deliberately written null-tolerant (`outcome is null ? default! : ...`) — a design choice in Norse's own code, not something protobuf-net does automatically. `Result<T>`'s conversion can never be written that way without gutting the illegal-write law `[MustConsume]` exists to enforce. A class with the same "throw on unset state" law would hit an identical wall — struct-ness is incidental.

### 1.3 Two candidate fixes, and why we're proposing the second

**(A) Change the generator's merge-priming behavior.** Don't call the user's converter to prime when the merge-target is the underlying type's own `default` (no existing value to merge into) — construct the surrogate side via `default(TSurrogate)` directly in that case, only invoking the converter when there's a genuine existing value. This is a real behavior change to documented, load-bearing merge semantics for *every* `[ProtoSurrogate]`/`SetSurrogate` consumer upstream, not just us. High blast radius, would need careful design and real scrutiny from maintainers. **Not the one we're pursuing first.**

**(B) Give the hand-written-serializer mechanism the same assembly-level reach `[ProtoSurrogate]` already has.** This is the one. See §2.

---

## 2. Proposal 1 (Option B) — assembly-level external serializer declaration

### 2.1 What exists today

`[ProtoContract(Serializer = typeof(X), IsScalar = true)]` — a **contract-level** attribute. Per protobuf-net's own contributor notes: this means the contract has a hand-written serializer, so the generator "emits no body at all" — the services type implements `ISerializerProxy<T>` handing that serializer out, and members pass `SerializerCache.Get<X, T>()` rather than going through any generated Read/Write body. This is the *exact* compile-time equivalent of what Midgard's runtime `RuntimeTypeModel` already does for `Result<T>`:

```csharp
model.Add(typeof(Result<T>), applyDefaultBehaviour: false).SerializerType = typeof(ResultSerializer<T>);
```

The catch: `[ProtoContract(...)]` has to go **on the type itself**. `Result<T>` is Svartálfheim's open generic, doctrine-required to carry zero protobuf-net attributes (the NORSE070 realm boundary) — so this route is closed for `Result<T>` as things stand, even setting aside that you can't attribute one closed instantiation of an open generic definition from outside anyway.

`[ProtoSurrogate]`, meanwhile, **already has** an assembly-level form specifically for declaring behavior on types you don't own:

```csharp
[assembly: ProtoSurrogate(typeof(Uri), typeof(UriSurrogate))]
```

This is how `protobuf-net.NodaTime` ships surrogates for `Instant`/`Duration` without ever touching those types — declarations are gathered least-to-most-specific (referenced assemblies → this assembly → the model), so a consumer can always override a library's choice, and the "three-assembly hand-off" (types in one package, the helper that knows how to serialize them in a second, the consumer in a third referencing only the helper) is a real, tested, working pattern (`ProtoSurrogateReferenceTests`).

**There is no equivalent for the hand-written-serializer mechanism.**

### 2.2 The proposal

Add an assembly (and/or module) level attribute — working name `ProtoExternalSerializerAttribute` — that does for `[ProtoContract(Serializer=...)]` what `[assembly: ProtoSurrogate(...)]` already does for surrogates:

```csharp
[assembly: ProtoExternalSerializer(typeof(Result<int>), typeof(ResultSerializer<int>), IsScalar = true)]
[assembly: ProtoExternalSerializer(typeof(Result<Guid>), typeof(ResultSerializer<Guid>), IsScalar = true)]
[assembly: ProtoExternalSerializer(typeof(Result<EmailAddress>), typeof(PiiResultSerializer<EmailAddress>), IsScalar = true)]
// ... one per closed instantiation the platform's taxonomy needs — mirrors exactly how
// ResultSerializers.cs enumerates ~19 BCL scalars + 4 PII types at runtime today.
```

Consuming-side codegen requirement: when the generator sees `Result<int>` referenced (as a seed or as a member type) and an external-serializer declaration exists for it, emit the *same* "no body at all, `ISerializerProxy<T>` / `SerializerCache.Get<X,T>()`" shape that the contract-level form already emits — no new codegen shape needed, just a new *discovery* path feeding the existing emission.

### 2.3 Why this closes the gap completely, not just partially — three independent confirmations

This isn't just "avoids the specific bug found." Spot-checked live against 3.3.8 (2026-08-12, same worktree as the original spike) using the *contract-level* form as a stand-in for what the assembly-level form would enable, since the codegen shape is identical either way:

1. **No merge-priming call exists on this path at all**, so there's no throw-on-default hazard to work around. Confirmed both architecturally (protobuf-net's own "no body at all" description) and by reading the real `ResultSerializer<T>.Read` — it never touches its incoming merge-target parameter; only `Write` enforces the illegal-write law. There's no priming call for a throw to happen inside.

2. **`init`-only members work.** A live test — `[ProtoContract(Serializer = typeof(BoxedIntSerializer), IsScalar = true)] readonly record struct BoxedInt(int Value)` used as an `init`-only member on an ordinary `[ProtoContract]` — compiled and round-tripped cleanly. The `[UnsafeAccessor]`-mistargeting bug found against the *surrogate* mechanism (see the parent spike's §6 amendment) does not reproduce here; different, simpler codegen path.

3. **Guid needs no cross-cutting `DataFormat` help.** Real `SequentialGuidSerializer`/`ResultSerializer<T>` source (Midgard's shipped code, not spike code) hardcodes `GuidWire.Read`/`Write` with an explicit `SerializerFeatures` declaration, completely independent of any model-level `DataFormat` sweep. A `Result<Guid>` hand-written serializer under this route does the same — no dependency on Proposal 2 below.

Net: `[DataMember(Order = 1)] public Result<Guid> Id { get; init; }` — the doctrine's own intended future shape (`the-two-unions.md`) — should work cleanly under this proposal, on every axis checked so far.

### 2.4 Open questions to resolve before/while coding the PR

- **Attribute name and shape** — `ProtoExternalSerializer` is a placeholder; check for naming conventions the maintainers would prefer (possible: extend `[ProtoSurrogate]` itself with an `IsScalar`/serializer-only mode rather than a wholly new attribute — smaller surface, but conflates two different mechanisms under one name; worth raising as an open question in the issue rather than presupposing the answer).
- **`IsScalar` inference at assembly scope** — the contract-level form has three fallback routes for determining scalar-vs-message framing (explicit `IsScalar` argument → `Features` expression-folding when the serializer is in-compilation → deferred-to-runtime `WriteAny`/`ReadAny`). Route 2 doesn't apply cleanly when the serializer type is out-of-assembly by definition (that's the whole point) — likely means route 1 (explicit `IsScalar`) becomes mandatory for the assembly-level form, or route 3 (runtime-deferred) becomes the fallback. Needs a design decision, not just an implementation detail.
- **Precedence/collision rules** — should mirror `[ProtoSurrogate]`'s existing least-to-most-specific resolution (referenced assemblies → this assembly → model) for consistency, but confirm this is actually desired for a *serializer* declaration (arguably a consumer overriding a library's serializer choice is a stranger thing to want than overriding a surrogate choice — worth asking upstream, not assuming).
- **Whether `[MustConsume]`-style illegal-write laws are a use case worth naming explicitly** in the PR description, or whether that's too Norse-specific to lead with (the generic framing — "let a hand-written serializer be declared for a type you don't own, the way surrogates already can" — probably lands better upstream than leading with a downstream union type's specific law).

---

## 3. Problem 2 — `DataFormat` has no cross-cutting default

### 3.1 What's missing

`CompatibilityLevel` has a real, documented resolution chain: member attribute → type attribute (inherited from base types) → module → assembly → `200` (global default). `[module: CompatibilityLevel(...)]` and `[assembly: CompatibilityLevel(...)]` are real, and the AOT generator honors them identically to the runtime model — confirmed working (see the parent spike's §6).

`DataFormat` has **no equivalent at any level above the individual member.** Verified against `protobuf-net.Core.xml` (3.3.8) directly — `[ProtoMember(DataFormat = ...)]` is the only place it's settable. There's no `[module: ...]`/`[assembly: ...]` form, and `[ProtoModel]` itself exposes exactly one cross-cutting knob (`AllowParseableTypes`) — nothing DataFormat-related.

### 3.2 Where this actually bites (scoped correctly, per §2.3 above)

**Not** `Result<Guid>`, `SequentialGuid`, or `DeterministicGuid` — all three already ride hand-written serializers (today at runtime, and under Proposal 1 at compile time) that hardcode their own wire format, bypassing any model-level `DataFormat` setting entirely.

It bites a genuinely **bare** `Guid`-typed member — `[ProtoMember(N)] public Guid CorrelationId { get; set; }` with no wrapper, no custom serializer — which goes through protobuf-net's own inbuilt Guid handling, whose `DataFormat` is per-member-configurable and defaults to something other than the platform's chosen `FixedSize` (16-byte RFC 9562 bytes). Today's `IdentifierSerializers.ApplyWireLaw` (`Midgard/src/Infrastructure.Web.Grpc/IdentifierSerializers.cs`) forces this platform-wide via `RuntimeTypeModel.AfterApplyDefaultBehaviour` — a runtime-only hook with no compile-time equivalent.

Practical impact if left unaddressed: every bare-`Guid` member on every AOT-seeded contract needs its own explicit `[ProtoMember(N, DataFormat = DataFormat.FixedSize)]` by hand — mechanical, easy to forget, exactly the kind of thing a platform-wide sweep exists to prevent in the first place.

### 3.3 The proposal

Mirror `CompatibilityLevel`'s exact precedent, scoped to a `Type` argument (unlike `CompatibilityLevel`, `DataFormat`'s legal values are type-dependent — `FixedSize` only means something for certain types — so the attribute needs to name the type it applies to, not apply universally):

```csharp
[module: ProtoDataFormat(typeof(Guid), DataFormat.FixedSize)]
// or, following the CompatibilityLevel precedent's own assembly-level form:
[assembly: ProtoDataFormat(typeof(Guid), DataFormat.FixedSize)]
```

Consulted by `ValueMember`/the generator's member-resolution step whenever a member's own `[ProtoMember]` doesn't set `DataFormat` explicitly (an explicit per-member value should still win — same override relationship `CompatibilityLevel` already has between member and higher scopes).

### 3.4 Open questions

- **Untested**: whether `[assembly: ProtoSurrogate(typeof(Guid), typeof(byte[]))]` could serve as a *workaround* today, ahead of a real `ProtoDataFormat` attribute existing — given §2.3/§1.2's finding that bare-scalar surrogate targets are refused (`PBN2002`), `byte[]` would likely need the same contract-wrapper treatment `NullableInt32` needed, which would add message-wrapping cost to *every* Guid field — probably worse than just accepting per-member attributes until the real fix lands. Worth a quick spike before assuming it's a dead end, but low priority relative to Proposal 1.
- **Scope of the first PR**: worth deciding whether to propose `ProtoDataFormat` generically (any type, any format) or narrowly ask for exactly what's needed (Guid → FixedSize) as a smaller, easier-to-review first cut, with the general mechanism as a stated but separate follow-up.

---

## 4. Suggested sequencing once the clone exists

1. Open an issue for Proposal 1 first — it's the one with real evidence already in hand (the spike's §6 amendment) and the clearer, narrower fix. Lead with the generic framing (§2.4's last point), evidence from §1.1–§1.3, and the confirmed-clean shape from §2.3.
2. Prototype the attribute + generator discovery-path change against a local protobuf-net checkout, re-running this platform's spike project (or a fresh minimal repro) against the patched build before writing a formal PR description.
3. Proposal 2 as a separate issue/PR — smaller, more mechanical, no dependency on Proposal 1 landing first.
4. Neither proposal is a green light to adopt the AOT model on this platform — that's still gated behind §2.1/§2.3 of the parent spike (does the *current* wire law survive AOT as-is; can the two models genuinely coexist), and behind the MAUI head actually existing, per the parent spike's own non-goals.

---

## 5. Non-goals

No Norse platform code changes here — this is entirely about the upstream library. No decision to adopt the AOT source-generator model on this platform (separate, still-open question). No commitment that either proposal will be accepted upstream as designed here — the open questions in §2.4/§3.4 are real unknowns, not formalities.
