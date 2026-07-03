# Svartalfheim — Identifiers Design (`Norse.Primitives.Identifiers`)

**Date:** 2026-07-03
**Status:** Design — approved, pending plan
**Realm:** Svartalfheim (`Norse.Primitives`)
**Prior art:** [buvinghausen/SequentialGuid](https://github.com/buvinghausen/SequentialGuid) — Buvy's own OSS library, mounted read-only in this workspace at `../../SequentialGuid`. Cited as reference for the generation algorithms and byte-order problem; not carried over wholesale. This realm is .NET 11 only, so none of the source's multi-targeting (`netstandard`, `NET6_0_OR_GREATER` conditionals, legacy fallback branches) applies — every API goes straight to modern `Span<byte>`/`stackalloc`/big-endian `Guid` construction.

## 1. Problem

Two recurring identifier-generation needs across the platform, both currently unaddressed in `Norse.Primitives`:

1. **Deterministic, content-derived identifiers** for reference/lookup-table rows, so a foreign key can be *computed in memory* from a namespace + natural key without round-tripping to the database to look it up.
2. **Time-ordered, insert-friendly identifiers** for transactional tables, generated at whichever boundary needs to mint the ID — including client-side boundaries (WASM/MAUI) — without fragmenting a clustered index on write.

Both motivating cases feed directly into the migrations framework's seeding effort (`../../plans/2026-06-28-migrations-framework-identity-schema.md` lineage): seed data for reference tables wants deterministic IDs; seed data for transactional tables wants time-ordered IDs generated in bulk.

A secondary, harder problem specific to (2): the platform is provider-aware (Postgres primary today, SQL Server a live open question — see Bifröst CLAUDE.md §8). Postgres's `uuid` type sorts by plain byte-order, so an RFC 9562 UUIDv7 already sorts correctly there. SQL Server's `uniqueidentifier` does **not** compare bytes in storage order — it uses a documented, peculiar per-byte-group significance ranking — so a UUIDv7 written to a SQL Server column unmodified fragments its clustered index on every insert. Something has to reorder bytes to match SQL Server's comparison semantics.

## 2. Scope

**In scope, this increment:**
- `SequentialGuid` — a single struct wrapping RFC 9562 UUIDv7 generation, carrying an explicit byte-order tag (`GuidByteOrder`) and bidirectional shuffle methods between RFC and SQL Server byte order.
- `DeterministicGuid` — a single struct wrapping RFC 9562 UUIDv5 (SHA-1, namespace + name) generation.
- `INorseGuid` — a minimal marker interface shared by both.
- Batch generation for `SequentialGuid` (`Fill`/`CreateMany`), since bulk seeding is the stated motivating use case.

**Explicitly out of scope:**
- EF Core value generators/converters, value comparers, or any EF wiring — a separate effort lands in Urdarbrunnr once this ships.
- UUIDv4, UUIDv8 (both the time-based and name-based custom variants from the prior art) — not ported.
- MongoDB and NodaTime integration packages from the prior art — not ported.
- Runtime byte-order *detection* (the prior art's `SequentialGuidByteOrder.TryDetect` and its heuristics for disambiguating standard-vs-SQL-vs-legacy layouts purely from bit patterns) — obsolete under this design. Byte order is always explicit and carried on the instance (`GuidByteOrder`), never inferred.

## 3. `INorseGuid` — marker interface

```csharp
public interface INorseGuid
{
    Guid Value { get; }
}
```

Deliberately minimal — no `Timestamp`, no byte-order member. `SequentialGuid` and `DeterministicGuid` solve different problems and look different; the marker interface exists only so generic code (e.g. a future EF value converter) can accept "either kind of guaranteed-well-formed Norse guid" without caring which, and without either type stubbing members that don't apply to it.

## 3.1 Public surface is deliberately narrow — the trust-boundary argument

Neither type gets `ToString`/`TryFormat` overrides, `Parse`/`TryParse`, implicit `string` conversion, or comparison operators (`<`, `<=`, `>`, `>=`). This isn't an oversight — it follows directly from where these types are and aren't allowed to sit relative to a trust boundary:

- **Untrusted external input** (an inbound HTTP request, an outbound `HttpClient` response body, a parsed file) always goes through the existing `Result<Guid>`/`GuidParser` gateway in `Norse.Primitives` — never through `SequentialGuid`/`DeterministicGuid` directly. A third party cannot be expected to hand back an RFC 9562-tagged, version-correct value, so parsing untrusted text into one of these types is not a supported path at all.
- **Boundaries this platform controls on both ends** — EF Core loading a column, an NServiceBus handler deserializing a message — exchange a plain BCL `Guid` (the storage/wire type) and construct the wrapping struct from it via the validating constructor. Since both producer and consumer are ours, a malformed value there means a bug, not adversarial input, so it's correct for the constructor to throw rather than return a `Result`.

That leaves exactly two operations these types need: **unwrap to `Guid`** (storage/wire) and **wrap from `Guid`** (loading back), both already covered by `Value` and the validating constructors. Everything else — formatting, parsing, operator sugar — is deferred until a concrete caller actually needs it.

**Surface, both types:**
- `Guid Value { get; }`
- Validating constructor(s) — throw on malformed version/variant bits
- Implicit operator to `Guid` (unwrap only — safe, never throws)
- **No** implicit operator *from* `Guid` — wrapping stays an explicit constructor call so a thrown validation exception is visible at the call site
- `IComparable<T>` / `IEquatable<T>` (`CompareTo`/`Equals`) — kept regardless of the trust-boundary argument above, since in-memory sorting and dictionary/EF-key comparisons need them
- Record struct's auto-generated `ToString()` is left as-is (fine for logs/debugging) — no override

## 4. `DeterministicGuid` — UUIDv5, deterministic lookup keys

```csharp
public readonly record struct DeterministicGuid :
    INorseGuid, IComparable<DeterministicGuid>, IEquatable<DeterministicGuid>
{
    public Guid Value { get; }

    public static class Namespaces
    {
        public static readonly Guid Dns;   // RFC 9562 well-known namespaces, ported for convenience
        public static readonly Guid Url;
        public static readonly Guid Oid;
        public static readonly Guid X500;
    }

    public DeterministicGuid(Guid namespaceId, string name);
    public DeterministicGuid(Guid namespaceId, ReadOnlySpan<char> name);
    public DeterministicGuid(Guid namespaceId, ReadOnlySpan<byte> name);

    // Wraps an already-computed value. Throws ArgumentException if the version/variant
    // bits don't identify a valid UUIDv5 — no silent normalization of malformed input.
    public DeterministicGuid(Guid value);
}
```

- Generation logic (SHA-1 over namespace + name, per RFC 9562 §5.5/§A.4) is folded directly into the constructors — no separate public static generator class. This type exists specifically so a caller can compute a lookup-table FK in memory; a bare `Guid`-returning free function would obscure that intent.
- No `Timestamp`, no `GuidByteOrder`. A content hash has no time component and no meaningful sort order — none of §6's SQL byte-order machinery applies to this type at all.
- The RFC well-known namespaces are ported as constants for convenience. The expected primary use is domain-specific: a bounded context defines its own namespace GUID per entity/lookup-table type and combines it with each row's natural key.
- The wrapping constructor validates version (5) and variant bits and throws on a malformed value — fail loudly, no silent fallback, matching platform convention.

## 5. `SequentialGuid` — UUIDv7, time-ordered transactional IDs

```csharp
public enum GuidByteOrder
{
    Unspecified = 0,   // sentinel CLR default — never a valid argument; guards against default(GuidByteOrder)
    Rfc9562 = 1,
    SqlServer = 2
}

public readonly record struct SequentialGuid :
    INorseGuid, IComparable<SequentialGuid>, IEquatable<SequentialGuid>
{
    public Guid Value { get; }
    public GuidByteOrder Order { get; }
    public DateTime Timestamp { get; }   // extracted according to Order

    // Generates a new value. Always Order = Rfc9562 — the "native" form.
    public SequentialGuid();

    // Wraps an existing value. Order must be stated explicitly: it is not detectable
    // from the bits by design (see §6 — the whole point is that version/variant bits
    // sit at identical offsets regardless of tag, so there is no bit pattern to sniff).
    // Throws ArgumentException if version/variant bits don't identify a valid UUIDv7.
    public SequentialGuid(Guid value, GuidByteOrder order);

    public SequentialGuid ToSqlOrder();   // no-op (returns this) if already SqlServer
    public SequentialGuid ToRfcOrder();   // no-op (returns this) if already Rfc9562

    public static void Fill(Span<SequentialGuid> destination);
    public static SequentialGuid[] CreateMany(int count);
}
```

**Enum values are always explicit** — every enum in this codebase states its numeric values explicitly, never relies on default ordinal assignment. A value inserted in the middle of an unnumbered enum silently renumbers everything after it, which is catastrophic for any enum EF has persisted as a raw integer. This is a standing platform convention as of this spec, not a one-off for `GuidByteOrder`. It follows the same `0 = Unspecified` sentinel pattern `ParseFailure` already established in this project (`src/Primitives/ParseFailure.cs`): the `SequentialGuid(Guid value, GuidByteOrder order)` constructor throws `ArgumentOutOfRangeException` if `order == GuidByteOrder.Unspecified`, catching the "forgot to pass a real value" mistake at the boundary instead of silently treating a defaulted argument as `Rfc9562`.

### 5.1 RFC 9562 layout (unchanged from the prior art), 16 bytes

```
[0..6)   unix_ts_ms       48 bits, big-endian
[6]      ver(4 bits) | ctrHi nibble(4 bits)
[7]      ctrHi low byte                        \_ rand_a = 12-bit counter chunk
[8]      var(2 bits) | ctrLo(6 bits)
[9]      ctrLo low byte                        \_ rand_b-start = 14-bit counter chunk
[10..16) entropy          48 bits, random
```

The monotonic counter mechanism is unchanged from the prior art's `GuidV7`: a process-global 26-bit counter, seeded randomly at startup, advanced via `Interlocked.Increment` (single) / `Interlocked.Add` (batch) per RFC 9562 §6.2 Method 1 (Fixed Bit-Length Dedicated Counter). Only the byte-order story below is new.

Generation always produces `Order = Rfc9562`. A `SqlServer`-ordered instance is always obtained via an explicit `.ToSqlOrder()` call after generation — mirrors the prior art's `NewGuid()`/`NewSqlGuid()` split, just expressed as a conversion method on the struct rather than a second static entry point.

### 5.2 SQL Server byte-order conversion — contract, not hand-derived arithmetic

The prior art's SQL byte-order transform operates on .NET's *native* `Guid` byte layout (which itself reverses `Data1`/`Data2`/`Data3` relative to the RFC big-endian representation) and fully permutes all 16 bytes, including the version/variant bit positions. This design deliberately does **less**: version and variant stay fixed, and only the timestamp/counter/entropy portions move.

**Contract for `ToSqlOrder()` / `ToRfcOrder()`:**

1. The version nibble (RFC byte 6, top nibble) and variant bits (RFC byte 8, top 2 bits) occupy the **same native byte offsets** in both `Rfc9562`- and `SqlServer`-tagged instances — never moved, never reinterpreted. This is what makes "is this a well-formed Norse `SequentialGuid`" checkable without knowing the tag in advance, even though the tag itself still isn't *derivable* from the bits (see below).
2. The 48-bit timestamp occupies whichever native byte positions SQL Server's comparison algorithm ranks most significant, written in correct MSB→LSB order.
3. The 26-bit counter is re-split (14-bit chunk / 12-bit chunk — mirrored from the RFC order's 12/14 split) so its more-significant half lands in whichever of the two counter byte-groups SQL Server's comparison ranks higher. This is the full-correctness option: without it, intra-millisecond ordering under SQL Server's comparison could invert at counter carry boundaries.
4. Entropy fills whatever native byte positions remain. No ordering constraint applies — it's random, so any bijective placement preserves its randomness.
5. The transform is bijective: `x.ToSqlOrder().ToRfcOrder()` reproduces `x` exactly, for every generated `x`.

Exact byte-index formulas are **not** asserted here — deriving them by hand risks a silent off-by-one, and this realm's process is test-driven regardless. They are worked out and pinned by tests during implementation, per `superpowers:test-driven-development`.

**Correctness oracle (written test-first):**
- **Round-trip property test:** generate N instances, assert `ToSqlOrder().ToRfcOrder()` reproduces the original bytes exactly.
- **Monotonic-sort test — the one that proves the point of this whole design:** generate a batch in sequence (including across at least one counter carry boundary), convert every element to `SqlServer` order, sort using `System.Data.SqlTypes.SqlGuid` comparison (the same algorithm SQL Server itself applies), and assert the result matches generation order.
- **Fixed-offset test:** confirm version/variant bit positions are byte-identical between an `Rfc9562` instance and its `SqlServer`-tagged counterpart.

### 5.3 Equality & comparison semantics

- **`Equals`** normalizes both sides to canonical `Rfc9562` bytes before comparing. Two `SequentialGuid` instances representing the same logical value compare equal regardless of which `Order` each currently carries — safe for dictionary/lookup use, and consistent regardless of which database a value happened to come from.
- **`CompareTo`** normalizes the *other* instance to match *this* instance's `Order` (via `ToSqlOrder()`/`ToRfcOrder()` as needed), then compares using that order's native semantics: plain `Guid` comparison for `Rfc9562`, `System.Data.SqlTypes.SqlGuid` comparison for `SqlServer`. This guarantees `CompareTo(other) == 0` iff `Equals(other)`, and a `SqlServer`-tagged instance always sorts the way SQL Server itself would sort it.

### 5.4 Batch generation

```csharp
public static void Fill(Span<SequentialGuid> destination);
public static SequentialGuid[] CreateMany(int count);
```

Both generate in `Rfc9562` order — matching the single-instance default — sharing one timestamp capture and a contiguous counter-slot block across the batch, mirroring the prior art's `GuidV7.Fill`/`NewGuids` mechanism. There is no dedicated batch-to-`SqlServer`-order API: converting a batch is `foreach` + `.ToSqlOrder()`. A bulk conversion path would be premature — the per-item conversion is a handful of shift/mask operations, not worth a second API surface until profiling says otherwise.

## 6. File layout

New sub-namespace `Norse.Primitives.Identifiers`, physically grouped under `src/Primitives/Identifiers/`:

- `INorseGuid.cs`
- `DeterministicGuid.cs`
- `SequentialGuid.cs`
- `GuidByteOrder.cs`
- An internal helper file for the shared byte-level math (block placement, counter repack, version/variant bit setters) used by both generation and the shuffle methods — exact name TBD at plan time, not a public surface.

Physical subfolder **and** distinct sub-namespace, signaling this is a separable concern (identifier *generation*) from the existing parse-gateway concern (`Result<T>`, `Parser`, and the scalar parsers including the pre-existing `GuidParser`, which parses *untrusted external text* into a `Guid` — a different problem from minting a well-formed identifier).

## 7. Testing

xUnit v3 + Shouldly on Microsoft.Testing.Platform, per this realm's existing convention (`tests/Primitives.Tests`). Test naming `Should_{behavior}_when_{condition}`, test classes `public sealed`, methods omit access modifiers, per house style. The prior art's test suite (`GuidV7Tests`, `GuidV7BulkTests`, `GuidV5Tests`, `SequentialGuidStructTests`, `SequentialSqlGuidStructTests`) is reference material for scenario coverage, not a file to port verbatim — the struct shape here differs enough (single tagged type vs. two separate types, folded generation, no runtime detection) that tests are written fresh against this design.

Required coverage beyond the correctness oracle in §5.2:
- `DeterministicGuid`: same namespace + name always produces the same value; different namespace or name produces a different value; malformed-value constructor guard throws.
- `SequentialGuid`: malformed-value constructor guard throws for both `Order` values; `Timestamp` extraction is correct for both `Order` values; batch generation shares one timestamp capture and produces a contiguous, gap-free counter sequence.
- AOT smoke (`tests/smoke/Primitives.Aot.Smoke`) must still publish and run clean — no reflection introduced by either type.

## 8. Open questions for the plan

None outstanding — every design fork raised during brainstorming was resolved above. The implementation plan's first task should derive and pin the exact byte-index formulas for §5.2 via the correctness-oracle tests before any other code depends on them.
